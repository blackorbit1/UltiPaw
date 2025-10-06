#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

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
    private static HashSet<int> failedRequests = new HashSet<int>();
    
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
        // Don't request if already cached
        if (userCache.ContainsKey(uploaderId))
        {
            onComplete?.Invoke();
            return;
        }
        
        // Don't request if already pending
        if (pendingRequests.Contains(uploaderId))
        {
            return;
        }
        
        // Don't request if previously failed (unless explicitly cleared)
        if (failedRequests.Contains(uploaderId))
        {
            onComplete?.Invoke();
            return;
        }
        
        pendingRequests.Add(uploaderId);
        EditorCoroutineUtility.StartCoroutineOwnerless(FetchUserInfo(uploaderId, null, onComplete));
    }
    
    // Overload allowing an explicit auth token when auth.dat is not available yet
    public static void RequestUserInfo(int uploaderId, string authToken, System.Action onComplete)
    {
        // Don't request if already cached
        if (userCache.ContainsKey(uploaderId))
        {
            onComplete?.Invoke();
            return;
        }
        
        // Don't request if already pending
        if (pendingRequests.Contains(uploaderId))
        {
            return;
        }
        
        // Don't request if previously failed (unless explicitly cleared)
        if (failedRequests.Contains(uploaderId))
        {
            onComplete?.Invoke();
            return;
        }
        
        pendingRequests.Add(uploaderId);
        EditorCoroutineUtility.StartCoroutineOwnerless(FetchUserInfo(uploaderId, authToken, onComplete));
    }
    
    private static IEnumerator FetchUserInfo(int userId, string overrideToken, System.Action onComplete)
    {
        string tokenToUse = overrideToken;
        if (string.IsNullOrEmpty(tokenToUse))
        {
            var auth = UltiPawUtils.GetAuth();
            if (auth != null) tokenToUse = auth.token;
        }
        
        if (string.IsNullOrEmpty(tokenToUse))
        {
            pendingRequests.Remove(userId);
            failedRequests.Add(userId);
            onComplete?.Invoke();
            yield break;
        }
        
        string url = $"{UltiPawUtils.getServerUrl()}/user?u={userId}&t={tokenToUse}";
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();
            
            pendingRequests.Remove(userId);
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var userInfo = JsonConvert.DeserializeObject<UserInfo>(request.downloadHandler.text);
                    if (userInfo != null)
                    {
                        userCache[userId] = userInfo;
                        failedRequests.Remove(userId); // Remove from failed list on success
                        
                        // Start downloading avatar if we have a URL
                        if (!string.IsNullOrEmpty(userInfo.avatarUrl))
                        {
                            EditorCoroutineUtility.StartCoroutineOwnerless(DownloadAvatar(userId, userInfo.avatarUrl));
                        }
                    }
                }
                catch (Exception ex)
                {
                    UltiPawLogger.LogError($"[UltiPaw] Failed to parse user info for ID {userId}: {ex.Message}");
                }
            }
            else
            {
                failedRequests.Add(userId);
                UltiPawLogger.LogWarning($"[UltiPaw] Failed to fetch user info for ID {userId}: {request.error}");
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
                        Texture2D processed = MakeCircularAvatar(texture) ?? texture;
                        byte[] pngData = processed.EncodeToPNG();
                        File.WriteAllBytes(localPath, pngData);

                        avatarCache[uploaderId] = processed;
                        UltiPawLogger.Log($"[UltiPaw] Downloaded and cached avatar for user {uploaderId}");

                        if (!ReferenceEquals(processed, texture))
                        {
                            Object.DestroyImmediate(texture);
                        }
                    }
                }
                catch (Exception ex)
                {
                    UltiPawLogger.LogError($"[UltiPaw] Failed to process avatar for user {uploaderId}: {ex.Message}");
                }
            }
            else
            {
                UltiPawLogger.LogWarning($"[UltiPaw] Failed to download avatar for user {uploaderId}: {request.error}");
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
                Texture2D processed = MakeCircularAvatar(texture) ?? texture;
                avatarCache[uploaderId] = processed;

                if (!ReferenceEquals(processed, texture))
                {
                    File.WriteAllBytes(localPath, processed.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                }

                UltiPawLogger.Log($"[UltiPaw] Loaded cached avatar for user {uploaderId}");
            }
        }
        catch (Exception ex)
        {
            UltiPawLogger.LogError($"[UltiPaw] Failed to load cached avatar for user {uploaderId}: {ex.Message}");
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
    
    private static Texture2D MakeCircularAvatar(Texture2D texture)
    {
        if (texture == null)
            return null;

        try
        {
            int size = Mathf.Min(texture.width, texture.height);
            int xOffset = Mathf.Max(0, (texture.width - size) / 2);
            int yOffset = Mathf.Max(0, (texture.height - size) / 2);
            Color[] sourcePixels = texture.GetPixels(xOffset, yOffset, size, size);

            float radius = size * 0.5f;
            float radiusSquared = radius * radius;

            for (int y = 0; y < size; y++)
            {
                float dy = (y + 0.5f) - radius;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - radius;
                    int index = y * size + x;
                    if ((dx * dx) + (dy * dy) > radiusSquared)
                    {
                        Color c = sourcePixels[index];
                        c.a = 0f;
                        sourcePixels[index] = c;
                    }
                }
            }

            Texture2D circular = new Texture2D(size, size, TextureFormat.RGBA32, false);
            circular.SetPixels(sourcePixels);
            circular.Apply();
            circular.wrapMode = TextureWrapMode.Clamp;
            circular.filterMode = FilterMode.Bilinear;
            circular.name = texture.name;
            return circular;
        }
        catch (UnityException ex)
        {
            UltiPawLogger.LogWarning($"[UltiPaw] Avatar texture for circular mask was not readable: {ex.Message}");
            return texture;
        }
    }

    public static bool IsUserInfoAvailable(int uploaderId)
    {
        return userCache.ContainsKey(uploaderId);
    }
    
    // Clear failed requests to allow retry (e.g., when user clicks refresh)
    public static void ClearFailedRequest(int uploaderId)
    {
        failedRequests.Remove(uploaderId);
    }
    
    // Clear all failed requests (e.g., on component reload or manual refresh all)
    public static void ClearAllFailedRequests()
    {
        failedRequests.Clear();
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