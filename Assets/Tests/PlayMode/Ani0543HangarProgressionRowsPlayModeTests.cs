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
    /// <summary>ANI-05.4.3: stable rows on the hangar and progression screens.</summary>
    public class Ani0543HangarProgressionRowsPlayModeTests
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
        public IEnumerator AHundredHangarRefreshes_EnterNoVehicleTwice()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaContext context = Context();

            Nebula.UI.Menu.NebulaFleetPage screen = context.Presentation.FleetPage;
            Assert.IsNotNull(screen);

            int entries = 0;
            for (int i = 0; i < 100; i++)
            {
                yield return null;
                entries += screen.VehicleRowsEnteredLastRefresh;
            }

            Assert.AreEqual(0, entries,
                "a vehicle already listed keeps its row through every phase refresh");
        }

        [UnityTest]
        public IEnumerator AHundredProgressionRefreshes_EnterNoModuleTwice()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaContext context = Context();

            var screen = context.Presentation.ParentMenu.PageOf<Nebula.UI.Menu.NebulaProgressionPage>();
            Assert.IsNotNull(screen);

            int entries = 0;
            for (int i = 0; i < 100; i++)
            {
                yield return null;
                entries += screen.ModuleRowsEnteredLastRefresh;
            }

            Assert.AreEqual(0, entries, "the module list is stable across rebuilds");
        }

        [UnityTest]
        public IEnumerator ANewIdEntersOnce_AndARemovedOneLeaves()
        {
            yield return LoadSceneFresh();

            var rows = new UiStableRowRegistry();
            rows.BeginRefresh();
            rows.Observe("vehicle-a");
            rows.EndRefresh();
            Assert.AreEqual(1, rows.EnteredLastRefresh);

            rows.BeginRefresh();
            rows.Observe("vehicle-a");
            rows.Observe("vehicle-b");
            rows.EndRefresh();
            Assert.AreEqual(1, rows.EnteredLastRefresh, "only the genuinely new one enters");

            rows.BeginRefresh();
            rows.Observe("vehicle-b");
            var gone = rows.EndRefresh();
            Assert.AreEqual(1, gone.Count);
            Assert.AreEqual("vehicle-a", gone[0]);
            Assert.IsFalse(rows.IsKnown("vehicle-a"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheScreensSelectionAndModalStateSurvive()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaContext context = Context();

            // Both screens are pages in the one menu now, so the state that must
            // survive row bookkeeping is the menu's open flag and its current page.
            bool menuOpen = context.Presentation.ParentMenu.IsOpen;
            var page = context.Presentation.ParentMenu.CurrentPage;

            for (int i = 0; i < 60; i++) yield return null;

            Assert.AreEqual(menuOpen, context.Presentation.ParentMenu.IsOpen,
                "row bookkeeping must not open or close a screen");
            Assert.AreEqual(page, context.Presentation.ParentMenu.CurrentPage);
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesNoRegistryLeak()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 8; i++) yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 10; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Menu.NebulaParentMenuScreen>(true).Length,
                "the pages live in one menu, and a restart must leave exactly one of it");
            Assert.IsNotNull(Context().Presentation.FleetPage);
            Assert.IsNotNull(Context().Presentation.ParentMenu.PageOf<Nebula.UI.Menu.NebulaProgressionPage>());
        }
    }
}
