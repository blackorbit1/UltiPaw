#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UltiPaw))]
public class UltiPawEditor : Editor
{
    // --- Target & Serialized Object ---
    public UltiPaw ultiPawTarget;
    public new SerializedObject serializedObject;
    private Texture2D bannerTexture;
    
    // --- Serialized Properties ---
    public SerializedProperty specifyCustomBaseFbxProp, baseFbxFilesProp, blendShapeValuesProp, isCreatorModeProp,
                              customFbxForCreatorProp, ultipawAvatarForCreatorProp, avatarLogicPrefabProp, customBlendshapesForCreatorProp;

    // --- Services and Modules ---
    private NetworkService networkService;
    private AuthenticationModule authModule;
    private VersionManagementModule versionModule;
    private CreatorModeModule creatorModule;
    
    // --- SHARED EDITOR STATE ---
    public bool isAuthenticated;
    public string authToken;
    public bool fetchAttempted;
    public bool isFetching, isDownloading, isDeleting, isSubmitting;
    public string fetchError, downloadError, deleteError, submitError;
    public string currentBaseFbxHash;
    public bool isUltiPaw;
    public List<UltiPawVersion> serverVersions = new List<UltiPawVersion>();
    public UltiPawVersion recommendedVersion, selectedVersionForAction;
    
    private void OnEnable()
    {
        ultiPawTarget = (UltiPaw)target;
        serializedObject = new SerializedObject(ultiPawTarget);
        
        // This path should point to your banner inside your UPM package or Assets folder.
        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.Combine(UltiPawUtils.PACKAGE_BASE_FOLDER, "Editor/banner.png")); 
        FindSerializedProperties();
        
        networkService = new NetworkService();
        var fileManagerService = new FileManagerService();
        
        authModule = new AuthenticationModule(this);
        versionModule = new VersionManagementModule(this, networkService, fileManagerService);
        creatorModule = new CreatorModeModule(this);
        
        creatorModule.Initialize();
        CheckAuthentication();
        versionModule.OnEnable();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        DrawBanner();

        if (!isAuthenticated)
        {
            authModule.DrawMagicSyncAuth();
        }
        
        // If not authenticated, the auth module will draw the login prompt and we stop here.
        if (isAuthenticated)
        {
            // The VersionManagementModule now handles the file config, version list,
            // action buttons, and blendshape sliders internally.
            versionModule.Draw();

            // The CreatorModeModule handles its own section, including the foldout.
            creatorModule.Draw();

            // The generic help box at the end.
            DrawHelpBox();
            
            authModule.DrawLogoutButton();
        }

        serializedObject.ApplyModifiedProperties();
    }
    
    private void DrawBanner()
    {
        if (bannerTexture == null) return;
        float aspect = (float)bannerTexture.width / bannerTexture.height;
        float desiredWidth = EditorGUIUtility.currentViewWidth - 40;
        Rect rect = GUILayoutUtility.GetRect(desiredWidth, desiredWidth / aspect);
        GUI.DrawTexture(rect, bannerTexture, ScaleMode.ScaleToFit);
        GUILayout.Space(5);
    }
    
    private void DrawHelpBox()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "1. Ensure Base FBX is detected/assigned correctly.\n" +
            "2. Click 'Check for Updates' to find compatible versions.\n" +
            "3. Select a version and use the action buttons to apply or reset.",
            MessageType.Info);
    }
    
    public void CheckAuthentication()
    {
        authToken = UltiPawUtils.GetAuth().token;
        isAuthenticated = !string.IsNullOrEmpty(authToken);
    }

    private void FindSerializedProperties()
    {
        specifyCustomBaseFbxProp = serializedObject.FindProperty("specifyCustomBaseFbx");
        baseFbxFilesProp = serializedObject.FindProperty("baseFbxFiles");
        blendShapeValuesProp = serializedObject.FindProperty("blendShapeValues");
        isCreatorModeProp = serializedObject.FindProperty("isCreatorMode");
        customFbxForCreatorProp = serializedObject.FindProperty("customFbxForCreator");
        ultipawAvatarForCreatorProp = serializedObject.FindProperty("ultipawAvatarForCreatorProp");
        avatarLogicPrefabProp = serializedObject.FindProperty("avatarLogicPrefab");
        customBlendshapesForCreatorProp = serializedObject.FindProperty("customBlendshapesForCreator");
    }
    
    // Helper methods for version parsing, accessible by all modules.
    public Version ParseVersion(string v)
    {
        if (string.IsNullOrEmpty(v)) return new Version(0,0);
        if (v.Count(c => c == '.') == 0) v += ".0.0";
        if (v.Count(c => c == '.') == 1) v += ".0";
        return Version.TryParse(v, out var ver) ? ver : new Version(0,0);
    }

    public int CompareVersions(string v1, string v2) => ParseVersion(v1).CompareTo(ParseVersion(v2));
}
#endif