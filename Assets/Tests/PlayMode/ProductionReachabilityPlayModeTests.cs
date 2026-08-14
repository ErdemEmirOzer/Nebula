using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// <c>VehicleAgent.Update</c>'s missing ship-lifecycle guard, driven through the
    /// real player loop: a real hangar, a real vehicle, a real launch, and then the
    /// ShipController going away underneath it — which is exactly what session
    /// teardown and a destroyed ship do.
    /// <para>
    /// <c>EnemyAgent.Update</c> has guarded its equivalent (<c>playerShip == null</c>)
    /// since VS-013; <c>VehicleAgent.Update</c> checked only <c>dt &lt;= 0</c> and
    /// <c>Stranded</c>, so <c>AnimateLaunch</c> resolved <c>ship.transform</c> twice
    /// with nothing behind it and every launching vehicle threw
    /// <c>MissingReferenceException</c> once per frame. Run against the pre-fix
    /// build, the freeze test below fails on that unhandled error.
    /// </para>
    /// </summary>
    public class ProductionReachabilityPlayModeTests
    {
        private const ulong Seed = 20260809UL;

        private GameObject shipObject;
        private ShipHangar hangar;
        private SystemManifest system;
        private string planetWorldId;

        [SetUp]
        public void SetUp()
        {
            ExtractionState.Reset((int)Seed);
            VehicleAgent.Registry.Clear();
            StarSystemRegistry.Initialize(Seed);

            SectorManifest sector = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
            system = sector.Systems.First(s => s.Planets.Count > 0);
            StarSystemRegistry.SetStartSystem(system.Sector, system.SystemId);
            planetWorldId = system.Planets[0].PlanetId;

            shipObject = new GameObject("Lifecycle Test Ship");
            // Absolute position is transform.position + FloatingOrigin.TotalOffset, and
            // the hangar's CurrentSystemId is an absolute-position lookup — so the ship
            // is placed to land exactly on the system centre whatever the offset is.
            shipObject.transform.position = system.AbsoluteCenter - FloatingOrigin.TotalOffset;

            shipObject.AddComponent<ShipController>();
            ShipLogistics logistics = shipObject.AddComponent<ShipLogistics>();
            hangar = shipObject.AddComponent<ShipHangar>();

            logistics.InitializeNewCampaign();
            hangar.InitializeNewCampaign();
            hangar.ApplyHangarTier(2);
        }

        [TearDown]
        public void TearDown()
        {
            if (shipObject != null) Object.Destroy(shipObject);
            shipObject = null;
            hangar = null;
            VehicleAgent.Registry.Clear();
            StarSystemRegistry.Clear();
            ExtractionState.Reset((int)Seed);
        }

        /// <summary>A real vehicle, launched through the real hangar command.</summary>
        private VehicleAgent LaunchOne()
        {
            VehicleAgent vehicle = hangar.RestoreVehicle("vehicle-0000", VehicleCatalog.Scout);
            Assert.AreEqual(ShipHangar.LaunchRejectReason.None,
                hangar.TryLaunch(vehicle, MissionKind.Scout, planetWorldId),
                "the fixture must really launch a vehicle, or it proves nothing about launching");
            Assert.AreEqual(VehiclePhase.Launching, vehicle.Mission.Phase);
            return vehicle;
        }

        /// <summary>
        /// The launching vehicle outlives its ship. It must stay quiet, and — the part
        /// a bare try/catch would not give — it must not COMPLETE anything: the
        /// Launching phase timer is what turns into TravelOut after
        /// <see cref="VehicleMissionState.LaunchSeconds"/>, so ticking on without a
        /// ship would hand the player a mission leg they never flew.
        /// </summary>
        [UnityTest]
        public IEnumerator AVehicleWhoseShipIsDestroyedFreezesInsteadOfFlyingOn()
        {
            VehicleAgent vehicle = LaunchOne();
            yield return null; // one real frame with the ship still there

            // The component only: the hangar and the vehicle both survive, which is
            // the ordering a scene teardown or a rebuilt ship really produces.
            Object.DestroyImmediate(shipObject.GetComponent<ShipController>());
            Assert.IsTrue(vehicle.ShipMissing, "the fixture must really remove the ship reference");

            VehiclePhase frozenAt = vehicle.Mission.Phase;
            float frozenTimer = vehicle.Mission.PhaseTimer;

            // Longer than the whole Launching phase: if the agent were still ticking
            // it would have crossed into TravelOut inside this wait.
            yield return new WaitForSeconds(VehicleMissionState.LaunchSeconds + 0.75f);

            Assert.AreEqual(frozenAt, vehicle.Mission.Phase,
                "a vehicle must not complete a mission leg because its ship vanished");
            Assert.AreEqual(frozenTimer, vehicle.Mission.PhaseTimer, 0.0001f,
                "the phase timer kept running with nothing to fly from");
            Assert.IsTrue(vehicle != null && vehicle.gameObject.activeSelf,
                "the guard must freeze the agent, not destroy or park it");
            Assert.AreEqual(1, hangar.Vehicles.Count, "the roster lost the frozen vehicle");

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// And it is a freeze, not a death sentence: once a ship exists again the same
        /// agent resumes — same id, same phase it was frozen in, and exactly one entry
        /// in the roster and in the registry. Nothing is respawned.
        /// </summary>
        [UnityTest]
        public IEnumerator ARematerialisedShipResumesTheSameVehicle()
        {
            VehicleAgent vehicle = LaunchOne();
            yield return null;

            Object.DestroyImmediate(shipObject.GetComponent<ShipController>());
            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(VehiclePhase.Launching, vehicle.Mission.Phase);

            shipObject.AddComponent<ShipController>();
            yield return null; // ShipHangar.Update re-attaches the frozen agents

            Assert.IsFalse(vehicle.ShipMissing, "the hangar never re-attached its own vehicle");
            Assert.AreEqual(1, hangar.Vehicles.Count, "re-attaching duplicated the fleet");
            Assert.AreEqual(1, VehicleAgent.Registry.Count, "re-attaching minted a second agent");
            Assert.AreSame(vehicle, hangar.Vehicles[0]);

            yield return new WaitForSeconds(VehicleMissionState.LaunchSeconds + 0.75f);

            Assert.AreNotEqual(VehiclePhase.Launching, vehicle.Mission.Phase,
                "the agent stayed frozen after its ship came back");

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The intervention group the Fleet page can now fill and policy has to
        /// survive the director's own tick — the tick that, with no control anywhere,
        /// returned on its first line forever. EditMode cannot show this: Update never
        /// runs there, and <c>EnsureGroupForSystem</c> only executes inside it.
        /// </summary>
        [UnityTest]
        public IEnumerator AFilledInterventionGroupSurvivesTheDirectorsOwnTick()
        {
            var directorObject = new GameObject("Lifecycle Test Intervention");
            try
            {
                var director = directorObject.AddComponent<InterventionGroupDirector>();
                director.Initialize(hangar);

                VehicleAgent engineer = hangar.RestoreVehicle("vehicle-0001", VehicleCatalog.EngineerMk1);
                Assert.IsTrue(director.AddMember(engineer.VehicleId),
                    "the group must accept a real support vehicle while the ship is in a system");
                director.SetPolicy(Nebula.Combat.InterventionPolicy.SystemIntervention);

                yield return null;
                yield return null;

                Assert.IsNotNull(director.Group);
                Assert.AreEqual(system.SystemId, director.Group.SystemId,
                    "the director rebuilt the group for a different system and threw the membership away");
                Assert.IsFalse(director.Group.IsEmpty,
                    "TickInterventionGroup returns on its first line for an empty group — that was the whole dormancy");
                Assert.AreEqual(Nebula.Combat.InterventionPolicy.SystemIntervention, director.Group.Policy);
                CollectionAssert.Contains(director.Group.MemberVehicleIds, engineer.VehicleId);

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(directorObject);
            }
        }

        /// <summary>
        /// The guard is in <c>Update</c>, not in one animation helper, so it covers
        /// every phase at once. A docking vehicle resolves the ship's transform on a
        /// completely different code path and must be just as quiet.
        /// </summary>
        [UnityTest]
        public IEnumerator ARecalledVehicleWithNoShipIsAlsoQuiet()
        {
            VehicleAgent vehicle = LaunchOne();
            yield return null;

            hangar.Recall(vehicle);
            Assert.IsTrue(vehicle.Mission.RecallRequested);

            Object.DestroyImmediate(shipObject.GetComponent<ShipController>());
            yield return new WaitForSeconds(VehicleMissionState.LaunchSeconds + 0.75f);

            Assert.IsTrue(vehicle != null, "the recalled vehicle was destroyed instead of frozen");
            Assert.AreEqual(VehiclePhase.Launching, vehicle.Mission.Phase,
                "a recall must not resolve while there is no ship to return to");

            LogAssert.NoUnexpectedReceived();
        }
    }
}
