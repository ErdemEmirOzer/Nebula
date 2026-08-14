using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>Where a delivery capsule is in its descent. Derived from the real countdown, never from a timer of its own.</summary>
    public enum CapsuleFlightPhase
    {
        /// <summary>Sitting on the ship, not yet released.</summary>
        Prepared,

        /// <summary>Just left the hull — still close, still oriented to the ship.</summary>
        Launch,

        /// <summary>The long middle of the trip.</summary>
        Travel,

        /// <summary>Close enough that the planet, not the ship, decides which way is up.</summary>
        SurfaceApproach,

        /// <summary>On the slot, settling.</summary>
        Settle,

        /// <summary>Done. Construction owns the site from here.</summary>
        ConstructionHandOff
    }

    /// <summary>
    /// The pure geometry and phasing of a capsule's descent: no scene, no
    /// GameObject, no state of its own. Given where a capsule left, where it is
    /// going, which way the surface faces and how far through it is, this says
    /// where it should be and which way it should point.
    /// <para>
    /// Progress always comes from the real <c>CapsuleRemaining</c> countdown
    /// the runtime already owns, which is what makes a loaded save resume from
    /// the right point in the sky instead of replaying the launch.
    /// </para>
    /// </summary>
    public static class CapsuleFlightProfile
    {
        /// <summary>Below this the capsule is still leaving.</summary>
        public const float LaunchUntil = 0.12f;

        /// <summary>Past this the planet's normal takes over the capsule's orientation.</summary>
        public const float ApproachFrom = 0.72f;

        /// <summary>Past this it is on the slot.</summary>
        public const float SettleFrom = 0.94f;

        /// <summary>How far the route bows away from a straight line, as a fraction of the trip length.</summary>
        public const float ArcHeightFraction = 0.22f;

        /// <summary>Resolves the phase from real progress. Complete is only ever reported at the very end.</summary>
        public static CapsuleFlightPhase Phase(float progress01)
        {
            float p = Mathf.Clamp01(progress01);

            if (p <= 0f) return CapsuleFlightPhase.Prepared;
            if (p < LaunchUntil) return CapsuleFlightPhase.Launch;
            if (p < ApproachFrom) return CapsuleFlightPhase.Travel;
            if (p < SettleFrom) return CapsuleFlightPhase.SurfaceApproach;
            if (p < 1f) return CapsuleFlightPhase.Settle;
            return CapsuleFlightPhase.ConstructionHandOff;
        }

        /// <summary>
        /// Turns a remaining countdown into 0..1 progress. A capsule loaded
        /// mid-flight therefore appears exactly where its remaining time says
        /// it should, rather than starting over at the ship.
        /// </summary>
        public static float ProgressFromRemaining(float remainingSeconds, float totalSeconds)
        {
            if (totalSeconds <= 0f) return 1f;
            return Mathf.Clamp01(1f - remainingSeconds / totalSeconds);
        }

        /// <summary>
        /// The two control points of a cubic Bézier from ship to slot. The
        /// route bows away from the planet so the capsule arcs down onto the
        /// surface rather than driving straight through it.
        /// </summary>
        public static (Vector3 c1, Vector3 c2) ControlPoints(Vector3 start, Vector3 end, Vector3 surfaceNormal)
        {
            Vector3 travel = end - start;
            float distance = travel.magnitude;
            if (distance < 1e-4f) return (start, end);

            Vector3 lift = surfaceNormal.sqrMagnitude > 1e-6f
                ? surfaceNormal.normalized
                : Vector3.up;
            float arc = distance * ArcHeightFraction;

            // The first control point pulls away from the ship along the route;
            // the second hangs above the slot so the final approach comes down
            // the surface normal rather than sideways into it.
            return (start + travel * 0.25f + lift * (arc * 0.35f),
                    end + lift * arc);
        }

        /// <summary>Position along the descent at the given real progress.</summary>
        public static Vector3 Position(Vector3 start, Vector3 end, Vector3 surfaceNormal, float progress01)
        {
            float t = Mathf.Clamp01(progress01);
            (Vector3 c1, Vector3 c2) = ControlPoints(start, end, surfaceNormal);

            float u = 1f - t;
            return u * u * u * start
                   + 3f * u * u * t * c1
                   + 3f * u * t * t * c2
                   + t * t * t * end;
        }

        /// <summary>
        /// Which way the capsule faces. It flies nose-first down the route, and
        /// as it nears the surface it rotates to stand upright on the planet's
        /// normal — so it lands on its feet rather than nose-down.
        /// </summary>
        public static Quaternion Orientation(Vector3 start, Vector3 end, Vector3 surfaceNormal, float progress01)
        {
            float t = Mathf.Clamp01(progress01);
            Vector3 up = surfaceNormal.sqrMagnitude > 1e-6f ? surfaceNormal.normalized : Vector3.up;

            // Sample slightly ahead to get the travel direction without
            // differentiating the curve.
            Vector3 here = Position(start, end, surfaceNormal, t);
            Vector3 ahead = Position(start, end, surfaceNormal, Mathf.Min(1f, t + 0.02f));
            Vector3 forward = ahead - here;

            Quaternion travelling = forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward.normalized, up)
                : Quaternion.LookRotation(up, up);

            // Standing on the slot: up is the surface normal.
            Quaternion landed = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(end - start, up).sqrMagnitude > 1e-6f
                    ? Vector3.ProjectOnPlane(end - start, up).normalized
                    : Vector3.Cross(up, Vector3.right).normalized,
                up);

            float blend = Mathf.InverseLerp(ApproachFrom, 1f, t);
            return Quaternion.Slerp(travelling, landed, blend);
        }

        /// <summary>The stagger the Quick Asset Package really uses. Read here so a test can pin it; animation never decides it.</summary>
        public static float QuickAssetSendDelay(int capsuleIndex)
        {
            switch (capsuleIndex)
            {
                case 0: return Nebula.Extraction.QuickAssetPackageService.DrillSendDelay;
                case 1: return Nebula.Extraction.QuickAssetPackageService.CommandSendDelay;
                case 2: return Nebula.Extraction.QuickAssetPackageService.LaserSendDelay;
                default: return 0f;
            }
        }
    }
}
