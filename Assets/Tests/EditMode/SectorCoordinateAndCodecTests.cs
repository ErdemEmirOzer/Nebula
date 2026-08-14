using NUnit.Framework;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure sector coordinate/id grammar (VS-008 §1/§10).</summary>
    public class SectorCoordinateAndCodecTests
    {
        [Test]
        public void SectorCoordinate_NegativeCoordinates_RoundTripThroughId()
        {
            var c = new SectorCoordinate(-3, -1, 7);
            Assert.AreEqual("sector.-3.-1.7", c.Id);
            Assert.IsTrue(SectorCoordinate.TryParse(c.Id, out SectorCoordinate parsed));
            Assert.AreEqual(c, parsed);
        }

        [Test]
        public void SectorCoordinate_FromAbsolutePosition_NegativeAxis_FloorsCorrectly()
        {
            // -1 unit is inside sector -1 (floor(-1/2,000,000) == -1), not sector 0.
            var c = SectorCoordinate.FromAbsolutePosition(new UnityEngine.Vector3(-1f, 0f, 0f));
            Assert.AreEqual(-1, c.X);
        }

        [Test]
        public void SectorCoordinate_Neighbors26_Has26UniqueNeverSelf()
        {
            var c = new SectorCoordinate(0, 0, 0);
            var seen = new System.Collections.Generic.HashSet<SectorCoordinate>();
            int count = 0;
            foreach (SectorCoordinate n in c.Neighbors26())
            {
                Assert.AreNotEqual(c, n);
                Assert.IsTrue(seen.Add(n));
                count++;
            }
            Assert.AreEqual(26, count);
        }

        [Test]
        public void WorldIdCodec_SystemAndPlanetId_RoundTrip()
        {
            var sector = new SectorCoordinate(2, -5, 0);
            string systemId = WorldIdCodec.SystemId(sector, 3);
            Assert.AreEqual("sector.2.-5.0/system.003", systemId);

            string planetId = WorldIdCodec.PlanetId(systemId, 7);
            Assert.AreEqual("sector.2.-5.0/system.003/planet.07", planetId);

            Assert.IsTrue(WorldIdCodec.TryParseSystemId(systemId, out SectorCoordinate s2, out int idx2));
            Assert.AreEqual(sector, s2);
            Assert.AreEqual(3, idx2);

            Assert.IsTrue(WorldIdCodec.TryParsePlanetIndex(planetId, out int pIdx));
            Assert.AreEqual(7, pIdx);
        }

        [Test]
        public void WorldIdCodec_SystemIdFromPlanetWorldId_UsesLastSlash_NotFirst()
        {
            // The bug this guards against: taking everything before the FIRST
            // slash of a canonical id returns just "sector.x.y.z" (missing
            // "/system.NNN") because the sector id itself already has a dot-
            // separated (not slash-separated) coordinate — but a naive
            // first-slash split over the WHOLE id would still be wrong the
            // moment a second segment is introduced. This asserts the codec
            // recovers the full system id, not a truncated prefix.
            var sector = new SectorCoordinate(0, 0, 0);
            string systemId = WorldIdCodec.SystemId(sector, 0);
            string planetId = WorldIdCodec.PlanetId(systemId, 5);

            string recovered = WorldIdCodec.SystemIdFromPlanetWorldId(planetId);
            Assert.AreEqual(systemId, recovered);
            Assert.AreNotEqual(sector.Id, recovered, "must not truncate to just the sector id");
        }

        [Test]
        public void WorldIdCodec_IsPlanetIdAndIsSystemId_AreMutuallyExclusive()
        {
            var sector = new SectorCoordinate(1, 1, 1);
            string systemId = WorldIdCodec.SystemId(sector, 0);
            string planetId = WorldIdCodec.PlanetId(systemId, 0);

            Assert.IsTrue(WorldIdCodec.IsSystemId(systemId));
            Assert.IsFalse(WorldIdCodec.IsPlanetId(systemId));
            Assert.IsTrue(WorldIdCodec.IsPlanetId(planetId));
            Assert.IsFalse(WorldIdCodec.IsSystemId(planetId));
        }
    }
}
