using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §15/§18: hostile projectile interception idempotency, splash falloff sharing, and the generalized active-defense-weapon predicate.</summary>
    public class Vs014InterceptionAndMiscTests
    {
        [Test]
        public void ApplyInterceptionHit_NeverAppliesASecondResultOnceShotDown()
        {
            var state = new ProjectileState("p1", ProjectileKind.EnemySiege, 1, "enemy.0001", UnityEngine.Vector3.zero,
                UnityEngine.Vector3.forward, 10f, null, ProjectileFaction.Hostile, interceptable: true, interceptionDurability: 10f, damageAmount: 50f);
            state.ApplyInterceptionHit(10f);
            Assert.IsTrue(state.InterceptedDown);
            state.ApplyInterceptionHit(999f); // must be a no-op
            Assert.AreEqual(0f, state.InterceptionDurabilityRemaining, 0.001f);
        }

        [Test]
        public void ApplyInterceptionHit_NeverAppliesToANonInterceptableProjectile()
        {
            var state = new ProjectileState("p2", ProjectileKind.Plasma, 1, "ship", UnityEngine.Vector3.zero,
                UnityEngine.Vector3.forward, 10f, null, ProjectileFaction.Player, interceptable: false);
            state.ApplyInterceptionHit(50f);
            Assert.IsFalse(state.InterceptedDown); // interceptable=false -> InterceptedDown is always false by definition
        }

        [Test]
        public void PlasmaSplashDamageForDirect_LinearFalloffToZeroAtRadius()
        {
            float atZero = WeaponCatalog.PlasmaSplashDamageForDirect(100f, 0f);
            float atHalfRadius = WeaponCatalog.PlasmaSplashDamageForDirect(100f, WeaponCatalog.PlasmaSplashRadius * 0.5f);
            float atRadius = WeaponCatalog.PlasmaSplashDamageForDirect(100f, WeaponCatalog.PlasmaSplashRadius);
            float beyondRadius = WeaponCatalog.PlasmaSplashDamageForDirect(100f, WeaponCatalog.PlasmaSplashRadius + 1f);

            Assert.AreEqual(100f * WeaponCatalog.PlasmaSplashMaxFractionOfDirect, atZero, 0.01f);
            Assert.Less(atHalfRadius, atZero);
            Assert.Greater(atHalfRadius, 0f);
            Assert.AreEqual(0f, atRadius, 0.001f);
            Assert.AreEqual(0f, beyondRadius, 0.001f);
        }

        [Test]
        public void PlasmaSplashDamageForDirect_SharedFormula_MatchesTierBasedOverload()
        {
            float viaDirect = WeaponCatalog.PlasmaSplashDamageForDirect(WeaponCatalog.PlasmaDirectDamage(1), 60f);
            float viaTier = WeaponCatalog.PlasmaSplashDamage(1, 60f);
            Assert.AreEqual(viaTier, viaDirect, 0.001f);
        }

        [Test]
        public void IsActiveDefenseWeapon_TrueForBothLaserAndPlasma_NeverForCommand()
        {
            Assert.IsTrue(SurfaceStructureCatalog.IsActiveDefenseWeapon(SurfaceStructureType.Laser));
            Assert.IsTrue(SurfaceStructureCatalog.IsActiveDefenseWeapon(SurfaceStructureType.Plasma));
            Assert.IsFalse(SurfaceStructureCatalog.IsActiveDefenseWeapon(SurfaceStructureType.Command));
        }

        [Test]
        public void ProjectileState_HostileFaction_CarriesItsOwnDamageAmount_NeverZeroForARealHit()
        {
            var state = new ProjectileState("p3", ProjectileKind.EnemySiege, 1, "enemy.0002", UnityEngine.Vector3.zero,
                UnityEngine.Vector3.forward, 20f, null, ProjectileFaction.Hostile, true, ProjectileCatalog.InterceptionDurability, damageAmount: 360f);
            Assert.AreEqual(360f, state.DamageAmount, 0.001f);
            Assert.AreEqual(ProjectileFaction.Hostile, state.Faction);
        }

        [Test]
        public void SiegeProjectileSpeed_IsSlowerThanEveryPlayerProjectile()
        {
            Assert.Less(ProjectileCatalog.SiegeProjectileSpeed, WeaponCatalog.PlasmaProjectileSpeed);
        }
    }
}
