using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Remote;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-017A §1: the pure parts of the intended-target continuity chain -- ProjectileState's own IntendedTargetId default/override behavior, and a pending attack whose target no longer exists deterministically missing rather than picking a new one. The live-spawn-site (EnemyAgent/SurfaceStructure) and demote/promote round-trip parts need a real scene and are covered in PlayMode.</summary>
    public class Vs017aProjectileContinuityTests
    {
        [Test]
        public void ProjectileState_IntendedTargetId_DefaultsToLockedTargetId_WhenNotExplicitlyGiven()
        {
            // Mirrors the player EM path (ShipCombat.cs) -- untouched by VS-017A, so it must keep
            // getting a real IntendedTargetId "for free" via this default.
            var state = new ProjectileState("id1", ProjectileKind.Em, 1, "ship", Vector3.zero, Vector3.forward, 10f, "enemy-1");
            Assert.AreEqual("enemy-1", state.IntendedTargetId);
            Assert.AreEqual("enemy-1", state.LockedTargetId);
        }

        [Test]
        public void ProjectileState_IntendedTargetId_ExplicitValueIsIndependentOfLockedTargetId()
        {
            // Mirrors the Siege/Plasma path -- no live lock at all (proximity-hit), but a real
            // stable intended target is still carried.
            var state = new ProjectileState("id2", ProjectileKind.EnemySiege, 1, "enemy-9", Vector3.zero, Vector3.forward, 10f, null, intendedTargetId: "structure-1/defense.0");
            Assert.AreEqual("structure-1/defense.0", state.IntendedTargetId);
            Assert.IsNull(state.LockedTargetId, "Siege/Plasma must never gain a live homing lock just because an intended target id now exists");
        }

        [Test]
        public void ProjectileState_NoIntendedOrLockedTarget_LeavesIntendedTargetIdNull()
        {
            var state = new ProjectileState("id3", ProjectileKind.EnemySiege, 1, "enemy-9", Vector3.zero, Vector3.forward, 10f, null);
            Assert.IsNull(state.IntendedTargetId);
        }

        [Test]
        public void PendingAttack_TargetNoLongerExists_DeterministicallyMisses_NeverRetargetsOrThrows()
        {
            var manifest = BuildEmptyManifest("sys-x");
            var ctx = new RemoteRaidContext(new Nebula.Sectors.SystemDelta { systemId = "sys-x" }, manifest, new ThreatPressureState());
            ctx.PendingAttacks.Add(new PendingAttack("atk-1", "enemy-1", "structure-that-doesnt-exist/defense.0", ProjectileKind.EnemySiege,
                ProjectileFaction.Hostile, damageAmount: 100f, remainingFlightSeconds: 0.5f, splashRadius: 0f, splashMaxFractionOfDirect: 0f,
                isEm: false, emDisruptSeconds: 0f, emImmunityAfterDisruptSeconds: 0f, interceptable: true, interceptionDurabilityRemaining: ProjectileCatalog.InterceptionDurability));

            Assert.DoesNotThrow(() => RemotePendingAttackTick.Tick(ctx, 1f));
            Assert.AreEqual(0, ctx.PendingAttacks.Count, "a shot whose target vanished must still resolve (consumed) exactly once, never linger or retry against a different target");
        }

        [Test]
        public void PendingAttack_NoTargetAtAll_ExpiresWithNoResult_NeverPicksANewRemoteTarget()
        {
            var manifest = BuildEmptyManifest("sys-y");
            var ctx = new RemoteRaidContext(new Nebula.Sectors.SystemDelta { systemId = "sys-y" }, manifest, new ThreatPressureState());
            var durability = new Nebula.Extraction.Durability(0f, 0f, 1000f, 1f);
            var enemyDef = EnemyCatalog.Get(EnemyIds.FighterMk1);
            var ai = new EnemyAiState(enemyDef);
            var enemy = new RemoteEnemyRuntime("e1", EnemyIds.FighterMk1, enemyDef, ai, durability, Vector3.zero, Vector3.zero);
            ctx.Enemies.Add(enemy);
            float hullBefore = enemy.Durability.Hull;

            // Faction.Player + empty targetId: a friendly shot whose intended target was never resolvable.
            ctx.PendingAttacks.Add(new PendingAttack("atk-2", "ship", "", ProjectileKind.Em, ProjectileFaction.Player,
                50f, 0.5f, 0f, 0f, false, 0f, 0f, false, 0f));

            RemotePendingAttackTick.Tick(ctx, 1f);
            Assert.AreEqual(hullBefore, enemy.Durability.Hull, 0.001f, "a targetless shot must never opportunistically hit some other live enemy instead");
            Assert.AreEqual(0, ctx.PendingAttacks.Count);
        }

        private static Nebula.Sectors.SystemManifest BuildEmptyManifest(string systemId)
        {
            var sector = new Nebula.Sectors.SectorCoordinate(0, 0, 0);
            return new Nebula.Sectors.SystemManifest(systemId, sector, 0, "Test", Nebula.Sectors.StarClass.Yellow, Vector3.zero,
                500000f, 50000f, false, Nebula.Sectors.SystemProfileKind.Balanced, 2, new System.Collections.Generic.List<Nebula.Sectors.PlanetManifest>());
        }
    }
}
