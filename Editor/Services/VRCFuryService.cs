#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static UnityEditor.EditorApplication;

public class VRCFuryService
{
    private static VRCFuryService _instance;
    public static VRCFuryService Instance => _instance ??= new VRCFuryService();

    private const string SLIDERS_GAMEOBJECT_NAME = "ultipaw sliders";
    private const string SLIDER_PARAM_PREFIX = "UP_Slider_";
    private const string CONST_PARAM_PREFIX = "UP_CorrectiveConst_";
    private const int CorrectiveRetryCount = 25;
    private const double CorrectiveRetryInterval = 0.2;

    // Type Cache
    private Type _vrcFuryType;
    private Type _setIconType;
    private Type _guidTextureType;
    private Type _toggleType;
    private Type _unlimitedType;
    private Type _applyDuringUploadType;
    private Type _stateType;
    private Type _blendShapeActionType;

    // Play mode corrective application state
    private GameObject _correctiveAvatarRoot;
    private UltiPawVersion _correctiveVersion;
    private int _correctiveRemainingRetries;
    private double _correctiveNextRetryTime;

    public struct ParameterUsage
    {
        public int currentSyncedBits;
        public int totalUsedAfterBuild;
        public int usedByAvatar;
        public int usedBySliders;
        public bool compressionEnabled;
        public bool compressionIsExternal;
        public string compressionPath;
    }

    public void ApplySliders(GameObject avatarRoot, string menuPath, List<CustomBlendshapeEntry> selectedSliders)
    {
        if (avatarRoot == null)
        {
            Debug.LogError("[UltiPaw] Avatar root is null. Cannot apply sliders.");
            return;
        }

        Transform slidersTransform = avatarRoot.transform.Find(SLIDERS_GAMEOBJECT_NAME);
        GameObject slidersObj;
        if (slidersTransform == null)
        {
            slidersObj = new GameObject(SLIDERS_GAMEOBJECT_NAME);
            slidersObj.transform.SetParent(avatarRoot.transform, false);
            Undo.RegisterCreatedObjectUndo(slidersObj, "Create UltiPaw Sliders GameObject");
        }
        else
        {
            slidersObj = slidersTransform.gameObject;
        }

        if (_vrcFuryType == null) _vrcFuryType = FindType("VF.Model.VRCFury");
        if (_toggleType == null) _toggleType = FindType("VF.Model.Feature.Toggle");
        if (_applyDuringUploadType == null) _applyDuringUploadType = FindType("VF.Model.Feature.ApplyDuringUpload");
        if (_stateType == null) _stateType = FindType("VF.Model.State");
        if (_blendShapeActionType == null) _blendShapeActionType = FindType("VF.Model.StateAction.BlendShapeAction");

        if (_vrcFuryType == null)
        {
            Debug.LogError("[UltiPaw] VRCFury not found. Cannot sync sliders.");
            return;
        }

        if (_toggleType == null || _applyDuringUploadType == null || _stateType == null || _blendShapeActionType == null)
        {
            Debug.LogError("[UltiPaw] Could not resolve required VRCFury types for sliders.");
            return;
        }

        var existingVrcfComponents = slidersObj.GetComponents(_vrcFuryType).ToList();
        var componentsToRemove = new List<Component>();

        foreach (var comp in existingVrcfComponents)
        {
            var content = _vrcFuryType.GetField("content").GetValue(comp);
            if (content == null)
            {
                componentsToRemove.Add(comp as Component);
                continue;
            }

            string contentTypeName = content.GetType().FullName;
            if (contentTypeName == "VF.Model.Feature.Toggle")
            {
                componentsToRemove.Add(comp as Component);
            }
            else if (contentTypeName == "VF.Model.Feature.ApplyDuringUpload")
            {
                componentsToRemove.Add(comp as Component);
            }
            else if (contentTypeName == "VF.Model.Feature.SetIcon")
            {
                var iconPath = content.GetType().GetField("path")?.GetValue(content) as string;
                if (iconPath != menuPath) componentsToRemove.Add(comp as Component);
            }
            else if (contentTypeName == "VF.Model.Feature.UnlimitedParameters")
            {
                // Keep - controlled by compression checkbox.
            }
            else
            {
                componentsToRemove.Add(comp as Component);
            }
        }

        foreach (var comp in componentsToRemove) Undo.DestroyObjectImmediate(comp);

        foreach (var sliderEntry in selectedSliders)
        {
            AddSliderToggleFeature(slidersObj, avatarRoot, menuPath, sliderEntry);
            AddApplyDuringUploadFeature(slidersObj, sliderEntry.name);
        }

        if (!string.IsNullOrEmpty(menuPath))
        {
            bool hasIcon = existingVrcfComponents
                .Except(componentsToRemove)
                .Any(c => _vrcFuryType.GetField("content").GetValue(c)?.GetType().FullName == "VF.Model.Feature.SetIcon");

            if (!hasIcon)
            {
                Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/ultipaw/Editor/vrcSliderIcon.png");
                if (icon != null) AddOverrideMenuIcon(slidersObj, menuPath, icon);
            }
        }
    }

    private void AddSliderToggleFeature(GameObject obj, GameObject avatarRoot, string menuPath, CustomBlendshapeEntry sliderEntry)
    {
        if (_vrcFuryType == null || _toggleType == null || _stateType == null || _blendShapeActionType == null) return;

        var vrcf = Undo.AddComponent(obj, _vrcFuryType);
        var toggleFeature = Activator.CreateInstance(_toggleType);

        string fullPath = string.IsNullOrEmpty(menuPath) ? sliderEntry.name : $"{menuPath}/{sliderEntry.name}";
        _toggleType.GetField("name")?.SetValue(toggleFeature, fullPath);
        _toggleType.GetField("slider")?.SetValue(toggleFeature, true);
        _toggleType.GetField("useGlobalParam")?.SetValue(toggleFeature, true);
        _toggleType.GetField("globalParam")?.SetValue(toggleFeature, BuildSliderGlobalParamName(sliderEntry.name));
        _toggleType.GetField("defaultSliderValue")?.SetValue(toggleFeature, GetCurrentSliderValue(avatarRoot, sliderEntry.name));

        var state = Activator.CreateInstance(_stateType);
        var blendshapeAction = CreateBlendshapeAction(sliderEntry.name, 100f);
        if (blendshapeAction != null)
        {
            var actionsField = _stateType.GetField("actions");
            if (actionsField?.GetValue(state) is System.Collections.IList actionsList)
            {
                actionsList.Add(blendshapeAction);
            }
        }

        _toggleType.GetField("state")?.SetValue(toggleFeature, state);
        _vrcFuryType.GetField("content").SetValue(vrcf, toggleFeature);
    }

    private void AddApplyDuringUploadFeature(GameObject obj, string blendshapeName)
    {
        if (_vrcFuryType == null || _applyDuringUploadType == null || _stateType == null) return;

        var vrcf = Undo.AddComponent(obj, _vrcFuryType);
        var uploadFeature = Activator.CreateInstance(_applyDuringUploadType);

        var state = Activator.CreateInstance(_stateType);
        var blendshapeAction = CreateBlendshapeAction(blendshapeName, 0f);
        if (blendshapeAction != null)
        {
            var actionsField = _stateType.GetField("actions");
            if (actionsField?.GetValue(state) is System.Collections.IList actionsList)
            {
                actionsList.Add(blendshapeAction);
            }
        }

        _applyDuringUploadType.GetField("action")?.SetValue(uploadFeature, state);
        _vrcFuryType.GetField("content").SetValue(vrcf, uploadFeature);
    }

    private object CreateBlendshapeAction(string blendshapeName, float blendshapeValue)
    {
        if (_blendShapeActionType == null) return null;

        var action = Activator.CreateInstance(_blendShapeActionType);
        _blendShapeActionType.GetField("blendShape")?.SetValue(action, blendshapeName);
        _blendShapeActionType.GetField("blendShapeValue")?.SetValue(action, blendshapeValue);
        _blendShapeActionType.GetField("renderer")?.SetValue(action, null);
        _blendShapeActionType.GetField("allRenderers")?.SetValue(action, true);
        return action;
    }

    public void OnPlayModeStateChanged(PlayModeStateChange state, GameObject avatarRoot, UltiPawVersion appliedVersion)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            StopCorrectiveRetry();
            return;
        }

        if (state != PlayModeStateChange.EnteredPlayMode) return;
        if (avatarRoot == null || appliedVersion?.customBlendshapes == null) return;
        if (!HasAnyCorrectives(appliedVersion)) return;

        _correctiveAvatarRoot = avatarRoot;
        _correctiveVersion = appliedVersion;
        _correctiveRemainingRetries = CorrectiveRetryCount;
        _correctiveNextRetryTime = timeSinceStartup + 0.1;

        update -= CorrectiveRetryTick;
        update += CorrectiveRetryTick;
    }

    private void StopCorrectiveRetry()
    {
        update -= CorrectiveRetryTick;
        _correctiveAvatarRoot = null;
        _correctiveVersion = null;
        _correctiveRemainingRetries = 0;
        _correctiveNextRetryTime = 0;
    }

    private static bool HasAnyCorrectives(UltiPawVersion version)
    {
        if (version?.customBlendshapes == null) return false;
        return version.customBlendshapes.Any(b => b?.correctiveBlendshapes != null && b.correctiveBlendshapes.Length > 0);
    }

    private void CorrectiveRetryTick()
    {
        if (!isPlaying)
        {
            StopCorrectiveRetry();
            return;
        }

        if (_correctiveAvatarRoot == null || _correctiveVersion == null)
        {
            StopCorrectiveRetry();
            return;
        }

        if (_correctiveRemainingRetries <= 0)
        {
            StopCorrectiveRetry();
            return;
        }

        if (timeSinceStartup < _correctiveNextRetryTime) return;
        _correctiveNextRetryTime = timeSinceStartup + CorrectiveRetryInterval;

        bool done = ApplyVersionCorrectivesNow(_correctiveAvatarRoot, _correctiveVersion, _correctiveRemainingRetries == 1);
        _correctiveRemainingRetries--;
        if (done) StopCorrectiveRetry();
    }

    private bool ApplyVersionCorrectivesNow(GameObject avatarRoot, UltiPawVersion version, bool finalAttempt)
    {
        var renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(s => s != null && s.sharedMesh != null)
            .ToArray();

        if (renderers.Length == 0)
        {
            if (finalAttempt) Debug.LogWarning("[UltiPaw] Correctives: no skinned meshes found.");
            return finalAttempt;
        }

        bool allSucceeded = true;
        bool hasRetryWorthyFailure = false;

        foreach (var sourceBlendshape in version.customBlendshapes ?? Array.Empty<CustomBlendshapeEntry>())
        {
            if (sourceBlendshape?.correctiveBlendshapes == null || sourceBlendshape.correctiveBlendshapes.Length == 0) continue;

            bool useSliderFactor = IsSliderFactorActive(avatarRoot, sourceBlendshape);
            string sliderGlobalParam = useSliderFactor ? GetSliderGlobalParamOrFallback(avatarRoot, sourceBlendshape.name) : null;

            foreach (var corrective in sourceBlendshape.correctiveBlendshapes)
            {
                if (corrective == null) continue;
                if (string.IsNullOrWhiteSpace(corrective.blendshapeToFix) || string.IsNullOrWhiteSpace(corrective.fixingBlendshape)) continue;

                foreach (var renderer in renderers)
                {
                    if (renderer.sharedMesh.GetBlendShapeIndex(corrective.blendshapeToFix) < 0) continue;
                    if (renderer.sharedMesh.GetBlendShapeIndex(corrective.fixingBlendshape) < 0) continue;

                    string factorParam = useSliderFactor
                        ? sliderGlobalParam
                        : BuildConstFactorParamName(sourceBlendshape, renderer);

                    float? factorDefault = useSliderFactor
                        ? null
                        : Mathf.Clamp01(GetBlendshapeWeight01(renderer, sourceBlendshape?.name));

                    var result = BlendShapeLinkService.Instance.ApplyFactorLinkToTemporaryControllers(
                        avatarRoot,
                        renderer,
                        corrective.blendshapeToFix,
                        corrective.fixingBlendshape,
                        factorParam,
                        factorDefault
                    );

                    if (!result.success)
                    {
                        allSucceeded = false;
                        bool retryWorthy =
                            result.message.IndexOf("No temporary controllers found yet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.message.IndexOf("No matching source blendshape curves", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (retryWorthy) hasRetryWorthyFailure = true;
                        else if (finalAttempt) Debug.LogWarning("[UltiPaw] Correctives: " + result.message);
                    }
                }
            }
        }

        if (allSucceeded) return true;
        if (hasRetryWorthyFailure && !finalAttempt) return false;
        return true;
    }

    public string GetFactorDebugLabel(GameObject avatarRoot, CustomBlendshapeEntry sourceBlendshape)
    {
        if (IsSliderFactorActive(avatarRoot, sourceBlendshape))
        {
            string param = GetSliderGlobalParamOrFallback(avatarRoot, sourceBlendshape?.name);
            return $"factor param: {param}";
        }

        float factor = 0f;
        if (avatarRoot != null)
        {
            var renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var first = renderers.FirstOrDefault(s => s != null && s.sharedMesh != null && s.sharedMesh.GetBlendShapeIndex(sourceBlendshape?.name) >= 0);
            if (first != null)
            {
                factor = GetBlendshapeWeight01(first, sourceBlendshape?.name);
            }
        }

        return $"factor: constant {factor:F2} (internal const param per mesh)";
    }

    private bool IsSliderFactorActive(GameObject avatarRoot, CustomBlendshapeEntry sourceBlendshape)
    {
        if (avatarRoot == null || sourceBlendshape == null || string.IsNullOrWhiteSpace(sourceBlendshape.name)) return false;
        if (!sourceBlendshape.isSlider) return false;
        return TryGetActiveSliderGlobalParam(avatarRoot, sourceBlendshape.name, out _);
    }

    private string GetSliderGlobalParamOrFallback(GameObject avatarRoot, string blendshapeName)
    {
        if (TryGetActiveSliderGlobalParam(avatarRoot, blendshapeName, out var globalParam))
        {
            return globalParam;
        }

        return BuildSliderGlobalParamName(blendshapeName);
    }

    public bool TryGetActiveSliderGlobalParam(GameObject avatarRoot, string blendshapeName, out string globalParam)
    {
        globalParam = null;
        if (avatarRoot == null || string.IsNullOrWhiteSpace(blendshapeName)) return false;

        if (_vrcFuryType == null) _vrcFuryType = FindType("VF.Model.VRCFury");
        if (_toggleType == null) _toggleType = FindType("VF.Model.Feature.Toggle");
        if (_vrcFuryType == null || _toggleType == null) return false;

        Transform slidersTransform = avatarRoot.transform.Find(SLIDERS_GAMEOBJECT_NAME);
        if (slidersTransform == null) return false;

        foreach (var comp in slidersTransform.GetComponents(_vrcFuryType))
        {
            if (!(comp is Component c)) continue;
            var content = _vrcFuryType.GetField("content")?.GetValue(c);
            if (content == null || content.GetType() != _toggleType) continue;

            bool isSlider = (bool)(_toggleType.GetField("slider")?.GetValue(content) ?? false);
            if (!isSlider) continue;

            string name = _toggleType.GetField("name")?.GetValue(content) as string;
            if (string.IsNullOrWhiteSpace(name)) continue;

            bool matchesBlendshape =
                string.Equals(name, blendshapeName, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("/" + blendshapeName, StringComparison.OrdinalIgnoreCase);
            if (!matchesBlendshape) continue;

            bool useGlobalParam = (bool)(_toggleType.GetField("useGlobalParam")?.GetValue(content) ?? false);
            if (!useGlobalParam) continue;

            string configuredParam = _toggleType.GetField("globalParam")?.GetValue(content) as string;
            if (string.IsNullOrWhiteSpace(configuredParam)) continue;

            globalParam = configuredParam;
            return true;
        }

        return false;
    }

    private static float GetBlendshapeWeight01(SkinnedMeshRenderer renderer, string blendshapeName)
    {
        if (renderer == null || renderer.sharedMesh == null || string.IsNullOrWhiteSpace(blendshapeName)) return 0f;
        int index = renderer.sharedMesh.GetBlendShapeIndex(blendshapeName);
        if (index < 0) return 0f;
        return Mathf.Clamp01(renderer.GetBlendShapeWeight(index) / 100f);
    }

    private static string BuildConstFactorParamName(CustomBlendshapeEntry sourceBlendshape, SkinnedMeshRenderer renderer)
    {
        string sourceName = sourceBlendshape?.name ?? "blendshape";
        string rendererToken = renderer != null ? SanitizeParamToken(renderer.transform.GetHierarchyPath()) : "renderer";
        return CONST_PARAM_PREFIX + SanitizeParamToken(sourceName) + "_" + rendererToken;
    }

    private static string BuildSliderGlobalParamName(string sliderName)
    {
        return SLIDER_PARAM_PREFIX + SanitizeParamToken(sliderName);
    }

    private static string SanitizeParamToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unnamed";
        var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        string output = new string(chars);
        while (output.Contains("__")) output = output.Replace("__", "_");
        output = output.Trim('_');
        if (string.IsNullOrWhiteSpace(output)) output = "Unnamed";
        if (output.Length > 48) output = output.Substring(0, 48);
        return output;
    }

    private static float GetCurrentSliderValue(GameObject avatarRoot, string blendshapeName)
    {
        if (avatarRoot == null || string.IsNullOrEmpty(blendshapeName)) return 0f;

        var skinnedMeshes = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skinnedMeshes == null || skinnedMeshes.Length == 0) return 0f;

        SkinnedMeshRenderer bodySmr = skinnedMeshes
            .FirstOrDefault(smr => smr != null && smr.gameObject.name.Equals("Body", StringComparison.OrdinalIgnoreCase));

        if (bodySmr != null && bodySmr.sharedMesh != null)
        {
            int bodyIndex = bodySmr.sharedMesh.GetBlendShapeIndex(blendshapeName);
            if (bodyIndex >= 0) return Mathf.Clamp01(bodySmr.GetBlendShapeWeight(bodyIndex) / 100f);
        }

        foreach (var smr in skinnedMeshes)
        {
            if (smr == null || smr.sharedMesh == null) continue;
            int blendshapeIndex = smr.sharedMesh.GetBlendShapeIndex(blendshapeName);
            if (blendshapeIndex >= 0) return Mathf.Clamp01(smr.GetBlendShapeWeight(blendshapeIndex) / 100f);
        }

        return 0f;
    }

    private void AddOverrideMenuIcon(GameObject obj, string menuPath, Texture2D icon)
    {
        if (_vrcFuryType == null) _vrcFuryType = FindType("VF.Model.VRCFury");
        if (_setIconType == null) _setIconType = FindType("VF.Model.Feature.SetIcon");
        if (_guidTextureType == null) _guidTextureType = FindType("VF.Model.GuidTexture2d");

        if (_vrcFuryType == null || _setIconType == null || _guidTextureType == null) return;

        var vrcf = Undo.AddComponent(obj, _vrcFuryType);
        var feature = Activator.CreateInstance(_setIconType);
        _setIconType.GetField("path").SetValue(feature, menuPath);

        var guidTex = Activator.CreateInstance(_guidTextureType);
        _guidTextureType.GetField(
                "objRef",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy
            )
            .SetValue(guidTex, icon);
        _setIconType.GetField("icon").SetValue(feature, guidTex);

        _vrcFuryType.GetField("content").SetValue(vrcf, feature);
    }

    public ParameterUsage GetAvatarParameterUsage(GameObject avatarRoot, int selectedUltiPawSlidersCount)
    {
        var usage = AvatarParametersService.Instance.GetAvatarParameterUsage(avatarRoot, selectedUltiPawSlidersCount);
        return new ParameterUsage
        {
            currentSyncedBits = usage.currentSyncedBits,
            totalUsedAfterBuild = usage.totalUsedAfterBuild,
            usedByAvatar = usage.usedByAvatar,
            usedBySliders = usage.usedBySliders,
            compressionEnabled = usage.compressionEnabled,
            compressionIsExternal = usage.compressionIsExternal,
            compressionPath = usage.compressionPath
        };
    }

    public ParameterUsage GetAvatarParameterUsage(GameObject avatarRoot)
    {
        return GetAvatarParameterUsage(avatarRoot, 0);
    }

    public void SetCompression(GameObject avatarRoot, bool enabled)
    {
        if (avatarRoot == null) return;

        if (_vrcFuryType == null) _vrcFuryType = FindType("VF.Model.VRCFury");
        if (_unlimitedType == null) _unlimitedType = FindType("VF.Model.Feature.UnlimitedParameters");

        if (_vrcFuryType == null || _unlimitedType == null)
        {
            Debug.LogError("[UltiPaw] VRCFury types not found. Cannot toggle compression.");
            return;
        }

        Transform slidersTransform = avatarRoot.transform.Find(SLIDERS_GAMEOBJECT_NAME);
        GameObject slidersObj;
        if (slidersTransform == null)
        {
            if (!enabled) return;
            slidersObj = new GameObject(SLIDERS_GAMEOBJECT_NAME);
            slidersObj.transform.SetParent(avatarRoot.transform, false);
            Undo.RegisterCreatedObjectUndo(slidersObj, "Create UltiPaw Sliders GameObject");
        }
        else
        {
            slidersObj = slidersTransform.gameObject;
        }

        var existingVrcfComponents = slidersObj.GetComponents(_vrcFuryType);
        var compressionComp = existingVrcfComponents.FirstOrDefault(c => _vrcFuryType.GetField("content").GetValue(c)?.GetType() == _unlimitedType);

        if (enabled)
        {
            if (compressionComp == null)
            {
                var vrcf = Undo.AddComponent(slidersObj, _vrcFuryType);
                var feature = Activator.CreateInstance(_unlimitedType);
                _vrcFuryType.GetField("content").SetValue(vrcf, feature);
                Debug.Log("[UltiPaw] VRCFury Parameter Compression enabled.");
            }
        }
        else
        {
            if (compressionComp != null && compressionComp is Component comp)
            {
                Undo.DestroyObjectImmediate(comp);
                Debug.Log("[UltiPaw] VRCFury Parameter Compression disabled.");
            }
        }
    }

    private Type FindType(string fullName)
    {
        if (fullName == "VF.Model.VRCFury" && _vrcFuryType != null) return _vrcFuryType;
        if (fullName == "VF.Model.Feature.SetIcon" && _setIconType != null) return _setIconType;
        if (fullName == "VF.Model.GuidTexture2d" && _guidTextureType != null) return _guidTextureType;
        if (fullName == "VF.Model.Feature.Toggle" && _toggleType != null) return _toggleType;
        if (fullName == "VF.Model.Feature.UnlimitedParameters" && _unlimitedType != null) return _unlimitedType;
        if (fullName == "VF.Model.Feature.ApplyDuringUpload" && _applyDuringUploadType != null) return _applyDuringUploadType;
        if (fullName == "VF.Model.State" && _stateType != null) return _stateType;
        if (fullName == "VF.Model.StateAction.BlendShapeAction" && _blendShapeActionType != null) return _blendShapeActionType;

        string[] assemblyNames = { "VRCFury-Runtime", "VRCFury-Editor", "VRCFury" };
        foreach (var assemblyName in assemblyNames)
        {
            var type = Type.GetType($"{fullName}, {assemblyName}");
            if (type != null) return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName);
            if (type != null) return type;
            if (assembly.GetName().Name.Contains("VRCFury"))
            {
                type = assembly.GetTypes().FirstOrDefault(t => t.FullName == fullName);
                if (type != null) return type;
            }
        }

        return null;
    }
}

static class TransformPathExt
{
    public static string GetHierarchyPath(this Transform t)
    {
        if (t == null) return "null";
        var names = new List<string>();
        var current = t;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }
}
#endif
