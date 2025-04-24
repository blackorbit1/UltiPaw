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
    private SerializedProperty blendShapeNamesProp;
    private SerializedProperty blendShapeValuesProp;

    // Version management state
    private List<UltiPawVersion> serverVersions = new List<UltiPawVersion>();
    private string recommendedVersionGuid = "";
    private UltiPawVersion selectedVersion = null;
    private UltiPawVersion recommendedVersionDetails = null;

    private bool isFetching = false;
    private bool isDownloading = false;
    private string fetchError = "";
    private string downloadError = "";
    private string lastFetchedHash = null; // Keep track of hash used for fetch, might still be useful for checks
    private bool versionsFoldout = false;

    // Server settings
    private const string serverBaseUrl = "http://192.168.1.180:8080"; // Update with your server URL
    private const string versionsEndpoint = "/ultipaw/getVersions";
    private const string modelEndpoint = "/ultipaw/getModel";

    private void OnEnable()
    {
        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(UltiPawUtils.BASE_FOLDER + "/banner.png");
        baseFbxFilesProp = serializedObject.FindProperty("baseFbxFiles");
        specifyCustomBaseFbxProp = serializedObject.FindProperty("specifyCustomBaseFbx");
        blendShapeNamesProp = serializedObject.FindProperty("blendShapeNames");
        blendShapeValuesProp = serializedObject.FindProperty("blendShapeValues");

        UltiPaw ultiPaw = (UltiPaw)target;
        // Restore selected version state from the component if possible
        if (ultiPaw.activeUltiPawVersion.version != "")
        {
            selectedVersion = ultiPaw.activeUltiPawVersion;
        }
        

        // Initial fetch if hash changed or never fetched
        if (ultiPaw.UpdateCurrentBaseFbxHash() || lastFetchedHash == null || selectedVersion == null)
        {
            StartVersionFetch(ultiPaw);
        }
        
        // if (ultiPaw.UpdateCurrentBaseFbxHash() || lastFetchedHash == null) // Fetch if hash changed or never fetched
        // {
        //     StartVersionFetch(ultiPaw);
        // }
        
        // retrieve blendShapeValuesProp
        if (blendShapeValuesProp == null)
        {
            Debug.LogError("[UltiPawEditor] blendShapeValuesProp is null. Ensure the property is correctly defined in the UltiPaw script.");
        }
        else
        {
            // Initialize blendShapeValuesProp if needed
            if (blendShapeValuesProp.arraySize != 0)
            {
                for (int i = 0; i < blendShapeValuesProp.arraySize; i++)
                {
                    blendShapeValuesProp.GetArrayElementAtIndex(i).floatValue = 0f; // Initialize to 0
                }
            }
        }
        
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        UltiPaw ultiPaw = (UltiPaw)target;

        DrawBanner();
        DrawFileConfiguration(ultiPaw); // This might trigger a fetch if FBX changes

        // Ensure hash is current before drawing version management
        //bool hashChanged = ultiPaw.UpdateCurrentBaseFbxHash();
        //if (hashChanged && !isFetching) // Fetch if hash changed and not already fetching
        //{
        //     EditorApplication.delayCall += () => StartVersionFetch(ultiPaw);
        //}
        
        DrawVersionManagement(ultiPaw);
        DrawActionButtons(ultiPaw);
        DrawBlendshapeSliders(ultiPaw);
        DrawHelpBox();

        serializedObject.ApplyModifiedProperties();
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

        bool fbxFieldChanged = false;
        if (specifyCustomBaseFbxProp.boolValue)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(baseFbxFilesProp, new GUIContent("Base FBX File(s)"), true);
            fbxFieldChanged = EditorGUI.EndChangeCheck();
        }
        else // Auto-detect mode
        {
            if (fbxSpecChanged) // Just switched to auto-detect
            {
                EditorApplication.delayCall += ultiPaw.AutoDetectBaseFbxViaHierarchy; // Schedule detection
            }
            GUI.enabled = false;
            EditorGUILayout.PropertyField(baseFbxFilesProp, new GUIContent("Detected Base FBX"), true);
            GUI.enabled = true;
        }

        // If any relevant property changed, apply and potentially trigger fetch
        if (fbxSpecChanged || fbxFieldChanged)
        {
            serializedObject.ApplyModifiedProperties(); // Apply changes first
            // Fetch will be triggered by hash check in OnInspectorGUI
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }
    
    private void DrawVersionManagement(UltiPaw ultiPaw)
    {
        EditorGUILayout.LabelField("UltiPaw Version Manager", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Display Errors First (without hiding rest of UI)
        if (!string.IsNullOrEmpty(fetchError))
        {
            EditorGUILayout.HelpBox("Fetch Error: " + fetchError, MessageType.Error);
        }
        if (!string.IsNullOrEmpty(downloadError))
        {
            EditorGUILayout.HelpBox("Download Error: " + downloadError, MessageType.Warning); // Warning, might be recoverable
        }

        // Update/Fetch Button
        GUI.enabled = !isFetching && !isDownloading;
        if (GUILayout.Button(isFetching ? "Fetching..." : "Check for Updates"))
        {
            StartVersionFetch(ultiPaw); // Manually trigger fetch
        }
        GUI.enabled = true; // Re-enable GUI for elements below

        // Display Version Info only if fetch hasn't failed catastrophically
        if (string.IsNullOrEmpty(fetchError) || fetchError.Contains("parse") || fetchError.Contains("406")) // Show UI even on parsing/406 errors
        {
             // Display Recommended / Currently Selected Version
            UltiPawVersion versionToShow = selectedVersion ?? recommendedVersionDetails;

            if (versionToShow != null)
            {
                EditorGUILayout.LabelField("Selected Version:", EditorStyles.boldLabel);
                DrawVersionDetails(versionToShow, ultiPaw, true);
            }
            else if (serverVersions.Count > 0 && !isFetching) // Fetch succeeded but no recommended/selected
            {
                 EditorGUILayout.HelpBox("Please select a version from the list below.", MessageType.Info);
            }
             else if (!isFetching && lastFetchedHash != null && serverVersions.Count == 0) // Fetch succeeded, hash known, but no versions returned
            {
                 EditorGUILayout.HelpBox("No compatible versions found for the detected Base FBX hash.", MessageType.Warning);
            }
            else if (!isFetching && lastFetchedHash == null) // Haven't fetched successfully yet
            {
                 EditorGUILayout.HelpBox("Click 'Check for Updates' or assign a Base FBX.", MessageType.Info);
            }


            // Foldout for other available versions
            if (serverVersions.Count > 0) // Only show foldout if there are versions
            {
                // Determine if there are versions *other* than the one displayed prominently
                bool hasOtherVersions = serverVersions.Any(v => v != versionToShow);

                if (hasOtherVersions)
                {
                    versionsFoldout = EditorGUILayout.Foldout(versionsFoldout, "Other Versions", true);
                    if (versionsFoldout)
                    {
                        EditorGUI.indentLevel++;
                        foreach (var ver in serverVersions)
                        {
                            if (ver != versionToShow) // Only draw versions not already shown above
                            {
                                DrawVersionListItem(ver, ultiPaw);
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
                }
                // If only one version exists and it's already displayed, don't show the foldout.
            }
        }


        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    private void DrawVersionDetails(UltiPawVersion ver, UltiPaw ultiPaw, bool showDownloadOrSelect)
    {
        if (ver == null) return; // Already checked

        EditorGUILayout.BeginHorizontal();
        // Check validity *before* generating paths
        bool hasValidVersionStrings = !string.IsNullOrEmpty(ver.version) && !string.IsNullOrEmpty(ver.defaultAviVersion);

        // Display basic info even if strings are invalid
        GUILayout.Label($"UltiPaw {ver.version ?? "N/A"}", EditorStyles.boldLabel, GUILayout.Width(100));
        DrawScopeLabel(ver.scope);
        GUILayout.FlexibleSpace();

        if (hasValidVersionStrings) // Only draw buttons/status if data is valid
        {
            string expectedBinPath = UltiPawUtils.GetVersionBinPath(ver.version, ver.defaultAviVersion);
            // Check path validity *and* file existence
            bool isDownloaded = !string.IsNullOrEmpty(expectedBinPath) && File.Exists(expectedBinPath);

            if (showDownloadOrSelect)
            {
                 if (selectedVersion == ver)
                 {
                     GUI.enabled = false;
                     GUILayout.Button("Selected", GUILayout.Width(100));
                     GUI.enabled = true;
                 }
                 else if (isDownloaded)
                 {
                    GUI.enabled = !isFetching && !isDownloading;
                    if (GUILayout.Button("Select", GUILayout.Width(100)))
                    {
                        // Pass the already validated path
                        SelectVersion(ver, ultiPaw, expectedBinPath);
                    }
                     GUI.enabled = true;
                 }
                 else // Not downloaded
                 {
                    GUI.enabled = !isDownloading && !isFetching;
                    if (GUILayout.Button(isDownloading ? "Downloading..." : "Download", GUILayout.Width(100)))
                    {
                        StartVersionDownload(ver, ultiPaw);
                    }
                    GUI.enabled = true;
                 }
            }
        } else {
            GUILayout.Label("(Invalid Version Data)", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();

        // Display Changelog (safe)
        if (!string.IsNullOrEmpty(ver.changelog))
        {
            EditorGUILayout.LabelField("Changelog:", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(ver.changelog, MessageType.None);
        }
        GUILayout.Space(5);
    }



    private void DrawVersionListItem(UltiPawVersion ver, UltiPaw ultiPaw)
    {
        if (ver == null) return;

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        bool hasValidVersionStrings = !string.IsNullOrEmpty(ver.version) && !string.IsNullOrEmpty(ver.defaultAviVersion);

        GUILayout.Label($"UltiPaw {ver.version ?? "N/A"}", GUILayout.Width(100));
        DrawScopeLabel(ver.scope);
        GUILayout.FlexibleSpace();

        if (hasValidVersionStrings)
        {
            string expectedBinPath = UltiPawUtils.GetVersionBinPath(ver.version, ver.defaultAviVersion);
            bool isDownloaded = !string.IsNullOrEmpty(expectedBinPath) && File.Exists(expectedBinPath);

            // Use more robust comparison for selected state
            bool isCurrentlySelected = selectedVersion != null &&
                                       selectedVersion.version == ver.version &&
                                       selectedVersion.defaultAviVersion == ver.defaultAviVersion;

            if (isCurrentlySelected)
            {
                GUI.enabled = false;
                GUILayout.Button("Selected", GUILayout.Width(80));
                GUI.enabled = true;
            }
            else if (isDownloaded)
            {
                GUI.enabled = !isFetching && !isDownloading;
                if (GUILayout.Button("Select", GUILayout.Width(80)))
                {
                    SelectVersion(ver, ultiPaw, expectedBinPath);
                }
                GUI.enabled = true;
            }
            else // Not downloaded
            {
                GUI.enabled = !isDownloading && !isFetching;
                if (GUILayout.Button(isDownloading ? "Downloading..." : "Download", GUILayout.Width(80)))
                {
                    StartVersionDownload(ver, ultiPaw);
                }
                GUI.enabled = true;
            }
        } else {
             GUILayout.Label("(Invalid Data)", EditorStyles.miniLabel);
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
                            File.Exists(ultiPaw.selectedUltiPawBinPath) && // Bin file must exist // TODO: Needs to be factored
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
        
        if(!canTransform && !hasRestore) { // inconsistent state
            // check if the fbx hash matches the base fbx hash, if yes ultiPaw.isUltiPaw should be false
            ultiPaw.UpdateCurrentBaseFbxHash();
        } 
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
        if (isFetching) return;

        // Ensure hash is up-to-date
        ultiPaw.UpdateCurrentBaseFbxHash();
        string hashToFetch = ultiPaw.currentBaseFbxHash;

        if (string.IsNullOrEmpty(hashToFetch))
        {
            // Don't set fetchError here, let the UI show "Assign FBX" message
            // Clear previous results though
            serverVersions.Clear();
            selectedVersion = null;
            recommendedVersionDetails = null;
            lastFetchedHash = null;
            fetchError = ""; // Clear previous errors
            downloadError = "";
            Repaint();
            return;
        }

        fetchError = "";
        downloadError = "";
        isFetching = true;
        Repaint();

        EditorCoroutineUtility.StartCoroutineOwnerless(FetchVersionsCoroutine(hashToFetch, ultiPaw));
    }

    private void StartVersionDownload(UltiPawVersion versionToDownload, UltiPaw ultiPaw)
    {
        if (isDownloading || isFetching) return;

        // Use the defaultAviVersion from the specific version being downloaded
        if (string.IsNullOrEmpty(versionToDownload?.defaultAviVersion)) {
            downloadError = "Version data is missing required 'defaultAviVersion'. Cannot determine download path.";
            Debug.LogError("[UltiPawEditor] " + downloadError);
            Repaint();
            return;
        }

        downloadError = "";
        isDownloading = true;
        Repaint();

        // Pass the correct defaultAviVersion for path generation
        EditorCoroutineUtility.StartCoroutineOwnerless(DownloadVersionCoroutine(versionToDownload, ultiPaw));
    }


    private IEnumerator FetchVersionsCoroutine(string baseFbxHash, UltiPaw ultiPaw)
    {
        string url = $"{serverBaseUrl}{versionsEndpoint}?s={UltiPaw.SCRIPT_VERSION}&d={baseFbxHash}";
        Debug.Log($"[UltiPawEditor] Fetching versions from: {url}");

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            //yield return req.SendWebRequest();
            
            var op = req.SendWebRequest();
            while (!op.isDone) 
                yield return null;

            isFetching = false; // Mark fetch as complete for the UI

            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
            {
                HandleFetchError(req, ultiPaw);
            }
            else // Success
            {
                try
                {
                    string json = req.downloadHandler.text;
                    UltiPawVersionResponse response = JsonUtility.FromJson<UltiPawVersionResponse>(json);

                    if (response != null && response.versions != null)
                    {
                        serverVersions = response.versions;
                        recommendedVersionGuid = response.recommendedVersion;
                        fetchError = ""; // Clear error on success
                        lastFetchedHash = baseFbxHash;

                        // --- Refined Selection Logic ---
                        recommendedVersionDetails = serverVersions.FirstOrDefault(v => v != null && v.version == recommendedVersionGuid);

                        UltiPawVersion versionToSelect = null;
                        string binPathForSelection = null;

                        // 1. Try to keep the currently selected version if it's still valid and downloaded
                        if (selectedVersion != null)
                        {
                            var currentSelectedInNewList = serverVersions.FirstOrDefault(v => v != null && v.version == selectedVersion.version && v.defaultAviVersion == selectedVersion.defaultAviVersion);
                            if (currentSelectedInNewList != null)
                            {
                                string binPath = UltiPawUtils.GetVersionBinPath(currentSelectedInNewList.version, currentSelectedInNewList.defaultAviVersion);
                                if (!string.IsNullOrEmpty(binPath) && File.Exists(binPath))
                                {
                                    versionToSelect = currentSelectedInNewList; // Keep current selection
                                    binPathForSelection = binPath;
                                }
                            }
                        }

                        // 2. If no valid current selection, try the recommended version (if downloaded)
                        if (versionToSelect == null && recommendedVersionDetails != null)
                        {
                            string binPath = UltiPawUtils.GetVersionBinPath(recommendedVersionDetails.version, recommendedVersionDetails.defaultAviVersion);
                            if (!string.IsNullOrEmpty(binPath) && File.Exists(binPath))
                            {
                                versionToSelect = recommendedVersionDetails; // Select recommended
                                binPathForSelection = binPath;
                            }
                        }

                        // 3. Apply the determined selection (or clear if none found)
                        if (versionToSelect != null)
                        {
                            // Only call SelectVersion if it's actually different from the current one
                            if (selectedVersion != versionToSelect)
                            {
                                SelectVersion(versionToSelect, ultiPaw, binPathForSelection);
                            }
                            // If it's the same, ensure the reference is updated from the new list
                            else if (selectedVersion != null && selectedVersion != versionToSelect)
                            {
                                selectedVersion = versionToSelect; // Update internal reference
                                ultiPaw.activeUltiPawVersion = versionToSelect; // Update component reference
                            }
                        }
                        else
                        {
                            // No valid selection found (neither current nor recommended is downloaded/valid)
                            ClearSelection(ultiPaw);
                        }
                        // --- End Refined Selection Logic ---
                    }
                    else
                    {
                        fetchError = "Failed to parse server response (JSON structure might be incorrect).";
                        Debug.LogError("[UltiPawEditor] JSON parsing error or null response/versions. Check server response format.");
                        serverVersions.Clear();
                        recommendedVersionDetails = null;
                        ClearSelection(ultiPaw);
                    }
                }
                catch (System.Exception e)
                {
                    fetchError = $"Exception parsing server response: {e.Message}";
                    Debug.LogError($"[UltiPawEditor] Exception: {e}");
                    serverVersions.Clear();
                    recommendedVersionDetails = null;
                    ClearSelection(ultiPaw);
                }
            }
        } // Dispose UnityWebRequest

        Repaint(); // Update UI with results
    }
    private IEnumerator DownloadVersionCoroutine(UltiPawVersion versionToDownload, UltiPaw ultiPaw)
    {
        // --- 1. Setup ---
        string baseFbxHashForQuery = lastFetchedHash ?? "unknown";
        string downloadUrl = $"{serverBaseUrl}{modelEndpoint}?version={UnityWebRequest.EscapeURL(versionToDownload.version)}&d={baseFbxHashForQuery}";
        string targetExtractFolder = UltiPawUtils.VERSIONS_FOLDER;
        string targetZipPath = $"{targetExtractFolder}.zip";

        // Ensure directories exist
        UltiPawUtils.EnsureDirectoryExists(targetExtractFolder); // For extraction
        UltiPawUtils.EnsureDirectoryExists(Path.GetDirectoryName(targetZipPath)); // For temp zip

        Debug.Log($"[UltiPawEditor] Starting download for version {versionToDownload.version} (Base: {versionToDownload.defaultAviVersion})");
        Debug.Log($"[UltiPawEditor] Download URL: {downloadUrl}");
        Debug.Log($"[UltiPawEditor] Target Extract Folder: {targetExtractFolder}");

        downloadError = ""; // Clear previous errors
        isDownloading = true;
        Repaint();

        // --- 2. Create Request Objects and Perform Download ---
        UnityWebRequest req = null;
        DownloadHandlerFile downloadHandler = null;
        bool downloadAttempted = false;

        try // Minimal try block JUST for setup that might fail before yield
        {
            if (File.Exists(targetZipPath)) File.Delete(targetZipPath); // Clean up old temp file first
            downloadHandler = new DownloadHandlerFile(targetZipPath);
            req = new UnityWebRequest(downloadUrl, UnityWebRequest.kHttpVerbGET, downloadHandler, null);
            downloadAttempted = true;
        }
        catch (System.Exception setupEx)
        {
            downloadError = $"Download setup failed: {setupEx.Message}";
            Debug.LogError($"[UltiPawEditor] {downloadError}");
            // Setup failed, skip yield and go to cleanup
        }

        // --- 3. Yield for Download (Only if setup succeeded) ---
        // This yield is NOT inside a try block with catch/finally
        if (downloadAttempted && req != null)
        {
            var op = req.SendWebRequest();
            while (!op.isDone)
                yield return null;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[UltiPawEditor] Download failed [{req.responseCode} {req.result}]: {req.error}");
                yield break;
            }
        }

        // --- 4. Process Result, Extract, and Cleanup (Uses try/finally) ---
        bool downloadSucceeded = false;
        try // This try/finally ensures cleanup happens
        {
            if (downloadAttempted && req != null) // Check if the request was actually sent
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[UltiPawEditor] Successfully downloaded ZIP to: {targetZipPath}");
                    downloadSucceeded = true;
                }
                else
                {
                    downloadError = $"Download failed [{req.responseCode} {req.result}]: {req.error}";
                    Debug.LogError($"[UltiPawEditor] {downloadError}");
                }
            }
            // If setup failed, downloadError was already set

            // --- 5. Extraction (Only if download succeeded) ---
            if (downloadSucceeded)
            {
                // Dispose handler *before* extraction
                downloadHandler.Dispose();
                downloadHandler = null; // Prevent double disposal in finally

                try // Separate try/catch specifically for extraction errors
                {
                    ZipFile.ExtractToDirectory(targetZipPath, targetExtractFolder, true);
                    Debug.Log($"[UltiPawEditor] Successfully extracted files to: {targetExtractFolder}");
                    AssetDatabase.Refresh();

                    // Auto-select after successful download and extraction
                    string expectedBinPath = UltiPawUtils.GetVersionBinPath(versionToDownload.version, versionToDownload.defaultAviVersion);
                    // Ensure bin path is valid before selecting
                    if (!string.IsNullOrEmpty(expectedBinPath)) {
                         SelectVersion(versionToDownload, ultiPaw, expectedBinPath);
                    } else {
                         Debug.LogError("[UltiPawEditor] Could not determine bin path after download. Selection skipped.");
                         downloadError = "Extraction seemed successful, but failed to determine file path for selection."; // Update error state
                    }
                }
                catch (System.Exception ex)
                {
                    downloadError = $"Extraction failed: {ex.Message}"; // Set specific error
                    Debug.LogError($"[UltiPawEditor] Failed to extract ZIP file '{targetZipPath}' to '{targetExtractFolder}': {ex}");
                    AssetDatabase.Refresh(); // Refresh anyway
                }
            }
        }
        finally // This block ensures disposal and cleanup happens
        {
            isDownloading = false; // Mark process finished

            // Dispose handlers if they exist and weren't disposed earlier
            downloadHandler?.Dispose();
            req?.Dispose();

            // Clean up temp file if it exists
            if (File.Exists(targetZipPath))
            {
                try { File.Delete(targetZipPath); }
                catch (IOException ioEx) { Debug.LogWarning($"[UltiPawEditor] Could not delete temp file '{targetZipPath}' (might be locked): {ioEx.Message}"); }
                catch (System.Exception ex) { Debug.LogWarning($"[UltiPawEditor] Error deleting temp file '{targetZipPath}': {ex.Message}"); }
            }
            Repaint(); // Update UI after everything
        }
    }


    private void HandleFetchError(UnityWebRequest req, UltiPaw ultiPaw)
    {
        if (req.responseCode == 406)
        {
            fetchError = $"Server rejected request [{req.responseCode}]: {req.downloadHandler?.text ?? req.error}";
            Debug.LogError($"[UltiPawEditor] Server Error 406: {fetchError}");
        }
        else
        {
            fetchError = $"Network Error [{req.responseCode} {req.result}]: {req.error}";
            Debug.LogError($"[UltiPawEditor] Fetch Error: {fetchError}");
        }
        serverVersions.Clear();
        recommendedVersionDetails = null;
        lastFetchedHash = null;
        ClearSelection(ultiPaw); // Pass ultiPaw instance
    }

    private void SelectVersion(UltiPawVersion version, UltiPaw ultiPaw, string binPath)
    {
        if (version == null || ultiPaw == null) return;

        // Check if already selected
        if (selectedVersion == version) return;

        selectedVersion = version;

        // Record Undo for changes to the UltiPaw component
        Undo.RecordObject(ultiPaw, "Select UltiPaw Version");

        ultiPaw.activeUltiPawVersion = version;
        ultiPaw.selectedUltiPawBinPath = binPath;

        // Update blendshapes safely
        var newBlendshapes = version.customBlendshapes ?? new string[0]; // Ensure not null
        if (!ultiPaw.blendShapeNames.SequenceEqual(newBlendshapes))
        {
            ultiPaw.blendShapeNames = new List<string>(newBlendshapes);
            ultiPaw.SyncBlendshapeLists(); // Resizes value list, resets values to 0

            // Reset actual model blendshapes for the new set
            foreach (var name in ultiPaw.blendShapeNames)
            {
                ultiPaw.UpdateBlendshapeFromSlider(name, 0f); // Use the method that finds Body SMR
            }
        }

        EditorUtility.SetDirty(ultiPaw);
        serializedObject.Update(); // Update serialized object to reflect component changes
        Debug.Log($"[UltiPawEditor] Selected version: {version.version}");
        Repaint();
    }

    private void ClearSelection(UltiPaw ultiPaw) {
        if (selectedVersion == null && (ultiPaw == null || ultiPaw.activeUltiPawVersion == null)) return; // Nothing to clear

        selectedVersion = null;
        if (ultiPaw != null) {
            Undo.RecordObject(ultiPaw, "Clear UltiPaw Selection");
            ultiPaw.activeUltiPawVersion = null;
            ultiPaw.selectedUltiPawBinPath = null;
            // Keep blendshapes as they were? Or clear? Let's clear for consistency.
            if (ultiPaw.blendShapeNames.Count > 0) {
                ultiPaw.blendShapeNames.Clear();
                ultiPaw.blendShapeValues.Clear();
            }
            EditorUtility.SetDirty(ultiPaw);
            serializedObject.Update();
        }
        Repaint();
     }


    private bool AnyBackupExists(List<GameObject> files)
    {
        if (files == null) return false;
        // Use Linq Any() - requires 'using System.Linq;'
        return files.Any(file => file != null && File.Exists(AssetDatabase.GetAssetPath(file) + ".old"));
    }
}
#endif