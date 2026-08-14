using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.World;
using Nebula.Visuals;

namespace Nebula.Ship
{
    public enum ShipDamageLayer { Shield, Armor, Hull }

    /// <summary>
    /// Ship-side combat component (VS-013): owns <see cref="ShipCombatState"/>
    /// (K/Z/G, energy, boost) and up to 5 <see cref="HardpointState"/>
    /// slots, resolves fire input into damage/heat/energy through the pure
    /// <see cref="WeaponCatalog"/> math, and tracks the current target for
    /// the HUD/EM lock. Never a second speed or movement authority — Combat
    /// Boost only ever multiplies <see cref="ShipController"/>'s own
    /// commanded speed for its duration, exactly like DT-001's test
    /// multiplier composes into the same single formula.
    /// </summary>
    public class ShipCombat : MonoBehaviour, ICombatTarget
    {
        public event Action<WeaponFamily> WeaponFired;
        public event Action FireDenied;
        public event Action<EnemyAgent> TargetChanged;
        public event Action<ShipDamageLayer, bool, bool> DamageApplied;
        public event Action EmergencyShutdownStarted;
        public event Action RepairStarted;
        public event Action RepairCompleted;
        public event Action RepairCancelled;

        private ShipController ship;
        private Modules.ShipModules modules;
        private Extraction.ShipLogistics logistics;

        public ShipCombatState State { get; private set; }
        public HardpointState[] Hardpoints { get; private set; }
        public EmergencyShutdownState Shutdown { get; private set; } = new EmergencyShutdownState();
        public WeaponProductionJob ActiveProductionJob { get; private set; }

        public RepairJobState Repair { get; private set; } = new RepairJobState();
        public RepairRejectReason LastRepairRejectReason { get; private set; } = RepairRejectReason.None;

        public EnemyAgent Target { get; private set; }
        private string emLockTargetId;
        private float emLockElapsed;
        private float emLockOutOfConeSeconds;

        // P1-COMBAT-009: `boostSpeedMultiplierApplied` used to live here. It was written
        // every frame by ApplyBoostToShipSpeed and read by nothing — a second copy of a
        // number ShipController already owns (CombatBoostSpeedMultiplier), kept in step
        // by nothing but the one line that wrote both. A duplicate of an authority is a
        // divergence waiting for its first missed assignment, so it is gone rather than
        // kept "for a future readout": anything that wants the applied multiplier reads
        // it from the ship, which is where it is actually used.
        private ShipWeaponVisuals weaponVisuals;
        private float repairVfxCooldown;

        public int HullReinforcementTier => modules != null ? modules.Modules.GetTier("defense.hull_reinforcement") : 0;
        public int ShieldSystemsTier => modules != null ? modules.Modules.GetTier("defense.shield_systems") : 0;
        public int WeaponCoordinationTier => modules != null ? modules.Modules.GetTier("defense.weapon_coordination") : 0;

        /// <summary>Hull Form (VS-013.1 §1): purely level-derived, never a purchasable module — re-read fresh every frame, never cached in save data.</summary>
        public int HullFormValue => CombatCatalog.HullFormForLevel(Progression.ProgressionService.State.Level);

        /// <summary>Hardpoint count comes from Hull Form, never from defense.hull_reinforcement (VS-013.1 fix).</summary>
        public int ActiveHardpointCount => CombatCatalog.ActiveHardpointSlots(HullFormValue, WeaponCoordinationTier);

        private int lastAppliedHullForm = -1;

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            modules = GetComponent<Modules.ShipModules>();
            logistics = GetComponent<Extraction.ShipLogistics>();
            weaponVisuals = GetComponent<ShipWeaponVisuals>();
        }

        /// <summary>
        /// Declares the Combat context to the router.
        /// <para>
        /// Combat is live exactly while the HUD owns input — that is the rule the
        /// reserved-key derivation already states and the reason a non-combat owner
        /// that is also live then holds its key against combat permanently. Registering
        /// it is what lets the nine combat rows be read through
        /// <see cref="NebulaInputRouter"/> instead of through
        /// <c>CombatBindings.Input.GetKey*</c>: the service still owns the binding, but
        /// the sample now comes through the same substitutable source and the same
        /// single-owner arbitration as every other action in the game.
        /// </para>
        /// </summary>
        private void OnEnable() =>
            NebulaInputRouter.SetContextSource(InputContext.Combat, () => !InputGate.Suppressed);

        private void OnDisable() => NebulaInputRouter.SetContextSource(InputContext.Combat, null);

        /// <summary>
        /// What releasing the weapon in hardpoint <paramref name="index"/> would refuse
        /// with, or <see cref="HardpointReleaseRejectReason.None"/>. Pure: it frees
        /// nothing, so a page can ask it on every repaint to decide whether its control
        /// is interactable and what to print underneath when it is not.
        /// </summary>
        public HardpointReleaseRejectReason CheckReleaseHardpoint(int index) =>
            WeaponLoadoutService.CheckRelease(ActiveProductionJob, HardpointAt(index), InCombat);

        /// <summary>
        /// Frees a hardpoint so a new weapon can be produced into it.
        /// <para>
        /// The ship-side seam for <see cref="WeaponLoadoutService.TryRelease"/>, which
        /// is the single authority for the rule. It matters because Hull Form I opens
        /// exactly one hardpoint and the campaign starts with the free T1 Laser already
        /// in it: with nothing able to empty a slot, <see cref="TryStartProduction"/>
        /// answered <see cref="WeaponProductionRejectReason.NoHardpointAvailable"/> for
        /// the rest of the run, and the Savaş page's three production buttons were dead
        /// controls with a correct-sounding refusal behind them.
        /// </para>
        /// </summary>
        public HardpointReleaseRejectReason TryReleaseHardpoint(int index) =>
            WeaponLoadoutService.TryRelease(ActiveProductionJob, HardpointAt(index), InCombat);

        private HardpointState HardpointAt(int index) =>
            Hardpoints != null && index >= 0 && index < Hardpoints.Length ? Hardpoints[index] : null;

        /// <summary>New-campaign start: Hull Form I baseline with hardpoint 0 permanently pre-equipped with the free, fixed T1 Laser (VS-013 §2).</summary>
        public void InitializeNewCampaign()
        {
            lastAppliedHullForm = HullFormValue;
            State = new ShipCombatState(HullFormValue, HullReinforcementTier, ShieldSystemsTier, WeaponCoordinationTier);
            Hardpoints = new HardpointState[CombatCatalog.HullFormHardpointsByForm[^1]];
            for (int i = 0; i < Hardpoints.Length; i++) Hardpoints[i] = new HardpointState(i);
            Hardpoints[0].Equip(WeaponFamily.Laser, 1);
        }

        /// <summary>
        /// Re-derives max Shield/Armor/Hull/Energy from the CURRENT Hull
        /// Form + module tiers, preserving current percentages (VS-013.1
        /// §1/§2). Called whenever a defense.* module purchase completes
        /// (ShipModules.OnJobCompleted) and every FixedUpdate tick to catch a
        /// Hull Form change from leveling up — a level crossing is not a
        /// discrete "job completed" event, so it must be polled.
        /// </summary>
        public void ReapplyDurabilityTiers()
        {
            if (State == null) return;
            State.ApplyModuleTiers(HullFormValue, HullReinforcementTier, ShieldSystemsTier, WeaponCoordinationTier, resetCurrentToFull: false);
        }

        public bool InCombat { get; private set; }
        public void SetEncounterOpen(bool open) { InCombat = open; CombatInputContext.CombatActive = open; }

        /// <summary>VS-013 §10 load restore: overwrites the already-InitializeNewCampaign'd State/Hardpoints in place — never re-constructs them, so any live reference an installer holds stays valid across a load.</summary>
        public void RestoreFromSave(float shield, float armor, float hull, float regenTimer, float energy,
            bool weaponsLocked, float boostRemaining, float boostCooldownRemaining, bool damaged,
            IEnumerable<(int index, bool equipped, WeaponFamily weaponFamily, int tier, float heat, bool overheated)> hardpoints,
            string targetEnemyId)
        {
            State.RestoreFromSave(shield, armor, hull, regenTimer, energy, weaponsLocked, boostRemaining, boostCooldownRemaining, damaged);
            foreach (var h in hardpoints)
            {
                if (h.index < 0 || h.index >= Hardpoints.Length) continue;
                if (h.equipped) Hardpoints[h.index].RestoreFromSave(h.weaponFamily, h.tier, h.heat, h.overheated, 0f);
                else Hardpoints[h.index].RestoreFromSave(null, 0, 0f, false, 0f);
            }
            Target = targetEnemyId != null ? EnemyAgent.Find(targetEnemyId) : null;
        }

        public bool RestoreProductionJob(WeaponFamily family, int targetTier, int reservedParts, string materialId, int reservedMaterial, float totalSeconds, float remainingSeconds)
        {
            var costs = new List<(string itemId, float amount)>
            {
                (ProcessingCatalog.BasicPartId, reservedParts)
            };
            if (!string.IsNullOrEmpty(materialId) && reservedMaterial > 0) costs.Add((materialId, reservedMaterial));
            LogisticsCargo cargo = (logistics != null ? logistics : (logistics = GetComponent<Extraction.ShipLogistics>()))?.Cargo;
            if (cargo == null || !cargo.TrySetReservation(WeaponProductionJob.CargoOwnerId, costs))
            {
                ActiveProductionJob = null;
                return false;
            }
            ActiveProductionJob = new WeaponProductionJob(family, targetTier, reservedParts, materialId, reservedMaterial, totalSeconds, remainingSeconds);
            return true;
        }

        private void Update()
        {
            if (State == null) return;
            if (State.EmergencyShutdown) { UpdateShutdown(); return; }
            if (InputGate.Suppressed) return;

            HandleTargeting();
            HandleBoostInput();
            HandleWeaponFire();
            HandleRepairInput();
            HandleDefenseCoordinationInput();
        }

        private void FixedUpdate()
        {
            if (State == null) return;
            float dt = Time.fixedDeltaTime;

            // Hull Form is purely level-derived (VS-013.1 §1) -- a level-up crossing
            // L21/L41 is not a discrete "job completed" event like a module purchase,
            // so it must be polled here rather than event-driven.
            int currentHullForm = HullFormValue;
            if (currentHullForm != lastAppliedHullForm)
            {
                lastAppliedHullForm = currentHullForm;
                ReapplyDurabilityTiers();
            }

            StellarRiskState risk = CurrentStarRisk();
            State.Tick(dt, risk);
            foreach (HardpointState hp in Hardpoints) hp.Tick(dt, risk.EnvironmentalHeatPerSecondFractionOfMax * WeaponCatalog.HardpointMaxHeat);

            AdvanceProduction(dt);

            ApplyBoostToShipSpeed();
            TickRepair(dt, currentHullForm);

            if (State.EmergencyShutdown) TriggerShutdown();
        }

        private StellarRiskState CurrentStarRisk()
        {
            Vector3 shipAbs = transform.position + FloatingOrigin.TotalOffset;
            foreach (StarSystemRecord record in StarSystemRegistry.SystemsNear(shipAbs))
            {
                float distance = Vector3.Distance(shipAbs, record.AbsoluteCenter);
                StellarRiskState risk = StellarRiskResolver.Resolve(distance, record.StarRadius, record.StarClass);
                if (risk.Level != StellarRiskLevel.Safe) return risk;
            }
            return new StellarRiskState(StellarRiskLevel.Safe, 1f, 1f, 1f, 0f, true);
        }

        // --- Targeting -------------------------------------------------------------
        private void HandleTargeting()
        {
            if (NebulaInputRouter.Pressed(NebulaInputActions.CombatManualTarget)) TrySelectNearestTarget();
            if (NebulaInputRouter.Pressed(NebulaInputActions.CombatCycleTarget)) TryCycleTarget();
            if (Target != null && Target.Destroyed) SetTarget(null);
        }

        /// <summary>
        /// Acquires the nearest live enemy. Returns false when there is nothing to
        /// lock, leaving the current target alone.
        /// <para>
        /// Lifted out of <see cref="HandleTargeting"/> so target acquisition has a
        /// seam that is not a keypress. It was reachable only from
        /// <c>Input.GetKeyDown</c> through a private <c>SetTarget</c>, which made
        /// targeting the one combat action nothing else in the project could
        /// perform or observe being performed.
        /// </para>
        /// </summary>
        public bool TrySelectNearestTarget()
        {
            EnemyAgent nearest = FindNearestEnemy(excludeCurrent: false);
            if (nearest == null) return false;
            SetTarget(nearest);
            return true;
        }

        /// <summary>
        /// Moves to the next nearest enemy that is not the current target. Returns
        /// false when the current target is the only one.
        /// </summary>
        public bool TryCycleTarget()
        {
            EnemyAgent next = FindNearestEnemy(excludeCurrent: true);
            if (next == null) return false;
            SetTarget(next);
            return true;
        }

        private void SetTarget(EnemyAgent agent)
        {
            if (Target == agent) return;
            Target = agent;
            emLockTargetId = null; emLockElapsed = 0f; emLockOutOfConeSeconds = 0f;
            if (agent != null) RegisterThreat();
            TargetChanged?.Invoke(agent);
        }

        private EnemyAgent FindNearestEnemy(bool excludeCurrent)
        {
            EnemyAgent best = null; float bestSqr = float.MaxValue;
            foreach (EnemyAgent a in EnemyAgent.All.Values)
            {
                if (a == null || a.Destroyed) continue;
                if (excludeCurrent && a == Target) continue;
                float sqr = (a.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = a; }
            }
            return best;
        }

        // --- Planetary defense coordination (VS-014 §4) -----------------------------
        /// <summary>
        /// Alpha7 cycles, Alpha8 focuses -- both act on the nearest planet with an
        /// active Command Center to the player (the closest real stand-in for "the
        /// active raid's target planet" available before the raid scheduler/
        /// ThreatPressureState exists — once that lands it should prefer the actual
        /// active raid's target planet and only fall back to nearest-Command). An
        /// invalid target (no Command planet in range, or no current ship Target for
        /// SetFocusTarget) is a no-op: no state change, per §4's explicit-reject rule.
        /// </summary>
        /// <summary>
        /// Why the last Alpha7/Alpha8 order did nothing, or
        /// <see cref="DefenseCoordinationRejectReason.None"/>. State, not an event, for
        /// the same reason <see cref="LastRepairRejectReason"/> is: a screen repainting
        /// at its own rate has to be able to ask.
        /// </summary>
        public DefenseCoordinationRejectReason LastDefenseCoordinationReject { get; private set; }
            = DefenseCoordinationRejectReason.None;

        private void HandleDefenseCoordinationInput()
        {
            bool cycle = NebulaInputRouter.Pressed(NebulaInputActions.CombatCycleDefenseMode);
            bool focus = NebulaInputRouter.Pressed(NebulaInputActions.CombatSetFocusTarget);
            if (!cycle && !focus) return;

            if (director == null) { RecordDefenseCoordinationReject(DefenseCoordinationRejectReason.NoEncounterDirector); return; }

            string worldId = CommandPlanetInCoordinationRange();
            if (worldId == null) { RecordDefenseCoordinationReject(DefenseCoordinationRejectReason.NoCommandPlanetInRange); return; }

            if (cycle)
            {
                director.DefenseCoordinator.CycleMode(worldId);
                RecordDefenseCoordinationReject(DefenseCoordinationRejectReason.None);
            }
            if (focus)
            {
                if (Target == null || Target.Destroyed) { RecordDefenseCoordinationReject(DefenseCoordinationRejectReason.NoLiveTarget); return; }
                director.DefenseCoordinator.SetFocus(worldId, Target.Id);
                RecordDefenseCoordinationReject(DefenseCoordinationRejectReason.None);
                // VS-014 §13: a valid Focus command is one of the three meaningful-participation signals -- only counts when it actually landed (a real planet, a real live target), matching the reject-on-invalid rule above.
                if (director.Pressure.Phase == RaidPhase.Active && worldId == director.Pressure.TargetPlanetWorldId) director.Pressure.NoteValidFocusCommand();
            }
        }

        /// <summary>Publishes on the same typed channel every other combat refusal uses, so a coordination order is as audible as a repair refusal.</summary>
        private void RecordDefenseCoordinationReject(DefenseCoordinationRejectReason reason)
        {
            LastDefenseCoordinationReject = reason;
            if (reason == DefenseCoordinationRejectReason.None) return;
            NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Combat,
                NebulaGameplayFeedback.DefenseCoordinationText(reason), GameplayFeedbackTone.Rejection);
        }

        /// <summary>
        /// The nearest planet with an active Command Center that is actually within
        /// <see cref="PlanetaryDefenseCoordinator.CoordinationRange"/>, or null. The
        /// range test is the fix: "nearest" alone always found something, however far
        /// away, so the order silently landed on a planet the player had no part in.
        /// </summary>
        private string CommandPlanetInCoordinationRange()
        {
            float limit = PlanetaryDefenseCoordinator.CoordinationRange;
            float limitSqr = limit * limit;
            Extraction.SurfaceStructure best = null; float bestSqr = float.MaxValue;
            foreach (Extraction.SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || s.Type != Extraction.SurfaceStructureType.Command || !s.Runtime.IsActive) continue;
                float sqr = (s.transform.position - transform.position).sqrMagnitude;
                if (sqr > limitSqr) continue;
                if (sqr < bestSqr) { bestSqr = sqr; best = s; }
            }
            return best?.WorldId;
        }

        // --- Combat encounter open trigger -----------------------------------------
        private CombatEncounterDirector director;
        public CombatEncounterDirector Director => director;
        public void SetDirector(CombatEncounterDirector d) => director = d;
        private void RegisterThreat()
        {
            director?.Encounter.RegisterThreatEvent();
            // VS-014 §13: "real damage/EM" is one of the three meaningful-participation signals -- every weapon-fire call site already routes threat registration through here, so this is the one place to note it, never duplicated per weapon type.
            if (director != null && director.Pressure.Phase == RaidPhase.Active) director.Pressure.NoteMeaningfulDamage();
        }

        // --- VS-013.2: Safety Marker authority (physical repair safe-zone check) ---
        private SafetyMarkerAnchor safetyMarker;
        public void SetSafetyMarker(SafetyMarkerAnchor anchor) => safetyMarker = anchor;

        // --- Boost -------------------------------------------------------------------
        private void HandleBoostInput()
        {
            if (NebulaInputRouter.Pressed(NebulaInputActions.CombatBoost)) State.TryStartBoost();
        }

        private void ApplyBoostToShipSpeed()
        {
            float target = State.IsBoosting ? CombatCatalog.BoostSpeedMultiplier : 1f;
            ship.CombatBoostSpeedMultiplier = target;
            ship.DamagedSpeedMultiplier = State.Damaged ? CombatCatalog.DamagedFlightSpeedMultiplier : 1f;
        }

        // --- Weapon fire ---------------------------------------------------------------
        private void HandleWeaponFire()
        {
            if (Hardpoints == null) return;
            // Sampled once each, then reused. Three router reads instead of nine, and
            // more importantly one answer per family per frame: asking twice inside the
            // loop could disagree if a binding moved mid-frame.
            bool laserHeld = WeaponHeld(NebulaInputActions.CombatFireLaser);
            bool plasmaHeld = WeaponHeld(NebulaInputActions.CombatFirePlasma);
            bool emHeld = WeaponHeld(NebulaInputActions.CombatFireEm);

            bool requested = laserHeld || plasmaHeld || emHeld;

            // Which controls are being asked for, so that "one press is one refusal"
            // can be read as one press of *a control* rather than one unbroken period
            // of any control being held. A player holding left mouse with nothing in
            // range is refused once; pressing right mouse as well is a second, separate
            // press for a family the ship may not even carry, and it used to be
            // swallowed because the shared latch was already set and no frame with
            // everything released ever came round to clear it. Only a rising edge
            // re-arms: letting go of one of two held controls is not a new press.
            int requestMask = (laserHeld ? 1 : 0) | (plasmaHeld ? 2 : 0) | (emHeld ? 4 : 0);
            if ((requestMask & ~lastRequestMask) != 0) fireDeniedAnnounced = false;
            lastRequestMask = requestMask;

            int beforeSerial = weaponFireSerial;
            int activeSlots = ActiveHardpointCount;
            for (int i = 0; i < Hardpoints.Length && i < activeSlots; i++)
            {
                HardpointState hp = Hardpoints[i];
                if (hp.IsEmpty || !hp.CanFire) continue;

                switch (hp.Weapon)
                {
                    case WeaponFamily.Laser:
                        if (laserHeld) FireLaser(hp);
                        break;
                    case WeaponFamily.Plasma:
                        if (plasmaHeld) FirePlasma(hp);
                        break;
                    case WeaponFamily.Em:
                        if (emHeld) TickEmLock(hp);
                        break;
                }
            }
            if (!requested) { fireDeniedAnnounced = false; return; }

            if (beforeSerial != weaponFireSerial) { fireDeniedAnnounced = false; return; }

            // Edge-triggered, not per frame. Holding the fire control for an unequipped
            // family used to raise this every single frame — sixty refusals a second
            // into the audio bridge and, through it, sixty refusal cues — which is not
            // a louder answer, it is an answer nobody can read. One press is one
            // refusal; the latch clears the moment the player lets go or the weapon
            // actually fires, so a second attempt is heard again.
            if (fireDeniedAnnounced) return;
            fireDeniedAnnounced = true;
            FireDenied?.Invoke();
        }

        /// <summary>
        /// One weapon family's control, with the pointer-capture rule applied.
        /// <para>
        /// A weapon bound to a mouse button may not fire on the frame the player
        /// clicked the game view to take the pointer: that click is the capture, and
        /// firing on it as well makes one input do two things. A weapon bound to a
        /// keyboard key is unaffected — the rule is about the pointer, not about
        /// combat — so rebinding fire onto a letter removes the condition with it.
        /// See <see cref="ShipController.PointerAimLive"/> for why the question is
        /// asked about the previous frame.
        /// </para>
        /// </summary>
        private static bool WeaponHeld(string actionId)
        {
            if (!NebulaInputRouter.Held(actionId)) return false;
            KeyCode key = NebulaInputActions.Key(actionId);
            return !IsPointerButton(key) || ShipController.PointerAimLive;
        }

        private static bool IsPointerButton(KeyCode key) =>
            key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6;

        private bool fireDeniedAnnounced;

        /// <summary>Which fire controls were held last frame, as laser|plasma|em bits. See HandleWeaponFire.</summary>
        private int lastRequestMask;

        private int weaponFireSerial;

        private void FireLaser(HardpointState hp)
        {
            if (Target == null || Target.Destroyed) return;
            float dt = Time.deltaTime;
            float energyCost = WeaponCatalog.LaserEnergyPerTick(hp.Tier, dt);

            Vector3 toTarget = Target.transform.position - transform.position;
            float distance = toTarget.magnitude;
            if (distance > WeaponCatalog.LaserRange(hp.Tier)) return;
            float gimbalAngle = Vector3.Angle(transform.forward, toTarget);
            if (gimbalAngle > WeaponCatalog.LaserGimbalDegrees) return;
            if (!HasLineOfSight(transform.position, Target.transform.position)) return;
            if (!State.TryConsumeEnergy(energyCost)) return;

            hp.AddHeat(WeaponCatalog.LaserHeatPerSecond * dt);
            Target.ApplyDamage(WeaponCatalog.LaserDamagePerTick(hp.Tier, dt), DamageProfile.Laser);
            weaponFireSerial++;
            WeaponFired?.Invoke(WeaponFamily.Laser);
            laserVisualCooldownRemaining -= dt;
            if (laserVisualCooldownRemaining <= 0f)
            {
                ShipWeaponVisuals visuals = weaponVisuals != null ? weaponVisuals : weaponVisuals = GetComponent<ShipWeaponVisuals>();
                Vector3 muzzle = visuals != null ? visuals.GetMuzzle(WeaponFamily.Laser).position : transform.position;
                LaserBeamPulse.Spawn(muzzle, Target.transform.position, transform.parent);
                laserVisualCooldownRemaining = 0.08f;
            }
            RegisterThreat();
        }

        private float plasmaCooldownRemaining;
        private float laserVisualCooldownRemaining;
        private void FirePlasma(HardpointState hp)
        {
            if (Target == null || Target.Destroyed) return;
            if (plasmaCooldownRemaining > 0f) return;
            float distance = Vector3.Distance(transform.position, Target.transform.position);
            if (distance > WeaponCatalog.PlasmaRange(hp.Tier)) return;
            if (!State.TryConsumeEnergy(WeaponCatalog.PlasmaEnergyPerShot)) return;

            hp.AddHeat(WeaponCatalog.PlasmaHeatPerShot);
            plasmaCooldownRemaining = WeaponCatalog.PlasmaFireIntervalSeconds;
            Vector3 direction = (Target.transform.position - transform.position).normalized;
            ShipWeaponVisuals visuals = weaponVisuals != null ? weaponVisuals : weaponVisuals = GetComponent<ShipWeaponVisuals>();
            Vector3 muzzle = visuals != null ? visuals.GetMuzzle(WeaponFamily.Plasma).position : transform.position;
            ProjectileObject.Spawn(ProjectileKind.Plasma, hp.Tier, "ship", muzzle, direction * WeaponCatalog.PlasmaProjectileSpeed,
                WeaponCatalog.PlasmaRange(hp.Tier) / WeaponCatalog.PlasmaProjectileSpeed, null, director);
            weaponFireSerial++;
            WeaponFired?.Invoke(WeaponFamily.Plasma);
            RegisterThreat();
        }

        private void TickEmLock(HardpointState hp)
        {
            if (Target == null || Target.Destroyed) { emLockElapsed = 0f; return; }
            float dt = Time.deltaTime;
            float distance = Vector3.Distance(transform.position, Target.transform.position);
            Vector3 toTarget = Target.transform.position - transform.position;
            float angle = Vector3.Angle(transform.forward, toTarget);
            bool withinCone = distance <= WeaponCatalog.EmRange(hp.Tier) && angle <= WeaponCatalog.EmLockConeDegrees;

            if (Target.Id != emLockTargetId) { emLockTargetId = Target.Id; emLockElapsed = 0f; emLockOutOfConeSeconds = 0f; }

            if (withinCone)
            {
                emLockElapsed += dt;
                emLockOutOfConeSeconds = 0f;
            }
            else
            {
                emLockOutOfConeSeconds += dt;
                if (emLockOutOfConeSeconds >= WeaponCatalog.EmLockBreakGraceSeconds) { emLockElapsed = 0f; emLockTargetId = null; }
                return;
            }

            if (emLockElapsed < WeaponCatalog.EmLockSeconds) return;
            if (!State.TryConsumeEnergy(WeaponCatalog.EmEnergyPerShot)) return;

            hp.AddHeat(WeaponCatalog.EmHeatPerShot);
            Vector3 direction = toTarget.normalized;
            ShipWeaponVisuals visuals = weaponVisuals != null ? weaponVisuals : weaponVisuals = GetComponent<ShipWeaponVisuals>();
            Vector3 muzzle = visuals != null ? visuals.GetMuzzle(WeaponFamily.Em).position : transform.position;
            ProjectileObject.Spawn(ProjectileKind.Em, hp.Tier, "ship", muzzle, direction * WeaponCatalog.EmProjectileSpeed,
                20f, Target.Id, director);
            weaponFireSerial++;
            WeaponFired?.Invoke(WeaponFamily.Em);
            emLockElapsed = 0f; emLockTargetId = null;
            RegisterThreat();
        }

        public float EmLockProgress01 => emLockTargetId != null ? Mathf.Clamp01(emLockElapsed / WeaponCatalog.EmLockSeconds) : 0f;

        private bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            if (Physics.Raycast(from, delta.normalized, out RaycastHit hit, distance))
            {
                // Only celestial bodies/asteroids block line of sight -- other ships/enemies never do.
                if (hit.collider.GetComponentInParent<Celestial.CelestialBody>() != null) return false;
            }
            return true;
        }

        private int FindEmptyHardpointIndex()
        {
            for (int i = 0; i < Hardpoints.Length; i++) if (Hardpoints[i].IsEmpty) return i;
            return -1;
        }

        // --- Weapon production -------------------------------------------------------
        /// <summary>
        /// Exactly the answer <see cref="TryStartProduction"/> would give, without
        /// starting anything. A surface needs this to decide whether its control may be
        /// live at all: offering a button and then refusing the click is the dead control
        /// this redesign removes, and the reason it would have published belongs on the
        /// disabled control instead. <see cref="TryStartProduction"/> is implemented on
        /// top of it so the two answers cannot drift apart.
        /// </summary>
        public WeaponProductionRejectReason CheckProduction(WeaponFamily family, int targetTier)
        {
            // The service's own gates speak first. Ahead of them, the hardpoint check
            // masked every other reason — including CombatActive — and answered every
            // click with "no free slot" for the whole early campaign.
            var reject = WeaponLoadoutService.CheckStart(ActiveProductionJob, family, targetTier, WeaponCoordinationTier, logistics?.Cargo, InCombat);
            if (reject != WeaponProductionRejectReason.None) return reject;
            return FindEmptyActiveHardpointIndex() < 0
                ? WeaponProductionRejectReason.NoHardpointAvailable
                : WeaponProductionRejectReason.None;
        }

        public WeaponProductionRejectReason TryStartProduction(WeaponFamily family, int targetTier)
        {
            WeaponProductionRejectReason reject = CheckProduction(family, targetTier);
            if (reject != WeaponProductionRejectReason.None) return reject;
            ActiveProductionJob = WeaponLoadoutService.TryStart(ActiveProductionJob, family, targetTier, WeaponCoordinationTier, logistics?.Cargo, InCombat);
            return ActiveProductionJob != null ? WeaponProductionRejectReason.None : WeaponProductionRejectReason.InsufficientResources;
        }

        public void CancelProduction()
        {
            if (ActiveProductionJob == null || logistics == null) return;
            WeaponLoadoutService.Cancel(logistics.Cargo);
            ActiveProductionJob = null;
        }

        /// <summary>
        /// Advances the single real weapon-production job and equips through
        /// <see cref="WeaponLoadoutService.Complete"/>. Public so deterministic
        /// tutorial preparation and tests can advance the same paid/reserved job;
        /// it is not an equip shortcut and cannot create a weapon without a job.
        /// </summary>
        public bool AdvanceProduction(float seconds)
        {
            if (ActiveProductionJob == null) return false;
            // Tick reports completion exactly once and false forever after. Reading
            // RemainingSeconds instead lets a later frame retry the equip, so a full
            // loadout on the completing frame parks the job at 100% rather than
            // stranding its reservation until the player pays the cancel penalty.
            if (seconds > 0f) ActiveProductionJob.Tick(seconds);
            if (ActiveProductionJob.RemainingSeconds > 0f) return false;

            int slot = FindEmptyActiveHardpointIndex();
            if (slot < 0 || logistics == null) return false;

            WeaponLoadoutService.Complete(ActiveProductionJob, logistics.Cargo, Hardpoints[slot]);
            ActiveProductionJob = null;
            return true;
        }

        private int FindEmptyActiveHardpointIndex()
        {
            if (Hardpoints == null) return -1;
            int count = Mathf.Min(ActiveHardpointCount, Hardpoints.Length);
            for (int i = 0; i < count; i++)
                if (Hardpoints[i] != null && Hardpoints[i].IsEmpty) return i;
            return -1;
        }

        // --- Repair (VS-013.1 §5) -----------------------------------------------------
        private void HandleRepairInput()
        {
            if (NebulaInputRouter.Pressed(NebulaInputActions.CombatRepair))
            {
                if (!Repair.Active) TryStartRepair();
            }
            // Shared-intent read: Escape is every surface's cancel and stays unarbitrated,
            // but the key comes from the combat.cancel_repair row rather than a literal.
            if (NebulaInputRouter.SharedPressed(NebulaInputActions.CombatCancelRepair) && Repair.Active) CancelRepair();
        }

        public RepairRejectReason CheckRepairStart()
        {
            bool armorAtCap = ArmorFraction() >= CurrentRepairCapFraction();
            bool hullAtCap = HullFraction() >= CurrentRepairCapFraction();
            return ShipRepairService.CheckStart(Repair, InCombat, State.EmergencyShutdown,
                State.SinceLastDamageSeconds, ship.Velocity.magnitude, armorAtCap, hullAtCap, HullFormValue, logistics?.Cargo);
        }

        public bool TryStartRepair()
        {
            RepairRejectReason reason = CheckRepairStart();
            LastRepairRejectReason = reason;
            if (reason != RepairRejectReason.None)
            {
                NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Combat,
                    NebulaGameplayFeedback.RepairText(reason), GameplayFeedbackTone.Rejection);
                return false;
            }
            bool started = ShipRepairService.TryStart(Repair, HullFormValue, logistics?.Cargo);
            if (!started)
            {
                // Same outcome as the checked rejections above, so it reaches the
                // player the same way rather than being recorded and never shown.
                LastRepairRejectReason = RepairRejectReason.InsufficientResources;
                NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Combat,
                    NebulaGameplayFeedback.RepairText(RepairRejectReason.InsufficientResources),
                    GameplayFeedbackTone.Rejection);
            }
            else
            {
                NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Combat, "ONARIM BAŞLADI", GameplayFeedbackTone.Success);
                RepairStarted?.Invoke();
            }
            return started;
        }

        public void CancelRepair()
        {
            if (!Repair.Active || logistics == null) return;
            ShipRepairService.Cancel(Repair, logistics.Cargo);
            RepairCancelled?.Invoke();
        }

        private float ArmorFraction() => State.Durability.ArmorMax > 0f ? State.Durability.Armor / State.Durability.ArmorMax : 1f;
        private float HullFraction() => State.Durability.HullMax > 0f ? State.Durability.Hull / State.Durability.HullMax : 1f;
        private float CurrentRepairCapFraction() => RepairSafeZoneResolver.CapFraction(IsInRepairSafeZone());

        /// <summary>
        /// VS-013.2: a REAL physical-proximity check, in absolute world units —
        /// never a system-wide/ownership shortcut. The ship gets the 100% cap
        /// only when it is within RepairCatalog.RepairSafeRadius of either the
        /// Safety Marker anchor or the real position of a live, Active,
        /// non-abandoned Command Center structure. Being in the start system, or
        /// a system merely having SOME active Command Center somewhere in it, is
        /// never alone sufficient. ExtractionState.Structures only ever holds
        /// entries for the currently Full-materialized system, so a Command
        /// Center in another system (or one that exists only as an unmaterialized
        /// SystemDelta entry) simply has no live GameObject here and is
        /// automatically excluded — no extra system-id filtering needed. The
        /// actual distance decision is delegated to the pure, unit-tested
        /// RepairSafeZoneResolver; this method only ever gathers real live
        /// positions.
        /// </summary>
        private bool IsInRepairSafeZone()
        {
            Vector3 shipAbs = transform.position + FloatingOrigin.TotalOffset;
            Vector3? markerAbs = safetyMarker != null ? safetyMarker.transform.position + FloatingOrigin.TotalOffset : (Vector3?)null;

            var activeCommandPositions = new List<Vector3>();
            foreach (SurfaceStructure structure in ExtractionState.Structures)
            {
                if (structure == null || structure.Type != SurfaceStructureType.Command) continue;
                if (!structure.Runtime.IsActive) continue;
                if (ExtractionState.IsPlanetAbandoned(structure.WorldId)) continue;
                activeCommandPositions.Add(structure.transform.position + FloatingOrigin.TotalOffset);
            }

            return RepairSafeZoneResolver.IsSafe(shipAbs, markerAbs, activeCommandPositions);
        }

        /// <summary>Movement, new damage, or combat pause an active repair job WITHOUT resetting its progress (VS-013.1 §5); it auto-resumes once the exact same start-eligibility conditions hold true again — no separate second timer.</summary>
        private void TickRepair(float dt, int currentHullForm)
        {
            if (!Repair.Active || logistics == null) return;

            bool mustPause = InCombat
                || State.SinceLastDamageSeconds < RepairCatalog.RequiredSafeSecondsSinceLastDamage
                || ship.Velocity.magnitude > RepairCatalog.RequiredMaxShipSpeed;
            if (mustPause) { Repair.Pause(); return; }
            if (Repair.Paused) Repair.Resume();

            float capFraction = CurrentRepairCapFraction();
            float armorCap = State.Durability.ArmorMax * capFraction;
            float hullCap = State.Durability.HullMax * capFraction;
            float combinedMax = State.Durability.ArmorMax + State.Durability.HullMax;
            float wantHp = RepairCatalog.RatePerSecondOfCombinedMax * combinedMax * dt;

            (float toArmor, float toHull) = RepairAllocator.Allocate(wantHp,
                State.Durability.Armor, State.Durability.ArmorMax, armorCap,
                State.Durability.Hull, State.Durability.HullMax, hullCap);

            if (toArmor > 0f || toHull > 0f)
            {
                State.Durability.RepairExact(toArmor, toHull);
                State.ClearDamagedIfHealedEnough();
                float progressDelta = Repair.AdvanceByAppliedHp(toArmor + toHull, combinedMax);
                ShipRepairService.ConsumeProportional(Repair, logistics.Cargo, progressDelta);
                repairVfxCooldown -= dt;
                if (repairVfxCooldown <= 0f)
                {
                    NebulaVfxPulse.Emit(
                        NebulaCommonVfx.RepairPulse, transform.position, 14f,
                        new Color(0.25f, 1f, 0.62f, 0.75f), 0.7f, transform.parent);
                    repairVfxCooldown = 0.6f;
                }
            }

            if (Repair.PackageComplete)
            {
                bool armorAtCap = ArmorFraction() >= capFraction;
                bool hullAtCap = HullFraction() >= capFraction;
                bool wasActive = Repair.Active;
                ShipRepairService.CompletePackageAndTryContinue(Repair, currentHullForm, logistics.Cargo, armorAtCap, hullAtCap);
                if (wasActive && !Repair.Active) RepairCompleted?.Invoke();
            }
        }

        /// <summary>
        /// LogisticsCargo's reservation MAP is never itself persisted (only
        /// physical stock is) — restoring an Active job must therefore
        /// re-reserve here, or the next ConsumePartialReservation call would
        /// silently no-op (no reservation entry exists) and the rest of that
        /// package would be repaired for free. VS-013.2: the reserved amount
        /// must be ONLY the still-unconsumed remaining fraction of the
        /// package — packageProgress01's worth was already physically
        /// consumed from stock before save, so re-reserving the FULL package
        /// cost here would be an extra phantom reservation on top of stock
        /// already spent. ShipRepairService.RestorePartialReservation clears
        /// any stale reservation first, then reserves exactly the remainder.
        /// If that reservation fails (stock no longer sufficient), the job is
        /// stopped rather than left in a phantom active state with nothing
        /// backing it.
        /// </summary>
        public void RestoreRepair(bool active, bool paused, int hullFormForCurrentPackage, float packageProgress01, float sinceLastDamageSeconds)
        {
            State.RestoreSinceLastDamage(sinceLastDamageSeconds);
            Repair.RestoreFromSave(active, paused, hullFormForCurrentPackage, packageProgress01);
            if (!active) return;
            bool reserved = logistics != null && ShipRepairService.RestorePartialReservation(logistics.Cargo, hullFormForCurrentPackage, packageProgress01);
            if (!reserved) Repair.Stop();
        }

        // --- Damage / shutdown -------------------------------------------------------
        public void ApplyDamage(float amount) => ApplyDamage(amount, DamageProfile.Neutral);

        /// <summary>VS-014 §6/ICombatTarget: the same shared damage-profile entry point every other combat target uses.</summary>
        public void ApplyDamage(float amount, DamageProfile profile)
        {
            if (State == null || State.EmergencyShutdown) return;
            Durability durability = State.Durability;
            float shieldBefore = durability.Shield;
            float armorBefore = durability.Armor;
            float hullBefore = durability.Hull;

            State.ApplyDamage(amount, profile);

            // P1-COMBAT-006: report every layer the blow actually reached, in the order
            // the cascade reached them.
            //
            // Damage cascades Shield -> Armor -> Hull inside a single call
            // (Durability.ApplyDamageWithProfile), so one hit can empty the shield,
            // chew through the armour and bite the hull. This used to pick "the first
            // layer that was above zero before the hit" and report that one alone, with
            // only that layer's loss — so the killing blow of a fight announced itself
            // as a shield graze, and the armour and hull gauges did not react at all to
            // damage they had just taken.
            //
            // The event is raised once per layer that really lost something, each with
            // its own loss measured against its own maximum. Nothing about the event's
            // shape changed, so every listener — vitals, audio, impact VFX — becomes
            // truthful without knowing this was fixed; a hit that only touches the
            // shield still raises exactly one event, as before.
            RaiseLayerDamage(ShipDamageLayer.Shield, shieldBefore, durability.Shield, durability.ShieldMax);
            RaiseLayerDamage(ShipDamageLayer.Armor, armorBefore, durability.Armor, durability.ArmorMax);
            RaiseLayerDamage(ShipDamageLayer.Hull, hullBefore, durability.Hull, durability.HullMax);
        }

        private void RaiseLayerDamage(ShipDamageLayer layer, float before, float after, float max)
        {
            float loss = Mathf.Max(0f, before - after);
            if (loss <= 0.0001f) return;
            DamageApplied?.Invoke(layer, loss >= Mathf.Max(1f, max * 0.12f), before > 0.0001f && after <= 0.0001f);
        }

        // --- ICombatTarget (VS-014 §6) -----------------------------------------------
        public string TargetId => "ship";
        public TargetCategory Category => TargetCategory.Ship;
        public Vector3 AbsolutePosition => transform.position + FloatingOrigin.TotalOffset;
        /// <summary>Never a valid raid target while Emergency Shutdown is active (VS-014 §5: "player Emergency Shutdown sırasında hedeflenmez").</summary>
        public bool IsTargetable => State != null && !State.EmergencyShutdown;

        private void TriggerShutdown()
        {
            bool alreadyActive = Shutdown.Active;
            Shutdown.Trigger();
            if (!alreadyActive) EmergencyShutdownStarted?.Invoke();
            InCombat = false;
            if (director != null && director.Pressure.Phase == RaidPhase.Active) director.Pressure.NotePlayerShutdown();
            // A repair job's reservation must never be left orphaned by a shutdown --
            // refund whatever's still reserved-but-unconsumed for the current package.
            if (Repair.Active) CancelRepair();
        }

        private void UpdateShutdown()
        {
            if (Shutdown.Tick(Time.deltaTime))
            {
                ShipEmergencyRecovery recovery = GetComponent<ShipEmergencyRecovery>();
                if (recovery == null)
                {
                    Debug.LogError("[Nebula] Emergency Shutdown expired without a ShipEmergencyRecovery owner; recovery remains pending and will not re-arm.", this);
                    return;
                }
                recovery.PerformRecovery();
                Shutdown.CompleteRecovery();
            }
        }
    }
}
