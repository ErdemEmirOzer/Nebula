using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A §8: everything <see cref="RemoteRaidTick"/> needs for one
    /// non-Full system's raid progression, sourced from that system's
    /// <see cref="SystemDelta"/> (structures/drills as DTOs) instead of the
    /// live <see cref="ExtractionState"/> collections a Full system's
    /// <see cref="Combat.RaidScheduler"/> reads — the same rules
    /// (RaidComposer/EconomicValueResolver/WarningResolver/RaidOutcomeResolver/
    /// RaidSalvageResolver/ThreatPressureState), a different, headless data
    /// source. Dominance is intentionally NOT read from here: <see cref="ExtractionState.GetDominance"/>
    /// is a plain global dictionary that already works identically for any
    /// system regardless of streaming state, so it is called directly.
    /// </summary>
    public class RemoteRaidContext
    {
        public SystemDelta Delta { get; }
        public SystemManifest Manifest { get; }
        public Combat.ThreatPressureState Pressure { get; }
        public readonly List<RemoteEnemyRuntime> Enemies = new List<RemoteEnemyRuntime>();
        public readonly List<PendingAttack> PendingAttacks = new List<PendingAttack>();

        private int nextAttackSequence;

        public RemoteRaidContext(SystemDelta delta, SystemManifest manifest, Combat.ThreatPressureState pressure)
        {
            Delta = delta; Manifest = manifest; Pressure = pressure;
        }

        public string NextAttackId() => $"remote-attack-{Delta.systemId}-{nextAttackSequence++:0000}";

        /// <summary>
        /// Reconstructs a full working context from a SystemDelta's own DTOs --
        /// same reconstruct-tick-writeback shape SystemStreamingDirector.TickDeltaDataOnly
        /// already uses for drills/routes, so raid ticking never needs a
        /// persistent cross-tick runtime cache of its own.
        /// </summary>
        public static RemoteRaidContext LoadFromDelta(SystemDelta delta, SystemManifest manifest)
        {
            Combat.ThreatPressureState pressure = new Combat.ThreatPressureState();
            Extraction.ThreatPressureDto p = delta.threatPressure;
            if (p != null)
            {
                pressure.RestoreFromSave(p.pressure, p.graceRemainingActiveMinutes, p.foreignPresenceActive, p.foreignPresenceActiveMinutesElapsed,
                    (Combat.RaidPhase)p.phase, p.stage >= 0 ? (Combat.RaidStage)p.stage : (Combat.RaidStage?)null,
                    p.doctrine >= 0 ? (Combat.RaidDoctrine)p.doctrine : (Combat.RaidDoctrine?)null,
                    p.targetPlanetWorldId, p.compositionDefIds, ulong.TryParse(p.encounterSeed, out ulong seed) ? seed : 0UL,
                    p.warningRemainingSeconds, p.elapsedActiveSeconds, p.playerDealtMeaningfulDamage, p.playerIssuedValidFocusCommand,
                    p.playerActiveDefenseParticipation, p.playerWasShutdownDuringRaid, p.anyResourceStolen, p.resultApplied, p.salvageApplied,
                    p.pendingProbeEffectConsumedThisRaid, p.firstControlledGraceGranted, p.spawnedEnemyIds, p.wasTargetContestedDuringRaid);
            }

            var ctx = new RemoteRaidContext(delta, manifest, pressure);
            foreach (Extraction.EnemyDto e in delta.enemies)
            {
                if (e == null) continue;
                Combat.EnemyDef def = Combat.EnemyCatalog.Get(e.defId);
                if (def == null) continue;
                var durability = new Extraction.Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0f);
                durability.RestoreFromSave(e.shield, e.armor, e.hull, e.regenTimer);
                var ai = new Combat.EnemyAiState(def);
                ai.RestoreFromSave((Combat.EnemyAiPhase)e.aiPhase, e.targetId, e.scanElapsed, e.continuousEscapeSeconds);
                var runtime = new RemoteEnemyRuntime(e.enemyId, e.defId, def, ai, durability, new Vector3(e.px, e.py, e.pz), new Vector3(e.vx, e.vy, e.vz))
                {
                    StructureTargetId = e.structureTargetId, DisruptRemaining = e.disruptRemaining,
                    DisruptImmunityRemaining = e.disruptImmunityRemaining, TelegraphRemaining = e.telegraphRemaining
                };
                foreach (Extraction.CargoItemDto c in e.looterCargo) runtime.LooterCargo.Add((c.itemId, c.amount));
                ctx.Enemies.Add(runtime);
            }

            foreach (Sectors.PendingAttackDto a in delta.pendingAttacks)
            {
                if (a == null || a.applied) continue; // an already-applied/consumed attack is never restored -- it simply no longer exists (VS-016A.1 §1).
                ctx.PendingAttacks.Add(PendingAttack.Restore(a.attackId, a.attackerId, a.targetId, (Combat.ProjectileKind)a.kind,
                    (Combat.ProjectileFaction)a.faction, a.damageAmount, a.remainingFlightSeconds, a.splashRadius, a.splashMaxFractionOfDirect,
                    a.isEm, a.emDisruptSeconds, a.emImmunityAfterDisruptSeconds, a.interceptable, a.interceptionDurabilityRemaining, a.applied));
            }
            return ctx;
        }

        /// <summary>Writes this tick's results back into the delta's own DTOs -- same direction/shape as SystemStreamingDirector.CaptureCombat.</summary>
        public void SaveToDelta()
        {
            Combat.ThreatPressureState pr = Pressure;
            Delta.threatPressure = new Extraction.ThreatPressureDto
            {
                pressure = pr.Pressure, graceRemainingActiveMinutes = pr.GraceRemainingActiveMinutes,
                foreignPresenceActive = pr.ForeignPresenceActive, foreignPresenceActiveMinutesElapsed = pr.ForeignPresenceActiveMinutesElapsed,
                firstControlledGraceGranted = pr.FirstControlledGraceGranted,
                phase = (int)pr.Phase, stage = pr.Stage.HasValue ? (int)pr.Stage.Value : -1, doctrine = pr.Doctrine.HasValue ? (int)pr.Doctrine.Value : -1,
                targetPlanetWorldId = pr.TargetPlanetWorldId, encounterSeed = pr.EncounterSeed.ToString(),
                warningRemainingSeconds = pr.WarningRemainingSeconds, elapsedActiveSeconds = pr.ElapsedActiveSeconds,
                playerDealtMeaningfulDamage = pr.PlayerDealtMeaningfulDamage, playerIssuedValidFocusCommand = pr.PlayerIssuedValidFocusCommand,
                playerActiveDefenseParticipation = pr.PlayerActiveDefenseParticipation, playerWasShutdownDuringRaid = pr.PlayerWasShutdownDuringRaid,
                anyResourceStolen = pr.AnyResourceStolen, resultApplied = pr.ResultApplied, salvageApplied = pr.SalvageApplied,
                pendingProbeEffectConsumedThisRaid = pr.PendingProbeEffectConsumedThisRaid, wasTargetContestedDuringRaid = pr.WasTargetContestedDuringRaid
            };
            Delta.threatPressure.compositionDefIds.AddRange(pr.CompositionDefIds);
            Delta.threatPressure.spawnedEnemyIds.AddRange(pr.SpawnedEnemyIds);

            Delta.enemies.Clear();
            foreach (RemoteEnemyRuntime en in Enemies)
            {
                if (en.Destroyed) continue;
                var dto = new Extraction.EnemyDto
                {
                    enemyId = en.Id, defId = en.DefId, px = en.Position.x, py = en.Position.y, pz = en.Position.z,
                    vx = en.Velocity.x, vy = en.Velocity.y, vz = en.Velocity.z, rx = 0f, ry = 0f, rz = 0f, rw = 1f,
                    aiPhase = (int)en.Ai.Phase, targetId = en.Ai.TargetId, scanElapsed = en.Ai.ScanElapsed, continuousEscapeSeconds = 0f,
                    shield = en.Durability.Shield, armor = en.Durability.Armor, hull = en.Durability.Hull, regenTimer = en.Durability.SavedRegenTimer,
                    structureTargetId = en.StructureTargetId, disruptRemaining = en.DisruptRemaining,
                    disruptImmunityRemaining = en.DisruptImmunityRemaining, telegraphRemaining = en.TelegraphRemaining
                };
                foreach (var (itemId, amount) in en.LooterCargo) dto.looterCargo.Add(new Extraction.CargoItemDto { itemId = itemId, amount = amount });
                Delta.enemies.Add(dto);
            }

            Delta.pendingAttacks.Clear();
            foreach (PendingAttack a in PendingAttacks)
            {
                if (a.Applied) continue; // consumed this tick -- never written back, never re-applied on a later load (VS-016A.1 §1).
                Delta.pendingAttacks.Add(new Sectors.PendingAttackDto
                {
                    attackId = a.AttackId, attackerId = a.AttackerId, targetId = a.TargetId, kind = (int)a.Kind, faction = (int)a.Faction,
                    damageAmount = a.DamageAmount, remainingFlightSeconds = a.RemainingFlightSeconds,
                    splashRadius = a.SplashRadius, splashMaxFractionOfDirect = a.SplashMaxFractionOfDirect,
                    isEm = a.IsEm, emDisruptSeconds = a.EmDisruptSeconds, emImmunityAfterDisruptSeconds = a.EmImmunityAfterDisruptSeconds,
                    interceptable = a.Interceptable, interceptionDurabilityRemaining = a.InterceptionDurabilityRemaining, applied = a.Applied
                });
            }
        }

        public PendingAttack FindPendingAttack(string attackId)
        {
            foreach (PendingAttack a in PendingAttacks) if (a.AttackId == attackId) return a;
            return null;
        }

        public RemoteEnemyRuntime FindEnemy(string id)
        {
            foreach (RemoteEnemyRuntime e in Enemies) if (e.Id == id) return e;
            return null;
        }

        public bool HasActiveDefenseOn(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return false;
            foreach (StructureDto s in Delta.structures)
                if (s != null && s.worldId == worldId && SurfaceStructureCatalog.IsActiveDefenseWeapon((SurfaceStructureType)s.type)
                    && s.state == (int)DrillLifecycle.Active) return true;
            return false;
        }

        /// <summary>The remote-combat structure target priority: active defense first, then Command, then any other active structure on the planet -- mirrors HasActiveDefenseOn's own weapon-first bias.</summary>
        public StructureDto ChooseStructureTarget(string worldId)
        {
            StructureDto command = null, other = null;
            foreach (StructureDto s in Delta.structures)
            {
                if (s == null || s.worldId != worldId || s.state != (int)DrillLifecycle.Active) continue;
                if (SurfaceStructureCatalog.IsActiveDefenseWeapon((SurfaceStructureType)s.type)) return s;
                if ((SurfaceStructureType)s.type == SurfaceStructureType.Command) command = s;
                else other ??= s;
            }
            return command ?? other;
        }

        public DrillDto ChooseDrillTarget(string worldId)
        {
            foreach (DrillDto d in Delta.drills)
                if (d != null && d.worldId == worldId && d.state == (int)DrillLifecycle.Active && d.stored > 0.01f) return d;
            return null;
        }

        public static string StructureTargetId(StructureDto s) => s.worldId + "/" + s.nodeId;

        public StructureDto FindStructureByTargetId(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return null;
            foreach (StructureDto s in Delta.structures) if (s != null && StructureTargetId(s) == targetId) return s;
            return null;
        }

        public VehicleDto FindVehicle(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return null;
            foreach (VehicleDto v in Delta.vehicles) if (v != null && v.vehicleId == vehicleId) return v;
            return null;
        }

        /// <summary>Every active planetary Laser (T1 or T2 -- interception always uses the shared T1 baseline rate, matching ProjectileObject.TickInterception's own deliberate simplification), canonical structure-ID order.</summary>
        public List<StructureDto> ActiveLasersOrderedById()
        {
            var list = new List<StructureDto>();
            foreach (StructureDto s in Delta.structures)
                if (s != null && (SurfaceStructureType)s.type == SurfaceStructureType.Laser && s.state == (int)DrillLifecycle.Active) list.Add(s);
            list.Sort((a, b) => string.CompareOrdinal(StructureTargetId(a), StructureTargetId(b)));
            return list;
        }

        /// <summary>Every active defense weapon (Laser or Plasma), canonical structure-ID order -- the tie-break VS-016A.1 §2A explicitly requires so list/call order never changes which weapon acts first.</summary>
        public List<StructureDto> ActiveDefenseWeaponsOrderedById(string worldId)
        {
            var list = new List<StructureDto>();
            foreach (StructureDto s in Delta.structures)
                if (s != null && s.worldId == worldId && SurfaceStructureCatalog.IsActiveDefenseWeapon((SurfaceStructureType)s.type) && s.state == (int)DrillLifecycle.Active)
                    list.Add(s);
            list.Sort((a, b) => string.CompareOrdinal(StructureTargetId(a), StructureTargetId(b)));
            return list;
        }

        /// <summary>Every real, assigned, non-retreating remote Escort protecting something in this system -- canonical vehicle-ID order.</summary>
        public List<VehicleDto> ActiveEscortsOrderedById()
        {
            var list = new List<VehicleDto>();
            foreach (VehicleDto v in Delta.vehicles)
            {
                if (v == null || v.escortState == null || !v.escortState.active) continue;
                if ((Hangar.EscortCommand)v.escortState.command == Hangar.EscortCommand.Retreat) continue;
                if ((Hangar.EscortSubState)v.escortState.subState == Hangar.EscortSubState.Retreating) continue;
                list.Add(v);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.vehicleId, b.vehicleId));
            return list;
        }

        public int CountControlledPlanets()
        {
            int count = 0;
            foreach (PlanetManifest p in Manifest.Planets) if (ExtractionState.GetDominance(p.PlanetId).State == DominanceState.Controlled) count++;
            return count;
        }

        public List<(string worldId, float economicValue)> BuildEconomicCandidates()
        {
            var list = new List<(string, float)>();
            bool starConnected = Combat.EconomicValueResolver.HasStarConnection(Delta.systemId);
            bool pendingProbe = Combat.CombatCampaignState.FirstProbe.PendingReconEffect != null;
            foreach (PlanetManifest p in Manifest.Planets)
            {
                if (ExtractionState.GetDominance(p.PlanetId).State != DominanceState.Controlled) continue;
                int activeDrills = 0; float fillTotal = 0f; int fillCount = 0;
                foreach (DrillDto d in Delta.drills)
                {
                    if (d == null || d.worldId != p.PlanetId || d.state != (int)DrillLifecycle.Active) continue;
                    activeDrills++;
                    fillTotal += d.stored / Mathf.Max(1f, DrillCapacityFor(d));
                    fillCount++;
                }
                float avgFill = fillCount > 0 ? fillTotal / fillCount : 0f;
                float value = Combat.EconomicValueResolver.Resolve(p.Rarity, activeDrills, avgFill, starConnected, pendingProbe);
                list.Add((p.PlanetId, value));
            }
            return list;
        }

        private static float DrillCapacityFor(DrillDto d) => 1000f; // capacity isn't itself persisted per-drill (derived from PlanetProfile at restore); a stable non-zero fallback keeps fill-fraction bounded and non-crashing.

        /// <summary>A real, currently-launched (not docked) Scout physically present in this remote system -- SystemDelta.vehicles equivalent of WarningResolver's live-vehicle scan.</summary>
        public bool HasRealActiveScout(out int scoutMk)
        {
            scoutMk = 0;
            foreach (VehicleDto v in Delta.vehicles)
            {
                if (v == null || (Hangar.VehicleClass)v.vehicleClass != Hangar.VehicleClass.Scout) continue;
                if (v.phase == (int)Hangar.VehiclePhase.InHangar) continue;
                scoutMk = v.mk;
                return true;
            }
            return false;
        }

        /// <summary>Deterministic, seed+index-derived system-local spawn offset -- same shape as RaidScheduler's own DeterministicDirection; distance anchors on the target planet's own stable OrbitRadius (manifest data, always available regardless of materialization) rather than a live planet Transform.</summary>
        public Vector3 SpawnPosition(ulong seed, int index, string targetWorldId)
        {
            float azimuth = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "remote-raid/spawn/azimuth/" + index)) * Mathf.PI * 2f;
            float elevation = (ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "remote-raid/spawn/elevation/" + index)) - 0.5f) * Mathf.PI * 0.6f;
            float cosE = Mathf.Cos(elevation);
            Vector3 direction = new Vector3(cosE * Mathf.Cos(azimuth), Mathf.Sin(elevation), cosE * Mathf.Sin(azimuth));
            PlanetManifest target = WorldIdCodec.TryParsePlanetIndex(targetWorldId, out int planetIndex) ? Manifest.FindPlanet(planetIndex) : null;
            float baseRadius = target != null ? target.PlanetRadius : 200f;
            float distance = Mathf.Max(500f, baseRadius * 3f);
            return direction * distance;
        }
    }
}
