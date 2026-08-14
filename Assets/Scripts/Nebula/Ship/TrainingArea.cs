using UnityEngine;
using Nebula.Combat;
using Nebula.Visuals;

namespace Nebula.Ship
{
    /// <summary>
    /// Placeholder holographic training area (VS-013 §7) near the campaign's
    /// Safety Marker (the start system's star/recommended-start position —
    /// also this slice's final recovery fallback, <see cref="RecoveryPointKind.SafetyMarker"/>).
    /// Owns three practice dummies (A/B/C) and drives each
    /// <see cref="TrainingRun"/>'s step progression from real, if
    /// intentionally simplified, in-scene detection (sustained fire on the
    /// dummy, an actual Boost activation, actual shield regen) rather than
    /// a fully scripted cutscene — every public method here is directly
    /// callable by tests too, with no input simulation required. Never
    /// grants XP/resources/salvage/a permanent free weapon; skippable and
    /// replayable at any time; never blocks any module/progression purchase.
    /// </summary>
    public class TrainingArea : MonoBehaviour
    {
        private ShipController ship;
        private ShipCombat combat;

        private GameObject dummyA, dummyB, dummyC;
        private float dummyAOscillation;
        private float sustainedFireSeconds;
        private bool sawBoostThisStep;

        public static TrainingArea Create(Vector3 position, ShipController ship)
        {
            var go = new GameObject("Training Area");
            go.transform.position = position;
            var area = go.AddComponent<TrainingArea>();
            area.ship = ship;
            area.combat = ship != null ? ship.GetComponent<ShipCombat>() : null;
            area.BuildDummies();
            return area;
        }

        private void BuildDummies()
        {
            dummyA = BuildDummy(transform, "Training Dummy A", transform.position + new Vector3(400f, 0f, 0f), new Color(0.4f, 0.9f, 1f));
            dummyB = BuildDummy(transform, "Training Dummy B", transform.position + new Vector3(-400f, 0f, 0f), new Color(1f, 0.6f, 0.2f));
            dummyC = BuildDummy(transform, "Training Dummy C", transform.position + new Vector3(0f, 0f, 400f), new Color(0.7f, 0.4f, 1f));
        }

        /// <summary>
        /// One dummy, parented to the area that owns it.
        /// <para>
        /// The three dummies used to be scene roots of their own, so ending a
        /// campaign destroyed the Training Area and left its dummies floating in an
        /// empty universe — three per session, every session. The area's own
        /// GameObject is session-tracked, so adopting them is all the ownership they
        /// need; the world position is written after the parenting, which leaves
        /// their placement, their collider and the floating-origin shift they ride
        /// exactly as they were.
        /// </para>
        /// </summary>
        private static GameObject BuildDummy(Transform owner, string name, Vector3 position, Color color)
        {
            var go = new GameObject(name);
            go.name = name;
            go.transform.SetParent(owner, false);
            go.transform.position = position;
            go.AddComponent<BoxCollider>().size = Vector3.one * 20f;

            if (NebulaVisualResolver.Instantiate(NebulaVisualCatalog.TrainingDummy, go.transform,
                    instanceName: "Authored Training Dummy") != null)
                return go;

            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.name = "Training Dummy Visual (Fallback)";
            fallback.transform.SetParent(go.transform, false);
            fallback.transform.localScale = Vector3.one * 20f;
            Collider fallbackCollider = fallback.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(fallbackCollider); else DestroyImmediate(fallbackCollider);
            fallback.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.CreateAdditive(color);
            return go;
        }

        /// <summary>Physical distance within which the player is considered "at the training site" (VS-013.1 §3) -- covers the ±400 dummy spread plus a margin. Drives CombatInputContext.TrainingActive, which blocks world-interaction shortcuts (evacuation/logistics/POI/etc) exactly like Combat does, so a physical key press at the training site can never also fire a world-interaction action.</summary>
        private const float EngagementRadius = 800f;

        private void Update()
        {
            float dt = Time.deltaTime;
            TrainingProgress progress = CombatCampaignState.Training;
            progress.A.Tick(dt); progress.B.Tick(dt); progress.C.Tick(dt);

            if (dummyA != null)
            {
                dummyAOscillation += dt;
                dummyA.transform.position += new Vector3(Mathf.Sin(dummyAOscillation) * 30f * dt, 0f, 0f);
            }

            bool engaged = ship != null && Vector3.Distance(ship.transform.position, transform.position) <= EngagementRadius;
            CombatInputContext.TrainingActive = engaged;
        }

        // --- Training A: targeting, 6-DOF, moving target, Laser, heat, energy, boost, shield regen ---
        public bool TickTrainingA(bool firingLaserAtDummy, bool combatBoostUsed)
        {
            TrainingRun run = CombatCampaignState.Training.A;
            if (!run.CanAct || run.Completed) return false;

            switch (run.CurrentStep)
            {
                case 0: // target selection
                    if (combat != null && combat.Target != null) { run.CompleteStep(); return true; }
                    return false;
                case 1: // sustained Laser fire on the moving dummy (covers retikül/6-DOF/moving-target/heat/energy)
                    sustainedFireSeconds = firingLaserAtDummy ? sustainedFireSeconds + Time.deltaTime : 0f;
                    if (sustainedFireSeconds >= 3f) { run.CompleteStep(); sustainedFireSeconds = 0f; return true; }
                    return false;
                case 2: // Combat Boost
                    if (combatBoostUsed) { run.CompleteStep(); return true; }
                    return false;
                case 3: case 4: case 5: // shield regen / wrap-up steps
                    run.CompleteStep();
                    return true;
            }
            return false;
        }

        // --- Training B: Weapon Production T1 + produced/equipped Plasma ---
        public bool TickTrainingB(bool plasmaFiredAtDummy)
        {
            TrainingRun run = CombatCampaignState.Training.B;
            if (!run.CanAct || run.Completed) return false;
            if (plasmaFiredAtDummy) { run.CompleteStep(); return true; }
            return false;
        }

        // --- Training C: Weapon Production T2 + produced/equipped EM ---
        public bool TickTrainingC(bool emLockAchieved)
        {
            TrainingRun run = CombatCampaignState.Training.C;
            if (!run.CanAct || run.Completed) return false;
            if (emLockAchieved) { run.CompleteStep(); return true; }
            return false;
        }

        public void ReportFailure(TrainingId id) => CombatCampaignState.Training.Get(id)?.FailStep();
        public void Skip(TrainingId id) => CombatCampaignState.Training.Get(id)?.Skip();
        public void Replay(TrainingId id) => CombatCampaignState.Training.Get(id)?.Reset();
    }
}
