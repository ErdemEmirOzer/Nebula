using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §3/§18: the shared Shield→Armor→Hull cascade under per-layer weapon-family multipliers.</summary>
    public class Vs014DamageProfileTests
    {
        [Test]
        public void NeutralProfile_ReproducesTheUnprofiledCascadeExactly()
        {
            var a = new Durability(100f, 100f, 100f, startFraction: 1f);
            var b = new Durability(100f, 100f, 100f, startFraction: 1f);
            a.ApplyDamage(250f);
            b.ApplyDamageWithProfile(250f, 1f, 1f, 1f);
            Assert.AreEqual(a.Shield, b.Shield, 0.001f);
            Assert.AreEqual(a.Armor, b.Armor, 0.001f);
            Assert.AreEqual(a.Hull, b.Hull, 0.001f);
        }

        [Test]
        public void LaserProfile_ExactMultipliers()
        {
            Assert.AreEqual(1f, DamageProfile.Laser.ShieldMultiplier, 0.0001f);
            Assert.AreEqual(0.65f, DamageProfile.Laser.ArmorMultiplier, 0.0001f);
            Assert.AreEqual(1.25f, DamageProfile.Laser.HullMultiplier, 0.0001f);
        }

        [Test]
        public void PlasmaProfile_ExactMultipliers()
        {
            Assert.AreEqual(0.70f, DamageProfile.Plasma.ShieldMultiplier, 0.0001f);
            Assert.AreEqual(1.50f, DamageProfile.Plasma.ArmorMultiplier, 0.0001f);
            Assert.AreEqual(1.15f, DamageProfile.Plasma.HullMultiplier, 0.0001f);
        }

        [Test]
        public void EmProfile_ExactMultipliers()
        {
            Assert.AreEqual(2f, DamageProfile.Em.ShieldMultiplier, 0.0001f);
            Assert.AreEqual(0.35f, DamageProfile.Em.ArmorMultiplier, 0.0001f);
            Assert.AreEqual(0.50f, DamageProfile.Em.HullMultiplier, 0.0001f);
        }

        [Test]
        public void Overflow_CascadesShieldToArmorToHull_ConvertingPerLayerMultiplier()
        {
            // Durability's constructor always starts Shield at 0 regardless of startFraction
            // (correct, intentional behavior for its original under-construction-structure use
            // case -- see Durability's own constructor comment) -- a fixture that needs a real
            // FULL Shield/Armor/Hull starting point must force it explicitly via RestoreFromSave,
            // exactly like the live spawn paths (EnemyAgent.Create, ShipCombatState, VehicleAgent)
            // already do for the same reason.
            // Shield 50 (mult 1 -> consumes 50 raw), Armor 50 (mult 0.65 -> consumes 50/0.65 raw), Hull 1000.
            var d = new Durability(50f, 50f, 1000f, startFraction: 1f);
            d.RestoreFromSave(d.ShieldMax, d.ArmorMax, d.HullMax, 0f);
            float overflow = d.ApplyDamageWithProfile(200f, 1f, 0.65f, 1.25f);
            Assert.AreEqual(0f, d.Shield, 0.001f);
            Assert.AreEqual(0f, d.Armor, 0.001f);
            // remaining after shield: 200-50=150 raw; armor mult 0.65 -> hpConsumed=min(50,150*0.65=97.5)=50 -> rawConsumed=50/0.65=76.923...
            float rawAfterArmor = 150f - (50f / 0.65f);
            Assert.Greater(rawAfterArmor, 0f);
            float expectedHullDamage = rawAfterArmor * 1.25f;
            Assert.AreEqual(1000f - expectedHullDamage, d.Hull, 0.01f);
            Assert.AreEqual(0f, overflow, 0.01f); // hull absorbed everything, never Disabled
        }

        [Test]
        public void ZeroOrNegativeMultiplier_NeverThrows_ConsumesAllRemainingRawAtThatLayer()
        {
            var d = new Durability(0f, 100f, 100f, startFraction: 1f);
            Assert.DoesNotThrow(() => d.ApplyDamageWithProfile(50f, 0f, 1f, 1f));
        }

        [Test]
        public void DamageResolver_ApplyRoutesThroughDurabilityExactly()
        {
            // See the Overflow test above for why RestoreFromSave is required here: the
            // constructor alone never yields a full Shield, only startFraction of Armor/Hull.
            var d = new Durability(100f, 100f, 100f, startFraction: 1f);
            d.RestoreFromSave(d.ShieldMax, d.ArmorMax, d.HullMax, 0f);
            float overflow = DamageResolver.Apply(d, 50f, DamageProfile.Laser);
            Assert.AreEqual(50f, d.Shield, 0.001f); // Laser shield mult 1 -> consumes exactly 50 raw
            Assert.AreEqual(0f, overflow, 0.001f);
        }

        [Test]
        public void DamageResolver_NullDurabilityOrZeroDamage_NeverThrowsReturnsZero()
        {
            Assert.AreEqual(0f, DamageResolver.Apply(null, 50f, DamageProfile.Neutral));
            var d = new Durability(10f, 10f, 10f, 1f);
            Assert.AreEqual(0f, DamageResolver.Apply(d, 0f, DamageProfile.Neutral));
            Assert.AreEqual(0f, DamageResolver.Apply(d, -5f, DamageProfile.Neutral));
        }
    }
}
