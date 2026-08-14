using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Visual-only accessibility switches for lighting. Every one of these
    /// changes how the same world is drawn and nothing else: no gameplay
    /// value, no simulation input, no save field reads or writes these. A
    /// player who turns all of them on is playing exactly the same game, lit
    /// differently.
    /// <para>
    /// Static because lighting is a global render state and there is exactly
    /// one screen. Defaults are the unmodified look, so a session that never
    /// touches settings behaves as if this class did not exist.
    /// </para>
    /// </summary>
    public static class LightingAccessibility
    {
        /// <summary>Hard floor and ceiling on <see cref="LightIntensityScale"/> — dark enough to help, never dark enough to make the game unreadable.</summary>
        public const float MinIntensityScale = 0.25f;
        public const float MaxIntensityScale = 1.5f;

        /// <summary>How much of a flash survives when <see cref="FlashReduction"/> is on.</summary>
        public const float FlashReductionFactor = 0.35f;

        /// <summary>Photosensitivity Safe Mode is stricter still, and cannot be softened by any other setting.</summary>
        public const float PhotosensitivityFlashFactor = 0.15f;

        /// <summary>How far <see cref="HighContrast"/> pulls the ambient floor down, deepening the gap between lit and unlit.</summary>
        public const float HighContrastAmbientScale = 0.4f;

        private static float lightIntensityScale = 1f;

        /// <summary>Softens weapon, explosion and damage flashes without dimming steady light.</summary>
        public static bool FlashReduction { get; set; }

        /// <summary>Whole-scene brightness trim. Clamped on write, so no caller can drive the screen to black.</summary>
        public static float LightIntensityScale
        {
            get => lightIntensityScale;
            set => lightIntensityScale = Mathf.Clamp(value, MinIntensityScale, MaxIntensityScale);
        }

        /// <summary>Deepens shadow by lowering the ambient floor, so silhouettes separate from the background more sharply.</summary>
        public static bool HighContrast { get; set; }

        /// <summary>
        /// The strongest guarantee this class makes: no rapid, high-amplitude
        /// flashing. Implies <see cref="FlashReduction"/> and overrides it
        /// downward — turning Flash Reduction off cannot re-enable full-strength
        /// flashes while this is on.
        /// </summary>
        public static bool PhotosensitivitySafeMode { get; set; }

        /// <summary>Restores the unmodified look. Called on session start so one campaign never inherits another's settings.</summary>
        public static void ResetToDefaults()
        {
            FlashReduction = false;
            lightIntensityScale = 1f;
            HighContrast = false;
            PhotosensitivitySafeMode = false;
        }

        /// <summary>Applies the brightness trim to a steady light (key, fill, emissive glow).</summary>
        public static float ScaleIntensity(float intensity) => intensity * lightIntensityScale;

        /// <summary>
        /// Applies the brightness trim plus whichever flash limit is in force.
        /// Photosensitivity Safe Mode always wins, so a caller cannot get a
        /// full-strength flash past it by leaving Flash Reduction off.
        /// </summary>
        public static float ScaleFlashIntensity(float intensity)
        {
            float scaled = ScaleIntensity(intensity);
            if (PhotosensitivitySafeMode) return scaled * PhotosensitivityFlashFactor;
            if (FlashReduction) return scaled * FlashReductionFactor;
            return scaled;
        }

        /// <summary>
        /// The shortest a flash is allowed to be. Safe Mode stretches brief
        /// pops into something the eye reads as a glow rather than a strobe.
        /// </summary>
        public static float ResolveFlashDuration(float requestedSeconds)
            => PhotosensitivitySafeMode ? Mathf.Max(requestedSeconds, 0.25f) : requestedSeconds;

        /// <summary>The ambient floor after accessibility, given the art-directed base.</summary>
        public static Color ResolveAmbient(Color baseAmbient)
        {
            Color ambient = baseAmbient * lightIntensityScale;
            if (HighContrast) ambient *= HighContrastAmbientScale;
            ambient.a = 1f;
            return ambient;
        }
    }
}
