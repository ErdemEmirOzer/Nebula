using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Campaign-scoped static resets, start sector/system resolution, and
    /// the eager Full-boot of the start system (VS-008 §5: the only system
    /// ever built eagerly). Split into two explicit phases — exactly the
    /// two-phase seam TF-002 §6 allows — because starting the streaming
    /// director needs the ship/hangar <see cref="PlayerInstaller"/> creates,
    /// while every other WorldInstaller responsibility must run before the
    /// player (and before <see cref="EnvironmentInstaller"/>'s RNG-consuming
    /// decor generation) so the start manifest exists first.
    /// </summary>
    public sealed class WorldInstaller
    {
        /// <summary>
        /// Phase 1 (composition step 3+5): campaign registry resets, then the
        /// start sector/system/manifest resolve. Consumes zero
        /// <see cref="Random"/> state — sector/system manifest generation is
        /// pure deterministic hashing from the universe seed (see
        /// <see cref="SectorManifestRepository"/>), never global Unity RNG —
        /// so this may run before, after, or interleaved with
        /// <see cref="EnvironmentInstaller"/>'s decor generation with zero
        /// effect on either's determinism.
        /// </summary>
        public void PrepareSession(int resolvedSeed, NebulaContext context)
        {
            // Session-owned globals are reset before any installer can enqueue a
            // presenter or attach a subscriber. Doing this later (Animation step
            // 10.8) erased the presenters streaming had already created at step 8.
            Visuals.NebulaAnimationDirector.ResetSessionState();
            Visuals.MaterialVariantCache.Clear();
            FloatingOrigin.ResetForSession();
            Visuals.DefenseFireSignal.ClearSubscribers();
            Remote.RemoteCommandService.ResetForSession();
            Remote.RemoteCommunicationService.ResetForSession();
            UI.Design.NebulaUiAssetCatalog.ResetForSession();
            UI.Shell.NebulaUiPrefabCatalog.ResetForSession();
            Ship.InputGate.Suppressed = false;
            Time.timeScale = 1f;

            LocationRegistry.Clear();
            StarSystemRegistry.Initialize(unchecked((ulong)resolvedSeed));
            ExtractionState.Reset(resolvedSeed);
            LogisticsState.Reset();
            Poi.PoiLiveState.Reset();
            Poi.PoiObject.ClearRegistry();
            Poi.PoiManifestRepository.Reset();
            // VS-013: same reasoning as LogisticsState/PoiLiveState above — a static
            // campaign registry that must never leak stale Training/FirstProbe/Recovery
            // state (or a dangling enemy GameObject registry entry) into a fresh campaign.
            Combat.CombatCampaignState.Reset();
            Combat.EnemyAgent.ClearRegistry();
            // VS-013.1: CombatActive/TrainingActive are per-frame-recomputed flags with
            // no owner-authoritative source other than "was recently true" -- reset them
            // explicitly so a restart can never start a fresh campaign with stale
            // world-interaction blocking left over from the previous session's last frame.
            Ship.CombatInputContext.CombatActive = false;
            Ship.CombatInputContext.TrainingActive = false;
            Progression.ProgressionService.Reset();
            // VS-017A.1 §1: same "never leak a prior session's subscriber into the next
            // one" reasoning as every other static event reset above.
            Hangar.ResourceTransferService.ResetForSession();
            Extraction.CampaignIdentity.ResetSubscribersForSession();

            // VS-017A.2 §A2/§A3: preparing a session IS starting a genuinely new campaign
            // (this project has no auto-load-at-boot -- a save is only ever restored by an
            // explicit later LoadNow, which adopts its own recorded identity instead). So a
            // brand-new unique campaign id is minted exactly here, and exactly here is what
            // makes a real "new_game" funnel event fire once per campaign.
            Extraction.CampaignIdentity.StartNewCampaign();
            Progression.SessionLog.StartSession(Extraction.CampaignIdentity.Current);

            // VS-008: the start sector/system is fixed at sector.0.0.0's first
            // slot — FrontierGuaranteePass runs automatically the first time
            // this sector resolves.
            var startSector = new SectorCoordinate(0, 0, 0);
            string startSystemId = WorldIdCodec.SystemId(startSector, 0);
            StarSystemRegistry.SetStartSystem(startSector, startSystemId);
            SystemManifest startSystem = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);

            context.SetStartSector(startSector);
            context.SetStartSystemId(startSystemId);
            context.SetStartSystem(startSystem);
        }

        /// <summary>
        /// Phase 2 (composition step 8): materializes the start system Full —
        /// the only system ever built eagerly — after the player/hangar
        /// exist. Registers the result on <see cref="StarSystemRegistry.Streaming"/>
        /// and flags the recommended-start planet exactly as before.
        /// </summary>
        public void StartStreaming(NebulaContext context, NebulaSessionLifetime lifetime, bool showOrbitRings, bool showPlanetTrails)
        {
            var streamingGO = new GameObject("System Streaming Director");
            lifetime.Track(SessionLifetime.CampaignSession, streamingGO);
            var streamingDirector = streamingGO.AddComponent<SystemStreamingDirector>();
            streamingDirector.Initialize(context.Ship, context.Gravity, context.Hangar, context.PlanetMaterialPool, showOrbitRings, showPlanetTrails);
            // BootFull publishes the director into StarSystemRegistry itself, so there is
            // no assignment here any more. There used to be, and it was the only one on
            // any path — which is exactly why a manual load, which reboots the director
            // without going through composition, left the registry empty for the rest of
            // the session.
            streamingDirector.BootFull(context.StartSystem);

            PlanetIdentity startPlanetIdentity = null;
            if (streamingDirector.BootStarTransform != null)
            {
                foreach (PlanetManifest pm in context.StartSystem.Planets)
                {
                    if (!pm.RecommendedStart) continue;
                    PlanetIdentity id = ExtractionState.FindPlanet(pm.PlanetId);
                    if (id != null) startPlanetIdentity = id;
                    break;
                }
            }
            if (startPlanetIdentity != null)
            {
                PlanetProfile startProfile = ExtractionState.GetProfile(startPlanetIdentity);
                startProfile.RecommendedStart = true;
            }

            context.SetStreaming(streamingDirector);

            // VS-013.2: single deterministic Safety Marker authority for this campaign
            // session, re-derived from the start manifest alone (never saved).
            SafetyMarkerAnchor safetyMarker = SafetyMarkerAnchor.Create(context.StartSystem);
            lifetime.Track(SessionLifetime.CampaignSession, safetyMarker.gameObject);
            context.SetSafetyMarkerAnchor(safetyMarker);
        }
    }
}
