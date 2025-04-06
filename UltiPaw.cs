#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using IEditorOnly = VRC.SDKBase.IEditorOnly;

public class UltiPaw : MonoBehaviour, IEditorOnly
{
    // Script version constant (used for server requests)
    public const string SCRIPT_VERSION = "0.1";

    // If true, user manually assigns the base FBX; otherwise auto-detect.
    public bool specifyCustomBaseFbx = false;

    // List of base FBX files – the one we want to transform.
    public List<GameObject> baseFbxFiles = new List<GameObject>();

    // Mapping from a base FBX file path to its calculated hash.
    [HideInInspector]
    public Dictionary<string, string> winterpawFbxHashes = new Dictionary<string, string>();

    // The path (set via version management UI) to the selected UltiPaw .bin file.
    [HideInInspector]
    public string selectedUltiPawBinPath;

    // The active version details (selected via the integrated version manager).
    [HideInInspector]
    public UltiPawVersionManager.UltiPawVersion activeUltiPawVersion = null;

    [HideInInspector] public bool isUltiPaw = false;

    // Blendshape settings – these will be updated from the selected version.
    [HideInInspector] public List<string> blendShapeNames = new List<string>(); // Initially empty; updated from server.
    [HideInInspector] public List<float> blendShapeValues = new List<float>(); // Values range 0 to 100.


    // Predefined keywords for detecting the original (WinterPaw) FBX.
    private string[] possibleFbxNames = { "MasculineCanine.v1.5" };

    private void OnValidate()
    {
#if UNITY_EDITOR
        // Auto-detect a base FBX if none is assigned and custom is not specified.
        if (!specifyCustomBaseFbx && (baseFbxFiles == null || baseFbxFiles.Count == 0 || baseFbxFiles[0] == null))
        {
            AutoDetectBaseFbx();
        }
#endif
        // Ensure the blendshape slider list matches the blendshape names.
        while (blendShapeValues.Count < blendShapeNames.Count)
            blendShapeValues.Add(0f);
        while (blendShapeValues.Count > blendShapeNames.Count)
            blendShapeValues.RemoveAt(blendShapeValues.Count - 1);

        // Update the mapping of base FBX file paths to their hashes.
        UpdateBaseFbxHashes();
    }

    public string GetDetectedBaseFbxPath()
    {
    #if UNITY_EDITOR
        if (baseFbxFiles != null && baseFbxFiles.Count > 0 && baseFbxFiles[0] != null)
        {
            return AssetDatabase.GetAssetPath(baseFbxFiles[0]);
        }
    #endif
        return null;
    }


    // Searches the Assets for an FBX whose name contains one of the keywords.
    private void AutoDetectBaseFbx()
    {
#if UNITY_EDITOR
        baseFbxFiles.Clear();
        string[] guids = AssetDatabase.FindAssets("t:GameObject");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetExtension(path).ToLower() == ".fbx")
            {
                string filename = Path.GetFileNameWithoutExtension(path);
                if (possibleFbxNames.Any(keyword => filename.ToLower().Contains(keyword.ToLower())))
                {
                    GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (fbxAsset != null)
                    {
                        baseFbxFiles.Add(fbxAsset);
                        Debug.Log($"[UltiPaw] Auto-detected base FBX: {path}");
                        break;
                    }
                }
            }
        }
#endif
    }

    // Update the winterpawFbxHashes dictionary.
    private void UpdateBaseFbxHashes()
    {
#if UNITY_EDITOR
        winterpawFbxHashes.Clear();
        foreach (var fbx in baseFbxFiles)
        {
            if (fbx != null)
            {
                string path = AssetDatabase.GetAssetPath(fbx);
                string hash = UltiPawUtils.CalculateFileHash(path);
                if (!string.IsNullOrEmpty(hash))
                {
                    winterpawFbxHashes[path] = hash;
                }
            }
        }
#endif
    }

#if UNITY_EDITOR
    // Transform the base FBX using XOR with the selected UltiPaw .bin.
    public void TurnItIntoUltiPaw()
    {
        if (baseFbxFiles.Count == 0 || baseFbxFiles[0] == null)
        {
            Debug.LogError("[UltiPaw] No base FBX file available.");
            return;
        }
        string baseFbxPath = AssetDatabase.GetAssetPath(baseFbxFiles[0]);
        if (!File.Exists(baseFbxPath))
        {
            Debug.LogError($"[UltiPaw] Base FBX file not found at: {baseFbxPath}");
            return;
        }
        if (string.IsNullOrEmpty(selectedUltiPawBinPath) || !File.Exists(selectedUltiPawBinPath))
        {
            Debug.LogError("[UltiPaw] Selected UltiPaw bin file is missing.");
            return;
        }

        // Read files.
        byte[] baseData = File.ReadAllBytes(baseFbxPath);
        byte[] binData = File.ReadAllBytes(selectedUltiPawBinPath);

        // Perform XOR operation.
        byte[] transformedData = new byte[binData.Length];
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

        // Force reimport.
        AssetDatabase.ImportAsset(baseFbxPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[UltiPaw] Reimporting {baseFbxPath}...");

        // Apply external avatar using the ultipaw avatar file from the selected version.
        if (activeUltiPawVersion != null && !string.IsNullOrEmpty(activeUltiPawVersion.ultipawAvatarPath))
        {
            UltiPawAvatarUtility.ApplyExternalAvatar(baseFbxFiles[0], activeUltiPawVersion.ultipawAvatarPath);
            // Also update the Animator's Avatar field on the avatar root.
            SetRootAvatar(activeUltiPawVersion.ultipawAvatarPath);
        }
        else
        {
            Debug.LogWarning("[UltiPaw] Active version details missing or ultipaw avatar path not set.");
        }

        isUltiPaw = true;
        EditorUtility.SetDirty(this);
        AssetDatabase.Refresh();
    }

    // Reset by restoring the backup and applying the default avatar.
    public void ResetIntoWinterPaw()
    {
        if (baseFbxFiles.Count == 0 || baseFbxFiles[0] == null)
        {
            Debug.LogError("[UltiPaw] No base FBX file available for reset.");
            return;
        }
        string baseFbxPath = AssetDatabase.GetAssetPath(baseFbxFiles[0]);
        string backupPath = baseFbxPath + ".old";
        if (File.Exists(backupPath))
        {
            if (File.Exists(baseFbxPath)) File.Delete(baseFbxPath);
            File.Move(backupPath, baseFbxPath);
            Debug.Log($"[UltiPaw] Restored original FBX from: {backupPath}");
            AssetDatabase.ImportAsset(baseFbxPath, ImportAssetOptions.ForceUpdate);
        }
        else
        {
            Debug.LogWarning("[UltiPaw] No backup file found to restore.");
        }

        // Apply default avatar rig using the default avatar from the active version.
        if (activeUltiPawVersion != null && !string.IsNullOrEmpty(activeUltiPawVersion.defaultAvatarPath))
        {
            UltiPawAvatarUtility.ApplyExternalAvatar(baseFbxFiles[0], activeUltiPawVersion.defaultAvatarPath);
            SetRootAvatar(activeUltiPawVersion.defaultAvatarPath);
        }
        else
        {
            Debug.LogWarning("[UltiPaw] Active version details missing or default avatar path not set.");
        }

        // Reset blendshape sliders to 0.
        for (int i = 0; i < blendShapeValues.Count; i++)
        {
            blendShapeValues[i] = 0f;
            ToggleBlendShape(blendShapeNames[i], 0f);
        }

        isUltiPaw = false;
        activeUltiPawVersion = null;
        selectedUltiPawBinPath = null;
        EditorUtility.SetDirty(this);
        AssetDatabase.Refresh();
    }
#endif

    // Update the Avatar field on the root's Animator.
    private void SetRootAvatar(string avatarAssetPath)
    {
#if UNITY_EDITOR
        Transform root = transform.root;
        if (root != null)
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator != null)
            {
                Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarAssetPath);
                if (avatar != null)
                {
                    animator.avatar = avatar;
                    EditorUtility.SetDirty(animator);
                    Debug.Log($"[UltiPaw] Root Animator's Avatar set to {avatar.name}");
                }
                else
                {
                    Debug.LogWarning($"[UltiPaw] Could not load Avatar at {avatarAssetPath}");
                }
            }
            else
            {
                Debug.LogWarning("[UltiPaw] Root object has no Animator component.");
            }
        }
#endif
    }

    // Adjust a blendshape to the given weight (0 to 100).
    public void ToggleBlendShape(string shapeName, float weight)
    {
#if UNITY_EDITOR
        // Find the "Body" GameObject (assumed to be a child of the avatar root).
        GameObject body = GameObject.Find("Body");
        if (!body)
        {
            Debug.LogWarning("[UltiPaw] Could not find GameObject named 'Body' in the scene.");
            return;
        }
        SkinnedMeshRenderer smr = body.GetComponent<SkinnedMeshRenderer>();
        if (!smr || smr.sharedMesh == null)
        {
            Debug.LogWarning("[UltiPaw] 'Body' does not have a valid SkinnedMeshRenderer.");
            return;
        }
        int index = smr.sharedMesh.GetBlendShapeIndex(shapeName);
        if (index < 0) return;
        Undo.RecordObject(smr, "Adjust Blendshape");
        smr.SetBlendShapeWeight(index, weight);
        EditorUtility.SetDirty(smr);
#endif
    }
}
