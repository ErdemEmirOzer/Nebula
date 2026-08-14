using UnityEngine;
using Nebula.Combat;

namespace Nebula.Visuals
{
    /// <summary>Where a projectile is in its own real life, read for presentation only.</summary>
    public enum ProjectileVisualPhase
    {
        Launch,
        Flight,
        Impact,
        Intercepted,
        Expired
    }

    /// <summary>
    /// The mechanical node names on the shipped combat models, the adapters that
    /// let a legacy-named model be driven without renaming it, and the pure
    /// maths behind gimbal aiming, recoil and wreckage drift.
    /// <para>
    /// Every name here was read off a real asset. Nothing invents a node, and
    /// nothing renames or rewrites a model file.
    /// </para>
    /// </summary>
    public static class CombatMechanicsProfiles
    {
        // --- gimbal ---------------------------------------------------------

        /// <summary>
        /// The swing limit for a family. Laser's is the game's own rule; the
        /// other two have no gameplay gimbal rule, so the same angle is reused
        /// purely as a visual bound — it constrains the model, never the shot.
        /// </summary>
        public static float VisualGimbalLimitDegrees(WeaponFamily family)
            => WeaponCatalog.LaserGimbalDegrees;

        /// <summary>
        /// The yaw and pitch that point a mount at a target, expressed in the
        /// mount's own space and clamped to the limit. Returns zero when there
        /// is nothing to aim at, so an idle weapon rests where it was authored.
        /// </summary>
        public static Vector2 AimAngles(Transform mount, Vector3 targetPosition, float limitDegrees)
        {
            if (mount == null) return Vector2.zero;

            Vector3 local = mount.InverseTransformPoint(targetPosition);
            if (local.sqrMagnitude < 1e-6f) return Vector2.zero;

            float yaw = Mathf.Atan2(local.x, Mathf.Max(0.0001f, local.z)) * Mathf.Rad2Deg;
            float flat = new Vector2(local.x, local.z).magnitude;
            float pitch = -Mathf.Atan2(local.y, Mathf.Max(0.0001f, flat)) * Mathf.Rad2Deg;

            return new Vector2(
                Mathf.Clamp(yaw, -limitDegrees, limitDegrees),
                Mathf.Clamp(pitch, -limitDegrees, limitDegrees));
        }

        /// <summary>How far a barrel slides back on a real shot, in metres. Visual only.</summary>
        public const float TurretRecoilMetres = 0.35f;

        /// <summary>How long a recoil takes to return, in seconds.</summary>
        public const float RecoilRecoverySeconds = 0.28f;

        /// <summary>
        /// The recoil offset at a point in its recovery. One means the instant
        /// of the shot; zero means fully returned to the authored pose.
        /// </summary>
        public static float RecoilCurve01(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f) return 1f;
            if (elapsedSeconds >= RecoilRecoverySeconds) return 0f;
            float t = elapsedSeconds / RecoilRecoverySeconds;
            return 1f - Mathf.SmoothStep(0f, 1f, t);
        }

        // --- player weapon node names ----------------------------------------

        public const string GimbalYaw = "part.gimbal_yaw";
        public const string GimbalPitch = "part.gimbal_pitch";

        /// <summary>The extra mechanism a family's own weapon model carries, or null.</summary>
        public static string ExtraMechanism(WeaponFamily family)
        {
            switch (family)
            {
                case WeaponFamily.Plasma: return "part.cooling_shroud_left";
                case WeaponFamily.Em: return "part.seeker_array_yaw";
                default: return null; // the Laser model carries only its gimbal
            }
        }

        /// <summary>
        /// No ship weapon model carries a recoil node — only
        /// <c>LaserDefenseT2</c> does. A weapon without one gets gimbal and
        /// muzzle VFX and no invented slide.
        /// </summary>
        public static bool HasRecoilNode(WeaponFamily family) => false;

        // --- turret node adapters --------------------------------------------

        /// <summary>
        /// One turret's real node names. Built per model so a legacy-named
        /// asset is driven exactly as it is, without a rename or a GLB edit.
        /// </summary>
        public readonly struct TurretNodes
        {
            public readonly string Yaw;
            public readonly string Pitch;
            public readonly string Recoil;
            public readonly string MuzzleAnchor;

            public TurretNodes(string yaw, string pitch, string recoil, string muzzleAnchor)
            {
                Yaw = yaw;
                Pitch = pitch;
                Recoil = recoil;
                MuzzleAnchor = muzzleAnchor;
            }

            public bool HasYaw => !string.IsNullOrEmpty(Yaw);
            public bool HasPitch => !string.IsNullOrEmpty(Pitch);
            public bool HasRecoil => !string.IsNullOrEmpty(Recoil);
        }

        /// <summary>Planetary Laser T1: standard names, no recoil node.</summary>
        public static readonly TurretNodes LaserDefenseT1 =
            new TurretNodes("part.turret_yaw", "part.barrel_pitch", null, "anchor.vfx.muzzle");

        /// <summary>Planetary Laser T2: the one model in the game with a real recoil node.</summary>
        public static readonly TurretNodes LaserDefenseT2 =
            new TurretNodes("part.turret_yaw", "part.elevation_yoke", "part.laser_lance_recoil", "anchor.vfx.muzzle");

        /// <summary>
        /// Planetary Plasma T1 is authored under the older naming. The names are
        /// real and are used as they are — this is an adapter, not a gap.
        /// </summary>
        public static readonly TurretNodes PlasmaDefenseT1 =
            new TurretNodes("swivel-joint", "turret-head", null, null);

        /// <summary>Planetary Plasma T2: standard names.</summary>
        public static readonly TurretNodes PlasmaDefenseT2 =
            new TurretNodes("part.turret_yaw", "part.turret_pitch", null, "anchor.vfx.muzzle");

        /// <summary>A Pirate Base turret. The base carries one per side.</summary>
        public static TurretNodes PirateBaseTurret(bool port)
            => port
                ? new TurretNodes("part.laser_turret_port", "part.laser_yoke_port", null, "anchor.laser.port")
                : new TurretNodes("part.laser_turret_starboard", "part.laser_yoke_starboard", null, "anchor.laser.starboard");

        /// <summary>Resolves a defense structure's nodes from its real content id.</summary>
        public static TurretNodes DefenseTurret(string structureId, int tier)
        {
            bool plasma = structureId != null && structureId.Contains("plasma");
            if (plasma) return tier >= 2 ? PlasmaDefenseT2 : PlasmaDefenseT1;
            return tier >= 2 ? LaserDefenseT2 : LaserDefenseT1;
        }

        // --- projectile node adapters -----------------------------------------

        /// <summary>One projectile model's real moving parts, standard or legacy.</summary>
        public readonly struct ProjectileNodes
        {
            public readonly string Core;
            public readonly string[] Fins;
            public readonly string TrailAnchor;

            public ProjectileNodes(string core, string[] fins, string trailAnchor)
            {
                Core = core;
                Fins = fins ?? new string[0];
                TrailAnchor = trailAnchor;
            }

            public bool HasCore => !string.IsNullOrEmpty(Core);
            public bool HasFins => Fins.Length > 0;
        }

        /// <summary>
        /// The EM round is authored under the standard convention.
        /// </summary>
        public static readonly ProjectileNodes EmProjectile = new ProjectileNodes(
            "part.field_gimbal",
            new[] { "part.guidance_fin_upper", "part.guidance_fin_lower", "part.guidance_fin_port", "part.guidance_fin_starboard" },
            "anchor.vfx.trail");

        /// <summary>The hostile siege round: breaching jaws and an interception spindle.</summary>
        public static readonly ProjectileNodes SiegeProjectile = new ProjectileNodes(
            "part.interception_spindle",
            new[] { "part.breach_jaw_upper", "part.breach_jaw_lower", "part.breach_jaw_port", "part.breach_jaw_starboard" },
            "anchor.vfx.trail");

        /// <summary>
        /// The plasma round predates the naming convention. Its nodes are real,
        /// so they are addressed by their real names rather than renamed.
        /// </summary>
        public static readonly ProjectileNodes PlasmaProjectile = new ProjectileNodes(
            "plasma-core", new[] { "fins" }, "tail");

        /// <summary>Picks the adapter for a real projectile kind.</summary>
        public static ProjectileNodes NodesFor(ProjectileKind kind)
        {
            switch (kind)
            {
                case ProjectileKind.Em: return EmProjectile;
                case ProjectileKind.EnemySiege: return SiegeProjectile;
                default: return PlasmaProjectile;
            }
        }

        /// <summary>True when this kind's model uses the pre-convention names.</summary>
        public static bool UsesLegacyNames(ProjectileKind kind)
            => kind == ProjectileKind.Plasma || kind == ProjectileKind.EnemyPlasma;

        /// <summary>
        /// Reads where a real projectile is in its own life. Nothing here moves
        /// it, retargets it or shortens its lifetime.
        /// </summary>
        public static ProjectileVisualPhase PhaseOf(float ageSeconds, float remainingLifetime,
            bool resolved, bool hit, bool intercepted)
        {
            if (intercepted) return ProjectileVisualPhase.Intercepted;
            if (resolved) return hit ? ProjectileVisualPhase.Impact : ProjectileVisualPhase.Expired;
            if (ageSeconds < 0.12f) return ProjectileVisualPhase.Launch;
            return ProjectileVisualPhase.Flight;
        }

        /// <summary>A resolved round is done; only a live one keeps spinning.</summary>
        public static bool IsLive(ProjectileVisualPhase phase)
            => phase == ProjectileVisualPhase.Launch || phase == ProjectileVisualPhase.Flight;

        // --- raid ---------------------------------------------------------------

        /// <summary>
        /// What a raid phase is allowed to show. Scheduled deliberately shows
        /// nothing at all: the player must not learn a raid is coming from an
        /// effect the simulation did not tell them about.
        /// </summary>
        public static NebulaCommonVfx? RaidVfx(RaidPhase phase)
        {
            switch (phase)
            {
                case RaidPhase.Warning: return NebulaCommonVfx.WarningPulse;
                default: return null;
            }
        }

        /// <summary>True when this phase leaks nothing and must draw nothing.</summary>
        public static bool RaidIsSilent(RaidPhase phase)
            => RaidVfx(phase) == null;

        /// <summary>
        /// A resolved or cleaned-up raid hands its visuals back. A reloaded save
        /// lands in one of these and therefore replays no warning.
        /// </summary>
        public static bool RaidReleasesVisuals(RaidPhase phase)
            => phase == RaidPhase.Resolved || phase == RaidPhase.None;

        // --- wreckage ------------------------------------------------------------

        /// <summary>How fast a wreck tumbles, in degrees per second. Deliberately slow and heavy.</summary>
        public const float WreckageSpinDegreesPerSecond = 6f;

        /// <summary>
        /// A wreck's tumble axis, derived from its own id so the same wreck
        /// always drifts the same way — across a demote, a reload and a
        /// restart — without a single byte in the save.
        /// </summary>
        public static Vector3 WreckageDriftAxis(string wreckageId)
        {
            if (string.IsNullOrEmpty(wreckageId)) return Vector3.up;

            unchecked
            {
                int hash = 17;
                for (int i = 0; i < wreckageId.Length; i++) hash = hash * 31 + wreckageId[i];

                float x = ((hash & 0xFF) / 255f) * 2f - 1f;
                float y = (((hash >> 8) & 0xFF) / 255f) * 2f - 1f;
                float z = (((hash >> 16) & 0xFF) / 255f) * 2f - 1f;

                Vector3 axis = new Vector3(x, y, z);
                return axis.sqrMagnitude < 1e-4f ? Vector3.up : axis.normalized;
            }
        }

        /// <summary>
        /// The tumble angle for a wreck at a given age. A pure function of id
        /// and age, so nothing accumulates and nothing has to be saved.
        /// </summary>
        public static float WreckageSpinDegrees(string wreckageId, float ageSeconds)
        {
            if (AnimationAccessibility.MotionOff) return 0f;
            return ageSeconds * WreckageSpinDegreesPerSecond * AnimationAccessibility.MotionScale;
        }

        // --- accessibility --------------------------------------------------------

        /// <summary>
        /// With motion off a gimbal still points where it is really pointing —
        /// it simply snaps there instead of swinging.
        /// </summary>
        public static bool SnapsToTargetPose => AnimationAccessibility.SnapStateChanges;

        /// <summary>Recoil and spin are decoration and scale with the motion setting.</summary>
        public static float SecondaryScale(in AnimationQualityProfile profile)
        {
            float scale = AnimationAccessibility.MotionScale;
            return profile.SecondaryMotion ? scale : scale * 0.35f;
        }
    }
}
