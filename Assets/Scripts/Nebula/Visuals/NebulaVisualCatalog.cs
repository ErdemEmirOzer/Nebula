using System;
using System.Collections.Generic;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Poi;
using Nebula.Sectors;

namespace Nebula.Visuals
{
    /// <summary>
    /// Visual-only scale contract. These values never affect gameplay
    /// colliders, save data, content identity, or simulation dimensions.
    /// </summary>
    public enum NebulaVisualScalePolicy
    {
        Meters,
        UniformToRuntimeDiameter,
        UniformToRuntimeRadius,
        FitToOwnerAnchor,
        FitToTargetBounds,
        FitToGameplayEnvelope
    }

    /// <summary>One immutable Resources-prefab binding for the visual layer.</summary>
    public sealed class NebulaVisualDefinition
    {
        public string VisualId { get; }
        public string ResourcePath { get; }
        public NebulaVisualScalePolicy ScalePolicy { get; }
        public string ScaleAnchorName { get; }
        public string PlacementAnchorName { get; }

        public NebulaVisualDefinition(string visualId, string resourcePath,
            NebulaVisualScalePolicy scalePolicy, string scaleAnchorName = null,
            string placementAnchorName = null)
        {
            VisualId = visualId ?? throw new ArgumentNullException(nameof(visualId));
            ResourcePath = resourcePath ?? throw new ArgumentNullException(nameof(resourcePath));
            ScalePolicy = scalePolicy;
            ScaleAnchorName = scaleAnchorName;
            PlacementAnchorName = placementAnchorName;
        }
    }

    /// <summary>
    /// Visual-only mapping authority for the approved Part 1 and Part 2
    /// runtime assets. It is intentionally independent of RuntimeCatalog and
    /// contains no gameplay values. Star assets are deliberately excluded:
    /// Nebula continues to use its established procedural star presentation.
    /// </summary>
    public static class NebulaVisualCatalog
    {
        private const string Root = "VisualPrefabs/";

        public static readonly NebulaVisualDefinition BlackHole = Def(
            "visual.celestial.black_hole", "Environment/Celestial/BlackHole/BlackHole",
            NebulaVisualScalePolicy.UniformToRuntimeRadius, "event-horizon");

        public static readonly NebulaVisualDefinition BuildCapsule = Def(
            "visual.delivery.build_capsule", "Delivery/Construction/BuildCapsule",
            NebulaVisualScalePolicy.Meters);

        public static readonly NebulaVisualDefinition SafetyMarker = Def(
            "visual.world.safety_marker", "World/SafetyMarker/SafetyMarker",
            NebulaVisualScalePolicy.Meters);

        public static readonly NebulaVisualDefinition TrainingDummy = Def(
            "training.combat_dummy", "Training/CombatDummy/TrainingDummy",
            NebulaVisualScalePolicy.Meters);

        // Part 24 player-ship family. PlayerShip_Master.glb intentionally lives
        // under Assets/ArtSource and never enters this runtime table: its Mk I
        // and Mk II sets repeat anchor names by design. Runtime always loads one
        // isolated hull/stage prefab, keeping anchor lookup unambiguous.
        private static readonly NebulaVisualDefinition[,] PlayerShips = BuildPlayerShips();

        private static readonly Dictionary<PlanetArchetype, NebulaVisualDefinition> Planets =
            new Dictionary<PlanetArchetype, NebulaVisualDefinition>
            {
                { PlanetArchetype.MetallicRocky, Def("visual.celestial.planet.metal", "Environment/Celestial/Planets/Metal/PlanetMetal", NebulaVisualScalePolicy.UniformToRuntimeDiameter) },
                { PlanetArchetype.Biomass, Def("visual.celestial.planet.wood", "Environment/Celestial/Planets/Wood/PlanetWood", NebulaVisualScalePolicy.UniformToRuntimeDiameter) },
                { PlanetArchetype.UpperAtmospherePlatform, Def("visual.celestial.planet.gas", "Environment/Celestial/Planets/Gas/PlanetGas", NebulaVisualScalePolicy.UniformToRuntimeDiameter, "gas-body") },
                { PlanetArchetype.FloatingOceanic, Def("visual.celestial.planet.water", "Environment/Celestial/Planets/Water/PlanetWater", NebulaVisualScalePolicy.UniformToRuntimeDiameter) },
                { PlanetArchetype.CrystalShell, Def("visual.celestial.planet.diamond", "Environment/Celestial/Planets/Diamond/PlanetDiamond", NebulaVisualScalePolicy.UniformToRuntimeDiameter) }
            };

        private static readonly Dictionary<SurfaceAnchorCategory, NebulaVisualDefinition> Foundations =
            new Dictionary<SurfaceAnchorCategory, NebulaVisualDefinition>
            {
                { SurfaceAnchorCategory.Solid, Def("environment.foundation.solid", "Environment/Foundations/Solid/PlanetFoundationSolid", NebulaVisualScalePolicy.Meters) },
                { SurfaceAnchorCategory.Water, Def("environment.foundation.ocean", "Environment/Foundations/Ocean/PlanetFoundationOcean", NebulaVisualScalePolicy.Meters) },
                { SurfaceAnchorCategory.Gas, Def("environment.foundation.gas", "Environment/Foundations/Gas/PlanetFoundationGas", NebulaVisualScalePolicy.Meters) }
            };

        private static readonly NebulaVisualDefinition[] Asteroids =
        {
            Def("environment.asteroid.variant_a", "Environment/Asteroids/VariantA/AsteroidVariantA", NebulaVisualScalePolicy.UniformToRuntimeRadius),
            Def("environment.asteroid.variant_b", "Environment/Asteroids/VariantB/AsteroidVariantB", NebulaVisualScalePolicy.UniformToRuntimeRadius),
            Def("environment.asteroid.variant_c", "Environment/Asteroids/VariantC/AsteroidVariantC", NebulaVisualScalePolicy.UniformToRuntimeRadius)
        };

        private static readonly Dictionary<string, NebulaVisualDefinition> Items =
            new Dictionary<string, NebulaVisualDefinition>(StringComparer.Ordinal)
            {
                { "part.basic", Def("part.basic", "Items/Parts/Basic/BasicBuildingPart", NebulaVisualScalePolicy.Meters) },
                { "resource.metal", Def("resource.metal", "Items/RawResources/Metal/ResourceMetal", NebulaVisualScalePolicy.Meters) },
                { "resource.wood", Def("resource.wood", "Items/RawResources/Wood/ResourceWood", NebulaVisualScalePolicy.Meters) },
                { "resource.gas", Def("resource.gas", "Items/RawResources/Gas/ResourceGas", NebulaVisualScalePolicy.Meters) },
                { "resource.water", Def("resource.water", "Items/RawResources/Water/ResourceWater", NebulaVisualScalePolicy.Meters) },
                { "resource.diamond", Def("resource.diamond", "Items/RawResources/Diamond/ResourceDiamond", NebulaVisualScalePolicy.Meters) },
                { "material.metal_alloy", Def("material.metal_alloy", "Items/ProcessedMaterials/MetalAlloy/MaterialMetalAlloy", NebulaVisualScalePolicy.Meters) },
                { "material.biocomposite", Def("material.biocomposite", "Items/ProcessedMaterials/Biocomposite/MaterialBiocomposite", NebulaVisualScalePolicy.Meters) },
                { "material.refined_gas", Def("material.refined_gas", "Items/ProcessedMaterials/RefinedGas/MaterialRefinedGas", NebulaVisualScalePolicy.Meters) },
                { "material.purified_water", Def("material.purified_water", "Items/ProcessedMaterials/PurifiedWater/MaterialPurifiedWater", NebulaVisualScalePolicy.Meters) },
                { "material.industrial_diamond", Def("material.industrial_diamond", "Items/ProcessedMaterials/IndustrialDiamond/MaterialIndustrialDiamond", NebulaVisualScalePolicy.Meters) },
                { ExtractionCatalog.BuildKitId, Def(ExtractionCatalog.BuildKitId, "Items/Special/UniversalPioneerBuildKit", NebulaVisualScalePolicy.Meters) },
                { ModuleCatalog.EnergyNuclearId, Def(ModuleCatalog.EnergyNuclearId, "Items/Nuclear/NuclearEnergyCell", NebulaVisualScalePolicy.Meters) },
                { ModuleCatalog.CoreNuclearId, Def(ModuleCatalog.CoreNuclearId, "Items/Nuclear/NuclearCore", NebulaVisualScalePolicy.Meters) }
            };

        public static readonly NebulaVisualDefinition Drill = Def(
            "structure.drill.t1", "Structures/Drill/DrillT1",
            NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount");
        public static readonly NebulaVisualDefinition DrillT2 = Def(
            "structure.drill.t2", "Structures/Drill/DrillT2",
            NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount");

        public static readonly NebulaVisualDefinition CargoPod = Def(
            "visual.vehicle.cargo_pod", "Vehicles/Collector/CargoPod",
            NebulaVisualScalePolicy.Meters);

        private static readonly Dictionary<SurfaceAnchorCategory, NebulaVisualDefinition> DrillAdapters =
            new Dictionary<SurfaceAnchorCategory, NebulaVisualDefinition>
            {
                { SurfaceAnchorCategory.Solid, Def("structure.drill_adapter.solid", "Structures/Drill/Adapters/DrillAdapterSolid", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount.core") },
                { SurfaceAnchorCategory.Water, Def("structure.drill_adapter.ocean", "Structures/Drill/Adapters/DrillAdapterOcean", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount.core") },
                { SurfaceAnchorCategory.Gas, Def("structure.drill_adapter.gas", "Structures/Drill/Adapters/DrillAdapterGas", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount.core") }
            };

        private static readonly Dictionary<(SurfaceStructureType type, int tier), NebulaVisualDefinition> Structures =
            new Dictionary<(SurfaceStructureType, int), NebulaVisualDefinition>
            {
                { (SurfaceStructureType.Command, 1), Def("structure.command.t1", "Structures/Command/CommandCenterT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount") },
                { (SurfaceStructureType.Command, 2), Def("structure.command.t2", "Structures/Command/CommandCenterT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount") },
                { (SurfaceStructureType.Laser, 1), Def("structure.defense.laser.t1", "Structures/Defense/LaserDefenseT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount") },
                { (SurfaceStructureType.Laser, 2), Def("structure.defense.laser.t2", "Structures/Defense/LaserDefenseT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount") },
                { (SurfaceStructureType.Plasma, 1), Def("structure.defense.plasma.t1", "Structures/Defense/PlasmaDefenseT1", NebulaVisualScalePolicy.Meters) },
                { (SurfaceStructureType.Plasma, 2), Def("structure.defense.plasma.t2", "Structures/Defense/PlasmaDefenseT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.foundation") },
                { (SurfaceStructureType.SensorArray, 1), Def("structure.support.sensor_array.t1", "Structures/Support/SensorArrayT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount") },
                { (SurfaceStructureType.SensorArray, 2), Def("structure.support.sensor_array.t2", "Structures/Support/SensorArrayT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount") },
                { (SurfaceStructureType.MaintenancePlatform, 1), Def("structure.support.maintenance_platform.t1", "Structures/Support/MaintenancePlatformT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount") },
                { (SurfaceStructureType.MaintenancePlatform, 2), Def("structure.support.maintenance_platform.t2", "Structures/Support/MaintenancePlatformT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount") }
            };

        private static readonly Dictionary<LogisticsCenterTier, NebulaVisualDefinition> LogisticsCenters =
            new Dictionary<LogisticsCenterTier, NebulaVisualDefinition>
            {
                { LogisticsCenterTier.T1, Def("station.system_logistics.t1", "Structures/Logistics/SystemLogisticsCenterT1", NebulaVisualScalePolicy.Meters) },
                { LogisticsCenterTier.T2, Def("station.system_logistics.t2", "Structures/Logistics/SystemLogisticsCenterT2", NebulaVisualScalePolicy.Meters) }
            };

        private static readonly Dictionary<string, NebulaVisualDefinition> Vehicles =
            new Dictionary<string, NebulaVisualDefinition>(StringComparer.Ordinal)
            {
                { VehicleCatalog.ScoutId, Def(VehicleCatalog.ScoutId, "Vehicles/Scout/ScoutMk1", NebulaVisualScalePolicy.Meters) },
                { VehicleCatalog.CollectorId, Def(VehicleCatalog.CollectorId, "Vehicles/Collector/CollectorMk1", NebulaVisualScalePolicy.Meters) },
                { VehicleCatalog.CollectorMk2Id, Def(VehicleCatalog.CollectorMk2Id, "Vehicles/Collector/CollectorMk2", NebulaVisualScalePolicy.Meters) },
                { VehicleCatalog.EngineerMk1Id, Def(VehicleCatalog.EngineerMk1Id, "Vehicles/Engineer/EngineerMk1", NebulaVisualScalePolicy.Meters) },
                { VehicleCatalog.EngineerMk2Id, Def(VehicleCatalog.EngineerMk2Id, "Vehicles/Engineer/EngineerMk2", NebulaVisualScalePolicy.Meters) },
                { VehicleCatalog.EscortMk1Id, Def(VehicleCatalog.EscortMk1Id, "Vehicles/Escort/EscortMk1", NebulaVisualScalePolicy.Meters) },
                { VehicleCatalog.EscortMk2Id, Def(VehicleCatalog.EscortMk2Id, "Vehicles/Escort/EscortMk2", NebulaVisualScalePolicy.Meters) }
            };

        private static readonly Dictionary<string, NebulaVisualDefinition> Enemies =
            new Dictionary<string, NebulaVisualDefinition>(StringComparer.Ordinal)
            {
                { EnemyIds.ScoutProbeMk1, Def(EnemyIds.ScoutProbeMk1, "Enemies/ScoutProbe/EnemyScoutProbeMk1", NebulaVisualScalePolicy.UniformToRuntimeDiameter) },
                { EnemyIds.FighterMk1, Def(EnemyIds.FighterMk1, "Enemies/Fighter/EnemyFighterMk1", NebulaVisualScalePolicy.UniformToRuntimeDiameter) },
                { EnemyIds.LooterTransportMk1, Def(EnemyIds.LooterTransportMk1, "Enemies/LooterTransport/EnemyLooterTransportMk1", NebulaVisualScalePolicy.UniformToRuntimeDiameter) },
                { EnemyIds.EmDisruptorMk1, Def(EnemyIds.EmDisruptorMk1, "Enemies/EmDisruptor/EmDisruptorMk1", NebulaVisualScalePolicy.UniformToRuntimeDiameter) },
                { EnemyIds.HeavyAssaultMk1, Def(EnemyIds.HeavyAssaultMk1, "Enemies/HeavyAssault/HeavyAssaultMk1", NebulaVisualScalePolicy.UniformToRuntimeDiameter) },
                { EnemyIds.SiegeMk1, Def(EnemyIds.SiegeMk1, "Enemies/Siege/SiegeMk1", NebulaVisualScalePolicy.UniformToRuntimeDiameter) }
            };

        private static readonly Dictionary<(string moduleId, int tier), NebulaVisualDefinition> Modules =
            new Dictionary<(string, int), NebulaVisualDefinition>
            {
                { ("defense.hull_reinforcement", 1), Def("module.defense.hull_reinforcement.t1", "Modules/Defense/HullReinforcement/HullReinforcementT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("defense.hull_reinforcement", 2), Def("module.defense.hull_reinforcement.t2", "Modules/Defense/HullReinforcement/HullReinforcementT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("defense.shield_systems", 1), Def("module.defense.shield_systems.t1", "Modules/Defense/ShieldSystems/ShieldSystemsT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("defense.shield_systems", 2), Def("module.defense.shield_systems.t2", "Modules/Defense/ShieldSystems/ShieldSystemsT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("defense.weapon_coordination", 1), Def("module.defense.weapon_coordination.t1", "Modules/Defense/WeaponCoordination/WeaponCoordinationT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("defense.weapon_coordination", 2), Def("module.defense.weapon_coordination.t2", "Modules/Defense/WeaponCoordination/WeaponCoordinationT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("exploration.engine_navigation", 1), Def("module.exploration.engine_navigation.t1", "Modules/Exploration/EngineNavigation/EngineNavigationT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("exploration.engine_navigation", 2), Def("module.exploration.engine_navigation.t2", "Modules/Exploration/EngineNavigation/EngineNavigationT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("exploration.hangar_bot", 1), Def("module.exploration.hangar_bot.t1", "Modules/Exploration/HangarBot/HangarBotT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("exploration.hangar_bot", 2), Def("module.exploration.hangar_bot.t2", "Modules/Exploration/HangarBot/HangarBotT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("exploration.scanner", 1), Def("module.exploration.scanner.t1", "Modules/Exploration/Scanner/ScannerT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("exploration.scanner", 2), Def("module.exploration.scanner.t2", "Modules/Exploration/Scanner/ScannerT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("industry.logistics_cargo", 1), Def("module.industry.logistics_cargo.t1", "Modules/Industry/LogisticsCargo/LogisticsCargoT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("industry.logistics_cargo", 2), Def("module.industry.logistics_cargo.t2", "Modules/Industry/LogisticsCargo/LogisticsCargoT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("industry.resource_processing", 1), Def("module.industry.resource_processing.t1", "Modules/Industry/ResourceProcessing/ResourceProcessingT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("industry.resource_processing", 2), Def("module.industry.resource_processing.t2", "Modules/Industry/ResourceProcessing/ResourceProcessingT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("industry.station_construction", 1), Def("module.industry.station_construction.t1", "Modules/Industry/StationConstruction/StationConstructionT1", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") },
                { ("industry.station_construction", 2), Def("module.industry.station_construction.t2", "Modules/Industry/StationConstruction/StationConstructionT2", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount") }
            };

        private static readonly Dictionary<WeaponFamily, NebulaVisualDefinition> Weapons =
            new Dictionary<WeaponFamily, NebulaVisualDefinition>
            {
                { WeaponFamily.Laser, Def("weapon.laser", "Weapons/Laser/ShipLaserWeapon", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.hardpoint.mount") },
                { WeaponFamily.Plasma, Def("weapon.plasma", "Weapons/Plasma/ShipPlasmaWeapon", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.hardpoint.mount") },
                { WeaponFamily.Em, Def("weapon.em", "Weapons/EM/ShipEmWeapon", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.hardpoint.mount") }
            };

        private static readonly Dictionary<ProjectileKind, NebulaVisualDefinition> Projectiles =
            new Dictionary<ProjectileKind, NebulaVisualDefinition>
            {
                { ProjectileKind.Plasma, Def("projectile.plasma", "Projectiles/Player/Plasma/PlasmaProjectile", NebulaVisualScalePolicy.FitToGameplayEnvelope) },
                { ProjectileKind.EnemyPlasma, Def("projectile.plasma", "Projectiles/Player/Plasma/PlasmaProjectile", NebulaVisualScalePolicy.FitToGameplayEnvelope) },
                { ProjectileKind.Em, Def("projectile.em", "Projectiles/Player/EM/EmProjectile", NebulaVisualScalePolicy.FitToGameplayEnvelope) },
                { ProjectileKind.EnemySiege, Def("projectile.enemy.siege", "Projectiles/Hostile/Siege/EnemySiegeProjectile", NebulaVisualScalePolicy.FitToGameplayEnvelope) }
            };

        public static readonly NebulaVisualDefinition LaserBeam = Def(
            "vfx.weapon.laser.beam", "VFX/Weapons/Laser/LaserBeamVisual", NebulaVisualScalePolicy.Meters);

        private static readonly Dictionary<PoiType, NebulaVisualDefinition> Pois =
            new Dictionary<PoiType, NebulaVisualDefinition>
            {
                { PoiType.Anomaly, Def("poi.anomaly", "POI/Anomaly/Anomaly", NebulaVisualScalePolicy.Meters) },
                { PoiType.Wreck, Def("poi.wreck", "POI/Wreck/Wreck", NebulaVisualScalePolicy.Meters) },
                { PoiType.AbandonedFacility, Def("poi.abandoned_facility", "POI/AbandonedFacility/AbandonedFacility", NebulaVisualScalePolicy.Meters) }
            };

        public static readonly NebulaVisualDefinition SensorRelayResolved = Def(
            "poi.sensor_relay.resolved", "POI/SensorRelay/SensorRelayResolved",
            NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.surface_mount");

        private static readonly NebulaVisualDefinition[,] WreckVariants =
        {
            {
                Def("poi.wreck.small.fresh", "POI/Wreck/Variants/Wreck_small_fresh", NebulaVisualScalePolicy.Meters),
                Def("poi.wreck.small.depleted", "POI/Wreck/Variants/Wreck_small_depleted", NebulaVisualScalePolicy.Meters)
            },
            {
                Def("poi.wreck.medium.fresh", "POI/Wreck/Variants/Wreck_medium_fresh", NebulaVisualScalePolicy.Meters),
                Def("poi.wreck.medium.depleted", "POI/Wreck/Variants/Wreck_medium_depleted", NebulaVisualScalePolicy.Meters)
            },
            {
                Def("poi.wreck.large.fresh", "POI/Wreck/Variants/Wreck_large_fresh", NebulaVisualScalePolicy.Meters),
                Def("poi.wreck.large.depleted", "POI/Wreck/Variants/Wreck_large_depleted", NebulaVisualScalePolicy.Meters)
            }
        };

        public static readonly NebulaVisualDefinition ProtectedPirateBase = Def(
            "poi.pirate_base.protected", "POI/PirateBase/ProtectedPirateBase", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount");
        public static readonly NebulaVisualDefinition SalvageNode = Def(
            "visual.poi.salvage_node", "POI/Salvage/SalvageNode", NebulaVisualScalePolicy.Meters, placementAnchorName: "anchor.mount");
        public static readonly NebulaVisualDefinition CombatWreckage = Def(
            "visual.combat.wreckage_cache", "Combat/Wreckage/CombatWreckageCache", NebulaVisualScalePolicy.Meters);
        public static readonly NebulaVisualDefinition StructureConstruction = Def(
            "visual.state.structure.construction_scaffold", "StateKits/Structure/Construction/StructureConstructionScaffold", NebulaVisualScalePolicy.FitToTargetBounds);
        public static readonly NebulaVisualDefinition StructureDamageDisabled = Def(
            "visual.state.structure.damage_disabled", "StateKits/Structure/DamageDisabled/StructureDamageDisabledOverlay", NebulaVisualScalePolicy.FitToTargetBounds);
        public static readonly NebulaVisualDefinition StructureRepairUpgrade = Def(
            "visual.state.structure.repair_upgrade", "StateKits/Structure/RepairUpgrade/StructureRepairUpgradeFrame", NebulaVisualScalePolicy.FitToTargetBounds);

        private static readonly IReadOnlyList<NebulaVisualDefinition> AllDefinitions = BuildAll();
        public static IReadOnlyList<NebulaVisualDefinition> All => AllDefinitions;

        public static bool TryGetPlanet(PlanetArchetype archetype, out NebulaVisualDefinition definition) =>
            Planets.TryGetValue(archetype, out definition);

        public static bool TryGetFoundation(SurfaceAnchorCategory category, out NebulaVisualDefinition definition) =>
            Foundations.TryGetValue(category, out definition);

        public static NebulaVisualDefinition Asteroid(int variantIndex) =>
            Asteroids[Math.Abs(variantIndex) % Asteroids.Length];

        public static bool TryGetItem(string contentId, out NebulaVisualDefinition definition)
        {
            if (contentId != null) return Items.TryGetValue(contentId, out definition);
            definition = null;
            return false;
        }

        public static bool TryGetDrillAdapter(
            SurfaceAnchorCategory category, out NebulaVisualDefinition definition) =>
            DrillAdapters.TryGetValue(category, out definition);

        public static NebulaVisualDefinition DrillForTier(int tier) =>
            tier >= 2 ? DrillT2 : Drill;

        public static NebulaVisualDefinition WreckVariant(int salvageNodeCount, bool depleted)
        {
            int size = salvageNodeCount <= 2 ? 0 : salvageNodeCount == 3 ? 1 : 2;
            return WreckVariants[size, depleted ? 1 : 0];
        }

        public static bool TryGetStructure(
            SurfaceStructureType type, int tier, out NebulaVisualDefinition definition) =>
            Structures.TryGetValue((type, tier >= 2 ? 2 : 1), out definition);

        public static bool TryGetLogisticsCenter(
            LogisticsCenterTier tier, out NebulaVisualDefinition definition) =>
            LogisticsCenters.TryGetValue(tier, out definition);

        public static bool TryGetVehicle(string vehicleId, out NebulaVisualDefinition definition)
        {
            if (vehicleId != null) return Vehicles.TryGetValue(vehicleId, out definition);
            definition = null;
            return false;
        }

        public static bool TryGetEnemy(string enemyId, out NebulaVisualDefinition definition)
        {
            if (enemyId != null) return Enemies.TryGetValue(enemyId, out definition);
            definition = null;
            return false;
        }

        public static bool TryGetModule(string moduleId, int tier, out NebulaVisualDefinition definition) =>
            Modules.TryGetValue((moduleId, tier >= 2 ? 2 : 1), out definition);

        public static bool TryGetWeapon(WeaponFamily family, out NebulaVisualDefinition definition) =>
            Weapons.TryGetValue(family, out definition);

        public static bool TryGetProjectile(ProjectileKind kind, out NebulaVisualDefinition definition) =>
            Projectiles.TryGetValue(kind, out definition);

        public static bool TryGetPoi(PoiType type, out NebulaVisualDefinition definition) =>
            Pois.TryGetValue(type, out definition);

        public static bool TryGetPlayerShip(
            int hullForm, int cumulativeStage, out NebulaVisualDefinition definition)
        {
            int hullIndex = hullForm >= 2 ? 1 : 0;
            int stageIndex = Math.Max(0, Math.Min(12, cumulativeStage));
            definition = PlayerShips[hullIndex, stageIndex];
            return definition != null;
        }

        private static NebulaVisualDefinition Def(string visualId, string relativePath,
            NebulaVisualScalePolicy scalePolicy, string scaleAnchorName = null,
            string placementAnchorName = null) =>
            new NebulaVisualDefinition(
                visualId, Root + relativePath, scalePolicy, scaleAnchorName, placementAnchorName);

        private static IReadOnlyList<NebulaVisualDefinition> BuildAll()
        {
            var result = new List<NebulaVisualDefinition>(116);
            for (int hull = 0; hull < 2; hull++)
            for (int stage = 0; stage <= 12; stage++)
                result.Add(PlayerShips[hull, stage]);
            result.AddRange(Planets.Values);
            result.Add(BlackHole);
            result.AddRange(Asteroids);
            result.AddRange(Foundations.Values);
            result.AddRange(Items.Values);
            result.Add(BuildCapsule);
            result.Add(SafetyMarker);
            result.Add(TrainingDummy);
            result.Add(Drill);
            result.Add(DrillT2);
            result.AddRange(DrillAdapters.Values);
            result.AddRange(Structures.Values);
            result.AddRange(LogisticsCenters.Values);
            result.AddRange(Vehicles.Values);
            result.Add(CargoPod);
            result.AddRange(Enemies.Values);
            result.AddRange(Modules.Values);
            result.AddRange(Weapons.Values);
            result.Add(Projectiles[ProjectileKind.Plasma]);
            result.Add(Projectiles[ProjectileKind.Em]);
            result.Add(Projectiles[ProjectileKind.EnemySiege]);
            result.Add(LaserBeam);
            result.AddRange(Pois.Values);
            result.Add(SensorRelayResolved);
            for (int size = 0; size < 3; size++)
            for (int state = 0; state < 2; state++)
                result.Add(WreckVariants[size, state]);
            result.Add(ProtectedPirateBase);
            result.Add(SalvageNode);
            result.Add(CombatWreckage);
            result.Add(StructureConstruction);
            result.Add(StructureDamageDisabled);
            result.Add(StructureRepairUpgrade);
            return result.AsReadOnly();
        }

        private static NebulaVisualDefinition[,] BuildPlayerShips()
        {
            var result = new NebulaVisualDefinition[2, 13];
            for (int hull = 0; hull < 2; hull++)
            {
                string mark = hull == 0 ? "MkI" : "MkII";
                string visualMark = hull == 0 ? "mki" : "mkii";
                for (int stage = 0; stage <= 12; stage++)
                {
                    string variant = stage == 0 ? "Base" : $"Stage{stage:00}";
                    string visualVariant = stage == 0 ? "base" : $"stage{stage:00}";
                    result[hull, stage] = Def(
                        $"visual.ship.player.{visualMark}.{visualVariant}",
                        $"PlayerShip/{mark}/PlayerShip_{mark}_{variant}",
                        NebulaVisualScalePolicy.FitToGameplayEnvelope);
                }
            }
            return result;
        }
    }
}
