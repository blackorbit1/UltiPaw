#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class MaterialService
{
    private readonly Transform avatarRoot;
    
    // Static list of supported shaders

    private static readonly List<string> SupportedShaders = new List<string>
    {
        "Standard",
        "Poiyomi Toon",
        "Poiyomi Pro",
        "Mochie/Uber",
        "Mochie/Standard",
        "VRChat/Mobile/Standard Lite",
        "VRChat/Mobile/Toon Standard",
    };

    private static readonly string[] DetailNormalKeywordCandidates =
    {
        "_DETAIL_NORMAL_ON",
        "_DETAIL_NORMALMAP_ON",
        "_DETAIL_NORMALMAP",
        "_DETAILNORMALMAP",
        "_DetailNormalMap",
        "_DETAIL_MULX2",
        "DETAIL_NORMALS",
        "POI_DETAIL_NORMALS_ON",
        "_DETAIL",
        "USE_DETAIL_MAPS",
        "USE_NORMAL_MAPS",
        "_NORMALMAP",
        "USE_DETAIL_MAPS",
        "USE_NORMAL_MAPS",
        "DETAIL_NORMALS_ON",
    };

    private static readonly string[] DetailNormalToggleProperties =
    {
        "_UseDetailNormalMap",
        "_DetailNormalToggle",
        "_DetailNormalEnable",
        "_DetailNormalEnabled",
        "_DetailNormalsEnable",
        "_DetailNormalsEnabled",
        "_EnableDetailNormal",
        "_EnableDetailNormals",
        "_EnableDetailNormalMap",
        "_DetailNormal",
        "_DetailEnabled",
        "_DetailNormals",
        "_DetailNormToggle",
        "_DetailNormEnabled",
        "_DetailNormalsToggle",
        "_DetailNormalsToggleOn",
        "_N_DETAIL_ENABLE",
        "USE_DETAIL_MAPS",
        "USE_NORMAL_MAPS",
        "_NORMALMAP",
        "USE_DETAIL_MAPS",
        "USE_NORMAL_MAPS",
        "DETAIL_NORMALS_ON",
    };

    private static readonly string[] DetailNormalStrengthProperties =
    {
        "_DetailNormalMapScale",
        "_DetailNormalStrength",
        "_DetailNormalsStrength",
        "_DetailNormalsIntensity",
        "_DetailNormalFactor",
        "_DetailNormalMultiplier",
        "_DetailBumpScale",
        "_DetailBumpStrength",
        "_DetailBumpIntensity",
        "_DetailNormalPower",
        "_DetailNormalWeight"
    };

    private static readonly string[] DetailNormalTextureProperties =
    {
        "_DetailNormalMap",
        "_DetailNormalsTex",
        "_DetailBumpMap",
        "_DetailNormalTex"
    };

    public MaterialService(Transform avatarRoot)
    {
        this.avatarRoot = avatarRoot; 
    }

    /// <summary>
    /// Gets the shader name from the specified material slot (e.g., "Body")
    /// </summary>
    public string GetShader(string materialSlot)
    {
        var material = GetMaterialForSlot(materialSlot);
        if (material == null || material.shader == null)
        {
            return null;
        }
        
        return material.shader.name;
    }

    /// <summary>
    /// Checks if a shader is supported
    /// </summary>
    public bool IsShaderSupported(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName))
            return false;

        // Check if the shader name contains any of the supported shader keywords
        string lowerShaderName = shaderName.ToLowerInvariant();
        
        // Check for Poiyomi variants (case-insensitive contains)
        if (lowerShaderName.Contains("poiyomi"))
            return true;
        
        // Check for Mochie variants (case-insensitive contains)
        if (lowerShaderName.Contains("mochie"))
            return true;
        
        // Check for VRChat variants (case-insensitive contains)
        if (lowerShaderName.Contains("vrchat"))
            return true;
        
        // Check for the unity Standard shader (use equals to avoid match with other shaders)
        if (lowerShaderName.Equals("standard"))
            return true;
        
        return false;
    }

    /// <summary>
    /// Returns the list of supported shaders
    /// </summary>
    public List<string> GetSupportedShadersList()
    {
        return new List<string>(SupportedShaders);
    }

    /// <summary>
    /// Sets the detail normal map texture for the specified material slot
    /// </summary>
    public bool SetDetailNormalMap(string materialSlot, string filePath)
    {
        var smr = GetSkinnedMeshRendererForSlot(materialSlot);
        if (smr == null) return false;

        var material = smr.sharedMaterial;
        if (material == null)
        {
            UltiPawLogger.LogError($"[MaterialService] SkinnedMeshRenderer '{materialSlot}' has no material assigned");
            return false;
        }

        // Check if material is locked and ask user if they want to unlock it
        if (IsMaterialLocked(material))
        {
            bool shouldUnlock = EditorUtility.DisplayDialog(
                "Material Shader is Locked",
                $"The material on '{materialSlot}' has a locked shader. Custom veins cannot be applied to locked shaders.\n\n" +
                "Would you like to unlock the material to apply custom veins?",
                "Unlock and Apply",
                "Don't apply"
            );

            if (shouldUnlock)
            {
                if (!UnlockMaterial(material))
                {
                    UltiPawLogger.LogError("[MaterialService] Failed to unlock material, cannot apply custom veins");
                    return false;
                }
                UltiPawLogger.Log("[MaterialService] Material unlocked successfully");
            }
            else
            {
                UltiPawLogger.Log("[MaterialService] User cancelled custom veins application due to locked shader");
                return false;
            }
        }

        if (!IsShaderSupported(material.shader.name))
        {
            UltiPawLogger.LogError($"[MaterialService] Shader '{material.shader.name}' is not supported");
            return false;
        }

        // Load the texture from the file path
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);
        if (texture == null)
        {
            UltiPawLogger.LogError($"[MaterialService] Could not load texture from path: {filePath}");
            return false;
        }

        // Set the detail normal map property
        PrepareMaterialEdit(smr, material, "UltiPaw Apply Detail Normal Map");
        if (!TrySetDetailNormalTexture(material, texture))
        {
            UltiPawLogger.LogError("[MaterialService] Unable to find detail normal texture property on material");
            return false;
        }
        EnableDetailNormalFeatures(material);
        FinalizeMaterialEdit(smr, material);
        AssetDatabase.SaveAssets(); // Force save to disk
        AssetDatabase.Refresh(); // Force asset refresh

        UltiPawLogger.Log($"[MaterialService] Set detail normal map on {materialSlot} material to: {filePath}");
        return true;
    }

    /// <summary>
    /// Sets the detail normal map opacity (scale) for the specified material slot
    /// </summary>
    public bool SetDetailNormalOpacity(string materialSlot, float opacity)
    {
        var smr = GetSkinnedMeshRendererForSlot(materialSlot);
        if (smr == null) return false;
        
        var material = smr.sharedMaterial;
        if (material == null)
        {
            UltiPawLogger.LogError($"[MaterialService] SkinnedMeshRenderer '{materialSlot}' has no material assigned");
            return false;
        }

        if (!IsShaderSupported(material.shader.name))
        {
            UltiPawLogger.LogError($"[MaterialService] Shader '{material.shader.name}' is not supported");
            return false;
        }

        // Set the detail normal map scale property
        PrepareMaterialEdit(smr, material, "UltiPaw Update Detail Normal Opacity");
        EnableDetailNormalFeatures(material);
        ApplyDetailNormalOpacity(material, opacity);
        FinalizeMaterialEdit(smr, material);
        AssetDatabase.SaveAssets(); // Force save to disk
        AssetDatabase.Refresh(); // Force asset refresh
        
        UltiPawLogger.Log($"[MaterialService] Set detail normal map opacity on {materialSlot} material to: {opacity}");
        return true;
    }

    /// <summary>
    /// Removes the detail normal map from the specified material slot
    /// </summary>
    public bool RemoveDetailNormalMap(string materialSlot)
    {
        var smr = GetSkinnedMeshRendererForSlot(materialSlot);
        if (smr == null) return false;
        
        var material = smr.sharedMaterial;
        if (material == null)
        {
            UltiPawLogger.LogError($"[MaterialService] SkinnedMeshRenderer '{materialSlot}' has no material assigned");
            return false;
        }

        if (!IsShaderSupported(material.shader.name))
        {
            UltiPawLogger.LogError($"[MaterialService] Shader '{material.shader.name}' is not supported");
            return false;
        }

        // Remove the detail normal map by setting it to null
        PrepareMaterialEdit(smr, material, "UltiPaw Remove Detail Normal Map");
        if (!TrySetDetailNormalTexture(material, null))
        {
            UltiPawLogger.LogError("[MaterialService] Unable to clear detail normal texture on material");
            return false;
        }
        ApplyDetailNormalOpacity(material, 0f);
        DisableDetailNormalFeatures(material);
        FinalizeMaterialEdit(smr, material);
        AssetDatabase.SaveAssets(); // Force save to disk
        AssetDatabase.Refresh(); // Force asset refresh
        
        UltiPawLogger.Log($"[MaterialService] Removed detail normal map from {materialSlot} material");
        return true;
    }

    /// <summary>
    /// Gets the SkinnedMeshRenderer from the specified slot (e.g., "Body")
    /// </summary>
    private SkinnedMeshRenderer GetSkinnedMeshRendererForSlot(string materialSlot)
    {
        if (avatarRoot == null)
        {
            UltiPawLogger.LogError("[MaterialService] Avatar root is null");
            return null;
        }

        // Find the SkinnedMeshRenderer with the matching name
        var smr = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(s => s.gameObject.name.Equals(materialSlot, System.StringComparison.OrdinalIgnoreCase));

        if (smr == null)
        {
            UltiPawLogger.LogError($"[MaterialService] Could not find SkinnedMeshRenderer with name: {materialSlot}");
            return null;
        }

        return smr;
    }

    /// <summary>
    /// Gets the material from the specified slot (e.g., "Body")
    /// </summary>
    private Material GetMaterialForSlot(string materialSlot)
    {
        var smr = GetSkinnedMeshRendererForSlot(materialSlot);
        if (smr == null) return null;

        // Get the shared material (the actual material asset, not an instance)
        if (smr.sharedMaterial == null)
        {
            UltiPawLogger.LogError($"[MaterialService] SkinnedMeshRenderer '{materialSlot}' has no material assigned");
            return null;
        }

        return smr.sharedMaterial;
    }

    private void EnableDetailNormalFeatures(Material material)
    {
        if (material == null)
        {
            return;
        }

        SetDetailNormalKeyword(material, true);

        foreach (var property in DetailNormalToggleProperties)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, 1f);
            }
        }
    }

    private void DisableDetailNormalFeatures(Material material)
    {
        if (material == null)
        {
            return;
        }

        SetDetailNormalKeyword(material, false);

        foreach (var property in DetailNormalToggleProperties)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, 0f);
            }
        }
    }

    private void ApplyDetailNormalOpacity(Material material, float opacity)
    {
        if (material == null)
        {
            return;
        }

        bool adjustedAny = false;

        foreach (var property in DetailNormalStrengthProperties)
        {
            if (!material.HasProperty(property))
            {
                continue;
            }

            float current = material.GetFloat(property);
            float target = Mathf.Clamp01(opacity);

            if (Mathf.Approximately(current, target))
            {
                float nudge = target < 0.5f ? Mathf.Min(target + 0.0001f, 1f) : Mathf.Max(target - 0.0001f, 0f);

                if (!Mathf.Approximately(nudge, target))
                {
                    material.SetFloat(property, nudge);
                }
                else
                {
                    material.SetFloat(property, target == 0f ? 0.0001f : Mathf.Clamp01(target - 0.0001f));
                }
            }

            material.SetFloat(property, target);
            adjustedAny = true;
        }

        if (!adjustedAny && material.HasProperty("_DetailNormalMapScale"))
        {
            // Fallback in case property list missed the active property
            float target = Mathf.Clamp01(opacity);
            float current = material.GetFloat("_DetailNormalMapScale");

            if (Mathf.Approximately(current, target))
            {
                float nudge = target < 0.5f ? Mathf.Min(target + 0.0001f, 1f) : Mathf.Max(target - 0.0001f, 0f);

                if (!Mathf.Approximately(nudge, target))
                {
                    material.SetFloat("_DetailNormalMapScale", nudge);
                }
                else
                {
                    material.SetFloat("_DetailNormalMapScale", target == 0f ? 0.0001f : Mathf.Clamp01(target - 0.0001f));
                }
            }

            material.SetFloat("_DetailNormalMapScale", target);
        }
    }

    private bool TrySetDetailNormalTexture(Material material, Texture texture)
    {
        if (material == null)
        {
            return false;
        }

        foreach (var property in DetailNormalTextureProperties)
        {
            if (!material.HasProperty(property))
            {
                continue;
            }

            material.SetTexture(property, texture);
            return true;
        }

        return false;
    }

    private void SetDetailNormalKeyword(Material material, bool enable)
    {
        if (material == null || material.shader == null)
        {
            return;
        }

        var shader = material.shader;
        var validKeywords = new List<(LocalKeyword keyword, string name)>();
        bool hasAnyKeyword = false;

        // Get all keyword names from the shader to avoid Unity error logging
        var shaderKeywordNames = shader.keywordSpace.keywordNames;

        foreach (var candidate in DetailNormalKeywordCandidates)
        {
            // Check if keyword exists in shader before constructing LocalKeyword
            // This avoids Unity's internal error logging
            bool keywordExists = false;
            foreach (var keywordName in shaderKeywordNames)
            {
                if (keywordName == candidate)
                {
                    keywordExists = true;
                    break;
                }
            }

            if (!keywordExists)
            {
                continue;
            }

            // Keyword exists, safe to construct LocalKeyword
            var localKeyword = new LocalKeyword(shader, candidate);
            if (localKeyword.isValid)
            {
                validKeywords.Add((localKeyword, candidate));
                hasAnyKeyword = true;
            }
        }

        if (hasAnyKeyword)
        {
            foreach (var (keyword, name) in validKeywords)
            {
                material.SetKeyword(keyword, enable);
            }
        }
        else if (enable)
        {
            // Only log error when trying to enable and no valid keyword was found
            UltiPawLogger.LogError($"[MaterialService] No valid detail normal keyword found in shader '{shader.name}'. Please report this shader for support.");
        }

#pragma warning disable CS0618
        foreach (var candidate in DetailNormalKeywordCandidates)
        {
            if (enable && validKeywords.Any(vk => vk.name == candidate))
            {
                continue;
            }

            try
            {
                material.DisableKeyword(candidate);
            }
            catch
            {
                // Expected - keyword doesn't exist, ignore
            }
        }
#pragma warning restore CS0618
    }
    private void PrepareMaterialEdit(SkinnedMeshRenderer smr, Material material, string undoLabel)
    {
        if (smr == null || material == null)
        {
            return;
        }

        Undo.RecordObject(material, undoLabel);
        Undo.RecordObject(smr, undoLabel);
    }

    private void ForceRendererRefresh(SkinnedMeshRenderer smr)
    {
        if (smr == null)
        {
            return;
        }

        var propertyBlock = new MaterialPropertyBlock();
        smr.GetPropertyBlock(propertyBlock);
        smr.SetPropertyBlock(propertyBlock);

        var materials = smr.sharedMaterials;
        if (materials != null && materials.Length > 0)
        {
            var tempMaterials = materials.ToArray();
            int swapIndex = System.Array.FindIndex(tempMaterials, mat => mat != null);

            if (swapIndex >= 0)
            {
                var original = tempMaterials[swapIndex];
                tempMaterials[swapIndex] = null;
                smr.sharedMaterials = tempMaterials;
                tempMaterials[swapIndex] = original;
                smr.sharedMaterials = tempMaterials;
            }
            else
            {
                smr.sharedMaterials = tempMaterials;
            }
        }
        else
        {
            smr.sharedMaterials = materials;
        }

        bool wasEnabled = smr.enabled;
        smr.enabled = false;
        smr.enabled = wasEnabled;

        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
    }
    private void FinalizeMaterialEdit(SkinnedMeshRenderer smr, Material material)
    {
        if (smr == null || material == null)
        {
            return;
        }

        EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(smr);

        if (PrefabUtility.IsPartOfPrefabInstance(smr))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
        }

        var sharedMaterials = smr.sharedMaterials;
        if (sharedMaterials != null)
        {
            smr.sharedMaterials = sharedMaterials.ToArray();
        }

        smr.UpdateGIMaterials();
        ForceRendererRefresh(smr);
    }

    /// <summary>
    /// Checks if a material's shader is locked
    /// </summary>
    public bool IsMaterialLocked(Material material)
    {
        if (material == null || material.shader == null)
        {
            return false;
        }

        // Check if shader name starts with "Hidden/" and has the original shader tag
        return material.shader.name.StartsWith("Hidden/", System.StringComparison.Ordinal) && !string.IsNullOrEmpty(material.GetTag("OriginalShader", false, ""));
    }

    /// <summary>
    /// Unlocks a locked material by restoring its original shader
    /// Based on the UnlockConcrete logic from ShaderOptimizer.cs with zero dependencies
    /// </summary>
    public bool UnlockMaterial(Material material)
    {
        if (material == null)
        {
            UltiPawLogger.LogError("[MaterialService] Material is null");
            return false;
        }

        Shader lockedShader = material.shader;

        // Check if shader is locked
        if (!lockedShader.name.StartsWith("Hidden/", System.StringComparison.Ordinal))
        {
            UltiPawLogger.LogWarning($"[MaterialService] Shader {lockedShader.name} is not locked");
            return true; // Not locked, so consider it a success
        }

        // Get original shader name from material tag
        string originalShaderName = material.GetTag("OriginalShader", false, string.Empty);
        if (string.IsNullOrEmpty(originalShaderName))
        {
            UltiPawLogger.LogError("[MaterialService] Original shader name not saved to material, could not unlock");
            return false;
        }

        // Try to find the original shader by exact name
        Shader originalShader = Shader.Find(originalShaderName);

        // If exact match not found, try fallback matching strategies
        if (originalShader == null)
        {
            UltiPawLogger.LogWarning($"[MaterialService] Original shader '{originalShaderName}' could not be found by exact match, trying fallback strategies...");

            // Strategy 1: Try to find by shader base name only (e.g., "Poiyomi Toon" from ".poiyomi/Poiyomi 8.1/Poiyomi Toon")
            originalShader = FindShaderByBaseName(originalShaderName);

            if (originalShader != null)
            {
                UltiPawLogger.Log($"[MaterialService] Found similar shader '{originalShader.name}' by base name matching");
            }
        }

        if (originalShader == null)
        {
            UltiPawLogger.LogError($"[MaterialService] Could not find any matching shader for '{originalShaderName}'");
            return false;
        }

        // Save render type and queue (they get reset when changing shaders)
        string renderType = material.GetTag("RenderType", false, "");
        int renderQueue = material.renderQueue;

        // Record undo
        Undo.RecordObject(material, "Unlock Material Shader");

        // Switch back to original shader
        material.shader = originalShader;

        // Restore render type and queue
        material.SetOverrideTag("RenderType", renderType);
        material.renderQueue = renderQueue;

        // Restore keywords
        string originalKeywords = material.GetTag("OriginalKeywords", false, string.Empty);
        if (!string.IsNullOrEmpty(originalKeywords))
        {
            material.shaderKeywords = originalKeywords.Split(' ');
        }

        // Mark as dirty
        EditorUtility.SetDirty(material);

        UltiPawLogger.Log($"[MaterialService] Successfully unlocked material shader from '{lockedShader.name}' to '{originalShader.name}'");
        return true;
    }

    /// <summary>
    /// Finds a shader by matching its base name (the part after the last '/')
    /// For example, finds ".poiyomi/Poiyomi Toon" when looking for ".poiyomi/Poiyomi 8.1/Poiyomi Toon"
    /// </summary>
    private Shader FindShaderByBaseName(string originalShaderName)
    {
        // Extract the base name (part after the last '/')
        string baseName = originalShaderName;
        int lastSlashIndex = originalShaderName.LastIndexOf('/');
        if (lastSlashIndex >= 0 && lastSlashIndex < originalShaderName.Length - 1)
        {
            baseName = originalShaderName.Substring(lastSlashIndex + 1);
        }

        if (string.IsNullOrEmpty(baseName))
        {
            return null;
        }

        // Get all shaders in the project
        ShaderVariantCollection.ShaderVariant[] allVariants = null;
        List<Shader> allShaders = new List<Shader>();

        // Use ShaderUtil to get all shader info
        ShaderInfo[] shaderInfos = ShaderUtil.GetAllShaderInfo();

        foreach (var shaderInfo in shaderInfos)
        {
            // Skip unsupported shaders
            if (!shaderInfo.supported)
                continue;

            // Skip hidden shaders (locked shaders)
            if (shaderInfo.name.StartsWith("Hidden/", System.StringComparison.Ordinal))
                continue;

            Shader shader = Shader.Find(shaderInfo.name);
            if (shader != null)
            {
                allShaders.Add(shader);
            }
        }

        // First pass: Try exact base name match
        foreach (Shader shader in allShaders)
        {
            string shaderBaseName = shader.name;
            int shaderLastSlashIndex = shader.name.LastIndexOf('/');
            if (shaderLastSlashIndex >= 0 && shaderLastSlashIndex < shader.name.Length - 1)
            {
                shaderBaseName = shader.name.Substring(shaderLastSlashIndex + 1);
            }

            if (shaderBaseName.Equals(baseName, System.StringComparison.OrdinalIgnoreCase))
            {
                return shader;
            }
        }

        // Second pass: Try to find closest match by comparing full names
        // This handles cases like "Poiyomi 8.1" vs "Poiyomi 9.0"
        Shader bestMatch = null;
        int bestMatchScore = int.MaxValue;

        foreach (Shader shader in allShaders)
        {
            // Calculate a simple distance score (lower is better)
            int score = CalculateShaderNameDistance(originalShaderName, shader.name);

            // Only consider it a match if the score is reasonable (less than half the original name length)
            if (score < originalShaderName.Length / 2 && score < bestMatchScore)
            {
                bestMatchScore = score;
                bestMatch = shader;
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Calculates a simple distance score between two shader names
    /// Lower score means better match
    /// </summary>
    private int CalculateShaderNameDistance(string name1, string name2)
    {
        // Normalize names for comparison (lowercase, no spaces)
        string normalized1 = name1.ToLowerInvariant().Replace(" ", "").Replace(".", "").Replace("/", "");
        string normalized2 = name2.ToLowerInvariant().Replace(" ", "").Replace(".", "").Replace("/", "");

        // Simple Levenshtein distance calculation
        int[,] distance = new int[normalized1.Length + 1, normalized2.Length + 1];

        for (int i = 0; i <= normalized1.Length; i++)
            distance[i, 0] = i;

        for (int j = 0; j <= normalized2.Length; j++)
            distance[0, j] = j;

        for (int i = 1; i <= normalized1.Length; i++)
        {
            for (int j = 1; j <= normalized2.Length; j++)
            {
                int cost = (normalized1[i - 1] == normalized2[j - 1]) ? 0 : 1;

                distance[i, j] = System.Math.Min(
                    System.Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[normalized1.Length, normalized2.Length];
    }

}
#endif
