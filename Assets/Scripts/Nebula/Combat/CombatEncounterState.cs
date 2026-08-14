namespace Nebula.Combat
{
    /// <summary>
    /// Pure combat-encounter open/close timer for ONE materialized system
    /// (VS-013 §5). Opens the instant any of the four documented triggers
    /// fires (player fires at a valid target, player selects a target, a
    /// friendly entity is attacked, an active enemy threat enters sensor
    /// range) — the MonoBehaviour layer detects those events and calls
    /// <see cref="RegisterThreatEvent"/>; this class only owns the "close 8s
    /// after the last one" timer itself.
    /// </summary>
    public class CombatEncounterState
    {
        public const float CloseAfterSeconds = 8f;

        public bool IsOpen { get; private set; }
        public float SinceLastThreatSeconds { get; private set; }

        public void RegisterThreatEvent()
        {
            IsOpen = true;
            SinceLastThreatSeconds = 0f;
        }

        public void Tick(float dt)
        {
            if (!IsOpen) return;
            SinceLastThreatSeconds += dt;
            if (SinceLastThreatSeconds >= CloseAfterSeconds) IsOpen = false;
        }

        public void RestoreFromSave(bool isOpen, float sinceLastThreatSeconds)
        {
            IsOpen = isOpen;
            SinceLastThreatSeconds = sinceLastThreatSeconds;
        }
    }
}
