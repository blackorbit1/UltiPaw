#if UNITY_EDITOR
using VRC.SDKBase;
#endif
using System.Collections.Generic;
using UnityEngine;

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

    // --- CREATOR MODE PERSISTENT DATA ---
    [HideInInspector] public bool isCreatorMode = false;
    [HideInInspector] public GameObject customFbxForCreator;
    [HideInInspector] public Avatar ultipawAvatarForCreatorProp;
    [HideInInspector] public GameObject avatarLogicPrefab;
    [HideInInspector] public bool includeCustomVeinsForCreator = false;
    [HideInInspector] public Texture2D customVeinsNormalMap;
    [HideInInspector] public List<string> customBlendshapesForCreator = new List<string>();
}