#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class AvatarOptionsModule
{
    private readonly UltiPawEditor editor;
    private readonly CustomVeinsDrawer customVeinsDrawer;
    private readonly BlendshapeDrawer blendshapeDrawer;

    public AvatarOptionsModule(UltiPawEditor editor)
    {
        this.editor = editor;
        customVeinsDrawer = new CustomVeinsDrawer(editor);
        blendshapeDrawer = new BlendshapeDrawer(editor);
    }

    public void Draw()
    {
        // Draw CustomVeinsDrawer first
        customVeinsDrawer.Draw();
        
        // Then draw BlendshapeDrawer
        blendshapeDrawer.Draw();
    }
}
#endif
