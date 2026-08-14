using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The one clock every moving world on the map is read from.
    ///
    /// A planet's position is a pure function of time — its start angle plus its own real
    /// angular speed times the seconds elapsed — rather than something integrated frame by
    /// frame. That matters for more than tidiness: the bodies, the click picker, the
    /// selection reticle and the plotted route all have to agree on where a world IS right
    /// now, and three of those four are not even ticking. A shared function cannot drift
    /// the way four separate accumulators would, so a click can never land where a planet
    /// was half a second ago.
    ///
    /// The clock runs on the game's scaled simulation time. Pausing or slowing the
    /// authoritative world therefore pauses or slows the map too; wall-clock time and time
    /// spent offline can never move a gameplay orbit.
    /// </summary>
    public static class StrategicMapOrbitClock
    {
        private static double? overrideSeconds;

        /// <summary>Seconds the orbits have been running for.</summary>
        public static double Seconds => overrideSeconds ?? Time.timeAsDouble;

        /// <summary>Pins the clock, so a test can place a planet at an exact point on its orbit.</summary>
        public static void OverrideSeconds(double seconds) => overrideSeconds = seconds;

        /// <summary>Hands the clock back to real time.</summary>
        public static void ClearOverride() => overrideSeconds = null;

        /// <summary>
        /// Where this world is on its orbit right now, as the phase
        /// <see cref="StrategicMapOrbits.OrbitPoint"/> takes. Accumulated in double and cast
        /// once at the end: after an hour of play a float would be quantising the angle.
        /// </summary>
        public static float PhaseOf(StrategicMapUniverseSnapshot.PlanetRow planet) =>
            planet == null ? 0f : planet.phaseFrozen
                ? planet.phase
                : (float)(planet.phase + planet.AngularSpeed * (Seconds - planet.phaseEpochSeconds));
    }
}
