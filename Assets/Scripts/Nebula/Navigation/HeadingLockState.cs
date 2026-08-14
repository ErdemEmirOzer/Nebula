using UnityEngine;

namespace Nebula.Navigation
{
    /// <summary>
    /// Pure "Yön Sabitle" (H) state: holds the forward vector captured at
    /// engage time. Not an autopilot — it never points anywhere by itself,
    /// it only records what direction was held so a save can restore it and
    /// the ship-side controller can freeze rotation input while engaged.
    /// </summary>
    public class HeadingLockState
    {
        public bool Engaged { get; private set; }
        public Vector3 HeldDirection { get; private set; } = Vector3.forward;

        public void Engage(Vector3 currentForward)
        {
            Engaged = true;
            HeldDirection = currentForward.sqrMagnitude > 0.0001f ? currentForward.normalized : Vector3.forward;
        }

        public void Disengage() => Engaged = false;

        public void Toggle(Vector3 currentForward)
        {
            if (Engaged) Disengage(); else Engage(currentForward);
        }

        public void Restore(bool engaged, Vector3 direction)
        {
            Engaged = engaged;
            HeldDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }
    }
}
