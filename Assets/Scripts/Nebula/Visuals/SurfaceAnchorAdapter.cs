using UnityEngine;
using Nebula.Sectors;

namespace Nebula.Visuals
{
    /// <summary>
    /// Shared authored grounding visual for Command/Defense/Support/Drill
    /// structures. It is visual-only and never changes the canonical anchor.
    /// </summary>
    public static class SurfaceAnchorAdapter
    {
        /// <summary>Creates the archetype-specific authored foundation. The old primitive is retained only as an import-failure fallback.</summary>
        public static GameObject CreatePlatform(Transform parent, SurfaceAnchorCategory category, float unit)
        {
            if (NebulaVisualCatalog.TryGetFoundation(category, out NebulaVisualDefinition definition))
            {
                GameObject authored = NebulaVisualResolver.Instantiate(definition, parent, instanceName: "Authored Surface Foundation");
                if (authored != null) return authored;
            }

            // Solid never needed a procedural adapter; preserving null here
            // keeps the old fail-safe behavior if its asset cannot load.
            if (category == SurfaceAnchorCategory.Solid) return null;

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = category == SurfaceAnchorCategory.Water ? "Water Adapter Platform (Fallback)" : "Gas Adapter Platform (Fallback)";
            Object.Destroy(platform.GetComponent<Collider>());
            platform.transform.SetParent(parent, false);
            platform.transform.localScale = new Vector3(unit * 1.8f, unit * 0.12f, unit * 1.8f);
            platform.transform.localPosition = Vector3.down * (unit * 0.5f);

            Color color = category == SurfaceAnchorCategory.Water ? new Color(0.5f, 0.6f, 0.65f) : new Color(0.7f, 0.72f, 0.75f);
            platform.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateLit(color, 0.3f, 0.4f);
            return platform;
        }
    }
}
