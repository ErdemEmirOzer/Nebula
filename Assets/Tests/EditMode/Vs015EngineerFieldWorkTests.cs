using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §3: live per-tick structure repair — real HP applied via the shared RepairAllocator/Durability.RepairExact path, resource consumed proportionally (never more than reserved), field cap never exceeded, and partial-tick consumption stays exact.</summary>
    public class Vs015EngineerFieldWorkTests
    {
        private static EngineerMissionState NewTask(float plannedHp, int reservedParts, float reservedMaterial)
        {
            var task = new EngineerMissionState("m1", "vehicle-1", "sys001", EngineerTargetKind.Structure, "sys001/pl00/defense.0",
                EngineerWorkKind.Repair, EngineerSourceKind.ShipCargo, "material.metal_alloy", true, reservedParts, reservedMaterial);
            task.SetPlannedRestoredHp(plannedHp);
            return task;
        }

        [Test]
        public void TickStructureRepair_NeverExceedsFieldCap()
        {
            // Armor/Hull both start below the 60% cap; a huge repair rate must still stop exactly at the cap.
            var durability = new Durability(0f, 100f, 100f, startFraction: 0.5f); // 50/100 each
            var task = NewTask(plannedHp: 20f, reservedParts: 10, reservedMaterial: 5f); // (60-50)*2 = 20
            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 100f);
            cargo.Add("material.metal_alloy", 100f);
            cargo.TryReserveAll("owner", new System.Collections.Generic.List<(string, float)> { (ProcessingCatalog.BasicPartId, 10f), ("material.metal_alloy", 5f) });

            for (int i = 0; i < 100; i++) EngineerFieldWork.TickStructureRepair(durability, task, ratePerSecond: 1000f, capFraction: 0.6f, cargo, "owner", dt: 1f);

            Assert.AreEqual(60f, durability.Armor, 0.01f);
            Assert.AreEqual(60f, durability.Hull, 0.01f);
        }

        [Test]
        public void TickStructureRepair_ConsumesResourceProportionalToActualHealing_NeverMoreThanReserved()
        {
            var durability = new Durability(0f, 100f, 100f, startFraction: 0.5f);
            var task = NewTask(plannedHp: 20f, reservedParts: 10, reservedMaterial: 5f);
            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 100f);
            cargo.Add("material.metal_alloy", 100f);
            cargo.TryReserveAll("owner", new System.Collections.Generic.List<(string, float)> { (ProcessingCatalog.BasicPartId, 10f), ("material.metal_alloy", 5f) });

            // Heal exactly halfway (10 of 20 planned HP) via a bounded rate, then check consumption is ~half the reservation.
            EngineerFieldWork.TickStructureRepair(durability, task, ratePerSecond: 10f, capFraction: 0.6f, cargo, "owner", dt: 1f);
            Assert.AreEqual(10f, task.ActualRestoredHpSoFar, 0.01f);
            Assert.AreEqual(5, task.ConsumedParts); // ceil(10 * 0.5) = 5
            Assert.LessOrEqual(task.ConsumedParts, task.ReservedParts);
            Assert.LessOrEqual(task.ConsumedMaterialAmount, task.ReservedMaterialAmount);
        }

        [Test]
        public void TickStructureRepair_ResourceConservation_ConsumedPlusUnusedEqualsReserved()
        {
            var durability = new Durability(0f, 100f, 100f, startFraction: 0.5f);
            var task = NewTask(plannedHp: 20f, reservedParts: 10, reservedMaterial: 5f);
            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 100f);
            cargo.Add("material.metal_alloy", 100f);
            cargo.TryReserveAll("owner", new System.Collections.Generic.List<(string, float)> { (ProcessingCatalog.BasicPartId, 10f), ("material.metal_alloy", 5f) });

            bool done = false;
            for (int i = 0; i < 50 && !done; i++) done = EngineerFieldWork.TickStructureRepair(durability, task, 5f, 0.6f, cargo, "owner", 1f);

            Assert.IsTrue(done);
            Assert.AreEqual(task.ReservedParts, task.ConsumedParts + task.UnusedParts);
            Assert.AreEqual(task.ReservedMaterialAmount, task.ConsumedMaterialAmount + task.UnusedMaterialAmount, 0.01f);
        }

        [Test]
        public void TickStructureRepair_AlreadyAtCap_IsANoOpAndReturnsTrue()
        {
            var durability = new Durability(0f, 100f, 100f, startFraction: 0.6f); // already exactly at 60% cap
            var task = NewTask(plannedHp: 0f, reservedParts: 0, reservedMaterial: 0f);
            var cargo = new LogisticsCargo(100f);
            bool done = EngineerFieldWork.TickStructureRepair(durability, task, 100f, 0.6f, cargo, "owner", 1f);
            Assert.IsTrue(done);
            Assert.AreEqual(60f, durability.Armor, 0.01f);
        }

        [Test]
        public void TickStructureRepair_NullDurability_IsSafeNoOp()
        {
            var task = NewTask(0f, 0, 0f);
            bool done = EngineerFieldWork.TickStructureRepair(null, task, 10f, 0.6f, new LogisticsCargo(10f), "owner", 1f);
            Assert.IsTrue(done);
        }
    }
}
