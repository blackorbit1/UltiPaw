#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class AdvancedModeModule
{
    private readonly UltiPawEditor editor;
    private bool isAdvancedMode = false;
    private bool advancedModeFoldout = true; // Opened by default when advanced mode is on
    
    public AdvancedModeModule(UltiPawEditor editor)
    {
        this.editor = editor;
    }

    public void Draw()
    {
        EditorGUILayout.Space();
        
        // Advanced Mode checkbox
        isAdvancedMode = EditorGUILayout.Toggle("Advanced Mode", isAdvancedMode);
        
        if (isAdvancedMode)
        {
            EditorGUILayout.Space();
            
            // Advanced Mode foldout
            advancedModeFoldout = EditorGUILayout.Foldout(advancedModeFoldout, "Advanced Settings", true, EditorStyles.foldoutHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            if (advancedModeFoldout)
            {
                EditorGUI.indentLevel++;
                
                // Dev Environment checkbox
                bool currentDevEnvironment = UltiPawUtils.isDevEnvironment;
                bool newDevEnvironment = EditorGUILayout.Toggle("Dev Environment", currentDevEnvironment);
                
                if (newDevEnvironment != currentDevEnvironment)
                {
                    UltiPawUtils.isDevEnvironment = newDevEnvironment;
                }
                
                

                if (editor.isAuthenticated)
                {
                    EditorGUILayout.Space();
                    // File Configuration section
                    if (editor.versionModule?.fileConfigDrawer != null)
                    {
                        editor.versionModule.fileConfigDrawer.Draw();
                    }
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
        }
    }
}
#endif