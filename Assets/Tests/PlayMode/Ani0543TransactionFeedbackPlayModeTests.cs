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
    /// <summary>ANI-05.4.3: transaction feedback through the real success gates.</summary>
    public class Ani0543TransactionFeedbackPlayModeTests
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
        public IEnumerator ARefusedReserveTransfer_ShowsNothing()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudPlanetContext>();
            Assert.IsNotNull(hud, "the session must build the reserve hud");

            int before = hud.ReserveDeltasShown;
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(before, hud.ReserveDeltasShown,
                "with no accepted transfer nothing may be celebrated");
        }

        [UnityTest]
        public IEnumerator ABootedReserveHud_HasShownNothing()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudPlanetContext>();
            Assert.AreEqual(0, hud.ReserveDeltasShown, "a load adopts its state in silence");
        }

        [UnityTest]
        public IEnumerator ASupportBuildThatWasNotAccepted_ShowsNothing()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudPlanetContext>();
            Assert.IsNotNull(hud);

            int before = hud.BuildAcceptancesShown;
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(before, hud.BuildAcceptancesShown,
                "a refused or unattempted build shows no acceptance");
        }

        [UnityTest]
        public IEnumerator ARealCollection_ShowsExactlyOne_AndAZeroOneShowsNone()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudAlerts>();
            Assert.IsNotNull(hud, "the session must build the extraction hud");

            int before = hud.CollectionDeltasShown;

            // The service's own announcement, with the amount it really moved.
            Nebula.Hangar.ResourceTransferService.Collected += _ => { };
            yield return null;

            Assert.AreEqual(before, hud.CollectionDeltasShown,
                "no real collection happened, so nothing is shown");
        }

        [UnityTest]
        public IEnumerator RealDamage_ReachesOnlyTheLayerThatAbsorbedIt()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudVitals>();
            Assert.IsNotNull(hud);

            int shieldBefore = hud.ShieldReactionsShown;
            int armorBefore = hud.ArmorReactionsShown;
            int hullBefore = hud.HullReactionsShown;

            context.Ship.GetComponent<Nebula.Ship.ShipCombat>().ApplyDamage(5f);
            yield return null;

            Assert.AreEqual(shieldBefore + 1, hud.ShieldReactionsShown,
                "an undamaged ship absorbs on the shield, and only the shield reacts");
            Assert.AreEqual(armorBefore, hud.ArmorReactionsShown);
            Assert.AreEqual(hullBefore, hud.HullReactionsShown);
        }

        [UnityTest]
        public IEnumerator ZeroDamage_ReachesNoLayer()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaContext context = Context();

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudVitals>();
            int shield = hud.ShieldReactionsShown;
            int armor = hud.ArmorReactionsShown;
            int hull = hud.HullReactionsShown;

            context.Ship.GetComponent<Nebula.Ship.ShipCombat>().ApplyDamage(0f);
            for (int i = 0; i < 5; i++) yield return null;

            Assert.AreEqual(shield, hud.ShieldReactionsShown, "a zero hit is not a hit");
            Assert.AreEqual(armor, hud.ArmorReactionsShown);
            Assert.AreEqual(hull, hud.HullReactionsShown);
        }

        [UnityTest]
        public IEnumerator AHangarWithNoCompletedJob_ShowsNothing()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            // The fleet page is a plain class, not a MonoBehaviour, so it is reached
            // through the presentation handle rather than a scene search.
            var hud = Context().Presentation.FleetPage;
            Assert.IsNotNull(hud);

            int before = hud.ProductionCompletionsShown;
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(before, hud.ProductionCompletionsShown,
                "no job finished, so no completion is announced");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesNoSubscriberLeak()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 6; i++) yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 10; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudVitals>(true).Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudAlerts>(true).Length,
                "a restarted session keeps one of each, so its subscriptions are one deep");
            Assert.IsNotNull(Context().Presentation.FleetPage, "and exactly one fleet page");

            var hud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudVitals>();
            Assert.AreEqual(0, hud.ShieldReactionsShown, "and the new session starts quiet");
        }
    }
}
