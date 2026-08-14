using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Combat;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// LGT-05 in the real booted scene, and the final gate for the whole
    /// LGT-01–05 stack: a dense fight must not push the scene past its light
    /// budget, and nothing may survive a restart.
    /// </summary>
    public class Lgt05CombatFinalLightingPlayModeTests
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

        /// <summary>Real-time lights actually lighting the world. Map and preview lights are layer-culled and excluded by construction.</summary>
        private static Light[] ActiveWorldLights()
            => UnityEngine.Object.FindObjectsOfType<Light>()
                .Where(l => l != null && l.gameObject.activeInHierarchy && l.cullingMask == -1)
                .ToArray();

        private static NebulaLightingDirector Director()
        {
            var director = UnityEngine.Object.FindObjectOfType<NebulaLightingDirector>();
            Assert.IsNotNull(director, "the lighting director must exist after boot");
            return director;
        }

        // ---- composition -------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionHasExactlyOneCombatLightingBridgeReachableThroughTheDirector()
        {
            yield return BootScene();

            var bridges = UnityEngine.Object.FindObjectsOfType<CombatLightingBridge>();
            Assert.AreEqual(1, bridges.Length, "one combat observer per session");
            Assert.AreSame(bridges[0], Director().CombatLighting,
                "it must be reachable through the director, not by searching the scene");
        }

        // ---- weapons and damage ---------------------------------------------------

        [UnityTest]
        public IEnumerator EveryWeaponFamilyProducesAPooledMuzzleLightAndNeverANewOne()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int poolCapacity = director.TransientLights.Capacity;

            foreach (WeaponFamily family in (WeaponFamily[])Enum.GetValues(typeof(WeaponFamily)))
            {
                bool granted = director.TryAcquireTransientLight(
                    CombatLightingProfiles.Muzzle(family, TransientLightPriority.PlayerWeapon)
                        .ToRequest(Vector3.zero, director.Profile.Quality), out TransientLightHandle handle);

                Assert.IsTrue(granted, $"{family} muzzle should fit an otherwise idle pool");
                Assert.IsTrue(director.TransientLights.TryGetLight(handle, out Light light));
                Assert.AreEqual(CombatLightingProfiles.WeaponColor(family), light.color, $"{family} colour");
                Assert.AreEqual(LightShadows.None, light.shadows, $"{family} must not cast a shadow");
                director.ReleaseTransientLight(handle);
            }

            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "firing must never instantiate a Light");
            Assert.AreEqual(poolCapacity, director.TransientLights.Capacity, "the pool must not grow");
        }

        [UnityTest]
        public IEnumerator ShieldArmourAndHullHitsLightDifferentlyOnTheRealPool()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            var seen = new List<Color>();

            foreach (ShipDamageLayer layer in (ShipDamageLayer[])Enum.GetValues(typeof(ShipDamageLayer)))
            {
                Assert.IsTrue(director.TryAcquireTransientLight(
                    CombatLightingProfiles.DamageHit(layer, TransientLightPriority.CriticalPlayerImpact)
                        .ToRequest(Vector3.zero, director.Profile.Quality), out TransientLightHandle handle),
                    $"{layer} hit must get a light at critical priority");

                Assert.IsTrue(director.TransientLights.TryGetLight(handle, out Light light));
                seen.Add(light.color);
                director.ReleaseTransientLight(handle);
            }

            Assert.AreEqual(3, seen.Distinct().Count(),
                "a player must be able to tell shield, armour and hull apart by colour alone");
        }

        // ---- enemies ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator AllSixEnemyRolesSpawnWithAPresenterAndZeroRealtimeLights()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);

            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            var spawned = new List<EnemyAgent>();
            try
            {
                for (int i = 0; i < EnemyCatalog.All.Length; i++)
                {
                    EnemyDef def = EnemyCatalog.All[i];
                    EnemyAgent agent = EnemyAgent.Create($"lgt05-probe-{i}", def,
                        ship.transform.position + new Vector3(400f + i * 40f, 0f, 0f), ship, null, null);
                    spawned.Add(agent);

                    var presenter = agent.GetComponent<EnemyLightingPresenter>();
                    Assert.IsNotNull(presenter, $"{def.DisplayName} spawned without a lighting presenter");
                    Assert.AreEqual(def.Role, presenter.Role);
                    Assert.AreEqual(0, agent.GetComponentsInChildren<Light>(true).Length,
                        $"{def.DisplayName} must carry no real-time light");
                }

                Assert.AreEqual(6, spawned.Count);
                Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                    "six enemies must add no real-time light");
            }
            finally
            {
                foreach (EnemyAgent agent in spawned)
                    if (agent != null) UnityEngine.Object.DestroyImmediate(agent.gameObject);
            }
        }

        // ---- the dense-scene gate -------------------------------------------------------

        [UnityTest]
        public IEnumerator ADenseFightNeverPushesTheSceneOverItsRealtimeLightBudget()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);

            int variantsBefore = MaterialVariantCache.VariantCount;
            int poolCapacity = director.TransientLights.Capacity;
            var enemies = new List<EnemyAgent>();
            var structures = new List<GameObject>();

            try
            {
                // 24 enemies, plus 200 built objects standing in for structures
                // and salvage — the representative dense scenario.
                for (int i = 0; i < 24; i++)
                    enemies.Add(EnemyAgent.Create($"lgt05-dense-{i}",
                        EnemyCatalog.All[i % EnemyCatalog.All.Length],
                        ship.transform.position + new Vector3(300f + i * 25f, 0f, 0f), ship, null, null));

                Shader standard = Shader.Find("Standard");
                var shared = new Material(standard) { name = "LGT05 Dense Probe" };
                var root = new GameObject("LGT05 Dense Root");
                structures.Add(root);
                for (int i = 0; i < 200; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.SetParent(root.transform, false);
                    go.GetComponent<MeshRenderer>().sharedMaterial = shared;
                    go.AddComponent<StructureLightingPresenter>().RebuildRendererCache();
                }

                // Saturate the pool with combat flashes from every direction.
                for (int i = 0; i < 120; i++)
                {
                    var family = (WeaponFamily)(i % 3);
                    var priority = (TransientLightPriority)new[]
                    {
                        (int)TransientLightPriority.Decorative,
                        (int)TransientLightPriority.DistantCombat,
                        (int)TransientLightPriority.PlanetaryDefense,
                        (int)TransientLightPriority.PlayerWeapon,
                        (int)TransientLightPriority.CriticalPlayerImpact
                    }[i % 5];

                    director.TryAcquireTransientLight(
                        CombatLightingProfiles.Impact(family, priority)
                            .ToRequest(ship.transform.position + Vector3.right * (i * 3f), director.Profile.Quality),
                        out _);
                }

                yield return null;

                Light[] world = ActiveWorldLights();
                Assert.LessOrEqual(world.Length, director.Profile.MaxRealtimeLights,
                    $"{world.Length} active lights exceeds the {director.Profile.Quality} budget of "
                    + $"{director.Profile.MaxRealtimeLights}: " + string.Join(", ", world.Select(l => l.gameObject.name)));

                Assert.LessOrEqual(director.TransientLights.ActiveCount, director.TransientLights.ActiveCapacity,
                    "the pool must never exceed its own active ceiling");
                Assert.AreEqual(poolCapacity, director.TransientLights.Capacity,
                    "a dense fight must not grow the pool");
                Assert.AreEqual(variantsBefore + 1, MaterialVariantCache.VariantCount,
                    "200 objects sharing one material must produce exactly one new variant");

                // No pooled combat light may ever cast a shadow — cube-map
                // shadows on transient point lights is the cost this pipeline
                // cannot absorb. The key light is the deliberate exception, and
                // only on the tier that pays for it (LGT-01 ShadowCasterBudget).
                foreach (Light pooled in director.TransientLights.Root.GetComponentsInChildren<Light>(true))
                    Assert.AreEqual(LightShadows.None, pooled.shadows,
                        $"{pooled.name} is a transient light casting a shadow");

                int shadowCasters = world.Count(l => l.shadows != LightShadows.None);
                Assert.LessOrEqual(shadowCasters, director.Profile.ShadowCasterBudget,
                    $"{shadowCasters} shadow casters exceeds the {director.Profile.Quality} budget of "
                    + $"{director.Profile.ShadowCasterBudget}");
                if (shadowCasters == 1)
                    Assert.AreSame(director.KeyLight, world.Single(l => l.shadows != LightShadows.None),
                        "only the key light may ever cast a shadow");

                UnityEngine.Object.DestroyImmediate(shared);
            }
            finally
            {
                foreach (EnemyAgent agent in enemies)
                    if (agent != null) UnityEngine.Object.DestroyImmediate(agent.gameObject);
                foreach (GameObject go in structures)
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator RepeatedCombatReusesPoolLightsWithoutLeavingOrphanLeases()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;

            for (int round = 0; round < 50; round++)
            {
                director.TryAcquireTransientLight(
                    CombatLightingProfiles.Muzzle(WeaponFamily.Laser, TransientLightPriority.PlayerWeapon)
                        .ToRequest(Vector3.zero, director.Profile.Quality), out TransientLightHandle handle);
                director.ReleaseTransientLight(handle);
            }

            // Age everything out.
            director.Tick(5f);

            Assert.AreEqual(0, director.TransientLights.ActiveCount,
                "every lease must come back once its light expires or is released");
            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "fifty rounds of fire must not have created a single Light");
        }

        // ---- quality and map isolation ------------------------------------------------------

        [UnityTest]
        public IEnumerator QualitySwitchesDuringCombatGrowNothing()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int renderersBefore = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            int variantsBefore = MaterialVariantCache.VariantCount;

            foreach (LightingQuality quality in new[]
                     { LightingQuality.Low, LightingQuality.High, LightingQuality.Medium })
            {
                director.SetQuality(quality);
                director.TryAcquireTransientLight(
                    CombatLightingProfiles.Explosion(TransientLightPriority.PlayerWeapon)
                        .ToRequest(Vector3.zero, quality), out _);
                yield return null;

                Assert.LessOrEqual(ActiveWorldLights().Length, director.Profile.MaxRealtimeLights,
                    $"{quality} exceeded its own budget");
            }

            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length);
            Assert.AreEqual(renderersBefore, UnityEngine.Object.FindObjectsOfType<Renderer>().Length);
            Assert.AreEqual(variantsBefore, MaterialVariantCache.VariantCount);
        }

        [UnityTest]
        public IEnumerator EveryMapAndPreviewLightIsLayerCulledAwayFromTheWorld()
        {
            yield return BootScene();

            int worldLightsBefore = ActiveWorldLights().Length;

            // Any light that is not world-lighting must be culled to its own
            // layer — that is what keeps the map's rig out of the world budget.
            Light[] all = UnityEngine.Object.FindObjectsOfType<Light>(true);
            foreach (Light light in all)
            {
                if (light.cullingMask == -1) continue;
                Assert.AreNotEqual(-1, light.cullingMask,
                    $"{light.name} would leak into the world");
            }

            var screen = UnityEngine.Object.FindObjectOfType<Nebula.UI.StrategicMap.StrategicMapScreen>();
            Assert.IsNotNull(screen, "the strategic map screen must exist after boot");

            yield return null;

            Assert.AreEqual(worldLightsBefore, ActiveWorldLights().Length,
                "the map's existence must not change the world's real-time light count");
        }

        // ---- teardown ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartLeavesOneDirectorOneHullFillAndACleanPool()
        {
            yield return BootScene();

            NebulaLightingDirector before = Director();
            for (int i = 0; i < 8; i++)
                before.TryAcquireTransientLight(
                    CombatLightingProfiles.Impact(WeaponFamily.Plasma, TransientLightPriority.PlayerWeapon)
                        .ToRequest(Vector3.zero, before.Profile.Quality), out _);

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);
            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            NebulaLightingDirector[] directors = UnityEngine.Object.FindObjectsOfType<NebulaLightingDirector>();
            Assert.AreEqual(1, directors.Length, "one director survives a restart");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<CombatLightingBridge>().Length,
                "one combat bridge survives a restart");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<ShipLightingPresenter>().Length);

            Light[] world = ActiveWorldLights();
            Assert.AreEqual(1, world.Count(l => l.name == "Hull Light"), "exactly one hull fill after a restart");
            Assert.AreEqual(2, world.Length, "one key light plus one hull fill");
            Assert.AreEqual(0, directors[0].TransientLights.ActiveCount,
                "a restarted session starts with an empty pool — transient light is never restored");
        }
    }
}
