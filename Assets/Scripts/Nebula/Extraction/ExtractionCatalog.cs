using UnityEngine;

namespace Nebula.Extraction
{
    /// <summary>The five resource families of the new (planet-type based) system.</summary>
    public enum ResourceFamily { Metal = 0, Wood = 1, Gas = 2, Water = 3, Diamond = 4 }

    public enum Rarity { Common = 0, Uncommon = 1, Rare = 2, VeryRare = 3, Legendary = 4 }

    public enum SurfaceNodeKind { Drill, Command, Defense, Support }

    /// <summary>
    /// Pure data + deterministic math for VS-001. Content identity lives here
    /// as stable strings (never enum ordinals in save files). Nothing in this
    /// class touches UnityEngine.Random, so building a planet profile never
    /// disturbs the universe generation stream.
    /// </summary>
    public static class ExtractionCatalog
    {
        public const string DrillT1Id = "structure.drill.t1";
        public const string BuildKitId = "item.universal_pioneer_build_kit";

        // --- Drill T1 build price (P1-DATA-011) --------------------------------------
        // The drill was the one buildable structure with no catalog row of its own, so
        // its price lived as a private const in ShipExtraction, a second const in
        // QuickAssetPackageService and a third literal ("20 PARÇA") in the HUD. Three
        // copies of one number, none of them the authority. It lives here now, beside
        // the drill's own content id, and all three read it.

        /// <summary>Basic Building Parts one drill T1 costs when the pioneer kit is spent. The single authority — never re-typed at a call site or in interface text.</summary>
        public const int DrillT1PartsCost = 20;

        /// <summary>The 75%-cancel refund of <see cref="DrillT1PartsCost"/>, floored — the same <see cref="SurfaceStructureCatalog.RefundFraction"/> every other structure's cancel uses, never a second hand-written 15.</summary>
        public static readonly int DrillT1RefundParts =
            Mathf.FloorToInt(DrillT1PartsCost * SurfaceStructureCatalog.RefundFraction);

        // Content IDs, indexed by enum for stable persistence.
        public static readonly string[] ResourceIds =
        {
            "resource.metal", "resource.wood", "resource.gas", "resource.water", "resource.diamond"
        };
        public static readonly string[] RarityIds =
        {
            "rarity.common", "rarity.uncommon", "rarity.rare", "rarity.very_rare", "rarity.legendary"
        };
        public static readonly string[] ResourceNames = { "Metal", "Wood", "Gas", "Water", "Diamond" };
        public static readonly string[] RarityNames = { "Common", "Uncommon", "Rare", "Very Rare", "Legendary" };

        private const float BaseRawPerMinute = 8f;

        // Rarity distribution (cumulative): 50 / 30 / 15 / 4 / 1.
        private static readonly float[] RarityCumulative = { 0.50f, 0.80f, 0.95f, 0.99f, 1.00f };
        private static readonly float[] RarityYieldMultiplier = { 1f, 1.25f, 1.6f, 2.1f, 3f };
        private static readonly float[] T1Efficiency = { 1f, 0.75f, 0.5f, 0.25f, 0.25f };

        public static Color ResourceColor(ResourceFamily family)
        {
            switch (family)
            {
                case ResourceFamily.Metal: return new Color(0.7f, 0.78f, 0.9f);
                case ResourceFamily.Wood: return new Color(0.55f, 0.82f, 0.4f);
                case ResourceFamily.Gas: return new Color(0.3f, 0.95f, 0.7f);
                case ResourceFamily.Water: return new Color(0.35f, 0.7f, 1f);
                default: return new Color(0.9f, 0.6f, 1f);
            }
        }

        public static Rarity RarityFromUnit(float unit01)
        {
            for (int i = 0; i < RarityCumulative.Length; i++)
            {
                if (unit01 < RarityCumulative[i]) return (Rarity)i;
            }
            return Rarity.Legendary;
        }

        /// <summary>Effective T1 production in raw units per minute for a rarity.</summary>
        public static float T1ProductionPerMinute(Rarity rarity)
        {
            return BaseRawPerMinute * RarityYieldMultiplier[(int)rarity] * T1Efficiency[(int)rarity];
        }

        public static float T1EfficiencyPercent(Rarity rarity) => T1Efficiency[(int)rarity] * 100f;

        /// <summary>Local drill storage = 10 minutes of output, rounded up to the nearest 5.</summary>
        public static float T1StorageCapacity(Rarity rarity)
        {
            float tenMinutes = T1ProductionPerMinute(rarity) * 10f;
            return Mathf.Ceil(tenMinutes / 5f) * 5f;
        }

        // --- VS-017A.2 §B4: Drill T2 -------------------------------------------------
        // The SINGLE numeric authority for drill output/storage at any tier. Both the
        // live Full-system path (DrillRuntime) and the remote data-only distant path
        // (SystemStreamingDirector.TickDeltaDataOnly) call exactly these methods, so the
        // two can never drift apart or round differently.

        /// <summary>T2 output multiplier over the same planet/rarity's own effective T1 rate.</summary>
        public const float T2ProductionMultiplier = 1.5f;
        /// <summary>T2 local storage holds exactly this many seconds (15 minutes) of that drill's own effective T2 output.</summary>
        public const float T2StorageSeconds = 900f;

        /// <summary>Effective T2 production per minute — the same rarity's effective T1 rate × 1.5.</summary>
        public static float T2ProductionPerMinute(Rarity rarity) => T1ProductionPerMinute(rarity) * T2ProductionMultiplier;

        /// <summary>T2 local drill storage = exactly 15 minutes (900s) of that drill's own effective T2 output. Deliberately NOT rounded to the nearest 5 like T1 — this is one exact formula, evaluated identically on both the Full and remote paths.</summary>
        public static float T2StorageCapacity(Rarity rarity) => T2ProductionPerMinute(rarity) / 60f * T2StorageSeconds;

        /// <summary>Tier-dispatched production rate — the single entry point every drill production site uses. Any tier other than 2 reads as T1.</summary>
        public static float ProductionPerMinute(Rarity rarity, int tier) =>
            tier >= 2 ? T2ProductionPerMinute(rarity) : T1ProductionPerMinute(rarity);

        /// <summary>Tier-dispatched storage capacity — the single entry point every drill storage site uses. Any tier other than 2 reads as T1.</summary>
        public static float StorageCapacity(Rarity rarity, int tier) =>
            tier >= 2 ? T2StorageCapacity(rarity) : T1StorageCapacity(rarity);

        /// <summary>FNV-1a 64-bit over a UTF-16 string — deterministic, engine-independent.</summary>
        public static ulong Hash(string value)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static ulong Hash(ulong worldSeed, string subKey) => Hash(worldSeed.ToString() + ":" + subKey);

        /// <summary>Deterministic float in [0,1) from a hash (splitmix64 finalizer).</summary>
        public static float Unit01(ulong hash)
        {
            hash ^= hash >> 30; hash *= 0xbf58476d1ce4e5b9UL;
            hash ^= hash >> 27; hash *= 0x94d049bb133111ebUL;
            hash ^= hash >> 31;
            return (hash >> 11) * (1.0f / 9007199254740992.0f); // 53-bit mantissa
        }
    }
}
