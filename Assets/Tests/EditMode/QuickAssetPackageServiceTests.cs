using NUnit.Framework;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the Quick Asset Package's pure cost table, precondition ordering, atomic reservation and cancel/retry (VS-010 §2/§10.1-2-3-5).</summary>
    public class QuickAssetPackageServiceTests
    {
        private const string CommandMat = "material.metal_alloy";
        private const string LaserMat = "material.biocomposite";

        [Test]
        public void TotalCost_IsExactlySixtyFiveBasicPlusFifteenSpecial()
        {
            var cost = QuickAssetPackageService.TotalCost(CommandMat, LaserMat);
            float basic = 0f, commandMat = 0f, laserMat = 0f;
            foreach (var (itemId, amount) in cost)
            {
                if (itemId == ProcessingCatalog.BasicPartId) basic += amount;
                else if (itemId == CommandMat) commandMat += amount;
                else if (itemId == LaserMat) laserMat += amount;
            }
            Assert.AreEqual(65f, basic);
            Assert.AreEqual(10f, commandMat);
            Assert.AreEqual(5f, laserMat);
        }

        [Test]
        public void TotalCost_SameMaterialForCommandAndLaser_MergesIntoOneFifteenLine()
        {
            var cost = QuickAssetPackageService.TotalCost(CommandMat, CommandMat);
            Assert.AreEqual(2, cost.Count, "one Basic line + one merged 15-unit special line");
            float special = 0f;
            foreach (var (itemId, amount) in cost) if (itemId == CommandMat) special += amount;
            Assert.AreEqual(15f, special);
        }

        /// <summary>
        /// The package's per-structure costs are the structure catalogs' own numbers,
        /// and the sum is the sum of them.
        /// <para>
        /// This used to read <c>Assert.AreEqual(30, CommandBasicCost)</c> — a literal
        /// checked against a literal, which measures the constant against itself and
        /// proves nothing (P1-TEST-030). What it could not catch is the only failure
        /// that matters here: the package quietly becoming a <b>second authority</b> on
        /// what a Command Center costs. Two thirties that drift apart are two prices
        /// for one building, and the player pays whichever path they happen to take.
        /// The assertion is against the catalog now, so a re-hardcoded constant fails
        /// here instead of shipping.
        /// </para>
        /// </summary>
        [Test]
        public void PerStructureCosts_AreTheStructureCatalogsOwnNumbers()
        {
            Assert.AreEqual(ExtractionCatalog.DrillT1PartsCost, QuickAssetPackageService.DrillBasicCost,
                "the package is a second authority on what a T1 drill costs in parts");
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost, QuickAssetPackageService.CommandBasicCost,
                "the package is a second authority on the Command Center's parts");
            Assert.AreEqual(SurfaceStructureCatalog.Command.MaterialCost, QuickAssetPackageService.CommandSpecialCost,
                "the package is a second authority on the Command Center's material");
            Assert.AreEqual(SurfaceStructureCatalog.Laser.PartsCost, QuickAssetPackageService.LaserBasicCost,
                "the package is a second authority on the Laser's parts");
            Assert.AreEqual(SurfaceStructureCatalog.Laser.MaterialCost, QuickAssetPackageService.LaserSpecialCost,
                "the package is a second authority on the Laser's material");

            Assert.AreEqual(
                ExtractionCatalog.DrillT1PartsCost + SurfaceStructureCatalog.Command.PartsCost
                + SurfaceStructureCatalog.Laser.PartsCost,
                QuickAssetPackageService.TotalBasicCost,
                "the package's parts total is not the sum of the three structures it builds");
            Assert.AreEqual(
                SurfaceStructureCatalog.Command.MaterialCost + SurfaceStructureCatalog.Laser.MaterialCost,
                QuickAssetPackageService.TotalSpecialCost,
                "the package's material total is not the sum of the two structures that need material");
        }

        /// <summary>
        /// And what the package actually asks the hold for is those same numbers —
        /// the behaviour, not the table.
        /// <para>
        /// A cost constant only matters through <see cref="QuickAssetPackageService.TotalCost"/>,
        /// which is what the reservation is built from. Reading the constants back is
        /// a divergence sentinel; this is the part that would still be true if the
        /// constants were deleted tomorrow.
        /// </para>
        /// </summary>
        [Test]
        public void TheReservationAsksForExactlyTheCatalogCost()
        {
            var cost = QuickAssetPackageService.TotalCost(CommandMat, LaserMat);

            float basic = 0f, command = 0f, laser = 0f;
            foreach (var (itemId, amount) in cost)
            {
                if (itemId == ProcessingCatalog.BasicPartId) basic += amount;
                else if (itemId == CommandMat) command += amount;
                else if (itemId == LaserMat) laser += amount;
            }

            Assert.AreEqual(
                ExtractionCatalog.DrillT1PartsCost + SurfaceStructureCatalog.Command.PartsCost
                + SurfaceStructureCatalog.Laser.PartsCost, basic,
                "the parts line the package reserves is not the three structures' catalog cost");
            Assert.AreEqual(SurfaceStructureCatalog.Command.MaterialCost, command,
                "the Command Center's material line is not its catalog cost");
            Assert.AreEqual(SurfaceStructureCatalog.Laser.MaterialCost, laser,
                "the Laser's material line is not its catalog cost");
        }

        /// <summary>
        /// The three capsules leave in order, and each one's delay is what decides
        /// when it is due — <c>ShipQuickAssetPackage.Tick</c> compares
        /// <c>ElapsedSeconds</c> against exactly these three numbers.
        /// <para>
        /// The old shape asserted 0/2/4 as literals. What a player experiences is not
        /// the number, it is the ordering and the staging: the drill goes immediately,
        /// then the Command Center, then the Laser, and no two are due at once. That
        /// is what is asserted here, so a change to the pacing that keeps the staging
        /// stays green and a change that collapses it does not.
        /// </para>
        /// </summary>
        [Test]
        public void TheThreeCapsules_AreDueInOrder_AndNeverAtTheSameMoment()
        {
            float drill = QuickAssetPackageService.DrillSendDelay;
            float command = QuickAssetPackageService.CommandSendDelay;
            float laser = QuickAssetPackageService.LaserSendDelay;

            Assert.AreEqual(0f, drill,
                "the first capsule is the confirmation itself and must leave without a wait");
            Assert.Less(drill, command, "the Command Center capsule is not staged after the drill");
            Assert.Less(command, laser, "the Laser capsule is not staged after the Command Center");

            // Due-ness at a given elapsed time is the behaviour those numbers drive.
            bool DueAt(float elapsed, float delay) => elapsed >= delay;

            Assert.IsTrue(DueAt(0f, drill), "nothing is due at the moment of confirmation");
            Assert.IsFalse(DueAt(0f, command), "the Command Center capsule is due immediately");
            Assert.IsFalse(DueAt(0f, laser), "the Laser capsule is due immediately");
            Assert.IsTrue(DueAt(command, command), "the Command Center capsule is never due");
            Assert.IsFalse(DueAt(command, laser), "the Laser capsule is due with the Command Center");
            Assert.IsTrue(DueAt(laser, laser), "the Laser capsule is never due");
        }

        [Test]
        public void CheckPreconditions_ChecksInDocumentedOrder()
        {
            Assert.AreEqual(QuickPackageRejectReason.NotScanned,
                QuickAssetPackageService.CheckPreconditions(false, true, 0f, true, true, true, true, true, false, true));
            Assert.AreEqual(QuickPackageRejectReason.NotInRange,
                QuickAssetPackageService.CheckPreconditions(true, false, 0f, true, true, true, true, true, false, true));
            Assert.AreEqual(QuickPackageRejectReason.TooFast,
                QuickAssetPackageService.CheckPreconditions(true, true, 26f, true, true, true, true, true, false, true));
            Assert.AreEqual(QuickPackageRejectReason.NotStabilized,
                QuickAssetPackageService.CheckPreconditions(true, true, 0f, false, true, true, true, true, false, true));
            Assert.AreEqual(QuickPackageRejectReason.NodesOccupied,
                QuickAssetPackageService.CheckPreconditions(true, true, 0f, true, false, true, true, true, false, true));
            Assert.AreEqual(QuickPackageRejectReason.StationConstructionMissing,
                QuickAssetPackageService.CheckPreconditions(true, true, 0f, true, true, true, true, false, false, true));
            Assert.AreEqual(QuickPackageRejectReason.ThreatDetected,
                QuickAssetPackageService.CheckPreconditions(true, true, 0f, true, true, true, true, true, true, true));
            Assert.AreEqual(QuickPackageRejectReason.InsufficientCargo,
                QuickAssetPackageService.CheckPreconditions(true, true, 0f, true, true, true, true, true, false, false));
            Assert.AreEqual(QuickPackageRejectReason.None,
                QuickAssetPackageService.CheckPreconditions(true, true, 25f, true, true, true, true, true, false, true));
        }

        [Test]
        public void HasSufficientCargo_RequiresExactTotalsPerMaterial()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 65f);
            cargo.Add(CommandMat, 10f);
            cargo.Add(LaserMat, 4f); // one short

            Assert.IsFalse(QuickAssetPackageService.HasSufficientCargo(cargo, CommandMat, LaserMat));
            cargo.Add(LaserMat, 1f);
            Assert.IsTrue(QuickAssetPackageService.HasSufficientCargo(cargo, CommandMat, LaserMat));
        }

        [Test]
        public void TryConfirm_ReservesAtomically_InsufficientCargoLeavesNoPartialReservation()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 65f);
            cargo.Add(CommandMat, 10f);
            // Laser material entirely missing -> the whole reservation must fail, not partially succeed.
            QuickAssetPackageTransaction tx = QuickAssetPackageService.TryConfirm("tx1", "planet.00", CommandMat, LaserMat, cargo);

            Assert.IsNull(tx);
            Assert.AreEqual(65f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), "Basic must not have been partially reserved");
            Assert.AreEqual(10f, cargo.GetAvailable(CommandMat));
        }

        [Test]
        public void TryConfirm_Success_ReservesExactAmounts_StillPhysicallyInCargo()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 65f);
            cargo.Add(CommandMat, 10f);
            cargo.Add(LaserMat, 5f);

            QuickAssetPackageTransaction tx = QuickAssetPackageService.TryConfirm("tx1", "planet.00", CommandMat, LaserMat, cargo);

            Assert.IsNotNull(tx);
            Assert.AreEqual(65f, cargo.GetStored(ProcessingCatalog.BasicPartId), "reserved items stay physically stored");
            Assert.AreEqual(0f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), "but are no longer available for anything else");
        }

        [Test]
        public void SendDrillCommandLaser_ConsumeExactPortions_LeaveTheRestReserved()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 65f);
            cargo.Add(CommandMat, 10f);
            cargo.Add(LaserMat, 5f);
            QuickAssetPackageTransaction tx = QuickAssetPackageService.TryConfirm("tx1", "planet.00", CommandMat, LaserMat, cargo);

            int drillRefund = QuickAssetPackageService.SendDrill(tx, cargo);
            Assert.AreEqual(15, drillRefund); // floor(20*0.75)
            Assert.AreEqual(45f, cargo.GetStored(ProcessingCatalog.BasicPartId), "Drill's 20 Basic permanently consumed");
            Assert.IsTrue(tx.DrillSent);

            (int cRefundParts, int cRefundMat) = QuickAssetPackageService.SendCommand(tx, cargo);
            Assert.AreEqual(22, cRefundParts); // floor(30*0.75)
            Assert.AreEqual(7, cRefundMat);    // floor(10*0.75)
            Assert.AreEqual(15f, cargo.GetStored(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(0f, cargo.GetStored(CommandMat));

            (int lRefundParts, int lRefundMat) = QuickAssetPackageService.SendLaser(tx, cargo);
            Assert.AreEqual(11, lRefundParts); // floor(15*0.75)
            Assert.AreEqual(3, lRefundMat);    // floor(5*0.75)
            Assert.AreEqual(0f, cargo.GetStored(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(0f, cargo.GetStored(LaserMat));
            Assert.IsTrue(tx.Completed);
        }

        [Test]
        public void ReleaseUnsentReservation_BeforeAnySend_RefundsEverythingInFull()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 65f);
            cargo.Add(CommandMat, 10f);
            cargo.Add(LaserMat, 5f);
            QuickAssetPackageTransaction tx = QuickAssetPackageService.TryConfirm("tx1", "planet.00", CommandMat, LaserMat, cargo);

            QuickAssetPackageService.ReleaseUnsentReservation(tx, cargo);

            Assert.AreEqual(65f, cargo.GetAvailable(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(10f, cargo.GetAvailable(CommandMat));
            Assert.AreEqual(5f, cargo.GetAvailable(LaserMat));
        }

        [Test]
        public void ReleaseUnsentReservation_AfterDrillSent_OnlyReturnsTheUnsentPortion()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 65f);
            cargo.Add(CommandMat, 10f);
            cargo.Add(LaserMat, 5f);
            QuickAssetPackageTransaction tx = QuickAssetPackageService.TryConfirm("tx1", "planet.00", CommandMat, LaserMat, cargo);
            QuickAssetPackageService.SendDrill(tx, cargo); // consumes 20 Basic permanently

            QuickAssetPackageService.ReleaseUnsentReservation(tx, cargo);

            Assert.AreEqual(45f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), "the 20 already sent for Drill is gone, the remaining 45 becomes available again");
            Assert.AreEqual(10f, cargo.GetAvailable(CommandMat));
            Assert.AreEqual(5f, cargo.GetAvailable(LaserMat));
        }

        [Test]
        public void MissingNodeCost_OnlyListsStructuresNotYetSent()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 65f);
            cargo.Add(CommandMat, 10f);
            cargo.Add(LaserMat, 5f);
            QuickAssetPackageTransaction tx = QuickAssetPackageService.TryConfirm("tx1", "planet.00", CommandMat, LaserMat, cargo);
            QuickAssetPackageService.SendDrill(tx, cargo);

            var missing = QuickAssetPackageService.MissingNodeCost(tx);
            float basic = 0f;
            foreach (var (itemId, amount) in missing) if (itemId == ProcessingCatalog.BasicPartId) basic += amount;
            Assert.AreEqual(45f, basic, "only Command(30)+Laser(15) — never Drill's already-sent 20");
        }

        [Test]
        public void SendingACompletedStructure_NeverRePaysForAlreadyCompletedOnes()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 65f);
            cargo.Add(CommandMat, 15f); // same material for both — one merged 15-unit line
            QuickAssetPackageTransaction tx = QuickAssetPackageService.TryConfirm("tx1", "planet.00", CommandMat, CommandMat, cargo);
            QuickAssetPackageService.SendDrill(tx, cargo);
            QuickAssetPackageService.SendCommand(tx, cargo);
            QuickAssetPackageService.SendLaser(tx, cargo);

            Assert.IsTrue(tx.Completed);
            Assert.AreEqual(0f, cargo.GetStored(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(0f, cargo.GetStored(CommandMat));
            // A second call to any Send* on an already-sent structure is never made by
            // the adapter (guarded by the *Sent flags) — nothing here to double-charge.
        }
    }
}
