using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nebula.Extraction
{
    // --- Data-transfer objects (plain, JsonUtility-friendly) -----------------

    [Serializable]
    public class ShipDto
    {
        public float px, py, pz;          // absolute universe position
        public float rx, ry, rz, rw;      // rotation
        public float vx, vy, vz;          // velocity
        public float throttle;
        public string systemStar;
    }

    [Serializable]
    public class DrillDto
    {
        public string worldId, nodeId;
        public int state;
        public float capsuleRemaining, buildRemaining, stored, accumulator;
        public int source;        // DrillStructure.BuildSource
        public int refundParts;

        // --- VS-017A.2 §B5: Drill T1→T2 tier + in-flight upgrade -------------------
        // Fully additive. A pre-VS-017A.2 save simply has none of these fields, so
        // JsonUtility leaves the field initializers in place: tier = 1 (T1) and
        // upgradeActive = false (no in-flight upgrade) -- exactly the correct legacy
        // state, with zero migration code. This is the identical pattern StructureDto.tier
        // has used since VS-014, so it is proven against this project's own save chain.
        public int tier = 1;
        /// <summary>Explicit validity signal for the upgrade block below — never inferred from a null/default nested object, since JsonUtility materializes ghost defaults rather than leaving them null.</summary>
        public bool upgradeActive;
        public float upgradeTotalSeconds, upgradeRemainingSeconds;
        /// <summary>The still-unconsumed remainder of the atomically reserved upgrade cost — load re-reserves exactly this, never a fresh full cost (which would double-charge) and never nothing (which would be a free upgrade).</summary>
        public List<ItemAmountDto> upgradeRemainingCost = new List<ItemAmountDto>();
    }

    [Serializable]
    public class StructureDto
    {
        public string worldId, nodeId;
        public int type;              // SurfaceStructureType
        public int state;             // DrillLifecycle
        public float capsuleRemaining, buildRemaining;
        public float shield, armor, hull, regenTimer;

        /// <summary>VS-015.1 §6: the full build-cost recipe (parts + every material) atomically reserved for this structure — empty once it first reaches Active (the reservation is consumed then, nothing left to restore). Replaces the old parts-only refund fields, which silently lost every special material on a multi-item structure's cancel.</summary>
        public List<ItemAmountDto> buildCost = new List<ItemAmountDto>();
        /// <summary>VS-010 Quick Asset Package only: a pre-computed flat refund debt (mutually exclusive with buildCost — only one of the two is ever non-empty for a given structure).</summary>
        public List<ItemAmountDto> flatRefund = new List<ItemAmountDto>();

        // --- VS-014 §2/§17: tier + in-flight upgrade job -----------------------
        // Absent on a pre-v12 save -> tier defaults to 1 (field initializer),
        // upgrading defaults false -- an old T1-only structure round-trips with
        // zero migration code, exactly the "additive field" pattern every prior
        // slice already used.
        public int tier = 1;
        public bool upgrading;
        public int upgradeTargetTier;
        public float upgradeProgress01;

        /// <summary>VS-015 §5/§10: only populated for a Command structure — null/default-empty on every non-Command structure and on any pre-v13 save (safe default: empty reserve, Manual policy, no active target).</summary>
        public MaintenanceReserveDto maintenanceReserve;

        // --- VS-016A.1 §2A: remote planetary defense firing state -------------------
        // Additive, safe-zero-defaults (absent on any pre-existing save round-trips as
        // "never fired yet, no overheat, no cooldown") -- mirrors SurfaceStructure's own
        // private continuousFireSeconds/overheatCooldownRemaining/plasmaFireCooldownRemaining
        // fields exactly, just persisted so a remote (never-materialized) structure can
        // carry the same real 8s-continuous/3s-overheat and Plasma fire-interval state.
        public float defenseContinuousFireSeconds;
        public float defenseOverheatRemaining;
        public float defensePlasmaCooldownRemaining;
    }

    /// <summary>VS-015 §5: one Command Center's physical Maintenance Reserve.</summary>
    [Serializable]
    public class MaintenanceReserveDto
    {
        public int policy;         // Nebula.Extraction.MaintenancePolicy
        public string activeEngineerTargetId;
        public List<string> queuedTargetIds = new List<string>();
        public List<ItemAmountDto> items = new List<ItemAmountDto>();
    }

    [Serializable]
    public class DominanceDto
    {
        public string worldId;
        public int state;             // DominanceState
        public float stabilizeTimer, recoveryTimer;
    }

    [Serializable]
    public class CargoItemDto { public string itemId; public float amount; }

    /// <summary>
    /// LEGACY / MIGRATION ONLY — one timed processing slot's state, as saves v3
    /// through v14 wrote it.
    /// <para>
    /// The timed bay this described no longer exists: v15 replaced it with a single
    /// inventory-backed bench whose conversions are instant (see
    /// <see cref="ProcessingBenchDto"/>). This DTO is kept solely so a v14 file can
    /// still be READ and migrated — <c>ExtractionDirector.MigrateLegacyProcessing</c>
    /// is its only consumer, and nothing writes it any more. It must never be
    /// populated on the save path or consulted on the production start path; a second
    /// reader of these fields would be the timed system growing back.
    /// </para>
    /// </summary>
    [Serializable]
    public class ProcessorDto
    {
        public int recipeIndex = -1;
        public float batchTimer;
        public bool waiting;
        public string pendingItemId;
        public float pendingAmount;
        public string reservedItemId; // the single input item this slot had reserved, if any
        public float reservedAmount;
    }

    /// <summary>
    /// VS-017A.2 (schema v15): the processing bench — one input item id, a whole-unit
    /// amount, and whatever the v14 migration still owes the player.
    /// <para>
    /// Additive and safe-defaulting: an absent/empty <c>inputItemId</c> round-trips as
    /// an empty bench, which is exactly the state a campaign that never loaded a v14
    /// save is in. Conversion history, the page's selection and any open amount box are
    /// deliberately not here — they are presentation, and persisting them would make a
    /// reload able to disagree with what the player can see.
    /// </para>
    /// </summary>
    [Serializable]
    public class ProcessingBenchDto
    {
        public string inputItemId;
        public int inputAmount;

        /// <summary>Empty on every save that never migrated a v14 file, and empty again once the queue has drained.</summary>
        public List<LegacyProcessingClaimDto> legacyClaims = new List<LegacyProcessingClaimDto>();
    }

    /// <summary>One item the v14 migration rescued and has not yet been able to hand back.</summary>
    [Serializable]
    public class LegacyProcessingClaimDto { public string itemId; public int amount; }

    [Serializable]
    public class ModuleTierDto { public string moduleId; public int tier; }

    /// <summary>One resource line of an active module-upgrade job's reservation, for exact save/restore.</summary>
    [Serializable]
    public class ModuleJobResourceDto { public string itemId; public float amount; }

    [Serializable]
    public class ModuleJobDto
    {
        public string moduleId;
        public int fromTier, toTier;
        public int reservedPoints;
        public List<ModuleJobResourceDto> reservedResources = new List<ModuleJobResourceDto>();
        public float totalSeconds, remainingSeconds;
        public bool paused;
        public float safeStreakSeconds;
    }

    [Serializable]
    public class PlanetSnapDto
    {
        public string worldId;
        public float px, py, pz;          // absolute position at save time
        public float rx, ry, rz, rw;
        public float vx, vy, vz;
    }

    // --- VS-006 (schema v4) ---------------------------------------------------

    [Serializable]
    public class SystemKnowledgeDto { public string systemId; public int level; } // Nebula.Navigation.SystemKnowledgeLevel

    // --- VS-010 (schema v8) ----------------------------------------------------

    /// <summary>One origin(Controlled planet)-&gt;target(real system) frontier-signal claim.</summary>
    [Serializable]
    public class FrontierProvenanceDto { public string origin; public string target; }

    [Serializable]
    public class NavigationDto
    {
        public List<SystemKnowledgeDto> systemKnowledge = new List<SystemKnowledgeDto>();
        // VS-010 §3/§7: replaces the old flat "revealGuard" list — FrontierSignalService
        // is now the single authority for frontier-open provenance (see FrontierSignalService.cs).
        public List<FrontierProvenanceDto> frontierProvenance = new List<FrontierProvenanceDto>();
        public string selectedRouteSystemId;
        public string signalAnalysisTargetSystemId;
        public float signalAnalysisRemainingSeconds;
        public int flightMode;             // Nebula.Navigation.FlightMode
        public float flightModeElapsedSeconds; // ramp/deceleration progress, meaningless in other modes
        public float cruiseSpeedMultiplier;
        public bool headingLockEngaged;
        public float headingX, headingY, headingZ;

        // VS-012 (schema v10): which frontierProvenance origins are one-shot POI signal
        // grants (never backfilled) — see FrontierSignalService.RestoreOneShotOrigins.
        public List<string> oneShotSignalOrigins = new List<string>();
        /// <summary>VS-012: the ordered first-visit list Poi.PoiVisitLedger persists — see Poi.PoiManifestGenerator for why nothing else about POIs needs saving.</summary>
        public List<string> poiVisitOrder = new List<string>();
    }

    // --- VS-007 (schema v5) ----------------------------------------------------

    [Serializable]
    public class HangarJobDto
    {
        // JsonUtility cannot represent a null nested-class field: serializing
        // a genuinely-null HangarJobDto still produces a default instance,
        // and deserializing it back gives a non-null object — so a plain
        // "payload.hangarJob != null" check can never detect "no job" after
        // a real save/load round trip. This explicit flag is the actual
        // signal (defaults to false exactly when no job was captured).
        public bool active;
        public int vehicleClass;   // Nebula.Hangar.VehicleClass
        /// <summary>VS-011: 1 (default) or 2 — meaningless (always effectively 2) for an upgrade job. Absent on a pre-v9 save defaults to Mk I via the field initializer, zero migration code.</summary>
        public int mk = 1;
        public string chosenMaterialId;
        public int reservedParts;
        public int reservedMaterial;
        public float totalSeconds, remainingSeconds;

        // VS-011 §5: Mk I -> Mk II in-place upgrade job fields. Absent on any pre-v9 save (isUpgrade defaults false), so an old save's direct-build job restores exactly as before with zero migration code.
        public bool isUpgrade;
        public string upgradeVehicleId;
        public int reservedSecondMaterial;

        // --- VS-015 §1/§10: generic fixed-recipe job (Mk II direct build or any class's Mk I->Mk II upgrade) ---
        // Absent on any pre-v13 save (targetVehicleId null / reservedMaterials empty) -- an old Collector job
        // keeps restoring exactly through the fields above, zero migration code.
        public string targetVehicleId;
        public List<ItemAmountDto> reservedMaterials = new List<ItemAmountDto>();
    }

    /// <summary>VS-015: a generic (itemId, amount) pair — JsonUtility cannot serialize a ValueTuple, so every new multi-item list uses this instead.</summary>
    [Serializable]
    public class ItemAmountDto
    {
        public string itemId;
        public float amount;
    }

    /// <summary>VS-015 §3/§10: one Engineer maintenance mission — nested inside its vehicle's VehicleDto, active=false when the vehicle isn't running one.</summary>
    [Serializable]
    public class EngineerMissionDto
    {
        public bool active;
        public string missionId;
        public string systemId;
        public int targetKind;     // Nebula.Hangar.EngineerTargetKind
        public string targetId;
        public int workKind;       // Nebula.Hangar.EngineerWorkKind
        public int sourceKind;     // Nebula.Hangar.EngineerSourceKind
        public string chosenMaterialId;
        public bool playerManuallyStarted;
        public int reservedParts;
        public float reservedMaterialAmount;
        public int consumedParts;
        public float consumedMaterialAmount;
        public int phase;          // Nebula.Hangar.EngineerPhase
        public float safeSecondsAccumulated;
        public bool xpAwarded;
        public float plannedRestoredHp;
        public float actualRestoredHpSoFar;
    }

    /// <summary>VS-015 §4/§10: Escort protection assignment — nested inside its vehicle's VehicleDto, active=false when the vehicle isn't escorting.</summary>
    [Serializable]
    public class EscortStateDto
    {
        public bool active;
        public string missionId;
        public string protectedTargetId;
        public int protectedTargetCategory; // Nebula.Combat.TargetCategory
        public bool playerManuallyStarted;
        public bool xpAwarded;
        public int command;        // Nebula.Hangar.EscortCommand
        public int stance;         // Nebula.Hangar.EscortStance
        public int subState;       // Nebula.Hangar.EscortSubState
        public string engagedThreatId;
        /// <summary>VS-016A.1 §2D: an EM-disrupted remote Escort can't fire -- additive, safe-zero-default field (absent on any pre-existing save round-trips as "never disrupted").</summary>
        public float disruptRemaining;
        public float disruptImmunityRemaining;
    }

    [Serializable]
    public class VehicleDto
    {
        public string vehicleId;
        public int vehicleClass;   // Nebula.Hangar.VehicleClass
        /// <summary>VS-011: 1 (default) or 2 — which VehicleCatalog def (Mk I/Mk II) this vehicle is. Absent on any pre-v9 save round-trips as the field initializer default (1 = Mk I), so an old save never needs explicit migration code.</summary>
        public int mk = 1;
        public string homeSystemId;
        public int phase;          // Nebula.Hangar.VehiclePhase
        public int missionKind;    // Nebula.Hangar.MissionKind
        public string targetId;
        public float phaseTimer;
        public bool recallRequested;
        public float px, py, pz;   // absolute position at save time
        public float rx, ry, rz, rw;
        public string heldItemId;
        public float heldAmount;

        // --- VS-015 §3/§10: real K/Z/G + Stranded (disabled-in-field, awaiting Engineer recovery) ---
        // Absent on a pre-v13 save -> shield/armor/hull default to 0, which the loader falls back to
        // the vehicle's own full-health default for (never a real save/load HP loss for old vehicles).
        public float shield, armor, hull;
        public bool stranded;
        public EngineerMissionDto engineerTask;
        public EscortStateDto escortState;
        /// <summary>VS-015.1 §4: a Collector Mk II's real physical Maintenance Resupply run — active=false when not running one.</summary>
        public MaintenanceResupplyDto resupplyTask;
    }

    /// <summary>VS-015.1 §4: one Collector Mk II Maintenance Resupply mission.</summary>
    [Serializable]
    public class MaintenanceResupplyDto
    {
        public bool active;
        public string missionId;
        public string systemId;
        public string targetCommandStructureId;
        public string itemId;
        public float plannedAmount;
        public int phase; // Nebula.Hangar.MaintenanceResupplyPhase
    }

    // --- VS-010 (schema v8) ----------------------------------------------------

    /// <summary>The ship's single in-flight Quick Asset Package transaction (VS-010 §2/§7) — null (active=false) when none is running.</summary>
    [Serializable]
    public class QuickPackageDto
    {
        public bool active;
        public string transactionId;
        public string planetWorldId;
        public string commandMaterialId;
        public string laserMaterialId;
        public bool drillSent, commandSent, laserSent;
        public float elapsedSeconds;
    }

    /// <summary>The ship's single in-flight planet-evacuation transaction (VS-010 §5/§7) — null (active=false) when none is running.</summary>
    [Serializable]
    public class EvacuationDto
    {
        public bool active;
        public string planetWorldId;
        public List<string> pendingVehicleIds = new List<string>();
    }

    // --- VS-011 (schema v9) ----------------------------------------------------

    /// <summary>One System Logistics Center's persisted state — lives inside its system's SystemDelta, never as a separate top-level authority (matches drills/structures/vehicles).</summary>
    [Serializable]
    public class LogisticsCenterDto
    {
        public string systemId;
        public int tier;              // Logistics.LogisticsCenterTier (1 or 2)
        public int state;             // Logistics.LogisticsCenterState
        public float buildRemaining;
        public bool upgrading;
        public float upgradeRemaining;
        public float shield, armor, hull, regenTimer;
        public List<CargoItemDto> inventory = new List<CargoItemDto>();
        public int refundParts, refundMetalAlloy, refundBiocomposite;
        public int upgradeRefundParts, upgradeRefundMetalAlloy, upgradeRefundBiocomposite;
    }

    /// <summary>One persistent Collector route's exact state — lives inside its system's SystemDelta.</summary>
    [Serializable]
    public class CollectorRouteDto
    {
        public string routeId;
        public string systemId;
        public string drillWorldId;
        public string vehicleId;
        public int priority;          // Logistics.RoutePriority
        public int phase;             // Logistics.RoutePhase
        public int resumePhase;       // Logistics.RoutePhase to resume into after Paused
        public int waitReason;        // Logistics.RouteWaitReason
        // Held cargo/position are NOT duplicated here — VehicleDto (keyed by
        // vehicleId, already captured alongside every other vehicle) is the
        // single authority for a route-controlled vehicle's physical state;
        // this DTO only ever carries route-LOGICAL bookkeeping.
        public float remainingDistance;
        public string activeTransferTransactionId;
        public string lastTransferTransactionId;
        public float dockWaitStartedAt;
    }

    // --- VS-012 (schema v10) ----------------------------------------------------

    [Serializable]
    public class PoiSalvageNodeDto { public string itemId; public float total; public float remaining; }

    /// <summary>One POI's mutable state — lives inside its system's SystemDelta (never top-level), matching the drills/structures/logisticsCenters/collectorRoutes precedent. The immutable manifest (type/risk/position/size/seeds) is never stored here — see Poi.PoiManifestGenerator.</summary>
    [Serializable]
    public class PoiDto
    {
        public string poiId;
        public int level;             // Poi.PoiKnowledgeLevel
        public float locateElapsed, identifyElapsed, operationElapsed;
        public int pauseReason;       // Poi.PoiPauseReason
        public string assignedVehicleId;
        public bool anomalyXpAwarded;
        public bool wreckXpAwarded;
        public List<PoiSalvageNodeDto> salvageNodes = new List<PoiSalvageNodeDto>();
        public int facilityChoice;    // Poi.FacilityChoice
        public float facilityOutputBasicRemaining, facilityOutputSpecialRemaining;
        public string facilityOutputSpecialItemId;
        public bool facilityXpAwarded;
        public bool facilitySignalApplied;
    }

    // --- VS-013 (schema v11) ----------------------------------------------------

    /// <summary>One hardpoint's saved state — lives inside ShipCombatDto (ship-owned, never per-system).</summary>
    [Serializable]
    public class HardpointDto
    {
        public int index;
        public bool equipped;
        public int weaponFamily;   // Nebula.Combat.WeaponFamily -- meaningless when !equipped
        public int tier;
        public float heat;
        public bool overheated;
        public float overheatCooldownRemaining;
    }

    [Serializable]
    public class WeaponProductionJobDto
    {
        public bool active;
        public int weaponFamily;
        public int targetTier;
        public int reservedParts;
        public string materialId;
        public int reservedMaterial;
        public float totalSeconds, remainingSeconds;
    }

    /// <summary>Ship-owned combat state (VS-013 §10) -- never per-system, exactly like cargo/module jobs.</summary>
    [Serializable]
    public class ShipCombatDto
    {
        public float shield, armor, hull, regenTimer;
        public float energy;
        public bool weaponsLocked;
        public float boostRemaining, boostCooldownRemaining;
        public bool damaged;
        public List<HardpointDto> hardpoints = new List<HardpointDto>();
        public WeaponProductionJobDto productionJob; // null (active=false) when no job is running
        public string targetEnemyId;
        public string emLockTargetId;
        public float emLockElapsed;

        // --- VS-013.1 §6/§10: physical repair transaction + Damaged-gate state ---
        // Purely additive on the existing schema 11 (no schema/generation bump):
        // an old vs-013-auto-pass-shaped save never wrote these keys, so JsonUtility
        // leaves them at their C# defaults (false/0/0f) on load -- which already
        // means exactly "no repair job was ever in flight", the correct safe
        // default. No nullness check is used anywhere for these fields.
        public bool repairActive, repairPaused;
        public int repairHullForm;
        public float repairPackageProgress01;
        public float sinceLastDamageSeconds = 999f;
    }

    [Serializable]
    public class TrainingRunDto
    {
        public int currentStep;
        public List<bool> stepCompleted = new List<bool>();
    }

    [Serializable]
    public class TrainingProgressDto
    {
        public TrainingRunDto a = new TrainingRunDto();
        public TrainingRunDto b = new TrainingRunDto();
        public TrainingRunDto c = new TrainingRunDto();
    }

    /// <summary>The campaign's single, exactly-once Scout Probe introduction (VS-013 §7).</summary>
    [Serializable]
    public class FirstProbeDto
    {
        public int status; // Nebula.Combat.FirstProbeStatus
        public float warningRemainingSeconds;
        public bool threatKnown;
        public string pendingReconEffect;
    }

    [Serializable]
    public class EmergencyShutdownDto
    {
        public bool active;
        public float remainingSeconds;
    }

    [Serializable]
    public class RecoverySoftLockDto
    {
        public float totalActiveMinutesElapsed;
        public List<float> recentDefeatTimestamps = new List<float>();
        public string lastVisitedControlledSystemId;
    }

    [Serializable]
    public class CombatWreckageNodeDto { public string itemId; public float total; public float remaining; }

    /// <summary>One physical combat-loss/Probe-salvage wreckage — lives inside its system's SystemDelta (VS-013 §8), matching every other physical world object's precedent.</summary>
    [Serializable]
    public class CombatWreckageDto
    {
        public string wreckageId;
        public float remainingActiveSeconds;
        public List<CombatWreckageNodeDto> nodes = new List<CombatWreckageNodeDto>();
    }

    /// <summary>One materialized-system-lifetime enemy — lives inside its system's SystemDelta, never captured for a system that isn't currently Full (encounters only progress in Full, VS-013 §5).</summary>
    [Serializable]
    public class EnemyDto
    {
        public string enemyId;
        public string defId;       // e.g. "enemy.scout_probe.mk1"
        public float px, py, pz;   // absolute position at save time
        public float vx, vy, vz;
        public float rx, ry, rz, rw;
        public int aiPhase;        // Nebula.Combat.EnemyAiPhase
        public string targetId;
        public float scanElapsed;
        public float continuousEscapeSeconds;
        public float shield, armor, hull, regenTimer;

        // --- VS-014 §5/§7/§10/§17: Looter/Disruptor/Heavy/Siege additive fields --
        // Absent on a pre-v12 save -> every field defaults to its C# zero/empty
        // value, which is already the correct "not applicable to this role" state
        // (mirrors EnemyDef's own "inapplicable stays zero" convention).
        public List<CargoItemDto> looterCargo = new List<CargoItemDto>();
        public string structureTargetId;         // Heavy/Siege's priority structure/logistics-center target, if any
        public float disruptRemaining;
        public float disruptImmunityRemaining;
        public float telegraphRemaining;
    }

    [Serializable]
    public class ProjectileDto
    {
        public string projectileId;
        public int kind;           // Nebula.Combat.ProjectileKind
        public int tier;
        public string ownerId;
        public float px, py, pz;
        public float vx, vy, vz;
        public float remainingLifetimeSeconds;
        public string lockedTargetId;

        // --- VS-014 §15/§17: hostile/interceptable projectile fields ------------
        // Absent on a pre-v12 save -> faction defaults to Player(0), interceptable
        // false, damageAmount 0 -- an old ship-only Plasma/EM projectile round-trips
        // exactly as before with zero migration code.
        public int faction;                 // Nebula.Combat.ProjectileFaction
        public bool interceptable;
        public float interceptionDurabilityRemaining;
        public float damageAmount;
    }

    // --- VS-014 (schema v12) ----------------------------------------------------

    [Serializable]
    public class RaidHistoryEntryDto
    {
        public int stage;          // Nebula.Combat.RaidStage
        public int doctrine;       // Nebula.Combat.RaidDoctrine
        public string leaderDefId;
        public string planetWorldId;
        public List<string> compositionDefIds = new List<string>();
    }

    /// <summary>Campaign-lifetime raid registry (VS-014 §8/§17) — ship/campaign-owned, never per-system, mirrors ShipCombatDto/TrainingProgressDto's own top-level placement.</summary>
    [Serializable]
    public class RaidCampaignDto
    {
        public int difficulty;     // Nebula.Combat.RaidDifficulty
        public string activeMajorRaidSystemId;
        public List<string> taughtEnemyDefIds = new List<string>();
        public List<RaidHistoryEntryDto> history = new List<RaidHistoryEntryDto>();
        public bool pirateBaseSpawned;
        public string pirateBasePoiId;
        public string pirateBaseSystemId;
        public bool pirateBaseResolved;
        public bool pirateBaseRewardAwarded;
    }

    /// <summary>One system's pressure/grace/raid-flow state (VS-014 §8/§17) — lives inside its SystemDelta, exactly like enemies/projectiles/combatWreckage above; absent/default on a pre-v12 save means "no pressure ever accumulated here", the correct safe start state.</summary>
    [Serializable]
    public class ThreatPressureDto
    {
        public float pressure;
        public float graceRemainingActiveMinutes;
        public bool foreignPresenceActive;
        public float foreignPresenceActiveMinutesElapsed;
        public bool firstControlledGraceGranted;

        public int phase;          // Nebula.Combat.RaidPhase
        public int stage = -1;     // -1 = null (no stage set); Nebula.Combat.RaidStage otherwise
        public int doctrine = -1;  // -1 = null; Nebula.Combat.RaidDoctrine otherwise
        public string targetPlanetWorldId;
        public List<string> compositionDefIds = new List<string>();
        public List<string> spawnedEnemyIds = new List<string>();
        public string encounterSeed = "0"; // ulong as string -- JsonUtility has no native ulong support
        public float warningRemainingSeconds;
        public float elapsedActiveSeconds;

        public bool playerDealtMeaningfulDamage;
        public bool playerIssuedValidFocusCommand;
        public bool playerActiveDefenseParticipation;
        public bool playerWasShutdownDuringRaid;
        public bool anyResourceStolen;
        public bool resultApplied;
        public bool salvageApplied;
        public bool pendingProbeEffectConsumedThisRaid;
        public bool wasTargetContestedDuringRaid;
    }

    /// <summary>One planet's defense-mode/focus coordination state (VS-014 §4/§17) — lives inside its SystemDelta, keyed by worldId since a system can have multiple planets each with their own Command.</summary>
    [Serializable]
    public class DefenseCoordinationDto
    {
        public string worldId;
        public int mode;           // Nebula.Combat.DefenseMode
        public string focusEnemyId;
    }

    [Serializable]
    public class SavePayload
    {
        public int seed;
        public int kit;                    // ExtractionState.KitState
        public ShipDto ship = new ShipDto();
        public List<string> scanned = new List<string>();
        public List<DrillDto> drills = new List<DrillDto>();
        public List<StructureDto> structures = new List<StructureDto>();
        public List<DominanceDto> dominance = new List<DominanceDto>();
        public List<CargoItemDto> cargo = new List<CargoItemDto>();
        public List<PlanetSnapDto> planetSnapshots = new List<PlanetSnapDto>();

        // VS-004 progression (schema v2; absent in v1 saves → defaults migrate to L1/0/empty).
        public long totalXp;
        public int developmentPoints;
        public List<string> ledger = new List<string>();

        // VS-005 (schema v3) / VS-017A.2 (schema v15). Both of these are now
        // LEGACY READ-ONLY: "processor" is the v2 single-slot field and
        // "processorSlots" the v3-v14 list, kept ONLY so JsonUtility can still
        // deserialize an old file for ExtractionDirector.MigrateLegacyProcessing to
        // fold into the v15 bench (processingBench below). A v15 save writes neither,
        // so both round-trip as their empty defaults and the timed bay has no way
        // back into a live campaign.
        public ProcessorDto processor;
        public List<ProcessorDto> processorSlots = new List<ProcessorDto>();

        /// <summary>VS-017A.2 (schema v15): the single processing bench. Empty on any pre-v15 payload, which is exactly what the migration turns the old slots into.</summary>
        public ProcessingBenchDto processingBench;

        public List<ModuleTierDto> moduleTiers = new List<ModuleTierDto>();
        public ModuleJobDto activeModuleJob; // null when no upgrade is in progress

        // VS-006 (schema v4). Empty systemKnowledge on any v1-v3 payload (JsonUtility
        // always default-constructs this object even when absent from the JSON, so
        // ExtractionDirector.LoadNow checks emptiness, not nullness, to run
        // NavigationDirector.MigrateLegacySave instead of RestoreFromSave).
        public NavigationDto navigation;

        // VS-007 (schema v5). Unlike navigation's "always has the start
        // system" trick, an empty vehicles list is a perfectly legitimate
        // real v5 state (the player may never build one) — so migration
        // can't use emptiness as the signal here. ExtractionDirector.LoadNow
        // instead branches on the envelope's actual schemaVersion (see
        // SaveService.Load's out-parameter overload).
        public HangarJobDto hangarJob;         // null when no production is in progress
        public List<VehicleDto> vehicles = new List<VehicleDto>();
        public int hangarCommandUsed;
        public int hangarNextVehicleSequence;

        // VS-008 (schema v6+). Every real system is pure procedural generation,
        // reproducible from (seed, GenerationVersion, sector coordinate) alone
        // — never persisted here. Only the mutable delta on top of it is: one
        // SystemDelta per system that still carries real state (an unvisited
        // system has none and is simply absent from this list — "yalnız
        // mutable delta taşıyan manifestleri kalıcılaştır"). A save older
        // than v6 is rejected outright on load (no v1-v5 migration path).
        // VS-009 (schema v7, generation v2): typed StarClass/EnvironmentClass
        // and the canonical 12-node scheme. A v6 payload's DrillDto/StructureDto
        // nodeId values are rewritten losslessly on load (NodeIdMigration) —
        // nothing here needs a new field for that, it's a pure string remap.
        public List<Sectors.SystemDelta> systemDeltas = new List<Sectors.SystemDelta>();

        // VS-010 (schema v8, generation v2 unchanged). Ownership/attention/link
        // tags are never persisted here — always re-derived from real state.
        // Abandoned planets live inside each SystemDelta (systemDeltas above),
        // not as a separate top-level list.
        public QuickPackageDto quickPackage;   // null (active=false) when no package is in flight
        public EvacuationDto evacuation;       // null (active=false) when no evacuation is in flight

        // VS-011 (schema v9). Like drills/structures/vehicles, the Logistics
        // Center and Collector routes live inside each system's SystemDelta
        // (Sectors.SystemDelta.logisticsCenters/collectorRoutes below), never
        // as a separate top-level list here — a v8 payload simply has no such
        // entries in any delta, which round-trips as an empty list with zero
        // migration code (the same "empty is a legitimate current state"
        // reasoning already used for the vehicles list, VS-007).

        // VS-013 (schema v11). Ship/campaign-owned combat state, never
        // per-system (mirrors cargo/module jobs). Enemies/projectiles/combat
        // wreckage are physical world objects and live inside each system's
        // SystemDelta instead (Sectors.SystemDelta.enemies/projectiles/combatWreckage
        // below) — a v10 payload simply has none of these fields/entries,
        // which round-trips as empty/null with zero migration code.
        public ShipCombatDto shipCombat;               // null only on a pre-v11 payload
        public TrainingProgressDto training;
        public FirstProbeDto firstProbe;
        public EmergencyShutdownDto emergencyShutdown;
        public RecoverySoftLockDto recoverySoftLock;

        // VS-014 (schema v12). Campaign-lifetime raid registry, ship/campaign-owned
        // like everything else in this block — null on any pre-v12 payload, which
        // ExtractionDirector.LoadNow treats as "campaign never saw a raid": fresh
        // RaidCampaignState defaults (Commander difficulty, no active raid, nothing
        // taught, empty history, Pirate Base unspawned).
        public RaidCampaignDto raidCampaign;
    }

    [Serializable]
    public class SaveEnvelope
    {
        // Zero means "schema absent/unknown". SaveService.Save always stamps
        // CurrentSchema explicitly; a hand-authored or truncated envelope must
        // never masquerade as a valid historical v13 save.
        public int schemaVersion;
        public int contentVersion = 1;
        public int generationVersion = 2;
        public string campaignId;
        public int revision;
        public string payloadHash;
        public string savedAtUtc;          // metadata only — never advances timers
        public string payloadJson;
    }

    /// <summary>
    /// Local JSON save service: temp-write → read-back → hash/JSON validate →
    /// rotate three backups → safe replace. Corrupt primary falls back to a
    /// backup; a newer schema is rejected. All Unity-object reads must happen
    /// on the main thread before calling here — this class only touches DTOs
    /// and files. VS-001 uses slot 1; the API is slot-parameterized so three
    /// slots drop in later without a rewrite.
    /// </summary>
    public static class SaveService
    {
        // VS-017A.2 raised this from 14 to 15: the timed processing bay became a
        // single instant bench, which needs new additive fields (bench input id and
        // amount, legacy claim queue) and a real migration of every v14 slot state.
        // SectorManifestGenerator.GenerationVersion stays 3 and
        // ContentFingerprint.ContentRevision stays 3 — nothing about world generation
        // or content identity moved, only ship-owned processing state.
        public const int CurrentSchema = 15;

        /// <summary>Lowest slot number a player can save into.</summary>
        public const int FirstSlot = 1;

        /// <summary>
        /// How many save slots exist. Owned here rather than by the save screen:
        /// the screen offered three slots while the director wrote a hard-coded
        /// one, so the range the player saw and the range the save system honoured
        /// were two different facts with nothing holding them together.
        /// </summary>
        public const int SlotCount = 3;

        /// <summary>The valid slot range, applied wherever a slot arrives from outside.</summary>
        public static int ClampSlot(int slot) =>
            slot < FirstSlot ? FirstSlot
            : slot > FirstSlot + SlotCount - 1 ? FirstSlot + SlotCount - 1
            : slot;

        /// <summary>
        /// Directory override for tests, so a run never writes over the operator's real
        /// saves. Modelled on <c>NebulaInputBindingStore.DirectoryOverride</c>, which is
        /// the same seam for the same reason — a test that has to point somewhere else is
        /// otherwise a test that quietly edits the machine it runs on.
        /// <para>
        /// Test-only intent, not a feature: the game never sets it, so a player's saves
        /// always come from <c>Application.persistentDataPath</c>. It is also what lets
        /// <see cref="Platform.NebulaLegacyDataMigrator"/> be exercised end to end against
        /// temp folders, including through the main menu's Continue query, which reads
        /// slots through <see cref="SlotFile"/>.
        /// </para>
        /// </summary>
        public static string DirectoryOverride { get; set; }

        /// <summary>
        /// Three sources, in the order of who is entitled to override whom.
        /// <para>
        /// A test's explicit <see cref="DirectoryOverride"/> comes first because it is
        /// the most local statement of intent. <c>NebulaSmokeDataRoot</c> comes next and
        /// is <c>null</c> in every shipped launch — it only answers for an automated
        /// smoke run that passed <c>-nebulaSmoke</c> with a stamped sandbox, which is
        /// what stops such a run from writing over the player's real campaign. The
        /// engine's own folder is last, and <c>??</c> short-circuits before reaching it,
        /// which matters: reading <c>Application.persistentDataPath</c> creates that
        /// folder, and an isolated run has no business creating it.
        /// </para>
        /// </summary>
        private static string Dir =>
            DirectoryOverride
            ?? Platform.NebulaSmokeDataRoot.Folder("saves")
            ?? Platform.NebulaEditorScratch.Folder("saves")
            ?? Path.Combine(Application.persistentDataPath, "nebula", "saves");

        private static string MainPath(int slot) => Path.Combine(Dir, $"slot{slot}.json");
        private static string BackupPath(int slot, int n) => Path.Combine(Dir, $"slot{slot}.bak{n}.json");
        private static string TempPath(int slot) => Path.Combine(Dir, $"slot{slot}.tmp.json");

        public static string SlotFile(int slot) => MainPath(slot);

        private static string Hash(string payloadJson) => ExtractionCatalog.Hash(payloadJson).ToString();

        public static bool Save(int slot, SavePayload payload, int revision)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string payloadJson = JsonUtility.ToJson(payload);
                var envelope = new SaveEnvelope
                {
                    schemaVersion = CurrentSchema,
                    generationVersion = Sectors.SectorManifestGenerator.GenerationVersion,
                    // VS-017A.2 §A2: the real, persistent campaign identity owned by
                    // CampaignIdentity -- never the old hardcoded "campaign-1" placeholder.
                    campaignId = CampaignIdentity.ForSave(),
                    revision = revision,
                    payloadHash = Hash(payloadJson),
                    savedAtUtc = DateTime.UtcNow.ToString("o"),
                    payloadJson = payloadJson
                };
                string envelopeJson = JsonUtility.ToJson(envelope, true);

                string temp = TempPath(slot);
                File.WriteAllText(temp, envelopeJson);

                // Read back and validate before committing.
                if (!TryRead(temp, out _, out _)) { File.Delete(temp); return false; }

                RotateBackups(slot);
                if (File.Exists(MainPath(slot))) File.Copy(MainPath(slot), BackupPath(slot, 1), true);
                if (File.Exists(temp)) File.Copy(temp, MainPath(slot), true);
                File.Delete(temp);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nebula] Save failed: " + e.Message);
                return false;
            }
        }

        private static void RotateBackups(int slot)
        {
            try
            {
                if (File.Exists(BackupPath(slot, 2))) File.Copy(BackupPath(slot, 2), BackupPath(slot, 3), true);
                if (File.Exists(BackupPath(slot, 1))) File.Copy(BackupPath(slot, 1), BackupPath(slot, 2), true);
            }
            catch { /* backups are best-effort */ }
        }

        /// <summary>Loads the best valid file for the slot (main, then backups), or null.</summary>
        public static SavePayload Load(int slot) => Load(slot, out _);

        /// <summary>Same as <see cref="Load(int)"/> but also returns the envelope's actual saved schema version — the reliable signal for migrations where an empty/default DTO is a legitimate current-schema state (e.g. VS-007's vehicles list).</summary>
        public static SavePayload Load(int slot, out int loadedSchemaVersion) => Load(slot, out loadedSchemaVersion, out _);

        /// <summary>VS-017A.2 §A2: also returns the envelope's own persistent campaign identity, so a load can adopt it verbatim instead of being mistaken for a new campaign. Empty for a legacy save that never carried a real one — <see cref="CampaignIdentity.AdoptFromSave"/> upgrades that case exactly once.</summary>
        public static SavePayload Load(int slot, out int loadedSchemaVersion, out string loadedCampaignId)
        {
            loadedSchemaVersion = 0; loadedCampaignId = null;
            string[] candidates = { MainPath(slot), BackupPath(slot, 1), BackupPath(slot, 2), BackupPath(slot, 3) };
            foreach (string path in candidates)
            {
                if (TryRead(path, out SavePayload payload, out int schemaVersion, out string campaignId))
                { loadedSchemaVersion = schemaVersion; loadedCampaignId = campaignId; return payload; }
            }
            return null;
        }

        private static bool TryRead(string path, out SavePayload payload, out int schemaVersion) =>
            TryRead(path, out payload, out schemaVersion, out _);

        /// <summary>
        /// True when a file on disk would actually load: envelope shape, payload hash and
        /// the refusal of a newer schema, all decided by the same reader
        /// <see cref="Load(int)"/> uses.
        /// <para>
        /// Exposed for <see cref="Platform.NebulaLegacyDataMigrator"/>, which has to
        /// decide whether a save it found in an old folder is worth installing. "Parses as
        /// JSON" would not answer that — a truncated file whose payload no longer matches
        /// its <c>payloadHash</c> parses perfectly and then fails at the moment the player
        /// presses Continue. Reusing the real validator is the difference between the
        /// migration knowing that and finding out later.
        /// </para>
        /// </summary>
        public static bool IsReadableSaveFile(string path) => TryRead(path, out _, out _, out _);

        private static bool TryRead(string path, out SavePayload payload, out int schemaVersion, out string campaignId)
        {
            payload = null; schemaVersion = 0; campaignId = null;
            try
            {
                if (!File.Exists(path)) return false;
                var envelope = JsonUtility.FromJson<SaveEnvelope>(File.ReadAllText(path));
                if (envelope == null || string.IsNullOrEmpty(envelope.payloadJson)) return false;
                if (envelope.schemaVersion > CurrentSchema) return false;          // reject newer schema
                if (Hash(envelope.payloadJson) != envelope.payloadHash) return false; // hash mismatch
                payload = JsonUtility.FromJson<SavePayload>(envelope.payloadJson);
                schemaVersion = envelope.schemaVersion;
                campaignId = envelope.campaignId;
                return payload != null;
            }
            catch { return false; }
        }
    }
}
