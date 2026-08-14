using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-013.1 §3/§7 regression: every pre-existing non-combat key owner
    /// must still own exactly its original key after the combat remap, and
    /// J/Z/Tab/Alpha0/save/load must be untouched. Combined with
    /// CombatBindingValidatorTests (which proves the new combat defaults
    /// never collide with these), this is the "scan every real key owner"
    /// regression the spec calls for.
    /// </summary>
    public class Vs0131KeyOwnershipRegressionTests
    {
        [Test]
        public void PlanetEvacuationAndReclaim_StillOwnVAndC()
        {
            Assert.AreEqual(KeyCode.V, ShipPlanetEvacuation.EvacuateKey);
            Assert.AreEqual(KeyCode.C, ShipPlanetEvacuation.ReclaimKey);
        }

        [Test]
        public void LogisticsCenter_StillOwnsFTNAndX()
        {
            Assert.AreEqual(KeyCode.F, ShipLogisticsCenter.BuildOrUpgradeKey);
            Assert.AreEqual(KeyCode.T, ShipLogisticsCenter.WithdrawKey);
            Assert.AreEqual(KeyCode.N, ShipLogisticsCenter.DepositKey);
            Assert.AreEqual(KeyCode.X, ShipLogisticsCenter.DismantleKey);
        }

        [Test]
        public void ExtractionScan_StillOwnsR()
        {
            Assert.AreEqual(KeyCode.R, ShipExtraction.ScanKey);
        }

        /// <summary>
        /// Tab is deliberately absent from this list now. It asserted
        /// <c>HudToggleKey == Tab</c> for a binding no runtime file read — see
        /// <c>NebulaInputBindingsTests.TabIsNoLongerReserved_BecauseNothingOwnsIt</c>
        /// for the deletion and the reason. The three shortcuts below all have real
        /// consumers (<c>SectorDebugHud.cs:69</c>, <c>ExtractionDirector.cs:102-103</c>)
        /// and are still pinned.
        /// </summary>
        [Test]
        public void SaveLoadSystemDebugKeys_Untouched()
        {
            Assert.AreEqual(KeyCode.Alpha0, NebulaInputBindings.SectorDebugToggleKey);
            Assert.AreEqual(KeyCode.Comma, NebulaInputBindings.ManualSaveKey);
            Assert.AreEqual(KeyCode.Period, NebulaInputBindings.ManualLoadKey);
        }

        [Test]
        public void NoCombatDefaultBinding_CollidesWithAnyRealNonCombatKeyOwner()
        {
            var realOwners = new[]
            {
                ShipPlanetEvacuation.EvacuateKey, ShipPlanetEvacuation.ReclaimKey,
                ShipLogisticsCenter.BuildOrUpgradeKey, ShipLogisticsCenter.WithdrawKey,
                ShipLogisticsCenter.DepositKey, ShipLogisticsCenter.DismantleKey,
                ShipExtraction.ScanKey,
                NebulaInputBindings.SectorDebugToggleKey,
                NebulaInputBindings.ManualSaveKey, NebulaInputBindings.ManualLoadKey,
            };

            foreach (var (action, key) in CombatBindings.CurrentBindings())
            {
                // Mouse buttons are a deliberate exception (documented dual-purpose with cursor
                // capture) -- every other default combat key must be disjoint from every real
                // non-combat key owner listed above.
                if (key == KeyCode.Mouse0 || key == KeyCode.Mouse1 || key == KeyCode.Mouse2) continue;
                foreach (KeyCode owned in realOwners)
                    Assert.AreNotEqual(owned, key, $"combat action {action} (key {key}) collides with a real non-combat key owner");
            }
        }
    }
}
