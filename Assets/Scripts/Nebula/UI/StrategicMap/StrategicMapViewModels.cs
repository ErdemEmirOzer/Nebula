using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Sectors;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// Owns the collection boundary for every strategic-map view model. The data
    /// source deliberately reuses working buffers, while a published snapshot is
    /// a stable point-in-time value; copying behind a read-only wrapper keeps those
    /// two lifetime rules from ever leaking into one another.
    /// </summary>
    internal static class StrategicMapReadOnlyList
    {
        public static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0) return System.Array.Empty<T>();

            var owned = new T[source.Count];
            for (int i = 0; i < source.Count; i++) owned[i] = source[i];
            return System.Array.AsReadOnly(owned);
        }
    }

    /// <summary>
    /// What a single canonical surface slot currently is, from the player's point of
    /// view. Deliberately a presentation state, not a gameplay state: the map never
    /// decides anything, it only describes what the real structure/drill authorities
    /// already say.
    /// </summary>
    public enum StrategicSlotState
    {
        /// <summary>Nothing there and nothing the player could put there right now.</summary>
        Locked,
        /// <summary>Nothing there, and this slot is genuinely available to build on.</summary>
        Buildable,
        Constructing,
        Active,
        Damaged,
        Disabled,
        Upgrading
    }

    /// <summary>One of the canonical 12 surface slots on a planet.</summary>
    public readonly struct StrategicSlotView
    {
        public readonly string NodeId;
        public readonly SurfaceNodeKind Kind;
        public readonly StrategicSlotState State;
        public readonly int Tier;
        /// <summary>0..1 for Constructing/Upgrading, otherwise 0.</summary>
        public readonly float Progress01;
        /// <summary>Local surface direction, so the view can place the marker on the planet sprite.</summary>
        public readonly Vector3 LocalNormal;
        /// <summary>Stable content id used ONLY to resolve a real icon/prefab — never shown as text.</summary>
        public readonly string VisualContentId;

        public StrategicSlotView(string nodeId, SurfaceNodeKind kind, StrategicSlotState state, int tier,
            float progress01, Vector3 localNormal, string visualContentId)
        {
            NodeId = nodeId; Kind = kind; State = state; Tier = tier;
            Progress01 = progress01; LocalNormal = localNormal; VisualContentId = visualContentId;
        }
    }

    /// <summary>
    /// A planet as the player is currently allowed to see it. Every "known" flag is a
    /// real information gate: when it is false the corresponding field is meaningless
    /// and the view must not render it.
    /// </summary>
    public readonly struct StrategicPlanetView
    {
        public readonly string PlanetId;
        public readonly string DisplayName;
        public readonly int PlanetIndex;
        public readonly float OrbitRadius;
        public readonly float PlanetRadius;
        public readonly float OrbitAngleDegrees;

        /// <summary>Scan gate: family/rarity/environment/archetype/slots are only meaningful when true.</summary>
        public readonly bool Scanned;
        public readonly ResourceFamily Family;
        public readonly Rarity Rarity;
        public readonly EnvironmentClass Environment;
        public readonly PlanetArchetype Archetype;

        public readonly DominanceState Dominance;
        public readonly bool Abandoned;

        /// <summary>Drill presence/tier/fill — only populated for a scanned planet on a Live or LastKnown link.</summary>
        public readonly bool HasDrill;
        public readonly int DrillTier;
        public readonly float DrillFill01;

        public readonly IReadOnlyList<StrategicSlotView> Slots;

        public StrategicPlanetView(string planetId, string displayName, int planetIndex, float orbitRadius,
            float planetRadius, float orbitAngleDegrees, bool scanned, ResourceFamily family, Rarity rarity,
            EnvironmentClass environment, PlanetArchetype archetype, DominanceState dominance, bool abandoned,
            bool hasDrill, int drillTier, float drillFill01, IReadOnlyList<StrategicSlotView> slots)
        {
            PlanetId = planetId; DisplayName = displayName; PlanetIndex = planetIndex;
            OrbitRadius = orbitRadius; PlanetRadius = planetRadius; OrbitAngleDegrees = orbitAngleDegrees;
            Scanned = scanned; Family = family; Rarity = rarity; Environment = environment; Archetype = archetype;
            Dominance = dominance; Abandoned = abandoned;
            HasDrill = hasDrill; DrillTier = drillTier; DrillFill01 = drillFill01;
            Slots = StrategicMapReadOnlyList.Freeze(slots);
        }
    }

    /// <summary>
    /// A known POI inside a visited system. Position, risk and type are separate
    /// gates because the gameplay discovery chain deliberately reveals them at
    /// different steps: Signal → Located → Identified.
    /// </summary>
    public readonly struct StrategicPoiView
    {
        public readonly string PoiId;
        public readonly string DisplayName;
        /// <summary>True from Located onward; otherwise <see cref="LocalOffset"/> is deliberately zero.</summary>
        public readonly bool PositionKnown;
        public readonly Vector3 LocalOffset;
        /// <summary>True from Identified onward. Risk stays hidden with type until identification completes.</summary>
        public readonly bool RiskKnown;
        public readonly PoiRisk Risk;
        /// <summary>True from Identified onward. Type must not leak at Signal or Located.</summary>
        public readonly bool TypeKnown;
        public readonly PoiType Type;
        /// <summary>The real POI knowledge state; the map only renders it and never advances it.</summary>
        public readonly PoiKnowledgeLevel KnowledgeLevel;

        public StrategicPoiView(string poiId, string displayName, bool positionKnown, Vector3 localOffset,
            bool riskKnown, PoiRisk risk, bool typeKnown, PoiType type, PoiKnowledgeLevel knowledgeLevel)
        {
            PoiId = poiId; DisplayName = displayName;
            PositionKnown = positionKnown; LocalOffset = positionKnown ? localOffset : Vector3.zero;
            RiskKnown = riskKnown; Risk = riskKnown ? risk : default;
            TypeKnown = typeKnown; Type = typeKnown ? type : default;
            KnowledgeLevel = knowledgeLevel;
        }
    }

    /// <summary>
    /// A system as the player is currently allowed to see it.
    ///
    /// Knowledge gates, exactly as the design requires:
    /// <list type="bullet">
    /// <item>Unknown — never produced at all (the map must not reveal it exists).</item>
    /// <item>Signal — only <see cref="ApproximateDirection"/> and risk; no exact position,
    /// no star class, no planets.</item>
    /// <item>Located — adds the real position (<see cref="PositionKnown"/>); still no star
    /// class and no planets.</item>
    /// <item>Visited — adds star class, planet count and the planet/POI lists
    /// (<see cref="DetailsKnown"/>).</item>
    /// </list>
    /// </summary>
    public readonly struct StrategicSystemView
    {
        public readonly string SystemId;
        public readonly string DisplayName;
        public readonly SectorCoordinate Sector;
        public readonly SystemKnowledgeLevel Knowledge;
        public readonly SystemLinkState Link;

        /// <summary>Located+ only. Meaningless (and must not be drawn as a real position) otherwise.</summary>
        public readonly bool PositionKnown;
        public readonly Vector3 AbsoluteCenter;
        /// <summary>Signal only: a coarse, quantized unit direction — never an exact position.</summary>
        public readonly Vector3 ApproximateDirection;

        /// <summary>Visited only: star class, planet count, planets and POIs are meaningful.</summary>
        public readonly bool DetailsKnown;
        public readonly StarClass StarClass;
        public readonly float StarRadius;
        public readonly float NavigationRadius;
        public readonly int PlanetCount;

        public readonly SystemOwnershipTag Ownership;
        public readonly bool IsCurrentSystem;
        public readonly bool IsStartSystem;
        public readonly bool HasAttention;
        public readonly bool HasRaidWarning;

        /// <summary>
        /// Active-simulation seconds since this system's last live snapshot. Zero is a
        /// valid just-disconnected reading; NaN means no authoritative age source exists.
        /// </summary>
        public readonly float LastKnownAgeSeconds;

        public readonly IReadOnlyList<StrategicPlanetView> Planets;
        public readonly IReadOnlyList<StrategicPoiView> Pois;

        public StrategicSystemView(string systemId, string displayName, SectorCoordinate sector,
            SystemKnowledgeLevel knowledge, SystemLinkState link, bool positionKnown, Vector3 absoluteCenter,
            Vector3 approximateDirection, bool detailsKnown, StarClass starClass, float starRadius,
            float navigationRadius, int planetCount, SystemOwnershipTag ownership, bool isCurrentSystem,
            bool isStartSystem, bool hasAttention, bool hasRaidWarning, float lastKnownAgeSeconds,
            IReadOnlyList<StrategicPlanetView> planets, IReadOnlyList<StrategicPoiView> pois)
        {
            SystemId = systemId; DisplayName = displayName; Sector = sector;
            Knowledge = knowledge; Link = link;
            PositionKnown = positionKnown; AbsoluteCenter = absoluteCenter; ApproximateDirection = approximateDirection;
            DetailsKnown = detailsKnown; StarClass = starClass; StarRadius = starRadius;
            NavigationRadius = navigationRadius; PlanetCount = planetCount;
            Ownership = ownership; IsCurrentSystem = isCurrentSystem; IsStartSystem = isStartSystem;
            HasAttention = hasAttention; HasRaidWarning = hasRaidWarning;
            LastKnownAgeSeconds = lastKnownAgeSeconds;
            Planets = StrategicMapReadOnlyList.Freeze(planets);
            Pois = StrategicMapReadOnlyList.Freeze(pois);
        }

        /// <summary>Whether the domain supplied an age rather than the map guessing one.</summary>
        public bool HasLastKnownAge => !float.IsNaN(LastKnownAgeSeconds);

        /// <summary>Only LastKnown carries a cached dynamic snapshot; Disconnected does not.</summary>
        public bool IsStale => Link == SystemLinkState.LastKnown;
    }

    /// <summary>One non-zero cargo line for the player panel.</summary>
    public readonly struct StrategicResourceLine
    {
        public readonly string ItemId;
        public readonly float Amount;
        public StrategicResourceLine(string itemId, float amount) { ItemId = itemId; Amount = amount; }
    }

    /// <summary>
    /// The top-left player summary: real ship cargo plus the handful of facts that
    /// actually help a decision. Never debug data, never hidden intelligence.
    /// </summary>
    public readonly struct StrategicPlayerSummary
    {
        public readonly int Level;
        public readonly long TotalXp;
        public readonly float CargoUsedVolume;
        public readonly float CargoCapacity;
        public readonly string CurrentSystemName;
        public readonly SystemLinkState CurrentLink;
        public readonly int CommandUsed;
        public readonly int CommandCapacity;
        public readonly bool HasCriticalAlert;
        public readonly string WaypointName;
        public readonly float WaypointDistance;
        public readonly IReadOnlyList<StrategicResourceLine> Resources;

        public StrategicPlayerSummary(int level, long totalXp, float cargoUsedVolume, float cargoCapacity,
            string currentSystemName, SystemLinkState currentLink, int commandUsed, int commandCapacity,
            bool hasCriticalAlert, string waypointName, float waypointDistance,
            IReadOnlyList<StrategicResourceLine> resources)
        {
            Level = level; TotalXp = totalXp;
            CargoUsedVolume = cargoUsedVolume; CargoCapacity = cargoCapacity;
            CurrentSystemName = currentSystemName; CurrentLink = currentLink;
            CommandUsed = commandUsed; CommandCapacity = commandCapacity;
            HasCriticalAlert = hasCriticalAlert;
            WaypointName = waypointName; WaypointDistance = waypointDistance;
            Resources = StrategicMapReadOnlyList.Freeze(resources);
        }
    }

    /// <summary>A known relationship between two systems, drawn as a thin line.</summary>
    public readonly struct StrategicLinkView
    {
        public readonly string FromSystemId;
        public readonly string ToSystemId;
        /// <summary>True when this edge is the player's own active navigation route.</summary>
        public readonly bool IsActiveRoute;

        public StrategicLinkView(string fromSystemId, string toSystemId, bool isActiveRoute)
        {
            FromSystemId = fromSystemId; ToSystemId = toSystemId; IsActiveRoute = isActiveRoute;
        }
    }

    /// <summary>
    /// Knowledge-gated, player-facing facts from one pending reconnect report.
    /// Raw task descriptions and stable ids deliberately stay in the domain DTO;
    /// the map only needs enough information to present an honest compact summary.
    /// </summary>
    public readonly struct StrategicReconnectSummaryView
    {
        public readonly string SystemId;
        public readonly string SystemName;
        public readonly bool RaidResolved;
        public readonly bool WasDamagedOrDisabled;
        public readonly int StolenResourceKinds;
        public readonly int SalvagedResourceKinds;
        public readonly int CompletedTaskCount;
        public readonly int StalledTaskCount;
        public readonly bool HasLogisticsStatus;

        public StrategicReconnectSummaryView(string systemId, string systemName, bool raidResolved,
            bool wasDamagedOrDisabled, int stolenResourceKinds, int salvagedResourceKinds,
            int completedTaskCount, int stalledTaskCount, bool hasLogisticsStatus)
        {
            SystemId = systemId;
            SystemName = systemName;
            RaidResolved = raidResolved;
            WasDamagedOrDisabled = wasDamagedOrDisabled;
            StolenResourceKinds = stolenResourceKinds;
            SalvagedResourceKinds = salvagedResourceKinds;
            CompletedTaskCount = completedTaskCount;
            StalledTaskCount = stalledTaskCount;
            HasLogisticsStatus = hasLogisticsStatus;
        }
    }

    /// <summary>One immutable snapshot of everything the strategic map is allowed to draw.</summary>
    public sealed class StrategicMapSnapshot
    {
        public IReadOnlyList<StrategicSystemView> Systems { get; }
        public IReadOnlyList<StrategicLinkView> Links { get; }
        public StrategicPlayerSummary Player { get; }
        public string CurrentSystemId { get; }
        public IReadOnlyList<StrategicReconnectSummaryView> ReconnectSummaries { get; }

        public StrategicMapSnapshot(IReadOnlyList<StrategicSystemView> systems, IReadOnlyList<StrategicLinkView> links,
            StrategicPlayerSummary player, string currentSystemId,
            IReadOnlyList<StrategicReconnectSummaryView> reconnectSummaries = null)
        {
            Systems = StrategicMapReadOnlyList.Freeze(systems);
            Links = StrategicMapReadOnlyList.Freeze(links);
            Player = player;
            CurrentSystemId = currentSystemId;
            ReconnectSummaries = StrategicMapReadOnlyList.Freeze(reconnectSummaries);
        }

        public bool TryGetSystem(string systemId, out StrategicSystemView view)
        {
            for (int i = 0; i < Systems.Count; i++)
                if (Systems[i].SystemId == systemId) { view = Systems[i]; return true; }
            view = default;
            return false;
        }
    }
}
