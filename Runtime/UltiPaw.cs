#if UNITY_EDITOR
using VRC.SDKBase;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

// Represents a blendshape with a default value in creator mode.
[Serializable]
public class CreatorBlendshapeEntry
{
    public string name;
    public string defaultValue;
}

// This component is a pure data container for an avatar that has been modified
// by UltiPaw. It holds only the state that needs to be saved with the scene/prefab.
public class UltiPaw : MonoBehaviour
#if UNITY_EDITOR
    , IEditorOnly
#endif
{
    // --- USER CONFIGURATION ---
    [Tooltip("If checked, you must manually assign the FBX file below. If unchecked, the tool will try to find it automatically.")]
    public bool specifyCustomBaseFbx = false;

    [Tooltip("The base FBX file for this avatar. Used to find compatible UltiPaw versions.")]
    public List<GameObject> baseFbxFiles = new List<GameObject>();

    // --- APPLIED STATE ---
    [Tooltip("The version information of the UltiPaw modification that is currently applied to this avatar's FBX.")]
    [HideInInspector] public UltiPawVersion appliedUltiPawVersion = null;

    [Tooltip("Stores the current values of the custom blendshape sliders.")]
    [HideInInspector] public List<float> blendShapeValues = new List<float>();

    [Tooltip("Stores user-customized blendshape values that override defaults (keys: blendshape names).")]
    [HideInInspector] [SerializeField] public List<string> customBlendshapeOverrideNames = new List<string>();
    
    [Tooltip("Stores user-customized blendshape values that override defaults (values: custom values).")]
    [HideInInspector] [SerializeField] public List<float> customBlendshapeOverrideValues = new List<float>();

    [Tooltip("Enable/disable dynamic normals recalculation for blendshapes.")]
    [HideInInspector] public bool useDynamicNormals = true;

    [Tooltip("Use an A-Pose instead of T-Pose for dynamic normal calculations to prevent thigh clipping.")]
    [HideInInspector] public bool useAPoseForDynamicNormals = true;

    // --- CREATOR MODE PERSISTENT DATA ---
    [HideInInspector] public bool isCreatorMode = false;
    [HideInInspector] public GameObject customFbxForCreator;
    [HideInInspector] public Avatar ultipawAvatarForCreatorProp;
    [HideInInspector] public GameObject avatarLogicPrefab;
    [HideInInspector] public bool includeCustomVeinsForCreator = false;
    [HideInInspector] public Texture2D customVeinsNormalMap;
    [HideInInspector] public bool includeDynamicNormalsBodyForCreator = false;
    [HideInInspector] public bool includeDynamicNormalsFlexingForCreator = false;
    [HideInInspector] public List<CreatorBlendshapeEntry> customBlendshapesForCreator = new List<CreatorBlendshapeEntry>();
}