namespace Nebula.Combat
{
    public enum FirstProbeStatus { NotScheduled, WarningActive, ProbeActive, Resolved }

    /// <summary>
    /// Campaign-scoped, exactly-once Scout Probe introduction (VS-013 §7):
    /// a 150-active-second warning starts the first time the player has an
    /// active planetary defense, then the Probe encounter itself begins.
    /// Pure C#, save/load restorable, and the single source of truth behind
    /// Shield Systems' "First AI Threat" milestone predicate
    /// (<see cref="ThreatKnown"/>) — never a second copy of that flag.
    /// </summary>
    public class FirstProbeState
    {
        public const float WarningSeconds = 150f;

        public FirstProbeStatus Status { get; private set; } = FirstProbeStatus.NotScheduled;
        public float WarningRemainingSeconds { get; private set; }

        /// <summary>True from the instant the warning is scheduled (a real, known threat) — the "defense.shield_systems" milestone predicate reads this directly.</summary>
        public bool ThreatKnown { get; private set; }

        /// <summary>Set at most once per campaign (Wreck/Facility-style single non-accumulating flag) when the Probe scans successfully — VS-014's own consumer, never re-armed by a duplicate resolution.</summary>
        public string PendingReconEffect { get; private set; }

        /// <summary>No-op if a warning was already scheduled or the encounter already resolved — exactly-once per campaign.</summary>
        public void ScheduleWarningIfNeeded()
        {
            if (Status != FirstProbeStatus.NotScheduled) return;
            Status = FirstProbeStatus.WarningActive;
            WarningRemainingSeconds = WarningSeconds;
            ThreatKnown = true;
        }

        /// <summary>
        /// Ticks only while WarningActive (campaign-wide, independent of which
        /// system is Full). Returns true exactly once, the single tick the
        /// warning expires — the caller spawns the Probe then. Moves Status to
        /// ProbeActive on that same tick so a caller that ticks this every
        /// frame for the rest of the campaign (as CombatCampaignDirector does)
        /// can never spawn a second Probe — the guard is Status itself, not
        /// caller discipline.
        /// </summary>
        public bool TickWarning(float dt)
        {
            if (Status != FirstProbeStatus.WarningActive) return false;
            WarningRemainingSeconds -= dt;
            if (WarningRemainingSeconds > 0f) return false;
            WarningRemainingSeconds = 0f;
            Status = FirstProbeStatus.ProbeActive;
            return true;
        }

        /// <summary>Called exactly once when the Probe encounter concludes (destroyed or scan-completed-and-escaped). probeScanned marks the recon effect VS-014 will consume; a destroyed-before-scanning Probe leaves none.</summary>
        public void MarkResolved(bool probeScanned)
        {
            if (Status == FirstProbeStatus.Resolved) return;
            Status = FirstProbeStatus.Resolved;
            if (probeScanned && PendingReconEffect == null) PendingReconEffect = "vs014.recon.pending";
        }

        /// <summary>Reads and clears the flag in one step — VS-014's own consumer never sees it twice.</summary>
        public string ConsumePendingReconEffect()
        {
            string value = PendingReconEffect;
            PendingReconEffect = null;
            return value;
        }

        public void RestoreFromSave(FirstProbeStatus status, float warningRemainingSeconds, bool threatKnown, string pendingReconEffect)
        {
            Status = status;
            WarningRemainingSeconds = warningRemainingSeconds;
            ThreatKnown = threatKnown;
            PendingReconEffect = pendingReconEffect;
        }
    }
}
