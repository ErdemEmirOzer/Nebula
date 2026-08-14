using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.UI;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for DT-001's J/Z input-ownership split and untouched schema/generation constants (§3/§8).</summary>
    public class DT001KeyOwnershipTests
    {
        /// <summary>
        /// Every process-wide input static back to its shipped state before each test.
        /// <para>
        /// The rebindable catalog, the router's registered contexts and its substituted
        /// key source are all statics that outlive a fixture. Without a reset on entry
        /// this fixture measured whatever the previous fixture in the same Unity process
        /// happened to leave behind, which is what made a batched run disagree with the
        /// same tests run alone. The real public reset contract, never reflection.
        /// </para>
        /// </summary>
        [SetUp]
        public void ResetProcessWideInputState()
        {
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
        }

        [TearDown]
        public void RestoreProcessWideInputState()
        {
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
        }

        [Test]
        public void SensorRelayKey_IsZ()
        {
            Assert.AreEqual(KeyCode.Z, ShipPoi.RelayChoiceKey);
        }

        [Test]
        public void TestSpeedPanelKey_IsJ()
        {
            Assert.AreEqual(KeyCode.J, FlightTestSpeedHud.ToggleKey);
        }

        [Test]
        public void NoTwoPoiOrTestSpeedInputConsumers_ShareAKey()
        {
            var keys = new[] { FlightTestSpeedHud.ToggleKey, ShipPoi.RelayChoiceKey, ShipPoi.StartOperationKey, ShipPoi.CancelKey };
            for (int i = 0; i < keys.Length; i++)
                for (int j = i + 1; j < keys.Length; j++)
                    Assert.AreNotEqual(keys[i], keys[j], $"keys[{i}] and keys[{j}] must not collide");
        }

        [Test]
        public void SaveSchema_IsPinned_UnaffectedByDT001()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
        }

        /// <summary>
        /// Old contract: this pinned 2, the generation version in force when DT-001 split the J/Z keys.
        /// Current production authority: <see cref="SectorManifestGenerator.GenerationVersion"/> = 3
        /// (Assets/Scripts/Nebula/Sectors/SectorManifestGenerator.cs) -- 9d33695 moved systems off the
        /// uniform 2x2x2 sub-cell grid onto the galaxy spiral arms, a deliberate world-generation change
        /// that regenerates every existing campaign's universe and was approved as such.
        /// Invariant still protected: an input-ownership change touches no world generation, so DT-001
        /// must leave that number where it is. The literal is deliberate -- reading the constant back
        /// would assert nothing, so a bump has to break this test and be re-pinned alongside the
        /// world-gen change that earned it.
        /// </summary>
        [Test]
        public void GenerationVersion_IsPinnedToThree_UnaffectedByDT001()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
        }
    }
}
