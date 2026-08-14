using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>VS-014 §6: what category of player asset a live combat target is — used only for AI target-priority selection, never a save/gameplay authority of its own.</summary>
    public enum TargetCategory { Ship, Structure, Vehicle, LogisticsCenter }

    /// <summary>
    /// VS-014 §6: the common boundary hostile AI (and the planetary defense
    /// coordinator, for its own OWN-side lookups) reads every physically
    /// targetable PLAYER asset through — a stable id, its real absolute
    /// position, whether it's currently a legitimate target, and a damage
    /// entry point that always routes to the real, single Durability/K-Z-G
    /// authority each MonoBehaviour already owns (ShipCombatState.Durability,
    /// StructureRuntime.Durability, LogisticsCenterRuntime.Durability, or a
    /// VehicleAgent's own instance) — never a second copy of that state.
    /// Only assets enemies can genuinely damage implement this; a Drill is a
    /// Looter loot SOURCE (see <see cref="LooterCargoService"/>), never a
    /// weapon target, so it deliberately does not implement this interface.
    /// </summary>
    public interface ICombatTarget
    {
        string TargetId { get; }
        TargetCategory Category { get; }
        /// <summary>Absolute (FloatingOrigin-corrected) world position.</summary>
        Vector3 AbsolutePosition { get; }
        /// <summary>False for a destroyed/inactive/dematerialized/passive (Lost/Abandoned) asset — hostile AI must never fire at or select a non-targetable target.</summary>
        bool IsTargetable { get; }
        void ApplyDamage(float rawAmount, DamageProfile profile);
    }
}
