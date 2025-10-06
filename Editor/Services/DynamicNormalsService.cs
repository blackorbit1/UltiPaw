#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UltiPawEditorUtils;

public class DynamicNormalsService
{
    private readonly UltiPawEditor editor;
    private List<string> activeBlendshapes = new List<string>();
    private Dictionary<SkinnedMeshRenderer, Mesh> originalMeshes = new Dictionary<SkinnedMeshRenderer, Mesh>();

    public DynamicNormalsService(UltiPawEditor editor)
    {
        this.editor = editor;
    }

    public List<string> GetActiveBlendshapes()
    {
        return new List<string>(activeBlendshapes);
    }

    public void FlushOriginalMeshes()
    {
        int count = originalMeshes.Count;
        originalMeshes.Clear();
        Debug.Log($"[DynamicNormals] Flushed {count} original mesh reference(s) from cache.");
    }

    public void Apply(bool includeBody = true, bool includeFlexing = true)
    {
        if (editor.ultiPawTarget == null) return;

        var root = editor.ultiPawTarget.transform.root;
        
        // Find the Body mesh
        var bodyMesh = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(s => s.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));

        if (bodyMesh?.sharedMesh == null)
        {
            Debug.LogWarning("[DynamicNormals] Body mesh not found.");
            activeBlendshapes.Clear();
            return;
        }

        // Get blendshapes based on includeBody and includeFlexing parameters
        var targetBlendshapes = new List<string>();
        for (int i = 0; i < bodyMesh.sharedMesh.blendShapeCount; i++)
        {
            string name = bodyMesh.sharedMesh.GetBlendShapeName(i);
            string nameLower = name.ToLowerInvariant();
            
            bool shouldInclude = false;
            if (includeBody && nameLower.Contains("muscle"))
            {
                shouldInclude = true;
            }
            if (includeFlexing && nameLower.Contains("flex"))
            {
                shouldInclude = true;
            }
            
            if (shouldInclude)
            {
                targetBlendshapes.Add(name);
            }
        }

        // Check if dynamic normals are already applied
        if (bodyMesh.sharedMesh.name.Contains("(DynamicNormals)"))
        {
            Debug.Log("[DynamicNormals] Dynamic normals are already applied to this mesh. Skipping re-application.");
            activeBlendshapes = targetBlendshapes;
            return;
        }

        if (targetBlendshapes.Count == 0)
        {
            Debug.LogWarning("[DynamicNormals] No blendshapes containing 'muscle' or 'flex' found on Body mesh.");
            activeBlendshapes.Clear();
            return;
        }

        try
        {
            // Store the original mesh reference BEFORE applying dynamic normals
            if (!originalMeshes.ContainsKey(bodyMesh))
            {
                originalMeshes[bodyMesh] = bodyMesh.sharedMesh;
                Debug.Log($"[DynamicNormals] Stored original mesh reference: {bodyMesh.sharedMesh.name}");
            }
            
            DynamicNormals.ForRoot(root)
                .limitToMeshes(new[] { bodyMesh })
                .applyToBlendshapes(targetBlendshapes)
                .enable(true)
                .Apply();

            activeBlendshapes = targetBlendshapes;
            Debug.Log($"[DynamicNormals] Applied to {targetBlendshapes.Count} blendshapes on Body mesh: {string.Join(", ", targetBlendshapes)}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DynamicNormals] Failed to apply: {ex.Message}\n{ex.StackTrace}");
            activeBlendshapes.Clear();
        }
    }

    public void Remove()
    {
        if (editor.ultiPawTarget == null) return;

        var root = editor.ultiPawTarget.transform.root;
        
        // Find the Body mesh
        var bodyMesh = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(s => s.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));

        if (bodyMesh?.sharedMesh == null)
        {
            activeBlendshapes.Clear();
            return;
        }

        Mesh originalMesh = null;

        // Try 1: Restore from our stored reference (for immediate toggling within same session)
        if (originalMeshes.TryGetValue(bodyMesh, out originalMesh))
        {
            bodyMesh.sharedMesh = originalMesh;
            Debug.Log($"[DynamicNormals] Restored original mesh from cached reference: {originalMesh.name}");
            
            // Remove from tracking dictionary since we've restored it
            originalMeshes.Remove(bodyMesh);
            activeBlendshapes.Clear();
            return;
        }

        // Try 2: Detect if current mesh is a DynamicNormals-modified mesh and find original in assets
        var currentMesh = bodyMesh.sharedMesh;
        if (currentMesh.name.Contains("(DynamicNormals)"))
        {
            Debug.Log($"[DynamicNormals] Detected modified mesh: {currentMesh.name}. Searching for original in assets...");
            
            // Extract the original mesh name
            string originalName = currentMesh.name.Replace(" (DynamicNormals)", "");
            
            // Get the base FBX path to ensure we load from the correct FBX
            string baseFbxPath = GetBaseFbxPath();
            
            // Search for the original mesh in assets
            originalMesh = FindOriginalMeshInAssets(originalName, baseFbxPath);
            
            if (originalMesh != null)
            {
                bodyMesh.sharedMesh = originalMesh;
                Debug.Log($"[DynamicNormals] Successfully restored original mesh from assets: {originalMesh.name}");
                EditorUtility.SetDirty(bodyMesh);
                activeBlendshapes.Clear();
                return;
            }
            else
            {
                Debug.LogError($"[DynamicNormals] Could not find original mesh '{originalName}' in assets. The mesh may need to be manually reassigned.");
            }
        }
        else
        {
            Debug.Log("[DynamicNormals] Current mesh does not appear to have DynamicNormals applied (no suffix found). Nothing to remove.");
        }

        activeBlendshapes.Clear();
    }

    private string GetBaseFbxPath()
    {
        // Get the base FBX path from the editor's baseFbxFiles property
        var baseFbxFilesProp = editor.serializedObject.FindProperty("baseFbxFiles");
        if (baseFbxFilesProp != null && baseFbxFilesProp.arraySize > 0)
        {
            var fbx = baseFbxFilesProp.GetArrayElementAtIndex(0).objectReferenceValue as GameObject;
            if (fbx != null)
            {
                string path = AssetDatabase.GetAssetPath(fbx);
                Debug.Log($"[DynamicNormals] Detected base FBX path: {path}");
                return path;
            }
        }
        return null;
    }

    private Mesh FindOriginalMeshInAssets(string meshName, string preferredFbxPath)
    {
        // Search for mesh assets with the given name
        string[] guids = AssetDatabase.FindAssets($"t:Mesh {meshName}");
        
        Mesh preferredMesh = null;
        Mesh fallbackMesh = null;
        
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            
            // Load all assets at this path (FBX files can contain multiple meshes)
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            
            foreach (var asset in assets)
            {
                if (asset is Mesh mesh && mesh.name == meshName)
                {
                    // Check if this mesh is from the preferred FBX
                    if (!string.IsNullOrEmpty(preferredFbxPath) && assetPath.Equals(preferredFbxPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log($"[DynamicNormals] Found original mesh in preferred FBX at: {assetPath}");
                        preferredMesh = mesh;
                        break;
                    }
                    
                    // Store as fallback if we don't find the preferred one
                    if (fallbackMesh == null)
                    {
                        fallbackMesh = mesh;
                        Debug.Log($"[DynamicNormals] Found fallback mesh at: {assetPath}");
                    }
                }
            }
            
            // If we found the preferred mesh, stop searching
            if (preferredMesh != null)
            {
                break;
            }
        }
        
        if (preferredMesh != null)
        {
            return preferredMesh;
        }
        else if (fallbackMesh != null)
        {
            Debug.LogWarning($"[DynamicNormals] Using fallback mesh (preferred FBX path not found or not specified).");
            return fallbackMesh;
        }
        
        Debug.LogWarning($"[DynamicNormals] Could not find mesh '{meshName}' in asset database.");
        return null;
    }
}
#endif
