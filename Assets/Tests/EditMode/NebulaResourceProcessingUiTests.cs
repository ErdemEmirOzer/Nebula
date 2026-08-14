using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Tutorial;
using Nebula.Tutorial.Steps;
using Nebula.UI.Bench;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The Resources page's half of the processing bench: the refusal vocabulary,
    /// what the columns contain, and the fact that the timed bay is genuinely gone
    /// rather than merely unused.
    /// <para>
    /// The defect this fixture guards against is not a wrong number. It is a page
    /// that describes a mechanic the backend no longer has — a progress bar over an
    /// instant transaction, a "cancel" for a batch that cannot exist, a slot count
    /// bought by a tier that buys nothing. Every assertion about what is
    /// <i>absent</i> here is load-bearing for exactly that reason.
    /// </para>
    /// <para>
    /// The running-session half — real drags, real modals and real cargo moving —
    /// is <c>NebulaResourceProcessingPlayModeTests</c>. Everything here works on a
    /// page built with no session at all, which is a real state (the page can be
    /// opened before composition finishes) and its own refusal.
    /// </para>
    /// </summary>
    public class NebulaResourceProcessingUiTests
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
                if (spawned[i] != null) UnityEngine.Object.DestroyImmediate(spawned[i]);
            spawned.Clear();

            if (shell != null)
            {
                shell.Shutdown();
                UnityEngine.Object.DestroyImmediate(shell.gameObject);
                shell = null;
            }
            ExclusiveModal.ResetForSession();
        }

        private RectTransform Host()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            go.transform.SetParent(shell.Root(UiLayer.Menu), false);
            spawned.Add(go);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>The host the last <see cref="BuiltPage"/> was built into.</summary>
        private RectTransform pageHost;

        private NebulaResourcesPage BuiltPage()
        {
            var page = new NebulaResourcesPage(null);
            pageHost = Host();
            page.Build(pageHost);
            page.Refresh();
            return page;
        }

        private static string RepoRoot() =>
            Directory.GetParent(Application.dataPath).FullName;

        private static string ReadDoc(string relativePath)
        {
            string full = Path.Combine(RepoRoot(), relativePath);
            Assert.IsTrue(File.Exists(full), relativePath + " is missing");
            return File.ReadAllText(full);
        }

        private static List<TutorialStepDefinition> ProcessingSteps()
        {
            var steps = new List<TutorialStepDefinition>();
            foreach (TutorialStepDefinition step in ChapterDSteps.Build())
            {
                // D-01..D-07 are the processing block; D-08 onward is cargo and
                // logistics and is not this workstream's to speak for.
                if (string.CompareOrdinal(step.StepId, "D-08") < 0) steps.Add(step);
            }
            return steps;
        }

        private static IEnumerable<string> Words(TutorialStepDefinition step)
        {
            yield return step.TitleTr;
            yield return step.InstructionTr;
            yield return step.SummaryTr;
            for (int i = 0; i < step.ChecklistTr.Count; i++) yield return step.ChecklistTr[i];
        }

        // =====================================================================
        // The three columns exist and are reachable
        // =====================================================================

        /// <summary>
        /// The hold lists every catalog item, the bench offers its three controls,
        /// and a card exists for every recipe the catalog ships.
        /// </summary>
        [Test]
        public void ThePageCarriesTheHoldTheBenchAndACardPerRecipe()
        {
            NebulaResourcesPage page = BuiltPage();

            foreach (string id in ExtractionCatalog.ResourceIds)
                Assert.IsNotNull(page.CargoRowButton(id), id + " has no row in the hold");
            foreach (string id in ProcessingCatalog.MaterialIds)
                Assert.IsNotNull(page.CargoRowButton(id), id + " has no row in the hold");
            Assert.IsNotNull(page.CargoRowButton(ProcessingCatalog.BasicPartId),
                "the shared building part has no row in the hold");

            Assert.IsNotNull(page.BenchAddButton, "the bench has no EKLE control");
            Assert.IsNotNull(page.BenchWithdrawButton, "the bench has no KARGOYA GERİ AL control");
            Assert.IsNotNull(page.BenchWithdrawAllButton, "the bench has no TÜMÜNÜ GERİ AL control");
            Assert.IsNotNull(page.DropZone, "the bench is not a drop target at all");

            Assert.AreEqual(ProcessingCatalog.Recipes.Length, page.RecipeCardCount,
                "the conversion column does not match the catalog");
            for (int i = 0; i < ProcessingCatalog.Recipes.Length; i++)
            {
                Assert.IsNotNull(page.RecipeCardButton(i),
                    $"recipe {ProcessingCatalog.Recipes[i].Id} cannot be chosen");
                Assert.IsNotNull(page.RecipeConvertButton(i),
                    $"recipe {ProcessingCatalog.Recipes[i].Id} cannot be run");
            }
        }

        /// <summary>
        /// Only the items a recipe actually consumes are draggable and only they
        /// carry the keyboard path onto the bench. Offering the gesture on a
        /// finished material would be offering a refusal.
        /// </summary>
        [Test]
        public void OnlyProcessableItemsOfferDragAndTheKeyboardPath()
        {
            NebulaResourcesPage page = BuiltPage();

            foreach (string id in ExtractionCatalog.ResourceIds)
            {
                Assert.IsTrue(ProcessingBenchCommands.IsProcessable(id),
                    "precondition: raw resources are processable");
                Assert.IsNotNull(page.CargoDragSource(id), id + " cannot be dragged");
                Assert.IsNotNull(page.CargoAddButton(id), id + " has no TEZGÂHA EKLE control");
            }

            foreach (string id in ProcessingCatalog.MaterialIds)
            {
                Assert.IsFalse(ProcessingBenchCommands.IsProcessable(id),
                    "precondition: a finished material feeds no recipe");
                Assert.IsNull(page.CargoDragSource(id),
                    id + " offers a drag that the bench would refuse");
                Assert.IsNull(page.CargoAddButton(id),
                    id + " offers a bench control that could only fail");
            }
        }

        /// <summary>
        /// Every control a pointer can use is at least the interface's own minimum
        /// hit size, so the page is usable without pixel hunting.
        /// </summary>
        [Test]
        public void EveryControlMeetsTheMinimumHitTarget()
        {
            BuiltPage();
            RectTransform host = pageHost;

            var offenders = new List<string>();
            foreach (Button button in host.GetComponentsInChildren<Button>(true))
            {
                Rect rect = ((RectTransform)button.transform).rect;
                // Width is stretched from the column at layout time and reads as 0
                // on an un-laid-out rect; height is what a row actually authors.
                if (rect.height < NebulaUiDesignTokens.MinimumHitTarget)
                    offenders.Add($"{button.name} h={rect.height:0.#}");
            }

            Assert.IsEmpty(offenders,
                "controls below the minimum hit target: " + string.Join(", ", offenders));
        }

        // =====================================================================
        // The timed bay is gone, not hidden
        // =====================================================================

        /// <summary>
        /// No progress bar, no countdown, no slot and no cancel survives anywhere on
        /// the page.
        /// <para>
        /// This is the guard against the cheapest possible version of this change:
        /// leaving the old chrome in place and never updating it. Every one of these
        /// strings described a mechanic that no longer exists, and a page that still
        /// says "%62 · 1.5 SN" over an instant transaction is lying at a hundred
        /// percent duty cycle.
        /// </para>
        /// </summary>
        [Test]
        public void NoTimerProgressSlotOrCancelSurvivesOnThePage()
        {
            NebulaResourcesPage page = BuiltPage();
            page.SelectCargoRow(ExtractionCatalog.ResourceIds[0]);
            page.Refresh();

            var offenders = new List<string>();
            foreach (TextMeshProUGUI label in pageHost.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                string text = label.text ?? string.Empty;
                foreach (string banned in new[]
                         {
                             " SN", "İŞLENİYOR", "DURAKLATILDI", "ÇIKTI BEKLİYOR",
                             "YUVA", "İPTAL ET", "HIZ ×", "%",
                         })
                    if (text.Contains(banned)) offenders.Add($"'{text}' contains '{banned}'");
            }
            Assert.IsEmpty(offenders, "timed-bay wording survives: " + string.Join(" | ", offenders));

            var bars = new List<string>();
            foreach (RectTransform rect in pageHost.GetComponentsInChildren<RectTransform>(true))
                if (rect.gameObject.name == "Progress") bars.Add(rect.gameObject.name);
            Assert.IsEmpty(bars, "a progress bar survives over a transaction that has no duration");
        }

        // =====================================================================
        // Refusals — one table, every member, no enum names
        // =====================================================================

        /// <summary>
        /// Every typed deposit refusal has its own words. A reason that fell through
        /// to a default would put an enum name in front of a player; a reason that
        /// returned an empty string would put nothing there at all.
        /// </summary>
        [Test]
        public void EveryDepositReasonHasItsOwnPlayerFacingText()
        {
            AssertCovered<BenchDepositReason>(
                r => NebulaBenchMessages.Deposit(r), BenchDepositReason.None);
        }

        /// <inheritdoc cref="EveryDepositReasonHasItsOwnPlayerFacingText"/>
        [Test]
        public void EveryWithdrawReasonHasItsOwnPlayerFacingText()
        {
            AssertCovered<BenchWithdrawReason>(
                r => NebulaBenchMessages.Withdraw(r), BenchWithdrawReason.None);
        }

        /// <inheritdoc cref="EveryDepositReasonHasItsOwnPlayerFacingText"/>
        [Test]
        public void EveryConvertReasonHasItsOwnPlayerFacingText()
        {
            AssertCovered<BenchConvertReason>(
                r => NebulaBenchMessages.Convert(r, ProcessingCatalog.Recipes[0]),
                BenchConvertReason.None);
        }

        private static void AssertCovered<T>(Func<T, string> map, T success) where T : Enum
        {
            var seen = new Dictionary<string, T>();
            foreach (T reason in (T[])Enum.GetValues(typeof(T)))
            {
                string text = map(reason);
                if (Equals(reason, success))
                {
                    Assert.IsEmpty(text, "success is not a refusal and must carry no text");
                    continue;
                }

                Assert.IsNotEmpty(text, reason + " has no player-facing text");
                Assert.AreNotEqual(reason.ToString(), text, reason + " leaks its enum name");
                Assert.IsFalse(seen.ContainsKey(text),
                    $"{reason} and {(seen.TryGetValue(text, out T other) ? other.ToString() : "")} " +
                    $"share the words \"{text}\"");
                seen[text] = reason;
            }
        }

        /// <summary>
        /// The multiple refusal names the number the player has to hit, in Turkish
        /// that reads like a person wrote it. "MİKTAR 3 TAM KATI OLMALI" is not a
        /// sentence; the suffix follows how the numeral is spoken, which the digits
        /// alone do not carry.
        /// </summary>
        [Test]
        public void TheMultipleRefusalNamesTheRecipesOwnNumber()
        {
            foreach (ProcessingRecipe recipe in ProcessingCatalog.Recipes)
            {
                string text = NebulaBenchMessages.Convert(
                    BenchConvertReason.AmountNotMultipleOfRecipeInput, recipe);
                StringAssert.Contains(recipe.InputCount.ToString(), text,
                    recipe.Id + ": the refusal does not name the number");
                StringAssert.Contains("TAM KATI", text, recipe.Id + ": the refusal is not the rule");
                StringAssert.Contains("'", text,
                    recipe.Id + ": the number was concatenated without its suffix");
            }

            Assert.AreEqual("2'NİN", NebulaBenchMessages.Genitive(2));
            Assert.AreEqual("3'ÜN", NebulaBenchMessages.Genitive(3));
            Assert.AreEqual("1'İN", NebulaBenchMessages.Genitive(1));
            Assert.AreEqual("6'NIN", NebulaBenchMessages.Genitive(6));
            Assert.AreEqual("10'UN", NebulaBenchMessages.Genitive(10));
            Assert.AreEqual("100'ÜN", NebulaBenchMessages.Genitive(100));
        }

        /// <summary>
        /// With no session the page refuses rather than pretending: the bench reads
        /// empty, every bench control is disabled with a reason, and no card is shown.
        /// </summary>
        [Test]
        public void WithNoSessionTheBenchIsEmptyAndEveryControlSaysWhy()
        {
            NebulaResourcesPage page = BuiltPage();

            Assert.IsNull(page.BenchItemId, "a page with no session invented a bench item");
            Assert.AreEqual(0, page.BenchAmount);
            Assert.AreEqual(0, page.VisibleRecipeCards, "cards were drawn for an empty bench");
            Assert.AreEqual(NebulaBenchMessages.BenchEmptyHint, page.RecipesEmptyText,
                "the empty conversion column does not say what to do");
            Assert.AreEqual(NebulaBenchMessages.DropHint, page.BenchHintText,
                "the empty bench does not say what to do");

            Assert.IsFalse(page.BenchAddButton.interactable);
            Assert.IsNotEmpty(page.BenchAddBlockedReason ?? string.Empty,
                "the bench's EKLE is dead with no reason given");
            Assert.IsFalse(page.BenchWithdrawButton.interactable);
            Assert.AreEqual(NebulaBenchMessages.Withdraw(BenchWithdrawReason.BenchEmpty),
                page.BenchWithdrawBlockedReason);
        }

        /// <summary>
        /// A deposit that cannot happen is refused on screen and moves nothing —
        /// and, critically, does not open a box asking a question whose only answer
        /// is a refusal.
        /// </summary>
        [Test]
        public void ARefusedDepositSaysSoAndOpensNoBox()
        {
            NebulaResourcesPage page = BuiltPage();

            page.RequestDeposit(ExtractionCatalog.ResourceIds[0]);

            Assert.IsFalse(page.Modal.IsOpen, "a box was opened over a deposit that cannot happen");
            Assert.AreEqual(0, page.DepositsIssued);
            Assert.IsNotEmpty(page.FeedbackText, "the refusal never reached the screen");
            Assert.AreEqual(0, page.SuccessCuesPlayed, "a refusal played a success cue");
        }

        /// <summary>
        /// A recipe the bench cannot honour is dropped rather than carried, and it
        /// is dropped by the very refresh the selection triggers.
        /// <para>
        /// A stale index is exactly what produces
        /// <see cref="BenchConvertReason.RecipeInputMismatch"/>, and a page that
        /// held one would be offering a control whose only outcome is a refusal.
        /// With no session there is no bench at all, so <i>every</i> recipe is
        /// unhonourable and the selection cannot stick — which is the honest
        /// behaviour, not a lost click.
        /// </para>
        /// </summary>
        [Test]
        public void ASelectionTheBenchCannotHonourIsCleared()
        {
            NebulaResourcesPage page = BuiltPage();
            Assert.AreEqual(-1, page.SelectedRecipe, "the page starts with something already chosen");

            page.SelectRecipe(3);

            Assert.AreEqual(-1, page.SelectedRecipe,
                "a selection survived a bench that cannot run it");
            Assert.AreEqual(0, page.VisibleRecipeCards,
                "a card was drawn for a recipe the bench cannot run");
        }

        // =====================================================================
        // The quantity box
        // =====================================================================

        /// <summary>
        /// The box exists, starts closed, and carries every control the design calls
        /// for. A modal built lazily on first use is a modal that has never been
        /// laid out at the scale it will first appear at.
        /// </summary>
        [Test]
        public void TheQuantityBoxIsBuiltClosedAndCarriesEveryControl()
        {
            NebulaResourcesPage page = BuiltPage();
            NebulaQuantityModal box = page.Modal;

            Assert.IsNotNull(box, "the page has no quantity box");
            Assert.IsFalse(box.IsOpen, "the box starts open");
            Assert.IsFalse(box.gameObject.activeSelf, "a closed box is still on screen");

            Assert.IsNotNull(box.Field, "there is no number box");
            Assert.IsNotNull(box.MinusButton);
            Assert.IsNotNull(box.PlusButton);
            Assert.IsNotNull(box.OneButton);
            Assert.IsNotNull(box.MaxButton);
            Assert.IsNotNull(box.ConfirmButton);
            Assert.IsNotNull(box.CancelButton);
            Assert.IsNotNull(box.Viewport.GetComponent<ScrollRect>(),
                "the box cannot scroll, so a short viewport clips its confirm control");
            Assert.AreEqual(TMP_InputField.CharacterValidation.Digit, box.Field.characterValidation,
                "the number box accepts something other than digits");
        }

        /// <summary>
        /// The bench's own state and colour are never the only channel: the drop
        /// refusal, the selection and a card's readiness are all words as well.
        /// </summary>
        [Test]
        public void StateIsCarriedByMoreThanColour()
        {
            NebulaResourcesPage page = BuiltPage();
            page.SelectCargoRow(ExtractionCatalog.ResourceIds[1]);
            page.Refresh();

            var names = new List<string>();
            foreach (TextMeshProUGUI label in pageHost.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (label.gameObject.name == "Name") names.Add(label.text);

            Assert.AreEqual(1, names.FindAll(n => n.StartsWith(">")).Count,
                "exactly one row must be marked as chosen, by a marker and not by a tint");

            // The markers have to exist in the face the interface is actually drawn
            // with, or they render as tofu and carry nothing at all -- which is what
            // happened to an earlier cut's ▸/✔/✕.
            TMP_FontAsset face = NebulaUiFactory.SharedFontAsset;
            Assert.IsNotNull(face, "the shared SDF face did not resolve");
            foreach (char glyph in ">[]+!-")
                Assert.IsTrue(face.HasCharacter(glyph),
                    $"the shared face cannot draw '{glyph}', so a state marker would be a tofu box");

            // The three typographic characters the page uses are allowed to come
            // from the fallback chain, which is how they are actually resolved at
            // draw time -- but they must resolve somewhere.
            foreach (char glyph in "×→·")
                Assert.IsTrue(face.HasCharacter(glyph, searchFallbacks: true, tryAddCharacter: false),
                    $"neither the shared face nor its fallbacks can draw '{glyph}'");
        }

        // =====================================================================
        // The tutorial teaches the bench
        // =====================================================================

        /// <summary>
        /// The processing steps teach the bench: dragging, choosing an amount,
        /// choosing a recipe, the conversion amount, the instant result, taking the
        /// leftover back, and the control that replaces the drag for a pad.
        /// </summary>
        [Test]
        public void TheProcessingTutorialTeachesTheBench()
        {
            List<TutorialStepDefinition> steps = ProcessingSteps();
            Assert.AreEqual(7, steps.Count, "the processing block is no longer D-01..D-07");

            var all = new List<string>();
            foreach (TutorialStepDefinition step in steps)
                foreach (string line in Words(step))
                    all.Add(line);
            string corpus = string.Join("\n", all);

            foreach (string taught in new[]
                     {
                         "sürükle",        // the drag
                         "TEZGÂHA EKLE",   // the keyboard/pad alternative
                         "Miktar",         // choosing an amount
                         "tarif",          // choosing a recipe
                         "tam kat",        // the conversion amount rule
                         "anında",         // the instant result
                         "geri al",        // taking the leftover back
                     })
                StringAssert.Contains(taught, corpus,
                    "the processing steps never teach: " + taught);
        }

        /// <summary>
        /// And they no longer teach the mechanic that was removed. A step that tells
        /// a player to wait for a bar is a step that can never be completed.
        /// </summary>
        [Test]
        public void TheProcessingTutorialNoLongerTeachesTheTimedBay()
        {
            var offenders = new List<string>();
            foreach (TutorialStepDefinition step in ProcessingSteps())
                foreach (string line in Words(step))
                    foreach (string banned in new[]
                             {
                                 "yuva", "Yuva", "saniye", "2 sn", "4 sn",
                                 "ilerleme", "İlerleme", "tamamlanınca", "bekle", "Bekle",
                             })
                        if (line.Contains(banned)) offenders.Add($"{step.StepId}: '{line}'");

            Assert.IsEmpty(offenders,
                "timed-bay instructions survive in the tutorial: " + string.Join(" | ", offenders));
        }

        // =====================================================================
        // Documentation drift
        // =====================================================================

        /// <summary>
        /// The player guide describes the bench that exists rather than the bay that
        /// does not. Documentation that survives a mechanic is worse than no
        /// documentation: it is confidently wrong.
        /// </summary>
        [Test]
        public void ThePlayerGuideDescribesTheBench()
        {
            string guide = ReadDoc("UIDesign/Finalization/RESOURCE-PROCESSING-GUIDE.md");

            foreach (string required in new[]
                     {
                         "sürükle", "İŞLEME TEZGÂHI", "DÖNÜŞTÜR",
                         "TÜMÜNÜ GERİ AL", NebulaBenchMessages.DropHint,
                         NebulaBenchMessages.BenchEmptyHint,
                         "ProcessingBenchCommands",
                     })
                StringAssert.Contains(required, guide, "the guide never mentions: " + required);

            foreach (string stale in new[]
                     {
                         "ProcessorRuntime", "ProcessingCommands.TryCancel", "İŞLEMEYİ BAŞLAT",
                         "İPTAL ET", "DURAKLATILDI", "SecondsScaled", "HIZ ×",
                     })
                Assert.IsFalse(guide.Contains(stale), "the guide still documents: " + stale);
        }

        /// <summary>
        /// The shipped Turkish docs say what the keys and the page actually do. The
        /// quick start walked a player through <c>G</c>, <c>1</c>, <c>2</c> and
        /// <c>[</c>/<c>]</c> over slots that no longer exist.
        /// </summary>
        [Test]
        public void TheShippedTurkishDocsDescribeTheBench()
        {
            string quickStart = ReadDoc("Distribution/Docs/QUICK-START_TR.md");
            string readme = ReadDoc("Distribution/Docs/README_TR.md");

            foreach (string required in new[] { "tezgâh", "sürükle" })
            {
                StringAssert.Contains(required, quickStart.ToLowerInvariant(),
                    "the quick start never mentions: " + required);
                StringAssert.Contains(required, readme.ToLowerInvariant(),
                    "the readme never mentions: " + required);
            }

            foreach (string stale in new[] { "işleme yuvaları arasında", "süreli bir tarif" })
            {
                Assert.IsFalse(quickStart.Contains(stale),
                    "the quick start still documents: " + stale);
                Assert.IsFalse(readme.Contains(stale), "the readme still documents: " + stale);
            }
        }
    }
}
