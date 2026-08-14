namespace Nebula.Extraction
{
    public enum DominanceState { Unowned, Developing, Controlled, Contested, Lost }

    /// <summary>The four live control conditions, evaluated each tick by the dominance system.</summary>
    public struct DominanceConditions
    {
        public bool Scanned;

        /// <summary>
        /// This planet has an active drill on it.
        /// <para>
        /// Was <c>ActiveMatchingDrill</c>, and the word "matching" promised a fifth
        /// condition that did not exist (P1-ECO-019): a drill's family is taken from
        /// the planet's own profile in both of <c>DrillRuntime</c>'s constructors, so
        /// "a drill whose family matches this planet's" and "a drill" were the same
        /// set, always. Four conditions that were really three, with the reader left
        /// looking for the difference. The remote path never compared families at all,
        /// so this is also the name the two paths already shared in fact.
        /// </para>
        /// </summary>
        public bool ActiveDrill;

        public bool ActiveCommand;
        public bool ActiveDefense;
        public bool CommandDisabled;

        public bool AllMet => Scanned && ActiveDrill && ActiveCommand && ActiveDefense;
    }

    /// <summary>
    /// Pure per-planet dominance state machine. All conditions met → 120s
    /// active stabilization → Controlled. Losing a condition mid-stabilization
    /// resets the counter. A disabled/absent Command while Controlled →
    /// Contested (drill production stops) with a 300s recovery window: repair
    /// in time re-checks conditions and re-stabilizes; the window expiring →
    /// Lost (structures passivated). Reclaiming from Lost needs command +
    /// defense + a fresh 120s stabilization. No offline progress — advanced
    /// only by real Tick(dt).
    /// </summary>
    public class PlanetDominance
    {
        public const float StabilizeSeconds = 120f;
        public const float RecoverySeconds = 300f;

        public DominanceState State { get; private set; } = DominanceState.Unowned;
        public float StabilizeTimer { get; private set; }
        public float RecoveryTimer { get; private set; }

        /// <summary>
        /// VS-010 §5: set only by physical evacuation, cleared only by
        /// reclaim — a separate overlay flag, never a DominanceState value
        /// of its own. While true, <see cref="DominanceSystem"/> never ticks
        /// this planet at all (State/timers stay exactly as they were at
        /// evacuation) and production/dominance bonuses stay inactive.
        /// </summary>
        public bool Abandoned { get; private set; }

        /// <summary>Drill production is suspended while the planet is Contested, Lost, or Abandoned.</summary>
        public bool ProductionSuspended => State == DominanceState.Contested || State == DominanceState.Lost || Abandoned;

        public void SetAbandoned(bool abandoned) => Abandoned = abandoned;

        /// <summary>Reclaim: clears Abandoned and forces a cold restart (Unowned, timers zeroed) so the next tick always requires a fresh 120s stabilization — never resumes a stale Controlled/Developing state.</summary>
        public void Reclaim()
        {
            Abandoned = false;
            State = DominanceState.Unowned;
            StabilizeTimer = 0f;
            RecoveryTimer = 0f;
        }

        public void Tick(float dt, DominanceConditions c)
        {
            if (Abandoned) return; // frozen — no offline-style progress while abandoned, matches Tick(dt)'s own "no offline progress" rule

            switch (State)
            {
                case DominanceState.Unowned:
                case DominanceState.Developing:
                    if (c.AllMet)
                    {
                        State = DominanceState.Developing;
                        StabilizeTimer += dt;
                        if (StabilizeTimer >= StabilizeSeconds) { State = DominanceState.Controlled; StabilizeTimer = 0f; }
                    }
                    else { State = DominanceState.Unowned; StabilizeTimer = 0f; }
                    break;

                case DominanceState.Controlled:
                    if (!c.ActiveCommand || c.CommandDisabled)
                    {
                        State = DominanceState.Contested;
                        RecoveryTimer = 0f;
                    }
                    break;

                case DominanceState.Contested:
                    RecoveryTimer += dt;
                    if (c.ActiveCommand && !c.CommandDisabled)
                    {
                        // Command restored: re-check and re-stabilize from scratch.
                        RecoveryTimer = 0f;
                        StabilizeTimer = 0f;
                        State = c.AllMet ? DominanceState.Developing : DominanceState.Unowned;
                    }
                    else if (RecoveryTimer >= RecoverySeconds)
                    {
                        State = DominanceState.Lost;
                        RecoveryTimer = 0f;
                    }
                    break;

                case DominanceState.Lost:
                    // Reclaim requires a fresh stabilization once command+defense are back.
                    if (c.AllMet)
                    {
                        State = DominanceState.Developing;
                        StabilizeTimer = 0f;
                    }
                    break;
            }
        }

        public void Restore(DominanceState state, float stabilizeTimer, float recoveryTimer, bool abandoned = false)
        {
            State = state;
            StabilizeTimer = stabilizeTimer;
            RecoveryTimer = recoveryTimer;
            Abandoned = abandoned;
        }
    }
}
