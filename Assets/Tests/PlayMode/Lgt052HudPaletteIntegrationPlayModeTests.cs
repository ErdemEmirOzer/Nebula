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
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// LGT-05.2 in the real booted scene: the HUDs exist, read the shared
    /// palette, and walking a reading through its states costs the world
    /// nothing.
    /// </summary>
    public class Lgt052HudPaletteIntegrationPlayModeTests
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

        private static void AssertExactlyOne<T>(string because = null) where T : UnityEngine.Object
        {
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<T>(true).Length,
                because ?? $"exactly one {typeof(T).Name}");
        }

        private static NebulaContext Context()
            => UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>().Context;

        // ---- the HUDs are really there ------------------------------------------

        [UnityTest]
        public IEnumerator EveryStatusHudExistsExactlyOnceInABootedSession()
        {
            yield return BootScene();

            // Six legacy HUDs became four widgets plus a menu page: ShipHud and
            // NavigationHud were both navigation, AttentionHud and ExtractionHud
            // were both alerts. Inactive objects count -- a widget starts with its
            // panel hidden, and a duplicate hidden one is still a duplicate.
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudNavigation>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudVitals>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudAlerts>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudPlanetContext>();
            Assert.IsNotNull(Context().Presentation.FleetPage, "the fleet page replaces the hangar HUD");
        }

        // ---- readings walk their states ---------------------------------------------

        [UnityTest]
        public IEnumerator TheShipReadingWalksNormalToDamagedToShutdownToRepair()
        {
            yield return BootScene();

            var states = new[]
            {
                (ShipLightingState.Idle, UiStatusLevel.Nominal),
                (ShipLightingState.Damaged, UiStatusLevel.Warning),
                (ShipLightingState.Disabled, UiStatusLevel.Critical),
                (ShipLightingState.Repair, UiStatusLevel.Working)
            };

            var seen = states.Select(s =>
            {
                Assert.AreEqual(s.Item2, LightingUiPalette.Ship(s.Item1), $"{s.Item1}");
                return LightingUiPalette.Resolve(s.Item2, 0.95f);
            }).ToArray();

            Assert.AreEqual(4, seen.Distinct().Count(), "each ship state must look different from the last");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ADrillReadingWalksNormalToEightyPercentToFull()
        {
            yield return BootScene();

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Storage(20f, 100f));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Storage(85f, 100f));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Storage(100f, 100f));
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Storage(20f, 100f),
                "emptying the store must return the reading to nominal, not latch");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AStructureReadingWalksBuildingToActiveToDisabled()
        {
            yield return BootScene();

            Assert.AreEqual(UiStatusLevel.Working, LightingUiPalette.Structure(DrillLifecycle.Constructing));
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Structure(DrillLifecycle.Active));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Structure(DrillLifecycle.Disabled));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DominanceLinkAndAttentionAllWalkTheirStates()
        {
            yield return BootScene();

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Dominance(DominanceState.Controlled));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Dominance(DominanceState.Contested));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Dominance(DominanceState.Lost));

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.CommunicationLink(true, true));
            Assert.AreEqual(UiStatusLevel.Caution, LightingUiPalette.CommunicationLink(false, true));
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.CommunicationLink(false, false));

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Attention(0));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Attention(3));

            // Maintenance reserve and support build read through the same two
            // resolvers the panels use.
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Storage(80f, 100f));
            Assert.AreEqual(UiStatusLevel.Working, LightingUiPalette.Structure(DrillLifecycle.CapsuleInTransit));

            yield return null;
        }

        // ---- the UI costs the world nothing -----------------------------------------------

        [UnityTest]
        public IEnumerator WalkingEveryHudStateChangesNoLightMaterialOrGameObjectCount()
        {
            yield return BootScene();

            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int renderersBefore = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            int variantsBefore = MaterialVariantCache.VariantCount;
            int worldLightsBefore = ActiveWorldLights().Length;

            // Resolve every status the HUDs can show, including under every
            // accessibility combination.
            foreach (bool flash in new[] { false, true })
                foreach (bool contrast in new[] { false, true })
                {
                    LightingAccessibility.FlashReduction = flash;
                    LightingAccessibility.HighContrast = contrast;
                    foreach (UiStatusLevel level in (UiStatusLevel[])Enum.GetValues(typeof(UiStatusLevel)))
                        LightingUiPalette.Resolve(level, 0.95f);
                }

            yield return null;

            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "a HUD colour change must never create a Light");
            Assert.AreEqual(renderersBefore, UnityEngine.Object.FindObjectsOfType<Renderer>().Length,
                "nor a Renderer");
            Assert.AreEqual(variantsBefore, MaterialVariantCache.VariantCount,
                "nor a material variant");
            Assert.AreEqual(worldLightsBefore, ActiveWorldLights().Length);
        }

        // ---- teardown -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartLeavesExactlyOneOfEachHudAndNoDuplicateBinder()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            AssertExactlyOne<Nebula.UI.Hud.NebulaHudNavigation>("a restart must not accumulate HUDs");
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudVitals>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudAlerts>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudPlanetContext>();
            Assert.IsNotNull(Context().Presentation.FleetPage, "and must leave exactly one fleet page");

            Assert.AreEqual(2, ActiveWorldLights().Length,
                "the world still runs on one key light plus one hull fill");
        }
    }
}
