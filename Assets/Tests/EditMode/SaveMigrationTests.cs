using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for save schema v3 and v1/v2 migration presence-detection
    /// (VS-005 §8, tests 27-29). Uses a dedicated high slot number
    /// (<see cref="TestSlot"/>) so these tests never touch a real player save,
    /// and cleans its own files up in TearDown. The actual migration BRANCH
    /// logic lives in ExtractionDirector (a MonoBehaviour that needs
    /// ShipLogistics/ShipProcessing/ShipModules and a scene) — these tests
    /// verify the schema/JSON layer SaveService gives it to branch on.
    /// </summary>
    public class SaveMigrationTests
    {
        private const int TestSlot = 91;

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
        public void CurrentSchemaIsTheOneThisSliceShipped()
        {
            // VS-007 raised this from 4 to 5 (Hangar), VS-008 raised it to 6
            // (sector manifests/streaming), VS-009 raised it to 7 (typed star
            // class/environment + canonical node scheme), VS-010 raised it to
            // 8 (Quick Asset Package / evacuation transactions + frontier provenance),
            // VS-011 raised it to 9 (System Logistics Center + Collector routes),
            // VS-012 raised it to 10 (POI: Anomaly/Wreck/Abandoned Facility),
            // VS-013 raised it to 11 (active combat core), VS-014 raised it to 12
            // (planetary defense tiers + raids + Pirate Base), VS-017A.2 raised it
            // to 15 (timed processing bay -> instant bench).
            Assert.AreEqual(15, SaveService.CurrentSchema);
        }

        [Test]
        public void V2ShapedFile_LoadsWithEmptyV3Lists_AndPopulatedLegacyProcessorField()
        {
            // Hand-construct a v2-shaped file: schemaVersion 2, the old single
            // "processor" field populated, v3's processorSlots/moduleTiers absent
            // (JsonUtility leaves them at their List defaults: empty, non-null).
            var legacyPayload = new SavePayload { seed = 555 };
            legacyPayload.processor = new ProcessorDto { recipeIndex = 2, batchTimer = 4.5f, waiting = false };
            legacyPayload.totalXp = 999;
            legacyPayload.developmentPoints = 3;

            string payloadJson = JsonUtility.ToJson(legacyPayload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = 2,
                campaignId = "campaign-1",
                revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(),
                payloadJson = payloadJson
            };
            WriteEnvelope(envelope);

            SavePayload loaded = SaveService.Load(TestSlot);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(555, loaded.seed);
            Assert.AreEqual(0, loaded.processorSlots.Count, "v2 files never populated processorSlots");
            Assert.AreEqual(0, loaded.moduleTiers.Count, "v2 files never populated moduleTiers");
            Assert.IsNotNull(loaded.processor);
            Assert.AreEqual(2, loaded.processor.recipeIndex, "the legacy single-slot job must still be readable for migration");
            Assert.AreEqual(999, loaded.totalXp, "XP must be preserved untouched by the module migration");
            Assert.AreEqual(3, loaded.developmentPoints);
        }

        [Test]
        public void NewerSchema_IsRejected()
        {
            var payload = new SavePayload { seed = 1 };
            string payloadJson = JsonUtility.ToJson(payload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = SaveService.CurrentSchema + 1,
                campaignId = "campaign-1",
                revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(),
                payloadJson = payloadJson
            };
            WriteEnvelope(envelope);

            Assert.IsNull(SaveService.Load(TestSlot), "a save from a future schema version must be rejected");
        }

        [Test]
        public void SaveLoadSave_RoundTrip_IsIdempotent()
        {
            var payload = new SavePayload { seed = 42, totalXp = 1000, developmentPoints = 5 };
            payload.cargo.Add(new CargoItemDto { itemId = "resource.water", amount = 55f });
            payload.moduleTiers.Add(new ModuleTierDto { moduleId = "industry.logistics_cargo", tier = 1 });

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded1 = SaveService.Load(TestSlot);
            Assert.IsNotNull(loaded1);
            Assert.AreEqual(55f, loaded1.cargo[0].amount);

            Assert.IsTrue(SaveService.Save(TestSlot, loaded1, 2));
            SavePayload loaded2 = SaveService.Load(TestSlot);

            Assert.AreEqual(loaded1.totalXp, loaded2.totalXp);
            Assert.AreEqual(loaded1.developmentPoints, loaded2.developmentPoints);
            Assert.AreEqual(1, loaded2.cargo.Count, "a second save/load cycle must not duplicate cargo entries");
            Assert.AreEqual(55f, loaded2.cargo[0].amount);
        }

        [Test]
        public void RoundTrip_NoActiveModuleJob_NeverProducesAPhantomJob()
        {
            // Regression guard: JsonUtility serializes a null nested-class
            // field (activeModuleJob left unassigned here, its C# default)
            // as a non-null, all-default instance — so ExtractionDirector's
            // restore branch must not treat "activeModuleJob != null" as
            // "a real job was saved" (moduleId, string.Empty/null in that
            // phantom instance, is the actual signal it checks).
            var payload = new SavePayload { seed = 11 };
            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));

            SavePayload loaded = SaveService.Load(TestSlot);
            bool looksLikeARealJob = loaded.activeModuleJob != null && !string.IsNullOrEmpty(loaded.activeModuleJob.moduleId);
            Assert.IsFalse(looksLikeARealJob, "a payload saved with no active upgrade must never round-trip into a phantom one");
        }

        [Test]
        public void V3ShapedFile_LoadsWithEmptyNavigation_TriggeringMigrationBranch()
        {
            // Hand-written JSON text with no "navigation" key at all — this is
            // what a REAL pre-VS-006 save looks like (its SavePayload class
            // never had the field, so it could never have been written).
            // JsonUtility.ToJson(new SavePayload{...}) would NOT reproduce
            // this faithfully (it always emits every field of the CURRENT
            // class), so a hand-written literal is required.
            string payloadJson = "{\"seed\":777,"
                + "\"moduleTiers\":[{\"moduleId\":\"industry.logistics_cargo\",\"tier\":1}],"
                + "\"dominance\":[{\"worldId\":\"sys002/pl01\",\"state\":" + (int)DominanceState.Controlled + "}]}";
            var envelope = new SaveEnvelope
            {
                schemaVersion = 3,
                campaignId = "campaign-1",
                revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(),
                payloadJson = payloadJson
            };
            WriteEnvelope(envelope);

            SavePayload loaded = SaveService.Load(TestSlot);

            Assert.IsNotNull(loaded);
            // JsonUtility always default-constructs "navigation" even though
            // the JSON never mentions it — so it is NOT null, but its
            // systemKnowledge list (which a real v4 save is never empty on)
            // is empty. That emptiness, not nullness, is exactly what
            // ExtractionDirector.LoadNow branches on to run
            // NavigationDirector.MigrateLegacySave instead of RestoreFromSave.
            Assert.IsNotNull(loaded.navigation);
            Assert.AreEqual(0, loaded.navigation.systemKnowledge.Count);
            Assert.AreEqual(1, loaded.dominance.Count);
            Assert.AreEqual((int)DominanceState.Controlled, loaded.dominance[0].state);
        }

        [Test]
        public void V4Payload_NavigationRoundTrips_NoLossOrDuplication()
        {
            var payload = new SavePayload { seed = 42 };
            payload.navigation = new NavigationDto
            {
                selectedRouteSystemId = "sys002",
                signalAnalysisTargetSystemId = "sys003",
                signalAnalysisRemainingSeconds = 5.5f,
                flightMode = (int)Nebula.Navigation.FlightMode.CruiseRamp,
                flightModeElapsedSeconds = 3f,
                cruiseSpeedMultiplier = 4.2f,
                headingLockEngaged = true,
                headingX = 0f, headingY = 0f, headingZ = 1f
            };
            payload.navigation.systemKnowledge.Add(new SystemKnowledgeDto { systemId = "sys000", level = (int)Nebula.Navigation.SystemKnowledgeLevel.Visited });
            payload.navigation.systemKnowledge.Add(new SystemKnowledgeDto { systemId = "sys002", level = (int)Nebula.Navigation.SystemKnowledgeLevel.Located });
            payload.navigation.frontierProvenance.Add(new FrontierProvenanceDto { origin = "sys000/pl00", target = "sys002" });

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            Assert.IsNotNull(loaded.navigation);
            Assert.AreEqual(2, loaded.navigation.systemKnowledge.Count);
            Assert.AreEqual("sys002", loaded.navigation.selectedRouteSystemId);
            Assert.AreEqual(5.5f, loaded.navigation.signalAnalysisRemainingSeconds);
            Assert.AreEqual((int)Nebula.Navigation.FlightMode.CruiseRamp, loaded.navigation.flightMode);
            Assert.IsTrue(loaded.navigation.headingLockEngaged);
            Assert.AreEqual(1, loaded.navigation.frontierProvenance.Count);
            Assert.AreEqual("sys000/pl00", loaded.navigation.frontierProvenance[0].origin);
            Assert.AreEqual("sys002", loaded.navigation.frontierProvenance[0].target);

            // Second save/load cycle must not duplicate anything.
            Assert.IsTrue(SaveService.Save(TestSlot, loaded, 2));
            SavePayload loaded2 = SaveService.Load(TestSlot);
            Assert.AreEqual(2, loaded2.navigation.systemKnowledge.Count);
            Assert.AreEqual(1, loaded2.navigation.frontierProvenance.Count);
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
