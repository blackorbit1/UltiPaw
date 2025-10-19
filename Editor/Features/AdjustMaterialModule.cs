#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Provides contextual warnings and quick fixes for Poiyomi body materials.
public class AdjustMaterialModule
{
    private const string MaterialSlotName = "Body";
    private const string LightingModeProperty = "_LightingMode";
    private const string BumpMapProperty = "_BumpMap";
    private const int LightingModeTextureRampIndex = 0;
    private const int LightingModeRealisticIndex = 6;

    private readonly UltiPawEditor editor;

    private MaterialService materialService;
    private Transform cachedRoot;
    private bool foldout = true;

    public AdjustMaterialModule(UltiPawEditor editor)
    {
        this.editor = editor;
    }

    public void Draw()
    {
        if (!EnsureMaterialService())
        {
            return;
        }

        if (!materialService.TryGetMaterialWithRenderer(MaterialSlotName, out var material, out var smr))
        {
            return;
        }

        if (!IsPoiyomiShader(material?.shader))
        {
            return;
        }

        bool hasLightingProperty = material.HasProperty(LightingModeProperty);
        float lightingModeValue = 0f;
        bool isLightingTextureRamp = false;
        bool isLightingRealistic = false;

        if (hasLightingProperty)
        {
            lightingModeValue = material.GetFloat(LightingModeProperty);
            isLightingTextureRamp = Mathf.Approximately(lightingModeValue, LightingModeTextureRampIndex);
            isLightingRealistic = Mathf.Approximately(lightingModeValue, LightingModeRealisticIndex);
        }

        bool hasMuscleNormal = TryGetMuscleNormal(material, out var normalTexture, out var normalTexturePath);

        bool shouldShowModule = (hasLightingProperty && isLightingTextureRamp) || hasMuscleNormal;
        if (!shouldShowModule)
        {
            return;
        }

        EditorGUILayout.Space();
        foldout = EditorGUILayout.Foldout(foldout, "Adjust Material", true, EditorStyles.foldoutHeader);
        if (!foldout)
        {
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        if (hasLightingProperty && !isLightingRealistic)
        {
            DrawLightingWarning(material, smr);
        }

        if (hasMuscleNormal)
        {
            DrawNormalMapWarning(material, smr, normalTexture, normalTexturePath);
        }

        EditorGUILayout.EndVertical();
    }

    private bool EnsureMaterialService()
    {
        var target = editor?.ultiPawTarget;
        if (target == null)
        {
            return false;
        }

        Transform root = target.transform != null ? target.transform.root : null;
        if (root == null)
        {
            return false;
        }

        if (materialService == null || root != cachedRoot)
        {
            materialService = new MaterialService(root);
            cachedRoot = root;
        }

        return true;
    }

    private void DrawLightingWarning(Material material, SkinnedMeshRenderer smr)
    {
        EditorGUILayout.HelpBox(
            "Your body material is set to a lighting type that won't show the muscles well, it is recommended to set the lighting mode to Realistic.",
            MessageType.Warning);

        bool isLocked = materialService.IsMaterialLocked(material);
        string buttonLabel = isLocked ? "Unlock and set to Realistic" : "Set to Realistic";

        using (new EditorGUI.DisabledScope(!material.HasProperty(LightingModeProperty)))
        {
            if (GUILayout.Button(buttonLabel, GUILayout.Width(220f)))
            {
                if (!EnsureUnlocked(material))
                {
                    EditorUtility.DisplayDialog(
                        "Unlock Failed",
                        "Could not unlock the material shader. Please unlock it manually from Poiyomi before trying again.",
                        "Ok");
                    return;
                }

                ApplyLightingMode();
            }
        }
    }

    private void DrawNormalMapWarning(Material material, SkinnedMeshRenderer smr, Texture normalTexture, string normalTexturePath)
    {
        EditorGUILayout.HelpBox(
            "Your body material is using a normal map with fake muscles, it will conflict with the ultipaw muscles look.",
            MessageType.Warning);

        if (normalTexture != null)
        {
            EditorGUILayout.LabelField("Detected normal map:", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(normalTexture, typeof(Texture), false);
            }
        }
        else if (!string.IsNullOrEmpty(normalTexturePath))
        {
            EditorGUILayout.LabelField("Detected normal map:", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(normalTexturePath, EditorStyles.wordWrappedMiniLabel);
        }

        bool isLocked = materialService.IsMaterialLocked(material);
        string buttonLabel = isLocked ? "Unlock and remove normal map" : "Remove normal map";

        if (GUILayout.Button(buttonLabel, GUILayout.Width(220f)))
        {
            if (!EnsureUnlocked(material))
            {
                EditorUtility.DisplayDialog(
                    "Unlock Failed",
                    "Could not unlock the material shader. Please unlock it manually from Poiyomi before trying again.",
                    "Ok");
                return;
            }

            RemoveNormalMap(material, smr);
        }
    }

    private bool EnsureUnlocked(Material material)
    {
        if (!materialService.IsMaterialLocked(material))
        {
            return true;
        }

        return materialService.UnlockMaterial(material);
    }

    private static bool IsPoiyomiShader(Shader shader)
    {
        if (shader == null)
        {
            return false;
        }

        string shaderName = shader.name;
        return !string.IsNullOrEmpty(shaderName) && shaderName.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ApplyLightingMode()
    {
        if (!materialService.SetLightingMode(MaterialSlotName, LightingModeRealisticIndex))
        {
            EditorUtility.DisplayDialog(
                "Lighting Mode Update Failed",
                "UltiPaw could not switch the material to Realistic lighting. Please try updating the property manually.",
                "Ok");
        }
        else
        {
            UltiPawLogger.Log("[AdjustMaterial] Set body lighting mode to Realistic");
        }
    }

    private static void RemoveNormalMap(Material material, SkinnedMeshRenderer smr)
    {
        if (material == null || smr == null)
        {
            return;
        }

        if (!material.HasProperty(BumpMapProperty))
        {
            return;
        }

        Undo.RecordObject(material, "Remove body normal map");
        Undo.RecordObject(smr, "Remove body normal map");

        material.SetTexture(BumpMapProperty, null);
        material.DisableKeyword("_NORMALMAP");
        material.DisableKeyword("_BUMP");

        EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(smr);

        UltiPawLogger.Log("[AdjustMaterial] Removed body normal map");
    }

    private static bool TryGetMuscleNormal(Material material, out Texture texture, out string texturePath)
    {
        texture = null;
        texturePath = null;

        if (material == null || !material.HasProperty(BumpMapProperty))
        {
            return false;
        }

        texture = material.GetTexture(BumpMapProperty);
        if (texture == null)
        {
            return false;
        }

        string assetPath = AssetDatabase.GetAssetPath(texture);
        string nameToCheck = !string.IsNullOrEmpty(assetPath) ? Path.GetFileName(assetPath) : texture.name;
        string lower = nameToCheck.ToLowerInvariant();

        if (!lower.Contains("muscle") && !lower.Contains("buff"))
        {
            texture = null;
            return false;
        }

        texturePath = !string.IsNullOrEmpty(assetPath) ? assetPath : texture.name;
        return true;
    }
}
#endif
