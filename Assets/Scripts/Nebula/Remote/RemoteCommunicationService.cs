using System.Collections.Generic;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Sectors;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A §9: ownership (does this system's state exist/tick) is
    /// entirely separate from communication (can the player currently see
    /// live results from it) -- this class only ever answers the latter,
    /// reusing <see cref="World.SystemStatusQuery.Link"/> (built in VS-010,
    /// already a pure Live/LastKnown/Disconnected resolver over the exact
    /// same dual live/delta state) rather than a second link authority.
    /// Never a MonoBehaviour, never UI -- a future Strategic Map is the only
    /// intended caller, and this round doesn't wire one up.
    /// </summary>
    public static class RemoteCommunicationService
    {
        /// <summary>Per-system last-observed link state, purely so a reconnect (Disconnected/LastKnown -> Live) can be detected and produce exactly one merged summary -- never itself part of the save (link state is always re-derived, per spec: "Link state save'e yazılmasın").</summary>
        private static readonly Dictionary<string, SystemLinkState> lastObservedLink = new Dictionary<string, SystemLinkState>();

        public static void ResetForSession() => lastObservedLink.Clear();

        /// <summary>The current link state -- Live/LastKnown/Disconnected -- purely derived, never persisted. Also detects a reconnect transition and produces the single merged summary the spec requires (§9), stored on the delta itself so it survives until shown.</summary>
        public static SystemLinkState GetLink(string systemId, string shipCurrentSystemId, SystemKnowledgeService knowledge)
        {
            SystemLinkState current = World.SystemStatusQuery.Link(systemId, shipCurrentSystemId, knowledge);
            SystemLinkState previous = lastObservedLink.TryGetValue(systemId, out SystemLinkState p) ? p : SystemLinkState.Disconnected;

            if (previous != SystemLinkState.Live && current == SystemLinkState.Live)
                TryBuildReconnectSummary(systemId);

            lastObservedLink[systemId] = current;
            return current;
        }

        private static void TryBuildReconnectSummary(string systemId)
        {
            SystemDelta delta = World.StarSystemRegistry.Deltas.TryGet(systemId);
            if (delta == null || delta.pendingReconnectSummary != null) return; // one at a time -- never overwrites an unshown summary

            var summary = new ReconnectSummaryDto { systemId = systemId };

            if (delta.threatPressure != null)
            {
                var stage = delta.threatPressure.stage >= 0 ? (Combat.RaidStage?)delta.threatPressure.stage : null;
                if (stage.HasValue && delta.threatPressure.resultApplied)
                    summary.raidResultText = $"Raid resolved: {(Combat.RaidStage)stage.Value}, target {delta.threatPressure.targetPlanetWorldId}";
                if (delta.threatPressure.anyResourceStolen) summary.wasDamagedOrDisabled = true;
            }

            foreach (StructureDto s in delta.structures)
                if (s.state == (int)DrillLifecycle.Disabled) summary.wasDamagedOrDisabled = true;
            foreach (VehicleDto v in delta.vehicles)
            {
                if (v.stranded) summary.wasDamagedOrDisabled = true;
                if (v.engineerTask != null && v.engineerTask.active && (Hangar.EngineerPhase)v.engineerTask.phase == Hangar.EngineerPhase.ReturnUnusedCargo)
                    summary.completedTaskDescriptions.Add($"Engineer {v.vehicleId} finished its mission");
                if (v.resupplyTask != null && v.resupplyTask.active && (Hangar.MaintenanceResupplyPhase)v.resupplyTask.phase == Hangar.MaintenanceResupplyPhase.Returning)
                    summary.completedTaskDescriptions.Add($"Collector {v.vehicleId} finished resupply");
            }

            foreach (CollectorRouteDto r in delta.collectorRoutes)
                if (r.waitReason != (int)Logistics.RouteWaitReason.None) summary.stalledTaskDescriptions.Add($"Route {r.routeId}: {(Logistics.RouteWaitReason)r.waitReason}");

            if (delta.logisticsCenters.Count > 0)
            {
                LogisticsCenterDto center = delta.logisticsCenters[0];
                summary.logisticsMaintenanceStatusText = $"Logistics Center T{center.tier}: {(Logistics.LogisticsCenterState)center.state}";
            }

            delta.pendingReconnectSummary = summary;
        }

        /// <summary>Consumes (and clears) this system's pending reconnect summary -- returns null if none is pending. Opening it awards no XP/reward; the same summary is never returned twice, including across a save/load (cleared here, not merely marked-read).</summary>
        public static ReconnectSummaryDto ConsumeReconnectSummary(string systemId)
        {
            SystemDelta delta = World.StarSystemRegistry.Deltas.TryGet(systemId);
            if (delta?.pendingReconnectSummary == null) return null;
            ReconnectSummaryDto summary = delta.pendingReconnectSummary;
            delta.pendingReconnectSummary = null;
            return summary;
        }
    }
}
