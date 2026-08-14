using UnityEngine;

namespace Nebula.Hangar
{
    /// <summary>
    /// Pure on-station scan timer for a Scout vehicle (VS-007 §4): 60 active
    /// seconds within 1500 range of the target planet. Leaving range pauses
    /// progress (never resets it) — the physical mover independently tries
    /// to re-approach, so this class only needs to know "in range this tick
    /// or not."
    /// </summary>
    public class ScoutScanWork
    {
        public const float RequiredSeconds = 60f;
        public const float Range = 1500f;

        public float Elapsed { get; private set; }

        /// <summary>Advances by dt only while in range; returns true the tick that crosses RequiredSeconds.</summary>
        public bool Tick(float dt, bool inRange)
        {
            if (!inRange) return false;
            Elapsed += dt;
            return Elapsed >= RequiredSeconds;
        }

        public void Reset() => Elapsed = 0f;
        public void Restore(float elapsed) => Elapsed = Mathf.Clamp(elapsed, 0f, RequiredSeconds);
    }
}
