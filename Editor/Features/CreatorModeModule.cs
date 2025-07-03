#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

// Handles all UI and logic for the Creator Mode feature.
public class CreatorModeModule
{
    private readonly UltiPawEditor editor;
    private readonly NetworkService networkService;
    private readonly FileManagerService fileManagerService;
    private ReorderableList blendshapeList;
    
    // UI State
    private bool creatorModeFoldout = true;
    private int newVersionMajor = 0, newVersionMinor = 1, newVersionPatch = 0;
    private Scope newVersionScope = Scope.BETA;
    private string newChangelog = "";

    private List<UltiPawVersion> compatibleParentVersions;
    private List<string> parentVersionDisplayOptions;
    private int defaultParentIndex;
    private int selectedParentVersionIndex = -1;
    private UltiPawVersion selectedParentVersionObject = null;
    
    public CreatorModeModule(UltiPawEditor editor)
    {
        this.editor = editor;
        this.networkService = new NetworkService();
        this.fileManagerService = new FileManagerService();
    }

    public void Initialize()
    {
        blendshapeList = new ReorderableList(editor.serializedObject, editor.customBlendshapesForCreatorProp, true, true, true, true);
        
        blendshapeList.drawHeaderCallback = (Rect rect) => EditorGUI.LabelField(rect, "Custom Blendshapes to Expose");
        blendshapeList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            var element = blendshapeList.serializedProperty.GetArrayElementAtIndex(index);
            rect.y += 2;
            EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), element, GUIContent.none);
        };
        blendshapeList.onAddDropdownCallback = OnAddBlendshapeDropdown;
    }

    public void Draw()
    {
        EditorGUILayout.PropertyField(editor.isCreatorModeProp, new GUIContent("Enable Creator Mode"));
        if (!editor.isCreatorModeProp.boolValue) return;

        creatorModeFoldout = EditorGUILayout.Foldout(creatorModeFoldout, "Creator Mode", true, EditorStyles.foldoutHeader);
        if (!creatorModeFoldout) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        using (new EditorGUI.DisabledScope(editor.isSubmitting))
        {
            // IMPROVEMENT: This now only runs if the parent hasn't been manually selected.
            // When a parent is selected, the version numbers are set by the dropdown's change event.
            if (selectedParentVersionIndex == -1)
            {
                SetDefaultVersionNumbers(editor.ultiPawTarget.appliedUltiPawVersion);
            }
            PopulateParentVersionDropdown();

            EditorGUILayout.PropertyField(editor.customFbxForCreatorProp, new GUIContent("Custom FBX (Transformed)"));
            EditorGUILayout.PropertyField(editor.ultipawAvatarForCreatorProp, new GUIContent("UltiPaw Avatar (Transformed)"));
            EditorGUILayout.PropertyField(editor.avatarLogicPrefabProp, new GUIContent("Avatar Logic Prefab"));
            
            EditorGUILayout.Space();
            blendshapeList.DoLayoutList();
            EditorGUILayout.Space();

            DrawParentVersionDropdown();

            EditorGUILayout.LabelField("New Version Details:", EditorStyles.miniBoldLabel);
            DrawVersionFields();
            
            string newVersionString = $"{newVersionMajor}.{newVersionMinor}.{newVersionPatch}";
            // FIX: Validation now uses the selected parent version, not the applied one.
            bool isVersionValid = IsNewVersionValid(newVersionString);
            if (!isVersionValid)
            {
                string baseVer = selectedParentVersionObject?.version ?? "the base version";
                EditorGUILayout.HelpBox($"New version must be higher than the selected parent version (v{baseVer}).", MessageType.Warning);
            }

            EditorGUILayout.LabelField("Changelog:", EditorStyles.miniBoldLabel);
            newChangelog = EditorGUILayout.TextArea(newChangelog, GUILayout.Height(80));

            EditorGUILayout.Space();
            bool canSubmit = editor.customFbxForCreatorProp.objectReferenceValue != null &&
                             editor.avatarLogicPrefabProp.objectReferenceValue != null &&
                             editor.ultipawAvatarForCreatorProp.objectReferenceValue != null &&
                             selectedParentVersionObject != null &&
                             isVersionValid;
            
            using (new EditorGUI.DisabledScope(!canSubmit))
            {
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button(editor.isSubmitting ? "Submitting..." : "Submit New Version", GUILayout.Height(30)))
                {
                    string parentInfo = selectedParentVersionObject != null ? $"Parent: {selectedParentVersionObject.version}\n" : "";
                    if (EditorUtility.DisplayDialog("Confirm Upload", $"This will create and upload the new version files.\n\nVersion: {newVersionString} ({newVersionScope})\n{parentInfo}This action is irreversible.", "Upload", "Cancel"))
                    {
                        StartSubmit();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
        }
        
        if (!string.IsNullOrEmpty(editor.submitError))
        {
            EditorGUILayout.HelpBox("Submission Error: " + editor.submitError, MessageType.Error);
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void OnAddBlendshapeDropdown(Rect buttonRect, ReorderableList l)
    {
        var menu = new GenericMenu();
        var fbxObject = editor.customFbxForCreatorProp.objectReferenceValue as GameObject;
        if (fbxObject == null) { menu.AddDisabledItem(new GUIContent("Assign a Custom FBX first")); menu.ShowAsContext(); return; }

        var smr = fbxObject.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null || smr.sharedMesh == null) { menu.AddDisabledItem(new GUIContent("FBX has no SkinnedMeshRenderer/Mesh")); menu.ShowAsContext(); return; }

        var allBlendshapes = Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Select(i => smr.sharedMesh.GetBlendShapeName(i));
        var existingBlendshapes = editor.ultiPawTarget.customBlendshapesForCreator;
        var availableBlendshapes = allBlendshapes.Except(existingBlendshapes).OrderBy(s => s).ToList();

        if (availableBlendshapes.Count == 0) { menu.AddDisabledItem(new GUIContent("No more blendshapes to add")); }
        
        foreach (var shapeName in availableBlendshapes)
        {
            menu.AddItem(new GUIContent(shapeName), false, () => {
                var index = l.serializedProperty.arraySize;
                l.serializedProperty.arraySize++;
                l.serializedProperty.GetArrayElementAtIndex(index).stringValue = shapeName;
                editor.serializedObject.ApplyModifiedProperties();
            });
        }
        menu.ShowAsContext();
    }

    private void DrawParentVersionDropdown()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Parent Version:", GUILayout.Width(100));
        int currentParentPopupIndex = (selectedParentVersionIndex == -1) ? defaultParentIndex : selectedParentVersionIndex;
        
        EditorGUI.BeginChangeCheck();
        int newParentIndex = EditorGUILayout.Popup(currentParentPopupIndex, parentVersionDisplayOptions.ToArray());
        // FIX: The check now triggers when the dropdown value changes OR if it's the first run and a default is set.
        if (EditorGUI.EndChangeCheck() || (selectedParentVersionIndex == -1 && defaultParentIndex != -1 && currentParentPopupIndex != newParentIndex))
        {
            selectedParentVersionIndex = newParentIndex;
            if (selectedParentVersionIndex >= 0 && selectedParentVersionIndex < compatibleParentVersions.Count)
            {
                 selectedParentVersionObject = compatibleParentVersions[selectedParentVersionIndex];
                 // IMPROVEMENT: Update version numbers when dropdown changes
                 SetDefaultVersionNumbers(selectedParentVersionObject);
            }
            else
            {
                 selectedParentVersionObject = null;
                 SetDefaultVersionNumbers(null); // Reset to 0.1.0 if "None" or invalid
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawVersionFields()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Version:", GUILayout.Width(50));
        newVersionMajor = EditorGUILayout.IntField(newVersionMajor, GUILayout.Width(40));
        EditorGUILayout.LabelField(".", GUILayout.Width(10));
        newVersionMinor = EditorGUILayout.IntField(newVersionMinor, GUILayout.Width(40));
        EditorGUILayout.LabelField(".", GUILayout.Width(10));
        newVersionPatch = EditorGUILayout.IntField(newVersionPatch, GUILayout.Width(40));
        newVersionScope = (Scope)EditorGUILayout.EnumPopup("", newVersionScope, GUILayout.Width(100));
        EditorGUILayout.EndHorizontal();
    }
    
    private void StartSubmit()
    {
        EditorCoroutineUtility.StartCoroutineOwnerless(SubmitNewVersionCoroutine());
    }
    
    // FIX: Modified to accept a base version object to set numbers from.
    private void SetDefaultVersionNumbers(UltiPawVersion baseVersionObject)
    {
        string currentVersionStr = baseVersionObject?.version;
        
        if (string.IsNullOrEmpty(currentVersionStr))
        {
            newVersionMajor = 0; newVersionMinor = 1; newVersionPatch = 0;
        }
        else
        {
            Version baseVersion = editor.ParseVersion(currentVersionStr);
            newVersionMajor = baseVersion.Major; 
            newVersionMinor = baseVersion.Minor; 
            newVersionPatch = baseVersion.Build + 1;
        }
    }
    
    private void PopulateParentVersionDropdown()
    {
        compatibleParentVersions = editor.serverVersions.OrderByDescending(v => editor.ParseVersion(v.version)).ToList();
        parentVersionDisplayOptions = compatibleParentVersions.Select(v => $"{v.version} ({v.scope})").ToList();

        var applied = editor.ultiPawTarget.appliedUltiPawVersion;
        if (applied != null)
        {
            // Find the index of the currently applied version in our sorted list.
            defaultParentIndex = compatibleParentVersions.FindIndex(v => v.Equals(applied));
            if (defaultParentIndex == -1) defaultParentIndex = 0; // Fallback to first item if not found
        }
        else { defaultParentIndex = 0; } // Default to the first (newest) item if nothing is applied.
        
        // IMPROVEMENT: Initialize the selected object if it hasn't been chosen yet by the user.
        if (selectedParentVersionIndex == -1 && defaultParentIndex < compatibleParentVersions.Count && defaultParentIndex >= 0)
        {
            selectedParentVersionObject = compatibleParentVersions[defaultParentIndex];
            // Also set the initial version numbers based on this default parent.
            SetDefaultVersionNumbers(selectedParentVersionObject);
        }
    }

    private bool IsNewVersionValid(string newVersionString)
    {
        // FIX: Compare against the selected parent, not the applied version.
        string baseVersionString = selectedParentVersionObject?.version;
        if (string.IsNullOrEmpty(baseVersionString)) return true; // No parent selected, any version is valid
        return editor.CompareVersions(newVersionString, baseVersionString) > 0;
    }
    
    private IEnumerator SubmitNewVersionCoroutine()
    {
        // --- 1. Setup ---
        editor.isSubmitting = true;
        editor.submitError = "";
        editor.Repaint();

        string tempZipPath = null;
        Exception setupException = null;
        System.Threading.Tasks.Task<(bool success, string response, string error)> uploadTask = null;

        // --- 2. Setup phase (no yield returns) ---
        try
        {
            var customFbxGO = editor.customFbxForCreatorProp.objectReferenceValue as GameObject;
            var customFbxPath = AssetDatabase.GetAssetPath(customFbxGO);
            var ultipawAvatar = editor.ultipawAvatarForCreatorProp.objectReferenceValue as Avatar;
            var logicPrefab = editor.avatarLogicPrefabProp.objectReferenceValue as GameObject;

            string originalFbxPath = new VersionActions(editor, networkService, fileManagerService).GetCurrentFBXPath() + FileManagerService.OriginalSuffix;
            if (!File.Exists(originalFbxPath))
                throw new Exception("Original FBX backup (.old) not found. Apply an UltiPaw version first.");

            if (selectedParentVersionObject == null)
                throw new Exception("A Parent Version must be selected.");

            string newVersionString = $"{newVersionMajor}.{newVersionMinor}.{newVersionPatch}";
            EditorUtility.DisplayProgressBar("Preparing Upload", "Creating version package...", 0.2f);
            
            tempZipPath = fileManagerService.CreateVersionPackageForUpload(
                newVersionString,
                selectedParentVersionObject.defaultAviVersion,
                originalFbxPath,
                customFbxGO,
                ultipawAvatar,
                logicPrefab,
                selectedParentVersionObject
            );
            
            EditorUtility.DisplayProgressBar("Preparing Upload", "Calculating hashes and dependencies...", 0.5f);
            string binPath = Path.Combine(UltiPawUtils.GetVersionDataPath(newVersionString, selectedParentVersionObject.defaultAviVersion), "ultipaw.bin");
            
            var metadata = new {
                baseFbxHash = fileManagerService.CalculateFileHash(originalFbxPath),
                version = newVersionString,
                scope = newVersionScope,
                changelog = newChangelog,
                defaultAviVersion = selectedParentVersionObject.defaultAviVersion,
                parentVersion = selectedParentVersionObject?.version,
                customAviHash = fileManagerService.CalculateFileHash(binPath),
                appliedCustomAviHash = fileManagerService.CalculateFileHash(customFbxPath),
                customBlendshapes = editor.ultiPawTarget.customBlendshapesForCreator.ToArray(),
                dependencies = fileManagerService.FindPrefabDependencies(logicPrefab)
            };
            string metadataJson = JsonConvert.SerializeObject(metadata, new StringEnumConverter());
            
            EditorUtility.DisplayProgressBar("Uploading", "Sending package to server...", 0.8f);
            string uploadUrl = $"{UltiPawUtils.getServerUrl()}{UltiPawUtils.NEW_VERSION_ENDPOINT}?t={editor.authToken}";
            uploadTask = networkService.SubmitNewVersionAsync(uploadUrl, editor.authToken, tempZipPath, metadataJson);
        }
        catch (Exception ex)
        {
            setupException = ex;
        }

        // --- 3. Upload phase (with yield returns, NOT in try/catch) ---
        if (setupException == null && uploadTask != null)
        {
            while (!uploadTask.IsCompleted) 
            { 
                yield return null; 
            }
        }

        // --- 4. Process result and cleanup ---
        try
        {
            if (setupException != null)
            {
                throw setupException;
            }

            if (uploadTask != null)
            {
                var (success, response, error) = uploadTask.Result;
                if (!success) 
                    throw new Exception(error);

                EditorUtility.DisplayDialog("Upload Successful", $"New UltiPaw version {newVersionMajor}.{newVersionMinor}.{newVersionPatch} has been uploaded.", "OK");
                new VersionActions(editor, networkService, fileManagerService).StartVersionFetch();
            }
        }
        catch (Exception ex)
        {
            editor.submitError = ex.Message;
            Debug.LogError($"[CreatorMode] Submission failed: {ex}");
        }
        finally
        {
            // --- 5. Cleanup ---
            EditorUtility.ClearProgressBar();
            if (!string.IsNullOrEmpty(tempZipPath) && File.Exists(tempZipPath)) 
                File.Delete(tempZipPath);
            
            editor.isSubmitting = false;
            editor.Repaint();
        }
    }
}
#endif