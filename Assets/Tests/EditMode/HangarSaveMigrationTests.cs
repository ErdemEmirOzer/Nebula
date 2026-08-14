using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for save schema v5 (VS-007 §8) at the DTO/schema layer
    /// — mirrors <see cref="SaveMigrationTests"/>'s scope exactly (a dedicated
    /// high slot, self-cleaning). The actual migration BRANCH (which DTOs
    /// become which live Hangar/vehicle objects) lives in
    /// ExtractionDirector.LoadNow, a MonoBehaviour that needs a real ship —
    /// these tests verify the schema/JSON layer it branches on, including
    /// the new schema-version-aware Load overload that VS-007 needed because
    /// (unlike every earlier migration) an empty vehicles list is a
    /// legitimate real v5 state.
    /// </summary>
    public class HangarSaveMigrationTests
    {
        private const int TestSlot = 92;

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

        [Test]
        public void CurrentSchema_IsPinned()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
        }

        [Test]
        public void V4ShapedFile_LoadsWithEmptyVehiclesList_AndReportsSchema4()
        {
            var legacyPayload = new SavePayload { seed = 777 };
            legacyPayload.navigation = new NavigationDto();
            legacyPayload.navigation.systemKnowledge.Add(new SystemKnowledgeDto { systemId = "sys000", level = 3 });

            string payloadJson = JsonUtility.ToJson(legacyPayload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = 4,
                campaignId = "campaign-1",
                revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(),
                payloadJson = payloadJson
            };
            WriteEnvelope(envelope);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(4, schemaVersion, "the actual saved schema version must be reported, not guessed from DTO emptiness");
            Assert.AreEqual(0, loaded.vehicles.Count, "a v4 file never wrote vehicles — an empty list here is expected and NOT evidence of a real (empty) v5 fleet");
            // JsonUtility can't serialize a null nested object as null — it round-trips
            // as a non-null, all-default instance, so "active" (not nullness) is the real signal.
            Assert.IsFalse(loaded.hangarJob != null && loaded.hangarJob.active, "no real job was ever captured for this legacy payload");
        }

        [Test]
        public void RoundTrip_NoActiveJob_StaysInactiveAfterSaveAndLoad()
        {
            // Regression guard for the JsonUtility null-nested-object quirk:
            // hangarJob is never assigned here (stays the C# default, null),
            // exactly like a real Capture() with no production running.
            var payload = new SavePayload { seed = 9 };
            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));

            SavePayload loaded = SaveService.Load(TestSlot);
            bool hasRealJob = loaded.hangarJob != null && loaded.hangarJob.active;
            Assert.IsFalse(hasRealJob, "a payload with no active job must never round-trip into a phantom active job");
        }

        [Test]
        public void V5RoundTrip_PreservesVehicleAndJobDataExactly()
        {
            var payload = new SavePayload { seed = 42 };
            payload.hangarCommandUsed = 1;
            payload.hangarNextVehicleSequence = 2;
            payload.hangarJob = new HangarJobDto
            {
                active = true, vehicleClass = 1, chosenMaterialId = "material.refined_gas",
                reservedParts = 30, reservedMaterial = 20, totalSeconds = 45f, remainingSeconds = 12.5f
            };
            payload.vehicles.Add(new VehicleDto
            {
                vehicleId = "vehicle-0000", vehicleClass = 0, homeSystemId = "sys001",
                phase = 3, missionKind = 1, targetId = "sys001/pl02",
                phaseTimer = 1.5f, recallRequested = false,
                px = 100f, py = 0f, pz = -50f, rx = 0f, ry = 0f, rz = 0f, rw = 1f,
                heldItemId = null, heldAmount = 0f
            });

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.AreEqual(1, loaded.hangarCommandUsed);
            Assert.AreEqual(2, loaded.hangarNextVehicleSequence);
            Assert.IsNotNull(loaded.hangarJob);
            Assert.AreEqual("material.refined_gas", loaded.hangarJob.chosenMaterialId);
            Assert.AreEqual(12.5f, loaded.hangarJob.remainingSeconds);
            Assert.AreEqual(1, loaded.vehicles.Count);
            Assert.AreEqual("vehicle-0000", loaded.vehicles[0].vehicleId);
            Assert.AreEqual(3, loaded.vehicles[0].phase);
            Assert.AreEqual(100f, loaded.vehicles[0].px);
        }

        [Test]
        public void SaveLoadSave_RoundTrip_NeverDuplicatesVehicles()
        {
            var payload = new SavePayload { seed = 5 };
            payload.vehicles.Add(new VehicleDto { vehicleId = "vehicle-0000", vehicleClass = 0, phase = 0 });

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded1 = SaveService.Load(TestSlot);
            Assert.IsTrue(SaveService.Save(TestSlot, loaded1, 2));
            SavePayload loaded2 = SaveService.Load(TestSlot);

            Assert.AreEqual(1, loaded2.vehicles.Count);
        }

        [Test]
        public void NewerSchema_IsStillRejected()
        {
            var payload = new SavePayload { seed = 1 };
            string payloadJson = JsonUtility.ToJson(payload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = SaveService.CurrentSchema + 1,
                campaignId = "campaign-1", revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(), payloadJson = payloadJson
            };
            WriteEnvelope(envelope);

            Assert.IsNull(SaveService.Load(TestSlot));
        }

        private static void WriteEnvelope(SaveEnvelope envelope)
        {
            string json = JsonUtility.ToJson(envelope, true);
            string path = SaveService.SlotFile(TestSlot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }
    }
}
