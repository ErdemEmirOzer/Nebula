using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Content;
using Nebula.World.Composition;

namespace Nebula.Tutorial
{
    /// <summary>
    /// The tutorial's own gameplay composition, and the reason a tutorial run
    /// cannot touch a campaign.
    /// <para>
    /// It runs the project's real installers, in the project's real order, and
    /// produces a real <see cref="NebulaContext"/> -- the same ship, the same
    /// streamed start system, the same hangar, modules, combat and navigation
    /// the campaign gets. What it does not run is the four things that make a
    /// composition a <i>campaign</i>:
    /// </para>
    /// <list type="bullet">
    /// <item>it never calls <c>SolarSystemBootstrap.BeginSession()</c> or
    /// <c>NebulaBootstrap.StartSession</c>;</item>
    /// <item>it never calls <c>CampaignIdentity.StartNewCampaign()</c>, so no
    /// campaign id is minted and no save lineage is started
    /// (<c>WorldInstaller.PrepareSession</c> does both, which is exactly why
    /// <see cref="PrepareWorldWithoutCampaignIdentity"/> exists instead);</item>
    /// <item>it never runs <c>TelemetryInstaller</c>, so
    /// <c>TelemetryService.StartSession</c> never fires and
    /// <see cref="NebulaContext.TelemetryBridge"/> stays unset;</item>
    /// <item>it severs every save trigger the moment gameplay is wired, so no
    /// player action inside the tutorial can reach <c>SaveService</c>.</item>
    /// </list>
    /// <para>
    /// Everything the run produces lives in RAM and in one
    /// <see cref="NebulaSessionLifetime"/>, and dies with
    /// <see cref="Dispose"/> -- so re-entering the tutorial is always a genuinely
    /// fresh world, never a resumed one.
    /// </para>
    /// </summary>
    public sealed class TutorialSession : IDisposable
    {
        /// <summary>
        /// Fixed, not random. The tutorial teaches with named, reproducible
        /// bodies: a step that says "fly to the recommended planet" must mean the
        /// same planet every run, and a bug report about step 19 must be
        /// reproducible. This is the scene's own default seed, so the tutorial's
        /// sky is the sky a default campaign shows.
        /// </summary>
        public const int TutorialSeed = 12345;

        private readonly NebulaSessionLifetime lifetime = new NebulaSessionLifetime();
        private float restoreTimeScale = 1f;
        private bool timeScaleTouched;

        public NebulaContext Gameplay { get; private set; }

        public bool IsDisposed => lifetime.IsDisposed;

        private TutorialSession() { }

        /// <summary>
        /// Builds the tutorial world. Throws on a content or config error exactly
        /// as production composition does, and unwinds whatever was built so far
        /// rather than leaving a half-world behind.
        /// </summary>
        public static TutorialSession Compose(int seed = TutorialSeed)
        {
            var session = new TutorialSession { Gameplay = new NebulaContext() };
            NebulaContext context = session.Gameplay;

            // Step 0 -- pure content, before anything can exist to unwind.
            IContentCatalog contentSource = new CSharpContentCatalog();
            System.Collections.Generic.List<string> contentErrors = ContentCatalogValidator.Validate(contentSource);
            if (contentErrors.Count > 0) throw new ContentValidationException(contentErrors);
            RuntimeCatalog runtimeCatalog = RuntimeCatalogBuilder.Build(contentSource);

            NebulaBootstrapConfig config = BuildTutorialConfig(seed);
            config.Validate();

            try
            {
                context.SetRuntimeCatalog(runtimeCatalog);

                int resolvedSeed = config.Seed;
                UnityEngine.Random.InitState(resolvedSeed);
                context.SetConfig(config);
                context.SetResolvedSeed(resolvedSeed);

                var world = new WorldInstaller();
                var environment = new EnvironmentInstaller();
                var player = new PlayerInstaller();
                var gameplay = new GameplayInstaller();
                var navigation = new NavigationInstaller();
                var lighting = new LightingInstaller();
                var animation = new AnimationInstaller();

                PrepareWorldWithoutCampaignIdentity(resolvedSeed, context);                       // 3+5, minus the campaign
                environment.BuildWorldFoundation(config, context, session.lifetime);              // 4
                player.CreatePlayerAndCamera(context, session.lifetime);                          // 6+7
                environment.BuildCameraPresentationBindings(config, context, session.lifetime);   // 6 (tail)
                world.StartStreaming(context, session.lifetime, config.ShowOrbitRings, config.ShowPlanetTrails); // 8
                gameplay.WireGameplay(context, session.lifetime, resolvedSeed);                   // 9
                navigation.WireNavigation(context, session.lifetime);                             // 10
                // 10.5 telemetry: deliberately absent. See the class comment.
                lighting.WireLighting(context, session.lifetime);                                 // 10.7
                animation.WireAnimation(context, session.lifetime);                               // 10.8
                // 11 presentation: deliberately absent -- it builds the campaign shell
                //    and the strategic map, and the tutorial has its own card instead.
                // 11.5 audio: deliberately absent -- NebulaAudioGameplayBridge reads
                //    context.Presentation.StrategicMapScreen, which does not exist here.

                SeverEverySaveTrigger(context);                                                   // isolation

                var floatingOriginGO = new GameObject("Floating Origin");                         // 12
                session.lifetime.Track(SessionLifetime.CampaignSession, floatingOriginGO);
                var floatingOrigin = floatingOriginGO.AddComponent<World.FloatingOrigin>();
                floatingOrigin.Initialize(context.Ship.transform);
                context.SetFloatingOrigin(floatingOrigin);
            }
            catch
            {
                session.lifetime.DisposePartialBoot();
                throw;
            }

            return session;
        }

        /// <summary>
        /// The campaign-scoped static resets <c>WorldInstaller.PrepareSession</c>
        /// does, and the start system it resolves -- without the two lines that
        /// make it a campaign.
        /// <para>
        /// Those two lines are <c>CampaignIdentity.StartNewCampaign()</c>, which
        /// mints the id every save envelope is stamped with, and
        /// <c>SessionLog.StartSession()</c>, which writes a session_start record
        /// to disk and trims the log directory. Both are correct for a campaign
        /// and both are wrong for a tutorial, and neither can be skipped from
        /// outside because they live in the middle of a single production method.
        /// Duplicating the reset list is the honest cost of not editing it.
        /// </para>
        /// <para>
        /// The resets themselves are RAM-only: they clear static registries so
        /// this run cannot inherit a previous one. They touch no file. A campaign
        /// started afterwards runs the real <c>PrepareSession</c>, which resets
        /// all of them again -- so the tutorial cannot leave residue in one either.
        /// </para>
        /// </summary>
        private static void PrepareWorldWithoutCampaignIdentity(int resolvedSeed, NebulaContext context)
        {
            Visuals.NebulaAnimationDirector.ResetSessionState();
            Visuals.MaterialVariantCache.Clear();
            World.FloatingOrigin.ResetForSession();
            Visuals.DefenseFireSignal.ClearSubscribers();
            Remote.RemoteCommandService.ResetForSession();
            Remote.RemoteCommunicationService.ResetForSession();
            UI.Design.NebulaUiAssetCatalog.ResetForSession();
            UI.Shell.NebulaUiPrefabCatalog.ResetForSession();
            Ship.InputGate.Suppressed = false;
            Time.timeScale = 1f;

            World.LocationRegistry.Clear();
            World.StarSystemRegistry.Initialize(unchecked((ulong)resolvedSeed));
            Extraction.ExtractionState.Reset(resolvedSeed);
            Logistics.LogisticsState.Reset();
            Poi.PoiLiveState.Reset();
            Poi.PoiObject.ClearRegistry();
            Poi.PoiManifestRepository.Reset();
            Combat.CombatCampaignState.Reset();
            Combat.EnemyAgent.ClearRegistry();
            Ship.CombatInputContext.CombatActive = false;
            Ship.CombatInputContext.TrainingActive = false;
            Progression.ProgressionService.Reset();
            Hangar.ResourceTransferService.ResetForSession();
            Extraction.CampaignIdentity.ResetSubscribersForSession();

            // NO CampaignIdentity.StartNewCampaign() -- no id is minted.
            // NO SessionLog.StartSession()          -- nothing is written.

            var startSector = new Sectors.SectorCoordinate(0, 0, 0);
            string startSystemId = Sectors.WorldIdCodec.SystemId(startSector, 0);
            World.StarSystemRegistry.SetStartSystem(startSector, startSystemId);
            Sectors.SystemManifest startSystem = World.StarSystemRegistry.Manifests.ResolveSystem(startSystemId);

            context.SetStartSector(startSector);
            context.SetStartSystemId(startSystemId);
            context.SetStartSystem(startSystem);
        }

        /// <summary>
        /// Cuts every wire that could reach <c>SaveService</c>, immediately after
        /// the installers that create them.
        /// <para>
        /// This is the part that has to be airtight, because the danger is not the
        /// tutorial calling <c>SaveNow</c> -- it never does -- but the <i>player</i>
        /// reaching it through real gameplay. <c>GameplayInstaller</c> and
        /// <c>NavigationInstaller</c> point five <c>RequestSave</c> delegates at
        /// <c>extractionDirector.SaveNow(null)</c>, and the game invokes those on
        /// perfectly ordinary actions: equipping a module, starting a hangar job,
        /// entering cruise, visiting a system. In a tutorial that teaches "equip
        /// your first module", the very act the step is teaching would overwrite
        /// slot 1 of a real campaign.
        /// </para>
        /// <para>
        /// So every one is set back to <c>null</c> rather than to an empty
        /// delegate. Each call site is <c>RequestSave?.Invoke()</c>, so null is the
        /// state in which the save path is not merely inert but absent -- there is
        /// no delegate that a later line of code could have replaced.
        /// </para>
        /// <para>
        /// The three triggers that are not delegates are inside
        /// <c>ExtractionDirector</c> itself: a 300-second autosave timer, the
        /// manual save/load chord, and a save on application quit. All three are
        /// Unity messages (<c>Update</c>, <c>OnApplicationQuit</c>), so
        /// deactivating the director's GameObject removes all three at once --
        /// Unity sends no messages to an inactive GameObject at all. The autosave
        /// one matters most: a tutorial run longer than five wall-clock minutes
        /// would otherwise write a real save with no player action whatsoever.
        /// </para>
        /// <para>
        /// Deactivating rather than destroying: the director is still a live C#
        /// object that streaming, navigation and the ship hold real references to,
        /// and its methods still work when called directly. Only the frame loop is
        /// gone.
        /// </para>
        /// <para>
        /// Because <c>SaveService.Save</c> is the only caller of
        /// <c>CampaignIdentity.ForSave()</c>, and <c>ForSave</c> is what mints an
        /// id when none exists, proving the save path unreachable proves the
        /// no-campaign-id guarantee at the same time.
        /// </para>
        /// </summary>
        private static void SeverEverySaveTrigger(NebulaContext context)
        {
            context.Modules.RequestSave = null;
            context.Hangar.RequestSave = null;
            context.NavigationDirector.RequestSave = null;

            var quickPackage = context.Ship.GetComponent<Extraction.ShipQuickAssetPackage>();
            if (quickPackage != null) quickPackage.RequestSave = null;

            var evacuation = context.Ship.GetComponent<Extraction.ShipPlanetEvacuation>();
            if (evacuation != null) evacuation.RequestSave = null;

            Extraction.ExtractionDirector director = context.ExtractionDirector;
            if (director != null) director.gameObject.SetActive(false);
        }

        /// <summary>
        /// Test-facing: true only while every save trigger is severed. An assertion
        /// can read this instead of reaching into five components, and it fails
        /// loudly if a future installer re-wires one of them.
        /// </summary>
        public bool SaveTriggersSevered
        {
            get
            {
                if (Gameplay == null) return false;
                if (Gameplay.Modules.RequestSave != null) return false;
                if (Gameplay.Hangar.RequestSave != null) return false;
                if (Gameplay.NavigationDirector.RequestSave != null) return false;

                var quickPackage = Gameplay.Ship.GetComponent<Extraction.ShipQuickAssetPackage>();
                if (quickPackage != null && quickPackage.RequestSave != null) return false;

                var evacuation = Gameplay.Ship.GetComponent<Extraction.ShipPlanetEvacuation>();
                if (evacuation != null && evacuation.RequestSave != null) return false;

                Extraction.ExtractionDirector director = Gameplay.ExtractionDirector;
                return director == null || !director.gameObject.activeInHierarchy;
            }
        }

        /// <summary>
        /// The tutorial's own config. A separate instance built here, never the
        /// scene's serialized values and never a production constant: nothing in
        /// this method can change what a campaign does.
        /// </summary>
        private static NebulaBootstrapConfig BuildTutorialConfig(int seed) => new NebulaBootstrapConfig
        {
            Seed = seed == 0 ? TutorialSeed : seed,
            GravitationalConstant = 0.015f,
            StarsPerArm = 10,
            ArmStarRadiusRange = new Vector2(3000f, 7000f),
            GalaxyName = "Aetheria",
            GalaxyCenter = new Vector3(-4500000f, 0f, 0f),
            GalaxyRadius = 10000000f,
            GalaxyParticleCount = 150000,
            GalaxyArmCount = 4,
            GalaxyRotationSpeedDegPerSec = 0.05f,
            BlackHoleRadius = 120000f,
            CompanionGalaxies = new[]
            {
                new NebulaBootstrapConfig.CompanionGalaxy { Name = "Elysia",   Scale = 0.75f, Center = new Vector3(-4500000f, 4000000f, 0f), CloudWhiteness = 0.35f },
                new NebulaBootstrapConfig.CompanionGalaxy { Name = "Nyxara",   Scale = 0.6f,  Center = new Vector3(10500000f, 4000000f, 0f), CloudWhiteness = 0.1f },
                new NebulaBootstrapConfig.CompanionGalaxy { Name = "Thalassa", Scale = 0.8f,  Center = new Vector3(14500000f, 0f, 0f),       CloudWhiteness = 0.2f }
            },
            HaloStarsPerGalaxy = 2200,
            ClustersPerGalaxy = 4,
            BridgeStarCount = 1200,
            AnomalyCount = 8,
            ShowOrbitRings = true,
            ShowPlanetTrails = true,
            StarfieldStarCount = 12000,
            StarfieldRadius = 500000f,
            NebulaCount = 26
        };

        // ---- tutorial-only time control ---------------------------------------

        /// <summary>How far a step may compress waiting. A ceiling, so a "speed it up" cannot become a different game.</summary>
        public const float MaxTimeScale = 8f;

        /// <summary>
        /// Compresses the wall-clock wait inside a tutorial step -- a refinery
        /// batch, a production job -- without changing a single production value.
        /// <para>
        /// It moves <see cref="Time.timeScale"/>, which is the one dial that
        /// speeds the same simulation up instead of substituting a shorter one:
        /// the mechanic shown and the state that proves success are still the real
        /// ones. The original is restored on <see cref="Dispose"/>, so a tutorial
        /// that ends mid-fast-forward cannot leave the main menu running at 8x.
        /// The tutorial's own animation is on unscaled time and is unaffected.
        /// </para>
        /// </summary>
        public void SetTutorialTimeScale(float scale)
        {
            if (!timeScaleTouched)
            {
                restoreTimeScale = Time.timeScale;
                timeScaleTouched = true;
            }
            Time.timeScale = Mathf.Clamp(scale, 0f, MaxTimeScale);
        }

        public void RestoreTimeScale()
        {
            if (!timeScaleTouched) return;
            Time.timeScale = restoreTimeScale;
            timeScaleTouched = false;
        }

        /// <summary>
        /// Tears the whole tutorial world down. Idempotent, and the same path a
        /// partial boot unwinds through, so there is exactly one teardown to get
        /// right. Nothing is written on the way out -- no save, no session log
        /// flush, no telemetry flush.
        /// <para>
        /// The progression reset at the end is what makes "tutorial state dies
        /// with the scene" literally true rather than nearly true. Ordinary
        /// tutorial actions award XP (scanning a planet, the first drill), which
        /// lands in a static ledger and a static event. A campaign started
        /// afterwards resets both itself, so nothing could ever have leaked into
        /// one -- but until it did, a main menu standing between the two would
        /// have been showing the tutorial's level. Resetting here also drops the
        /// <c>Awarded</c> subscribers, so no tutorial listener can outlive the run.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            RestoreTimeScale();
            DestroyLiveVehicles();
            lifetime.Dispose();
            Progression.ProgressionService.Reset();
        }

        /// <summary>
        /// Destroys any support vehicle still in the world and empties the static
        /// registry that holds them.
        /// <para>
        /// A <c>VehicleAgent</c> is registered in a process-wide static and keeps
        /// running <c>Update</c> for as long as it exists. One left behind by a
        /// hangar step outlived the session that made it, and its next frame
        /// dereferenced a <c>ShipController</c> that had just been destroyed --
        /// so the leak did not merely accumulate objects, it threw a
        /// <c>MissingReferenceException</c> into whatever ran next. The step scope
        /// is still the first line of defence; this is the guarantee that holds
        /// even when a step forgets.
        /// </para>
        /// </summary>
        private static void DestroyLiveVehicles()
        {
            var doomed = new List<Hangar.VehicleAgent>(Hangar.VehicleAgent.Registry.Values);
            Hangar.VehicleAgent.Registry.Clear();

            foreach (Hangar.VehicleAgent agent in doomed)
            {
                if (agent == null) continue;
                // Deactivated first: destruction is deferred in play mode, and an
                // active agent gets one more Update between here and the destroy.
                agent.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(agent.gameObject);
                else UnityEngine.Object.DestroyImmediate(agent.gameObject);
            }
        }
    }
}
