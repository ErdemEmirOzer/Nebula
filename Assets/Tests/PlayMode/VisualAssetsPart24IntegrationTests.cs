using System.Collections;
using System.Linq;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.UI;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Nebula.Tests.PlayMode
{
    public class VisualAssetsPart24IntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>
        /// Frames, never seconds — but a stopped clock still has to be
        /// normalized, or the booted session never reaches the asserted stage.
        /// <para>Normalized rather than restored-as-found, for the reason spelled
        /// out in <c>VisualAssetsPart1IntegrationTests</c>: a neighbour's stopped
        /// clock is stopped here, not carried forward.</para>
        /// </summary>
        [SetUp]
        public void NormalizeClock()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Debug.LogWarning($"[test isolation] Time.timeScale was {Time.timeScale} on "
                                 + "entry to VisualAssetsPart24IntegrationTests; a "
                                 + "previous fixture did not restore it.");
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreClock() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator RealScene_UsesMkIStage03_AnchorsAndRuntimeIcons()
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
            PlayerShipVisualController visual = ship.GetComponent<PlayerShipVisualController>();
            Assert.IsNotNull(visual);
            Assert.AreEqual(1, visual.CurrentHullForm);
            Assert.AreEqual(3, visual.CurrentStage);
            Assert.IsNotNull(visual.CurrentVisual);
            Assert.AreEqual("visual.ship.player.mki.stage03",
                visual.CurrentVisual.GetComponent<NebulaVisualInstance>().VisualId);

            Assert.IsNotNull(visual.FindCurrentAnchor("anchor.thruster.L"));
            Assert.IsNotNull(visual.FindCurrentAnchor("anchor.thruster.R"));
            Assert.IsNotNull(visual.FindCurrentAnchor("anchor.hardpoint.01"));
            Assert.IsFalse(visual.CurrentVisual.GetComponentsInChildren<Collider>(true).Any(x => x.enabled));

            ShipModuleVisuals moduleVisuals = ship.GetComponent<ShipModuleVisuals>();
            Assert.IsNotNull(moduleVisuals);
            NebulaVisualInstance logistics = ship.GetComponentsInChildren<NebulaVisualInstance>(true)
                .FirstOrDefault(x => x.VisualId == "module.industry.logistics_cargo.t1");
            Assert.IsNotNull(logistics, "fallback module visual remains available for non-contiguous/building states");
            Assert.IsFalse(logistics.gameObject.activeInHierarchy,
                "a module already baked into Stage03 must not render twice");

            ShipWeaponVisuals weaponVisuals = ship.GetComponent<ShipWeaponVisuals>();
            Assert.IsNotNull(weaponVisuals);
            weaponVisuals.Refresh();
            Transform weaponScaleRoot = ship.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name.StartsWith("Part 24 Laser Hardpoint"));
            Assert.IsNotNull(weaponScaleRoot, "the free laser must remain visually mounted");
            Assert.AreEqual(PlayerShipVisualController.ExternalWeaponVisualScale,
                weaponScaleRoot.localScale.x, 0.001f,
                "the standalone weapon must be fitted to the Part 24 hull");

            // HangarScreen is the Filo page now (UI-09). The page is a plain class,
            // not a MonoBehaviour, so it is reached through the presentation handle;
            // its icons live under the menu it is built into.
            var menu = Object.FindObjectOfType<Nebula.UI.Menu.NebulaParentMenuScreen>(true);
            Assert.IsNotNull(menu);
            Assert.IsNotNull(menu.PageOf<Nebula.UI.Menu.NebulaFleetPage>(),
                "the fleet page carries what the hangar screen used to");

            Assert.IsNotNull(NebulaIconResolver.Item("energy.nuclear"));
        }
    }
}
