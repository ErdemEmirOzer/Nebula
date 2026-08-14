using System.Collections.Generic;

namespace Nebula.Combat
{
    /// <summary>VS-014 §9: changeable only outside combat, saved, never auto-changed — base enemy stats never vary by difficulty, only budget/pressure-rate/warning per §9.</summary>
    public enum RaidDifficulty { Explorer, Commander, Conqueror }

    /// <summary>VS-014 §8: campaign-lifetime record of one resolved/scheduled raid, kept for the last-5 anti-repeat check (§9: same doctrine/leader/planet at most twice consecutively when an alternative exists; never the exact same composition).</summary>
    public class RaidHistoryEntry
    {
        public RaidStage Stage;
        public RaidDoctrine Doctrine;
        public string LeaderDefId;
        public string PlanetWorldId;
        public List<string> CompositionDefIds = new List<string>();
    }

    /// <summary>VS-014 §14: the single Protected Pirate Base ledger — exactly one ever exists in a campaign.</summary>
    public class PirateBaseLedger
    {
        public bool Spawned;
        public string PoiId;
        public string SystemId;
        public bool Resolved;
        public bool RewardAwarded;
    }

    /// <summary>
    /// Campaign-scoped raid registry (VS-014 §8) — mirrors
    /// <see cref="CombatCampaignState"/>'s own static-holder shape and is
    /// reset alongside it. Owns the ONE cross-system authority a per-Full-
    /// system <see cref="ThreatPressureState"/> cannot: the global "at most
    /// one major raid" cap (a raid frozen in a demoted, non-Full system
    /// still occupies the slot), which enemy classes the player has been
    /// taught, the last-5 anti-repeat history, and the single persistent
    /// Pirate Base ledger.
    /// </summary>
    public class RaidCampaignState
    {
        public RaidDifficulty Difficulty { get; private set; } = RaidDifficulty.Commander;

        /// <summary>Non-null while a Pirate/Assault/Siege raid is scheduled/warning/spawning/active anywhere (even a demoted, frozen system) — the global major-raid cap authority.</summary>
        public string ActiveMajorRaidSystemId { get; private set; }
        public bool HasGlobalMajorRaidActive => ActiveMajorRaidSystemId != null;

        /// <summary>VS-016A §8: the global "at most one Probe encounter anywhere" slot — additive to, and independent of, the Major Raid slot above (a system may have a Major Raid AND a Probe encounter active in two different systems at once, never two Probes).</summary>
        public string ActiveProbeRaidSystemId { get; private set; }
        public bool HasGlobalProbeRaidActive => ActiveProbeRaidSystemId != null;

        private readonly HashSet<string> taughtEnemyDefIds = new HashSet<string>();
        public IReadOnlyCollection<string> TaughtEnemyDefIds => taughtEnemyDefIds;
        public bool IsTaught(string enemyDefId) => !string.IsNullOrEmpty(enemyDefId) && taughtEnemyDefIds.Contains(enemyDefId);
        public void MarkTaught(string enemyDefId) { if (!string.IsNullOrEmpty(enemyDefId)) taughtEnemyDefIds.Add(enemyDefId); }

        private readonly List<RaidHistoryEntry> history = new List<RaidHistoryEntry>();
        /// <summary>Newest last, capped at 5 (§9/§18's "last-5 anti-repeat").</summary>
        public IReadOnlyList<RaidHistoryEntry> History => history;

        public PirateBaseLedger PirateBase { get; private set; } = new PirateBaseLedger();

        public bool TryStartGlobalMajorRaid(string systemId)
        {
            if (HasGlobalMajorRaidActive || string.IsNullOrEmpty(systemId)) return false;
            ActiveMajorRaidSystemId = systemId;
            return true;
        }

        /// <summary>No-op if `systemId` isn't the current holder — never lets a stale/duplicate resolve call release someone else's slot.</summary>
        public void EndGlobalMajorRaid(string systemId)
        {
            if (ActiveMajorRaidSystemId == systemId) ActiveMajorRaidSystemId = null;
        }

        public bool TryStartGlobalProbeRaid(string systemId)
        {
            if (HasGlobalProbeRaidActive || string.IsNullOrEmpty(systemId)) return false;
            ActiveProbeRaidSystemId = systemId;
            return true;
        }

        /// <summary>No-op if `systemId` isn't the current holder — same non-stomping guarantee as EndGlobalMajorRaid.</summary>
        public void EndGlobalProbeRaid(string systemId)
        {
            if (ActiveProbeRaidSystemId == systemId) ActiveProbeRaidSystemId = null;
        }

        /// <summary>Additive restore, called separately from RestoreFromSave so that method's existing signature/call site never needs to change.</summary>
        public void RestoreProbeSlot(string activeProbeRaidSystemId) => ActiveProbeRaidSystemId = activeProbeRaidSystemId;

        public void RecordHistory(RaidStage stage, RaidDoctrine doctrine, string leaderDefId, string planetWorldId, IEnumerable<string> compositionDefIds)
        {
            var entry = new RaidHistoryEntry { Stage = stage, Doctrine = doctrine, LeaderDefId = leaderDefId, PlanetWorldId = planetWorldId };
            if (compositionDefIds != null) entry.CompositionDefIds.AddRange(compositionDefIds);
            history.Add(entry);
            while (history.Count > 5) history.RemoveAt(0);
        }

        /// <summary>Caller enforces "outside combat" — this is a pure setter with no gameplay gate of its own.</summary>
        public void SetDifficulty(RaidDifficulty difficulty) => Difficulty = difficulty;

        public void MarkPirateBaseSpawned(string poiId, string systemId)
        {
            if (PirateBase.Spawned) return; // exactly once per campaign
            PirateBase.Spawned = true;
            PirateBase.PoiId = poiId;
            PirateBase.SystemId = systemId;
        }

        public void MarkPirateBaseResolved()
        {
            if (!PirateBase.Spawned) return;
            PirateBase.Resolved = true;
        }

        public void MarkPirateBaseRewardAwarded()
        {
            if (!PirateBase.Resolved) return;
            PirateBase.RewardAwarded = true;
        }

        public void RestoreFromSave(RaidDifficulty difficulty, string activeMajorRaidSystemId, IEnumerable<string> taught,
            IEnumerable<RaidHistoryEntry> historyEntries,
            bool pirateBaseSpawned, string pirateBasePoiId, string pirateBaseSystemId, bool pirateBaseResolved, bool pirateBaseRewardAwarded)
        {
            Difficulty = difficulty;
            ActiveMajorRaidSystemId = activeMajorRaidSystemId;
            taughtEnemyDefIds.Clear();
            if (taught != null) foreach (string t in taught) taughtEnemyDefIds.Add(t);
            history.Clear();
            if (historyEntries != null) history.AddRange(historyEntries);
            PirateBase = new PirateBaseLedger
            {
                Spawned = pirateBaseSpawned, PoiId = pirateBasePoiId, SystemId = pirateBaseSystemId,
                Resolved = pirateBaseResolved, RewardAwarded = pirateBaseRewardAwarded
            };
        }
    }
}
