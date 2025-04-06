#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Added for Linq

public class UltiPawVersionManager : EditorWindow
{
    private const string serverBaseUrl = "http://192.168.1.180:8080"; // Replace with your actual server URL

    // Store fetched versions and selection
    private static List<UltiPawVersion> availableVersions = new List<UltiPawVersion>();
    private static string recommendedVersion;
    private static string fetchError = null;
    private static bool isFetching = false;

    // Store the currently selected version globally so UltiPaw can access it
    public static UltiPawVersion SelectedVersion { get; private set; }
    public static string SelectedVersionBinPath { get; private set; } // Path to the downloaded .bin

    // Store the hash of the default FBX used for fetching
    private static string lastFetchedFbxHash = null;

    // Reference to the UltiPaw component in the scene (optional, but can be useful)
    private static UltiPaw ultiPawInstance;

    [MenuItem("UltiPaw/Version Manager")]
    public static void ShowWindow()
    {
        GetWindow<UltiPawVersionManager>("UltiPaw Versions");
        // Try to find the UltiPaw component when window opens
        ultiPawInstance = FindObjectOfType<UltiPaw>();
    }

    private void OnGUI()
    {
        if (ultiPawInstance == null)
        {
            EditorGUILayout.HelpBox("No UltiPaw component found in the scene. Please add one to a GameObject.", MessageType.Warning);
            if (GUILayout.Button("Refresh Scene Scan"))
            {
                 ultiPawInstance = FindObjectOfType<UltiPaw>();
            }
            return; // Don't proceed without an instance
        }

        EditorGUILayout.LabelField("UltiPaw Script Version:", UltiPawUtils.SCRIPT_VERSION);
        EditorGUILayout.Space();

        // --- Fetching Section ---
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Fetch Versions from Server", EditorStyles.boldLabel);

        string defaultFbxPath = ultiPawInstance.GetDefaultFbxPath(); // Get path from UltiPaw instance
        if (string.IsNullOrEmpty(defaultFbxPath) || !File.Exists(defaultFbxPath))
        {
            EditorGUILayout.HelpBox("Default WinterPaw FBX path is not set or file not found in UltiPaw component. Cannot fetch versions.", MessageType.Error);
        }
        else
        {
            EditorGUILayout.LabelField("Using Base FBX:", defaultFbxPath);
             GUI.enabled = !isFetching;
             if (GUILayout.Button(isFetching ? "Fetching..." : "Fetch Available Versions"))
             {
                 fetchError = null; // Clear previous errors
                 availableVersions.Clear(); // Clear previous versions
                 SelectedVersion = null; // Clear selection
                 SelectedVersionBinPath = null;
                 EditorCoroutineUtility.StartCoroutineOwnerless(FetchVersions(defaultFbxPath));
             }
             GUI.enabled = true;
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // --- Display Errors or Versions ---
        if (fetchError != null)
        {
            EditorGUILayout.HelpBox($"Error fetching versions: {fetchError}", MessageType.Error);
        }
        else if (availableVersions.Count > 0)
        {
            EditorGUILayout.LabelField($"Recommended Version: {recommendedVersion}");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Available Versions:", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            foreach (var version in availableVersions)
            {
                EditorGUILayout.BeginHorizontal();
                // Display Version Info
                string label = $"UltiPaw {version.version}";
                GUILayout.Label(label, GUILayout.Width(100));
                GUILayout.Label($"[{version.scope}]", GUILayout.Width(60));

                // Display Status/Action Button
                string versionBinPath = UltiPawUtils.GetVersionBinPath(version.version, lastFetchedFbxHash);
                bool isDownloaded = File.Exists(versionBinPath);
                bool isSelected = SelectedVersion == version;

                Color defaultColor = GUI.backgroundColor;

                if (isSelected)
                {
                    GUI.backgroundColor = Color.cyan;
                    GUILayout.Button("Selected", GUILayout.Width(80));
                    GUI.backgroundColor = defaultColor;
                }
                else if (isDownloaded)
                {
                     GUI.backgroundColor = Color.green;
                     if (GUILayout.Button("Select", GUILayout.Width(80)))
                     {
                         SelectVersion(version, versionBinPath);
                     }
                     GUI.backgroundColor = defaultColor;
                }
                else
                {
                    if (GUILayout.Button("Download", GUILayout.Width(80)))
                    {
                        EditorCoroutineUtility.StartCoroutineOwnerless(DownloadVersion(version, lastFetchedFbxHash));
                    }
                }

                EditorGUILayout.EndHorizontal();
                // Simple separator
                Rect rect = EditorGUILayout.GetControlRect(false, 1);
                EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
            }
            EditorGUILayout.EndVertical();
        }
        else if (!isFetching)
        {
             EditorGUILayout.HelpBox("No versions fetched yet, or none available for your base model.", MessageType.Info);
        }
    }

    private void SelectVersion(UltiPawVersion version, string binPath)
    {
        SelectedVersion = version;
        SelectedVersionBinPath = binPath;
        Debug.Log($"[UltiPawVersionManager] Selected Version: {version.version}, Path: {binPath}");

        // Optionally update the UltiPaw component's file C reference immediately
        if (ultiPawInstance != null)
        {
            ultiPawInstance.SetSelectedUltiPawBin(binPath);
        }
        Repaint(); // Update UI to show selection
    }

    private IEnumerator FetchVersions(string fbxPath)
    {
        isFetching = true;
        Repaint();

        string fbxHash = UltiPawUtils.CalculateFileHash(fbxPath);
        if (string.IsNullOrEmpty(fbxHash))
        {
            fetchError = "Could not calculate hash for the base FBX file.";
            isFetching = false;
            Repaint();
            yield break;
        }
        lastFetchedFbxHash = fbxHash; // Store the hash used for this fetch

        string url = $"{serverBaseUrl}/ultipaw/getVersions?s={UltiPawUtils.SCRIPT_VERSION}&d={fbxHash}";
        Debug.Log($"[UltiPawVersionManager] Fetching versions from: {url}");

        UnityWebRequest req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
        {
            if (req.responseCode == 406)
            {
                // Handle specific 406 errors
                if (req.downloadHandler.text.StartsWith("406001"))
                {
                    fetchError = "Server response: Your UltiPaw script is too old. Please update.";
                    Debug.LogError("[UltiPawVersionManager] Server Error: " + req.downloadHandler.text);
                }
                else if (req.downloadHandler.text.StartsWith("406002"))
                {
                    fetchError = "Server response: No matching UltiPaw versions found for your base model hash.";
                    Debug.LogWarning("[UltiPawVersionManager] Server Info: " + req.downloadHandler.text);
                }
                else
                {
                    fetchError = $"Server returned HTTP {req.responseCode}: {req.downloadHandler.text}";
                    Debug.LogError($"[UltiPawVersionManager] Fetch Error {req.responseCode}: {req.error} - {req.downloadHandler.text}");
                }
            }
            else
            {
                fetchError = $"Failed to fetch versions: {req.error} (HTTP {req.responseCode})";
                Debug.LogError($"[UltiPawVersionManager] Fetch Error: {req.error} (HTTP {req.responseCode})");
            }
        }
        else if (req.result == UnityWebRequest.Result.Success)
        {
            try
            {
                string jsonText = req.downloadHandler.text;
                // Debug.Log("Received JSON: " + jsonText); // Uncomment for debugging JSON structure
                UltiPawVersionResponse response = JsonUtility.FromJson<UltiPawVersionResponse>(jsonText);

                if (response == null || response.versions == null)
                {
                     fetchError = "Failed to parse server response.";
                     Debug.LogError("[UltiPawVersionManager] Failed to parse JSON response. Is the structure correct?");
                }
                else
                {
                    availableVersions = response.versions ?? new List<UltiPawVersion>(); // Ensure list is not null
                    recommendedVersion = response.recommendedVersion;
                    fetchError = null; // Clear error on success
                    Debug.Log($"[UltiPawVersionManager] Successfully fetched {availableVersions.Count} versions. Recommended: {recommendedVersion}");
                }
            }
            catch (System.Exception e)
            {
                fetchError = $"Error parsing server response: {e.Message}";
                Debug.LogError($"[UltiPawVersionManager] JSON Parsing Exception: {e}");
            }
        }

        isFetching = false;
        Repaint(); // Update UI after fetch completes or fails
    }

    private IEnumerator DownloadVersion(UltiPawVersion version, string baseFbxHash)
    {
        string downloadUrl = $"{serverBaseUrl}/ultipaw/getModel?version={UnityWebRequest.EscapeURL(version.version)}&d={baseFbxHash}"; // Assuming server needs hash for download too
        string targetPath = UltiPawUtils.GetVersionBinPath(version.version, baseFbxHash);

        Debug.Log($"[UltiPawVersionManager] Starting download for version {version.version}...");
        Debug.Log($"[UltiPawVersionManager] Download URL: {downloadUrl}");
        Debug.Log($"[UltiPawVersionManager] Target Path: {targetPath}");

        UltiPawUtils.EnsureDirectoryExists(targetPath); // Make sure the directory exists

        UnityWebRequest req = UnityWebRequest.Get(downloadUrl);
        req.downloadHandler = new DownloadHandlerFile(targetPath); // Download directly to file

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[UltiPawVersionManager] Failed to download version {version.version}: {req.error} (HTTP {req.responseCode})");
            // Optionally delete partially downloaded file if it exists
            if (File.Exists(targetPath)) File.Delete(targetPath);
            EditorUtility.DisplayDialog("Download Failed", $"Failed to download UltiPaw version {version.version}.\nError: {req.error}", "OK");
        }
        else
        {
            Debug.Log($"[UltiPawVersionManager] Successfully downloaded version {version.version} to: {targetPath}");
            AssetDatabase.Refresh(); // Important: Make Unity aware of the new file
            // Automatically select the downloaded version
            SelectVersion(version, targetPath);
        }
        Repaint(); // Update UI to show downloaded status
    }

    // --- JSON Data Structures ---
    [System.Serializable]
    public class UltiPawVersionResponse
    {
        public string recommendedVersion;
        public List<UltiPawVersion> versions;
    }

    [System.Serializable]
    public class UltiPawVersion
    {
        public string version;
        public string scope;
        public string date;
        public string changelog;
        public string customAviHash; // Hash of the expected ultipaw.bin
        public string defaultAviHash; // Hash of the expected base WinterPaw FBX
        public Dictionary<string, string> dependencies; // Keep this if needed, but not used in current logic
    }
}
#endif