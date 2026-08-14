using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Hangar;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.2.1/.2: the pure side of the vehicle mechanics — which class has
    /// which mechanism, and the drive states they are allowed to reach.
    /// </summary>
    public class Ani0521VehiclePhaseMechanicsTests
    {
        [TearDown]
        public void Cleanup() => AnimationAccessibility.ResetToDefaults();

        [Test]
        public void EveryDriveState_IsDistinct()
        {
            var values = Enum.GetValues(typeof(MechanicalDrive)).Cast<MechanicalDrive>().ToArray();
            Assert.AreEqual(values.Length, values.Distinct().Count());
            Assert.Contains(MechanicalDrive.Stowed, values);
            Assert.Contains(MechanicalDrive.Working, values);
            Assert.Contains(MechanicalDrive.Holding, values);
            Assert.Contains(MechanicalDrive.Disabled, values);
        }

        [Test]
        public void EveryCatalogVehicle_HasATableEntry()
        {
            foreach (VehicleDef def in VehicleCatalog.All)
            {
                Assert.IsNotNull(def);
                Assert.IsTrue(NebulaVisualCatalog.TryGetVehicle(def.Id, out NebulaVisualDefinition visual),
                    $"{def.Id} must have a visual");
                Assert.IsTrue(ShipVehicleMechanicalProfiles.HasRealMechanisms(visual.VisualId),
                    $"{def.Id} -> {visual.VisualId} must have real mechanisms");
            }
        }

        [Test]
        public void EachClassMechanismMatchesItsRole()
        {
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.scout.mk1")
                .All(p => p.Role == MechanicalRole.Deploy), "wings deploy, they do not spin");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.collector.mk2")
                .Any(p => p.Role == MechanicalRole.Rotary), "the Mk II carousel turns");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.engineer.mk2")
                .Any(p => p.NodeName == "part.recovery_winch" && p.Role == MechanicalRole.Rotary),
                "a winch winds");
        }

        [Test]
        public void OnlyTheEngineerMkTwo_HasARecoveryWinch()
        {
            Assert.IsFalse(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.engineer.mk1")
                .Any(p => p.NodeName.Contains("winch")), "the Mk I model carries none");
            Assert.IsTrue(ShipVehicleMechanicalProfiles.ForVehicle("vehicle.engineer.mk2")
                .Any(p => p.NodeName.Contains("winch")));
        }

        [Test]
        public void BothEscortMarks_HaveAFixedWeaponPart_AndTheyDiffer()
        {
            MechanicalPart[] mk1 = ShipVehicleMechanicalProfiles.ForVehicle("vehicle.escort.mk1");
            MechanicalPart[] mk2 = ShipVehicleMechanicalProfiles.ForVehicle("vehicle.escort.mk2");

            Assert.IsNotEmpty(mk1);
            Assert.IsNotEmpty(mk2);
            Assert.AreNotEqual(mk1[0].NodeName, mk2[0].NodeName, "each Mk has its own authored weapon part");
        }

        [Test]
        public void NoVehiclePart_ClaimsATransformAnotherPresenterDrives()
        {
            foreach (string id in ShipVehicleMechanicalProfiles.KnownVehicleIds)
                foreach (MechanicalPart part in ShipVehicleMechanicalProfiles.ForVehicle(id))
                {
                    Assert.IsFalse(ShipVehicleMechanicalProfiles.IsForeignOwned(part.NodeName), $"{id}");
                    Assert.IsFalse(EconomyMechanicalProfiles.IsTurretOwned(part.NodeName), $"{id}");
                }
        }

        [Test]
        public void MotionOff_MeansEveryMechanismSnapsRatherThanEases()
        {
            AnimationAccessibility.MotionOff = true;
            Assert.IsTrue(AnimationAccessibility.SnapStateChanges,
                "with motion off a state change lands immediately on its real pose");
            Assert.AreEqual(0f, AnimationAccessibility.MotionScale);
        }

        [Test]
        public void ReducedMotion_DampsWithoutHidingTheStateChange()
        {
            AnimationAccessibility.ReducedMotion = true;
            Assert.Less(AnimationAccessibility.MotionScale, 1f);
            Assert.Greater(AnimationAccessibility.MotionScale, 0f);
            Assert.IsFalse(AnimationAccessibility.SnapStateChanges,
                "reduced motion still animates; it does not snap");
        }
    }
}
