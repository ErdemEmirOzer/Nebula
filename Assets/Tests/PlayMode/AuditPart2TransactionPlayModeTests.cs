using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENERAL REVIEW PART 2 / AGENT 1 — transaction regressions that require the
    /// engine's real Update and deferred-destruction semantics.
    /// <para>
    /// EditMode can only execute the statement a completion WOULD run; here the real
    /// <c>SurfaceStructure.Update</c> drives the build to Active on its own, so the
    /// "free structure" claim is measured end to end rather than reconstructed.
    /// </para>
    /// <para>
    /// The second reason this fixture exists is the destruction-semantics split.
    /// <c>SurfaceStructure.DestroyNow</c> resolves to <c>DestroyImmediate</c> outside
    /// play mode and to the deferred <c>Destroy</c> inside it. Part 1 measured a
    /// registry leak under the first and could not tell whether it was the product or
    /// the test runner. The evacuation test below settles that by measuring the
    /// registry in the same frame and again after one frame boundary.
    /// </para>
    /// <para>
    /// No scene is booted: these tests build exactly the objects the streaming path
    /// builds and nothing else, so the fixture stays cheap enough to share a Unity
    /// process with the rest of the suite.
    /// </para>
    /// </summary>
    public class AuditPart2TransactionPlayModeTests
    {
        private const ulong Seed = 20260812UL;
        private const string PartId = ProcessingCatalog.BasicPartId;

        private SystemManifest system;
        private SystemStreamingDirector savedStreaming;
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // The presenters Setup attaches expect a booted animation/lighting world.
            // This fixture deliberately does not boot one — it is measuring resource
            // arithmetic, not presentation — so presenter complaints must not be read
            // as the subject failing.
            LogAssert.ignoreFailingMessages = true;

            savedStreaming = StarSystemRegistry.Streaming;
            StarSystemRegistry.Streaming = null;
            ExtractionState.Reset((int)Seed);
            StarSystemRegistry.Initialize(Seed);

            SectorManifest sector = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
            system = sector.Systems.First(s => s.Planets.Count > 0);
            StarSystemRegistry.SetStartSystem(system.Sector, system.SystemId);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            ExtractionState.DestroyAllDrills();
            ExtractionState.DestroyAllStructures();
            StarSystemRegistry.Streaming = savedStreaming;
            StarSystemRegistry.Clear();
            ExtractionState.Reset((int)Seed);
            Nebula.Progression.ProgressionService.Reset();
            LogAssert.ignoreFailingMessages = false;
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private PlanetIdentity MaterializePlanet()
        {
            var go = new GameObject("AuditPart2 PlayMode Planet");
            spawned.Add(go);
            CelestialBody body = go.AddComponent<CelestialBody>();
            body.Configure(CelestialBodyType.Planet, 60f, 1f, false);
            PlanetIdentity planet = go.AddComponent<PlanetIdentity>();
            planet.Initialize(system.Planets[0].PlanetId, body, true);
            return planet;
        }

        private static LogisticsCargo StockedFor(ResourceFamily family, float multiplier)
        {
            var cargo = new LogisticsCargo(1000000f);
            foreach (var (itemId, amount) in CommandBuildService.BuildT1Cost(family))
                cargo.Add(itemId, amount * multiplier);
            return cargo;
        }

        private static float ShipExtractionConst(string name)
        {
            FieldInfo field = typeof(ShipExtraction).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"ShipExtraction.{name} must exist for this audit to be measuring the real path");
            return System.Convert.ToSingle(field.GetRawConstantValue());
        }

        /// <summary>A structure restored mid-build, exactly as SystemStreamingDirector.RestoreDeltaInto restores one.</summary>
        private SurfaceStructure RestoreConstructingCommand(PlanetIdentity planet, PlanetProfile profile,
            LogisticsCargo cargo, List<(string itemId, float amount)> buildCost)
        {
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
            durability.RestoreFromSave(def.ShieldMax, def.ArmorMax, def.HullMax, 0f);
            var runtime = new StructureRuntime(def, DrillLifecycle.Constructing, 0f, BuildSeconds, durability, 1);
            SurfaceStructure s = SurfaceStructure.Restore(planet, profile, def, profile.CommandNode, runtime, cargo, buildCost);
            if (s != null) spawned.Add(s.gameObject);
            return s;
        }

        /// <summary>
        /// The build time a restored structure in this fixture is given.
        /// <para>
        /// Deliberately far below anything a frame budget could miss. The second run of
        /// this fixture measured 600 frames buying only 0.0322s — about 54µs per frame,
        /// because a batch-mode frame renders nothing. A frame count is not a clock, and
        /// the funded control only passed at 0.05s because it runs first, alphabetically,
        /// while the engine is still warming up and frames are still expensive. A test
        /// whose result depends on its position in the run order is the worst kind of
        /// test, so the build is shortened until that dependence cannot exist rather
        /// than the ceiling being raised until it happens not to bite.
        /// </para>
        /// </summary>
        private const float BuildSeconds = 0.005f;

        /// <summary>
        /// Simulated seconds the last <see cref="RunUntilActive"/> actually delivered.
        /// <para>
        /// The first run of this fixture failed with "Expected Active, But was
        /// Constructing" under a FRAME-counted loop while the funded control — which
        /// runs first, alphabetically, while the engine is still warming up — reached
        /// Active. A frame count is not a clock: in batch mode later frames are far
        /// cheaper, so the same 240 frames buy far less simulated time. This loop is
        /// budgeted in <c>Time.deltaTime</c> instead and records what it spent, so a
        /// failure message states how many seconds of build time really elapsed and
        /// the XML alone distinguishes "starved of frames" from "genuinely never
        /// finishes" — which would contradict the EditMode measurement and matter far
        /// more.
        /// </para>
        /// </summary>
        private static float lastRunSimulatedSeconds;

        private static IEnumerator RunUntilActive(SurfaceStructure structure,
            float budgetSeconds = BuildSeconds * 40f, int frameCeiling = 3000)
        {
            lastRunSimulatedSeconds = 0f;
            for (int i = 0; i < frameCeiling; i++)
            {
                if (structure == null || structure.Runtime.State == DrillLifecycle.Active) break;
                if (lastRunSimulatedSeconds >= budgetSeconds) break;
                yield return null;
                lastRunSimulatedSeconds += Time.deltaTime;
            }
        }

        private static string RunBudgetReport() =>
            $"[{lastRunSimulatedSeconds:0.####}s of simulated time elapsed against a {BuildSeconds:0.####}s build]";

        // =====================================================================
        // 1. THE FREE STRUCTURE, END TO END
        // =====================================================================

        /// <summary>
        /// POSITIVE CONTROL. A funded restore reserves, runs, and its own
        /// <c>Update</c> charges the catalog cost exactly once when it reaches Active.
        /// Without this passing, the negative case below would prove nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator AFundedRestoredStructure_ReachesActive_AndChargesExactlyItsCatalogCost()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            List<(string itemId, float amount)> cost = CommandBuildService.BuildT1Cost(profile.Family);
            LogisticsCargo cargo = StockedFor(profile.Family, 3f);
            float partsCost = cost.First(c => c.itemId == PartId).amount;
            float before = cargo.GetStored(PartId);

            SurfaceStructure structure = RestoreConstructingCommand(planet, profile, cargo, cost);
            Assert.AreEqual(partsCost, cargo.GetOwnerReservation(structure.BuildOwnerId, PartId), 0.001f,
                "precondition: the restore reserved the full cost");

            yield return RunUntilActive(structure);

            Assert.AreEqual(DrillLifecycle.Active, structure.Runtime.State,
                $"the build must have finished on its own {RunBudgetReport()}");
            Assert.AreEqual(before - partsCost, cargo.GetStored(PartId), 0.001f,
                "a funded build charges its catalog cost exactly once");
        }

        /// <summary>
        /// THE FINDING. Same restore into a hold whose stock is already reserved by
        /// another owner — the ordinary state after a load, where the module job, the
        /// hangar job and the boot system's own structures re-reserve before this
        /// system is ever promoted. <c>Setup:146</c> discards the failed
        /// <c>TryReserveAll</c>, the build runs anyway, and the completion consume at
        /// <c>:204</c> finds an empty owner and charges nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator AnUnfundedRestoredStructure_IsRejectedBeforeItCanAdvance()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            List<(string itemId, float amount)> cost = CommandBuildService.BuildT1Cost(profile.Family);
            LogisticsCargo cargo = StockedFor(profile.Family, 1f);
            cargo.TryReserveAll(HangarProductionJob.CargoOwnerId, cost); // someone else got there first
            float before = cargo.GetStored(PartId);

            SurfaceStructure structure = RestoreConstructingCommand(planet, profile, cargo, cost);
            Assert.IsNull(structure, "an unfunded restore must not materialize a live economic job");
            Assert.AreEqual(before, cargo.GetStored(PartId), 0.001f,
                "a rejected restore must not mutate physical stock");
            yield return null;
        }

        /// <summary>
        /// P2-ECO-102 regression contract; the funded test above remains the
        /// ordinary-package positive control.
        /// </summary>
        [UnityTest]
        public IEnumerator P2ECO102_UnfundedRestoredStructure_MustNotCompleteForFree()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            List<(string itemId, float amount)> cost = CommandBuildService.BuildT1Cost(profile.Family);
            LogisticsCargo cargo = StockedFor(profile.Family, 1f);
            Assert.IsTrue(cargo.TryReserveAll(HangarProductionJob.CargoOwnerId, cost));
            float before = cargo.GetStored(PartId);

            SurfaceStructure structure = RestoreConstructingCommand(planet, profile, cargo, cost);
            Assert.IsNull(structure,
                "P2-ECO-102: reservation failure must abort restore before a runtime can reach Active");
            Assert.AreEqual(before, cargo.GetStored(PartId), 0.001f,
                "a stopped unfunded build must not mutate physical stock");
            yield return null;
        }

        // =====================================================================
        // 2. THE DRILL MINT, WITH REAL DESTRUCTION SEMANTICS
        // =====================================================================

        /// <summary>
        /// P1-ECO-001's loop, run with the engine's real deferred <c>Destroy</c>, and
        /// accounted for exactly rather than guessed at.
        /// <para>
        /// The first run of this fixture expected 3 free cycles and measured 2. That
        /// number was not noise: <b>the exploit self-limits.</b> Every mint raises
        /// <c>GetAvailable</c>, and once available has climbed back above the drill
        /// cost the charge at <c>ShipExtraction.cs:348</c> starts SUCCEEDING, so later
        /// cycles are ordinary paid builds. The defect is real and creates resources
        /// from nothing, but it is bounded by how far the hold is over-reserved — which
        /// is exactly the kind of thing Part 3 needs to know before it writes a repro.
        /// </para>
        /// <para>
        /// So this test asserts a LEDGER IDENTITY built from what each cycle actually
        /// did, never a typed-in total: every part in the hold at the end is accounted
        /// for by the refunds paid out minus the costs actually taken.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AReservedHold_CannotCreateARefundEntitlementForASecondDrill()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            float cost = ShipExtractionConst("SecondDrillPartsCost");
            var cargo = new LogisticsCargo(1000000f);
            cargo.Add(PartId, cost);
            cargo.TryReserveAll(Nebula.Modules.ModuleUpgradeJob.CargoOwnerId,
                new List<(string, float)> { (PartId, cost) });

            float before = cargo.GetStored(PartId);
            Assert.IsFalse(ShipExtraction.TryPaySecondDrillCost(cargo),
                "reserved stock is not available to create a paid/refundable drill");
            Assert.AreEqual(before, cargo.GetStored(PartId), 0.001f);
            Assert.AreEqual(0, ExtractionState.Drills.Count,
                "the failed payment seam creates no drill and therefore no refund entitlement");
            yield return null;
        }

        // =====================================================================
        // 3. P1-ECO-009 — SETTLING THE REGISTRY QUESTION
        // =====================================================================

        /// <summary>
        /// The differential Part 1 asked for. In play mode <c>DestroyNow</c> is the
        /// deferred <c>Destroy</c>, so the object legitimately survives the rest of the
        /// frame and <c>OnDestroy</c> — and with it
        /// <c>ExtractionState.UnregisterStructure</c> — only runs at the frame
        /// boundary. Measuring both sides tells Part 3 whether the registry entry Part
        /// 1 saw is a real leak or the shape of destruction under a test runner.
        /// The refund reading is taken in the same test because it is the same
        /// evacuation and cannot disagree with itself.
        /// </summary>
        [UnityTest]
        public IEnumerator Evacuation_PreservesAPaidDisabledStructure_AndItsRegistryEntry()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            var cargo = new LogisticsCargo(1000000f);
            cargo.Add(PartId, 100f);

            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
            durability.RestoreFromSave(0f, 0f, 0f, 0f); // hull 0 -> Disabled, and Tick keeps it there
            var runtime = new StructureRuntime(def, DrillLifecycle.Disabled, 0f, 0f, durability, 1);
            SurfaceStructure disabled = SurfaceStructure.Restore(planet, profile, def, profile.CommandNode,
                runtime, cargo, new List<(string itemId, float amount)>());
            spawned.Add(disabled.gameObject);

            Assert.AreEqual(1, ExtractionState.Structures.Count, "precondition: one structure registered");
            Assert.IsFalse(disabled.Runtime.CanCancelForRefund,
                "precondition: Disabled is outside the refundable window, which is the point");

            float before = cargo.GetStored(PartId);
            PlanetEvacuationService.ResolveActiveBuilds(planet.WorldId, cargo);

            Assert.AreEqual(before, cargo.GetStored(PartId), 0.001f,
                "preserving a paid disabled structure neither charges nor refunds cargo");

            yield return null; // the deferred Destroy and its OnDestroy land here

            Assert.AreEqual(1, ExtractionState.Structures.Count,
                "evacuation must preserve the disabled structure and its canonical registry entry");
            Assert.IsNotNull(disabled);
        }
    }
}
