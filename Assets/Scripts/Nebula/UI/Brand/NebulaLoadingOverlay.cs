using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.UI.Brand
{
    /// <summary>
    /// The loading surface: the Depth-FX relay loop over a dimmed scrim, shown while
    /// a real operation is in flight and torn down the moment it ends.
    /// <para>
    /// It lives on <see cref="UiLayer.Overlay"/>, which has no raycaster, and it
    /// never touches the pause, the input gate or <c>ExclusiveModal</c>. That is
    /// deliberate: a loading picture is not a screen, and a surface that claimed
    /// modal ownership would have to hand it back correctly on every failure path —
    /// including the ones where the operation threw.
    /// </para>
    /// <para>
    /// At most one exists. <see cref="Show"/> returns the live one rather than
    /// building a second, so a restart that runs two nested operations produces one
    /// overlay and one teardown.
    /// </para>
    /// </summary>
    public sealed class NebulaLoadingOverlay : MonoBehaviour
    {
        /// <summary>The live overlay, or <c>null</c>. Test-facing, and the single-instance guard.</summary>
        public static NebulaLoadingOverlay Active { get; private set; }

        /// <summary>
        /// How many overlays have been built this session.
        /// <para>
        /// A plain counter, incremented where the surface is actually constructed, so
        /// nothing branches on it. It exists because "the loading picture was shown
        /// and then went away" is otherwise unobservable from outside a synchronous
        /// operation: by the time <see cref="Show"/> has returned to the caller's
        /// caller the work is done and <see cref="Active"/> is null again, and a
        /// surface that never appeared at all looks identical. The Player smoke and
        /// the lifecycle fixture both read it; production never does.
        /// </para>
        /// </summary>
        public static int ShownCount { get; private set; }

        /// <summary>Zeroes <see cref="ShownCount"/>. For a fixture measuring one operation.</summary>
        public static void ResetShownCount() => ShownCount = 0;

        /// <summary>Default caption. Turkish, like the rest of the front door.</summary>
        public const string DefaultCaption = "YÜKLENİYOR";

        private Image frame;
        private NebulaUiFactory.LabelHandle caption;
        private IReadOnlyList<Sprite> frames;
        private float elapsed;
        private int shownIndex = -1;

        /// <summary>Test-facing: whether this overlay is playing the loop or holding a still.</summary>
        public bool IsAnimated { get; private set; }

        /// <summary>Test-facing: which frame is on screen, or -1 for the static plate.</summary>
        public int FrameIndex => IsAnimated ? shownIndex : -1;

        /// <summary>Test-facing: how many frames playback resolved to.</summary>
        public int FrameCount => frames == null ? 0 : frames.Count;

        /// <summary>Test-facing: the sprite currently drawn.</summary>
        public Sprite CurrentSprite => frame == null ? null : frame.sprite;

        /// <summary>
        /// Shows the overlay, or returns the one already up.
        /// <para>
        /// Returns <c>null</c> when there is no shell to draw on. A caller that
        /// cannot show a loading picture still has to run its operation, so this
        /// never throws and never blocks the work.
        /// </para>
        /// </summary>
        public static NebulaLoadingOverlay Show(NebulaUiShell shell, string text = DefaultCaption)
        {
            if (Active != null)
            {
                Active.SetCaption(text);
                return Active;
            }

            if (shell == null) return null;
            RectTransform host = shell.Root(UiLayer.Overlay);
            if (host == null) return null;

            // Constructed with a RectTransform. A bare `new GameObject` gets a plain
            // Transform, and every canvas child has to be a RectTransform -- adding
            // one afterwards is not possible either, so this is the only moment it
            // can be decided.
            var go = new GameObject("Loading Overlay", typeof(RectTransform));
            var overlay = go.AddComponent<NebulaLoadingOverlay>();
            overlay.Build(host, text);
            Active = overlay;
            ShownCount++;
            return overlay;
        }

        /// <summary>Tears the overlay down. Idempotent, and safe from a finally block.</summary>
        public static void HideActive()
        {
            if (Active == null) return;
            NebulaLoadingOverlay doomed = Active;
            Active = null;
            doomed.Dispose();
        }

        private void Build(RectTransform host, string text)
        {
            var root = (RectTransform)transform;
            root.SetParent(host, false);
            NebulaUiFactory.Fill(root);

            // A scrim rather than an opaque fill: the menu behind stays legible, so
            // an operation that fails and closes this does not look like a crash.
            Image scrim = NebulaUiFactory.Rect(root, "Scrim", new Color(0f, 0f, 0f, 0.72f));
            NebulaUiFactory.Fill(scrim.rectTransform);

            var relay = new GameObject("Relay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            relay.transform.SetParent(root, false);
            frame = relay.GetComponent<Image>();
            frame.raycastTarget = false;
            frame.preserveAspect = true;
            NebulaUiFactory.Place((RectTransform)relay.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(192f, 192f));

            caption = NebulaUiFactory.Label(root, "Caption", text, NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleCenter, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(caption.Text.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -96f), new Vector2(520f, 22f));

            Resolve();
        }

        /// <summary>
        /// Picks the variant, at show time and again whenever the settings that feed
        /// it change while the surface is up.
        /// <para>
        /// Motion Off gets the still plate and nothing else. Reduced Motion now gets
        /// the <b>reduced sheet</b> — that is the point of the second delivery. The
        /// first pass had to fall back to the still plate here, because the reduced
        /// variant shipped only as an APNG and a WebP and Unity imports neither as a
        /// sprite sheet; falling back was honest then and would be a regression now.
        /// </para>
        /// <para>
        /// The decision belongs to the catalog; this obeys it and reports what it
        /// actually managed to draw, which is not always the same thing.
        /// </para>
        /// </summary>
        private void Resolve()
        {
            NebulaBrandAssetCatalog.LoadingPresentation presentation =
                NebulaBrandAssetCatalog.CurrentPresentation();
            NebulaBrandAssetCatalog.LoadingFidelity fidelity = NebulaBrandAssetCatalog.CurrentFidelity();

            resolvedPresentation = presentation;
            resolvedFidelity = fidelity;

            if (presentation == NebulaBrandAssetCatalog.LoadingPresentation.Static)
            {
                NebulaBrandAssetCatalog.NoteReducedFellBackToStatic(false);
                ShowStill();
                return;
            }

            IReadOnlyList<Sprite> sheet = NebulaBrandAssetCatalog.LoadingFrames(presentation, fidelity);
            if (sheet == null || sheet.Count == 0)
            {
                // The sheet did not resolve. Fall to the still rather than drawing
                // nothing -- an empty rectangle over a dimmed menu reads as a hang --
                // and record it, so a Reduced Motion session that silently lost its
                // animation is visible to a test instead of looking intentional.
                NebulaBrandAssetCatalog.NoteReducedFellBackToStatic(
                    presentation == NebulaBrandAssetCatalog.LoadingPresentation.ReducedAnimated);
                ShowStill();
                return;
            }

            NebulaBrandAssetCatalog.NoteReducedFellBackToStatic(false);
            frames = sheet;
            IsAnimated = true;
            elapsed = 0f;
            shownIndex = -1;
            ShowFrame(0);
        }

        private void ShowStill()
        {
            IsAnimated = false;
            frames = null;
            shownIndex = -1;
            frame.sprite = NebulaBrandAssetCatalog.LoadingStatic;
            frame.enabled = frame.sprite != null;
        }

        /// <summary>Test-facing: which variant playback resolved to.</summary>
        public NebulaBrandAssetCatalog.LoadingPresentation ResolvedPresentation => resolvedPresentation;

        /// <summary>Test-facing: whether the reduced sheet is the one playing.</summary>
        public bool IsReduced => IsAnimated
            && resolvedPresentation == NebulaBrandAssetCatalog.LoadingPresentation.ReducedAnimated;

        private NebulaBrandAssetCatalog.LoadingPresentation resolvedPresentation;
        private NebulaBrandAssetCatalog.LoadingFidelity resolvedFidelity;

        /// <summary>
        /// Re-resolves if the motion setting or the quality level changed under the
        /// surface, so a player who turns Reduced Motion on mid-load sees the change
        /// rather than the loop they asked to stop.
        /// <para>
        /// The frame clock is restarted on a real swap, not carried across: index 61
        /// of the normal sheet is not index 61 of the reduced one, and continuing
        /// would jump the picture.
        /// </para>
        /// </summary>
        private void ResolveIfSettingsChanged()
        {
            NebulaBrandAssetCatalog.LoadingPresentation presentation =
                NebulaBrandAssetCatalog.CurrentPresentation();
            NebulaBrandAssetCatalog.LoadingFidelity fidelity = NebulaBrandAssetCatalog.CurrentFidelity();
            if (presentation == resolvedPresentation && fidelity == resolvedFidelity) return;

            Resolve();
        }

        public void SetCaption(string text)
        {
            if (caption.Text != null) caption.Value = text;
        }

        /// <summary>
        /// Advances the loop.
        /// <para>
        /// <c>unscaledDeltaTime</c>, because loading happens while the interface has
        /// the game paused and a scaled clock would freeze the picture at exactly the
        /// moment it has a job to do.
        /// </para>
        /// <para>
        /// Nothing is allocated here and nothing is created: the frames were resolved
        /// once at show time, and a step is an index into that array plus one sprite
        /// assignment. The elapsed clock is wrapped rather than left to grow, so a
        /// long load does not lose frame precision to a large float.
        /// </para>
        /// </summary>
        private void Update()
        {
            ResolveIfSettingsChanged();
            if (!IsAnimated || frames == null || frames.Count == 0) return;

            elapsed += Time.unscaledDeltaTime;
            while (elapsed >= NebulaBrandAssetCatalog.LoadingLoopSeconds)
                elapsed -= NebulaBrandAssetCatalog.LoadingLoopSeconds;

            int index = (int)(elapsed * NebulaBrandAssetCatalog.LoadingFps);
            if (index >= frames.Count) index = frames.Count - 1;
            ShowFrame(index);
        }

        /// <summary>Test-facing: drives playback by an explicit delta, with no frame loop.</summary>
        public void AdvanceForTest(float unscaledSeconds)
        {
            if (!IsAnimated || frames == null || frames.Count == 0) return;
            elapsed += unscaledSeconds;
            while (elapsed >= NebulaBrandAssetCatalog.LoadingLoopSeconds)
                elapsed -= NebulaBrandAssetCatalog.LoadingLoopSeconds;
            int index = (int)(elapsed * NebulaBrandAssetCatalog.LoadingFps);
            if (index >= frames.Count) index = frames.Count - 1;
            ShowFrame(index);
        }

        private void ShowFrame(int index)
        {
            if (index == shownIndex) return;
            shownIndex = index;
            frame.sprite = frames[index];
            frame.enabled = frame.sprite != null;
        }

        private void Dispose()
        {
            frames = null;
            if (this == null) return;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>
        /// Clears the static handle if this instance dies some other way -- a shell
        /// teardown takes the Overlay root and everything on it, and a stale Active
        /// would make the next Show believe an overlay was already up.
        /// </summary>
        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }
    }
}
