using UnityEngine;
using Nebula.Visuals;

namespace Nebula.Tutorial
{
    /// <summary>
    /// The tutorial's one answer to "how long, how bright, how much movement".
    /// <para>
    /// Every timing here is in <b>unscaled</b> seconds. The tutorial is allowed
    /// to compress gameplay time (<see cref="TutorialSession.SetTutorialTimeScale"/>)
    /// and is allowed to be paused; neither may change how long the instruction
    /// card takes to appear or how long a success beat is held, or the interface
    /// would race away from the player at 8x and freeze at 0x.
    /// </para>
    /// <para>
    /// The accessibility switches are the project's own -- there is no tutorial
    /// copy of them and no tutorial default that could disagree with the settings
    /// page. Motion Off snaps straight to the end pose rather than removing it:
    /// a step still has to visibly complete, it just must not be watched
    /// travelling there.
    /// </para>
    /// </summary>
    public static class TutorialMotion
    {
        /// <summary>How long a success beat is held before the director moves on, at full motion.</summary>
        public const float SuccessBeatSeconds = 1.1f;

        /// <summary>How long a corrective feedback line is guaranteed to stay legible, at full motion.</summary>
        public const float FeedbackHoldSeconds = 2.0f;

        /// <summary>Card transition length, at full motion.</summary>
        public const float CardTransitionSeconds = 0.28f;

        /// <summary>True when a state change must snap rather than travel.</summary>
        public static bool Snap => AnimationAccessibility.MotionOff;

        /// <summary>
        /// A duration, after the motion settings. Motion Off returns 0 -- the end
        /// pose is applied on the same frame -- and Reduced Motion shortens rather
        /// than removes, so a beat still registers as a beat.
        /// </summary>
        public static float Duration(float seconds)
        {
            if (AnimationAccessibility.MotionOff) return 0f;
            return AnimationAccessibility.ReducedMotion ? seconds * AnimationAccessibility.ReducedMotionScale : seconds;
        }

        /// <summary>
        /// A hold, after the motion settings. Unlike <see cref="Duration"/> this
        /// never collapses to zero: Motion Off is about movement, not about
        /// taking text away before it has been read.
        /// </summary>
        public static float Hold(float seconds) =>
            AnimationAccessibility.ReducedMotion ? Mathf.Max(seconds * 0.6f, 0.8f) : seconds;

        /// <summary>
        /// False when the player has asked for no repeating emphasis at all --
        /// Photosensitivity Safe Mode, Flash Reduction, or Motion Off. A pulsing
        /// highlight on the thing the step wants clicked is the tutorial's most
        /// obvious use of a flash, and this is the one gate it goes through.
        /// </summary>
        public static bool EmphasisPulseAllowed =>
            !LightingAccessibility.PhotosensitivitySafeMode &&
            !LightingAccessibility.FlashReduction &&
            !AnimationAccessibility.MotionOff;

        /// <summary>A highlight intensity, already through the project's flash limits. Photosensitivity Safe Mode always wins.</summary>
        public static float EmphasisIntensity(float intensity) =>
            LightingAccessibility.ScaleFlashIntensity(intensity);

        /// <summary>True when the interface should draw at the high-contrast end of its palette.</summary>
        public static bool HighContrast => LightingAccessibility.HighContrast;

        /// <summary>Eased 0→1 progress for a card transition, snapped to 1 under Motion Off.</summary>
        public static float Progress(float elapsedUnscaled, float seconds)
        {
            float length = Duration(seconds);
            if (length <= 0f) return 1f;
            float t = Mathf.Clamp01(elapsedUnscaled / length);
            return t * t * (3f - 2f * t);
        }
    }
}
