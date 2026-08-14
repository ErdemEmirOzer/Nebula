using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-015 §3: live per-tick application of an Engineer's Repair/Recover
    /// work — heals via the existing <see cref="RepairAllocator"/>/
    /// <see cref="Durability.RepairExact"/> path (never a second cascade),
    /// consumes resources proportionally to actual HP restored (mirrors
    /// ShipRepairService.ConsumeProportional's "never charge for elapsed
    /// time alone" discipline), against the mission's reservation taken
    /// atomically at Reserve time. Parts consume/return 1:1 with the
    /// material; both are driven off the SAME progress fraction.
    /// </summary>
    public static class EngineerFieldWork
    {
        /// <summary>Repairs a structure/drill/support-structure Durability toward its field cap. Returns true once the target is at/above cap or no further healing occurred (mission's Working phase should complete).</summary>
        public static bool TickStructureRepair(Durability durability, EngineerMissionState task, float ratePerSecond,
            float capFraction, LogisticsCargo sourceCargo, string reservationOwnerId, float dt)
        {
            if (durability == null) return true;
            float armorCap = durability.ArmorMax * capFraction;
            float hullCap = durability.HullMax * capFraction;
            bool atCap = durability.Armor >= armorCap - 0.001f && durability.Hull >= hullCap - 0.001f;
            if (atCap || task.PlannedRestoredHp <= 0.0001f) return true;

            float wantHp = Mathf.Max(0f, ratePerSecond) * dt;
            var (toArmor, toHull) = RepairAllocator.Allocate(wantHp, durability.Armor, durability.ArmorMax, armorCap, durability.Hull, durability.HullMax, hullCap);
            float applied = toArmor + toHull;
            if (applied <= 0f) return true;

            durability.RepairExact(toArmor, toHull);
            task.RecordProgress(applied);
            ConsumeProportional(task, sourceCargo, reservationOwnerId);

            return durability.Armor >= armorCap - 0.001f && durability.Hull >= hullCap - 0.001f;
        }

        /// <summary>
        /// Recovers a Disabled vehicle over the Engineer's fixed recovery duration
        /// (never rate-based) — <see cref="EngineerMissionState.PlannedRestoredHp"/>
        /// is repurposed here as "required seconds" and ActualRestoredHpSoFar as
        /// "elapsed seconds", since recovery has no HP-based progress of its own
        /// until the very end. Resource consumption still tracks the same time
        /// fraction. Returns true once recovery completes (target restored to the
        /// recovery-complete hull fraction and released from Stranded).
        /// </summary>
        public static bool TickVehicleRecovery(VehicleAgent target, EngineerMissionState task, float dt, LogisticsCargo sourceCargo, string reservationOwnerId)
        {
            if (target == null || !target.IsRecoverable) return true;
            if (task.PlannedRestoredHp <= 0f) { target.CompleteRecovery(EngineerRepairCatalog.RecoveryCompleteHullFraction); return true; }

            task.RecordProgress(dt);
            ConsumeProportional(task, sourceCargo, reservationOwnerId);
            if (task.ActualRestoredHpSoFar < task.PlannedRestoredHp) return false;

            target.CompleteRecovery(EngineerRepairCatalog.RecoveryCompleteHullFraction);
            return true;
        }

        /// <summary>Repairs a target VehicleAgent's own Durability toward the 60%/100% cap (combat/non-combat), same cost/consumption discipline as a structure.</summary>
        public static bool TickVehicleRepair(VehicleAgent target, EngineerMissionState task, float ratePerSecond,
            float capFraction, LogisticsCargo sourceCargo, string reservationOwnerId, float dt)
        {
            if (target == null || target.DurabilityRef == null) return true;
            Durability durability = target.DurabilityRef;
            float armorCap = durability.ArmorMax * capFraction;
            float hullCap = durability.HullMax * capFraction;
            bool atCap = durability.Armor >= armorCap - 0.001f && durability.Hull >= hullCap - 0.001f;
            if (atCap || task.PlannedRestoredHp <= 0.0001f) return true;

            float wantHp = Mathf.Max(0f, ratePerSecond) * dt;
            var (toArmor, toHull) = RepairAllocator.Allocate(wantHp, durability.Armor, durability.ArmorMax, armorCap, durability.Hull, durability.HullMax, hullCap);
            float applied = toArmor + toHull;
            if (applied <= 0f) return true;

            durability.RepairExact(toArmor, toHull);
            task.RecordProgress(applied);
            ConsumeProportional(task, sourceCargo, reservationOwnerId);

            return durability.Armor >= armorCap - 0.001f && durability.Hull >= hullCap - 0.001f;
        }

        private static void ConsumeProportional(EngineerMissionState task, LogisticsCargo cargo, string ownerId)
        {
            if (cargo == null || task.PlannedRestoredHp <= 0f) return;
            float progress = Mathf.Clamp01(task.ActualRestoredHpSoFar / task.PlannedRestoredHp);

            int targetParts = Mathf.CeilToInt(task.ReservedParts * progress);
            int deltaParts = Mathf.Max(0, targetParts - task.ConsumedParts);
            float targetMaterial = task.ReservedMaterialAmount * progress;
            float deltaMaterial = Mathf.Max(0f, targetMaterial - task.ConsumedMaterialAmount);

            if (deltaParts > 0) cargo.ConsumePartialReservation(ownerId, ProcessingCatalog.BasicPartId, deltaParts);
            if (deltaMaterial > 0f && !string.IsNullOrEmpty(task.ChosenMaterialId)) cargo.ConsumePartialReservation(ownerId, task.ChosenMaterialId, deltaMaterial);
            task.RecordConsumption(deltaParts, deltaMaterial);
        }
    }
}
