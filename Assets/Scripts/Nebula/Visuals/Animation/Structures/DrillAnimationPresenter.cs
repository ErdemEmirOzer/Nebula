using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Visuals
{
    /// <summary>
    /// Poses one drill from its own runtime. Reads the lifecycle, the real
    /// build progress and the real stored amount, and changes none of them:
    /// production rate, storage capacity, build duration, cost and refund are
    /// all exactly what they would be without this component.
    /// </summary>
    public class DrillAnimationPresenter : StructurePosePresenter
    {
        private DrillStructure drill;
        private float capsuleTotalSeconds;
        private float buildTotalSeconds;

        public ResourceFamily Family => drill != null && drill.Runtime != null ? drill.Runtime.Family : ResourceFamily.Metal;

        /// <summary>True while a full store has stopped the drill working. Test-facing.</summary>
        public bool StorageBlocked { get; private set; }

        public void Bind(DrillStructure drillStructure, NebulaAnimationDirector director)
        {
            drill = drillStructure;

            // Captured once for normalising the runtime's countdown into a
            // 0..1 visual progress. Not a second timer: the countdown itself
            // remains the only authority on when building finishes.
            if (drill != null && drill.Runtime != null)
            {
                capsuleTotalSeconds = Mathf.Max(0.01f, drill.Runtime.CapsuleRemaining);
                buildTotalSeconds = Mathf.Max(0.01f, drill.Runtime.BuildRemaining);
            }

            BindAndRegister(director);
        }

        protected override DrillLifecycle ResolveState()
            => drill != null && drill.Runtime != null ? drill.Runtime.State : DrillLifecycle.Planned;

        protected override float ResolveProgress()
        {
            if (drill == null || drill.Runtime == null) return 0f;

            switch (drill.Runtime.State)
            {
                case DrillLifecycle.CapsuleInTransit:
                    return 1f - Mathf.Clamp01(drill.Runtime.CapsuleRemaining / capsuleTotalSeconds);
                case DrillLifecycle.Constructing:
                    return 1f - Mathf.Clamp01(drill.Runtime.BuildRemaining / buildTotalSeconds);
                case DrillLifecycle.Active:
                case DrillLifecycle.Damaged:
                case DrillLifecycle.Disabled:
                    // Already built: the pose is final, whatever the counters say.
                    return 1f;
                default:
                    return 0f;
            }
        }

        protected override (float rateHz, float amplitude) ResolveWork()
        {
            if (drill == null || drill.Runtime == null) return (0f, 0f);

            DrillRuntime runtime = drill.Runtime;
            StorageBlocked = StructureAnimationProfiles.StorageBlocksWork(runtime.StoredAmount, runtime.StorageCapacity);

            // A drill with nowhere to put its output stops. This is read from
            // the real store, and stopping the animation does not stop (or
            // start) a single unit of production.
            if (StorageBlocked) return (0f, 0f);

            (float rateHz, float amplitude) = StructureAnimationProfiles.DrillWork(runtime.Family);

            // T2 reads as steadier rather than faster: a better machine, not a
            // machine in a hurry.
            if (runtime.Tier >= 2) return (rateHz * 0.9f, amplitude * 0.75f);
            return (rateHz, amplitude);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            drill = null;
        }
    }
}
