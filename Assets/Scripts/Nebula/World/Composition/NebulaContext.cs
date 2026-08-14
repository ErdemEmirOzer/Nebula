using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Simulation;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Every UI/HUD/Screen instance the <see cref="PresentationInstaller"/>
    /// creates, exactly once each — the "explicit presentation lifetime
    /// handle" NebulaContext carries instead of raw untyped GameObjects.
    /// </summary>
        public sealed class NebulaPresentationHandle
    {
        public UI.StrategicMap.StrategicMapScreen StrategicMapScreen { get; }
        public UI.SectorDebugHud SectorDebugHud { get; }

        /// <summary>DT-001: null in a release (non-Editor, non-Development Build) session — the dev-only Test Flight Speed panel is simply never created there.</summary>
        public UI.FlightTestSpeedHud FlightTestSpeedHud { get; }


        // ---- the redesigned interface (UI-09) --------------------------------
        // Exposed on the same handle the legacy panels use, so a PlayMode test can
        // reach the new widgets exactly the way it reached the old ones. Settable
        // separately rather than added to the constructor: the legacy arguments are
        // still byte-identical to the original monolith's, and keeping them that way
        // is what makes this refactor reviewable.

        /// <summary>The one session UI root, or <c>null</c> when the redesign is switched off.</summary>
        public UI.Shell.NebulaUiShell UiShell { get; private set; }

        /// <summary>The parent command menu, or <c>null</c>.</summary>
        public UI.Menu.NebulaParentMenuScreen ParentMenu { get; private set; }

        /// <summary>Bottom-centre action bar, or <c>null</c>.</summary>
        public UI.Hud.NebulaHudContextBar HudContextBar { get; private set; }

        /// <summary>Ship durability cluster, or <c>null</c>.</summary>
        public UI.Hud.NebulaHudVitals HudVitals { get; private set; }

        /// <summary>Site, route and flight clusters, or <c>null</c>.</summary>
        public UI.Hud.NebulaHudNavigation HudNavigation { get; private set; }

        /// <summary>Raid, cargo and attention column, or <c>null</c>.</summary>
        public UI.Hud.NebulaHudAlerts HudAlerts { get; private set; }

        /// <summary>Reserve transfer and support build context panels, or <c>null</c>.</summary>
        public UI.Hud.NebulaHudPlanetContext HudPlanetContext { get; private set; }

        /// <summary>The shell's single confirmation surface, or <c>null</c>.</summary>
        public UI.Shell.NebulaConfirmationDialog Confirmation { get; private set; }

        /// <summary>Fleet page, or <c>null</c>. The one page carrying a stable row registry.</summary>
        public UI.Menu.NebulaFleetPage FleetPage { get; private set; }

        /// <summary>XP and level-up line, or <c>null</c>.</summary>
        public UI.Hud.NebulaHudNotifications HudNotifications { get; private set; }

        /// <summary>Records the redesigned interface. Called once, by composition.</summary>
        public void SetRedesignedUi(UI.Shell.NebulaUiShell shell, UI.Menu.NebulaParentMenuScreen parentMenu,
            UI.Hud.NebulaHudContextBar contextBar, UI.Hud.NebulaHudVitals vitals,
            UI.Hud.NebulaHudNavigation navigation, UI.Hud.NebulaHudAlerts alerts,
            UI.Hud.NebulaHudPlanetContext planetContext, UI.Shell.NebulaConfirmationDialog confirmation,
            UI.Menu.NebulaFleetPage fleetPage, UI.Hud.NebulaHudNotifications notifications)
        {
            UiShell = shell;
            ParentMenu = parentMenu;
            HudContextBar = contextBar;
            HudVitals = vitals;
            HudNavigation = navigation;
            HudAlerts = alerts;
            HudPlanetContext = planetContext;
            Confirmation = confirmation;
            FleetPage = fleetPage;
            HudNotifications = notifications;
        }

        public NebulaPresentationHandle(
            UI.StrategicMap.StrategicMapScreen strategicMapScreen,
            UI.SectorDebugHud sectorDebugHud,
            UI.FlightTestSpeedHud flightTestSpeedHud)
        {
                                  StrategicMapScreen = strategicMapScreen;
            SectorDebugHud = sectorDebugHud;
            FlightTestSpeedHud = flightTestSpeedHud;        }
    }

    /// <summary>
    /// Typed, non-generic, set-once bag of every runtime dependency a
    /// composition-root installer resolves during a single campaign session
    /// (VS/TF-002 §5). Deliberately NOT a generic service locator: every
    /// dependency has its own named property, so "what does this session
    /// carry" is fully readable from this file alone, and a typo can never
    /// silently resolve to null the way a string/Type-keyed lookup could.
    ///
    /// Every property may be set exactly once (a second <c>SetX</c> call
    /// throws) and may only be READ after it has been set (an early read
    /// throws too) — both fail fast with a message naming the property, so a
    /// wiring-order bug surfaces immediately instead of producing a silent
    /// null downstream. Setters are public (this codebase has no
    /// internal-only convention anywhere else either) but are named
    /// <c>SetX</c>, never a property setter, to read unambiguously as
    /// "composition-root wiring only" — never call these from gameplay code.
    ///
    /// Carries only real runtime references (MonoBehaviours, services,
    /// resolved identifiers) for THIS session. It is never a second save or
    /// gameplay-state authority: nothing here is serialized, and nothing
    /// here is read by save/load — those still go through the existing
    /// static registries (ExtractionState, StarSystemRegistry, etc.).
    /// </summary>
    public sealed class NebulaContext
    {
        private struct SetOnce<T>
        {
            private T value;
            private bool isSet;

            /// <summary>True once Set has run. Lets a caller ask without throwing.</summary>
            public bool IsSet => isSet;

            public T Get(string propertyName)
            {
                if (!isSet) throw new InvalidOperationException($"NebulaContext.{propertyName} was read before it was set.");
                return value;
            }

            public void Set(string propertyName, T v)
            {
                if (isSet) throw new InvalidOperationException($"NebulaContext.{propertyName} was already set once and cannot be set again.");
                value = v;
                isSet = true;
            }
        }

        private SetOnce<NebulaBootstrapConfig> config;
        public NebulaBootstrapConfig Config { get => config.Get(nameof(Config)); }
        public void SetConfig(NebulaBootstrapConfig value) => config.Set(nameof(Config), value);

        private SetOnce<int> resolvedSeed;
        public int ResolvedSeed { get => resolvedSeed.Get(nameof(ResolvedSeed)); }
        public void SetResolvedSeed(int value) => resolvedSeed.Set(nameof(ResolvedSeed), value);

        private SetOnce<SectorCoordinate> startSector;
        public SectorCoordinate StartSector { get => startSector.Get(nameof(StartSector)); }
        public void SetStartSector(SectorCoordinate value) => startSector.Set(nameof(StartSector), value);

        private SetOnce<string> startSystemId;
        public string StartSystemId { get => startSystemId.Get(nameof(StartSystemId)); }
        public void SetStartSystemId(string value) => startSystemId.Set(nameof(StartSystemId), value);

        private SetOnce<SystemManifest> startSystem;
        public SystemManifest StartSystem { get => startSystem.Get(nameof(StartSystem)); }
        public void SetStartSystem(SystemManifest value) => startSystem.Set(nameof(StartSystem), value);

        private SetOnce<GravitySystem> gravity;
        public GravitySystem Gravity { get => gravity.Get(nameof(Gravity)); }
        public void SetGravity(GravitySystem value) => gravity.Set(nameof(Gravity), value);

        private SetOnce<List<Material>> planetMaterialPool;
        public List<Material> PlanetMaterialPool { get => planetMaterialPool.Get(nameof(PlanetMaterialPool)); }
        public void SetPlanetMaterialPool(List<Material> value) => planetMaterialPool.Set(nameof(PlanetMaterialPool), value);

        private SetOnce<List<Vector3>> decorStarPositions;
        /// <summary>Absolute positions of the decorative arm stars — the visible body of each galaxy. Never navigation targets.</summary>
        public List<Vector3> DecorStarPositions { get => decorStarPositions.Get(nameof(DecorStarPositions)); }
        public void SetDecorStarPositions(List<Vector3> value) => decorStarPositions.Set(nameof(DecorStarPositions), value);

        private SetOnce<List<GalaxyMap>> galaxyMaps;
        public List<GalaxyMap> GalaxyMaps { get => galaxyMaps.Get(nameof(GalaxyMaps)); }
        public void SetGalaxyMaps(List<GalaxyMap> value) => galaxyMaps.Set(nameof(GalaxyMaps), value);

        private SetOnce<Camera> mainCamera;
        public Camera MainCamera { get => mainCamera.Get(nameof(MainCamera)); }
        public void SetMainCamera(Camera value) => mainCamera.Set(nameof(MainCamera), value);

        private SetOnce<ShipController> ship;
        public ShipController Ship { get => ship.Get(nameof(Ship)); }
        public void SetShip(ShipController value) => ship.Set(nameof(Ship), value);

        /// <summary>
        /// The ship, or <c>null</c> before composition has set one — same shape as
        /// <see cref="AudioServiceOrNull"/> and for the same reason. A menu page
        /// refreshes on a timer that can out-run composition, and reading
        /// <see cref="Ship"/> there would throw the wiring-order error that property
        /// exists to raise for genuine bugs.
        /// </summary>
        public ShipController ShipOrNull => ship.IsSet ? ship.Get(nameof(Ship)) : null;

        private SetOnce<ShipHangar> hangar;
        public ShipHangar Hangar { get => hangar.Get(nameof(Hangar)); }
        public void SetHangar(ShipHangar value) => hangar.Set(nameof(Hangar), value);

        private SetOnce<ShipModules> modules;
        public ShipModules Modules { get => modules.Get(nameof(Modules)); }
        public void SetModules(ShipModules value) => modules.Set(nameof(Modules), value);

        /// <inheritdoc cref="ShipOrNull"/>
        public ShipModules ModulesOrNull => modules.IsSet ? modules.Get(nameof(Modules)) : null;

        private SetOnce<ShipModuleVisuals> moduleVisuals;
        public ShipModuleVisuals ModuleVisuals { get => moduleVisuals.Get(nameof(ModuleVisuals)); }
        public void SetModuleVisuals(ShipModuleVisuals value) => moduleVisuals.Set(nameof(ModuleVisuals), value);

        private SetOnce<SystemStreamingDirector> streaming;
        public SystemStreamingDirector Streaming { get => streaming.Get(nameof(Streaming)); }
        public void SetStreaming(SystemStreamingDirector value) => streaming.Set(nameof(Streaming), value);

        private SetOnce<ExtractionDirector> extractionDirector;
        public ExtractionDirector ExtractionDirector { get => extractionDirector.Get(nameof(ExtractionDirector)); }
        public void SetExtractionDirector(ExtractionDirector value) => extractionDirector.Set(nameof(ExtractionDirector), value);

        private SetOnce<CollectorRouteDirector> collectorRouteDirector;
        public CollectorRouteDirector CollectorRouteDirector { get => collectorRouteDirector.Get(nameof(CollectorRouteDirector)); }
        public void SetCollectorRouteDirector(CollectorRouteDirector value) => collectorRouteDirector.Set(nameof(CollectorRouteDirector), value);

        private SetOnce<InterventionGroupDirector> interventionGroupDirector;
        public InterventionGroupDirector InterventionGroupDirector { get => interventionGroupDirector.Get(nameof(InterventionGroupDirector)); }

        /// <summary>
        /// The director, or <c>null</c> when composition has not reached it yet —
        /// same shape as <see cref="AudioServiceOrNull"/> and for the same reason:
        /// presentation is built alongside gameplay, so a menu page that asked for
        /// this directly would throw during composition.
        /// </summary>
        public InterventionGroupDirector InterventionGroupDirectorOrNull =>
            interventionGroupDirector.IsSet ? interventionGroupDirector.Get(nameof(InterventionGroupDirector)) : null;
        public void SetInterventionGroupDirector(InterventionGroupDirector value) => interventionGroupDirector.Set(nameof(InterventionGroupDirector), value);

        private SetOnce<NavigationDirector> navigationDirector;
        public NavigationDirector NavigationDirector { get => navigationDirector.Get(nameof(NavigationDirector)); }
        public void SetNavigationDirector(NavigationDirector value) => navigationDirector.Set(nameof(NavigationDirector), value);

        private SetOnce<INavigationWorldProvider> navigationWorldProvider;
        public INavigationWorldProvider NavigationWorldProvider { get => navigationWorldProvider.Get(nameof(NavigationWorldProvider)); }
        public void SetNavigationWorldProvider(INavigationWorldProvider value) => navigationWorldProvider.Set(nameof(NavigationWorldProvider), value);

        private SetOnce<FloatingOrigin> floatingOrigin;
        public FloatingOrigin FloatingOrigin { get => floatingOrigin.Get(nameof(FloatingOrigin)); }
        public void SetFloatingOrigin(FloatingOrigin value) => floatingOrigin.Set(nameof(FloatingOrigin), value);

        private SetOnce<NebulaPresentationHandle> presentation;
        public NebulaPresentationHandle Presentation { get => presentation.Get(nameof(Presentation)); }
        public void SetPresentation(NebulaPresentationHandle value) => presentation.Set(nameof(Presentation), value);

        private SetOnce<ShipCombat> shipCombat;
        public ShipCombat ShipCombat { get => shipCombat.Get(nameof(ShipCombat)); }
        public void SetShipCombat(ShipCombat value) => shipCombat.Set(nameof(ShipCombat), value);

        private SetOnce<Combat.CombatCampaignDirector> combatCampaignDirector;
        public Combat.CombatCampaignDirector CombatCampaignDirector { get => combatCampaignDirector.Get(nameof(CombatCampaignDirector)); }
        public void SetCombatCampaignDirector(Combat.CombatCampaignDirector value) => combatCampaignDirector.Set(nameof(CombatCampaignDirector), value);

        private SetOnce<TrainingArea> trainingArea;
        public TrainingArea TrainingArea { get => trainingArea.Get(nameof(TrainingArea)); }
        public void SetTrainingArea(TrainingArea value) => trainingArea.Set(nameof(TrainingArea), value);

        /// <summary>VS-013.2: the single shared Safety Marker position authority (repair safe-zone, emergency recovery fallback, training placement).</summary>
        private SetOnce<SafetyMarkerAnchor> safetyMarkerAnchor;
        public SafetyMarkerAnchor SafetyMarkerAnchor { get => safetyMarkerAnchor.Get(nameof(SafetyMarkerAnchor)); }
        public void SetSafetyMarkerAnchor(SafetyMarkerAnchor value) => safetyMarkerAnchor.Set(nameof(SafetyMarkerAnchor), value);

        /// <summary>TF-003: the validated, immutable content snapshot built at the very start of composition (before any world/ship/director exists) — never a GameObject/MonoBehaviour.</summary>
        private SetOnce<RuntimeCatalog> runtimeCatalog;
        public RuntimeCatalog RuntimeCatalog { get => runtimeCatalog.Get(nameof(RuntimeCatalog)); }
        public void SetRuntimeCatalog(RuntimeCatalog value) => runtimeCatalog.Set(nameof(RuntimeCatalog), value);

        /// <summary>VS-017A.1 §1: the single telemetry gameplay bridge — set once by TelemetryInstaller, the only typed-composition path any code should ever reach it through (never FindObjectOfType).</summary>
        private SetOnce<Telemetry.TelemetryGameplayBridge> telemetryBridge;
        public Telemetry.TelemetryGameplayBridge TelemetryBridge { get => telemetryBridge.Get(nameof(TelemetryBridge)); }
        public void SetTelemetryBridge(Telemetry.TelemetryGameplayBridge value) => telemetryBridge.Set(nameof(TelemetryBridge), value);

        /// <summary>Presentation-only audio playback authority; never serialized and never a gameplay state owner.</summary>
        private SetOnce<Audio.NebulaAudioService> audioService;
        public Audio.NebulaAudioService AudioService { get => audioService.Get(nameof(AudioService)); }

        /// <summary>
        /// The service, or <c>null</c> when composition has not reached it yet.
        /// Presentation is built before audio, so a settings page that asked for
        /// it directly threw during composition and took the whole session down.
        /// </summary>
        public Audio.NebulaAudioService AudioServiceOrNull =>
            audioService.IsSet ? audioService.Get(nameof(AudioService)) : null;
        public void SetAudioService(Audio.NebulaAudioService value) => audioService.Set(nameof(AudioService), value);

        /// <summary>
        /// LGT-01: the single real-time lighting authority. Presentation only —
        /// never serialized, never read by save/load or simulation. Set-once is
        /// what guarantees one director and one global rig per session, with no
        /// <c>FindObjectOfType</c> anywhere.
        /// </summary>
        private SetOnce<Visuals.NebulaLightingDirector> lightingDirector;
        public Visuals.NebulaLightingDirector LightingDirector { get => lightingDirector.Get(nameof(LightingDirector)); }
        public void SetLightingDirector(Visuals.NebulaLightingDirector value) => lightingDirector.Set(nameof(LightingDirector), value);

        /// <summary>
        /// ANI-01: the single visual animation authority. Presentation only —
        /// never serialized, never read by save/load or simulation, and never
        /// permitted to drive a gameplay transform, Rigidbody or camera.
        /// Set-once is what guarantees one director per session.
        /// </summary>
        private SetOnce<Visuals.NebulaAnimationDirector> animationDirector;
        public Visuals.NebulaAnimationDirector AnimationDirector { get => animationDirector.Get(nameof(AnimationDirector)); }
        public void SetAnimationDirector(Visuals.NebulaAnimationDirector value) => animationDirector.Set(nameof(AnimationDirector), value);

        /// <summary>Presentation observer translating existing gameplay events/state into audio cues.</summary>
        private SetOnce<Audio.NebulaAudioGameplayBridge> audioBridge;
        public Audio.NebulaAudioGameplayBridge AudioBridge { get => audioBridge.Get(nameof(AudioBridge)); }
        public void SetAudioBridge(Audio.NebulaAudioGameplayBridge value) => audioBridge.Set(nameof(AudioBridge), value);
    }
}
