using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;

namespace Nebula.Visuals
{
    /// <summary>
    /// Poses the Logistics Center from its own runtime and a fill reading it is
    /// given rather than one it goes looking for — this is never a second
    /// capacity or transfer authority.
    /// <para>
    /// The dock doors, arms and rings this station's states are really about
    /// are not in the model, so what is left is how busy the whole station
    /// reads. A Collector's flight in and its docking belong to ANI-03.
    /// </para>
    /// </summary>
    public class LogisticsAnimationPresenter : StructurePosePresenter
    {
        private LogisticsCenterRuntime runtime;
        private System.Func<float> fillFraction;
        private float buildTotalSeconds;

        public LogisticsCenterState State => runtime != null ? runtime.State : LogisticsCenterState.Constructing;

        /// <summary>Fill last read, 0–1. Test-facing.</summary>
        public float CurrentFill { get; private set; }

        public void Bind(LogisticsCenterRuntime centerRuntime, System.Func<float> fillReader, NebulaAnimationDirector director)
        {
            runtime = centerRuntime;
            fillFraction = fillReader;
            if (runtime != null) buildTotalSeconds = Mathf.Max(0.01f, runtime.BuildRemaining);

            BindAndRegister(director);
        }

        /// <summary>
        /// Maps the centre's own four states onto the shared structure
        /// lifecycle, so it poses through exactly the same code every drill and
        /// surface structure uses.
        /// </summary>
        protected override DrillLifecycle ResolveState()
        {
            if (runtime == null) return DrillLifecycle.Planned;

            switch (runtime.State)
            {
                case LogisticsCenterState.Constructing: return DrillLifecycle.Constructing;
                case LogisticsCenterState.Active: return DrillLifecycle.Active;
                case LogisticsCenterState.Disabled: return DrillLifecycle.Disabled;
                case LogisticsCenterState.Dismantled: return DrillLifecycle.Dismantled;
                default: return DrillLifecycle.Planned;
            }
        }

        protected override float ResolveProgress()
        {
            if (runtime == null) return 0f;
            if (runtime.State == LogisticsCenterState.Constructing)
                return 1f - Mathf.Clamp01(runtime.BuildRemaining / buildTotalSeconds);
            // Dismantled is an end state here, not a countdown, so it poses fully closed.
            return 1f;
        }

        protected override (float rateHz, float amplitude) ResolveWork()
        {
            if (runtime == null) return (0f, 0f);

            CurrentFill = fillFraction != null ? Mathf.Clamp01(fillFraction()) : 0f;
            return StructureAnimationProfiles.LogisticsWork(runtime.State, CurrentFill, runtime.Upgrading);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            runtime = null;
            fillFraction = null;
        }
    }
}
