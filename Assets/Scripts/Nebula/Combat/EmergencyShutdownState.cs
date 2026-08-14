using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>
    /// Hull-reaches-0 handling (VS-013 §8): the ship is never destroyed —
    /// only an 8-second Emergency Shutdown countdown, after which the
    /// recovery transition (<see cref="RecoveryResolver"/>) applies. Pure
    /// timer, no MonoBehaviour dependency.
    /// </summary>
    public class EmergencyShutdownState
    {
        public const float ShutdownSeconds = 8f;

        public bool Active { get; private set; }
        public bool RecoveryPending { get; private set; }
        public float RemainingSeconds { get; private set; }

        public void Trigger()
        {
            if (Active || RecoveryPending) return; // never restarts while expiry awaits its recovery owner
            Active = true;
            RemainingSeconds = ShutdownSeconds;
        }

        /// <summary>Returns true exactly once, the tick the countdown reaches 0 — the caller applies the recovery transition then.</summary>
        public bool Tick(float dt)
        {
            if (!Active) return false;
            RemainingSeconds -= dt;
            if (RemainingSeconds > 0f) return false;
            RemainingSeconds = 0f;
            Active = false;
            RecoveryPending = true;
            return true;
        }

        /// <summary>Called only after the recovery transition actually completed.</summary>
        public void CompleteRecovery() => RecoveryPending = false;

        public void RestoreFromSave(bool active, float remainingSeconds)
        {
            Active = active;
            RecoveryPending = false;
            RemainingSeconds = Mathf.Max(0f, remainingSeconds);
        }
    }

    public enum RecoveryPointKind { ActiveCommandCenterPlanet, LastVisitedControlledSystem, SafetyMarker }

    /// <summary>Pure priority resolution + the exact recovery-state fractions (VS-013 §8). Candidate availability is pre-computed by the caller (real system/dominance queries live outside pure C#).</summary>
    public static class RecoveryResolver
    {
        public const float RecoveryHullFraction = 0.25f;
        public const float RecoveryArmorFraction = 0.10f;
        public const float RecoveryShieldFraction = 0f;

        /// <summary>Soft-lock mercy result (VS-013 §8): better stats, and cargo loss is skipped entirely by the caller.</summary>
        public const float SoftLockHullFraction = 0.60f;
        public const float SoftLockArmorFraction = 0.30f;

        public static RecoveryPointKind Resolve(bool hasActiveCommandCenterInSameSystem, bool hasLastVisitedControlledSystem)
        {
            if (hasActiveCommandCenterInSameSystem) return RecoveryPointKind.ActiveCommandCenterPlanet;
            if (hasLastVisitedControlledSystem) return RecoveryPointKind.LastVisitedControlledSystem;
            return RecoveryPointKind.SafetyMarker;
        }
    }

    /// <summary>Pure cargo-loss math (VS-013 §8): 20% of each unreserved, non-protected item; caller pre-filters out Pioneer Kit / reserved inputs / progression-unique items / modules before calling.</summary>
    public static class DefeatCargoLossResolver
    {
        public const float LossFractionPerItem = 0.20f;

        public static System.Collections.Generic.Dictionary<string, float> ComputeLoss(
            System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, float>> unreservedNormalCargo)
        {
            var lost = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var kv in unreservedNormalCargo)
            {
                float loss = kv.Value * LossFractionPerItem;
                if (loss > 0.0001f) lost[kv.Key] = loss;
            }
            return lost;
        }
    }

    /// <summary>
    /// Campaign-scoped soft-lock tracking (VS-013 §8): a sliding 20-active-minute
    /// window of defeat timestamps. Two defeats inside the window mean the
    /// THIRD recovery within that same window also gets the mercy treatment,
    /// on top of the separate "nothing left to lose" condition the caller
    /// checks independently. Never grants XP/reward, never reverses the
    /// enemy's own combat result — this only affects the recovery OUTCOME.
    /// </summary>
    public class RecoverySoftLockState
    {
        public const float WindowActiveMinutes = 20f;
        public const int DefeatsWithinWindowForMercy = 2;

        public float TotalActiveMinutesElapsed { get; private set; }
        private readonly System.Collections.Generic.List<float> recentDefeatTimestamps = new System.Collections.Generic.List<float>();

        /// <summary>VS-013 §8 recovery-priority candidate 2: the most recently Full-materialized system that had at least one Controlled planet at the time. Updated by CombatEncounterDirector whenever a system boots Full.</summary>
        public string LastVisitedControlledSystemId { get; private set; }
        public void NoteVisitedSystem(string systemId, bool hasControlledPlanet)
        {
            if (hasControlledPlanet) LastVisitedControlledSystemId = systemId;
        }

        /// <summary>Only ever advanced by real active (non-paused, non-offline) session time — the caller is responsible for not ticking during demotion/offline.</summary>
        public void TickActiveMinutes(float dt) => TotalActiveMinutesElapsed += dt / 60f;

        private void PruneOldDefeats() => recentDefeatTimestamps.RemoveAll(t => TotalActiveMinutesElapsed - t > WindowActiveMinutes);

        /// <summary>Call once per defeat, BEFORE deciding this recovery's treatment.</summary>
        public bool ShouldApplyMercyForRepeatedDefeats()
        {
            PruneOldDefeats();
            return recentDefeatTimestamps.Count >= DefeatsWithinWindowForMercy;
        }

        public void RecordDefeat()
        {
            PruneOldDefeats();
            recentDefeatTimestamps.Add(TotalActiveMinutesElapsed);
        }

        public void RestoreFromSave(float totalActiveMinutesElapsed, System.Collections.Generic.IEnumerable<float> defeatTimestamps, string lastVisitedControlledSystemId = null)
        {
            TotalActiveMinutesElapsed = totalActiveMinutesElapsed;
            recentDefeatTimestamps.Clear();
            if (defeatTimestamps != null) recentDefeatTimestamps.AddRange(defeatTimestamps);
            if (lastVisitedControlledSystemId != null) LastVisitedControlledSystemId = lastVisitedControlledSystemId;
        }

        public System.Collections.Generic.IReadOnlyList<float> SnapshotDefeatTimestamps() => recentDefeatTimestamps;
    }

    /// <summary>One physical, stable-ID wreckage of lost combat cargo (VS-013 §8) — mirrors Poi.SalvageNode's shape, with its own 30-active-minute lifetime.</summary>
    public class CombatWreckageNode
    {
        public string ItemId;
        public float Total;
        public float Remaining;
        public CombatWreckageNode(string itemId, float total) { ItemId = itemId; Total = total; Remaining = total; }
    }

    public class CombatWreckageState
    {
        public const float LifetimeActiveSeconds = 30f * 60f;

        public string WreckageId { get; }
        public System.Collections.Generic.List<CombatWreckageNode> Nodes { get; } = new System.Collections.Generic.List<CombatWreckageNode>();
        public float RemainingActiveSeconds { get; private set; } = LifetimeActiveSeconds;

        public bool Expired => RemainingActiveSeconds <= 0f || AllNodesEmpty();

        public CombatWreckageState(string wreckageId) { WreckageId = wreckageId; }

        private bool AllNodesEmpty()
        {
            if (Nodes.Count == 0) return false;
            foreach (CombatWreckageNode n in Nodes) if (n.Remaining > 0.0001f) return false;
            return true;
        }

        /// <summary>Only ever ticked while this system is Full and the session is active — never offline, never during demotion (VS-013 §8).</summary>
        public void Tick(float dt) => RemainingActiveSeconds = UnityEngine.Mathf.Max(0f, RemainingActiveSeconds - dt);

        public float Withdraw(string itemId, float maxAmount)
        {
            foreach (CombatWreckageNode n in Nodes)
            {
                if (n.ItemId != itemId || n.Remaining <= 0.0001f) continue;
                float taken = UnityEngine.Mathf.Min(maxAmount, n.Remaining);
                n.Remaining -= taken;
                return taken;
            }
            return 0f;
        }

        public void RestoreFromSave(float remainingActiveSeconds, System.Collections.Generic.IEnumerable<(string itemId, float total, float remaining)> nodes)
        {
            RemainingActiveSeconds = remainingActiveSeconds;
            Nodes.Clear();
            if (nodes == null) return;
            foreach (var (itemId, total, remaining) in nodes)
            {
                var node = new CombatWreckageNode(itemId, total) { Remaining = remaining };
                Nodes.Add(node);
            }
        }
    }
}
