namespace Nebula.Progression
{
    /// <summary>
    /// Pure, deterministic XP↔level math (no Unity dependency, fully testable).
    /// Cost to go from level L to L+1 is roundToNearest10(100 + 20L + 2L²).
    /// Level is always derived from total XP; the campaign runs L1..L100.
    /// </summary>
    public static class ProgressionMath
    {
        public const int MaxLevel = 100;

        /// <summary>XP to advance from <paramref name="level"/> to level+1 (0 outside 1..99).</summary>
        public static long XpForLevel(int level)
        {
            if (level < 1 || level >= MaxLevel) return 0;
            long raw = 100 + 20L * level + 2L * level * level;
            return (raw + 5) / 10 * 10; // round to nearest 10 (half up)
        }

        /// <summary>Total XP needed to have reached <paramref name="level"/> (0 for L1).</summary>
        public static long TotalXpToReach(int level)
        {
            if (level <= 1) return 0;
            if (level > MaxLevel) level = MaxLevel;
            long sum = 0;
            for (int k = 1; k < level; k++) sum += XpForLevel(k);
            return sum;
        }

        /// <summary>Total XP at the level cap — the maximum ever stored.</summary>
        public static long MaxTotalXp => TotalXpToReach(MaxLevel);

        public static int LevelForTotalXp(long totalXp)
        {
            int level = 1;
            while (level < MaxLevel && totalXp >= TotalXpToReach(level + 1)) level++;
            return level;
        }

        /// <summary>XP accumulated inside the current level.</summary>
        public static long XpIntoLevel(long totalXp)
        {
            int level = LevelForTotalXp(totalXp);
            long clamped = totalXp > MaxTotalXp ? MaxTotalXp : totalXp;
            return clamped - TotalXpToReach(level);
        }
    }

    /// <summary>Stable content IDs and XP amounts for the unique progression events.</summary>
    public static class ProgressionEvents
    {
        public const int ScanXp = 100;
        public const int FamilyXp = 300;
        public const int DrillXp = 200;
        public const int RecipeXp = 150;
        public const int CommandXp = 250;
        public const int DefenseXp = 200;
        public const int DominanceXp = 1200;
        /// <summary>VS-015 §8: a successful, player-manually-started Engineer maintenance/recovery mission or Escort protection mission — 300 XP each, awarded at most once per mission id.</summary>
        public const int EngineerMissionXp = 300;
        public const int EscortMissionXp = 300;

        public static string Scan(string worldId) => "scan:" + worldId;
        public static string Family(string familyId) => "resource-family:" + familyId;
        public static string DrillFirst(string worldId) => "drill:first:" + worldId;
        public static string RecipeFirst(string recipeId) => "recipe:first:" + recipeId;
        public static string CommandFirst(string worldId) => "command:first:" + worldId;
        public static string DefenseInnerFirst(string worldId) => "defense:inner:first:" + worldId;
        public static string DominanceFirst(string worldId) => "dominance:first:" + worldId;
        public static string EngineerMission(string missionId) => "engineer-mission:" + missionId;
        public static string EscortMission(string missionId) => "escort-mission:" + missionId;
    }
}
