namespace Nebula.Hangar
{
    /// <summary>VS-015.1 §4: a Collector Mk II's real physical resupply run — ship cargo -&gt; Command Maintenance Reserve, same system only, Mk II only.</summary>
    public enum MaintenanceResupplyPhase { Loading, ToReserve, Unloading, Returning, Completed, Cancelled }

    /// <summary>
    /// VS-015.1 §4: pure state for one Collector Mk II MaintenanceResupply
    /// mission — mutually exclusive with Route/PoiTask/EngineerTask/
    /// EscortState/the single-fetch Mission on the same VehicleAgent, exactly
    /// like every other VS-011+ task type. Physical loading/unloading amounts
    /// are driven by VehicleAgent through the existing HeldItemId/HeldAmount
    /// fields and ResourceTransferService — this class only tracks phase/
    /// target/systemId, never a second cargo authority.
    /// </summary>
    public class MaintenanceResupplyMission
    {
        public string MissionId { get; }
        public string VehicleId { get; }
        public string SystemId { get; }
        public string TargetCommandStructureId { get; }
        public string ItemId { get; }
        public float PlannedAmount { get; }

        public MaintenanceResupplyPhase Phase { get; private set; }

        public MaintenanceResupplyMission(string missionId, string vehicleId, string systemId, string targetCommandStructureId,
            string itemId, float plannedAmount)
        {
            MissionId = missionId; VehicleId = vehicleId; SystemId = systemId;
            TargetCommandStructureId = targetCommandStructureId; ItemId = itemId; PlannedAmount = plannedAmount;
            Phase = MaintenanceResupplyPhase.Loading;
        }

        public void BeginTravel() { if (Phase == MaintenanceResupplyPhase.Loading) Phase = MaintenanceResupplyPhase.ToReserve; }
        public void BeginUnloading() { if (Phase == MaintenanceResupplyPhase.ToReserve) Phase = MaintenanceResupplyPhase.Unloading; }
        public void BeginReturn() { if (Phase == MaintenanceResupplyPhase.Unloading) Phase = MaintenanceResupplyPhase.Returning; }
        public void Complete() => Phase = MaintenanceResupplyPhase.Completed;
        public void Cancel() => Phase = MaintenanceResupplyPhase.Cancelled;
        public bool IsFinished => Phase == MaintenanceResupplyPhase.Completed || Phase == MaintenanceResupplyPhase.Cancelled;

        public static MaintenanceResupplyMission Restore(string missionId, string vehicleId, string systemId, string targetCommandStructureId,
            string itemId, float plannedAmount, MaintenanceResupplyPhase phase)
        {
            var m = new MaintenanceResupplyMission(missionId, vehicleId, systemId, targetCommandStructureId, itemId, plannedAmount);
            m.SetPhase(phase);
            return m;
        }

        private void SetPhase(MaintenanceResupplyPhase phase) => Phase = phase;
    }
}
