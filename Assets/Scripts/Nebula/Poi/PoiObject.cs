using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Modules;
using Nebula.Navigation;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Poi
{
    /// <summary>
    /// MonoBehaviour adapter for one materialized POI (VS-012 §8) — mirrors
    /// the DrillRuntime/DrillStructure split: <see cref="PoiState"/> owns
    /// pure knowledge/timer/salvage/facility state, this class owns
    /// placeholder visuals, self-ticks that state every frame (like
    /// DrillStructure — at most 2 of these ever exist at once, no aggregate
    /// budget to centrally ration, so no separate director is needed), and
    /// centrally decides "who's currently the active worker" (the main ship,
    /// autonomously, or a single assigned Scout/Collector) so Anomaly/
    /// Wreck/Facility service calls only ever happen from one place.
    /// </summary>
    public class PoiObject : MonoBehaviour
    {
        private static readonly Dictionary<string, PoiObject> byId = new Dictionary<string, PoiObject>();
        public static PoiObject Find(string poiId) => poiId != null && byId.TryGetValue(poiId, out PoiObject o) ? o : null;
        public static void ClearRegistry() => byId.Clear();
        /// <summary>VS-015 §7: read-only access to every currently-materialized POI, so a Sensor Array's "foreign Entity/Anomaly nearby" build gate can check for a real live Anomaly without a second registry.</summary>
        public static IReadOnlyCollection<PoiObject> All => byId.Values;

        public PoiManifest Manifest { get; private set; }
        public PoiState State { get; private set; }

        private ShipController ship;
        private ShipLogistics shipLogistics;
        private ShipModules shipModules;
        private SystemKnowledgeService knowledge;
        private FrontierSignalService frontierSignal;
        private ShipHangar hangar;
        private readonly List<GameObject> salvageVisuals = new List<GameObject>();
        private bool salvageVisualsBuilt;
        private GameObject primaryVisual;
        private string shownPrimaryVisualId;

        public static PoiObject Create(PoiManifest manifest, Transform starTransform, PoiState state,
            ShipController ship, ShipHangar hangar, SystemKnowledgeService knowledge, FrontierSignalService frontierSignal)
        {
            var go = new GameObject($"POI ({manifest.Type}) [{manifest.PoiId}]");
            go.transform.SetParent(starTransform.parent, false);
            go.transform.position = starTransform.position + manifest.SystemLocalPosition;

            var poi = go.AddComponent<PoiObject>();
            poi.Manifest = manifest;
            poi.State = state;
            poi.ship = ship;
            poi.shipLogistics = ship != null ? ship.GetComponent<ShipLogistics>() : null;
            poi.shipModules = ship != null ? ship.GetComponent<ShipModules>() : null;
            poi.hangar = hangar;
            poi.knowledge = knowledge;
            poi.frontierSignal = frontierSignal;
            poi.BuildVisuals();
            // LGT-04: presentation only — emission gated by the player's own
            // knowledge level. Reads that level and never advances it.
            go.AddComponent<Nebula.Visuals.PoiLightingPresenter>()
                .Bind(poi, Nebula.Visuals.LightingQualityProfile.FromQualitySettings().Quality);
            // ANI-05.3: presentation only — turns this POI's own visual at a
            // rate its real knowledge level allows, and reveals nothing early.
            go.AddComponent<Nebula.Visuals.PoiAnimationPresenter>().Initialize(poi);

            byId[manifest.PoiId] = poi;
            PoiLiveState.Register(state);
            return poi;
        }

        private void OnDestroy()
        {
            if (Manifest == null) return;
            string poiId = Manifest.PoiId;
            if (byId.TryGetValue(poiId, out PoiObject current) && current == this)
                byId.Remove(poiId);
            PoiLiveState.Unregister(poiId, State);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            RefreshPrimaryVisual();
            BuildSalvageVisuals();
            UpdateSalvageVisuals();
            if (dt <= 0f || State == null || ship == null) return;

            switch (State.Level)
            {
                case PoiKnowledgeLevel.Signal: TickLocate(dt); break;
                case PoiKnowledgeLevel.Located: TickIdentify(dt); break;
                case PoiKnowledgeLevel.Active: TickActive(dt); break;
                case PoiKnowledgeLevel.Identified:
                    // Wreck collection can start directly from Identified without a separate "start" step (see WreckService).
                    if (Manifest.Type == PoiType.Wreck) TickWreckCollection(dt);
                    break;
            }
            RefreshPrimaryVisual();
            BuildSalvageVisuals();
            UpdateSalvageVisuals();
        }

        private float DistanceToShip() => Vector3.Distance(transform.position, ship.transform.position);

        private VehicleAgent FindAssignedScout()
        {
            if (hangar == null) return null;
            foreach (VehicleAgent v in hangar.Vehicles)
                if (v != null && v.Def.Class == VehicleClass.Scout && v.PoiTask != null && v.PoiTask.PoiId == Manifest.PoiId
                    && v.PoiTask.Phase == PoiTaskPhase.Working) return v;
            return null;
        }

        private VehicleAgent FindAssignedCollector()
        {
            if (hangar == null) return null;
            foreach (VehicleAgent v in hangar.Vehicles)
                if (v != null && v.Def.Class == VehicleClass.Collector && v.PoiTask != null && v.PoiTask.PoiId == Manifest.PoiId
                    && v.PoiTask.Phase == PoiTaskPhase.Working) return v;
            return null;
        }

        // --- Locate (VS-012 §3) -------------------------------------------------------
        private void TickLocate(float dt)
        {
            VehicleAgent scout = FindAssignedScout();
            if (scout != null)
            {
                bool inRange = Vector3.Distance(transform.position, scout.transform.position) <= PoiCatalog.IdentifyRange;
                if (State.TickLocate(dt, inRange, PoiCatalog.ScoutLocateSeconds)) return;
                return;
            }

            // P1-WORLD-004 / P1-DATA-020. Two defects in two lines: the duration table
            // had no tier-0 row, so no scanner and Scanner T1 located at the same 12s
            // and the module the player bought did nothing here; and the condition was
            // the literal `true`, so the one exploration verb with a timer had no
            // spatial meaning — a POI on the far side of the star finished on its own
            // while the ship flew the other way. Both the duration and the range now
            // come from the scanner's own tier, through PoiCatalog.
            int scannerTier = shipModules != null ? shipModules.Modules.GetTier("exploration.scanner") : 0;
            bool inScannerRange = DistanceToShip() <= PoiCatalog.ShipLocateRange(scannerTier);
            State.TickLocate(dt, inScannerRange, PoiCatalog.ShipLocateSeconds(scannerTier));
        }

        // --- Identify (VS-012 §3) -----------------------------------------------------
        private void TickIdentify(float dt)
        {
            VehicleAgent scout = FindAssignedScout();
            bool byShip = scout == null;
            bool inRange = byShip ? DistanceToShip() <= PoiCatalog.IdentifyRange
                                   : Vector3.Distance(transform.position, scout.transform.position) <= PoiCatalog.IdentifyRange;

            if (State.TickIdentify(dt, inRange) && Manifest.Type == PoiType.Wreck)
                WreckService.OnIdentified(State, Manifest.PoiId);
        }

        // --- Active operation (Anomaly work / Facility) ------------------------------
        private void TickActive(float dt)
        {
            if (Manifest.Type == PoiType.Anomaly) { TickAnomaly(dt); return; }
            if (Manifest.Type == PoiType.AbandonedFacility) { TickFacility(dt); return; }
            if (Manifest.Type == PoiType.Wreck) TickWreckCollection(dt); // Active just means "currently being salvaged"
        }

        private void TickAnomaly(float dt)
        {
            VehicleAgent scout = FindAssignedScout();
            bool byScout = scout != null;
            bool inRange = byScout ? Vector3.Distance(transform.position, scout.transform.position) <= PoiCatalog.IdentifyRange : DistanceToShip() <= PoiCatalog.IdentifyRange;
            float baseSeconds = AnomalyService.RequiredSeconds(byScout, Manifest.Risk);

            if (State.TickOperation(dt, inRange, baseSeconds, PoiPauseReason.OutOfRange))
            {
                Vector3 fromAbs = ship.transform.position + FloatingOrigin.TotalOffset;
                List<(string systemId, float distance)> candidates = PoiCandidateBuilder.NearestUnknown(fromAbs, knowledge);
                AnomalyService.TryComplete(State, Manifest.PoiId, candidates, knowledge, frontierSignal);
            }
        }

        private void TickFacility(float dt)
        {
            bool inRange = DistanceToShip() <= PoiCatalog.IdentifyRange;
            float seconds = State.Choice == FacilityChoice.SensorRelay ? FacilityService.RelaySeconds(Manifest.Risk) : FacilityService.DismantleSeconds(Manifest.Risk);

            if (State.TickOperation(dt, inRange, seconds, PoiPauseReason.OutOfRange))
            {
                if (State.Choice == FacilityChoice.Dismantle)
                {
                    FacilityService.CompleteDismantle(State, Manifest);
                }
                else if (State.Choice == FacilityChoice.SensorRelay)
                {
                    Vector3 fromAbs = ship.transform.position + FloatingOrigin.TotalOffset;
                    List<(string systemId, float distance)> candidates = PoiCandidateBuilder.NearestUnknown(fromAbs, knowledge);
                    FacilityService.CompleteRelay(State, Manifest, shipLogistics?.Cargo, candidates, knowledge, frontierSignal);
                }
            }
        }

        // --- Wreck collection (ship direct, or an assigned Collector) ------------------
        private void TickWreckCollection(float dt)
        {
            bool shipInRange = DistanceToShip() <= PoiCatalog.IdentifyRange;
            if (shipInRange && shipLogistics != null)
            {
                // Ship collects directly into its own cargo — Safe and Unstable both allowed.
                if (State.Level == PoiKnowledgeLevel.Identified) State.StartOperation(null);
                string heldItem = null; float heldAmount = 0f;
                float taken = WreckService.Withdraw(State, ref heldItem, ref heldAmount, shipLogistics.Cargo.AvailableVolume, ResourceTransferService.VolumePerSecond, dt);
                if (taken > 0f && heldItem != null) shipLogistics.Cargo.Add(heldItem, taken); // bounded above by the ship's own free space -- always fits exactly
                WreckService.CheckDepletedAndResolve(State);
            }

            VehicleAgent collector = FindAssignedCollector();
            if (collector != null && Manifest.Risk == PoiRisk.Safe)
            {
                if (State.Level == PoiKnowledgeLevel.Identified) State.StartOperation(collector.VehicleId);
                string heldItem = collector.HeldItemId; float heldAmount = collector.HeldAmount;
                WreckService.Withdraw(State, ref heldItem, ref heldAmount, collector.Def.CargoCapacity, collector.Def.TransferVolumePerSecond, dt);
                collector.HeldItemId = heldItem; collector.HeldAmount = heldAmount;
                WreckService.CheckDepletedAndResolve(State);
            }
        }

        // --- Placeholder visuals ---------------------------------------------------
        private void BuildVisuals()
        {
            if (RefreshPrimaryVisual(force: true))
            {
                BuildSalvageVisuals();
                return;
            }

            // Before identification the world marker carries neither type nor
            // risk. The authored type-specific visual replaces it only once the
            // same PoiState authority used by UI reaches Identified.
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Marker";
            var collider = body.GetComponent<Collider>();
            if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            body.transform.SetParent(transform, false);
            body.transform.localScale = Vector3.one * 40f;
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateLit(new Color(0.45f, 0.5f, 0.55f), 0.2f, 0f);
            primaryVisual = body;
            shownPrimaryVisualId = "poi.generic.signal";

            BuildSalvageVisuals();
        }

        private bool RefreshPrimaryVisual(bool force = false)
        {
            NebulaVisualDefinition definition = ResolvePrimaryVisual();
            if (definition == null) return false;
            if (!force && primaryVisual != null && shownPrimaryVisualId == definition.VisualId) return true;

            GameObject replacement = NebulaVisualResolver.Instantiate(
                definition, transform, instanceName: $"Authored {definition.VisualId}");
            if (replacement == null) return false;
            if (primaryVisual != null) NebulaVisualResolver.DestroyInstance(primaryVisual);
            primaryVisual = replacement;
            shownPrimaryVisualId = definition.VisualId;
            return true;
        }

        private NebulaVisualDefinition ResolvePrimaryVisual()
        {
            if (Manifest == null || State == null || State.Level < PoiKnowledgeLevel.Identified) return null;
            if (Manifest.Type == PoiType.Wreck)
            {
                int count = State != null ? State.SalvageNodes.Count : 2;
                bool depleted = State != null
                    && (State.Level == PoiKnowledgeLevel.Resolved
                        || State.SalvageNodes.TrueForAll(node => node.Remaining <= 0.0001f));
                return NebulaVisualCatalog.WreckVariant(count, depleted);
            }

            if (Manifest.Type == PoiType.AbandonedFacility && State != null
                && State.Level == PoiKnowledgeLevel.Resolved
                && State.Choice == FacilityChoice.SensorRelay)
                return NebulaVisualCatalog.SensorRelayResolved;

            return NebulaVisualCatalog.TryGetPoi(Manifest.Type, out NebulaVisualDefinition definition)
                ? definition
                : null;
        }

        private void BuildSalvageVisuals()
        {
            if (salvageVisualsBuilt || Manifest.Type != PoiType.Wreck
                || State == null || State.Level < PoiKnowledgeLevel.Identified) return;
            salvageVisualsBuilt = true;
            var contents = new GameObject("Authored Salvage Contents");
            contents.transform.SetParent(transform, false);
            int count = State != null ? State.SalvageNodes.Count : 0;
            for (int i = 0; i < count; i++)
            {
                SalvageNode node = State.SalvageNodes[i];
                GameObject item = NebulaVisualResolver.Instantiate(
                    NebulaVisualCatalog.SalvageNode, contents.transform, instanceName: $"Salvage Node {i + 1:00}");
                if (item == null) item = NebulaVisualResolver.InstantiateItem(
                    node.ItemId, contents.transform, $"Salvage {i + 1:00} :: {node.ItemId}");
                else
                {
                    Transform cargoAnchor = NebulaVisualResolver.FindAnchor(item.transform, "anchor.cargo.output");
                    NebulaVisualResolver.InstantiateItem(
                        node.ItemId, cargoAnchor != null ? cargoAnchor : item.transform, $"Cargo :: {node.ItemId}");
                }
                if (item == null) continue;
                float angle = count > 0 ? i * Mathf.PI * 2f / count : 0f;
                item.transform.localPosition = new Vector3(Mathf.Cos(angle), 0.2f, Mathf.Sin(angle)) * 14f;
                item.transform.localRotation = Quaternion.Euler(0f, i * 57f, 0f);
                salvageVisuals.Add(item);
            }
            UpdateSalvageVisuals();
        }

        private void UpdateSalvageVisuals()
        {
            if (State == null || Manifest == null || Manifest.Type != PoiType.Wreck) return;
            int visualIndex = 0;
            for (int nodeIndex = 0; nodeIndex < State.SalvageNodes.Count && visualIndex < salvageVisuals.Count; nodeIndex++)
            {
                SalvageNode node = State.SalvageNodes[nodeIndex];
                if (!NebulaVisualCatalog.TryGetItem(node.ItemId, out _)) continue;
                GameObject item = salvageVisuals[visualIndex++];
                if (item != null) item.SetActive(node.Remaining > 0.0001f);
            }
        }
    }
}
