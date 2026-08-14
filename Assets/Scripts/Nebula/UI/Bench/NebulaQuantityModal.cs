using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.Ship;

namespace Nebula.UI.Bench
{
    /// <summary>Which of the bench's three transactions the modal is collecting a number for.</summary>
    public enum QuantityMode
    {
        /// <summary>Cargo → bench. Step 1, minimum 1, ceiling is the hold's whole units.</summary>
        Deposit,

        /// <summary>Bench → output. Step and minimum are the recipe's input count.</summary>
        Convert,

        /// <summary>Bench → cargo. Step 1, minimum 1, ceiling is what is on the bench.</summary>
        Withdraw,
    }

    /// <summary>
    /// The one place the interface asks "how many?".
    /// <para>
    /// One modal for all three transactions rather than three dialogs, because the
    /// question is the same question and the differences are three numbers — step,
    /// minimum and ceiling — plus an optional preview line. Three copies would be
    /// three chances for the deposit box and the conversion box to disagree about
    /// what an empty field means.
    /// </para>
    /// <para>
    /// <b>It performs nothing.</b> The modal collects an integer and hands it to a
    /// callback; every mutation happens in <see cref="Nebula.Extraction.ProcessingBenchCommands"/>
    /// behind that callback. Cancelling is therefore zero mutation by construction
    /// rather than by remembering to undo something, and the same is true of the
    /// modal being closed by the page going away underneath it.
    /// </para>
    /// <para>
    /// The card sits inside a <see cref="ScrollRect"/>. At 140% interface scale the
    /// shell expresses the scale by shrinking the canvas's reference resolution, so
    /// a 1280x720 viewport has markedly fewer logical pixels to spend — a fixed
    /// card that fits at 100% can be taller than the page at 140%. Scrolling makes
    /// every control reachable at every supported combination instead of clipping
    /// the confirm button off the bottom edge.
    /// </para>
    /// </summary>
    public sealed class NebulaQuantityModal : MonoBehaviour
    {
        /// <summary>The card's authored size, in reference pixels.</summary>
        public const float CardWidth = 560f;

        /// <inheritdoc cref="CardWidth"/>
        public const float CardHeight = 430f;

        /// <summary>Space kept above the card when the viewport is too short to centre it.</summary>
        private const float TopPadding = 32f;

        private RectTransform root;
        private RectTransform viewport;
        private RectTransform content;
        private Image itemIcon;
        private NebulaUiFactory.LabelHandle title, itemName, holdings, preview, warning;
        private TMP_InputField field;
        private NebulaUiFactory.ButtonHandle minusButton, plusButton, oneButton, maxButton;
        private NebulaUiFactory.ButtonHandle confirmButton, cancelButton;

        private Action<int> onConfirm;
        private Func<int, string> previewFor;

        /// <summary>Guards against one press being turned into two transactions.</summary>
        private bool closing;

        // =====================================================================
        // STATE
        // =====================================================================

        /// <summary>
        /// Whether the box is up and awaiting an answer.
        /// <para>
        /// Derived from the object's own activation rather than tracked beside it. The
        /// flag alone was wrong in exactly the case that matters: <c>OnDisable</c> is
        /// the only thing that cleared it, and Unity does not call <c>OnDisable</c> for
        /// an object that was never active in the hierarchy — so a box built under an
        /// inactive host, or one whose whole page is deactivated in a single
        /// <c>SetActive(false)</c> on a parent, went on reporting itself open forever.
        /// A page that believes a modal is still up refuses every later interaction,
        /// and nothing about the screen would say why.
        /// </para>
        /// </summary>
        public bool IsOpen => openRequested && root != null && root.gameObject.activeSelf;

        private bool openRequested;

        public QuantityMode Mode { get; private set; }

        /// <summary>The item the number is about. Read by the page, never by the backend.</summary>
        public string ItemId { get; private set; }

        public int Min { get; private set; } = 1;
        public int Max { get; private set; } = 1;
        public int Step { get; private set; } = 1;

        /// <summary>Test-facing: how many confirmations actually ran a callback.</summary>
        public int ConfirmedCount { get; private set; }

        /// <summary>Test-facing: how many times the player backed out.</summary>
        public int CancelledCount { get; private set; }

        /// <summary>Exactly what is in the box, before any parsing.</summary>
        public string RawText => field == null ? string.Empty : field.text;

        /// <summary>
        /// The number in the box, or <c>0</c> when there is not one.
        /// <para>
        /// Parsed on read rather than tracked alongside the text, so the box and the
        /// number cannot drift: the field is the only place the value lives.
        /// </para>
        /// </summary>
        public int Value => TryParse(RawText, out int parsed) ? parsed : 0;

        /// <summary>
        /// Whether the box currently holds a number this transaction would accept.
        /// An empty box is not a zero and never confirms.
        /// </summary>
        public bool HasValidValue =>
            TryParse(RawText, out int parsed)
            && parsed >= Min && parsed <= Max
            && Step > 0 && parsed % Step == 0;

        /// <summary>Test-facing: why the confirm control is unavailable, or <c>null</c>.</summary>
        public string ConfirmBlockedReason => confirmButton?.DisabledReason;

        public string TitleText => title == null ? string.Empty : title.Value;
        public string HoldingsText => holdings == null ? string.Empty : holdings.Value;
        public string PreviewText => preview == null ? string.Empty : preview.Value;
        public string WarningText =>
            warning != null && warning.Text.gameObject.activeSelf ? warning.Value : string.Empty;

        public TMP_InputField Field => field;
        public Button MinusButton => minusButton?.Button;
        public Button PlusButton => plusButton?.Button;
        public Button OneButton => oneButton?.Button;
        public Button MaxButton => maxButton?.Button;
        public Button ConfirmButton => confirmButton?.Button;
        public Button CancelButton => cancelButton?.Button;

        /// <summary>The scrolling frame the card lives in, so a fixture can measure clipping.</summary>
        public RectTransform Viewport => viewport;

        /// <summary>The card itself.</summary>
        public RectTransform Card => content;

        // =====================================================================
        // BUILD
        // =====================================================================

        /// <summary>
        /// Builds the modal into a page's own host and leaves it closed.
        /// <para>
        /// Inside the page rather than on the shell's stack on purpose: the page is
        /// what owns the transaction, and a modal parented into the page is torn
        /// down, hidden and re-shown with it. A shell screen would outlive the page
        /// being paged away from and would need its own reconciliation.
        /// </para>
        /// </summary>
        public static NebulaQuantityModal Create(RectTransform host, string name = "Quantity Modal")
        {
            RectTransform rect = NebulaUiFactory.Group(host, name);
            NebulaUiFactory.Fill(rect);
            var modal = rect.gameObject.AddComponent<NebulaQuantityModal>();
            modal.Build(rect);
            return modal;
        }

        private void Build(RectTransform rect)
        {
            root = rect;

            // The scrim is what stops the page underneath from answering a pointer.
            // Every control on this page is a raycast target, so without a blocker
            // in front of them a click meant for the modal's Confirm would also be
            // a click on whatever card happened to be behind it.
            Image scrim = NebulaUiFactory.Rect(root, "Scrim", NebulaUiDesignTokens.OverlayDim);
            NebulaUiFactory.Fill(scrim.rectTransform);
            scrim.raycastTarget = true;

            viewport = NebulaUiFactory.Group(root, "Viewport");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(16f, 16f);
            viewport.offsetMax = new Vector2(-16f, -16f);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = NebulaUiFactory.Group(viewport, "Content");
            content.anchorMin = new Vector2(0.5f, 1f);
            content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = new Vector2(0f, -TopPadding);
            content.sizeDelta = new Vector2(CardWidth, CardHeight);

            var scroller = viewport.gameObject.AddComponent<ScrollRect>();
            scroller.viewport = viewport;
            scroller.content = content;
            scroller.horizontal = false;
            scroller.vertical = true;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            scroller.scrollSensitivity = 24f;
            scroller.inertia = false;

            BuildCard();
            root.gameObject.SetActive(false);
        }

        private void BuildCard()
        {
            Image panel = NebulaUiFactory.Panel(content, "Card", NebulaUiAssetCatalog.Modal);
            NebulaUiFactory.Fill(panel.rectTransform);
            RectTransform card = panel.rectTransform;
            // The card eats the pointer as well as the scrim: a click that lands on
            // the card's own background must not fall through to the page behind it.
            panel.raycastTarget = true;

            title = NebulaUiFactory.Label(card, "Title", "", NebulaUiDesignTokens.SizeScreenTitle,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingScreenTitle);
            NebulaUiFactory.Place(title.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(28f, -24f), new Vector2(CardWidth - 56f, 28f));

            itemIcon = NebulaUiFactory.Icon(card, "Icon", null, 28f);
            NebulaUiFactory.Place(itemIcon.rectTransform, new Vector2(0f, 1f),
                new Vector2(28f, -66f), new Vector2(28f, 28f));

            itemName = NebulaUiFactory.Label(card, "Item", "", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            NebulaUiFactory.Place(itemName.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(66f, -70f), new Vector2(CardWidth - 94f, 20f));

            holdings = NebulaUiFactory.Label(card, "Holdings", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(holdings.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(28f, -104f), new Vector2(CardWidth - 56f, 16f));

            BuildField(card);
            BuildSteppers(card);

            preview = NebulaUiFactory.Label(card, "Preview", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption, wrap: true);
            NebulaUiFactory.Place(preview.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(28f, -258f), new Vector2(CardWidth - 56f, 40f));

            warning = NebulaUiFactory.Label(card, "Warning", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Caution), TextAnchor.UpperLeft,
                FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            NebulaUiFactory.Place(warning.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(28f, -306f), new Vector2(CardWidth - 56f, 34f));
            warning.Text.gameObject.SetActive(false);

            cancelButton = NebulaUiFactory.ButtonControl(card, "Cancel", "İPTAL",
                NebulaUiAssetCatalog.ButtonKind.Secondary, Cancel);
            NebulaUiFactory.Place(cancelButton.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(28f, 26f), new Vector2(200f, 44f));

            confirmButton = NebulaUiFactory.ButtonControl(card, "Confirm", "ONAYLA",
                NebulaUiAssetCatalog.ButtonKind.Primary, Confirm);
            NebulaUiFactory.Place(confirmButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-28f, 26f), new Vector2(240f, 44f));
        }

        /// <summary>
        /// The number box.
        /// <para>
        /// <see cref="TMP_InputField.CharacterValidation.Digit"/> rather than a
        /// parse-and-scold: a minus sign, a decimal point and an exponent are all
        /// things the backend would refuse as <c>InvalidAmount</c>, and a control
        /// that cannot produce a refusal is better than one that explains it.
        /// </para>
        /// </summary>
        private void BuildField(RectTransform card)
        {
            Image background = NebulaUiFactory.Panel(card, "Amount",
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            RectTransform rect = background.rectTransform;
            NebulaUiFactory.Place(rect, new Vector2(0f, 1f), new Vector2(28f, -132f),
                new Vector2(200f, 46f));
            background.raycastTarget = true;

            RectTransform area = NebulaUiFactory.Group(rect, "Text Area");
            NebulaUiFactory.Fill(area, 8f);
            area.gameObject.AddComponent<RectMask2D>();

            NebulaUiFactory.LabelHandle text = NebulaUiFactory.Label(area, "Text", "",
                NebulaUiDesignTokens.SizeValue, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.MiddleLeft);
            NebulaUiFactory.Fill(text.Text.rectTransform);

            field = rect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text.Text;
            field.targetGraphic = background;
            field.transition = Selectable.Transition.None;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.contentType = TMP_InputField.ContentType.Custom;
            field.inputType = TMP_InputField.InputType.Standard;
            field.characterValidation = TMP_InputField.CharacterValidation.Digit;
            field.keyboardType = TouchScreenKeyboardType.NumberPad;
            field.characterLimit = 10;
            field.restoreOriginalTextOnEscape = false;
            field.onValueChanged.AddListener(_ => Repaint());
            // Enter, from inside the box. The confirm button answers Submit on its
            // own once it has focus; this is the case where the player has just
            // finished typing and never left the field.
            field.onSubmit.AddListener(_ => Confirm());
        }

        private void BuildSteppers(RectTransform card)
        {
            minusButton = Stepper(card, "Minus", "−", new Vector2(240f, -132f), () => Nudge(-1));
            plusButton = Stepper(card, "Plus", "+", new Vector2(300f, -132f), () => Nudge(1));
            oneButton = Stepper(card, "One", "1", new Vector2(374f, -132f), () => SetValue(Step), 74f);
            maxButton = Stepper(card, "Max", "MAKS", new Vector2(456f, -132f), () => SetValue(Max), 76f);
        }

        private NebulaUiFactory.ButtonHandle Stepper(RectTransform card, string name, string caption,
            Vector2 offset, UnityEngine.Events.UnityAction action, float width = 52f)
        {
            NebulaUiFactory.ButtonHandle handle = NebulaUiFactory.ButtonControl(card, name, caption,
                NebulaUiAssetCatalog.ButtonKind.Secondary, action);
            NebulaUiFactory.Place(handle.Button.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                offset, new Vector2(width, 46f));
            return handle;
        }

        // =====================================================================
        // OPEN / CLOSE
        // =====================================================================

        /// <summary>
        /// Asks for a number. <paramref name="previewBuilder"/> turns a candidate
        /// amount into the sentence under the box; pass <c>null</c> when there is
        /// nothing to preview.
        /// <para>
        /// Returns false when a modal is already open, so a second drop landing in
        /// the same frame cannot stack two questions over one bench.
        /// </para>
        /// </summary>
        public bool Open(QuantityMode mode, string heading, string itemId, string holdingsLine,
            int min, int max, int step, Func<int, string> previewBuilder, Action<int> confirmed)
        {
            if (IsOpen) return false;

            Mode = mode;
            ItemId = itemId;
            Step = Mathf.Max(1, step);
            Min = Mathf.Max(Step, min);
            Max = Mathf.Max(0, max);
            previewFor = previewBuilder;
            onConfirm = confirmed;
            closing = false;

            title.Value = heading;
            itemName.Value = NebulaBenchMessages.ItemName(itemId);
            holdings.Value = holdingsLine ?? string.Empty;

            Sprite sprite = NebulaIconResolver.Item(itemId);
            itemIcon.sprite = sprite;
            itemIcon.enabled = sprite != null;

            // Opens on the largest amount the transaction can actually take rather
            // than on the minimum: "convert what is on the bench" is the common
            // case, and a box that starts there costs no keystrokes at all.
            SetValue(Max >= Min ? Max : Min);

            openRequested = true;
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            Repaint();
            FocusField();
            return true;
        }

        /// <summary>
        /// Re-states the ceiling from live backend state, clamping what is in the
        /// box down to it. Called by the page on every refresh: the hold can lose
        /// stock underneath an open box, and a confirm against a stale ceiling
        /// would be a refusal the player was invited to make.
        /// </summary>
        public void SetLimits(int min, int max)
        {
            if (!IsOpen) return;
            Min = Mathf.Max(Step, min);
            Max = Mathf.Max(0, max);
            if (TryParse(RawText, out int current) && current > Max) SetValue(Max);
            else Repaint();
        }

        /// <summary>Applies the number. Public so a fixture presses what a player presses.</summary>
        public void Confirm()
        {
            // Guarded twice over. The button is non-interactable while the value is
            // invalid, and `closing` stops a second Confirm arriving inside the
            // callback -- a confirm that opens something of its own would otherwise
            // find this modal still nominally open.
            if (!IsOpen || closing || !HasValidValue) return;

            closing = true;
            int amount = Value;
            Action<int> callback = onConfirm;
            onConfirm = null;
            ConfirmedCount++;
            Close();
            callback?.Invoke(amount);
        }

        /// <summary>Backs out. Zero mutation, by construction: nothing was applied yet.</summary>
        public void Cancel()
        {
            if (!IsOpen || closing) return;
            closing = true;
            onConfirm = null;
            CancelledCount++;
            Close();
        }

        private void Close()
        {
            openRequested = false;
            onConfirm = null;
            previewFor = null;
            if (root != null) root.gameObject.SetActive(false);
            closing = false;
        }

        /// <summary>
        /// The page going away closes the box.
        /// <para>
        /// Silently, and without running the callback: the box has applied nothing,
        /// so leaving it is exactly as free as cancelling it. The counters are left
        /// alone because nobody cancelled anything — the page was paged away from or
        /// the menu was closed.
        /// </para>
        /// </summary>
        private void OnDisable()
        {
            openRequested = false;
            onConfirm = null;
            previewFor = null;
            closing = false;
        }

        // =====================================================================
        // VALUE
        // =====================================================================

        /// <summary>
        /// Digits only, and only a value the machine can hold.
        /// <para>
        /// Leading zeros parse rather than being rejected — <c>007</c> is a player
        /// typing seven, not an error — and are normalised out of the box the next
        /// time anything writes to it. Anything longer than the box's limit or past
        /// the integer range fails here rather than wrapping, which is how a
        /// "quantity" becomes a negative number.
        /// </para>
        /// </summary>
        private static bool TryParse(string raw, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(raw)) return false;
            for (int i = 0; i < raw.Length; i++)
                if (raw[i] < '0' || raw[i] > '9') return false;
            return int.TryParse(raw, out value);
        }

        /// <summary>Writes a normalised amount into the box, clamped to the transaction's own range.</summary>
        public void SetValue(int amount)
        {
            int clamped = Clamp(amount);
            string normalised = clamped.ToString();
            if (!string.Equals(field.text, normalised, StringComparison.Ordinal))
                field.text = normalised;      // fires onValueChanged -> Repaint
            else Repaint();
        }

        /// <summary>Steps by whole multiples of the recipe's own step.</summary>
        public void Nudge(int steps)
        {
            int start = TryParse(RawText, out int parsed) ? parsed : Min;
            SetValue(start + steps * Step);
        }

        /// <summary>
        /// The nearest amount this transaction would accept.
        /// <para>
        /// Rounded <i>down</i> to a whole step, never up: rounding up would spend
        /// input the player did not agree to spend, which is the same rule the
        /// backend enforces with <c>AmountNotMultipleOfRecipeInput</c>.
        /// </para>
        /// </summary>
        private int Clamp(int amount)
        {
            int ceiling = Mathf.Max(Min, Max);
            int clamped = Mathf.Clamp(amount, Min, ceiling);
            if (Step > 1) clamped = clamped / Step * Step;
            return Mathf.Max(Step, clamped);
        }

        // =====================================================================
        // PAINT
        // =====================================================================

        private void Repaint()
        {
            if (!IsOpen) return;

            bool valid = HasValidValue;
            bool empty = string.IsNullOrEmpty(RawText);

            preview.Value = previewFor != null && valid ? previewFor(Value) : string.Empty;

            string note =
                empty ? "BİR MİKTAR GİR"
                : !TryParse(RawText, out int parsed) ? "YALNIZ TAM SAYI"
                : Max < Min ? "UYGUN MİKTAR YOK"
                : parsed < Min ? $"EN AZ {Min}"
                : parsed > Max ? $"EN FAZLA {Max}"
                : Step > 1 && parsed % Step != 0 ? $"MİKTAR {NebulaBenchMessages.Genitive(Step)} TAM KATI OLMALI"
                : null;

            bool showNote = note != null;
            if (warning.Text.gameObject.activeSelf != showNote)
                warning.Text.gameObject.SetActive(showNote);
            if (showNote) warning.Value = note;

            confirmButton.SetAvailable(valid, note ?? "GEÇERSİZ MİKTAR");
            minusButton.SetAvailable(Max >= Min, "UYGUN MİKTAR YOK");
            plusButton.SetAvailable(Max >= Min, "UYGUN MİKTAR YOK");
            oneButton.SetAvailable(Max >= Min, "UYGUN MİKTAR YOK");
            maxButton.SetAvailable(Max >= Min, "UYGUN MİKTAR YOK");

            // NOTE for whoever adopts NebulaUiFactory.ButtonHandle.KeepRefusalReachableByFocus
            // here: this call has to move ABOVE the block above first. A stand-in copies
            // the navigation of the control it replaces at the moment it appears, and
            // the box's explicit ring is what keeps focus from walking out of it.
            WireNavigation();
        }

        /// <summary>
        /// An explicit ring through the box's own controls.
        /// <para>
        /// Explicit rather than automatic because the modal is drawn <i>over</i> the
        /// page: Unity's directional search does not know about the scrim and would
        /// happily walk the stick out of the dialog and onto a cargo row that cannot
        /// be clicked. The ring ends at Cancel and starts again at the box, so there
        /// is no direction that leaves.
        /// </para>
        /// </summary>
        private void WireNavigation()
        {
            Link(field, up: cancelButton.Button, down: confirmButton.Button,
                left: maxButton.Button, right: minusButton.Button);
            Link(minusButton.Button, up: cancelButton.Button, down: confirmButton.Button,
                left: field, right: plusButton.Button);
            Link(plusButton.Button, up: cancelButton.Button, down: confirmButton.Button,
                left: minusButton.Button, right: oneButton.Button);
            Link(oneButton.Button, up: cancelButton.Button, down: confirmButton.Button,
                left: plusButton.Button, right: maxButton.Button);
            Link(maxButton.Button, up: cancelButton.Button, down: confirmButton.Button,
                left: oneButton.Button, right: field);
            Link(cancelButton.Button, up: field, down: field,
                left: confirmButton.Button, right: confirmButton.Button);
            Link(confirmButton.Button, up: field, down: field,
                left: cancelButton.Button, right: cancelButton.Button);
        }

        private static void Link(Selectable target, Selectable up, Selectable down,
            Selectable left, Selectable right)
        {
            if (target == null) return;
            target.navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right,
            };
        }

        /// <summary>Puts the keyboard on the box, so typing a number needs no click first.</summary>
        public void FocusField()
        {
            EventSystem events = EventSystem.current;
            if (events == null || field == null || !field.gameObject.activeInHierarchy) return;
            events.SetSelectedGameObject(field.gameObject);
        }

        /// <summary>
        /// The two things a pad can do that a mouse cannot reach: step the value
        /// with the shoulders, and centre the card once the layout is known.
        /// <para>
        /// The shoulders rather than the horizontal d-pad, deliberately. While the
        /// box has focus the left and right keys belong to the caret; binding a
        /// step to them would make one press do two things and would move the caret
        /// under the player's own edit. The on-screen <c>−</c>/<c>+</c> stay the
        /// primary path for both a pad and a mouse.
        /// </para>
        /// </summary>
        private void Update()
        {
            if (!IsOpen) return;

            // Through the router on the catalog's own gamepad rows rather than two
            // KeyCode literals. The rows existed (P1-DATA-001 closed by adding them);
            // this is the runtime finally reading them, so the catalog and the game
            // agree about which buttons step the amount.
            if (NebulaInputRouter.Pressed(NebulaInputActions.MenuQuantityDecrease)) Nudge(-1);
            else if (NebulaInputRouter.Pressed(NebulaInputActions.MenuQuantityIncrease)) Nudge(1);

            CentreCard();
        }

        /// <summary>
        /// Centres the card when the frame has room and leaves the ScrollRect alone
        /// when it does not. Written here rather than at build time because the
        /// viewport's height is only known after a layout pass, and the interface
        /// scale can change it mid-session.
        /// </summary>
        private void CentreCard()
        {
            if (viewport == null || content == null) return;
            float free = viewport.rect.height - content.rect.height;
            if (free <= TopPadding) return;

            float target = -free * 0.5f;
            if (!Mathf.Approximately(content.anchoredPosition.y, target))
                content.anchoredPosition = new Vector2(0f, target);
        }
    }
}
