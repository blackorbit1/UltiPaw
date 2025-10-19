#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public class NetworkService
{
    public async Task<(bool success, UltiPawVersionResponse response, string error)> FetchVersionsAsync(string url)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            await req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                switch (req.responseCode)
                {
                    case 401:
                        return (false, null, "Unauthorized: If you haven't done anything that could lead to your account being limited, contact blackorbit.");
                    case 404:
                        return (false, null, "Not Found: The requested resource could not be found.");
                    case 500:
                        return (false, null, "Server Error: An error occurred on the server.");
                }
                return (false, null, $"Request failed: {req.error}");
            } 
            try
            {
                var response = JsonConvert.DeserializeObject<UltiPawVersionResponse>(req.downloadHandler.text);
                return (true, response, null);
            }
            catch (Exception e) { return (false, null, $"Failed to parse server response: {e.Message}"); }
        }
    }

    public async Task<(bool success, string error)> DownloadFileAsync(string url, string destinationPath)
    {
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
        {
            req.downloadHandler = new DownloadHandlerFile(destinationPath);
            await req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                var error = $"Download failed: {req.error}";
                if (req.responseCode == 404) error += " - 404 Not Found";
                UltiPawLogger.LogError($"[NetworkService] {error}, url = {url}");
                return (false, error);
            }
            return (true, null);
        }
    }
    
    // --- NEW: Creator Mode Upload Method ---
    public async Task<(bool success, string serverResponse, string error)> SubmitNewVersionAsync(string url, string authToken, string zipFilePath, string metadataJson)
    {
        byte[] fileBytes = File.ReadAllBytes(zipFilePath);
        string zipFileName = Path.GetFileName(zipFilePath);
        
        WWWForm form = new WWWForm();
        form.AddBinaryData("packageFile", fileBytes, zipFileName, "application/zip");
        form.AddField("metadata", metadataJson);

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.SetRequestHeader("Authorization", $"Bearer {authToken}");
            req.timeout = 300; // 5 minute timeout for uploads

            await req.SendWebRequest();
            
            if (req.result != UnityWebRequest.Result.Success)
            {
                return (false, req.downloadHandler.text, $"Upload failed: [{req.responseCode}] {req.error}");
            }

            return (true, req.downloadHandler.text, null);
        }
    }

    public class CheckConnectionResponse { public string state; }

    public async Task<string> CheckConnectionAsync(string url, string authToken = null)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            if (!string.IsNullOrEmpty(authToken))
            {
                req.SetRequestHeader("Authorization", $"Bearer {authToken}");
            }

            try
            {
                await req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    long code = req.responseCode;
                    string body = null;
                    try { body = req.downloadHandler?.text; } catch { /* ignore */ }
                    UltiPawLogger.LogWarning($"[UltiPaw] Connection check failed: [{code}] {req.error} {(string.IsNullOrEmpty(body) ? string.Empty : "- " + body)}");
                    return "disconnected";
                }

                var text = req.downloadHandler.text;
                CheckConnectionResponse resp = null;
                try { resp = JsonConvert.DeserializeObject<CheckConnectionResponse>(text); }
                catch (Exception jex)
                {
                    UltiPawLogger.LogWarning($"[UltiPaw] Invalid connection check response JSON: {jex.Message}");
                    return "disconnected";
                }
                if (resp == null || string.IsNullOrEmpty(resp.state))
                {
                    UltiPawLogger.LogWarning("[UltiPaw] Connection check response missing 'state'.");
                    return "disconnected";
                }
                if (resp.state == "connected" || resp.state == "limited") return resp.state;
                UltiPawLogger.LogWarning($"[UltiPaw] Connection check returned unexpected state '{resp.state}'. Treating as disconnected.");
                return "disconnected";
            }
            catch (Exception ex)
            {
                UltiPawLogger.LogWarning($"[UltiPaw] Connection check error: {ex.Message}");
                return "disconnected";
            }
        }
    }

}

public static class EditorAsyncExtensions
{
    public static TaskAwaiter GetAwaiter(this AsyncOperation asyncOp)
    {
        var tcs = new TaskCompletionSource<object>();
        asyncOp.completed += obj => { tcs.SetResult(null); };
        return ((Task)tcs.Task).GetAwaiter();
    }
}
#endif