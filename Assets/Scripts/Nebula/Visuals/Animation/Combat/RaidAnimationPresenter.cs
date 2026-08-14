using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Visuals
{
    /// <summary>
    /// Shows a raid warning, and nothing else.
    /// <para>
    /// The raid scheduler owns every phase and every enemy. This reads
    /// <see cref="ThreatPressureState.Phase"/> and draws one pooled warning
    /// while the phase is <see cref="RaidPhase.Warning"/> — the one moment the
    /// simulation has already told the player something is coming. A scheduled
    /// raid draws nothing at all, so no effect can leak that an attack is due.
    /// Spawning draws nothing either: the enemies fly in from their real spawn
    /// positions under their own agents, and no warp or teleport is invented
    /// for them.
    /// </para>
    /// </summary>
    public sealed class RaidAnimationPresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        private CombatEncounterDirector encounters;
        private NebulaAnimationDirector director;
        private Transform warningTarget;
        private Camera billboardCamera;
        private AnimationRegistration registration;

        private SupportOperationLease warningLease = SupportOperationLease.None;
        private RaidPhase lastPhase = RaidPhase.None;

        /// <summary>The phase last read from the real scheduler. Test-facing.</summary>
        public RaidPhase CurrentPhase => lastPhase;

        /// <summary>True while a warning is actually on screen. Test-facing.</summary>
        public bool ShowingWarning => warningLease.IsValid;

        public bool IsAlive => this != null && gameObject != null;

        /// <summary>
        /// <paramref name="warningAnchor"/> is where a warning belongs — the
        /// threatened planet or the Command Center. A session with neither
        /// falls back to the encounter director's own transform.
        /// </summary>
        public void Initialize(CombatEncounterDirector encounterDirector, NebulaAnimationDirector animationDirector,
            Transform warningAnchor)
        {
            encounters = encounterDirector;
            director = animationDirector;
            warningTarget = warningAnchor;

            if (director != null) registration = director.Register(this);
        }

        public void VisualTick(float deltaSeconds)
        {
            if (encounters == null || encounters.Pressure == null) { Release(); return; }

            RaidPhase phase = encounters.Pressure.Phase;
            lastPhase = phase;

            // Anything that is not an active warning hands the lease back —
            // which is also what a load lands on, so no past warning replays.
            if (phase != RaidPhase.Warning || AnimationAccessibility.MotionOff) { Release(); return; }
            if (director == null || director.SupportOperations == null) return;

            NebulaCommonVfx? sprite = CombatMechanicsProfiles.RaidVfx(phase);
            if (!sprite.HasValue) { Release(); return; }

            if (!warningLease.IsValid)
            {
                if (!director.SupportOperations.TryLease(sprite.Value, out SupportOperationLease lease, out _)) return;
                warningLease = lease;
            }

            if (!director.SupportOperations.TryGetQuad(warningLease, out Transform quad) || quad == null)
            {
                warningLease = SupportOperationLease.None;
                return;
            }

            // The warning belongs over the planet the scheduler really named.
            Vector3 at = warningTarget != null ? warningTarget.position : transform.position;
            string worldId = encounters.Pressure.TargetPlanetWorldId;
            if (!string.IsNullOrEmpty(worldId))
            {
                PlanetIdentity threatened = ExtractionState.FindPlanet(worldId);
                if (threatened != null) at = threatened.transform.position;
            }
            quad.position = at;
            quad.localScale = Vector3.one * 240f;

            if (billboardCamera == null) billboardCamera = Camera.main;
            if (billboardCamera != null)
                quad.rotation = Quaternion.LookRotation(quad.position - billboardCamera.transform.position, Vector3.up);
        }

        private void Release()
        {
            if (!warningLease.IsValid) return;
            director?.SupportOperations?.Release(warningLease);
            warningLease = SupportOperationLease.None;
        }

        public void ResetToBasePose() => Release();

        private void OnDisable() => Release();

        private void OnDestroy()
        {
            registration.Release();
            Release();
            encounters = null;
            director = null;
            warningTarget = null;
        }
    }
}
