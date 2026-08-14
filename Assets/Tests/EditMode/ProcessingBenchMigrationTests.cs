using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for save schema v15 and the v14 → v15 processing migration
    /// (VS-017A.2 §5).
    /// <para>
    /// The migration's whole job is that a campaign mid-batch when the timed model was
    /// removed loses nothing and gains nothing. So every test here measures a total
    /// across the hold, the bench and the recovery queue before and after, rather than
    /// checking that one field landed somewhere — a migration can put the right number
    /// in the right place and still have duplicated it.
    /// </para>
    /// <para>
    /// <see cref="LegacyProcessingMigration"/> takes plain DTOs, so the arithmetic is
    /// exercised here with no scene and no MonoBehaviour. The director's part is only
    /// deciding which legacy shape to hand it, which the schema-version pin below
    /// covers.
    /// </para>
    /// </summary>
    public class ProcessingBenchMigrationTests
    {
        private const int TestSlot = 92;

        private const int Family = 3;
        private static string RawId => ExtractionCatalog.ResourceIds[Family];
        private static string OtherRawId => ExtractionCatalog.ResourceIds[0];
        private static int PartRecipe => Family * 2;

        [TearDown]
        public void Cleanup()
        {
            string main = SaveService.SlotFile(TestSlot);
            string dir = Path.GetDirectoryName(main);
            if (dir == null) return;
            foreach (string f in new[]
            {
                main, Path.Combine(dir, $"slot{TestSlot}.bak1.json"),
                Path.Combine(dir, $"slot{TestSlot}.bak2.json"),
                Path.Combine(dir, $"slot{TestSlot}.bak3.json"),
                Path.Combine(dir, $"slot{TestSlot}.tmp.json")
            })
            {
                if (File.Exists(f)) File.Delete(f);
            }
        }

        /// <summary>Everything the player owns of one item, across the hold, the bench and the recovery queue.</summary>
        private static float Owned(LogisticsCargo cargo, ProcessingBenchRuntime bench, string itemId)
        {
            float total = cargo.GetStored(itemId);
            if (!bench.IsEmpty && bench.InputItemId == itemId) total += bench.InputAmount;
            foreach (LegacyProcessingClaim claim in bench.LegacyClaims)
                if (claim != null && claim.ItemId == itemId) total += claim.Amount;
            return total;
        }

        private static ProcessorDto ActiveJob(string inputId, float reserved) =>
            new ProcessorDto
            {
                recipeIndex = PartRecipe, batchTimer = 1.25f, waiting = false,
                reservedItemId = inputId, reservedAmount = reserved
            };

        // ---- version pins ------------------------------------------------------

        /// <summary>
        /// The three version numbers this slice was allowed to move, and the two it
        /// was not. Generation and content identity did not change — only ship-owned
        /// processing state did — and bumping either would invalidate every generated
        /// system or every content fingerprint for no reason at all.
        /// </summary>
        [Test]
        public void SchemaIs15AndGenerationAndContentVersionsAreUnmoved()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema, "the bench migration needs schema 15");
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion,
                "world generation did not change; bumping this would rebuild every system");
            Assert.AreEqual(3, ContentFingerprint.ContentRevision,
                "content identity did not change");
            // P2-SAVE-201 moved this one deliberately: 13 let a schema-less
            // envelope pass for a historical save. 0 means "unknown" and the
            // loader rejects anything below 6.
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion,
                "an absent schemaVersion must stay unknown, not impersonate v13");
        }

        // ---- v14 slot states, one per row of the design table -------------------

        [Test]
        public void AnIdleSlotMigratesToAnEmptyBench()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 40f);
            var bench = new ProcessingBenchRuntime();

            ProcessingMigrationReport report = LegacyProcessingMigration.Migrate(
                new List<ProcessorDto> { new ProcessorDto { recipeIndex = -1 } }, cargo, bench);

            Assert.IsTrue(bench.IsEmpty, "an idle slot put something on the bench");
            Assert.IsFalse(bench.HasLegacyClaims);
            Assert.AreEqual(40f, cargo.GetStored(RawId), 0.0001f, "an idle slot moved the hold");
            Assert.AreEqual(1, report.IdleSlots);
            Assert.AreEqual(0, report.ActiveJobs);
        }

        /// <summary>
        /// The decisive row. A running batch had its input RESERVED — physically still
        /// in the hold, merely unspendable. The migration releases that lock and moves
        /// the same units onto the bench, which means taking them out of the hold: the
        /// bench and the hold are disjoint now, so leaving the units in both is how
        /// this would mint resources. The batch is never auto-completed, so no output
        /// appears out of an unfinished job.
        /// </summary>
        [Test]
        public void AnActiveTimedJobBecomesBenchInputWithTheCampaignTotalUnchanged()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 40f);
            var bench = new ProcessingBenchRuntime();
            float ownedBefore = cargo.GetStored(RawId);

            ProcessingMigrationReport report = LegacyProcessingMigration.Migrate(
                new List<ProcessorDto> { ActiveJob(RawId, 2f) }, cargo, bench);

            Assert.AreEqual(RawId, bench.InputItemId);
            Assert.AreEqual(2, bench.InputAmount, "the reserved input did not reach the bench");
            Assert.AreEqual(38f, cargo.GetStored(RawId), 0.0001f,
                "the reserved units stayed in the hold as well — the migration minted resources");
            Assert.AreEqual(ownedBefore, Owned(cargo, bench, RawId), 0.0001f,
                "the campaign total changed across the migration");
            Assert.AreEqual(0f, cargo.GetReserved(RawId), 0.0001f, "a reservation survived the migration");
            Assert.AreEqual(0f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the unfinished batch was auto-completed");
            Assert.AreEqual(0L, bench.ConversionsTotal, "the migration counted as a conversion");
            Assert.AreEqual(1, report.ActiveJobs);
            Assert.AreEqual(2, report.BenchAmountMoved);
        }

        [Test]
        public void APendingOutputIsDeliveredExactlyOnceWhenItFits()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 10f);
            var bench = new ProcessingBenchRuntime();

            var finished = new ProcessorDto
            {
                recipeIndex = PartRecipe, waiting = true,
                pendingItemId = ProcessingCatalog.BasicPartId, pendingAmount = 1f
            };

            ProcessingMigrationReport report =
                LegacyProcessingMigration.Migrate(new List<ProcessorDto> { finished }, cargo, bench);

            Assert.AreEqual(1f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the finished batch's output was lost");
            Assert.IsFalse(bench.HasLegacyClaims, "a delivered claim stayed in the queue");
            Assert.AreEqual(0, report.ClaimsStillOwed);

            // Draining again must not hand it over a second time.
            ProcessingBenchCommands.DrainLegacyClaims(bench, cargo);
            Assert.AreEqual(1f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the same output was delivered twice");
        }

        [Test]
        public void APendingOutputThatDoesNotFitIsPreservedAsAnExplicitClaim()
        {
            // Room for the reserved input only; the output has nowhere to go.
            var cargo = new LogisticsCargo(6f);
            cargo.LoadItem(RawId, 2f);
            cargo.LoadItem(ProcessingCatalog.BasicPartId, 4f);
            var bench = new ProcessingBenchRuntime();

            var finished = new ProcessorDto
            {
                recipeIndex = PartRecipe, waiting = true,
                pendingItemId = ProcessingCatalog.BasicPartId, pendingAmount = 2f
            };

            LegacyProcessingMigration.Migrate(new List<ProcessorDto> { finished }, cargo, bench);

            Assert.IsTrue(bench.HasLegacyClaims, "an undeliverable output was silently dropped");
            Assert.AreEqual(2f, Owned(cargo, bench, ProcessingCatalog.BasicPartId) - 4f, 0.0001f,
                "the preserved claim does not add up to what was owed");

            // The player makes room; the claim is handed over, once.
            cargo.TryConsumeImmediate(ProcessingCatalog.BasicPartId, 4f);
            Assert.IsFalse(ProcessingBenchCommands.DrainLegacyClaims(bench, cargo));
            Assert.AreEqual(2f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f);
        }

        [Test]
        public void SeveralSlotsHoldingTheSameInputMergeOntoOneBench()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 40f);
            var bench = new ProcessingBenchRuntime();

            ProcessingMigrationReport report = LegacyProcessingMigration.Migrate(new List<ProcessorDto>
            {
                ActiveJob(RawId, 2f), ActiveJob(RawId, 3f), new ProcessorDto { recipeIndex = -1 }
            }, cargo, bench);

            Assert.AreEqual(RawId, bench.InputItemId);
            Assert.AreEqual(5, bench.InputAmount, "the same-item slots did not merge");
            Assert.AreEqual(35f, cargo.GetStored(RawId), 0.0001f);
            Assert.AreEqual(40f, Owned(cargo, bench, RawId), 0.0001f, "merging changed the total");
            Assert.IsFalse(bench.HasLegacyClaims, "a same-item merge queued a claim it did not need");
            Assert.AreEqual(1, report.InputTypes);
            Assert.AreEqual(1, report.IdleSlots);
        }

        /// <summary>
        /// Different inputs are never silently merged into one pile — that would turn
        /// what the player owns into something they never had. The largest takes the
        /// bench; every other item stays exactly where it already was, because a v14
        /// reservation never removed it from the hold in the first place.
        /// <para>
        /// The assertion that matters is the second one: handing the secondary input
        /// "back" through the recovery queue would leave the player owning 22 of
        /// something they had 20 of, which is the failure this test exists to catch.
        /// </para>
        /// </summary>
        [Test]
        public void SeveralSlotsHoldingDifferentInputsAreNeverMergedAndNeverDuplicated()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 20f);
            cargo.LoadItem(OtherRawId, 20f);
            var bench = new ProcessingBenchRuntime();

            ProcessingMigrationReport report = LegacyProcessingMigration.Migrate(new List<ProcessorDto>
            {
                ActiveJob(RawId, 6f), ActiveJob(OtherRawId, 2f)
            }, cargo, bench);

            Assert.AreEqual(RawId, bench.InputItemId, "the larger input did not take the bench");
            Assert.AreEqual(6, bench.InputAmount);
            Assert.AreEqual(14f, cargo.GetStored(RawId), 0.0001f, "the bench's units stayed in the hold too");
            Assert.AreEqual(20f, Owned(cargo, bench, RawId), 0.0001f, "the bench item's total changed");

            Assert.AreEqual(20f, Owned(cargo, bench, OtherRawId), 0.0001f,
                "the secondary input was duplicated or destroyed rather than simply left alone");
            Assert.AreEqual(20f, cargo.GetStored(OtherRawId), 0.0001f,
                "the secondary input moved somewhere it never needed to go");
            Assert.IsFalse(bench.HasLegacyClaims,
                "a secondary input was queued for return to the hold it was already in");
            Assert.AreEqual(2, report.InputTypes);
            Assert.AreEqual(2, report.SecondaryInputUnitsLeftInHold);
        }

        /// <summary>
        /// The same rule under a hold with no spare room at all. Nothing needs room:
        /// the secondary input is not going anywhere, which is exactly why it cannot be
        /// stranded, lost, or turned into a second permanent inventory.
        /// </summary>
        [Test]
        public void ASecondaryInputNeedsNoRoomBecauseItNeverLeftTheHold()
        {
            var cargo = new LogisticsCargo(12f);
            cargo.LoadItem(RawId, 6f);
            cargo.LoadItem(OtherRawId, 6f);
            var bench = new ProcessingBenchRuntime();
            Assert.AreEqual(0f, cargo.Capacity - cargo.UsedVolume, 0.0001f, "precondition: a completely full hold");

            LegacyProcessingMigration.Migrate(new List<ProcessorDto>
            {
                ActiveJob(RawId, 6f), ActiveJob(OtherRawId, 6f)
            }, cargo, bench);

            Assert.AreEqual(6f, Owned(cargo, bench, RawId), 0.0001f);
            Assert.AreEqual(6f, Owned(cargo, bench, OtherRawId), 0.0001f);
            Assert.AreEqual(6f, cargo.GetStored(OtherRawId), 0.0001f,
                "the full hold lost the secondary input it was already holding");

            ProcessingBenchCommands.DrainLegacyClaims(bench, cargo);
            Assert.AreEqual(6f, Owned(cargo, bench, OtherRawId), 0.0001f,
                "a drain duplicated an input that was never queued");
        }

        [Test]
        public void TheV2SingleProcessorFieldIsStillReadable()
        {
            // A v1/v2 file wrote one "processor" and never populated processorSlots.
            var payload = new SavePayload { seed = 555 };
            payload.processor = ActiveJob(RawId, 2f);

            string payloadJson = JsonUtility.ToJson(payload);
            var envelope = new SaveEnvelope
            {
                schemaVersion = 2, campaignId = "campaign-1", revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(), payloadJson = payloadJson
            };
            WriteEnvelope(envelope);

            SavePayload loaded = SaveService.Load(TestSlot);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(0, loaded.processorSlots.Count);
            Assert.AreEqual(RawId, loaded.processor.reservedItemId,
                "the legacy single-slot job must still be readable for migration");

            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 10f);
            var bench = new ProcessingBenchRuntime();
            LegacyProcessingMigration.Migrate(new List<ProcessorDto> { loaded.processor }, cargo, bench);
            Assert.AreEqual(2, bench.InputAmount);
        }

        [Test]
        public void NoLegacySlotsAtAllIsAReportedNoOp()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 10f);
            var bench = new ProcessingBenchRuntime();

            ProcessingMigrationReport report = LegacyProcessingMigration.Migrate(null, cargo, bench);

            Assert.IsFalse(report.HadLegacySlots);
            Assert.AreEqual("no_legacy_slots", report.Summary);
            Assert.IsTrue(bench.IsEmpty);
            Assert.AreEqual(10f, cargo.GetStored(RawId), 0.0001f);
        }

        // ---- save round trip ---------------------------------------------------

        [Test]
        public void TheBenchSurvivesASaveLoadRoundTripWithItsTypeAndAmount()
        {
            var payload = new SavePayload { seed = 42 };
            payload.cargo.Add(new CargoItemDto { itemId = RawId, amount = 31f });
            payload.processingBench = new ProcessingBenchDto { inputItemId = RawId, inputAmount = 7 };
            payload.processingBench.legacyClaims.Add(new LegacyProcessingClaimDto
            {
                itemId = ProcessingCatalog.BasicPartId, amount = 2
            });

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion, out _);

            Assert.AreEqual(15, schemaVersion, "the save did not record the current schema");
            Assert.AreEqual(RawId, loaded.processingBench.inputItemId);
            Assert.AreEqual(7, loaded.processingBench.inputAmount);
            Assert.AreEqual(1, loaded.processingBench.legacyClaims.Count);
            Assert.AreEqual(2, loaded.processingBench.legacyClaims[0].amount);
            Assert.AreEqual(31f, loaded.cargo[0].amount, 0.0001f, "the hold did not survive alongside the bench");

            // A second cycle must not duplicate anything.
            Assert.IsTrue(SaveService.Save(TestSlot, loaded, 2));
            SavePayload again = SaveService.Load(TestSlot);
            Assert.AreEqual(7, again.processingBench.inputAmount, "a second cycle changed the bench");
            Assert.AreEqual(1, again.processingBench.legacyClaims.Count, "a second cycle duplicated a claim");
            Assert.AreEqual(1, again.cargo.Count, "a second cycle duplicated a cargo line");
        }

        /// <summary>
        /// Restoring twice — what a restart does — must land on the same bench, not on
        /// twice the bench. <see cref="ProcessingBenchRuntime.Clear"/> before each
        /// restore is what guarantees it, so this is the test that would catch its
        /// removal.
        /// </summary>
        [Test]
        public void RestoringTwiceDoesNotDuplicateTheBench()
        {
            var bench = new ProcessingBenchRuntime();

            bench.Clear();
            bench.Restore(RawId, 7);
            bench.AddLegacyClaim(ProcessingCatalog.BasicPartId, 2);

            bench.Clear();
            bench.Restore(RawId, 7);
            bench.AddLegacyClaim(ProcessingCatalog.BasicPartId, 2);

            Assert.AreEqual(7, bench.InputAmount, "a second restore stacked onto the first");
            Assert.AreEqual(1, bench.LegacyClaims.Count, "a second restore duplicated the claim queue");
            Assert.AreEqual(2, bench.LegacyClaims[0].Amount);
            Assert.AreEqual(0L, bench.ConversionsTotal, "a restore replayed conversion history");
        }

        [Test]
        public void AnEmptyBenchRoundTripsAsAnEmptyBenchRatherThanAPhantomOne()
        {
            var payload = new SavePayload { seed = 11 };
            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));

            SavePayload loaded = SaveService.Load(TestSlot);
            bool looksLoaded = loaded.processingBench != null
                               && !string.IsNullOrEmpty(loaded.processingBench.inputItemId)
                               && loaded.processingBench.inputAmount > 0;
            Assert.IsFalse(looksLoaded,
                "a payload saved with an empty bench round-tripped into a loaded one");

            var bench = new ProcessingBenchRuntime();
            bench.Restore(loaded.processingBench?.inputItemId, loaded.processingBench?.inputAmount ?? 0);
            Assert.IsTrue(bench.IsEmpty);
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
