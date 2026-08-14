using NUnit.Framework;
using UnityEngine;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for ShipController's DT-001 session-local test speed multiplier (§4/§8).</summary>
    public class ShipControllerTestSpeedTests
    {
        private ShipController ship;

        [SetUp]
        public void CreateShip() => ship = new GameObject("test-ship").AddComponent<ShipController>();

        [TearDown]
        public void DestroyShip() { if (ship != null) Object.DestroyImmediate(ship.gameObject); }

        [Test]
        public void DefaultMultiplier_IsOne()
        {
            Assert.AreEqual(1f, ship.TestSpeedMultiplier);
        }

        [Test]
        public void SetTestSpeedMultiplier_NegativeValue_ClampsToOne()
        {
            ship.SetTestSpeedMultiplier(-10f);
            Assert.AreEqual(1f, ship.TestSpeedMultiplier);
        }

        [Test]
        public void SetTestSpeedMultiplier_Zero_ClampsToOne()
        {
            ship.SetTestSpeedMultiplier(0f);
            Assert.AreEqual(1f, ship.TestSpeedMultiplier);
        }

        [Test]
        public void SetTestSpeedMultiplier_AboveHundred_ClampsToHundred()
        {
            ship.SetTestSpeedMultiplier(9999f);
            Assert.AreEqual(100f, ship.TestSpeedMultiplier);
        }

        [Test]
        public void SetTestSpeedMultiplier_WithinRange_IsStoredExactly()
        {
            ship.SetTestSpeedMultiplier(37.5f);
            Assert.AreEqual(37.5f, ship.TestSpeedMultiplier);
        }

        [Test]
        public void ResetTestSpeedMultiplier_ReturnsToOne()
        {
            ship.SetTestSpeedMultiplier(80f);
            ship.ResetTestSpeedMultiplier();
            Assert.AreEqual(1f, ship.TestSpeedMultiplier);
        }
    }
}
