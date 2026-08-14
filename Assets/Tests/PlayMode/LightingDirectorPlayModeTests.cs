using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Sectors;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// LGT-01 in the real booted scene: one director, one global rig, and no
    /// light left behind by a restart or a system change.
    /// </summary>
    public class LightingDirectorPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        private IEnumerator BootScene()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null; // let every bootstrapped Awake()/Start() run

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu's New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [TearDown]
        public void TearDown() => LightingAccessibility.ResetToDefaults();

        /// <summary>
        /// Every real-time light actually lighting the world. Preview-stage
        /// lights (hangar, progression portrait) are excluded because they are
        /// culled to their own layer and never touch the live scene.
        /// </summary>
        private static Light[] ActiveWorldLights()
            => Object.FindObjectsOfType<Light>()
                .Where(l => l != null && l.gameObject.activeInHierarchy && l.cullingMask == -1)
                .ToArray();

        [UnityTest]
        public IEnumerator FirstBoot_CreatesExactlyOneLightingDirector()
        {
            yield return BootScene();

            NebulaLightingDirector[] directors = Object.FindObjectsOfType<NebulaLightingDirector>();
            Assert.AreEqual(1, directors.Length, "a session has exactly one lighting authority");

            var bootstrap = Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap?.Context, "the session context must exist after boot");
            Assert.AreSame(directors[0], bootstrap.Context.LightingDirector,
                "the director must be reachable through the typed context, not by searching the scene");
        }

        [UnityTest]
        public IEnumerator RestartingTheCampaign_LeavesOneDirectorAndNoOrphanedLights()
        {
            yield return BootScene();

            var bootstrap = Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            NebulaLightingDirector[] directors = Object.FindObjectsOfType<NebulaLightingDirector>()
                .Where(d => d != null).ToArray();
            Assert.AreEqual(1, directors.Length, "the previous session's director must be disposed with it");

            Light[] worldLights = ActiveWorldLights();
            Assert.AreEqual(1, worldLights.Count(l => l == directors[0].KeyLight),
                "the surviving session must be lit by its own director's key light");
            Assert.IsEmpty(worldLights.Where(l => l.name == "System Key Light" && l != directors[0].KeyLight),
                "a disposed session must not leave its key light burning");
            Assert.AreEqual(1, worldLights.Count(l => l.name == "Hull Light"),
                "one ship, one hull fill — a restart must not accumulate them: " +
                string.Join(", ", worldLights.Select(l => l.gameObject.name)));
        }

        [UnityTest]
        public IEnumerator TheBootedSessionIsLitByExactlyOneRealtimeWorldLight()
        {
            yield return BootScene();

            var director = Object.FindObjectOfType<NebulaLightingDirector>();
            Assert.IsNotNull(director);

            Light[] worldLights = ActiveWorldLights();

            // The whole sky costs one key light plus the ship's own hull fill.
            // Nothing else — no star, no black hole, no decor — is allowed to
            // add a real-time light to the scene.
            Assert.AreEqual(2, worldLights.Length,
                "unexpected real-time world lights: " + string.Join(", ", worldLights.Select(l => l.gameObject.name)));
            CollectionAssert.Contains(worldLights, director.KeyLight);
            Assert.AreEqual(1, worldLights.Count(l => l.name == "Hull Light"));
            Assert.IsTrue(director.HasKeyLight, "the start system's star must own the key light on the first frame");
            Assert.LessOrEqual(worldLights.Length, director.Profile.MaxRealtimeLights,
                "the lit scene must sit inside the active profile's real-time budget");
        }

        [UnityTest]
        public IEnumerator NoStarInTheSkyCreatesItsOwnRealtimeLight()
        {
            yield return BootScene();

            Celestial.Star[] stars = Object.FindObjectsOfType<Celestial.Star>();
            Assert.Greater(stars.Length, 0, "the galaxy must actually have stars for this to mean anything");

            foreach (Celestial.Star star in stars)
                Assert.AreEqual(0, star.GetComponentsInChildren<Light>(true).Length,
                    $"{star.name} produced its own real-time light; Warm and decor stars must read as emissive only");
        }

        [UnityTest]
        public IEnumerator TheKeyLightSitsOnTheFullSystemsStar_AndFollowsItThroughAnOriginShift()
        {
            yield return BootScene();

            var director = Object.FindObjectOfType<NebulaLightingDirector>();
            var streaming = Object.FindObjectOfType<SystemStreamingDirector>();
            Assert.IsNotNull(director);
            Assert.IsNotNull(streaming);

            Transform star = streaming.TryGetStarTransform(streaming.FullSystemId);
            Assert.IsNotNull(star, "the start system must be Full after boot");
            Assert.AreEqual(streaming.FullSystemId, director.LitSystemId);
            Assert.AreEqual(star.position, director.KeyLight.transform.position);

            // Let real frames pass so any drift between the light and its star shows up.
            for (int i = 0; i < 5; i++) yield return null;

            Assert.AreEqual(star.position, director.KeyLight.transform.position,
                "the key light must track its star exactly, whatever the floating origin did in between");
        }

        [UnityTest]
        public IEnumerator SwitchingBetweenAllThreeQualityProfilesNeverThrowsAndNeverAddsLights()
        {
            yield return BootScene();

            var director = Object.FindObjectOfType<NebulaLightingDirector>();
            Assert.IsNotNull(director);
            int lightCountBefore = director.GetComponentsInChildren<Light>(true).Length;

            foreach (LightingQuality quality in new[]
                     { LightingQuality.Low, LightingQuality.Medium, LightingQuality.High, LightingQuality.Low })
            {
                Assert.DoesNotThrow(() => director.SetQuality(quality), $"switching to {quality} threw");
                yield return null;

                Assert.AreEqual(quality, director.Profile.Quality);
                Assert.AreEqual(lightCountBefore, director.GetComponentsInChildren<Light>(true).Length,
                    $"switching to {quality} allocated or destroyed a light");
            }
        }

        [UnityTest]
        public IEnumerator BootingUnderLightingDidNotBreakTheStarfieldOrTheCamera()
        {
            yield return BootScene();

            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "the player camera must survive lighting composition");
            // SkyboxBaker destroys the starfield backdrop once it has been
            // baked into a cubemap, so the surviving evidence of the star field
            // is the skybox itself, not the generator.
            Assert.IsNotNull(RenderSettings.skybox,
                "the baked starfield skybox must not be cleared by the lighting environment");
            Assert.AreEqual(CameraClearFlags.Skybox, camera.clearFlags,
                "the camera must still be clearing to the baked star field");
            Assert.IsFalse(RenderSettings.fog, "fog is a fill light by another name and stays off");
        }
    }
}
