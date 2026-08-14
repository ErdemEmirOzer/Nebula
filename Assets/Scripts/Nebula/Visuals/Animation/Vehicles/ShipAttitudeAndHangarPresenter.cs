using System.Collections.Generic;
using UnityEngine;
using Nebula.Hangar;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>
    /// Works the two mechanisms on the player's own hull: the hangar hatch, and
    /// the auxiliary thrusters that answer a turn.
    /// <para>
    /// Both read and never write. The hatch follows the phases
    /// <see cref="ShipHangar"/>'s vehicles are really in, and opening it neither
    /// starts nor delays a launch — a vehicle leaves exactly when the hangar
    /// says so, whatever the door is doing. The auxiliary thrusters answer the
    /// ship's own frame-to-frame rotation; no input is read here and no torque
    /// is produced. The main engine anchors stay ThrusterEffect's.
    /// </para>
    /// </summary>
    public sealed class ShipAttitudeAndHangarPresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        /// <summary>How far a turn has to be to register visually, in degrees per second.</summary>
        private const float AttitudeDeadZone = 2f;

        /// <summary>Full auxiliary deflection is reached at this turn rate.</summary>
        private const float AttitudeFullRate = 45f;

        private ShipHangar hangar;
        private PlayerShipVisualController shipVisual;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;

        private PoseAnchor hatch;
        private PoseAnchor auxLeft;
        private PoseAnchor auxRight;
        private GameObject boundVisual;

        private Quaternion previousRotation;
        private bool hasPreviousRotation;
        private float door01;
        private float yawSignal;

        /// <summary>True while the real hangar has an operation that opens the door. Test-facing.</summary>
        public bool DoorShouldBeOpen { get; private set; }

        /// <summary>How far the hatch is open, 0..1. Test-facing.</summary>
        public float DoorOpen01 => door01;

        /// <summary>True when the ship's model really carries the hatch. Test-facing.</summary>
        public bool HasHatch => hatch.IsValid;

        /// <summary>True when both auxiliary thrusters were found. Test-facing.</summary>
        public bool HasAttitudeThrusters => auxLeft.IsValid && auxRight.IsValid;

        /// <summary>The turn the ship is really making, -1..1. Test-facing; never written to the ship.</summary>
        public float AttitudeSignal => yawSignal;

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(ShipHangar shipHangar, PlayerShipVisualController visualController,
            NebulaAnimationDirector animationDirector)
        {
            hangar = shipHangar;
            shipVisual = visualController;
            director = animationDirector;

            Bind();
            if (director != null) registration = director.Register(this);
        }

        /// <summary>
        /// Captures whichever of the two mechanisms this hull really has. Early
        /// stages carry no hangar bay at all, and that is a clean no-op rather
        /// than an invented door.
        /// </summary>
        private void Bind()
        {
            hatch.Reset();
            auxLeft.Reset();
            auxRight.Reset();

            GameObject visual = shipVisual != null ? shipVisual.CurrentVisual : null;
            boundVisual = visual;
            if (visual == null) return;

            hatch = PoseAnchor.Capture(
                NebulaVisualResolver.FindAnchor(visual.transform, ShipVehicleMechanicalProfiles.HangarHatchNode));
            auxLeft = PoseAnchor.Capture(
                NebulaVisualResolver.FindAnchor(visual.transform, "part.thruster.aux.L"));
            auxRight = PoseAnchor.Capture(
                NebulaVisualResolver.FindAnchor(visual.transform, "part.thruster.aux.R"));
        }

        public void VisualTick(float deltaSeconds)
        {
            // A stage or Mk change rebuilds the hull under us.
            GameObject visual = shipVisual != null ? shipVisual.CurrentVisual : null;
            if (!ReferenceEquals(visual, boundVisual)) Bind();

            DoorShouldBeOpen = ResolveDoorWanted();
            UpdateDoor(deltaSeconds);
            UpdateAttitude(deltaSeconds);
        }

        /// <summary>
        /// The door opens for the phases a vehicle really passes through on its
        /// way out and back in, and for nothing else. A refused launch never
        /// leaves the hangar phase, so it never opens the door.
        /// </summary>
        private bool ResolveDoorWanted()
        {
            if (hangar == null || hangar.Vehicles == null) return false;

            foreach (VehicleAgent vehicle in hangar.Vehicles)
            {
                if (vehicle == null || vehicle.Mission == null) continue;
                VehiclePhase phase = vehicle.Mission.Phase;
                if (phase == VehiclePhase.Launching || phase == VehiclePhase.Docking) return true;
            }
            return false;
        }

        private void UpdateDoor(float deltaSeconds)
        {
            if (!hatch.IsValid) return;

            float wanted = DoorShouldBeOpen ? 1f : 0f;

            // Snapping is what motion-off means; otherwise the door takes its
            // own time, which is deliberately shorter than any launch phase so
            // it can never be what a vehicle is waiting on.
            door01 = AnimationAccessibility.SnapStateChanges
                ? wanted
                : Mathf.MoveTowards(door01, wanted, deltaSeconds * 1.6f);

            MechanicalPart part = ShipVehicleMechanicalProfiles.HangarDoor[0];
            hatch.Apply(Vector3.zero, Quaternion.AngleAxis(part.Magnitude * door01, part.Axis));
        }

        /// <summary>
        /// Reads the hull's own rotation between ticks. Nothing here touches
        /// input, torque or the flight model — the ship turns exactly as it
        /// would with the animation switched off.
        /// </summary>
        private void UpdateAttitude(float deltaSeconds)
        {
            Quaternion rotation = transform.rotation;

            float rate = 0f;
            if (hasPreviousRotation && deltaSeconds > 0f)
            {
                Vector3 previousForward = previousRotation * Vector3.forward;
                Vector3 currentForward = rotation * Vector3.forward;
                Vector3 localDelta = Quaternion.Inverse(rotation) * (currentForward - previousForward);
                rate = localDelta.x / deltaSeconds * Mathf.Rad2Deg;
            }
            previousRotation = rotation;
            hasPreviousRotation = true;

            yawSignal = Mathf.Abs(rate) < AttitudeDeadZone
                ? 0f
                : Mathf.Clamp(rate / AttitudeFullRate, -1f, 1f);

            if (AnimationAccessibility.MotionOff) yawSignal = 0f;

            if (!auxLeft.IsValid && !auxRight.IsValid) return;

            float scale = director != null
                ? CombatMechanicsProfiles.SecondaryScale(director.Profile)
                : AnimationAccessibility.MotionScale;

            // Opposite sides answer a turn in opposite directions, which is what
            // an attitude thruster pair does.
            MechanicalPart[] aux = ShipVehicleMechanicalProfiles.AttitudeThrusters;
            if (auxLeft.IsValid)
                auxLeft.Apply(Vector3.zero, Quaternion.AngleAxis(aux[0].Magnitude * yawSignal * scale, aux[0].Axis));
            if (auxRight.IsValid)
                auxRight.Apply(Vector3.zero, Quaternion.AngleAxis(aux[1].Magnitude * -yawSignal * scale, aux[1].Axis));
        }

        public void ResetToBasePose()
        {
            hatch.Reset();
            auxLeft.Reset();
            auxRight.Reset();
            door01 = 0f;
            yawSignal = 0f;
            hasPreviousRotation = false;
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            ResetToBasePose();
            hangar = null;
            shipVisual = null;
            director = null;
        }
    }
}
