using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Hangar;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-03: how the four support-vehicle classes read on screen, and the
    /// guarantee that every pose is derived from a mission phase the hangar
    /// already owns rather than invented by the animation layer.
    /// </summary>
    public class Ani03SupportVehicleAnimationTests
    {
        [TearDown]
        public void ResetAccessibility() => AnimationAccessibility.ResetToDefaults();

        // --- phase mapping is total, and never invents a phase ---------------

        [Test]
        public void EveryMissionPhase_MapsToAPose_WithNoUnhandledFallthrough()
        {
            foreach (VehiclePhase phase in Enum.GetValues(typeof(VehiclePhase)).Cast<VehiclePhase>())
            {
                VehicleAnimationState state = VehicleAnimationProfiles.FromMissionPhase(phase);
                Assert.IsTrue(Enum.IsDefined(typeof(VehicleAnimationState), state),
                    $"{phase} must map to a real pose");
            }
        }

        [Test]
        public void MissionPhases_ReadAsTheThingTheVehicleIsActuallyDoing()
        {
            Assert.AreEqual(VehicleAnimationState.Docked, VehicleAnimationProfiles.FromMissionPhase(VehiclePhase.InHangar));
            Assert.AreEqual(VehicleAnimationState.Launching, VehicleAnimationProfiles.FromMissionPhase(VehiclePhase.Launching));
            Assert.AreEqual(VehicleAnimationState.Travel, VehicleAnimationProfiles.FromMissionPhase(VehiclePhase.TravelOut));
            Assert.AreEqual(VehicleAnimationState.Working, VehicleAnimationProfiles.FromMissionPhase(VehiclePhase.OnStation));
            Assert.AreEqual(VehicleAnimationState.Returning, VehicleAnimationProfiles.FromMissionPhase(VehiclePhase.TravelReturn));
            Assert.AreEqual(VehicleAnimationState.Docking, VehicleAnimationProfiles.FromMissionPhase(VehiclePhase.Docking));
        }

        [Test]
        public void InHangar_ReadsAsParked_NotAsFlying()
        {
            VehicleAnimationState docked = VehicleAnimationProfiles.FromMissionPhase(VehiclePhase.InHangar);
            Assert.IsTrue(VehicleAnimationProfiles.IsParked(docked));
            Assert.IsFalse(VehicleAnimationProfiles.IsFlying(docked));
            Assert.AreEqual(0f, VehicleAnimationProfiles.StateAmplitude(docked),
                "a parked vehicle leans into nothing");
        }

        [Test]
        public void ParkedAndFlying_AreMutuallyExclusive()
        {
            foreach (VehicleAnimationState state in Enum.GetValues(typeof(VehicleAnimationState)).Cast<VehicleAnimationState>())
                Assert.IsFalse(VehicleAnimationProfiles.IsParked(state) && VehicleAnimationProfiles.IsFlying(state),
                    $"{state} cannot be parked and flying at once");
        }

        // --- the other three mission types ----------------------------------

        [Test]
        public void EveryResupplyPhase_MapsToAPose()
        {
            foreach (MaintenanceResupplyPhase phase in Enum.GetValues(typeof(MaintenanceResupplyPhase)).Cast<MaintenanceResupplyPhase>())
                Assert.IsTrue(Enum.IsDefined(typeof(VehicleAnimationState), VehicleAnimationProfiles.FromResupplyPhase(phase)),
                    $"{phase} must map to a real pose");

            Assert.AreEqual(VehicleAnimationState.Working, VehicleAnimationProfiles.FromResupplyPhase(MaintenanceResupplyPhase.Loading));
            Assert.AreEqual(VehicleAnimationState.Travel, VehicleAnimationProfiles.FromResupplyPhase(MaintenanceResupplyPhase.ToReserve));
            Assert.AreEqual(VehicleAnimationState.Returning, VehicleAnimationProfiles.FromResupplyPhase(MaintenanceResupplyPhase.Returning));
        }

        [Test]
        public void FinishedAndCancelledResupply_BothComeToRest()
        {
            foreach (MaintenanceResupplyPhase over in new[] { MaintenanceResupplyPhase.Completed, MaintenanceResupplyPhase.Cancelled })
            {
                VehicleAnimationState state = VehicleAnimationProfiles.FromResupplyPhase(over);
                Assert.AreEqual(VehicleAnimationState.Completed, state);
                Assert.IsTrue(VehicleAnimationProfiles.IsParked(state), $"{over} must settle, not keep flying");
            }
        }

        [Test]
        public void EveryEscortSubState_MapsToAPose_AndRetreatReadsAsComingHome()
        {
            foreach (EscortSubState sub in Enum.GetValues(typeof(EscortSubState)).Cast<EscortSubState>())
                Assert.IsTrue(Enum.IsDefined(typeof(VehicleAnimationState), VehicleAnimationProfiles.FromEscortSubState(sub)),
                    $"{sub} must map to a real pose");

            Assert.AreEqual(VehicleAnimationState.Travel, VehicleAnimationProfiles.FromEscortSubState(EscortSubState.Following));
            Assert.AreEqual(VehicleAnimationState.Working, VehicleAnimationProfiles.FromEscortSubState(EscortSubState.Engaging));
            Assert.AreEqual(VehicleAnimationState.Returning, VehicleAnimationProfiles.FromEscortSubState(EscortSubState.Retreating));
        }

        // --- class character ------------------------------------------------

        [Test]
        public void AScoutLeansHarderThanALadenCollector()
        {
            float scout = VehicleAnimationProfiles.FlightCharacter(VehicleClass.Scout).bankScale;
            float collector = VehicleAnimationProfiles.FlightCharacter(VehicleClass.Collector).bankScale;
            float engineer = VehicleAnimationProfiles.FlightCharacter(VehicleClass.Engineer).bankScale;
            float escort = VehicleAnimationProfiles.FlightCharacter(VehicleClass.Escort).bankScale;

            Assert.Greater(scout, escort, "nothing turns harder than a Scout");
            Assert.Greater(escort, engineer, "an Escort is sharper than a work platform");
            Assert.Greater(engineer, collector, "a laden Collector is the least agile");
        }

        [Test]
        public void EveryClassStaysWithinItsBudget()
        {
            foreach (VehicleClass cls in Enum.GetValues(typeof(VehicleClass)).Cast<VehicleClass>())
            {
                (float bank, float pitch) = VehicleAnimationProfiles.FlightCharacter(cls);
                Assert.Greater(bank, 0f, $"{cls} must still read as flying");
                Assert.LessOrEqual(bank, 1f, $"{cls} must not exceed the class budget");
                Assert.Greater(pitch, 0f);
                Assert.LessOrEqual(pitch, 1f);
            }
        }

        [Test]
        public void TheBankCeiling_StaysInsideTheSameComfortLimitThePlayerShipObeys()
        {
            Assert.LessOrEqual(VehicleAnimationProfiles.MaxBankDegrees, AnimationAccessibility.MaxVisualBankDegrees);
            Assert.Greater(VehicleAnimationProfiles.MaxBankDegrees, 0f);
            Assert.LessOrEqual(VehicleAnimationProfiles.MaxPitchDegrees, VehicleAnimationProfiles.MaxBankDegrees);
        }

        // --- state amplitude ------------------------------------------------

        [Test]
        public void LiningUpOnAHangar_IsCalmerThanOpenTravel()
        {
            float travel = VehicleAnimationProfiles.StateAmplitude(VehicleAnimationState.Travel);
            float approach = VehicleAnimationProfiles.StateAmplitude(VehicleAnimationState.Approach);
            float docking = VehicleAnimationProfiles.StateAmplitude(VehicleAnimationState.Docking);

            Assert.Greater(travel, approach, "an approach is more careful than open travel");
            Assert.Greater(approach, docking, "the final docking pass is the calmest of all");
            Assert.Greater(docking, 0f, "it is still flying, just barely");
        }

        [Test]
        public void ADisabledVehicle_HoldsASafeClosedPose()
        {
            Assert.AreEqual(0f, VehicleAnimationProfiles.StateAmplitude(VehicleAnimationState.Disabled));
            Assert.AreEqual(0f, VehicleAnimationProfiles.StateAmplitude(VehicleAnimationState.Recovery),
                "a stranded vehicle under tow does not fly itself");
        }

        [Test]
        public void NoStateEverExceedsFullAmplitude()
        {
            foreach (VehicleAnimationState state in Enum.GetValues(typeof(VehicleAnimationState)).Cast<VehicleAnimationState>())
            {
                float amplitude = VehicleAnimationProfiles.StateAmplitude(state);
                Assert.GreaterOrEqual(amplitude, 0f, $"{state} amplitude must not be negative");
                Assert.LessOrEqual(amplitude, 1f, $"{state} must not exceed the budget");
            }
        }

        // --- accessibility --------------------------------------------------

        [Test]
        public void MotionOff_ZeroesEveryVehiclePose()
        {
            AnimationAccessibility.MotionOff = true;

            foreach (VehicleAnimationState state in Enum.GetValues(typeof(VehicleAnimationState)).Cast<VehicleAnimationState>())
                Assert.AreEqual(0f, VehicleAnimationProfiles.StateAmplitude(state) * AnimationAccessibility.MotionScale,
                    $"{state} must be completely still with motion off");
        }

        [Test]
        public void ReducedMotion_DampsButNeverInvertsTheOrdering()
        {
            float travelFull = VehicleAnimationProfiles.StateAmplitude(VehicleAnimationState.Travel);
            float dockingFull = VehicleAnimationProfiles.StateAmplitude(VehicleAnimationState.Docking);

            AnimationAccessibility.ReducedMotion = true;
            float scale = AnimationAccessibility.MotionScale;

            Assert.Less(scale, 1f, "reduced motion must actually reduce");
            Assert.Greater(scale, 0f, "reduced motion is not motion off");
            Assert.Greater(travelFull * scale, dockingFull * scale, "the ordering survives the damping");
        }

        // --- the seven real vehicles ----------------------------------------

        [Test]
        public void EveryCatalogVehicle_HasAFlightCharacter()
        {
            Assert.AreEqual(7, VehicleCatalog.All.Length, "ANI-03 covers the seven catalog vehicles");

            foreach (VehicleDef def in VehicleCatalog.All)
            {
                Assert.IsNotNull(def);
                (float bank, float pitch) = VehicleAnimationProfiles.FlightCharacter(def.Class);
                Assert.Greater(bank, 0f, $"{def.Id} must have a defined flight character");
                Assert.Greater(pitch, 0f, $"{def.Id} must have a defined flight character");
            }
        }

        [Test]
        public void BothMarksOfAClass_ShareThatClassesCharacter()
        {
            // A Mk II is a better vehicle, not a different kind of flyer.
            Assert.AreEqual(VehicleCatalog.Collector.Class, VehicleCatalog.CollectorMk2.Class);
            Assert.AreEqual(VehicleCatalog.EngineerMk1.Class, VehicleCatalog.EngineerMk2.Class);
            Assert.AreEqual(VehicleCatalog.EscortMk1.Class, VehicleCatalog.EscortMk2.Class);
        }
    }
}
