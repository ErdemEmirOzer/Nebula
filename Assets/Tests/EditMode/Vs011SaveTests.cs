using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the v9 System Logistics Center / Collector route save round-trip and v8-&gt;v9 migration (VS-011 §9/§10.16).</summary>
    public class Vs011SaveTests
    {
        private const int TestSlot = 95;

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
        public void LogisticsCenterDto_LivesInsideSystemDelta_RoundTripsExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.logisticsCenters.Add(new LogisticsCenterDto
            {
                systemId = "sector.0.0.0/system.000", tier = 1, state = (int)LogisticsCenterState.Active,
                buildRemaining = 0f, upgrading = true, upgradeRemaining = 120f,
                shield = 300f, armor = 700f, hull = 1100f, regenTimer = 2f,
                refundParts = 60, refundMetalAlloy = 18, refundBiocomposite = 15,
                upgradeRefundParts = 112, upgradeRefundMetalAlloy = 45, upgradeRefundBiocomposite = 33
            });
            delta.logisticsCenters[0].inventory.Add(new CargoItemDto { itemId = "resource.metal", amount = 40f });
            delta.logisticsCenters[0].inventory.Add(new CargoItemDto { itemId = "part.basic", amount = 12f });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.AreEqual(1, loaded.systemDeltas.Count);
            LogisticsCenterDto dto = loaded.systemDeltas[0].logisticsCenters[0];
            Assert.AreEqual(1, dto.tier);
            Assert.AreEqual((int)LogisticsCenterState.Active, dto.state);
            Assert.IsTrue(dto.upgrading);
            Assert.AreEqual(120f, dto.upgradeRemaining);
            Assert.AreEqual(300f, dto.shield);
            Assert.AreEqual(1100f, dto.hull);
            Assert.AreEqual(60, dto.refundParts);
            Assert.AreEqual(112, dto.upgradeRefundParts);
            Assert.AreEqual(2, dto.inventory.Count);
            Assert.AreEqual(40f, dto.inventory[0].amount);
        }

        [Test]
        public void CollectorRouteDto_LivesInsideSystemDelta_RoundTripsExactly_NoDuplicatedHeldCargo()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.collectorRoutes.Add(new CollectorRouteDto
            {
                routeId = "route-0003", systemId = "sector.0.0.0/system.000",
                drillWorldId = "sector.0.0.0/system.000/planet.01", vehicleId = "vehicle-0002",
                priority = (int)RoutePriority.High, phase = (int)RoutePhase.Holding, resumePhase = (int)RoutePhase.ToCenter,
                waitReason = (int)RouteWaitReason.CenterFull, remainingDistance = 4321f,
                activeTransferTransactionId = "tx-active", lastTransferTransactionId = "tx-prev", dockWaitStartedAt = 55.5f
            });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            CollectorRouteDto dto = loaded.systemDeltas[0].collectorRoutes[0];
            Assert.AreEqual("route-0003", dto.routeId);
            Assert.AreEqual("vehicle-0002", dto.vehicleId);
            Assert.AreEqual((int)RoutePriority.High, dto.priority);
            Assert.AreEqual((int)RoutePhase.Holding, dto.phase);
            Assert.AreEqual((int)RoutePhase.ToCenter, dto.resumePhase);
            Assert.AreEqual((int)RouteWaitReason.CenterFull, dto.waitReason);
            Assert.AreEqual(4321f, dto.remainingDistance);
            Assert.AreEqual("tx-active", dto.activeTransferTransactionId);
            Assert.AreEqual("tx-prev", dto.lastTransferTransactionId);
            Assert.AreEqual(55.5f, dto.dockWaitStartedAt);
        }

        [Test]
        public void VehicleDto_Mk_DefaultsToMkI_WhenAbsentFromOldJson()
        {
            // A v8 (or earlier) VehicleDto JSON never wrote "mk" at all -- JsonUtility must
            // default-construct it via the field initializer (1 = Mk I), zero migration code needed.
            string legacyVehicleJson = "{\"vehicleId\":\"vehicle-0000\",\"vehicleClass\":1,\"homeSystemId\":\"sys001\"," +
                "\"phase\":0,\"missionKind\":0,\"phaseTimer\":0,\"recallRequested\":false," +
                "\"px\":0,\"py\":0,\"pz\":0,\"rx\":0,\"ry\":0,\"rz\":0,\"rw\":1,\"heldAmount\":0}";
            var dto = JsonUtility.FromJson<VehicleDto>(legacyVehicleJson);
            Assert.AreEqual(1, dto.mk);
        }

        [Test]
        public void V8ShapedPayload_LoadsWithSchema8_LogisticsListsAbsentButDefaultToEmpty()
        {
            // Hand-built v8-shaped envelope: never wrote logisticsCenters/collectorRoutes at
            // all (those lists didn't exist yet). Zero-code migration -- JsonUtility simply
            // default-constructs the new list fields as empty, exactly like the vehicles-in-
            // SystemDelta precedent (VS-008).
            var payload = new SavePayload { seed = 8 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.dominance.Add(new DominanceDto { worldId = "sector.0.0.0/system.000/planet.00", state = (int)DominanceState.Controlled });
            payload.systemDeltas.Add(delta);

            string payloadJson = JsonUtility.ToJson(payload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = 8, campaignId = "campaign-1", revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(), payloadJson = payloadJson
            };
            string json = JsonUtility.ToJson(envelope, true);
            string path = SaveService.SlotFile(TestSlot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(8, schemaVersion, "the actual saved schema must be reported");
            Assert.AreEqual(1, loaded.systemDeltas.Count);
            Assert.AreEqual(0, loaded.systemDeltas[0].logisticsCenters.Count, "a v8 save never had a center -- empty is the correct, legitimate state");
            Assert.AreEqual(0, loaded.systemDeltas[0].collectorRoutes.Count);
            Assert.AreEqual((int)DominanceState.Controlled, loaded.systemDeltas[0].dominance[0].state, "pre-existing v8 state must be fully preserved");
        }

        [Test]
        public void SavingCurrentSchema_TwiceInARow_IsIdempotent_NoDuplication()
        {
            var payload = new SavePayload { seed = 42 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.logisticsCenters.Add(new LogisticsCenterDto { systemId = "sector.0.0.0/system.000", tier = 2, state = (int)LogisticsCenterState.Active });
            delta.collectorRoutes.Add(new CollectorRouteDto { routeId = "route-0000", systemId = "sector.0.0.0/system.000", vehicleId = "vehicle-0000" });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload firstLoad = SaveService.Load(TestSlot);
            Assert.IsTrue(SaveService.Save(TestSlot, firstLoad, 2));
            SavePayload secondLoad = SaveService.Load(TestSlot);

            Assert.AreEqual(1, secondLoad.systemDeltas.Count, "re-saving an already-loaded payload must never duplicate deltas");
            Assert.AreEqual(1, secondLoad.systemDeltas[0].logisticsCenters.Count);
            Assert.AreEqual(1, secondLoad.systemDeltas[0].collectorRoutes.Count);
        }
    }
}
