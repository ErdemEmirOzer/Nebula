using System;
using UnityEngine;
using Nebula.Combat;

namespace Nebula.Visuals
{
    /// <summary>Who fired — enough for the lighting to pick a priority, and nothing more.</summary>
    public enum DefenseFireOwner
    {
        /// <summary>A planetary Laser or Plasma structure the player built.</summary>
        PlanetaryStructure,

        /// <summary>A Pirate Base turret.</summary>
        PirateBase
    }

    /// <summary>One accepted shot, described for presentation only.</summary>
    public readonly struct DefenseFireEvent
    {
        public readonly WeaponFamily Family;
        public readonly Vector3 MuzzlePosition;
        public readonly Vector3 Direction;
        public readonly DefenseFireOwner Owner;

        public DefenseFireEvent(WeaponFamily family, Vector3 muzzlePosition, Vector3 direction, DefenseFireOwner owner)
        {
            Family = family;
            MuzzlePosition = muzzlePosition;
            Direction = direction;
            Owner = owner;
        }
    }

    /// <summary>
    /// The one visual-only announcement that a defensive weapon actually fired.
    /// <para>
    /// Raised from the single production point in each system where a shot is
    /// <em>accepted</em> — after every cooldown, overheat, range and
    /// line-of-sight check has passed and damage or a projectile has already
    /// been committed. A refused shot raises nothing, so a muzzle light can
    /// never appear for a shot that did not happen.
    /// </para>
    /// <para>
    /// Carries no authority: it changes no damage, cooldown, heat, projectile
    /// or targeting value, is never serialized, and is never replayed. Both
    /// raise sites live in per-frame Full-system code, so a distant system
    /// resolving combat at 1 Hz produces no events at all, and a system the
    /// player returns to cannot re-announce shots fired while it was away.
    /// </para>
    /// </summary>
    public static class DefenseFireSignal
    {
        /// <summary>Presentation subscribers only. Nothing that affects simulation may listen here.</summary>
        public static event Action<DefenseFireEvent> Fired;

        /// <summary>Announcements raised this session. Test-facing.</summary>
        public static int RaisedCount { get; private set; }

        /// <summary>
        /// Called only where a shot has already been committed.
        /// <paramref name="muzzle"/> is the real anchor when the visual has
        /// one; callers pass the owner's own position when it does not, so a
        /// missing anchor costs accuracy rather than throwing.
        /// </summary>
        public static void Raise(WeaponFamily family, Vector3 muzzle, Vector3 direction, DefenseFireOwner owner)
        {
            RaisedCount++;
            Fired?.Invoke(new DefenseFireEvent(family, muzzle, direction, owner));
        }

        /// <summary>
        /// Drops every subscriber. Called on session teardown so a restarted
        /// campaign never leaves the previous session's bridge listening — a
        /// static event is the one place that leak could survive a scene load.
        /// </summary>
        public static void ClearSubscribers()
        {
            Fired = null;
            RaisedCount = 0;
        }
    }
}
