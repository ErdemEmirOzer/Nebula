using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Sectors;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for VS-013.2's Safety Marker position authority determinism and the pure physical repair safe-zone boundary decision.</summary>
    public class SafetyMarkerAnchorTests
    {
        [TearDown]
        public void Cleanup() => StarSystemRegistry.Clear();

        private static SystemManifest StartManifest(int seed)
        {
            var context = new NebulaContext();
            new WorldInstaller().PrepareSession(seed, context);
            return context.StartSystem;
        }

        [Test]
        public void ComputePosition_SameManifest_IsFullyDeterministic()
        {
            SystemManifest manifest = StartManifest(9001);
            Vector3 first = SafetyMarkerAnchor.ComputePosition(manifest);
            Vector3 second = SafetyMarkerAnchor.ComputePosition(manifest);
            Assert.AreEqual(first, second);
        }

        [Test]
        public void ComputePosition_IsNeverAtTheStarCenter()
        {
            SystemManifest manifest = StartManifest(9002);
            Vector3 marker = SafetyMarkerAnchor.ComputePosition(manifest);
            Assert.AreNotEqual(manifest.AbsoluteCenter, marker);
        }

        [Test]
        public void ComputePosition_MatchesPlayerInstallersOwnShipSpawnFormula()
        {
            SystemManifest manifest = StartManifest(9003);
            float firstOrbit = manifest.Planets.Count > 0 ? manifest.Planets[0].OrbitRadius : 40000f;
            Vector3 expected = manifest.AbsoluteCenter + new Vector3(0f, firstOrbit * 0.15f, -firstOrbit * 0.85f);
            Assert.AreEqual(expected, SafetyMarkerAnchor.ComputePosition(manifest));
        }
    }

    /// <summary>EditMode tests for the pure RepairSafeZoneResolver distance decision (VS-013.2 §1) — real physical proximity, never system ownership alone.</summary>
    public class RepairSafeZoneResolverTests
    {
        [Test]
        public void WithinRadius_OfSafetyMarker_Is100PercentSafe()
        {
            Vector3 ship = new Vector3(1499f, 0f, 0f);
            Vector3 marker = Vector3.zero;
            Assert.IsTrue(RepairSafeZoneResolver.IsSafe(ship, marker, new List<Vector3>()));
        }

        [Test]
        public void ExactlyAtTheRadiusBoundary_1500Units_IsStillSafe()
        {
            Vector3 ship = new Vector3(1500f, 0f, 0f);
            Vector3 marker = Vector3.zero;
            Assert.IsTrue(RepairSafeZoneResolver.IsSafe(ship, marker, new List<Vector3>()));
        }

        [Test]
        public void JustOverTheRadiusBoundary_1501Units_IsNotSafe()
        {
            Vector3 ship = new Vector3(1501f, 0f, 0f);
            Vector3 marker = Vector3.zero;
            Assert.IsFalse(RepairSafeZoneResolver.IsSafe(ship, marker, new List<Vector3>()));
        }

        [Test]
        public void FarFromMarker_WithNoActiveCommandCenters_IsNotSafe()
        {
            Vector3 ship = new Vector3(100000f, 0f, 0f);
            Vector3 marker = Vector3.zero;
            Assert.IsFalse(RepairSafeZoneResolver.IsSafe(ship, marker, new List<Vector3>()));
        }

        [Test]
        public void NearAnActiveCommandCenter_IsSafe_EvenFarFromTheMarker()
        {
            Vector3 ship = new Vector3(100000f, 0f, 0f);
            Vector3 marker = Vector3.zero;
            var commandCenters = new List<Vector3> { new Vector3(100500f, 0f, 0f) };
            Assert.IsTrue(RepairSafeZoneResolver.IsSafe(ship, marker, commandCenters));
        }

        [Test]
        public void FarFromAnActiveCommandCenter_InTheSameSystem_IsNotSafe()
        {
            Vector3 ship = new Vector3(100000f, 0f, 0f);
            Vector3 marker = Vector3.zero;
            var commandCenters = new List<Vector3> { new Vector3(150000f, 0f, 0f) }; // 50000 units away, same system
            Assert.IsFalse(RepairSafeZoneResolver.IsSafe(ship, marker, commandCenters));
        }

        [Test]
        public void NoSafetyMarker_OnlyActiveCommandCenterCandidatesConsidered()
        {
            Vector3 ship = new Vector3(500f, 0f, 0f);
            var commandCenters = new List<Vector3> { new Vector3(600f, 0f, 0f) };
            Assert.IsTrue(RepairSafeZoneResolver.IsSafe(ship, null, commandCenters));
        }

        [Test]
        public void CapFraction_MapsSafeToSafeZoneCap_UnsafeToFieldCap()
        {
            Assert.AreEqual(RepairCatalog.SafeZoneCapFraction, RepairSafeZoneResolver.CapFraction(true));
            Assert.AreEqual(RepairCatalog.FieldCapFraction, RepairSafeZoneResolver.CapFraction(false));
        }

        [Test]
        public void FloatingOriginShift_DoesNotChangeTheAbsoluteDistanceResult()
        {
            // Both ship and marker positions are already "absolute" by convention (caller adds
            // FloatingOrigin.TotalOffset before calling); shifting both by the same offset must
            // never change the outcome, exactly like every other absolute-distance check in the project.
            Vector3 ship = new Vector3(1499f, 0f, 0f);
            Vector3 marker = Vector3.zero;
            bool before = RepairSafeZoneResolver.IsSafe(ship, marker, new List<Vector3>());

            Vector3 shiftedOrigin = new Vector3(500000f, 12345f, -98765f);
            bool after = RepairSafeZoneResolver.IsSafe(ship + shiftedOrigin, marker + shiftedOrigin, new List<Vector3>());

            Assert.AreEqual(before, after);
            Assert.IsTrue(after);
        }
    }
}
