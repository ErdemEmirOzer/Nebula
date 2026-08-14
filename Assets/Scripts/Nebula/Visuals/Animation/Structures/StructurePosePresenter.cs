using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Visuals
{
    /// <summary>
    /// Shared machinery for anything built on a planet that has to be posed
    /// from its own lifecycle: a drill, a surface structure, the Logistics
    /// Center.
    /// <para>
    /// Poses a <em>visual child</em> only. The gameplay root keeps its
    /// position, its collider and its surface slot, so a structure's world
    /// placement, its defence aim and every distance the simulation measures
    /// are exactly what they would be with animation absent.
    /// </para>
    /// <para>
    /// Subclasses answer three questions — what state is this in, how far
    /// through is it, and how hard is it working — and never touch a transform
    /// themselves.
    /// </para>
    /// </summary>
    public abstract class StructurePosePresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        /// <summary>
        /// Presenters built by gameplay code that has no route to the session
        /// context — a drill spawned mid-flight, a structure restored from a
        /// save — wait here until the director adopts them.
        /// <para>
        /// This is a dispatch queue, not a service locator: nothing reads it to
        /// find the director, and the director remains the only thing that can
        /// register anything. It is the same shape the lighting presenters
        /// already use to receive a quality change.
        /// </para>
        /// </summary>
        private static readonly System.Collections.Generic.List<StructurePosePresenter> AwaitingAdoption =
            new System.Collections.Generic.List<StructurePosePresenter>();

        /// <summary>Drains the queue into a director. Called by the director itself, at the start of its tick.</summary>
        internal static void AdoptPending(NebulaAnimationDirector director)
        {
            if (director == null) return;

            for (int i = 0; i < AwaitingAdoption.Count; i++)
            {
                StructurePosePresenter presenter = AwaitingAdoption[i];
                if (presenter == null) continue;
                presenter.AttachTo(director);
            }
            AwaitingAdoption.Clear();
        }

        /// <summary>Clears the queue at session start, so a new campaign never adopts the previous one's leftovers.</summary>
        public static void ResetPending() => AwaitingAdoption.Clear();

        /// <summary>How many presenters are still waiting. Test-facing.</summary>
        public static int PendingCount => AwaitingAdoption.Count;

        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private PoseAnchor visual;
        private float workPhase;
        private float objectHeight = 1f;

        /// <summary>The lifecycle state last resolved. Test-facing.</summary>
        public DrillLifecycle CurrentState { get; private set; } = DrillLifecycle.Planned;

        /// <summary>Real build/dismantle progress last read, 0–1. Test-facing.</summary>
        public float CurrentProgress { get; private set; }

        /// <summary>True once a visual child has been found and captured.</summary>
        public bool HasVisual => visual.IsValid;

        public bool IsAlive => this != null && gameObject != null;

        /// <summary>The lifecycle this thing is really in. Read-only.</summary>
        protected abstract DrillLifecycle ResolveState();

        /// <summary>How far through construction or dismantling it really is, 0–1.</summary>
        protected abstract float ResolveProgress();

        /// <summary>Rate and amplitude of the working motion, or zero when it should be still.</summary>
        protected abstract (float rateHz, float amplitude) ResolveWork();

        /// <summary>
        /// Set when this model carries real mechanisms, so the whole structure
        /// stops bobbing and the parts do the work instead. Construction rise
        /// and dismantle retraction still move the whole root, because those
        /// really are whole-structure states.
        /// </summary>
        public bool SuppressWholeRootWorkMotion { get; set; }

        /// <summary>
        /// Binds to the visual child and either registers immediately or waits
        /// to be adopted. Called from the single production Setup seam each
        /// type already has, so Create, Restore and every other entry point are
        /// covered once rather than each growing their own presenter.
        /// </summary>
        protected void BindAndRegister(NebulaAnimationDirector animationDirector)
        {
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
        /// Captures the visual child's authored pose. The first child that
        /// carries a renderer is the model; the gameplay root itself is never
        /// a candidate.
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

            visual = PoseAnchor.Capture(found);

            if (found != null)
            {
                var renderer = found.GetComponentInChildren<Renderer>(true);
                objectHeight = renderer != null ? Mathf.Max(0.01f, renderer.bounds.size.y) : 1f;
            }

            workPhase = 0f;
        }

        public void VisualTick(float deltaSeconds)
        {
            CurrentState = ResolveState();
            CurrentProgress = Mathf.Clamp01(ResolveProgress());

            // No visual child: a safe no-op. The structure still works.
            if (!visual.IsValid) return;

            (float sinkFraction, float scale, float tiltDegrees) =
                StructureAnimationProfiles.Lifecycle(CurrentState, CurrentProgress);

            float motion = AnimationAccessibility.MotionScale;
            Vector3 offset = Vector3.down * (sinkFraction * objectHeight);

            // The working motion is decorative; the lifecycle pose is not, so
            // only the former answers Reduced Motion and Motion Off.
            // ANI-05.1: a model with real authored mechanisms works through those
            // parts, not by bobbing the whole building. The earlier audit read
            // the prefab wrapper and wrongly concluded there was nothing to
            // drive; whole-root motion is now construction and dismantle only.
            if (motion > 0f && !SuppressWholeRootWorkMotion && StructureAnimationProfiles.IsWorking(CurrentState))
            {
                (float rateHz, float amplitude) = ResolveWork();
                if (rateHz > 0f && amplitude > 0f)
                {
                    workPhase += deltaSeconds * rateHz;
                    float clamped = Mathf.Min(amplitude, StructureAnimationProfiles.MaxWorkAmplitude);
                    offset += Vector3.up * (Mathf.Sin(workPhase * Mathf.PI * 2f) * clamped * motion);
                }
            }

            var rotation = Quaternion.Euler(tiltDegrees * motion, 0f, 0f);
            visual.Apply(offset, rotation, scale);
        }

        public void ResetToBasePose()
        {
            workPhase = 0f;
            visual.Reset();
        }

        private void OnDisable() => ResetToBasePose();

        protected virtual void OnDestroy()
        {
            registration.Release();
            ResetToBasePose();
            director = null;
        }
    }
}
