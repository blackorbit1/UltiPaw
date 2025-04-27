#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class UltiPawUtils
{
    public const string SCRIPT_VERSION = "0.1";
    public const string BASE_FOLDER = "Packages/ultipaw";
    public const string VERSIONS_FOLDER = BASE_FOLDER + "/versions";
    public const string DEFAULT_AVATAR_NAME = "default avatar.asset";
    public const string ULTIPAW_AVATAR_NAME = "ultipaw avatar.asset";

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

    // Generates the path for a specific version's data folder
    // Uses UltiPaw version and the *Base FBX* version string
    public static string GetVersionDataPath(string ultiPawVersion, string defaultFbxVersion)
    {
        // *** Return null if inputs are invalid ***
        if (string.IsNullOrEmpty(ultiPawVersion) || string.IsNullOrEmpty(defaultFbxVersion))
        {
            // *** Log Warning instead of Error, and only if needed (maybe remove log entirely) ***
            // Debug.LogWarning("[UltiPawUtils] Cannot generate version path with null/empty version strings.");
            return null; // Indicate failure clearly
        }
        return $"{VERSIONS_FOLDER}/u{ultiPawVersion}d{defaultFbxVersion}";
    }

    // Generates the full path to the downloaded .bin file for a version
    public static string GetVersionBinPath(string ultiPawVersion, string defaultFbxVersion)
    {
        string dataPath = GetVersionDataPath(ultiPawVersion, defaultFbxVersion);
        // *** Handle null return from GetVersionDataPath ***
        if (dataPath == null) return null;
        return $"{dataPath}/ultipaw.bin";
    }

    // Generates the full path to an avatar file within a version folder
    public static string GetVersionAvatarPath(string ultiPawVersion, string defaultFbxVersion, string relativeAvatarPath)
    {
        if (string.IsNullOrEmpty(relativeAvatarPath)) return null;
        string dataPath = GetVersionDataPath(ultiPawVersion, defaultFbxVersion);
        // *** Handle null return from GetVersionDataPath ***
        if (dataPath == null) return null;
        return Path.Combine(dataPath, relativeAvatarPath).Replace("\\", "/");
    }


    // Ensures the directory exists
    public static void EnsureDirectoryExists(string directoryPath) // Renamed parameter for clarity
    {
        // Check if the path is actually a directory path, not a file path
        string directory = directoryPath;
        if (!string.IsNullOrEmpty(Path.GetExtension(directoryPath))) // If it has an extension, likely a file path
        {
            directory = Path.GetDirectoryName(directoryPath);
        }

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh(); // Make Unity aware of the new folder
            }
            catch (System.Exception e)
            {
                 Debug.LogError($"[UltiPawUtils] Failed to create directory '{directory}': {e.Message}");
            }
        }
    }
}
#endif
