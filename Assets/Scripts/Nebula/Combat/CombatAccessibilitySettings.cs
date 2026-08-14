namespace Nebula.Combat
{
    public enum ColorblindMode { None, Protanopia, Deuteranopia, Tritanopia }
    public enum AimAssistMode { Off, Standard3Degrees, Strong6Degrees }

    /// <summary>
    /// Session-only combat HUD accessibility settings (VS-013 §9) — deliberately
    /// NEVER part of <see cref="Extraction.SavePayload"/> ("bu ayarlar kampanya
    /// save'inden ayrı tutulmalı"). Plain mutable static fields for this
    /// slice; a future settings surface can add real persistence without
    /// this class's shape changing.
    /// </summary>
    public static class CombatAccessibilitySettings
    {
        public const float MinHudScale = 0.75f;
        public const float MaxHudScale = 1.50f;

        public static float HudScale = 1f;
        public static float ReticleSize = 1f;
        public static float ReticleThickness = 1f;
        public static ColorblindMode Colorblind = ColorblindMode.None;
        public static float ShakeIntensity01 = 0.5f; // 0-100 exposed as 0..1
        public static bool ReduceFlash = false;
        public static bool AudioVisualCueForWarnings = true;
        public static AimAssistMode AimAssist = AimAssistMode.Standard3Degrees;

        public static void ResetToDefaults()
        {
            HudScale = 1f; ReticleSize = 1f; ReticleThickness = 1f;
            Colorblind = ColorblindMode.None; ShakeIntensity01 = 0.5f;
            ReduceFlash = false; AudioVisualCueForWarnings = true;
            AimAssist = AimAssistMode.Standard3Degrees;
        }

        public static float ClampHudScale(float value) => UnityEngine.Mathf.Clamp(value, MinHudScale, MaxHudScale);

        public static float AimAssistDegrees(AimAssistMode mode) => mode switch
        {
            AimAssistMode.Off => 0f,
            AimAssistMode.Standard3Degrees => 3f,
            AimAssistMode.Strong6Degrees => 6f,
            _ => 0f
        };
    }
}
