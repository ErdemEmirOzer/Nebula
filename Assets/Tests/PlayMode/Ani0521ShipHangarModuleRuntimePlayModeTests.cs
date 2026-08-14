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
using Nebula.Modules;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05.2.2 in a real session: the hangar hatch following real launch and
    /// dock phases, the auxiliary thrusters answering the hull's own turn, the
    /// module mechanics rebuilding cleanly, and an Escort shot drawn only when
    /// the damage was really accepted.
    /// </summary>
    public class Ani0521ShipHangarModuleRuntimePlayModeTests
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

        private static ShipAttitudeAndHangarPresenter ShipMechanics()
        {
            var p = Object.FindObjectOfType<ShipAttitudeAndHangarPresenter>();
            Assert.IsNotNull(p, "the session must install the ship's hangar/attitude presenter");
            return p;
        }

        private static VehicleAgent Spawn(NebulaContext context, string id, VehicleDef def, VehiclePhase phase)
        {
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);
            VehicleAgent agent = hangar.RestoreVehicle(id, def);
            Assert.IsNotNull(agent);
            agent.RestorePhysical(context.StartSystemId, context.Ship.transform.position + Vector3.right * 40f,
                Quaternion.identity, Vector3.zero, phase, MissionKind.None, null, 0f, false, null, 0f);
            return agent;
        }

        // --- installation -------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionInstallsExactlyOneShipMechanicsPresenter()
        {
            yield return LoadSceneFresh();
            Assert.AreEqual(1, Object.FindObjectsOfType<ShipAttitudeAndHangarPresenter>().Length,
                "two presenters would fight over the same hatch");
        }

        // --- §1 hangar door ------------------------------------------------------

        [UnityTest]
        public IEnumerator TheDoorStaysShut_WhileEveryVehicleIsParked()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            Spawn(context, "ani0522-parked", VehicleCatalog.Scout, VehiclePhase.InHangar);
            director.Tick(0.1f);

            Assert.IsFalse(ShipMechanics().DoorShouldBeOpen,
                "a vehicle sitting in the hangar is not an operation");
        }

        [UnityTest]
        public IEnumerator TheDoorOpensForARealLaunch_AndForARealDock()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();
            ShipAttitudeAndHangarPresenter mechanics = ShipMechanics();

            VehicleAgent agent = Spawn(context, "ani0522-launch", VehicleCatalog.Scout, VehiclePhase.Launching);
            director.Tick(0.1f);
            Assert.IsTrue(mechanics.DoorShouldBeOpen, "a real launch opens it");

            agent.RestorePhysical(context.StartSystemId, agent.transform.position, Quaternion.identity,
                Vector3.zero, VehiclePhase.TravelOut, MissionKind.None, null, 0f, false, null, 0f);
            director.Tick(0.1f);
            Assert.IsFalse(mechanics.DoorShouldBeOpen, "and it shuts once the vehicle is away");

            agent.RestorePhysical(context.StartSystemId, agent.transform.position, Quaternion.identity,
                Vector3.zero, VehiclePhase.Docking, MissionKind.None, null, 0f, false, null, 0f);
            director.Tick(0.1f);
            Assert.IsTrue(mechanics.DoorShouldBeOpen, "a real dock opens it again");

            agent.RestorePhysical(context.StartSystemId, agent.transform.position, Quaternion.identity,
                Vector3.zero, VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);
            director.Tick(0.1f);
            Assert.IsFalse(mechanics.DoorShouldBeOpen, "and shuts once it is home");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARefusedLaunch_NeverOpensTheDoor()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            // A vehicle already out in the field cannot launch again: that is
            // the mission state machine's own gate, and it is the real refusal.
            VehicleAgent agent = Spawn(context, "ani0522-refused", VehicleCatalog.Scout, VehiclePhase.TravelOut);
            Assert.IsFalse(agent.Mission.CanLaunch, "the real gate must already be closed");

            director.Tick(0.1f);
            Assert.IsFalse(ShipMechanics().DoorShouldBeOpen, "travelling out is not an operation at the door");

            agent.Launch(MissionKind.Scout, "nowhere", context.StartSystemId);
            director.Tick(0.1f);

            Assert.AreEqual(VehiclePhase.TravelOut, agent.Mission.Phase,
                "the refused launch must not have moved the phase");
            Assert.IsFalse(ShipMechanics().DoorShouldBeOpen,
                "and a refused launch must never open the door");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TwoVehiclesOperating_ShareOneDoorWithoutFighting()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent a = Spawn(context, "ani0522-a", VehicleCatalog.Scout, VehiclePhase.Launching);
            VehicleAgent b = Spawn(context, "ani0522-b", VehicleCatalog.Collector, VehiclePhase.Docking);
            director.Tick(0.1f);
            Assert.IsTrue(ShipMechanics().DoorShouldBeOpen);

            a.RestorePhysical(context.StartSystemId, a.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.TravelOut, MissionKind.None, null, 0f, false, null, 0f);
            director.Tick(0.1f);
            Assert.IsTrue(ShipMechanics().DoorShouldBeOpen,
                "the other vehicle is still docking, so the door stays open");

            b.RestorePhysical(context.StartSystemId, b.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);
            director.Tick(0.1f);
            Assert.IsFalse(ShipMechanics().DoorShouldBeOpen, "and shuts only when neither needs it");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheDoorAnimation_DoesNotChangeHowLongTheMissionTakes()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = Spawn(context, "ani0522-timing", VehicleCatalog.Scout, VehiclePhase.Launching);
            float timerBefore = agent.Mission.PhaseTimer;

            for (int i = 0; i < 200; i++) director.Tick(0.02f);

            Assert.AreEqual(timerBefore, agent.Mission.PhaseTimer, 1e-4f,
                "ticking the animation must not advance the mission's own clock");
            Assert.AreEqual(VehiclePhase.Launching, agent.Mission.Phase,
                "and must not move it to the next phase");
            yield return null;
        }

        // --- §3 auxiliary thrusters ------------------------------------------------

        [UnityTest]
        public IEnumerator TheAuxiliaryThrusters_AnswerTheHullsOwnTurn_AndRestWhenItStops()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();
            ShipAttitudeAndHangarPresenter mechanics = ShipMechanics();

            Transform ship = context.Ship.transform;

            director.Tick(0.02f);
            director.Tick(0.02f);
            Assert.AreEqual(0f, mechanics.AttitudeSignal, 1e-3f, "a ship flying straight deflects nothing");

            // Turn the hull the way the flight model would, and read the answer.
            for (int i = 0; i < 10; i++)
            {
                ship.rotation *= Quaternion.Euler(0f, 3f, 0f);
                director.Tick(0.02f);
            }
            Assert.AreNotEqual(0f, mechanics.AttitudeSignal, "a real turn must register");

            for (int i = 0; i < 10; i++) director.Tick(0.02f);
            Assert.AreEqual(0f, mechanics.AttitudeSignal, 1e-3f, "and it must settle when the turn stops");
        }

        [UnityTest]
        public IEnumerator TheAttitudeAnimation_ChangesNothingAboutTheShip()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            Transform ship = context.Ship.transform;
            Vector3 position = ship.position;
            Quaternion rotation = ship.rotation;
            Vector3 velocity = context.Ship.Velocity;

            for (int i = 0; i < 300; i++) director.Tick(0.02f);

            Assert.AreEqual(position, ship.position, "the hull is the flight model's");
            Assert.AreEqual(rotation, ship.rotation, "and so is its heading");
            Assert.AreEqual(velocity, context.Ship.Velocity, "and its velocity");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MotionOff_ReturnsTheHullMechanismsToTheirAuthoredPose()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();
            ShipAttitudeAndHangarPresenter mechanics = ShipMechanics();

            for (int i = 0; i < 10; i++)
            {
                context.Ship.transform.rotation *= Quaternion.Euler(0f, 3f, 0f);
                director.Tick(0.02f);
            }

            AnimationAccessibility.MotionOff = true;
            director.Tick(0.02f);

            Assert.AreEqual(0f, mechanics.AttitudeSignal, "motion off means no deflection at all");
            yield return null;
        }

        // --- §2 module mechanics -----------------------------------------------------

        [UnityTest]
        public IEnumerator AModuleRebuild_LeavesNoDuplicatePresenter()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();

            var moduleVisuals = context.Ship.GetComponent<ShipModuleVisuals>();
            Assert.IsNotNull(moduleVisuals, "the ship must carry its module visuals");

            for (int i = 0; i < 4; i++) yield return null;

            // Every module body carries at most one mechanical presenter.
            foreach (EconomyMechanicalPresenter presenter
                     in context.Ship.GetComponentsInChildren<EconomyMechanicalPresenter>(true))
            {
                Assert.AreEqual(1, presenter.gameObject.GetComponents<EconomyMechanicalPresenter>().Length,
                    $"{presenter.VisualId} must not have stacked a second presenter");
            }
        }

        // --- §4 escort fire response ----------------------------------------------------

        [UnityTest]
        public IEnumerator AnAcceptedEscortShot_DrawsExactlyOneResponse()
        {
            yield return LoadSceneFresh();
            NebulaAnimationDirector director = Director();
            var bridge = Object.FindObjectOfType<CombatAnimationBridge>();
            Assert.IsNotNull(bridge);

            int before = bridge.EscortShotsShown;

            // The same call the Escort makes once its damage has been applied.
            EscortFireSignal.Raise("ani0522-escort", Vector3.zero, Vector3.forward);
            director.Tick(0.05f);

            Assert.AreEqual(before + 1, bridge.EscortShotsShown, "one accepted shot, one response");
            Assert.AreEqual("ani0522-escort", bridge.LastEscortShotVehicleId);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARefusedEscortShot_DrawsNothing()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();
            var bridge = Object.FindObjectOfType<CombatAnimationBridge>();

            VehicleAgent escort = Spawn(context, "ani0522-outofrange", VehicleCatalog.EscortMk1, VehiclePhase.OnStation);
            var state = new EscortState("ani0522-outofrange", "m", "ship", TargetCategory.Ship, true);
            escort.AssignEscort(state);

            // A target far outside the fixed laser's real range: the resolver
            // refuses the shot before any damage is applied.
            EnemyAgent enemy = context.Streaming.CombatEncounter.SpawnEnemy(EnemyIds.FighterMk1,
                context.Ship.transform.position + context.Ship.transform.forward * 50000f);
            Assert.IsNotNull(enemy);
            state.EngageThreat(enemy.Id);

            int before = bridge.EscortShotsShown;
            float hullBefore = enemy.Combat.Hull;

            for (int i = 0; i < 30; i++) { director.Tick(0.02f); yield return null; }

            Assert.AreEqual(hullBefore, enemy.Combat.Hull, 1e-4f,
                "the shot really was refused — no damage was applied");
            Assert.AreEqual(before, bridge.EscortShotsShown,
                "so no recoil and no muzzle flash may have been drawn");
        }

        // --- session hygiene --------------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesOneOfEverythingAndNoStaleListener()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<ShipAttitudeAndHangarPresenter>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<CombatAnimationBridge>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount);
        }
    }
}
