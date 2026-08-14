using System;
using System.Collections.Generic;
using System.Linq;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Poi;

namespace Nebula.Content
{
    /// <summary>
    /// TF-003: the only <see cref="IContentCatalog"/> implementation this
    /// slice — reads the EXISTING static C# catalogs (ExtractionCatalog,
    /// ProcessingCatalog, ModuleCatalog, VehicleCatalog, SurfaceStructureCatalog,
    /// LogisticsCenterCatalog, WeaponCatalog, EnemyCatalog, PoiCatalog) and
    /// deep-copies every value into this file's immutable Content* types.
    /// Never re-declares a stat/cost/gate value by hand — every number here
    /// is read directly from its one real source. Built once per instance
    /// (constructor snapshots every list); a fresh instance always re-reads
    /// the live static catalogs, so mutating a source array before
    /// constructing a NEW CSharpContentCatalog is reflected, but an
    /// ALREADY-BUILT instance (and any RuntimeCatalog built from it) never
    /// changes afterward.
    /// </summary>
    public sealed class CSharpContentCatalog : IContentCatalog
    {
        public IReadOnlyList<ContentItem> Items { get; }
        public IReadOnlyList<ContentRecipe> Recipes { get; }
        public IReadOnlyList<ContentModule> Modules { get; }
        public IReadOnlyList<ContentVehicle> Vehicles { get; }
        public IReadOnlyList<ContentStructure> Structures { get; }
        public IReadOnlyList<ContentLogisticsCenterTier> LogisticsCenterTiers { get; }
        public IReadOnlyList<ContentWeapon> Weapons { get; }
        public IReadOnlyList<ContentEnemy> Enemies { get; }
        public IReadOnlyList<ContentPoiType> PoiTypes { get; }
        public IReadOnlyList<string> FreeStartingModuleIds { get; }

        public CSharpContentCatalog()
        {
            Items = BuildItems();
            Recipes = BuildRecipes();
            Modules = BuildModules();
            Vehicles = BuildVehicles();
            Structures = BuildStructures();
            LogisticsCenterTiers = BuildLogisticsCenterTiers();
            Weapons = BuildWeapons();
            Enemies = BuildEnemies();
            PoiTypes = BuildPoiTypes();
            FreeStartingModuleIds = new List<string>(ModuleCatalog.FreeStartingModuleIds);
        }

        private static List<ContentItem> BuildItems()
        {
            var list = new List<ContentItem>();
            foreach (string id in ExtractionCatalog.ResourceIds) list.Add(MakeItem(id));
            list.Add(MakeItem(ProcessingCatalog.BasicPartId));
            foreach (string id in ProcessingCatalog.MaterialIds) list.Add(MakeItem(id));
            list.Add(MakeItem(ModuleCatalog.EnergyNuclearId));
            list.Add(MakeItem(ModuleCatalog.CoreNuclearId));
            return list;
        }

        private static ContentItem MakeItem(string id) =>
            new ContentItem(id, ProcessingCatalog.ItemDisplayName(id), CargoItemVolumes.PerUnit(id));

        private static List<ContentRecipe> BuildRecipes()
        {
            var list = new List<ContentRecipe>();
            foreach (ProcessingRecipe r in ProcessingCatalog.Recipes)
                list.Add(new ContentRecipe(r.Id, r.InputItemId, r.InputCount, r.OutputItemId, r.OutputCount, r.Seconds, r.DisplayName));
            return list;
        }

        private static List<ContentModule> BuildModules()
        {
            var freeSet = new HashSet<string>(ModuleCatalog.FreeStartingModuleIds, StringComparer.Ordinal);
            var list = new List<ContentModule>();
            foreach (ModuleDef def in ModuleCatalog.Modules)
            {
                var tierCosts = new List<ContentModuleTierCost>();
                for (int t = 0; t < (def.TierCosts?.Length ?? 0); t++)
                {
                    ModuleTierCost tc = def.TierCosts[t];
                    var resources = new List<ContentResourceCost>();
                    foreach (ModuleResourceCost r in tc.Resources ?? Array.Empty<ModuleResourceCost>())
                        resources.Add(new ContentResourceCost(r.ItemId, r.Amount));
                    tierCosts.Add(new ContentModuleTierCost(t + 1, tc.DevelopmentPoints, tc.Seconds, resources));
                }
                list.Add(new ContentModule(def.Id, def.DisplayName, def.UnlockLevel, def.MaxTierThisSlice, freeSet.Contains(def.Id), tierCosts));
            }
            return list;
        }

        private static List<ContentVehicle> BuildVehicles()
        {
            var list = new List<ContentVehicle>();
            foreach (VehicleDef def in VehicleCatalog.All)
            {
                List<ContentResourceCost> upgrade = VehicleCatalog.TryGetUpgradeRecipe(RecipeFromIdFor(def.Id), out VehicleUpgradeRecipe upgradeRecipe) && upgradeRecipe.ToVehicleId == def.Id
                    ? upgradeRecipe.MaterialCosts.Select(c => new ContentResourceCost(c.itemId, c.amount)).Prepend(new ContentResourceCost(ProcessingCatalog.BasicPartId, upgradeRecipe.PartsCost)).ToList()
                    : new List<ContentResourceCost>();
                List<ContentResourceCost> fixedDirect = VehicleCatalog.TryGetFixedDirectBuildRecipe(def.Id, out VehicleUpgradeRecipe directRecipe)
                    ? directRecipe.MaterialCosts.Select(c => new ContentResourceCost(c.itemId, c.amount)).Prepend(new ContentResourceCost(ProcessingCatalog.BasicPartId, directRecipe.PartsCost)).ToList()
                    : new List<ContentResourceCost>();
                list.Add(new ContentVehicle(def.Id, def.DisplayName, def.UnlockLevel, def.RequiredHangarTier, def.Mk,
                    def.BuildSeconds, def.Speed, def.Shield, def.Armor, def.Hull,
                    def.ScanRange, def.ScanSeconds, def.CargoCapacity, def.TransferVolumePerSecond,
                    def.BasicPartCost, def.SpecialMaterialCost, upgrade,
                    def.CommandCost, def.RepairRatePerSecond, def.DisabledVehicleRecoverSeconds,
                    def.FixedLaserDps, def.FixedLaserRange, fixedDirect));
            }
            return list;
        }

        /// <summary>VS-015: maps a Mk II def id back to its Mk I "from" id so the upgrade recipe (keyed by from-id) can be located while building the Mk II's own content entry.</summary>
        private static string RecipeFromIdFor(string toOrFromId) => toOrFromId switch
        {
            VehicleCatalog.CollectorMk2Id => VehicleCatalog.CollectorId,
            VehicleCatalog.EngineerMk2Id => VehicleCatalog.EngineerMk1Id,
            VehicleCatalog.EscortMk2Id => VehicleCatalog.EscortMk1Id,
            _ => toOrFromId
        };

        private static List<ContentStructure> BuildStructures()
        {
            var list = new List<ContentStructure>();
            foreach (SurfaceStructureDef def in new[] { SurfaceStructureCatalog.Command, SurfaceStructureCatalog.Laser })
            {
                string id = def.Type == SurfaceStructureType.Command ? "structure.command" : "structure.laser";
                list.Add(new ContentStructure(id, def.Name, def.PartsCost, def.MaterialCost, def.AnyMaterial,
                    def.BuildSeconds, def.ShieldMax, def.ArmorMax, def.HullMax));
            }

            // VS-014 §1/§2: Command T2, Laser T2, Plasma T1/T2 -- read directly from PlanetaryDefenseCatalog, never re-typed.
            list.Add(MakeTierStructure("structure.command.t2", PlanetaryDefenseCatalog.CommandT2));
            list.Add(MakeTierStructure("structure.defense.laser.t2", PlanetaryDefenseCatalog.LaserT2));
            list.Add(MakeTierStructure("structure.defense.plasma.t1", PlanetaryDefenseCatalog.PlasmaT1));
            list.Add(MakeTierStructure("structure.defense.plasma.t2", PlanetaryDefenseCatalog.PlasmaT2));

            // VS-015 §7/§2: Sensor Array T1/T2, Maintenance Platform T1/T2.
            list.Add(MakeTierStructure("structure.support.sensor_array.t1", PlanetaryDefenseCatalog.SensorArrayT1));
            list.Add(MakeTierStructure("structure.support.sensor_array.t2", PlanetaryDefenseCatalog.SensorArrayT2));
            list.Add(MakeTierStructure("structure.support.maintenance_platform.t1", PlanetaryDefenseCatalog.MaintenancePlatformT1));
            list.Add(MakeTierStructure("structure.support.maintenance_platform.t2", PlanetaryDefenseCatalog.MaintenancePlatformT2));
            return list;
        }

        private static ContentStructure MakeTierStructure(string id, StructureTierDef def)
        {
            var materialCosts = def.MaterialCosts.Select(c => new ContentResourceCost(c.ItemId, c.Amount)).ToList();
            return new ContentStructure(id, def.DisplayName, def.PartsCost, 0, false,
                def.BuildSeconds, def.ShieldMax, def.ArmorMax, def.HullMax,
                def.Tier, def.RequiredCommandTier, def.RequiredWeaponCoordinationTier, materialCosts,
                def.Dps, def.Range, def.TurnDegreesPerSecond, def.ContinuousFireSecondsBeforeOverheat, def.OverheatCooldownSeconds,
                def.SplashRadius, def.SplashMaxFractionOfDirect,
                def.WarningBonusSeconds, def.EngineerRepairMultiplier, def.RecoveryTimeMultiplier, def.RequiresLocalEngineer);
        }

        private static List<ContentLogisticsCenterTier> BuildLogisticsCenterTiers()
        {
            var list = new List<ContentLogisticsCenterTier>();
            foreach (LogisticsCenterTierDef def in new[] { LogisticsCenterCatalog.T1, LogisticsCenterCatalog.T2 })
            {
                string id = $"{LogisticsCenterCatalog.CenterId}.t{(int)def.Tier}";
                List<ContentResourceCost> costs = def.Cost().Select(c => new ContentResourceCost(c.itemId, c.amount)).ToList();
                list.Add(new ContentLogisticsCenterTier(id, $"System Logistics Center {(int)def.Tier}", (int)def.Tier,
                    def.RequiredLevel, def.RequiredModuleTier, costs, def.BuildSeconds, def.Capacity, def.MaxRoutes,
                    def.DockCount, def.TransferVolumePerSecond, def.ShieldMax, def.ArmorMax, def.HullMax));
            }
            return list;
        }

        private static List<ContentWeapon> BuildWeapons()
        {
            return new List<ContentWeapon>
            {
                BuildWeapon(WeaponFamily.Laser, "weapon.laser", "Laser", WeaponCatalog.LaserBaseDps, WeaponCatalog.LaserBaseRange),
                BuildWeapon(WeaponFamily.Plasma, "weapon.plasma", "Plasma", WeaponCatalog.PlasmaBaseDps, WeaponCatalog.PlasmaBaseRange),
                BuildWeapon(WeaponFamily.Em, "weapon.em", "Heat-Guided EM", WeaponCatalog.EmBaseDps, WeaponCatalog.EmBaseRange)
            };
        }

        private static ContentWeapon BuildWeapon(WeaponFamily family, string id, string displayName, float baseDps, float baseRange)
        {
            var tierCosts = new List<ContentWeaponTierCost>();
            for (int tier = 1; tier <= WeaponCatalog.MaxTierThisSlice; tier++)
            {
                WeaponTierCost cost = WeaponCatalog.CostForTier(family, tier);
                var resources = new List<ContentResourceCost> { new ContentResourceCost(ProcessingCatalog.BasicPartId, cost.Parts) };
                if (!string.IsNullOrEmpty(cost.MaterialId)) resources.Add(new ContentResourceCost(cost.MaterialId, cost.Material));
                tierCosts.Add(new ContentWeaponTierCost(tier, resources, cost.Seconds));
            }
            return new ContentWeapon(id, displayName, WeaponCatalog.MaxTierThisSlice, baseDps, baseRange, tierCosts);
        }

        private static List<ContentEnemy> BuildEnemies()
        {
            var list = new List<ContentEnemy>();
            foreach (EnemyDef def in EnemyCatalog.All)
                list.Add(new ContentEnemy(def.Id, def.DisplayName, def.Speed, def.ShieldMax, def.ArmorMax, def.HullMax,
                    def.IsCombatCapable, def.Dps, def.AttackRange, def.EngageBandMin, def.EngageBandMax,
                    def.BreakDistance, def.ApproachThreshold, def.RetreatHullFraction, def.ScanSeconds,
                    def.Role.ToString(), def.ThreatBudget, def.CargoCapacityVolume, def.CargoTransferVolumePerSecond,
                    def.TelegraphSeconds, def.DisruptSeconds, def.DisruptImmunitySeconds));
            return list;
        }

        private static List<ContentPoiType> BuildPoiTypes()
        {
            var list = new List<ContentPoiType>
            {
                new ContentPoiType("poi.anomaly", "Anomaly", PoiCatalog.AnomalySafeWeight, PoiCatalog.AnomalyUnstableWeight, PoiCatalog.AnomalyXp, new List<ContentResourceCost>()),
                new ContentPoiType("poi.wreck", "Wreck", PoiCatalog.WreckSafeWeight, PoiCatalog.WreckUnstableWeight, PoiCatalog.WreckXp, new List<ContentResourceCost>())
            };
            List<ContentResourceCost> facilityCost = FacilityService.RelayCost().Select(c => new ContentResourceCost(c.itemId, c.amount)).ToList();
            list.Add(new ContentPoiType("poi.abandoned_facility", "Abandoned Facility", PoiCatalog.FacilitySafeWeight, PoiCatalog.FacilityUnstableWeight, PoiCatalog.FacilityXp, facilityCost));

            // VS-014 §1/§14: the Protected Pirate Base -- 0/0 weights so PoiManifestGenerator's
            // weighted pick never selects it (it is NOT part of the existing procedural POI
            // distribution); it is only ever born dynamically after the first resolved Pirate
            // raid. Registered here purely so its stable id is validated/fingerprinted like
            // every other real content id -- its one-time XP/parts/material reward is a
            // separate raid-outcome ledger event, never this XpReward field.
            list.Add(new ContentPoiType("poi.pirate_base", "Protected Pirate Base", 0f, 0f, 0, new List<ContentResourceCost>()));
            return list;
        }
    }
}
