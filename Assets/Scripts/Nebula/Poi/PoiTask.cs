namespace Nebula.Poi
{
    /// <summary>A vehicle's current physical leg while working a POI.</summary>
    public enum PoiTaskPhase { ToTarget, Working, Returning }

    /// <summary>
    /// Pure, single-assignment vehicle task for a POI (VS-012 §6/§8) —
    /// mirrors <see cref="Logistics.CollectorRoute"/>'s minimal shape but is
    /// NOT persistent/repeating: once Working finishes (or the player
    /// recalls), the vehicle returns and the task ends. A vehicle may hold
    /// at most one of Mission/Route/PoiTask at a time (mutual exclusion
    /// enforced by <see cref="Hangar.VehicleAgent"/>).
    /// </summary>
    public class PoiTask
    {
        public string PoiId { get; }
        public string VehicleId { get; }
        public string SystemId { get; }
        public PoiTaskPhase Phase { get; private set; } = PoiTaskPhase.ToTarget;
        public bool RecallRequested { get; private set; }

        public PoiTask(string poiId, string vehicleId, string systemId) { PoiId = poiId; VehicleId = vehicleId; SystemId = systemId; }

        public PoiTask(string poiId, string vehicleId, string systemId, PoiTaskPhase phase, bool recallRequested)
        {
            PoiId = poiId; VehicleId = vehicleId; SystemId = systemId; Phase = phase; RecallRequested = recallRequested;
        }

        public void ArriveAtTarget() { if (Phase == PoiTaskPhase.ToTarget) Phase = PoiTaskPhase.Working; }
        public void BeginReturn() { if (Phase != PoiTaskPhase.Returning) Phase = PoiTaskPhase.Returning; }
        public void RequestRecall() { RecallRequested = true; BeginReturn(); }
    }
}
