﻿#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

public class CustomVeinsDrawer
{
    private readonly UltiPawEditor editor;
    private Texture2D customVeinsTexture;
    private Texture2D okIcon;
    private Texture2D koIcon;
    private MaterialService materialService;
    
    private const string CUSTOM_VEINS_PREF_KEY = "UltiPaw_CustomVeins_Enabled";

    public CustomVeinsDrawer(UltiPawEditor editor)
    {
        this.editor = editor;
        LoadTextures();
    }

    private void LoadTextures()
    {
        customVeinsTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/ultipaw/Editor/customVeins.png");
        okIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/ultipaw/Editor/ok.png");
        koIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/ultipaw/Editor/ko.png");
    }

    public void Draw()
    {
        var appliedVersion = editor.ultiPawTarget.appliedUltiPawVersion;
        
        // Only show if extraCustomization contains "customVeins" 
        if (!editor.isUltiPaw || appliedVersion?.extraCustomization == null || 
            !appliedVersion.extraCustomization.Contains("customVeins"))
            return;

        // Initialize MaterialService with avatar root
        if (materialService == null)
        {
            materialService = new MaterialService(editor.ultiPawTarget.transform.root);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Custom Veins", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical();

        // Horizontal layout for image on left, controls on right
        EditorGUILayout.BeginHorizontal();

        // Left side: Image
        if (customVeinsTexture != null)
        {
            GUILayout.BeginVertical(GUILayout.Width(110));
            GUILayout.FlexibleSpace();
            GUILayout.Label(customVeinsTexture, GUILayout.Width(100), GUILayout.Height(100));
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        // Right side: Checkbox, shader info, and texture preview
        GUILayout.BeginVertical();
        
        // Checkbox
        bool currentEnabled = EditorPrefs.GetBool(CUSTOM_VEINS_PREF_KEY, true);
        EditorGUI.BeginChangeCheck();
        bool newEnabled = EditorGUILayout.Toggle("Custom veins", currentEnabled);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(CUSTOM_VEINS_PREF_KEY, newEnabled);
            if (newEnabled)
            {
                ApplyCustomVeins();
            }
            else
            {
                RemoveCustomVeins();
            }
        }

        // Shader detection and compatibility display
        DrawShaderCompatibility();

        // "Applied on detail normal map" label and texture preview
        DrawVeinsTexturePreview();

        // Re-apply button below the image
        using (new EditorGUI.DisabledScope(!currentEnabled))
        {
            if (GUILayout.Button("Re-apply", GUILayout.Width(100)))
            {
                ApplyCustomVeins();
            }
        }
        
        GUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawShaderCompatibility()
    {
        string shaderName = materialService.GetShader("Body");
        
        if (string.IsNullOrEmpty(shaderName))
        {
            EditorGUILayout.HelpBox("Could not detect shader on Body material", MessageType.Warning);
            return;
        }

        bool isSupported = materialService.IsShaderSupported(shaderName);
        Texture2D icon = isSupported ? okIcon : koIcon;

        // Display "Detected Shader:" label
        EditorGUILayout.LabelField("Detected Shader:", EditorStyles.miniLabel);

        // Display shader name with icon in a fixed-height horizontal layout for proper vertical alignment
        EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
        
        if (icon != null)
        {
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            GUILayout.Space(2); // spacing between icon and label
        }
        
        string supportText = isSupported ? "Supported" : "Not Supported";
        // Create a custom style with top padding to vertically center the text with the 20x20 icon
        GUIStyle centeredLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            padding = new RectOffset(0, 0, 4, 0) // Add 4px top padding to align with icon center
        };
        GUILayout.Label($"{shaderName}: {supportText}", centeredLabelStyle, GUILayout.ExpandHeight(false));
        
        EditorGUILayout.EndHorizontal();
    }

    private void DrawVeinsTexturePreview()
    {
        var appliedVersion = editor.ultiPawTarget.appliedUltiPawVersion;
        if (appliedVersion == null) return;

        // Construct the path to the veins normal map using the utility method
        string versionFolder = UltiPawUtils.GetVersionDataPath(appliedVersion.version, appliedVersion.defaultAviVersion);
        string veinsNormalPath = System.IO.Path.Combine(versionFolder, "veins normal.png").Replace("\\", "/");

        // Load the texture
        Texture2D veinsTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(veinsNormalPath);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        // Display "Applied on detail normal map" label
        EditorGUILayout.LabelField("Applied on detail normal map", EditorStyles.miniLabel);

        // Display mini read-only texture field
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(veinsTexture, typeof(Texture2D), false, GUILayout.Height(16));
        }
        EditorGUILayout.EndVertical();
    }


    private void ApplyCustomVeins()
    {
        var appliedVersion = editor.ultiPawTarget.appliedUltiPawVersion;
        if (appliedVersion == null)
        {
            UltiPawLogger.LogError("[CustomVeinsDrawer] No applied version found");
            return;
        }

        // Construct the path to the veins normal map using the utility method
        string versionFolder = UltiPawUtils.GetVersionDataPath(appliedVersion.version, appliedVersion.defaultAviVersion);
        string veinsNormalPath = System.IO.Path.Combine(versionFolder, "veins normal.png").Replace("\\", "/");

        UltiPawLogger.Log($"[CustomVeinsDrawer] Applying custom veins from: {veinsNormalPath}");

        // Apply the detail normal map
        bool success = materialService.SetDetailNormalMap("Body", veinsNormalPath);
        if (success)
        {
            // Set opacity to 1.0
            materialService.SetDetailNormalOpacity("Body", 1.0f);
            UltiPawLogger.Log("[CustomVeinsDrawer] Custom veins applied successfully");
        }
        else
        {
            UltiPawLogger.LogError("[CustomVeinsDrawer] Failed to apply custom veins");
        }
    }

    private void RemoveCustomVeins()
    {
        UltiPawLogger.Log("[CustomVeinsDrawer] Removing custom veins");
        
        bool success = materialService.RemoveDetailNormalMap("Body");
        if (success)
        {
            UltiPawLogger.Log("[CustomVeinsDrawer] Custom veins removed successfully");
        }
        else
        {
            UltiPawLogger.LogError("[CustomVeinsDrawer] Failed to remove custom veins");
        }
    }
}
#endif
