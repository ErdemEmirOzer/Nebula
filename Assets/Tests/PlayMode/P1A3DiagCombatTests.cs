using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 1 / AJAN 3 — diagnostic fixture (P1A3Diag).
    /// <para>
    /// Behaviour evidence for the flight/combat audit's findings. Each test
    /// asserts the behaviour the design calls for, so a FAILURE here is the
    /// finding's proof. Expected to fail on HEAD=4d16782.
    /// </para>
    /// <para>
    /// The scene-boot/save-slot handling is copied from
    /// <c>Vs013CombatTests</c> deliberately: this fixture must not leave the
    /// player's real gameplay slot altered.
    /// </para>
    /// </summary>
    public class P1A3DiagCombatTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;

        private readonly Dictionary<string, string> slot1Backup = new Dictionary<string, string>();

        private static IEnumerable<string> Slot1Files()
        {
            string main = SaveService.SlotFile(GameplaySlot);
            string dir = Path.GetDirectoryName(main);
            yield return main;
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak1.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak2.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak3.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.tmp.json");
        }

        [SetUp]
        public void BackupRealGameplaySlot()
        {
            slot1Backup.Clear();
            foreach (string f in Slot1Files())
                slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
            InputGate.Suppressed = false;
            foreach (var kv in slot1Backup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;
            Object.FindObjectOfType<SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        // ---- P1-COMBAT: the laser spends energy on a shot it then refuses ----

        /// <summary>
        /// <c>ShipCombat.FireLaser</c> calls <c>State.TryConsumeEnergy</c>
        /// BEFORE the range, gimbal and line-of-sight checks, and every one of
        /// those checks is a bare <c>return</c> — the energy is never refunded.
        /// <c>FirePlasma</c> does it the other way round (range first, then
        /// energy), so the two weapons disagree about what a refused shot
        /// costs. Held fire pointed away from the target therefore drains the
        /// pool to the 0→20% weapons lockout while dealing no damage.
        /// </summary>
        [UnityTest]
        public IEnumerator Laser_RefusedForRange_DoesNotSpendEnergy()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController ship = adapter.Context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            CombatEncounterDirector director = StarSystemRegistry.Streaming?.CombatEncounter;

            Assert.That(combat, Is.Not.Null, "baseline: the ship has a ShipCombat");
            Assert.That(combat.State, Is.Not.Null, "baseline: combat state was initialized by the campaign start");
            Assert.That(director, Is.Not.Null, "baseline: the Full system has a combat encounter director");

            HardpointState laser = combat.Hardpoints[0];
            Assert.That(laser.Weapon, Is.EqualTo(WeaponFamily.Laser), "baseline: hardpoint 0 carries the free T1 laser");

            // Far beyond WeaponCatalog.LaserRange(1), so the shot is refused on range.
            float wayOutOfRange = WeaponCatalog.LaserRange(laser.Tier) * 20f;
            EnemyAgent target = director.SpawnEnemy(EnemyIds.FighterMk1,
                ship.transform.position + ship.transform.forward * wayOutOfRange);
            Assert.That(target, Is.Not.Null, "baseline: a target could be spawned");

            Assert.That(combat.TrySelectNearestTarget(), Is.True, "baseline: the far target can still be locked");

            float hullBefore = target.Combat.Hull;
            float energyBefore = combat.State.Energy;

            MethodInfo fireLaser = typeof(ShipCombat).GetMethod("FireLaser",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(fireLaser, Is.Not.Null, "baseline: ShipCombat.FireLaser exists (private — no player-facing seam)");

            // Sixty frames of held fire, exactly what HandleWeaponFire would do.
            for (int i = 0; i < 60; i++)
            {
                fireLaser.Invoke(combat, new object[] { laser });
                yield return null;
            }

            float hullAfter = target.Combat.Hull;
            float energyAfter = combat.State.Energy;

            Assert.That(hullAfter, Is.EqualTo(hullBefore).Within(0.001f),
                "baseline: an out-of-range laser deals no damage");
            Assert.That(energyAfter, Is.GreaterThanOrEqualTo(energyBefore - 0.001f),
                $"a refused laser shot spent energy: {energyBefore:0.###} -> {energyAfter:0.###}. "
                + "ShipCombat.FireLaser consumes energy before the range/gimbal/line-of-sight checks and never refunds it.");
        }

        // ---- P1-COMBAT: Emergency Shutdown freezes behind an open screen -----

        /// <summary>
        /// <c>ShipCombat.Update</c> returns on <c>InputGate.Suppressed</c>
        /// before it reaches <c>UpdateShutdown</c>, so the 8-second Emergency
        /// Shutdown countdown — the only route back from hull 0 — stops
        /// advancing while any screen is open. Every other campaign timer keeps
        /// running behind a screen since 3fa9dda ("keep the world running while
        /// a screen is open"), and <c>EnemyAgent</c> documents its own
        /// suspension as a deliberate exception for COMBAT, not for recovery.
        /// </summary>
        [UnityTest]
        public IEnumerator EmergencyShutdownCountdown_KeepsRunningWhileAScreenIsOpen()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController ship = adapter.Context.Ship;
            var combat = ship.GetComponent<ShipCombat>();

            // Drive the hull to 0 through the real shared damage entry point.
            Durability durability = combat.State.Durability;
            combat.ApplyDamage(durability.Shield + durability.Armor + durability.Hull + 50f);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(combat.State.EmergencyShutdown, Is.True, "baseline: hull 0 puts the ship into Emergency Shutdown");
            Assert.That(combat.Shutdown.Active, Is.True, "baseline: the countdown started");

            float remainingBefore = combat.Shutdown.RemainingSeconds;

            InputGate.Suppressed = true;          // exactly what any management screen raises
            for (int i = 0; i < 30; i++) yield return null;
            float remainingWithScreenOpen = combat.Shutdown.RemainingSeconds;
            InputGate.Suppressed = false;

            Assert.That(remainingWithScreenOpen, Is.LessThan(remainingBefore - 0.0001f),
                $"the Emergency Shutdown countdown did not advance while a screen was open "
                + $"({remainingBefore:0.###}s -> {remainingWithScreenOpen:0.###}s over 30 frames). "
                + "ShipCombat.Update returns on InputGate.Suppressed before UpdateShutdown, so the player's only "
                + "route out of hull 0 is frozen for as long as a screen stays up.");
        }

        // ---- P1-WORLD: an Active raid with no live enemies pays out ---------

        /// <summary>
        /// <c>RaidScheduler.TickActive</c> resolves the raid the moment none of
        /// <c>SpawnedEnemyIds</c> resolves to a live agent, and
        /// <c>FinalizeRaid</c> then applies the full Decisive Victory
        /// bookkeeping: salvage wreckage, raid history, and MarkTaught for
        /// every class in the composition. That is also exactly the state a
        /// raid is in if its enemies fail to materialize (a bad def id, or a
        /// restore that did not re-create them), so a raid nobody ever fought
        /// pays out as a clean win.
        /// </summary>
        [UnityTest]
        public IEnumerator ActiveRaidWithNoLiveEnemies_DoesNotPayOutAsAVictory()
        {
            yield return LoadSceneFresh();
            CombatEncounterDirector director = StarSystemRegistry.Streaming?.CombatEncounter;
            Assert.That(director, Is.Not.Null, "baseline: the Full system has a combat encounter director");

            ThreatPressureState pressure = director.Pressure;
            RaidCampaignState campaign = CombatCampaignState.RaidCampaign;

            var composition = new List<string> { EnemyIds.HeavyAssaultMk1, EnemyIds.FighterMk1 };
            pressure.ScheduleRaid(RaidStage.Assault, RaidDoctrine.Suppression,
                "p1a3-diag/no-such-planet", composition, encounterSeed: 4242UL, warningSeconds: 0f);

            // Straight to Active with an empty SpawnedEnemyIds list: nothing was
            // ever physically spawned, and nothing was ever fought.
            pressure.SetPhase(RaidPhase.Active);
            Assert.That(pressure.SpawnedEnemyIds, Is.Empty, "baseline: this raid has no live enemies");
            Assert.That(pressure.HasMeaningfulParticipation, Is.False, "baseline: the player never engaged");

            int wreckageBefore = director.Wreckage.Count;
            int historyBefore = campaign.History.Count;

            // Active -> RetreatCleanup -> Resolved -> FinalizeRaid
            for (int i = 0; i < 4; i++)
            {
                RaidScheduler.Tick(director, 0.02f);
                yield return null;
            }

            Assert.That(director.Wreckage.Count, Is.EqualTo(wreckageBefore),
                "a raid whose enemies never existed produced salvage wreckage (RaidSalvageResolver ran on a DecisiveVictory).");
            Assert.That(campaign.History.Count, Is.EqualTo(historyBefore),
                "a raid whose enemies never existed was written into the campaign raid history.");
            Assert.That(campaign.IsTaught(EnemyIds.HeavyAssaultMk1), Is.False,
                "a Heavy Assault the player never saw was marked as a taught class, which escalates the next raid's composition.");
        }
    }
}
