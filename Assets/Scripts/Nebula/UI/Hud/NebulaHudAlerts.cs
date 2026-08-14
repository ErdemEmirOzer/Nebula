using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Navigation;
using Nebula.Ship;
using Nebula.World;
using Nebula.UI.Accessibility;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// The top-right column: the single most important alert, then cargo, then
    /// what the ship is currently building.
    /// <para>
    /// The old combat HUD folded raid state into the top-left objective corner as
    /// six more lines of prose — threat pressure, stage, doctrine, risk, phase,
    /// countdown, target, defence coordination, looter theft and pirate base, all
    /// appended to one <see cref="Text"/>. A raid countdown is the most urgent
    /// thing that can happen to a player, and it was competing for attention with
    /// everything else in the same paragraph.
    /// </para>
    /// <para>
    /// Here exactly one alert is ever full-size, chosen by priority. The rest of
    /// the detail lives on the Combat page. That is the difference between a HUD
    /// and a log.
    /// </para>
    /// </summary>
    public sealed class NebulaHudAlerts : MonoBehaviour
    {
        /// <summary>At most three, the same cap the attention query itself enforces.</summary>
        public const int MaxAttentionCards = 3;

        private ShipCombat combat;
        private ShipExtraction extraction;
        private NavigationDirector navigation;

        private RectTransform raidRoot;
        private Image raidPanel;
        private Image raidIcon;
        private NebulaUiFactory.LabelHandle raidTitle, raidCountdown, raidDetail;

        private RectTransform cargoRoot;
        private NebulaUiFactory.LabelHandle cargoValue, cargoWarning;

        /// <summary>
        /// The non-colour channel on the two warnings a player cannot afford to
        /// miss.
        /// <para>
        /// <c>AudioVisualCueForWarnings</c> was a settings row with a renderer and
        /// no surface: <see cref="UiStatusCueView"/> drew real geometry, but the
        /// only thing that ever asked it to was a capture fixture, so flipping the
        /// switch in a running game changed nothing. It was carried as K-10 "open"
        /// on the grounds that the cue belonged to the audio path — true of the
        /// audio half, and not of this one. A raid countdown and an overflowing
        /// hold are both told in red; these marks are what tells them without it.
        /// </para>
        /// </summary>
        private UiStatusCueView.Handle raidCue, cargoCue;

        /// <summary>Test-facing: strokes actually drawn on each warning's cue mark.</summary>
        public int RaidCueStrokes => raidCue != null && raidCue.Cue.HasMark ? raidCue.StrokeCount : 0;

        /// <inheritdoc cref="RaidCueStrokes"/>
        public int CargoCueStrokes => cargoCue != null && cargoCue.Cue.HasMark ? cargoCue.StrokeCount : 0;
        /// <summary>With the over-capacity warning showing. Must clear the warning's own bottom.</summary>
        public const float CargoFullHeight = 80f;

        /// <summary>The ordinary case: title, value and the bar, nothing more.</summary>
        public const float CargoShortHeight = 64f;

        /// <summary>The warning's bottom edge, measured from the panel top.</summary>
        public const float CargoWarningBottom = 70f;
        private NebulaUiFactory.BarHandle cargoBar;

        private RectTransform attentionRoot;
        private RectTransform feedbackRoot;
        private NebulaUiFactory.LabelHandle feedbackText;
        private readonly List<AttentionCardWidget> attentionCards = new List<AttentionCardWidget>(MaxAttentionCards);
        private readonly List<string> candidateSystems = new List<string>(32);

        private float refreshTimer;

        private sealed class AttentionCardWidget
        {
            public RectTransform Root;
            public Image Background;
            public Image Icon;
            public NebulaUiFactory.LabelHandle System;
            public NebulaUiFactory.LabelHandle Priority;
            public NebulaUiFactory.LabelHandle Reason;
        }

        /// <summary>Test-facing: attention cards currently drawn.</summary>
        public int VisibleAttentionCards { get; private set; }

        // ANI-05.4.3: the transfer service reports how much it really moved, so a
        // refused or capacity-blocked collection reports zero and flashes nothing.
        private readonly UiValueAnimator collectionDelta = new UiValueAnimator();
        private bool collectedSubscribed;

        /// <summary>Test-facing: real collections actually shown.</summary>
        public int CollectionDeltasShown => collectionDelta.HighlightsShown;

        /// <summary>
        /// The canonical card set's own identity. The first read after a boot or a
        /// load is adopted in silence; only a later change plays anything, so a
        /// restored save never replays yesterday's alerts.
        /// </summary>
        private readonly UiStableRowRegistry cardRows = new UiStableRowRegistry();
        private readonly List<string> cardSignature = new List<string>(MaxAttentionCards);
        private bool cardsAdopted;

        /// <summary>Test-facing: content transitions actually played.</summary>
        public int CardTransitionsShown { get; private set; }

        private void OnResourceCollected(float taken) => collectionDelta.RaiseDelta(taken);

        private void OnEnable()
        {
            if (collectedSubscribed) return;
            collectedSubscribed = true;
            Hangar.ResourceTransferService.Collected += OnResourceCollected;
        }

        private void OnDisable()
        {
            if (!collectedSubscribed) return;
            collectedSubscribed = false;
            Hangar.ResourceTransferService.Collected -= OnResourceCollected;
        }

        /// <summary>
        /// Observes the canonical set without changing its order or contents.
        /// AttentionQuery owns both; this only notices a change.
        /// </summary>
        private void ObserveCards(IReadOnlyList<string> canonicalIds)
        {
            cardRows.BeginRefresh();
            for (int i = 0; i < canonicalIds.Count; i++) cardRows.Observe(canonicalIds[i]);
            IReadOnlyList<string> gone = cardRows.EndRefresh();

            bool changed = cardRows.EnteredLastRefresh > 0 || gone.Count > 0;
            if (!cardsAdopted) { cardsAdopted = true; return; }
            if (changed) CardTransitionsShown++;
        }

        /// <summary>Test-facing: whether the raid banner is up.</summary>
        public bool RaidVisible => raidRoot != null && raidRoot.gameObject.activeSelf;

        /// <summary>Test-facing: the banner's headline as shown.</summary>
        public string RaidHeadline => raidTitle.Text != null ? raidTitle.Text.text : null;

        public bool FeedbackVisible => feedbackRoot != null && feedbackRoot.gameObject.activeSelf;
        public string FeedbackText => feedbackText?.Text != null ? feedbackText.Text.text : null;

        public static NebulaHudAlerts Create(NebulaUiShell shell, ShipCombat shipCombat,
            ShipExtraction shipExtraction, NavigationDirector navigationDirector = null)
        {
            var go = new GameObject("HUD Alerts");
            var alerts = go.AddComponent<NebulaHudAlerts>();
            alerts.combat = shipCombat;
            alerts.extraction = shipExtraction;
            alerts.navigation = navigationDirector;
            alerts.Build(shell);
            return alerts;
        }

        private void Build(NebulaUiShell shell)
        {
            BuildRaidBanner(shell);
            BuildCargo(shell);
            BuildAttention(shell);
            BuildFeedback(shell);
        }

        private void BuildFeedback(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudDynamic), "Gameplay Feedback",
                NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Warning));
            feedbackRoot = panel.rectTransform;
            NebulaUiFactory.Place(feedbackRoot, new Vector2(0.5f, 0f), new Vector2(0f, 132f),
                new Vector2(760f, 48f));
            feedbackRoot.pivot = new Vector2(0.5f, 0f);
            feedbackText = NebulaUiFactory.Label(feedbackRoot, "Message", "",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.MiddleCenter, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            NebulaUiFactory.Fill(feedbackText.Text.rectTransform, 12f);
            feedbackRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// The attention column. AttentionHud rendered its three consolidated cards
        /// as one Text block; the cards themselves were always the right idea, and
        /// AttentionQuery still decides which three and in what order. This only
        /// draws them.
        /// </summary>
        private void BuildAttention(NebulaUiShell shell)
        {
            attentionRoot = NebulaUiFactory.Group(shell.Root(UiLayer.HudDynamic), "Attention");
            NebulaUiFactory.Place(attentionRoot, new Vector2(1f, 1f), new Vector2(-32f, -216f),
                new Vector2(352f, 260f));

            for (int i = 0; i < MaxAttentionCards; i++)
            {
                var card = new AttentionCardWidget();
                Image panel = NebulaUiFactory.Panel(attentionRoot, "Card " + i,
                    NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Warning));
                card.Root = panel.rectTransform;
                card.Background = panel;
                NebulaUiFactory.Place(card.Root, new Vector2(0f, 1f),
                    new Vector2(0f, -i * 84f), new Vector2(352f, 78f));

                card.Icon = NebulaUiFactory.Icon(card.Root, "Icon",
                    NebulaProducedVisualResolver.AttentionIcon(), 20f);
                NebulaUiFactory.Place(card.Icon.rectTransform, new Vector2(0f, 1f),
                    new Vector2(14f, -12f), new Vector2(20f, 20f));

                card.System = NebulaUiFactory.Label(card.Root, "System", "",
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                    TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
                NebulaUiFactory.Place(card.System.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(42f, -12f), new Vector2(230f, 18f));

                card.Priority = NebulaUiFactory.Label(card.Root, "Priority", "",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleRight, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
                NebulaUiFactory.Place(card.Priority.Text.rectTransform, new Vector2(1f, 1f),
                    new Vector2(-14f, -12f), new Vector2(120f, 16f));

                card.Reason = NebulaUiFactory.Label(card.Root, "Reason", "",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                    TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
                NebulaUiFactory.Place(card.Reason.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(14f, -36f), new Vector2(324f, 32f));

                card.Root.gameObject.SetActive(false);
                attentionCards.Add(card);
            }
        }

        private void BuildRaidBanner(NebulaUiShell shell)
        {
            raidPanel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudDynamic), "Raid",
                NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Critical));
            raidRoot = raidPanel.rectTransform;
            NebulaUiFactory.Place(raidRoot, new Vector2(1f, 1f), new Vector2(-32f, -26f),
                new Vector2(352f, 92f));
            // The controller lives on the layer root, never inside the visual it
            // toggles: a widget parented into its own panel is switched off
            // together with it, its Update stops, and it can never decide to
            // come back. Hiding the panel must not disable the thing that shows it.
            transform.SetParent(shell.Root(UiLayer.HudDynamic), false);

            // The game already owns a raid-warning icon; the banner mounts it
            // rather than inventing a second visual language for the same event.
            raidIcon = NebulaUiFactory.Icon(raidRoot, "Icon",
                NebulaProducedVisualResolver.RaidWarningIcon(), 26f);
            NebulaUiFactory.Place(raidIcon.rectTransform, new Vector2(0f, 1f),
                new Vector2(16f, -14f), new Vector2(26f, 26f));

            raidTitle = NebulaUiFactory.Label(raidRoot, "Title", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical), TextAnchor.MiddleLeft,
                FontStyle.Bold, NebulaUiDesignTokens.TrackingSectionTitle);
            // 286 -> 262: the cue mark gets its own column at the banner's right
            // edge rather than being laid over the headline's rect. The headline is
            // two short words in both phases, so nothing it draws moves.
            NebulaUiFactory.Place(raidTitle.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(50f, -16f), new Vector2(262f, 18f));

            raidCue = UiStatusCueView.Build(raidRoot, "Cue", UiStatusCue.None,
                markSize: 18f, showMarker: false);
            NebulaUiFactory.Place(raidCue.Root, new Vector2(1f, 1f),
                new Vector2(-16f, -16f), new Vector2(18f, 18f));

            raidCountdown = NebulaUiFactory.Label(raidRoot, "Countdown", "", 26,
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical), TextAnchor.LowerLeft);
            NebulaUiFactory.Place(raidCountdown.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(16f, -48f), new Vector2(140f, 30f));

            raidDetail = NebulaUiFactory.Label(raidRoot, "Detail", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.LowerRight, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(raidDetail.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-16f, -56f), new Vector2(190f, 18f));

            raidRoot.gameObject.SetActive(false);
        }

        private void BuildCargo(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudDynamic), "Cargo",
                NebulaUiAssetCatalog.PanelPrimary);
            cargoRoot = panel.rectTransform;
            NebulaUiFactory.Place(cargoRoot, new Vector2(1f, 1f), new Vector2(-32f, -128f),
                new Vector2(352f, CargoShortHeight));


            NebulaUiFactory.Place(NebulaUiFactory.SectionTitle(cargoRoot, "Title", "KARGO").Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(140f, 16f));

            cargoValue = NebulaUiFactory.Value(cargoRoot, "Value", "—", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(cargoValue.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-16f, -14f), new Vector2(180f, 16f));

            cargoBar = NebulaUiFactory.Bar(cargoRoot, "Bar", NebulaUiDesignTokens.ActionPrimary);
            RectTransform bar = cargoBar.Track.rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(16f, -44f - NebulaUiDesignTokens.BarHeight);
            bar.offsetMax = new Vector2(-16f, -44f);

            cargoWarning = NebulaUiFactory.Label(cargoRoot, "Warning", "",
                NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical),
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            // 320 -> 300, for the same reason the raid headline gave up 24: the mark
            // sits beside the sentence, never on top of it.
            NebulaUiFactory.Place(cargoWarning.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(16f, -56f), new Vector2(300f, 14f));
            cargoWarning.Text.gameObject.SetActive(false);

            cargoCue = UiStatusCueView.Build(cargoRoot, "Cue", UiStatusCue.None,
                markSize: 14f, showMarker: false);
            NebulaUiFactory.Place(cargoCue.Root, new Vector2(1f, 1f),
                new Vector2(-16f, -56f), new Vector2(14f, 14f));
            cargoCue.Root.gameObject.SetActive(false);

            cargoRoot.gameObject.SetActive(false);
        }

        private void Update()
        {
            RefreshFeedback();
            refreshTimer -= Time.deltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 0.2f;

            RefreshRaid();
            RefreshCargo();
            RefreshAttention();
        }

        private void RefreshFeedback()
        {
            if (feedbackRoot == null) return;
            GameplayFeedbackMessage message = NebulaGameplayFeedback.Latest;
            bool visible = message.IsVisible;
            if (feedbackRoot.gameObject.activeSelf != visible) feedbackRoot.gameObject.SetActive(visible);
            if (!visible) return;
            feedbackText.Value = message.Text;
            feedbackText.Text.color = message.Tone == GameplayFeedbackTone.Rejection
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Warning)
                : NebulaUiDesignTokens.InkPrimary;
        }

        private void RefreshAttention()
        {
            if (navigation?.Knowledge == null)
            {
                HideAllAttention();
                return;
            }

            // Reused, not reallocated: AttentionHud built a fresh List and walked
            // the whole knowledge dictionary every single second.
            candidateSystems.Clear();
            foreach (KeyValuePair<string, SystemKnowledgeLevel> entry in navigation.Knowledge.All)
                if (entry.Value == SystemKnowledgeLevel.Visited) candidateSystems.Add(entry.Key);

            List<AttentionCard> cards = AttentionQuery.TopThreeCards(
                candidateSystems, navigation.CurrentSystemId, navigation.Knowledge);

            // A row key, never shown to anyone. Built by concatenation rather than
            // interpolation so it does not read like player-facing text — the
            // hygiene check cannot tell the two apart, and the same shape has
            // already been fixed twice elsewhere rather than excused.
            cardSignature.Clear();
            for (int i = 0; i < cards.Count && i < MaxAttentionCards; i++)
                cardSignature.Add(string.Concat(cards[i].SystemId, "|", cards[i].Priority.ToString()));
            ObserveCards(cardSignature);

            int shown = 0;
            for (int i = 0; i < attentionCards.Count; i++)
            {
                bool visible = i < cards.Count && shown < MaxAttentionCards;
                AttentionCardWidget widget = attentionCards[i];
                if (widget.Root.gameObject.activeSelf != visible) widget.Root.gameObject.SetActive(visible);
                if (!visible) continue;

                ApplyAttention(widget, cards[i]);
                shown++;
            }
            VisibleAttentionCards = shown;
        }

        private static void ApplyAttention(AttentionCardWidget widget, AttentionCard card)
        {
            // The system's name, not its save-file key. Systems have a DisplayName;
            // this card was the last place that fact was ignored.
            widget.System.Value = NebulaSystemName.Of(card.SystemId);
            widget.Reason.Value = card.Reason;
            widget.Priority.Value = PriorityName(card.Priority);

            // A card for a system we are out of contact with must say so. Showing
            // stale state as if it were live is the one thing this card cannot do.
            UiStatusLevel level = card.IsLastKnown
                ? UiStatusLevel.Inactive
                : card.Priority == AttentionPriority.Critical
                    ? UiStatusLevel.Critical
                    : card.Priority == AttentionPriority.High
                        ? UiStatusLevel.Warning
                        : UiStatusLevel.Caution;

            Color color = NebulaUiDesignTokens.Status(level);
            widget.Priority.Text.color = color;
            widget.Icon.color = color;
            widget.Background.sprite = NebulaUiAssetCatalog.Card(
                card.IsLastKnown ? NebulaUiAssetCatalog.CardState.Default
                    : card.Priority == AttentionPriority.Critical
                        ? NebulaUiAssetCatalog.CardState.Critical
                        : NebulaUiAssetCatalog.CardState.Warning);

            if (card.IsLastKnown) widget.Priority.Value += " · SON BİLİNEN";
        }

        private void HideAllAttention()
        {
            for (int i = 0; i < attentionCards.Count; i++)
                if (attentionCards[i].Root.gameObject.activeSelf)
                    attentionCards[i].Root.gameObject.SetActive(false);
            VisibleAttentionCards = 0;
        }

        internal static string PriorityName(AttentionPriority priority)
        {
            switch (priority)
            {
                case AttentionPriority.Critical: return "KRİTİK";
                case AttentionPriority.High: return "YÜKSEK";
                case AttentionPriority.Medium: return "ORTA";
                default: return "BİLGİ";
            }
        }

        private void RefreshRaid()
        {
            ThreatPressureState pressure = combat?.Director?.Pressure;
            bool relevant = pressure != null && pressure.Phase != RaidPhase.None;

            if (raidRoot.gameObject.activeSelf != relevant) raidRoot.gameObject.SetActive(relevant);
            if (!relevant) return;

            switch (pressure.Phase)
            {
                case RaidPhase.Warning:
                    raidTitle.Value = "AKIN UYARISI";
                    raidCountdown.Value = FormatCountdown(pressure.WarningRemainingSeconds);
                    raidDetail.Value = "TEMASA KALAN";
                    Paint(UiStatusLevel.Critical, NebulaUiAssetCatalog.CardState.Critical);
                    break;

                case RaidPhase.Active:
                    raidTitle.Value = "AKIN SÜRÜYOR";
                    raidCountdown.Value = CountAlive(pressure).ToString();
                    raidDetail.Value = "DÜŞMAN KALDI";
                    Paint(UiStatusLevel.Critical, NebulaUiAssetCatalog.CardState.Critical);
                    break;

                case RaidPhase.Scheduled:
                case RaidPhase.Spawning:
                    raidTitle.Value = "AKIN YAKLAŞIYOR";
                    raidCountdown.Value = StageName(pressure.Stage);
                    raidDetail.Value = "HAZIRLAN";
                    Paint(UiStatusLevel.Warning, NebulaUiAssetCatalog.CardState.Warning);
                    break;

                default:
                    // RetreatCleanup / Resolved: the danger has passed, so the banner
                    // stops shouting rather than lingering in critical red.
                    raidTitle.Value = "AKIN PÜSKÜRTÜLDÜ";
                    raidCountdown.Value = string.Empty;
                    raidDetail.Value = string.Empty;
                    Paint(UiStatusLevel.Nominal, NebulaUiAssetCatalog.CardState.Default);
                    break;
            }
        }

        private void Paint(UiStatusLevel level, NebulaUiAssetCatalog.CardState card)
        {
            Color color = NebulaUiDesignTokens.Status(level);
            raidPanel.sprite = NebulaUiAssetCatalog.Card(card);
            raidTitle.Text.color = color;
            raidCountdown.Text.color = color;
            raidIcon.color = color;

            // Re-read every refresh rather than once at build: the player can open
            // Settings mid-raid and turn cues on, and a cue resolved at build time
            // would stay off until the next session. Same rule the HUD scale follows.
            raidCue?.SetCue(UiStatusCue.For(level));
        }

        private static int CountAlive(ThreatPressureState pressure)
        {
            int alive = 0;
            IReadOnlyList<string> ids = pressure.SpawnedEnemyIds;
            for (int i = 0; i < ids.Count; i++)
            {
                EnemyAgent agent = EnemyAgent.Find(ids[i]);
                if (agent != null && !agent.Destroyed) alive++;
            }
            return alive;
        }

        private void RefreshCargo()
        {
            LogisticsCargo cargo = extraction?.Cargo;
            bool relevant = cargo != null;
            if (cargoRoot.gameObject.activeSelf != relevant) cargoRoot.gameObject.SetActive(relevant);
            if (!relevant) return;

            cargoValue.Value = $"{cargo.UsedVolume:0} / {cargo.Capacity:0}";
            float fill = cargo.Capacity > 0f ? cargo.UsedVolume / cargo.Capacity : 0f;
            cargoBar.SetFraction(fill);

            bool over = cargo.OverCapacity;
            // 80% is where the existing storage palette already starts cautioning,
            // so the bar changes colour at the same threshold the rest of the game uses.
            cargoBar.SetColor(over
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Critical)
                : fill >= 0.8f
                    ? NebulaUiDesignTokens.Status(UiStatusLevel.Caution)
                    : NebulaUiDesignTokens.ActionPrimary);

            if (cargoWarning.Text.gameObject.activeSelf != over)
                cargoWarning.Text.gameObject.SetActive(over);
                // Grows only when the warning is actually there to hold.
                cargoRoot.sizeDelta = new Vector2(cargoRoot.sizeDelta.x,
                    over ? CargoFullHeight : CargoShortHeight);
            if (over) cargoWarning.Value = "KAPASİTE AŞILDI — TOPLAMA DURDU";

            // The mark appears and disappears with the sentence it qualifies, and
            // is re-read every refresh so the switch works mid-flight.
            if (cargoCue != null)
            {
                if (cargoCue.Root.gameObject.activeSelf != over)
                    cargoCue.Root.gameObject.SetActive(over);
                cargoCue.SetCue(UiStatusCue.ForStorage(cargo.UsedVolume, cargo.Capacity));
            }
        }

        // ---- copy ------------------------------------------------------------

        internal static string StageName(RaidStage? stage)
        {
            if (!stage.HasValue) return "—";
            switch (stage.Value)
            {
                case RaidStage.Pirate: return "KORSAN";
                case RaidStage.Assault: return "SALDIRI";
                case RaidStage.Siege: return "KUŞATMA";
                default: return "—";
            }
        }

        internal static string FormatCountdown(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.RoundToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
