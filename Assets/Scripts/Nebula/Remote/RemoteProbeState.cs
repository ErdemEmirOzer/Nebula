namespace Nebula.Remote
{
    /// <summary>VS-016A §8: a remote system's own single Probe encounter -- deliberately NOT routed through ThreatPressureState/RaidStage (Probe is not a raid stage; it never occupies the Major Raid slot, only its own separate global slot, RaidCampaignState.ActiveProbeRaidSystemId).</summary>
    public enum RemoteProbePhase { None, Approaching, Scanning, Retreating, Resolved }

    /// <summary>Pure per-system state for one Probe encounter -- much smaller than a full raid (single Scout-Probe-Mk1 enemy, no composition/budget/target-planet-dominance mechanic, no XP/salvage).</summary>
    public class RemoteProbeState
    {
        public RemoteProbePhase Phase { get; private set; } = RemoteProbePhase.None;
        public string EnemyId { get; private set; }
        public bool ScanCompleted { get; private set; }

        public void Schedule(string enemyId)
        {
            Phase = RemoteProbePhase.Approaching;
            EnemyId = enemyId;
        }

        public void SetPhase(RemoteProbePhase phase) => Phase = phase;
        public void MarkScanCompleted() => ScanCompleted = true;
        public void ClearToNone() { Phase = RemoteProbePhase.None; EnemyId = null; ScanCompleted = false; }

        public static RemoteProbeState Restore(RemoteProbePhase phase, string enemyId, bool scanCompleted)
        {
            var s = new RemoteProbeState();
            s.Phase = phase; s.EnemyId = enemyId; s.ScanCompleted = scanCompleted;
            return s;
        }
    }
}
