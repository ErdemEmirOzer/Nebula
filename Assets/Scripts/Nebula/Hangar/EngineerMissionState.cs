namespace Nebula.Hangar
{
    /// <summary>VS-015 §3: what an Engineer mission is working on.</summary>
    /// <summary>VS-015.1 §2: Vehicle is additive (repair of a damaged-but-active vehicle, distinct from DisabledVehicle's Recover) — appended last so existing save-facing ordinals never shift.</summary>
    public enum EngineerTargetKind { Ship, Structure, Drill, SupportStructure, DisabledVehicle, Vehicle }

    /// <summary>VS-015 §3: Repair (structure/drill/support/ship) or Recover (a Disabled vehicle).</summary>
    public enum EngineerWorkKind { Repair, Recover }

    /// <summary>VS-015 §3: which physical stock the mission draws from.</summary>
    public enum EngineerSourceKind { ShipCargo, PlanetMaintenanceReserve }

    /// <summary>
    /// VS-015 §3: pure physical state machine for one Engineer maintenance
    /// mission — SelectSource -&gt; Reserve -&gt; Load -&gt; Launch -&gt; Travel
    /// -&gt; WaitForSafety -&gt; Working (Repair/Recover) -&gt; ReturnUnusedCargo
    /// -&gt; TravelHome -&gt; Dock -&gt; Completed/Cancelled. No result (repair/
    /// recovery HP, resource consumption) is ever applied before Working is
    /// reached — mirrors <see cref="VehicleMissionState"/>/<see cref="Poi.PoiTask"/>'s
    /// "pure phase, physical driver owns movement" split. A vehicle holds at
    /// most one of Mission/Route/PoiTask/EngineerTask/EscortState at a time.
    /// </summary>
    public class EngineerMissionState
    {
        public const float RequiredSafeSeconds = 8f;

        public string MissionId { get; }
        public string VehicleId { get; }
        public string SystemId { get; }
        public EngineerTargetKind TargetKind { get; }
        public string TargetId { get; }
        public EngineerWorkKind WorkKind { get; }
        public EngineerSourceKind SourceKind { get; }
        /// <summary>The processed material chosen for a structure/vehicle repair's resource cost (main-ship repair instead reuses RepairCatalog's own package resources).</summary>
        public string ChosenMaterialId { get; }
        public bool PlayerManuallyStarted { get; }

        public EngineerPhase Phase { get; private set; }
        public float SafeSecondsAccumulated { get; private set; }
        public bool XpAwarded { get; private set; }
        public bool WorkAccepted { get; private set; }

        /// <summary>Reserved cost at mission start (parts + chosen material amounts) — physically returned to the source if unused at cancel or ReturnUnusedCargo.</summary>
        public int ReservedParts { get; private set; }
        public float ReservedMaterialAmount { get; private set; }
        public float ConsumedMaterialAmount { get; private set; }
        public int ConsumedParts { get; private set; }

        /// <summary>VS-015 §3: total HP the mission plans to restore (the field-cap gap computed once at Reserve time) — consumption during Working is proportional to ActualRestoredHpSoFar / PlannedRestoredHp, mirroring ShipRepairService.ConsumeProportional, never a flat per-tick charge.</summary>
        public float PlannedRestoredHp { get; private set; }
        public float ActualRestoredHpSoFar { get; private set; }

        public void SetPlannedRestoredHp(float plannedHp) => PlannedRestoredHp = System.Math.Max(0f, plannedHp);
        public void RecordProgress(float restoredHpDelta) => ActualRestoredHpSoFar += System.Math.Max(0f, restoredHpDelta);
        public void MarkWorkAccepted() => WorkAccepted = true;

        public EngineerMissionState(string missionId, string vehicleId, string systemId, EngineerTargetKind targetKind,
            string targetId, EngineerWorkKind workKind, EngineerSourceKind sourceKind, string chosenMaterialId,
            bool playerManuallyStarted, int reservedParts, float reservedMaterialAmount)
        {
            MissionId = missionId; VehicleId = vehicleId; SystemId = systemId;
            TargetKind = targetKind; TargetId = targetId; WorkKind = workKind; SourceKind = sourceKind;
            ChosenMaterialId = chosenMaterialId; PlayerManuallyStarted = playerManuallyStarted;
            ReservedParts = reservedParts; ReservedMaterialAmount = reservedMaterialAmount;
            Phase = EngineerPhase.Reserve;
        }

        public void BeginLoad() { if (Phase == EngineerPhase.Reserve) Phase = EngineerPhase.Load; }
        public void BeginLaunch() { if (Phase == EngineerPhase.Load) Phase = EngineerPhase.Launch; }
        public void BeginTravel() { if (Phase == EngineerPhase.Launch) Phase = EngineerPhase.Travel; }

        public void ArriveAtTarget()
        {
            if (Phase != EngineerPhase.Travel) return;
            Phase = EngineerPhase.WaitForSafety;
            SafeSecondsAccumulated = 0f;
        }

        /// <summary>Ticks the continuous-safe gate; returns true the instant Working is entered this call. No repair/recovery result exists before that.</summary>
        public bool TickSafety(float dt, bool currentlySafe)
        {
            if (Phase != EngineerPhase.WaitForSafety) return false;
            if (!currentlySafe) { SafeSecondsAccumulated = 0f; return false; }
            SafeSecondsAccumulated += dt;
            if (SafeSecondsAccumulated < RequiredSafeSeconds) return false;
            Phase = EngineerPhase.Working;
            return true;
        }

        /// <summary>Active enemy contact / re-damage resets the trust counter AND pauses in-progress work (falls back to WaitForSafety).</summary>
        public void RegisterThreatEvent()
        {
            if (Phase == EngineerPhase.Working) Phase = EngineerPhase.WaitForSafety;
            if (Phase == EngineerPhase.WaitForSafety) SafeSecondsAccumulated = 0f;
        }

        public void RecordConsumption(int parts, float material)
        {
            ConsumedParts += parts;
            ConsumedMaterialAmount += material;
        }

        public void CompleteWork()
        {
            if (Phase != EngineerPhase.Working) return;
            Phase = EngineerPhase.ReturnUnusedCargo;
        }

        public void BeginTravelHome() { if (Phase == EngineerPhase.ReturnUnusedCargo) Phase = EngineerPhase.TravelHome; }
        public void BeginDock() { if (Phase == EngineerPhase.TravelHome) Phase = EngineerPhase.Dock; }
        public void CompleteMission() { Phase = EngineerPhase.Completed; }
        public void Cancel() { Phase = EngineerPhase.Cancelled; }

        public void MarkXpAwarded() => XpAwarded = true;

        /// <summary>Unused reservation still physically owed back to the source (ship cargo or planet reserve) at cancel/ReturnUnusedCargo.</summary>
        public int UnusedParts => System.Math.Max(0, ReservedParts - ConsumedParts);
        public float UnusedMaterialAmount => System.Math.Max(0f, ReservedMaterialAmount - ConsumedMaterialAmount);

        public bool IsFinished => Phase == EngineerPhase.Completed || Phase == EngineerPhase.Cancelled;

        /// <summary>Restore-only: rebuilds exact save state without re-validating or re-reserving (caller re-derives the reservation separately from remaining cost).</summary>
        public static EngineerMissionState Restore(string missionId, string vehicleId, string systemId, EngineerTargetKind targetKind,
            string targetId, EngineerWorkKind workKind, EngineerSourceKind sourceKind, string chosenMaterialId, bool playerManuallyStarted,
            int reservedParts, float reservedMaterialAmount, int consumedParts, float consumedMaterialAmount,
            EngineerPhase phase, float safeSecondsAccumulated, bool xpAwarded, float plannedRestoredHp, float actualRestoredHpSoFar)
        {
            var state = new EngineerMissionState(missionId, vehicleId, systemId, targetKind, targetId, workKind, sourceKind,
                chosenMaterialId, playerManuallyStarted, reservedParts, reservedMaterialAmount)
            {
                Phase = phase,
                SafeSecondsAccumulated = safeSecondsAccumulated,
                XpAwarded = xpAwarded,
                ConsumedParts = consumedParts,
                ConsumedMaterialAmount = consumedMaterialAmount,
                PlannedRestoredHp = plannedRestoredHp,
                ActualRestoredHpSoFar = actualRestoredHpSoFar
            };
            return state;
        }
    }

    public enum EngineerPhase
    {
        Reserve, Load, Launch, Travel, WaitForSafety, Working,
        ReturnUnusedCargo, TravelHome, Dock, Completed, Cancelled
    }
}
