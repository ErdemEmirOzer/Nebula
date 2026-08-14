using UnityEngine;

namespace Nebula.Navigation
{
    /// <summary>
    /// Pure formatting helpers for the deliberately-imprecise Signal-level
    /// display (VS-006 §2): a coarse compass sector instead of an exact
    /// bearing, and distance rounded to the nearest 50,000 units instead of
    /// an exact value. Located/Visited systems bypass this and show exact
    /// numbers directly.
    /// </summary>
    public static class NavigationFormat
    {
        private static readonly string[] SectorLabels =
        {
            "KUZEY", "KUZEYDOĞU", "DOĞU", "GÜNEYDOĞU", "GÜNEY", "GÜNEYBATI", "BATI", "KUZEYBATI"
        };

        /// <summary>8-way compass sector on the XZ plane, world-axis-relative (not ship-relative — stable, imprecise on purpose).</summary>
        public static string ApproxBearingLabel(Vector3 fromAbs, Vector3 toAbs)
        {
            Vector3 delta = toAbs - fromAbs;
            float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            int sector = Mathf.RoundToInt(angle / 45f) % 8;
            return SectorLabels[sector];
        }

        public const float DistanceRoundingUnit = 50000f;

        public static float ApproxDistance(float exactDistance)
        {
            return Mathf.Round(exactDistance / DistanceRoundingUnit) * DistanceRoundingUnit;
        }
    }
}
