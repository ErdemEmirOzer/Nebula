using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.UI;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05.4: every player HUD really carries a panel animator, driven by
    /// its own open state on the unscaled clock.
    /// </summary>
    public class Ani054HudAnimationPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;
        private readonly Dictionary<string, string> slot1Backup = new Dictionary<string, string>();

        private static IEnumerable<string> Slot1Files()
        {
            string main = SaveService.SlotFile(GameplaySlot);
            string dir = Path.GetDirectoryName(main);
            yield return main;
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak1.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak2.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak3.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.tmp.json");
        }

        [SetUp]
        public void BackupRealGameplaySlot()
        {
            slot1Backup.Clear();
            foreach (string f in Slot1Files()) slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
            Time.timeScale = 1f;
            foreach (var kv in slot1Backup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        private static IEnumerator LoadSceneFresh()
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
            // entry the menu\'s New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        private static NebulaAnimationDirector Director()
        {
            var d = Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(d);
            return d;
        }

        private static NebulaContext Context() => Object.FindObjectOfType<SolarSystemBootstrap>().Context;

        [UnityTest]
        public IEnumerator EveryPlayerHud_CarriesExactlyOneAnimator()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();

            var panels = new GameObject[]
            {
                // Re-pointed to the redesigned interface (UI-09). The contract is
                // unchanged: exactly one animator per player-facing panel, and the
                // dev-only HUDs and the map still excluded.
                context.Presentation.HudContextBar.gameObject,
                context.Presentation.HudVitals.gameObject,
                context.Presentation.HudNavigation.gameObject,
                context.Presentation.HudAlerts.gameObject,
                context.Presentation.HudPlanetContext.gameObject,
                context.Presentation.ParentMenu.gameObject,
                context.Presentation.Confirmation.gameObject
            };

            foreach (GameObject panel in panels)
            {
                UiPanelAnimator[] animators = panel.GetComponents<UiPanelAnimator>();
                Assert.AreEqual(1, animators.Length, $"{panel.name} must have exactly one animator");
            }
        }

        [UnityTest]
        public IEnumerator TheDevOnlyHudsAndTheMap_AreDeliberatelyExcluded()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();

            Assert.IsNull(context.Presentation.StrategicMapScreen.GetComponent<UiPanelAnimator>(),
                "the StrategicMap is out of ANI-05.4's scope entirely");
            if (context.Presentation.SectorDebugHud != null)
                Assert.IsNull(context.Presentation.SectorDebugHud.GetComponent<UiPanelAnimator>(),
                    "a developer hud is not part of the player's UI language");
        }

        [UnityTest]
        public IEnumerator PanelsRunOnTheUnscaledClock_NotTheWorldClock()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            Assert.Greater(director.UnscaledParticipantCount, 0,
                "the panels must be registered on the UI clock");
        }

        [UnityTest]
        public IEnumerator AHudsAnimator_NeverOpensOrClosesItsPanel()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            Nebula.UI.Hud.NebulaHudVitals hud = context.Presentation.HudVitals;
            bool activeBefore = hud.gameObject.activeSelf;

            for (int i = 0; i < 100; i++) director.TickUnscaled(0.02f);

            Assert.AreEqual(activeBefore, hud.gameObject.activeSelf,
                "the animation reads the panel's state; it never sets it");
        }

        [UnityTest]
        public IEnumerator AnOpenPanelSettlesAtItsAuthoredAlphaAndPosition()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            // The context bar, not the vitals cluster. Vitals is hidden at boot by
            // design — it appears only in combat or while damaged — so its animator
            // correctly reports Hidden, which is the wrong subject for a test about
            // what an *open* panel settles at. The context bar is always up.
            var animator = context.Presentation.HudContextBar.GetComponent<UiPanelAnimator>();
            Assert.IsNotNull(animator);

            for (int i = 0; i < 60; i++) director.TickUnscaled(0.02f);

            var group = context.Presentation.HudContextBar.GetComponent<CanvasGroup>();
            var rect = context.Presentation.HudContextBar.GetComponent<RectTransform>();

            Assert.AreEqual(UiPanelPhase.Shown, animator.Phase);
            if (group != null) Assert.AreEqual(animator.BaseAlpha, group.alpha, 1e-3f);
            if (rect != null)
            {
                Assert.AreEqual(animator.BasePosition.x, rect.anchoredPosition.x, 0.01f);
                Assert.AreEqual(animator.BasePosition.y, rect.anchoredPosition.y, 0.01f);
            }
        }

        [UnityTest]
        public IEnumerator MotionOff_LandsEveryPanelOnItsFinalPoseAtOnce()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            AnimationAccessibility.MotionOff = true;
            director.TickUnscaled(0.001f);

            foreach (UiPanelAnimator animator in Object.FindObjectsOfType<UiPanelAnimator>())
                Assert.IsTrue(animator.Progress == 0f || animator.Progress == 1f,
                    $"{animator.name} must be at an end state, not mid-transition");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesNoDuplicateAnimator()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 4; i++) yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            NebulaContext context = Context();
            Assert.AreEqual(1, context.Presentation.HudNavigation.GetComponents<UiPanelAnimator>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
        }
    }
}
