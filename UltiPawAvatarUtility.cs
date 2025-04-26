#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class UltiPawAvatarUtility
{
    public static void ApplyExternalAvatar(GameObject fbx, string avatarAssetPath)
    {
        string fbxPath = AssetDatabase.GetAssetPath(fbx);
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("[UltiPaw] Failed to get ModelImporter for FBX.");
            return;
        }

        if (!File.Exists(avatarAssetPath))
        {
            Debug.LogWarning($"[UltiPaw] Avatar file not found at: {avatarAssetPath}");
            return;
        }

        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarAssetPath);
        if (avatar == null)
        {
            Debug.LogWarning($"[UltiPaw] Could not load Avatar at: {avatarAssetPath}");
            return;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        
        // Persist the new import settings to disk so Unity won't pop up
        // “Unapplied import settings…” when you hit Play.
        AssetDatabase.SaveAssets();

        Debug.Log($"[UltiPaw] Avatar '{avatar.name}' applied successfully.");
    }
}
#endif
