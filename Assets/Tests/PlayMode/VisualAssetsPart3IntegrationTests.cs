using System.Collections;
using System.Linq;
using Nebula.Combat;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Nebula.Tests.PlayMode
{
    public class VisualAssetsPart3IntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>
        /// Frames, never seconds — but a stopped clock still has to be
        /// normalized, or the projectile spawned below never travels.
        /// <para>Normalized rather than restored-as-found, for the reason spelled
        /// out in <c>VisualAssetsPart1IntegrationTests</c>: a neighbour's stopped
        /// clock is stopped here, not carried forward.</para>
        /// </summary>
        [SetUp]
        public void NormalizeClock()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Debug.LogWarning($"[test isolation] Time.timeScale was {Time.timeScale} on "
                                 + "entry to VisualAssetsPart3IntegrationTests; a "
                                 + "previous fixture did not restore it.");
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreClock() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator RealScene_ShipModulesWeaponsAndProjectile_UsePart3Assets()
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
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;

            ShipController ship = Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);
            ShipModuleVisuals modules = ship.GetComponent<ShipModuleVisuals>();
            ShipWeaponVisuals weapons = ship.GetComponent<ShipWeaponVisuals>();
            Assert.IsNotNull(modules);
            Assert.IsNotNull(weapons);
            weapons.Refresh();

            NebulaVisualInstance[] shipVisuals = ship.GetComponentsInChildren<NebulaVisualInstance>(true);
            Assert.IsTrue(shipVisuals.Any(x => x.VisualId == "module.industry.logistics_cargo.t1"));
            Assert.IsTrue(shipVisuals.Any(x => x.VisualId == "module.industry.resource_processing.t1"));
            Assert.IsTrue(shipVisuals.Any(x => x.VisualId == "module.industry.station_construction.t1"));
            Assert.IsTrue(shipVisuals.Any(x => x.VisualId == "weapon.laser"));

            ProjectileObject projectile = ProjectileObject.Spawn(
                ProjectileKind.Plasma, 1, "test", ship.transform.position,
                Vector3.forward, 10f, null, null);
            Assert.IsTrue(projectile.GetComponentsInChildren<NebulaVisualInstance>(true)
                .Any(x => x.VisualId == "projectile.plasma"));
            Assert.IsFalse(projectile.GetComponentsInChildren<Collider>(true)
                .Any(x => x.GetComponentInParent<NebulaVisualInstance>() != null && x.enabled));
            Object.Destroy(projectile.gameObject);
            yield return null;
        }
    }
}
