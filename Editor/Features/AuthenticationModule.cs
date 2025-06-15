#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Handles the UI and logic for user authentication.
public class AuthenticationModule
{
    private readonly UltiPawEditor editor;

    public AuthenticationModule(UltiPawEditor editor)
    {
        this.editor = editor;
    }

    public void DrawMagicSyncAuth()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Magic Sync", GUILayout.Width(120f), GUILayout.Height(30f)))
        {
            // TODO: Move to AuthenticationService
            UltiPawUtils.RegisterAuth().ContinueWith(task =>
            {
                // Queue the result to be processed on the main thread
                EditorApplication.delayCall += () =>
                {
                    if (task.Result)
                    {
                        editor.CheckAuthentication(); // Update state in the main editor
                        editor.Repaint();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Authentication Failed", "Please visit the Orbiters website and click 'Magic Sync' first.", "OK");
                    }
                };
            });
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox("Use Magic Sync to authenticate this tool. Go to the Orbiters website, click 'Magic Sync' to copy your token, then click the button above.", MessageType.Info);
        EditorGUILayout.Space(5);
    }

    public void DrawLogoutButton()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
        if (GUILayout.Button("Logout", GUILayout.Width(100f), GUILayout.Height(25f)))
        {
            if (EditorUtility.DisplayDialog("Confirm Logout", "Are you sure you want to log out?", "Logout", "Cancel"))
            {
                // TODO: Move to AuthenticationService
                if (UltiPawUtils.RemoveAuth())
                {
                    editor.isAuthenticated = false;
                    editor.authToken = null;
                    editor.Repaint();
                }
            }
        }
        GUI.backgroundColor = Color.white;

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(10);
    }
}
#endif