using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Combat
{
    public enum DefenseMode { RolePriority, Balanced, FocusTarget }

    /// <summary>
    /// Why an Alpha7/Alpha8 defense-coordination order did nothing (P1-COMBAT-008).
    /// Every one of these used to be a bare <c>return</c>: the player pressed the key,
    /// the game changed nothing, and said nothing.
    /// </summary>
    public enum DefenseCoordinationRejectReason
    {
        None,
        /// <summary>No combat encounter director is running — there is nothing to coordinate.</summary>
        NoEncounterDirector,
        /// <summary>No planet with an active Command Center is inside <see cref="PlanetaryDefenseCoordinator.CoordinationRange"/>.</summary>
        NoCommandPlanetInRange,
        /// <summary>Focus was ordered with no live enemy selected.</summary>
        NoLiveTarget
    }

    /// <summary>
    /// VS-014 §4: lives under (owned/ticked by) <see cref="CombatEncounterDirector"/>
    /// — never a second global director. Per-planet (worldId-keyed) defense
    /// mode + a single shared target reservation (VS-014 §4: "aynı planet
    /// coordinator üzerinden hedef paylaşır" is implemented as ONE shared
    /// pick per planet every active weapon on it fires at, not a separate
    /// reservation per weapon — the simplest faithful reading of "weapons
    /// share a target"). While the planet's Command Center is Active,
    /// selection follows Role Priority/Balanced/Focus and a 1.5s reservation
    /// so a fresh decision doesn't thrash mid-engagement; while Command is
    /// Disabled, each weapon instead falls back to its own local nearest-
    /// in-range pick at ×0.60 acquisition/turn efficiency (VS-014 §4)-- damage
    /// output itself is never reduced, only how quickly a NEW target is
    /// acquired/tracked.
    /// </summary>
    public class PlanetaryDefenseCoordinator
    {
        public const float TargetReservationSeconds = 1.5f;
        public const float CommandDisabledAcquisitionMultiplier = 0.60f;

        /// <summary>
        /// The furthest a coordination order may reach (P1-COMBAT-008). "Nearest active
        /// Command planet" had no upper bound at all, so a press anywhere in the galaxy
        /// retargeted whichever Command Center happened to be least far away — a planet
        /// the player could not see, in a fight they were not in. The bound is the
        /// planet's own defensive envelope, shared with the Sensor Array's
        /// foreign-presence gate so both mean one distance.
        /// </summary>
        public static readonly float CoordinationRange = PlanetaryDefenseCatalog.PlanetDefenseEnvelopeRange;

        private readonly Dictionary<string, DefenseMode> modeByPlanet = new Dictionary<string, DefenseMode>();
        private readonly Dictionary<string, string> focusByPlanet = new Dictionary<string, string>();
        private readonly Dictionary<string, string> reservedTargetByPlanet = new Dictionary<string, string>();
        private readonly Dictionary<string, float> reservationRemainingByPlanet = new Dictionary<string, float>();

        public DefenseMode GetMode(string worldId) => modeByPlanet.TryGetValue(worldId, out DefenseMode m) ? m : DefenseMode.RolePriority;

        /// <summary>VS-014 §4/Alpha7: cycles Role Priority -> Balanced -> Focus Target -> Role Priority for the given planet.</summary>
        public void CycleMode(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return;
            DefenseMode current = GetMode(worldId);
            modeByPlanet[worldId] = current switch
            {
                DefenseMode.RolePriority => DefenseMode.Balanced,
                DefenseMode.Balanced => DefenseMode.FocusTarget,
                _ => DefenseMode.RolePriority
            };
        }

        public void SetMode(string worldId, DefenseMode mode) { if (!string.IsNullOrEmpty(worldId)) modeByPlanet[worldId] = mode; }

        /// <summary>VS-014 §4/Alpha8: sets the planet's manually-focused enemy id. Ignored (no state change) if enemyId is null/unknown — the caller reports the explicit reject reason.</summary>
        public void SetFocus(string worldId, string enemyId)
        {
            if (string.IsNullOrEmpty(worldId) || string.IsNullOrEmpty(enemyId)) return;
            focusByPlanet[worldId] = enemyId;
        }

        public string GetFocus(string worldId) => focusByPlanet.TryGetValue(worldId, out string id) ? id : null;

        /// <summary>VS-014 §17: every planet this coordinator currently holds a non-default mode or a focus for — the save capture point. A planet on default Role Priority with no focus never appears (nothing to restore).</summary>
        public IEnumerable<(string worldId, DefenseMode mode, string focusEnemyId)> Snapshot()
        {
            var worldIds = new HashSet<string>(modeByPlanet.Keys);
            worldIds.UnionWith(focusByPlanet.Keys);
            foreach (string worldId in worldIds) yield return (worldId, GetMode(worldId), GetFocus(worldId));
        }

        /// <summary>VS-014 §17: restores one planet's saved mode/focus exactly — never re-derives them.</summary>
        public void Restore(string worldId, DefenseMode mode, string focusEnemyId)
        {
            if (string.IsNullOrEmpty(worldId)) return;
            modeByPlanet[worldId] = mode;
            if (!string.IsNullOrEmpty(focusEnemyId)) focusByPlanet[worldId] = focusEnemyId;
        }

        /// <summary>Laser priority order (VS-014 §4): interceptable hostile projectiles are handled separately (ProjectileObject.TickInterception); among enemies, light/Looter/Fighter before anything else.</summary>
        private static int LaserPriorityRank(EnemyAgent a) => a.Def.Role switch
        {
            EnemyRole.Looter => 0,
            EnemyRole.Fighter => 1,
            _ => 2
        };

        /// <summary>Plasma priority order (VS-014 §4): Siege, then Heavy, then anything else.</summary>
        private static int PlasmaPriorityRank(EnemyAgent a) => a.Def.Role switch
        {
            EnemyRole.Siege => 0,
            EnemyRole.Heavy => 1,
            _ => 2
        };

        /// <summary>
        /// Ticks the shared per-planet reservation clock and returns the
        /// planet's current shared target (or null if none valid) — call
        /// once per planet per frame, not once per weapon, so every weapon
        /// on the planet reads the SAME already-resolved pick this tick.
        /// </summary>
        public EnemyAgent ResolveSharedTarget(string worldId, bool commandActive, SurfaceStructureType weaponTypeForPriority,
            IReadOnlyList<EnemyAgent> candidatesInRange, Vector3 planetPosition, float dt)
        {
            if (candidatesInRange == null || candidatesInRange.Count == 0) { ClearReservation(worldId); return null; }

            if (!commandActive)
            {
                // Command disabled -- no shared coordination; nearest-in-range local pick (acquisition penalty is applied by the caller's own cooldown/turn pacing, never here).
                return NearestOf(candidatesInRange, planetPosition);
            }

            DefenseMode mode = GetMode(worldId);
            if (mode == DefenseMode.FocusTarget)
            {
                string focusId = GetFocus(worldId);
                EnemyAgent focused = !string.IsNullOrEmpty(focusId) ? Find(candidatesInRange, focusId) : null;
                if (focused != null) return focused;
                // No valid focus -- fall back to Role Priority behavior below, safely, without changing the stored mode.
            }

            if (reservedTargetByPlanet.TryGetValue(worldId, out string reservedId))
            {
                EnemyAgent reserved = Find(candidatesInRange, reservedId);
                if (reserved != null)
                {
                    float remaining = reservationRemainingByPlanet.TryGetValue(worldId, out float r) ? r : 0f;
                    remaining -= dt;
                    if (remaining > 0f) { reservationRemainingByPlanet[worldId] = remaining; return reserved; }
                }
            }

            EnemyAgent picked = mode == DefenseMode.Balanced
                ? NearestOf(candidatesInRange, planetPosition)
                : PriorityOf(candidatesInRange, weaponTypeForPriority, planetPosition);

            if (picked != null)
            {
                reservedTargetByPlanet[worldId] = picked.Id;
                reservationRemainingByPlanet[worldId] = TargetReservationSeconds;
            }
            return picked;
        }

        private void ClearReservation(string worldId)
        {
            reservedTargetByPlanet.Remove(worldId);
            reservationRemainingByPlanet.Remove(worldId);
        }

        private static EnemyAgent Find(IReadOnlyList<EnemyAgent> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].Id == id && !list[i].Destroyed) return list[i];
            return null;
        }

        private static EnemyAgent NearestOf(IReadOnlyList<EnemyAgent> list, Vector3 from)
        {
            EnemyAgent best = null; float bestSqr = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].Destroyed) continue;
                float sqr = (list[i].transform.position - from).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = list[i]; }
            }
            return best;
        }

        private static EnemyAgent PriorityOf(IReadOnlyList<EnemyAgent> list, SurfaceStructureType weaponType, Vector3 from)
        {
            EnemyAgent best = null; int bestRank = int.MaxValue; float bestSqr = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                EnemyAgent a = list[i];
                if (a == null || a.Destroyed) continue;
                int rank = weaponType == SurfaceStructureType.Plasma ? PlasmaPriorityRank(a) : LaserPriorityRank(a);
                float sqr = (a.transform.position - from).sqrMagnitude;
                if (rank < bestRank || (rank == bestRank && sqr < bestSqr)) { bestRank = rank; bestSqr = sqr; best = a; }
            }
            return best;
        }
    }
}
