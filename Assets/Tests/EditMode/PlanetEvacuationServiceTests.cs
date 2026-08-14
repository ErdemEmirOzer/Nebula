using NUnit.Framework;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for physical evacuation/reclaim preconditions and the
    /// Abandoned overlay flag on <see cref="PlanetDominance"/> (VS-010 §5/§10.10).
    /// Vehicle-recall-wait and cancel-active-builds require live
    /// DrillStructure/SurfaceStructure/VehicleAgent objects and are covered
    /// by the manual smoke checklist instead (see final report).
    /// </summary>
    public class PlanetEvacuationServiceTests
    {
        [SetUp]
        public void Reset() => ExtractionState.Reset(0);

        [Test]
        public void CheckPreconditions_ChecksInDocumentedOrder()
        {
            Assert.AreEqual(EvacuationRejectReason.AlreadyInProgress,
                PlanetEvacuationService.CheckPreconditions(true, false, true, true, false, false));
            Assert.AreEqual(EvacuationRejectReason.AlreadyAbandoned,
                PlanetEvacuationService.CheckPreconditions(false, true, true, true, false, false));
            Assert.AreEqual(EvacuationRejectReason.NotInRange,
                PlanetEvacuationService.CheckPreconditions(false, false, false, true, false, false));
            Assert.AreEqual(EvacuationRejectReason.NotStabilized,
                PlanetEvacuationService.CheckPreconditions(false, false, true, false, false, false));
            Assert.AreEqual(EvacuationRejectReason.ThreatDetected,
                PlanetEvacuationService.CheckPreconditions(false, false, true, true, true, false));
            Assert.AreEqual(EvacuationRejectReason.PlanetContested,
                PlanetEvacuationService.CheckPreconditions(false, false, true, true, false, true));
            Assert.AreEqual(EvacuationRejectReason.None,
                PlanetEvacuationService.CheckPreconditions(false, false, true, true, false, false));
        }

        [Test]
        public void CheckReclaimPreconditions_RequiresAbandonedThenDominanceConditions()
        {
            Assert.AreEqual(ReclaimRejectReason.NotAbandoned, PlanetEvacuationService.CheckReclaimPreconditions(false, true));
            Assert.AreEqual(ReclaimRejectReason.DominanceConditionsNotMet, PlanetEvacuationService.CheckReclaimPreconditions(true, false));
            Assert.AreEqual(ReclaimRejectReason.None, PlanetEvacuationService.CheckReclaimPreconditions(true, true));
        }

        [Test]
        public void Passivate_SetsAbandoned_WithoutTouchingStateOrTimers()
        {
            const string worldId = "sector.0.0.0/system.000/planet.00";
            PlanetDominance dom = ExtractionState.GetDominance(worldId);
            dom.Tick(0.001f, new DominanceConditions { Scanned = true, ActiveDrill = true, ActiveCommand = true, ActiveDefense = true });
            // Force it to Controlled deterministically via repeated ticks (120s worth).
            for (int i = 0; i < 130; i++)
                dom.Tick(1f, new DominanceConditions { Scanned = true, ActiveDrill = true, ActiveCommand = true, ActiveDefense = true });
            Assert.AreEqual(DominanceState.Controlled, dom.State);

            PlanetEvacuationService.Passivate(worldId);

            Assert.IsTrue(dom.Abandoned);
            Assert.AreEqual(DominanceState.Controlled, dom.State, "the underlying state is frozen, not reset, by Passivate alone");
        }

        [Test]
        public void Abandoned_SuspendsProduction_LikeContestedOrLost()
        {
            var dom = new PlanetDominance();
            Assert.IsFalse(dom.ProductionSuspended);
            dom.SetAbandoned(true);
            Assert.IsTrue(dom.ProductionSuspended);
        }

        [Test]
        public void Abandoned_FreezesTick_StateAndTimersNeverAdvanceWhileSet()
        {
            var dom = new PlanetDominance();
            dom.SetAbandoned(true);
            var allMet = new DominanceConditions { Scanned = true, ActiveDrill = true, ActiveCommand = true, ActiveDefense = true };
            for (int i = 0; i < 200; i++) dom.Tick(1f, allMet);

            Assert.AreEqual(DominanceState.Unowned, dom.State);
            Assert.AreEqual(0f, dom.StabilizeTimer);
        }

        [Test]
        public void Reclaim_ClearsAbandoned_AndForcesAColdRestart_RequiringAFreshOneTwentySecondStabilization()
        {
            var dom = new PlanetDominance();
            dom.Restore(DominanceState.Controlled, 0f, 0f, abandoned: true);

            dom.Reclaim();

            Assert.IsFalse(dom.Abandoned);
            Assert.AreEqual(DominanceState.Unowned, dom.State);
            Assert.AreEqual(0f, dom.StabilizeTimer);

            // The very next tick must behave exactly like a first-time acquisition — 120s, not instant.
            var allMet = new DominanceConditions { Scanned = true, ActiveDrill = true, ActiveCommand = true, ActiveDefense = true };
            dom.Tick(119f, allMet);
            Assert.AreEqual(DominanceState.Developing, dom.State, "must still be stabilizing just before 120s");
            dom.Tick(2f, allMet);
            Assert.AreEqual(DominanceState.Controlled, dom.State);
        }

        [Test]
        public void EvacuationLock_BlocksAndUnblocksIndependentlyPerPlanet()
        {
            const string a = "planet.A", b = "planet.B";
            Assert.IsFalse(ExtractionState.IsEvacuationLocked(a));
            ExtractionState.SetEvacuationLocked(a, true);
            Assert.IsTrue(ExtractionState.IsEvacuationLocked(a));
            Assert.IsFalse(ExtractionState.IsEvacuationLocked(b));
            ExtractionState.SetEvacuationLocked(a, false);
            Assert.IsFalse(ExtractionState.IsEvacuationLocked(a));
        }

        [Test]
        public void EvacuationAndReclaim_NeverAwardXp()
        {
            Progression.ProgressionService.Reset();
            const string worldId = "sector.0.0.0/system.000/planet.00";
            var dom = ExtractionState.GetDominance(worldId);
            dom.Restore(DominanceState.Controlled, 0f, 0f);

            PlanetEvacuationService.Passivate(worldId);
            PlanetEvacuationService.Reclaim(worldId);
            PlanetEvacuationService.Passivate(worldId);
            PlanetEvacuationService.Reclaim(worldId);

            Assert.AreEqual(0L, Progression.ProgressionService.State.TotalXp, "evacuation/reclaim/re-Presence must never produce XP, even replayed");
        }
    }
}
