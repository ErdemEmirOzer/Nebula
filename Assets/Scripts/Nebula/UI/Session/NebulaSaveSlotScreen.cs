using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.UI.Session
{
    /// <summary>
    /// KAYIT YÜKLE — every slot, what is in it, and whether it can be started.
    /// <para>
    /// The main menu's Continue card describes exactly one slot: the most recent
    /// playable one. That is the right default and the wrong whole story — a player
    /// with three campaigns could see one of them and reach none of the others. This
    /// screen is the list, built on the same read-only
    /// <see cref="NebulaSaveSlotQuery"/> the card uses, so a slot is never loaded to
    /// find out what is in it.
    /// </para>
    /// <para>
    /// A slot that cannot be started is drawn <b>with its reason</b> rather than
    /// omitted. "Nothing here" and "written by a newer build" are different facts,
    /// and a player whose save was made by a newer build must be told that instead
    /// of being told it is gone.
    /// </para>
    /// </summary>
    public sealed class NebulaSaveSlotScreen : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "session.load";

        private INebulaSessionLauncher launcher;
        private NebulaUiShell shell;
        private RectTransform root;
        private RectTransform list;
        private NebulaUiFactory.LabelHandle emptyState;

        private readonly List<SlotCard> cards = new List<SlotCard>(NebulaSaveSlotQuery.SlotCount);

        /// <summary>Test-facing: slots drawn on the screen.</summary>
        public int SlotCount => cards.Count;

        /// <summary>Test-facing: how many slots could actually be started at the last refresh.</summary>
        public int PlayableSlots { get; private set; }

        /// <summary>Test-facing: the slot the player last asked to load, or -1.</summary>
        public int LastRequestedSlot { get; private set; } = -1;

        /// <summary>
        /// Test-facing: why the screen has nothing to offer, or <c>null</c> when at
        /// least one slot is playable.
        /// </summary>
        public string NothingToLoadReason { get; private set; }

        private sealed class SlotCard
        {
            public int Slot;
            public RectTransform Root;
            public NebulaUiFactory.LabelHandle Heading;
            public NebulaUiFactory.LabelHandle Detail;
            public NebulaUiFactory.LabelHandle Blocked;
            public NebulaUiFactory.ButtonHandle Load;
        }

        public static NebulaSaveSlotScreen Create(NebulaUiShell shell, INebulaSessionLauncher launcher)
        {
            var go = new GameObject("Save Slot Screen");
            var screen = go.AddComponent<NebulaSaveSlotScreen>();
            screen.launcher = launcher;
            screen.Build(shell);
            return screen;
        }

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Load Root");
            NebulaUiFactory.Fill(root);
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            Image backdrop = NebulaUiFactory.Rect(root, "Backdrop", NebulaUiDesignTokens.SpaceVoid);
            NebulaUiFactory.Fill(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            NebulaSessionChrome.Header(root, "KAYIT YÜKLE");
            NebulaSessionChrome.BackButton(root, () => shell.Back());

            list = NebulaUiFactory.Group(root, "Slots");
            NebulaUiFactory.Place(list, new Vector2(0f, 1f), new Vector2(88f, -110f),
                new Vector2(900f, 640f));

            emptyState = NebulaUiFactory.Label(root, "Empty", "",
                NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Caution),
                TextAnchor.UpperLeft, FontStyle.Bold, 0f, wrap: true);
            NebulaUiFactory.Place(emptyState.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(88f, -96f), new Vector2(900f, 24f));

            for (int i = 0; i < NebulaSaveSlotQuery.SlotCount; i++)
                cards.Add(BuildCard(NebulaSaveSlotQuery.FirstSlot + i, i));

            Refresh();
        }

        private SlotCard BuildCard(int slot, int index)
        {
            var card = new SlotCard { Slot = slot };

            // "Slot N" is the name the session page already gives its own slot cards,
            // so one finder walks both surfaces.
            Image panel = NebulaUiFactory.Panel(list, "Slot " + slot, NebulaUiAssetCatalog.PanelSecondary);
            card.Root = panel.rectTransform;
            NebulaUiFactory.Place(card.Root, new Vector2(0f, 1f), new Vector2(0f, -index * 150f),
                new Vector2(900f, 138f));

            card.Heading = NebulaUiFactory.Label(card.Root, "Heading", "", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(card.Heading.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(24f, -22f), new Vector2(520f, 20f));

            card.Detail = NebulaUiFactory.Label(card.Root, "Detail", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(card.Detail.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(24f, -46f), new Vector2(560f, 76f));

            card.Blocked = NebulaUiFactory.Label(card.Root, "Blocked", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Caution), TextAnchor.UpperLeft,
                FontStyle.Bold, 0f, wrap: true);
            NebulaUiFactory.Place(card.Blocked.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(24f, -46f), new Vector2(560f, 40f));

            int captured = slot;
            card.Load = NebulaUiFactory.ButtonControl(card.Root, "Load", "YÜKLE",
                NebulaUiAssetCatalog.ButtonKind.Primary, () => LoadSlot(captured));
            NebulaUiFactory.Place(card.Load.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(200f, 44f));

            return card;
        }

        /// <summary>
        /// Re-reads every slot from disk. Called on open and on reveal, so a save
        /// made elsewhere in the same sitting is never described from a stale read.
        /// </summary>
        public void Refresh()
        {
            PlayableSlots = 0;

            for (int i = 0; i < cards.Count; i++)
            {
                SlotCard card = cards[i];
                SaveSlotSummary summary = NebulaSaveSlotQuery.Read(card.Slot);

                bool playable = summary != null && summary.CanContinue;
                if (playable) PlayableSlots++;

                card.Heading.Value = $"YUVA {card.Slot}";
                card.Detail.Text.gameObject.SetActive(playable);
                card.Blocked.Text.gameObject.SetActive(!playable);

                if (playable) card.Detail.Value = Describe(summary);
                else card.Blocked.Value = NebulaSaveSlotQuery.BlockReasonText(summary) ?? "KAYIT OKUNAMADI";

                // Disabled with the reason on the control itself, never a dead
                // button: a slot the player cannot start has to say which of the
                // three things it is.
                card.Load.SetAvailable(playable,
                    playable ? null : NebulaSaveSlotQuery.BlockReasonText(summary));
            }

            NothingToLoadReason = PlayableSlots > 0 ? null : "YÜKLENEBİLECEK KAYIT YOK — YENİ BİR SEFER BAŞLATIN";
            emptyState.Value = NothingToLoadReason ?? string.Empty;
            emptyState.Text.gameObject.SetActive(NothingToLoadReason != null);
        }

        private static string Describe(SaveSlotSummary slot)
        {
            var text = new System.Text.StringBuilder(200);
            text.Append($"SEVİYE {slot.Level}   ·   {slot.TotalXp} XP\n");
            text.Append($"{slot.KnownSystems} SİSTEM   ·   {slot.HeldPlanets} GEZEGEN   ·   {slot.FleetVehicles} ARAÇ\n");
            if (!string.IsNullOrEmpty(slot.LocationName)) text.Append($"KONUM: {slot.LocationName}\n");
            if (slot.SavedAtUtc.HasValue) text.Append(NebulaMainMenuScreen.RelativeTime(slot.SavedAtUtc.Value));
            return text.ToString();
        }

        /// <summary>
        /// Starts exactly one session on the chosen slot.
        /// <para>
        /// The slot is re-read immediately before the launch rather than trusted from
        /// the last refresh: a slot can rot between being drawn and being clicked,
        /// and handing a corrupt file to the loader is the one failure this screen
        /// exists to prevent. A refused click refreshes in place so the player sees
        /// the new reason instead of a screen that did nothing.
        /// </para>
        /// </summary>
        private void LoadSlot(int slot)
        {
            SaveSlotSummary summary = NebulaSaveSlotQuery.Read(slot);
            if (summary == null || !summary.CanContinue) { Refresh(); return; }

            LastRequestedSlot = slot;

            // The screen closes itself first. The host tears the whole menu shell
            // down once the session is running, but that happens a frame later --
            // and until it does, a still-open screen holds the pause the new
            // session's own shell is about to take.
            shell?.PopAll();
            launcher?.ContinueFromSlot(slot);
        }

        // ---- INebulaUiScreen -------------------------------------------------

        public void OnOpened(NebulaUiShell owningShell)
        {
            shell = owningShell;
            root.gameObject.SetActive(true);
            Refresh();
        }

        public void OnClosed() => root.gameObject.SetActive(false);

        public void OnCovered() { }

        public void OnRevealed() => Refresh();

        /// <summary>No nested state, so Back falls through and the shell pops this screen.</summary>
        public bool HandleBack() => false;
    }
}
