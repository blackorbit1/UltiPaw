#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class AsyncVersionService
{
    private static AsyncVersionService _instance;
    public static AsyncVersionService Instance
    {
        get
        {
            if (_instance == null)
                _instance = new AsyncVersionService();
            return _instance;
        }
    }

    private readonly NetworkService networkService;
    private readonly AsyncHashService hashService;
    private readonly PersistentCache cache;
    private readonly AsyncTaskManager taskManager;

    // Events for UI updates
    public event Action<List<UltiPawVersion>, UltiPawVersion> OnVersionsUpdated;
    public event Action<string> OnVersionFetchError; 

    private AsyncVersionService()
    {
        networkService = new NetworkService();
        hashService = AsyncHashService.Instance;
        cache = PersistentCache.Instance;
        taskManager = AsyncTaskManager.Instance;
    }

    public async Task<(List<UltiPawVersion> versions, UltiPawVersion recommended, string error)> FetchVersionsAsync(
        string fbxPath, string authToken, bool useCache = true)
    {
        var taskId = $"fetch_versions_{Guid.NewGuid().ToString().Substring(0, 8)}";
        var fileName = System.IO.Path.GetFileName(fbxPath);
        
        taskManager.StartTask(taskId, $"Fetching versions for {fileName}");

        try
        {
            // Step 1: Get base FBX hash (wait for hash calculation if needed)
            taskManager.UpdateTaskProgress(taskId, 0.1f, "Calculating FBX hash...");
            
            string baseFbxHash = await GetBaseFbxHashAsync(fbxPath);
            if (string.IsNullOrEmpty(baseFbxHash))
            {
                var error = "Could not calculate FBX hash";
                taskManager.CompleteTask(taskId, true, error);
                OnVersionFetchError?.Invoke(error);
                return (new List<UltiPawVersion>(), null, error);
            }

            // Step 2: Check cache if requested
            if (useCache)
            {
                taskManager.UpdateTaskProgress(taskId, 0.3f, "Checking version cache...");
                
                var cachedEntry = cache.GetCachedVersions(baseFbxHash, authToken);
                if (cachedEntry != null)
                {
                    Debug.Log($"[AsyncVersionService] Using cached versions for hash: {baseFbxHash}");
                    taskManager.CompleteTask(taskId);
                    
                    // Fire event on main thread
                    taskManager.ExecuteOnMainThread(() => 
                        OnVersionsUpdated?.Invoke(cachedEntry.serverVersions, cachedEntry.recommendedVersion));
                    
                    return (cachedEntry.serverVersions, cachedEntry.recommendedVersion, null);
                }
            }

            // Step 3: Fetch from server
            taskManager.UpdateTaskProgress(taskId, 0.5f, "Fetching from server...");
            
            string url = $"{UltiPawUtils.getServerUrl()}{UltiPawUtils.VERSION_ENDPOINT}?d={baseFbxHash}&t={authToken}";
            var fetchTask = networkService.FetchVersionsAsync(url);

            // Wait for network request with progress updates
            var random = new System.Random();
            while (!fetchTask.IsCompleted)
            {
                await Task.Delay(100); // Check every 100ms
                taskManager.UpdateTaskProgress(taskId, 0.5f + (0.4f * (float)random.NextDouble()), "Waiting for server response...");
            }

            var (success, response, fetchError) = fetchTask.Result;
            
            if (success && response != null)
            {
                taskManager.UpdateTaskProgress(taskId, 0.9f, "Processing server response...");
                
                var versions = response.versions ?? new List<UltiPawVersion>();
                var recommendedVersion = versions.FirstOrDefault(v => v.version == response.recommendedVersion);

                // Cache the results
                cache.CacheVersions(baseFbxHash, versions, recommendedVersion, authToken);

                taskManager.CompleteTask(taskId);
                
                // Fire event on main thread
                taskManager.ExecuteOnMainThread(() => 
                    OnVersionsUpdated?.Invoke(versions, recommendedVersion));

                return (versions, recommendedVersion, null);
            }
            else
            {
                var errorMsg = fetchError ?? "Unknown server error";
                taskManager.CompleteTask(taskId, true, errorMsg);
                taskManager.ExecuteOnMainThread(() => OnVersionFetchError?.Invoke(errorMsg));
                return (new List<UltiPawVersion>(), null, errorMsg);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Version fetch failed: {ex.Message}";
            Debug.LogError($"[AsyncVersionService] {errorMsg}");
            taskManager.CompleteTask(taskId, true, errorMsg);
            taskManager.ExecuteOnMainThread(() => OnVersionFetchError?.Invoke(errorMsg));
            return (new List<UltiPawVersion>(), null, errorMsg);
        }
    }

    private async Task<string> GetBaseFbxHashAsync(string fbxPath)
    {
        // Check if we have a backup file (original)
        string originalPath = fbxPath + FileManagerService.OriginalSuffix;
        bool hasBackup = System.IO.File.Exists(originalPath);

        if (hasBackup)
        {
            // Use original file hash for server compatibility
            return await hashService.CalculateFileHashAsync(originalPath);
        }
        else
        {
            // Use current file hash
            return await hashService.CalculateFileHashAsync(fbxPath);
        }
    }

    public void StartVersionFetchInBackground(string fbxPath, string authToken, bool useCache = true)
    {
        _ = Task.Run(async () =>
        {
            await FetchVersionsAsync(fbxPath, authToken, useCache);
        });
    }

    public bool AreVersionsCached(string fbxPath, string authToken)
    {
        // We need the hash to check cache, but we can check if the hash is cached
        string cachedHash = hashService.GetHashIfCached(fbxPath);
        if (string.IsNullOrEmpty(cachedHash))
        {
            // Check for backup file hash
            string originalPath = fbxPath + FileManagerService.OriginalSuffix;
            if (System.IO.File.Exists(originalPath))
            {
                cachedHash = hashService.GetHashIfCached(originalPath);
            }
        }

        if (string.IsNullOrEmpty(cachedHash))
            return false;

        var cachedVersions = cache.GetCachedVersions(cachedHash, authToken);
        return cachedVersions != null;
    }

    public (List<UltiPawVersion> versions, UltiPawVersion recommended) GetCachedVersions(string fbxPath, string authToken)
    {
        // Try to get cached hash first
        string cachedHash = hashService.GetHashIfCached(fbxPath);
        if (string.IsNullOrEmpty(cachedHash))
        {
            // Check for backup file hash
            string originalPath = fbxPath + FileManagerService.OriginalSuffix;
            if (System.IO.File.Exists(originalPath))
            {
                cachedHash = hashService.GetHashIfCached(originalPath);
            }
        }

        if (string.IsNullOrEmpty(cachedHash))
            return (new List<UltiPawVersion>(), null);

        var cachedVersions = cache.GetCachedVersions(cachedHash, authToken);
        if (cachedVersions != null)
        {
            return (cachedVersions.serverVersions, cachedVersions.recommendedVersion);
        }

        return (new List<UltiPawVersion>(), null);
    }

    public async Task<(bool success, string error)> DownloadVersionAsync(UltiPawVersion version, string baseFbxHash, string authToken)
    {
        var taskId = $"download_{version.version}_{Guid.NewGuid().ToString().Substring(0, 8)}";
        
        taskManager.StartTask(taskId, $"Downloading version {version.version}");

        try
        {
            string tempZipPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ultipaw_dl_{Guid.NewGuid()}.zip");
            string url = $"{UltiPawUtils.getServerUrl()}{UltiPawUtils.MODEL_ENDPOINT}?version={version.version}&d={baseFbxHash}&t={authToken}";

            taskManager.UpdateTaskProgress(taskId, 0.1f, "Starting download...");

            var downloadTask = networkService.DownloadFileAsync(url, tempZipPath);
            
            // Monitor download progress
            var random = new System.Random();
            while (!downloadTask.IsCompleted)
            {
                await Task.Delay(500);
                // NetworkService should provide progress updates through its own mechanism
                taskManager.UpdateTaskProgress(taskId, 0.1f + (0.7f * (float)random.NextDouble()), $"Downloading {version.version}...");
            }

            var (success, error) = downloadTask.Result;
            
            if (success)
            {
                taskManager.UpdateTaskProgress(taskId, 0.8f, "Extracting files...");
                
                // Extract and move files (this should also be made async in the future)
                string dataPath = UltiPawUtils.GetVersionDataPath(version.version, version.defaultAviVersion);
                if (!string.IsNullOrEmpty(dataPath))
                {
                    UltiPawUtils.EnsureDirectoryExists(dataPath, false);
                    
                    // Extract ZIP (this is still synchronous for now, but wrapped in task)
                    await Task.Run(() =>
                    {
                        System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, dataPath);
                        System.IO.File.Delete(tempZipPath);
                    });
                }

                taskManager.CompleteTask(taskId);
                return (true, null);
            }
            else
            {
                taskManager.CompleteTask(taskId, true, error);
                return (false, error);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Download failed: {ex.Message}";
            Debug.LogError($"[AsyncVersionService] {errorMsg}");
            taskManager.CompleteTask(taskId, true, errorMsg);
            return (false, errorMsg);
        }
    }

    public void ClearVersionCache()
    {
        cache.ClearVersionCache();
        Debug.Log("[AsyncVersionService] Version cache cleared.");
    }

    public void ClearAllCache()
    {
        cache.ClearAllCache();
        Debug.Log("[AsyncVersionService] All cache cleared.");
    }

    // Get statistics about cache usage
    public (int hashEntries, int versionEntries) GetCacheStats()
    {
        return cache.GetCacheStats();
    }

    // Force refresh versions (bypass cache)
    public async Task<(List<UltiPawVersion> versions, UltiPawVersion recommended, string error)> RefreshVersionsAsync(
        string fbxPath, string authToken)
    {
        return await FetchVersionsAsync(fbxPath, authToken, useCache: false);
    }
}
#endif