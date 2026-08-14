using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>
    /// Poses the player ship's <em>visual</em> root from what the ship is
    /// already doing. It reads throttle, flight mode, hull and repair state and
    /// decides nothing: no flight state machine, no speed, no rotation, no
    /// physics.
    /// <para>
    /// Everything it writes lands on
    /// <see cref="PlayerShipVisualController.CurrentVisual"/> — a child of the
    /// gameplay root. The gameplay root itself, the Rigidbody, the colliders,
    /// the camera and every hardpoint's world position are untouched, so a
    /// projectile leaves the ship on exactly the same heading whether this
    /// component exists or not.
    /// </para>
    /// <para>
    /// It also deliberately does <em>not</em> drive the thrusters:
    /// <see cref="ThrusterEffect"/> already scales the engine halo and flame
    /// from throttle and speed, and two writers on one transform is the bug
    /// this whole part is built to avoid.
    /// </para>
    /// </summary>
    public class PlayerShipAnimationPresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        /// <summary>Idle vibration amplitude in local units — a heavy machine ticking over, not a boat on a swell.</summary>
        public const float IdleVibrationAmplitude = 0.008f;

        /// <summary>How far the nose settles back under full thrust, in degrees.</summary>
        public const float ThrustPitchDegrees = 2.2f;

        /// <summary>Extra settle under boost, in degrees.</summary>
        public const float BoostPitchDegrees = 3.6f;

        /// <summary>Degrees of visual roll per radian/second of yaw, before clamping.</summary>
        public const float BankDegreesPerYawRate = 22f;

        /// <summary>How quickly the visual pose chases its target. Higher is snappier; this is a visual filter only.</summary>
        public const float PoseDamping = 6f;

        private ShipController controller;
        private ShipCruise cruise;
        private ShipCombat combat;
        private PlayerShipVisualController visualController;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;

        private PoseAnchor visualRoot;
        private GameObject cachedVisual;

        private Quaternion previousRootRotation = Quaternion.identity;
        private bool hasPreviousRotation;

        private float bankDegrees;
        private float pitchDegrees;
        private float vibrationPhase;

        /// <summary>The state last resolved. Reuses the lighting vocabulary rather than inventing a second one.</summary>
        public ShipLightingState CurrentState { get; private set; } = ShipLightingState.Idle;

        /// <summary>Current visual bank, in degrees. Test-facing.</summary>
        public float BankDegrees => bankDegrees;

        /// <summary>True once a visual root has been found and captured.</summary>
        public bool HasVisualRoot => visualRoot.IsValid;

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(ShipController shipController, NebulaAnimationDirector animationDirector)
        {
            controller = shipController;
            cruise = shipController != null ? shipController.GetComponent<ShipCruise>() : null;
            combat = shipController != null ? shipController.GetComponent<ShipCombat>() : null;
            visualController = shipController != null ? shipController.GetComponent<PlayerShipVisualController>() : null;
            director = animationDirector;

            RebindVisualRoot();

            if (director != null) registration = director.Register(this);
        }

        /// <summary>
        /// Re-captures the visual root's authored pose. Called when the ship's
        /// visual is rebuilt — installing a module swaps the whole model, and
        /// the old anchor would then be pointing at a destroyed transform.
        /// </summary>
        public void RebindVisualRoot()
        {
            // Put the outgoing model back before letting go of it, so a swapped
            // root is never left carrying our offset.
            visualRoot.Reset();

            cachedVisual = visualController != null ? visualController.CurrentVisual : null;
            visualRoot = PoseAnchor.Capture(cachedVisual != null ? cachedVisual.transform : null);

            bankDegrees = 0f;
            pitchDegrees = 0f;
            hasPreviousRotation = false;
        }

        public void VisualTick(float deltaSeconds)
        {
            // A module install rebuilds the model; the anchor must follow it.
            GameObject liveVisual = visualController != null ? visualController.CurrentVisual : null;
            if (!ReferenceEquals(liveVisual, cachedVisual) || (cachedVisual != null && !visualRoot.IsValid))
                RebindVisualRoot();

            CurrentState = ResolveState();

            // No visual root yet (or none at all): nothing to pose, and the
            // ship still flies. This is the safe no-op the boot depends on.
            if (!visualRoot.IsValid) return;

            if (AnimationAccessibility.MotionOff)
            {
                visualRoot.Reset();
                return;
            }

            float motion = AnimationAccessibility.MotionScale
                           * (director != null && director.Profile.SecondaryMotion ? director.Profile.SecondaryScale : 0f);

            UpdateBank(deltaSeconds, motion);
            UpdatePitch(deltaSeconds, motion);

            Vector3 positionOffset = ResolveVibration(deltaSeconds, motion);
            var rotationOffset = Quaternion.Euler(pitchDegrees, 0f, bankDegrees);

            visualRoot.Apply(positionOffset, rotationOffset);
        }

        /// <summary>
        /// Reads the ship's own angular movement — the gameplay root's
        /// rotation delta — and leans the model into it. Read-only: the ship
        /// turns exactly as fast as it would with this component absent.
        /// </summary>
        private void UpdateBank(float deltaSeconds, float motion)
        {
            float yawRate = 0f;
            Quaternion rootRotation = transform.rotation;

            if (hasPreviousRotation && deltaSeconds > 0f)
            {
                Quaternion delta = Quaternion.Inverse(previousRootRotation) * rootRotation;
                delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);
                if (angleDegrees > 180f) angleDegrees -= 360f;
                // Only the component about the ship's own up axis banks it.
                yawRate = (angleDegrees * Mathf.Deg2Rad * axis.y) / deltaSeconds;
            }

            previousRootRotation = rootRotation;
            hasPreviousRotation = true;

            float ceiling = AnimationAccessibility.BankCeilingDegrees;
            float target = Mathf.Clamp(-yawRate * BankDegreesPerYawRate * motion, -ceiling, ceiling);
            bankDegrees = Mathf.Lerp(bankDegrees, target, 1f - Mathf.Exp(-PoseDamping * deltaSeconds));
        }

        /// <summary>Weight transfer: the nose settles back under thrust and further under boost.</summary>
        private void UpdatePitch(float deltaSeconds, float motion)
        {
            float throttle = controller != null ? Mathf.Clamp01(controller.GetThrottleFraction()) : 0f;

            float target;
            switch (CurrentState)
            {
                case ShipLightingState.Boost: target = BoostPitchDegrees; break;
                case ShipLightingState.Cruise: target = ThrustPitchDegrees * 0.5f; break; // steady, long-haul
                case ShipLightingState.Disabled: target = 0f; break;                      // safe closed pose
                default: target = ThrustPitchDegrees * throttle; break;
            }

            target *= motion;
            pitchDegrees = Mathf.Lerp(pitchDegrees, target, 1f - Mathf.Exp(-PoseDamping * deltaSeconds));
        }

        /// <summary>
        /// The small idle tick of a heavy machine, and the slightly irregular
        /// one of a damaged hull. Never enough to read as a wobble, and off
        /// entirely when the ship has no power.
        /// </summary>
        private Vector3 ResolveVibration(float deltaSeconds, float motion)
        {
            if (motion <= 0f || CurrentState == ShipLightingState.Disabled) return Vector3.zero;

            vibrationPhase += deltaSeconds;

            float amplitude = IdleVibrationAmplitude * motion;
            if (CurrentState == ShipLightingState.Damaged) amplitude *= 2.2f;

            // Two incommensurate rates so it never settles into a visible beat.
            float x = Mathf.Sin(vibrationPhase * 27f) * amplitude;
            float y = Mathf.Sin(vibrationPhase * 19.7f + 1.3f) * amplitude;
            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Classifies the ship from the same live sources the lighting reads.
        /// Neither presenter is an authority: both observe, independently, and
        /// neither writes anything back.
        /// </summary>
        private ShipLightingState ResolveState()
        {
            if (combat != null && combat.State != null)
            {
                if (combat.State.EmergencyShutdown) return ShipLightingState.Disabled;
                if (combat.Repair != null && combat.Repair.Active) return ShipLightingState.Repair;

                Durability durability = combat.State.Durability;
                if (durability != null && durability.HullMax > 0f && durability.Hull / durability.HullMax < 0.5f)
                    return ShipLightingState.Damaged;
            }

            if (cruise != null && (cruise.Mode == FlightMode.DeepSpaceCruise || cruise.Mode == FlightMode.CruiseRamp))
                return ShipLightingState.Cruise;

            if (controller != null)
            {
                if (controller.CombatBoostSpeedMultiplier > 1.001f) return ShipLightingState.Boost;
                if (controller.GetThrottleFraction() > 0.02f) return ShipLightingState.Throttle;
            }

            return ShipLightingState.Idle;
        }

        public void ResetToBasePose()
        {
            bankDegrees = 0f;
            pitchDegrees = 0f;
            vibrationPhase = 0f;
            hasPreviousRotation = false;
            visualRoot.Reset();
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            ResetToBasePose();
            controller = null;
            cruise = null;
            combat = null;
            visualController = null;
            director = null;
        }
    }
}
