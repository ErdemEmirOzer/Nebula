using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §10: pure warning-duration math. A first-appearance-of-a-new-
    /// enemy-class encounter always gets the long 150s tutorial warning
    /// (teaching moments never get shortened); otherwise a successful Probe
    /// scan's still-pending recon effect (consumed exactly once, by the
    /// caller, on the very next raid — <see cref="FirstProbeState.ConsumePendingReconEffect"/>)
    /// shortens the base 90s down to 60s. Difficulty's warning offset and a
    /// real, currently-launched Scout Mk I bonus both then apply on top,
    /// clamped to the shared 150s cap either way.
    /// </summary>
    public static class WarningResolver
    {
        public const float BaseWarningSeconds = 90f;
        public const float ProbePendingEffectWarningSeconds = 60f;
        public const float NewClassFirstAppearanceWarningSeconds = 150f;
        public const float WarningCapSeconds = 150f;

        /// <summary>Keyed by VehicleCatalog Mk tier (Scout is Mk I only this slice) — kept as a lookup, not a single constant, so a future Mk II+ Scout can add a row without touching call sites.</summary>
        public static float ScoutBonusSecondsForMk(int scoutMk) => scoutMk switch
        {
            1 => 15f,
            _ => 0f
        };

        /// <summary>VS-014 §10: only a REAL, currently-launched (not docked, not in a different system) Scout counts — never a fabricated "Patrol" state.</summary>
        public static bool HasRealActiveScoutInSystem(IEnumerable<VehicleAgent> vehicles, string targetSystemId, out int scoutMk)
        {
            scoutMk = 0;
            if (vehicles == null || string.IsNullOrEmpty(targetSystemId)) return false;
            foreach (VehicleAgent v in vehicles)
            {
                if (v == null || v.Def == null || v.Def.Class != VehicleClass.Scout) continue;
                if (!v.gameObject.activeSelf) continue; // docked in Hangar -- not physically launched
                if (v.HomeSystemId != targetSystemId) continue;
                scoutMk = v.Def.Mk;
                return true;
            }
            return false;
        }

        public static float Resolve(bool isNewClassFirstAppearance, bool probePendingEffectActive,
            float difficultyWarningOffsetSeconds, bool hasRealActiveScoutInSystem, int scoutMk, float sensorArrayBonusSeconds = 0f)
        {
            float baseSeconds = isNewClassFirstAppearance ? NewClassFirstAppearanceWarningSeconds
                : (probePendingEffectActive ? ProbePendingEffectWarningSeconds : BaseWarningSeconds);
            float withDifficulty = baseSeconds + difficultyWarningOffsetSeconds;
            float withScout = withDifficulty + (hasRealActiveScoutInSystem ? ScoutBonusSecondsForMk(scoutMk) : 0f);
            float withSensorArray = withScout + Mathf.Max(0f, sensorArrayBonusSeconds);
            return Mathf.Clamp(withSensorArray, 0f, WarningCapSeconds);
        }

        /// <summary>VS-015 §7: the strongest active Sensor Array on the planet contributes its warning bonus (tiers don't stack with each other, only with Scout) — 0 if none is Active.</summary>
        public static float SensorArrayBonusSeconds(bool hasActiveSensorArray, int sensorArrayTier) =>
            hasActiveSensorArray ? PlanetaryDefenseCatalog.Get(SurfaceStructureType.SensorArray, sensorArrayTier)?.WarningBonusSeconds ?? 0f : 0f;

        /// <summary>VS-014 §10's Probe-pending-effect "reveals the weakest defense info" — the live active defense structure with the lowest current hull fraction, or null if there is none to reveal.</summary>
        public static SurfaceStructureType? WeakestDefense(IReadOnlyList<(SurfaceStructureType type, float hullFraction)> activeDefenses)
        {
            if (activeDefenses == null || activeDefenses.Count == 0) return null;
            SurfaceStructureType? weakest = null;
            float lowest = float.MaxValue;
            foreach (var (type, hullFraction) in activeDefenses)
            {
                if (hullFraction >= lowest) continue;
                lowest = hullFraction;
                weakest = type;
            }
            return weakest;
        }
    }
}
