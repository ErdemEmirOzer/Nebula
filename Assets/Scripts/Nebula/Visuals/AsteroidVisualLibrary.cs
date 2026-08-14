using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nebula.Visuals
{
    /// <summary>
    /// Low-overhead asteroid visual instance. Each rock remains one renderer;
    /// the central drift system swaps the three combined authored LOD meshes.
    /// </summary>
    public sealed class AsteroidAuthoredVisual : MonoBehaviour
    {
        private AsteroidVisualLibrary.Variant variant;
        private MeshFilter filter;
        private MeshRenderer meshRenderer;
        private int currentLod = -1;

        internal void Initialize(AsteroidVisualLibrary.Variant value, MeshFilter targetFilter, MeshRenderer targetRenderer)
        {
            variant = value;
            filter = targetFilter;
            meshRenderer = targetRenderer;
            SetLod(0);
        }

        public void SetLod(int lod)
        {
            if (variant == null || filter == null || meshRenderer == null) return;
            lod = Mathf.Clamp(lod, 0, variant.Meshes.Length - 1);
            if (lod == currentLod) return;
            filter.sharedMesh = variant.Meshes[lod];
            meshRenderer.sharedMaterials = variant.Materials[lod];
            currentLod = lod;
        }
    }

    /// <summary>
    /// Extracts each asteroid prefab's authored LOD geometry once, combines
    /// each LOD into one shared mesh, and reuses those meshes/materials for
    /// every asteroid. This avoids multiplying the prefab's hierarchy and
    /// renderer count across hundreds of rocks per system.
    /// </summary>
    public static class AsteroidVisualLibrary
    {
        internal sealed class Variant
        {
            public Mesh[] Meshes;
            public Material[][] Materials;
            public float AuthoredDiameter;
        }

        private static readonly Variant[] Variants = new Variant[3];
        private static bool initialized;

        public static bool Apply(GameObject rock, int variantIndex, float runtimeDiameter, out Renderer renderer)
        {
            renderer = null;
            EnsureInitialized();
            Variant variant = Variants[Mathf.Abs(variantIndex) % Variants.Length];
            if (variant == null || variant.Meshes == null || variant.Meshes.Length == 0) return false;

            var filter = rock.AddComponent<MeshFilter>();
            var meshRenderer = rock.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            rock.transform.localScale = Vector3.one * (runtimeDiameter / Mathf.Max(0.0001f, variant.AuthoredDiameter));

            var authored = rock.AddComponent<AsteroidAuthoredVisual>();
            authored.Initialize(variant, filter, meshRenderer);
            renderer = meshRenderer;
            return true;
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            for (int i = 0; i < Variants.Length; i++)
                Variants[i] = BuildVariant(NebulaVisualCatalog.Asteroid(i), i);
        }

        private static Variant BuildVariant(NebulaVisualDefinition definition, int variantIndex)
        {
            GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
            if (prefab == null) return null;

            GameObject template = Object.Instantiate(prefab);
            template.name = "Asteroid Visual Template";
            template.SetActive(false);
            template.transform.position = Vector3.zero;
            template.transform.rotation = Quaternion.identity;
            template.transform.localScale = Vector3.one;

            LODGroup group = template.GetComponent<LODGroup>();
            LOD[] sourceLods = group != null ? group.GetLODs() : null;
            if (sourceLods == null || sourceLods.Length == 0)
                sourceLods = new[] { new LOD(0f, template.GetComponentsInChildren<Renderer>(true)) };

            int lodCount = Mathf.Min(3, sourceLods.Length);
            var meshes = new Mesh[lodCount];
            var materials = new Material[lodCount][];
            float diameter = 1f;

            for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
            {
                if (!TryCombine(template.transform, sourceLods[lodIndex].renderers,
                        $"Asteroid Variant {variantIndex} LOD{lodIndex}", out Mesh mesh, out Material[] mats))
                {
                    DestroyTemplate(template);
                    return null;
                }
                meshes[lodIndex] = mesh;
                materials[lodIndex] = mats;
                if (lodIndex == 0)
                    diameter = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
            }

            DestroyTemplate(template);
            return new Variant { Meshes = meshes, Materials = materials, AuthoredDiameter = diameter };
        }

        private static bool TryCombine(Transform root, Renderer[] renderers, string meshName, out Mesh mesh, out Material[] materials)
        {
            var combines = new List<CombineInstance>();
            var materialList = new List<Material>();
            int vertexCount = 0;

            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is MeshRenderer)) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh source = filter != null ? filter.sharedMesh : null;
                if (source == null) continue;

                Material[] sourceMaterials = renderer.sharedMaterials;
                int subMeshCount = Mathf.Max(1, source.subMeshCount);
                for (int sub = 0; sub < subMeshCount; sub++)
                {
                    combines.Add(new CombineInstance
                    {
                        mesh = source,
                        subMeshIndex = Mathf.Min(sub, source.subMeshCount - 1),
                        transform = root.worldToLocalMatrix * filter.transform.localToWorldMatrix
                    });
                    Material material = sourceMaterials.Length > 0
                        ? sourceMaterials[Mathf.Min(sub, sourceMaterials.Length - 1)]
                        : null;
                    if (material != null) material.enableInstancing = true;
                    materialList.Add(material);
                }
                vertexCount += source.vertexCount;
            }

            if (combines.Count == 0)
            {
                mesh = null;
                materials = null;
                return false;
            }

            mesh = new Mesh { name = meshName };
            if (vertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(combines.ToArray(), false, true, false);
            mesh.RecalculateBounds();
            materials = materialList.ToArray();
            return true;
        }

        private static void DestroyTemplate(GameObject template)
        {
            if (Application.isPlaying) Object.Destroy(template);
            else Object.DestroyImmediate(template);
        }
    }
}
