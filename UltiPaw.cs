#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using IEditorOnly = VRC.SDKBase.IEditorOnly;

public class UltiPaw : MonoBehaviour, IEditorOnly
{
    // --- Configuration ---
    // Keep specifyCustomFiles if you want the *option* to manually assign the BASE FBX.
    // The UltiPaw .bin file will now ALWAYS come from the Version Manager selection.
    public bool specifyCustomBaseFbx = false;

    [Tooltip("The base FBX model(s) to transform (e.g., WinterPaw).")]
    public List<GameObject> baseFbxFiles = new List<GameObject>();

    // This will now hold the SINGLE selected UltiPaw .bin file path from the Version Manager
    [HideInInspector] // Hide as it's managed internally now
    public string selectedUltiPawBinPath;

    // Default location for the BASE FBX if not specifying custom
    private string defaultWinterpawLocation = "Assets/MasculineCanine/FX/MasculineCanine.v1.5.fbx";

    // Store the selected version details from the manager
    [HideInInspector] public UltiPawVersionManager.UltiPawVersion activeUltiPawVersion = null;

    // --- State & Blendshapes ---
    [HideInInspector] public bool isUltiPaw = false;
    [HideInInspector] public List<string> blendShapeNames = new List<string> { /* ... your blendshapes ... */ };
    [HideInInspector] public List<bool> blendShapeStates = new List<bool>();

    // --- Methods ---

    private void OnValidate()
    {
        // Blendshape state sync
        while (blendShapeStates.Count < blendShapeNames.Count) blendShapeStates.Add(false);
        while (blendShapeStates.Count > blendShapeNames.Count) blendShapeStates.RemoveAt(blendShapeStates.Count - 1);

        // Auto-assign default BASE FBX if not specifying custom and list is empty
        if (!specifyCustomBaseFbx && baseFbxFiles.Count == 0)
        {
            AssignDefaultBaseFbx();
        }
        ValidateFiles(); // Validate base FBX and selected bin path
    }

    private void AssignDefaultBaseFbx()
    {
        baseFbxFiles.Clear();
        var defaultFbx = AssetDatabase.LoadAssetAtPath<GameObject>(defaultWinterpawLocation);
        if (defaultFbx != null)
        {
            baseFbxFiles.Add(defaultFbx);
        }
        else
        {
             Debug.LogWarning($"[UltiPaw] Default base FBX not found at: {defaultWinterpawLocation}");
        }
    }

    // Public method for Version Manager to get the primary base FBX path
    public string GetDefaultFbxPath()
    {
        if (baseFbxFiles.Count > 0 && baseFbxFiles[0] != null)
        {
            return AssetDatabase.GetAssetPath(baseFbxFiles[0]);
        }
        // If custom is specified but empty, or default not found, return null/empty
        if (!specifyCustomBaseFbx)
        {
             // Check if default exists even if not assigned yet
             if (File.Exists(defaultWinterpawLocation)) return defaultWinterpawLocation;
        }
        return null;
    }

    // Public method for Version Manager to set the selected .bin path
    public void SetSelectedUltiPawBin(string path)
    {
        if (File.Exists(path))
        {
            selectedUltiPawBinPath = path;
            activeUltiPawVersion = UltiPawVersionManager.SelectedVersion; // Store the corresponding version details
            Debug.Log($"[UltiPaw] Set active UltiPaw binary: {path} (Version: {activeUltiPawVersion?.version ?? "Unknown"})");
            EditorUtility.SetDirty(this); // Mark component as changed
        }
        else
        {
             Debug.LogError($"[UltiPaw] Attempted to set non-existent UltiPaw binary path: {path}");
             selectedUltiPawBinPath = null;
             activeUltiPawVersion = null;
        }
         ValidateFiles();
    }


    private void ValidateFiles()
    {
        // Validate Base FBX(s)
        if (baseFbxFiles.Count == 0 || baseFbxFiles.Any(f => f == null))
        {
             Debug.LogWarning("[UltiPaw] Base FBX file(s) are missing or not assigned.");
        }
        else if (baseFbxFiles.Any(f => !File.Exists(AssetDatabase.GetAssetPath(f))))
        {
             Debug.LogWarning("[UltiPaw] One or more assigned Base FBX files do not exist at their paths.");
        }

        // Validate Selected UltiPaw Bin
        if (string.IsNullOrEmpty(selectedUltiPawBinPath))
        {
            // This is expected if no version is selected yet
            // Debug.LogWarning("[UltiPaw] No UltiPaw version selected or downloaded yet.");
        }
        else if (!File.Exists(selectedUltiPawBinPath))
        {
             Debug.LogWarning($"[UltiPaw] Selected UltiPaw binary file is missing: {selectedUltiPawBinPath}");
             // Optionally clear the selection if the file is gone
             // selectedUltiPawBinPath = null;
             // activeUltiPawVersion = null;
        }
    }

    // Called by the editor button
    public void TurnItIntoUltiPaw()
    {
        // --- Pre-checks ---
        if (activeUltiPawVersion == null || string.IsNullOrEmpty(selectedUltiPawBinPath) || !File.Exists(selectedUltiPawBinPath))
        {
            EditorUtility.DisplayDialog("UltiPaw Error", "No valid UltiPaw version selected or the .bin file is missing.\nPlease use the Version Manager to select and download a version.", "OK");
            return;
        }

        if (baseFbxFiles.Count == 0 || baseFbxFiles[0] == null)
        {
             EditorUtility.DisplayDialog("UltiPaw Error", "No Base FBX file assigned in the 'Base Fbx Files' list.", "OK");
             return;
        }

        string baseFbxPath = AssetDatabase.GetAssetPath(baseFbxFiles[0]); // Assuming we primarily work with the first FBX for hashing/transformation
        if (!File.Exists(baseFbxPath))
        {
             EditorUtility.DisplayDialog("UltiPaw Error", $"The assigned Base FBX file does not exist:\n{baseFbxPath}", "OK");
             return;
        }

        // --- Hash Verification ---
        string currentBaseFbxHash = UltiPawUtils.CalculateFileHash(baseFbxPath);
        string currentBinHash = UltiPawUtils.CalculateFileHash(selectedUltiPawBinPath);

        if (currentBaseFbxHash == null || currentBinHash == null)
        {
             EditorUtility.DisplayDialog("UltiPaw Error", "Failed to calculate hashes for verification. Check console for details.", "OK");
             return;
        }

        bool baseHashMatch = currentBaseFbxHash.Equals(activeUltiPawVersion.defaultAviHash, System.StringComparison.OrdinalIgnoreCase);
        bool binHashMatch = currentBinHash.Equals(activeUltiPawVersion.customAviHash, System.StringComparison.OrdinalIgnoreCase);

        if (!baseHashMatch || !binHashMatch)
        {
            string message = $"Hash mismatch detected for selected version '{activeUltiPawVersion.version}':\n\n";
            if (!baseHashMatch) message += $"Base FBX Hash: MISMATCH\n (Expected: {activeUltiPawVersion.defaultAviHash.Substring(0, 12)}... Found: {currentBaseFbxHash.Substring(0, 12)}...)\n";
            if (!binHashMatch) message += $"UltiPaw .bin Hash: MISMATCH\n (Expected: {activeUltiPawVersion.customAviHash.Substring(0, 12)}... Found: {currentBinHash.Substring(0, 12)}...)\n";
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


        // --- Transformation Process ---
        // We'll process only the first FBX for now, assuming single avatar transformation
        // Extend this loop if you need to transform multiple base FBXs with the same .bin
        GameObject fbxToTransform = baseFbxFiles[0];
        string pathToTransform = baseFbxPath; // Already got this path

        try
        {
            byte[] dataA = File.ReadAllBytes(pathToTransform);
            byte[] dataC = File.ReadAllBytes(selectedUltiPawBinPath);

            // XOR operation
            byte[] dataB = new byte[dataC.Length]; // Resulting data should match the size of the UltiPaw data
            for (int j = 0; j < dataC.Length; j++)
            {
                // Ensure we don't go out of bounds on dataA if dataC is longer
                dataB[j] = (byte)(dataC[j] ^ dataA[j % dataA.Length]);
            }

            // Backup original
            string backupPath = pathToTransform + ".old";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(pathToTransform, backupPath);
            Debug.Log($"[UltiPaw] Backed up original FBX to: {backupPath}");

            // Write new file
            File.WriteAllBytes(pathToTransform, dataB);
            Debug.Log($"[UltiPaw] Wrote transformed data to: {pathToTransform}");

            // --- Reimport and Apply Avatar Rig ---
            // We need to force Unity to reimport the modified FBX *before* applying the avatar
            AssetDatabase.ImportAsset(pathToTransform, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[UltiPaw] Reimporting {pathToTransform}...");

            // Apply the external avatar rig configuration
            // Assuming the rig file is consistent and not versioned for now
            UltiPawAvatarUtility.ApplyExternalAvatar(fbxToTransform, UltiPawUtils.DEFAULT_AVATAR_RIG_PATH);

            // Optional: Post-transformation checks (like chest bone)
            // Note: Getting component directly from GameObject might not work immediately after reimport.
            // It's better to reload the asset or wait briefly. A simple refresh might suffice.
            AssetDatabase.Refresh(); // Refresh asset database again after rig application

            // Mark state as "UltiPaw" now
            isUltiPaw = true;
            EditorUtility.SetDirty(this); // Save state change

            Debug.Log($"[UltiPaw] Successfully transformed {Path.GetFileName(pathToTransform)} using version {activeUltiPawVersion.version}.");

        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UltiPaw] Transformation failed: {e}");
            EditorUtility.DisplayDialog("UltiPaw Error", $"An error occurred during transformation:\n{e.Message}\n\nCheck the console for more details. You may need to manually restore the backup (.old file).", "OK");

            // Attempt to restore backup on failure
            string backupPath = pathToTransform + ".old";
            if (File.Exists(backupPath))
            {
                 if (File.Exists(pathToTransform)) File.Delete(pathToTransform);
                 File.Move(backupPath, pathToTransform);
                 Debug.LogWarning($"[UltiPaw] Attempted to restore backup: {pathToTransform}");
                 AssetDatabase.ImportAsset(pathToTransform, ImportAssetOptions.ForceUpdate); // Reimport restored file
            }
            isUltiPaw = false; // Ensure state is not set to UltiPaw on failure
            EditorUtility.SetDirty(this);
        }

        AssetDatabase.Refresh();
    }

    // Called by the editor button
    public void ResetIntoWinterPaw()
    {
        bool restoredAny = false;
        foreach (var fileA in baseFbxFiles) // Use the correct list name
        {
            if (fileA == null) continue;

            string path = AssetDatabase.GetAssetPath(fileA);
            string backupPath = path + ".old";

            if (File.Exists(backupPath))
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(backupPath, path);
                    Debug.Log($"[UltiPaw] Restored original FBX: {path}");
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); // Reimport restored file
                    restoredAny = true;
                }
                catch (System.Exception e)
                {
                     Debug.LogError($"[UltiPaw] Failed to restore {path} from backup: {e.Message}");
                     EditorUtility.DisplayDialog("Restore Error", $"Failed to restore {Path.GetFileName(path)}.\n{e.Message}\n\nYou may need to do it manually.", "OK");
                }
            }
            else
            {
                 Debug.LogWarning($"[UltiPaw] No backup file found for {path}");
            }
        }

        if (restoredAny)
        {
            // Mark state as not UltiPaw ONLY if a restore happened
            isUltiPaw = false;

            // Reset all blendshapes to 0
            ResetAllBlendshapes();

            // Clear the selected version info as we're back to base
            selectedUltiPawBinPath = null;
            activeUltiPawVersion = null;

            EditorUtility.SetDirty(this); // Save state changes
            Debug.Log("[UltiPaw] Reset to WinterPaw state completed.");
        } else {
            Debug.LogWarning("[UltiPaw] Reset requested, but no backup files were found to restore.");
            EditorUtility.DisplayDialog("Reset Info", "No '.old' backup files were found for the assigned Base FBX files. Cannot perform automatic reset.", "OK");
        }

        AssetDatabase.Refresh();
    }

     private void ResetAllBlendshapes()
     {
        for (int i = 0; i < blendShapeStates.Count; i++)
        {
            blendShapeStates[i] = false;
            ToggleBlendShape(blendShapeNames[i], false);
        }
     }
     

    // Toggle a given blendshape on the "Body" GameObject
    public void ToggleBlendShape(string shapeName, bool isOn)
    {
        // Find the SkinnedMeshRenderer on the *first* base FBX GameObject's hierarchy
        // This assumes the blendshapes are on a child named "Body" of the root FBX object
        if (baseFbxFiles.Count == 0 || baseFbxFiles[0] == null) return;

        // A more robust find: search children for SkinnedMeshRenderer
        var skinnedRenderer = baseFbxFiles[0].GetComponentInChildren<SkinnedMeshRenderer>(true); // Include inactive

        if (!skinnedRenderer)
        {
            Debug.LogWarning($"[UltiPaw] Could not find a SkinnedMeshRenderer within '{baseFbxFiles[0].name}'.");
            return;
        }

        if (!skinnedRenderer.sharedMesh)
        {
            Debug.LogWarning($"[UltiPaw] SkinnedMeshRenderer found on '{skinnedRenderer.gameObject.name}' has no sharedMesh.");
            return;
        }

        int shapeIndex = skinnedRenderer.sharedMesh.GetBlendShapeIndex(shapeName);
        if (shapeIndex < 0)
        {
            // Don't warn every time if it just doesn't exist on this mesh
            // Debug.LogWarning($"[UltiPaw] Blend shape '{shapeName}' not found on mesh '{skinnedRenderer.sharedMesh.name}'.");
            return;
        }

        // Use EditorUtility to handle Undo and marking dirty in Editor
        Undo.RecordObject(skinnedRenderer, "Toggle Blend Shape");
        skinnedRenderer.SetBlendShapeWeight(shapeIndex, isOn ? 100f : 0f);
        EditorUtility.SetDirty(skinnedRenderer); // Mark renderer as changed
    }
}
#endif