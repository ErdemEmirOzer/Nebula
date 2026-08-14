using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-02/03: the shell's input-gate, cursor and back-stack contract. This is
    /// the block four separate screens used to each carry a copy of, so it is the
    /// one place a divergence could permanently gate the game.
    /// <para>
    /// An open screen takes the controls, never the clock. Every test below that
    /// touches <c>Time.timeScale</c> is therefore asserting that the shell left it
    /// exactly where it found it — the simulation runs on behind the menu.
    /// </para>
    /// </summary>
    public class NebulaUiShellTests
    {
        private NebulaUiShell shell;
        private float enteredTimeScale;
        private bool enteredInputGate;

        [SetUp]
        public void CreateShell()
        {
            enteredTimeScale = Time.timeScale;
            enteredInputGate = InputGate.Suppressed;
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void DestroyShell()
        {
            if (shell != null) Object.DestroyImmediate(shell.gameObject);
            ExclusiveModal.ResetForSession();
            // Never restore a zero: in edit mode Time.timeScale persists into
            // ProjectSettings/TimeManager.asset, and a committed 0 freezes the
            // project for whoever opens it next.
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
            InputGate.Suppressed = enteredInputGate;
            NebulaUiShell.UiScale = 1f;
        }

        /// <summary>Minimal screen that records what the shell told it.</summary>
        private sealed class FakeScreen : INebulaUiScreen
        {
            public FakeScreen(string id, bool consumesBack = false)
            {
                ScreenId = id;
                ConsumesBack = consumesBack;
            }

            public string ScreenId { get; }
            public bool ConsumesBack { get; set; }

            public int Opened, Closed, Covered, Revealed, BackHandled;

            public void OnOpened(NebulaUiShell shell) => Opened++;
            public void OnClosed() => Closed++;
            public void OnCovered() => Covered++;
            public void OnRevealed() => Revealed++;

            public bool HandleBack()
            {
                BackHandled++;
                return ConsumesBack;
            }
        }

        // ---- layers ----------------------------------------------------------

        [Test]
        public void ShellBuilds_ExactlyFourCanvases()
        {
            var canvases = shell.GetComponentsInChildren<Canvas>(true);
            Assert.AreEqual(4, canvases.Length,
                "One canvas per layer, four total. A canvas per widget is what made the old HUD rebuild twelve times.");
        }

        [Test]
        public void OnlyInteractiveLayers_Raycast()
        {
            Assert.IsNotNull(shell.CanvasFor(UiLayer.HudStatic).GetComponent<UnityEngine.UI.GraphicRaycaster>());
            Assert.IsNotNull(shell.CanvasFor(UiLayer.Menu).GetComponent<UnityEngine.UI.GraphicRaycaster>());
            Assert.IsNull(shell.CanvasFor(UiLayer.HudDynamic).GetComponent<UnityEngine.UI.GraphicRaycaster>(),
                "A dynamic readout is never clickable.");
            Assert.IsNull(shell.CanvasFor(UiLayer.Overlay).GetComponent<UnityEngine.UI.GraphicRaycaster>(),
                "A tooltip that eats clicks is worse than no tooltip.");
        }

        [Test]
        public void MenuLayer_SortsAboveEveryHudLayer()
        {
            Assert.Greater(shell.CanvasFor(UiLayer.Menu).sortingOrder,
                shell.CanvasFor(UiLayer.HudDynamic).sortingOrder);
            Assert.Greater(shell.CanvasFor(UiLayer.Overlay).sortingOrder,
                shell.CanvasFor(UiLayer.Menu).sortingOrder,
                "Tooltips must be able to sit over a modal.");
        }

        /// <summary>
        /// Read where the scale is expressed, not where it used to be written.
        /// <para>
        /// These two asserted <c>canvas.scaleFactor == UiScale</c>. In
        /// <c>ScaleWithScreenSize</c> mode the <see cref="CanvasScaler"/> owns
        /// <c>scaleFactor</c> and recomputes it on every canvas update from the
        /// reference resolution and the real screen size, so a number written
        /// straight onto the Canvas survives until the next rebuild and no further
        /// — which is why an 80% interface silently came back at 100%. The
        /// interface scale is a <b>multiplier</b>, and the reference resolution is
        /// where a multiplier lives.
        /// </para>
        /// <para>
        /// The two are also in different units, which is the trap that made the old
        /// assertion look right: they agree only when the screen happens to BE the
        /// reference resolution. At the size a headless fixture runs, the screen's
        /// own factor is 0.385, so the old assertion measured the window rather than
        /// the setting. The same correction was already made to the end-to-end
        /// fixture; these are its siblings.
        /// </para>
        /// </summary>
        [Test]
        public void UiScale_ReachesTheCanvasesImmediately()
        {
            // Caught by a screenshot run: the three UI-scale captures came back
            // byte-identical because the scale was only applied in LateUpdate, which
            // never runs outside play mode. A settings slider that takes effect a
            // frame later reads as broken; one that never takes effect is worse.
            // That is still exactly what this measures -- the read happens with no
            // frame in between.
            NebulaUiShell.UiScale = 1.4f;
            AssertScaleReached(UiLayer.Menu, 1.4f, "Setting UiScale did not reach the canvas.");

            NebulaUiShell.UiScale = 0.8f;
            AssertScaleReached(UiLayer.HudStatic, 0.8f, "Setting UiScale did not reach the canvas.");
        }

        [Test]
        public void UiScale_AppliesToEveryLayer()
        {
            NebulaUiShell.UiScale = 1.2f;
            foreach (UiLayer layer in System.Enum.GetValues(typeof(UiLayer)))
                AssertScaleReached(layer, 1.2f, $"{layer} did not follow the interface scale.");
        }

        /// <summary>
        /// The scale a layer is actually rendering at, taken from the scaler that
        /// owns it. Checks the exact reference resolution rather than merely that it
        /// moved, so a scale applied in the wrong direction still fails.
        /// <para>
        /// <paramref name="uiScale"/> is the global setting; the expected value goes
        /// through <see cref="NebulaUiShell.ScaleFor"/> because the two HUD layers
        /// deliberately carry the accessibility HUD scale on top of it, and hard-coding
        /// the global here would assert that they do not.
        /// </para>
        /// </summary>
        private void AssertScaleReached(UiLayer layer, float uiScale, string because)
        {
            Assert.AreEqual(uiScale, NebulaUiShell.UiScale, 0.001f,
                "the setting did not even reach the shell.");

            var scaler = shell.CanvasFor(layer).GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, $"{layer} has no CanvasScaler to express a scale through.");

            Vector2 wanted = NebulaUiShell.ReferenceResolution / NebulaUiShell.ScaleFor(layer);
            Assert.AreEqual(wanted.x, scaler.referenceResolution.x, 0.5f, because);
            Assert.AreEqual(wanted.y, scaler.referenceResolution.y, 0.5f, because);
        }

        [Test]
        public void UiScale_IsClampedToTheSupportedRange()
        {
            NebulaUiShell.UiScale = 5f;
            Assert.AreEqual(NebulaUiShell.MaxUiScale, NebulaUiShell.UiScale);
            NebulaUiShell.UiScale = 0.1f;
            Assert.AreEqual(NebulaUiShell.MinUiScale, NebulaUiShell.UiScale);
        }

        // ---- input-gate contract ---------------------------------------------

        [Test]
        public void FirstPush_GatesInputAndTakesTheModalGate()
        {
            Time.timeScale = 1f;
            InputGate.Suppressed = false;

            shell.Push(new FakeScreen("a"));

            Assert.IsTrue(InputGate.Suppressed,
                "A screen that does not gate input lets WASD fly the ship from inside the menu.");
            Assert.IsTrue(ExclusiveModal.IsHeldByOther(new object()),
                "The shell must own ExclusiveModal while a screen is open.");
        }

        /// <summary>
        /// The behaviour the player asked for: opening the menu, a session screen
        /// or the map must not stop the world. Nothing behind the screen freezes.
        /// </summary>
        [Test]
        public void AnOpenScreen_LeavesTheClockRunning()
        {
            Time.timeScale = 1f;

            shell.Push(new FakeScreen("page"));
            Assert.AreEqual(1f, Time.timeScale,
                "the shell froze the simulation behind an open screen");

            shell.Push(new FakeScreen("confirm"));
            Assert.AreEqual(1f, Time.timeScale,
                "a confirmation over a page froze the simulation");

            shell.PopAll();
            Assert.AreEqual(1f, Time.timeScale, "and closing everything moved it again");
        }

        /// <summary>
        /// Not merely "does not zero it" -- does not write it at all. A shell that
        /// captured the clock on push and re-applied it on pop would clobber any
        /// legitimate change made while the screen was open (a tutorial step's own
        /// slow-motion, for instance).
        /// </summary>
        [Test]
        public void TheShell_NeverWritesTheClock_EvenWhenItIsNotOne()
        {
            Time.timeScale = 1f;
            shell.Push(new FakeScreen("a"));

            // Somebody else moves the clock while the screen is open.
            Time.timeScale = 0.25f;
            shell.Pop();

            Assert.AreEqual(0.25f, Time.timeScale,
                "the shell restored a clock it had captured on push instead of leaving it alone");
        }

        [Test]
        public void LastPop_RestoresTheGateThatWasThere_NotADefault()
        {
            // A screen opened while input was already suppressed for another reason
            // must not release it on close.
            InputGate.Suppressed = true;

            shell.Push(new FakeScreen("a"));
            shell.Pop();

            Assert.IsTrue(InputGate.Suppressed,
                "InputGate was cleared even though it was already suppressed before the screen opened.");
        }

        [Test]
        public void NestedPushes_GateOnlyOnce_AndReleaseOnlyOnce()
        {
            InputGate.Suppressed = false;
            var page = new FakeScreen("page");
            var confirm = new FakeScreen("confirm");

            shell.Push(page);
            shell.Push(confirm);
            Assert.AreEqual(2, shell.StackDepth);

            shell.Pop();
            Assert.IsTrue(InputGate.Suppressed,
                "Popping a confirmation must not hand the controls back to the page underneath it.");
            Assert.IsTrue(ExclusiveModal.IsHeldByOther(new object()),
                "Popping a confirmation must not hand the modal gate back either.");

            shell.Pop();
            Assert.IsFalse(InputGate.Suppressed);
            Assert.IsFalse(ExclusiveModal.IsHeldByOther(new object()));
        }

        [Test]
        public void CoveringAndRevealing_IsReportedToTheScreensBelow()
        {
            var page = new FakeScreen("page");
            var confirm = new FakeScreen("confirm");

            shell.Push(page);
            shell.Push(confirm);
            Assert.AreEqual(1, page.Covered);

            shell.Pop();
            Assert.AreEqual(1, page.Revealed,
                "A revealed page must be told, or it shows data from before the confirmation ran.");
        }

        [Test]
        public void PushingTheSameScreenTwice_IsRefused()
        {
            var page = new FakeScreen("page");
            Assert.IsTrue(shell.Push(page));
            Assert.IsFalse(shell.Push(page), "A screen must never appear twice in the back stack.");
            Assert.AreEqual(1, shell.StackDepth);
        }

        [Test]
        public void PushIsRefused_WhenAnotherOwnerHoldsTheModalGate()
        {
            var other = new object();
            ExclusiveModal.TryAcquire(other);

            Assert.IsFalse(shell.Push(new FakeScreen("a")),
                "A second modal owner must never be able to open a screen.");
            Assert.AreEqual(0, shell.StackDepth);
            ExclusiveModal.Release(other);
        }

        // ---- back ------------------------------------------------------------

        [Test]
        public void Back_LetsTheTopScreenConsumeItFirst()
        {
            var page = new FakeScreen("page");
            var confirm = new FakeScreen("confirm", consumesBack: true);
            shell.Push(page);
            shell.Push(confirm);

            Assert.IsTrue(shell.Back());
            Assert.AreEqual(2, shell.StackDepth,
                "An open confirmation must swallow Back instead of closing the page under it.");
            Assert.AreEqual(1, confirm.BackHandled);
        }

        [Test]
        public void Back_PopsWhenTheTopScreenDoesNotConsumeIt()
        {
            shell.Push(new FakeScreen("page"));
            shell.Push(new FakeScreen("sub"));

            Assert.IsTrue(shell.Back());
            Assert.AreEqual(1, shell.StackDepth);
        }

        [Test]
        public void Back_OnAnEmptyStack_ReportsNothingToDo()
        {
            Assert.IsFalse(shell.Back());
        }

        [Test]
        public void Shutdown_IsIdempotent()
        {
            InputGate.Suppressed = false;
            shell.Push(new FakeScreen("a"));

            shell.Shutdown();
            Assert.DoesNotThrow(() => shell.Shutdown());
            Assert.IsFalse(InputGate.Suppressed,
                "A second shutdown must not re-apply the gate it already handed back.");
            Assert.IsNull(ExclusiveModal.Owner);
        }

        [Test]
        public void PopAll_ClosesEveryScreen_AndHandsInputBackOnce()
        {
            InputGate.Suppressed = false;
            var a = new FakeScreen("a");
            var b = new FakeScreen("b");
            shell.Push(a);
            shell.Push(b);

            shell.PopAll();

            Assert.AreEqual(0, shell.StackDepth);
            Assert.AreEqual(1, a.Closed);
            Assert.AreEqual(1, b.Closed);
            Assert.IsFalse(InputGate.Suppressed);
            Assert.IsNull(ExclusiveModal.Owner);
        }

        // ---- teardown --------------------------------------------------------

        [Test]
        public void SessionTeardown_WithScreensOpen_LeavesNothingGated()
        {
            // The failure this guards is severe: a shell torn down mid-modal used to
            // leave ExclusiveModal pointing at a dead object, blocking every future
            // modal for the rest of the process, with every contextual ship
            // component left refusing input behind a screen that no longer exists.
            //
            // Shutdown() is called rather than relying on OnDestroy, because Unity
            // does not run lifecycle callbacks outside play mode -- an EditMode test
            // that only destroys the object would assert nothing at all.
            Time.timeScale = 1f;
            InputGate.Suppressed = false;
            shell.Push(new FakeScreen("a"));
            shell.Push(new FakeScreen("b"));

            shell.Shutdown();
            Object.DestroyImmediate(shell.gameObject);
            shell = null;

            Assert.AreEqual(1f, Time.timeScale, "Session teardown moved the clock.");
            Assert.IsFalse(InputGate.Suppressed, "Session teardown left input suppressed.");
            Assert.IsNull(ExclusiveModal.Owner, "Session teardown left the modal gate held by a dead object.");
        }

        [Test]
        public void AFreshShell_CanOpenAScreen_AfterAPreviousOneWasTornDown()
        {
            shell.Push(new FakeScreen("a"));
            shell.Shutdown();
            Object.DestroyImmediate(shell.gameObject);

            shell = NebulaUiShell.Create();
            Assert.IsTrue(shell.Push(new FakeScreen("a")),
                "A restarted session must be able to open screens again.");
        }

        // ---- the handover, which is where the gate used to get stuck -----------

        /// <summary>
        /// A shell that captured its gate off another shell mid-teardown must not
        /// re-apply it on release.
        /// <para>
        /// Destroy is deferred, so the outgoing shell is routinely still on
        /// <c>Live</c> with a screen on its stack — still holding the gate — at the
        /// moment its replacement captures it. Leaving a campaign and pressing New
        /// Campaign is the player-visible form: the front door's shell captures a
        /// raised gate from the outgoing campaign's menu, the outgoing shell stands
        /// down and lowers it, and then the front door hands the campaign a world
        /// where every contextual ship component silently refuses to answer,
        /// because they all open with <c>if (InputGate.Suppressed) return;</c>.
        /// </para>
        /// <para>
        /// The clock half of this used to be here too and is deliberately gone: no
        /// shell writes <c>Time.timeScale</c> any more, so there is no stale clock
        /// left to take back and forcing one would clobber whoever does own it.
        /// </para>
        /// </summary>
        [Test]
        public void AGateCapturedFromAShellMidTeardown_IsNotReappliedOnRelease()
        {
            InputGate.Suppressed = false;
            shell.Push(new FakeScreen("outgoing"));
            Assert.IsTrue(InputGate.Suppressed, "the outgoing shell never took the gate");

            // What session dispose does before the replacement is built.
            ExclusiveModal.ResetForSession();

            NebulaUiShell replacement = NebulaUiShell.Create();
            try
            {
                Assert.IsTrue(replacement.Push(new FakeScreen("front door")),
                    "the replacement could not open its own screen");

                // The outgoing shell finally goes away and lowers the gate it owns.
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
                shell = null;
                Assert.IsFalse(InputGate.Suppressed,
                    "the outgoing shell did not hand its own gate back");

                replacement.Pop();

                Assert.IsFalse(InputGate.Suppressed,
                    "the replacement re-applied a gate it had inherited from a dead shell, "
                    + "so the world came back with every contextual component refusing input");
            }
            finally
            {
                replacement.Shutdown();
                Object.DestroyImmediate(replacement.gameObject);
            }
        }

        /// <summary>
        /// The mirror image, which is why the floor above is keyed on provenance
        /// rather than on the value: <c>StrategicMapScreen</c> raises the gate
        /// without taking a shell stack, and a shell closing a screen must not
        /// drop somebody else's hold.
        /// </summary>
        [Test]
        public void AGateRaisedBySomethingThatIsNotAShell_SurvivesAScreenClosing()
        {
            InputGate.Suppressed = true; // as the strategic map leaves it

            shell.Push(new FakeScreen("a"));
            shell.Pop();

            Assert.IsTrue(InputGate.Suppressed,
                "closing a screen dropped a gate the shell never raised");
        }

        /// <summary>
        /// K-8: an empty bar is empty because it has no width, not because it is a
        /// different colour.
        /// <para>
        /// The original defect was subtler than "wrong colour": the track was left
        /// at the prefab's white, which composited brighter than the panel behind
        /// it, so a 0% bar and a 100% bar were the same full-width bright band and
        /// the only channel carrying the value — length — was cancelled. That was
        /// fixed by tinting the track, and a tint is exactly the kind of fix that a
        /// later palette edit can silently undo.
        /// </para>
        /// <para>
        /// So this asserts the geometry instead: at 0% the fill is measurably zero
        /// pixels wide, at 100% it is the full track, and half is half. A colour
        /// regression cannot make this test pass, and a layout regression cannot
        /// hide behind a colour that happens to look right.
        /// </para>
        /// </summary>
        [Test]
        public void AnEmptyBar_IsEmptyByGeometry_NotByColour()
        {
            NebulaUiFactory.BarHandle bar = NebulaUiFactory.Bar(
                shell.Root(UiLayer.HudStatic), "Bar", Color.cyan);

            RectTransform track = bar.Track.rectTransform;
            track.sizeDelta = new Vector2(200f, 8f);
            RectTransform fill = bar.Fill.rectTransform;

            foreach ((float fraction, float expected) in
                     new[] { (0f, 0f), (0.5f, 100f), (1f, 200f) })
            {
                bar.SetFraction(fraction);
                Canvas.ForceUpdateCanvases();

                Assert.AreEqual(expected, fill.rect.width, 0.5f,
                    $"a bar at {fraction:P0} drew {fill.rect.width}px of fill "
                    + $"against a 200px track -- the length channel is what carries "
                    + "the value, and it must not be standing in for a colour");
            }

            // And the empty case specifically: not merely small, actually nothing.
            bar.SetFraction(0f);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(0f, fill.rect.width, 0.001f,
                "0% must be geometrically empty");
            Assert.AreNotEqual(bar.Track.color, bar.Fill.color,
                "and the track must still be a groove rather than a band the same "
                + "colour as its own fill");
        }
    }
}
