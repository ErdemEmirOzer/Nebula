using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Visuals;

namespace Nebula.Combat
{
    /// <summary>
    /// Visual-only mirror of the existing combat-wreckage ledger. Placement
    /// is deterministic from the stable id and never becomes save/gameplay
    /// authority.
    /// <para>
    /// ANI-04.3 adds the tumble. The ledger still owns which wrecks exist,
    /// what they hold, when they expire and what picking one up is worth; this
    /// only turns the model, from an axis derived from the same stable id, at
    /// an angle recomputed from age rather than accumulated — so a demote, a
    /// reload and a restart all land on exactly the same pose without a single
    /// byte in the save.
    /// </para>
    /// </summary>
    public sealed class CombatWreckagePresenter : MonoBehaviour, IAdoptableParticipant
    {
        private sealed class Mirror
        {
            public GameObject Visual;
            public PoseAnchor Pose;
            public Vector3 Axis;
            public float Age;
        }

        private CombatEncounterDirector director;
        private NebulaAnimationDirector animation;
        private AnimationRegistration registration;

        private readonly Dictionary<string, Mirror> visuals = new Dictionary<string, Mirror>();

        // Reused every frame: the old code allocated a HashSet and a List per
        // Update, which at a hundred wrecks is a hundred collections a second.
        private readonly HashSet<string> liveIds = new HashSet<string>();
        private readonly List<string> stale = new List<string>();

        /// <summary>How many wrecks are being mirrored. Test-facing.</summary>
        public int MirroredCount => visuals.Count;

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(CombatEncounterDirector owner)
        {
            director = owner;
            CombatPresenterAdoption.Enqueue(this);
        }

        public void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(animation, animationDirector)) return;
            registration.Release();
            animation = animationDirector;
            registration = animation.Register(this);
        }

        private void Update()
        {
            if (director == null) return;

            liveIds.Clear();
            foreach (CombatWreckageState wreckage in director.Wreckage)
            {
                if (wreckage == null || wreckage.Expired) continue;
                liveIds.Add(wreckage.WreckageId);
                if (visuals.ContainsKey(wreckage.WreckageId)) continue;

                GameObject visual = NebulaVisualResolver.Instantiate(
                    NebulaVisualCatalog.CombatWreckage, transform,
                    instanceName: $"Combat Wreckage :: {wreckage.WreckageId}");
                if (visual == null) continue;
                ulong hash = ExtractionCatalog.Hash(ExtractionState.UniverseSeed, wreckage.WreckageId);
                float angle = ExtractionCatalog.Unit01(hash) * Mathf.PI * 2f;
                float radius = 450f + ExtractionCatalog.Unit01(ExtractionCatalog.Hash(hash, "radius")) * 350f;
                visual.transform.localPosition = new Vector3(Mathf.Cos(angle), 0.15f, Mathf.Sin(angle)) * radius;
                visual.transform.localRotation = Quaternion.Euler(
                    0f, ExtractionCatalog.Unit01(ExtractionCatalog.Hash(hash, "rotation")) * 360f, 0f);

                visuals.Add(wreckage.WreckageId, new Mirror
                {
                    Visual = visual,
                    // Captured after placement, so the tumble is an offset from
                    // where the ledger put it and can never drag it away.
                    Pose = PoseAnchor.Capture(visual.transform),
                    Axis = CombatMechanicsProfiles.WreckageDriftAxis(wreckage.WreckageId),
                    Age = 0f
                });
            }

            stale.Clear();
            foreach (KeyValuePair<string, Mirror> pair in visuals)
                if (!liveIds.Contains(pair.Key)) stale.Add(pair.Key);

            for (int i = 0; i < stale.Count; i++)
            {
                Mirror mirror = visuals[stale[i]];
                mirror.Pose.Reset();
                NebulaVisualResolver.DestroyInstance(mirror.Visual);
                visuals.Remove(stale[i]);
            }
        }

        /// <summary>
        /// Turns each wreck about its own axis. The angle is a function of age,
        /// never an accumulated rotation, so nothing drifts however long the
        /// session runs.
        /// </summary>
        public void VisualTick(float deltaSeconds)
        {
            foreach (KeyValuePair<string, Mirror> pair in visuals)
            {
                Mirror mirror = pair.Value;
                if (!mirror.Pose.IsValid) continue;

                mirror.Age += deltaSeconds;
                float degrees = CombatMechanicsProfiles.WreckageSpinDegrees(pair.Key, mirror.Age);

                if (degrees == 0f) { mirror.Pose.Reset(); continue; }
                mirror.Pose.Apply(Vector3.zero, Quaternion.AngleAxis(degrees, mirror.Axis));
            }
        }

        public void ResetToBasePose()
        {
            foreach (KeyValuePair<string, Mirror> pair in visuals) pair.Value.Pose.Reset();
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            CombatPresenterAdoption.Remove(this);
            ResetToBasePose();
            visuals.Clear();
            director = null;
            animation = null;
        }
    }
}
