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
    /// <summary>ANI-05.4.1: the two remaining HUDs really wired, and widget
    /// behaviour against a live session.</summary>
    public class Ani0541HudWidgetAnimationPlayModeTests
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
        public IEnumerator TheTwoRemainingHuds_AreReallyRegistered()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;

            // Reserve and support build are one widget now, so there is one
            // animator where there were two -- and still exactly one, which is
            // the property this test exists to hold.
            var planetContext = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudPlanetContext>();
            Assert.IsNotNull(planetContext, "the session must build the planet context widget");

            Assert.AreEqual(1, planetContext.GetComponents<UiPanelAnimator>().Length,
                "the planet context widget must carry exactly one animator");
        }

        [UnityTest]
        public IEnumerator AllTwelvePlayerPanels_AreOnTheUiClock()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            // Include inactive: an animator on a hidden panel is still a UI-clock
            // participant, and counting only active ones silently under-reports.
            int animators = Object.FindObjectsOfType<UiPanelAnimator>(true).Length;
            // The twelve legacy panels are gone; this is the redesign's own set,
            // named by the installer so adding a panel without animating it fails
            // here rather than quietly lowering the bar.
            Assert.AreEqual(PresentationInstaller.AnimatedPanelCount, animators,
                "every player-facing panel carries exactly one animator");
            Assert.AreEqual(animators, director.UnscaledParticipantCount,
                "every panel animator is a UI-clock participant, and nothing else is");
        }

        [UnityTest]
        public IEnumerator TheNewPanels_AnswerMotionOffLikeEveryOther()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            AnimationAccessibility.MotionOff = true;
            director.TickUnscaled(0.001f);

            // Both legacy panels are now the one planet-context widget. The old
            // loop skipped a null hud, so once those panels went it asserted
            // nothing at all and passed on an empty scene -- hence the explicit
            // not-null before the check.
            var planetContext = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudPlanetContext>();
            Assert.IsNotNull(planetContext, "the session must build the planet context widget");

            foreach (var hud in new MonoBehaviour[] { planetContext })
            {
                var animator = hud.GetComponent<UiPanelAnimator>();
                Assert.IsNotNull(animator, $"{hud.name} must carry a panel animator");
                Assert.IsTrue(animator.Progress == 0f || animator.Progress == 1f,
                    $"{hud.name} must land on an end state at once");
            }
        }

        [UnityTest]
        public IEnumerator ABarShowsTheRealNumberWhileTheFillCatchesUp()
        {
            yield return LoadSceneFresh();

            var bar = new UiValueAnimator();
            bar.AdoptSilently(0f);
            bar.Tick(1f, 0.016f);

            Assert.AreEqual(1f, bar.RealValue, 1e-5f, "the text reads the truth");
            Assert.Less(bar.ShownFill, 1f, "while only the graphic lags");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARestoredValue_RaisesNoGhostDelta()
        {
            yield return LoadSceneFresh();

            // What a load looks like: the first frame already has the value.
            var bar = new UiValueAnimator();
            bar.Tick(0.85f, 0.016f);

            Assert.AreEqual(0.85f, bar.ShownFill, 1e-5f);
            Assert.AreEqual(0, bar.HighlightsShown, "a load must not celebrate what it restored");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AThousandValueChanges_GrowNothing()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            int animators = Object.FindObjectsOfType<UiPanelAnimator>(true).Length;
            int groups = Object.FindObjectsOfType<CanvasGroup>(true).Length;
            int unscaled = director.UnscaledParticipantCount;

            var bar = new UiValueAnimator();
            bar.AdoptSilently(0f);
            for (int i = 0; i < 1000; i++)
            {
                bar.Tick(i % 2 == 0 ? 1f : 0f, 0.016f);
                bar.RaiseDelta(i % 3 == 0 ? 1f : 0f);
                director.TickUnscaled(0.016f);
            }

            Assert.AreEqual(animators, Object.FindObjectsOfType<UiPanelAnimator>(true).Length);
            Assert.AreEqual(groups, Object.FindObjectsOfType<CanvasGroup>(true).Length);
            Assert.AreEqual(unscaled, director.UnscaledParticipantCount);
        }


        [UnityTest]
        public IEnumerator TheTwoRealBarsAreBound_AndNeitherDelaysItsNumber()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            // The project has exactly two fill bars: the ship's throttle and the
            // XP bar. Both are now eased; every other panel is text-only and has
            // no fill graphic to bind.
            // Re-pointed to the redesigned interface (UI-09). The contract is the
            // one that matters most about these widgets: easing the bar must never
            // move the value the bar is reporting.
            Nebula.UI.Hud.NebulaHudNavigation shipHud = context.Presentation.HudNavigation;
            Nebula.UI.Hud.NebulaHudNotifications progressionHud = context.Presentation.HudNotifications;
            Assert.IsNotNull(shipHud);
            Assert.IsNotNull(progressionHud);

            float throttleBefore = context.Ship.GetThrottleFraction();
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(throttleBefore, context.Ship.GetThrottleFraction(), 1e-4f,
                "easing the bar must not touch the ship's real throttle");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesOneAnimatorPerPanel()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 4; i++) yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            var planetContext = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudPlanetContext>();
            Assert.IsNotNull(planetContext, "a restarted session rebuilds the planet context widget");
            Assert.AreEqual(1, planetContext.GetComponents<UiPanelAnimator>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
        }
    }
}
