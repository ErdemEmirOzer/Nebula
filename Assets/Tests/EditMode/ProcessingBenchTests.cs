using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the processing bench (VS-017A.2) — the inventory-backed,
    /// instant, atomic replacement for the timed slot bay.
    /// <para>
    /// The property under nearly every test here is conservation: at any moment,
    /// <c>hold + bench</c> holds exactly what the player owns, and a conversion moves
    /// value along the recipe's ratio without ever creating or destroying any. The old
    /// model could not state that, because a reservation left input in the hold while
    /// also promising it to a slot, so two places disagreed about the same units. That
    /// is why the assertions below are almost always about a total rather than about a
    /// single number.
    /// </para>
    /// <para>
    /// Every test builds its own <see cref="LogisticsCargo"/> and
    /// <see cref="ProcessingBenchRuntime"/>. Neither needs a scene, a MonoBehaviour or
    /// a frame — which is the point: the bench has no tick to run.
    /// </para>
    /// </summary>
    public class ProcessingBenchTests
    {
        // Water, family 3. Its two catalog recipes are 2 -> 1 part and 3 -> 1 material.
        private const int Family = 3;
        private static string RawId => ExtractionCatalog.ResourceIds[Family];
        private static int PartRecipe => Family * 2;
        private static int MaterialRecipe => Family * 2 + 1;
        private static string MaterialId => ProcessingCatalog.MaterialIds[Family];

        private static LogisticsCargo Hold(float capacity, float raw)
        {
            var cargo = new LogisticsCargo(capacity);
            if (raw > 0f) cargo.LoadItem(RawId, raw);
            return cargo;
        }

        /// <summary>
        /// Everything the player owns of one item, wherever it is sitting. The bench is
        /// physically disjoint from the hold, so the only honest total spans both.
        /// </summary>
        private static float Owned(LogisticsCargo cargo, ProcessingBenchRuntime bench, string itemId)
        {
            float total = cargo.GetStored(itemId);
            if (!bench.IsEmpty && bench.InputItemId == itemId) total += bench.InputAmount;
            return total;
        }

        // ---- deposit -----------------------------------------------------------

        [Test]
        public void DepositMovesWholeUnitsOutOfTheHoldAndOntoTheBench()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();

            Assert.AreEqual(BenchDepositReason.None,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 10));

            Assert.AreEqual(30f, cargo.GetStored(RawId), 0.0001f,
                "the deposit did not physically leave the hold — that is a reservation, not a move");
            Assert.AreEqual(10, bench.InputAmount);
            Assert.AreEqual(RawId, bench.InputItemId);
            Assert.AreEqual(40f, Owned(cargo, bench, RawId), 0.0001f, "the deposit changed the campaign total");
            Assert.AreEqual(0f, cargo.GetReserved(RawId), 0.0001f,
                "the bench must not reserve; the two authorities are disjoint by construction");
        }

        [Test]
        public void DepositingTheSameItemAgainAddsToWhatIsAlreadyThere()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();

            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 6);
            Assert.AreEqual(BenchDepositReason.None,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 4));

            Assert.AreEqual(10, bench.InputAmount, "the second deposit replaced the first instead of adding to it");
            Assert.AreEqual(30f, cargo.GetStored(RawId), 0.0001f);
            Assert.AreEqual(40f, Owned(cargo, bench, RawId), 0.0001f);
        }

        /// <summary>
        /// The bench is single-type. A different item is refused rather than swapped:
        /// swapping would send the standing input back to the hold without the player
        /// asking, and a hold that is full would then have nowhere to put it.
        /// </summary>
        [Test]
        public void ADifferentInputIsRefusedAndNeitherSideMoves()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            string otherRaw = ExtractionCatalog.ResourceIds[0];
            cargo.LoadItem(otherRaw, 20f);
            var bench = new ProcessingBenchRuntime();

            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 10);

            Assert.AreEqual(BenchDepositReason.DifferentInputAlreadyLoaded,
                ProcessingBenchCommands.TryDeposit(bench, cargo, otherRaw, 5));

            Assert.AreEqual(RawId, bench.InputItemId, "the refused deposit changed the bench's type");
            Assert.AreEqual(10, bench.InputAmount);
            Assert.AreEqual(20f, cargo.GetStored(otherRaw), 0.0001f, "the refused deposit still debited the hold");
        }

        [Test]
        public void DepositRefusesEveryAmountThatIsNotAWholePositiveInRangeNumber()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();

            Assert.AreEqual(BenchDepositReason.InvalidAmount,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 0), "zero");
            Assert.AreEqual(BenchDepositReason.InvalidAmount,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, -5), "negative");
            Assert.AreEqual(BenchDepositReason.InvalidAmount,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, int.MinValue), "int.MinValue");
            Assert.AreEqual(BenchDepositReason.InvalidAmount,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, int.MaxValue), "past the safe range");
            Assert.AreEqual(BenchDepositReason.InvalidAmount,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId,
                    ProcessingBenchCommands.MaxTransactionAmount + 1), "one past the cap");

            Assert.AreEqual(BenchDepositReason.UnknownItem,
                ProcessingBenchCommands.TryDeposit(bench, cargo, null, 1), "null item");
            Assert.AreEqual(BenchDepositReason.ItemNotProcessable,
                ProcessingBenchCommands.TryDeposit(bench, cargo, ProcessingCatalog.BasicPartId, 1),
                "no recipe takes a finished part as input");

            Assert.IsTrue(bench.IsEmpty, "a refused deposit put something on the bench");
            Assert.AreEqual(40f, cargo.GetStored(RawId), 0.0001f, "a refused deposit moved the hold");
        }

        /// <summary>
        /// A fractional or non-finite amount never reaches the commands: it is refused
        /// at the boundary, so nothing can silently cast <c>2.7</c> down to 2 and
        /// destroy the 0.7 the player still owns.
        /// </summary>
        [Test]
        public void FractionalAndNonFiniteAmountsAreRefusedAtTheBoundary()
        {
            Assert.IsFalse(ProcessingBenchCommands.TryWholeUnits(2.7, out _), "2.7");
            Assert.IsFalse(ProcessingBenchCommands.TryWholeUnits(0.5, out _), "0.5");
            Assert.IsFalse(ProcessingBenchCommands.TryWholeUnits(-3, out _), "negative");
            Assert.IsFalse(ProcessingBenchCommands.TryWholeUnits(0, out _), "zero");
            Assert.IsFalse(ProcessingBenchCommands.TryWholeUnits(double.NaN, out _), "NaN");
            Assert.IsFalse(ProcessingBenchCommands.TryWholeUnits(double.PositiveInfinity, out _), "infinity");
            Assert.IsFalse(ProcessingBenchCommands.TryWholeUnits(
                ProcessingBenchCommands.MaxTransactionAmount + 1d, out _), "past the cap");

            Assert.IsTrue(ProcessingBenchCommands.TryWholeUnits(3, out int three));
            Assert.AreEqual(3, three);
            Assert.IsTrue(ProcessingBenchCommands.TryWholeUnits(
                ProcessingBenchCommands.MaxTransactionAmount, out int cap), "the cap itself is valid");
            Assert.AreEqual(ProcessingBenchCommands.MaxTransactionAmount, cap);
        }

        /// <summary>
        /// A hold really can carry 7.4 of something — extraction delivers fractions. The
        /// bench offers the 7 whole units and the 0.4 stays visible in the hold rather
        /// than being rounded up into a unit the player never had, or floored away.
        /// </summary>
        [Test]
        public void AFractionalStackExposesItsWholeUnitsAndKeepsTheRemainder()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 7.4f);
            var bench = new ProcessingBenchRuntime();

            Assert.AreEqual(7, ProcessingBenchCommands.WholeUnitsAvailable(cargo, RawId));
            Assert.AreEqual(BenchDepositReason.InsufficientCargo,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 8), "8 whole units were never there");

            Assert.AreEqual(BenchDepositReason.None,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 7));
            Assert.AreEqual(0.4f, cargo.GetStored(RawId), 0.001f, "the fraction was silently destroyed");
            Assert.AreEqual(7.4f, Owned(cargo, bench, RawId), 0.001f);
        }

        [Test]
        public void DepositRefusesWhatWouldPushTheBenchPastTheSafeRange()
        {
            var cargo = new LogisticsCargo(float.MaxValue);
            cargo.LoadItem(RawId, ProcessingBenchCommands.MaxTransactionAmount);
            var bench = new ProcessingBenchRuntime();
            bench.Restore(RawId, ProcessingBenchCommands.MaxTransactionAmount - 1);

            Assert.AreEqual(BenchDepositReason.Overflow,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 5));
            Assert.AreEqual(ProcessingBenchCommands.MaxTransactionAmount - 1, bench.InputAmount);
        }

        // ---- withdraw ----------------------------------------------------------

        [Test]
        public void PartialAndFullWithdrawBothReturnEveryUnitToTheHold()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 10);

            Assert.AreEqual(BenchWithdrawReason.None,
                ProcessingBenchCommands.TryWithdraw(bench, cargo, 4));
            Assert.AreEqual(6, bench.InputAmount);
            Assert.AreEqual(34f, cargo.GetStored(RawId), 0.0001f);

            Assert.AreEqual(BenchWithdrawReason.None,
                ProcessingBenchCommands.TryWithdrawAll(bench, cargo, out int withdrawn));
            Assert.AreEqual(6, withdrawn);
            Assert.IsTrue(bench.IsEmpty);
            Assert.IsNull(bench.InputItemId, "an emptied bench kept its item type");
            Assert.AreEqual(40f, cargo.GetStored(RawId), 0.0001f, "a full round trip changed the total");
        }

        [Test]
        public void WithdrawRefusesMoreThanTheBenchHoldsAndAnEmptyBench()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();

            Assert.AreEqual(BenchWithdrawReason.BenchEmpty,
                ProcessingBenchCommands.TryWithdraw(bench, cargo, 1));
            Assert.AreEqual(BenchWithdrawReason.BenchEmpty,
                ProcessingBenchCommands.TryWithdrawAll(bench, cargo, out int nothing));
            Assert.AreEqual(0, nothing);

            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 5);
            Assert.AreEqual(BenchWithdrawReason.InsufficientBenchInput,
                ProcessingBenchCommands.TryWithdraw(bench, cargo, 6));
            Assert.AreEqual(BenchWithdrawReason.InvalidAmount,
                ProcessingBenchCommands.TryWithdraw(bench, cargo, 0));
            Assert.AreEqual(5, bench.InputAmount, "a refused withdrawal still moved the bench");
            Assert.AreEqual(35f, cargo.GetStored(RawId), 0.0001f);
        }

        /// <summary>
        /// All-or-nothing. A hold with room for two of the five units on the bench gets
        /// none of them: a half-withdrawal would leave the player guessing where the
        /// rest went, and the bench is a perfectly safe place for it to stay.
        /// </summary>
        [Test]
        public void WithdrawIntoAFullHoldIsRefusedWholeAndDeliversNothing()
        {
            var cargo = new LogisticsCargo(10f);
            cargo.LoadItem(RawId, 5f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 5);

            // Fill the freed room with something else, leaving 2 units of headroom.
            cargo.Add(ProcessingCatalog.BasicPartId, 8f);
            Assert.AreEqual(2f, cargo.Capacity - cargo.UsedVolume, 0.0001f, "precondition: room for 2, not 5");

            Assert.AreEqual(BenchWithdrawReason.NoCargoRoom,
                ProcessingBenchCommands.TryWithdraw(bench, cargo, 5));
            Assert.AreEqual(5, bench.InputAmount, "a refused withdrawal emptied the bench anyway");
            Assert.AreEqual(0f, cargo.GetStored(RawId), 0.0001f, "a refused withdrawal delivered part of itself");

            // The part that does fit is still allowed, on the player's explicit ask.
            Assert.AreEqual(BenchWithdrawReason.None,
                ProcessingBenchCommands.TryWithdraw(bench, cargo, 2));
            Assert.AreEqual(3, bench.InputAmount);
        }

        // ---- catalog-derived questions -----------------------------------------

        /// <summary>
        /// Processability is derived from the recipe table, never from a hand-written
        /// list — so a raw resource is processable, a finished part is not, and a
        /// recipe added later needs no change anywhere.
        /// </summary>
        [Test]
        public void ProcessabilityAndRecipeFilteringComeFromTheCatalogAlone()
        {
            foreach (string rawId in ExtractionCatalog.ResourceIds)
                Assert.IsTrue(ProcessingBenchCommands.IsProcessable(rawId), rawId + " should be processable");

            Assert.IsFalse(ProcessingBenchCommands.IsProcessable(ProcessingCatalog.BasicPartId));
            foreach (string materialId in ProcessingCatalog.MaterialIds)
                Assert.IsFalse(ProcessingBenchCommands.IsProcessable(materialId));
            Assert.IsFalse(ProcessingBenchCommands.IsProcessable(null));
            Assert.IsFalse(ProcessingBenchCommands.IsProcessable("item.does_not_exist"));

            List<int> indices = ProcessingBenchCommands.RecipeIndicesFor(RawId);
            Assert.AreEqual(2, indices.Count, "water feeds exactly the part and material recipes");
            foreach (int index in indices)
                Assert.AreEqual(RawId, ProcessingCatalog.Recipes[index].InputItemId,
                    "the filter returned a recipe that does not take this input");

            // The filter and the catalog agree in both directions: every recipe whose
            // input is this item is listed, and nothing else is.
            int expected = 0;
            for (int i = 0; i < ProcessingCatalog.Recipes.Length; i++)
                if (ProcessingCatalog.Recipes[i].InputItemId == RawId) expected++;
            Assert.AreEqual(expected, indices.Count);

            Assert.AreEqual(0, ProcessingBenchCommands.RecipeIndicesFor(null).Count);
            Assert.IsNull(ProcessingBenchCommands.RecipeAt(-1));
            Assert.IsNull(ProcessingBenchCommands.RecipeAt(ProcessingCatalog.Recipes.Length));
        }

        /// <summary>The two approved ratios, from the one table that owns them.</summary>
        [Test]
        public void EveryRecipeKeepsItsApprovedRatio()
        {
            for (int f = 0; f < ExtractionCatalog.ResourceIds.Length; f++)
            {
                ProcessingRecipe part = ProcessingCatalog.Recipes[f * 2];
                ProcessingRecipe material = ProcessingCatalog.Recipes[f * 2 + 1];

                Assert.AreEqual(2, part.InputCount, part.Id + " lost its 2 -> 1 ratio");
                Assert.AreEqual(1, part.OutputCount, part.Id);
                Assert.AreEqual(ProcessingCatalog.BasicPartId, part.OutputItemId, part.Id);

                Assert.AreEqual(3, material.InputCount, material.Id + " lost its 3 -> 1 ratio");
                Assert.AreEqual(1, material.OutputCount, material.Id);
                Assert.AreEqual(ProcessingCatalog.MaterialIds[f], material.OutputItemId, material.Id);
            }
        }

        [Test]
        public void MaxConvertibleInputRoundsDownToAWholeMultipleOfTheRecipe()
        {
            var bench = new ProcessingBenchRuntime();

            bench.Restore(RawId, 7);
            Assert.AreEqual(6, ProcessingBenchCommands.MaxConvertibleInput(bench, PartRecipe), "7 at 2 -> 1");
            Assert.AreEqual(6, ProcessingBenchCommands.MaxConvertibleInput(bench, MaterialRecipe), "7 at 3 -> 1");

            bench.Restore(RawId, 2);
            Assert.AreEqual(2, ProcessingBenchCommands.MaxConvertibleInput(bench, PartRecipe));
            Assert.AreEqual(0, ProcessingBenchCommands.MaxConvertibleInput(bench, MaterialRecipe),
                "2 cannot feed a 3 -> 1 recipe at all");

            // A recipe that does not take what is loaded converts none of it.
            bench.Restore(ExtractionCatalog.ResourceIds[0], 9);
            Assert.AreEqual(0, ProcessingBenchCommands.MaxConvertibleInput(bench, PartRecipe));

            Assert.AreEqual(3, ProcessingBenchCommands.OutputFor(ProcessingCatalog.Recipes[PartRecipe], 6));
            Assert.AreEqual(2, ProcessingBenchCommands.OutputFor(ProcessingCatalog.Recipes[MaterialRecipe], 6));
            Assert.AreEqual(0, ProcessingBenchCommands.OutputFor(null, 6));
        }

        [Test]
        public void AnAmountThatIsNotAWholeMultipleIsRefusedRatherThanRounded()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 10);

            Assert.AreEqual(BenchConvertReason.AmountNotMultipleOfRecipeInput,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, PartRecipe, 5),
                "5 is not a multiple of 2");
            Assert.AreEqual(BenchConvertReason.AmountNotMultipleOfRecipeInput,
                ProcessingBenchCommands.TryConvert(bench, cargo, MaterialRecipe, 10, out int produced),
                "10 is not a multiple of 3");
            Assert.AreEqual(0, produced);

            Assert.AreEqual(10, bench.InputAmount, "a refused conversion spent input anyway");
            Assert.AreEqual(0f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f);
            Assert.AreEqual(0L, bench.ConversionsTotal, "a refused conversion counted as one");
        }

        // ---- convert -----------------------------------------------------------

        [Test]
        public void ConversionIsInstantAtomicAndCreatesNoTimerOrPendingClaim()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 6);

            Assert.AreEqual(BenchConvertReason.None,
                ProcessingBenchCommands.TryConvert(bench, cargo, PartRecipe, 6, out int produced));

            Assert.AreEqual(3, produced, "6 raw at 2 -> 1 is 3 parts, in the same call");
            Assert.AreEqual(3f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the output did not reach the hold inside the transaction");
            Assert.IsTrue(bench.IsEmpty, "the bench kept input it had already spent");
            Assert.AreEqual(34f, cargo.GetStored(RawId), 0.0001f, "the untouched raw in the hold moved");
            Assert.AreEqual(1L, bench.ConversionsTotal);
            Assert.AreEqual(3L, bench.BatchEquivalentsTotal, "three legacy batches' worth, in one transaction");
            Assert.AreEqual(ProcessingCatalog.Recipes[PartRecipe].Id, bench.LastRecipeId);

            // No timer anywhere, and nothing owed to the player: the hold's used volume
            // is exactly what it physically holds (34 raw + 3 parts, volume 1 each).
            // The pending-claim ledger this used to read is gone with P2-DATA-301 —
            // asserting the volume identity says the same thing about the state that
            // still exists.
            Assert.AreEqual(37f, cargo.UsedVolume, 0.0001f,
                "the hold owes volume to something outside its physical stock");
            Assert.IsFalse(bench.HasLegacyClaims, "a conversion created a legacy claim");
        }

        [Test]
        public void APartialConversionLeavesTheRestOnTheBench()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 10);

            Assert.AreEqual(BenchConvertReason.None,
                ProcessingBenchCommands.TryConvert(bench, cargo, PartRecipe, 6, out int produced));

            Assert.AreEqual(3, produced);
            Assert.AreEqual(4, bench.InputAmount, "the unconverted input must stay on the bench");
            Assert.AreEqual(RawId, bench.InputItemId);
        }

        /// <summary>
        /// The hold has no room for the output, so the conversion does not start. This
        /// is the single most important difference from the timed model: there, the
        /// batch ran anyway, spent the input and left a pending claim. Here the input
        /// is untouched and there is nothing to be owed.
        /// </summary>
        [Test]
        public void AConversionWithNoRoomForItsOutputNeverStarts()
        {
            var cargo = new LogisticsCargo(20f);
            cargo.LoadItem(RawId, 6f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 6);

            // Fill every unit of freed room, so a 1-volume part cannot land.
            cargo.Add(ProcessingCatalog.BasicPartId, 20f);
            Assert.AreEqual(0f, cargo.Capacity - cargo.UsedVolume, 0.0001f, "precondition: no headroom");

            Assert.AreEqual(BenchConvertReason.OutputNoRoom,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, PartRecipe, 6));
            Assert.AreEqual(BenchConvertReason.OutputNoRoom,
                ProcessingBenchCommands.TryConvert(bench, cargo, PartRecipe, 6, out int produced));

            Assert.AreEqual(0, produced);
            Assert.AreEqual(6, bench.InputAmount, "the refused conversion spent the input");
            Assert.AreEqual(20f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the refused conversion delivered part of its output");
            Assert.AreEqual(0L, bench.ConversionsTotal);
            Assert.IsFalse(bench.HasLegacyClaims,
                "a refused conversion created a pending claim — that state must exist only as a v14 artefact");
        }

        [Test]
        public void ConvertNamesEveryRefusalItCanReallyProduce()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();

            Assert.AreEqual(BenchConvertReason.NoSession,
                ProcessingBenchCommands.ValidateConvert(null, cargo, PartRecipe, 2));
            Assert.AreEqual(BenchConvertReason.NoCargo,
                ProcessingBenchCommands.ValidateConvert(bench, null, PartRecipe, 2));
            Assert.AreEqual(BenchConvertReason.UnknownRecipe,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, 9999, 2));
            Assert.AreEqual(BenchConvertReason.BenchEmpty,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, PartRecipe, 2));

            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 4);

            // A stale selection held across a deposit that changed the bench's type.
            int otherFamilyRecipe = 0;
            Assert.AreNotEqual(RawId, ProcessingCatalog.Recipes[otherFamilyRecipe].InputItemId);
            Assert.AreEqual(BenchConvertReason.RecipeInputMismatch,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, otherFamilyRecipe, 2));

            Assert.AreEqual(BenchConvertReason.InvalidAmount,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, PartRecipe, 0));
            Assert.AreEqual(BenchConvertReason.InvalidAmount,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, PartRecipe, -2));
            Assert.AreEqual(BenchConvertReason.InsufficientBenchInput,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, PartRecipe, 6));
            Assert.AreEqual(BenchConvertReason.None,
                ProcessingBenchCommands.ValidateConvert(bench, cargo, PartRecipe, 4));

            Assert.AreEqual(4, bench.InputAmount, "a validator moved something");
            Assert.AreEqual(36f, cargo.GetStored(RawId), 0.0001f, "a validator moved the hold");
        }

        /// <summary>
        /// A double click. The second call finds a bench that no longer has the input
        /// and refuses; it cannot produce a second output from the same units, and it
        /// says which of the two happened rather than reporting a silent success.
        /// </summary>
        [Test]
        public void ASecondConvertCallCannotSpendTheSameInputTwice()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 6);

            Assert.AreEqual(BenchConvertReason.None,
                ProcessingBenchCommands.TryConvert(bench, cargo, PartRecipe, 6, out int first));
            Assert.AreEqual(BenchConvertReason.BenchEmpty,
                ProcessingBenchCommands.TryConvert(bench, cargo, PartRecipe, 6, out int second));

            Assert.AreEqual(3, first);
            Assert.AreEqual(0, second, "the second call produced output out of nothing");
            Assert.AreEqual(3f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f);
            Assert.AreEqual(1L, bench.ConversionsTotal, "the refused second call counted as a transaction");
        }

        [Test]
        public void ASecondDepositCallCannotDebitTheSameUnitsTwice()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 5f);
            var bench = new ProcessingBenchRuntime();

            Assert.AreEqual(BenchDepositReason.None,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 5));
            Assert.AreEqual(BenchDepositReason.InsufficientCargo,
                ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 5));

            Assert.AreEqual(5, bench.InputAmount, "the hold paid twice for one deposit");
            Assert.AreEqual(0f, cargo.GetStored(RawId), 0.0001f);
            Assert.AreEqual(5f, Owned(cargo, bench, RawId), 0.0001f);
        }

        // ---- conservation ------------------------------------------------------

        /// <summary>
        /// The property, at the amounts where an off-by-one would hide: exactly one
        /// unit, one below the ratio, the ratio, one above it, a large amount, and an
        /// amount close enough to the safe integer cap that a careless
        /// <c>amount * outputCount</c> would wrap.
        /// <para>
        /// Measured in raw-equivalents, the only unit both sides of a conversion share:
        /// whatever is left as input, plus the input each produced output cost. If that
        /// total ever moves, value was created or destroyed.
        /// </para>
        /// </summary>
        [Test]
        public void ConversionConservesValueAtEveryInterestingAmount()
        {
            foreach (int recipeIndex in new[] { PartRecipe, MaterialRecipe })
            {
                ProcessingRecipe recipe = ProcessingCatalog.Recipes[recipeIndex];
                int ratio = recipe.InputCount;

                foreach (int amount in new[]
                {
                    1, ratio - 1, ratio, ratio + 1, 1000,
                    ProcessingBenchCommands.MaxTransactionAmount - 1
                })
                {
                    var cargo = new LogisticsCargo(float.MaxValue);
                    var bench = new ProcessingBenchRuntime();
                    bench.Restore(RawId, amount);

                    int convertible = ProcessingBenchCommands.MaxConvertibleInput(bench, recipeIndex);
                    string label = $"{recipe.Id} at {amount}";

                    if (convertible <= 0)
                    {
                        Assert.AreNotEqual(BenchConvertReason.None,
                            ProcessingBenchCommands.ValidateConvert(bench, cargo, recipeIndex, amount),
                            label + ": an amount below the ratio was accepted");
                        continue;
                    }

                    Assert.AreEqual(BenchConvertReason.None,
                        ProcessingBenchCommands.TryConvert(bench, cargo, recipeIndex, convertible,
                            out int produced), label);

                    Assert.AreEqual(convertible / ratio * recipe.OutputCount, produced, label + ": wrong yield");
                    Assert.AreEqual(amount - convertible, bench.InputAmount, label + ": wrong remainder");

                    long rawEquivalent = bench.InputAmount
                                         + (long)produced / recipe.OutputCount * ratio;
                    Assert.AreEqual(amount, rawEquivalent, label + ": value was created or destroyed");
                    Assert.AreEqual((float)produced, cargo.GetStored(recipe.OutputItemId), 0.5f,
                        label + ": the hold and the transaction disagree about the output");
                }
            }
        }

        /// <summary>
        /// Deposit, convert, withdraw, repeated. Every cycle must land on the same
        /// total, or the bench is a slow leak in one direction.
        /// </summary>
        [Test]
        public void RepeatedDepositConvertWithdrawCyclesNeitherMintNorDestroy()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 60f);
            var bench = new ProcessingBenchRuntime();

            for (int cycle = 0; cycle < 5; cycle++)
            {
                Assert.AreEqual(BenchDepositReason.None,
                    ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 7), "cycle " + cycle);
                Assert.AreEqual(BenchConvertReason.None,
                    ProcessingBenchCommands.TryConvert(bench, cargo, PartRecipe, 6, out int produced),
                    "cycle " + cycle);
                Assert.AreEqual(3, produced, "cycle " + cycle);
                Assert.AreEqual(BenchWithdrawReason.None,
                    ProcessingBenchCommands.TryWithdrawAll(bench, cargo, out int back), "cycle " + cycle);
                Assert.AreEqual(1, back, "cycle " + cycle);

                Assert.AreEqual(60f - (cycle + 1) * 6f, cargo.GetStored(RawId), 0.0001f,
                    "cycle " + cycle + ": raw drifted");
                Assert.AreEqual((cycle + 1) * 3f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                    "cycle " + cycle + ": output drifted");
                Assert.IsTrue(bench.IsEmpty, "cycle " + cycle);
            }

            Assert.AreEqual(5L, bench.ConversionsTotal);
            Assert.AreEqual(15L, bench.BatchEquivalentsTotal);
        }

        [Test]
        public void TheMaterialRecipeConvertsAtItsOwnRatioIntoItsOwnMaterial()
        {
            LogisticsCargo cargo = Hold(999f, 40f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 9);

            Assert.AreEqual(BenchConvertReason.None,
                ProcessingBenchCommands.TryConvert(bench, cargo, MaterialRecipe, 9, out int produced));

            Assert.AreEqual(3, produced, "9 raw at 3 -> 1");
            Assert.AreEqual(3f, cargo.GetStored(MaterialId), 0.0001f);
            Assert.AreEqual(0f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the material recipe produced the part recipe's output");
        }

        // ---- progression -------------------------------------------------------

        /// <summary>
        /// One conversion worth many legacy batches pays the recipe's first-time XP
        /// exactly once — the funnel singularity the ledger enforces — and pays the
        /// same total as the same work done one batch at a time. A conversion is a
        /// player action, not an XP faucet.
        /// </summary>
        [Test]
        public void OneBigConversionAwardsExactlyWhatTheSameWorkInSmallStepsWould()
        {
            Progression.ProgressionService.Reset();

            var oneGo = new LogisticsCargo(float.MaxValue);
            var benchA = new ProcessingBenchRuntime();
            benchA.Restore(RawId, 20);
            ProcessingBenchCommands.TryConvert(benchA, oneGo, PartRecipe, 20, out _);

            long xpForOneBigConversion = Progression.ProgressionService.State.TotalXp;
            int ledgerAfterBig = CountLedger();

            Progression.ProgressionService.Reset();

            var stepped = new LogisticsCargo(float.MaxValue);
            var benchB = new ProcessingBenchRuntime();
            benchB.Restore(RawId, 20);
            for (int i = 0; i < 10; i++)
                ProcessingBenchCommands.TryConvert(benchB, stepped, PartRecipe, 2, out _);

            Assert.AreEqual(Progression.ProgressionService.State.TotalXp, xpForOneBigConversion,
                "one transaction and the same work in ten paid different XP");
            Assert.AreEqual(CountLedger(), ledgerAfterBig);
            Assert.AreEqual(1, ledgerAfterBig, "recipe:first stopped being a funnel singularity");
            Assert.AreEqual(Progression.ProgressionEvents.RecipeXp, xpForOneBigConversion,
                "the recipe paid more or less than its own award");

            Progression.ProgressionService.Reset();
        }

        [Test]
        public void ARefusedConversionAwardsNothingAndCountsNothing()
        {
            Progression.ProgressionService.Reset();

            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(RawId, 10f);
            var bench = new ProcessingBenchRuntime();
            ProcessingBenchCommands.TryDeposit(bench, cargo, RawId, 5);

            Assert.AreEqual(BenchConvertReason.AmountNotMultipleOfRecipeInput,
                ProcessingBenchCommands.TryConvert(bench, cargo, PartRecipe, 5, out _));

            Assert.AreEqual(0L, Progression.ProgressionService.State.TotalXp,
                "a refused conversion paid XP");
            Assert.AreEqual(0, CountLedger(), "a refused conversion wrote to the ledger");
            Assert.AreEqual(0L, bench.ConversionsTotal,
                "a refused conversion moved the counter presentation reads");

            Progression.ProgressionService.Reset();
        }

        private static int CountLedger()
        {
            int count = 0;
            foreach (string _ in Progression.ProgressionService.Ledger.All) count++;
            return count;
        }

        // ---- legacy claim queue ------------------------------------------------

        /// <summary>
        /// Nothing at runtime adds to the queue; it only drains, and once empty it
        /// stays empty. A claim the hold cannot take yet is preserved rather than
        /// dropped, and delivered exactly once when the room appears.
        /// </summary>
        [Test]
        public void TheLegacyClaimQueueOnlyEverDrainsAndDeliversOnce()
        {
            var cargo = new LogisticsCargo(10f);
            cargo.LoadItem(ProcessingCatalog.BasicPartId, 10f);
            var bench = new ProcessingBenchRuntime();
            bench.AddLegacyClaim(ProcessingCatalog.BasicPartId, 4);

            Assert.IsTrue(ProcessingBenchCommands.DrainLegacyClaims(bench, cargo),
                "a full hold somehow absorbed the claim");
            Assert.AreEqual(10f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f);
            Assert.IsTrue(bench.HasLegacyClaims, "the undeliverable claim was dropped");

            cargo.TryConsumeImmediate(ProcessingCatalog.BasicPartId, 6f);
            Assert.IsFalse(ProcessingBenchCommands.DrainLegacyClaims(bench, cargo));
            Assert.AreEqual(8f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the claim was not delivered in full when the room appeared");

            Assert.IsFalse(ProcessingBenchCommands.DrainLegacyClaims(bench, cargo));
            Assert.AreEqual(8f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "a second drain delivered the same claim twice");
            Assert.AreEqual(0, bench.LegacyClaims.Count, "the drained queue was not pruned");
        }
    }
}
