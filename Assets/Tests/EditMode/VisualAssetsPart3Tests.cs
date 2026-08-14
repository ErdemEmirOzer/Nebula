using System.Collections.Generic;
using System.Linq;
using Nebula.Combat;
using Nebula.Modules;
using Nebula.Poi;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    public class VisualAssetsPart3Tests
    {
        private static readonly HashSet<string> LodVisualIds = new HashSet<string>
        {
            "weapon.laser", "weapon.plasma", "weapon.em",
            "poi.abandoned_facility", "poi.anomaly", "poi.pirate_base.protected",
            "visual.poi.salvage_node", "visual.combat.wreckage_cache",
            "visual.state.structure.construction_scaffold",
            "visual.state.structure.damage_disabled",
            "visual.state.structure.repair_upgrade"
        };

        /// <summary>Static resolver cache, cleared at both ends so fixture order
        /// cannot reach any assertion below.</summary>
        [SetUp]
        public void SetUp() => NebulaVisualResolver.ClearCache();

        [TearDown]
        public void TearDown() => NebulaVisualResolver.ClearCache();

        [Test]
        public void Part3_ContainsExactlyThirtyFourApprovedNonStarPrefabs()
        {
            NebulaVisualDefinition[] part3 = Part3();
            Assert.AreEqual(34, part3.Length);
            Assert.AreEqual(34, part3.Select(x => x.VisualId).Distinct().Count());
            Assert.AreEqual(34, part3.Select(x => x.ResourcePath).Distinct().Count());
            Assert.IsFalse(part3.Any(x => x.VisualId.Contains("star") || x.ResourcePath.Contains("/Stars/")));
        }

        [Test]
        public void EveryPart3Prefab_LoadsWithRenderers_AndNoActiveGameplayComponents()
        {
            foreach (NebulaVisualDefinition definition in Part3())
            {
                GameObject owner = new GameObject("part3 owner");
                try
                {
                    GameObject instance = NebulaVisualResolver.Instantiate(definition, owner.transform, 12f);
                    Assert.IsNotNull(instance, definition.VisualId);
                    Assert.Greater(instance.GetComponentsInChildren<Renderer>(true).Length, 0, definition.VisualId);
                    Assert.IsFalse(instance.GetComponentsInChildren<Collider>(true).Any(x => x.enabled), definition.VisualId);
                    Assert.IsFalse(instance.GetComponentsInChildren<Camera>(true).Any(x => x.enabled), definition.VisualId);
                    Assert.IsFalse(instance.GetComponentsInChildren<Light>(true).Any(x => x.enabled), definition.VisualId);
                    foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.IsNotNull(material, definition.VisualId);
                        Assert.IsNotNull(material.shader, definition.VisualId);
                        Assert.AreNotEqual("Hidden/InternalErrorShader", material.shader.name, definition.VisualId);
                    }
                }
                finally { Object.DestroyImmediate(owner); }
            }
        }

        [Test]
        public void Part3_PreservesOnlySuppliedLodGroups()
        {
            foreach (NebulaVisualDefinition definition in Part3())
            {
                GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
                LODGroup group = prefab.GetComponent<LODGroup>();
                if (!LodVisualIds.Contains(definition.VisualId))
                {
                    Assert.IsNull(group, $"{definition.VisualId} received an invented LOD.");
                    continue;
                }
                Assert.IsNotNull(group, $"{definition.VisualId} lost supplied LODs.");
                Assert.AreEqual(3, group.GetLODs().Length, definition.VisualId);
                Assert.AreEqual(LODFadeMode.None, group.fadeMode, definition.VisualId);
                Assert.IsFalse(group.animateCrossFading, definition.VisualId);
            }
        }

        [Test]
        public void AllNineSupportedModuleFamilies_MapBothTiers()
        {
            string[] ids =
            {
                "defense.hull_reinforcement", "defense.shield_systems", "defense.weapon_coordination",
                "exploration.engine_navigation", "exploration.hangar_bot", "exploration.scanner",
                "industry.logistics_cargo", "industry.resource_processing", "industry.station_construction"
            };
            foreach (string id in ids)
            for (int tier = 1; tier <= 2; tier++)
            {
                Assert.IsTrue(NebulaVisualCatalog.TryGetModule(id, tier, out NebulaVisualDefinition definition), $"{id}/T{tier}");
                Assert.AreEqual($"module.{id}.t{tier}", definition.VisualId);
            }
        }

        [Test]
        public void WeaponsAndProjectiles_HaveTypedMappingsAndRequiredAnchors()
        {
            foreach (WeaponFamily family in new[] { WeaponFamily.Laser, WeaponFamily.Plasma, WeaponFamily.Em })
            {
                Assert.IsTrue(NebulaVisualCatalog.TryGetWeapon(family, out NebulaVisualDefinition definition));
                GameObject owner = new GameObject("weapon owner");
                try
                {
                    GameObject visual = NebulaVisualResolver.Instantiate(definition, owner.transform);
                    Assert.IsNotNull(NebulaVisualResolver.FindAnchor(visual.transform, "anchor.hardpoint.mount"));
                    string muzzle = family == WeaponFamily.Laser ? "anchor.muzzle.laser" : "anchor.projectile.spawn";
                    Assert.IsNotNull(NebulaVisualResolver.FindAnchor(visual.transform, muzzle));
                }
                finally { Object.DestroyImmediate(owner); }
            }

            foreach (ProjectileKind kind in new[] { ProjectileKind.Plasma, ProjectileKind.Em, ProjectileKind.EnemySiege })
                Assert.IsTrue(NebulaVisualCatalog.TryGetProjectile(kind, out _), kind.ToString());
        }

        [Test]
        public void PoiWreckageAndStateKits_HaveCompleteTypedCoverage()
        {
            foreach (PoiType type in new[] { PoiType.Anomaly, PoiType.Wreck, PoiType.AbandonedFacility })
                Assert.IsTrue(NebulaVisualCatalog.TryGetPoi(type, out _), type.ToString());

            Assert.IsNotNull(NebulaVisualResolver.Load(NebulaVisualCatalog.ProtectedPirateBase.ResourcePath));
            Assert.IsNotNull(NebulaVisualResolver.Load(NebulaVisualCatalog.SalvageNode.ResourcePath));
            Assert.IsNotNull(NebulaVisualResolver.Load(NebulaVisualCatalog.CombatWreckage.ResourcePath));
            Assert.IsNotNull(NebulaVisualResolver.Load(NebulaVisualCatalog.StructureConstruction.ResourcePath));
            Assert.IsNotNull(NebulaVisualResolver.Load(NebulaVisualCatalog.StructureDamageDisabled.ResourcePath));
            Assert.IsNotNull(NebulaVisualResolver.Load(NebulaVisualCatalog.StructureRepairUpgrade.ResourcePath));
        }

        private static NebulaVisualDefinition[] Part3() =>
            NebulaVisualCatalog.All.Where(x =>
                x.ResourcePath.StartsWith("VisualPrefabs/Modules/")
                || x.ResourcePath.StartsWith("VisualPrefabs/Weapons/")
                || x.ResourcePath.StartsWith("VisualPrefabs/Projectiles/")
                || x.ResourcePath.StartsWith("VisualPrefabs/VFX/")
                || (x.ResourcePath.StartsWith("VisualPrefabs/POI/")
                    && !x.ResourcePath.StartsWith("VisualPrefabs/POI/SensorRelay/")
                    && !x.ResourcePath.StartsWith("VisualPrefabs/POI/Wreck/Variants/"))
                || x.ResourcePath.StartsWith("VisualPrefabs/Combat/")
                || x.ResourcePath.StartsWith("VisualPrefabs/StateKits/")).ToArray();
    }
}
