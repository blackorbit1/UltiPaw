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
            return req.result == UnityWebRequest.Result.Success ? (true, null) : (false, $"Download failed: {req.error}");
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