using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Tutorial;
using Nebula.UI.Controls;
using Nebula.UI.Design;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Chapters A, B and K of the tutorial curriculum, checked where the seam is
    /// pure enough not to need a scene: the definitions themselves, the assets they
    /// declare, and every step whose real command can be driven without a frame
    /// loop.
    /// <para>
    /// The steps that need a running world — everything that waits on
    /// <c>Update</c>/<c>FixedUpdate</c>, a materialized planet, a POI timer or the
    /// key-capture listener — are covered by
    /// <c>TutorialChapterABKGameplayTests</c> in PlayMode. Between the two, all 36
    /// steps have a positive test, a negative test and a
    /// prepare-does-not-auto-complete test.
    /// </para>
    /// </summary>
    public class TutorialChapterABKValidatorTests
    {
        /// <summary>The chapter A/B/K steps whose real command seam needs no frame loop.</summary>
        private static readonly string[] PureSeamSteps =
        {
            "A-01", "A-02", "A-03",
            "K-01", "K-02", "K-03", "K-04", "K-05", "K-06", "K-07", "K-08", "K-09", "K-10",
        };

        /// <summary>
        /// Curriculum order, which is not id order: A-14 was written after the
        /// chapter was numbered and teaches boost next to acceleration, so it is
        /// registered fourth. The catalog numbers by registration order, so this
        /// list is the one that has to match.
        /// </summary>
        private static readonly string[] ChapterAIds =
        {
            "A-01", "A-02", "A-03", "A-14", "A-04", "A-05", "A-06", "A-07",
            "A-08", "A-09", "A-10", "A-11", "A-12", "A-13",
        };

        private static readonly string[] ChapterBIds =
        {
            "B-01", "B-02", "B-03", "B-04", "B-05", "B-06",
            "B-07", "B-08", "B-09", "B-10", "B-11",
        };

        private static readonly string[] ChapterKIds =
        {
            "K-01", "K-02", "K-03", "K-04", "K-05", "K-06",
            "K-07", "K-08", "K-09", "K-10", "K-11", "K-12",
        };

        // ---- the world these tests leave behind must be the one they found ----

        private TutorialStepScope scope;
        private GameObject shipObject;
        private ShipController ship;

        private float savedTimeScale, savedUiScale, savedHudScale;
        private bool savedReducedMotion, savedMotionOff, savedCameraComfort;
        private bool savedFlashReduction, savedSafeMode, savedHighContrast, savedCue;
        private int savedQualityLevel, savedVSync;
        private bool savedInputSuppressed;
        private readonly Dictionary<CombatAction, KeyCode> savedBindings = new Dictionary<CombatAction, KeyCode>();

        [SetUp]
        public void SetUp()
        {
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            savedUiScale = NebulaUiShell.UiScale;
            savedHudScale = CombatAccessibilitySettings.HudScale;
            savedReducedMotion = AnimationAccessibility.ReducedMotion;
            savedMotionOff = AnimationAccessibility.MotionOff;
            savedCameraComfort = AnimationAccessibility.CameraComfort;
            savedFlashReduction = LightingAccessibility.FlashReduction;
            savedSafeMode = LightingAccessibility.PhotosensitivitySafeMode;
            savedHighContrast = LightingAccessibility.HighContrast;
            savedCue = CombatAccessibilitySettings.AudioVisualCueForWarnings;
            savedQualityLevel = QualitySettings.GetQualityLevel();
            savedVSync = QualitySettings.vSyncCount;
            savedInputSuppressed = InputGate.Suppressed;

            savedBindings.Clear();
            foreach (CombatAction action in NebulaControlBindings.Actions)
                savedBindings[action] = NebulaControlBindings.KeyFor(action);

            ExclusiveModal.ResetForSession();
            NebulaKeyCaptureListener.ResetSourceToKeyboard();
        }

        [TearDown]
        public void TearDown()
        {
            DisposeScope();
            if (shipObject != null) UnityEngine.Object.DestroyImmediate(shipObject);
            shipObject = null;
            ship = null;

            foreach (KeyValuePair<CombatAction, KeyCode> entry in savedBindings)
                if (NebulaControlBindings.KeyFor(entry.Key) != entry.Value)
                    NebulaControlBindings.TryRebind(entry.Key, entry.Value);

            NebulaUiShell.UiScale = savedUiScale;
            CombatAccessibilitySettings.HudScale = savedHudScale;
            AnimationAccessibility.ReducedMotion = savedReducedMotion;
            AnimationAccessibility.MotionOff = savedMotionOff;
            AnimationAccessibility.CameraComfort = savedCameraComfort;
            LightingAccessibility.FlashReduction = savedFlashReduction;
            LightingAccessibility.PhotosensitivitySafeMode = savedSafeMode;
            LightingAccessibility.HighContrast = savedHighContrast;
            CombatAccessibilitySettings.AudioVisualCueForWarnings = savedCue;
            QualitySettings.SetQualityLevel(savedQualityLevel, true);
            QualitySettings.vSyncCount = savedVSync;
            InputGate.Suppressed = savedInputSuppressed;
            Time.timeScale = savedTimeScale;

            ExclusiveModal.ResetForSession();
            NebulaKeyCaptureListener.ResetSourceToKeyboard();
        }

        // =====================================================================
        // THE DEFINITIONS THEMSELVES
        // =====================================================================

        /// <summary>
        /// Numbering is the catalog's job, so what a chapter has to guarantee is
        /// that its own steps are present, in curriculum order, and consecutive
        /// wherever they land. Nothing here names an absolute global number: a
        /// chapter that has not shipped yet must not be able to break this.
        /// </summary>
        [Test]
        public void EachChapterRegistersItsCurriculumStepsInOrder()
        {
            AssertChapter(TutorialChapterId.Flight, ChapterAIds);
            AssertChapter(TutorialChapterId.Exploration, ChapterBIds);
            AssertChapter(TutorialChapterId.Session, ChapterKIds);
        }

        private static void AssertChapter(TutorialChapterId chapter, string[] expectedIds)
        {
            IReadOnlyList<TutorialStepDefinition> steps = TutorialStepCatalog.InChapter(chapter);
            Assert.AreEqual(expectedIds.Length, steps.Count,
                $"chapter {chapter} must carry exactly its curriculum steps");

            for (int i = 0; i < expectedIds.Length; i++)
            {
                Assert.AreEqual(expectedIds[i], steps[i].StepId, $"chapter {chapter} step {i} is out of order");
                Assert.AreEqual(chapter, steps[i].Chapter);
                if (i > 0)
                    Assert.AreEqual(steps[i - 1].GlobalNumber + 1, steps[i].GlobalNumber,
                        $"{steps[i].StepId} is not consecutive with {steps[i - 1].StepId}");
            }
        }

        /// <summary>
        /// A step that ships with a missing prefab still renders — as a grey
        /// primitive — and still teaches the player something untrue. Resolving the
        /// declared requirements needs no scene at all, which is the only cheap way
        /// to keep that honest.
        /// </summary>
        [Test]
        public void EveryDeclaredAssetResolves()
        {
            var world = new TutorialWorldFactory(() => null, null);
            var failures = new List<string>();

            foreach (TutorialStepDefinition step in MySteps())
                foreach (TutorialAssetRequirement requirement in step.Assets)
                    if (!world.CanResolve(requirement, out string reason))
                        failures.Add($"{step.StepId}: {reason}");

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// The card is Turkish and the player reads all of it. An empty instruction
        /// or a blank checklist row is a step that asks for nothing.
        /// </summary>
        [Test]
        public void EveryStepSaysSomethingToThePlayer()
        {
            var failures = new List<string>();

            foreach (TutorialStepDefinition step in MySteps())
            {
                if (string.IsNullOrWhiteSpace(step.TitleTr)) failures.Add($"{step.StepId} has no title");
                if (string.IsNullOrWhiteSpace(step.InstructionTr)) failures.Add($"{step.StepId} has no instruction");
                if (string.IsNullOrWhiteSpace(step.SummaryTr)) failures.Add($"{step.StepId} has no summary");
                for (int i = 0; i < step.ChecklistTr.Count; i++)
                    if (string.IsNullOrWhiteSpace(step.ChecklistTr[i]))
                        failures.Add($"{step.StepId} checklist row {i} is blank");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// A key prompt is resolved from the live binding, never written as a
        /// literal, so a rebound key is shown as the player actually bound it.
        /// </summary>
        [Test]
        public void EveryKeyHintResolvesToARealLabel()
        {
            var failures = new List<string>();

            foreach (TutorialStepDefinition step in MySteps())
            {
                if (step.KeyHint == null) continue;
                string label = step.KeyHint();
                if (string.IsNullOrWhiteSpace(label)) failures.Add($"{step.StepId} has an empty key hint");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // =====================================================================
        // THE RULE THIS WHOLE DESIGN EXISTS FOR
        // =====================================================================

        /// <summary>
        /// How many times a prepared step is polled before it is allowed to be
        /// called honest.
        /// <para>
        /// One evaluation is not enough, and the reason is not hypothetical: a
        /// validator that latches ("was fast" then "is now slow"), one that arms
        /// itself on the first poll, or one whose objectives callback writes the
        /// state the completion test reads, can all be false on the first
        /// evaluation and true on the second with the player doing nothing at all.
        /// There is no frame loop here, so this is what "several ticks" means in
        /// EditMode; the real-frame version of the same sweep is in
        /// <c>TutorialLifecycleTests</c>.
        /// </para>
        /// </summary>
        private const int IdlePolls = 8;

        /// <summary>
        /// Prepare may install everything the step needs except the thing the player
        /// is being asked to do. A step that prepares its way into its own success
        /// condition completes itself.
        /// </summary>
        [Test]
        public void NoPureSeamStepCompletesWhileThePlayerDoesNothing()
        {
            var failures = new List<string>();

            foreach (string stepId in PureSeamSteps)
            {
                Run run = Enter(stepId);
                int completedOn = Idle(run);
                if (completedOn >= 0)
                    failures.Add($"{stepId} completed on poll {completedOn + 1} with no player action at all");
                DisposeScope();
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void EveryPureSeamStepCompletesOnItsRealCommandAndOnlyThen()
        {
            var failures = new List<string>();

            foreach (string stepId in PureSeamSteps)
            {
                Run run = Enter(stepId);
                try
                {
                    // The same sequence in one run: idled first, so "it completed"
                    // below can only mean the drive did it.
                    int completedOn = Idle(run);
                    if (completedOn >= 0)
                    {
                        failures.Add($"{stepId} completed on poll {completedOn + 1} before its command was driven");
                    }
                    else
                    {
                        DrivePositive(stepId, run);
                        run.Runtime.Evaluate(run.Report);
                        if (!run.Report.IsComplete) failures.Add($"{stepId} did not complete on its real command");
                    }
                }
                catch (IgnoreOnThisMachine skip)
                {
                    Debug.Log($"[Tutorial tests] {stepId} skipped: {skip.Message}");
                }
                DisposeScope();
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void NoPureSeamStepCompletesOnTheWrongAction()
        {
            var failures = new List<string>();

            foreach (string stepId in PureSeamSteps)
            {
                Run run = Enter(stepId);
                try
                {
                    DriveNegative(stepId, run);
                    // Polled repeatedly afterwards too: a wrong action that is
                    // refused on the frame it happens and accepted two polls later
                    // is still a wrong action being accepted.
                    run.Runtime.Evaluate(run.Report);
                    int completedOn = run.Report.IsComplete ? 0 : Idle(run);
                    if (run.Report.IsComplete)
                        failures.Add($"{stepId} completed on an action that is not its own (poll {completedOn + 1})");
                }
                catch (IgnoreOnThisMachine skip)
                {
                    Debug.Log($"[Tutorial tests] {stepId} skipped: {skip.Message}");
                }
                DisposeScope();
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// Polls a prepared step <see cref="IdlePolls"/> times with no player action
        /// between them. Returns the zero-based poll it completed on, or -1 when it
        /// stayed honest throughout.
        /// </summary>
        private static int Idle(Run run)
        {
            for (int poll = 0; poll < IdlePolls; poll++)
            {
                run.Runtime.Evaluate(run.Report);
                if (run.Report.IsComplete) return poll;
            }
            return -1;
        }

        // =====================================================================
        // CHAPTER K: PERSISTENCE AND LEAKS
        // =====================================================================

        /// <summary>
        /// The tutorial must never write a real save. Running every chapter-K step
        /// that can be driven here, all the way through its own command, must leave
        /// all three slot files exactly as they were — including not existing.
        /// </summary>
        [Test]
        public void ChapterKWritesNoSaveFile()
        {
            var before = new Dictionary<int, (bool exists, DateTime written, long length)>();
            for (int slot = NebulaSaveSlotQuery.FirstSlot;
                 slot < NebulaSaveSlotQuery.FirstSlot + NebulaSaveSlotQuery.SlotCount; slot++)
                before[slot] = SlotState(slot);

            foreach (string stepId in PureSeamSteps)
            {
                if (!stepId.StartsWith("K-", StringComparison.Ordinal)) continue;
                Run run = Enter(stepId);
                try { DrivePositive(stepId, run); }
                catch (IgnoreOnThisMachine) { }
                run.Runtime.Evaluate(run.Report);
                DisposeScope();
            }

            var failures = new List<string>();
            foreach (KeyValuePair<int, (bool exists, DateTime written, long length)> entry in before)
            {
                (bool exists, DateTime written, long length) now = SlotState(entry.Key);
                if (!now.Equals(entry.Value))
                    failures.Add($"slot {entry.Key} changed: {entry.Value} -> {now}");
            }

            Assert.IsEmpty(failures,
                "A tutorial run wrote a save file.\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Nothing in this project persists a setting, so a step that changes one
        /// and does not put it back changes it for the player's campaign for the
        /// rest of the process. Every chapter-K step is run to completion and its
        /// scope disposed; nothing may be different afterwards.
        /// </summary>
        [Test]
        public void ChapterKLeaksNoSettingAfterCleanup()
        {
            SettingsSnapshot before = SettingsSnapshot.Capture();

            foreach (string stepId in PureSeamSteps)
            {
                if (!stepId.StartsWith("K-", StringComparison.Ordinal)) continue;
                Run run = Enter(stepId);
                try { DrivePositive(stepId, run); }
                catch (IgnoreOnThisMachine) { }
                run.Runtime.Evaluate(run.Report);
                DisposeScope();
            }

            Assert.IsEmpty(before.DifferencesFromNow(), string.Join("\n", before.DifferencesFromNow()));
        }

        /// <summary>
        /// The tutorial is not allowed to become this project's first PlayerPrefs
        /// writer, and the chapter that touches settings is where that would happen.
        /// </summary>
        [Test]
        public void ChapterFilesIntroduceNoPlayerPrefsWrite()
        {
            string[] files =
            {
                "Assets/Scripts/Nebula/Tutorial/Steps/ChapterASteps.cs",
                "Assets/Scripts/Nebula/Tutorial/Steps/ChapterBSteps.cs",
                "Assets/Scripts/Nebula/Tutorial/Steps/ChapterKSteps.cs",
            };

            foreach (string file in files)
            {
                if (!File.Exists(file)) continue;

                // Comment lines are skipped, not searched. ChapterKSteps documents in
                // prose that it introduces no PlayerPrefs write, and a raw text search
                // read that promise as the violation it was promising not to be.
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*")) continue;
                    StringAssert.DoesNotContain("PlayerPrefs", line,
                        $"{file} introduces a PlayerPrefs call; nothing else in Assets/ has one");
                }
            }
        }

        // =====================================================================
        // A-14: THE BOOST SEAM DID NOT MOVE THE GAME
        // =====================================================================

        /// <summary>
        /// A-14 needed the boost control to be injectable, and the one thing that
        /// must not have bought is a change to how the shipping game flies.
        /// <para>
        /// Read off the source, because that is the only place the claim can
        /// actually be checked: the live path and the test path now run the same
        /// line, so no behavioural test can tell you whether the keyboard reads were
        /// preserved or quietly rewritten. What is asserted is that the boost key is
        /// named exactly once in the whole file, inside the shipping source, and
        /// that the ship's own boost decision is taken from the seam rather than
        /// from <c>Input</c>.
        /// </para>
        /// </summary>
        [Test]
        public void TheBoostSeamLeftTheLiveKeyboardPathAlone()
        {
            string path = Path.Combine(Application.dataPath, "Scripts", "Nebula", "Ship", "ShipController.cs");
            Assert.IsTrue(File.Exists(path), $"missing source file: {path}");

            var boostKeyLines = new List<string>();
            var boostAssignments = new List<string>();

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*")) continue;

                if (line.Contains("KeyCode.LeftShift")) boostKeyLines.Add(line);
                if (line.Contains("Boosting =")) boostAssignments.Add(line);
            }

            Assert.AreEqual(1, boostKeyLines.Count,
                "the boost key must be named in exactly one place -- the shipping keyboard source. Found:\n"
                + string.Join("\n", boostKeyLines));
            StringAssert.Contains("Input.GetKey(KeyCode.LeftShift)", boostKeyLines[0],
                "the shipping source must still read the same key through the same call");

            Assert.AreEqual(1, boostAssignments.Count,
                "Boosting must be decided in exactly one place. Found:\n" + string.Join("\n", boostAssignments));
            StringAssert.Contains("BoostHeld", boostAssignments[0],
                "the ship must take its boost decision from the flight input seam");
            StringAssert.DoesNotContain("Input.GetKey", boostAssignments[0],
                "the boost decision still reads UnityEngine.Input directly, so the seam is bypassed");
            StringAssert.Contains("CruiseSpeedMultiplier", boostAssignments[0],
                "the cruise refusal must stay on the ship's side of the seam, or a held control would " +
                "read as a boost the game had actually refused");
        }

        /// <summary>
        /// And the default really is the keyboard rather than a constant: with no
        /// key down, the shipping source reports nothing held.
        /// </summary>
        [Test]
        public void TheShippingFlightInputReadsTheRealKeyboard()
        {
            ShipController.ResetFlightInputToKeyboard();

            Assert.IsFalse(ShipController.FlightInput.BoostHeld,
                "the shipping source reports the boost control held with no keyboard behind it");
            Assert.IsFalse(ShipController.FlightInput.AnyThrustHeld,
                "the shipping source reports thrust held with no keyboard behind it");
            Assert.AreEqual(Vector3.zero, ShipController.FlightInput.Thrust);
        }

        // =====================================================================
        // THE HARNESS
        // =====================================================================

        private sealed class Run
        {
            internal ITutorialStepRuntime Runtime;
            internal TutorialStepReport Report;
            internal TutorialStepDefinition Definition;
        }

        /// <summary>Thrown by a driver when this machine genuinely cannot offer the control (a single quality level, for instance).</summary>
        private sealed class IgnoreOnThisMachine : Exception
        {
            internal IgnoreOnThisMachine(string message) : base(message) { }
        }

        private Run Enter(string stepId)
        {
            TutorialStepDefinition definition = TutorialStepCatalog.Find(stepId);
            Assert.IsNotNull(definition, $"step '{stepId}' is not registered");

            scope = TutorialStepScope.Create(stepId, null);
            var world = new TutorialWorldFactory(() => scope, null);

            NebulaContext gameplay = null;
            if (stepId.StartsWith("A-", StringComparison.Ordinal))
            {
                gameplay = new NebulaContext();
                gameplay.SetShip(Ship());
            }

            var context = new TutorialStepContext(gameplay, scope, TutorialRunMode.Standalone, world);
            var report = new TutorialStepReport();
            report.Reset(definition.ChecklistTr.Count);

            ITutorialStepRuntime runtime = definition.CreateRuntime();
            runtime.Prepare(context);

            return new Run { Runtime = runtime, Report = report, Definition = definition };
        }

        private void DisposeScope()
        {
            if (scope == null) return;
            scope.Dispose();
            Assert.IsEmpty(scope.CleanupFailures,
                "a step's cleanup threw: " + string.Join("; ", scope.CleanupFailures));
            scope = null;
        }

        private ShipController Ship()
        {
            if (ship != null) return ship;
            shipObject = new GameObject("Tutorial Test Ship");
            ship = shipObject.AddComponent<ShipController>();
            return ship;
        }

        // ---- the real commands, per step ---------------------------------------

        private void DrivePositive(string stepId, Run run)
        {
            switch (stepId)
            {
                // ShipController.ApplyLook is the same rotation the mouse drives.
                case "A-01": Ship().ApplyLook(60f, 0f, 0f); break;

                case "A-02": Ship().SetThrottleFraction(0.80f); break;

                case "A-03":
                    Ship().SetVelocity(Vector3.forward * 80f);
                    run.Runtime.Evaluate(run.Report);
                    Ship().SetVelocity(Vector3.zero);
                    break;

                case "K-01": ClickRail("Session"); break;
                case "K-02": Click("Load"); break;

                case "K-03":
                    Click("MainMenu");
                    run.Runtime.Evaluate(run.Report);
                    Click("Cancel");
                    break;

                case "K-04":
                    if (QualitySettings.names.Length < 2)
                        throw new IgnoreOnThisMachine("this build has a single quality level");
                    NudgeQuality();
                    break;

                case "K-05": ClickIn("Ana Ses", "Down"); break;
                case "K-06": ClickIn("HUD Ölçeği", "Up"); break;
                case "K-07": ClickIn("Hareketi Azalt", "Toggle"); break;
                case "K-08": ClickIn("Parlama Azaltma", "Toggle"); break;
                case "K-09": ClickIn("Yüksek Kontrast", "Toggle"); break;
                case "K-10": ClickIn("Uyarılar İçin Görsel İpucu", "Toggle"); break;

                default: throw new InvalidOperationException($"no positive driver for {stepId}");
            }
        }

        private void DriveNegative(string stepId, Run run)
        {
            switch (stepId)
            {
                // Far short of the 45 degrees the step asks for.
                case "A-01": Ship().ApplyLook(8f, 0f, 0f); break;

                case "A-02": Ship().SetThrottleFraction(0.30f); break;

                // Fast, then never slowed: half the step is not the step.
                case "A-03":
                    Ship().SetVelocity(Vector3.forward * 80f);
                    run.Runtime.Evaluate(run.Report);
                    break;

                case "K-01": ClickRail("Overview"); break;
                case "K-02": Click("Credits"); break;

                // The dialog opens and is left open: reading the warning is not the
                // step, deciding is.
                case "K-03": Click("MainMenu"); break;

                case "K-04": ClickIn("Dikey Eşitleme", "Toggle"); break;
                case "K-05": ClickIn("Dikey Eşitleme", "Toggle"); break;
                case "K-06": ClickIn("Kamera Konforu", "Toggle"); break;
                case "K-07": ClickIn("Kamera Konforu", "Toggle"); break;
                case "K-08": ClickIn("Yüksek Kontrast", "Toggle"); break;
                case "K-09": ClickIn("Parlama Azaltma", "Toggle"); break;
                case "K-10": ClickIn("Yüksek Kontrast", "Toggle"); break;

                default: throw new InvalidOperationException($"no negative driver for {stepId}");
            }
        }

        private void NudgeQuality()
        {
            bool atTop = QualitySettings.GetQualityLevel() >= QualitySettings.names.Length - 1;
            ClickIn("Kalite", atTop ? "Down" : "Up");
        }

        // ---- finding the real controls the step built ---------------------------

        private static void Click(string controlName)
        {
            Button button = FindButton(controlName);
            Assert.IsNotNull(button, $"no control named '{controlName}' is on screen");
            button.onClick.Invoke();
        }

        private static void ClickRail(string page) => Click(page + " Rail");

        /// <summary>Clicks a control inside one named settings row, so "Toggle" is unambiguous.</summary>
        private static void ClickIn(string rowName, string controlName)
        {
            Transform row = FindByName(rowName);
            Assert.IsNotNull(row, $"no settings row named '{rowName}' is on screen");

            foreach (Button button in row.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == controlName) { button.onClick.Invoke(); return; }

            Assert.Fail($"row '{rowName}' has no control named '{controlName}'");
        }

        private static Button FindButton(string controlName)
        {
            foreach (GameObject root in SceneRoots())
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                    if (button.gameObject.name == controlName) return button;
            return null;
        }

        private static Transform FindByName(string name)
        {
            foreach (GameObject root in SceneRoots())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.gameObject.name == name) return child;
            return null;
        }

        private static IEnumerable<GameObject> SceneRoots() =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        // ---- snapshots ----------------------------------------------------------

        private static (bool exists, DateTime written, long length) SlotState(int slot)
        {
            string path = SaveService.SlotFile(slot);
            if (!File.Exists(path)) return (false, default, 0L);
            var info = new FileInfo(path);
            return (true, info.LastWriteTimeUtc, info.Length);
        }

        private readonly struct SettingsSnapshot
        {
            private readonly float uiScale, hudScale;
            private readonly bool reducedMotion, motionOff, cameraComfort;
            private readonly bool flashReduction, safeMode, highContrast, cue;
            private readonly int qualityLevel, vSync;

            private SettingsSnapshot(float uiScale, float hudScale, bool reducedMotion, bool motionOff,
                bool cameraComfort, bool flashReduction, bool safeMode, bool highContrast, bool cue,
                int qualityLevel, int vSync)
            {
                this.uiScale = uiScale; this.hudScale = hudScale;
                this.reducedMotion = reducedMotion; this.motionOff = motionOff; this.cameraComfort = cameraComfort;
                this.flashReduction = flashReduction; this.safeMode = safeMode;
                this.highContrast = highContrast; this.cue = cue;
                this.qualityLevel = qualityLevel; this.vSync = vSync;
            }

            internal static SettingsSnapshot Capture() => new SettingsSnapshot(
                NebulaUiShell.UiScale, CombatAccessibilitySettings.HudScale,
                AnimationAccessibility.ReducedMotion, AnimationAccessibility.MotionOff,
                AnimationAccessibility.CameraComfort, LightingAccessibility.FlashReduction,
                LightingAccessibility.PhotosensitivitySafeMode, LightingAccessibility.HighContrast,
                CombatAccessibilitySettings.AudioVisualCueForWarnings,
                QualitySettings.GetQualityLevel(), QualitySettings.vSyncCount);

            internal List<string> DifferencesFromNow()
            {
                SettingsSnapshot now = Capture();
                var differences = new List<string>();
                Compare(differences, "NebulaUiShell.UiScale", uiScale, now.uiScale);
                Compare(differences, "CombatAccessibilitySettings.HudScale", hudScale, now.hudScale);
                Compare(differences, "AnimationAccessibility.ReducedMotion", reducedMotion, now.reducedMotion);
                Compare(differences, "AnimationAccessibility.MotionOff", motionOff, now.motionOff);
                Compare(differences, "AnimationAccessibility.CameraComfort", cameraComfort, now.cameraComfort);
                Compare(differences, "LightingAccessibility.FlashReduction", flashReduction, now.flashReduction);
                Compare(differences, "LightingAccessibility.PhotosensitivitySafeMode", safeMode, now.safeMode);
                Compare(differences, "LightingAccessibility.HighContrast", highContrast, now.highContrast);
                Compare(differences, "CombatAccessibilitySettings.AudioVisualCueForWarnings", cue, now.cue);
                Compare(differences, "QualitySettings.GetQualityLevel()", qualityLevel, now.qualityLevel);
                Compare(differences, "QualitySettings.vSyncCount", vSync, now.vSync);
                return differences;
            }

            private static void Compare<T>(List<string> into, string name, T before, T after)
            {
                if (!EqualityComparer<T>.Default.Equals(before, after))
                    into.Add($"{name} leaked: {before} -> {after}");
            }
        }

        private static IEnumerable<TutorialStepDefinition> MySteps()
        {
            foreach (TutorialStepDefinition step in TutorialStepCatalog.InChapter(TutorialChapterId.Flight))
                yield return step;
            foreach (TutorialStepDefinition step in TutorialStepCatalog.InChapter(TutorialChapterId.Exploration))
                yield return step;
            foreach (TutorialStepDefinition step in TutorialStepCatalog.InChapter(TutorialChapterId.Session))
                yield return step;
        }
    }
}
