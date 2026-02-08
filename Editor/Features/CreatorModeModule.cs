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
using AutocompleteSearchField;

// Handles all UI and logic for the Creator Mode feature.
public class CreatorModeModule
{
    private readonly UltiPawEditor editor;
    private readonly NetworkService networkService;
    private readonly FileManagerService fileManagerService;
    private ReorderableList blendshapeList;
    private AutocompleteSearchField.AutocompleteSearchField blendshapeSearchField;
    private readonly Dictionary<string, AutocompleteSearchField.AutocompleteSearchField> correctiveSearchFields
        = new Dictionary<string, AutocompleteSearchField.AutocompleteSearchField>();
    private readonly Dictionary<string, string> pendingCorrectiveValues = new Dictionary<string, string>();

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
    private const string CustomVeinsKey = "customVeins";
    private const string DynamicNormalBodyKey = "dynamicNormalBody";
    private const string DynamicNormalFlexingKey = "dynamicNormalFlexing";
    private string autoAssignedVeinsTexturePath;
    private UltiPawVersion lastParentVersionForVeins;
    private bool isRestoringFromVersionState;
    private const float BlendshapeNameColumn = 0.35f;
    private const float BlendshapeValueColumn = 0.16f;
    private const float BlendshapeSliderColumn = 0.10f;
    private const float BlendshapeDefaultColumn = 0.10f;
    
    public CreatorModeModule(UltiPawEditor editor)
    {
        this.editor = editor;
        this.networkService = new NetworkService();
        this.fileManagerService = new FileManagerService();
    }

    public void Initialize()
    {
        blendshapeList = new ReorderableList(editor.serializedObject, editor.customBlendshapesForCreatorProp, true, true, false, true);
        
        blendshapeList.drawHeaderCallback = (Rect rect) =>
        {
            float nameWidth = rect.width * BlendshapeNameColumn;
            float defaultValWidth = rect.width * BlendshapeValueColumn;
            float sliderWidth = rect.width * BlendshapeSliderColumn;
            float isDefaultWidth = rect.width * BlendshapeDefaultColumn;
            float correctiveWidth = rect.width - nameWidth - defaultValWidth - sliderWidth - isDefaultWidth;
            
            EditorGUI.LabelField(new Rect(rect.x, rect.y, nameWidth, rect.height), "Blendshape Name");
            EditorGUI.LabelField(new Rect(rect.x + nameWidth, rect.y, defaultValWidth, rect.height), "Value");
            EditorGUI.LabelField(new Rect(rect.x + nameWidth + defaultValWidth, rect.y, sliderWidth, rect.height), "Slider");
            EditorGUI.LabelField(new Rect(rect.x + nameWidth + defaultValWidth + sliderWidth, rect.y, isDefaultWidth, rect.height), "Default");
            EditorGUI.LabelField(new Rect(rect.x + nameWidth + defaultValWidth + sliderWidth + isDefaultWidth, rect.y, correctiveWidth, rect.height), "Correctives");
        };
        blendshapeList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            var element = blendshapeList.serializedProperty.GetArrayElementAtIndex(index);
            rect.y += 2;
            float spacing = 5f;
            float nameWidth = rect.width * BlendshapeNameColumn - spacing;
            float defaultValWidth = rect.width * BlendshapeValueColumn - spacing;
            float sliderWidth = rect.width * BlendshapeSliderColumn - spacing;
            float isDefaultWidth = rect.width * BlendshapeDefaultColumn - spacing;
            float correctiveWidth = rect.width - nameWidth - defaultValWidth - sliderWidth - isDefaultWidth - spacing * 4f;
            
            var nameProp = element.FindPropertyRelative("name");
            var defaultValueProp = element.FindPropertyRelative("defaultValue");
            var isSliderProp = element.FindPropertyRelative("isSlider");
            var isSliderDefaultProp = element.FindPropertyRelative("isSliderDefault");
            
            float currentX = rect.x;
            EditorGUI.PropertyField(new Rect(currentX, rect.y, nameWidth, EditorGUIUtility.singleLineHeight), nameProp, GUIContent.none);
            currentX += nameWidth + spacing;
            EditorGUI.PropertyField(new Rect(currentX, rect.y, defaultValWidth, EditorGUIUtility.singleLineHeight), defaultValueProp, GUIContent.none);
            currentX += defaultValWidth + spacing;
            
            isSliderProp.boolValue = EditorGUI.Toggle(new Rect(currentX + (sliderWidth - 16f) / 2f, rect.y, 16f, EditorGUIUtility.singleLineHeight), isSliderProp.boolValue);
            currentX += sliderWidth + spacing;

            using (new EditorGUI.DisabledScope(!isSliderProp.boolValue))
            {
                isSliderDefaultProp.boolValue = EditorGUI.Toggle(new Rect(currentX + (isDefaultWidth - 16f) / 2f, rect.y, 16f, EditorGUIUtility.singleLineHeight), isSliderDefaultProp.boolValue);
                if (!isSliderProp.boolValue) isSliderDefaultProp.boolValue = false;
            }
            currentX += isDefaultWidth + spacing;

            if (GUI.Button(new Rect(currentX, rect.y, correctiveWidth, EditorGUIUtility.singleLineHeight), "add fixes"))
            {
                var correctiveProp = element.FindPropertyRelative("correctiveBlendshapes");
                AddCorrectivePair(correctiveProp);
                editor.serializedObject.ApplyModifiedProperties();
            }
        };
        
        // Initialize the autocomplete search field for blendshapes
        blendshapeSearchField = new AutocompleteSearchField.AutocompleteSearchField();
        blendshapeSearchField.onInputChanged = OnBlendshapeSearchInputChanged;
        blendshapeSearchField.onConfirm = OnBlendshapeSearchConfirm;
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
            
            DrawParentVersionDropdown();
            
            EditorGUILayout.PropertyField(editor.customFbxForCreatorProp, new GUIContent("Custom FBX (Transformed)"));
            EditorGUILayout.PropertyField(editor.ultipawAvatarForCreatorProp, new GUIContent("UltiPaw Avatar (Transformed)"));
            EditorGUILayout.PropertyField(editor.avatarLogicPrefabProp, new GUIContent("Avatar Logic Prefab"));
            DrawCustomVeinsSection();
            DrawDynamicNormalsSection();
            
            EditorGUILayout.Space();
            
            // vertical group with helpbox style
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            blendshapeList.DoLayoutList();
            
            // Display inline blendshape search field
            if (blendshapeSearchField != null)
            {
                EditorGUILayout.LabelField("Search and Add Blendshapes:", EditorStyles.miniBoldLabel);
                blendshapeSearchField.OnGUI();
            }

            DrawBlendshapeCorrectivesSection();
            
            EditorGUILayout.EndVertical();  
            
            EditorGUILayout.Space();

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
            if (editor.includeCustomVeinsForCreatorProp.boolValue && editor.customVeinsNormalMapProp.objectReferenceValue == null)
            {
                canSubmit = false;
            }
            
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
    

    private void OnBlendshapeSearchInputChanged(string searchString)
    {
        blendshapeSearchField.ClearResults();
        
        if (string.IsNullOrEmpty(searchString))
            return;
        
        var allBlendshapes = GetAllBlendshapeNamesFromCustomFbx();
        var existingBlendshapeNames = editor.ultiPawTarget.customBlendshapesForCreator.Select(e => e.name);
        var availableBlendshapes = allBlendshapes.Except(existingBlendshapeNames).OrderBy(s => s);

        // Filter blendshapes that contain the search string (case-insensitive)
        foreach (var shapeName in availableBlendshapes)
        {
            if (shapeName.IndexOf(searchString, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                blendshapeSearchField.AddResult(shapeName);
            }
        }
    }

    private void OnBlendshapeSearchConfirm(string selectedBlendshape)
    {
        if (string.IsNullOrEmpty(selectedBlendshape))
            return;

        // Add the selected blendshape to the list
        var prop = editor.customBlendshapesForCreatorProp;
        int index = prop.arraySize;
        prop.arraySize++;
        var element = prop.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("name").stringValue = selectedBlendshape;
        element.FindPropertyRelative("defaultValue").stringValue = "0";
        element.FindPropertyRelative("isSlider").boolValue = false;
        element.FindPropertyRelative("isSliderDefault").boolValue = false;
        element.FindPropertyRelative("correctiveBlendshapes").arraySize = 0;
        editor.serializedObject.ApplyModifiedProperties();

        // Clear the search field
        blendshapeSearchField.searchString = "";
        blendshapeSearchField.ClearResults();
    }

    private IEnumerable<string> GetAllBlendshapeNamesFromCustomFbx()
    {
        var fbxObject = editor.customFbxForCreatorProp.objectReferenceValue as GameObject;
        if (fbxObject == null) return Enumerable.Empty<string>();

        var smr = fbxObject.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null || smr.sharedMesh == null) return Enumerable.Empty<string>();

        return Enumerable.Range(0, smr.sharedMesh.blendShapeCount)
            .Select(i => smr.sharedMesh.GetBlendShapeName(i));
    }

    private void DrawBlendshapeCorrectivesSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Blendshape Correctives", EditorStyles.miniBoldLabel);

        var blendshapesProp = editor.customBlendshapesForCreatorProp;
        if (blendshapesProp == null || blendshapesProp.arraySize == 0)
        {
            EditorGUILayout.HelpBox("Add blendshapes above to create corrective links.", MessageType.None);
            return;
        }

        bool drewOne = false;
        for (int i = 0; i < blendshapesProp.arraySize; i++)
        {
            var blendshapeElement = blendshapesProp.GetArrayElementAtIndex(i);
            var correctiveProp = blendshapeElement.FindPropertyRelative("correctiveBlendshapes");
            if (correctiveProp == null || correctiveProp.arraySize == 0) continue;

            drewOne = true;
            string title = blendshapeElement.FindPropertyRelative("name").stringValue;
            if (string.IsNullOrWhiteSpace(title)) title = "(Unnamed Blendshape)";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (GUILayout.Button("Delete", GUILayout.Width(70f)))
            {
                correctiveProp.arraySize = 0;
                editor.serializedObject.ApplyModifiedProperties();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                continue;
            }
            EditorGUILayout.EndHorizontal();

            for (int j = 0; j < correctiveProp.arraySize; j++)
            {
                var item = correctiveProp.GetArrayElementAtIndex(j);
                var toFixProp = item.FindPropertyRelative("blendshapeToFix");
                var fixingProp = item.FindPropertyRelative("fixingBlendshape");
                string keyPrefix = $"corrective_{i}_{j}";
                float indentPadding = EditorGUI.indentLevel * 15f;
                float availableWidth = EditorGUIUtility.currentViewWidth - indentPadding - 110f;
                float fieldWidth = Mathf.Clamp((availableWidth - 28f - 4f) * 0.5f, 95f, 260f);

                EditorGUILayout.BeginHorizontal();
                DrawCorrectiveBlendshapeAutocompleteField(
                    toFixProp,
                    keyPrefix + "_toFix",
                    "blendshape to fix",
                    GUILayout.Width(fieldWidth)
                );
                GUILayout.Space(4f);
                DrawCorrectiveBlendshapeAutocompleteField(
                    fixingProp,
                    keyPrefix + "_fixing",
                    "fixing blendshape",
                    GUILayout.Width(fieldWidth)
                );
                if (GUILayout.Button("-", GUILayout.Width(24f)))
                {
                    correctiveProp.DeleteArrayElementAtIndex(j);
                    editor.serializedObject.ApplyModifiedProperties();
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add", GUILayout.Width(80f)))
            {
                AddCorrectivePair(correctiveProp);
                editor.serializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.EndVertical();
        }

        if (!drewOne)
        {
            EditorGUILayout.HelpBox("Use 'add corrective blendshapes' in the table to create entries.", MessageType.None);
        }

        EditorGUILayout.HelpBox(
            "The fixing blendshape activation will be proportional to the fixed blendshape and the blendshape needing correctives",
            MessageType.Info
        );
    }

    private void DrawCorrectiveBlendshapeAutocompleteField(
        SerializedProperty stringProp,
        string fieldKey,
        string label,
        params GUILayoutOption[] layout
    )
    {
        EditorGUILayout.BeginVertical(layout);
        var clippedMiniLabel = new GUIStyle(EditorStyles.miniLabel)
        {
            clipping = TextClipping.Clip,
            wordWrap = false
        };
        EditorGUILayout.LabelField(label, clippedMiniLabel, layout);

        var searchField = GetOrCreateCorrectiveSearchField(fieldKey);
        string currentValue = stringProp != null ? (stringProp.stringValue ?? string.Empty) : string.Empty;
        if (searchField.searchString != currentValue)
        {
            searchField.searchString = currentValue;
        }

        searchField.OnGUI();

        if (pendingCorrectiveValues.TryGetValue(fieldKey, out var pending))
        {
            if (stringProp != null) stringProp.stringValue = pending ?? string.Empty;
            pendingCorrectiveValues.Remove(fieldKey);
        }

        EditorGUILayout.EndVertical();
    }

    private AutocompleteSearchField.AutocompleteSearchField GetOrCreateCorrectiveSearchField(string fieldKey)
    {
        if (correctiveSearchFields.TryGetValue(fieldKey, out var existing))
        {
            return existing;
        }

        var field = new AutocompleteSearchField.AutocompleteSearchField();
        field.onInputChanged = input =>
        {
            pendingCorrectiveValues[fieldKey] = input ?? string.Empty;
            field.ClearResults();
            if (string.IsNullOrWhiteSpace(input)) return;

            foreach (var name in GetAllBlendshapeNamesFromCustomFbx()
                         .Where(n => !string.IsNullOrWhiteSpace(n))
                         .Distinct()
                         .OrderBy(n => n))
            {
                if (name.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    field.AddResult(name);
                }
            }
        };
        field.onConfirm = selected =>
        {
            pendingCorrectiveValues[fieldKey] = selected ?? string.Empty;
        };

        correctiveSearchFields[fieldKey] = field;
        return field;
    }

    private static void AddCorrectivePair(SerializedProperty correctiveProp, string blendshapeToFix = "", string fixingBlendshape = "")
    {
        if (correctiveProp == null) return;
        int i = correctiveProp.arraySize;
        correctiveProp.InsertArrayElementAtIndex(i);
        var row = correctiveProp.GetArrayElementAtIndex(i);
        row.FindPropertyRelative("blendshapeToFix").stringValue = blendshapeToFix ?? string.Empty;
        row.FindPropertyRelative("fixingBlendshape").stringValue = fixingBlendshape ?? string.Empty;
    }

    private void DrawParentVersionDropdown()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Parent Version:", GUILayout.Width(100));

        if (parentVersionDisplayOptions == null || parentVersionDisplayOptions.Count == 0)
        {
            EditorGUILayout.Popup(0, Array.Empty<string>());
            EditorGUILayout.EndHorizontal();
            HandleParentVersionChanged(null);
            return;
        }

        int currentParentPopupIndex = (selectedParentVersionIndex == -1) ? defaultParentIndex : selectedParentVersionIndex;

        EditorGUI.BeginChangeCheck();
        int newParentIndex = EditorGUILayout.Popup(currentParentPopupIndex, parentVersionDisplayOptions.ToArray());
        if (EditorGUI.EndChangeCheck())
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
        else if (selectedParentVersionIndex == -1 && defaultParentIndex != -1)
        {
            // Initialize on first draw only
            selectedParentVersionIndex = defaultParentIndex;
            selectedParentVersionObject = compatibleParentVersions[defaultParentIndex];
            SetDefaultVersionNumbers(selectedParentVersionObject);
        }
        EditorGUILayout.EndHorizontal();
        HandleParentVersionChanged(selectedParentVersionObject);
    }

    private void DrawCustomVeinsSection()
    {
        var includeProp = editor.includeCustomVeinsForCreatorProp;
        var textureProp = editor.customVeinsNormalMapProp;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUI.BeginChangeCheck();
        bool includeVeins = EditorGUILayout.ToggleLeft(new GUIContent("Include custom veins"), includeProp.boolValue);
        if (EditorGUI.EndChangeCheck())
        {
            includeProp.boolValue = includeVeins;
            if (!includeVeins)
            {
                textureProp.objectReferenceValue = null;
                autoAssignedVeinsTexturePath = null;
            }
        }

        if (includeProp.boolValue)
        {
            DrawVeinsTextureField(textureProp);
            if (textureProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign a normal map texture to include custom veins.", MessageType.Warning);
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawDynamicNormalsSection()
    {
        var includeBodyProp = editor.includeDynamicNormalsBodyForCreatorProp;
        var includeFlexingProp = editor.includeDynamicNormalsFlexingForCreatorProp;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Enable dynamic normals for body checkbox
        EditorGUI.BeginChangeCheck();
        bool includeBody = EditorGUILayout.ToggleLeft(new GUIContent("Enable dynamic normals for body"), includeBodyProp.boolValue);
        if (EditorGUI.EndChangeCheck())
        {
            includeBodyProp.boolValue = includeBody;
        }

        // Info bubble under first checkbox
        EditorGUILayout.HelpBox("Don't use if muscles normal is already baked in the mesh.\nWill apply to all blendshapes containing \"muscle\" in the name.", MessageType.Info);

        EditorGUILayout.Space(5);

        // Enable dynamic normals for flexings checkbox
        EditorGUI.BeginChangeCheck();
        bool includeFlexing = EditorGUILayout.ToggleLeft(new GUIContent("Enable dynamic normals for flexings"), includeFlexingProp.boolValue);
        if (EditorGUI.EndChangeCheck())
        {
            includeFlexingProp.boolValue = includeFlexing;
        }

        // Info bubble under second checkbox
        EditorGUILayout.HelpBox("Allows for the flexed muscles to be more visible.\nWill apply to all blendshapes containing \"flex\" in the name.", MessageType.Info);

        EditorGUILayout.EndVertical();
    }

    private void DrawVeinsTextureField(SerializedProperty textureProp)
    {
        UnityEngine.Object currentTexture = textureProp.objectReferenceValue;
        EditorGUI.indentLevel++;
        EditorGUIUtility.SetIconSize(new Vector2(64f, 64f));
        Texture2D newTexture = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Custom veins", "Normal map applied to the veins detail layer."), currentTexture, typeof(Texture2D), false, GUILayout.Height(64f));
        EditorGUIUtility.SetIconSize(Vector2.zero);
        EditorGUI.indentLevel--;

        if (!Equals(newTexture, currentTexture))
        {
            textureProp.objectReferenceValue = newTexture;
            string selectedPath = AssetDatabase.GetAssetPath(newTexture);
            string parentPath = GetVeinsTexturePathForVersion(selectedParentVersionObject);
            autoAssignedVeinsTexturePath = (!string.IsNullOrEmpty(selectedPath) && selectedPath == parentPath) ? selectedPath : null;
        }
    }

    private static string GetVeinsTexturePathForVersion(UltiPawVersion version)
    {
        if (version == null) return null;
        string folder = UltiPawUtils.GetVersionDataPath(version.version, version.defaultAviVersion);
        if (string.IsNullOrEmpty(folder)) return null;
        return Path.Combine(folder, "veins normal.png").Replace("\\", "/");
    }

    private bool ParentSupportsCustomVeins(UltiPawVersion version)
    {
        return version?.extraCustomization != null && version.extraCustomization.Contains(CustomVeinsKey);
    }

    private void HandleParentVersionChanged(UltiPawVersion newParent)
    {
        if (ReferenceEquals(lastParentVersionForVeins, newParent) && !isRestoringFromVersionState)
        {
            return;
        }

        lastParentVersionForVeins = newParent;

        var includeProp = editor.includeCustomVeinsForCreatorProp;
        var textureProp = editor.customVeinsNormalMapProp;

        // Only auto-populate if parent supports custom veins
        if (ParentSupportsCustomVeins(newParent))
        {
            // Auto-check the checkbox if not already checked (but only when not restoring from saved state)
            if (!isRestoringFromVersionState && !includeProp.boolValue)
            {
                includeProp.boolValue = true;
            }

            string parentTexturePath = GetVeinsTexturePathForVersion(newParent);
            if (!string.IsNullOrEmpty(parentTexturePath))
            {
                Texture2D parentTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(parentTexturePath);
                if (parentTexture != null)
                {
                    string currentTexturePath = AssetDatabase.GetAssetPath(textureProp.objectReferenceValue);
                    bool wasAutoAssigned = !string.IsNullOrEmpty(autoAssignedVeinsTexturePath) && autoAssignedVeinsTexturePath == currentTexturePath;

                    // Auto-assign the texture if empty or was previously auto-assigned
                    if (textureProp.objectReferenceValue == null || wasAutoAssigned)
                    {
                        textureProp.objectReferenceValue = parentTexture;
                        autoAssignedVeinsTexturePath = parentTexturePath;
                    }
                }
            }
        }
        // If parent doesn't support custom veins, don't uncheck or clear - just leave it as is
        
        // Auto-populate dynamic normals checkboxes from parent version
        var includeBodyProp = editor.includeDynamicNormalsBodyForCreatorProp;
        var includeFlexingProp = editor.includeDynamicNormalsFlexingForCreatorProp;
        
        if (newParent?.extraCustomization != null && !isRestoringFromVersionState)
        {
            bool parentHasBody = newParent.extraCustomization.Contains(DynamicNormalBodyKey);
            bool parentHasFlexing = newParent.extraCustomization.Contains(DynamicNormalFlexingKey);
            
            // Auto-check the checkboxes if parent has the features
            if (parentHasBody && !includeBodyProp.boolValue)
            {
                includeBodyProp.boolValue = true;
            }
            if (parentHasFlexing && !includeFlexingProp.boolValue)
            {
                includeFlexingProp.boolValue = true;
            }
        }
        
        // Auto-populate custom blendshapes from parent version
        if (newParent?.customBlendshapes != null && newParent.customBlendshapes.Length > 0 && !isRestoringFromVersionState)
        {
            editor.customBlendshapesForCreatorProp.ClearArray();
            for (int i = 0; i < newParent.customBlendshapes.Length; i++)
            {
                editor.customBlendshapesForCreatorProp.InsertArrayElementAtIndex(i);
                var element = editor.customBlendshapesForCreatorProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = newParent.customBlendshapes[i].name;
                element.FindPropertyRelative("defaultValue").stringValue = newParent.customBlendshapes[i].defaultValue;
                element.FindPropertyRelative("isSlider").boolValue = newParent.customBlendshapes[i].isSlider;
                element.FindPropertyRelative("isSliderDefault").boolValue = newParent.customBlendshapes[i].isSliderDefault;
                CopyCorrectivesToSerialized(element, newParent.customBlendshapes[i].correctiveBlendshapes);
            }
            editor.serializedObject.ApplyModifiedProperties();
        }
    }

    private static void CopyCorrectivesToSerialized(SerializedProperty blendshapeElement, CorrectiveBlendshapeEntry[] source)
    {
        var correctiveProp = blendshapeElement.FindPropertyRelative("correctiveBlendshapes");
        if (correctiveProp == null) return;

        correctiveProp.arraySize = 0;
        if (source == null || source.Length == 0) return;

        foreach (var corrective in source)
        {
            if (corrective == null) continue;
            AddCorrectivePair(correctiveProp, corrective.blendshapeToFix, corrective.fixingBlendshape);
        }
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
        bool shouldIncludeCustomVeins = editor.includeCustomVeinsForCreatorProp.boolValue;
        var customVeinsTexture = editor.customVeinsNormalMapProp.objectReferenceValue as Texture2D;
        bool shouldIncludeDynamicNormalsBody = editor.includeDynamicNormalsBodyForCreatorProp.boolValue;
        bool shouldIncludeDynamicNormalsFlexing = editor.includeDynamicNormalsFlexingForCreatorProp.boolValue;

        if (shouldIncludeCustomVeins)
        {
            if (customVeinsTexture == null)
                throw new Exception("Custom veins is enabled but no normal map texture is assigned.");
        }

        string currentFbxPath = new VersionActions(editor, networkService, fileManagerService).GetCurrentFBXPath();
        string originalFbxPath = currentFbxPath + FileManagerService.OriginalSuffix;
        
        // If .old backup doesn't exist, use the current FBX (first-time submission case)
        if (!File.Exists(originalFbxPath))
        {
            originalFbxPath = currentFbxPath;
            if (!File.Exists(originalFbxPath))
                throw new Exception("FBX file not found. Cannot proceed with version creation.");
        }

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
            selectedParentVersionObject,
            shouldIncludeCustomVeins,
            customVeinsTexture
        );

        EditorUtility.DisplayProgressBar("Preparing Build", "Calculating hashes and dependencies...", 0.5f);
        string binPath = UltiPawUtils.CombineUnityPath(UltiPawUtils.GetVersionDataPath(newVersionString, selectedParentVersionObject.defaultAviVersion), "ultipaw.bin");

        var extraCustomization = new List<string>();
        if (selectedParentVersionObject.extraCustomization != null)
        {
            extraCustomization.AddRange(selectedParentVersionObject.extraCustomization);
        }

        if (shouldIncludeCustomVeins)
        {
            if (!extraCustomization.Contains(CustomVeinsKey))
            {
                extraCustomization.Add(CustomVeinsKey);
            }
        }
        else
        {
            extraCustomization.RemoveAll(value => value == CustomVeinsKey);
        }

        // Handle dynamic normals for body
        if (shouldIncludeDynamicNormalsBody)
        {
            if (!extraCustomization.Contains(DynamicNormalBodyKey))
            {
                extraCustomization.Add(DynamicNormalBodyKey);
            }
        }
        else
        {
            extraCustomization.RemoveAll(value => value == DynamicNormalBodyKey);
        }

        // Handle dynamic normals for flexing
        if (shouldIncludeDynamicNormalsFlexing)
        {
            if (!extraCustomization.Contains(DynamicNormalFlexingKey))
            {
                extraCustomization.Add(DynamicNormalFlexingKey);
            }
        }
        else
        {
            extraCustomization.RemoveAll(value => value == DynamicNormalFlexingKey);
        }

        string customVeinsAssetPath = shouldIncludeCustomVeins ? AssetDatabase.GetAssetPath(customVeinsTexture) : null;

        // Convert CreatorBlendshapeEntry list to CustomBlendshapeEntry array
        var customBlendshapeEntries = editor.ultiPawTarget.customBlendshapesForCreator
            .Select(entry =>
            {
                var corrective = (entry.correctiveBlendshapes ?? new List<CreatorCorrectiveBlendshapeEntry>())
                    .Where(c => c != null
                                && !string.IsNullOrWhiteSpace(c.blendshapeToFix)
                                && !string.IsNullOrWhiteSpace(c.fixingBlendshape))
                    .Select(c => new CorrectiveBlendshapeEntry {
                        blendshapeToFix = c.blendshapeToFix,
                        fixingBlendshape = c.fixingBlendshape
                    })
                    .ToArray();

                return new CustomBlendshapeEntry {
                    name = entry.name,
                    defaultValue = entry.defaultValue,
                    isSlider = entry.isSlider,
                    isSliderDefault = entry.isSliderDefault,
                    correctiveBlendshapes = corrective.Length > 0 ? corrective : null
                };
            })
            .ToArray();

        var metadata = new UltiPawVersion {
            version = newVersionString,
            scope = newVersionScope,
            changelog = newChangelog,
            defaultAviVersion = selectedParentVersionObject.defaultAviVersion,
            parentVersion = selectedParentVersionObject?.version,
            dependencies = fileManagerService.FindPrefabDependencies(logicPrefab),
            customBlendshapes = customBlendshapeEntries,
            extraCustomization = extraCustomization.Count > 0 ? extraCustomization.Distinct().ToArray() : null,
            includeCustomVeins = shouldIncludeCustomVeins ? true : (bool?)null,
            customVeinsTexturePath = customVeinsAssetPath,
            includeDynamicNormalsBody = shouldIncludeDynamicNormalsBody ? true : (bool?)null,
            includeDynamicNormalsFlexing = shouldIncludeDynamicNormalsFlexing ? true : (bool?)null,
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

    public void RemoveUnsubmittedVersion(UltiPawVersion versionToRemove)
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
        isRestoringFromVersionState = true;
        try
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
                    SetDefaultVersionNumbers(selectedParentVersionObject);
                }
                else
                {
                    selectedParentVersionIndex = -1;
                    selectedParentVersionObject = null;
                    SetDefaultVersionNumbers(null);
                }
            }
            else
            {
                selectedParentVersionIndex = -1;
                selectedParentVersionObject = null;
                SetDefaultVersionNumbers(null);
            }

            editor.customFbxForCreatorProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ver.customFbxPath);
            editor.ultipawAvatarForCreatorProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Avatar>(ver.ultipawAvatarPath);
            editor.avatarLogicPrefabProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ver.logicPrefabPath);

            editor.customBlendshapesForCreatorProp.ClearArray();
            if (ver.customBlendshapes != null)
            {
                for (int i = 0; i < ver.customBlendshapes.Length; i++)
                {
                    editor.customBlendshapesForCreatorProp.InsertArrayElementAtIndex(i);
                    var element = editor.customBlendshapesForCreatorProp.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("name").stringValue = ver.customBlendshapes[i].name;
                    element.FindPropertyRelative("defaultValue").stringValue = ver.customBlendshapes[i].defaultValue;
                    element.FindPropertyRelative("isSlider").boolValue = ver.customBlendshapes[i].isSlider;
                    element.FindPropertyRelative("isSliderDefault").boolValue = ver.customBlendshapes[i].isSliderDefault;
                    CopyCorrectivesToSerialized(element, ver.customBlendshapes[i].correctiveBlendshapes);
                }
            }

            bool includeCustomVeins = ver.includeCustomVeins ?? (ver.extraCustomization != null && ver.extraCustomization.Contains(CustomVeinsKey));
            editor.includeCustomVeinsForCreatorProp.boolValue = includeCustomVeins;

            if (!string.IsNullOrEmpty(ver.customVeinsTexturePath))
            {
                editor.customVeinsNormalMapProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ver.customVeinsTexturePath);
            }
            else
            {
                editor.customVeinsNormalMapProp.objectReferenceValue = null;
            }

            string assignedPath = AssetDatabase.GetAssetPath(editor.customVeinsNormalMapProp.objectReferenceValue);
            string parentPath = GetVeinsTexturePathForVersion(selectedParentVersionObject);
            autoAssignedVeinsTexturePath = (!string.IsNullOrEmpty(assignedPath) && assignedPath == parentPath) ? assignedPath : null;
            lastParentVersionForVeins = selectedParentVersionObject;

            editor.serializedObject.ApplyModifiedProperties();
            editor.Repaint();
        }
        finally
        {
            isRestoringFromVersionState = false;
        }
    }

    private IEnumerator BuildAndApplyLocalVersionCoroutine()
    {
        editor.isSubmitting = true;
        editor.submitError = "";
        editor.warningsModule.Clear();
        editor.Repaint();

        (UltiPawVersion metadata, string zipPath) buildResult = default;
        Exception buildError = null;

        try
        {
            try { buildResult = BuildNewVersion(); }
            catch (Exception ex) { buildError = ex; }

            if (buildError != null)
            {
                editor.submitError = buildError.Message;
                editor.warningsModule.AddWarning(buildError.Message, MessageType.Error, "Test Build Failed");
                UltiPawLogger.LogError($"[CreatorMode] Test Build failed: {buildError}");
            }
            else
            {
                SaveUnsubmittedVersion(buildResult.metadata);
                var versionActions = new VersionActions(editor, networkService, fileManagerService);
                
                AssetDatabase.Refresh();
                while(EditorApplication.isCompiling || EditorApplication.isUpdating) { yield return null; }

                // Apply the newly built version
                yield return versionActions.ApplyOrResetCoroutine(buildResult.metadata, false);
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(buildResult.zipPath) && File.Exists(buildResult.zipPath))
            {
                File.Delete(buildResult.zipPath);
            }
            
            editor.isSubmitting = false;
            EditorUtility.ClearProgressBar();
            editor.Repaint();
        }

        if (buildError == null)
        {
            EditorUtility.DisplayDialog("Test Build Complete", $"Version {buildResult.metadata.version} has been built and applied locally. You can find it in the version list.", "OK");
        }
    }
    
    public IEnumerator UploadUnsubmittedVersionCoroutine(UltiPawVersion unsubmittedVersion)
    {
        editor.isSubmitting = true;
        editor.submitError = "";
        editor.warningsModule.Clear();
        editor.Repaint();

        string zipPath = null;
        Exception error = null;
        System.Threading.Tasks.Task<(bool success, string response, string error)> uploadTask = null;
        string uploadUrl = null;

        try
        {
            // Load the assets from the stored paths
            var customFbxGO = AssetDatabase.LoadAssetAtPath<GameObject>(unsubmittedVersion.customFbxPath);
            var ultipawAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(unsubmittedVersion.ultipawAvatarPath);
            var logicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(unsubmittedVersion.logicPrefabPath);
            var customVeinsTexture = !string.IsNullOrEmpty(unsubmittedVersion.customVeinsTexturePath) 
                ? AssetDatabase.LoadAssetAtPath<Texture2D>(unsubmittedVersion.customVeinsTexturePath) 
                : null;

            string currentFbxPath = new VersionActions(editor, networkService, fileManagerService).GetCurrentFBXPath();
            string originalFbxPath = currentFbxPath + FileManagerService.OriginalSuffix;
            
            // If .old backup doesn't exist, use the current FBX (first-time submission case)
            if (!File.Exists(originalFbxPath))
            {
                originalFbxPath = currentFbxPath;
                if (!File.Exists(originalFbxPath))
                    throw new Exception("FBX file not found. Cannot proceed with version creation.");
            }

            // Get parent version
            var parentVersion = editor.serverVersions.FirstOrDefault(v => v.version == unsubmittedVersion.parentVersion);
            if (parentVersion == null)
                throw new Exception("Parent version not found.");

            EditorUtility.DisplayProgressBar("Preparing Upload", "Creating version package...", 0.3f);

            // Re-create the zip from existing files
            zipPath = fileManagerService.CreateVersionPackageForUpload(
                unsubmittedVersion.version,
                unsubmittedVersion.defaultAviVersion,
                originalFbxPath,
                customFbxGO,
                ultipawAvatar,
                logicPrefab,
                parentVersion,
                unsubmittedVersion.includeCustomVeins ?? false,
                customVeinsTexture
            );

            EditorUtility.DisplayProgressBar("Uploading", "Sending package to server...", 0.7f);
            string metadataJson = JsonConvert.SerializeObject(unsubmittedVersion, new StringEnumConverter());
            uploadUrl = $"{UltiPawUtils.getApiUrl()}{UltiPawUtils.NEW_VERSION_ENDPOINT}?t={editor.authToken}";
            uploadTask = networkService.SubmitNewVersionAsync(uploadUrl, editor.authToken, zipPath, metadataJson);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (uploadTask != null)
        {
            while (!uploadTask.IsCompleted) { yield return null; }
        }

        bool uploadSucceeded = false;
        try
        {
            if (error != null) throw error;

            if (uploadTask != null)
            {
                var (success, response, uploadError) = uploadTask.Result;
                if (!success) throw new Exception(uploadError);

                uploadSucceeded = true;
                RemoveUnsubmittedVersion(unsubmittedVersion);
                new VersionActions(editor, networkService, fileManagerService).StartVersionFetch();
            }
        }
        catch (Exception ex)
        {
            editor.submitError = ex.Message;
            editor.warningsModule.AddWarning(ex.Message, MessageType.Error, "Upload Failed");
            UltiPawLogger.LogError($"[CreatorMode] Upload failed: {ex}, url: {uploadUrl}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
                File.Delete(zipPath);
            
            editor.isSubmitting = false;
            editor.Repaint();
        }

        if (uploadSucceeded)
        {
            EditorUtility.DisplayDialog("Upload Successful", $"UltiPaw version {unsubmittedVersion.version} has been uploaded.", "OK");
        }
    }

    private IEnumerator SubmitNewVersionCoroutine()
    {
        editor.isSubmitting = true;
        editor.submitError = "";
        editor.warningsModule.Clear();
        editor.Repaint();

        (UltiPawVersion metadata, string zipPath) buildResult = default;
        Exception error = null;
        System.Threading.Tasks.Task<(bool success, string response, string error)> uploadTask = null;
        string uploadUrl = null;
            
        try
        {
            buildResult = BuildNewVersion();
            EditorUtility.DisplayProgressBar("Uploading", "Sending package to server...", 0.8f);
            string metadataJson = JsonConvert.SerializeObject(buildResult.metadata, new StringEnumConverter());
            uploadUrl = $"{UltiPawUtils.getApiUrl()}{UltiPawUtils.NEW_VERSION_ENDPOINT}?t={editor.authToken}";
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

        bool submissionSucceeded = false;
        try
        {
            if (error != null) throw error;

            if (uploadTask != null)
            {
                var (success, response, uploadError) = uploadTask.Result;
                if (!success) throw new Exception(uploadError);

                submissionSucceeded = true;
                RemoveUnsubmittedVersion(buildResult.metadata);
                new VersionActions(editor, networkService, fileManagerService).StartVersionFetch();
            }
        }
        catch (Exception ex)
        {
            editor.submitError = ex.Message;
            editor.warningsModule.AddWarning(ex.Message, MessageType.Error, "Submission Failed");
            UltiPawLogger.LogError($"[CreatorMode] Submission failed: {ex}, url: {uploadUrl}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (!string.IsNullOrEmpty(buildResult.zipPath) && File.Exists(buildResult.zipPath))
                File.Delete(buildResult.zipPath);
            
            editor.isSubmitting = false;
            editor.Repaint();
        }

        if (submissionSucceeded)
        {
            EditorUtility.DisplayDialog("Upload Successful", $"New UltiPaw version {buildResult.metadata.version} has been uploaded.", "OK");
        }
    }
}
#endif


