using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.UI.Controls
{
    /// <summary>
    /// The rebindable half of the controls setting.
    /// <para>
    /// The settings screen has always <i>listed</i> keys — they were compile-time
    /// constants, so a list was the honest thing to show. Nine of them are not
    /// constants: <see cref="CombatBindings"/> has had <c>TrySetBinding</c>,
    /// <c>CurrentBindings</c> and <c>ResetToDefaults</c> since the combat slice, and
    /// there was no surface anywhere that reached them. A player could not change a
    /// single key in the game.
    /// </para>
    /// <para>
    /// A row here is: what the action is, what it is bound to, and one control that
    /// arms a capture. The capture itself is a real key press read from a real
    /// <c>Update</c>; nothing about it is simulated, and cancelling leaves the
    /// existing binding exactly where it was.
    /// </para>
    /// </summary>
    public sealed class NebulaControlBindingSection
    {
        /// <summary>The name of the control that arms a capture, on every row.</summary>
        public const string RebindControlName = "Rebind";

        /// <summary>The name of the control that abandons an armed capture, on every row.</summary>
        public const string CancelControlName = "Cancel";

        /// <summary>The name of the section-wide restore-defaults control.</summary>
        public const string ResetControlName = "ResetBindings";

        /// <summary>The name every row's GameObject carries: <c>Bind &lt;action&gt;</c>.</summary>
        public static string RowName(CombatAction action) => "Bind " + action;

        /// <summary>The name a group's rail control carries: <c>Group &lt;context&gt;</c>.</summary>
        public static string GroupControlName(InputContext context) => "Group " + context;

        /// <summary>The name a group's row host carries.</summary>
        public static string GroupHostName(InputContext context) => "Group Rows " + context;

        private sealed class BindingRow
        {
            public CombatAction Action;
            public NebulaUiFactory.LabelHandle Value;
            public NebulaUiFactory.ButtonHandle Rebind;
            public NebulaUiFactory.ButtonHandle Cancel;
        }

        private sealed class ContextBindingRow
        {
            public string ActionId;
            public NebulaUiFactory.LabelHandle Value;
            public NebulaUiFactory.ButtonHandle Rebind;
            public NebulaUiFactory.ButtonHandle Cancel;
        }

        /// <summary>
        /// One rail entry: a context, the rows that belong to it, and the host that
        /// holds them.
        /// <para>
        /// The list is grouped because it stopped fitting. Nine combat rows fitted the
        /// panel it was designed for; every rebindable non-combat row was added later
        /// and the layout was not revisited, so thirty-one rows were laid out down a
        /// 544px column at 48px each and two thirds of them were drawn past the bottom
        /// of the panel — off the page entirely at 140% interface scale. A player could
        /// not reach the extraction, logistics, processing or POI keys at all.
        /// </para>
        /// <para>
        /// Grouped by <see cref="InputContext"/>, which is the catalog's own data, not
        /// by a hand-kept list of "the ones we thought of" beside it — the same reason
        /// the rows themselves come from the service's enumeration. The largest group
        /// is nine rows, which fits with the rail beside it and no scrolling, so every
        /// control stays a real focusable <c>Button</c> for the keyboard and the pad.
        /// </para>
        /// </summary>
        private sealed class BindingGroup
        {
            public InputContext Context;
            public RectTransform Host;
            public NebulaUiFactory.ButtonHandle Rail;
            public int RowCount;
        }

        /// <summary>The name of the label that reports a key two live owners both answer.</summary>
        public const string ConflictLabelName = "BindingConflict";

        private readonly List<BindingRow> rows = new List<BindingRow>();
        private readonly List<ContextBindingRow> contextRows = new List<ContextBindingRow>();
        private readonly List<BindingGroup> groups = new List<BindingGroup>();
        private NebulaUiFactory.LabelHandle status;
        private NebulaUiFactory.LabelHandle conflicts;
        private NebulaKeyCaptureListener listener;

        /// <summary>The group whose rows are on screen, or <c>null</c> before Build.</summary>
        public InputContext? SelectedGroup { get; private set; }

        /// <summary>Test-facing: the contexts the rail offers, in rail order.</summary>
        public IReadOnlyList<InputContext> Groups
        {
            get
            {
                var list = new List<InputContext>(groups.Count);
                foreach (BindingGroup group in groups) list.Add(group.Context);
                return list;
            }
        }

        /// <summary>
        /// Shows one group's rows and hides the rest. Refused while a capture is armed
        /// — an armed row that has just been hidden would still be listening, which is
        /// the same defect <c>NebulaKeyCaptureListener.OnDisable</c> exists to prevent.
        /// </summary>
        public void ShowGroup(InputContext context)
        {
            if (CapturingActionId != null) return;
            for (int i = 0; i < groups.Count; i++)
            {
                BindingGroup group = groups[i];
                bool selected = group.Context == context;
                if (group.Host.gameObject.activeSelf != selected)
                    group.Host.gameObject.SetActive(selected);
                if (selected) SelectedGroup = context;
            }
            Refresh();
        }

        /// <summary>Test-facing: how many actions the section lists.</summary>
        public int RowCount => rows.Count + contextRows.Count;

        /// <summary>The action waiting for a key, or <c>null</c> when nothing is armed.</summary>
        public CombatAction? Capturing { get; private set; }

        /// <summary>Stable id of any armed row, including contextual non-combat actions.</summary>
        public string CapturingActionId { get; private set; }

        /// <summary>
        /// The component whose <c>Update</c> samples the keyboard. Exposed so a test
        /// can assert the capture is armed at the <i>listener</i> and not merely in
        /// this class's own field — the two disagreeing is exactly how a screen ends
        /// up showing "press a key" over a listener that has stopped reading.
        /// </summary>
        public NebulaKeyCaptureListener Listener => listener;

        /// <summary>Test-facing: the last thing the section told the player.</summary>
        public string StatusText => status?.Text != null ? status.Text.text : null;

        /// <summary>Test-facing: the key label a given row is showing.</summary>
        public string LabelFor(CombatAction action)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Action == action) return rows[i].Value.Text.text;
            return null;
        }

        /// <summary>
        /// Builds one row per action into <paramref name="column"/>, advancing
        /// <paramref name="y"/> the way the settings page's own rows do so the two
        /// halves of the column stay in one flow.
        /// </summary>
        public void Build(RectTransform column, ref float y)
        {
            // Which contexts have rows at all, in catalog order. Derived, so a new
            // rebindable action appears on the rail without anything here changing.
            // Combat leads because it is the group with the most rows and the one a
            // player arrives here for; the rest follow the catalog's own order.
            var order = new List<InputContext>();
            if (NebulaControlBindings.Actions.Count > 0) order.Add(InputContext.Combat);
            foreach (InputActionDefinition action in NebulaInputActions.All)
                if (action.Rebindable && !action.Combat.HasValue && !order.Contains(action.Context))
                    order.Add(action.Context);

            float railY = y;
            foreach (InputContext context in order)
            {
                var host = NebulaUiFactory.Group(column, GroupHostName(context));
                host.anchorMin = new Vector2(0f, 1f);
                host.anchorMax = new Vector2(0f, 1f);
                host.pivot = new Vector2(0f, 1f);
                host.anchoredPosition = new Vector2(RailWidth + NebulaUiDesignTokens.S4, y);
                host.sizeDelta = new Vector2(RowsWidth, RowsHeight);

                InputContext captured = context;
                NebulaUiFactory.ButtonHandle rail = NebulaUiFactory.ButtonControl(column,
                    GroupControlName(context), NebulaText.Upper(NebulaControlBindings.GroupName(context)),
                    NebulaUiAssetCatalog.ButtonKind.Tertiary, () => ShowGroup(captured));
                NebulaUiFactory.Place(rail.Button.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(0f, railY), new Vector2(RailWidth, NebulaUiDesignTokens.MinimumHitTarget));
                railY -= NebulaUiDesignTokens.MinimumHitTarget + NebulaUiDesignTokens.S1;

                groups.Add(new BindingGroup { Context = context, Host = host, Rail = rail });
            }

            // One row per action, from the service's own enumeration -- never a
            // hand-kept list beside it, which is how a screen ends up missing an
            // action or showing one twice.
            foreach (CombatAction action in NebulaControlBindings.Actions)
            {
                BindingGroup group = GroupFor(InputContext.Combat);
                if (group == null) break;
                float rowY = -group.RowCount * RowPitch;
                rows.Add(BuildRow(group.Host, action, ref rowY));
                group.RowCount++;
            }

            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                if (!action.Rebindable || action.Combat.HasValue) continue;
                BindingGroup group = GroupFor(action.Context);
                if (group == null) continue;
                float rowY = -group.RowCount * RowPitch;
                contextRows.Add(BuildContextRow(group.Host, action, ref rowY));
                group.RowCount++;
            }

            y -= RowsHeight + NebulaUiDesignTokens.S2;

            NebulaUiFactory.ButtonHandle reset = NebulaUiFactory.ButtonControl(column, ResetControlName,
                "VARSAYILANLARA DÖN", NebulaUiAssetCatalog.ButtonKind.Secondary, ResetToDefaults);
            NebulaUiFactory.Place(reset.Button.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(240f, NebulaUiDesignTokens.MinimumHitTarget));
            y -= NebulaUiDesignTokens.MinimumHitTarget + NebulaUiDesignTokens.S1;

            status = NebulaUiFactory.Label(column, "BindingStatus", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(status.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(RailWidth + NebulaUiDesignTokens.S4 + RowsWidth, 24f));
            y -= 24f + NebulaUiDesignTokens.S1;

            // A standing warning, not a transient one. The status line above answers
            // the last thing the player did; this answers "is anything wrong right
            // now", which is a different question and must not be overwritten by the
            // next successful rebind. It is painted in the warning colour and carries
            // no text at all when there is nothing to warn about.
            conflicts = NebulaUiFactory.Label(column, ConflictLabelName, "",
                NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Warning),
                TextAnchor.UpperLeft, FontStyle.Bold, 0f, wrap: true);
            NebulaUiFactory.Place(conflicts.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(RailWidth + NebulaUiDesignTokens.S4 + RowsWidth, 24f));
            y -= 24f + NebulaUiDesignTokens.S1;

            // What the player may NOT reassign, and why — stated rather than left as a
            // list they will look for and not find. Menu navigation and Select are read
            // by Unity's own StandaloneInputModule out of InputManager.asset, so a row
            // here would move the label and not the key; the catalog marks them
            // (InputActionDefinition.UnityAxisOwned) and this is where that mark
            // reaches the player.
            NebulaUiFactory.LabelHandle systemOwned = NebulaUiFactory.Label(column, SystemOwnedLabelName,
                SystemOwnedNote(), NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(systemOwned.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(RailWidth + NebulaUiDesignTokens.S4 + RowsWidth, 32f));
            y -= 40f;

            // The listener lives on the column, so it stops reading input the moment
            // the page it belongs to is hidden -- an armed capture must never survive
            // out of sight and swallow the next key the player presses somewhere else.
            listener = column.gameObject.AddComponent<NebulaKeyCaptureListener>();
            listener.Initialize(Apply, CancelCapture);

            if (groups.Count > 0) ShowGroup(groups[0].Context);
            Refresh();
        }

        // ---- geometry --------------------------------------------------------
        // Named rather than inline so the containment test can measure against the
        // same numbers the layout is built from, and so "the rows fit the panel" is
        // arithmetic rather than a hope.

        /// <summary>Width of the group rail, wide enough for the longest Turkish group name.</summary>
        public const float RailWidth = 184f;

        /// <summary>Width of the row area beside the rail.</summary>
        public const float RowsWidth = 648f;

        /// <summary>Vertical pitch of one binding row.</summary>
        public const float RowPitch = NebulaUiDesignTokens.RowHeight;

        /// <summary>Height reserved for the rows of the largest group.</summary>
        public const float RowsHeight = 9f * RowPitch;

        /// <summary>The name of the label naming the actions the player may not reassign.</summary>
        public const string SystemOwnedLabelName = "SystemOwnedBindings";

        private BindingGroup GroupFor(InputContext context)
        {
            for (int i = 0; i < groups.Count; i++)
                if (groups[i].Context == context) return groups[i];
            return groups.Count > 0 ? groups[0] : null;
        }

        /// <summary>Test-facing: the sentence naming the Unity-axis-owned actions.</summary>
        public string SystemOwnedText =>
            groups.Count == 0 ? null : SystemOwnedNote();

        private static string SystemOwnedNote()
        {
            var names = new List<string>();
            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                if (!action.UnityAxisOwned) continue;
                names.Add(action.Key == KeyCode.None
                    ? NebulaText.Upper(action.LabelTr)
                    : $"{NebulaText.Upper(action.LabelTr)} ({NebulaControlBindings.KeyLabel(action.Key)})");
            }
            return names.Count == 0
                ? string.Empty
                : "SİSTEM TARAFINDAN YÖNETİLEN VE DEĞİŞTİRİLEMEYEN TUŞLAR — "
                  + string.Join("  ·  ", names);
        }

        private ContextBindingRow BuildContextRow(RectTransform column, InputActionDefinition action, ref float y)
        {
            var row = new ContextBindingRow { ActionId = action.Id };
            Image panel = NebulaUiFactory.Panel(column, "Bind " + action.Id,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(0f, NebulaUiDesignTokens.RowHeight);

            NebulaUiFactory.Place(NebulaUiFactory.Label(rect, "Label", action.LabelTr,
                    NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft)
                .Text.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(230f, 20f));

            row.Value = NebulaUiFactory.Value(rect, "Value", "", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(row.Value.Text.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-190f, 0f), new Vector2(150f, 18f));

            string captured = action.Id;
            row.Rebind = NebulaUiFactory.ButtonControl(rect, RebindControlName, "ATA",
                NebulaUiAssetCatalog.ButtonKind.Secondary, () => BeginCapture(captured));
            NebulaUiFactory.Place(row.Rebind.Button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(-100f, 0f), new Vector2(72f, NebulaUiDesignTokens.MinimumHitTarget));

            row.Cancel = NebulaUiFactory.ButtonControl(rect, CancelControlName, "VAZGEÇ",
                NebulaUiAssetCatalog.ButtonKind.Tertiary, CancelCapture);
            NebulaUiFactory.Place(row.Cancel.Button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f), new Vector2(76f, NebulaUiDesignTokens.MinimumHitTarget));
            row.Cancel.Button.gameObject.SetActive(false);
            y -= NebulaUiDesignTokens.RowHeight + NebulaUiDesignTokens.S2;
            return row;
        }

        private BindingRow BuildRow(RectTransform column, CombatAction action, ref float y)
        {
            var row = new BindingRow { Action = action };

            Image panel = NebulaUiFactory.Panel(column, RowName(action),
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(0f, NebulaUiDesignTokens.RowHeight);

            NebulaUiFactory.Place(NebulaUiFactory.Label(rect, "Label",
                    NebulaControlBindings.ActionName(action), NebulaUiDesignTokens.SizeBody,
                    NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft)
                .Text.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(230f, 20f));

            row.Value = NebulaUiFactory.Value(rect, "Value", "", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(row.Value.Text.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-190f, 0f), new Vector2(150f, 18f));

            CombatAction captured = action;
            row.Rebind = NebulaUiFactory.ButtonControl(rect, RebindControlName, "ATA",
                NebulaUiAssetCatalog.ButtonKind.Secondary, () => BeginCapture(captured));
            NebulaUiFactory.Place(row.Rebind.Button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(-100f, 0f), new Vector2(72f, NebulaUiDesignTokens.MinimumHitTarget));

            row.Cancel = NebulaUiFactory.ButtonControl(rect, CancelControlName, "VAZGEÇ",
                NebulaUiAssetCatalog.ButtonKind.Tertiary, CancelCapture);
            NebulaUiFactory.Place(row.Cancel.Button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f), new Vector2(76f, NebulaUiDesignTokens.MinimumHitTarget));
            row.Cancel.Button.gameObject.SetActive(false);

            y -= NebulaUiDesignTokens.RowHeight + NebulaUiDesignTokens.S2;
            return row;
        }

        // ---- capture ---------------------------------------------------------

        /// <summary>
        /// The key the prompt tells the player to press to abandon a capture, read
        /// from the <c>rebind.cancel</c> row rather than typed here.
        /// <para>
        /// The prompt used to say <c>ESC</c> as a literal while the row said what
        /// the key was, which is two places for one fact. The row is now the one
        /// place: <see cref="NebulaSettingsPage.HandleBack"/> is what carries the
        /// press out (the listener does not read it — see <see cref="Candidates"/>),
        /// and this is what the player is told.
        /// </para>
        /// </summary>
        private static string CancelPrompt =>
            NebulaControlBindings.KeyLabel(NebulaInputActions.Key(NebulaInputActions.RebindCancel));

        /// <summary>Arms the capture for one action. Arming a second one abandons the first.</summary>
        public void BeginCapture(CombatAction action)
        {
            Capturing = action;
            CapturingActionId = NebulaInputActions.ForCombat(action)?.Id;
            listener?.Arm();
            Report($"{NebulaText.Upper(NebulaControlBindings.ActionName(action))} İÇİN BİR TUŞA BASIN — {CancelPrompt} İPTAL");
            Refresh();
        }

        public void BeginCapture(string actionId)
        {
            InputActionDefinition action = NebulaInputActions.Find(actionId);
            if (action == null || !action.Rebindable) return;
            Capturing = action.Combat;
            CapturingActionId = action.Id;
            listener?.Arm();
            Report($"{NebulaText.Upper(action.LabelTr)} İÇİN BİR TUŞA BASIN — {CancelPrompt} İPTAL");
            Refresh();
        }

        /// <summary>
        /// Abandons an armed capture. The binding is untouched — this is the path a
        /// player takes when they change their mind, and it must be indistinguishable
        /// from never having pressed the control.
        /// </summary>
        public void CancelCapture()
        {
            if (CapturingActionId == null) return;
            Capturing = null;
            CapturingActionId = null;
            listener?.Disarm();
            Report("DEĞİŞİKLİK YAPILMADI");
            Refresh();
        }

        /// <summary>
        /// The captured key. This is the one seam the listener's <c>Update</c> calls,
        /// so a test that drives it is driving the production path rather than a
        /// test-only shortcut.
        /// </summary>
        public ControlRebindResult Apply(KeyCode key)
        {
            if (CapturingActionId == null) return ControlRebindResult.Refused;

            InputActionDefinition definition = NebulaInputActions.Find(CapturingActionId);
            CombatAction? action = Capturing;
            ControlRebindResult result;
            if (action.HasValue)
                result = NebulaControlBindings.TryRebind(action.Value, key);
            else
            {
                bool accepted = definition != null && NebulaInputActions.TrySetBinding(definition.Id, key);
                result = accepted ? ControlRebindResult.Accepted : ControlRebindResult.Refused;
                if (accepted) NebulaInputBindingStore.Save();
            }

            // A refusal keeps the capture armed. The player asked to change this key
            // and pressing a reserved one by accident should not silently drop them
            // back out of the mode they deliberately entered.
            if (result == ControlRebindResult.Accepted)
            {
                Capturing = null;
                CapturingActionId = null;
                listener?.Disarm();
            }

            Report(action.HasValue
                ? NebulaControlBindings.ResultText(result, action.Value, key)
                : result == ControlRebindResult.Accepted
                    ? $"{NebulaText.Upper(definition.LabelTr)} → {NebulaControlBindings.KeyLabel(key)}"
                    : "BU TUŞ BAŞKA BİR CANLI EYLEMLE ÇAKIŞIYOR");
            Refresh();
            return result;
        }

        private void ResetToDefaults()
        {
            Capturing = null;
            CapturingActionId = null;
            listener?.Disarm();
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputBindingStore.Clear();
            Report("TUŞLAR VARSAYILANLARA DÖNDÜ");
            Refresh();
        }

        private void Report(string message)
        {
            if (status == null) return;
            status.Value = message ?? string.Empty;
        }

        /// <summary>Test-facing: the standing conflict warning, or empty when there is none.</summary>
        public string ConflictText => conflicts?.Text != null ? conflicts.Text.text : null;

        /// <summary>Re-reads every binding from the service and repaints the rows.</summary>
        public void Refresh()
        {
            if (conflicts != null)
            {
                IReadOnlyList<string> warnings = NebulaControlBindings.ConflictWarnings();
                conflicts.Value = warnings.Count == 0
                    ? string.Empty
                    : "AYNI TUŞ İKİ İŞLEVDE — " + string.Join("  ·  ", warnings);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                BindingRow row = rows[i];
                bool armed = CapturingActionId == NebulaInputActions.ForCombat(row.Action)?.Id;

                row.Value.Value = armed
                    ? "…"
                    : NebulaControlBindings.KeyLabel(NebulaControlBindings.KeyFor(row.Action));

                // While one row is armed the others are unavailable rather than
                // hidden, and they say why: two armed rows would race for the same
                // key press.
                bool other = CapturingActionId != null && !armed;
                row.Rebind.SetAvailable(!other, "ÖNCE AÇIK ATAMAYI BİTİRİN");
                row.Rebind.SetCaption(armed ? "BEKLENİYOR" : "ATA");

                if (row.Cancel.Button.gameObject.activeSelf != armed)
                    row.Cancel.Button.gameObject.SetActive(armed);
            }


            for (int i = 0; i < contextRows.Count; i++)
            {
                ContextBindingRow row = contextRows[i];
                bool armed = CapturingActionId == row.ActionId;
                row.Value.Value = armed ? "…" : NebulaControlBindings.KeyLabel(NebulaInputActions.Key(row.ActionId));
                bool other = CapturingActionId != null && !armed;
                row.Rebind.SetAvailable(!other, "ÖNCE AÇIK ATAMAYI BİTİRİN");
                row.Rebind.SetCaption(armed ? "BEKLENİYOR" : "ATA");
                if (row.Cancel.Button.gameObject.activeSelf != armed)
                    row.Cancel.Button.gameObject.SetActive(armed);
            }

            // The rail says which group is showing and refuses to move while a capture
            // is armed — leaving the armed row is what would hide a listener that is
            // still reading, and the control says so rather than doing nothing.
            for (int i = 0; i < groups.Count; i++)
            {
                BindingGroup group = groups[i];
                bool selected = SelectedGroup.HasValue && SelectedGroup.Value == group.Context;
                group.Rail.SetAvailable(CapturingActionId == null && !selected,
                    CapturingActionId != null ? "ÖNCE AÇIK ATAMAYI BİTİRİN" : "ZATEN AÇIK");
                group.Rail.SetCaption(NebulaText.Upper(NebulaControlBindings.GroupName(group.Context)));
            }
        }
    }

    /// <summary>
    /// Where a physical key-down sample comes from.
    /// <para>
    /// One method, deliberately: this is the sampling of hardware and nothing else.
    /// It carries no notion of a binding, an action, a capture or a refusal, so
    /// substituting it can move the <i>input</i> and cannot move the outcome — every
    /// rule about what may be bound stays on the far side of it, in the capture state
    /// machine and the binding validator, and runs identically whichever
    /// implementation is installed.
    /// </para>
    /// </summary>
    public interface IKeyInputSource
    {
        /// <summary>True on the frame this key went down.</summary>
        bool GetKeyDown(KeyCode key);
    }

    /// <summary>
    /// The shipping implementation: legacy <c>UnityEngine.Input</c>, straight through.
    /// </summary>
    public sealed class LegacyUnityKeyInputSource : IKeyInputSource
    {
        public bool GetKeyDown(KeyCode key) => Input.GetKeyDown(key);
    }

    /// <summary>
    /// Reads the key the player presses while a rebind is armed.
    /// <para>
    /// It exists because the settings page is a plain class with no <c>Update</c> of
    /// its own, and a capture has to be a real frame-by-frame read of real input.
    /// The whole enum is scanned rather than a curated candidate list: the point of
    /// rebinding is that the player chooses, and a hand-written list of "keys we
    /// thought of" is the same defect as no rebinding at all.
    /// </para>
    /// <para>
    /// It scans only while armed. An always-on scan of four hundred key codes every
    /// frame is not something a settings screen should cost.
    /// </para>
    /// <para>
    /// The sample itself comes through <see cref="Source"/> rather than from
    /// <c>Input</c> directly. Legacy <c>Input</c> cannot be driven from a batch-mode
    /// test, which left the one link nobody could prove — that a physical key press
    /// reaches the capture at all — resting on a method call the test made itself.
    /// Everything the capture does with the key it is handed is unchanged and still
    /// runs here: Escape still cancels before anything else is considered, the same
    /// enumeration is still walked, and the same <c>Apply</c> is still what decides
    /// whether the press is taken.
    /// </para>
    /// </summary>
    public sealed class NebulaKeyCaptureListener : MonoBehaviour
    {
        private static KeyCode[] candidates;

        private static IKeyInputSource source = new LegacyUnityKeyInputSource();

        /// <summary>
        /// Where key-down samples are read from. Defaults to the real keyboard and is
        /// never <c>null</c>: assigning <c>null</c> restores the shipping source
        /// rather than leaving a listener that silently reads nothing.
        /// </summary>
        public static IKeyInputSource Source
        {
            get => source;
            set => source = value ?? new LegacyUnityKeyInputSource();
        }

        /// <summary>Puts the real keyboard back.</summary>
        public static void ResetSourceToKeyboard() => source = new LegacyUnityKeyInputSource();

        private Func<KeyCode, ControlRebindResult> onCaptured;
        private Action onCancelled;

        /// <summary>True while a key press would be taken as a rebind.</summary>
        public bool Armed { get; private set; }

        public void Initialize(Func<KeyCode, ControlRebindResult> captured, Action cancelled)
        {
            onCaptured = captured;
            onCancelled = cancelled;
        }

        public void Arm() => Armed = true;

        public void Disarm() => Armed = false;

        /// <summary>An armed capture must not survive the page being hidden.</summary>
        private void OnDisable()
        {
            if (!Armed) return;
            Armed = false;
            onCancelled?.Invoke();
        }

        private void Update()
        {
            if (!Armed) return;

            IKeyInputSource keyboard = Source;

            KeyCode[] keys = Candidates();
            for (int i = 0; i < keys.Length; i++)
            {
                if (!keyboard.GetKeyDown(keys[i])) continue;
                onCaptured?.Invoke(keys[i]);
                return;
            }
        }

        private static KeyCode[] Candidates()
        {
            if (candidates != null) return candidates;

            var list = new List<KeyCode>(512);
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (key == KeyCode.None) continue;

                // Escape is not this listener's to see at all — neither as a binding
                // nor as a cancel.
                //
                // It used to be both. The listener consumed it as "abandon the
                // capture" while the screen stack consumed the same press as
                // "back", so one Escape closed two levels: the capture AND the whole
                // rebinding panel underneath it. Two consumers, one press.
                //
                // The screen stack keeps it, because that is the surface Escape
                // already belongs to everywhere else in the interface, and the
                // settings page now unwinds exactly one level per press
                // (NebulaSettingsPage.HandleBack: armed capture first, panel second).
                // Nothing about the player's experience changes except that the panel
                // stops disappearing out from under them.
                if (key == KeyCode.Escape) continue;

                list.Add(key);
            }

            candidates = list.ToArray();
            return candidates;
        }
    }
}
