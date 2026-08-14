using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.UI.Accessibility;
using Nebula.UI.Bench;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The bench page at every supported size, with movement switched off, and
    /// across a thousand gestures.
    /// <para>
    /// These are separated from the transaction fixture because they are the
    /// expensive kind: each one drives the canvas through several
    /// resolution/scale combinations or repeats a gesture a thousand times, and
    /// mixing them in with the "does a conversion work" tests would make every one
    /// of those slower to no purpose.
    /// </para>
    /// <para>
    /// The reason a scale sweep is needed at all: the shell expresses interface
    /// scale by <i>shrinking the canvas's reference resolution</i>, so at 140% a
    /// 1280x720 window has markedly fewer logical pixels than at 100%. Every
    /// fraction-anchored column shrinks with it, and a fixed-width label that
    /// clears a button at 100% runs straight through it at 140%.
    /// </para>
    /// </summary>
    // ReleaseRegression: this fixture runs headless. The individual tests that need real
    // pixels carry [Category("Graphics")] of their own and must be excluded by that name --
    // NUnit propagates this class category to them, so Graphics is the authoritative one.
    [Category("ReleaseRegression")]
    public class ResourceBenchUiScaleAndLifecyclePlayModeTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;
        private bool motionOffBefore;

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
            motionOffBefore = AnimationAccessibility.MotionOff;
        }

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            AnimationAccessibility.MotionOff = motionOffBefore;

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

        private static NebulaResourcesPage OpenResources()
        {
            NebulaParentMenuScreen menu = Menu();
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Resources),
                "the menu refused to open on Resources");
            var page = menu.PageOf<NebulaResourcesPage>();
            Assert.IsNotNull(page, "the command menu carries no Resources page");
            page.Refresh();
            return page;
        }

        private static LogisticsCargo Hold() => Ctx().Ship.GetComponent<ShipExtraction>().Cargo;
        private static ProcessingBenchRuntime Bench() =>
            Ctx().Ship.GetComponent<ShipProcessing>().Bench;

        private static string Raw => ExtractionCatalog.ResourceIds[0];

        private static int MaterialRecipeIndex()
        {
            for (int i = 0; i < ProcessingCatalog.Recipes.Length; i++)
                if (ProcessingCatalog.Recipes[i].InputItemId == Raw
                    && ProcessingCatalog.Recipes[i].OutputItemId == ProcessingCatalog.MaterialIds[0])
                    return i;
            Assert.Fail("the catalog no longer turns the first raw family into a material");
            return -1;
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static bool Contains(Rect outer, Rect inner, float slack = 0.5f) =>
            inner.xMin >= outer.xMin - slack && inner.xMax <= outer.xMax + slack
            && inner.yMin >= outer.yMin - slack && inner.yMax <= outer.yMax + slack;

        /// <summary>Loads the bench with a real deposit, through the real controls.</summary>
        private static void LoadBench(NebulaResourcesPage page, int amount)
        {
            Assert.IsTrue(page.DragToBench(Raw), "the cargo row could not be dragged");
            page.Modal.SetValue(amount);
            page.Modal.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(amount, page.BenchAmount, "the fixture could not load the bench");
        }

        private static readonly (int width, int height)[] Sizes =
        {
            (1280, 720), (1920, 1080), (3440, 1440),
        };

        private static readonly float[] Scales = { 1f, 1.4f };

        // =====================================================================
        // Scale and resolution
        // =====================================================================

        /// <summary>
        /// Every control on the page, and every control in the open quantity box,
        /// stays inside the surface that owns it and stays at or above the minimum
        /// hit target — at both supported interface scales and across the whole
        /// supported width.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBenchAndItsQuantityBoxSurviveEveryScaleAndResolution()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            Bench().Clear();
            Hold().Add(Raw, 40f);

            NebulaResourcesPage page = OpenResources();
            LoadBench(page, 9);
            page.RecipeCardButton(MaterialRecipeIndex()).onClick.Invoke();

            NebulaUiShell shell = Ctx().Presentation.UiShell;
            Canvas canvas = shell.CanvasFor(UiLayer.Menu);
            RenderMode savedMode = canvas.renderMode;
            Camera savedCamera = canvas.worldCamera;
            float savedPlane = canvas.planeDistance;
            float savedScale = NebulaUiShell.UiScale;

            var cameraObject = new GameObject("BenchScaleCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -100f);

            // Never drawn. Every assertion below is RectTransform geometry — does this
            // card still fit inside its frame at this resolution and scale — and not one
            // of them reads a pixel. The camera and its RenderTexture exist only to
            // simulate eight resolutions this host does not have. Left enabled, the
            // player loop renders it through RenderOffscreenCameras, which on the
            // -nographics null device segfaults the editor and takes the whole run's
            // results with it; that is what this test used to Assert.Ignore for, at the
            // cost of one real test on every headless run. Disabled, the canvas is still
            // sized and projected by it and nothing is ever drawn. Same fix as
            // NebulaMenuNavigationPlayModeTests.cs:741.
            camera.enabled = false;

            RenderTexture target = null;

            try
            {
                foreach ((int width, int height) in Sizes)
                foreach (float scale in Scales)
                {
                    if (target != null)
                    {
                        camera.targetTexture = null;
                        target.Release();
                        Object.DestroyImmediate(target);
                    }
                    target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                    camera.targetTexture = target;

                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 100f;
                    NebulaUiShell.UiScale = scale;
                    shell.ApplyScale();
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    Canvas.ForceUpdateCanvases();

                    page.Refresh();
                    Canvas.ForceUpdateCanvases();

                    string where = $"{width}x{height} @ {scale:0.0}x";

                    // The row's own bench control clears the row's text rather than
                    // running through it -- the failure a fixed-width label produces
                    // only at the narrow end of this sweep.
                    var addRect = (RectTransform)page.CargoAddButton(Raw).transform;
                    Rect addBox = WorldRect(addRect);
                    Rect rowBox = WorldRect((RectTransform)addRect.parent);
                    Assert.IsTrue(Contains(rowBox, addBox), where + ": the row's control escapes its row");
                    Assert.GreaterOrEqual(addRect.rect.height,
                        NebulaUiDesignTokens.MinimumHitTarget - 0.01f,
                        where + ": the row's control is below the minimum hit target");

                    foreach (string part in new[] { "Name", "Amounts" })
                    {
                        RectTransform label = Child(addRect.parent, part);
                        Assert.IsFalse(WorldRect(label).Overlaps(addBox),
                            $"{where}: the row's control overlaps its {part}");
                    }

                    // The conversion card's own control, same rule.
                    int material = MaterialRecipeIndex();
                    var convertRect = (RectTransform)page.RecipeConvertButton(material).transform;
                    Assert.IsTrue(Contains(WorldRect((RectTransform)convertRect.parent), WorldRect(convertRect)),
                        where + ": the convert control escapes its card");

                    // And the box: open it here, at this scale, and check that every
                    // one of its controls is inside the scrolling frame.
                    page.RecipeConvertButton(material).onClick.Invoke();
                    Assert.IsTrue(page.Modal.IsOpen, where + ": the quantity box would not open");
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    Canvas.ForceUpdateCanvases();

                    Rect frame = WorldRect(page.Modal.Viewport);
                    foreach (Selectable control in new Selectable[]
                             {
                                 page.Modal.Field, page.Modal.MinusButton, page.Modal.PlusButton,
                                 page.Modal.OneButton, page.Modal.MaxButton,
                                 page.Modal.ConfirmButton, page.Modal.CancelButton,
                             })
                    {
                        var rect = (RectTransform)control.transform;
                        Assert.IsTrue(control.gameObject.activeInHierarchy,
                            $"{where}: the box's {control.name} is not on screen");
                        Assert.GreaterOrEqual(rect.rect.height,
                            NebulaUiDesignTokens.MinimumHitTarget - 0.01f,
                            $"{where}: the box's {control.name} is below the minimum hit target");

                        // Horizontally it must be inside the frame outright; vertically
                        // the ScrollRect is allowed to have it off-screen, because that
                        // is exactly what scrolling is for -- but the content it lives
                        // in must be reachable by scrolling to it.
                        Rect box = WorldRect(rect);
                        Assert.GreaterOrEqual(box.xMin, frame.xMin - 0.5f,
                            $"{where}: the box's {control.name} is clipped on the left");
                        Assert.LessOrEqual(box.xMax, frame.xMax + 0.5f,
                            $"{where}: the box's {control.name} is clipped on the right");
                        Assert.IsTrue(Contains(WorldRect(page.Modal.Card), box),
                            $"{where}: the box's {control.name} escapes the card");
                    }

                    Rect card = WorldRect(page.Modal.Card);
                    Assert.LessOrEqual(card.height, frame.height + 0.5f,
                        where + ": the card is taller than its frame and does not scroll");

                    page.Modal.Cancel();
                    page.Refresh();
                }
            }
            finally
            {
                NebulaUiShell.UiScale = savedScale;
                canvas.renderMode = savedMode;
                canvas.worldCamera = savedCamera;
                canvas.planeDistance = savedPlane;
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                shell.ApplyScale();
            }
        }

        private static RectTransform Child(Transform parent, string name)
        {
            foreach (Transform child in parent)
                if (child.name == name) return (RectTransform)child;
            Assert.Fail($"{parent.name} has no child named '{name}'");
            return null;
        }

        // =====================================================================
        // Motion Off
        // =====================================================================

        /// <summary>
        /// With movement switched off the conversion still happens, still says what
        /// changed, and simply does not move while saying it.
        /// <para>
        /// The rule this protects is that no information is carried by motion alone.
        /// An emphasis a player cannot see because they turned animation off is an
        /// emphasis that was never doing the job.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator MotionOffKeepsTheResultAndDropsOnlyTheMovement()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            Bench().Clear();

            AnimationAccessibility.MotionOff = true;
            Assert.IsFalse(UiEmphasisPulse.Repeats, "precondition: nothing in the UI may repeat");

            LogisticsCargo hold = Hold();
            hold.Add(Raw, 20f);
            NebulaResourcesPage page = OpenResources();
            LoadBench(page, 9);

            int material = MaterialRecipeIndex();
            ProcessingRecipe recipe = ProcessingCatalog.Recipes[material];
            float outputBefore = hold.GetStored(recipe.OutputItemId);

            page.RecipeConvertButton(material).onClick.Invoke();
            page.Modal.SetValue(6);
            page.Modal.ConfirmButton.onClick.Invoke();

            Assert.AreEqual(BenchConvertReason.None, page.LastConvertResult,
                "Motion Off changed whether a conversion happens");
            Assert.AreEqual(outputBefore + 2f, hold.GetStored(recipe.OutputItemId), 0.001f,
                "Motion Off changed what a conversion produces");
            Assert.AreEqual(3, page.BenchAmount);

            // Everything the emphasis was carrying is still readable as words.
            StringAssert.Contains("-6", page.LastDeltaText);
            StringAssert.Contains("+2", page.LastDeltaText);
            Assert.IsNotEmpty(page.CargoDeltaText(recipe.OutputItemId),
                "the changed row lost its delta entirely instead of holding it steady");
            Assert.IsNotEmpty(page.FeedbackText, "Motion Off silenced the result");

            // And the shared pulse is genuinely held rather than merely slowed.
            Assert.AreEqual(1f, UiEmphasisPulse.Floor, 0.0001f,
                "the emphasis still swings with Motion Off on");
        }

        // =====================================================================
        // The ghost is pooled
        // =====================================================================

        /// <summary>
        /// A thousand gestures use one ghost, leave none behind, and leave the page
        /// with no drag believing it is live.
        /// <para>
        /// The alternative — instantiate on begin, destroy on end — leaks one object
        /// for every gesture that ends in a way the page did not anticipate, and a
        /// pointer leaving the window is one of those ways. Half of the runs below
        /// end outside the bench for exactly that reason.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AThousandGesturesUseOneGhostAndLeaveNoneBehind()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            Bench().Clear();
            Hold().Add(Raw, 40f);

            NebulaResourcesPage page = OpenResources();
            Transform menuRoot = Menu().transform.parent;

            NebulaBenchDragSource source = page.CargoDragSource(Raw);
            Assert.IsNotNull(source, "the raw row cannot be dragged");

            // One gesture before the baseline is taken, deliberately.
            //
            // The property under test is that gestures do not ACCUMULATE objects, and
            // the first one is not like the rest: opening the box for the first time
            // makes TextMeshPro build a sub-mesh child for the glyphs the primary face
            // does not carry (the box uses − and →, which live in the wired fallback
            // asset). That object is created once and reused forever. Measuring from
            // zero would charge it to the loop and report a leak that does not exist;
            // measuring after it means any later growth is real growth.
            var warmup = new PointerEventData(EventSystem.current) { pointerDrag = source.gameObject };
            source.OnBeginDrag(warmup);
            source.OnDrag(warmup);
            page.DropZone.OnPointerEnter(warmup);
            page.DropZone.OnDrop(warmup);
            source.OnEndDrag(warmup);
            page.Modal.Cancel();
            yield return null;

            int objectsBefore = CountUnder(menuRoot);
            int ghostsBefore = CountNamed(menuRoot, "Drag Ghost");
            Assert.AreEqual(1, ghostsBefore, "the page does not carry exactly one pooled ghost");

            for (int i = 0; i < 1000; i++)
            {
                var pointer = new PointerEventData(EventSystem.current)
                {
                    pointerDrag = source.gameObject,
                };
                source.OnBeginDrag(pointer);
                source.OnDrag(pointer);

                if (i % 2 == 0)
                {
                    // Over the bench and released: the box opens, and is dismissed.
                    page.DropZone.OnPointerEnter(pointer);
                    page.DropZone.OnDrop(pointer);
                    source.OnEndDrag(pointer);
                    Assert.IsTrue(page.Modal.IsOpen, $"drop {i} asked nothing");
                    page.Modal.Cancel();
                }
                else
                {
                    // Released over nothing at all. No drop handler fires, so the
                    // gesture must still end cleanly on its own.
                    source.OnEndDrag(pointer);
                    Assert.IsFalse(page.Modal.IsOpen, $"gesture {i} opened a box off the bench");
                }

                Assert.IsFalse(page.IsDragging, $"gesture {i} left the page believing a drag is live");
                Assert.IsFalse(page.DragGhost.Visible, $"gesture {i} left the ghost on screen");
            }

            Assert.AreEqual(ghostsBefore, CountNamed(menuRoot, "Drag Ghost"),
                "a thousand gestures created ghosts");
            Assert.AreEqual(objectsBefore, CountUnder(menuRoot),
                "a thousand gestures created objects");
            Assert.AreEqual(1, menuRoot.GetComponentsInChildren<NebulaQuantityModal>(true).Length,
                "a thousand gestures created quantity boxes");
            Assert.AreEqual(501, page.Modal.CancelledCount,
                "a drop did not open exactly one box (500 in the loop, plus the warm-up)");
            Assert.AreEqual(0, page.DepositsIssued, "a cancelled gesture moved stock");
            Assert.IsTrue(Bench().IsEmpty, "a thousand cancelled gestures loaded the bench");
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
    }
}
