#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Added for Linq

[CustomEditor(typeof(UltiPaw))]
public class UltiPawEditor : Editor
{
    private Texture2D bannerTexture;
    private SerializedProperty baseFbxFilesProp; // Use SerializedProperty for list editing

    private void OnEnable()
    {
        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(UltiPawUtils.BASE_FOLDER + "/banner.png");
        baseFbxFilesProp = serializedObject.FindProperty("baseFbxFiles"); // Get SerializedProperty
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update(); // Always start with this

        UltiPaw ultipaw = (UltiPaw)target;

        // --- Banner ---
        if (bannerTexture != null)
        {
            // ... (banner drawing code remains the same) ...
            float aspect = (float)bannerTexture.width / bannerTexture.height;
            float desiredWidth = EditorGUIUtility.currentViewWidth - 40; // Adjust padding as needed
            float desiredHeight = desiredWidth / aspect;
            Rect rect = GUILayoutUtility.GetRect(desiredWidth, desiredHeight, GUILayout.ExpandWidth(true)); // Use ExpandWidth true
            GUI.DrawTexture(rect, bannerTexture, ScaleMode.ScaleToFit);
            GUILayout.Space(5); // Add some space after banner
        }

        // --- File Configuration ---
        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Toggle for specifying base FBX
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("specifyCustomBaseFbx"), new GUIContent("Specify Base FBX Manually"));
        if (EditorGUI.EndChangeCheck())
        {
             serializedObject.ApplyModifiedProperties(); // Apply immediately if changed
             // If switching back to default, trigger assignment if needed
             if (!ultipaw.specifyCustomBaseFbx)
             {
                 CallMethodByName(ultipaw, "AssignDefaultBaseFbx");
             }
        }


        // Show Base FBX list only if specifying custom OR if default assignment failed
        if (ultipaw.specifyCustomBaseFbx || (!ultipaw.specifyCustomBaseFbx && ultipaw.baseFbxFiles.Count == 0))
        {
            EditorGUILayout.PropertyField(baseFbxFilesProp, new GUIContent("Base FBX File(s)"), true); // Use PropertyField for list
        }
        else if (!ultipaw.specifyCustomBaseFbx && ultipaw.baseFbxFiles.Count > 0 && ultipaw.baseFbxFiles[0] != null)
        {
             // Show the default path being used
             GUI.enabled = false; // Make it read-only
             EditorGUILayout.ObjectField("Using Default Base FBX", ultipaw.baseFbxFiles[0], typeof(GameObject), false);
             GUI.enabled = true;
        }

        // Display Selected UltiPaw Version Info
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Active UltiPaw Version", EditorStyles.boldLabel);
        if (ultipaw.activeUltiPawVersion != null && !string.IsNullOrEmpty(ultipaw.selectedUltiPawBinPath))
        {
            EditorGUILayout.LabelField("Version:", ultipaw.activeUltiPawVersion.version);
            EditorGUILayout.LabelField("Scope:", ultipaw.activeUltiPawVersion.scope);
            EditorGUILayout.LabelField("Path:", ultipaw.selectedUltiPawBinPath, EditorStyles.wordWrappedMiniLabel); // Show path concisely
        }
        else
        {
            EditorGUILayout.HelpBox("No UltiPaw version selected. Use 'UltiPaw > Version Manager' to select and download a version.", MessageType.Info);
        }

        EditorGUILayout.EndVertical(); // End Configuration Box
        EditorGUILayout.Space();


        // --- Action Buttons ---
        bool canTransform = ultipaw.baseFbxFiles.Count > 0 && ultipaw.baseFbxFiles[0] != null &&
                            !string.IsNullOrEmpty(ultipaw.selectedUltiPawBinPath) &&
                            File.Exists(ultipaw.selectedUltiPawBinPath) &&
                            !ultipaw.isUltiPaw;

        GUI.enabled = canTransform;
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Turn into UltiPaw", GUILayout.Height(40)))
        {
            // Confirmation recommended due to file modification
            if (EditorUtility.DisplayDialog("Confirm Transformation",
                $"This will modify:\n{AssetDatabase.GetAssetPath(ultipaw.baseFbxFiles[0])}\n\nUsing UltiPaw version: {ultipaw.activeUltiPawVersion?.version ?? "Unknown"}\nA backup (.old) will be created.",
                "Proceed", "Cancel"))
            {
                ultipaw.TurnItIntoUltiPaw();
            }
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true; // Reset GUI enabled state


        bool hasRestoreCandidates = AnyBackupExists(ultipaw.baseFbxFiles); // Use correct list name
        GUI.enabled = hasRestoreCandidates && ultipaw.isUltiPaw; // Enable only if in UltiPaw state and backups exist
        if (GUILayout.Button("Reset to Original FBX"))
        {
             if (EditorUtility.DisplayDialog("Confirm Reset",
                "This will restore the original FBX file(s) from their '.old' backups (if they exist) and reset blendshapes.",
                "Reset", "Cancel"))
            {
                ultipaw.ResetIntoWinterPaw();
            }
        }
        GUI.enabled = true; // Reset GUI enabled state

        // --- Blendshapes ---
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

        // --- Help Box ---
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Use 'UltiPaw > Version Manager' to fetch, download, and select UltiPaw versions compatible with your base FBX.\n" +
            "The 'Turn into UltiPaw' button modifies your base FBX file directly (a backup '.old' file is created).\n" +
            "The 'Reset' button restores from the '.old' backup.",
            MessageType.Info
        );

        // Apply changes to the serialized object
        if (GUI.changed || serializedObject.ApplyModifiedProperties())
        {
            // No explicit SetDirty needed when using SerializedObject/PropertyField
            // EditorUtility.SetDirty(ultipaw);
        }
    }

    // --- Helpers --- (Keep these as they are, but ensure list name is correct)
    private bool ValidateFileList<T>(List<T> list) where T : Object // Keep generic version if needed elsewhere
    {
        // ... (implementation remains the same) ...
         if (list == null || list.Count == 0) return false;
         return list.All(obj => obj != null && File.Exists(AssetDatabase.GetAssetPath(obj)));
    }

     private bool AnyBackupExists(List<GameObject> files)
     {
         if (files == null) return false;
         return files.Any(file => file != null && File.Exists(AssetDatabase.GetAssetPath(file) + ".old"));
     }


    // Reflection helper (keep as is)
    private void CallMethodByName(Object target, string methodName)
    {
        // ... (implementation remains the same) ...
        var method = target.GetType().GetMethod(methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public); // Add Public flag just in case
        if (method != null) method.Invoke(target, null);
        else Debug.LogError($"[UltiPawEditor] Could not find method '{methodName}' on target '{target.GetType().Name}'");
    }
}
#endif