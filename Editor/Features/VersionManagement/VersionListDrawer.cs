
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VersionListDrawer
{
    private readonly UltiPawEditor editor;
    private readonly VersionActions actions;
    
    // UI State
    private static bool displayAllChangelogs = false;
    private static Dictionary<string, bool> individualChangelogStates = new Dictionary<string, bool>();
    private static bool isListCollapsed = false;
    
    // Special reset version identifier
    public static readonly UltiPawVersion RESET_VERSION = new UltiPawVersion
    {
        version = "Base Default Winterpaw",
        scope = Scope.PUBLIC,
        changelog = "Reset to the original avatar configuration without any UltiPaw modifications."
    };
    
    // Cached textures for collapse/expand icons
    private static Texture2D collapseIcon;
    private static Texture2D expandIcon;

    public VersionListDrawer(UltiPawEditor editor, VersionActions actions)
    {
        this.editor = editor;
        this.actions = actions;
        LoadIcons();
    }
    
    private void LoadIcons()
    {
        if (collapseIcon == null)
            collapseIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/ultipaw/Editor/collapse.png");
        if (expandIcon == null)
            expandIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/ultipaw/Editor/expand.png");
    }

    public void Draw()
    {
        DrawHeader();
        
        var allVersions = editor.GetAllVersions();
        
        if (allVersions.Any() || editor.isFetching)
        {
            if (!editor.isFetching)
            {
                if (allVersions.Count > 0)
                {
                    // Draw first version
                    DrawVersionListItem(allVersions[0], true, false);
                    
                    // Draw collapse/expand button after first item (only if there are more than 2 items total including reset)
                    if (allVersions.Count > 1)
                    {
                        DrawCollapseExpandButton(false, false);
                    }
                    
                    // Draw middle versions (only if not collapsed and there are middle items)
                    if (!isListCollapsed && allVersions.Count > 1)
                    {
                        for (var i = 1; i < allVersions.Count; i++)
                        {
                            DrawVersionListItem(allVersions[i], false, false);
                        }
                    }
                }
                
                // Draw reset item as the last item
                DrawResetVersionItem(allVersions.Count == 0);
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

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal();
        
        EditorGUILayout.LabelField("UltiPaw Versions", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        
        EditorGUI.BeginChangeCheck();
        displayAllChangelogs = EditorGUILayout.Toggle("Display all changelogs", displayAllChangelogs);
        if (EditorGUI.EndChangeCheck())
        {
            // Clear individual states when switching modes
            individualChangelogStates.Clear();
        }
        
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(5);
    }
    
    private void DrawCollapseExpandButton(bool isFirst, bool isLast)
    {
        // Mark the start of the entire button area
        Rect buttonItemStartRect = GUILayoutUtility.GetRect(0, 0);
        
        // Start the button item layout
        EditorGUILayout.BeginVertical();
        
        // Add space above button
        GUILayout.Space(2);
        
        EditorGUILayout.BeginHorizontal();
        
        // Reserve space for timeline - this will span the entire button height
        Rect timelineRect = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f));
        
        // Right side content (the clickable area)
        EditorGUILayout.BeginVertical();
        
        // Center the content vertically and horizontally
        GUILayout.FlexibleSpace(); // Push content to center vertically
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace(); // Push content to center horizontally
        
        Texture2D iconToUse = isListCollapsed ? expandIcon : collapseIcon;
        string labelText = isListCollapsed ? "expand all versions" : "collapse all versions";
        
        // Draw icon and text without button frame, centered
        if (iconToUse != null)
        {
            // Increase icon size by ~40% (from 16x16 to 22x22)
            GUILayout.Label(iconToUse, GUILayout.Width(22), GUILayout.Height(22));
            GUILayout.Space(8); // Slightly more space between icon and text
        }
        
        GUILayout.Label(labelText, EditorStyles.miniLabel);
        
        GUILayout.FlexibleSpace(); // Push content to center horizontally
        EditorGUILayout.EndHorizontal();
        
        GUILayout.FlexibleSpace(); // Push content to center vertically
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        
        // Add space below button
        GUILayout.Space(2);
        
        EditorGUILayout.EndVertical();
        
        // Get the rect for the entire button item after all layout is complete
        Rect buttonItemEndRect = GUILayoutUtility.GetRect(0, 0);
        Rect fullButtonItemRect = new Rect(buttonItemStartRect.x, buttonItemStartRect.y, 
                                          buttonItemStartRect.width, buttonItemEndRect.y - buttonItemStartRect.y);
        
        // Handle click on the entire button area
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && fullButtonItemRect.Contains(Event.current.mousePosition))
        {
            isListCollapsed = !isListCollapsed;
            Event.current.Use();
            editor.Repaint();
        }
        
        // Change cursor to pointer when hovering over the clickable area
        if (Event.current.type == EventType.Repaint && fullButtonItemRect.Contains(Event.current.mousePosition))
        {
            EditorGUIUtility.AddCursorRect(fullButtonItemRect, MouseCursor.Link);
        }
        
        // Draw the timeline graphics (line only, no dot)
        if (Event.current.type == EventType.Repaint)
        {
            Color timelineColor = Color.green;
            float centerX = timelineRect.center.x;
            float lineWidth = 2f;
            
            // Draw connecting lines through the button area (no dot)
            if (!isFirst)
            {
                Handles.color = timelineColor;
                Handles.DrawAAPolyLine(lineWidth, new Vector3(centerX, fullButtonItemRect.yMin, 0), new Vector3(centerX, fullButtonItemRect.yMax, 0));
            }
        }
    }

    private void DrawVersionListItem(UltiPawVersion ver, bool isFirst, bool isLast)
    {
        string binPath = UltiPawUtils.GetVersionBinPath(ver.version, ver.defaultAviVersion);
        bool isDownloaded = !string.IsNullOrEmpty(binPath) && File.Exists(binPath);
        bool isSelected = ver.Equals(editor.selectedVersionForAction);
        bool isApplied = ver.Equals(editor.ultiPawTarget.appliedUltiPawVersion);

        DrawVersionItemInternal(ver, isFirst, isLast, isSelected, isApplied, () => {
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
                // Changelog button (only show when not displaying all changelogs and changelog exists)
                if (!displayAllChangelogs && !string.IsNullOrEmpty(ver.changelog))
                {
                    string changelogKey = $"{ver.version}_{ver.scope}";
                    bool showingChangelog = individualChangelogStates.ContainsKey(changelogKey) && individualChangelogStates[changelogKey];
                    
                    if (GUILayout.Button(EditorGUIUtility.IconContent("UnityEditor.ConsoleWindow"), GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        individualChangelogStates[changelogKey] = !showingChangelog;
                    }
                }
                
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
        });
    }
    
    private void DrawResetVersionItem(bool isFirst)
    {
        bool isSelected = RESET_VERSION.Equals(editor.selectedVersionForAction);
        var fileManagerService = new FileManagerService();
        bool canReset = fileManagerService.BackupExists(actions.GetCurrentFBXPath()) || editor.isUltiPaw;
        bool isApplied = !editor.isUltiPaw; // Reset is "applied" when we're not in UltiPaw state

        DrawVersionItemInternal(RESET_VERSION, isFirst, true, isSelected, isApplied, () => {
            GUILayout.Label("Base Default Winterpaw", GUILayout.Width(140));
            GUILayout.FlexibleSpace();
            
            if (isApplied)
            {
                DrawScopeLabel("Current", new Color(0.33f, 0.79f, 0f));
                GUILayout.Space(10);
            }
            
            // Removed the "Reset" label as requested
            
            // Show changelog button if not displaying all changelogs
            if (!displayAllChangelogs && !string.IsNullOrEmpty(RESET_VERSION.changelog))
            {
                string changelogKey = "reset_base";
                bool showingChangelog = individualChangelogStates.ContainsKey(changelogKey) && individualChangelogStates[changelogKey];
                
                if (GUILayout.Button(EditorGUIUtility.IconContent("UnityEditor.ConsoleWindow"), GUILayout.Width(22), GUILayout.Height(22)))
                {
                    individualChangelogStates[changelogKey] = !showingChangelog;
                }
            }
            
            // No download/delete buttons for reset option
        }, canReset ? null : "No backup available");
    }
    
    private void DrawVersionItemInternal(UltiPawVersion ver, bool isFirst, bool isLast, bool isSelected, bool isApplied, System.Action drawContent, string disabledReason = null)
    {
        bool isDisabled = !string.IsNullOrEmpty(disabledReason);
        
        // Mark the start of the entire version item area
        Rect versionItemStartRect = GUILayoutUtility.GetRect(0, 0);
        
        // Start the whole version item layout
        EditorGUILayout.BeginVertical();
        
        // Add space above version box
        GUILayout.Space(2);
        
        EditorGUILayout.BeginHorizontal();
        
        // Reserve space for timeline - this will span the entire version item height
        Rect timelineRect = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f));
        
        // Right side content
        EditorGUILayout.BeginVertical();
        
        // Create custom GUIStyle for selected helpbox
        GUIStyle helpBoxStyle = new GUIStyle(EditorStyles.helpBox);
        if (isSelected && !isDisabled)
        {
            // Make selected version brighter
            Color originalBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(originalBg.r * 1.3f, originalBg.g * 1.3f, originalBg.b * 1.3f, originalBg.a);
        }
        else if (isDisabled)
        {
            // Make disabled versions grayed out
            GUI.backgroundColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        }
        
        using (new EditorGUI.DisabledScope(isDisabled))
        {
            EditorGUILayout.BeginVertical(helpBoxStyle);
            EditorGUILayout.BeginHorizontal();
            
            drawContent();
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        
        // Reset background color
        GUI.backgroundColor = Color.white;
        
        // Show changelog based on display mode
        bool shouldShowChangelog = false;
        if (displayAllChangelogs)
        {
            shouldShowChangelog = !string.IsNullOrEmpty(ver.changelog);
        }
        else
        {
            string changelogKey = ver == RESET_VERSION ? "reset_base" : $"{ver.version}_{ver.scope}";
            shouldShowChangelog = individualChangelogStates.ContainsKey(changelogKey) && 
                                individualChangelogStates[changelogKey] && 
                                !string.IsNullOrEmpty(ver.changelog);
        }

        if (shouldShowChangelog)
        {
            EditorGUILayout.LabelField(ver.changelog, EditorStyles.wordWrappedLabel);
        }
        
        if (isDisabled && !string.IsNullOrEmpty(disabledReason))
        {
            EditorGUILayout.LabelField(disabledReason, EditorStyles.miniLabel);
        }
        
        EditorGUILayout.EndVertical(); // End right side content
        EditorGUILayout.EndHorizontal(); // End main horizontal layout
        
        // Add space below version box
        GUILayout.Space(2);
        
        EditorGUILayout.EndVertical(); // End whole version item
        
        // Get the rect for the entire version item after all layout is complete
        Rect versionItemEndRect = GUILayoutUtility.GetRect(0, 0);
        Rect fullVersionItemRect = new Rect(versionItemStartRect.x, versionItemStartRect.y, 
                                           versionItemStartRect.width, versionItemEndRect.y - versionItemStartRect.y);
        
        // Handle dot interaction
        Vector2 dotCenter = new Vector2(timelineRect.center.x, fullVersionItemRect.center.y);
        float dotRadius = 4f;
        float clickableRadius = 8f;
        Rect dotClickArea = new Rect(dotCenter.x - clickableRadius, dotCenter.y - clickableRadius, 
                                    clickableRadius * 2, clickableRadius * 2);
        
        // Handle click on dot OR anywhere in the full version item area (only if not disabled)
        if (!isDisabled && Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            bool clickedOnDot = dotClickArea.Contains(Event.current.mousePosition);
            bool clickedOnVersionArea = fullVersionItemRect.Contains(Event.current.mousePosition);
            
            if (clickedOnDot || clickedOnVersionArea)
            {
                if (isSelected)
                {
                    editor.selectedVersionForAction = null;
                }
                else
                {
                    editor.selectedVersionForAction = ver;
                }
                Event.current.Use();
                editor.Repaint();
            }
        }
        
        // Draw the timeline graphics
        if (Event.current.type == EventType.Repaint)
        {
            Color timelineColor = isDisabled ? Color.gray : Color.green;
            float centerX = timelineRect.center.x;
            float centerY = fullVersionItemRect.center.y;
            
            // Draw selection circle if this version is selected
            if (isSelected && !isDisabled)
            {
                Handles.color = timelineColor;
                Handles.DrawWireDisc(new Vector3(centerX, centerY, 0), Vector3.forward, dotRadius + 3f);
            }
            
            // Draw the dot
            Handles.color = timelineColor;
            Handles.DrawSolidDisc(new Vector3(centerX, centerY, 0), Vector3.forward, dotRadius);

            // Draw the connecting lines spanning the full item height (including spaces)
            float lineWidth = 2f;
            if (!isFirst)
            {
                Handles.DrawAAPolyLine(lineWidth, new Vector3(centerX, fullVersionItemRect.yMin, 0), new Vector3(centerX, centerY - dotRadius, 0));
            }
            if (!isLast)
            {
                Handles.DrawAAPolyLine(lineWidth, new Vector3(centerX, centerY + dotRadius, 0), new Vector3(centerX, fullVersionItemRect.yMax, 0));
            }
        }
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