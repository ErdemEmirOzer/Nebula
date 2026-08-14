using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Celestial;
using Nebula.Sectors;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// LGT-02 in the real booted scene: celestial bodies answer the one key
    /// light through material response, and none of them adds a light of its
    /// own.
    /// </summary>
    public class Lgt02CelestialLightingPlayModeTests
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
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu's New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            UnityEngine.Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [TearDown]
        public void TearDown() => LightingAccessibility.ResetToDefaults();

        private static Light[] ActiveWorldLights()
            => UnityEngine.Object.FindObjectsOfType<Light>()
                .Where(l => l != null && l.gameObject.activeInHierarchy && l.cullingMask == -1)
                .ToArray();

        [UnityTest]
        public IEnumerator TheWholeSkyIsStillLitByExactlyOneGlobalKeyLight()
        {
            yield return BootScene();

            var director = UnityEngine.Object.FindObjectOfType<NebulaLightingDirector>();
            Assert.IsNotNull(director);

            Light[] worldLights = ActiveWorldLights();
            Assert.AreEqual(2, worldLights.Length,
                "key light + hull fill only: " + string.Join(", ", worldLights.Select(l => l.gameObject.name)));
            CollectionAssert.Contains(worldLights, director.KeyLight);
            Assert.IsTrue(director.HasKeyLight);
        }

        [UnityTest]
        public IEnumerator EveryStarClassResolvesToItsOwnKeyLightColourAndIntensity()
        {
            yield return BootScene();

            var starClasses = (StarClass[])Enum.GetValues(typeof(StarClass));
            var seenColors = starClasses.Select(c => CelestialLightingProfiles.ForStar(c).KeyLightColor).ToArray();
            Assert.AreEqual(4, seenColors.Distinct().Count(), "four classes, four key light colours");

            // Drive a real Star through each class and confirm the description
            // it hands the director actually differs.
            foreach (StarClass starClass in starClasses)
            {
                var go = new GameObject("Probe Star " + starClass);
                try
                {
                    var star = go.AddComponent<Star>();
                    star.InitializeStar(100f, 1.4f, lightRange: 1000f,
                        Color.white, Color.grey, Color.white, Color.white, starClass: starClass);

                    StarLightProfile expected = CelestialLightingProfiles.ForStar(starClass);
                    Assert.AreEqual(expected.KeyLightColor, star.KeyLightColor, $"{starClass} key colour");
                    Assert.AreEqual(expected.IntensityScale, star.KeyLightIntensityScale, 1e-4f, $"{starClass} intensity");
                    Assert.AreEqual(1000f * expected.RangeMultiplier, star.KeyLightRange, 1e-2f, $"{starClass} range");
                    Assert.AreEqual(0, star.GetComponentsInChildren<Light>(true).Length,
                        $"{starClass} must not create a real-time light of its own");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TheStartSystemsPlanetsAllCarryAConfiguredSurfacePresenter()
        {
            yield return BootScene();

            var presenters = UnityEngine.Object.FindObjectsOfType<CelestialSurfacePresenter>();
            Assert.Greater(presenters.Length, 0, "the Full system's planets must be presented");

            foreach (CelestialSurfacePresenter presenter in presenters)
            {
                Renderer[] renderers = presenter.GetComponentsInChildren<Renderer>(true);
                Assert.Greater(renderers.Length, 0, $"{presenter.name} has nothing to light");
                Assert.IsTrue(renderers.Any(r => r.HasPropertyBlock()),
                    $"{presenter.name} must write its response through a property block");
                Assert.LessOrEqual(presenter.AppliedResponse.Emission.maxColorComponent,
                    CelestialLightingProfiles.MaxSafeEmissionLuminance + 1e-4f,
                    $"{presenter.name} ({presenter.Archetype}) clips toward white");
                Assert.AreEqual(0, presenter.GetComponentsInChildren<Light>(true).Length,
                    $"{presenter.name} must not add a real-time light");
            }
        }

        [UnityTest]
        public IEnumerator NoStarBlackHoleOrAsteroidAddsARealtimeLight()
        {
            yield return BootScene();

            foreach (Star star in UnityEngine.Object.FindObjectsOfType<Star>())
                Assert.AreEqual(0, star.GetComponentsInChildren<Light>(true).Length,
                    $"{star.name} created its own light — Warm and decor stars stay emissive-only");

            foreach (BlackHole hole in UnityEngine.Object.FindObjectsOfType<BlackHole>())
                Assert.AreEqual(0, hole.GetComponentsInChildren<Light>(true).Length,
                    $"{hole.name} must read through emissive material, not a light");

            Asteroid[] asteroids = UnityEngine.Object.FindObjectsOfType<Asteroid>();
            Assert.Greater(asteroids.Length, 0, "the belt must exist for this to mean anything");
            foreach (Asteroid asteroid in asteroids)
                Assert.AreEqual(0, asteroid.GetComponentsInChildren<Light>(true).Length,
                    $"{asteroid.name} must answer the key light, never carry one");
        }

        [UnityTest]
        public IEnumerator SwitchingQualityRepresentsEveryBodyWithoutGrowingTheScene()
        {
            yield return BootScene();

            var director = UnityEngine.Object.FindObjectOfType<NebulaLightingDirector>();
            Assert.IsNotNull(director);

            int renderersBefore = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            int lightsBefore = ActiveWorldLights().Length;
            int presentersBefore = CelestialSurfacePresenter.LiveCount;
            Assert.Greater(presentersBefore, 0);

            director.SetQuality(LightingQuality.High);
            yield return null;

            foreach (CelestialSurfacePresenter presenter in UnityEngine.Object.FindObjectsOfType<CelestialSurfacePresenter>())
                Assert.AreEqual(LightingQuality.High, presenter.Quality,
                    $"{presenter.name} did not receive the quality change");

            director.SetQuality(LightingQuality.Low);
            yield return null;

            Assert.AreEqual(renderersBefore, UnityEngine.Object.FindObjectsOfType<Renderer>().Length,
                "a quality switch must not add a renderer");
            // Low deliberately drops the hull fill (LGT-03), so the count may
            // legitimately fall. The claim is that a switch never *adds* one.
            Assert.LessOrEqual(ActiveWorldLights().Length, lightsBefore,
                "a quality switch must not add a light");
            Assert.AreEqual(presentersBefore, CelestialSurfacePresenter.LiveCount,
                "a quality switch must not duplicate a presenter");
        }

        [UnityTest]
        public IEnumerator AfterFullToWarmToFullThePlanetsComeBackWithTheSameResponse()
        {
            yield return BootScene();

            var streaming = UnityEngine.Object.FindObjectOfType<SystemStreamingDirector>();
            var bootstrap = UnityEngine.Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>();
            Assert.IsNotNull(streaming);
            Assert.IsNotNull(bootstrap);

            var before = UnityEngine.Object.FindObjectsOfType<CelestialSurfacePresenter>()
                .OrderBy(p => p.name)
                .Select(p => (p.name, p.Archetype, p.Environment, p.AppliedResponse.Smoothness, p.AppliedResponse.Metallic))
                .ToArray();
            Assert.Greater(before.Length, 0);

            // ResetAndBootFull tears the system down and re-materializes it —
            // the same round trip a Full -> Warm -> Full flight produces.
            SystemManifest manifest = Nebula.World.StarSystemRegistry.Manifests.ResolveSystem(streaming.FullSystemId);
            Assert.IsNotNull(manifest, "the Full system must be registered");
            streaming.ResetAndBootFull(manifest);
            yield return null;
            yield return null;

            var after = UnityEngine.Object.FindObjectsOfType<CelestialSurfacePresenter>()
                .OrderBy(p => p.name)
                .Select(p => (p.name, p.Archetype, p.Environment, p.AppliedResponse.Smoothness, p.AppliedResponse.Metallic))
                .ToArray();

            CollectionAssert.AreEqual(before, after,
                "a re-materialized system must come back looking exactly as it did");
        }
    }
}
