using UnityEngine;
using Nebula.Tutorial;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.UI.Tutorial;
using Nebula.World;
using Nebula.Visuals;
using Nebula.Ship;

namespace Nebula.UI.Session
{
    /// <summary>
    /// Owns the main menu before a session exists.
    /// <para>
    /// The rest of the interface is built by <c>PresentationInstaller</c>, which
    /// only runs once a session is composed — so a menu-first launch had nowhere
    /// to live. <c>SolarSystemBootstrap.DeferToMainMenu</c> documented that flow
    /// and, on its own, produced a black screen: no session meant no presentation,
    /// no presentation meant no shell, and no shell meant no menu.
    /// </para>
    /// <para>
    /// This host is the missing half. It builds its own shell, shows the menu, and
    /// stands down the moment a session starts — the session's own shell takes
    /// over from there, so the two never coexist and the modal gate never has two
    /// owners.
    /// </para>
    /// </summary>
    public sealed class NebulaMainMenuHost : MonoBehaviour
    {
        public static NebulaMainMenuHost Active { get; private set; }

        private SolarSystemBootstrap bootstrap;
        private NebulaUiShell shell;
        private NebulaMainMenuScreen menu;

        /// <summary>Test-facing: whether the menu is currently on screen.</summary>
        public bool MenuVisible => menu != null && shell != null;

        /// <summary>Test-facing: the launcher the menu acts through.</summary>
        public NebulaSessionLauncher Launcher { get; private set; }

        // Built on first use rather than up front. Three secondary screens the
        // player may never open should not be three built object trees behind the
        // front door of every boot -- and the settings page in particular writes
        // live globals the moment it refreshes.
        private NebulaMenuPageScreen settings;
        private NebulaMenuPageScreen accessibility;
        private NebulaSaveSlotScreen load;
        private NebulaCreditsScreen credits;
        private TutorialModeScreen tutorialModes;
        private TutorialChapterListScreen tutorialChapters;
        private readonly RuntimeResourceScope resources = new RuntimeResourceScope();

        /// <summary>
        /// How a tutorial run is started. Resolved from
        /// <see cref="TutorialLaunchService"/> at build time and settable so a
        /// fixture — and a future composition root — can hand one in directly.
        /// <para>
        /// Kept separate from <see cref="Launcher"/> on purpose. The tutorial must
        /// never reach <c>BeginSession</c>: no campaign id, no save lineage, no
        /// session. Two launchers is how that stays true by construction rather than
        /// by discipline.
        /// </para>
        /// </summary>
        public ITutorialLauncher TutorialLauncher { get; set; }

        /// <summary>Test-facing: the tutorial mode screen, once it has been opened at least once.</summary>
        public TutorialModeScreen TutorialModeScreen => tutorialModes;

        /// <summary>Test-facing: the chapter list, once it has been opened at least once.</summary>
        public TutorialChapterListScreen TutorialChapterScreen => tutorialChapters;

        /// <summary>Test-facing: the settings screen, once it has been opened at least once.</summary>
        public NebulaMenuPageScreen SettingsScreen => settings;

        /// <summary>Test-facing: the accessibility screen, once it has been opened at least once.</summary>
        public NebulaMenuPageScreen AccessibilityScreen => accessibility;

        /// <summary>Test-facing: the save-slot screen, once it has been opened at least once.</summary>
        public NebulaSaveSlotScreen LoadScreen => load;

        /// <summary>Test-facing: the credits screen, once it has been opened at least once.</summary>
        public NebulaCreditsScreen CreditsScreen => credits;

        public static NebulaMainMenuHost Create(SolarSystemBootstrap solarSystemBootstrap)
        {
            if (Active != null) return Active;
            var go = new GameObject("Main Menu Host");
            var host = go.AddComponent<NebulaMainMenuHost>();
            Active = host;
            host.bootstrap = solarSystemBootstrap;
            using (host.resources.Activate()) host.Build();
            return host;
        }

        private void Build()
        {
            shell = NebulaUiShell.Create();
            Launcher = new NebulaSessionLauncher(bootstrap);

            // The menu acts through a decorator, not through Launcher directly, so
            // every entry that loads something raises the loading surface first. The
            // shell is passed as a delegate because this host's shell is destroyed
            // the instant a session starts, and the decorator outlives that moment.
            //
            // Launcher itself stays the undecorated one: it is the lifecycle
            // authority and what the tests assert against, and wrapping the property
            // would make "did a session start" a question about presentation.
            menu = NebulaMainMenuScreen.Create(shell,
                new Brand.NebulaLoadingSessionLauncher(Launcher, () => shell));

            // The three secondary entries. The screen has raised these events since
            // it was written and nothing in production ever subscribed, so AYARLAR,
            // KAYIT YÜKLE and KÜNYE were controls that answered a click with
            // nothing -- worse than absent, because absent is at least honest. The
            // host is the only thing that owns both the shell and the screen, so it
            // is the only thing that can answer them.
            menu.SettingsRequested += OpenSettings;
            menu.AccessibilityRequested += OpenAccessibility;
            menu.LoadRequested += OpenLoad;
            menu.CreditsRequested += OpenCredits;
            menu.TutorialRequested += OpenTutorial;

            // Asked for once, here, rather than every time the entry is drawn: the
            // service hands back a launcher bound to a live bootstrap, and the menu
            // has exactly one lifetime.
            // Registered first, constructed second. The service is how a composition
            // root or a fixture substitutes a launcher; the fallback is what stops
            // the front door being a dead control when nobody has.
            if (TutorialLauncher == null) TutorialLauncher = TutorialLaunchService.Resolve();
            if (TutorialLauncher == null) TutorialLauncher = new Nebula.Tutorial.TutorialLauncher(bootstrap);

            // Decorated for the same reason the session launcher is: BAŞTAN BAŞLA and
            // a single chapter both additively load a scene, which is the one
            // transition here that genuinely spans frames. Wrapped once, after the
            // resolution order above has settled, so a launcher handed in by a
            // fixture is wrapped too and the surface is not something production gets
            // and tests do not.
            if (TutorialLauncher != null && !(TutorialLauncher is Brand.NebulaLoadingTutorialLauncher))
                TutorialLauncher = new Brand.NebulaLoadingTutorialLauncher(TutorialLauncher, () => shell);

            // Reachable when there is either a way to run the tutorial or a
            // curriculum to read. Both absent is the one case where the entry would
            // open a screen with nothing on it, and an entry that leads nowhere is
            // worse than an entry that says why.
            menu.SetTutorialAvailable(
                TutorialLauncher != null || TutorialStepCatalog.Count > 0,
                TutorialUiCopy.NoLauncher);

            shell.Push(menu);
            menu.Refresh();
        }

        /// <summary>
        /// The real settings page, with no session behind it.
        /// <para>
        /// <see cref="NebulaSettingsPage"/> takes an optional context and skips only
        /// the audio section without one, so the front door reaches the same page the
        /// campaign does rather than a second, thinner settings authority that would
        /// drift out of step with it. Everything it writes is a process-wide static
        /// or a Unity setting, which is why a change made here survives into the
        /// campaign the player then starts.
        /// </para>
        /// </summary>
        public void OpenSettings()
        {
            if (shell == null) return;
            if (settings == null) settings = NebulaMenuPageScreen.Create(shell, new NebulaSettingsPage());
            shell.Push(settings);
        }

        /// <summary>
        /// The accessibility front door: the same page, built with only the rows a
        /// player comes here for. Cached like the others, so opening it twice is one
        /// screen rather than two.
        /// </summary>
        public void OpenAccessibility()
        {
            if (shell == null) return;
            if (accessibility == null)
                accessibility = NebulaMenuPageScreen.Create(shell,
                    new NebulaSettingsPage(accessibilityFrontDoor: true), "accessibility");
            shell.Push(accessibility);
        }

        public void OpenLoad()
        {
            if (shell == null) return;
            if (load == null) load = NebulaSaveSlotScreen.Create(shell, Launcher);
            shell.Push(load);
        }

        public void OpenCredits()
        {
            if (shell == null) return;
            if (credits == null) credits = NebulaCreditsScreen.Create(shell);
            shell.Push(credits);
        }

        /// <summary>
        /// EĞİTİM. Opens the tutorial's own front door and starts nothing.
        /// <para>
        /// Cached like the others, so opening it three times is one screen. The
        /// tutorial launcher is handed down rather than looked up again — a screen
        /// that could resolve its own launcher is a screen that could acquire one
        /// after the host has stood down.
        /// </para>
        /// </summary>
        public void OpenTutorial()
        {
            if (shell == null) return;
            if (tutorialModes == null)
            {
                tutorialModes = TutorialModeScreen.Create(shell, TutorialLauncher);
                tutorialModes.ChaptersRequested += OpenTutorialChapters;
                tutorialModes.RunStarted += OnTutorialRunStarted;
            }
            shell.Push(tutorialModes);
        }

        /// <summary>BÖLÜMLER, pushed on top of the mode screen so GERİ returns to it.</summary>
        public void OpenTutorialChapters()
        {
            if (shell == null) return;
            if (tutorialChapters == null)
            {
                tutorialChapters = TutorialChapterListScreen.Create(shell, TutorialLauncher);
                tutorialChapters.RunStarted += OnTutorialRunStarted;
            }
            shell.Push(tutorialChapters);
        }

        /// <summary>
        /// A tutorial run has begun, so the front door's shell stands down exactly as
        /// it does for a campaign.
        /// <para>
        /// It cannot wait for <see cref="SolarSystemBootstrap.SessionActive"/> the way
        /// <see cref="Update"/> does, because a tutorial run deliberately never sets
        /// it — no session is composed, no campaign id is minted. Without this the
        /// menu's shell would survive underneath the running tutorial, holding the
        /// pause and the modal gate against the run's own interface.
        /// </para>
        /// </summary>
        private void OnTutorialRunStarted() => Dismiss();

        /// <summary>
        /// The menu's job ends when a campaign begins. Checked rather than pushed
        /// from the launcher so that a session started any other way -- a test, a
        /// console, a future deep link -- also dismisses it.
        /// </summary>
        private void Update()
        {
            if (shell == null) return;

            if (bootstrap != null && bootstrap.SessionActive) { Dismiss(); return; }

            // Escape closes a secondary screen and never the front door itself: the
            // main menu is the root, and the parent menu's Escape handler does not
            // exist before a session does, so without this the only way off a
            // settings screen would be the mouse.
            if (NebulaInputRouter.SharedPressed(NebulaInputActions.MenuBack) && shell.StackDepth > 1) shell.Back();
        }

        /// <summary>
        /// Tears down the menu's own shell so the session's shell is the only one.
        /// Idempotent: a second call after the first is a no-op rather than a null
        /// dereference, because both Update and an explicit teardown can reach it.
        /// </summary>
        public void Dismiss()
        {
            if (shell == null) return;

            if (Active == this) Active = null;

            NebulaUiShell doomed = shell;
            shell = null;

            // The overlay lives on the shell about to be destroyed. Cleared here
            // rather than left to die with it so the presenter's nesting depth goes
            // back to zero as well -- a depth left standing would make the next
            // operation believe an overlay was already up and show none.
            Brand.NebulaLoadingPresenter.ForceClear();

            // Unsubscribed before the screen goes: the events are instance events on
            // a screen this host is about to destroy, but a handler left attached to
            // a live screen after the host has stood down would push a screen onto a
            // shell that no longer exists.
            if (menu != null)
            {
                menu.SettingsRequested -= OpenSettings;
                menu.AccessibilityRequested -= OpenAccessibility;
                menu.LoadRequested -= OpenLoad;
                menu.CreditsRequested -= OpenCredits;
                menu.TutorialRequested -= OpenTutorial;
            }

            // The tutorial screens raise their own events, and this host is the
            // subscriber. A handler left attached would push a screen onto a shell
            // that no longer exists -- and OnTutorialRunStarted would call Dismiss on
            // a host that has already gone.
            if (tutorialModes != null)
            {
                tutorialModes.ChaptersRequested -= OpenTutorialChapters;
                tutorialModes.RunStarted -= OnTutorialRunStarted;
            }
            if (tutorialChapters != null) tutorialChapters.RunStarted -= OnTutorialRunStarted;

            menu = null;
            settings = null;
            accessibility = null;
            load = null;
            credits = null;
            tutorialModes = null;
            tutorialChapters = null;

            doomed.Shutdown();
            resources.Dispose();
            DestroyNow(doomed.gameObject);
            DestroyNow(gameObject);
        }

        /// <summary>
        /// <c>Destroy</c> is deferred and simply errors outside play mode, which
        /// left every EditMode test that built a host leaking a shell into the next
        /// one -- four shells and sixteen canvases by the end of a fixture.
        /// </summary>
        private static void DestroyNow(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            resources.Dispose();
            // Only the shell needs releasing; Shutdown already frees the pause block
            // and the modal gate, and refuses to do it twice.
            if (shell == null) return;
            shell.Shutdown();
        }
    }
}
