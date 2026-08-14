using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.Extraction;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-09 groundwork: the pages have to carry the screens' actions before the
    /// screens can be deleted. That starts with one confirmation surface and one
    /// primary action, both on the shell's stack.
    /// </summary>
    public class NebulaConfirmationTests
    {
        private NebulaUiShell shell;
        private NebulaConfirmationDialog dialog;
        private float enteredTimeScale;

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
            dialog = NebulaConfirmationDialog.Create(shell);
        }

        [TearDown]
        public void Teardown()
        {
            if (dialog != null) Object.DestroyImmediate(dialog.gameObject);
            if (shell != null)
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
            }
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
        }

        private RectTransform Host()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            go.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        // ---- the dialog is a screen, not a panel -----------------------------

        [Test]
        public void AskingPushesOntoTheStack()
        {
            Assert.IsTrue(dialog.Ask("BAŞLIK", "Soru?", "30 PUAN", "ONAYLA", () => { }));
            Assert.IsTrue(dialog.IsOpen);
            Assert.AreEqual(1, shell.StackDepth,
                "The confirmation must be a real screen, or Back cannot unwind it correctly.");
        }

        [Test]
        public void BackCancelsTheConfirmation_AndLeavesThePageBehindIt()
        {
            bool ran = false;
            var page = new object();
            shell.Push(new FakePage());
            dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => ran = true);
            Assert.AreEqual(2, shell.StackDepth);

            shell.Back();

            Assert.IsFalse(ran, "Backing out of a confirmation must not run its action.");
            Assert.AreEqual(1, shell.StackDepth, "Back closed more than the confirmation.");
            Assert.AreEqual(1, dialog.CancelledCount);
        }

        [Test]
        public void AsecondAskWhileOpenIsRefused()
        {
            dialog.Ask("A", "?", null, "OK", () => { });
            Assert.IsFalse(dialog.Ask("B", "?", null, "OK", () => { }),
                "Two stacked confirmations would leave the player unsure what they are answering.");
        }

        [Test]
        public void TeardownDropsAPendingAction()
        {
            // A dialog dismissed by session teardown must never run its action later
            // against a world that no longer exists.
            bool ran = false;
            dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => ran = true);
            shell.Shutdown();
            Assert.IsFalse(ran);
            Assert.IsFalse(dialog.IsOpen);
        }

        private sealed class FakePage : INebulaUiScreen
        {
            public string ScreenId => "fake";
            public void OnOpened(NebulaUiShell shell) { }
            public void OnClosed() { }
            public void OnCovered() { }
            public void OnRevealed() { }
            public bool HandleBack() => false;
        }

        // ---- the dialog has to be in front of what it is asking about --------
        //
        // A confirmation that opens behind the page is not a smaller defect than
        // one that never opens: the game pauses, the back stack grows, the player
        // sees nothing, and the only clue is that Escape appears to be eaten once
        // before the menu will close. Nothing above this line could catch it --
        // IsOpen, StackDepth and CancelledCount were all correct the whole time.
        // Depth is the only thing that was wrong, so depth is what is measured.

        /// <summary>
        /// The real menu, built over this dialog in the order composition builds
        /// them: <c>PresentationInstaller</c> creates the confirmation first and
        /// the parent menu after it, so on the one canvas they share the menu is
        /// the later sibling. That order is the bug, so the fixture reproduces it
        /// rather than working around it, and asserts it really holds -- if the
        /// installer's order ever flips, these tests would otherwise pass while
        /// proving nothing.
        /// </summary>
        private NebulaParentMenuScreen MenuBuiltOverTheDialog(out RectTransform menuRoot)
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            menu.AddPage(new NebulaFleetPage(null, dialog));

            menuRoot = shell.Root(UiLayer.Menu).Find("Parent Menu Root") as RectTransform;
            Assert.IsNotNull(menuRoot, "the parent menu did not build a root on the menu layer");
            Assert.Greater(menuRoot.GetSiblingIndex(), dialog.Root.GetSiblingIndex(),
                "Precondition: the menu is created after the dialog and so starts in front of it.");

            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Fleet));
            return menu;
        }

        /// <summary>
        /// Every transform under the menu layer, numbered in the order that canvas
        /// paints them.
        /// <para>
        /// A screen-space overlay canvas draws its hierarchy front-to-back in
        /// pre-order -- a later sibling over an earlier one, a child over its own
        /// parent -- and <c>GraphicRaycaster</c> hands the pointer to whatever is
        /// painted last. So the pre-order index is the depth, and unlike
        /// <c>Graphic.depth</c> it needs no camera, no pointer and no completed
        /// canvas build, which is what makes this measurable outside play mode.
        /// </para>
        /// </summary>
        private Dictionary<Transform, int> PaintOrder()
        {
            var order = new Dictionary<Transform, int>();
            int next = 0;

            void Walk(Transform node)
            {
                order[node] = next++;
                for (int i = 0; i < node.childCount; i++) Walk(node.GetChild(i));
            }

            Walk(shell.Root(UiLayer.Menu));
            return order;
        }

        private static string PathOf(Transform node)
        {
            string path = node.name;
            for (Transform parent = node.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        [Test]
        public void AskingOverAPage_PutsTheDialogInFrontOfIt()
        {
            NebulaParentMenuScreen menu = MenuBuiltOverTheDialog(out RectTransform menuRoot);

            dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => { });

            Assert.IsTrue(dialog.IsOpen);
            Assert.Greater(dialog.Root.GetSiblingIndex(), menuRoot.GetSiblingIndex(),
                "The confirmation is behind the page that asked the question, so the player never sees it.");

            Object.DestroyImmediate(menu.gameObject);
        }

        [Test]
        public void AnOpenDialog_IsPaintedOverEveryControlOnThePageBeneathIt()
        {
            // Sibling order alone is necessary and not sufficient: what the player
            // needs is that no control of the page under the dialog is painted --
            // and therefore clickable -- after the dialog's own scrim.
            NebulaParentMenuScreen menu = MenuBuiltOverTheDialog(out RectTransform menuRoot);
            dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => { });

            Transform scrim = dialog.Root.Find("Scrim");
            Assert.IsNotNull(scrim, "the dialog has no scrim, so nothing stops a click reaching the page");

            var blocker = scrim.GetComponent<UnityEngine.UI.Graphic>();
            Assert.IsNotNull(blocker, "the dialog's scrim draws nothing, so it can take no pointer");
            Assert.IsTrue(blocker.raycastTarget,
                "the scrim does not take the pointer, so the page underneath still answers clicks");

            Dictionary<Transform, int> order = PaintOrder();
            int scrimDepth = order[scrim];

            foreach (UnityEngine.UI.Graphic graphic in
                     menuRoot.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                if (!graphic.raycastTarget || !graphic.gameObject.activeInHierarchy) continue;
                Assert.Less(order[graphic.transform], scrimDepth,
                    $"{PathOf(graphic.transform)} is painted over the confirmation's scrim, " +
                    "so it takes the click the dialog was asking for");
            }

            Object.DestroyImmediate(menu.gameObject);
        }

        [Test]
        public void BackClosesOnlyTheConfirmation_WithTheRealMenuUnderIt()
        {
            // The menu's Escape is a plain shell.Back(), and the dialog has to be the
            // screen that answers it. Covered above with a stand-in page; repeated
            // here against the real menu, which is what the player is looking at.
            NebulaParentMenuScreen menu = MenuBuiltOverTheDialog(out RectTransform _);
            bool ran = false;
            dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => ran = true);
            Assert.AreEqual(2, shell.StackDepth);

            shell.Back();

            Assert.IsFalse(ran, "Escape confirmed the action instead of cancelling it.");
            Assert.IsFalse(dialog.IsOpen);
            Assert.IsTrue(menu.IsOpen, "Escape closed the whole menu instead of just the confirmation.");
            Assert.AreEqual(1, shell.StackDepth);
            Assert.AreEqual(1, dialog.CancelledCount);

            Object.DestroyImmediate(menu.gameObject);
        }

        [Test]
        public void AScreenThatCoveredTheDialogAndClosed_HandsTheFrontBack()
        {
            // Depth is claimed when a screen opens, so anything that opens over this
            // dialog and raises itself keeps the front after it closes unless the
            // dialog takes it back on reveal. The dialog is still the screen holding
            // the question, so it is still the screen that must be seen.
            NebulaParentMenuScreen menu = MenuBuiltOverTheDialog(out RectTransform menuRoot);
            dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => { });

            shell.Push(new FakePage());
            menuRoot.SetAsLastSibling();      // what a screen raising itself does
            shell.Pop();

            Assert.IsTrue(dialog.IsOpen);
            Assert.Greater(dialog.Root.GetSiblingIndex(), menuRoot.GetSiblingIndex(),
                "The confirmation stayed buried after the screen above it closed.");

            Object.DestroyImmediate(menu.gameObject);
        }

        // ---- the progression action ------------------------------------------

        [Test]
        public void WithNothingSelected_TheUpgradeActionSaysSo()
        {
            var page = new NebulaProgressionPage(null, dialog);
            page.Build(Host());
            page.Refresh();

            Assert.IsNull(page.SelectedModuleId);
            Assert.IsNotNull(page.UpgradeBlockedReason,
                "A disabled primary action with no reason is the dead control this redesign removes.");
        }

        [Test]
        public void SelectingAModuleWithoutASession_StillExplainsItself()
        {
            var page = new NebulaProgressionPage(null, dialog);
            page.Build(Host());
            page.Select(ModuleCatalog.Modules[0].Id);

            Assert.AreEqual(ModuleCatalog.Modules[0].Id, page.SelectedModuleId);
            Assert.IsNotNull(page.UpgradeBlockedReason);
        }

        // ---- the two context panels ------------------------------------------

        [Test]
        public void ContextPanelsStayHiddenWhenTheShipIsNowhereNear()
        {
            // Both replace panels that were proximity-gated on purpose. Showing them
            // from the menu would turn a physical transfer into a teleporting one.
            NebulaHudPlanetContext panel = NebulaHudPlanetContext.Create(shell, null, null);
            Assert.IsFalse(panel.ReserveVisible);
            Assert.IsFalse(panel.SupportVisible);
            Object.DestroyImmediate(panel.gameObject);
        }

        [Test]
        public void EveryReserveRejectReason_HasPlayerFacingCopy()
        {
            foreach (ReserveTransferRejectReason reason in
                     System.Enum.GetValues(typeof(ReserveTransferRejectReason)))
            {
                if (reason == ReserveTransferRejectReason.None)
                {
                    Assert.IsNull(NebulaHudPlanetContext.ReserveRejectText(reason));
                    continue;
                }

                string text = NebulaHudPlanetContext.ReserveRejectText(reason);
                Assert.IsFalse(string.IsNullOrEmpty(text), $"{reason} has no copy.");
                Assert.AreNotEqual(reason.ToString(), text,
                    $"{reason} is being shown as its raw enum name.");
            }
        }

        [Test]
        public void CostReadsAsPointsAndMaterials_NotAsProse()
        {
            ModuleDef module = ModuleCatalog.Modules[0];
            string cost = NebulaProgressionPage.DescribeCost(module.TierCosts[0]);

            StringAssert.Contains("GELİŞİM PUANI", cost);
            Assert.IsFalse(cost.Contains("."), $"The cost line reads as a sentence: '{cost}'");
        }

        // ---- the fleet actions ------------------------------------------------

        [Test]
        public void FleetActionsExplainThemselvesWithNothingSelected()
        {
            var page = new NebulaFleetPage(null, dialog);
            page.Build(Host());
            page.Refresh();

            Assert.IsNull(page.SelectedVehicleId);
            Assert.IsNotNull(page.RecallBlockedReason,
                "Recall must say why it cannot run, not sit there dead.");
            Assert.IsNotNull(page.UpgradeBlockedReason);
        }

        [Test]
        public void TheTargetPickerStartsClosed_AndBackFallsThroughWhenItIs()
        {
            var page = new NebulaFleetPage(null, dialog);
            page.Build(Host());
            page.Refresh();

            Assert.IsFalse(page.TargetPickerOpen);
            Assert.IsFalse(page.HandleBack(),
                "With no picker open the page must let Back reach the shell, or the menu can never close.");
        }

        [Test]
        public void LaunchExplainsItselfWithNothingSelected()
        {
            var page = new NebulaFleetPage(null, dialog);
            page.Build(Host());
            page.Refresh();
            Assert.IsNotNull(page.LaunchBlockedReason);
        }

        [Test]
        public void SelectingAVehicleThatDoesNotExist_IsHarmless()
        {
            var page = new NebulaFleetPage(null, dialog);
            page.Build(Host());
            Assert.DoesNotThrow(() => page.Select("vehicle-that-is-not-there"));
            Assert.IsNotNull(page.RecallBlockedReason);
        }

        [Test]
        public void ClickingUpgradeWithoutASelection_DoesNothing()
        {
            var page = new NebulaProgressionPage(null, dialog);
            page.Build(Host());
            page.Refresh();

            Assert.AreEqual(0, dialog.ConfirmedCount);
            Assert.IsFalse(dialog.IsOpen);
        }
    }
}
