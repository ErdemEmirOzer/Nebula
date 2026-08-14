using Nebula.UI.StrategicMap.Preview;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Derelict and anomaly rows in the reference's data carry no galaxy column, so their
    /// position starts one field earlier than every other row's. Reading them at the wrong
    /// offset is silent: the ids and names still line up, and the only symptom is markers
    /// standing thousands of units outside the universe with a tumble speed or a layer size
    /// where their z ought to be. These pin the coordinates so that cannot come back.
    /// </summary>
    public sealed class StrategicMapDiscoveryMarkerTests
    {
        private static StrategicMapUniverseSnapshot Load()
        {
            var snapshot = StrategicMapStaticPreviewSource.Load();
            Assert.IsNotNull(snapshot, "universe snapshot must load from Resources");
            return snapshot;
        }

        [Test]
        public void DiscoveryPoints_AreTheReferenceCount()
        {
            StrategicMapUniverseSnapshot universe = Load();
            Assert.AreEqual(4, universe.derelicts.Count);
            Assert.AreEqual(4, universe.anomalies.Count);
        }

        [Test]
        public void DiscoveryPoints_SitInsideTheUniverse()
        {
            StrategicMapUniverseSnapshot universe = Load();

            // Every other object in the map lives within a couple of dozen units of the
            // centre; the starfield shell itself only reaches 158.
            foreach (StrategicMapUniverseSnapshot.MarkerRow row in universe.derelicts)
                AssertInsideUniverse(row, universe);
            foreach (StrategicMapUniverseSnapshot.MarkerRow row in universe.anomalies)
                AssertInsideUniverse(row, universe);
        }

        private static void AssertInsideUniverse(StrategicMapUniverseSnapshot.MarkerRow row,
            StrategicMapUniverseSnapshot universe)
        {
            float distance = Vector3.Distance(row.Position, universe.UniverseCenter);
            Assert.Less(distance, 40f,
                $"{row.id} is {distance:F0} units from the universe centre, which means its " +
                "position was read from the wrong columns");
        }

        [Test]
        public void DiscoveryPoints_MatchTheReferenceCoordinates()
        {
            StrategicMapUniverseSnapshot universe = Load();

            // Straight out of the reference's own rows, read with vectorFromRow(row, 2).
            AssertPosition(universe.derelicts[0], "derelict-1", 9.91816395f, -12.03577947f, -8.96082665f);
            AssertPosition(universe.anomalies[0], "anomaly-1", -5.21946136f, -9.19936884f, -12.21298232f);
        }

        private static void AssertPosition(StrategicMapUniverseSnapshot.MarkerRow row, string id,
            float x, float y, float z)
        {
            Assert.AreEqual(id, row.id);
            Assert.AreEqual(x, row.x, 1e-4f);
            Assert.AreEqual(y, row.y, 1e-4f);
            Assert.AreEqual(z, row.z, 1e-4f);
        }
    }
}
