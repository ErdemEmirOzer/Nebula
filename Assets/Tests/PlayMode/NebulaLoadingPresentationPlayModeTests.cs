using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Brand;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The loading surface: when it appears, what it plays, and — the part that
    /// matters most — that it always goes away.
    /// </summary>
    // ReleaseRegression: this fixture runs headless. The individual tests that need real
    // pixels carry [Category("Graphics")] of their own and must be excluded by that name --
    // NUnit propagates this class category to them, so Graphics is the authoritative one.
    [Category("ReleaseRegression")]
    public class NebulaLoadingPresentationPlayModeTests
    {
        private NebulaUiShell shell;
        private float enteredTimeScale;
        private bool enteredMotionOff;
        private bool enteredReducedMotion;

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            enteredMotionOff = AnimationAccessibility.MotionOff;
            enteredReducedMotion = AnimationAccessibility.ReducedMotion;

            ExclusiveModal.ResetForSession();
            NebulaLoadingPresenter.ForceClear();
            NebulaBrandAssetCatalog.Release();
            AnimationAccessibility.ResetToDefaults();

            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void Teardown()
        {
            NebulaLoadingPresenter.ForceClear();
            if (shell != null) UnityEngine.Object.DestroyImmediate(shell.gameObject);
            shell = null;

            AnimationAccessibility.MotionOff = enteredMotionOff;
            AnimationAccessibility.ReducedMotion = enteredReducedMotion;
            NebulaBrandAssetCatalog.Release();
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
        }

        // ---- lifecycle -------------------------------------------------------

        /// <summary>The surface exists while the work runs and not afterwards.</summary>
        [UnityTest]
        public IEnumerator TheOverlayIsUpForTheOperationAndDownAfterIt()
        {
            bool sawOverlay = false;

            Assert.IsNull(NebulaLoadingOverlay.Active, "precondition: nothing is up");
            NebulaLoadingPresenter.Run(shell, "TEST", () =>
            {
                sawOverlay = NebulaLoadingOverlay.Active != null;
            });

            Assert.IsTrue(sawOverlay, "the operation ran with no loading surface up");
            Assert.IsNull(NebulaLoadingOverlay.Active, "the overlay outlived its operation");
            Assert.IsFalse(NebulaLoadingPresenter.Busy, "the presenter kept a nesting depth");
            yield return null;
        }

        /// <summary>
        /// An operation that throws still takes the surface down.
        /// <para>
        /// The worst available outcome: a loading picture left over a failed
        /// operation is indistinguishable from a hang, and it would cover the error
        /// the screen is trying to show.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AFailingOperationLeavesNoOverlayBehind()
        {
            Assert.Throws<InvalidOperationException>(() =>
                NebulaLoadingPresenter.Run(shell, "TEST",
                    () => throw new InvalidOperationException("composition failed")));

            Assert.IsNull(NebulaLoadingOverlay.Active,
                "the overlay survived a failed operation and is now covering the error");
            Assert.IsFalse(NebulaLoadingPresenter.Busy);
            yield return null;
        }

        /// <summary>
        /// A failing coroutine operation is the same promise on the multi-frame path.
        /// </summary>
        [UnityTest]
        public IEnumerator AFailingAsyncOperationLeavesNoOverlayBehind()
        {
            IEnumerator Failing()
            {
                yield return null;
                throw new InvalidOperationException("scene load failed");
            }

            IEnumerator run = NebulaLoadingPresenter.RunAsync(shell, "TEST", Failing());
            Assert.IsTrue(run.MoveNext(), "the operation should have yielded once");
            Assert.IsNotNull(NebulaLoadingOverlay.Active, "the surface must be up while it runs");
            Assert.Throws<InvalidOperationException>(() => run.MoveNext());

            Assert.IsNull(NebulaLoadingOverlay.Active, "the overlay survived a failed async operation");
            Assert.IsFalse(NebulaLoadingPresenter.Busy);
            yield return null;
        }

        /// <summary>
        /// Nesting produces one overlay, and the inner scope does not take it down
        /// early. A restart runs a teardown and a launch inside one transition.
        /// </summary>
        [UnityTest]
        public IEnumerator NestedOperationsShowExactlyOneOverlay()
        {
            NebulaLoadingOverlay outer = null, inner = null, afterInner = null;

            NebulaLoadingPresenter.Run(shell, "OUTER", () =>
            {
                outer = NebulaLoadingOverlay.Active;
                NebulaLoadingPresenter.Run(shell, "INNER", () => inner = NebulaLoadingOverlay.Active);
                afterInner = NebulaLoadingOverlay.Active;
            });

            Assert.IsNotNull(outer);
            Assert.AreSame(outer, inner, "the inner scope built a second overlay");
            Assert.AreSame(outer, afterInner, "the inner scope tore down the outer scope's overlay");
            Assert.IsNull(NebulaLoadingOverlay.Active, "the outer scope left the overlay up");
            yield return null;
        }

        /// <summary>Two direct Show calls are one overlay, not two stacked.</summary>
        [UnityTest]
        public IEnumerator ShowingTwiceReturnsTheSameSurface()
        {
            NebulaLoadingOverlay first = NebulaLoadingOverlay.Show(shell, "ONE");
            NebulaLoadingOverlay second = NebulaLoadingOverlay.Show(shell, "TWO");
            yield return null;

            Assert.AreSame(first, second);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaLoadingOverlay>(true).Length,
                "more than one loading overlay exists at once");

            NebulaLoadingOverlay.HideActive();
            yield return null;
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsOfType<NebulaLoadingOverlay>(true).Length,
                "hiding left the overlay object behind");
        }

        /// <summary>
        /// The surface does not touch the pause, the input gate or the modal owner.
        /// <para>
        /// It draws on the Overlay layer, which has no raycaster by design. Claiming
        /// the modal gate would mean handing it back correctly on every failure path
        /// — including the ones where the operation threw.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheOverlayNeverTakesModalOwnershipOrThePause()
        {
            float scaleBefore = Time.timeScale;

            NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;

            Assert.AreEqual(scaleBefore, Time.timeScale, "the overlay changed the pause");
            Assert.IsNull(ExclusiveModal.Owner, "the overlay claimed the modal gate");
            Assert.AreEqual(0, shell.StackDepth, "the overlay was pushed as a screen");

            NebulaLoadingOverlay.HideActive();
        }

        // ---- playback --------------------------------------------------------

        /// <summary>
        /// The loop plays the 75 frames in order, at 30fps, over 2.5 seconds.
        /// <para>
        /// Driven by explicit deltas rather than by waiting: a batch-mode frame loop
        /// is not a clock, and a test that slept 2.5 seconds would measure the test
        /// runner instead of the playback.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheLoopPlaysSeventyFiveFramesInOrderAcrossTwoAndAHalfSeconds()
        {
            AnimationAccessibility.ResetToDefaults();
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;

            Assert.IsTrue(overlay.IsAnimated, "the default motion setting must animate");
            Assert.AreEqual(75, overlay.FrameCount);
            Assert.AreEqual(0, overlay.FrameIndex, "playback starts at frame 0");

            const float step = 1f / 30f;
            for (int expected = 1; expected < 75; expected++)
            {
                overlay.AdvanceForTest(step);
                Assert.AreEqual(expected, overlay.FrameIndex,
                    $"after {expected} frame steps the loop should be on frame {expected}");
            }

            // One more step completes 2.5s and wraps to the first frame.
            overlay.AdvanceForTest(step);
            Assert.AreEqual(0, overlay.FrameIndex, "the loop must wrap after 75 frames, not stall on the last");

            NebulaLoadingOverlay.HideActive();
        }

        /// <summary>
        /// A long operation does not drift. The clock is wrapped rather than left to
        /// grow, so frame selection stays exact after minutes rather than losing
        /// precision to a large float.
        /// </summary>
        [UnityTest]
        public IEnumerator PlaybackStaysExactAfterManyLoops()
        {
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;

            for (int loop = 0; loop < 200; loop++) overlay.AdvanceForTest(2.5f);
            Assert.AreEqual(0, overlay.FrameIndex, "after 200 whole loops the loop is back at frame 0");

            overlay.AdvanceForTest(1f / 30f);
            Assert.AreEqual(1, overlay.FrameIndex, "the next step after a wrap is frame 1");

            NebulaLoadingOverlay.HideActive();
        }

        /// <summary>
        /// Playback creates nothing. A loop that allocated a sprite, a material or a
        /// GameObject per frame would allocate 30 a second for as long as a load runs.
        /// </summary>
        [UnityTest]
        public IEnumerator PlaybackCreatesNoObjects()
        {
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;

            int objectsBefore = UnityEngine.Object.FindObjectsOfType<GameObject>(true).Length;
            int spritesBefore = Resources.FindObjectsOfTypeAll<Sprite>().Length;
            int materialsBefore = Resources.FindObjectsOfTypeAll<Material>().Length;

            for (int i = 0; i < 300; i++) overlay.AdvanceForTest(1f / 30f);

            Assert.AreEqual(objectsBefore, UnityEngine.Object.FindObjectsOfType<GameObject>(true).Length,
                "playback created GameObjects");
            Assert.AreEqual(spritesBefore, Resources.FindObjectsOfTypeAll<Sprite>().Length,
                "playback created Sprites");
            Assert.AreEqual(materialsBefore, Resources.FindObjectsOfTypeAll<Material>().Length,
                "playback created Materials");

            NebulaLoadingOverlay.HideActive();
        }

        /// <summary>
        /// The frames actually change what is drawn. Playback that advances an index
        /// without repointing the Image passes every timing assertion above.
        /// </summary>
        [UnityTest]
        public IEnumerator EachStepChangesTheSpriteOnScreen()
        {
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;

            Sprite first = overlay.CurrentSprite;
            overlay.AdvanceForTest(1f / 30f);
            Sprite second = overlay.CurrentSprite;
            overlay.AdvanceForTest(1f / 30f);
            Sprite third = overlay.CurrentSprite;

            Assert.IsNotNull(first);
            Assert.AreNotSame(first, second, "frame 1 draws the same sprite as frame 0");
            Assert.AreNotSame(second, third, "frame 2 draws the same sprite as frame 1");

            NebulaLoadingOverlay.HideActive();
        }

        // ---- accessibility ---------------------------------------------------

        /// <summary>Motion Off shows the still plate and never advances.</summary>
        [UnityTest]
        public IEnumerator MotionOffShowsTheStillPlateAndNeverAnimates()
        {
            AnimationAccessibility.MotionOff = true;
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;

            Assert.IsFalse(overlay.IsAnimated, "Motion Off must not animate");
            Assert.AreEqual(-1, overlay.FrameIndex);

            Sprite still = overlay.CurrentSprite;
            Assert.IsNotNull(still, "Motion Off must still show the plate");
            Assert.AreEqual("brand.loading.static", still.name);

            overlay.AdvanceForTest(1f);
            Assert.AreSame(still, overlay.CurrentSprite, "the plate changed under Motion Off");

            NebulaLoadingOverlay.HideActive();
        }

        /// <summary>
        /// Reduced Motion plays the reduced sheet.
        /// <para>
        /// The first pass fell back to the still plate here, because the reduced
        /// variant shipped only as an APNG and a WebP and Unity imports neither as a
        /// sprite sheet. The second delivery supplies both reduced spritesheets, so
        /// falling back is now a regression rather than the honest answer — which is
        /// what <c>ReducedUsesStatic</c> exists to catch.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ReducedMotionPlaysTheReducedSheet()
        {
            AnimationAccessibility.ReducedMotion = true;
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;

            Assert.IsTrue(overlay.IsAnimated, "Reduced Motion must animate, not hold a still");
            Assert.IsTrue(overlay.IsReduced, "Reduced Motion must play the reduced sheet");
            Assert.AreEqual(NebulaBrandAssetCatalog.LoadingPresentation.ReducedAnimated,
                overlay.ResolvedPresentation);
            Assert.AreEqual(75, overlay.FrameCount, "the reduced sheet is still 75 cells");
            Assert.IsFalse(NebulaBrandAssetCatalog.ReducedUsesStatic,
                "Reduced Motion fell back to the still plate");

            NebulaLoadingOverlay.HideActive();
        }

        /// <summary>
        /// The reduced sheet is a different asset from the normal one, and the two
        /// draw different pictures at the same index.
        /// </summary>
        [UnityTest]
        public IEnumerator TheReducedSheetIsNotTheNormalSheet()
        {
            AnimationAccessibility.ResetToDefaults();
            NebulaLoadingOverlay normal = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;
            normal.AdvanceForTest(10f / 30f);
            Sprite normalFrame = normal.CurrentSprite;
            Assert.IsNotNull(normalFrame);
            NebulaLoadingOverlay.HideActive();
            yield return null;

            AnimationAccessibility.ReducedMotion = true;
            NebulaLoadingOverlay reduced = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;
            reduced.AdvanceForTest(10f / 30f);
            Sprite reducedFrame = reduced.CurrentSprite;
            Assert.IsNotNull(reducedFrame);

            Assert.AreNotSame(normalFrame.texture, reducedFrame.texture,
                "Reduced Motion is drawing out of the normal sheet");
            StringAssert.Contains("reduced", reducedFrame.name, "the reduced frame is not from the reduced sheet");

            NebulaLoadingOverlay.HideActive();
        }

        /// <summary>
        /// Photosensitivity Safe Mode never gets the full-rate loop.
        /// <para>
        /// It is not the same setting as Reduced Motion, but the one thing it must
        /// not be handed is the high-frequency animation.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator PhotosensitivitySafeModeNeverGetsTheFullLoop()
        {
            bool entered = LightingAccessibility.PhotosensitivitySafeMode;
            try
            {
                LightingAccessibility.PhotosensitivitySafeMode = true;
                NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "TEST");
                yield return null;

                Assert.AreNotEqual(NebulaBrandAssetCatalog.LoadingPresentation.Animated,
                    overlay.ResolvedPresentation,
                    "Photosensitivity Safe Mode was handed the full-rate loop");

                NebulaLoadingOverlay.HideActive();
            }
            finally { LightingAccessibility.PhotosensitivitySafeMode = entered; }
        }

        /// <summary>
        /// Turning Reduced Motion on while the surface is up swaps the sheet rather
        /// than continuing the loop the player just asked to stop.
        /// </summary>
        [UnityTest]
        public IEnumerator ChangingTheMotionSettingSwapsTheSheetInPlace()
        {
            AnimationAccessibility.ResetToDefaults();
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "TEST");
            yield return null;
            Assert.IsFalse(overlay.IsReduced, "precondition: the normal sheet is playing");

            AnimationAccessibility.ReducedMotion = true;
            yield return null;

            Assert.IsTrue(overlay.IsReduced, "the surface kept playing the normal sheet");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaLoadingOverlay>(true).Length,
                "the swap built a second overlay");

            AnimationAccessibility.MotionOff = true;
            yield return null;
            Assert.IsFalse(overlay.IsAnimated, "Motion Off did not stop playback");

            NebulaLoadingOverlay.HideActive();
        }

        // ---- transitions -----------------------------------------------------

        /// <summary>
        /// The session entry points raise the surface. Checked through the decorator
        /// the host actually installs, with a launcher that records instead of
        /// composing a universe.
        /// </summary>
        [UnityTest]
        public IEnumerator EverySessionTransitionRaisesTheSurface()
        {
            var probe = new ProbeLauncher();
            var decorated = new NebulaLoadingSessionLauncher(probe, () => shell);

            decorated.StartNewCampaign();
            Assert.IsTrue(probe.SawOverlayOnNew, "YENİ SEFER showed no loading surface");

            decorated.ContinueFromSlot(2);
            Assert.IsTrue(probe.SawOverlayOnContinue, "DEVAM ET showed no loading surface");

            decorated.RestartCampaign();
            Assert.IsTrue(probe.SawOverlayOnRestart, "restart showed no loading surface");

            Assert.IsNull(NebulaLoadingOverlay.Active, "a transition left its surface up");
            yield return null;
        }

        /// <summary>
        /// Quitting does not raise it. There is nothing to wait through, and a
        /// spinner over a closing application reads as a hang on the way out.
        /// </summary>
        [UnityTest]
        public IEnumerator QuittingShowsNoLoadingSurface()
        {
            var probe = new ProbeLauncher();
            var decorated = new NebulaLoadingSessionLauncher(probe, () => shell);

            decorated.QuitGame();
            Assert.IsTrue(probe.Quit);
            Assert.IsFalse(probe.SawOverlayOnQuit, "ÇIKIŞ raised a loading surface");
            Assert.IsNull(NebulaLoadingOverlay.Active);
            yield return null;
        }

        /// <summary>Returning to the menu clears any surface rather than inheriting one.</summary>
        [UnityTest]
        public IEnumerator ReturningToTheMenuClearsTheSurface()
        {
            NebulaLoadingOverlay.Show(shell, "STRANDED");
            Assert.IsNotNull(NebulaLoadingOverlay.Active, "precondition");

            var decorated = new NebulaLoadingSessionLauncher(new ProbeLauncher(), () => shell);
            decorated.ReturnToMainMenu();

            Assert.IsNull(NebulaLoadingOverlay.Active, "a surface survived the return to the menu");
            Assert.IsFalse(NebulaLoadingPresenter.Busy);
            yield return null;
        }

        /// <summary>
        /// No artificial delay is inserted. The surface is up for exactly as long as
        /// the operation, and an instant operation gets an instant surface.
        /// </summary>
        [UnityTest]
        public IEnumerator AnInstantOperationIsNotPaddedOut()
        {
            // Warmed first. The first Show pulls 75 sprites off a 4.9MB compressed
            // sheet, and timing that would measure asset loading rather than the
            // question being asked, which is whether the presenter adds a delay.
            NebulaLoadingOverlay.Show(shell, "WARM");
            NebulaLoadingOverlay.HideActive();
            yield return null;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            NebulaLoadingPresenter.Run(shell, "TEST", () => { });
            clock.Stop();

            Assert.Less(clock.ElapsedMilliseconds, 250L,
                "an instant operation was padded out to make the animation legible");
            yield return null;
        }

        // ---- visual evidence -------------------------------------------------

        /// <summary>
        /// Photographs the loading surface in each accessibility state, so the
        /// Motion Off and Reduced Motion variants can be held against the delivered
        /// storyboard rather than taken on trust.
        /// </summary>
        [UnityTest]
        // Graphics: reads the rendered loading surface back with ReadPixels; on the -nographics null device that
        // path segfaults the Editor rather than returning a blank frame.
        [Category("Graphics")]
        public IEnumerator TheLoadingSurfaceIsPhotographedInEveryMotionState()
        {
            RequireGraphicsDevice();

            yield return CaptureLoading("brand-loading-animated-frame00.png", motionOff: false,
                reducedMotion: false, advance: 0f);
            yield return CaptureLoading("brand-loading-animated-frame37.png", motionOff: false,
                reducedMotion: false, advance: 37f / 30f);
            yield return CaptureLoading("brand-loading-motion-off.png", motionOff: true,
                reducedMotion: false, advance: 0f);
            yield return CaptureLoading("brand-loading-reduced-motion.png", motionOff: false,
                reducedMotion: true, advance: 0f);
        }

        private IEnumerator CaptureLoading(string fileName, bool motionOff, bool reducedMotion, float advance)
        {
            AnimationAccessibility.ResetToDefaults();
            AnimationAccessibility.MotionOff = motionOff;
            AnimationAccessibility.ReducedMotion = reducedMotion;

            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "YÜKLENİYOR");
            Assert.IsNotNull(overlay, "the overlay must build to be photographed");
            if (advance > 0f) overlay.AdvanceForTest(advance);
            yield return null;

            const int width = 1920, height = 1080;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("LoadingCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();

            Canvas overlayCanvas = shell.CanvasFor(UiLayer.Overlay);
            RenderMode savedMode = overlayCanvas.renderMode;
            Camera savedCamera = overlayCanvas.worldCamera;
            RenderTexture previous = RenderTexture.active;

            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Nebula.UI.Design.NebulaUiDesignTokens.SpaceVoid;
                camera.orthographic = true;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0f, 0f, -100f);

                overlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                overlayCanvas.worldCamera = camera;
                overlayCanvas.planeDistance = 100f;

                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = target;
                var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();

                Color32[] pixels = shot.GetPixels32();
                bool uniform = true;
                for (int i = 1; i < pixels.Length && uniform; i++)
                    if (pixels[i].r != pixels[0].r || pixels[i].g != pixels[0].g || pixels[i].b != pixels[0].b)
                        uniform = false;
                Assert.IsFalse(uniform, $"{fileName} is a single flat colour -- nothing rendered");

                Directory.CreateDirectory(OutputDir);
                File.WriteAllBytes(Path.Combine(OutputDir, fileName), shot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(shot);
            }
            finally
            {
                RenderTexture.active = previous;
                overlayCanvas.renderMode = savedMode;
                overlayCanvas.worldCamera = savedCamera;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                NebulaLoadingOverlay.HideActive();
            }
        }

        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); run with graphics enabled to capture");
        }

        /// <summary>Records whether a surface was up while each call ran.</summary>
        private sealed class ProbeLauncher : Nebula.UI.Session.INebulaSessionLauncher
        {
            public bool SawOverlayOnNew, SawOverlayOnContinue, SawOverlayOnRestart, SawOverlayOnQuit;
            public bool Quit;

            public void StartNewCampaign() => SawOverlayOnNew = NebulaLoadingOverlay.Active != null;
            public void ContinueFromSlot(int slot) => SawOverlayOnContinue = NebulaLoadingOverlay.Active != null;
            public void RestartCampaign() => SawOverlayOnRestart = NebulaLoadingOverlay.Active != null;
            public void QuitGame()
            {
                SawOverlayOnQuit = NebulaLoadingOverlay.Active != null;
                Quit = true;
            }
            public void ReturnToMainMenu() { }
        }
    }
}
