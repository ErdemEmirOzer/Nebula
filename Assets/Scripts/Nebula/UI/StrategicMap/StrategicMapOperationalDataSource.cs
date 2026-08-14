using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Navigation;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.UI.StrategicMap.Preview;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.UI.StrategicMap
{
    /// <summary>The three read-only operational overlays carried by the strategic map.</summary>
    public enum StrategicMapOperationalKind { Fleet, Logistics, Threat }

    /// <summary>
    /// One real operational contact. Exact contacts use a live or persisted absolute
    /// position. A schematic contact is deliberately pinned to its known system centre
    /// because the owning gameplay DTO does not persist a physical position.
    /// </summary>
    public readonly struct StrategicMapOperationalMarker
    {
        public readonly string Id;
        public readonly string SystemId;
        public readonly string GalaxyId;
        public readonly string Label;
        public readonly StrategicMapOperationalKind Kind;
        public readonly Vector3 Position;
        public readonly bool IsLastKnown;
        public readonly bool IsSchematic;
        public readonly bool NeedsAttention;

        public StrategicMapOperationalMarker(string id, string systemId, string galaxyId,
            string label, StrategicMapOperationalKind kind, Vector3 position,
            bool isLastKnown, bool isSchematic, bool needsAttention)
        {
            Id = id;
            SystemId = systemId;
            GalaxyId = galaxyId;
            Label = label;
            Kind = kind;
            Position = position;
            IsLastKnown = isLastKnown;
            IsSchematic = isSchematic;
            NeedsAttention = needsAttention;
        }
    }

    /// <summary>A stable point-in-time read of fleet, logistics, and threat contacts.</summary>
    public sealed class StrategicMapOperationalSnapshot
    {
        public static readonly StrategicMapOperationalSnapshot Empty = new StrategicMapOperationalSnapshot(
            Array.Empty<StrategicMapOperationalMarker>(), Array.Empty<StrategicMapOperationalMarker>(),
            Array.Empty<StrategicMapOperationalMarker>());

        public IReadOnlyList<StrategicMapOperationalMarker> Fleet { get; }
        public IReadOnlyList<StrategicMapOperationalMarker> Logistics { get; }
        public IReadOnlyList<StrategicMapOperationalMarker> Threats { get; }

        public StrategicMapOperationalSnapshot(IReadOnlyList<StrategicMapOperationalMarker> fleet,
            IReadOnlyList<StrategicMapOperationalMarker> logistics,
            IReadOnlyList<StrategicMapOperationalMarker> threats)
        {
            Fleet = StrategicMapReadOnlyList.Freeze(fleet);
            Logistics = StrategicMapReadOnlyList.Freeze(logistics);
            Threats = StrategicMapReadOnlyList.Freeze(threats);
        }
    }

    /// <summary>
    /// Optional live seam used by the renderer. The static HTML/reference universe does not
    /// implement it and therefore receives no invented operational contacts.
    /// </summary>
    public interface IStrategicMapOperationalSource
    {
        StrategicMapOperationalSnapshot BuildOperationalSnapshot(StrategicMapUniverseSnapshot universe);
    }

    /// <summary>
    /// Reads the existing fleet, logistics, combat, attention, communication and delta
    /// authorities. It owns no state beyond reusable presentation buffers and never ticks,
    /// reveals, commands, or saves gameplay.
    /// </summary>
    public sealed class StrategicMapOperationalDataSource
    {
        private readonly NebulaContext context;
        private readonly List<StrategicMapOperationalMarker> fleet = new List<StrategicMapOperationalMarker>();
        private readonly List<StrategicMapOperationalMarker> logistics = new List<StrategicMapOperationalMarker>();
        private readonly List<StrategicMapOperationalMarker> threats = new List<StrategicMapOperationalMarker>();

        public StrategicMapOperationalDataSource(NebulaContext context) => this.context = context;

        public StrategicMapOperationalSnapshot Build(StrategicMapUniverseSnapshot universe)
        {
            fleet.Clear();
            logistics.Clear();
            threats.Clear();

            if (universe == null) return StrategicMapOperationalSnapshot.Empty;
            if (!TrySessionState(out string currentSystemId, out SystemKnowledgeService knowledge))
                return StrategicMapOperationalSnapshot.Empty;

            for (int i = 0; i < universe.systems.Count; i++)
            {
                StrategicMapUniverseSnapshot.SystemRow system = universe.systems[i];
                if ((SystemKnowledgeLevel)system.knowledge < SystemKnowledgeLevel.Visited) continue;

                SystemLinkState link = SafeLink(system.id, currentSystemId, knowledge);
                // A disconnected system cannot report an operational position or count.
                // Known static world geometry remains elsewhere on the map; these layers omit it.
                if (link == SystemLinkState.Disconnected) continue;

                bool isFull = system.id == currentSystemId;
                if (isFull)
                {
                    AppendLiveFleet(system, link, fleet);
                    AppendLiveThreats(system, link, threats);
                }
                else
                {
                    SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(system.id);
                    AppendStoredFleet(delta, system, link, fleet);
                    AppendStoredThreats(delta, system, link, threats);
                }

                AppendLogistics(LogisticsCentreDirectory.Read(system.id), system, link, logistics);

                try
                {
                    SystemAttentionInputs attention = AttentionQuery.BuildInputs(system.id, currentSystemId, knowledge);
                    AppendThreatAttention(attention, system, link, threats);
                }
                catch (InvalidOperationException)
                {
                    // The map may open during composition. Missing authorities mean no marker,
                    // never a guessed warning.
                }
            }

            Sort(fleet);
            Sort(logistics);
            Sort(threats);
            return new StrategicMapOperationalSnapshot(fleet, logistics, threats);
        }

        private bool TrySessionState(out string currentSystemId, out SystemKnowledgeService knowledge)
        {
            currentSystemId = null;
            knowledge = null;
            if (context == null) return false;
            try
            {
                currentSystemId = context.Streaming?.FullSystemId;
                knowledge = context.NavigationDirector?.Knowledge;
                return !string.IsNullOrEmpty(currentSystemId) && knowledge != null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static SystemLinkState SafeLink(string systemId, string currentSystemId,
            SystemKnowledgeService knowledge)
        {
            try { return SystemStatusQuery.Link(systemId, currentSystemId, knowledge); }
            catch (InvalidOperationException) { return SystemLinkState.Disconnected; }
        }

        private static void AppendLiveFleet(StrategicMapUniverseSnapshot.SystemRow system,
            SystemLinkState link, List<StrategicMapOperationalMarker> into)
        {
            int count = 0;
            int stranded = 0;
            foreach (KeyValuePair<string, VehicleAgent> pair in VehicleAgent.Registry)
            {
                VehicleAgent vehicle = pair.Value;
                if (vehicle == null || !vehicle.gameObject.activeInHierarchy) continue;
                if (vehicle.HomeSystemId != system.id) continue;
                count++;
                if (vehicle.Stranded) stranded++;
            }

            if (count == 0) return;
            string label = $"Filo · {count} sahadaki araç";
            if (stranded > 0) label += $" · {stranded} arızalı";
            into.Add(new StrategicMapOperationalMarker(
                "fleet:summary:" + system.id, system.id, system.galaxyId,
                label + " · sistem göstergesi", StrategicMapOperationalKind.Fleet,
                system.Position,
                isLastKnown: link == SystemLinkState.LastKnown,
                isSchematic: true,
                needsAttention: stranded > 0));
        }

        internal static void AppendStoredFleet(SystemDelta delta,
            StrategicMapUniverseSnapshot.SystemRow system, SystemLinkState link,
            List<StrategicMapOperationalMarker> into)
        {
            if (delta == null || system == null || into == null || link == SystemLinkState.Disconnected) return;
            int count = 0;
            int stranded = 0;
            for (int i = 0; i < delta.vehicles.Count; i++)
            {
                VehicleDto vehicle = delta.vehicles[i];
                if (vehicle == null || string.IsNullOrEmpty(vehicle.vehicleId)) continue;
                count++;
                if (vehicle.stranded) stranded++;
            }

            if (count == 0) return;
            string label = $"Filo · {count} sahadaki araç";
            if (stranded > 0) label += $" · {stranded} arızalı";
            into.Add(new StrategicMapOperationalMarker(
                "fleet:summary:" + system.id, system.id, system.galaxyId,
                label + " · sistem göstergesi", StrategicMapOperationalKind.Fleet,
                system.Position,
                isLastKnown: link == SystemLinkState.LastKnown,
                isSchematic: true,
                needsAttention: stranded > 0));
        }

        internal static void AppendLogistics(LogisticsCentreReading reading,
            StrategicMapUniverseSnapshot.SystemRow system, SystemLinkState link,
            List<StrategicMapOperationalMarker> into)
        {
            if (reading == null || system == null || into == null || link == SystemLinkState.Disconnected) return;

            bool schematic = !reading.IsMaterialized;
            Vector3 position = schematic
                ? system.Position
                : reading.Live.AbsolutePosition / StrategicMapOrbits.MapUnit;
            bool attention = reading.State == LogisticsCenterState.Disabled || reading.FillWarningActive;
            for (int i = 0; i < reading.Routes.Count && !attention; i++)
                attention = reading.Routes[i].WaitReason != RouteWaitReason.None;

            string label = $"Lojistik Merkezi T{(int)reading.Tier} · {LogisticsStateLabel(reading.State)}";
            if (schematic) label += " · sistem göstergesi";
            into.Add(new StrategicMapOperationalMarker(
                "logistics:" + system.id, system.id, system.galaxyId, label,
                StrategicMapOperationalKind.Logistics, position,
                isLastKnown: link == SystemLinkState.LastKnown,
                isSchematic: schematic,
                needsAttention: attention));
        }

        private static void AppendLiveThreats(StrategicMapUniverseSnapshot.SystemRow system,
            SystemLinkState link, List<StrategicMapOperationalMarker> into)
        {
            int count = 0;
            int combatCapable = 0;
            int telegraphing = 0;
            foreach (KeyValuePair<string, EnemyAgent> pair in EnemyAgent.All)
            {
                EnemyAgent enemy = pair.Value;
                if (enemy == null || enemy.Destroyed || !enemy.gameObject.activeInHierarchy) continue;
                count++;
                if (enemy.Def != null && enemy.Def.IsCombatCapable) combatCapable++;
                if (enemy.IsTelegraphing) telegraphing++;
            }

            if (count == 0) return;
            string label = $"Tehdit teması · {count}";
            if (telegraphing > 0) label += $" · {telegraphing} saldırı hazırlıyor";
            into.Add(new StrategicMapOperationalMarker(
                "threat:summary:" + system.id, system.id, system.galaxyId,
                label + " · sistem göstergesi", StrategicMapOperationalKind.Threat,
                system.Position,
                isLastKnown: link == SystemLinkState.LastKnown,
                isSchematic: true,
                needsAttention: combatCapable > 0 || telegraphing > 0));
        }

        internal static void AppendStoredThreats(SystemDelta delta,
            StrategicMapUniverseSnapshot.SystemRow system, SystemLinkState link,
            List<StrategicMapOperationalMarker> into)
        {
            if (delta == null || system == null || into == null || link == SystemLinkState.Disconnected) return;
            int count = 0;
            int combatCapable = 0;
            int telegraphing = 0;
            for (int i = 0; i < delta.enemies.Count; i++)
            {
                EnemyDto enemy = delta.enemies[i];
                if (enemy == null || string.IsNullOrEmpty(enemy.enemyId)) continue;
                EnemyDef def = EnemyCatalog.Get(enemy.defId);
                count++;
                if (def != null && def.IsCombatCapable) combatCapable++;
                if (enemy.telegraphRemaining > 0f) telegraphing++;
            }

            bool remoteProbe = delta.remoteProbe != null
                && (RemoteProbePhase)delta.remoteProbe.phase != RemoteProbePhase.None
                && (RemoteProbePhase)delta.remoteProbe.phase != RemoteProbePhase.Resolved;
            if (count == 0 && !remoteProbe) return;

            string label = remoteProbe && count == 0
                ? "Uzak prob teması"
                : $"Tehdit teması · {count + (remoteProbe ? 1 : 0)}";
            if (telegraphing > 0) label += $" · {telegraphing} saldırı hazırlıyor";
            into.Add(new StrategicMapOperationalMarker(
                "threat:summary:" + system.id, system.id, system.galaxyId,
                label + " · sistem göstergesi", StrategicMapOperationalKind.Threat,
                system.Position,
                isLastKnown: link == SystemLinkState.LastKnown,
                isSchematic: true,
                needsAttention: remoteProbe || combatCapable > 0 || telegraphing > 0));
        }

        internal static void AppendThreatAttention(SystemAttentionInputs attention,
            StrategicMapUniverseSnapshot.SystemRow system, SystemLinkState link,
            List<StrategicMapOperationalMarker> into)
        {
            if (system == null || into == null || link == SystemLinkState.Disconnected) return;

            string label = null;
            if (attention.AnyCommandDisabled) label = "Komuta yapısı devre dışı";
            else if (attention.AnyPlanetContested) label = "Tartışmalı gezegen";
            else if (attention.RaidDetected)
                label = attention.RaidEtaSeconds <= 0f
                    ? "Baskın aktif"
                    : $"Baskın yaklaşıyor · {attention.RaidEtaSeconds:0} sn";

            if (label == null) return;
            for (int i = 0; i < into.Count; i++)
            {
                StrategicMapOperationalMarker existing = into[i];
                if (existing.SystemId != system.id) continue;
                into[i] = new StrategicMapOperationalMarker(
                    existing.Id, existing.SystemId, existing.GalaxyId,
                    label + " · " + existing.Label, existing.Kind, existing.Position,
                    existing.IsLastKnown, existing.IsSchematic, needsAttention: true);
                return;
            }

            into.Add(new StrategicMapOperationalMarker(
                "threat:summary:" + system.id, system.id, system.galaxyId,
                label + " · sistem göstergesi", StrategicMapOperationalKind.Threat,
                system.Position,
                isLastKnown: link == SystemLinkState.LastKnown,
                isSchematic: true,
                needsAttention: true));
        }

        private static string LogisticsStateLabel(LogisticsCenterState state) => state switch
        {
            LogisticsCenterState.Constructing => "İnşa ediliyor",
            LogisticsCenterState.Active => "Aktif",
            LogisticsCenterState.Disabled => "Devre dışı",
            _ => "Söküldü"
        };

        private static void Sort(List<StrategicMapOperationalMarker> markers) =>
            markers.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }
}
