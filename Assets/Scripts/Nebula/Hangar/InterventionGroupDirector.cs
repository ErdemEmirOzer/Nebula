using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Hangar
{
    /// <summary>
    /// VS-015 §6: central per-frame owner of the CURRENT system's single
    /// physical intervention group (Manual/DefendInPlace/SystemIntervention)
    /// and each Command Center's automatic Maintenance Reserve policy
    /// (Emergency/Full Field). Only ever acts on the currently Full system —
    /// mirrors CollectorRouteDirector's composition (Initialize(ShipHangar),
    /// no FindObjectOfType, no second locator). VS-015 explicitly has no
    /// remote/1Hz treatment for this — a demoted system simply stops being
    /// ticked here until promoted back to Full.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public class InterventionGroupDirector : MonoBehaviour
    {
        private ShipHangar hangar;
        public InterventionGroup Group { get; private set; }

        public void Initialize(ShipHangar shipHangar) => hangar = shipHangar;

        public void EnsureGroupForSystem(string systemId)
        {
            if (Group != null && Group.SystemId == systemId) return;
            Group = new InterventionGroup(systemId);
        }

        /// <summary>VS-015.1 §1: restore-only, replaces whatever transient group exists with the exact saved one — never validated against live state (the next Update tick re-derives target/holding from real data).</summary>
        public void RestoreGroup(InterventionGroup group) => Group = group;

        public bool AddMember(string vehicleId)
        {
            EnsureCurrentSystemGroup();
            return Group != null && Group.AddMember(vehicleId);
        }

        public void RemoveMember(string vehicleId) => Group?.RemoveMember(vehicleId);
        public void SetPolicy(InterventionPolicy policy) { EnsureCurrentSystemGroup(); Group?.SetPolicy(policy); }

        private void EnsureCurrentSystemGroup()
        {
            string systemId = hangar != null ? hangar.CurrentSystemId : null;
            if (systemId == null) return;
            EnsureGroupForSystem(systemId);
        }

        private void Update()
        {
            if (hangar == null) return;
            string systemId = hangar.CurrentSystemId;
            if (systemId == null) return;
            EnsureGroupForSystem(systemId);

            TickMaintenancePolicies(systemId);
            TickInterventionGroup(systemId);
        }

        // --- VS-015 §5: automatic Maintenance Reserve policies ------------------
        private void TickMaintenancePolicies(string systemId)
        {
            foreach (SurfaceStructure command in ExtractionState.Structures)
            {
                if (command == null || command.Type != SurfaceStructureType.Command || command.MaintenanceReserve == null) continue;
                if (WorldIdCodec.SystemIdFromPlanetWorldId(command.WorldId) != systemId) continue;
                MaintenanceReserve reserve = command.MaintenanceReserve;
                if (reserve.Policy == MaintenancePolicy.Manual) continue;
                if (!string.IsNullOrEmpty(reserve.ActiveEngineerTargetId)) continue; // one active Engineer-target per planet

                string targetId = ResolvePolicyTarget(command, reserve);
                if (targetId == null) continue;

                VehicleAgent freeEngineer = FindIdleEngineerInHangar();
                if (freeEngineer == null) { reserve.TrySetActiveTarget(targetId); continue; } // queued -- no Engineer available yet

                EngineerTargetKind kind = targetId == command.TargetId ? EngineerTargetKind.Structure : EngineerTargetKind.SupportStructure;
                var reason = hangar.TrySendEngineer(freeEngineer, kind, targetId, EngineerSourceKind.PlanetMaintenanceReserve, PickReserveMaterial(reserve), playerManuallyStarted: false);
                if (reason == ShipHangar.EngineerSendRejectReason.None) reserve.TrySetActiveTarget(targetId);
            }
        }

        private string PickReserveMaterial(MaintenanceReserve reserve)
        {
            foreach (var kv in reserve.Cargo.Items) if (kv.Value > 0f && kv.Key != ProcessingCatalog.BasicPartId) return kv.Key;
            return ProcessingCatalog.MaterialIds.Length > 0 ? ProcessingCatalog.MaterialIds[0] : null;
        }

        private string ResolvePolicyTarget(SurfaceStructure command, MaintenanceReserve reserve)
        {
            bool commandNeedsRepair = command.Runtime.Durability.Armor < command.Runtime.Durability.ArmorMax * EngineerRepairCatalog.StructureFieldCapFraction
                || command.Runtime.Durability.Hull < command.Runtime.Durability.HullMax * EngineerRepairCatalog.StructureFieldCapFraction;

            var activeDefenses = new List<string>();
            var others = new List<string>();
            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || s.WorldId != command.WorldId || s == command || !s.Runtime.IsActive) continue;
                bool needsRepair = s.Runtime.Durability.Armor < s.Runtime.Durability.ArmorMax * EngineerRepairCatalog.StructureFieldCapFraction
                    || s.Runtime.Durability.Hull < s.Runtime.Durability.HullMax * EngineerRepairCatalog.StructureFieldCapFraction;
                if (!needsRepair) continue;
                if (SurfaceStructureCatalog.IsActiveDefenseWeapon(s.Type)) activeDefenses.Add(s.TargetId);
                else others.Add(s.TargetId);
            }

            if (reserve.Policy == MaintenancePolicy.Emergency)
                return MaintenancePolicyResolver.ResolveEmergencyTarget(command.TargetId, commandNeedsRepair, activeDefenses);

            var disabledVehicles = new List<string>();
            foreach (VehicleAgent v in VehicleAgent.Registry.Values)
                if (v != null && v.IsRecoverable && v.HomeSystemId == hangar.CurrentSystemId) disabledVehicles.Add(v.VehicleId);

            List<string> order = MaintenancePolicyResolver.ResolveFullFieldOrder(commandNeedsRepair ? command.TargetId : null, activeDefenses, disabledVehicles, null, others);
            return order.Count > 0 ? order[0] : null;
        }

        private VehicleAgent FindIdleEngineerInHangar()
        {
            foreach (VehicleAgent v in hangar.Vehicles)
                if (v != null && v.Def != null && v.Def.Class == VehicleClass.Engineer && v.Mission.IsIdleInHangar
                    && v.Route == null && v.PoiTask == null && v.EngineerTask == null) return v;
            return null;
        }

        // --- VS-015 §6: System Intervention target resolution --------------------
        private void TickInterventionGroup(string systemId)
        {
            if (Group == null || Group.Policy != InterventionPolicy.SystemIntervention || Group.IsEmpty) return;

            var candidates = new List<InterventionCandidate>();
            foreach (SurfaceStructure command in ExtractionState.Structures)
            {
                if (command == null || command.Type != SurfaceStructureType.Command) continue;
                if (WorldIdCodec.SystemIdFromPlanetWorldId(command.WorldId) != systemId) continue;
                DominanceState state = ExtractionState.GetDominance(command.WorldId).State;
                if (state != DominanceState.Controlled) continue;
                bool commandThreatened = !command.Runtime.IsActive;
                candidates.Add(new InterventionCandidate(command.WorldId, commandThreatened, false, 0f, 0f, true, 0f));
            }

            float groupPower = 0f;
            int escortCount = 0;
            foreach (string id in Group.MemberVehicleIds)
            {
                if (!VehicleAgent.Registry.TryGetValue(id, out VehicleAgent v) || v == null) continue;
                groupPower += v.Hull + v.Armor;
                if (v.Def != null && v.Def.Class == VehicleClass.Escort) escortCount++;
            }

            string target = InterventionGroupResolver.SelectTarget(candidates, groupPower);
            if (target != null) Group.SetActiveTarget(target); else Group.ClearActiveTarget();

            // VS-015.1 §1: this tick's known-threat snapshot is what gets frozen into the save on the next demote.
            float knownThreatForTarget = 0f;
            bool threatIsProbeOnly = true;
            foreach (InterventionCandidate c in candidates)
                if (c.PlanetId == target) { knownThreatForTarget = c.KnownThreatPower; threatIsProbeOnly = c.ThreatIsProbeOnly; break; }
            Group.SetKnownThreatSnapshot(knownThreatForTarget);
            Group.SetHoldingOrRetreating(target != null && InterventionGroupResolver.ShouldHoldOrRetreat(escortCount, threatIsProbeOnly));
        }
    }
}
