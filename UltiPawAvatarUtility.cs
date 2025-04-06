#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class UltiPawAvatarUtility
{
    public static void ApplyExternalAvatar(GameObject fbx, string avatarAssetPath)
    {
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(fbx)) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("[UltiPaw] Failed to get ModelImporter.");
            return;
        }

        if (!File.Exists(avatarAssetPath))
        {
            Debug.LogWarning($"[UltiPaw] ⚠️ Avatar file not found at: {avatarAssetPath}");
            return;
        }

        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarAssetPath);
        if (avatar == null)
        {
            Debug.LogWarning($"[UltiPaw] ❌ Could not load Avatar at: {avatarAssetPath}");
            return;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        Debug.Log($"[UltiPaw] ✅ Avatar '{avatar.name}' successfully assigned.");
    }
}
#endif
