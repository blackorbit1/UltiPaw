#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public class UltiPawVersionManager
{
  [System.Serializable]
  public class UltiPawVersionResponse
  {
      public string recommendedVersion;
      public List<UltiPawVersion> versions;
  }

  [System.Serializable]
  public class UltiPawVersion
  {
      public string version;
      public string scope;
      public string date;
      public string changelog;
      public string customAviHash;      // Expected hash for the ultipaw.bin file.
      public string defaultAviHash;     // Expected hash for the base FBX.
      public string defaultAvatarPath;  // Path to "default avatar.avatar" (for reset).
      public string ultipawAvatarPath;  // Path to "ultipaw avatar.avatar" (for transformation).
      public string[] customBlendshapes; // Custom blendshape names to use for this version.
      public Dictionary<string, string> dependencies;
  }
}
#endif
