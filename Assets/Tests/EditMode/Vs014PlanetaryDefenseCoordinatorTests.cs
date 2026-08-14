using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §4/§18: defense mode cycling, focus, target reservation, priority ordering, Command-disabled fallback.</summary>
    public class Vs014PlanetaryDefenseCoordinatorTests
    {
        [TearDown]
        public void TearDown() => EnemyAgent.ClearRegistry();

        [Test]
        public void GetMode_DefaultsToRolePriority()
        {
            var c = new PlanetaryDefenseCoordinator();
            Assert.AreEqual(DefenseMode.RolePriority, c.GetMode("planet.a"));
        }

        [Test]
        public void CycleMode_GoesRolePriority_Balanced_FocusTarget_RolePriority()
        {
            var c = new PlanetaryDefenseCoordinator();
            c.CycleMode("planet.a");
            Assert.AreEqual(DefenseMode.Balanced, c.GetMode("planet.a"));
            c.CycleMode("planet.a");
            Assert.AreEqual(DefenseMode.FocusTarget, c.GetMode("planet.a"));
            c.CycleMode("planet.a");
            Assert.AreEqual(DefenseMode.RolePriority, c.GetMode("planet.a"));
        }

        [Test]
        public void CycleMode_NeverAffectsADifferentPlanet()
        {
            var c = new PlanetaryDefenseCoordinator();
            c.CycleMode("planet.a");
            Assert.AreEqual(DefenseMode.RolePriority, c.GetMode("planet.b"));
        }

        [Test]
        public void SetFocus_IgnoredWhenEnemyIdIsNullOrEmpty()
        {
            var c = new PlanetaryDefenseCoordinator();
            c.SetFocus("planet.a", null);
            c.SetFocus("planet.a", "");
            Assert.IsNull(c.GetFocus("planet.a"));
        }

        [Test]
        public void SetFocus_StoresRealValue()
        {
            var c = new PlanetaryDefenseCoordinator();
            c.SetFocus("planet.a", "enemy.0001");
            Assert.AreEqual("enemy.0001", c.GetFocus("planet.a"));
        }

        [Test]
        public void Snapshot_OnlyIncludesPlanetsWithNonDefaultModeOrFocus()
        {
            var c = new PlanetaryDefenseCoordinator();
            c.CycleMode("planet.a"); // -> Balanced
            c.SetFocus("planet.b", "enemy.0002");
            var snapshot = new List<(string, DefenseMode, string)>(c.Snapshot());
            Assert.AreEqual(2, snapshot.Count);
        }

        [Test]
        public void Restore_ReappliesModeAndFocusExactly()
        {
            var c = new PlanetaryDefenseCoordinator();
            c.Restore("planet.a", DefenseMode.FocusTarget, "enemy.0003");
            Assert.AreEqual(DefenseMode.FocusTarget, c.GetMode("planet.a"));
            Assert.AreEqual("enemy.0003", c.GetFocus("planet.a"));
        }

        [Test]
        public void ResolveSharedTarget_CommandDisabled_UsesLocalNearestPick_IgnoringMode()
        {
            var c = new PlanetaryDefenseCoordinator();
            var far = MakeEnemy("far", new Vector3(1000f, 0f, 0f), EnemyRole.Fighter);
            var near = MakeEnemy("near", new Vector3(10f, 0f, 0f), EnemyRole.Looter);
            try
            {
                EnemyAgent picked = c.ResolveSharedTarget("planet.a", commandActive: false, SurfaceStructureType.Laser,
                    new List<EnemyAgent> { far, near }, Vector3.zero, 0.1f);
                Assert.AreEqual(near, picked);
            }
            finally { Object.DestroyImmediate(far.gameObject); Object.DestroyImmediate(near.gameObject); }
        }

        [Test]
        public void ResolveSharedTarget_NoCandidates_ReturnsNull()
        {
            var c = new PlanetaryDefenseCoordinator();
            Assert.IsNull(c.ResolveSharedTarget("planet.a", true, SurfaceStructureType.Laser, new List<EnemyAgent>(), Vector3.zero, 0.1f));
        }

        [Test]
        public void ResolveSharedTarget_CommandDisabled_TurnMultiplierConstant_IsExactly0Point60()
        {
            Assert.AreEqual(0.60f, PlanetaryDefenseCoordinator.CommandDisabledAcquisitionMultiplier, 0.001f);
        }

        [Test]
        public void TargetReservationSeconds_IsExactly1Point5()
        {
            Assert.AreEqual(1.5f, PlanetaryDefenseCoordinator.TargetReservationSeconds, 0.001f);
        }

        private static EnemyAgent MakeEnemy(string id, Vector3 position, EnemyRole role)
        {
            EnemyDef def = role switch
            {
                EnemyRole.Looter => EnemyCatalog.LooterTransportMk1,
                EnemyRole.Heavy => EnemyCatalog.HeavyAssaultMk1,
                EnemyRole.Siege => EnemyCatalog.SiegeMk1,
                _ => EnemyCatalog.FighterMk1
            };
            return EnemyAgent.Create(id, def, position, null, null, null);
        }
    }
}
