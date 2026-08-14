using System;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Simulation;

namespace Nebula.Ship
{
    /// <summary>
    /// One frame of the flight controls, as the player is holding them.
    /// <para>
    /// The same seam <c>NebulaKeyCaptureListener.Source</c> is: this project uses
    /// the legacy Input Manager, and <c>UnityEngine.Input</c> cannot be faked in an
    /// automated test here at all. Without a seam, translation and boost were
    /// <c>Input.GetKey</c> calls read inline in a private method, so nothing outside
    /// the input loop could exercise the flight the game actually flies — the same
    /// hole <see cref="ShipController.ApplyLook"/> was extracted to close for
    /// rotation.
    /// </para>
    /// <para>
    /// It carries the controls and nothing else. There is no speed here, no
    /// velocity and no "make the ship fast" call: a source can say the boost
    /// control is held, and the ship still decides — from cruise state, throttle,
    /// tier and its own commanded-speed formula — whether that means anything.
    /// </para>
    /// </summary>
    public interface IShipFlightInput
    {
        /// <summary>
        /// Local-space thrust request: x strafes, y lifts, z drives forward. Each
        /// axis is the sum of its two controls, so opposing controls cancel to zero
        /// exactly as holding both keys does.
        /// </summary>
        Vector3 Thrust { get; }

        /// <summary>
        /// True while <i>any</i> thrust control is held, including when opposing
        /// ones cancel <see cref="Thrust"/> to zero. Orbit Stabilize releases on a
        /// commanded thrust rather than on a resulting direction, so the two are
        /// not the same question.
        /// </summary>
        bool AnyThrustHeld { get; }

        /// <summary>True while the boost control is held. Whether it does anything is the ship's decision, not this one's.</summary>
        bool BoostHeld { get; }
    }

    /// <summary>
    /// Free-flight 6-DOF controller for the observation ship. No combat, no
    /// systems — just smooth inertial movement for exploring the space scene.
    /// Click the game view to capture the mouse, Esc to release it. The HUD
    /// (speed, location, throttle bar) lives in Nebula.UI.ShipHud.
    /// </summary>
    public class ShipController : MonoBehaviour
    {
        /// <summary>
        /// The shipping source: the keyboard, read through the same
        /// <c>UnityEngine.Input</c> calls that used to sit inline in
        /// <see cref="HandleTranslation"/>, in the same order, with the same keys.
        /// </summary>
        private sealed class KeyboardFlightInput : IShipFlightInput
        {
            public Vector3 Thrust
            {
                get
                {
                    Vector3 input = Vector3.zero;
                    if (Input.GetKey(KeyCode.W)) input.z += 1f;
                    if (Input.GetKey(KeyCode.S)) input.z -= 1f;
                    if (Input.GetKey(KeyCode.D)) input.x += 1f;
                    if (Input.GetKey(KeyCode.A)) input.x -= 1f;
                    if (Input.GetKey(KeyCode.Space)) input.y += 1f;
                    // C is the one flight key another context also claims: Evacuation's
                    // "Geri Al" outranks down-thrust wherever reclaim can actually land.
                    // Read through the router so the arbitration that OwnerOf already
                    // decides is also enforced — sampling the key directly here left the
                    // thruster firing underneath the reclaim it had just lost the key to.
                    if (NebulaInputRouter.Held(NebulaInputActions.FlightVerticalDown)) input.y -= 1f;
                    return input;
                }
            }

            public bool AnyThrustHeld =>
                Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.A)
                || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Space)
                || NebulaInputRouter.Held(NebulaInputActions.FlightVerticalDown);

            public bool BoostHeld => Input.GetKey(KeyCode.LeftShift);
        }

        private static IShipFlightInput flightInput = new KeyboardFlightInput();

        /// <summary>
        /// Where flight controls are read from. Defaults to the real keyboard and is
        /// never <c>null</c>: assigning <c>null</c> restores the shipping source
        /// rather than leaving a ship that silently reads nothing.
        /// </summary>
        public static IShipFlightInput FlightInput
        {
            get => flightInput;
            set => flightInput = value ?? new KeyboardFlightInput();
        }

        /// <summary>Puts the real keyboard back. Called from test teardown, never from the game.</summary>
        public static void ResetFlightInputToKeyboard() => flightInput = new KeyboardFlightInput();

        /// <summary>
        /// Whether the pointer was already aiming the ship <b>before this frame began</b>.
        /// <para>
        /// The click that captures the pointer used to do two things at once. Left mouse
        /// is <c>combat.fire_laser</c> as well as "capture the game view", and
        /// <see cref="HandleCursor"/> captured on the down edge while
        /// <c>ShipCombat.HandleWeaponFire</c> read the same button held on the same
        /// frame — so the click that gave the player their crosshair also fired the
        /// laser under it. One input, two actions.
        /// </para>
        /// <para>
        /// Answered from the <i>end of the previous frame</i>, which is the only form
        /// of the question that does not depend on execution order. Unity promises no
        /// ordering between <see cref="ShipController"/>'s <c>Update</c> and
        /// <c>ShipCombat</c>'s, so "have I captured yet this frame?" has two different
        /// answers depending on who ran first — and a rule with two answers is not a
        /// rule. At the end of the capture frame this is still false, so no weapon on a
        /// pointer button can fire on it whichever way round the two components ran;
        /// from the next frame on it is true and the button is the weapon's again.
        /// </para>
        /// <para>
        /// It is deliberately not <c>Cursor.lockState</c>. The UI shell saves and
        /// restores that global around every screen, so reading it here would make the
        /// answer depend on a value another system owns and mutates.
        /// </para>
        /// </summary>
        public static bool PointerAimLive => pointerAimLiveAtFrameEnd;

        private static bool pointerAimLiveAtFrameEnd;

        /// <summary>
        /// Drops the pointer-aim latch. Part of the session reset contract: a new
        /// session starts with the pointer free, and a latch carried across would let
        /// the first click of the next session fire a weapon.
        /// </summary>
        public static void ResetPointerAim()
        {
            pointerAimLiveAtFrameEnd = false;
            PointerOverUiSource = null;
        }

        /// <summary>
        /// Where "is the pointer over the interface" comes from. <c>null</c> is the real
        /// EventSystem, which is what ships.
        /// <para>
        /// The same seam, and for the same reason, as
        /// <see cref="NebulaInputRouter.Source"/>: the question cannot be answered
        /// honestly outside a real session with a real mouse. In batch mode there is no
        /// pointer at all, so <c>IsPointerOverGameObject</c> answers from position
        /// (0,0) — where the HUD happens to sit — and every capture is refused for a
        /// reason that has nothing to do with the player. Driving the EventSystem
        /// instead was tried and is worse: switching it off to make the answer false
        /// perturbs the shell that owns it, which is how the first test in a fixture
        /// captured the pointer and the ones after it did not.
        /// </para>
        /// <para>
        /// Only the <i>precondition</i> moves behind the seam. Whether the click then
        /// captures, and whether that same press is kept out of the weapon, stay exactly
        /// where they were — so the rule under test is still the shipped one.
        /// </para>
        /// </summary>
        public static System.Func<bool> PointerOverUiSource { get; set; }

        private static bool PointerIsOverUi()
        {
            if (PointerOverUiSource != null) return PointerOverUiSource();
            UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
            return events != null && events.IsPointerOverGameObject();
        }

        /// <summary>
        /// Whether this ship believes it holds the pointer. Its own state rather than a
        /// read of <c>Cursor.lockState</c>, for the reason given on
        /// <see cref="PointerAimLive"/>.
        /// </summary>
        private bool cursorCaptured;

        /// <summary>
        /// Releases flight's pointer ownership through the same state that
        /// <see cref="HandleCursor"/> uses. UI surfaces which temporarily need the
        /// desktop pointer must call this instead of changing <see cref="Cursor"/>
        /// directly; otherwise the global cursor is free while the ship still believes
        /// it owns it, and the next click can never capture the game view again.
        /// </summary>
        public void ReleasePointerAim()
        {
            cursorCaptured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Presentation seam fired only when collision resolution actually shortens a movement step.</summary>
        public event Action<float> CollisionResolved;
        [Header("Translation")]
        [Tooltip("Commanded local-flight speed in units/second. Adjust with the scroll wheel or the HUD throttle bar.")]
        [SerializeField] private float throttle = 90f;
        [SerializeField] private float minThrottle = 4f;
        // Local top speed (VS-006 §4): 180 at T0/T1 engine tier, 194.4 at T2 —
        // set at runtime via SetLocalMaxThrottle from ShipCruise/ShipModules.
        // Deep-space cruise speed (1,800-4,500 u/s) is a separate multiplier
        // (see CruiseSpeedMultiplier), not part of this local throttle range.
        [SerializeField] private float maxThrottle = 180f;
        [SerializeField] private float boostMultiplier = 4f;
        [Tooltip("How quickly the ship converges on the commanded velocity (inertia feel).")]
        [SerializeField] private float responsiveness = 3.5f;

        [Header("Rotation")]
        [SerializeField] private float lookSensitivity = 2.2f;
        [SerializeField] private float rollSpeedDegrees = 110f;

        [Header("Collision")]
        [Tooltip("Radius of the sphere used to keep the ship out of celestial bodies and asteroids.")]
        [SerializeField] private float collisionRadius = 3.5f;

        [Header("Gravity")]
        [Tooltip("Off for comfortable observation; enable to let the stars and planets pull the ship.")]
        [SerializeField] private bool affectedByGravity = false;

        private Vector3 velocity;
        private Transform orbitAnchor;
        private Vector3 orbitAnchorLast;

        public Vector3 Velocity => velocity;

        /// <summary>True while Orbit Stabilize is matching a planet's motion (free translation off).</summary>
        public bool IsStabilized => orbitAnchor != null;

        /// <summary>
        /// What the ship is currently riding, or null. Read-only: callers that
        /// need to know whether the ship is holding station on a particular
        /// body — rather than merely on something — can ask without being able
        /// to change it.
        /// </summary>
        public Transform StabilizedAnchor => orbitAnchor;

        /// <summary>Lock free translation and ride the anchor's movement (used by the surface build mode).</summary>
        public void StabilizeTo(Transform anchor)
        {
            orbitAnchor = anchor;
            if (anchor != null) orbitAnchorLast = anchor.position;
            velocity = Vector3.zero;
        }

        public void ReleaseStabilize() => orbitAnchor = null;

        /// <summary>Restores the inertial velocity (used by save/load).</summary>
        public void SetVelocity(Vector3 value) => velocity = value;

        /// <summary>1 while any thrust key is held, 0 otherwise (drives the thruster flame).</summary>
        public float ThrustInput { get; private set; }

        /// <summary>
        /// True on the frames boost is actually multiplying speed — which is not the
        /// same as "the boost control is down", because boost is refused at cruise
        /// multipliers above 1 (VS-006 §4).
        /// <para>
        /// Recomputed from <see cref="FlightInput"/> every frame, and writable by
        /// nothing outside this class. That is deliberate: it is the observable the
        /// tutorial's boost step reads, and a flag anything in the assembly could
        /// set would let a test hand the player a step they never performed. The
        /// only way to make it true is to hold the boost control on the flight input
        /// source — which in the shipping game is the keyboard.
        /// </para>
        /// </summary>
        public bool Boosting { get; private set; }

        public float Throttle
        {
            get => throttle;
            set => throttle = Mathf.Clamp(value, minThrottle, maxThrottle);
        }

        public float LocalMaxThrottle => maxThrottle;

        /// <summary>Engine-Navigation tier effect (VS-006 §4): raises/lowers the local-flight speed ceiling, clamping the current throttle down if needed.</summary>
        public void SetLocalMaxThrottle(float value)
        {
            maxThrottle = Mathf.Max(minThrottle, value);
            throttle = Mathf.Clamp(throttle, minThrottle, maxThrottle);
        }

        /// <summary>Multiplies commanded speed on top of throttle*boost — driven by ShipCruise's ramp/cruise/deceleration state. 1 in LocalFlight.</summary>
        public float CruiseSpeedMultiplier { get; set; } = 1f;

        /// <summary>VS-013: Combat Boost's +40% speed for its 3s duration, driven by Nebula.Ship.ShipCombat. 1 outside Boost — a separate layer from the flight-system factors in FlightTestSpeedMath.CommandedSpeed, applied once as an outer multiplier so it can never double-apply with anything else.</summary>
        public float CombatBoostSpeedMultiplier { get; set; } = 1f;

        /// <summary>VS-013.1 §5: 0.75 while ShipCombatState.Damaged, 1 otherwise — driven by ShipCombat, another independent outer multiplier layer (never combined with CombatBoostSpeedMultiplier at the same time in practice, since Boost is itself unavailable while Damaged, but each stays structurally separate regardless).</summary>
        public float DamagedSpeedMultiplier { get; set; } = 1f;

        // --- DT-001: session-local flight test speed multiplier -----------------
        // Test-only, never persisted, never a second speed authority: applied
        // exactly once in HandleTranslation's commanded-speed formula, on top
        // of the existing throttle/boost/cruise result. Forced to 1x outside
        // Editor/Development Build regardless of what was last set.
        private float rawTestSpeedMultiplier = 1f;

        /// <summary>Effective test speed multiplier actually applied to flight this frame — always 1x in a release (non-development, non-Editor) build.</summary>
        public float TestSpeedMultiplier => FlightTestSpeedMath.ResolveEffectiveMultiplier(rawTestSpeedMultiplier, Debug.isDebugBuild);

        /// <summary>Clamped to [1, 100]. The stored raw value only takes effect through <see cref="TestSpeedMultiplier"/>'s release gate.</summary>
        public void SetTestSpeedMultiplier(float value) => rawTestSpeedMultiplier = FlightTestSpeedMath.ClampMultiplier(value);

        /// <summary>Back to 1x — called on session start/restart/load so a leftover test value never survives into a new session.</summary>
        public void ResetTestSpeedMultiplier() => rawTestSpeedMultiplier = 1f;

        /// <summary>
        /// DT-001 test-only seam: drives exactly one segment-checked movement
        /// step through the exact same <see cref="MoveWithCollision"/> path
        /// <see cref="HandleTranslation"/> uses every frame — input-free, for
        /// automated high-test-speed collision regression only. Never a
        /// teleport: the displacement is still resolved against every
        /// celestial body/asteroid sphere before the transform moves.
        /// </summary>
        public void TestApplyDisplacement(Vector3 displacement) => MoveWithCollision(displacement);

        /// <summary>While true, HandleRotation ignores mouse/roll input entirely (Yön Sabitle / Heading Lock).</summary>
        public bool RotationLocked { get; set; }

        /// <summary>Throttle position 0..1 on a logarithmic scale (for the HUD bar).</summary>
        public float GetThrottleFraction()
        {
            return Mathf.InverseLerp(Mathf.Log(minThrottle), Mathf.Log(maxThrottle), Mathf.Log(throttle));
        }

        public void SetThrottleFraction(float fraction)
        {
            throttle = Mathf.Exp(Mathf.Lerp(Mathf.Log(minThrottle), Mathf.Log(maxThrottle), Mathf.Clamp01(fraction)));
        }

        private void Update()
        {
            if (InputGate.Suppressed) return;
            HandleCursor();
            HandleRotation();
            HandleTranslation();
        }

        /// <summary>
        /// Publishes the pointer-aim latch at the end of the frame, so every reader
        /// gets the same answer regardless of the order the Updates ran in. See
        /// <see cref="PointerAimLive"/>.
        /// </summary>
        private void LateUpdate() => pointerAimLiveAtFrameEnd = cursorCaptured;

        private void HandleCursor()
        {
            // Through the router's observation seam rather than Input directly: legacy
            // Input cannot be driven from a batch-mode test, and "the click that
            // captures the pointer must not also fire the laser" is only provable if
            // that click can be pressed by a test the way the player presses it.
            if (NebulaInputRouter.KeyDown(KeyCode.Mouse0) && !cursorCaptured)
            {
                // Don't steal the cursor while the player is using the HUD.
                if (PointerIsOverUi()) return;

                cursorCaptured = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // The shared-intent read, so releasing the cursor follows the catalog row
            // that already describes it (flight.release_cursor) instead of a constant
            // frozen at this call site. Escape stays unarbitrated on purpose: every
            // surface answers it for its own thing, and this surface's own thing is the
            // captured pointer.
            if (NebulaInputRouter.SharedPressed(NebulaInputActions.FlightReleaseCursor))
            {
                ReleasePointerAim();
            }
        }

        private void HandleRotation()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;

            float yaw = Input.GetAxis("Mouse X") * lookSensitivity;
            float pitch = -Input.GetAxis("Mouse Y") * lookSensitivity;

            float roll = 0f;
            if (Input.GetKey(KeyCode.Q)) roll += 1f;
            if (Input.GetKey(KeyCode.E)) roll -= 1f;

            ApplyLook(yaw, pitch, roll);
        }

        /// <summary>
        /// Applies one frame of look/roll. Split out of <see cref="HandleRotation"/>
        /// so a caller that is not the input loop can drive the same rotation the
        /// player's mouse does, without any second copy of the maths.
        /// <para>
        /// Added for the tutorial, which has to observe a real attitude change to
        /// know the player actually looked around. It reads the ship rather than
        /// steering it, but the underlying rotation had no seam at all: it was
        /// computed and consumed inside a private method that reads
        /// <c>Input.GetAxis</c> directly, so nothing outside the input loop — a
        /// test included — could exercise it.
        /// </para>
        /// </summary>
        public void ApplyLook(float yawDegrees, float pitchDegrees, float rollInput)
        {
            if (RotationLocked) return;
            transform.Rotate(pitchDegrees, yawDegrees,
                rollInput * rollSpeedDegrees * Time.deltaTime, Space.Self);
        }

        private void HandleTranslation()
        {
            // Read once, so every decision below is made from the same frame's
            // controls. In the shipping game this is the keyboard and the calls it
            // makes are the ones that used to be written out here.
            IShipFlightInput controls = flightInput;

            // Orbit Stabilize: carry the ship with the planet unless the player
            // commands thrust (which releases the lock). Rotation stays free.
            if (orbitAnchor != null)
            {
                bool thrust = controls.AnyThrustHeld;
                if (!thrust)
                {
                    Vector3 delta = orbitAnchor.position - orbitAnchorLast;
                    orbitAnchorLast = orbitAnchor.position;
                    transform.position += delta;
                    velocity = Vector3.zero;
                    ThrustInput = 0f;
                    return;
                }
                ReleaseStabilize();
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                throttle = Mathf.Clamp(throttle * Mathf.Pow(1.35f, scroll * 10f), minThrottle, maxThrottle);
            }

            Vector3 input = controls.Thrust;

            ThrustInput = Mathf.Clamp01(input.sqrMagnitude);

            // Shift boost never multiplies cruise speed (VS-006 §4): only
            // available at multiplier 1 (pure LocalFlight). The refusal lives here
            // and not in the input source, so a held control that the game turns
            // down still reads as "not boosting" — which is what the tutorial's
            // boost step has to be able to tell apart.
            Boosting = controls.BoostHeld && CruiseSpeedMultiplier <= 1.0001f;
            float speed = FlightTestSpeedMath.CommandedSpeed(throttle, Boosting ? boostMultiplier : 1f, CruiseSpeedMultiplier, TestSpeedMultiplier) * CombatBoostSpeedMultiplier * DamagedSpeedMultiplier;
            Vector3 targetVelocity = input.sqrMagnitude > 0f
                ? transform.TransformDirection(input.normalized) * speed
                : Vector3.zero;

            float blend = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
            velocity = Vector3.Lerp(velocity, targetVelocity, blend);

            if (affectedByGravity && GravitySystem.Instance != null)
            {
                velocity += GravitySystem.Instance.AccelerationAt(transform.position) * Time.deltaTime;
            }

            MoveWithCollision(velocity * Time.deltaTime);
        }

        /// <summary>
        /// Moves the ship, treating every celestial body and asteroid as a
        /// solid sphere. The whole movement segment is tested (not just the
        /// end point), so even at maximum boost the ship cannot tunnel through
        /// anything; on contact it is pushed to the surface and slides along it.
        /// </summary>
        private void MoveWithCollision(Vector3 displacement)
        {
            Vector3 start = transform.position;
            Vector3 intendedEnd = start + displacement;
            Vector3 end = intendedEnd;

            var bodies = GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] == null) continue;
                end = ResolveSphereCollision(start, end, bodies[i].transform.position, bodies[i].Radius + collisionRadius);
            }

            var asteroids = Asteroid.All;
            for (int i = 0; i < asteroids.Count; i++)
            {
                if (asteroids[i] == null) continue;
                end = ResolveSphereCollision(start, end, asteroids[i].transform.position, asteroids[i].Radius + collisionRadius);
            }

            transform.position = end;
            float prevented = Vector3.Distance(intendedEnd, end);
            if (prevented > 0.001f)
                CollisionResolved?.Invoke(Mathf.Max(velocity.magnitude, prevented / Mathf.Max(Time.deltaTime, 0.0001f)));
        }

        private Vector3 ResolveSphereCollision(Vector3 start, Vector3 end, Vector3 center, float minDistance)
        {
            // Closest point to the sphere center on the movement segment.
            Vector3 segment = end - start;
            float segmentLengthSq = segment.sqrMagnitude;
            float t = segmentLengthSq > 1e-8f
                ? Mathf.Clamp01(Vector3.Dot(center - start, segment) / segmentLengthSq)
                : 0f;
            Vector3 closest = start + segment * t;
            Vector3 fromCenter = closest - center;

            if (fromCenter.sqrMagnitude >= minDistance * minDistance) return end;

            Vector3 normal = fromCenter.sqrMagnitude > 1e-6f
                ? fromCenter.normalized
                : (start - center).normalized;

            float into = Vector3.Dot(velocity, normal);
            if (into < 0f) velocity -= normal * into; // keep only the sliding component

            return center + normal * minDistance;
        }
    }
}
