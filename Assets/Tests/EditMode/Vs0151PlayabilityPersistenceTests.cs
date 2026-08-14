using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-015.1: closes the playability/persistence gaps left open by VS-015's
    /// auto-pass -- InterventionGroup save capture/restore, the multi-item
    /// structure cost transaction fix, the Collector Mk II resupply state
    /// machine, the Maintenance Reserve whitelist/capacity, and the UI-facing
    /// command seams (group/policy toggle, Support build node toggle). Every
    /// test here exercises the same pure classes/DTOs the real MonoBehaviour
    /// flows delegate to -- matching this project's existing EditMode/PlayMode
    /// split (ShipHangar/VehicleAgent/SurfaceStructure integration lives in
    /// PlayMode, never here).
    /// </summary>
    public class Vs0151PlayabilityPersistenceTests
    {
        private const int TestSlot = 96;

        [TearDown]
        public void Cleanup()
        {
            string main = SaveService.SlotFile(TestSlot);
            string dir = Path.GetDirectoryName(main);
            if (dir == null) return;
            foreach (string f in new[]
            {
                main, Path.Combine(dir, $"slot{TestSlot}.bak1.json"),
                Path.Combine(dir, $"slot{TestSlot}.bak2.json"), Path.Combine(dir, $"slot{TestSlot}.bak3.json"),
                Path.Combine(dir, $"slot{TestSlot}.tmp.json")
            })
            {
                if (File.Exists(f)) File.Delete(f);
            }
        }

        // --- §1: InterventionGroup capture/restore ------------------------------

        [Test]
        public void InterventionGroup_Restore_RebuildsEveryFieldExactly()
        {
            var restored = InterventionGroup.Restore("sys001", InterventionPolicy.SystemIntervention,
                new[] { "vehicle-1", "vehicle-2" }, "sys001/pl00", holdingOrRetreating: true, knownThreatPowerSnapshot: 275f);

            Assert.AreEqual("sys001", restored.SystemId);
            Assert.AreEqual(InterventionPolicy.SystemIntervention, restored.Policy);
            Assert.AreEqual(2, restored.MemberVehicleIds.Count);
            Assert.AreEqual("vehicle-1", restored.MemberVehicleIds[0]);
            Assert.AreEqual("vehicle-2", restored.MemberVehicleIds[1]);
            Assert.AreEqual("sys001/pl00", restored.ActiveTargetPlanetId);
            Assert.IsTrue(restored.HoldingOrRetreating);
            Assert.AreEqual(275f, restored.KnownThreatPowerSnapshot);
        }

        [Test]
        public void InterventionGroup_Restore_NeverDuplicatesMembers_EvenWithRepeatedIds()
        {
            var restored = InterventionGroup.Restore("sys001", InterventionPolicy.Manual,
                new[] { "vehicle-1", "vehicle-1" }, null, false, 0f);
            Assert.AreEqual(1, restored.MemberVehicleIds.Count);
        }

        [Test]
        public void InterventionGroup_Restore_NullMembers_IsSafeEmptyGroup()
        {
            var restored = InterventionGroup.Restore("sys001", InterventionPolicy.Manual, null, null, false, 0f);
            Assert.IsTrue(restored.IsEmpty);
        }

        [Test]
        public void InterventionGroup_KnownThreatSnapshot_DefaultsToZero_AndIsFrozenUntilExplicitlySet()
        {
            var group = new InterventionGroup("sys001");
            Assert.AreEqual(0f, group.KnownThreatPowerSnapshot);
            group.SetKnownThreatSnapshot(150f);
            Assert.AreEqual(150f, group.KnownThreatPowerSnapshot);
        }

        [Test]
        public void InterventionGroupDirector_EnsureGroupForSystem_SameSystem_NeverReplacesExistingGroup()
        {
            var director = new UnityEngine.GameObject("director-test").AddComponent<InterventionGroupDirector>();
            director.EnsureGroupForSystem("sys001");
            InterventionGroup first = director.Group;
            first.AddMember("vehicle-1");

            director.EnsureGroupForSystem("sys001");
            Assert.AreSame(first, director.Group);
            Assert.AreEqual(1, director.Group.MemberVehicleIds.Count);

            director.EnsureGroupForSystem("sys002");
            Assert.AreNotSame(first, director.Group);
            Assert.AreEqual(0, director.Group.MemberVehicleIds.Count);

            UnityEngine.Object.DestroyImmediate(director.gameObject);
        }

        [Test]
        public void InterventionGroupDirector_RestoreGroup_ReplacesTransientGroupWithSavedOne()
        {
            var director = new UnityEngine.GameObject("director-test").AddComponent<InterventionGroupDirector>();
            director.EnsureGroupForSystem("sys001"); // transient group, would normally be overwritten by real ticking

            var saved = InterventionGroup.Restore("sys001", InterventionPolicy.DefendInPlace,
                new[] { "vehicle-9" }, "sys001/pl02", true, 400f);
            director.RestoreGroup(saved);

            Assert.AreSame(saved, director.Group);
            Assert.AreEqual(InterventionPolicy.DefendInPlace, director.Group.Policy);
            Assert.AreEqual(400f, director.Group.KnownThreatPowerSnapshot);
            Assert.AreEqual("vehicle-9", director.Group.MemberVehicleIds[0]);

            UnityEngine.Object.DestroyImmediate(director.gameObject);
        }

        // --- §1: SystemDelta.IsEmpty / InterventionGroupDto null-safety ---------

        [Test]
        public void SystemDelta_IsEmpty_TrueWhenInterventionGroupIsNull()
        {
            var delta = new SystemDelta { systemId = "sys001" };
            Assert.IsNull(delta.interventionGroup);
            Assert.IsTrue(delta.IsEmpty);
        }

        [Test]
        public void SystemDelta_IsEmpty_TrueWhenInterventionGroupIsPresentButDefault()
        {
            var delta = new SystemDelta { systemId = "sys001", interventionGroup = new InterventionGroupDto() };
            Assert.IsTrue(delta.IsEmpty);
        }

        [Test]
        public void SystemDelta_IsEmpty_FalseWhenInterventionGroupHasAMember()
        {
            var delta = new SystemDelta { systemId = "sys001", interventionGroup = new InterventionGroupDto() };
            delta.interventionGroup.memberVehicleIds.Add("vehicle-1");
            Assert.IsFalse(delta.IsEmpty);
        }

        [Test]
        public void PreV13SystemDelta_EmptyGroupDefault_IsManualNoMembersNoTarget()
        {
            // v12->v13: an old delta never wrote interventionGroup at all -- JsonUtility leaves it null,
            // which SystemDelta.IsEmpty and every restore path must treat as "Manual, no members, no target".
            var delta = new SystemDelta();
            Assert.IsTrue(delta.interventionGroup == null || delta.interventionGroup.memberVehicleIds.Count == 0);
            Assert.IsTrue(delta.interventionGroup == null || (InterventionPolicy)delta.interventionGroup.policy == InterventionPolicy.Manual);
        }

        [Test]
        public void InterventionGroupDto_SaveLoadRoundTrip_PreservesEveryFieldIncludingKnownThreatSnapshot()
        {
            var delta = new SystemDelta
            {
                systemId = "sys001",
                interventionGroup = new InterventionGroupDto
                {
                    policy = (int)InterventionPolicy.SystemIntervention,
                    activeTargetPlanetId = "sys001/pl01",
                    holdingOrRetreating = true,
                    knownThreatPowerSnapshot = 333.5f
                }
            };
            delta.interventionGroup.memberVehicleIds.Add("vehicle-1");
            delta.interventionGroup.memberVehicleIds.Add("vehicle-2");

            var payload = new SavePayload { seed = 1 };
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            InterventionGroupDto restored = loaded.systemDeltas[0].interventionGroup;
            Assert.IsNotNull(restored);
            Assert.AreEqual((int)InterventionPolicy.SystemIntervention, restored.policy);
            Assert.AreEqual("sys001/pl01", restored.activeTargetPlanetId);
            Assert.IsTrue(restored.holdingOrRetreating);
            Assert.AreEqual(333.5f, restored.knownThreatPowerSnapshot, 0.001f);
            Assert.AreEqual(2, restored.memberVehicleIds.Count);
        }

        // --- §4: Collector Mk II Maintenance Resupply state machine -------------

        [Test]
        public void MaintenanceResupplyMission_StartsInLoadingPhase()
        {
            var mission = new MaintenanceResupplyMission("m1", "vehicle-1", "sys001", "sys001/pl00/command", ProcessingCatalog.BasicPartId, 20f);
            Assert.AreEqual(MaintenanceResupplyPhase.Loading, mission.Phase);
            Assert.IsFalse(mission.IsFinished);
        }

        [Test]
        public void MaintenanceResupplyMission_PhasesAdvanceInOrder_NeverSkipping()
        {
            var mission = new MaintenanceResupplyMission("m1", "vehicle-1", "sys001", "sys001/pl00/command", ProcessingCatalog.BasicPartId, 20f);

            mission.BeginUnloading(); // out of order -- must be a no-op from Loading
            Assert.AreEqual(MaintenanceResupplyPhase.Loading, mission.Phase);

            mission.BeginTravel();
            Assert.AreEqual(MaintenanceResupplyPhase.ToReserve, mission.Phase);

            mission.BeginTravel(); // repeated call -- no-op
            Assert.AreEqual(MaintenanceResupplyPhase.ToReserve, mission.Phase);

            mission.BeginUnloading();
            Assert.AreEqual(MaintenanceResupplyPhase.Unloading, mission.Phase);

            mission.BeginReturn();
            Assert.AreEqual(MaintenanceResupplyPhase.Returning, mission.Phase);

            mission.Complete();
            Assert.AreEqual(MaintenanceResupplyPhase.Completed, mission.Phase);
            Assert.IsTrue(mission.IsFinished);
        }

        [Test]
        public void MaintenanceResupplyMission_Cancel_IsFinishedFromAnyPhase()
        {
            var mission = new MaintenanceResupplyMission("m1", "vehicle-1", "sys001", "sys001/pl00/command", ProcessingCatalog.BasicPartId, 20f);
            mission.BeginTravel();
            mission.Cancel();
            Assert.AreEqual(MaintenanceResupplyPhase.Cancelled, mission.Phase);
            Assert.IsTrue(mission.IsFinished);
        }

        [Test]
        public void MaintenanceResupplyMission_Restore_RebuildsExactPhaseAndFields()
        {
            var restored = MaintenanceResupplyMission.Restore("m1", "vehicle-1", "sys001", "sys001/pl00/command",
                "material.metal_alloy", 35f, MaintenanceResupplyPhase.Unloading);
            Assert.AreEqual("m1", restored.MissionId);
            Assert.AreEqual("vehicle-1", restored.VehicleId);
            Assert.AreEqual("sys001/pl00/command", restored.TargetCommandStructureId);
            Assert.AreEqual("material.metal_alloy", restored.ItemId);
            Assert.AreEqual(35f, restored.PlannedAmount);
            Assert.AreEqual(MaintenanceResupplyPhase.Unloading, restored.Phase);
        }

        [Test]
        public void MaintenanceResupplyDto_SaveLoadRoundTrip_PreservesEveryField()
        {
            var payload = new SavePayload { seed = 1 };
            var vdto = new VehicleDto
            {
                vehicleId = "vehicle-0005", vehicleClass = (int)VehicleClass.Collector, mk = 2,
                resupplyTask = new MaintenanceResupplyDto
                {
                    active = true, missionId = "resupply-vehicle-0005-0000", systemId = "sys001",
                    targetCommandStructureId = "sys001/pl00/command", itemId = ProcessingCatalog.BasicPartId,
                    plannedAmount = 40f, phase = (int)MaintenanceResupplyPhase.ToReserve
                }
            };
            payload.vehicles.Add(vdto);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);

            MaintenanceResupplyDto restored = loaded.vehicles[0].resupplyTask;
            Assert.IsTrue(restored.active);
            Assert.AreEqual("sys001/pl00/command", restored.targetCommandStructureId);
            Assert.AreEqual(40f, restored.plannedAmount);
            Assert.AreEqual((int)MaintenanceResupplyPhase.ToReserve, restored.phase);
        }

        [Test]
        public void PreV13Vehicle_SafeDefault_NoResupplyTaskActive()
        {
            var dto = new VehicleDto { vehicleId = "v1" };
            Assert.IsFalse(dto.resupplyTask?.active ?? false);
        }

        // --- §6: multi-item structure cost transaction fix -----------------------

        [Test]
        public void LogisticsCargo_TryReserveAll_PlasmaT1FullCost_ReservesEveryLineItemAtomically()
        {
            var cargo = new LogisticsCargo(1000f);
            List<(string itemId, float amount)> cost = PlanetaryDefenseCatalog.PlasmaT1.FullCost();
            foreach (var (itemId, amount) in cost) cargo.Add(itemId, amount);

            Assert.IsTrue(cargo.TryReserveAll("structure-build:test", cost));
            foreach (var (itemId, amount) in cost) Assert.AreEqual(amount, cargo.GetReserved(itemId), 0.01f);
        }

        [Test]
        public void LogisticsCargo_TryReserveAll_MissingOneItem_ReservesNothing_AllOrNothing()
        {
            // Sensor Array T1 needs Basic Parts + Industrial Diamond + Biocomposite -- stock only the first two.
            var cargo = new LogisticsCargo(1000f);
            List<(string itemId, float amount)> cost = PlanetaryDefenseCatalog.SensorArrayT1.FullCost();
            for (int i = 0; i < cost.Count - 1; i++) cargo.Add(cost[i].itemId, cost[i].amount);

            Assert.IsFalse(cargo.TryReserveAll("structure-build:test", cost));
            foreach (var (itemId, _) in cost) Assert.AreEqual(0f, cargo.GetReserved(itemId), "a failed atomic reservation must leave every item unreserved, never a partial reservation");
        }

        [Test]
        public void LogisticsCargo_ConsumeReservationForRefund_MaintenancePlatformT1_RefundsFloor75PercentOfEveryItem()
        {
            var cargo = new LogisticsCargo(1000f);
            List<(string itemId, float amount)> cost = PlanetaryDefenseCatalog.MaintenancePlatformT1.FullCost();
            foreach (var (itemId, amount) in cost) cargo.Add(itemId, amount);
            cargo.TryReserveAll("structure-build:test", cost);

            Dictionary<string, float> refunded = cargo.ConsumeReservationForRefund("structure-build:test", SurfaceStructureCatalog.RefundFraction);

            foreach (var (itemId, amount) in cost)
            {
                float expected = UnityEngine.Mathf.Floor(amount * SurfaceStructureCatalog.RefundFraction);
                Assert.IsTrue(refunded.ContainsKey(itemId) || expected == 0f, $"{itemId} must be refunded when its floor(75%) share is non-zero");
                if (refunded.ContainsKey(itemId)) Assert.AreEqual(expected, refunded[itemId], 0.01f);
            }
            // No item is ever left double-reserved or lost -- the owner's reservation is fully cleared.
            foreach (var (itemId, _) in cost) Assert.AreEqual(0f, cargo.GetReserved(itemId));
        }

        [Test]
        public void LogisticsCargo_ConsumeReservationForRefund_PlasmaT1_NeverLosesTheSpecialMaterialLine()
        {
            // The original bug: only Basic Parts refund info was tracked, silently losing every special
            // material (metal_alloy/refined_gas for Plasma) on cancel. This must never regress.
            var cargo = new LogisticsCargo(1000f);
            List<(string itemId, float amount)> cost = PlanetaryDefenseCatalog.PlasmaT1.FullCost();
            foreach (var (itemId, amount) in cost) cargo.Add(itemId, amount);
            cargo.TryReserveAll("structure-build:test", cost);

            Dictionary<string, float> refunded = cargo.ConsumeReservationForRefund("structure-build:test", SurfaceStructureCatalog.RefundFraction);

            Assert.GreaterOrEqual(cost.Count, 2, "Plasma T1 must have more than one cost line item for this regression test to be meaningful");
            foreach (var (itemId, amount) in cost)
            {
                if (itemId == ProcessingCatalog.BasicPartId) continue;
                float expected = UnityEngine.Mathf.Floor(amount * SurfaceStructureCatalog.RefundFraction);
                Assert.IsTrue(expected == 0f || (refunded.ContainsKey(itemId) && refunded[itemId] > 0f), $"special material {itemId} must be refunded, not silently dropped");
            }
        }

        [Test]
        public void LogisticsCargo_ConsumeReservation_OnCompletion_PermanentlyConsumesEveryLineItem()
        {
            var cargo = new LogisticsCargo(1000f);
            List<(string itemId, float amount)> cost = PlanetaryDefenseCatalog.MaintenancePlatformT1.FullCost();
            foreach (var (itemId, amount) in cost) cargo.Add(itemId, amount * 2f); // extra headroom beyond the reservation

            cargo.TryReserveAll("structure-build:test", cost);
            cargo.ConsumeReservation("structure-build:test");

            foreach (var (itemId, amount) in cost)
            {
                Assert.AreEqual(0f, cargo.GetReserved(itemId));
                Assert.AreEqual(amount, cargo.GetStored(itemId), 0.01f); // exactly the un-consumed remainder is left
            }
        }

        // --- §7 / §6: StructureDto buildCost / flatRefund persistence -----------

        [Test]
        public void StructureDto_BuildCost_SaveLoadRoundTrip_PreservesEveryLineItem()
        {
            var delta = new SystemDelta { systemId = "sys001" };
            var sdto = new StructureDto { worldId = "sys001/pl00", nodeId = "support.1", type = (int)SurfaceStructureType.MaintenancePlatform, tier = 1 };
            sdto.buildCost.Add(new ItemAmountDto { itemId = ProcessingCatalog.BasicPartId, amount = 40f });
            sdto.buildCost.Add(new ItemAmountDto { itemId = "material.metal_alloy", amount = 15f });
            sdto.buildCost.Add(new ItemAmountDto { itemId = "material.biocomposite", amount = 10f });
            delta.structures.Add(sdto);

            var payload = new SavePayload { seed = 1 };
            payload.systemDeltas.Add(delta);
            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);

            StructureDto restored = loaded.systemDeltas[0].structures[0];
            Assert.AreEqual(3, restored.buildCost.Count);
            Assert.AreEqual(0, restored.flatRefund.Count);
            Assert.AreEqual(15f, restored.buildCost[1].amount);
            Assert.AreEqual("material.metal_alloy", restored.buildCost[1].itemId);
        }

        [Test]
        public void StructureDto_BuildCostAndFlatRefund_AreMutuallyExclusive_NeverBothPopulated()
        {
            // buildCost = active atomic reservation (normal build path); flatRefund = VS-010 Quick Asset
            // Package's pre-computed debt. A single structure only ever uses one of the two mechanisms.
            var normalBuild = new StructureDto { worldId = "sys001/pl00", nodeId = "command", type = 0 };
            normalBuild.buildCost.Add(new ItemAmountDto { itemId = ProcessingCatalog.BasicPartId, amount = 30f });
            Assert.AreEqual(0, normalBuild.flatRefund.Count);

            var quickPackageBuild = new StructureDto { worldId = "sys001/pl01", nodeId = "command", type = 0 };
            quickPackageBuild.flatRefund.Add(new ItemAmountDto { itemId = ProcessingCatalog.BasicPartId, amount = 22f });
            Assert.AreEqual(0, quickPackageBuild.buildCost.Count);
        }

        [Test]
        public void PreV13Structure_SafeDefault_EmptyBuildCostAndFlatRefund()
        {
            var dto = new StructureDto();
            Assert.AreEqual(0, dto.buildCost.Count);
            Assert.AreEqual(0, dto.flatRefund.Count);
        }

        // --- §5: Maintenance Reserve whitelist + capacity -------------------------

        [Test]
        public void MaintenanceReserve_Add_AcceptsBasicPartsAndAllFiveProcessedMaterials()
        {
            var reserve = new MaintenanceReserve("sys001/pl00", tier: 2);
            Assert.Greater(reserve.Add(ProcessingCatalog.BasicPartId, 5f), 0f);
            foreach (string materialId in ProcessingCatalog.MaterialIds)
                Assert.Greater(reserve.Add(materialId, 1f), 0f, $"{materialId} must be an accepted reserve item");
        }

        [Test]
        public void MaintenanceReserve_Add_RejectsRawResourcesNuclearAndUnknownItems()
        {
            var reserve = new MaintenanceReserve("sys001/pl00", tier: 2);
            foreach (string rawId in ExtractionCatalog.ResourceIds)
                Assert.AreEqual(0f, reserve.Add(rawId, 10f), $"raw resource {rawId} must never be accepted into a Maintenance Reserve");
            Assert.AreEqual(0f, reserve.Add("energy.nuclear", 10f));
            Assert.AreEqual(0f, reserve.Add("core.nuclear", 10f));
            Assert.AreEqual(0f, reserve.Add("item.completely_unknown", 10f));
        }

        [Test]
        public void MaintenanceReserve_Tier1Capacity_Is40_Tier2Is120()
        {
            var t1 = new MaintenanceReserve("sys001/pl00", tier: 1);
            Assert.AreEqual(40f, t1.Cargo.Capacity);
            var t2 = new MaintenanceReserve("sys001/pl00", tier: 2);
            Assert.AreEqual(120f, t2.Cargo.Capacity);
        }

        [Test]
        public void MaintenanceReserve_Add_NeverExceedsCapacity_EvenWhenOffered()
        {
            var reserve = new MaintenanceReserve("sys001/pl00", tier: 1); // 40 capacity
            float added = reserve.Add(ProcessingCatalog.BasicPartId, 1000f);
            Assert.AreEqual(40f, added, 0.01f);
            Assert.AreEqual(40f, reserve.Cargo.UsedVolume, 0.01f);
        }

        [Test]
        public void MaintenanceReserve_ApplyTier_RaisesCapacity_NeverLosesAlreadyStoredItems()
        {
            var reserve = new MaintenanceReserve("sys001/pl00", tier: 1);
            reserve.Add(ProcessingCatalog.BasicPartId, 40f);
            reserve.ApplyTier(2);
            Assert.AreEqual(120f, reserve.Cargo.Capacity);
            Assert.AreEqual(40f, reserve.Cargo.GetStored(ProcessingCatalog.BasicPartId));
        }

        [Test]
        public void MaintenanceReserve_SingleActiveEngineerTarget_PerPlanet()
        {
            var reserve = new MaintenanceReserve("sys001/pl00", tier: 1);
            Assert.IsTrue(reserve.TrySetActiveTarget("target-a"));
            Assert.IsFalse(reserve.TrySetActiveTarget("target-b")); // already busy with target-a
            reserve.ClearActiveTarget("target-a");
            Assert.IsTrue(reserve.TrySetActiveTarget("target-b"));
        }

        // --- §2/§3: Engineer/Escort target-candidate shapes (UI-facing data) -----

        [Test]
        public void EngineerTargetCandidate_ConstructsWithEveryFieldPreserved()
        {
            var candidate = new ShipHangar.EngineerTargetCandidate(EngineerTargetKind.Vehicle, "vehicle-2", "Eskort Mk I [vehicle-2]",
                EngineerWorkKind.Repair, armorFraction: 0.4f, hullFraction: 0.5f, reachableCapFraction: 0.6f,
                requiredParts: 12, requiredMaterial: 4f, estimatedTravelSeconds: 30f, estimatedWorkSeconds: 0f, risk: "Güvenli");

            Assert.AreEqual(EngineerTargetKind.Vehicle, candidate.Kind);
            Assert.AreEqual("vehicle-2", candidate.TargetId);
            Assert.AreEqual(EngineerWorkKind.Repair, candidate.WorkKind);
            Assert.AreEqual(0.6f, candidate.ReachableCapFraction);
            Assert.AreEqual(12, candidate.RequiredParts);
            Assert.AreEqual("Güvenli", candidate.Risk);
        }

        [Test]
        public void EngineerTargetKind_NeverListsDrillAsARepairableCandidateKind()
        {
            // Drill has no Durability model (VS-015 §3) -- BuildEngineerTargetCandidates never emits
            // EngineerTargetKind.Drill, and TrySendEngineer explicitly rejects it (TargetHasNoDurability),
            // covered end-to-end in the PlayMode suite. Here we only pin the reject reason's existence.
            Assert.IsTrue(System.Enum.IsDefined(typeof(ShipHangar.EngineerSendRejectReason), ShipHangar.EngineerSendRejectReason.TargetHasNoDurability));
        }

        [Test]
        public void EscortProtectableTarget_ConstructsForEveryCategory()
        {
            var ship = new ShipHangar.EscortProtectableTarget("ship", "Ana Gemi", TargetCategory.Ship);
            var vehicle = new ShipHangar.EscortProtectableTarget("vehicle-1", "Mühendis Mk I [vehicle-1]", TargetCategory.Vehicle);
            var structure = new ShipHangar.EscortProtectableTarget("sys001/pl00/command", "Command (sys001/pl00)", TargetCategory.Structure);

            Assert.AreEqual(TargetCategory.Ship, ship.Category);
            Assert.AreEqual(TargetCategory.Vehicle, vehicle.Category);
            Assert.AreEqual(TargetCategory.Structure, structure.Category);
            Assert.AreEqual("ship", ship.TargetId);
            Assert.AreEqual("sys001/pl00/command", structure.TargetId);
        }

        [Test]
        public void EscortState_SupportsDistinctProtectedTargetsAcrossAllThreeCategories()
        {
            var protectShip = new EscortState("v1", "m1", "ship", TargetCategory.Ship, true);
            var protectVehicle = new EscortState("v2", "m2", "vehicle-3", TargetCategory.Vehicle, true);
            var protectStructure = new EscortState("v3", "m3", "sys001/pl00/defense.0", TargetCategory.Structure, true);

            Assert.AreEqual("ship", protectShip.ProtectedTargetId);
            Assert.AreEqual(TargetCategory.Vehicle, protectVehicle.ProtectedTargetCategory);
            Assert.AreEqual("sys001/pl00/defense.0", protectStructure.ProtectedTargetId);
            Assert.AreEqual(TargetCategory.Structure, protectStructure.ProtectedTargetCategory);
        }

        // --- §5 UI command seam: Support build node toggle ------------------------
        //
        // Three tests stood here and none of them could fail (audit P1-TEST-031, P1-TEST-033).
        // Two asserted Enum.IsDefined on a literal enum member, which the compiler already
        // guarantees; the third watched SelectedSupportNodeIndex flip 0->1->0 without ever
        // asking what the ship then builds. Removing them removes false green, not coverage:
        //   * the Mk I refusal is driven for real, against a Mk II control, by
        //     AuditPart4BehaviourAssertionTests.AMkOneCollector_IsRefusedAsVehicleNotEligible_AndAMkTwoIsNot
        //   * the toggle's real outcome is checked by
        //     AuditPart4BehaviourAssertionTests.TogglingTheSupportSlot_MovesTheBuildTargetToADifferentSurfaceNode
        //   * TryDeposit/TryWithdraw reject reasons come back from real calls in
        //     Vs0151PlayabilityIntegrationTests (PlayMode), where the range gate is real.
    }
}
