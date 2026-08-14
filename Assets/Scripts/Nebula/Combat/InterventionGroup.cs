using System.Collections.Generic;

namespace Nebula.Combat
{
    /// <summary>VS-015 §6: how the local same-system intervention group behaves.</summary>
    public enum InterventionPolicy { Manual, DefendInPlace, SystemIntervention }

    /// <summary>
    /// VS-015 §6: pure per-system state for the single physical intervention
    /// group a system may have — Engineer and/or Escort vehicles, never a
    /// persistent fleet/formation, never cross-system. Membership/target are
    /// plain vehicle/planet ids; the director resolves and moves them.
    /// </summary>
    public class InterventionGroup
    {
        public string SystemId { get; }
        public InterventionPolicy Policy { get; private set; }
        public string ActiveTargetPlanetId { get; private set; }
        public bool HoldingOrRetreating { get; private set; }
        /// <summary>VS-015.1 §1: the last-computed known threat power for the active target (or 0 with none) — frozen exactly as-is across a demote, never recomputed with fresher data while not the Full system.</summary>
        public float KnownThreatPowerSnapshot { get; private set; }

        private readonly List<string> memberVehicleIds = new List<string>();
        public IReadOnlyList<string> MemberVehicleIds => memberVehicleIds;

        public InterventionGroup(string systemId)
        {
            SystemId = systemId;
            Policy = InterventionPolicy.Manual;
        }

        public bool AddMember(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId) || memberVehicleIds.Contains(vehicleId)) return false;
            memberVehicleIds.Add(vehicleId);
            return true;
        }

        public void RemoveMember(string vehicleId) => memberVehicleIds.Remove(vehicleId);
        public void SetPolicy(InterventionPolicy policy) => Policy = policy;
        public void SetActiveTarget(string planetId) => ActiveTargetPlanetId = planetId;
        public void ClearActiveTarget() => ActiveTargetPlanetId = null;
        public void SetHoldingOrRetreating(bool value) => HoldingOrRetreating = value;
        public void SetKnownThreatSnapshot(float value) => KnownThreatPowerSnapshot = value;
        public bool IsEmpty => memberVehicleIds.Count == 0;

        /// <summary>Restore-only: rebuilds exact saved state (members restored in saved order, ids never re-minted).</summary>
        public static InterventionGroup Restore(string systemId, InterventionPolicy policy, IEnumerable<string> memberVehicleIds,
            string activeTargetPlanetId, bool holdingOrRetreating, float knownThreatPowerSnapshot)
        {
            var group = new InterventionGroup(systemId);
            group.SetPolicy(policy);
            if (memberVehicleIds != null) foreach (string id in memberVehicleIds) group.AddMember(id);
            group.SetActiveTarget(activeTargetPlanetId);
            group.SetHoldingOrRetreating(holdingOrRetreating);
            group.SetKnownThreatSnapshot(knownThreatPowerSnapshot);
            return group;
        }
    }

    /// <summary>One candidate Controlled planet the intervention group could respond to.</summary>
    public readonly struct InterventionCandidate
    {
        public readonly string PlanetId;
        public readonly bool CommandThreatened;
        public readonly bool SiegeOrInvasion;
        public readonly float EngagementAgeSeconds;
        public readonly float EtaSeconds;
        public readonly bool ThreatIsProbeOnly;
        public readonly float KnownThreatPower;

        public InterventionCandidate(string planetId, bool commandThreatened, bool siegeOrInvasion,
            float engagementAgeSeconds, float etaSeconds, bool threatIsProbeOnly, float knownThreatPower)
        {
            PlanetId = planetId; CommandThreatened = commandThreatened; SiegeOrInvasion = siegeOrInvasion;
            EngagementAgeSeconds = engagementAgeSeconds; EtaSeconds = etaSeconds;
            ThreatIsProbeOnly = threatIsProbeOnly; KnownThreatPower = knownThreatPower;
        }
    }

    /// <summary>
    /// VS-015 §6: pure decision logic for the "System Intervention" policy —
    /// only Controlled same-system targets, priority Command-threat &gt;
    /// Siege/Invasion &gt; earliest engagement &gt; closest ETA &gt; canonical
    /// planet id, and the solo-Engineer / no-Escort safety gates.
    /// </summary>
    public static class InterventionGroupResolver
    {
        public static string SelectTarget(IReadOnlyList<InterventionCandidate> candidates, float knownGroupPower)
        {
            InterventionCandidate? best = null;
            foreach (InterventionCandidate c in candidates ?? System.Array.Empty<InterventionCandidate>())
            {
                if (c.KnownThreatPower > knownGroupPower) continue; // group power must not be below known threat
                if (best == null || IsHigherPriority(c, best.Value)) best = c;
            }
            return best?.PlanetId;
        }

        private static bool IsHigherPriority(InterventionCandidate a, InterventionCandidate b)
        {
            if (a.CommandThreatened != b.CommandThreatened) return a.CommandThreatened;
            if (a.SiegeOrInvasion != b.SiegeOrInvasion) return a.SiegeOrInvasion;
            if (System.Math.Abs(a.EngagementAgeSeconds - b.EngagementAgeSeconds) > 0.0001f) return a.EngagementAgeSeconds > b.EngagementAgeSeconds;
            if (System.Math.Abs(a.EtaSeconds - b.EtaSeconds) > 0.0001f) return a.EtaSeconds < b.EtaSeconds;
            return string.CompareOrdinal(a.PlanetId, b.PlanetId) < 0;
        }

        /// <summary>An Engineer-only group (no Escort) never engages anything but a pure Probe threat.</summary>
        public static bool MaySoloEngineerEngage(bool threatIsProbeOnly) => threatIsProbeOnly;

        /// <summary>With zero Escorts and a non-Probe threat, the group holds at a safe distance or retreats instead of engaging.</summary>
        public static bool ShouldHoldOrRetreat(int escortCount, bool threatIsProbeOnly) => escortCount < 1 && !threatIsProbeOnly;
    }
}
