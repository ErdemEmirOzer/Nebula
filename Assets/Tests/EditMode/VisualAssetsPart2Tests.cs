using System.Collections.Generic;
using System.Linq;
using Nebula.Combat;
using Nebula.Hangar;
using Nebula.Ship;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    public class VisualAssetsPart2Tests
    {
        /// <summary>
        /// Both registries are static and both are swept clean at each end, not just
        /// on the way out. Teardown alone only protects the fixtures that come after
        /// this one; a neighbour that leaves an agent enrolled would still be visible
        /// to the first test here, and "which fixture ran first" is not allowed to be
        /// an input.
        /// </summary>
        [SetUp]
        public void SetUp() => ClearStaticState();

        [TearDown]
        public void TearDown() => ClearStaticState();

        private static void ClearStaticState()
        {
            EnemyAgent.ClearRegistry();
            VehicleAgent.Registry.Clear();
            NebulaVisualResolver.ClearCache();
        }

        [Test]
        public void Part2_ContainsExactlyThirtyStructureVehicleAndEnemyPrefabs()
        {
            NebulaVisualDefinition[] part2 = NebulaVisualCatalog.All.Where(x =>
                (x.ResourcePath.StartsWith("VisualPrefabs/Structures/")
                 && x.VisualId != "structure.drill.t2")
                || x.ResourcePath.StartsWith("VisualPrefabs/Vehicles/")
                || x.ResourcePath.StartsWith("VisualPrefabs/Enemies/")).ToArray();

            Assert.AreEqual(30, part2.Length);
            Assert.AreEqual(30, part2.Select(x => x.VisualId).Distinct().Count());
            Assert.AreEqual(30, part2.Select(x => x.ResourcePath).Distinct().Count());
        }

        [Test]
        public void Part2_PreservesEverySuppliedLod_WithoutCrossFade()
        {
            NebulaVisualDefinition[] part2 = NebulaVisualCatalog.All.Where(x =>
                (x.ResourcePath.StartsWith("VisualPrefabs/Structures/")
                 && x.VisualId != "structure.drill.t2")
                || x.ResourcePath.StartsWith("VisualPrefabs/Vehicles/")
                || x.ResourcePath.StartsWith("VisualPrefabs/Enemies/")).ToArray();

            foreach (NebulaVisualDefinition definition in part2)
            {
                GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
                Assert.IsNotNull(prefab, definition.VisualId);
                LODGroup group = prefab.GetComponent<LODGroup>();
                if (definition.VisualId == "structure.defense.plasma.t1")
                {
                    Assert.IsNull(group, "Plasma T1 has no supplied LOD files and must not receive generated geometry.");
                    continue;
                }

                Assert.IsNotNull(group, $"{definition.VisualId} lost its supplied LOD group");
                Assert.AreEqual(3, group.GetLODs().Length, definition.VisualId);
                Assert.AreEqual(LODFadeMode.None, group.fadeMode, definition.VisualId);
                Assert.IsFalse(group.animateCrossFading, definition.VisualId);
            }
        }

        [Test]
        public void VehicleAgents_UseTypedAuthoredVisuals_AndSwapOnMkUpgrade()
        {
            var shipObject = new GameObject("test ship");
            VehicleAgent collector = null;
            try
            {
                ShipController ship = shipObject.AddComponent<ShipController>();
                collector = VehicleAgent.Create(ship, "vehicle-test", VehicleCatalog.Collector);
                AssertVisualId(collector.gameObject, VehicleCatalog.CollectorId);

                collector.UpgradeDefInPlace(VehicleCatalog.CollectorMk2);
                AssertVisualId(collector.gameObject, VehicleCatalog.CollectorMk2Id);
                Assert.IsNotNull(collector.GetComponentsInChildren<NebulaVisualInstance>(true)
                    .FirstOrDefault(x => x.VisualId == "visual.vehicle.cargo_pod"));
                Assert.IsFalse(collector.GetComponentsInChildren<Collider>(true)
                    .Any(x => x.GetComponentInParent<NebulaVisualInstance>() != null && x.enabled));
            }
            finally
            {
                if (collector != null) Object.DestroyImmediate(collector.gameObject);
                Object.DestroyImmediate(shipObject);
            }
        }

        [Test]
        public void EveryEnemyDefinition_UsesItsTypedAuthoredVisual()
        {
            var roots = new List<GameObject>();
            try
            {
                foreach (EnemyDef definition in EnemyCatalog.All)
                {
                    EnemyAgent enemy = EnemyAgent.Create(
                        "visual-test-" + definition.Id, definition, Vector3.zero, null, null, null);
                    roots.Add(enemy.gameObject);
                    AssertVisualId(enemy.gameObject, definition.Id);
                    Assert.IsFalse(enemy.GetComponentsInChildren<Collider>(true)
                        .Any(x => x.GetComponentInParent<NebulaVisualInstance>() != null && x.enabled));
                }
            }
            finally
            {
                foreach (GameObject root in roots) Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SurfaceMountedPart2Visuals_AlignTheirAuthoredAnchorToOwnerOrigin()
        {
            AssertSurfaceAnchor("structure.command.t1", "anchor.surface_mount");
            AssertSurfaceAnchor("structure.defense.laser.t2", "anchor.surface_mount");
            AssertSurfaceAnchor("structure.defense.plasma.t2", "anchor.foundation");
            AssertSurfaceAnchor("structure.support.sensor_array.t1", "anchor.surface_mount");
            AssertSurfaceAnchor("structure.support.maintenance_platform.t2", "anchor.surface_mount");
            AssertSurfaceAnchor("structure.drill.t1", "anchor.surface_mount");
            AssertSurfaceAnchor("structure.drill_adapter.solid", "anchor.mount.core");
        }

        private static void AssertVisualId(GameObject owner, string expected)
        {
            NebulaVisualInstance[] visuals = owner.GetComponentsInChildren<NebulaVisualInstance>(true);
            Assert.IsTrue(visuals.Any(x => x.VisualId == expected),
                $"{owner.name} is missing authored visual {expected}");
        }

        private static void AssertSurfaceAnchor(string visualId, string anchorName)
        {
            NebulaVisualDefinition definition =
                NebulaVisualCatalog.All.Single(x => x.VisualId == visualId);
            var owner = new GameObject("anchor owner");
            try
            {
                GameObject visual = NebulaVisualResolver.Instantiate(definition, owner.transform);
                Assert.IsNotNull(visual);
                Transform anchor = NebulaVisualResolver.FindAnchor(visual.transform, anchorName);
                Assert.IsNotNull(anchor, $"{visualId}/{anchorName}");
                Assert.Less(anchor.position.sqrMagnitude, 0.0001f,
                    $"{visualId}/{anchorName} is not aligned to the gameplay owner origin");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
