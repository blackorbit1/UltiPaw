#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public class AdvancedModeModule
{
    private readonly UltiPawEditor editor;
    private bool isAdvancedMode = false;
    private bool advancedModeFoldout = true; // Opened by default when advanced mode is on
    private bool addArmatureToggle = false;
    private GameObject debugPrefabInstance;
    
    public AdvancedModeModule(UltiPawEditor editor)
    {
        this.editor = editor;
        // Ensure issue reporter subscribes according to current settings
        EditorIssueReporter.RefreshListener();
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

                // Logging checkbox (false by default via EditorPrefs)
                bool currentLogging = UltiPawLogger.IsEnabled();
                bool newLogging = EditorGUILayout.Toggle("Log in Console", currentLogging);
                if (newLogging != currentLogging)
                {
                    UltiPawLogger.SetEnabled(newLogging);
                }

                // Share issue logs (true by default) + minimum severity slider
                bool currentShare = EditorIssueReporter.ShareIssueLogs;
                bool newShare = EditorGUILayout.Toggle(new GUIContent("Share issue logs", "Send warnings/errors/exceptions to the UltiPaw server to help us improve."), currentShare);
                if (newShare != currentShare)
                {
                    EditorIssueReporter.ShareIssueLogs = newShare;
                    EditorIssueReporter.RefreshListener();
                }

                int currentMin = EditorIssueReporter.MinSeverityLevel; // 1..4
                int newMin = EditorGUILayout.IntSlider(new GUIContent("Min error severity", "1=Info, 2=Warning, 3=Error, 4=Exception"), currentMin, 1, 4);
                // Severity chips visualization
                DrawSeverityChips(newMin);
                
                // Test button for info-level logging
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button(new GUIContent("Test INFO log", "Emits an INFO level log to test the logging & reporting pipeline"), GUILayout.Width(120)))
                {
                    UltiPawLogger.Log("[UltiPaw] Test INFO log from AdvancedModeModule");
                }
                EditorGUILayout.EndHorizontal();

                if (newMin != currentMin)
                {
                    EditorIssueReporter.MinSeverityLevel = newMin;
                }
                
                // Debug Tools section
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Debug Tools", EditorStyles.boldLabel);
                
                bool previousArmatureToggle = addArmatureToggle;
                addArmatureToggle = EditorGUILayout.Toggle("Add armature toggle", addArmatureToggle);
                
                if (addArmatureToggle != previousArmatureToggle)
                {
                    OnArmatureToggleChanged();
                }
                
                EditorGUI.BeginDisabledGroup(!addArmatureToggle);
                if (GUILayout.Button("Create armature now"))
                {
                    CreateArmatureNow();
                }
                EditorGUI.EndDisabledGroup();

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
    
    private void OnArmatureToggleChanged()
    {
        if (addArmatureToggle)
        {
            // Place the debug prefab in the root of the avatar
            PlaceDebugPrefab();
            // Immediately generate the mesh (disabled) to prevent stripping
            GenerateArmatureMesh(false);
        }
        else
        {
            // Remove the debug prefab
            RemoveDebugPrefab();
        }
    }
    
    private void PlaceDebugPrefab()
    {
        if (editor.ultiPawTarget == null) return;
        
        // Remove existing debug prefab if it exists
        RemoveDebugPrefab();
        
        string prefabPath = "Packages/ultipaw/debug/debug.prefab";
        GameObject debugPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        
        if (debugPrefab == null)
        {
            UltiPawLogger.LogError($"Could not find debug prefab at {prefabPath}");
            return;
        }
        
        debugPrefabInstance = (GameObject)PrefabUtility.InstantiatePrefab(debugPrefab, editor.ultiPawTarget.transform);
        debugPrefabInstance.name = "debug";
        
        UltiPawLogger.Log("Debug prefab placed in avatar root");
    }
    
    private void RemoveDebugPrefab()
    {
        if (debugPrefabInstance != null)
        {
            Object.DestroyImmediate(debugPrefabInstance);
            debugPrefabInstance = null;
            UltiPawLogger.Log("Debug prefab removed from avatar root");
        }
        else
        {
            // Try to find existing debug object by name
            if (editor.ultiPawTarget != null)
            {
                Transform existingDebug = editor.ultiPawTarget.transform.Find("debug");
                if (existingDebug != null)
                {
                    Object.DestroyImmediate(existingDebug.gameObject);
                    UltiPawLogger.Log("Existing debug object removed from avatar root");
                }
            }
        }
    }
    
    private void CreateArmatureNow()
    {
        if (editor.ultiPawTarget == null || debugPrefabInstance == null) return;
        
        GenerateArmatureMesh(true); // Enable the mesh
    }
    
    public void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Ensure reporter is set according to latest preference whenever play mode changes
        EditorIssueReporter.RefreshListener();

        if (state == PlayModeStateChange.ExitingEditMode && addArmatureToggle)
        {
            // Generate the mesh right before leaving edit mode (before VRCFury processing)
            // This ensures the debug prefab has content and won't be stripped
            GenerateArmatureMesh(false); // Keep it disabled for play mode
        }
        else if (state == PlayModeStateChange.EnteredPlayMode && addArmatureToggle)
        {
            // The mesh should already exist, but we can ensure it's still disabled
            EnsureMeshState(false);
        }
    }
    
    private void GenerateArmatureMesh(bool enableMesh)
    {
        if (debugPrefabInstance == null) return;
        
        // Remove existing ArmatureDebugMesh if it exists
        Transform existingMesh = debugPrefabInstance.transform.Find("ArmatureDebugMesh");
        if (existingMesh != null)
        {
            Object.DestroyImmediate(existingMesh.gameObject);
        }
        
        // Generate new mesh using ArmatureMeshGenerator
        ArmatureMeshGenerator.Generate(editor.ultiPawTarget.gameObject, ArmatureMeshGenerator.MeshType.Pyramid);
        
        // Find the generated mesh and move it to the debug prefab
        Transform generatedMesh = editor.ultiPawTarget.transform.Find("ArmatureDebugMesh");
        if (generatedMesh != null)
        {
            generatedMesh.SetParent(debugPrefabInstance.transform, true);
            
            // Apply the debug material
            string materialPath = "Packages/ultipaw/debug/ArmatureDebugMaterial.mat";
            Material debugMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            
            if (debugMaterial != null)
            {
                SkinnedMeshRenderer smr = generatedMesh.GetComponent<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    smr.sharedMaterial = debugMaterial;
                }
            }
            else
            {
                UltiPawLogger.LogWarning($"Could not find ArmatureDebugMaterial at {materialPath}");
            }
            
            // Set the enabled state
            generatedMesh.gameObject.SetActive(enableMesh);
            
            UltiPawLogger.Log($"ArmatureDebugMesh generated and placed in debug prefab (enabled: {enableMesh})");
        }
        else
        {
            UltiPawLogger.LogError("Failed to generate ArmatureDebugMesh");
        }
    }
    
    private void EnsureMeshState(bool enableMesh)
    {
        if (debugPrefabInstance == null) return;
        
        Transform existingMesh = debugPrefabInstance.transform.Find("ArmatureDebugMesh");
        if (existingMesh != null)
        {
            existingMesh.gameObject.SetActive(enableMesh);
        }
    }

    // Renders four severity chips using shared EditorUIUtils style; chips below the selected threshold are grayed out.
    private void DrawSeverityChips(int minLevel)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUI.indentLevel * 15);
        DrawSeverityChip("INFO", new Color(0.20f, 0.60f, 1.00f), 1, minLevel);
        GUILayout.Space(6);
        DrawSeverityChip("WARN", EditorUIUtils.OrangeColor, 2, minLevel);
        GUILayout.Space(6);
        DrawSeverityChip("ERROR", new Color(1.00f, 0.20f, 0.20f), 3, minLevel);
        GUILayout.Space(6);
        DrawSeverityChip("FATAL", Color.black, 4, minLevel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSeverityChip(string label, Color activeTextColor, int level, int minLevel)
    {
        bool isActive = level >= minLevel;
        // Reuse chip style from VersionListDrawer: dark background with border, colored text
        Color bg = new Color(0.28f, 0.28f, 0.28f);
        Color border = new Color(0.46f, 0.46f, 0.46f);
        Color txt = isActive ? activeTextColor : new Color(0.65f, 0.65f, 0.65f);
        // Slightly dim inactive chips by also lightening the background
        Color bgInactive = new Color(0.32f, 0.32f, 0.32f);

        // Draw using shared util (rounded chip with border)
        EditorUIUtils.DrawChipLabel(label, isActive ? bg : bgInactive, txt, border, width: 70, height: 20, cornerRadius: 8f, borderWidth: 1.0f);
    }
}
#endif