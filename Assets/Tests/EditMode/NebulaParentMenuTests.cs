using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-03: the parent command menu's shell behaviour — page switching, the back
    /// stack, rail badges, and the binding promise that it introduces no new key.
    /// </summary>
    public class NebulaParentMenuTests
    {
        private NebulaUiShell shell;
        private NebulaParentMenuScreen menu;
        private float enteredTimeScale;
        private bool enteredGate;

        private sealed class StubPage : INebulaMenuPage
        {
            public StubPage(NebulaUiAssetCatalog.Page page, string title)
            {
                Page = page;
                Title = title;
            }

            public NebulaUiAssetCatalog.Page Page { get; }
            public string Title { get; }
            public int Builds, Refreshes, BackCalls;
            public bool ConsumesBack;
            public int Attention;

            public int AttentionCount => Attention;
            public void Build(RectTransform host) => Builds++;
            public void Refresh() => Refreshes++;

            public bool HandleBack()
            {
                BackCalls++;
                return ConsumesBack;
            }
        }

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            enteredGate = InputGate.Suppressed;
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
            menu = NebulaParentMenuScreen.Create(shell);
        }

        [TearDown]
        public void Teardown()
        {
            if (menu != null) Object.DestroyImmediate(menu.gameObject);
            if (shell != null) Object.DestroyImmediate(shell.gameObject);
            ExclusiveModal.ResetForSession();
            // Never restore a zero: in edit mode Time.timeScale persists into
            // ProjectSettings/TimeManager.asset, and a committed 0 freezes the
            // project for whoever opens it next.
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
            InputGate.Suppressed = enteredGate;
        }

        private StubPage AddPage(NebulaUiAssetCatalog.Page page, string title = "Sayfa")
        {
            var stub = new StubPage(page, title);
            menu.AddPage(stub);
            return stub;
        }

        // ---- binding promise -------------------------------------------------

        [Test]
        public void MenuIntroducesNoNewKeybinding()
        {
            // Every letter was already taken and Escape already means five different
            // cancels. The menu reuses the two keys that already opened a
            // management screen rather than claiming a marginal one.
            Assert.AreEqual(KeyCode.U, NebulaParentMenuScreen.ProgressionKey);
            // And it must still land on the page it always landed on.
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Progression,
                NebulaParentMenuScreen.PageForKey(NebulaParentMenuScreen.ProgressionKey));
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Fleet,
                NebulaParentMenuScreen.PageForKey(NebulaParentMenuScreen.FleetKey));
            Assert.AreEqual(KeyCode.Y, NebulaParentMenuScreen.FleetKey);
        }

        [Test]
        public void MenuUsesNoFunctionKey()
        {
            KeyCode[] bindings =
            {
                NebulaParentMenuScreen.ProgressionKey,
                NebulaParentMenuScreen.FleetKey,
            };
            foreach (KeyCode key in bindings)
                Assert.IsFalse(key >= KeyCode.F1 && key <= KeyCode.F12,
                    $"{key} is a function key; those are forbidden project-wide.");
        }

        [Test]
        public void EscapeIsNotClaimedAsAnOpenKey()
        {
            Assert.AreNotEqual(KeyCode.Escape, NebulaParentMenuScreen.ProgressionKey);
            Assert.AreNotEqual(KeyCode.Escape, NebulaParentMenuScreen.FleetKey);
        }

        /// <summary>
        /// The strategic map owns M, and the older navigation screen had already
        /// yielded it so the key would have exactly one owner. The menu briefly
        /// bound M as well, which would have made one press open two surfaces --
        /// StrategicMapPresenterTests guards the rule at source level, but this
        /// states it where the menu's own bindings are decided.
        /// </summary>
        [Test]
        public void TheMenuDoesNotTakeTheMapKeyBackFromTheStrategicMap()
        {
            Assert.AreNotEqual(KeyCode.M, NebulaParentMenuScreen.ProgressionKey);
            Assert.AreNotEqual(KeyCode.M, NebulaParentMenuScreen.FleetKey);

            string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
                Application.dataPath, "Scripts", "Nebula", "UI", "Menu", "NebulaParentMenuScreen.cs"));
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(
                source, @"KeyCode\.M\b(?!ouse|enu|inus)"),
                "the menu must not bind M; the strategic map owns it");
        }

        /// <summary>
        /// Every page in the catalog has a class and a rail entry. The screenshot
        /// harness built its own list and quietly fell one short of the installer's,
        /// so every capture taken before this showed a rail with no Harita on it --
        /// evidence of an interface that did not exist.
        /// </summary>
        [Test]
        public void EveryCatalogPage_HasARailEntry()
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            menu.AddPage(new NebulaOverviewPage(null));
            menu.AddPage(new NebulaMapPage(null));
            menu.AddPage(new NebulaProgressionPage(null));
            menu.AddPage(new NebulaFleetPage(null));
            menu.AddPage(new NebulaPlanetsPage(null));
            menu.AddPage(new NebulaResourcesPage(null));
            menu.AddPage(new NebulaCombatPage(null));
            menu.AddPage(new NebulaDiscoveryPage(null));
            menu.AddPage(new NebulaSettingsPage());
            menu.AddPage(new NebulaSessionPage(null));

            var pages = System.Enum.GetValues(typeof(NebulaUiAssetCatalog.Page));
            Assert.AreEqual(pages.Length, menu.PageCount,
                "the rail carries one entry per catalog page, no more and no fewer");

            foreach (NebulaUiAssetCatalog.Page page in pages)
                Assert.IsTrue(menu.ShowPage(page), $"{page} has no page class behind it");
        }

        // ---- the map page ----------------------------------------------------

        /// <summary>
        /// The map is handed off to, never hosted. It is a full-screen surface with
        /// its own canvas, its own render target sized to the backbuffer, its own
        /// camera controls and its own modal ownership, and the brief was to
        /// integrate it without changing any of that.
        /// </summary>
        [Test]
        public void TheMapPage_BuildsAndRefusesSafelyWithoutASession()
        {
            var page = new NebulaMapPage(null);
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);

            Assert.DoesNotThrow(() => page.Build((RectTransform)host.transform));
            Assert.DoesNotThrow(() => page.Refresh());

            Assert.AreEqual("HARİTA HAZIR DEĞİL", page.OpenBlockedReason,
                "with no session the action must say why it cannot open the map");
            Assert.AreEqual(0, page.HandoffCount);
        }

        /// <summary>
        /// Without a map to open, the page must not close the menu either. Popping
        /// the shell first and then failing to hand over would drop the player onto
        /// the HUD with nothing having happened.
        /// </summary>
        [Test]
        public void TheMapPage_DoesNotCloseTheMenuWhenItCannotOpenTheMap()
        {
            NebulaParentMenuScreen menu = BuildMenuWithMap();
            menu.Open(NebulaUiAssetCatalog.Page.Map);
            Assert.IsTrue(menu.IsOpen);

            menu.PageOf<NebulaMapPage>().Refresh();

            Assert.IsTrue(menu.IsOpen, "a refused handoff leaves the menu exactly as it was");
            Assert.AreEqual(0, menu.PageOf<NebulaMapPage>().HandoffCount);
        }

        [Test]
        public void TheMapPage_IsRegisteredInTheRail()
        {
            NebulaParentMenuScreen menu = BuildMenuWithMap();

            Assert.IsNotNull(menu.PageOf<NebulaMapPage>(), "the rail must carry a map entry");
            Assert.IsTrue(menu.ShowPage(NebulaUiAssetCatalog.Page.Map),
                "and it must be selectable like any other page");
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Map, menu.CurrentPage);
        }

        private NebulaParentMenuScreen BuildMenuWithMap()
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            menu.AddPage(new NebulaOverviewPage(null));
            menu.AddPage(new NebulaMapPage(null, shell));
            return menu;
        }

        // ---- pages -----------------------------------------------------------

        [Test]
        public void AddingAPage_BuildsItExactlyOnce()
        {
            StubPage page = AddPage(NebulaUiAssetCatalog.Page.Overview);
            Assert.AreEqual(1, page.Builds);
            Assert.AreEqual(1, menu.PageCount);
        }

        [Test]
        public void RegisteringTheSamePageTwice_IsIgnored()
        {
            AddPage(NebulaUiAssetCatalog.Page.Overview);
            AddPage(NebulaUiAssetCatalog.Page.Overview);
            Assert.AreEqual(1, menu.PageCount, "A duplicate page would give the rail two identical rows.");
        }

        [Test]
        public void OpeningShowsTheRequestedPage_AndRefreshesIt()
        {
            StubPage overview = AddPage(NebulaUiAssetCatalog.Page.Overview);
            AddPage(NebulaUiAssetCatalog.Page.Fleet);

            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Overview));
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Overview, menu.CurrentPage);
            Assert.GreaterOrEqual(overview.Refreshes, 1,
                "A page must re-read state when it is shown, not display whatever it built with.");
        }

        [Test]
        public void OpeningWhileAlreadyOpen_SwitchesPageInsteadOfStacking()
        {
            AddPage(NebulaUiAssetCatalog.Page.Overview);
            AddPage(NebulaUiAssetCatalog.Page.Fleet);

            menu.Open(NebulaUiAssetCatalog.Page.Overview);
            menu.Open(NebulaUiAssetCatalog.Page.Fleet);

            Assert.AreEqual(NebulaUiAssetCatalog.Page.Fleet, menu.CurrentPage);
            Assert.AreEqual(1, shell.StackDepth,
                "Pressing a second menu key must switch pages, not open a second menu.");
        }

        [Test]
        public void SwitchingPages_RefreshesTheNewOne()
        {
            AddPage(NebulaUiAssetCatalog.Page.Overview);
            StubPage fleet = AddPage(NebulaUiAssetCatalog.Page.Fleet);

            menu.Open(NebulaUiAssetCatalog.Page.Overview);
            int before = fleet.Refreshes;
            menu.ShowPage(NebulaUiAssetCatalog.Page.Fleet);

            Assert.Greater(fleet.Refreshes, before);
        }

        [Test]
        public void ShowingAnUnregisteredPage_IsRefusedRatherThanBlank()
        {
            AddPage(NebulaUiAssetCatalog.Page.Overview);
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            Assert.IsFalse(menu.ShowPage(NebulaUiAssetCatalog.Page.Combat));
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Overview, menu.CurrentPage,
                "An unknown page must leave the current one up rather than blanking the content area.");
        }

        [Test]
        public void StepPage_WalksTheRailAndWraps()
        {
            AddPage(NebulaUiAssetCatalog.Page.Overview);
            AddPage(NebulaUiAssetCatalog.Page.Fleet);
            AddPage(NebulaUiAssetCatalog.Page.Combat);
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            menu.StepPage(1);
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Fleet, menu.CurrentPage);

            menu.StepPage(-1);
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Overview, menu.CurrentPage);

            menu.StepPage(-1);
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Combat, menu.CurrentPage,
                "The rail should loop rather than stop dead at its ends.");
        }

        // ---- input gate + back -----------------------------------------------

        /// <summary>
        /// Opening the menu takes the controls and nothing else. The world keeps
        /// simulating behind it — the player asked for a menu, not a pause — so the
        /// clock is asserted to be exactly where it was found, both while the menu
        /// is open and after it closes.
        /// </summary>
        [Test]
        public void OpeningGatesFlightInput_ButLeavesTheWorldRunning()
        {
            Time.timeScale = 1f;
            InputGate.Suppressed = false;
            AddPage(NebulaUiAssetCatalog.Page.Overview);

            menu.Open(NebulaUiAssetCatalog.Page.Overview);
            Assert.IsTrue(InputGate.Suppressed,
                "an open menu that does not gate input lets WASD fly the ship behind it");
            Assert.AreEqual(1f, Time.timeScale, "the open menu froze the simulation");

            menu.Close();
            Assert.IsFalse(InputGate.Suppressed);
            Assert.AreEqual(1f, Time.timeScale, "closing the menu moved the clock");
            Assert.IsFalse(menu.IsOpen);
        }

        [Test]
        public void BackGoesToThePageFirst_SoAConfirmationClosesItself()
        {
            StubPage overview = AddPage(NebulaUiAssetCatalog.Page.Overview);
            overview.ConsumesBack = true;
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            Assert.IsTrue(shell.Back());
            Assert.IsTrue(menu.IsOpen,
                "A page with an open confirmation must swallow Back instead of closing the menu.");
            Assert.AreEqual(1, overview.BackCalls);
        }

        [Test]
        public void BackClosesTheMenu_WhenThePageHasNothingToUnwind()
        {
            StubPage overview = AddPage(NebulaUiAssetCatalog.Page.Overview);
            overview.ConsumesBack = false;
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            Assert.IsTrue(shell.Back());
            Assert.IsFalse(menu.IsOpen);
        }

        [Test]
        public void MenuCannotOpen_WhileAnotherModalOwnsInput()
        {
            var other = new object();
            ExclusiveModal.TryAcquire(other);
            AddPage(NebulaUiAssetCatalog.Page.Overview);

            Assert.IsFalse(menu.Open(NebulaUiAssetCatalog.Page.Overview));
            Assert.IsFalse(menu.IsOpen);
            ExclusiveModal.Release(other);
        }

        [Test]
        public void ClosingTwice_IsHarmless()
        {
            InputGate.Suppressed = false;
            AddPage(NebulaUiAssetCatalog.Page.Overview);
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            menu.Close();
            Assert.DoesNotThrow(() => menu.Close());
            Assert.IsFalse(InputGate.Suppressed,
                "A second close must not re-apply the gate the first one handed back.");
        }

        [Test]
        public void MenuLivesOnTheMenuLayer()
        {
            Assert.IsTrue(menu.transform.IsChildOf(shell.Root(UiLayer.Menu)));
        }
    }
}
