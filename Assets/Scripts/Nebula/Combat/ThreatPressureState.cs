using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Combat
{
    public enum RaidStage { Pirate, Assault, Siege }
    public enum RaidDoctrine { Loot, Suppression, Siege }

    /// <summary>VS-014 §11: the physical raid flow. None = no raid pending/running this system.</summary>
    public enum RaidPhase { None, Scheduled, Warning, Spawning, Active, RetreatCleanup, Resolved }

    /// <summary>
    /// System-delta-lifetime raid/pressure state (VS-014 §8) — owned by the
    /// current Full system's <see cref="CombatEncounterDirector"/> exactly
    /// like <see cref="CombatEncounterState"/> already is, so it exists only
    /// while that system is materialized and is captured into/restored from
    /// its own <see cref="Sectors.SystemDelta"/> the same way (task-scoped
    /// for later save wiring). Pure state only: pressure rate, grace
    /// duration, and every phase TRANSITION are resolved by the scheduler
    /// (VS-014 §9/§11) and passed in already-computed — this class never
    /// re-derives them itself, mirroring the Resolver/pure-state split used
    /// throughout VS-013.
    /// </summary>
    public class ThreatPressureState
    {
        public const float MaxPressure = 100f;
        public const float EconomicValueMultiplierCap = 2.0f;
        public const float PressureSpeedEffectCap = 1.5f;

        public float Pressure { get; private set; }

        // --- Grace (VS-014 §8: first Controlled 15min, victory 15min, defeat 25min) --
        public float GraceRemainingActiveMinutes { get; private set; }
        public bool InGrace => GraceRemainingActiveMinutes > 0f;
        /// <summary>Extends (never shortens) the current grace window — a fresh grace never cuts an already-longer one short.</summary>
        public void StartGrace(float activeMinutes) => GraceRemainingActiveMinutes = Mathf.Max(GraceRemainingActiveMinutes, activeMinutes);

        /// <summary>VS-014 §8: the first-Controlled-planet-outside-the-start-system 15min grace is granted at most once per system (a fresh Full materialization can't tell "first Controlled" from "already was Controlled" any other way) -- never re-granted after load.</summary>
        public bool FirstControlledGraceGranted { get; private set; }
        public void MarkFirstControlledGraceGranted() => FirstControlledGraceGranted = true;
        /// <summary>Only ever called while this system is Full and active (never offline/demoted) — the caller guarantees that simply by this instance only existing then.</summary>
        public void TickGrace(float dt) { if (GraceRemainingActiveMinutes > 0f) GraceRemainingActiveMinutes = Mathf.Max(0f, GraceRemainingActiveMinutes - dt / 60f); }

        // --- Foreign presence (VS-014 §8: first-foreign-presence timer) -------------
        public bool ForeignPresenceActive { get; private set; }
        public float ForeignPresenceActiveMinutesElapsed { get; private set; }
        public void ActivateForeignPresence() => ForeignPresenceActive = true;
        public void TickForeignPresence(float dt) { if (ForeignPresenceActive) ForeignPresenceActiveMinutesElapsed += dt / 60f; }

        /// <summary>
        /// Never advances during grace or while a raid is already scheduled/running
        /// in THIS system (a second one can't stack on top) — the GLOBAL one-major-
        /// raid-anywhere cap is <see cref="RaidCampaignState.HasGlobalMajorRaidActive"/>,
        /// a separate campaign-level check the caller applies before ever calling this.
        /// `speedMultiplier` is the caller's already-computed economic-value speed
        /// bonus, clamped here to the shared ×1.5 cap regardless of what's passed in.
        /// </summary>
        public void TickPressure(float dt, float ratePerActiveMinute, float speedMultiplier)
        {
            if (InGrace || IsRaidInProgress) return;
            float clampedSpeed = Mathf.Min(Mathf.Max(0f, speedMultiplier), PressureSpeedEffectCap);
            Pressure = Mathf.Min(MaxPressure, Pressure + ratePerActiveMinute * clampedSpeed * (dt / 60f));
        }

        public void ResetPressure() => Pressure = 0f;

        /// <summary>VS-014 §14: the Pirate Base's one-time reward reduces this system's current pressure by a flat amount (never below 0) — never a percentage, never a second pressure authority.</summary>
        public void ReducePressure(float amount) => Pressure = Mathf.Max(0f, Pressure - amount);

        // --- Raid identity/composition (VS-014 §9/§11) -------------------------------
        public RaidPhase Phase { get; private set; } = RaidPhase.None;
        public RaidStage? Stage { get; private set; }
        public RaidDoctrine? Doctrine { get; private set; }
        public string TargetPlanetWorldId { get; private set; }
        public List<string> CompositionDefIds { get; } = new List<string>();
        public ulong EncounterSeed { get; private set; }
        public float WarningRemainingSeconds { get; private set; }
        public float ElapsedActiveSeconds { get; private set; }

        /// <summary>VS-014 §11: real EnemyAgent instance ids physically spawned for this raid (never re-rolled) — the "all this raid's enemies are gone" resolve check reads this, never CompositionDefIds (which is only the def-id recipe).</summary>
        public List<string> SpawnedEnemyIds { get; } = new List<string>();
        public void NoteSpawnedEnemy(string enemyId) { if (!string.IsNullOrEmpty(enemyId)) SpawnedEnemyIds.Add(enemyId); }

        public bool IsRaidInProgress => Phase != RaidPhase.None && Phase != RaidPhase.Resolved;

        /// <summary>Begins a freshly-composed raid at Scheduled — every per-raid participation/result/salvage guard resets here, exactly once per raid identity.</summary>
        public void ScheduleRaid(RaidStage stage, RaidDoctrine doctrine, string targetPlanetWorldId,
            IEnumerable<string> compositionDefIds, ulong encounterSeed, float warningSeconds)
        {
            Phase = RaidPhase.Scheduled;
            Stage = stage;
            Doctrine = doctrine;
            TargetPlanetWorldId = targetPlanetWorldId;
            CompositionDefIds.Clear();
            if (compositionDefIds != null) CompositionDefIds.AddRange(compositionDefIds);
            SpawnedEnemyIds.Clear();
            EncounterSeed = encounterSeed;
            WarningRemainingSeconds = Mathf.Max(0f, warningSeconds);
            ElapsedActiveSeconds = 0f;
            PlayerDealtMeaningfulDamage = false;
            PlayerIssuedValidFocusCommand = false;
            PlayerActiveDefenseParticipation = false;
            PlayerWasShutdownDuringRaid = false;
            AnyResourceStolen = false;
            ResultApplied = false;
            SalvageApplied = false;
            PendingProbeEffectConsumedThisRaid = false;
            WasTargetContestedDuringRaid = false;
            warningFired = false;
        }

        public void SetPhase(RaidPhase phase) => Phase = phase;

        /// <summary>
        /// VS-014.1 §3: in-memory-only fire-once guard for TickWarning — never
        /// part of save v12 (no schema field), since it always starts false
        /// on both a fresh instance and after RestoreFromSave, which is
        /// exactly the correct behavior for a raid loaded mid-Warning: it
        /// must still be able to advance exactly once after load.
        /// </summary>
        private bool warningFired;

        /// <summary>
        /// Returns true exactly once per raid, the tick the warning reaches
        /// 0 — mirrors FirstProbeState.TickWarning's own guarantee. Unlike
        /// FirstProbeState, this never changes Phase itself (RaidScheduler
        /// stays the sole phase-transition authority, VS-014 §11) — it only
        /// remembers that it already reported expiry once, so a caller that
        /// ticks this again before RaidScheduler moves Phase off Warning
        /// (or a caller bug that ticks it twice in one step) can never
        /// double-fire the same expiry event.
        /// </summary>
        public bool TickWarning(float dt)
        {
            if (Phase != RaidPhase.Warning || warningFired) return false;
            WarningRemainingSeconds -= dt;
            if (WarningRemainingSeconds > 0f) return false;
            WarningRemainingSeconds = 0f;
            warningFired = true;
            return true;
        }

        public void TickActive(float dt) { if (Phase == RaidPhase.Active) ElapsedActiveSeconds += dt; }

        // --- Player participation / result guards (VS-014 §13) ----------------------
        public bool PlayerDealtMeaningfulDamage { get; private set; }
        public bool PlayerIssuedValidFocusCommand { get; private set; }
        public bool PlayerActiveDefenseParticipation { get; private set; }
        public bool PlayerWasShutdownDuringRaid { get; private set; }
        public bool AnyResourceStolen { get; private set; }
        public bool ResultApplied { get; private set; }
        public bool SalvageApplied { get; private set; }
        public bool PendingProbeEffectConsumedThisRaid { get; private set; }
        /// <summary>VS-014 §13: whether the target planet's Command was ever Contested at any point during this raid (even if it recovered) — the outcome resolver's "costly" signal.</summary>
        public bool WasTargetContestedDuringRaid { get; private set; }

        public void NoteMeaningfulDamage() => PlayerDealtMeaningfulDamage = true;
        public void NoteValidFocusCommand() => PlayerIssuedValidFocusCommand = true;
        public void NoteActiveDefenseParticipation() => PlayerActiveDefenseParticipation = true;
        public void NotePlayerShutdown() => PlayerWasShutdownDuringRaid = true;
        public void NoteResourceStolen() => AnyResourceStolen = true;
        public void NoteTargetContested() => WasTargetContestedDuringRaid = true;

        /// <summary>§13: "meaningful" = real damage/EM, a valid Focus command, or active-defense participation while targeted — never a bare kill/tick/duration credit.</summary>
        public bool HasMeaningfulParticipation => PlayerDealtMeaningfulDamage || PlayerIssuedValidFocusCommand || PlayerActiveDefenseParticipation;

        public void MarkResultApplied() => ResultApplied = true;
        public void MarkSalvageApplied() => SalvageApplied = true;
        public void MarkPendingProbeEffectConsumed() => PendingProbeEffectConsumedThisRaid = true;

        public void ResolveRaid() => Phase = RaidPhase.Resolved;

        /// <summary>Back to no-raid-pending idle — called once the resolved raid's result/salvage/history bookkeeping is fully done.</summary>
        public void ClearToIdle()
        {
            Phase = RaidPhase.None;
            Stage = null;
            Doctrine = null;
            TargetPlanetWorldId = null;
            CompositionDefIds.Clear();
        }

        public void RestoreFromSave(float pressure, float graceRemainingActiveMinutes, bool foreignPresenceActive, float foreignPresenceActiveMinutesElapsed,
            RaidPhase phase, RaidStage? stage, RaidDoctrine? doctrine, string targetPlanetWorldId, IEnumerable<string> compositionDefIds,
            ulong encounterSeed, float warningRemainingSeconds, float elapsedActiveSeconds,
            bool playerDealtMeaningfulDamage, bool playerIssuedValidFocusCommand, bool playerActiveDefenseParticipation, bool playerWasShutdownDuringRaid,
            bool anyResourceStolen, bool resultApplied, bool salvageApplied, bool pendingProbeEffectConsumedThisRaid,
            bool firstControlledGraceGranted = false, IEnumerable<string> spawnedEnemyIds = null, bool wasTargetContestedDuringRaid = false)
        {
            FirstControlledGraceGranted = firstControlledGraceGranted;
            WasTargetContestedDuringRaid = wasTargetContestedDuringRaid;
            SpawnedEnemyIds.Clear();
            if (spawnedEnemyIds != null) SpawnedEnemyIds.AddRange(spawnedEnemyIds);
            Pressure = Mathf.Clamp(pressure, 0f, MaxPressure);
            GraceRemainingActiveMinutes = Mathf.Max(0f, graceRemainingActiveMinutes);
            ForeignPresenceActive = foreignPresenceActive;
            ForeignPresenceActiveMinutesElapsed = foreignPresenceActiveMinutesElapsed;
            Phase = phase;
            Stage = stage;
            Doctrine = doctrine;
            TargetPlanetWorldId = targetPlanetWorldId;
            CompositionDefIds.Clear();
            if (compositionDefIds != null) CompositionDefIds.AddRange(compositionDefIds);
            EncounterSeed = encounterSeed;
            WarningRemainingSeconds = Mathf.Max(0f, warningRemainingSeconds);
            ElapsedActiveSeconds = Mathf.Max(0f, elapsedActiveSeconds);
            PlayerDealtMeaningfulDamage = playerDealtMeaningfulDamage;
            PlayerIssuedValidFocusCommand = playerIssuedValidFocusCommand;
            PlayerActiveDefenseParticipation = playerActiveDefenseParticipation;
            PlayerWasShutdownDuringRaid = playerWasShutdownDuringRaid;
            AnyResourceStolen = anyResourceStolen;
            ResultApplied = resultApplied;
            SalvageApplied = salvageApplied;
            PendingProbeEffectConsumedThisRaid = pendingProbeEffectConsumedThisRaid;
        }
    }
}
