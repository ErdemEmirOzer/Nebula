using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Navigation;
using Nebula.Ship;

namespace Nebula.World
{
    /// <summary>
    /// Central orchestrator for VS-006: owns the player's system knowledge
    /// and the single active signal analysis, reveals the nearest three real
    /// systems the first time any planet reaches Controlled (via
    /// <see cref="DominanceSystem.PlanetFirstControlled"/>), detects physical
    /// system visits, and requests a save on every real state transition
    /// (never every frame). Reads <see cref="INavigationWorldProvider"/> for
    /// system positions/radii — never touches SolarSystemBootstrap directly,
    /// so a future on-demand sector provider only needs to implement that
    /// interface.
    /// </summary>
    public class NavigationDirector : MonoBehaviour
    {
        private INavigationWorldProvider provider;
        private ShipController ship;
        private ShipCruise cruise;
        private ShipExtraction extraction;
        private ShipModules modules;

        public SystemKnowledgeService Knowledge { get; } = new SystemKnowledgeService();
        public FrontierSignalService FrontierSignal { get; } = new FrontierSignalService();
        public SignalAnalysisState Analysis { get; } = new SignalAnalysisState();
        public string SelectedRouteSystemId { get; private set; }

        /// <summary>Set by the bootstrap once the save director exists (mirrors ShipModules.RequestSave).</summary>
        public Action RequestSave { get; set; }
        public event Action AnalysisStarted;
        public event Action AnalysisCompleted;
        public event Action AnalysisCancelled;

        public string LastEventMessage { get; private set; }
        private float messageUntil;
        public string TransientMessage => Time.unscaledTime < messageUntil ? LastEventMessage : null;

        private FlightMode lastCruiseMode = FlightMode.LocalFlight;

        public void Initialize(ShipController shipController, INavigationWorldProvider navigationProvider)
        {
            ship = shipController;
            provider = navigationProvider;
            cruise = shipController.GetComponent<ShipCruise>();
            extraction = shipController.GetComponent<ShipExtraction>();
            modules = shipController.GetComponent<ShipModules>();
        }

        /// <summary>New-campaign start: only the start system is Visited.</summary>
        public void InitializeNewCampaign()
        {
            Knowledge.InitializeNewGame(provider.StartSystemId);
            FrontierSignal.Clear();
            SelectedRouteSystemId = null;
            Analysis.Clear();
            if (cruise != null) lastCruiseMode = cruise.Mode;
        }

        private void OnEnable() => DominanceSystem.PlanetFirstControlled += OnPlanetFirstControlled;
        private void OnDisable() => DominanceSystem.PlanetFirstControlled -= OnPlanetFirstControlled;

        private void OnPlanetFirstControlled(string planetWorldId)
        {
            // VS-010 §7: a planet reaching Controlled (the Quick Asset Package's
            // usual outcome) is its own autosave trigger, independent of
            // whether a NEW frontier signal also opened this time.
            bool revealed = RevealNearestSignals(planetWorldId);
            if (revealed) SetMessage("YENİ SİNYALLER ALGILANDI");
            RequestSave?.Invoke();
        }

        /// <summary>Knowledge reveal + event log only — no save request (callers decide whether/when to persist; migration defers to the next natural save).</summary>
        private bool RevealNearestSignals(string planetWorldId)
        {
            if (provider == null) return false;
            string originSystemId = SystemIdFromPlanetWorldId(planetWorldId);
            List<(string systemId, float distance)> candidates = BuildCandidatesFromSystem(originSystemId);
            if (originSystemId == null) return false;

            if (!FrontierSignal.OpenOrigin(planetWorldId, candidates, Knowledge)) return false;
            SessionLogSafe("signal_revealed", originSystemId, planetWorldId);
            return true;
        }

        /// <summary>Every real system near an origin system's center, excluding the origin itself — the shared candidate builder for both the initial frontier open and its later backfill.</summary>
        private List<(string systemId, float distance)> BuildCandidatesFromSystem(string originSystemId)
        {
            var candidates = new List<(string systemId, float distance)>();
            if (provider == null || originSystemId == null || !provider.TryGetSystem(originSystemId, out StarSystemRecord origin)) return candidates;
            Vector3 originAbs = provider.AbsolutePosition(origin);
            foreach (StarSystemRecord record in provider.SystemsNear(originAbs))
            {
                if (record.SystemId == originSystemId) continue;
                float distance = Vector3.Distance(originAbs, provider.AbsolutePosition(record));
                candidates.Add((record.SystemId, distance));
            }
            return candidates;
        }

        private List<(string systemId, float distance)> BuildCandidatesForOriginPlanet(string originPlanetWorldId) =>
            BuildCandidatesFromSystem(SystemIdFromPlanetWorldId(originPlanetWorldId));

        /// <summary>Canonical "system.NNN/planet.NN" -> its system id. Single authority: <see cref="Sectors.WorldIdCodec"/>.</summary>
        public static string SystemIdFromPlanetWorldId(string planetWorldId) => Sectors.WorldIdCodec.SystemIdFromPlanetWorldId(planetWorldId);

        // --- Signal analysis ---------------------------------------------------
        public int ScannerTier => modules != null ? modules.Modules.GetTier("exploration.scanner") : 0;

        public bool TryStartAnalysis(string systemId, out string rejectReason)
        {
            rejectReason = null;
            if (ScannerTier < 1) { rejectReason = "REQUIRES SCANNER T1"; return false; }
            if (Knowledge.GetLevel(systemId) != SystemKnowledgeLevel.Signal) { rejectReason = "TARGET MUST BE A SIGNAL"; return false; }
            if (Analysis.Active) { rejectReason = "ANALYSIS ALREADY IN PROGRESS"; return false; }

            int index = Mathf.Clamp(ScannerTier - 1, 0, ModuleCatalog.SignalAnalysisSecondsByTier.Length - 1);
            float seconds = ModuleCatalog.SignalAnalysisSecondsByTier[index];
            if (!Analysis.Start(systemId, seconds)) { rejectReason = "CANNOT START"; return false; }

            SessionLogSafe("analysis_started", systemId, null);
            SetMessage($"ANALYZING {systemId}");
            RequestSave?.Invoke();
            AnalysisStarted?.Invoke();
            return true;
        }

        public void CancelAnalysis()
        {
            if (!Analysis.Active) return;
            SessionLogSafe("analysis_cancelled", Analysis.TargetSystemId, null);
            Analysis.Clear();
            RequestSave?.Invoke();
            AnalysisCancelled?.Invoke();
        }

        // --- Route -------------------------------------------------------------
        public bool TrySelectRoute(string systemId, out string rejectReason)
        {
            rejectReason = null;
            SystemKnowledgeLevel level = Knowledge.GetLevel(systemId);
            if (level != SystemKnowledgeLevel.Located && level != SystemKnowledgeLevel.Visited)
            { rejectReason = "TARGET MUST BE LOCATED OR VISITED"; return false; }

            SelectedRouteSystemId = systemId;
            SessionLogSafe("route_selected", systemId, null);
            SetMessage($"ROUTE SET: {systemId}");
            RequestSave?.Invoke();
            return true;
        }

        public void ClearRoute()
        {
            if (SelectedRouteSystemId == null) return;
            SessionLogSafe("route_cleared", SelectedRouteSystemId, null);
            SelectedRouteSystemId = null;
            SetMessage("ROTA TEMİZLENDİ");
            RequestSave?.Invoke();
        }

        // --- Live distance/ETA for the HUD/panel --------------------------------
        public bool TryGetSystem(string systemId, out StarSystemRecord record)
        {
            if (provider == null) { record = default; return false; }
            return provider.TryGetSystem(systemId, out record);
        }

        /// <summary>The real system (if any) currently containing the ship's absolute position — VS-010 §1's Live/Link resolver reference point.</summary>
        public string CurrentSystemId =>
            StarSystemRegistry.TryFindContaining(ShipAbsolutePosition(), out StarSystemRecord record) ? record.SystemId : null;

        public float AbsoluteDistanceTo(StarSystemRecord record) =>
            provider != null ? Vector3.Distance(ShipAbsolutePosition(), provider.AbsolutePosition(record)) : 0f;

        public Vector3 ShipAbsolutePosition() => ship != null ? ship.transform.position + FloatingOrigin.TotalOffset : Vector3.zero;

        /// <summary>Every real system in the ship's current sector + its 26 neighbors (VS-008 §4) — a bounded "nearby" view, never a global list.</summary>
        public IEnumerable<StarSystemRecord> SystemsNearShip() => provider != null ? provider.SystemsNear(ShipAbsolutePosition()) : System.Array.Empty<StarSystemRecord>();

        private void Update()
        {
            if (provider == null) return;

            float dt = Time.deltaTime; // 0 while paused (Time.timeScale = 0) — analysis/ramp naturally stall
            TickAnalysis(dt);
            DetectSystemVisit();
            DetectCruiseSaveTriggers();
        }

        private void TickAnalysis(float dt)
        {
            if (!Analysis.Active) return;
            string target = Analysis.TargetSystemId;
            if (Analysis.Tick(dt))
            {
                Analysis.Clear();
                Knowledge.Raise(target, SystemKnowledgeLevel.Located);
                FrontierSignal.OnSystemLevelRaised(target, SystemKnowledgeLevel.Located, BuildCandidatesForOriginPlanet, Knowledge);
                SessionLogSafe("analysis_completed", target, null);
                SetMessage($"{target} LOCATED");
                RequestSave?.Invoke();
                AnalysisCompleted?.Invoke();
            }
        }

        private void DetectSystemVisit()
        {
            if (ship == null) return;
            Vector3 shipAbs = ShipAbsolutePosition();
            foreach (StarSystemRecord record in provider.SystemsNear(shipAbs))
            {
                if (Knowledge.GetLevel(record.SystemId) != SystemKnowledgeLevel.Located) continue;
                if ((record.AbsoluteCenter - shipAbs).sqrMagnitude > record.NavigationRadius * record.NavigationRadius) continue;

                // The GetLevel(...) != Located guard above means this branch runs
                // exactly once per system: after Raise() the level becomes
                // Visited, so the next frame's check skips it — no re-log/re-save
                // from lingering inside the radius, and this survives save/load
                // unchanged since the guard is the persisted knowledge level itself.
                Knowledge.Raise(record.SystemId, SystemKnowledgeLevel.Visited);
                FrontierSignal.OnSystemLevelRaised(record.SystemId, SystemKnowledgeLevel.Visited, BuildCandidatesForOriginPlanet, Knowledge);
                SessionLogSafe("system_visited", record.SystemId, null);
                SetMessage($"ARRIVED: {record.DisplayName}");
                if (SelectedRouteSystemId == record.SystemId) SelectedRouteSystemId = null;
                RequestSave?.Invoke();
            }
        }

        private void DetectCruiseSaveTriggers()
        {
            if (cruise == null) return;
            FlightMode mode = cruise.Mode;
            if (mode == lastCruiseMode) return;

            if (mode == FlightMode.DeepSpaceCruise)
            {
                SessionLogSafe("cruise_entered", null, null);
                RequestSave?.Invoke();
            }
            else if (lastCruiseMode != FlightMode.LocalFlight && mode == FlightMode.LocalFlight)
            {
                SessionLogSafe("cruise_exited", null, null);
                RequestSave?.Invoke();
            }
            lastCruiseMode = mode;
        }

        private void SetMessage(string text) { LastEventMessage = text; messageUntil = Time.unscaledTime + 3.5f; NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Navigation, text); }
        private static void SessionLogSafe(string name, string result, string contentId) =>
            Progression.SessionLog.Log(name, result, contentId);

        // --- Save/load -----------------------------------------------------------
        /// <summary>New-game seeding is InitializeNewCampaign(); this restores exact saved knowledge/route/analysis/frontier-provenance (v8+ saves carry provenance directly).</summary>
        public void RestoreFromSave(IEnumerable<(string systemId, int level)> knowledgeEntries,
            string selectedRouteSystemId, string analysisTargetSystemId, float analysisRemainingSeconds,
            IEnumerable<(string origin, string target)> frontierProvenance)
        {
            Knowledge.Clear();
            foreach (var (systemId, level) in knowledgeEntries) Knowledge.ForceLevel(systemId, (SystemKnowledgeLevel)level);
            FrontierSignal.RestoreProvenance(frontierProvenance);

            SelectedRouteSystemId = string.IsNullOrEmpty(selectedRouteSystemId) ? null : selectedRouteSystemId;
            Analysis.Restore(analysisTargetSystemId, analysisRemainingSeconds);
            if (cruise != null) lastCruiseMode = cruise.Mode;
        }

        /// <summary>
        /// VS-010 §7: a v7 save has no frontier-provenance data at all — after
        /// RestoreFromSave already restored the exact saved knowledge levels
        /// unchanged, this backfills ONLY the provenance bookkeeping from
        /// every currently-Controlled planet (same nearest-3-Unknown rule a
        /// live open/backfill would use). Never changes an existing level, never awards XP.
        /// </summary>
        public void RebuildFrontierProvenanceFromControlled(IEnumerable<string> controlledPlanetWorldIds) =>
            FrontierSignal.RebuildProvenanceFromControlledPlanets(controlledPlanetWorldIds, BuildCandidatesForOriginPlanet, Knowledge);

        /// <summary>
        /// v1-v3 -> v4 migration (VS-006 §8): start system Visited, everything
        /// else Unknown, no route/analysis. If the legacy payload already has
        /// at least one Controlled planet, derive its three nearest signals
        /// deterministically (no XP, idempotent — reuses the same frontier
        /// guard as the live path, so a second load never duplicates it).
        /// </summary>
        public void MigrateLegacySave(IEnumerable<string> controlledPlanetWorldIds)
        {
            Knowledge.InitializeNewGame(provider.StartSystemId);
            FrontierSignal.Clear();
            SelectedRouteSystemId = null;
            Analysis.Clear();

            if (controlledPlanetWorldIds != null)
            {
                foreach (string worldId in controlledPlanetWorldIds) RevealNearestSignals(worldId);
            }
        }
    }
}
