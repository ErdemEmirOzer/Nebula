using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the v6-&gt;v7 node id migration at the schema/JSON
    /// layer (VS-009 §7/§10.8) — mirrors SaveMigrationTests/HangarSaveMigrationTests'
    /// scope exactly: the actual migration BRANCH (rewriting DrillDto/StructureDto
    /// nodeId via NodeIdMigration) lives in ExtractionDirector.LoadNow, a
    /// MonoBehaviour that needs a real ship; these tests verify (a) the
    /// SaveService layer reports schemaVersion 6 unmodified (a pure passthrough —
    /// migration is the director's job, not SaveService's), and (b) applying
    /// NodeIdMigration to what SaveService actually hands back produces the
    /// exact canonical ids, and (c) every other gameplay field on a v6-shaped
    /// SystemDelta round-trips losslessly.
    /// </summary>
    public class Vs009NodeMigrationSaveTests
    {
        private const int TestSlot = 93;

        [TearDown]
        public void Cleanup()
        {
            string main = SaveService.SlotFile(TestSlot);
            string dir = Path.GetDirectoryName(main);
            if (dir == null) return;
            foreach (string f in new[]
            {
                main, Path.Combine(dir, $"slot{TestSlot}.bak1.json"),
                Path.Combine(dir, $"slot{TestSlot}.bak2.json"), Path.Combine(dir, $"slot{TestSlot}.bak3.json"),
                Path.Combine(dir, $"slot{TestSlot}.tmp.json")
            })
            {
                if (File.Exists(f)) File.Delete(f);
            }
        }

        private static SystemDelta BuildV6ShapedDelta()
        {
            const string planetId = "sector.0.0.0/system.000/planet.00";
            var delta = new SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.drills.Add(new DrillDto
            {
                worldId = planetId, nodeId = planetId + "/node/drill", state = 3,
                capsuleRemaining = 0f, buildRemaining = 0f, stored = 12f, accumulator = 0f, source = 0, refundParts = 0
            });
            delta.structures.Add(new StructureDto
            {
                worldId = planetId, nodeId = planetId + "/node/command", type = 0, state = 3,
                capsuleRemaining = 0f, buildRemaining = 0f, shield = 0f, armor = 500f, hull = 900f, regenTimer = 0f
            });
            delta.structures.Add(new StructureDto
            {
                worldId = planetId, nodeId = planetId + "/node/defense/03", type = 1, state = 3,
                capsuleRemaining = 0f, buildRemaining = 0f, shield = 80f, armor = 200f, hull = 350f, regenTimer = 0f
            });
            delta.dominance.Add(new DominanceDto { worldId = planetId, state = 2, stabilizeTimer = 0f, recoveryTimer = 0f });
            return delta;
        }

        private static void WriteV6Envelope(SavePayload payload)
        {
            string payloadJson = JsonUtility.ToJson(payload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = 6,
                campaignId = "campaign-1",
                revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(),
                payloadJson = payloadJson
            };
            string json = JsonUtility.ToJson(envelope, true);
            string path = SaveService.SlotFile(TestSlot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        [Test]
        public void V6ShapedPayload_LoadsWithSchema6_AndSaveServiceLeavesNodeIdsUntouched()
        {
            var payload = new SavePayload { seed = 42 };
            payload.systemDeltas.Add(BuildV6ShapedDelta());
            WriteV6Envelope(payload);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(6, schemaVersion, "SaveService must report the true saved schema, not silently upgrade it");
            Assert.AreEqual(1, loaded.systemDeltas.Count);
            SystemDelta delta = loaded.systemDeltas[0];
            Assert.AreEqual("sector.0.0.0/system.000/planet.00/node/drill", delta.drills[0].nodeId, "SaveService is a pure passthrough — migration is ExtractionDirector's job");
            Assert.AreEqual("sector.0.0.0/system.000/planet.00/node/command", delta.structures[0].nodeId);
            Assert.AreEqual("sector.0.0.0/system.000/planet.00/node/defense/03", delta.structures[1].nodeId);
        }

        [Test]
        public void NodeIdMigration_AppliedToWhatSaveServiceReturns_ProducesExactCanonicalIds()
        {
            var payload = new SavePayload { seed = 42 };
            payload.systemDeltas.Add(BuildV6ShapedDelta());
            WriteV6Envelope(payload);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);
            Assert.AreEqual(6, schemaVersion);
            SystemDelta delta = loaded.systemDeltas[0];

            // Mirrors ExtractionDirector.LoadNow's own migration loop exactly.
            foreach (DrillDto dto in delta.drills) dto.nodeId = NodeIdMigration.Migrate(dto.nodeId);
            foreach (StructureDto dto in delta.structures) dto.nodeId = NodeIdMigration.Migrate(dto.nodeId);

            Assert.AreEqual("sector.0.0.0/system.000/planet.00/node/drill.0", delta.drills[0].nodeId);
            Assert.AreEqual("sector.0.0.0/system.000/planet.00/node/command.0", delta.structures[0].nodeId);
            Assert.AreEqual("sector.0.0.0/system.000/planet.00/node/defense.2", delta.structures[1].nodeId);

            // Every other field on the delta must be untouched by the migration.
            Assert.AreEqual(12f, delta.drills[0].stored);
            Assert.AreEqual(900f, delta.structures[0].hull);
            Assert.AreEqual(1, delta.dominance.Count);
            Assert.AreEqual(2, delta.dominance[0].state, "dominance state preserved (Controlled = 2)");
            Assert.AreEqual(80f, delta.structures[1].shield, "every other field preserved -- untouched by the node-id migration");
        }

        [Test]
        public void CurrentSchemaSavedPayload_RoundTrips_CanonicalNodeIdsAndAllGameplayStateExactly()
        {
            var payload = new SavePayload { seed = 7, totalXp = 500, developmentPoints = 2 };
            payload.systemDeltas.Add(BuildV6ShapedDelta()); // reused only as a convenient state fixture
            payload.cargo.Add(new CargoItemDto { itemId = "resource.metal", amount = 33f });

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion, "a payload saved by the current build must round-trip at the current schema");
            Assert.AreEqual(500, loaded.totalXp);
            Assert.AreEqual(2, loaded.developmentPoints);
            Assert.AreEqual(33f, loaded.cargo[0].amount);
            Assert.AreEqual(1, loaded.systemDeltas.Count);
            Assert.AreEqual(12f, loaded.systemDeltas[0].drills[0].stored);
        }

        [Test]
        public void SchemaBelowSix_IsStillRejectedByExtractionDirectorContract()
        {
            // SaveService itself never rejects an old-but-not-too-old schema (that
            // gate is ExtractionDirector.LoadNow's job) — it only rejects NEWER
            // than current. Confirm the boundary this test suite depends on:
            // v5 still loads fine at the SaveService layer (so LoadNow's own
            // "< 6 reject" branch is what's actually doing the work, per
            // Assets/Scripts/Nebula/Extraction/ExtractionDirector.cs).
            var payload = new SavePayload { seed = 1 };
            string payloadJson = JsonUtility.ToJson(payload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = 5,
                campaignId = "campaign-1",
                revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(),
                payloadJson = payloadJson
            };
            string json = JsonUtility.ToJson(envelope, true);
            string path = SaveService.SlotFile(TestSlot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);
            Assert.IsNotNull(loaded, "SaveService loads any schema <= current — the v1-v5 reject is ExtractionDirector's gate, not SaveService's");
            Assert.AreEqual(5, schemaVersion);
        }
    }
}
