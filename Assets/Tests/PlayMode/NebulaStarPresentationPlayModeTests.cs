using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Celestial;
using Nebula.Sectors;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// What the start system's star actually looks like, and how far its
    /// presentation reaches.
    /// <para>
    /// <see cref="SunGlow"/> builds four camera-facing additive quads whose sizes
    /// are multiples of the star's <b>diameter</b>. Its own comment claimed the
    /// outermost stayed inside the first orbit — but 3.6 diameters is 7.2 radii and
    /// the first orbit is guaranteed only 4.5, so the faint far glow reached past the
    /// innermost planet and, close up, filled the frame with a white curtain. The
    /// numbers and the intent had come apart, which is why this fixture measures the
    /// reach in radii rather than trusting the table.
    /// </para>
    /// <para>
    /// Both halves are asserted: the geometry (how far the effect extends, that the
    /// authored surface survives, that the light budget is untouched) and the
    /// picture (that the giant disk is gone from the real pixels). A renderer census
    /// alone would pass a halo that had been shrunk on paper and left bright enough
    /// to wash the screen anyway.
    /// </para>
    /// </summary>
    // ReleaseRegression: this fixture runs headless. The individual tests that need real
    // pixels carry [Category("Graphics")] of their own and must be excluded by that name --
    // NUnit propagates this class category to them, so Graphics is the authoritative one.
    [Category("ReleaseRegression")]
    public class NebulaStarPresentationPlayModeTests
    {
        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime", "Star");

        /// <summary>
        /// The reach ceiling, in star radii, measured at the widest the halo ever
        /// gets — its authored size times the full close-range swell.
        /// <para>
        /// 4.5 is the floor <c>StarSystemSettings</c> guarantees between a star and
        /// its first orbit, so a presentation that stays under 4 can never draw over
        /// the innermost planet at any distance.
        /// </para>
        /// </summary>
        private const float MaxHaloRadiiFromCentre = 4f;

        private UiFinalPlayMode.Globals globals;

        [SetUp]
        public void CaptureTheWorldAsItWasFound() => globals = UiFinalPlayMode.CaptureGlobals();

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();
            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
        }

        // =====================================================================
        // Harness
        // =====================================================================

        private static NebulaContext Ctx()
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            Assert.IsNotNull(context, "there is no live session behind this test");
            return context;
        }

        /// <summary>The star of the system the campaign starts in.</summary>
        private static Star StartStar()
        {
            Star[] stars = UnityEngine.Object.FindObjectsOfType<Star>(true);
            Assert.IsNotEmpty(stars, "the session materialised no star at all");

            Vector3 shipAt = Ctx().Ship.transform.position;
            Star nearest = stars[0];
            float best = float.MaxValue;
            foreach (Star star in stars)
            {
                float distance = Vector3.SqrMagnitude(star.transform.position - shipAt);
                if (distance < best) { best = distance; nearest = star; }
            }
            return nearest;
        }

        /// <summary>Every halo quad the star's glow built, with its live world size.</summary>
        private static List<(string name, float worldRadius)> HaloReach(Star star)
        {
            var found = new List<(string, float)>();
            foreach (SunGlow glow in star.GetComponentsInChildren<SunGlow>(true))
                foreach (MeshRenderer renderer in glow.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Vector3 extents = renderer.bounds.extents;
                    found.Add((renderer.gameObject.name, Mathf.Max(extents.x, extents.y, extents.z)));
                }
            return found;
        }

        private static float StarRadius(Star star)
        {
            Transform surface = star.transform.Find("Surface");
            Assert.IsNotNull(surface, "the star has no authored surface child");
            return surface.localScale.x * 0.5f;
        }

        // =====================================================================
        // Geometry
        // =====================================================================

        /// <summary>
        /// No part of the star's presentation reaches further than
        /// <see cref="MaxHaloRadiiFromCentre"/> star radii, even at the widest the
        /// close-range swell can push it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheStarsPresentationStaysCloseToTheStar()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            Star star = StartStar();
            float radius = StarRadius(star);
            Assert.Greater(radius, 0f, "the star has no radius");

            // Two readings, because they can disagree. The authored ceiling is what
            // the table promises at its widest; the live bounds are what the scene
            // is actually drawing this frame, pulse and close-range swell included.
            Assert.LessOrEqual(SunGlow.MaxReachRadii, MaxHaloRadiiFromCentre,
                $"the authored halo table reaches {SunGlow.MaxReachRadii:0.00} radii at its widest");

            var offenders = new List<string>();
            float widest = 0f;
            foreach ((string name, float worldRadius) in HaloReach(star))
            {
                float radii = worldRadius / radius;
                widest = Mathf.Max(widest, radii);
                if (radii > MaxHaloRadiiFromCentre)
                    offenders.Add($"{name} reaches {radii:0.00} radii");
            }

            TestContext.WriteLine(
                $"star radius {radius:0} · halo layers {HaloReach(star).Count} · " +
                $"live widest {widest:0.00} radii · authored ceiling {SunGlow.MaxReachRadii:0.00} radii");

            Assert.IsEmpty(offenders,
                $"the star's presentation extends past {MaxHaloRadiiFromCentre} radii: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// The authored surface is still drawn. Removing the outer glow must not
        /// have removed the star.
        /// </summary>
        [UnityTest]
        public IEnumerator TheAuthoredSurfaceIsStillDrawn()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            Star star = StartStar();
            Transform surface = star.transform.Find("Surface");
            var renderer = surface.GetComponent<MeshRenderer>();

            Assert.IsNotNull(renderer, "the star surface lost its renderer");
            Assert.IsTrue(renderer.enabled, "the star surface is no longer drawn");
            Assert.IsTrue(surface.gameObject.activeInHierarchy, "the star surface was switched off");
            Assert.IsNotNull(renderer.sharedMaterial.mainTexture,
                "the star surface lost its authored texture");
        }

        /// <summary>
        /// A star still describes the light it would cast, and the director still
        /// realises exactly one real-time light on the system the player is in.
        /// The glow was never a light; changing it must not change the budget.
        /// </summary>
        [UnityTest]
        public IEnumerator TheKeyLightAndTheLightBudgetAreUnchanged()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            Star star = StartStar();
            Assert.Greater(star.KeyLightRange, 0f, "the star stopped describing its key light");
            Assert.Greater(star.KeyLightIntensityScale, 0f, "the star's class intensity went to zero");
            Assert.AreNotEqual(Color.clear, star.KeyLightColor, "the star's key light lost its colour");

            int realtime = 0;
            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>(true))
                if (light.isActiveAndEnabled && light.type != LightType.Directional
                    && light.lightmapBakeType == LightmapBakeType.Realtime) realtime++;

            TestContext.WriteLine($"realtime point/spot lights in the session: {realtime}");

            // The star's own glow contributes none of these, so the count is whatever
            // the lighting director decided and this change must not have moved it.
            Assert.LessOrEqual(realtime, LightingQualityProfile.FromQualitySettings().MaxRealtimeLights,
                "the session is over its own real-time light budget");
        }

        /// <summary>
        /// All four star classes get the same treatment and keep their own colour.
        /// The glow is built by one shared presenter, so a fix applied to it has to
        /// be checked across the classes rather than on the one star in view.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryStarClassKeepsItsColourAndItsReach()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            var seen = new List<string>();
            foreach (StarClass starClass in (StarClass[])System.Enum.GetValues(typeof(StarClass)))
            {
                var host = new GameObject("Class Probe " + starClass);
                try
                {
                    var star = host.AddComponent<Star>();
                    (Color low, Color high, Color glow) = SystemMaterializer.StarColorsFor(starClass);
                    star.InitializeStar(100f, SystemMaterializer.StarDensity, lightRange: 5000f,
                        Color.white, low, high, glow, starClass);

                    Assert.AreNotEqual(Color.clear, star.KeyLightColor, starClass + ": lost its key light colour");

                    float widest = 0f;
                    foreach ((string _, float worldRadius) in HaloReach(star))
                        widest = Mathf.Max(widest, worldRadius / 100f
                            * (1f + SunGlow.MaxPulse) * (1f + SunGlow.MaxProximitySwell));

                    seen.Add($"{starClass} reach {widest:0.00} radii, key light {star.KeyLightColor}");
                    Assert.LessOrEqual(widest, MaxHaloRadiiFromCentre,
                        starClass + ": its glow reaches further than the shared ceiling");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }

            TestContext.WriteLine(string.Join("\n", seen));
            Assert.AreEqual(4, seen.Count, "the star class set changed size");
        }

        /// <summary>
        /// Two hundred frames beside the star add no renderers and no effect objects.
        /// A per-frame presenter that quietly accumulates quads is exactly how a
        /// close-range glow becomes a curtain.
        /// </summary>
        [UnityTest]
        public IEnumerator TwoHundredFramesAddNoRenderers()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            Star star = StartStar();
            int renderersBefore = star.GetComponentsInChildren<MeshRenderer>(true).Length;
            int glowsBefore = star.GetComponentsInChildren<SunGlow>(true).Length;

            for (int i = 0; i < 200; i++) yield return null;

            Assert.AreEqual(renderersBefore, star.GetComponentsInChildren<MeshRenderer>(true).Length,
                "the star grew renderers while it was simply being looked at");
            Assert.AreEqual(glowsBefore, star.GetComponentsInChildren<SunGlow>(true).Length,
                "the star grew a second glow");
        }

        /// <summary>
        /// A Full → Warm → Full round trip leaves one star, one glow and no ghost
        /// halo behind. Streaming rebuilds the system, and a halo that survived the
        /// teardown would be drawn on top of the new one forever.
        /// </summary>
        [UnityTest]
        public IEnumerator AFullWarmFullRoundTripLeavesNoGhostHalo()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            int glowsBefore = UnityEngine.Object.FindObjectsOfType<SunGlow>(true).Length;
            Assert.Greater(glowsBefore, 0, "the session materialised no star glow at all");

            SystemStreamingDirector streaming = Ctx().Streaming;
            Assert.IsNotNull(streaming, "the session has no streaming director");

            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            boot.EndSession();
            yield return UiFinalPlayMode.EndSessionAndSettle();
            yield return UiFinalPlayMode.BeginSession();

            int glowsAfter = UnityEngine.Object.FindObjectsOfType<SunGlow>(true).Length;
            Assert.AreEqual(glowsBefore, glowsAfter,
                $"a session round trip changed the number of star glows ({glowsBefore} → {glowsAfter})");

            Star star = StartStar();
            Assert.AreEqual(1, star.GetComponentsInChildren<SunGlow>(true).Length,
                "the restarted star carries more than one glow");
        }

        // =====================================================================
        // Pixels
        // =====================================================================

        /// <summary>
        /// Photographs the star at a readable distance and at normal flight
        /// distance, at both supported widths, and measures how much of the frame
        /// the effect actually covers.
        /// <para>
        /// A renderer census cannot see a white curtain. This can: it counts the
        /// pixels bright enough to be the glow rather than the sky, and states the
        /// fraction, so the before and the after are the same measurement rather
        /// than two impressions.
        /// </para>
        /// </summary>
        [UnityTest]
        // Graphics: measures how much of the frame the star covers, pixel by pixel; on the -nographics null device that
        // path segfaults the Editor rather than returning a blank frame.
        [Category("Graphics")]
        public IEnumerator PhotographTheStarAndMeasureHowMuchOfTheFrameItFills()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Fail("this capture needs a graphics device; run it with --graphics");

            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return null;

            Star star = StartStar();
            float radius = StarRadius(star);

            var report = new List<string>();
            report.Add(Shot(star, radius, 6f, 1920, 1080, "star-near-1920x1080.png"));
            report.Add(Shot(star, radius, 6f, 3440, 1440, "star-near-3440x1440.png"));
            report.Add(Shot(star, radius, 40f, 1920, 1080, "star-normal-1920x1080.png"));
            report.Add(Shot(star, radius, 40f, 3440, 1440, "star-normal-3440x1440.png"));

            TestContext.WriteLine(string.Join("\n", report));

            Directory.CreateDirectory(OutputDir);
            File.WriteAllText(Path.Combine(OutputDir, "star-coverage.txt"),
                string.Join("\n", report) + "\n");
        }

        /// <summary>
        /// Renders the world from a camera parked at a given multiple of the star's
        /// radius, writes the PNG, and returns the bright-pixel coverage.
        /// </summary>
        private static string Shot(Star star, float radius, float distanceInRadii,
            int width, int height, string fileName)
        {
            Directory.CreateDirectory(OutputDir);

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("StarCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            RenderTexture previousActive = RenderTexture.active;

            // The interface must not be in the frame: this is a picture of the world.
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
            var canvasWasOn = new bool[canvases.Length];

            try
            {
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvasWasOn[i] = canvases[i].enabled;
                    canvases[i].enabled = false;
                }

                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.orthographic = false;
                camera.fieldOfView = 60f;
                camera.nearClipPlane = radius * 0.05f;
                camera.farClipPlane = radius * 4000f;
                camera.targetTexture = target;

                Vector3 centre = star.transform.position;
                camera.transform.position = centre + new Vector3(0f, radius * 0.25f, -radius * distanceInRadii);
                camera.transform.LookAt(centre);

                camera.Render();

                RenderTexture.active = target;
                var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();

                Color32[] pixels = shot.GetPixels32();
                int bright = 0;
                bool varied = false;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    // Anything appreciably above the black sky is the star's own
                    // light: surface, corona or the glow that used to swamp both.
                    if (p.r + p.g + p.b > 150) bright++;
                    if (!varied && i > 0 &&
                        (p.r != pixels[0].r || p.g != pixels[0].g || p.b != pixels[0].b)) varied = true;
                }

                Assert.IsTrue(varied, fileName + " is a single flat colour — nothing rendered");

                File.WriteAllBytes(Path.Combine(OutputDir, fileName), shot.EncodeToPNG());
                float coverage = (float)bright / pixels.Length;
                UnityEngine.Object.DestroyImmediate(shot);

                return $"{fileName}: camera at {distanceInRadii:0}R — bright coverage {coverage * 100f:0.00}% of the frame";
            }
            finally
            {
                RenderTexture.active = previousActive;
                for (int i = 0; i < canvases.Length; i++)
                    if (canvases[i] != null) canvases[i].enabled = canvasWasOn[i];
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
