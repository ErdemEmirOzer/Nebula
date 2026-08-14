using Nebula.Combat;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>What an enemy is visibly doing, derived from its real AI phase and timers.</summary>
    public enum EnemyCombatVisual
    {
        Idle,
        Acquiring,
        Approaching,
        Telegraphing,
        Attacking,
        Scanning,
        Siphoning,
        Breaking,
        Repositioning,
        Retreating,
        Disrupted,
        Escaped,
        Destroyed
    }

    /// <summary>What a ship weapon is visibly doing, derived from its real hardpoint state.</summary>
    public enum WeaponVisualState
    {
        Idle,
        Tracking,
        Charging,
        Firing,
        Sustaining,
        Overheated
    }

    /// <summary>
    /// The real node names authored on the combat models, and the mapping from
    /// real combat state onto poses and the existing VFX sprites.
    /// <para>
    /// Every name below was read off the shipped model files. Nothing here is
    /// invented: a role whose model lacks a mechanism gets a VFX anchor instead
    /// and is listed as an ANI-GAP, never a shaken hull.
    /// </para>
    /// </summary>
    public static class CombatAnimationProfiles
    {
        // --- real node names, per the model audit --------------------------

        public const string GimbalYaw = "part.gimbal_yaw";
        public const string GimbalPitch = "part.gimbal_pitch";
        public const string LaserMuzzle = "anchor.muzzle.laser";
        public const string ProjectileSpawn = "anchor.projectile.spawn";
        public const string EmSeekerArray = "part.seeker_array_yaw";
        public const string EmLockSensor = "anchor.lock_sensor";

        /// <summary>How far a weapon slides back on its mount, in metres. Visual only.</summary>
        public const float RecoilMetres = 0.18f;

        /// <summary>How long the recoil takes to return, in seconds.</summary>
        public const float RecoilRecoverySeconds = 0.22f;

        /// <summary>Ceiling on any enemy's visual bank, in degrees.</summary>
        public const float MaxEnemyBankDegrees = AnimationAccessibility.MaxVisualBankDegrees;

        /// <summary>The muzzle flash for a family, using only sprites that already exist.</summary>
        public static NebulaCommonVfx MuzzleVfx(WeaponFamily family)
        {
            switch (family)
            {
                case WeaponFamily.Laser: return NebulaCommonVfx.HitSpark;
                case WeaponFamily.Plasma: return NebulaCommonVfx.HitSpark;
                case WeaponFamily.Em: return NebulaCommonVfx.ScanPulse;
                default: return NebulaCommonVfx.HitSpark;
            }
        }

        /// <summary>
        /// The impact for a real damage layer. A shield flare, a spark on
        /// armour, and the heaviest reading once the hull is being cut.
        /// </summary>
        public static NebulaCommonVfx ImpactVfx(ShipDamageLayer layer)
        {
            switch (layer)
            {
                case ShipDamageLayer.Shield: return NebulaCommonVfx.ShieldImpact;
                case ShipDamageLayer.Armor: return NebulaCommonVfx.HitSpark;
                case ShipDamageLayer.Hull: return NebulaCommonVfx.WarningPulse;
                default: return NebulaCommonVfx.HitSpark;
            }
        }

        /// <summary>
        /// How hard an impact shoves the visual child, in metres. Only the hull
        /// layer reads as a real blow; a shield absorbs by definition.
        /// </summary>
        public static float ImpactRecoilMetres(ShipDamageLayer layer)
        {
            switch (layer)
            {
                case ShipDamageLayer.Shield: return 0f;
                case ShipDamageLayer.Armor: return 0.08f;
                case ShipDamageLayer.Hull: return 0.25f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Reads the weapon's real hardpoint. Heat, the overheat latch and the
        /// cooldown all belong to <see cref="HardpointState"/> and are only
        /// looked at.
        /// </summary>
        public static WeaponVisualState ForHardpoint(bool equipped, bool overheated, bool hasTarget,
            bool firingNow, float chargeProgress01)
        {
            if (!equipped) return WeaponVisualState.Idle;
            if (overheated) return WeaponVisualState.Overheated;
            if (firingNow) return WeaponVisualState.Firing;
            if (chargeProgress01 > 0f && chargeProgress01 < 1f) return WeaponVisualState.Charging;
            return hasTarget ? WeaponVisualState.Tracking : WeaponVisualState.Idle;
        }

        /// <summary>An overheated weapon holds a safe closed pose: no aim, no recoil.</summary>
        public static bool HoldsClosedPose(WeaponVisualState state)
            => state == WeaponVisualState.Overheated;

        /// <summary>Only a weapon that is really tracking a target may swing its gimbal.</summary>
        public static bool AimsGimbal(WeaponVisualState state)
            => state == WeaponVisualState.Tracking
               || state == WeaponVisualState.Charging
               || state == WeaponVisualState.Firing
               || state == WeaponVisualState.Sustaining;

        // --- the six enemy roles -------------------------------------------

        /// <summary>
        /// Maps the shared decision graph's real phase, plus the two real
        /// timers that outrank it, onto a pose. Nothing here advances a phase,
        /// a telegraph or a disrupt.
        /// </summary>
        public static EnemyCombatVisual ForEnemy(EnemyAiPhase phase, bool destroyed, bool disrupted,
            bool telegraphing, bool scanning, bool siphoning)
        {
            if (destroyed) return EnemyCombatVisual.Destroyed;
            if (disrupted) return EnemyCombatVisual.Disrupted;

            switch (phase)
            {
                case EnemyAiPhase.Acquire: return EnemyCombatVisual.Acquiring;
                case EnemyAiPhase.Approach: return EnemyCombatVisual.Approaching;
                case EnemyAiPhase.AttackOrScan:
                    if (telegraphing) return EnemyCombatVisual.Telegraphing;
                    if (scanning) return EnemyCombatVisual.Scanning;
                    if (siphoning) return EnemyCombatVisual.Siphoning;
                    return EnemyCombatVisual.Attacking;
                case EnemyAiPhase.Break: return EnemyCombatVisual.Breaking;
                case EnemyAiPhase.Reposition: return EnemyCombatVisual.Repositioning;
                case EnemyAiPhase.ReEngage: return EnemyCombatVisual.Approaching;
                case EnemyAiPhase.Retreat: return EnemyCombatVisual.Retreating;
                case EnemyAiPhase.Escaped: return EnemyCombatVisual.Escaped;
                default: return EnemyCombatVisual.Idle;
            }
        }

        /// <summary>
        /// The moving parts each role's model really has. Empty means the model
        /// carries no mechanism for that role and only VFX is used.
        /// </summary>
        public static string[] MechanicalParts(string enemyId)
        {
            if (enemyId == EnemyIds.ScoutProbeMk1) return new[] { "part.scan_iris" };
            if (enemyId == EnemyIds.FighterMk1) return new[] { "part.weapon_pod_left", "part.weapon_pod_right" };
            if (enemyId == EnemyIds.LooterTransportMk1)
                return new[] { "part.cargo_hatch", "part.theft_harpoon_left", "part.theft_harpoon_right" };
            if (enemyId == EnemyIds.EmDisruptorMk1)
                return new[] { "part.resonator_left", "part.resonator_right", "part.em_core" };
            if (enemyId == EnemyIds.HeavyAssaultMk1)
                return new[] { "part.weapon_pod_left", "part.weapon_pod_right", "part.armor_wing_left", "part.armor_wing_right" };
            if (enemyId == EnemyIds.SiegeMk1)
                return new[] { "part.siege_barrel", "part.siege_elevation_cradle" };
            return new string[0];
        }

        /// <summary>The VFX anchor each role really carries for its signature action.</summary>
        public static string ActionVfxAnchor(string enemyId)
        {
            if (enemyId == EnemyIds.ScoutProbeMk1) return "anchor.vfx.scan";
            if (enemyId == EnemyIds.FighterMk1) return "anchor.vfx.muzzle_left";
            if (enemyId == EnemyIds.LooterTransportMk1) return "anchor.vfx.siphon_left";
            if (enemyId == EnemyIds.EmDisruptorMk1) return "anchor.vfx.em_telegraph";
            if (enemyId == EnemyIds.HeavyAssaultMk1) return "anchor.vfx.muzzle_left";
            if (enemyId == EnemyIds.SiegeMk1) return "anchor.vfx.siege_telegraph";
            return "anchor.vfx.status";
        }

        /// <summary>The sprite each role's signature action uses.</summary>
        public static NebulaCommonVfx ActionVfx(string enemyId, EnemyCombatVisual visual)
        {
            switch (visual)
            {
                case EnemyCombatVisual.Scanning: return NebulaCommonVfx.ScanPulse;
                case EnemyCombatVisual.Telegraphing: return NebulaCommonVfx.WarningPulse;
                case EnemyCombatVisual.Disrupted: return NebulaCommonVfx.WarningPulse;
                case EnemyCombatVisual.Siphoning: return NebulaCommonVfx.BuildHologram;
                case EnemyCombatVisual.Attacking: return NebulaCommonVfx.HitSpark;
                default: return NebulaCommonVfx.HitSpark;
            }
        }

        /// <summary>Whether this pose draws an effect at all.</summary>
        public static bool DrawsVfx(EnemyCombatVisual visual)
            => visual == EnemyCombatVisual.Scanning
               || visual == EnemyCombatVisual.Telegraphing
               || visual == EnemyCombatVisual.Disrupted
               || visual == EnemyCombatVisual.Siphoning
               || visual == EnemyCombatVisual.Attacking;

        /// <summary>
        /// How agile a role looks. A Fighter throws itself around; a Heavy and a
        /// Siege carrier do not. Purely a visual amplitude — the real speed and
        /// turn rate stay in <see cref="EnemyDef"/>.
        /// </summary>
        public static float BankScale(string enemyId)
        {
            if (enemyId == EnemyIds.FighterMk1) return 1f;
            if (enemyId == EnemyIds.ScoutProbeMk1) return 0.7f;
            if (enemyId == EnemyIds.EmDisruptorMk1) return 0.5f;
            if (enemyId == EnemyIds.LooterTransportMk1) return 0.35f;
            if (enemyId == EnemyIds.HeavyAssaultMk1) return 0.3f;
            if (enemyId == EnemyIds.SiegeMk1) return 0.2f;
            return 0.5f;
        }

        /// <summary>A destroyed or escaped enemy holds still; so does one under a disrupt.</summary>
        public static bool IsClosedPose(EnemyCombatVisual visual)
            => visual == EnemyCombatVisual.Destroyed
               || visual == EnemyCombatVisual.Escaped
               || visual == EnemyCombatVisual.Disrupted;

        // --- quality and accessibility --------------------------------------

        /// <summary>
        /// Low keeps the reads that carry information — muzzle, impact,
        /// destruction — and drops the decorative secondary motion.
        /// </summary>
        public static bool SecondaryMotionAllowed(in AnimationQualityProfile profile)
            => profile.SecondaryMotion;

        /// <summary>
        /// Reduced motion damps recoil and spin, but a telegraph must stay
        /// readable: an incoming siege shot is information, not decoration.
        /// </summary>
        public static float MotionScaleFor(EnemyCombatVisual visual)
        {
            if (visual == EnemyCombatVisual.Telegraphing) return 1f;
            return AnimationAccessibility.MotionScale;
        }
    }
}
