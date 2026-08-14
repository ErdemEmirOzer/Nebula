using System;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>One Escort shot that was actually taken.</summary>
    public readonly struct EscortFireEvent
    {
        public readonly string VehicleId;
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;

        public EscortFireEvent(string vehicleId, Vector3 origin, Vector3 direction)
        {
            VehicleId = vehicleId;
            Origin = origin;
            Direction = direction;
        }
    }

    /// <summary>
    /// Announces an Escort's fixed-laser shot, and only after the damage has
    /// really been applied.
    /// <para>
    /// Raised from the single line past every gate the resolver imposes, so a
    /// shot refused for range, cooldown, target or line of sight raises nothing
    /// at all. It carries no authority: no damage, no cooldown, no targeting,
    /// and it is never serialized or replayed — exactly the shape
    /// <see cref="DefenseFireSignal"/> already established for turrets.
    /// </para>
    /// </summary>
    public static class EscortFireSignal
    {
        /// <summary>Presentation subscribers only.</summary>
        public static event Action<EscortFireEvent> Fired;

        /// <summary>Shots announced this session. Test-facing.</summary>
        public static int RaisedCount { get; private set; }

        public static void Raise(string vehicleId, Vector3 origin, Vector3 direction)
        {
            RaisedCount++;
            Fired?.Invoke(new EscortFireEvent(vehicleId, origin, direction));
        }

        /// <summary>
        /// Drops every subscriber. Called at session start, so a restarted
        /// campaign never leaves the previous session's presenters listening —
        /// a static event is the one leak that survives a scene load.
        /// </summary>
        public static void ClearSubscribers()
        {
            Fired = null;
            RaisedCount = 0;
        }
    }
}
