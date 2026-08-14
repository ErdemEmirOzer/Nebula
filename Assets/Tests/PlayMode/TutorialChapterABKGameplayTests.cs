using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Celestial;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Simulation;
using Nebula.Tutorial;
using Nebula.Tutorial.Steps;
using Nebula.UI.Controls;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The chapter A/B/K steps whose real command needs a running world: a frame
    /// loop, a materialized planet, a POI timer, or the key-capture listener.
    /// <para>
    /// One tutorial world is composed for the whole fixture and disposed once.
    /// Composing per test would generate a galaxy per test, and this suite has
    /// sixty-nine cases in it.
    /// </para>
    /// <para>
    /// Every driver below goes through the production seam the curriculum names --
    /// <c>ShipController.ApplyLook</c>, <c>ShipController.FlightInput</c>,
    /// <c>TestApplyDisplacement</c>, <c>ShipExtraction.TickScan</c>,
    /// <c>PoiState.TickLocate</c>, <c>ShipPoi.TryStartPrimaryAction</c>, a real
    /// <c>Button.onClick</c>, or a real key press through
    /// <c>NebulaKeyCaptureListener.Source</c>. None of them writes the observable
    /// the step is watching.
    /// </para>
    /// </summary>
    public class TutorialChapterABKGameplayTests
    {
        /// <summary>The A/B/K steps that need the world. Together with the EditMode fixture's pure-seam list this is all 36.</summary>
        private static readonly string[] WorldSteps =
        {
            "A-14",
            "A-04", "A-05", "A-06", "A-07", "A-08", "A-09", "A-10", "A-11", "A-12", "A-13",
            "B-01", "B-02", "B-03", "B-04", "B-05", "B-06", "B-07", "B-08", "B-09", "B-10", "B-11",
            "K-11", "K-12",
        };

        /// <summary>
        /// Steps with no positive driver. Empty, and meant to stay empty: B-11 was
        /// here while it asked the player to fly out of a system, which is
        /// unreachable (see <c>LeavingASystemPutsTheShipInsideNoSystemAtAll</c>), and
        /// came back out once it was reformulated as a reading of two cards at once.
        /// </summary>
        private static readonly string[] NotCompletableInATutorialComposition = Array.Empty<string>();

        private static bool HasPositiveDriver(string stepId) =>
            Array.IndexOf(NotCompletableInATutorialComposition, stepId) < 0;

        private TutorialSession session;
        private Exception compositionError;
        private TutorialStepScope scope;

        private float savedTimeScale;
        private long savedXp;
        private int savedPoints;
        private readonly Dictionary<CombatAction, KeyCode> savedBindings = new Dictionary<CombatAction, KeyCode>();

        // =====================================================================
        // ONE WORLD FOR THE WHOLE FIXTURE
        // =====================================================================

        [OneTimeSetUp]
        public void ComposeOnce()
        {
            try { session = TutorialSession.Compose(); }
            catch (Exception error) { compositionError = error; }
        }

        [OneTimeTearDown]
        public void DisposeOnce()
        {
            session?.Dispose();
            session = null;
        }

        [SetUp]
        public void SetUp()
        {
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            savedXp = ProgressionService.State.TotalXp;
            savedPoints = ProgressionService.State.DevelopmentPoints;

            savedBindings.Clear();
            foreach (CombatAction action in NebulaControlBindings.Actions)
                savedBindings[action] = NebulaControlBindings.KeyFor(action);

            ExclusiveModal.ResetForSession();
            NebulaKeyCaptureListener.ResetSourceToKeyboard();
            ShipController.ResetFlightInputToKeyboard();
        }

        [TearDown]
        public void TearDown()
        {
            DisposeScope();
            // Before anything else: a held boost control left behind would fly the
            // ship through the next test's world.
            ShipController.ResetFlightInputToKeyboard();

            foreach (KeyValuePair<CombatAction, KeyCode> entry in savedBindings)
                if (NebulaControlBindings.KeyFor(entry.Key) != entry.Value)
                    NebulaControlBindings.TryRebind(entry.Key, entry.Value);

            NebulaKeyCaptureListener.ResetSourceToKeyboard();
            ExclusiveModal.ResetForSession();
            session?.RestoreTimeScale();
            Time.timeScale = savedTimeScale;
            InputGate.Suppressed = false;
            // Materialized first: Restore clears the ledger before it re-adds, so
            // handing it its own live set would empty it.
            ProgressionService.Restore(savedXp, savedPoints, new List<string>(ProgressionService.Ledger.All));
        }

        // =====================================================================
        // THE THREE CHECKS, ONE PASS EACH
        // =====================================================================

        /// <summary>
        /// Prepare may install everything except the thing the player is asked to
        /// do — and this asserts it over real frames rather than once.
        /// <para>
        /// Evaluating a single time immediately after Prepare is too weak, and this
        /// fixture is where that showed: a step can be false on the frame it opens
        /// and true two frames later because a HUD driver hit its 0.25s refresh, a
        /// widget painted for the first time, or a subscription caught up. Ten
        /// frames is a sixth of a second — long enough to catch anything that
        /// arrives "shortly", far too short to trip a drill or a scan timer, which
        /// are the waits the curriculum genuinely asks the player to sit through.
        /// </para>
        /// <para>
        /// A reading step is exempt from the <i>report</i> here and only from that:
        /// its validator being true is precisely what makes it a reading, and the
        /// gate that stops it finishing lives in the director, where
        /// <c>TutorialLifecycleTests</c> asserts it over the whole catalog.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator NoWorldStepCompletesWhileThePlayerDoesNothing()
        {
            RequireWorld();
            var failures = new List<string>();

            foreach (string stepId in WorldSteps)
            {
                Run run = Enter(stepId);
                bool reading = run.Definition != null
                    && run.Definition.Kind == TutorialStepKind.InformationAcknowledgement;

                for (int frame = 0; frame < IdleFrames; frame++)
                {
                    run.Runtime.Evaluate(run.Report);
                    if (run.Report.IsComplete && !reading)
                    {
                        failures.Add($"{stepId} completed on its own {frame + 1} frame(s) after Prepare");
                        break;
                    }
                    yield return null;
                }

                DisposeScope();
                yield return null;
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Frames a step is left alone for before it is allowed to be called honest.</summary>
        private const int IdleFrames = 10;

        [UnityTest]
        public IEnumerator EveryWorldStepCompletesOnItsRealCommand()
        {
            RequireWorld();
            var failures = new List<string>();

            foreach (string stepId in WorldSteps)
            {
                if (!HasPositiveDriver(stepId)) continue;
                Run run = Enter(stepId);
                yield return DrivePositive(stepId, run);
                run.Runtime.Evaluate(run.Report);
                if (!run.Report.IsComplete) failures.Add($"{stepId} did not complete on its real command");
                DisposeScope();
                yield return null;
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [UnityTest]
        public IEnumerator NoWorldStepCompletesOnTheWrongAction()
        {
            RequireWorld();
            var failures = new List<string>();

            foreach (string stepId in WorldSteps)
            {
                Run run = Enter(stepId);
                yield return DriveNegative(stepId, run);
                run.Runtime.Evaluate(run.Report);
                if (run.Report.IsComplete) failures.Add($"{stepId} completed on an action that is not its own");
                DisposeScope();
                yield return null;
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// The whole point of the tutorial session: a run of every world step must
        /// leave the save path unreachable and no campaign identity minted.
        /// </summary>
        [UnityTest]
        public IEnumerator RunningEveryWorldStepNeverReachesTheSavePath()
        {
            RequireWorld();
            string identityBefore = CampaignIdentity.Current;

            foreach (string stepId in WorldSteps)
            {
                if (!HasPositiveDriver(stepId)) continue;
                Run run = Enter(stepId);
                yield return DrivePositive(stepId, run);
                DisposeScope();
                yield return null;
            }

            Assert.IsTrue(session.SaveTriggersSevered,
                "a tutorial step re-wired a RequestSave delegate or re-enabled the ExtractionDirector");
            Assert.AreEqual(identityBefore, CampaignIdentity.Current,
                "a tutorial step minted or adopted a campaign identity");
        }

        // =====================================================================
        // THE POSITIVE DRIVERS
        // =====================================================================

        private IEnumerator DrivePositive(string stepId, Run run)
        {
            switch (stepId)
            {
                case "A-14":
                    // Boost used to be the one flight action with no seam at all --
                    // `Input.GetKey(KeyCode.LeftShift)` read inline inside
                    // HandleTranslation -- so the old driver wrote both observables
                    // and evaluated before production could disagree. It now goes
                    // through ShipController.FlightInput, the same injectable source
                    // NebulaKeyCaptureListener has, and writes nothing at all: the
                    // controls are held, production reads them, production decides
                    // whether that counts as boosting, and production integrates the
                    // velocity that follows.
                    yield return HoldControls(forward: true, boost: true, frames: 600, run: run);
                    yield break;

                case "A-04":
                    // The same rotation Q/E drive, one frame at a time.
                    for (int i = 0; i < 400 && !run.Report.IsComplete; i++)
                    {
                        Ship.ApplyLook(0f, 0f, 1f);
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "A-05":
                    // The ramp is a real 8 seconds; the tutorial's own sanctioned
                    // clock compression is what makes waiting for it affordable.
                    session.SetTutorialTimeScale(TutorialSession.MaxTimeScale);
                    Ship.SetThrottleFraction(1f);
                    for (int i = 0; i < 900 && !run.Report.IsComplete; i++)
                    {
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    session.RestoreTimeScale();
                    break;

                case "A-06":
                    yield return FlyToPlanet(run, PlanetAt(0), 1.6f);
                    break;

                case "A-07":
                    yield return null;
                    Ship.StabilizeTo(PlanetAt(0).Body.transform);
                    break;

                case "A-08":
                    Ship.ReleaseStabilize();
                    break;

                case "A-09":
                    // NebulaHudVitals subscribes on its first paint, not in
                    // OnEnable, so the hit has to land after a frame has run.
                    yield return null;
                    Combat.ApplyDamage(40f);
                    yield return null;
                    break;

                case "A-10":
                    yield return FlyIntoTheCriticalStarBand();
                    for (int i = 0; i < 240 && !run.Report.IsComplete; i++)
                    {
                        run.Runtime.Evaluate(run.Report);
                        yield return new WaitForFixedUpdate();
                    }
                    break;

                case "A-11":
                    Menu().Open(NebulaUiAssetCatalog.Page.Discovery);
                    yield return null;
                    break;

                case "A-12":
                    yield return FlyToPlanet(run, PlanetAt(0), 1.6f);
                    // The driver refreshes on its own 0.25s cadence.
                    for (int i = 0; i < 60 && !run.Report.IsComplete; i++)
                    {
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "A-13":
                    Menu().Open(NebulaUiAssetCatalog.Page.Overview);
                    yield return null;
                    run.Runtime.Evaluate(run.Report);
                    Menu().Close();
                    yield return null;
                    break;

                case "B-01":
                    yield return FlyToPlanet(run, PlanetAt(1), 1.6f);
                    break;

                case "B-02":
                    yield return FlyToPlanet(run, PlanetAt(0), 1.1f);
                    // The whole scan, held: range gate, progress and the shared
                    // ExtractionState.CompletePlanetScan at the end. Held within one
                    // frame, because ShipExtraction's own Update reads the real key
                    // every frame and a released key resets the progress to zero --
                    // which is exactly the behaviour B-02's negative case relies on.
                    for (int i = 0; i < 30 && !run.Report.IsComplete; i++)
                    {
                        for (int held = 0; held < 24; held++) Extraction.TickScan(0.5f, scanHeld: true);
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "B-03":
                    for (int i = 0; i < 60 && !run.Report.IsComplete; i++)
                    {
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "B-04":
                    for (int i = 0; i < 40 && !run.Report.IsComplete; i++)
                    {
                        FirstPoi().State.TickLocate(1f, conditionMet: true, PoiCatalog.ScannerT1LocateSeconds);
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "B-05":
                    for (int i = 0; i < 40 && !run.Report.IsComplete; i++)
                    {
                        FirstPoi().State.TickIdentify(1f, conditionMet: true);
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "B-06":
                    yield return null;
                    for (int i = 0; i < NebulaDiscoveryPage.MaxSystemRows && !run.Report.IsComplete; i++)
                    {
                        Button row = FindButton("System " + i);
                        if (row == null) break;
                        row.onClick.Invoke();
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "B-07":
                    yield return null;   // ShipPoi.UpdateTarget runs in Update
                    PoiDriver.TryStartPrimaryAction();
                    break;

                case "B-08":
                    yield return DriveAnomalyToCompletion(run, inRange: true);
                    break;

                case "B-09":
                    // The real path: fly in and let PoiObject.TickWreckCollection run.
                    yield return FlyToPoi(PoiCatalog.IdentifyRange * 0.4f);
                    for (int i = 0; i < 240 && !run.Report.IsComplete; i++)
                    {
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "B-10":
                    yield return null;
                    PoiDriver.TryStartPrimaryAction();
                    break;

                case "B-11":
                    // A reading: the player's part is to look, and the HUD's own
                    // Update is what puts both cards up. Nothing here writes the
                    // observable -- the two link states come from the real query
                    // over state Prepare installed.
                    for (int i = 0; i < 120 && !run.Report.IsComplete; i++)
                    {
                        run.Runtime.Evaluate(run.Report);
                        yield return null;
                    }
                    break;

                case "K-11":
                    Click("OpenBindings");
                    Click(NebulaControlBindingSection.RowName(CombatAction.CombatBoost),
                        NebulaControlBindingSection.RebindControlName);
                    yield return PressThroughTheRealListener(FreeKeyFor(CombatAction.CombatBoost));
                    break;

                case "K-12":
                    // W belongs to flight and is permanently reserved.
                    yield return PressThroughTheRealListener(KeyCode.W);
                    break;

                default: throw new InvalidOperationException($"no positive driver for {stepId}");
            }
        }

        // =====================================================================
        // THE NEGATIVE DRIVERS
        // =====================================================================

        private IEnumerator DriveNegative(string stepId, Run run)
        {
            switch (stepId)
            {
                case "A-14":
                    // Ordinary forward thrust, with the boost control never touched.
                    // The ship converges on exactly its boost-free ceiling and the
                    // step must sit there refusing all of it -- which is the whole
                    // difference between "the ship is fast" and "the player boosted".
                    yield return HoldControls(forward: true, boost: false, frames: 240, run: null);
                    break;

                case "A-04":
                    // Yaw is not roll: the step asks for the ship's own axis.
                    for (int i = 0; i < 60; i++) { Ship.ApplyLook(6f, 0f, 0f); yield return null; }
                    break;

                case "A-05":
                    // Half throttle never reaches the 0.85 entry gate.
                    session.SetTutorialTimeScale(TutorialSession.MaxTimeScale);
                    Ship.SetThrottleFraction(0.5f);
                    for (int i = 0; i < 120; i++) yield return null;
                    session.RestoreTimeScale();
                    break;

                case "A-06":
                    // Flown away from the planet, not toward it.
                    yield return FlyToPlanet(run, PlanetAt(0), 8f);
                    break;

                case "A-07":
                    yield return null;
                    // Stabilised on nothing at all is not stabilised on the planet.
                    Ship.StabilizeTo(null);
                    break;

                case "A-08":
                    // Rolling is not releasing.
                    Ship.ApplyLook(0f, 0f, 1f);
                    yield return null;
                    break;

                case "A-09":
                    // Zero damage absorbs on no layer, so the HUD counts nothing.
                    yield return null;
                    Combat.ApplyDamage(0f);
                    yield return null;
                    break;

                case "A-10":
                    // Sitting outside every risk band accumulates no environmental heat.
                    for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
                    break;

                case "A-11":
                    // Opened, but left on the wrong page.
                    Menu().Open(NebulaUiAssetCatalog.Page.Overview);
                    yield return null;
                    break;

                case "A-12":
                    // Left outside the interest radius, so the bar stays hidden.
                    for (int i = 0; i < 40; i++) yield return null;
                    break;

                case "A-13":
                    // Opened and left open: coming back to flight is half the step.
                    Menu().Open(NebulaUiAssetCatalog.Page.Overview);
                    for (int i = 0; i < 5; i++) { run.Runtime.Evaluate(run.Report); yield return null; }
                    break;

                case "B-01":
                    // Flying deeper into planet A's sphere is not the handover.
                    yield return FlyToPlanet(run, PlanetAt(0), 1.2f);
                    break;

                case "B-02":
                    // The key released: ScanProgress resets and nothing completes.
                    yield return FlyToPlanet(run, PlanetAt(0), 1.1f);
                    for (int i = 0; i < 120; i++)
                    {
                        Extraction.TickScan(0.5f, scanHeld: false);
                        yield return null;
                    }
                    break;

                case "B-03":
                    // Out of range, so the bar hides instead of offering anything.
                    yield return FlyToPlanet(run, PlanetAt(0), 8f);
                    for (int i = 0; i < 40; i++) yield return null;
                    break;

                case "B-04":
                    for (int i = 0; i < 40; i++)
                    {
                        FirstPoi().State.TickLocate(1f, conditionMet: false, PoiCatalog.ScannerT1LocateSeconds);
                        yield return null;
                    }
                    break;

                case "B-05":
                    for (int i = 0; i < 40; i++)
                    {
                        FirstPoi().State.TickIdentify(1f, conditionMet: false);
                        yield return null;
                    }
                    break;

                case "B-06":
                    // Asking for an analysis without selecting anything.
                    yield return null;
                    Click("Analyze");
                    yield return null;
                    break;

                case "B-07":
                    yield return null;
                    // Cancelling is not starting.
                    PoiDriver.TryCancelActiveOperation();
                    break;

                case "B-08":
                    yield return DriveAnomalyToCompletion(run, inRange: false);
                    break;

                case "B-09":
                    // Held outside IdentifyRange: nothing is ever withdrawn.
                    for (int i = 0; i < 120; i++) yield return null;
                    break;

                case "B-10":
                    yield return null;
                    // Cancelling leaves the choice unlocked, which is the point.
                    PoiDriver.TryCancelActiveOperation();
                    break;

                case "B-11":
                {
                    // One card is not the lesson. Dropping the distant system below
                    // Visited takes it out of the HUD's candidate list entirely, so
                    // the player is left looking at a Live card with nothing to
                    // compare it against -- which is what the step must refuse. The
                    // step's own Prepare captured the whole knowledge map, so this
                    // is put back at cleanup.
                    NavigationDirector navigation = session.Gameplay.NavigationDirector;
                    string distant = DistantVisitedSystem(navigation);
                    if (distant != null)
                        navigation.Knowledge.ForceLevel(distant, SystemKnowledgeLevel.Located);
                    for (int i = 0; i < 120; i++) yield return null;
                    break;
                }

                case "K-11":
                    Click("OpenBindings");
                    Click(NebulaControlBindingSection.RowName(CombatAction.CombatBoost),
                        NebulaControlBindingSection.RebindControlName);
                    // A reserved key: refused, so no binding moves.
                    yield return PressThroughTheRealListener(KeyCode.W);
                    break;

                case "K-12":
                    // A free key is accepted, which is not the refusal this step teaches.
                    yield return PressThroughTheRealListener(FreeKeyFor(CombatAction.CombatBoost));
                    break;

                default: throw new InvalidOperationException($"no negative driver for {stepId}");
            }
        }

        // =====================================================================
        // B-11: WHY THE LAST-KNOWN FLIP CANNOT BE OBSERVED HERE
        // =====================================================================

        /// <summary>
        /// B-11's premise is that flying out of a system makes its attention card
        /// fade to "SON BİLİNEN". It does not, because leaving a system leaves the
        /// ship inside no system at all — <c>NavigationDirector.CurrentSystemId</c>
        /// goes null rather than becoming another system's id.
        /// <para>
        /// This drives <c>SystemStatusQuery.Link</c> itself, and that is the whole
        /// point of the rewrite. The previous version asserted that
        /// <c>TutorialPoi.CardIsLastKnown</c> did not throw — but that helper
        /// returns early on a null current system id <i>before</i> it reaches
        /// production, so the assertion was guarding a call that could not throw and
        /// stayed green for the entire life of the crash it was supposed to catch.
        /// It tested the wrapper, not the game.
        /// </para>
        /// <para>
        /// What production now says is nameable and is asserted here: with the ship
        /// between systems nothing can be Live, so every target falls to
        /// <c>LastKnown</c> where a snapshot really exists and <c>Disconnected</c>
        /// where it does not. Both premises are asserted before the answer is, so a
        /// world that stopped carrying a snapshot fails loudly instead of passing
        /// vacuously.
        /// </para>
        /// <para>
        /// Deliberately run with no <c>NebulaHudAlerts</c> alive: production's own
        /// <c>RefreshAttention</c> feeds every Visited system through this same query
        /// every frame, so this is the cheap, isolated version of what the HUD does.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator InterstellarSpaceIsResolvedAsLastKnownOrDisconnected()
        {
            RequireWorld();
            NavigationDirector navigation = session.Gameplay.NavigationDirector;

            Vector3 startedAt = Ship.transform.position;
            try
            {
                string startedIn = navigation.CurrentSystemId;
                Assert.IsNotNull(startedIn,
                    "the tutorial world must start with the ship inside a system, or B-11 has no Live state to begin from");

                yield return LeaveTheCurrentSystem();

                Assert.IsNull(navigation.CurrentSystemId,
                    "B-11 assumes leaving a system means arriving in another one; if that ever becomes true, " +
                    "the step can be reinstated with a positive driver");

                // --- the system the ship just left: real data, so LastKnown -----
                Assert.IsTrue(SystemStatusQuery.HasValidSnapshot(startedIn),
                    $"premise: {startedIn} must still carry a snapshot after the ship left it, or the " +
                    "LastKnown assertion below would prove nothing");

                Assert.AreEqual(SystemLinkState.LastKnown,
                    SystemStatusQuery.Link(startedIn, null, navigation.Knowledge),
                    "a ship between systems has no Command to originate a relay from, so a target it has " +
                    "real data for is last-known -- never Live, and never guessed away as Disconnected");

                // --- a system nobody has ever been near: no data, so Disconnected -
                string neverSeen = WorldIdCodec.SystemId(new SectorCoordinate(97, 97, 97), 0);
                Assert.IsFalse(SystemStatusQuery.HasValidSnapshot(neverSeen),
                    $"premise: {neverSeen} must have no snapshot, or the Disconnected assertion proves nothing");

                Assert.AreEqual(SystemLinkState.Disconnected,
                    SystemStatusQuery.Link(neverSeen, null, navigation.Knowledge),
                    "a system with no snapshot at all must be Disconnected rather than reported as stale data");

                // --- the id that used to be a dictionary key with no null check --
                SystemManifest resolvedFromNull = null;
                Assert.DoesNotThrow(() => resolvedFromNull = StarSystemRegistry.Manifests.ResolveSystem(null),
                    "ResolveSystem still throws on a null id, which is the per-frame crash NebulaHudAlerts hit " +
                    "on every interstellar crossing");
                Assert.IsNull(resolvedFromNull);

                Assert.AreEqual(SystemLinkState.Disconnected,
                    SystemStatusQuery.Link(null, null, navigation.Knowledge),
                    "a target with no id must not coincidentally match a null ship id and read as Live");

                // --- and B-11's own guard still refuses to guess ------------------
                Assert.IsNull(TutorialPoi.CardIsLastKnown(startedIn, navigation),
                    "with the ship inside no system every card resolves last-known, so the Live/LastKnown " +
                    "contrast B-11 teaches does not exist and the step must not claim it does");
            }
            finally
            {
                Ship.SetVelocity(Vector3.zero);
                Ship.transform.position = startedAt;
            }
        }

        // =====================================================================
        // A-14: THE BOOST SEAM
        // =====================================================================

        /// <summary>
        /// A test-held set of flight controls. It says only what the player is
        /// holding: there is no speed here and no way to make the ship fast. Whether
        /// a held boost control means anything at all is still the ship's decision,
        /// taken from cruise state, throttle and its own commanded-speed formula.
        /// </summary>
        private sealed class HeldFlightControls : IShipFlightInput
        {
            internal Vector3 Request;
            internal bool Boost;

            public Vector3 Thrust => Request;
            public bool AnyThrustHeld => Request.sqrMagnitude > 0f;
            public bool BoostHeld => Boost;
        }

        /// <summary>What production made of the controls on the last frame they were held.</summary>
        private bool heldBoosting;

        private float heldSpeed;

        /// <summary>
        /// Holds a set of controls for real frames and lets production do the rest.
        /// Nothing here writes velocity, throttle or <c>Boosting</c>: the loop only
        /// holds controls down, exactly as a player's hand does, and reads back what
        /// the ship decided.
        /// <para>
        /// The keyboard goes back the moment the hold is over. Leaving a held boost
        /// control installed would fly the ship at four times its top speed through
        /// every step that runs after this one in the same sweep.
        /// </para>
        /// </summary>
        private IEnumerator HoldControls(bool forward, bool boost, int frames, Run run)
        {
            var controls = new HeldFlightControls
            {
                Request = forward ? Vector3.forward : Vector3.zero,
                Boost = boost,
            };
            ShipController.FlightInput = controls;

            for (int i = 0; i < frames; i++)
            {
                if (run != null)
                {
                    run.Runtime.Evaluate(run.Report);
                    if (run.Report.IsComplete) break;
                }
                yield return null;
            }

            // Read before the source is dropped, and before any further frame could
            // recompute the flag from an empty keyboard.
            heldBoosting = Ship.Boosting;
            heldSpeed = Ship.Velocity.magnitude;

            ShipController.ResetFlightInputToKeyboard();
        }

        /// <summary>
        /// The four cases that together say what "the player boosted" means.
        /// <para>
        /// The flag is no longer writable from this assembly at all — it is private
        /// to <c>ShipController</c> and recomputed from
        /// <c>ShipController.FlightInput</c> every frame — so the only way this
        /// fixture can produce it is by holding the boost control on the same seam
        /// the keyboard sits behind. What is left to prove is that the step tells
        /// apart a real boost, ordinary thrust, a speed somebody simply wrote onto
        /// the ship, and a boost the game refused.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator BoostCompletesOnlyOnARealRefusedNowhereElse()
        {
            RequireWorld();

            // 1. The real thing: the controls held, and nothing else touched.
            Run real = Enter("A-14");
            float ceiling = BoostFreeCeiling();
            yield return HoldControls(forward: true, boost: true, frames: 600, run: real);
            bool realPassed = real.Report.IsComplete;
            bool productionSawTheBoost = heldBoosting;
            float reached = heldSpeed;
            DisposeScope();
            yield return null;

            // 2. Ordinary thrust at the same throttle. The ship converges on its
            //    boost-free ceiling and never passes it.
            Run thrustOnly = Enter("A-14");
            yield return HoldControls(forward: true, boost: false, frames: 240, run: null);
            thrustOnly.Runtime.Evaluate(thrustOnly.Report);
            bool thrustOnlyPassed = thrustOnly.Report.IsComplete;
            DisposeScope();
            yield return null;

            // 3. A speed nobody flew to. SetVelocity is production's save/restore
            //    seam, and a step that accepted it would be accepting a number
            //    rather than an action.
            Run speedOnly = Enter("A-14");
            Ship.SetVelocity(Ship.transform.forward * (BoostFreeCeiling() * 2f));
            speedOnly.Runtime.Evaluate(speedOnly.Report);
            bool speedOnlyPassed = speedOnly.Report.IsComplete;
            DisposeScope();
            yield return null;

            // 4. The controls held and the game refusing: with the world's input
            //    gate shut -- a screen open over the top of the flight -- the ship
            //    reads nothing, so a held boost control produces no boost at all.
            Run refused = Enter("A-14");
            InputGate.Suppressed = true;
            yield return HoldControls(forward: true, boost: true, frames: 120, run: null);
            bool refusedBoosting = heldBoosting;
            refused.Runtime.Evaluate(refused.Report);
            bool refusedPassed = refused.Report.IsComplete;
            InputGate.Suppressed = false;
            DisposeScope();
            yield return null;

            Assert.IsTrue(productionSawTheBoost,
                "production never set Boosting while the boost control was held -- the seam is not wired to the ship");
            Assert.Greater(reached, ceiling,
                $"holding the controls only reached {reached:0} against a boost-free ceiling of {ceiling:0}; " +
                "production did not actually apply the boost multiplier");
            Assert.IsTrue(realPassed, "a real, production-driven boost did not complete the step");

            Assert.IsFalse(thrustOnlyPassed,
                "ordinary forward thrust at full throttle completed the boost step");
            Assert.IsFalse(speedOnlyPassed,
                "a speed written straight onto the ship completed the boost step");
            Assert.IsFalse(refusedBoosting,
                "the ship reported boosting while the world's input gate was shut");
            Assert.IsFalse(refusedPassed,
                "a boost the game refused completed the step");
        }

        /// <summary>
        /// The seam's own contract: the keyboard is the default, and clearing it
        /// puts the keyboard back rather than leaving a ship that reads nothing.
        /// A test that forgot its teardown would otherwise leave every later fixture
        /// flying on a dead source.
        /// </summary>
        [Test]
        public void TheFlightInputSeamDefaultsToTheKeyboardAndCannotBeLeftEmpty()
        {
            IShipFlightInput shipped = ShipController.FlightInput;
            Assert.IsNotNull(shipped, "the ship has no flight input source at all");

            ShipController.FlightInput = new HeldFlightControls { Boost = true };
            Assert.IsTrue(ShipController.FlightInput.BoostHeld, "the seam did not take the injected source");

            ShipController.FlightInput = null;
            Assert.IsNotNull(ShipController.FlightInput,
                "assigning null left the ship with no controls instead of restoring the keyboard");
            Assert.AreEqual(shipped.GetType(), ShipController.FlightInput.GetType(),
                "clearing the seam did not restore the shipping keyboard source");

            ShipController.ResetFlightInputToKeyboard();
            Assert.AreEqual(shipped.GetType(), ShipController.FlightInput.GetType());
        }

        /// <summary>
        /// The fastest this ship can legitimately go without boosting, from the
        /// game's own commanded-speed formula with the boost factor left at 1 —
        /// the same number A-14 measures in its own Prepare.
        /// </summary>
        private float BoostFreeCeiling() =>
            FlightTestSpeedMath.CommandedSpeed(Ship.Throttle, 1f, Ship.CruiseSpeedMultiplier, Ship.TestSpeedMultiplier)
            * Ship.CombatBoostSpeedMultiplier * Ship.DamagedSpeedMultiplier;

        // =====================================================================
        // MOVEMENT AND WORLD HELPERS
        // =====================================================================

        /// <summary>
        /// Flies to a multiple of the planet's radius through
        /// <c>ShipController.TestApplyDisplacement</c> -- the production
        /// collision-resolved movement step, never a teleport.
        /// <para>
        /// The centre is re-read every iteration, and the distance is re-established
        /// once more after the loop, because a real streamed planet is
        /// <c>movedByGravity: true</c> (<c>Planet.cs:16</c>) and is orbiting the whole
        /// time this runs. Snapshotting it once and then holding the ship a fixed
        /// offset from that snapshot does not hold the ship at N radii from the
        /// planet — it holds the ship STILL while the planet closes on it, and the
        /// step's own predicate reads the live distance
        /// (<c>ShipExtraction.UpdateTarget</c> targets any planet inside
        /// <c>Radius * 3</c>). The negative drivers are the ones that broke on it:
        /// A-06/B-02 ask for "flown away, not toward", drive at 8 radii, and then
        /// evaluate on every one of up to 400 frames while the planet walks back
        /// inside three — and <c>TutorialStepReport.IsComplete</c> latches
        /// (<c>TutorialContracts.cs:196</c>), so one such frame anywhere in the drive
        /// is permanent. It reported as "A-06 completed on an action that is not its
        /// own", which was true of the driver and not of the step. How many frames
        /// the loop runs, and how far the planet gets in them, both depend on the
        /// residual throttle and orbital phase the previous leaf happened to leave —
        /// which is why it only ever failed in a full-process run.
        /// </para>
        /// </summary>
        private IEnumerator FlyToPlanet(Run run, PlanetIdentity planet, float radiiFromCentre)
        {
            Assert.IsNotNull(planet, "the step did not install a planet to fly to");

            for (int i = 0; i < 400; i++)
            {
                if (!StepTowardsOrbitOf(planet, radiiFromCentre)) break;
                run?.Runtime.Evaluate(run.Report);
                yield return null;
            }

            // The trailing frame is where the caller's own Evaluate lands, so the
            // distance has to be true on it too rather than one frame stale.
            StepTowardsOrbitOf(planet, radiiFromCentre);
            yield return null;
            StepTowardsOrbitOf(planet, radiiFromCentre);
        }

        /// <summary>Moves the ship one collision-resolved step towards the live N-radii shell. False once it is already there.</summary>
        private bool StepTowardsOrbitOf(PlanetIdentity planet, float radiiFromCentre)
        {
            if (planet == null || planet.Body == null) return false;
            Vector3 centre = planet.Body.transform.position;
            Vector3 outward = Ship.transform.position - centre;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;

            Vector3 target = centre + outward.normalized * (planet.Body.Radius * radiiFromCentre);
            Vector3 delta = target - Ship.transform.position;
            if (delta.magnitude < 1f) return false;

            Ship.TestApplyDisplacement(Vector3.ClampMagnitude(delta, Mathf.Max(5000f, planet.Body.Radius * 4f)));
            return true;
        }

        private IEnumerator FlyToPoi(float distance)
        {
            PoiObject poi = FirstPoi();
            Vector3 target = poi.transform.position
                             + (Ship.transform.position - poi.transform.position).normalized * distance;
            for (int i = 0; i < 200; i++)
            {
                Vector3 delta = target - Ship.transform.position;
                if (delta.magnitude < 1f) break;
                Ship.TestApplyDisplacement(Vector3.ClampMagnitude(delta, 400f));
                yield return null;
            }
            yield return null;
        }

        private IEnumerator FlyIntoTheCriticalStarBand()
        {
            if (!StarSystemRegistry.TryGet(StarSystemRegistry.StartSystemId, out StarSystemRecord record))
                yield break;

            float critical = record.StarRadius * StarClassCatalog.CriticalRadiusMultiplier(record.StarClass);
            Vector3 shipAbsolute = Ship.transform.position + FloatingOrigin.TotalOffset;
            Vector3 direction = shipAbsolute - record.AbsoluteCenter;
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.forward;

            Vector3 targetAbsolute = record.AbsoluteCenter + direction.normalized * (critical * 0.85f);
            Vector3 targetLocal = targetAbsolute - FloatingOrigin.TotalOffset;

            for (int i = 0; i < 400; i++)
            {
                Vector3 delta = targetLocal - Ship.transform.position;
                if (delta.magnitude < 5f) break;
                Ship.TestApplyDisplacement(Vector3.ClampMagnitude(delta, Mathf.Max(20000f, record.StarRadius)));
                yield return null;
            }
            yield return null;
        }

        private IEnumerator LeaveTheCurrentSystem()
        {
            NavigationDirector navigation = session.Gameplay.NavigationDirector;
            string startedIn = navigation.CurrentSystemId;
            if (startedIn == null || !StarSystemRegistry.TryGet(startedIn, out StarSystemRecord record))
                yield break;

            Vector3 shipAbsolute = Ship.transform.position + FloatingOrigin.TotalOffset;
            Vector3 away = shipAbsolute - record.AbsoluteCenter;
            if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
            Vector3 targetLocal = record.AbsoluteCenter + away.normalized * (record.NavigationRadius * 1.6f)
                                  - FloatingOrigin.TotalOffset;

            for (int i = 0; i < 400 && navigation.CurrentSystemId == startedIn; i++)
            {
                Vector3 delta = targetLocal - Ship.transform.position;
                if (delta.magnitude < 5f) break;
                Ship.TestApplyDisplacement(Vector3.ClampMagnitude(delta, Mathf.Max(2000f, record.NavigationRadius * 0.2f)));
                yield return null;
            }
            yield return null;
        }

        /// <summary>
        /// Drives the anomaly exactly the way <c>PoiObject.TickAnomaly</c> does:
        /// the real timer, then the real completion service on the tick that
        /// crosses it.
        /// </summary>
        private IEnumerator DriveAnomalyToCompletion(Run run, bool inRange)
        {
            PoiObject poi = FirstPoi();
            NavigationDirector navigation = session.Gameplay.NavigationDirector;
            float required = AnomalyService.RequiredSeconds(byScout: false, poi.Manifest.Risk);

            for (int i = 0; i < 120 && !run.Report.IsComplete; i++)
            {
                if (poi.State.TickOperation(1f, inRange, required, PoiPauseReason.OutOfRange))
                {
                    Vector3 fromAbsolute = Ship.transform.position + FloatingOrigin.TotalOffset;
                    List<(string systemId, float distance)> candidates =
                        PoiCandidateBuilder.NearestUnknown(fromAbsolute, navigation.Knowledge);
                    AnomalyService.TryComplete(poi.State, poi.Manifest.PoiId, candidates,
                        navigation.Knowledge, navigation.FrontierSignal);
                }
                run.Runtime.Evaluate(run.Report);
                yield return null;
            }
        }

        /// <summary>
        /// A real key press, delivered the way a player's is: through the injectable
        /// sample source the capture listener reads every frame. The listener's own
        /// Update, the section's Apply and the binding validator all run unchanged.
        /// </summary>
        private IEnumerator PressThroughTheRealListener(KeyCode key)
        {
            var source = new OneKeySource(key);
            NebulaKeyCaptureListener.Source = source;
            yield return null;
            yield return null;
            NebulaKeyCaptureListener.ResetSourceToKeyboard();
            yield return null;
        }

        private sealed class OneKeySource : IKeyInputSource
        {
            private readonly KeyCode pressed;
            internal OneKeySource(KeyCode key) { pressed = key; }
            public bool GetKeyDown(KeyCode key) => key == pressed;
        }

        private static KeyCode FreeKeyFor(CombatAction action)
        {
            foreach (KeyCode candidate in (KeyCode[])Enum.GetValues(typeof(KeyCode)))
                if (NebulaControlBindings.Check(action, candidate) == ControlRebindResult.Accepted)
                    return candidate;
            return KeyCode.None;
        }

        // ---- live objects the step installed -----------------------------------

        private ShipController Ship => session.Gameplay.Ship;
        private ShipCombat Combat => Ship.GetComponent<ShipCombat>();
        private ShipExtraction Extraction => Ship.GetComponent<ShipExtraction>();
        private ShipPoi PoiDriver => Ship.GetComponent<ShipPoi>();

        /// <summary>
        /// The same planet <c>TutorialAbk.EnsurePlanet</c> hands the step: the live
        /// registry, ordered by world id.
        /// </summary>
        private static PlanetIdentity PlanetAt(int index)
        {
            var planets = new List<PlanetIdentity>();
            foreach (PlanetIdentity planet in ExtractionState.AllPlanets)
                if (planet != null && planet.Body != null) planets.Add(planet);
            planets.Sort((a, b) => string.CompareOrdinal(a.WorldId, b.WorldId));
            return index < planets.Count ? planets[index] : null;
        }

        /// <summary>The Visited system the ship is not in — the one B-11's LastKnown card is about.</summary>
        private static string DistantVisitedSystem(NavigationDirector navigation)
        {
            string here = navigation.CurrentSystemId;
            foreach (KeyValuePair<string, SystemKnowledgeLevel> entry in navigation.Knowledge.All)
                if (entry.Value == SystemKnowledgeLevel.Visited && entry.Key != here) return entry.Key;
            return null;
        }

        private static PoiObject FirstPoi()
        {
            foreach (PoiObject poi in PoiObject.All)
                if (poi != null && poi.State != null) return poi;
            Assert.Fail("the step did not install a POI");
            return null;
        }

        private static NebulaParentMenuScreen Menu()
        {
            foreach (GameObject root in SceneRoots())
            {
                NebulaParentMenuScreen menu = root.GetComponentInChildren<NebulaParentMenuScreen>(true);
                if (menu != null) return menu;
            }
            Assert.Fail("the step did not build a parent menu");
            return null;
        }

        private static void Click(string controlName)
        {
            Button button = FindButton(controlName);
            Assert.IsNotNull(button, $"no control named '{controlName}' is on screen");
            button.onClick.Invoke();
        }

        private static void Click(string rowName, string controlName)
        {
            Transform row = FindByName(rowName);
            Assert.IsNotNull(row, $"no row named '{rowName}' is on screen");
            foreach (Button button in row.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == controlName) { button.onClick.Invoke(); return; }
            Assert.Fail($"row '{rowName}' has no control named '{controlName}'");
        }

        private static Button FindButton(string controlName)
        {
            foreach (GameObject root in SceneRoots())
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                    if (button.gameObject.name == controlName) return button;
            return null;
        }

        private static Transform FindByName(string name)
        {
            foreach (GameObject root in SceneRoots())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.gameObject.name == name) return child;
            return null;
        }

        private static IEnumerable<GameObject> SceneRoots() =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        // ---- the harness --------------------------------------------------------

        private sealed class Run
        {
            internal ITutorialStepRuntime Runtime;
            internal TutorialStepReport Report;

            /// <summary>The catalog row, so a sweep can ask what kind of step it is holding.</summary>
            internal TutorialStepDefinition Definition;
        }

        private void RequireWorld()
        {
            if (compositionError != null)
                Assert.Fail("the tutorial world could not be composed: " + compositionError);
            Assert.IsNotNull(session, "the tutorial world was not composed");
        }

        private Run Enter(string stepId)
        {
            TutorialStepDefinition definition = TutorialStepCatalog.Find(stepId);
            Assert.IsNotNull(definition, $"step '{stepId}' is not registered");

            scope = TutorialStepScope.Create(stepId, null);
            var world = new TutorialWorldFactory(() => scope, session.Gameplay);
            var context = new TutorialStepContext(session.Gameplay, scope, TutorialRunMode.Standalone, world);

            var report = new TutorialStepReport();
            report.Reset(definition.ChecklistTr.Count);

            ITutorialStepRuntime runtime = definition.CreateRuntime();
            runtime.Prepare(context);

            Assert.IsEmpty(world.Blockers,
                $"{stepId} could not install a real asset: " + string.Join("; ", world.Blockers));

            return new Run { Runtime = runtime, Report = report, Definition = definition };
        }

        private void DisposeScope()
        {
            if (scope == null) return;
            TutorialStepScope doomed = scope;
            scope = null;
            doomed.Dispose();
            Assert.IsEmpty(doomed.CleanupFailures,
                "a step's cleanup threw: " + string.Join("; ", doomed.CleanupFailures));
        }
    }
}
