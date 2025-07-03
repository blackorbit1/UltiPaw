#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

public class FileManagerService
{
    public const string OriginalSuffix = ".old";

    public string CalculateFileHash(string path)
    {
        if (!File.Exists(path)) return null;
        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(path))
        {
            byte[] hash = sha256.ComputeHash(stream);
            StringBuilder sb = new StringBuilder();
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    public void CreateBackup(string fbxPath)
    {
        if (string.IsNullOrEmpty(fbxPath) || !File.Exists(fbxPath)) return;
        string backupPath = fbxPath + OriginalSuffix;
        if (File.Exists(backupPath)) File.Delete(backupPath);
        File.Move(fbxPath, backupPath);
    }
    
    public bool BackupExists(string fbxPath)
    {
        return !string.IsNullOrEmpty(fbxPath) && File.Exists(fbxPath + OriginalSuffix);
    }

    public void RestoreBackup(string fbxPath)
    {
        string backupPath = fbxPath + OriginalSuffix;
        if (!File.Exists(backupPath)) return;
        if (File.Exists(fbxPath)) File.Delete(fbxPath);
        File.Move(backupPath, fbxPath);
    }
    
    public void DeleteVersionFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        Directory.Delete(path, true);
        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
    }

    public byte[] XorTransform(byte[] baseData, byte[] keyData)
    {
        // The XOR key should be the BASE data, and the TARGET data is what's being 'encrypted'.
        // The provided code has keyData (the .bin) as the main loop, this is correct for decryption.
        byte[] transformedData = new byte[keyData.Length];
        for (int i = 0; i < keyData.Length; i++)
        {
            transformedData[i] = (byte)(keyData[i] ^ baseData[i % baseData.Length]);
        }
        return transformedData;
    }

    public void UnzipAndMove(string zipPath, string extractPath, string finalDestinationPath)
    {
        if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(zipPath, extractPath);

        string contentSourcePath = extractPath;
        var rootDirs = Directory.GetDirectories(extractPath);
        if (rootDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0)
        {
            contentSourcePath = rootDirs[0];
        }

        if (Directory.Exists(finalDestinationPath)) Directory.Delete(finalDestinationPath, true);
        CopyDirectory(contentSourcePath, finalDestinationPath);
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
    
    public void RemoveExistingLogic(Transform root)
    {
        Transform existingLogic = root.Find("ultipaw logic");
        if (existingLogic != null)
        {
            Undo.DestroyObjectImmediate(existingLogic.gameObject);
        }
    }

    public void ApplyAvatarToModel(Transform root, GameObject fbx, string avatarPath)
    {
        if (fbx == null || string.IsNullOrEmpty(avatarPath) || !File.Exists(avatarPath)) return;
        
        // FIX: Re-enabled the actual avatar application logic. This call re-imports the model with the new avatar.
        UltiPawAvatarUtility.ApplyExternalAvatar(fbx, avatarPath);
        
        SetRootAnimatorAvatar(root, avatarPath);
    }

    public void InstantiateLogicPrefab(string packagePath, Transform parent)
    {
        if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath) || parent == null) return;
        
        AssetDatabase.ImportPackage(packagePath, false); // Import silently
        
        // The prefab is inside the version folder, not at the root of the package path.
        string versionDataFolder = Path.GetDirectoryName(packagePath);
        string prefabPath = Path.Combine(versionDataFolder, "ultipaw logic.prefab");

        if (!File.Exists(prefabPath))
        {
            Debug.LogWarning($"[FileManager] Expected prefab not found at '{prefabPath}' after package import.");
            return;
        }

        GameObject logicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (logicPrefab != null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(logicPrefab, parent);
            instance.name = "ultipaw logic";
            Undo.RegisterCreatedObjectUndo(instance, "Install UltiPaw Logic");
        }
    }

    private void SetRootAnimatorAvatar(Transform root, string avatarAssetPath)
    {
        if (root == null) return;

        Animator animator = root.GetComponent<Animator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(root.gameObject);
        }

        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarAssetPath);
        if (animator.avatar != avatar)
        {
            Undo.RecordObject(animator, "Set Root Animator Avatar");
            animator.avatar = avatar;
        }
    }

    public Dictionary<string, string> FindPrefabDependencies(GameObject prefab)
    {
        var dependencies = new Dictionary<string, string>();
        if (prefab == null) return dependencies;

        string[] dependencyPaths = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab), true);
        
        foreach (string path in dependencyPaths)
        {
            if (path.EndsWith(".cs"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;

                string packageDir = Path.GetDirectoryName(path);
                string packageJsonPath = null;
                while (!string.IsNullOrEmpty(packageDir))
                {
                    string potentialPath = Path.Combine(packageDir, "package.json");
                    if (File.Exists(potentialPath))
                    {
                        packageJsonPath = potentialPath;
                        break;
                    }
                    if (Path.GetFileName(packageDir) == "Packages" || Path.GetFileName(packageDir) == "Assets") break;
                    packageDir = Directory.GetParent(packageDir)?.FullName;
                }
                
                if (string.IsNullOrEmpty(packageJsonPath)) continue;

                try
                {
                    string json = File.ReadAllText(packageJsonPath);
                    var packageInfo = JsonUtility.FromJson<PackageJson>(json);
                    if (packageInfo != null && !dependencies.ContainsKey(packageInfo.name))
                    {
                        dependencies.Add(packageInfo.name, $"^{packageInfo.version}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to parse {packageJsonPath}: {ex.Message}");
                }
            }
        }
        return dependencies;
    }
    
    [Serializable]
    private class PackageJson { public string name; public string version; }
    
    public string CreateVersionPackageForUpload(
        string newVersionString, 
        string baseFbxVersion,
        string originalFbxPath,
        GameObject customFbx,
        Avatar ultipawAvatar,
        GameObject logicPrefab,
        UltiPawVersion parentVersion)
    {
        string newVersionDataPath = UltiPawUtils.GetVersionDataPath(newVersionString, baseFbxVersion);
        string newVersionDataFullPath = Path.GetFullPath(newVersionDataPath);
        string tempZipPath = Path.Combine(Path.GetTempPath(), $"ultipaw_upload_{Guid.NewGuid()}.zip");

        try
        {
            UltiPawUtils.EnsureDirectoryExists(newVersionDataPath, canBeFilePath: false);
            
            // 1. Copy Avatars
            string ultipawAvatarSourcePath = AssetDatabase.GetAssetPath(ultipawAvatar);
            AssetDatabase.CopyAsset(ultipawAvatarSourcePath, Path.Combine(newVersionDataPath, UltiPawUtils.ULTIPAW_AVATAR_NAME));
            
            string defaultAvatarSourcePath = UltiPawUtils.GetVersionAvatarPath(parentVersion.version, parentVersion.defaultAviVersion, UltiPawUtils.DEFAULT_AVATAR_NAME);
            if (string.IsNullOrEmpty(defaultAvatarSourcePath) || !File.Exists(defaultAvatarSourcePath))
                throw new FileNotFoundException($"Could not find the required 'default avatar.asset' from the parent version '{parentVersion.version}'.");
            AssetDatabase.CopyAsset(defaultAvatarSourcePath, Path.Combine(newVersionDataPath, UltiPawUtils.DEFAULT_AVATAR_NAME));

            // 2. Create .bin file (Encrypt custom FBX against original base FBX)
            string customFbxPath = AssetDatabase.GetAssetPath(customFbx);
            byte[] baseData = File.ReadAllBytes(originalFbxPath);
            byte[] targetData = File.ReadAllBytes(customFbxPath);
            // In this case, we are creating the "key" (the .bin file). The operation is the same.
            byte[] encryptedData = XorTransform(baseData, targetData); 
            string binFilePath = Path.Combine(newVersionDataPath, "ultipaw.bin");
            File.WriteAllBytes(binFilePath, encryptedData);

            // 3. Create logic package
            string prefabSourcePath = AssetDatabase.GetAssetPath(logicPrefab);
            string prefabDestPath = Path.Combine(newVersionDataPath, "ultipaw logic.prefab");
            AssetDatabase.CopyAsset(prefabSourcePath, prefabDestPath);
            
            string packagePath = Path.Combine(newVersionDataPath, "ultipaw logic.unitypackage");
            AssetDatabase.ExportPackage(AssetDatabase.GetDependencies(prefabSourcePath, true), packagePath, ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);

            // 4. Create ZIP
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
            ZipFile.CreateFromDirectory(newVersionDataFullPath, tempZipPath, CompressionLevel.Optimal, false);

            return tempZipPath;
        }
        catch (Exception)
        {
            // Cleanup on failure
            if (Directory.Exists(newVersionDataPath)) Directory.Delete(newVersionDataPath, true);
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
            throw; // Re-throw the exception for the module to catch and display
        }
    }
}
#endif