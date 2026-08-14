using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// Builds an immutable <see cref="StrategicMapSnapshot"/> from the REAL game
    /// authorities only — <see cref="StarSystemRegistry"/>, <see cref="SystemStatusQuery"/>,
    /// <see cref="SystemKnowledgeService"/>, <see cref="RemoteCommunicationService"/>,
    /// <see cref="AttentionQuery"/>, <see cref="ExtractionState"/> and the ship's own
    /// cargo/hangar. It never invents world data, never reads the HTML prototype's
    /// demo snapshot, and never advances knowledge, scans, XP or dominance: every
    /// call is strictly read-only.
    ///
    /// Information gates live here and nowhere else, so a view physically cannot
    /// render something the player has not earned:
    /// Signal → approximate direction/risk only; Located → real position;
    /// Visited → star class, planets and POIs. An unscanned planet never exposes
    /// family/rarity/environment/archetype or its slot detail. A non-Live system
    /// never exposes live production/enemy/stock numbers.
    /// </summary>
    public sealed class StrategicMapDataSource
    {
        private readonly NebulaContext context;

        /// <summary>Reused buffers — a snapshot rebuild must not churn the heap while the map is open.</summary>
        private readonly List<StrategicSystemView> systemBuffer = new List<StrategicSystemView>();
        private readonly List<StrategicLinkView> linkBuffer = new List<StrategicLinkView>();
        private readonly List<StrategicResourceLine> resourceBuffer = new List<StrategicResourceLine>();
        private readonly List<StrategicReconnectSummaryView> reconnectBuffer = new List<StrategicReconnectSummaryView>();
        public StrategicMapDataSource(NebulaContext context) => this.context = context;

        /// <summary>Signal-level direction is deliberately coarse: quantized to 45° cones so it can never be reverse-engineered into a real position.</summary>
        private const float SignalDirectionQuantizationDegrees = 45f;

        public StrategicMapSnapshot Build()
        {
            systemBuffer.Clear();
            linkBuffer.Clear();
            reconnectBuffer.Clear();

            SystemKnowledgeService knowledge = context?.NavigationDirector?.Knowledge;
            if (knowledge == null) return new StrategicMapSnapshot(systemBuffer, linkBuffer, BuildPlayerSummary(null), null,
                reconnectBuffer);

            string currentSystemId = context.Streaming != null ? context.Streaming.FullSystemId : null;
            Vector3 shipAbsolute = context.NavigationDirector.ShipAbsolutePosition();

            BuildSystems(knowledge, currentSystemId, shipAbsolute, systemBuffer);
            BuildReconnectSummaries(systemBuffer, reconnectBuffer);

            BuildLinks(currentSystemId);
            return new StrategicMapSnapshot(systemBuffer, linkBuffer, BuildPlayerSummary(currentSystemId), currentSystemId,
                reconnectBuffer);
        }

        // ---------------------------------------------------------------- systems

        /// <summary>
        /// The information-gate core, deliberately static and context-free: it is a pure
        /// function of the real manifest registry, the player's knowledge and
        /// ExtractionState, so the gates can be proven in EditMode without booting a scene.
        /// </summary>
        public static void BuildSystems(SystemKnowledgeService knowledge, string currentSystemId,
            Vector3 shipAbsolute, List<StrategicSystemView> into)
        {
            if (knowledge == null || into == null) return;
            var ids = new List<string>();
            foreach (var kv in knowledge.All)
                if (kv.Value != SystemKnowledgeLevel.Unknown) ids.Add(kv.Key);
            // Canonical order so the map is deterministic and node identity never shuffles.
            ids.Sort(string.CompareOrdinal);

            foreach (string systemId in ids)
            {
                SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
                if (manifest == null) continue;
                into.Add(BuildSystem(manifest, knowledge, currentSystemId, shipAbsolute));
            }
        }

        private static StrategicSystemView BuildSystem(SystemManifest manifest, SystemKnowledgeService knowledge,
            string currentSystemId, Vector3 shipAbsolute)
        {
            SystemKnowledgeLevel level = knowledge.GetLevel(manifest.SystemId);
            SystemLinkState link = SafeLink(manifest.SystemId, currentSystemId, knowledge);

            bool positionKnown = level >= SystemKnowledgeLevel.Located;
            bool detailsKnown = level >= SystemKnowledgeLevel.Visited;
            bool isCurrent = manifest.SystemId == currentSystemId;

            // Signal: a coarse cone only. Never the real vector, never the real distance.
            Vector3 approximateDirection = Vector3.zero;
            if (!positionKnown)
            {
                Vector3 raw = manifest.AbsoluteCenter - shipAbsolute;
                approximateDirection = Quantize(raw);
            }

            SystemOwnershipTag ownership = ResolveOwnership(manifest, knowledge);
            ResolveAlerts(manifest.SystemId, currentSystemId, knowledge, out bool attention, out bool raid);

            var planets = new List<StrategicPlanetView>();
            var pois = new List<StrategicPoiView>();
            if (detailsKnown)
            {
                BuildPlanets(manifest, link, planets);
                BuildPois(manifest, pois);
            }

            return new StrategicSystemView(
                systemId: manifest.SystemId,
                displayName: manifest.DisplayName,
                sector: manifest.Sector,
                knowledge: level,
                link: link,
                positionKnown: positionKnown,
                absoluteCenter: positionKnown ? manifest.AbsoluteCenter : Vector3.zero,
                approximateDirection: approximateDirection,
                detailsKnown: detailsKnown,
                starClass: detailsKnown ? manifest.StarClass : default,
                starRadius: detailsKnown ? manifest.StarRadius : 0f,
                navigationRadius: detailsKnown ? manifest.NavigationRadius : 0f,
                planetCount: detailsKnown ? manifest.Planets.Count : 0,
                ownership: ownership,
                isCurrentSystem: isCurrent,
                isStartSystem: manifest.IsStartSystem,
                hasAttention: attention,
                hasRaidWarning: raid,
                lastKnownAgeSeconds: LastKnownAge(manifest.SystemId, link),
                planets: planets,
                pois: pois);
        }

        /// <summary>Rounds a direction into fixed cones so a Signal-level system leaks a heading, never a position.</summary>
        private static Vector3 Quantize(Vector3 raw)
        {
            if (raw.sqrMagnitude < 0.0001f) return Vector3.forward;
            Vector3 flat = new Vector3(raw.x, 0f, raw.z);
            if (flat.sqrMagnitude < 0.0001f) return Vector3.forward;
            float angle = Mathf.Atan2(flat.z, flat.x) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(angle / SignalDirectionQuantizationDegrees) * SignalDirectionQuantizationDegrees;
            float rad = snapped * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)).normalized;
        }

        /// <summary>
        /// Link state through the existing SystemStatusQuery authority. With no Full system
        /// yet (a not-fully-booted session), nothing can be live, so the honest answer is
        /// Disconnected — a presentation layer must degrade, never throw.
        /// </summary>
        private static SystemLinkState SafeLink(string systemId, string currentSystemId, SystemKnowledgeService knowledge)
        {
            if (string.IsNullOrEmpty(systemId) || string.IsNullOrEmpty(currentSystemId)) return SystemLinkState.Disconnected;
            try { return RemoteCommunicationService.GetLink(systemId, currentSystemId, knowledge); }
            catch { return SystemLinkState.Disconnected; }
        }

        /// <summary>Ownership comes from the existing SystemStatusQuery authority — never a second copy of the rule.</summary>
        private static SystemOwnershipTag ResolveOwnership(SystemManifest manifest, SystemKnowledgeService knowledge)
        {
            try { return SystemStatusQuery.Ownership(manifest.SystemId, knowledge); }
            catch { return SystemOwnershipTag.Discovered; }
        }

        private static void ResolveAlerts(string systemId, string currentSystemId, SystemKnowledgeService knowledge,
            out bool attention, out bool raid)
        {
            attention = false; raid = false;
            if (string.IsNullOrEmpty(currentSystemId)) return;
            try
            {
                SystemAttentionInputs inputs = AttentionQuery.BuildInputs(systemId, currentSystemId, knowledge);
                attention = inputs.AnyCommandDisabled || inputs.AnyPlanetContested
                            || inputs.AllActiveDrillsStalledFull || inputs.VehicleStrandedNoTarget;
                raid = inputs.RaidDetected;
            }
            catch
            {
                // A presentation layer must never be able to break because a query threw.
            }
        }

        /// <summary>
        /// Age is the delta's own active-simulation clock since capture. It deliberately
        /// does not subtract lastRemoteTickSimSeconds: that field is a scheduler tick marker,
        /// not the time at which the player last had a live communication link.
        /// </summary>
        internal static float LastKnownAge(string systemId, SystemLinkState link)
        {
            if (link == SystemLinkState.Live) return 0f;
            if (link != SystemLinkState.LastKnown) return float.NaN;
            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            return delta != null ? Mathf.Max(0f, delta.simElapsedSeconds) : float.NaN;
        }

        /// <summary>
        /// Reconnect reports are exposed only for a visited system whose link is live again.
        /// The DTO stays pending until a visible HUD actually presents and consumes it.
        /// </summary>
        internal static void BuildReconnectSummaries(IReadOnlyList<StrategicSystemView> systems,
            List<StrategicReconnectSummaryView> into)
        {
            if (systems == null || into == null) return;
            for (int i = 0; i < systems.Count; i++)
            {
                StrategicSystemView system = systems[i];
                if (!system.DetailsKnown || system.Link != SystemLinkState.Live) continue;
                ReconnectSummaryDto dto = StarSystemRegistry.Deltas?.TryGet(system.SystemId)?.pendingReconnectSummary;
                if (dto == null) continue;

                into.Add(new StrategicReconnectSummaryView(
                    system.SystemId,
                    system.DisplayName,
                    raidResolved: !string.IsNullOrEmpty(dto.raidResultText),
                    wasDamagedOrDisabled: dto.wasDamagedOrDisabled,
                    stolenResourceKinds: dto.stolenResources?.Count ?? 0,
                    salvagedResourceKinds: dto.salvagedResources?.Count ?? 0,
                    completedTaskCount: dto.completedTaskDescriptions?.Count ?? 0,
                    stalledTaskCount: dto.stalledTaskDescriptions?.Count ?? 0,
                    hasLogisticsStatus: !string.IsNullOrEmpty(dto.logisticsMaintenanceStatusText)));
            }
        }

        // ---------------------------------------------------------------- planets

        private static void BuildPlanets(SystemManifest manifest, SystemLinkState link, List<StrategicPlanetView> into)
        {
            Dictionary<string, DominanceState> dominance = SystemStatusQuery.PlanetDominanceStates(manifest.SystemId);
            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(manifest.SystemId);
            bool liveNumbersAllowed = link != SystemLinkState.Disconnected;

            foreach (PlanetManifest pm in manifest.Planets)
            {
                PlanetProfile profile = ExtractionState.GetProfile(pm.PlanetId);
                bool scanned = profile != null && profile.Scanned;

                dominance.TryGetValue(pm.PlanetId, out DominanceState dom);
                bool abandoned = ExtractionState.IsPlanetAbandoned(pm.PlanetId);

                bool hasDrill = false; int drillTier = 1; float fill = 0f;
                var slots = new List<StrategicSlotView>();

                // Scan gate: without a scan the player learns nothing about the surface at all.
                if (scanned)
                {
                    BuildSlots(pm.PlanetId, profile, delta, liveNumbersAllowed, slots, out hasDrill, out drillTier, out fill);
                    if (!liveNumbersAllowed) fill = 0f; // a disconnected system must not show a live stock value
                }

                into.Add(new StrategicPlanetView(
                    planetId: pm.PlanetId,
                    displayName: PlanetDisplayName(manifest, pm),
                    planetIndex: pm.PlanetIndex,
                    orbitRadius: pm.OrbitRadius,
                    planetRadius: pm.PlanetRadius,
                    orbitAngleDegrees: pm.StartAngleDegrees,
                    scanned: scanned,
                    family: scanned ? pm.Family : default,
                    rarity: scanned ? pm.Rarity : default,
                    environment: scanned ? pm.Environment : default,
                    archetype: scanned ? PlanetArchetypeCatalog.ForFamily(pm.Family) : default,
                    dominance: dom,
                    abandoned: abandoned,
                    hasDrill: hasDrill,
                    drillTier: drillTier,
                    drillFill01: fill,
                    slots: slots));
            }
        }

        /// <summary>Shared with the live universe source, so the 3D map and the card system can never disagree on a planet's name.</summary>
        internal static string PlanetDisplayName(SystemManifest manifest, PlanetManifest pm) =>
            $"{manifest.DisplayName} {RomanNumeral(pm.PlanetIndex + 1)}";

        private static string RomanNumeral(int value)
        {
            switch (value)
            {
                case 1: return "I"; case 2: return "II"; case 3: return "III"; case 4: return "IV";
                case 5: return "V"; case 6: return "VI"; case 7: return "VII"; case 8: return "VIII";
                default: return value.ToString();
            }
        }

        // ---------------------------------------------------------------- slots

        /// <summary>
        /// The canonical 12 surface slots for a scanned planet, resolved live when the
        /// system is Full and from its persisted delta otherwise. A slot the player
        /// cannot currently build on reads <see cref="StrategicSlotState.Locked"/> —
        /// the map states availability, it never grants it.
        /// </summary>
        private static void BuildSlots(string worldId, PlanetProfile profile, SystemDelta delta,
            bool liveNumbersAllowed, List<StrategicSlotView> into,
            out bool hasDrill, out int drillTier, out float drillFill01)
        {
            hasDrill = false; drillTier = 1; drillFill01 = 0f;
            if (profile == null) return;

            foreach (SurfaceNode node in profile.Nodes)
            {
                StrategicSlotState state = StrategicSlotState.Buildable;
                int tier = 0; float progress = 0f; string visualId = null;

                if (node.Kind == SurfaceNodeKind.Drill)
                {
                    if (TryResolveDrill(worldId, delta, out DrillLifecycle lifecycle, out int t, out float stored, out float capacity, out bool upgrading))
                    {
                        hasDrill = true; drillTier = t; tier = t;
                        visualId = t >= 2 ? "structure.drill.t2" : "structure.drill.t1";
                        drillFill01 = capacity > 0.0001f && liveNumbersAllowed ? Mathf.Clamp01(stored / capacity) : 0f;
                        state = upgrading ? StrategicSlotState.Upgrading : MapLifecycle(lifecycle);
                    }
                }
                else if (TryResolveStructure(worldId, node.Id, delta, out DrillLifecycle sl, out int st, out bool up, out string vid))
                {
                    tier = st; visualId = vid;
                    state = up ? StrategicSlotState.Upgrading : MapLifecycle(sl);
                }
                else if (node.Kind == SurfaceNodeKind.Support && IsHiddenSupportSlot(profile, node))
                {
                    state = StrategicSlotState.Locked; // support.2 is L41+ content: never presented as buildable
                }

                into.Add(new StrategicSlotView(node.Id, node.Kind, state, tier, progress, node.LocalNormal, visualId));
            }
        }

        private static bool IsHiddenSupportSlot(PlanetProfile profile, SurfaceNode node) =>
            node.Id == profile.SupportNode(2).Id;

        private static StrategicSlotState MapLifecycle(DrillLifecycle lifecycle) => lifecycle switch
        {
            DrillLifecycle.Active => StrategicSlotState.Active,
            DrillLifecycle.Damaged => StrategicSlotState.Damaged,
            DrillLifecycle.Disabled => StrategicSlotState.Disabled,
            DrillLifecycle.CapsuleInTransit => StrategicSlotState.Constructing,
            DrillLifecycle.Constructing => StrategicSlotState.Constructing,
            _ => StrategicSlotState.Buildable
        };

        private static bool TryResolveDrill(string worldId, SystemDelta delta, out DrillLifecycle lifecycle,
            out int tier, out float stored, out float capacity, out bool upgrading)
        {
            lifecycle = DrillLifecycle.Dismantled; tier = 1; stored = 0f; capacity = 0f; upgrading = false;

            foreach (DrillStructure d in ExtractionState.Drills)
            {
                if (d == null || d.WorldId != worldId) continue;
                lifecycle = d.Runtime.State; tier = d.Runtime.Tier;
                stored = d.Runtime.StoredAmount; capacity = d.Runtime.StorageCapacity;
                upgrading = d.Runtime.ActiveUpgradeJob != null;
                return true;
            }

            if (delta == null) return false;
            foreach (DrillDto dto in delta.drills)
            {
                if (dto == null || dto.worldId != worldId) continue;
                lifecycle = (DrillLifecycle)dto.state; tier = dto.tier;
                stored = dto.stored; upgrading = dto.upgradeActive;
                PlanetProfile p = ExtractionState.GetProfile(worldId);
                capacity = p != null ? ExtractionCatalog.StorageCapacity(p.Rarity, dto.tier) : 0f;
                return true;
            }
            return false;
        }

        private static bool TryResolveStructure(string worldId, string nodeId, SystemDelta delta,
            out DrillLifecycle lifecycle, out int tier, out bool upgrading, out string visualId)
        {
            lifecycle = DrillLifecycle.Dismantled; tier = 0; upgrading = false; visualId = null;

            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || s.WorldId != worldId || s.NodeId != nodeId) continue;
                lifecycle = s.Runtime.State; tier = s.Runtime.Tier;
                upgrading = s.ActiveUpgradeJob != null;
                visualId = StructureVisualId(s.Type, s.Runtime.Tier);
                return true;
            }

            if (delta == null) return false;
            foreach (StructureDto dto in delta.structures)
            {
                if (dto == null || dto.worldId != worldId || dto.nodeId != nodeId) continue;
                lifecycle = (DrillLifecycle)dto.state; tier = dto.tier; upgrading = dto.upgrading;
                visualId = StructureVisualId((SurfaceStructureType)dto.type, dto.tier);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The REAL, tier-aware visual id from NebulaVisualCatalog, so a T2 structure shows
        /// its own T2 asset rather than being flattened to T1. Never an invented id.
        /// </summary>
        private static string StructureVisualId(SurfaceStructureType type, int tier)
        {
            string suffix = tier >= 2 ? ".t2" : ".t1";
            return type switch
            {
                SurfaceStructureType.Command => "structure.command" + suffix,
                SurfaceStructureType.Laser => "structure.defense.laser" + suffix,
                SurfaceStructureType.Plasma => "structure.defense.plasma" + suffix,
                SurfaceStructureType.SensorArray => "structure.support.sensor_array" + suffix,
                SurfaceStructureType.MaintenancePlatform => "structure.support.maintenance_platform" + suffix,
                _ => null
            };
        }

        // ---------------------------------------------------------------- POIs

        private static void BuildPois(SystemManifest system, List<StrategicPoiView> into)
        {
            if (system == null || into == null) return;
            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(system.SystemId);

            // The delta stores mutable discovery/work state only. Type, risk and
            // position belong to the deterministic immutable manifest and must be
            // resolved from the campaign's actual seed rather than fabricated here.
            ulong seed = StarSystemRegistry.Manifests?.Seed ?? ExtractionState.UniverseSeed;
            List<PoiManifest> manifests = PoiManifestRepository.Resolve(seed, system);
            for (int i = 0; i < manifests.Count; i++)
            {
                PoiManifest manifest = manifests[i];
                if (manifest == null) continue;

                PoiState live = PoiLiveState.Find(manifest.PoiId);
                PoiDto dto = live == null ? FindPoiDto(delta, manifest.PoiId) : null;
                int rawLevel = live != null ? (int)live.Level : dto?.level ?? 0;
                // Level 0 = Unknown: the map must not reveal that it exists at all.
                if (rawLevel <= 0) continue;

                PoiKnowledgeLevel knowledge = (PoiKnowledgeLevel)Mathf.Clamp(rawLevel,
                    (int)PoiKnowledgeLevel.Unknown, (int)PoiKnowledgeLevel.Resolved);
                bool positionKnown = knowledge >= PoiKnowledgeLevel.Located;
                bool riskKnown = knowledge >= PoiKnowledgeLevel.Identified;
                bool typeKnown = knowledge >= PoiKnowledgeLevel.Identified;

                into.Add(new StrategicPoiView(
                    poiId: manifest.PoiId,
                    displayName: PoiDisplayName(manifest.Type, typeKnown, knowledge),
                    positionKnown: positionKnown,
                    localOffset: manifest.SystemLocalPosition,
                    riskKnown: riskKnown,
                    risk: manifest.Risk,
                    typeKnown: typeKnown,
                    type: manifest.Type,
                    knowledgeLevel: knowledge));
            }
        }

        private static PoiDto FindPoiDto(SystemDelta delta, string poiId)
        {
            if (delta?.pois == null || string.IsNullOrEmpty(poiId)) return null;
            for (int i = 0; i < delta.pois.Count; i++)
                if (delta.pois[i] != null && delta.pois[i].poiId == poiId) return delta.pois[i];
            return null;
        }

        private static PoiManifest FindPoiManifest(IReadOnlyList<PoiManifest> manifests, string poiId)
        {
            if (manifests == null || string.IsNullOrEmpty(poiId)) return null;
            for (int i = 0; i < manifests.Count; i++)
                if (manifests[i] != null && manifests[i].PoiId == poiId) return manifests[i];
            return null;
        }

        private static string PoiDisplayName(PoiType type, bool typeKnown, PoiKnowledgeLevel knowledge)
        {
            if (!typeKnown)
                return knowledge >= PoiKnowledgeLevel.Located ? "Tanımlanamayan temas" : "Bilinmeyen sinyal";

            return type switch
            {
                PoiType.Anomaly => "Anomali",
                PoiType.Wreck => "Enkaz",
                PoiType.AbandonedFacility => "Terk edilmiş tesis",
                PoiType.Protected => "Korunan bölge",
                _ => "İlgi noktası"
            };
        }

        // ---------------------------------------------------------------- player

        private StrategicPlayerSummary BuildPlayerSummary(string currentSystemId)
        {
            resourceBuffer.Clear();

            LogisticsCargo cargo = context?.Ship != null ? context.Ship.GetComponent<ShipLogistics>()?.Cargo : null;
            if (cargo != null)
                foreach (var kv in cargo.Items)
                    if (kv.Value > 0.0001f) resourceBuffer.Add(new StrategicResourceLine(kv.Key, kv.Value));
            resourceBuffer.Sort((a, b) => string.CompareOrdinal(a.ItemId, b.ItemId));

            ShipHangar hangar = context?.Hangar;
            string currentName = "—";
            if (!string.IsNullOrEmpty(currentSystemId))
            {
                SystemManifest m = StarSystemRegistry.Manifests?.ResolveSystem(currentSystemId);
                if (m != null) currentName = m.DisplayName;
            }

            string waypointName = null; float waypointDistance = 0f;
            NavigationDirector nav = context?.NavigationDirector;
            if (nav != null && !string.IsNullOrEmpty(nav.SelectedRouteSystemId))
            {
                SystemManifest wm = StarSystemRegistry.Manifests?.ResolveSystem(nav.SelectedRouteSystemId);
                if (wm != null)
                {
                    waypointName = wm.DisplayName;
                    waypointDistance = Vector3.Distance(nav.ShipAbsolutePosition(), wm.AbsoluteCenter);
                }
            }

            SystemLinkState currentLink = SystemLinkState.Live;
            if (nav != null && !string.IsNullOrEmpty(currentSystemId))
                currentLink = SafeLink(currentSystemId, currentSystemId, nav.Knowledge);

            return new StrategicPlayerSummary(
                level: Progression.ProgressionService.State.Level,
                totalXp: Progression.ProgressionService.State.TotalXp,
                cargoUsedVolume: cargo?.UsedVolume ?? 0f,
                cargoCapacity: cargo?.Capacity ?? 0f,
                currentSystemName: currentName,
                currentLink: currentLink,
                commandUsed: hangar != null ? hangar.Command.Used : 0,
                commandCapacity: hangar != null ? hangar.Command.Capacity : 0,
                hasCriticalAlert: HasCriticalAlert(systemBuffer),
                waypointName: waypointName,
                waypointDistance: waypointDistance,
                resources: resourceBuffer);
        }

        /// <summary>
        /// Critical map status is derived from the already knowledge-gated system
        /// views. This keeps the summary honest (and prevents it from becoming a
        /// second raid/attention query with subtly different rules).
        /// </summary>
        internal static bool HasCriticalAlert(IReadOnlyList<StrategicSystemView> systems)
        {
            if (systems == null) return false;
            for (int i = 0; i < systems.Count; i++)
                if (systems[i].HasAttention || systems[i].HasRaidWarning) return true;
            return false;
        }

        // ---------------------------------------------------------------- links

        /// <summary>
        /// Known relationships only. Sector co-membership is not a travel-lane authority,
        /// so the map draws the player's real active navigation route and nothing invented.
        /// </summary>
        private void BuildLinks(string currentSystemId)
        {
            string routeTarget = context?.NavigationDirector?.SelectedRouteSystemId;
            if (!string.IsNullOrEmpty(routeTarget) && !string.IsNullOrEmpty(currentSystemId))
                linkBuffer.Add(new StrategicLinkView(currentSystemId, routeTarget, isActiveRoute: true));
        }
    }
}
