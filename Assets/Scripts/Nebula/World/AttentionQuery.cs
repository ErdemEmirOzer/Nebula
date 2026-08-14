using System.Collections.Generic;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Navigation;
using Nebula.Sectors;

namespace Nebula.World
{
    /// <summary>
    /// Orchestration layer (VS-010 §4) that assembles real
    /// <see cref="SystemAttentionInputs"/> for one system — live if Full,
    /// else from its persisted <see cref="SystemDelta"/> — and feeds them to
    /// the pure <see cref="AttentionResolver"/>. VS-014 §16: RaidDetected/
    /// RaidEtaSeconds are now real for the CURRENTLY FULL system (the only
    /// place a live ThreatPressureState exists this slice — a raid frozen in
    /// a demoted/Warm system isn't yet visible here until save v12/SystemDelta
    /// wiring persists it, VS-014 §17); every other system still correctly
    /// reports no raid rather than fabricating one.
    /// </summary>
    public static class AttentionQuery
    {
        public static SystemAttentionInputs BuildInputs(string systemId, string shipCurrentSystemId, SystemKnowledgeService knowledge)
        {
            var result = new SystemAttentionInputs { RaidDetected = false, RaidEtaSeconds = float.MaxValue, MinRecoveryTimeRemaining = float.MaxValue };
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return result;

            result.IsLive = SystemStatusQuery.Link(systemId, shipCurrentSystemId, knowledge) == SystemLinkState.Live;

            Dictionary<string, DominanceState> states = SystemStatusQuery.PlanetDominanceStates(systemId);
            int controlled = 0;
            foreach (DominanceState s in states.Values) if (s == DominanceState.Controlled) controlled++;
            result.AffectedControlledPlanetCount = controlled;

            bool isFull = StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.GetState(systemId) == SystemStreamingState.Full;

            if (isFull)
            {
                CombatEncounterDirector director = StarSystemRegistry.Streaming?.CombatEncounter;
                if (director != null && director.SystemId == systemId)
                {
                    ThreatPressureState pressure = director.Pressure;
                    if (pressure.Phase == RaidPhase.Warning)
                    {
                        result.RaidDetected = true;
                        result.RaidEtaSeconds = pressure.WarningRemainingSeconds;
                    }
                    else if (pressure.Phase == RaidPhase.Active)
                    {
                        result.RaidDetected = true;
                        result.RaidEtaSeconds = 0f;
                    }
                }

                foreach (PlanetManifest p in manifest.Planets)
                {
                    if (states.TryGetValue(p.PlanetId, out DominanceState s) && s == DominanceState.Contested)
                    {
                        result.AnyPlanetContested = true;
                        PlanetDominance dom = ExtractionState.GetDominance(p.PlanetId);
                        float remaining = PlanetDominance.RecoverySeconds - dom.RecoveryTimer;
                        if (remaining < result.MinRecoveryTimeRemaining) result.MinRecoveryTimeRemaining = remaining;
                    }
                    if (ExtractionState.CommandDisabled(p.PlanetId)) result.AnyCommandDisabled = true;
                }

                bool anyActive = false, allFull = true;
                foreach (DrillStructure d in ExtractionState.Drills)
                {
                    if (d == null || WorldIdCodec.SystemIdFromPlanetWorldId(d.WorldId) != systemId) continue;
                    if (d.Runtime.State != DrillLifecycle.Active) continue;
                    anyActive = true;
                    if (!d.Runtime.IsFull) allFull = false;
                }
                result.AllActiveDrillsStalledFull = anyActive && allFull;

                if (StarSystemRegistry.Streaming != null)
                {
                    ShipHangar hangar = StarSystemRegistry.Streaming.HangarRef;
                    if (hangar != null)
                    {
                        foreach (VehicleAgent v in hangar.Vehicles)
                        {
                            if (v == null || v.HomeSystemId != systemId) continue;
                            if (v.Mission.Phase != VehiclePhase.Waiting || v.HeldAmount <= 0.0001f) continue;
                            bool hasDrillTarget = false;
                            foreach (DrillStructure d in ExtractionState.Drills)
                                if (d != null && d.WorldId == v.Mission.TargetId) { hasDrillTarget = true; break; }
                            if (!hasDrillTarget) { result.VehicleStrandedNoTarget = true; break; }
                        }
                    }
                }
            }
            else
            {
                SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
                if (delta != null)
                {
                    // VS-016A: a remote (non-Full) system's own raid, now that RemoteRaidTick actually
                    // progresses ThreatPressureDto for it -- previously this branch never looked at
                    // threatPressure at all, so a raid frozen/running in a demoted system never surfaced here.
                    if (delta.threatPressure != null)
                    {
                        var remotePhase = (RaidPhase)delta.threatPressure.phase;
                        if (remotePhase == RaidPhase.Warning) { result.RaidDetected = true; result.RaidEtaSeconds = delta.threatPressure.warningRemainingSeconds; }
                        else if (remotePhase == RaidPhase.Active) { result.RaidDetected = true; result.RaidEtaSeconds = 0f; }
                    }

                    foreach (DominanceDto dto in delta.dominance)
                    {
                        if ((DominanceState)dto.state != DominanceState.Contested) continue;
                        result.AnyPlanetContested = true;
                        float remaining = PlanetDominance.RecoverySeconds - dto.recoveryTimer;
                        if (remaining < result.MinRecoveryTimeRemaining) result.MinRecoveryTimeRemaining = remaining;
                    }
                    foreach (StructureDto dto in delta.structures)
                        if ((SurfaceStructureType)dto.type == SurfaceStructureType.Command && (DrillLifecycle)dto.state == DrillLifecycle.Disabled)
                            result.AnyCommandDisabled = true;

                    bool anyActive = false, allFull = true;
                    foreach (DrillDto dto in delta.drills)
                    {
                        if ((DrillLifecycle)dto.state != DrillLifecycle.Active) continue;
                        anyActive = true;
                        PlanetProfile profile = ExtractionState.GetProfile(dto.worldId);
                        float capacity = profile != null ? Extraction.ExtractionCatalog.StorageCapacity(profile.Rarity, dto.tier) : dto.stored;
                        if (dto.stored < capacity - 0.0001f) allFull = false;
                    }
                    result.AllActiveDrillsStalledFull = anyActive && allFull;

                    foreach (VehicleDto v in delta.vehicles)
                    {
                        if ((VehiclePhase)v.phase != VehiclePhase.Waiting || v.heldAmount <= 0.0001f) continue;
                        bool hasDrillTarget = false;
                        foreach (DrillDto d in delta.drills) if (d.worldId == v.targetId) { hasDrillTarget = true; break; }
                        if (!hasDrillTarget) { result.VehicleStrandedNoTarget = true; break; }
                    }
                }
            }

            return result;
        }

        /// <summary>Builds a card for every candidate system that has valid data (Live or LastKnown), then returns the global top 3.</summary>
        public static List<AttentionCard> TopThreeCards(IEnumerable<string> candidateSystemIds, string shipCurrentSystemId, SystemKnowledgeService knowledge)
        {
            var cards = new List<AttentionCard>();
            foreach (string systemId in candidateSystemIds)
            {
                SystemLinkState link = SystemStatusQuery.Link(systemId, shipCurrentSystemId, knowledge);
                if (link == SystemLinkState.Disconnected) continue;
                SystemAttentionInputs inputs = BuildInputs(systemId, shipCurrentSystemId, knowledge);
                AttentionCard? card = AttentionResolver.ResolveForSystem(systemId, inputs);
                if (card.HasValue) cards.Add(card.Value);
            }
            return AttentionResolver.TopThree(cards);
        }
    }
}
