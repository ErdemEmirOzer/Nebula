using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Telemetry
{
    /// <summary>
    /// VS-017A.1 §1: the single telemetry gameplay bridge, wired exclusively through typed
    /// <see cref="NebulaContext"/> references by <see cref="TelemetryInstaller"/> — never
    /// FindObjectOfType, never a second telemetry/event-bus authority. Every subscription here
    /// targets an authoritative, already-committed production transition (an XP ledger award, a
    /// completed production job, a real physical system-arrival check) — never a UI click, never
    /// a per-frame poll guessing at a result. All 14 documented funnel events go through
    /// <see cref="TelemetryService.RecordFunnelEventOnce"/>, so a duplicate firing of the
    /// underlying signal (a second load, a resubscribe, a demote/promote) can never double-count.
    /// </summary>
    public class TelemetryGameplayBridge : MonoBehaviour
    {
        private NebulaContext context;
        private string startSystemId;
        private float activePlayTimeSeconds;

        private bool subscribed;

        public void Initialize(NebulaContext ctx)
        {
            context = ctx;
            startSystemId = ctx.StartSystemId;

            Subscribe();

            // VS-017A.2 §A3: "new_game" belongs to the real campaign-CREATION transition, not
            // to this bridge being initialized. CampaignIdentity minted the campaign several
            // installer steps earlier, so this one-shot handshake is how that transition
            // reaches us -- it returns true exactly once per genuinely new campaign, and never
            // for a campaign adopted from a save, a re-boot of the same campaign, or a
            // re-materialization.
            if (CampaignIdentity.ConsumePendingNewCampaign())
                Now(TelemetryEvents.NewGame, systemId: startSystemId, contentId: startSystemId);
        }

        private void Subscribe()
        {
            if (subscribed || context == null) return;
            subscribed = true;

            DominanceSystem.PlanetFirstControlled += OnPlanetFirstControlled;
            Progression.ProgressionService.Awarded += OnAwarded;
            ResourceTransferService.Collected += OnResourceCollected;
            CampaignIdentity.IdentityChanged += OnCampaignIdentityChanged;
            if (context.Hangar != null) context.Hangar.Production.JobCompleted += OnHangarJobCompleted;
            if (context.Modules != null) context.Modules.Upgrades.JobCompleted += OnModuleUpgradeJobCompleted;
            if (context.Streaming != null) context.Streaming.FullSystemChangedByRealMovement += OnFullSystemChangedByRealMovement;
            if (context.NavigationDirector != null) context.NavigationDirector.FrontierSignal.OriginOpened += OnFrontierOriginOpened;
        }

        private void Unsubscribe()
        {
            if (!subscribed || context == null) return;
            subscribed = false;

            DominanceSystem.PlanetFirstControlled -= OnPlanetFirstControlled;
            Progression.ProgressionService.Awarded -= OnAwarded;
            ResourceTransferService.Collected -= OnResourceCollected;
            CampaignIdentity.IdentityChanged -= OnCampaignIdentityChanged;
            if (context.Hangar != null) context.Hangar.Production.JobCompleted -= OnHangarJobCompleted;
            if (context.Modules != null) context.Modules.Upgrades.JobCompleted -= OnModuleUpgradeJobCompleted;
            if (context.Streaming != null) context.Streaming.FullSystemChangedByRealMovement -= OnFullSystemChangedByRealMovement;
            if (context.NavigationDirector != null) context.NavigationDirector.FrontierSignal.OriginOpened -= OnFrontierOriginOpened;
        }

        private void OnEnable() => Subscribe();

        /// <summary>
        /// Releases every subscription the moment the session deactivates this
        /// object, rather than at the deferred <c>OnDestroy</c> that follows it.
        /// <para>
        /// <c>Destroy</c> does not run until the end of the frame, and
        /// <c>RestartCampaign</c> disposes the old session and boots the new one
        /// <b>inside one frame</b> — so the old bridge was still on
        /// <c>ProgressionService.Awarded</c>, <c>ResourceTransferService.Collected</c>
        /// and <c>CampaignIdentity.IdentityChanged</c> for the whole of the next
        /// campaign's boot, recording the new campaign's first awards against the
        /// dead one. The installer's own comment already promised this could not
        /// happen; the deactivation the session lifetime performs first is what
        /// makes it true.
        /// </para>
        /// </summary>
        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        /// <summary>A load adopting its save's own campaign identity re-scopes funnel deduplication onto that campaign — and, being a load, never produces a "new_game".</summary>
        private void OnCampaignIdentityChanged(string campaignId, bool isNewCampaign)
        {
            TelemetryService.SetCampaignId(campaignId);
            if (isNewCampaign) Now(TelemetryEvents.NewGame, systemId: startSystemId, contentId: startSystemId);
        }

        private void Update()
        {
            if (Time.timeScale > 0f) activePlayTimeSeconds += Time.unscaledDeltaTime;
        }

        private void Now(string eventId, string outcome = "", int level = 0, string systemId = "", string contentId = "") =>
            TelemetryService.RecordFunnelEventOnce(eventId, Time.realtimeSinceStartupAsDouble, Time.time, activePlayTimeSeconds, outcome, level, systemId, contentId);

        // --- DominanceSystem: first Controlled planet, and (derived) first fully-Controlled system ---

        private void OnPlanetFirstControlled(string worldId)
        {
            Now(TelemetryEvents.FirstControlledPlanet, level: Progression.ProgressionService.State.Level, contentId: worldId);
            CheckSecondSystemAsset(worldId);
            CheckFullSystemDominance(worldId);
        }

        /// <summary>
        /// VS-017A.2 §A1: the campaign's SECOND distinct star system reaching a real Asset
        /// state. "Asset" means the system has at least one genuinely Controlled planet — the
        /// start system counts as the first if it qualifies. A second planet, a second POI or
        /// a second Anomaly is explicitly NOT this event (that was the old mis-measurement).
        ///
        /// Ownership is re-derived every time from the authoritative
        /// <see cref="SystemStatusQuery.PlanetDominanceStates"/>, which reads live
        /// ExtractionState for the Full system and the persisted SystemDelta for every other
        /// one — so this is never limited to live systems and never needs a RAM-side ledger of
        /// its own.
        /// </summary>
        private void CheckSecondSystemAsset(string triggeringWorldId)
        {
            int assetSystems = 0;
            foreach (string systemId in KnownSystemIds())
            {
                foreach (var kv in SystemStatusQuery.PlanetDominanceStates(systemId))
                {
                    if (kv.Value != DominanceState.Controlled) continue;
                    assetSystems++;
                    break;
                }
                if (assetSystems >= 2) break;
            }
            if (assetSystems >= 2)
                Now(TelemetryEvents.SecondSystemAsset, level: Progression.ProgressionService.State.Level,
                    systemId: WorldIdCodec.SystemIdFromPlanetWorldId(triggeringWorldId), contentId: triggeringWorldId);
        }

        /// <summary>Every system this campaign could possibly own something in: the currently-Full one plus every system that has a persisted delta. Deduplicated, never a second registry of its own.</summary>
        private IEnumerable<string> KnownSystemIds()
        {
            var seen = new HashSet<string>();
            string fullId = context?.Streaming != null ? context.Streaming.FullSystemId : null;
            if (!string.IsNullOrEmpty(fullId) && seen.Add(fullId)) yield return fullId;
            if (StarSystemRegistry.Deltas == null) yield break;
            foreach (SystemDelta delta in StarSystemRegistry.Deltas.AllNonEmpty)
                if (delta != null && !string.IsNullOrEmpty(delta.systemId) && seen.Add(delta.systemId)) yield return delta.systemId;
        }

        private void CheckFullSystemDominance(string worldId)
        {
            string systemId = WorldIdCodec.SystemIdFromPlanetWorldId(worldId);
            if (string.IsNullOrEmpty(systemId)) return;
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return;
            foreach (PlanetManifest p in manifest.Planets)
                if (ExtractionState.GetDominance(p.PlanetId).State != DominanceState.Controlled) return;
            Now(TelemetryEvents.FirstFullSystemDominance, systemId: systemId, contentId: systemId);
        }

        // --- ProgressionService.Awarded: first scan / first drill / second drill / first processing / second asset ---

        private const string ScanPrefix = "scan:";
        private const string DrillFirstPrefix = "drill:first:";
        private const string RecipeFirstPrefix = "recipe:first:";

        private void OnAwarded(Progression.AwardResult result)
        {
            string id = result.EventId;
            if (string.IsNullOrEmpty(id)) return;

            if (id.StartsWith(ScanPrefix))
            {
                Now(TelemetryEvents.FirstScan, level: result.LevelAfter, contentId: id.Substring(ScanPrefix.Length));
            }
            else if (id.StartsWith(DrillFirstPrefix))
            {
                string worldId = id.Substring(DrillFirstPrefix.Length);
                Now(TelemetryEvents.FirstDrill, level: result.LevelAfter, contentId: worldId);
                CheckSecondDrill(result.LevelAfter, worldId);
            }
            else if (id.StartsWith(RecipeFirstPrefix))
            {
                Now(TelemetryEvents.FirstProcessing, level: result.LevelAfter, contentId: id.Substring(RecipeFirstPrefix.Length));
            }
        }

        /// <summary>
        /// VS-017A.2 §A4: "two distinct planets genuinely have a built drill" — re-derived from
        /// authoritative state (live <see cref="ExtractionState.Drills"/> for the Full system,
        /// every persisted SystemDelta for the rest) rather than from a RAM-only set of ids
        /// this bridge happened to observe. That makes it correct across load and restart: a
        /// first drill restored from a save still counts when a genuinely new second drill
        /// later completes.
        ///
        /// Only Active drills count, so a cancelled, failed or still-under-construction
        /// build is never mistaken for a real second drill; and re-materializing the SAME
        /// drill can never move the count, because the count is over distinct worldIds.
        /// </summary>
        private void CheckSecondDrill(int level, string triggeringWorldId)
        {
            var worldIds = new HashSet<string>();
            foreach (DrillStructure drill in ExtractionState.Drills)
                if (drill != null && drill.Runtime != null && drill.Runtime.State == DrillLifecycle.Active)
                    worldIds.Add(drill.WorldId);

            if (StarSystemRegistry.Deltas != null)
                foreach (SystemDelta delta in StarSystemRegistry.Deltas.AllNonEmpty)
                {
                    if (delta == null) continue;
                    foreach (DrillDto dto in delta.drills)
                        if (dto != null && (DrillLifecycle)dto.state == DrillLifecycle.Active)
                            worldIds.Add(dto.worldId);
                }

            if (worldIds.Count >= 2) Now(TelemetryEvents.SecondDrill, level: level, contentId: triggeringWorldId);
        }

        // --- ResourceTransferService: first physical collection from a drill ---

        private void OnResourceCollected(float amount) => Now(TelemetryEvents.FirstCollection, level: Progression.ProgressionService.State.Level);

        // --- HangarProductionService: first Collector / first Engineer actually added to the fleet ---

        private void OnHangarJobCompleted(HangarProductionJob job, string vehicleId)
        {
            if (job == null || string.IsNullOrEmpty(vehicleId)) return;
            if (job.VehicleClass == VehicleClass.Collector) Now(TelemetryEvents.FirstCollector, contentId: vehicleId);
            else if (job.VehicleClass == VehicleClass.Engineer) Now(TelemetryEvents.FirstEngineer, contentId: vehicleId);
        }

        // --- ModuleUpgradeService: Engine Navigation reaching T1 ---

        private const string EngineNavigationModuleId = "exploration.engine_navigation";

        private void OnModuleUpgradeJobCompleted(Modules.ModuleUpgradeJob job)
        {
            if (job != null && job.ModuleId == EngineNavigationModuleId && job.ToTier == 1) Now(TelemetryEvents.EngineT1, contentId: job.ModuleId);
        }

        // --- SystemStreamingDirector: real physical exit from the start system ---

        private void OnFullSystemChangedByRealMovement(string newSystemId)
        {
            if (!string.IsNullOrEmpty(newSystemId) && newSystemId != startSystemId)
                Now(TelemetryEvents.ManualExitStartSystem, systemId: startSystemId, contentId: newSystemId);
        }

        // --- FrontierSignalService: first frontier signal genuinely opened ---

        private void OnFrontierOriginOpened(string originPlanetWorldId) => Now(TelemetryEvents.FirstFrontierSignal, contentId: originPlanetWorldId);
    }
}
