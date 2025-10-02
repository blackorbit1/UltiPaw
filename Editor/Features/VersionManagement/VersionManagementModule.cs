#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class VersionManagementModule
{
    private readonly UltiPawEditor editor;
    public readonly VersionActions actions;
    public readonly FileConfigurationDrawer fileConfigDrawer;
    private readonly VersionListDrawer versionListDrawer;
    private readonly FileManagerService fileManagerService;
    
    private enum ActionType { INSTALL, UPDATE, DOWNGRADE, RESET, UNAVAILABLE }
    
    private bool versionsFoldout = true;
    private bool hasShownMissingVersionWarning;
    private UltiPawVersion lastSelectionForWarning;
    private bool lastRecommendedWasNull;


    public VersionManagementModule(UltiPawEditor editor, NetworkService network, FileManagerService files)
    {
        this.editor = editor;
        actions = new VersionActions(editor, network, files);
        fileManagerService = files;
        fileConfigDrawer = new FileConfigurationDrawer(editor, actions);
        versionListDrawer = new VersionListDrawer(editor, actions);
    }

    public void OnEnable()
    {
        fileConfigDrawer.OnEnable();
        ResetMissingVersionWarning();
    }

    public void OnFBXChange()
    {
        ResetMissingVersionWarning();
        actions.UpdateCurrentBaseFbxHash();
        actions.StartVersionFetch();
    }

    public void ResetMissingVersionWarning()
    {
        hasShownMissingVersionWarning = false;
        lastSelectionForWarning = editor.selectedVersionForAction;
        lastRecommendedWasNull = editor.recommendedVersion == null;
    }

    public void Draw()
    {
        // TODO fileConfigDrawer.Draw();
        DrawFetchUpdatesButton();
        actions.DisplayErrors();
        
        versionListDrawer.Draw();
        
        EditorGUILayout.Space();
        versionListDrawer.DrawUpdateNotification();
        DrawActionButtons();
    }

    private void DrawFetchUpdatesButton()
    {
        using (new EditorGUI.DisabledScope(editor.isFetching || editor.isDownloading || editor.isDeleting))
        {
            if (GUILayout.Button(editor.isFetching ? "Fetching..." : "Check for Updates"))
            {
                ResetMissingVersionWarning();
                actions.StartVersionFetch();
            }
        }
    }

    private void DrawActionButtons()
    {
        bool canInteract = !editor.isFetching && !editor.isDownloading && !editor.isDeleting;
        var selectedVersion = editor.selectedVersionForAction;
        bool recommendedIsNull = editor.recommendedVersion == null;
        if (editor.selectedVersionForAction != lastSelectionForWarning || recommendedIsNull != lastRecommendedWasNull)
        {
            hasShownMissingVersionWarning = false;
            lastSelectionForWarning = editor.selectedVersionForAction;
            lastRecommendedWasNull = recommendedIsNull;
        }


        if (selectedVersion == null) // If no version is selected, select the recommended version
        {
            if (editor.recommendedVersion == null)
            {
                if (!hasShownMissingVersionWarning)
                {
                    UltiPawLogger.LogWarning("[UltiPawEditor] No recommended version available. Please select a version from the list.");
                    hasShownMissingVersionWarning = true;
                }
                return;
            }
            selectedVersion = editor.recommendedVersion;
            editor.selectedVersionForAction = selectedVersion;
            lastSelectionForWarning = editor.selectedVersionForAction;
        }
        
        bool selectionIsValid = selectedVersion != null;
        bool isResetSelected = selectedVersion == VersionListDrawer.RESET_VERSION;
        
        using (new EditorGUI.DisabledScope(!canInteract))
        {
            // Main Apply/Update/Downgrade/Reset Button
            bool canReset = fileManagerService.BackupExists(actions.GetCurrentFBXPath()) || editor.isUltiPaw;
            bool buttonDisabled = !selectionIsValid ||
                                   (!isResetSelected && selectedVersion.Equals(editor.ultiPawTarget.appliedUltiPawVersion)) ||
                                   (isResetSelected && !canReset);
            
            using (new EditorGUI.DisabledScope(buttonDisabled))
            {
                var action = GetActionType();
                string buttonText = GetActionButtonText(action, selectedVersion);
                
                // Set button color based on action type
                if (action == ActionType.DOWNGRADE)
                {
                    GUI.backgroundColor = EditorUIUtils.OrangeColor;
                }
                else if (action != ActionType.RESET) // Keep default color for reset, green for others
                {
                    GUI.backgroundColor = Color.green;
                }
                // For RESET, use default button color (no background color change)

                if (GUILayout.Button(buttonText, GUILayout.Height(40)))
                {
                    if (isResetSelected)
                    {
                        if (EditorUtility.DisplayDialog("Confirm Reset", "This will restore the original FBX from its backup and reapply the default avatar configuration.", "Reset", "Cancel"))
                        {
                            actions.StartReset();
                        }
                    }
                    else
                    {
                        string binPath = UltiPawUtils.GetVersionBinPath(selectedVersion.version, selectedVersion.defaultAviVersion);
                        bool isDownloaded = !string.IsNullOrEmpty(binPath) && System.IO.File.Exists(binPath);
                        
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
                }
                GUI.backgroundColor = Color.white;
            }

            // Note: Reset button has been removed - reset functionality is now handled through the version list
        }
    }

    private ActionType GetActionType()
    {
        if (editor.selectedVersionForAction == null) return ActionType.UNAVAILABLE;
        
        if (editor.selectedVersionForAction == VersionListDrawer.RESET_VERSION) return ActionType.RESET;

        if (!editor.isUltiPaw) return ActionType.INSTALL; 
        
        var compare = editor.CompareVersions(editor.selectedVersionForAction.version, editor.ultiPawTarget.appliedUltiPawVersion?.version ?? "0.0.0");
        if (compare > 0) return ActionType.UPDATE;
        if (compare < 0) return ActionType.DOWNGRADE;
        return ActionType.UNAVAILABLE;
    }

    private string GetActionButtonText(ActionType action, UltiPawVersion selectedVersion)
    {
        if (selectedVersion == null) return "Select a Version";
        
        if (action == ActionType.RESET) return "Reset to Original Avatar";
        
        string binPath = UltiPawUtils.GetVersionBinPath(selectedVersion.version, selectedVersion.defaultAviVersion);
        bool isDownloaded = !string.IsNullOrEmpty(binPath) && System.IO.File.Exists(binPath);
        string downloadPrefix = isDownloaded ? "" : "Download and ";

        return action switch
        {
            ActionType.INSTALL => $"{downloadPrefix}Turn into UltiPaw",
            ActionType.UPDATE => $"{downloadPrefix}Update to v{selectedVersion.version}",
            ActionType.DOWNGRADE => $"{downloadPrefix}Downgrade to v{selectedVersion.version}",
            _ => $"Installed (v{editor.ultiPawTarget.appliedUltiPawVersion?.version})"
        };
    }
}
#endif
