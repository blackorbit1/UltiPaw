#if UNITY_EDITOR
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

public class BlendShapeLinkPostVrcfuryHook : IVRCSDKPreprocessAvatarCallback
{
    // VRCFury uses -10000; this runs after generated controllers are assigned.
    public int callbackOrder => -9000;

    public bool OnPreprocessAvatar(GameObject avatarRoot)
    {
        var versionResult = BlendShapeLinkService.Instance.ApplyActiveVersionFactorLinks(avatarRoot);
        if (versionResult.success)
        {
            UltiPawLogger.Log("[UltiPaw] " + versionResult.message);
        }
        else
        {
            UltiPawLogger.Log("[UltiPaw] Version BlendShape links skipped: " + versionResult.message);
        }

        var manualResult = BlendShapeLinkService.Instance.ApplyConfiguredFactorLinks(avatarRoot);
        if (manualResult.success)
        {
            UltiPawLogger.Log("[UltiPaw] " + manualResult.message);
        }
        else
        {
            UltiPawLogger.Log("[UltiPaw] Manual BlendShape links skipped: " + manualResult.message);
        }

        return true;
    }
}
#endif
