#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class UltiPawVersionManager : EditorWindow
{
    private const string serverBaseUrl = "https://orbiter.cc";
    private const string modelDownloadPath = "Assets/UltiPaw/ultipaw.bin";

    private List<UltiPawVersion> versions = new();
    private int selectedVersionIndex = 0;
    private string recommendedVersion;

    [MenuItem("UltiPaw/Version Manager")]
    public static void ShowWindow() => GetWindow<UltiPawVersionManager>("UltiPaw Versions");

    private void OnGUI()
    {
        if (versions.Count == 0)
        {
            if (GUILayout.Button("Fetch Available Versions"))
                EditorCoroutineUtility.StartCoroutineOwnerless(FetchVersions());
        }
        else
        {
            EditorGUILayout.LabelField("Recommended Version: " + recommendedVersion);
            string[] options = versions.ConvertAll(v => v.version + " (" + v.scope + ")").ToArray();
            selectedVersionIndex = EditorGUILayout.Popup("Select Version", selectedVersionIndex, options);

            UltiPawVersion selected = versions[selectedVersionIndex];

            EditorGUILayout.LabelField("Date", selected.date);
            EditorGUILayout.LabelField("Changelog", selected.changelog, EditorStyles.wordWrappedLabel);

            if (GUILayout.Button("Download This Version"))
                EditorCoroutineUtility.StartCoroutineOwnerless(DownloadVersion(selected.version));
        }
    }

    private IEnumerator FetchVersions()
    {
        UnityWebRequest req = UnityWebRequest.Get(serverBaseUrl + "/ultipaw/getVersions");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Failed to fetch versions: " + req.error);
            yield break;
        }

        UltiPawVersionResponse json = JsonUtility.FromJson<UltiPawVersionResponse>(req.downloadHandler.text);
        versions = json.versions;
        recommendedVersion = json.recommendedVersion;
        Repaint();
    }

    private IEnumerator DownloadVersion(string version)
    {
        string url = serverBaseUrl + "/ultipaw/getModel?version=" + UnityWebRequest.EscapeURL(version);
        UnityWebRequest req = UnityWebRequest.Get(url);
        req.downloadHandler = new DownloadHandlerBuffer();
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Failed to download version " + version + ": " + req.error);
            yield break;
        }

        File.WriteAllBytes(modelDownloadPath, req.downloadHandler.data);
        Debug.Log("Downloaded and saved version " + version + " to: " + modelDownloadPath);
        AssetDatabase.Refresh();
    }

    // JSON structure
    [System.Serializable]
    public class UltiPawVersionResponse
    {
        public string recommendedVersion;
        public List<UltiPawVersion> versions;
    }

    [System.Serializable]
    public class UltiPawVersion
    {
        public string version;
        public string scope;
        public string date;
        public string changelog;
        public Dictionary<string, string> dependencies;
    }
}
#endif
