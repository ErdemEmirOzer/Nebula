using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Poi;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the v10 POI save round-trip and v9-&gt;v10 migration (VS-012 §9/§10.9).</summary>
    public class Vs012SaveTests
    {
        private const int TestSlot = 96;

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
        public void CurrentSchema_Is12()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
        }

        [Test]
        public void PoiDto_LivesInsideSystemDelta_RoundTripsExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.001" };
            var dto = new PoiDto
            {
                poiId = "sector.0.0.0/system.001/poi.00", level = (int)PoiKnowledgeLevel.Active,
                locateElapsed = 12f, identifyElapsed = 15f, operationElapsed = 22.5f,
                pauseReason = (int)PoiPauseReason.OutOfRange, assignedVehicleId = "vehicle-0003",
                anomalyXpAwarded = true, wreckXpAwarded = false,
                facilityChoice = (int)FacilityChoice.SensorRelay,
                facilityOutputBasicRemaining = 40f, facilityOutputSpecialRemaining = 12f,
                facilityOutputSpecialItemId = "material.metal_alloy",
                facilityXpAwarded = true, facilitySignalApplied = true
            };
            dto.salvageNodes.Add(new PoiSalvageNodeDto { itemId = "part.basic", total = 20f, remaining = 8f });
            dto.salvageNodes.Add(new PoiSalvageNodeDto { itemId = "resource.metal", total = 10f, remaining = 10f });
            delta.pois.Add(dto);
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.AreEqual(1, loaded.systemDeltas.Count);
            PoiDto round = loaded.systemDeltas[0].pois[0];
            Assert.AreEqual("sector.0.0.0/system.001/poi.00", round.poiId);
            Assert.AreEqual((int)PoiKnowledgeLevel.Active, round.level);
            Assert.AreEqual(12f, round.locateElapsed);
            Assert.AreEqual(22.5f, round.operationElapsed);
            Assert.AreEqual("vehicle-0003", round.assignedVehicleId);
            Assert.IsTrue(round.anomalyXpAwarded);
            Assert.AreEqual((int)FacilityChoice.SensorRelay, round.facilityChoice);
            Assert.AreEqual(12f, round.facilityOutputSpecialRemaining);
            Assert.AreEqual("material.metal_alloy", round.facilityOutputSpecialItemId);
            Assert.IsTrue(round.facilitySignalApplied);
            Assert.AreEqual(2, round.salvageNodes.Count);
            Assert.AreEqual(8f, round.salvageNodes[0].remaining);
            Assert.AreEqual(10f, round.salvageNodes[1].total);
        }

        [Test]
        public void NavigationDto_OneShotSignalOriginsAndPoiVisitOrder_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            payload.navigation = new NavigationDto();
            payload.navigation.oneShotSignalOrigins.Add("sector.0.0.0/system.001/poi.00");
            payload.navigation.oneShotSignalOrigins.Add("sector.0.0.0/system.004/poi.01");
            payload.navigation.poiVisitOrder.Add("sector.0.0.0/system.001");
            payload.navigation.poiVisitOrder.Add("sector.0.0.0/system.002");
            payload.navigation.poiVisitOrder.Add("sector.0.0.0/system.003");

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            CollectionAssert.AreEqual(payload.navigation.oneShotSignalOrigins, loaded.navigation.oneShotSignalOrigins);
            CollectionAssert.AreEqual(payload.navigation.poiVisitOrder, loaded.navigation.poiVisitOrder);
        }

        [Test]
        public void V9ShapedPayload_LoadsWithSchema9_PoiListsAndNavFieldsAbsentButDefaultToEmpty()
        {
            // Hand-built v9-shaped envelope: never wrote pois/oneShotSignalOrigins/poiVisitOrder
            // at all (those fields didn't exist yet). Zero-code migration -- JsonUtility simply
            // default-constructs the new fields as empty lists, exactly like the v8-shaped
            // logisticsCenters/collectorRoutes precedent (VS-011).
            var payload = new SavePayload { seed = 9 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.logisticsCenters.Add(new LogisticsCenterDto { systemId = "sector.0.0.0/system.000", tier = 1 });
            payload.systemDeltas.Add(delta);
            payload.navigation = new NavigationDto();
            payload.navigation.systemKnowledge.Add(new SystemKnowledgeDto { systemId = "sector.0.0.0/system.000", level = 3 });

            string payloadJson = JsonUtility.ToJson(payload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = 9, campaignId = "campaign-1", revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(), payloadJson = payloadJson
            };
            string json = JsonUtility.ToJson(envelope, true);
            string path = SaveService.SlotFile(TestSlot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(9, schemaVersion, "the actual saved schema must be reported");
            Assert.AreEqual(1, loaded.systemDeltas.Count);
            Assert.AreEqual(0, loaded.systemDeltas[0].pois.Count, "a v9 save never had POI state -- empty is the correct, legitimate state");
            Assert.AreEqual(0, loaded.navigation.oneShotSignalOrigins.Count);
            Assert.AreEqual(0, loaded.navigation.poiVisitOrder.Count);
            Assert.AreEqual(1, loaded.systemDeltas[0].logisticsCenters.Count, "pre-existing v9 state must be fully preserved");
            Assert.AreEqual(3, loaded.navigation.systemKnowledge[0].level);
        }

        [Test]
        public void SavingCurrentSchema_TwiceInARow_WithPois_IsIdempotent_NoDuplication()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.pois.Add(new PoiDto { poiId = "sector.0.0.0/system.000/poi.00", level = (int)PoiKnowledgeLevel.Signal });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            Assert.IsTrue(SaveService.Save(TestSlot, payload, 2));
            SavePayload loaded = SaveService.Load(TestSlot);

            Assert.AreEqual(1, loaded.systemDeltas.Count);
            Assert.AreEqual(1, loaded.systemDeltas[0].pois.Count);
        }
    }
}
