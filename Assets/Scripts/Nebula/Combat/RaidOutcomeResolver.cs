using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>VS-014 §13: Failure &gt; PlayerDefeatedDefenseWon &gt; Costly &gt; Decisive, in that priority order — a lost target always overrides everything else, and an explicit player shutdown always reads as the 50% case regardless of how the fight otherwise went.</summary>
    public enum RaidOutcome { DecisiveVictory, CostlyVictory, PlayerDefeatedDefenseWon, Failure }

    /// <summary>
    /// VS-014 §13: pure raid result -> XP math. A raid's XP is exactly one
    /// ledger event (the caller passes a per-raid-unique key into
    /// Progression.ProgressionService.Award, whose own key-based dedup — the
    /// same mechanism already used for Command/defense/dominance first-XP —
    /// is what makes a save/load replay never re-grant it, not any state
    /// kept here). A single enemy kill, one hit, or raw duration alone never
    /// grants XP; only this one whole-raid result event does.
    /// </summary>
    public static class RaidOutcomeResolver
    {
        public static int BaseXp(RaidStage stage) => stage switch
        {
            RaidStage.Pirate => 400,
            RaidStage.Assault => 900,
            RaidStage.Siege => 1800,
            _ => 0
        };

        public static float OutcomeFraction(RaidOutcome outcome) => outcome switch
        {
            RaidOutcome.DecisiveVictory => 1.00f,
            RaidOutcome.CostlyVictory => 0.75f,
            RaidOutcome.PlayerDefeatedDefenseWon => 0.50f,
            _ => 0f
        };

        /// <summary>
        /// `targetLost` always wins (Failure). Otherwise a real player Emergency
        /// Shutdown during the raid is always the 50% case even if the defenses
        /// still ultimately won cleanly afterward. Otherwise the target ever
        /// having gone Contested, or the Looter successfully stealing anything,
        /// marks the win as costly rather than decisive.
        /// </summary>
        public static RaidOutcome Resolve(bool targetLost, bool playerWasShutdownDuringRaid, bool wasTargetContestedDuringRaid, bool anyResourceStolen)
        {
            if (targetLost) return RaidOutcome.Failure;
            if (playerWasShutdownDuringRaid) return RaidOutcome.PlayerDefeatedDefenseWon;
            if (wasTargetContestedDuringRaid || anyResourceStolen) return RaidOutcome.CostlyVictory;
            return RaidOutcome.DecisiveVictory;
        }

        /// <summary>Zero unless the player made a real, meaningful contribution (§13) — a raid the defenses won entirely on their own with the player never engaging at all grants nothing.</summary>
        public static int ResolveXp(RaidStage stage, RaidOutcome outcome, bool hasMeaningfulParticipation)
        {
            if (!hasMeaningfulParticipation) return 0;
            return Mathf.RoundToInt(BaseXp(stage) * OutcomeFraction(outcome));
        }
    }
}
