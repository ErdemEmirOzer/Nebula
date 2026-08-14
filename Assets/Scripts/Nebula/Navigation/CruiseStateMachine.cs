using UnityEngine;

namespace Nebula.Navigation
{
    public enum FlightMode { LocalFlight, CruiseRamp, DeepSpaceCruise, CruiseDeceleration }

    /// <summary>
    /// Every non-engine condition the ship must satisfy, continuously, to enter or stay
    /// in cruise — plus <see cref="InputFree"/>, which gates entry alone.
    /// </summary>
    public struct CruiseEntryConditions
    {
        public bool ThrottleHigh;
        public bool NotStabilized;
        public bool BuildModeOff;
        public bool NoPhysicalInteractionActive;
        public bool NotNearHazard;
        /// <summary>
        /// True while the player's input is actually being sampled — false behind an open
        /// screen. Deliberately NOT part of <see cref="AllMet"/> (P2-TEST-001): a menu
        /// suppresses input without stopping the world clock, so a cruise already under way
        /// must keep ramping, cruising and decelerating while a screen is open, and a
        /// throttle merely held down behind that screen must never start a new one.
        /// <see cref="CanEngage"/> is the entry test; <see cref="AllMet"/> is the sustain test.
        /// </summary>
        public bool InputFree;
        public bool NoSharpTurn;
        /// <summary>False while inside a star's Warning or Critical stellar-risk radius (VS-009 §2).</summary>
        public bool NotInStellarRisk;
        /// <summary>False while the ship's combat state is Damaged (VS-013.1 §5) — Deep Space Cruise unavailable until repaired back above the Damaged thresholds.</summary>
        public bool NotDamaged;

        /// <summary>
        /// The player's standing answer to "may this ship cruise" (P1-COMBAT-005).
        /// <para>
        /// Cruise had no control of any kind: it engaged whenever the other conditions
        /// lined up and could only be left by breaking one of them. This is the one
        /// condition the player sets directly, through <c>flight.cruise_toggle</c>. It
        /// belongs in <see cref="AllMet"/>, not only in <see cref="CanEngage"/> —
        /// disarming mid-cruise must actually leave cruise, which is the whole point of
        /// having the control — and it defaults to armed, so a player who never presses
        /// the key sees exactly the behaviour that shipped.
        /// </para>
        /// </summary>
        public bool PlayerArmed;

        /// <summary>The world-state conditions cruise needs continuously — to enter AND to stay.</summary>
        public bool AllMet => PlayerArmed && ThrottleHigh && NotStabilized && BuildModeOff && NoPhysicalInteractionActive
                               && NotNearHazard && NoSharpTurn && NotInStellarRisk && NotDamaged;

        /// <summary>
        /// Entering cruise, and re-accelerating out of a deceleration, additionally require
        /// live input: both are transitions the player triggers, and neither may fire from a
        /// key held down behind an open screen.
        /// </summary>
        public bool CanEngage => AllMet && InputFree;
    }

    /// <summary>
    /// Pure physical-ramp cruise state machine (VS-006 §4). LocalFlight to
    /// DeepSpaceCruise only through an 8-second CruiseRamp (SpeedMultiplier
    /// climbs from 1x to cruiseSpeed/localMaxSpeed); losing any entry
    /// condition or the engine tier dropping below T1 starts an 8-second
    /// CruiseDeceleration back down to 1x, starting from whatever multiplier
    /// was actually reached (no snap). Re-satisfying conditions mid-decel
    /// resumes the ramp from the equivalent point instead of restarting
    /// cold. SpeedMultiplier only ever changes continuously, never jumps.
    /// </summary>
    public class CruiseStateMachine
    {
        public const float RampSeconds = 8f;
        public const float DecelSeconds = 8f;

        public FlightMode Mode { get; private set; } = FlightMode.LocalFlight;
        public float RampElapsed { get; private set; }
        public float DecelElapsed { get; private set; }
        public int EngineTier { get; private set; }
        public float SpeedMultiplier { get; private set; } = 1f;

        private float decelStartRatio = 1f;

        public void SetEngineTier(int tier) => EngineTier = Mathf.Clamp(tier, 0, 2);

        public void Tick(float dt, CruiseEntryConditions conditions, float localMaxSpeed, float cruiseTargetSpeed)
        {
            // Two different questions, deliberately not the same one (P2-TEST-001).
            // `eligible` is "may this cruise CONTINUE" and is pure world state, so it keeps
            // being answered while a screen is open. `canEngage` is "may the player START
            // accelerating now" and additionally needs input to be live, so a throttle held
            // behind a menu cannot enter cruise or climb back out of a deceleration.
            bool eligible = EngineTier >= 1 && conditions.AllMet;
            bool canEngage = EngineTier >= 1 && conditions.CanEngage;
            float targetRatio = localMaxSpeed > 0.001f ? Mathf.Max(1f, cruiseTargetSpeed / localMaxSpeed) : 1f;

            switch (Mode)
            {
                case FlightMode.LocalFlight:
                    SpeedMultiplier = 1f;
                    if (canEngage) { Mode = FlightMode.CruiseRamp; RampElapsed = 0f; }
                    break;

                case FlightMode.CruiseRamp:
                    if (!eligible) { decelStartRatio = SpeedMultiplier; Mode = FlightMode.CruiseDeceleration; DecelElapsed = 0f; break; }
                    RampElapsed += dt;
                    float rampT = Mathf.Clamp01(RampElapsed / RampSeconds);
                    SpeedMultiplier = Mathf.Lerp(1f, targetRatio, rampT);
                    if (RampElapsed >= RampSeconds) { Mode = FlightMode.DeepSpaceCruise; SpeedMultiplier = targetRatio; }
                    break;

                case FlightMode.DeepSpaceCruise:
                    if (!eligible) { decelStartRatio = SpeedMultiplier; Mode = FlightMode.CruiseDeceleration; DecelElapsed = 0f; break; }
                    SpeedMultiplier = targetRatio;
                    break;

                case FlightMode.CruiseDeceleration:
                    if (canEngage)
                    {
                        // Resume the ramp from the equivalent progress instead of a cold restart.
                        Mode = FlightMode.CruiseRamp;
                        RampElapsed = Mathf.Clamp01(1f - DecelElapsed / DecelSeconds) * RampSeconds;
                        break;
                    }
                    DecelElapsed += dt;
                    float decelT = Mathf.Clamp01(DecelElapsed / DecelSeconds);
                    SpeedMultiplier = Mathf.Lerp(decelStartRatio, 1f, decelT);
                    if (DecelElapsed >= DecelSeconds)
                    {
                        Mode = FlightMode.LocalFlight;
                        SpeedMultiplier = 1f;
                        RampElapsed = 0f;
                    }
                    break;
            }
        }

        /// <summary>Restore from a save: exact mode, elapsed ramp/decel seconds and the multiplier it implies.</summary>
        public void Restore(FlightMode mode, float rampOrDecelElapsedSeconds, int engineTier, float speedMultiplier)
        {
            EngineTier = Mathf.Clamp(engineTier, 0, 2);
            Mode = mode;
            RampElapsed = mode == FlightMode.CruiseRamp ? Mathf.Max(0f, rampOrDecelElapsedSeconds) : 0f;
            DecelElapsed = mode == FlightMode.CruiseDeceleration ? Mathf.Max(0f, rampOrDecelElapsedSeconds) : 0f;
            SpeedMultiplier = Mathf.Max(1f, speedMultiplier);
            decelStartRatio = SpeedMultiplier;
        }
    }
}
