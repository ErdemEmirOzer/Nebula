using System.Collections.Generic;
using Nebula.Extraction;

namespace Nebula.Modules
{
    public enum ModuleBranch { Industry, Exploration, Defense, Nuclear }

    /// <summary>One resource line item in a tier's cost (item id + amount).</summary>
    public struct ModuleResourceCost
    {
        public string ItemId;
        public float Amount;
        public ModuleResourceCost(string itemId, float amount) { ItemId = itemId; Amount = amount; }
    }

    /// <summary>Full cost + duration to reach one target tier.</summary>
    public struct ModuleTierCost
    {
        public int DevelopmentPoints;
        public ModuleResourceCost[] Resources;
        public float Seconds;
    }

    /// <summary>
    /// Static definition of one of the 12 ship modules. Stable IDs, never
    /// renamed. TierCosts[i] is the cost to go from tier i to tier i+1 (index
    /// 0 = reaching T1 .. index 4 = reaching T5). MaxTierThisSlice caps what
    /// is actually purchasable in VS-005: 5 for the two functional Industry
    /// modules, 1 for Station Construction (T1 already represents today's
    /// working build capability; T2+ isn't implemented yet), 0 for the other
    /// nine (their runtime consumers don't exist yet — see LockedMessage).
    /// </summary>
    public class ModuleDef
    {
        public const int MaxTier = 5;
        private static readonly int[] GlobalTierLevelGate = { 0, 0, 21, 41, 61, 81 }; // index = tier; [0]/[1] unused (T1 uses UnlockLevel)

        public string Id { get; }
        public ModuleBranch Branch { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int UnlockLevel { get; }
        /// <summary>Human-readable extra gate shown in the UI (e.g. "First Controlled Planet"). Informational only in this slice.</summary>
        public string MilestoneLabel { get; }
        public int MaxTierThisSlice { get; }

        /// <summary>
        /// The highest tier of this module that actually changes something in the game
        /// (P1-ECO-015).
        /// <para>
        /// Normally the same as <see cref="MaxTierThisSlice"/>. It differs for
        /// <c>industry.resource_processing</c>, whose T2-T5 are purchasable and grant
        /// nothing: the bench has no slot count and no duration left for a tier to
        /// scale, and what those tiers should buy instead is an open balance decision
        /// (<c>UIDesign/Finalization/RESOURCE-PROCESSING-BENCH-DESIGN.md</c> §1). That
        /// was written honestly in the module's own description and in
        /// <c>ShipProcessing.ApplyTier</c> — but the Progression page shows neither, so
        /// the player could spend four tiers' worth of points and materials on nothing
        /// and never be told. This is the field the page reads to say so.
        /// </para>
        /// <para>
        /// Deliberately not a cap. Capping the module would delete four purchasable
        /// tiers from shipped content, which is a balance decision this field exists to
        /// surface rather than to pre-empt. It is also not part of the content
        /// fingerprint: it describes what the runtime does with a tier, not what the
        /// tier costs or unlocks.
        /// </para>
        /// </summary>
        public int MaxEffectiveTierThisSlice { get; }

        public string LockedMessage { get; }
        public ModuleTierCost[] TierCosts { get; }
        public string RuntimeConsumerDescription { get; }
        public string VisualAnchorId { get; }

        public bool FunctionalThisSlice => MaxTierThisSlice > 0;

        public ModuleDef(string id, ModuleBranch branch, string displayName, string description, int unlockLevel,
            string milestoneLabel, int maxTierThisSlice, string lockedMessage, ModuleTierCost[] tierCosts,
            string runtimeConsumerDescription, string visualAnchorId, int maxEffectiveTierThisSlice = -1)
        {
            Id = id; Branch = branch; DisplayName = displayName; Description = description;
            UnlockLevel = unlockLevel; MilestoneLabel = milestoneLabel;
            MaxTierThisSlice = maxTierThisSlice; LockedMessage = lockedMessage;
            MaxEffectiveTierThisSlice = maxEffectiveTierThisSlice < 0 ? maxTierThisSlice : maxEffectiveTierThisSlice;
            TierCosts = tierCosts; RuntimeConsumerDescription = runtimeConsumerDescription; VisualAnchorId = visualAnchorId;
        }

        /// <summary>
        /// Character level required to purchase the given target tier (1..5).
        /// <para>
        /// Monotone by construction (P1-DATA-012). The global table starts at 21 for
        /// T2, which is <i>below</i> the T1 unlock level of three modules — Weapon
        /// Coordination unlocks at 30, Shield Systems at 25, the nuclear line at 61-81.
        /// A ladder cannot ask for less as it climbs, and the Progression page prints
        /// exactly what this returns, so it was showing "T2: level 21" beside "T1:
        /// level 30" on the same card.
        /// </para>
        /// <para>
        /// Taking the higher of the table entry and the previous rung changes nothing
        /// about what is reachable — a tier can only be bought after the one below it,
        /// so the effective requirement was already the T1 level — it makes the number
        /// the player is shown the number that actually applies. No gate is raised for
        /// any module whose own unlock level is already at or below the table.
        /// </para>
        /// </summary>
        public int RequiredLevelForTier(int targetTier)
        {
            if (targetTier <= 1) return UnlockLevel;
            if (targetTier > MaxTier) targetTier = MaxTier;
            int below = RequiredLevelForTier(targetTier - 1);
            int here = GlobalTierLevelGate[targetTier];
            return here > below ? here : below;
        }
    }

    /// <summary>
    /// The 12-module catalog. Pure data; no UnityEngine.Random, no scene
    /// dependency. <see cref="Validate"/> is exercised by tests and (in
    /// editor) at bootstrap.
    /// </summary>
    public static class ModuleCatalog
    {
        public const string LockedMessageNotImplemented = "BAĞLI SİSTEM HENÜZ UYGULANMADI";
        public const string LockedMessageStationCap = "T3+ BAĞLI SİSTEM HENÜZ UYGULANMADI";
        public const string LockedMessageExplorationCap = "T3+ BAĞLI SİSTEM HENÜZ UYGULANMADI";

        // Item ids introduced by VS-005's standard resource-cost table.
        public const string EnergyNuclearId = "energy.nuclear";
        public const string CoreNuclearId = "core.nuclear";

        // Point cost per target tier (T1..T5): 1,1,1,2,3 — identical for every
        // module (three free starting T1s subtracted elsewhere give the 93 total).
        // Must be declared before Modules: static field initializers run in
        // textual declaration order, and Build() (called by Modules' own
        // initializer) reads these through StandardTiers().
        private static readonly int[] PointCostByTier = { 1, 1, 1, 2, 3 };
        private static readonly float[] SecondsByTier = { 30f, 90f, 180f, 360f, 720f };

        public static readonly ModuleDef[] Modules = Build();

        private static readonly Dictionary<string, int> indexById = BuildIndex();

        public static ModuleDef Get(string id) => indexById.TryGetValue(id ?? "", out int i) ? Modules[i] : null;
        public static int IndexOf(string id) => indexById.TryGetValue(id ?? "", out int i) ? i : -1;

        private static Dictionary<string, int> BuildIndex()
        {
            var map = new Dictionary<string, int>();
            for (int i = 0; i < Modules.Length; i++) map[Modules[i].Id] = i;
            return map;
        }

        /// <summary>Standard VS-005 resource table for one module's primary/secondary materials.</summary>
        private static ModuleTierCost[] StandardTiers(string primaryMaterialId, string secondaryMaterialId)
        {
            return new[]
            {
                new ModuleTierCost { DevelopmentPoints = PointCostByTier[0], Seconds = SecondsByTier[0],
                    Resources = new[] { new ModuleResourceCost(ProcessingCatalog.BasicPartId, 20) } },
                new ModuleTierCost { DevelopmentPoints = PointCostByTier[1], Seconds = SecondsByTier[1],
                    Resources = new[]
                    {
                        new ModuleResourceCost(ProcessingCatalog.BasicPartId, 40),
                        new ModuleResourceCost(primaryMaterialId, 10)
                    } },
                new ModuleTierCost { DevelopmentPoints = PointCostByTier[2], Seconds = SecondsByTier[2],
                    Resources = new[]
                    {
                        new ModuleResourceCost(ProcessingCatalog.BasicPartId, 70),
                        new ModuleResourceCost(primaryMaterialId, 25),
                        new ModuleResourceCost(secondaryMaterialId, 10)
                    } },
                new ModuleTierCost { DevelopmentPoints = PointCostByTier[3], Seconds = SecondsByTier[3],
                    Resources = new[]
                    {
                        new ModuleResourceCost(ProcessingCatalog.BasicPartId, 120),
                        new ModuleResourceCost(primaryMaterialId, 50),
                        new ModuleResourceCost(secondaryMaterialId, 25),
                        new ModuleResourceCost(EnergyNuclearId, 20)
                    } },
                new ModuleTierCost { DevelopmentPoints = PointCostByTier[4], Seconds = SecondsByTier[4],
                    Resources = new[]
                    {
                        new ModuleResourceCost(ProcessingCatalog.BasicPartId, 200),
                        new ModuleResourceCost(primaryMaterialId, 90),
                        new ModuleResourceCost(secondaryMaterialId, 50),
                        new ModuleResourceCost(CoreNuclearId, 1)
                    } }
            };
        }

        private static ModuleDef[] Build()
        {
            // Material ids, matching ProcessingCatalog.MaterialIds order (Metal/Wood/Gas/Water/Diamond families).
            const string metalAlloy = "material.metal_alloy";
            const string biocomposite = "material.biocomposite";
            const string refinedGas = "material.refined_gas";
            const string purifiedWater = "material.purified_water";
            const string industrialDiamond = "material.industrial_diamond";

            return new[]
            {
                // --- Industry: the only functional branch this slice (3 free T1s) ---
                new ModuleDef("industry.logistics_cargo", ModuleBranch.Industry, "Logistics Cargo",
                    "Ship cargo hold. Tier capacity: T1 150, T2 300, T3 600, T4 1000, T5 1500.",
                    unlockLevel: 1, milestoneLabel: null, maxTierThisSlice: ModuleDef.MaxTier, lockedMessage: null,
                    tierCosts: StandardTiers(biocomposite, metalAlloy),
                    runtimeConsumerDescription: "LogisticsCargo.Capacity", visualAnchorId: "anchor.industry.logistics_cargo"),

                new ModuleDef("industry.resource_processing", ModuleBranch.Industry, "Resource Processing",
                    "Processing bench. Conversions are instant and unmetered; T1 is free and T2-T5 currently grant no additional effect (open balance decision).",
                    unlockLevel: 1, milestoneLabel: null, maxTierThisSlice: ModuleDef.MaxTier, lockedMessage: null,
                    tierCosts: StandardTiers(purifiedWater, metalAlloy),
                    runtimeConsumerDescription: "ShipProcessing bench (tier recorded, no effect applied)", visualAnchorId: "anchor.industry.resource_processing",
                    // P1-ECO-015: T1 is the last tier that does anything. Purchasable
                    // beyond that, and the interface now says so before the player pays.
                    maxEffectiveTierThisSlice: 1),

                new ModuleDef("industry.station_construction", ModuleBranch.Industry, "Station Construction",
                    "Drill/Command/Laser construction, plus (VS-011) the orbital System Logistics Center T1/T2. T3+ not implemented this slice.",
                    unlockLevel: 1, milestoneLabel: null, maxTierThisSlice: 2, lockedMessage: LockedMessageStationCap,
                    tierCosts: StandardTiers(metalAlloy, biocomposite),
                    runtimeConsumerDescription: "ShipExtraction build commands (F/K/L) + ShipLogisticsCenter T1/T2", visualAnchorId: "anchor.industry.station_construction"),

                // --- Exploration-Logistics ---
                new ModuleDef("exploration.scanner", ModuleBranch.Exploration, "Scanner",
                    "Improved planet scanning + signal analysis. T1/T2 functional this slice (VS-006); T3+ not implemented.",
                    unlockLevel: 5, milestoneLabel: null, maxTierThisSlice: 2, lockedMessage: LockedMessageExplorationCap,
                    tierCosts: StandardTiers(industrialDiamond, biocomposite),
                    runtimeConsumerDescription: "ShipExtraction scan radius/duration + signal analysis duration", visualAnchorId: "anchor.exploration.scanner"),

                new ModuleDef("exploration.hangar_bot", ModuleBranch.Exploration, "Hangar Bot",
                    "Vehicle hangar: builds and launches a Scout and a Collector on same-system physical missions. T1/T2 functional this slice (VS-007); T3+ not implemented.",
                    unlockLevel: 10, milestoneLabel: "First Controlled Planet", maxTierThisSlice: 2, lockedMessage: LockedMessageExplorationCap,
                    tierCosts: StandardTiers(metalAlloy, biocomposite),
                    runtimeConsumerDescription: "ShipHangar command capacity + vehicle production", visualAnchorId: "anchor.exploration.hangar_bot"),

                new ModuleDef("exploration.engine_navigation", ModuleBranch.Exploration, "Engine Navigation",
                    "Improved engine/navigation systems: local top speed + deep-space cruise. T1/T2 functional this slice (VS-006); T3+ not implemented.",
                    unlockLevel: 10, milestoneLabel: null, maxTierThisSlice: 2, lockedMessage: LockedMessageExplorationCap,
                    tierCosts: StandardTiers(refinedGas, purifiedWater),
                    runtimeConsumerDescription: "ShipController local top speed + ShipCruise deep-space cruise", visualAnchorId: "anchor.exploration.engine_navigation"),

                // --- Defense-Combat ---
                // VS-013.1: Hull Reinforcement is a PURE Armor+Hull multiplier, applied on
                // top of the level-derived Hull Form baseline (Nebula.Combat.CombatCatalog.
                // HullFormForLevel) -- it never owns speed/turn/hardpoint/shield anymore
                // (that was VS-013's original bug; fixed here). T0=x1 (real, distinct
                // no-op baseline), T1=x1.15, T2=x1.30. T3+ out of this slice's scope.
                new ModuleDef("defense.hull_reinforcement", ModuleBranch.Defense, "Hull Reinforcement",
                    "Ship Armor+Hull multiplier (never Shield/speed/turn/hardpoints): T0 x1, T1 x1.15, T2 x1.30, applied on top of the level-derived Hull Form baseline. T1-T2 functional this slice (VS-013.1); T3+ not implemented.",
                    unlockLevel: 15, milestoneLabel: null, maxTierThisSlice: 2, lockedMessage: LockedMessageNotImplemented,
                    tierCosts: StandardTiers(metalAlloy, refinedGas),
                    runtimeConsumerDescription: "Nebula.Combat.CombatCatalog.HullReinforcementMultiplier (Armor+Hull only)", visualAnchorId: "anchor.defense.hull_reinforcement"),

                // VS-013.1: real T1-T2 Shield-max multiplier consumer (T1 x1.25, T2 x1.50 +
                // 8s->7s regen delay), gated on the real "First AI Threat" milestone
                // predicate (Nebula.Modules.ModuleMilestones -> FirstProbeState.ThreatKnown).
                new ModuleDef("defense.shield_systems", ModuleBranch.Defense, "Shield Systems",
                    "Ship Shield-max multiplier (never Armor/Hull/speed/turn/hardpoints/energy): T0 x1, T1 x1.25, T2 x1.50 (T2 also shortens post-hit regen delay 8s->7s). Requires the real First AI Threat milestone. T1-T2 functional this slice (VS-013.1); T3+ not implemented.",
                    unlockLevel: 25, milestoneLabel: "First AI Threat", maxTierThisSlice: 2, lockedMessage: LockedMessageNotImplemented,
                    tierCosts: StandardTiers(metalAlloy, refinedGas),
                    runtimeConsumerDescription: "Nebula.Combat.CombatCatalog.ShieldSystemsMultiplier (Shield only)", visualAnchorId: "anchor.defense.shield_systems"),

                // VS-013: functional T1-T2 this slice -- gates active hardpoint slot count
                // (min(hull hardpoints, tier+1)) and weapon-family unlock (T1 Plasma, T2 EM).
                new ModuleDef("defense.weapon_coordination", ModuleBranch.Defense, "Weapon Coordination",
                    "Weapon systems coordination: active hardpoint slots + Plasma(T1)/EM(T2) unlock. T1-T2 functional this slice (VS-013); T3+ not implemented.",
                    unlockLevel: 30, milestoneLabel: "Command Center", maxTierThisSlice: 2, lockedMessage: LockedMessageNotImplemented,
                    tierCosts: StandardTiers(metalAlloy, refinedGas),
                    runtimeConsumerDescription: "Nebula.Combat.CombatCatalog active hardpoint slots + weapon family gate", visualAnchorId: "anchor.defense.weapon_coordination"),

                // --- Nuclear-Command ---
                new ModuleDef("nuclear.star_energy_storage", ModuleBranch.Nuclear, "Star Energy Storage",
                    "System-wide energy storage. Consumer not implemented this slice.",
                    unlockLevel: 61, milestoneLabel: "System Dominance", maxTierThisSlice: 0, lockedMessage: LockedMessageNotImplemented,
                    tierCosts: StandardTiers(industrialDiamond, refinedGas),
                    runtimeConsumerDescription: "(planned) star energy production", visualAnchorId: "anchor.nuclear.star_energy_storage"),

                new ModuleDef("nuclear.laboratory", ModuleBranch.Nuclear, "Nuclear Laboratory",
                    "Nuclear research laboratory. Consumer not implemented this slice.",
                    unlockLevel: 65, milestoneLabel: "Active Collector", maxTierThisSlice: 0, lockedMessage: LockedMessageNotImplemented,
                    tierCosts: StandardTiers(industrialDiamond, refinedGas),
                    runtimeConsumerDescription: "(planned) nuclear lab function", visualAnchorId: "anchor.nuclear.laboratory"),

                new ModuleDef("nuclear.interstellar_logistics", ModuleBranch.Nuclear, "Interstellar Logistics",
                    "Interstellar-scale logistics. Consumer not implemented this slice.",
                    unlockLevel: 81, milestoneLabel: "Two Dominant Systems", maxTierThisSlice: 0, lockedMessage: LockedMessageNotImplemented,
                    tierCosts: StandardTiers(industrialDiamond, refinedGas),
                    runtimeConsumerDescription: "(planned) interstellar travel/logistics", visualAnchorId: "anchor.nuclear.interstellar_logistics"),
            };
        }

        /// <summary>Free starting modules — the only ones granted for free at T1.</summary>
        public static readonly string[] FreeStartingModuleIds =
        {
            "industry.logistics_cargo", "industry.resource_processing", "industry.station_construction"
        };

        // Tier effect table for the one functional Industry module that still has one (index 0 = T1).
        // Index 0 is the free T1 tier every campaign starts with, so it is also the
        // real starting cargo capacity — keep it equal to ShipLogistics.BaseCapacity.
        //
        // The whole ladder was scaled by 1.25 when the start moved 120 -> 150, rather
        // than widening only the first rung. The step ratios are therefore exactly what
        // they were before (2.0, 2.0, 1.667, 1.5): every upgrade buys the same relative
        // gain it always did, and nothing downstream that reasons in "a tier roughly
        // doubles the hold" changes meaning. Raising the floor alone would have
        // flattened T1 -> T2 to 1.6x and quietly made the first upgrade the weakest.
        public static readonly float[] CargoCapacityByTier = { 150f, 300f, 600f, 1000f, 1500f };

        /// <summary>The processing module's id. Named once so nothing has to spell it.</summary>
        public const string ResourceProcessingId = "industry.resource_processing";

        // The resource_processing tier tables are deliberately gone. They held slot
        // counts (1/2/3/4/6) and speed multipliers (x1 ... x3), and the bench model
        // has neither quantity to scale: there is one bench, and a conversion has no
        // duration for a multiplier to divide. Leaving the tables behind would have
        // left two numbers describing a bay that no longer exists, which is how a
        // page ends up promising a benefit the runtime does not grant. What T2-T5
        // should buy instead is an open balance decision, recorded in
        // UIDesign/Finalization/RESOURCE-PROCESSING-BENCH-DESIGN.md section 1; until it
        // is made, ShipProcessing.ApplyTier records the tier and applies nothing, and
        // T1 access stays free through GrantFreeStartingModules.

        // Scanner tier effect tables (VS-006 §3; index 0 = T1, index 1 = T2). T0
        // uses the original ShipExtraction defaults (1.5R / 8s) directly.
        public static readonly float[] ScanRadiusMultiplierByTier = { 2f, 3f };
        public static readonly float[] ScanSecondsByTier = { 6f, 4f };
        public static readonly float[] SignalAnalysisSecondsByTier = { 8f, 4f };

        // Engine-Navigation tier effect tables (VS-006 §4; index 0 = T1, index 1
        // = T2). T0 local top speed equals the T1 value — cruise itself is
        // unavailable below T1.
        public static readonly float[] LocalTopSpeedByTier = { 180f, 194.4f };
        public static readonly float[] CruiseSpeedByTier = { 1800f, 4500f };

        // Hangar-Bot tier effect tables (VS-007 §1; index 0 = T1, index 1 = T2).
        // Command capacity only counts launched/on-mission vehicles, never ones
        // sitting idle in the hangar. Production stays a single slot at every tier.
        public static readonly int[] HangarCommandCapacityByTier = { 2, 4 };
        public static readonly int[] HangarProductionSlotsByTier = { 1, 1 };

        /// <summary>Sum of every module's full T1-T5 point cost, minus the three free starting T1s. Must equal 93.</summary>
        public static int TotalSpendablePoints()
        {
            int total = 0;
            for (int m = 0; m < Modules.Length; m++)
                for (int t = 0; t < Modules[m].TierCosts.Length; t++)
                    total += Modules[m].TierCosts[t].DevelopmentPoints;
            total -= FreeStartingModuleIds.Length * PointCostByTier[0];
            return total;
        }

        /// <summary>Rejects empty/duplicate ids, incomplete tier tables, negative costs, invalid defs. Returns error strings (empty = valid).</summary>
        public static List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<string>();
            foreach (ModuleDef def in Modules)
            {
                if (string.IsNullOrEmpty(def.Id)) errors.Add("Module with empty id.");
                else if (!seen.Add(def.Id)) errors.Add($"Duplicate module id: {def.Id}");

                if (def.TierCosts == null || def.TierCosts.Length != ModuleDef.MaxTier)
                    errors.Add($"{def.Id}: expected {ModuleDef.MaxTier} tier costs, found {def.TierCosts?.Length ?? 0}.");
                else
                {
                    for (int t = 0; t < def.TierCosts.Length; t++)
                    {
                        ModuleTierCost cost = def.TierCosts[t];
                        if (cost.DevelopmentPoints < 0) errors.Add($"{def.Id} T{t + 1}: negative point cost.");
                        if (cost.Seconds < 0f) errors.Add($"{def.Id} T{t + 1}: negative duration.");
                        if (cost.Resources == null) errors.Add($"{def.Id} T{t + 1}: null resource list.");
                        else
                            foreach (ModuleResourceCost r in cost.Resources)
                            {
                                if (string.IsNullOrEmpty(r.ItemId)) errors.Add($"{def.Id} T{t + 1}: empty resource item id.");
                                if (r.Amount < 0f) errors.Add($"{def.Id} T{t + 1}: negative resource amount ({r.ItemId}).");
                            }
                    }
                }

                // P1-DATA-012: a rung may never ask for less than the rung below it.
                // Validate() checked costs, ids and durations but never the level
                // ladder, which is how a T2 gate of 21 sat under a T1 gate of 30 in
                // shipped content without a single test noticing.
                for (int t = 2; t <= ModuleDef.MaxTier; t++)
                    if (def.RequiredLevelForTier(t) < def.RequiredLevelForTier(t - 1))
                        errors.Add($"{def.Id}: T{t} level gate ({def.RequiredLevelForTier(t)}) is below T{t - 1}'s ({def.RequiredLevelForTier(t - 1)}).");

                if (def.MaxTierThisSlice < 0 || def.MaxTierThisSlice > ModuleDef.MaxTier)
                    errors.Add($"{def.Id}: MaxTierThisSlice out of range.");
                if (string.IsNullOrEmpty(def.DisplayName)) errors.Add($"{def.Id}: missing display name.");
                if (string.IsNullOrEmpty(def.VisualAnchorId)) errors.Add($"{def.Id}: missing visual anchor id.");
            }
            return errors;
        }
    }

}
