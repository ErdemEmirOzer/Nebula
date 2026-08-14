using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A §8: ticks one remote system's own Probe encounter -- reuses
    /// the exact same EnemyDef (ScoutProbeMk1), EnemyAiState/EnemyAiResolver
    /// phase machine, and Durability/DamageResolver a live Probe already
    /// uses; only the global "at most one Probe anywhere" slot
    /// (RaidCampaignState.ActiveProbeRaidSystemId, additive to the existing
    /// Major Raid slot) and the scan/retreat sequencing are new.
    /// </summary>
    public static class RemoteProbeTick
    {
        public static void Tick(RemoteRaidContext ctx, RemoteProbeState probe, float dt)
        {
            if (ctx == null || probe == null) return;
            RaidCampaignState campaign = CombatCampaignState.RaidCampaign;

            switch (probe.Phase)
            {
                case RemoteProbePhase.None:
                    TryStart(ctx, probe, campaign);
                    break;
                case RemoteProbePhase.Approaching:
                case RemoteProbePhase.Scanning:
                    TickActive(ctx, probe, dt);
                    break;
                case RemoteProbePhase.Retreating:
                    TickRetreat(ctx, probe, dt);
                    break;
                case RemoteProbePhase.Resolved:
                    campaign.EndGlobalProbeRaid(ctx.Delta.systemId);
                    probe.ClearToNone();
                    break;
            }
        }

        private static void TryStart(RemoteRaidContext ctx, RemoteProbeState probe, RaidCampaignState campaign)
        {
            if (ctx.CountControlledPlanets() == 0) return;
            if (campaign.HasGlobalProbeRaidActive) return;
            if (!campaign.TryStartGlobalProbeRaid(ctx.Delta.systemId)) return;

            EnemyDef def = EnemyCatalog.Get(EnemyIds.ScoutProbeMk1);
            string id = $"remote-probe-{ctx.Delta.systemId}-{ctx.Enemies.Count:00}";
            RemoteEnemyRuntime enemy = RemoteEnemyRuntime.Create(id, def, ctx.SpawnPosition(ExtractionState.UniverseSeed, 0, FirstControlledPlanetOrSystem(ctx)));
            ctx.Enemies.Add(enemy);
            probe.Schedule(id);
        }

        private static string FirstControlledPlanetOrSystem(RemoteRaidContext ctx)
        {
            foreach (Sectors.PlanetManifest p in ctx.Manifest.Planets)
                if (ExtractionState.GetDominance(p.PlanetId).State == DominanceState.Controlled) return p.PlanetId;
            return ctx.Delta.systemId + "/pl00";
        }

        private static void TickActive(RemoteRaidContext ctx, RemoteProbeState probe, float dt)
        {
            RemoteEnemyRuntime enemy = ctx.FindEnemy(probe.EnemyId);
            if (enemy == null || enemy.Destroyed) { probe.SetPhase(RemoteProbePhase.Resolved); return; }

            bool decisionDue = enemy.Ai.DecisionDue(dt);
            if (decisionDue) enemy.Ai.MakeDecision(distanceToTarget: enemy.Ai.ScanComplete ? 0f : 100f, targetLost: false, exitAttackOrScanToRetreat: enemy.Ai.ScanComplete);

            if (enemy.Ai.Phase == EnemyAiPhase.AttackOrScan)
            {
                probe.SetPhase(RemoteProbePhase.Scanning);
                enemy.Ai.TickScan(dt, true);
                if (enemy.Ai.ScanComplete)
                {
                    probe.MarkScanCompleted();
                    CompleteRemoteScan(FirstControlledPlanetOrSystem(ctx));
                }
            }

            if (enemy.Ai.Phase == EnemyAiPhase.Retreat) probe.SetPhase(RemoteProbePhase.Retreating);
        }

        /// <summary>Same idempotent "mark Scanned + award Scan/Family XP exactly once" contract as ExtractionState.CompletePlanetScan, using the worldId-keyed profile lookup instead of a live PlanetIdentity -- a remote system's planets are never materialized MonoBehaviours (VS-016A: no live GameObjects for a non-Full system).</summary>
        private static void CompleteRemoteScan(string worldId)
        {
            PlanetProfile profile = ExtractionState.GetProfile(worldId);
            if (profile == null || profile.Scanned) return;
            profile.Scanned = true;
            Progression.ProgressionService.Award(Progression.ProgressionEvents.Scan(worldId), Progression.ProgressionEvents.ScanXp);
            Progression.ProgressionService.Award(
                Progression.ProgressionEvents.Family(ExtractionCatalog.ResourceIds[(int)profile.Family]), Progression.ProgressionEvents.FamilyXp);
        }

        private static void TickRetreat(RemoteRaidContext ctx, RemoteProbeState probe, float dt)
        {
            RemoteEnemyRuntime enemy = ctx.FindEnemy(probe.EnemyId);
            if (enemy == null || enemy.Destroyed) { probe.SetPhase(RemoteProbePhase.Resolved); return; }
            enemy.Ai.TickRetreatEscape(dt, distanceToTarget: 999999f, stillInSensorRange: false);
            if (enemy.Ai.Phase == EnemyAiPhase.Escaped) probe.SetPhase(RemoteProbePhase.Resolved);
        }
    }
}
