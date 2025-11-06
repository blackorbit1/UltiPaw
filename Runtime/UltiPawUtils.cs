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
    public const string UNSUBMITTED_VERSIONS_FILE = ASSETS_BASE_FOLDER + "/unsubmittedVersions.json";
    public const string DEFAULT_AVATAR_NAME = "default avatar.asset";
    public const string ULTIPAW_AVATAR_NAME = "ultipaw avatar.asset";
    public const string CUSTOM_LOGIC_NAME = "ultipaw logic.asset";

    // EditorPrefs key for Dev Environment setting
    private const string DevEnvironmentPrefKey = "UltiPaw_DevEnvironment";
    
    // Dev Environment property with persistent storage
    public static bool isDevEnvironment
    {
        get
        {
            try { return EditorPrefs.GetBool(DevEnvironmentPrefKey, false); }
            catch { return false; }
        }
        set
        {
            try { EditorPrefs.SetBool(DevEnvironmentPrefKey, value); } catch { }
        }
    }
    
    public const string SERVER_BASE_URL = "orbiters.cc/"; // Update with your server URL
    public const string API_BASE_URL = "api." + SERVER_BASE_URL + "/"; // Update with your server URL
    public const string VERSION_ENDPOINT = "/ultipaw/versions";
    public const string MODEL_ENDPOINT = "/ultipaw/model";
    private const string TOKEN_ENDPOINT = "/token"; // Replace with your actual API endpoint
    
    public const string NEW_VERSION_ENDPOINT = "/ultipaw/newVersion";
    public const string CHECK_CONNECTION_ENDPOINT = "/check-connection";
    public static readonly string PACKAGE_BASE_FOLDER_FULL_PATH = Path.Combine(Application.dataPath, "UltiPaw");

    private const string AUTH_FILENAME = "auth.dat";
    private static HttpClient client = new HttpClient();
    
    public static string getApiUrl(string scope = "unity-wizard")
    {
        if (isDevEnvironment)
        {
            return "http://localhost:4100/" + scope;
        }
        return "https://" + API_BASE_URL + scope;
    }

    public static string getWebsiteUrl()
    {
        if (isDevEnvironment)
        {
            return "https://dev." + SERVER_BASE_URL;
        }
        return "https://" + SERVER_BASE_URL;
    }

    // Calculates SHA256 hash of a file
    public static string CalculateFileHash(string filePath)
    {
        if (!File.Exists(filePath))
        {
            UltiPawLogger.LogError($"[UltiPawUtils] File not found for hashing: {filePath}");
            return null;
        }

        using (var sha256 = SHA256.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                StringBuilder builder = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class AuthData
    {
        [JsonProperty] public string token;
        [JsonProperty] public string user;
    }

    public static async Task<bool> RegisterAuth()
    {
        try
        {
            string clipboardContent = EditorGUIUtility.systemCopyBuffer;
            string tokenToUse = "notoken";

            Regex tokenPattern = new Regex(@"orbit-\w{8}-\w{8}-\w{8}-\d{2}-\d{2}-\d{4}");
            if (!string.IsNullOrEmpty(clipboardContent) && tokenPattern.IsMatch(clipboardContent))
            {
                tokenToUse = clipboardContent;
                UltiPawLogger.Log("[UltiPawUtils] Found valid token pattern in clipboard");
            }

            AuthData authData = null;
            bool isValid = false;
            int retryCount = 0;
            const int maxRetries = 10;

            while (retryCount < maxRetries && !isValid)
            {
                try
                {
                    var response = await client.GetAsync(getApiUrl() + TOKEN_ENDPOINT + "?token=" + tokenToUse);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        authData = JsonConvert.DeserializeObject<AuthData>(jsonResponse);
                        isValid = !string.IsNullOrEmpty(authData?.token);
                        if (isValid) break;
                    }
                    else if ((int)response.StatusCode == 425)
                    {
                        retryCount++;
                        UltiPawLogger.Log($"[UltiPawUtils] Server is processing request (Status 425). Retry {retryCount}/{maxRetries}");
                        await Task.Delay(1000);
                    }
                    else
                    {
                        UltiPawLogger.LogWarning($"[UltiPawUtils] Authentication failed with status code {response.StatusCode}");
                        return false;
                    }
                }
                catch (System.Exception e)
                {
                    UltiPawLogger.LogError($"[UltiPawUtils] Error during authentication attempt {retryCount + 1}: {e.Message}");
                    retryCount++;
                    await Task.Delay(1000);
                }
            }

            if (isValid && authData != null)
            {
                string authPath = GetAuthFilePath();
                EnsureDirectoryExists(authPath);
                string authJson = JsonConvert.SerializeObject(authData);
                byte[] authBytes = Encoding.UTF8.GetBytes(authJson);
                byte[] encryptedBytes = new byte[authBytes.Length];
                byte[] key = Encoding.UTF8.GetBytes("UltiPawMagicSync");

                for (int i = 0; i < authBytes.Length; i++)
                {
                    encryptedBytes[i] = (byte)(authBytes[i] ^ key[i % key.Length]);
                }

                File.WriteAllBytes(authPath, encryptedBytes);
                UltiPawLogger.Log("[UltiPawUtils] Authentication data stored successfully");
                return true;
            }
            else
            {
                UltiPawLogger.LogWarning("[UltiPawUtils] Authentication failed after maximum retries");
                return false;
            }
        }
        catch (System.Exception e)
        {
            UltiPawLogger.LogError($"[UltiPawUtils] Error registering authentication: {e.Message}");
            return false;
        }
    }

    public static AuthData GetAuth()
    {
        try
        {
            string authPath = GetAuthFilePath();
            if (!File.Exists(authPath)) return null;

            byte[] encryptedBytes = File.ReadAllBytes(authPath);
            byte[] key = Encoding.UTF8.GetBytes("UltiPawMagicSync");
            byte[] authBytes = new byte[encryptedBytes.Length];

            for (int i = 0; i < encryptedBytes.Length; i++)
            {
                authBytes[i] = (byte)(encryptedBytes[i] ^ key[i % key.Length]);
            }

            string authJson = Encoding.UTF8.GetString(authBytes);
            return JsonConvert.DeserializeObject<AuthData>(authJson);
        }
        catch (System.Exception e)
        {
            UltiPawLogger.LogError($"[UltiPawUtils] Error retrieving authentication: {e.Message}");
            return null;
        }
    }

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
            var response = await client.GetAsync(getApiUrl() + TOKEN_ENDPOINT + token);
            return response.IsSuccessStatusCode;
        }
        catch (System.Exception e)
        {
            UltiPawLogger.LogError($"[UltiPawUtils] Error validating token with server: {e.Message}");
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
                UltiPawLogger.Log("[UltiPawUtils] Authentication data removed successfully");
                return true;
            }
            else
            {
                UltiPawLogger.Log("[UltiPawUtils] No authentication data found to remove");
            return false;
            }
        }
        catch (System.Exception e)
        {
            UltiPawLogger.LogError($"[UltiPawUtils] Error removing authentication data: {e.Message}");
            return false;
        }
    }

    private static string GetAuthFilePath()
    {
        string authFolder = Path.Combine(InternalEditorUtility.unityPreferencesFolder, "UltiPaw");
        return Path.Combine(authFolder, AUTH_FILENAME);
    }

    public static string GetVersionDataPath(string ultiPawVersion, string defaultFbxVersion)
    {
        if (string.IsNullOrEmpty(ultiPawVersion) || string.IsNullOrEmpty(defaultFbxVersion))
        {
            return null;
        }
        return $"{VERSIONS_FOLDER}/u{ultiPawVersion}d{defaultFbxVersion}"; 
    }

    public static string GetVersionBinPath(string ultiPawVersion, string defaultFbxVersion)
    {
        string dataPath = GetVersionDataPath(ultiPawVersion, defaultFbxVersion);
        if (dataPath == null) return null;
        return $"{dataPath}/ultipaw.bin";
    }

    public static string GetVersionAvatarPath(string ultiPawVersion, string defaultFbxVersion, string relativeAvatarPath)
    {
        if (string.IsNullOrEmpty(relativeAvatarPath)) return null;
        string dataPath = GetVersionDataPath(ultiPawVersion, defaultFbxVersion);
        if (dataPath == null) return null;
        return Path.Combine(dataPath, relativeAvatarPath).Replace("\\", "/");
    }

    public static string GetUltiPawDataFolder()
    {
        // Get the Unity Editor preferences folder and create UltiPaw subfolder
        string dataFolder = Path.Combine(InternalEditorUtility.unityPreferencesFolder, "UltiPaw", "Data");
        
        // Ensure the directory exists
        if (!Directory.Exists(dataFolder))
        {
            try
            {
                Directory.CreateDirectory(dataFolder);
                UltiPawLogger.Log($"[UltiPawUtils] Created UltiPaw data folder: {dataFolder}");
            }
            catch (System.Exception ex)
            {
                UltiPawLogger.LogError($"[UltiPawUtils] Failed to create UltiPaw data folder: {ex.Message}");
                // Fallback to temp directory
                dataFolder = Path.Combine(Path.GetTempPath(), "UltiPaw", "Data");
                Directory.CreateDirectory(dataFolder);
            }
        }
        
        return dataFolder;
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

            UltiPawLogger.Log(
                $"[UltiPawUtils] EnsureDirectoryExists - Input: '{directoryPath}' (canBeFilePath: {canBeFilePath}) -> Directory: '{directory}' -> Absolute: '{absoluteDirectory}'");
            UltiPawLogger.Log($"[UltiPawUtils] Directory exists check: {Directory.Exists(absoluteDirectory)}");

            if (!Directory.Exists(absoluteDirectory))
        {
            try
            {
                    UltiPawLogger.Log($"[UltiPawUtils] Creating directory: {absoluteDirectory}");
                    Directory.CreateDirectory(absoluteDirectory);

                    // Verify creation
                    if (Directory.Exists(absoluteDirectory))
                    {
                        UltiPawLogger.Log($"[UltiPawUtils] Successfully created directory: {absoluteDirectory}");
                        AssetDatabase.Refresh(); // Make Unity aware of the new folder
                    }
                    else
                    {
                        UltiPawLogger.LogError(
                            $"[UltiPawUtils] Directory creation appeared to succeed but directory still doesn't exist: {absoluteDirectory}");
                    }
            }
            catch (System.Exception e)
            {
                    UltiPawLogger.LogError($"[UltiPawUtils] Failed to create directory '{absoluteDirectory}': {e.Message}");
                    UltiPawLogger.LogError($"[UltiPawUtils] Exception details: {e}");
                    throw; // Re-throw to let caller handle if needed
                }
            }
            else
            {
                UltiPawLogger.Log($"[UltiPawUtils] Directory already exists: {absoluteDirectory}");
            }
        }
        else
        {
            UltiPawLogger.LogWarning(
                $"[UltiPawUtils] EnsureDirectoryExists called with empty or invalid directory path: '{directoryPath}' (canBeFilePath: {canBeFilePath})");
        }
    }
    public static string ToUnityPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        return path.Replace("\\", "/");
    }

    public static string CombineUnityPath(params string[] segments)
    {
        if (segments == null || segments.Length == 0) return string.Empty;

        string result = null;
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment)) continue;

            if (string.IsNullOrEmpty(result))
            {
                result = segment.TrimEnd('/', '\\');
            }
            else
            {
                result = $"{result.TrimEnd('/', '\\')}/{segment.TrimStart('/', '\\')}";
            }
        }

        return ToUnityPath(result);
    }
}
#endif
