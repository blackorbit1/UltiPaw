#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

[CustomEditor(typeof(UltiPaw))]
public class UltiPawEditor : Editor
{
    private Texture2D bannerTexture;

    private void OnEnable()
    {
        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UltiPaw/banner.png");
    }

    public override void OnInspectorGUI()
    {
        UltiPaw ultipaw = (UltiPaw)target;

        // ░▒▓ CENTERED BANNER ▓▒░
        if (bannerTexture != null)
        {
            float width = EditorGUIUtility.currentViewWidth - 40;
            Rect rect = GUILayoutUtility.GetRect(width, 220, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, bannerTexture, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.Space();

        // 🔘 Toggle
        ultipaw.specifyCustomFiles = EditorGUILayout.Toggle("Specify file locations", ultipaw.specifyCustomFiles);

        bool hasValidFilesA = ValidateFileList(ultipaw.filesA);
        bool hasValidFilesC = ValidateFileList(ultipaw.filesC);
        bool allFilesPresent = hasValidFilesA && hasValidFilesC;

        if (ultipaw.specifyCustomFiles)
        {
            EditorGUILayout.Space();

            // ░▒▓ GRAY BOX CONTAINER ▓▒░
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 📂 FILES A
            EditorGUILayout.LabelField("Files A (Key)", EditorStyles.boldLabel);
            for (int i = 0; i < ultipaw.filesA.Count; i++)
            {
                var newA = (GameObject)EditorGUILayout.ObjectField($"File A [{i}]", ultipaw.filesA[i], typeof(GameObject), false);
                if (newA != ultipaw.filesA[i])
                {
                    string path = AssetDatabase.GetAssetPath(newA);
                    Debug.Log($"[UltiPaw] Selected File A [{i}] Path: {path}");
                }
                ultipaw.filesA[i] = newA;
            }

            EditorGUILayout.Space();

            // 📂 FILES C
            EditorGUILayout.LabelField("Files C (Encrypted)", EditorStyles.boldLabel);
            for (int i = 0; i < ultipaw.filesC.Count; i++)
            {
                var newC = (DefaultAsset)EditorGUILayout.ObjectField($"File C [{i}]", ultipaw.filesC[i], typeof(DefaultAsset), false);
                if (newC != ultipaw.filesC[i])
                {
                    string path = AssetDatabase.GetAssetPath(newC);
                    Debug.Log($"[UltiPaw] Selected File C [{i}] Path: {path}");
                }
                ultipaw.filesC[i] = newC;
            }

            EditorGUILayout.Space();

            // 🔄 Reset Defaults
            if (GUILayout.Button("Reset to Default Locations"))
            {
                Undo.RecordObject(ultipaw, "Reset UltiPaw Defaults");
                CallMethodByName(ultipaw, "AssignDefaultFiles");
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        // ✅ BIG GREEN BUTTON (enabled only if valid)
        GUI.enabled = allFilesPresent;
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("turn it into a UltiPaw", GUILayout.Height(40)))
        {
            ultipaw.TurnItIntoUltiPaw();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        // 🔁 RESET BUTTON (enabled only if backups exist)
        bool hasRestoreCandidates = AnyBackupExists(ultipaw.filesA);
        GUI.enabled = hasRestoreCandidates;
        if (GUILayout.Button("reset into WinterPaw"))
        {
            ultipaw.ResetIntoWinterPaw();
        }
        GUI.enabled = true;

        if (GUI.changed)
        {
            EditorUtility.SetDirty(ultipaw);
        }
    }

    private bool ValidateFileList<T>(List<T> list) where T : Object
    {
        if (list == null || list.Count == 0) return false;

        foreach (var obj in list)
        {
            if (obj == null) return false;
            string path = AssetDatabase.GetAssetPath(obj);
            if (!File.Exists(path)) return false;
        }
        return true;
    }

    private bool AnyBackupExists(List<GameObject> files)
    {
        foreach (var file in files)
        {
            if (file == null) continue;
            string path = AssetDatabase.GetAssetPath(file);
            // For FBX, backup is stored as .bin.old.
            if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                string tempPath = Path.ChangeExtension(path, ".bin");
                if (File.Exists(tempPath + ".old")) return true;
            }
            else if (File.Exists(path + ".old"))
                return true;
        }
        return false;
    }

    private void CallMethodByName(Object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method != null) method.Invoke(target, null);
    }
}
#endif
