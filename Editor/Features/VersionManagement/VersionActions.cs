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

    internal IEnumerator ApplyOrResetCoroutine(UltiPawVersion version, bool isReset)
    {
        var root = editor.ultiPawTarget.transform.root;
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
                if (!File.Exists(binPath)) throw new FileNotFoundException("Apply failed: .bin file not found. Please download or build it first.");

                string originalFbxPath = fbxPath.EndsWith(FileManagerService.OriginalSuffix) ? fbxPath : fbxPath + FileManagerService.OriginalSuffix;
                if (!File.Exists(originalFbxPath))
                {
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
            UltiPawLogger.LogError($"[UltiPaw] Operation failed: {e.Message}");
            if(!isReset && fileManagerService.BackupExists(fbxPath)) fileManagerService.RestoreBackup(fbxPath);
        }
        
        if (success)
        {
            // --- CRITICAL FIX FOR RE-IMPORT ---
            // Force Unity to re-import the specific FBX we just changed.
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            
            // Wait until Unity has finished re-importing and compiling.
            // This is essential to prevent race conditions.
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                yield return null;
            }
            // --- END CRITICAL FIX ---
            
            var versionForAssets = isReset ? (editor.ultiPawTarget.appliedUltiPawVersion ?? editor.selectedVersionForAction) : version;
            if (versionForAssets != null)
            {
                string dataPath = UltiPawUtils.GetVersionDataPath(versionForAssets.version, versionForAssets.defaultAviVersion);
                string avatarName = isReset ? UltiPawUtils.DEFAULT_AVATAR_NAME : UltiPawUtils.ULTIPAW_AVATAR_NAME;
                string avatarPath = Path.Combine(dataPath, avatarName);
                
                var fbxGameObject = editor.baseFbxFilesProp.arraySize > 0 ? editor.baseFbxFilesProp.GetArrayElementAtIndex(0).objectReferenceValue as GameObject : null;
                fileManagerService.ApplyAvatarToModel(root, fbxGameObject, avatarPath);

                if (!isReset)
                {
                    string packagePath = Path.Combine(dataPath, "ultipaw logic.unitypackage");
                    fileManagerService.InstantiateLogicPrefab(packagePath, root);
                }
            }
            
            string customLogicPath = Path.Combine(UltiPawUtils.GetVersionDataPath(versionForAssets.version, versionForAssets.defaultAviVersion), UltiPawUtils.CUSTOM_LOGIC_NAME);
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
                    UltiPawLogger.LogWarning($"[UltiPaw] Custom logic prefab not found at: {customLogicPath}");
                }
            }
            
            editor.ultiPawTarget.appliedUltiPawVersion = version;
            
            // Apply or remove dynamic normals based on version feature flags
            bool hasDynamicNormalBody = !isReset && version != null && (version.extraCustomization?.Contains("dynamicNormalBody") ?? false);
            bool hasDynamicNormalFlexing = !isReset && version != null && (version.extraCustomization?.Contains("dynamicNormalFlexing") ?? false);
            bool shouldApplyDynamicNormals = (hasDynamicNormalBody || hasDynamicNormalFlexing) && editor.ultiPawTarget.useDynamicNormals;
            
            var dynamicNormalsService = new DynamicNormalsService(editor);
            
            if (shouldApplyDynamicNormals)
            {
                // Apply dynamic normals when version has the feature and it's enabled globally
                dynamicNormalsService.Apply(hasDynamicNormalBody, hasDynamicNormalFlexing);
            }
            else
            {
                // Remove dynamic normals when switching to a version without the feature or resetting
                dynamicNormalsService.Remove();
            }
            
            // Apply or remove custom veins based on version feature flag
            bool hasCustomVeins = !isReset && version != null && (version.extraCustomization?.Contains("customVeins") ?? false);
            var materialService = new MaterialService(root);
            
            if (hasCustomVeins)
            {
                // Apply custom veins
                string versionFolder = UltiPawUtils.GetVersionDataPath(version.version, version.defaultAviVersion);
                string veinsNormalPath = Path.Combine(versionFolder, "veins normal.png").Replace("\\", "/");
                
                if (File.Exists(veinsNormalPath))
                {
                    bool veinsApplied = materialService.SetDetailNormalMap("Body", veinsNormalPath);
                    if (veinsApplied)
                    {
                        materialService.SetDetailNormalOpacity("Body", 1.0f);
                        // Sync the toggle state with the applied state
                        EditorPrefs.SetBool(CustomVeinsDrawer.CUSTOM_VEINS_PREF_KEY, true);
                        UltiPawLogger.Log($"[VersionActions] Custom veins applied from version {version.version}");
                    }
                }
                else
                {
                    UltiPawLogger.LogWarning($"[VersionActions] Custom veins file not found at: {veinsNormalPath}");
                }
            }
            else
            {
                // Remove custom veins when switching to a version without the feature or resetting
                materialService.RemoveDetailNormalMap("Body");
                // Sync the toggle state - set to false when removing veins
                EditorPrefs.SetBool(CustomVeinsDrawer.CUSTOM_VEINS_PREF_KEY, false);
                UltiPawLogger.Log("[VersionActions] Custom veins removed");
            }
            
            // Apply blendshape default values and clear custom overrides
            if (!isReset && version != null && version.customBlendshapes != null && version.customBlendshapes.Length > 0)
            {
                // Clear all custom overrides when switching versions
                editor.ultiPawTarget.customBlendshapeOverrideNames.Clear();
                editor.ultiPawTarget.customBlendshapeOverrideValues.Clear();
                
                // Find the Body mesh
                var bodyMesh = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(s => s.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));
                
                if (bodyMesh?.sharedMesh != null)
                {
                    // Apply default values from the version
                    foreach (var entry in version.customBlendshapes)
                    {
                        int blendshapeIndex = bodyMesh.sharedMesh.GetBlendShapeIndex(entry.name);
                        if (blendshapeIndex >= 0)
                        {
                            float defaultValue = float.TryParse(entry.defaultValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;
                            bodyMesh.SetBlendShapeWeight(blendshapeIndex, defaultValue);
                        }
                    }
                    
                    EditorUtility.SetDirty(bodyMesh);
                    UltiPawLogger.Log($"[VersionActions] Applied {version.customBlendshapes.Length} blendshape default values");
                }
            }
            else if (isReset)
            {
                // Clear custom overrides when resetting
                editor.ultiPawTarget.customBlendshapeOverrideNames.Clear();
                editor.ultiPawTarget.customBlendshapeOverrideValues.Clear();
            }
        }
        
        UpdateCurrentBaseFbxHash();

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
        if (string.IsNullOrEmpty(path))
        {
            editor.currentBaseFbxHash = null;
            UpdateAppliedVersionAndState(null);
            return;
        }

        // Try to get cached hashes first (non-blocking)
        var hashService = AsyncHashService.Instance;
        string cachedCurrentHash = hashService.GetHashIfCached(path);
        
        string originalPath = path + FileManagerService.OriginalSuffix;
        string cachedOriginalHash = null;
        bool hasBackup = fileManagerService.BackupExists(path);
        
        if (hasBackup)
        {
            cachedOriginalHash = hashService.GetHashIfCached(originalPath);
        }

        // Use cached hashes if available, otherwise start async calculation
        if (cachedCurrentHash != null && (!hasBackup || cachedOriginalHash != null))
        {
            // We have all needed cached hashes - use them immediately
            editor.currentBaseFbxHash = hasBackup ? cachedOriginalHash : cachedCurrentHash;
            UpdateAppliedVersionAndState(cachedCurrentHash);
        }
        else
        {
            // Missing cache - start async hash calculation and use placeholder for now
            editor.currentBaseFbxHash = null; // Will be updated when async calculation completes
            
            // Start async hash calculation in background
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    string currentHash = await hashService.CalculateFileHashAsync(path, null, true);
                    string originalHash = null;
                    
                    if (hasBackup)
                    {
                        originalHash = await hashService.CalculateFileHashAsync(originalPath, null, true);
                    }
                    
                    // Update on main thread when calculation completes
                    AsyncTaskManager.Instance.ExecuteOnMainThread(() =>
                    {
                        editor.currentBaseFbxHash = hasBackup ? originalHash : currentHash;
                        UpdateAppliedVersionAndState(currentHash);
                        editor.Repaint();
                    });
                }
                catch (System.Exception ex)
                {
                    UltiPawLogger.LogError($"[VersionActions] Async hash calculation failed: {ex.Message}");
                }
            });
        }
    }
    
    // FIX: Centralized and corrected state detection logic. This is the single source of truth.
    public void UpdateAppliedVersionAndState(string currentFileHash = null)
    {
        // If currentFileHash isn't provided, try to get it from cache only (non-blocking)
        if (currentFileHash == null)
        {
            string path = GetCurrentFBXPath();
            if (!string.IsNullOrEmpty(path))
            {
                var hashService = AsyncHashService.Instance;
                currentFileHash = hashService.GetHashIfCached(path);
            }
            
            // If we don't have a cached hash, skip state update for now
            // The state will be updated later when the async hash calculation completes
            if (string.IsNullOrEmpty(currentFileHash))
            {
                return;
            }
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
            editor.ultiPawTarget.appliedUltiPawVersion = matchingVersion;
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