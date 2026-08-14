using System.Collections.Generic;

namespace Nebula.Sectors
{
    /// <summary>
    /// Decides, for one distant system on one cycle, whether it is evaluated now and
    /// with how many simulated seconds (P2-TIME-002).
    /// <para>
    /// The distant tick's schedule has two rates, and the project's comments claimed
    /// only one of them for years:
    /// </para>
    /// <list type="bullet">
    ///   <item>A system with anything in flight is evaluated <b>every cycle — 1 Hz</b>.</item>
    ///   <item>A system that has been calm for <see cref="CalmSkipThreshold"/>
    ///   consecutive evaluations is evaluated <b>once per <see cref="CalmSkipEvery"/>
    ///   cycles — 0.1 Hz</b>, because evaluating an idle system is a guaranteed no-op
    ///   whose cost scales with how much galaxy the player has discovered.</item>
    /// </list>
    /// <para>
    /// Only the evaluation rate differs. Simulated time does not: a skipped cycle's
    /// seconds are carried and handed to the evaluation that runs, so a calm system's
    /// drills, raids and vehicle tasks advance by exactly the elapsed wall time at
    /// either rate. That is what makes this a scheduler decision rather than a
    /// difficulty setting, and why remote results match a Full system's.
    /// </para>
    /// <para>
    /// It is a separate pure type so the schedule can be measured directly instead of
    /// inferred from a MonoBehaviour's frame loop, and it is the <i>only</i> place the
    /// decision is made — <see cref="SystemStreamingDirector"/> asks it, never its own
    /// copy of the rule.
    /// </para>
    /// </summary>
    public class SparseTickScheduler
    {
        /// <summary>Consecutive calm evaluations before a system drops to the slow rate.</summary>
        public const int CalmSkipThreshold = 10;

        /// <summary>Once slow, a system is evaluated on one cycle in this many.</summary>
        public const int CalmSkipEvery = 10;

        private readonly Dictionary<string, int> calmStreak = new Dictionary<string, int>();
        private readonly Dictionary<string, float> deferredSeconds = new Dictionary<string, float>();

        /// <summary>Wipes every system's schedule state (a fresh boot or a load).</summary>
        public void Clear()
        {
            calmStreak.Clear();
            deferredSeconds.Clear();
        }

        /// <summary>Drops one system's schedule state, so a later re-discovery starts clean instead of inheriting stale deferred seconds.</summary>
        public void Forget(string systemId)
        {
            calmStreak.Remove(systemId);
            deferredSeconds.Remove(systemId);
        }

        /// <summary>How many consecutive calm evaluations this system has had. Diagnostic only.</summary>
        public int CalmStreakOf(string systemId) => calmStreak.TryGetValue(systemId, out int s) ? s : 0;

        /// <summary>Seconds currently held back for a system that is being skipped. Diagnostic only.</summary>
        public float DeferredSecondsOf(string systemId) => deferredSeconds.TryGetValue(systemId, out float d) ? d : 0f;

        /// <summary>True while this system is on the slow (0.1 Hz) rate.</summary>
        public bool IsSlowRate(string systemId) => CalmStreakOf(systemId) >= CalmSkipThreshold;

        /// <summary>
        /// Accrues <paramref name="elapsedSeconds"/> for a system and answers whether it
        /// runs on this cycle.
        /// <para>
        /// When it does, <paramref name="secondsToApply"/> is every second accrued since
        /// its last evaluation — never a multiple of the cycle length assumed from the
        /// rate. That distinction is the bug this type was extracted to fix: the old
        /// code multiplied by <see cref="CalmSkipEvery"/> whenever a system was on the
        /// slow rate, which was right in the steady state and wrong on the way into it.
        /// A system whose streak crossed the threshold at cycle 97 was skipped for 98
        /// and 99 and then handed ten seconds for three seconds of real time — up to
        /// nine seconds of free drill output, once per entry into calm.
        /// </para>
        /// </summary>
        public bool ShouldRun(string systemId, long cycleIndex, float elapsedSeconds, out float secondsToApply)
        {
            float pending = DeferredSecondsOf(systemId) + elapsedSeconds;

            if (IsSlowRate(systemId) && cycleIndex % CalmSkipEvery != 0)
            {
                deferredSeconds[systemId] = pending;
                secondsToApply = 0f;
                return false;
            }

            deferredSeconds[systemId] = 0f;
            secondsToApply = pending;
            return true;
        }

        /// <summary>Records what the evaluation found: a calm system advances its streak toward the slow rate, a busy one drops straight back to 1 Hz.</summary>
        public void NoteEvaluated(string systemId, bool calm) =>
            calmStreak[systemId] = calm ? CalmStreakOf(systemId) + 1 : 0;
    }
}
