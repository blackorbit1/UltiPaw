#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class AsyncHashService
{
    private static AsyncHashService _instance;
    public static AsyncHashService Instance
    {
        get
        {
            if (_instance == null)
                _instance = new AsyncHashService();
            return _instance;
        }
    }

    private const int BUFFER_SIZE = 1024 * 1024; // 1MB buffer for reading files
    private readonly object _lockObject = new object();

    private AsyncHashService()
    {
    }

    public async Task<string> CalculateFileHashAsync(string filePath, string taskId = null)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError($"[AsyncHashService] File not found: {filePath}");
            return null;
        }

        // Check cache first
        string cachedHash = PersistentCache.Instance.GetCachedHash(filePath);
        if (!string.IsNullOrEmpty(cachedHash))
        {
            Debug.Log($"[AsyncHashService] Using cached hash for: {Path.GetFileName(filePath)}");
            return cachedHash;
        }

        // Generate unique task ID if not provided
        if (string.IsNullOrEmpty(taskId))
        {
            taskId = $"hash_{Path.GetFileName(filePath)}_{Guid.NewGuid().ToString().Substring(0, 8)}";
        }

        var taskManager = AsyncTaskManager.Instance;
        var fileName = Path.GetFileName(filePath);
        
        // Start the task
        taskManager.StartTask(taskId, $"Calculating hash for {fileName}");

        try
        {
            var hash = await Task.Run(() => CalculateHashInternal(filePath, taskId, taskManager));
            
            // Cache the result
            if (!string.IsNullOrEmpty(hash))
            {
                PersistentCache.Instance.CacheHash(filePath, hash);
            }
            
            taskManager.CompleteTask(taskId);
            return hash;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AsyncHashService] Hash calculation failed for {fileName}: {ex.Message}");
            taskManager.CompleteTask(taskId, true, ex.Message);
            return null;
        }
    }

    private string CalculateHashInternal(string filePath, string taskId, AsyncTaskManager taskManager)
    {
        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;
        long processedBytes = 0;

        using (var sha256 = SHA256.Create())
        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE))
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            int bytesRead;

            while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Update progress
                processedBytes += bytesRead;
                float progress = totalBytes > 0 ? (float)processedBytes / totalBytes : 1.0f;
                
                taskManager.UpdateTaskProgress(taskId, progress);

                // Add data to hash
                if (bytesRead == buffer.Length)
                {
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }
                else
                {
                    sha256.TransformFinalBlock(buffer, 0, bytesRead);
                }

                // Yield control periodically to prevent UI blocking
                if (processedBytes % (BUFFER_SIZE * 10) == 0) // Every 10MB
                {
                    System.Threading.Thread.Sleep(1); // Brief pause to yield
                }
            }

            // Finalize hash if not already done
            if (processedBytes == totalBytes && bytesRead == BUFFER_SIZE)
            {
                sha256.TransformFinalBlock(new byte[0], 0, 0);
            }

            var hashBytes = sha256.Hash;
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }

    public async Task<(string currentHash, string originalHash)> CalculateFBXHashesAsync(string fbxPath)
    {
        if (string.IsNullOrEmpty(fbxPath) || !File.Exists(fbxPath))
        {
            return (null, null);
        }

        var taskId = $"fbx_hashes_{Guid.NewGuid().ToString().Substring(0, 8)}";
        var taskManager = AsyncTaskManager.Instance;
        var fileName = Path.GetFileName(fbxPath);
        
        taskManager.StartTask(taskId, $"Calculating hashes for {fileName}");

        try
        {
            // Calculate current FBX hash
            taskManager.UpdateTaskProgress(taskId, 0.0f, $"Hashing current {fileName}");
            var currentHashTask = CalculateFileHashAsync(fbxPath, $"{taskId}_current");
            
            // Calculate original FBX hash if backup exists
            string originalPath = fbxPath + FileManagerService.OriginalSuffix;
            Task<string> originalHashTask = null;
            
            if (File.Exists(originalPath))
            {
                taskManager.UpdateTaskProgress(taskId, 0.5f, $"Hashing original {fileName}");
                originalHashTask = CalculateFileHashAsync(originalPath, $"{taskId}_original");
            }

            // Wait for both tasks to complete
            string currentHash = await currentHashTask;
            string originalHash = originalHashTask != null ? await originalHashTask : null;

            taskManager.CompleteTask(taskId);
            return (currentHash, originalHash);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AsyncHashService] FBX hash calculation failed: {ex.Message}");
            taskManager.CompleteTask(taskId, true, ex.Message);
            return (null, null);
        }
    }

    public bool IsFileHashCached(string filePath)
    {
        return !string.IsNullOrEmpty(PersistentCache.Instance.GetCachedHash(filePath));
    }

    public void InvalidateHashCache(string filePath)
    {
        // This will be handled automatically by the PersistentCache when file timestamp changes
        Debug.Log($"[AsyncHashService] Hash cache will be invalidated for: {Path.GetFileName(filePath)} on next access");
    }

    // Convenience method for immediate hash needs (checks cache first)
    public string GetHashIfCached(string filePath)
    {
        return PersistentCache.Instance.GetCachedHash(filePath);
    }

    // Method to start hash calculation without waiting for result
    public void StartHashCalculation(string filePath, Action<string> onCompleted = null)
    {
        var taskId = $"async_hash_{Path.GetFileName(filePath)}_{Guid.NewGuid().ToString().Substring(0, 8)}";
        
        _ = Task.Run(async () =>
        {
            var hash = await CalculateFileHashAsync(filePath, taskId);
            
            if (onCompleted != null && !string.IsNullOrEmpty(hash))
            {
                AsyncTaskManager.Instance.ExecuteOnMainThread(() => onCompleted(hash));
            }
        });
    }

    // Batch hash calculation for multiple files
    public async Task<System.Collections.Generic.Dictionary<string, string>> CalculateMultipleHashesAsync(System.Collections.Generic.List<string> filePaths)
    {
        var results = new System.Collections.Generic.Dictionary<string, string>();
        var tasks = new System.Collections.Generic.List<Task<(string path, string hash)>>();

        foreach (var filePath in filePaths)
        {
            if (File.Exists(filePath))
            {
                var task = Task.Run(async () =>
                {
                    var hash = await CalculateFileHashAsync(filePath);
                    return (filePath, hash);
                });
                tasks.Add(task);
            }
        }

        var completedTasks = await Task.WhenAll(tasks);
        
        foreach (var (path, hash) in completedTasks)
        {
            if (!string.IsNullOrEmpty(hash))
            {
                results[path] = hash;
            }
        }

        return results;
    }

    public void ClearHashCache()
    {
        PersistentCache.Instance.ClearHashCache();
        Debug.Log("[AsyncHashService] Hash cache cleared.");
    }
}
#endif