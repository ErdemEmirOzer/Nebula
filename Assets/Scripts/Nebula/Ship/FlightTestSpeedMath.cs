using UnityEngine;

namespace Nebula.Ship
{
    /// <summary>
    /// Pure, testable math for DT-001's session-local flight test speed
    /// multiplier: slider↔multiplier conversion (logarithmic, 1×–100×), the
    /// release/debug-build gate, and the one place the final commanded speed
    /// is composed. This is a stateless calculator only — it holds no
    /// dependency on <see cref="ShipController"/> or any runtime object and
    /// is never a second speed or gameplay authority; <see cref="ShipController"/>
    /// remains the sole owner of the ship's actual physical velocity.
    /// </summary>
    public static class FlightTestSpeedMath
    {
        public const float MinMultiplier = 1f;
        public const float MaxMultiplier = 100f;

        public static float ClampMultiplier(float raw) => Mathf.Clamp(raw, MinMultiplier, MaxMultiplier);

        /// <summary>Logarithmic slider (0..1) -&gt; multiplier: 1× at 0, ~10× at 0.5, 100× at 1.</summary>
        public static float MultiplierFromSlider(float slider01) => ClampMultiplier(Mathf.Pow(10f, 2f * Mathf.Clamp01(slider01)));

        /// <summary>Inverse of <see cref="MultiplierFromSlider"/> — for initializing/round-tripping the slider from a known multiplier.</summary>
        public static float SliderFromMultiplier(float multiplier) => Mathf.Clamp01(Mathf.Log10(ClampMultiplier(multiplier)) / 2f);

        /// <summary>The effective multiplier actually applied to flight — forced to 1× whenever debug tools are not allowed (release/non-development build), regardless of whatever raw value was set.</summary>
        public static float ResolveEffectiveMultiplier(float rawMultiplier, bool debugToolsAllowed) =>
            debugToolsAllowed ? ClampMultiplier(rawMultiplier) : MinMultiplier;

        /// <summary>The single place the final commanded flight speed is composed: throttle × local boost × cruise multiplier × test multiplier, applied exactly once.</summary>
        public static float CommandedSpeed(float throttle, float localBoost, float cruiseSpeedMultiplier, float testSpeedMultiplier) =>
            throttle * localBoost * cruiseSpeedMultiplier * testSpeedMultiplier;
    }
}
