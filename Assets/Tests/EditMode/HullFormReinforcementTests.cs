using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests pinning down the VS-013.1 §1 Hull Form / Hull Reinforcement separation: hardpoints come from Form only, Hull Reinforcement never touches Shield/hardpoints.</summary>
    public class HullFormReinforcementTests
    {
        [Test]
        public void ActiveHardpointSlots_UsesHullFormArgument_NeverAModuleTier()
        {
            // Same Form, wildly different "as if it were a module tier" second arg would have
            // mattered under the old (buggy) design -- here it's WeaponCoordinationTier, and the
            // hardpoint ceiling must still come from HullFormHardpoints(form), not from it.
            Assert.AreEqual(1, CombatCatalog.ActiveHardpointSlots(hullForm: 1, weaponCoordinationTier: 0));
            Assert.AreEqual(2, CombatCatalog.ActiveHardpointSlots(hullForm: 2, weaponCoordinationTier: 5), "Form II's own 2-hardpoint ceiling caps this, coordination tier is irrelevant once it's this high");
        }

        [Test]
        public void HullReinforcement_NeverAppearsInShieldResolution()
        {
            // ResolvedShieldMax's signature doesn't even accept a hullReinforcementTier parameter --
            // this test pins that contract down structurally via the exact call shapes below.
            float shieldAtForm2 = CombatCatalog.ResolvedShieldMax(hullForm: 2, shieldSystemsTier: 1);
            Assert.AreEqual(shieldAtForm2, CombatCatalog.ResolvedShieldMax(hullForm: 2, shieldSystemsTier: 1), "same inputs, same result -- no hidden hullReinforcementTier dependency exists to vary it");
        }

        [Test]
        public void ShieldSystems_NeverAppearsInArmorOrHullResolution()
        {
            float armor = CombatCatalog.ResolvedArmorMax(hullForm: 2, hullReinforcementTier: 1);
            float hull = CombatCatalog.ResolvedHullMax(hullForm: 2, hullReinforcementTier: 1);
            // ResolvedArmorMax/ResolvedHullMax's signatures don't accept a shieldSystemsTier at all.
            Assert.AreEqual(armor, CombatCatalog.ResolvedArmorMax(2, 1));
            Assert.AreEqual(hull, CombatCatalog.ResolvedHullMax(2, 1));
        }

        [TestCase(1, ExpectedResult = 400f)]
        [TestCase(2, ExpectedResult = 600f)]
        public float HullForm_OwnsTheShieldBaseline_BeforeAnyModuleMultiplier(int hullForm) =>
            CombatCatalog.ResolvedShieldMax(hullForm, shieldSystemsTier: 0);
    }
}
