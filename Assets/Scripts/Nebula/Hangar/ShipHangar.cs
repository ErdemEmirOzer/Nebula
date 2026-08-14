using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.Hangar
{
    /// <summary>
    /// Ship-side host for the Hangar-Bot module (VS-007 §1): owns the command
    /// capacity ledger, the single-slot production queue, and the roster of
    /// produced vehicles. Ticks production every frame (Time.timeScale = 0
    /// while any modal is open zeroes dt, same as every other pure Tick(dt)
    /// system here) and forwards start/complete/cancel/launch/dock to an
    /// optional save hook, matching ShipModules/NavigationDirector.
    /// </summary>
    public class ShipHangar : MonoBehaviour
    {
        public int HangarTier { get; private set; }
        public HangarCommandLedger Command { get; } = new HangarCommandLedger();
        public HangarProductionService Production { get; private set; }
        public IReadOnlyList<VehicleAgent> Vehicles => vehicles;

        /// <summary>Set by the bootstrap once the save director exists.</summary>
        public Action RequestSave { get; set; }

        private readonly List<VehicleAgent> vehicles = new List<VehicleAgent>();
        private ShipController ship;
        private ShipLogistics logistics;
        private int nextEngineerMissionSequence;
        private int nextEscortMissionSequence;

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            logistics = GetComponent<ShipLogistics>();
        }

        /// <summary>Called once by the bootstrap after ProgressionService.Reset(), mirroring ShipModules.InitializeNewCampaign.</summary>
        public void InitializeNewCampaign()
        {
            HangarTier = 0;
            Command.SetCapacity(0);
            Production = new HangarProductionService(logistics.Cargo);
            Production.JobStarted += _ => RequestSave?.Invoke();
            Production.JobCancelled += _ => RequestSave?.Invoke();
            Production.JobCompleted += OnProductionCompleted;
            Production.JobUpgradeCompleted += OnUpgradeCompleted;
            foreach (VehicleAgent v in vehicles) if (v != null) Destroy(v.gameObject);
            vehicles.Clear();
        }

        /// <summary>Hangar-Bot module tier effect (VS-007 §1). Command capacity only, tiers never downgrade.</summary>
        public void ApplyHangarTier(int tier)
        {
            HangarTier = Mathf.Clamp(tier, 0, 2);
            Command.SetCapacity(HangarTier >= 1 ? ModuleCatalog.HangarCommandCapacityByTier[HangarTier - 1] : 0);
        }

        private void OnProductionCompleted(HangarProductionJob job, string vehicleId)
        {
            SpawnVehicle(vehicleId, VehicleCatalog.Get(job.VehicleClass, job.Mk));
            RequestSave?.Invoke();
        }

        /// <summary>VS-011 §5 / VS-015 §1: a completed Mk I-&gt;Mk II upgrade mutates the EXISTING vehicle's Def in place — no new vehicle, vehicle id preserved, command counter untouched (it already counted this launched vehicle). K/Z/G percentages are preserved across the swap, not full-healed.</summary>
        private void OnUpgradeCompleted(HangarProductionJob job)
        {
            VehicleAgent vehicle = FindVehicle(job.UpgradeVehicleId);
            VehicleDef targetDef = VehicleCatalog.Get(job.TargetVehicleId) ?? VehicleCatalog.Get(job.VehicleClass, job.Mk);
            if (vehicle != null && targetDef != null) vehicle.UpgradeDefInPlace(targetDef);
            RequestSave?.Invoke();
        }

        private VehicleAgent FindVehicle(string vehicleId)
        {
            if (vehicleId == null) return null;
            foreach (VehicleAgent v in vehicles) if (v != null && v.VehicleId == vehicleId) return v;
            return null;
        }

        private VehicleAgent SpawnVehicle(string vehicleId, VehicleDef def)
        {
            VehicleAgent agent = VehicleAgent.Create(ship, vehicleId, def);
            agent.DockedInHangar += OnVehicleDocked;
            vehicles.Add(agent);
            return agent;
        }

        private void OnVehicleDocked(VehicleAgent agent)
        {
            Command.Release(agent != null && agent.Def != null ? agent.Def.CommandCost : 1);
            RequestSave?.Invoke();
        }

        private void Update()
        {
            // A vehicle whose ShipController reference died freezes instead of
            // finishing its mission (VehicleAgent.Update). The hangar is the only
            // object that knows the live ship, so re-attaching is its job. The loop
            // runs only on the frame the ship is actually recovered, and re-binds the
            // EXISTING agents — nothing is respawned, so the roster never duplicates.
            if (ship == null)
            {
                ship = GetComponent<ShipController>();
                if (ship != null)
                    foreach (VehicleAgent v in vehicles) if (v != null && v.ShipMissing) v.RebindShip(ship);
            }

            // JobCompleted (which spawns the vehicle) fires from inside Tick via the event.
            Production?.Tick(Time.deltaTime);
        }

        // --- Production ----------------------------------------------------------
        /// <summary>
        /// Direct build with a player-chosen special material (Scout, Collector,
        /// Engineer Mk I, Escort Mk I).
        /// <para>
        /// P1-ECO-013: <c>hasAnyControlledPlanet</c> is the real fact now, not the
        /// service's <c>true</c> default. Four of the five pieces of VS-015 §1's
        /// Engineer Mk I gate already existed and were correct — the catalog declares
        /// <c>requiresControlledPlanet</c>, the service checks it, the reject reason
        /// exists and the Fleet page has Turkish for it — and the one caller simply
        /// never passed the fact, so the gate could not fire and the requirement the
        /// player was shown was not one the game applied. Passing it is a wiring fix,
        /// not a balance change: nothing about which vehicles need a Controlled planet,
        /// or what that costs, moves. The tutorial's mirror of these gates
        /// (<c>TutorialFhij.DiagnoseProduction</c>) and its Engineer prerequisite were
        /// corrected in the same change, so tutorial and normal play still refuse for
        /// the same reasons.
        /// </para>
        /// </summary>
        public HangarProductionRejectReason TryStartProduction(VehicleClass vehicleClass, string chosenMaterialId, int mk = 1)
        {
            return Production.TryStartProduction(vehicleClass, chosenMaterialId, ProgressionService.State.Level, HangarTier, mk,
                ExtractionState.HasAnyControlledPlanet());
        }

        public void CancelProduction() => Production.CancelActiveJob();

        /// <summary>The ship cargo every build is paid from — read-only, so a build picker can show real stock beside a real cost instead of guessing.</summary>
        public LogisticsCargo Cargo => logistics != null ? logistics.Cargo : null;

        /// <summary>VS-011 §5: Mk I-&gt;Mk II in-place upgrade — vehicle must be hangar-parked, cargo empty, no active mission, no active Collector route. Kept for the exact existing Collector-only call shape.</summary>
        public HangarUpgradeRejectReason TryStartMk2Upgrade(VehicleAgent vehicle) => TryStartVehicleUpgrade(vehicle);

        /// <summary>VS-015 §1: generic Mk I-&gt;Mk II in-place upgrade — works for Collector, Engineer, and Escort alike via the stable vehicle-id/recipe table, same eligibility gates as the Collector-only path.</summary>
        public HangarUpgradeRejectReason TryStartVehicleUpgrade(VehicleAgent vehicle)
        {
            if (vehicle == null || vehicle.Def == null) return HangarUpgradeRejectReason.UnknownVehicle;
            if (!VehicleCatalog.TryGetUpgradeRecipe(vehicle.Def.Id, out _)) return HangarUpgradeRejectReason.NotMkI;
            if (!vehicle.Mission.IsIdleInHangar) return HangarUpgradeRejectReason.VehicleNotInHangar;
            if (vehicle.HeldAmount > 0.0001f) return HangarUpgradeRejectReason.VehicleCargoNotEmpty;
            if (LogisticsState.FindRouteByVehicle(vehicle.VehicleId) != null) return HangarUpgradeRejectReason.VehicleHasActiveRoute;
            if (vehicle.EngineerTask != null || vehicle.EscortState != null || vehicle.ResupplyTask != null) return HangarUpgradeRejectReason.VehicleHasActiveRoute;

            HangarUpgradeRejectReason reason = Production.TryStartUpgrade(vehicle.VehicleId, vehicle.Def.Id, true, ProgressionService.State.Level, HangarTier);
            if (reason == HangarUpgradeRejectReason.None) RequestSave?.Invoke();
            return reason;
        }

        /// <summary>VS-015 §1: Mk II fixed-recipe direct build (Engineer/Escort — no player-chosen material).</summary>
        public HangarProductionRejectReason TryStartFixedProduction(string vehicleId) =>
            Production.TryStartFixedProduction(vehicleId, ProgressionService.State.Level, HangarTier);

        // --- Missions --------------------------------------------------------------
        /// <summary>Absolute-position lookup (VS-008 §4) — never relies on a materialized Transform. Null with no ship at all, the same answer as "inside no system".</summary>
        public string CurrentSystemId => ship != null
            && StarSystemRegistry.TryFindContaining(ship.transform.position + FloatingOrigin.TotalOffset, out StarSystemRecord record)
            ? record.SystemId : null;

        public enum LaunchRejectReason { None, VehicleNotInHangar, NoCommandCapacity, TargetNotInShipSystem }

        /// <summary>Launches an in-hangar vehicle at a same-system target (a planet worldId for Scout, a drill worldId for Collector).</summary>
        public LaunchRejectReason TryLaunch(VehicleAgent vehicle, MissionKind kind, string targetWorldId)
        {
            if (vehicle == null || !vehicle.Mission.CanLaunch || vehicle.EngineerTask != null || vehicle.EscortState != null || vehicle.ResupplyTask != null)
                return LaunchRejectReason.VehicleNotInHangar;

            string systemId = CurrentSystemId;
            string targetSystemId = WorldIdCodec.SystemIdFromPlanetWorldId(targetWorldId);
            if (systemId == null || targetSystemId == null || systemId != targetSystemId) return LaunchRejectReason.TargetNotInShipSystem;

            if (!Command.TryReserve(vehicle.Def.CommandCost)) return LaunchRejectReason.NoCommandCapacity;

            vehicle.Launch(kind, targetWorldId, systemId);
            RequestSave?.Invoke();
            return LaunchRejectReason.None;
        }

        public void Recall(VehicleAgent vehicle)
        {
            if (vehicle == null) return;
            vehicle.Recall();
            RequestSave?.Invoke();
        }

        // --- VS-011 §4: persistent Collector routes ---------------------------------
        /// <summary>Creates a persistent drill-&gt;Collector-&gt;center route, reserving a command slot exactly like a normal launch — only while the ship is physically in the same system as both the drill and the center.</summary>
        public RouteCreateRejectReason TryCreateRoute(VehicleAgent vehicle, DrillStructure drill, LogisticsCenter center, RoutePriority priority)
        {
            string systemId = CurrentSystemId;
            bool sameSystem = systemId != null && center != null && center.SystemId == systemId
                               && drill != null && WorldIdCodec.SystemIdFromPlanetWorldId(drill.WorldId) == systemId;

            bool vehicleIsCollector = vehicle != null && vehicle.Def != null && vehicle.Def.Class == VehicleClass.Collector;
            bool vehicleEligible = vehicle != null && vehicle.Mission.CanLaunch && vehicle.EngineerTask == null && vehicle.EscortState == null && vehicle.ResupplyTask == null;
            bool vehicleAlreadyRouted = vehicle != null && LogisticsState.FindRouteByVehicle(vehicle.VehicleId) != null;
            bool drillAlreadyRouted = drill != null && LogisticsState.FindRouteByDrill(drill.WorldId) != null;
            bool drillActive = drill != null && drill.Runtime.State == DrillLifecycle.Active;
            bool centerActive = center != null && center.Runtime.IsActive;
            int routeCount = systemId != null ? LogisticsState.RouteCountForSystem(systemId) : 0;
            int maxRoutes = center != null ? center.Runtime.Def.MaxRoutes : 0;
            bool hasCommandCapacity = Command.Capacity - Command.Used > 0;

            RouteCreateRejectReason reason = CollectorRouteService.CheckCreate(centerActive, routeCount, maxRoutes,
                vehicleIsCollector, vehicleEligible, vehicleAlreadyRouted, drillAlreadyRouted, drillActive, sameSystem, hasCommandCapacity);
            if (reason != RouteCreateRejectReason.None) return reason;

            if (!Command.TryReserve(vehicle.Def.CommandCost)) return RouteCreateRejectReason.NoCommandCapacity;

            var route = new CollectorRoute(LogisticsState.NextRouteId(), systemId, drill.WorldId, vehicle.VehicleId, priority);
            LogisticsState.AddRoute(route);
            vehicle.AssignRoute(route);
            RequestSave?.Invoke();
            return RouteCreateRejectReason.None;
        }

        /// <summary>Deletes a route: the vehicle physically returns home first (never a teleport) — the route itself, and the command slot, are only released once it actually docks (VehicleAgent.TickRouteReturning).</summary>
        public void DeleteRoute(CollectorRoute route)
        {
            if (route == null) return;
            VehicleAgent vehicle = null;
            foreach (VehicleAgent v in vehicles) if (v != null && v.VehicleId == route.VehicleId) { vehicle = v; break; }
            vehicle?.BeginReturningFromRoute();
            RequestSave?.Invoke();
        }

        // --- VS-015 §3: Engineer maintenance mission --------------------------------
        public enum EngineerSendRejectReason { None, VehicleNotEligible, TargetNotEligible, NoCommandCapacity, NothingToRepair, InsufficientResources, TargetHasNoDurability }

        private bool VehicleIsIdle(VehicleAgent v) => v != null && v.Mission.CanLaunch && v.Route == null && v.PoiTask == null
            && v.EngineerTask == null && v.EscortState == null && v.ResupplyTask == null;

        /// <summary>
        /// Manual (player-initiated) Engineer maintenance/recovery mission —
        /// resource is reserved atomically here (Reserve phase) from the ship's
        /// own cargo or the target planet's Maintenance Reserve, the vehicle then
        /// physically travels, waits for 8s continuous safety, does the work, and
        /// returns any unused reservation.
        /// </summary>
        public EngineerSendRejectReason TrySendEngineer(VehicleAgent vehicle, EngineerTargetKind targetKind, string targetId,
            EngineerSourceKind sourceKind, string chosenMaterialId, bool playerManuallyStarted = true)
        {
            if (vehicle == null || vehicle.Def == null || vehicle.Def.Class != VehicleClass.Engineer || !VehicleIsIdle(vehicle))
                return EngineerSendRejectReason.VehicleNotEligible;
            string systemId = CurrentSystemId;
            if (systemId == null) return EngineerSendRejectReason.TargetNotEligible;

            if (targetKind == EngineerTargetKind.Drill) return EngineerSendRejectReason.TargetHasNoDurability;

            var workKind = targetKind == EngineerTargetKind.DisabledVehicle ? EngineerWorkKind.Recover : EngineerWorkKind.Repair;
            if (!ComputeEngineerPlan(targetKind, targetId, workKind, out float plannedHp, out int reservedParts, out float reservedMaterial))
                return EngineerSendRejectReason.NothingToRepair;

            LogisticsCargo source = sourceKind == EngineerSourceKind.ShipCargo ? logistics?.Cargo : FindMaintenanceReserveCargo(systemId);
            if (source == null) return EngineerSendRejectReason.TargetNotEligible;

            string missionId = $"engineer-{vehicle.VehicleId}-{nextEngineerMissionSequence:0000}";
            string ownerId = $"engineer-mission:{missionId}";
            var costs = new List<(string, float)>();
            if (reservedParts > 0) costs.Add((ProcessingCatalog.BasicPartId, reservedParts));
            if (reservedMaterial > 0f && !string.IsNullOrEmpty(chosenMaterialId)) costs.Add((chosenMaterialId, reservedMaterial));
            if (costs.Count > 0 && !source.TryReserveAll(ownerId, costs)) return EngineerSendRejectReason.InsufficientResources;

            if (!Command.TryReserve(vehicle.Def.CommandCost)) { source.ReleaseReservation(ownerId); return EngineerSendRejectReason.NoCommandCapacity; }

            nextEngineerMissionSequence++;
            var task = new EngineerMissionState(missionId, vehicle.VehicleId, systemId, targetKind, targetId, workKind, sourceKind,
                chosenMaterialId, playerManuallyStarted, reservedParts, reservedMaterial);
            task.SetPlannedRestoredHp(plannedHp);
            vehicle.AssignEngineerTask(task);
            RequestSave?.Invoke();
            return EngineerSendRejectReason.None;
        }

        /// <summary>Computes the field-cap gap (plannedHp) and the atomic reservation (parts + one material) for a Repair/Recover target, per VS-015 §3's cost formulas. Returns false if there is nothing to do (already at/above cap, or target not found).</summary>
        private bool ComputeEngineerPlan(EngineerTargetKind kind, string targetId, EngineerWorkKind workKind, out float plannedHp, out int reservedParts, out float reservedMaterial)
        {
            plannedHp = 0f; reservedParts = 0; reservedMaterial = 0f;
            if (workKind == EngineerWorkKind.Recover)
            {
                if (!VehicleAgent.Registry.TryGetValue(targetId, out VehicleAgent disabledVehicle) || disabledVehicle == null || !disabledVehicle.IsRecoverable) return false;
                plannedHp = 60f; // fixed recovery duration in seconds (Engineer's DisabledVehicleRecoverSeconds is applied by the caller via Def)
                VehicleDef disabledVehicleDef = disabledVehicle.Def;
                var recipe = new List<(string, float)> { (ProcessingCatalog.BasicPartId, disabledVehicleDef.BasicPartCost) };
                foreach (var (itemId, amount) in EngineerRepairCatalog.RecoveryCost(recipe)) { if (itemId == ProcessingCatalog.BasicPartId) reservedParts = amount; }
                return true;
            }
            if (kind == EngineerTargetKind.Ship) { plannedHp = 1f; return true; } // main ship repair reuses ShipRepairService's own package cost, not counted here
            if (kind == EngineerTargetKind.Drill) return false; // drills have no Durability model — nothing to repair

            if (kind == EngineerTargetKind.Vehicle)
            {
                if (!VehicleAgent.Registry.TryGetValue(targetId, out VehicleAgent damagedVehicle) || damagedVehicle == null
                    || damagedVehicle.DurabilityRef == null || damagedVehicle.Stranded) return false;
                Durability vd = damagedVehicle.DurabilityRef;
                bool inCombat = StarSystemRegistry.Streaming?.CombatEncounter?.Encounter?.IsOpen ?? false;
                float vehicleCap = inCombat ? EngineerRepairCatalog.VehicleCombatCapFraction : EngineerRepairCatalog.VehicleNonCombatCapFraction;
                float missingArmorV = Mathf.Max(0f, vd.ArmorMax * vehicleCap - vd.Armor);
                float missingHullV = Mathf.Max(0f, vd.HullMax * vehicleCap - vd.Hull);
                plannedHp = missingArmorV + missingHullV;
                if (plannedHp <= 0.0001f) return false;
                float combinedMaxV = vd.ArmorMax + vd.HullMax;
                float missingFraction = combinedMaxV > 0f ? plannedHp / combinedMaxV : 0f;
                reservedParts = EngineerRepairCatalog.VehicleRepairItemCost(damagedVehicle.Def.BasicPartCost, missingFraction);
                reservedMaterial = EngineerRepairCatalog.VehicleRepairItemCost(damagedVehicle.Def.SpecialMaterialCost, missingFraction);
                return true;
            }

            SurfaceStructure target = null;
            foreach (SurfaceStructure s in ExtractionState.Structures) if (s != null && s.TargetId == targetId) { target = s; break; }
            if (target == null || target.Runtime == null || target.Runtime.Durability == null) return false;
            Durability durability = target.Runtime.Durability;

            float armorCap = durability.ArmorMax * EngineerRepairCatalog.StructureFieldCapFraction;
            float hullCap = durability.HullMax * EngineerRepairCatalog.StructureFieldCapFraction;
            float missingArmor = Mathf.Max(0f, armorCap - durability.Armor);
            float missingHull = Mathf.Max(0f, hullCap - durability.Hull);
            plannedHp = missingArmor + missingHull;
            if (plannedHp <= 0.0001f) return false;

            float combinedMax = durability.ArmorMax + durability.HullMax;
            float restoredFraction = combinedMax > 0f ? plannedHp / combinedMax : 0f;
            StructureTierDef tierDef = PlanetaryDefenseCatalog.Get(target.Type, target.Runtime.Tier);
            int basePartsCost = tierDef != null ? tierDef.PartsCost : SurfaceStructureCatalog.Get(target.Type).PartsCost;
            int baseMaterialCost = tierDef != null && tierDef.MaterialCosts.Length > 0 ? tierDef.MaterialCosts[0].Amount : SurfaceStructureCatalog.Get(target.Type).MaterialCost;
            reservedParts = EngineerRepairCatalog.StructureRepairItemCost(basePartsCost, restoredFraction);
            reservedMaterial = EngineerRepairCatalog.StructureRepairItemCost(baseMaterialCost, restoredFraction);
            return true;
        }

        /// <summary>VS-015.1 §2: one real same-system Engineer target the player can pick in the UI — every field is computed from live data, never fabricated.</summary>
        public readonly struct EngineerTargetCandidate
        {
            public readonly EngineerTargetKind Kind;
            public readonly string TargetId;
            public readonly string DisplayName;
            public readonly EngineerWorkKind WorkKind;
            public readonly float ArmorFraction;
            public readonly float HullFraction;
            public readonly float ReachableCapFraction;
            public readonly int RequiredParts;
            public readonly float RequiredMaterial;
            public readonly float EstimatedTravelSeconds;
            public readonly float EstimatedWorkSeconds;
            public readonly string Risk;

            public EngineerTargetCandidate(EngineerTargetKind kind, string targetId, string displayName, EngineerWorkKind workKind,
                float armorFraction, float hullFraction, float reachableCapFraction, int requiredParts, float requiredMaterial,
                float estimatedTravelSeconds, float estimatedWorkSeconds, string risk)
            {
                Kind = kind; TargetId = targetId; DisplayName = displayName; WorkKind = workKind;
                ArmorFraction = armorFraction; HullFraction = hullFraction; ReachableCapFraction = reachableCapFraction;
                RequiredParts = requiredParts; RequiredMaterial = requiredMaterial;
                EstimatedTravelSeconds = estimatedTravelSeconds; EstimatedWorkSeconds = estimatedWorkSeconds; Risk = risk;
            }
        }

        /// <summary>VS-015.1 §2: the real same-system target list for a specific Engineer — main ship, damaged/disabled structures (Command/Laser/Plasma/Sensor/Maintenance), damaged support vehicles, and disabled vehicles. Drill is intentionally never listed (TargetHasNoDurability).</summary>
        public List<EngineerTargetCandidate> BuildEngineerTargetCandidates(VehicleAgent engineer)
        {
            var list = new List<EngineerTargetCandidate>();
            string systemId = CurrentSystemId;
            if (engineer == null || systemId == null) return list;

            string risk = CurrentSystemRisk();
            Vector3 fromPos = engineer.transform.position;

            var shipCombat = ship != null ? ship.GetComponent<ShipCombat>() : null;
            if (shipCombat != null && shipCombat.State != null && shipCombat.State.Durability != null)
            {
                Durability shipDurability = shipCombat.State.Durability;
                float armorFrac = shipDurability.ArmorMax > 0f ? shipDurability.Armor / shipDurability.ArmorMax : 1f;
                float hullFrac = shipDurability.HullMax > 0f ? shipDurability.Hull / shipDurability.HullMax : 1f;
                if (armorFrac < 0.9999f || hullFrac < 0.9999f)
                    list.Add(new EngineerTargetCandidate(EngineerTargetKind.Ship, "ship", "Ana Gemi", EngineerWorkKind.Repair,
                        armorFrac, hullFrac, EngineerRepairCatalog.StructureFieldCapFraction, 0, 0f,
                        EstimateTravel(fromPos, ship.transform.position, engineer.Def.Speed), 0f, risk));
            }

            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || WorldIdCodec.SystemIdFromPlanetWorldId(s.WorldId) != systemId || !s.Runtime.IsActive) continue;
                Durability d = s.Runtime.Durability;
                float cap = EngineerRepairCatalog.StructureFieldCapFraction;
                float armorFrac = d.ArmorMax > 0f ? d.Armor / d.ArmorMax : 1f;
                float hullFrac = d.HullMax > 0f ? d.Hull / d.HullMax : 1f;
                if (armorFrac >= cap - 0.0001f && hullFrac >= cap - 0.0001f) continue;
                if (!ComputeEngineerPlan(EngineerTargetKind.Structure, s.TargetId, EngineerWorkKind.Repair, out _, out int parts, out float material)) continue;
                list.Add(new EngineerTargetCandidate(s.Type == SurfaceStructureType.SensorArray || s.Type == SurfaceStructureType.MaintenancePlatform
                        ? EngineerTargetKind.SupportStructure : EngineerTargetKind.Structure,
                    s.TargetId, $"{s.Type} ({s.WorldId})", EngineerWorkKind.Repair, armorFrac, hullFrac, cap, parts, material,
                    EstimateTravel(fromPos, s.transform.position, engineer.Def.Speed), 0f, risk));
            }

            foreach (VehicleAgent v in VehicleAgent.Registry.Values)
            {
                if (v == null || v.HomeSystemId != systemId || v == engineer || v.DurabilityRef == null) continue;
                if (v.Stranded)
                {
                    if (!ComputeEngineerPlan(EngineerTargetKind.DisabledVehicle, v.VehicleId, EngineerWorkKind.Recover, out _, out int rp, out float rm)) continue;
                    list.Add(new EngineerTargetCandidate(EngineerTargetKind.DisabledVehicle, v.VehicleId, $"{v.Def.DisplayName} [{v.VehicleId}] (DEVRE DIŞI)",
                        EngineerWorkKind.Recover, v.Armor / Mathf.Max(1f, v.DurabilityRef.ArmorMax), 0f, EngineerRepairCatalog.RecoveryCompleteHullFraction,
                        rp, 0f, EstimateTravel(fromPos, v.transform.position, engineer.Def.Speed), v.Def.DisabledVehicleRecoverSeconds, risk));
                    continue;
                }
                if (!v.gameObject.activeSelf) continue;
                Durability vd = v.DurabilityRef;
                bool inCombat = StarSystemRegistry.Streaming?.CombatEncounter?.Encounter?.IsOpen ?? false;
                float vehicleCap = inCombat ? EngineerRepairCatalog.VehicleCombatCapFraction : EngineerRepairCatalog.VehicleNonCombatCapFraction;
                float armorFracV = vd.ArmorMax > 0f ? vd.Armor / vd.ArmorMax : 1f;
                float hullFracV = vd.HullMax > 0f ? vd.Hull / vd.HullMax : 1f;
                if (armorFracV >= vehicleCap - 0.0001f && hullFracV >= vehicleCap - 0.0001f) continue;
                if (!ComputeEngineerPlan(EngineerTargetKind.Vehicle, v.VehicleId, EngineerWorkKind.Repair, out _, out int vparts, out float vmaterial)) continue;
                list.Add(new EngineerTargetCandidate(EngineerTargetKind.Vehicle, v.VehicleId, $"{v.Def.DisplayName} [{v.VehicleId}]",
                    EngineerWorkKind.Repair, armorFracV, hullFracV, vehicleCap, vparts, vmaterial,
                    EstimateTravel(fromPos, v.transform.position, engineer.Def.Speed), 0f, risk));
            }

            return list;
        }

        private static float EstimateTravel(Vector3 from, Vector3 to, float speed) =>
            speed > 0.01f ? Vector3.Distance(from, to) / speed : 0f;

        private string CurrentSystemRisk()
        {
            var director = StarSystemRegistry.Streaming?.CombatEncounter;
            if (director == null) return "Belirsiz";
            return director.Encounter.IsOpen ? "Tehlikeli" : "Güvenli";
        }

        /// <summary>VS-015.1 §2: picks a real processed material with at least `amount` available in the chosen source, or null if none qualifies — used by the UI to auto-select a material for a target that needs one, without a second dedicated picker panel.</summary>
        public string FindAffordableMaterial(float amount, EngineerSourceKind source)
        {
            LogisticsCargo cargo = source == EngineerSourceKind.ShipCargo ? logistics?.Cargo : FindMaintenanceReserveCargo(CurrentSystemId);
            if (cargo == null) return null;
            foreach (string id in ProcessingCatalog.MaterialIds) if (cargo.GetAvailable(id) >= amount - 0.0001f) return id;
            return null;
        }

        private LogisticsCargo FindMaintenanceReserveCargo(string systemId)
        {
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.Type == SurfaceStructureType.Command && s.MaintenanceReserve != null && WorldIdCodec.SystemIdFromPlanetWorldId(s.WorldId) == systemId)
                    return s.MaintenanceReserve.Cargo;
            return null;
        }

        public void CancelEngineerMission(VehicleAgent vehicle) => vehicle?.RequestEngineerCancel();

        // --- VS-015 §4: Escort protection assignment/commands -----------------------
        public enum EscortAssignRejectReason { None, VehicleNotEligible, NoCommandCapacity }

        /// <summary>VS-015.1 §3: one real same-system protectable target the player can pick for an Escort.</summary>
        public readonly struct EscortProtectableTarget
        {
            public readonly string TargetId;
            public readonly string DisplayName;
            public readonly Combat.TargetCategory Category;
            public EscortProtectableTarget(string targetId, string displayName, Combat.TargetCategory category)
            { TargetId = targetId; DisplayName = displayName; Category = category; }
        }

        /// <summary>VS-015.1 §3: main ship, every Engineer/Collector physically in the system, this system's Command Center, and every active defense structure.</summary>
        public List<EscortProtectableTarget> BuildEscortProtectableTargets()
        {
            var list = new List<EscortProtectableTarget>();
            string systemId = CurrentSystemId;
            if (systemId == null) return list;

            list.Add(new EscortProtectableTarget("ship", "Ana Gemi", Combat.TargetCategory.Ship));

            foreach (VehicleAgent v in vehicles)
            {
                if (v == null || v.HomeSystemId != systemId || !v.gameObject.activeSelf) continue;
                if (v.Def.Class == VehicleClass.Engineer || v.Def.Class == VehicleClass.Collector)
                    list.Add(new EscortProtectableTarget(v.VehicleId, $"{v.Def.DisplayName} [{v.VehicleId}]", Combat.TargetCategory.Vehicle));
            }

            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || WorldIdCodec.SystemIdFromPlanetWorldId(s.WorldId) != systemId || !s.Runtime.IsActive) continue;
                if (s.Type == SurfaceStructureType.Command || SurfaceStructureCatalog.IsActiveDefenseWeapon(s.Type))
                    list.Add(new EscortProtectableTarget(s.TargetId, $"{s.Type} ({s.WorldId})", Combat.TargetCategory.Structure));
            }

            return list;
        }

        public EscortAssignRejectReason TryAssignEscort(VehicleAgent vehicle, string protectedTargetId, Combat.TargetCategory category)
        {
            if (vehicle == null || vehicle.Def == null || vehicle.Def.Class != VehicleClass.Escort || !VehicleIsIdle(vehicle))
                return EscortAssignRejectReason.VehicleNotEligible;
            if (!Command.TryReserve(vehicle.Def.CommandCost)) return EscortAssignRejectReason.NoCommandCapacity;

            string missionId = $"escort-{vehicle.VehicleId}-{nextEscortMissionSequence:0000}";
            nextEscortMissionSequence++;
            var state = new EscortState(vehicle.VehicleId, missionId, protectedTargetId, category, playerManuallyStarted: true);
            vehicle.AssignEscort(state);
            RequestSave?.Invoke();
            return EscortAssignRejectReason.None;
        }

        public void SetEscortCommand(VehicleAgent vehicle, EscortCommand command)
        {
            if (vehicle?.EscortState == null) return;
            vehicle.EscortState.SetCommand(command);
            if (command == EscortCommand.ReturnToHangar) { vehicle.ClearEscort(); vehicle.Recall(); }
            RequestSave?.Invoke();
        }

        public void SetEscortStance(VehicleAgent vehicle, EscortStance stance)
        {
            vehicle?.EscortState?.SetStance(stance);
            RequestSave?.Invoke();
        }

        // --- VS-015.1 §4: Collector Mk II Maintenance Resupply ----------------------
        public enum MaintenanceResupplyRejectReason { None, VehicleNotEligible, TargetNotEligible, InsufficientCargo, NoCommandCapacity }
        private int nextResupplyMissionSequence;

        /// <summary>Collector Mk II only — physically loads the chosen item from the ship's own cargo, then flies it to the selected same-system Command Center's Maintenance Reserve and back.</summary>
        public MaintenanceResupplyRejectReason TryStartMaintenanceResupply(VehicleAgent vehicle, string itemId, float amount, string targetCommandStructureId)
        {
            if (vehicle == null || vehicle.Def == null || vehicle.Def.Class != VehicleClass.Collector || vehicle.Def.Mk < 2 || !VehicleIsIdle(vehicle))
                return MaintenanceResupplyRejectReason.VehicleNotEligible;
            string systemId = CurrentSystemId;
            if (systemId == null) return MaintenanceResupplyRejectReason.TargetNotEligible;
            if (!MaintenanceReserve.IsAccepted(itemId)) return MaintenanceResupplyRejectReason.TargetNotEligible;

            SurfaceStructure target = null;
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.TargetId == targetCommandStructureId && s.Type == SurfaceStructureType.Command
                    && WorldIdCodec.SystemIdFromPlanetWorldId(s.WorldId) == systemId) { target = s; break; }
            if (target == null || target.MaintenanceReserve == null) return MaintenanceResupplyRejectReason.TargetNotEligible;

            LogisticsCargo shipCargo = logistics?.Cargo;
            if (shipCargo == null || shipCargo.GetAvailable(itemId) < amount - 0.0001f) return MaintenanceResupplyRejectReason.InsufficientCargo;
            float loadAmount = Mathf.Min(amount, vehicle.Def.CargoCapacity);
            if (!Command.TryReserve(vehicle.Def.CommandCost)) return MaintenanceResupplyRejectReason.NoCommandCapacity;

            // P1-ECO-016: the load is checked, and a refused load gives the command slot
            // straight back rather than launching an empty resupply run that has already
            // cost the player capacity.
            if (!shipCargo.TryConsumeImmediate(itemId, loadAmount))
            {
                Command.Release(vehicle.Def.CommandCost);
                return MaintenanceResupplyRejectReason.InsufficientCargo;
            }
            string missionId = $"resupply-{vehicle.VehicleId}-{nextResupplyMissionSequence:0000}";
            nextResupplyMissionSequence++;
            var mission = new MaintenanceResupplyMission(missionId, vehicle.VehicleId, systemId, targetCommandStructureId, itemId, loadAmount);
            vehicle.AssignResupplyTask(mission, itemId, loadAmount);
            RequestSave?.Invoke();
            return MaintenanceResupplyRejectReason.None;
        }

        public void CancelMaintenanceResupply(VehicleAgent vehicle) => vehicle?.RequestResupplyCancel();

        /// <summary>Convenience wrapper: auto-picks the ship's most-abundant whitelisted item and the nearest same-system active Command Center, then starts a real MaintenanceResupply run — same underlying atomic path as TryStartMaintenanceResupply, just without a dedicated item/target picker UI.</summary>
        public MaintenanceResupplyRejectReason TryStartQuickResupply(VehicleAgent vehicle)
        {
            LogisticsCargo cargo = logistics?.Cargo;
            if (cargo == null) return MaintenanceResupplyRejectReason.VehicleNotEligible;

            string bestItem = null; float bestAmount = 0f;
            foreach (var kv in cargo.Items)
            {
                if (!MaintenanceReserve.IsAccepted(kv.Key)) continue;
                float available = cargo.GetAvailable(kv.Key);
                if (available > bestAmount) { bestAmount = available; bestItem = kv.Key; }
            }
            if (bestItem == null || bestAmount <= 0.0001f) return MaintenanceResupplyRejectReason.InsufficientCargo;

            string systemId = CurrentSystemId;
            SurfaceStructure nearestCommand = null;
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.Type == SurfaceStructureType.Command && s.MaintenanceReserve != null
                    && WorldIdCodec.SystemIdFromPlanetWorldId(s.WorldId) == systemId) { nearestCommand = s; break; }
            if (nearestCommand == null) return MaintenanceResupplyRejectReason.TargetNotEligible;

            return TryStartMaintenanceResupply(vehicle, bestItem, bestAmount, nearestCommand.TargetId);
        }

        // --- Save/load -------------------------------------------------------------
        /// <summary>Restore-only: rebuilds the vehicle roster and the production job exactly as saved, without spending or awarding anything.</summary>
        public VehicleAgent RestoreVehicle(string vehicleId, VehicleDef def)
        {
            return SpawnVehicle(vehicleId, def);
        }

        public void RestoreCommandUsed(int used) => Command.Restore(used);

        /// <summary>Load-time cleanup: removes every live vehicle before restoring the saved roster, so a repeated manual load never duplicates the fleet.</summary>
        public void DestroyAllVehicles()
        {
            foreach (VehicleAgent v in vehicles) if (v != null) Destroy(v.gameObject);
            vehicles.Clear();
        }

        /// <summary>Drops just the roster reference for a vehicle whose GameObject the caller already destroyed itself (VS-008 system demotion: the vehicle becomes a SystemDelta record instead).</summary>
        public void RemoveVehicleReference(VehicleAgent vehicle) => vehicles.Remove(vehicle);
    }
}
