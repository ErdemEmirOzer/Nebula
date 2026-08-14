using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>What the processing module is visibly doing. Derived from the real bench, never decided here.</summary>
    public enum ProcessingAnimationState
    {
        Idle,

        /// <summary>Input is loaded on the bench and nothing has been converted yet.</summary>
        InputAccepted,

        /// <summary>Kept for the presentation vocabulary; unreachable now that a conversion has no duration to be in the middle of.</summary>
        Processing,

        /// <summary>A conversion just committed and the bench still holds input.</summary>
        OutputReady,

        /// <summary>A conversion just committed and emptied the bench.</summary>
        Complete
    }

    /// <summary>
    /// Poses the ship's resource-processing module from the real
    /// <see cref="ProcessingBenchRuntime"/>.
    /// <para>
    /// Reads only: what is on the bench, and the bench's own monotonic
    /// <see cref="ProcessingBenchRuntime.ConversionsTotal"/>. It keeps no timer,
    /// no recipe, no inventory and no output count of its own, so cargo cost and
    /// yield are identical whether this component exists or not.
    /// </para>
    /// <para>
    /// A conversion is instant, so nothing here may gate it. The transaction has
    /// already committed by the time this sees the counter move: the kick and the
    /// leaving proxy are a report of something that finished, never a wind-down the
    /// result waits on. With Motion Off the module jumps straight to its final visual
    /// state and no proxy travels at all.
    /// </para>
    /// <para>
    /// The module's own moving parts would carry this far better, but the
    /// processing module prefab is a single mesh — so what is animated is the
    /// module visual as a whole, at the real anchor, and nothing is invented to
    /// fill the gap.
    /// </para>
    /// </summary>
    public class ProcessingAnimationPresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        /// <summary>The real module anchor this presenter poses. Never guessed — it is in the ship's own anchor list.</summary>
        public const string ModuleAnchorName = "anchor.industry.resource_processing";

        /// <summary>Heavy industrial cadence. Deliberately slow: this is a refinery, not a toy.</summary>
        public const float ProcessingRateHz = 0.8f;

        /// <summary>How far the module travels on its working cycle, in local units.</summary>
        public const float ProcessingAmplitude = 0.02f;

        /// <summary>The short mechanical kick when a batch completes.</summary>
        public const float OutputKickAmplitude = 0.05f;

        /// <summary>How far an output proxy travels as it leaves.</summary>
        public const float OutputExitDistance = 1.1f;

        /// <summary>How long an output proxy stays visible after a batch lands.</summary>
        public const float OutputVisibleSeconds = 0.35f;

        private ShipProcessing processing;
        private PlayerShipVisualController visualController;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private ItemProxyPool itemProxies;

        private PoseAnchor moduleAnchor;
        private float workPhase;
        private float outputKickRemaining;
        private long lastConversionsTotal;

        private ProcessingBenchRuntime Bench => processing != null ? processing.Bench : null;

        private ItemProxyLease inputLease;
        private ItemProxyLease outputLease;
        private string activeInputId;
        private string activeOutputId;

        /// <summary>The recipe input id currently being shown, or null. Test-facing.</summary>
        public string ShownInputId => activeInputId;

        /// <summary>The recipe output id currently being shown, or null. Test-facing.</summary>
        public string ShownOutputId => activeOutputId;

        /// <summary>True while an input proxy is on its way in.</summary>
        public bool HasInputProxy => itemProxies != null && itemProxies.IsHolder(inputLease);

        /// <summary>True while an output proxy is leaving.</summary>
        public bool HasOutputProxy => itemProxies != null && itemProxies.IsHolder(outputLease);

        /// <summary>The state last resolved. Test-facing.</summary>
        public ProcessingAnimationState CurrentState { get; private set; } = ProcessingAnimationState.Idle;

        /// <summary>True once the real module anchor was found. False is a documented no-op, not a failure.</summary>
        public bool HasModuleAnchor => moduleAnchor.IsValid;

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(ShipProcessing shipProcessing, PlayerShipVisualController visuals,
            NebulaAnimationDirector animationDirector)
        {
            processing = shipProcessing;
            visualController = visuals;
            director = animationDirector;
            itemProxies = director != null ? director.ItemProxies : null;

            RebindAnchor();
            if (director != null) registration = director.Register(this);
        }

        /// <summary>
        /// Finds the real processing module anchor. Installing a module rebuilds
        /// the ship's visual, so this is called again whenever that happens.
        /// A ship without the module simply has no anchor, and this presenter
        /// does nothing at all — it never invents a position on the hull.
        /// </summary>
        public void RebindAnchor()
        {
            moduleAnchor.Reset();

            Transform anchor = visualController != null
                ? visualController.FindCurrentAnchor(ModuleAnchorName)
                : null;

            moduleAnchor = PoseAnchor.Capture(anchor);
            workPhase = 0f;
            outputKickRemaining = 0f;
            ReleaseInput();
            ReleaseOutput();
        }

        public void VisualTick(float deltaSeconds)
        {
            CurrentState = ResolveState();

            if (!moduleAnchor.IsValid) return; // documented no-op: no module, no motion

            if (AnimationAccessibility.MotionOff)
            {
                moduleAnchor.Reset();
                return;
            }

            float motion = AnimationAccessibility.MotionScale;
            if (director != null && !director.Profile.SecondaryMotion) motion *= 0f;

            Vector3 offset = Vector3.zero;

            // The slow cadence now runs while material is loaded on the bench, not
            // while a batch is counting down — there is no countdown. It is a loaded
            // intake idling, and it can no longer be mistaken for progress because
            // there is no duration for it to imply and nothing waits on it.
            if (CurrentState == ProcessingAnimationState.InputAccepted && motion > 0f)
            {
                workPhase += deltaSeconds * ProcessingRateHz;
                offset += Vector3.up * (Mathf.Sin(workPhase * Mathf.PI * 2f) * ProcessingAmplitude * motion);
            }

            if (outputKickRemaining > 0f)
            {
                outputKickRemaining = Mathf.Max(0f, outputKickRemaining - deltaSeconds);
                // A short mechanical push as the batch is ejected.
                offset += Vector3.forward * (outputKickRemaining * OutputKickAmplitude * Mathf.Max(motion, 0.35f));
            }

            moduleAnchor.Apply(offset, Quaternion.identity);
            DriveItemProxies(deltaSeconds);
        }

        /// <summary>
        /// Shows the real input sitting at the intake and the real output leaving.
        /// Both are resolved from the recipe's own stable item ids, so a refinery
        /// turning metal into alloy shows metal going in and alloy coming out — never
        /// a stand-in guessed from a slot index.
        /// </summary>
        private void DriveItemProxies(float deltaSeconds)
        {
            if (itemProxies == null) return;

            ProcessingRecipe recipe = ResolveActiveRecipe();

            // Nothing on the bench and no conversion left to report: everything goes back.
            if (recipe == null)
            {
                ReleaseInput();
                if (outputKickRemaining <= 0f) ReleaseOutput();
            }

            Vector3 anchorPosition = moduleAnchor.Target.position;
            Vector3 inward = moduleAnchor.Target.forward;

            // ---- input: approaches the module and is taken in --------------
            if (recipe != null && CurrentState != ProcessingAnimationState.Idle)
            {
                if (!HasInputProxy && activeInputId != recipe.InputItemId) ReleaseInput();

                if (!HasInputProxy && itemProxies.TryLease(recipe.InputItemId, out inputLease, out Transform inputProxy))
                    activeInputId = recipe.InputItemId;

                if (itemProxies.TryGetProxy(inputLease, out Transform live))
                {
                    // Sits at the intake. The old partial approach was the batch's own
                    // window drawn as distance; with no window there is nothing left to
                    // travel over, and pretending otherwise would be a timer in
                    // millimetres.
                    live.position = anchorPosition;
                    live.rotation = moduleAnchor.Target.rotation;
                }
            }

            // ---- output: leaves the module on completion --------------------
            if (outputKickRemaining > 0f && recipe != null)
            {
                if (!HasOutputProxy && itemProxies.TryLease(recipe.OutputItemId, out outputLease, out _))
                    activeOutputId = recipe.OutputItemId;

                if (itemProxies.TryGetProxy(outputLease, out Transform live))
                {
                    float exit = 1f - Mathf.Clamp01(outputKickRemaining / OutputVisibleSeconds);
                    if (AnimationAccessibility.SnapStateChanges) exit = 1f;

                    live.position = anchorPosition + inward * (OutputExitDistance * exit);
                    live.rotation = moduleAnchor.Target.rotation;
                }
            }
            else if (HasOutputProxy)
            {
                ReleaseOutput();
            }
        }

        /// <summary>
        /// The recipe worth showing right now, read straight from the real bench: the
        /// one the last conversion used while its kick is still playing, otherwise the
        /// first recipe the loaded input feeds. Both come from
        /// <see cref="ProcessingCatalog"/> — this keeps no copy of the recipe table.
        /// </summary>
        private ProcessingRecipe ResolveActiveRecipe()
        {
            ProcessingBenchRuntime bench = Bench;
            if (bench == null) return null;

            if (outputKickRemaining > 0f)
            {
                ProcessingRecipe last = ProcessingBenchCommands.RecipeAt(bench.LastRecipeIndex);
                if (last != null) return last;
            }

            if (bench.IsEmpty) return null;
            System.Collections.Generic.List<int> indices =
                ProcessingBenchCommands.RecipeIndicesFor(bench.InputItemId);
            return indices.Count > 0 ? ProcessingCatalog.Recipes[indices[0]] : null;
        }

        private void ReleaseInput()
        {
            itemProxies?.Release(inputLease);
            inputLease = ItemProxyLease.None;
            activeInputId = null;
        }

        private void ReleaseOutput()
        {
            itemProxies?.Release(outputLease);
            outputLease = ItemProxyLease.None;
            activeOutputId = null;
        }

        /// <summary>
        /// Classifies the module from the real bench. A conversion that just committed
        /// is OutputReady while the bench still holds input and Complete when it
        /// emptied; input sitting on the bench with nothing converted is InputAccepted;
        /// an empty bench is Idle.
        /// <para>
        /// A rise in the bench's own conversion total is the only signal a conversion
        /// happened. Reading it rather than timing it is what keeps a load — which
        /// starts a fresh runtime at zero — from replaying a conversion that already
        /// happened, and what makes two conversions between two ticks one kick rather
        /// than two.
        /// </para>
        /// </summary>
        private ProcessingAnimationState ResolveState()
        {
            ProcessingBenchRuntime bench = Bench;
            if (bench == null) return ProcessingAnimationState.Idle;

            long conversions = bench.ConversionsTotal;
            if (conversions > lastConversionsTotal)
            {
                // Motion Off wants the end state, not the travel to it. The kick is
                // skipped outright rather than played at zero amplitude, so no proxy
                // is ever leased for a journey that will not be shown.
                if (!AnimationAccessibility.MotionOff) outputKickRemaining = OutputVisibleSeconds;
                lastConversionsTotal = conversions;
                return bench.IsEmpty
                    ? ProcessingAnimationState.Complete
                    : ProcessingAnimationState.OutputReady;
            }

            if (bench.IsEmpty) return ProcessingAnimationState.Idle;
            return ProcessingAnimationState.InputAccepted;
        }

        /// <summary>
        /// Adopts the bench's real conversion total without playing anything. Called
        /// after a load so a restored session reports what the bench is holding rather
        /// than celebrating every conversion the campaign ever made.
        /// </summary>
        public void AdoptCurrentProgressSilently()
        {
            ProcessingBenchRuntime bench = Bench;
            lastConversionsTotal = bench != null ? bench.ConversionsTotal : 0;
            outputKickRemaining = 0f;
        }

        public void ResetToBasePose()
        {
            workPhase = 0f;
            outputKickRemaining = 0f;
            // A cancel, a module rebuild or a teardown must leave no proxy
            // hanging in the air beside a module that is no longer working.
            ReleaseInput();
            ReleaseOutput();
            moduleAnchor.Reset();
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            ResetToBasePose();
            processing = null;
            visualController = null;
            director = null;
            itemProxies = null;
        }
    }
}
