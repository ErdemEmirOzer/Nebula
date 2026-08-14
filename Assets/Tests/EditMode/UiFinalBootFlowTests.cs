using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The boot flow's wiring, checked without a running session.
    /// <para>
    /// The PlayMode counterpart proves the shipped scene actually opens on the
    /// menu. This one proves the parts that decide that: the production default
    /// on the bootstrap, that every entry on the front door is connected to
    /// something, and that the one entry which cannot be exercised in a test
    /// process — Quit — still reaches a seam that records the request.
    /// </para>
    /// <para>
    /// Nothing here greps a source file. Every assertion drives a real object and
    /// reads a real effect, and the fixture carries its own proof that the checks
    /// can fail, because this project has shipped tests that asserted nothing.
    /// </para>
    /// </summary>
    public class UiFinalBootFlowTests
    {
        private NebulaUiShell shell;
        private float enteredTimeScale;
        private bool enteredInputGate;
        private readonly List<GameObject> spawned = new List<GameObject>();

        /// <summary>Records intent instead of creating a universe or leaving the process.</summary>
        private sealed class RecordingLauncher : INebulaSessionLauncher
        {
            public int NewCampaigns;
            public readonly List<int> ContinuedSlots = new List<int>();
            public int Quits;

            public int ReturnsToMenu;

            public void StartNewCampaign() => NewCampaigns++;
            public void ContinueFromSlot(int slot) => ContinuedSlots.Add(slot);
            public void QuitGame() => Quits++;
            public void ReturnToMainMenu() => ReturnsToMenu++;
            public int Restarts;
            public void RestartCampaign() => Restarts++;
        }

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            enteredInputGate = InputGate.Suppressed;
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void Teardown()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();

            if (shell != null)
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
                shell = null;
            }
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
            // A leaked gate is invisible and expensive: every contextual ship
            // component simply stops answering for the rest of the run.
            InputGate.Suppressed = enteredInputGate;
        }

        private T Track<T>(T component) where T : Component
        {
            spawned.Add(component.gameObject);
            return component;
        }

        /// <summary>
        /// The control the player would click, found the way the player finds it —
        /// by what it is, not by a field the test was handed.
        /// </summary>
        private static Button Control(Transform root, string name)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;
            return null;
        }

        /// <summary>
        /// A control is wired when clicking it produces an effect. Counting
        /// listeners would pass on a listener that does nothing, so the caller
        /// supplies the effect and this only reports whether it moved.
        /// </summary>
        private static bool ClickChanges(Button button, System.Func<int> observe)
        {
            if (button == null) return false;
            int before = observe();
            button.onClick.Invoke();
            return observe() != before;
        }

        // =====================================================================
        // The production default
        // =====================================================================

        /// <summary>
        /// The flag that decides whether the game opens on a menu or on a world.
        /// It defaults to gameplay in a great many engines' sample projects and it
        /// defaulted to gameplay here too, because tests wanted a world one frame
        /// after loading the scene. A component built from scratch must carry the
        /// production answer, or every new scene ships the wrong front door.
        /// </summary>
        [Test]
        public void AFreshBootstrap_DefaultsToTheMenu_NotToAWorld()
        {
            // No [ExecuteAlways] on the bootstrap, so adding it outside play mode
            // runs no Awake and starts nothing -- the default is read, not run.
            var bootstrap = Track(new GameObject("Bootstrap").AddComponent<SolarSystemBootstrap>());

            Assert.AreEqual(NebulaBootMode.MainMenu, bootstrap.BootMode,
                "a scene built from this component would boot straight into a world");
            Assert.IsFalse(bootstrap.SessionActive, "and it must not have started one either");
            Assert.IsNull(bootstrap.Context);
        }

        /// <summary>
        /// Two modes that behave identically would make the flag decorative. This
        /// is the guard that the alternative is a real, distinct value.
        /// </summary>
        [Test]
        public void TheBootModeHasTwoDistinctMeanings()
        {
            Assert.AreNotEqual(NebulaBootMode.MainMenu, NebulaBootMode.GameplayDirect);

            var bootstrap = Track(new GameObject("Bootstrap").AddComponent<SolarSystemBootstrap>());
            bootstrap.BootMode = NebulaBootMode.GameplayDirect;
            Assert.AreEqual(NebulaBootMode.GameplayDirect, bootstrap.BootMode,
                "the mode must be settable, or the integration tests that ask for a world cannot");
        }

        // =====================================================================
        // The front door
        // =====================================================================

        [Test]
        public void EveryMainMenuEntry_DoesSomethingWhenClicked()
        {
            var launcher = new RecordingLauncher();
            int settings = 0, load = 0, credits = 0;

            NebulaMainMenuScreen menu = Track(NebulaMainMenuScreen.Create(shell, launcher));
            menu.SettingsRequested += () => settings++;
            menu.LoadRequested += () => load++;
            menu.CreditsRequested += () => credits++;

            Assert.IsTrue(ClickChanges(Control(shell.Root(UiLayer.Menu), "New"), () => launcher.NewCampaigns),
                "YENİ SEFER is the only way into the game and it does nothing");
            Assert.IsTrue(ClickChanges(Control(shell.Root(UiLayer.Menu), "Quit"), () => launcher.Quits),
                "ÇIKIŞ does nothing");
            Assert.IsTrue(ClickChanges(Control(shell.Root(UiLayer.Menu), "Settings"), () => settings),
                "AYARLAR does nothing");
            Assert.IsTrue(ClickChanges(Control(shell.Root(UiLayer.Menu), "Load"), () => load),
                "KAYIT YÜKLE does nothing");
            Assert.IsTrue(ClickChanges(Control(shell.Root(UiLayer.Menu), "Credits"), () => credits),
                "KÜNYE does nothing");
        }

        /// <summary>
        /// One click, one campaign. A menu entry that fires twice is how a player
        /// ends up with the second universe and loses the first.
        /// </summary>
        [Test]
        public void OneClickOnNewCampaign_StartsExactlyOne()
        {
            var launcher = new RecordingLauncher();
            Track(NebulaMainMenuScreen.Create(shell, launcher));

            Control(shell.Root(UiLayer.Menu), "New").onClick.Invoke();

            Assert.AreEqual(1, launcher.NewCampaigns);
            Assert.AreEqual(0, launcher.Quits, "and nothing else fired alongside it");
            Assert.IsEmpty(launcher.ContinuedSlots);
        }

        /// <summary>
        /// Continue with nothing behind it is the most common way a main menu
        /// lies. Both answers are asserted rather than one of them skipped: with
        /// no playable save the entry refuses <b>and says why</b> and starting a
        /// session anyway is a bug; with one, it must resume that exact slot.
        /// Which branch runs depends on the machine's save profile, so neither is
        /// allowed to be the "not checked today" case.
        /// </summary>
        [Test]
        public void ContinueEitherResumesTheRealSlotOrRefusesWithAReason()
        {
            var launcher = new RecordingLauncher();
            NebulaMainMenuScreen menu = Track(NebulaMainMenuScreen.Create(shell, launcher));
            int resume = menu.ResumeSlot;

            Control(shell.Root(UiLayer.Menu), "Continue").onClick.Invoke();

            if (resume >= 0)
            {
                Assert.IsNull(menu.ContinueBlockedReason,
                    "an available Continue must not also carry a blocked reason");
                Assert.AreEqual(new List<int> { resume }, launcher.ContinuedSlots,
                    "Continue resumed a different slot than the one it advertised");
            }
            else
            {
                Assert.IsNotNull(menu.ContinueBlockedReason, "a disabled control must carry its reason");
                Assert.IsEmpty(launcher.ContinuedSlots,
                    "a refused Continue must not start a session anyway");
            }
        }

        // =====================================================================
        // The quit seam
        // =====================================================================

        /// <summary>
        /// A test process cannot prove the application exits, so it proves the
        /// request reaches the seam that exits it. The real Player smoke run is
        /// what confirms the actual exit.
        /// </summary>
        [Test]
        public void TheRealLauncher_RecordsTheQuitRequest()
        {
            var launcher = new NebulaSessionLauncher(null);
            Assert.IsFalse(launcher.QuitRequested, "nothing has been asked for yet");

            launcher.QuitGame();

            Assert.IsTrue(launcher.QuitRequested,
                "the quit request never reached the host seam; in a Player this is the exit");
        }

        /// <summary>
        /// The launcher must not be able to create a universe just by existing:
        /// with no bootstrap behind it, asking for a campaign is a refusal rather
        /// than a crash, and nothing is counted as started.
        /// </summary>
        [Test]
        public void TheLauncherWithNoBootstrap_StartsNothingAndDoesNotThrow()
        {
            var launcher = new NebulaSessionLauncher(null);

            Assert.DoesNotThrow(() => launcher.StartNewCampaign());
            Assert.AreEqual(0, launcher.SessionsStarted, "no bootstrap means no session was started");
            Assert.AreEqual(-1, launcher.PendingLoadSlot, "a fresh campaign restores no slot");

            launcher.ContinueFromSlot(2);
            Assert.AreEqual(2, launcher.PendingLoadSlot,
                "the slot is recorded before launch so composition can pick it up");
            Assert.AreEqual(0, launcher.SessionsStarted);
        }

        // =====================================================================
        // Handing the screen over
        // =====================================================================

        /// <summary>
        /// The menu and the session cannot both own the screen. Two shells means
        /// two owners of the modal gate and two input-gate blocks, which is how a
        /// game ends up with the controls dead and no screen to explain it.
        /// </summary>
        [Test]
        public void TheMenuShellStandsDownCleanly_LeavingNoGateHeld()
        {
            Time.timeScale = 1f;
            InputGate.Suppressed = false;

            NebulaMainMenuScreen menu = Track(NebulaMainMenuScreen.Create(shell, new RecordingLauncher()));
            shell.Push(menu);

            Assert.AreSame(shell, ExclusiveModal.Owner, "an open menu owns the gate");
            Assert.IsTrue(InputGate.Suppressed, "and holds flight input");
            Assert.AreEqual(1f, Time.timeScale,
                "an open screen must not stop the world -- it takes the controls, not the clock");

            shell.Shutdown();

            Assert.IsNull(ExclusiveModal.Owner, "the gate must come back");
            Assert.IsFalse(InputGate.Suppressed, "and so must flight input");
            Assert.AreEqual(1f, Time.timeScale, "and the clock must not have moved at all");
            Assert.AreEqual(0, shell.StackDepth);
        }

        /// <summary>
        /// The parent menu is the session's front door and the rail is how it is
        /// navigated. A rail row that is drawn but not connected looks identical
        /// to one that is, right up until it is clicked.
        /// </summary>
        [Test]
        public void EveryRailRow_ActuallySwitchesToItsPage()
        {
            NebulaParentMenuScreen menu = Track(NebulaParentMenuScreen.Create(shell));
            var pages = (NebulaUiAssetCatalog.Page[])
                System.Enum.GetValues(typeof(NebulaUiAssetCatalog.Page));

            foreach (NebulaUiAssetCatalog.Page page in pages) menu.AddPage(new StubPage(page));
            menu.Open(pages[0]);

            foreach (NebulaUiAssetCatalog.Page page in pages)
            {
                Button row = Control(shell.Root(UiLayer.Menu), page + " Rail");
                Assert.IsNotNull(row, $"{page} has no clickable rail row");

                menu.ShowPage(pages[0]);
                row.onClick.Invoke();
                Assert.AreEqual(page, menu.CurrentPage,
                    $"clicking the {page} rail row did not switch to it");
            }
        }

        private sealed class StubPage : INebulaMenuPage
        {
            public StubPage(NebulaUiAssetCatalog.Page page) { Page = page; }
            public NebulaUiAssetCatalog.Page Page { get; }
            public string Title => Page.ToString();
            public int AttentionCount => 0;
            public int Refreshes { get; private set; }
            public void Build(RectTransform host) { }
            public void Refresh() => Refreshes++;
            public bool HandleBack() => false;
        }

        // =====================================================================
        // Proof the checks can fail
        // =====================================================================

        /// <summary>
        /// Green is not the same as covered. A control with no listener, and a
        /// control whose listener does nothing, must both be rejected by the same
        /// helper the tests above trust — otherwise those tests prove nothing.
        /// </summary>
        [Test]
        public void TheWiringCheckRejectsADeadControl()
        {
            int counter = 0;

            NebulaUiFactory.ButtonHandle dead = NebulaUiFactory.ButtonControl(
                shell.Root(UiLayer.Menu), "Dead", "ÖLÜ",
                NebulaUiAssetCatalog.ButtonKind.Secondary, null);
            Assert.IsFalse(ClickChanges(dead.Button, () => counter),
                "a control with no listener was reported as wired");

            NebulaUiFactory.ButtonHandle inert = NebulaUiFactory.ButtonControl(
                shell.Root(UiLayer.Menu), "Inert", "ETKİSİZ",
                NebulaUiAssetCatalog.ButtonKind.Secondary, () => { });
            Assert.IsFalse(ClickChanges(inert.Button, () => counter),
                "a control whose listener does nothing was reported as wired");

            NebulaUiFactory.ButtonHandle live = NebulaUiFactory.ButtonControl(
                shell.Root(UiLayer.Menu), "Live", "CANLI",
                NebulaUiAssetCatalog.ButtonKind.Secondary, () => counter++);
            Assert.IsTrue(ClickChanges(live.Button, () => counter),
                "and a control that does something must still pass");

            Assert.IsNull(Control(shell.Root(UiLayer.Menu), "NoSuchControl"),
                "the lookup must return nothing rather than the first button it finds");
        }
    }
}
