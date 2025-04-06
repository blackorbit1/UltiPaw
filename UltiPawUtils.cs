#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class UltiPawUtils
{
    public const string SCRIPT_VERSION = "0.1";
    public const string BASE_FOLDER = "Assets/UltiPaw";
    public const string VERSIONS_FOLDER = BASE_FOLDER + "/versions";

    // Calculates SHA256 hash of a file
    public static string CalculateFileHash(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"[UltiPawUtils] File not found for hashing: {filePath}");
            return null;
        }

        using (var sha256 = SHA256.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                // Convert byte array to a lowercase hex string
                StringBuilder builder = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }

    // Generates the path for a specific version's data
    public static string GetVersionDataPath(string ultiPawVersion, string baseFbxHash)
    {
        // Use a shortened hash for the folder name for brevity if desired
        string shortHash = baseFbxHash.Length > 8 ? baseFbxHash.Substring(0, 8) : baseFbxHash;
        return $"{VERSIONS_FOLDER}/u{ultiPawVersion}d{shortHash}";
    }

    // Generates the full path to the downloaded .bin file for a version
    public static string GetVersionBinPath(string ultiPawVersion, string baseFbxHash)
    {
        return $"{GetVersionDataPath(ultiPawVersion, baseFbxHash)}/ultipaw.bin";
    }

     // Ensures the directory exists
    public static void EnsureDirectoryExists(string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh(); // Make Unity aware of the new folder
        }
    }
}
#endif
