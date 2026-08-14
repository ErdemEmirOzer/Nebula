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
    /// ANI-05.2: the corrected record of what the ship and the seven vehicles
    /// really carry, and the ownership split that keeps two presenters off one
    /// transform.
    /// </summary>
    public class Ani052ShipVehicleMechanicalRetrofitTests
    {
        [TearDown]
        public void Cleanup() => AnimationAccessibility.ResetToDefaults();

        [Test]
        public void AllSevenVehicles_HaveRealMechanisms()
        {
            Assert.AreEqual(7, ShipVehicleMechanicalProfiles.KnownVehicleIds.Count(),
                "the earlier audit called all seven single meshes");

            foreach (string id in ShipVehicleMechanicalProfiles.KnownVehicleIds)
            {
                MechanicalPart[] parts = ShipVehicleMechanicalProfiles.ForVehicle(id);
                Assert.IsNotEmpty(parts, id);
                foreach (MechanicalPart part in parts)
                {
                    Assert.IsTrue(part.NodeName.StartsWith("part."), $"{id}: {part.NodeName}");
                    Assert.Greater(part.Magnitude, 0f, $"{id}: {part.NodeName}");
                }
            }
        }

        [Test]
        public void EachClassHasTheMechanismItIsNamedFor()
        {
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.scout.mk1")
                .Any(p => p.NodeName.Contains("sensor_wing")), "a Scout has sensor wings");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.collector.mk1")
                .Any(p => p.NodeName.Contains("cargo_clamp")), "a Collector has cargo clamps");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.collector.mk2")
                .Any(p => p.NodeName == "part.cargo_carousel"), "the Mk II has a carousel");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.engineer.mk1")
                .Any(p => p.NodeName.Contains("mandible")), "an Engineer Mk I has mandibles");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.engineer.mk2")
                .Any(p => p.NodeName == "part.recovery_winch"), "the Mk II has a recovery winch");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.escort.mk1")
                .Any(p => p.NodeName == "part.fixed_laser_lance"), "an Escort has a fixed lance");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.escort.mk2")
                .Any(p => p.NodeName == "part.fixed_laser_spine"));
        }

        [Test]
        public void AnUnknownVehicle_IsASafeEmptySet()
        {
            Assert.IsEmpty(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.that.does.not.exist"));
            Assert.IsEmpty(ShipVehicleMechanicalProfiles.ForVehicle(null));
            Assert.IsFalse(ShipVehicleMechanicalProfiles.HasRealMechanisms(null));
        }

        // --- §2 ownership -------------------------------------------------------

        [Test]
        public void NoTableEntry_NamesATransformAnotherPresenterOwns()
        {
            foreach (string id in ShipVehicleMechanicalProfiles.KnownVehicleIds)
                foreach (MechanicalPart part in ShipVehicleMechanicalProfiles.ForVehicle(id))
                    Assert.IsFalse(ShipVehicleMechanicalProfiles.IsForeignOwned(part.NodeName),
                        $"{id}: {part.NodeName} belongs to another presenter");

            foreach (MechanicalPart part in ShipVehicleMechanicalProfiles.AttitudeThrusters)
                Assert.IsFalse(ShipVehicleMechanicalProfiles.IsForeignOwned(part.NodeName));
            foreach (MechanicalPart part in ShipVehicleMechanicalProfiles.HangarDoor)
                Assert.IsFalse(ShipVehicleMechanicalProfiles.IsForeignOwned(part.NodeName));
        }

        [Test]
        public void TheEngineHaloAnchorsBelongToThrusterEffect()
        {
            Assert.AreEqual(TransformOwner.ThrusterEffect,
                ShipVehicleMechanicalProfiles.OwnerOf("anchor.thruster.L"));
            Assert.AreEqual(TransformOwner.ThrusterEffect,
                ShipVehicleMechanicalProfiles.OwnerOf("anchor.thruster.R"));
            Assert.IsTrue(ShipVehicleMechanicalProfiles.IsForeignOwned("anchor.thruster.L"));
        }

        [Test]
        public void TheWeaponGimbalBelongsToAni04()
        {
            Assert.AreEqual(TransformOwner.WeaponGimbal,
                ShipVehicleMechanicalProfiles.OwnerOf("part.gimbal_yaw"));
            Assert.IsTrue(ShipVehicleMechanicalProfiles.IsForeignOwned("part.gimbal_pitch"));
        }

        [Test]
        public void TheAttitudeThrustersAreAuxiliary_NotTheMainEngines()
        {
            foreach (MechanicalPart part in ShipVehicleMechanicalProfiles.AttitudeThrusters)
            {
                Assert.IsTrue(part.NodeName.StartsWith("part.thruster.aux."),
                    "attitude motion uses the auxiliary thrusters, never the main engine anchors");
                Assert.AreEqual(TransformOwner.ShipVehicleMechanics,
                    ShipVehicleMechanicalProfiles.OwnerOf(part.NodeName));
            }
        }

        // --- §4 hangar -------------------------------------------------------------

        [Test]
        public void TheHangarHatchIsARealAuthoredNode()
        {
            Assert.AreEqual("part.module.06.hatch.door", ShipVehicleMechanicalProfiles.HangarHatchNode);
            Assert.AreEqual("part.module.06.bay", ShipVehicleMechanicalProfiles.HangarBayNode);
            Assert.AreEqual("anchor.hangar", ShipVehicleMechanicalProfiles.HangarAnchor);
        }

        [Test]
        public void TheHangarDoorIsDrivenAsADoor_NotALoop()
        {
            MechanicalPart[] door = ShipVehicleMechanicalProfiles.HangarDoor;
            Assert.AreEqual(1, door.Length);
            Assert.AreEqual(MechanicalRole.Deploy, door[0].Role,
                "a door holds open or shut; it does not free-run");
            Assert.Greater(door[0].Magnitude, 45f, "and it must actually open");
        }

        // --- roles ---------------------------------------------------------------------

        [Test]
        public void ToolsDeploy_TheyDoNotSpinFreely()
        {
            foreach (string id in new[] { "vehicle.scout.mk1", "vehicle.engineer.mk1", "vehicle.escort.mk1" })
                Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle(id)
                    .Any(p => p.Role == MechanicalRole.Deploy), $"{id} deploys its tool");
        }

        [Test]
        public void OnlyTheMkTwoCollectorSpins_TheMkOneClamps()
        {
            Assert.IsFalse(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.collector.mk1")
                .Any(p => p.Role == MechanicalRole.Rotary), "the Mk I has no carousel to turn");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.collector.mk2")
                .Any(p => p.Role == MechanicalRole.Rotary));
        }

        [Test]
        public void EveryVehicleKeepsAPrimaryMechanism_SoLowQualityStillReads()
        {
            foreach (string id in ShipVehicleMechanicalProfiles.KnownVehicleIds)
                Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle(id).Any(p => !p.Secondary), id);
        }

        // --- versions ------------------------------------------------------------------------

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
