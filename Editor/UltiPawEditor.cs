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
using UnityEngine.UIElements; // Needed for ZipFile

[CustomEditor(typeof(UltiPaw))]
public class UltiPawEditor : Editor
{
    private Texture2D bannerTexture;
    private SerializedProperty baseFbxFilesProp;
    private SerializedProperty specifyCustomBaseFbxProp;
    private SerializedProperty blendShapeNamesProp;
    private SerializedProperty blendShapeValuesProp;
    private SerializedProperty appliedUltiPawVersionProp; // Serialized property for applied version

    // Version management state
    private List<UltiPawVersion> serverVersions = new List<UltiPawVersion>();
    private string recommendedVersionGuid = ""; // Stores the version string (e.g., "1.2.3")
    private UltiPawVersion selectedVersion = null; // Version selected in the UI for potential application
    private UltiPawVersion recommendedVersionDetails = null; // Full details of the recommended version

    private bool isFetching = false;
    private bool isDownloading = false;
    private bool isDeleting = false; // State for deletion process
    private string fetchError = "";
    private string downloadError = "";
    private string deleteError = ""; // Error specific to deletion
    private string lastFetchedHash = null;
    private bool versionsFoldout = true; // Default to open

    // Server settings
    private const string serverBaseUrl = "http://orbiters.cc:8080"; // Update with your server URL
    private const string versionsEndpoint = "/ultipaw/getVersions";
    private const string modelEndpoint = "/ultipaw/getModel";

    private static readonly Color OrangeColor = new Color(1.0f, 0.65f, 0.0f); // For Downgrade button
    private static readonly Color BACKGROUND_COLOR = new Color(0.28f, 0.28f, 0.28f);

    private void OnEnable()
    {
        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(UltiPawUtils.BASE_FOLDER + "/Editor/banner.png");
        baseFbxFilesProp = serializedObject.FindProperty("baseFbxFiles");
        specifyCustomBaseFbxProp = serializedObject.FindProperty("specifyCustomBaseFbx");
        blendShapeNamesProp = serializedObject.FindProperty("blendShapeNames");
        blendShapeValuesProp = serializedObject.FindProperty("blendShapeValues");
        appliedUltiPawVersionProp = serializedObject.FindProperty("appliedUltiPawVersion"); // Get applied version property

        UltiPaw ultiPaw = (UltiPaw)target;

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
        // Fetch if hash is now available AND server versions haven't been loaded
        // Or if the hash changed since last fetch
        if (!string.IsNullOrEmpty(ultiPaw.currentBaseFbxHash) && (serverVersions.Count == 0 || ultiPaw.currentBaseFbxHash != lastFetchedHash))
        {
            StartVersionFetch(ultiPaw);
        }
        // Also ensure the isUltiPaw state is correct based on the potentially loaded applied version
        else if (ultiPaw.UpdateIsUltiPawState()) // Check if state changed
        {
             Repaint(); // Repaint if state changed
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
        serializedObject.Update(); // Always start with Update
        UltiPaw ultiPaw = (UltiPaw)target;

        // --- Hash Check ---
        // Check if FBX changed and update hash if necessary. This is less frequent than every frame.
        bool fbxPathChanged = false;
        if (ultiPaw.baseFbxFiles != null && ultiPaw.baseFbxFiles.Count > 0 && ultiPaw.baseFbxFiles[0] != null)
        {
            string currentPath = AssetDatabase.GetAssetPath(ultiPaw.baseFbxFiles[0]);
            if (currentPath != ultiPaw.currentBaseFbxPath)
            {
                ultiPaw.currentBaseFbxPath = currentPath; // Update cached path
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
                 StartVersionFetch(ultiPaw);
             }
        }
        // --- End Hash Check ---


        DrawBanner();
        DrawFileConfiguration(ultiPaw); // This might trigger auto-detect, which calls hash update/fetch

        DrawVersionManagement(ultiPaw);
        DrawUpdateNotification(ultiPaw); // Draw notification between sections
        DrawActionButtons(ultiPaw);
        DrawBlendshapeSliders(ultiPaw);
        DrawHelpBox();

        serializedObject.ApplyModifiedProperties(); // Apply changes at the end
    }

    // --- UI Drawing Helper Methods ---

    private void DrawBanner()
    {
        // (Existing code - no changes needed)
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

    private void DrawFileConfiguration(UltiPaw ultiPaw)
    {
        // (Existing code - slight modification to trigger hash update on change)
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
            if (fbxSpecChanged) // Just switched to auto-detect
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
                 StartVersionFetch(ultiPaw); // Fetch if hash changed
            }
            Repaint(); // Ensure UI reflects potential state changes
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    private void DrawVersionManagement(UltiPaw ultiPaw)
    {
        EditorGUILayout.LabelField("UltiPaw Version Manager", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Display Errors First
        if (!string.IsNullOrEmpty(fetchError)) EditorGUILayout.HelpBox("Fetch Error: " + fetchError, MessageType.Error);
        if (!string.IsNullOrEmpty(downloadError)) EditorGUILayout.HelpBox("Download Error: " + downloadError, MessageType.Warning);
        if (!string.IsNullOrEmpty(deleteError)) EditorGUILayout.HelpBox("Delete Error: " + deleteError, MessageType.Warning); // Show delete errors

        ultiPaw.UpdateAppliedUltiPawVersion(serverVersions);

        // Update/Fetch Button
        GUI.enabled = !isFetching && !isDownloading && !isDeleting; // Disable during any operation
        if (GUILayout.Button(isFetching ? "Fetching..." : "Check for Updates"))
        {
            StartVersionFetch(ultiPaw); // Manually trigger fetch
        }
        GUI.enabled = true;

        // Display Currently Selected Version Info (UI Selection)
        if (selectedVersion != null)
        {
            DrawVersionDetails(selectedVersion, ultiPaw, true); // Don't show action buttons here
        }
        else if (!isFetching && serverVersions.Count > 0)
        {
             EditorGUILayout.HelpBox("Select a version from the list below to apply or manage.", MessageType.Info);
        }

        // Foldout for all available versions
        if (serverVersions.Count > 0 || isFetching) // Show foldout even while fetching
        {
            versionsFoldout = EditorGUILayout.Foldout(versionsFoldout, "All Available Versions", true, EditorStyles.foldoutHeader); // Use header style
            if (versionsFoldout && !isFetching) // Only draw list content if not fetching
            {
                EditorGUI.indentLevel++;
                // Sort versions (newest first) - requires System.Version parsing
                var sortedVersions = serverVersions
                    .Where(v => v != null && !string.IsNullOrEmpty(v.version)) // Filter out invalid entries
                    .OrderByDescending(v => ParseVersion(v.version)) // Sort using helper
                    .ToList();

                foreach (var ver in sortedVersions)
                {
                    DrawVersionListItemWithSplitButtons(ver, ultiPaw); // Use the new drawing method
                    EditorGUILayout.Space(2); // Add a little space between items
                }
                EditorGUI.indentLevel--;
            }
            else if (versionsFoldout && isFetching)
            {
                 EditorGUILayout.LabelField("Fetching version list...", EditorStyles.centeredGreyMiniLabel);
            }
        }
        else if (!isFetching && lastFetchedHash != null) // Fetch succeeded, hash known, but no versions returned
        {
             EditorGUILayout.HelpBox("No compatible versions found for the detected Base FBX hash.", MessageType.Warning);
        }
        else if (!isFetching && lastFetchedHash == null && string.IsNullOrEmpty(ultiPaw.currentBaseFbxHash)) // Haven't fetched successfully yet, no FBX
        {
             EditorGUILayout.HelpBox("Assign or detect a Base FBX first.", MessageType.Info);
        }
         else if (!isFetching && lastFetchedHash == null && !string.IsNullOrEmpty(ultiPaw.currentBaseFbxHash)) // Haven't fetched successfully yet, FBX assigned
        {
             EditorGUILayout.HelpBox("Click 'Check for Updates'.", MessageType.Info);
        }


        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    // Draws details for a specific version (used for Selected Version area)
    private void DrawVersionDetails(UltiPawVersion ver, UltiPaw ultiPaw, bool showActionButtons)
    {
        if (ver == null) return;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"UltiPaw {ver.version ?? "N/A"}", EditorStyles.boldLabel, GUILayout.Width(100));
        GUILayout.FlexibleSpace();
        

        // Indicate if this version is currently applied
        bool isApplied = ultiPaw.appliedUltiPawVersion != null && ultiPaw.appliedUltiPawVersion.Equals(ver);
        if (isApplied)
        {
            DrawChipLabel("Applied", new Color(0.28f, 0.28f, 0.28f), new Color(0.33f, 0.79f, 0f), new Color(0.54f, 0.54f, 0.54f));
        }
        DrawScopeLabel(ver.scope);

        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(ver.changelog))
        {
            EditorGUILayout.LabelField("Changelog:", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(ver.changelog, MessageType.None);
        }
        GUILayout.Space(5); // Add space after details
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

        // Top Left Arc
        AddArc(verts, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f);
        // Top Line
        verts.Add(new Vector2(rect.xMax - radius, rect.yMin));
        // Top Right Arc
        AddArc(verts, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f);
        // Right Line
        verts.Add(new Vector2(rect.xMax, rect.yMax - radius));
        // Bottom Right Arc
        AddArc(verts, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f);
        // Bottom Line
        verts.Add(new Vector2(rect.xMin + radius, rect.yMax));
        // Bottom Left Arc
        AddArc(verts, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f);
        // Left Line
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





    // New method to draw list items with split buttons
    private void DrawVersionListItemWithSplitButtons(UltiPawVersion ver, UltiPaw ultiPaw)
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

        EditorGUILayout.BeginVertical(EditorStyles.helpBox); // Box around each item

        // Row 1: Version Info
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"UltiPaw {ver.version}", GUILayout.Width(100));
        GUILayout.FlexibleSpace();
        
        if (isCurrentlyApplied)
        {
             //Color oldColor = GUI.color;
             //GUI.color = Color.cyan;
             //GUILayout.Label("[Applied]", EditorStyles.boldLabel, GUILayout.Width(60));
             //GUI.color = oldColor;
             
             DrawChipLabel("Applied", BACKGROUND_COLOR, Color.cyan, Color.cyan);
        }
        else if (isCurrentlySelected)
        {
             //GUILayout.Label("[Selected]", EditorStyles.boldLabel, GUILayout.Width(60));
             DrawChipLabel("Selected", BACKGROUND_COLOR, Color.white, Color.white);
        }
        DrawScopeLabel(ver.scope);
        EditorGUILayout.EndHorizontal();

        // Row 2: Action Buttons
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(18); // Indent buttons slightly

        // --- Top Button (Select/Update/Downgrade/Applied) ---
        bool canInteract = !isFetching && !isDownloading && !isDeleting;
        GUI.enabled = canInteract && isDownloaded; // Enable only if downloaded and not busy

        if (isCurrentlyApplied)
        {
            GUI.enabled = false; // Disable if applied
            GUILayout.Button("Applied", GUILayout.Width(100));
            GUI.enabled = true; // Re-enable for next element
        }
        else if (isCurrentlySelected)
        {
            // Option 1: Show "Selected" and disable
            GUI.enabled = false;
            GUILayout.Button("Selected", GUILayout.Width(100));
            GUI.enabled = true;
            // Option 2: Allow re-selecting (might be confusing)
            // if (GUILayout.Button("Select", GUILayout.Width(100))) { SelectVersion(ver, ultiPaw, expectedBinPath); }
        }
        else // Downloaded, but not applied or selected
        {
            GUI.enabled = canInteract; // Ensure enabled if downloaded and not busy
            string buttonText = "Select";
            Color buttonColor = GUI.backgroundColor;
            int comparison = 0;
            if (ultiPaw.appliedUltiPawVersion != null)
            {
                comparison = CompareVersions(ver.version, ultiPaw.appliedUltiPawVersion.version);
            }

            if (comparison > 0)
            {
                buttonText = "Update";
                GUI.backgroundColor = Color.green;
            }
            else if (comparison < 0)
            {
                buttonText = "Downgrade";
                GUI.backgroundColor = OrangeColor;
            }
            // else comparison == 0 or no applied version, keep "Select"

            if (GUILayout.Button(buttonText, GUILayout.Width(100)))
            {
                SelectVersion(ver, ultiPaw, expectedBinPath);
            }
            GUI.backgroundColor = buttonColor; // Restore color
        }
        GUI.enabled = true; // Ensure GUI is enabled after this button block

        GUILayout.Space(10); // Space between buttons

        // --- Bottom Button (Download/Delete) ---
        GUI.enabled = canInteract; // Enable if not busy

        if (isDownloaded)
        {
            // Prevent deleting the applied or selected version for safety
            GUI.enabled = canInteract;
            if (GUILayout.Button(isDeleting ? "Deleting..." : "Delete", GUILayout.Width(100)))
            {
                if (EditorUtility.DisplayDialog("Confirm Delete",
                    $"Are you sure you want to delete the downloaded files for UltiPaw version {ver.version} (Base: {ver.defaultAviVersion})?",
                    "Delete", "Cancel"))
                {
                    StartVersionDelete(ver, ultiPaw);
                }
            }
        }
        else // Not downloaded
        {
            GUI.enabled = canInteract; // Enable if not busy
            if (GUILayout.Button(isDownloading ? "Downloading..." : "Download", GUILayout.Width(100)))
            {
                StartVersionDownload(ver, ultiPaw);
            }
        }
        GUI.enabled = true; // Ensure GUI is enabled after this button block


        GUILayout.FlexibleSpace(); // Push buttons left
        EditorGUILayout.EndHorizontal();

        // Row 3: Changelog (Optional - could make list long)
        // if (!string.IsNullOrEmpty(ver.changelog)) {
        //     EditorGUILayout.HelpBox(ver.changelog, MessageType.None);
        // }

        EditorGUILayout.EndVertical(); // End box for item
    }


    // Helper to draw the scope label with color
    private void DrawScopeLabel(string scope)
    {
        // (Existing code - no changes needed)
        Color originalColor = GUI.contentColor;
        string scopeLower = scope?.ToLower() ?? "unknown";

        Color chipColor = Color.white; // Default color
        
        switch (scopeLower)
        {
            case "public":
                chipColor = Color.green;
                break;
            case "beta":
                chipColor = OrangeColor; // Use defined orange
                break;
            case "alpha":
                chipColor = Color.red;
                break;
            default:
                chipColor = Color.gray;
                break;
        }
        
        DrawChipLabel(scope ?? "N/A", BACKGROUND_COLOR, chipColor, new Color(0.46f, 0.46f, 0.46f));

        //GUILayout.Label($"[{scope ?? "N/A"}]", EditorStyles.boldLabel, GUILayout.Width(60));
        GUI.contentColor = originalColor;
    }

    // Draw notification about new version between sections
    private void DrawUpdateNotification(UltiPaw ultiPaw)
    {
        bool newVersionAvailable = false;
        if (recommendedVersionDetails != null && ultiPaw.appliedUltiPawVersion != null)
        {
            newVersionAvailable = CompareVersions(recommendedVersionDetails.version, ultiPaw.appliedUltiPawVersion.version) > 0;
        }
        else if (recommendedVersionDetails != null && ultiPaw.appliedUltiPawVersion == null && !ultiPaw.isUltiPaw)
        {
             // If no version is applied (original state), recommend the latest
             newVersionAvailable = true; // Consider recommended as "new" if nothing is applied
        }


        if (newVersionAvailable && recommendedVersionDetails != null)
        {
            EditorGUILayout.HelpBox($"New recommended version released: {recommendedVersionDetails.version}", MessageType.Info);
            if (!string.IsNullOrEmpty(recommendedVersionDetails.changelog))
            {
                EditorGUILayout.LabelField("Changelog:", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox(recommendedVersionDetails.changelog, MessageType.None);
            }
            EditorGUILayout.Space();
        }
    }


    private void DrawActionButtons(UltiPaw ultiPaw)
    {
        bool canInteract = !isFetching && !isDownloading && !isDeleting;

        // --- Update / Transform Button ---
        bool newVersionRecommended = false;
        bool recommendedIsDownloaded = false;
        string recommendedBinPath = null;

        if (recommendedVersionDetails != null)
        {
            // Check if recommended is newer than applied
            bool isNewer = ultiPaw.appliedUltiPawVersion == null || CompareVersions(recommendedVersionDetails.version, ultiPaw.appliedUltiPawVersion.version) > 0;

            if (isNewer)
            {
                 newVersionRecommended = true;
                 recommendedBinPath = UltiPawUtils.GetVersionBinPath(recommendedVersionDetails.version, recommendedVersionDetails.defaultAviVersion);
                 recommendedIsDownloaded = !string.IsNullOrEmpty(recommendedBinPath) && File.Exists(recommendedBinPath);
            }
        }

        bool canTransformCurrentSelection = selectedVersion != null &&
                                            !string.IsNullOrEmpty(ultiPaw.selectedUltiPawBinPath) &&
                                            File.Exists(ultiPaw.selectedUltiPawBinPath) &&
                                            !ultiPaw.isUltiPaw; // Can only transform if not already in UltiPaw state (matching applied version)

        if (newVersionRecommended)
        {
            GUI.enabled = canInteract;
            GUI.backgroundColor = Color.green;
            string buttonText = recommendedIsDownloaded
                ? $"Update to {recommendedVersionDetails.version}"
                : $"Download and Update to {recommendedVersionDetails.version}";

            if (GUILayout.Button(buttonText, GUILayout.Height(40)))
            {
                if (EditorUtility.DisplayDialog("Confirm Update",
                    $"This will apply UltiPaw version '{recommendedVersionDetails.version}' to your base FBX.\nA backup (.old) will be created if transforming from original.",
                    "Proceed", "Cancel"))
                {
                    if (recommendedIsDownloaded)
                    {
                        // Select and apply immediately
                        SelectAndApplyVersion(recommendedVersionDetails, ultiPaw, recommendedBinPath);
                    }
                    else
                    {
                        // Start download, which will then select and apply
                        StartDownloadAndApply(recommendedVersionDetails, ultiPaw);
                    }
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }
        else // No new version recommended, show standard "Turn into UltiPaw" button
        {
            // Check if the *selected* version can be applied
            GUI.enabled = canInteract && canTransformCurrentSelection;
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button($"Turn into UltiPaw ({selectedVersion?.version ?? "Select version"})", GUILayout.Height(40)))
            {
                if (EditorUtility.DisplayDialog("Confirm Transformation",
                    $"This will modify your base FBX file using the selected UltiPaw version '{selectedVersion?.version ?? "Unknown"}'.\nA backup (.old) will be created.",
                    "Proceed", "Cancel"))
                {
                    if (ultiPaw.TurnItIntoUltiPaw()) // Check if successful
                    {
                        // Success handled inside TurnItIntoUltiPaw (sets applied version, etc.)
                        Repaint(); // Force repaint
                    }
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }


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
                    Repaint(); // Force repaint
                }
            }
        }
        GUI.enabled = true;
    }


    private void DrawBlendshapeSliders(UltiPaw ultiPaw)
    {
        // Show sliders if *either* a version is applied OR a version is selected (allowing preview)
        bool showSliders = ultiPaw.isUltiPaw || selectedVersion != null;
        var namesToShow = ultiPaw.isUltiPaw ? ultiPaw.blendShapeNames : selectedVersion?.customBlendshapes?.ToList(); // Prioritize applied, fallback to selected

        // Ensure the component's list matches the selected version if not applied
        if (!ultiPaw.isUltiPaw && selectedVersion != null)
        {
             var selectedNames = selectedVersion.customBlendshapes?.ToList() ?? new List<string>();
             if (!selectedNames.SequenceEqual(ultiPaw.blendShapeNames))
             {
                 // Update the component's lists to match the selected version for preview
                 Undo.RecordObject(ultiPaw, "Update Blendshape List for Preview");
                 ultiPaw.blendShapeNames = selectedNames;
                 ultiPaw.SyncBlendshapeLists(); // Resizes value list, resets values to 0
                 serializedObject.Update(); // Reflect changes in serialized object
                 // Reset actual model blendshapes for the new preview set
                 // This might conflict if user had values set before selecting a new version.
                 // Maybe only reset if the *names* changed significantly?
                 // For now, reset them to match the slider state (0).
                 foreach (var name in ultiPaw.blendShapeNames)
                 {
                     ultiPaw.UpdateBlendshapeFromSlider(name, 0f);
                 }
                 EditorUtility.SetDirty(ultiPaw);
             }
             namesToShow = ultiPaw.blendShapeNames; // Use the component's list which now matches selected
        }


        if (showSliders && namesToShow != null && namesToShow.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Blendshapes" + (ultiPaw.isUltiPaw ? "" : " (Preview)"), EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (blendShapeValuesProp.arraySize != namesToShow.Count)
            {
                 serializedObject.ApplyModifiedPropertiesWithoutUndo();
                 blendShapeValuesProp.arraySize = namesToShow.Count;
                 // Initialize new values to 0
                 for(int i = 0; i < blendShapeValuesProp.arraySize; i++) {
                     if(blendShapeValuesProp.GetArrayElementAtIndex(i).floatValue != 0f) // Avoid unnecessary sets
                        blendShapeValuesProp.GetArrayElementAtIndex(i).floatValue = 0f;
                 }
                 serializedObject.Update();
            }

            for (int i = 0; i < namesToShow.Count; i++)
            {
                if (i >= blendShapeValuesProp.arraySize) break; // Safety check

                SerializedProperty blendValProp = blendShapeValuesProp.GetArrayElementAtIndex(i);

                EditorGUI.BeginChangeCheck();
                 float newValue = EditorGUILayout.Slider(
                     new GUIContent(namesToShow[i]),
                     blendValProp.floatValue,
                     0f, 100f
                 );

                if (EditorGUI.EndChangeCheck())
                {
                    blendValProp.floatValue = newValue;
                    // Apply the change to the actual blendshape on the model
                    // This now correctly uses the component's UpdateBlendshapeFromSlider
                    // which handles finding the SMR and applying the value.
                    // It also updates the component's blendShapeValues list internally.
                    ultiPaw.UpdateBlendshapeFromSlider(namesToShow[i], newValue);
                    // No need to call ApplyModifiedProperties here, slider does it.
                }
            }
            EditorGUILayout.EndVertical();
        }
    }

    private void DrawHelpBox()
    {
        // (Existing code - maybe update text slightly)
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

            if (System.Version.TryParse(versionString, out Version ver))
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


    private void StartVersionFetch(UltiPaw ultiPaw)
    {
        if (isFetching) return;
        isFetching = true;
        fetchError = ""; // Clear previous errors
        downloadError = "";
        deleteError = "";
        Repaint(); // Show "Fetching..."
        EditorCoroutineUtility.StartCoroutineOwnerless(FetchVersionsCoroutine(ultiPaw));
    }

    private void StartVersionDownload(UltiPawVersion versionToDownload, UltiPaw ultiPaw)
    {
        if (isDownloading || isFetching || isDeleting) return;

        if (string.IsNullOrEmpty(versionToDownload?.version) || string.IsNullOrEmpty(versionToDownload?.defaultAviVersion)) {
            downloadError = "Version data is missing required fields. Cannot download.";
            Debug.LogError("[UltiPawEditor] " + downloadError);
            Repaint();
            return;
        }

        downloadError = "";
        isDownloading = true;
        Repaint();
        EditorCoroutineUtility.StartCoroutineOwnerless(DownloadVersionCoroutine(versionToDownload, ultiPaw, false)); // false = don't apply after download
    }

    // New method to start deletion
    private void StartVersionDelete(UltiPawVersion versionToDelete, UltiPaw ultiPaw)
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
        EditorCoroutineUtility.StartCoroutineOwnerless(DeleteVersionCoroutine(versionToDelete, ultiPaw, dataPath));
    }

    // New method to start download *and then* apply
    private void StartDownloadAndApply(UltiPawVersion versionToDownload, UltiPaw ultiPaw)
    {
         if (isDownloading || isFetching || isDeleting) return;

        if (string.IsNullOrEmpty(versionToDownload?.version) || string.IsNullOrEmpty(versionToDownload?.defaultAviVersion)) {
            downloadError = "Version data is missing required fields. Cannot download and apply.";
            Debug.LogError("[UltiPawEditor] " + downloadError);
            Repaint();
            return;
        }

        downloadError = "";
        isDownloading = true; // Use the downloading flag
        Repaint();
        EditorCoroutineUtility.StartCoroutineOwnerless(DownloadVersionCoroutine(versionToDownload, ultiPaw, true)); // true = apply after download
    }


    private IEnumerator FetchVersionsCoroutine(UltiPaw ultiPaw)
    {
        // Ensure hash is up-to-date *before* fetching
        // This might have already been called, but ensure it's current
        ultiPaw.UpdateCurrentBaseFbxHash();
        string baseFbxHash = ultiPaw.currentBaseFbxHash;

        if (string.IsNullOrEmpty(baseFbxHash))
        {
            // No FBX, clear results and stop fetching
            serverVersions.Clear();
            recommendedVersionDetails = null;
            recommendedVersionGuid = "";
            lastFetchedHash = null;
            fetchError = ""; // Clear error, UI shows "Assign FBX"
            isFetching = false;
            Repaint();
            yield break; // Exit coroutine
        }

        // --- Proceed with fetch ---
        fetchError = "";
        downloadError = "";
        deleteError = "";
        lastFetchedHash = baseFbxHash; // Store hash used for this fetch

        string url = $"{serverBaseUrl}{versionsEndpoint}?s={UltiPaw.SCRIPT_VERSION}&d={baseFbxHash}";
        Debug.Log($"[UltiPawEditor] Fetching versions from: {url}");

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            var op = req.SendWebRequest();
            while (!op.isDone)
                yield return null;

            isFetching = false; // Mark fetch as complete

            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
            {
                HandleFetchError(req, ultiPaw);
            }
            else // Success
            {
                try
                {
                    string json = req.downloadHandler.text;
                    UltiPawVersionResponse response = JsonUtility.FromJson<UltiPawVersionResponse>(json);

                    if (response != null && response.versions != null)
                    {
                        serverVersions = response.versions ?? new List<UltiPawVersion>(); // Ensure list is not null
                        recommendedVersionGuid = response.recommendedVersion; // Store the recommended version string
                        fetchError = ""; // Clear error on success

                        // Find the full details for the recommended version
                        recommendedVersionDetails = serverVersions.FirstOrDefault(v => v != null && v.version == recommendedVersionGuid);

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
                        if (versionToKeepSelected == null && recommendedVersionDetails != null)
                        {
                            string binPath = UltiPawUtils.GetVersionBinPath(recommendedVersionDetails.version, recommendedVersionDetails.defaultAviVersion);
                            if (!string.IsNullOrEmpty(binPath) && File.Exists(binPath))
                            {
                                versionToKeepSelected = recommendedVersionDetails;
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
                                     SelectVersion(versionToKeepSelected, ultiPaw, binPath); // binPath might be null/invalid if selecting the applied version which isn't downloaded anymore, handle in SelectVersion
                                }
                                else if (selectedVersion != null)
                                {
                                     // Previous selection is no longer valid/downloaded, clear it
                                     ClearSelection(ultiPaw);
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
                            ClearSelection(ultiPaw);
                        }
                        // --- End Smart Selection Logic ---
                    }
                    else
                    {
                        fetchError = "Failed to parse server response (JSON structure might be incorrect).";
                        Debug.LogError("[UltiPawEditor] JSON parsing error or null response/versions. Check server response format.");
                        serverVersions.Clear();
                        recommendedVersionDetails = null;
                        recommendedVersionGuid = "";
                        ClearSelection(ultiPaw);
                    }
                }
                catch (System.Exception e)
                {
                    fetchError = $"Exception parsing server response: {e.Message}";
                    Debug.LogError($"[UltiPawEditor] Exception: {e}");
                    serverVersions.Clear();
                    recommendedVersionDetails = null;
                    recommendedVersionGuid = "";
                    ClearSelection(ultiPaw);
                }
            }
        } // Dispose UnityWebRequest

        Repaint(); // Update UI with results
    }

    // Modified Download Coroutine to optionally apply after download
    private IEnumerator DownloadVersionCoroutine(UltiPawVersion versionToDownload, UltiPaw ultiPaw, bool applyAfterDownload)
    {
        // --- 1. Setup ---
        string baseFbxHashForQuery = lastFetchedHash ?? "unknown"; // Use last fetched hash for consistency
        string downloadUrl = $"{serverBaseUrl}{modelEndpoint}?version={UnityWebRequest.EscapeURL(versionToDownload.version)}&d={baseFbxHashForQuery}";
        string targetExtractFolder = UltiPawUtils.GetVersionDataPath(versionToDownload.version, versionToDownload.defaultAviVersion);
        string targetZipPath = $"{targetExtractFolder}.zip"; // Temp zip file path

        if (string.IsNullOrEmpty(targetExtractFolder))
        {
            downloadError = "Could not determine target folder path. Version data might be invalid.";
            Debug.LogError("[UltiPawEditor] " + downloadError);
            isDownloading = false;
            Repaint();
            yield break;
        }

        UltiPawUtils.EnsureDirectoryExists(targetExtractFolder); // Ensure base versions folder exists
        UltiPawUtils.EnsureDirectoryExists(Path.GetDirectoryName(targetZipPath)); // Ensure folder for zip exists

        Debug.Log($"[UltiPawEditor] Starting download for version {versionToDownload.version} (Base: {versionToDownload.defaultAviVersion})");
        Debug.Log($"[UltiPawEditor] Download URL: {downloadUrl}");
        Debug.Log($"[UltiPawEditor] Target Extract Folder: {targetExtractFolder}");

        downloadError = ""; // Clear previous errors
        isDownloading = true; // Flag is already set by caller, but ensure it's true
        Repaint();

        // --- 2. Download ---
        UnityWebRequest req = null;
        DownloadHandlerFile downloadHandler = null;
        bool downloadSucceeded = false;
        
        UnityWebRequestAsyncOperation op = null;

        try // Outer try for resource disposal
        {
            if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
            downloadHandler = new DownloadHandlerFile(targetZipPath);
            req = new UnityWebRequest(downloadUrl, UnityWebRequest.kHttpVerbGET, downloadHandler, null);

            op = req.SendWebRequest();
            
        }
        catch (Exception ex)
        {
             downloadError = $"Download process error: {ex.Message}";
             Debug.LogError($"[UltiPawEditor] {downloadError}");
        }
        finally
        {
            // Dispose handlers *before* extraction attempt
            downloadHandler?.Dispose();
            req?.Dispose();
        }
        
        while (!op.isDone)
            yield return null; // Wait for download

        if (req.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[UltiPawEditor] Successfully downloaded ZIP to: {targetZipPath}");
            downloadSucceeded = true;
        }
        else
        {
            downloadError = $"Download failed [{req.responseCode} {req.result}]: {req.error}";
            Debug.LogError($"[UltiPawEditor] {downloadError}");
        }


        // --- 3. Extraction (Only if download succeeded) ---
        bool extractionSucceeded = false;
        if (downloadSucceeded)
        {
            try
            {
                // Ensure target directory exists *before* extraction
                UltiPawUtils.EnsureDirectoryExists(targetExtractFolder);
                // Delete existing directory content before extracting? Or rely on overwrite?
                // ZipFile.ExtractToDirectory(targetZipPath, targetExtractFolder, true); // true = overwrite
                 if (Directory.Exists(targetExtractFolder))
                 {
                     Directory.Delete(targetExtractFolder, true); // Clear target folder first
                     Debug.Log($"[UltiPawEditor] Cleared existing directory: {targetExtractFolder}");
                 }
                 Directory.CreateDirectory(targetExtractFolder); // Recreate directory
                 ZipFile.ExtractToDirectory(targetZipPath, targetExtractFolder); // Extract without overwrite flag after clearing

                Debug.Log($"[UltiPawEditor] Successfully extracted files to: {targetExtractFolder}");
                extractionSucceeded = true;
                AssetDatabase.Refresh(); // Refresh assets
            }
            catch (System.Exception ex)
            {
                downloadError = $"Extraction failed: {ex.Message}"; // Set specific error
                Debug.LogError($"[UltiPawEditor] Failed to extract ZIP file '{targetZipPath}' to '{targetExtractFolder}': {ex}");
                AssetDatabase.Refresh(); // Refresh anyway
            }
            finally
            {
                 // Clean up temp zip file
                 if (File.Exists(targetZipPath))
                 {
                     try { File.Delete(targetZipPath); } catch { /* Ignore cleanup error */ }
                 }
            }
        }

        // --- 4. Post-Download Actions (Select / Apply) ---
        if (extractionSucceeded)
        {
            string expectedBinPath = UltiPawUtils.GetVersionBinPath(versionToDownload.version, versionToDownload.defaultAviVersion);
            if (!string.IsNullOrEmpty(expectedBinPath) && File.Exists(expectedBinPath))
            {
                if (applyAfterDownload)
                {
                    // Select the version first (updates UI and component state)
                    SelectVersion(versionToDownload, ultiPaw, expectedBinPath);
                    // Then attempt to apply it
                    if (ultiPaw.TurnItIntoUltiPaw())
                    {
                        Debug.Log($"[UltiPawEditor] Successfully applied version {versionToDownload.version} after download.");
                    }
                    else
                    {
                        // Apply failed, error message shown by TurnItIntoUltiPaw
                        downloadError = $"Downloaded version {versionToDownload.version}, but failed to apply it. Check console."; // Update status
                    }
                }
                else // Just select it
                {
                    SelectVersion(versionToDownload, ultiPaw, expectedBinPath);
                }
            }
            else
            {
                 downloadError = "Extraction seemed successful, but failed to find required file (ultipaw.bin) for selection.";
                 Debug.LogError("[UltiPawEditor] " + downloadError);
            }
        }

        // --- 5. Final Cleanup ---
        isDownloading = false; // Mark process finished
        Repaint(); // Update UI after everything
    }

    // New Coroutine for Deleting a Version
    private IEnumerator DeleteVersionCoroutine(UltiPawVersion versionToDelete, UltiPaw ultiPaw, string dataPath)
    {
        yield return null; // Wait a frame

        try
        {
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, true); // Recursive delete
                // Also delete the temp zip file if it somehow exists
                string zipPath = dataPath + ".zip";
                if (File.Exists(zipPath)) File.Delete(zipPath);

                Debug.Log($"[UltiPawEditor] Successfully deleted version folder: {dataPath}");
                AssetDatabase.Refresh(); // Make Unity forget the files

                // If the deleted version was the selected one, clear selection
                if (selectedVersion != null && selectedVersion.Equals(versionToDelete))
                {
                    ClearSelection(ultiPaw);
                }
                // We shouldn't be able to delete the *applied* version due to UI checks,
                // but add a safety check anyway.
                if (ultiPaw.appliedUltiPawVersion != null && ultiPaw.appliedUltiPawVersion.Equals(versionToDelete))
                {
                     Debug.LogError("[UltiPawEditor] Deleted the currently applied version's files! This may cause issues. Resetting applied state.");
                     Undo.RecordObject(ultiPaw, "Clear Applied Version After Deletion");
                     ultiPaw.appliedUltiPawVersion = null;
                     ultiPaw.UpdateIsUltiPawState(); // Update state
                     EditorUtility.SetDirty(ultiPaw);
                }

                deleteError = ""; // Clear error on success
            }
            else
            {
                 deleteError = $"Directory not found, cannot delete: {dataPath}";
                 Debug.LogWarning("[UltiPawEditor] " + deleteError);
            }
        }
        catch (Exception ex)
        {
            deleteError = $"Failed to delete directory '{dataPath}': {ex.Message}";
            Debug.LogError("[UltiPawEditor] " + deleteError);
            AssetDatabase.Refresh(); // Refresh anyway
        }
        finally
        {
            isDeleting = false; // Mark process finished
            Repaint(); // Update UI
        }
    }


    private void HandleFetchError(UnityWebRequest req, UltiPaw ultiPaw)
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
        recommendedVersionDetails = null;
        recommendedVersionGuid = "";
        // Don't clear lastFetchedHash here, it might be useful for context
        ClearSelection(ultiPaw); // Clear UI selection on fetch error
    }

    // Selects a version in the UI, updating component state
    private void SelectVersion(UltiPawVersion version, UltiPaw ultiPaw, string binPath)
    {
        if (version == null || ultiPaw == null) return;

        // Check if already selected
        if (selectedVersion != null && selectedVersion.Equals(version)) return;

        // Update UI selection state
        selectedVersion = version;

        // Record Undo for changes to the UltiPaw component
        Undo.RecordObject(ultiPaw, "Select UltiPaw Version");

        // Update the component's active version (used for transform/reset actions)
        ultiPaw.activeUltiPawVersion = version;
        // Update the bin path only if it's valid and exists, otherwise clear it
        ultiPaw.selectedUltiPawBinPath = (!string.IsNullOrEmpty(binPath) && File.Exists(binPath)) ? binPath : null;

        // Update blendshapes only if the selected version is different from the applied one
        // Or if nothing is applied yet. This prevents resetting sliders when just re-selecting the applied version.
        bool needsBlendshapeUpdate = ultiPaw.appliedUltiPawVersion == null || !ultiPaw.appliedUltiPawVersion.Equals(version);

        if (needsBlendshapeUpdate)
        {
            var newBlendshapes = version.customBlendshapes ?? new string[0];
            var currentNames = ultiPaw.blendShapeNames ?? new List<string>();

            if (!currentNames.SequenceEqual(newBlendshapes))
            {
                ultiPaw.blendShapeNames = new List<string>(newBlendshapes);
                ultiPaw.SyncBlendshapeLists(); // Resizes value list, resets values to 0

                // Reset actual model blendshapes for the new set
                foreach (var name in ultiPaw.blendShapeNames)
                {
                    ultiPaw.UpdateBlendshapeFromSlider(name, 0f); // Use the method that finds Body SMR
                }
            }
        }

        EditorUtility.SetDirty(ultiPaw);
        serializedObject.Update(); // Update serialized object to reflect component changes
        Debug.Log($"[UltiPawEditor] Selected version for next action: {version.version}");
        Repaint();
    }

    // Clears the UI selection
    private void ClearSelection(UltiPaw ultiPaw) {
        if (selectedVersion == null) return; // Nothing to clear in UI

        selectedVersion = null; // Clear UI selection state

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
                 // Maybe reset blendshapes on model too?
                 // foreach(...) ultiPaw.UpdateBlendshapeFromSlider(name, 0f);
            }

            EditorUtility.SetDirty(ultiPaw);
            serializedObject.Update();
        }
        Repaint();
     }

    // Helper to select a version and immediately try to apply it
    private void SelectAndApplyVersion(UltiPawVersion version, UltiPaw ultiPaw, string binPath)
    {
        if (version == null || ultiPaw == null) return;

        // Select the version first (updates UI, component state, blendshapes if needed)
        SelectVersion(version, ultiPaw, binPath);

        // Ensure selection was successful and bin path is valid before attempting transform
        if (selectedVersion != null && selectedVersion.Equals(version) &&
            !string.IsNullOrEmpty(ultiPaw.selectedUltiPawBinPath) && File.Exists(ultiPaw.selectedUltiPawBinPath))
        {
            // Attempt to apply the now-selected version
            if (ultiPaw.TurnItIntoUltiPaw())
            {
                 Debug.Log($"[UltiPawEditor] Successfully applied version {version.version}.");
            }
            else
            {
                 Debug.LogError($"[UltiPawEditor] Failed to apply version {version.version} after selecting it.");
                 // Error message shown by TurnItIntoUltiPaw
            }
        }
        else
        {
             Debug.LogError($"[UltiPawEditor] Cannot apply version {version.version}. Selection failed or bin path is invalid.");
        }
        Repaint(); // Update UI after attempt
    }


    private bool AnyBackupExists(List<GameObject> files)
    {
        // (Existing code - no changes needed)
        if (files == null) return false;
        return files.Any(file => file != null && File.Exists(AssetDatabase.GetAssetPath(file) + ".old"));
    }
}
#endif