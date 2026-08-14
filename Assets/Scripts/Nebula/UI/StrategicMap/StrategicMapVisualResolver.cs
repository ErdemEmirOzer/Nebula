using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// A thin adapter onto the game's EXISTING visual catalogs, so every star, planet,
    /// structure, resource and status mark on the strategic map is the same real
    /// shipped asset the game world uses — never a re-drawn placeholder and never a
    /// raw GLB pulled in behind the catalogs' back.
    ///
    /// Two kinds of art are needed by a uGUI map:
    /// <list type="bullet">
    /// <item>Sprites — stars, icons and badges already ship as real textures, so they
    /// are simply wrapped (and cached) as sprites.</item>
    /// <item>Thumbnails — planets and structures ship as 3D prefabs, so a small
    /// <see cref="RenderTexture"/> preview is rendered ONCE per visual id from the real
    /// prefab and reused. The preview objects live on a dedicated hidden layer and are
    /// never gameplay entities: they have no colliders, never register with any
    /// gameplay system, and never appear in the played scene.</item>
    /// </list>
    ///
    /// Every lookup degrades safely: a missing asset yields a controlled fallback so
    /// the map still opens and still reads, rather than throwing or showing a broken
    /// magenta material.
    /// </summary>
    public static class StrategicMapVisualResolver
    {
        private const int ThumbnailSize = 256;

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> ThumbnailCache = new Dictionary<string, Sprite>();

        // ------------------------------------------------------------------ stars

        /// <summary>The real produced surface texture for a star class, as a sprite.</summary>
        public static Sprite StarSurface(StarClass starClass) =>
            SpriteFor(NebulaProducedVisualCatalog.StarSurface(starClass));

        /// <summary>The real produced additive corona texture for a star class.</summary>
        public static Sprite StarCorona(StarClass starClass) =>
            SpriteFor(NebulaProducedVisualCatalog.StarCorona(starClass));

        // ------------------------------------------------------------------ status art

        public static Sprite Knowledge(Navigation.SystemKnowledgeLevel level) =>
            SpriteFor(NebulaProducedVisualCatalog.Knowledge(level));

        public static Sprite Dominance(DominanceState state) =>
            SpriteFor(NebulaProducedVisualCatalog.Dominance(state));

        public static Sprite Link(SystemLinkState state) =>
            SpriteFor(NebulaProducedVisualCatalog.Link(state));

        public static Sprite Environment(EnvironmentClass environment) =>
            SpriteFor(NebulaProducedVisualCatalog.Environment(environment));

        public static Sprite Resource(ResourceFamily family) => NebulaIconResolver.Resource(family);
        public static Sprite Rarity(Rarity rarity) => NebulaIconResolver.Rarity(rarity);
        public static Sprite Item(string itemId) => NebulaIconResolver.Item(itemId);
        public static Sprite Attention() => NebulaProducedVisualResolver.AttentionIcon();
        public static Sprite RaidWarning() => NebulaProducedVisualResolver.RaidWarningIcon();

        // ------------------------------------------------------------------ prefab thumbnails

        /// <summary>
        /// A preview of the real planet prefab for an archetype. Rendered once and
        /// cached; null when the prefab is genuinely unavailable, which callers must
        /// treat as "draw the neutral fallback", never as an error.
        /// </summary>
        public static Sprite PlanetThumbnail(PlanetArchetype archetype) =>
            NebulaVisualCatalog.TryGetPlanet(archetype, out NebulaVisualDefinition definition)
                ? Thumbnail(definition.VisualId, definition.ResourcePath)
                : null;

        /// <summary>A preview of the real POI prefab. Unknown/gated types never call this.</summary>
        public static Sprite PoiThumbnail(PoiType type)
        {
            NebulaVisualDefinition definition;
            if (type == PoiType.Protected) definition = NebulaVisualCatalog.ProtectedPirateBase;
            else if (!NebulaVisualCatalog.TryGetPoi(type, out definition)) return null;
            return Thumbnail(definition.VisualId, definition.ResourcePath);
        }

        /// <summary>A preview of the real structure/drill prefab behind a stable content id.</summary>
        public static Sprite StructureThumbnail(string visualContentId)
        {
            if (string.IsNullOrEmpty(visualContentId)) return null;
            foreach (NebulaVisualDefinition definition in NebulaVisualCatalog.All)
                if (definition.VisualId == visualContentId)
                    return Thumbnail(definition.VisualId, definition.ResourcePath);
            return null;
        }

        /// <summary>Renders a prefab into a cached sprite exactly once. Never spawns a gameplay entity.</summary>
        private static Sprite Thumbnail(string visualId, string resourcePath)
        {
            if (string.IsNullOrEmpty(visualId)) return null;
            if (ThumbnailCache.TryGetValue(visualId, out Sprite cached)) return cached;

            Sprite rendered = null;
            try { rendered = RenderPrefabPreview(resourcePath); }
            catch { rendered = null; } // a preview failure must never break the map
            ThumbnailCache[visualId] = rendered;
            return rendered;
        }

        /// <summary>
        /// True only where a real GPU render can actually happen. A headless/batchmode
        /// context (a CI test run, a dedicated server) has no graphics device, and calling
        /// Camera.Render() there crashes the native runtime outright — so previews are
        /// simply not produced and every caller falls back to its neutral silhouette.
        /// </summary>
        private static bool CanRenderPreviews =>
            SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        private static Sprite RenderPrefabPreview(string resourcePath)
        {
            if (!CanRenderPreviews) return null;
            GameObject prefab = NebulaVisualResolver.Load(resourcePath);
            if (prefab == null) return null;

            RenderTexture rt = RenderTexture.GetTemporary(ThumbnailSize, ThumbnailSize, 16);
            GameObject stage = null;
            GameObject cameraGo = null;
            GameObject lightGo = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                // A private, inert stage: no colliders, no gameplay components, destroyed
                // immediately after the single render.
                stage = Object.Instantiate(prefab);
                stage.name = "StrategicMapPreview (temporary)";
                stage.hideFlags = HideFlags.HideAndDontSave;
                StripInteractive(stage.transform);

                Bounds bounds = EncapsulateRenderers(stage.transform, out bool hasRenderer);
                if (!hasRenderer) return null;

                stage.transform.position = Vector3.zero;
                Vector3 offset = -bounds.center;
                foreach (Transform child in stage.transform) child.localPosition += offset;

                // Without a light the preview renders almost black, which is exactly how the
                // planet markers looked: dark squares with badges floating on them.
                lightGo = new GameObject("StrategicMapPreviewLight") { hideFlags = HideFlags.HideAndDontSave };
                var keyLight = lightGo.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                keyLight.intensity = 1.5f;
                keyLight.color = new Color(1f, 0.97f, 0.92f);
                lightGo.transform.rotation = Quaternion.Euler(28f, 214f, 0f);

                cameraGo = new GameObject("StrategicMapPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraGo.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.04f, 0.05f, 0.09f, 0f);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(0.001f, bounds.extents.magnitude);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = camera.orthographicSize * 8f;
                camera.transform.position = new Vector3(0f, 0f, -camera.orthographicSize * 3f);
                camera.transform.rotation = Quaternion.identity;
                camera.targetTexture = rt;
                camera.enabled = false;
                camera.Render();

                RenderTexture.active = rt;
                var texture = new Texture2D(ThumbnailSize, ThumbnailSize, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, ThumbnailSize, ThumbnailSize), 0, 0);
                texture.Apply();
                return Sprite.Create(texture, new Rect(0, 0, ThumbnailSize, ThumbnailSize), new Vector2(0.5f, 0.5f));
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
                if (cameraGo != null) SafeDestroy(cameraGo);
                if (lightGo != null) SafeDestroy(lightGo);
                if (stage != null) SafeDestroy(stage);
            }
        }

        /// <summary>A preview must never leave an active collider or gameplay behaviour behind.</summary>
        private static void StripInteractive(Transform root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) SafeDestroy(collider);
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true)) SafeDestroy(body);
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) SafeDestroy(behaviour);
        }

        private static Bounds EncapsulateRenderers(Transform root, out bool hasRenderer)
        {
            var bounds = new Bounds(root.position, Vector3.zero);
            hasRenderer = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!hasRenderer) { bounds = renderer.bounds; hasRenderer = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }

        // ------------------------------------------------------------------ sprites

        private static Sprite SpriteFor(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (SpriteCache.TryGetValue(resourcePath, out Sprite cached)) return cached;
            Sprite sprite = NebulaIconResolver.LoadSprite(resourcePath);
            SpriteCache[resourcePath] = sprite;
            return sprite;
        }

        /// <summary>Drops every cached sprite/thumbnail — used by session teardown so nothing survives a restart.</summary>
        public static void ClearCache()
        {
            SpriteCache.Clear();
            ThumbnailCache.Clear();
        }
    }
}
