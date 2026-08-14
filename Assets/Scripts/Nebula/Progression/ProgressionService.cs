using System;
using System.Collections.Generic;

namespace Nebula.Progression
{
    /// <summary>Outcome of an award attempt (for HUD notifications).</summary>
    public struct AwardResult
    {
        public bool Awarded;
        public int Xp;
        public int LevelBefore;
        public int LevelAfter;
        public int PointsGained;
        /// <summary>VS-017A.1 §1: the stable event id that was just genuinely (non-duplicate) awarded -- additive, lets a single Awarded subscriber (e.g. telemetry) distinguish which real milestone just committed without a second parallel notification mechanism per milestone kind.</summary>
        public string EventId;
    }

    /// <summary>
    /// Pure progression state: total XP (capped at the L100 threshold), the
    /// derived level, and unspent Development Points (one per level gained,
    /// never lost). Deterministic and testable.
    /// </summary>
    public class ProgressionState
    {
        public long TotalXp { get; private set; }
        public int Level { get; private set; } = 1;
        public int DevelopmentPoints { get; private set; }

        public bool IsMaxLevel => Level >= ProgressionMath.MaxLevel;

        /// <summary>Adds XP, advancing the level and granting one point per level gained.</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0 || IsMaxLevel) return 0;
            int before = Level;
            long next = TotalXp + amount;
            if (next > ProgressionMath.MaxTotalXp) next = ProgressionMath.MaxTotalXp; // never store past cap
            TotalXp = next;
            Level = ProgressionMath.LevelForTotalXp(TotalXp);
            int gained = Level - before;
            DevelopmentPoints += gained;
            return gained;
        }

        public void Restore(long totalXp, int developmentPoints)
        {
            TotalXp = totalXp < 0 ? 0 : (totalXp > ProgressionMath.MaxTotalXp ? ProgressionMath.MaxTotalXp : totalXp);
            Level = ProgressionMath.LevelForTotalXp(TotalXp);
            DevelopmentPoints = developmentPoints < 0 ? 0 : developmentPoints;
        }

        /// <summary>Atomically spends points; returns false (state unchanged) if there aren't enough.</summary>
        public bool TrySpendDevelopmentPoints(int amount)
        {
            if (amount < 0) return false;
            if (amount == 0) return true;
            if (DevelopmentPoints < amount) return false;
            DevelopmentPoints -= amount;
            return true;
        }

        /// <summary>Returns points to the pool (upgrade cancel/rollback). Never goes negative.</summary>
        public void RefundDevelopmentPoints(int amount)
        {
            if (amount <= 0) return;
            DevelopmentPoints += amount;
        }
    }

    /// <summary>Persistent ledger of already-rewarded event IDs — the exploit guard.</summary>
    public class XpLedger
    {
        private readonly HashSet<string> awarded = new HashSet<string>();
        public bool Contains(string id) => awarded.Contains(id);
        public bool Add(string id) => awarded.Add(id);
        public IEnumerable<string> All => awarded;
        public void Clear() => awarded.Clear();
    }

    /// <summary>
    /// Central static progression facade: routes every award through the
    /// ledger (an ID awards at most once, ever), updates the state, mirrors
    /// events to <see cref="SessionLog"/>, and raises <see cref="Awarded"/>
    /// for the HUD. Reset by the bootstrap so runs never leak.
    /// </summary>
    public static class ProgressionService
    {
        public static ProgressionState State { get; private set; } = new ProgressionState();
        public static XpLedger Ledger { get; private set; } = new XpLedger();

        /// <summary>Raised on a genuine (non-duplicate) award, for HUD coalescing.</summary>
        public static event Action<AwardResult> Awarded;

        public static void Reset()
        {
            State = new ProgressionState();
            Ledger = new XpLedger();
            // TF-002: a session's HUD subscribers must never carry over into the
            // next campaign session even if a prior instance somehow skipped its
            // own OnDestroy unsubscribe.
            Awarded = null;
        }

        /// <summary>Awards XP for a stable event ID once. Duplicates give nothing.</summary>
        public static AwardResult Award(string eventId, int xp)
        {
            if (string.IsNullOrEmpty(eventId)) return default;
            if (Ledger.Contains(eventId))
            {
                SessionLog.Log("xp_duplicate", "duplicate", eventId);
                return default;
            }
            Ledger.Add(eventId);

            int before = State.Level;
            int gained = State.AddXp(xp);
            SessionLog.Log("xp_awarded", null, eventId);
            if (gained > 0) SessionLog.Log("level_changed", before + "->" + State.Level, eventId);

            var result = new AwardResult
            {
                Awarded = true, Xp = xp, LevelBefore = before, LevelAfter = State.Level, PointsGained = gained, EventId = eventId
            };
            Awarded?.Invoke(result);
            return result;
        }

        /// <summary>Restore path — seeds ledger + state WITHOUT awarding (never generates XP).</summary>
        public static void Restore(long totalXp, int developmentPoints, IEnumerable<string> ledgerIds)
        {
            State.Restore(totalXp, developmentPoints);
            Ledger.Clear();
            if (ledgerIds != null) foreach (string id in ledgerIds) Ledger.Add(id);
        }
    }
}
