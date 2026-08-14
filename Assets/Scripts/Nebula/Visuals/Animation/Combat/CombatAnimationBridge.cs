using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>
    /// The one place combat events become animation.
    /// <para>
    /// It subscribes to the events the simulation already raises — a weapon
    /// that really fired, damage that was really applied, a projectile the
    /// director really resolved, a defense turret that really shot — and turns
    /// each into a short pooled effect. It raises nothing, resolves nothing and
    /// decides nothing: there is no second event bus, no second damage
    /// resolver and no second projectile authority here.
    /// </para>
    /// <para>
    /// Every source is a per-frame Full-system signal, so a distant system
    /// resolving at 1 Hz produces no animation, and a restored save replays
    /// none: the bridge only ever hears what happens while it is listening.
    /// </para>
    /// </summary>
    public sealed class CombatAnimationBridge : MonoBehaviour, IVisualAnimationParticipant
    {
        /// <summary>How long a muzzle flash or impact stays on screen, in seconds.</summary>
        public const float FlashSeconds = 0.14f;

        /// <summary>How long a destruction reads, in seconds.</summary>
        public const float ExplosionSeconds = 0.6f;

        private struct Flash
        {
            public SupportOperationLease Lease;
            public Vector3 Position;
            public float Remaining;
            public float Scale;
        }

        private readonly List<Flash> flashes = new List<Flash>();

        private NebulaAnimationDirector director;
        private ShipCombat shipCombat;
        private ShipWeaponVisuals weaponVisuals;
        private CombatEncounterDirector encounters;
        private Camera billboardCamera;
        private bool subscribed;

        private AnimationRegistration registration;

        /// <summary>Effects raised since this bridge started. Test-facing; never a gameplay count.</summary>
        public int EffectsRaised { get; private set; }

        /// <summary>Effects on screen right now. Test-facing.</summary>
        public int ActiveEffects => flashes.Count;

        /// <summary>The last damage layer that was really applied to the ship. Test-facing.</summary>
        public ShipDamageLayer? LastImpactLayer { get; private set; }

        /// <summary>The last weapon family that really fired. Test-facing.</summary>
        public WeaponFamily? LastFiredFamily { get; private set; }

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(NebulaAnimationDirector animationDirector, ShipCombat combat,
            CombatEncounterDirector encounterDirector)
        {
            director = animationDirector;
            shipCombat = combat;
            weaponVisuals = combat != null ? combat.GetComponent<ShipWeaponVisuals>() : null;
            encounters = encounterDirector;

            Subscribe();
            if (director != null) registration = director.Register(this);
        }

        /// <summary>
        /// Idempotent by construction: a second call adds no second handler, so
        /// a re-initialized bridge never doubles its effects.
        /// </summary>
        private void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;

            if (shipCombat != null)
            {
                shipCombat.WeaponFired += OnWeaponFired;
                shipCombat.DamageApplied += OnDamageApplied;
            }
            if (encounters != null) encounters.ProjectileResolved += OnProjectileResolved;
            DefenseFireSignal.Fired += OnDefenseFired;
            EscortFireSignal.Fired += OnEscortFired;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;

            if (shipCombat != null)
            {
                shipCombat.WeaponFired -= OnWeaponFired;
                shipCombat.DamageApplied -= OnDamageApplied;
            }
            if (encounters != null) encounters.ProjectileResolved -= OnProjectileResolved;
            DefenseFireSignal.Fired -= OnDefenseFired;
            EscortFireSignal.Fired -= OnEscortFired;
        }

        // --- real events -------------------------------------------------------

        /// <summary>Raised only after a shot has been committed — a denied shot flashes nothing.</summary>
        private void OnWeaponFired(WeaponFamily family)
        {
            LastFiredFamily = family;
            if (shipCombat == null) return;

            // The *visual* muzzle, so the flash sits on the barrel as it kicks.
            // The gameplay muzzle is deliberately a different, unmoving anchor.
            Transform muzzle = weaponVisuals != null ? weaponVisuals.GetVisualMuzzle(family) : null;
            Vector3 at = muzzle != null ? muzzle.position : shipCombat.transform.position;
            Raise(CombatAnimationProfiles.MuzzleVfx(family), at, FlashSeconds, 3f);
        }

        /// <summary>Draws the layer that really absorbed the hit, on the ship itself.</summary>
        private void OnDamageApplied(ShipDamageLayer layer, bool destroyed, bool critical)
        {
            LastImpactLayer = layer;
            if (shipCombat == null) return;

            Raise(CombatAnimationProfiles.ImpactVfx(layer), shipCombat.transform.position,
                destroyed ? ExplosionSeconds : FlashSeconds, destroyed ? 14f : 6f);
        }

        /// <summary>
        /// The director tells us where a projectile really ended and whether it
        /// hit. A miss, an interception and an expiry all clean up the same way.
        /// </summary>
        private void OnProjectileResolved(ProjectileState projectile, Vector3 position, bool hit)
        {
            if (!hit) return; // a miss leaves no impact
            Raise(NebulaCommonVfx.HitSpark, position, FlashSeconds, 5f);
        }

        /// <summary>A planetary turret or a Pirate Base turret that really fired.</summary>
        private void OnDefenseFired(DefenseFireEvent fired)
            => Raise(CombatAnimationProfiles.MuzzleVfx(fired.Family), fired.MuzzlePosition, FlashSeconds, 4f);

        /// <summary>
        /// An Escort shot that really landed. The signal is raised past every
        /// gate the resolver imposes, so a refused shot flashes nothing.
        /// </summary>
        private void OnEscortFired(EscortFireEvent fired)
        {
            LastEscortShotVehicleId = fired.VehicleId;
            EscortShotsShown++;

            // The muzzle anchor the Escort's own model carries, when it is live.
            Vector3 at = fired.Origin;
            if (Nebula.Hangar.VehicleAgent.Registry.TryGetValue(fired.VehicleId, out Nebula.Hangar.VehicleAgent agent)
                && agent != null)
            {
                Transform muzzle = NebulaVisualResolver.FindAnchor(agent.transform, "anchor.projectile.muzzle");
                if (muzzle != null) at = muzzle.position;
            }

            Raise(CombatAnimationProfiles.MuzzleVfx(WeaponFamily.Laser), at, FlashSeconds, 3f);
        }

        /// <summary>The last Escort whose shot was really taken. Test-facing.</summary>
        public string LastEscortShotVehicleId { get; private set; }

        /// <summary>Escort shots drawn this session. Test-facing; never a gameplay count.</summary>
        public int EscortShotsShown { get; private set; }

        // --- pooled, bounded presentation ---------------------------------------

        /// <summary>
        /// Leases one pooled quad. A full pool drops the effect silently: the
        /// shot, the damage and the kill all already happened.
        /// </summary>
        private void Raise(NebulaCommonVfx sprite, Vector3 position, float seconds, float scale)
        {
            if (director == null || director.SupportOperations == null) return;
            if (AnimationAccessibility.MotionOff && seconds > FlashSeconds) seconds = FlashSeconds;

            if (!director.SupportOperations.TryLease(sprite, out SupportOperationLease lease, out Transform quad)) return;
            if (quad == null) { director.SupportOperations.Release(lease); return; }

            // Photosensitivity: the same settings the lighting stack already
            // owns shorten and shrink these. Safe Mode is the stricter of the
            // two and cannot be softened by anything else.
            float intensity = LightingAccessibility.PhotosensitivitySafeMode
                ? LightingAccessibility.PhotosensitivityFlashFactor
                : LightingAccessibility.FlashReduction
                    ? LightingAccessibility.FlashReductionFactor
                    : 1f;

            quad.position = position;
            quad.localScale = Vector3.one * scale * intensity;

            flashes.Add(new Flash
            {
                Lease = lease,
                Position = position,
                Remaining = seconds * intensity,
                Scale = scale * intensity
            });
            EffectsRaised++;
        }

        public void VisualTick(float deltaSeconds)
        {
            if (director == null || director.SupportOperations == null) return;

            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                Flash flash = flashes[i];
                flash.Remaining -= deltaSeconds;

                if (flash.Remaining <= 0f)
                {
                    director.SupportOperations.Release(flash.Lease);
                    flashes.RemoveAt(i);
                    continue;
                }

                if (director.SupportOperations.TryGetQuad(flash.Lease, out Transform quad) && quad != null)
                {
                    // Fades by shrinking rather than by flashing brighter, so
                    // nothing here produces a white strobe.
                    float life = Mathf.Clamp01(flash.Remaining / FlashSeconds);
                    quad.position = flash.Position;
                    quad.localScale = Vector3.one * flash.Scale * Mathf.Lerp(0.6f, 1f, life);

                    if (billboardCamera == null) billboardCamera = Camera.main;
                    if (billboardCamera != null)
                        quad.rotation = Quaternion.LookRotation(quad.position - billboardCamera.transform.position, Vector3.up);
                }

                flashes[i] = flash;
            }
        }

        public void ResetToBasePose() => ReleaseAll();

        private void ReleaseAll()
        {
            if (director?.SupportOperations != null)
                for (int i = 0; i < flashes.Count; i++) director.SupportOperations.Release(flashes[i].Lease);
            flashes.Clear();
        }

        private void OnDestroy()
        {
            registration.Release();
            Unsubscribe();
            ReleaseAll();
            director = null;
            shipCombat = null;
            encounters = null;
        }
    }
}
