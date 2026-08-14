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
    /// ANI-03.1 in a real session: effects and item proxies driven by real
    /// transfers, real repairs and the real scan counter — and proof that the
    /// gameplay result is identical whether or not any of it is drawn.
    /// </summary>
    public class Ani031SupportOperationVisualsPlayModeTests
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
            Assert.IsNotNull(director, "the session must install an animation director");
            return director;
        }

        private static VehicleAgent SpawnVehicle(NebulaContext context, string id, VehicleDef def,
            VehiclePhase phase, string heldItemId = null, float heldAmount = 0f)
        {
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);

            VehicleAgent agent = hangar.RestoreVehicle(id, def);
            Assert.IsNotNull(agent, $"the hangar must build {def.Id}");
            agent.RestorePhysical(context.StartSystemId, context.Ship.transform.position + Vector3.right * 30f,
                Quaternion.identity, Vector3.zero, phase, MissionKind.None, null, 0f, false, heldItemId, heldAmount);
            return agent;
        }

        private static SupportOperationPresenter OperationsOf(VehicleAgent agent)
        {
            var presenter = agent.GetComponent<SupportOperationPresenter>();
            Assert.IsNotNull(presenter, "every vehicle must carry an operation presenter");
            return presenter;
        }

        // --- wiring -----------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionInstallsABoundedOperationPool_AndAdoptsEveryPresenter()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            Assert.IsNotNull(director.SupportOperations, "ANI-03.1 must install the operation pool");
            Assert.Greater(director.SupportOperations.CapacityPerVfx, 0);

            SpawnVehicle(context, "ani031-adopt", VehicleCatalog.Scout, VehiclePhase.TravelOut);
            yield return null;
            yield return null;

            Assert.AreEqual(0, SupportOperationPresenter.PendingCount, "the director must drain the adoption queue");
        }

        // --- real transfers ---------------------------------------------------

        [UnityTest]
        public IEnumerator ARealDeliveryToTheShip_ConservesResources_AndShowsOneRepresentativeItem()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            LogisticsCargo shipCargo = context.Ship.GetComponent<ShipExtraction>().Cargo;
            Assert.IsNotNull(shipCargo);

            const string item = "resource.metal";
            VehicleAgent collector = SpawnVehicle(context, "ani031-deliver", VehicleCatalog.Collector,
                VehiclePhase.OnStation, item, 40f);
            SupportOperationPresenter operations = OperationsOf(collector);

            director.Tick(0.1f); // adopt and prime: a restored load is never replayed
            Assert.IsFalse(operations.HasItemInFlight, "a restored cargo is not a transfer");

            float shipBefore = shipCargo.GetStored(item);
            float vehicleBefore = collector.HeldAmount;

            // The real delivery path, through ResourceTransferService.
            collector.TickDeliverToShip(shipCargo, 1f);

            float moved = vehicleBefore - collector.HeldAmount;
            Assert.Greater(moved, 0f, "the real transfer must actually move cargo");
            Assert.AreEqual(shipBefore + moved, shipCargo.GetStored(item), 1e-3f,
                "every unit that left the vehicle must arrive at the ship");

            director.Tick(0.1f);
            Assert.AreEqual(TransferDirection.Outbound, operations.LastTransfer);
            Assert.AreEqual(1, operations.TransfersShown, "one representative item, however large the transfer");
        }

        [UnityTest]
        public IEnumerator ADeliveryThatMovesNothing_ProducesNoProxy()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            LogisticsCargo shipCargo = context.Ship.GetComponent<ShipExtraction>().Cargo;
            VehicleAgent collector = SpawnVehicle(context, "ani031-empty", VehicleCatalog.Collector, VehiclePhase.OnStation);

            SupportOperationPresenter operations = OperationsOf(collector);
            director.Tick(0.1f);

            collector.TickDeliverToShip(shipCargo, 1f); // nothing held: a real no-op
            for (int i = 0; i < 5; i++) director.Tick(0.1f);

            Assert.AreEqual(0, operations.TransfersShown, "an empty hold transfers nothing and shows nothing");
            Assert.IsFalse(operations.HasItemInFlight);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OneRepresentativeItem_HoweverManyUnitsMove()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            LogisticsCargo shipCargo = context.Ship.GetComponent<ShipExtraction>().Cargo;
            VehicleAgent collector = SpawnVehicle(context, "ani031-bulk", VehicleCatalog.CollectorMk2,
                VehiclePhase.OnStation, "resource.metal", 500f);
            SupportOperationPresenter operations = OperationsOf(collector);
            director.Tick(0.1f);

            collector.TickDeliverToShip(shipCargo, 1f);
            director.Tick(0.1f);

            Assert.AreEqual(1, operations.TransfersShown);
            Assert.LessOrEqual(director.ItemProxies.LeasedCount, director.ItemProxies.CapacityPerItem,
                "a large transfer must never become an object storm");
            yield return null;
        }

        // --- engineer ----------------------------------------------------------

        [UnityTest]
        public IEnumerator TheEngineer_ShowsNothingWhileWaitingForSafety_ThenWorksWhenTheHullReallyRises()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent engineer = SpawnVehicle(context, "ani031-engineer", VehicleCatalog.EngineerMk1, VehiclePhase.OnStation);
            var task = new EngineerMissionState("ani031-m", "ani031-engineer", context.StartSystemId,
                EngineerTargetKind.Ship, "ship", EngineerWorkKind.Repair, EngineerSourceKind.ShipCargo, null, true, 0, 0f);
            engineer.AssignEngineerTask(task);
            SupportOperationPresenter operations = OperationsOf(engineer);

            task.ArriveAtTarget();
            Assert.AreEqual(EngineerPhase.WaitForSafety, task.Phase, "the real gate must still be there");

            director.Tick(0.1f);
            Assert.AreEqual(SupportOperationVisual.None, operations.CurrentVisual,
                "waiting for safety draws no work");

            // Run the real eight-second gate; nothing may be drawn while it runs.
            for (int i = 0; i < 8; i++)
            {
                task.TickSafety(1f, currentlySafe: true);
                director.Tick(0.1f);
            }
            Assert.AreEqual(EngineerPhase.Working, task.Phase, "the real gate must open on its own schedule");

            task.RecordProgress(25f); // the real repair authority reports progress
            director.Tick(0.1f);
            Assert.AreEqual(SupportOperationVisual.Repair, operations.CurrentVisual,
                "a hull that is really rising is drawn as work");

            director.Tick(0.1f); // no further progress reported
            Assert.AreEqual(SupportOperationVisual.None, operations.CurrentVisual,
                "a stalled repair stops looking like a working one");
            yield return null;
        }

        [UnityTest]
        public IEnumerator CancellingAnEngineerJob_ClearsTheEffectImmediately()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent engineer = SpawnVehicle(context, "ani031-cancel", VehicleCatalog.EngineerMk2, VehiclePhase.OnStation);
            var task = new EngineerMissionState("ani031-cancel-m", "ani031-cancel", context.StartSystemId,
                EngineerTargetKind.Ship, "ship", EngineerWorkKind.Repair, EngineerSourceKind.ShipCargo, null, true, 0, 0f);
            engineer.AssignEngineerTask(task);
            SupportOperationPresenter operations = OperationsOf(engineer);
            director.Tick(0.1f); // the first tick adopts current progress silently

            task.ArriveAtTarget();
            for (int i = 0; i < 8; i++) task.TickSafety(1f, currentlySafe: true);
            task.RecordProgress(10f);
            director.Tick(0.1f);
            Assert.AreEqual(SupportOperationVisual.Repair, operations.CurrentVisual);

            int leasedDuringWork = director.SupportOperations.LeasedCount;
            Assert.Greater(leasedDuringWork, 0);

            engineer.RequestEngineerCancel();
            director.Tick(0.1f);

            Assert.AreEqual(SupportOperationVisual.None, operations.CurrentVisual, "a cancelled job draws nothing");
            Assert.AreEqual(0, director.SupportOperations.LeasedCount, "and hands its effect straight back");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecoveringADisabledVehicle_DrawsOnTheWreck_NotOnTheEngineer()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent casualty = SpawnVehicle(context, "ani031-casualty", VehicleCatalog.Scout, VehiclePhase.OnStation);
            casualty.transform.position = context.Ship.transform.position + Vector3.forward * 200f;
            casualty.DurabilityRef.ApplyDamage(casualty.Def.Shield + casualty.Def.Armor + casualty.Def.Hull + 500f);
            Assert.IsTrue(casualty.DurabilityRef.Disabled, "the casualty must really be disabled");

            VehicleAgent engineer = SpawnVehicle(context, "ani031-rescue", VehicleCatalog.EngineerMk2, VehiclePhase.OnStation);
            var task = new EngineerMissionState("ani031-rescue-m", "ani031-rescue", context.StartSystemId,
                EngineerTargetKind.DisabledVehicle, "ani031-casualty", EngineerWorkKind.Recover,
                EngineerSourceKind.ShipCargo, null, true, 0, 0f);
            engineer.AssignEngineerTask(task);
            SupportOperationPresenter operations = OperationsOf(engineer);
            director.Tick(0.1f); // the first tick adopts current progress silently

            task.ArriveAtTarget();
            for (int i = 0; i < 8; i++) task.TickSafety(1f, currentlySafe: true);
            task.RecordProgress(15f);
            director.Tick(0.1f);

            Assert.AreEqual(SupportOperationVisual.Recovery, operations.CurrentVisual);
            Assert.AreEqual(SupportOperationVisual.None, OperationsOf(casualty).CurrentVisual,
                "a disabled vehicle draws nothing of its own");

            Assert.Greater(director.SupportOperations.LeasedCount, 0, "the recovery must actually be drawn");

            // The rescue is drawn over the wreck, which is 200 units away.
            Transform drawn = null;
            foreach (Transform child in director.SupportOperations.Root)
                if (child.gameObject.activeSelf) { drawn = child; break; }
            Assert.IsNotNull(drawn, "an active effect quad must exist");

            float toCasualty = Vector3.Distance(drawn.position, casualty.transform.position);
            float toEngineer = Vector3.Distance(drawn.position, engineer.transform.position);
            Assert.Less(toCasualty, toEngineer, "the recovery is drawn on the wreck, not on the engineer");
            yield return null;
        }

        // --- escort ------------------------------------------------------------

        [UnityTest]
        public IEnumerator EscortOrders_AreMarkedFromTheRealSubState()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent escort = SpawnVehicle(context, "ani031-escort", VehicleCatalog.EscortMk1, VehiclePhase.OnStation);
            var state = new EscortState("ani031-escort", "ani031-escort-m", "ship", TargetCategory.Ship, true);
            escort.AssignEscort(state);
            SupportOperationPresenter operations = OperationsOf(escort);

            director.Tick(0.1f);
            Assert.AreEqual(SupportOperationVisual.Protect, operations.CurrentVisual,
                "a standing protection assignment is marked");

            state.ForceRetreat();
            director.Tick(0.1f);
            Assert.AreEqual(SupportOperationVisual.RetreatWarning, operations.CurrentVisual,
                "a real retreat reads as a warning");
            yield return null;
        }

        // --- scout scan ---------------------------------------------------------

        [UnityTest]
        public IEnumerator TheScoutScan_IsDrawnOnlyWhileTheRealSixtySecondCounterAdvances()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            PlanetIdentity planet = null;
            foreach (PlanetIdentity p in ExtractionState.AllPlanets) { planet = p; break; }
            Assert.IsNotNull(planet, "the session must have a real planet to scan");

            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);
            VehicleAgent scout = hangar.RestoreVehicle("ani031-scout", VehicleCatalog.Scout);
            SupportOperationPresenter operations = OperationsOf(scout);

            // A real Scout mission at a real planet.
            scout.Launch(MissionKind.Scout, planet.WorldId, context.StartSystemId);
            Assert.AreEqual(0f, scout.ScanProgress01, "a fresh mission has scanned nothing");

            director.Tick(0.1f);
            Assert.AreEqual(SupportOperationVisual.None, operations.CurrentVisual,
                "nothing is drawn before the counter moves");

            // Let the agent run its own launch animation untouched, then park it
            // on the planet so the real in-range gate opens. The counter is the
            // agent's own; nothing here advances it.
            // Budgeted on the agent's own real launch and travel, not on a frame
            // count: batchmode frame times vary with how much else is running.
            float deadline = Time.realtimeSinceStartup + 25f;
            while (scout.ScanProgress01 <= 0f && Time.realtimeSinceStartup < deadline)
            {
                if (scout.Mission.Phase == VehiclePhase.TravelOut || scout.Mission.Phase == VehiclePhase.OnStation)
                    scout.transform.position = planet.transform.position;
                yield return null;
            }

            Assert.AreEqual(VehiclePhase.OnStation, scout.Mission.Phase, "the scout must have really arrived");
            Assert.Greater(scout.ScanProgress01, 0f, "the real scan counter must have advanced");

            // The director samples on its own rate-limited schedule; give it a
            // few frames of genuinely advancing scan to observe.
            for (int i = 0; i < 20; i++)
            {
                scout.transform.position = planet.transform.position;
                yield return null;
            }

            Assert.AreEqual(SupportOperationVisual.Scan, operations.CurrentVisual,
                "an advancing scan is drawn");
            Assert.Less(scout.ScanProgress01, 1f, "ANI-03.1 must not shorten the real 60-second scan");
        }

        // --- accessibility, load, and pressure ---------------------------------

        [UnityTest]
        public IEnumerator MotionOff_HandsEveryEffectBack()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent escort = SpawnVehicle(context, "ani031-motionoff", VehicleCatalog.EscortMk2, VehiclePhase.OnStation);
            escort.AssignEscort(new EscortState("ani031-motionoff", "m", "ship", TargetCategory.Ship, true));
            director.Tick(0.1f);
            Assert.Greater(director.SupportOperations.LeasedCount, 0);

            AnimationAccessibility.MotionOff = true;
            director.Tick(0.1f);

            Assert.AreEqual(0, director.SupportOperations.LeasedCount, "motion off means nothing is drawn");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TwentyFourVehiclesAndAHundredOperations_LeaveThePoolBounded()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            LogisticsCargo shipCargo = context.Ship.GetComponent<ShipExtraction>().Cargo;

            var fleet = new List<VehicleAgent>();
            for (int i = 0; i < 24; i++)
            {
                VehicleDef def = VehicleCatalog.All[i % VehicleCatalog.All.Length];
                fleet.Add(SpawnVehicle(context, $"ani031-fleet-{i}", def, VehiclePhase.OnStation, "resource.metal", 200f));
            }
            director.Tick(0.1f);

            int quadsAfterWarmup = -1;
            for (int cycle = 0; cycle < 100; cycle++)
            {
                foreach (VehicleAgent v in fleet) v.TickDeliverToShip(shipCargo, 0.05f);
                director.Tick(0.05f);
                if (cycle == 4) quadsAfterWarmup = director.SupportOperations.TotalQuads;
            }

            Assert.AreEqual(quadsAfterWarmup, director.SupportOperations.TotalQuads,
                "100 operation cycles across 24 vehicles must not grow the pool");
            Assert.LessOrEqual(director.SupportOperations.LeasedCount,
                director.SupportOperations.CapacityPerVfx * 7,
                "the ceiling is capacity × the seven shared sprites");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnimationOnOrOff_ProducesTheIdenticalGameplayResult()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();
            LogisticsCargo shipCargo = context.Ship.GetComponent<ShipExtraction>().Cargo;

            const string item = "resource.metal";

            AnimationAccessibility.MotionOff = false;
            VehicleAgent drawn = SpawnVehicle(context, "ani031-drawn", VehicleCatalog.Collector,
                VehiclePhase.OnStation, item, 60f);
            director.Tick(0.1f);
            float shipBeforeDrawn = shipCargo.GetStored(item);
            drawn.TickDeliverToShip(shipCargo, 1f);
            float movedDrawn = shipCargo.GetStored(item) - shipBeforeDrawn;
            director.Tick(0.1f);

            AnimationAccessibility.MotionOff = true;
            VehicleAgent hidden = SpawnVehicle(context, "ani031-hidden", VehicleCatalog.Collector,
                VehiclePhase.OnStation, item, 60f);
            director.Tick(0.1f);
            float shipBeforeHidden = shipCargo.GetStored(item);
            hidden.TickDeliverToShip(shipCargo, 1f);
            float movedHidden = shipCargo.GetStored(item) - shipBeforeHidden;
            director.Tick(0.1f);

            Assert.AreEqual(movedDrawn, movedHidden, 1e-4f,
                "the transfer must not care whether anyone is watching");
            Assert.AreEqual(drawn.HeldAmount, hidden.HeldAmount, 1e-4f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ADockedOrDemotedVehicle_HoldsNoVisuals()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent escort = SpawnVehicle(context, "ani031-demote", VehicleCatalog.EscortMk1, VehiclePhase.OnStation);
            escort.AssignEscort(new EscortState("ani031-demote", "m", "ship", TargetCategory.Ship, true));
            director.Tick(0.1f);
            Assert.Greater(director.SupportOperations.LeasedCount, 0);

            escort.gameObject.SetActive(false); // what a demote or a dock does
            director.Tick(0.1f);

            Assert.AreEqual(0, director.SupportOperations.LeasedCount,
                "an off-stage vehicle holds nothing");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesOneDirector_AndNoStalePresenters()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            SpawnVehicle(adapter.Context, "ani031-restart", VehicleCatalog.EngineerMk1, VehiclePhase.OnStation);
            yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, SupportOperationPresenter.PendingCount,
                "the previous session's presenters must not linger");
            Assert.IsNotNull(Director().SupportOperations, "the new session gets its own pool");
        }
    }
}
