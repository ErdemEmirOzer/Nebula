using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>
    /// Swings the player's mounted weapons to follow the target the ship has
    /// really acquired, and kicks them back when a shot really goes off.
    /// <para>
    /// It writes only to <c>part.gimbal_yaw</c> and <c>part.gimbal_pitch</c>
    /// inside each weapon's own visual, plus the weapon visual's own local
    /// offset for recoil. The hardpoint, its heat, its overheat latch, the
    /// target lock and the direction a projectile actually leaves in are all
    /// read and never written — a gimbal that cannot reach the target still
    /// does not stop the shot, because the shot was never this object's to
    /// decide.
    /// </para>
    /// </summary>
    public sealed class WeaponGimbalPresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        private sealed class Mounted
        {
            public WeaponFamily Family;
            public PoseAnchor Yaw;
            public PoseAnchor Pitch;
            public PoseAnchor Body;
            public float RecoilElapsed = Mathf.Infinity;
        }

        private readonly List<Mounted> mounted = new List<Mounted>();

        private ShipCombat combat;
        private ShipWeaponVisuals weaponVisuals;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private int rebuiltForVisualSerial = -1;

        /// <summary>How many mounted weapons are really being posed. Test-facing.</summary>
        public int PosedWeaponCount => mounted.Count;

        /// <summary>True while any weapon is still recovering from a real shot. Test-facing.</summary>
        public bool AnyRecoiling
        {
            get
            {
                for (int i = 0; i < mounted.Count; i++)
                    if (mounted[i].RecoilElapsed < CombatMechanicsProfiles.RecoilRecoverySeconds) return true;
                return false;
            }
        }

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(ShipCombat shipCombat, ShipWeaponVisuals visuals, NebulaAnimationDirector animationDirector)
        {
            combat = shipCombat;
            weaponVisuals = visuals;
            director = animationDirector;

            if (combat != null) combat.WeaponFired += OnWeaponFired;
            if (director != null) registration = director.Register(this);
        }

        /// <summary>
        /// Kicks the family that really fired. A denied shot raises no event and
        /// therefore never produces a kick.
        /// </summary>
        private void OnWeaponFired(WeaponFamily family)
        {
            for (int i = 0; i < mounted.Count; i++)
                if (mounted[i].Family == family) mounted[i].RecoilElapsed = 0f;
        }

        /// <summary>
        /// Finds whichever gimbal parts each mounted weapon model actually
        /// carries. A hardpoint with no visual, or a model without a gimbal,
        /// simply contributes nothing.
        /// </summary>
        private void Rebuild()
        {
            for (int i = 0; i < mounted.Count; i++)
            {
                mounted[i].Yaw.Reset();
                mounted[i].Pitch.Reset();
                mounted[i].Body.Reset();
            }
            mounted.Clear();

            if (weaponVisuals == null) return;

            foreach ((WeaponFamily family, GameObject visual) in weaponVisuals.MountedWeapons)
            {
                if (visual == null) continue;

                Transform yaw = NebulaVisualResolver.FindAnchor(visual.transform, CombatMechanicsProfiles.GimbalYaw);
                Transform pitch = NebulaVisualResolver.FindAnchor(visual.transform, CombatMechanicsProfiles.GimbalPitch);
                if (yaw == null && pitch == null) continue;

                mounted.Add(new Mounted
                {
                    Family = family,
                    Yaw = PoseAnchor.Capture(yaw),
                    Pitch = PoseAnchor.Capture(pitch),
                    Body = PoseAnchor.Capture(visual.transform)
                });
            }
        }

        public void VisualTick(float deltaSeconds)
        {
            if (combat == null || weaponVisuals == null) return;

            // The loadout can change at any time; rebuild when the set of
            // mounted visuals actually differs from what we hold.
            int serial = weaponVisuals.MountedSerial;
            if (serial != rebuiltForVisualSerial)
            {
                Rebuild();
                rebuiltForVisualSerial = serial;
            }
            if (mounted.Count == 0) return;

            EnemyAgent target = combat.Target;
            Vector3 targetPosition = target != null ? target.transform.position : Vector3.zero;
            bool hasTarget = target != null && !target.Destroyed;

            float secondary = director != null
                ? CombatMechanicsProfiles.SecondaryScale(director.Profile)
                : AnimationAccessibility.MotionScale;

            for (int i = 0; i < mounted.Count; i++) Pose(mounted[i], deltaSeconds, hasTarget, targetPosition, secondary);
        }

        private void Pose(Mounted weapon, float deltaSeconds, bool hasTarget, Vector3 targetPosition, float secondary)
        {
            // An overheated hardpoint stops aiming: the safe closed pose.
            bool overheated = IsFamilyOverheated(weapon.Family);
            bool aims = hasTarget && !overheated;

            Vector2 angles = Vector2.zero;
            if (aims)
            {
                Transform reference = weapon.Yaw.IsValid ? weapon.Yaw.Target : weapon.Body.Target;
                angles = CombatMechanicsProfiles.AimAngles(reference, targetPosition,
                    CombatMechanicsProfiles.VisualGimbalLimitDegrees(weapon.Family));
            }

            // With motion off the mount still points where it really points; it
            // just gets there without the swing.
            float blend = CombatMechanicsProfiles.SnapsToTargetPose
                ? 1f
                : 1f - Mathf.Exp(-6f * deltaSeconds);

            if (weapon.Yaw.IsValid)
                weapon.Yaw.Apply(Vector3.zero, Quaternion.Euler(0f, Mathf.Lerp(0f, angles.x, blend), 0f));
            if (weapon.Pitch.IsValid)
                weapon.Pitch.Apply(Vector3.zero, Quaternion.Euler(Mathf.Lerp(0f, angles.y, blend), 0f, 0f));

            // Recoil. No ship weapon model carries a recoil node, so the whole
            // weapon slides on its mount rather than a part being invented.
            if (weapon.RecoilElapsed < CombatMechanicsProfiles.RecoilRecoverySeconds)
            {
                weapon.RecoilElapsed += deltaSeconds;
                if (weapon.Body.IsValid && !AnimationAccessibility.MotionOff)
                {
                    float kick = CombatMechanicsProfiles.RecoilCurve01(weapon.RecoilElapsed)
                                 * CombatAnimationProfiles.RecoilMetres * secondary;
                    weapon.Body.Apply(new Vector3(0f, 0f, -kick), Quaternion.identity);
                }
            }
            else if (weapon.Body.IsValid)
            {
                weapon.Body.Reset();
            }
        }

        private bool IsFamilyOverheated(WeaponFamily family)
        {
            if (combat == null || combat.Hardpoints == null) return false;
            for (int i = 0; i < combat.Hardpoints.Length; i++)
            {
                HardpointState hardpoint = combat.Hardpoints[i];
                if (hardpoint == null || hardpoint.IsEmpty || hardpoint.Weapon != family) continue;
                if (hardpoint.Overheated) return true;
            }
            return false;
        }

        public void ResetToBasePose()
        {
            for (int i = 0; i < mounted.Count; i++)
            {
                mounted[i].Yaw.Reset();
                mounted[i].Pitch.Reset();
                mounted[i].Body.Reset();
                mounted[i].RecoilElapsed = Mathf.Infinity;
            }
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            if (combat != null) combat.WeaponFired -= OnWeaponFired;
            ResetToBasePose();
            mounted.Clear();
            combat = null;
            weaponVisuals = null;
            director = null;
        }
    }
}
