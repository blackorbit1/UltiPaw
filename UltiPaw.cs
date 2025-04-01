using UnityEditor;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using IEditorOnly = VRC.SDKBase.IEditorOnly;

#if UNITY_EDITOR
public class UltiPaw : MonoBehaviour, IEditorOnly
{
    public bool specifyCustomFiles = false;

    [HideInInspector]
    public List<GameObject> filesA = new List<GameObject>();

    [HideInInspector]
    public List<DefaultAsset> filesC = new List<DefaultAsset>();

    private string defaultWinterpawLocation = "Assets/MasculineCanine/FX/MasculineCanine.v1.5.fbx";
    private string defaultUltiPawLocation   = "Assets/UltiPaw/ultipaw.bin";

    // -------------------------------------------
    // 1) Track whether we’re in "UltiPaw" state
    // -------------------------------------------
    [HideInInspector] public bool isUltiPaw = false;

    // -------------------------------------------
    // 2) Blendshape Toggling Support
    //    - Put your blendshape names here
    // -------------------------------------------
    [HideInInspector] public List<string> blendShapeNames = new List<string>
    {
        "orbit muscles",
        "orbit face",
        "jawline",
        "goatee",
        "heavy cheek fluff"
    };

    // Keep track of which blendshapes are toggled on (100%) or off (0%)
    [HideInInspector] public List<bool> blendShapeStates = new List<bool>();

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Ensure blendShapeStates matches the count of blendShapeNames
        while (blendShapeStates.Count < blendShapeNames.Count)
            blendShapeStates.Add(false);
        while (blendShapeStates.Count > blendShapeNames.Count)
            blendShapeStates.RemoveAt(blendShapeStates.Count - 1);

        // Auto-assign defaults only if lists are empty and user hasn’t toggled custom files
        if (!specifyCustomFiles)
        {
            AssignDefaultFiles();
        }
        ValidateFiles();
    }

    private void AssignDefaultFiles()
    {
        filesA.Clear();
        filesC.Clear();

        // Note: using LoadAssetAtPath here is okay; our TurnItIntoUltiPaw will handle FBX processing.
        var defaultA = AssetDatabase.LoadAssetAtPath<GameObject>(defaultWinterpawLocation);
        var defaultC = AssetDatabase.LoadAssetAtPath<DefaultAsset>(defaultUltiPawLocation);

        filesA.Add(defaultA);
        filesC.Add(defaultC);
    }

    private void ValidateFiles()
    {
        if (filesA.Count == 0 || filesA[0] == null)
            Debug.LogWarning("UltiPaw: Default File A not found at " + defaultWinterpawLocation);

        if (filesC.Count == 0 || filesC[0] == null)
            Debug.LogWarning("UltiPaw: Default File C not found at " + defaultUltiPawLocation);
    }
#endif

    // -----------------------------------------------------------
    // 3) Called by the big green button in the editor script
    // -----------------------------------------------------------
    public void TurnItIntoUltiPaw()
    {
#if UNITY_EDITOR
        for (int i = 0; i < filesA.Count; i++)
        {
            if (i >= filesC.Count || filesA[i] == null || filesC[i] == null) continue;

            string pathA = AssetDatabase.GetAssetPath(filesA[i]);
            string pathC = AssetDatabase.GetAssetPath(filesC[i]);

            byte[] dataA = File.ReadAllBytes(pathA);
            byte[] dataC = File.ReadAllBytes(pathC);

            byte[] dataB = new byte[dataC.Length];
            for (int j = 0; j < dataC.Length; j++)
                dataB[j] = (byte)(dataC[j] ^ dataA[j % dataA.Length]);

            // Backup original
            string backupPath = pathA + ".old";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(pathA, backupPath);

            // Write new file
            File.WriteAllBytes(pathA, dataB);

            Debug.Log($"Transformed {pathA}");
        }

        // Mark state as "UltiPaw" now
        isUltiPaw = true;

        AssetDatabase.Refresh();
#endif
    }

    // -----------------------------------------------------------
    // 4) Called by "reset into WinterPaw" in the editor script
    // -----------------------------------------------------------
    public void ResetIntoWinterPaw()
    {
#if UNITY_EDITOR
        foreach (var fileA in filesA)
        {
            if (fileA == null) continue;

            string path = AssetDatabase.GetAssetPath(fileA);
            string backupPath = path + ".old";

            if (File.Exists(backupPath))
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(backupPath, path);
                Debug.Log($"Restored {path}");
            }
        }

        // Mark state as not UltiPaw
        isUltiPaw = false;

        // Reset all blendshapes to 0
        for (int i = 0; i < blendShapeStates.Count; i++)
        {
            blendShapeStates[i] = false;
            ToggleBlendShape(blendShapeNames[i], false);
        }

        AssetDatabase.Refresh();
#endif
    }

    // -----------------------------------------------------------
    // 5) Toggle a given blendshape on the "Body" GameObject
    // -----------------------------------------------------------
    public void ToggleBlendShape(string shapeName, bool isOn)
    {
        // Try to find "Body" in the scene
        GameObject body = GameObject.Find("Body");
        if (!body)
        {
            Debug.LogWarning("UltiPaw: Could not find GameObject named 'Body' in the scene.");
            return;
        }

        var skinnedRenderer = body.GetComponent<SkinnedMeshRenderer>();
        if (!skinnedRenderer || !skinnedRenderer.sharedMesh)
        {
            Debug.LogWarning("UltiPaw: 'Body' has no SkinnedMeshRenderer or no sharedMesh.");
            return;
        }

        int shapeIndex = skinnedRenderer.sharedMesh.GetBlendShapeIndex(shapeName);
        if (shapeIndex < 0)
        {
            Debug.LogWarning($"UltiPaw: Blend shape '{shapeName}' not found on Body's mesh.");
            return;
        }

        skinnedRenderer.SetBlendShapeWeight(shapeIndex, isOn ? 100f : 0f);
    }
}
#endif
