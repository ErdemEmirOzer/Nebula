using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Extraction
{
    /// <summary>
    /// Why input could not be moved from the hold onto the bench, in the vocabulary
    /// the real chain can actually produce.
    /// <para>
    /// Every member is a state <see cref="ProcessingBenchRuntime"/> or
    /// <see cref="LogisticsCargo"/> can genuinely be in. None is a gate invented for
    /// the interface: a refusal the backend would not enforce is a second authority
    /// wearing a refusal's clothes.
    /// </para>
    /// </summary>
    public enum BenchDepositReason
    {
        /// <summary>The deposit went through in full.</summary>
        None = 0,

        /// <summary>No live session — no ship, nothing to read.</summary>
        NoSession,

        /// <summary>There is a session but no hold behind it.</summary>
        NoCargo,

        /// <summary>Zero, negative, non-integral, NaN, or past the safe integer range.</summary>
        InvalidAmount,

        /// <summary>The item id is null/empty or names nothing the game knows.</summary>
        UnknownItem,

        /// <summary>
        /// A real item, but no recipe in <see cref="ProcessingCatalog.Recipes"/> takes
        /// it as input. Derived from the catalog, never from a hand-written list.
        /// </summary>
        ItemNotProcessable,

        /// <summary>Not enough unreserved whole units of it in the hold.</summary>
        InsufficientCargo,

        /// <summary>
        /// The bench already holds a different input item. The bench is single-type by
        /// design; the standing input is never auto-returned, so this is a refusal
        /// rather than a swap.
        /// </summary>
        DifferentInputAlreadyLoaded,

        /// <summary>The deposit would push the bench past the safe integer range.</summary>
        Overflow,
    }

    /// <summary>Why input could not be moved from the bench back to the hold.</summary>
    public enum BenchWithdrawReason
    {
        /// <summary>The withdrawal went through in full.</summary>
        None = 0,

        /// <summary>No live session.</summary>
        NoSession,

        /// <summary>There is a session but no hold behind it.</summary>
        NoCargo,

        /// <summary>Zero, negative, non-integral, NaN, or past the safe integer range.</summary>
        InvalidAmount,

        /// <summary>Nothing is on the bench at all.</summary>
        BenchEmpty,

        /// <summary>The bench holds less than the amount asked for.</summary>
        InsufficientBenchInput,

        /// <summary>
        /// The hold cannot take the whole amount back. Refused rather than partially
        /// delivered: a half-withdrawal would leave the player guessing where the rest
        /// went, and the bench is a safe place for it to stay.
        /// </summary>
        NoCargoRoom,
    }

    /// <summary>Why a conversion could not be applied.</summary>
    public enum BenchConvertReason
    {
        /// <summary>The conversion was applied in full, in one transaction.</summary>
        None = 0,

        /// <summary>No live session.</summary>
        NoSession,

        /// <summary>There is a session but no hold behind it.</summary>
        NoCargo,

        /// <summary>Zero, negative, non-integral, NaN, or past the safe integer range.</summary>
        InvalidAmount,

        /// <summary>Nothing is on the bench to convert.</summary>
        BenchEmpty,

        /// <summary>The recipe index is outside <see cref="ProcessingCatalog.Recipes"/>.</summary>
        UnknownRecipe,

        /// <summary>
        /// A real recipe, but it does not take the bench's input item. Reachable from a
        /// stale selection held across a deposit that changed the bench's type.
        /// </summary>
        RecipeInputMismatch,

        /// <summary>
        /// The amount is not a whole multiple of the recipe's input count. Refused
        /// rather than silently rounded — rounding would spend input the player did
        /// not agree to spend.
        /// </summary>
        AmountNotMultipleOfRecipeInput,

        /// <summary>The bench holds less than the amount asked for.</summary>
        InsufficientBenchInput,

        /// <summary>
        /// The hold cannot take the output. The conversion does not start, so no
        /// pending claim is ever created by this path — that state exists only as a
        /// v14 legacy artefact carried by the migration.
        /// </summary>
        OutputNoRoom,

        /// <summary>The output count would exceed the safe integer range.</summary>
        Overflow,
    }

    /// <summary>
    /// One output the v14 → v15 migration rescued and has not yet been able to hand
    /// back: a finished batch's undelivered output, or a secondary legacy slot's input
    /// that could not join the single bench because it was a different item.
    /// <para>
    /// This is a migration queue, not a second inventory authority. Nothing ever adds
    /// to it at runtime; it only drains, and once empty it stays empty for the life of
    /// the campaign.
    /// </para>
    /// </summary>
    public sealed class LegacyProcessingClaim
    {
        public string ItemId { get; }
        public int Amount { get; private set; }

        public LegacyProcessingClaim(string itemId, int amount)
        {
            ItemId = itemId;
            Amount = Mathf.Max(0, amount);
        }

        /// <summary>Reduces the claim by what was actually delivered.</summary>
        public void Deliver(int delivered) => Amount = Mathf.Max(0, Amount - Mathf.Max(0, delivered));
    }

    /// <summary>
    /// The one authority for what is on the processing bench.
    /// <para>
    /// The bench holds whole units of exactly one input item, physically moved out of
    /// the hold rather than reserved in it. That is the decisive difference from the
    /// timed model it replaces: a reservation left stock in the hold and made it
    /// unspendable, which meant two places could disagree about what the player had.
    /// Here the hold's number and the bench's number are disjoint and always sum to
    /// what the player owns.
    /// </para>
    /// <para>
    /// There is no timer, no tick and no progress. A conversion is a single
    /// transaction in <see cref="ProcessingBenchCommands.TryConvert"/>; this type only
    /// stores what the bench is holding between transactions.
    /// </para>
    /// </summary>
    public sealed class ProcessingBenchRuntime
    {
        /// <summary>Stable id, used by the save and by any owner-keyed bookkeeping.</summary>
        public const string DefaultBenchId = "processing-bench-0";

        public string BenchId { get; }

        /// <summary>The single input item on the bench, or null when it is empty.</summary>
        public string InputItemId { get; private set; }

        /// <summary>Whole units of <see cref="InputItemId"/> on the bench. Always ≥ 0.</summary>
        public int InputAmount { get; private set; }

        public bool IsEmpty => InputAmount <= 0 || string.IsNullOrEmpty(InputItemId);

        /// <summary>
        /// Monotonic count of conversions applied to this bench this session, and the
        /// number of legacy-sized batches those conversions were worth.
        /// <para>
        /// This is what replaced <c>ProcessorRuntime.OutputsProducedTotal</c> as the
        /// signal presentation reads. A rising total is the only evidence a conversion
        /// happened: the animation presenter and the audio bridge compare it against
        /// the total they last saw, so a burst of two conversions between two polls is
        /// one piece of feedback rather than two, and a reload — which starts a fresh
        /// runtime at zero — replays nothing.
        /// </para>
        /// <para>
        /// Session-local and deliberately not saved. Persisting it would make the
        /// counter a second, weaker record of a transaction whose real effect (input
        /// off the bench, output in the hold) the save already carries in full.
        /// </para>
        /// </summary>
        public long ConversionsTotal { get; private set; }

        /// <inheritdoc cref="ConversionsTotal"/>
        public long BatchEquivalentsTotal { get; private set; }

        /// <summary>The recipe of the last applied conversion, for presentation. Null before the first.</summary>
        public string LastRecipeId { get; private set; }

        /// <summary>The catalog index of the last applied conversion, or -1 before the first.</summary>
        public int LastRecipeIndex { get; private set; } = -1;

        public string LastInputItemId { get; private set; }
        public string LastOutputItemId { get; private set; }
        public int LastInputConsumed { get; private set; }
        public int LastOutputProduced { get; private set; }

        private readonly List<LegacyProcessingClaim> legacyClaims = new List<LegacyProcessingClaim>();

        /// <summary>
        /// Outputs and secondary inputs the v14 migration rescued and still owes the
        /// player. Empty in every campaign that never loaded a v14 save.
        /// </summary>
        public IReadOnlyList<LegacyProcessingClaim> LegacyClaims => legacyClaims;

        public bool HasLegacyClaims
        {
            get
            {
                for (int i = 0; i < legacyClaims.Count; i++)
                    if (legacyClaims[i].Amount > 0) return true;
                return false;
            }
        }

        public ProcessingBenchRuntime(string benchId = DefaultBenchId)
        {
            BenchId = string.IsNullOrEmpty(benchId) ? DefaultBenchId : benchId;
        }

        /// <summary>
        /// Writes the bench's contents directly. Restore and migration only — every
        /// gameplay path goes through <see cref="ProcessingBenchCommands"/>, which is
        /// where the cargo side of each transaction lives.
        /// </summary>
        public void Restore(string inputItemId, int inputAmount)
        {
            if (string.IsNullOrEmpty(inputItemId) || inputAmount <= 0)
            {
                InputItemId = null;
                InputAmount = 0;
                return;
            }
            InputItemId = inputItemId;
            InputAmount = inputAmount;
        }

        /// <summary>Adds whole units of the bench's item. Caller has already validated type and range.</summary>
        internal void AddInput(string itemId, int amount)
        {
            if (amount <= 0) return;
            InputItemId = itemId;
            InputAmount += amount;
        }

        /// <summary>Removes whole units. Clears the item id when the bench empties.</summary>
        internal void RemoveInput(int amount)
        {
            if (amount <= 0) return;
            InputAmount = Mathf.Max(0, InputAmount - amount);
            if (InputAmount == 0) InputItemId = null;
        }

        /// <summary>
        /// Records a conversion that has already been applied in full.
        /// <para>
        /// Called from <see cref="ProcessingBenchCommands.TryConvert"/> after both
        /// sides of the transaction have committed, never before — a counter that rose
        /// on an attempt rather than on a success is exactly how a refused conversion
        /// ends up playing a completion sound.
        /// </para>
        /// </summary>
        internal void RecordConversion(int recipeIndex, ProcessingRecipe recipe,
            int inputConsumed, int outputProduced)
        {
            if (recipe == null || outputProduced <= 0) return;
            ConversionsTotal++;
            BatchEquivalentsTotal += recipe.InputCount > 0 ? inputConsumed / recipe.InputCount : 1;
            LastRecipeIndex = recipeIndex;
            LastRecipeId = recipe.Id;
            LastInputItemId = recipe.InputItemId;
            LastOutputItemId = recipe.OutputItemId;
            LastInputConsumed = inputConsumed;
            LastOutputProduced = outputProduced;
        }

        /// <summary>Migration only: queues an output/input the single bench could not absorb.</summary>
        public void AddLegacyClaim(string itemId, int amount)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0) return;
            for (int i = 0; i < legacyClaims.Count; i++)
            {
                if (legacyClaims[i].ItemId != itemId) continue;
                legacyClaims[i] = new LegacyProcessingClaim(itemId, legacyClaims[i].Amount + amount);
                return;
            }
            legacyClaims.Add(new LegacyProcessingClaim(itemId, amount));
        }

        /// <summary>Drops every fully-delivered claim. The queue empties and stays empty.</summary>
        internal void PruneLegacyClaims() => legacyClaims.RemoveAll(c => c.Amount <= 0);

        internal void ClearLegacyClaims() => legacyClaims.Clear();

        /// <summary>Wipes the bench. Load path only — a fresh restore is about to write the truth.</summary>
        public void Clear()
        {
            InputItemId = null;
            InputAmount = 0;
            legacyClaims.Clear();
            ConversionsTotal = 0;
            BatchEquivalentsTotal = 0;
            LastRecipeIndex = -1;
            LastRecipeId = null;
            LastInputItemId = null;
            LastOutputItemId = null;
            LastInputConsumed = 0;
            LastOutputProduced = 0;
        }
    }

    /// <summary>
    /// The one production entry point for moving resources on and off the bench and
    /// for converting them.
    /// <para>
    /// Every mutating method validates in full before it changes anything, so a
    /// refusal never leaves half a transaction behind. The validators are pure: a page
    /// may call them every refresh without spending, moving or reserving anything.
    /// </para>
    /// <para>
    /// Both the drag/drop path and the keyboard/gamepad button path come here. Two
    /// entry points writing their own transaction is how the hold and the bench end up
    /// disagreeing, so there is exactly one.
    /// </para>
    /// </summary>
    public static class ProcessingBenchCommands
    {
        /// <summary>
        /// The largest amount any single command will accept. Well inside int range so
        /// that <c>amount * recipe.OutputCount</c> and <c>bench + amount</c> cannot
        /// overflow even at the extremes, which is what lets the checked arithmetic
        /// below be a guarantee rather than a hope.
        /// </summary>
        public const int MaxTransactionAmount = 1_000_000_000;

        private const float Epsilon = 0.0001f;

        // -- Catalog-derived questions ------------------------------------------

        /// <summary>
        /// Whether any recipe takes this item as input. Derived from the catalog, so a
        /// recipe added later makes its input processable with no UI change.
        /// </summary>
        public static bool IsProcessable(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            ProcessingRecipe[] recipes = ProcessingCatalog.Recipes;
            for (int i = 0; i < recipes.Length; i++)
                if (recipes[i].InputItemId == itemId) return true;
            return false;
        }

        /// <summary>
        /// Every recipe index taking this item as input, in the catalog's own stable
        /// order. The page draws its cards from this and never sorts or filters by
        /// display name.
        /// </summary>
        public static List<int> RecipeIndicesFor(string inputItemId)
        {
            var indices = new List<int>();
            if (string.IsNullOrEmpty(inputItemId)) return indices;
            ProcessingRecipe[] recipes = ProcessingCatalog.Recipes;
            for (int i = 0; i < recipes.Length; i++)
                if (recipes[i].InputItemId == inputItemId) indices.Add(i);
            return indices;
        }

        /// <summary>The recipe at an index, or null when the index names nothing.</summary>
        public static ProcessingRecipe RecipeAt(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < ProcessingCatalog.Recipes.Length
                ? ProcessingCatalog.Recipes[recipeIndex]
                : null;

        /// <summary>
        /// Whole units of an item the hold can actually give up right now: physically
        /// stored, minus anything reserved by another owner, floored.
        /// <para>
        /// Floored rather than rounded because a bench deals in whole units and the
        /// hold does not: extraction can leave 7.4 metal in the hold. Rounding up would
        /// invent 0.6 of a unit; the fraction simply stays in the hold, visible, rather
        /// than being silently destroyed.
        /// </para>
        /// </summary>
        public static int WholeUnitsAvailable(LogisticsCargo cargo, string itemId)
        {
            if (cargo == null || string.IsNullOrEmpty(itemId)) return 0;
            float available = cargo.GetAvailable(itemId);
            if (float.IsNaN(available) || available <= 0f) return 0;
            float floored = Mathf.Floor(available + Epsilon);
            if (floored >= MaxTransactionAmount) return MaxTransactionAmount;
            return Mathf.Max(0, (int)floored);
        }

        /// <summary>
        /// The largest amount of bench input this recipe could consume: the bench
        /// amount rounded down to a whole multiple of the recipe's input count.
        /// </summary>
        public static int MaxConvertibleInput(ProcessingBenchRuntime bench, int recipeIndex)
        {
            ProcessingRecipe recipe = RecipeAt(recipeIndex);
            if (bench == null || recipe == null || recipe.InputCount <= 0) return 0;
            if (bench.IsEmpty || bench.InputItemId != recipe.InputItemId) return 0;
            return bench.InputAmount / recipe.InputCount * recipe.InputCount;
        }

        /// <summary>How many outputs a given input amount yields. Exact, never rounded up.</summary>
        public static int OutputFor(ProcessingRecipe recipe, int inputAmount)
        {
            if (recipe == null || recipe.InputCount <= 0 || inputAmount <= 0) return 0;
            return inputAmount / recipe.InputCount * recipe.OutputCount;
        }

        /// <summary>A whole, positive, in-range amount. NaN and fractional callers land here.</summary>
        private static bool IsValidAmount(int amount) =>
            amount > 0 && amount <= MaxTransactionAmount;

        /// <summary>
        /// Turns a number a text field or a slider produced into the whole units a
        /// command will accept, and says whether it was one.
        /// <para>
        /// The commands take <c>int</c>, which means a caller that casts is the caller
        /// that decides what happens to <c>2.7</c> — and a silent <c>(int)</c> cast
        /// decides "spend 2 and destroy 0.7" without telling anyone. This is the one
        /// place that question is answered: <c>2.7</c>, <c>NaN</c>, <c>∞</c>, <c>0</c>,
        /// a negative, and anything past <see cref="MaxTransactionAmount"/> are all
        /// refused outright, so a fraction can never be rounded into a spend.
        /// </para>
        /// </summary>
        public static bool TryWholeUnits(double amount, out int whole)
        {
            whole = 0;
            if (double.IsNaN(amount) || double.IsInfinity(amount)) return false;
            if (amount <= 0d || amount > MaxTransactionAmount) return false;
            if (System.Math.Abs(amount - System.Math.Round(amount)) > Epsilon) return false;

            whole = (int)System.Math.Round(amount);
            return whole > 0 && whole <= MaxTransactionAmount;
        }

        // -- Deposit -------------------------------------------------------------

        /// <summary>
        /// Whether this deposit could happen right now, and if not, why. Reads only.
        /// </summary>
        public static BenchDepositReason ValidateDeposit(ProcessingBenchRuntime bench,
            LogisticsCargo cargo, string itemId, int amount)
        {
            if (bench == null) return BenchDepositReason.NoSession;
            if (cargo == null) return BenchDepositReason.NoCargo;
            if (string.IsNullOrEmpty(itemId)) return BenchDepositReason.UnknownItem;
            if (!IsValidAmount(amount)) return BenchDepositReason.InvalidAmount;
            if (!IsProcessable(itemId)) return BenchDepositReason.ItemNotProcessable;

            if (!bench.IsEmpty && bench.InputItemId != itemId)
                return BenchDepositReason.DifferentInputAlreadyLoaded;

            if (WholeUnitsAvailable(cargo, itemId) < amount)
                return BenchDepositReason.InsufficientCargo;

            if ((long)bench.InputAmount + amount > MaxTransactionAmount)
                return BenchDepositReason.Overflow;

            return BenchDepositReason.None;
        }

        /// <summary>
        /// Moves whole units out of the hold and onto the bench in one transaction, or
        /// explains why it did not.
        /// <para>
        /// Validation runs first and the move only happens when it passed, so a refusal
        /// never half-deposits. The hold's removal and the bench's increase happen
        /// together; there is no window in which the units exist in both places or in
        /// neither.
        /// </para>
        /// </summary>
        public static BenchDepositReason TryDeposit(ProcessingBenchRuntime bench,
            LogisticsCargo cargo, string itemId, int amount)
        {
            BenchDepositReason reason = ValidateDeposit(bench, cargo, itemId, amount);
            if (reason != BenchDepositReason.None) return reason;

            // The hold is the authority on its own stock: if it declines, nothing has
            // moved and the bench is not touched.
            if (!cargo.TryConsumeImmediate(itemId, amount))
                return BenchDepositReason.InsufficientCargo;

            bench.AddInput(itemId, amount);
            return BenchDepositReason.None;
        }

        // -- Withdraw ------------------------------------------------------------

        /// <summary>Whether this withdrawal could happen right now, and if not, why. Reads only.</summary>
        public static BenchWithdrawReason ValidateWithdraw(ProcessingBenchRuntime bench,
            LogisticsCargo cargo, int amount)
        {
            if (bench == null) return BenchWithdrawReason.NoSession;
            if (cargo == null) return BenchWithdrawReason.NoCargo;
            if (!IsValidAmount(amount)) return BenchWithdrawReason.InvalidAmount;
            if (bench.IsEmpty) return BenchWithdrawReason.BenchEmpty;
            if (amount > bench.InputAmount) return BenchWithdrawReason.InsufficientBenchInput;

            if (!FitsInHold(cargo, bench.InputItemId, amount)) return BenchWithdrawReason.NoCargoRoom;

            return BenchWithdrawReason.None;
        }

        /// <summary>
        /// Whether the hold has room for this many whole units, measured the way
        /// <see cref="LogisticsCargo.Add"/> will actually measure it.
        /// <para>
        /// Signed <c>Capacity - UsedVolume</c> rather than
        /// <see cref="LogisticsCargo.AvailableVolume"/>: that property clamps at zero,
        /// which is right for "how much can I put in" and wrong here, because a hold
        /// already past capacity and one exactly at it both read as zero free.
        /// </para>
        /// </summary>
        private static bool FitsInHold(LogisticsCargo cargo, string itemId, int amount)
        {
            if (amount <= 0) return true;
            float needed = amount * LogisticsCargo.VolumePerUnit(itemId);
            float headroom = cargo.Capacity - cargo.UsedVolume;
            return headroom >= needed - Epsilon;
        }

        /// <summary>
        /// Moves whole units back off the bench into the hold in one transaction, or
        /// explains why it did not. All-or-nothing: a refusal changes neither side.
        /// </summary>
        public static BenchWithdrawReason TryWithdraw(ProcessingBenchRuntime bench,
            LogisticsCargo cargo, int amount)
        {
            BenchWithdrawReason reason = ValidateWithdraw(bench, cargo, amount);
            if (reason != BenchWithdrawReason.None) return reason;

            string itemId = bench.InputItemId;
            float delivered = cargo.Add(itemId, amount);

            // The validator measured the hold the same way Add does, so a short
            // delivery means the two have drifted apart. Put back exactly what was
            // delivered and refuse, rather than letting the difference evaporate.
            if (delivered < amount - Epsilon)
            {
                if (delivered > 0f) cargo.TryConsumeImmediate(itemId, delivered);
                return BenchWithdrawReason.NoCargoRoom;
            }

            bench.RemoveInput(amount);
            return BenchWithdrawReason.None;
        }

        /// <summary>
        /// Moves everything off the bench, or explains why it did not.
        /// <paramref name="withdrawn"/> is what actually moved — zero on any refusal.
        /// </summary>
        public static BenchWithdrawReason TryWithdrawAll(ProcessingBenchRuntime bench,
            LogisticsCargo cargo, out int withdrawn)
        {
            withdrawn = 0;
            if (bench == null) return BenchWithdrawReason.NoSession;
            if (bench.IsEmpty) return BenchWithdrawReason.BenchEmpty;

            int amount = bench.InputAmount;
            BenchWithdrawReason reason = TryWithdraw(bench, cargo, amount);
            if (reason == BenchWithdrawReason.None) withdrawn = amount;
            return reason;
        }

        // -- Convert -------------------------------------------------------------

        /// <summary>Whether this conversion could happen right now, and if not, why. Reads only.</summary>
        public static BenchConvertReason ValidateConvert(ProcessingBenchRuntime bench,
            LogisticsCargo cargo, int recipeIndex, int inputAmount)
        {
            if (bench == null) return BenchConvertReason.NoSession;
            if (cargo == null) return BenchConvertReason.NoCargo;

            ProcessingRecipe recipe = RecipeAt(recipeIndex);
            if (recipe == null) return BenchConvertReason.UnknownRecipe;
            if (bench.IsEmpty) return BenchConvertReason.BenchEmpty;
            if (recipe.InputItemId != bench.InputItemId) return BenchConvertReason.RecipeInputMismatch;
            if (!IsValidAmount(inputAmount)) return BenchConvertReason.InvalidAmount;

            if (recipe.InputCount <= 0) return BenchConvertReason.UnknownRecipe;
            if (inputAmount % recipe.InputCount != 0)
                return BenchConvertReason.AmountNotMultipleOfRecipeInput;

            if (inputAmount > bench.InputAmount) return BenchConvertReason.InsufficientBenchInput;

            long output = (long)(inputAmount / recipe.InputCount) * recipe.OutputCount;
            if (output <= 0 || output > MaxTransactionAmount) return BenchConvertReason.Overflow;

            if (!OutputWouldFit(cargo, recipe, (int)output)) return BenchConvertReason.OutputNoRoom;

            return BenchConvertReason.None;
        }

        /// <summary>
        /// Whether the hold can take the output.
        /// <para>
        /// Unlike the timed model this replaces, the input is already off the hold — it
        /// is sitting on the bench — so there is no volume for it to free. The output
        /// is measured against the hold's real headroom exactly as it stands.
        /// </para>
        /// </summary>
        private static bool OutputWouldFit(LogisticsCargo cargo, ProcessingRecipe recipe, int outputCount)
        {
            float needed = outputCount * LogisticsCargo.VolumePerUnit(recipe.OutputItemId);
            float headroom = cargo.Capacity - cargo.UsedVolume;
            return headroom >= needed - Epsilon;
        }

        /// <summary>
        /// Applies a conversion in one atomic transaction, or explains why it did not.
        /// <paramref name="outputProduced"/> is what reached the hold — zero on any
        /// refusal.
        /// <para>
        /// Every gate is checked before anything moves. The bench is only debited once
        /// the output has actually landed in the hold, and a short delivery rolls the
        /// whole thing back, so there is no state in which input was spent and output
        /// was not received. No timer, no tick and no pending claim is created here:
        /// when the hold has no room the conversion simply does not start.
        /// </para>
        /// </summary>
        public static BenchConvertReason TryConvert(ProcessingBenchRuntime bench,
            LogisticsCargo cargo, int recipeIndex, int inputAmount, out int outputProduced)
        {
            outputProduced = 0;

            BenchConvertReason reason = ValidateConvert(bench, cargo, recipeIndex, inputAmount);
            if (reason != BenchConvertReason.None) return reason;

            ProcessingRecipe recipe = ProcessingCatalog.Recipes[recipeIndex];
            int output = inputAmount / recipe.InputCount * recipe.OutputCount;

            float delivered = cargo.Add(recipe.OutputItemId, output);
            if (delivered < output - Epsilon)
            {
                // The hold took less than it said it would. Undo the partial delivery
                // and refuse: the bench has not been touched yet, so after this the
                // total change across hold and bench is exactly zero.
                if (delivered > 0f) cargo.TryConsumeImmediate(recipe.OutputItemId, delivered);
                return BenchConvertReason.OutputNoRoom;
            }

            bench.RemoveInput(inputAmount);
            outputProduced = output;

            // Both sides have committed and nothing below can turn this back into a
            // refusal. So the counter, the progression award, and every sound and VFX
            // that reads the counter all describe a transaction that really happened —
            // and a refusal returned above, before reaching any of them.
            bench.RecordConversion(recipeIndex, recipe, inputAmount, output);
            AwardRecipeProgression(recipe, inputAmount);
            return BenchConvertReason.None;
        }

        /// <summary>
        /// Awards the recipe's first-time XP for the legacy-sized batches this
        /// conversion was worth.
        /// <para>
        /// The timed model awarded on each completed batch, so converting 6 raw at
        /// <c>2 → 1</c> used to be three award attempts against one ledger id. That
        /// ledger is what makes <c>recipe:first</c> a funnel singularity: the first
        /// attempt pays and every later one is a no-op. So the loop below runs the same
        /// attempts in the same order and stops the moment the ledger reports a
        /// duplicate — which, for a single event id, is guaranteed to be the answer for
        /// every remaining attempt as well, since the ledger only ever grows.
        /// </para>
        /// <para>
        /// XP, level, development points, the <c>Awarded</c> event and the
        /// <c>xp_awarded</c> log line are therefore identical to what N separate
        /// batches produced. The one thing that is not reproduced is N-1 duplicate log
        /// lines — and it must not be: a conversion may legitimately be worth hundreds
        /// of millions of batches, and spending a ledger lookup and a log write on each
        /// would turn one player action into a freeze.
        /// </para>
        /// <para>
        /// Awarding here rather than in <see cref="ShipProcessing"/> is deliberate:
        /// this is the one place a conversion can commit, so every caller — the page's
        /// drag path, its button path, the in-world panel — awards exactly once, and a
        /// refused conversion, which returns before this line, awards nothing at all.
        /// </para>
        /// </summary>
        private static void AwardRecipeProgression(ProcessingRecipe recipe, int inputConsumed)
        {
            if (recipe == null || recipe.InputCount <= 0) return;
            int batches = inputConsumed / recipe.InputCount;
            if (batches <= 0) return;

            string eventId = Progression.ProgressionEvents.RecipeFirst(recipe.Id);
            for (int i = 0; i < batches; i++)
            {
                Progression.AwardResult result = Progression.ProgressionService.Award(
                    eventId, Progression.ProgressionEvents.RecipeXp);
                if (!result.Awarded) return;
            }
        }

        // -- Legacy migration drain ----------------------------------------------

        /// <summary>
        /// Hands back as much of the v14 migration queue as the hold can take, and
        /// reports whether anything is still owed.
        /// <para>
        /// Called when the hold might have gained room — after a withdrawal, a
        /// conversion, or a page refresh. Nothing ever enters the queue at runtime, so
        /// this only ever drains it.
        /// </para>
        /// </summary>
        public static bool DrainLegacyClaims(ProcessingBenchRuntime bench, LogisticsCargo cargo)
        {
            if (bench == null || cargo == null) return bench != null && bench.HasLegacyClaims;

            IReadOnlyList<LegacyProcessingClaim> claims = bench.LegacyClaims;
            for (int i = 0; i < claims.Count; i++)
            {
                LegacyProcessingClaim claim = claims[i];
                if (claim.Amount <= 0) continue;
                float delivered = cargo.Add(claim.ItemId, claim.Amount);
                int wholeDelivered = Mathf.FloorToInt(delivered + Epsilon);

                // Only whole units are ever claimed, so a fractional delivery would be
                // the hold rounding. Give back the fraction rather than record a
                // delivery that did not happen.
                if (delivered - wholeDelivered > Epsilon)
                    cargo.TryConsumeImmediate(claim.ItemId, delivered - wholeDelivered);

                claim.Deliver(wholeDelivered);
            }

            bench.PruneLegacyClaims();
            return bench.HasLegacyClaims;
        }
    }

    /// <summary>
    /// What a v14 → v15 processing migration actually did, for the load log and for
    /// the tests that hold it to the design's table.
    /// </summary>
    public struct ProcessingMigrationReport
    {
        /// <summary>False when the save carried no legacy slot lines at all.</summary>
        public bool HadLegacySlots;

        public int LegacySlots;
        public int IdleSlots;
        public int ActiveJobs;
        public int PendingOutputs;

        /// <summary>How many distinct input items the old slots were holding between them.</summary>
        public int InputTypes;

        public string BenchItemId;

        /// <summary>Whole units the migration wanted on the bench, and how many the hold could actually give up.</summary>
        public int BenchAmountWanted;

        /// <inheritdoc cref="BenchAmountWanted"/>
        public int BenchAmountMoved;

        /// <summary>Whole units queued as legacy claims, and how many are still owed after the first drain.</summary>
        public int ClaimedUnits;

        /// <inheritdoc cref="ClaimedUnits"/>
        public int ClaimsStillOwed;

        /// <summary>
        /// Whole units of a non-primary legacy input that stayed exactly where they
        /// already were. A v14 reservation never removed stock from the hold, so these
        /// need no return trip — see <see cref="LegacyProcessingMigration"/>.
        /// </summary>
        public int SecondaryInputUnitsLeftInHold;

        /// <summary>Sub-unit remainders of a pending output handed straight back to the hold, and any the hold had no room for.</summary>
        public float FractionsReturnedToHold;

        /// <inheritdoc cref="FractionsReturnedToHold"/>
        public float FractionsUndeliverable;

        public string Summary =>
            !HadLegacySlots
                ? "no_legacy_slots"
                : $"slots={LegacySlots} idle={IdleSlots} active={ActiveJobs} pending={PendingOutputs} " +
                  $"inputTypes={InputTypes} benchWanted={BenchAmountWanted} benchMoved={BenchAmountMoved} " +
                  $"secondaryLeftInHold={SecondaryInputUnitsLeftInHold} " +
                  $"claimed={ClaimedUnits} stillOwed={ClaimsStillOwed} " +
                  $"fracReturned={FractionsReturnedToHold:0.###} fracUndeliverable={FractionsUndeliverable:0.###}";
    }

    /// <summary>
    /// LEGACY / MIGRATION ONLY — folds a v3–v14 timed processing bay into the v15
    /// bench, losing nothing and inventing nothing.
    /// <para>
    /// This is the whole of what survives the timed model. It reads
    /// <see cref="ProcessorDto"/>, the save DTO, and nothing else: the runtime type
    /// those slots used no longer exists, so there is no path by which this could hand
    /// a live session a running batch. Its one caller is
    /// <c>ExtractionDirector.RestoreProcessing</c>, and only for a save written before
    /// schema 15 — it is never on the production start path.
    /// </para>
    /// <para>
    /// The rules, one per v14 state (design doc §5): an idle slot leaves the bench
    /// empty; an active timed job's reservation is released and the same amount moves
    /// onto the bench, never auto-completed, so the campaign's total is unchanged; a
    /// pending output is never lost — delivered once if it fits, preserved as an
    /// explicit claim if it does not; several slots on the same input merge; several on
    /// different inputs are never silently merged — the largest becomes the bench and
    /// the rest simply stay in the hold they were never actually removed from.
    /// </para>
    /// <para>
    /// That last point is where this departs from the design doc's wording, which has
    /// the secondary inputs "returned to cargo through the recovery queue as room
    /// allows". They need no return trip: a v14 reservation left stock physically in
    /// the hold and only made it unspendable, so a secondary input is already there in
    /// the restored cargo, and adding it back through the queue would hand the player a
    /// second copy. The queue is kept for the one thing that genuinely is outside the
    /// hold — a finished batch's undelivered output.
    /// </para>
    /// </summary>
    public static class LegacyProcessingMigration
    {
        private const float Epsilon = 0.0001f;

        public static ProcessingMigrationReport Migrate(IReadOnlyList<ProcessorDto> slots,
            LogisticsCargo cargo, ProcessingBenchRuntime bench)
        {
            var report = new ProcessingMigrationReport();
            if (bench == null || slots == null || slots.Count == 0) return report;

            report.HadLegacySlots = true;
            report.LegacySlots = slots.Count;

            // Pass 1 — gather. The reservation is deliberately NOT re-established: it
            // named a slot owner that no longer exists, and the stock it locked is
            // physically in the restored hold already. Banking that amount on the bench
            // while leaving it in the hold is the one way this could mint resources, so
            // the amount is taken out of the hold as it is banked, below.
            var inputTotals = new Dictionary<string, int>();
            var pendingOwed = new List<KeyValuePair<string, float>>();

            for (int i = 0; i < slots.Count; i++)
            {
                ProcessorDto dto = slots[i];
                if (dto == null) continue;
                if (dto.recipeIndex < 0) { report.IdleSlots++; continue; }
                report.ActiveJobs++;

                if (!string.IsNullOrEmpty(dto.reservedItemId) && dto.reservedAmount > Epsilon)
                {
                    // Whole units only — the bench deals in them. A reservation was
                    // always a whole recipe input count, so a fraction here can only be
                    // float drift; it stays in the hold rather than being rounded onto
                    // the bench, where it would become a unit the player never had.
                    int whole = Mathf.FloorToInt(dto.reservedAmount + Epsilon);
                    if (whole > 0)
                    {
                        inputTotals.TryGetValue(dto.reservedItemId, out int have);
                        inputTotals[dto.reservedItemId] = have + whole;
                    }
                }

                // The batch already finished: its input is spent and this output is
                // owed. Collected now and settled after the bench move below, because
                // that move frees hold volume and a delivered claim beats an open one.
                if (!string.IsNullOrEmpty(dto.pendingItemId) && dto.pendingAmount > Epsilon)
                {
                    report.PendingOutputs++;
                    pendingOwed.Add(new KeyValuePair<string, float>(dto.pendingItemId, dto.pendingAmount));
                }
            }

            // Pass 2 — the bench's single input. The largest amount wins, so the most
            // work survives where the player can act on it immediately. Every other
            // item becomes a claim: never a silent merge into a type the player did not
            // choose, and never a deletion.
            string benchItem = null;
            int benchAmount = 0;
            foreach (KeyValuePair<string, int> kv in inputTotals)
                if (kv.Value > benchAmount) { benchItem = kv.Key; benchAmount = kv.Value; }

            report.InputTypes = inputTotals.Count;
            report.BenchItemId = benchItem;
            report.BenchAmountWanted = benchAmount;

            // Every other item is already home and needs no queue. A v14 reservation
            // never removed anything from the hold — TryReserve only made stock
            // unspendable — so a secondary input is physically in the restored cargo
            // right now. Handing it back through the recovery queue would ADD it a
            // second time; releasing the (already-gone) reservation and leaving it
            // where it is conserves it exactly. Only the primary input moves, and only
            // because the bench, unlike a reservation, really does take it out.
            foreach (KeyValuePair<string, int> kv in inputTotals)
                if (kv.Key != benchItem) report.SecondaryInputUnitsLeftInHold += kv.Value;

            if (benchItem != null && benchAmount > 0)
            {
                // Whatever the hold cannot give up — a save whose cargo shrank under it
                // — simply does not reach the bench, rather than being conjured onto it
                // in addition to whatever the hold still has.
                int available = ProcessingBenchCommands.WholeUnitsAvailable(cargo, benchItem);
                report.BenchAmountMoved = Mathf.Min(benchAmount, available);
                if (report.BenchAmountMoved > 0 && cargo != null)
                    cargo.TryConsumeImmediate(benchItem, report.BenchAmountMoved);
                bench.Restore(benchItem, report.BenchAmountMoved);
            }

            // Pass 3 — settle what the finished batches were owed. The whole part
            // becomes a claim the drain below delivers; a sub-unit remainder (only
            // reachable when a v14 hold delivered part of an output) goes straight back
            // as the float it is, because the bench cannot represent it, and neither
            // rounding it up, which mints, nor dropping it, which destroys, is honest
            // about a value the old model really did hold.
            for (int i = 0; i < pendingOwed.Count; i++)
            {
                string itemId = pendingOwed[i].Key;
                float amount = pendingOwed[i].Value;

                int whole = Mathf.FloorToInt(amount + Epsilon);
                if (whole > 0)
                {
                    bench.AddLegacyClaim(itemId, whole);
                    report.ClaimedUnits += whole;
                }

                float fraction = amount - whole;
                if (fraction <= Epsilon || cargo == null) continue;
                float returned = cargo.Add(itemId, fraction);
                report.FractionsReturnedToHold += returned;
                report.FractionsUndeliverable += fraction - returned;
            }

            ProcessingBenchCommands.DrainLegacyClaims(bench, cargo);
            foreach (LegacyProcessingClaim claim in bench.LegacyClaims)
                if (claim != null) report.ClaimsStillOwed += claim.Amount;

            return report;
        }
    }
}
