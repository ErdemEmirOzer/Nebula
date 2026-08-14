using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Tutorial;
using Nebula.Tutorial.Steps;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The chapter C/D/E/G steps whose success genuinely arrives from a real
    /// frame, and the live-frame proof that the same step runtimes work when
    /// production <c>Update</c> methods -- not a test -- are driving them.
    /// <para>
    /// Three steps cannot be completed in EditMode at all, for reasons that are
    /// properties of production rather than of the tutorial: E-12's tier hook is
    /// raised inside <c>ShipModuleVisuals.Update</c>; G-16's transfer is refused
    /// unless <c>ShipMaintenanceReserveAccess.Update</c> has already resolved a
    /// Command Center within its 500-unit approach range; and G-12's real seam
    /// ends in <c>Object.Destroy</c>, which only exists in play mode.
    /// </para>
    /// <para>
    /// No scene is loaded and no galaxy is generated. Every fixture below builds
    /// only the real components the step under test touches, which is what keeps
    /// the whole file inside one Unity process and a few seconds.
    /// </para>
    /// </summary>
    public class TutorialChapterCDEGGameplayTests
    {
        private const int Seed = 12345;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<TutorialStepScope> openScopes = new List<TutorialStepScope>();
        private static Dictionary<string, TutorialStepDefinition> definitions;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            StarSystemRegistry.Initialize(unchecked((ulong)Seed));
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));

            definitions = new Dictionary<string, TutorialStepDefinition>(System.StringComparer.Ordinal);
            foreach (TutorialStepDefinition step in ChapterCSteps.Build()) definitions[step.StepId] = step;
            foreach (TutorialStepDefinition step in ChapterDSteps.Build()) definitions[step.StepId] = step;
            foreach (TutorialStepDefinition step in ChapterESteps.Build()) definitions[step.StepId] = step;
            foreach (TutorialStepDefinition step in ChapterGSteps.Build()) definitions[step.StepId] = step;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => StarSystemRegistry.Clear();

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
            for (int i = spawned.Count - 1; i >= 0; i--) if (spawned[i] != null) Object.Destroy(spawned[i]);
            spawned.Clear();

            ExtractionState.Reset(Seed);
            LogisticsState.Reset();
            ProgressionService.Reset();
            TutorialSite.PendingResupply = null;

            // C-07 runs the drill's own Update at TutorialSession.MaxTimeScale and puts
            // it back itself, but only on the path where it reaches that line. A run
            // aborted inside its wait loop would leave every later test in the process
            // on a compressed clock, and the fixtures that pause the world already keep
            // this same safety net (Ani054, Ani0541, Ani0543).
            Time.timeScale = 1f;
        }

        // ---- the three steps that need a real frame ---------------------------

        [UnityTest]
        public IEnumerator E12_TheTierHook_FiresFromTheShipsOwnModuleVisuals()
        {
            Step run = Start("E-12");
            Assert.IsFalse(run.Evaluate(), "E-12 must not be complete before the module tier really moves.");

            ModuleProgressionState tiers = run.Scope.Root.GetComponentInChildren<ShipModules>(true).Modules;
            tiers.SetTier("industry.station_construction", 2);

            // ShipModuleVisuals.Update is what notices; nothing else may.
            yield return null;

            Assert.IsTrue(run.Evaluate(),
                "the module's tier really changed and ShipModuleVisuals raised its hook, but the step did not see it.");
        }

        [UnityTest]
        public IEnumerator E12_ATierThatNeverMoves_NeverFiresTheHook()
        {
            Step run = Start("E-12");
            yield return null;
            yield return null;
            Assert.IsFalse(run.Evaluate(), "a step that completes on its own frame count is not observing anything.");
        }

        [UnityTest]
        public IEnumerator G16_TheReserveTransfer_MovesRealStockBothWays()
        {
            Step run = Start("G-16");
            string itemId = ProcessingCatalog.MaterialIds[0];

            SurfaceStructure command = ExtractionState.FindStructure(run.WorldId, SurfaceStructureType.Command);
            Assert.IsNotNull(command, "G-16 prepares an Active Command Center to transfer against.");

            ShipController ship = run.Scope.Root.GetComponentInChildren<ShipController>(true);
            var access = run.Scope.Root.GetComponentInChildren<ShipMaintenanceReserveAccess>(true);

            // The approach range is resolved in Update, and the transfer is refused until it is.
            yield return ParkOnTheCommandCenter(ship, access, command);

            Assert.AreEqual(ReserveTransferRejectReason.None,
                access.TryDeposit(itemId, ShipMaintenanceReserveAccess.DepositWithdrawIncrement));
            Assert.IsFalse(run.Evaluate(), "a deposit alone is only half of what the step teaches.");

            Assert.AreEqual(ReserveTransferRejectReason.None, access.TryWithdraw(itemId, 4f));
            Assert.IsTrue(run.Evaluate(), "stock moved out and back through the real reserve seam, and the step missed it.");
        }

        [UnityTest]
        public IEnumerator G16_AnItemTheReserveDoesNotAccept_IsRefused()
        {
            Step run = Start("G-16");
            SurfaceStructure command = ExtractionState.FindStructure(run.WorldId, SurfaceStructureType.Command);
            Assert.IsNotNull(command, "G-16 prepares an Active Command Center to transfer against.");
            ShipController ship = run.Scope.Root.GetComponentInChildren<ShipController>(true);
            var access = run.Scope.Root.GetComponentInChildren<ShipMaintenanceReserveAccess>(true);

            // Same precondition as the transfer test: out of range, the refusal under
            // test is never reached and NotInRange is returned instead of the
            // whitelist's own answer, which reads as "Expected: ItemNotAccepted".
            yield return ParkOnTheCommandCenter(ship, access, command);

            // Raw resources are deliberately outside the reserve's whitelist.
            Assert.AreEqual(ReserveTransferRejectReason.ItemNotAccepted,
                access.TryDeposit(ExtractionCatalog.ResourceIds[0], 10f));
            Assert.IsFalse(run.Evaluate());
        }

        /// <summary>
        /// Parks the ship on the Command Center and waits for
        /// <c>ShipMaintenanceReserveAccess.Update</c> to resolve its 500-unit approach
        /// range — the precondition both G-16 tests need and neither is testing.
        /// <para>
        /// Re-parked on every attempt rather than once against a position sampled
        /// before any frame ran: the structure re-derives its own world position from
        /// its planet every frame (<c>SurfaceStructure.cs:215</c>), so a single sample
        /// taken during <c>Prepare</c> is only valid until the next frame. The
        /// assertion is unchanged — if production never reports the range, this still
        /// fails, and now it names what it saw instead of leaving the reader to guess
        /// which of five conditions in that Update was the one that did not hold.
        /// </para>
        /// </summary>
        private static IEnumerator ParkOnTheCommandCenter(ShipController ship,
            ShipMaintenanceReserveAccess access, SurfaceStructure command)
        {
            // A frame first, so the structure has run its own Update at least once and
            // is sitting where it will actually be read from.
            yield return null;

            // Then park and resolve in the same instant. Re-parking across frames was
            // treating a frame-of-reference problem as a settling problem and could
            // never converge: the Command Center re-derives its world position from its
            // planet every frame (SurfaceStructure.cs:215), and the two Update methods
            // run in undefined order, so the ship was always compared against a position
            // the structure had already left -- 5321.5 units, roughly the far side of
            // the planet, no matter how many frames it was given.
            ship.transform.position = command.transform.position;
            access.ResolveNearbyCommand();

            Assert.IsTrue(access.InRange,
                "the ship was placed on the Command Center and is still reported out of range. "
                + $"commandAlive={command != null}, "
                + $"distance={(command != null ? Vector3.Distance(ship.transform.position, command.transform.position) : -1f):0.0}, "
                + $"registeredStructures={ExtractionState.Structures.Count}, "
                + $"active={(command != null && command.Runtime.IsActive)}, "
                + $"reserve={(command != null && command.MaintenanceReserve != null ? "present" : "null")}, "
                + $"nearby={(access.NearbyCommand == null ? "none" : access.NearbyCommand.name)}");
        }

        [UnityTest]
        public IEnumerator G12_CancellingAnUnfinishedBuild_RefundsEveryReservedItem()
        {
            Step run = Start("G-12");
            SurfaceStructure command = ExtractionState.FindStructure(run.WorldId, SurfaceStructureType.Command);
            Assert.IsNotNull(command, "G-12 prepares a real, still-building structure to cancel.");
            Assert.IsFalse(run.Evaluate(), "the structure is still standing, so the step cannot be complete.");

            LogisticsCargo cargo = run.Scope.Root.GetComponentInChildren<ShipLogistics>(true).Cargo;
            command.Cancel(cargo);

            // Destroy is deferred to the end of the frame; the registry only clears then.
            yield return null;

            Assert.IsTrue(run.Evaluate(),
                "every reserved item came back at floor(75%) and the structure is gone, but the step did not see it.");
        }

        [UnityTest]
        public IEnumerator G12_TickingTheBuildForward_IsNotACancel()
        {
            Step run = Start("G-12");
            yield return null;
            yield return null;
            Assert.IsFalse(run.Evaluate(), "letting the build run on is not the cancel the step asks for.");
        }

        // ---- the live path: production Update methods driving a step ----------

        [UnityTest]
        public IEnumerator C07_TheDrillsOwnUpdate_CompletesTheConstructionStep()
        {
            Step run = Start("C-07");
            DrillStructure drill = FindDrill(run.WorldId);
            Assert.IsNotNull(drill);
            Assert.AreEqual(DrillLifecycle.Constructing, drill.Runtime.State);
            Assert.IsFalse(run.Evaluate());

            // The drill's own Update is the only thing advancing this; the test
            // never touches the runtime. A tutorial-only time scale is what keeps
            // the real 30s constant from becoming a 30s test.
            float restore = Time.timeScale;
            Time.timeScale = TutorialSession.MaxTimeScale;
            float guard = 0f;
            while (drill.Runtime.State != DrillLifecycle.Active && guard < 20f)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            Time.timeScale = restore;

            Assert.AreEqual(DrillLifecycle.Active, drill.Runtime.State,
                "the drill never finished under its own Update; the step's duration source is wrong.");
            Assert.IsTrue(run.Evaluate());
        }

        [UnityTest]
        public IEnumerator C05_ADrillThatWasNeverLaunched_LeavesTheStepIncomplete()
        {
            Step run = Start("C-05");
            yield return null;
            Assert.IsFalse(run.Evaluate(), "no capsule was sent, so nothing may report success.");
        }

        // ---- the real-frame idle sweep ----------------------------------------

        /// <summary>
        /// Frames each step is left alone for. A sixth of a second: long enough for
        /// anything that arrives on the next frame or two (a driver's refresh
        /// cadence, a first paint, a late subscription), and far too short to move a
        /// drill, an upgrade job or a logistics run, which are the waits the
        /// curriculum genuinely asks the player to sit through.
        /// </summary>
        private const int IdleFrames = 10;

        /// <summary>
        /// Every step in these four chapters, prepared and then left completely
        /// alone while real <c>Update</c> methods run.
        /// <para>
        /// The EditMode fixture polls the same steps repeatedly, which catches a
        /// validator that latches its way to true — but with no player loop it
        /// cannot catch a step that becomes true because production ticked. That is
        /// the shape of failure this exists for, and it is why the sweep is worth
        /// paying real frames for even though every one of these steps already has
        /// three EditMode assertions.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator NoStepCompletesWhileTheWorldSimplyRuns()
        {
            var failures = new List<string>();

            foreach (string stepId in definitions.Keys)
            {
                Step run;
                try { run = Start(stepId); }
                catch (System.Exception error)
                {
                    failures.Add($"{stepId} threw while preparing: {error.GetType().Name}: {error.Message}");
                    TearDown();
                    SetUp();
                    continue;
                }

                for (int frame = 0; frame < IdleFrames; frame++)
                {
                    if (run.Evaluate())
                    {
                        failures.Add($"{stepId} completed {frame + 1} frame(s) after Prepare, " +
                                     "with the player doing nothing at all");
                        break;
                    }
                    yield return null;
                }

                // Between steps rather than at the end: these steps share
                // ExtractionState, LogisticsState and the progression ledger, and a
                // drill one step built would otherwise be a drill the next one finds.
                TearDown();
                SetUp();
                yield return null;
            }

            CollectionAssert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ---- harness ----------------------------------------------------------

        private static DrillStructure FindDrill(string worldId)
        {
            foreach (DrillStructure drill in ExtractionState.Drills)
                if (drill != null && drill.WorldId == worldId) return drill;
            return null;
        }

        private Step Start(string stepId)
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
            return new Step(definition, runtime, scope, report);
        }

        private sealed class Step
        {
            public readonly TutorialStepDefinition Definition;
            public readonly ITutorialStepRuntime Runtime;
            public readonly TutorialStepScope Scope;
            public readonly TutorialStepReport Report;

            public Step(TutorialStepDefinition definition, ITutorialStepRuntime runtime,
                TutorialStepScope scope, TutorialStepReport report)
            {
                Definition = definition; Runtime = runtime; Scope = scope; Report = report;
            }

            public bool Evaluate()
            {
                Runtime.Evaluate(Report);
                return Report.IsComplete;
            }

            public string WorldId
            {
                get
                {
                    foreach (PlanetIdentity planet in ExtractionState.AllPlanets)
                        if (planet != null && planet.Body != null) return planet.WorldId;
                    return null;
                }
            }
        }
    }
}
