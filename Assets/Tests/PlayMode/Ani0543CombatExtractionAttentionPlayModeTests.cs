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
    /// <summary>ANI-05.4.3: the attention panel's signature, and accessibility.</summary>
    public class Ani0543CombatExtractionAttentionPlayModeTests
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
        public IEnumerator TheSameCardSet_NeverReplaysItsTransition()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudAlerts>();
            Assert.IsNotNull(hud, "the session must build the attention hud");

            int before = hud.CardTransitionsShown;
            for (int i = 0; i < 100; i++) yield return null;

            Assert.AreEqual(before, hud.CardTransitionsShown,
                "an unchanged card set must not animate on every tick");
        }

        [UnityTest]
        public IEnumerator TheFirstReadIsAdoptedInSilence()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 10; i++) yield return null;

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudAlerts>();
            Assert.AreEqual(0, hud.CardTransitionsShown,
                "a boot or a load adopts whatever is already there");
        }

        [UnityTest]
        public IEnumerator TheAttentionOrderIsNeverTouched()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudAlerts>();
            Assert.IsNotNull(hud);
            // The redesign names this VisibleAttentionCards; the cap it asserts is
            // the same one, and it is now enforced by the widget rather than the query.
            Assert.LessOrEqual(hud.VisibleAttentionCards, 3,
                "the panel shows at most three cards, and the widget owns that");
        }

        [UnityTest]
        public IEnumerator MotionOff_LandsEveryPanelAtOnce()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            AnimationAccessibility.MotionOff = true;
            director.TickUnscaled(0.001f);

            foreach (UiPanelAnimator animator in Object.FindObjectsOfType<UiPanelAnimator>(true))
                Assert.IsTrue(animator.Progress == 0f || animator.Progress == 1f, animator.name);
        }

        [UnityTest]
        public IEnumerator TimeScaleZero_StillCompletesTheUi()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            Time.timeScale = 0f;
            long uiBefore = director.UnscaledTickCount;
            long worldBefore = director.TickCount;

            for (int i = 0; i < 20; i++) { director.Tick(0f); director.TickUnscaled(0.02f); }

            Assert.Greater(director.UnscaledTickCount, uiBefore, "the UI keeps working while paused");
            Assert.AreEqual(worldBefore, director.TickCount, "and the world does not");
        }

        [UnityTest]
        public IEnumerator NoSubscriberOrRegistryGrowsAcrossAThousandFrames()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaAnimationDirector director = Director();

            var attention = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudAlerts>();
            var combat = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudVitals>();
            int transitions = attention.CardTransitionsShown;
            int shieldReactions = combat.ShieldReactionsShown;
            int animators = Object.FindObjectsOfType<UiPanelAnimator>(true).Length;

            for (int i = 0; i < 1000; i++) director.TickUnscaled(0.016f);

            Assert.AreEqual(transitions, attention.CardTransitionsShown, "no phantom transitions");
            Assert.AreEqual(shieldReactions, combat.ShieldReactionsShown, "no phantom damage");
            Assert.AreEqual(animators, Object.FindObjectsOfType<UiPanelAnimator>(true).Length);
        }

        [UnityTest]
        public IEnumerator TheVersionsAreUntouched()
        {
            yield return LoadSceneFresh();
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
            Assert.AreEqual(3, Nebula.Sectors.SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, Nebula.Content.ContentFingerprint.ContentRevision);
        }
    }
}
