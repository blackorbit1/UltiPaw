#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

[Serializable]
public class HashCacheEntry
{
    public string filePath;
    public string hash;
    public long lastWriteTime; // File.GetLastWriteTime().ToBinary()
    public DateTime cacheTime;
    
    public HashCacheEntry(string filePath, string hash, long lastWriteTime)
    {
        this.filePath = filePath;
        this.hash = hash;
        this.lastWriteTime = lastWriteTime;
        this.cacheTime = DateTime.Now;
    }
    
    public bool IsValid()
    {
        if (!File.Exists(filePath))
            return false;
            
        var currentWriteTime = File.GetLastWriteTime(filePath).ToBinary();
        return currentWriteTime == lastWriteTime;
    }
}

[Serializable]
public class VersionCacheEntry
{
    public string baseFbxHash;
    public List<UltiPawVersion> serverVersions;
    public UltiPawVersion recommendedVersion;
    public DateTime cacheTime;
    public string authToken; // To invalidate cache when user changes
    
    public VersionCacheEntry(string baseFbxHash, List<UltiPawVersion> serverVersions, UltiPawVersion recommendedVersion, string authToken)
    {
        this.baseFbxHash = baseFbxHash;
        this.serverVersions = serverVersions ?? new List<UltiPawVersion>();
        this.recommendedVersion = recommendedVersion;
        this.cacheTime = DateTime.Now;
        this.authToken = authToken;
    }
    
    public bool IsValid(string currentBaseFbxHash, string currentAuthToken, TimeSpan maxAge)
    {
        return baseFbxHash == currentBaseFbxHash && 
               authToken == currentAuthToken &&
               DateTime.Now - cacheTime < maxAge;
    }
}

[Serializable]
public class PersistentCacheData
{
    public Dictionary<string, HashCacheEntry> hashCache = new Dictionary<string, HashCacheEntry>();
    public Dictionary<string, VersionCacheEntry> versionCache = new Dictionary<string, VersionCacheEntry>();
    public DateTime lastCleanup = DateTime.Now;
}

public class PersistentCache
{
    private static PersistentCache _instance;
    public static PersistentCache Instance
    {
        get
        {
            if (_instance == null)
                _instance = new PersistentCache();
            return _instance;
        }
    }

    private const string CACHE_FILE_NAME = "ultipaw_cache.json";
    private static readonly TimeSpan VERSION_CACHE_MAX_AGE = TimeSpan.FromHours(1); // Cache versions for 1 hour
    private static readonly TimeSpan HASH_CACHE_MAX_AGE = TimeSpan.FromDays(7); // Cache hashes for 7 days
    private static readonly TimeSpan CLEANUP_INTERVAL = TimeSpan.FromDays(1); // Cleanup old entries daily
    
    private PersistentCacheData cacheData;
    private string cacheFilePath;

    private PersistentCache()
    {
        cacheFilePath = Path.Combine(GetCacheDirectory(), CACHE_FILE_NAME);
        LoadCache();
        
        // Subscribe to editor update for periodic cleanup
        EditorApplication.update += PeriodicCleanup;
    }

    private string GetCacheDirectory()
    {
        string cacheDir = Path.Combine(UltiPawUtils.GetUltiPawDataFolder(), "cache");
        UltiPawUtils.EnsureDirectoryExists(cacheDir, false);
        return cacheDir;
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(cacheFilePath))
            {
                string json = File.ReadAllText(cacheFilePath);
                cacheData = JsonConvert.DeserializeObject<PersistentCacheData>(json);
                UltiPawLogger.Log($"[PersistentCache] Loaded cache with {cacheData.hashCache.Count} hash entries and {cacheData.versionCache.Count} version entries.");
            }
            else
            {
                cacheData = new PersistentCacheData();
                UltiPawLogger.Log("[PersistentCache] Created new cache data.");
            }
        }
        catch (Exception ex)
        {
            UltiPawLogger.LogError($"[PersistentCache] Failed to load cache: {ex.Message}");
            cacheData = new PersistentCacheData();
        }
    }

    private void SaveCache()
    {
        try
        {
            string json = JsonConvert.SerializeObject(cacheData, Formatting.Indented);
            File.WriteAllText(cacheFilePath, json);
            UltiPawLogger.Log($"[PersistentCache] Saved cache with {cacheData.hashCache.Count} hash entries and {cacheData.versionCache.Count} version entries.");
        }
        catch (Exception ex)
        {
            UltiPawLogger.LogError($"[PersistentCache] Failed to save cache: {ex.Message}");
        }
    }

    // Hash Cache Methods
    public string GetCachedHash(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        string normalizedPath = Path.GetFullPath(filePath);
        
        if (cacheData.hashCache.TryGetValue(normalizedPath, out var cacheEntry))
        {
            if (cacheEntry.IsValid() && DateTime.Now - cacheEntry.cacheTime < HASH_CACHE_MAX_AGE)
            {
                UltiPawLogger.Log($"[PersistentCache] Hash cache hit for: {normalizedPath}");
                return cacheEntry.hash;
            }
            else
            {
                // Remove invalid entry
                cacheData.hashCache.Remove(normalizedPath);
                UltiPawLogger.Log($"[PersistentCache] Hash cache invalidated for: {normalizedPath}");
            }
        }

        return null;
    }

    public void CacheHash(string filePath, string hash)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(hash) || !File.Exists(filePath))
            return;

        string normalizedPath = Path.GetFullPath(filePath);
        long lastWriteTime = File.GetLastWriteTime(normalizedPath).ToBinary();
        
        cacheData.hashCache[normalizedPath] = new HashCacheEntry(normalizedPath, hash, lastWriteTime);
        UltiPawLogger.Log($"[PersistentCache] Cached hash for: {normalizedPath}");
        
        SaveCache();
    }

    // Version Cache Methods
    public VersionCacheEntry GetCachedVersions(string baseFbxHash, string authToken)
    {
        if (string.IsNullOrEmpty(baseFbxHash) || string.IsNullOrEmpty(authToken))
            return null;

        string cacheKey = $"{baseFbxHash}_{authToken.GetHashCode()}";
        
        if (cacheData.versionCache.TryGetValue(cacheKey, out var cacheEntry))
        {
            if (cacheEntry.IsValid(baseFbxHash, authToken, VERSION_CACHE_MAX_AGE))
            {
                UltiPawLogger.Log($"[PersistentCache] Version cache hit for hash: {baseFbxHash}");
                return cacheEntry;
            }
            else
            {
                // Remove invalid entry
                cacheData.versionCache.Remove(cacheKey);
                UltiPawLogger.Log($"[PersistentCache] Version cache invalidated for hash: {baseFbxHash}");
            }
        }

        return null;
    }

    public void CacheVersions(string baseFbxHash, List<UltiPawVersion> serverVersions, UltiPawVersion recommendedVersion, string authToken)
    {
        if (string.IsNullOrEmpty(baseFbxHash) || string.IsNullOrEmpty(authToken))
            return;

        string cacheKey = $"{baseFbxHash}_{authToken.GetHashCode()}";
        var cacheEntry = new VersionCacheEntry(baseFbxHash, serverVersions, recommendedVersion, authToken);
        
        cacheData.versionCache[cacheKey] = cacheEntry;
        UltiPawLogger.Log($"[PersistentCache] Cached {serverVersions?.Count ?? 0} versions for hash: {baseFbxHash}");
        
        SaveCache();
    }

    // Cleanup Methods
    private void PeriodicCleanup()
    {
        if (DateTime.Now - cacheData.lastCleanup > CLEANUP_INTERVAL)
        {
            CleanupExpiredEntries();
            cacheData.lastCleanup = DateTime.Now;
            SaveCache();
        }
    }

    public void CleanupExpiredEntries()
    {
        int removedHashEntries = 0;
        int removedVersionEntries = 0;

        // Cleanup hash cache
        var expiredHashKeys = new List<string>();
        foreach (var kvp in cacheData.hashCache)
        {
            if (!kvp.Value.IsValid() || DateTime.Now - kvp.Value.cacheTime > HASH_CACHE_MAX_AGE)
            {
                expiredHashKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredHashKeys)
        {
            cacheData.hashCache.Remove(key);
            removedHashEntries++;
        }

        // Cleanup version cache
        var expiredVersionKeys = new List<string>();
        foreach (var kvp in cacheData.versionCache)
        {
            if (DateTime.Now - kvp.Value.cacheTime > VERSION_CACHE_MAX_AGE)
            {
                expiredVersionKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredVersionKeys)
        {
            cacheData.versionCache.Remove(key);
            removedVersionEntries++;
        }

        if (removedHashEntries > 0 || removedVersionEntries > 0)
        {
            UltiPawLogger.Log($"[PersistentCache] Cleaned up {removedHashEntries} hash entries and {removedVersionEntries} version entries.");
        }
    }

    public void ClearAllCache()
    {
        cacheData.hashCache.Clear();
        cacheData.versionCache.Clear();
        SaveCache();
        UltiPawLogger.Log("[PersistentCache] Cleared all cache data.");
    }

    public void ClearHashCache()
    {
        cacheData.hashCache.Clear();
        SaveCache();
        UltiPawLogger.Log("[PersistentCache] Cleared hash cache.");
    }

    public void ClearVersionCache()
    {
        cacheData.versionCache.Clear();
        SaveCache();
        UltiPawLogger.Log("[PersistentCache] Cleared version cache.");
    }

    // Statistics
    public (int hashEntries, int versionEntries) GetCacheStats()
    {
        return (cacheData.hashCache.Count, cacheData.versionCache.Count);
    }
}
#endif