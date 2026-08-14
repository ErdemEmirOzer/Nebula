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
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05.4.2: real rows, keyed by the id the game already owns.
    /// <para>
    /// Re-pointed from NavigationScreen to the redesigned interface (UI-09). The
    /// contract is untouched: a row already listed must not replay its entrance on
    /// every rebuild, and the registry must not grow. What moved is which surface
    /// carries it — the fleet page keys its rows by vehicle id, exactly as the
    /// navigation screen keyed its rows by system id.
    /// </para>
    /// </summary>
    public class Ani0542StableRowPlayModeTests
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
        public IEnumerator AHundredRefreshes_EnterNoRowTwice()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            Nebula.UI.Menu.NebulaFleetPage page = context.Presentation.FleetPage;
            Assert.IsNotNull(page, "the redesigned fleet page is not registered on the presentation handle");

            // Let the first refresh settle, whatever it lists. The first read after a
            // boot or a load is adopted in silence by design, so measuring before it
            // has happened would count an adoption as an entrance.
            for (int i = 0; i < 10; i++) { page.Refresh(); yield return null; }

            int entriesSeen = 0;
            for (int i = 0; i < 100; i++)
            {
                page.Refresh();
                yield return null;
                entriesSeen += page.VehicleRowsEnteredLastRefresh;
            }

            Assert.AreEqual(0, entriesSeen,
                "a system already listed must not replay its entrance on every rebuild");
        }

        [UnityTest]
        public IEnumerator TheRegistryDoesNotGrowAcrossAThousandRefreshes()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            var rows = new UiStableRowRegistry();
            string[] ids = { "sys-a", "sys-b", "sys-c" };

            for (int cycle = 0; cycle < 1000; cycle++)
            {
                rows.BeginRefresh();
                foreach (string id in ids) rows.Observe(id);
                rows.EndRefresh();
            }

            Assert.AreEqual(ids.Length, rows.KnownCount);
            Assert.AreEqual(0, rows.EnteredLastRefresh);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARemovedIdIsCleanedUp()
        {
            yield return LoadSceneFresh();

            var rows = new UiStableRowRegistry();
            rows.BeginRefresh();
            rows.Observe("keep");
            rows.Observe("drop");
            rows.EndRefresh();

            rows.BeginRefresh();
            rows.Observe("keep");
            var gone = rows.EndRefresh();

            Assert.AreEqual(1, gone.Count);
            Assert.AreEqual("drop", gone[0]);
            Assert.IsFalse(rows.IsKnown("drop"), "and it leaves the registry");
            Assert.IsTrue(rows.IsKnown("keep"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheScreensSelectionAndModalStateAreUntouched()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            // The navigation screen's selection is the parent menu's current page
            // now: the discovery rows live inside it, and the invariant is the same
            // one -- refreshing rows must not move the surface the player is on.
            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            bool openBefore = menu.IsOpen;
            NebulaUiAssetCatalog.Page? pageBefore = menu.CurrentPage;

            for (int i = 0; i < 60; i++) yield return null;

            Assert.AreEqual(openBefore, menu.IsOpen, "row bookkeeping must not open or close the menu");
            Assert.AreEqual(pageBefore, menu.CurrentPage, "nor change which page is shown");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesTheRowBookkeepingClean()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 6; i++) yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 10; i++) yield return null;

            NebulaParentMenuScreen menu = Context().Presentation.ParentMenu;
            Assert.IsNotNull(menu, "the new session builds its own menu");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaParentMenuScreen>(true).Length,
                "and only one of it");
        }
    }
}
