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
    /// <summary>ANI-05.4.1: stable list rows and modal safety on the three
    /// player screens.</summary>
    public class Ani0541ScreenWidgetAnimationPlayModeTests
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
        public IEnumerator AStableRowSurvivesAThousandRebuilds()
        {
            yield return LoadSceneFresh();

            var rows = new UiStableRowRegistry();
            string[] ids = { "vehicle-1", "vehicle-2", "vehicle-3" };

            rows.BeginRefresh();
            foreach (string id in ids) rows.Observe(id);
            rows.EndRefresh();

            for (int cycle = 0; cycle < 1000; cycle++)
            {
                rows.BeginRefresh();
                foreach (string id in ids) rows.Observe(id);
                rows.EndRefresh();
                Assert.AreEqual(0, rows.EnteredLastRefresh, $"rebuild {cycle} replayed an entry");
            }

            Assert.AreEqual(ids.Length, rows.KnownCount, "and the registry did not grow");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnlyGenuinelyNewRowsEnter()
        {
            yield return LoadSceneFresh();

            var rows = new UiStableRowRegistry();
            rows.BeginRefresh();
            rows.Observe("system-a");
            rows.EndRefresh();

            rows.BeginRefresh();
            Assert.AreEqual(UiRowPhase.Held, rows.Observe("system-a"));
            Assert.AreEqual(UiRowPhase.Entering, rows.Observe("system-b"));
            rows.EndRefresh();
            Assert.AreEqual(1, rows.EnteredLastRefresh);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheScreensKeepTheirOwnModalAuthority()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            // The three screens are three pages in one menu now, so "what is open"
            // is the menu plus which page it is showing.
            bool menuOpen = context.Presentation.ParentMenu.IsOpen;
            var page = context.Presentation.ParentMenu.CurrentPage;
            bool confirmOpen = context.Presentation.Confirmation.IsOpen;

            for (int i = 0; i < 300; i++) director.TickUnscaled(0.02f);

            Assert.AreEqual(menuOpen, context.Presentation.ParentMenu.IsOpen);
            Assert.AreEqual(confirmOpen, context.Presentation.Confirmation.IsOpen);
            Assert.AreEqual(page, context.Presentation.ParentMenu.CurrentPage,
                "widget animation must not change what is open");
        }

        [UnityTest]
        public IEnumerator ARejectedPurchase_ProducesNoSuccessResponse()
        {
            yield return LoadSceneFresh();

            // A rejection returns nothing gained; the widget must stay quiet.
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.4f);
            bar.RaiseDelta(0f);

            Assert.AreEqual(0, bar.HighlightsShown,
                "a refused upgrade must never look like a completed one");
            Assert.IsFalse(bar.IsHighlighting);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ALevelUpCelebratesExactlyOnce()
        {
            yield return LoadSceneFresh();

            var bar = new UiValueAnimator();
            bar.AdoptSilently(0f);
            bar.RaiseDelta(1f);

            for (int i = 0; i < 120; i++) bar.Tick(1f, 0.016f);

            Assert.AreEqual(1, bar.HighlightsShown, "one level-up, one celebration");
            Assert.IsFalse(bar.IsHighlighting, "and it ends");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TimeScaleZero_StillCompletesAWidgetTransition()
        {
            yield return LoadSceneFresh();

            Time.timeScale = 0f;
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0f);
            for (int i = 0; i < 200; i++) bar.Tick(1f, 0.016f);

            Assert.AreEqual(1f, bar.ShownFill, 1e-3f,
                "a paused game must not freeze a UI bar mid-fill");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReducedMotion_ShortensSelectionMovementOnly()
        {
            yield return LoadSceneFresh();

            float full = UiAnimationProfiles.SlideDistance();
            float duration = UiAnimationProfiles.StateTransitionSeconds;

            AnimationAccessibility.ReducedMotion = true;

            Assert.AreEqual(full * UiAnimationProfiles.ReducedMotionDistanceScale,
                UiAnimationProfiles.SlideDistance(), 1e-4f);
            Assert.AreEqual(duration, UiAnimationProfiles.StateTransitionSeconds,
                "the state change stays just as readable");
            yield return null;
        }
    }
}
