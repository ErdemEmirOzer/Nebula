using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Visuals
{
    /// <summary>
    /// Poses one surface structure — Command Center, Laser, Plasma, Sensor
    /// Array, Maintenance Platform — from its own runtime.
    /// <para>
    /// Only the whole object moves. The per-type mechanics each of these
    /// deserves — a dish sweeping, a barrel settling to standby, a maintenance
    /// arm idling — need child nodes none of these prefabs has. Targeting,
    /// aim and every gameplay position are untouched regardless.
    /// </para>
    /// </summary>
    public class StructureAnimationPresenter : StructurePosePresenter
    {
        private SurfaceStructure structure;
        private float capsuleTotalSeconds;
        private float buildTotalSeconds;

        public SurfaceStructureType Type
            => structure != null ? structure.Type : SurfaceStructureType.Command;

        /// <summary>True while a tier upgrade is running. Test-facing.</summary>
        public bool Upgrading => structure != null && structure.ActiveUpgradeJob != null;

        public void Bind(SurfaceStructure surfaceStructure, NebulaAnimationDirector director)
        {
            structure = surfaceStructure;

            if (structure != null && structure.Runtime != null)
            {
                capsuleTotalSeconds = Mathf.Max(0.01f, structure.Runtime.CapsuleRemaining);
                buildTotalSeconds = Mathf.Max(0.01f, structure.Runtime.BuildRemaining);
            }

            BindAndRegister(director);
        }

        protected override DrillLifecycle ResolveState()
            => structure != null && structure.Runtime != null ? structure.Runtime.State : DrillLifecycle.Planned;

        protected override float ResolveProgress()
        {
            if (structure == null || structure.Runtime == null) return 0f;

            switch (structure.Runtime.State)
            {
                case DrillLifecycle.CapsuleInTransit:
                    return 1f - Mathf.Clamp01(structure.Runtime.CapsuleRemaining / capsuleTotalSeconds);
                case DrillLifecycle.Constructing:
                    return 1f - Mathf.Clamp01(structure.Runtime.BuildRemaining / buildTotalSeconds);
                case DrillLifecycle.Active:
                case DrillLifecycle.Damaged:
                case DrillLifecycle.Disabled:
                    return 1f;
                default:
                    return 0f;
            }
        }

        protected override (float rateHz, float amplitude) ResolveWork()
        {
            if (structure == null || structure.Runtime == null) return (0f, 0f);

            // An upgrading structure reads as worked-on: a slow, deliberate
            // mechanical cycle rather than its usual stillness.
            if (structure.ActiveUpgradeJob != null) return (1.1f, 0.016f);

            // Defensive and support structures are still when idle. What they
            // would actually do — sweep, track, settle — belongs to nodes that
            // do not exist, and ANI-04 owns aim and recoil in any case.
            return (0f, 0f);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            structure = null;
        }
    }
}
