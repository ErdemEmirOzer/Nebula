using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>How hard the machine is running, read from its own real state.</summary>
    public enum MechanicalDrive
    {
        /// <summary>Not built yet, or dismantled. Everything stowed.</summary>
        Stowed,

        /// <summary>Built and idle: deployed, but nothing turning.</summary>
        Ready,

        /// <summary>Really producing, transferring or repairing.</summary>
        Working,

        /// <summary>Deployed but deliberately held — a full store, a queued dock.</summary>
        Holding,

        /// <summary>Damaged or disabled. Safe closed pose.</summary>
        Disabled
    }

    /// <summary>
    /// Drives the mechanisms a structure's model really carries.
    /// <para>
    /// Everything it moves is named in <see cref="EconomyMechanicalProfiles"/>,
    /// which was built from the shipped GLB hierarchies. It writes only to those
    /// transforms: the gameplay root, the collider, every surface anchor, and
    /// the turret nodes ANI-04.3 owns are all off limits. Rates, output,
    /// capacity and timers are read from elsewhere or not at all.
    /// </para>
    /// </summary>
    public sealed class EconomyMechanicalPresenter : MonoBehaviour, IAdoptableParticipant
    {
        private struct Driven
        {
            public PoseAnchor Anchor;
            public MechanicalPart Part;
        }

        private readonly List<Driven> driven = new List<Driven>();

        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private System.Func<MechanicalDrive> readDrive;
        private System.Func<float> readCadence;

        private float workPhase;
        private float deploy01;

        /// <summary>The drive last read from the structure's own state. Test-facing.</summary>
        public MechanicalDrive CurrentDrive { get; private set; } = MechanicalDrive.Stowed;

        /// <summary>How many of this model's real mechanisms were found. Test-facing.</summary>
        public int DrivenPartCount => driven.Count;

        /// <summary>The visual id this presenter was bound to. Test-facing.</summary>
        public string VisualId { get; private set; }

        public bool IsAlive => this != null && gameObject != null;

        /// <summary>
        /// <paramref name="drive"/> and <paramref name="cadence"/> read the
        /// structure's own state. Neither is ever written.
        /// </summary>
        public void Initialize(string visualId, Transform visualRoot,
            System.Func<MechanicalDrive> drive, System.Func<float> cadence)
            => Initialize(visualId, visualRoot, EconomyMechanicalProfiles.For(visualId), drive, cadence);

        /// <summary>
        /// Same contract with an explicit part set, for models whose table lives
        /// elsewhere — ANI-05.2's ship and vehicle mechanisms.
        /// </summary>
        public void Initialize(string visualId, Transform visualRoot, MechanicalPart[] parts,
            System.Func<MechanicalDrive> drive, System.Func<float> cadence)
        {
            VisualId = visualId;
            explicitParts = parts;
            readDrive = drive;
            readCadence = cadence;

            Bind(visualRoot);
            CombatPresenterAdoption.Enqueue(this);
        }

        private MechanicalPart[] explicitParts;

        public void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(director, animationDirector)) return;
            registration.Release();
            director = animationDirector;
            registration = director.Register(this);
        }

        /// <summary>
        /// Captures whichever of this model's listed mechanisms the instance
        /// actually has. A missing part is skipped; a part another presenter
        /// owns is refused outright.
        /// </summary>
        private void Bind(Transform visualRoot)
        {
            for (int i = 0; i < driven.Count; i++) driven[i].Anchor.Reset();
            driven.Clear();
            if (visualRoot == null) return;

            foreach (MechanicalPart part in explicitParts ?? EconomyMechanicalProfiles.For(VisualId))
            {
                // Belt and braces: the table is asserted turret-free by a test,
                // and refused here as well.
                if (EconomyMechanicalProfiles.IsTurretOwned(part.NodeName)) continue;

                Transform node = NebulaVisualResolver.FindAnchor(visualRoot, part.NodeName);
                if (node == null) continue;

                driven.Add(new Driven { Anchor = PoseAnchor.Capture(node), Part = part });
            }
        }

        public void VisualTick(float deltaSeconds)
        {
            if (driven.Count == 0) return;
            if (!isActiveAndEnabled) { ResetToBasePose(); return; }

            CurrentDrive = readDrive != null ? readDrive() : MechanicalDrive.Stowed;

            // Deploy is a position, not a loop: it holds wherever the structure
            // really is, and a load lands straight on the right pose.
            float wantedDeploy = CurrentDrive == MechanicalDrive.Stowed ? 0f : 1f;
            deploy01 = AnimationAccessibility.SnapStateChanges
                ? wantedDeploy
                : Mathf.MoveTowards(deploy01, wantedDeploy, deltaSeconds * 0.8f);

            bool running = CurrentDrive == MechanicalDrive.Working && !AnimationAccessibility.MotionOff;
            float cadence = running && readCadence != null ? readCadence() : 0f;
            if (running) workPhase += deltaSeconds * cadence;

            bool secondaryAllowed = director == null || director.Profile.SecondaryMotion;
            float scale = AnimationAccessibility.MotionScale;

            for (int i = 0; i < driven.Count; i++)
            {
                Driven entry = driven[i];
                if (entry.Part.Secondary && !secondaryAllowed) { entry.Anchor.Reset(); continue; }
                Pose(entry, running, cadence, scale);
            }
        }

        private void Pose(in Driven entry, bool running, float cadence, float scale)
        {
            MechanicalPart part = entry.Part;

            switch (part.Role)
            {
                case MechanicalRole.Rotary:
                    entry.Anchor.Apply(Vector3.zero, running
                        ? Quaternion.AngleAxis(Mathf.Repeat(workPhase * part.Magnitude * scale, 360f), part.Axis)
                        : Quaternion.identity);
                    break;

                case MechanicalRole.Linear:
                    float stroke = running
                        ? (Mathf.Sin(workPhase * Mathf.PI * 2f) * 0.5f + 0.5f) * part.Magnitude * scale
                        : 0f;
                    entry.Anchor.Apply(part.Axis.normalized * stroke, Quaternion.identity);
                    break;

                case MechanicalRole.Sweep:
                    float sweep = running
                        ? Mathf.Sin(workPhase * Mathf.PI) * part.Magnitude * scale
                        : 0f;
                    entry.Anchor.Apply(Vector3.zero, Quaternion.AngleAxis(sweep, part.Axis));
                    break;

                case MechanicalRole.Clamp:
                    // A clamp closes while the machine works and opens when it
                    // is merely ready — it never free-runs.
                    float close = running ? part.Magnitude * scale : 0f;
                    entry.Anchor.Apply(Vector3.zero, Quaternion.AngleAxis(close, part.Axis));
                    break;

                case MechanicalRole.Deploy:
                    entry.Anchor.Apply(Vector3.zero,
                        Quaternion.AngleAxis(part.Magnitude * deploy01, part.Axis));
                    break;
            }
        }

        public void ResetToBasePose()
        {
            for (int i = 0; i < driven.Count; i++) driven[i].Anchor.Reset();
            workPhase = 0f;
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            CombatPresenterAdoption.Remove(this);
            ResetToBasePose();
            driven.Clear();
            readDrive = null;
            readCadence = null;
            director = null;
        }
    }
}
