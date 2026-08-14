using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// The module tree: what is installed, what is next, and what is standing in
    /// the way.
    /// <para>
    /// ProgressionScreen listed modules as rows of text and put the reason a
    /// module could not be upgraded somewhere else entirely. The rule this page
    /// is built on is that a locked module states its own gate on its own row —
    /// level, milestone, points or materials — because "why can't I take this"
    /// is the only question the screen exists to answer.
    /// </para>
    /// <para>
    /// Read-only. Starting an upgrade still goes through
    /// <see cref="ModuleUpgradeService"/>, which owns every rule; this page only
    /// asks it what it would say.
    /// </para>
    /// </summary>
    public sealed class NebulaProgressionPage : INebulaMenuPage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Progression;
        public string Title => "Gelişim";

        private readonly NebulaContext context;

        private NebulaUiFactory.LabelHandle levelValue, pointsValue;
        private NebulaUiFactory.BarHandle xpBar;
        private NebulaUiFactory.LabelHandle jobName, jobEta;
        private NebulaUiFactory.BarHandle jobBar;
        private RectTransform moduleList;

        private readonly Dictionary<string, ModuleRow> rows = new Dictionary<string, ModuleRow>();

        private readonly NebulaConfirmationDialog confirmation;
        private NebulaUiFactory.ButtonHandle upgradeButton;

        /// <summary>The module the action bar acts on, or <c>null</c> when none is chosen.</summary>
        public string SelectedModuleId { get; private set; }

        /// <summary>Test-facing: why the upgrade button is unavailable, or <c>null</c>.</summary>
        public string UpgradeBlockedReason { get; private set; }

        /// <summary>Modules that could be upgraded right now, and an upgrade needing attention.</summary>
        public int AttentionCount { get; private set; }

        /// <summary>Test-facing: module rows built.</summary>
        public int RowCount => rows.Count;

        /// <summary>
        /// Module rows keyed by module id. The catalog is fixed, so in practice no
        /// row ever enters after the first refresh -- which is the point: the
        /// registry states that as an observed fact rather than an assumption, and
        /// would report the entrance immediately if the list ever became dynamic.
        /// </summary>
        private readonly UiStableRowRegistry moduleRows = new UiStableRowRegistry();
        private bool rowsAdopted;

        /// <summary>Test-facing: modules that genuinely entered on the last refresh.</summary>
        public int ModuleRowsEnteredLastRefresh => rowsAdopted ? moduleRows.EnteredLastRefresh : 0;

        private sealed class ModuleRow
        {
            public RectTransform Root;
            public Image Background;
            public NebulaUiFactory.LabelHandle Name;
            public NebulaUiFactory.LabelHandle Tier;
            public NebulaUiFactory.LabelHandle Status;
        }

        public NebulaProgressionPage(NebulaContext sessionContext,
            NebulaConfirmationDialog confirmationDialog = null)
        {
            context = sessionContext;
            confirmation = confirmationDialog;
        }

        public void Build(RectTransform host)
        {
            var column = NebulaUiFactory.Group(host, "Progression Column");
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(32f, 24f);
            column.offsetMax = new Vector2(-32f, -24f);

            BuildLevelCard(column);
            BuildJobCard(column);
            BuildModuleList(column);
            BuildActionBar(column);
        }

        /// <summary>
        /// The one primary action on this page. It is disabled with the validator's
        /// own reason rather than hidden, so the player always learns why.
        /// </summary>
        private void BuildActionBar(RectTransform parent)
        {
            upgradeButton = NebulaUiFactory.ButtonControl(parent, "Upgrade", "YÜKSELT",
                NebulaUiAssetCatalog.ButtonKind.Primary, OnUpgradeClicked);
            NebulaUiFactory.Place(upgradeButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(300f, 44f));
            upgradeButton.SetAvailable(false, "BİR MODÜL SEÇİN");
        }

        /// <summary>Picks the module the action bar acts on.</summary>
        public void Select(string moduleId)
        {
            SelectedModuleId = moduleId;
            Refresh();
        }

        private void OnUpgradeClicked()
        {
            if (SelectedModuleId == null) return;

            ModuleDef module = ModuleCatalog.Get(SelectedModuleId);
            ModuleUpgradeService upgrades = context?.Modules?.Upgrades;
            if (module == null || upgrades == null) return;

            int tier = context.Modules.Modules.GetTier(module.Id);
            int nextTier = tier + 1;
            if (nextTier - 1 >= module.TierCosts.Length) return;

            ModuleTierCost cost = module.TierCosts[nextTier - 1];
            string costLine = DescribeCost(cost);

            // An upgrade spends points and reserves materials and cannot be undone
            // for free, so it asks. A cheap, repeatable action would not.
            void Start() => upgrades.TryStartUpgrade(module.Id,
                ModuleMilestones.IsSatisfied(module.Id));

            if (confirmation == null) { Start(); Refresh(); return; }

            confirmation.Ask(
                $"{NebulaModuleName.Of(module)} · T{tier} → T{nextTier}",
                "Bu yükseltme gelişim puanı ve malzeme harcar. Devam edilsin mi?",
                costLine, "YÜKSELT", () => { Start(); Refresh(); });
        }

        /// <summary>Points and materials as icon-adjacent amounts, never a sentence.</summary>
        internal static string DescribeCost(ModuleTierCost cost)
        {
            var text = new System.Text.StringBuilder(96);
            text.Append($"{cost.DevelopmentPoints} GELİŞİM PUANI");
            if (cost.Resources != null)
            {
                for (int i = 0; i < cost.Resources.Length; i++)
                {
                    ModuleResourceCost resource = cost.Resources[i];
                    text.Append("  +  ")
                        .Append($"{resource.Amount:0} ")
                        .Append(NebulaText.Upper(Extraction.ProcessingCatalog.ItemDisplayName(resource.ItemId)));
                }
            }
            return text.ToString();
        }

        private void BuildLevelCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Level", 0f, 100f, "SEVİYE");

            levelValue = NebulaUiFactory.Label(card, "Level", "—", NebulaUiDesignTokens.SizeValue,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(levelValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -42f), new Vector2(420f, 22f));

            pointsValue = NebulaUiFactory.Value(card, "Points", "—", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(pointsValue.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -42f), new Vector2(260f, 20f));

            xpBar = NebulaUiFactory.Bar(card, "Xp", NebulaUiDesignTokens.ActionPrimary);
            StretchBar(xpBar, -72f);
        }

        private void BuildJobCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Job", -112f, 96f, "AKTİF YÜKSELTME");

            jobName = NebulaUiFactory.Label(card, "Name", "", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(jobName.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -42f), new Vector2(440f, 20f));

            jobEta = NebulaUiFactory.Value(card, "Eta", "", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(jobEta.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -42f), new Vector2(220f, 20f));

            jobBar = NebulaUiFactory.Bar(card, "Bar", NebulaUiDesignTokens.Status(UiStatusLevel.Working));
            StretchBar(jobBar, -70f);
        }

        private void BuildModuleList(RectTransform parent)
        {
            RectTransform card = Card(parent, "Modules", -220f, 596f, "MODÜLLER");

            moduleList = NebulaUiFactory.Group(card, "List");
            moduleList.anchorMin = new Vector2(0f, 1f);
            moduleList.anchorMax = new Vector2(1f, 1f);
            moduleList.pivot = new Vector2(0.5f, 1f);
            moduleList.offsetMin = new Vector2(14f, -584f);
            moduleList.offsetMax = new Vector2(-14f, -44f);

            // Every module in the catalog gets a row up front. The set is fixed, so
            // building once and only repainting keeps a refresh allocation-free.
            ModuleDef[] modules = ModuleCatalog.Modules;
            for (int i = 0; i < modules.Length; i++) CreateRow(modules[i], i);
        }

        private static RectTransform Card(RectTransform parent, string name, float y, float height, string title)
        {
            Image panel = NebulaUiFactory.Panel(parent, name, NebulaUiAssetCatalog.PanelPrimary);
            RectTransform card = panel.rectTransform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, y);
            card.sizeDelta = new Vector2(0f, height);

            NebulaUiFactory.Place(NebulaUiFactory.SectionTitle(card, "Title", title).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(340f, 18f));
            return card;
        }

        private static void StretchBar(NebulaUiFactory.BarHandle bar, float y)
        {
            RectTransform rect = bar.Track.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(20f, y - NebulaUiDesignTokens.BarHeight);
            rect.offsetMax = new Vector2(-20f, y);
        }

        private void CreateRow(ModuleDef module, int index)
        {
            var row = new ModuleRow();
            Image panel = NebulaUiFactory.Panel(moduleList, module.Id,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            row.Root = panel.rectTransform;
            row.Background = panel;

            row.Root.anchorMin = new Vector2(0f, 1f);
            row.Root.anchorMax = new Vector2(1f, 1f);
            row.Root.pivot = new Vector2(0.5f, 1f);
            row.Root.anchoredPosition = new Vector2(0f, -index * (NebulaUiDesignTokens.RowHeight + 4f));
            row.Root.sizeDelta = new Vector2(0f, NebulaUiDesignTokens.RowHeight);

            row.Name = NebulaUiFactory.Label(row.Root, "Name", NebulaModuleName.Of(module),
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(row.Name.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(300f, 18f));

            row.Tier = NebulaUiFactory.Label(row.Root, "Tier", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Tier.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(324f, 0f), new Vector2(80f, 16f));

            row.Status = NebulaUiFactory.Label(row.Root, "Status", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleRight, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Status.Text.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-16f, 0f), new Vector2(420f, 16f));

            // The whole row is the hit target. A label-only target on a 40px row
            // reads as broken, and the minimum hit size is 32px.
            //
            // The row's existing panel is the target graphic. A second Image cannot
            // be added to a GameObject that already has one -- Graphic requires a
            // CanvasRenderer, which is single-instance -- so AddComponent returns
            // null and the next line throws. That is exactly what happened here.
            panel.raycastTarget = true;
            var button = row.Root.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            // Transition.None meant a module row answered a hover, a press and a
            // keyboard focus with nothing at all, and these rows are the only way
            // to pick the module the YÜKSELT button acts on.
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = NebulaUiAssetCatalog.RowInteraction();
            string captured = module.Id;
            button.onClick.AddListener(() => Select(captured));

            rows[module.Id] = row;
        }

        // ---- refresh ---------------------------------------------------------

        public void Refresh()
        {
            AttentionCount = 0;
            RefreshLevel();
            RefreshJob();
            RefreshModules();
            RefreshAction();
        }

        private void RefreshAction()
        {
            if (upgradeButton == null) return;

            if (SelectedModuleId == null)
            {
                UpgradeBlockedReason = "BİR MODÜL SEÇİN";
                upgradeButton.SetAvailable(false, UpgradeBlockedReason);
                return;
            }

            ModuleDef module = ModuleCatalog.Get(SelectedModuleId);
            ModuleProgressionState modules = context?.Modules?.Modules;
            ModuleUpgradeService upgrades = context?.Modules?.Upgrades;

            if (module == null || modules == null || upgrades == null)
            {
                UpgradeBlockedReason = "OTURUM YOK";
                upgradeButton.SetAvailable(false, UpgradeBlockedReason);
                return;
            }

            int tier = modules.GetTier(module.Id);
            UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(
                module, tier, tier + 1, ProgressionService.State.Level,
                ModuleMilestones.IsSatisfied(module.Id),
                upgrades.ActiveJob != null,
                ProgressionService.State.DevelopmentPoints,
                context?.Ship != null
                    ? context.Ship.GetComponent<Extraction.ShipExtraction>()?.Cargo
                    : null);

            upgradeButton.SetCaption($"{NebulaModuleName.Of(module)} · T{tier + 1}");
            UpgradeBlockedReason = reason == UpgradeRejectReason.None
                ? null : RejectText(reason, module);
            upgradeButton.SetAvailable(reason == UpgradeRejectReason.None, UpgradeBlockedReason);
        }

        private void RefreshLevel()
        {
            ProgressionState state = ProgressionService.State;
            if (state.IsMaxLevel)
            {
                levelValue.Value = $"SEVİYE {state.Level} · MAKSİMUM";
                xpBar.SetFraction(1f);
            }
            else
            {
                long into = ProgressionMath.XpIntoLevel(state.TotalXp);
                long need = ProgressionMath.XpForLevel(state.Level);
                levelValue.Value = $"SEVİYE {state.Level}";
                xpBar.SetFraction(need > 0 ? (float)into / need : 0f);
            }

            pointsValue.Value = $"{state.DevelopmentPoints} GELİŞİM PUANI";
        }

        private void RefreshJob()
        {
            ModuleUpgradeJob job = context?.Modules?.Upgrades?.ActiveJob;
            if (job == null)
            {
                jobName.Value = "YÜKSELTME YOK";
                jobName.Text.color = NebulaUiDesignTokens.InkMuted;
                jobEta.Value = string.Empty;
                jobBar.SetFraction(0f);
                return;
            }

            ModuleDef module = ModuleCatalog.Get(job.ModuleId);
            jobName.Text.color = NebulaUiDesignTokens.InkPrimary;
            jobName.Value = module != null
                ? $"{NebulaModuleName.Of(module)} · T{job.FromTier} → T{job.ToTier}"
                : $"T{job.FromTier} → T{job.ToTier}";

            // A paused job is the one thing on this page that needs the player:
            // it is waiting on safety, and it will not finish on its own.
            if (job.Paused)
            {
                jobEta.Value = "DURAKLATILDI";
                jobEta.Text.color = NebulaUiDesignTokens.Status(UiStatusLevel.Warning);
                AttentionCount++;
            }
            else
            {
                jobEta.Value = FormatDuration(job.RemainingSeconds);
                jobEta.Text.color = NebulaUiDesignTokens.InkPrimary;
            }

            jobBar.SetFraction(job.TotalSeconds > 0f
                ? 1f - Mathf.Clamp01(job.RemainingSeconds / job.TotalSeconds)
                : 0f);
        }

        private void RefreshModules()
        {
            ModuleProgressionState modules = context?.Modules?.Modules;
            ModuleUpgradeService upgrades = context?.Modules?.Upgrades;
            ProgressionState progression = ProgressionService.State;

            ModuleDef[] all = ModuleCatalog.Modules;
            moduleRows.BeginRefresh();
            for (int i = 0; i < all.Length; i++)
            {
                ModuleDef module = all[i];
                if (!rows.TryGetValue(module.Id, out ModuleRow row)) continue;

                moduleRows.Observe(module.Id);

                int tier = modules?.GetTier(module.Id) ?? 0;
                row.Tier.Value = tier > 0 ? $"T{tier}" : "—";

                ApplyStatus(row, module, tier, progression, upgrades);
            }
            moduleRows.EndRefresh();

            // The first pass adopts what is already there in silence, exactly as
            // the fleet page does; celebrating a boot is not an entrance.
            rowsAdopted = true;
        }

        private void ApplyStatus(ModuleRow row, ModuleDef module, int tier,
            ProgressionState progression, ModuleUpgradeService upgrades)
        {
            // Not part of this slice at all. Saying so is kinder than showing a
            // module that looks purchasable and silently never is.
            if (!module.FunctionalThisSlice)
            {
                Paint(row, module.LockedMessage ?? "BU SÜRÜMDE YOK",
                    UiStatusLevel.Inactive, NebulaUiAssetCatalog.RowState.Disabled);
                return;
            }

            if (tier >= module.MaxTierThisSlice)
            {
                Paint(row, "AZAMİ SEVİYEDE", UiStatusLevel.Nominal, NebulaUiAssetCatalog.RowState.Default);
                return;
            }

            int nextTier = tier + 1;
            int requiredLevel = module.RequiredLevelForTier(nextTier);

            if (progression.Level < requiredLevel)
            {
                Paint(row, $"SEVİYE {requiredLevel} GEREKLİ",
                    UiStatusLevel.Inactive, NebulaUiAssetCatalog.RowState.Disabled);
                return;
            }

            if (upgrades?.ActiveJob != null)
            {
                Paint(row, "KUYRUK MEŞGUL", UiStatusLevel.Inactive, NebulaUiAssetCatalog.RowState.Disabled);
                return;
            }

            // The validator, not TryStartUpgrade. They share the same rules, but
            // TryStartUpgrade spends points and reserves cargo -- calling it from a
            // refresh would make merely looking at this page buy things. Re-deriving
            // affordability here instead would be a second authority, and two
            // authorities drift.
            UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(
                module, tier, nextTier, progression.Level,
                ModuleMilestones.IsSatisfied(module.Id),
                upgrades?.ActiveJob != null,
                progression.DevelopmentPoints,
                context?.Ship != null
                    ? context.Ship.GetComponent<Extraction.ShipExtraction>()?.Cargo
                    : null);

            if (reason == UpgradeRejectReason.None)
            {
                // P1-ECO-015: a tier that costs points and materials and grants nothing
                // has to say so before it is bought, not in a design document. It is
                // still purchasable and still selectable — this is disclosure, not a
                // gate — but it no longer competes for the player's attention as
                // something worth doing.
                if (nextTier > module.MaxEffectiveTierThisSlice)
                {
                    Paint(row, $"T{nextTier}'E YÜKSELTİLEBİLİR — ETKİSİZ, DENGE KARARI BEKLİYOR",
                        UiStatusLevel.Caution, NebulaUiAssetCatalog.RowState.Default);
                    return;
                }

                Paint(row, $"T{nextTier}'E YÜKSELTİLEBİLİR",
                    UiStatusLevel.Working, NebulaUiAssetCatalog.RowState.Selected);
                AttentionCount++;
                return;
            }

            Paint(row, RejectText(reason, module), UiStatusLevel.Caution,
                NebulaUiAssetCatalog.RowState.Warning);
        }

        private static void Paint(ModuleRow row, string status, UiStatusLevel level,
            NebulaUiAssetCatalog.RowState state)
        {
            row.Status.Value = status;
            // StatusInk, not Status: this string is the answer to "why can't I
            // take this", and at Inactive the mark colour measured 3.1:1 on the
            // disabled row -- fine for a dot, unreadable as an eleven-pixel
            // sentence. Nine of the twelve module rows are Inactive on a fresh
            // save, so that was most of the page.
            row.Status.Text.color = NebulaUiDesignTokens.StatusInk(level);
            row.Background.sprite = NebulaUiAssetCatalog.Row(state);
        }

        /// <summary>
        /// The service's refusal, in the player's words. Every reason is covered
        /// so a new one can never surface as a bare enum name.
        /// </summary>
        internal static string RejectText(UpgradeRejectReason reason, ModuleDef module)
        {
            switch (reason)
            {
                case UpgradeRejectReason.None: return string.Empty;
                case UpgradeRejectReason.InsufficientPoints: return "GELİŞİM PUANI YETERSİZ";
                case UpgradeRejectReason.InsufficientResources: return "MALZEME YETERSİZ";
                case UpgradeRejectReason.LevelTooLow: return "SEVİYE YETERSİZ";
                case UpgradeRejectReason.MilestoneNotMet:
                    return string.IsNullOrEmpty(module?.MilestoneLabel)
                        ? "KİLOMETRE TAŞI TAMAMLANMADI"
                        : $"GEREKLİ: {module.MilestoneLabel}";
                case UpgradeRejectReason.QueueBusy: return "KUYRUK MEŞGUL";
                case UpgradeRejectReason.AlreadyMaxTier: return "AZAMİ SEVİYEDE";
                case UpgradeRejectReason.NotImplemented: return "BU SÜRÜMDE YOK";
                case UpgradeRejectReason.TierSkip: return "SEVİYE ATLANAMAZ";
                case UpgradeRejectReason.ScreenPaused: return "EKRAN DURAKLATILDI";
                case UpgradeRejectReason.UnknownModule: return "BİLİNMEYEN MODÜL";
                default: return "ŞU AN YÜKSELTİLEMEZ";
            }
        }

        internal static string FormatDuration(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.RoundToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }

        /// <summary>No nested state, so Back falls through to the shell.</summary>
        public bool HandleBack() => false;
    }
}
