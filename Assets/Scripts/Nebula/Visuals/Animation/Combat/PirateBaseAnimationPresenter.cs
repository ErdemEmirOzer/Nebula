using UnityEngine;
using Nebula.Combat;

namespace Nebula.Visuals
{
    /// <summary>What a Pirate Base is visibly doing, read from its own runtime.</summary>
    public enum PirateBaseVisual
    {
        Idle,
        Firing,
        Damaged,
        Destroyed
    }

    /// <summary>
    /// Works a Pirate Base's two turrets from the shots it really fires and the
    /// hits it really takes.
    /// <para>
    /// <see cref="PirateBaseRuntime"/> keeps every decision: which turret is
    /// live, what it hits, how much durability is left, when the base falls and
    /// what the player is paid for it. This listens to the events that runtime
    /// already raises and poses turret and yoke nodes. A base restored as
    /// already resolved starts destroyed and plays nothing, so a reload never
    /// re-runs a destruction the player watched hours ago.
    /// </para>
    /// </summary>
    public sealed class PirateBaseAnimationPresenter : MonoBehaviour, IAdoptableParticipant
    {
        private PirateBaseRuntime runtime;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;

        private PoseAnchor portTurret;
        private PoseAnchor portYoke;
        private PoseAnchor starboardTurret;
        private PoseAnchor starboardYoke;

        private Vector3 lastFireDirection;
        private bool hasFireDirection;
        private bool firePort = true;
        private float damageFlashRemaining;
        private bool subscribed;

        /// <summary>What is being shown right now. Test-facing.</summary>
        public PirateBaseVisual CurrentVisual { get; private set; } = PirateBaseVisual.Idle;

        /// <summary>How many of the base's four turret parts the model really has. Test-facing.</summary>
        public int TurretPartCount
        {
            get
            {
                int found = 0;
                if (portTurret.IsValid) found++;
                if (portYoke.IsValid) found++;
                if (starboardTurret.IsValid) found++;
                if (starboardYoke.IsValid) found++;
                return found;
            }
        }

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(PirateBaseRuntime baseRuntime, bool alreadyResolved)
        {
            runtime = baseRuntime;
            Bind();

            // A base the player already destroyed comes back destroyed. It never
            // replays the explosion.
            if (alreadyResolved) CurrentVisual = PirateBaseVisual.Destroyed;

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

        private void Bind()
        {
            portTurret.Reset();
            portYoke.Reset();
            starboardTurret.Reset();
            starboardYoke.Reset();

            Transform visual = FindVisualChild();
            if (visual == null) return;

            CombatMechanicsProfiles.TurretNodes port = CombatMechanicsProfiles.PirateBaseTurret(port: true);
            CombatMechanicsProfiles.TurretNodes starboard = CombatMechanicsProfiles.PirateBaseTurret(port: false);

            portTurret = PoseAnchor.Capture(NebulaVisualResolver.FindAnchor(visual, port.Yaw));
            portYoke = PoseAnchor.Capture(NebulaVisualResolver.FindAnchor(visual, port.Pitch));
            starboardTurret = PoseAnchor.Capture(NebulaVisualResolver.FindAnchor(visual, starboard.Yaw));
            starboardYoke = PoseAnchor.Capture(NebulaVisualResolver.FindAnchor(visual, starboard.Pitch));
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
            if (subscribed || runtime == null) return;
            subscribed = true;

            runtime.TurretFired += OnTurretFired;
            runtime.Hit += OnHit;
            runtime.Destroyed += OnDestroyed;
            DefenseFireSignal.Fired += OnDefenseFired;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;

            if (runtime != null)
            {
                runtime.TurretFired -= OnTurretFired;
                runtime.Hit -= OnHit;
                runtime.Destroyed -= OnDestroyed;
            }
            DefenseFireSignal.Fired -= OnDefenseFired;
        }

        /// <summary>The base's own announcement that a turret has fired. Alternates sides.</summary>
        private void OnTurretFired()
        {
            if (CurrentVisual == PirateBaseVisual.Destroyed) return;
            firePort = !firePort;
            CurrentVisual = PirateBaseVisual.Firing;
        }

        /// <summary>
        /// The same shot again, this time carrying the direction it went in.
        /// The base raises both; this one is what lets the turret point.
        /// </summary>
        private void OnDefenseFired(DefenseFireEvent fired)
        {
            if (fired.Owner != DefenseFireOwner.PirateBase) return;
            if (fired.Direction.sqrMagnitude < 1e-6f) return;

            lastFireDirection = fired.Direction;
            hasFireDirection = true;
        }

        private void OnHit()
        {
            if (CurrentVisual == PirateBaseVisual.Destroyed) return;
            damageFlashRemaining = 0.4f;
            CurrentVisual = PirateBaseVisual.Damaged;
        }

        private void OnDestroyed()
        {
            CurrentVisual = PirateBaseVisual.Destroyed;
            ResetPose();
        }

        public void VisualTick(float deltaSeconds)
        {
            if (CurrentVisual == PirateBaseVisual.Destroyed) { ResetPose(); return; }
            if (!isActiveAndEnabled) return;

            if (damageFlashRemaining > 0f)
            {
                damageFlashRemaining -= deltaSeconds;
                if (damageFlashRemaining <= 0f) CurrentVisual = PirateBaseVisual.Idle;
            }

            if (!hasFireDirection || AnimationAccessibility.MotionOff) { ResetPose(); return; }

            PoseAnchor turret = firePort ? portTurret : starboardTurret;
            PoseAnchor yoke = firePort ? portYoke : starboardYoke;
            if (!turret.IsValid && !yoke.IsValid) return;

            Transform reference = turret.IsValid ? turret.Target : yoke.Target;
            Vector2 angles = CombatMechanicsProfiles.AimAngles(reference,
                transform.position + lastFireDirection.normalized * 1000f, 180f);

            float blend = CombatMechanicsProfiles.SnapsToTargetPose ? 1f : 1f - Mathf.Exp(-4f * deltaSeconds);

            if (turret.IsValid) turret.Apply(Vector3.zero, Quaternion.Euler(0f, Mathf.Lerp(0f, angles.x, blend), 0f));
            if (yoke.IsValid) yoke.Apply(Vector3.zero, Quaternion.Euler(Mathf.Lerp(0f, angles.y, blend), 0f, 0f));
        }

        private void ResetPose()
        {
            portTurret.Reset();
            portYoke.Reset();
            starboardTurret.Reset();
            starboardYoke.Reset();
        }

        public void ResetToBasePose()
        {
            ResetPose();
            hasFireDirection = false;
            damageFlashRemaining = 0f;
        }

        private void OnDisable() => ResetPose();

        private void OnDestroy()
        {
            registration.Release();
            Unsubscribe();
            CombatPresenterAdoption.Remove(this);
            ResetToBasePose();
            runtime = null;
            director = null;
        }
    }
}
