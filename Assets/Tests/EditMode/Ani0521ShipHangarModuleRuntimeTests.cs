using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.2.2: the ship-side tables — hangar hatch, auxiliary thrusters and
    /// the industry modules — plus the ownership split they must respect.
    /// </summary>
    public class Ani0521ShipHangarModuleRuntimeTests
    {
        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            EscortFireSignal.ClearSubscribers();
        }

        // --- hangar ----------------------------------------------------------

        [Test]
        public void TheHangarNodesAreTheRealAuthoredOnes()
        {
            Assert.AreEqual("part.module.06.hatch.door", ShipVehicleMechanicalProfiles.HangarHatchNode);
            Assert.AreEqual("part.module.06.bay", ShipVehicleMechanicalProfiles.HangarBayNode);
            Assert.AreEqual("anchor.hangar", ShipVehicleMechanicalProfiles.HangarAnchor);
        }

        [Test]
        public void TheHatchIsADoor_HeldOpenOrShut_NeverFreeRunning()
        {
            MechanicalPart[] door = ShipVehicleMechanicalProfiles.HangarDoor;
            Assert.AreEqual(1, door.Length);
            Assert.AreEqual(MechanicalRole.Deploy, door[0].Role);
            Assert.AreEqual(ShipVehicleMechanicalProfiles.HangarHatchNode, door[0].NodeName);
        }

        // --- auxiliary thrusters ------------------------------------------------

        [Test]
        public void AttitudeUsesTheAuxiliaryThrusters_NotTheMainEngineAnchors()
        {
            MechanicalPart[] aux = ShipVehicleMechanicalProfiles.AttitudeThrusters;
            Assert.AreEqual(2, aux.Length, "one pair, port and starboard");

            foreach (MechanicalPart part in aux)
            {
                Assert.IsTrue(part.NodeName.StartsWith("part.thruster.aux."), part.NodeName);
                Assert.IsFalse(ShipVehicleMechanicalProfiles.IsForeignOwned(part.NodeName));
                Assert.AreEqual(TransformOwner.ShipVehicleMechanics,
                    ShipVehicleMechanicalProfiles.OwnerOf(part.NodeName));
            }

            Assert.AreEqual(TransformOwner.ThrusterEffect,
                ShipVehicleMechanicalProfiles.OwnerOf("anchor.thruster.L"));
        }

        [Test]
        public void ThePairAnswersATurnInOppositeDirections()
        {
            MechanicalPart[] aux = ShipVehicleMechanicalProfiles.AttitudeThrusters;
            Assert.AreNotEqual(aux[0].Axis, aux[1].Axis,
                "a port and a starboard thruster do not push the same way");
        }

        // --- industry modules ------------------------------------------------------

        [Test]
        public void EveryIndustryModule_HasItsRealMechanisms()
        {
            string[] modules =
            {
                "module.industry.logistics_cargo.t1", "module.industry.logistics_cargo.t2",
                "module.industry.station_construction.t1", "module.industry.station_construction.t2",
                "module.industry.resource_processing.t1", "module.industry.resource_processing.t2"
            };

            foreach (string id in modules)
            {
                MechanicalPart[] parts = EconomyMechanicalProfiles.For(id);
                Assert.IsNotEmpty(parts, $"{id} must have real mechanisms");
                foreach (MechanicalPart part in parts)
                {
                    Assert.IsTrue(part.NodeName.StartsWith("part."), $"{id}: {part.NodeName}");
                    Assert.Greater(part.Magnitude, 0f, $"{id}: {part.NodeName}");
                }
            }
        }

        [Test]
        public void EachModuleMovesLikeWhatItIs()
        {
            Assert.IsTrue(EconomyMechanicalProfiles.For("module.industry.logistics_cargo.t1")
                .Any(p => p.NodeName.Contains("magazine")), "a cargo module has a magazine");
            Assert.IsTrue(EconomyMechanicalProfiles.For("module.industry.station_construction.t1")
                .Any(p => p.NodeName.Contains("fabricator_arm")), "a construction module has arms");
            Assert.IsTrue(EconomyMechanicalProfiles.For("module.industry.station_construction.t2")
                .Any(p => p.NodeName == "part.fabrication_gate"), "the T2 adds its gate");
        }

        [Test]
        public void NoModulePart_TouchesAWeaponOrEngineTransform()
        {
            foreach (string id in EconomyMechanicalProfiles.KnownVisualIds)
                foreach (MechanicalPart part in EconomyMechanicalProfiles.For(id))
                {
                    Assert.IsFalse(ShipVehicleMechanicalProfiles.IsForeignOwned(part.NodeName),
                        $"{id}: {part.NodeName}");
                    Assert.IsFalse(EconomyMechanicalProfiles.IsTurretOwned(part.NodeName),
                        $"{id}: {part.NodeName}");
                }
        }

        [Test]
        public void EveryModuleKeepsAPrimaryMechanism_SoLowQualityStillReads()
        {
            foreach (string id in new[] { "module.industry.logistics_cargo.t1",
                     "module.industry.station_construction.t1", "module.industry.resource_processing.t1" })
                Assert.IsTrue(EconomyMechanicalProfiles.For(id).Any(p => !p.Secondary), id);
        }

        // --- escort fire signal -------------------------------------------------------

        [Test]
        public void TheEscortSignalCountsOnlyWhatIsRaised()
        {
            EscortFireSignal.ClearSubscribers();
            Assert.AreEqual(0, EscortFireSignal.RaisedCount, "a fresh session has announced nothing");

            int heard = 0;
            EscortFireSignal.Fired += _ => heard++;

            EscortFireSignal.Raise("v1", Vector3.zero, Vector3.forward);
            Assert.AreEqual(1, EscortFireSignal.RaisedCount);
            Assert.AreEqual(1, heard);
        }

        [Test]
        public void ClearSubscribers_LeavesNoListenerFromThePreviousSession()
        {
            int heard = 0;
            EscortFireSignal.Fired += _ => heard++;
            EscortFireSignal.ClearSubscribers();

            EscortFireSignal.Raise("v1", Vector3.zero, Vector3.forward);
            Assert.AreEqual(0, heard, "a restarted campaign must not keep the old bridge listening");
        }

        [Test]
        public void TheSignalCarriesWhereTheShotCameFrom()
        {
            EscortFireSignal.ClearSubscribers();
            EscortFireEvent captured = default;
            EscortFireSignal.Fired += e => captured = e;

            EscortFireSignal.Raise("escort-7", new Vector3(1f, 2f, 3f), Vector3.right);

            Assert.AreEqual("escort-7", captured.VehicleId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), captured.Origin);
            Assert.AreEqual(Vector3.right, captured.Direction);
        }

        // --- versions ---------------------------------------------------------------------

        [Test]
        public void TheSaveAndContentVersions_AreUntouched()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
