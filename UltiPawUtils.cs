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

    // Generates the path for a specific version's data folder
    // Uses UltiPaw version and the *Base FBX* version string
    public static string GetVersionDataPath(string ultiPawVersion, string defaultFbxVersion)
    {
        if (string.IsNullOrEmpty(ultiPawVersion) || string.IsNullOrEmpty(defaultFbxVersion))
        {
            Debug.LogError("[UltiPawUtils] Cannot generate version path with null/empty version strings.");
            // Return a fallback or handle appropriately, maybe use hash as fallback?
            // For now, return path based on input even if potentially invalid.
            return $"{VERSIONS_FOLDER}/u{ultiPawVersion ?? "unknown" }d{defaultFbxVersion ?? "unknown"}";
        }
        // Clean version strings slightly (replace dots, spaces if needed, though usually not necessary for folders)
        // string cleanUltiPawVersion = ultiPawVersion.Replace(".", "_");
        // string cleanDefaultFbxVersion = defaultFbxVersion.Replace(".", "_");
        return $"{VERSIONS_FOLDER}/u{ultiPawVersion}d{defaultFbxVersion}";
    }

    // Generates the full path to the downloaded .bin file for a version
    public static string GetVersionBinPath(string ultiPawVersion, string defaultFbxVersion)
    {
        // Ensure the base path is valid before appending
        string dataPath = GetVersionDataPath(ultiPawVersion, defaultFbxVersion);
        return $"{dataPath}/ultipaw.bin"; // Assuming bin file is always named this
    }

    // Generates the full path to an avatar file within a version folder
    public static string GetVersionAvatarPath(string ultiPawVersion, string defaultFbxVersion, string relativeAvatarPath)
    {
        if (string.IsNullOrEmpty(relativeAvatarPath)) return null;
        string dataPath = GetVersionDataPath(ultiPawVersion, defaultFbxVersion);
        // Use Path.Combine for robustness across OS
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
