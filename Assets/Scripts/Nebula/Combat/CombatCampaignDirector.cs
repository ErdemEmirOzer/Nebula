using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.Combat
{
    /// <summary>
    /// Campaign-session-lifetime director (VS-013 §7/§8) — the one live
    /// MonoBehaviour that ticks <see cref="CombatCampaignState"/>'s
    /// campaign-wide timers (the First Probe warning countdown, the
    /// soft-lock active-minutes counter) regardless of which system is
    /// currently Full. Reaches the current Full system's
    /// <see cref="CombatEncounterDirector"/> only through
    /// <see cref="StarSystemRegistry.Streaming"/> — the same established
    /// static accessor every other combat-adjacent class already uses,
    /// never a new locator/Find.
    /// </summary>
    public class CombatCampaignDirector : MonoBehaviour
    {
        private ShipController ship;

        public static CombatCampaignDirector Create(ShipController shipController)
        {
            var go = new GameObject("Combat Campaign Director");
            var director = go.AddComponent<CombatCampaignDirector>();
            director.ship = shipController;
            return director;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return; // paused -- no offline/paused progress, matching every other campaign timer in this project

            CombatCampaignState.Recovery.TickActiveMinutes(dt);

            // VS-013 §7: schedules exactly once, the first time ANY active planetary
            // defense (Command Center or Laser) exists -- ScheduleWarningIfNeeded is
            // itself idempotent, so a plain per-frame check is safe and needs no
            // separate "structure just completed" event hook.
            if (HasAnyActiveDefense()) CombatCampaignState.FirstProbe.ScheduleWarningIfNeeded();

            if (CombatCampaignState.FirstProbe.TickWarning(dt))
            {
                SpawnFirstProbe();
            }

            RaidScheduler.Tick(StarSystemRegistry.Streaming?.CombatEncounter, dt);
        }

        /// <summary>
        /// VS-013.1 §4 fix: must require a real active DEFENSE WEAPON (Laser
        /// this slice), never just any active structure — a lone Command
        /// Center (no weapon) must never schedule the First Probe warning.
        /// </summary>
        private static bool HasAnyActiveDefense()
        {
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && SurfaceStructureCatalog.IsActiveDefenseWeapon(s.Type) && s.Runtime.State == DrillLifecycle.Active) return true;
            return false;
        }

        private void SpawnFirstProbe()
        {
            CombatEncounterDirector director = StarSystemRegistry.Streaming?.CombatEncounter;
            if (director == null || ship == null) return;

            Vector3 spawnPosition = ship.transform.position + ship.transform.forward * 2000f + ship.transform.right * 500f;
            director.SpawnEnemy(EnemyIds.ScoutProbeMk1, spawnPosition);
        }
    }
}
