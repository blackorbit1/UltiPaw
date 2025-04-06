#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Networking;
using System.IO.Compression; // Needed for ZipFile

[CustomEditor(typeof(UltiPaw))]
public class UltiPawEditor : Editor
{
    private Texture2D bannerTexture;
    private SerializedProperty baseFbxFilesProp;
    private SerializedProperty specifyCustomBaseFbxProp;
    private SerializedProperty blendShapeNamesProp; // Needed if we modify list directly
    private SerializedProperty blendShapeValuesProp;

    // Version management state (kept within the editor script)
    private List<UltiPawVersion> serverVersions = new List<UltiPawVersion>();
    private string recommendedVersionGuid = ""; // Store version string, not index
    private UltiPawVersion selectedVersion = null; // The one actively chosen by user or recommended
    private UltiPawVersion recommendedVersionDetails = null; // Details of the recommended version

    private bool isFetching = false;
    private bool isDownloading = false;
    private string fetchError = "";
    private string downloadError = "";
    private string lastFetchedHash = null; // Track hash used for last successful fetch
    private bool versionsFoldout = false; // State for the available versions foldout

    // Server settings
    private const string serverBaseUrl = "http://192.168.1.180:8080"; // Update with your server URL
    private const string versionsEndpoint = "/ultipaw/getVersions";
    private const string modelEndpoint = "/ultipaw/getModel"; // Assuming endpoint for zip download

    private void OnEnable()
    {
        // Load banner
        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(UltiPawUtils.BASE_FOLDER + "/banner.png");

        // Get SerializedProperties
        baseFbxFilesProp = serializedObject.FindProperty("baseFbxFiles");
        specifyCustomBaseFbxProp = serializedObject.FindProperty("specifyCustomBaseFbx");
        blendShapeNamesProp = serializedObject.FindProperty("blendShapeNames");
        blendShapeValuesProp = serializedObject.FindProperty("blendShapeValues");

        // Initial fetch when inspector becomes visible or script reloads
        UltiPaw ultiPaw = (UltiPaw)target;
        if (ultiPaw.UpdateCurrentBaseFbxHash() || lastFetchedHash == null) // Fetch if hash changed or never fetched
        {
            StartVersionFetch(ultiPaw);
        }
        else
        {
            // Ensure selectedVersion reflects the active one on enable
            selectedVersion = ultiPaw.activeUltiPawVersion;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update(); // Read latest values from UltiPaw component
        UltiPaw ultiPaw = (UltiPaw)target;

        // --- Banner ---
        DrawBanner(); // Encapsulated banner drawing

        // --- File Configuration ---
        DrawFileConfiguration(ultiPaw); // Encapsulated file config section

        // --- Version Management Section ---
        DrawVersionManagement(ultiPaw); // Encapsulated version management UI

        // --- Action Buttons ---
        DrawActionButtons(ultiPaw); // Encapsulated action buttons

        // --- Blendshape Sliders ---
        DrawBlendshapeSliders(ultiPaw); // Encapsulated blendshape sliders

        // --- Help Section ---
        DrawHelpBox(); // Encapsulated help box

        // Apply any changes made through SerializedProperties
        if (serializedObject.ApplyModifiedProperties())
        {
            // If properties changed, might need to trigger updates
            // e.g., if baseFbxFiles changed, re-calculate hash and fetch
             if (ultiPaw.UpdateCurrentBaseFbxHash()) {
                 StartVersionFetch(ultiPaw);
             }
        }

        // Note: Avoid calling SetDirty frequently here to prevent lag.
        // It's called within specific actions like SelectVersion, TurnItIntoUltiPaw etc.
    }

    // --- UI Drawing Helper Methods ---

    private void DrawBanner()
    {
        if (bannerTexture != null)
        {
            float aspect = (float)bannerTexture.width / bannerTexture.height;
            float desiredWidth = EditorGUIUtility.currentViewWidth - 40;
            float desiredHeight = desiredWidth / aspect;
            Rect rect = GUILayoutUtility.GetRect(desiredWidth, desiredHeight, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, bannerTexture, ScaleMode.ScaleToFit);
            GUILayout.Space(5);
        }
    }

    private void DrawFileConfiguration(UltiPaw ultiPaw)
    {
        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(specifyCustomBaseFbxProp, new GUIContent("Specify Base FBX Manually"));
        bool fbxSpecChanged = EditorGUI.EndChangeCheck();

        if (specifyCustomBaseFbxProp.boolValue)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(baseFbxFilesProp, new GUIContent("Base FBX File(s)"), true);
            if (EditorGUI.EndChangeCheck() || fbxSpecChanged)
            {
                serializedObject.ApplyModifiedProperties(); // Apply immediately
                if (ultiPaw.UpdateCurrentBaseFbxHash()) StartVersionFetch(ultiPaw); // Fetch if hash changed
            }
        }
        else // Auto-detect mode
        {
            if (fbxSpecChanged) // Just switched to auto-detect
            {
                serializedObject.ApplyModifiedProperties(); // Apply the toggle change
                ultiPaw.AutoDetectBaseFbxViaHierarchy(); // Trigger detection
                if (ultiPaw.UpdateCurrentBaseFbxHash()) StartVersionFetch(ultiPaw); // Fetch if detection changed hash
            }

            GUI.enabled = false;
            EditorGUILayout.PropertyField(baseFbxFilesProp, new GUIContent("Detected Base FBX"), true);
            GUI.enabled = true;
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    private void DrawVersionManagement(UltiPaw ultiPaw)
    {
        EditorGUILayout.LabelField("UltiPaw Version Manager", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Update/Fetch Button
        GUI.enabled = !isFetching && !isDownloading;
        if (GUILayout.Button(isFetching ? "Fetching..." : "Check for Updates"))
        {
            StartVersionFetch(ultiPaw);
        }
        GUI.enabled = true;

        // Display Fetching Errors
        if (!string.IsNullOrEmpty(fetchError))
        {
            EditorGUILayout.HelpBox("Error fetching versions: " + fetchError, MessageType.Error);
        }
        // Display Download Errors
        else if (!string.IsNullOrEmpty(downloadError))
        {
             EditorGUILayout.HelpBox("Error downloading version: " + downloadError, MessageType.Error);
        }
        // Display Version Info if fetch was successful
        else if (lastFetchedHash != null) // Indicates a successful fetch happened at some point
        {
            // Display Recommended / Currently Selected Version
            UltiPawVersion versionToShow = selectedVersion ?? recommendedVersionDetails; // Show selected, fallback to recommended

            if (versionToShow != null)
            {
                EditorGUILayout.LabelField("Selected Version:", EditorStyles.boldLabel);
                DrawVersionDetails(versionToShow, ultiPaw, true); // Draw details prominently
            }
            else if (serverVersions.Count > 0)
            {
                 EditorGUILayout.HelpBox("No version selected. Recommended version details could not be determined.", MessageType.Warning);
            }
            else if (!isFetching)
            {
                 EditorGUILayout.HelpBox("No compatible versions found for the detected Base FBX hash.", MessageType.Warning);
            }


            // Foldout for other available versions
            if (serverVersions.Count > 1 || (serverVersions.Count == 1 && serverVersions[0] != versionToShow))
            {
                versionsFoldout = EditorGUILayout.Foldout(versionsFoldout, "Available Versions", true);
                if (versionsFoldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var ver in serverVersions)
                    {
                        // Skip drawing the one already displayed prominently above if it's the only one
                        if (ver == versionToShow && serverVersions.Count == 1) continue;
                        // Skip drawing the prominently displayed one if it's different from selected
                        if (ver == versionToShow && selectedVersion != recommendedVersionDetails) continue;


                        DrawVersionListItem(ver, ultiPaw);
                    }
                     EditorGUI.indentLevel--;
                }
            }
        }
        else if (!isFetching)
        {
            EditorGUILayout.HelpBox("Click 'Check for Updates' to fetch compatible versions.", MessageType.Info);
        }


        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    // Helper to draw details of a specific version (used for recommended/selected)
    private void DrawVersionDetails(UltiPawVersion ver, UltiPaw ultiPaw, bool showDownloadButton)
    {
         EditorGUILayout.BeginHorizontal();
         GUILayout.Label($"UltiPaw {ver.version}", EditorStyles.boldLabel, GUILayout.Width(100));
         DrawScopeLabel(ver.scope);
         GUILayout.FlexibleSpace(); // Push download button to the right

         // Check if downloaded
         string expectedBinPath = UltiPawUtils.GetVersionBinPath(ver.version, lastFetchedHash); // Use last fetched hash for path consistency
         bool isDownloaded = File.Exists(expectedBinPath);

         if (showDownloadButton)
         {
             GUI.enabled = !isDownloading && !isFetching;
             if (isDownloaded)
             {
                 // Optionally show a "Re-download" button or just indicate downloaded
                 Color oldCol = GUI.color;
                 GUI.color = Color.green;
                 GUILayout.Label("(Downloaded)", GUILayout.Width(100));
                 GUI.color = oldCol;
             }
             else
             {
                 if (GUILayout.Button(isDownloading ? "Downloading..." : "Download", GUILayout.Width(100)))
                 {
                     StartVersionDownload(ver, ultiPaw);
                 }
             }
             GUI.enabled = true;
         }

         EditorGUILayout.EndHorizontal();

         // Display Changelog
         if (!string.IsNullOrEmpty(ver.changelog))
         {
             EditorGUILayout.LabelField("Changelog:", EditorStyles.miniBoldLabel);
             EditorGUILayout.HelpBox(ver.changelog, MessageType.None);
         }
         GUILayout.Space(5);
    }


    // Helper to draw a single item in the "Available Versions" list
    private void DrawVersionListItem(UltiPawVersion ver, UltiPaw ultiPaw)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox); // Use helpBox for slight bordering
        GUILayout.Label($"UltiPaw {ver.version}", GUILayout.Width(100));
        DrawScopeLabel(ver.scope);
        GUILayout.FlexibleSpace(); // Pushes button to the right

        if (selectedVersion != null && selectedVersion.version == ver.version)
        {
            GUI.enabled = false;
            GUILayout.Button("Selected", GUILayout.Width(80));
            GUI.enabled = true;
        }
        else
        {
             // Check if downloaded before allowing selection
             string expectedBinPath = UltiPawUtils.GetVersionBinPath(ver.version, lastFetchedHash);
             bool isDownloaded = File.Exists(expectedBinPath);

             GUI.enabled = isDownloaded && !isFetching && !isDownloading; // Can only select if downloaded
             if (GUILayout.Button("Select", GUILayout.Width(80)))
             {
                 SelectVersion(ver, ultiPaw, expectedBinPath);
             }
             GUI.enabled = true;

             if (!isDownloaded) {
                 GUILayout.Label("(Not Downloaded)", EditorStyles.miniLabel, GUILayout.Width(100));
             }
        }
        EditorGUILayout.EndHorizontal();
    }

    // Helper to draw the scope label with color
    private void DrawScopeLabel(string scope)
    {
        Color originalColor = GUI.contentColor;
        string scopeLower = scope?.ToLower() ?? "unknown";

        switch (scopeLower)
        {
            case "public":
                GUI.contentColor = Color.green;
                break;
            case "beta":
                GUI.contentColor = new Color(1.0f, 0.7f, 0.0f); // Orange/Yellow
                break;
            case "alpha":
                GUI.contentColor = Color.red;
                break;
            default:
                GUI.contentColor = Color.gray; // Or keep original
                break;
        }

        GUILayout.Label($"[{scope ?? "N/A"}]", EditorStyles.boldLabel, GUILayout.Width(60));
        GUI.contentColor = originalColor; // Restore original color
    }


    private void DrawActionButtons(UltiPaw ultiPaw)
    {
        // --- Turn into UltiPaw Button ---
        bool canTransform = selectedVersion != null && // A version must be selected
                            !string.IsNullOrEmpty(ultiPaw.selectedUltiPawBinPath) &&
                            File.Exists(ultiPaw.selectedUltiPawBinPath) && // Bin file must exist
                            !ultiPaw.isUltiPaw; // Must not already be in UltiPaw state

        GUI.enabled = canTransform && !isFetching && !isDownloading;
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Turn into UltiPaw", GUILayout.Height(40)))
        {
            // Confirmation recommended
            if (EditorUtility.DisplayDialog("Confirm Transformation",
                $"This will modify your base FBX file using UltiPaw version '{selectedVersion?.version ?? "Unknown"}'.\nA backup (.old) will be created.",
                "Proceed", "Cancel"))
            {
                ultiPaw.TurnItIntoUltiPaw();
                // Force repaint to update blendshape section visibility
                Repaint();
            }
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        // --- Reset Button ---
        bool hasRestore = AnyBackupExists(ultiPaw.baseFbxFiles) && ultiPaw.isUltiPaw;
        GUI.enabled = hasRestore && !isFetching && !isDownloading;
        if (GUILayout.Button("Reset to Original FBX"))
        {
            if (EditorUtility.DisplayDialog("Confirm Reset", "This will restore the original FBX (from its '.old' backup, if found) and attempt to reapply the default avatar configuration.", "Reset", "Cancel"))
            {
                ultiPaw.ResetIntoWinterPaw();
                 // Force repaint to update blendshape section visibility
                Repaint();
            }
        }
        GUI.enabled = true;
    }

    private void DrawBlendshapeSliders(UltiPaw ultiPaw)
    {
        if (ultiPaw.isUltiPaw && ultiPaw.blendShapeNames != null && ultiPaw.blendShapeNames.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Blendshapes", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Use SerializedProperty for direct modification and Undo handling
            if (blendShapeValuesProp.arraySize != ultiPaw.blendShapeNames.Count)
            {
                 // Ensure property array size matches name list if changed externally
                 serializedObject.ApplyModifiedPropertiesWithoutUndo(); // Apply potential external changes first
                 blendShapeValuesProp.arraySize = ultiPaw.blendShapeNames.Count;
                 serializedObject.Update(); // Re-fetch after resize
            }


            int columns = Mathf.Max(1, (int)(EditorGUIUtility.currentViewWidth - 50) / 180); // Dynamic columns based on width
            for (int i = 0; i < ultiPaw.blendShapeNames.Count; i++)
            {
                if (i % columns == 0) EditorGUILayout.BeginHorizontal();

                // Get the specific element property
                SerializedProperty blendValProp = blendShapeValuesProp.GetArrayElementAtIndex(i);

                EditorGUI.BeginChangeCheck();
                // Use PropertyField for better layout and label handling, or Slider if preferred
                // EditorGUILayout.PropertyField(blendValProp, new GUIContent(ultiPaw.blendShapeNames[i]));
                 float newValue = EditorGUILayout.Slider(
                     new GUIContent(ultiPaw.blendShapeNames[i]), // Use GUIContent for tooltips later if needed
                     blendValProp.floatValue,
                     0f, 100f,
                     GUILayout.MinWidth(150) // Ensure minimum width
                 );


                if (EditorGUI.EndChangeCheck())
                {
                    // Update the property value - Undo is handled automatically by SerializedProperty
                    blendValProp.floatValue = newValue;
                    // Apply the change to the actual blendshape on the model
                    ultiPaw.UpdateBlendshapeFromSlider(ultiPaw.blendShapeNames[i], newValue);
                }

                if (i % columns == columns - 1 || i == ultiPaw.blendShapeNames.Count - 1)
                {
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }
    }

    private void DrawHelpBox()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "1. Ensure Base FBX is detected/assigned correctly.\n" +
            "2. Click 'Check for Updates' to find compatible versions.\n" +
            "3. Download and 'Select' a version.\n" +
            "4. Click 'Turn into UltiPaw' to apply the selected version.\n" +
            "5. Use 'Reset' to restore the original FBX from backup.",
            MessageType.Info
        );
    }

    // --- Logic Methods ---

    private void StartVersionFetch(UltiPaw ultiPaw)
    {
        if (isFetching) return; // Don't start if already fetching

        // Ensure hash is up-to-date before fetching
        ultiPaw.UpdateCurrentBaseFbxHash();
        string hashToFetch = ultiPaw.currentBaseFbxHash;

        if (string.IsNullOrEmpty(hashToFetch))
        {
            fetchError = "Base FBX hash could not be calculated. Assign a valid FBX.";
            serverVersions.Clear();
            selectedVersion = null;
            recommendedVersionDetails = null;
            lastFetchedHash = null;
            Repaint(); // Update UI to show error
            return;
        }

        // Reset state for new fetch
        fetchError = "";
        downloadError = ""; // Clear download error too
        isFetching = true;
        Repaint(); // Show "Fetching..." button state

        EditorCoroutineUtility.StartCoroutineOwnerless(FetchVersionsCoroutine(hashToFetch, ultiPaw));
    }

     private void StartVersionDownload(UltiPawVersion versionToDownload, UltiPaw ultiPaw)
    {
        if (isDownloading || isFetching) return;

        // Need the hash the versions were fetched *with* to construct the correct download path/URL
        if (string.IsNullOrEmpty(lastFetchedHash)) {
            downloadError = "Cannot download: Base FBX hash is unknown or versions haven't been fetched.";
            Repaint();
            return;
        }

        downloadError = "";
        isDownloading = true;
        Repaint(); // Show "Downloading..." button state

        EditorCoroutineUtility.StartCoroutineOwnerless(DownloadVersionCoroutine(versionToDownload, lastFetchedHash, ultiPaw));
    }


    private IEnumerator FetchVersionsCoroutine(string baseFbxHash, UltiPaw ultiPaw)
    {
        string url = $"{serverBaseUrl}{versionsEndpoint}?s={UltiPaw.SCRIPT_VERSION}&d={baseFbxHash}";
        Debug.Log($"[UltiPawEditor] Fetching versions from: {url}");

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            isFetching = false; // Fetch attempt finished

            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
            {
                HandleFetchError(req);
            }
            else // Success
            {
                try
                {
                    string json = req.downloadHandler.text;
                    Debug.Log($"[UltiPawEditor] Received JSON: {json}");
                    UltiPawVersionResponse response = JsonUtility.FromJson<UltiPawVersionResponse>(json);

                    if (response != null && response.versions != null)
                    {
                        serverVersions = response.versions;
                        recommendedVersionGuid = response.recommendedVersion;
                        fetchError = ""; // Clear error on success
                        lastFetchedHash = baseFbxHash; // Store the hash used for this successful fetch

                        // Find recommended version details
                        recommendedVersionDetails = serverVersions.FirstOrDefault(v => v.version == recommendedVersionGuid);

                        // Auto-select recommended version if nothing is selected or current selection is invalid
                        if (selectedVersion == null || !serverVersions.Any(v => v.version == selectedVersion.version))
                        {
                            if (recommendedVersionDetails != null)
                            {
                                 string expectedBinPath = UltiPawUtils.GetVersionBinPath(recommendedVersionDetails.version, lastFetchedHash);
                                 if(File.Exists(expectedBinPath)) {
                                     SelectVersion(recommendedVersionDetails, ultiPaw, expectedBinPath);
                                 } else {
                                     // Recommended isn't downloaded, clear selection
                                     ClearSelection(ultiPaw);
                                 }
                            } else {
                                ClearSelection(ultiPaw);
                            }
                        }
                        // If current selection is still valid in the new list, keep it
                        else if (selectedVersion != null) {
                             // Re-find the selected version object from the new list to ensure it's the current reference
                             var currentSelectedInNewList = serverVersions.FirstOrDefault(v => v.version == selectedVersion.version);
                             if(currentSelectedInNewList != null) {
                                 selectedVersion = currentSelectedInNewList; // Update reference
                                 ultiPaw.activeUltiPawVersion = selectedVersion; // Ensure component has updated reference
                             } else {
                                 // Previous selection is no longer valid, try recommended
                                 if (recommendedVersionDetails != null) {
                                     string expectedBinPath = UltiPawUtils.GetVersionBinPath(recommendedVersionDetails.version, lastFetchedHash);
                                     if(File.Exists(expectedBinPath)) {
                                         SelectVersion(recommendedVersionDetails, ultiPaw, expectedBinPath);
                                     } else { ClearSelection(ultiPaw); }
                                 } else { ClearSelection(ultiPaw); }
                             }
                        }

                    }
                    else
                    {
                        fetchError = "Failed to parse server response.";
                        Debug.LogError("[UltiPawEditor] JSON parsing error or null response/versions.");
                        serverVersions.Clear();
                        recommendedVersionDetails = null;
                        ClearSelection(ultiPaw);
                    }
                }
                catch (System.Exception e)
                {
                    fetchError = "Exception while parsing server response.";
                    Debug.LogError($"[UltiPawEditor] Exception: {e}");
                    serverVersions.Clear();
                    recommendedVersionDetails = null;
                    ClearSelection(ultiPaw);
                }
            }
        } // Dispose UnityWebRequest

        Repaint(); // Update UI with results or errors
    }


     private IEnumerator DownloadVersionCoroutine(UltiPawVersion versionToDownload, string baseFbxHashForPath, UltiPaw ultiPaw)
    {
        string downloadUrl = $"{serverBaseUrl}{modelEndpoint}?version={UnityWebRequest.EscapeURL(versionToDownload.version)}&d={baseFbxHashForPath}"; // Assuming server needs hash
        string targetZipPath = Path.Combine(Path.GetTempPath(), $"ultipaw_{versionToDownload.version}.zip"); // Download to temp file
        string targetExtractFolder = UltiPawUtils.GetVersionDataPath(versionToDownload.version, baseFbxHashForPath);

        Debug.Log($"[UltiPawEditor] Starting download for version {versionToDownload.version}...");
        Debug.Log($"[UltiPawEditor] Download URL: {downloadUrl}");
        Debug.Log($"[UltiPawEditor] Target Extract Folder: {targetExtractFolder}");

        UltiPawUtils.EnsureDirectoryExists(targetExtractFolder); // Ensure target directory exists before extraction

        using (UnityWebRequest req = UnityWebRequest.Get(downloadUrl))
        {
            req.downloadHandler = new DownloadHandlerFile(targetZipPath); // Download to temp zip file

            yield return req.SendWebRequest();

            isDownloading = false; // Download attempt finished

            if (req.result != UnityWebRequest.Result.Success)
            {
                downloadError = $"HTTP {req.responseCode}: {req.error}";
                Debug.LogError($"[UltiPawEditor] Failed to download version {versionToDownload.version}: {downloadError}");
                if (File.Exists(targetZipPath)) File.Delete(targetZipPath); // Clean up temp file on error
            }
            else
            {
                Debug.Log($"[UltiPawEditor] Successfully downloaded ZIP to: {targetZipPath}");
                downloadError = ""; // Clear error on success

                // --- Extraction ---
                try
                {
                    // Ensure target directory is clean before extracting (optional, prevents issues with old files)
                    if (Directory.Exists(targetExtractFolder))
                    {
                        // Directory.Delete(targetExtractFolder, true); // Use with caution!
                        // Directory.CreateDirectory(targetExtractFolder);
                    }

                    ZipFile.ExtractToDirectory(targetZipPath, targetExtractFolder, true); // Use System.IO.Compression, overwrite files
                    Debug.Log($"[UltiPawEditor] Successfully extracted files to: {targetExtractFolder}");

                    // Clean up downloaded zip file
                    if (File.Exists(targetZipPath)) File.Delete(targetZipPath);

                    AssetDatabase.Refresh(); // IMPORTANT: Make Unity aware of the new/updated files

                    // Automatically select the downloaded version if it was the recommended one or only one
                     if (selectedVersion == null && versionToDownload == recommendedVersionDetails) {
                         string expectedBinPath = UltiPawUtils.GetVersionBinPath(versionToDownload.version, baseFbxHashForPath);
                         SelectVersion(versionToDownload, ultiPaw, expectedBinPath);
                     }
                     // Or if the user explicitly clicked download for this version, select it
                     else if (selectedVersion == null || selectedVersion.version != versionToDownload.version) {
                          string expectedBinPath = UltiPawUtils.GetVersionBinPath(versionToDownload.version, baseFbxHashForPath);
                          SelectVersion(versionToDownload, ultiPaw, expectedBinPath);
                     }


                }
                catch (System.Exception e)
                {
                    downloadError = $"Extraction failed: {e.Message}";
                    Debug.LogError($"[UltiPawEditor] Failed to extract ZIP file '{targetZipPath}' to '{targetExtractFolder}': {e}");
                    // Don't delete the zip on extraction error, user might want it
                    AssetDatabase.Refresh(); // Refresh anyway to show potentially partial extraction
                }
            }
        } // Dispose UnityWebRequest

        Repaint(); // Update UI
    }


    private void HandleFetchError(UnityWebRequest req)
    {
        if (req.responseCode == 406) // Specific server errors
        {
            fetchError = req.downloadHandler.text; // Display server message directly
            Debug.LogError($"[UltiPawEditor] Server Error 406: {fetchError}");
        }
        else // Generic network/protocol errors
        {
            fetchError = $"Error {req.responseCode}: {req.error}";
            Debug.LogError($"[UltiPawEditor] Fetch Error: {fetchError}");
        }
        serverVersions.Clear();
        recommendedVersionDetails = null;
        lastFetchedHash = null; // Fetch failed, invalidate hash
        ClearSelection(null); // Clear selection on fetch error
    }

    private void SelectVersion(UltiPawVersion version, UltiPaw ultiPaw, string binPath)
    {
        if (version == null) return;

        selectedVersion = version;
        ultiPaw.activeUltiPawVersion = version; // Update component
        ultiPaw.selectedUltiPawBinPath = binPath; // Update component

        // Update blendshapes from the version's customBlendshapes.
        Undo.RecordObject(ultiPaw, "Select UltiPaw Version"); // Record state change for Undo

        if (version.customBlendshapes != null)
        {
            // Check if blendshapes actually changed to avoid unnecessary list recreation
            if (!ultiPaw.blendShapeNames.SequenceEqual(version.customBlendshapes))
            {
                 ultiPaw.blendShapeNames = new List<string>(version.customBlendshapes);
                 ultiPaw.SyncBlendshapeLists(); // Resize value list, reset values to 0
                 // Reset actual model blendshapes to 0 for the new set
                 foreach(var name in ultiPaw.blendShapeNames) {
                     ultiPaw.UpdateBlendshapeFromSlider(name, 0f);
                 }
                 // Update SerializedProperty representation
                 serializedObject.Update(); // Fetch latest state after list change
            }
        }
        else // No blendshapes defined for this version
        {
             if (ultiPaw.blendShapeNames.Count > 0) {
                 ultiPaw.blendShapeNames.Clear();
                 ultiPaw.blendShapeValues.Clear();
                 serializedObject.Update();
             }
        }

        EditorUtility.SetDirty(ultiPaw); // Mark component dirty
        Debug.Log($"[UltiPawEditor] Selected version: {version.version}");
        Repaint(); // Update UI to show selection change
    }

     private void ClearSelection(UltiPaw ultiPaw) {
         selectedVersion = null;
         if (ultiPaw != null) {
             if (ultiPaw.activeUltiPawVersion != null) {
                 Undo.RecordObject(ultiPaw, "Clear UltiPaw Selection");
                 ultiPaw.activeUltiPawVersion = null;
                 ultiPaw.selectedUltiPawBinPath = null;
                 // Optionally clear blendshapes? Or keep last used ones? Let's clear them.
                 ultiPaw.blendShapeNames.Clear();
                 ultiPaw.blendShapeValues.Clear();
                 EditorUtility.SetDirty(ultiPaw);
                 serializedObject.Update(); // Update editor's view of the object
             }
         }
     }


    private bool AnyBackupExists(List<GameObject> files)
    {
        if (files == null) return false;
        // Use Linq Any() - requires 'using System.Linq;'
        return files.Any(file => file != null && File.Exists(AssetDatabase.GetAssetPath(file) + ".old"));
    }
}
#endif