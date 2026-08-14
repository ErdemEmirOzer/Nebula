using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Drives one bar's fill toward the real value, and flashes it once when
    /// something really changed.
    /// <para>
    /// The number a panel prints is never this object's: only the fill graphic
    /// is interpolated, so the text always reads the truth while the bar
    /// catches up. A load adopts whatever the value already is in silence — a
    /// restored save must not animate a bar up from zero as if it had just
    /// been earned.
    /// </para>
    /// <para>
    /// A highlight is raised only by a caller that already knows its
    /// transaction succeeded. A refused deposit, an out-of-range transfer and a
    /// zero-amount move all call nothing, so none of them can look like a win.
    /// </para>
    /// </summary>
    public sealed class UiValueAnimator
    {
        private float shown;
        private float lastReal;
        private float highlightRemaining;
        private bool adopted;

        /// <summary>Where the bar graphic currently is, 0..1. Test-facing.</summary>
        public float ShownFill => shown;

        /// <summary>How much highlight is left, 0..1. Test-facing.</summary>
        public float Highlight01 => UiAnimationProfiles.DeltaHighlightSeconds <= 0f
            ? 0f
            : Mathf.Clamp01(highlightRemaining / UiAnimationProfiles.DeltaHighlightSeconds);

        /// <summary>True while a delta is being shown. Test-facing.</summary>
        public bool IsHighlighting => highlightRemaining > 0f;

        /// <summary>Deltas shown since this bar started. Test-facing; never a gameplay count.</summary>
        public int HighlightsShown { get; private set; }

        /// <summary>
        /// Takes the current value as the starting point without animating to
        /// it. Called on the first frame and after a load.
        /// </summary>
        public void AdoptSilently(float realValue)
        {
            shown = realValue;
            lastReal = realValue;
            highlightRemaining = 0f;
            adopted = true;
        }

        /// <summary>
        /// Advances the fill toward the real value. Motion off passes the real
        /// value straight through, so nothing is ever hidden behind a bar that
        /// has not caught up.
        /// </summary>
        public float Tick(float realValue, float deltaSeconds)
        {
            if (!adopted) { AdoptSilently(realValue); return shown; }

            shown = UiAnimationProfiles.ChaseFill(shown, realValue, deltaSeconds);
            lastReal = realValue;

            if (highlightRemaining > 0f)
                highlightRemaining = Mathf.Max(0f, highlightRemaining - deltaSeconds);

            return shown;
        }

        /// <summary>
        /// Flashes once for a change that really happened. A zero or negative
        /// amount is not a change and raises nothing, so the caller can pass a
        /// transaction's own result straight in.
        /// </summary>
        public void RaiseDelta(float amountMoved)
        {
            if (amountMoved <= 0f) return;
            if (AnimationAccessibility.MotionOff) return;

            highlightRemaining = UiAnimationProfiles.DeltaHighlightSeconds;
            HighlightsShown++;
        }

        /// <summary>The real value last seen. Test-facing; the text should print this, not the fill.</summary>
        public float RealValue => lastReal;

        /// <summary>Back to nothing shown and nothing pending.</summary>
        public void Reset()
        {
            shown = 0f;
            lastReal = 0f;
            highlightRemaining = 0f;
            adopted = false;
            HighlightsShown = 0;
        }
    }
}
