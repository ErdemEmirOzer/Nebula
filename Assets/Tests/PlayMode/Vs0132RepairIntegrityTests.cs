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
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for VS-013.2 (repair integrity hotfix): a
    /// single deterministic SafetyMarkerAnchor with no duplicates across
    /// restart, the real physical 1500-unit repair safe-zone check (Safety
    /// Marker and a real live active Command Center), and end-to-end
    /// save/load resource conservation for a partially-progressed repair
    /// package. Driven through the same real composition/public APIs a
    /// keypress or a save/load button already uses, exactly like every
    /// other PlayMode file in this project.
    /// </summary>
    public class Vs0132RepairIntegrityTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;

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
            slot1Backup.Clear();
            foreach (string f in Slot1Files())
                slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
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

        [UnityTest]
        public IEnumerator Composition_CreatesExactlyOneSafetyMarkerAnchor()
        {
            yield return LoadSceneFresh();
            var anchors = Object.FindObjectsOfType<SafetyMarkerAnchor>();
            Assert.AreEqual(1, anchors.Length);
        }

        [UnityTest]
        public IEnumerator RestartCampaign_DestroysTheOldAnchor_CreatesExactlyOneNewOne()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            SafetyMarkerAnchor firstAnchor = Object.FindObjectOfType<SafetyMarkerAnchor>();
            Assert.IsNotNull(firstAnchor);

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            Assert.IsTrue(firstAnchor == null, "the old Safety Marker anchor must be destroyed by the session's Dispose(), same as every other CampaignSession-lifetime object");
            var anchorsAfter = Object.FindObjectsOfType<SafetyMarkerAnchor>();
            Assert.AreEqual(1, anchorsAfter.Length, "no duplicate anchor after restart");
        }

        [UnityTest]
        public IEnumerator RepairCap_Is100PercentNearTheSafetyMarker_60PercentAtAFarPointInTheSameStartSystem()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            var logistics = ship.GetComponent<ShipLogistics>();
            SafetyMarkerAnchor marker = context.SafetyMarkerAnchor;

            logistics.Cargo.Add(ProcessingCatalog.BasicPartId, 200f);
            combat.State.RecoverToFractions(RepairCatalog.FieldCapFraction, RepairCatalog.FieldCapFraction, 0f);
            ship.SetVelocity(Vector3.zero);

            ship.transform.position = marker.transform.position;
            yield return new WaitForFixedUpdate();
            RepairRejectReason nearReason = combat.CheckRepairStart();
            Assert.AreEqual(RepairRejectReason.None, nearReason, "at exactly the field cap (60%) but within 1500 units of the Safety Marker, the 100% safe-zone cap must apply -- still eligible to repair further");

            ship.transform.position = marker.transform.position + new Vector3(5000f, 0f, 0f);
            yield return new WaitForFixedUpdate();
            RepairRejectReason farReason = combat.CheckRepairStart();
            Assert.AreEqual(RepairRejectReason.AlreadyAtCap, farReason, "5000 units from the Safety Marker (same start system, no Command Center nearby) must fall back to the 60% field cap -- already at cap, so blocked");
        }

        [UnityTest]
        public IEnumerator RepairCap_Is100Percent_NearARealLiveActiveCommandCenter_EvenFarFromTheSafetyMarker()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            var logistics = ship.GetComponent<ShipLogistics>();
            SafetyMarkerAnchor marker = context.SafetyMarkerAnchor;

            // The outermost planet -- deliberately far from Planets[0] (which the Safety
            // Marker's own position is derived from) so this genuinely exercises the
            // Command-Center branch, never coincidentally passing via marker proximity.
            PlanetManifest outerPlanet = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outerPlanet.PlanetId);
            Assert.IsNotNull(planet, "the start system's planets must already be Full-materialized after LoadSceneFresh");
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            SurfaceNode node = profile.CommandNode;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
            var runtime = new StructureRuntime(def, DrillLifecycle.Active, 0f, 0f, durability);
            SurfaceStructure structure = SurfaceStructure.Restore(planet, profile, def, node, runtime, null, null);
            yield return null; // let its own Update() place it on the real live surface point

            Assert.Greater(Vector3.Distance(structure.transform.position, marker.transform.position), RepairCatalog.RepairSafeRadius,
                "sanity: the Command Center must genuinely be outside the marker's own safe radius, or this test wouldn't isolate the Command-Center branch");

            logistics.Cargo.Add(ProcessingCatalog.BasicPartId, 200f);
            combat.State.RecoverToFractions(RepairCatalog.FieldCapFraction, RepairCatalog.FieldCapFraction, 0f);
            ship.SetVelocity(Vector3.zero);
            ship.transform.position = structure.transform.position;
            yield return new WaitForFixedUpdate();

            RepairRejectReason reason = combat.CheckRepairStart();
            Assert.AreEqual(RepairRejectReason.None, reason, "next to a real live Active Command Center, far from the Safety Marker, the 100% cap must still apply");

            Object.Destroy(structure.gameObject);
        }

        [UnityTest]
        public IEnumerator RepairCap_DisabledCommandCenter_NeverGrantsTheSafeZoneCap()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            var logistics = ship.GetComponent<ShipLogistics>();
            SafetyMarkerAnchor marker = context.SafetyMarkerAnchor;

            PlanetManifest outerPlanet = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outerPlanet.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            SurfaceNode node = profile.CommandNode;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
            durability.ApplyDamage(def.ShieldMax + def.ArmorMax + def.HullMax); // zero the hull -- Durability.Disabled must genuinely be true, or StructureRuntime.Tick flips a full-hull "Disabled" straight back to Active on the very next tick
            var runtime = new StructureRuntime(def, DrillLifecycle.Disabled, 0f, 0f, durability); // Disabled, not Active
            SurfaceStructure structure = SurfaceStructure.Restore(planet, profile, def, node, runtime, null, null);
            yield return null;

            Assert.Greater(Vector3.Distance(structure.transform.position, marker.transform.position), RepairCatalog.RepairSafeRadius);

            logistics.Cargo.Add(ProcessingCatalog.BasicPartId, 200f);
            combat.State.RecoverToFractions(RepairCatalog.FieldCapFraction, RepairCatalog.FieldCapFraction, 0f);
            ship.SetVelocity(Vector3.zero);
            ship.transform.position = structure.transform.position;
            yield return new WaitForFixedUpdate();

            RepairRejectReason reason = combat.CheckRepairStart();
            Assert.AreEqual(RepairRejectReason.AlreadyAtCap, reason, "a Disabled Command Center must never grant the 100% safe-zone cap -- only a live Active one may");

            Object.Destroy(structure.gameObject);
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_PartiallyProgressedRepairPackage_ConservesResources_ReservesOnlyTheRemainder()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipController ship = context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            var logistics = ship.GetComponent<ShipLogistics>();

            logistics.Cargo.Add(ProcessingCatalog.BasicPartId, 200f); // Form I package cost: 2 parts
            float stockAtVeryStart = logistics.Cargo.GetStored(ProcessingCatalog.BasicPartId);
            combat.ApplyDamage(100f);
            combat.State.RecoverToFractions(0.25f, 0.10f, 0f);
            ship.SetVelocity(Vector3.zero);

            yield return new WaitForSeconds(8.3f); // clear the post-damage repair gate (same real-time wait as the VS-013.1 repair test)

            Assert.AreEqual(RepairRejectReason.None, combat.CheckRepairStart());
            Assert.IsTrue(combat.TryStartRepair());

            yield return new WaitForSeconds(3f); // real, measurable, but deliberately incomplete progress into the first 10% package
            Assert.IsTrue(combat.Repair.Active);
            float progressBeforeSave = combat.Repair.PackageProgress01;
            Assert.Greater(progressBeforeSave, 0f);
            Assert.Less(progressBeforeSave, 1f);
            float partsBeforeSave = logistics.Cargo.GetStored(ProcessingCatalog.BasicPartId);

            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;

            var reloadedCombat = ship.GetComponent<ShipCombat>();
            var reloadedLogistics = ship.GetComponent<ShipLogistics>();
            Assert.IsTrue(reloadedCombat.Repair.Active, "an active partial repair must survive the round trip");
            Assert.AreEqual(progressBeforeSave, reloadedCombat.Repair.PackageProgress01, 0.02f);
            Assert.AreEqual(partsBeforeSave, reloadedLogistics.Cargo.GetStored(ProcessingCatalog.BasicPartId), 0.05f,
                "restoring must never itself consume or duplicate physical stock -- only the reservation is re-seeded");

            float fullCost = RepairCatalog.PackageCost(reloadedCombat.Repair.HullFormForCurrentPackage)[0].amount;
            float expectedRemaining = fullCost * (1f - progressBeforeSave);
            Assert.AreEqual(expectedRemaining, reloadedLogistics.Cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId), 0.05f,
                "restore must reserve exactly the remaining fraction of the package, never the full cost again (the phantom-reservation bug)");

            // Let the restored package run to completion and confirm the reservation
            // never accumulates a leftover on top of the auto-chained next package.
            float progressPrev = reloadedCombat.Repair.PackageProgress01;
            float maxWait = RepairCatalog.PackageDurationSeconds * (1f - progressBeforeSave) + 3f;
            float waited = 0f;
            bool completedOnce = false;
            while (waited < maxWait)
            {
                yield return null;
                waited += Time.deltaTime;
                if (reloadedCombat.Repair.PackageProgress01 < progressPrev - 0.5f) { completedOnce = true; break; }
                progressPrev = reloadedCombat.Repair.PackageProgress01;
            }
            Assert.IsTrue(completedOnce, "the restored package must complete within its own expected remaining duration");

            // Measured against the stock captured before ANY repair activity began (not the
            // mid-test save-point snapshot) -- the real end-to-end invariant this hotfix
            // guarantees is that ONE package, split across a save/load boundary at any
            // progress point, still consumes in total exactly its own cost. Using the
            // save-point snapshot here would be sensitive to exactly which real-time frame
            // the completion poll happens to land on; this is not.
            float stockAfterCompletion = reloadedLogistics.Cargo.GetStored(ProcessingCatalog.BasicPartId);
            Assert.AreEqual(stockAtVeryStart - fullCost, stockAfterCompletion, 0.3f,
                "exactly one package's worth of resources consumed in total for the restored package -- no phantom double reservation, no free repair");

            float reservationAfterCompletion = reloadedLogistics.Cargo.GetOwnerReservation(RepairCatalog.RepairCargoOwnerId, ProcessingCatalog.BasicPartId);
            Assert.AreEqual(fullCost, reservationAfterCompletion, 0.3f,
                "immediately after completion the reservation must be exactly the NEXT package's own full cost -- never stacked with a leftover from the just-finished restored one");
        }
    }
}
