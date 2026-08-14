using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A.1 §1/§2D: advances every in-flight <see cref="PendingAttack"/>
    /// in a remote system by one tick and resolves whichever ones are ready
    /// -- the single authority for "did this attack land" in a non-Full
    /// system, exactly mirroring what live per-frame Update()/TickInterception
    /// on <see cref="ProjectileObject"/> already does, just headless and at
    /// the distant tick. No target-lost pending attack ever re-acquires a new remote
    /// target -- it simply expires (never applies, never respawns).
    /// </summary>
    public static class RemotePendingAttackTick
    {
        public static void Tick(RemoteRaidContext ctx, float dt)
        {
            TickInterception(ctx, dt);

            var toRemove = new List<PendingAttack>();
            foreach (PendingAttack attack in ctx.PendingAttacks)
            {
                if (attack.Applied) { toRemove.Add(attack); continue; }
                bool ready = attack.Tick(dt);
                if (attack.InterceptedDown) { toRemove.Add(attack); continue; } // consumed by interception this same pass
                if (!ready) continue;
                Resolve(ctx, attack);
                toRemove.Add(attack);
            }
            foreach (PendingAttack a in toRemove) ctx.PendingAttacks.Remove(a);
        }

        /// <summary>VS-016A.1 §2D: mirrors ProjectileObject.TickInterception -- only hostile+interceptable pending attacks, only active planetary Lasers in range, shared T1 baseline interception DPS, canonical structure-ID order, never a second result once shot down.</summary>
        private static void TickInterception(RemoteRaidContext ctx, float dt)
        {
            List<StructureDto> lasers = ctx.ActiveLasersOrderedById();
            if (lasers.Count == 0) return;

            foreach (PendingAttack attack in ctx.PendingAttacks)
            {
                if (attack.Applied || !attack.Interceptable || attack.Faction != ProjectileFaction.Hostile) continue;
                foreach (StructureDto laser in lasers)
                {
                    if (attack.Applied || attack.InterceptedDown) break;
                    attack.ApplyInterceptionHit(PlanetaryDefenseCatalog.LaserT1.Dps * dt);
                }
            }
        }

        private static void Resolve(RemoteRaidContext ctx, PendingAttack attack)
        {
            if (attack.Applied) return; // idempotent -- a re-tick of an already-resolved attack (e.g. a stale reference) is always a safe no-op.

            if (attack.Faction == ProjectileFaction.Hostile)
            {
                ResolveAgainstStructureOrEscort(ctx, attack);
            }
            else
            {
                ResolveAgainstEnemy(ctx, attack);
            }
            attack.MarkApplied();
        }

        /// <summary>Enemy-fired (Siege physical shot, or a Disruptor's telegraphed EM hit) resolving against whatever structure/Escort it was aimed at -- Neutral profile, exactly like ProjectileObject.CheckHostileHit's ship-hit convention.</summary>
        private static void ResolveAgainstStructureOrEscort(RemoteRaidContext ctx, PendingAttack attack)
        {
            StructureDto structureTarget = ctx.FindStructureByTargetId(attack.TargetId);
            if (structureTarget != null)
            {
                if (structureTarget.state != (int)DrillLifecycle.Active && structureTarget.state != (int)DrillLifecycle.Disabled) return; // target no longer real (e.g. dismantled) -- miss, never fabricate a hit
                Durability d = RemoteStructureAccess.ToDurability(structureTarget);
                DamageResolver.Apply(d, attack.DamageAmount, DamageProfile.Neutral);
                RemoteStructureAccess.WriteBack(structureTarget, d);
                // VS-016A.1 §2C/§2A: an EM hit disrupts the defense weapon itself -- reuses the same
                // "cannot fire" gate an overheated Laser already uses (TickPlanetaryDefenseFire),
                // rather than a second dedicated field for what is functionally the same effect.
                if (attack.IsEm) structureTarget.defenseOverheatRemaining = Mathf.Max(structureTarget.defenseOverheatRemaining, attack.EmDisruptSeconds);
                return;
            }

            VehicleDto escortTarget = ctx.FindVehicle(attack.TargetId);
            if (escortTarget?.escortState == null || !escortTarget.escortState.active) return; // target gone/no longer a real Escort -- miss
            Hangar.VehicleDef def = Hangar.VehicleCatalog.Get((Hangar.VehicleClass)escortTarget.vehicleClass, escortTarget.mk);
            if (def == null) return;
            Durability ed = RemoteVehicleAccess.ToDurability(escortTarget, def);
            DamageResolver.Apply(ed, attack.DamageAmount, DamageProfile.Neutral);
            RemoteVehicleAccess.WriteBack(escortTarget, ed);
            // VS-016A.1 §2D: respects the same disrupt-immunity gate TickEscortFire grants once a
            // prior disrupt naturally expires -- an immune Escort takes damage but is never re-disrupted.
            if (attack.IsEm && escortTarget.escortState.disruptImmunityRemaining <= 0f)
            {
                escortTarget.escortState.disruptRemaining = Mathf.Max(escortTarget.escortState.disruptRemaining, attack.EmDisruptSeconds);
            }
        }

        /// <summary>Defense/Escort-fired (Plasma direct+splash, or a fixed-Laser hit already resolved same-tick and never pending) resolving against the enemy it was aimed at -- profile follows Kind, matching ProjectileObject.ApplyHit's player-vs-enemy convention.</summary>
        private static void ResolveAgainstEnemy(RemoteRaidContext ctx, PendingAttack attack)
        {
            RemoteEnemyRuntime target = ctx.FindEnemy(attack.TargetId);
            if (target == null || target.Destroyed) return; // already dead/gone -- miss, never fabricate a hit on a corpse

            DamageProfile profile = attack.Kind == ProjectileKind.Plasma || attack.Kind == ProjectileKind.EnemyPlasma ? DamageProfile.Plasma
                : attack.IsEm ? DamageProfile.Em : DamageProfile.Neutral;
            target.ApplyDamage(attack.DamageAmount, profile);

            if (attack.SplashRadius > 0.001f)
            {
                foreach (RemoteEnemyRuntime other in ctx.Enemies)
                {
                    if (other == target || other.Destroyed) continue;
                    float distance = Vector3.Distance(target.Position, other.Position);
                    float splash = WeaponCatalog.PlasmaSplashDamageForDirect(attack.DamageAmount, distance);
                    if (splash > 0f) other.ApplyDamage(splash, profile);
                }
            }
        }
    }
}
