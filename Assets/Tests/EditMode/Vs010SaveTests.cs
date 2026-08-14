using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the v8 Quick Asset Package / evacuation transaction round-trip at the SaveService layer (VS-010 §7/§10.12).</summary>
    public class Vs010SaveTests
    {
        private const int TestSlot = 94;

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
        public void ActiveQuickPackage_RoundTripsExactly()
        {
            var payload = new SavePayload { seed = 1 };
            payload.quickPackage = new QuickPackageDto
            {
                active = true, transactionId = "tx-abc", planetWorldId = "sector.0.0.0/system.000/planet.00",
                commandMaterialId = "material.metal_alloy", laserMaterialId = "material.biocomposite",
                drillSent = true, commandSent = false, laserSent = false, elapsedSeconds = 2.5f
            };

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.IsNotNull(loaded.quickPackage);
            Assert.IsTrue(loaded.quickPackage.active);
            Assert.AreEqual("tx-abc", loaded.quickPackage.transactionId);
            Assert.AreEqual("sector.0.0.0/system.000/planet.00", loaded.quickPackage.planetWorldId);
            Assert.AreEqual("material.metal_alloy", loaded.quickPackage.commandMaterialId);
            Assert.AreEqual("material.biocomposite", loaded.quickPackage.laserMaterialId);
            Assert.IsTrue(loaded.quickPackage.drillSent);
            Assert.IsFalse(loaded.quickPackage.commandSent);
            Assert.AreEqual(2.5f, loaded.quickPackage.elapsedSeconds);
        }

        [Test]
        public void NoActiveQuickPackage_RoundTripsAsInactive_JsonUtilityNullQuirkSafe()
        {
            // JsonUtility cannot represent a null nested-class field as null — a
            // payload saved with quickPackage left unassigned still round-trips
            // as a non-null, all-default instance, so "active" (not nullness) is
            // the real signal, exactly like ModuleJobDto/HangarJobDto before it.
            var payload = new SavePayload { seed = 1 };
            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            bool looksActive = loaded.quickPackage != null && loaded.quickPackage.active;
            Assert.IsFalse(looksActive);
        }

        [Test]
        public void ActiveEvacuation_RoundTripsExactly_IncludingPendingVehicleList()
        {
            var payload = new SavePayload { seed = 1 };
            payload.evacuation = new EvacuationDto { active = true, planetWorldId = "sector.0.0.0/system.000/planet.01" };
            payload.evacuation.pendingVehicleIds.Add("vehicle-0000");
            payload.evacuation.pendingVehicleIds.Add("vehicle-0001");

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.IsNotNull(loaded.evacuation);
            Assert.IsTrue(loaded.evacuation.active);
            Assert.AreEqual("sector.0.0.0/system.000/planet.01", loaded.evacuation.planetWorldId);
            Assert.AreEqual(2, loaded.evacuation.pendingVehicleIds.Count);
            CollectionAssert.Contains(loaded.evacuation.pendingVehicleIds, "vehicle-0000");
            CollectionAssert.Contains(loaded.evacuation.pendingVehicleIds, "vehicle-0001");
        }

        [Test]
        public void NoActiveEvacuation_RoundTripsAsInactive()
        {
            var payload = new SavePayload { seed = 1 };
            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            bool looksActive = loaded.evacuation != null && loaded.evacuation.active;
            Assert.IsFalse(looksActive);
        }

        [Test]
        public void AbandonedPlanets_LiveInsideSystemDelta_NotAsASeparateTopLevelList()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.abandonedPlanets.Add("sector.0.0.0/system.000/planet.00");
            delta.dominance.Add(new DominanceDto { worldId = "sector.0.0.0/system.000/planet.00", state = (int)DominanceState.Controlled });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            Assert.AreEqual(1, loaded.systemDeltas.Count);
            Assert.AreEqual(1, loaded.systemDeltas[0].abandonedPlanets.Count);
            Assert.AreEqual("sector.0.0.0/system.000/planet.00", loaded.systemDeltas[0].abandonedPlanets[0]);
        }

        [Test]
        public void V7ShapedPayload_LoadsWithSchema7_NoFrontierProvenanceOrQuickPackageOrEvacuationFields()
        {
            // Hand-built v7-shaped envelope (no quickPackage/evacuation/frontierProvenance
            // ever written) must still load cleanly — ExtractionDirector.LoadNow is
            // responsible for the v7->v8 backfill (RebuildFrontierProvenanceFromControlled),
            // not SaveService; this test only proves the schema/JSON layer accepts it.
            var payload = new SavePayload { seed = 7 };
            payload.navigation = new NavigationDto();
            payload.navigation.systemKnowledge.Add(new SystemKnowledgeDto { systemId = "sector.0.0.0/system.000", level = 3 });

            string payloadJson = JsonUtility.ToJson(payload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = 7, campaignId = "campaign-1", revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(), payloadJson = payloadJson
            };
            string json = JsonUtility.ToJson(envelope, true);
            string path = SaveService.SlotFile(TestSlot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(7, schemaVersion);
            Assert.AreEqual(0, loaded.navigation.frontierProvenance.Count, "a v7 payload never wrote provenance — empty here is expected, not evidence of anything");
            bool quickPackageActive = loaded.quickPackage != null && loaded.quickPackage.active;
            bool evacuationActive = loaded.evacuation != null && loaded.evacuation.active;
            Assert.IsFalse(quickPackageActive);
            Assert.IsFalse(evacuationActive);
        }
    }
}
