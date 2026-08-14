using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>Marker used by validation and by later visual-only upgrades.</summary>
    public sealed class NebulaVisualInstance : MonoBehaviour
    {
        public string VisualId { get; private set; }
        public void Initialize(string visualId) => VisualId = visualId;
    }

    /// <summary>
    /// Cached, fail-safe Resources prefab loader. Instantiated visuals are
    /// render-only children: cameras/lights/colliders are forcibly disabled
    /// and the owner's existing gameplay components remain authoritative.
    /// </summary>
    public static class NebulaVisualResolver
    {
        private static readonly Dictionary<string, GameObject> PrefabCache =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        public static GameObject Instantiate(
            NebulaVisualDefinition definition,
            Transform parent,
            float runtimeDiameter = 0f,
            string instanceName = null)
        {
            if (definition == null || parent == null) return null;
            GameObject prefab = Load(definition.ResourcePath);
            if (prefab == null) return null;

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = string.IsNullOrEmpty(instanceName)
                ? "Visual :: " + definition.VisualId
                : instanceName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Sanitize(instance);
            ApplyPresentationProfile(instance, definition.VisualId);
            if (definition.ScalePolicy != NebulaVisualScalePolicy.Meters && runtimeDiameter > 0f)
                FitUniformToDiameter(instance.transform, runtimeDiameter, definition.ScaleAnchorName);
            if (!string.IsNullOrEmpty(definition.PlacementAnchorName))
                AlignPlacementAnchor(instance.transform, definition.PlacementAnchorName);

            instance.AddComponent<NebulaVisualInstance>().Initialize(definition.VisualId);
            return instance;
        }

        public static GameObject InstantiateItem(string contentId, Transform parent, string instanceName = null)
        {
            return NebulaVisualCatalog.TryGetItem(contentId, out NebulaVisualDefinition definition)
                ? Instantiate(definition, parent, instanceName: instanceName)
                : null;
        }

        /// <summary>
        /// Loads and mounts a wrapper prefab straight from its <c>Resources</c>
        /// path, with no <see cref="NebulaVisualCatalog"/> lookup anywhere in the
        /// call.
        /// <para>
        /// This exists for exactly one caller —
        /// <c>PlayerShipVisualController.CreateFallbackHullVisual</c> — and the
        /// reason it is not simply <see cref="Instantiate(NebulaVisualDefinition,
        /// Transform, float, string)"/> with a hand-built definition is the state
        /// it runs in. The fail-safe path is only reached once the catalog has
        /// already failed to produce a hull, so it must not read the catalog to
        /// recover from the catalog. Everything it does need — collider, camera
        /// and light suppression, LOD fade normalisation, and the identical
        /// envelope fit the normal path uses — is geometry and sanitation that
        /// lives here and has no table behind it.
        /// </para>
        /// <para>
        /// The per-visual presentation profile is deliberately not applied: it is
        /// keyed by catalog <c>visualId</c> and only ever addresses celestial
        /// bodies, so running it would be a lookup with nothing to find.
        /// </para>
        /// </summary>
        /// <returns>The mounted instance, or null when the prefab is not present —
        /// a missing fail-safe must degrade, never throw.</returns>
        public static GameObject InstantiatePrefabPath(
            string resourcePath,
            Transform parent,
            float runtimeDiameter = 0f,
            string instanceName = null)
        {
            if (parent == null) return null;
            GameObject prefab = Load(resourcePath);
            if (prefab == null) return null;

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = string.IsNullOrEmpty(instanceName) ? prefab.name : instanceName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Sanitize(instance);
            if (runtimeDiameter > 0f) FitUniformToDiameter(instance.transform, runtimeDiameter, null);
            return instance;
        }

        public static GameObject Load(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return null;
            if (!PrefabCache.TryGetValue(resourcePath, out GameObject prefab))
            {
                prefab = Resources.Load<GameObject>(resourcePath);
                PrefabCache[resourcePath] = prefab;
            }
            return prefab;
        }

        public static void ClearCache() => PrefabCache.Clear();

        public static void DestroyInstance(GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }

        public static Transform FindAnchor(Transform root, string exactName)
        {
            if (root == null || string.IsNullOrEmpty(exactName)) return null;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (string.Equals(transforms[i].name, exactName, StringComparison.Ordinal))
                    return transforms[i];
            return null;
        }

        private static void Sanitize(GameObject instance)
        {
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Camera camera in instance.GetComponentsInChildren<Camera>(true))
                camera.enabled = false;
            foreach (Light light in instance.GetComponentsInChildren<Light>(true))
                light.enabled = false;

            // The generated wrappers used cross-fade LODs, while glTFast's
            // imported shaders do not compile Unity's LOD cross-fade variant.
            // Keeping the mismatch makes two LOD meshes overlap and produces
            // bright/jagged silhouettes. Hard switching preserves the authored
            // geometry and avoids rendering duplicate layers.
            foreach (LODGroup lodGroup in instance.GetComponentsInChildren<LODGroup>(true))
            {
                lodGroup.fadeMode = LODFadeMode.None;
                lodGroup.animateCrossFading = false;
            }
        }

        private static void ApplyPresentationProfile(GameObject instance, string visualId)
        {
            // CorrectedExport preserves the author's optional atmosphere and
            // vapour shells. In Unity's linear Built-in renderer those full
            // sphere layers read as a pale veil and hide the baked BaseColor.
            // Disable only the enclosing veil; weather, rings, terrain,
            // crystals and emissive surface detail remain authored and active.
            switch (visualId)
            {
                case "visual.celestial.planet.metal":
                    SetNamedLayersActive(instance.transform, false, "vapour-halo", "vapour-outer");
                    break;
                case "visual.celestial.planet.wood":
                case "visual.celestial.planet.water":
                    SetNamedLayersActive(instance.transform, false, "atmosphere");
                    break;
                case "visual.celestial.planet.gas":
                    SetNamedLayersActive(instance.transform, false, "atmosphere-haze");
                    break;
                case "visual.celestial.planet.diamond":
                    ApplyDiamondPresentation(instance);
                    break;
            }
        }

        private static void ApplyDiamondPresentation(GameObject instance)
        {
            // The crystalline shell intentionally contains gaps. Hiding its
            // authored core exposed the hollow interior, while the nearly
            // white shell materials washed out under the system light. Keep
            // the opaque core as the interior fill and apply per-instance
            // colour overrides so shared imported materials stay untouched.
            SetNamedLayersActive(instance.transform, true, "diamond-core");
            ApplyLayerTint(instance.transform, "diamond-core", new Color(0.10f, 0.20f, 0.46f, 1f));
            ApplyLayerTint(instance.transform, "planet-crust", new Color(0.27f, 0.56f, 0.91f, 1f));
            ApplyLayerTint(instance.transform, "crystal-field-clear", new Color(0.44f, 0.76f, 1f, 1f));
            ApplyLayerTint(instance.transform, "crystal-field-ice", new Color(0.38f, 0.61f, 0.94f, 1f));
            ApplyLayerTint(instance.transform, "crystal-monoliths", new Color(0.30f, 0.48f, 0.86f, 1f));
        }

        private static void ApplyLayerTint(Transform root, string layerName, Color tint)
        {
            Transform layer = FindAnchor(root, layerName);
            if (layer == null) return;

            foreach (Renderer renderer in layer.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material material = materials[index];
                    if (material == null) continue;
                    if (material.HasProperty("baseColorFactor"))
                        block.SetColor("baseColorFactor", tint);
                    if (material.HasProperty("_BaseColor"))
                        block.SetColor("_BaseColor", tint);
                    if (material.HasProperty("_Color"))
                        block.SetColor("_Color", tint);
                }
                renderer.SetPropertyBlock(block);
            }
        }

        private static void SetNamedLayersActive(
            Transform root, bool active, params string[] exactNames)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            for (int nameIndex = 0; nameIndex < exactNames.Length; nameIndex++)
                if (string.Equals(transforms[index].name, exactNames[nameIndex], StringComparison.Ordinal))
                    transforms[index].gameObject.SetActive(active);
        }

        private static void FitUniformToDiameter(
            Transform root, float targetDiameter, string scaleAnchorName)
        {
            Transform renderRoot = string.IsNullOrEmpty(scaleAnchorName)
                ? root
                : FindAnchor(root, scaleAnchorName);
            if (renderRoot == null) renderRoot = root;
            if (!TryCalculateLocalBounds(root, renderRoot, out Bounds bounds)) return;
            float authoredDiameter = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (authoredDiameter <= 0.0001f) return;

            float scale = targetDiameter / authoredDiameter;
            root.localScale = Vector3.one * scale;
            root.localPosition = -bounds.center * scale;
        }

        private static void AlignPlacementAnchor(Transform root, string anchorName)
        {
            Transform anchor = FindAnchor(root, anchorName);
            Transform parent = root.parent;
            if (anchor == null || parent == null) return;
            Vector3 anchorInParent = parent.InverseTransformPoint(anchor.position);
            root.localPosition -= anchorInParent;
        }

        private static bool TryCalculateLocalBounds(
            Transform root, Transform renderRoot, out Bounds result)
        {
            Renderer[] renderers = renderRoot.GetComponentsInChildren<Renderer>(true);
            result = default;
            bool found = false;
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;

            foreach (Renderer renderer in renderers)
            {
                Bounds local;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    local = skinned.localBounds;
                }
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    local = filter.sharedMesh.bounds;
                }

                Matrix4x4 toRoot = worldToRoot * renderer.transform.localToWorldMatrix;
                Vector3 min = local.min;
                Vector3 max = local.max;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                    Vector3 point = toRoot.MultiplyPoint3x4(corner);
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            }
            return found;
        }
    }
}
