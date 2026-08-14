using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Bench;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The one quantity box, on its own.
    /// <para>
    /// It is worth testing away from the page because it is the single place three
    /// different transactions ask the same question, and because it is the one
    /// control in this feature that can lose the player money by being wrong: a box
    /// that rounds 7 up to 9 for a 3-input recipe spends two units the player never
    /// agreed to spend, and does it silently.
    /// </para>
    /// <para>
    /// Nothing here touches a bench or a hold, and that is the point — the box
    /// performs no transaction at all. Every assertion below is about the number it
    /// hands over and about it handing over nothing when it should not.
    /// </para>
    /// </summary>
    public class ResourceBenchUiQuantityModalTests
    {
        private NebulaUiShell shell;
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void Setup()
        {
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void Teardown()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();

            if (shell != null)
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
                shell = null;
            }
            ExclusiveModal.ResetForSession();
        }

        private NebulaQuantityModal Box()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            go.transform.SetParent(shell.Root(UiLayer.Menu), false);
            spawned.Add(go);
            var host = (RectTransform)go.transform;
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = Vector2.zero;
            host.offsetMax = Vector2.zero;
            return NebulaQuantityModal.Create(host);
        }

        private static readonly string Metal = ExtractionCatalog.ResourceIds[0];

        // =====================================================================
        // Deposit: step 1, minimum 1, ceiling is what the hold can give up
        // =====================================================================

        [Test]
        public void ADepositBoxStepsByOneAndOpensOnTheWholeStack()
        {
            NebulaQuantityModal box = Box();
            int delivered = -1;

            Assert.IsTrue(box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal,
                "KULLANILABİLİR 9", min: 1, max: 9, step: 1, null, amount => delivered = amount));

            Assert.AreEqual(1, box.Min);
            Assert.AreEqual(9, box.Max);
            Assert.AreEqual(1, box.Step);
            Assert.AreEqual(9, box.Value, "the box did not open on the largest amount it could take");
            Assert.IsTrue(box.HasValidValue);

            box.MinusButton.onClick.Invoke();
            Assert.AreEqual(8, box.Value, "the − control did not step by one");
            box.PlusButton.onClick.Invoke();
            Assert.AreEqual(9, box.Value, "the + control did not step by one");

            box.OneButton.onClick.Invoke();
            Assert.AreEqual(1, box.Value, "the 1 shortcut did not reach the minimum");
            box.MaxButton.onClick.Invoke();
            Assert.AreEqual(9, box.Value, "the MAKS shortcut did not reach the ceiling");

            box.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(9, delivered, "the box delivered a different number than it showed");
            Assert.IsFalse(box.IsOpen, "confirming left the box open");
            Assert.AreEqual(1, box.ConfirmedCount);
        }

        [Test]
        public void TheBoxWillNotStepPastEitherEnd()
        {
            NebulaQuantityModal box = Box();
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 4, 1, null, _ => { });

            for (int i = 0; i < 10; i++) box.MinusButton.onClick.Invoke();
            Assert.AreEqual(1, box.Value, "the box stepped below its own minimum");

            for (int i = 0; i < 10; i++) box.PlusButton.onClick.Invoke();
            Assert.AreEqual(4, box.Value, "the box stepped past the amount that actually exists");
        }

        // =====================================================================
        // Convert: step and minimum are the recipe's input count
        // =====================================================================

        [Test]
        public void AConversionBoxStepsByTheRecipesInputCountAndNeverRoundsUp()
        {
            ProcessingRecipe recipe = null;
            foreach (ProcessingRecipe candidate in ProcessingCatalog.Recipes)
                if (candidate.InputCount == 3) { recipe = candidate; break; }
            Assert.IsNotNull(recipe, "the catalog no longer has a three-input recipe");

            NebulaQuantityModal box = Box();
            // 11 on the bench: the largest valid amount is 9, not 11 and not 12.
            int ceiling = 11 / recipe.InputCount * recipe.InputCount;
            box.Open(QuantityMode.Convert, "DÖNÜŞTÜR", recipe.InputItemId, "TEZGÂHTA 11",
                min: recipe.InputCount, max: ceiling, step: recipe.InputCount,
                amount => $"ÇIKTI: {ProcessingBenchCommands.OutputFor(recipe, amount)}", _ => { });

            Assert.AreEqual(3, box.Step);
            Assert.AreEqual(3, box.Min);
            Assert.AreEqual(9, box.Max, "the ceiling was not rounded down to a whole batch");
            Assert.AreEqual(9, box.Value);

            box.MinusButton.onClick.Invoke();
            Assert.AreEqual(6, box.Value, "a conversion stepped by one instead of by a batch");

            // The number the player could type, but must not be able to spend.
            box.Field.text = "7";
            Assert.AreEqual(7, box.Value, "the box refused to hold what was typed");
            Assert.IsFalse(box.HasValidValue, "7 was accepted for a 3-input recipe");
            Assert.IsFalse(box.ConfirmButton.interactable, "a non-multiple could be confirmed");
            StringAssert.Contains("TAM KATI", box.WarningText,
                "the box does not say why 7 is refused");

            // And confirming anyway -- past the disabled control -- delivers nothing.
            box.Confirm();
            Assert.IsTrue(box.IsOpen, "a refused confirm closed the box");
            Assert.AreEqual(0, box.ConfirmedCount);
        }

        [Test]
        public void AConversionBoxPreviewsTheOutputAndWhatStaysOnTheBench()
        {
            NebulaQuantityModal box = Box();
            box.Open(QuantityMode.Convert, "DÖNÜŞTÜR", Metal, "TEZGÂHTA 9",
                min: 3, max: 9, step: 3,
                amount => $"ÇIKTI: {amount / 3}\nTEZGÂHTA KALACAK: {9 - amount}", _ => { });

            StringAssert.Contains("ÇIKTI: 3", box.PreviewText, "the box does not preview the output");
            StringAssert.Contains("TEZGÂHTA KALACAK: 0", box.PreviewText,
                "the box does not preview what is left behind");

            box.MinusButton.onClick.Invoke();
            StringAssert.Contains("ÇIKTI: 2", box.PreviewText, "the preview did not follow the number");
            StringAssert.Contains("TEZGÂHTA KALACAK: 3", box.PreviewText);
        }

        // =====================================================================
        // Withdraw
        // =====================================================================

        [Test]
        public void AWithdrawBoxIsCappedByWhatIsActuallyOnTheBench()
        {
            NebulaQuantityModal box = Box();
            int delivered = -1;
            box.Open(QuantityMode.Withdraw, "KARGOYA GERİ AL", Metal, "TEZGÂHTA 5",
                min: 1, max: 5, step: 1, null, amount => delivered = amount);

            Assert.AreEqual(QuantityMode.Withdraw, box.Mode);
            Assert.AreEqual(5, box.Max);

            box.Field.text = "6";
            Assert.IsFalse(box.HasValidValue, "the box offered to take back more than exists");
            StringAssert.Contains("EN FAZLA 5", box.WarningText);

            box.Field.text = "2";
            box.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(2, delivered);
        }

        // =====================================================================
        // What the box refuses to hand over
        // =====================================================================

        [Test]
        public void AnEmptyBoxCannotConfirm()
        {
            NebulaQuantityModal box = Box();
            int calls = 0;
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 5, 1, null, _ => calls++);

            box.Field.text = string.Empty;

            Assert.IsFalse(box.HasValidValue, "an empty box reads as a number");
            Assert.IsFalse(box.ConfirmButton.interactable, "an empty box can be confirmed");
            StringAssert.Contains("BİR MİKTAR GİR", box.WarningText);

            box.Confirm();
            Assert.AreEqual(0, calls, "an empty box delivered a number anyway");
            Assert.IsTrue(box.IsOpen);
        }

        [Test]
        public void LeadingZerosAreReadAsTheNumberTheyAreAndNormalisedSafely()
        {
            NebulaQuantityModal box = Box();
            int delivered = -1;
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 20, 1, null, a => delivered = a);

            box.Field.text = "007";
            Assert.AreEqual(7, box.Value, "a leading zero broke the parse");
            Assert.IsTrue(box.HasValidValue);

            // Anything that writes the value normalises it, so the box does not sit
            // there showing 007 -- but the caret is never rewritten mid-edit.
            box.PlusButton.onClick.Invoke();
            Assert.AreEqual("8", box.RawText, "the box did not normalise once it wrote the value");

            box.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(8, delivered);
        }

        [Test]
        public void CancellingDeliversNothingAndClosesTheBox()
        {
            NebulaQuantityModal box = Box();
            int calls = 0;
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 5, 1, null, _ => calls++);

            box.CancelButton.onClick.Invoke();

            Assert.AreEqual(0, calls, "cancelling ran the transaction");
            Assert.IsFalse(box.IsOpen);
            Assert.AreEqual(1, box.CancelledCount);
            Assert.AreEqual(0, box.ConfirmedCount);
        }

        [Test]
        public void ADoubleConfirmDeliversOnce()
        {
            NebulaQuantityModal box = Box();
            int calls = 0;
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 5, 1, null, _ => calls++);

            box.ConfirmButton.onClick.Invoke();
            box.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(1, calls, "one press produced two transactions");
            Assert.AreEqual(1, box.ConfirmedCount);
        }

        [Test]
        public void ASecondOpenOverAnOpenBoxIsRefused()
        {
            NebulaQuantityModal box = Box();
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 5, 1, null, _ => { });

            Assert.IsFalse(box.Open(QuantityMode.Withdraw, "KARGOYA GERİ AL", Metal, "",
                    1, 3, 1, null, _ => { }),
                "a second question stacked over the first");
            Assert.AreEqual(QuantityMode.Deposit, box.Mode, "the second open changed the first's mode");
        }

        /// <summary>
        /// The ceiling is re-stated from live state, and a number that no longer
        /// fits under it comes down with it. The page refreshes on a timer while the
        /// box is open, so a stale ceiling is a refusal the interface invited.
        /// </summary>
        [Test]
        public void ADroppingCeilingPullsTheNumberDownWithIt()
        {
            NebulaQuantityModal box = Box();
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 20, 1, null, _ => { });
            Assert.AreEqual(20, box.Value);

            box.SetLimits(1, 6);

            Assert.AreEqual(6, box.Max);
            Assert.AreEqual(6, box.Value, "the box still offered an amount that no longer exists");
            Assert.IsTrue(box.HasValidValue);
        }

        /// <summary>
        /// The page going away closes the box without running anything. Nothing was
        /// applied, so leaving is exactly as free as cancelling.
        /// </summary>
        [Test]
        public void TheBoxClosesSilentlyWhenThePageIsTakenAway()
        {
            NebulaQuantityModal box = Box();
            int calls = 0;
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 5, 1, null, _ => calls++);
            Assert.IsTrue(box.IsOpen);

            box.gameObject.SetActive(false);

            Assert.IsFalse(box.IsOpen, "the box still believes it is open");
            Assert.AreEqual(0, calls, "being taken away ran the transaction");
            Assert.AreEqual(0, box.CancelledCount, "nobody cancelled anything");
        }

        /// <summary>
        /// The page underneath cannot be clicked through. Every control on the
        /// Resources page is a raycast target, so without a blocker in front of them
        /// a click meant for ONAYLA is also a click on whatever is behind it.
        /// </summary>
        [Test]
        public void AnOpenBoxBlocksThePageUnderneath()
        {
            NebulaQuantityModal box = Box();
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 5, 1, null, _ => { });

            Image scrim = null;
            foreach (Image image in box.GetComponentsInChildren<Image>(true))
                if (image.gameObject.name == "Scrim") scrim = image;

            Assert.IsNotNull(scrim, "the box has no blocker in front of the page");
            Assert.IsTrue(scrim.raycastTarget, "the blocker lets the pointer through");
            Assert.AreEqual(Vector2.zero, scrim.rectTransform.anchorMin);
            Assert.AreEqual(Vector2.one, scrim.rectTransform.anchorMax);
        }

        /// <summary>
        /// Focus cannot walk out of the box. Unity's directional search does not know
        /// about the scrim and would happily step from ONAYLA onto a cargo row that
        /// cannot be clicked.
        /// </summary>
        [Test]
        public void FocusCannotLeaveTheOpenBox()
        {
            NebulaQuantityModal box = Box();
            box.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", Metal, "", 1, 5, 1, null, _ => { });

            var inside = new HashSet<GameObject>();
            foreach (Selectable selectable in box.GetComponentsInChildren<Selectable>(true))
                inside.Add(selectable.gameObject);

            foreach (Selectable selectable in box.GetComponentsInChildren<Selectable>(true))
            {
                if (!selectable.gameObject.activeInHierarchy) continue;
                Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, selectable.navigation.mode,
                    selectable.name + " walks wherever Unity's search takes it");

                foreach (Selectable neighbour in new[]
                         {
                             selectable.navigation.selectOnUp, selectable.navigation.selectOnDown,
                             selectable.navigation.selectOnLeft, selectable.navigation.selectOnRight,
                         })
                    if (neighbour != null)
                        Assert.IsTrue(inside.Contains(neighbour.gameObject),
                            $"{selectable.name} hands focus to {neighbour.name}, outside the box");
            }
        }
    }
}
