using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Celestial;
using Nebula.Simulation;

namespace Nebula.Visuals
{
    /// <summary>
    /// Camera-based lens reflection. For every registered star it draws a core
    /// glare, an anamorphic streak and a chain of ghost reflections on a
    /// screen-space overlay canvas. Because every element lives in UI pixel
    /// space (not world space), the effect is completely immune to the
    /// floating-point jitter that world-space flare quads suffer from at large
    /// coordinates. Elements fade smoothly when the star leaves the screen or
    /// is occluded, and intensify as the camera approaches the star.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(200)]
    public class CameraLensFlare : MonoBehaviour
    {
        private class FlareSet
        {
            public Star star;
            public float starRadius;
            public RectTransform[] rects;
            public Image[] images;
            public Color[] baseColors;
            public float fade;
        }

        private struct PendingStar
        {
            public Star star;
            public Color tint;
            public float radius;
        }

        // axis: 1 = on the star, 0 = screen center, negative = mirrored ghost side.
        // size: fraction of the reference screen height.
        private static readonly (float axis, float size, float alpha, float whiteBlend, bool streak)[] ElementDefinitions =
        {
            (1.00f, 0.55f, 0.50f, 0.80f, true),   // anamorphic streak across the star
            (1.00f, 0.30f, 0.42f, 0.60f, false),  // core glare
            (0.50f, 0.085f, 0.14f, 0.30f, false), // ghost chain toward / past the center
            (0.25f, 0.045f, 0.11f, 0.40f, false),
            (-0.20f, 0.06f, 0.10f, 0.25f, false),
            (-0.50f, 0.12f, 0.09f, 0.30f, false),
            (-0.90f, 0.22f, 0.07f, 0.20f, false)
        };

        private static readonly List<PendingStar> pendingStars = new List<PendingStar>();
        private static Sprite glowSprite;
        private static Sprite streakSprite;
        private static Material uiAdditiveMaterial;

        private Camera targetCamera;
        private RectTransform canvasRect;

        /// <summary>
        /// The overlay canvas this component creates in <see cref="Awake"/>. Held so
        /// it can be destroyed again.
        /// <para>
        /// It is created parentless, so nothing else owns it and nothing else was
        /// removing it: ten session rebuilds left ten orphaned "Lens Flare Canvas"
        /// objects behind, which is exactly the canvas count a lifecycle run
        /// measured climbing 9 → 19 while every UI-owned counter stayed flat.
        /// </para>
        /// </summary>
        private GameObject flareCanvas;

        /// <summary>
        /// Takes the canvas down with the component that made it.
        /// <para>
        /// <c>Destroy</c> is deferred in play mode and refused outside it, so an
        /// EditMode fixture that builds and tears down a camera would keep every
        /// canvas it ever made without the immediate path.
        /// </para>
        /// </summary>
        private void OnDestroy()
        {
            if (flareCanvas == null) return;
            if (Application.isPlaying) Destroy(flareCanvas);
            else DestroyImmediate(flareCanvas);
            flareCanvas = null;
        }
        private readonly List<FlareSet> flares = new List<FlareSet>();

        /// <summary>Stars call this once at initialization; the camera picks them up automatically.</summary>
        public static void RegisterStar(Star star, Color tint, float radius)
        {
            pendingStars.Add(new PendingStar { star = star, tint = tint, radius = radius });
        }

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();

            var canvasGO = new GameObject("Lens Flare Canvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // below the HUD (20)
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasRect = canvas.GetComponent<RectTransform>();
            flareCanvas = canvasGO;

            // These three are process-lifetime statics, so what creates them must say
            // so. Without the suppression, MaterialLibrary.CreateAdditive registers
            // the material with whatever RuntimeResourceScope happens to be active at
            // the moment the first star registers — normally the promoting system's
            // FullResources — and that scope then destroys a material a static field
            // still points at. Every flare quad built afterwards is handed a destroyed
            // Material through `image.material` below, which is how a UI census that
            // counts distinct Graphic materials watched one of only two go away.
            //
            // Same statement, same reason, as AsteroidFieldGenerator.CreateRockMaterials
            // (AsteroidFieldGenerator.cs:142), which is the pattern this was missing.
            using (RuntimeResourceScope.SuppressOwnership())
            {
                if (glowSprite == null)
                {
                    Texture2D glowTex = MaterialLibrary.RadialGlowTexture;
                    glowSprite = Sprite.Create(glowTex, new Rect(0f, 0f, glowTex.width, glowTex.height), new Vector2(0.5f, 0.5f));
                    Texture2D streakTex = MaterialLibrary.StreakTexture;
                    streakSprite = Sprite.Create(streakTex, new Rect(0f, 0f, streakTex.width, streakTex.height), new Vector2(0.5f, 0.5f));
                }
                if (uiAdditiveMaterial == null)
                {
                    uiAdditiveMaterial = MaterialLibrary.CreateAdditive(Color.white);
                    uiAdditiveMaterial.mainTexture = null; // the CanvasRenderer supplies each sprite's texture
                }
            }
        }

        private void LateUpdate()
        {
            if (pendingStars.Count > 0)
            {
                foreach (PendingStar pending in pendingStars)
                {
                    if (pending.star != null) BuildFlareSet(pending);
                }
                pendingStars.Clear();
            }

            // A star that has left the universe has no reflection to cast. Sets whose
            // star is gone were previously kept forever: every promotion, demotion,
            // load and campaign restart materializes new stars, so the canvas
            // accumulated seven dead quads per vanished star — counted, updated every
            // frame, and invisible to anyone reading a canvas count, because they all
            // hang off the one Lens Flare Canvas rather than a root of their own.
            for (int i = flares.Count - 1; i >= 0; i--)
            {
                if (flares[i].star != null) continue;
                ReleaseFlareSet(flares[i]);
                flares.RemoveAt(i);
            }

            for (int i = 0; i < flares.Count; i++)
            {
                UpdateFlareSet(flares[i]);
            }
        }

        /// <summary>
        /// Destroys one set's quads. <c>Destroy</c> is deferred in play mode and
        /// refused outside it, so the immediate path is what makes this work in an
        /// EditMode fixture as well as in the running game.
        /// </summary>
        private static void ReleaseFlareSet(FlareSet set)
        {
            if (set?.rects == null) return;
            foreach (RectTransform rect in set.rects)
            {
                if (rect == null) continue;
                if (Application.isPlaying) Destroy(rect.gameObject);
                else DestroyImmediate(rect.gameObject);
            }
        }

        private void BuildFlareSet(PendingStar pending)
        {
            var set = new FlareSet
            {
                star = pending.star,
                starRadius = pending.radius,
                rects = new RectTransform[ElementDefinitions.Length],
                images = new Image[ElementDefinitions.Length],
                baseColors = new Color[ElementDefinitions.Length]
            };

            for (int i = 0; i < ElementDefinitions.Length; i++)
            {
                var definition = ElementDefinitions[i];

                var go = new GameObject(pending.star.name + " Flare " + i);
                go.transform.SetParent(canvasRect, false);

                var rect = go.AddComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                var image = go.AddComponent<Image>();
                image.sprite = definition.streak ? streakSprite : glowSprite;
                image.material = uiAdditiveMaterial;
                image.raycastTarget = false;
                image.enabled = false;

                Color color = Color.Lerp(pending.tint, Color.white, definition.whiteBlend);
                color.a = definition.alpha;

                set.rects[i] = rect;
                set.images[i] = image;
                set.baseColors[i] = color;
            }

            flares.Add(set);
        }

        private void UpdateFlareSet(FlareSet set)
        {
            float targetFade = 0f;
            float proximity = 0f;
            Vector2 starViewport = Vector2.zero;

            if (set.star != null)
            {
                targetFade = ComputeVisibility(set, out starViewport, out proximity);
            }

            set.fade = Mathf.MoveTowards(set.fade, targetFade, Time.deltaTime * 3.5f);
            bool visible = set.fade > 0.004f;

            for (int i = 0; i < set.images.Length; i++)
            {
                set.images[i].enabled = visible;
            }
            if (!visible) return;

            Vector2 canvasSize = canvasRect.rect.size;
            // Canvas coordinates relative to the screen center, so every ghost
            // position is just the star position scaled by its axis factor.
            Vector2 starCanvas = new Vector2(
                (starViewport.x - 0.5f) * canvasSize.x,
                (starViewport.y - 0.5f) * canvasSize.y);

            float sizeBoost = 0.8f + proximity * 0.8f;

            for (int i = 0; i < ElementDefinitions.Length; i++)
            {
                var definition = ElementDefinitions[i];

                set.rects[i].anchoredPosition = starCanvas * definition.axis;

                float pixels = definition.size * canvasSize.y * sizeBoost;
                set.rects[i].sizeDelta = definition.streak
                    ? new Vector2(pixels * 6f, pixels * 0.28f)
                    : new Vector2(pixels, pixels);

                Color color = set.baseColors[i];
                color.a *= set.fade;
                set.images[i].color = color;
            }
        }

        private float ComputeVisibility(FlareSet set, out Vector2 starViewport, out float proximity)
        {
            starViewport = Vector2.zero;
            proximity = 0f;

            Vector3 starPosition = set.star.transform.position;
            Vector3 cameraPos = targetCamera.transform.position;
            Vector3 toStar = starPosition - cameraPos;
            float distanceToStar = toStar.magnitude;

            // Cheapest test first: with 100+ stars in the scene, nearly all of
            // them are far beyond lens-reflection range every frame.
            float distanceFade = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(set.starRadius * 80f, set.starRadius * 350f, distanceToStar));
            if (distanceFade <= 0f) return 0f;

            Vector3 viewportPos = targetCamera.WorldToViewportPoint(starPosition);
            if (viewportPos.z <= 0f) return 0f;

            starViewport = new Vector2(viewportPos.x, viewportPos.y);
            if (viewportPos.x < -0.2f || viewportPos.x > 1.2f ||
                viewportPos.y < -0.2f || viewportPos.y > 1.2f) return 0f;

            // Anything solid between the camera and the star surface kills the
            // flare. Analytic sphere test against the body registry — planets
            // deliberately carry no colliders, so PhysX raycasts can't see them.
            if (IsOccluded(cameraPos, toStar / distanceToStar, distanceToStar - set.starRadius * 1.1f, set.star))
            {
                return 0f;
            }

            float centerFactor = Mathf.Clamp01(1.25f - Vector2.Distance(starViewport, new Vector2(0.5f, 0.5f)) * 1.2f);
            proximity = 1f - Mathf.Clamp01((distanceToStar - set.starRadius * 3f) / (set.starRadius * 60f));
            return centerFactor * (0.5f + proximity * 1.2f) * distanceFade;
        }

        private static bool IsOccluded(Vector3 origin, Vector3 direction, float rayLength, Star self)
        {
            if (rayLength <= 0f) return false;

            var bodies = GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBody body = bodies[i];
                if (body == null || body == self) continue;

                Vector3 toBody = body.transform.position - origin;
                float projection = Vector3.Dot(toBody, direction);
                if (projection < 0f || projection - body.Radius > rayLength) continue;

                float perpendicularSq = toBody.sqrMagnitude - projection * projection;
                if (perpendicularSq < body.Radius * body.Radius) return true;
            }
            return false;
        }
    }
}
