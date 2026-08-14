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
using Nebula.Modules;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// LGT-05.3 in the real booted scene: the progression panels exist, their
    /// readings walk the shared status levels, and a restart leaves one of each.
    /// </summary>
    public class Lgt053ProgressionPalettePlayModeTests
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
        public IEnumerator BothProgressionPanelsExistExactlyOnce()
        {
            yield return BootScene();

            // The world-space HUD line became the notification line; the modal
            // screen became a page inside the one parent menu.
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudNotifications>(true).Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<Nebula.UI.Menu.NebulaParentMenuScreen>(true).Length);
        }

        [UnityTest]
        public IEnumerator AnUpgradeWalksLockedToAvailableToWorkingToMaxed()
        {
            yield return BootScene();

            // The four points of a module's life, in the order a player meets them.
            Assert.AreEqual(UiStatusLevel.Inactive,
                LightingUiPalette.Progression(UpgradeRejectReason.LevelTooLow));
            Assert.AreEqual(UiStatusLevel.Nominal,
                LightingUiPalette.Progression(UpgradeRejectReason.None));
            Assert.AreEqual(UiStatusLevel.Working,
                LightingUiPalette.Progression(UpgradeRejectReason.None, upgradeActive: true));
            Assert.AreEqual(UiStatusLevel.Nominal,
                LightingUiPalette.Progression(UpgradeRejectReason.AlreadyMaxTier));

            var seen = new[]
            {
                LightingUiPalette.Resolve(UiStatusLevel.Inactive, 0.95f),
                LightingUiPalette.Resolve(UiStatusLevel.Nominal, 0.95f),
                LightingUiPalette.Resolve(UiStatusLevel.Working, 0.95f)
            };
            Assert.AreEqual(3, seen.Distinct().Count(), "each step must look different from the last");

            yield return null;
        }

        [UnityTest]
        public IEnumerator MissingResourcesAndMissingMilestonesReadDifferently()
        {
            yield return BootScene();

            UiStatusLevel milestone = LightingUiPalette.Progression(UpgradeRejectReason.MilestoneNotMet);
            UiStatusLevel resources = LightingUiPalette.Progression(UpgradeRejectReason.InsufficientResources);
            UiStatusLevel points = LightingUiPalette.Progression(UpgradeRejectReason.InsufficientPoints);

            Assert.AreEqual(UiStatusLevel.Caution, milestone);
            Assert.AreEqual(UiStatusLevel.Warning, resources);
            Assert.AreEqual(UiStatusLevel.Warning, points);
            Assert.AreNotEqual(LightingUiPalette.Resolve(milestone), LightingUiPalette.Resolve(resources),
                "a player must be able to tell 'not yet unlocked' from 'cannot afford'");

            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryProgressionStateStaysDistinctAndLegibleInAliveSession()
        {
            yield return BootScene();

            foreach (UpgradeRejectReason reason in (UpgradeRejectReason[])Enum.GetValues(typeof(UpgradeRejectReason)))
            {
                Color color = LightingUiPalette.Resolve(LightingUiPalette.Progression(reason), 0.95f);
                Assert.Greater(color.maxColorComponent, 0.2f, $"{reason} is unreadable");
                Assert.AreEqual(0.95f, color.a, 1e-5f, $"{reason} lost its opacity");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ProgressionColourChangesCostTheWorldNothing()
        {
            yield return BootScene();

            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int renderersBefore = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            int variantsBefore = MaterialVariantCache.VariantCount;

            foreach (bool flash in new[] { false, true })
                foreach (bool contrast in new[] { false, true })
                {
                    LightingAccessibility.FlashReduction = flash;
                    LightingAccessibility.HighContrast = contrast;
                    foreach (UpgradeRejectReason reason in (UpgradeRejectReason[])Enum.GetValues(typeof(UpgradeRejectReason)))
                        LightingUiPalette.Resolve(LightingUiPalette.Progression(reason), 0.95f);
                }

            yield return null;

            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length);
            Assert.AreEqual(renderersBefore, UnityEngine.Object.FindObjectsOfType<Renderer>().Length);
            Assert.AreEqual(variantsBefore, MaterialVariantCache.VariantCount);
        }

        [UnityTest]
        public IEnumerator RestartLeavesOneOfEachProgressionPanelAndACleanWorld()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudNotifications>(true).Length,
                "a restart must not accumulate progression panels");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<Nebula.UI.Menu.NebulaParentMenuScreen>(true).Length);
            Assert.AreEqual(2, ActiveWorldLights().Length,
                "the world still runs on one key light plus one hull fill");
        }
    }
}
