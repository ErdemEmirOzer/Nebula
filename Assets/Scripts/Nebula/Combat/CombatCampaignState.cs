namespace Nebula.Combat
{
    /// <summary>
    /// Static, campaign-scoped combat registry (VS-013) — mirrors the exact
    /// shape of <see cref="Nebula.Logistics.LogisticsState"/>/<see cref="Nebula.Poi.PoiLiveState"/>:
    /// reset once per new campaign/load by <see cref="Nebula.World.Composition.WorldInstaller"/>,
    /// captured/restored as top-level <see cref="Extraction.SavePayload"/>
    /// fields (never per-system — this is ship/campaign-owned progression,
    /// not materialized-system state). Never a second gameplay/save
    /// authority: everything here is exactly what gets captured to v11 DTOs
    /// and nothing else reads or writes it in parallel.
    /// </summary>
    public static class CombatCampaignState
    {
        public static FirstProbeState FirstProbe { get; private set; } = new FirstProbeState();
        public static TrainingProgress Training { get; private set; } = new TrainingProgress();
        public static RecoverySoftLockState Recovery { get; private set; } = new RecoverySoftLockState();
        /// <summary>VS-014 §8: campaign-lifetime raid registry (difficulty, global major-raid cap, taught classes, history, Pirate Base ledger).</summary>
        public static RaidCampaignState RaidCampaign { get; private set; } = new RaidCampaignState();

        public static void Reset()
        {
            FirstProbe = new FirstProbeState();
            Training = new TrainingProgress();
            Recovery = new RecoverySoftLockState();
            RaidCampaign = new RaidCampaignState();
        }
    }
}
