using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// One transform's authored pose, captured once, plus the only safe way to
    /// animate it.
    /// <para>
    /// Every frame writes <c>base + offset</c>, never <c>current + offset</c>.
    /// That single rule is what makes ten thousand ticks land in exactly the
    /// same place as one: an animation that reads back its own output
    /// accumulates floating-point error until the model has visibly walked off
    /// its mount, and this type exists so no presenter can make that mistake.
    /// </para>
    /// <para>
    /// <see cref="Reset"/> restores the captured pose exactly, so switching
    /// animation off leaves the ship in its authored pose rather than frozen
    /// wherever the last tick happened to put it.
    /// </para>
    /// </summary>
    public struct PoseAnchor
    {
        private Transform target;
        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Vector3 baseScale;
        private bool captured;

        /// <summary>
        /// Captures a transform's current local pose as its baseline. A null
        /// transform yields an invalid anchor whose every operation is a no-op
        /// — that is the safe fallback for a ship whose visual root has not
        /// been built, and it must never break the boot.
        /// </summary>
        public static PoseAnchor Capture(Transform transform)
        {
            var anchor = new PoseAnchor();
            if (transform == null) return anchor;

            anchor.target = transform;
            anchor.basePosition = transform.localPosition;
            anchor.baseRotation = transform.localRotation;
            anchor.baseScale = transform.localScale;
            anchor.captured = true;
            return anchor;
        }

        /// <summary>False for an unbound anchor, or once its transform has been destroyed.</summary>
        public bool IsValid => captured && target != null;

        public Transform Target => target;
        public Vector3 BasePosition => basePosition;
        public Quaternion BaseRotation => baseRotation;
        public Vector3 BaseScale => baseScale;

        /// <summary>
        /// Poses the transform at its baseline plus the given offsets. Rotation
        /// is applied after the base rotation, so an offset of identity is
        /// always exactly the authored pose.
        /// </summary>
        public void Apply(Vector3 positionOffset, Quaternion rotationOffset)
        {
            if (!IsValid) return;
            target.localPosition = basePosition + positionOffset;
            target.localRotation = baseRotation * rotationOffset;
        }

        /// <summary>Poses with a uniform scale multiplier as well. 1 is exactly the authored scale.</summary>
        public void Apply(Vector3 positionOffset, Quaternion rotationOffset, float scaleMultiplier)
        {
            if (!IsValid) return;
            Apply(positionOffset, rotationOffset);
            target.localScale = baseScale * scaleMultiplier;
        }

        /// <summary>Puts the transform back exactly where it was captured.</summary>
        public void Reset()
        {
            if (!IsValid) return;
            target.localPosition = basePosition;
            target.localRotation = baseRotation;
            target.localScale = baseScale;
        }

        /// <summary>
        /// True while the transform still sits at its captured pose. Test-facing:
        /// this is the drift check.
        /// </summary>
        public bool IsAtBasePose(float tolerance = 1e-4f)
        {
            if (!IsValid) return false;
            return (target.localPosition - basePosition).sqrMagnitude <= tolerance * tolerance
                   && Quaternion.Angle(target.localRotation, baseRotation) <= tolerance * 100f
                   && (target.localScale - baseScale).sqrMagnitude <= tolerance * tolerance;
        }
    }
}
