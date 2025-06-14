#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq; // Keep Linq
using IEditorOnly = VRC.SDKBase.IEditorOnly;
using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters; // Needed for Version comparison

[JsonObject(MemberSerialization.OptIn)]
public class UltiPawVersionResponse
{
    [JsonProperty] public string recommendedVersion;
    [JsonProperty] public List<UltiPawVersion> versions;
}

[JsonObject(MemberSerialization.OptIn)]
public class UltiPawVersion : IEquatable<UltiPawVersion> // Add IEquatable for easier comparison
{
    [JsonProperty] public string version; // UltiPaw Version (e.g., "1.2.6", "0.1") - Matches JSON example
    [JsonProperty] public string defaultAviVersion; // Base FBX Version (e.g., "1.5") - New field
    [JsonProperty] public Scope scope;
    [JsonProperty] public string date;
    [JsonProperty] public string changelog;
    [JsonProperty] public string customAviHash;
    [JsonProperty] public string appliedCustomAviHash;
    [JsonProperty] public string[] defaultAviHash;
    [JsonProperty] public string[] customBlendshapes;
    [JsonProperty] public Dictionary<string, string> dependencies;

    // Implement IEquatable for reliable comparison (e.g., in Linq)
    public bool Equals(UltiPawVersion other)
    {
        if (other == null) return false;
        // Compare key identifiers. Add more fields if needed for uniqueness.
        return version == other.version && defaultAviVersion == other.defaultAviVersion;
    }

    public override int GetHashCode()
    {
        // Generate hash code based on the same key identifiers
        int hash = 17;
        hash = hash * 31 + (version?.GetHashCode() ?? 0);
        hash = hash * 31 + (defaultAviVersion?.GetHashCode() ?? 0);
        return hash;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as UltiPawVersion);
    }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum Scope
{
    [EnumMember(Value = "public")]  PUBLIC,
    [EnumMember(Value = "beta")]    BETA,
    [EnumMember(Value = "alpha")]   ALPHA,
    [EnumMember(Value = "unknown")] UNKNOWN
}


public class UltiPaw : MonoBehaviour, IEditorOnly
{
    // Script version constant (used for server requests)
    public const string SCRIPT_VERSION = "0.0.2";

    // If true, user manually assigns the base FBX; otherwise auto-detect.
    public bool specifyCustomBaseFbx = false;

    // List of base FBX files – primarily using the first one.
    public List<GameObject> baseFbxFiles = new List<GameObject>();

    // Mapping from a base FBX file path to its calculated hash. (Maintained for potential future use, but primary hash is calculated directly)
    public Dictionary<string, string> winterpawFbxHashes = new Dictionary<string, string>();

    // The path (set via version management UI) to the selected UltiPaw .bin file.
    [HideInInspector] public string selectedUltiPawBinPath;

    // The active version details (selected via the integrated version manager UI).
    [HideInInspector] public UltiPawVersion activeUltiPawVersion = null; // This is the one SELECTED in the UI
    [HideInInspector] public UltiPawVersion appliedUltiPawVersion = null;

    [HideInInspector] public bool isUltiPaw = false; // Reflects if FBX matches appliedUltiPawVersion.customAviHash

    // Blendshape settings – these will be updated from the selected version.
    [HideInInspector] public List<string> blendShapeNames = new List<string>();
    [HideInInspector] public List<float> blendShapeValues = new List<float>(); // Values range 0 to 100.

    // --- Internal State ---
    [HideInInspector] public string currentBaseFbxPath = null; // Cache the path
    [HideInInspector] public string currentBaseFbxHash = null; // Cache the hash
    [HideInInspector] public string currentOriginalBaseFbxHash = null; // Cache the hash
    [HideInInspector] public List<UltiPawVersion> serverVersions = new List<UltiPawVersion>();

    
    [HideInInspector] public bool isCreatorMode = false; 
    [HideInInspector] public GameObject customFbxForCreator; // Make sure this is a GameObject type for FBX
    [HideInInspector] public Avatar ultipawAvatarForCreatorProp; // Path to the custom FBX for creator mode
    [HideInInspector] public GameObject avatarLogicPrefab;    // Make sure this is a GameObject type for Prefab
    [HideInInspector] public const string ORIGINAL_SUFFIX = ".old"; // Suffix for backup files

    // Called when script is loaded or a value changes in the Inspector
    private void OnValidate()
    {
#if UNITY_EDITOR
        // Attempt to auto-detect if needed and possible
        if (!specifyCustomBaseFbx && (baseFbxFiles == null || baseFbxFiles.Count == 0 || baseFbxFiles[0] == null))
        {
            // Delay auto-detection slightly if called too early during scene load/prefab instantiation
             EditorApplication.delayCall += AutoDetectBaseFbxViaHierarchy;
        }
        else if (baseFbxFiles.Count > 0 && baseFbxFiles[0] != null)
        {
            // Update cached path if manually assigned FBX changes
            string newPath = AssetDatabase.GetAssetPath(baseFbxFiles[0]);
            if (newPath != currentBaseFbxPath)
            {
                currentBaseFbxPath = newPath;
                // Hash calculation will be triggered by the editor script when needed
                // We might need to re-evaluate isUltiPaw state here if path changes
                // but hash calculation is expensive, defer it.
            }
        }
        else
        {
            currentBaseFbxPath = null; // Clear path if FBX is removed
            // If path becomes null, likely not UltiPaw anymore
            // Maybe update hash and state here?
            // UpdateCurrentBaseFbxHash(); // Could trigger hash calc -> null
            // UpdateIsUltiPawState();
        }
#endif
        // Ensure the blendshape slider list matches the blendshape names.
        SyncBlendshapeLists();

#if UNITY_EDITOR
        // Update the isUltiPaw state based on the persisted applied version and current hash
        // Do this check less frequently if performance becomes an issue
        // UpdateCurrentBaseFbxHash(); // Avoid hashing here if possible
        // UpdateIsUltiPawState(); // Check based on existing hash and applied version
#endif
    }

    // Called when the component is enabled
    private void OnEnable()
    {
#if UNITY_EDITOR
        // Ensure detection runs when component becomes active in editor
        if (!specifyCustomBaseFbx && (baseFbxFiles == null || baseFbxFiles.Count == 0 || baseFbxFiles[0] == null))
        {
             EditorApplication.delayCall += AutoDetectBaseFbxViaHierarchy;
        }
        else if (baseFbxFiles.Count > 0 && baseFbxFiles[0] != null)
        {
            currentBaseFbxPath = AssetDatabase.GetAssetPath(baseFbxFiles[0]);
            // Don't hash here unless necessary, OnInspectorGUI will handle it
        }

        // Check consistency on enable
        // UpdateCurrentBaseFbxHash(); // Avoid hashing here
        UpdateIsUltiPawState();
#endif
    }


    // Syncs the blendShapeValues list count to match blendShapeNames
    public void SyncBlendshapeLists()
    {
        if (blendShapeNames == null) blendShapeNames = new List<string>();
        if (blendShapeValues == null) blendShapeValues = new List<float>();

        while (blendShapeValues.Count < blendShapeNames.Count)
            blendShapeValues.Add(0f);
        while (blendShapeValues.Count > blendShapeNames.Count)
            blendShapeValues.RemoveAt(blendShapeValues.Count - 1);
    }

    // Tries to find the FBX by looking for a "Body" SkinnedMeshRenderer under the root
    public void AutoDetectBaseFbxViaHierarchy()
    {
#if UNITY_EDITOR
        if (this == null || transform == null) return; // Check if component/transform is valid

        Transform root = transform.root;
        if (root == null)
        {
            return;
        }

        // Find SkinnedMeshRenderer named "Body" (case-insensitive search recommended)
        SkinnedMeshRenderer bodySmr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true) // Include inactive
                                          .FirstOrDefault(smr => smr.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));

        if (bodySmr == null || bodySmr.sharedMesh == null)
        {
            return;
        }

        string meshPath = AssetDatabase.GetAssetPath(bodySmr.sharedMesh);
        if (string.IsNullOrEmpty(meshPath))
        {
            return;
        }

        // Often, the mesh path IS the FBX path. Load the GameObject asset at this path.
        GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath);

        if (fbxAsset != null && AssetImporter.GetAtPath(meshPath) is ModelImporter)
        {
            bool changed = false;
            if (baseFbxFiles.Count == 0)
            {
                baseFbxFiles.Add(fbxAsset);
                changed = true;
            }
            else if (baseFbxFiles[0] != fbxAsset)
            {
                baseFbxFiles[0] = fbxAsset; // Replace if one exists
                changed = true;
            }

            if (changed)
            {
                currentBaseFbxPath = meshPath; // Update cached path
                Debug.Log($"[UltiPaw] Auto-detected base FBX via hierarchy: {meshPath}");
                EditorUtility.SetDirty(this); // Mark component dirty as we changed baseFbxFiles
                // Trigger hash update and state check after delay to ensure editor is ready
                EditorApplication.delayCall += () => {
                    UpdateCurrentBaseFbxHash();
                    UpdateIsUltiPawState();
                };
            }
        }
#endif
    }

    // Gets the currently assigned or detected base FBX path
    public string GetCurrentBaseFbxPath()
    {
#if UNITY_EDITOR
        if (baseFbxFiles != null && baseFbxFiles.Count > 0 && baseFbxFiles[0] != null)
        {
            // Ensure cached path is up-to-date
            string path = AssetDatabase.GetAssetPath(baseFbxFiles[0]);
            if(path != currentBaseFbxPath) {
                currentBaseFbxPath = path;
            }
            return currentBaseFbxPath;
        }
        // Return cached path even if object is null temporarily, auto-detect might fix it
        return currentBaseFbxPath;
#else
        return null;
#endif
    }

    public string GetCurrentOriginalBaseFbxPath()
    {
        string currentPath = GetCurrentBaseFbxPath();
        if (File.Exists(currentPath + ORIGINAL_SUFFIX))
        {
            // UpdateIsUltiPawState(currentPath + ORIGINAL_SUFFIX);
            return currentPath + ORIGINAL_SUFFIX;
        }
        isUltiPaw = false; // If no backup exists, we assume it's not in the transformed state
        return currentPath;
    }

    public bool UpdateIsUltiPawState(string baseFbxHash = null)
    {
        if (appliedUltiPawVersion == null) 
            throw new CantDetermineUltiPawStateException("appliedUltiPawVersion is null. Cannot determine if the FBX is in the transformed state.");
        
        bool previousState = isUltiPaw;
        if (appliedUltiPawVersion != null && !string.IsNullOrEmpty(appliedUltiPawVersion.customAviHash) && !string.IsNullOrEmpty(currentBaseFbxHash))
        {
            isUltiPaw = serverVersions.Any(v => v.appliedCustomAviHash.Equals(currentBaseFbxHash, StringComparison.OrdinalIgnoreCase))
                || (baseFbxHash ?? currentBaseFbxHash).Equals(appliedUltiPawVersion.customAviHash, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // If no applied version is known, or hash is missing, assume it's not the transformed state
            isUltiPaw = false;
        }

        // Return true if the state changed
        return isUltiPaw != previousState;
    }



    public string GetCurrentBaseFbxHash()
    {
        string res = string.IsNullOrEmpty(currentOriginalBaseFbxHash) ?
            currentBaseFbxHash
            : currentOriginalBaseFbxHash;
        if (string.IsNullOrEmpty(res))
            return null;
        return res;
    }
    // Calculates and updates the hash for the current base FBX
    public bool UpdateCurrentBaseFbxHash()
    {
#if UNITY_EDITOR
        string path = GetCurrentBaseFbxPath();
        string pathOld = GetCurrentOriginalBaseFbxPath();
        
        string oldHash = currentBaseFbxHash; // Store old hash

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            currentBaseFbxHash = UltiPawUtils.CalculateFileHash(path);
            if (path != pathOld)
            {
                currentOriginalBaseFbxHash = UltiPawUtils.CalculateFileHash(pathOld);
            }

            try
            {
                UpdateIsUltiPawState();
                if (!isUltiPaw)
                {
                    Debug.LogWarning($"[UltiPaw] Base FBX file has been replaced by another one. Tell the user, he can try to re-fetch the versions for this specific FBX.");
                    EditorUtility.DisplayDialog("UltiPaw - File Replacement Detected",
                        "The base FBX file has been replaced. Please re-fetch UltiPaw versions for this one.",
                        "OK");
                    // if (path != pathOld) // The FBX seem to have externally been replaced by an unknown one
                    // {
                    //     // TODO: make it so the software can also check if the fbx in old path is a valid winterpaw and automatically reestablish it if the user wants
                    // }
                }
            }
            catch (CantDetermineUltiPawStateException e)
            {
                // nothing particular to do yet
            };
            
            if (!string.IsNullOrEmpty(currentBaseFbxHash))
            {
                winterpawFbxHashes[path] = currentBaseFbxHash;
            }
        }
        else
        {
            currentBaseFbxHash = null; // Clear hash if file invalid
            winterpawFbxHashes.Clear();
        }

        // Check if hash actually changed
        bool hashChanged = oldHash != currentBaseFbxHash;

        // If hash changed, update the isUltiPaw state
        if (hashChanged)
        {
            try
            {
                UpdateIsUltiPawState();
            } catch (CantDetermineUltiPawStateException e)
            {
                // nothing particular to do yet
            }
        }
        return hashChanged; // Return whether the hash value changed
#else
        return false;
#endif
    }
    
    // update appliedUltiPawVersion with a list serverVersions
    public bool UpdateAppliedUltiPawVersion(List<UltiPawVersion> serverVersions)
    {
        if (serverVersions == null || serverVersions.Count == 0)
        {
            Debug.LogWarning("[UltiPaw] No server versions available to update applied version.");
            return false; // need to get versions from the server
        }

        // Check if the current hash matches any version's customAviHash
        foreach (var version in serverVersions)
        {
            if (currentBaseFbxHash != null && currentBaseFbxHash.Equals(version.appliedCustomAviHash, StringComparison.OrdinalIgnoreCase))
            {
                appliedUltiPawVersion = version; // Update applied version
                isUltiPaw = true; // Update isUltiPaw state
                return true; // Exit loop on first match
            }
        }
        if(isUltiPaw) Debug.LogWarning("[UltiPaw] No matching version found for current hash. Current hash: " + currentBaseFbxHash);
        return true;
    }

#if UNITY_EDITOR
    // Transform the base FBX using XOR with the selected UltiPaw .bin.
    public bool ApplyUltiPaw() // Return bool indicating success
    {
        // --- PRE-TRANSFORMATION ---
        // ** NEW ** Remove existing logic prefab before applying a new one
        Transform root = transform.root;
        Transform existingLogic = root.Find("ultipaw logic");
        if (existingLogic != null)
        {
            Undo.DestroyObjectImmediate(existingLogic.gameObject);
            Debug.Log("[UltiPaw] Removed existing 'ultipaw logic' GameObject.");
        }

        string baseFbxPath = GetCurrentOriginalBaseFbxPath();
        if (string.IsNullOrEmpty(baseFbxPath) || !File.Exists(baseFbxPath))
        {
            EditorUtility.DisplayDialog("UltiPaw Error", "Base FBX file is not assigned or not found.", "OK");
            Debug.LogError("[UltiPaw] No valid base FBX file available for transformation.");
            return false; // Indicate failure
        }
        if (string.IsNullOrEmpty(selectedUltiPawBinPath) || !File.Exists(selectedUltiPawBinPath))
        {
            EditorUtility.DisplayDialog("UltiPaw Error", "Selected UltiPaw version (.bin file) is missing.\nPlease select and potentially re-download the version.", "OK");
            Debug.LogError("[UltiPaw] Selected UltiPaw bin file is missing.");
            return false; // Indicate failure
        }
        // Use activeUltiPawVersion (the one selected in UI) for the transformation process
        if (activeUltiPawVersion == null)
        {
             EditorUtility.DisplayDialog("UltiPaw Error", "No UltiPaw version selected in the UI. Please select a version.", "OK");
             Debug.LogError("[UltiPaw] Active (selected) UltiPaw version details are null.");
             return false; // Indicate failure
        }

        // --- HASH VERIFICATION ---
        UpdateCurrentBaseFbxHash(); // Ensure hash is current before check
        bool baseHashMatch = activeUltiPawVersion.defaultAviHash.Any(v => v != null && (
            v.Equals(currentBaseFbxHash, StringComparison.OrdinalIgnoreCase) ||
            v.Equals(currentOriginalBaseFbxHash, StringComparison.OrdinalIgnoreCase)
        ));
        
        if (!baseHashMatch)
        {
            string message = $"Hash mismatch detected for applying version '{activeUltiPawVersion.version}':\n\n";
            message += $"Base FBX Hash: MISMATCH\n Neither of :\n- {currentBaseFbxHash ?? "N/A"}\n- {currentOriginalBaseFbxHash ?? "N/A"}\n\nMatches with : \n{string.Join("\n", activeUltiPawVersion.defaultAviHash.Select(hash => $"- {hash}"))}";
            message += "\n\nFirst, how did you arrived here without doing crazy weird stuff ?? And then, if you continue anyway the resulting file will be corrupted for sure. Do it only if you're the orb";

            if (!EditorUtility.DisplayDialog("UltiPaw - Hash Mismatch", message, "Continue Anyway", "Cancel"))
            {
                Debug.LogWarning("[UltiPaw] Transformation cancelled due to hash mismatch.");
                return false; // Indicate failure
            }
            Debug.LogWarning("[UltiPaw] Proceeding with transformation despite hash mismatch.");
        }
        else {
             Debug.Log("[UltiPaw] Base FBX hash verified successfully.");
        }

        // --- FBX TRANSFORMATION ---
        try
        {
            byte[] baseData = File.ReadAllBytes(baseFbxPath);
            byte[] binData = File.ReadAllBytes(selectedUltiPawBinPath);

            // Perform XOR operation.
            byte[] transformedData = new byte[binData.Length]; // Result size matches bin data
            for (int i = 0; i < binData.Length; i++)
            {
                transformedData[i] = (byte)(binData[i] ^ baseData[i % baseData.Length]);
            }
            
            bool shouldCreateBackup = !baseFbxPath.EndsWith(ORIGINAL_SUFFIX, StringComparison.OrdinalIgnoreCase);
            if (shouldCreateBackup)
            {
                // Backup original.
                string backupPath = baseFbxPath + ".old";
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(baseFbxPath, backupPath);
                Debug.Log($"[UltiPaw] Backed up original FBX to: {backupPath}");
            }


            string fbxPathToOverride = shouldCreateBackup ? baseFbxPath : baseFbxPath.Remove(baseFbxPath.IndexOf(ORIGINAL_SUFFIX));
            // Write transformed FBX.
            File.WriteAllBytes(fbxPathToOverride, transformedData);
            Debug.Log($"[UltiPaw] Wrote transformed data to: {fbxPathToOverride}");

            // Apply external avatar using the ultipaw avatar file from the selected version.
            string versionDataPath = UltiPawUtils.GetVersionDataPath(activeUltiPawVersion.version, activeUltiPawVersion.defaultAviVersion);
            string ultiAvatarFullPath = Path.Combine(versionDataPath, UltiPawUtils.ULTIPAW_AVATAR_NAME).Replace("\\", "/");

            bool avatarApplied = false;
            if (File.Exists(ultiAvatarFullPath))
            {
                avatarApplied = UltiPawAvatarUtility.ApplyExternalAvatar(baseFbxFiles[0], ultiAvatarFullPath); // Check return value
                if (avatarApplied) SetRootAnimatorAvatar(ultiAvatarFullPath);
            }
            else
            {
                Debug.LogWarning($"[UltiPaw] UltiPaw avatar file not found or path not set in version details. Path checked: {ultiAvatarFullPath}");
            }

            // --- ** NEW ** Install Avatar Logic ---
            string packagePath = Path.Combine(versionDataPath, "ultipaw logic.unitypackage").Replace("\\", "/");
            if (File.Exists(packagePath))
            {
                Debug.Log($"[UltiPaw] Found logic package. Importing from: {packagePath}");
                AssetDatabase.ImportPackage(packagePath, false); // Import silently

                // After import, the prefab should be available at its source path within the version folder
                string prefabPath = Path.Combine(versionDataPath, "ultipaw logic.prefab").Replace("\\", "/");
                if(File.Exists(prefabPath))
                {
                    GameObject logicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if(logicPrefab != null)
                    {
                        GameObject newLogicInstance = (GameObject)PrefabUtility.InstantiatePrefab(logicPrefab, root);
                        newLogicInstance.name = "ultipaw logic"; // Standardize name for future removal
                        Undo.RegisterCreatedObjectUndo(newLogicInstance, "Install UltiPaw Logic");
                        Debug.Log($"[UltiPaw] Instantiated 'ultipaw logic' as a child of '{root.name}'.");
                    }
                    else
                    {
                        Debug.LogWarning($"[UltiPaw] Imported package but could not load 'ultipaw logic.prefab' from '{prefabPath}'.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[UltiPaw] Logic package '{packagePath}' exists, but the expected 'ultipaw logic.prefab' was not found inside the version folder after import.");
                }
            }
            else
            {
                Debug.Log("[UltiPaw] No 'ultipaw logic.unitypackage' found for this version. Skipping logic installation.");
            }


            // --- Success State Update ---
            Undo.RecordObject(this, "Apply UltiPaw Version");
            appliedUltiPawVersion = activeUltiPawVersion;
            UpdateCurrentBaseFbxHash();
            UpdateIsUltiPawState();
            EditorUtility.SetDirty(this);
            AssetDatabase.Refresh();
            Debug.Log($"[UltiPaw] Transformation to version {activeUltiPawVersion.version} complete.");
            return true; // Indicate success

        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UltiPaw] Transformation failed: {e}");
            EditorUtility.DisplayDialog("UltiPaw Error", $"An error occurred during transformation:\n{e.Message}\n\nCheck the console. Attempting to restore backup.", "OK");
            // Attempt restore on failure
            string backupPath = baseFbxPath + ".old";
             if (File.Exists(backupPath))
             {
                 try {
                     if (File.Exists(baseFbxPath)) File.Delete(baseFbxPath);
                     File.Move(backupPath, baseFbxPath);
                     AssetDatabase.ImportAsset(baseFbxPath, ImportAssetOptions.ForceUpdate);
                     Debug.LogWarning($"[UltiPaw] Restored backup due to error: {baseFbxPath}");
                 } catch (System.Exception restoreEx) {
                     Debug.LogError($"[UltiPaw] Failed to restore backup after error: {restoreEx.Message}");
                 }
             }

             Undo.RecordObject(this, "Apply UltiPaw Version Failed");
             UpdateCurrentBaseFbxHash();
             UpdateIsUltiPawState();
             EditorUtility.SetDirty(this);
             AssetDatabase.Refresh();
             return false; // Indicate failure
        }
    }

    // Reset by restoring the backup and applying the default avatar.
    public bool ResetIntoWinterPaw() // Return bool indicating success
    {
        // ** NEW ** Remove existing logic prefab before resetting
        Transform root = transform.root;
        Transform existingLogic = root.Find("ultipaw logic");
        if (existingLogic != null)
        {
            Undo.DestroyObjectImmediate(existingLogic.gameObject);
            Debug.Log("[UltiPaw] Removed existing 'ultipaw logic' GameObject during reset.");
        }

        string baseFbxPath = GetCurrentBaseFbxPath();
        if (string.IsNullOrEmpty(baseFbxPath))
        {
             Debug.LogError("[UltiPaw] No base FBX file available for reset.");
             return false;
        }

        string backupPath = baseFbxPath + ORIGINAL_SUFFIX;
        bool restored = false;
        if (File.Exists(backupPath))
        {
            try
            {
                if (File.Exists(baseFbxPath)) File.Delete(baseFbxPath);
                File.Move(backupPath, baseFbxPath);
                Debug.Log($"[UltiPaw] Restored original FBX from: {backupPath}");
                AssetDatabase.ImportAsset(baseFbxPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();
                restored = true;
            }
            catch (System.Exception e)
            {
                 Debug.LogError($"[UltiPaw] Failed to restore {baseFbxPath} from backup: {e.Message}");
                 EditorUtility.DisplayDialog("Restore Error", $"Failed to restore {Path.GetFileName(baseFbxPath)}.\n{e.Message}\n\nYou may need to do it manually.", "OK");
            }
        }
        else
        {
            Debug.LogWarning("[UltiPaw] No backup file found to restore. Attempting to apply default avatar to current FBX.");
            restored = true; // Treat as "restored" for the purpose of applying default avatar
        }

        if (restored)
        {
            UltiPawVersion versionForDefault = appliedUltiPawVersion ?? activeUltiPawVersion;

            if (versionForDefault != null)
            {
                string versionDataPath = UltiPawUtils.GetVersionDataPath(versionForDefault.version, versionForDefault.defaultAviVersion);
                string defaultAvatarFullPath = Path.Combine(versionDataPath, UltiPawUtils.DEFAULT_AVATAR_NAME).Replace("\\", "/");

                if (File.Exists(defaultAvatarFullPath))
                {
                    if (UltiPawAvatarUtility.ApplyExternalAvatar(baseFbxFiles[0], defaultAvatarFullPath))
                    {
                        SetRootAnimatorAvatar(defaultAvatarFullPath);
                    }
                    else
                    {
                        Debug.LogError("[UltiPaw] Failed to apply default avatar during reset.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[UltiPaw] Default avatar file not found for version {versionForDefault.version}. Path checked: {defaultAvatarFullPath}");
                }
            }
            else
            {
                Debug.LogWarning("[UltiPaw] No version details available to find the default avatar path for reset.");
            }

            Undo.RecordObject(this, "Reset UltiPaw");
            appliedUltiPawVersion = null;
            UpdateCurrentBaseFbxHash();
            UpdateIsUltiPawState();
            EditorUtility.SetDirty(this);
            AssetDatabase.Refresh();
            Debug.Log("[UltiPaw] Reset process finished.");
            return true;
        }
        else
        {
            AssetDatabase.Refresh();
            return false;
        }
    }
#endif

    // Update the Avatar field on the root's Animator.
    private void SetRootAnimatorAvatar(string avatarAssetPath)
    {
#if UNITY_EDITOR
        if (this == null || transform == null) return;
        Transform root = transform.root;
        if (root == null) return;

        GameObject rootObject = root.gameObject;
        Animator animator = rootObject.GetComponent<Animator>();
        if (animator == null)
        {
            Undo.RecordObject(rootObject, "Add Animator Component");
            animator = rootObject.AddComponent<Animator>();
            EditorUtility.SetDirty(rootObject);
            Debug.Log($"[UltiPaw] Animator component added to root object '{rootObject.name}'.");
        }

        // Load avatar if provided
        Avatar avatar = null;
        if (!string.IsNullOrEmpty(avatarAssetPath) && File.Exists(avatarAssetPath))
        {
            avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarAssetPath);
        }

        // Only set avatar if different
        if (animator.avatar != avatar)
        {
            Undo.RecordObject(animator, "Set Root Animator Avatar");
            animator.avatar = avatar;
            EditorUtility.SetDirty(animator);
            Debug.Log($"[UltiPaw] Root Animator's avatar set to {(avatar != null ? avatar.name : "None")}.");
        }
#endif
    }

    public SkinnedMeshRenderer GetBodySkinnedMeshRenderer()
    {
#if UNITY_EDITOR
        if (this == null || transform == null) return null;
        Transform root = transform.root;
        if (root == null) return null;

        return root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                     .FirstOrDefault(s => s.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));
#endif
        return null;
    }
}

public class CantDetermineUltiPawStateException : Exception
{
    public CantDetermineUltiPawStateException(string message) : base(message) { }
}