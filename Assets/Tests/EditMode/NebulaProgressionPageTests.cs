using NUnit.Framework;
using UnityEngine;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-05: the module tree. The question this page exists to answer is "why
    /// can't I take this", so the reject copy is what gets tested hardest.
    /// </summary>
    public class NebulaProgressionPageTests
    {
        private NebulaUiShell shell;
        private float enteredTimeScale;

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void Teardown()
        {
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

        [Test]
        public void EveryModuleInTheCatalogGetsARow()
        {
            var page = new NebulaProgressionPage(null);
            page.Build(Host());
            Assert.AreEqual(ModuleCatalog.Modules.Length, page.RowCount,
                "A module with no row is a module the player cannot find.");
        }

        [Test]
        public void BuildsAndRefreshesWithoutASession()
        {
            var page = new NebulaProgressionPage(null);
            Assert.DoesNotThrow(() => page.Build(Host()));
            Assert.DoesNotThrow(() => page.Refresh());
        }

        [Test]
        public void RefreshingDoesNotSpendAnything()
        {
            // The page asks the validator, never TryStartUpgrade. Calling the latter
            // from a refresh would mean that merely looking at the screen buys
            // things — the worst possible bug on a page about spending resources.
            var page = new NebulaProgressionPage(null);
            page.Build(Host());

            int pointsBefore = Progression.ProgressionService.State.DevelopmentPoints;
            for (int i = 0; i < 50; i++) page.Refresh();

            Assert.AreEqual(pointsBefore, Progression.ProgressionService.State.DevelopmentPoints,
                "Refreshing the progression page spent development points.");
        }

        [Test]
        public void EveryRejectReasonHasPlayerFacingCopy()
        {
            // A reason with no translation surfaces to the player as an enum name.
            foreach (UpgradeRejectReason reason in System.Enum.GetValues(typeof(UpgradeRejectReason)))
            {
                if (reason == UpgradeRejectReason.None) continue;
                string text = NebulaProgressionPage.RejectText(reason, null);
                Assert.IsFalse(string.IsNullOrEmpty(text), $"{reason} has no copy.");
                Assert.AreNotEqual(reason.ToString(), text,
                    $"{reason} is being shown as its raw enum name.");
            }
        }

        [Test]
        public void AMilestoneRejectNamesTheMilestone()
        {
            ModuleDef withMilestone = null;
            foreach (ModuleDef module in ModuleCatalog.Modules)
                if (!string.IsNullOrEmpty(module.MilestoneLabel)) { withMilestone = module; break; }

            if (withMilestone == null) Assert.Ignore("No module in the catalog declares a milestone.");

            string text = NebulaProgressionPage.RejectText(
                UpgradeRejectReason.MilestoneNotMet, withMilestone);
            StringAssert.Contains(withMilestone.MilestoneLabel, text,
                "A milestone gate must name the milestone, not just say one is missing.");
        }

        [Test]
        public void DurationNeverGoesNegative()
        {
            Assert.AreEqual("0:00", NebulaProgressionPage.FormatDuration(-5f));
            Assert.AreEqual("1:30", NebulaProgressionPage.FormatDuration(90f));
        }

        [Test]
        public void RepeatedRefreshesCreateNothing()
        {
            var page = new NebulaProgressionPage(null);
            RectTransform host = Host();
            page.Build(host);
            page.Refresh();

            int baseline = host.GetComponentsInChildren<Transform>(true).Length;
            for (int i = 0; i < 300; i++) page.Refresh();

            Assert.AreEqual(baseline, host.GetComponentsInChildren<Transform>(true).Length,
                "Refreshing rebuilt rows instead of repainting them.");
        }
    }
}
