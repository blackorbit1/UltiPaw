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
        public AnimationClip toFixAnimationClip;
        public List<AnimationBindingSignature> toFixAnimationSignature;
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

    private struct AnimationBindingSignature
    {
        public string path;
        public Type type;
        public string propertyName;
        public float[] sampleTimes;
        public float[] sampleValues;
    }

    private struct CurveCandidate
    {
        public string path;
        public Type type;
        public string propertyName;
        public AnimationCurve curve;
    }

    private struct VersionState
    {
        public UltiPawVersion version;
        public bool useCustomSliderSelection;
        public List<string> customSliderSelectionNames;
    }

    public void ApplyVersionLinks(GameObject avatarRoot, UltiPawVersion version, bool useCustomSliderSelection,
        List<string> customSliderSelectionNames)
    {
        if (avatarRoot == null || version == null) return;
        var planned =
            BuildVersionPlannedLinks(avatarRoot, version, useCustomSliderSelection, customSliderSelectionNames);
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
        if (!TryBuildAndValidateEntry(avatarRoot, targetRenderer, toFixType, toFix, fixedByType, fixedBy,
                factorParameterName, enabled, out var entry, out var error))
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

        var planned = BuildVersionPlannedLinks(avatarRoot, state.version, state.useCustomSliderSelection,
            state.customSliderSelectionNames);
        if (planned.Count == 0) return FailApply("No version BlendShape links are active for this avatar.");

        return ApplyPlannedLinks(avatarRoot, planned, "version");
    }

    public List<VersionLinkDebugInfo> GetActiveVersionLinkDebugInfo(GameObject avatarRoot)
    {
        if (!TryResolveVersionState(avatarRoot, out var state)) return new List<VersionLinkDebugInfo>();

        var planned = BuildVersionPlannedLinks(avatarRoot, state.version, state.useCustomSliderSelection,
            state.customSliderSelectionNames);

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
        if ((versionSource == null || versionSource.customBlendshapes == null ||
             versionSource.customBlendshapes.Length == 0) &&
            ultiPaw.appliedVersionBlendshapeLinksCache != null &&
            ultiPaw.appliedVersionBlendshapeLinksCache.Count > 0)
        {
            versionSource = BuildVersionFromCache(ultiPaw.appliedVersionBlendshapeLinksCache);
        }

        if (versionSource == null || versionSource.customBlendshapes == null ||
            versionSource.customBlendshapes.Length == 0) return false;

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

}
#endif
