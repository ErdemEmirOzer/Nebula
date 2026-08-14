using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nebula.Content
{
    /// <summary>
    /// TF-003: one resource line item (item id + amount) — the shared shape
    /// every cost/reward table in the game already uses ad hoc as
    /// <c>(string itemId, float amount)</c> tuples/lists (RepairCatalog,
    /// LogisticsCenterTierDef.Cost(), FacilityService.RelayCost(), ...).
    /// Immutable by construction (readonly auto-properties, no setters).
    /// </summary>
    public sealed class ContentResourceCost
    {
        public string ItemId { get; }
        public float Amount { get; }
        public ContentResourceCost(string itemId, float amount) { ItemId = itemId; Amount = amount; }
    }

    /// <summary>A physically stored/transferred item — the CargoItemVolumes universe (raw resources, part.basic, processed materials, nuclear energy/core).</summary>
    public sealed class ContentItem
    {
        public string Id { get; }
        public string DisplayName { get; }
        public float VolumePerUnit { get; }
        public ContentItem(string id, string displayName, float volumePerUnit)
        {
            Id = id; DisplayName = displayName; VolumePerUnit = volumePerUnit;
        }
    }

    /// <summary>One processing recipe (ProcessingCatalog.ProcessingRecipe, deep-copied).</summary>
    public sealed class ContentRecipe
    {
        public string Id { get; }
        public string InputItemId { get; }
        public int InputCount { get; }
        public string OutputItemId { get; }
        public int OutputCount { get; }
        public float Seconds { get; }
        public string DisplayName { get; }
        public ContentRecipe(string id, string inputItemId, int inputCount, string outputItemId, int outputCount, float seconds, string displayName)
        {
            Id = id; InputItemId = inputItemId; InputCount = inputCount;
            OutputItemId = outputItemId; OutputCount = outputCount; Seconds = seconds; DisplayName = displayName;
        }
    }

    /// <summary>Cost + duration to reach one target module tier (deep-copied from Nebula.Modules.ModuleTierCost — that source struct has public MUTABLE fields, so a defensive copy is required, not just a reference hand-off).</summary>
    public sealed class ContentModuleTierCost
    {
        public int Tier { get; }
        public int DevelopmentPoints { get; }
        public float Seconds { get; }
        public IReadOnlyList<ContentResourceCost> Resources { get; }
        public ContentModuleTierCost(int tier, int developmentPoints, float seconds, IReadOnlyList<ContentResourceCost> resources)
        {
            Tier = tier; DevelopmentPoints = developmentPoints; Seconds = seconds;
            Resources = new ReadOnlyCollection<ContentResourceCost>(new List<ContentResourceCost>(resources));
        }
    }

    /// <summary>One of the 12 ship modules (Nebula.Modules.ModuleDef, deep-copied).</summary>
    public sealed class ContentModule
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int UnlockLevel { get; }
        public int MaxTierThisSlice { get; }
        public bool FreeStarting { get; }
        public IReadOnlyList<ContentModuleTierCost> TierCosts { get; }
        public ContentModule(string id, string displayName, int unlockLevel, int maxTierThisSlice, bool freeStarting, IReadOnlyList<ContentModuleTierCost> tierCosts)
        {
            Id = id; DisplayName = displayName; UnlockLevel = unlockLevel;
            MaxTierThisSlice = maxTierThisSlice; FreeStarting = freeStarting;
            TierCosts = new ReadOnlyCollection<ContentModuleTierCost>(new List<ContentModuleTierCost>(tierCosts));
        }
    }

    /// <summary>One buildable vehicle (Nebula.Hangar.VehicleDef, deep-copied). BasicPartCost always references the real "part.basic" item; SpecialMaterialCost has no single fixed item id (the player picks any one of the 5 processed materials at build time) so it is validated only as a non-negative amount, never cross-referenced to one id.</summary>
    public sealed class ContentVehicle
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int UnlockLevel { get; }
        public int RequiredHangarTier { get; }
        public int Mk { get; }
        public float BuildSeconds { get; }
        public float Speed { get; }
        public float Shield { get; }
        public float Armor { get; }
        public float Hull { get; }
        public float ScanRange { get; }
        public float ScanSeconds { get; }
        public float CargoCapacity { get; }
        public float TransferVolumePerSecond { get; }
        public int BasicPartCost { get; }
        public int SpecialMaterialCost { get; }
        /// <summary>Empty for a direct-build-only vehicle; the fixed in-place Mk-upgrade cost (all real item ids) for one that has one (Collector/Engineer/Escort Mk I -> Mk II).</summary>
        public IReadOnlyList<ContentResourceCost> UpgradeCost { get; }

        // --- VS-015 §1/§2: Engineer/Escort additive fields -----------------------
        public int CommandCost { get; }
        public float RepairRatePerSecond { get; }
        public float DisabledVehicleRecoverSeconds { get; }
        public float FixedLaserDps { get; }
        public float FixedLaserRange { get; }
        /// <summary>Empty for a chosen-material direct build (Scout/Collector/Mk I Engineer/Escort); the fixed multi-item DIRECT-build cost for a Mk II vehicle (no player choice).</summary>
        public IReadOnlyList<ContentResourceCost> FixedDirectBuildCost { get; }

        public ContentVehicle(string id, string displayName, int unlockLevel, int requiredHangarTier, int mk,
            float buildSeconds, float speed, float shield, float armor, float hull,
            float scanRange, float scanSeconds, float cargoCapacity, float transferVolumePerSecond,
            int basicPartCost, int specialMaterialCost, IReadOnlyList<ContentResourceCost> upgradeCost,
            int commandCost = 1, float repairRatePerSecond = 0f, float disabledVehicleRecoverSeconds = 0f,
            float fixedLaserDps = 0f, float fixedLaserRange = 0f, IReadOnlyList<ContentResourceCost> fixedDirectBuildCost = null)
        {
            Id = id; DisplayName = displayName; UnlockLevel = unlockLevel; RequiredHangarTier = requiredHangarTier; Mk = mk;
            BuildSeconds = buildSeconds; Speed = speed; Shield = shield; Armor = armor; Hull = hull;
            ScanRange = scanRange; ScanSeconds = scanSeconds; CargoCapacity = cargoCapacity; TransferVolumePerSecond = transferVolumePerSecond;
            BasicPartCost = basicPartCost; SpecialMaterialCost = specialMaterialCost;
            UpgradeCost = new ReadOnlyCollection<ContentResourceCost>(new List<ContentResourceCost>(upgradeCost ?? new ContentResourceCost[0]));
            CommandCost = commandCost; RepairRatePerSecond = repairRatePerSecond; DisabledVehicleRecoverSeconds = disabledVehicleRecoverSeconds;
            FixedLaserDps = fixedLaserDps; FixedLaserRange = fixedLaserRange;
            FixedDirectBuildCost = new ReadOnlyCollection<ContentResourceCost>(new List<ContentResourceCost>(fixedDirectBuildCost ?? new ContentResourceCost[0]));
        }
    }

    /// <summary>A buildable surface structure (Nebula.Extraction.SurfaceStructureDef, deep-copied). MaterialCost (like Vehicle's SpecialMaterialCost) never has one fixed item id — either "any one material family" (Laser) or "the planet's own family material" (Command), both resolved dynamically per-instance, never a static id to cross-reference.</summary>
    public sealed class ContentStructure
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int PartsCost { get; }
        public int MaterialCost { get; }
        public bool AnyMaterial { get; }
        public float BuildSeconds { get; }
        public float ShieldMax { get; }
        public float ArmorMax { get; }
        public float HullMax { get; }

        // --- VS-014 §1: Command T2 / Laser T2 / Plasma T1-T2 tier fields --------
        // Inapplicable-to-T1 fields stay at their zero default, the same
        // convention ContentEnemy/EnemyDef already use — the two original T1
        // entries (Command/Laser) never pass any of these and stay on the old
        // single-optional-material shape untouched.
        public int Tier { get; }
        public int RequiredCommandTier { get; }
        public int RequiredWeaponCoordinationTier { get; }
        public IReadOnlyList<ContentResourceCost> MaterialCosts { get; }
        public float Dps { get; }
        public float Range { get; }
        public float TurnDegreesPerSecond { get; }
        public float ContinuousFireSecondsBeforeOverheat { get; }
        public float OverheatCooldownSeconds { get; }
        public float SplashRadius { get; }
        public float SplashMaxFractionOfDirect { get; }

        // --- VS-015 §7: Sensor Array / Maintenance Platform additive fields -----
        public float WarningBonusSeconds { get; }
        public float EngineerRepairMultiplier { get; }
        public float RecoveryTimeMultiplier { get; }
        public bool RequiresLocalEngineer { get; }

        public ContentStructure(string id, string displayName, int partsCost, int materialCost, bool anyMaterial,
            float buildSeconds, float shieldMax, float armorMax, float hullMax,
            int tier = 1, int requiredCommandTier = 0, int requiredWeaponCoordinationTier = 0,
            IReadOnlyList<ContentResourceCost> materialCosts = null,
            float dps = 0f, float range = 0f, float turnDegreesPerSecond = 0f,
            float continuousFireSecondsBeforeOverheat = 0f, float overheatCooldownSeconds = 0f,
            float splashRadius = 0f, float splashMaxFractionOfDirect = 0f,
            float warningBonusSeconds = 0f, float engineerRepairMultiplier = 1f, float recoveryTimeMultiplier = 1f,
            bool requiresLocalEngineer = false)
        {
            Id = id; DisplayName = displayName; PartsCost = partsCost; MaterialCost = materialCost; AnyMaterial = anyMaterial;
            BuildSeconds = buildSeconds; ShieldMax = shieldMax; ArmorMax = armorMax; HullMax = hullMax;
            Tier = tier; RequiredCommandTier = requiredCommandTier; RequiredWeaponCoordinationTier = requiredWeaponCoordinationTier;
            MaterialCosts = new ReadOnlyCollection<ContentResourceCost>(new List<ContentResourceCost>(materialCosts ?? new ContentResourceCost[0]));
            Dps = dps; Range = range; TurnDegreesPerSecond = turnDegreesPerSecond;
            ContinuousFireSecondsBeforeOverheat = continuousFireSecondsBeforeOverheat; OverheatCooldownSeconds = overheatCooldownSeconds;
            SplashRadius = splashRadius; SplashMaxFractionOfDirect = splashMaxFractionOfDirect;
            WarningBonusSeconds = warningBonusSeconds; EngineerRepairMultiplier = engineerRepairMultiplier;
            RecoveryTimeMultiplier = recoveryTimeMultiplier; RequiresLocalEngineer = requiresLocalEngineer;
        }
    }

    /// <summary>One tier of the orbital System Logistics Center (Nebula.Logistics.LogisticsCenterTierDef, deep-copied). Costs are always the real 3 fixed item ids (part.basic + metal_alloy + biocomposite) via the source's own Cost().</summary>
    public sealed class ContentLogisticsCenterTier
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Tier { get; }
        public int RequiredLevel { get; }
        public int RequiredModuleTier { get; }
        public IReadOnlyList<ContentResourceCost> Costs { get; }
        public float BuildSeconds { get; }
        public float Capacity { get; }
        public int MaxRoutes { get; }
        public int DockCount { get; }
        public float TransferVolumePerSecond { get; }
        public float ShieldMax { get; }
        public float ArmorMax { get; }
        public float HullMax { get; }
        public ContentLogisticsCenterTier(string id, string displayName, int tier, int requiredLevel, int requiredModuleTier,
            IReadOnlyList<ContentResourceCost> costs, float buildSeconds, float capacity, int maxRoutes, int dockCount,
            float transferVolumePerSecond, float shieldMax, float armorMax, float hullMax)
        {
            Id = id; DisplayName = displayName; Tier = tier; RequiredLevel = requiredLevel; RequiredModuleTier = requiredModuleTier;
            Costs = new ReadOnlyCollection<ContentResourceCost>(new List<ContentResourceCost>(costs));
            BuildSeconds = buildSeconds; Capacity = capacity; MaxRoutes = maxRoutes; DockCount = dockCount;
            TransferVolumePerSecond = transferVolumePerSecond; ShieldMax = shieldMax; ArmorMax = armorMax; HullMax = hullMax;
        }
    }

    /// <summary>One purchasable tier of a ship weapon family (Nebula.Combat.WeaponCatalog.CostForTier, deep-copied). Only real tiers (1..MaxTierThisSlice) are ever present — WeaponCatalog's own default/unimplemented tiers are never snapshotted as fake zero-cost entries.</summary>
    public sealed class ContentWeaponTierCost
    {
        public int Tier { get; }
        public IReadOnlyList<ContentResourceCost> Resources { get; }
        public float Seconds { get; }
        public ContentWeaponTierCost(int tier, IReadOnlyList<ContentResourceCost> resources, float seconds)
        {
            Tier = tier; Resources = new ReadOnlyCollection<ContentResourceCost>(new List<ContentResourceCost>(resources)); Seconds = seconds;
        }
    }

    /// <summary>One of the three ship weapon families (Nebula.Combat.WeaponCatalog, deep-copied). Stable ids weapon.laser/weapon.plasma/weapon.em are minted here — WeaponCatalog itself only has a WeaponFamily enum, never a save-facing string id, so this mapping is purely a runtime-content-layer addition, never a save format change.</summary>
    public sealed class ContentWeapon
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int MaxTierThisSlice { get; }
        public float BaseDps { get; }
        public float BaseRange { get; }
        public IReadOnlyList<ContentWeaponTierCost> TierCosts { get; }
        public ContentWeapon(string id, string displayName, int maxTierThisSlice, float baseDps, float baseRange, IReadOnlyList<ContentWeaponTierCost> tierCosts)
        {
            Id = id; DisplayName = displayName; MaxTierThisSlice = maxTierThisSlice; BaseDps = baseDps; BaseRange = baseRange;
            TierCosts = new ReadOnlyCollection<ContentWeaponTierCost>(new List<ContentWeaponTierCost>(tierCosts));
        }
    }

    /// <summary>One AI-driven enemy type (Nebula.Combat.EnemyCatalog.EnemyDef, deep-copied). Never purchased/produced by the player, so no item cost cross-reference applies.</summary>
    public sealed class ContentEnemy
    {
        public string Id { get; }
        public string DisplayName { get; }
        public float Speed { get; }
        public float ShieldMax { get; }
        public float ArmorMax { get; }
        public float HullMax { get; }
        public bool IsCombatCapable { get; }
        public float Dps { get; }
        public float AttackRange { get; }
        public float EngageBandMin { get; }
        public float EngageBandMax { get; }
        public float BreakDistance { get; }
        public float ApproachThreshold { get; }
        public float RetreatHullFraction { get; }
        public float ScanSeconds { get; }

        // --- VS-014 §5: Looter/Disruptor/Heavy/Siege fields ----------------------
        // Role is a plain string (never Nebula.Combat.EnemyRole) so Nebula.Content
        // stays engine/domain-independent, exactly like every other field here.
        public string Role { get; }
        public int ThreatBudget { get; }
        public float CargoCapacityVolume { get; }
        public float CargoTransferVolumePerSecond { get; }
        public float TelegraphSeconds { get; }
        public float DisruptSeconds { get; }
        public float DisruptImmunitySeconds { get; }

        public ContentEnemy(string id, string displayName, float speed, float shieldMax, float armorMax, float hullMax,
            bool isCombatCapable, float dps, float attackRange, float engageBandMin, float engageBandMax,
            float breakDistance, float approachThreshold, float retreatHullFraction, float scanSeconds,
            string role = null, int threatBudget = 0, float cargoCapacityVolume = 0f, float cargoTransferVolumePerSecond = 0f,
            float telegraphSeconds = 0f, float disruptSeconds = 0f, float disruptImmunitySeconds = 0f)
        {
            Role = role; ThreatBudget = threatBudget; CargoCapacityVolume = cargoCapacityVolume; CargoTransferVolumePerSecond = cargoTransferVolumePerSecond;
            TelegraphSeconds = telegraphSeconds; DisruptSeconds = disruptSeconds; DisruptImmunitySeconds = disruptImmunitySeconds;
            Id = id; DisplayName = displayName; Speed = speed; ShieldMax = shieldMax; ArmorMax = armorMax; HullMax = hullMax;
            IsCombatCapable = isCombatCapable; Dps = dps; AttackRange = attackRange;
            EngageBandMin = engageBandMin; EngageBandMax = engageBandMax;
            BreakDistance = breakDistance; ApproachThreshold = approachThreshold;
            RetreatHullFraction = retreatHullFraction; ScanSeconds = scanSeconds;
        }
    }

    /// <summary>One of the three implemented POI types (Nebula.Poi.PoiCatalog, deep-copied). Stable ids poi.anomaly/poi.wreck/poi.abandoned_facility are minted here — PoiCatalog itself only has a PoiType enum, never a save-facing string id. CostReferences is empty for Anomaly/Wreck (their rewards are dynamically resolved from the material-family list, never one fixed id) and carries the Abandoned Facility Sensor Relay's 3 real fixed item costs.</summary>
    public sealed class ContentPoiType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public float SafeWeight { get; }
        public float UnstableWeight { get; }
        public int XpReward { get; }
        public IReadOnlyList<ContentResourceCost> CostReferences { get; }
        public ContentPoiType(string id, string displayName, float safeWeight, float unstableWeight, int xpReward, IReadOnlyList<ContentResourceCost> costReferences)
        {
            Id = id; DisplayName = displayName; SafeWeight = safeWeight; UnstableWeight = unstableWeight; XpReward = xpReward;
            CostReferences = new ReadOnlyCollection<ContentResourceCost>(new List<ContentResourceCost>(costReferences ?? new ContentResourceCost[0]));
        }
    }
}
