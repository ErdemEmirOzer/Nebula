using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Tutorial;
using Nebula.UI.Design;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.UI.Tutorial;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Photographs every tutorial surface, at every resolution and accessibility
    /// mode that has to hold, and refuses a frame that is not really a picture.
    /// <para>
    /// The refusal is the point. This project has already shipped a screenshot
    /// suite of seventeen blank PNGs that passed, because <c>-nographics</c> gives
    /// no graphics device, <c>Camera.Render()</c> draws nothing and
    /// <c>ReadPixels</c> returns flat grey. Every capture here is therefore run
    /// with a device and measured for colour variety before it is written.
    /// </para>
    /// <para>
    /// One fixture, one process. Stacking capture fixtures in a single Unity run
    /// drives <c>UnloadUnusedAssets</c> from 10ms to a minute and the run stops
    /// producing output at all.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class TutorialCaptureTests
    {
        private static string OutputDir
        {
            get
            {
                string dir = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", "TutorialDesign", "Captures"));
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private NebulaUiShell shell;
        private float enteredScale;
        private bool enteredMotionOff, enteredReducedMotion, enteredHighContrast;

        [SetUp]
        public void SetUp()
        {
            enteredScale = NebulaUiShell.UiScale;
            enteredMotionOff = AnimationAccessibility.MotionOff;
            enteredReducedMotion = AnimationAccessibility.ReducedMotion;
            enteredHighContrast = LightingAccessibility.HighContrast;
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void TearDown()
        {
            if (shell != null)
            {
                shell.Shutdown();
                if (shell.gameObject != null) UnityEngine.Object.DestroyImmediate(shell.gameObject);
            }
            shell = null;
            NebulaUiShell.UiScale = enteredScale;
            AnimationAccessibility.MotionOff = enteredMotionOff;
            AnimationAccessibility.ReducedMotion = enteredReducedMotion;
            LightingAccessibility.HighContrast = enteredHighContrast;
            TutorialRunRequest.Clear();
        }

        // ---- the main menu, with the tutorial entry on it ----------------------

        [Test]
        public void MainMenuWithTheTutorialEntry_AtEveryResolution()
        {
            NebulaMainMenuScreen.Create(shell, new SilentLauncher());

            Capture(1280, 720, "menu-1280x720.png");
            Capture(1920, 1080, "menu-1920x1080.png");
            Capture(2560, 1080, "menu-ultrawide-2560x1080.png");
        }

        [Test]
        public void MainMenu_AtTheLargestInterfaceScale()
        {
            // The scale at which the tutorial entry pushed the accessibility row off
            // the bottom. Photographed so a regression is visible, not just asserted.
            NebulaUiShell.UiScale = NebulaUiShell.MaxUiScale;
            NebulaMainMenuScreen.Create(shell, new SilentLauncher());
            Capture(1920, 1080, "menu-uiscale-140.png");
        }

        [Test]
        public void MainMenu_HighContrast()
        {
            LightingAccessibility.HighContrast = true;
            NebulaMainMenuScreen.Create(shell, new SilentLauncher());
            Capture(1920, 1080, "menu-high-contrast.png");
        }

        // ---- the tutorial's own screens ---------------------------------------

        [Test]
        public void TutorialModeSelection()
        {
            var launcher = new SilentLauncher();
            shell.Push(TutorialModeScreen.Create(shell, launcher));
            Capture(1920, 1080, "tutorial-mode-1920x1080.png");
            Capture(1280, 720, "tutorial-mode-1280x720.png");
        }

        [Test]
        public void ChapterList()
        {
            var launcher = new SilentLauncher();
            shell.Push(TutorialChapterListScreen.Create(shell, launcher));
            Capture(1920, 1080, "tutorial-chapters-1920x1080.png");
            Capture(2560, 1080, "tutorial-chapters-ultrawide.png");
        }

        [Test]
        public void ChapterList_MotionOff()
        {
            AnimationAccessibility.MotionOff = true;
            AnimationAccessibility.ReducedMotion = true;
            shell.Push(TutorialChapterListScreen.Create(shell, new SilentLauncher()));
            Capture(1920, 1080, "tutorial-chapters-motion-off.png");
        }

        // ---- the instruction card ---------------------------------------------

        [Test]
        public void InstructionCard_ForARepresentativeStepOfEveryChapter()
        {
            Assert.That(TutorialStepCatalog.Count, Is.GreaterThan(0), "the curriculum is empty");

            TutorialHud hud = TutorialHud.Create(shell, new SilentLauncher());

            foreach (TutorialChapter chapter in TutorialStepCatalog.Chapters)
            {
                IReadOnlyList<TutorialStepDefinition> steps =
                    TutorialStepCatalog.InChapter(chapter.Id);
                if (steps.Count == 0) continue;

                // The chapter's first step with a checklist, so the captured card
                // shows the sub-objective rows rather than the simplest possible one.
                TutorialStepDefinition step =
                    steps.FirstOrDefault(s => s.ChecklistTr.Count > 0) ?? steps[0];

                hud.ShowStep(step, TutorialStepCatalog.Count);
                hud.Card.Tick(1f);
                Capture(1920, 1080, $"card-{chapter.Letter}-{step.StepId}.png");
            }
        }

        [Test]
        public void InstructionCard_ShowingCorrectiveFeedbackForAnIncompleteStep()
        {
            TutorialHud hud = TutorialHud.Create(shell, new SilentLauncher());
            TutorialStepDefinition step = TutorialStepCatalog.All
                .FirstOrDefault(s => s.ChecklistTr.Count > 1) ?? TutorialStepCatalog.All[0];

            hud.ShowStep(step, TutorialStepCatalog.Count);

            var report = new TutorialStepReport();
            report.Reset(step.ChecklistTr.Count);
            if (step.ChecklistTr.Count > 0) report.SetObjective(0, true);
            report.Reject("MENZİL DIŞINDA — HEDEFE YAKLAŞ");
            hud.UpdateReport(report);
            hud.Card.Tick(1f);

            Capture(1920, 1080, "card-reject-feedback.png");
            Capture(1280, 720, "card-reject-feedback-1280x720.png");
        }

        [Test]
        public void InstructionCard_ForAReadingStepOffersTheConfirmation()
        {
            // The one card that carries a control. Photographed because "the button
            // is only on reading steps" is a rule about what the player sees, and a
            // capture is the only check that looks at what the player sees.
            TutorialStepDefinition reading = TutorialStepCatalog.All
                .FirstOrDefault(s => s.Kind == TutorialStepKind.InformationAcknowledgement);
            Assert.IsNotNull(reading,
                "no InformationAcknowledgement step in the catalog — the acknowledgement " +
                "gate exists but nothing uses it");

            TutorialHud hud = TutorialHud.Create(shell, new SilentLauncher());
            hud.ShowStep(reading, TutorialStepCatalog.Count);
            hud.Card.Tick(1f);

            Assert.IsTrue(hud.AcknowledgementOffered,
                $"{reading.StepId} is a reading step but the card offered no confirmation");
            // The confirmation row is the only thing that ever makes this card taller
            // than it was designed to be, and a card that reaches the middle of the
            // screen is sitting on the reticle the player is being asked to use.
            Assert.Greater(hud.Card.ReticleClearance, 0f,
                "the confirmation row pushed the card down onto the reticle");

            Capture(1920, 1080, $"card-acknowledge-{reading.StepId}.png");
            // 720p as well: the card is bounded against the live interface scale, and
            // the small canvas is where the row runs out of room first.
            Capture(1280, 720, $"card-acknowledge-{reading.StepId}-1280x720.png");
        }

        [Test]
        public void InstructionCard_ForAGameplayStepOffersNoConfirmation()
        {
            TutorialStepDefinition interactive = TutorialStepCatalog.All
                .First(s => s.Kind == TutorialStepKind.InteractiveAction);

            TutorialHud hud = TutorialHud.Create(shell, new SilentLauncher());
            hud.ShowStep(interactive, TutorialStepCatalog.Count);
            hud.Card.Tick(1f);

            Assert.IsFalse(hud.AcknowledgementOffered,
                $"{interactive.StepId} is a gameplay step and must offer no way to confirm past it");
            Capture(1920, 1080, "card-no-acknowledge-gameplay-step.png");
        }

        [Test]
        public void InstructionCard_MotionOffSnapsStraightToItsRestPose()
        {
            AnimationAccessibility.MotionOff = true;
            TutorialHud hud = TutorialHud.Create(shell, new SilentLauncher());
            hud.ShowStep(TutorialStepCatalog.All[0], TutorialStepCatalog.Count);

            // No Tick at all: with motion off the card must already be at its final
            // pose, so the frame is legible without a single frame of animation.
            Assert.IsTrue(hud.Card.MotionSnapped, "the card did not snap with motion off");
            Capture(1920, 1080, "card-motion-off.png");
        }

        [Test]
        public void InstructionCard_HighContrast()
        {
            LightingAccessibility.HighContrast = true;
            TutorialHud hud = TutorialHud.Create(shell, new SilentLauncher());
            TutorialStepDefinition step = TutorialStepCatalog.All
                .FirstOrDefault(s => s.ChecklistTr.Count > 0) ?? TutorialStepCatalog.All[0];
            hud.ShowStep(step, TutorialStepCatalog.Count);
            hud.Card.Tick(1f);
            Capture(1920, 1080, "card-high-contrast.png");
        }

        // ---- the end of a run ---------------------------------------------------

        [Test]
        public void SummaryScreen_ForBothRunModes()
        {
            TutorialHud hud = TutorialHud.Create(shell, new SilentLauncher());

            hud.ShowSummary(TutorialRunMode.Sequential,
                TutorialStepCatalog.Count, TutorialStepCatalog.Count);
            Capture(1920, 1080, "summary-complete.png");

            hud.ShowSummary(TutorialRunMode.Standalone, 1, TutorialStepCatalog.Count);
            Capture(1920, 1080, "summary-standalone.png");
        }

        // ---- capture ------------------------------------------------------------

        /// <summary>
        /// Renders the shell's canvases at a resolution, checks the frame is really
        /// a picture, and writes the PNG. Any failure leaves no file behind, so the
        /// output directory never contains a frame that did not pass.
        /// </summary>
        private void Capture(int width, int height, string fileName)
        {
            Canvas[] canvases = shell.gameObject.GetComponentsInChildren<Canvas>(true);
            Assert.That(canvases.Length, Is.GreaterThan(0), "the shell has no canvas to render");

            var savedMode = new RenderMode[canvases.Length];
            var savedCamera = new Camera[canvases.Length];
            var savedDistance = new float[canvases.Length];

            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("Tutorial Capture Camera");
            Camera camera = cameraObject.AddComponent<Camera>();

            try
            {
                if (!target.Create())
                    Assert.Ignore($"SKIPPED — no graphics device would allocate {width}x{height}, "
                                  + $"so {fileName} cannot be captured");

                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = NebulaUiDesignTokens.SpaceVoid;
                camera.orthographic = true;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0f, 0f, -100f);

                for (int i = 0; i < canvases.Length; i++)
                {
                    savedMode[i] = canvases[i].renderMode;
                    savedCamera[i] = canvases[i].worldCamera;
                    savedDistance[i] = canvases[i].planeDistance;
                    if (!canvases[i].isActiveAndEnabled) continue;
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = camera;
                    canvases[i].planeDistance = 100f;
                }

                // Repointing a canvas at a camera re-runs its CanvasScaler, and the
                // scaler owns scaleFactor -- so the shell's scale is applied after
                // the repoint, not before it.
                shell.ApplyScale();
                Canvas.ForceUpdateCanvases();

                camera.Render();

                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                try
                {
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    texture.Apply();
                    AssertTheFrameHasAPictureOnIt(texture.GetPixels32(), width, height, fileName);
                    File.WriteAllBytes(Path.Combine(OutputDir, fileName), texture.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i] == null) continue;
                    canvases[i].renderMode = savedMode[i];
                    canvases[i].worldCamera = savedCamera[i];
                    canvases[i].planeDistance = savedDistance[i];
                }
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// A flat frame is what a headless run produces, and it is indistinguishable
        /// from success unless something counts the colours in it.
        /// </summary>
        private static void AssertTheFrameHasAPictureOnIt(Color32[] pixels, int width, int height,
            string fileName)
        {
            var distinct = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i += 2)
                distinct.Add((pixels[i].r << 16) | (pixels[i].g << 8) | pixels[i].b);

            Assert.Greater(distinct.Count, 1, fileName + " is a single flat colour — nothing rendered");
            Assert.Greater(distinct.Count, 64,
                $"{fileName} has only {distinct.Count} distinct colours, which is not a rendered interface");

            Debug.Log($"[tutorial-capture] {fileName}: {width}x{height}, {distinct.Count} distinct colours");
        }

        /// <summary>A launcher that records and does nothing — no scene load, no session.</summary>
        private sealed class SilentLauncher : ITutorialLauncher, INebulaSessionLauncher
        {
            public int Requests { get; private set; }

            public void StartFromBeginning() => Requests++;
            public void StartSingleStep(string stepId) => Requests++;
            public void ExitToMainMenu() => Requests++;
            public event Action<TutorialRunMode, bool> RunEnded;

            public void StartNewCampaign() => Requests++;
            public void ContinueFromSlot(int slot) => Requests++;
            public void QuitGame() { }
            public void ReturnToMainMenu() { }
            public void RestartCampaign() => Requests++;

            internal void Silence() => RunEnded?.Invoke(TutorialRunMode.Sequential, false);
        }
    }
}
