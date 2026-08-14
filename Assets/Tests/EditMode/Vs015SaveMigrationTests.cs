using System.IO;
using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Progression;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §10: save schema v13, v12-&gt;v13 additive migration safe defaults, and Engineer/Escort mission-XP idempotency via the shared ledger.</summary>
    public class Vs015SaveMigrationTests
    {
        private const int TestSlot = 97;

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
        public void CurrentSchemaIsAtLeast13()
        {
            // VS-016A moved CurrentSchema to 14 -- this test's own concern (v13's DTOs still round-trip)
            // stays valid regardless of later bumps, so it asserts the floor rather than an exact, staleness-prone number.
            Assert.GreaterOrEqual(SaveService.CurrentSchema, 13);
        }

        [Test]
        public void VehicleDto_EngineerTaskAndEscortState_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var vdto = new VehicleDto
            {
                vehicleId = "vehicle-0001", vehicleClass = (int)Hangar.VehicleClass.Engineer, mk = 1,
                shield = 50f, armor = 200f, hull = 300f, stranded = false,
                engineerTask = new EngineerMissionDto
                {
                    active = true, missionId = "engineer-vehicle-0001-0000", systemId = "sys001",
                    targetKind = (int)Hangar.EngineerTargetKind.Structure, targetId = "sys001/pl00/defense.0",
                    workKind = (int)Hangar.EngineerWorkKind.Repair, sourceKind = (int)Hangar.EngineerSourceKind.ShipCargo,
                    chosenMaterialId = "material.metal_alloy", playerManuallyStarted = true,
                    reservedParts = 10, reservedMaterialAmount = 5f, consumedParts = 3, consumedMaterialAmount = 1.5f,
                    phase = (int)Hangar.EngineerPhase.Working, safeSecondsAccumulated = 8f, xpAwarded = false,
                    plannedRestoredHp = 40f, actualRestoredHpSoFar = 12f
                }
            };
            payload.vehicles.Add(vdto);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.AreEqual(1, loaded.vehicles.Count);
            Assert.IsTrue(loaded.vehicles[0].engineerTask.active);
            Assert.AreEqual("engineer-vehicle-0001-0000", loaded.vehicles[0].engineerTask.missionId);
            Assert.AreEqual(40f, loaded.vehicles[0].engineerTask.plannedRestoredHp);
            Assert.AreEqual(12f, loaded.vehicles[0].engineerTask.actualRestoredHpSoFar);
            Assert.IsFalse(loaded.vehicles[0].escortState.active); // never assigned -> correct default
        }

        [Test]
        public void StructureDto_MaintenanceReserve_RoundTripsExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var sdto = new StructureDto { worldId = "sys001/pl00", nodeId = "command", type = 0, tier = 2 };
            sdto.maintenanceReserve = new MaintenanceReserveDto { policy = (int)MaintenancePolicy.Emergency, activeEngineerTargetId = "target-a" };
            sdto.maintenanceReserve.items.Add(new ItemAmountDto { itemId = ProcessingCatalog.BasicPartId, amount = 20f });
            var delta = new SystemDelta { systemId = "sys001" };
            delta.structures.Add(sdto);
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);

            StructureDto restored = loaded.systemDeltas[0].structures[0];
            Assert.IsNotNull(restored.maintenanceReserve);
            Assert.AreEqual((int)MaintenancePolicy.Emergency, restored.maintenanceReserve.policy);
            Assert.AreEqual("target-a", restored.maintenanceReserve.activeEngineerTargetId);
            Assert.AreEqual(20f, restored.maintenanceReserve.items[0].amount);
        }

        [Test]
        public void PreV13Vehicle_SafeDefaults_NoEngineerOrEscortTask()
        {
            // A pre-v13 VehicleDto never wrote engineerTask/escortState -- JsonUtility still produces
            // non-null default (active=false) instances, which is the correct "no task" signal.
            var dto = new VehicleDto { vehicleId = "v1" };
            Assert.IsFalse(dto.engineerTask?.active ?? false);
            Assert.IsFalse(dto.escortState?.active ?? false);
            Assert.AreEqual(0f, dto.shield); Assert.AreEqual(0f, dto.armor); Assert.AreEqual(0f, dto.hull);
            Assert.IsFalse(dto.stranded);
        }

        [Test]
        public void PreV13Structure_SafeDefault_EmptyManualReserveNoTarget()
        {
            var dto = new StructureDto();
            Assert.IsNull(dto.maintenanceReserve); // Command structures build a fresh reserve at restore time from their own tier
        }

        [Test]
        public void PreV13SystemDelta_SafeDefault_NoInterventionGroup()
        {
            var delta = new SystemDelta();
            Assert.IsTrue(delta.interventionGroup == null || delta.interventionGroup.memberVehicleIds.Count == 0);
        }

        [Test]
        public void EngineerMissionXp_IsAwardedAtMostOncePerMissionId()
        {
            ProgressionService.Reset();
            string eventId = ProgressionEvents.EngineerMission("engineer-vehicle-0001-0000");
            AwardResult first = ProgressionService.Award(eventId, ProgressionEvents.EngineerMissionXp);
            AwardResult second = ProgressionService.Award(eventId, ProgressionEvents.EngineerMissionXp);
            Assert.IsTrue(first.Awarded); Assert.AreEqual(300, first.Xp);
            Assert.IsFalse(second.Awarded);
        }

        [Test]
        public void EscortMissionXp_IsAwardedAtMostOncePerMissionId()
        {
            ProgressionService.Reset();
            string eventId = ProgressionEvents.EscortMission("escort-vehicle-0002-0000");
            AwardResult first = ProgressionService.Award(eventId, ProgressionEvents.EscortMissionXp);
            AwardResult second = ProgressionService.Award(eventId, ProgressionEvents.EscortMissionXp);
            Assert.IsTrue(first.Awarded); Assert.AreEqual(300, first.Xp);
            Assert.IsFalse(second.Awarded);
        }

        [Test]
        public void EngineerAndEscortMissionXp_AreExactly300Each()
        {
            Assert.AreEqual(300, ProgressionEvents.EngineerMissionXp);
            Assert.AreEqual(300, ProgressionEvents.EscortMissionXp);
        }

        [Test]
        public void Restore_SeedsLedgerWithoutReAwarding()
        {
            ProgressionService.Reset();
            string eventId = ProgressionEvents.EngineerMission("m1");
            var ledger = new System.Collections.Generic.List<string> { eventId };
            ProgressionService.Restore(0, 0, ledger);
            AwardResult result = ProgressionService.Award(eventId, ProgressionEvents.EngineerMissionXp);
            Assert.IsFalse(result.Awarded); // already in the restored ledger -- save/load never duplicates XP
        }
    }
}
