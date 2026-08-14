using UnityEngine;
using Nebula.Visuals;

namespace Nebula.UI.Accessibility
{
    /// <summary>
    /// The one repeating emphasis in the interface, and the only thing allowed
    /// to produce one.
    /// <para>
    /// <see cref="UiAnimationProfiles.MaxRepeatHz"/> has been a ceiling with
    /// nothing under it: Flash Reduction lowered a number no code read, so the
    /// setting could be proven to exist and never proven to do anything. This is
    /// the consumer. Anything in the interface that wants to draw attention by
    /// repeating asks this object for its level and multiplies a colour by it —
    /// which means Flash Reduction and Photosensitivity Safe Mode reach every
    /// repeat in the game by construction rather than by each caller
    /// remembering.
    /// </para>
    /// <para>
    /// Three guarantees, in order of strength:
    /// </para>
    /// <list type="bullet">
    /// <item>Never above <see cref="AbsoluteCeilingHz"/>, whatever any setting or
    /// caller asks for. Three hertz is the general flash threshold; the interface
    /// is authored well under it and the clamp is here so it cannot drift over.</item>
    /// <item>Flash Reduction drops both the rate and the depth, so the same
    /// emphasis costs less luminance change per second.</item>
    /// <item>Safe Mode does not slow the repeat down, it removes it: the level
    /// holds at full and the state is carried by a steady colour, a steady shape
    /// and steady text instead.</item>
    /// </list>
    /// </summary>
    public sealed class UiEmphasisPulse
    {
        /// <summary>
        /// The hard ceiling, in hertz. Above three, a repeat stops being emphasis
        /// and becomes a flash hazard.
        /// </summary>
        public const float AbsoluteCeilingHz = 3f;

        /// <summary>How far down a normal pulse dips. 1 is full strength.</summary>
        public const float NormalFloor = 0.55f;

        /// <summary>How far down it dips once Flash Reduction is on. Shallower, so the swing is smaller.</summary>
        public const float ReducedFloor = 0.82f;

        private float phase;

        /// <summary>Where the pulse is, 0..1, this instant. Multiply a colour or an alpha by it.</summary>
        public float Level { get; private set; } = 1f;

        /// <summary>The rate actually in force, after every setting and the ceiling.</summary>
        public static float Hz => Mathf.Clamp(UiAnimationProfiles.MaxRepeatHz, 0f, AbsoluteCeilingHz);

        /// <summary>True when this pulse is allowed to move at all.</summary>
        public static bool Repeats => Hz > 0f && !AnimationAccessibility.MotionOff;

        /// <summary>The lowest value the pulse may reach, after accessibility.</summary>
        public static float Floor
        {
            get
            {
                if (!Repeats) return 1f;
                return LightingAccessibility.FlashReduction ? ReducedFloor : NormalFloor;
            }
        }

        /// <summary>
        /// The full luminance swing of one cycle, 0..1. This is the number the
        /// photosensitivity guarantee is really about — a slow deep pulse and a
        /// fast shallow one are not equally safe, so rate alone is not enough.
        /// </summary>
        public static float Depth => 1f - Floor;

        /// <summary>Back to a steady, undimmed state.</summary>
        public void Reset()
        {
            phase = 0f;
            Level = 1f;
        }

        /// <summary>
        /// Advances the pulse and returns the new level. Motion Off and Safe Mode
        /// both hold it at full: a state that has to move to be noticed is a state
        /// some players cannot use, so the answer is to stop moving, never to stop
        /// showing.
        /// </summary>
        public float Tick(float deltaSeconds)
        {
            if (!Repeats)
            {
                phase = 0f;
                Level = 1f;
                return Level;
            }

            phase += Mathf.Max(0f, deltaSeconds) * Hz;
            phase -= Mathf.Floor(phase);

            // A cosine, not a square wave: the whole point is that the change is
            // gradual. Instant switching at any rate is the thing that hurts.
            float wave = 0.5f * (1f + Mathf.Cos(phase * 2f * Mathf.PI));
            Level = Mathf.Lerp(Floor, 1f, wave);
            return Level;
        }

        /// <summary>
        /// The emphasised form of a colour. Never changes hue and never reaches
        /// zero, so a pulsing mark is always still the mark it was.
        /// </summary>
        public Color Apply(Color color)
        {
            return new Color(color.r * Level, color.g * Level, color.b * Level, color.a);
        }
    }
}
