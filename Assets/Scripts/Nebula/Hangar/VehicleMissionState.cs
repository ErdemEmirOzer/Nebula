using UnityEngine;

namespace Nebula.Hangar
{
    public enum VehiclePhase { InHangar, Launching, TravelOut, OnStation, TravelReturn, Waiting, Docking }
    public enum MissionKind { None, Scout, Collector }

    /// <summary>
    /// Pure phase state machine for one vehicle's physical mission (VS-007
    /// §3). Every real transition (arriving somewhere, docking, recalling)
    /// is driven explicitly by the physical mover (<see cref="VehicleAgent"/>)
    /// once it has actually covered the distance — this class never assumes
    /// distance/time equivalence, so it stays safe across floating-origin
    /// shifts and variable dt. Launching/Docking are short, fixed-duration
    /// placeholder animations; everything else is phase-only (no timers).
    /// </summary>
    public class VehicleMissionState
    {
        public const float LaunchSeconds = 2f;
        public const float DockSeconds = 2f;

        public VehiclePhase Phase { get; private set; } = VehiclePhase.InHangar;
        public MissionKind Kind { get; private set; } = MissionKind.None;
        public string TargetId { get; private set; }
        public float PhaseTimer { get; private set; }
        public bool RecallRequested { get; private set; }

        public bool CanLaunch => Phase == VehiclePhase.InHangar;
        public bool IsIdleInHangar => Phase == VehiclePhase.InHangar;

        public void Launch(MissionKind kind, string targetId)
        {
            if (!CanLaunch) return;
            Kind = kind;
            TargetId = targetId;
            Phase = VehiclePhase.Launching;
            PhaseTimer = 0f;
            RecallRequested = false;
        }

        /// <summary>Advances the short Launching placeholder animation. Returns true the call that crosses LaunchSeconds (Launching -> TravelOut).</summary>
        public bool TickTimedPhase(float dt)
        {
            if (Phase != VehiclePhase.Launching) return false;
            PhaseTimer += dt;
            if (PhaseTimer < LaunchSeconds) return false;
            PhaseTimer = 0f;
            Phase = VehiclePhase.TravelOut;
            return true;
        }

        /// <summary>Docking's timer is decoupled from its transition: a loaded Collector must finish delivering before it may return to InHangar (VS-007 §5).</summary>
        public void AdvancePhaseTimer(float dt)
        {
            if (Phase == VehiclePhase.Docking) PhaseTimer += dt;
        }

        public bool DockAnimationDone => Phase == VehiclePhase.Docking && PhaseTimer >= DockSeconds;

        /// <summary>Call once DockAnimationDone AND any Collector cargo is fully delivered.</summary>
        public void CompleteDocking()
        {
            if (Phase != VehiclePhase.Docking) return;
            Phase = VehiclePhase.InHangar;
            Kind = MissionKind.None;
            TargetId = null;
            RecallRequested = false;
            PhaseTimer = 0f;
        }

        /// <summary>Physically reached the mission target (planet or drill).</summary>
        public void ArriveAtTarget()
        {
            if (Phase == VehiclePhase.TravelOut) Phase = VehiclePhase.OnStation;
        }

        /// <summary>On-station work is done (scan complete, cargo full, or drill exhausted) — start heading back.</summary>
        public void BeginReturn()
        {
            if (Phase == VehiclePhase.OnStation || Phase == VehiclePhase.TravelOut) Phase = VehiclePhase.TravelReturn;
        }

        /// <summary>Physically reached the return point (the ship, or the drill when returning cargo mid-mission).</summary>
        public void ArriveHome(bool dockingTargetPresent)
        {
            if (Phase != VehiclePhase.TravelReturn) return;
            if (dockingTargetPresent) { Phase = VehiclePhase.Docking; PhaseTimer = 0f; }
            else Phase = VehiclePhase.Waiting;
        }

        /// <summary>The thing the vehicle was waiting for (ship back in system) is present again — resume the return leg.</summary>
        public void ResumeFromWaiting()
        {
            if (Phase == VehiclePhase.Waiting) Phase = VehiclePhase.TravelReturn;
        }

        /// <summary>Recall: mid-mission vehicles turn back immediately; already-returning/hangared vehicles are unaffected.</summary>
        public void RequestRecall()
        {
            RecallRequested = true;
            if (Phase == VehiclePhase.TravelOut || Phase == VehiclePhase.OnStation) Phase = VehiclePhase.TravelReturn;
        }

        public void Restore(VehiclePhase phase, MissionKind kind, string targetId, float phaseTimer, bool recallRequested)
        {
            Phase = phase;
            Kind = kind;
            TargetId = targetId;
            PhaseTimer = Mathf.Max(0f, phaseTimer);
            RecallRequested = recallRequested;
        }
    }
}
