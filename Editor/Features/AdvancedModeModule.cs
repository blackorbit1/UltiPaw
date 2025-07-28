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
            Debug.LogError($"Could not find debug prefab at {prefabPath}");
            return;
        }
        
        debugPrefabInstance = (GameObject)PrefabUtility.InstantiatePrefab(debugPrefab, editor.ultiPawTarget.transform);
        debugPrefabInstance.name = "debug";
        
        Debug.Log("Debug prefab placed in avatar root");
    }
    
    private void RemoveDebugPrefab()
    {
        if (debugPrefabInstance != null)
        {
            Object.DestroyImmediate(debugPrefabInstance);
            debugPrefabInstance = null;
            Debug.Log("Debug prefab removed from avatar root");
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
                    Debug.Log("Existing debug object removed from avatar root");
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
                Debug.LogWarning($"Could not find ArmatureDebugMaterial at {materialPath}");
            }
            
            // Set the enabled state
            generatedMesh.gameObject.SetActive(enableMesh);
            
            Debug.Log($"ArmatureDebugMesh generated and placed in debug prefab (enabled: {enableMesh})");
        }
        else
        {
            Debug.LogError("Failed to generate ArmatureDebugMesh");
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
}
#endif