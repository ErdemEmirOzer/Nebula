using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Logistics
{
    public enum AnchorRejectReason { None, NoSafeGap }

    /// <summary>
    /// Deterministic, system-local anchor for the orbital Logistics Center
    /// (VS-011 §2): strictly outside the star's Warning-radius danger
    /// boundary and strictly inside the innermost planet's orbit, both with a
    /// fixed safety margin. Never random, never player-placed — the same
    /// system always resolves the same anchor. Returns an explicit rejection
    /// (never a clamp) when the system has no safe gap at all. The offset is
    /// system-local (relative to the star), so a live materialization simply
    /// adds it to the star's CURRENT transform position — automatically
    /// floating-origin correct, exactly like planet placement.
    /// </summary>
    public static class LogisticsCenterAnchor
    {
        private const float DangerMargin = 1.10f;   // 10% clear of the Warning-radius boundary
        private const float OrbitMargin = 0.85f;     // stay safely inside the innermost planet's orbit

        public static bool TryComputeAnchor(SystemManifest manifest, out Vector3 systemLocalOffset, out AnchorRejectReason reason)
        {
            systemLocalOffset = Vector3.zero;
            reason = AnchorRejectReason.None;
            if (manifest == null || manifest.Planets.Count == 0) { reason = AnchorRejectReason.NoSafeGap; return false; }

            float dangerRadius = manifest.StarRadius * StarClassCatalog.WarningRadiusMultiplier(manifest.StarClass);
            float innermostOrbit = float.MaxValue;
            foreach (PlanetManifest p in manifest.Planets) if (p.OrbitRadius < innermostOrbit) innermostOrbit = p.OrbitRadius;

            float safeInner = dangerRadius * DangerMargin;
            float safeOuter = innermostOrbit * OrbitMargin;
            if (safeOuter <= safeInner) { reason = AnchorRejectReason.NoSafeGap; return false; }

            float anchorRadius = (safeInner + safeOuter) * 0.5f;
            float angleDeg = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(manifest.SystemId + "/logistics_center_anchor")) * 360f;
            float rad = angleDeg * Mathf.Deg2Rad;
            systemLocalOffset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * anchorRadius;
            return true;
        }

        /// <summary>Manifest-space absolute position (for distance/ETA math on a non-materialized system) — never floating-origin adjusted.</summary>
        public static bool TryComputeAbsolutePosition(SystemManifest manifest, out Vector3 absolutePosition, out AnchorRejectReason reason)
        {
            if (!TryComputeAnchor(manifest, out Vector3 offset, out reason)) { absolutePosition = Vector3.zero; return false; }
            absolutePosition = manifest.AbsoluteCenter + offset;
            return true;
        }
    }
}
