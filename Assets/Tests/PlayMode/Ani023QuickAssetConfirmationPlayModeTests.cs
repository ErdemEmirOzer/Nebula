using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-02.3: the Quick Asset Package driven the way a player drives it —
    /// a real target, a real scan, real cargo, real gates, then
    /// <c>CanConfirm</c> and <c>TryConfirm</c> — and the three capsules that
    /// follow on the game's own t=0/2/4 schedule.
    /// <para>
    /// Nothing here reaches past a public gameplay API. No restore path, no
    /// reflection, no direct structure creation, and no production code added
    /// for the test's benefit: the fixture puts the ship where a player would
    /// have to put it and stocks what a player would have to stock.
    /// </para>
    /// </summary>
    public class Ani023QuickAssetConfirmationPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>Everything the fixture assembled, so each test can assert against the real objects.</summary>
        private sealed class Fixture
        {
            public PlanetIdentity Planet;
            public PlanetProfile Profile;
            public ShipController Ship;
            public ShipExtraction Extraction;
            public ShipQuickAssetPackage Package;
            public LogisticsCargo Cargo;
            public string CommandMaterialId;
            public string LaserMaterialId;
        }

        private IEnumerator BootScene()
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
            // entry the menu's New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [TearDown]
        public void TearDown() => AnimationAccessibility.ResetToDefaults();

        private static int CapsulePresenterCount()
            => UnityEngine.Object.FindObjectsOfType<BuildCapsulePresenter>().Length;

        private static int PresenterCountOn<T>() where T : MonoBehaviour
            => UnityEngine.Object.FindObjectsOfType<T>()
                .Count(x => x.GetComponent<BuildCapsulePresenter>() != null);

        /// <summary>
        /// Builds the state a player would have to reach before the package is
        /// even offered: in range of a scanned planet, stabilised, holding the
        /// parts and materials, with the construction module installed.
        /// </summary>
        private IEnumerator BuildRealFixture(Fixture fixture, bool stockCargo = true, int stationTier = 1)
        {
            // From the live session, not from a scene sweep. Unity defers Destroy to
            // the end of the frame, so after a restart a scene query can still hand
            // back the outgoing session's ship or planets -- and then the fixture
            // stabilises one session's ship against another session's body, which
            // reports a real relative speed and trips the package's own speed gate.
            var bootstrap = UnityEngine.Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>();
            ShipController ship = bootstrap != null && bootstrap.Context != null
                ? bootstrap.Context.Ship
                : UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship, "the ship must exist after boot");

            // Every component is taken off the ship itself, exactly as the
            // package takes them in its own Awake. A scene-wide search can land
            // on the progression screen's preview clone of the ship instead,
            // and then the fixture would be configuring an object nothing reads.
            var extraction = ship.GetComponent<ShipExtraction>();
            var package = ship.GetComponent<ShipQuickAssetPackage>();
            var modules = ship.GetComponent<ShipModules>();
            var logistics = ship.GetComponent<ShipLogistics>();

            Assert.IsNotNull(extraction, "the ship must carry its extraction component");
            Assert.IsNotNull(package, "the ship must carry the quick-asset driver");
            Assert.IsNotNull(modules);
            Assert.IsNotNull(logistics?.Cargo);

            // A planet whose three canonical nodes are all still free.
            // Same reason: the registry holds only what the live session registered,
            // and a restart clears it, so there is no window where a destroyed
            // planet still answers.
            PlanetIdentity planet = ExtractionState.AllPlanets
                .FirstOrDefault(p =>
                {
                    PlanetProfile profile = ExtractionState.GetProfile(p);
                    return profile != null
                           && !ExtractionState.IsNodeOccupied(p.WorldId, profile.DrillNode.Id)
                           && !ExtractionState.IsNodeOccupied(p.WorldId, profile.CommandNode.Id)
                           && !ExtractionState.IsNodeOccupied(p.WorldId, profile.FirstDefenseNode.Id);
                });
            Assert.IsNotNull(planet, "the Full system must offer a planet with three free canonical nodes");

            PlanetProfile planetProfile = ExtractionState.GetProfile(planet);
            Assert.IsNotNull(planetProfile);

            // Scanned, through the real public state API rather than a private flag.
            ExtractionState.MarkScanned(planet.WorldId);
            Assert.IsTrue(planetProfile.Scanned, "the planet must really be scanned");

            // In scanner range: close enough that the ship's own target sweep
            // picks this planet, and inside the build proximity gate.
            float radius = planet.Body.Radius;
            float desiredAltitude = radius * extraction.ScannerProximityR * 0.5f;
            ship.transform.position = planet.Body.transform.position + Vector3.up * (radius + desiredAltitude);

            // Stabilised on the planet, which also makes the relative speed zero.
            // Stabilising makes the ship hold station on the planet, which is
            // also what brings the relative speed under the package's limit.
            ship.StabilizeTo(planet.Body.transform);
            Assert.IsTrue(ship.IsStabilized, "the ship must be stabilised on the planet");

            // The construction module gate.
            modules.Modules.SetTier("industry.station_construction", stationTier);
            Assert.AreEqual(stationTier, modules.Modules.GetTier("industry.station_construction"),
                "the module gate must have been set on the ship the package actually reads");

            fixture.Planet = planet;
            fixture.Profile = planetProfile;
            fixture.Ship = ship;
            fixture.Extraction = extraction;
            fixture.Package = package;
            fixture.Cargo = logistics.Cargo;
            fixture.CommandMaterialId = ProcessingCatalog.MaterialIds[(int)planetProfile.Family];
            fixture.LaserMaterialId = ProcessingCatalog.MaterialIds[(int)planetProfile.Family];

            if (stockCargo)
            {
                fixture.Cargo.SetCapacity(100000f);
                fixture.Cargo.Add(ProcessingCatalog.BasicPartId, QuickAssetPackageService.TotalBasicCost * 4f);
                foreach (string materialId in ProcessingCatalog.MaterialIds)
                    fixture.Cargo.Add(materialId, QuickAssetPackageService.TotalSpecialCost * 4f);
            }

            // Let the ship's own Update run so its target sweep picks a planet.
            // A restart leaves the outgoing session's planets alive until Unity's
            // deferred Destroy runs, so the sweep may legitimately settle on a
            // different instance than the one this fixture moved to. The claim
            // is that the sweep really chose one, not which one it chose.
            for (int i = 0; i < 12 && fixture.Extraction.TargetPlanet == null; i++) yield return null;

            Assert.IsNotNull(fixture.Extraction.TargetPlanet,
                "the ship's own proximity sweep must have selected a planet — no target was forced");
            planet = fixture.Extraction.TargetPlanet;

            // Stabilised on the body the package will actually measure against.
            // The stabilisation above had to name a planet before the sweep had
            // run, and a restart can leave the sweep settling on a different
            // instance -- and then the ship holds station on one body while the
            // speed gate reads another. That is a real relative speed, and the
            // package rejects it: RELATIVE SPEED TOO HIGH, from a fixture that
            // believed it had parked. The rest of the fixture follows the sweep
            // too, because the material the package needs comes from the family
            // of the planet it is really going to build on.
            ship.StabilizeTo(planet.Body.transform);
            Assert.IsTrue(ship.IsStabilized, "the ship must be stabilised on the planet the sweep chose");
            Assert.AreEqual(0f, fixture.Extraction.RelativeSpeed(planet.Body), 1e-3f,
                "stabilised on the target body means no relative speed to it");

            planetProfile = ExtractionState.GetProfile(planet);
            fixture.Planet = planet;
            fixture.Profile = planetProfile;
            fixture.CommandMaterialId = ProcessingCatalog.MaterialIds[(int)planetProfile.Family];
            fixture.LaserMaterialId = ProcessingCatalog.MaterialIds[(int)planetProfile.Family];

            // The gate must still hold at the moment CanConfirm is asked, not
            // merely at the moment it was set.
            Assert.AreEqual(stationTier, modules.Modules.GetTier("industry.station_construction"),
                "the construction tier must survive the frames between fixture and confirmation");
            CollectionAssert.Contains(ModuleCatalog.FreeStartingModuleIds, "industry.station_construction",
                "the construction module is a free starting module, so a fresh ship already has it");
        }

        // ---- the gate itself ---------------------------------------------------

        [UnityTest]
        public IEnumerator TheRealFixtureSatisfiesCanConfirmWithNoRejectReason()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            bool canConfirm = fixture.Package.CanConfirm(out QuickPackageRejectReason reason, fixture.LaserMaterialId);

            Assert.AreEqual(QuickPackageRejectReason.None, reason,
                "the fixture must clear every gate, not merely most of them: "
                + ShipQuickAssetPackage.RejectMessage(reason));
            Assert.IsTrue(canConfirm);
        }

        [UnityTest]
        public IEnumerator TryConfirmOpensARealTransactionWithNothingSentYet()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            Assert.IsTrue(fixture.Package.TryConfirm(fixture.LaserMaterialId),
                "the real player path must accept this package");

            QuickAssetPackageTransaction tx = fixture.Package.ActiveTransaction;
            Assert.IsNotNull(tx, "confirming must open a real transaction");
            Assert.IsFalse(tx.Cancelled);
            Assert.AreEqual(fixture.Planet.WorldId, tx.PlanetWorldId);

            fixture.Package.Cancel();
        }

        // ---- the t=0 / 2 / 4 timeline ---------------------------------------------

        [UnityTest]
        public IEnumerator TheThreeCapsulesArriveOnTheGamesOwnScheduleAndNeverEarly()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            int presentersBefore = CapsulePresenterCount();
            Assert.IsTrue(fixture.Package.TryConfirm(fixture.LaserMaterialId));
            QuickAssetPackageTransaction tx = fixture.Package.ActiveTransaction;

            // --- t = 0: the drill goes, and only the drill ---------------------
            yield return null;
            yield return null;

            Assert.IsTrue(tx.DrillSent, "the drill capsule leaves at t=0");
            Assert.IsFalse(tx.CommandSent, "the command capsule must not have gone yet");
            Assert.IsFalse(tx.LaserSent);
            Assert.AreEqual(presentersBefore + 1, CapsulePresenterCount(),
                "exactly one capsule presenter at t=0");
            Assert.AreEqual(1, PresenterCountOn<DrillStructure>(),
                "and it belongs to the drill");

            // --- 0 < t < 2: still only the drill --------------------------------
            yield return new WaitForSeconds(1.2f);

            Assert.IsFalse(tx.CommandSent,
                $"nothing may go before t=2 (elapsed {tx.ElapsedSeconds:0.00}s)");
            Assert.IsFalse(tx.LaserSent);
            Assert.AreEqual(presentersBefore + 1, CapsulePresenterCount());

            // --- t = 2: the command centre goes ---------------------------------
            yield return new WaitForSeconds(1.4f);

            Assert.IsTrue(tx.CommandSent, $"the command capsule leaves at t=2 (elapsed {tx.ElapsedSeconds:0.00}s)");
            Assert.IsFalse(tx.LaserSent, "but the laser must still be waiting");
            Assert.AreEqual(presentersBefore + 2, CapsulePresenterCount(),
                "two capsule presenters once the command has gone");

            // --- t = 4: the laser goes -------------------------------------------
            yield return new WaitForSeconds(2.2f);

            Assert.IsTrue(tx.LaserSent, $"the laser capsule leaves at t=4 (elapsed {tx.ElapsedSeconds:0.00}s)");
            Assert.IsTrue(tx.Completed, "all three sent is a completed package");
            Assert.AreEqual(presentersBefore + 3, CapsulePresenterCount(),
                "exactly three capsule presenters in total — never a duplicate");

            // The right structures, of the right types.
            Assert.AreEqual(1, PresenterCountOn<DrillStructure>(), "one drill");
            SurfaceStructure[] surfaces = UnityEngine.Object.FindObjectsOfType<SurfaceStructure>()
                .Where(s => s.GetComponent<BuildCapsulePresenter>() != null).ToArray();
            Assert.AreEqual(2, surfaces.Length, "one command centre and one laser");
            CollectionAssert.AreEquivalent(
                new[] { SurfaceStructureType.Command, SurfaceStructureType.Laser },
                surfaces.Select(s => s.Type).ToArray());
        }

        [UnityTest]
        public IEnumerator TheAnimationOnlyReadsTheGamesElapsedTimeAndNeverSetsIt()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            Assert.IsTrue(fixture.Package.TryConfirm(fixture.LaserMaterialId));
            QuickAssetPackageTransaction tx = fixture.Package.ActiveTransaction;

            yield return new WaitForSeconds(1f);
            float elapsedAfterASecond = tx.ElapsedSeconds;

            // Ticking the animation director hard must not move the package on.
            var director = UnityEngine.Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director);
            for (int i = 0; i < 600; i++) director.Tick(1f / 60f);

            Assert.AreEqual(elapsedAfterASecond, tx.ElapsedSeconds, 1e-4f,
                "ten seconds of animation ticks must not advance the package by a millisecond");
            Assert.IsFalse(tx.CommandSent,
                "and must not have sent a capsule the schedule had not reached");

            fixture.Package.Cancel();
        }

        // ---- negative paths ------------------------------------------------------------

        [UnityTest]
        public IEnumerator WithoutTheMaterialsTheConfirmationIsRefusedAndNoCapsuleAppears()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture, stockCargo: false);

            int presentersBefore = CapsulePresenterCount();

            bool canConfirm = fixture.Package.CanConfirm(out QuickPackageRejectReason reason, fixture.LaserMaterialId);
            Assert.IsFalse(canConfirm, "an empty hold cannot pay for the package");
            Assert.AreEqual(QuickPackageRejectReason.InsufficientCargo, reason);

            Assert.IsFalse(fixture.Package.TryConfirm(fixture.LaserMaterialId));
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(presentersBefore, CapsulePresenterCount(),
                "a refused package must produce no capsule at all");
            Assert.IsNull(fixture.Package.ActiveTransaction?.PlanetWorldId ?? null);
        }

        [UnityTest]
        public IEnumerator WithoutTheConstructionModuleTheConfirmationIsRefusedAndNoCapsuleAppears()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture, stationTier: 0);

            int presentersBefore = CapsulePresenterCount();

            bool canConfirm = fixture.Package.CanConfirm(out QuickPackageRejectReason reason, fixture.LaserMaterialId);
            Assert.IsFalse(canConfirm, "the station construction module is a real gate");
            Assert.AreNotEqual(QuickPackageRejectReason.None, reason);

            Assert.IsFalse(fixture.Package.TryConfirm(fixture.LaserMaterialId));
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(presentersBefore, CapsulePresenterCount());
        }

        [UnityTest]
        public IEnumerator CancellingBeforeTwoSecondsMeansTheCommandAndLaserNeverAppear()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            int presentersBefore = CapsulePresenterCount();
            Assert.IsTrue(fixture.Package.TryConfirm(fixture.LaserMaterialId));

            yield return null;
            yield return null;
            QuickAssetPackageTransaction tx = fixture.Package.ActiveTransaction;
            Assert.IsTrue(tx.DrillSent, "the drill already went at t=0");

            fixture.Package.Cancel();
            yield return new WaitForSeconds(3f); // well past both remaining send times

            Assert.IsFalse(tx.CommandSent, "a cancelled package must not send its command capsule");
            Assert.IsFalse(tx.LaserSent);
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsOfType<SurfaceStructure>()
                .Count(s => s.GetComponent<BuildCapsulePresenter>() != null),
                "and no surface structure was ever created");

            // Cancelling the package also cancels the drill it had already
            // sent — that is the existing 75% refund rule, not something this
            // part changed. What matters here is that nothing visual is left
            // over: no presenter, and no proxy stranded in the sky.
            Assert.AreEqual(presentersBefore, CapsulePresenterCount(),
                "a cancelled package must leave no capsule presenter behind");

            var director = UnityEngine.Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director);
            director.Tick(1f);
            Assert.AreEqual(0, director.CapsuleProxies.LeasedCount,
                "and no proxy may be left flying");
        }

        // ---- the pool never changes the gameplay result -------------------------------------

        [UnityTest]
        public IEnumerator AnExhaustedProxyPoolDoesNotChangeWhatThePackageDelivers()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            // Starve the pool before confirming: every proxy is already out.
            var director = UnityEngine.Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director);
            var hogged = new List<CapsuleProxyLease>();
            while (director.CapsuleProxies.TryLease(out CapsuleProxyLease lease, out _)) hogged.Add(lease);
            Assert.Greater(hogged.Count, 0, "the pool must have had something to take");

            try
            {
                Assert.IsTrue(fixture.Package.TryConfirm(fixture.LaserMaterialId),
                    "an exhausted scenery pool must not stop a package the player paid for");

                yield return new WaitForSeconds(4.6f);
                QuickAssetPackageTransaction tx = fixture.Package.ActiveTransaction;

                Assert.IsTrue(tx.Completed,
                    "all three capsules must still be delivered with no proxy available");
                Assert.AreEqual(1, PresenterCountOn<DrillStructure>());
                Assert.AreEqual(2, UnityEngine.Object.FindObjectsOfType<SurfaceStructure>()
                    .Count(s => s.GetComponent<BuildCapsulePresenter>() != null));
            }
            finally
            {
                foreach (CapsuleProxyLease lease in hogged) director.CapsuleProxies.Release(lease);
            }
        }

        // ---- regression closure: the two gameplay seams this part changed ---------------

        [UnityTest]
        public IEnumerator StabilisedOnABodyMeansZeroRelativeSpeedToThatBody()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            Assert.IsTrue(fixture.Ship.IsStabilized);
            Assert.AreSame(fixture.Planet.Body.transform, fixture.Ship.StabilizedAnchor,
                "the ship must be riding this planet specifically");

            Assert.AreEqual(0f, fixture.Extraction.RelativeSpeed(fixture.Planet.Body), 1e-4f,
                "holding station on a body is zero relative speed to it, whatever the raw velocities say");
        }

        [UnityTest]
        public IEnumerator WithoutStabilisationTheRealVelocityDifferenceIsStillReported()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            fixture.Ship.ReleaseStabilize();
            Assert.IsFalse(fixture.Ship.IsStabilized);

            // Give the ship a velocity of its own and check the maths is the
            // ordinary difference again.
            var shipVelocity = new Vector3(120f, -40f, 15f);
            fixture.Ship.SetVelocity(shipVelocity);

            float expected = (shipVelocity - fixture.Planet.Body.Velocity).magnitude;
            Assert.AreEqual(expected, fixture.Extraction.RelativeSpeed(fixture.Planet.Body), 1e-3f,
                "a free-flying ship must report the real velocity difference");
            Assert.Greater(expected, 0f, "and this case must genuinely be non-zero, or it proves nothing");
        }

        [UnityTest]
        public IEnumerator StabilisingOnOneBodyDoesNotZeroTheSpeedToAnother()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            // A second, different body in the same system.
            Nebula.Celestial.CelestialBody other = UnityEngine.Object.FindObjectsOfType<PlanetIdentity>()
                .Select(p => p.Body)
                .FirstOrDefault(b => b != null && b != fixture.Planet.Body);
            if (other == null)
            {
                // Fall back to the system's star, which is always present.
                other = UnityEngine.Object.FindObjectsOfType<Nebula.Celestial.CelestialBody>()
                    .FirstOrDefault(b => b != null && b != fixture.Planet.Body);
            }
            Assert.IsNotNull(other, "the system must contain a second body to compare against");

            Assert.IsTrue(fixture.Ship.IsStabilized);
            Assert.AreEqual(0f, fixture.Extraction.RelativeSpeed(fixture.Planet.Body), 1e-4f);

            float toOther = fixture.Extraction.RelativeSpeed(other);
            Assert.AreEqual((fixture.Ship.Velocity - other.Velocity).magnitude, toOther, 1e-3f,
                "riding one body must not make the ship look motionless relative to a different one");
        }

        [UnityTest]
        public IEnumerator ReleasingStabilisationReturnsToTheOrdinaryCalculation()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            Assert.AreEqual(0f, fixture.Extraction.RelativeSpeed(fixture.Planet.Body), 1e-4f);

            fixture.Ship.ReleaseStabilize();
            Assert.IsNull(fixture.Ship.StabilizedAnchor);

            float afterRelease = fixture.Extraction.RelativeSpeed(fixture.Planet.Body);
            Assert.AreEqual((fixture.Ship.Velocity - fixture.Planet.Body.Velocity).magnitude, afterRelease, 1e-3f,
                "letting go must put the ordinary difference back, not latch zero");
        }

        [UnityTest]
        public IEnumerator TheLazyModuleLookupStillHoldsAfterARestart()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);
            bootstrap.RestartCampaign();
            // Unity defers Destroy to end of frame, so for a moment the old
            // session's planets still answer a scene query while the new
            // session's are already the live ones. Wait until only one set is
            // left, or the fixture can pick a planet the ship will never target.
            for (int i = 0; i < 8; i++) yield return null;

            // A brand new ship, built by the same factory in the same order
            // that produced the null reference in the first place.
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            Assert.IsTrue(fixture.Package.CanConfirm(out QuickPackageRejectReason reason, fixture.LaserMaterialId),
                "the module gate must resolve on a restarted session too: "
                + ShipQuickAssetPackage.RejectMessage(reason));
            Assert.AreEqual(QuickPackageRejectReason.None, reason);
            Assert.IsTrue(fixture.Package.TryConfirm(fixture.LaserMaterialId));

            fixture.Package.Cancel();
        }

        [UnityTest]
        public IEnumerator RepeatedGateChecksResolveTheSameModulesWithoutDuplicatingAnything()
        {
            yield return BootScene();
            var fixture = new Fixture();
            yield return BuildRealFixture(fixture);

            int modulesBefore = UnityEngine.Object.FindObjectsOfType<ShipModules>().Length;
            var stateBefore = fixture.Ship.GetComponent<ShipModules>().Modules;

            // The lazy lookup runs on every gate check; a hundred of them must
            // not add a component or swap the progression state.
            for (int i = 0; i < 100; i++)
                fixture.Package.CanConfirm(out _, fixture.LaserMaterialId);

            Assert.AreEqual(modulesBefore, UnityEngine.Object.FindObjectsOfType<ShipModules>().Length,
                "resolving the modules must never add another ShipModules");
            Assert.AreSame(stateBefore, fixture.Ship.GetComponent<ShipModules>().Modules,
                "nor replace the progression state the tiers live in");
            Assert.AreEqual(1, fixture.Ship.GetComponents<ShipModules>().Length,
                "the ship carries exactly one modules component");
        }

        // ---- preserved authorities --------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheScheduleAndCostsAreUnchangedByAnyOfThis()
        {
            yield return BootScene();

            Assert.AreEqual(0f, QuickAssetPackageService.DrillSendDelay, 1e-4f);
            Assert.AreEqual(2f, QuickAssetPackageService.CommandSendDelay, 1e-4f);
            Assert.AreEqual(4f, QuickAssetPackageService.LaserSendDelay, 1e-4f);
            Assert.AreEqual(65, QuickAssetPackageService.TotalBasicCost);
            Assert.AreEqual(15, QuickAssetPackageService.TotalSpecialCost);
        }
    }
}
