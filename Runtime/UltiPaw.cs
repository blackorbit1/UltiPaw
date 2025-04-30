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
    [JsonProperty] public string defaultAviHash;
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

    // The active version details (selected via the integrated version manager UI).
    [HideInInspector]
    public UltiPawVersion activeUltiPawVersion = null; // This is the one SELECTED in the UI

    // *** NEW: Stores the details of the version last successfully APPLIED to the FBX ***
    [HideInInspector]
    public UltiPawVersion appliedUltiPawVersion = null;

    [HideInInspector] public bool isUltiPaw = false; // Reflects if FBX matches appliedUltiPawVersion.customAviHash

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
        UpdateIsUltiPawState(); // Check based on existing hash and applied version
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

    // *** NEW: Updates the isUltiPaw state based on applied version and current hash ***
    public bool UpdateIsUltiPawState()
    {
        bool previousState = isUltiPaw;
        if (appliedUltiPawVersion != null && !string.IsNullOrEmpty(appliedUltiPawVersion.customAviHash) && !string.IsNullOrEmpty(currentBaseFbxHash))
        {
            isUltiPaw = currentBaseFbxHash.Equals(appliedUltiPawVersion.customAviHash, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // If no applied version is known, or hash is missing, assume it's not the transformed state
            isUltiPaw = false;
        }

        // Return true if the state changed
        return isUltiPaw != previousState;
    }


    // Calculates and updates the hash for the current base FBX
    public bool UpdateCurrentBaseFbxHash()
    {
#if UNITY_EDITOR
        string path = GetCurrentBaseFbxPath();
        string oldHash = currentBaseFbxHash; // Store old hash

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            currentBaseFbxHash = UltiPawUtils.CalculateFileHash(path); // Update hash

            // Update the dictionary as well
            winterpawFbxHashes.Clear();
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
            UpdateIsUltiPawState();
        }
        return hashChanged; // Return whether the hash value changed
#else
        return false;
#endif
    }
    
    // update appliedUltiPawVersion with a list serverVersions
    public void UpdateAppliedUltiPawVersion(List<UltiPawVersion> serverVersions)
    {
        if (serverVersions == null || serverVersions.Count == 0)
        {
            Debug.LogWarning("[UltiPaw] No server versions available to update applied version.");
            return;
        }

        // Check if the current hash matches any version's customAviHash
        foreach (var version in serverVersions)
        {
            if (currentBaseFbxHash != null && currentBaseFbxHash.Equals(version.appliedCustomAviHash, StringComparison.OrdinalIgnoreCase))
            {
                appliedUltiPawVersion = version; // Update applied version
                isUltiPaw = true; // Update isUltiPaw state
                return; // Exit loop on first match
            }
        }
        Debug.LogWarning("[UltiPaw] No matching version found for current hash. Current hash: " + currentBaseFbxHash);
    }

#if UNITY_EDITOR
    // Transform the base FBX using XOR with the selected UltiPaw .bin.
    public bool ApplyUltiPaw() // Return bool indicating success
    {
        string baseFbxPath = GetCurrentBaseFbxPath();
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

        // --- Hash Verification ---
        UpdateCurrentBaseFbxHash(); // Ensure hash is current before check
        //string currentBinHash = UltiPawUtils.CalculateFileHash(selectedUltiPawBinPath);

        // Verify current FBX against the *expected default* hash of the version being applied
        bool baseHashMatch = currentBaseFbxHash != null && currentBaseFbxHash.Equals(activeUltiPawVersion.defaultAviHash, StringComparison.OrdinalIgnoreCase);
        // Verify the bin file against the *expected custom* hash of the version being applied (this hash check seems reversed in original code, correcting it)
        // The BIN file itself doesn't have a hash stored *in* the version data usually.
        // Let's assume the check was meant to ensure the BIN file *corresponds* to the version somehow.
        // We'll keep the original logic for now, but it might need review based on server implementation.
        // A better check might be hashing the BIN and comparing to a hash provided *for the bin* by the server, if available.
        // For now, let's assume activeUltiPawVersion.customAviHash is the *expected hash of the resulting FBX*, not the bin.
        // And activeUltiPawVersion.defaultAviHash is the *expected hash of the input FBX*.

        // Let's re-evaluate the hash check logic based on variable names:
        // defaultAviHash = hash of the original FBX this version applies to.
        // customAviHash = hash of the FBX *after* this version is applied.
        // So, we check if currentBaseFbxHash == defaultAviHash.

        if (!baseHashMatch)
        {
            string message = $"Hash mismatch detected for applying version '{activeUltiPawVersion.version}':\n\n";
            message += $"Base FBX Hash: MISMATCH\n (Expected: {activeUltiPawVersion.defaultAviHash ?? "N/A"}... Found: {currentBaseFbxHash ?? "N/A"}...)\n";
            message += "\nApplying this version might lead to unexpected results or errors. Are you sure you want to continue?";

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

            // Write transformed FBX.
            File.WriteAllBytes(baseFbxPath, transformedData);
            Debug.Log($"[UltiPaw] Wrote transformed data to: {baseFbxPath}");

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

            // --- Success State Update ---
            // Record Undo for the component state change
            Undo.RecordObject(this, "Apply UltiPaw Version");

            // Update applied version *only on success*
            appliedUltiPawVersion = activeUltiPawVersion;

            // Recalculate hash of the *newly written* file
            UpdateCurrentBaseFbxHash();
            // Update the state based on the new hash and the now set applied version
            UpdateIsUltiPawState();

            EditorUtility.SetDirty(this); // Mark component dirty
            AssetDatabase.Refresh(); // Refresh after all changes
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

             // Record Undo for the component state change (even on failure, to revert potential partial changes)
             Undo.RecordObject(this, "Apply UltiPaw Version Failed");

             // Ensure state reflects failure
             // appliedUltiPawVersion remains unchanged or null
             UpdateCurrentBaseFbxHash(); // Re-hash the restored (or original) file
             UpdateIsUltiPawState(); // Update state based on hash and potentially null applied version

             EditorUtility.SetDirty(this);
             AssetDatabase.Refresh();
             return false; // Indicate failure
        }
    }

    // Reset by restoring the backup and applying the default avatar.
    public bool ResetIntoWinterPaw() // Return bool indicating success
    {
        string baseFbxPath = GetCurrentBaseFbxPath();
        if (string.IsNullOrEmpty(baseFbxPath))
        {
             Debug.LogError("[UltiPaw] No base FBX file available for reset.");
             return false;
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
                AssetDatabase.SaveAssets();
                restored = true;
            }
            catch (System.Exception e)
            {
                 Debug.LogError($"[UltiPaw] Failed to restore {baseFbxPath} from backup: {e.Message}");
                 EditorUtility.DisplayDialog("Restore Error", $"Failed to restore {Path.GetFileName(baseFbxPath)}.\n{e.Message}\n\nYou may need to do it manually.", "OK");
                 // Continue to try and apply default avatar if possible
            }
        }
        else
        {
            Debug.LogWarning("[UltiPaw] No backup file found to restore. Attempting to apply default avatar to current FBX.");
            // If no backup, we assume the current FBX is the one we want to reset the avatar on
            restored = true; // Treat as "restored" for the purpose of applying default avatar
        }

        // Only proceed if restore was successful or no backup existed (meaning we operate on current file)
        if (restored)
        {
            // Apply default avatar rig using the default avatar from the *previously applied* version if available
            UltiPawVersion versionForDefault = appliedUltiPawVersion ?? activeUltiPawVersion; // Prefer applied, fallback to selected

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
                        // Don't clear applied version if avatar application failed
                    }
                }
                else
                {
                    Debug.LogWarning($"[UltiPaw] Default avatar file not found for version {versionForDefault.version}. Path checked: {defaultAvatarFullPath}");
                    // Attempt to apply Unity's default humanoid rig as a fallback?
                    // if (UltiPawAvatarUtility.ApplyInternalHumanoidRig(baseFbxFiles[0])) {
                    //     SetRootAnimatorAvatar(null); // Clear root animator avatar
                    // }
                }
            }
            else
            {
                Debug.LogWarning("[UltiPaw] No version details available to find the default avatar path for reset.");
                // Fallback? Apply Unity's default humanoid rig?
                // if (UltiPawAvatarUtility.ApplyInternalHumanoidRig(baseFbxFiles[0])) {
                //     SetRootAnimatorAvatar(null);
                // }
            }

            // Record Undo for component state changes
            Undo.RecordObject(this, "Reset UltiPaw");

            // Reset blendshape sliders to 0.
            if (blendShapeNames != null)
            {
                // Use the blendshapes from the version that *was* applied, if known
                var shapesToReset = appliedUltiPawVersion?.customBlendshapes ?? blendShapeNames.ToArray();
                foreach (var blendshape in shapesToReset)
                {
                    // Find the current value in the component's list if it exists
                    var valueIndex = blendShapeNames.IndexOf(blendshape);
                    if (valueIndex >= 0 && valueIndex < blendShapeValues.Count && blendShapeValues[valueIndex] != 0f)
                    {
                        blendShapeValues[valueIndex] = 0f;
                    }
                    // Always attempt to reset on the mesh itself
                    SetBlendshapeWeightOnBody(blendshape, 0f);
                }
            }

            // Clear the applied version state
            appliedUltiPawVersion = null;
            // Recalculate hash of the restored/original file
            UpdateCurrentBaseFbxHash();
            // Update the state based on the new hash and null applied version
            UpdateIsUltiPawState();

            EditorUtility.SetDirty(this);
            AssetDatabase.Refresh();
            Debug.Log("[UltiPaw] Reset process finished.");
            return true; // Indicate success
        }
        else
        {
            // Restore failed, don't change component state
            AssetDatabase.Refresh(); // Refresh just in case
            return false; // Indicate failure
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
            // Gemini's proposition which looks like garbage:
            // Don't add animator automatically here, assume it exists if needed.
            // Let the avatar application handle importer settings.
            // Debug.LogWarning($"[UltiPaw] Animator component not found on root object '{rootObject.name}'. Cannot set avatar.");
            //return;
            
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
        // Find the index in our value list and update it
        int listIndex = blendShapeNames.IndexOf(shapeName);
        if (listIndex >= 0 && listIndex < blendShapeValues.Count)
        {
            // Check if value actually changed before dirtying
            if (!Mathf.Approximately(blendShapeValues[listIndex], weight))
            {
                // No Undo needed here as the editor handles SerializedProperty changes
                blendShapeValues[listIndex] = weight;
                // Don't SetDirty(this) here, SerializedObject handles it.
            }
        }
        // Apply the change to the actual model
        SetBlendshapeWeightOnBody(shapeName, weight);
#endif
    }
}