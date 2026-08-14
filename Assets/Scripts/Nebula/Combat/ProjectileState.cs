using UnityEngine;

namespace Nebula.Combat
{
    public enum ProjectileKind { Plasma, Em, EnemyPlasma, EnemySiege }

    /// <summary>VS-014 §15: which side fired this projectile — determines what it can hit (a Player projectile only ever hits EnemyAgent, exactly as before; a Hostile projectile only ever hits the ship/planetary structures) and whether a planetary Laser may intercept it.</summary>
    public enum ProjectileFaction { Player, Hostile }

    /// <summary>
    /// Pure kinematic state for a physical, unguided (Plasma) or
    /// limited-guidance (EM) projectile (VS-013 §3), extended for hostile
    /// enemy fire (VS-014 §15: EM Disruptor telegraph handled separately by
    /// EnemyAgent itself, not a projectile — Plasma/Siege enemy fire IS a
    /// physical projectile). Never a hitscan for these — position integrates
    /// every tick and is checked against real colliders by the MonoBehaviour
    /// adapter (<see cref="ProjectileObject"/>), exactly like
    /// <see cref="Ship.ShipController.MoveWithCollision"/>'s own
    /// segment-based approach, never an instant/teleported hit.
    /// </summary>
    public class ProjectileState
    {
        public string ProjectileId { get; }
        public ProjectileKind Kind { get; }
        public int Tier { get; }
        /// <summary>Ship/enemy id that fired this — used for collision-pair/friendly-fire rules.</summary>
        public string OwnerId { get; }
        public ProjectileFaction Faction { get; }
        /// <summary>VS-014 §15: a hostile Plasma/Siege shot a planetary Laser may shoot down before it reaches its target. Never true for player-fired projectiles (friendly fire is never a concept here).</summary>
        public bool Interceptable { get; }
        /// <summary>Remaining hit points before an intercepting Laser destroys this projectile — a single catalog constant (see ProjectileCatalog), never a per-file-invented number.</summary>
        public float InterceptionDurabilityRemaining { get; private set; }
        /// <summary>VS-014 §15: a hostile projectile carries its OWN damage amount (the firing enemy's real DPS-derived hit) — never re-derived from the player's own WeaponCatalog on hit.</summary>
        public float DamageAmount { get; }

        public Vector3 Position { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float RemainingLifetimeSeconds { get; private set; }

        /// <summary>EM only — null means no lock (Plasma) or lock lost (EM continues on its last heading, VS-013 §3).</summary>
        public string LockedTargetId { get; private set; }

        /// <summary>
        /// VS-017A §1: who this shot was aimed AT when fired — set once at spawn and never
        /// changed afterward (unlike <see cref="LockedTargetId"/>, which EM can lose mid-flight
        /// via <see cref="LoseLock"/>). Plasma/Siege never lock a target in the live game (proximity
        /// hit detection instead), so without this a demoted-mid-flight shot had nothing to carry
        /// forward remotely. Defaults to lockedTargetId when not given explicitly, so EM's own real
        /// target continues to flow through for free with no call-site change required.
        /// </summary>
        public string IntendedTargetId { get; }

        public bool Expired => RemainingLifetimeSeconds <= 0f;
        public bool InterceptedDown => Interceptable && InterceptionDurabilityRemaining <= 0f;

        public ProjectileState(string projectileId, ProjectileKind kind, int tier, string ownerId,
            Vector3 position, Vector3 velocity, float lifetimeSeconds, string lockedTargetId,
            ProjectileFaction faction = ProjectileFaction.Player, bool interceptable = false, float interceptionDurability = 0f,
            float damageAmount = 0f, string intendedTargetId = null)
        {
            ProjectileId = projectileId; Kind = kind; Tier = tier; OwnerId = ownerId;
            Position = position; Velocity = velocity; RemainingLifetimeSeconds = lifetimeSeconds;
            LockedTargetId = lockedTargetId;
            Faction = faction; Interceptable = interceptable; InterceptionDurabilityRemaining = interceptionDurability;
            DamageAmount = damageAmount;
            IntendedTargetId = intendedTargetId ?? lockedTargetId;
        }

        /// <summary>Never applies a second interception result to an already-shot-down projectile (VS-014 §15: "interception aynı projectile'a iki kez sonuç uygulamaz").</summary>
        public void ApplyInterceptionHit(float amount)
        {
            if (!Interceptable || InterceptedDown || amount <= 0f) return;
            InterceptionDurabilityRemaining = Mathf.Max(0f, InterceptionDurabilityRemaining - amount);
        }

        /// <summary>Once lock is lost the projectile keeps flying dead-straight on whatever heading it already had — never re-homes, never disappears.</summary>
        public void LoseLock() => LockedTargetId = null;

        /// <summary>Steers velocity toward targetPosition at up to maxTurnRateDegreesPerSecond, preserving speed. No-op once lock is lost.</summary>
        public void GuideToward(Vector3 targetPosition, float maxTurnRateDegreesPerSecond, float dt)
        {
            if (LockedTargetId == null) return;
            Vector3 toTarget = targetPosition - Position;
            if (toTarget.sqrMagnitude < 0.0001f) return;
            float speed = Velocity.magnitude;
            Vector3 desired = toTarget.normalized * speed;
            float maxRadians = maxTurnRateDegreesPerSecond * Mathf.Deg2Rad * dt;
            Velocity = Vector3.RotateTowards(Velocity, desired, maxRadians, 0f);
        }

        public void Tick(float dt)
        {
            Position += Velocity * dt;
            RemainingLifetimeSeconds = Mathf.Max(0f, RemainingLifetimeSeconds - dt);
        }

        public void RestorePosition(Vector3 position, Vector3 velocity, float remainingLifetimeSeconds, string lockedTargetId)
        {
            Position = position; Velocity = velocity; RemainingLifetimeSeconds = remainingLifetimeSeconds; LockedTargetId = lockedTargetId;
        }

        /// <summary>VS-014 §17: restores the interception durability remaining exactly as saved — never re-rolled/reset on load.</summary>
        public void RestoreInterceptionDurability(float remaining) => InterceptionDurabilityRemaining = Mathf.Max(0f, remaining);
    }
}
