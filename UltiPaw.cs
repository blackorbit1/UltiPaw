#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq; // Keep Linq
using IEditorOnly = VRC.SDKBase.IEditorOnly;

[System.Serializable]
public class UltiPawVersionResponse
{
    public string recommendedVersion;
    public List<UltiPawVersion> versions;
}

[System.Serializable]
public class UltiPawVersion
{
    public string version; // UltiPaw Version (e.g., "1.2.6", "0.1") - Matches JSON example
    public string defaultAviVersion; // Base FBX Version (e.g., "1.5") - New field
    public string scope;
    public string date;
    public string changelog;
    public string customAviHash;
    public string defaultAviHash;
    public string[] customBlendshapes;
    public Dictionary<string, string> dependencies;
}


public class UltiPaw : MonoBehaviour, IEditorOnly
{
    // Script version constant (used for server requests)
    public const string SCRIPT_VERSION = "0.1";

    // If true, user manually assigns the base FBX; otherwise auto-detect.
    public bool specifyCustomBaseFbx = false;

    // List of base FBX files – primarily using the first one.
    public List<GameObject> baseFbxFiles = new List<GameObject>();

    // Mapping from a base FBX file path to its calculated hash. (Maintained for potential future use, but primary hash is calculated directly)
    [HideInInspector]
    public Dictionary<string, string> winterpawFbxHashes = new Dictionary<string, string>();

    // The path (set via version management UI) to the selected UltiPaw .bin file.
    [HideInInspector]
    public string selectedUltiPawBinPath;

    // The active version details (selected via the integrated version manager).
    [HideInInspector]
    public UltiPawVersion activeUltiPawVersion = null;

    [HideInInspector] public bool isUltiPaw = false;

    // Blendshape settings – these will be updated from the selected version.
    [HideInInspector] public List<string> blendShapeNames = new List<string>();
    [HideInInspector] public List<float> blendShapeValues = new List<float>(); // Values range 0 to 100.

    // --- Internal State ---
    [HideInInspector] public string currentBaseFbxPath = null; // Cache the path
    [HideInInspector] public string currentBaseFbxHash = null; // Cache the hash


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
            }
        }
        else
        {
            currentBaseFbxPath = null; // Clear path if FBX is removed
        }
#endif
        // Ensure the blendshape slider list matches the blendshape names.
        SyncBlendshapeLists();
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
        }
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
            // Debug.LogWarning("[UltiPaw] Cannot find root transform for auto-detection.");
            return;
        }

        // Find SkinnedMeshRenderer named "Body" (case-insensitive search recommended)
        SkinnedMeshRenderer bodySmr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true) // Include inactive
                                          .FirstOrDefault(smr => smr.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));

        if (bodySmr == null || bodySmr.sharedMesh == null)
        {
            // Debug.LogWarning("[UltiPaw] Could not find 'Body' SkinnedMeshRenderer with a mesh under the root.");
            return;
        }

        string meshPath = AssetDatabase.GetAssetPath(bodySmr.sharedMesh);
        if (string.IsNullOrEmpty(meshPath))
        {
            // Debug.LogWarning("[UltiPaw] Could not determine asset path for the 'Body' mesh.");
            return;
        }

        // Often, the mesh path IS the FBX path. Load the GameObject asset at this path.
        GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath);

        if (fbxAsset != null && AssetImporter.GetAtPath(meshPath) is ModelImporter)
        {
            if (baseFbxFiles.Count == 0)
            {
                baseFbxFiles.Add(fbxAsset);
            }
            else
            {
                baseFbxFiles[0] = fbxAsset; // Replace if one exists
            }
            currentBaseFbxPath = meshPath; // Update cached path
            Debug.Log($"[UltiPaw] Auto-detected base FBX via hierarchy: {meshPath}");
            EditorUtility.SetDirty(this); // Mark component dirty as we changed baseFbxFiles
        }
        else
        {
            // Debug.LogWarning($"[UltiPaw] Asset at mesh path '{meshPath}' is not a valid FBX GameObject.");
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

    // Calculates and updates the hash for the current base FBX
    public bool UpdateCurrentBaseFbxHash()
    {
#if UNITY_EDITOR
        string path = GetCurrentBaseFbxPath();
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            string newHash = UltiPawUtils.CalculateFileHash(path);
            if (newHash != currentBaseFbxHash)
            {
                currentBaseFbxHash = newHash;
                // Update the dictionary as well (though maybe less critical now)
                winterpawFbxHashes.Clear();
                if (!string.IsNullOrEmpty(newHash))
                {
                    winterpawFbxHashes[path] = newHash;
                }
                return true; // Hash changed
            }
        }
        else if (currentBaseFbxHash != null)
        {
            // FBX became invalid, clear hash
            currentBaseFbxHash = null;
            winterpawFbxHashes.Clear();
            return true; // Hash changed (to null)
        }
        return false; // Hash did not change
#else
        return false;
#endif
    }

#if UNITY_EDITOR
    // Transform the base FBX using XOR with the selected UltiPaw .bin.
    public void TurnItIntoUltiPaw()
    {
        string baseFbxPath = GetCurrentBaseFbxPath();
        if (string.IsNullOrEmpty(baseFbxPath) || !File.Exists(baseFbxPath))
        {
            EditorUtility.DisplayDialog("UltiPaw Error", "Base FBX file is not assigned or not found.", "OK");
            Debug.LogError("[UltiPaw] No valid base FBX file available for transformation.");
            return;
        }
        if (string.IsNullOrEmpty(selectedUltiPawBinPath) || !File.Exists(selectedUltiPawBinPath))
        {
            EditorUtility.DisplayDialog("UltiPaw Error", "Selected UltiPaw version (.bin file) is missing.\nPlease select and potentially re-download the version.", "OK");
            Debug.LogError("[UltiPaw] Selected UltiPaw bin file is missing.");
            return;
        }
        if (activeUltiPawVersion == null)
        {
             EditorUtility.DisplayDialog("UltiPaw Error", "No UltiPaw version details loaded. Please select a version.", "OK");
             Debug.LogError("[UltiPaw] Active UltiPaw version details are null.");
             return;
        }

        // --- Hash Verification ---
        UpdateCurrentBaseFbxHash(); // Ensure hash is current
        string currentBinHash = UltiPawUtils.CalculateFileHash(selectedUltiPawBinPath);

        bool baseHashMatch = currentBaseFbxHash != null && currentBaseFbxHash.Equals(activeUltiPawVersion.defaultAviHash, System.StringComparison.OrdinalIgnoreCase);
        bool binHashMatch = currentBinHash != null && currentBinHash.Equals(activeUltiPawVersion.customAviHash, System.StringComparison.OrdinalIgnoreCase);

        if (!baseHashMatch || !binHashMatch)
        {
            string message = $"Hash mismatch detected for selected version '{activeUltiPawVersion.version}':\n\n";
            if (!baseHashMatch) message += $"Base FBX Hash: MISMATCH\n (Expected: {activeUltiPawVersion.defaultAviHash ?? "N/A"}... Found: {currentBaseFbxHash ?? "N/A"}...)\n";
            if (!binHashMatch) message += $"UltiPaw .bin Hash: MISMATCH\n (Expected: {activeUltiPawVersion.customAviHash ?? "N/A"}... Found: {currentBinHash ?? "N/A"}...)\n";
            message += "\nApplying this version might lead to unexpected results or errors. Are you sure you want to continue?";

            if (!EditorUtility.DisplayDialog("UltiPaw - Hash Mismatch", message, "Continue Anyway", "Cancel"))
            {
                Debug.LogWarning("[UltiPaw] Transformation cancelled due to hash mismatch.");
                return;
            }
             Debug.LogWarning("[UltiPaw] Proceeding with transformation despite hash mismatch.");
        }
        else {
             Debug.Log("[UltiPaw] File hashes verified successfully.");
        }

        // --- Transformation ---
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

            // Backup original.
            string backupPath = baseFbxPath + ".old";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(baseFbxPath, backupPath);
            Debug.Log($"[UltiPaw] Backed up original FBX to: {backupPath}");
            
            // --- Stops here --> Error 

            // Write transformed FBX.
            File.WriteAllBytes(baseFbxPath, transformedData);
            Debug.Log($"[UltiPaw] Wrote transformed data to: {baseFbxPath}");

            // Force reimport.
            AssetDatabase.ImportAsset(baseFbxPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[UltiPaw] Reimporting {baseFbxPath}...");

            // Apply external avatar using the ultipaw avatar file from the selected version.
            string versionDataPath = UltiPawUtils.GetVersionDataPath(activeUltiPawVersion.version, activeUltiPawVersion.defaultAviVersion); // Use current hash for path consistency
            string ultiAvatarFullPath = Path.Combine(versionDataPath, UltiPawUtils.ULTIPAW_AVATAR_NAME).Replace("\\", "/");

            if (File.Exists(ultiAvatarFullPath))
            {
                // Apply to ModelImporter
                UltiPawAvatarUtility.ApplyExternalAvatar(baseFbxFiles[0], ultiAvatarFullPath);
                // Also update the Animator's Avatar field on the avatar root.
                SetRootAnimatorAvatar(ultiAvatarFullPath);
            }
            else
            {
                Debug.LogWarning($"[UltiPaw] UltiPaw avatar file not found or path not set in version details. Path checked: {ultiAvatarFullPath}");
            }

            isUltiPaw = true;
            EditorUtility.SetDirty(this);
            AssetDatabase.Refresh(); // Refresh after all changes
            Debug.Log($"[UltiPaw] Transformation to version {activeUltiPawVersion.version} complete.");

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
             isUltiPaw = false; // Ensure state is correct on failure
             EditorUtility.SetDirty(this);
             AssetDatabase.Refresh();
        }
    }

    // Reset by restoring the backup and applying the default avatar.
    public void ResetIntoWinterPaw()
    {
        string baseFbxPath = GetCurrentBaseFbxPath();
        if (string.IsNullOrEmpty(baseFbxPath))
        {
             Debug.LogError("[UltiPaw] No base FBX file available for reset.");
             return;
        }

        string backupPath = baseFbxPath + ".old";
        bool restored = false;
        if (File.Exists(backupPath))
        {
            try
            {
                if (File.Exists(baseFbxPath)) File.Delete(baseFbxPath);
                File.Move(backupPath, baseFbxPath);
                Debug.Log($"[UltiPaw] Restored original FBX from: {backupPath}");
                AssetDatabase.ImportAsset(baseFbxPath, ImportAssetOptions.ForceUpdate);
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
            Debug.LogWarning("[UltiPaw] No backup file found to restore.");
            // Optionally, still allow resetting avatar definition if FBX wasn't transformed
        }

        // Apply default avatar rig using the default avatar from the *previously* active version if available
        // Or potentially try to find a default rig if no version was active? For now, rely on activeVersion
        if (activeUltiPawVersion != null)
        {
            UpdateCurrentBaseFbxHash(); // Need hash for path generation
            string versionDataPath = UltiPawUtils.GetVersionDataPath(activeUltiPawVersion.version, currentBaseFbxHash ?? "unknown"); // Use hash if available
            string defaultAvatarFullPath = Path.Combine(versionDataPath, UltiPawUtils.DEFAULT_AVATAR_NAME).Replace("\\", "/");

            if (File.Exists(defaultAvatarFullPath))
            {
                UltiPawAvatarUtility.ApplyExternalAvatar(baseFbxFiles[0], defaultAvatarFullPath);
                SetRootAnimatorAvatar(defaultAvatarFullPath);
            }
            else
            {
                 Debug.LogWarning($"[UltiPaw] Default avatar file not found or path not set. Path checked: {defaultAvatarFullPath}");
                 // Maybe try applying Unity's default humanoid rig as a fallback?
                 // UltiPawAvatarUtility.ApplyInternalHumanoidRig(baseFbxFiles[0]);
                 // SetRootAnimatorAvatar(null); // Clear root animator avatar?
            }
        }
        else
        {
            Debug.LogWarning("[UltiPaw] No active version details available to find the default avatar path for reset.");
            // Fallback? Apply Unity's default humanoid rig?
            // UltiPawAvatarUtility.ApplyInternalHumanoidRig(baseFbxFiles[0]);
            // SetRootAnimatorAvatar(null);
        }

        // Reset blendshape sliders to 0.
        if (blendShapeNames != null)
        {
            for (int i = 0; i < blendShapeValues.Count; i++)
            {
                if (blendShapeValues[i] != 0f) // Only update if not already 0
                {
                    blendShapeValues[i] = 0f;
                    // Find and update the actual blendshape on the mesh
                    SetBlendshapeWeightOnBody(blendShapeNames[i], 0f);
                }
            }
        }

        isUltiPaw = false;
        // Don't clear activeUltiPawVersion here, user might want to re-apply it without re-selecting
        // selectedUltiPawBinPath = null; // Keep bin path maybe?
        EditorUtility.SetDirty(this);
        AssetDatabase.Refresh();
        Debug.Log("[UltiPaw] Reset process finished.");
    }
#endif

    // Update the Avatar field on the root's Animator.
    private void SetRootAnimatorAvatar(string avatarAssetPath)
    {
#if UNITY_EDITOR
        if (this == null || transform == null) return;
        Transform root = transform.root;
        if (root != null)
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator != null)
            {
                Avatar avatar = null;
                if (!string.IsNullOrEmpty(avatarAssetPath) && File.Exists(avatarAssetPath))
                {
                     avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarAssetPath);
                }

                if (animator.avatar != avatar) // Only update if changed
                {
                    Undo.RecordObject(animator, "Set Root Animator Avatar");
                    animator.avatar = avatar;
                    EditorUtility.SetDirty(animator);
                    Debug.Log($"[UltiPaw] Root Animator's Avatar set to {(avatar != null ? avatar.name : "None")}");
                }
            }
            // else { Debug.LogWarning("[UltiPaw] Root object has no Animator component."); }
        }
#endif
    }

    // Finds the "Body" SMR and sets a specific blendshape weight
    private void SetBlendshapeWeightOnBody(string shapeName, float weight)
    {
#if UNITY_EDITOR
        if (this == null || transform == null) return;
        Transform root = transform.root;
        if (root == null) return;

        SkinnedMeshRenderer smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                     .FirstOrDefault(s => s.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));

        if (!smr || smr.sharedMesh == null)
        {
            // Don't warn every time if Body isn't found during slider adjustment
            // Debug.LogWarning("[UltiPaw] 'Body' SkinnedMeshRenderer not found for blendshape adjustment.");
            return;
        }

        int index = smr.sharedMesh.GetBlendShapeIndex(shapeName);
        if (index < 0) return; // Shape doesn't exist on this mesh

        // Check if weight actually needs changing to avoid unnecessary Undo/Dirty
        if (!Mathf.Approximately(smr.GetBlendShapeWeight(index), weight))
        {
            Undo.RecordObject(smr, "Adjust Blendshape");
            smr.SetBlendShapeWeight(index, weight);
            EditorUtility.SetDirty(smr);
        }
#endif
    }

    // Public method called by editor slider changes
    public void UpdateBlendshapeFromSlider(string shapeName, float weight)
    {
#if UNITY_EDITOR
        SetBlendshapeWeightOnBody(shapeName, weight);
#endif
    }
}