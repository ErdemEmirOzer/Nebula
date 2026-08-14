using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.Tutorial;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.UI.Tutorial;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The reading gate: the ANLADIM control on the instruction card, and the rule
    /// that it exists for exactly one kind of step.
    /// <para>
    /// Attention cards and context readouts carry no interaction, so a step whose
    /// whole lesson is "look at this" had nothing real to wait for and completed on
    /// the frame after it opened. The confirmation is the fix, and the danger it
    /// introduces is obvious: a control that finishes a step without touching the
    /// world is a skip button if it is ever offered on a step that has a real
    /// action. Everything below is about that danger.
    /// </para>
    /// <para>
    /// The card is exercised through <c>TutorialHud.Acknowledge</c> rather than by
    /// clicking, for the same reason the exit prompt is: this project uses the
    /// legacy Input Manager and there is no frame loop here. The click path is
    /// asserted separately by finding the real <see cref="Button"/> and invoking its
    /// own <c>onClick</c>, which is the same call a pointer makes.
    /// </para>
    /// </summary>
    public class TutorialAcknowledgementTests
    {
        /// <summary>
        /// The whole acknowledgeable set. Named here rather than derived, so adding
        /// a twelfth reading step is a deliberate edit to a test and not a quiet
        /// widening of the one path that can finish a step without gameplay.
        /// </summary>
        private static readonly string[] ExpectedReadingSteps = { "B-03", "B-11", "I-07" };

        private NebulaUiShell shell;
        private TutorialHud hud;

        private bool savedMotionOff, savedReducedMotion;

        [SetUp]
        public void SetUp()
        {
            savedMotionOff = AnimationAccessibility.MotionOff;
            savedReducedMotion = AnimationAccessibility.ReducedMotion;

            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
            hud = TutorialHud.Create(shell, new SilentLauncher());
        }

        [TearDown]
        public void TearDown()
        {
            if (hud != null && hud.gameObject != null) UnityEngine.Object.DestroyImmediate(hud.gameObject);
            hud = null;

            if (shell != null)
            {
                shell.Shutdown();
                if (shell.gameObject != null) UnityEngine.Object.DestroyImmediate(shell.gameObject);
            }
            shell = null;

            AnimationAccessibility.MotionOff = savedMotionOff;
            AnimationAccessibility.ReducedMotion = savedReducedMotion;
            ExclusiveModal.ResetForSession();
        }

        // =====================================================================
        // WHICH STEPS MAY BE ACKNOWLEDGED AT ALL
        // =====================================================================

        /// <summary>
        /// The tripwire. A step is acknowledgeable only because someone declared it
        /// so; the default is <see cref="TutorialStepKind.InteractiveAction"/>, and
        /// this is what makes a change to that default visible in review rather
        /// than in a player's save.
        /// </summary>
        [Test]
        public void ExactlyTheDeclaredReadingStepsAreAcknowledgeable()
        {
            string[] acknowledgeable = TutorialStepCatalog.All
                .Where(s => s.Kind == TutorialStepKind.InformationAcknowledgement)
                .Select(s => s.StepId).OrderBy(id => id, StringComparer.Ordinal).ToArray();

            CollectionAssert.AreEqual(
                ExpectedReadingSteps.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                acknowledgeable,
                "The acknowledgeable set changed. A reading step is one with no player action " +
                "available at all -- not one that is merely slow or passive. Every other step " +
                "must complete on its own real validator, or the confirmation becomes a skip button.");
        }

        /// <summary>
        /// A reading step still has to be worth reading. If its checklist and
        /// instruction said nothing, the confirmation would be a button the player
        /// presses to make an empty card go away.
        /// </summary>
        [Test]
        public void EveryReadingStepStillSaysWhatToRead()
        {
            foreach (TutorialStepDefinition step in ReadingSteps())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(step.InstructionTr),
                    $"{step.StepId} asks the player to read nothing");
                Assert.That(step.ChecklistTr.Count, Is.GreaterThan(0),
                    $"{step.StepId} is a reading step with no checklist, so the player is not told what they are looking at");
            }
        }

        // =====================================================================
        // THE CONTROL EXISTS ONLY ON A READING STEP
        // =====================================================================

        [Test]
        public void AnInteractiveStepIsOfferedNoConfirmationControlAtAll()
        {
            var offered = new List<string>();

            foreach (TutorialStepDefinition step in TutorialStepCatalog.All)
            {
                if (step.Kind == TutorialStepKind.InformationAcknowledgement) continue;

                hud.ShowStep(step, TutorialStepCatalog.Count);
                if (hud.AcknowledgementOffered) offered.Add(step.StepId);
                if (ActiveConfirmControl() != null) offered.Add(step.StepId + " (control is on screen)");
            }

            CollectionAssert.IsEmpty(offered,
                "these gameplay steps were offered a confirmation control: " + string.Join(", ", offered));
        }

        [Test]
        public void AReadingStepIsOfferedTheControl()
        {
            foreach (TutorialStepDefinition step in ReadingSteps())
            {
                hud.ShowStep(step, TutorialStepCatalog.Count);

                Assert.IsTrue(hud.AcknowledgementOffered, $"{step.StepId} was offered no way to confirm the reading");
                Assert.IsNotNull(ActiveConfirmControl(), $"{step.StepId} offers no control the player can press");
                Assert.AreEqual(TutorialUiCopy.Acknowledge, ConfirmCaption(),
                    "the control must say what pressing it means");
            }
        }

        /// <summary>
        /// The strongest form of the rule: not "the button is hidden" but "the call
        /// itself is refused". A gameplay step that somehow had the control on
        /// screen still could not be finished with it.
        /// </summary>
        [Test]
        public void ConfirmingIsRefusedOnEveryInteractiveStep()
        {
            var accepted = new List<string>();

            foreach (TutorialStepDefinition step in TutorialStepCatalog.All)
            {
                if (step.Kind == TutorialStepKind.InformationAcknowledgement) continue;

                hud.ShowStep(step, TutorialStepCatalog.Count);
                bool taken = hud.Acknowledge();
                if (taken || hud.AcknowledgedCurrentStep) accepted.Add(step.StepId);
            }

            CollectionAssert.IsEmpty(accepted,
                "these gameplay steps accepted an acknowledgement: " + string.Join(", ", accepted));
        }

        // =====================================================================
        // WHAT DOES AND DOES NOT COUNT AS CONFIRMING
        // =====================================================================

        [Test]
        public void OpeningTheCardIsNotConfirming()
        {
            foreach (TutorialStepDefinition step in ReadingSteps())
            {
                hud.ShowStep(step, TutorialStepCatalog.Count);
                Assert.IsFalse(hud.AcknowledgedCurrentStep,
                    $"{step.StepId} counted as read the moment its card opened");
            }
        }

        [Test]
        public void LettingFramesPassIsNotConfirming()
        {
            TutorialStepDefinition step = ReadingSteps().First();
            hud.ShowStep(step, TutorialStepCatalog.Count);

            // Sixty frames of the card's own animation clock, which is the only
            // clock it has. A confirmation that arrived from here would be a timer
            // wearing a button's clothes.
            for (int i = 0; i < 60; i++) hud.Tick(1f);

            Assert.IsFalse(hud.AcknowledgedCurrentStep,
                "the reading confirmed itself after enough frames had passed");
        }

        [Test]
        public void PressingTheControlConfirms()
        {
            TutorialStepDefinition step = ReadingSteps().First();
            hud.ShowStep(step, TutorialStepCatalog.Count);

            Assert.IsTrue(hud.Acknowledge(), "the confirmation was refused on a reading step");
            Assert.IsTrue(hud.AcknowledgedCurrentStep);
        }

        /// <summary>
        /// The click path, not the seam: the real <see cref="Button"/> the card
        /// built, invoked the same way a pointer invokes it.
        /// </summary>
        [Test]
        public void TheRealButtonsOwnClickConfirms()
        {
            TutorialStepDefinition step = ReadingSteps().First();
            hud.ShowStep(step, TutorialStepCatalog.Count);

            Button confirm = ActiveConfirmControl();
            Assert.IsNotNull(confirm, "the reading step drew no confirmation button");
            Assert.IsTrue(confirm.interactable, "the confirmation button is on screen but dead");

            confirm.onClick.Invoke();

            Assert.IsTrue(hud.AcknowledgedCurrentStep, "clicking the control did not confirm the reading");
        }

        [Test]
        public void ConfirmingTwiceIsStillOneConfirmation()
        {
            TutorialStepDefinition step = ReadingSteps().First();
            hud.ShowStep(step, TutorialStepCatalog.Count);

            Assert.IsTrue(hud.Acknowledge());
            Assert.IsFalse(hud.Acknowledge(), "the control accepted a second confirmation for the same step");
            Assert.IsTrue(hud.AcknowledgedCurrentStep);
        }

        /// <summary>
        /// The carry-over rule, at the card. One press must never reach two steps,
        /// and the reset is on <c>ShowStep</c> rather than on completion so it holds
        /// however the previous step ended -- including when the player abandoned it.
        /// </summary>
        [Test]
        public void ShowingTheNextStepClearsTheConfirmation()
        {
            TutorialStepDefinition first = ReadingSteps().First();
            hud.ShowStep(first, TutorialStepCatalog.Count);
            Assert.IsTrue(hud.Acknowledge());

            TutorialStepDefinition second = ReadingSteps().Last();
            hud.ShowStep(second, TutorialStepCatalog.Count);

            Assert.IsFalse(hud.AcknowledgedCurrentStep,
                "one acknowledgement carried into the next step and would have completed it too");
        }

        [Test]
        public void ShowingAnInteractiveStepAfterAReadingClearsBothTheFlagAndTheControl()
        {
            hud.ShowStep(ReadingSteps().First(), TutorialStepCatalog.Count);
            Assert.IsTrue(hud.Acknowledge());

            TutorialStepDefinition interactive = TutorialStepCatalog.All
                .First(s => s.Kind == TutorialStepKind.InteractiveAction);
            hud.ShowStep(interactive, TutorialStepCatalog.Count);

            Assert.IsFalse(hud.AcknowledgedCurrentStep, $"{interactive.StepId} inherited a confirmation");
            Assert.IsFalse(hud.AcknowledgementOffered, $"{interactive.StepId} kept the confirmation control");
            Assert.IsNull(ActiveConfirmControl(), $"{interactive.StepId} left the control on screen");
        }

        [Test]
        public void ClearingTheHudForgetsTheConfirmation()
        {
            hud.ShowStep(ReadingSteps().First(), TutorialStepCatalog.Count);
            Assert.IsTrue(hud.Acknowledge());

            hud.Clear();

            Assert.IsFalse(hud.AcknowledgedCurrentStep, "a cleared card still reports the reading as confirmed");
            Assert.IsFalse(hud.AcknowledgementOffered);
        }

        // =====================================================================
        // ACCESSIBILITY
        // =====================================================================

        /// <summary>
        /// Motion Off removes the travel, never the control. A player who turned
        /// animation off must still be able to finish a reading step.
        /// </summary>
        [Test]
        public void TheControlWorksWithMotionOff()
        {
            AnimationAccessibility.MotionOff = true;
            AnimationAccessibility.ReducedMotion = true;

            TutorialStepDefinition step = ReadingSteps().First();
            hud.ShowStep(step, TutorialStepCatalog.Count);

            Assert.IsTrue(hud.Card.MotionSnapped, "motion off did not actually snap the card");
            Assert.IsTrue(hud.AcknowledgementOffered, "motion off took the confirmation control away");

            Button confirm = ActiveConfirmControl();
            Assert.IsNotNull(confirm, "motion off left the reading step with no control at all");
            confirm.onClick.Invoke();

            Assert.IsTrue(hud.AcknowledgedCurrentStep, "motion off made the control unusable");
        }

        /// <summary>
        /// The keyboard path exists and is named in one place, so the card's prompt
        /// and the key the HUD reads cannot drift apart. The press itself goes
        /// through <c>UnityEngine.Input</c>, which cannot be driven here -- what is
        /// asserted is that the prompt tells the player the same key the reader
        /// listens for, and that it is not a flight key.
        /// </summary>
        [Test]
        public void TheKeyboardPromptNamesTheKeyTheHudActuallyReads()
        {
            string prompt = TutorialKeyPrompts.Acknowledge();

            StringAssert.Contains(TutorialUiCopy.Acknowledge, prompt);
            StringAssert.Contains(TutorialKeyPrompts.Key(TutorialAcknowledgeInput.PrimaryKey), prompt);

            Assert.AreNotEqual(KeyCode.Escape, TutorialAcknowledgeInput.PrimaryKey,
                "Escape already leaves the tutorial; confirming and leaving must not be the same key");
            foreach (KeyCode flightKey in new[]
                     {
                         KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
                         KeyCode.Space, KeyCode.C, KeyCode.Q, KeyCode.E, KeyCode.LeftShift
                     })
            {
                Assert.AreNotEqual(flightKey, TutorialAcknowledgeInput.PrimaryKey,
                    "the confirmation key collides with a flight control");
                Assert.AreNotEqual(flightKey, TutorialAcknowledgeInput.SecondaryKey,
                    "the confirmation key collides with a flight control");
            }
        }

        /// <summary>
        /// The card is a HUD widget the player reads while flying, and every pixel
        /// of it is deliberately click-through. The confirmation row is the single
        /// exception, and it has to be an exception rather than the card opening up.
        /// </summary>
        [Test]
        public void OnlyTheConfirmationRowEverTakesAClick()
        {
            hud.ShowStep(ReadingSteps().First(), TutorialStepCatalog.Count);

            Transform row = FindRow(null);
            Assert.IsNotNull(row, "the reading step drew no confirmation row");

            CanvasGroup rowGroup = row.GetComponent<CanvasGroup>();
            Assert.IsNotNull(rowGroup, "the confirmation row carries no group of its own");
            Assert.IsTrue(rowGroup.blocksRaycasts, "the confirmation control cannot be clicked");
            Assert.IsTrue(rowGroup.interactable, "the confirmation control is not interactive");
            Assert.IsTrue(rowGroup.ignoreParentGroups,
                "the card's own group is non-interactive by design, so the confirmation row must ignore it " +
                "rather than the card being opened up and swallowing clicks meant for the game");
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private static IEnumerable<TutorialStepDefinition> ReadingSteps()
        {
            TutorialStepDefinition[] steps = TutorialStepCatalog.All
                .Where(s => s.Kind == TutorialStepKind.InformationAcknowledgement).ToArray();
            Assert.That(steps.Length, Is.GreaterThan(0), "the curriculum declares no reading step");
            return steps;
        }

        /// <summary>The confirmation button, or <c>null</c> when the card is not offering one.</summary>
        private Button ActiveConfirmControl()
        {
            Transform row = FindRow(hud.Card.transform);
            if (row == null) return null;
            if (!row.gameObject.activeInHierarchy) return null;
            return row.GetComponentInChildren<Button>(false);
        }

        private string ConfirmCaption()
        {
            Button confirm = ActiveConfirmControl();
            if (confirm == null) return null;
            var label = confirm.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            return label == null ? null : label.text;
        }

        /// <summary>
        /// The row is looked up from the shell rather than from the card component,
        /// because the card's controller sits on the layer root and its visuals are
        /// a sibling panel -- a widget parented into the panel it toggles stops
        /// updating the moment that panel is hidden.
        /// </summary>
        private Transform FindRow(Transform _)
        {
            RectTransform layer = shell.Root(UiLayer.HudStatic);
            foreach (RectTransform candidate in layer.GetComponentsInChildren<RectTransform>(true))
                if (candidate.gameObject.name == TutorialUiCopy.AcknowledgeName) return candidate;
            return null;
        }

        private sealed class SilentLauncher : ITutorialLauncher
        {
            public void StartFromBeginning() { }
            public void StartSingleStep(string stepId) { }
            public void ExitToMainMenu() { }
            public event Action<TutorialRunMode, bool> RunEnded;

            /// <summary>Kept referenced so the compiler does not warn the event is never raised.</summary>
            internal void Never() => RunEnded?.Invoke(TutorialRunMode.Standalone, false);
        }
    }
}
