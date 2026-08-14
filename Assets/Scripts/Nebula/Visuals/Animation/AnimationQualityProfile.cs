using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>The three animation cost tiers. Never serialized — a visual budget, not a setting the world remembers.</summary>
    public enum AnimationQuality
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// What one tier is allowed to spend on animation. Held as data so the
    /// director can be handed a profile in a test without a scene.
    /// <para>
    /// The tick rate is a <em>visual</em> rate only. Gameplay, physics and
    /// simulation continue at their own cadence whatever this says; lowering
    /// it makes motion coarser, never slower.
    /// </para>
    /// </summary>
    public readonly struct AnimationQualityProfile
    {
        public readonly AnimationQuality Quality;

        /// <summary>How often participants are posed, in ticks per second.</summary>
        public readonly float TickHz;

        /// <summary>
        /// Whether the small secondary motions — idle vibration, weight
        /// transfer, settle-back overshoot — are played at all. The cheap tier
        /// keeps only the motion that carries state.
        /// </summary>
        public readonly bool SecondaryMotion;

        /// <summary>Scales every secondary amplitude. The primary state poses are unaffected.</summary>
        public readonly float SecondaryScale;

        public AnimationQualityProfile(AnimationQuality quality, float tickHz, bool secondaryMotion, float secondaryScale)
        {
            Quality = quality;
            TickHz = tickHz;
            SecondaryMotion = secondaryMotion;
            SecondaryScale = secondaryScale;
        }

        /// <summary>Seconds between visual ticks.</summary>
        public float TickInterval => TickHz > 0f ? 1f / TickHz : 0f;

        /// <summary>Coarse but honest: state changes still read, nothing wobbles.</summary>
        public static readonly AnimationQualityProfile Low =
            new AnimationQualityProfile(AnimationQuality.Low, tickHz: 24f, secondaryMotion: false, secondaryScale: 0f);

        /// <summary>The target look.</summary>
        public static readonly AnimationQualityProfile Medium =
            new AnimationQualityProfile(AnimationQuality.Medium, tickHz: 40f, secondaryMotion: true, secondaryScale: 0.75f);

        /// <summary>Every controlled secondary motion, at display rate.</summary>
        public static readonly AnimationQualityProfile High =
            new AnimationQualityProfile(AnimationQuality.High, tickHz: 60f, secondaryMotion: true, secondaryScale: 1f);

        /// <summary>Never throws; anything unrecognised lands on <see cref="Medium"/>.</summary>
        public static AnimationQualityProfile Resolve(AnimationQuality quality)
        {
            switch (quality)
            {
                case AnimationQuality.Low: return Low;
                case AnimationQuality.High: return High;
                case AnimationQuality.Medium: return Medium;
                default: return Medium;
            }
        }

        /// <summary>
        /// Follows the lighting tier, so a player who turns the game down gets
        /// a consistently cheaper picture instead of two unrelated dials.
        /// </summary>
        public static AnimationQualityProfile FromLightingQuality(LightingQuality lightingQuality)
        {
            switch (lightingQuality)
            {
                case LightingQuality.Low: return Low;
                case LightingQuality.High: return High;
                default: return Medium;
            }
        }
    }
}
