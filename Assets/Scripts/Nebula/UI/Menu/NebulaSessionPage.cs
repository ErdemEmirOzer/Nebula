using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.UI.Design;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// Save, load, restart, and leaving.
    /// <para>
    /// Until now the only way to save was a <c>Ctrl</c>+<c>,</c> chord that
    /// nothing on screen mentioned. A player who did not read the source could
    /// not save at all, which is a strange thing for a game with a schema-14 save
    /// system to be true of. This is that surface.
    /// </para>
    /// <para>
    /// Every slot states what is in it and what is wrong with it, using the same
    /// read the main menu uses — see <see cref="NebulaSaveSlotQuery"/>. Loading
    /// and restarting are irreversible, so both ask first.
    /// </para>
    /// </summary>
    public sealed class NebulaSessionPage : INebulaMenuPage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Session;
        public string Title => "Oturum";

        /// <summary>Session actions raise no attention; the rail badge stays empty.</summary>
        public int AttentionCount => 0;

        private readonly NebulaContext context;
        private readonly NebulaConfirmationDialog confirmation;
        private readonly INebulaSessionLauncher launcher;

        private readonly List<SlotRow> slots = new List<SlotRow>(NebulaSaveSlotQuery.SlotCount);
        private NebulaUiFactory.LabelHandle statusLine;

        /// <summary>Test-facing: the last thing the page told the player.</summary>
        public string StatusText => statusLine?.Text != null ? statusLine.Text.text : null;

        /// <summary>Test-facing: slot rows built.</summary>
        public int SlotCount => slots.Count;

        private sealed class SlotRow
        {
            public int Slot;
            public NebulaUiFactory.LabelHandle Title;
            public NebulaUiFactory.LabelHandle Detail;
            public NebulaUiFactory.ButtonHandle Save;
            public NebulaUiFactory.ButtonHandle Load;
        }

        /// <summary>
        /// The shell this page lives in, so leaving the campaign can release the
        /// modal and the pause before the session that owns them is disposed. Same
        /// reason <see cref="NebulaMapPage"/> takes one; optional for the fixtures
        /// that build a page without a shell.
        /// </summary>
        private readonly NebulaUiShell shell;

        public NebulaSessionPage(NebulaContext sessionContext,
            NebulaConfirmationDialog confirmationDialog = null,
            INebulaSessionLauncher sessionLauncher = null,
            NebulaUiShell owningShell = null)
        {
            context = sessionContext;
            confirmation = confirmationDialog;
            launcher = sessionLauncher;
            shell = owningShell;
        }

        public void Build(RectTransform host)
        {
            var column = NebulaUiFactory.Group(host, "Session Column");
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(32f, 24f);
            column.offsetMax = new Vector2(-32f, -24f);

            BuildSlots(column);
            BuildDangerZone(column);
        }

        private void BuildSlots(RectTransform parent)
        {
            RectTransform card = Card(parent, "Slots", 0f, 372f, "KAYIT YUVALARI");

            for (int i = 0; i < NebulaSaveSlotQuery.SlotCount; i++)
            {
                int slotNumber = NebulaSaveSlotQuery.FirstSlot + i;
                var row = new SlotRow { Slot = slotNumber };

                Image panel = NebulaUiFactory.Panel(card, "Slot " + slotNumber,
                    NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Default));
                RectTransform rect = panel.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -46f - i * 104f);
                rect.sizeDelta = new Vector2(-40f, 96f);

                row.Title = NebulaUiFactory.Label(rect, "Title", $"YUVA {slotNumber}",
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                    TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
                NebulaUiFactory.Place(row.Title.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(18f, -16f), new Vector2(420f, 18f));

                row.Detail = NebulaUiFactory.Label(rect, "Detail", "",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
                NebulaUiFactory.Place(row.Detail.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(18f, -40f), new Vector2(560f, 36f));

                int captured = slotNumber;
                row.Save = NebulaUiFactory.ButtonControl(rect, "Save", "KAYDET",
                    NebulaUiAssetCatalog.ButtonKind.Secondary, () => SaveTo(captured));
                NebulaUiFactory.Place(row.Save.Button.GetComponent<RectTransform>(),
                    new Vector2(1f, 1f), new Vector2(-190f, -18f), new Vector2(160f, 36f));

                row.Load = NebulaUiFactory.ButtonControl(rect, "Load", "YÜKLE",
                    NebulaUiAssetCatalog.ButtonKind.Primary, () => LoadFrom(captured));
                NebulaUiFactory.Place(row.Load.Button.GetComponent<RectTransform>(),
                    new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(160f, 36f));

                slots.Add(row);
            }
        }

        private void BuildDangerZone(RectTransform parent)
        {
            RectTransform card = Card(parent, "Danger", -388f, 172f, "SEFER");

            statusLine = NebulaUiFactory.Label(card, "Status", "",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(statusLine.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -44f), new Vector2(700f, 34f));

            NebulaUiFactory.ButtonHandle restart = NebulaUiFactory.ButtonControl(card, "Restart",
                "SEFERİ YENİDEN BAŞLAT", NebulaUiAssetCatalog.ButtonKind.Destructive, Restart);
            NebulaUiFactory.Place(restart.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(280f, 40f));

            NebulaUiFactory.ButtonHandle toMenu = NebulaUiFactory.ButtonControl(card, "MainMenu",
                "ANA MENÜYE DÖN", NebulaUiAssetCatalog.ButtonKind.Secondary, ReturnToMainMenu);
            NebulaUiFactory.Place(toMenu.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(316f, 20f), new Vector2(240f, 40f));

            NebulaUiFactory.ButtonHandle quit = NebulaUiFactory.ButtonControl(card, "Quit",
                "OYUNDAN ÇIK", NebulaUiAssetCatalog.ButtonKind.Destructive, Quit);
            NebulaUiFactory.Place(quit.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(576f, 20f), new Vector2(220f, 40f));
        }

        /// <summary>
        /// Leaves the campaign for the main menu. Confirmed, because unsaved
        /// progress dies with the session — the same reason Quit asks.
        /// </summary>
        private void ReturnToMainMenu()
        {
            if (confirmation == null) return;
            confirmation.Ask("ANA MENÜYE DÖN",
                "Kaydedilmemiş ilerleme kaybolur. Ana menüye dönülsün mü?",
                null, "DÖN",
                () =>
                {
                    // The shell has to let go before the session it belongs to is
                    // disposed, or the menu comes back underneath a page that is
                    // still holding the modal and the pause.
                    shell?.PopAll();
                    launcher?.ReturnToMainMenu();
                },
                destructive: true);
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

        // ---- actions ---------------------------------------------------------

        private void SaveTo(int slot)
        {
            ExtractionDirector director = context?.ExtractionDirector;
            if (director == null) { Report("KAYDEDİLEMEDİ — OTURUM YOK"); return; }

            // Overwriting an existing campaign is not recoverable, so it asks. An
            // empty slot does not: there is nothing to lose.
            SaveSlotSummary existing = NebulaSaveSlotQuery.Read(slot);
            if (existing.Exists && confirmation != null)
            {
                confirmation.Ask($"YUVA {slot}", "Bu yuvadaki kayıt üzerine yazılacak. Devam edilsin mi?",
                    null, "ÜZERİNE YAZ", () => DoSave(director, slot), destructive: true);
                return;
            }

            DoSave(director, slot);
        }

        private void DoSave(ExtractionDirector director, int slot)
        {
            // The slot has to reach the director before the write, not just the
            // status line. Without this the screen reported "YUVA 2 KAYDEDİLDİ"
            // over a file it had written to slot 1.
            director.ActiveSlot = slot;
            director.SaveNow("MENU SAVE");
            Report($"YUVA {slot} KAYDEDİLDİ");
            Refresh();
        }

        private void LoadFrom(int slot)
        {
            SaveSlotSummary summary = NebulaSaveSlotQuery.Read(slot);
            if (!summary.CanContinue)
            {
                Report(NebulaSaveSlotQuery.BlockReasonText(summary));
                return;
            }

            ExtractionDirector director = context?.ExtractionDirector;
            if (director == null) { Report("YÜKLENEMEDİ — OTURUM YOK"); return; }

            void Load()
            {
                director.ActiveSlot = slot;
                director.LoadNow();
                Report($"YUVA {slot} YÜKLENDİ");
                Refresh();
            }

            // Loading throws away everything since the last save, which is exactly
            // the kind of loss a confirmation exists for.
            if (confirmation == null) { Load(); return; }
            confirmation.Ask($"YUVA {slot}",
                "Son kayıttan bu yana yapılan her şey kaybolacak. Devam edilsin mi?",
                null, "YÜKLE", Load, destructive: true);
        }

        private void Restart()
        {
            if (confirmation == null) return;
            confirmation.Ask("SEFERİ YENİDEN BAŞLAT",
                "Mevcut sefer tamamen silinecek ve yeni bir evren kurulacak. Bu işlem geri alınamaz.",
                null, "YENİDEN BAŞLAT",
                () => { launcher?.RestartCampaign(); Report("YENİ SEFER BAŞLATILDI"); },
                destructive: true);
        }

        private void Quit()
        {
            if (confirmation == null) return;
            confirmation.Ask("OYUNDAN ÇIK",
                "Kaydedilmemiş ilerleme kaybolur. Çıkılsın mı?",
                null, "ÇIK", () => launcher?.QuitGame(), destructive: true);
        }

        private void Report(string message)
        {
            if (statusLine == null) return;
            statusLine.Value = message ?? string.Empty;
        }

        // ---- refresh ---------------------------------------------------------

        public void Refresh()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                SlotRow row = slots[i];
                SaveSlotSummary summary = NebulaSaveSlotQuery.Read(row.Slot);

                row.Detail.Value = Describe(summary);
                row.Detail.Text.color = summary.CanContinue
                    ? NebulaUiDesignTokens.InkMuted
                    : NebulaUiDesignTokens.Status(UiStatusLevel.Caution);

                // Saving into a slot is always possible; loading is not, and the
                // reason belongs on the control that is refusing.
                row.Save.SetAvailable(context?.ExtractionDirector != null, "OTURUM YOK");
                row.Load.SetAvailable(summary.CanContinue,
                    NebulaSaveSlotQuery.BlockReasonText(summary));
            }
        }

        internal static string Describe(SaveSlotSummary summary)
        {
            if (summary == null) return "OKUNAMADI";
            if (!summary.Exists) return "BOŞ";
            if (!summary.CanContinue) return NebulaSaveSlotQuery.BlockReasonText(summary);

            var text = new System.Text.StringBuilder(120);
            text.Append($"SEVİYE {summary.Level}  ·  {summary.KnownSystems} SİSTEM  ·  ");
            text.Append($"{summary.HeldPlanets} GEZEGEN  ·  {summary.FleetVehicles} ARAÇ");
            if (summary.SavedAtUtc.HasValue)
                text.Append("\n").Append(NebulaMainMenuScreen.RelativeTime(summary.SavedAtUtc.Value));
            return text.ToString();
        }

        /// <summary>No nested state — the confirmation is the shell's, not this page's.</summary>
        public bool HandleBack() => false;
    }
}
