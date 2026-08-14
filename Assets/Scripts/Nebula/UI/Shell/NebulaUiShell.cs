using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;

namespace Nebula.UI.Shell
{
    /// <summary>Where a piece of interface lives. One canvas per layer, four in total.</summary>
    public enum UiLayer
    {
        /// <summary>Chrome that rarely changes: frames, static labels. Batches once.</summary>
        HudStatic = 0,

        /// <summary>Readings that change every frame. Kept off the static canvas so its rebuild stays cheap.</summary>
        HudDynamic = 1,

        /// <summary>
        /// Parent menu, session screens, confirmations. Gates ship/world input and
        /// frees the cursor; the simulation behind it keeps running.
        /// </summary>
        Menu = 2,

        /// <summary>Tooltips and notifications. Never blocks a raycast.</summary>
        Overlay = 3,
    }

    /// <summary>
    /// The one session UI root.
    /// <para>
    /// Before this, four screens each carried their own copy of the same eighteen
    /// lines: save <c>InputGate</c>, free the cursor, acquire
    /// <see cref="ExclusiveModal"/>, and undo all of it on close. Four copies means
    /// one divergence is one permanently-gated game, so the block lives here once
    /// and the screens above it only declare that they are open.
    /// </para>
    /// <para>
    /// An open screen does <b>not</b> stop the world. The clock is left exactly
    /// where it was: the player asked to keep flying, mining and being shot at
    /// while a map or a menu is on screen, and a future online build could not
    /// honour a client-side freeze anyway. What an open screen takes is
    /// <i>input</i> — every contextual ship component begins with
    /// <c>if (InputGate.Suppressed) return;</c>, so WASD and the mouse belong to
    /// the screen and never fly the ship from inside a menu.
    /// <see cref="StrategicMap.StrategicMapScreen"/> has always worked this way;
    /// the shell now matches it.
    /// </para>
    /// <para>
    /// The gate is restored to what it <b>was</b>, never to a default. A screen
    /// opened while input was already suppressed for another reason must not
    /// release it on close.
    /// </para>
    /// </summary>
    public sealed class NebulaUiShell : MonoBehaviour
    {
        private const int SortingHudStatic = 20;
        private const int SortingHudDynamic = 22;
        private const int SortingMenu = 40;
        private const int SortingOverlay = 80;

        private readonly Canvas[] canvases = new Canvas[4];
        private readonly RectTransform[] roots = new RectTransform[4];

        /// <summary>Screens currently stacked, deepest first. The top one owns input.</summary>
        private readonly List<INebulaUiScreen> stack = new List<INebulaUiScreen>();

        private bool savedInputGateSuppressed;
        private CursorLockMode savedCursorLock;
        private bool savedCursorVisible;
        private bool inputHeld;

        /// <summary>
        /// Whether the values in <c>saved*</c> were taken off another shell that was
        /// still mid-teardown, rather than off a genuine pre-existing suppression.
        /// Recorded at capture because it cannot be recovered afterwards -- see
        /// <see cref="HoldInput"/>.
        /// </summary>
        private bool capturedAnotherShellsGate;

        /// <summary>Test-facing: how deep the back stack is.</summary>
        public int StackDepth => stack.Count;

        /// <summary>The screen that currently owns input, or <c>null</c> when the HUD does.</summary>
        public INebulaUiScreen Top => stack.Count == 0 ? null : stack[stack.Count - 1];

        /// <summary>True while any screen is open, i.e. while the interface owns input.</summary>
        public bool AnyScreenOpen => stack.Count > 0;

        /// <summary>Reference resolution every layer scales against.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>
        /// Player-set interface scale. Applied on top of the canvas scaler so the
        /// accessibility setting and the resolution fit stay independent.
        /// </summary>
        public static float UiScale
        {
            get => uiScale;
            set
            {
                uiScale = Mathf.Clamp(value, MinUiScale, MaxUiScale);
                // Applied immediately rather than waiting for the next LateUpdate.
                // A settings slider that only takes effect a frame later reads as
                // broken, and outside play mode LateUpdate never runs at all, so a
                // deferred-only apply is untestable and silently does nothing in the
                // editor.
                for (int i = Live.Count - 1; i >= 0; i--)
                {
                    if (Live[i] == null) { Live.RemoveAt(i); continue; }
                    Live[i].ApplyScale();
                }
            }
        }

        public const float MinUiScale = 0.8f;
        public const float MaxUiScale = 1.4f;
        private static float uiScale = 1f;

        /// <summary>Every shell currently alive. Normally one; a restart can briefly overlap two.</summary>
        private static readonly List<NebulaUiShell> Live = new List<NebulaUiShell>(2);

        /// <summary>True for the two canvases that carry the in-game HUD.</summary>
        private static bool IsHudLayer(UiLayer layer) =>
            layer == UiLayer.HudStatic || layer == UiLayer.HudDynamic;

        /// <summary>
        /// The interface scale a given layer should render at. The accessibility HUD
        /// scale rides on top of the global interface scale for the two HUD layers
        /// only — a player who enlarges the HUD to read it in flight is not asking
        /// for a larger settings screen, and scaling the menu with it would push the
        /// parent menu off its own safe area at 1.5.
        /// </summary>
        public static float ScaleFor(UiLayer layer) =>
            IsHudLayer(layer)
                ? uiScale * Combat.CombatAccessibilitySettings.ClampHudScale(
                    Combat.CombatAccessibilitySettings.HudScale)
                : uiScale;

        /// <summary>
        /// Pushes the current scale onto this shell's canvases.
        /// <para>
        /// This is also where <c>CombatAccessibilitySettings.HudScale</c> finally
        /// reaches something. The settings slider had been writing it since the
        /// screen was built and nothing anywhere read it back, so the control moved
        /// and the HUD did not — an accessibility setting that exists but does
        /// nothing is worse than an absent one, because it looks like it worked.
        /// </para>
        /// </summary>
        public void ApplyScale()
        {
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null) continue;
                float target = ScaleFor((UiLayer)i);

                // Through the CanvasScaler's reference resolution, not by writing
                // canvas.scaleFactor and hoping.
                //
                // In ScaleWithScreenSize mode the scaler owns scaleFactor: it hooks
                // Canvas.preWillRenderCanvases and recomputes the value on every
                // canvas update. A number written straight onto the Canvas therefore
                // survives exactly until the next rebuild, which is why an 80%
                // interface silently came back at 100% and why no capture ever
                // photographed a scale other than the one the scaler had computed
                // for itself. Halving the reference resolution is how you ask that
                // mode for a bigger interface, so the scaler recomputes to the value
                // wanted instead of away from it.
                // The two are also in different units, which is the trap. Under
                // ScaleWithScreenSize the interface scale is a *multiplier* on
                // whatever the screen already warrants, while scaleFactor is the
                // absolute number of canvas units per pixel. Writing the multiplier
                // into scaleFactor happens to look right at exactly the reference
                // resolution and is wrong at every other one — at 640x480 the
                // screen's own factor is 0.385, so 80% is 0.308 and not 0.8.
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    Vector2 reference = ReferenceResolution / Mathf.Max(0.01f, target);
                    if (scaler.referenceResolution != reference) scaler.referenceResolution = reference;
                }
                else if (!Mathf.Approximately(canvas.scaleFactor, target))
                {
                    // No scaler to express it through, so the canvas carries it.
                    canvas.scaleFactor = target;
                }
            }
        }

        public static NebulaUiShell Create()
        {
            var go = new GameObject("Nebula UI Shell");
            var shell = go.AddComponent<NebulaUiShell>();
            shell.Build();
            return shell;
        }

        /// <summary>
        /// The authored layer names, in <see cref="UiLayer"/> order. Shared by the
        /// prefab path and the code path so the two cannot drift apart into a shell
        /// whose layers are bound to the wrong canvases.
        /// </summary>
        private static readonly string[] LayerNames = { "HUD Static", "HUD Dynamic", "Menu", "Overlay" };

        private void Build()
        {
            Live.Add(this);
            EnsureEventSystem();

            if (!AdoptAuthoredRoot())
            {
                BuildLayer(UiLayer.HudStatic, LayerNames[0], SortingHudStatic, raycasts: true);
                BuildLayer(UiLayer.HudDynamic, LayerNames[1], SortingHudDynamic, raycasts: false);
                BuildLayer(UiLayer.Menu, LayerNames[2], SortingMenu, raycasts: true);
                BuildLayer(UiLayer.Overlay, LayerNames[3], SortingOverlay, raycasts: false);
            }

            // A new shell adopts the scale the player already chose.
            //
            // UiScale is static and its setter pushes the value out by walking
            // Live -- which reaches every shell that exists at the moment it is
            // set, and no shell built afterwards. Nothing else applied it except
            // LateUpdate, and as the comment on the setter says, outside play mode
            // LateUpdate never runs at all. So a shell constructed after the
            // setting was chosen came up at scaleFactor 1 and stayed there.
            //
            // Player-facing, not a capture artefact: set the interface to 80% and
            // then do anything that stands a fresh shell up -- return to the main
            // menu, start a campaign, restart one -- and the interface comes back
            // at 100%. In play mode LateUpdate hides it after one frame, which is
            // a visible pop; in the editor and in any capture it never corrects
            // at all.
            //
            // The authored-prefab path is inside the same call rather than behind
            // the early return it used to sit behind, because a shell that adopted
            // UiRoot.prefab needs the scale exactly as much as one that built its
            // own canvases -- and that path is the one production uses.
            ApplyScale();
        }

        /// <summary>
        /// Takes the four canvas layers from <c>UiRoot.prefab</c> instead of building
        /// them here. The prefab authors the same names, sorting orders, scaler and
        /// raycast policy this class used to hard-code, so the swap is structural
        /// only -- but it is what makes the interface's own root an editable asset
        /// rather than four numbers buried in a private method.
        /// <para>
        /// A prefab that loads but is missing a layer is treated as a miss, not
        /// patched up: a null canvas here is a screen with nowhere to be pushed, and
        /// half an authored root is worse than none. The partial clone is destroyed
        /// so the fallback does not build its layers alongside orphaned ones.
        /// </para>
        /// </summary>
        private bool AdoptAuthoredRoot()
        {
            GameObject clone = NebulaUiPrefabCatalog.Instantiate(NebulaUiPrefab.UiRoot, null, "UI Root");
            if (clone == null) return false;

            clone.transform.SetParent(transform, false);

            for (int i = 0; i < LayerNames.Length; i++)
            {
                Transform child = clone.transform.Find(LayerNames[i]);
                Canvas canvas = child == null ? null : child.GetComponent<Canvas>();
                if (canvas == null)
                {
                    NebulaUiPrefabCatalog.NoteFallback(NebulaUiPrefab.UiRoot);
                    for (int j = 0; j < canvases.Length; j++) { canvases[j] = null; roots[j] = null; }
                    DestroyNow(clone);
                    return false;
                }

                canvases[i] = canvas;
                roots[i] = (RectTransform)child;
            }

            return true;
        }

        /// <summary>
        /// <c>Destroy</c> is deferred in play mode and simply refused outside it, so
        /// a rejected prefab clone would survive the very call meant to remove it
        /// and leave a second set of canvases in the scene.
        /// </summary>
        private static void DestroyNow(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void BuildLayer(UiLayer layer, string name, int sorting, bool raycasts)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sorting;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            // Match width and height evenly so an ultrawide viewport widens the
            // layout instead of magnifying it -- the rails stay fixed and the
            // content column flexes, which is what the responsive contract promises.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // A tooltip that eats clicks is worse than no tooltip, and a dynamic
            // readout is never interactive, so only two layers raycast at all.
            if (raycasts) go.AddComponent<GraphicRaycaster>();

            canvases[(int)layer] = canvas;
            roots[(int)layer] = (RectTransform)go.transform;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>The parent every widget on a layer should attach to.</summary>
        public RectTransform Root(UiLayer layer) => roots[(int)layer];

        public Canvas CanvasFor(UiLayer layer) => canvases[(int)layer];

        /// <summary>Shows or hides a whole layer without destroying anything on it.</summary>
        public void SetLayerVisible(UiLayer layer, bool visible)
        {
            Canvas canvas = canvases[(int)layer];
            if (canvas != null) canvas.enabled = visible;
        }

        /// <summary>Safety net for anything that changes the scale without the setter.</summary>
        private void LateUpdate() => ApplyScale();

        // =====================================================================
        // SCREEN STACK
        // =====================================================================

        /// <summary>
        /// Pushes a screen. The first push is what raises the input gate and takes
        /// <see cref="ExclusiveModal"/>; deeper pushes ride the gate already held,
        /// so a confirmation over a page does not gate twice or fight for the modal.
        /// </summary>
        public bool Push(INebulaUiScreen screen)
        {
            if (screen == null || stack.Contains(screen)) return false;

            if (stack.Count == 0)
            {
                if (!ExclusiveModal.TryAcquire(this)) return false;
                HoldInput();
            }
            else
            {
                stack[stack.Count - 1].OnCovered();
            }

            stack.Add(screen);
            screen.OnOpened(this);
            return true;
        }

        /// <summary>
        /// Pops the top screen. Releasing the last one is what hands input and
        /// <see cref="ExclusiveModal"/> back.
        /// </summary>
        public bool Pop()
        {
            if (stack.Count == 0) return false;

            INebulaUiScreen top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            top.OnClosed();

            if (stack.Count == 0) ReleaseInput();
            else stack[stack.Count - 1].OnRevealed();
            return true;
        }

        /// <summary>Closes every screen, innermost first. Used by teardown and by "back to gameplay".</summary>
        public void PopAll()
        {
            while (stack.Count > 0) Pop();
        }

        /// <summary>
        /// One step of Back. Gives the top screen the chance to consume it first --
        /// that is how a confirmation closes itself instead of the page under it.
        /// Returns false when there was nothing left to go back to.
        /// </summary>
        public bool Back()
        {
            if (stack.Count == 0) return false;
            if (stack[stack.Count - 1].HandleBack()) return true;
            return Pop();
        }

        /// <summary>
        /// The HUD while a screen is open. Dimming it through the scrim left its
        /// panels sliced in half by the opaque rail -- "YILDIZLAR ARASI" reading as
        /// "RASI" past the rail's edge -- which looks like a rendering fault rather
        /// than depth. A screen owns the screen.
        /// </summary>
        private void SetHudVisible(bool visible)
        {
            Canvas staticHud = canvases[(int)UiLayer.HudStatic];
            Canvas dynamicHud = canvases[(int)UiLayer.HudDynamic];

            if (staticHud != null) staticHud.enabled = visible;
            if (dynamicHud != null) dynamicHud.enabled = visible;
        }

        /// <summary>
        /// Takes input for the interface: the gate goes up and the cursor comes
        /// back. The clock is deliberately untouched — the world keeps simulating
        /// behind an open screen, which is the whole point of the screen not being
        /// a pause.
        /// </summary>
        private void HoldInput()
        {
            if (inputHeld) return;
            inputHeld = true;

            // Decided here, because here is the only place the answer exists.
            //
            // What must be told apart on release is "the gate was already held for a
            // legitimate reason" from "it was captured from a shell that had not
            // finished tearing down". By value the two are identical -- identically
            // `true`, a bool with no spare state to encode a sentinel in, which is
            // why a release-time test of the value alone can only ever guess. What
            // actually separates them is WHO was holding.
            //
            // Note this cannot be narrowed to "only a screen raises the gate": the
            // strategic map raises it without taking a shell stack. So the question
            // asked is the one that can be answered -- did another *shell* have a
            // screen open at the moment of capture -- and anything else that was
            // holding is left alone as not ours to clear.
            capturedAnotherShellsGate = AnyShellHoldsAScreen(except: this);

            SetHudVisible(false);

            savedInputGateSuppressed = InputGate.Suppressed;
            savedCursorLock = Cursor.lockState;
            savedCursorVisible = Cursor.visible;

            // Ship/world/combat input only. Without this the player's WASD and mouse
            // in an open menu would fly the ship and trigger world interactions
            // behind it, because every contextual component is gated on this one
            // flag. The simulation itself is never stopped.
            InputGate.Suppressed = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ReleaseInput()
        {
            if (!inputHeld) return;
            inputHeld = false;

            // Restored to what it was, never to false. A screen opened while input
            // was already suppressed for another reason must not release it.
            SetHudVisible(true);

            InputGate.Suppressed = savedInputGateSuppressed;
            Cursor.lockState = savedCursorLock;
            Cursor.visible = savedCursorVisible;

            ExclusiveModal.Release(this);

            // The restore above is right whenever what was captured was real. It is
            // wrong in exactly one case, and only that case is undone here.
            //
            // Deferred destruction is what makes that case ordinary rather than
            // exotic: Destroy runs at the end of the frame, so a shell handing over
            // to its replacement is routinely still holding the gate at the moment
            // the replacement captures it. The player-visible form is leaving a
            // campaign for the main menu and pressing New Campaign -- the front
            // door's shell captured a raised gate from the outgoing campaign's menu,
            // stood down, restored it, and the new campaign opened with every
            // contextual ship component silently refusing to answer, because they
            // all open with `if (InputGate.Suppressed) return`.
            //
            // The first version of this floor fired on the value instead of on the
            // provenance -- any lingering raised gate was cleared once no shell held
            // a screen. That fixed the handover and broke its mirror image: a gate
            // raised by something that is not a shell (the strategic map raises it
            // without taking a shell stack) was silently dropped the next time any
            // screen closed, which is the same class of bug pointing the other way.
            // Provenance is recorded at capture; only what this shell took from
            // another shell is taken back.
            //
            // The clock used to be floored here too, for the same handover: a shell
            // that captured a zeroed `Time.timeScale` and re-applied it on release
            // left the new campaign frozen. That half is gone with the pause itself.
            // No shell writes the clock any more, so a zero found here can only
            // belong to somebody else -- a tutorial step, a test harness -- and
            // forcing it back to 1 would be the mirror-image bug all over again.
            if (!capturedAnotherShellsGate) return;
            capturedAnotherShellsGate = false;

            // And not even then, while some other shell still legitimately has a
            // screen open -- its gate is current, not stale.
            if (AnyShellHoldsAScreen(except: this)) return;

            if (InputGate.Suppressed) InputGate.Suppressed = false;
        }

        /// <summary>True while any live shell other than <paramref name="except"/> has a screen on its stack.</summary>
        private static bool AnyShellHoldsAScreen(NebulaUiShell except)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (Live[i] == null) { Live.RemoveAt(i); continue; }
                if (ReferenceEquals(Live[i], except)) continue;
                if (Live[i].AnyScreenOpen) return true;
            }
            return false;
        }

        /// <summary>
        /// Closes everything and hands back input, the cursor and the modal gate.
        /// Idempotent, so calling it twice — or calling it and then being
        /// destroyed — is harmless.
        /// <para>
        /// Public and explicit rather than living only in <see cref="OnDestroy"/>,
        /// because Unity does not run MonoBehaviour lifecycle callbacks outside
        /// play mode: an EditMode test that destroys a shell never gets OnDestroy,
        /// and neither does any tooling that tears a shell down in the editor. A
        /// shell that goes away while still holding the gate leaves
        /// <see cref="ExclusiveModal.Owner"/> pointing at a dead object, which
        /// blocks every future modal for the rest of the process, with every
        /// contextual ship component left refusing input.
        /// </para>
        /// </summary>
        public void Shutdown()
        {
            Live.Remove(this);
            PopAll();
            ReleaseInput();
            if (ReferenceEquals(ExclusiveModal.Owner, this)) ExclusiveModal.Release(this);

            // No floor of its own here any more. It used to repeat the one in
            // ReleaseInput so that a shell destroyed without ever having gated
            // input still forced the world to answer again -- but a shell that never
            // gated never captured anything, so anything it would have cleared
            // belonged to somebody else by definition. PopAll above ends in
            // ReleaseInput on the last screen, which is where a shell that did
            // capture gives it back.
        }

        /// <summary>Play-mode safety net. The real teardown path is <see cref="Shutdown"/>.</summary>
        private void OnDestroy() => Shutdown();
    }

    /// <summary>
    /// A screen the shell can stack. Screens do not gate input, do not touch the
    /// cursor, and do not take <see cref="ExclusiveModal"/> -- the shell does all
    /// three, exactly once, for the whole stack. None of them stops the clock.
    /// </summary>
    public interface INebulaUiScreen
    {
        /// <summary>Stable id. Used by tests and the back stack, never shown to the player.</summary>
        string ScreenId { get; }

        void OnOpened(NebulaUiShell shell);

        void OnClosed();

        /// <summary>Another screen was pushed on top of this one.</summary>
        void OnCovered();

        /// <summary>The screen above this one closed.</summary>
        void OnRevealed();

        /// <summary>
        /// Consume Back if there is internal state to unwind first -- an open
        /// confirmation, an expanded row. Return false to let the shell pop this
        /// screen instead.
        /// </summary>
        bool HandleBack();
    }
}
