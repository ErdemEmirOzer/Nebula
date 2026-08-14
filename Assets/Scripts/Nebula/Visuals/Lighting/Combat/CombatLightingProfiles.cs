using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>
    /// One short-lived combat light, described before it is asked for.
    /// </summary>
    public readonly struct CombatFlash
    {
        public readonly Color Color;
        public readonly float Intensity;
        public readonly float Range;
        public readonly float Duration;
        public readonly TransientLightPriority Priority;

        public CombatFlash(Color color, float intensity, float range, float duration, TransientLightPriority priority)
        {
            Color = color;
            Intensity = intensity;
            Range = range;
            Duration = duration;
            Priority = priority;
        }

        /// <summary>Turns the description into a pool request, folding in accessibility and the quality tier.</summary>
        public TransientLightRequest ToRequest(Vector3 position, LightingQuality quality)
        {
            float qualityScale = quality switch
            {
                LightingQuality.Low => 0.7f,
                LightingQuality.High => 1.1f,
                _ => 1f
            };

            return new TransientLightRequest(
                position,
                Color,
                // Combat light is by definition a flash, so it always goes
                // through the flash path — Photosensitivity Safe Mode and Flash
                // Reduction both bite here.
                LightingAccessibility.ScaleFlashIntensity(Intensity * qualityScale),
                Range,
                LightingAccessibility.ResolveFlashDuration(Duration),
                (int)Priority);
        }
    }

    /// <summary>
    /// The light language of combat: muzzles, impacts, damage layers, enemy
    /// classes, raid phases and dominance.
    /// <para>
    /// Continues the palette the earlier parts established rather than opening
    /// a new one — friendly systems are cold white-cyan, industry is amber,
    /// damage is amber-orange rising to a restrained red, and hostiles are
    /// dirty red. Nothing here flashes white or fills the screen: every value
    /// is short, directional and bounded.
    /// </para>
    /// </summary>
    public static class CombatLightingProfiles
    {
        /// <summary>Nothing in combat may exceed this. The ceiling is what keeps a firefight from becoming a strobe.</summary>
        public const float MaxFlashIntensity = 3.2f;

        /// <summary>Longest a combat flash may last. Past this it stops reading as an impact and starts reading as a lamp.</summary>
        public const float MaxFlashDuration = 0.6f;

        // ---- weapon palettes -------------------------------------------------

        /// <summary>Laser: the existing hot red-orange laser palette.</summary>
        public static readonly Color LaserColor = new Color(1f, 0.42f, 0.32f);

        /// <summary>Plasma: a white-hot core reading through a blue-violet shell.</summary>
        public static readonly Color PlasmaColor = new Color(0.72f, 0.66f, 1f);

        /// <summary>EM: electric blue-violet.</summary>
        public static readonly Color EmColor = new Color(0.55f, 0.62f, 1f);

        public static Color WeaponColor(WeaponFamily family)
        {
            switch (family)
            {
                case WeaponFamily.Laser: return LaserColor;
                case WeaponFamily.Plasma: return PlasmaColor;
                case WeaponFamily.Em: return EmColor;
                default: return LaserColor;
            }
        }

        /// <summary>
        /// The flash at a gun's muzzle. Short and small: it exists to say a
        /// shot left this hardpoint, not to light the neighbourhood.
        /// </summary>
        public static CombatFlash Muzzle(WeaponFamily family, TransientLightPriority priority)
        {
            switch (family)
            {
                case WeaponFamily.Laser:
                    return new CombatFlash(LaserColor, 1.6f, 14f, 0.07f, priority);
                case WeaponFamily.Plasma:
                    return new CombatFlash(PlasmaColor, 1.9f, 18f, 0.12f, priority);
                case WeaponFamily.Em:
                    return new CombatFlash(EmColor, 1.4f, 16f, 0.10f, priority);
                default:
                    return new CombatFlash(LaserColor, 1.5f, 14f, 0.08f, priority);
            }
        }

        /// <summary>The flash where a shot lands. Slightly wider and shorter-lived than its muzzle.</summary>
        public static CombatFlash Impact(WeaponFamily family, TransientLightPriority priority)
        {
            switch (family)
            {
                case WeaponFamily.Laser:
                    return new CombatFlash(LaserColor, 1.8f, 20f, 0.09f, priority);
                case WeaponFamily.Plasma:
                    return new CombatFlash(PlasmaColor, 2.2f, 30f, 0.16f, priority);
                case WeaponFamily.Em:
                    return new CombatFlash(EmColor, 1.7f, 26f, 0.14f, priority);
                default:
                    return new CombatFlash(LaserColor, 1.7f, 20f, 0.09f, priority);
            }
        }

        // ---- damage layers ------------------------------------------------------

        /// <summary>
        /// A hit reads as the layer it landed on, so a player can tell a shield
        /// absorbing a shot from armour taking one without a number on screen.
        /// </summary>
        public static CombatFlash DamageHit(ShipDamageLayer layer, TransientLightPriority priority)
        {
            switch (layer)
            {
                // Shield: cold white-cyan, clearly not the hull's colour.
                case ShipDamageLayer.Shield:
                    return new CombatFlash(new Color(0.62f, 0.9f, 1f), 1.7f, 22f, 0.10f, priority);
                // Armour: hot amber.
                case ShipDamageLayer.Armor:
                    return new CombatFlash(new Color(1f, 0.62f, 0.24f), 1.9f, 24f, 0.12f, priority);
                // Hull: a darker, restrained red — the most serious and the
                // least bright, so it reads as damage rather than fireworks.
                case ShipDamageLayer.Hull:
                    return new CombatFlash(new Color(1f, 0.3f, 0.16f), 1.6f, 26f, 0.16f, priority);
                default:
                    return new CombatFlash(new Color(1f, 0.62f, 0.24f), 1.6f, 22f, 0.12f, priority);
            }
        }

        /// <summary>
        /// A destruction flash: quick attack, controlled decay, no shadow, and
        /// deliberately not white — a white peak is what blows a dark-adapted
        /// eye out and is exactly what the art direction rules out.
        /// </summary>
        public static CombatFlash Explosion(TransientLightPriority priority)
            => new CombatFlash(new Color(1f, 0.55f, 0.22f), 2.6f, 60f, 0.35f, priority);

        // ---- enemy classes --------------------------------------------------------

        /// <summary>
        /// Every enemy reads as hostile first — dirty red-amber — with a
        /// secondary colour that says which of the six roles it is.
        /// </summary>
        public static Color EnemyRoleColor(EnemyRole role)
        {
            switch (role)
            {
                case EnemyRole.Probe: return new Color(0.9f, 0.45f, 0.35f);      // sensor character, still hostile
                case EnemyRole.Fighter: return new Color(1f, 0.34f, 0.24f);      // compact red attack system
                case EnemyRole.Looter: return new Color(0.92f, 0.55f, 0.24f);    // dirty industrial amber
                case EnemyRole.Disruptor: return new Color(0.72f, 0.45f, 1f);    // EM violet
                case EnemyRole.Heavy: return new Color(1f, 0.42f, 0.18f);        // heavy hot orange-red
                case EnemyRole.Siege: return new Color(0.85f, 0.2f, 0.16f);      // deep red siege core
                default: return new Color(1f, 0.34f, 0.24f);
            }
        }

        /// <summary>
        /// Emission for an enemy in a given AI phase. Static values only — a
        /// telegraph reads as a steady brightening, never as a blink, because
        /// a flashing tell is one some players cannot use.
        /// </summary>
        public static (Color color, float strength) Enemy(EnemyRole role, EnemyAiPhase phase, bool disrupted, bool disabled)
        {
            if (disabled) return (ShipLightingProfiles.Emergency, 0.1f);
            if (disrupted) return (EmColor, 0.34f); // held under EM: steady violet status

            Color color = EnemyRoleColor(role);
            switch (phase)
            {
                case EnemyAiPhase.Acquire: return (color, 0.22f);
                case EnemyAiPhase.Approach: return (color, 0.3f);
                case EnemyAiPhase.AttackOrScan: return (color, 0.55f); // attack-ready / telegraphing
                case EnemyAiPhase.Break:
                case EnemyAiPhase.Reposition: return (color, 0.3f);
                case EnemyAiPhase.ReEngage: return (color, 0.45f);
                case EnemyAiPhase.Retreat: return (color, 0.18f);
                case EnemyAiPhase.Escaped: return (color, 0f);
                default: return (color, 0.22f);
            }
        }

        // ---- raid phases -------------------------------------------------------------

        /// <summary>
        /// What a raid phase does to friendly structures. Scheduled shows
        /// nothing at all — a raid the player has not been told about must not
        /// be readable from the lighting.
        /// </summary>
        public static (Color color, float strength) Raid(RaidPhase phase)
        {
            switch (phase)
            {
                case RaidPhase.None:
                case RaidPhase.Scheduled: return (ShipLightingProfiles.Warning, 0f);
                case RaidPhase.Warning: return (ShipLightingProfiles.Warning, 0.3f);
                case RaidPhase.Spawning: return (ShipLightingProfiles.Warning, 0.45f);
                case RaidPhase.Active: return (ShipLightingProfiles.Warning, 0.55f);
                case RaidPhase.RetreatCleanup: return (ShipLightingProfiles.Warning, 0.2f);
                case RaidPhase.Resolved: return (ShipLightingProfiles.Warning, 0f);
                default: return (ShipLightingProfiles.Warning, 0f);
            }
        }

        // ---- dominance ------------------------------------------------------------------

        /// <summary>
        /// System and planet control, as light. Never brighter than the
        /// structures themselves — this is a tint on what is already there, not
        /// a second display.
        /// </summary>
        public static (Color color, float strength) Dominance(DominanceState state)
        {
            switch (state)
            {
                case DominanceState.Unowned: return (StructureLightingProfiles.PoiContactColor, 0f);
                case DominanceState.Developing: return (ShipLightingProfiles.Industrial, 0.18f);
                case DominanceState.Controlled: return (new Color(0.66f, 0.9f, 1f), 0.3f);   // friendly cyan-white
                case DominanceState.Contested: return (ShipLightingProfiles.Industrial, 0.4f); // amber warning
                case DominanceState.Lost: return (new Color(0.8f, 0.28f, 0.22f), 0.16f);      // low red, operations off
                default: return (StructureLightingProfiles.PoiContactColor, 0f);
            }
        }

        /// <summary>True while the player's operations on this body should read as running.</summary>
        public static bool OperationsReadAsRunning(DominanceState state)
            => state == DominanceState.Developing || state == DominanceState.Controlled || state == DominanceState.Contested;

        // ---- clamping ----------------------------------------------------------------------

        /// <summary>Every combat flash must survive this. Nothing reaches the pool unbounded.</summary>
        public static bool IsWithinCombatCeiling(in CombatFlash flash)
            => flash.Intensity > 0f
               && flash.Intensity <= MaxFlashIntensity
               && flash.Duration > 0f
               && flash.Duration <= MaxFlashDuration
               && flash.Range > 0f;
    }
}
