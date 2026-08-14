using UnityEngine;
using Nebula.Combat;
using Nebula.Sectors;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>
    /// Turns real combat events into short pooled lights. A pure observer: it
    /// subscribes to events the combat systems already raise and never decides
    /// what was hit, how hard, or whether a shot was allowed — there is no
    /// second damage, weapon, target or encounter authority here.
    /// <para>
    /// Every light it produces comes from LGT-01's
    /// <see cref="TransientLightPool"/> through the director. It never
    /// instantiates a <see cref="Light"/>, so a firefight cannot push the
    /// scene past the active quality profile's ceiling no matter how many
    /// shots are in the air, and nothing it allocates is ever saved.
    /// </para>
    /// <para>
    /// A refused light is a normal outcome. The pool says no when something
    /// more important is using its last slot, and the shot still fires, still
    /// hits and still does its damage — see <see cref="RefusedFlashes"/>.
    /// </para>
    /// </summary>
    public class CombatLightingBridge : MonoBehaviour
    {
        /// <summary>Beyond this the player is a spectator to someone else's fight, and its lights yield to theirs.</summary>
        public const float NearPlayerDistance = 900f;

        private NebulaLightingDirector director;
        private ShipCombat shipCombat;
        private ShipWeaponVisuals weaponVisuals;
        private Transform shipTransform;
        private IFullSystemEncounterSource encounterSource;
        private CombatEncounterDirector boundEncounter;

        /// <summary>How many flashes were asked for this session. Test-facing.</summary>
        public int RequestedFlashes { get; private set; }

        /// <summary>How many the pool actually granted. The gap is the pool doing its job, not a fault.</summary>
        public int GrantedFlashes { get; private set; }

        public int RefusedFlashes => RequestedFlashes - GrantedFlashes;

        public void Initialize(NebulaLightingDirector lightingDirector, ShipCombat combat,
            ShipWeaponVisuals visuals, IFullSystemEncounterSource encounters)
        {
            director = lightingDirector;
            shipCombat = combat;
            weaponVisuals = visuals;
            shipTransform = combat != null ? combat.transform : null;
            encounterSource = encounters;

            if (shipCombat != null)
            {
                shipCombat.WeaponFired += OnWeaponFired;
                shipCombat.DamageApplied += OnDamageApplied;
            }

            // LGT-05.1: planetary Laser/Plasma and Pirate Base turrets announce
            // their accepted shots through one visual-only signal.
            DefenseFireSignal.Fired += OnDefenseFired;

            RebindEncounter();
        }

        /// <summary>
        /// Rebinds when the Full system changes. Only the live encounter
        /// director is ever subscribed, so a system the player returns to
        /// cannot replay the combat that happened while it was away, and a
        /// distant system ticking at 1 Hz produces no lights at all.
        /// </summary>
        private void LateUpdate()
        {
            CombatEncounterDirector current = encounterSource?.CombatEncounter;
            if (!ReferenceEquals(current, boundEncounter)) RebindEncounter();
        }

        private void RebindEncounter()
        {
            if (boundEncounter != null) boundEncounter.ProjectileResolved -= OnProjectileResolved;

            boundEncounter = encounterSource?.CombatEncounter;

            if (boundEncounter != null) boundEncounter.ProjectileResolved += OnProjectileResolved;
        }

        // ---- event handlers -------------------------------------------------

        private void OnWeaponFired(WeaponFamily family)
        {
            Vector3 position = ResolveMuzzlePosition(family);
            TryFlash(CombatLightingProfiles.Muzzle(family, TransientLightPriority.PlayerWeapon), position);
        }

        private void OnDamageApplied(ShipDamageLayer layer, bool significant, bool critical)
        {
            if (shipTransform == null) return;

            // A hit on the player is the one thing that must never lose its
            // light, so it is requested at the top priority.
            TryFlash(CombatLightingProfiles.DamageHit(layer, TransientLightPriority.CriticalPlayerImpact),
                shipTransform.position);
        }

        /// <summary>
        /// A defensive weapon fired. Produces a muzzle flash only — the matching
        /// impact still arrives through <see cref="OnProjectileResolved"/> for
        /// Plasma, and a hitscan Laser has no separate impact event, so no shot
        /// can ever produce two muzzles or two impacts.
        /// </summary>
        private void OnDefenseFired(DefenseFireEvent shot)
        {
            // A hostile turret shooting at the player outranks the player's own
            // planetary battery, which in turn outranks distant fire.
            TransientLightPriority priority = shot.Owner == DefenseFireOwner.PirateBase
                ? TransientLightPriority.CriticalPlayerImpact
                : TransientLightPriority.PlanetaryDefense;

            TryFlash(CombatLightingProfiles.Muzzle(shot.Family, priority), shot.MuzzlePosition);
        }

        private void OnProjectileResolved(ProjectileState projectile, Vector3 position, bool hit)
        {
            if (!hit || projectile == null) return;

            WeaponFamily family = FamilyOf(projectile.Kind);

            // A hostile round landing near the player outranks the player's own
            // gun; the same round landing across the system does not.
            bool nearPlayer = shipTransform != null
                              && (position - shipTransform.position).sqrMagnitude
                              <= NearPlayerDistance * NearPlayerDistance;

            TransientLightPriority priority =
                projectile.Faction == ProjectileFaction.Hostile && nearPlayer
                    ? TransientLightPriority.CriticalPlayerImpact
                    : nearPlayer
                        ? TransientLightPriority.PlayerWeapon
                        : TransientLightPriority.DistantCombat;

            TryFlash(CombatLightingProfiles.Impact(family, priority), position);
        }

        /// <summary>Maps the projectile kinds the game actually fires onto their weapon palette.</summary>
        public static WeaponFamily FamilyOf(ProjectileKind kind)
        {
            switch (kind)
            {
                case ProjectileKind.Em: return WeaponFamily.Em;
                case ProjectileKind.Plasma:
                case ProjectileKind.EnemyPlasma:
                case ProjectileKind.EnemySiege: return WeaponFamily.Plasma;
                default: return WeaponFamily.Plasma;
            }
        }

        /// <summary>Falls back to the ship itself when a hardpoint has no muzzle anchor, rather than throwing.</summary>
        private Vector3 ResolveMuzzlePosition(WeaponFamily family)
        {
            Transform muzzle = weaponVisuals != null ? weaponVisuals.GetMuzzle(family) : null;
            if (muzzle != null) return muzzle.position;
            return shipTransform != null ? shipTransform.position : Vector3.zero;
        }

        private void TryFlash(in CombatFlash flash, Vector3 position)
        {
            if (director == null) return;

            RequestedFlashes++;
            TransientLightRequest request = flash.ToRequest(position, director.Profile.Quality);
            if (director.TryAcquireTransientLight(request, out _)) GrantedFlashes++;
        }

        private void OnDestroy()
        {
            if (shipCombat != null)
            {
                shipCombat.WeaponFired -= OnWeaponFired;
                shipCombat.DamageApplied -= OnDamageApplied;
            }
            if (boundEncounter != null) boundEncounter.ProjectileResolved -= OnProjectileResolved;
            DefenseFireSignal.Fired -= OnDefenseFired;

            director = null;
            shipCombat = null;
            weaponVisuals = null;
            encounterSource = null;
            boundEncounter = null;
        }
    }
}
