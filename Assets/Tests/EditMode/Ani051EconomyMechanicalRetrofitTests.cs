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
    /// ANI-05.1: the corrected record of what the economy models really carry,
    /// and the ownership rule that stops two presenters fighting over one node.
    /// </summary>
    public class Ani051EconomyMechanicalRetrofitTests
    {
        [TearDown]
        public void Cleanup() => AnimationAccessibility.ResetToDefaults();

        // --- the corrected audit -------------------------------------------

        [Test]
        public void TheDrillT2_HasItsRealMechanisms_NotOne()
        {
            MechanicalPart[] parts = EconomyMechanicalProfiles.For("structure.drill.t2");
            Assert.Greater(parts.Length, 10,
                "the earlier audit read the prefab wrapper and called this a single mesh");
            Assert.IsTrue(parts.Any(p => p.NodeName == "part.drill_bit"));
            Assert.IsTrue(parts.Any(p => p.NodeName == "part.drill_shaft"));
            Assert.AreEqual(3, parts.Count(p => p.NodeName.StartsWith("part.stabilizer.")));
        }

        [Test]
        public void TheProcessingModule_HasItsSixRealParts()
        {
            MechanicalPart[] t1 = EconomyMechanicalProfiles.For("module.industry.resource_processing.t1");
            Assert.AreEqual(6, t1.Length, "ANI-GAP-002 was wrong: this model has six mechanisms");
            Assert.IsTrue(t1.Any(p => p.NodeName == "part.intake"));
            Assert.IsTrue(t1.Any(p => p.NodeName == "part.output_manifold"));
            Assert.IsTrue(t1.Any(p => p.NodeName == "part.processor_drum"));
        }

        [Test]
        public void EveryCoveredVisual_HasAtLeastOneRealMechanism()
        {
            foreach (string id in EconomyMechanicalProfiles.KnownVisualIds)
            {
                Assert.IsTrue(EconomyMechanicalProfiles.HasRealMechanisms(id), id);
                foreach (MechanicalPart part in EconomyMechanicalProfiles.For(id))
                {
                    Assert.IsTrue(part.NodeName.StartsWith("part."), $"{id}: {part.NodeName}");
                    Assert.Greater(part.Magnitude, 0f, $"{id}: {part.NodeName} must actually move");
                    Assert.AreEqual(1f, part.Axis.magnitude, 1e-3f, $"{id}: {part.NodeName} axis must be unit");
                }
            }
        }

        [Test]
        public void AnUnknownVisual_IsASafeEmptySet()
        {
            Assert.IsEmpty(EconomyMechanicalProfiles.For("structure.that.does.not.exist"));
            Assert.IsEmpty(EconomyMechanicalProfiles.For(null));
            Assert.IsFalse(EconomyMechanicalProfiles.HasRealMechanisms(null));
        }

        // --- §2 ownership ----------------------------------------------------

        [Test]
        public void NoEconomyPartIsATurretTransform()
        {
            foreach (string id in EconomyMechanicalProfiles.KnownVisualIds)
                foreach (MechanicalPart part in EconomyMechanicalProfiles.For(id))
                    Assert.IsFalse(EconomyMechanicalProfiles.IsTurretOwned(part.NodeName),
                        $"{id}: {part.NodeName} belongs to ANI-04.3's turret presenter");
        }

        [Test]
        public void TheTurretNodesAreTheOnesAni043ReallyDrives()
        {
            Assert.IsTrue(EconomyMechanicalProfiles.IsTurretOwned("part.turret_yaw"));
            Assert.IsTrue(EconomyMechanicalProfiles.IsTurretOwned("part.laser_lance_recoil"));
            Assert.IsTrue(EconomyMechanicalProfiles.IsTurretOwned("swivel-joint"));
            Assert.IsTrue(EconomyMechanicalProfiles.IsTurretOwned("turret-head"));
            Assert.IsFalse(EconomyMechanicalProfiles.IsTurretOwned("part.drill_bit"));
        }

        [Test]
        public void PlasmaT2_ContributesOnlyItsSupportPylons()
        {
            MechanicalPart[] parts = EconomyMechanicalProfiles.For("structure.defense.plasma.t2");
            Assert.AreEqual(3, parts.Length);
            foreach (MechanicalPart part in parts)
            {
                Assert.AreEqual(MechanicalRole.Deploy, part.Role, "a defense structure only deploys here");
                Assert.IsTrue(part.NodeName.StartsWith("part.capacitor_pylon."));
            }
        }

        [Test]
        public void NoDefenceTurretIsCoveredByTheEconomyTable()
        {
            Assert.IsFalse(EconomyMechanicalProfiles.HasRealMechanisms("structure.defense.laser.t1"));
            Assert.IsFalse(EconomyMechanicalProfiles.HasRealMechanisms("structure.defense.laser.t2"),
                "the T2 recoil node is ANI-04.3's and must not appear here");
        }

        // --- roles ------------------------------------------------------------

        [Test]
        public void ADrillTurnsAndStrokes()
        {
            MechanicalPart[] t1 = EconomyMechanicalProfiles.For("structure.drill.t1");
            Assert.IsTrue(t1.Any(p => p.Role == MechanicalRole.Rotary), "a drill must turn");
            Assert.IsTrue(t1.Any(p => p.Role == MechanicalRole.Linear), "and feed");
        }

        [Test]
        public void EachDrillAdapter_MovesLikeWhatItIs()
        {
            Assert.IsTrue(EconomyMechanicalProfiles.For("structure.drill_adapter.solid")
                .All(p => p.Role == MechanicalRole.Rotary), "solid bores");
            Assert.IsTrue(EconomyMechanicalProfiles.For("structure.drill_adapter.ocean")
                .Any(p => p.NodeName.Contains("turbine")), "ocean pumps");
            Assert.IsTrue(EconomyMechanicalProfiles.For("structure.drill_adapter.gas")
                .Any(p => p.NodeName.Contains("compressor")), "gas compresses");
        }

        [Test]
        public void TheFiveFamilies_DifferOnlyInCadence()
        {
            float metal = EconomyMechanicalProfiles.FamilyCadence(ResourceFamily.Metal);
            float gas = EconomyMechanicalProfiles.FamilyCadence(ResourceFamily.Gas);
            float diamond = EconomyMechanicalProfiles.FamilyCadence(ResourceFamily.Diamond);

            Assert.Greater(gas, metal, "a compressor runs faster than heavy percussion");
            Assert.Greater(metal, diamond, "a precise cutter is the slowest of all");
            foreach (ResourceFamily family in System.Enum.GetValues(typeof(ResourceFamily)).Cast<ResourceFamily>())
            {
                float cadence = EconomyMechanicalProfiles.FamilyCadence(family);
                Assert.Greater(cadence, 0f, $"{family}");
                Assert.Less(cadence, 2f, $"{family} must stay plausible");
            }
        }

        [Test]
        public void SensorsSweep_TheyDoNotSpinLikeArcadeRadar()
        {
            MechanicalPart[] t1 = EconomyMechanicalProfiles.For("structure.support.sensor_array.t1");
            MechanicalPart crown = t1.First(p => p.NodeName == "part.sensor_crown_yaw");
            Assert.AreEqual(MechanicalRole.Sweep, crown.Role);
            Assert.Less(crown.Magnitude, 90f, "a sweep is an arc, not a spin");
        }

        [Test]
        public void TheLogisticsCentre_HasDockAndCargoMechanisms()
        {
            MechanicalPart[] t2 = EconomyMechanicalProfiles.For("station.system_logistics.t2");
            Assert.IsTrue(t2.Any(p => p.NodeName.Contains("dock_ring")));
            Assert.IsTrue(t2.Any(p => p.NodeName.Contains("cargo_carousel")));
            Assert.IsTrue(t2.Any(p => p.NodeName == "part.transfer_core"));
        }

        [Test]
        public void MaintenanceHasArms_AndT2HasItsRecoveryCradle()
        {
            Assert.IsTrue(EconomyMechanicalProfiles.For("structure.support.maintenance_platform.t1")
                .Any(p => p.NodeName.StartsWith("part.repair_arm_")));
            Assert.IsTrue(EconomyMechanicalProfiles.For("structure.support.maintenance_platform.t2")
                .Any(p => p.NodeName == "part.recovery_cradle"));
        }

        // --- quality tiers ------------------------------------------------------

        [Test]
        public void EveryCoveredVisual_HasAtLeastOnePrimaryMechanism()
        {
            foreach (string id in EconomyMechanicalProfiles.KnownVisualIds)
                Assert.IsTrue(EconomyMechanicalProfiles.For(id).Any(p => !p.Secondary),
                    $"{id} must still read on Low quality");
        }

        [Test]
        public void TheBusiestModels_KeepMostOfTheirDetailSecondary()
        {
            MechanicalPart[] t2 = EconomyMechanicalProfiles.For("structure.drill.t2");
            int secondary = t2.Count(p => p.Secondary);
            Assert.Greater(secondary, t2.Length / 2, "Low quality must not drive eighteen parts");
        }

        // --- versions -------------------------------------------------------------

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
