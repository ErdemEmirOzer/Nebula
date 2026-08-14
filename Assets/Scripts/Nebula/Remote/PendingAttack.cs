using UnityEngine;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A §5: an in-flight attack carried across a Full-system demotion,
    /// through however many distant remote ticks, and back through promotion —
    /// deliberately a distinct, dedicated pure class rather than reusing
    /// <see cref="Combat.ProjectileState"/> directly, so the well-tested live
    /// projectile/combat path (VS-013/VS-014, 1101 EditMode + 60 PlayMode
    /// baseline) is never touched by this slice. A pending attack never hits
    /// or vanishes the instant a system demotes — its remaining flight time
    /// is what a distant tick actually advances (<see cref="Tick"/>), and it
    /// carries everything needed to re-resolve the exact same hit later
    /// without re-deriving anything from a live weapon/catalog context.
    /// </summary>
    public class PendingAttack
    {
        public string AttackId { get; }
        public string AttackerId { get; }
        public string TargetId { get; }
        public Combat.ProjectileKind Kind { get; }
        public Combat.ProjectileFaction Faction { get; }
        public float DamageAmount { get; }
        public float RemainingFlightSeconds { get; private set; }
        public float SplashRadius { get; }
        public float SplashMaxFractionOfDirect { get; }
        public bool IsEm { get; }
        public float EmDisruptSeconds { get; }
        public float EmImmunityAfterDisruptSeconds { get; }
        public bool Interceptable { get; }
        public float InterceptionDurabilityRemaining { get; private set; }

        /// <summary>Guards against ever applying the same attack's result twice — across a distant tick, a save/load, or a promotion back to Full.</summary>
        public bool Applied { get; private set; }

        public PendingAttack(string attackId, string attackerId, string targetId, Combat.ProjectileKind kind,
            Combat.ProjectileFaction faction, float damageAmount, float remainingFlightSeconds,
            float splashRadius, float splashMaxFractionOfDirect, bool isEm, float emDisruptSeconds,
            float emImmunityAfterDisruptSeconds, bool interceptable, float interceptionDurabilityRemaining, bool applied = false)
        {
            AttackId = attackId; AttackerId = attackerId; TargetId = targetId; Kind = kind; Faction = faction;
            DamageAmount = damageAmount; RemainingFlightSeconds = Mathf.Max(0f, remainingFlightSeconds);
            SplashRadius = splashRadius; SplashMaxFractionOfDirect = splashMaxFractionOfDirect;
            IsEm = isEm; EmDisruptSeconds = emDisruptSeconds; EmImmunityAfterDisruptSeconds = emImmunityAfterDisruptSeconds;
            Interceptable = interceptable; InterceptionDurabilityRemaining = Mathf.Max(0f, interceptionDurabilityRemaining);
            Applied = applied;
        }

        /// <summary>Captures a live projectile's essential attack data at demotion time, resolving splash/EM constants from the same shared WeaponCatalog every live projectile already uses -- never a second/duplicated set of numbers.</summary>
        public static PendingAttack CaptureFromLive(string attackId, string attackerId, string targetId,
            Combat.ProjectileKind kind, Combat.ProjectileFaction faction, float damageAmount, float remainingFlightSeconds,
            bool interceptable, float interceptionDurabilityRemaining)
        {
            bool isPlasma = kind == Combat.ProjectileKind.Plasma || kind == Combat.ProjectileKind.EnemyPlasma;
            bool isEm = kind == Combat.ProjectileKind.Em;
            float splashRadius = isPlasma ? Combat.WeaponCatalog.PlasmaSplashRadius : 0f;
            float splashFraction = isPlasma ? Combat.WeaponCatalog.PlasmaSplashMaxFractionOfDirect : 0f;
            float emDisrupt = isEm ? Combat.WeaponCatalog.EmDisruptSeconds : 0f;
            float emImmunity = isEm ? Combat.WeaponCatalog.EmImmunityAfterDisruptSeconds : 0f;
            return new PendingAttack(attackId, attackerId, targetId, kind, faction, damageAmount, remainingFlightSeconds,
                splashRadius, splashFraction, isEm, emDisrupt, emImmunity, interceptable, interceptionDurabilityRemaining);
        }

        /// <summary>Advances remaining flight time only -- never applies damage itself. Returns true once flight time has fully elapsed (ready to resolve).</summary>
        public bool Tick(float dt)
        {
            if (Applied) return true;
            RemainingFlightSeconds = Mathf.Max(0f, RemainingFlightSeconds - dt);
            return RemainingFlightSeconds <= 0f;
        }

        /// <summary>Interception reduces remaining durability; a fully-intercepted attack is marked Applied (consumed) without ever dealing its damage.</summary>
        public void ApplyInterceptionHit(float amount)
        {
            if (!Interceptable || Applied || amount <= 0f) return;
            InterceptionDurabilityRemaining = Mathf.Max(0f, InterceptionDurabilityRemaining - amount);
            if (InterceptionDurabilityRemaining <= 0f) Applied = true;
        }

        public bool InterceptedDown => Interceptable && InterceptionDurabilityRemaining <= 0f;

        /// <summary>Marks this attack's result as applied -- idempotent, safe to call more than once.</summary>
        public void MarkApplied() => Applied = true;

        public static PendingAttack Restore(string attackId, string attackerId, string targetId, Combat.ProjectileKind kind,
            Combat.ProjectileFaction faction, float damageAmount, float remainingFlightSeconds, float splashRadius,
            float splashMaxFractionOfDirect, bool isEm, float emDisruptSeconds, float emImmunityAfterDisruptSeconds,
            bool interceptable, float interceptionDurabilityRemaining, bool applied) =>
            new PendingAttack(attackId, attackerId, targetId, kind, faction, damageAmount, remainingFlightSeconds,
                splashRadius, splashMaxFractionOfDirect, isEm, emDisruptSeconds, emImmunityAfterDisruptSeconds,
                interceptable, interceptionDurabilityRemaining, applied);
    }
}
