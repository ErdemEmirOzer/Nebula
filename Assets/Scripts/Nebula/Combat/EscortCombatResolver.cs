using Nebula.Hangar;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-015 §4: pure decision logic for Escort behavior — target priority,
    /// stance-based retreat threshold, and the chase-then-return distance
    /// rule. Callers gather real candidate ids/positions (EnemyAgent,
    /// ProjectileObject, ShipCombat.Target) and hand them to these static
    /// methods; nothing here touches a MonoBehaviour, mirroring
    /// RepairSafeZoneResolver's "MonoBehaviour gathers, pure class decides" split.
    /// </summary>
    public static class EscortCombatResolver
    {
        /// <summary>Priority: attacker of the protected target &gt; a threatening interceptable projectile &gt; a raider &gt; a fighter &gt; the player's own focus target. Any absent candidate is passed as null/empty.</summary>
        public static string ResolveTarget(string attackerOfProtectedId, string interceptableProjectileId,
            string raiderId, string fighterId, string playerFocusTargetId)
        {
            if (!string.IsNullOrEmpty(attackerOfProtectedId)) return attackerOfProtectedId;
            if (!string.IsNullOrEmpty(interceptableProjectileId)) return interceptableProjectileId;
            if (!string.IsNullOrEmpty(raiderId)) return raiderId;
            if (!string.IsNullOrEmpty(fighterId)) return fighterId;
            if (!string.IsNullOrEmpty(playerFocusTargetId)) return playerFocusTargetId;
            return null;
        }

        /// <summary>Safe retreats under 60% total durability; Balanced under 35% OR when the known enemy power is judged insufficient; Persistent only below 15% (forced).</summary>
        public static bool ShouldRetreat(float totalDurabilityFraction, EscortStance stance, bool knownEnemyPowerInsufficient)
        {
            return stance switch
            {
                EscortStance.Safe => totalDurabilityFraction < EscortState.SafeRetreatFraction,
                EscortStance.Persistent => totalDurabilityFraction < EscortState.PersistentRetreatFraction,
                _ => totalDurabilityFraction < EscortState.BalancedRetreatFraction || knownEnemyPowerInsufficient
            };
        }

        /// <summary>An Escort chases a target it left its protect radius for by at most 400 extra units before turning back.</summary>
        public static bool ShouldReturnToProtect(float distanceFromProtectRadiusEdge) => distanceFromProtectRadiusEdge > EscortState.ChaseExtraDistance;
    }
}
