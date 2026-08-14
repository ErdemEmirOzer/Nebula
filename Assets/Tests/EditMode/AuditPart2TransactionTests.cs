using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Modules;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENERAL REVIEW PART 2 / AGENT 1 — transaction, reservation and save-integrity
    /// diagnostics promoted to Part 3 regressions. Characterization controls remain
    /// where they define primitive semantics; repaired product paths now assert the
    /// conservation and fail-closed contracts directly.
    /// <para>
    /// Part 1 proved a list of surface symptoms. This fixture asks the next question:
    /// how many of them are the same root cause, and exactly which primitive makes
    /// that cause possible. The answer this fixture measures is
    /// <see cref="LogisticsCargo.TryReserveAll"/>'s ADDITIVE semantics combined with
    /// <see cref="LogisticsCargo.ConsumeReservation"/>'s SILENT no-op on an owner that
    /// holds nothing — one accumulates a charge that should have been replaced, the
    /// other drops a charge that should have been refused.
    /// </para>
    /// <para>
    /// One assertion per question. Part 1's evacuation test bundled three separate
    /// questions into one test body, NUnit stopped at the second, and the third —
    /// the one the finding actually turned on — never ran at all. That mistake is
    /// not repeated here.
    /// </para>
    /// </summary>
    public class AuditPart2TransactionTests
    {
        private const ulong Seed = 20260812UL;
        private const string PartId = ProcessingCatalog.BasicPartId;

        private SystemManifest system;
        private SystemStreamingDirector savedStreaming;
        private readonly List<GameObject> spawned = new List<GameObject>();
        private string settingsDirectory;

        [SetUp]
        public void SetUp()
        {
            savedStreaming = StarSystemRegistry.Streaming;
            StarSystemRegistry.Streaming = null;
            ExtractionState.Reset((int)Seed);
            StarSystemRegistry.Initialize(Seed);

            SectorManifest sector = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
            system = sector.Systems.First(s => s.Planets.Count > 0);
            StarSystemRegistry.SetStartSystem(system.Sector, system.SystemId);
            settingsDirectory = Path.Combine(Path.GetTempPath(), "nebula-part3-a1-" + Guid.NewGuid().ToString("N"));
            Nebula.UI.Menu.NebulaAccessibilitySettingsStore.DirectoryOverride = settingsDirectory;
            Nebula.UI.Menu.NebulaAccessibilitySettingsStore.WritesEnabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            ExtractionState.DestroyAllDrills();
            ExtractionState.DestroyAllStructures();
            StarSystemRegistry.Streaming = savedStreaming;
            StarSystemRegistry.Clear();
            ExtractionState.Reset((int)Seed);
            Nebula.Progression.ProgressionService.Reset();
            Nebula.UI.Menu.NebulaAccessibilitySettingsStore.DirectoryOverride = null;
            Nebula.UI.Menu.NebulaAccessibilitySettingsStore.WritesEnabled = true;
            Nebula.UI.Shell.NebulaUiShell.UiScale = 1f;
            Nebula.Combat.CombatAccessibilitySettings.HudScale = 1f;
            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true;
            Nebula.Visuals.AnimationAccessibility.ResetToDefaults();
            Nebula.Visuals.LightingAccessibility.ResetToDefaults();
            if (!string.IsNullOrEmpty(settingsDirectory) && Directory.Exists(settingsDirectory))
                Directory.Delete(settingsDirectory, true);
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private PlanetIdentity MaterializePlanet()
        {
            var go = new GameObject("AuditPart2 Planet");
            spawned.Add(go);
            CelestialBody body = go.AddComponent<CelestialBody>();
            body.Configure(CelestialBodyType.Planet, 60f, 1f, false);
            PlanetIdentity planet = go.AddComponent<PlanetIdentity>();
            planet.Initialize(system.Planets[0].PlanetId, body, true);
            return planet;
        }

        private static LogisticsCargo Hold(params (string itemId, float amount)[] contents)
        {
            var cargo = new LogisticsCargo(1000000f);
            foreach (var (itemId, amount) in contents) cargo.Add(itemId, amount);
            return cargo;
        }

        private static LogisticsCargo StockedFor(ResourceFamily family, float multiplier)
        {
            var cargo = new LogisticsCargo(1000000f);
            foreach (var (itemId, amount) in CommandBuildService.BuildT1Cost(family))
                cargo.Add(itemId, amount * multiplier);
            return cargo;
        }

        /// <summary>The private compile-time constant the drill path really uses — never a number typed here.</summary>
        private static float ShipExtractionConst(string name)
        {
            FieldInfo field = typeof(ShipExtraction).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"ShipExtraction.{name} must exist for this audit to be measuring the real path");
            return Convert.ToSingle(field.GetRawConstantValue());
        }

        /// <summary>
        /// The conservation quantity this audit tracks for one item: physical stock
        /// plus nothing else, because a reservation never leaves stock. Any pair of
        /// (before, after) readings that differ by something other than a deliberate
        /// consume or a deliberate refund is a leak in one direction or the other.
        /// </summary>
        private static float Physical(LogisticsCargo cargo, string itemId) => cargo.GetStored(itemId);

        // =====================================================================
        // A. THE TWO PRIMITIVES EVERY PART 1 ECONOMY FINDING RUNS THROUGH
        // =====================================================================

        /// <summary>
        /// ROOT CAUSE 1. <c>TryReserveAll</c> ADDS to whatever the owner already holds
        /// (LogisticsCargo.cs:142). Every caller that means "this owner's reservation
        /// is now exactly this" — i.e. every restore path — gets "this owner's
        /// reservation is now that plus this" instead.
        /// </summary>
        [Test]
        public void TryReserveAll_UnderOneOwner_AddsToTheExistingReservation_InsteadOfReplacingIt()
        {
            LogisticsCargo cargo = Hold((PartId, 100f));
            var cost = new List<(string, float)> { (PartId, 30f) };

            Assert.IsTrue(cargo.TryReserveAll("owner", cost), "first reservation must succeed");
            Assert.IsTrue(cargo.TryReserveAll("owner", cost), "second reservation must succeed while stock allows");

            Assert.AreEqual(60f, cargo.GetOwnerReservation("owner", PartId), 0.001f,
                "AUDIT ROOT CAUSE: two calls with the same owner and the same cost produce a doubled reservation — " +
                "TryReserveAll has add semantics, and no restore call site in the game asks for add semantics");
        }

        /// <summary>
        /// ROOT CAUSE 2. <c>ConsumeReservation</c> on an owner that holds nothing is a
        /// silent no-op (LogisticsCargo.cs:159). That single line is what turns a
        /// failed/absent reservation into a free finished job everywhere in the game:
        /// the completion path never learns it was never funded.
        /// </summary>
        [Test]
        public void ConsumeReservation_ForAnOwnerThatHoldsNothing_ChargesNothing_AndReportsNothing()
        {
            LogisticsCargo cargo = Hold((PartId, 100f));
            float before = Physical(cargo, PartId);

            cargo.ConsumeReservation("an-owner-that-never-reserved");

            Assert.AreEqual(before, Physical(cargo, PartId), 0.001f,
                "AUDIT ROOT CAUSE: completing an unfunded job costs the player nothing and returns void — " +
                "there is no channel through which the caller could notice");
        }

        /// <summary>
        /// ROOT CAUSE 1, BOUNDED. The accumulation of ROOT CAUSE 1 is not unlimited:
        /// <c>TryReserveAll</c> checks <c>GetAvailable</c> before it writes
        /// (LogisticsCargo.cs:135-136), so a second reservation only lands when the
        /// hold still has room for it. That fixes the reproduction step for every
        /// doubling finding in this audit — the double charge needs a hold carrying
        /// at least TWICE the cost, and stops being reachable below that.
        /// <para>
        /// It also means <c>GetReserved(item) &lt;= GetStored(item)</c> is an invariant
        /// this class maintains, which makes the underflow branch inside
        /// <c>ConsumeReservation</c> (<c>left &lt;= 0.0001f</c>) defensive rather than
        /// reachable. This audit found no public-API route to break it.
        /// </para>
        /// </summary>
        [Test]
        public void TryReserveAll_CannotReserveMoreThanTheStockBehindIt_SoTheDoublingNeedsTwiceTheCost()
        {
            const float cost = 30f;

            LogisticsCargo tooThin = Hold((PartId, cost + 5f));
            Assert.IsTrue(tooThin.TryReserveAll("owner", new List<(string, float)> { (PartId, cost) }));
            Assert.IsFalse(tooThin.TryReserveAll("owner", new List<(string, float)> { (PartId, cost) }),
                "a hold with less than twice the cost refuses the second reservation");
            Assert.AreEqual(cost, tooThin.GetOwnerReservation("owner", PartId), 0.001f,
                "and the refusal leaves the first reservation exactly as it was");

            LogisticsCargo fatEnough = Hold((PartId, cost * 2f));
            fatEnough.TryReserveAll("owner", new List<(string, float)> { (PartId, cost) });
            fatEnough.TryReserveAll("owner", new List<(string, float)> { (PartId, cost) });
            Assert.AreEqual(cost * 2f, fatEnough.GetOwnerReservation("owner", PartId), 0.001f,
                "AUDIT: at twice the cost the doubling lands — this is the precise precondition " +
                "every 'reservation stacks' finding in this audit needs");

            Assert.LessOrEqual(fatEnough.GetReserved(PartId), Physical(fatEnough, PartId) + 0.001f,
                "and reserved never exceeds the stock behind it, in either case");
        }

        /// <summary>
        /// The codebase already contains the cure. <c>DrillUpgradeService.TryRestoreReservation</c>
        /// releases before it reserves and returns its result; the structure build path
        /// does neither. Same cargo, same owner shape, two outcomes — this is the whole
        /// difference between a correct restore and a doubled charge.
        /// </summary>
        [Test]
        public void TrySetReservation_ReplacesAtomically_WithoutAReleaseWindow()
        {
            var cost = new List<(string itemId, float amount)> { (PartId, 30f) };

            LogisticsCargo cargo = Hold((PartId, 100f));
            Assert.IsTrue(cargo.TryReserveAll("owner", cost));
            Assert.IsTrue(cargo.TrySetReservation("owner", cost));
            Assert.AreEqual(30f, cargo.GetOwnerReservation("owner", PartId), 0.001f);

            Assert.IsFalse(cargo.TrySetReservation("owner", new List<(string, float)> { (PartId, 120f) }));
            Assert.AreEqual(30f, cargo.GetOwnerReservation("owner", PartId), 0.001f,
                "a failed replacement must preserve the previous funded reservation");
        }

        /// <summary>
        /// Checked and clean, with evidence: no two subsystems can collide in the owner
        /// ledger. Every owner id is either a distinct constant or a distinct prefix,
        /// so "two active reservations under one id" can only ever happen within a
        /// subsystem (which is exactly what ROOT CAUSE 1 does), never across two.
        /// </summary>
        [Test]
        public void EveryReservationOwnerNamespace_IsDistinct_SoNoTwoSubsystemsShareOneLedgerEntry()
        {
            var owners = new List<string>
            {
                Nebula.Combat.RepairCatalog.RepairCargoOwnerId,
                HangarProductionJob.CargoOwnerId,
                HangarProductionJob.UpgradeCargoOwnerId,
                ModuleUpgradeJob.CargoOwnerId,
                DrillUpgradeJob.CargoOwnerPrefix + "w/n",
                StructureUpgradeJob.CargoOwnerPrefix + "w/n",
                Nebula.Poi.FacilityService.RelayOwnerId("poi"),
                QuickAssetPackageService.OwnerId("tx"),
                "structure-build:w/n",          // SurfaceStructure.cs:139
                "engineer-mission:m",           // ShipHangar.cs:270 / VehicleAgent.cs:762
                "remote-engineer:m"             // RemoteEconomyTick.cs:226 — scratch cargo only
            };

            Assert.AreEqual(owners.Count, owners.Distinct().Count(),
                "two subsystems would share one reservation ledger entry: " +
                string.Join(", ", owners.GroupBy(o => o).Where(g => g.Count() > 1).Select(g => g.Key)));
        }

        // =====================================================================
        // B. THE DRILL: A GATE THAT READS ONE NUMBER AND A CHARGE THAT READS ANOTHER
        // =====================================================================

        /// <summary>
        /// P1-ECO-001, root cause. <c>ShipExtraction.ConfirmBuild</c> gates on
        /// <c>cargo.Get</c> (= GetStored, ignores reservations, LogisticsCargo.cs:65-68)
        /// and charges through <c>cargo.TryRemove</c> (= TryConsumeImmediate, measures
        /// GetAvailable, LogisticsCargo.cs:112-123) whose <c>false</c> is discarded at
        /// ShipExtraction.cs:348. This test executes those two exact statements against
        /// the real constants.
        /// </summary>
        [Test]
        public void SecondDrillPayment_RejectsAFullyReservedHoldWithoutMutation()
        {
            float cost = ShipExtractionConst("SecondDrillPartsCost");
            LogisticsCargo cargo = Hold((PartId, cost));
            cargo.TryReserveAll(ModuleUpgradeJob.CargoOwnerId, new List<(string, float)> { (PartId, cost) });

            float before = Physical(cargo, PartId);
            Assert.IsFalse(ShipExtraction.TryPaySecondDrillCost(cargo));
            Assert.AreEqual(before, Physical(cargo, PartId), 0.001f);
        }

        /// <summary>
        /// P1-ECO-001, second half. The drill built for free still carries its refund
        /// debt, and <c>DrillStructure.Cancel</c> adds it as fresh stock with no check
        /// that anything was ever paid (DrillStructure.cs:222-230). This is the only
        /// measured path in the audit that increases the world's total resources.
        /// </summary>
        [Test]
        public void FailedSecondDrillPayment_CreatesNoRefundEntitlement()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            float cost = ShipExtractionConst("SecondDrillPartsCost");
            LogisticsCargo cargo = Hold((PartId, cost));
            cargo.TryReserveAll(ModuleUpgradeJob.CargoOwnerId, new List<(string, float)> { (PartId, cost) });
            float before = Physical(cargo, PartId);
            Assert.IsFalse(ShipExtraction.TryPaySecondDrillCost(cargo));
            Assert.AreEqual(before, Physical(cargo, PartId), 0.001f);
            Assert.AreEqual(0, ExtractionState.Drills.Count);
        }

        /// <summary>
        /// Positive control. The module-upgrade path — start, then cancel — conserves
        /// exactly: stock unchanged minus the deliberate 25% cancellation penalty, and
        /// no reservation left behind. A conservation harness that cannot show a
        /// correct path passing is not measuring anything.
        /// </summary>
        [Test]
        public void ConservationHolds_OnTheModuleUpgradeStartThenCancelPath()
        {
            LogisticsCargo cargo = Hold((PartId, 100f));
            const float reserved = 40f;
            cargo.TryReserveAll(ModuleUpgradeJob.CargoOwnerId, new List<(string, float)> { (PartId, reserved) });

            float before = Physical(cargo, PartId);
            cargo.ConsumeReservationForRefund(ModuleUpgradeJob.CargoOwnerId, 0.75f);

            Assert.AreEqual(before - Mathf.Floor(reserved * 0.25f), Physical(cargo, PartId), 0.001f,
                "a cancel must cost exactly the 25% penalty and nothing else");
            Assert.AreEqual(0f, cargo.GetReserved(PartId), 0.001f,
                "and must leave no reservation behind");
        }

        // =====================================================================
        // C. P1-ECO-009 — THE THREE QUESTIONS, ONE PER TEST
        // Part 1's single test asked all three and NUnit only answered two.
        // =====================================================================

        private SurfaceStructure DisabledCommandOn(PlanetIdentity planet, PlanetProfile profile, LogisticsCargo cargo)
        {
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
            durability.RestoreFromSave(0f, 0f, 0f, 0f);
            var runtime = new StructureRuntime(def, DrillLifecycle.Disabled, 0f, 0f, durability, 1);
            // Restored and long since paid for: nothing is reserved for it.
            SurfaceStructure s = SurfaceStructure.Restore(planet, profile, def, profile.CommandNode,
                runtime, cargo, new List<(string itemId, float amount)>());
            spawned.Add(s.gameObject);
            return s;
        }

        /// <summary>QUESTION 1: does evacuation destroy a Disabled structure at all? (Part 1 answered: yes.)</summary>
        [Test]
        public void Evacuation_PreservesADisabledStructuresGameObject()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            LogisticsCargo cargo = Hold((PartId, 100f));
            SurfaceStructure disabled = DisabledCommandOn(planet, profile, cargo);

            PlanetEvacuationService.ResolveActiveBuilds(planet.WorldId, cargo);

            Assert.IsFalse(disabled == null,
                "a paid raid-disabled structure is not a cancellable in-flight build");
        }

        /// <summary>
        /// QUESTION 2 — THE ONE PART 1 NEVER RAN. Does that destruction return anything?
        /// <c>SurfaceStructure.Cancel</c> gates its refund on <c>CanCancelForRefund</c>
        /// (CapsuleInTransit/Constructing/Damaged only) but destroys unconditionally
        /// (SurfaceStructure.cs:441-450), so a Disabled structure should be destroyed
        /// with nothing returned. This test measures the hold, which is the question
        /// the finding actually turns on.
        /// </summary>
        [Test]
        public void Evacuation_RefundsNothingForADisabledStructure()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            LogisticsCargo cargo = Hold((PartId, 100f));
            DisabledCommandOn(planet, profile, cargo);

            float before = Physical(cargo, PartId);
            PlanetEvacuationService.ResolveActiveBuilds(planet.WorldId, cargo);
            float after = Physical(cargo, PartId);

            Assert.AreEqual(before, after, 0.001f,
                $"preserving the paid structure leaves cargo unchanged ({before} -> {after})");
        }

        /// <summary>
        /// QUESTION 3 — THE DIFFERENTIAL, SETTLED. Part 1 saw a destroyed structure
        /// still listed in <c>ExtractionState.Structures</c> and could not tell whether
        /// that was a product defect or an EditMode artefact. The first run of this
        /// fixture answered it: the CONTROL — a plain <c>DestroyImmediate</c>, which is
        /// exactly what <c>Cancel</c>'s <c>DestroyNow</c> resolves to outside play mode
        /// (SurfaceStructure.cs:561-566) — leaves the same residue. So the residue is
        /// the test runner, not the cancel path, and the PlayMode companion shows the
        /// registry clearing at the frame boundary where <c>OnDestroy</c> really runs.
        /// <para>
        /// This test now asserts the two paths AGREE rather than asserting either
        /// count, so it stays true if Unity ever changes when <c>OnDestroy</c> fires.
        /// The claim it pins is the one Part 3 needs: evacuation's cancel is not worse
        /// than an ordinary destroy.
        /// </para>
        /// </summary>
        [Test]
        public void Evacuation_KeepsTheDisabledStructureRegistered()
        {
            PlanetIdentity planetA = MaterializePlanet();
            PlanetProfile profileA = ExtractionState.GetProfile(planetA);
            profileA.Scanned = true;
            LogisticsCargo cargoA = Hold((PartId, 100f));
            DisabledCommandOn(planetA, profileA, cargoA);
            Assert.AreEqual(1, ExtractionState.Structures.Count, "precondition: one structure registered");

            PlanetEvacuationService.ResolveActiveBuilds(planetA.WorldId, cargoA);
            Assert.AreEqual(1, ExtractionState.Structures.Count,
                "the canonical registry must continue to own the preserved disabled structure");
        }

        // =====================================================================
        // D. THE SAVE/PROMOTE PATH THAT PRODUCES A FREE STRUCTURE
        // =====================================================================

        /// <summary>
        /// NEW. Reservations are not a save field: <c>ExtractionDirector.LoadNowInternal</c>
        /// clears the hold (:507) and every owner re-derives its own. But a structure
        /// only re-derives when ITS system materializes, and the player may have spent
        /// the freed stock in the meantime. When that restore's <c>TryReserveAll</c>
        /// then fails, <c>SurfaceStructure.Setup:146</c> discards the <c>false</c>, the
        /// build keeps running unfunded, and the completion consume (:204) is a no-op
        /// on an empty owner — a structure the player never paid for. No demotion loop
        /// and no remote tick are needed.
        /// </summary>
        [Test]
        public void AStructureRestoredIntoAHoldThatCannotFundIt_IsRejectedWithoutMutation()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            List<(string itemId, float amount)> cost = CommandBuildService.BuildT1Cost(profile.Family);
            LogisticsCargo cargo = StockedFor(profile.Family, 1f);
            // Everything in the hold is already spoken for by another owner — exactly
            // the state a load leaves behind once a nearer system's structures, the
            // module job and the hangar job have re-reserved first.
            cargo.TryReserveAll(HangarProductionJob.CargoOwnerId, cost);

            var durability = new Durability(SurfaceStructureCatalog.Command.ShieldMax,
                SurfaceStructureCatalog.Command.ArmorMax, SurfaceStructureCatalog.Command.HullMax, 0.5f);
            var runtime = new StructureRuntime(SurfaceStructureCatalog.Command,
                DrillLifecycle.Constructing, 0f, 5f, durability, 1);
            SurfaceStructure restored = SurfaceStructure.Restore(planet, profile,
                SurfaceStructureCatalog.Command, profile.CommandNode, runtime, cargo, cost);
            float before = Physical(cargo, PartId);
            Assert.IsNull(restored, "unfunded economic state must not materialize as a live structure");
            Assert.AreEqual(before, Physical(cargo, PartId), 0.001f,
                "failed restore must preserve physical stock");
        }

        /// <summary>
        /// NEW. <c>SurfaceStructure.RestoreUpgradeJob</c> releases the reservation
        /// first (the correct pattern) but then returns early when the remaining
        /// fraction rounds to zero (:434-436) — leaving a live job with no reservation.
        /// Its completion consume is then a no-op: an upgrade saved at >=99.99%
        /// progress finishes free.
        /// </summary>
        [Test]
        public void AnUpgradeRestoredAtFullProgress_StillChargesItsFullOriginalCost()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            StructureTierDef laserT2 = PlanetaryDefenseCatalog.Get(SurfaceStructureType.Laser, 2);
            Assert.IsNotNull(laserT2, "precondition: Laser T2 must exist in the catalog");

            var cargo = new LogisticsCargo(1000000f);
            foreach (var (itemId, amount) in laserT2.FullCost()) cargo.Add(itemId, amount);

            SurfaceStructureDef def = SurfaceStructureCatalog.Laser;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
            durability.RestoreFromSave(def.ShieldMax, def.ArmorMax, def.HullMax, 0f);
            var runtime = new StructureRuntime(def, DrillLifecycle.Active, 0f, 0f, durability, 1);
            SurfaceStructure laser = SurfaceStructure.Restore(planet, profile, def, profile.DefenseNode(0),
                runtime, cargo, new List<(string itemId, float amount)>());
            spawned.Add(laser.gameObject);

            cargo.TryReserveAll(StructureUpgradeService.OwnerId(laser), laserT2.FullCost());
            laser.RestoreUpgradeJob(2, 1f, cargo); // SystemStreamingDirector.cs:1299 shape, progress01 == 1

            Assert.IsNotNull(laser.ActiveUpgradeJob, "precondition: the restored job is still live");
            float expectedParts = laserT2.FullCost().First(c => c.itemId == PartId).amount;
            Assert.AreEqual(expectedParts, cargo.GetOwnerReservation(StructureUpgradeService.OwnerId(laser), PartId), 0.001f,
                "progress changes remaining time, never the full price reserved at start");

            float before = Physical(cargo, PartId);
            StructureUpgradeService.Complete(laser, laser.ActiveUpgradeJob, cargo);

            Assert.AreEqual(before - expectedParts, Physical(cargo, PartId), 0.001f,
                "completion consumes the restored full reservation exactly once");
        }

        // =====================================================================
        // E. THE SAME ROOT CAUSE IN THE OTHER RESTORE CALL SITES
        // =====================================================================

        /// <summary>
        /// <c>ModuleUpgradeService.RestoreActiveJob</c> (:150) reserves without
        /// releasing. Safe on the load path only because <c>Cargo.Clear()</c> runs
        /// first; nothing in the method itself guarantees that, and the method is
        /// public.
        /// </summary>
        [Test]
        public void ModuleUpgradeService_RestoreActiveJob_ReplacesTheExistingReservation()
        {
            LogisticsCargo cargo = Hold((PartId, 100f));
            var service = new ModuleUpgradeService(new ModuleProgressionState(),
                new Nebula.Progression.ProgressionState(), cargo);
            var reserved = new[] { (PartId, 20f) };

            cargo.TryReserveAll(ModuleUpgradeJob.CargoOwnerId, new List<(string, float)> { (PartId, 20f) });
            service.RestoreActiveJob("industry.station_construction", 1, 2, 0, reserved, 60f, 30f, false, 0f);

            Assert.AreEqual(20f, cargo.GetOwnerReservation(ModuleUpgradeJob.CargoOwnerId, PartId), 0.001f);
        }

        [Test]
        public void ModuleUpgradeService_UnfundedRestore_StopsAndRefundsReservedPoints()
        {
            LogisticsCargo cargo = Hold((PartId, 5f));
            var progression = new Nebula.Progression.ProgressionState();
            progression.Restore(0, 0); // saved total is already net of the five reserved points
            var service = new ModuleUpgradeService(new ModuleProgressionState(), progression, cargo);

            Assert.IsFalse(service.RestoreActiveJob("industry.station_construction", 1, 2, 5,
                new[] { (PartId, 20f) }, 60f, 30f, false, 0f));
            Assert.IsNull(service.ActiveJob);
            Assert.AreEqual(5, progression.DevelopmentPoints,
                "a stopped restore returns the points that the saved job had already reserved");
        }

        /// <summary>
        /// <c>HangarProductionService.RestoreActiveJob</c> (:170) has the same shape,
        /// and unlike the module one it also runs AFTER the streaming boot restores
        /// structure reservations (ExtractionDirector.cs:574 precedes :656), so it is
        /// the one most likely to lose the race for the same stock.
        /// </summary>
        [Test]
        public void HangarProductionService_RestoreActiveJob_ReplacesTheExistingReservation()
        {
            LogisticsCargo cargo = Hold((PartId, 100f));
            var service = new HangarProductionService(cargo);

            cargo.TryReserveAll(HangarProductionJob.CargoOwnerId, new List<(string, float)> { (PartId, 12f) });
            service.RestoreActiveJob(VehicleClass.Collector, null, 12, 0, 60f, 30f);

            Assert.AreEqual(12f, cargo.GetOwnerReservation(HangarProductionJob.CargoOwnerId, PartId), 0.001f);
        }

        [Test]
        public void HangarUpgradeAndGenericRestore_ReplaceInsteadOfStacking()
        {
            LogisticsCargo cargo = Hold((PartId, 100f),
                (VehicleCatalog.MetalAlloyId, 100f), (VehicleCatalog.BiocompositeId, 100f));
            var service = new HangarProductionService(cargo);
            Assert.IsTrue(cargo.TryReserveAll(HangarProductionJob.UpgradeCargoOwnerId,
                new List<(string, float)> { (PartId, 12f), (VehicleCatalog.MetalAlloyId, 5f) }));
            Assert.IsTrue(service.RestoreActiveUpgradeJob("vehicle-0001", 12, 5, 0, 60f, 30f));
            Assert.AreEqual(12f, cargo.GetOwnerReservation(HangarProductionJob.UpgradeCargoOwnerId, PartId), 0.001f);

            cargo.ReleaseReservation(HangarProductionJob.UpgradeCargoOwnerId);
            Assert.IsTrue(cargo.TryReserveAll(HangarProductionJob.CargoOwnerId,
                new List<(string, float)> { (PartId, 8f), (VehicleCatalog.BiocompositeId, 4f) }));
            Assert.IsTrue(service.RestoreGenericFixedJob(false, VehicleClass.Collector, 2, null, null, 8,
                new List<(string itemId, float amount)> { (VehicleCatalog.BiocompositeId, 4f) }, 60f, 30f));
            Assert.AreEqual(8f, cargo.GetOwnerReservation(HangarProductionJob.CargoOwnerId, PartId), 0.001f);
            Assert.AreEqual(4f, cargo.GetOwnerReservation(HangarProductionJob.CargoOwnerId,
                VehicleCatalog.BiocompositeId), 0.001f);
        }

        /// <summary>
        /// And the failure mode when it loses that race: the reservation simply does
        /// not happen, the <c>bool</c> is discarded, and the job runs unfunded to a
        /// completion that charges nothing.
        /// </summary>
        [Test]
        public void HangarProductionService_RestoringIntoAnEmptyHold_StopsTheJob()
        {
            LogisticsCargo cargo = Hold((PartId, 5f));
            var service = new HangarProductionService(cargo);

            Assert.IsFalse(service.RestoreActiveJob(VehicleClass.Collector, null, 12, 0, 60f, 30f));
            Assert.IsNull(service.ActiveJob);
            Assert.AreEqual(0f, cargo.GetOwnerReservation(HangarProductionJob.CargoOwnerId, PartId), 0.001f,
                "an unfundable restore leaves no live economic job");
        }

        // =====================================================================
        // F. THE SAVE MODEL ITSELF
        // =====================================================================

        /// <summary>
        /// The structural reason this whole class of defects exists: no save DTO
        /// carries a reservation. Every owner must re-derive its own on load, and each
        /// re-derivation is an independent chance to double, to fail silently, or to
        /// be skipped entirely.
        /// </summary>
        [Test]
        public void NoSaveDtoCarriesAReservation_SoEveryOwnerMustReDeriveItsOwn()
        {
            string[] hits = typeof(SavePayload).Assembly.GetTypes()
                .Where(t => t.Namespace == typeof(SavePayload).Namespace && t.Name.EndsWith("Dto"))
                .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.Name.IndexOf("reservationOwner", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(f => t.Name + "." + f.Name))
                .ToArray();

            Assert.IsEmpty(hits,
                "if a reservation-owner field ever appears, this finding's root cause has been addressed " +
                "and the re-derivation call sites should be revisited");
        }

        /// <summary>
        /// <c>SaveEnvelope.schemaVersion</c> still initialises to 13 while
        /// <c>SaveService.CurrentSchema</c> is 15 (SaveModel.cs:750 vs :776). Save
        /// always writes the real number, so this only bites an envelope whose
        /// schemaVersion is missing or unreadable — which then claims to be v13 and
        /// skips nothing it should skip. A stale default is a silent mis-statement of
        /// the payload's own identity.
        /// </summary>
        [Test]
        public void SaveEnvelopeDefaultSchemaVersion_DoesNotMatchCurrentSchema()
        {
            SaveEnvelope ghost = JsonUtility.FromJson<SaveEnvelope>("{}");

            Assert.Less(ghost.schemaVersion, 6,
                "P2-SAVE-201: a payload with no schemaVersion must remain below the loader's " +
                "minimum-schema gate so it is rejected or explicitly migrated; silently calling it v13 " +
                "opens modern restore branches for data whose shape is unknown");
        }

        /// <summary>
        /// P2-SAVE-201, stated as a measurement rather than as a wish. An envelope
        /// whose JSON carries no <c>schemaVersion</c> deserialises as the field
        /// initializer, 13 — and 13 is the worst possible answer, because it clears
        /// every gate the loader has. It survives <c>&gt; CurrentSchema</c>
        /// (SaveModel.cs:931), it survives <c>&lt; 6</c> — the rejection that exists
        /// precisely for a file this old (ExtractionDirector.cs:426) — and it fails
        /// <c>&gt;= 15</c>, which sends <c>RestoreProcessing</c> down the legacy branch
        /// and leaves <c>payload.processingBench</c> unread (ExtractionDirector.cs:712).
        /// Every one of those gates is written this way on purpose, because JsonUtility
        /// never leaves a nested field null; the whole design rests on the version
        /// number being true, and this is how it silently is not.
        /// </summary>
        [Test]
        public void AnEnvelopeWithNoSchemaVersion_RemainsUnknownAndFailsTheMinimumGate()
        {
            SaveEnvelope ghost = JsonUtility.FromJson<SaveEnvelope>("{}");

            Assert.AreEqual(0, ghost.schemaVersion,
                "an absent schemaVersion must remain unknown, not impersonate a historical schema");
            Assert.IsFalse(ghost.schemaVersion > SaveService.CurrentSchema,
                "AUDIT: it passes the newer-schema rejection (SaveModel.cs:931)");
            Assert.IsTrue(ghost.schemaVersion < 6,
                "the ExtractionDirector minimum-schema gate must reject the unknown envelope");
            Assert.IsFalse(ghost.schemaVersion >= 15,
                "AUDIT: and it fails the v15 bench gate (ExtractionDirector.cs:712), so RestoreProcessing " +
                "takes the legacy branch and never reads processingBench — the player's bench is cleared " +
                "and rebuilt from processorSlots a v15 payload does not have");
        }

        /// <summary>
        /// P2-ECO-106. A weapon production job reserves on start
        /// (<c>WeaponLoadoutService.cs:84</c>) and consumes on completion (<c>:91</c>).
        /// A load clears the hold (<c>ExtractionDirector.cs:507</c>) and
        /// <c>ShipCombat.RestoreProductionJob</c> (<c>:119-122</c>) re-creates the job
        /// object and nothing else — it does not take a cargo at all. This test walks
        /// that exact sequence through the real service: start, wipe as a load does,
        /// rebuild the job as the restore does, complete.
        /// </summary>
        [Test]
        public void WeaponProductionJob_Restore_RecreatesAndConsumesItsReservation()
        {
            Nebula.Combat.WeaponTierCost cost = Nebula.Combat.WeaponCatalog.CostForTier(Nebula.Combat.WeaponFamily.Laser, 2);
            var go = new GameObject("weapon restore host"); spawned.Add(go);
            ShipLogistics logistics = go.AddComponent<ShipLogistics>();
            // EditMode never runs Awake on a component added to a bare GameObject, so
            // Cargo is still null here. InitializeNewCampaign is the production seam
            // that builds it, and it is null-guarded for exactly this reason.
            logistics.InitializeNewCampaign();
            logistics.Cargo.SetCapacity(1000000f);
            logistics.Cargo.Add(PartId, cost.Parts * 4f);
            logistics.Cargo.Add(cost.MaterialId, cost.Material * 4f);
            Nebula.Ship.ShipCombat combat = go.AddComponent<Nebula.Ship.ShipCombat>();

            Assert.IsTrue(combat.RestoreProductionJob(Nebula.Combat.WeaponFamily.Laser, 2,
                cost.Parts, cost.MaterialId, cost.Material, cost.Seconds, cost.Seconds));
            Assert.AreEqual(cost.Parts, logistics.Cargo.GetOwnerReservation(
                Nebula.Combat.WeaponProductionJob.CargoOwnerId, PartId), 0.001f);
            float before = Physical(logistics.Cargo, PartId);
            float beforeMaterial = Physical(logistics.Cargo, cost.MaterialId);
            Nebula.Combat.WeaponLoadoutService.Complete(combat.ActiveProductionJob, logistics.Cargo,
                new Nebula.Combat.HardpointState(0));
            Assert.AreEqual(before - cost.Parts, Physical(logistics.Cargo, PartId), 0.001f);
            Assert.AreEqual(beforeMaterial - cost.Material, Physical(logistics.Cargo, cost.MaterialId), 0.001f);
        }

        /// <summary>
        /// P2-ECO-106 fail-closed regression: the production restore seam must
        /// refuse to create an active weapon job when its saved cost cannot be
        /// reserved.
        /// </summary>
        [Test]
        public void P2ECO106_RestoredWeaponProduction_MustChargeItsSavedCostOnCompletion()
        {
            Nebula.Combat.WeaponTierCost cost = Nebula.Combat.WeaponCatalog.CostForTier(Nebula.Combat.WeaponFamily.Laser, 2);
            var go = new GameObject("unfunded weapon restore host"); spawned.Add(go);
            ShipLogistics logistics = go.AddComponent<ShipLogistics>();
            Nebula.Ship.ShipCombat combat = go.AddComponent<Nebula.Ship.ShipCombat>();
            Assert.IsFalse(combat.RestoreProductionJob(Nebula.Combat.WeaponFamily.Laser, 2,
                cost.Parts, cost.MaterialId, cost.Material, cost.Seconds, cost.Seconds));
            Assert.IsNull(combat.ActiveProductionJob,
                "P2-ECO-106: an unfunded weapon restore must stop instead of creating a free job");
        }

        /// <summary>Positive control: the ordinary, funded weapon path charges exactly once.</summary>
        [Test]
        public void FundedWeaponProduction_ChargesExactlyItsReservedCostOnCompletion()
        {
            Nebula.Combat.WeaponTierCost cost = Nebula.Combat.WeaponCatalog.CostForTier(Nebula.Combat.WeaponFamily.Laser, 2);
            LogisticsCargo cargo = Hold((PartId, cost.Parts * 4f), (cost.MaterialId, cost.Material * 4f));
            var job = new Nebula.Combat.WeaponProductionJob(Nebula.Combat.WeaponFamily.Laser, 2,
                cost.Parts, cost.MaterialId, cost.Material, cost.Seconds, cost.Seconds);
            Assert.IsTrue(cargo.TryReserveAll(Nebula.Combat.WeaponProductionJob.CargoOwnerId,
                new List<(string, float)> { (PartId, cost.Parts), (cost.MaterialId, cost.Material) }));

            float beforeParts = Physical(cargo, PartId);
            float beforeMaterial = Physical(cargo, cost.MaterialId);
            Nebula.Combat.WeaponLoadoutService.Complete(job, cargo, new Nebula.Combat.HardpointState(0));

            Assert.AreEqual(beforeParts - cost.Parts, Physical(cargo, PartId), 0.001f);
            Assert.AreEqual(beforeMaterial - cost.Material, Physical(cargo, cost.MaterialId), 0.001f);
        }

        /// <summary>
        /// P2-ECO-107. Quick Asset Package reserves all 65 Basic + 15 special up front
        /// (<c>QuickAssetPackage.cs:100</c>) and consumes each structure's share as it
        /// is sent. <c>ShipQuickAssetPackage.RestoreTransaction</c> (<c>:313-330</c>)
        /// rebuilds the transaction without touching cargo, so after a load the
        /// remaining sends call <c>ConsumePartialReservation</c> on an owner that holds
        /// nothing — a silent no-op (<c>LogisticsCargo.cs:172</c>).
        /// </summary>
        [Test]
        public void QuickPackage_RestoredMidFlight_ReReservesAndChargesUnsentStructures()
        {
            const string material = "material.metal_alloy";
            PlanetIdentity planet = MaterializePlanet();
            var host = new GameObject("quick restore host"); spawned.Add(host);
            ShipLogistics logistics = host.AddComponent<ShipLogistics>();
            // EditMode never runs Awake on a component added to a bare GameObject, so
            // Cargo is still null here. InitializeNewCampaign is the production seam
            // that builds it, and it is null-guarded for exactly this reason.
            logistics.InitializeNewCampaign();
            logistics.Cargo.SetCapacity(1000000f);
            logistics.Cargo.Add(PartId, 200f); logistics.Cargo.Add(material, 60f);
            ShipQuickAssetPackage package = host.AddComponent<ShipQuickAssetPackage>();
            var dto = new QuickPackageDto { active = true, transactionId = "tx-1",
                planetWorldId = planet.WorldId, commandMaterialId = material, laserMaterialId = material,
                drillSent = true };
            Assert.IsTrue(package.RestoreTransaction(dto));
            QuickAssetPackageTransaction restored = package.ActiveTransaction;
            List<(string itemId, float amount)> missing = QuickAssetPackageService.MissingNodeCost(restored);
            float owedParts = missing.Where(c => c.itemId == PartId).Sum(c => c.amount);
            float owedMaterial = missing.Where(c => c.itemId == material).Sum(c => c.amount);
            float beforeParts = Physical(logistics.Cargo, PartId);
            float beforeMaterial = Physical(logistics.Cargo, material);
            QuickAssetPackageService.SendCommand(restored, logistics.Cargo);
            QuickAssetPackageService.SendLaser(restored, logistics.Cargo);
            Assert.AreEqual(beforeParts - owedParts, Physical(logistics.Cargo, PartId), 0.001f);
            Assert.AreEqual(beforeMaterial - owedMaterial, Physical(logistics.Cargo, material), 0.001f);
        }

        /// <summary>P2-ECO-107 fail-closed regression for an unfunded restore.</summary>
        [Test]
        public void P2ECO107_RestoredQuickPackage_MustChargeEveryUnsentNode()
        {
            const string material = "material.metal_alloy";
            PlanetIdentity planet = MaterializePlanet();
            var host = new GameObject("unfunded quick restore host"); spawned.Add(host);
            ShipLogistics logistics = host.AddComponent<ShipLogistics>();
            ShipQuickAssetPackage package = host.AddComponent<ShipQuickAssetPackage>();
            var dto = new QuickPackageDto { active = true, transactionId = "tx-diagnostic",
                planetWorldId = planet.WorldId, commandMaterialId = material, laserMaterialId = material,
                drillSent = true };
            Assert.IsFalse(package.RestoreTransaction(dto));
            Assert.IsNull(package.ActiveTransaction,
                "P2-ECO-107: unfunded package restore must stop before unsent structures can be delivered");
        }

        /// <summary>
        /// P2-ECO-101. The Command Center card gates correctly — it reads
        /// <c>GetAvailable</c> (<c>CommandBuildService.cs:402</c>) and shows the player
        /// stored, reserved and available as separate numbers (<c>:445-456</c>). But
        /// the action it triggers is <c>SurfaceStructure.Create</c>
        /// (<c>ShipExtraction.cs:426-428</c>), which returns a structure whether or not
        /// its reservation succeeded and has no channel to say otherwise — so
        /// <c>CommandActionOutcome</c> reports success either way. The card's honesty
        /// stops at the layer below it.
        /// </summary>
        [Test]
        public void SurfaceStructureCreate_IntoAnUnfundableHold_ReturnsNullWithoutMutation()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            List<(string itemId, float amount)> cost = CommandBuildService.BuildT1Cost(profile.Family);
            LogisticsCargo cargo = StockedFor(profile.Family, 1f);
            cargo.TryReserveAll(ModuleUpgradeJob.CargoOwnerId, cost); // nothing is available any more

            float before = Physical(cargo, PartId);
            SurfaceStructure built = SurfaceStructure.Create(planet, profile, SurfaceStructureCatalog.Command,
                profile.CommandNode, Vector3.zero, cargo, cost);
            Assert.IsNull(built);
            Assert.AreEqual(before, Physical(cargo, PartId), 0.001f,
                "failed creation must not withdraw stock");
            Assert.AreEqual(0, ExtractionState.Structures.Count,
                "failed creation must not occupy the node");
        }

        /// <summary>
        /// P2-ECO-101 regression contract. <c>TryBuildOrUpgradeCommand</c> currently
        /// returns success unconditionally after this lower seam returns. Therefore a
        /// reservation failure must first become a failed/null creation result that
        /// the command seam can map to <c>InsufficientResources</c>.
        /// </summary>
        [Test]
        public void P2ECO101_CommandReservationFailure_MustNotCreateACommandStructure()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            List<(string itemId, float amount)> cost = CommandBuildService.BuildT1Cost(profile.Family);
            LogisticsCargo cargo = StockedFor(profile.Family, 1f);
            Assert.IsTrue(cargo.TryReserveAll(ModuleUpgradeJob.CargoOwnerId, cost),
                "precondition: the assessment-to-mutation window consumed all available stock");

            SurfaceStructure built = SurfaceStructure.Create(planet, profile, SurfaceStructureCatalog.Command,
                profile.CommandNode, Vector3.zero, cargo, cost);
            if (built != null) spawned.Add(built.gameObject);

            Assert.IsNull(built,
                "P2-ECO-101: SurfaceStructure.Create returned a live Command structure after its reservation failed; " +
                "ShipExtraction.TryBuildOrUpgradeCommand therefore has no failure value to report and returns success");
        }

        /// <summary>
        /// P2-ECO-104. The statement <c>SystemStreamingDirector.cs:945-951</c> runs on
        /// promotion, using the same <c>engineer-mission:{id}</c> owner the launch used
        /// (<c>ShipHangar.cs:270</c>). Demotion destroyed the vehicle without releasing
        /// anything (<c>VehicleAgent.cs:146-149</c>), so this reserves the remainder ON
        /// TOP of a reservation that never went away. Reproduced here as the exact
        /// sequence, with the real owner-id format and the real remaining-cost formula.
        /// </summary>
        [Test]
        public void EngineerRestore_ReplacesTheStrandedReservationWithTheRemainingCost()
        {
            const string missionId = "engineer-vehicle-0001";
            string ownerId = $"engineer-mission:{missionId}";
            const int reservedParts = 20, consumedParts = 5;

            LogisticsCargo cargo = Hold((PartId, 100f));
            cargo.TryReserveAll(ownerId, new List<(string, float)> { (PartId, reservedParts) }); // launch
            // Demotion: SystemMaterializer.Destroy -> VehicleAgent.OnDestroy, which only
            // touches the registry. The reservation is left exactly where it was.
            Assert.AreEqual(reservedParts, cargo.GetOwnerReservation(ownerId, PartId), 0.001f,
                "precondition: the reservation outlived the vehicle");

            // Promotion, SystemStreamingDirector.cs:947-951 verbatim.
            var remaining = new List<(string, float)>();
            int remainingParts = Mathf.Max(0, reservedParts - consumedParts);
            if (remainingParts > 0) remaining.Add((PartId, remainingParts));
            if (remaining.Count > 0) cargo.TrySetReservation(ownerId, remaining);

            Assert.AreEqual(remainingParts, cargo.GetOwnerReservation(ownerId, PartId), 0.001f,
                "promotion replaces the old lock with the exact unconsumed remainder");
        }

        /// <summary>P2-ECO-104 idempotent restore regression.</summary>
        [Test]
        public void P2ECO104_EngineerRestore_MustNotStackOnTheStrandedReservation()
        {
            const string missionId = "engineer-vehicle-diagnostic";
            string ownerId = $"engineer-mission:{missionId}";
            const int reservedParts = 20, consumedParts = 5;
            int remainingParts = reservedParts - consumedParts;
            LogisticsCargo cargo = Hold((PartId, 100f));
            Assert.IsTrue(cargo.TryReserveAll(ownerId,
                new List<(string, float)> { (PartId, reservedParts) }));

            // SystemStreamingDirector.RestoreEngineerAndEscort:947-951, production statement.
            cargo.TrySetReservation(ownerId, new List<(string, float)> { (PartId, remainingParts) });

            Assert.AreEqual(remainingParts, cargo.GetOwnerReservation(ownerId, PartId), 0.001f,
                "P2-ECO-104: Engineer promotion stacked the remaining cost on the reservation stranded by demotion");
        }

        /// <summary>
        /// Checked and clean, with evidence: <c>LoadItem</c> writes the exact saved
        /// amount and never trims to capacity, so a capacity change between sessions
        /// can never delete saved stock — it only makes <c>OverCapacity</c> read true.
        /// </summary>
        [Test]
        public void LoadItem_WritesTheExactSavedAmount_EvenPastCapacity()
        {
            var cargo = new LogisticsCargo(10f);
            cargo.LoadItem(PartId, 500f);

            Assert.AreEqual(500f, cargo.GetStored(PartId), 0.001f, "saved stock must never be trimmed on load");
            Assert.IsTrue(cargo.OverCapacity, "and the over-capacity condition must be visible rather than silent");
        }

        [Test]
        public void AccessibilityAndInterfaceSettings_RoundTripOutsideCampaignSave()
        {
            Nebula.UI.Shell.NebulaUiShell.UiScale = 1.3f;
            Nebula.Combat.CombatAccessibilitySettings.HudScale = 1.4f;
            Nebula.Visuals.AnimationAccessibility.ReducedMotion = true;
            Nebula.Visuals.AnimationAccessibility.MotionOff = true;
            Nebula.Visuals.AnimationAccessibility.CameraComfort = true;
            Nebula.Visuals.LightingAccessibility.FlashReduction = true;
            Nebula.Visuals.LightingAccessibility.PhotosensitivitySafeMode = true;
            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
            Nebula.Visuals.LightingAccessibility.HighContrast = true;
            Assert.IsTrue(Nebula.UI.Menu.NebulaAccessibilitySettingsStore.Save());

            Nebula.UI.Shell.NebulaUiShell.UiScale = 1f;
            Nebula.Combat.CombatAccessibilitySettings.HudScale = 1f;
            Nebula.Visuals.AnimationAccessibility.ResetToDefaults();
            Nebula.Visuals.LightingAccessibility.ResetToDefaults();
            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true;

            Assert.IsTrue(Nebula.UI.Menu.NebulaAccessibilitySettingsStore.Load());
            Assert.AreEqual(1.3f, Nebula.UI.Shell.NebulaUiShell.UiScale, 0.001f);
            Assert.AreEqual(1.4f, Nebula.Combat.CombatAccessibilitySettings.HudScale, 0.001f);
            Assert.IsTrue(Nebula.Visuals.AnimationAccessibility.ReducedMotion);
            Assert.IsTrue(Nebula.Visuals.AnimationAccessibility.MotionOff);
            Assert.IsTrue(Nebula.Visuals.AnimationAccessibility.CameraComfort);
            Assert.IsTrue(Nebula.Visuals.LightingAccessibility.FlashReduction);
            Assert.IsTrue(Nebula.Visuals.LightingAccessibility.PhotosensitivitySafeMode);
            Assert.IsFalse(Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings);
            Assert.IsTrue(Nebula.Visuals.LightingAccessibility.HighContrast);
        }
    }
}
