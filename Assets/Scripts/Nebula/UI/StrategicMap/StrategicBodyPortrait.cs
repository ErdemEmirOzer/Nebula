using UnityEngine;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The card's portrait: the body itself, rendered live into a
    /// <see cref="RenderTexture"/> rather than shown as a baked thumbnail.
    ///
    /// The card used to show <see cref="StrategicMapVisualResolver.PlanetThumbnail"/> — one
    /// 256px still, rendered once per archetype and reused for every world of that family.
    /// It was small, it never moved, and for a star it was the raw equirectangular surface
    /// map squeezed into a circle, which reads as a wallpaper swatch rather than as a sun.
    ///
    /// This draws the real thing instead: the same planet prefab the map's own bodies come
    /// from, turning slowly under its own key light, at portrait resolution; or, for a star,
    /// the same disk/corona/ray maths the map draws its stars with, sized to fill the frame.
    /// The body is always framed whole — the camera is fitted to its bounds, so nothing is
    /// ever cropped.
    ///
    /// Completely isolated: its own layer, its own camera, its own lights, and a stage that
    /// is switched off whenever no card is showing. It renders on demand, never every frame
    /// of the session, and it is a picture of a body — never a gameplay entity.
    /// </summary>
    public sealed class StrategicBodyPortrait : MonoBehaviour
    {
        /// <summary>
        /// The portrait's own layer. Deliberately not 30, which the map's universe and the
        /// progression screen's ship preview both already use.
        /// </summary>
        public const int PortraitLayer = 29;

        /// <summary>Card portraits are shown at up to 460pt on a 1440-tall reference; 768 keeps them crisp there and on a 4K panel.</summary>
        public const int Resolution = 768;

        private const string StarShaderPath = "StrategicMap/StrategicMapPortraitStar";

        /// <summary>How far the disk's radius is from the frame edge; matches the shader's own default.</summary>
        private const float StarQuadRadii = 3.4f;

        /// <summary>A turn every ~40 seconds: enough to read as alive, slow enough to read the surface.</summary>
        private const float PlanetSpinDegreesPerSecond = 9f;

        private Camera portraitCamera;
        private Transform stage;
        private Transform pivot;
        private GameObject body;
        private Material starMaterial;
        private RenderTexture target;
        private string builtKey;
        private float elapsed;
        private bool isStar;

        /// <summary>The live portrait texture, or null where no GPU can produce one.</summary>
        public RenderTexture Target => target;

        /// <summary>True while a body is actually staged — the card uses this to decide between the live portrait and its flat fallback.</summary>
        public bool HasBody => body != null && target != null;

        /// <summary>
        /// A real GPU render is only possible where there is a graphics device. A headless
        /// run (CI, a dedicated server) has none, and Camera.Render() there takes the native
        /// runtime down — so the portrait produces nothing and the card falls back to the
        /// flat sprite it has always had.
        /// </summary>
        private static bool CanRender =>
            SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        /// <summary>
        /// Built under the owning screen, exactly like the progression screen's ship preview:
        /// the stage and its camera move together, so a floating-origin rebase carries both
        /// and the framing never shifts.
        /// </summary>
        public static StrategicBodyPortrait Create(Transform parent)
        {
            var go = new GameObject("Body Portrait");
            go.transform.SetParent(parent, false);
            var portrait = go.AddComponent<StrategicBodyPortrait>();
            portrait.BuildStage();
            return portrait;
        }

        private void BuildStage()
        {
            var stageGo = new GameObject("Portrait Stage");
            stageGo.transform.SetParent(transform, false);
            stage = stageGo.transform;

            var pivotGo = new GameObject("Pivot") { layer = PortraitLayer };
            pivotGo.transform.SetParent(stage, false);
            pivot = pivotGo.transform;

            int portraitOnly = 1 << PortraitLayer;

            var cameraGo = new GameObject("Portrait Camera");
            cameraGo.transform.SetParent(stage, false);
            portraitCamera = cameraGo.AddComponent<Camera>();
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            // Opaque deep space rather than a transparent clear: the card frames the portrait
            // in a circular mask, so a body against space is exactly right, and an opaque
            // target keeps the additive corona out of premultiplied-alpha trouble.
            portraitCamera.backgroundColor = StrategicMapTheme.Space;
            portraitCamera.cullingMask = portraitOnly;
            portraitCamera.orthographic = true;
            portraitCamera.nearClipPlane = 0.01f;
            portraitCamera.farClipPlane = 200f;
            portraitCamera.allowHDR = false;
            portraitCamera.enabled = false; // driven from Update while a body is staged

            // A key light with a cool rim behind it, so a sphere reads as a sphere. Both are
            // confined to the portrait layer: nothing in the played scene ever sees them.
            var keyGo = new GameObject("Key Light");
            keyGo.transform.SetParent(stage, false);
            keyGo.transform.localRotation = Quaternion.Euler(26f, 212f, 0f);
            Light key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.9f;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.shadows = LightShadows.None;
            key.cullingMask = portraitOnly;

            var rimGo = new GameObject("Rim Light");
            rimGo.transform.SetParent(stage, false);
            rimGo.transform.localRotation = Quaternion.Euler(-14f, 28f, 0f);
            Light rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.55f;
            rim.color = StrategicMapTheme.Blue;
            rim.shadows = LightShadows.None;
            rim.cullingMask = portraitOnly;

            stageGo.SetActive(false);
        }

        private void EnsureTarget()
        {
            if (target != null || !CanRender) return;
            target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
            {
                name = "StrategicMapPortrait",
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
                useMipMap = false
            };
            target.Create();
            if (portraitCamera != null) portraitCamera.targetTexture = target;
        }

        // ------------------------------------------------------------------ staging

        /// <summary>
        /// Stages a planet: the real prefab for its archetype, from the same catalog seam the
        /// map's own bodies use, so a change to the art reaches the card for free.
        /// </summary>
        public void ShowPlanet(PlanetArchetype archetype)
        {
            string key = "planet:" + archetype;
            if (key == builtKey) { SetStaged(true); return; }
            if (!CanRender) { Clear(); return; }

            if (!NebulaVisualCatalog.TryGetPlanet(archetype, out NebulaVisualDefinition definition))
            { Clear(); return; }

            ClearBody();
            EnsureTarget();

            GameObject instance = NebulaVisualResolver.Instantiate(definition, pivot, runtimeDiameter: 1f,
                instanceName: "Portrait :: " + archetype);
            if (instance == null) { Clear(); return; }

            SetLayerRecursively(instance.transform, PortraitLayer);
            StripInteractive(instance);
            instance.transform.localPosition = Vector3.zero;

            body = instance;
            isStar = false;
            builtKey = key;
            FrameStagedBody();
            SetStaged(true);
        }

        /// <summary>Stages a star: the map's own star, drawn to fill the portrait instead of to a fixed pixel size.</summary>
        public void ShowStar(StarClass starClass)
        {
            string key = "star:" + starClass;
            if (key == builtKey) { SetStaged(true); return; }
            if (!CanRender) { Clear(); return; }

            Shader shader = Resources.Load<Shader>(StarShaderPath);
            if (shader == null) { Clear(); return; }

            ClearBody();
            EnsureTarget();

            if (starMaterial == null)
                starMaterial = new Material(shader) { name = "StrategicMapPortraitStar", hideFlags = HideFlags.HideAndDontSave };
            starMaterial.SetColor("_Star", StrategicMapTheme.StarColor(starClass));
            starMaterial.SetColor("_Highlight", StrategicMapTheme.SpaceText);
            starMaterial.SetColor("_Red", StrategicMapTheme.Red);
            starMaterial.SetFloat("_QuadRadii", StarQuadRadii);
            starMaterial.SetFloat("_DiskPixels", Resolution * 0.5f / StarQuadRadii);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Portrait :: " + starClass;
            quad.transform.SetParent(pivot, false);
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = Vector3.one * 2f; // the orthographic frame is 2 units tall
            StripInteractive(quad);
            SetLayerRecursively(quad.transform, PortraitLayer);
            quad.GetComponent<MeshRenderer>().sharedMaterial = starMaterial;

            body = quad;
            isStar = true;
            builtKey = key;

            // The star quad IS the frame: no bounds fitting, or the corona would be zoomed
            // into until only the disk was left.
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            portraitCamera.orthographicSize = 1f;
            portraitCamera.transform.localPosition = new Vector3(0f, 0f, -6f);
            portraitCamera.transform.localRotation = Quaternion.identity;

            SetStaged(true);
        }

        /// <summary>Takes the portrait down: nothing staged, nothing rendering.</summary>
        public void Clear()
        {
            ClearBody();
            builtKey = null;
            SetStaged(false);
        }

        private void SetStaged(bool staged)
        {
            if (stage != null && stage.gameObject.activeSelf != staged) stage.gameObject.SetActive(staged);
            if (portraitCamera != null) portraitCamera.enabled = false; // always rendered explicitly
        }

        private void ClearBody()
        {
            if (body != null) Destroy(body);
            body = null;
        }

        /// <summary>
        /// Fits the camera to whatever was staged, so the whole body is in frame with a little
        /// air around it. A world is never cropped by the portrait, whatever size its prefab
        /// happens to be authored at.
        /// </summary>
        private void FrameStagedBody()
        {
            if (body == null || portraitCamera == null) return;

            var bounds = new Bounds(pivot.position, Vector3.zero);
            bool any = false;
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) return;

            // Centre the body on the pivot it spins about, then frame it.
            body.transform.position += pivot.position - bounds.center;

            // Frame by what the camera actually SEES, not by the bounding sphere. The camera
            // looks down -Z orthographically, so the on-screen half-height is the larger of
            // the x/y extents. Using extents.magnitude instead (the box diagonal) is 1.73x too
            // large for a sphere, which is what left the planet floating small in a big frame.
            float onScreen = Mathf.Max(0.001f, Mathf.Max(bounds.extents.x, bounds.extents.y));
            float depth = Mathf.Max(0.001f, bounds.extents.magnitude);

            // A hairline of air, with a floor tied to the box diagonal so a body that is NOT a
            // sphere cannot clip its own corners as the portrait turns it. For a sphere the
            // floor works out at about 7% air, which is what the card wants.
            portraitCamera.orthographicSize = Mathf.Max(onScreen * 1.04f, depth * 0.62f);
            portraitCamera.transform.localPosition = new Vector3(0f, 0f, -depth * 8f);
            portraitCamera.transform.localRotation = Quaternion.identity;
            portraitCamera.farClipPlane = depth * 24f;
        }

        // ------------------------------------------------------------------ tick

        private void Update()
        {
            if (body == null || target == null) return;

            if (!StrategicMapTheme.ReducedMotion) elapsed += Time.unscaledDeltaTime;

            if (isStar)
            {
                if (starMaterial != null) starMaterial.SetFloat("_Time01", elapsed);
            }
            else
            {
                // A slow turn on the world's own axis. Composed fresh each frame from the
                // elapsed time rather than accumulated onto the transform, so a long session
                // can never drift the tilt into the spin.
                pivot.localRotation = Quaternion.Euler(0f, elapsed * PlanetSpinDegreesPerSecond, 0f);
            }

            RenderNow();
        }

        /// <summary>Draws one frame of the portrait. Public so a capture or a test can force it.</summary>
        public void RenderNow()
        {
            if (portraitCamera == null || target == null || body == null) return;
            portraitCamera.Render();
        }

        private static void StripInteractive(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Rigidbody rigid in root.GetComponentsInChildren<Rigidbody>(true)) Destroy(rigid);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }

        private void OnDestroy()
        {
            ClearBody();
            if (starMaterial != null) Destroy(starMaterial);
            if (target == null) return;
            if (portraitCamera != null) portraitCamera.targetTexture = null;
            target.Release();
            Destroy(target);
            target = null;
        }
    }
}
