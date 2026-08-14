using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.UI.Bench;
using Nebula.UI.Controls;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Photographs the states this part actually changed, from a real booted session.
    /// <para>
    /// Evidence, not decoration. A timed batch, a paused one, a cancel that reported
    /// itself and an output waiting on a full hold are all states that only exist
    /// because the lifecycle was restored — a screenshot is the only artefact that
    /// shows the player's side of that, and the only one that catches a control which
    /// is present in the tree and invisible on screen.
    /// </para>
    /// <para>
    /// One fixture, one Unity process, and every capture inside it. Stacking capture
    /// fixtures in a single run drives <c>UnloadUnusedAssets</c> from 10ms to a minute
    /// apiece and the run stops producing output altogether; that has already cost two
    /// dead runs in this project.
    /// </para>
    /// <para>
    /// Needs a graphics device. Under <c>-nographics</c> there are no pixels to read
    /// and every file would be a flat rectangle, so this skips loudly rather than
    /// shipping blanks — this suite has shipped seventeen of them before.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class Part1RuntimeCaptureTests
    {
        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "UIDesign", "Runtime", "Part1");

        private UiFinalPlayMode.Globals globals;

        [SetUp]
        public void CaptureTheWorldAsItWasFound() => globals = UiFinalPlayMode.CaptureGlobals();

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();
            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
        }

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); run with --graphics");
        }

        /// <summary>
        /// Presses a control the way a player does, and refuses to press one a player
        /// could not.
        /// <para>
        /// These captures used to call <c>onClick.Invoke()</c>, which runs the listener
        /// whether or not the button is interactable. A regression that left a control
        /// permanently disabled would therefore still have produced every photograph
        /// below — of states no player can reach — and the suite would have stayed green
        /// while documenting a dead interface. The interactability check is the assertion;
        /// the pointer events are how the click gets there.
        /// </para>
        /// </summary>
        private static void Press(Selectable control)
        {
            Assert.IsNotNull(control, "there is no control here to press");
            Assert.IsTrue(control.gameObject.activeInHierarchy,
                $"{control.gameObject.name} is not on screen, so no player could press it");
            Assert.IsTrue(control.IsInteractable(),
                $"{control.gameObject.name} is disabled: a disabled control is not clicked, its reason is read");

            EventSystem events = EventSystem.current;
            var pointer = new PointerEventData(events)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
            ExecuteEvents.Execute(control.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(control.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(control.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        // =====================================================================
        // Capture
        // =====================================================================

        /// <summary>
        /// A capture that drew nothing must fail rather than ship. Writing a PNG
        /// succeeds whether or not anything rendered into it, which is exactly how a
        /// green suite once produced seventeen flat grey files.
        /// </summary>
        private static void Save(Texture2D shot, string fileName)
        {
            Assert.IsNotNull(shot, $"{fileName}: nothing was captured");

            Color32[] pixels = shot.GetPixels32();
            Color32 first = pixels[0];
            bool uniform = true;
            for (int i = 1; i < pixels.Length && uniform; i++)
                if (pixels[i].r != first.r || pixels[i].g != first.g || pixels[i].b != first.b)
                    uniform = false;
            Assert.IsFalse(uniform, $"{fileName} is a single flat colour — nothing rendered");

            Directory.CreateDirectory(OutputDir);
            File.WriteAllBytes(Path.Combine(OutputDir, fileName), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
        }

        /// <summary>
        /// Renders the live shell through a capture camera at an explicit resolution.
        /// <para>
        /// Not <c>WaitForEndOfFrame</c> + <c>CaptureScreenshotAsTexture</c>: that
        /// coroutine never resumes in batch mode and hangs until the timeout. Driving
        /// a camera works with no presented frame.
        /// </para>
        /// </summary>
        private static IEnumerator Capture(string fileName, int width = 1920, int height = 1080)
        {
            yield return null;

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("Part1CaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();

            var canvases = Object.FindObjectsOfType<Canvas>(true);
            var savedMode = new RenderMode[canvases.Length];
            var savedCamera = new Camera[canvases.Length];
            var savedDistance = new float[canvases.Length];
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Nebula.UI.Design.NebulaUiDesignTokens.SpaceVoid;
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

                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = target;
                var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();
                Save(shot, fileName);
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
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        // =====================================================================
        // Harness — the same seams the player's own inputs travel
        // =====================================================================

        private static NebulaContext Ctx()
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            Assert.IsNotNull(context, "there is no live session behind this capture");
            return context;
        }

        private static ShipProcessing Bay() => Ctx().Ship.GetComponent<ShipProcessing>();

        private static LogisticsCargo Hold() => Ctx().Ship.GetComponent<ShipExtraction>().Cargo;

        private static NebulaResourcesPage OpenResources()
        {
            NebulaParentMenuScreen menu = Ctx().Presentation.ParentMenu;
            Assert.IsNotNull(menu, "the session built no parent menu");
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Resources),
                "the menu refused to open on Resources");

            var page = menu.PageOf<NebulaResourcesPage>();
            Assert.IsNotNull(page, "the command menu carries no Resources page");
            page.Refresh();
            return page;
        }

        private static void SetProcessingTier(int tier)
        {
            ShipModules modules = Ctx().Modules;
            modules.Modules.SetTier(ModuleCatalog.ResourceProcessingId, tier);
            modules.ApplyFunctionalEffect(ModuleCatalog.ResourceProcessingId);
        }


        private static IEnumerator BootWithABay(int tier = 2)
        {
            RequireGraphicsDevice();
            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return UiFinalPlayMode.SettleAfterSessionStart();
            SetProcessingTier(tier);
            EmptyBench();
            yield return null;
        }

        /// <summary>The raw family every bench capture uses. Index 0 is Metal.</summary>
        private static string Raw => ExtractionCatalog.ResourceIds[0];

        /// <summary>Stocks the hold through the real load path.</summary>
        private static void StockRaw(float units = 40f) => Hold().LoadItem(Raw, units);

        /// <summary>
        /// Leaves the bench genuinely empty before a capture.
        /// <para>
        /// A session started from a save, or one a previous test left loaded, would
        /// otherwise photograph someone else's state — and the empty-bench capture is
        /// the one piece of evidence that cannot be faked by arranging it afterwards.
        /// </para>
        /// </summary>
        private static void EmptyBench()
        {
            ProcessingBenchRuntime bench = Bay().Bench;
            if (!bench.IsEmpty) ProcessingBenchCommands.TryWithdrawAll(bench, Hold(), out _);
            bench.Clear();
        }

        /// <summary>Loads the bench through the real command, the way a confirmed drop does.</summary>
        private static void LoadBench(int amount)
        {
            Assert.AreEqual(BenchDepositReason.None,
                ProcessingBenchCommands.TryDeposit(Bay().Bench, Hold(), Raw, amount),
                "the bench refused the deposit this capture is about");
        }

        /// <summary>The catalog index of the 3-to-1 material recipe for the captured family.</summary>
        private static int MaterialRecipe()
        {
            foreach (int index in ProcessingBenchCommands.RecipeIndicesFor(Raw))
                if (ProcessingCatalog.Recipes[index].InputCount == 3) return index;
            Assert.Fail("the catalog no longer has a three-to-one recipe for " + Raw);
            return -1;
        }

        // =====================================================================
        // Captures — the processing bench
        //
        // Every state below is reached through the page's own controls or the one
        // backend command seam behind them, never by writing the bench. A capture of
        // a state the player cannot produce is not evidence of anything.
        // =====================================================================

        /// <summary>The bench with nothing on it: the drop invitation and the empty recipe column.</summary>
        [UnityTest]
        public IEnumerator BenchEmptyAndInviting()
        {
            yield return BootWithABay();
            StockRaw();

            NebulaResourcesPage page = OpenResources();
            Assert.AreEqual(0, page.BenchAmount, "the bench was not empty for the empty capture");

            yield return Capture("01-bench-empty.png");
        }

        /// <summary>
        /// Mid-drag: the ghost is up and following the pointer, and — decisively —
        /// nothing has moved yet. A drag that had already paid would make every later
        /// capture a lie.
        /// </summary>
        [UnityTest]
        public IEnumerator BenchWhileDraggingAStackOntoIt()
        {
            yield return BootWithABay();
            StockRaw();

            NebulaResourcesPage page = OpenResources();
            float heldBefore = Hold().GetStored(Raw);

            Assert.IsTrue(page.BeginBenchDrag(Raw), "the cargo row refused to start a drag");
            page.HoverBench(entering: true);
            page.Refresh();

            Assert.IsTrue(page.IsDragging, "the drag ended before it could be photographed");
            Assert.AreEqual(heldBefore, Hold().GetStored(Raw), 0.001f,
                "the drag itself moved stock");
            Assert.AreEqual(0, page.BenchAmount, "the drag loaded the bench before any confirmation");

            yield return Capture("02-bench-drag-in-progress.png");
            page.EndBenchDrag();
        }

        /// <summary>The deposit box the drop opens, with the hold's real whole-unit ceiling.</summary>
        [UnityTest]
        public IEnumerator BenchDepositQuantityBox()
        {
            yield return BootWithABay();
            StockRaw();

            NebulaResourcesPage page = OpenResources();
            Assert.IsTrue(page.DragToBench(Raw), "the drop did not reach the bench");
            page.Refresh();

            Assert.IsTrue(page.Modal.IsOpen, "the drop did not ask how many");
            Assert.AreEqual(QuantityMode.Deposit, page.Modal.Mode);
            Assert.AreEqual(0, page.BenchAmount, "the box was open and the bench had already moved");
            page.Modal.SetValue(25);

            yield return Capture("03-bench-deposit-quantity-box.png");
        }

        /// <summary>Twenty-five units on the bench, loaded through the confirmed box.</summary>
        [UnityTest]
        public IEnumerator BenchLoadedWithTwentyFiveUnits()
        {
            yield return BootWithABay();
            StockRaw();

            NebulaResourcesPage page = OpenResources();
            Assert.IsTrue(page.DragToBench(Raw), "the drop did not reach the bench");
            page.Modal.SetValue(25);
            Press(page.Modal.ConfirmButton);
            page.Refresh();

            Assert.AreEqual(25, page.BenchAmount, "the confirmed box did not load twenty-five");
            yield return Capture("04-bench-loaded-25.png");
        }

        /// <summary>
        /// The recipe column for the loaded input: every recipe that input feeds, in
        /// catalog order, each with its real ratio and its real ceiling.
        /// </summary>
        [UnityTest]
        public IEnumerator BenchConversionOptionsForTheLoadedInput()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(25);

            NebulaResourcesPage page = OpenResources();
            int expected = ProcessingBenchCommands.RecipeIndicesFor(Raw).Count;
            Assert.AreEqual(expected, page.VisibleRecipeCards,
                "the column does not offer exactly the recipes this input feeds");
            Assert.Greater(expected, 1, "this capture is meant to show more than one option");

            yield return Capture("05-bench-conversion-options.png");
        }

        /// <summary>
        /// The conversion box on a 3-to-1 recipe with 25 on the bench: the ceiling is
        /// 24, not 25, and the preview says what 24 buys and what stays behind.
        /// </summary>
        [UnityTest]
        public IEnumerator BenchConversionQuantityBoxPreviewsTwentyFourToEight()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(25);

            NebulaResourcesPage page = OpenResources();
            int material = MaterialRecipe();
            Press(page.RecipeCardButton(material));
            Press(page.RecipeConvertButton(material));
            page.Refresh();

            Assert.IsTrue(page.Modal.IsOpen, "the convert control ran without asking how much");
            Assert.AreEqual(QuantityMode.Convert, page.Modal.Mode);
            Assert.AreEqual(3, page.Modal.Step, "the box does not step by the recipe's input count");
            Assert.AreEqual(24, page.Modal.Max,
                "the ceiling was not rounded down to a whole batch — 25 is not a multiple of 3");

            page.Modal.SetValue(24);
            page.Refresh();
            yield return Capture("06-bench-convert-box-24-to-8.png");
        }

        /// <summary>
        /// Immediately after the conversion: eight alloy in the hold, one unit left on
        /// the bench, the delta on screen — and no progress bar anywhere, because there
        /// is nothing left to wait for.
        /// </summary>
        [UnityTest]
        public IEnumerator BenchAfterConvertingTwentyFourLeavingOne()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(25);

            NebulaResourcesPage page = OpenResources();
            int material = MaterialRecipe();
            ProcessingRecipe recipe = ProcessingCatalog.Recipes[material];
            float outputBefore = Hold().GetStored(recipe.OutputItemId);

            page.SelectRecipe(material);
            Assert.AreEqual(BenchConvertReason.None, page.Convert(material, 24),
                "the conversion this capture is about was refused: " + page.FeedbackText);
            page.Refresh();

            Assert.AreEqual(1, page.BenchAmount, "the leftover on the bench is not one unit");
            Assert.AreEqual(outputBefore + 8f, Hold().GetStored(recipe.OutputItemId), 0.001f,
                "twenty-four units of a three-to-one recipe did not produce eight outputs");

            yield return Capture("07-bench-after-convert-1-left.png");
        }

        /// <summary>Part of the bench taken back, the rest still loaded.</summary>
        [UnityTest]
        public IEnumerator BenchAfterAPartialWithdrawal()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(20);

            NebulaResourcesPage page = OpenResources();
            Assert.AreEqual(BenchWithdrawReason.None, page.Withdraw(8),
                "the partial withdrawal was refused: " + page.FeedbackText);
            page.Refresh();

            Assert.AreEqual(12, page.BenchAmount, "the withdrawal took the wrong amount");
            yield return Capture("08-bench-partial-withdraw.png");
        }

        /// <summary>
        /// A different material refused at the drop: the bench keeps what it has, the
        /// zone reads as invalid, and the page says which resource is in the way.
        /// </summary>
        [UnityTest]
        public IEnumerator BenchRefusingADifferentMaterial()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(15);

            string other = ExtractionCatalog.ResourceIds[1];
            Hold().LoadItem(other, 20f);

            NebulaResourcesPage page = OpenResources();
            Assert.AreEqual(BenchDepositReason.DifferentInputAlreadyLoaded,
                page.Deposit(other, 4),
                "the bench accepted a second input type");
            page.Refresh();

            Assert.AreEqual(15, page.BenchAmount, "the refused drop changed the bench");
            Assert.AreEqual(Raw, page.BenchItemId, "the refused drop changed the bench's item");

            yield return Capture("09-bench-different-material-refused.png");
        }

        /// <summary>
        /// A conversion refused because the hold has no room for the output. Nothing is
        /// spent, nothing is owed, and the reason is on screen — the state the old
        /// model called a pending output cannot happen any more.
        /// </summary>
        [UnityTest]
        public IEnumerator BenchRefusingAConversionWithAFullHold()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(24);

            // Fill the hold to its capacity with something the recipe does not consume,
            // so the only thing missing is room for the output.
            LogisticsCargo hold = Hold();
            hold.LoadItem(ProcessingCatalog.BasicPartId,
                Mathf.Max(0f, hold.Capacity / LogisticsCargo.VolumePerUnit(ProcessingCatalog.BasicPartId)));

            NebulaResourcesPage page = OpenResources();
            int material = MaterialRecipe();
            page.SelectRecipe(material);

            Assert.AreEqual(BenchConvertReason.OutputNoRoom, page.Convert(material, 24),
                "a full hold did not refuse the conversion");
            page.Refresh();

            Assert.AreEqual(24, page.BenchAmount, "a refused conversion spent the bench");
            yield return Capture("10-bench-cargo-full-refused.png");
        }

        /// <summary>
        /// The bench reached without a mouse: the focus ring resting on a real control,
        /// which is the whole of the keyboard/gamepad claim.
        /// </summary>
        [UnityTest]
        public IEnumerator BenchUnderKeyboardAndGamepadFocus()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(18);

            NebulaResourcesPage page = OpenResources();
            page.FocusFirstControl();
            yield return null;

            GameObject focused = EventSystem.current == null
                ? null : EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(focused, "nothing holds focus, so there is no gamepad path to photograph");

            yield return Capture("11-bench-gamepad-focus.png");
        }

        /// <summary>The whole page at the largest UI scale the settings offer.</summary>
        [UnityTest]
        public IEnumerator BenchAtTheLargestUiScale()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(25);

            float entered = NebulaUiShell.UiScale;
            try
            {
                NebulaUiShell.UiScale = 1.4f;
                yield return null;
                OpenResources();
                yield return null;
                yield return Capture("12-bench-ui-scale-140.png");
            }
            finally
            {
                NebulaUiShell.UiScale = entered;
            }
        }

        /// <summary>The smallest supported window: every control still reachable.</summary>
        [UnityTest]
        public IEnumerator BenchAt1280x720()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(25);

            OpenResources();
            yield return Capture("13-bench-1280x720.png", 1280, 720);
        }

        /// <summary>Ultrawide: the three columns must spread, not stretch.</summary>
        [UnityTest]
        public IEnumerator BenchAt3440x1440()
        {
            yield return BootWithABay();
            StockRaw();
            LoadBench(25);

            OpenResources();
            yield return Capture("14-bench-3440x1440.png", 3440, 1440);
        }


        // =====================================================================
        // Captures — controls
        // =====================================================================

        /// <summary>The key a combat action answers to right now, read from the live binding service.</summary>
        private static KeyCode KeyNowBoundTo(CombatAction action)
        {
            foreach ((CombatAction bound, KeyCode key) in CombatBindings.CurrentBindings())
                if (bound == action) return key;
            return KeyCode.None;
        }

        private static NebulaSettingsPage OpenSettings()
        {
            NebulaParentMenuScreen menu = Ctx().Presentation.ParentMenu;
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Settings),
                "the menu refused to open on Settings");

            var page = menu.PageOf<NebulaSettingsPage>();
            Assert.IsNotNull(page, "the command menu carries no Settings page");
            return page;
        }

        /// <summary>
        /// The controls screen with the U collision gone: the command-menu key and the
        /// package cancel are both listed, each named, with no standing conflict.
        /// </summary>
        [UnityTest]
        public IEnumerator ControlsScreenWithTheUCollisionResolved()
        {
            RequireGraphicsDevice();
            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            NebulaSettingsPage page = OpenSettings();
            yield return null;

            Assert.IsEmpty(NebulaControlBindings.ConflictWarnings(),
                "the controls screen opens already warning about a conflict");

            yield return Capture("08-controls-no-u-collision.png");
        }

        /// <summary>
        /// The duplicate-binding warning, produced the way a player produces it: by
        /// asking the real rebind seam for a key another combat action already holds.
        /// The refusal is what is being photographed, so the binding must not take.
        /// </summary>
        [UnityTest]
        public IEnumerator ControlsScreenRefusingADuplicateBinding()
        {
            RequireGraphicsDevice();
            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            NebulaSettingsPage page = OpenSettings();
            yield return null;

            // The combat bindings live behind their own control, so the panel has to be
            // opened the way a player opens it — otherwise the conflict label this
            // capture is about is not on screen at all, and the file would be a
            // perfectly valid photograph of the wrong thing.
            Button openBindings = UiFinalPlayMode.Require(
                UiFinalPlayMode.MenuRoot(), NebulaSettingsPage.OpenBindingsControlName,
                "the settings page has no control to open the combat bindings");
            Press(openBindings);
            yield return null;

            // Two live combat actions, and the second is offered the first one's key.
            var bound = new List<(CombatAction action, KeyCode key)>();
            foreach ((CombatAction action, KeyCode key) in CombatBindings.CurrentBindings())
                bound.Add((action, key));
            Assert.Greater(bound.Count, 1, "there are not two combat bindings to collide");

            CombatAction victim = bound[1].action;
            KeyCode taken = bound[0].key;
            Assert.AreEqual(ControlRebindResult.ConflictsWithAction,
                NebulaControlBindings.Check(victim, taken),
                "the rebind seam does not consider an already-held key a conflict");

            // Apply is the one seam the capture listener's own Update calls, so this is
            // the production path a player's keypress takes, not a shortcut past it.
            page.Bindings.BeginCapture(victim);
            ControlRebindResult applied = page.Bindings.Apply(taken);
            yield return null;

            Assert.AreEqual(ControlRebindResult.ConflictsWithAction, applied,
                "the duplicate key was not refused");
            Assert.AreEqual(bound[1].key, KeyNowBoundTo(victim),
                "the refused duplicate was taken anyway");

            // The refusal has to be legible on the page, not merely returned to the
            // caller. Without this the capture proves only that a PNG was written.
            StringAssert.Contains(NebulaText.Upper(NebulaControlBindings.ActionName(bound[0].action)),
                NebulaText.Upper(page.Bindings.StatusText ?? string.Empty),
                "the refusal does not name the action already holding the key");

            yield return Capture("09-controls-duplicate-binding-warning.png");
        }
    }
}
