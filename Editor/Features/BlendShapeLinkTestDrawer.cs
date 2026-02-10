#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class BlendShapeLinkTestDrawer
{
    private readonly UltiPawEditor editor;

    private SkinnedMeshRenderer targetRenderer;
    private string sourceBlendshape = "Blink";
    private string destinationBlendshape = "Blink Fix";
    private string factorParameterName = "custom_face";
    private bool foldout;
    private bool testEnabled;

    private const string FoldoutPrefKey = "UltiPaw_BlendShapeLinkTest_Foldout";
    private const string EnabledPrefKey = "UltiPaw_BlendShapeLinkTest_Enabled";
    private const string SourcePrefKey = "UltiPaw_BlendShapeLinkTest_Source";
    private const string DestinationPrefKey = "UltiPaw_BlendShapeLinkTest_Destination";
    private const string ParamPrefKey = "UltiPaw_BlendShapeLinkTest_Param";

    public BlendShapeLinkTestDrawer(UltiPawEditor editor)
    {
        this.editor = editor;
        foldout = EditorPrefs.GetBool(FoldoutPrefKey, false);
        testEnabled = EditorPrefs.GetBool(EnabledPrefKey, true);
        sourceBlendshape = EditorPrefs.GetString(SourcePrefKey, sourceBlendshape);
        destinationBlendshape = EditorPrefs.GetString(DestinationPrefKey, destinationBlendshape);
        factorParameterName = EditorPrefs.GetString(ParamPrefKey, factorParameterName);
    }

    public void Draw()
    {
        foldout = EditorGUILayout.Foldout(foldout, "BlendShape Link Factor Test", true);
        EditorPrefs.SetBool(FoldoutPrefKey, foldout);
        if (!foldout) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox(
            "Saves a BlendShape factor link configuration. " +
            "The patch is applied only to VRCFury temporary controllers during avatar preprocess (build/upload and play-mode build).",
            MessageType.Info
        );

        GameObject avatarRoot = editor?.ultiPawTarget != null ? editor.ultiPawTarget.transform.root.gameObject : null;
        if (avatarRoot == null)
        {
            EditorGUILayout.HelpBox("Avatar root not found.", MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        if (targetRenderer == null)
        {
            targetRenderer = FindDefaultBodyRenderer(avatarRoot);
        }

        EditorGUI.BeginChangeCheck();
        testEnabled = EditorGUILayout.Toggle(
            new GUIContent("Enable Test Link", "Toggles only this manual test link. Does not affect version-based BlendShape links."),
            testEnabled
        );
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(EnabledPrefKey, testEnabled);
            if (targetRenderer != null &&
                !string.IsNullOrWhiteSpace(sourceBlendshape) &&
                !string.IsNullOrWhiteSpace(destinationBlendshape) &&
                !string.IsNullOrWhiteSpace(factorParameterName))
            {
                BlendShapeLinkService.Instance.UpsertFactorLinkConfig(
                    avatarRoot,
                    targetRenderer,
                    sourceBlendshape,
                    destinationBlendshape,
                    factorParameterName,
                    testEnabled
                );
            }
        }

        using (new EditorGUI.DisabledScope(!testEnabled))
        {
            EditorGUI.BeginChangeCheck();
            targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                new GUIContent("Target Mesh", "SkinnedMeshRenderer that has both source and destination blendshapes."),
                targetRenderer,
                typeof(SkinnedMeshRenderer),
                true
            );
            if (EditorGUI.EndChangeCheck())
            {
                sourceBlendshape = string.Empty;
                destinationBlendshape = string.Empty;
            }

            DrawBlendshapeSelectors();

            EditorGUI.BeginChangeCheck();
            factorParameterName = EditorGUILayout.TextField(
                new GUIContent("Factor Parameter", "Float parameter name used as the multiplier (0..1)."),
                factorParameterName
            );
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(ParamPrefKey, factorParameterName ?? string.Empty);
            }

        }

        EditorGUILayout.Space(4f);
        using (new EditorGUI.DisabledScope(targetRenderer == null))
        {
            if (GUILayout.Button("Save Factor Link Config"))
            {
                SaveConfig(avatarRoot);
            }
        }

        DrawActiveVersionLinksDebug(avatarRoot);

        EditorGUILayout.EndVertical();
    }

    private void DrawBlendshapeSelectors()
    {
        var names = GetBlendshapeNames(targetRenderer);
        bool hasNames = names.Count > 0;

        if (!hasNames)
        {
            sourceBlendshape = EditorGUILayout.TextField("Source Blendshape", sourceBlendshape);
            destinationBlendshape = EditorGUILayout.TextField("Destination Blendshape", destinationBlendshape);
            return;
        }

        int sourceIndex = Mathf.Max(0, names.IndexOf(sourceBlendshape));
        int destinationIndex = Mathf.Max(0, names.IndexOf(destinationBlendshape));

        EditorGUI.BeginChangeCheck();
        sourceIndex = EditorGUILayout.Popup("Source Blendshape", sourceIndex, names.ToArray());
        if (EditorGUI.EndChangeCheck())
        {
            sourceBlendshape = names[sourceIndex];
            EditorPrefs.SetString(SourcePrefKey, sourceBlendshape ?? string.Empty);
        }

        EditorGUI.BeginChangeCheck();
        destinationIndex = EditorGUILayout.Popup("Destination Blendshape", destinationIndex, names.ToArray());
        if (EditorGUI.EndChangeCheck())
        {
            destinationBlendshape = names[destinationIndex];
            EditorPrefs.SetString(DestinationPrefKey, destinationBlendshape ?? string.Empty);
        }
    }

    private void SaveConfig(GameObject avatarRoot)
    {
        var result = BlendShapeLinkService.Instance.UpsertFactorLinkConfig(
            avatarRoot,
            targetRenderer,
            sourceBlendshape,
            destinationBlendshape,
            factorParameterName,
            testEnabled
        );

        if (!result.success)
        {
            EditorUtility.DisplayDialog("BlendShape Factor Link", result.message, "Ok");
            return;
        }

        EditorUtility.SetDirty(editor?.ultiPawTarget);
        UltiPawLogger.Log("[UltiPaw] BlendShape factor link config saved.");
        EditorUtility.DisplayDialog("BlendShape Factor Link", result.message, "Ok");
    }

    public void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Intentionally no-op. Execution is handled by BlendShapeLinkPostVrcfuryHook.
    }

    private void DrawActiveVersionLinksDebug(GameObject avatarRoot)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Active Version Links (Debug)", EditorStyles.boldLabel);

        var infos = BlendShapeLinkService.Instance.GetActiveVersionLinkDebugInfo(avatarRoot);
        if (infos == null || infos.Count == 0)
        {
            EditorGUILayout.HelpBox("No active version BlendShape links.", MessageType.None);
            return;
        }

        foreach (var info in infos)
        {
            string factorInfo = info.usesConstantFactor
                ? $"param={info.factorParameterName}, constant={info.constantFactor.ToString("G9", CultureInfo.InvariantCulture)}"
                : $"param={info.factorParameterName}";

            EditorGUILayout.LabelField(
                $"[{info.targetRendererPath}] {info.sourceBlendshape} -> {info.destinationBlendshape} | {factorInfo}");
        }
    }

    private static List<string> GetBlendshapeNames(SkinnedMeshRenderer renderer)
    {
        var output = new List<string>();
        if (renderer == null || renderer.sharedMesh == null) return output;

        var mesh = renderer.sharedMesh;
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            output.Add(mesh.GetBlendShapeName(i));
        }

        return output;
    }

    private static SkinnedMeshRenderer FindDefaultBodyRenderer(GameObject avatarRoot)
    {
        if (avatarRoot == null) return null;
        return avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(r => r != null && string.Equals(r.name, "Body", StringComparison.OrdinalIgnoreCase));
    }
}
#endif
