using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>What a panel is doing right now.</summary>
    public enum UiPanelPhase
    {
        Hidden,
        Entering,
        Shown,
        Exiting
    }

    /// <summary>
    /// The timings and easing every player panel shares.
    /// <para>
    /// All of it is presentation. A panel's real open/close state, its input
    /// gate and its modal exclusivity belong to the UI itself; these numbers
    /// only decide how the pixels get there. Nothing here blinks, strobes or
    /// breathes: a HUD that never stops moving is a HUD nobody can read.
    /// </para>
    /// </summary>
    public static class UiAnimationProfiles
    {
        /// <summary>A small state change — a row selecting, a colour settling.</summary>
        public const float StateTransitionSeconds = 0.13f;

        /// <summary>A panel opening.</summary>
        public const float PanelEnterSeconds = 0.19f;

        /// <summary>A panel closing. Deliberately quicker than opening.</summary>
        public const float PanelExitSeconds = 0.15f;

        /// <summary>A notification's entry and its exit. The hold between them is the real one.</summary>
        public const float NotificationEnterSeconds = 0.18f;
        public const float NotificationExitSeconds = 0.18f;

        /// <summary>How far a panel slides on entry, in canvas units.</summary>
        public const float PanelSlideDistance = 18f;

        /// <summary>How much of that distance survives Reduced Motion.</summary>
        public const float ReducedMotionDistanceScale = 0.25f;

        /// <summary>How fast a bar fill chases its real value. The number itself never lags.</summary>
        public const float BarFillLerpPerSecond = 9f;

        /// <summary>How long a value-delta highlight lasts.</summary>
        public const float DeltaHighlightSeconds = 0.35f;

        /// <summary>The duration for a phase, or zero when nothing should animate.</summary>
        public static float DurationFor(UiPanelPhase phase)
        {
            if (AnimationAccessibility.MotionOff) return 0f;

            switch (phase)
            {
                case UiPanelPhase.Entering: return PanelEnterSeconds;
                case UiPanelPhase.Exiting: return PanelExitSeconds;
                default: return 0f;
            }
        }

        /// <summary>
        /// How far a panel may travel. Reduced Motion shortens the journey but
        /// never removes it, so the change is still visible.
        /// </summary>
        public static float SlideDistance()
        {
            if (AnimationAccessibility.MotionOff) return 0f;
            return AnimationAccessibility.ReducedMotion
                ? PanelSlideDistance * ReducedMotionDistanceScale
                : PanelSlideDistance;
        }

        /// <summary>
        /// A smooth in-out curve. No overshoot: a panel that bounces past its
        /// mark reads as a toy.
        /// </summary>
        public static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        /// <summary>
        /// Where a bar's fill should be this frame. The real value is passed
        /// straight through when motion is off, so nothing is ever hidden.
        /// </summary>
        public static float ChaseFill(float shown, float real, float deltaSeconds)
        {
            if (AnimationAccessibility.MotionOff || deltaSeconds <= 0f) return real;
            return Mathf.Lerp(shown, real, 1f - Mathf.Exp(-BarFillLerpPerSecond * deltaSeconds));
        }

        /// <summary>
        /// True while a panel must not accept clicks. A closing panel's buttons
        /// stop working the instant it starts closing.
        /// </summary>
        public static bool BlocksInput(UiPanelPhase phase)
            => phase == UiPanelPhase.Exiting || phase == UiPanelPhase.Hidden;

        /// <summary>True when the panel should be raycastable at all.</summary>
        public static bool IsInteractive(UiPanelPhase phase)
            => phase == UiPanelPhase.Shown || phase == UiPanelPhase.Entering;

        /// <summary>
        /// Photosensitivity keeps opacity and colour transitions — they carry
        /// information — but never a fast repeat. This is the ceiling on how
        /// often anything may change, in Hz.
        /// </summary>
        public static float MaxRepeatHz
            => LightingAccessibility.PhotosensitivitySafeMode ? 0f
             : LightingAccessibility.FlashReduction ? 1f
             : 2f;

        /// <summary>
        /// True when a repeating emphasis is allowed at all. Under Safe Mode a
        /// critical state is a steady colour, never a blink.
        /// </summary>
        public static bool AllowsRepeatingEmphasis => MaxRepeatHz > 0f;
    }
}
