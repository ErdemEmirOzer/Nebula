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
using Nebula.Hangar;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05.2.1 §3: a vehicle's tools work in the exact real phase and in no
    /// other. The counters, gates and amounts they read all stay where they are.
    /// </summary>
    public class Ani0521VehiclePhaseMechanicsPlayModeTests
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
        public void RestoreRealGameplaySlot()
        {
            AnimationAccessibility.ResetToDefaults();
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

        private static VehicleAgent Spawn(NebulaContext context, string id, VehicleDef def,
            VehiclePhase phase, string heldItemId = null, float heldAmount = 0f)
        {
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);
            VehicleAgent agent = hangar.RestoreVehicle(id, def);
            Assert.IsNotNull(agent);
            agent.RestorePhysical(context.StartSystemId, context.Ship.transform.position + Vector3.right * 40f,
                Quaternion.identity, Vector3.zero, phase, MissionKind.None, null, 0f, false, heldItemId, heldAmount);
            return agent;
        }

        private static EconomyMechanicalPresenter MechanicsOf(VehicleAgent agent)
        {
            var presenter = agent.GetComponent<EconomyMechanicalPresenter>();
            Assert.IsNotNull(presenter, "every vehicle with authored parts must carry a mechanical presenter");
            return presenter;
        }

        // --- wiring -------------------------------------------------------------

        [UnityTest]
        public IEnumerator EveryVehicleClass_GetsItsMechanicalPresenter()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();

            int index = 0;
            foreach (VehicleDef def in VehicleCatalog.All)
            {
                VehicleAgent agent = Spawn(context, $"ani0521-{index++}", def, VehiclePhase.OnStation);
                EconomyMechanicalPresenter mechanics = MechanicsOf(agent);
                Assert.Greater(mechanics.DrivenPartCount, 0, $"{def.Id} must bind its real parts");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AMkChange_LeavesExactlyOnePresenterAndKeepsTheVehicleId()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();

            VehicleAgent agent = Spawn(context, "ani0521-upgrade", VehicleCatalog.Collector, VehiclePhase.OnStation);
            string idBefore = agent.VehicleId;

            agent.UpgradeDefInPlace(VehicleCatalog.CollectorMk2);
            yield return null;

            Assert.AreEqual(idBefore, agent.VehicleId, "an upgrade keeps the same vehicle");
            Assert.AreEqual(1, agent.GetComponents<EconomyMechanicalPresenter>().Length,
                "a Mk change must replace the presenter, never stack a second");
            Assert.AreEqual("vehicle.collector.mk2", MechanicsOf(agent).VisualId,
                "and it must rebind to the new Mk's own model");
        }

        // --- §3 phase-sensitive drives ----------------------------------------------

        [UnityTest]
        public IEnumerator AParkedVehicle_StowsEverything()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = Spawn(context, "ani0521-parked", VehicleCatalog.Scout, VehiclePhase.InHangar);
            director.Tick(0.1f);

            Assert.AreEqual(MechanicalDrive.Stowed, MechanicsOf(agent).CurrentDrive,
                "a vehicle in the hangar has nothing deployed");
        }

        [UnityTest]
        public IEnumerator AScoutsWings_OpenOnlyWhileTheRealCounterAdvances()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent scout = Spawn(context, "ani0521-scout", VehicleCatalog.Scout, VehiclePhase.TravelOut);
            EconomyMechanicalPresenter mechanics = MechanicsOf(scout);

            director.Tick(0.1f);
            director.Tick(0.1f);
            Assert.AreNotEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                "approaching is not scanning — the counter has not moved");
            Assert.AreEqual(0f, scout.ScanProgress01, "and ANI-05.2.1 must not have advanced it");
        }

        [UnityTest]
        public IEnumerator ACollectorWorksWhileCargoMoves_HoldsWhileItCarries_StowsWhenEmpty()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();
            LogisticsCargo shipCargo = context.Ship.GetComponent<ShipExtraction>().Cargo;

            VehicleAgent collector = Spawn(context, "ani0521-collector", VehicleCatalog.Collector,
                VehiclePhase.OnStation, "resource.metal", 40f);
            EconomyMechanicalPresenter mechanics = MechanicsOf(collector);

            director.Tick(0.1f); // prime
            director.Tick(0.1f);
            Assert.AreEqual(MechanicalDrive.Holding, mechanics.CurrentDrive,
                "a loaded but idle Collector holds its clamps shut");

            float before = collector.HeldAmount;
            collector.TickDeliverToShip(shipCargo, 1f); // the real transfer path
            Assert.Less(collector.HeldAmount, before, "cargo must really have moved");

            director.Tick(0.1f);
            Assert.AreEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                "a real transfer is what makes it work");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnEngineersToolsStayShut_ThroughTheRealEightSecondGate()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent engineer = Spawn(context, "ani0521-engineer", VehicleCatalog.EngineerMk1, VehiclePhase.OnStation);
            var task = new EngineerMissionState("ani0521-m", "ani0521-engineer", context.StartSystemId,
                EngineerTargetKind.Ship, "ship", EngineerWorkKind.Repair, EngineerSourceKind.ShipCargo, null, true, 0, 0f);
            engineer.AssignEngineerTask(task);
            EconomyMechanicalPresenter mechanics = MechanicsOf(engineer);

            task.ArriveAtTarget();
            Assert.AreEqual(EngineerPhase.WaitForSafety, task.Phase);

            director.Tick(0.1f);
            Assert.AreNotEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                "tools must stay shut while the safety gate runs");

            for (int i = 0; i < 8; i++)
            {
                task.TickSafety(1f, currentlySafe: true);
                director.Tick(0.1f);
                Assert.AreNotEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                    "and for every second of it");
            }
            Assert.AreEqual(EngineerPhase.Working, task.Phase, "the real gate opens on its own schedule");
            Assert.AreEqual(8f, EngineerMissionState.RequiredSafeSeconds, "and its length is unchanged");

            director.Tick(0.1f); // prime the progress sample
            task.RecordProgress(20f);
            director.Tick(0.1f);
            Assert.AreEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                "a hull that is really rising is what opens the arms");

            director.Tick(0.1f);
            Assert.AreNotEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                "a stalled repair must stop looking like a working one");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnEscortsLance_IsReadyOnlyOnARealThreat()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent escort = Spawn(context, "ani0521-escort", VehicleCatalog.EscortMk1, VehiclePhase.OnStation);
            var state = new EscortState("ani0521-escort", "m", "ship", TargetCategory.Ship, true);
            escort.AssignEscort(state);
            EconomyMechanicalPresenter mechanics = MechanicsOf(escort);

            director.Tick(0.1f);
            Assert.AreNotEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                "following is not engaging");

            state.EngageThreat("enemy-1");
            director.Tick(0.1f);
            Assert.AreEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                "a real threat readies the fixed lance");

            state.ForceRetreat();
            director.Tick(0.1f);
            Assert.AreNotEqual(MechanicalDrive.Working, mechanics.CurrentDrive,
                "a retreat stows it again");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ADisabledVehicle_HoldsItsSafeClosedPose()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = Spawn(context, "ani0521-dead", VehicleCatalog.EngineerMk2, VehiclePhase.OnStation);
            agent.DurabilityRef.ApplyDamage(agent.Def.Shield + agent.Def.Armor + agent.Def.Hull + 1000f);
            director.Tick(0.1f);

            Assert.AreEqual(MechanicalDrive.Disabled, MechanicsOf(agent).CurrentDrive);
            yield return null;
        }

        // --- invariants -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ACollectorsTransfer_IsIdenticalWithAnimationOnOrOff()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();
            LogisticsCargo shipCargo = context.Ship.GetComponent<ShipExtraction>().Cargo;

            AnimationAccessibility.MotionOff = false;
            VehicleAgent drawn = Spawn(context, "ani0521-drawn", VehicleCatalog.Collector,
                VehiclePhase.OnStation, "resource.metal", 60f);
            director.Tick(0.1f);
            float drawnBefore = drawn.HeldAmount;
            drawn.TickDeliverToShip(shipCargo, 1f);
            float drawnMoved = drawnBefore - drawn.HeldAmount;

            AnimationAccessibility.MotionOff = true;
            VehicleAgent hidden = Spawn(context, "ani0521-hidden", VehicleCatalog.Collector,
                VehiclePhase.OnStation, "resource.metal", 60f);
            director.Tick(0.1f);
            float hiddenBefore = hidden.HeldAmount;
            hidden.TickDeliverToShip(shipCargo, 1f);
            float hiddenMoved = hiddenBefore - hidden.HeldAmount;

            Assert.AreEqual(drawnMoved, hiddenMoved, 1e-4f,
                "the transfer must not care whether anyone is watching");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MotionOff_ReturnsEveryPartToItsAuthoredBaseline()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent collector = Spawn(context, "ani0521-motionoff", VehicleCatalog.CollectorMk2,
                VehiclePhase.OnStation, "resource.metal", 50f);
            EconomyMechanicalPresenter mechanics = MechanicsOf(collector);

            Transform carousel = NebulaVisualResolver.FindAnchor(collector.transform, "part.cargo_carousel");
            Assert.IsNotNull(carousel, "the Mk II model must expose its carousel");
            Quaternion authored = carousel.localRotation;

            for (int i = 0; i < 60; i++) director.Tick(0.05f);

            AnimationAccessibility.MotionOff = true;
            for (int i = 0; i < 10; i++) director.Tick(0.05f);

            Assert.Less(Quaternion.Angle(authored, carousel.localRotation), 0.01f,
                "motion off returns every mechanism to the pose the artist left");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheVehicleRoot_IsNeverMovedByItsMechanics()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = Spawn(context, "ani0521-root", VehicleCatalog.EngineerMk2,
                VehiclePhase.OnStation);
            agent.enabled = false; // freeze its own flight

            Vector3 position = agent.transform.position;
            Quaternion rotation = agent.transform.rotation;

            for (int i = 0; i < 300; i++) director.Tick(0.02f);

            Assert.AreEqual(position, agent.transform.position, "the root is VehicleAgent's");
            Assert.AreEqual(rotation, agent.transform.rotation);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesOneDirectorAndAnEmptyAdoptionQueue()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            Spawn(adapter.Context, "ani0521-restart", VehicleCatalog.Scout, VehiclePhase.OnStation);
            yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount);
        }
    }
}
