using System;
using System.Collections.Generic;
using Nebula.Extraction;

namespace Nebula.Sectors
{
    /// <summary>
    /// The ONLY mutable state a real system carries beyond its immutable
    /// manifest (VS-008 §5): scans, drills, structures, dominance, planet
    /// orbital phase snapshots, and any vehicle currently physically present
    /// there while not docked on the ship. Reuses the existing VS-001..007
    /// save DTOs directly (no parallel duplicate shapes) so capture/restore
    /// stays a single source of truth. Never carries ship-owned state
    /// (cargo, module jobs, Hangar production, docked vehicles) — those stay
    /// with the ship's own save data regardless of which system it's in.
    /// </summary>
    [Serializable]
    public class SystemDelta
    {
        public string systemId;
        public List<string> scanned = new List<string>();
        public List<DrillDto> drills = new List<DrillDto>();
        public List<StructureDto> structures = new List<StructureDto>();
        public List<DominanceDto> dominance = new List<DominanceDto>();
        public List<PlanetSnapDto> planetSnapshots = new List<PlanetSnapDto>();
        public List<VehicleDto> vehicles = new List<VehicleDto>();
        /// <summary>VS-010 §5: planet world ids currently Abandoned by physical evacuation — a separate flag, never a DominanceState value, never destroyed/reset by itself.</summary>
        public List<string> abandonedPlanets = new List<string>();

        /// <summary>VS-011: at most one entry (a real system has at most one Logistics Center).</summary>
        public List<LogisticsCenterDto> logisticsCenters = new List<LogisticsCenterDto>();
        /// <summary>VS-011: this system's persistent Collector routes.</summary>
        public List<CollectorRouteDto> collectorRoutes = new List<CollectorRouteDto>();
        /// <summary>VS-011: running active-simulation-seconds counter for this delta, used only as the "now" reference for a route's dock-wait tie-break while ticked remotely (mirrors Time.time's role on the live path).</summary>
        public float simElapsedSeconds;

        /// <summary>VS-012: this system's POI mutable state (knowledge/timers/salvage/facility) — at most 2 entries. The immutable POI manifest itself (type/risk/position/size/seeds) is never stored here, exactly like a planet's own manifest — it is re-derived from (seed, PoiVisitLedger) by Poi.PoiManifestGenerator.</summary>
        public List<PoiDto> pois = new List<PoiDto>();

        // --- VS-013: this system's combat state (only ever non-empty while it was
        // the Full system at save time — encounters/enemies/projectiles never exist
        // in a Warm or sparse-ticked system, VS-013 §5) --------------------------
        public List<EnemyDto> enemies = new List<EnemyDto>();
        public List<ProjectileDto> projectiles = new List<ProjectileDto>();
        public List<CombatWreckageDto> combatWreckage = new List<CombatWreckageDto>();
        public bool combatEncounterOpen;
        public float combatEncounterSinceLastThreat;

        // --- VS-014 (schema v12): this system's pressure/grace/raid-flow + per-
        // planet defense coordination state -- absent/default on a pre-v12
        // delta, which is already the correct "no pressure ever accumulated,
        // Role Priority/no focus" start state (zero migration code needed).
        public ThreatPressureDto threatPressure;
        public List<DefenseCoordinationDto> defenseCoordination = new List<DefenseCoordinationDto>();

        // --- VS-015 (schema v13): this system's single same-system intervention group ---
        // Absent/inactive on a pre-v13 delta -> Manual policy, no members, no target
        // (the correct safe default, zero migration code).
        public InterventionGroupDto interventionGroup;

        // --- VS-016A (schema v14): deterministic remote simulation -------------------
        // Absent/default on a pre-v14 delta -> no Probe encounter, no pending attacks
        // (the correct safe default, zero migration code needed for either).
        public List<PendingAttackDto> pendingAttacks = new List<PendingAttackDto>();
        public RemoteProbeDto remoteProbe;
        /// <summary>Last remote-sim tick expressed as this delta's own simElapsedSeconds reference -- never real/DateTime wall-clock, so offline progress stays exactly zero regardless of how long the save was closed.</summary>
        public float lastRemoteTickSimSeconds;
        /// <summary>VS-016A §9: a single unshown reconnect summary awaiting the player -- cleared the moment it is shown, never re-shown after save/load.</summary>
        public ReconnectSummaryDto pendingReconnectSummary;

        public bool IsEmpty => scanned.Count == 0 && drills.Count == 0 && structures.Count == 0
                                && dominance.Count == 0 && planetSnapshots.Count == 0 && vehicles.Count == 0
                                && abandonedPlanets.Count == 0 && logisticsCenters.Count == 0 && collectorRoutes.Count == 0
                                && pois.Count == 0 && enemies.Count == 0 && projectiles.Count == 0 && combatWreckage.Count == 0
                                && !combatEncounterOpen && threatPressure == null && defenseCoordination.Count == 0
                                && (interventionGroup == null || (interventionGroup.policy == 0 && interventionGroup.memberVehicleIds.Count == 0))
                                && pendingAttacks.Count == 0 && (remoteProbe == null || remoteProbe.phase == 0) && pendingReconnectSummary == null;
    }

    /// <summary>VS-016A §5: one attack carried across a Full-system demotion, through however many remote ticks, and back through promotion -- never applied twice.</summary>
    [Serializable]
    public class PendingAttackDto
    {
        public string attackId, attackerId, targetId;
        public int kind;        // Nebula.Combat.ProjectileKind
        public int faction;     // Nebula.Combat.ProjectileFaction
        public float damageAmount;
        public float remainingFlightSeconds;
        public float splashRadius, splashMaxFractionOfDirect;
        public bool isEm;
        public float emDisruptSeconds, emImmunityAfterDisruptSeconds;
        public bool interceptable;
        public float interceptionDurabilityRemaining;
        public bool applied;
    }

    /// <summary>VS-016A §8: one remote system's own single Probe encounter -- deliberately separate from InterventionGroupDto/ThreatPressureDto (Probe is not a raid stage, never occupies the Major Raid slot).</summary>
    [Serializable]
    public class RemoteProbeDto
    {
        public int phase; // Nebula.Remote.RemoteProbePhase
        public string enemyId;
        public bool scanCompleted;
    }

    /// <summary>VS-016A §9: one merged reconnect summary -- raid result, damage/disabled, stolen/salvaged resources, completed/stalled tasks, logistics/maintenance status. Opening it never grants XP/reward; it is cleared (never re-shown) the moment it's been shown once.</summary>
    [Serializable]
    public class ReconnectSummaryDto
    {
        public string systemId;
        public string raidResultText;
        public bool wasDamagedOrDisabled;
        public List<ItemAmountDto> stolenResources = new List<ItemAmountDto>();
        public List<ItemAmountDto> salvagedResources = new List<ItemAmountDto>();
        public List<string> completedTaskDescriptions = new List<string>();
        public List<string> stalledTaskDescriptions = new List<string>();
        public string logisticsMaintenanceStatusText;
    }

    /// <summary>VS-015 §6: the current system's single physical intervention group.</summary>
    [Serializable]
    public class InterventionGroupDto
    {
        public int policy;     // Nebula.Combat.InterventionPolicy
        public List<string> memberVehicleIds = new List<string>();
        public string activeTargetPlanetId;
        public bool holdingOrRetreating;
        /// <summary>VS-015.1 §1: the frozen known-threat snapshot for the active target at capture time — never recomputed while demoted.</summary>
        public float knownThreatPowerSnapshot;
    }
}
