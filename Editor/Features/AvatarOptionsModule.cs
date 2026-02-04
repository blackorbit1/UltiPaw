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
    private readonly RepartitionGraph repartitionGraph;
    private readonly List<RepartitionGraph.GraphElement> sampleGraphData;

    public AvatarOptionsModule(UltiPawEditor editor)
    {
        this.editor = editor;
        customVeinsDrawer = new CustomVeinsDrawer(editor);
        blendshapeDrawer = new BlendshapeDrawer(editor);
        
        testChipGroup = new SelectableChipGroup(new List<string> { 
            "orbit muscles", "orbit face", "orbit eyes", 
            "orbit face details", "orbit eye reflections", "orbit muscle definition" 
        });

        repartitionGraph = new RepartitionGraph();
        sampleGraphData = new List<RepartitionGraph.GraphElement>
        {
            new RepartitionGraph.GraphElement(258, "Used by the avatar", "#e0e0e0"),
            new RepartitionGraph.GraphElement(150, "Used by the sliders", "#008000"),
            new RepartitionGraph.GraphElement(300, "Available", "#333333")
        };
    }

    public void Draw()
    {
        // Draw CustomVeinsDrawer first
        customVeinsDrawer.Draw();

        EditorGUILayout.Space(15);
        repartitionGraph.Draw(sampleGraphData);
        EditorGUILayout.Space(15);

        EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);
        testChipGroup.Draw();
        EditorGUILayout.Space(10);
        
        // Then draw BlendshapeDrawer
        blendshapeDrawer.Draw();
    }
}
#endif
