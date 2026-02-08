#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

public class BlendShapeLinkService
{
    private static BlendShapeLinkService _instance;
    public static BlendShapeLinkService Instance => _instance ??= new BlendShapeLinkService();

    private const string WrapperPrefix = "UP_BSLINK_FACTOR_";
    private const string VariantPrefix = "UP_BSLINK_VARIANT_";

    public struct ApplyResult
    {
        public bool success;
        public int controllersProcessed;
        public int clipsWrapped;
        public int statesRewritten;
        public int wrappersAlreadyPresent;
        public string message;
    }

    public ApplyResult ApplyFactorLinkToTemporaryControllers(
        GameObject avatarRoot,
        SkinnedMeshRenderer targetRenderer,
        string sourceBlendshape,
        string destinationBlendshape,
        string factorParameterName,
        float? factorDefaultValue = null
    )
    {
        if (avatarRoot == null) return Fail("Avatar root is null.");
        if (targetRenderer == null) return Fail("Target mesh is missing.");
        if (targetRenderer.sharedMesh == null) return Fail("Target mesh renderer has no shared mesh.");
        if (string.IsNullOrWhiteSpace(sourceBlendshape)) return Fail("Source blendshape is empty.");
        if (string.IsNullOrWhiteSpace(destinationBlendshape)) return Fail("Destination blendshape is empty.");
        if (string.IsNullOrWhiteSpace(factorParameterName)) return Fail("Factor parameter name is empty.");
        if (targetRenderer.sharedMesh.GetBlendShapeIndex(sourceBlendshape) < 0)
            return Fail($"Source blendshape '{sourceBlendshape}' was not found on target mesh.");
        if (targetRenderer.sharedMesh.GetBlendShapeIndex(destinationBlendshape) < 0)
            return Fail($"Destination blendshape '{destinationBlendshape}' was not found on target mesh.");
        if (!targetRenderer.transform.IsChildOf(avatarRoot.transform))
            return Fail("Target mesh is not under avatar root.");

        string targetPath = AnimationUtility.CalculateTransformPath(targetRenderer.transform, avatarRoot.transform);
        string sourceProperty = "blendShape." + sourceBlendshape;
        string destinationProperty = "blendShape." + destinationBlendshape;

        var controllers = CollectTemporaryControllers(avatarRoot);
        if (controllers.Count == 0)
            return Fail("No temporary controllers found yet.");

        int clipsWrapped = 0;
        int statesRewritten = 0;
        int wrappersAlreadyPresent = 0;
        int controllersProcessed = 0;
        bool anyChanged = false;

        foreach (var controller in controllers)
        {
            if (!EnsureFloatParameter(controller, factorParameterName, factorDefaultValue, out var paramError))
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
                if (RewriteStateMachine(
                        layer.stateMachine,
                        targetPath,
                        sourceProperty,
                        destinationProperty,
                        factorParameterName,
                        clipCache,
                        visitedStateMachines,
                        visitedTrees,
                        ref clipsWrapped,
                        ref statesRewritten,
                        ref wrappersAlreadyPresent))
                {
                    controllerChanged = true;
                }
            }

            if (controllerChanged)
            {
                controllersProcessed++;
                anyChanged = true;

                // Ensure live animators in Play Mode receive the parameter value immediately
                if (Application.isPlaying && factorDefaultValue.HasValue)
                {
                    foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true))
                    {
                        if (animator == null) continue;
                        var runtimeCtrl = animator.runtimeAnimatorController;
                        bool matches = false;
                        if (runtimeCtrl == controller) matches = true;
                        else if (runtimeCtrl is AnimatorOverrideController odc && odc.runtimeAnimatorController == controller) matches = true;

                        if (matches)
                        {
                            animator.SetFloat(factorParameterName, Mathf.Clamp01(factorDefaultValue.Value));
                        }
                    }
                }
            }
        }

        if (anyChanged || wrappersAlreadyPresent > 0)
        {
            string status = anyChanged ? "Patched" : "Already patched";
            return new ApplyResult
            {
                success = true,
                controllersProcessed = controllersProcessed,
                clipsWrapped = clipsWrapped,
                statesRewritten = statesRewritten,
                wrappersAlreadyPresent = wrappersAlreadyPresent,
                message =
                    $"{status} {controllersProcessed} temp controller(s), wrapped {clipsWrapped} clip(s), rewrote {statesRewritten} state/tree motion reference(s)."
            };
        }

        return Fail("No matching source blendshape curves were found in temporary controllers.");
    }

    private static ApplyResult Fail(string message)
    {
        return new ApplyResult
        {
            success = false,
            controllersProcessed = 0,
            clipsWrapped = 0,
            statesRewritten = 0,
            wrappersAlreadyPresent = 0,
            message = message
        };
    }

    private static List<AnimatorController> CollectTemporaryControllers(GameObject avatarRoot)
    {
        var found = new HashSet<AnimatorController>();

        // Runtime Animator references on avatar hierarchy.
        foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true))
        {
            if (animator?.runtimeAnimatorController is AnimatorController ctrl && IsTemporaryController(ctrl))
            {
                found.Add(ctrl);
            }
        }

        // Descriptor playable layer references.
        var descriptor = avatarRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
        if (descriptor != null)
        {
            CollectFromLayers(descriptor.baseAnimationLayers, found);
            CollectFromLayers(descriptor.specialAnimationLayers, found);
        }

        return found.ToList();
    }

    private static void CollectFromLayers(VRCAvatarDescriptor.CustomAnimLayer[] layers, ISet<AnimatorController> found)
    {
        if (layers == null) return;
        foreach (var layer in layers)
        {
            if (layer.animatorController is AnimatorController ctrl && IsTemporaryController(ctrl))
            {
                found.Add(ctrl);
            }
        }
    }

    private static bool IsTemporaryController(AnimatorController controller)
    {
        if (controller == null) return false;

        // In-memory generated controller.
        if (!EditorUtility.IsPersistent(controller)) return true;

        string path = AssetDatabase.GetAssetPath(controller);
        if (string.IsNullOrEmpty(path)) return true;

        string normalized = path.Replace('\\', '/').ToLowerInvariant();
        if (normalized.Contains("/temp/")) return true;
        if (normalized.Contains("/library/") && normalized.Contains("vrcfury")) return true;
        if (normalized.Contains("proxy_")) return true;
        return false;
    }

    private static bool EnsureFloatParameter(AnimatorController controller, string parameterName, float? defaultValue, out string error)
    {
        error = null;
        var existing = controller.parameters.FirstOrDefault(p => p.name == parameterName);
        if (existing != null)
        {
            if (existing.type != AnimatorControllerParameterType.Float)
            {
                error = $"Controller '{controller.name}' has parameter '{parameterName}' with type {existing.type}.";
                return false;
            }
            if (defaultValue.HasValue)
            {
                var paramsCopy = controller.parameters;
                bool changed = false;
                for (int i = 0; i < paramsCopy.Length; i++)
                {
                    if (paramsCopy[i].name == parameterName)
                    {
                        paramsCopy[i].defaultFloat = Mathf.Clamp01(defaultValue.Value);
                        changed = true;
                        break;
                    }
                }
                if (changed) controller.parameters = paramsCopy;
            }
            return true;
        }

        controller.AddParameter(parameterName, AnimatorControllerParameterType.Float);
        if (defaultValue.HasValue)
        {
            var paramsCopy = controller.parameters;
            bool changed = false;
            for (int i = 0; i < paramsCopy.Length; i++)
            {
                if (paramsCopy[i].name == parameterName)
                {
                    paramsCopy[i].defaultFloat = Mathf.Clamp01(defaultValue.Value);
                    changed = true;
                    break;
                }
            }
            if (changed) controller.parameters = paramsCopy;
        }
        return true;
    }

    private static bool RewriteStateMachine(
        AnimatorStateMachine stateMachine,
        string targetPath,
        string sourceProperty,
        string destinationProperty,
        string factorParameterName,
        IDictionary<AnimationClip, Motion> clipCache,
        ISet<AnimatorStateMachine> visitedStateMachines,
        ISet<BlendTree> visitedTrees,
        ref int clipsWrapped,
        ref int statesRewritten,
        ref int wrappersAlreadyPresent
    )
    {
        if (visitedStateMachines.Contains(stateMachine)) return false;
        visitedStateMachines.Add(stateMachine);

        bool changed = false;

        foreach (var childState in stateMachine.states)
        {
            var state = childState.state;
            if (state == null || state.motion == null) continue;

            Motion rewritten = RewriteMotion(
                state.motion,
                targetPath,
                sourceProperty,
                destinationProperty,
                factorParameterName,
                clipCache,
                visitedTrees,
                ref clipsWrapped,
                ref wrappersAlreadyPresent,
                out bool internalChanged
            );

            if (rewritten == null) continue;

            if (rewritten != state.motion)
            {
                state.motion = rewritten;
                statesRewritten++;
                changed = true;
            }
            else if (internalChanged)
            {
                changed = true;
            }
        }

        foreach (var childMachine in stateMachine.stateMachines)
        {
            if (childMachine.stateMachine == null) continue;
            if (RewriteStateMachine(
                    childMachine.stateMachine,
                    targetPath,
                    sourceProperty,
                    destinationProperty,
                    factorParameterName,
                    clipCache,
                    visitedStateMachines,
                    visitedTrees,
                    ref clipsWrapped,
                    ref statesRewritten,
                    ref wrappersAlreadyPresent))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static Motion RewriteMotion(
        Motion motion,
        string targetPath,
        string sourceProperty,
        string destinationProperty,
        string factorParameterName,
        IDictionary<AnimationClip, Motion> clipCache,
        ISet<BlendTree> visitedTrees,
        ref int clipsWrapped,
        ref int wrappersAlreadyPresent,
        out bool changed
    )
    {
        changed = false;
        if (motion == null) return null;

        if (motion is BlendTree tree)
        {
            if (visitedTrees.Contains(tree)) return tree;
            visitedTrees.Add(tree);

            if (IsWrapperTree(tree, factorParameterName))
            {
                if (TryAugmentExistingWrapperVariant(
                        tree,
                        targetPath,
                        sourceProperty,
                        destinationProperty
                    ))
                {
                    changed = true;
                }
                wrappersAlreadyPresent++;
                return tree;
            }

            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var childMotion = children[i].motion;
                var rewritten = RewriteMotion(
                    childMotion,
                    targetPath,
                    sourceProperty,
                    destinationProperty,
                    factorParameterName,
                    clipCache,
                    visitedTrees,
                    ref clipsWrapped,
                    ref wrappersAlreadyPresent,
                    out bool childInternalChanged
                );

                if (rewritten != null && (rewritten != childMotion || childInternalChanged))
                {
                    children[i].motion = rewritten;
                    changed = true;
                }
            }

            if (changed)
            {
                tree.children = children;
                EditorUtility.SetDirty(tree);
            }
            return tree;
        }

        if (!(motion is AnimationClip clip)) return motion;

        if (clipCache.TryGetValue(clip, out var cached))
        {
            return cached;
        }

        if (!TryGetSourceBinding(clip, targetPath, sourceProperty, out var sourceBinding, out var sourceCurve))
        {
            clipCache[clip] = clip;
            return clip;
        }

        var variantClip = CreateVariantClip(clip, sourceBinding, sourceCurve, destinationProperty);
        var wrapperTree = CreateWrapperTree(clip, variantClip, factorParameterName);
        clipCache[clip] = wrapperTree;
        clipsWrapped++;
        changed = true;
        return wrapperTree;
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

    private static bool TryGetSourceBinding(
        AnimationClip clip,
        string targetPath,
        string sourceProperty,
        out EditorCurveBinding sourceBinding,
        out AnimationCurve sourceCurve
    )
    {
        sourceBinding = default;
        sourceCurve = null;

        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(SkinnedMeshRenderer)) continue;
            if (!string.Equals(binding.path, targetPath, StringComparison.Ordinal)) continue;
            if (!string.Equals(binding.propertyName, sourceProperty, StringComparison.Ordinal)) continue;

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) continue;
            sourceBinding = binding;
            sourceCurve = curve;
            return true;
        }

        return false;
    }

    private static bool TryAugmentExistingWrapperVariant(
        BlendTree wrapperTree,
        string targetPath,
        string sourceProperty,
        string destinationProperty
    )
    {
        if (wrapperTree == null) return false;
        var children = wrapperTree.children;
        if (children == null || children.Length < 2) return false;

        // Recursively find the original clip in case of nested wrappers
        AnimationClip originalClip = null;
        Motion m0 = children[0].motion;
        while (m0 != null)
        {
            if (m0 is AnimationClip c)
            {
                originalClip = c;
                break;
            }
            if (m0 is BlendTree t && t.name.StartsWith(WrapperPrefix, StringComparison.Ordinal))
            {
                var tChildren = t.children;
                if (tChildren != null && tChildren.Length > 0)
                {
                    m0 = tChildren[0].motion;
                    continue;
                }
            }
            break;
        }

        if (originalClip == null) return false;
        if (!(children[1].motion is AnimationClip variantClip)) return false;

        if (!TryGetSourceBinding(originalClip, targetPath, sourceProperty, out var sourceBinding, out var sourceCurve))
        {
            return false;
        }

        var destinationBinding = sourceBinding;
        destinationBinding.propertyName = destinationProperty;

        var existing = AnimationUtility.GetEditorCurve(variantClip, destinationBinding);
        if (existing != null) return false;

        AnimationUtility.SetEditorCurve(variantClip, destinationBinding, sourceCurve);
        EditorUtility.SetDirty(variantClip);
        return true;
    }

    private static AnimationClip CreateVariantClip(
        AnimationClip sourceClip,
        EditorCurveBinding sourceBinding,
        AnimationCurve sourceCurve,
        string destinationProperty
    )
    {
        var variantClip = UnityEngine.Object.Instantiate(sourceClip);
        variantClip.name = BuildName(VariantPrefix, sourceClip.name);
        variantClip.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;

        var destinationBinding = sourceBinding;
        destinationBinding.propertyName = destinationProperty;
        AnimationUtility.SetEditorCurve(variantClip, destinationBinding, sourceCurve);
        EditorUtility.SetDirty(variantClip);
        return variantClip;
    }

    private static BlendTree CreateWrapperTree(
        AnimationClip originalClip,
        AnimationClip variantClip,
        string factorParameterName
    )
    {
        var tree = new BlendTree
        {
            name = BuildName(WrapperPrefix, originalClip.name),
            blendType = BlendTreeType.Simple1D,
            blendParameter = factorParameterName,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor
        };

        var children = new ChildMotion[2];
        children[0] = new ChildMotion { motion = originalClip, threshold = 0f, timeScale = 1f };
        children[1] = new ChildMotion { motion = variantClip, threshold = 1f, timeScale = 1f };
        tree.children = children;
        return tree;
    }

    private static string BuildName(string prefix, string sourceName)
    {
        return string.IsNullOrWhiteSpace(sourceName) ? prefix + "Clip" : prefix + sourceName;
    }
}
#endif
