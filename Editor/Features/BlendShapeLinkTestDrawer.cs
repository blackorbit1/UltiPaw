#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class BlendShapeLinkTestDrawer
{
    private readonly UltiPawEditor editor;

    private SkinnedMeshRenderer targetRenderer;
    private string targetRendererPath;
    private string sourceBlendshape = "Blink";
    private string destinationBlendshape = "Blink Fix";
    private string factorParameterName = "custom_face";
    private bool foldout;
    private bool testEnabled = true;
    private bool autoApplyOnEnterPlay = true;

    private int remainingRetries;
    private double nextRetryTime;
    private string runtimeStatus;
    private bool runtimeStatusIsError;

    private const string FoldoutPrefKey = "UltiPaw_BlendShapeLinkTest_Foldout";
    private const string SourcePrefKey = "UltiPaw_BlendShapeLinkTest_Source";
    private const string DestinationPrefKey = "UltiPaw_BlendShapeLinkTest_Destination";
    private const string ParamPrefKey = "UltiPaw_BlendShapeLinkTest_Param";
    private const string TargetPathPrefKey = "UltiPaw_BlendShapeLinkTest_TargetPath";
    private const string AutoPlayPrefKey = "UltiPaw_BlendShapeLinkTest_AutoPlay";
    private const string EnabledPrefKey = "UltiPaw_BlendShapeLinkTest_Enabled";

    private const int MaxRetries = 25;
    private const double RetryIntervalSeconds = 0.2;

    public BlendShapeLinkTestDrawer(UltiPawEditor editor)
    {
        this.editor = editor;
        foldout = EditorPrefs.GetBool(FoldoutPrefKey, false);
        sourceBlendshape = EditorPrefs.GetString(SourcePrefKey, sourceBlendshape);
        destinationBlendshape = EditorPrefs.GetString(DestinationPrefKey, destinationBlendshape);
        factorParameterName = EditorPrefs.GetString(ParamPrefKey, factorParameterName);
        targetRendererPath = EditorPrefs.GetString(TargetPathPrefKey, string.Empty);
        testEnabled = EditorPrefs.GetBool(EnabledPrefKey, true);
        autoApplyOnEnterPlay = EditorPrefs.GetBool(AutoPlayPrefKey, true);
    }

    public void Draw()
    {
        foldout = EditorGUILayout.Foldout(foldout, "BlendShape Link Factor Test", true);
        EditorPrefs.SetBool(FoldoutPrefKey, foldout);
        if (!foldout) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox(
            "This runs in Play Mode after VRCFury creates temporary controllers. " +
            "It patches only temporary controllers and does not modify your original controller assets.",
            MessageType.Info
        );

        EditorGUI.BeginChangeCheck();
        testEnabled = EditorGUILayout.ToggleLeft("Enable Test", testEnabled);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(EnabledPrefKey, testEnabled);
            if (!testEnabled)
            {
                EditorApplication.update -= RetryTick;
                remainingRetries = 0;
                nextRetryTime = 0;
            }
        }

        GameObject avatarRoot = editor?.ultiPawTarget != null ? editor.ultiPawTarget.transform.root.gameObject : null;
        if (avatarRoot == null)
        {
            EditorGUILayout.HelpBox("Avatar root not found.", MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        ResolveTargetRendererFromPathIfNeeded(avatarRoot);
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
                targetRendererPath = GetRendererPath(avatarRoot, targetRenderer);
                EditorPrefs.SetString(TargetPathPrefKey, targetRendererPath ?? string.Empty);
            }

            DrawBlendshapeSelectors();

            EditorGUI.BeginChangeCheck();
            factorParameterName = EditorGUILayout.TextField(
                new GUIContent("Factor Parameter", "Float parameter name used as multiplier (0..1)."),
                factorParameterName
            );
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(ParamPrefKey, factorParameterName ?? string.Empty);
            }

            EditorGUI.BeginChangeCheck();
            autoApplyOnEnterPlay = EditorGUILayout.ToggleLeft("Auto-apply on Enter Play Mode", autoApplyOnEnterPlay);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(AutoPlayPrefKey, autoApplyOnEnterPlay);
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    autoApplyOnEnterPlay
                        ? "Configured. Enter Play Mode to patch temporary VRCFury controllers."
                        : "Auto-apply is disabled.",
                    MessageType.None
                );
            }
            else
            {
                DrawRuntimeControls(avatarRoot);
            }
        }

        if (!string.IsNullOrEmpty(runtimeStatus))
        {
            EditorGUILayout.HelpBox(runtimeStatus, runtimeStatusIsError ? MessageType.Warning : MessageType.Info);
        }

        DrawActiveVersionCorrectiveLinksDebug();

        EditorGUILayout.EndVertical();
    }

    public void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!testEnabled) return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            runtimeStatus = null;
            runtimeStatusIsError = false;
            if (autoApplyOnEnterPlay)
            {
                StartRetryPatch();
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            remainingRetries = 0;
            nextRetryTime = 0;
        }
    }

    private void DrawActiveVersionCorrectiveLinksDebug()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Active Links (Current Applied Version)", EditorStyles.miniBoldLabel);

        var version = editor?.ultiPawTarget?.appliedUltiPawVersion;
        GameObject avatarRoot = editor?.ultiPawTarget != null ? editor.ultiPawTarget.transform.root.gameObject : null;
        if (version?.customBlendshapes == null || version.customBlendshapes.Length == 0)
        {
            EditorGUILayout.HelpBox("No applied version or no blendshape data.", MessageType.None);
            return;
        }

        var lines = new List<string>();
        foreach (var source in version.customBlendshapes)
        {
            if (source?.correctiveBlendshapes == null || source.correctiveBlendshapes.Length == 0) continue;

            string factorMode = VRCFuryService.Instance.GetFactorDebugLabel(avatarRoot, source);
            foreach (var link in source.correctiveBlendshapes)
            {
                if (link == null) continue;
                if (string.IsNullOrWhiteSpace(link.blendshapeToFix) || string.IsNullOrWhiteSpace(link.fixingBlendshape)) continue;
                lines.Add($"{source.name}: {link.blendshapeToFix} -> {link.fixingBlendshape} ({factorMode})");
            }
        }

        if (lines.Count == 0)
        {
            EditorGUILayout.HelpBox("No corrective links found on applied version.", MessageType.None);
            return;
        }

        EditorGUILayout.HelpBox(string.Join("\n", lines), MessageType.None);
    }

    private void DrawRuntimeControls(GameObject avatarRoot)
    {
        if (remainingRetries > 0)
        {
            EditorGUILayout.LabelField($"Waiting for VRCFury temp controllers... retries left: {remainingRetries}");
        }

        if (GUILayout.Button("Apply Now (Play Mode)"))
        {
            TryApplyOnce(avatarRoot, finalAttempt: true);
        }

        EditorApplication.QueuePlayerLoopUpdate();
    }

    private void StartRetryPatch()
    {
        remainingRetries = MaxRetries;
        nextRetryTime = EditorApplication.timeSinceStartup + 0.1;
        EditorApplication.update -= RetryTick;
        EditorApplication.update += RetryTick;
    }

    private void RetryTick()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= RetryTick;
            remainingRetries = 0;
            return;
        }

        if (remainingRetries <= 0)
        {
            EditorApplication.update -= RetryTick;
            runtimeStatusIsError = true;
            if (string.IsNullOrEmpty(runtimeStatus))
            {
                runtimeStatus = "Timed out waiting for matching temporary controllers.";
            }
            return;
        }

        if (EditorApplication.timeSinceStartup < nextRetryTime) return;
        nextRetryTime = EditorApplication.timeSinceStartup + RetryIntervalSeconds;

        GameObject avatarRoot = editor?.ultiPawTarget != null ? editor.ultiPawTarget.transform.root.gameObject : null;
        if (avatarRoot == null)
        {
            remainingRetries = 0;
            runtimeStatus = "Avatar root missing during play mode.";
            runtimeStatusIsError = true;
            EditorApplication.update -= RetryTick;
            return;
        }

        bool completed = TryApplyOnce(avatarRoot, finalAttempt: remainingRetries == 1);
        remainingRetries--;
        if (completed)
        {
            EditorApplication.update -= RetryTick;
            remainingRetries = 0;
        }
    }

    private bool TryApplyOnce(GameObject avatarRoot, bool finalAttempt)
    {
        ResolveTargetRendererFromPathIfNeeded(avatarRoot);
        if (targetRenderer == null)
        {
            targetRenderer = FindDefaultBodyRenderer(avatarRoot);
            targetRendererPath = GetRendererPath(avatarRoot, targetRenderer);
        }

        var result = BlendShapeLinkService.Instance.ApplyFactorLinkToTemporaryControllers(
            avatarRoot,
            targetRenderer,
            sourceBlendshape,
            destinationBlendshape,
            factorParameterName
        );

        if (result.success)
        {
            runtimeStatusIsError = false;
            runtimeStatus = result.message;
            UltiPawLogger.Log("[UltiPaw] " + result.message);
            return true;
        }

        // Retry-worthy outcomes: temp controllers not ready yet or source curve not present yet.
        bool retryWorthy =
            result.message.IndexOf("No temporary controllers found yet", StringComparison.OrdinalIgnoreCase) >= 0 ||
            result.message.IndexOf("No matching source blendshape curves", StringComparison.OrdinalIgnoreCase) >= 0;

        if (retryWorthy && !finalAttempt)
        {
            runtimeStatusIsError = false;
            runtimeStatus = result.message;
            return false;
        }

        runtimeStatusIsError = true;
        runtimeStatus = result.message;
        return true;
    }

    private void DrawBlendshapeSelectors()
    {
        var names = GetBlendshapeNames(targetRenderer);
        bool hasNames = names.Count > 0;

        if (!hasNames)
        {
            sourceBlendshape = EditorGUILayout.TextField("Source Blendshape", sourceBlendshape);
            destinationBlendshape = EditorGUILayout.TextField("Destination Blendshape", destinationBlendshape);
            EditorPrefs.SetString(SourcePrefKey, sourceBlendshape ?? string.Empty);
            EditorPrefs.SetString(DestinationPrefKey, destinationBlendshape ?? string.Empty);
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

    private void ResolveTargetRendererFromPathIfNeeded(GameObject avatarRoot)
    {
        if (avatarRoot == null) return;
        if (targetRenderer != null && targetRenderer.transform.IsChildOf(avatarRoot.transform)) return;

        if (!string.IsNullOrEmpty(targetRendererPath))
        {
            var t = avatarRoot.transform.Find(targetRendererPath);
            if (t != null)
            {
                var resolved = t.GetComponent<SkinnedMeshRenderer>();
                if (resolved != null)
                {
                    targetRenderer = resolved;
                    return;
                }
            }
        }

        targetRenderer = FindDefaultBodyRenderer(avatarRoot);
        targetRendererPath = GetRendererPath(avatarRoot, targetRenderer);
    }

    private static string GetRendererPath(GameObject avatarRoot, SkinnedMeshRenderer renderer)
    {
        if (avatarRoot == null || renderer == null) return string.Empty;
        if (!renderer.transform.IsChildOf(avatarRoot.transform)) return string.Empty;
        return AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform);
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
