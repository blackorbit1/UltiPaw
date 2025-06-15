#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditorInternal;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

public static class UltiPawUtils
{
    public const string SCRIPT_VERSION = "0.1";
    public const string PACKAGE_BASE_FOLDER = "Packages/UltiPaw";
    public const string ASSETS_BASE_FOLDER = "Assets/UltiPaw";
    public const string VERSIONS_FOLDER = ASSETS_BASE_FOLDER + "/versions";
    public const string DEFAULT_AVATAR_NAME = "default avatar.asset";
    public const string ULTIPAW_AVATAR_NAME = "ultipaw avatar.asset";
    public const string CUSTOM_LOGIC_NAME = "ultipaw logic.asset";

    public const string SERVER_BASE_URL = "http://orbiters.cc:4000/api/unity-wizard"; // Update with your server URL
    public const string VERSION_ENDPOINT = "/ultipaw/versions";
    public const string MODEL_ENDPOINT = "/ultipaw/model";
    private const string TOKEN_ENDPOINT = "/token"; // Replace with your actual API endpoint

    public const string NEW_VERSION_ENDPOINT = "/ultipaw/newVersion";
    public static readonly string PACKAGE_BASE_FOLDER_FULL_PATH = Path.Combine(Application.dataPath, "UltiPaw");

    private const string AUTH_FILENAME = "auth.dat";
    private static HttpClient client = new HttpClient();

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

    // Add this class to store authentication data
    [JsonObject(MemberSerialization.OptIn)]
    public class AuthData
    {
        [JsonProperty] public string token;
        [JsonProperty] public string user;
    }

    // Registers authentication token from clipboard
    public static async Task<bool> RegisterAuth()
    {
        try
        {
            // Get token from clipboard
            string clipboardContent = EditorGUIUtility.systemCopyBuffer;
            string tokenToUse = "notoken";

            // Check if clipboard content matches the pattern "orbit-\w{8}-\w{8}-\w{8}-\d{2}-\d{2}-\d{4}"
            Regex tokenPattern = new Regex(@"orbit-\w{8}-\w{8}-\w{8}-\d{2}-\d{2}-\d{4}");
            if (!string.IsNullOrEmpty(clipboardContent) && tokenPattern.IsMatch(clipboardContent))
            {
                tokenToUse = clipboardContent;
                Debug.Log("[UltiPawUtils] Found valid token pattern in clipboard");
            }

            // Try to validate the token with the server, with retry mechanism for error 425
            AuthData authData = null;
            bool isValid = false;
            int retryCount = 0;
            const int maxRetries = 10;

            while (retryCount < maxRetries && !isValid)
            {
                try
                {
                    // Make the request to the server
                    var response = await client.GetAsync(SERVER_BASE_URL + TOKEN_ENDPOINT + "?token=" + tokenToUse);

                    if (response.IsSuccessStatusCode)
                    {
                        // Read the JSON response
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        authData = JsonConvert.DeserializeObject<AuthData>(jsonResponse);
                        isValid = !string.IsNullOrEmpty(authData?.token);

                        if (isValid)
                        {
                            Debug.Log("[UltiPawUtils] Authentication successful");
                            break;
                        }
                    }
                    else if ((int)response.StatusCode == 425)
                    {
                        // Status code 425 - need to retry
                        retryCount++;
                        Debug.Log(
                            $"[UltiPawUtils] Server is processing request (Status 425). Retry {retryCount}/{maxRetries}");
                        await Task.Delay(1000); // Wait 1 second before retrying
                    }
                    else
                    {
                        // Other error - don't retry
                        Debug.LogWarning(
                            $"[UltiPawUtils] Authentication failed with status code {response.StatusCode}");
                        return false;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UltiPawUtils] Error during authentication attempt {retryCount + 1}: {e.Message}");
                    retryCount++;
                    await Task.Delay(1000);
                }
            }

            if (isValid && authData != null)
            {
                // Save the auth data
                string authPath = GetAuthFilePath();
                EnsureDirectoryExists(authPath);

                // Serialize the auth data to JSON
                string authJson = JsonConvert.SerializeObject(authData);

                // Simple encryption by converting to bytes and XOR with a key
                byte[] authBytes = Encoding.UTF8.GetBytes(authJson);
                byte[] encryptedBytes = new byte[authBytes.Length];
                byte[] key = Encoding.UTF8.GetBytes("UltiPawMagicSync"); // Simple encryption key

                for (int i = 0; i < authBytes.Length; i++)
                {
                    encryptedBytes[i] = (byte)(authBytes[i] ^ key[i % key.Length]);
                }

                File.WriteAllBytes(authPath, encryptedBytes);
                Debug.Log("[UltiPawUtils] Authentication data stored successfully");
                return true;
            }
            else
            {
                Debug.LogWarning("[UltiPawUtils] Authentication failed after maximum retries");
                return false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UltiPawUtils] Error registering authentication: {e.Message}");
            return false;
        }
    }

    // Gets the current authentication data
    public static AuthData GetAuth()
    {
        try
        {
            string authPath = GetAuthFilePath();

            if (!File.Exists(authPath))
            {
                return null;
            }

            byte[] encryptedBytes = File.ReadAllBytes(authPath);
            byte[] key = Encoding.UTF8.GetBytes("UltiPawMagicSync");
            byte[] authBytes = new byte[encryptedBytes.Length];

            // Decrypt the data
            for (int i = 0; i < encryptedBytes.Length; i++)
            {
                authBytes[i] = (byte)(encryptedBytes[i] ^ key[i % key.Length]);
            }

            string authJson = Encoding.UTF8.GetString(authBytes);
            return JsonConvert.DeserializeObject<AuthData>(authJson);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UltiPawUtils] Error retrieving authentication: {e.Message}");
            return null;
        }
    }

    // Checks if the user has valid authentication
    public static bool HasAuth()
    {
        AuthData auth = GetAuth();
        return auth != null && !string.IsNullOrEmpty(auth.token);
    }

    // Validates the token with the server
    private static async Task<bool> ValidateTokenWithServer(string token)
    {
        try
        {
            var response = await client.GetAsync(SERVER_BASE_URL + TOKEN_ENDPOINT + token);
            return response.IsSuccessStatusCode;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UltiPawUtils] Error validating token with server: {e.Message}");
            return false;
        }
    }

    // Removes the authentication data file
    public static bool RemoveAuth()
    {
        try
        {
            string authPath = GetAuthFilePath();

            if (File.Exists(authPath))
            {
                File.Delete(authPath);
                Debug.Log("[UltiPawUtils] Authentication data removed successfully");
                return true;
            }
            else
            {
                Debug.Log("[UltiPawUtils] No authentication data found to remove");
                return false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UltiPawUtils] Error removing authentication data: {e.Message}");
            return false;
        }
    }

    // Gets the path to the authentication file
    private static string GetAuthFilePath()
    {
        string authFolder = Path.Combine(InternalEditorUtility.unityPreferencesFolder, "UltiPaw");
        return Path.Combine(authFolder, AUTH_FILENAME);
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
    public static string GetVersionAvatarPath(string ultiPawVersion, string defaultFbxVersion,
        string relativeAvatarPath)
    {
        if (string.IsNullOrEmpty(relativeAvatarPath)) return null;
        string dataPath = GetVersionDataPath(ultiPawVersion, defaultFbxVersion);
        // *** Handle null return from GetVersionDataPath ***
        if (dataPath == null) return null;
        return Path.Combine(dataPath, relativeAvatarPath).Replace("\\", "/");
    }


    // Ensures the directory exists
    public static void EnsureDirectoryExists(string directoryPath, bool canBeFilePath = true)
    {
        // Check if the path is actually a directory path, not a file path
        string directory = directoryPath;

        if (canBeFilePath && !string.IsNullOrEmpty(Path.GetExtension(directoryPath)))
        {
            // If it has an extension and canBeFilePath is true, treat it as a file path
            directory = Path.GetDirectoryName(directoryPath);
        }
        // If canBeFilePath is false, treat the entire path as a directory path regardless of extension

        if (!string.IsNullOrEmpty(directory))
        {
            // Convert Unity relative path to absolute system path
            string absoluteDirectory;
            if (directory.StartsWith("Assets/") || directory.StartsWith("Assets\\"))
            {
                // Convert Unity Assets path to absolute path
                // Remove "Assets/" and combine with Application.dataPath
                string relativePath = directory.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar);
                absoluteDirectory = Path.Combine(Application.dataPath, relativePath);
            }
            else if (directory.StartsWith("Packages/") || directory.StartsWith("Packages\\"))
            {
                // Handle Packages path - go one level up from dataPath then into Packages
                string relativePath = directory.Substring("Packages/".Length).Replace('/', Path.DirectorySeparatorChar);
                absoluteDirectory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Packages", relativePath);
            }
            else if (Path.IsPathRooted(directory))
            {
                // Already absolute path
                absoluteDirectory = directory;
            }
            else
            {
                // Relative path, make it relative to Application.dataPath
                absoluteDirectory = Path.Combine(Application.dataPath, directory);
            }

            // Normalize the path
            absoluteDirectory = Path.GetFullPath(absoluteDirectory);

            Debug.Log(
                $"[UltiPawUtils] EnsureDirectoryExists - Input: '{directoryPath}' (canBeFilePath: {canBeFilePath}) -> Directory: '{directory}' -> Absolute: '{absoluteDirectory}'");
            Debug.Log($"[UltiPawUtils] Directory exists check: {Directory.Exists(absoluteDirectory)}");

            if (!Directory.Exists(absoluteDirectory))
            {
                try
                {
                    Debug.Log($"[UltiPawUtils] Creating directory: {absoluteDirectory}");
                    Directory.CreateDirectory(absoluteDirectory);

                    // Verify creation
                    if (Directory.Exists(absoluteDirectory))
                    {
                        Debug.Log($"[UltiPawUtils] Successfully created directory: {absoluteDirectory}");
                        AssetDatabase.Refresh(); // Make Unity aware of the new folder
                    }
                    else
                    {
                        Debug.LogError(
                            $"[UltiPawUtils] Directory creation appeared to succeed but directory still doesn't exist: {absoluteDirectory}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UltiPawUtils] Failed to create directory '{absoluteDirectory}': {e.Message}");
                    Debug.LogError($"[UltiPawUtils] Exception details: {e}");
                    throw; // Re-throw to let caller handle if needed
                }
            }
            else
            {
                Debug.Log($"[UltiPawUtils] Directory already exists: {absoluteDirectory}");
            }
        }
        else
        {
            Debug.LogWarning(
                $"[UltiPawUtils] EnsureDirectoryExists called with empty or invalid directory path: '{directoryPath}' (canBeFilePath: {canBeFilePath})");
        }
    }
}
#endif