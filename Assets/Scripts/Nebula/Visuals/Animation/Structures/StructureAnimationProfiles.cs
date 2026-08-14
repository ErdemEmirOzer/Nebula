using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;

namespace Nebula.Visuals
{
    /// <summary>
    /// How a built thing is posed for each stage of its real lifecycle.
    /// <para>
    /// Every value here moves the whole object, because that is all the current
    /// art supports: every drill, structure and logistics prefab is a single
    /// mesh with no child parts and no anchors. Per-part mechanics — a rotary
    /// head, a dish, a dock door — need nodes that do not exist yet, and this
    /// part deliberately does not fake them.
    /// </para>
    /// </summary>
    public static class StructureAnimationProfiles
    {
        /// <summary>How far below its final resting place a structure starts while it is being built, as a fraction of its own height.</summary>
        public const float ConstructionSinkFraction = 0.55f;

        /// <summary>Scale a structure starts construction at. Never zero — a site that vanishes reads as a bug, not as building.</summary>
        public const float ConstructionStartScale = 0.35f;

        /// <summary>Static tilt a damaged structure settles at, in degrees. Small: the thing is damaged, not falling over.</summary>
        public const float DamagedTiltDegrees = 2.5f;

        /// <summary>Ceiling on any working oscillation, in local units.</summary>
        public const float MaxWorkAmplitude = 0.05f;

        /// <summary>
        /// Where a structure sits for a given lifecycle state, as an offset
        /// along its own up axis plus a uniform scale.
        /// <para>
        /// <paramref name="progress01"/> is the real build or dismantle
        /// progress the runtime already tracks — this reads it and never keeps
        /// a second counter.
        /// </para>
        /// </summary>
        public static (float sinkFraction, float scale, float tiltDegrees) Lifecycle(DrillLifecycle state, float progress01)
        {
            float progress = Mathf.Clamp01(progress01);

            switch (state)
            {
                // Nothing has been delivered yet: no visual presence at all.
                case DrillLifecycle.Planned:
                case DrillLifecycle.CapsuleInTransit:
                    return (ConstructionSinkFraction, 0f, 0f);

                // Rises out of the surface and grows to full as the real build advances.
                case DrillLifecycle.Constructing:
                    return (Mathf.Lerp(ConstructionSinkFraction, 0f, progress),
                            Mathf.Lerp(ConstructionStartScale, 1f, progress),
                            0f);

                // Exactly the authored pose. Nothing else may claim to be finished.
                case DrillLifecycle.Active:
                    return (0f, 1f, 0f);

                case DrillLifecycle.Damaged:
                    return (0f, 1f, DamagedTiltDegrees);

                // Safe closed pose: settled, still, upright.
                case DrillLifecycle.Disabled:
                    return (0f, 1f, 0f);

                // The controlled reverse of construction.
                case DrillLifecycle.Dismantled:
                    return (Mathf.Lerp(0f, ConstructionSinkFraction, progress),
                            Mathf.Lerp(1f, ConstructionStartScale, progress),
                            0f);

                default:
                    return (0f, 1f, 0f);
            }
        }

        /// <summary>True while the thing should read as working, and therefore as moving.</summary>
        public static bool IsWorking(DrillLifecycle state) => state == DrillLifecycle.Active;

        // ---- drill families ---------------------------------------------------

        /// <summary>
        /// The working character of each resource family, expressed as the only
        /// thing the current art can carry: rate and amplitude of a small
        /// whole-object working motion.
        /// <para>
        /// This is not the per-part mechanism each family deserves — a rotary
        /// head for Metal, a pump cycle for Gas, a precision cutter for
        /// Diamond. Those need geometry that does not exist. What is here is
        /// honest about that: five families that feel different in weight and
        /// cadence, not five different machines.
        /// </para>
        /// </summary>
        public static (float rateHz, float amplitude) DrillWork(ResourceFamily family)
        {
            switch (family)
            {
                // Heavy, slow, high-mass percussion.
                case ResourceFamily.Metal: return (1.6f, 0.030f);
                // Controlled boring — steadier and quieter than metal.
                case ResourceFamily.Wood: return (2.4f, 0.018f);
                // A pump/compressor cycle: regular, longer stroke.
                case ResourceFamily.Gas: return (1.1f, 0.024f);
                // Turbine flow — fast and shallow.
                case ResourceFamily.Water: return (3.2f, 0.012f);
                // Precision cutter: the fastest and the smallest.
                case ResourceFamily.Diamond: return (4.1f, 0.008f);
                default: return (2.0f, 0.020f);
            }
        }

        /// <summary>
        /// A drill with nowhere to put its output stops working. Read from the
        /// real stored amount against the real capacity — never a second
        /// production model.
        /// </summary>
        public static bool StorageBlocksWork(float stored, float capacity)
            => capacity > 0f && stored >= capacity - 0.001f;

        // ---- logistics center --------------------------------------------------

        /// <summary>
        /// The Logistics Center's own mechanical response. A dock door or arm
        /// would carry this far better; with a single mesh, the readable signal
        /// is how busy the whole station looks.
        /// </summary>
        public static (float rateHz, float amplitude) LogisticsWork(LogisticsCenterState state, float fillFraction, bool upgrading)
        {
            if (state != LogisticsCenterState.Active) return (0f, 0f);
            if (upgrading) return (1.2f, 0.020f);

            // Full: holding, not working. The station goes still.
            if (fillFraction >= 0.999f) return (0f, 0f);
            if (fillFraction >= 0.8f) return (0.9f, 0.010f);

            return (1.4f, 0.016f);
        }
    }
}
