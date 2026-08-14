using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Sectors;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A §7: advances the pieces of a non-Full system's economy that
    /// VS-008/VS-011 did NOT already give a remote tick --
    /// SystemStreamingDirector.TickDeltaDataOnly/TickLogisticsDataOnly
    /// already remote-ticks drill production/storage, structure capsule/
    /// build timers, and Collector route movement/loading/dock/unload/
    /// holding against the exact same SystemDelta this class reads (see that
    /// file's own doc comments) -- this class is called ALONGSIDE that
    /// existing mechanism, never instead of it, and never re-implements any
    /// of it. What's new here: dominance stabilization/contested-recovery
    /// (previously frozen the instant a system left Full), structure TIER
    /// UPGRADE completion (capsule/build already covered, upgrade wasn't),
    /// Engineer missions, Escort, Maintenance Resupply, Maintenance Reserve
    /// auto-policies, and InterventionGroup target selection -- none of
    /// which had ANY remote equivalent before VS-016A. A remote system's
    /// ship is never physically present, so any phase that depends on
    /// "return to ship" (EngineerReturnUnusedCargo/TravelHome/Dock,
    /// ResupplyReturning) simply waits -- exactly the existing live rule
    /// when the ship has left the system mid-task.
    /// </summary>
    public static class RemoteEconomyTick
    {
        public static void Tick(SystemDelta delta, SystemManifest manifest, float dt, bool remoteRaidActive)
        {
            TickDominance(delta, dt);
            TickStructureUpgrades(delta, dt);
            TickEngineerMissions(delta, dt, remoteRaidActive);
            TickEscorts(delta, dt, remoteRaidActive);
            TickMaintenanceResupply(delta, dt);
            TickMaintenancePolicies(delta);
            TickInterventionGroup(delta);
        }

        // --- Dominance stabilization/contested-recovery (VS-006) --------------------
        private static void TickDominance(SystemDelta delta, float dt)
        {
            var worldIds = new HashSet<string>();
            foreach (DrillDto d in delta.drills) if (d != null) worldIds.Add(d.worldId);
            foreach (StructureDto s in delta.structures) if (s != null) worldIds.Add(s.worldId);

            foreach (string worldId in worldIds)
            {
                PlanetProfile profile = ExtractionState.GetProfile(worldId);
                if (profile == null) continue;

                bool activeDrill = false;
                foreach (DrillDto d in delta.drills)
                    if (d != null && d.worldId == worldId && d.state == (int)DrillLifecycle.Active) { activeDrill = true; break; }

                bool activeCommand = false; bool commandDisabled = false; int activeDefenseCount = 0;
                foreach (StructureDto s in delta.structures)
                {
                    if (s == null || s.worldId != worldId) continue;
                    var type = (SurfaceStructureType)s.type;
                    bool isActive = s.state == (int)DrillLifecycle.Active;
                    if (type == SurfaceStructureType.Command)
                    {
                        activeCommand = isActive;
                        commandDisabled = s.state == (int)DrillLifecycle.Disabled;
                    }
                    else if (SurfaceStructureCatalog.IsActiveDefenseWeapon(type) && isActive) activeDefenseCount++;
                }

                var cond = new DominanceConditions
                {
                    Scanned = profile.Scanned, ActiveDrill = activeDrill,
                    ActiveCommand = activeCommand, ActiveDefense = activeDefenseCount > 0, CommandDisabled = commandDisabled
                };

                PlanetDominance dom = ExtractionState.GetDominance(worldId);
                DominanceState before = dom.State;
                dom.Tick(dt, cond);

                if (before != DominanceState.Controlled && dom.State == DominanceState.Controlled)
                    Progression.ProgressionService.Award(Progression.ProgressionEvents.DominanceFirst(worldId), Progression.ProgressionEvents.DominanceXp);
            }
        }

        // --- Structure tier upgrades (VS-014 §2/§17) ---------------------------------
        // Capsule-in-transit/constructing timers are already remote-ticked by
        // SystemStreamingDirector.TickDeltaDataOnly -- only the in-flight tier
        // upgrade (a separate, additive job that never touches State) is new here.
        private static void TickStructureUpgrades(SystemDelta delta, float dt)
        {
            foreach (StructureDto s in delta.structures)
            {
                if (s == null || !s.upgrading) continue;
                float total = Mathf.Max(0.0001f, UpgradeTotalSeconds(s));
                float remaining = total * (1f - Mathf.Clamp01(s.upgradeProgress01)) - dt;
                if (remaining <= 0f)
                {
                    // Percentage preservation across tier upgrade -- same contract as StructureRuntime.ApplyTierUpgrade:
                    // never a free full heal, never a loss beyond rounding.
                    (float oldShieldMax, float oldArmorMax, float oldHullMax) = RemoteStructureAccess.MaxFor((SurfaceStructureType)s.type, s.tier);
                    float shieldFrac = oldShieldMax > 0f ? s.shield / oldShieldMax : 0f;
                    float armorFrac = oldArmorMax > 0f ? s.armor / oldArmorMax : 0f;
                    float hullFrac = oldHullMax > 0f ? s.hull / oldHullMax : 0f;

                    s.tier = s.upgradeTargetTier;
                    s.upgrading = false;
                    s.upgradeProgress01 = 0f;
                    (float newShieldMax, float newArmorMax, float newHullMax) = RemoteStructureAccess.MaxFor((SurfaceStructureType)s.type, s.tier);
                    s.shield = newShieldMax * shieldFrac; s.armor = newArmorMax * armorFrac; s.hull = newHullMax * hullFrac;
                }
                else
                {
                    s.upgradeProgress01 = Mathf.Clamp01(1f - remaining / total);
                }
            }
        }

        private static float UpgradeTotalSeconds(StructureDto s)
        {
            StructureTierDef def = PlanetaryDefenseCatalog.Get((SurfaceStructureType)s.type, s.upgradeTargetTier);
            return def != null ? def.BuildSeconds : 60f;
        }

        private static VehicleDto FindVehicle(SystemDelta delta, string vehicleId)
        {
            foreach (VehicleDto v in delta.vehicles) if (v != null && v.vehicleId == vehicleId) return v;
            return null;
        }

        // --- Engineer missions (VS-015) ----------------------------------------------
        private static void TickEngineerMissions(SystemDelta delta, float dt, bool remoteRaidActive)
        {
            foreach (VehicleDto v in delta.vehicles)
            {
                if (v?.engineerTask == null || !v.engineerTask.active) continue;
                EngineerMissionDto task = v.engineerTask;
                var phase = (EngineerPhase)task.phase;

                if (phase == EngineerPhase.WaitForSafety)
                {
                    task.safeSecondsAccumulated = remoteRaidActive ? 0f : task.safeSecondsAccumulated + dt;
                    if (task.safeSecondsAccumulated >= EngineerMissionState.RequiredSafeSeconds) task.phase = (int)EngineerPhase.Working;
                }
                else if (phase == EngineerPhase.Working)
                {
                    TickEngineerWork(delta, v, task, dt);
                }
                else if (phase == EngineerPhase.ReturnUnusedCargo || phase == EngineerPhase.TravelHome || phase == EngineerPhase.Dock)
                {
                    // Ship not present remotely -- the unused-reservation return and final dock leg wait, same "never chases across systems" rule.
                }
            }
        }

        private static void TickEngineerWork(SystemDelta delta, VehicleDto engineer, EngineerMissionDto task, float dt)
        {
            VehicleDef engineerDef = VehicleCatalog.Get((VehicleClass)engineer.vehicleClass, engineer.mk);
            if (engineerDef == null) return;
            var kind = (EngineerTargetKind)task.targetKind;

            if (kind == EngineerTargetKind.Structure || kind == EngineerTargetKind.SupportStructure)
            {
                StructureDto target = FindStructure(delta, task.targetId);
                if (target == null) return;
                Durability d = RemoteStructureAccess.ToDurability(target);
                var mirrorTask = ToEngineerMissionState(engineer.vehicleId, task);
                LogisticsCargo scratchCargo = ScratchCargoFor(task, mirrorTask.ReservedParts, mirrorTask.ReservedMaterialAmount);
                EngineerFieldWork.TickStructureRepair(d, mirrorTask, engineerDef.RepairRatePerSecond,
                    EngineerRepairCatalog.StructureFieldCapFraction, scratchCargo, "remote-engineer:" + engineer.vehicleId, dt);
                RemoteStructureAccess.WriteBack(target, d);
                task.consumedParts = mirrorTask.ConsumedParts; task.consumedMaterialAmount = mirrorTask.ConsumedMaterialAmount;
                task.actualRestoredHpSoFar = mirrorTask.ActualRestoredHpSoFar;
                if (mirrorTask.ActualRestoredHpSoFar >= mirrorTask.PlannedRestoredHp - 0.01f) task.phase = (int)EngineerPhase.ReturnUnusedCargo;
            }
            else if (kind == EngineerTargetKind.Vehicle)
            {
                VehicleDto target = FindVehicle(delta, task.targetId);
                if (target == null) return;
                VehicleDef targetDef = VehicleCatalog.Get((VehicleClass)target.vehicleClass, target.mk);
                if (targetDef == null) return;
                Durability d = RemoteVehicleAccess.ToDurability(target, targetDef);
                var mirrorTask = ToEngineerMissionState(engineer.vehicleId, task);
                LogisticsCargo scratchCargo = ScratchCargoFor(task, mirrorTask.ReservedParts, mirrorTask.ReservedMaterialAmount);
                EngineerFieldWork.TickStructureRepair(d, mirrorTask, engineerDef.RepairRatePerSecond,
                    EngineerRepairCatalog.VehicleNonCombatCapFraction, scratchCargo, "remote-engineer:" + engineer.vehicleId, dt);
                RemoteVehicleAccess.WriteBack(target, d);
                task.consumedParts = mirrorTask.ConsumedParts; task.consumedMaterialAmount = mirrorTask.ConsumedMaterialAmount;
                task.actualRestoredHpSoFar = mirrorTask.ActualRestoredHpSoFar;
                if (mirrorTask.ActualRestoredHpSoFar >= mirrorTask.PlannedRestoredHp - 0.01f) task.phase = (int)EngineerPhase.ReturnUnusedCargo;
            }
            else if (kind == EngineerTargetKind.DisabledVehicle)
            {
                task.actualRestoredHpSoFar += dt; // recovery is a pure elapsed-seconds countdown, no HP math (mirrors EngineerFieldWork.TickVehicleRecovery's own timer-only contract)
                if (task.actualRestoredHpSoFar >= task.plannedRestoredHp)
                {
                    task.phase = (int)EngineerPhase.ReturnUnusedCargo;
                    VehicleDto recovered = FindVehicle(delta, task.targetId);
                    VehicleDef recoveredDef = recovered != null ? VehicleCatalog.Get((VehicleClass)recovered.vehicleClass, recovered.mk) : null;
                    if (recovered != null && recoveredDef != null)
                    {
                        recovered.stranded = false;
                        recovered.hull = recoveredDef.Hull * EngineerRepairCatalog.RecoveryCompleteHullFraction;
                    }
                }
            }
        }

        private static EngineerMissionState ToEngineerMissionState(string vehicleId, EngineerMissionDto task) =>
            EngineerMissionState.Restore(task.missionId, vehicleId, task.systemId, (EngineerTargetKind)task.targetKind, task.targetId,
                (EngineerWorkKind)task.workKind, (EngineerSourceKind)task.sourceKind, task.chosenMaterialId, task.playerManuallyStarted,
                task.reservedParts, task.reservedMaterialAmount, task.consumedParts, task.consumedMaterialAmount,
                (EngineerPhase)task.phase, task.safeSecondsAccumulated, task.xpAwarded, task.plannedRestoredHp, task.actualRestoredHpSoFar);

        /// <summary>A single-owner scratch cargo pre-loaded with exactly the mission's already-reserved remaining amounts -- TickStructureRepair only ever consumes up to what's reserved under this owner id, so this never creates or loses resources versus the real source (ship cargo or planet reserve), only defers the physical settle-up to ReturnUnusedCargo/Dock (which wait for the ship, same as every other "ship not present" leg here).</summary>
        private static LogisticsCargo ScratchCargoFor(EngineerMissionDto task, int reservedParts, float reservedMaterial)
        {
            var cargo = new LogisticsCargo(100000f);
            int remainingParts = System.Math.Max(0, reservedParts - task.consumedParts);
            float remainingMaterial = Mathf.Max(0f, reservedMaterial - task.consumedMaterialAmount);
            if (remainingParts > 0) cargo.Add(ProcessingCatalog.BasicPartId, remainingParts);
            if (remainingMaterial > 0f && !string.IsNullOrEmpty(task.chosenMaterialId)) cargo.Add(task.chosenMaterialId, remainingMaterial);
            cargo.TryReserveAll("remote-engineer:" + task.missionId, BuildCostList(task.chosenMaterialId, remainingParts, remainingMaterial));
            return cargo;
        }

        private static List<(string, float)> BuildCostList(string materialId, int parts, float material)
        {
            var list = new List<(string, float)>();
            if (parts > 0) list.Add((ProcessingCatalog.BasicPartId, parts));
            if (material > 0f && !string.IsNullOrEmpty(materialId)) list.Add((materialId, material));
            return list;
        }

        private static StructureDto FindStructure(SystemDelta delta, string targetId)
        {
            foreach (StructureDto s in delta.structures) if (s != null && StructureTargetId(s) == targetId) return s;
            return null;
        }

        private static string StructureTargetId(StructureDto s) => s.worldId + "/" + s.nodeId;

        // --- Escort (VS-015) ----------------------------------------------------------
        private static void TickEscorts(SystemDelta delta, float dt, bool remoteRaidActive)
        {
            foreach (VehicleDto v in delta.vehicles)
            {
                if (v?.escortState == null || !v.escortState.active) continue;
                EscortStateDto state = v.escortState;
                if (state.command == (int)EscortCommand.Retreat) { state.subState = (int)EscortSubState.Retreating; continue; }

                if (!remoteRaidActive) { if (state.subState != (int)EscortSubState.Idle) state.subState = (int)EscortSubState.Following; continue; }

                // Remote escort combat detail (per-shot exchange against a specific raid attacker) is intentionally
                // not modeled -- see deliverable report; only the stance retreat-threshold rule is enforced against
                // the vehicle's own remaining KZG fraction, reusing EscortCombatResolver exactly like the live path.
                VehicleDef def = VehicleCatalog.Get((VehicleClass)v.vehicleClass, v.mk);
                if (def != null)
                {
                    float frac = (v.armor + v.hull) / Mathf.Max(1f, def.Armor + def.Hull);
                    bool shouldRetreat = EscortCombatResolver.ShouldRetreat(frac, (EscortStance)state.stance, knownEnemyPowerInsufficient: false);
                    state.subState = shouldRetreat ? (int)EscortSubState.Retreating : (int)EscortSubState.Engaging;
                }
            }
        }

        // --- Maintenance Resupply (VS-015.1) ------------------------------------------
        private static void TickMaintenanceResupply(SystemDelta delta, float dt)
        {
            foreach (VehicleDto v in delta.vehicles)
            {
                if (v?.resupplyTask == null || !v.resupplyTask.active) continue;
                MaintenanceResupplyDto task = v.resupplyTask;
                var phase = (MaintenanceResupplyPhase)task.phase;
                VehicleDef def = VehicleCatalog.Get((VehicleClass)v.vehicleClass, v.mk);
                if (def == null) continue;

                if (phase == MaintenanceResupplyPhase.Loading) { task.phase = (int)MaintenanceResupplyPhase.ToReserve; }
                else if (phase == MaintenanceResupplyPhase.ToReserve)
                {
                    if (delta.structures.Count == 0) return;
                    task.phase = (int)MaintenanceResupplyPhase.Unloading;
                }
                else if (phase == MaintenanceResupplyPhase.Unloading)
                {
                    StructureDto target = FindStructure(delta, task.targetCommandStructureId);
                    if (target?.maintenanceReserve == null) return;
                    float amount = Mathf.Min(v.heldAmount, def.TransferVolumePerSecond * dt);
                    if (amount <= 0f) { task.phase = (int)MaintenanceResupplyPhase.Returning; continue; }
                    v.heldAmount -= amount;
                    AddToReserve(target.maintenanceReserve, task.itemId, amount);
                    if (v.heldAmount <= 0.0001f) { v.heldItemId = null; v.heldAmount = 0f; task.phase = (int)MaintenanceResupplyPhase.Returning; }
                }
                // Returning: ship not present remotely -- waits.
            }
        }

        private static void AddToReserve(MaintenanceReserveDto reserve, string itemId, float amount)
        {
            if (!MaintenanceReserve.IsAccepted(itemId)) return;
            foreach (ItemAmountDto item in reserve.items)
            {
                if (item.itemId != itemId) continue;
                item.amount += amount;
                return;
            }
            reserve.items.Add(new ItemAmountDto { itemId = itemId, amount = amount });
        }

        // --- Maintenance Reserve auto-policies (VS-015 §5) ----------------------------
        private static void TickMaintenancePolicies(SystemDelta delta)
        {
            foreach (StructureDto command in delta.structures)
            {
                if (command == null || (SurfaceStructureType)command.type != SurfaceStructureType.Command || command.maintenanceReserve == null) continue;
                var policy = (MaintenancePolicy)command.maintenanceReserve.policy;
                if (policy == MaintenancePolicy.Manual) continue;
                if (!string.IsNullOrEmpty(command.maintenanceReserve.activeEngineerTargetId)) continue;

                bool commandNeedsRepair = command.state != (int)DrillLifecycle.Active || command.armor < RemoteStructureAccess.MaxFor(SurfaceStructureType.Command, command.tier).armorMax * EngineerRepairCatalog.StructureFieldCapFraction;
                var activeDefenses = new List<string>();
                foreach (StructureDto s in delta.structures)
                {
                    if (s == null || s.worldId != command.worldId || s == command || s.state != (int)DrillLifecycle.Active) continue;
                    if (SurfaceStructureCatalog.IsActiveDefenseWeapon((SurfaceStructureType)s.type)) activeDefenses.Add(StructureTargetId(s));
                }

                string targetId = policy == MaintenancePolicy.Emergency
                    ? MaintenancePolicyResolver.ResolveEmergencyTarget(StructureTargetId(command), commandNeedsRepair, activeDefenses)
                    : FirstOrNull(MaintenancePolicyResolver.ResolveFullFieldOrder(commandNeedsRepair ? StructureTargetId(command) : null, activeDefenses, new List<string>(), new List<string>(), new List<string>()));

                if (targetId != null) command.maintenanceReserve.activeEngineerTargetId = targetId;
            }
        }

        private static string FirstOrNull(List<string> list) => list != null && list.Count > 0 ? list[0] : null;

        // --- InterventionGroup target selection (VS-015 §6) ---------------------------
        private static void TickInterventionGroup(SystemDelta delta)
        {
            if (delta.interventionGroup == null || (InterventionPolicy)delta.interventionGroup.policy != InterventionPolicy.SystemIntervention) return;
            if (delta.interventionGroup.memberVehicleIds.Count == 0) return;

            var candidates = new List<InterventionCandidate>();
            foreach (StructureDto command in delta.structures)
            {
                if (command == null || (SurfaceStructureType)command.type != SurfaceStructureType.Command) continue;
                if (ExtractionState.GetDominance(command.worldId).State != DominanceState.Controlled) continue;
                bool commandThreatened = command.state != (int)DrillLifecycle.Active;
                candidates.Add(new InterventionCandidate(command.worldId, commandThreatened, false, 0f, 0f, true, 0f));
            }

            float groupPower = 0f; int escortCount = 0;
            foreach (string id in delta.interventionGroup.memberVehicleIds)
            {
                VehicleDto v = FindVehicle(delta, id);
                if (v == null) continue;
                groupPower += v.armor + v.hull;
                if ((VehicleClass)v.vehicleClass == VehicleClass.Escort) escortCount++;
            }

            string target = InterventionGroupResolver.SelectTarget(candidates, groupPower);
            delta.interventionGroup.activeTargetPlanetId = target;
            delta.interventionGroup.holdingOrRetreating = target != null && InterventionGroupResolver.ShouldHoldOrRetreat(escortCount, threatIsProbeOnly: true);
        }
    }
}
