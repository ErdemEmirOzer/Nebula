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
using Nebula.Hangar;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// LGT-03 in the real booted scene: one hull fill on the ship, zero lights
    /// on any vehicle, and emission that actually renders.
    /// </summary>
    public class Lgt03ShipVehicleLightingPlayModeTests
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
        public IEnumerator ThePlayerShipHasExactlyOneLightingPresenterAndOneHullFill()
        {
            yield return BootScene();

            var presenters = UnityEngine.Object.FindObjectsOfType<ShipLightingPresenter>();
            Assert.AreEqual(1, presenters.Length, "the ship has one lighting authority");

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);
            Light[] shipLights = ship.GetComponentsInChildren<Light>(true);
            Assert.AreEqual(1, shipLights.Length,
                "one hull fill for the whole ship, whatever is bolted to it: "
                + string.Join(", ", shipLights.Select(l => l.name)));
            Assert.AreSame(presenters[0].HullFill, shipLights[0]);
        }

        [UnityTest]
        public IEnumerator TheWholeSceneStillRunsOnOneKeyLightPlusOneHullFill()
        {
            yield return BootScene();

            Light[] worldLights = ActiveWorldLights();
            Assert.AreEqual(2, worldLights.Length,
                "unexpected real-time lights: " + string.Join(", ", worldLights.Select(l => l.gameObject.name)));
        }

        [UnityTest]
        public IEnumerator LowDropsTheHullFillAndMediumAndHighKeepExactlyOne()
        {
            yield return BootScene();

            var director = UnityEngine.Object.FindObjectOfType<NebulaLightingDirector>();
            var presenter = UnityEngine.Object.FindObjectOfType<ShipLightingPresenter>();
            Assert.IsNotNull(director);
            Assert.IsNotNull(presenter);

            director.SetQuality(LightingQuality.Low);
            yield return null;
            Assert.IsFalse(presenter.HullFill.gameObject.activeSelf,
                "Low reads the hull by key light and emission alone");
            Assert.AreEqual(1, ActiveWorldLights().Length, "only the key light remains on Low");

            foreach (LightingQuality quality in new[] { LightingQuality.Medium, LightingQuality.High })
            {
                director.SetQuality(quality);
                yield return null;
                Assert.IsTrue(presenter.HullFill.gameObject.activeSelf, $"{quality} keeps the hull fill");
                Assert.AreEqual(2, ActiveWorldLights().Length, $"{quality} must add no further light");
            }
        }

        [UnityTest]
        public IEnumerator SwitchingQualityGrowsNoLightMaterialOrGameObjectCount()
        {
            yield return BootScene();

            var director = UnityEngine.Object.FindObjectOfType<NebulaLightingDirector>();
            Assert.IsNotNull(director);

            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int renderersBefore = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            int variantsBefore = MaterialVariantCache.VariantCount;

            foreach (LightingQuality quality in new[]
                     { LightingQuality.Low, LightingQuality.High, LightingQuality.Medium })
            {
                director.SetQuality(quality);
                yield return null;
            }

            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "a quality switch must not create a light");
            Assert.AreEqual(renderersBefore, UnityEngine.Object.FindObjectsOfType<Renderer>().Length,
                "a quality switch must not create a renderer");
            Assert.AreEqual(variantsBefore, MaterialVariantCache.VariantCount,
                "a quality switch must not create a material variant");
        }

        [UnityTest]
        public IEnumerator EveryVehicleClassSpawnsWithAPresenterAndZeroRealtimeLights()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);

            int lightsBeforeSpawn = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;

            foreach (VehicleClass vehicleClass in (VehicleClass[])Enum.GetValues(typeof(VehicleClass)))
            {
                VehicleDef def = VehicleCatalog.Get(vehicleClass);
                Assert.IsNotNull(def, $"{vehicleClass} must exist in the catalog");

                VehicleAgent agent = VehicleAgent.Create(ship, $"lgt03-probe-{vehicleClass}", def);
                try
                {
                    var presenter = agent.GetComponent<VehicleLightingPresenter>();
                    Assert.IsNotNull(presenter, $"{vehicleClass} spawned without a lighting presenter");
                    Assert.AreEqual(vehicleClass, presenter.VehicleClass);

                    Assert.AreEqual(0, agent.GetComponentsInChildren<Light>(true).Length,
                        $"{vehicleClass} must carry no real-time light");

                    Color expected = ShipLightingProfiles.VehicleClassColor(vehicleClass);
                    Assert.Greater(Vector3.Dot(
                        new Vector3(presenter.AppliedEmission.r, presenter.AppliedEmission.g, presenter.AppliedEmission.b).normalized,
                        new Vector3(expected.r, expected.g, expected.b).normalized), 0.95f,
                        $"{vehicleClass} emission must keep its class hue");
                }
                finally
                {
                    VehicleAgent.Registry.Remove(agent.VehicleId);
                    UnityEngine.Object.DestroyImmediate(agent.gameObject);
                }
                yield return null;
            }

            Assert.AreEqual(lightsBeforeSpawn, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "spawning four vehicles must leave the scene's light count untouched");
        }

        [UnityTest]
        public IEnumerator ShipStateChangesOnlyMoveLightValues_NotGameplay()
        {
            yield return BootScene();

            var presenter = UnityEngine.Object.FindObjectOfType<ShipLightingPresenter>();
            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(presenter);
            Assert.IsNotNull(ship);

            float throttleBefore = ship.GetThrottleFraction();
            Vector3 positionBefore = ship.transform.position;
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;

            ship.SetThrottleFraction(0.8f);
            presenter.Tick();
            yield return null;

            Assert.AreEqual(ShipLightingState.Throttle, presenter.CurrentState,
                "throttle must read as the throttle lighting state");
            Assert.AreEqual(0.8f, ship.GetThrottleFraction(), 1e-3f,
                "the presenter must not have written back to the throttle");
            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "a state change must not add a light");

            ship.SetThrottleFraction(0f);
            presenter.Tick();
            yield return null;
            Assert.AreEqual(ShipLightingState.Idle, presenter.CurrentState);

            ship.SetThrottleFraction(throttleBefore);
            Assert.AreEqual(positionBefore, ship.transform.position,
                "lighting must never move the ship");
        }

        [UnityTest]
        public IEnumerator EmissiveVariantsAreSharedNotClonedPerRenderer()
        {
            yield return BootScene();

            int variants = MaterialVariantCache.VariantCount;
            int renderers = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;

            Assert.Less(variants, renderers,
                $"{variants} variants for {renderers} renderers — variants must track source materials, not renderers");

            // Every renderer driven for emission must be able to show it.
            var celestial = UnityEngine.Object.FindObjectsOfType<CelestialSurfacePresenter>()
                .Where(p => p.AppliedResponse.Emission.maxColorComponent > 0f)
                .ToArray();

            foreach (CelestialSurfacePresenter presenter in celestial)
                foreach (Renderer renderer in presenter.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.sharedMaterial == null) continue;
                    if (!renderer.sharedMaterial.HasProperty("_EmissionColor")) continue;
                    Assert.IsTrue(renderer.sharedMaterial.IsKeywordEnabled("_EMISSION"),
                        $"{presenter.name} ({presenter.Archetype}) writes emission that would never render");
                }
        }

        [UnityTest]
        public IEnumerator RestartLeavesNoDuplicatePresenterFillOrVariantPileUp()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<ShipLightingPresenter>().Length,
                "the previous session's ship presenter must be disposed with it");

            Light[] worldLights = ActiveWorldLights();
            Assert.AreEqual(1, worldLights.Count(l => l.name == "Hull Light"),
                "a restart must not accumulate hull fills: "
                + string.Join(", ", worldLights.Select(l => l.gameObject.name)));
            Assert.AreEqual(2, worldLights.Length, "still one key light plus one hull fill after a restart");
        }
    }
}
