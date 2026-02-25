#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

public partial class BlendShapeLinkService
{
    private static bool TryGetBlendshapeCurve(AnimationClip clip, string targetPath, string propertyName, out AnimationCurve curve)
    {
        curve = null;
        if (clip == null || string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(propertyName)) return false;

        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(SkinnedMeshRenderer)) continue;
            if (!string.Equals(binding.path, targetPath, StringComparison.Ordinal)) continue;
            if (!string.Equals(binding.propertyName, propertyName, StringComparison.Ordinal)) continue;
            curve = AnimationUtility.GetEditorCurve(clip, binding);
            return curve != null;
        }

        return false;
    }

    private static AnimationClip CreateVariantClipWithBlendshapeCurve(AnimatorController controller, AnimationClip sourceClip, string destinationPath, string destinationProperty, AnimationCurve sourceCurve)
    {
        if (sourceClip == null || sourceCurve == null) return null;
        if (string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(destinationProperty)) return null;

        var variantClip = UnityEngine.Object.Instantiate(sourceClip);
        variantClip.name = BuildName(VariantPrefix, sourceClip.name);
        variantClip.hideFlags = HideFlags.HideInHierarchy;

        var binding = new EditorCurveBinding
        {
            path = destinationPath,
            type = typeof(SkinnedMeshRenderer),
            propertyName = destinationProperty
        };

        AnimationUtility.SetEditorCurve(variantClip, binding, sourceCurve);

        AttachAsSubAsset(controller, variantClip);
        EditorUtility.SetDirty(variantClip);
        return variantClip;
    }

    private static AnimationClip CreateVariantClipWithConstantBlendshape(AnimatorController controller, AnimationClip sourceClip, string destinationPath, string destinationProperty, float value)
    {
        if (sourceClip == null) return null;
        if (string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(destinationProperty)) return null;

        var variantClip = UnityEngine.Object.Instantiate(sourceClip);
        variantClip.name = BuildName(VariantPrefix, sourceClip.name);
        variantClip.hideFlags = HideFlags.HideInHierarchy;

        float endTime = Mathf.Max(sourceClip.length, 1f / 60f);
        var curve = new AnimationCurve(new Keyframe(0f, value), new Keyframe(endTime, value));
        var binding = new EditorCurveBinding
        {
            path = destinationPath,
            type = typeof(SkinnedMeshRenderer),
            propertyName = destinationProperty
        };

        AnimationUtility.SetEditorCurve(variantClip, binding, curve);

        AttachAsSubAsset(controller, variantClip);
        EditorUtility.SetDirty(variantClip);
        return variantClip;
    }

    private static AnimationClip CreateVariantClipWithAnimationOverlay(AnimatorController controller, AnimationClip sourceClip, AnimationClip overlayClip)
    {
        if (sourceClip == null || overlayClip == null) return null;

        var variantClip = UnityEngine.Object.Instantiate(sourceClip);
        variantClip.name = BuildName(VariantPrefix, sourceClip.name);
        variantClip.hideFlags = HideFlags.HideInHierarchy;

        foreach (var binding in AnimationUtility.GetCurveBindings(overlayClip))
        {
            var overlayCurve = AnimationUtility.GetEditorCurve(overlayClip, binding);
            if (overlayCurve == null) continue;
            AnimationUtility.SetEditorCurve(variantClip, binding, overlayCurve);
        }

        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(overlayClip))
        {
            var refCurve = AnimationUtility.GetObjectReferenceCurve(overlayClip, binding);
            if (refCurve == null || refCurve.Length == 0) continue;
            AnimationUtility.SetObjectReferenceCurve(variantClip, binding, refCurve);
        }

        AttachAsSubAsset(controller, variantClip);
        EditorUtility.SetDirty(variantClip);
        return variantClip;
    }

    private static AnimationClip CreateVariantClipWithScaledAnimationOverlay(AnimatorController controller, AnimationClip sourceClip, AnimationClip overlayClip, AnimationCurve sourceFactorCurve)
    {
        if (sourceClip == null || overlayClip == null || sourceFactorCurve == null) return null;

        var variantClip = UnityEngine.Object.Instantiate(sourceClip);
        variantClip.name = BuildName(VariantPrefix, sourceClip.name);
        variantClip.hideFlags = HideFlags.HideInHierarchy;

        foreach (var binding in AnimationUtility.GetCurveBindings(overlayClip))
        {
            var overlayCurve = AnimationUtility.GetEditorCurve(overlayClip, binding);
            if (overlayCurve == null) continue;

            var baseCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            var blended = BlendCurvesBySourceActivation(baseCurve, overlayCurve, sourceFactorCurve, sourceClip.length, overlayClip.length);
            AnimationUtility.SetEditorCurve(variantClip, binding, blended);
        }

        AttachAsSubAsset(controller, variantClip);
        EditorUtility.SetDirty(variantClip);
        return variantClip;
    }

    private static AnimationCurve BlendCurvesBySourceActivation(AnimationCurve baseCurve, AnimationCurve overlayCurve, AnimationCurve sourceFactorCurve, float sourceLength, float overlayLength)
    {
        float endTime = Mathf.Max(sourceLength, overlayLength, 1f / 60f);
        var times = new SortedSet<float> { 0f, endTime };
        AddCurveKeyTimes(times, baseCurve);
        AddCurveKeyTimes(times, overlayCurve);
        AddCurveKeyTimes(times, sourceFactorCurve);

        var keys = new List<Keyframe>(times.Count);
        foreach (var t in times)
        {
            float baseValue = baseCurve != null ? baseCurve.Evaluate(t) : 0f;
            float overlayValue = overlayCurve.Evaluate(t);
            float activation = Mathf.Clamp01(sourceFactorCurve.Evaluate(t) / 100f);
            keys.Add(new Keyframe(t, Mathf.Lerp(baseValue, overlayValue, activation)));
        }

        return new AnimationCurve(keys.ToArray());
    }

    private static void AddCurveKeyTimes(ISet<float> times, AnimationCurve curve)
    {
        if (times == null || curve == null) return;
        var keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
        {
            times.Add(keys[i].time);
        }
    }

    private static BlendTree CreateWrapperTree(AnimatorController controller, AnimationClip originalClip, AnimationClip variantClip, string factorParameterName)
    {
        var tree = new BlendTree
        {
            name = BuildName(WrapperPrefix, originalClip.name),
            blendType = BlendTreeType.Simple1D,
            blendParameter = factorParameterName,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };

        var children = new ChildMotion[2];
        children[0] = new ChildMotion { motion = originalClip, threshold = 0f, timeScale = 1f };
        children[1] = new ChildMotion { motion = variantClip, threshold = 1f, timeScale = 1f };
        tree.children = children;

        AttachAsSubAsset(controller, tree);
        EditorUtility.SetDirty(tree);
        return tree;
    }

    private static string BuildName(string prefix, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return prefix + "Clip";
        return prefix + sourceName;
    }

    private static void AttachAsSubAsset(AnimatorController controller, UnityEngine.Object obj)
    {
        if (controller == null || obj == null) return;

        string path = AssetDatabase.GetAssetPath(controller);
        if (string.IsNullOrEmpty(path)) return;
        if (AssetDatabase.Contains(obj)) return;

        AssetDatabase.AddObjectToAsset(obj, controller);
    }
}
#endif
