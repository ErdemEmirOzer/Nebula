using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;

namespace Nebula.Visuals
{
    /// <summary>
    /// Works the moving parts a projectile's own model carries while it is in
    /// flight: the plasma core, the EM field gimbal and its guidance fins, the
    /// siege round's interception spindle and breaching jaws.
    /// <para>
    /// <see cref="ProjectileState"/> remains the only authority on where the
    /// round is, how fast it is going and how long it has left. This writes to
    /// nodes inside the visual child and nowhere else — the gameplay root, its
    /// position, its velocity, its lifetime and its collider are untouched, so
    /// a round hits exactly what it would have hit with the animation off.
    /// </para>
    /// </summary>
    public sealed class ProjectileAnimationPresenter : MonoBehaviour, IAdoptableParticipant
    {
        /// <summary>How fast a live core turns over, in degrees per second.</summary>
        private const float CoreSpinDegreesPerSecond = 220f;

        /// <summary>How far the fins or jaws swing out once the round is under way.</summary>
        private const float SurfaceDeployDegrees = 14f;

        private ProjectileObject projectile;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;

        private PoseAnchor core;
        private readonly List<PoseAnchor> surfaces = new List<PoseAnchor>();

        private float age;
        private float coreAngle;
        private float deploy01;

        /// <summary>Where the round is in its own life, as last read. Test-facing.</summary>
        public ProjectileVisualPhase CurrentPhase { get; private set; } = ProjectileVisualPhase.Launch;

        /// <summary>True when this round's model exposed a real core node. Test-facing.</summary>
        public bool HasCore => core.IsValid;

        /// <summary>How many real fins or jaws are being worked. Test-facing.</summary>
        public int SurfaceCount => surfaces.Count;

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(ProjectileObject owner)
        {
            projectile = owner;
            Bind();
            CombatPresenterAdoption.Enqueue(this);
        }

        public void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(director, animationDirector)) return;
            registration.Release();
            director = animationDirector;
            registration = director.Register(this);
        }

        /// <summary>
        /// Finds this kind's real nodes through the adapter, so a model authored
        /// before the naming convention is driven by the names it actually has.
        /// </summary>
        private void Bind()
        {
            core.Reset();
            for (int i = 0; i < surfaces.Count; i++) surfaces[i].Reset();
            surfaces.Clear();

            Transform visual = FindVisualChild();
            if (visual == null || projectile == null || projectile.State == null) return;

            CombatMechanicsProfiles.ProjectileNodes nodes = CombatMechanicsProfiles.NodesFor(projectile.State.Kind);

            Transform coreNode = NebulaVisualResolver.FindAnchor(visual, nodes.Core);
            core = PoseAnchor.Capture(coreNode);

            for (int i = 0; i < nodes.Fins.Length; i++)
            {
                Transform surface = NebulaVisualResolver.FindAnchor(visual, nodes.Fins[i]);
                if (surface != null) surfaces.Add(PoseAnchor.Capture(surface));
            }
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

        public void VisualTick(float deltaSeconds)
        {
            if (projectile == null || projectile.State == null) { ResetToBasePose(); return; }

            // A restored round arrives with time already on it; its own state
            // says how much, so nothing here replays the flight it already made.
            if (!core.IsValid && surfaces.Count == 0) Bind();

            age += deltaSeconds;

            ProjectileState state = projectile.State;
            CurrentPhase = CombatMechanicsProfiles.PhaseOf(age, state.RemainingLifetimeSeconds,
                resolved: state.Expired, hit: false, intercepted: state.InterceptedDown);

            if (!CombatMechanicsProfiles.IsLive(CurrentPhase) || AnimationAccessibility.MotionOff)
            {
                // Resolved, intercepted or motion off: the model settles.
                ResetPose();
                return;
            }

            float scale = director != null
                ? CombatMechanicsProfiles.SecondaryScale(director.Profile)
                : AnimationAccessibility.MotionScale;

            // The core turns over; the surfaces swing out once the round is
            // clear of the muzzle and stay there for the rest of the flight.
            coreAngle += CoreSpinDegreesPerSecond * deltaSeconds * scale;
            if (coreAngle > 360f) coreAngle -= 360f;
            deploy01 = Mathf.MoveTowards(deploy01, CurrentPhase == ProjectileVisualPhase.Flight ? 1f : 0f,
                deltaSeconds * 4f);

            if (core.IsValid) core.Apply(Vector3.zero, Quaternion.Euler(0f, 0f, coreAngle));

            float swing = SurfaceDeployDegrees * deploy01 * scale;
            for (int i = 0; i < surfaces.Count; i++)
            {
                float sign = (i % 2 == 0) ? 1f : -1f;
                surfaces[i].Apply(Vector3.zero, Quaternion.Euler(swing * sign, 0f, 0f));
            }
        }

        private void ResetPose()
        {
            core.Reset();
            for (int i = 0; i < surfaces.Count; i++) surfaces[i].Reset();
        }

        public void ResetToBasePose()
        {
            ResetPose();
            coreAngle = 0f;
            deploy01 = 0f;
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            CombatPresenterAdoption.Remove(this);
            ResetToBasePose();
            surfaces.Clear();
            projectile = null;
            director = null;
        }
    }
}
