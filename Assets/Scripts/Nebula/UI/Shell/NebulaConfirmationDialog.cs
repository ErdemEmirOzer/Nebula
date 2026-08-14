using System;
using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;

namespace Nebula.UI.Shell
{
    /// <summary>
    /// The one confirmation surface.
    /// <para>
    /// It is a real screen on the shell's stack rather than a panel a page owns,
    /// which is what makes Back behave: the confirmation closes first, the page
    /// stays, and the menu stays. Four legacy screens each carried their own
    /// dialog and their own idea of what Escape meant while it was open.
    /// </para>
    /// <para>
    /// Only for the expensive and the irreversible. The brief's rule is that a
    /// frequent action must not ask twice, so a caller that confirms something
    /// routine is misusing this.
    /// </para>
    /// </summary>
    public sealed class NebulaConfirmationDialog : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "shell.confirm";

        private NebulaUiShell shell;
        private RectTransform root;
        private NebulaUiFactory.LabelHandle title, body, cost;
        private NebulaUiFactory.ButtonHandle confirmButton, cancelButton;

        private Action onConfirm;

        /// <summary>Test-facing: whether the dialog is currently on the stack.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Test-facing: how many times a confirmation actually ran.</summary>
        public int ConfirmedCount { get; private set; }

        /// <summary>Test-facing: how many times the player backed out.</summary>
        public int CancelledCount { get; private set; }

        /// <summary>
        /// Test-facing: the visible root, so a fixture can measure this dialog's
        /// depth against the page it is supposed to be covering.
        /// </summary>
        public RectTransform Root => root;

        public static NebulaConfirmationDialog Create(NebulaUiShell shell)
        {
            var go = new GameObject("Confirmation");
            var dialog = go.AddComponent<NebulaConfirmationDialog>();
            dialog.Build(shell);
            return dialog;
        }

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Confirmation Root");
            NebulaUiFactory.Fill(root);
            // On the layer root, not inside the panel. A closed screen hides its
            // root, and a controller parented into that root stops updating with
            // it -- which for this screen means the key that opens it is never
            // read, and the screen can never be opened again.
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            Image scrim = NebulaUiFactory.Rect(root, "Scrim", NebulaUiDesignTokens.OverlayDim);
            NebulaUiFactory.Fill(scrim.rectTransform);
            scrim.raycastTarget = true;

            Image panel = NebulaUiFactory.Panel(root, "Panel", NebulaUiAssetCatalog.Modal);
            RectTransform card = panel.rectTransform;
            NebulaUiFactory.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 240f));

            title = NebulaUiFactory.Label(card, "Title", "", NebulaUiDesignTokens.SizeScreenTitle,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingScreenTitle);
            NebulaUiFactory.Place(title.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(28f, -28f), new Vector2(464f, 28f));

            body = NebulaUiFactory.Label(card, "Body", "", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(body.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(28f, -70f), new Vector2(464f, 60f));

            cost = NebulaUiFactory.Label(card, "Cost", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Caution), TextAnchor.MiddleLeft,
                FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(cost.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(28f, -140f), new Vector2(464f, 18f));

            cancelButton = NebulaUiFactory.ButtonControl(card, "Cancel", "VAZGEÇ",
                NebulaUiAssetCatalog.ButtonKind.Secondary, Cancel);
            NebulaUiFactory.Place(cancelButton.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(28f, 26f), new Vector2(180f, 42f));

            confirmButton = NebulaUiFactory.ButtonControl(card, "Confirm", "ONAYLA",
                NebulaUiAssetCatalog.ButtonKind.Primary, Confirm);
            NebulaUiFactory.Place(confirmButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-28f, 26f), new Vector2(220f, 42f));

            root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Asks. <paramref name="costLine"/> is the price in the same compact form
        /// the HUD uses; leave it empty when the action is free.
        /// </summary>
        public bool Ask(string heading, string question, string costLine, string confirmCaption,
            Action confirmed, bool destructive = false)
        {
            if (IsOpen) return false;

            title.Value = heading;
            body.Value = question;
            cost.Value = costLine ?? string.Empty;
            cost.Text.gameObject.SetActive(!string.IsNullOrEmpty(costLine));

            confirmButton.SetCaption(string.IsNullOrEmpty(confirmCaption) ? "ONAYLA" : confirmCaption);
            confirmButton.Background.sprite = NebulaUiAssetCatalog.Button(
                destructive ? NebulaUiAssetCatalog.ButtonKind.Destructive
                            : NebulaUiAssetCatalog.ButtonKind.Primary,
                NebulaUiAssetCatalog.ControlState.Default);

            onConfirm = confirmed;
            return shell.Push(this);
        }

        private void Confirm()
        {
            // The callback runs after the dialog has left the stack, so an action
            // that opens something of its own is not fighting this one for the top.
            Action callback = onConfirm;
            onConfirm = null;
            ConfirmedCount++;
            shell.Pop();
            callback?.Invoke();
        }

        private void Cancel()
        {
            onConfirm = null;
            CancelledCount++;
            shell.Pop();
        }

        // ---- INebulaUiScreen -------------------------------------------------

        public void OnOpened(NebulaUiShell owningShell)
        {
            shell = owningShell;
            IsOpen = true;
            root.gameObject.SetActive(true);
            RaiseToFront();
        }

        public void OnClosed()
        {
            IsOpen = false;
            root.gameObject.SetActive(false);
            // Dropped so a dialog dismissed by a session teardown cannot run its
            // action later against a world that no longer exists.
            onConfirm = null;
        }

        public void OnCovered() { }

        /// <summary>The screen above this one closed; take the front back from it.</summary>
        public void OnRevealed() => RaiseToFront();

        /// <summary>
        /// Puts the visible root last among its siblings, which on a screen-space
        /// overlay canvas is what "in front" means.
        /// <para>
        /// This dialog and every screen it opens over share one canvas — the Menu
        /// layer — so nothing but sibling order decides which of them the player
        /// sees. Build order alone decided it before, and build order is against
        /// this dialog: composition creates the confirmation before the parent
        /// menu, so the menu's root was always the later sibling and always drawn
        /// on top. The dialog opened, paused the game, took the top of the back
        /// stack and was <i>completely invisible behind the page that asked the
        /// question</i>. The only symptom a player got was Escape appearing to be
        /// swallowed once: the first press closed the confirmation nobody could
        /// see, the second closed the menu.
        /// </para>
        /// <para>
        /// The raise is on the root, never on this component's own transform. That
        /// transform is deliberately a sibling of the root rather than a child of
        /// it — see <see cref="Build"/> — and it carries no graphics, so its order
        /// is irrelevant and re-parenting it into the root would break the very
        /// thing that comment protects.
        /// </para>
        /// <para>
        /// Raising on open rather than at build time on purpose: whatever is
        /// created after this dialog would win a one-off ordering, and the set of
        /// screens on the Menu layer grows. Ordering at the moment of asking is
        /// ordering against whatever is actually on screen.
        /// </para>
        /// </summary>
        private void RaiseToFront()
        {
            if (root != null) root.SetAsLastSibling();
        }

        /// <summary>
        /// Back cancels. Returning false lets the shell pop this screen, which is
        /// exactly the desired behaviour, but the cancel has to be counted first.
        /// </summary>
        public bool HandleBack()
        {
            onConfirm = null;
            CancelledCount++;
            return false;
        }
    }
}
