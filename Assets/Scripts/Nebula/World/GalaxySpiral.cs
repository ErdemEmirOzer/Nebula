using UnityEngine;

namespace Nebula.World
{
    /// <summary>
    /// One galaxy's spiral geometry as pure data and pure maths.
    ///
    /// The same logarithmic spiral <see cref="GalaxyGenerator"/> builds its clouds from and
    /// <see cref="GalaxyMap"/> answers arm queries with — but with no GameObject, no
    /// Transform and no scene. That matters because the world's system placement lives in
    /// <c>SectorManifestGenerator</c>, which is seed-only and pure by contract: it can never
    /// reach for a runtime MonoBehaviour, so putting real star systems on the galaxy arms
    /// requires the arm maths to exist somewhere it can call.
    ///
    /// Derived exactly as GalaxyGenerator derives it, so a point produced here and the same
    /// point produced by the live galaxy are the same point.
    /// </summary>
    public readonly struct GalaxySpiral
    {
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly float BulgeRadius;
        public readonly float R0;
        public readonly float B;
        public readonly float MaxTheta;
        public readonly float ArmPhase0;
        public readonly int ArmCount;

        /// <summary>How many turns the arms make — GalaxyGenerator's own maxTheta.</summary>
        public const float DefaultMaxTheta = Mathf.PI * 4.6f;

        private GalaxySpiral(Vector3 center, float radius, float bulgeRadius, float r0, float b,
            float maxTheta, float armPhase0, int armCount)
        {
            Center = center; Radius = radius; BulgeRadius = bulgeRadius; R0 = r0; B = b;
            MaxTheta = maxTheta; ArmPhase0 = armPhase0; ArmCount = armCount;
        }

        /// <summary>
        /// Builds the geometry for a galaxy at a centre with a radius and arm count, deriving
        /// every value the way GalaxyGenerator does — including phasing the first arm so it
        /// runs through the world origin, which is what puts the play area on an arm.
        /// </summary>
        public static GalaxySpiral Create(Vector3 center, float radius, int armCount)
        {
            float bulgeRadius = radius * 0.13f;
            float r0 = bulgeRadius * 0.6f;
            float maxTheta = DefaultMaxTheta;
            float b = Mathf.Log(radius / r0) / maxTheta;

            // Phase the first arm so it runs through the play area (world origin).
            Vector3 toOrigin = -center;
            float originDistance = Mathf.Max(toOrigin.magnitude, r0 * 1.5f);
            float armPhase0 = Mathf.Atan2(toOrigin.z, toOrigin.x) - Mathf.Log(originDistance / r0) / b;

            return new GalaxySpiral(center, radius, bulgeRadius, r0, b, maxTheta, armPhase0,
                Mathf.Max(1, armCount));
        }

        /// <summary>Point on the given arm's centreline; t 0..1 runs core to rim. Galaxy-local, exactly as GalaxyMap.ArmLocalPoint.</summary>
        public Vector3 ArmLocalPoint(int arm, float t)
        {
            float theta = Mathf.Clamp01(t) * MaxTheta;
            float r = R0 * Mathf.Exp(B * theta);
            float angle = theta + ArmPhase0 + arm * (Mathf.PI * 2f / ArmCount);
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
        }

        /// <summary>The same point in absolute world coordinates. The galaxy root sits at its centre and is not rotated at generation time.</summary>
        public Vector3 ArmPoint(int arm, float t) => Center + ArmLocalPoint(arm, t);

        /// <summary>True when an absolute position falls inside this galaxy's disc radius.</summary>
        public bool Contains(Vector3 absolute) => (absolute - Center).sqrMagnitude <= Radius * Radius;
    }
}
