using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.UI;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for VS-015.1 (playability/persistence gaps
    /// closed on top of VS-015's auto-pass): real Engineer/Escort target
    /// candidates drawn from the same builder the HangarScreen UI uses,
    /// physical Maintenance Reserve transfer, a real Collector Mk II resupply
    /// flight, UI-driven Support build/cancel with full multi-item refund,
    /// and InterventionGroup Full-&gt;demote-&gt;Full save continuity. Every
    /// flow is driven through the same public, input-free APIs a keypress or
    /// UI button already calls -- no keyboard/mouse simulation, matching
    /// every other PlayMode file in this project (e.g. Vs015SupportOperationsTests).
    /// </summary>
    public class Vs0151PlayabilityIntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;
        private float originalTimeScale;

        private readonly Dictionary<string, string> slot1Backup = new Dictionary<string, string>();

        private static IEnumerable<string> Slot1Files()
        {
            string main = SaveService.SlotFile(GameplaySlot);
            string dir = Path.GetDirectoryName(main);
            yield return main;
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak1.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak2.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak3.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.tmp.json");
        }

        [SetUp]
        public void BackupRealGameplaySlot()
        {
            originalTimeScale = Time.timeScale;
            slot1Backup.Clear();
            foreach (string f in Slot1Files())
                slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
            Time.timeScale = originalTimeScale;
            foreach (var kv in slot1Backup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu\'s New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        private static void FillCargoWithEverything(LogisticsCargo cargo)
        {
            // The ship's real starting capacity is small -- raise it first, or the later Add calls
            // silently deliver 0 once it fills up (that exact silent-truncation bug bit this file once already).
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 10000f));
            cargo.Add(ProcessingCatalog.BasicPartId, 1000f);
            foreach (string m in ProcessingCatalog.MaterialIds) cargo.Add(m, 1000f);
        }

        // --- Composition sanity ---------------------------------------------------

        [UnityTest]
        public IEnumerator SceneBoots_WithEveryVs0151ComponentPresentExactlyOnce()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController ship = adapter.Context.Ship;

            Assert.IsNotNull(ship.GetComponent<ShipMaintenanceReserveAccess>(), "VS-015.1 §4 physical reserve-access component missing from the ship");
            // Reserve and support build are two halves of one widget now, so the
            // count is one, not two. Inactive objects count: both halves start
            // hidden until the ship is near a planet.
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudPlanetContext>(true).Length);
        }

        [UnityTest]
        public IEnumerator RestartCampaign_NeverDuplicatesTheNewVs0151UiOrComponents()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudPlanetContext>(true).Length, "restart must never duplicate the planet context widget");
            Assert.AreEqual(1, Object.FindObjectsOfType<ShipMaintenanceReserveAccess>().Length, "restart must never duplicate the ship's reserve-access component");
        }

        // --- Engineer: real UI-facing target candidate -> physical repair --------

        [UnityTest]
        public IEnumerator Engineer_RealTargetCandidate_PhysicallyRepairsADamagedCommandStructure()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            ShipHangar hangar = ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(1);
            FillCargoWithEverything(ship.GetComponent<ShipLogistics>().Cargo);

            PlanetManifest outerPlanet = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outerPlanet.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            SurfaceNode node = profile.CommandNode;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, startFraction: 0.3f); // well below the 60% field cap
            var runtime = new StructureRuntime(def, DrillLifecycle.Active, 0f, 0f, durability);
            SurfaceStructure structure = SurfaceStructure.Restore(planet, profile, def, node, runtime, null, null);
            yield return null;
            float armorBefore = structure.Runtime.Durability.Armor;

            VehicleAgent engineer = hangar.RestoreVehicle("vehicle-test-eng-repair", VehicleCatalog.EngineerMk1);
            engineer.RestorePhysical(context.StartSystemId, structure.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);
            engineer.transform.position = structure.transform.position; // real target already reached -- isolates safety+work, not travel

            List<ShipHangar.EngineerTargetCandidate> candidates = hangar.BuildEngineerTargetCandidates(engineer);
            ShipHangar.EngineerTargetCandidate? found = null;
            foreach (var c in candidates) if (c.TargetId == structure.TargetId) { found = c; break; }
            Assert.IsTrue(found.HasValue, "the damaged Command Center must appear in the real same-system Engineer target list the UI draws from");
            var candidate = found.Value;

            string material = candidate.RequiredMaterial > 0.01f ? hangar.FindAffordableMaterial(candidate.RequiredMaterial, EngineerSourceKind.ShipCargo) : null;
            ShipHangar.EngineerSendRejectReason reason = hangar.TrySendEngineer(engineer, candidate.Kind, candidate.TargetId, EngineerSourceKind.ShipCargo, material);
            Assert.AreEqual(ShipHangar.EngineerSendRejectReason.None, reason);

            Time.timeScale = 20f;
            yield return new WaitForSeconds(9f); // clears TravelHome->WaitForSafety's required 8s continuous safety
            yield return new WaitForSeconds(6f); // real field-work ticking

            Assert.Greater(structure.Runtime.Durability.Armor, armorBefore, "the Engineer must have physically restored real Armor through the shared RepairAllocator/Durability path");
            Object.Destroy(structure.gameObject);
        }

        [UnityTest]
        public IEnumerator Engineer_RealTargetCandidate_RecoversADisabledVehicle()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            ShipHangar hangar = ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);
            FillCargoWithEverything(ship.GetComponent<ShipLogistics>().Cargo);

            VehicleAgent victim = hangar.RestoreVehicle("vehicle-test-victim", VehicleCatalog.Collector);
            Vector3 fieldPos = ship.transform.position + ship.transform.forward * 500f;
            victim.RestorePhysical(context.StartSystemId, fieldPos, Quaternion.identity, Vector3.zero,
                VehiclePhase.TravelOut, MissionKind.Collector, null, 0f, false, null, 0f);
            victim.RestoreDurability(0.01f, 0.01f, 0f, stranded: true); // Hull exactly 0 -> genuinely Disabled, never the pre-v13 full-health fallback
            Assert.IsTrue(victim.IsRecoverable, "test setup sanity: the victim vehicle must be a real Disabled+Stranded target");

            VehicleAgent engineer = hangar.RestoreVehicle("vehicle-test-eng-recover", VehicleCatalog.EngineerMk1);
            engineer.RestorePhysical(context.StartSystemId, fieldPos, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);
            engineer.transform.position = fieldPos;

            List<ShipHangar.EngineerTargetCandidate> candidates = hangar.BuildEngineerTargetCandidates(engineer);
            ShipHangar.EngineerTargetCandidate? found = null;
            foreach (var c in candidates) if (c.TargetId == victim.VehicleId) { found = c; break; }
            Assert.IsTrue(found.HasValue, "the disabled vehicle must appear in the real same-system Engineer target list");
            Assert.AreEqual(EngineerTargetKind.DisabledVehicle, found.Value.Kind);

            ShipHangar.EngineerSendRejectReason reason = hangar.TrySendEngineer(engineer, EngineerTargetKind.DisabledVehicle, victim.VehicleId, EngineerSourceKind.ShipCargo, null);
            Assert.AreEqual(ShipHangar.EngineerSendRejectReason.None, reason);

            Time.timeScale = 30f;
            yield return new WaitForSeconds(9f);   // 8s safety
            yield return new WaitForSeconds(65f);  // Mk1 recovery = 60 fixed seconds of field work

            Assert.IsFalse(victim.Stranded, "recovery must have physically completed and cleared Stranded");
            Assert.Greater(victim.DurabilityRef.Hull, 0f, "a completed recovery must restore real Hull, never stay at 0");
        }

        [UnityTest]
        public IEnumerator Engineer_TrySendEngineer_Drill_IsExplicitlyRejected_NeverASilentNoOp()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(1);

            VehicleAgent engineer = hangar.RestoreVehicle("vehicle-test-eng-drill", VehicleCatalog.EngineerMk1);
            engineer.RestorePhysical(context.StartSystemId, context.Ship.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);

            ShipHangar.EngineerSendRejectReason reason = hangar.TrySendEngineer(engineer, EngineerTargetKind.Drill, "any-drill-id", EngineerSourceKind.ShipCargo, null);
            Assert.AreEqual(ShipHangar.EngineerSendRejectReason.TargetHasNoDurability, reason, "Drill must be explicitly rejected, never silently accepted or silently ignored");
            Assert.IsNull(engineer.EngineerTask, "a rejected send must never assign a mission");
        }

        // --- Escort: real UI-facing protectable target ----------------------------

        [UnityTest]
        public IEnumerator Escort_RealProtectableTarget_AssignsAndPhysicallyProtects()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);

            List<ShipHangar.EscortProtectableTarget> targets = hangar.BuildEscortProtectableTargets();
            ShipHangar.EscortProtectableTarget? shipTarget = null;
            foreach (var t in targets) if (t.TargetId == "ship") { shipTarget = t; break; }
            Assert.IsTrue(shipTarget.HasValue, "the main ship must always be a real protectable target for Escort");

            VehicleAgent escort = hangar.RestoreVehicle("vehicle-test-escort-real", VehicleCatalog.EscortMk1);
            escort.RestorePhysical(context.StartSystemId, context.Ship.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);

            ShipHangar.EscortAssignRejectReason reason = hangar.TryAssignEscort(escort, shipTarget.Value.TargetId, shipTarget.Value.Category);
            Assert.AreEqual(ShipHangar.EscortAssignRejectReason.None, reason);
            Assert.IsNotNull(escort.EscortState);
            Assert.AreEqual("ship", escort.EscortState.ProtectedTargetId);
        }

        // --- Maintenance Reserve: physical ship<->reserve transfer -----------------

        [UnityTest]
        public IEnumerator ShipMaintenanceReserveAccess_PhysicalDepositAndWithdraw_ConservesResources()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            LogisticsCargo shipCargo = ship.GetComponent<ShipLogistics>().Cargo;
            // These assertions are on ABSOLUTE stock totals, so this test owns its own
            // starting cargo outright rather than layering on top of whatever a fresh
            // campaign happens to be seeded with (ShipLogistics.StartingBasicParts).
            shipCargo.Clear();
            shipCargo.Add(ProcessingCatalog.BasicPartId, 100f);

            PlanetManifest outerPlanet = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outerPlanet.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 1f);
            var runtime = new StructureRuntime(def, DrillLifecycle.Active, 0f, 0f, durability);
            SurfaceStructure command = SurfaceStructure.Restore(planet, profile, def, profile.CommandNode, runtime, null, null);
            yield return null;

            var access = ship.GetComponent<ShipMaintenanceReserveAccess>();
            ship.transform.position = command.transform.position;
            yield return null; // Update() re-checks proximity

            Assert.IsTrue(access.InRange, "the ship must detect the real Command Center within ApproachRange, no teleport shortcut");

            ReserveTransferRejectReason depositReason = access.TryDeposit(ProcessingCatalog.BasicPartId, 30f);
            Assert.AreEqual(ReserveTransferRejectReason.None, depositReason);
            Assert.AreEqual(70f, shipCargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f);
            Assert.AreEqual(30f, command.MaintenanceReserve.Cargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f);

            ReserveTransferRejectReason rejectRaw = access.TryDeposit("resource.metal", 10f);
            Assert.AreEqual(ReserveTransferRejectReason.ItemNotAccepted, rejectRaw, "a raw resource must be rejected by the whitelist, never physically moved");

            ReserveTransferRejectReason withdrawReason = access.TryWithdraw(ProcessingCatalog.BasicPartId, 10f);
            Assert.AreEqual(ReserveTransferRejectReason.None, withdrawReason);
            Assert.AreEqual(80f, shipCargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f);
            Assert.AreEqual(20f, command.MaintenanceReserve.Cargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f);

            Object.Destroy(command.gameObject);
        }

        // --- Collector Mk II: real physical Maintenance Resupply flight ----------

        [UnityTest]
        public IEnumerator CollectorMk2_RealResupplyMission_PhysicallyTransfersCargoIntoTheCommandReserve()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            ShipHangar hangar = ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);
            LogisticsCargo shipCargo = ship.GetComponent<ShipLogistics>().Cargo;
            // These assertions are on ABSOLUTE stock totals, so this test owns its own
            // starting cargo outright rather than layering on top of whatever a fresh
            // campaign happens to be seeded with (ShipLogistics.StartingBasicParts).
            shipCargo.Clear();
            shipCargo.Add(ProcessingCatalog.BasicPartId, 100f);

            PlanetManifest outerPlanet = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outerPlanet.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 1f);
            var runtime = new StructureRuntime(def, DrillLifecycle.Active, 0f, 0f, durability);
            SurfaceStructure command = SurfaceStructure.Restore(planet, profile, def, profile.CommandNode, runtime, null, null);
            yield return null;

            VehicleAgent collector = hangar.RestoreVehicle("vehicle-test-resupply", VehicleCatalog.CollectorMk2);
            collector.RestorePhysical(context.StartSystemId, ship.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);

            ShipHangar.MaintenanceResupplyRejectReason reason = hangar.TryStartMaintenanceResupply(collector, ProcessingCatalog.BasicPartId, 40f, command.TargetId);
            Assert.AreEqual(ShipHangar.MaintenanceResupplyRejectReason.None, reason);
            Assert.AreEqual(60f, shipCargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f, "the Collector must have physically loaded the cargo out of the ship immediately");
            Assert.IsNotNull(collector.ResupplyTask);

            collector.transform.position = command.transform.position; // isolates unloading, not the travel bound already covered elsewhere

            Time.timeScale = 15f;
            float waited = 0f;
            while (waited < 10f && command.MaintenanceReserve.Cargo.GetStored(ProcessingCatalog.BasicPartId) < 39.9f)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            Assert.AreEqual(40f, command.MaintenanceReserve.Cargo.GetStored(ProcessingCatalog.BasicPartId), 0.5f, "the real physical Unloading tick must deliver exactly the loaded amount into the Command Reserve");
            Object.Destroy(command.gameObject);
        }

        [UnityTest]
        public IEnumerator CollectorMk1_MaintenanceResupply_IsRejected_VehicleNotEligible()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(1);
            context.Ship.GetComponent<ShipLogistics>().Cargo.Add(ProcessingCatalog.BasicPartId, 100f);

            VehicleAgent collectorMk1 = hangar.RestoreVehicle("vehicle-test-resupply-mk1", VehicleCatalog.Collector);
            collectorMk1.RestorePhysical(context.StartSystemId, context.Ship.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);

            ShipHangar.MaintenanceResupplyRejectReason reason = hangar.TryStartMaintenanceResupply(collectorMk1, ProcessingCatalog.BasicPartId, 20f, "sys/pl00/command");
            Assert.AreEqual(ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible, reason, "Collector Mk I must never be accepted for Maintenance Resupply -- Mk II only");
            Assert.IsNull(collectorMk1.ResupplyTask);
        }

        // --- Support structures: real UI-driven build + full multi-item refund ---

        [UnityTest]
        public IEnumerator SupportBuildHud_DrivenBuild_MaintenancePlatformT1_BuildsOnARealNode()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            ShipHangar hangar = ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);
            Progression.ProgressionService.Award("test.push-support-level", 500000);
            FillCargoWithEverything(ship.GetComponent<ShipLogistics>().Cargo);

            PlanetManifest outerPlanet = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outerPlanet.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            ExtractionState.CompletePlanetScan(planet);

            // A real, already-Active Command T2 (Maintenance Platform T1's own gate) -- built directly via
            // Restore rather than the slow real upgrade flow, exactly like Vs0132RepairIntegrityTests does.
            SurfaceStructureDef commandDef = SurfaceStructureCatalog.Command;
            var commandDurability = new Durability(commandDef.ShieldMax, commandDef.ArmorMax, commandDef.HullMax, 1f);
            var commandRuntime = new StructureRuntime(commandDef, DrillLifecycle.Active, 0f, 0f, commandDurability, tier: 2);
            SurfaceStructure command = SurfaceStructure.Restore(planet, profile, commandDef, profile.CommandNode, commandRuntime, null, null);
            yield return null;

            // A real Engineer physically present in-system (Maintenance Platform T1's other gate).
            VehicleAgent engineer = hangar.RestoreVehicle("vehicle-test-support-eng", VehicleCatalog.EngineerMk1);
            engineer.RestorePhysical(context.StartSystemId, ship.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);
            engineer.gameObject.SetActive(true);

            var extraction = ship.GetComponent<ShipExtraction>();
            ship.transform.position = command.transform.position; // physically near the target planet, no teleport shortcut beyond normal test setup
            yield return null;
            yield return null;
            Assert.IsNotNull(extraction.TargetPlanet, "the ship must have physically detected the planet in range");

            extraction.ToggleSupportNodeSelection(); // 0 = Sensor Array -> 1 = Maintenance Platform, the exact seam SupportBuildHud's toggle button calls
            Assert.AreEqual(1, extraction.SelectedSupportNodeIndex);

            extraction.BuildOrUpgradeSupport(); // the exact seam SupportBuildHud's build button calls
            yield return null;

            SurfaceStructure built = ExtractionState.FindStructure(planet.WorldId, SurfaceStructureType.MaintenancePlatform);
            Assert.IsNotNull(built, $"BuildOrUpgradeSupport, reached only through the same public seam the UI button uses, must have created a real Maintenance Platform structure. Msg={extraction.TransientMessage}");

            Object.Destroy(command.gameObject);
            if (built != null) Object.Destroy(built.gameObject);
        }

        [UnityTest]
        public IEnumerator SupportBuild_Cancel_RefundsFloor75PercentOfEveryLineItem_NotJustBasicParts()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            LogisticsCargo cargo = ship.GetComponent<ShipLogistics>().Cargo;

            PlanetManifest outerPlanet = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outerPlanet.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceNode node = profile.SupportNode(1); // Maintenance Platform node

            List<(string itemId, float amount)> cost = PlanetaryDefenseCatalog.MaintenancePlatformT1.FullCost();
            // Absolute per-item assertions below -- own the starting stock outright rather than
            // layering on top of a fresh campaign's own seeded parts.
            cargo.Clear();
            foreach (var (itemId, amount) in cost) cargo.Add(itemId, amount);

            SurfaceStructure structure = SurfaceStructure.Create(planet, profile, SurfaceStructureCatalog.MaintenancePlatform, node, ship.transform.position, cargo, cost);
            yield return null;
            Assert.IsTrue(structure.Runtime.CanCancelForRefund, "sanity: must still be pre-Active for Cancel to apply the refund path");
            // Every cost item is reserved (physically present, no other stock of these items exists in this fresh cargo).
            foreach (var (itemId, amount) in cost) Assert.AreEqual(amount, cargo.GetStored(itemId), 0.01f, "sanity: reservation must not have removed physical stock yet");

            structure.Cancel(cargo);

            Assert.Greater(cost.Count, 1, "Maintenance Platform T1 must have more than one cost line for this regression to be meaningful");
            foreach (var (itemId, amount) in cost)
            {
                // Cancel removes the full reservation from stock, then adds floor(75%) back -- the absolute post-cancel
                // stock for an item this test never stocked beyond its own cost IS that floor(75%) refund.
                float expectedRemaining = Mathf.Floor(amount * SurfaceStructureCatalog.RefundFraction);
                Assert.AreEqual(expectedRemaining, cargo.GetStored(itemId), 0.01f, $"{itemId} must be refunded at floor(75%) -- this is the exact multi-item transaction bug this slice fixes");
            }
        }

        // --- InterventionGroup: Full -> demote -> Full save continuity -----------

        [UnityTest]
        public IEnumerator InterventionGroup_FullDemoteFullCycle_SameMembersPolicyAndTarget_NeverDuplicated()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(1);

            const string vehicleId = "vehicle-test-group-member";
            VehicleAgent engineer = hangar.RestoreVehicle(vehicleId, VehicleCatalog.EngineerMk1);
            engineer.RestorePhysical(startSystemId, context.Ship.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);

            InterventionGroupDirector director = context.Streaming.InterventionGroupDirector;
            Assert.IsNotNull(director);
            director.EnsureGroupForSystem(startSystemId);
            director.AddMember(vehicleId);
            director.SetPolicy(InterventionPolicy.DefendInPlace);
            Assert.AreEqual(1, director.Group.MemberVehicleIds.Count);

            // The real, full save/load round trip -- a docked-in-hangar vehicle's roster is ship-level save
            // state (never part of a SystemDelta), so only the real SaveNow/LoadNow cycle exercises both the
            // hangar roster restore AND the InterventionGroup's SystemDelta restore together, exactly like a
            // player's own save/load would.
            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<InterventionGroupDirector>().Length, "load must never leave a second intervention group director");
            InterventionGroupDirector directorAfter = context.Streaming.InterventionGroupDirector;
            Assert.IsNotNull(directorAfter.Group, "the saved group must be restored, not left null/Manual-empty");
            Assert.AreEqual(InterventionPolicy.DefendInPlace, directorAfter.Group.Policy);
            Assert.AreEqual(1, directorAfter.Group.MemberVehicleIds.Count);
            Assert.AreEqual(vehicleId, directorAfter.Group.MemberVehicleIds[0], "the same vehicle id must continue in the group -- never a re-minted id, never a second vehicle spawned");

            Assert.IsTrue(VehicleAgent.Registry.ContainsKey(vehicleId), "the same vehicle id must exist after load");
            // includeInactive:true -- a docked-in-hangar vehicle is a real, correctly-restored but INACTIVE
            // GameObject (VehiclePhase.InHangar), which the default FindObjectsOfType silently excludes.
            int matchingCount = 0;
            foreach (VehicleAgent v in Object.FindObjectsOfType<VehicleAgent>(true)) if (v.VehicleId == vehicleId) matchingCount++;
            Assert.AreEqual(1, matchingCount, "restore must never duplicate the vehicle across the save/load cycle");
        }
    }
}
