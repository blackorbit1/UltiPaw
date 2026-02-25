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
    private static ApplyResult ApplyPlannedLinks(GameObject avatarRoot, List<PlannedLink> plannedLinks, string sourceLabel)
    {
        if (avatarRoot == null) return FailApply("Avatar root is null.");
        if (plannedLinks == null || plannedLinks.Count == 0) return FailApply("No corrective links to apply.");

        var controllers = CollectVrcFuryBuiltControllers(avatarRoot);
        if (controllers.Count == 0)
        {
            return FailApply("No VRCFury temporary AnimatorController found on avatar descriptor.");
        }

        int linksProcessed = 0;
        int clipsWrapped = 0;
        int statesRewritten = 0;
        bool anyControllerChanged = false;
        var changedControllers = new HashSet<AnimatorController>();

        foreach (var planned in plannedLinks)
        {
            bool thisLinkChangedAnyController = false;
            foreach (var controller in controllers)
            {
                if (!EnsureFloatParameter(controller, planned.factorParameterName, planned.setFactorDefaultValue, planned.factorDefaultValue, out var paramError))
                {
                    Debug.LogWarning("[UltiPaw] " + paramError);
                    continue;
                }

                bool controllerChanged = false;
                var clipCache = new Dictionary<AnimationClip, Motion>();
                var visitedStateMachines = new HashSet<AnimatorStateMachine>();
                var visitedTrees = new HashSet<BlendTree>();

                foreach (var layer in controller.layers)
                {
                    if (layer?.stateMachine == null) continue;
                    if (RewriteStateMachine(controller, layer.stateMachine, planned, clipCache, visitedStateMachines, visitedTrees, ref clipsWrapped, ref statesRewritten))
                    {
                        controllerChanged = true;
                        thisLinkChangedAnyController = true;
                    }
                }

                if (!controllerChanged) continue;
                changedControllers.Add(controller);
                anyControllerChanged = true;
                EditorUtility.SetDirty(controller);

                // Register applied link for debug window
                int ctrlId = controller.GetInstanceID();
                if (!_appliedLinksRegistry.TryGetValue(ctrlId, out var records))
                {
                    records = new List<AppliedLinkRecord>();
                    _appliedLinksRegistry[ctrlId] = records;
                }
                records.Add(new AppliedLinkRecord
                {
                    controllerName = controller.name,
                    controllerAssetPath = AssetDatabase.GetAssetPath(controller),
                    factorParameterName = planned.factorParameterName,
                    targetRendererPath = planned.targetRendererPath,
                    toFixName = planned.toFixName,
                    fixedByName = planned.fixedByName,
                    sourceLabel = sourceLabel
                });
            }

            if (thisLinkChangedAnyController) linksProcessed++;
        }

        if (anyControllerChanged)
        {
            AssetDatabase.SaveAssets();
            return new ApplyResult
            {
                success = true,
                linksProcessed = linksProcessed,
                controllersProcessed = changedControllers.Count,
                clipsWrapped = clipsWrapped,
                statesRewritten = statesRewritten,
                message = $"Applied {sourceLabel} corrective links: {linksProcessed} link(s), {changedControllers.Count} controller(s), {clipsWrapped} wrapped clip(s), {statesRewritten} rewritten state/tree motion reference(s)."
            };
        }

        return FailApply("No matching blendshape curves or animation motions were found in VRCFury temporary controllers.");
    }

    private static List<AnimatorController> CollectVrcFuryBuiltControllers(GameObject avatarRoot)
    {
        var found = new HashSet<AnimatorController>();
        var descriptor = avatarRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);

        if (descriptor != null)
        {
            CollectFromLayers(descriptor.baseAnimationLayers, found);
            CollectFromLayers(descriptor.specialAnimationLayers, found);
        }

        var animator = avatarRoot.GetComponentInChildren<Animator>(true);
        if (animator?.runtimeAnimatorController is AnimatorController controllerFromAnimator && IsVrcFuryBuiltController(controllerFromAnimator))
        {
            found.Add(controllerFromAnimator);
        }

        return found.ToList();
    }

    private static void CollectFromLayers(VRCAvatarDescriptor.CustomAnimLayer[] layers, ISet<AnimatorController> found)
    {
        if (layers == null) return;
        foreach (var layer in layers)
        {
            if (layer.isDefault) continue;
            var ctrl = layer.animatorController as AnimatorController;
            if (ctrl == null) continue;
            if (!IsVrcFuryBuiltController(ctrl)) continue;
            found.Add(ctrl);
        }
    }

    private static bool IsVrcFuryBuiltController(AnimatorController controller)
    {
        string path = AssetDatabase.GetAssetPath(controller);
        if (string.IsNullOrEmpty(path)) return false;
        string normalized = path.Replace("\\", "/");
        return normalized.IndexOf("com.vrcfury.temp", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool EnsureFloatParameter(AnimatorController controller, string parameterName, bool setDefaultValue, float defaultValue, out string error)
    {
        error = null;
        var existing = controller.parameters.FirstOrDefault(p => p.name == parameterName);
        if (existing != null)
        {
            if (existing.type != AnimatorControllerParameterType.Float)
            {
                error = $"Controller '{controller.name}' already has parameter '{parameterName}' but it is not Float.";
                return false;
            }

            if (setDefaultValue && Mathf.Abs(existing.defaultFloat - defaultValue) > 0.0001f)
            {
                existing.defaultFloat = defaultValue;
                EditorUtility.SetDirty(controller);
            }

            return true;
        }

        controller.AddParameter(parameterName, AnimatorControllerParameterType.Float);
        var created = controller.parameters.FirstOrDefault(p => p.name == parameterName);
        if (created != null && setDefaultValue) created.defaultFloat = defaultValue;

        EditorUtility.SetDirty(controller);
        return true;
    }

    private static bool RewriteStateMachine(
        AnimatorController controller,
        AnimatorStateMachine stateMachine,
        PlannedLink planned,
        IDictionary<AnimationClip, Motion> clipCache,
        ISet<AnimatorStateMachine> visitedStateMachines,
        ISet<BlendTree> visitedTrees,
        ref int clipsWrapped,
        ref int statesRewritten)
    {
        if (visitedStateMachines.Contains(stateMachine)) return false;
        visitedStateMachines.Add(stateMachine);

        bool changed = false;

        foreach (var childState in stateMachine.states)
        {
            var state = childState.state;
            if (state == null || state.motion == null) continue;

            Motion rewritten = RewriteMotion(controller, state.motion, planned, clipCache, visitedTrees, ref clipsWrapped);

            if (rewritten == null || rewritten == state.motion) continue;
            state.motion = rewritten;
            EditorUtility.SetDirty(state);
            statesRewritten++;
            changed = true;
        }

        foreach (var childMachine in stateMachine.stateMachines)
        {
            if (childMachine.stateMachine == null) continue;
            if (RewriteStateMachine(controller, childMachine.stateMachine, planned, clipCache, visitedStateMachines, visitedTrees, ref clipsWrapped, ref statesRewritten))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static Motion RewriteMotion(
        AnimatorController controller,
        Motion motion,
        PlannedLink planned,
        IDictionary<AnimationClip, Motion> clipCache,
        ISet<BlendTree> visitedTrees,
        ref int clipsWrapped)
    {
        if (motion == null) return null;

        if (motion is BlendTree tree)
        {
            if (visitedTrees.Contains(tree)) return tree;
            visitedTrees.Add(tree);

            if (IsWrapperTree(tree, planned.factorParameterName))
            {
                // Already wrapped for this factor. Apply subsequent links only to the
                // variant child so links can stack without re-wrapping.
                var wrapperChildren = tree.children;
                if (wrapperChildren != null && wrapperChildren.Length == 2)
                {
                    var variantMotion = wrapperChildren[1].motion;
                    var rewrittenVariant = RewriteMotion(controller, variantMotion, planned, clipCache, visitedTrees, ref clipsWrapped);
                    if (rewrittenVariant != null && rewrittenVariant != variantMotion)
                    {
                        wrapperChildren[1].motion = rewrittenVariant;
                        tree.children = wrapperChildren;
                        EditorUtility.SetDirty(tree);
                    }
                }

                return tree;
            }

            bool changed = false;
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var childMotion = children[i].motion;
                var rewritten = RewriteMotion(controller, childMotion, planned, clipCache, visitedTrees, ref clipsWrapped);
                if (rewritten == null || rewritten == childMotion) continue;
                children[i].motion = rewritten;
                changed = true;
            }

            if (changed)
            {
                tree.children = children;
                EditorUtility.SetDirty(tree);
            }

            return tree;
        }

        if (!(motion is AnimationClip clip)) return motion;
        if (clipCache.TryGetValue(clip, out var cached)) return cached;

        var variantClip = TryCreateVariantClip(controller, clip, planned);
        if (variantClip == null)
        {
            clipCache[clip] = clip;
            return clip;
        }

        var wrapperTree = CreateWrapperTree(controller, clip, variantClip, planned.factorParameterName);
        clipCache[clip] = wrapperTree;
        clipsWrapped++;
        return wrapperTree;
    }

    private static AnimationClip TryCreateVariantClip(AnimatorController controller, AnimationClip sourceClip, PlannedLink planned)
    {
        if (sourceClip == null) return null;
        if (planned.fixedByType == CorrectiveActivationType.Animation && planned.fixedByAnimationClip == null) return null;
        if (planned.toFixType == CorrectiveActivationType.Animation &&
            !DoesClipMatchAnimationTarget(sourceClip, planned.toFixName, planned.toFixAnimationSignature)) return null;

        if (planned.toFixType == CorrectiveActivationType.Blendshape && planned.fixedByType == CorrectiveActivationType.Blendshape)
        {
            if (!TryGetBlendshapeCurve(sourceClip, planned.sourcePath, planned.sourceProperty, out var sourceCurve)) return null;
            return CreateVariantClipWithBlendshapeCurve(controller, sourceClip, planned.destinationPath, planned.destinationProperty, sourceCurve);
        }

        if (planned.toFixType == CorrectiveActivationType.Animation && planned.fixedByType == CorrectiveActivationType.Blendshape)
        {
            if (string.IsNullOrWhiteSpace(planned.destinationPath) || string.IsNullOrWhiteSpace(planned.destinationProperty)) return null;
            return CreateVariantClipWithConstantBlendshape(controller, sourceClip, planned.destinationPath, planned.destinationProperty, 100f);
        }

        if (planned.toFixType == CorrectiveActivationType.Blendshape && planned.fixedByType == CorrectiveActivationType.Animation)
        {
            if (!TryGetBlendshapeCurve(sourceClip, planned.sourcePath, planned.sourceProperty, out var sourceCurve)) return null;
            return CreateVariantClipWithScaledAnimationOverlay(controller, sourceClip, planned.fixedByAnimationClip, sourceCurve);
        }

        if (planned.toFixType == CorrectiveActivationType.Animation && planned.fixedByType == CorrectiveActivationType.Animation)
        {
            return CreateVariantClipWithAnimationOverlay(controller, sourceClip, planned.fixedByAnimationClip);
        }

        return null;
    }

    private static bool DoesClipMatchAnimationTarget(AnimationClip clip, string targetName,
        List<AnimationBindingSignature> targetSignature)
    {
        if (clip == null || string.IsNullOrWhiteSpace(targetName)) return false;

        // Prefer semantic matching: same animated bindings with same sampled values.
        if (targetSignature != null && targetSignature.Count > 0 && DoesClipMatchSignature(clip, targetSignature))
        {
            return true;
        }

        string clipName = NormalizeAnimationKey(clip.name);
        string target = NormalizeAnimationKey(targetName);
        if (string.Equals(clipName, target, StringComparison.Ordinal)) return true;

        string clipPath = AssetDatabase.GetAssetPath(clip);
        string fileName = string.IsNullOrWhiteSpace(clipPath)
            ? string.Empty
            : NormalizeAnimationKey(System.IO.Path.GetFileNameWithoutExtension(clipPath));

        return string.Equals(fileName, target, StringComparison.Ordinal);
    }

    private static bool DoesClipMatchSignature(AnimationClip clip, List<AnimationBindingSignature> signature)
    {
        if (clip == null || signature == null || signature.Count == 0) return false;

        var candidates = CollectClipCurveCandidates(clip);
        if (candidates.Count == 0) return false;

        const float Epsilon = 0.001f;
        foreach (var sig in signature)
        {
            if (sig.sampleTimes == null || sig.sampleValues == null) continue;
            if (sig.sampleTimes.Length != sig.sampleValues.Length) continue;

            bool hasExactPath = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!IsSameTypeAndProperty(sig, candidate)) continue;
                if (!string.Equals(sig.path ?? string.Empty, candidate.path ?? string.Empty, StringComparison.Ordinal))
                    continue;

                hasExactPath = true;
                if (DoesCurveMatchSamples(candidate.curve, sig.sampleTimes, sig.sampleValues, Epsilon)) return true;
            }

            if (hasExactPath) continue;

            // Fallback for VRCFury-remapped paths: require same type/property and sampled values.
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!IsSameTypeAndProperty(sig, candidate)) continue;
                if (DoesCurveMatchSamples(candidate.curve, sig.sampleTimes, sig.sampleValues, Epsilon)) return true;
            }
        }

        return false;
    }

    private static List<CurveCandidate> CollectClipCurveCandidates(AnimationClip clip)
    {
        var output = new List<CurveCandidate>();
        if (clip == null) return output;

        var dedupe = new HashSet<string>(StringComparer.Ordinal);

        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            TryAppendCurveCandidate(output, dedupe, binding.path, binding.type, binding.propertyName, curve);
        }

        foreach (var curveData in AnimationUtility.GetAllCurves(clip, true))
        {
            if (curveData == null) continue;
            TryAppendCurveCandidate(output, dedupe, curveData.path, curveData.type, curveData.propertyName, curveData.curve);
        }

        return output;
    }

    private static void TryAppendCurveCandidate(
        List<CurveCandidate> output,
        HashSet<string> dedupe,
        string path,
        Type type,
        string propertyName,
        AnimationCurve curve)
    {
        if (output == null || dedupe == null) return;
        if (curve == null || curve.length == 0) return;
        if (string.IsNullOrWhiteSpace(propertyName)) return;

        string normalizedPath = path ?? string.Empty;
        string normalizedProperty = propertyName ?? string.Empty;
        string typeName = type != null ? type.FullName : string.Empty;
        string dedupeKey = normalizedPath + "|" + normalizedProperty + "|" + typeName;
        if (!dedupe.Add(dedupeKey)) return;

        output.Add(new CurveCandidate
        {
            path = normalizedPath,
            type = type,
            propertyName = normalizedProperty,
            curve = curve
        });
    }

    private static bool IsSameTypeAndProperty(AnimationBindingSignature signature, CurveCandidate candidate)
    {
        if (!string.Equals(signature.propertyName ?? string.Empty, candidate.propertyName ?? string.Empty, StringComparison.Ordinal))
            return false;

        // Allow one side to be null (GetAllCurves can have reduced type metadata in some cases).
        if (signature.type == null || candidate.type == null) return true;
        return signature.type == candidate.type;
    }

    private static bool DoesCurveMatchSamples(AnimationCurve curve, float[] sampleTimes, float[] sampleValues, float epsilon)
    {
        if (curve == null || sampleTimes == null || sampleValues == null) return false;
        if (sampleTimes.Length != sampleValues.Length) return false;

        for (int i = 0; i < sampleTimes.Length; i++)
        {
            float actual = curve.Evaluate(sampleTimes[i]);
            float expected = sampleValues[i];
            if (Mathf.Abs(actual - expected) > epsilon) return false;
        }

        return true;
    }

    private static bool IsWrapperTree(BlendTree tree, string factorParameterName)
    {
        if (tree == null) return false;
        if (!tree.name.StartsWith(WrapperPrefix, StringComparison.Ordinal)) return false;
        if (tree.blendType != BlendTreeType.Simple1D) return false;
        if (!string.Equals(tree.blendParameter, factorParameterName, StringComparison.Ordinal)) return false;
        var children = tree.children;
        return children != null && children.Length == 2;
    }

}
#endif
