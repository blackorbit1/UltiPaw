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

        var blendshapeEntries = appliedVersion.customBlendshapes;
        var values = editor.blendShapeValuesProp;

        while (values.arraySize < blendshapeEntries.Length) values.InsertArrayElementAtIndex(values.arraySize);
        while (values.arraySize > blendshapeEntries.Length) values.DeleteArrayElementAtIndex(values.arraySize - 1);
        
        for (int i = 0; i < blendshapeEntries.Length; i++)
        {
            var entry = blendshapeEntries[i];
            string shapeName = entry.name;
            float defaultValue = float.TryParse(entry.defaultValue, out float parsedDefault) ? parsedDefault : 0f;
            
            int index = smr.sharedMesh.GetBlendShapeIndex(shapeName);
            if (index < 0) continue;

            // Get current weight from the mesh
            float currentWeight = smr.GetBlendShapeWeight(index);
            
            EditorGUI.BeginChangeCheck();
            float newWeight = EditorGUILayout.Slider(new GUIContent(shapeName), currentWeight, 0f, 100f);
            if (EditorGUI.EndChangeCheck())
            {
                smr.SetBlendShapeWeight(index, newWeight);
                values.GetArrayElementAtIndex(i).floatValue = newWeight;
                
                // Save custom override if different from default
                if (!Mathf.Approximately(newWeight, defaultValue))
                {
                    SetCustomOverrideValue(shapeName, newWeight);
                }
                else
                {
                    // If user set it back to default, remove the override
                    RemoveCustomOverride(shapeName);
                }
                
                EditorUtility.SetDirty(smr);
                EditorUtility.SetDirty(editor.ultiPawTarget);
            }
        }
        
        EditorGUILayout.Space();
        if (GUILayout.Button("Reset to Default Values"))
        {
            ClearAllCustomOverrides();
            
            // Reapply default values
            for (int i = 0; i < blendshapeEntries.Length; i++)
            {
                var entry = blendshapeEntries[i];
                float defaultValue = float.TryParse(entry.defaultValue, out float parsedDefault) ? parsedDefault : 0f;
                int index = smr.sharedMesh.GetBlendShapeIndex(entry.name);
                if (index >= 0)
                {
                    smr.SetBlendShapeWeight(index, defaultValue);
                    values.GetArrayElementAtIndex(i).floatValue = defaultValue;
                }
            }
            
            EditorUtility.SetDirty(smr);
            EditorUtility.SetDirty(editor.ultiPawTarget);
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private float GetCustomOverrideValue(string blendshapeName, float defaultValue)
    {
        var overrideNames = editor.ultiPawTarget.customBlendshapeOverrideNames;
        var overrideValues = editor.ultiPawTarget.customBlendshapeOverrideValues;
        
        int index = overrideNames.IndexOf(blendshapeName);
        if (index >= 0 && index < overrideValues.Count)
        {
            return overrideValues[index];
        }
        
        return defaultValue;
    }
    
    private void SetCustomOverrideValue(string blendshapeName, float value)
    {
        var overrideNames = editor.ultiPawTarget.customBlendshapeOverrideNames;
        var overrideValues = editor.ultiPawTarget.customBlendshapeOverrideValues;
        
        int index = overrideNames.IndexOf(blendshapeName);
        if (index >= 0)
        {
            overrideValues[index] = value;
        }
        else
        {
            overrideNames.Add(blendshapeName);
            overrideValues.Add(value);
        }
    }
    
    private void RemoveCustomOverride(string blendshapeName)
    {
        var overrideNames = editor.ultiPawTarget.customBlendshapeOverrideNames;
        var overrideValues = editor.ultiPawTarget.customBlendshapeOverrideValues;
        
        int index = overrideNames.IndexOf(blendshapeName);
        if (index >= 0)
        {
            overrideNames.RemoveAt(index);
            overrideValues.RemoveAt(index);
        }
    }
    
    private void ClearAllCustomOverrides()
    {
        editor.ultiPawTarget.customBlendshapeOverrideNames.Clear();
        editor.ultiPawTarget.customBlendshapeOverrideValues.Clear();
    }
}
#endif