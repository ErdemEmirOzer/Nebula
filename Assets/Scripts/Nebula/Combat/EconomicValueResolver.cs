using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §8: a planet's raid-relevant economic value — feeds both the
    /// pressure-accumulation speed multiplier (additionally capped at ×1.5
    /// by <see cref="ThreatPressureState.TickPressure"/>) and raid-target
    /// planet selection. Never scales enemy stats/budget — existing
    /// planetary defense strength plays no part in this formula at all.
    /// </summary>
    public static class EconomicValueResolver
    {
        public const float EconomicValueCap = 2.0f;

        public static float RarityMultiplier(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => 1.10f,
            Rarity.Rare => 1.25f,
            Rarity.VeryRare => 1.45f,
            Rarity.Legendary => 1.70f,
            _ => 1f
        };

        /// <summary>
        /// VS-014 §8 names a "star-connection ×1.25" factor but the codebase has
        /// no star-connection/trade-lane concept yet this slice (no field, no
        /// system anywhere computes one) — the brief's own "only if genuinely
        /// present" qualifier means this must never be faked. Always false until
        /// a real star-connection authority exists; wiring one up is out of
        /// VS-014's scope.
        /// </summary>
        public static bool HasStarConnection(string systemId) => false;

        /// <summary>
        /// `activeDrillCountOnPlanet` and `averageStorageFillFraction01` are real,
        /// already-existing ExtractionState/DrillRuntime data the caller reads;
        /// `pendingProbeReconActive` is FirstProbeState's own pending-recon flag
        /// (§10's "successful Probe escape" effect IS this same ×1.15 term, not a
        /// second one) — consumed by the caller exactly once per raid.
        /// </summary>
        public static float Resolve(Rarity rarity, int activeDrillCountOnPlanet, float averageStorageFillFraction01,
            bool starConnected, bool pendingProbeReconActive)
        {
            float value = RarityMultiplier(rarity);
            value *= 1f + 0.10f * Mathf.Max(0, activeDrillCountOnPlanet - 1); // +10% per extra active drill beyond the first
            value *= 1f + 0.20f * Mathf.Clamp01(averageStorageFillFraction01); // up to +20% at full storage
            if (starConnected) value *= 1.25f;
            if (pendingProbeReconActive) value *= 1.15f;
            return Mathf.Min(value, EconomicValueCap);
        }
    }
}
