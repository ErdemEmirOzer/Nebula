using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure v6->v7 node id string remap (VS-009 §5/§7/§10.8).</summary>
    public class NodeIdMigrationTests
    {
        private const string WorldId = "sector.0.0.0/system.000/planet.00";

        [Test]
        public void Migrate_DrillAndCommand_MapToDotZeroSlots()
        {
            Assert.AreEqual(WorldId + "/node/drill.0", NodeIdMigration.Migrate(WorldId + "/node/drill"));
            Assert.AreEqual(WorldId + "/node/command.0", NodeIdMigration.Migrate(WorldId + "/node/command"));
        }

        [Test]
        public void Migrate_Defense01Through07_MapToZeroBasedDefenseZeroThroughSix()
        {
            for (int old = 1; old <= 7; old++)
            {
                string oldId = $"{WorldId}/node/defense/{old:00}";
                string expected = $"{WorldId}/node/defense.{old - 1}";
                Assert.AreEqual(expected, NodeIdMigration.Migrate(oldId), $"defense/{old:00}");
            }
        }

        [Test]
        public void Migrate_Support01Through03_MapToZeroBasedSupportZeroThroughTwo()
        {
            for (int old = 1; old <= 3; old++)
            {
                string oldId = $"{WorldId}/node/support/{old:00}";
                string expected = $"{WorldId}/node/support.{old - 1}";
                Assert.AreEqual(expected, NodeIdMigration.Migrate(oldId), $"support/{old:00}");
            }
        }

        [Test]
        public void Migrate_AlreadyCanonicalIds_PassThroughUnchanged()
        {
            Assert.AreEqual(WorldId + "/node/drill.0", NodeIdMigration.Migrate(WorldId + "/node/drill.0"));
            Assert.AreEqual(WorldId + "/node/defense.3", NodeIdMigration.Migrate(WorldId + "/node/defense.3"));
        }

        [Test]
        public void Migrate_NullOrEmpty_ReturnsInputUnchanged()
        {
            Assert.IsNull(NodeIdMigration.Migrate(null));
            Assert.AreEqual(string.Empty, NodeIdMigration.Migrate(string.Empty));
        }

        [Test]
        public void Migrate_FullTwelveNodeSet_ProducesNoDuplicatesOrOrphans()
        {
            var oldIds = new List<string> { WorldId + "/node/drill", WorldId + "/node/command" };
            for (int i = 1; i <= 7; i++) oldIds.Add($"{WorldId}/node/defense/{i:00}");
            for (int i = 1; i <= 3; i++) oldIds.Add($"{WorldId}/node/support/{i:00}");
            Assert.AreEqual(12, oldIds.Count);

            var migrated = new HashSet<string>();
            foreach (string oldId in oldIds) Assert.IsTrue(migrated.Add(NodeIdMigration.Migrate(oldId)), $"duplicate migrated id for {oldId}");
            Assert.AreEqual(12, migrated.Count, "every migrated id must be unique — no collisions, no orphans");
        }
    }
}
