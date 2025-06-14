#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System; // For Version comparison
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Networking;
using System.IO.Compression;
using Newtonsoft.Json;
using System.Security.Cryptography;
using CompressionLevel = System.IO.Compression.CompressionLevel; // For MD5 hash calculation


[CustomEditor(typeof(UltiPaw))]
public class UltiPawEditor : Editor
{
    private UltiPaw ultiPaw; // The component being inspected

    private Texture2D bannerTexture;
    private SerializedProperty baseFbxFilesProp;
    private SerializedProperty specifyCustomBaseFbxProp;
    private SerializedProperty blendShapeValuesProp;

    // Version management state
    private List<UltiPawVersion> serverVersions = new List<UltiPawVersion>();
    private string recommendedVersionGuid = ""; // Stores the version string (e.g., "1.2.3")
    private UltiPawVersion selectedVersion = null; // Version selected in the UI for potential application
    private UltiPawVersion recommendedVersion = null; // Full details of the recommended version

    private bool isFetching = false;
    private bool isDownloading = false;
    private bool isDeleting = false; // State for deletion process
    private string fetchError = "";
    private string downloadError = "";
    private string deleteError = ""; // Error specific to deletion
    private string hashToFetch = null; // TODO deprecate this, use the ultipaw's hash instead
    private bool versionsFoldout = true; // Default to open
    
    private bool isAuthenticated = false; // Track authentication state

    // Server settings

    private static readonly Color OrangeColor = new Color(1.0f, 0.65f, 0.0f); // For Downgrade button
    private static readonly Color BACKGROUND_COLOR = new Color(0.28f, 0.28f, 0.28f);
    private static readonly Color BORDER_COLOR = new Color(0.46f, 0.46f, 0.46f);
    
    
    private enum ActionType
    {
        INSTALL,
        UPDATE,
        DOWNGRADE,
        UNAVAILABLE
    }

    // --- NEW: Creator Mode Variables ---
    private SerializedProperty isCreatorModeProp;
    private SerializedProperty customFbxForCreatorProp; // For the custom FBX to upload
    private SerializedProperty avatarLogicPrefabProp;   // For the prefab containing logic
    private SerializedProperty ultipawAvatarForCreatorProp; // For the transformed UltiPaw avatar

    private bool creatorModeFoldout = false; // State for the foldout
    private int newVersionMajor = 0;
    private int newVersionMinor = 0;
    private int newVersionPatch = 0;
    private Scope newVersionScope = Scope.BETA; // Default to Beta
    private string newChangelog = "";
    private bool isSubmitting = false;
    private string submitError = "";
    private List<UltiPawVersion> compatibleParentVersions;
    private List<string> parentVersionDisplayOptions;
    private int defaultParentIndex;

    // NEW: For "Parent Version" dropdown
    private int selectedParentVersionIndex = -1; // -1 indicates no explicit user selection, will default to "None" or applied
    private UltiPawVersion selectedParentVersionObject = null; // Stores the actual UltiPawVersion object for submission
    // --- End Creator Mode Variables ---
    
    // --- NEW: For Creator Mode versioning ---
    private string _lastBaseVersionForCreatorDefaults = null;
    // --- End ---


    private void OnEnable()
    {
        ultiPaw = (UltiPaw)target; // Initialize the property

        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(UltiPawUtils.PACKAGE_BASE_FOLDER + "/Editor/banner.png");
        
        baseFbxFilesProp = serializedObject.FindProperty("baseFbxFiles");
        specifyCustomBaseFbxProp = serializedObject.FindProperty("specifyCustomBaseFbx");
        blendShapeValuesProp = serializedObject.FindProperty("blendShapeValues");

        // --- NEW: Creator Mode Initialization ---
        isCreatorModeProp = serializedObject.FindProperty("isCreatorMode"); 
        customFbxForCreatorProp = serializedObject.FindProperty("customFbxForCreator"); 
        avatarLogicPrefabProp = serializedObject.FindProperty("avatarLogicPrefab");
        // *** FIX: Corrected the property name string to match the field in UltiPaw.cs ***
        ultipawAvatarForCreatorProp = serializedObject.FindProperty("ultipawAvatarForCreatorProp");
        // --- End Creator Mode Initialization ---

        // Restore selected version state from the component if possible
        // We keep 'selectedVersion' separate from 'appliedUltiPawVersion'
        // 'selectedVersion' is the UI selection, 'appliedUltiPawVersion' is the FBX state.
        if (ultiPaw.activeUltiPawVersion != null && !string.IsNullOrEmpty(ultiPaw.activeUltiPawVersion.version))
        {
            selectedVersion = ultiPaw.activeUltiPawVersion;
        }
        else
        {
            selectedVersion = null; // Ensure clean state if component's active version is null/invalid
        }

        // Initial fetch if hash is missing or no versions loaded yet
        bool needsHashUpdate = string.IsNullOrEmpty(ultiPaw.currentBaseFbxHash);
        if (needsHashUpdate)
        {
            ultiPaw.UpdateCurrentBaseFbxHash(); // Calculate hash if missing
        }

        if (UltiPawUtils.HasAuth())
        {
            isAuthenticated = true;
        }
        // Fetch if hash is now available AND server versions haven't been loaded
        // Or if the hash changed since last fetch
        if (isAuthenticated && !string.IsNullOrEmpty(ultiPaw.currentBaseFbxHash) && (serverVersions.Count == 0 || ultiPaw.currentBaseFbxHash != hashToFetch))
        {
            StartVersionFetch();
        }

        // Initialize blendshape values (existing logic)
        if (blendShapeValuesProp != null && blendShapeValuesProp.arraySize != 0)
        {
            // No need to reset to 0 here, OnValidate/SyncBlendshapeLists handles size,
            // and values should persist unless reset/version changed.
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (isAuthenticated)
        {
            // Check if FBX changed and update hash if necessary. This is less frequent than every frame.
            bool fbxPathChanged = false;
            if (ultiPaw.baseFbxFiles != null && ultiPaw.baseFbxFiles.Count > 0 && ultiPaw.baseFbxFiles[0] != null)
            {
                string currentPath = AssetDatabase.GetAssetPath(ultiPaw.baseFbxFiles[0]);
                if (currentPath != ultiPaw.currentBaseFbxPath)
                {
                    ultiPaw.currentBaseFbxPath = currentPath;
                    fbxPathChanged = true;
                }
            }
            else if (!string.IsNullOrEmpty(ultiPaw.currentBaseFbxPath)) // FBX was removed
            {
                 ultiPaw.currentBaseFbxPath = null;
                 fbxPathChanged = true;
            }
    
            if (fbxPathChanged)
            {
                 if (ultiPaw.UpdateCurrentBaseFbxHash()) // Update hash and state if path changed
                 {
                     // Hash changed, trigger fetch
                     StartVersionFetch();
                 }
            }
        }
        

        DrawBanner();
        
        if (!isAuthenticated)
        {
            if (!UltiPawUtils.HasAuth())
            {
                DrawMagicSyncAuth();
                return; // Don't show the rest of the UI
            }
            isAuthenticated = true;
        }

        DrawFileConfiguration(); // This might trigger auto-detect, which calls hash update/fetch
        // --- NEW: Creator Mode Checkbox and Conditional Display ---
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(isCreatorModeProp, new GUIContent("Enable Creator Mode"));
        if (isCreatorModeProp.boolValue)
        {
            DrawCreatorMode();
        }
        EditorGUILayout.Space();

        DrawVersionManagement();
        DrawUpdateNotification();
        DrawActionButtons();
        DrawBlendshapeSliders();
        DrawHelpBox();
        DrawLogoutButton();

        serializedObject.ApplyModifiedProperties(); 
    }

    // --- UI Drawing Helper Methods ---
    
    void DrawMagicSyncAuth()
    {
        EditorGUILayout.Space(10);
    
        // Center the button with flexible space on both sides
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
    
        // Use a fixed width button with better sizing
        if (GUILayout.Button("Magic Sync", GUILayout.Width(120f), GUILayout.Height(30f)))
        {
            UltiPawUtils.RegisterAuth().ContinueWith(task => {
                if (task.Result)
                {
                    // Force repaint to update the UI after successful authentication
                    Repaint();
                    isAuthenticated = true;
                }
                else
                {
                    EditorUtility.DisplayDialog("Authentication Failed", 
                        "Please visit the Orbiters website and click 'Magic Sync' first.", "OK");
                }
            });
        }
    
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    
        EditorGUILayout.Space(10); // Add space after the button
    
        EditorGUILayout.HelpBox("Use Magic Sync to authenticate this tool. Go to the Orbiters website, click 'Magic Sync' to copy your token, then click the button above.", MessageType.Info);
    
        EditorGUILayout.Space(5); // A bit more space at the bottom
    }

    private void DrawBanner()
    {
        if (bannerTexture != null)
        {
            float aspect = (float)bannerTexture.width / bannerTexture.height;
            float desiredWidth = EditorGUIUtility.currentViewWidth - 40;
            float desiredHeight = desiredWidth / aspect;
            Rect rect = GUILayoutUtility.GetRect(desiredWidth, desiredHeight, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, bannerTexture, ScaleMode.ScaleToFit);
            GUILayout.Space(5);
        }
    }

    private void DrawFileConfiguration()
    {
        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(specifyCustomBaseFbxProp, new GUIContent("Specify Base FBX Manually"));
        bool fbxSpecChanged = EditorGUI.EndChangeCheck();

        bool fbxFieldChanged = false;
        if (specifyCustomBaseFbxProp.boolValue)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(baseFbxFilesProp, new GUIContent("Base FBX File(s)"), true);
            fbxFieldChanged = EditorGUI.EndChangeCheck();
        }
        else // Auto-detect mode
        {
            if (fbxSpecChanged) 
            {
                EditorApplication.delayCall += ultiPaw.AutoDetectBaseFbxViaHierarchy; // Schedule detection
            }
            GUI.enabled = false;
            EditorGUILayout.PropertyField(baseFbxFilesProp, new GUIContent("Detected Base FBX"), true);
            GUI.enabled = true;
        }

        // If any relevant property changed, apply and trigger hash update/fetch
        if (fbxSpecChanged || fbxFieldChanged)
        {
            serializedObject.ApplyModifiedProperties(); // Apply changes first
            if (ultiPaw.UpdateCurrentBaseFbxHash()) // Update hash and state
            {
                 StartVersionFetch(); // Fetch if hash changed
            }
            Repaint();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    private void DrawVersionManagement()
    {
        EditorGUILayout.BeginVertical();

        // Display Errors First
        if (!string.IsNullOrEmpty(fetchError)) EditorGUILayout.HelpBox("Fetch Error: " + fetchError, MessageType.Error);
        if (!string.IsNullOrEmpty(downloadError)) EditorGUILayout.HelpBox("Download Error: " + downloadError, MessageType.Warning);
        if (!string.IsNullOrEmpty(deleteError)) EditorGUILayout.HelpBox("Delete Error: " + deleteError, MessageType.Warning); // Show delete errors

        if (!isFetching && serverVersions != null)
        {
            if (!ultiPaw.UpdateAppliedUltiPawVersion(serverVersions))
            {
                StartVersionFetch(); // Re-fetch if applied version is not found in the list
            }
            
        }
        
        // Update the component's active version (used for transform/reset actions)
        ultiPaw.activeUltiPawVersion = selectedVersion;

        // Update/Fetch Button
        GUI.enabled = !isFetching && !isDownloading && !isDeleting; // Disable during any operation
        if (GUILayout.Button(isFetching ? "Fetching..." : "Check for Updates"))
        {
            StartVersionFetch();
        }
        GUI.enabled = true;


        if (
            selectedVersion == null &&
            !isFetching &&
            serverVersions.Count > 0 &&
            ultiPaw.appliedUltiPawVersion != null &&
            recommendedVersion != null &&
            CompareVersions(ultiPaw.appliedUltiPawVersion.version, recommendedVersion.version) < 0)
        {
            selectedVersion = recommendedVersion;
        }

        // Foldout for all available versions
        if (serverVersions.Count > 0 || isFetching) // Show foldout even while fetching
        {
            versionsFoldout = EditorGUILayout.Foldout(versionsFoldout, "All UltiPaw Versions", true, EditorStyles.foldoutHeader); // Use header style
            if (versionsFoldout && !isFetching) // Only draw list content if not fetching
            {
                // Sort versions (newest first) - requires System.Version parsing
                var sortedVersions = serverVersions
                    .Where(v => v != null && !string.IsNullOrEmpty(v.version)) // Filter out invalid entries
                    .OrderByDescending(v => ParseVersion(v.version)) // Sort using helper
                    .ToList();

                foreach (var ver in sortedVersions)
                {
                    DrawVersionListItemWithSplitButtons(ver); 
                    EditorGUILayout.Space(2);
                }
            }
            else if (versionsFoldout && isFetching)
            {
                 EditorGUILayout.LabelField("Fetching version list...", EditorStyles.centeredGreyMiniLabel);
            }
        }
        else if (!isFetching && hashToFetch != null) // Fetch succeeded, hash known, but no versions returned
        {
             EditorGUILayout.HelpBox("No compatible versions found for the detected Base FBX hash.", MessageType.Warning);
        }
        else if (!isFetching && hashToFetch == null && string.IsNullOrEmpty(ultiPaw.currentBaseFbxHash)) // Haven't fetched successfully yet, no FBX
        {
             EditorGUILayout.HelpBox("Assign or detect a Base FBX first.", MessageType.Info);
        }
         else if (!isFetching && hashToFetch == null && !string.IsNullOrEmpty(ultiPaw.currentBaseFbxHash)) // Haven't fetched successfully yet, FBX assigned
        {
             EditorGUILayout.HelpBox("Click 'Check for Updates'.", MessageType.Info);
        }


        EditorGUILayout.Space();
        EditorGUILayout.EndVertical();
    }
    
    private void DrawChipLabel(string text, Color backgroundColor, Color textColor, Color? borderColor = null, int width = 65, int height = 18, float cornerRadius = 8f, float borderWidth = 1f)
    {
        Rect rect = GUILayoutUtility.GetRect(width, height);

        Handles.BeginGUI();

        Color oldColor = Handles.color;

        if (borderColor.HasValue)
        {
            // Draw border first
            Handles.color = borderColor.Value;
            DrawRoundedRect(rect, cornerRadius);

            // Compute adjusted inner rect and radius
            float shrink = borderWidth;
            Rect innerRect = new Rect(rect.x + shrink, rect.y + shrink, rect.width - shrink * 2f, rect.height - shrink * 2f);
            float innerRadius = Mathf.Max(0, cornerRadius - borderWidth * 0.5f);

            Handles.color = backgroundColor;
            DrawRoundedRect(innerRect, innerRadius);
        }
        else
        {
            // No border, normal background
            Handles.color = backgroundColor;
            DrawRoundedRect(rect, cornerRadius);
        }

        Handles.color = oldColor;
        Handles.EndGUI();

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = textColor },
            padding = new RectOffset(4, 4, 1, 1)
        };

        GUI.Label(rect, text, labelStyle);
    }


    private void DrawRoundedRect(Rect rect, float radius)
    {
        List<Vector3> verts = new List<Vector3>();

        AddArc(verts, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f);
        verts.Add(new Vector2(rect.xMax - radius, rect.yMin));
        AddArc(verts, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f);
        verts.Add(new Vector2(rect.xMax, rect.yMax - radius));
        AddArc(verts, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f);
        verts.Add(new Vector2(rect.xMin + radius, rect.yMax));
        AddArc(verts, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f);
        verts.Add(new Vector2(rect.xMin, rect.yMin + radius));

        Handles.DrawAAConvexPolygon(verts.ToArray());
    }

    private void AddArc(List<Vector3> verts, Vector2 center, float radius, float startAngle, float endAngle, int segments = 4)
    {
        float angleStep = (endAngle - startAngle) / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (startAngle + i * angleStep) * Mathf.Deg2Rad;
            verts.Add(new Vector2(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius
            ));
        }
    }

    private void DrawVersionListItemWithSplitButtons(UltiPawVersion ver)
    {
        if (ver == null) return;

        bool hasValidVersionStrings = !string.IsNullOrEmpty(ver.version) && !string.IsNullOrEmpty(ver.defaultAviVersion);
        if (!hasValidVersionStrings)
        {
            EditorGUILayout.LabelField($"Invalid version data for entry.", EditorStyles.miniLabel);
            return;
        }

        string expectedBinPath = UltiPawUtils.GetVersionBinPath(ver.version, ver.defaultAviVersion);
        bool isDownloaded = !string.IsNullOrEmpty(expectedBinPath) && File.Exists(expectedBinPath);
        bool isCurrentlySelected = selectedVersion != null && selectedVersion.Equals(ver);
        bool isCurrentlyApplied = ultiPaw.appliedUltiPawVersion != null && ultiPaw.appliedUltiPawVersion.Equals(ver);
        bool canInteract = !isFetching && !isDownloading && !isDeleting;

        GUIContent downloadIcon = EditorGUIUtility.IconContent("Download-Available", "|Download this version");
        GUIContent deleteIcon = EditorGUIUtility.IconContent("TreeEditor.Trash", "|Delete downloaded files for this version");
        if (downloadIcon.image == null) downloadIcon = new GUIContent("↓", "|Download this version");
        if (deleteIcon.image == null) deleteIcon = new GUIContent("X", "|Delete downloaded files for this version");

        // Begin the main horizontal row with the helpBox style
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        // --- Column 1: Version Label (Vertically Centered) ---
        EditorGUILayout.BeginVertical(GUILayout.Width(100)); // Fixed width for label column
        GUILayout.FlexibleSpace(); // Push label down
        GUILayout.Label($"UltiPaw {ver.version}");
        GUILayout.FlexibleSpace(); // Push label up (centers it)
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace(); // Pushes all subsequent elements to the right

        // --- Column 2: Applied Chip (Vertically Centered) ---
        if (isCurrentlyApplied)
        {
            EditorGUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            DrawChipLabel("Installed", new Color(0.28f, 0.28f, 0.28f), new Color(0.33f, 0.79f, 0f), new Color(0.54f, 0.54f, 0.54f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
            GUILayout.Space(5); // Space after chip
        }

        // --- Column 3: Scope Chip (Vertically Centered) ---
        EditorGUILayout.BeginVertical();
        GUILayout.FlexibleSpace();
        DrawScopeLabel(ver.scope);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();
        GUILayout.Space(10); // Space before icons

        // --- Column 4: Download/Delete Icon Button (Vertically Centered) ---
        float iconButtonSize = 22f; // Keep button size reasonable
        EditorGUILayout.BeginVertical(GUILayout.Width(iconButtonSize)); // Constrain width
        GUILayout.FlexibleSpace();
        GUI.enabled = canInteract; // Base interaction state
        if (isDownloaded)
        {
            GUI.enabled = canInteract; // Disable delete for applied version
            if (GUILayout.Button(deleteIcon, GUILayout.Width(iconButtonSize), GUILayout.Height(iconButtonSize)))
            {
                if (EditorUtility.DisplayDialog("Confirm Delete",
                    $"Are you sure you want to delete the downloaded files for UltiPaw version {ver.version} (Base: {ver.defaultAviVersion})?",
                    "Delete", "Cancel"))
                {
                    StartVersionDelete(ver);
                }
            }
        }
        else // Not downloaded
        {
            GUI.enabled = canInteract; // Enable download if possible
            if (GUILayout.Button(downloadIcon, GUILayout.Width(iconButtonSize), GUILayout.Height(iconButtonSize)))
            {
                DownloadVersion(ver, applyAfterDownload: false);
            }
        }
        GUI.enabled = true; // Reset base enabled state
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();
        GUILayout.Space(5); 

        // --- Column 5: Selection Radio Button (Vertically Centered) ---
        EditorGUILayout.BeginVertical(GUILayout.Width(18)); // Constrain width
        GUILayout.FlexibleSpace();
        GUI.enabled = canInteract; // && (isDownloaded || isCurrentlyApplied); // Enable selection if possible
        EditorGUI.BeginChangeCheck();
        bool selectionToggle = GUILayout.Toggle(isCurrentlySelected, "", EditorStyles.radioButton);
        if (EditorGUI.EndChangeCheck() && selectionToggle)
        {
            SelectVersion(ver, expectedBinPath);
        }
        GUI.enabled = true; // Restore GUI enabled state
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();

        // End the main horizontal row
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);
        
        if (!string.IsNullOrEmpty(ver.changelog))
        {
            EditorGUILayout.LabelField("Changelog:", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(ver.changelog, MessageType.None);
        }
        GUILayout.Space(5); // Add space after details
        
        EditorGUILayout.EndVertical();
    }
    
    
    // Helper to draw the scope label with color
    private void DrawScopeLabel(Scope scope)
    {
        Color originalColor = GUI.contentColor;
        DrawChipLabel(
            text: scope.ToString().ToLower(), 
            BACKGROUND_COLOR, 
            textColor: scope switch
            {
                Scope.PUBLIC  => Color.green,
                Scope.BETA    => Color.yellow,
                Scope.ALPHA   => Color.red,
                Scope.UNKNOWN => Color.magenta,
                _             => Color.white
            }, 
            BORDER_COLOR
            );
        GUI.contentColor = originalColor;
    }

    // Draw notification about new version between sections
    private void DrawUpdateNotification()
    {
        if (recommendedVersion != null && 
            ultiPaw.appliedUltiPawVersion != null &&
            CompareVersions(recommendedVersion.version, ultiPaw.appliedUltiPawVersion.version) > 0)
        {
            // Display a green label
            GUI.contentColor = Color.green;
            EditorGUILayout.LabelField($"New version released: v{recommendedVersion.version} !", EditorStyles.boldLabel);
            GUI.contentColor = Color.white; // Reset color
            if (!string.IsNullOrEmpty(recommendedVersion.changelog))
            {
                EditorGUILayout.LabelField("Changelog:", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox(recommendedVersion.changelog, MessageType.None);
            }
            EditorGUILayout.Space();
        }
    }


    private void DrawActionButtons()
    {
        bool canInteract = !isFetching && !isDownloading && !isDeleting;

        bool canTransformCurrentSelection = selectedVersion != null &&
                                            !selectedVersion.Equals(ultiPaw.appliedUltiPawVersion) &&
                                            !string.IsNullOrEmpty(ultiPaw.selectedUltiPawBinPath);
        
        // Check if the *selected* version can be applied
        GUI.enabled = canInteract && canTransformCurrentSelection;
        GUI.backgroundColor = Color.green;

        var compareResult = selectedVersion == null ? 0
            : CompareVersions(selectedVersion.version, ultiPaw.appliedUltiPawVersion?.version);

        ActionType action = ultiPaw.isUltiPaw ? 
            // Is already ultipaw, check if update/downgrade needed
            compareResult switch { 
                < 0 => ActionType.DOWNGRADE,
                > 0 => ActionType.UPDATE,
                _   => ActionType.UNAVAILABLE
            }
            // If not ultipaw, will be "install" button
            : ActionType.INSTALL;

        bool isDownloaded = !string.IsNullOrEmpty(ultiPaw.selectedUltiPawBinPath) && File.Exists(ultiPaw.selectedUltiPawBinPath);
        String possiblyDownloadAnd = isDownloaded ? "" : "Download and ";

        var actionText = action switch
        {
            ActionType.INSTALL => $"{possiblyDownloadAnd}Turn into UltiPaw",
            ActionType.UPDATE  => $"{possiblyDownloadAnd}Update to v{selectedVersion?.version}",
            ActionType.DOWNGRADE => f(() => {
                    GUI.backgroundColor = OrangeColor;
                }, $"{possiblyDownloadAnd}Downgrade to v{selectedVersion?.version}"),
            ActionType.UNAVAILABLE => f(() => {
                    GUI.enabled = false;
                }, $"Installed (v{ultiPaw.appliedUltiPawVersion?.version})"),
            _ => "Unknown Action"
        };
        
        if (GUILayout.Button(actionText, GUILayout.Height(40)))
        {
            if (EditorUtility.DisplayDialog("Confirm Transformation",
                    $"This will modify your base FBX file using the selected UltiPaw version '{selectedVersion?.version ?? "Unknown"}'.\nA backup (.old) will be created.",
                    "Proceed", "Cancel"))
            {
                if (isDownloaded) SelectAndApplyVersion(selectedVersion, ultiPaw.selectedUltiPawBinPath);
                else DownloadVersion(selectedVersion, applyAfterDownload: true);
                
                Repaint();
            }
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;


        // --- Reset Button ---
        bool canRestore = AnyBackupExists(ultiPaw.baseFbxFiles) || ultiPaw.isUltiPaw; // Allow reset even if backup missing but state is 'isUltiPaw'
        GUI.enabled = canInteract && canRestore;
        if (GUILayout.Button("Reset to Original FBX"))
        {
            if (EditorUtility.DisplayDialog("Confirm Reset", "This will restore the original FBX (from its '.old' backup, if found) and attempt to reapply the default avatar configuration.", "Reset", "Cancel"))
            {
                if (ultiPaw.ResetIntoWinterPaw()) // Check success
                {
                    // Success handled inside ResetIntoWinterPaw (clears applied version, etc.)
                    Repaint();
                }
            }
        }
        GUI.enabled = true;
    }

    private void DrawBlendshapeSliders()
    {
        if (!ultiPaw.isUltiPaw || ultiPaw.appliedUltiPawVersion == null) return;

        var namesToShow = ultiPaw.appliedUltiPawVersion?.customBlendshapes;
        if (namesToShow == null || namesToShow.Length == 0) return;

        var smr = ultiPaw.GetBodySkinnedMeshRenderer();
        if (smr == null || smr.sharedMesh == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Blendshapes" + (ultiPaw.isUltiPaw ? "" : " (Preview)"), EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        foreach (var shapeName in namesToShow)
        {
            int index = smr.sharedMesh.GetBlendShapeIndex(shapeName);
            if (index < 0) continue; // Shape doesn't exist

            float currentWeight = smr.GetBlendShapeWeight(index);

            EditorGUI.BeginChangeCheck();
            float newWeight = EditorGUILayout.Slider(
                new GUIContent(shapeName),
                currentWeight,
                0f, 100f
            );
            if (EditorGUI.EndChangeCheck())
            {
                if (!Mathf.Approximately(currentWeight, newWeight))
                {
                    smr.SetBlendShapeWeight(index, newWeight);
                    EditorUtility.SetDirty(smr);
                }
            }
        }

        EditorGUILayout.EndVertical();
    }
    private void DrawHelpBox()
    {
        // (Existing code - no changes needed)
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "1. Ensure Base FBX is detected/assigned correctly.\n" +
            "2. Click 'Check for Updates' to find compatible versions.\n" +
            "3. Download/Select a version from 'All Available Versions'.\n" +
            "4. Use 'Turn into UltiPaw' or 'Update' button to apply the selected/recommended version.\n" +
            "5. Use 'Reset' to restore the original FBX from backup.",
            MessageType.Info
        );
    }
    void DrawLogoutButton()
    {
        // Only show logout button if authenticated
        if (isAuthenticated)
        {
            EditorGUILayout.Space(10);
        
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
        
            // Use a red-styled button to indicate it's a logout/remove action
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f); // Red-ish color
            if (GUILayout.Button("Logout", GUILayout.Width(100f), GUILayout.Height(25f)))
            {
                if (EditorUtility.DisplayDialog("Confirm Logout", 
                        "Are you sure you want to log out? You will need to authenticate again to use this tool.", 
                        "Logout", "Cancel"))
                {
                    if (UltiPawUtils.RemoveAuth())
                    {
                        isAuthenticated = false;
                        // Force repaint to update the UI after logout
                        Repaint();
                    }
                }
            }
            GUI.backgroundColor = Color.white; // Reset color
        
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        
            EditorGUILayout.Space(10);
        }
    }

    // --- NEW: Creator Mode UI ---
     private void DrawCreatorMode()
    {
        EditorGUILayout.Space();
        creatorModeFoldout = EditorGUILayout.Foldout(creatorModeFoldout, "Creator Mode", true, EditorStyles.foldoutHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);


        if (creatorModeFoldout)
        {
            GUI.enabled = !isSubmitting; // Disable fields during submission

            if (ultiPaw.appliedUltiPawVersion != null)
            {
                if (ultipawAvatarForCreatorProp != null && ultipawAvatarForCreatorProp.objectReferenceValue == null)
                {
                    string ultipawAvatarPath = UltiPawUtils.GetVersionAvatarPath(ultiPaw.appliedUltiPawVersion.version, ultiPaw.appliedUltiPawVersion.defaultAviVersion, UltiPawUtils.ULTIPAW_AVATAR_NAME);
                    if (!string.IsNullOrEmpty(ultipawAvatarPath) && File.Exists(ultipawAvatarPath))
                    {
                        ultipawAvatarForCreatorProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Avatar>(ultipawAvatarPath);
                    }
                }
            }
            
            // --- Set Default Version Logic ---
            string currentVersionStr = ultiPaw.appliedUltiPawVersion?.version;
            if (currentVersionStr != _lastBaseVersionForCreatorDefaults)
            {
                _lastBaseVersionForCreatorDefaults = currentVersionStr;
                if (string.IsNullOrEmpty(currentVersionStr))
                {
                    newVersionMajor = 0;
                    newVersionMinor = 1;
                    newVersionPatch = 0;
                }
                else
                {
                    Version baseVersion = ParseVersion(currentVersionStr);
                    if (baseVersion.Build >= 0 && baseVersion.Revision == -1) 
                    {
                        newVersionMajor = baseVersion.Major;
                        newVersionMinor = baseVersion.Minor;
                        newVersionPatch = baseVersion.Build + 1;
                    }
                    else if (baseVersion.Minor >= 0)
                    {
                        newVersionMajor = baseVersion.Major;
                        newVersionMinor = baseVersion.Minor + 1;
                        newVersionPatch = 0;
                    }
                    else
                    {
                        newVersionMajor = baseVersion.Major + 1;
                        newVersionMinor = 0;
                        newVersionPatch = 0;
                    }
                }
            }

            // FBX field for creator mode.
            EditorGUILayout.PropertyField(customFbxForCreatorProp, new GUIContent("Custom FBX (Transformed)"));
            if (ultipawAvatarForCreatorProp != null)
            {
                EditorGUILayout.PropertyField(ultipawAvatarForCreatorProp, new GUIContent("UltiPaw Avatar (Transformed)"));
            }
            EditorGUILayout.PropertyField(avatarLogicPrefabProp, new GUIContent("Avatar Logic Prefab"));

            // --- Parent Version Dropdown ---
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Parent Version:", GUILayout.Width(100));
            
            int currentParentPopupIndex = (selectedParentVersionIndex == -1) ? defaultParentIndex : selectedParentVersionIndex;

            EditorGUI.BeginChangeCheck();
            int newParentIndex = EditorGUILayout.Popup(currentParentPopupIndex, parentVersionDisplayOptions.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                selectedParentVersionIndex = newParentIndex;
                if (selectedParentVersionIndex > 0) // If not "None"
                {
                    selectedParentVersionObject = compatibleParentVersions[selectedParentVersionIndex - 1];
                }
                else
                {
                    selectedParentVersionObject = null; // "None" selected
                }
            } else {
                if (selectedParentVersionIndex == -1 && defaultParentIndex > 0)
                {
                    selectedParentVersionObject = compatibleParentVersions[defaultParentIndex - 1];
                }
                else if (selectedParentVersionIndex == 0)
                {
                    selectedParentVersionObject = null;
                }
            }
            EditorGUILayout.EndHorizontal();


            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New Version Details:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Version:", GUILayout.Width(50));
            newVersionMajor = EditorGUILayout.IntField(newVersionMajor, GUILayout.Width(40));
            EditorGUILayout.LabelField(".", GUILayout.Width(10));
            newVersionMinor = EditorGUILayout.IntField(newVersionMinor, GUILayout.Width(40));
            EditorGUILayout.LabelField(".", GUILayout.Width(10));
            newVersionPatch = EditorGUILayout.IntField(newVersionPatch, GUILayout.Width(40));
            newVersionScope = (Scope)EditorGUILayout.EnumPopup("", newVersionScope, GUILayout.Width(100)); 
            EditorGUILayout.EndHorizontal();
            
            // --- Validation Logic for Version ---
            bool isVersionValid = false;
            string newVersionString = $"{newVersionMajor}.{newVersionMinor}.{newVersionPatch}";
            string appliedVersionString = ultiPaw.appliedUltiPawVersion?.version;
            
            if (string.IsNullOrEmpty(appliedVersionString))
            {
                isVersionValid = true; // No applied version, so any new version is fine.
            }
            else
            {
                isVersionValid = CompareVersions(newVersionString, appliedVersionString) > 0;
            }

            if (!isVersionValid)
            {
                EditorGUILayout.HelpBox($"New version must be higher than the currently applied version (v{appliedVersionString}).", MessageType.Warning);
            }

            EditorGUILayout.LabelField("Changelog:", EditorStyles.miniBoldLabel);
            newChangelog = EditorGUILayout.TextArea(newChangelog, GUILayout.Height(80));

            EditorGUILayout.Space();
            GUI.enabled = !isSubmitting && customFbxForCreatorProp.objectReferenceValue != null && 
                          avatarLogicPrefabProp.objectReferenceValue != null &&
                          ultipawAvatarForCreatorProp.objectReferenceValue != null && 
                          isVersionValid;
            
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button(isSubmitting ? "Submitting..." : "Submit New Version", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("Confirm Upload",
                        "This will create the new version files locally and upload them to the server.\n\n" +
                        $"Version: {newVersionMajor}.{newVersionMinor}.{newVersionPatch} ({newVersionScope})\n" +
                        (selectedParentVersionObject != null ? $"Parent: {selectedParentVersionObject.version}\n" : "") +
                        "This action is irreversible.", "Upload", "Cancel"))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(SubmitNewVersionCoroutine());
                }
            }
            GUI.enabled = true; // Re-enable GUI after button
            GUI.backgroundColor = Color.white; // Reset background color

            if (!string.IsNullOrEmpty(submitError))
            {
                EditorGUILayout.HelpBox("Submission Error: " + submitError, MessageType.Error);
            }
        }
        EditorGUILayout.EndVertical();
    }

    protected void PopulateParentVersionDropdownForCreatorMode()
    {
        compatibleParentVersions = new List<UltiPawVersion>();
        parentVersionDisplayOptions = new List<string> {};
        defaultParentIndex = 0;

        if (serverVersions.Count > 0)
        {
            var filtered = serverVersions
                .OrderByDescending(v => ParseVersion(v.version))
                .ToList();

            compatibleParentVersions.AddRange(filtered);

            for (int i = 0; i < compatibleParentVersions.Count; i++)
            {
                var ver = compatibleParentVersions[i];
                parentVersionDisplayOptions.Add($"{ver.version} ({ver.scope})");
                if (ultiPaw.appliedUltiPawVersion != null && ultiPaw.appliedUltiPawVersion.Equals(ver))
                {
                    defaultParentIndex = i;
                }
            }
        }
    }


    // --- Logic Methods ---

    // Helper for version comparison
    private static Version ParseVersion(string versionString)
    {
        try
        {
            // Handle simple versions like "0.1" which System.Version doesn't like directly
            if (versionString != null && !versionString.Contains('.'))
            {
                versionString += ".0"; // Append .0 if no dot exists
            }
            // Pad with .0 if only one dot exists (e.g., "1.2" -> "1.2.0")
            else if (versionString != null && versionString.Count(c => c == '.') == 1)
            {
                 versionString += ".0";
            }

            if (Version.TryParse(versionString, out Version ver))
            {
                return ver;
            }
        }
        catch (Exception ex) {
             Debug.LogWarning($"[UltiPawEditor] Could not parse version string '{versionString}': {ex.Message}");
        }
        return new Version(0, 0); // Fallback version
    }

    // Compares two version strings (v1 > v2 -> positive, v1 < v2 -> negative, v1 == v2 -> 0)
    private static int CompareVersions(string v1, string v2)
    {
        Version ver1 = ParseVersion(v1);
        Version ver2 = ParseVersion(v2);
        return ver1.CompareTo(ver2);
    }


    private void StartVersionFetch()
    {
        if (isFetching) return;
        isFetching = true;
        fetchError = ""; // Clear previous errors
        downloadError = "";
        deleteError = "";
        Repaint(); // Show "Fetching..."
        EditorCoroutineUtility.StartCoroutineOwnerless(FetchVersionsCoroutine());
    }

    private void DownloadVersion(UltiPawVersion versionToDownload, bool applyAfterDownload)
    {
        if (isDownloading || isFetching || isDeleting) return;

        if (string.IsNullOrEmpty(versionToDownload?.version) || string.IsNullOrEmpty(versionToDownload.defaultAviVersion)) {
            downloadError = "Version data is missing required fields. Cannot download.";
            Debug.LogError("[UltiPawEditor] " + downloadError);
            Repaint();
            return;
        }

        downloadError = "";
        isDownloading = true;
        Repaint();
        EditorCoroutineUtility.StartCoroutineOwnerless(DownloadVersionCoroutine(versionToDownload, applyAfterDownload)); // false = don't apply after download
    }

    // New method to start deletion
    private void StartVersionDelete(UltiPawVersion versionToDelete)
    {
        if (isDownloading || isFetching || isDeleting) return;

        string dataPath = UltiPawUtils.GetVersionDataPath(versionToDelete.version, versionToDelete.defaultAviVersion);
        if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
        {
            deleteError = $"Version folder not found for {versionToDelete.version}. Cannot delete.";
            Debug.LogWarning("[UltiPawEditor] " + deleteError);
            Repaint();
            return;
        }

        deleteError = "";
        isDeleting = true;
        Repaint();
        EditorCoroutineUtility.StartCoroutineOwnerless(DeleteFolderCoroutine(dataPath));
    }

    private IEnumerator FetchVersionsCoroutine()
    {
        // Ensure hash is up-to-date *before* fetching
        // This might have already been called, but ensure it's current
        ultiPaw.UpdateCurrentBaseFbxHash();
        string baseFbxHash = ultiPaw.GetCurrentBaseFbxHash() ?? hashToFetch; // Fallback to current hash if not found

        if (string.IsNullOrEmpty(baseFbxHash))
        {
            // No FBX, clear results and stop fetching
            serverVersions.Clear();
            recommendedVersion = null;
            recommendedVersionGuid = "";
            hashToFetch = null;
            fetchError = ""; // Clear error, UI shows "Assign FBX"
            isFetching = false;
            Repaint();
            yield break; // Exit coroutine
        }

        // --- Proceed with fetch ---
        fetchError = "";
        downloadError = "";
        deleteError = "";
        hashToFetch = baseFbxHash; // Store hash used for this fetch

        string url = $"{UltiPawUtils.SERVER_BASE_URL}{UltiPawUtils.VERSION_ENDPOINT}?s={UltiPaw.SCRIPT_VERSION}&d={baseFbxHash}&t={UltiPawUtils.GetAuth().token}";
        Debug.Log($"[UltiPawEditor] Fetching versions from: {url}");

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            var op = req.SendWebRequest();
            while (!op.isDone)
                yield return null;

            isFetching = false; // Mark fetch as complete

            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
            {
                HandleFetchError(req);
            }
            else // Success
            {
                try
                {
                    string json = req.downloadHandler.text;
                    UltiPawVersionResponse response = JsonConvert.DeserializeObject<UltiPawVersionResponse>(json);

                    if (response != null && response.versions != null)
                    {
                        serverVersions = response.versions ?? new List<UltiPawVersion>(); // Ensure list is not null
                        ultiPaw.serverVersions = serverVersions; // Update component reference
                        recommendedVersionGuid = response.recommendedVersion; // Store the recommended version string
                        fetchError = ""; // Clear error on success

                        // Find the full details for the recommended version
                        recommendedVersion = serverVersions.FirstOrDefault(v => v != null && v.version == recommendedVersionGuid);

                        // --- Smart Selection Logic ---
                        // 1. Try to keep the currently selected UI version if it's still valid in the new list
                        UltiPawVersion versionToKeepSelected = null;
                        if (selectedVersion != null)
                        {
                            var currentSelectedInNewList = serverVersions.FirstOrDefault(v => v != null && v.Equals(selectedVersion));
                            if (currentSelectedInNewList != null)
                            {
                                versionToKeepSelected = currentSelectedInNewList; // Keep current UI selection
                            }
                        }

                        // 2. If no UI selection or it's invalid, select the *applied* version if it exists in the list
                        if (versionToKeepSelected == null && ultiPaw.appliedUltiPawVersion != null)
                        {
                             var appliedInNewList = serverVersions.FirstOrDefault(v => v != null && v.Equals(ultiPaw.appliedUltiPawVersion));
                             if (appliedInNewList != null)
                             {
                                 versionToKeepSelected = appliedInNewList;
                             }
                        }

                        // 3. If still nothing selected, select the recommended version *if it's downloaded*
                        if (versionToKeepSelected == null && recommendedVersion != null)
                        {
                            string binPath = UltiPawUtils.GetVersionBinPath(recommendedVersion.version, recommendedVersion.defaultAviVersion);
                            if (!string.IsNullOrEmpty(binPath) && File.Exists(binPath))
                            {
                                versionToKeepSelected = recommendedVersion;
                            }
                        }

                        // 4. Apply the determined selection (or clear if none found/valid)
                        if (versionToKeepSelected != null)
                        {
                            // Only call SelectVersion if it's different from the current UI selection
                            if (selectedVersion == null || !selectedVersion.Equals(versionToKeepSelected))
                            {
                                string binPath = UltiPawUtils.GetVersionBinPath(versionToKeepSelected.version, versionToKeepSelected.defaultAviVersion);
                                // Select only if downloaded (or if it's the applied version)
                                if ((!string.IsNullOrEmpty(binPath) && File.Exists(binPath)) || (ultiPaw.appliedUltiPawVersion != null && ultiPaw.appliedUltiPawVersion.Equals(versionToKeepSelected)))
                                {
                                     SelectVersion(versionToKeepSelected, binPath); // binPath might be null/invalid if selecting the applied version which isn't downloaded anymore, handle in SelectVersion
                                }
                                else if (selectedVersion != null)
                                {
                                     // Previous selection is no longer valid/downloaded, clear it
                                     ClearSelection();
                                }
                            }
                            // If it's the same, ensure the reference is updated from the new list
                            else if (selectedVersion != versionToKeepSelected)
                            {
                                selectedVersion = versionToKeepSelected; // Update internal reference
                                ultiPaw.activeUltiPawVersion = versionToKeepSelected; // Update component reference
                                // No need to call SelectVersion logic again
                            }
                        }
                        else if (selectedVersion != null) // No valid version to keep selected, clear previous selection
                        {
                            ClearSelection(); 
                        }
                        // --- End Smart Selection Logic ---
                        PopulateParentVersionDropdownForCreatorMode();
                    }
                    else
                    {
                        fetchError = "Failed to parse server response (JSON structure might be incorrect).";
                        Debug.LogError("[UltiPawEditor] JSON parsing error or null response/versions. Check server response format.");
                        serverVersions.Clear();
                        recommendedVersion = null;
                        recommendedVersionGuid = "";
                        ClearSelection();
                    }
                }
                catch (Exception e)
                {
                    fetchError = $"Exception parsing server response: {e.Message}";
                    Debug.LogError($"[UltiPawEditor] Exception: {e}");
                    serverVersions.Clear();
                    recommendedVersion = null;
                    recommendedVersionGuid = "";
                    ClearSelection();
                }
            }
        } // Dispose UnityWebRequest

        Repaint();
    }

    // Modified Download Coroutine to optionally apply after download
    private IEnumerator DownloadVersionCoroutine(UltiPawVersion versionToDownload, bool applyAfterDownload)
    {
        // --- 1. Setup ---
        string baseFbxHashForQuery = ultiPaw.GetCurrentBaseFbxHash() ?? hashToFetch; // Use last fetched hash for consistency
        if(baseFbxHashForQuery == null)
        {
            Debug.LogError("[UltiPawEditor] Failed to fetch hash for download. Cannot download.");
            downloadError = "Failed to fetch hash for download. Cannot download.";
            EditorUtility.DisplayDialog("Error", downloadError, "OK");
            isDownloading = false;
            Repaint();
            yield break;
        }
        string downloadUrl = $"{UltiPawUtils.SERVER_BASE_URL}{UltiPawUtils.MODEL_ENDPOINT}?version={UnityWebRequest.EscapeURL(versionToDownload.version)}&d={baseFbxHashForQuery}&t={UltiPawUtils.GetAuth().token}";
        string targetExtractFolder = UltiPawUtils.VERSIONS_FOLDER;
        string targetZipPath = $"{targetExtractFolder}/temp.zip";

        // Initial validation and directory setup
        if (string.IsNullOrEmpty(targetExtractFolder))
        {
            downloadError = "Could not determine target folder path. Version data might be invalid.";
            Debug.LogError("[UltiPawEditor] " + downloadError);
            isDownloading = false; // Ensure flag is reset on early exit
            Repaint();
            yield break; // Exit coroutine
        }
        try
        {
            UltiPawUtils.EnsureDirectoryExists(targetExtractFolder);
            UltiPawUtils.EnsureDirectoryExists(Path.GetDirectoryName(targetZipPath));
        }
        catch (Exception ex)
        {
            downloadError = $"Directory setup failed: {ex.Message}";
            Debug.LogError("[UltiPawEditor] " + downloadError);
            isDownloading = false; // Ensure flag is reset on early exit
            Repaint();
            yield break; // Exit coroutine
        }


        Debug.Log($"[UltiPawEditor] Starting download for version {versionToDownload.version} (Base: {versionToDownload.defaultAviVersion})");
        Debug.Log($"[UltiPawEditor] Download URL: {downloadUrl}");
        Debug.Log($"[UltiPawEditor] Target Extract Folder: {targetExtractFolder}");

        downloadError = ""; // Clear previous errors
        isDownloading = true; // Flag is already set by caller, but ensure it's true
        Repaint();

        // --- 2. Create Request Objects ---
        UnityWebRequest req = null;
        DownloadHandlerFile downloadHandler = null;
        UnityWebRequestAsyncOperation op = null;
        bool setupOk = false;

        try // Minimal try block JUST for setup that might fail before yield
        {
            if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
            downloadHandler = new DownloadHandlerFile(targetZipPath);
            req = new UnityWebRequest(downloadUrl, UnityWebRequest.kHttpVerbGET, downloadHandler, null);
            setupOk = true;
        }
        catch (Exception ex)
        {
            downloadError = $"Download setup failed: {ex.Message}";
            Debug.LogError($"[UltiPawEditor] {downloadError}");
            // Cleanup partially created objects if setup fails
            downloadHandler?.Dispose();
            req?.Dispose(); // Dispose req if created before exception
            isDownloading = false; // Reset flag
            Repaint();
            yield break; // Exit coroutine
        }

        // --- 3. Send Request and Yield (Only if setup succeeded) ---
        // This section is NOT inside a try block with a 'finally' that disposes req/handler
        if (setupOk && req != null)
        {
            op = req.SendWebRequest();
            while (!op.isDone)
            {
                yield return null; // Wait for download
            }
        }

        // --- 4. Process Result, Extract, and Cleanup (Uses try/finally) ---
        bool downloadSucceeded = false;
        bool extractionSucceeded = false;
        try
        {
            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[UltiPawEditor] Successfully downloaded ZIP to: {targetZipPath}");
                downloadSucceeded = true;
            }
            else
            {
                downloadError = $"Download failed : make sure you have a supported winterpaw and a stable connection. Else contact Orbit";
                Debug.LogError($"[UltiPawEditor] {downloadError}");
            }


            // --- 5. Extraction (Only if download succeeded) ---
            if (downloadSucceeded)
            {
                // Dispose handler *before* extraction, but keep req alive for now
                downloadHandler?.Dispose();
                downloadHandler = null; // Prevent double disposal in finally

                try // Separate try/catch specifically for extraction errors
                {
                    ZipFile.ExtractToDirectory(targetZipPath, targetExtractFolder, true);
                    Debug.Log($"[UltiPawEditor] Successfully extracted files to: {targetExtractFolder}");
                    extractionSucceeded = true;
                    AssetDatabase.Refresh(); // Refresh assets
                }
                catch (Exception ex)
                {
                    downloadError = $"Extraction failed: {ex.Message}"; // Set specific error
                    Debug.LogError($"[UltiPawEditor] Failed to extract ZIP file '{targetZipPath}' to '{targetExtractFolder}': {ex}");
                    AssetDatabase.Refresh();
                }
            }

            // --- 6. Post-Download Actions (Select / Apply) ---
            if (extractionSucceeded) // Only proceed if extraction was also successful
            {
                string expectedBinPath = UltiPawUtils.GetVersionBinPath(versionToDownload.version, versionToDownload.defaultAviVersion);
                if (!string.IsNullOrEmpty(expectedBinPath) && File.Exists(expectedBinPath))
                {
                    if (applyAfterDownload)
                    {
                        SelectVersion(versionToDownload, expectedBinPath);
                        if (ultiPaw.ApplyUltiPaw())
                        {
                            Debug.Log($"[UltiPawEditor] Successfully applied version {versionToDownload.version} after download.");
                        }
                        else
                        {
                            downloadError = $"Downloaded version {versionToDownload.version}, but failed to apply it. Check console.";
                        }
                    }
                    else
                    {
                        SelectVersion(versionToDownload, expectedBinPath);
                    }
                }
                else
                {
                    downloadError = "Extraction seemed successful, but failed to find required file (ultipaw.bin) for selection.";
                    Debug.LogError("[UltiPawEditor] " + downloadError);
                }
            }
        }
        finally // This block ensures final disposal and cleanup happens
        {
            isDownloading = false; // Mark process finished *reliably*

            // Dispose remaining resources
            downloadHandler?.Dispose(); // Dispose if extraction failed before it could be disposed
            req?.Dispose();

            // Clean up temp zip file
            if (File.Exists(targetZipPath))
            {
                try { File.Delete(targetZipPath); }
                catch (IOException ioEx) { Debug.LogWarning($"[UltiPawEditor] Could not delete temp file '{targetZipPath}' (might be locked): {ioEx.Message}"); }
                catch (Exception ex) { Debug.LogWarning($"[UltiPawEditor] Error deleting temp file '{targetZipPath}': {ex.Message}"); }
            }

            Repaint();
        }
    }
    
    private IEnumerator DeleteFolderCoroutine(string path)
    {
        yield return null; // Wait a frame

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true); // Recursive delete
                File.Delete(path + ".meta");

                Debug.Log($"[UltiPawEditor] Successfully deleted folder: {path}");
                AssetDatabase.Refresh(); // Make Unity forget the files

                deleteError = ""; // Clear error on success
            }
            else
            {
                 deleteError = $"Directory not found, cannot delete: {path}";
                 Debug.LogWarning("[UltiPawEditor] " + deleteError);
            }
        }
        catch (Exception ex)
        {
            deleteError = $"Failed to delete directory '{path}': {ex.Message}";
            Debug.LogError("[UltiPawEditor] " + deleteError);
            AssetDatabase.Refresh(); // Refresh anyway
        }
        finally
        {
            isDeleting = false; 
            Repaint();
        }
    }


    private void HandleFetchError(UnityWebRequest req)
    {
        // (Existing code - no changes needed)
        if (req.responseCode == 406)
        {
            fetchError = $"Server rejected request [{req.responseCode}]: {req.downloadHandler?.text ?? req.error}";
            Debug.LogError($"[UltiPawEditor] Server Error 406: {fetchError}");
        }
        else
        {
            fetchError = $"Network Error [{req.responseCode} {req.result}]: {req.error}";
            Debug.LogError($"[UltiPawEditor] Fetch Error: {fetchError}");
        }
        serverVersions.Clear();
        recommendedVersion = null;
        recommendedVersionGuid = "";
        // Don't clear lastFetchedHash here, it might be useful for context
        ClearSelection(); // Clear UI selection on fetch error
    }

    // Selects a version in the UI, updating component state
    private void SelectVersion(UltiPawVersion version, string binPath)
    {
        if (version == null || ultiPaw == null) return;
        if (selectedVersion != null && selectedVersion.Equals(version)) return;
        
        selectedVersion = version;
        Undo.RecordObject(ultiPaw, "Select UltiPaw Version");
        
        // Update the bin path only if it's valid, otherwise clear it
        ultiPaw.selectedUltiPawBinPath = !string.IsNullOrEmpty(binPath) ? binPath : null;

        EditorUtility.SetDirty(ultiPaw);
        serializedObject.Update(); // Update serialized object to reflect component changes
        Debug.Log($"[UltiPawEditor] Selected version for next action: {version.version}");
        Repaint();
    }

    private void ClearSelection() {
        if (selectedVersion == null) return; 
        selectedVersion = null; 
        
        // Clear component's active version and path if it matches the cleared UI selection
        if (ultiPaw != null && ultiPaw.activeUltiPawVersion != null && ultiPaw.activeUltiPawVersion.Equals(selectedVersion)) // Check equality properly
        {
            Undo.RecordObject(ultiPaw, "Clear UltiPaw Selection");
            ultiPaw.activeUltiPawVersion = null;
            ultiPaw.selectedUltiPawBinPath = null;

            // Optionally clear blendshapes if nothing is applied?
            // Let's keep the blendshapes related to the *applied* version if one exists.
            // If no version is applied, then clearing selection should clear blendshapes.
            if (ultiPaw.appliedUltiPawVersion == null && ultiPaw.blendShapeNames.Count > 0) {
                 ultiPaw.blendShapeNames.Clear();
                 ultiPaw.blendShapeValues.Clear();
            }

            EditorUtility.SetDirty(ultiPaw);
            serializedObject.Update();
        }
        Repaint();
     }

    // Helper to select a version and immediately try to apply it
    private void SelectAndApplyVersion(UltiPawVersion version, string binPath)
    {
        if (version == null || ultiPaw == null) return;

        SelectVersion(version, binPath);

        // Ensure selection was successful and bin path is valid before attempting transform
        if (selectedVersion != null && selectedVersion.Equals(version) &&
            !string.IsNullOrEmpty(ultiPaw.selectedUltiPawBinPath) && File.Exists(ultiPaw.selectedUltiPawBinPath))
        {
            // Attempt to apply the now-selected version
            if (ultiPaw.ApplyUltiPaw())
            {
                 Debug.Log($"[UltiPawEditor] Successfully applied version {version.version}.");
            }
            else
            {
                 Debug.LogError($"[UltiPawEditor] Failed to apply version {version.version} after selecting it.");
            }
        }
        else
        {
             Debug.LogError($"[UltiPawEditor] Cannot apply version {version.version}. Selection failed or bin path is invalid.");
        }
        Repaint(); 
    }


    private bool AnyBackupExists(List<GameObject> files)
    {
        // (Existing code - no changes needed)
        if (files == null) return false;
        return files.Any(file => file != null && File.Exists(AssetDatabase.GetAssetPath(file) + ".old"));
    }
    
    
    // UTILS
    
    private String f(Action action, String returnValue)
    {
        action();
        return returnValue;
    }

    // --- NEW: Creator Mode Logic ---
    

    private IEnumerator SubmitNewVersionCoroutine()
    {
        isSubmitting = true;
        submitError = "";
        Repaint();

        string newVersionString = $"{newVersionMajor}.{newVersionMinor}.{newVersionPatch}";

        // --- 1. Validation ---
        string originalFbxForXorPath = ultiPaw.GetCurrentOriginalBaseFbxPath();
        if (string.IsNullOrEmpty(originalFbxForXorPath) || !originalFbxForXorPath.EndsWith(UltiPaw.ORIGINAL_SUFFIX))
        {
            submitError = "An original FBX backup (.fbx.old) must exist to create a new version. Please apply an existing UltiPaw version first.";
            isSubmitting = false; Repaint(); yield break;
        }

        if (customFbxForCreatorProp.objectReferenceValue == null 
            || ultipawAvatarForCreatorProp.objectReferenceValue == null 
            || avatarLogicPrefabProp.objectReferenceValue == null
            )
        {
            submitError = "All creator mode fields must be assigned.";
            isSubmitting = false; Repaint(); yield break;
        }

        if (ultiPaw.appliedUltiPawVersion == null)
        {
            submitError = "An existing UltiPaw version must be applied to determine the base FBX version for the new folder name.";
            isSubmitting = false; Repaint(); yield break;
        }
        
        string baseFbxVersion = ultiPaw.appliedUltiPawVersion.defaultAviVersion;
        if(string.IsNullOrEmpty(baseFbxVersion))
        {
            submitError = "The currently applied UltiPaw version does not have a 'defaultAviVersion'. Cannot proceed.";
            isSubmitting = false; Repaint(); yield break;
        }

        // --- 2. Setup Paths ---
        string newVersionDataPath = UltiPawUtils.GetVersionDataPath(newVersionString, baseFbxVersion);
        string newVersionDataFullPath = Path.GetFullPath(newVersionDataPath);
        string tempZipPath = Path.Combine(Path.GetTempPath(), $"ultipaw_upload_{Guid.NewGuid()}.zip");

        // --- 3. Create Local Version Folder and Files (Setup Phase) ---
        bool fileCreationSucceeded = false;
        try
        {
            UltiPawUtils.EnsureDirectoryExists(newVersionDataPath, canBeFilePath: false);

            // Copy the transformed avatar asset provided by the user
            string ultipawAvatarSourcePath = AssetDatabase.GetAssetPath(ultipawAvatarForCreatorProp.objectReferenceValue);
            AssetDatabase.CopyAsset(ultipawAvatarSourcePath, Path.Combine(newVersionDataPath, UltiPawUtils.ULTIPAW_AVATAR_NAME));

            // Copy the DEFAULT avatar from the PARENT version, which is the currently applied one.
            var parentVersion = ultiPaw.appliedUltiPawVersion;
            string defaultAvatarSourcePath = UltiPawUtils.GetVersionAvatarPath(parentVersion.version, parentVersion.defaultAviVersion, UltiPawUtils.DEFAULT_AVATAR_NAME);
            if (string.IsNullOrEmpty(defaultAvatarSourcePath) || !File.Exists(defaultAvatarSourcePath))
            {
                throw new FileNotFoundException($"Could not find the required 'default avatar.asset' from the parent version '{parentVersion.version}'. Path checked: {defaultAvatarSourcePath}");
            }
            AssetDatabase.CopyAsset(defaultAvatarSourcePath, Path.Combine(newVersionDataPath, UltiPawUtils.DEFAULT_AVATAR_NAME));
            Debug.Log($"[UltiPaw-Creator] Copied avatar assets to {newVersionDataPath}");

            // Encrypt FBX to create ultipaw.bin
            string transformedFbxPath = AssetDatabase.GetAssetPath(customFbxForCreatorProp.objectReferenceValue);
            byte[] keyData = File.ReadAllBytes(originalFbxForXorPath);
            byte[] targetData = File.ReadAllBytes(transformedFbxPath);
            byte[] encryptedData = new byte[targetData.Length];
            for (int i = 0; i < targetData.Length; i++)
            {
                encryptedData[i] = (byte)(targetData[i] ^ keyData[i % keyData.Length]);
            }
            File.WriteAllBytes(Path.Combine(newVersionDataPath, "ultipaw.bin"), encryptedData);
            Debug.Log($"[UltiPaw-Creator] Encrypted FBX and saved as ultipaw.bin");
            
            // ** NEW ** Copy the logic prefab itself into the version folder
            string prefabSourcePath = AssetDatabase.GetAssetPath(avatarLogicPrefabProp.objectReferenceValue);
            string prefabDestPath = Path.Combine(newVersionDataPath, "ultipaw logic.prefab");
            AssetDatabase.CopyAsset(prefabSourcePath, prefabDestPath);
            Debug.Log($"[UltiPaw-Creator] Copied logic prefab to {prefabDestPath}");

            // Create .unitypackage containing the prefab and its dependencies
            string[] dependencies = AssetDatabase.GetDependencies(prefabSourcePath, true);
            string packagePath = Path.Combine(newVersionDataPath, "ultipaw logic.unitypackage");
            AssetDatabase.ExportPackage(dependencies, packagePath, ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
            Debug.Log($"[UltiPaw-Creator] Exported logic prefab package to {packagePath}");

            fileCreationSucceeded = true;
        }
        catch (Exception ex)
        {
            submitError = $"Failed during local file creation: {ex.Message}";
            Debug.LogError($"[UltiPaw-Creator] Error during file preparation: {ex}");
            isSubmitting = false; 
            Repaint(); 
            yield break;
        }

        // --- 4. Asset Processing Phase (Outside try-catch to allow yields) ---
        if (fileCreationSucceeded)
        {
            AssetDatabase.Refresh();
            yield return null; // Wait for asset processing

            // Create ZIP file (separate try-catch for zip creation)
            bool zipCreationSucceeded = false;
            try
            {
                if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                ZipFile.CreateFromDirectory(newVersionDataFullPath, tempZipPath, CompressionLevel.Optimal, false);
                Debug.Log($"[UltiPaw-Creator] Created temporary zip for upload: {tempZipPath}");
                zipCreationSucceeded = true;
            }
            catch (Exception ex)
            {
                submitError = $"Failed to create ZIP file: {ex.Message}";
                Debug.LogError($"[UltiPaw-Creator] Error during ZIP creation: {ex}");
                isSubmitting = false;
                Repaint();
                yield break;
            }

            if (!zipCreationSucceeded || !File.Exists(tempZipPath))
            {
                submitError = "Failed to create ZIP file for upload.";
                isSubmitting = false; 
                Repaint(); 
                yield break;
            }
        }

        // --- 5. Upload Setup Phase (Minimal try block for setup) ---
        UnityWebRequest req = null;
        UnityWebRequestAsyncOperation op = null;
        bool uploadSetupSucceeded = false;

        try
        {
            string uploadUrl = $"{UltiPawUtils.SERVER_BASE_URL}{UltiPawUtils.NEW_VERSION_ENDPOINT}";
            byte[] fileBytes = File.ReadAllBytes(tempZipPath);
            string zipFileName = Path.GetFileName(newVersionDataFullPath) + ".zip"; // e.g., u0.3d1.5.zip

            WWWForm form = new WWWForm();
            form.AddBinaryData("packageFile", fileBytes, zipFileName, "application/zip");
            
            var metadata = new Dictionary<string, string>
            {
                { "baseFbxHash", ultiPaw.currentOriginalBaseFbxHash }, // Hash of the .old file
                { "version", newVersionString },
                { "scope", newVersionScope.ToString() },
                { "changelog", newChangelog },
                { "defaultAviVersion", baseFbxVersion }
            };
            if (selectedParentVersionObject != null)
            {
                metadata.Add("parentVersion", selectedParentVersionObject.version);
            }
            string metadataJson = JsonConvert.SerializeObject(metadata);
            form.AddField("metadata", metadataJson);

            req = UnityWebRequest.Post(uploadUrl, form);
            req.SetRequestHeader("Authorization", $"Bearer {UltiPawUtils.GetAuth().token}");
            req.timeout = 300;

            uploadSetupSucceeded = true;
        }
        catch (Exception ex)
        {
            submitError = $"Upload setup failed: {ex.Message}";
            Debug.LogError($"[UltiPaw-Creator] Error during upload setup: {ex}");
            req?.Dispose();
            isSubmitting = false;
            CleanupTempZip(tempZipPath);
            Repaint();
            yield break;
        }

        // --- 6. Send Request Phase (Outside try-catch to allow yields) ---
        if (uploadSetupSucceeded && req != null)
        {
            string zipFileName = Path.GetFileName(newVersionDataFullPath) + ".zip";
            op = req.SendWebRequest();
            while (!op.isDone)
            {
                EditorUtility.DisplayProgressBar("Uploading UltiPaw Version", $"Uploading {zipFileName}...", op.progress);
                yield return null;
            }
            EditorUtility.ClearProgressBar();
        }

        // --- 7. Process Upload Result and Cleanup (Uses try/finally) ---
        bool uploadSucceeded = false;
        try
        {
            if (req != null)
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[UltiPaw-Creator] Upload successful! Server Response: {req.downloadHandler.text}");
                    EditorUtility.DisplayDialog("Upload Successful!", $"New UltiPaw version {newVersionString} has been uploaded.", "OK");
                    uploadSucceeded = true;
                    StartVersionFetch();
                }
                else
                {
                    submitError = $"Upload failed: {req.error} | Response: {req.downloadHandler.text}";
                    Debug.LogError($"[UltiPaw-Creator] {submitError}");
                }
            }
        }
        catch (Exception ex)
        {
            submitError = $"An exception occurred during upload processing: {ex.Message}";
            Debug.LogError($"[UltiPaw-Creator] {submitError}");
        }
        finally
        {
            isSubmitting = false;
            req?.Dispose();
            CleanupTempZip(tempZipPath);
            Repaint();
        }
    }

    // Helper method for temp zip cleanup
    private void CleanupTempZip(string tempZipPath)
    {
        if (File.Exists(tempZipPath))
        {
            try { File.Delete(tempZipPath); }
            catch (IOException ioEx) { Debug.LogWarning($"[UltiPaw-Creator] Could not delete temp zip file '{tempZipPath}' (might be locked): {ioEx.Message}"); }
            catch (Exception ex) { Debug.LogWarning($"[UltiPaw-Creator] Error deleting temp zip file '{tempZipPath}': {ex.Message}"); }
        }
    }

    // Helper to calculate MD5 hash of a file
    private string CalculateMD5(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"[UltiPawEditor-Creator] File not found for MD5 hash: {filePath}");
            return null;
        }

        try
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UltiPawEditor-Creator] Error calculating MD5 for {filePath}: {ex.Message}");
            return null;
        }
    }
}
#endif