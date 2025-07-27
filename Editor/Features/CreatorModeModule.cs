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
    private UltiPawVersion previouslySelectedVersion = null;
    
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
            PopulateParentVersionDropdown();
            
            // Auto-populate fields if an unsubmitted version is selected
            if (editor.selectedVersionForAction != previouslySelectedVersion)
            {
                if (editor.selectedVersionForAction != null && editor.selectedVersionForAction.isUnsubmitted)
                {
                    PopulateFieldsFromVersion(editor.selectedVersionForAction);
                }
                previouslySelectedVersion = editor.selectedVersionForAction;
            }
            
            if (selectedParentVersionIndex == -1)
            {
                SetDefaultVersionNumbers(editor.ultiPawTarget.appliedUltiPawVersion);
            }

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
                EditorGUILayout.BeginHorizontal();
                
                // Test Button
                if (GUILayout.Button(editor.isSubmitting ? "Building..." : "Test", GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog("Confirm Test Build", "This will create and apply the new version locally without uploading it. The original FBX will be backed up.", "Build and Test", "Cancel"))
                    {
                        EditorCoroutineUtility.StartCoroutineOwnerless(BuildAndApplyLocalVersionCoroutine());
                    }
                }
                
                // Submit Button
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button(editor.isSubmitting ? "Submitting..." : "Submit New Version", GUILayout.Height(30)))
                {
                    string parentInfo = selectedParentVersionObject != null ? $"Parent: {selectedParentVersionObject.version}\n" : "";
                    if (EditorUtility.DisplayDialog("Confirm Upload", $"This will create and upload the new version files.\n\nVersion: {newVersionString} ({newVersionScope})\n{parentInfo}This action is irreversible.", "Upload", "Cancel"))
                    {
                        EditorCoroutineUtility.StartCoroutineOwnerless(SubmitNewVersionCoroutine());
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
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
        if (EditorGUI.EndChangeCheck() || (selectedParentVersionIndex == -1 && defaultParentIndex != -1 && currentParentPopupIndex != newParentIndex))
        {
            selectedParentVersionIndex = newParentIndex;
            if (selectedParentVersionIndex >= 0 && selectedParentVersionIndex < compatibleParentVersions.Count)
            {
                 selectedParentVersionObject = compatibleParentVersions[selectedParentVersionIndex];
                 SetDefaultVersionNumbers(selectedParentVersionObject);
            }
            else
            {
                 selectedParentVersionObject = null;
                 SetDefaultVersionNumbers(null);
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
            defaultParentIndex = compatibleParentVersions.FindIndex(v => v.Equals(applied));
            if (defaultParentIndex == -1) defaultParentIndex = 0;
        }
        else { defaultParentIndex = 0; }
        
        if (selectedParentVersionIndex == -1 && defaultParentIndex < compatibleParentVersions.Count && defaultParentIndex >= 0)
        {
            selectedParentVersionObject = compatibleParentVersions[defaultParentIndex];
            SetDefaultVersionNumbers(selectedParentVersionObject);
        }
    }

    private bool IsNewVersionValid(string newVersionString)
    {
        string baseVersionString = selectedParentVersionObject?.version;
        if (string.IsNullOrEmpty(baseVersionString)) return true;
        return editor.CompareVersions(newVersionString, baseVersionString) > 0;
    }

    private (UltiPawVersion metadata, string zipPath) BuildNewVersion()
    {
        var customFbxGO = editor.customFbxForCreatorProp.objectReferenceValue as GameObject;
        var customFbxPath = AssetDatabase.GetAssetPath(customFbxGO);
        var ultipawAvatar = editor.ultipawAvatarForCreatorProp.objectReferenceValue as Avatar;
        var logicPrefab = editor.avatarLogicPrefabProp.objectReferenceValue as GameObject;

        string originalFbxPath = new VersionActions(editor, networkService, fileManagerService).GetCurrentFBXPath() + FileManagerService.OriginalSuffix;
        if (!File.Exists(originalFbxPath))
            throw new Exception("Original FBX backup (.old) not found. Apply an UltiPaw version first to create the backup.");

        if (selectedParentVersionObject == null)
            throw new Exception("A Parent Version must be selected.");

        string newVersionString = $"{newVersionMajor}.{newVersionMinor}.{newVersionPatch}";
        EditorUtility.DisplayProgressBar("Preparing Build", "Creating version package...", 0.2f);

        string tempZipPath = fileManagerService.CreateVersionPackageForUpload(
            newVersionString,
            selectedParentVersionObject.defaultAviVersion,
            originalFbxPath,
            customFbxGO,
            ultipawAvatar,
            logicPrefab,
            selectedParentVersionObject
        );

        EditorUtility.DisplayProgressBar("Preparing Build", "Calculating hashes and dependencies...", 0.5f);
        string binPath = Path.Combine(UltiPawUtils.GetVersionDataPath(newVersionString, selectedParentVersionObject.defaultAviVersion), "ultipaw.bin");
        
        var metadata = new UltiPawVersion {
            version = newVersionString,
            scope = newVersionScope,
            changelog = newChangelog,
            defaultAviVersion = selectedParentVersionObject.defaultAviVersion,
            parentVersion = selectedParentVersionObject?.version,
            dependencies = fileManagerService.FindPrefabDependencies(logicPrefab),
            customBlendshapes = editor.ultiPawTarget.customBlendshapesForCreator.ToArray(),
            // Local-only data for repopulating fields
            baseFbxHash = fileManagerService.CalculateFileHash(originalFbxPath),
            customFbxPath = customFbxPath,
            ultipawAvatarPath = AssetDatabase.GetAssetPath(ultipawAvatar),
            logicPrefabPath = AssetDatabase.GetAssetPath(logicPrefab),
            // Hashes of created files
            customAviHash = fileManagerService.CalculateFileHash(binPath),
            appliedCustomAviHash = fileManagerService.CalculateFileHash(customFbxPath)
        };
        
        return (metadata, tempZipPath);
    }

    private void SaveUnsubmittedVersion(UltiPawVersion versionToSave)
    {
        string path = UltiPawUtils.UNSUBMITTED_VERSIONS_FILE;
        List<UltiPawVersion> unsubmitted = new List<UltiPawVersion>();
        if (File.Exists(path))
        {
            unsubmitted = JsonConvert.DeserializeObject<List<UltiPawVersion>>(File.ReadAllText(path)) ?? new List<UltiPawVersion>();
        }

        int existingIndex = unsubmitted.FindIndex(v => v.Equals(versionToSave));
        if (existingIndex != -1) unsubmitted[existingIndex] = versionToSave;
        else unsubmitted.Add(versionToSave);

        UltiPawUtils.EnsureDirectoryExists(path);
        File.WriteAllText(path, JsonConvert.SerializeObject(unsubmitted, Formatting.Indented, new StringEnumConverter()));
        
        editor.LoadUnsubmittedVersions();
        editor.Repaint();
    }

    private void RemoveUnsubmittedVersion(UltiPawVersion versionToRemove)
    {
        string path = UltiPawUtils.UNSUBMITTED_VERSIONS_FILE;
        if (!File.Exists(path)) return;
        
        List<UltiPawVersion> unsubmitted = JsonConvert.DeserializeObject<List<UltiPawVersion>>(File.ReadAllText(path)) ?? new List<UltiPawVersion>();

        unsubmitted.RemoveAll(v => v.Equals(versionToRemove));

        File.WriteAllText(path, JsonConvert.SerializeObject(unsubmitted, Formatting.Indented, new StringEnumConverter()));
        
        editor.LoadUnsubmittedVersions();
        editor.Repaint();
    }

    private void PopulateFieldsFromVersion(UltiPawVersion ver)
    {
        var parsedVersion = editor.ParseVersion(ver.version);
        newVersionMajor = parsedVersion.Major;
        newVersionMinor = parsedVersion.Minor;
        newVersionPatch = parsedVersion.Build;
        newVersionScope = ver.scope;
        newChangelog = ver.changelog;

        if (!string.IsNullOrEmpty(ver.parentVersion))
        {
            int parentIdx = compatibleParentVersions.FindIndex(p => p.version == ver.parentVersion);
            if (parentIdx != -1)
            {
                selectedParentVersionIndex = parentIdx;
                selectedParentVersionObject = compatibleParentVersions[parentIdx];
            }
        }
        
        editor.customFbxForCreatorProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ver.customFbxPath);
        editor.ultipawAvatarForCreatorProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Avatar>(ver.ultipawAvatarPath);
        editor.avatarLogicPrefabProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ver.logicPrefabPath);
        
        editor.customBlendshapesForCreatorProp.ClearArray();
        if(ver.customBlendshapes != null)
        {
            for(int i = 0; i < ver.customBlendshapes.Length; i++)
            {
                editor.customBlendshapesForCreatorProp.InsertArrayElementAtIndex(i);
                editor.customBlendshapesForCreatorProp.GetArrayElementAtIndex(i).stringValue = ver.customBlendshapes[i];
            }
        }
        editor.serializedObject.ApplyModifiedProperties();
        editor.Repaint();
    }

    private IEnumerator BuildAndApplyLocalVersionCoroutine()
    {
        editor.isSubmitting = true;
        editor.submitError = "";
        editor.Repaint();

        (UltiPawVersion metadata, string zipPath) buildResult = default;
        Exception buildError = null;

        try { buildResult = BuildNewVersion(); }
        catch (Exception ex) { buildError = ex; }

        if (buildError != null)
        {
            editor.submitError = buildError.Message;
            Debug.LogError($"[CreatorMode] Test Build failed: {buildError}");
        }
        else
        {
            SaveUnsubmittedVersion(buildResult.metadata);
            var versionActions = new VersionActions(editor, networkService, fileManagerService);
            
            AssetDatabase.Refresh();
            while(EditorApplication.isCompiling || EditorApplication.isUpdating) { yield return null; }

            // Apply the newly built version
            yield return versionActions.ApplyOrResetCoroutine(buildResult.metadata, false);
            
            EditorUtility.DisplayDialog("Test Build Complete", $"Version {buildResult.metadata.version} has been built and applied locally. You can find it in the version list.", "OK");
        }

        if (!string.IsNullOrEmpty(buildResult.zipPath) && File.Exists(buildResult.zipPath))
        {
            File.Delete(buildResult.zipPath);
        }
        editor.isSubmitting = false;
        EditorUtility.ClearProgressBar();
        editor.Repaint();
    }
    
    private IEnumerator SubmitNewVersionCoroutine()
    {
        editor.isSubmitting = true;
        editor.submitError = "";
        editor.Repaint();

        (UltiPawVersion metadata, string zipPath) buildResult = default;
        Exception error = null;
        System.Threading.Tasks.Task<(bool success, string response, string error)> uploadTask = null;

        try
        {
            buildResult = BuildNewVersion();
            EditorUtility.DisplayProgressBar("Uploading", "Sending package to server...", 0.8f);
            string metadataJson = JsonConvert.SerializeObject(buildResult.metadata, new StringEnumConverter());
            string uploadUrl = $"{UltiPawUtils.getServerUrl()}{UltiPawUtils.NEW_VERSION_ENDPOINT}?t={editor.authToken}";
            uploadTask = networkService.SubmitNewVersionAsync(uploadUrl, editor.authToken, buildResult.zipPath, metadataJson);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (uploadTask != null)
        {
            while (!uploadTask.IsCompleted) { yield return null; }
        }

        try
        {
            if (error != null) throw error;

            if (uploadTask != null)
            {
                var (success, response, uploadError) = uploadTask.Result;
                if (!success) throw new Exception(uploadError);

                EditorUtility.DisplayDialog("Upload Successful", $"New UltiPaw version {buildResult.metadata.version} has been uploaded.", "OK");
                RemoveUnsubmittedVersion(buildResult.metadata);
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
            EditorUtility.ClearProgressBar();
            if (!string.IsNullOrEmpty(buildResult.zipPath) && File.Exists(buildResult.zipPath))
                File.Delete(buildResult.zipPath);
            
            editor.isSubmitting = false;
            editor.Repaint();
        }
    }
}
#endif