#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class UltiPaw : MonoBehaviour
{
    public bool specifyCustomFiles = false;

    [HideInInspector]
    public List<GameObject> filesA = new List<GameObject>();

    [HideInInspector]
    public List<DefaultAsset> filesC = new List<DefaultAsset>();

    private string defaultWinterpawLocation = "Assets/MasculineCanine/FX/MasculineCanine.v1.5.fbx";
    private string defaultUltiPawLocation = "Assets/UltiPaw/ultipaw.bin";

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Auto-assign defaults only if lists are empty
        if (!specifyCustomFiles)
        {
            AssignDefaultFiles();
        }

        ValidateFiles();
    }

    private void AssignDefaultFiles()
    {
        filesA.Clear();
        filesC.Clear();

        // Note: using LoadAssetAtPath here is okay; our TurnItIntoUltiPaw will handle FBX processing.
        var defaultA = AssetDatabase.LoadAssetAtPath<GameObject>(defaultWinterpawLocation);
        var defaultC = AssetDatabase.LoadAssetAtPath<DefaultAsset>(defaultUltiPawLocation);

        filesA.Add(defaultA);
        filesC.Add(defaultC);
    }

    private void ValidateFiles()
    {
        if (filesA.Count == 0 || filesA[0] == null)
            Debug.LogWarning("UltiPaw: Default File A not found at " + defaultWinterpawLocation);

        if (filesC.Count == 0 || filesC[0] == null)
            Debug.LogWarning("UltiPaw: Default File C not found at " + defaultUltiPawLocation);
    }
#endif

    public void TurnItIntoUltiPaw()
    {
#if UNITY_EDITOR
        for (int i = 0; i < filesA.Count; i++)
        {
            if (i >= filesC.Count || filesA[i] == null || filesC[i] == null) continue;

            // Get the original file path (should be .fbx for models)
            string originalPath = UnityEditor.AssetDatabase.GetAssetPath(filesA[i]);
            bool isFBX = originalPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
            string keyPath = originalPath;
            string tempPath = originalPath;

            // If it's an FBX, change the extension to .bin to prevent auto-import issues.
            if (isFBX)
            {
                tempPath = Path.ChangeExtension(originalPath, ".bin");
                if (File.Exists(tempPath)) File.Delete(tempPath);
                File.Move(originalPath, tempPath);
                keyPath = tempPath;
            }

            string pathC = UnityEditor.AssetDatabase.GetAssetPath(filesC[i]);

            byte[] dataA = File.ReadAllBytes(keyPath);
            byte[] dataC = File.ReadAllBytes(pathC);
            byte[] dataB = new byte[dataC.Length];
            for (int j = 0; j < dataC.Length; j++)
                dataB[j] = (byte)(dataC[j] ^ dataA[j % dataA.Length]);

            // Backup the key file
            string backupPath = keyPath + ".old";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(keyPath, backupPath);

            // Write the XOR result to keyPath
            File.WriteAllBytes(keyPath, dataB);

            // If it was an FBX, rename it back to .fbx.
            if (isFBX)
            {
                if (File.Exists(originalPath)) File.Delete(originalPath);
                File.Move(keyPath, originalPath);
            }

            Debug.Log($"Transformed {originalPath}");
        }

        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    public void ResetIntoWinterPaw()
    {
#if UNITY_EDITOR
        foreach (var fileA in filesA)
        {
            if (fileA == null) continue;

            string originalPath = UnityEditor.AssetDatabase.GetAssetPath(fileA);
            bool isFBX = originalPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
            string backupPath = "";

            if (isFBX)
            {
                // For FBX, the backup was created on the temporary .bin file.
                string tempPath = Path.ChangeExtension(originalPath, ".bin");
                backupPath = tempPath + ".old";
            }
            else
            {
                backupPath = originalPath + ".old";
            }

            if (File.Exists(backupPath))
            {
                if (File.Exists(originalPath)) File.Delete(originalPath);
                File.Move(backupPath, originalPath);
                Debug.Log($"Restored {originalPath}");
            }
        }

        UnityEditor.AssetDatabase.Refresh();
#endif
    }
}
