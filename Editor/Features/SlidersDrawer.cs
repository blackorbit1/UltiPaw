#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SlidersDrawer
{
    private readonly UltiPawEditor editor;
    private readonly SelectableChipGroup selectableChipGroup;
    private readonly RepartitionGraph repartitionGraph;
    private readonly Texture2D sideImage;

    private List<string> sliderNames = new List<string>();
    private List<RepartitionGraph.GraphElement> graphData = new List<RepartitionGraph.GraphElement>();
    private HashSet<int> selectedIndices = new HashSet<int>();
    
    private double lastMenuNameChangeTime;
    private bool hasPendingMenuNameUpdate;
    private const double DEBOUNCE_DELAY = 4.0; // Seconds

    private const int MAX_PARAMETERS = 256;
    private const int PARAMETERS_PER_SLIDER = 6;
    private const int USED_BY_AVATAR_DEFAULT = 45;

    public SlidersDrawer(UltiPawEditor editor)
    {
        this.editor = editor;
        
        // Load the image
        sideImage = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/ultipaw/Editor/slidersFactoryImageHalf.png");

        repartitionGraph = new RepartitionGraph();
        
        // Initialize with default/empty state
        UpdateSliderData();
        
        HashSet<int> initialSelection = new HashSet<int>();
        var entries = GetSliderEntries();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].isSliderDefault) initialSelection.Add(i);
        }

        selectedIndices = initialSelection;
        selectableChipGroup = new SelectableChipGroup(sliderNames, initialSelection, (selection) => {
            selectedIndices = selection;
            UpdateGraph(selection.Count);
            
            // Start debounce timer for chips too
            lastMenuNameChangeTime = EditorApplication.timeSinceStartup;
            hasPendingMenuNameUpdate = true;
        });
        
        UpdateGraph(initialSelection.Count);
    }

    private List<CustomBlendshapeEntry> GetSliderEntries()
    {
        if (editor.ultiPawTarget.appliedUltiPawVersion?.customBlendshapes == null)
            return new List<CustomBlendshapeEntry>();

        return editor.ultiPawTarget.appliedUltiPawVersion.customBlendshapes
            .Where(e => e.isSlider)
            .ToList();
    }

    private void UpdateSliderData()
    {
        var entries = GetSliderEntries();
        sliderNames = entries.Select(e => e.name).ToList();
    }

    private void UpdateGraph(int selectedCount)
    {
        int usedBySliders = selectedCount * PARAMETERS_PER_SLIDER;
        int usedByAvatar = USED_BY_AVATAR_DEFAULT; // TODO: Logic to retrieve the "Used by avatar" amount
        int available = Mathf.Max(0, MAX_PARAMETERS - usedByAvatar - usedBySliders);

        graphData = new List<RepartitionGraph.GraphElement>
        {
            new RepartitionGraph.GraphElement(usedByAvatar, $"Parameters used by the avatar : {usedByAvatar}", "#e0e0e0"),
            new RepartitionGraph.GraphElement(usedBySliders, $"Parameters used by the sliders : {usedBySliders}", "#008000"),
            new RepartitionGraph.GraphElement(available, $"Parameters available : {available}", "#333333")
        };
    }

    public void Draw()
    {
        var entries = GetSliderEntries();
        if (entries.Count == 0) return;

        // If entries changed, refresh names and chip group
        if (entries.Count != sliderNames.Count)
        {
            UpdateSliderData();
            selectableChipGroup.SetOptions(sliderNames);
            
            HashSet<int> initialSelection = new HashSet<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].isSliderDefault) initialSelection.Add(i);
            }
            selectableChipGroup.SetSelection(initialSelection);
        }

        float drawerHeight = 224f;
        float imageWidth = 115f;
        
        // Define styles for transparent text field
        GUIStyle transparentTextFieldStyle = new GUIStyle(GUIStyle.none);
        transparentTextFieldStyle.alignment = TextAnchor.UpperCenter;
        transparentTextFieldStyle.normal.textColor = Color.white;
        transparentTextFieldStyle.focused.textColor = Color.white;
        transparentTextFieldStyle.fontSize = 12;
        transparentTextFieldStyle.fontStyle = FontStyle.Bold;
        transparentTextFieldStyle.wordWrap = true;
        transparentTextFieldStyle.clipping = TextClipping.Overflow;

        EditorGUILayout.BeginHorizontal();
        {
            // Reserve space for the image
            Rect imagePlaceholderRect = EditorGUILayout.GetControlRect(false, drawerHeight, GUILayout.Width(imageWidth));
            
            // Draw the right side content
            GUILayout.BeginVertical(GUILayout.Height(drawerHeight));
            {
                GUILayout.FlexibleSpace();
                
                EditorGUILayout.LabelField("Sliders", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                repartitionGraph.Draw(graphData);
                EditorGUILayout.Space(15);

                selectableChipGroup.Draw();
                
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndVertical();

            // Debounce logic for sliders setup
            if (hasPendingMenuNameUpdate)
            {
                double timeRemaining = DEBOUNCE_DELAY - (EditorApplication.timeSinceStartup - lastMenuNameChangeTime);
                if (timeRemaining <= 0)
                {
                    hasPendingMenuNameUpdate = false;
                    ApplySlidersToAvatar();
                }
                else
                {
                    // Draw a small status indicator
                    Rect statusRect = new Rect(imagePlaceholderRect.xMax + 10, imagePlaceholderRect.yMax - 20, 200, 20);
                    GUI.Label(statusRect, $"<color=#888888>Applying with VRCFury in {timeRemaining:F0}s...</color>", new GUIStyle(EditorStyles.miniLabel) { richText = true });
                    editor.Repaint(); // Force repaint to see the countdown
                }
            }

            // Draw overlay elements (Image + Icon + Text) at absolute coordinates
            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout || Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp)
            {
                // Draw background image
                if (sideImage != null && Event.current.type == EventType.Repaint)
                {
                    Rect imageRect = new Rect(0, imagePlaceholderRect.y, imageWidth, drawerHeight);
                    GUI.DrawTexture(imageRect, sideImage, ScaleMode.StretchToFill);
                }

                Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/ultipaw/Editor/vrcSliderIcon.png");
                if (icon != null)
                {
                    float iconSize = 50f;
                    float paddingLeft = 49f;
                    float spacing = 2f;
                    float textWidth = 70f;
                    
                    string currentName = editor.ultiPawTarget.slidersMenuName;
                    
                    // Calculate dynamic height based on content
                    float textHeight = transparentTextFieldStyle.CalcHeight(new GUIContent(currentName), textWidth);
                    
                    // Calculate total height of the block to center it
                    float totalBlockHeight = iconSize + spacing + textHeight;
                    float blockStartY = imagePlaceholderRect.y + (drawerHeight - totalBlockHeight) / 2f;
                    
                    Rect iconRect = new Rect(paddingLeft, blockStartY, iconSize, iconSize);

                    // Draw Icon
                    if (Event.current.type == EventType.Repaint)
                    {
                        GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                    }
                    
                    // Draw Text Field below icon
                    float textX = paddingLeft + (iconSize / 2f) - (textWidth / 2f);
                    float textY = iconRect.yMax + spacing;
                    Rect textRect = new Rect(textX, textY, textWidth, textHeight);

                    EditorGUI.BeginChangeCheck();
                    string newName = EditorGUI.TextField(textRect, currentName, transparentTextFieldStyle);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(editor.ultiPawTarget, "Change Slider Menu Name");
                        editor.ultiPawTarget.slidersMenuName = newName;
                        
                        // Start debounce timer
                        lastMenuNameChangeTime = EditorApplication.timeSinceStartup;
                        hasPendingMenuNameUpdate = true;
                    }
                }
            }
        }
        EditorGUILayout.EndHorizontal();
    }
    private void ApplySlidersToAvatar()
    {
        var allEntries = GetSliderEntries();
        var selectedEntries = selectedIndices.Select(index => allEntries[index]).ToList();
        
        GameObject avatarRoot = editor.ultiPawTarget.transform.root.gameObject;
        string menuName = editor.ultiPawTarget.slidersMenuName;
        
        // Use the TaskQueue to avoid blocking the UI
        VRCFuryTaskQueue.Enqueue(() => {
            VRCFuryService.Instance.ApplySliders(avatarRoot, menuName, selectedEntries);
        });
    }
}
#endif
