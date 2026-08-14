using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Visuals
{
    /// <summary>
    /// Marks a planet with the control state it really has.
    /// <para>
    /// <see cref="PlanetDominance"/> owns the state, both timers, the abandoned
    /// flag and every consequence of them. This leases at most one pooled
    /// sprite per planet and moves it; it adds no light, no body and no
    /// collider, creates no second state, and awards nothing. An unowned world
    /// is deliberately unmarked.
    /// </para>
    /// </summary>
    public sealed class DominanceMarkerPresenter : MonoBehaviour, IAdoptableParticipant
    {
        private System.Func<PlanetDominance> readDominance;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private Camera billboardCamera;

        private SupportOperationLease lease = SupportOperationLease.None;
        private float radius = 40f;
        private float age;

        /// <summary>The state last read from the planet's own dominance. Test-facing.</summary>
        public DominanceState CurrentState { get; private set; } = DominanceState.Unowned;

        /// <summary>True while a marker is actually leased. Test-facing.</summary>
        public bool ShowingMarker => lease.IsValid;

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(System.Func<PlanetDominance> dominance)
        {
            readDominance = dominance;

            var renderer = GetComponentInChildren<Renderer>(true);
            radius = renderer != null ? Mathf.Max(10f, renderer.bounds.extents.magnitude) : 40f;

            CombatPresenterAdoption.Enqueue(this);
        }

        public void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(director, animationDirector)) return;
            registration.Release();
            director = animationDirector;
            registration = director.Register(this);
        }

        public void VisualTick(float deltaSeconds)
        {
            PlanetDominance dominance = readDominance != null ? readDominance() : null;
            if (dominance == null) { Release(); return; }

            CurrentState = dominance.State;

            bool wanted = PoiDominanceAnimationProfiles.HasMarker(CurrentState, dominance.Abandoned)
                          && !AnimationAccessibility.MotionOff
                          && isActiveAndEnabled;
            if (!wanted) { Release(); return; }
            if (director == null || director.SupportOperations == null) return;

            NebulaCommonVfx sprite = PoiDominanceAnimationProfiles
                .DominanceVfx(CurrentState, dominance.Abandoned).Value;

            if (lease.IsValid && lease.Vfx != sprite) Release();

            if (!lease.IsValid)
            {
                // A full pool costs this planet its marker and nothing else.
                if (!director.SupportOperations.TryLease(sprite, out SupportOperationLease claimed, out _)) return;
                lease = claimed;
            }

            if (!director.SupportOperations.TryGetQuad(lease, out Transform quad) || quad == null)
            {
                lease = SupportOperationLease.None;
                return;
            }

            age += deltaSeconds;

            // Contested deliberately holds steady rather than pulsing: a
            // blinking warning is exactly what Safe Mode forbids.
            float motion = PoiDominanceAnimationProfiles.MarkerMotion(CurrentState)
                           * AnimationAccessibility.MotionScale;
            float orbit = Mathf.Repeat(age * 8f * motion, 360f);

            quad.position = transform.position
                            + Quaternion.AngleAxis(orbit, Vector3.up) * (Vector3.forward * (radius * 1.6f));
            quad.localScale = Vector3.one * (radius * 0.5f);

            if (billboardCamera == null) billboardCamera = Camera.main;
            if (billboardCamera != null)
                quad.rotation = Quaternion.LookRotation(quad.position - billboardCamera.transform.position, Vector3.up);
        }

        private void Release()
        {
            if (!lease.IsValid) return;
            director?.SupportOperations?.Release(lease);
            lease = SupportOperationLease.None;
        }

        public void ResetToBasePose() => Release();

        private void OnDisable() => Release();

        private void OnDestroy()
        {
            registration.Release();
            CombatPresenterAdoption.Remove(this);
            Release();
            readDominance = null;
            director = null;
        }
    }
}
