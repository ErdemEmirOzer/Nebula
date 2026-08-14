using System.Collections.Generic;

namespace Nebula.Extraction
{
    public enum AttentionPriority { None, Medium, High, Critical }

    /// <summary>One consolidated per-system attention card (VS-010 §4). Purely derived every call — never persisted, never pauses the game, never auto-decides.</summary>
    public readonly struct AttentionCard
    {
        public readonly string SystemId;
        public readonly AttentionPriority Priority;
        public readonly string Reason;
        public readonly float TimeToLossSeconds;   // tie-break 1 (ascending) — float.MaxValue if not applicable
        public readonly float RaidEtaSeconds;       // tie-break 2 (ascending) — float.MaxValue if not applicable
        public readonly int AffectedControlledPlanets; // tie-break 3 (descending)
        public readonly bool IsLastKnown;

        public AttentionCard(string systemId, AttentionPriority priority, string reason,
            float timeToLossSeconds, float raidEtaSeconds, int affectedControlledPlanets, bool isLastKnown)
        {
            SystemId = systemId; Priority = priority; Reason = reason;
            TimeToLossSeconds = timeToLossSeconds; RaidEtaSeconds = raidEtaSeconds;
            AffectedControlledPlanets = affectedControlledPlanets; IsLastKnown = isLastKnown;
        }
    }

    /// <summary>Real per-system facts an <see cref="AttentionResolver"/> call needs — assembled by the caller from live or delta-snapshot state (never faked).</summary>
    public struct SystemAttentionInputs
    {
        public bool AnyCommandDisabled;
        public bool AnyPlanetContested;
        /// <summary>Seconds remaining before a Contested planet becomes Lost (PlanetDominance.RecoverySeconds - RecoveryTimer). float.MaxValue if none Contested.</summary>
        public float MinRecoveryTimeRemaining;
        /// <summary>Never true this slice — no combat/raid-detection system exists yet (VS-011+). Kept as real input, not hardcoded inside the resolver, so a future provider only needs to fill this in.</summary>
        public bool RaidDetected;
        public float RaidEtaSeconds;
        public bool AllActiveDrillsStalledFull;
        public bool VehicleStrandedNoTarget;
        public int AffectedControlledPlanetCount;
        public bool IsLive;
    }

    /// <summary>
    /// Pure resolver: one system's real facts -&gt; at most one consolidated
    /// attention card. The caller (an orchestration query, not this class)
    /// is responsible for only ever calling this with real data — if no
    /// combat provider exists, <see cref="SystemAttentionInputs.RaidDetected"/>
    /// is simply always false, so High-priority cards never appear rather
    /// than being faked.
    /// </summary>
    public static class AttentionResolver
    {
        public static AttentionCard? ResolveForSystem(string systemId, SystemAttentionInputs i)
        {
            if (i.AnyCommandDisabled || i.AnyPlanetContested)
            {
                string reason = i.AnyCommandDisabled ? "COMMAND DISABLED" : "PLANET CONTESTED";
                return new AttentionCard(systemId, AttentionPriority.Critical, reason,
                    i.MinRecoveryTimeRemaining, float.MaxValue, i.AffectedControlledPlanetCount, !i.IsLive);
            }
            if (i.RaidDetected)
            {
                return new AttentionCard(systemId, AttentionPriority.High, "RAID INBOUND",
                    float.MaxValue, i.RaidEtaSeconds, i.AffectedControlledPlanetCount, !i.IsLive);
            }
            if (i.AllActiveDrillsStalledFull || i.VehicleStrandedNoTarget)
            {
                string reason = i.AllActiveDrillsStalledFull ? "DRILLS FULL — TRANSFER NEEDED" : "VEHICLE STRANDED";
                return new AttentionCard(systemId, AttentionPriority.Medium, reason,
                    float.MaxValue, float.MaxValue, i.AffectedControlledPlanetCount, !i.IsLive);
            }
            return null;
        }

        /// <summary>Priority desc, then time-to-loss asc, then raid ETA asc, then affected-Controlled-count desc, then canonical system id asc — capped at 3.</summary>
        public static List<AttentionCard> TopThree(IEnumerable<AttentionCard> cards)
        {
            var list = new List<AttentionCard>(cards);
            list.Sort((a, b) =>
            {
                int p = b.Priority.CompareTo(a.Priority); if (p != 0) return p;
                int t = a.TimeToLossSeconds.CompareTo(b.TimeToLossSeconds); if (t != 0) return t;
                int r = a.RaidEtaSeconds.CompareTo(b.RaidEtaSeconds); if (r != 0) return r;
                int c = b.AffectedControlledPlanets.CompareTo(a.AffectedControlledPlanets); if (c != 0) return c;
                return string.CompareOrdinal(a.SystemId, b.SystemId);
            });
            return list.Count > 3 ? list.GetRange(0, 3) : list;
        }
    }
}
