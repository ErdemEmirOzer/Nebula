using System.Collections.Generic;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Navigation;
using Nebula.Sectors;

namespace Nebula.World
{
    /// <summary>
    /// Single integration point (VS-010 §1) that assembles real per-system
    /// facts (from live <see cref="ExtractionState"/> when a system is
    /// currently Full, or its persisted <see cref="SystemDelta"/> otherwise)
    /// and feeds them to the pure <see cref="SystemOwnershipResolver"/> /
    /// <see cref="SystemCommunicationResolver"/>. Nothing here is ever
    /// persisted as its own authority — every call re-derives from the same
    /// ground truth the rest of the game already reads.
    /// </summary>
    public static class SystemStatusQuery
    {
        /// <summary>
        /// Every real planet's current dominance state for a system, live if
        /// Full, else from its delta (Unowned for a planet with no delta
        /// entry). VS-010 §5: an Abandoned planet always reads as Unowned
        /// here — ownership/link/attention queries must never count a
        /// passivated planet as Controlled, even though its underlying
        /// DominanceState is frozen (not reset) until reclaim.
        /// </summary>
        public static Dictionary<string, DominanceState> PlanetDominanceStates(string systemId)
        {
            var result = new Dictionary<string, DominanceState>();
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return result;

            bool isFull = StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.GetState(systemId) == SystemStreamingState.Full;
            foreach (PlanetManifest p in manifest.Planets) result[p.PlanetId] = DominanceState.Unowned;

            if (isFull)
            {
                foreach (PlanetManifest p in manifest.Planets)
                    result[p.PlanetId] = ExtractionState.IsPlanetAbandoned(p.PlanetId) ? DominanceState.Unowned : ExtractionState.GetDominance(p.PlanetId).State;
            }
            else
            {
                SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
                if (delta != null)
                {
                    foreach (DominanceDto dto in delta.dominance)
                        if (result.ContainsKey(dto.worldId)) result[dto.worldId] = (DominanceState)dto.state;
                    foreach (string abandonedId in delta.abandonedPlanets)
                        if (result.ContainsKey(abandonedId)) result[abandonedId] = DominanceState.Unowned;
                }
            }
            return result;
        }

        /// <summary>True if any planet in the system currently has an Active (built, not disabled) Command structure — live if Full, else from its delta.</summary>
        public static bool HasActiveCommandAnywhere(string systemId)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return false;

            bool isFull = StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.GetState(systemId) == SystemStreamingState.Full;
            if (isFull)
            {
                foreach (PlanetManifest p in manifest.Planets)
                    if (ExtractionState.HasActiveCommand(p.PlanetId)) return true;
                return false;
            }

            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            if (delta == null) return false;
            foreach (StructureDto dto in delta.structures)
                if ((SurfaceStructureType)dto.type == SurfaceStructureType.Command && (DrillLifecycle)dto.state == DrillLifecycle.Active
                    && !delta.abandonedPlanets.Contains(dto.worldId))
                    return true;
            return false;
        }

        /// <summary>True if any planet in the system currently has at least one Active Laser defense — live if Full, else from its delta.</summary>
        public static bool HasActiveDefenseAnywhere(string systemId)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return false;

            bool isFull = StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.GetState(systemId) == SystemStreamingState.Full;
            if (isFull)
            {
                foreach (PlanetManifest p in manifest.Planets)
                    if (ExtractionState.ActiveDefenseCount(p.PlanetId) > 0) return true;
                return false;
            }

            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            if (delta == null) return false;
            foreach (StructureDto dto in delta.structures)
                if ((SurfaceStructureType)dto.type == SurfaceStructureType.Laser && (DrillLifecycle)dto.state == DrillLifecycle.Active
                    && !delta.abandonedPlanets.Contains(dto.worldId))
                    return true;
            return false;
        }

        /// <summary>VS-011 §2/§8: true if this system currently has an Active (T1 or T2, not Constructing/Disabled/Dismantled) Logistics Center — live if Full, else from its delta.</summary>
        public static bool HasActiveLogisticsCenter(string systemId)
        {
            bool isFull = StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.GetState(systemId) == SystemStreamingState.Full;
            if (isFull)
            {
                LogisticsCenter center = LogisticsState.FindCenter(systemId);
                return center != null && center.Runtime.IsActive;
            }
            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            if (delta == null) return false;
            foreach (LogisticsCenterDto dto in delta.logisticsCenters)
                if (dto.systemId == systemId && (LogisticsCenterState)dto.state == LogisticsCenterState.Active) return true;
            return false;
        }

        /// <summary>VS-011 §4/§8: true if this system has at least one non-Paused Collector route right now — live if Full, else from its delta.</summary>
        public static bool HasWorkingCollectorRoute(string systemId)
        {
            bool isFull = StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.GetState(systemId) == SystemStreamingState.Full;
            if (isFull)
            {
                foreach (CollectorRoute route in LogisticsState.Routes)
                    if (route != null && route.SystemId == systemId && !route.Paused) return true;
                return false;
            }
            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            if (delta == null) return false;
            foreach (CollectorRouteDto dto in delta.collectorRoutes)
                if (dto.systemId == systemId && (RoutePhase)dto.phase != RoutePhase.Paused) return true;
            return false;
        }

        /// <summary>VS-011 §8: (tier, fill 0..1, active-route count) for the Navigation screen's one-line summary — (0, 0, 0) when no center exists yet.</summary>
        public static (int tier, float fill01, int activeRouteCount) LogisticsCenterSummary(string systemId)
        {
            bool isFull = StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.GetState(systemId) == SystemStreamingState.Full;
            if (isFull)
            {
                int routeCount = 0;
                foreach (CollectorRoute route in LogisticsState.Routes) if (route != null && route.SystemId == systemId && !route.Paused) routeCount++;
                LogisticsCenter center = LogisticsState.FindCenter(systemId);
                if (center == null) return (0, 0f, routeCount);
                float fill = center.Runtime.Def.Capacity > 0f ? center.Inventory.Total / center.Runtime.Def.Capacity : 0f;
                return ((int)center.Runtime.Tier, fill, routeCount);
            }

            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            if (delta == null) return (0, 0f, 0);
            int remoteRouteCount = 0;
            foreach (CollectorRouteDto dto in delta.collectorRoutes) if (dto.systemId == systemId && (RoutePhase)dto.phase != RoutePhase.Paused) remoteRouteCount++;
            foreach (LogisticsCenterDto dto in delta.logisticsCenters)
            {
                if (dto.systemId != systemId) continue;
                LogisticsCenterTierDef def = LogisticsCenterCatalog.Get((LogisticsCenterTier)dto.tier);
                float totalStored = 0f;
                foreach (CargoItemDto item in dto.inventory) totalStored += item.amount;
                float fill = def.Capacity > 0f ? totalStored / def.Capacity : 0f;
                return (dto.tier, fill, remoteRouteCount);
            }
            return (0, 0f, remoteRouteCount);
        }

        /// <summary>A non-empty persisted delta OR a currently-materialized (Full/Warm) system counts as a valid snapshot.</summary>
        public static bool HasValidSnapshot(string systemId)
        {
            if (StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.GetState(systemId) != SystemStreamingState.None) return true;
            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            return delta != null && !delta.IsEmpty;
        }

        public static SystemOwnershipTag Ownership(string systemId, SystemKnowledgeService knowledge)
        {
            bool visited = knowledge.GetLevel(systemId) == SystemKnowledgeLevel.Visited;
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            int total = manifest?.Planets.Count ?? 0;

            Dictionary<string, DominanceState> states = PlanetDominanceStates(systemId);
            int controlled = 0;
            foreach (DominanceState s in states.Values) if (s == DominanceState.Controlled) controlled++;

            return SystemOwnershipResolver.Resolve(visited, total, controlled,
                hasActiveLogisticsCenter: HasActiveLogisticsCenter(systemId), hasWorkingCollectorRoute: HasWorkingCollectorRoute(systemId),
                hasActiveDefense: HasActiveDefenseAnywhere(systemId));
        }

        /// <summary>
        /// The ship is inside NO system whenever it is outside every system's
        /// navigation radius — <see cref="Navigation.NavigationDirector.CurrentSystemId"/>
        /// is null there by design, which is every interstellar crossing.
        /// </summary>
        public static bool ShipIsInterstellar(string shipCurrentSystemId) => string.IsNullOrEmpty(shipCurrentSystemId);

        /// <summary>
        /// VS-010 §1. Interstellar space is a real, nameable communication
        /// state, not an error: "Live" models a Command-relay mesh that has to
        /// ORIGINATE somewhere, and a ship inside no system has no local
        /// Command to originate from. So the ship's end of the relay is
        /// honestly false, no target can be Live, and every target falls to
        /// exactly the answer the resolver already computes —
        /// <see cref="SystemLinkState.LastKnown"/> where
        /// <see cref="HasValidSnapshot"/> says real data exists,
        /// <see cref="SystemLinkState.Disconnected"/> where it does not.
        /// Nothing is guessed and nothing unknown leaks in: the card built on
        /// top of this is flagged last-known, and a Disconnected system never
        /// gets a card at all.
        /// <para>
        /// This also removes a per-frame crash: the id used to be handed
        /// straight to <see cref="Sectors.SectorManifestRepository.ResolveSystem"/>,
        /// which uses it as a Dictionary key, so <c>NebulaHudAlerts.RefreshAttention</c>
        /// threw <c>ArgumentNullException</c> on every frame spent between systems.
        /// </para>
        /// </summary>
        public static SystemLinkState Link(string systemId, string shipCurrentSystemId, SystemKnowledgeService knowledge)
        {
            // A target with no id is not a system anyone can be in contact with,
            // and must never coincidentally match a null ship id as "the same system".
            if (string.IsNullOrEmpty(systemId)) return SystemLinkState.Disconnected;

            bool known = knowledge.GetLevel(systemId) >= SystemKnowledgeLevel.Located;
            bool interstellar = ShipIsInterstellar(shipCurrentSystemId);
            return SystemCommunicationResolver.Resolve(systemId, interstellar ? null : shipCurrentSystemId,
                targetHasActiveCommand: HasActiveCommandAnywhere(systemId),
                shipSystemHasActiveCommand: !interstellar && HasActiveCommandAnywhere(shipCurrentSystemId),
                targetIsKnown: known,
                targetHasValidSnapshot: HasValidSnapshot(systemId));
        }
    }
}
