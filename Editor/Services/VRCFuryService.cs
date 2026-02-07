#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using com.vrcfury.api;
using UnityEditor;
using UnityEngine;

public class VRCFuryService
{
    private static VRCFuryService _instance;
    public static VRCFuryService Instance => _instance ??= new VRCFuryService();

    private const string SLIDERS_GAMEOBJECT_NAME = "ultipaw sliders";

    // Type Cache
    private System.Type _vrcFuryType;
    private System.Type _setIconType;
    private System.Type _guidTextureType;
    private System.Type _toggleType;

    public void ApplySliders(GameObject avatarRoot, string menuPath, List<CustomBlendshapeEntry> selectedSliders)
    {
        if (avatarRoot == null)
        {
            Debug.LogError("[UltiPaw] Avatar root is null. Cannot apply sliders.");
            return;
        }

        // 1. Locate the "Body" SkinnedMeshRenderer
        SkinnedMeshRenderer bodyMesh = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(smr => smr.gameObject.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));

        if (bodyMesh == null)
        {
            bodyMesh = avatarRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        if (bodyMesh == null)
        {
            Debug.LogError("[UltiPaw] No SkinnedMeshRenderer found on avatar.");
            return;
        }

        // 2. Find or create the "ultipaw sliders" GameObject
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

        // 3. Smart Sync: Get existing VRCFury components
        if (_vrcFuryType == null) _vrcFuryType = FindType("VF.Model.VRCFury");
        if (_vrcFuryType == null) {
            Debug.LogError("[UltiPaw] VRCFury not found. Cannot sync sliders.");
            return;
        }

        var existingVrcfComponents = slidersObj.GetComponents(_vrcFuryType).ToList();
        var componentsToRemove = new List<Component>();
        var slidersToCreate = new List<CustomBlendshapeEntry>(selectedSliders);

        foreach (var comp in existingVrcfComponents)
        {
            var content = _vrcFuryType.GetField("content").GetValue(comp);
            if (content == null) { componentsToRemove.Add(comp as Component); continue; }

            string contentTypeName = content.GetType().FullName;
            
            // Check if it's a Toggle (vrcfury toggle)
            if (contentTypeName == "VF.Model.Feature.Toggle")
            {
                var togglePath = content.GetType().GetField("name")?.GetValue(content) as string;
                var matchingSlider = slidersToCreate.FirstOrDefault(s => $"{menuPath}/{s.name}" == togglePath);
                if (matchingSlider != null)
                {
                    slidersToCreate.Remove(matchingSlider);
                }
                else
                {
                    componentsToRemove.Add(comp as Component);
                }
            }
            // Check if it's the Menu Icon
            else if (contentTypeName == "VF.Model.Feature.SetIcon")
            {
                var iconPath = content.GetType().GetField("path")?.GetValue(content) as string;
                if (iconPath == menuPath) { /* Keep */ }
                else componentsToRemove.Add(comp as Component);
            }
            else componentsToRemove.Add(comp as Component);
        }

        // 4. Execute Changes
        foreach (var comp in componentsToRemove) Undo.DestroyObjectImmediate(comp);

        foreach (var sliderEntry in slidersToCreate)
        {
            var toggle = FuryComponents.CreateToggle(slidersObj);
            string fullPath = string.IsNullOrEmpty(menuPath) ? sliderEntry.name : $"{menuPath}/{sliderEntry.name}";
            toggle.SetMenuPath(fullPath);
            toggle.SetSlider(true);
            toggle.GetActions().AddBlendshape(sliderEntry.name, 100, bodyMesh);
        }

        // 5. Add/Update Override Menu Icon if missing
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

    private void AddOverrideMenuIcon(GameObject obj, string menuPath, Texture2D icon)
    {
        if (_vrcFuryType == null) _vrcFuryType = FindType("VF.Model.VRCFury");
        if (_setIconType == null) _setIconType = FindType("VF.Model.Feature.SetIcon");
        if (_guidTextureType == null) _guidTextureType = FindType("VF.Model.GuidTexture2d");

        if (_vrcFuryType == null || _setIconType == null || _guidTextureType == null) return;

        var vrcf = Undo.AddComponent(obj, _vrcFuryType);
        var feature = System.Activator.CreateInstance(_setIconType);
        _setIconType.GetField("path").SetValue(feature, menuPath);

        var guidTex = System.Activator.CreateInstance(_guidTextureType);
        _guidTextureType.GetField("objRef", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)
            .SetValue(guidTex, icon);
        _setIconType.GetField("icon").SetValue(feature, guidTex);

        _vrcFuryType.GetField("content").SetValue(vrcf, feature);
    }

    private System.Type FindType(string fullName)
    {
        // Check if already in cache (redundant but safe)
        if (fullName == "VF.Model.VRCFury" && _vrcFuryType != null) return _vrcFuryType;
        if (fullName == "VF.Model.Feature.SetIcon" && _setIconType != null) return _setIconType;
        if (fullName == "VF.Model.GuidTexture2d" && _guidTextureType != null) return _guidTextureType;

        string[] assemblyNames = { "VRCFury-Runtime", "VRCFury-Editor", "VRCFury" };
        foreach (var assemblyName in assemblyNames)
        {
            var type = System.Type.GetType($"{fullName}, {assemblyName}");
            if (type != null) return type;
        }

        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
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
#endif
