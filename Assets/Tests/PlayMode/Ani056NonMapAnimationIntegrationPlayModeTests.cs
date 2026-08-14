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
    /// <summary>ANI-05.6: the whole non-map animation stack, live in one session.</summary>
    public class Ani056NonMapAnimationIntegrationPlayModeTests
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
        public IEnumerator TheSessionHasExactlyOneDirectorAndOneOfEachSingleton()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<CombatAnimationBridge>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<RaidAnimationPresenter>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<WeaponGimbalPresenter>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<ShipAttitudeAndHangarPresenter>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<PlayerShipAnimationPresenter>().Length);
        }

        [UnityTest]
        public IEnumerator EveryLayerIsReallyAttachedInTheLiveSession()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaContext context = Context();

            Assert.IsNotNull(context.Ship.GetComponent<PlayerShipAnimationPresenter>(), "ANI-01");
            Assert.IsNotNull(context.Ship.GetComponent<ProcessingAnimationPresenter>(), "ANI-02");
            Assert.IsNotNull(context.Ship.GetComponent<WeaponGimbalPresenter>(), "ANI-04");
            Assert.IsNotNull(context.Ship.GetComponent<ShipAttitudeAndHangarPresenter>(), "ANI-05.2");

            Assert.Greater(Object.FindObjectsOfType<CelestialAnimationPresenter>().Length, 0, "ANI-05 celestial");
            Assert.Greater(Object.FindObjectsOfType<DominanceMarkerPresenter>().Length, 0, "ANI-05.3 dominance");
            // Inactive included: a panel starts hidden and its animator still counts.
            Assert.AreEqual(PresentationInstaller.AnimatedPanelCount,
                Object.FindObjectsOfType<UiPanelAnimator>(true).Length, "ANI-05.4 UI");

            NebulaAnimationDirector director = Director();
            Assert.IsNotNull(director.CapsuleProxies, "ANI-02.1");
            Assert.IsNotNull(director.ItemProxies, "ANI-02.2");
            Assert.IsNotNull(director.SupportOperations, "ANI-03.1");
        }

        [UnityTest]
        public IEnumerator TheWorldAndUiParticipantListsAreBothSingular()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaAnimationDirector director = Director();

            int world = director.ParticipantCount;
            int ui = director.UnscaledParticipantCount;

            for (int i = 0; i < 200; i++) { director.Tick(0.02f); director.TickUnscaled(0.02f); }

            Assert.AreEqual(world, director.ParticipantCount, "ticking must register nothing new");
            Assert.AreEqual(ui, director.UnscaledParticipantCount);
            Assert.AreEqual(Object.FindObjectsOfType<UiPanelAnimator>().Length, ui,
                "the UI clock carries exactly the panels");
        }

        [UnityTest]
        public IEnumerator MotionOff_BringsEverySystemToItsAuthoredPose()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaAnimationDirector director = Director();

            for (int i = 0; i < 100; i++) director.Tick(0.02f);

            AnimationAccessibility.MotionOff = true;
            director.Tick(0.02f);
            director.TickUnscaled(0.02f);

            foreach (CelestialAnimationPresenter c in Object.FindObjectsOfType<CelestialAnimationPresenter>())
                Assert.AreEqual(0f, c.CurrentSpinDegrees, $"{c.name} must be at its authored pose");
            foreach (UiPanelAnimator p in Object.FindObjectsOfType<UiPanelAnimator>())
                Assert.IsTrue(p.Progress == 0f || p.Progress == 1f, $"{p.name} must be at an end state");
            foreach (DominanceMarkerPresenter d in Object.FindObjectsOfType<DominanceMarkerPresenter>())
                Assert.IsFalse(d.ShowingMarker, "and no marker may be held");
        }

        [UnityTest]
        public IEnumerator TheGameplayShipIsNeverMovedByAnyOfIt()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            Transform ship = context.Ship.transform;
            Vector3 position = ship.position;
            Quaternion rotation = ship.rotation;
            Vector3 velocity = context.Ship.Velocity;

            var visuals = context.Ship.GetComponent<Nebula.Ship.ShipWeaponVisuals>();
            context.Ship.GetComponent<Nebula.Ship.ShipCombat>().Hardpoints[0]
                .Equip(Nebula.Combat.WeaponFamily.Plasma, 1);
            visuals.Refresh();
            Vector3 muzzle = visuals.GetMuzzle(Nebula.Combat.WeaponFamily.Plasma).position;

            for (int i = 0; i < 400; i++) { director.Tick(0.02f); director.TickUnscaled(0.02f); }

            Assert.AreEqual(position, ship.position, "the hull belongs to the flight model");
            Assert.AreEqual(rotation, ship.rotation);
            Assert.AreEqual(velocity, context.Ship.Velocity);
            Assert.AreEqual(muzzle, visuals.GetMuzzle(Nebula.Combat.WeaponFamily.Plasma).position,
                "and the stable gameplay muzzle never moves");
        }

        [UnityTest]
        public IEnumerator ABootedSession_ReplaysNothingItRestored()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 10; i++) yield return null;

            // These were null-guarded, which meant a scene that built none of them
            // passed the test. A booted session must build all four, so the
            // guard is now an assertion.
            var progression = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudNotifications>();
            var attention = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudAlerts>();
            var reserve = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudPlanetContext>();
            var combat = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudVitals>();

            Assert.IsNotNull(progression, "a booted session builds the notification line");
            Assert.IsNotNull(attention, "a booted session builds the alerts widget");
            Assert.IsNotNull(reserve, "a booted session builds the planet context widget");
            Assert.IsNotNull(combat, "a booted session builds the vitals widget");

            Assert.AreEqual(0, progression.LevelUpsCelebrated);
            Assert.AreEqual(0, progression.NotificationsPlayed);
            Assert.AreEqual(0, attention.CardTransitionsShown);
            Assert.AreEqual(0, reserve.ReserveDeltasShown);
            Assert.AreEqual(0, combat.ShieldReactionsShown);
        }

        [UnityTest]
        public IEnumerator RestartCampaign_DuplicatesNothingAndLeaksNoLease()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 8; i++) yield return null;
            Director().Tick(0.1f);

            adapter.RestartCampaign();
            for (int i = 0; i < 10; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<CombatAnimationBridge>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<RaidAnimationPresenter>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<WeaponGimbalPresenter>().Length);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount, "no presenter left waiting");
            Assert.AreEqual(0, Director().SupportOperations.LeasedCount, "and no lease survived");
        }

        [UnityTest]
        public IEnumerator NoStrategicMapObjectCarriesAnAnimationPresenter()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 8; i++) yield return null;
            NebulaContext context = Context();

            GameObject map = context.Presentation.StrategicMapScreen.gameObject;
            Assert.IsNull(map.GetComponent<UiPanelAnimator>(),
                "StrategicMap animation is deferred and must have no presenter");
            Assert.IsNull(map.GetComponent<CelestialAnimationPresenter>());
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
