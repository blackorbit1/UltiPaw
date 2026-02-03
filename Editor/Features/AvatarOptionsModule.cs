#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class AvatarOptionsModule
{
    private readonly UltiPawEditor editor;
    private readonly CustomVeinsDrawer customVeinsDrawer;
    private readonly BlendshapeDrawer blendshapeDrawer;
    private readonly SelectableChipGroup testChipGroup;

    public AvatarOptionsModule(UltiPawEditor editor)
    {
        this.editor = editor;
        customVeinsDrawer = new CustomVeinsDrawer(editor);
        blendshapeDrawer = new BlendshapeDrawer(editor);
        
        testChipGroup = new SelectableChipGroup(new List<string> { 
            "orbit muscles", "orbit face", "orbit eyes", 
            "orbit face details", "orbit eye reflections", "orbit muscle definition" 
        });
    }

    public void Draw()
    {
        // Draw CustomVeinsDrawer first
        customVeinsDrawer.Draw();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);
        testChipGroup.Draw();
        EditorGUILayout.Space(10);
        
        // Then draw BlendshapeDrawer
        blendshapeDrawer.Draw();
    }
}
#endif
