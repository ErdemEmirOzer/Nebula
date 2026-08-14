using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for Plasma/EM physical projectile kinematics (VS-013 §3/§11).</summary>
    public class ProjectileStateTests
    {
        [Test]
        public void Tick_IntegratesPositionByVelocityAndDecaysLifetime()
        {
            var p = new ProjectileState("proj-1", ProjectileKind.Plasma, 1, "player",
                Vector3.zero, new Vector3(1100f, 0f, 0f), lifetimeSeconds: 5f, lockedTargetId: null);
            p.Tick(1f);
            Assert.AreEqual(new Vector3(1100f, 0f, 0f), p.Position);
            Assert.AreEqual(4f, p.RemainingLifetimeSeconds, 0.001f);
        }

        [Test]
        public void Expired_TrueOnceLifetimeReachesZero_NeverNegative()
        {
            var p = new ProjectileState("proj-1", ProjectileKind.Plasma, 1, "player",
                Vector3.zero, Vector3.forward, lifetimeSeconds: 1f, lockedTargetId: null);
            p.Tick(5f);
            Assert.IsTrue(p.Expired);
            Assert.AreEqual(0f, p.RemainingLifetimeSeconds);
        }

        [Test]
        public void GuideToward_TurnsVelocityTowardTarget_PreservingSpeed_WhileLocked()
        {
            var p = new ProjectileState("proj-2", ProjectileKind.Em, 1, "player",
                Vector3.zero, new Vector3(1600f, 0f, 0f), lifetimeSeconds: 5f, lockedTargetId: "enemy-1");
            float speedBefore = p.Velocity.magnitude;
            p.GuideToward(new Vector3(0f, 1600f, 0f), maxTurnRateDegreesPerSecond: 45f, dt: 1f);
            Assert.AreEqual(speedBefore, p.Velocity.magnitude, 0.01f, "guidance must preserve speed, only change heading");
            Assert.Greater(p.Velocity.y, 0f, "must have turned toward the target");
        }

        [Test]
        public void GuideToward_IsNoOp_OnceLockIsLost()
        {
            var p = new ProjectileState("proj-3", ProjectileKind.Em, 1, "player",
                Vector3.zero, new Vector3(1600f, 0f, 0f), lifetimeSeconds: 5f, lockedTargetId: "enemy-1");
            p.LoseLock();
            Vector3 before = p.Velocity;
            p.GuideToward(new Vector3(0f, 9999f, 0f), 45f, 1f);
            Assert.AreEqual(before, p.Velocity, "an unlocked EM projectile must continue dead-straight on its last heading");
        }

        [Test]
        public void LoseLock_ClearsLockedTargetId()
        {
            var p = new ProjectileState("proj-4", ProjectileKind.Em, 1, "player",
                Vector3.zero, Vector3.forward, 5f, "enemy-1");
            p.LoseLock();
            Assert.IsNull(p.LockedTargetId);
        }

        [Test]
        public void RestorePosition_OverwritesAllKinematicFields()
        {
            var p = new ProjectileState("proj-5", ProjectileKind.Plasma, 1, "player",
                Vector3.zero, Vector3.forward, 5f, null);
            p.RestorePosition(new Vector3(10f, 20f, 30f), new Vector3(1f, 2f, 3f), 2.5f, "enemy-9");
            Assert.AreEqual(new Vector3(10f, 20f, 30f), p.Position);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), p.Velocity);
            Assert.AreEqual(2.5f, p.RemainingLifetimeSeconds);
            Assert.AreEqual("enemy-9", p.LockedTargetId);
        }
    }

    /// <summary>EditMode tests for ship-ship/ship-enemy collision damage math (VS-013 §6/§11).</summary>
    public class ShipCollisionResolverTests
    {
        [Test]
        public void RelativeSpeedAtOrBelow80_NeverDamages()
        {
            Assert.IsFalse(ShipCollisionResolver.ShouldDamage(80f));
            Assert.IsFalse(ShipCollisionResolver.ShouldDamage(50f));
            Assert.AreEqual(0f, ShipCollisionResolver.DamageForEvent(80f, 1000f));
        }

        [Test]
        public void AboveThreshold_Damages_CappedAt15PercentOfVictimMaxHull()
        {
            // Huge excess speed to force the cap to bind.
            float damage = ShipCollisionResolver.DamageForEvent(10000f, 1000f);
            Assert.AreEqual(150f, damage, 0.01f, "must never exceed 15% of the victim's own max Hull");
        }

        [Test]
        public void JustAboveThreshold_ScalesBelowCap()
        {
            float damage = ShipCollisionResolver.DamageForEvent(90f, 1000f); // 10 u/s excess * 2 = 20, well under the 150 cap
            Assert.AreEqual(20f, damage, 0.01f);
        }

        [Test]
        public void MinimumSeparation_SumsRadiiPlusMargin()
        {
            Assert.AreEqual(50f + 60f + 50f, ShipCollisionResolver.MinimumSeparation(50f, 60f));
        }
    }
}
