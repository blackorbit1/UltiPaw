#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

[CustomEditor(typeof(UltiPaw))]
public class UltiPawEditor : UnityEditor.Editor
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
    public VersionManagementModule versionModule;
    private CreatorModeModule creatorModule;
    private AdvancedModeModule advancedModule;
    
    // --- SHARED EDITOR STATE ---
    public bool isAuthenticated;
    public string authToken;
    public bool fetchAttempted;
    public bool isFetching, isDownloading, isDeleting, isSubmitting;
    public string fetchError, downloadError, deleteError, submitError;
    public string currentBaseFbxHash;
    public bool isUltiPaw;
    public List<UltiPawVersion> serverVersions = new List<UltiPawVersion>();
    public List<UltiPawVersion> unsubmittedVersions = new List<UltiPawVersion>();
    public UltiPawVersion recommendedVersion, selectedVersionForAction;
    
    public string uiRenderingError;
    private string lastUiRenderingExceptionSignature;
    
    private void OnEnable()
    {
        ultiPawTarget = (UltiPaw)target;
        serializedObject = new SerializedObject(ultiPawTarget);
        
        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.Combine(UltiPawUtils.PACKAGE_BASE_FOLDER, "Editor/banner.png")); 
        FindSerializedProperties();
        
        networkService = new NetworkService();
        var fileManagerService = new FileManagerService();
        
        authModule = new AuthenticationModule(this);
        versionModule = new VersionManagementModule(this, networkService, fileManagerService);
        creatorModule = new CreatorModeModule(this);
        advancedModule = new AdvancedModeModule(this);
        
        LoadUnsubmittedVersions(); // Load local versions first
        creatorModule.Initialize();
        CheckAuthentication();
        versionModule.OnEnable();
        
        // Subscribe to play mode state changes
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }
    
    private void OnDisable()
    {
        // Unsubscribe from play mode state changes
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }
    
    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        advancedModule?.OnPlayModeStateChanged(state);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        try
        {
            SafeUiCall(DrawBanner);

            if (!isAuthenticated)
            {
                SafeUiCall(() => authModule.DrawMagicSyncAuth());
            }

            if (isAuthenticated)
            {
                SafeUiCall(() => creatorModule.Draw());
                SafeUiCall(() => versionModule.Draw());
                SafeUiCall(DrawHelpBox);
            }

            DrawLogoutSectionSafely();
            SafeUiCall(() => advancedModule.Draw());
            DrawUiRenderingError();
        }
        finally
        {
            serializedObject.ApplyModifiedProperties();
        }
    }
    
    private void SafeUiCall(Action drawAction)
    {
        if (drawAction == null) return;

        try
        {
            drawAction.Invoke();
        }
        catch (Exception ex)
        {
            if (ex is ExitGUIException) throw;
            RecordUiException(ex);
        }
    }

    private void DrawLogoutSectionSafely()
    {
        if (!isAuthenticated) return;

        Color originalColor = GUI.backgroundColor;

        try
        {
            authModule.DrawLogoutButton();
        }
        catch (Exception ex)
        {
            if (ex is ExitGUIException) throw;

            GUI.backgroundColor = originalColor;
            RecordUiException(ex);
            DrawFallbackLogoutButton();
        }
        finally
        {
            GUI.backgroundColor = originalColor;
        }
    }

    private void DrawFallbackLogoutButton()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Logout", GUILayout.Width(100f), GUILayout.Height(25f)))
        {
            if (EditorUtility.DisplayDialog("Confirm Logout", "Are you sure you want to log out?", "Logout", "Cancel"))
            {
                if (UltiPawUtils.RemoveAuth())
                {
                    isAuthenticated = false;
                    authToken = null;
                    Repaint();
                }
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(10);
    }

    private void DrawUiRenderingError()
    {
        if (string.IsNullOrEmpty(uiRenderingError)) return;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(uiRenderingError, MessageType.Error);

        if (GUILayout.Button("Dismiss Error Message"))
        {
            uiRenderingError = null;
            lastUiRenderingExceptionSignature = null;
        }
    }

    private void RecordUiException(Exception ex)
    {
        if (ex == null) return;

        string baseMessage = "UltiPaw encountered a problem while drawing the editor UI. Check the Console for details.";
        string reason = $"Reason: {ex.Message}";

        if (string.IsNullOrEmpty(uiRenderingError))
        {
            uiRenderingError = $"{baseMessage}\n{reason}";
        }
        else if (!uiRenderingError.Contains(reason))
        {
            uiRenderingError += $"\n{reason}";
        }

        string signature = ex.ToString();
        if (!string.Equals(lastUiRenderingExceptionSignature, signature, StringComparison.Ordinal))
        {
            Debug.LogException(ex);
            lastUiRenderingExceptionSignature = signature;
        }
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
        authToken = UltiPawUtils.GetAuth()?.token;
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

    public void LoadUnsubmittedVersions()
    {
        unsubmittedVersions.Clear();
        string path = UltiPawUtils.UNSUBMITTED_VERSIONS_FILE;
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<List<UltiPawVersion>>(json);
                if (loaded != null)
                {
                    foreach (var v in loaded)
                    {
                        v.isUnsubmitted = true; // Set runtime flag
                        unsubmittedVersions.Add(v);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UltiPaw] Failed to load unsubmitted versions from {path}: {ex.Message}");
            }
        }
    }

    public List<UltiPawVersion> GetAllVersions()
    {
        // Combine server versions with unsubmitted ones, preventing duplicates
        var allVersions = new List<UltiPawVersion>(serverVersions);
        var unsubmittedToAdd = unsubmittedVersions.Where(uv => !allVersions.Any(sv => sv.Equals(uv)));
        allVersions.AddRange(unsubmittedToAdd);
        return allVersions.OrderByDescending(v => ParseVersion(v.version)).ToList();
    }
    
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