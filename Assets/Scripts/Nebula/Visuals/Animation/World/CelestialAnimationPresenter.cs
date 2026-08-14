using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Sectors;

namespace Nebula.Visuals
{
    /// <summary>
    /// Turns a world on its own axis, without ever moving it.
    /// <para>
    /// The orbit, the position, the radius, the hazard band and every surface
    /// slot on a planet belong to the sector system and to
    /// <see cref="CelestialBody"/>. This poses the visual child and, where a
    /// surface really supports it, scrolls that surface's texture offset — and
    /// nothing else. The angle is recomputed from age rather than accumulated,
    /// so a Floating Origin shift, a demote and a reload all land on the same
    /// pose with no catch-up and nothing saved.
    /// </para>
    /// </summary>
    public sealed class CelestialAnimationPresenter : MonoBehaviour, IAdoptableParticipant
    {
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;

        private PoseAnchor visual;
        private PoseAnchor secondary;
        private Renderer[] surfaceRenderers;
        private MaterialPropertyBlock scratch;

        private CelestialMotion motion;
        private Vector3 spinAxis = Vector3.up;
        private float environmentAmplitude = 1f;
        private float age;
        private bool photosensitive;

        /// <summary>The rates this body is being animated at. Test-facing.</summary>
        public CelestialMotion Motion => motion;

        /// <summary>True when a visual child was found to pose. Test-facing.</summary>
        public bool HasVisual => visual.IsValid;

        /// <summary>How many renderers this body scrolls. Zero for families whose surface does not flow.</summary>
        public int ScrollingRendererCount => surfaceRenderers != null ? surfaceRenderers.Length : 0;

        /// <summary>The angle currently applied, in degrees. Test-facing.</summary>
        public float CurrentSpinDegrees { get; private set; }

        public bool IsAlive => this != null && gameObject != null;

        /// <summary>
        /// <paramref name="bodyId"/> only ever seeds deterministic motion; it is
        /// never used to look anything up or to change anything.
        /// </summary>
        public void Configure(CelestialBodyType type, StarClass starClass, PlanetArchetype archetype,
            EnvironmentClass environment, string bodyId)
        {
            motion = CelestialAnimationProfiles.ForBodyType(type, starClass, archetype, bodyId);
            environmentAmplitude = CelestialAnimationProfiles.EnvironmentAmplitude(environment);
            spinAxis = type == CelestialBodyType.Asteroid
                ? CelestialAnimationProfiles.AsteroidAxis(bodyId)
                : Vector3.up;

            // A black hole's disk is the one place fast contrast could appear.
            photosensitive = type == CelestialBodyType.BlackHole;

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
        /// Captures the visual child and, when this family's surface really
        /// flows, the renderers it flows on. A body with no visual child is a
        /// safe no-op rather than a reason to pose the gameplay root.
        /// </summary>
        private void Bind()
        {
            visual.Reset();
            secondary.Reset();
            surfaceRenderers = null;

            Transform found = null;
            Transform second = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null || child.GetComponentInChildren<Renderer>(true) == null) continue;
                if (found == null) found = child;
                else if (second == null) second = child;
            }

            visual = PoseAnchor.Capture(found);
            secondary = PoseAnchor.Capture(second);

            if (motion.SurfaceFlowPerSecond > 0f && found != null)
            {
                surfaceRenderers = found.GetComponentsInChildren<Renderer>(true);
                scratch = new MaterialPropertyBlock();
            }
        }

        public void VisualTick(float deltaSeconds)
        {
            if (!isActiveAndEnabled) return;
            if (!visual.IsValid) { Bind(); if (!visual.IsValid) return; }

            age += deltaSeconds;

            if (AnimationAccessibility.MotionOff)
            {
                // Authored pose, and the world keeps orbiting regardless — its
                // orbit was never this object's to stop.
                visual.Reset();
                secondary.Reset();
                ResetSurfaces();
                CurrentSpinDegrees = 0f;
                return;
            }

            float damp = environmentAmplitude;
            if (photosensitive) damp *= CelestialAnimationProfiles.PhotosensitivityScale;
            if (director != null && !director.Profile.SecondaryMotion) damp *= 0.5f;

            CurrentSpinDegrees = CelestialAnimationProfiles.SpinDegrees(
                motion.SpinDegreesPerSecond * damp, age);

            if (motion.SpinDegreesPerSecond > 0f)
                visual.Apply(Vector3.zero, Quaternion.AngleAxis(CurrentSpinDegrees, spinAxis));

            if (secondary.IsValid && motion.SecondaryDegreesPerSecond > 0f)
            {
                float secondaryDegrees = CelestialAnimationProfiles.SpinDegrees(
                    motion.SecondaryDegreesPerSecond * damp, age);
                secondary.Apply(Vector3.zero, Quaternion.AngleAxis(secondaryDegrees, Vector3.up));
            }

            if (surfaceRenderers != null && motion.SurfaceFlowPerSecond > 0f)
            {
                float offset = Mathf.Repeat(
                    motion.SurfaceFlowPerSecond * age * AnimationAccessibility.MotionScale * damp, 1f);

                // Read-modify-write: lighting's smoothness, metallic and
                // emission on these same renderers survive untouched.
                for (int i = 0; i < surfaceRenderers.Length; i++)
                    MaterialPropertyChannels.ScrollSurface(surfaceRenderers[i], scratch, new Vector2(offset, 0f));
            }
        }

        private void ResetSurfaces()
        {
            if (surfaceRenderers == null || scratch == null) return;
            for (int i = 0; i < surfaceRenderers.Length; i++)
                MaterialPropertyChannels.ResetSurface(surfaceRenderers[i], scratch);
        }

        public void ResetToBasePose()
        {
            visual.Reset();
            secondary.Reset();
            ResetSurfaces();
            CurrentSpinDegrees = 0f;
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            CombatPresenterAdoption.Remove(this);
            ResetToBasePose();
            surfaceRenderers = null;
            scratch = null;
            director = null;
        }
    }
}
