using UnityEngine;
using Nebula.Combat;

namespace Nebula.Visuals
{
    /// <summary>
    /// Aims and kicks a planetary defense turret from the shots it really
    /// fires.
    /// <para>
    /// The structure decides everything: what it shoots at, whether it is in
    /// range, whether it has line of sight, how much damage it does, and when
    /// it overheats. This listens to <see cref="DefenseFireSignal"/> — which is
    /// raised only after a shot is committed — and writes to the turret's own
    /// yaw, pitch and, where the model has one, its recoil node. The structure
    /// root, its collider and its aim are never touched, so the whole building
    /// never swings around.
    /// </para>
    /// </summary>
    public sealed class DefenseTurretAnimationPresenter : MonoBehaviour, IAdoptableParticipant
    {
        /// <summary>How close a fire event must be to count as this turret's own shot.</summary>
        private const float OwnShotRadius = 60f;

        private NebulaAnimationDirector director;
        private AnimationRegistration registration;

        private PoseAnchor yaw;
        private PoseAnchor pitch;
        private PoseAnchor recoil;
        private CombatMechanicsProfiles.TurretNodes nodes;

        private Vector3 lastFireDirection;
        private bool hasFireDirection;
        private float recoilElapsed = Mathf.Infinity;
        private bool subscribed;

        private System.Func<float> overheatRemaining;

        /// <summary>The turret's real node names, as resolved for this model. Test-facing.</summary>
        public CombatMechanicsProfiles.TurretNodes Nodes => nodes;

        /// <summary>True when this model really carries a recoil node. Test-facing.</summary>
        public bool HasRecoilNode => recoil.IsValid;

        /// <summary>True when the model exposed a yaw or a pitch part. Test-facing.</summary>
        public bool HasMechanism => yaw.IsValid || pitch.IsValid;

        /// <summary>True while this turret is recovering from a real shot. Test-facing.</summary>
        public bool IsRecoiling => recoilElapsed < CombatMechanicsProfiles.RecoilRecoverySeconds;

        public bool IsAlive => this != null && gameObject != null;

        /// <summary>
        /// <paramref name="overheatSecondsRemaining"/> reads the structure's own
        /// overheat clock; it is never written, only asked.
        /// </summary>
        public void Initialize(string structureId, int tier, System.Func<float> overheatSecondsRemaining)
        {
            nodes = CombatMechanicsProfiles.DefenseTurret(structureId, tier);
            overheatRemaining = overheatSecondsRemaining;

            Bind();
            Subscribe();
            CombatPresenterAdoption.Enqueue(this);
        }

        public void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(director, animationDirector)) return;
            registration.Release();
            director = animationDirector;
            registration = director.Register(this);
        }

        /// <summary>
        /// Captures whichever of this model's parts really exist. A single-mesh
        /// or partly-authored turret contributes only what it has; nothing is
        /// invented and the hull is never shaken as a substitute.
        /// </summary>
        private void Bind()
        {
            yaw.Reset();
            pitch.Reset();
            recoil.Reset();

            Transform visual = FindVisualChild();
            if (visual == null) return;

            if (nodes.HasYaw) yaw = PoseAnchor.Capture(NebulaVisualResolver.FindAnchor(visual, nodes.Yaw));
            if (nodes.HasPitch) pitch = PoseAnchor.Capture(NebulaVisualResolver.FindAnchor(visual, nodes.Pitch));
            if (nodes.HasRecoil) recoil = PoseAnchor.Capture(NebulaVisualResolver.FindAnchor(visual, nodes.Recoil));
        }

        private Transform FindVisualChild()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.GetComponentInChildren<Renderer>(true) != null) return child;
            }
            return null;
        }

        private void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            DefenseFireSignal.Fired += OnDefenseFired;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            DefenseFireSignal.Fired -= OnDefenseFired;
        }

        /// <summary>
        /// Only this structure's own committed shots. A Pirate Base turret and
        /// a neighbouring emplacement both raise the same signal, so the owner
        /// and the muzzle position decide whose shot it was.
        /// </summary>
        private void OnDefenseFired(DefenseFireEvent fired)
        {
            if (fired.Owner != DefenseFireOwner.PlanetaryStructure) return;
            if ((fired.MuzzlePosition - transform.position).sqrMagnitude > OwnShotRadius * OwnShotRadius) return;

            lastFireDirection = fired.Direction;
            hasFireDirection = fired.Direction.sqrMagnitude > 1e-6f;
            recoilElapsed = 0f;
        }

        public void VisualTick(float deltaSeconds)
        {
            if (!isActiveAndEnabled) { ResetToBasePose(); return; }
            if (!yaw.IsValid && !pitch.IsValid) return; // VFX-only fallback: nothing mechanical to drive

            // An overheating turret holds a safe pose rather than tracking.
            bool overheated = overheatRemaining != null && overheatRemaining() > 0f;

            Vector2 angles = Vector2.zero;
            if (hasFireDirection && !overheated)
            {
                Transform reference = yaw.IsValid ? yaw.Target : pitch.Target;
                angles = CombatMechanicsProfiles.AimAngles(reference,
                    transform.position + lastFireDirection.normalized * 1000f, 180f);
            }

            float blend = CombatMechanicsProfiles.SnapsToTargetPose ? 1f : 1f - Mathf.Exp(-4f * deltaSeconds);

            if (yaw.IsValid) yaw.Apply(Vector3.zero, Quaternion.Euler(0f, Mathf.Lerp(0f, angles.x, blend), 0f));
            if (pitch.IsValid) pitch.Apply(Vector3.zero, Quaternion.Euler(Mathf.Lerp(0f, angles.y, blend), 0f, 0f));

            if (recoilElapsed < CombatMechanicsProfiles.RecoilRecoverySeconds)
            {
                recoilElapsed += deltaSeconds;

                if (recoil.IsValid && !AnimationAccessibility.MotionOff)
                {
                    float scale = director != null
                        ? CombatMechanicsProfiles.SecondaryScale(director.Profile)
                        : AnimationAccessibility.MotionScale;
                    float kick = CombatMechanicsProfiles.RecoilCurve01(recoilElapsed)
                                 * CombatMechanicsProfiles.TurretRecoilMetres * scale;
                    recoil.Apply(new Vector3(0f, 0f, -kick), Quaternion.identity);
                }
            }
            else if (recoil.IsValid)
            {
                recoil.Reset();
            }
        }

        public void ResetToBasePose()
        {
            yaw.Reset();
            pitch.Reset();
            recoil.Reset();
            recoilElapsed = Mathf.Infinity;
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            Unsubscribe();
            CombatPresenterAdoption.Remove(this);
            ResetToBasePose();
            overheatRemaining = null;
            director = null;
        }
    }
}
