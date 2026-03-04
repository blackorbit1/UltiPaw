#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

namespace UltiPawEditorUtils
{
    public static class MeshFinder
    {
        /// <summary>
        /// Finds a SkinnedMeshRenderer by name, prioritizing children directly under the root.
        /// This avoids picking up nested meshes (like those in Armature) when a root-level one exists.
        /// </summary>
        public static SkinnedMeshRenderer FindMeshPrioritizingRoot(Transform root, string meshName)
        {
            if (root == null || string.IsNullOrEmpty(meshName)) return null;

            // Priority 1: Direct children of the root
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name.Equals(meshName, StringComparison.OrdinalIgnoreCase))
                {
                    var smr = child.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null) return smr;
                }
            }

            // Priority 2: Anywhere else in the hierarchy (fallback)
            var allSmrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            return allSmrs.FirstOrDefault(s => s.gameObject.name.Equals(meshName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all SkinnedMeshRenderers in the hierarchy.
        /// </summary>
        public static SkinnedMeshRenderer[] GetAllSkinnedMeshRenderers(Transform root)
        {
            if (root == null) return Array.Empty<SkinnedMeshRenderer>();
            return root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }
    }
}
#endif
