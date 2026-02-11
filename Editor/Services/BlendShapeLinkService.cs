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
        public CorrectiveActivationType toFixType;
        public string toFix;
        public CorrectiveActivationType fixedByType;
        public string fixedBy;
        public string factorParameterName;
        public bool usesConstantFactor;
        public float constantFactor;
        public string driverBlendshape;
    }

    private struct PlannedLink
    {
        public string targetRendererPath;
        public CorrectiveActivationType toFixType;
        public string toFixName;
        public CorrectiveActivationType fixedByType;
        public string fixedByName;
        public string sourcePath;
        public string sourceProperty;
        public string destinationPath;
        public string destinationProperty;
        public AnimationClip fixedByAnimationClip;
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
        CorrectiveActivationType toFixType,
        string toFix,
        CorrectiveActivationType fixedByType,
        string fixedBy,
        string factorParameterName,
        bool enabled = true)
    {
        if (!TryBuildAndValidateEntry(avatarRoot, targetRenderer, toFixType, toFix, fixedByType, fixedBy, factorParameterName, enabled, out var entry, out var error))
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
            toFixType = x.toFixType,
            toFix = x.toFixName,
            fixedByType = x.fixedByType,
            fixedBy = x.fixedByName,
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
                                toFixType = c.toFixType,
                                toFix = c.toFix,
                                fixedByType = c.fixedByType,
                                fixedBy = c.fixedBy
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
        var renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true) ?? Array.Empty<SkinnedMeshRenderer>();
        var animationClipLookup = BuildAnimationClipLookup();
        var dedupe = new HashSet<string>(StringComparer.Ordinal);
        foreach (var driver in version.customBlendshapes)
        {
            if (driver == null || string.IsNullOrWhiteSpace(driver.name)) continue;
            if (driver.correctiveBlendshapes == null || driver.correctiveBlendshapes.Length == 0) continue;

            bool isSliderFactor = driver.isSlider && selectedSliders.Contains(driver.name);
            float globalConstantFactor = Mathf.Clamp01(ReadBlendshapeWeight01(renderers, driver.name));

            foreach (var corrective in driver.correctiveBlendshapes)
            {
                if (corrective == null) continue;
                if (string.IsNullOrWhiteSpace(corrective.toFix)) continue;
                if (string.IsNullOrWhiteSpace(corrective.fixedBy)) continue;

                AnimationClip fixedByClip = null;
                if (corrective.fixedByType == CorrectiveActivationType.Animation &&
                    !TryResolveAnimationClipByName(corrective.fixedBy, animationClipLookup, out fixedByClip))
                {
                    continue;
                }

                bool needsRenderer = corrective.toFixType == CorrectiveActivationType.Blendshape || corrective.fixedByType == CorrectiveActivationType.Blendshape;
                if (!needsRenderer)
                {
                    string factorParamNoRenderer = isSliderFactor
                        ? VRCFuryService.GetSliderGlobalParamName(driver.name)
                        : BuildConstantFactorParamName(driver.name, "anim", corrective.toFixType.ToString(), corrective.toFix, corrective.fixedByType.ToString(), corrective.fixedBy);

                    string keyNoRenderer = string.Join("|", "", corrective.toFixType, corrective.toFix, corrective.fixedByType, corrective.fixedBy, factorParamNoRenderer);
                    if (!dedupe.Add(keyNoRenderer)) continue;

                    output.Add(new PlannedLink
                    {
                        targetRendererPath = string.Empty,
                        toFixType = corrective.toFixType,
                        toFixName = corrective.toFix,
                        fixedByType = corrective.fixedByType,
                        fixedByName = corrective.fixedBy,
                        sourcePath = string.Empty,
                        sourceProperty = string.Empty,
                        destinationPath = string.Empty,
                        destinationProperty = string.Empty,
                        fixedByAnimationClip = fixedByClip,
                        factorParameterName = factorParamNoRenderer,
                        setFactorDefaultValue = !isSliderFactor,
                        factorDefaultValue = globalConstantFactor,
                        driverBlendshape = driver.name
                    });
                    continue;
                }

                foreach (var renderer in renderers)
                {
                    if (renderer == null || renderer.sharedMesh == null) continue;
                    var mesh = renderer.sharedMesh;
                    if (corrective.toFixType == CorrectiveActivationType.Blendshape && mesh.GetBlendShapeIndex(corrective.toFix) < 0) continue;
                    if (corrective.fixedByType == CorrectiveActivationType.Blendshape && mesh.GetBlendShapeIndex(corrective.fixedBy) < 0) continue;

                    string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform);
                    if (string.IsNullOrWhiteSpace(rendererPath)) continue;

                    float constantFactor = Mathf.Clamp01(ReadBlendshapeWeight01(renderer, driver.name));
                    string factorParam = isSliderFactor
                        ? VRCFuryService.GetSliderGlobalParamName(driver.name)
                        : BuildConstantFactorParamName(driver.name, rendererPath, corrective.toFixType.ToString(), corrective.toFix, corrective.fixedByType.ToString(), corrective.fixedBy);

                    string key = string.Join("|", rendererPath, corrective.toFixType, corrective.toFix, corrective.fixedByType, corrective.fixedBy, factorParam);
                    if (!dedupe.Add(key)) continue;

                    output.Add(new PlannedLink
                    {
                        targetRendererPath = rendererPath,
                        toFixType = corrective.toFixType,
                        toFixName = corrective.toFix,
                        fixedByType = corrective.fixedByType,
                        fixedByName = corrective.fixedBy,
                        sourcePath = corrective.toFixType == CorrectiveActivationType.Blendshape ? rendererPath : string.Empty,
                        sourceProperty = corrective.toFixType == CorrectiveActivationType.Blendshape ? "blendShape." + corrective.toFix : string.Empty,
                        destinationPath = corrective.fixedByType == CorrectiveActivationType.Blendshape ? rendererPath : string.Empty,
                        destinationProperty = corrective.fixedByType == CorrectiveActivationType.Blendshape ? "blendShape." + corrective.fixedBy : string.Empty,
                        fixedByAnimationClip = fixedByClip,
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

    private static Dictionary<string, List<AnimationClip>> BuildAnimationClipLookup()
    {
        var lookup = new Dictionary<string, List<AnimationClip>>(StringComparer.Ordinal);
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null || string.IsNullOrWhiteSpace(clip.name)) continue;

            if (!lookup.TryGetValue(clip.name, out var list))
            {
                list = new List<AnimationClip>();
                lookup[clip.name] = list;
            }

            list.Add(clip);
        }

        foreach (var kvp in lookup)
        {
            kvp.Value.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
        }

        return lookup;
    }

    private static bool TryResolveAnimationClipByName(string clipName, Dictionary<string, List<AnimationClip>> lookup, out AnimationClip clip)
    {
        clip = null;
        if (string.IsNullOrWhiteSpace(clipName) || lookup == null) return false;
        if (!lookup.TryGetValue(clipName, out var candidates) || candidates == null || candidates.Count == 0) return false;

        clip = candidates[0];
        return clip != null;
    }

    private static float ReadBlendshapeWeight01(SkinnedMeshRenderer[] renderers, string blendshapeName)
    {
        if (renderers == null || string.IsNullOrWhiteSpace(blendshapeName)) return 0f;

        foreach (var renderer in renderers)
        {
            float value = ReadBlendshapeWeight01(renderer, blendshapeName);
            if (value > 0f) return value;
        }

        return 0f;
    }

    private static float ReadBlendshapeWeight01(SkinnedMeshRenderer renderer, string blendshapeName)
    {
        if (renderer == null || renderer.sharedMesh == null || string.IsNullOrWhiteSpace(blendshapeName)) return 0f;
        int index = renderer.sharedMesh.GetBlendShapeIndex(blendshapeName);
        if (index < 0) return 0f;
        return renderer.GetBlendShapeWeight(index) / 100f;
    }

    private static string BuildConstantFactorParamName(string driverBlendshape, params string[] contextParts)
    {
        var allParts = new List<string> { driverBlendshape };
        if (contextParts != null) allParts.AddRange(contextParts.Where(p => !string.IsNullOrWhiteSpace(p)));

        string baseName = string.Join("_", allParts.Select(SanitizeForParam));
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
        if (string.IsNullOrWhiteSpace(link.toFix)) { error = "To-fix target is empty."; return false; }
        if (string.IsNullOrWhiteSpace(link.fixedBy)) { error = "Fixing target is empty."; return false; }
        if (string.IsNullOrWhiteSpace(link.factorParameterName)) { error = "Factor parameter name is empty."; return false; }

        SkinnedMeshRenderer target = null;
        if (link.toFixType == CorrectiveActivationType.Blendshape || link.fixedByType == CorrectiveActivationType.Blendshape)
        {
            if (string.IsNullOrWhiteSpace(link.targetRendererPath))
            {
                error = "Target mesh path is required when using blendshape corrective targets.";
                return false;
            }

            target = FindRendererByPath(avatarRoot, link.targetRendererPath);
            if (target == null || target.sharedMesh == null)
            {
                error = $"Target mesh path '{link.targetRendererPath}' was not found on avatar clone.";
                return false;
            }

            if (link.toFixType == CorrectiveActivationType.Blendshape && target.sharedMesh.GetBlendShapeIndex(link.toFix) < 0)
            {
                error = $"To-fix blendshape '{link.toFix}' was not found on target mesh '{target.name}'.";
                return false;
            }

            if (link.fixedByType == CorrectiveActivationType.Blendshape && target.sharedMesh.GetBlendShapeIndex(link.fixedBy) < 0)
            {
                error = $"Fixing blendshape '{link.fixedBy}' was not found on target mesh '{target.name}'.";
                return false;
            }
        }

        AnimationClip fixedByClip = null;
        if (link.fixedByType == CorrectiveActivationType.Animation)
        {
            fixedByClip = FindAnimationClipsByName(link.fixedBy).FirstOrDefault();
            if (fixedByClip == null)
            {
                error = $"Fixing animation '{link.fixedBy}' was not found in project assets.";
                return false;
            }
        }

        resolved = new PlannedLink
        {
            targetRendererPath = link.targetRendererPath ?? string.Empty,
            toFixType = link.toFixType,
            toFixName = link.toFix,
            fixedByType = link.fixedByType,
            fixedByName = link.fixedBy,
            sourcePath = link.toFixType == CorrectiveActivationType.Blendshape ? link.targetRendererPath : string.Empty,
            sourceProperty = link.toFixType == CorrectiveActivationType.Blendshape ? "blendShape." + link.toFix : string.Empty,
            destinationPath = link.fixedByType == CorrectiveActivationType.Blendshape ? link.targetRendererPath : string.Empty,
            destinationProperty = link.fixedByType == CorrectiveActivationType.Blendshape ? "blendShape." + link.fixedBy : string.Empty,
            fixedByAnimationClip = fixedByClip,
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
        CorrectiveActivationType toFixType,
        string toFix,
        CorrectiveActivationType fixedByType,
        string fixedBy,
        string factorParameterName,
        bool enabled,
        out BlendShapeFactorLinkEntry entry,
        out string error)
    {
        entry = null;
        error = null;

        if (avatarRoot == null) { error = "Avatar root is null."; return false; }
        if (string.IsNullOrWhiteSpace(toFix)) { error = "To-fix target is empty."; return false; }
        if (string.IsNullOrWhiteSpace(fixedBy)) { error = "Fixing target is empty."; return false; }
        if (string.IsNullOrWhiteSpace(factorParameterName)) { error = "Factor parameter name is empty."; return false; }

        string targetPath = string.Empty;
        if (toFixType == CorrectiveActivationType.Blendshape || fixedByType == CorrectiveActivationType.Blendshape)
        {
            if (targetRenderer == null) { error = "Target mesh is required when using blendshape corrective targets."; return false; }
            if (targetRenderer.sharedMesh == null) { error = "Target mesh renderer has no shared mesh."; return false; }
            if (!targetRenderer.transform.IsChildOf(avatarRoot.transform)) { error = "Target mesh is not a child of avatar root."; return false; }

            if (toFixType == CorrectiveActivationType.Blendshape && targetRenderer.sharedMesh.GetBlendShapeIndex(toFix) < 0)
            {
                error = $"To-fix blendshape '{toFix}' was not found on target mesh.";
                return false;
            }

            if (fixedByType == CorrectiveActivationType.Blendshape && targetRenderer.sharedMesh.GetBlendShapeIndex(fixedBy) < 0)
            {
                error = $"Fixing blendshape '{fixedBy}' was not found on target mesh.";
                return false;
            }

            targetPath = AnimationUtility.CalculateTransformPath(targetRenderer.transform, avatarRoot.transform);
            if (string.IsNullOrWhiteSpace(targetPath)) { error = "Failed to compute target mesh transform path."; return false; }
        }

        if (fixedByType == CorrectiveActivationType.Animation && FindAnimationClipsByName(fixedBy).Count == 0)
        {
            error = $"Fixing animation '{fixedBy}' was not found in project assets.";
            return false;
        }

        entry = new BlendShapeFactorLinkEntry
        {
            enabled = enabled,
            targetRendererPath = targetPath,
            toFixType = toFixType,
            toFix = toFix,
            fixedByType = fixedByType,
            fixedBy = fixedBy,
            factorParameterName = factorParameterName
        };

        return true;
    }

    private static List<AnimationClip> FindAnimationClipsByName(string clipName)
    {
        var clips = new List<AnimationClip>();
        if (string.IsNullOrWhiteSpace(clipName)) return clips;

        string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) continue;
            if (string.Equals(clip.name, clipName, StringComparison.Ordinal))
            {
                clips.Add(clip);
            }
        }

        clips.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
        return clips;
    }

    private static bool IsSameManualLink(BlendShapeFactorLinkEntry a, BlendShapeFactorLinkEntry b)
    {
        if (a == null || b == null) return false;
        return string.Equals(a.targetRendererPath, b.targetRendererPath, StringComparison.Ordinal)
            && a.toFixType == b.toFixType
            && a.fixedByType == b.fixedByType
            && string.Equals(a.toFix, b.toFix, StringComparison.Ordinal)
            && string.Equals(a.fixedBy, b.fixedBy, StringComparison.Ordinal);
    }

    private static UltiPaw FindUltiPaw(GameObject avatarRoot)
    {
        if (avatarRoot == null) return null;

        var onRoot = avatarRoot.GetComponent<UltiPaw>();
        if (onRoot != null) return onRoot;

        var inChildren = avatarRoot.GetComponentInChildren<UltiPaw>(true);
        if (inChildren != null) return inChildren;

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

            if (IsWrapperTree(tree, planned.factorParameterName)) return tree;

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
        if (planned.toFixType == CorrectiveActivationType.Animation && !string.Equals(sourceClip.name, planned.toFixName, StringComparison.Ordinal)) return null;

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
    private static bool IsWrapperTree(BlendTree tree, string factorParameterName)
    {
        if (tree == null) return false;
        if (!tree.name.StartsWith(WrapperPrefix, StringComparison.Ordinal)) return false;
        if (tree.blendType != BlendTreeType.Simple1D) return false;
        if (!string.Equals(tree.blendParameter, factorParameterName, StringComparison.Ordinal)) return false;
        var children = tree.children;
        return children != null && children.Length == 2;
    }

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
