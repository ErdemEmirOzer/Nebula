using UnityEngine;
using Nebula.Poi;

namespace Nebula.Visuals
{
    /// <summary>
    /// Turns a point of interest's own visual, at a rate its own knowledge
    /// level allows.
    /// <para>
    /// <see cref="PoiObject"/> owns the state, the timers, the salvage, the
    /// facility choice and which model is shown; this reads them. The
    /// information gate is the point of the class: while the game has only told
    /// the player "there is something here", all four types move identically,
    /// so no one can tell an anomaly from a wreck by watching it.
    /// </para>
    /// </summary>
    public sealed class PoiAnimationPresenter : MonoBehaviour, IAdoptableParticipant
    {
        private PoiObject poi;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;

        private PoseAnchor visual;
        private GameObject boundVisual;
        private float age;

        /// <summary>What is being shown, given the real knowledge level. Test-facing.</summary>
        public PoiVisualCharacter Character { get; private set; } = PoiVisualCharacter.NeutralContact;

        /// <summary>The angle currently applied, in degrees. Test-facing.</summary>
        public float CurrentSpinDegrees { get; private set; }

        /// <summary>True when a visual was found to turn. Test-facing.</summary>
        public bool HasVisual => visual.IsValid;

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(PoiObject owner)
        {
            poi = owner;
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
        /// Captures whichever model the POI is currently showing. It swaps its
        /// own visual as knowledge grows, so this is called again whenever that
        /// happens and the old anchor is dropped with it.
        /// </summary>
        private void Bind()
        {
            visual.Reset();

            Transform found = FindVisualChild();
            boundVisual = found != null ? found.gameObject : null;
            visual = PoseAnchor.Capture(found);
            CurrentSpinDegrees = 0f;
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
            if (poi == null || poi.State == null) { visual.Reset(); return; }

            // A new knowledge level can swap the model underneath us.
            Transform live = FindVisualChild();
            if (!ReferenceEquals(live != null ? live.gameObject : null, boundVisual)) Bind();

            Character = PoiDominanceAnimationProfiles.Resolve(poi.State.Level, poi.State.PauseReason);

            if (!visual.IsValid) return;

            if (!PoiDominanceAnimationProfiles.IsAnimated(Character) || AnimationAccessibility.MotionOff)
            {
                // Resolved and paused both stop dead. A resolved POI holds a low
                // permanent pose and never replays anything.
                visual.Reset();
                CurrentSpinDegrees = 0f;
                return;
            }

            age += deltaSeconds;

            float rate = PoiDominanceAnimationProfiles.SpinDegreesPerSecond(poi.Manifest.Type, Character);
            if (poi.Manifest.Type == PoiType.AbandonedFacility)
                rate *= PoiDominanceAnimationProfiles.FacilityCadence(poi.State.Choice);

            // A pure function of age: a demote, a reload and a restart all land
            // on the same angle, and nothing has to be saved.
            CurrentSpinDegrees = Mathf.Repeat(rate * age * AnimationAccessibility.MotionScale, 360f);
            visual.Apply(Vector3.zero, Quaternion.AngleAxis(CurrentSpinDegrees, Vector3.up));
        }

        public void ResetToBasePose()
        {
            visual.Reset();
            CurrentSpinDegrees = 0f;
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            CombatPresenterAdoption.Remove(this);
            ResetToBasePose();
            poi = null;
            director = null;
        }
    }
}
