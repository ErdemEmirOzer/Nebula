using System.Collections.Generic;

namespace Nebula.Extraction
{
    public enum EvacuationRejectReason
    {
        None, AlreadyInProgress, AlreadyAbandoned, NotInRange, NotStabilized, ThreatDetected, PlanetContested
    }

    /// <summary>
    /// One in-progress planet evacuation (VS-010 §5): after locking new
    /// submissions and resolving in-flight builds/resources, physically
    /// recalled vehicles must actually dock (<see cref="PendingVehicleIds"/>)
    /// before the planet is passivated — never finalized early.
    /// </summary>
    public class PlanetEvacuationTransaction
    {
        public string PlanetWorldId;
        public List<string> PendingVehicleIds = new List<string>();
        public bool Finalized;
        public bool Cancelled;

        public bool AllVehiclesReturned => PendingVehicleIds.Count == 0;
    }

    public enum ReclaimRejectReason { None, NotAbandoned, DominanceConditionsNotMet }

    /// <summary>
    /// Pure precondition/transaction logic for physical planet evacuation and
    /// its reverse, reclaim. Never deletes a structure/node/durability/inventory
    /// value, never re-charges an existing structure, never awards XP.
    /// </summary>
    public static class PlanetEvacuationService
    {
        public static EvacuationRejectReason CheckPreconditions(bool alreadyInProgress, bool alreadyAbandoned,
            bool inRange, bool orbitStabilized, bool threatDetected, bool planetContested)
        {
            if (alreadyInProgress) return EvacuationRejectReason.AlreadyInProgress;
            if (alreadyAbandoned) return EvacuationRejectReason.AlreadyAbandoned;
            if (!inRange) return EvacuationRejectReason.NotInRange;
            if (!orbitStabilized) return EvacuationRejectReason.NotStabilized;
            if (threatDetected) return EvacuationRejectReason.ThreatDetected;
            if (planetContested) return EvacuationRejectReason.PlanetContested;
            return EvacuationRejectReason.None;
        }

        /// <summary>Step 2: cancels every not-yet-Active drill/structure on the planet using their existing 75%-refund cancel rule — never touches an already-Active one.</summary>
        public static void ResolveActiveBuilds(string planetWorldId, LogisticsCargo cargo)
        {
            foreach (DrillStructure d in new List<DrillStructure>(ExtractionState.Drills))
                if (d != null && d.WorldId == planetWorldId && d.Runtime.CanCancelForKitReturn) d.Cancel(cargo);
            foreach (SurfaceStructure s in new List<SurfaceStructure>(ExtractionState.Structures))
                if (s != null && s.WorldId == planetWorldId && s.Runtime.CanCancelForRefund) s.Cancel(cargo);
        }

        /// <summary>Step 4: best-effort auto-transfer of every active drill's stored output into the ship's cargo. Whatever doesn't fit is deposited straight back — it simply stays on the planet (step 5), never lost.</summary>
        public static void AutoTransferResources(string planetWorldId, LogisticsCargo shipCargo)
        {
            foreach (DrillStructure d in ExtractionState.Drills)
            {
                if (d == null || d.WorldId != planetWorldId) continue;
                if (d.Runtime.StoredAmount <= 0.0001f) continue;
                float amount = d.Runtime.StoredAmount;
                float taken = d.Runtime.Withdraw(amount);
                float delivered = shipCargo != null ? shipCargo.Add(ExtractionCatalog.ResourceIds[(int)d.Runtime.Family], taken) : 0f;
                float leftover = taken - delivered;
                if (leftover > 0f) d.Runtime.Deposit(leftover);
            }
        }

        /// <summary>Step 6: passivates the planet without touching any structure/node/durability/inventory value.</summary>
        public static void Passivate(string planetWorldId) => ExtractionState.GetDominance(planetWorldId).SetAbandoned(true);

        public static ReclaimRejectReason CheckReclaimPreconditions(bool isAbandoned, bool dominanceConditionsMet)
        {
            if (!isAbandoned) return ReclaimRejectReason.NotAbandoned;
            if (!dominanceConditionsMet) return ReclaimRejectReason.DominanceConditionsNotMet;
            return ReclaimRejectReason.None;
        }

        /// <summary>Step 9: clears Abandoned and forces a cold restart so the very next tick requires a fresh 120s stabilization — existing structures are never re-charged (step 10); if damaged they still need real repair, unaffected by this call.</summary>
        public static void Reclaim(string planetWorldId) => ExtractionState.GetDominance(planetWorldId).Reclaim();
    }
}
