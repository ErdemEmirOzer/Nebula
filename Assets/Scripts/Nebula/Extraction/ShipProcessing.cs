using UnityEngine;
using Nebula.Ship;

namespace Nebula.Extraction
{
    /// <summary>
    /// The ship's processing bay: one bench, no slots and no clock.
    /// <para>
    /// What used to live here was a bank of <c>ProcessorRuntime</c> slots ticked every
    /// frame from <c>Update</c>, each reserving its input in the hold and completing on
    /// a timer. All of it is gone. A conversion is now a single transaction in
    /// <see cref="ProcessingBenchCommands"/>: the input is physically moved onto
    /// <see cref="Bench"/>, and converting it is instant and atomic. This component
    /// keeps no timer, produces no output and is not required for a conversion to
    /// happen — it holds the bench and offers the in-world panel's keys.
    /// </para>
    /// <para>
    /// G toggles the panel; with it open, [1]/[2] convert one recipe's worth of the raw
    /// resource the ship carries most of, and [ / ] pick which recipe the two keys mean
    /// when a family has more than the two the catalog ships. These keys sit behind
    /// <c>InputGate.Suppressed</c>, which the shell raises for every screen it opens, so
    /// they are the flight-view path only; the Resources page drives the very same
    /// <see cref="ProcessingBenchCommands"/> entry points. There is no second
    /// transaction path and no cancel — with no running batch there is nothing to give
    /// up, only <see cref="ProcessingBenchCommands.TryWithdraw"/>.
    /// </para>
    /// </summary>
    public class ShipProcessing : MonoBehaviour
    {
        public const KeyCode PanelKey = KeyCode.G;
        public const KeyCode MakePartsKey = KeyCode.Alpha1;
        public const KeyCode MakeMaterialKey = KeyCode.Alpha2;
        public const KeyCode PrevSlotKey = KeyCode.LeftBracket;
        public const KeyCode NextSlotKey = KeyCode.RightBracket;

        /// <summary>
        /// The processing bench — the one authority for what is loaded and the only
        /// thing conversions read or write. Created with the component so no caller
        /// ever has to null-check it into existence.
        /// </summary>
        public ProcessingBenchRuntime Bench { get; } = new ProcessingBenchRuntime();

        /// <summary>
        /// The installed resource_processing tier, recorded for display and for the
        /// save's module table. It grants nothing on its own — see
        /// <see cref="ApplyTier"/>.
        /// </summary>
        public int Tier { get; private set; } = 1;

        public bool PanelOpen { get; private set; }
        public int ActiveRawFamily { get; private set; } = -1;

        /// <summary>Which of the active family's two recipes [1]/[2] currently mean. Presentation state; never saved.</summary>
        public int SelectedRecipeIndex { get; private set; } = -1;

        public string Message { get; private set; }

        private ShipLogistics logistics;
        private float messageUntil;

        public string TransientMessage => Time.unscaledTime < messageUntil ? Message : null;
        private LogisticsCargo Cargo => logistics != null ? logistics.Cargo : null;

        private void Awake() => logistics = GetComponent<ShipLogistics>();

        /// <summary>
        /// Tells the router when this panel is listening, so it can arbitrate the two
        /// keys the panel shares with extraction's build mode.
        /// <para>
        /// A predicate rather than a flag written each frame: on the frame the panel
        /// opens or closes, a flag is only right for whoever runs after this component
        /// and Unity promises no such order.
        /// </para>
        /// </summary>
        private void OnEnable() =>
            NebulaInputRouter.SetContextSource(InputContext.Processing, () => PanelOpen);

        private void OnDisable() =>
            NebulaInputRouter.SetContextSource(InputContext.Processing, null);

        /// <summary>
        /// Records the installed resource_processing tier.
        /// <para>
        /// It applies no effect, and that is the honest state of the balance rather
        /// than an oversight. The tier used to buy slots (1/2/3/4/6) and a speed
        /// multiplier (×1 … ×3); with one bench and no duration, neither quantity
        /// exists to multiply. Rather than invent a hidden replacement benefit, the
        /// tier buys nothing until a balance decision says what it should — it must
        /// not silently become a cap: nothing here limits how much may sit on the
        /// bench or how much one conversion may consume. T1 access stays free, exactly
        /// as <c>ModuleCatalog.GrantFreeStartingModules</c> already grants it.
        /// </para>
        /// </summary>
        public void ApplyTier(int tier) => Tier = Mathf.Max(1, tier);

        /// <summary>
        /// The in-world panel. No processing happens here on a tick — the only thing
        /// this reads is input, and the only thing it can start is a transaction that
        /// finishes inside the same call.
        /// </summary>
        /// <summary>
        /// Opens or closes the in-world panel. Public because the key is a shortcut for
        /// the action, not the action: G and any control that offers the bay come
        /// through here, so there is one panel state rather than two.
        /// </summary>
        public void TogglePanel() => PanelOpen = !PanelOpen;

        private void Update()
        {
            if (InputGate.Suppressed || CombatInputContext.WorldInteractionBlocked) return;

            // Every read here is a catalog row through the router, not a KeyCode
            // constant through Input. The constants above are still the single
            // declaration of each shipped default -- the catalog reads them -- but the
            // runtime now asks the row for its *current* key, so a rebind of any of
            // these four is honoured on the frame it is taken instead of being
            // accepted by the settings screen and quietly ignored here.
            if (NebulaInputRouter.Pressed(NebulaInputActions.ProcessingPanel)) TogglePanel();
            if (!PanelOpen) return;

            ActiveRawFamily = MostAbundantRawFamily();

            // Through the router: extraction's build mode reads the same two keys for
            // its defense node and the two sub-modes can be open at once, so one press
            // has to reach exactly one of them. While the panel is open it wins — see
            // NebulaInputActions.Rank.
            if (NebulaInputRouter.Pressed(NebulaInputActions.ProcessingPrevSlot)) StepRecipe(-1);
            if (NebulaInputRouter.Pressed(NebulaInputActions.ProcessingNextSlot)) StepRecipe(1);

            if (NebulaInputRouter.Pressed(NebulaInputActions.ProcessingMakeParts)) ConvertOnce(isMaterial: false);
            else if (NebulaInputRouter.Pressed(NebulaInputActions.ProcessingMakeMaterial)) ConvertOnce(isMaterial: true);
        }

        /// <summary>
        /// Moves one recipe's worth of raw from the hold onto the bench and converts it
        /// in the same press.
        /// <para>
        /// Two commands rather than one, because the bench really is two places: the
        /// hold pays, the bench holds, the hold is paid. If the conversion refuses
        /// after the deposit went through, the deposit is withdrawn again, so a refused
        /// press leaves the hold exactly as it was rather than parking input on a bench
        /// the player never asked to load. The withdrawal is guaranteed room — the hold
        /// gave up that exact volume a line earlier.
        /// </para>
        /// </summary>
        private void ConvertOnce(bool isMaterial)
        {
            LogisticsCargo cargo = Cargo;
            if (ActiveRawFamily < 0 || cargo == null) { SetMessage("KARGODA HAM KAYNAK YOK"); return; }

            int recipeIndex = ResolveRecipeIndex(isMaterial);
            ProcessingRecipe recipe = ProcessingBenchCommands.RecipeAt(recipeIndex);
            if (recipe == null) { SetMessage("TARİF YOK"); return; }

            SelectedRecipeIndex = recipeIndex;

            int needed = recipe.InputCount;
            int onBench = Bench.InputItemId == recipe.InputItemId ? Bench.InputAmount : 0;
            int toDeposit = Mathf.Max(0, needed - onBench);

            if (toDeposit > 0)
            {
                BenchDepositReason deposit =
                    ProcessingBenchCommands.TryDeposit(Bench, cargo, recipe.InputItemId, toDeposit);
                if (deposit != BenchDepositReason.None) { SetMessage(DepositMessage(deposit)); return; }
            }

            BenchConvertReason convert =
                ProcessingBenchCommands.TryConvert(Bench, cargo, recipeIndex, needed, out int produced);
            if (convert != BenchConvertReason.None)
            {
                if (toDeposit > 0) ProcessingBenchCommands.TryWithdraw(Bench, cargo, toDeposit);
                SetMessage(ConvertMessage(convert));
                return;
            }

            SetMessage($"{recipe.DisplayName} ×{produced}");
        }

        /// <summary>
        /// Which recipe [1]/[2] mean: the active family's part or material recipe,
        /// unless [ / ] has picked another of that family's recipes. Derived from the
        /// catalog, so a family that gains a third recipe is reachable without a change
        /// here.
        /// </summary>
        private int ResolveRecipeIndex(bool isMaterial)
        {
            string rawId = ExtractionCatalog.ResourceIds[ActiveRawFamily];
            var indices = ProcessingBenchCommands.RecipeIndicesFor(rawId);
            if (indices.Count == 0) return -1;

            if (SelectedRecipeIndex >= 0 && indices.Contains(SelectedRecipeIndex))
                return SelectedRecipeIndex;

            int wanted = isMaterial ? 1 : 0;
            return indices[Mathf.Clamp(wanted, 0, indices.Count - 1)];
        }

        private void StepRecipe(int direction)
        {
            if (ActiveRawFamily < 0) return;
            var indices = ProcessingBenchCommands.RecipeIndicesFor(
                ExtractionCatalog.ResourceIds[ActiveRawFamily]);
            if (indices.Count == 0) { SelectedRecipeIndex = -1; return; }

            int current = indices.IndexOf(SelectedRecipeIndex);
            if (current < 0) current = 0;
            SelectedRecipeIndex = indices[(current + direction + indices.Count) % indices.Count];
        }

        private int MostAbundantRawFamily()
        {
            LogisticsCargo cargo = Cargo;
            if (cargo == null) return -1;
            int best = -1; float bestAmount = 0f;
            for (int f = 0; f < ExtractionCatalog.ResourceIds.Length; f++)
            {
                float amount = cargo.GetAvailable(ExtractionCatalog.ResourceIds[f]);
                if (amount > bestAmount) { bestAmount = amount; best = f; }
            }
            return best;
        }

        private static string DepositMessage(BenchDepositReason reason) => reason switch
        {
            BenchDepositReason.InsufficientCargo => "YETERLİ HAM KAYNAK YOK",
            BenchDepositReason.DifferentInputAlreadyLoaded => "TEZGÂHTA FARKLI BİR KAYNAK VAR",
            BenchDepositReason.ItemNotProcessable => "BU KAYNAK İŞLENEMEZ",
            BenchDepositReason.NoCargo => "KARGO YOK",
            _ => "TEZGÂHA EKLENEMEDİ"
        };

        private static string ConvertMessage(BenchConvertReason reason) => reason switch
        {
            BenchConvertReason.OutputNoRoom => "KARGODA YER YOK",
            BenchConvertReason.InsufficientBenchInput => "TEZGÂHTA YETERLİ GİRDİ YOK",
            BenchConvertReason.RecipeInputMismatch => "TEZGÂHTA FARKLI BİR KAYNAK VAR",
            BenchConvertReason.AmountNotMultipleOfRecipeInput => "MİKTAR TARİFİN KATI DEĞİL",
            _ => "DÖNÜŞTÜRÜLEMEDİ"
        };

        private void SetMessage(string text) { Message = text; messageUntil = Time.unscaledTime + 3.5f; NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Processing, text); }
    }
}
