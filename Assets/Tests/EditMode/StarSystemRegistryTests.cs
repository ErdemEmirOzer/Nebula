using NUnit.Framework;
using UnityEngine;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the VS-008 manifest-backed registry: no live
    /// Transform is required for a system to resolve, and identical
    /// seed/coordinate always agrees (spec §10).
    /// </summary>
    public class StarSystemRegistryTests
    {
        [TearDown]
        public void Cleanup() => StarSystemRegistry.Clear();

        [Test]
        public void TryGet_ResolvesWithoutAnyMaterializedTransform()
        {
            StarSystemRegistry.Initialize(777UL);
            var startSector = new SectorCoordinate(0, 0, 0);
            string startId = WorldIdCodec.SystemId(startSector, 0);
            StarSystemRegistry.SetStartSystem(startSector, startId);

            Assert.IsTrue(StarSystemRegistry.TryGet(startId, out StarSystemRecord record));
            Assert.IsNull(record.StarTransform, "a resolved record must not require materialization");
            Assert.IsTrue(record.IsStartSystem);
            Assert.AreEqual(startId, StarSystemRegistry.StartSystemId);
        }

        [Test]
        public void SystemsNear_ReturnsTheContainingSectorPlusNeighbors_NeverGlobal()
        {
            StarSystemRegistry.Initialize(42UL);
            var origin = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(origin, WorldIdCodec.SystemId(origin, 0));

            int count = 0;
            foreach (StarSystemRecord r in StarSystemRegistry.SystemsNear(origin.Center)) count++;
            Assert.Greater(count, 0);

            // Every returned record must belong to the origin sector or one of its 26 neighbors.
            foreach (StarSystemRecord r in StarSystemRegistry.SystemsNear(origin.Center))
            {
                Assert.IsTrue(WorldIdCodec.TryParseSystemId(r.SystemId, out SectorCoordinate sc, out _));
                bool isNeighborOrSelf = sc.Equals(origin);
                if (!isNeighborOrSelf) foreach (SectorCoordinate n in origin.Neighbors26()) if (n.Equals(sc)) { isNeighborOrSelf = true; break; }
                Assert.IsTrue(isNeighborOrSelf);
            }
        }

        [Test]
        public void TryFindContaining_MatchesTheSystemWhoseRadiusContainsThePosition()
        {
            StarSystemRegistry.Initialize(1234UL);
            var origin = new SectorCoordinate(0, 0, 0);
            string startId = WorldIdCodec.SystemId(origin, 0);
            StarSystemRegistry.SetStartSystem(origin, startId);
            StarSystemRegistry.TryGet(startId, out StarSystemRecord start);

            Assert.IsTrue(StarSystemRegistry.TryFindContaining(start.AbsoluteCenter, out StarSystemRecord found));
            Assert.AreEqual(startId, found.SystemId);

            Vector3 farAway = start.AbsoluteCenter + Vector3.one * (start.NavigationRadius * 50f + 10_000_000f);
            Assert.IsFalse(StarSystemRegistry.TryFindContaining(farAway, out _));
        }

        [Test]
        public void PlanetWorldIdPrefix_UsesTheLastSlash_MatchesTheCodec()
        {
            var sector = new SectorCoordinate(1, -2, 3);
            string systemId = WorldIdCodec.SystemId(sector, 2);
            string planetId = WorldIdCodec.PlanetId(systemId, 5);
            Assert.AreEqual(systemId, WorldIdCodec.SystemIdFromPlanetWorldId(planetId));
        }
    }
}
