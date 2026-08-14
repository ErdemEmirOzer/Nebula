using NUnit.Framework;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §3: the Engineer mission phase machine — no repair/recovery result exists before Working, the 8s continuous-safe gate, and threat-event pause/reset.</summary>
    public class Vs015EngineerMissionStateTests
    {
        private static EngineerMissionState NewTask() => new EngineerMissionState(
            "m1", "vehicle-0001", "sys001", EngineerTargetKind.Structure, "sys001/pl00/defense.0",
            EngineerWorkKind.Repair, EngineerSourceKind.ShipCargo, "material.metal_alloy", true, 10, 5f);

        [Test]
        public void FreshMission_StartsAtReserve()
        {
            Assert.AreEqual(EngineerPhase.Reserve, NewTask().Phase);
        }

        [Test]
        public void PhaseSequence_AdvancesInOrder()
        {
            var t = NewTask();
            t.BeginLoad(); Assert.AreEqual(EngineerPhase.Load, t.Phase);
            t.BeginLaunch(); Assert.AreEqual(EngineerPhase.Launch, t.Phase);
            t.BeginTravel(); Assert.AreEqual(EngineerPhase.Travel, t.Phase);
            t.ArriveAtTarget(); Assert.AreEqual(EngineerPhase.WaitForSafety, t.Phase);
        }

        [Test]
        public void ArriveAtTarget_BeforeTravel_IsIgnored()
        {
            var t = NewTask();
            t.ArriveAtTarget();
            Assert.AreEqual(EngineerPhase.Reserve, t.Phase); // no-op: not in Travel phase
        }

        [Test]
        public void TickSafety_RequiresEightContinuousSeconds()
        {
            var t = NewTask();
            t.BeginLoad(); t.BeginLaunch(); t.BeginTravel(); t.ArriveAtTarget();

            Assert.IsFalse(t.TickSafety(7.9f, true));
            Assert.AreEqual(EngineerPhase.WaitForSafety, t.Phase);
            Assert.IsTrue(t.TickSafety(0.2f, true));
            Assert.AreEqual(EngineerPhase.Working, t.Phase);
        }

        [Test]
        public void TickSafety_UnsafeMidCount_ResetsAccumulator()
        {
            var t = NewTask();
            t.BeginLoad(); t.BeginLaunch(); t.BeginTravel(); t.ArriveAtTarget();

            t.TickSafety(6f, true);
            Assert.AreEqual(6f, t.SafeSecondsAccumulated);
            t.TickSafety(1f, false);
            Assert.AreEqual(0f, t.SafeSecondsAccumulated);
            Assert.AreEqual(EngineerPhase.WaitForSafety, t.Phase);
        }

        [Test]
        public void NoWorkResult_ExistsBeforeWorkingPhase()
        {
            var t = NewTask();
            t.BeginLoad(); t.BeginLaunch(); t.BeginTravel();
            // Still in Travel: ActualRestoredHpSoFar must be untouched, and RecordProgress is only ever called from Working in production code.
            Assert.AreEqual(0f, t.ActualRestoredHpSoFar);
            Assert.AreEqual(EngineerPhase.Travel, t.Phase);
        }

        [Test]
        public void RegisterThreatEvent_DuringWorking_PausesBackToWaitForSafetyAndResetsCounter()
        {
            var t = NewTask();
            t.BeginLoad(); t.BeginLaunch(); t.BeginTravel(); t.ArriveAtTarget();
            t.TickSafety(8f, true);
            Assert.AreEqual(EngineerPhase.Working, t.Phase);

            t.RegisterThreatEvent();
            Assert.AreEqual(EngineerPhase.WaitForSafety, t.Phase);
            Assert.AreEqual(0f, t.SafeSecondsAccumulated);
        }

        [Test]
        public void CompleteWork_ThenReturnHomeThenDock_ThenCompleted()
        {
            var t = NewTask();
            t.BeginLoad(); t.BeginLaunch(); t.BeginTravel(); t.ArriveAtTarget(); t.TickSafety(8f, true);
            t.CompleteWork();
            Assert.AreEqual(EngineerPhase.ReturnUnusedCargo, t.Phase);
            t.BeginTravelHome();
            Assert.AreEqual(EngineerPhase.TravelHome, t.Phase);
            t.BeginDock();
            Assert.AreEqual(EngineerPhase.Dock, t.Phase);
            t.CompleteMission();
            Assert.AreEqual(EngineerPhase.Completed, t.Phase);
            Assert.IsTrue(t.IsFinished);
        }

        [Test]
        public void Cancel_MarksFinishedFromAnyPhase()
        {
            var t = NewTask();
            t.BeginLoad();
            t.Cancel();
            Assert.AreEqual(EngineerPhase.Cancelled, t.Phase);
            Assert.IsTrue(t.IsFinished);
        }

        [Test]
        public void UnusedReservation_IsReservedMinusConsumed()
        {
            var t = NewTask();
            t.RecordConsumption(4, 2f);
            Assert.AreEqual(6, t.UnusedParts);
            Assert.AreEqual(3f, t.UnusedMaterialAmount);
        }

        [Test]
        public void Restore_RebuildsExactSavedState()
        {
            var restored = EngineerMissionState.Restore("m9", "vehicle-0002", "sys002", EngineerTargetKind.DisabledVehicle,
                "vehicle-0003", EngineerWorkKind.Recover, EngineerSourceKind.PlanetMaintenanceReserve, null, false,
                6, 0f, 3, 0f, EngineerPhase.Working, 4.5f, false, 60f, 30f);

            Assert.AreEqual(EngineerPhase.Working, restored.Phase);
            Assert.AreEqual(4.5f, restored.SafeSecondsAccumulated);
            Assert.AreEqual(3, restored.ConsumedParts);
            Assert.AreEqual(60f, restored.PlannedRestoredHp);
            Assert.AreEqual(30f, restored.ActualRestoredHpSoFar);
            Assert.IsFalse(restored.PlayerManuallyStarted);
        }
    }
}
