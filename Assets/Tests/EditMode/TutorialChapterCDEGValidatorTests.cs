using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Tutorial;
using Nebula.Tutorial.Steps;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Chapters C, D, E and G -- every one of the sixty-eight steps, three ways.
    /// <para>
    /// The three tables below are the whole contract of a tutorial step, asserted
    /// against the real production seams rather than against the tutorial's own
    /// bookkeeping:
    /// </para>
    /// <list type="number">
    /// <item><b>Prepare never completes a step.</b> A step that prepares its way
    /// into its own success condition completes itself, and the player is told
    /// they did something they did not do.</item>
    /// <item><b>A wrong or refused action never completes a step.</b> Each case
    /// drives a real, plausible, <i>different</i> action -- a duplicated route, a
    /// cancelled upgrade, a resource added without being withdrawn -- and
    /// asserts the validator stays silent. Green without this is green without
    /// coverage.</item>
    /// <item><b>The real command seam completes it.</b> Time is advanced through
    /// the production tick seams (<c>DrillRuntime.Tick</c>,
    /// <c>DrillUpgradeJob.Tick</c>, <c>ModuleUpgradeService.Tick</c>,
    /// <c>StructureUpgradeJob.Tick</c>, <c>LogisticsCenterRuntime.Tick</c>), never
    /// by waiting, and never by changing a balance constant.</item>
    /// </list>
    /// <para>
    /// Three steps are covered positively in the PlayMode fixture instead,
    /// because their real seam only exists inside a running frame: E-12
    /// (ShipModuleVisuals' tier hook), G-16 (the reserve's proximity gate) and
    /// G-12 (a cancel that ends in <c>Object.Destroy</c>). All three still
    /// appear in tables 1 and 2 here.
    /// </para>
    /// </summary>
    public class TutorialChapterCDEGValidatorTests
    {
        private const int Seed = 12345;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<TutorialStepScope> openScopes = new List<TutorialStepScope>();
        private static Dictionary<string, TutorialStepDefinition> definitions;

        // ---- fixture lifetime -------------------------------------------------

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Resolved once for the whole fixture: a sector manifest is pure CPU,
            // but regenerating it per test case would pay for it two hundred times.
            StarSystemRegistry.Initialize(unchecked((ulong)Seed));
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));

            definitions = new Dictionary<string, TutorialStepDefinition>(StringComparer.Ordinal);
            Collect(ChapterCSteps.Build());
            Collect(ChapterDSteps.Build());
            Collect(ChapterESteps.Build());
            Collect(ChapterGSteps.Build());
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => StarSystemRegistry.Clear();

        private static void Collect(IEnumerable<TutorialStepDefinition> steps)
        {
            foreach (TutorialStepDefinition step in steps) definitions[step.StepId] = step;
        }

        [SetUp]
        public void SetUp()
        {
            ExtractionState.Reset(Seed);
            LogisticsState.Reset();
            ProgressionService.Reset();
            TutorialSite.PendingResupply = null;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = openScopes.Count - 1; i >= 0; i--) openScopes[i].Dispose();
            openScopes.Clear();
            for (int i = spawned.Count - 1; i >= 0; i--) TutorialSite.Kill(spawned[i]);
            spawned.Clear();

            ExtractionState.Reset(Seed);
            LogisticsState.Reset();
            ProgressionService.Reset();
            TutorialSite.PendingResupply = null;
        }

        // ---- the three tables -------------------------------------------------

        /// <summary>
        /// How many times a prepared step is polled with nothing happening between
        /// polls.
        /// <para>
        /// One evaluation immediately after Prepare is too weak. A validator that
        /// latches across polls, arms itself on the first one, or reads state its
        /// own objectives callback wrote can be false on poll one and true on poll
        /// two with the player doing nothing. There is no frame loop in EditMode, so
        /// this is what "several ticks" means here; the real-frame version of the
        /// same sweep runs over the whole catalog in <c>TutorialLifecycleTests</c>.
        /// </para>
        /// </summary>
        private const int IdlePolls = 8;

        [Test, TestCaseSource(nameof(EveryStepId))]
        public void NothingHappening_NeverCompletesTheStep(string stepId)
        {
            using (Run run = Start(stepId))
            {
                int completedOn = Idle(run);
                Assert.AreEqual(-1, completedOn,
                    $"{stepId}: completed on poll {completedOn + 1} with the player doing nothing at all. " +
                    "Either Prepare satisfied the step's own success condition, or the validator becomes " +
                    "true on its own a poll or two later -- both credit the player with an action they " +
                    "never performed.");
            }
        }

        [Test, TestCaseSource(nameof(EveryStepId))]
        public void AWrongOrRefusedAction_NeverCompletesTheStep(string stepId)
        {
            using (Run run = Start(stepId))
            {
                Plan(stepId).Wrong(run);
                run.Evaluate();
                Assert.IsFalse(run.Report.IsComplete,
                    $"{stepId}: an action the game refuses (or a different action entirely) was accepted as success.");

                // And it must still be refused a few polls later: a wrong action
                // that is turned down on the poll it happens and accepted on the
                // next one is still a wrong action being accepted.
                int completedOn = Idle(run);
                Assert.AreEqual(-1, completedOn,
                    $"{stepId}: the wrong action was accepted {completedOn + 1} poll(s) after it happened.");
            }
        }

        [Test, TestCaseSource(nameof(EditModeCompletableStepIds))]
        public void TheRealCommandSeam_CompletesTheStep_AndNothingBeforeItDoes(string stepId)
        {
            using (Run run = Start(stepId))
            {
                // The whole sequence in one run: idled first, so the completion
                // below can only have been caused by the command seam.
                int completedOn = Idle(run);
                Assert.AreEqual(-1, completedOn,
                    $"{stepId}: completed on poll {completedOn + 1}, before its command seam was ever driven.");

                Plan(stepId).Real(run);
                run.Evaluate();
                Assert.IsTrue(run.Report.IsComplete,
                    $"{stepId}: the real command seam ran and the validator did not see it.");
            }
        }

        /// <summary>
        /// Polls a prepared step <see cref="IdlePolls"/> times with nothing
        /// happening in between. Returns the zero-based poll it completed on, or -1
        /// when it stayed honest throughout.
        /// </summary>
        private static int Idle(Run run)
        {
            for (int poll = 0; poll < IdlePolls; poll++)
            {
                run.Evaluate();
                if (run.Report.IsComplete) return poll;
            }
            return -1;
        }

        [Test]
        public void EveryStepInTheseFourChapters_HasAPlan()
        {
            foreach (string id in EveryStepId())
                Assert.IsTrue(Plans.ContainsKey(id), $"{id} has no positive/negative plan, so it is untested.");
            Assert.AreEqual(68, Plans.Count, "chapters C+D+E+G are 17+16+16+19 = 68 steps.");
        }

        [Test]
        public void EveryStepId_IsUniqueAndCarriesTurkishText()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in EveryStepId())
            {
                Assert.IsTrue(seen.Add(id), $"duplicate step id '{id}'");
                TutorialStepDefinition step = definitions[id];
                Assert.IsNotEmpty(step.TitleTr, id);
                Assert.IsNotEmpty(step.InstructionTr, id);
                Assert.IsNotEmpty(step.SummaryTr, id);
            }
        }

        // ---- run harness ------------------------------------------------------

        private static IEnumerable<string> EveryStepId()
        {
            foreach (string id in Plans.Keys) yield return id;
        }

        /// <summary>
        /// The three steps the PlayMode fixture owns positively: E-12's tier hook
        /// is raised from <c>ShipModuleVisuals.Update</c>, G-16's transfer is gated
        /// on a proximity flag only <c>Update</c> sets, and G-12's real seam ends in
        /// <c>Object.Destroy</c>, which is a play-mode-only call.
        /// </summary>
        private static IEnumerable<string> EditModeCompletableStepIds()
        {
            foreach (string id in Plans.Keys)
                if (id != "E-12" && id != "G-16" && id != "G-12") yield return id;
        }

        private Run Start(string stepId)
        {
            Assert.IsTrue(definitions.ContainsKey(stepId), $"no step definition for '{stepId}'");
            TutorialStepDefinition definition = definitions[stepId];
            TutorialStepScope scope = TutorialStepScope.Create(stepId, null);
            openScopes.Add(scope);

            var world = new TutorialWorldFactory(() => scope, null);
            var context = new TutorialStepContext(null, scope, TutorialRunMode.Standalone, world);
            ITutorialStepRuntime runtime = definition.CreateRuntime();
            runtime.Prepare(context);

            var report = new TutorialStepReport();
            report.Reset(definition.ChecklistTr.Count);
            return new Run(this, definition, runtime, scope, report);
        }

        /// <summary>One prepared step, plus the real state a driver needs to reach.</summary>
        private sealed class Run : IDisposable
        {
            private readonly TutorialChapterCDEGValidatorTests owner;
            public readonly TutorialStepDefinition Definition;
            public readonly ITutorialStepRuntime Runtime;
            public readonly TutorialStepScope Scope;
            public readonly TutorialStepReport Report;

            public Run(TutorialChapterCDEGValidatorTests owner, TutorialStepDefinition definition,
                ITutorialStepRuntime runtime, TutorialStepScope scope, TutorialStepReport report)
            {
                this.owner = owner;
                Definition = definition; Runtime = runtime; Scope = scope; Report = report;
            }

            public void Evaluate() => Runtime.Evaluate(Report);

            public T Own<T>(T component) where T : Component
            {
                if (component != null) owner.spawned.Add(component.gameObject);
                return component;
            }

            public GameObject OwnObject(GameObject go)
            {
                if (go != null) owner.spawned.Add(go);
                return go;
            }

            // --- the ship rig the step's own site built ---
            public ShipController Ship => Scope.Root.GetComponentInChildren<ShipController>(true);
            public ShipLogistics Logistics => Scope.Root.GetComponentInChildren<ShipLogistics>(true);
            public LogisticsCargo Cargo => Logistics.Cargo;
            public ShipProcessing Processing => Scope.Root.GetComponentInChildren<ShipProcessing>(true);
            public ShipExtraction Extraction => Scope.Root.GetComponentInChildren<ShipExtraction>(true);
            public ShipModules ModuleHost => Scope.Root.GetComponentInChildren<ShipModules>(true);
            public ModuleProgressionState Tiers => ModuleHost.Modules;
            public ModuleUpgradeService Upgrades => ModuleHost.Upgrades;
            public ProcessingBenchRuntime Bench => Processing.Bench;

            // --- the world the step's own site registered ---
            public PlanetIdentity Planet
            {
                get
                {
                    foreach (PlanetIdentity planet in ExtractionState.AllPlanets)
                        if (planet != null && planet.Body != null) return planet;
                    return null;
                }
            }

            public PlanetProfile Profile => ExtractionState.GetProfile(Planet);
            public string WorldId => Planet.WorldId;
            public string SystemId => WorldIdCodec.SystemIdFromPlanetWorldId(WorldId);
            public SystemManifest Manifest => StarSystemRegistry.Manifests.ResolveSystem(SystemId);
            public float Radius => Planet.Body.Radius;

            public DrillStructure Drill
            {
                get
                {
                    foreach (DrillStructure drill in ExtractionState.Drills)
                        if (drill != null && drill.WorldId == WorldId) return drill;
                    return null;
                }
            }

            public SurfaceStructure Structure(SurfaceStructureType type) =>
                ExtractionState.FindStructure(WorldId, type);

            public LogisticsCenter Center => LogisticsState.FindCenter(SystemId);

            public string RawId => ExtractionCatalog.ResourceIds[(int)Profile.Family];

            public void PlaceShip(float altitude) =>
                Ship.transform.position = Planet.Body.transform.position + Vector3.up * (Radius + altitude);

            /// <summary>A real structure, through the real atomic-reservation build path.</summary>
            public SurfaceStructure BuildStructure(SurfaceStructureDef def, SurfaceNode node) =>
                Own(SurfaceStructure.Create(Planet, Profile, def, node, Ship.transform.position, Cargo,
                    TutorialSite.BuildCost(def, Profile.Family)));

            /// <summary>A real structure already in a given lifecycle state, through the real restore path.</summary>
            public SurfaceStructure PlaceStructure(SurfaceStructureDef def, SurfaceNode node,
                DrillLifecycle state = DrillLifecycle.Active, int tier = 1)
            {
                var runtime = new StructureRuntime(def, state, 0f, 0f, TutorialSite.DurabilityFor(def, tier), tier);
                return Own(SurfaceStructure.Restore(Planet, Profile, def, node, runtime, Cargo,
                    new List<(string itemId, float amount)>()));
            }

            public Transform Star => OwnObject(new GameObject("Test Star")).transform;

            /// <summary>Tears the step down the way the director would, at the end of the case rather than at the end of the fixture.</summary>
            public void Dispose()
            {
                Runtime.Cleanup();
                Scope.Dispose();
            }
        }

        private static StepPlan Plan(string stepId)
        {
            Assert.IsTrue(Plans.ContainsKey(stepId), $"no plan for '{stepId}'");
            return Plans[stepId];
        }

        private sealed class StepPlan
        {
            public Action<Run> Real;
            public Action<Run> Wrong;
        }

        private static void Add(Dictionary<string, StepPlan> map, string id, Action<Run> real, Action<Run> wrong) =>
            map[id] = new StepPlan { Real = real, Wrong = wrong };

        // ---- the plans --------------------------------------------------------

        private static readonly Dictionary<string, StepPlan> Plans = BuildPlans();

        private static Dictionary<string, StepPlan> BuildPlans()
        {
            var map = new Dictionary<string, StepPlan>(StringComparer.Ordinal);
            AddChapterC(map);
            AddChapterD(map);
            AddChapterE(map);
            AddChapterG(map);
            return map;
        }

        private static void AddChapterC(Dictionary<string, StepPlan> map)
        {
            Add(map, "C-01",
                real: r => r.PlaceShip(r.Radius * 0.5f),
                wrong: r => r.PlaceShip(r.Radius * 20f));

            Add(map, "C-02",
                real: r => ExtractionState.CompletePlanetScan(r.Planet),
                // The award without the scan: proves the validator is the profile, not the ledger.
                wrong: r => ProgressionService.Award(ProgressionEvents.Scan(r.WorldId), ProgressionEvents.ScanXp));

            Add(map, "C-03",
                real: r => ExtractionState.CompletePlanetScan(r.Planet),
                wrong: r =>
                {
                    ExtractionState.CompletePlanetScan(r.Planet);
                    r.Own(DrillStructure.Restore(r.Planet, r.Profile,
                        new DrillRuntime(r.Profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f),
                        DrillStructure.BuildSource.Kit, 0));
                });

            Add(map, "C-04",
                real: r => r.Ship.StabilizeTo(r.Planet.Body.transform),
                wrong: r => r.Ship.StabilizeTo(r.OwnObject(new GameObject("Not The Planet")).transform));

            Add(map, "C-05",
                real: r =>
                {
                    ExtractionState.ReserveKit();
                    r.Own(DrillStructure.Create(r.Planet, r.Profile, r.Ship.transform.position,
                        DrillStructure.BuildSource.Kit, 0));
                },
                // The confirm key pressed over the command node instead: a real build, wrong slot.
                wrong: r => r.BuildStructure(SurfaceStructureCatalog.Command, r.Profile.CommandNode));

            Add(map, "C-06",
                real: r => r.Drill.Runtime.Tick(4f),
                wrong: r => r.Drill.Runtime.Tick(1f));

            Add(map, "C-07",
                real: r => r.Drill.Runtime.Tick(120f),
                wrong: r => { r.Drill.Runtime.OnDamaged(); r.Drill.Runtime.Tick(1f); });

            Add(map, "C-08",
                real: r => r.Drill.Runtime.Tick(60f),
                wrong: r => { r.Drill.Runtime.ProductionSuspended = true; r.Drill.Runtime.Tick(60f); });

            Add(map, "C-09",
                real: r => r.Drill.Runtime.Tick(60f),
                // Filled to the brim: the store is no longer strictly between empty and full.
                wrong: r => r.Drill.Runtime.Deposit(r.Drill.Runtime.StorageCapacity));

            Add(map, "C-10",
                real: r =>
                {
                    float taken = r.Drill.Runtime.Withdraw(5f);
                    r.Cargo.Add(r.RawId, taken);
                },
                // Adding without withdrawing would mint resources: conservation must fail it.
                wrong: r => r.Cargo.Add(r.RawId, 5f));

            Add(map, "C-11",
                real: r => r.Drill.Runtime.Tick(600f),
                wrong: r => r.Drill.Runtime.Withdraw(5f));

            Add(map, "C-12",
                real: r => r.Drill.Runtime.Tick(120f),
                wrong: r =>
                {
                    r.Tiers.SetTier(DrillUpgradeCatalog.GateModuleId, 2);
                    r.Drill.Runtime.Tick(120f);
                });

            Add(map, "C-13",
                real: r => r.Drill.StartUpgrade(2, r.Cargo),
                wrong: r => r.Drill.StartUpgrade(1, r.Cargo));

            // The player's own command starts the upgrade; the halt is what the step
            // then shows. Ticking the drill is the wrong move here precisely because
            // it is what the step used to complete on all by itself: sixty seconds of
            // production with no upgrade running grows the store instead of freezing it.
            Add(map, "C-14",
                real: r => r.Drill.StartUpgrade(2, r.Cargo),
                wrong: r => r.Drill.Runtime.Tick(60f));

            Add(map, "C-15",
                real: r => r.Drill.CancelUpgrade(r.Cargo),
                wrong: r => r.Drill.Runtime.Tick(5f));

            Add(map, "C-16",
                real: r => DrillUpgradeService.Complete(r.Drill.Runtime, r.Drill.WorldId, r.Drill.NodeId,
                    r.Drill.ActiveUpgradeJob, r.Cargo),
                wrong: r => r.Drill.CancelUpgrade(r.Cargo));

            // No public seam exists for the remote 1 Hz drill tick (it lives in
            // SystemStreamingDirector's private TickDeltaDataOnly), so the fixture
            // writes the DTO field that tick would write. The step still observes
            // real state; only its producer is out of reach from a test.
            Add(map, "C-17",
                real: r => StarSystemRegistry.Deltas.TryGet(r.SystemId).drills[0].stored += 4f,
                wrong: r => StarSystemRegistry.Deltas.TryGet(r.SystemId).drills[0].accumulator += 4f);
        }

        /// <summary>
        /// Runs one recipe's worth of input through the bench, hold to hold.
        /// <para>
        /// This used to start a timed batch and then tick it past
        /// <c>SecondsScaled</c>, because a bare <c>Start</c> only reserved: the input
        /// was still physically in the hold and the output did not exist until the
        /// timer expired. Neither half of that is true any more. The input is moved
        /// onto the bench and converted in two calls that both complete immediately,
        /// so there is no elapsed time for a fixture to supply.
        /// </para>
        /// <para>
        /// What Chapter D's steps watch is unchanged, and that is the point: the
        /// durable half of the transaction — what left the hold, what arrived in it —
        /// is exactly what a validator can still see.
        /// </para>
        /// </summary>
        /// <summary>Family 0's two catalog recipes: 2-to-1 parts, 3-to-1 material.</summary>
        private const int PartsRecipe = 0;

        /// <inheritdoc cref="PartsRecipe"/>
        private const int MaterialRecipe = 1;

        /// <summary>Loads the bench through the one command seam the page and the panel both use.</summary>
        private static void BenchDeposit(Run r, int amount, int family = 0) =>
            ProcessingBenchCommands.TryDeposit(
                r.Bench, r.Cargo, ExtractionCatalog.ResourceIds[family], amount);

        private static void BenchConvert(Run r, int recipeIndex, int amount) =>
            ProcessingBenchCommands.TryConvert(r.Bench, r.Cargo, recipeIndex, amount, out _);

        private static void BenchWithdraw(Run r, int amount) =>
            ProcessingBenchCommands.TryWithdraw(r.Bench, r.Cargo, amount);

        private static void RunBatch(Run r, int recipeIndex)
        {
            ProcessingRecipe recipe = ProcessingBenchCommands.RecipeAt(recipeIndex);
            if (recipe == null) return;
            if (ProcessingBenchCommands.TryDeposit(r.Bench, r.Cargo, recipe.InputItemId, recipe.InputCount)
                != BenchDepositReason.None) return;
            ProcessingBenchCommands.TryConvert(r.Bench, r.Cargo, recipeIndex, recipe.InputCount, out _);
        }

        private static void AddChapterD(Dictionary<string, StepPlan> map)
        {
            const string processingId = "industry.resource_processing";
            const string cargoId = "industry.logistics_cargo";

            // D-01..D-05 follow the rewritten Chapter D: load the bench, choose an
            // amount, choose a recipe, convert, take the leftover back. Each "wrong"
            // is a real action the game permits that is nonetheless not the lesson —
            // an assertion the validator could only pass by watching the right thing.

            Add(map, "D-01",
                real: r => BenchDeposit(r, 4),
                // Stock leaves the hold and never arrives on the bench. D-01's whole
                // claim is that the two are the same units, so a hold that merely
                // shrank must not satisfy it.
                wrong: r => r.Cargo.TryRemove(ExtractionCatalog.ResourceIds[0], 4f));

            Add(map, "D-02",
                // One confirmation carrying four units.
                real: r => BenchDeposit(r, 4),
                // The same four units, one at a time. The bench ends up identical and
                // the lesson — choosing a quantity — never happened, which is exactly
                // the distinction the step's poll-to-poll jump exists to draw.
                wrong: r =>
                {
                    for (int i = 0; i < 4; i++) { BenchDeposit(r, 1); r.Evaluate(); }
                });

            Add(map, "D-03",
                // The parts recipe, chosen over the material recipe offered beside it.
                real: r => { BenchDeposit(r, 4); BenchConvert(r, PartsRecipe, 4); },
                // The other recipe on the same input: a conversion did happen, so a
                // validator blind to which one would accept this.
                wrong: r => { BenchDeposit(r, 3); BenchConvert(r, MaterialRecipe, 3); });

            Add(map, "D-04",
                real: r =>
                {
                    BenchDeposit(r, 6);
                    r.Evaluate();               // the baseline the step's poll needs
                    BenchConvert(r, MaterialRecipe, 6);   // two outputs in one transaction
                },
                // One batch, not two: a real conversion that is not the "spend several
                // batches in a single confirmation" the step is about.
                wrong: r =>
                {
                    BenchDeposit(r, 6);
                    r.Evaluate();
                    BenchConvert(r, MaterialRecipe, 3);
                });

            // The bench starts loaded here (the step's own prepare restores five units),
            // so the action under test is the return trip, not the loading.
            Add(map, "D-05",
                real: r => BenchWithdraw(r, 3),
                // The bench falls by four, but the hold gains parts rather than the raw
                // input. Only a validator checking that the same units came back passes
                // the first and refuses this.
                wrong: r => BenchConvert(r, PartsRecipe, 4));

            // One bench holding one item type, so each family's input has to be cleared
            // before the next can be loaded — five sequential conversions, not five
            // simultaneous ones.
            Add(map, "D-06",
                real: r =>
                {
                    for (int f = 0; f < ProcessingCatalog.MaterialIds.Length; f++)
                        RunBatch(r, f * 2 + 1);
                },
                wrong: r =>
                {
                    for (int f = 0; f < 3; f++) RunBatch(r, f * 2 + 1);
                });

            Add(map, "D-07",
                real: r => { RunBatch(r, 0); RunBatch(r, 1); },
                wrong: r => RunBatch(r, 0));

            Add(map, "D-08",
                real: r => { r.Tiers.SetTier(cargoId, 1); r.ModuleHost.ApplyFunctionalEffect(cargoId); },
                wrong: r => r.Cargo.SetCapacity(999f));

            Add(map, "D-09",
                real: r => r.Cargo.TryReserveAll(ChapterDSteps.TutorialReserveOwner,
                    new List<(string, float)> { (ProcessingCatalog.BasicPartId, 10f) }),
                wrong: r => r.Cargo.TryRemove(ProcessingCatalog.BasicPartId, 10f));

            Add(map, "D-10",
                real: r => r.Own(LogisticsCenter.Create(r.Manifest, r.Star, Vector3.zero, 40, 12, 10)),
                wrong: r => r.Own(LogisticsCenter.Restore(r.Manifest, r.Star, Vector3.zero,
                    new LogisticsCenterRuntime(LogisticsCenterTier.T1, LogisticsCenterState.Active, 0f,
                        new Durability(400f, 800f, 1200f, 1f), false, 0f),
                    new List<(string itemId, float amount)>())));

            Add(map, "D-11",
                real: r =>
                {
                    LogisticsCenter center = r.Center;
                    center.Inventory.Add(ProcessingCatalog.BasicPartId, 10f);
                    r.Cargo.TryRemove(ProcessingCatalog.BasicPartId, 10f);
                    r.Evaluate();                       // the outbound leg has to be seen before the return one
                    center.Inventory.TryRemove(ProcessingCatalog.BasicPartId, 4f);
                    r.Cargo.Add(ProcessingCatalog.BasicPartId, 4f);
                },
                wrong: r =>
                {
                    r.Center.Inventory.Add(ProcessingCatalog.BasicPartId, 10f);
                    r.Cargo.TryRemove(ProcessingCatalog.BasicPartId, 10f);
                });

            Add(map, "D-12",
                real: r => r.Center.StartUpgrade(),
                wrong: r => r.Center.Runtime.Tick(1f));

            Add(map, "D-13",
                real: r => r.Center.CancelUpgrade(r.Cargo),
                wrong: r => r.Center.Runtime.Tick(5f));

            Add(map, "D-14",
                real: r => LogisticsState.AddRoute(new CollectorRoute(LogisticsState.NextRouteId(), r.SystemId,
                    r.WorldId, "tutorial-collector", RoutePriority.Normal)),
                wrong: r => LogisticsState.AddRoute(new CollectorRoute(LogisticsState.NextRouteId(), r.SystemId,
                    "some-other-world/pl99", "tutorial-collector", RoutePriority.Normal)));

            Add(map, "D-15",
                real: r => LogisticsState.AddRoute(new CollectorRoute(LogisticsState.NextRouteId(), r.SystemId,
                    r.WorldId, "tutorial-collector", RoutePriority.Normal)),
                wrong: r => LogisticsState.AddRoute(new CollectorRoute(LogisticsState.NextRouteId(), r.SystemId,
                    "some-other-world/pl99", "another-collector", RoutePriority.Normal)));

            Add(map, "D-16",
                real: r => { r.Center.Inventory.Clear(); r.Center.Dismantle(); },
                wrong: r => r.Center.Inventory.Add(ProcessingCatalog.BasicPartId, 5f));
        }

        private static void AddChapterE(Dictionary<string, StepPlan> map)
        {
            const string demoId = "industry.station_construction";
            const string nuclearId = "nuclear.star_energy_storage";
            const string processingId = "industry.resource_processing";
            const string scannerId = "exploration.scanner";
            const string cargoId = "industry.logistics_cargo";

            Add(map, "E-01",
                real: r => ProgressionService.Award(ProgressionEvents.DrillFirst(r.WorldId), ProgressionEvents.DrillXp),
                wrong: r => ProgressionService.Award(ProgressionEvents.RecipeFirst("recipe.part.0"),
                    ProgressionEvents.RecipeXp));

            Add(map, "E-02",
                real: r => ProgressionService.Award("tutorial-e02-cross", 500),
                wrong: r => ProgressionService.Award("tutorial-e02-nothing", 0));

            Add(map, "E-03",
                real: r => r.Tiers.GrantFreeStartingModules(),
                wrong: r => r.Tiers.SetTier("nuclear.laboratory", 1));

            Add(map, "E-04",
                real: r => ExtractionState.GetDominance(r.WorldId).Restore(DominanceState.Controlled, 0f, 0f),
                wrong: r => ExtractionState.GetDominance(r.WorldId).Restore(DominanceState.Developing, 0f, 0f));

            Add(map, "E-05",
                real: r =>
                {
                    foreach (ModuleResourceCost line in ModuleCatalog.Get(demoId).TierCosts[1].Resources)
                        r.Cargo.Add(line.ItemId, line.Amount);
                },
                wrong: r =>
                {
                    ModuleResourceCost first = ModuleCatalog.Get(demoId).TierCosts[1].Resources[0];
                    r.Cargo.Add(first.ItemId, first.Amount * 0.5f);
                });

            Add(map, "E-06",
                real: r => r.Tiers.SetTier(demoId, 1),
                wrong: r => r.Tiers.SetTier(nuclearId, 1));

            Add(map, "E-07",
                real: r =>
                {
                    ProgressionService.Restore(ProgressionService.State.TotalXp, 5, TutorialSite.LedgerSnapshot());
                    foreach (ModuleResourceCost line in ModuleCatalog.Get(demoId).TierCosts[1].Resources)
                        r.Cargo.Add(line.ItemId, line.Amount);
                },
                wrong: r =>
                {
                    foreach (ModuleResourceCost line in ModuleCatalog.Get(demoId).TierCosts[1].Resources)
                        r.Cargo.Add(line.ItemId, line.Amount);
                });

            Add(map, "E-08",
                real: r => r.Upgrades.TryStartUpgrade(demoId),
                wrong: r => r.Upgrades.TryStartUpgrade("nuclear.laboratory"));

            Add(map, "E-09",
                real: r => r.Upgrades.TryStartUpgrade(demoId),
                wrong: r => r.Upgrades.TryStartUpgrade(processingId));

            Add(map, "E-10",
                real: r => r.Upgrades.TryStartUpgrade(processingId),
                wrong: r => ProgressionService.Restore(ProgressionService.State.TotalXp, 99,
                    TutorialSite.LedgerSnapshot()));

            Add(map, "E-11",
                real: r => r.Upgrades.Tick(ModuleCatalog.Get(demoId).TierCosts[1].Seconds + 1f),
                wrong: r =>
                {
                    r.Upgrades.ScreenPaused = true;
                    r.Upgrades.Tick(ModuleCatalog.Get(demoId).TierCosts[1].Seconds + 1f);
                });

            // E-12's tier hook is raised from ShipModuleVisuals.Update; PlayMode owns the positive case.
            Add(map, "E-12",
                real: r => r.Tiers.SetTier(demoId, 2),
                wrong: r => r.Tiers.SetTier(nuclearId, 1));

            Add(map, "E-13",
                real: r => r.Tiers.SetTier(demoId, ModuleCatalog.Get(demoId).MaxTierThisSlice),
                wrong: r => r.Tiers.SetTier(processingId, 5));

            Add(map, "E-14",
                real: r => r.Tiers.SetTier(DrillUpgradeCatalog.GateModuleId, 2),
                wrong: r => r.Tiers.SetTier(cargoId, 5));

            Add(map, "E-15",
                real: r => r.PlaceStructure(SurfaceStructureCatalog.Command, r.Profile.CommandNode),
                wrong: r => r.PlaceStructure(SurfaceStructureCatalog.Laser, r.Profile.DefenseNode(0)));

            Add(map, "E-16",
                real: r =>
                {
                    r.Tiers.SetTier(scannerId, 1);
                    r.ModuleHost.ApplyFunctionalEffect(cargoId);
                    r.ModuleHost.ApplyFunctionalEffect(processingId);
                    r.ModuleHost.ApplyFunctionalEffect(scannerId);
                },
                wrong: r => r.ModuleHost.ApplyFunctionalEffect(cargoId));
        }

        private static void AddChapterG(Dictionary<string, StepPlan> map)
        {
            Add(map, "G-01",
                real: r => r.Extraction.ToggleSupportNodeSelection(),
                wrong: r => { r.Extraction.ToggleSupportNodeSelection(); r.Extraction.ToggleSupportNodeSelection(); });

            Add(map, "G-02",
                real: r => r.BuildStructure(SurfaceStructureCatalog.Command, r.Profile.CommandNode),
                wrong: r => r.PlaceStructure(SurfaceStructureCatalog.Command, r.Profile.CommandNode));

            Add(map, "G-03",
                real: r => r.Structure(SurfaceStructureType.Command).StartUpgrade(
                    ProgressionService.State.Level, 1, 0, true, r.Cargo),
                wrong: r => r.Structure(SurfaceStructureType.Command).StartUpgrade(1, 1, 0, false, r.Cargo));

            Add(map, "G-04",
                real: r => r.PlaceStructure(SurfaceStructureCatalog.Laser, r.Profile.DefenseNode(0)),
                wrong: r => r.PlaceStructure(SurfaceStructureCatalog.Laser, r.Profile.DefenseNode(0),
                    DrillLifecycle.Constructing));

            Add(map, "G-05",
                real: r => UpgradeToTwo(r, SurfaceStructureType.Laser, 2, 2),
                wrong: r => r.Structure(SurfaceStructureType.Laser).StartUpgrade(
                    ProgressionService.State.Level, 2, 2, true, r.Cargo));

            Add(map, "G-06",
                real: r => r.PlaceStructure(SurfaceStructureCatalog.Plasma, r.Profile.DefenseNode(1)),
                wrong: r => r.PlaceStructure(SurfaceStructureCatalog.Plasma, r.Profile.DefenseNode(2)));

            Add(map, "G-07",
                real: r => UpgradeToTwo(r, SurfaceStructureType.Plasma, 2, 2),
                wrong: r => r.Structure(SurfaceStructureType.Plasma).StartUpgrade(
                    ProgressionService.State.Level, 2, 2, true, r.Cargo));

            Add(map, "G-08",
                real: r => r.BuildStructure(SurfaceStructureCatalog.SensorArray, r.Profile.SupportNode(0)),
                wrong: r => r.BuildStructure(SurfaceStructureCatalog.MaintenancePlatform, r.Profile.SupportNode(1)));

            Add(map, "G-09",
                real: r => UpgradeToTwo(r, SurfaceStructureType.SensorArray, 2, 0),
                wrong: r => r.Structure(SurfaceStructureType.SensorArray).StartUpgrade(
                    ProgressionService.State.Level, 2, 0, true, r.Cargo));

            Add(map, "G-10",
                real: r => r.BuildStructure(SurfaceStructureCatalog.MaintenancePlatform, r.Profile.SupportNode(1)),
                wrong: r => r.BuildStructure(SurfaceStructureCatalog.SensorArray, r.Profile.SupportNode(0)));

            Add(map, "G-11",
                real: r => UpgradeToTwo(r, SurfaceStructureType.MaintenancePlatform, 2, 0),
                wrong: r => r.Structure(SurfaceStructureType.MaintenancePlatform).StartUpgrade(
                    ProgressionService.State.Level, 2, 0, true, r.Cargo));

            Add(map, "G-12",
                real: r => r.Structure(SurfaceStructureType.Command).Cancel(r.Cargo),
                wrong: r => r.Structure(SurfaceStructureType.Command).Runtime.Tick(2f));

            Add(map, "G-13",
                real: r =>
                {
                    Durability durability = r.Structure(SurfaceStructureType.Laser).Runtime.Durability;
                    r.Structure(SurfaceStructureType.Laser).Runtime
                        .ApplyDamage(durability.ShieldMax + durability.ArmorMax * 0.25f);
                },
                wrong: r =>
                {
                    Durability durability = r.Structure(SurfaceStructureType.Laser).Runtime.Durability;
                    r.Structure(SurfaceStructureType.Laser).Runtime.ApplyDamage(durability.ShieldMax * 0.5f);
                });

            Add(map, "G-14",
                real: r =>
                {
                    StructureRuntime runtime = r.Structure(SurfaceStructureType.Laser).Runtime;
                    runtime.ApplyDamage(runtime.Durability.ShieldMax + runtime.Durability.ArmorMax
                                        + runtime.Durability.HullMax + 10f);
                    r.Evaluate();                       // the disabled state has to be seen while it is true
                    runtime.Repair(200f);
                },
                wrong: r =>
                {
                    StructureRuntime runtime = r.Structure(SurfaceStructureType.Laser).Runtime;
                    runtime.ApplyDamage(runtime.Durability.ShieldMax + runtime.Durability.ArmorMax
                                        + runtime.Durability.HullMax + 10f);
                });

            Add(map, "G-15",
                real: r =>
                {
                    SurfaceStructure command = r.Structure(SurfaceStructureType.Command);
                    StructureUpgradeService.Complete(command,
                        new StructureUpgradeJob(SurfaceStructureType.Command, 2,
                            PlanetaryDefenseCatalog.CommandT2.BuildSeconds, 0f), r.Cargo);
                    command.MaintenanceReserve.ApplyTier(command.Runtime.Tier);
                },
                wrong: r => r.Structure(SurfaceStructureType.Command).MaintenanceReserve.ApplyTier(2));

            // G-16's real seam (ShipMaintenanceReserveAccess.TryDeposit) is gated on a
            // proximity flag only Update sets; PlayMode owns the positive case.
            Add(map, "G-16",
                real: r => { },
                wrong: r =>
                {
                    string itemId = ProcessingCatalog.MaterialIds[0];
                    r.Structure(SurfaceStructureType.Command).MaintenanceReserve.Add(itemId, 10f);
                    r.Cargo.TryRemove(itemId, 10f);
                });

            Add(map, "G-17",
                real: r => r.Structure(SurfaceStructureType.Command).MaintenanceReserve
                    .SetPolicy(MaintenancePolicy.Emergency),
                wrong: r => r.Structure(SurfaceStructureType.Command).MaintenanceReserve
                    .SetPolicy(MaintenancePolicy.FullField));

            Add(map, "G-18",
                real: r =>
                {
                    SurfaceStructure command = r.Structure(SurfaceStructureType.Command);
                    string itemId = ProcessingCatalog.MaterialIds[0];
                    var mission = new MaintenanceResupplyMission("tutorial-resupply", "tutorial-collector-mk2",
                        r.SystemId, command.TargetId, itemId, 10f);
                    TutorialSite.PendingResupply = mission;
                    mission.BeginTravel();
                    mission.BeginUnloading();
                    command.MaintenanceReserve.Add(itemId, 10f);
                    r.Cargo.TryRemove(itemId, 10f);
                    mission.BeginReturn();
                    mission.Complete();
                },
                wrong: r =>
                {
                    SurfaceStructure command = r.Structure(SurfaceStructureType.Command);
                    var mission = new MaintenanceResupplyMission("tutorial-resupply", "tutorial-collector-mk2",
                        r.SystemId, command.TargetId, ProcessingCatalog.MaterialIds[0], 10f);
                    TutorialSite.PendingResupply = mission;
                    mission.BeginTravel();
                });

            Add(map, "G-19",
                real: r =>
                {
                    r.Center.Runtime.ApplyDamage(1000000f);
                    foreach (CollectorRoute route in LogisticsState.Routes)
                    {
                        if (route == null || route.SystemId != r.SystemId) continue;
                        r.Drill.Runtime.Deposit(route.HeldAmount);
                        route.SetHeld(null, 0f);
                        route.Pause();
                    }
                },
                wrong: r => r.Center.Runtime.ApplyDamage(1000000f));
        }

        /// <summary>Starts and finishes a real structure upgrade through the production service.</summary>
        private static void UpgradeToTwo(Run r, SurfaceStructureType type, int commandTier, int weaponCoordinationTier)
        {
            SurfaceStructure structure = r.Structure(type);
            StructureUpgradeRejectReason reason = structure.StartUpgrade(ProgressionService.State.Level,
                commandTier, weaponCoordinationTier, true, r.Cargo);
            Assert.AreEqual(StructureUpgradeRejectReason.None, reason,
                $"{type} T2 upgrade was refused, so the step under test can never be reached honestly.");
            StructureUpgradeJob job = structure.ActiveUpgradeJob;
            while (!job.Tick(10f)) { }
            StructureUpgradeService.Complete(structure, job, r.Cargo);
        }
    }
}
