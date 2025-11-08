#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

// Handles the UI and logic for user authentication.
public class AuthenticationModule
{
    private readonly UltiPawEditor editor;

    public AuthenticationModule(UltiPawEditor editor)
    {
        this.editor = editor;
    }

    public void DrawMagicSyncAuth()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Magic Sync", GUILayout.Width(120f), GUILayout.Height(30f)))
        {
            // TODO: Move to AuthenticationService
            RegisterAuth().ContinueWith(task =>
            {
                // Queue the result to be processed on the main thread
                EditorApplication.delayCall += () =>
                {
                    if (task.Result)
                    {
                        editor.CheckAuthentication(); // Update state in the main editor
                        editor.Repaint();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Authentication Failed", "Please visit the Orbiters website and click 'Magic Sync' first.", "OK");
                    }
                };
            });
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox("Use Magic Sync to authenticate this tool. Go to the Orbiters website, click 'Magic Sync' to copy your token, then click the button above.", MessageType.Info);
        EditorGUILayout.Space(5);
    }

    public void DrawLogoutButton()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
        if (GUILayout.Button("Logout", GUILayout.Width(100f), GUILayout.Height(25f)))
        {
            if (EditorUtility.DisplayDialog("Confirm Logout", "Are you sure you want to log out?", "Logout", "Cancel"))
            {
                // TODO: Move to AuthenticationService
                if (RemoveAuth())
                {
                    editor.isAuthenticated = false;
                    editor.authToken = null;
                    editor.Repaint();
                }
            }
        }
        GUI.backgroundColor = Color.white;

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(10);
    }

    private const string AUTH_FILENAME = "auth.dat";

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
                    var response = await UltiPawUtils.client.GetAsync(UltiPawUtils.getApiUrl() + UltiPawUtils.TOKEN_ENDPOINT + "?token=" + tokenToUse);

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
                UltiPawUtils.EnsureDirectoryExists(authPath);
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

    private static async Task<bool> ValidateTokenWithServer(string token)
    {
        try
        {
            var response = await UltiPawUtils.client.GetAsync(UltiPawUtils.getApiUrl() + UltiPawUtils.TOKEN_ENDPOINT + token);
            return response.IsSuccessStatusCode;
        }
        catch (System.Exception e)
        {
            UltiPawLogger.LogError($"[UltiPawUtils] Error validating token with server: {e.Message}");
            return false;
        }
    }

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
}
#endif