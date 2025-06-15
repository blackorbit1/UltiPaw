#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

public class BlendshapeDrawer
{
    private readonly UltiPawEditor editor;

    public BlendshapeDrawer(UltiPawEditor editor)
    {
        this.editor = editor;
    }

    public void Draw()
    {
        var appliedVersion = editor.ultiPawTarget.appliedUltiPawVersion;
        if (!editor.isUltiPaw || appliedVersion?.customBlendshapes == null || !appliedVersion.customBlendshapes.Any()) return;

        var smr = editor.ultiPawTarget.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(s => s.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));
            
        if (smr?.sharedMesh == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Blendshapes", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        var names = appliedVersion.customBlendshapes;
        var values = editor.blendShapeValuesProp;

        while (values.arraySize < names.Length) values.InsertArrayElementAtIndex(values.arraySize);
        while (values.arraySize > names.Length) values.DeleteArrayElementAtIndex(values.arraySize - 1);
        
        for (int i = 0; i < names.Length; i++)
        {
            string shapeName = names[i];
            int index = smr.sharedMesh.GetBlendShapeIndex(shapeName);
            if (index < 0) continue;

            float currentWeight = smr.GetBlendShapeWeight(index);
            
            EditorGUI.BeginChangeCheck();
            float newWeight = EditorGUILayout.Slider(new GUIContent(shapeName), currentWeight, 0f, 100f);
            if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(currentWeight, newWeight))
            {
                smr.SetBlendShapeWeight(index, newWeight);
                values.GetArrayElementAtIndex(i).floatValue = newWeight;
                EditorUtility.SetDirty(smr);
            }
        }
        EditorGUILayout.EndVertical();
    }
}
#endif