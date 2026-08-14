using Nebula.Extraction;

namespace Nebula.Sectors
{
    /// <summary>Four gameplay star classes (VS-009 §2) — replaces the old cosmetic O/B/A/F/G/K/M letter.</summary>
    public enum StarClass { RedDwarf, Yellow, White, BlueGiant }

    /// <summary>
    /// Pure data + deterministic picks for <see cref="StarClass"/>. Weights,
    /// energy multiplier and the Critical/Warning radius multipliers (both
    /// expressed as a factor of <see cref="SystemManifest.StarRadius"/>) are
    /// fixed gameplay constants, never re-derived per seed.
    /// </summary>
    public static class StarClassCatalog
    {
        private static readonly (StarClass cls, float weight)[] WeightTable =
        {
            (StarClass.RedDwarf, 40f), (StarClass.Yellow, 35f), (StarClass.White, 20f), (StarClass.BlueGiant, 5f)
        };

        /// <summary>
        /// Reads <see cref="WeightTable"/> rather than repeating it. The two used to carry
        /// the same four numbers independently, which is the same trap
        /// <c>EnvironmentClassCatalog</c> fell into outright: there, <c>Pick</c> hardcoded
        /// its own thresholds and <c>Weight</c> was powerless, so skewing it to 50/30/20
        /// changed nothing about what generated. Here <c>Pick</c> did read the table, so
        /// only <c>Weight</c> could go stale — quieter, same defect.
        /// </summary>
        public static float Weight(StarClass c)
        {
            foreach (var (cls, weight) in WeightTable)
                if (cls == c) return weight;
            return 0f;
        }

        /// <summary>Baseline energy-output trait of the star class (not a risk-level modifier) — data for future consumers (VS-013+).</summary>
        public static float EnergyMultiplier(StarClass c) => c switch
        {
            StarClass.RedDwarf => 0.75f, StarClass.Yellow => 1f, StarClass.White => 1.25f, StarClass.BlueGiant => 1.5f, _ => 1f
        };

        public static float CriticalRadiusMultiplier(StarClass c) => c switch
        {
            StarClass.RedDwarf => 1.35f, StarClass.Yellow => 1.50f, StarClass.White => 1.75f, StarClass.BlueGiant => 2f, _ => 1.5f
        };

        public static float WarningRadiusMultiplier(StarClass c) => c switch
        {
            StarClass.RedDwarf => 2.25f, StarClass.Yellow => 2.75f, StarClass.White => 3.25f, StarClass.BlueGiant => 4f, _ => 2.75f
        };

        public static string Label(StarClass c) => c switch
        {
            StarClass.RedDwarf => "KIRMIZI CÜCE", StarClass.Yellow => "SARI", StarClass.White => "BEYAZ", StarClass.BlueGiant => "MAVİ DEV", _ => "?"
        };

        /// <summary>Short text+symbol tag — never color alone (VS-009 §4 information-gate rule).</summary>
        public static string ShortTag(StarClass c) => c switch
        {
            StarClass.RedDwarf => "●KC", StarClass.Yellow => "●S", StarClass.White => "●B", StarClass.BlueGiant => "●MD", _ => "?"
        };

        /// <summary>Weighted deterministic pick (40/35/20/5) from the system id's own hash channel.</summary>
        public static StarClass Pick(ulong seed, string systemId)
        {
            float roll = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, systemId + "/starclass")) * 100f;
            float cumulative = 0f;
            foreach (var (cls, weight) in WeightTable)
            {
                cumulative += weight;
                if (roll < cumulative) return cls;
            }
            return WeightTable[WeightTable.Length - 1].cls; // float rounding guard
        }

        /// <summary>
        /// Deterministic reroll excluding BlueGiant (renormalized over the
        /// remaining 40/35/20 weights) — used only by FrontierGuaranteePass
        /// to fix a nearest-three system that rolled BlueGiant; a distinct
        /// hash tag so it never just reproduces the original biased roll.
        /// </summary>
        public static StarClass PickNonBlueGiant(ulong seed, string systemId)
        {
            float roll = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, systemId + "/starclass/fixup")) * 95f;
            if (roll < 40f) return StarClass.RedDwarf;
            if (roll < 75f) return StarClass.Yellow;
            return StarClass.White;
        }
    }
}
