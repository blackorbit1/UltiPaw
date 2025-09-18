#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class UltiPawAvatarUtility
{
    // Returns true if successful, false otherwise
    public static bool ApplyExternalAvatar(GameObject fbx, string avatarAssetPath)
    {
        if (fbx == null)
        {
            UltiPawLogger.LogError("[UltiPaw] ApplyExternalAvatar: FBX GameObject is null.");
            return false;
        }

        string fbxPath = AssetDatabase.GetAssetPath(fbx);
        if (string.IsNullOrEmpty(fbxPath))
        {
            UltiPawLogger.LogError($"[UltiPaw] ApplyExternalAvatar: Could not get asset path for FBX '{fbx.name}'.");
            return false;
        }

        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            UltiPawLogger.LogError($"[UltiPaw] Failed to get ModelImporter for FBX at path: {fbxPath}");
            return false;
        }

        if (!File.Exists(avatarAssetPath))
        {
            UltiPawLogger.LogWarning($"[UltiPaw] Avatar file not found at: {avatarAssetPath}");
            // Should we still try to set Humanoid type without an avatar? Maybe not.
            // importer.animationType = ModelImporterAnimationType.Human;
            // importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; // Fallback?
            // EditorUtility.SetDirty(importer);
            // importer.SaveAndReimport();
            // AssetDatabase.SaveAssets();
            // UltiPawLogger.LogWarning($"[UltiPaw] Set importer to Humanoid (Create From This Model) as fallback.");
            return false; // Indicate failure if avatar file is missing
        }

        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarAssetPath);
        if (avatar == null)
        {
            UltiPawLogger.LogWarning($"[UltiPaw] Could not load Avatar at: {avatarAssetPath}");
            return false; // Indicate failure if avatar couldn't be loaded
        }

        // Check if changes are actually needed
        bool needsReimport = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            needsReimport = true;
        }

        if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            needsReimport = true;
        }

        if (importer.sourceAvatar != avatar)
        {
            importer.sourceAvatar = avatar;
            needsReimport = true;
        }


        if (needsReimport)
        {
            try
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                AssetDatabase.SaveAssets(); // Persist settings
                UltiPawLogger.Log($"[UltiPaw] Avatar '{avatar.name}' applied successfully via importer.");
                return true; // Indicate success
            }
            catch (System.Exception ex)
            {
                UltiPawLogger.LogError($"[UltiPaw] Failed during SaveAndReimport for avatar '{avatar.name}': {ex.Message}");
                return false; // Indicate failure
            }
        }
        else
        {
            UltiPawLogger.Log($"[UltiPaw] Avatar '{avatar.name}' already applied. No reimport needed.");
            return true; // Indicate success (already correct)
        }
    }

    // Optional: Add ApplyInternalHumanoidRig if needed as fallback
    // public static bool ApplyInternalHumanoidRig(GameObject fbx) { ... }
}
#endif