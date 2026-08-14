using UnityEngine;

namespace Nebula.Navigation
{
    /// <summary>
    /// Pure state for the single active signal analysis (VS-006 §3): one
    /// Signal-level system at a time, ticked only by active-simulation
    /// deltaTime (callers must never pass wall-clock/offline time — pause
    /// and offline naturally produce dt=0 the same way every other pure
    /// Tick(dt) system in this project does). No resource cost, no XP —
    /// completion only raises the target to Located.
    /// </summary>
    public class SignalAnalysisState
    {
        public string TargetSystemId { get; private set; }
        public float RemainingSeconds { get; private set; }
        public bool Active => TargetSystemId != null;

        public bool Start(string systemId, float seconds)
        {
            if (Active || string.IsNullOrEmpty(systemId) || seconds <= 0f) return false;
            TargetSystemId = systemId;
            RemainingSeconds = seconds;
            return true;
        }

        /// <summary>Advances by dt; returns true exactly once, the tick that crosses zero.</summary>
        public bool Tick(float dt)
        {
            if (!Active) return false;
            RemainingSeconds -= dt;
            if (RemainingSeconds <= 0f) { RemainingSeconds = 0f; return true; }
            return false;
        }

        public void Clear()
        {
            TargetSystemId = null;
            RemainingSeconds = 0f;
        }

        public void Restore(string systemId, float remainingSeconds)
        {
            if (string.IsNullOrEmpty(systemId)) { Clear(); return; }
            TargetSystemId = systemId;
            RemainingSeconds = Mathf.Max(0f, remainingSeconds);
        }
    }
}
