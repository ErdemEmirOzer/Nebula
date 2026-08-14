using System;
using System.Collections.Generic;

namespace Nebula.Logistics
{
    public enum RouteCreateRejectReason
    {
        None, CenterNotActive, RouteCapReached, VehicleNotCollector, VehicleNotEligible,
        VehicleAlreadyRouted, DrillAlreadyRouted, DrillNotActive, NotSameSystem, NoCommandCapacity
    }

    /// <summary>
    /// Pure validation + the deterministic dock-assignment tie-break (VS-011
    /// §4), shared verbatim by both the live per-frame director and the
    /// remote delta tick so a route resolves identically whichever path
    /// is currently ticking it.
    /// </summary>
    public static class CollectorRouteService
    {
        public static RouteCreateRejectReason CheckCreate(bool centerActive, int currentRouteCountAtCenter, int maxRoutes,
            bool vehicleIsCollector, bool vehicleEligible, bool vehicleAlreadyRouted, bool drillAlreadyRouted,
            bool drillActive, bool sameSystem, bool hasCommandCapacity)
        {
            if (!sameSystem) return RouteCreateRejectReason.NotSameSystem;
            if (!centerActive) return RouteCreateRejectReason.CenterNotActive;
            if (currentRouteCountAtCenter >= maxRoutes) return RouteCreateRejectReason.RouteCapReached;
            if (!vehicleIsCollector) return RouteCreateRejectReason.VehicleNotCollector;
            if (!vehicleEligible) return RouteCreateRejectReason.VehicleNotEligible;
            if (vehicleAlreadyRouted) return RouteCreateRejectReason.VehicleAlreadyRouted;
            if (drillAlreadyRouted) return RouteCreateRejectReason.DrillAlreadyRouted;
            if (!drillActive) return RouteCreateRejectReason.DrillNotActive;
            if (!hasCommandCapacity) return RouteCreateRejectReason.NoCommandCapacity;
            return RouteCreateRejectReason.None;
        }

        public static string NextRouteId(int sequence) => $"route-{sequence:0000}";

        /// <summary>One dock-assignment candidate (a route currently Holding or Queued at one center).</summary>
        public readonly struct DockCandidate
        {
            public readonly string RouteId;
            public readonly string VehicleId;
            public readonly bool IsHolding;
            public readonly bool SourceDrillFull;
            public readonly RoutePriority Priority;
            public readonly float WaitStartedAt;

            public DockCandidate(string routeId, string vehicleId, bool isHolding, bool sourceDrillFull, RoutePriority priority, float waitStartedAt)
            {
                RouteId = routeId; VehicleId = vehicleId; IsHolding = isHolding;
                SourceDrillFull = sourceDrillFull; Priority = priority; WaitStartedAt = waitStartedAt;
            }
        }

        /// <summary>
        /// Deterministic dock queue order (VS-011 §4): loaded Holding
        /// vehicles unload before any fresh Queued route proceeds; within
        /// the same group — 1) source drill currently full, 2) High/Normal/
        /// Low priority, 3) oldest wait time, 4) canonical vehicle id.
        /// </summary>
        public static int CompareDockPriority(DockCandidate a, DockCandidate b)
        {
            if (a.IsHolding != b.IsHolding) return a.IsHolding ? -1 : 1;
            if (a.SourceDrillFull != b.SourceDrillFull) return a.SourceDrillFull ? -1 : 1;
            if (a.Priority != b.Priority) return b.Priority.CompareTo(a.Priority); // High(2) first
            if (Math.Abs(a.WaitStartedAt - b.WaitStartedAt) > 0.0001f) return a.WaitStartedAt.CompareTo(b.WaitStartedAt);
            return string.CompareOrdinal(a.VehicleId, b.VehicleId);
        }

        /// <summary>Sorts candidates into dock-assignment order (stable — ties broken purely by the comparator, never List order).</summary>
        public static List<DockCandidate> OrderForDockAssignment(List<DockCandidate> candidates)
        {
            var sorted = new List<DockCandidate>(candidates);
            sorted.Sort(CompareDockPriority);
            return sorted;
        }
    }
}
