using System;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A §10: the public command surface a future Strategic Map may
    /// call for a Live-linked remote system -- no UI this round. Every
    /// method returns a typed, testable reject reason and mutates only the
    /// SystemDelta's own DTOs (the exact same fields RemoteEconomyTick/
    /// RemoteRaidTick already read), never a live GameObject, never a
    /// second command authority for anything already commandable in a Full
    /// system. Disconnected/LastKnown always rejects every NEW order --
    /// only a policy already set while Live keeps running (RemoteEconomyTick
    /// keeps applying it every remote tick regardless of link state).
    /// </summary>
    public enum RemoteCommandRejectReason
    {
        None,
        Disconnected,
        NotFound,
        NotAllowedRemotely
    }

    public static class RemoteCommandService
    {
        /// <summary>Presentation-only result seam; gameplay callers remain typed and unchanged.</summary>
        public static event Action<RemoteCommandRejectReason> CommandCompleted;
        public static void ResetForSession() => CommandCompleted = null;
        private static RemoteCommandRejectReason Report(RemoteCommandRejectReason result)
        {
            CommandCompleted?.Invoke(result);
            return result;
        }
        private static RemoteCommandRejectReason RequireLive(SystemLinkState link) =>
            link == SystemLinkState.Live ? RemoteCommandRejectReason.None : RemoteCommandRejectReason.Disconnected;

        // --- Allowed (§10) -----------------------------------------------------------
        public static RemoteCommandRejectReason SetDefenseCoordination(SystemDelta delta, SystemLinkState link, string worldId, Combat.DefenseMode mode, string focusEnemyId)
        {
            RemoteCommandRejectReason gate = RequireLive(link);
            if (gate != RemoteCommandRejectReason.None) return Report(gate);
            for (int i = 0; i < delta.defenseCoordination.Count; i++)
            {
                if (delta.defenseCoordination[i].worldId != worldId) continue;
                delta.defenseCoordination[i].mode = (int)mode;
                delta.defenseCoordination[i].focusEnemyId = mode == Combat.DefenseMode.FocusTarget ? focusEnemyId : null;
                return Report(RemoteCommandRejectReason.None);
            }
            delta.defenseCoordination.Add(new DefenseCoordinationDto { worldId = worldId, mode = (int)mode, focusEnemyId = mode == Combat.DefenseMode.FocusTarget ? focusEnemyId : null });
            return Report(RemoteCommandRejectReason.None);
        }

        public static RemoteCommandRejectReason SetInterventionPolicy(SystemDelta delta, SystemLinkState link, Combat.InterventionPolicy policy)
        {
            RemoteCommandRejectReason gate = RequireLive(link);
            if (gate != RemoteCommandRejectReason.None) return Report(gate);
            delta.interventionGroup ??= new InterventionGroupDto();
            delta.interventionGroup.policy = (int)policy;
            return Report(RemoteCommandRejectReason.None);
        }

        /// <summary>Recalls every same-system InterventionGroup member -- clears the group's active target/holding state; the member vehicles themselves finish their current physical leg then idle (no teleport home).</summary>
        public static RemoteCommandRejectReason RecallInterventionGroup(SystemDelta delta, SystemLinkState link)
        {
            RemoteCommandRejectReason gate = RequireLive(link);
            if (gate != RemoteCommandRejectReason.None) return Report(gate);
            if (delta.interventionGroup == null) return Report(RemoteCommandRejectReason.NotFound);
            delta.interventionGroup.activeTargetPlanetId = null;
            delta.interventionGroup.holdingOrRetreating = false;
            delta.interventionGroup.policy = (int)Combat.InterventionPolicy.Manual;
            return Report(RemoteCommandRejectReason.None);
        }

        public static RemoteCommandRejectReason SetRoutePaused(SystemDelta delta, SystemLinkState link, string routeId, bool paused)
        {
            RemoteCommandRejectReason gate = RequireLive(link);
            if (gate != RemoteCommandRejectReason.None) return Report(gate);
            foreach (CollectorRouteDto r in delta.collectorRoutes)
            {
                if (r.routeId != routeId) continue;
                if (paused)
                {
                    if ((Logistics.RoutePhase)r.phase != Logistics.RoutePhase.Paused) { r.resumePhase = r.phase; r.phase = (int)Logistics.RoutePhase.Paused; }
                }
                else if ((Logistics.RoutePhase)r.phase == Logistics.RoutePhase.Paused) r.phase = r.resumePhase;
                return Report(RemoteCommandRejectReason.None);
            }
            return Report(RemoteCommandRejectReason.NotFound);
        }

        public static RemoteCommandRejectReason SetMaintenancePolicy(SystemDelta delta, SystemLinkState link, string commandWorldId, MaintenancePolicy policy)
        {
            RemoteCommandRejectReason gate = RequireLive(link);
            if (gate != RemoteCommandRejectReason.None) return Report(gate);
            foreach (StructureDto s in delta.structures)
            {
                if (s.worldId != commandWorldId || (SurfaceStructureType)s.type != SurfaceStructureType.Command) continue;
                s.maintenanceReserve ??= new MaintenanceReserveDto();
                s.maintenanceReserve.policy = (int)policy;
                return Report(RemoteCommandRejectReason.None);
            }
            return Report(RemoteCommandRejectReason.NotFound);
        }

        // Follow/notification settings are session-local UI preference, not gameplay state --
        // intentionally not modeled as SystemDelta fields (nothing there for RemoteEconomyTick to read).
        // Exposed here only as the typed command-surface placeholder a future settings panel would call.
        public static RemoteCommandRejectReason SetFollowNotify(SystemLinkState link, bool _) => Report(RequireLive(link));

        // --- Explicitly forbidden (§10) -----------------------------------------------
        // Every one of these always rejects, regardless of link state -- typed and testable
        // so a future caller (and this slice's own tests) can assert the exact boundary.
        public static RemoteCommandRejectReason RejectBuildOrUpgrade() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectDirectRepair() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectDismantle() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectTransfer() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectProduction() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectScanOrPoi() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectEvacuateOrReclaim() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectVehicleProduction() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectCrossSystemFleet() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
        public static RemoteCommandRejectReason RejectTeleport() => Report(RemoteCommandRejectReason.NotAllowedRemotely);
    }
}
