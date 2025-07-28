#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VersionListDrawer
{
    private readonly UltiPawEditor editor;
    private readonly VersionActions actions;

    public VersionListDrawer(UltiPawEditor editor, VersionActions actions)
    {
        this.editor = editor;
        this.actions = actions;
    }

    public void Draw()
    {
        var allVersions = editor.GetAllVersions();
        
        if (allVersions.Any() || editor.isFetching)
        {
            if (!editor.isFetching)
            {
                for (var i = 0; i < allVersions.Count; i++)
                {
                    DrawVersionListItem(allVersions[i], i == 0, i == allVersions.Count - 1);
                }
            }
            else
            {
                EditorGUILayout.LabelField("Fetching version list...", EditorStyles.centeredGreyMiniLabel);
            }
        }
        else if (!editor.isFetching)
        {
            if (string.IsNullOrEmpty(editor.currentBaseFbxHash))
                EditorGUILayout.HelpBox("Assign or detect a Base FBX first.", MessageType.Info);
            else if (editor.fetchAttempted)
                EditorGUILayout.HelpBox("No compatible versions found for the detected Base FBX hash.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Click 'Check for Updates' to find compatible versions.", MessageType.Info);
        }
    }

    private void DrawVersionListItem(UltiPawVersion ver, bool isFirst, bool isLast)
    {
        string binPath = UltiPawUtils.GetVersionBinPath(ver.version, ver.defaultAviVersion);
        bool isDownloaded = !string.IsNullOrEmpty(binPath) && File.Exists(binPath);
        bool isSelected = ver.Equals(editor.selectedVersionForAction);
        bool isApplied = ver.Equals(editor.ultiPawTarget.appliedUltiPawVersion);

        EditorGUILayout.BeginHorizontal();
        
        Rect timelineRect = GUILayoutUtility.GetRect(20f, 20f, GUILayout.ExpandHeight(true), GUILayout.Width(20f));
        if (Event.current.type == EventType.Repaint)
        {
            Handles.color = Color.green;
            float centerX = timelineRect.center.x;
            
            // Dot
            float dotRadius = 4f;
            Handles.DrawSolidDisc(new Vector3(centerX, timelineRect.center.y, 0), Vector3.forward, dotRadius);

            // Line
            float lineWidth = 2f;
            if (!isFirst)
            {
                Handles.DrawAAPolyLine(lineWidth, new Vector3(centerX, timelineRect.yMin, 0), new Vector3(centerX, timelineRect.center.y - dotRadius, 0));
            }
            if (!isLast)
            {
                Handles.DrawAAPolyLine(lineWidth, new Vector3(centerX, timelineRect.center.y + dotRadius, 0), new Vector3(centerX, timelineRect.yMax, 0));
            }
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        
        GUILayout.Label($"UltiPaw {ver.version}", GUILayout.Width(100));
        GUILayout.FlexibleSpace();
        
        if (ver.isUnsubmitted)
        {
            DrawScopeLabel("Unsubmitted", Color.gray);
            GUILayout.Space(5);
        }
        if (isApplied)
        {
            DrawScopeLabel("Installed", new Color(0.33f, 0.79f, 0f));
            GUILayout.Space(5);
        }
        DrawScopeLabel(ver.scope.ToString(), GetColorForScope(ver.scope));
        GUILayout.Space(10);
        
        using (new EditorGUI.DisabledScope(editor.isDownloading || editor.isDeleting))
        {
            if (isDownloaded)
            {
                if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash"), GUILayout.Width(22), GUILayout.Height(22)))
                {
                    if (EditorUtility.DisplayDialog("Confirm Delete", $"Delete local files for version {ver.version}?", "Delete", "Cancel"))
                        actions.StartVersionDelete(ver);
                }
            }
            else
            {
                if (GUILayout.Button(EditorGUIUtility.IconContent("Download-Available"), GUILayout.Width(22), GUILayout.Height(22)))
                    actions.StartVersionDownload(ver, false);
            }
        }
        
        if (EditorGUILayout.Toggle(isSelected, EditorStyles.radioButton, GUILayout.Width(18)))
        {
            if (!isSelected) editor.selectedVersionForAction = ver;
        }
        else
        {
            if (isSelected) editor.selectedVersionForAction = null;
        }

        EditorGUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(ver.changelog))
        {
            EditorGUILayout.HelpBox(ver.changelog, MessageType.None);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawScopeLabel(string text, Color textColor)
    {
        EditorUIUtils.DrawChipLabel(text.ToLower(), new Color(0.28f, 0.28f, 0.28f), textColor, new Color(0.46f, 0.46f, 0.46f));
    }

    private Color GetColorForScope(Scope scope) => scope switch
    {
        Scope.PUBLIC => Color.green,
        Scope.BETA => Color.yellow,
        Scope.ALPHA => Color.red,
        _ => Color.magenta
    };
    
    public void DrawUpdateNotification()
    {
        var recommended = editor.recommendedVersion;
        var applied = editor.ultiPawTarget.appliedUltiPawVersion;

        if (recommended != null && applied != null && editor.CompareVersions(recommended.version, applied.version) > 0)
        {
            GUI.contentColor = Color.green;
            EditorGUILayout.LabelField($"New version released: v{recommended.version}!", EditorStyles.boldLabel);
            GUI.contentColor = Color.white;
            if (!string.IsNullOrEmpty(recommended.changelog))
            {
                EditorGUILayout.HelpBox(recommended.changelog, MessageType.None);
            }
            EditorGUILayout.Space();
        }
    }
}
#endif