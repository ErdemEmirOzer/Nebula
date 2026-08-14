using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Progression;

namespace Nebula.Modules
{
    /// <summary>
    /// Orchestrates the single global module-upgrade queue: atomic
    /// start (validate → reserve points → reserve resources, with a full
    /// rollback if either reservation step fails), safety-gated ticking
    /// (pauses while unsafe, resumes after 8 continuous safe seconds),
    /// idempotent completion (tier bump + resource consumption + autosave
    /// hook, exactly once), and cancel (100% point refund, floor(75%)
    /// resource refund). Pure enough to unit-test with a real
    /// <see cref="ProgressionState"/> and <see cref="LogisticsCargo"/> and no
    /// scene at all.
    /// </summary>
    public class ModuleUpgradeService
    {
        private const float SafeResumeSeconds = 8f;

        public ModuleProgressionState Modules { get; }
        private readonly ProgressionState progression;
        private readonly LogisticsCargo cargo;
        private IUpgradeSafetyProvider safety = new AlwaysSafeProvider();

        public ModuleUpgradeJob ActiveJob { get; private set; }

        /// <summary>While true (progression screen open), Tick() advances nothing at all.</summary>
        public bool ScreenPaused { get; set; }

        public event Action<ModuleUpgradeJob> JobStarted;
        /// <summary>Fired exactly once per job, after the tier bump and resource consumption are already applied.</summary>
        public event Action<ModuleUpgradeJob> JobCompleted;
        public event Action<ModuleUpgradeJob> JobCancelled;

        public ModuleUpgradeService(ModuleProgressionState modules, ProgressionState progressionState, LogisticsCargo logisticsCargo)
        {
            Modules = modules;
            progression = progressionState;
            cargo = logisticsCargo;
        }

        public void SetSafetyProvider(IUpgradeSafetyProvider provider) => safety = provider ?? new AlwaysSafeProvider();

        /// <summary>Milestone gates are informational-only in this slice (see ModuleDef.MilestoneLabel) — always satisfied unless a caller wires a real check later.</summary>
        public UpgradeRejectReason TryStartUpgrade(string moduleId, bool milestoneSatisfied = true)
        {
            ModuleDef def = ModuleCatalog.Get(moduleId);
            int currentTier = Modules.GetTier(moduleId);
            int targetTier = currentTier + 1;

            UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(
                def, currentTier, targetTier, progression.Level, milestoneSatisfied,
                ActiveJob != null, progression.DevelopmentPoints, cargo);
            if (reason != UpgradeRejectReason.None) return reason;

            ModuleTierCost cost = def.TierCosts[targetTier - 1];

            // Atomic transaction: points first (cheap/instant to roll back), then resources.
            if (!progression.TrySpendDevelopmentPoints(cost.DevelopmentPoints)) return UpgradeRejectReason.InsufficientPoints;

            var costList = new List<(string itemId, float amount)>(cost.Resources.Length);
            foreach (ModuleResourceCost r in cost.Resources) costList.Add((r.ItemId, r.Amount));

            if (costList.Count > 0 && cargo != null && !cargo.TryReserveAll(ModuleUpgradeJob.CargoOwnerId, costList))
            {
                progression.RefundDevelopmentPoints(cost.DevelopmentPoints); // full rollback — state unchanged
                return UpgradeRejectReason.InsufficientResources;
            }

            ActiveJob = new ModuleUpgradeJob(moduleId, currentTier, targetTier, cost.DevelopmentPoints,
                costList.ToArray(), cost.Seconds);
            JobStarted?.Invoke(ActiveJob);
            return UpgradeRejectReason.None;
        }

        /// <summary>Advances the active job by dt (active-simulation time only — callers must never pass wall-clock/offline time).</summary>
        public void Tick(float dt)
        {
            if (ActiveJob == null || ActiveJob.Cancelled || ActiveJob.Completed) return;
            if (ScreenPaused) return;

            bool safe = safety.IsSafe();
            if (!safe)
            {
                ActiveJob.Paused = true;
                ActiveJob.SafeStreakSeconds = 0f;
                return;
            }
            if (ActiveJob.Paused)
            {
                ActiveJob.SafeStreakSeconds += dt;
                if (ActiveJob.SafeStreakSeconds < SafeResumeSeconds) return;
                ActiveJob.Paused = false;
                ActiveJob.SafeStreakSeconds = 0f;
            }

            ActiveJob.RemainingSeconds -= dt;
            if (ActiveJob.RemainingSeconds <= 0f) CompleteActiveJob();
        }

        private void CompleteActiveJob()
        {
            ModuleUpgradeJob job = ActiveJob;
            if (job == null || job.Completed || job.Cancelled) return; // idempotency guard

            job.Completed = true;
            job.RemainingSeconds = 0f;
            if (cargo != null) cargo.ConsumeReservation(ModuleUpgradeJob.CargoOwnerId);
            Modules.SetTier(job.ModuleId, job.ToTier);
            ActiveJob = null;
            JobCompleted?.Invoke(job);
        }

        /// <summary>Cancels the active job: 100% point refund, floor(75%) resource refund. Second call on an already-cancelled/completed job is a safe no-op.</summary>
        public bool CancelActiveJob()
        {
            ModuleUpgradeJob job = ActiveJob;
            if (job == null || job.Cancelled || job.Completed) return false;

            job.Cancelled = true;
            progression.RefundDevelopmentPoints(job.ReservedPoints);
            if (cargo != null) cargo.ConsumeReservationForRefund(ModuleUpgradeJob.CargoOwnerId, 0.75f);
            ActiveJob = null;
            JobCancelled?.Invoke(job);
            return true;
        }

        /// <summary>
        /// Rebuilds the active job from a save snapshot WITHOUT re-validating or
        /// re-spending: the reservation is re-derived from the (already
        /// restored) physical cargo using the same stable owner id, matching
        /// the job's saved reserved-resource list exactly.
        /// </summary>
        public bool RestoreActiveJob(string moduleId, int fromTier, int toTier, int reservedPoints,
            (string itemId, float amount)[] reservedResources, float totalSeconds, float remainingSeconds,
            bool paused, float safeStreakSeconds)
        {
            var job = new ModuleUpgradeJob(moduleId, fromTier, toTier, reservedPoints, reservedResources, totalSeconds)
            {
                RemainingSeconds = Mathf.Max(0f, remainingSeconds),
                Paused = paused,
                SafeStreakSeconds = Mathf.Max(0f, safeStreakSeconds)
            };
            if (reservedResources.Length > 0
                && (cargo == null || !cargo.TrySetReservation(ModuleUpgradeJob.CargoOwnerId,
                    new List<(string, float)>(reservedResources))))
            {
                progression.RefundDevelopmentPoints(reservedPoints);
                ActiveJob = null;
                return false;
            }
            ActiveJob = job;
            return true;
        }
    }
}
