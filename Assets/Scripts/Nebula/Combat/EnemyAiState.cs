using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>
    /// Mutable per-enemy AI bookkeeping: which phase it's in, its committed
    /// target, the 5 Hz decision cadence accumulator, the Scout Probe's
    /// pause/resume scan timer, and the continuous-escape tracking that
    /// turns Retreat into the terminal Escaped phase. Pure C# — no
    /// MonoBehaviour/Transform access; the MonoBehaviour adapter
    /// (<see cref="EnemyAgent"/>) supplies live distances and calls
    /// <see cref="MakeDecision"/> only when <see cref="DecisionDue"/> says so.
    /// </summary>
    public class EnemyAiState
    {
        public EnemyDef Def { get; }
        public EnemyAiPhase Phase { get; private set; } = EnemyAiPhase.Acquire;
        public string TargetId { get; private set; }

        private float decisionAccumulator;
        private float targetCommitElapsed;
        private float scanElapsed;
        private float continuousEscapeSeconds;

        public float TargetCommitElapsed => targetCommitElapsed;
        public float ScanElapsed => scanElapsed;
        public bool ScanComplete => Def.ScanSeconds > 0f && scanElapsed >= Def.ScanSeconds - 0.0001f;

        public EnemyAiState(EnemyDef def) { Def = def; }

        /// <summary>No-op while still committed to the current target (VS-013 §6: at least 5s per target).</summary>
        public void AcquireTarget(string targetId)
        {
            if (TargetId != null && targetCommitElapsed < EnemyCatalog.MinimumTargetCommitSeconds) return;
            TargetId = targetId;
            targetCommitElapsed = 0f;
        }

        public void ClearTarget()
        {
            TargetId = null;
            targetCommitElapsed = 0f;
        }

        /// <summary>Advances the shared 5 Hz decision clock; returns true on the tick a new decision should be made.</summary>
        public bool DecisionDue(float dt)
        {
            decisionAccumulator += dt;
            targetCommitElapsed += dt;
            if (decisionAccumulator + 1e-6f >= EnemyCatalog.DecisionIntervalSeconds)
            {
                decisionAccumulator -= EnemyCatalog.DecisionIntervalSeconds;
                return true;
            }
            return false;
        }

        /// <summary>Scout Probe only. Pause-not-reset, matching this project's established timer convention (ScoutScanWork/SignalAnalysisState/PoiState).</summary>
        public void TickScan(float dt, bool conditionMet)
        {
            if (Def.ScanSeconds <= 0f || !conditionMet) return;
            scanElapsed = Mathf.Min(Def.ScanSeconds, scanElapsed + dt);
        }

        /// <summary>exitAttackOrScanToRetreat is caller-supplied and type-specific: Probe passes ScanComplete, Fighter passes (hullFraction &lt;= Def.RetreatHullFraction).</summary>
        public void MakeDecision(float distanceToTarget, bool targetLost, bool exitAttackOrScanToRetreat)
        {
            Phase = EnemyAiResolver.Resolve(Def, Phase, distanceToTarget, targetLost, exitAttackOrScanToRetreat);
        }

        /// <summary>Only meaningful while Phase == Retreat; Escaped is the only phase this class ever sets outside the shared resolver (VS-013 §6's own explicit distance+continuous-time rule).</summary>
        public void TickRetreatEscape(float dt, float distanceToTarget, bool stillInSensorRange)
        {
            if (Phase != EnemyAiPhase.Retreat) { continuousEscapeSeconds = 0f; return; }

            bool clear = distanceToTarget >= EnemyCatalog.RetreatEscapeDistance && !stillInSensorRange;
            continuousEscapeSeconds = clear ? continuousEscapeSeconds + dt : 0f;

            if (continuousEscapeSeconds >= EnemyCatalog.RetreatEscapeContinuousSeconds) Phase = EnemyAiPhase.Escaped;
        }

        public void RestoreFromSave(EnemyAiPhase phase, string targetId, float scanElapsedSaved, float continuousEscapeSecondsSaved)
        {
            Phase = phase; TargetId = targetId;
            scanElapsed = Mathf.Clamp(scanElapsedSaved, 0f, Def.ScanSeconds > 0f ? Def.ScanSeconds : 0f);
            continuousEscapeSeconds = Mathf.Max(0f, continuousEscapeSecondsSaved);
            targetCommitElapsed = EnemyCatalog.MinimumTargetCommitSeconds; // never re-locks the load-time target artificially early
        }
    }
}
