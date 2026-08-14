using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §14: pure data + deterministic position/reward math for the
    /// single, persistent Protected Pirate Base. Combat itself (Durability,
    /// DamageResolver, PlanetaryDefenseCatalog.LaserT1's own DPS/range) is
    /// never re-typed here — this only holds the numbers the brief gives
    /// that don't already live in an existing catalog (KZG, garrison
    /// composition, reward ranges, safe spawn distance).
    /// </summary>
    public static class PirateBaseCatalog
    {
        public const float ShieldMax = 500f;
        public const float ArmorMax = 900f;
        public const float HullMax = 1400f;

        /// <summary>Deterministic, safe distance from the system's star — never near any planet's own defense range.</summary>
        public const float SafeDistanceFromStar = 20000f;

        public const int RewardXp = 500;
        public const int RewardBasicPartsMin = 30, RewardBasicPartsMax = 60;
        public const int RewardMaterialMin = 10, RewardMaterialMax = 25;
        public const float RewardThreatPressureReduction = 15f;

        /// <summary>Deterministic safe position for the base within its home system, relative to the star — never re-rolled once computed, always reproducible from the same (campaign seed, systemId).</summary>
        public static Vector3 ComputePosition(ulong campaignSeed, string systemId)
        {
            ulong azimuthHash = ExtractionCatalog.Hash(campaignSeed, systemId + "/pirate-base/azimuth");
            ulong elevationHash = ExtractionCatalog.Hash(campaignSeed, systemId + "/pirate-base/elevation");
            float azimuth = ExtractionCatalog.Unit01(azimuthHash) * Mathf.PI * 2f;
            float elevation = (ExtractionCatalog.Unit01(elevationHash) - 0.5f) * Mathf.PI * 0.5f;
            float cosE = Mathf.Cos(elevation);
            Vector3 direction = new Vector3(cosE * Mathf.Cos(azimuth), Mathf.Sin(elevation), cosE * Mathf.Sin(azimuth));
            return direction * SafeDistanceFromStar;
        }

        /// <summary>
        /// Garrison def ids (VS-014 §14: 2 Fighter + 1 Looter always; +1 Heavy
        /// only if already taught elsewhere AND the threat stage has
        /// progressed past Pirate -- the Base itself must never be the first
        /// place a player encounters Heavy).
        /// </summary>
        public static System.Collections.Generic.List<string> Garrison(bool heavyTaughtAndStageProgressed)
        {
            var list = new System.Collections.Generic.List<string>
            {
                EnemyIds.FighterMk1, EnemyIds.FighterMk1, EnemyIds.LooterTransportMk1
            };
            if (heavyTaughtAndStageProgressed) list.Add(EnemyIds.HeavyAssaultMk1);
            return list;
        }

        public static int ResolveBasicPartsReward(ulong seed) =>
            RewardBasicPartsMin + Mathf.FloorToInt(ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "pirate-base/reward/parts")) * (RewardBasicPartsMax - RewardBasicPartsMin + 1));

        public static int ResolveMaterialReward(ulong seed, out string materialId)
        {
            int materialIndex = Mathf.FloorToInt(ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "pirate-base/reward/material-id")) * ProcessingCatalog.MaterialIds.Length) % ProcessingCatalog.MaterialIds.Length;
            materialId = ProcessingCatalog.MaterialIds[materialIndex];
            return RewardMaterialMin + Mathf.FloorToInt(ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "pirate-base/reward/material-amount")) * (RewardMaterialMax - RewardMaterialMin + 1));
        }
    }
}
