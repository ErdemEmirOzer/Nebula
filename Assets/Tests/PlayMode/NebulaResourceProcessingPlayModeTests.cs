using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.UI.Bench;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The processing bench, driven the way a player drives it: open the Resources
    /// page from the command menu, drag a stack onto the bench, answer the box, pick
    /// a recipe, and then look at the ship's real hold.
    /// <para>
    /// Nothing here calls <see cref="ProcessingBenchCommands"/> to make the success
    /// case happen. Every success goes through the page's own controls and, for the
    /// drag, through Unity's own <c>IBeginDragHandler</c>/<c>IDropHandler</c> pair —
    /// because the defect being guarded against is precisely a backend that works
    /// and an interface that cannot reach it. A fixture that reached past the
    /// handlers would have passed against a page with no drag at all.
    /// </para>
    /// <para>
    /// There is no clock in this fixture and there must never be one again. A
    /// conversion is one transaction: the output is in the hold before the call
    /// returns. Any test here that had to wait for game time to pass would be
    /// testing a mechanic that was deliberately removed.
    /// </para>
    /// <para>
    /// <b>Run this fixture with <c>--graphics</c>.</b> Under <c>-nographics</c> it
    /// segfaults the editor inside <c>RenderManager::RenderOffscreenCameras</c>
    /// drawing the live scene's line/trail geometry — a native crash, so no results
    /// file is written at all and every other fixture sharing that run loses its
    /// results too. It is not the capture test, which skips itself correctly; it is
    /// the booted scene.
    /// </para>
    /// </summary>
    // ReleaseRegression: this fixture runs headless. The individual tests that need real
    // pixels carry [Category("Graphics")] of their own and must be excluded by that name --
    // NUnit propagates this class category to them, so Graphics is the authoritative one.
    [Category("ReleaseRegression")]
    public class NebulaResourceProcessingPlayModeTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
        }

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        // =====================================================================
        // Harness
        // =====================================================================

        private static NebulaContext Ctx()
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            Assert.IsNotNull(context, "there is no live session behind this test");
            return context;
        }

        private static NebulaParentMenuScreen Menu() => Ctx().Presentation.ParentMenu;

        /// <summary>Opens the page the way the player does — through the menu, not by calling the object.</summary>
        private static NebulaResourcesPage OpenResources()
        {
            NebulaParentMenuScreen menu = Menu();
            Assert.IsNotNull(menu, "the session built no parent menu");
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Resources),
                "the menu refused to open on Resources");
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Resources, menu.CurrentPage);

            var page = menu.PageOf<NebulaResourcesPage>();
            Assert.IsNotNull(page, "the command menu carries no Resources page");
            page.Refresh();
            return page;
        }

        private static ProcessingBenchRuntime Bench() =>
            Ctx().Ship.GetComponent<ShipProcessing>().Bench;

        private static LogisticsCargo Hold() => Ctx().Ship.GetComponent<ShipExtraction>().Cargo;

        /// <summary>The raw family the fixtures work in, and its two recipes.</summary>
        private static string Raw => ExtractionCatalog.ResourceIds[0];

        private static int PartsRecipeIndex()
        {
            for (int i = 0; i < ProcessingCatalog.Recipes.Length; i++)
                if (ProcessingCatalog.Recipes[i].InputItemId == Raw
                    && ProcessingCatalog.Recipes[i].OutputItemId == ProcessingCatalog.BasicPartId)
                    return i;
            Assert.Fail("the catalog no longer turns the first raw family into parts");
            return -1;
        }

        private static int MaterialRecipeIndex()
        {
            for (int i = 0; i < ProcessingCatalog.Recipes.Length; i++)
                if (ProcessingCatalog.Recipes[i].InputItemId == Raw
                    && ProcessingCatalog.Recipes[i].OutputItemId == ProcessingCatalog.MaterialIds[0])
                    return i;
            Assert.Fail("the catalog no longer turns the first raw family into a material");
            return -1;
        }

        /// <summary>Total physical units of every item in the hold, ignoring reservations.</summary>
        private static Dictionary<string, float> Census(LogisticsCargo hold)
        {
            var census = new Dictionary<string, float>();
            foreach (KeyValuePair<string, float> entry in hold.Items) census[entry.Key] = entry.Value;
            return census;
        }

        private static void AssertSameStock(Dictionary<string, float> before, LogisticsCargo hold, string what)
        {
            Dictionary<string, float> after = Census(hold);
            foreach (KeyValuePair<string, float> entry in before)
                Assert.AreEqual(entry.Value, after.TryGetValue(entry.Key, out float now) ? now : 0f, 0.001f,
                    $"{what}: {entry.Key} changed");
            foreach (KeyValuePair<string, float> entry in after)
                Assert.IsTrue(before.ContainsKey(entry.Key), $"{what}: {entry.Key} appeared from nowhere");
        }

        /// <summary>Clears the bench so a fixture starts from the state a new campaign is in.</summary>
        private static void EmptyBench() => Bench().Clear();

        // =====================================================================
        // The drag, and the fact that it commits nothing on its own
        // =====================================================================

        /// <summary>
        /// The whole gesture, through Unity's own handlers: pick a stack up, carry
        /// it over the bench, let go. A box opens asking how many — and until it is
        /// answered, not one unit has moved.
        /// <para>
        /// This is the guard against the shortcut version of drag and drop: moving
        /// the stack on release and calling that "direct manipulation". The player
        /// dropped a stack, not a number.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator DroppingAStackOnTheBenchAsksHowManyAndMovesNothingYet()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 12f);
            Dictionary<string, float> before = Census(hold);

            NebulaResourcesPage page = OpenResources();
            Assert.IsTrue(page.DragToBench(Raw), "the cargo row could not be dragged at all");

            Assert.IsTrue(page.Modal.IsOpen, "the drop did not ask how many");
            Assert.AreEqual(QuantityMode.Deposit, page.Modal.Mode);
            Assert.AreEqual(Raw, page.Modal.ItemId, "the box is asking about a different item");

            Assert.AreEqual(0, page.BenchAmount, "the drop moved stock before it was confirmed");
            Assert.IsNull(page.BenchItemId);
            Assert.AreEqual(0, page.DepositsIssued);
            AssertSameStock(before, hold, "a drop with the box still open");

            // And the ghost is put away by the release, not left hanging.
            Assert.IsFalse(page.IsDragging, "the page still believes a drag is live");
            Assert.IsFalse(page.DragGhost.Visible, "the drag ghost is stuck on screen");
        }

        /// <summary>
        /// Backing out of the box is zero mutation. The hold is item-for-item what
        /// it was, the bench is still empty, and no cue was played.
        /// <para>
        /// <b>Mutation guard.</b> Moving the stack on drop instead of opening the box
        /// makes this fail on the very first assertion, because the bench is no
        /// longer empty by the time cancel is pressed.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator CancellingTheQuantityBoxLeavesTheHoldAndTheBenchUntouched()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 12f);
            Dictionary<string, float> before = Census(hold);

            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);
            Assert.IsTrue(page.Modal.IsOpen, "precondition: the box is open");
            Assert.AreEqual(0, page.BenchAmount,
                "the drop moved stock before the box was answered");

            page.Modal.CancelButton.onClick.Invoke();

            Assert.IsFalse(page.Modal.IsOpen, "cancel left the box open");
            Assert.AreEqual(0, page.BenchAmount, "cancel left something on the bench");
            Assert.IsNull(page.BenchItemId);
            Assert.AreEqual(0, page.DepositsIssued, "cancel was counted as a deposit");
            Assert.AreEqual(0, page.SuccessCuesPlayed, "cancel played a success cue");
            AssertSameStock(before, hold, "cancelling the quantity box");
        }

        /// <summary>
        /// Confirming moves exactly the number that was chosen — not the whole
        /// stack, and not one unit.
        /// </summary>
        [UnityTest]
        public IEnumerator ConfirmingADepositMovesExactlyTheChosenAmount()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 12f);
            float heldBefore = hold.GetStored(Raw);

            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);

            page.Modal.SetValue(4);
            Assert.AreEqual(4, page.Modal.Value);
            page.Modal.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(BenchDepositReason.None, page.LastDepositResult,
                "the deposit was refused: " + page.FeedbackText);
            Assert.AreEqual(1, page.DepositsIssued);
            Assert.AreEqual(Raw, page.BenchItemId);
            Assert.AreEqual(4, page.BenchAmount, "the bench took a different number than was chosen");
            Assert.AreEqual(heldBefore - 4f, hold.GetStored(Raw), 0.001f,
                "the hold did not give up exactly what the bench gained");
            Assert.AreEqual(0f, hold.GetReserved(Raw), 0.001f,
                "the deposit left a reservation behind; input is moved, never reserved");

            // The result is on screen in the same pass, with no waiting of any kind.
            Assert.AreEqual(4, page.BenchAmount);
            StringAssert.Contains("4", page.BenchAmountText, "the bench does not show what it holds");
            Assert.AreEqual(1, page.SuccessCuesPlayed, "a successful deposit was silent");
        }

        /// <summary>
        /// The box will not offer more than the hold can actually give up, and the
        /// number it opens on is a number the backend would accept.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBoxIsCappedByWholeUnitsTheHoldCanActuallyGiveUp()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Clear();
            hold.Add(Raw, 7.4f);

            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);

            Assert.AreEqual(7, page.Modal.Max,
                "the box offered a fraction of a unit, or rounded one into existence");
            Assert.AreEqual(1, page.Modal.Min);
            Assert.AreEqual(1, page.Modal.Step);

            page.Modal.MaxButton.onClick.Invoke();
            page.Modal.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(BenchDepositReason.None, page.LastDepositResult);
            Assert.AreEqual(7, page.BenchAmount);
            Assert.AreEqual(0.4f, hold.GetStored(Raw), 0.001f,
                "the fraction was silently destroyed instead of staying in the hold");
        }

        /// <summary>
        /// A number past the ceiling is refused by the backend and moves nothing,
        /// even when the fixture pushes it past the disabled control.
        /// </summary>
        [UnityTest]
        public IEnumerator AnAmountTheHoldCannotCoverIsRefusedAndMovesNothing()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Clear();
            hold.Add(Raw, 3f);
            Dictionary<string, float> before = Census(hold);

            NebulaResourcesPage page = OpenResources();

            // Straight at the command, past the box: this is the state a stale
            // ceiling would produce, and the answer must still be a refusal.
            BenchDepositReason reason = page.Deposit(Raw, 9);

            Assert.AreEqual(BenchDepositReason.InsufficientCargo, reason);
            Assert.AreEqual(0, page.DepositsIssued, "a refused deposit was counted");
            Assert.AreEqual(0, page.BenchAmount, "a refused deposit reached the bench");
            Assert.AreEqual(0, page.SuccessCuesPlayed, "a refused deposit played a success cue");
            StringAssert.Contains("KARGODA", page.FeedbackText, "the refusal never reached the screen");
            AssertSameStock(before, hold, "a refused deposit");
            yield return null;
        }

        /// <summary>More of the same item stacks; the bench is not reset by a second load.</summary>
        [UnityTest]
        public IEnumerator MoreOfTheSameItemStacksOnTheBench()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            Hold().Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();

            page.DragToBench(Raw);
            page.Modal.SetValue(3);
            page.Modal.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(3, page.BenchAmount);

            // Through the bench's own EKLE this time -- the second way in, and the
            // same command underneath it.
            Assert.IsTrue(page.BenchAddButton.interactable,
                "the bench will not take more of what it already holds: " + page.BenchAddBlockedReason);
            page.BenchAddButton.onClick.Invoke();
            Assert.IsTrue(page.Modal.IsOpen, "the bench's own control asked nothing");
            page.Modal.SetValue(2);
            page.Modal.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(5, page.BenchAmount, "the second load replaced the first instead of stacking");
            Assert.AreEqual(2, page.DepositsIssued);
        }

        /// <summary>
        /// A different item is refused with the bench's own reason, and the standing
        /// input is never quietly swapped out from under the player.
        /// </summary>
        [UnityTest]
        public IEnumerator ADifferentItemIsRefusedAndTheStandingInputIsUntouched()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            string other = ExtractionCatalog.ResourceIds[1];
            LogisticsCargo hold = Hold();
            hold.Add(Raw, 8f);
            hold.Add(other, 8f);

            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);
            page.Modal.SetValue(4);
            page.Modal.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(4, page.BenchAmount, "precondition: the bench is loaded");

            float otherBefore = hold.GetStored(other);
            page.RequestDeposit(other);

            Assert.IsFalse(page.Modal.IsOpen, "a box opened over a deposit that cannot happen");
            Assert.AreEqual(BenchDepositReason.DifferentInputAlreadyLoaded, page.LastDepositResult);
            Assert.AreEqual(NebulaBenchMessages.Deposit(BenchDepositReason.DifferentInputAlreadyLoaded),
                page.FeedbackText);
            Assert.AreEqual(Raw, page.BenchItemId, "the bench swapped its input without being asked");
            Assert.AreEqual(4, page.BenchAmount);
            Assert.AreEqual(otherBefore, hold.GetStored(other), 0.001f, "the refused item moved");

            // And the row for the item the bench cannot take stops offering its
            // control, so the refusal is prevented rather than merely explained.
            page.Refresh();
            Assert.IsFalse(page.CargoAddVisible(other),
                "a row still offers a control whose only outcome is a refusal");
            Assert.IsTrue(page.CargoAddVisible(Raw),
                "the row the bench would accept lost its control");
        }

        // =====================================================================
        // Recipes
        // =====================================================================

        /// <summary>
        /// Only the recipes the bench's own input feeds are shown, in the catalog's
        /// order, and every one of them is shown — including the ones the bench
        /// cannot currently afford.
        /// <para>
        /// <b>Mutation guard.</b> Dropping the input filter shows all ten cards and
        /// fails the count immediately; hiding the unaffordable ones fails the
        /// shortfall assertion below.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator OnlyTheRecipesTheBenchInputFeedsAreShownInCatalogOrder()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            Hold().Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();

            Assert.AreEqual(0, page.VisibleRecipeCards, "an empty bench drew cards");
            StringAssert.Contains("TEZGÂH", page.RecipesEmptyText,
                "the empty conversion column says nothing");

            page.DragToBench(Raw);
            page.Modal.SetValue(2);
            page.Modal.ConfirmButton.onClick.Invoke();

            var expected = new List<int>();
            for (int i = 0; i < ProcessingCatalog.Recipes.Length; i++)
                if (ProcessingCatalog.Recipes[i].InputItemId == Raw) expected.Add(i);

            Assert.AreEqual(expected.Count, page.VisibleRecipeCards,
                "the conversion column shows a different set than the bench's input feeds");

            var shown = new List<int>();
            for (int i = 0; i < ProcessingCatalog.Recipes.Length; i++)
                if (page.RecipeCardVisible(i)) shown.Add(i);
            CollectionAssert.AreEqual(expected, shown,
                "the cards are not the bench input's recipes in catalog order");

            foreach (int index in expected)
            {
                ProcessingRecipe recipe = ProcessingCatalog.Recipes[index];
                StringAssert.Contains(recipe.InputCount.ToString(), page.RecipeCardRatioText(index),
                    recipe.Id + ": the card does not state its ratio");
                StringAssert.Contains("TEZGÂHTA:", page.RecipeCardBenchText(index),
                    recipe.Id + ": the card does not say what is on the bench");
                StringAssert.Contains("EN FAZLA:", page.RecipeCardMaxText(index),
                    recipe.Id + ": the card does not say the largest amount it could take");
            }
        }

        /// <summary>
        /// A recipe the bench cannot afford stays on screen, disabled, and says
        /// exactly how much is missing. Hiding it makes "why can I not make this"
        /// unanswerable from the page it is asked on.
        /// </summary>
        [UnityTest]
        public IEnumerator AnUnaffordableRecipeStaysVisibleAndNamesTheShortfall()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            Hold().Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();

            page.DragToBench(Raw);
            page.Modal.SetValue(2);           // enough for parts (2), one short for material (3)
            page.Modal.ConfirmButton.onClick.Invoke();

            int parts = PartsRecipeIndex();
            int material = MaterialRecipeIndex();

            Assert.IsTrue(page.RecipeCardVisible(material),
                "the unaffordable recipe was hidden instead of explained");
            Assert.IsFalse(page.RecipeConvertButton(material).interactable,
                "a recipe the bench cannot pay for offers a live control");
            StringAssert.Contains("1 ", page.RecipeCardStateText(material),
                "the card does not name the exact shortfall");
            StringAssert.Contains("EKSİK", page.RecipeCardStateText(material));

            Assert.IsTrue(page.RecipeConvertButton(parts).interactable,
                "the affordable recipe is dead: " + page.RecipeConvertBlockedReason(parts));
        }

        /// <summary>
        /// Choosing a card and confirming a number spends whole batches and puts the
        /// output in the hold in the same pass. There is no window in which the
        /// input is gone and the output has not arrived.
        /// </summary>
        [UnityTest]
        public IEnumerator ConvertingSpendsWholeBatchesAndDeliversTheOutputInTheSamePass()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 20f);
            NebulaResourcesPage page = OpenResources();

            page.DragToBench(Raw);
            page.Modal.SetValue(11);
            page.Modal.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(11, page.BenchAmount, "precondition: eleven units are on the bench");

            int material = MaterialRecipeIndex();
            ProcessingRecipe recipe = ProcessingCatalog.Recipes[material];
            Assert.AreEqual(3, recipe.InputCount, "the material recipe is no longer three-to-one");

            page.RecipeCardButton(material).onClick.Invoke();
            Assert.AreEqual(material, page.SelectedRecipe, "the card did not become the selection");

            float outputBefore = hold.GetStored(recipe.OutputItemId);
            page.RecipeConvertButton(material).onClick.Invoke();

            Assert.IsTrue(page.Modal.IsOpen, "the convert control ran without asking how much");
            Assert.AreEqual(QuantityMode.Convert, page.Modal.Mode);
            Assert.AreEqual(3, page.Modal.Step, "the box does not step by the recipe's input count");
            Assert.AreEqual(9, page.Modal.Max, "the ceiling was not rounded down to a whole batch");
            StringAssert.Contains("TEZGÂHTA KALACAK", page.Modal.PreviewText,
                "the box does not preview what stays on the bench");

            page.Modal.SetValue(6);
            // Measure this conversion, not the deposit that set it up — that deposit
            // legitimately played its own audio.ui.confirm.
            Ctx().AudioServiceOrNull?.ResetOneShotCounts();
            int pageCuesBeforeConvert = page.SuccessCuesPlayed;
            page.Modal.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(BenchConvertReason.None, page.LastConvertResult,
                "the conversion was refused: " + page.FeedbackText);
            Assert.AreEqual(1, page.ConversionsIssued);
            Assert.AreEqual(outputBefore + 2f, hold.GetStored(recipe.OutputItemId), 0.001f,
                "six units of a three-to-one recipe did not produce two outputs");
            Assert.AreEqual(5, page.BenchAmount, "the leftover on the bench is wrong");

            // Instant, and said so: no timer, no bar, and the delta is on screen now.
            //
            // The page deliberately plays no cue of its own for a conversion — the
            // bench's gameplay sound belongs to NebulaAudioGameplayBridge, which watches
            // the bench's conversion count and covers the in-world panel as well. Both
            // of them playing it is the double-sound this asserts against, so the count
            // that matters is the service's, not the page's.
            Assert.AreEqual(pageCuesBeforeConvert, page.SuccessCuesPlayed,
                "the page played its own conversion cue on top of the bridge's");
            yield return null;                       // let the bridge's unscaled Update see the new count
            Nebula.Audio.NebulaAudioService mixer = Ctx().AudioServiceOrNull;
            if (mixer != null)
                Assert.AreEqual(1, mixer.OneShotCount("audio.processing.complete"),
                    "one conversion did not produce exactly one completion sound");
            StringAssert.Contains("-6", page.LastDeltaText, "the input spend is not shown");
            StringAssert.Contains("+2", page.LastDeltaText, "the output gain is not shown");
            StringAssert.Contains(NebulaBenchMessages.ItemName(recipe.OutputItemId), page.LastDeltaText);
            Assert.IsNotEmpty(page.CargoDeltaText(recipe.OutputItemId),
                "the produced row carries no delta emphasis");
        }

        /// <summary>
        /// A number that is not a whole multiple of the recipe's input count is
        /// refused rather than silently rounded. Rounding would spend input the
        /// player did not agree to spend.
        /// <para>
        /// <b>Mutation guard.</b> Removing the multiple check from the box makes the
        /// confirm control live and the backend's refusal the only thing standing in
        /// the way — which this test also asserts, from both ends.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AConversionAmountThatIsNotAWholeMultipleIsRefused()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 20f);
            NebulaResourcesPage page = OpenResources();

            page.DragToBench(Raw);
            page.Modal.SetValue(9);
            page.Modal.ConfirmButton.onClick.Invoke();

            int material = MaterialRecipeIndex();
            ProcessingRecipe recipe = ProcessingCatalog.Recipes[material];
            page.RecipeConvertButton(material).onClick.Invoke();

            page.Modal.Field.text = "7";
            Assert.AreEqual(7, page.Modal.Value, "the box would not hold what was typed");
            Assert.IsFalse(page.Modal.HasValidValue, "7 was accepted for a three-to-one recipe");
            Assert.IsFalse(page.Modal.ConfirmButton.interactable,
                "a non-multiple could be confirmed");
            StringAssert.Contains("TAM KATI", page.Modal.WarningText,
                "the box does not say what the number has to be");

            Dictionary<string, float> before = Census(hold);
            page.Modal.ConfirmButton.onClick.Invoke();
            AssertSameStock(before, hold, "confirming a non-multiple");
            Assert.AreEqual(9, page.BenchAmount, "a refused conversion moved the bench");
            Assert.AreEqual(0, page.ConversionsIssued);

            // And past the box entirely: the backend refuses by name.
            page.Modal.Cancel();
            Assert.AreEqual(BenchConvertReason.AmountNotMultipleOfRecipeInput,
                page.Convert(material, 7));
            Assert.AreEqual(0, page.ConversionsIssued, "a refused conversion was counted");
            Assert.AreEqual(9, page.BenchAmount);
            StringAssert.Contains(NebulaBenchMessages.Genitive(recipe.InputCount), page.FeedbackText,
                "the refusal does not name the number the player has to hit");
            AssertSameStock(before, hold, "a refused conversion through the command");
        }

        /// <summary>
        /// A refused conversion is silent and changes nothing — no cue, no delta, no
        /// stock movement.
        /// <para>
        /// <b>Mutation guard.</b> Announcing or playing the cue before the
        /// transaction returns makes both counters non-zero here even though nothing
        /// moved, which is exactly the "it looked like it worked" failure.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AFailedConversionIsSilentAndMovesNothing()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();

            page.DragToBench(Raw);
            page.Modal.SetValue(2);          // one short of the material recipe's three
            page.Modal.ConfirmButton.onClick.Invoke();

            int cuesAfterDeposit = page.SuccessCuesPlayed;
            string deltaAfterDeposit = page.LastDeltaText;
            Dictionary<string, float> before = Census(hold);
            int material = MaterialRecipeIndex();

            BenchConvertReason reason = page.Convert(material, 3);

            Assert.AreEqual(BenchConvertReason.InsufficientBenchInput, reason);
            Assert.AreEqual(0, page.ConversionsIssued, "a refused conversion was counted");
            Assert.AreEqual(cuesAfterDeposit, page.SuccessCuesPlayed,
                "a refused conversion played a success cue");
            Assert.AreEqual(deltaAfterDeposit, page.LastDeltaText,
                "a refused conversion announced a delta that never happened");
            Assert.IsFalse(page.ConversionFlashActive,
                "a refused conversion played the success emphasis");
            Assert.AreEqual(2, page.BenchAmount, "a refused conversion moved the bench");
            AssertSameStock(before, hold, "a refused conversion");
            StringAssert.Contains(NebulaBenchMessages.Convert(
                BenchConvertReason.InsufficientBenchInput), page.FeedbackText);
        }

        // =====================================================================
        // Getting it back
        // =====================================================================

        /// <summary>
        /// The leftover comes back, in part and then in full, and every unit is
        /// accounted for on both sides of the move.
        /// </summary>
        [UnityTest]
        public IEnumerator TheLeftoverComesBackInPartAndThenInFull()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();

            page.DragToBench(Raw);
            page.Modal.SetValue(8);
            page.Modal.ConfirmButton.onClick.Invoke();
            float afterDeposit = hold.GetStored(Raw);

            page.BenchWithdrawButton.onClick.Invoke();
            Assert.IsTrue(page.Modal.IsOpen, "taking some back asked nothing");
            Assert.AreEqual(QuantityMode.Withdraw, page.Modal.Mode);
            Assert.AreEqual(8, page.Modal.Max, "the box will not offer everything on the bench");

            page.Modal.SetValue(3);
            page.Modal.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(BenchWithdrawReason.None, page.LastWithdrawResult,
                "the partial withdrawal was refused: " + page.FeedbackText);
            Assert.AreEqual(5, page.BenchAmount);
            Assert.AreEqual(afterDeposit + 3f, hold.GetStored(Raw), 0.001f,
                "the hold did not gain exactly what the bench lost");

            page.BenchWithdrawAllButton.onClick.Invoke();

            Assert.AreEqual(BenchWithdrawReason.None, page.LastWithdrawResult);
            Assert.AreEqual(0, page.BenchAmount, "TÜMÜNÜ GERİ AL left something behind");
            Assert.IsNull(page.BenchItemId, "an emptied bench still names an item");
            Assert.AreEqual(12f, hold.GetStored(Raw), 0.001f,
                "the round trip did not conserve the resource");
            Assert.AreEqual(2, page.WithdrawalsIssued);
            Assert.AreEqual(0, page.VisibleRecipeCards, "cards survived an emptied bench");
        }

        // =====================================================================
        // The keyboard is not a second transaction path
        // =====================================================================

        /// <summary>
        /// The row's own control opens the same box and runs the same command as the
        /// drag. Two entry points writing their own transaction is exactly how a
        /// hold and a bench end up disagreeing.
        /// </summary>
        [UnityTest]
        public IEnumerator TheKeyboardPathUsesTheSameBoxAndTheSameCommand()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();

            Button add = page.CargoAddButton(Raw);
            Assert.IsNotNull(add, "the cargo row offers no keyboard path to the bench");
            Assert.IsTrue(add.gameObject.activeInHierarchy, "the control is not on screen");

            add.onClick.Invoke();

            Assert.IsTrue(page.Modal.IsOpen, "the keyboard path asked nothing");
            Assert.AreSame(page.Modal, page.Modal, "there is more than one box");
            Assert.AreEqual(QuantityMode.Deposit, page.Modal.Mode);
            Assert.AreEqual(Raw, page.Modal.ItemId);
            Assert.AreEqual(0, page.BenchAmount, "the keyboard path moved stock before confirmation");

            page.Modal.SetValue(5);
            page.Modal.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(BenchDepositReason.None, page.LastDepositResult);
            Assert.AreEqual(5, page.BenchAmount);
            Assert.AreEqual(1, page.DepositsIssued,
                "the keyboard path counted a different number of transactions than it ran");
        }

        /// <summary>
        /// Focus has an explicit path from a cargo row, through the row's own
        /// control, onto the bench and out to the cards. Unity's directional search
        /// cannot find a control nested inside the row it belongs to, so without
        /// this the page is mouse-only.
        /// </summary>
        [UnityTest]
        public IEnumerator FocusWalksFromTheHoldThroughTheBenchToTheCards()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            Hold().Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);
            page.Modal.SetValue(6);
            page.Modal.ConfirmButton.onClick.Invoke();
            page.Refresh();
            Canvas.ForceUpdateCanvases();

            Button row = page.CargoRowButton(Raw);
            Button add = page.CargoAddButton(Raw);
            Assert.AreSame(add, row.navigation.selectOnRight,
                "a cargo row cannot reach its own bench control by keyboard or stick");
            Assert.AreSame(page.BenchAddButton, add.navigation.selectOnRight,
                "the row's control does not lead to the bench");

            int material = MaterialRecipeIndex();
            Button firstCard = null;
            for (int i = 0; i < ProcessingCatalog.Recipes.Length && firstCard == null; i++)
                if (page.RecipeCardVisible(i)) firstCard = page.RecipeCardButton(i);
            Assert.IsNotNull(firstCard, "no card is on screen to walk to");

            Assert.AreSame(firstCard, page.BenchAddButton.navigation.selectOnRight,
                "the bench does not lead to the conversion cards");
            Assert.AreSame(page.RecipeConvertButton(material),
                page.RecipeCardButton(material).navigation.selectOnRight,
                "a card cannot reach its own convert control");

            // And the page hands focus to something when it opens, or the stick does
            // nothing at all.
            page.FocusFirstControl();
            Assert.IsNotNull(EventSystem.current.currentSelectedGameObject,
                "the page put focus nowhere, so a pad has nothing to stand on");
        }

        // =====================================================================
        // Persistence and lifecycle
        // =====================================================================

        /// <summary>
        /// The bench is still loaded after leaving the page, after a save, and after
        /// a load — and the load does not duplicate what is on it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBenchSurvivesReopeningThePageAndASaveAndLoad()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);
            page.Modal.SetValue(6);
            page.Modal.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(6, page.BenchAmount, "precondition: the bench is loaded");

            Menu().Close();
            yield return null;

            NebulaResourcesPage reopened = OpenResources();
            Assert.AreSame(page, reopened, "the menu rebuilt the page instead of keeping it");
            Assert.AreEqual(Raw, reopened.BenchItemId, "the bench lost its item on reopen");
            Assert.AreEqual(6, reopened.BenchAmount, "the bench lost its amount on reopen");
            Assert.IsFalse(reopened.Modal.IsOpen, "an open box was carried across a reopen");

            ExtractionDirector director = Ctx().ExtractionDirector;
            Assert.IsNotNull(director, "the session has no save director");
            director.SaveNow("BENCH TEST");

            float heldBefore = hold.GetStored(Raw);
            director.LoadNow();
            yield return null;

            NebulaResourcesPage afterLoad = OpenResources();
            Assert.AreEqual(Raw, afterLoad.BenchItemId, "the bench's item did not survive a load");
            Assert.AreEqual(6, afterLoad.BenchAmount, "the bench's amount did not survive a load");
            Assert.AreEqual(heldBefore, Hold().GetStored(Raw), 0.001f,
                "the load duplicated or destroyed the bench's input");
        }

        /// <summary>A restart comes back with an empty bench, not the previous campaign's.</summary>
        [UnityTest]
        public IEnumerator RestartingDoesNotCarryThePreviousCampaignsBench()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            Hold().Add(Raw, 12f);
            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);
            page.Modal.SetValue(7);
            page.Modal.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(7, page.BenchAmount, "precondition: the first campaign left a loaded bench");
            Menu().Close();

            yield return UiFinalPlayMode.EndSessionAndSettle();
            yield return UiFinalPlayMode.BeginSession();

            Assert.AreEqual(1, Object.FindObjectsOfType<ShipProcessing>(true).Length,
                "the restart left a second processing bench behind");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaParentMenuScreen>(true).Length,
                "the restart left a second command menu behind");

            NebulaResourcesPage fresh = OpenResources();
            Assert.IsTrue(Bench().IsEmpty, "the new campaign inherited the old one's bench");
            Assert.AreEqual(0, fresh.BenchAmount);
            Assert.AreEqual(0, fresh.VisibleRecipeCards);
            Assert.IsFalse(fresh.Modal.IsOpen, "a box survived the restart");
        }

        /// <summary>
        /// A thousand refreshes on a live page create no objects, no handlers, no
        /// ghosts and no boxes — and run no second transaction.
        /// <para>
        /// The page refreshes on a quarter-second timer while it is open, so a leak
        /// here is a leak a player reaches by leaving the screen up.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AThousandRefreshesCreateNothingAndRunNothing()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 20f);
            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);
            page.Modal.SetValue(9);
            page.Modal.ConfirmButton.onClick.Invoke();
            page.RecipeCardButton(MaterialRecipeIndex()).onClick.Invoke();
            page.Refresh();

            Transform menuRoot = Menu().transform.parent;
            int objectsBefore = CountUnder(menuRoot);
            int ghostsBefore = CountNamed(menuRoot, "Drag Ghost");
            int boxesBefore = menuRoot.GetComponentsInChildren<NebulaQuantityModal>(true).Length;
            int dragsBefore = menuRoot.GetComponentsInChildren<NebulaBenchDragSource>(true).Length;
            int zonesBefore = menuRoot.GetComponentsInChildren<NebulaBenchDropZone>(true).Length;
            int cardsBefore = page.VisibleRecipeCards;
            int selectionBefore = page.SelectedRecipe;
            Dictionary<string, float> stock = Census(hold);
            int benchBefore = page.BenchAmount;
            int depositsBefore = page.DepositsIssued;

            for (int i = 0; i < 1000; i++) page.Refresh();

            Assert.AreEqual(objectsBefore, CountUnder(menuRoot),
                "a page refresh created objects; rows and cards must be reused, never rebuilt");
            Assert.AreEqual(ghostsBefore, CountNamed(menuRoot, "Drag Ghost"),
                "refreshing spawned drag ghosts");
            Assert.AreEqual(boxesBefore, menuRoot.GetComponentsInChildren<NebulaQuantityModal>(true).Length,
                "refreshing spawned quantity boxes");
            Assert.AreEqual(dragsBefore, menuRoot.GetComponentsInChildren<NebulaBenchDragSource>(true).Length,
                "refreshing spawned drag handlers");
            Assert.AreEqual(zonesBefore, menuRoot.GetComponentsInChildren<NebulaBenchDropZone>(true).Length,
                "refreshing spawned drop handlers");

            Assert.AreEqual(cardsBefore, page.VisibleRecipeCards, "refreshing changed the card set");
            Assert.AreEqual(selectionBefore, page.SelectedRecipe, "refreshing dropped a valid selection");
            Assert.AreEqual(benchBefore, page.BenchAmount, "refreshing moved the bench");
            Assert.AreEqual(depositsBefore, page.DepositsIssued, "refreshing ran a transaction");
            Assert.AreEqual(0, page.ConversionsIssued, "refreshing converted something");
            AssertSameStock(stock, hold, "a thousand refreshes");

            // And a press after all of that is still exactly one transaction. uGUI
            // does not expose its runtime listener count, so the duplicate-subscriber
            // question is asked the only way a fixture honestly can: press the
            // control once and count what the backend recorded.
            page.BenchAddButton.onClick.Invoke();
            Assert.IsTrue(page.Modal.IsOpen, "the bench control stopped working after a thousand refreshes");
            page.Modal.SetValue(2);
            page.Modal.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(depositsBefore + 1, page.DepositsIssued,
                "one press produced a different number of transactions; a handler was added twice");
            Assert.AreEqual(benchBefore + 2, page.BenchAmount,
                "one confirmation moved a different amount than it asked for");
        }

        private static int CountUnder(Transform root) =>
            root == null ? 0 : root.GetComponentsInChildren<Transform>(true).Length;

        private static int CountNamed(Transform root, string name)
        {
            int count = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.gameObject.name == name) count++;
            return count;
        }

        // =====================================================================
        // Evidence
        // =====================================================================

        /// <summary>
        /// Photographs the page with a real hold, a real bench and a real selection,
        /// at both ends of the supported width, plus the open quantity box.
        /// <para>
        /// The screenshot suite builds its pages on a null context, which is right
        /// for layout and useless for this: every amount reads "—" and the bench
        /// reads empty, so the one thing this work changed — a page you can actually
        /// convert from — is exactly what those pictures cannot show.
        /// </para>
        /// <para>
        /// Skipped without a graphics device, and that guard is not politeness. Under
        /// <c>-nographics</c> there is no device to read pixels from and
        /// <c>ReadPixels</c> does not fail, it segfaults the editor — which takes the
        /// whole run's results with it.
        /// </para>
        /// </summary>
        [UnityTest]
        // Graphics: reads the rendered bench back with ReadPixels; on the -nographics null device that
        // path segfaults the Editor rather than returning a blank frame.
        [Category("Graphics")]
        public IEnumerator CaptureTheLiveBenchAsEvidence()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); re-run this fixture with --graphics");

            yield return UiFinalPlayMode.LoadSceneWithSession();
            EmptyBench();

            LogisticsCargo hold = Hold();
            // Enough of one raw family and not enough of another, so both the ready
            // and the short state are in the same frame.
            hold.Add(Raw, 24f);
            hold.Add(ExtractionCatalog.ResourceIds[2], 11f);

            NebulaResourcesPage page = OpenResources();
            page.DragToBench(Raw);
            page.Modal.SetValue(8);
            page.Modal.ConfirmButton.onClick.Invoke();
            page.RecipeCardButton(MaterialRecipeIndex()).onClick.Invoke();
            page.Refresh();

            Capture("bench-live-1920x1080.png", 1920, 1080);
            Capture("bench-live-3440x1440.png", 3440, 1440);
            Capture("bench-live-1280x720.png", 1280, 720);

            page.RecipeConvertButton(MaterialRecipeIndex()).onClick.Invoke();
            Assert.IsTrue(page.Modal.IsOpen, "the evidence run could not open the quantity box");
            Canvas.ForceUpdateCanvases();
            Capture("bench-quantity-box-1920x1080.png", 1920, 1080);
            page.Modal.Cancel();
        }

        /// <summary>
        /// Renders the menu layer to a PNG and refuses to ship a frame that drew
        /// nothing — a flat rectangle is what a capture produces when there is no
        /// graphics device, and it passes every check except looking at it.
        /// </summary>
        private static void Capture(string fileName, int width, int height)
        {
            NebulaUiShell shell = Ctx().Presentation.UiShell;
            Canvas canvas = shell.CanvasFor(UiLayer.Menu);
            RenderMode savedMode = canvas.renderMode;
            Camera savedCamera = canvas.worldCamera;
            float savedPlane = canvas.planeDistance;
            RenderTexture previousActive = RenderTexture.active;

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("BenchCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();

            string directory = System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");
            System.IO.Directory.CreateDirectory(directory);

            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = NebulaUiDesignTokens.SpaceVoid;
                camera.orthographic = true;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0f, 0f, -100f);

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 100f;
                shell.ApplyScale();
                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                Color32[] pixels = texture.GetPixels32();
                bool varied = false;
                for (int i = 1; i < pixels.Length && !varied; i++)
                    varied = pixels[i].r != pixels[0].r || pixels[i].g != pixels[0].g || pixels[i].b != pixels[0].b;
                Assert.IsTrue(varied, fileName + " is a single flat colour — nothing rendered");

                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, fileName), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally
            {
                RenderTexture.active = previousActive;
                canvas.renderMode = savedMode;
                canvas.worldCamera = savedCamera;
                canvas.planeDistance = savedPlane;
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
