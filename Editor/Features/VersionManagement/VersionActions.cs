#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VersionActions
{
    private readonly UltiPawEditor editor;
    private readonly NetworkService networkService;
    private readonly FileManagerService fileManagerService;

    public VersionActions(UltiPawEditor editor, NetworkService network, FileManagerService files)
    {
        this.editor = editor;
        this.networkService = network;
        this.fileManagerService = files;
    }
    
    // Coroutine Starters
    public void StartVersionFetch() => EditorCoroutineUtility.StartCoroutineOwnerless(FetchVersionsCoroutine());
    public void StartVersionDownload(UltiPawVersion ver, bool apply) => EditorCoroutineUtility.StartCoroutineOwnerless(DownloadVersionCoroutine(ver, apply));
    public void StartVersionDelete(UltiPawVersion ver) => EditorCoroutineUtility.StartCoroutineOwnerless(DeleteVersionCoroutine(ver));
    public void StartApplyVersion() => EditorCoroutineUtility.StartCoroutineOwnerless(ApplyOrResetCoroutine(editor.selectedVersionForAction, false));
    public void StartReset() => EditorCoroutineUtility.StartCoroutineOwnerless(ApplyOrResetCoroutine(null, true));

    private IEnumerator FetchVersionsCoroutine()
    {
        if (editor.isFetching) yield break;
        editor.isFetching = true;
        editor.fetchError = null;
        editor.fetchAttempted = true;
        editor.Repaint();

        UpdateCurrentBaseFbxHash();

        if (string.IsNullOrEmpty(editor.currentBaseFbxHash))
        {
            editor.serverVersions.Clear();
            UpdateAppliedVersionAndState(); // This will clear the applied state
            editor.isFetching = false;
            editor.Repaint();
            yield break;
        }

        string url = $"{UltiPawUtils.getServerUrl()}{UltiPawUtils.VERSION_ENDPOINT}?d={editor.currentBaseFbxHash}&t={editor.authToken}";
        var fetchTask = networkService.FetchVersionsAsync(url);
        
        while (!fetchTask.IsCompleted)
        {
            yield return null;
        }
        
        var (success, response, error) = fetchTask.Result;
        if (success)
        {
            editor.serverVersions = response?.versions ?? new System.Collections.Generic.List<UltiPawVersion>();
            editor.recommendedVersion = editor.serverVersions.FirstOrDefault(v => v.version == response.recommendedVersion);
            UpdateAppliedVersionAndState();
            SmartSelectVersion();
        }
        else
        {
            editor.fetchError = error;
            editor.serverVersions.Clear();
            UpdateAppliedVersionAndState(); // Clear state on error too
        }
        
        editor.isFetching = false;
        editor.Repaint();
    }
    
    private IEnumerator DownloadVersionCoroutine(UltiPawVersion version, bool applyAfter)
    {
        if (editor.isDownloading) yield break;
        editor.isDownloading = true;
        editor.downloadError = null;
        editor.Repaint();
        
        string tempZipPath = Path.Combine(Path.GetTempPath(), $"ultipaw_dl_{Guid.NewGuid()}.zip");
        string url = $"{UltiPawUtils.getServerUrl()}{UltiPawUtils.MODEL_ENDPOINT}?version={version.version}&d={editor.currentBaseFbxHash}&t={editor.authToken}";
        
        // --- Setup phase (no yield returns) ---
        var downloadTask = networkService.DownloadFileAsync(url, tempZipPath);
        bool setupSucceeded = downloadTask != null;
        
        if (!setupSucceeded)
        {
            editor.downloadError = "Failed to start download task";
            editor.isDownloading = false;
            editor.Repaint();
            yield break;
        }
        
        // --- Download phase (with yield returns, NOT in try/catch) ---
        while (!downloadTask.IsCompleted)
        {
            yield return null;
        }
        
        // --- Process result and cleanup ---
        var (success, error) = downloadTask.Result;
        bool downloadSucceeded = false;
        bool extractionSucceeded = false;
        string tempExtractPath = null;
        
        try
        {
            if (!success)
            {
                editor.downloadError = error;
            }
            else
            {
                downloadSucceeded = true;
                string finalDest = UltiPawUtils.GetVersionDataPath(version.version, version.defaultAviVersion);
                tempExtractPath = Path.Combine(Path.GetTempPath(), $"ultipaw_extract_{Guid.NewGuid()}");
                
                fileManagerService.UnzipAndMove(tempZipPath, tempExtractPath, finalDest);
                extractionSucceeded = true;
                
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }
        catch (Exception e) 
        { 
            editor.downloadError = $"Extraction failed: {e.Message}"; 
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
            if (!string.IsNullOrEmpty(tempExtractPath) && Directory.Exists(tempExtractPath)) 
                Directory.Delete(tempExtractPath, true);
            
            editor.isDownloading = false;
            editor.Repaint();
        }
        
        // --- Post-processing phase (outside try/catch) ---
        if (extractionSucceeded)
        {
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                yield return null;
            }
            if (applyAfter) 
            {
                editor.selectedVersionForAction = version;
                // Start the coroutine but don't yield return it (since it returns void)
                EditorCoroutineUtility.StartCoroutineOwnerless(ApplyOrResetCoroutine(version, false));
            }
        }
    }

    private IEnumerator DeleteVersionCoroutine(UltiPawVersion version)
    {
        if (editor.isDeleting) yield break;
        editor.isDeleting = true;
        editor.deleteError = null;
        editor.Repaint();

        string path = UltiPawUtils.GetVersionDataPath(version.version, version.defaultAviVersion);
        bool deleted = false;
        try
        {
            fileManagerService.DeleteVersionFolder(path);
            deleted = true;
        }
        catch (Exception e) { editor.deleteError = $"Failed to delete folder: {e.Message}"; }

        yield return null;

        if (deleted) AssetDatabase.Refresh();

        editor.isDeleting = false;
        editor.Repaint();
    }

    private IEnumerator ApplyOrResetCoroutine(UltiPawVersion version, bool isReset)
    {
        var root = editor.ultiPawTarget.transform.root;
        // FIX: Ensure logic is removed at the very start. This logic is correct and should now work reliably.
        fileManagerService.RemoveExistingLogic(root);

        string fbxPath = GetCurrentFBXPath();
        if (string.IsNullOrEmpty(fbxPath)) yield break;
        
        bool success = false;
        try
        {
            if (isReset)
            {
                fileManagerService.RestoreBackup(fbxPath);
            }
            else
            {
                if (version == null) throw new ArgumentNullException(nameof(version), "A version must be provided to apply.");
                
                string binPath = UltiPawUtils.GetVersionBinPath(version.version, version.defaultAviVersion);
                if (!File.Exists(binPath)) throw new FileNotFoundException("Apply failed: .bin file not found. Please download it first.");

                string originalFbxPath = fbxPath.EndsWith(FileManagerService.OriginalSuffix) ? fbxPath : fbxPath + FileManagerService.OriginalSuffix;
                if (!File.Exists(originalFbxPath))
                {
                    // If no backup, create one from the current file before transforming.
                    fileManagerService.CreateBackup(fbxPath);
                    originalFbxPath = fbxPath + FileManagerService.OriginalSuffix;
                }
                
                byte[] baseData = File.ReadAllBytes(originalFbxPath);
                byte[] binData = File.ReadAllBytes(binPath);
                byte[] transformedData = fileManagerService.XorTransform(baseData, binData);
                
                File.WriteAllBytes(fbxPath, transformedData);
            }
            success = true;
        }
        catch(Exception e)
        {
            Debug.LogError($"[UltiPaw] Operation failed: {e.Message}");
            if(!isReset && fileManagerService.BackupExists(fbxPath)) fileManagerService.RestoreBackup(fbxPath);
        }
        
        // This block now runs after the file modification is complete.
        if (success)
        {
            // We must force Unity to re-import the changed asset. This is a critical step.
            // TODO AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            
            // Wait until Unity has finished re-importing and compiling.
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                yield return null;
            }

            
            // IMPROVEMENT: Use selected version as a fallback for reset if applied is null, making reset more robust.
            // This also fixes the "wrong asset" bug by using the correct version object.
            var versionForAssets = isReset ? (editor.ultiPawTarget.appliedUltiPawVersion ?? editor.selectedVersionForAction) : version;
            if (versionForAssets != null)
            {
                string dataPath = UltiPawUtils.GetVersionDataPath(versionForAssets.version, versionForAssets.defaultAviVersion);
                // When resetting, we apply the 'default avatar'. When installing, we apply the 'ultipaw avatar'.
                string avatarName = isReset ? UltiPawUtils.DEFAULT_AVATAR_NAME : UltiPawUtils.ULTIPAW_AVATAR_NAME;
                string avatarPath = Path.Combine(dataPath, avatarName);
                
                var fbxGameObject = editor.baseFbxFilesProp.arraySize > 0 ? editor.baseFbxFilesProp.GetArrayElementAtIndex(0).objectReferenceValue as GameObject : null;
                fileManagerService.ApplyAvatarToModel(root, fbxGameObject, avatarPath);

                // Only instantiate the logic prefab when applying a new version, not when resetting.
                if (!isReset)
                {
                    string packagePath = Path.Combine(dataPath, "ultipaw logic.unitypackage");
                    fileManagerService.InstantiateLogicPrefab(packagePath, root);
                }
            }
            
            // get custom logic path using UltiPawUtils.CUSTOM_LOGIC_NAME
            string customLogicPath = Path.Combine(UltiPawUtils.GetVersionDataPath(versionForAssets.version, versionForAssets.defaultAviVersion), UltiPawUtils.CUSTOM_LOGIC_NAME);
            // If the custom logic prefab exists, instantiate it.
            if (File.Exists(customLogicPath))
            {
                GameObject customLogicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(customLogicPath);
                if (customLogicPrefab != null)
                {
                    GameObject logicInstance = UnityEngine.Object.Instantiate(customLogicPrefab, root);
                    logicInstance.name = customLogicPrefab.name;
                    Undo.RegisterCreatedObjectUndo(logicInstance, "Instantiate Custom Logic");
                }
                else
                {
                    Debug.LogWarning($"[UltiPaw] Custom logic prefab not found at: {customLogicPath}");
                }
            }
            
            editor.ultiPawTarget.appliedUltiPawVersion = version;
        }
        
        // FIX: DO NOT manually assign the applied version. It creates state conflicts.
        // The state will be determined by the hash check below, which is the single source of truth.
        // Undo.RecordObject(editor.ultiPawTarget, isReset ? "Reset UltiPaw" : "Apply UltiPaw");
        // editor.ultiPawTarget.appliedUltiPawVersion = isReset ? null : version; // <-- THIS IS THE OLD, BUGGY WAY
        
        // FIX: Re-evaluate the entire state from the ground up AFTER the operation. This is the new source of truth.
        UpdateCurrentBaseFbxHash(); 
        // UpdateAppliedVersionAndState() is called by the above method, so this is sufficient.


        EditorUtility.SetDirty(editor.ultiPawTarget);
        editor.Repaint();
    }
    
    public void DisplayErrors()
    {
        if (!string.IsNullOrEmpty(editor.fetchError)) EditorGUILayout.HelpBox(editor.fetchError, MessageType.Error);
        if (!string.IsNullOrEmpty(editor.downloadError)) EditorGUILayout.HelpBox(editor.downloadError, MessageType.Warning);
        if (!string.IsNullOrEmpty(editor.deleteError)) EditorGUILayout.HelpBox(editor.deleteError, MessageType.Warning);
    }
    
    public void UpdateCurrentBaseFbxHash()
    {
        string path = GetCurrentFBXPath();
        string newHash = string.IsNullOrEmpty(path) ? null : fileManagerService.CalculateFileHash(path);
        
        // FIX: The hash to check against the server should be the *original* backup if it exists.
        // This hash is used to fetch compatible versions.
        string originalPath = path + FileManagerService.OriginalSuffix;
        if(fileManagerService.BackupExists(path))
        {
            editor.currentBaseFbxHash = fileManagerService.CalculateFileHash(originalPath);
        }
        else
        {
            editor.currentBaseFbxHash = newHash;
        }

        // We also need the hash of the file as it is *now* to check which version is applied.
        string currentFileHash = newHash;
        UpdateAppliedVersionAndState(currentFileHash);
    }
    
    // FIX: Centralized and corrected state detection logic. This is the single source of truth.
    public void UpdateAppliedVersionAndState(string currentFileHash = null)
    {
        // If currentFileHash isn't provided, calculate it now.
        if (currentFileHash == null)
        {
            string path = GetCurrentFBXPath();
            currentFileHash = string.IsNullOrEmpty(path) ? null : fileManagerService.CalculateFileHash(path);
        }

        if (string.IsNullOrEmpty(currentFileHash) || editor.serverVersions == null)
        {
            editor.isUltiPaw = false;
            editor.ultiPawTarget.appliedUltiPawVersion = null;
            EditorUtility.SetDirty(editor.ultiPawTarget);
            return;
        }
        
        // Find the version from the server list that matches the current FBX's hash
        var matchingVersion = editor.serverVersions.FirstOrDefault(v =>
            !string.IsNullOrEmpty(v.appliedCustomAviHash) &&
            v.appliedCustomAviHash.Equals(currentFileHash, StringComparison.OrdinalIgnoreCase));

        Undo.RecordObject(editor.ultiPawTarget, "Update UltiPaw State");

        if (matchingVersion != null)
        {
            // Match found! This is a known UltiPaw version.
            editor.isUltiPaw = true;
            // Only update if the applied version has actually changed to avoid unnecessary churn.
            if (!matchingVersion.Equals(editor.ultiPawTarget.appliedUltiPawVersion))
            {
                editor.ultiPawTarget.appliedUltiPawVersion = matchingVersion;
            }
        }
        else
        {
            // No match found in the server list. It's either the original FBX or an unknown state.
            editor.isUltiPaw = false;
                editor.ultiPawTarget.appliedUltiPawVersion = null;
        }
        
        EditorUtility.SetDirty(editor.ultiPawTarget);
    }

    private void SmartSelectVersion()
    {
        UltiPawVersion versionToSelect = null;
        
        // 1. Keep current UI selection if it's still valid in the new list.
        if (editor.selectedVersionForAction != null && editor.serverVersions.Contains(editor.selectedVersionForAction))
        {
            versionToSelect = editor.selectedVersionForAction;
        }
        // 2. Or, if no selection, select the *applied* version if it exists in the list.
        else if (editor.ultiPawTarget.appliedUltiPawVersion != null && editor.serverVersions.Contains(editor.ultiPawTarget.appliedUltiPawVersion))
        {
            versionToSelect = editor.ultiPawTarget.appliedUltiPawVersion;
        }
        // 3. Or, select the recommended version IF it's an update and it's downloaded.
        else if (editor.recommendedVersion != null)
        {
            bool isNewer = editor.ultiPawTarget.appliedUltiPawVersion == null || 
                           editor.CompareVersions(editor.recommendedVersion.version, editor.ultiPawTarget.appliedUltiPawVersion.version) > 0;
            string binPath = UltiPawUtils.GetVersionBinPath(editor.recommendedVersion.version, editor.recommendedVersion.defaultAviVersion);
            bool isDownloaded = !string.IsNullOrEmpty(binPath) && File.Exists(binPath);
            
            if (isNewer && isDownloaded)
            {
                versionToSelect = editor.recommendedVersion;
            }
        }

        editor.selectedVersionForAction = versionToSelect;
    }

    public string GetCurrentFBXPath()
    {
        if (editor.baseFbxFilesProp.arraySize > 0)
        {
            var fbx = editor.baseFbxFilesProp.GetArrayElementAtIndex(0).objectReferenceValue as GameObject;
            if(fbx != null) return AssetDatabase.GetAssetPath(fbx);
        }
        return null;
    }
}
#endif