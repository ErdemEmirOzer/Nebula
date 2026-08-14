using System.Collections.Generic;
using System.Reflection;
using Nebula.Modules;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Guards NavigationDirector's save-on-real-transition contract for the
    /// signal-analysis and route commands. Rejected and idempotent command
    /// paths must remain side-effect free so repeated UI input cannot generate
    /// redundant saves, and a real transition (start / cancel / route set /
    /// route cleared) must request exactly one — those are the only moments
    /// NavigationDto's signalAnalysisTargetSystemId, its remaining seconds and
    /// selectedRouteSystemId ever reach disk.
    /// </summary>
    public class NavigationDirectorPersistenceTests
    {
        /// <summary>Same seed style as Vs017aRouteHarnessTests: a fixed universe, so every id below is identical on every run and platform.</summary>
        private const ulong Seed = 701UL;

        private GameObject root;
        private NavigationDirector director;
        private ShipModules modules;
        private int saveRequests;

        // Three systems this seed's galaxy really places — never ids built by hand.
        private string signalId;
        private string locatedId;
        private string otherSignalId;

        [SetUp]
        public void SetUp()
        {
            saveRequests = 0;

            List<string> real = RealSystemIds(Seed, 3);
            signalId = real[0];
            locatedId = real[1];
            otherSignalId = real[2];

            root = new GameObject("NavigationDirectorPersistenceTests");
            ShipController ship = root.AddComponent<ShipController>();
            modules = root.AddComponent<ShipModules>();
            RunAwake(modules);
            director = root.AddComponent<NavigationDirector>();
            director.Initialize(ship, null);
            director.RequestSave = () => saveRequests++;
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        // ==================== analysis ====================

        [Test]
        public void TryStartAnalysis_Success_RequestsSaveExactlyOnce()
        {
            modules.Modules.SetTier("exploration.scanner", 1);
            director.Knowledge.ForceLevel(signalId, SystemKnowledgeLevel.Signal);

            bool accepted = director.TryStartAnalysis(signalId, out string rejectReason);

            Assert.IsTrue(accepted, rejectReason);
            Assert.IsTrue(director.Analysis.Active);
            Assert.AreEqual(signalId, director.Analysis.TargetSystemId);
            Assert.AreEqual(1, saveRequests);
        }

        [Test]
        public void TryStartAnalysis_RejectedPaths_DoNotRequestSave()
        {
            director.Knowledge.ForceLevel(signalId, SystemKnowledgeLevel.Signal);
            director.Knowledge.ForceLevel(locatedId, SystemKnowledgeLevel.Located);
            director.Knowledge.ForceLevel(otherSignalId, SystemKnowledgeLevel.Signal);

            Assert.AreEqual(0, director.ScannerTier, "the scanner gate below is only meaningful while no scanner is owned");
            Assert.IsFalse(director.TryStartAnalysis(signalId, out _), "scanner gate must reject");
            Assert.AreEqual(0, saveRequests, "scanner rejection is not a state transition");

            modules.Modules.SetTier("exploration.scanner", 1);
            Assert.AreEqual(1, director.ScannerTier, "the tier the module state was just given must be the one the gate reads");
            Assert.IsFalse(director.TryStartAnalysis(locatedId, out _), "non-signal target must reject");
            Assert.AreEqual(0, saveRequests, "knowledge-level rejection is not a state transition");

            Assert.IsTrue(director.TryStartAnalysis(signalId, out string acceptedReject), acceptedReject);
            saveRequests = 0;

            Assert.IsFalse(director.TryStartAnalysis(otherSignalId, out _), "an active analysis must reject another start");
            Assert.AreEqual(0, saveRequests, "active-analysis rejection must not request another save");
            Assert.AreEqual(signalId, director.Analysis.TargetSystemId, "a rejected start must not retarget the running analysis");
        }

        [Test]
        public void CancelAnalysis_RealTransitionRequestsOnce_RepeatedCancelIsNoOp()
        {
            modules.Modules.SetTier("exploration.scanner", 1);
            director.Knowledge.ForceLevel(signalId, SystemKnowledgeLevel.Signal);
            Assert.IsTrue(director.TryStartAnalysis(signalId, out string rejectReason), rejectReason);
            saveRequests = 0;

            director.CancelAnalysis();

            Assert.IsFalse(director.Analysis.Active);
            Assert.AreEqual(1, saveRequests);

            director.CancelAnalysis();

            Assert.AreEqual(1, saveRequests, "cancelling an already inactive analysis must not save");
        }

        // ==================== route ====================

        [Test]
        public void TrySelectRoute_RejectsUnlocatedTargetWithoutSaving_AcceptedTargetSavesOnce()
        {
            director.Knowledge.ForceLevel(signalId, SystemKnowledgeLevel.Signal);
            director.Knowledge.ForceLevel(locatedId, SystemKnowledgeLevel.Located);

            Assert.IsFalse(director.TrySelectRoute(signalId, out _), "a merely-detected signal is not a navigable destination");
            Assert.IsNull(director.SelectedRouteSystemId, "a rejected route must leave the waypoint untouched");
            Assert.AreEqual(0, saveRequests, "route rejection is not a state transition");

            Assert.IsTrue(director.TrySelectRoute(locatedId, out string rejectReason), rejectReason);
            Assert.AreEqual(locatedId, director.SelectedRouteSystemId);
            Assert.AreEqual(1, saveRequests, "setting a real waypoint is exactly one persisted transition");
        }

        [Test]
        public void ClearRoute_RealTransitionRequestsOnce_RepeatedClearIsNoOp()
        {
            director.Knowledge.ForceLevel(locatedId, SystemKnowledgeLevel.Located);
            Assert.IsTrue(director.TrySelectRoute(locatedId, out string rejectReason), rejectReason);
            saveRequests = 0;

            director.ClearRoute();

            Assert.IsNull(director.SelectedRouteSystemId);
            Assert.AreEqual(1, saveRequests);

            director.ClearRoute();

            Assert.AreEqual(1, saveRequests, "clearing an already empty route must not save");
        }

        // ==================== helpers ====================

        /// <summary>
        /// EditMode runs no play loop, so Unity never calls a MonoBehaviour's
        /// Awake when AddComponent adds it.
        /// <para>
        /// <see cref="ShipModules"/> builds its <see cref="ModuleProgressionState"/>
        /// in Awake and nowhere else, so the component otherwise exists with a
        /// null <c>Modules</c> and every tier read through it throws — including
        /// <see cref="NavigationDirector.ScannerTier"/>, which is the first thing
        /// <c>TryStartAnalysis</c> asks. Driving the component's own real Awake
        /// reproduces the runtime lifecycle instead of substituting a stub, so
        /// the production path under test is entirely unfaked.
        /// </para>
        /// </summary>
        private static void RunAwake(ShipModules shipModules)
        {
            MethodInfo awake = typeof(ShipModules).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(awake, "ShipModules.Awake no longer exists; this fixture's lifecycle stand-in would be silently blind");
            awake.Invoke(shipModules, null);
            Assert.IsNotNull(shipModules.Modules, "ShipModules.Awake must leave a real ModuleProgressionState behind");
        }

        /// <summary>
        /// The first <paramref name="count"/> systems the galaxy genuinely
        /// places for a seed, in a fixed canonical order (start sector first,
        /// then lexicographic), resolved through the same
        /// <see cref="SectorManifestRepository"/> production uses.
        /// <para>
        /// Ids are never synthesised here. <see cref="GalaxyStarField"/> is
        /// finite and sparse — the campaign's own sector.0.0.0 usually holds
        /// exactly the one pinned start star — so a hand-written
        /// "system.analysis-target", or even a canonical-looking
        /// <c>WorldIdCodec.SystemId(sector, 1)</c>, names a system that does not
        /// exist. Knowledge levels, waypoints and analysis targets all persist
        /// as raw ids, so a fixture that invents them proves nothing about the
        /// ids a real save actually round-trips.
        /// </para>
        /// </summary>
        private static List<string> RealSystemIds(ulong seed, int count)
        {
            var manifests = new SectorManifestRepository(seed);
            manifests.SetStartSector(GalaxyStarField.StartSector);

            var ids = new List<string>();
            foreach (SectorCoordinate sector in OccupiedSectors(seed))
            {
                foreach (SystemManifest system in manifests.Resolve(sector).Systems)
                {
                    ids.Add(system.SystemId);
                    if (ids.Count == count) return ids;
                }
            }

            Assert.Fail($"the galaxy for seed {seed} places fewer than {count} systems; this fixture needs {count} distinct real ones");
            return ids;
        }

        /// <summary>Every sector this seed's star field really puts a star in, campaign start sector first and then lexicographically.</summary>
        private static List<SectorCoordinate> OccupiedSectors(ulong seed)
        {
            SectorCoordinate start = GalaxyStarField.StartSector;
            var seen = new HashSet<SectorCoordinate>();
            var occupied = new List<SectorCoordinate>();
            foreach (GalaxyStarField.Placement placement in GalaxyStarField.All(seed))
            {
                SectorCoordinate sector = SectorCoordinate.FromAbsolutePosition(placement.AbsolutePosition);
                if (seen.Add(sector)) occupied.Add(sector);
            }
            occupied.Sort((a, b) =>
            {
                if (a.Equals(start) != b.Equals(start)) return a.Equals(start) ? -1 : 1;
                if (a.X != b.X) return a.X.CompareTo(b.X);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            });
            return occupied;
        }
    }
}
