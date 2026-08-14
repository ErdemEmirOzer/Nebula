using NUnit.Framework;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure physical mission phase machine (VS-007 §3).</summary>
    public class VehicleMissionStateTests
    {
        [Test]
        public void FreshVehicle_StartsInHangarAndCanLaunch()
        {
            var m = new VehicleMissionState();
            Assert.AreEqual(VehiclePhase.InHangar, m.Phase);
            Assert.IsTrue(m.CanLaunch);
        }

        [Test]
        public void Launch_MovesToLaunchingWithMissionData()
        {
            var m = new VehicleMissionState();
            m.Launch(MissionKind.Scout, "sys000/pl01");
            Assert.AreEqual(VehiclePhase.Launching, m.Phase);
            Assert.AreEqual(MissionKind.Scout, m.Kind);
            Assert.AreEqual("sys000/pl01", m.TargetId);
            Assert.IsFalse(m.CanLaunch);
        }

        [Test]
        public void Launch_WhileNotInHangar_IsIgnored()
        {
            var m = new VehicleMissionState();
            m.Launch(MissionKind.Scout, "sys000/pl01");
            m.Launch(MissionKind.Collector, "sys000/pl02"); // already Launching — must be a no-op
            Assert.AreEqual(MissionKind.Scout, m.Kind);
            Assert.AreEqual("sys000/pl01", m.TargetId);
        }

        [Test]
        public void TickTimedPhase_LaunchingBecomesTravelOutAfterLaunchSeconds()
        {
            var m = new VehicleMissionState();
            m.Launch(MissionKind.Scout, "sys000/pl01");

            Assert.IsFalse(m.TickTimedPhase(VehicleMissionState.LaunchSeconds - 0.1f));
            Assert.AreEqual(VehiclePhase.Launching, m.Phase);

            Assert.IsTrue(m.TickTimedPhase(0.2f));
            Assert.AreEqual(VehiclePhase.TravelOut, m.Phase);
        }

        [Test]
        public void FullMissionCycle_ScoutReturnsToHangar()
        {
            var m = new VehicleMissionState();
            m.Launch(MissionKind.Scout, "sys000/pl01");
            m.TickTimedPhase(VehicleMissionState.LaunchSeconds);
            Assert.AreEqual(VehiclePhase.TravelOut, m.Phase);

            m.ArriveAtTarget();
            Assert.AreEqual(VehiclePhase.OnStation, m.Phase);

            m.BeginReturn();
            Assert.AreEqual(VehiclePhase.TravelReturn, m.Phase);

            m.ArriveHome(dockingTargetPresent: true);
            Assert.AreEqual(VehiclePhase.Docking, m.Phase);

            Assert.IsFalse(m.TickTimedPhase(VehicleMissionState.DockSeconds), "Docking's transition is decoupled — TickTimedPhase only ever handles Launching");
            m.AdvancePhaseTimer(VehicleMissionState.DockSeconds);
            Assert.IsTrue(m.DockAnimationDone);
            m.CompleteDocking();

            Assert.AreEqual(VehiclePhase.InHangar, m.Phase);
            Assert.AreEqual(MissionKind.None, m.Kind);
            Assert.IsNull(m.TargetId);
            Assert.IsTrue(m.CanLaunch);
        }

        [Test]
        public void ArriveHome_ShipAbsent_GoesToWaiting_ThenResumes()
        {
            var m = new VehicleMissionState();
            m.Launch(MissionKind.Scout, "sys000/pl01");
            m.TickTimedPhase(VehicleMissionState.LaunchSeconds);
            m.ArriveAtTarget();
            m.BeginReturn();

            m.ArriveHome(dockingTargetPresent: false);
            Assert.AreEqual(VehiclePhase.Waiting, m.Phase);

            m.ResumeFromWaiting();
            Assert.AreEqual(VehiclePhase.TravelReturn, m.Phase);
        }

        [Test]
        public void RequestRecall_WhileTravelingOut_ImmediatelyTurnsBack()
        {
            var m = new VehicleMissionState();
            m.Launch(MissionKind.Collector, "sys000/pl01/drill");
            m.TickTimedPhase(VehicleMissionState.LaunchSeconds);
            Assert.AreEqual(VehiclePhase.TravelOut, m.Phase);

            m.RequestRecall();
            Assert.AreEqual(VehiclePhase.TravelReturn, m.Phase);
            Assert.IsTrue(m.RecallRequested);
        }

        [Test]
        public void RequestRecall_WhileOnStation_TurnsBack()
        {
            var m = new VehicleMissionState();
            m.Launch(MissionKind.Scout, "sys000/pl01");
            m.TickTimedPhase(VehicleMissionState.LaunchSeconds);
            m.ArriveAtTarget();

            m.RequestRecall();
            Assert.AreEqual(VehiclePhase.TravelReturn, m.Phase);
        }

        [Test]
        public void Restore_ReproducesExactSnapshot()
        {
            var m = new VehicleMissionState();
            m.Restore(VehiclePhase.OnStation, MissionKind.Collector, "sys002/pl03", 4.5f, recallRequested: true);

            Assert.AreEqual(VehiclePhase.OnStation, m.Phase);
            Assert.AreEqual(MissionKind.Collector, m.Kind);
            Assert.AreEqual("sys002/pl03", m.TargetId);
            Assert.AreEqual(4.5f, m.PhaseTimer);
            Assert.IsTrue(m.RecallRequested);
        }
    }
}
