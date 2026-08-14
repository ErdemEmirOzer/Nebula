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
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-04.1 in a real session: mounted weapons that follow the target the
    /// ship really acquired, a Looter that shows only cargo it really stole,
    /// and the guarantee that none of it touches the numbers underneath.
    /// </summary>
    public class Ani041CombatMechanicsAnimationPlayModeTests
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
            foreach (string f in Slot1Files())
                slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlotAndAccessibility()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
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
            var director = Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director);
            return director;
        }

        private static EnemyAgent Spawn(NebulaContext context, string enemyId, float forwardMetres)
        {
            EnemyAgent enemy = context.Streaming.CombatEncounter.SpawnEnemy(enemyId,
                context.Ship.transform.position + context.Ship.transform.forward * forwardMetres);
            Assert.IsNotNull(enemy, $"the director must spawn {enemyId}");
            return enemy;
        }

        // --- player weapon gimbals ------------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionInstallsExactlyOneGimbalPresenter()
        {
            yield return LoadSceneFresh();

            Assert.AreEqual(1, Object.FindObjectsOfType<WeaponGimbalPresenter>().Length,
                "a second presenter would fight the first for the same nodes");
        }

        [UnityTest]
        public IEnumerator EquippingAWeapon_GivesItsModelsGimbalToThePresenter()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            var visuals = context.Ship.GetComponent<ShipWeaponVisuals>();
            var gimbals = context.Ship.GetComponent<WeaponGimbalPresenter>();
            Assert.IsNotNull(combat);
            Assert.IsNotNull(visuals);
            Assert.IsNotNull(gimbals);

            // The real equip path, then the real visual refresh.
            combat.Hardpoints[0].Equip(WeaponFamily.Laser, 1);
            visuals.Refresh();
            director.Tick(0.1f);

            Assert.Greater(gimbals.PosedWeaponCount, 0,
                "the Laser model carries part.gimbal_yaw and part.gimbal_pitch");
        }

        [UnityTest]
        public IEnumerator TheGimbalSwings_WithoutTouchingTheHardpointOrTheShip()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            var visuals = context.Ship.GetComponent<ShipWeaponVisuals>();
            combat.Hardpoints[0].Equip(WeaponFamily.Laser, 1);
            visuals.Refresh();

            float heatBefore = combat.Hardpoints[0].Heat;
            Vector3 shipPosition = context.Ship.transform.position;
            Quaternion shipRotation = context.Ship.transform.rotation;

            Spawn(context, EnemyIds.FighterMk1, 300f);
            for (int i = 0; i < 60; i++) director.Tick(0.02f);

            Assert.AreEqual(heatBefore, combat.Hardpoints[0].Heat, 1e-4f,
                "aiming must not add heat — only firing does");
            Assert.IsFalse(combat.Hardpoints[0].Overheated);
            Assert.AreEqual(shipPosition, context.Ship.transform.position, "the ship is not the gimbal's to move");
            Assert.AreEqual(shipRotation, context.Ship.transform.rotation);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AGimbalNeverExceedsTheGamesOwnLimit_HoweverFarOffTheTargetIs()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            var visuals = context.Ship.GetComponent<ShipWeaponVisuals>();
            combat.Hardpoints[0].Equip(WeaponFamily.Laser, 1);
            visuals.Refresh();
            director.Tick(0.1f);

            Transform muzzle = visuals.GetMuzzle(WeaponFamily.Laser);
            Assert.IsNotNull(muzzle, "the Laser model must expose its real muzzle anchor");

            // An enemy directly abeam — far outside the gimbal's reach.
            EnemyAgent abeam = Spawn(context, EnemyIds.FighterMk1, 10f);
            abeam.transform.position = context.Ship.transform.position + context.Ship.transform.right * 400f;

            for (int i = 0; i < 120; i++) director.Tick(0.02f);

            foreach ((WeaponFamily _, GameObject visual) in visuals.MountedWeapons)
            {
                Transform yaw = NebulaVisualResolver.FindAnchor(visual.transform, CombatMechanicsProfiles.GimbalYaw);
                if (yaw == null) continue;
                float swing = Quaternion.Angle(Quaternion.identity, yaw.localRotation);
                Assert.LessOrEqual(swing, WeaponCatalog.LaserGimbalDegrees + 1f,
                    "the model must never swing past the game's own gimbal rule");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MotionOff_StillPointsTheMountWhereItReallyPoints()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            var visuals = context.Ship.GetComponent<ShipWeaponVisuals>();
            var gimbals = context.Ship.GetComponent<WeaponGimbalPresenter>();
            combat.Hardpoints[0].Equip(WeaponFamily.Laser, 1);
            visuals.Refresh();

            AnimationAccessibility.MotionOff = true;
            Spawn(context, EnemyIds.FighterMk1, 300f);
            director.Tick(0.1f);

            Assert.Greater(gimbals.PosedWeaponCount, 0, "the mount is still posed, it just does not swing");
            Assert.IsFalse(gimbals.AnyRecoiling, "nothing has fired");
            yield return null;
        }

        // --- the Looter's real theft ------------------------------------------------

        [UnityTest]
        public IEnumerator ALooterShowsOnlyCargoItReallyStole()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            EnemyAgent looter = Spawn(context, EnemyIds.LooterTransportMk1, 200f);
            var presenter = looter.GetComponent<EnemyCombatAnimationPresenter>();
            Assert.IsNotNull(presenter);
            Assert.IsNotNull(looter.LooterCargo, "a Looter must have a real cargo hold");

            director.Tick(0.1f); // primes: a restored haul is never replayed
            Assert.AreEqual(0, presenter.TheftsShown);

            // A theft that takes nothing shows nothing.
            float refused = looter.LooterCargo.Add("resource.metal", 0f);
            director.Tick(0.1f);
            Assert.AreEqual(0f, refused);
            Assert.AreEqual(0, presenter.TheftsShown, "a zero-amount theft draws no proxy");

            // A theft that really moves cargo does.
            float taken = looter.LooterCargo.Add("resource.metal", 20f);
            Assert.Greater(taken, 0f, "the real cargo path must accept the haul");
            director.Tick(0.1f);

            Assert.AreEqual(1, presenter.TheftsShown, "one representative item, however much was taken");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ALootersHaul_IsUnchangedByWhetherItIsDrawn()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            EnemyAgent drawn = Spawn(context, EnemyIds.LooterTransportMk1, 200f);
            EnemyAgent hidden = Spawn(context, EnemyIds.LooterTransportMk1, 260f);
            director.Tick(0.1f);

            AnimationAccessibility.MotionOff = false;
            float takenDrawn = drawn.LooterCargo.Add("resource.metal", 30f);
            director.Tick(0.1f);

            AnimationAccessibility.MotionOff = true;
            float takenHidden = hidden.LooterCargo.Add("resource.metal", 30f);
            director.Tick(0.1f);

            Assert.AreEqual(takenDrawn, takenHidden, 1e-4f, "the haul must not care whether anyone is watching");
            Assert.AreEqual(drawn.LooterCargo.UsedVolume, hidden.LooterCargo.UsedVolume, 1e-4f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARestoredLooter_NeverReplaysAHaulItAlreadyHad()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            EnemyAgent looter = Spawn(context, EnemyIds.LooterTransportMk1, 200f);
            var presenter = looter.GetComponent<EnemyCombatAnimationPresenter>();

            // The real restore path, before the presenter has ever sampled it.
            looter.RestoreLooterCargo(new[] { ("resource.metal", 40f) });
            for (int i = 0; i < 10; i++) director.Tick(0.05f);

            Assert.AreEqual(0, presenter.TheftsShown,
                "a load must not replay thefts that happened before it");
            yield return null;
        }

        // --- pool and session hygiene ------------------------------------------------

        [UnityTest]
        public IEnumerator TwentyFourEnemiesAndAHundredEvents_LeaveBothPoolsBounded()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            var looters = new List<EnemyAgent>();
            for (int i = 0; i < 24; i++)
            {
                EnemyAgent enemy = Spawn(context, EnemyIds.LooterTransportMk1, 200f + i * 30f);
                looters.Add(enemy);
            }
            director.Tick(0.1f);

            int itemsAfterWarmup = -1;
            for (int cycle = 0; cycle < 100; cycle++)
            {
                foreach (EnemyAgent looter in looters) looter.LooterCargo.Add("resource.metal", 0.5f);
                director.Tick(0.02f);
                if (cycle == 9) itemsAfterWarmup = director.ItemProxies.TotalProxies;
            }

            Assert.AreEqual(itemsAfterWarmup, director.ItemProxies.TotalProxies,
                "100 theft cycles across 24 Looters must not grow the proxy pool");
            Assert.LessOrEqual(director.ItemProxies.LeasedCount,
                director.ItemProxies.CapacityPerItem * director.ItemProxies.DistinctItemCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesOneOfEverything()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            Spawn(adapter.Context, EnemyIds.LooterTransportMk1, 200f);
            yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<WeaponGimbalPresenter>().Length,
                "a restart must not leave a second gimbal presenter");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, EnemyCombatAnimationPresenter.PendingCount);
        }
    }
}
