#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine.Networking;
//using Unity.EditorCoroutines.Editor;

[CustomEditor(typeof(UltiPaw))]
public class UltiPawEditor : Editor
{
    private Texture2D bannerTexture;

    // Version management fields
    private List<UltiPawVersionManager.UltiPawVersion> serverVersions = new List<UltiPawVersionManager.UltiPawVersion>();
    private string recommendedVersion = "";
    private bool isFetching = false;
    private string fetchError = "";
    private UltiPawVersionManager.UltiPawVersion selectedVersion = null;

    // Local hashes
    private string localBaseFbxHash = "";
    private string localBinHash = "";

    // Server settings
    private const string serverBaseUrl = "http://192.168.1.180:8080"; // Update with your server URL
    private const string versionsEndpoint = "/ultipaw/getVersions";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        UltiPaw ultiPaw = (UltiPaw)target;

        // --- Banner ---
        if (bannerTexture == null)
        {
            bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(UltiPawUtils.BASE_FOLDER + "/banner.png");
        }
        if (bannerTexture != null)
        {
            float aspect = (float)bannerTexture.width / bannerTexture.height;
            float desiredWidth = EditorGUIUtility.currentViewWidth - 40;
            float desiredHeight = desiredWidth / aspect;
            Rect rect = GUILayoutUtility.GetRect(desiredWidth, desiredHeight, GUILayout.ExpandWidth(true)); // Use ExpandWidth true
            GUI.DrawTexture(rect, bannerTexture, ScaleMode.ScaleToFit);
            GUILayout.Space(5);
        }

        // --- File Configuration ---
        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("specifyCustomBaseFbx"), new GUIContent("Specify Base FBX Manually"));
        if (!ultiPaw.specifyCustomBaseFbx && ultiPaw.baseFbxFiles.Count > 0 && ultiPaw.baseFbxFiles[0] != null)
        {
            GUI.enabled = false;
            EditorGUILayout.ObjectField("Using Base FBX", ultiPaw.baseFbxFiles[0], typeof(GameObject), false);
            GUI.enabled = true;
        }
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("baseFbxFiles"), new GUIContent("Base FBX File(s)"), true);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // --- Version Management Section ---
        EditorGUILayout.LabelField("UltiPaw Version Manager", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Display local base FBX hash.
        if (ultiPaw.baseFbxFiles.Count > 0 && ultiPaw.baseFbxFiles[0] != null)
        {
            string baseFbxPath = AssetDatabase.GetAssetPath(ultiPaw.baseFbxFiles[0]);
            localBaseFbxHash = UltiPawUtils.CalculateFileHash(baseFbxPath) ?? "";
            EditorGUILayout.LabelField("Base FBX Hash:", localBaseFbxHash);
        }
        else
        {
            EditorGUILayout.HelpBox("No Base FBX file assigned.", MessageType.Warning);
        }

        // Fetch Versions button.
        GUI.enabled = !isFetching;
        if (GUILayout.Button(isFetching ? "Fetching Versions..." : "Fetch Available Versions"))
        {
            fetchError = "";
            serverVersions.Clear();
            selectedVersion = null;
            EditorCoroutineUtility.StartCoroutineOwnerless(FetchVersions(ultiPaw));
        }
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(fetchError))
        {
            EditorGUILayout.HelpBox("Error: " + fetchError, MessageType.Error);
        }
        else if (serverVersions.Count > 0)
        {
            EditorGUILayout.LabelField("Recommended Version: " + recommendedVersion);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Available Versions:", EditorStyles.boldLabel);
            foreach (var ver in serverVersions)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label($"UltiPaw {ver.version}", GUILayout.Width(100));
                GUILayout.Label($"[{ver.scope}]", GUILayout.Width(60));
                if (selectedVersion == ver)
                {
                    GUI.enabled = false;
                    GUILayout.Button("Selected", GUILayout.Width(80));
                    GUI.enabled = true;
                }
                else
                {
                    if (GUILayout.Button("Select", GUILayout.Width(80)))
                    {
                        selectedVersion = ver;
                        // Update the UltiPaw component with version info.
                        string binPath = UltiPawUtils.GetVersionBinPath(ver.version, localBaseFbxHash);
                        ultiPaw.selectedUltiPawBinPath = binPath;
                        ultiPaw.activeUltiPawVersion = ver;
                        // Update blendshapes from the version's customBlendshapes.
                        if (ver.customBlendshapes != null && ver.customBlendshapes.Length > 0)
                        {
                            ultiPaw.blendShapeNames = new List<string>(ver.customBlendshapes);
                        }
                        // Ensure blendshape slider list matches.
                        while (ultiPaw.blendShapeValues.Count < ultiPaw.blendShapeNames.Count)
                            ultiPaw.blendShapeValues.Add(0f);
                        while (ultiPaw.blendShapeValues.Count > ultiPaw.blendShapeNames.Count)
                            ultiPaw.blendShapeValues.RemoveAt(ultiPaw.blendShapeValues.Count - 1);
                        Debug.Log($"[UltiPawVersionManager] Selected version: {ver.version}");
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // --- Action Buttons ---
        bool canTransform = ultiPaw.baseFbxFiles.Count > 0 && ultiPaw.baseFbxFiles[0] != null &&
                            !string.IsNullOrEmpty(ultiPaw.selectedUltiPawBinPath) &&
                            File.Exists(ultiPaw.selectedUltiPawBinPath) &&
                            !ultiPaw.isUltiPaw;
        GUI.enabled = canTransform;
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Turn into UltiPaw", GUILayout.Height(40)))
        {
            if (selectedVersion == null)
            {
                EditorUtility.DisplayDialog("No Version Selected", "Please select a version from the list before transforming.", "OK");
            }
            else
            {
                ultiPaw.TurnItIntoUltiPaw();
            }
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        bool hasRestore = AnyBackupExists(ultiPaw.baseFbxFiles) && ultiPaw.isUltiPaw;
        GUI.enabled = hasRestore;
        if (GUILayout.Button("Reset to Original FBX"))
        {
            if (EditorUtility.DisplayDialog("Confirm Reset", "This will restore the original FBX (from its '.old' backup) and reapply the default avatar configuration.", "Reset", "Cancel"))
            {
                ultiPaw.ResetIntoWinterPaw();
            }
        }
        GUI.enabled = true;

        // --- Blendshape Sliders ---
        if (ultiPaw.isUltiPaw && ultiPaw.blendShapeNames != null && ultiPaw.blendShapeNames.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Blendshapes", EditorStyles.boldLabel);
            int columns = 3;
            for (int i = 0; i < ultiPaw.blendShapeNames.Count; i++)
            {
                if (i % columns == 0) EditorGUILayout.BeginHorizontal();
                float newValue = EditorGUILayout.Slider(ultiPaw.blendShapeNames[i], ultiPaw.blendShapeValues[i], 0f, 100f, GUILayout.Width(150));
                if (!Mathf.Approximately(newValue, ultiPaw.blendShapeValues[i]))
                {
                    ultiPaw.blendShapeValues[i] = newValue;
                    ultiPaw.ToggleBlendShape(ultiPaw.blendShapeNames[i], newValue);
                }
                if (i % columns == columns - 1) EditorGUILayout.EndHorizontal();
            }
            if (ultiPaw.blendShapeNames.Count % columns != 0)
                EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // --- Help Section ---
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "If the 'Reset to Original FBX' button is disabled while your project is not back to its default state, please reimport the original WinterPaw avatar package.",
            MessageType.Info
        );

        serializedObject.ApplyModifiedProperties();
        if (GUI.changed)
            EditorUtility.SetDirty(ultiPaw);
    }

    private IEnumerator FetchVersions(UltiPaw ultiPaw)
    {
        isFetching = true;
        Repaint();

        string defaultFbxPath = ultiPaw.GetDetectedBaseFbxPath();
        if (string.IsNullOrEmpty(defaultFbxPath) || !File.Exists(defaultFbxPath))
        {
            fetchError = "Default base FBX file not found.";
            isFetching = false;
            Repaint();
            yield break;
        }
        localBaseFbxHash = UltiPawUtils.CalculateFileHash(defaultFbxPath);
        if (string.IsNullOrEmpty(localBaseFbxHash))
        {
            fetchError = "Failed to compute hash for base FBX file.";
            isFetching = false;
            Repaint();
            yield break;
        }
        // Clear previous versions each time.
        serverVersions.Clear();
        selectedVersion = null;

        string url = $"{serverBaseUrl}{versionsEndpoint}?s={UltiPaw.SCRIPT_VERSION}&d={localBaseFbxHash}";
        Debug.Log($"[UltiPawVersionManager] Fetching versions from: {url}");

        UnityWebRequest req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
        {
            if (req.responseCode == 406)
            {
                fetchError = req.downloadHandler.text;
                Debug.LogError($"[UltiPawVersionManager] Server Error: {req.downloadHandler.text}");
            }
            else
            {
                fetchError = $"Error {req.responseCode}: {req.error}";
                Debug.LogError($"[UltiPawVersionManager] Fetch Error: {req.error}");
            }
        }
        else
        {
            try
            {
                string json = req.downloadHandler.text;
                Debug.Log($"[UltiPawVersionManager] Received JSON: {json}");
                UltiPawVersionManager.UltiPawVersionResponse response = JsonUtility.FromJson<UltiPawVersionManager.UltiPawVersionResponse>(json);
                if (response != null && response.versions != null)
                {
                    serverVersions = response.versions;
                    recommendedVersion = response.recommendedVersion;
                    fetchError = "";
                }
                else
                {
                    fetchError = "Failed to parse server response.";
                    Debug.LogError("[UltiPawVersionManager] JSON parsing error.");
                }
            }
            catch (System.Exception e)
            {
                fetchError = "Exception while parsing server response.";
                Debug.LogError($"[UltiPawVersionManager] Exception: {e}");
            }
        }

        isFetching = false;
        Repaint();
    }

    private bool AnyBackupExists(List<GameObject> files)
    {
        if (files == null) return false;
        return files.Any(file => file != null && File.Exists(AssetDatabase.GetAssetPath(file) + ".old"));
    }
}
#endif
