using System.Collections.Generic;
using UnityEngine;
using Nebula.Hangar;

namespace Nebula.Visuals
{
    /// <summary>
    /// Poses one support vehicle's <em>visual child</em> from the mission it is
    /// really running.
    /// <para>
    /// <see cref="VehicleAgent"/> remains the only authority on where the
    /// vehicle is and what it is doing. This reads its phase and its actual
    /// movement, and writes nothing back: no route, no speed, no ETA, no
    /// docking arrival, no transfer, no repair, no scan result, and no command
    /// capacity. The gameplay root, its collider and its rotation are never
    /// touched, so a hardpoint still points where the agent aimed it.
    /// </para>
    /// <para>
    /// Only the whole visual child moves, because every vehicle prefab is a
    /// single mesh — there is no cargo door, tool arm or sensor dish to deploy.
    /// Those are recorded as ANI-GAP rather than mimicked by shaking the hull.
    /// </para>
    /// </summary>
    public class VehicleAnimationPresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        /// <summary>Presenters spawned by gameplay code with no route to the session context wait here.</summary>
        private static readonly List<VehicleAnimationPresenter> AwaitingAdoption = new List<VehicleAnimationPresenter>();

        internal static void AdoptPending(NebulaAnimationDirector director)
        {
            if (director == null) return;

            for (int i = 0; i < AwaitingAdoption.Count; i++)
            {
                VehicleAnimationPresenter presenter = AwaitingAdoption[i];
                if (presenter != null) presenter.AttachTo(director);
            }
            AwaitingAdoption.Clear();
        }

        public static void ResetPending() => AwaitingAdoption.Clear();

        /// <summary>Presenters genuinely still waiting; destroyed ones are not counted.</summary>
        public static int PendingCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < AwaitingAdoption.Count; i++)
                    if (AwaitingAdoption[i] != null) live++;
                return live;
            }
        }

        private VehicleAgent agent;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;

        private PoseAnchor visual;
        private GameObject cachedVisual;
        private Vector3 previousPosition;
        private bool hasPreviousPosition;

        private float bankDegrees;
        private float pitchDegrees;

        /// <summary>The state last resolved from the vehicle's real mission. Test-facing.</summary>
        public VehicleAnimationState CurrentState { get; private set; } = VehicleAnimationState.Docked;

        /// <summary>Current visual bank in degrees. Test-facing.</summary>
        public float BankDegrees => bankDegrees;

        public VehicleClass VehicleClass => agent != null && agent.Def != null ? agent.Def.Class : VehicleClass.Scout;

        public bool HasVisual => visual.IsValid;

        /// <summary>The transform this presenter poses. Test-facing; nothing writes through it.</summary>
        public Transform VisualTransform => visual.Target;

        /// <summary>True while the model sits exactly where the artist left it.</summary>
        public bool IsAtBasePose => !visual.IsValid || visual.IsAtBasePose();

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(VehicleAgent vehicleAgent, NebulaAnimationDirector animationDirector)
        {
            agent = vehicleAgent;
            RebindVisual();

            if (animationDirector != null) AttachTo(animationDirector);
            else if (!AwaitingAdoption.Contains(this)) AwaitingAdoption.Add(this);
        }

        private void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(director, animationDirector)) return;

            registration.Release();
            director = animationDirector;
            registration = director.Register(this);
        }

        /// <summary>
        /// Captures the visual child's authored pose. Called again when the
        /// vehicle's model is rebuilt — a Mk change swaps the whole prefab and
        /// the old anchor would point at a destroyed transform.
        /// </summary>
        public void RebindVisual()
        {
            visual.Reset();

            Transform found = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null) continue;
                if (child.GetComponentInChildren<Renderer>(true) == null) continue;
                found = child;
                break;
            }

            cachedVisual = found != null ? found.gameObject : null;
            visual = PoseAnchor.Capture(found);

            bankDegrees = 0f;
            pitchDegrees = 0f;
            hasPreviousPosition = false;
        }

        public void VisualTick(float deltaSeconds)
        {
            // A Mk change or a respawn rebuilds the model under us.
            Transform liveVisual = FindVisualChild();
            if (!ReferenceEquals(liveVisual != null ? liveVisual.gameObject : null, cachedVisual)) RebindVisual();

            CurrentState = ResolveState();

            // Parked in the hangar the object is inactive; the state above still
            // reads correctly, but there is nothing on screen to pose.
            if (!isActiveAndEnabled) return;

            if (!visual.IsValid) return; // no model yet: a safe no-op

            if (AnimationAccessibility.MotionOff)
            {
                // The agent's own flight continues; only the decorative child
                // motion stops, so the vehicle still visibly goes where it goes.
                visual.Reset();
                return;
            }

            float amplitude = VehicleAnimationProfiles.StateAmplitude(CurrentState)
                              * AnimationAccessibility.MotionScale;
            if (director != null && !director.Profile.SecondaryMotion) amplitude *= 0.35f;

            UpdateFlightPose(deltaSeconds, amplitude);

            visual.Apply(Vector3.zero, Quaternion.Euler(pitchDegrees, 0f, bankDegrees));
        }

        private Transform FindVisualChild()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.GetComponentInChildren<Renderer>(true) != null) return child;
            }
            return null;
        }

        /// <summary>
        /// Leans the model into the turn the agent is really flying. Derived
        /// from the root's own frame-to-frame movement: read-only, and the
        /// offset is written to the child, never added onto the agent's
        /// rotation.
        /// </summary>
        private void UpdateFlightPose(float deltaSeconds, float amplitude)
        {
            Vector3 position = transform.position;
            Vector3 velocity = Vector3.zero;

            if (hasPreviousPosition && deltaSeconds > 0f)
                velocity = (position - previousPosition) / deltaSeconds;

            previousPosition = position;
            hasPreviousPosition = true;

            (float bankScale, float pitchScale) = VehicleAnimationProfiles.FlightCharacter(VehicleClass);

            float targetBank = 0f;
            float targetPitch = 0f;

            if (VehicleAnimationProfiles.IsFlying(CurrentState) && velocity.sqrMagnitude > 1e-4f)
            {
                // How far the travel direction sits off the hull's own forward
                // decides the lean; how much of it is vertical decides the pitch.
                Vector3 local = transform.InverseTransformDirection(velocity.normalized);
                targetBank = Mathf.Clamp(-local.x * VehicleAnimationProfiles.MaxBankDegrees * bankScale * amplitude,
                    -VehicleAnimationProfiles.MaxBankDegrees, VehicleAnimationProfiles.MaxBankDegrees);
                targetPitch = Mathf.Clamp(-local.y * VehicleAnimationProfiles.MaxPitchDegrees * pitchScale * amplitude,
                    -VehicleAnimationProfiles.MaxPitchDegrees, VehicleAnimationProfiles.MaxPitchDegrees);
            }

            float blend = 1f - Mathf.Exp(-VehicleAnimationProfiles.PoseDamping * deltaSeconds);
            bankDegrees = Mathf.Lerp(bankDegrees, targetBank, blend);
            pitchDegrees = Mathf.Lerp(pitchDegrees, targetPitch, blend);
        }

        /// <summary>
        /// Reads the vehicle's real condition and mission. The order below is
        /// the game's own precedence: a dead vehicle is dead whatever it was
        /// doing, and the four mission kinds are already mutually exclusive —
        /// this only reflects that, it does not enforce or change it.
        /// </summary>
        private VehicleAnimationState ResolveState()
        {
            if (agent == null || agent.Def == null) return VehicleAnimationState.Docked;

            if (agent.DurabilityRef != null && agent.DurabilityRef.Disabled)
                return agent.Stranded ? VehicleAnimationState.Recovery : VehicleAnimationState.Disabled;

            if (agent.ResupplyTask != null)
                return VehicleAnimationProfiles.FromResupplyPhase(agent.ResupplyTask.Phase);

            if (agent.EscortState != null)
                return VehicleAnimationProfiles.FromEscortSubState(agent.EscortState.SubState);

            // An Engineer on a field job, or a Collector on a route, is working
            // wherever its own mission phase says it is.
            if (agent.EngineerTask != null || agent.Route != null)
                return VehicleAnimationProfiles.FromMissionPhase(
                    agent.Mission != null ? agent.Mission.Phase : VehiclePhase.OnStation);

            return VehicleAnimationProfiles.FromMissionPhase(
                agent.Mission != null ? agent.Mission.Phase : VehiclePhase.InHangar);
        }

        public void ResetToBasePose()
        {
            bankDegrees = 0f;
            pitchDegrees = 0f;
            hasPreviousPosition = false;
            visual.Reset();
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            ResetToBasePose();
            AwaitingAdoption.Remove(this);
            agent = null;
            director = null;
        }
    }
}
