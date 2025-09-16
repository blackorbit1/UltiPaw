#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

[JsonObject(MemberSerialization.OptIn)]
public class UserInfo
{
    [JsonProperty] public string username;
    [JsonProperty] public string avatarUrl;
}

public class UserService
{
    private static Dictionary<int, UserInfo> userCache = new Dictionary<int, UserInfo>();
    private static Dictionary<int, Texture2D> avatarCache = new Dictionary<int, Texture2D>();
    private static HashSet<int> pendingRequests = new HashSet<int>();
    
    private const string AVATARS_FOLDER = "Packages/ultipaw/data/avatars";
    
    static UserService()
    {
        // Ensure avatars folder exists
        if (!Directory.Exists(AVATARS_FOLDER))
        {
            Directory.CreateDirectory(AVATARS_FOLDER);
        }
    }
    
    public static void RequestUserInfo(int uploaderId, System.Action onComplete = null)
    {
        // Skip if already cached or request is pending
        // if (userCache.ContainsKey(uploaderId) || pendingRequests.Contains(uploaderId))
        // {
        //     onComplete?.Invoke();
        //     return;
        // }
        
        pendingRequests.Add(uploaderId);
        EditorCoroutineUtility.StartCoroutineOwnerless(FetchUserInfo(uploaderId, onComplete));
    }
    
    private static IEnumerator FetchUserInfo(int uploaderId, System.Action onComplete)
    {
        var auth = UltiPawUtils.GetAuth();
        if (auth == null)
        {
            pendingRequests.Remove(uploaderId);
            onComplete?.Invoke();
            yield break;
        }
        
        string url = $"{UltiPawUtils.getServerUrl()}/user?u={uploaderId}&t={auth.token}";
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();
            
            pendingRequests.Remove(uploaderId);
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var userInfo = JsonConvert.DeserializeObject<UserInfo>(request.downloadHandler.text);
                    if (userInfo != null)
                    {
                        userCache[uploaderId] = userInfo;
                        
                        // Start downloading avatar if we have a URL
                        if (!string.IsNullOrEmpty(userInfo.avatarUrl))
                        {
                            EditorCoroutineUtility.StartCoroutineOwnerless(DownloadAvatar(uploaderId, userInfo.avatarUrl));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UltiPaw] Failed to parse user info for ID {uploaderId}: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[UltiPaw] Failed to fetch user info for ID {uploaderId}: {request.error}");
            }
            
            onComplete?.Invoke();
        }
    }
    
    private static IEnumerator DownloadAvatar(int uploaderId, string avatarUrl)
    {
        string localPath = Path.Combine(AVATARS_FOLDER, $"avatar_{uploaderId}.png");
        
        // Check if avatar already exists locally
        if (File.Exists(localPath))
        {
            LoadLocalAvatar(uploaderId, localPath);
            yield break;
        }
        
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(avatarUrl))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    if (texture != null)
                    {
                        // Save to disk
                        byte[] pngData = texture.EncodeToPNG();
                        File.WriteAllBytes(localPath, pngData);
                        
                        // Cache in memory
                        avatarCache[uploaderId] = texture;
                        
                        Debug.Log($"[UltiPaw] Downloaded and cached avatar for user {uploaderId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UltiPaw] Failed to process avatar for user {uploaderId}: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[UltiPaw] Failed to download avatar for user {uploaderId}: {request.error}");
            }
        }
    }
    
    private static void LoadLocalAvatar(int uploaderId, string localPath)
    {
        try
        {
            byte[] fileData = File.ReadAllBytes(localPath);
            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(fileData))
            {
                avatarCache[uploaderId] = texture;
                Debug.Log($"[UltiPaw] Loaded cached avatar for user {uploaderId}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UltiPaw] Failed to load cached avatar for user {uploaderId}: {ex.Message}");
        }
    }
    
    public static UserInfo GetUserInfo(int uploaderId)
    {
        return userCache.ContainsKey(uploaderId) ? userCache[uploaderId] : null;
    }
    
    public static Texture2D GetUserAvatar(int uploaderId)
    {
        // Try to load from disk if not in memory cache
        if (!avatarCache.ContainsKey(uploaderId))
        {
            string localPath = Path.Combine(AVATARS_FOLDER, $"avatar_{uploaderId}.png");
            if (File.Exists(localPath))
            {
                LoadLocalAvatar(uploaderId, localPath);
            }
        }
        
        return avatarCache.ContainsKey(uploaderId) ? avatarCache[uploaderId] : null;
    }
    
    public static bool IsUserInfoAvailable(int uploaderId)
    {
        return userCache.ContainsKey(uploaderId);
    }
    
    public static void PreloadUserInfo(List<UltiPawVersion> versions)
    {
        HashSet<int> uploaderIds = new HashSet<int>();
        
        foreach (var version in versions)
        {
            if (version.uploaderId > 0 && !IsUserInfoAvailable(version.uploaderId))
            {
                uploaderIds.Add(version.uploaderId);
            }
        }
        
        foreach (int uploaderId in uploaderIds)
        {
            RequestUserInfo(uploaderId);
        }
    }
}
#endif