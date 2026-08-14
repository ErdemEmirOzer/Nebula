namespace Nebula.Combat
{
    /// <summary>Acquire→Approach→Attack/Scan→Break→Reposition→Re-engage→Retreat→Escaped (VS-013 §6) -- shared by every real AI-driven enemy this slice.</summary>
    public enum EnemyAiPhase { Acquire, Approach, AttackOrScan, Break, Reposition, ReEngage, Retreat, Escaped }

    /// <summary>
    /// The pure, shared decision graph both <see cref="EnemyCatalog.ScoutProbeMk1"/>
    /// and <see cref="EnemyCatalog.FighterMk1"/> run through, driven entirely by
    /// <see cref="EnemyDef"/> data plus the caller's live geometry/timers — no
    /// per-type branching lives inside this resolver itself, only the exit
    /// condition for leaving AttackOrScan (scan-complete for the Probe,
    /// low-hull for the Fighter) is genuinely type-specific, so that single
    /// bool is supplied by the caller rather than computed here. No
    /// teleport, no instant heading snap, no hidden stat/vision bonus —
    /// this returns only the next PHASE; physical movement is a separate,
    /// obstacle-aware concern (see <see cref="ShipCollisionResolver"/>).
    /// </summary>
    public static class EnemyAiResolver
    {
        public static EnemyAiPhase Resolve(EnemyDef def, EnemyAiPhase phase, float distanceToTarget,
            bool targetLost, bool exitAttackOrScanToRetreat)
        {
            switch (phase)
            {
                case EnemyAiPhase.Acquire:
                    return targetLost ? EnemyAiPhase.Acquire : EnemyAiPhase.Approach;

                case EnemyAiPhase.Approach:
                    if (targetLost) return EnemyAiPhase.Acquire;
                    if (distanceToTarget <= def.BreakDistance) return EnemyAiPhase.Break;
                    if (distanceToTarget <= def.EngageBandMax) return EnemyAiPhase.AttackOrScan;
                    return EnemyAiPhase.Approach;

                case EnemyAiPhase.AttackOrScan:
                    if (targetLost) return EnemyAiPhase.Acquire;
                    if (exitAttackOrScanToRetreat) return EnemyAiPhase.Retreat;
                    if (distanceToTarget <= def.BreakDistance) return EnemyAiPhase.Break;
                    if (distanceToTarget > def.EngageBandMax) return EnemyAiPhase.Reposition;
                    return EnemyAiPhase.AttackOrScan;

                case EnemyAiPhase.Break:
                    if (targetLost) return EnemyAiPhase.Acquire;
                    if (distanceToTarget >= def.EngageBandMin) return EnemyAiPhase.Reposition;
                    return EnemyAiPhase.Break;

                case EnemyAiPhase.Reposition:
                    if (targetLost) return EnemyAiPhase.Acquire;
                    if (distanceToTarget <= def.EngageBandMax && distanceToTarget > def.BreakDistance) return EnemyAiPhase.ReEngage;
                    return EnemyAiPhase.Reposition;

                case EnemyAiPhase.ReEngage:
                    // A one-tick funnel back into the shared attack/scan evaluation above.
                    return EnemyAiPhase.AttackOrScan;

                case EnemyAiPhase.Retreat:
                    return EnemyAiPhase.Retreat; // Escaped is only ever set explicitly by the caller's continuous-distance/time tracking (see EnemyAiState)

                case EnemyAiPhase.Escaped:
                    return EnemyAiPhase.Escaped; // terminal

                default:
                    return phase;
            }
        }
    }
}
