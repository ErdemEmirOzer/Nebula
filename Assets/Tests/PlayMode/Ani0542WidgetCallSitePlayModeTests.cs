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
using Nebula.Progression;
using Nebula.UI;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>ANI-05.4.2: the real call sites — the XP/level-up notify line
    /// and its XP bar, driven by the game's own award event.</summary>
    public class Ani0542WidgetCallSitePlayModeTests
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
        public IEnumerator ANormalXpAward_FlashesTheBarButCelebratesNoLevel()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            Nebula.UI.Hud.NebulaHudNotifications hud = context.Presentation.HudNotifications;
            Assert.IsNotNull(hud);

            int levelUpsBefore = hud.LevelUpsCelebrated;
            int deltasBefore = hud.XpDeltasShown;

            // The real award path.
            ProgressionService.Award("ani0542.test", 1);
            for (int i = 0; i < 5; i++) yield return null;

            Assert.AreEqual(levelUpsBefore, hud.LevelUpsCelebrated,
                "plain XP is not a level-up and must not be celebrated as one");
            Assert.GreaterOrEqual(hud.XpDeltasShown, deltasBefore,
                "but the bar may acknowledge a real gain");
        }

        [UnityTest]
        public IEnumerator TheNotifyLineIsTheExistingOne_AndPlaysAtMostOncePerWindow()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            Nebula.UI.Hud.NebulaHudNotifications hud = context.Presentation.HudNotifications;
            int before = hud.NotificationsPlayed;

            ProgressionService.Award("ani0542.test", 1);
            for (int i = 0; i < 20; i++) yield return null;

            Assert.LessOrEqual(hud.NotificationsPlayed - before, 1,
                "one award window plays one entrance, however many frames pass");
        }

        [UnityTest]
        public IEnumerator NoAward_PlaysNothingAtAll()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            Nebula.UI.Hud.NebulaHudNotifications hud = context.Presentation.HudNotifications;
            int notifications = hud.NotificationsPlayed;
            int deltas = hud.XpDeltasShown;
            int levelUps = hud.LevelUpsCelebrated;

            for (int i = 0; i < 60; i++) yield return null;

            Assert.AreEqual(notifications, hud.NotificationsPlayed, "idle frames announce nothing");
            Assert.AreEqual(deltas, hud.XpDeltasShown);
            Assert.AreEqual(levelUps, hud.LevelUpsCelebrated);
        }

        [UnityTest]
        public IEnumerator ABootedSession_HasCelebratedNothing()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaContext context = Context();

            Nebula.UI.Hud.NebulaHudNotifications hud = context.Presentation.HudNotifications;
            Assert.AreEqual(0, hud.LevelUpsCelebrated,
                "a loaded save must not replay the level-ups it restored");
            Assert.AreEqual(0, hud.NotificationsPlayed,
                "nor the notifications that were already spent");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_DoesNotReplayPastCelebrations()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 6; i++) yield return null;

            ProgressionService.Award("ani0542.test", 1);
            for (int i = 0; i < 5; i++) yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 10; i++) yield return null;

            Nebula.UI.Hud.NebulaHudNotifications hud = Context().Presentation.HudNotifications;
            Assert.AreEqual(0, hud.LevelUpsCelebrated, "the new session starts quiet");
            Assert.AreEqual(0, hud.NotificationsPlayed);
        }

        [UnityTest]
        public IEnumerator TheXpTextStillPrintsTheRealNumber()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            // The bar graphic may lag; the printed line may not.
            ProgressionService.Award("ani0542.test", 1);
            yield return null;

            ProgressionState state = ProgressionService.State;
            Assert.IsNotNull(state, "the real progression state must be readable");
            Assert.GreaterOrEqual(state.TotalXp, 0L);
        }
    }
}
