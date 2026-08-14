using UnityEngine;

namespace Nebula.Navigation
{
    /// <summary>
    /// Pure advisory math for the Arrival Assist HUD warning (VS-006 §4):
    /// recommends where to start braking and what speed to be at when
    /// braking begins, assuming a uniform deceleration from cruise speed
    /// down to local speed over <see cref="CruiseStateMachine.DecelSeconds"/>
    /// (the same physical deceleration the cruise state machine itself
    /// performs). Advisory only — nothing here ever writes to the ship.
    /// </summary>
    public static class ArrivalAssist
    {
        /// <summary>Distance (uniform-deceleration average-speed formula) needed to shed speed down to localSpeed.</summary>
        public static float BrakeDistance(float currentSpeed, float localSpeed, float decelSeconds)
        {
            float from = Mathf.Max(currentSpeed, localSpeed);
            return Mathf.Max(0f, from - localSpeed) * Mathf.Max(0f, decelSeconds) * 0.5f;
        }

        /// <summary>True once the ship should start braking to arrive near localSpeed by the target.</summary>
        public static bool ShouldWarn(float distanceRemaining, float currentSpeed, float localSpeed, float decelSeconds)
        {
            return distanceRemaining <= BrakeDistance(currentSpeed, localSpeed, decelSeconds) * 1.15f;
        }

        /// <summary>ETA in seconds at a constant speed; -1 when the speed is too small to produce a meaningful estimate.</summary>
        public static float EtaSeconds(float distanceRemaining, float speed)
        {
            return speed > 0.5f ? distanceRemaining / speed : -1f;
        }
    }
}
