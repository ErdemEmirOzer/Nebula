using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Navigation;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// What the player knows, and — just as importantly — what they do not.
    /// <para>
    /// This page is where the information gate is most easily broken, because
    /// every field it could show exists in memory already. A system at Signal
    /// knowledge has a real name, a real position and real planets in the
    /// simulation; showing any of that would hand the player something the game
    /// says they have not earned. So each row is rendered from its knowledge level
    /// down, never from the data up.
    /// </para>
    /// </summary>
    public sealed class NebulaDiscoveryPage : INebulaMenuPage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Discovery;
        public string Title => "Keşif";

        /// <summary>Rows drawn at once, most-known first.</summary>
        public const int MaxSystemRows = 10;

        private readonly NebulaContext context;

        private NebulaUiFactory.LabelHandle summaryValue;
        private NebulaUiFactory.LabelHandle analysisValue;
        private NebulaUiFactory.LabelHandle emptyState;
        private RectTransform systemList;

        private readonly List<SystemRow> rows = new List<SystemRow>(MaxSystemRows);
        private readonly List<KeyValuePair<string, SystemKnowledgeLevel>> ordered =
            new List<KeyValuePair<string, SystemKnowledgeLevel>>(64);

        /// <summary>Signals that have not been analysed yet.</summary>
        public int AttentionCount { get; private set; }

        /// <summary>Test-facing: system rows currently shown.</summary>
        public int VisibleRows { get; private set; }

        /// <summary>The system the action bar acts on, or <c>null</c> when none is chosen.</summary>
        public string SelectedSystemId { get; private set; }

        /// <summary>Test-facing: why analysis is unavailable, or <c>null</c>.</summary>
        public string AnalysisBlockedReason { get; private set; }

        /// <summary>Test-facing: the last thing the page told the player.</summary>
        public string StatusText => statusLine?.Text != null ? statusLine.Text.text : null;

        private NebulaUiFactory.ButtonHandle analyzeButton, cancelAnalysisButton;
        private NebulaUiFactory.LabelHandle statusLine;

        /// <summary>Row ids in draw order, so a click resolves to a system.</summary>
        private readonly List<string> rowIds = new List<string>(MaxSystemRows);

        private sealed class SystemRow
        {
            public RectTransform Root;
            public Image Background;
            public Image KnowledgeIcon;
            public NebulaUiFactory.LabelHandle Name;
            public NebulaUiFactory.LabelHandle Level;
        }

        public NebulaDiscoveryPage(NebulaContext sessionContext)
        {
            context = sessionContext;
        }

        public void Build(RectTransform host)
        {
            var column = NebulaUiFactory.Group(host, "Discovery Column");
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(32f, 24f);
            column.offsetMax = new Vector2(-32f, -24f);

            BuildSummary(column);
            BuildList(column);
            BuildActions(column);
        }

        /// <summary>
        /// Starting a signal analysis had exactly one home before the redesign --
        /// the navigation screen -- and the redesign showed the analysis running
        /// without offering any way to start one. Deleting that screen without this
        /// would have removed the action from the game.
        /// </summary>
        private void BuildActions(RectTransform parent)
        {
            analyzeButton = NebulaUiFactory.ButtonControl(parent, "Analyze", "SİNYAL ANALİZİ BAŞLAT",
                NebulaUiAssetCatalog.ButtonKind.Primary, StartAnalysis);
            NebulaUiFactory.Place(analyzeButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(260f, 44f));

            cancelAnalysisButton = NebulaUiFactory.ButtonControl(parent, "CancelAnalysis", "ANALİZİ İPTAL",
                NebulaUiAssetCatalog.ButtonKind.Secondary, CancelAnalysis);
            NebulaUiFactory.Place(cancelAnalysisButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-272f, 0f), new Vector2(200f, 44f));

            statusLine = NebulaUiFactory.Label(parent, "Status", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(statusLine.Text.rectTransform, new Vector2(0f, 0f),
                new Vector2(0f, 12f), new Vector2(560f, 32f));
        }

        /// <summary>Picks the system the action bar acts on.</summary>
        public void Select(string systemId)
        {
            SelectedSystemId = systemId;
            Refresh();
        }

        private void StartAnalysis()
        {
            NavigationDirector director = context?.NavigationDirector;
            if (director == null || SelectedSystemId == null) return;

            // The service owns every refusal; the page only translates it. The old
            // screen discarded the reason entirely (`out _`), so a refused analysis
            // looked exactly like nothing happening.
            if (!director.TryStartAnalysis(SelectedSystemId, out string reject))
            {
                Report(AnalysisRejectText(reject));
                return;
            }

            Report("SİNYAL ANALİZİ BAŞLADI");
            Refresh();
        }

        private void CancelAnalysis()
        {
            NavigationDirector director = context?.NavigationDirector;
            if (director == null || !director.Analysis.Active) return;

            director.CancelAnalysis();
            Report("SİNYAL ANALİZİ İPTAL EDİLDİ");
            Refresh();
        }

        private void Report(string message)
        {
            if (statusLine == null) return;
            statusLine.Value = message ?? string.Empty;
        }

        /// <summary>
        /// The director answers in English because it predates the redesign. The
        /// player never sees those strings; this is the whole mapping, and an
        /// unrecognised reason still says something rather than nothing.
        /// </summary>
        internal static string AnalysisRejectText(string reject) => reject switch
        {
            "REQUIRES SCANNER T1" => "TARAYICI T1 GEREKLİ",
            "TARGET MUST BE A SIGNAL" => "HEDEF BİR SİNYAL OLMALI",
            "ANALYSIS ALREADY IN PROGRESS" => "ZATEN BİR ANALİZ SÜRÜYOR",
            _ => "ANALİZ BAŞLATILAMADI"
        };

        private void BuildSummary(RectTransform parent)
        {
            RectTransform card = Card(parent, "Summary", 0f, 104f, "BİLGİ");

            summaryValue = NebulaUiFactory.Value(card, "Value", "—", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(summaryValue.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -18f), new Vector2(460f, 16f));

            analysisValue = NebulaUiFactory.Label(card, "Analysis", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(analysisValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -56f), new Vector2(520f, 20f));
        }

        private void BuildList(RectTransform parent)
        {
            RectTransform card = Card(parent, "Systems", -116f, 700f, "SİSTEMLER");

            emptyState = NebulaUiFactory.Label(card, "Empty", "HENÜZ HİÇBİR SİSTEM BİLİNMİYOR",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleCenter, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(emptyState.Text.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -80f), new Vector2(700f, 20f));

            systemList = NebulaUiFactory.Group(card, "List");
            systemList.anchorMin = new Vector2(0f, 1f);
            systemList.anchorMax = new Vector2(1f, 1f);
            systemList.pivot = new Vector2(0.5f, 1f);
            systemList.offsetMin = new Vector2(14f, -688f);
            systemList.offsetMax = new Vector2(-14f, -44f);

            for (int i = 0; i < MaxSystemRows; i++) rows.Add(CreateRow(i));
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

        private SystemRow CreateRow(int index)
        {
            var row = new SystemRow();
            Image panel = NebulaUiFactory.Panel(systemList, "System " + index,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            row.Root = panel.rectTransform;
            row.Background = panel;

            row.Root.anchorMin = new Vector2(0f, 1f);
            row.Root.anchorMax = new Vector2(1f, 1f);
            row.Root.pivot = new Vector2(0.5f, 1f);
            row.Root.anchoredPosition = new Vector2(0f, -index * (NebulaUiDesignTokens.RowHeight + 4f));
            row.Root.sizeDelta = new Vector2(0f, NebulaUiDesignTokens.RowHeight);

            row.KnowledgeIcon = NebulaUiFactory.Icon(row.Root, "Knowledge", null, 20f);
            NebulaUiFactory.Place(row.KnowledgeIcon.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(20f, 20f));

            row.Name = NebulaUiFactory.Label(row.Root, "Name", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(row.Name.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(46f, 0f), new Vector2(420f, 18f));

            row.Level = NebulaUiFactory.Label(row.Root, "Level", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleRight, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Level.Text.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-16f, 0f), new Vector2(240f, 16f));

            // The row is the selector. Reusing the row panel as the button's target
            // graphic is required -- a second Image on the same GameObject returns
            // null, because Graphic needs a single-instance CanvasRenderer.
            var button = row.Root.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = row.Background;
            int captured = index;
            button.onClick.AddListener(() => SelectRow(captured));

            row.Root.gameObject.SetActive(false);
            return row;
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= rowIds.Count) return;
            Select(rowIds[index]);
        }

        // ---- refresh ---------------------------------------------------------

        public void Refresh()
        {
            AttentionCount = 0;
            NavigationDirector director = context?.NavigationDirector;

            ordered.Clear();
            if (director?.Knowledge != null)
                foreach (KeyValuePair<string, SystemKnowledgeLevel> entry in director.Knowledge.All)
                    if (entry.Value != SystemKnowledgeLevel.Unknown) ordered.Add(entry);

            // Most-known first: a visited system is a place, a signal is a rumour,
            // and the list should read in that order rather than by id.
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

            int signal = 0, located = 0, visited = 0;
            foreach (KeyValuePair<string, SystemKnowledgeLevel> entry in ordered)
            {
                switch (entry.Value)
                {
                    case SystemKnowledgeLevel.Signal: signal++; break;
                    case SystemKnowledgeLevel.Located: located++; break;
                    case SystemKnowledgeLevel.Visited: visited++; break;
                }
            }

            AttentionCount = signal;
            summaryValue.Value = $"{signal} SİNYAL · {located} BELİRLENDİ · {visited} ZİYARET EDİLDİ";

            bool any = ordered.Count > 0;
            if (emptyState.Text.gameObject.activeSelf == any)
                emptyState.Text.gameObject.SetActive(!any);

            RefreshAnalysis(director);
            RefreshRows(director);
            RefreshActions(director);
        }

        private void RefreshActions(NavigationDirector director)
        {
            if (analyzeButton == null) return;

            bool analysing = director?.Analysis != null && director.Analysis.Active;

            AnalysisBlockedReason =
                director == null ? "OTURUM YOK"
                : SelectedSystemId == null ? "BİR SİSTEM SEÇİN"
                : analysing ? "ZATEN BİR ANALİZ SÜRÜYOR"
                : director.Knowledge.GetLevel(SelectedSystemId) != SystemKnowledgeLevel.Signal
                    ? "HEDEF BİR SİNYAL OLMALI"
                : director.ScannerTier < 1 ? "TARAYICI T1 GEREKLİ"
                : null;

            analyzeButton.SetAvailable(AnalysisBlockedReason == null, AnalysisBlockedReason);
            cancelAnalysisButton.SetAvailable(analysing, "SÜRMEKTE OLAN ANALİZ YOK");
        }

        private void RefreshAnalysis(NavigationDirector director)
        {
            if (director?.Analysis == null || !director.Analysis.Active)
            {
                analysisValue.Value = "SİNYAL ANALİZİ YOK";
                analysisValue.Text.color = NebulaUiDesignTokens.InkMuted;
                return;
            }

            // The target of an in-flight analysis is by definition not yet known,
            // so it is described by what is happening, never named.
            analysisValue.Value = $"SİNYAL ANALİZİ SÜRÜYOR · {director.Analysis.RemainingSeconds:0}sn";
            analysisValue.Text.color = NebulaUiDesignTokens.Status(UiStatusLevel.Working);
        }

        private void RefreshRows(NavigationDirector director)
        {
            int shown = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                bool visible = i < ordered.Count;
                if (rows[i].Root.gameObject.activeSelf != visible)
                    rows[i].Root.gameObject.SetActive(visible);
                if (!visible) continue;

                if (rowIds.Count <= i) rowIds.Add(ordered[i].Key); else rowIds[i] = ordered[i].Key;
                ApplyRow(rows[i], ordered[i].Key, ordered[i].Value, director);
                shown++;
            }
            VisibleRows = shown;
        }

        private static void ApplyRow(SystemRow row, string systemId, SystemKnowledgeLevel level,
            NavigationDirector director)
        {
            row.Name.Value = DisplayName(systemId, level, director);
            row.Name.Text.color = level == SystemKnowledgeLevel.Signal
                ? NebulaUiDesignTokens.InkMuted
                : NebulaUiDesignTokens.InkPrimary;

            row.Level.Value = LevelName(level);
            row.Level.Text.color = NebulaUiDesignTokens.Status(LevelStatus(level));

            Sprite icon = NebulaProducedVisualResolver.Knowledge(level);
            row.KnowledgeIcon.sprite = icon;
            row.KnowledgeIcon.enabled = icon != null;

            row.Background.sprite = NebulaUiAssetCatalog.Row(
                level == SystemKnowledgeLevel.Signal
                    ? NebulaUiAssetCatalog.RowState.Disabled
                    : NebulaUiAssetCatalog.RowState.Default);
        }

        /// <summary>
        /// The name, only if it has been earned.
        /// <para>
        /// A system at Signal knowledge has a real display name sitting in the
        /// registry, and printing it would be the easiest information leak in the
        /// game — the player would learn a place exists, and what it is called,
        /// from a rumour. Signals are described, not named.
        /// </para>
        /// </summary>
        internal static string DisplayName(string systemId, SystemKnowledgeLevel level,
            NavigationDirector director)
        {
            if (level <= SystemKnowledgeLevel.Signal) return "TANIMLANMAMIŞ SİNYAL";

            if (director != null && director.TryGetSystem(systemId, out StarSystemRecord record)
                && !string.IsNullOrEmpty(record.DisplayName))
            {
                return record.DisplayName;
            }

            return "BİLİNMEYEN SİSTEM";
        }

        internal static string LevelName(SystemKnowledgeLevel level)
        {
            switch (level)
            {
                case SystemKnowledgeLevel.Signal: return "SİNYAL";
                case SystemKnowledgeLevel.Located: return "BELİRLENDİ";
                case SystemKnowledgeLevel.Visited: return "ZİYARET EDİLDİ";
                default: return "BİLİNMİYOR";
            }
        }

        private static UiStatusLevel LevelStatus(SystemKnowledgeLevel level)
        {
            switch (level)
            {
                case SystemKnowledgeLevel.Visited: return UiStatusLevel.Nominal;
                case SystemKnowledgeLevel.Located: return UiStatusLevel.Working;
                case SystemKnowledgeLevel.Signal: return UiStatusLevel.Caution;
                default: return UiStatusLevel.Inactive;
            }
        }

        /// <summary>No nested state, so Back falls through to the shell.</summary>
        public bool HandleBack() => false;
    }
}
