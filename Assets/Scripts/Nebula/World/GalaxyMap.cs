using UnityEngine;

namespace Nebula.World
{
    /// <summary>
    /// Lives on the galaxy root and knows its spiral geometry. Answers two
    /// questions: "give me a point on arm N" (used to seed arm star systems)
    /// and "which arm is this world position in?" (used by the HUD location).
    /// Queries go through the root's transform, so galaxy rotation and
    /// floating-origin shifts are handled automatically — the arm label always
    /// matches the visible clouds.
    /// </summary>
    public class GalaxyMap : MonoBehaviour
    {
        private float radius;
        private float bulgeRadius;
        private float r0;
        private float b;
        private float maxTheta;
        private float armPhase0;
        private int armCount;

        public int ArmCount => armCount;
        public float Radius => radius;
        /// <summary>The first arm's spiral phase, exactly as configured — the strategic map draws its spiral from this rather than re-deriving the generator's formula.</summary>
        public float ArmPhase0 => armPhase0;

        public void Configure(float galaxyRadius, float galaxyBulgeRadius, float spiralR0, float spiralB,
            float spiralMaxTheta, float firstArmPhase, int arms)
        {
            radius = galaxyRadius;
            bulgeRadius = galaxyBulgeRadius;
            r0 = spiralR0;
            b = spiralB;
            maxTheta = spiralMaxTheta;
            armPhase0 = firstArmPhase;
            armCount = Mathf.Max(1, arms);
        }

        /// <summary>Point on the given arm's centerline; t 0..1 runs from core to rim (galaxy-local space).</summary>
        public Vector3 ArmLocalPoint(int arm, float t)
        {
            float theta = Mathf.Clamp01(t) * maxTheta;
            float r = r0 * Mathf.Exp(b * theta);
            float angle = theta + armPhase0 + arm * (Mathf.PI * 2f / armCount);
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
        }

        /// <summary>
        /// Arm index (0-based) containing the world position, or -1 when
        /// between arms / outside the disc. inCore is true inside the bulge.
        /// </summary>
        public int ArmIndexAt(Vector3 worldPosition, out bool inCore)
        {
            inCore = false;
            Vector3 local = transform.InverseTransformPoint(worldPosition);

            float r = new Vector2(local.x, local.z).magnitude;
            if (r < bulgeRadius * 1.15f)
            {
                inCore = true;
                return -1;
            }
            if (r > radius * 1.05f) return -1;
            // Too far above/below the disc = not in any arm. Kept in sync
            // with the cloud layer's (doubled) vertical sigma.
            if (Mathf.Abs(local.y) > radius * 0.02f + r * 0.06f) return -1;

            float thetaAtR = Mathf.Log(Mathf.Max(r, r0) / r0) / b;
            float phi = Mathf.Atan2(local.z, local.x);

            int best = -1;
            float bestArc = float.MaxValue;
            for (int arm = 0; arm < armCount; arm++)
            {
                float expected = thetaAtR + armPhase0 + arm * (Mathf.PI * 2f / armCount);
                float delta = Mathf.Repeat(phi - expected + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
                float arc = Mathf.Abs(delta) * r;
                if (arc < bestArc)
                {
                    bestArc = arc;
                    best = arm;
                }
            }

            // Accept only if within roughly the cloud layer's arm width.
            float width = (radius * 0.01f + r * 0.035f) * 3f;
            return bestArc <= width ? best : -1;
        }
    }
}
