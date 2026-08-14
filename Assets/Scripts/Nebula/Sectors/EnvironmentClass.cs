using Nebula.Extraction;

namespace Nebula.Sectors
{
    /// <summary>Three planetary environment classes (VS-009 §3) — independent of ResourceFamily/Rarity.</summary>
    public enum EnvironmentClass { Calm, Harsh, Extreme }

    public static class EnvironmentClassCatalog
    {
        public static float Weight(EnvironmentClass e) => e switch
        {
            EnvironmentClass.Calm => 60f, EnvironmentClass.Harsh => 30f, EnvironmentClass.Extreme => 10f, _ => 0f
        };

        /// <summary>Duration multiplier for post-capsule structure construction / structure upgrade / field repair only — never production, storage, cost, scan, flight, vehicle speed, damage or upkeep.</summary>
        public static float DurationMultiplier(EnvironmentClass e) => e switch
        {
            EnvironmentClass.Calm => 1f, EnvironmentClass.Harsh => 1.15f, EnvironmentClass.Extreme => 1.30f, _ => 1f
        };

        public static string Label(EnvironmentClass e) => e switch
        {
            EnvironmentClass.Calm => "SAKİN", EnvironmentClass.Harsh => "SERT", EnvironmentClass.Extreme => "AŞIRI", _ => "?"
        };

        /// <summary>
        /// Text+symbol tag — never color alone (VS-009 §4 information-gate rule).
        /// <para>
        /// The symbol is the redundancy channel §4 asks for, so it has to be a
        /// symbol and not more words. ◇/◆ could not be one: LiberationSans.ttf is
        /// the only face in the project, its cmap has no U+25C7 or U+25C6, and
        /// both drew as the same empty box — which left colour doing the work
        /// alone, exactly what the rule forbids. ◊/♦ are in the face and keep the
        /// hollow-then-filled ramp. Do not restore the originals.
        /// </para>
        /// </summary>
        public static string ShortTag(EnvironmentClass e) => e switch
        {
            EnvironmentClass.Calm => "◊ SAKİN", EnvironmentClass.Harsh => "♦ SERT", EnvironmentClass.Extreme => "▲ AŞIRI", _ => "?"
        };

        /// <summary>
        /// The classes in declaration order, which is also weight order. Cached because
        /// <c>Enum.GetValues</c> allocates, and <see cref="Pick"/> runs once per planet.
        /// </summary>
        private static readonly EnvironmentClass[] Ordered =
        {
            EnvironmentClass.Calm, EnvironmentClass.Harsh, EnvironmentClass.Extreme
        };

        /// <summary>
        /// Weighted deterministic pick (60/30/10) on its own hash channel — independent of
        /// Family/Rarity.
        /// <para>
        /// Thresholds are accumulated from <see cref="Weight"/> rather than written out
        /// again. They used to be hardcoded here as <c>roll &lt; 60f</c> / <c>roll &lt; 90f</c>,
        /// which made <see cref="Weight"/> a second, powerless authority: skewing it to
        /// 50/30/20 left the generated distribution at exactly 60/30/10, because nothing
        /// downstream ever read it. Now there is one number for each class and this is
        /// where it lands.
        /// </para>
        /// </summary>
        public static EnvironmentClass Pick(ulong seed, string planetId)
        {
            float roll = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, planetId + "/environment")) * 100f;
            float cumulative = 0f;
            for (int i = 0; i < Ordered.Length; i++)
            {
                cumulative += Weight(Ordered[i]);
                if (roll < cumulative) return Ordered[i];
            }
            return Ordered[Ordered.Length - 1]; // float rounding guard
        }
    }
}
