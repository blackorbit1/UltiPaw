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

        // ░▒▓ BANNER ▓▒░
        if (bannerTexture != null)
        {
            float aspect     = (float)bannerTexture.width / bannerTexture.height;
            float desiredWidth  = EditorGUIUtility.currentViewWidth - 40;
            float desiredHeight = desiredWidth / aspect;
            Rect rect = GUILayoutUtility.GetRect(desiredWidth, desiredHeight, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, bannerTexture, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.Space();

        // -----------------------
        // 1) File location toggle
        // -----------------------
        ultipaw.specifyCustomFiles = EditorGUILayout.Toggle("Specify file locations", ultipaw.specifyCustomFiles);

        bool hasValidFilesA = ValidateFileList(ultipaw.filesA);
        bool hasValidFilesC = ValidateFileList(ultipaw.filesC);
        bool allFilesPresent = hasValidFilesA && hasValidFilesC;

        // Show the custom file fields only if user toggles them
        if (ultipaw.specifyCustomFiles)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ---- FILES A (Key) ----
            EditorGUILayout.LabelField("Files A (Key)", EditorStyles.boldLabel);
            for (int i = 0; i < ultipaw.filesA.Count; i++)
            {
                var newA = (GameObject)EditorGUILayout.ObjectField(
                    $"File A [{i}]",
                    ultipaw.filesA[i],
                    typeof(GameObject),
                    false
                );

                // Log path when changed
                if (newA != ultipaw.filesA[i])
                {
                    string path = AssetDatabase.GetAssetPath(newA);
                    Debug.Log($"[UltiPaw] Selected File A [{i}] Path: {path}");
                }
                ultipaw.filesA[i] = newA;
            }

            EditorGUILayout.Space();

            // ---- FILES C (Encrypted) ----
            EditorGUILayout.LabelField("Files C (Encrypted)", EditorStyles.boldLabel);
            for (int i = 0; i < ultipaw.filesC.Count; i++)
            {
                var newC = (DefaultAsset)EditorGUILayout.ObjectField(
                    $"File C [{i}]",
                    ultipaw.filesC[i],
                    typeof(DefaultAsset),
                    false
                );

                if (newC != ultipaw.filesC[i])
                {
                    string path = AssetDatabase.GetAssetPath(newC);
                    Debug.Log($"[UltiPaw] Selected File C [{i}] Path: {path}");
                }
                ultipaw.filesC[i] = newC;
            }

            EditorGUILayout.Space();

            // Reset Defaults Button
            if (GUILayout.Button("Reset to Default Locations"))
            {
                Undo.RecordObject(ultipaw, "Reset UltiPaw Defaults");
                CallMethodByName(ultipaw, "AssignDefaultFiles");
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        // ------------------------------------------
        // 2) "turn it into a UltiPaw" button
        //    - Disabled if files are missing OR
        //      if already isUltiPaw
        // ------------------------------------------
        GUI.enabled = allFilesPresent && !ultipaw.isUltiPaw;
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("turn it into a UltiPaw", GUILayout.Height(40)))
        {
            ultipaw.TurnItIntoUltiPaw();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        // ------------------------------------------
        // 3) "reset into WinterPaw" button
        //    - Enabled only if a .old backup exists
        // ------------------------------------------
        bool hasRestoreCandidates = AnyBackupExists(ultipaw.filesA);
        GUI.enabled = hasRestoreCandidates;
        if (GUILayout.Button("reset into WinterPaw"))
        {
            ultipaw.ResetIntoWinterPaw();
        }
        GUI.enabled = true;

        // ------------------------------------------
        // 4) Show the blendshape 3×N grid if isUltiPaw
        // ------------------------------------------
        if (ultipaw.isUltiPaw)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Blendshapes (Click to Toggle)", EditorStyles.boldLabel);

            const int columns = 3;
            for (int i = 0; i < ultipaw.blendShapeNames.Count; i++)
            {
                if (i % columns == 0) EditorGUILayout.BeginHorizontal();

                bool currentState = ultipaw.blendShapeStates[i];
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = currentState ? Color.green : Color.gray;

                if (GUILayout.Button(ultipaw.blendShapeNames[i], GUILayout.Width(120)))
                {
                    bool newState = !currentState;
                    ultipaw.blendShapeStates[i] = newState;
                    ultipaw.ToggleBlendShape(ultipaw.blendShapeNames[i], newState);
                }

                GUI.backgroundColor = oldColor;

                if (i % columns == columns - 1) EditorGUILayout.EndHorizontal();
            }

            // Close last row if incomplete
            if (ultipaw.blendShapeNames.Count % columns != 0)
                EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ------------------------------------------
        // 5) Show "Help" section at the bottom
        // ------------------------------------------
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "If the 'reset into WinterPaw' button is disabled while your project is still not back to its default WinterPaw state, please, reimport the winterpaw avatar package",
            MessageType.Info
        );

        if (GUI.changed)
        {
            EditorUtility.SetDirty(ultipaw);
        }
    }

    // ------------------------------------------
    // Helpers
    // ------------------------------------------
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
            if (!file) continue;
            string path = AssetDatabase.GetAssetPath(file);
            if (File.Exists(path + ".old")) return true;
        }
        return false;
    }

    private void CallMethodByName(Object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method != null) method.Invoke(target, null);
    }
}
#endif
