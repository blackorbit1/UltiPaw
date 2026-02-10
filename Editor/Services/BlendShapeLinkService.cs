#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    private const string ConstParamPrefix = "UP_BSLINK_CONST_";

    public struct ConfigResult
    {
        public bool success;
        public string message;
    }

    public struct ApplyResult
    {
        public bool success;
        public int linksProcessed;
        public int controllersProcessed;
        public int clipsWrapped;
        public int statesRewritten;
        public string message;
    }

    public struct VersionLinkDebugInfo
    {
        public string targetRendererPath;
        public string sourceBlendshape;
        public string destinationBlendshape;
        public string factorParameterName;
        public bool usesConstantFactor;
        public float constantFactor;
        public string driverBlendshape;
    }

    private struct PlannedLink
    {
        public string targetRendererPath;
        public string sourceBlendshape;
        public string destinationBlendshape;
        public string sourceProperty;
        public string destinationProperty;
        public string factorParameterName;
        public bool setFactorDefaultValue;
        public float factorDefaultValue;
        public string driverBlendshape;
    }

    private struct VersionState
    {
        public UltiPawVersion version;
        public bool useCustomSliderSelection;
        public List<string> customSliderSelectionNames;
    }

    public void ApplyVersionLinks(GameObject avatarRoot, UltiPawVersion version, bool useCustomSliderSelection, List<string> customSliderSelectionNames)
    {
        if (avatarRoot == null || version == null) return;
        var planned = BuildVersionPlannedLinks(avatarRoot, version, useCustomSliderSelection, customSliderSelectionNames);
        ApplyPlannedLinks(avatarRoot, planned, "version");
    }

    public ConfigResult UpsertFactorLinkConfig(
        GameObject avatarRoot,
        SkinnedMeshRenderer targetRenderer,
        string sourceBlendshape,
        string destinationBlendshape,
        string factorParameterName,
        bool enabled = true)
    {
        if (!TryBuildAndValidateEntry(avatarRoot, targetRenderer, sourceBlendshape, destinationBlendshape, factorParameterName, enabled, out var entry, out var error))
        {
            return FailConfig(error);
        }

        var ultiPaw = FindUltiPaw(avatarRoot);
        if (ultiPaw == null)
        {
            return FailConfig("UltiPaw component not found on avatar root.");
        }

        if (ultiPaw.blendShapeFactorLinks == null)
        {
            ultiPaw.blendShapeFactorLinks = new List<BlendShapeFactorLinkEntry>();
        }

        int index = ultiPaw.blendShapeFactorLinks.FindIndex(x => IsSameManualLink(x, entry));
        Undo.RecordObject(ultiPaw, "Save BlendShape Factor Link");
        if (index >= 0) ultiPaw.blendShapeFactorLinks[index] = entry;
        else ultiPaw.blendShapeFactorLinks.Add(entry);
        EditorUtility.SetDirty(ultiPaw);

        return new ConfigResult
        {
            success = true,
            message = enabled
                ? "Link saved. It will be applied to VRCFury built controllers during avatar preprocess."
                : "Link saved but disabled. It will not be applied unless re-enabled."
        };
    }

    public ApplyResult ApplyConfiguredFactorLinks(GameObject avatarRoot)
    {
        if (avatarRoot == null) return FailApply("Avatar root is null.");

        var ultiPaw = FindUltiPaw(avatarRoot);
        if (ultiPaw == null || ultiPaw.blendShapeFactorLinks == null || ultiPaw.blendShapeFactorLinks.Count == 0)
        {
            return FailApply("No BlendShape factor link configuration was found.");
        }

        var planned = new List<PlannedLink>();
        foreach (var rawLink in ultiPaw.blendShapeFactorLinks)
        {
            if (rawLink == null || !rawLink.enabled) continue;
            if (!TryResolveManualLink(avatarRoot, rawLink, out var resolved, out _)) continue;
            planned.Add(resolved);
        }

        if (planned.Count == 0) return FailApply("No valid configured links could be applied.");

        return ApplyPlannedLinks(avatarRoot, planned, "manual");
    }

    public ApplyResult ApplyActiveVersionFactorLinks(GameObject avatarRoot)
    {
        if (!TryResolveVersionState(avatarRoot, out var state))
        {
            return FailApply("No applied version state found for BlendShape links.");
        }

        var planned = BuildVersionPlannedLinks(avatarRoot, state.version, state.useCustomSliderSelection, state.customSliderSelectionNames);
        if (planned.Count == 0) return FailApply("No version BlendShape links are active for this avatar.");

        return ApplyPlannedLinks(avatarRoot, planned, "version");
    }

    public List<VersionLinkDebugInfo> GetActiveVersionLinkDebugInfo(GameObject avatarRoot)
    {
        if (!TryResolveVersionState(avatarRoot, out var state)) return new List<VersionLinkDebugInfo>();

        var planned = BuildVersionPlannedLinks(avatarRoot, state.version, state.useCustomSliderSelection, state.customSliderSelectionNames);

        return planned.Select(x => new VersionLinkDebugInfo
        {
            targetRendererPath = x.targetRendererPath,
            sourceBlendshape = x.sourceBlendshape,
            destinationBlendshape = x.destinationBlendshape,
            factorParameterName = x.factorParameterName,
            usesConstantFactor = x.setFactorDefaultValue,
            constantFactor = x.factorDefaultValue,
            driverBlendshape = x.driverBlendshape
        }).ToList();
    }
    private static bool TryResolveVersionState(GameObject avatarRoot, out VersionState state)
    {
        state = default;
        var ultiPaw = FindUltiPaw(avatarRoot);

        if (ultiPaw == null) return false;

        UltiPawVersion versionSource = ultiPaw.appliedUltiPawVersion;
        if ((versionSource == null || versionSource.customBlendshapes == null || versionSource.customBlendshapes.Length == 0) &&
            ultiPaw.appliedVersionBlendshapeLinksCache != null &&
            ultiPaw.appliedVersionBlendshapeLinksCache.Count > 0)
        {
            versionSource = BuildVersionFromCache(ultiPaw.appliedVersionBlendshapeLinksCache);
        }

        if (versionSource == null || versionSource.customBlendshapes == null || versionSource.customBlendshapes.Length == 0) return false;

        state = new VersionState
        {
            version = versionSource,
            useCustomSliderSelection = ultiPaw.useCustomSliderSelection,
            customSliderSelectionNames = ultiPaw.customSliderSelectionNames != null
                ? new List<string>(ultiPaw.customSliderSelectionNames)
                : new List<string>()
        };
        return true;
    }

    private static UltiPawVersion BuildVersionFromCache(List<CreatorBlendshapeEntry> cachedEntries)
    {
        if (cachedEntries == null || cachedEntries.Count == 0) return null;

        var version = new UltiPawVersion
        {
            version = "cached",
            defaultAviVersion = "cached",
            customBlendshapes = cachedEntries
                .Where(x => x != null)
                .Select(x => new CustomBlendshapeEntry
                {
                    name = x.name,
                    defaultValue = x.defaultValue,
                    isSlider = x.isSlider,
                    isSliderDefault = x.isSliderDefault,
                    correctiveBlendshapes = x.correctiveBlendshapes != null
                        ? x.correctiveBlendshapes
                            .Where(c => c != null)
                            .Select(c => new CorrectiveBlendshapeEntry
                            {
                                blendshapeToFix = c.blendshapeToFix,
                                fixingBlendshape = c.fixingBlendshape
                            })
                            .ToArray()
                        : null
                })
                .ToArray()
        };

        return version;
    }

    private static List<PlannedLink> BuildVersionPlannedLinks(GameObject avatarRoot, UltiPawVersion version, bool useCustomSliderSelection, List<string> customSliderSelectionNames)
    {
        var output = new List<PlannedLink>();
        if (avatarRoot == null || version?.customBlendshapes == null || version.customBlendshapes.Length == 0) return output;

        var selectedSliders = BuildSelectedSliderSet(version.customBlendshapes, useCustomSliderSelection, customSliderSelectionNames);
        var renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers == null || renderers.Length == 0) return output;

        var dedupe = new HashSet<string>(StringComparer.Ordinal);
        foreach (var driver in version.customBlendshapes)
        {
            if (driver == null || string.IsNullOrWhiteSpace(driver.name)) continue;
            if (driver.correctiveBlendshapes == null || driver.correctiveBlendshapes.Length == 0) continue;

            bool isSliderFactor = driver.isSlider && selectedSliders.Contains(driver.name);

            foreach (var corrective in driver.correctiveBlendshapes)
            {
                if (corrective == null) continue;
                if (string.IsNullOrWhiteSpace(corrective.blendshapeToFix)) continue;
                if (string.IsNullOrWhiteSpace(corrective.fixingBlendshape)) continue;

                foreach (var renderer in renderers)
                {
                    if (renderer == null || renderer.sharedMesh == null) continue;
                    var mesh = renderer.sharedMesh;

                    if (mesh.GetBlendShapeIndex(corrective.blendshapeToFix) < 0) continue;
                    if (mesh.GetBlendShapeIndex(corrective.fixingBlendshape) < 0) continue;

                    string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform);
                    if (string.IsNullOrWhiteSpace(rendererPath)) continue;

                    float constantFactor = Mathf.Clamp01(ReadBlendshapeWeight01(renderer, driver.name));
                    string factorParam = isSliderFactor
                        ? VRCFuryService.GetSliderGlobalParamName(driver.name)
                        : BuildConstantFactorParamName(driver.name, rendererPath, corrective.blendshapeToFix, corrective.fixingBlendshape);

                    string key = rendererPath + "|" + corrective.blendshapeToFix + "|" + corrective.fixingBlendshape + "|" + factorParam;
                    if (!dedupe.Add(key)) continue;

                    output.Add(new PlannedLink
                    {
                        targetRendererPath = rendererPath,
                        sourceBlendshape = corrective.blendshapeToFix,
                        destinationBlendshape = corrective.fixingBlendshape,
                        sourceProperty = "blendShape." + corrective.blendshapeToFix,
                        destinationProperty = "blendShape." + corrective.fixingBlendshape,
                        factorParameterName = factorParam,
                        setFactorDefaultValue = !isSliderFactor,
                        factorDefaultValue = constantFactor,
                        driverBlendshape = driver.name
                    });
                }
            }
        }

        return output;
    }

    private static HashSet<string> BuildSelectedSliderSet(CustomBlendshapeEntry[] entries, bool useCustomSliderSelection, List<string> customSliderSelectionNames)
    {
        if (entries == null) return new HashSet<string>(StringComparer.Ordinal);

        if (useCustomSliderSelection)
        {
            return new HashSet<string>(customSliderSelectionNames ?? new List<string>(), StringComparer.Ordinal);
        }

        var defaults = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry != null && entry.isSlider && entry.isSliderDefault && !string.IsNullOrWhiteSpace(entry.name))
            {
                defaults.Add(entry.name);
            }
        }

        return defaults;
    }

    private static float ReadBlendshapeWeight01(SkinnedMeshRenderer renderer, string blendshapeName)
    {
        if (renderer == null || renderer.sharedMesh == null || string.IsNullOrWhiteSpace(blendshapeName)) return 0f;
        int index = renderer.sharedMesh.GetBlendShapeIndex(blendshapeName);
        if (index < 0) return 0f;
        return renderer.GetBlendShapeWeight(index) / 100f;
    }

    private static string BuildConstantFactorParamName(string driverBlendshape, string rendererPath, string source, string destination)
    {
        string baseName = SanitizeForParam(driverBlendshape) + "_" + SanitizeForParam(rendererPath) + "_" + SanitizeForParam(source) + "_" + SanitizeForParam(destination);
        if (baseName.Length > 72) baseName = baseName.Substring(0, 72);
        return ConstParamPrefix + baseName;
    }

    private static string SanitizeForParam(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "X";
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }
        return sb.ToString();
    }
    private static bool TryResolveManualLink(GameObject avatarRoot, BlendShapeFactorLinkEntry link, out PlannedLink resolved, out string error)
    {
        resolved = default;
        error = null;

        if (link == null) { error = "Link is null."; return false; }
        if (string.IsNullOrWhiteSpace(link.targetRendererPath)) { error = "Target mesh path is empty."; return false; }
        if (string.IsNullOrWhiteSpace(link.sourceBlendshape)) { error = "Source blendshape is empty."; return false; }
        if (string.IsNullOrWhiteSpace(link.destinationBlendshape)) { error = "Destination blendshape is empty."; return false; }
        if (string.IsNullOrWhiteSpace(link.factorParameterName)) { error = "Factor parameter name is empty."; return false; }

        var target = FindRendererByPath(avatarRoot, link.targetRendererPath);
        if (target == null || target.sharedMesh == null)
        {
            error = $"Target mesh path '{link.targetRendererPath}' was not found on avatar clone.";
            return false;
        }
        if (target.sharedMesh.GetBlendShapeIndex(link.sourceBlendshape) < 0)
        {
            error = $"Source blendshape '{link.sourceBlendshape}' was not found on target mesh '{target.name}'.";
            return false;
        }
        if (target.sharedMesh.GetBlendShapeIndex(link.destinationBlendshape) < 0)
        {
            error = $"Destination blendshape '{link.destinationBlendshape}' was not found on target mesh '{target.name}'.";
            return false;
        }

        resolved = new PlannedLink
        {
            targetRendererPath = link.targetRendererPath,
            sourceBlendshape = link.sourceBlendshape,
            destinationBlendshape = link.destinationBlendshape,
            sourceProperty = "blendShape." + link.sourceBlendshape,
            destinationProperty = "blendShape." + link.destinationBlendshape,
            factorParameterName = link.factorParameterName,
            setFactorDefaultValue = false,
            factorDefaultValue = 0f,
            driverBlendshape = string.Empty
        };
        return true;
    }

    private static bool TryBuildAndValidateEntry(
        GameObject avatarRoot,
        SkinnedMeshRenderer targetRenderer,
        string sourceBlendshape,
        string destinationBlendshape,
        string factorParameterName,
        bool enabled,
        out BlendShapeFactorLinkEntry entry,
        out string error)
    {
        entry = null;
        error = null;

        if (avatarRoot == null) { error = "Avatar root is null."; return false; }
        if (targetRenderer == null) { error = "Target mesh is missing."; return false; }
        if (targetRenderer.sharedMesh == null) { error = "Target mesh renderer has no shared mesh."; return false; }
        if (string.IsNullOrWhiteSpace(sourceBlendshape)) { error = "Source blendshape is empty."; return false; }
        if (string.IsNullOrWhiteSpace(destinationBlendshape)) { error = "Destination blendshape is empty."; return false; }
        if (string.IsNullOrWhiteSpace(factorParameterName)) { error = "Factor parameter name is empty."; return false; }
        if (!targetRenderer.transform.IsChildOf(avatarRoot.transform)) { error = "Target mesh is not a child of avatar root."; return false; }
        if (targetRenderer.sharedMesh.GetBlendShapeIndex(sourceBlendshape) < 0)
        {
            error = $"Source blendshape '{sourceBlendshape}' was not found on target mesh.";
            return false;
        }
        if (targetRenderer.sharedMesh.GetBlendShapeIndex(destinationBlendshape) < 0)
        {
            error = $"Destination blendshape '{destinationBlendshape}' was not found on target mesh.";
            return false;
        }

        string targetPath = AnimationUtility.CalculateTransformPath(targetRenderer.transform, avatarRoot.transform);
        if (string.IsNullOrWhiteSpace(targetPath)) { error = "Failed to compute target mesh transform path."; return false; }

        entry = new BlendShapeFactorLinkEntry
        {
            enabled = enabled,
            targetRendererPath = targetPath,
            sourceBlendshape = sourceBlendshape,
            destinationBlendshape = destinationBlendshape,
            factorParameterName = factorParameterName
        };
        return true;
    }

    private static bool IsSameManualLink(BlendShapeFactorLinkEntry a, BlendShapeFactorLinkEntry b)
    {
        if (a == null || b == null) return false;
        return string.Equals(a.targetRendererPath, b.targetRendererPath, StringComparison.Ordinal)
            && string.Equals(a.sourceBlendshape, b.sourceBlendshape, StringComparison.Ordinal)
            && string.Equals(a.destinationBlendshape, b.destinationBlendshape, StringComparison.Ordinal);
    }

    private static UltiPaw FindUltiPaw(GameObject avatarRoot)
    {
        if (avatarRoot == null) return null;

        var onRoot = avatarRoot.GetComponent<UltiPaw>();
        if (onRoot != null) return onRoot;

        var inChildren = avatarRoot.GetComponentInChildren<UltiPaw>(true);
        if (inChildren != null) return inChildren;

        // Preprocess often runs on a clone where editor-only components may be absent.
        string rootName = avatarRoot.name;
        if (!string.IsNullOrEmpty(rootName) && rootName.EndsWith("(Clone)", StringComparison.Ordinal))
        {
            rootName = rootName.Substring(0, rootName.Length - "(Clone)".Length);
        }

        var all = Resources.FindObjectsOfTypeAll<UltiPaw>();
        return all.FirstOrDefault(x =>
            x != null &&
            x.gameObject != null &&
            x.gameObject.scene.IsValid() &&
            x.transform.root != null &&
            string.Equals(x.transform.root.name, rootName, StringComparison.Ordinal));
    }

    private static SkinnedMeshRenderer FindRendererByPath(GameObject avatarRoot, string targetPath)
    {
        if (avatarRoot == null || string.IsNullOrWhiteSpace(targetPath)) return null;
        var transform = avatarRoot.transform.Find(targetPath);
        if (transform == null) return null;
        return transform.GetComponent<SkinnedMeshRenderer>();
    }

    private static ConfigResult FailConfig(string message) => new ConfigResult { success = false, message = message };

    private static ApplyResult FailApply(string message)
    {
        return new ApplyResult
        {
            success = false,
            linksProcessed = 0,
            controllersProcessed = 0,
            clipsWrapped = 0,
            statesRewritten = 0,
            message = message
        };
    }

    private static ApplyResult ApplyPlannedLinks(GameObject avatarRoot, List<PlannedLink> plannedLinks, string sourceLabel)
    {
        if (avatarRoot == null) return FailApply("Avatar root is null.");
        if (plannedLinks == null || plannedLinks.Count == 0) return FailApply("No BlendShape links to apply.");

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
                    if (RewriteStateMachine(controller, layer.stateMachine, planned.targetRendererPath, planned.sourceProperty, planned.destinationProperty, planned.factorParameterName, clipCache, visitedStateMachines, visitedTrees, ref clipsWrapped, ref statesRewritten))
                    {
                        controllerChanged = true;
                        thisLinkChangedAnyController = true;
                    }
                }

                if (!controllerChanged) continue;
                changedControllers.Add(controller);
                anyControllerChanged = true;
                EditorUtility.SetDirty(controller);
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
                message = $"Applied {sourceLabel} BlendShape links: {linksProcessed} link(s), {changedControllers.Count} controller(s), {clipsWrapped} wrapped clip(s), {statesRewritten} rewritten state/tree motion reference(s)."
            };
        }

        return FailApply("No matching source blendshape curves were found in VRCFury temporary controllers.");
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
        string targetPath,
        string sourceProperty,
        string destinationProperty,
        string factorParameterName,
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

            Motion rewritten = RewriteMotion(controller, state.motion, targetPath, sourceProperty, destinationProperty, factorParameterName, clipCache, visitedTrees, ref clipsWrapped);

            if (rewritten == null || rewritten == state.motion) continue;
            state.motion = rewritten;
            EditorUtility.SetDirty(state);
            statesRewritten++;
            changed = true;
        }

        foreach (var childMachine in stateMachine.stateMachines)
        {
            if (childMachine.stateMachine == null) continue;
            if (RewriteStateMachine(controller, childMachine.stateMachine, targetPath, sourceProperty, destinationProperty, factorParameterName, clipCache, visitedStateMachines, visitedTrees, ref clipsWrapped, ref statesRewritten))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static Motion RewriteMotion(
        AnimatorController controller,
        Motion motion,
        string targetPath,
        string sourceProperty,
        string destinationProperty,
        string factorParameterName,
        IDictionary<AnimationClip, Motion> clipCache,
        ISet<BlendTree> visitedTrees,
        ref int clipsWrapped)
    {
        if (motion == null) return null;

        if (motion is BlendTree tree)
        {
            if (visitedTrees.Contains(tree)) return tree;
            visitedTrees.Add(tree);

            if (IsWrapperTree(tree, factorParameterName)) return tree;

            bool changed = false;
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var childMotion = children[i].motion;
                var rewritten = RewriteMotion(controller, childMotion, targetPath, sourceProperty, destinationProperty, factorParameterName, clipCache, visitedTrees, ref clipsWrapped);
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

        if (!TryGetSourceBinding(clip, targetPath, sourceProperty, out var sourceBinding, out var sourceCurve))
        {
            clipCache[clip] = clip;
            return clip;
        }

        var variantClip = CreateVariantClip(controller, clip, sourceBinding, sourceCurve, destinationProperty);
        var wrapperTree = CreateWrapperTree(controller, clip, variantClip, factorParameterName);
        clipCache[clip] = wrapperTree;
        clipsWrapped++;
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

    private static bool TryGetSourceBinding(AnimationClip clip, string targetPath, string sourceProperty, out EditorCurveBinding sourceBinding, out AnimationCurve sourceCurve)
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

    private static AnimationClip CreateVariantClip(AnimatorController controller, AnimationClip sourceClip, EditorCurveBinding sourceBinding, AnimationCurve sourceCurve, string destinationProperty)
    {
        var variantClip = UnityEngine.Object.Instantiate(sourceClip);
        variantClip.name = BuildName(VariantPrefix, sourceClip.name);
        variantClip.hideFlags = HideFlags.HideInHierarchy;

        var destinationBinding = sourceBinding;
        destinationBinding.propertyName = destinationProperty;

        AnimationUtility.SetEditorCurve(variantClip, destinationBinding, sourceCurve);

        AttachAsSubAsset(controller, variantClip);
        EditorUtility.SetDirty(variantClip);
        return variantClip;
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
