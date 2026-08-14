using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure physical repair transaction domain (VS-013.1 §5/§7): allocator priority, catalog rate/cost, job progress/pause, and the orchestrating service's reserve/consume/refund contract.</summary>
    public class RepairAllocatorTests
    {
        [Test]
        public void LowerFractionLayer_IsHealedFirst()
        {
            // Armor at 50%, Hull at 90% -- Armor must be healed first.
            var (toArmor, toHull) = RepairAllocator.Allocate(healAmount: 10f,
                armor: 50f, armorMax: 100f, armorCap: 100f,
                hull: 90f, hullMax: 100f, hullCap: 100f);
            Assert.AreEqual(10f, toArmor, 0.001f);
            Assert.AreEqual(0f, toHull);
        }

        [Test]
        public void Overflow_SpillsIntoTheOtherLayer_OnceThePriorityLayerRunsOutOfRoom()
        {
            // Hull has the LOWER fraction (50% < 90%) so it's healed first, but its cap only
            // leaves 10 room -- the remaining 5 of the 15 healAmount must spill into Armor.
            var (toArmor, toHull) = RepairAllocator.Allocate(healAmount: 15f,
                armor: 90f, armorMax: 100f, armorCap: 100f,   // 90% frac, 10 room
                hull: 50f, hullMax: 100f, hullCap: 60f);      // 50% frac (lower -> priority), only 10 room
            Assert.AreEqual(10f, toHull, 0.001f, "the lower-fraction layer is healed first, up to its own room");
            Assert.AreEqual(5f, toArmor, 0.001f, "the remainder spills into the other layer");
        }

        [Test]
        public void Cap_LimitsRoomEvenWhenUnderMax()
        {
            var (toArmor, toHull) = RepairAllocator.Allocate(healAmount: 10f,
                armor: 55f, armorMax: 100f, armorCap: 60f, // field cap -- only 5 room even though max is 100
                hull: 55f, hullMax: 100f, hullCap: 60f);
            Assert.AreEqual(5f, toArmor, 0.001f);
            Assert.AreEqual(5f, toHull, 0.001f);
        }

        [Test]
        public void BothAtCap_ProducesZeroApplication()
        {
            var (toArmor, toHull) = RepairAllocator.Allocate(10f, 60f, 100f, 60f, 60f, 100f, 60f);
            Assert.AreEqual(0f, toArmor);
            Assert.AreEqual(0f, toHull);
        }
    }

    public class RepairCatalogTests
    {
        [Test]
        public void PackageDuration_IsExactlyTenSeconds_IndependentOfCapacity()
        {
            // 1%/s rate, 10% package -> always exactly 10s regardless of the ship's actual max.
            Assert.AreEqual(10f, RepairCatalog.PackageDurationSeconds, 0.001f);
        }

        [Test]
        public void PackageCost_FormIAndII_MatchSpecExactly()
        {
            var formI = RepairCatalog.PackageCost(1);
            Assert.AreEqual(1, formI.Count);
            Assert.AreEqual((ProcessingCatalog.BasicPartId, 2f), formI[0]);

            var formII = RepairCatalog.PackageCost(2);
            Assert.AreEqual(2, formII.Count);
            Assert.AreEqual((ProcessingCatalog.BasicPartId, 3f), formII[0]);
            Assert.AreEqual(("material.metal_alloy", 1f), formII[1]);
        }

        [Test]
        public void FieldAndSafeZoneCaps_MatchSpec()
        {
            Assert.AreEqual(0.60f, RepairCatalog.FieldCapFraction, 0.001f);
            Assert.AreEqual(1.00f, RepairCatalog.SafeZoneCapFraction, 0.001f);
        }

        [Test]
        public void RefundFraction_ReusesProjectStandard75Percent()
        {
            Assert.AreEqual(SurfaceStructureCatalog.RefundFraction, RepairCatalog.RefundFraction);
        }

        [Test]
        public void RepairSafeRadius_Matches1500UnitsSpec()
        {
            Assert.AreEqual(1500f, RepairCatalog.RepairSafeRadius, 0.001f);
        }

        [Test]
        public void RemainingCost_At25PercentProgress_Reserves75PercentOfFullCost()
        {
            var remaining = RepairCatalog.RemainingCost(1, 0.25f); // Form I: 2 parts
            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual(1.5f, remaining[0].amount, 0.001f);
        }

        [Test]
        public void RemainingCost_At50PercentProgress_Reserves50PercentOfFullCost()
        {
            var remaining = RepairCatalog.RemainingCost(1, 0.5f);
            Assert.AreEqual(1f, remaining[0].amount, 0.001f);
        }

        [Test]
        public void RemainingCost_At99PercentProgress_ReservesOnly1PercentOfFullCost()
        {
            var remaining = RepairCatalog.RemainingCost(1, 0.99f);
            Assert.AreEqual(0.02f, remaining[0].amount, 0.001f);
        }

        [Test]
        public void RemainingCost_AtZeroProgress_ReservesTheFullCost()
        {
            var full = RepairCatalog.PackageCost(2);
            var remaining = RepairCatalog.RemainingCost(2, 0f);
            Assert.AreEqual(full[0].amount, remaining[0].amount, 0.001f);
            Assert.AreEqual(full[1].amount, remaining[1].amount, 0.001f);
        }
    }

    public class RepairJobStateTests
    {
        [Test]
        public void StartPackage_SetsActiveAndResetsProgress()
        {
            var job = new RepairJobState();
            job.StartPackage(hullForm: 2);
            Assert.IsTrue(job.Active);
            Assert.IsFalse(job.Paused);
            Assert.AreEqual(2, job.HullFormForCurrentPackage);
            Assert.AreEqual(0f, job.PackageProgress01);
        }

        [Test]
        public void AdvanceByAppliedHp_TracksRealHealingNotElapsedTime()
        {
            var job = new RepairJobState();
            job.StartPackage(1);
            // combinedMax 1600 (Form I: Armor 600 + Hull 1000), package span = 10% = 160 HP.
            float delta = job.AdvanceByAppliedHp(actuallyAppliedHp: 80f, combinedMaxCapacity: 1600f);
            Assert.AreEqual(0.5f, delta, 0.001f);
            Assert.AreEqual(0.5f, job.PackageProgress01, 0.001f);
        }

        [Test]
        public void AdvanceByAppliedHp_ZeroAppliedHp_NeverAdvancesProgress()
        {
            var job = new RepairJobState();
            job.StartPackage(1);
            float delta = job.AdvanceByAppliedHp(0f, 1600f);
            Assert.AreEqual(0f, delta);
            Assert.AreEqual(0f, job.PackageProgress01);
        }

        [Test]
        public void PauseResume_NeverResetsProgress()
        {
            var job = new RepairJobState();
            job.StartPackage(1);
            job.AdvanceByAppliedHp(80f, 1600f);
            job.Pause();
            Assert.IsTrue(job.Paused);
            Assert.AreEqual(0.5f, job.PackageProgress01, 0.001f);
            job.Resume();
            Assert.IsFalse(job.Paused);
            Assert.AreEqual(0.5f, job.PackageProgress01, 0.001f);
        }

        [Test]
        public void PackageComplete_TrueOnlyAtFullProgress()
        {
            var job = new RepairJobState();
            job.StartPackage(1);
            job.AdvanceByAppliedHp(160f, 1600f);
            Assert.IsTrue(job.PackageComplete);
        }

        [Test]
        public void Stop_ClearsActiveAndProgress()
        {
            var job = new RepairJobState();
            job.StartPackage(1);
            job.AdvanceByAppliedHp(80f, 1600f);
            job.Stop();
            Assert.IsFalse(job.Active);
            Assert.AreEqual(0f, job.PackageProgress01);
        }

        [Test]
        public void ResetForNextPackage_KeepsActive_ZeroesProgress_UpdatesForm()
        {
            var job = new RepairJobState();
            job.StartPackage(1);
            job.AdvanceByAppliedHp(160f, 1600f);
            job.ResetForNextPackage(2);
            Assert.IsTrue(job.Active);
            Assert.AreEqual(0f, job.PackageProgress01);
            Assert.AreEqual(2, job.HullFormForCurrentPackage);
        }

        [Test]
        public void RestoreFromSave_RoundTripsExactly()
        {
            var job = new RepairJobState();
            job.RestoreFromSave(active: true, paused: true, hullFormForCurrentPackage: 2, packageProgress01: 0.35f);
            Assert.IsTrue(job.Active);
            Assert.IsTrue(job.Paused);
            Assert.AreEqual(2, job.HullFormForCurrentPackage);
            Assert.AreEqual(0.35f, job.PackageProgress01, 0.001f);
        }
    }

    public class ShipRepairServiceTests
    {
        private static LogisticsCargo RichCargo()
        {
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 100f);
            cargo.Add("material.metal_alloy", 50f);
            return cargo;
        }

        [Test]
        public void CheckStart_RejectsInCombat()
        {
            var job = new RepairJobState();
            RepairRejectReason reason = ShipRepairService.CheckStart(job, inCombat: true, emergencyShutdown: false,
                safeSecondsSinceLastDamage: 999f, shipSpeed: 0f, armorAtCap: false, hullAtCap: false, hullForm: 1, RichCargo());
            Assert.AreEqual(RepairRejectReason.InCombat, reason);
        }

        [Test]
        public void CheckStart_RejectsTooSoonAfterDamage()
        {
            var job = new RepairJobState();
            RepairRejectReason reason = ShipRepairService.CheckStart(job, false, false,
                safeSecondsSinceLastDamage: 3f, shipSpeed: 0f, false, false, 1, RichCargo());
            Assert.AreEqual(RepairRejectReason.TooSoonAfterDamage, reason);
        }

        [Test]
        public void CheckStart_RejectsTooFast()
        {
            var job = new RepairJobState();
            RepairRejectReason reason = ShipRepairService.CheckStart(job, false, false, 999f, shipSpeed: 40f, false, false, 1, RichCargo());
            Assert.AreEqual(RepairRejectReason.TooFast, reason);
        }

        [Test]
        public void CheckStart_RejectsAlreadyAtCap()
        {
            var job = new RepairJobState();
            RepairRejectReason reason = ShipRepairService.CheckStart(job, false, false, 999f, 0f, armorAtCap: true, hullAtCap: true, 1, RichCargo());
            Assert.AreEqual(RepairRejectReason.AlreadyAtCap, reason);
        }

        [Test]
        public void CheckStart_RejectsInsufficientResources()
        {
            var job = new RepairJobState();
            var poorCargo = new LogisticsCargo(50f);
            RepairRejectReason reason = ShipRepairService.CheckStart(job, false, false, 999f, 0f, false, false, 1, poorCargo);
            Assert.AreEqual(RepairRejectReason.InsufficientResources, reason);
        }

        [Test]
        public void CheckStart_AllowsWhenEverythingIsFine()
        {
            var job = new RepairJobState();
            RepairRejectReason reason = ShipRepairService.CheckStart(job, false, false, 999f, 10f, false, false, 1, RichCargo());
            Assert.AreEqual(RepairRejectReason.None, reason);
        }

        [Test]
        public void TryStart_AtomicallyReservesThePackageCost()
        {
            var job = new RepairJobState();
            LogisticsCargo cargo = RichCargo();
            Assert.IsTrue(ShipRepairService.TryStart(job, 1, cargo));
            Assert.AreEqual(2f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId));
            Assert.IsTrue(job.Active);
        }

        [Test]
        public void ConsumeProportional_TakesExactlyItsFractionOfTheReservedCost_PermanentlyFromStock()
        {
            var job = new RepairJobState();
            LogisticsCargo cargo = RichCargo();
            ShipRepairService.TryStart(job, 2, cargo); // cost: 3 parts + 1 metal_alloy
            float partsBefore = cargo.GetStored(ProcessingCatalog.BasicPartId);

            ShipRepairService.ConsumeProportional(job, cargo, progressDeltaThisTick: 0.5f);

            Assert.AreEqual(partsBefore - 1.5f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f);
            Assert.AreEqual(1.5f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f, "half the reservation should remain");
        }

        [Test]
        public void Cancel_MidPackage_OnlyResolvesTheStillReservedRemainder_AtTheStandard75PercentRefund()
        {
            // Form1 costs 2 parts. Half-progress permanently consumes 1 (real healing already
            // happened, gone regardless of cancel); the OTHER 1 is still reserved-but-unconsumed
            // when Cancel fires -- that remainder is finalized-then-refunded at 75%, same as any
            // other cancelled job, floor(1*0.75) = 0 back. Net: 100 -> 99 (consumed) -> 98 (the
            // leftover reservation's cancel-finalization) + 0 refund = 98.
            var job = new RepairJobState();
            LogisticsCargo cargo = RichCargo();
            float partsBeforeStart = cargo.GetStored(ProcessingCatalog.BasicPartId);
            ShipRepairService.TryStart(job, 1, cargo); // reserves 2 parts
            ShipRepairService.ConsumeProportional(job, cargo, 0.5f); // permanently consumes 1
            float partsAfterConsume = cargo.GetStored(ProcessingCatalog.BasicPartId);
            Assert.AreEqual(partsBeforeStart - 1f, partsAfterConsume, 0.001f, "half of the 2-part reservation permanently consumed so far");

            ShipRepairService.Cancel(job, cargo);

            Assert.IsFalse(job.Active);
            Assert.AreEqual(partsBeforeStart - 2f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f,
                "the already-consumed part is never refunded (real healing happened); the last still-reserved part is removed and its 75% refund floors to 0");
        }

        [Test]
        public void Cancel_WithACleanlyDivisibleRemainder_RefundsExactly75Percent()
        {
            var job = new RepairJobState();
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 100f);
            cargo.Add("material.metal_alloy", 50f);
            ShipRepairService.TryStart(job, 2, cargo); // cost: 3 parts + 1 metal_alloy, nothing consumed yet
            float partsBefore = cargo.GetStored(ProcessingCatalog.BasicPartId);

            ShipRepairService.Cancel(job, cargo);

            // Full 3-part reservation refunded at 75% -> floor(3*0.75) = 2 back.
            Assert.AreEqual(partsBefore - 3f + 2f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f);
        }

        [Test]
        public void CompletePackageAndTryContinue_StopsJob_WhenBothLayersAlreadyAtCap()
        {
            var job = new RepairJobState();
            LogisticsCargo cargo = RichCargo();
            ShipRepairService.TryStart(job, 1, cargo);
            job.AdvanceByAppliedHp(160f, 1600f); // completes the package

            bool continued = ShipRepairService.CompletePackageAndTryContinue(job, 1, cargo, armorAtCap: true, hullAtCap: true);

            Assert.IsFalse(continued);
            Assert.IsFalse(job.Active, "nothing left to accomplish -- the job must stop, not silently reserve another package for zero gain");
        }

        [Test]
        public void CompletePackageAndTryContinue_ReservesExactlyTheNextPackage_NeverStackedWithTheDrainedOne()
        {
            var job = new RepairJobState();
            LogisticsCargo cargo = RichCargo();
            ShipRepairService.TryStart(job, 1, cargo); // reserves 2 parts
            // Mirror the real ShipCombat.TickRepair flow: progress and consumption always move
            // together, tick by tick, so by the time PackageComplete is true the reservation has
            // already been drained to 0 (never left over for the next reservation to stack onto).
            for (int i = 0; i < 10; i++)
            {
                float delta = job.AdvanceByAppliedHp(16f, 1600f); // 10 * 16 = 160 = full package span
                ShipRepairService.ConsumeProportional(job, cargo, delta);
            }
            Assert.IsTrue(job.PackageComplete);
            Assert.AreEqual(0f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f, "sanity: the first package's reservation must be fully drained by now");

            bool continued = ShipRepairService.CompletePackageAndTryContinue(job, 1, cargo, armorAtCap: false, hullAtCap: false);

            Assert.IsTrue(continued);
            Assert.IsTrue(job.Active);
            Assert.AreEqual(0f, job.PackageProgress01);
            Assert.AreEqual(2f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f, "exactly one fresh package's cost, never stacked with the already-drained previous one");
        }
    }

    /// <summary>VS-013.2 §3 EditMode tests: restoring a saved active repair job must reserve ONLY the remaining fraction of the current package's cost, never the full package again (the phantom-reservation fix).</summary>
    public class ShipRepairServiceRestorePartialReservationTests
    {
        private static LogisticsCargo RichCargo()
        {
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 100f); // Form I package cost: 2 parts
            return cargo;
        }

        [Test]
        public void Restore_At25PercentProgress_ReservesExactly75PercentOfPackageCost()
        {
            LogisticsCargo cargo = RichCargo();
            bool ok = ShipRepairService.RestorePartialReservation(cargo, hullForm: 1, packageProgress01: 0.25f);
            Assert.IsTrue(ok);
            Assert.AreEqual(1.5f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f);
        }

        [Test]
        public void Restore_At50PercentProgress_ReservesExactly50PercentOfPackageCost()
        {
            LogisticsCargo cargo = RichCargo();
            ShipRepairService.RestorePartialReservation(cargo, 1, 0.5f);
            Assert.AreEqual(1f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f);
        }

        [Test]
        public void Restore_At99PercentProgress_ReservesExactly1PercentOfPackageCost()
        {
            LogisticsCargo cargo = RichCargo();
            ShipRepairService.RestorePartialReservation(cargo, 1, 0.99f);
            Assert.AreEqual(0.02f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f);
        }

        [Test]
        public void Restore_At100PercentProgress_ReservesNothing_NoPhantomReservation()
        {
            LogisticsCargo cargo = RichCargo();
            bool ok = ShipRepairService.RestorePartialReservation(cargo, 1, 1f);
            Assert.IsTrue(ok, "a fully-complete package has nothing left to reserve -- this must not be treated as a failure");
            Assert.AreEqual(0f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f);
        }

        [Test]
        public void Restore_ClearsAnyStaleReservation_BeforeReservingTheRemainder()
        {
            LogisticsCargo cargo = RichCargo();
            ShipRepairService.TryStart(new RepairJobState(), 1, cargo); // simulates a stale full-cost reservation left over from the old buggy path
            Assert.AreEqual(2f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId));

            ShipRepairService.RestorePartialReservation(cargo, 1, 0.5f);

            Assert.AreEqual(1f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f,
                "the stale full reservation must be replaced, not added on top of");
        }

        [Test]
        public void Restore_InsufficientRemainingResources_FailsAndReservesNothing()
        {
            var poorCargo = new LogisticsCargo(50f);
            poorCargo.Add(ProcessingCatalog.BasicPartId, 0.5f); // not enough even for the 50% remaining fraction of a 2-part cost
            bool ok = ShipRepairService.RestorePartialReservation(poorCargo, 1, 0.5f);
            Assert.IsFalse(ok);
            Assert.AreEqual(0f, poorCargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId));
        }

        [Test]
        public void Restore_ThenComplete_ThenContinue_NeverAccumulatesReservationAcrossTheChain()
        {
            LogisticsCargo cargo = RichCargo();
            var job = new RepairJobState();
            job.RestoreFromSave(active: true, paused: false, hullFormForCurrentPackage: 1, packageProgress01: 0.5f);
            ShipRepairService.RestorePartialReservation(cargo, 1, 0.5f); // reserves 1 (half of 2)
            Assert.AreEqual(1f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f);

            // Drain the restored remainder exactly (mirrors the real TickRepair flow).
            float delta = job.AdvanceByAppliedHp(80f, 1600f); // half of the 160 HP package span still remains
            ShipRepairService.ConsumeProportional(job, cargo, delta);
            Assert.IsTrue(job.PackageComplete);
            Assert.AreEqual(0f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f, "the restored remainder must be fully drained by completion");

            bool continued = ShipRepairService.CompletePackageAndTryContinue(job, 1, cargo, armorAtCap: false, hullAtCap: false);
            Assert.IsTrue(continued);
            Assert.AreEqual(2f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f,
                "the next package reserves exactly its own full cost -- never stacked with any leftover from the restored one");
        }

        [Test]
        public void Restore_RepeatedSaveLoadCycles_ProduceNoResourceDrift()
        {
            LogisticsCargo cargo = RichCargo();
            float startingStock = cargo.GetStored(ProcessingCatalog.BasicPartId);

            // Simulate 3 successive save/load cycles at the same 50% progress point -- each restore
            // must replace, never add to, the previous reservation, so repeated cycles never drift stock.
            for (int i = 0; i < 3; i++) ShipRepairService.RestorePartialReservation(cargo, 1, 0.5f);

            Assert.AreEqual(startingStock, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f, "physical stock must be untouched by reservation alone");
            Assert.AreEqual(1f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f, "still exactly one package's worth of remaining reservation, not tripled");
        }

        [Test]
        public void Cancel_AfterPartialLoad_Applies75PercentOnlyToTheRemainingReservation()
        {
            LogisticsCargo cargo = RichCargo();
            var job = new RepairJobState();
            job.RestoreFromSave(true, false, 1, 0.5f);
            ShipRepairService.RestorePartialReservation(cargo, 1, 0.5f); // reserves 1 part (half of 2)
            float stockBeforeCancel = cargo.GetStored(ProcessingCatalog.BasicPartId);

            ShipRepairService.Cancel(job, cargo);

            // Full 1-part remaining reservation refunded at 75% -> floor(1*0.75) = 0 back, net -1.
            Assert.AreEqual(stockBeforeCancel - 1f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f);
            Assert.IsFalse(job.Active);
        }

        [Test]
        public void Cancel_CalledTwice_SecondCallIsANoOp()
        {
            LogisticsCargo cargo = RichCargo();
            var job = new RepairJobState();
            job.RestoreFromSave(true, false, 1, 0.5f);
            ShipRepairService.RestorePartialReservation(cargo, 1, 0.5f);

            ShipRepairService.Cancel(job, cargo);
            float stockAfterFirstCancel = cargo.GetStored(ProcessingCatalog.BasicPartId);

            ShipRepairService.Cancel(job, cargo); // job already stopped, reservation already empty
            Assert.AreEqual(stockAfterFirstCancel, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f, "a second cancel must never consume or refund anything further");
        }

        [Test]
        public void FullCycle_StartingStockMinusEndingStock_EqualsExactlyOnePackagesCost()
        {
            LogisticsCargo cargo = RichCargo(); // Form I package: 2 parts total
            float startingStock = cargo.GetStored(ProcessingCatalog.BasicPartId);
            var job = new RepairJobState();

            // Start fresh, progress to 50%, save/load-restore (remaining reservation only), then finish.
            ShipRepairService.TryStart(job, 1, cargo);
            float d1 = job.AdvanceByAppliedHp(80f, 1600f);
            ShipRepairService.ConsumeProportional(job, cargo, d1);

            // Simulate save/load at 50% progress: a fresh restore must reserve only the remainder.
            var restoredJob = new RepairJobState();
            restoredJob.RestoreFromSave(true, false, 1, job.PackageProgress01);
            bool restored = ShipRepairService.RestorePartialReservation(cargo, 1, job.PackageProgress01);
            Assert.IsTrue(restored);

            float d2 = restoredJob.AdvanceByAppliedHp(80f, 1600f);
            ShipRepairService.ConsumeProportional(restoredJob, cargo, d2);
            Assert.IsTrue(restoredJob.PackageComplete);

            Assert.AreEqual(2f, startingStock - cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f, "exactly one package's cost consumed in total -- no free repair, no double consumption");
            Assert.AreEqual(0f, cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.001f);
        }
    }
}
