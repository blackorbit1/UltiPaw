﻿#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class VersionManagementModule
{
    private readonly UltiPawEditor editor;
    private readonly VersionActions actions;
    public readonly FileConfigurationDrawer fileConfigDrawer;
    private readonly VersionListDrawer versionListDrawer;
    private readonly BlendshapeDrawer blendshapeDrawer;
    private readonly FileManagerService fileManagerService;
    
    private enum ActionType { INSTALL, UPDATE, DOWNGRADE, UNAVAILABLE }
    
    private bool versionsFoldout = true;

    public VersionManagementModule(UltiPawEditor editor, NetworkService network, FileManagerService files)
    {
        this.editor = editor;
        actions = new VersionActions(editor, network, files);
        fileManagerService = files;
        fileConfigDrawer = new FileConfigurationDrawer(editor, actions);
        versionListDrawer = new VersionListDrawer(editor, actions);
        blendshapeDrawer = new BlendshapeDrawer(editor);
    }

    public void OnEnable()
    {
        fileConfigDrawer.OnEnable();
    }

    public void OnFBXChange()
    {
        actions.UpdateCurrentBaseFbxHash();
        actions.StartVersionFetch();
    }

    public void Draw()
    {
        // TODO fileConfigDrawer.Draw();
        DrawFetchUpdatesButton();
        versionsFoldout = EditorGUILayout.Foldout(versionsFoldout, "All UltiPaw Versions", true, EditorStyles.foldoutHeader);
        actions.DisplayErrors();
        
        if (versionsFoldout)
        {
            versionListDrawer.Draw();
        }
        
        EditorGUILayout.Space();
        versionListDrawer.DrawUpdateNotification();
        DrawActionButtons();
        blendshapeDrawer.Draw();
    }

    private void DrawFetchUpdatesButton()
    {
        using (new EditorGUI.DisabledScope(editor.isFetching || editor.isDownloading || editor.isDeleting))
        {
            if (GUILayout.Button(editor.isFetching ? "Fetching..." : "Check for Updates"))
            {
                actions.StartVersionFetch();
            }
        }
    }

    private void DrawActionButtons()
    {
        bool canInteract = !editor.isFetching && !editor.isDownloading && !editor.isDeleting;
        var selectedVersion = editor.selectedVersionForAction;

        if (selectedVersion == null) // If no version is selected, select the recommended version
        {
            selectedVersion = editor.recommendedVersion;
            editor.selectedVersionForAction = selectedVersion;
        }
        
        string binPath = UltiPawUtils.GetVersionBinPath(selectedVersion.version, selectedVersion.defaultAviVersion);
        bool isDownloaded = !string.IsNullOrEmpty(binPath) && System.IO.File.Exists(binPath);
        
        bool selectionIsValid = selectedVersion != null;
        
        using (new EditorGUI.DisabledScope(!canInteract))
        {
            // Main Apply/Update/Downgrade Button
            using (new EditorGUI.DisabledScope(!selectionIsValid || selectedVersion.Equals(editor.ultiPawTarget.appliedUltiPawVersion)))
            {
                var action = GetActionType();
                string buttonText = GetActionButtonText(action, isDownloaded);
                
                GUI.backgroundColor = action == ActionType.DOWNGRADE ? EditorUIUtils.OrangeColor : Color.green;

                if (GUILayout.Button(buttonText, GUILayout.Height(40)))
                {
                    if (EditorUtility.DisplayDialog("Confirm Transformation", $"This will modify your base FBX file using UltiPaw version '{selectedVersion.version}'.\nA backup will be created.", "Proceed", "Cancel"))
                    {
                        if (isDownloaded)
                        {
                            actions.StartApplyVersion();
                        }
                        else
                        {
                            actions.StartVersionDownload(selectedVersion, true);
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            // Reset Button
            bool canRestore = fileManagerService.BackupExists(actions.GetCurrentFBXPath()) || editor.isUltiPaw;
            using (new EditorGUI.DisabledScope(!canRestore))
            {
                if (GUILayout.Button("Reset to Original FBX"))
                {
                    if (EditorUtility.DisplayDialog("Confirm Reset", "This will restore the original FBX from its backup and reapply the default avatar configuration.", "Reset", "Cancel"))
                    {
                        actions.StartReset();
                    }
                }
            }
        }
    }

    private ActionType GetActionType()
    {
        if (editor.selectedVersionForAction == null) return ActionType.UNAVAILABLE;

        if (!editor.isUltiPaw) return ActionType.INSTALL;
        
        var compare = editor.CompareVersions(editor.selectedVersionForAction.version, editor.ultiPawTarget.appliedUltiPawVersion?.version ?? "0.0.0");
        if (compare > 0) return ActionType.UPDATE;
        if (compare < 0) return ActionType.DOWNGRADE;
        return ActionType.UNAVAILABLE;
    }

    private string GetActionButtonText(ActionType action, bool isDownloaded)
    {
        if (editor.selectedVersionForAction == null) return "Select a Version";
        
        string downloadPrefix = isDownloaded ? "" : "Download and ";

        return action switch
        {
            ActionType.INSTALL => $"{downloadPrefix}Turn into UltiPaw",
            ActionType.UPDATE => $"{downloadPrefix}Update to v{editor.selectedVersionForAction.version}",
            ActionType.DOWNGRADE => $"{downloadPrefix}Downgrade to v{editor.selectedVersionForAction.version}",
            _ => $"Installed (v{editor.ultiPawTarget.appliedUltiPawVersion?.version})"
        };
    }
}
#endif