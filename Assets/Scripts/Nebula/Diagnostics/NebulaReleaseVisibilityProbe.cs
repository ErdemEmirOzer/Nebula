using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nebula.Diagnostics
{
    /// <summary>
    /// Runs the <b>release</b> side of every <c>Debug.isDebugBuild</c> gate on a real player.
    /// <para>
    /// Nothing else in this project can. The Editor, every test runner and the Development
    /// player all report <c>isDebugBuild == true</c>, so each of them takes the development
    /// branch of those gates — the release branch has never been executed anywhere. The
    /// existing guard, <c>UiFinalReleaseVisibilityTests</c>, reads
    /// <c>PresentationInstaller.cs</c> and confirms by brace depth that the overlay
    /// construction sits inside an <c>isDebugBuild</c> branch. That is a real check and it is
    /// still a check on the <i>text</i>: it proves the branch was written, never that a
    /// shipped player behaves. This probe closes that gap by asking the running non-Development
    /// player itself.
    /// </para>
    /// <para>
    /// Deliberately NOT part of <see cref="NebulaSmokeRunner"/>. That runner is
    /// Development-only by design and writes save slots; its guarantee is worth keeping
    /// verbatim. This is a separate, read-only observer: it starts a campaign through the
    /// menu's own launcher, reads what composition produced, and returns. It writes no save
    /// data, touches no player preference, and creates no UI of its own — a probe that drew
    /// something would be the very thing it is here to rule out.
    /// </para>
    /// <para>
    /// Its only gate is an explicit command-line flag, because <c>isDebugBuild</c> cannot be
    /// the gate on the code whose job is to observe a build where that property is false. A
    /// player who never passes <c>-nebulaReleaseCheck</c> never constructs any of this: the
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/> hook returns on its second line.
    /// </para>
    /// </summary>
    public static class NebulaReleaseVisibilityProbe
    {
        public const string Flag = "-nebulaReleaseCheck";
        public const string OutputFlag = "-nebulaReleaseCheckOutput";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void TryStart()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, Flag) < 0) return;

            string outputPath = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == OutputFlag) { outputPath = args[i + 1]; break; }
            // Inside the sandbox when an isolated smoke run supplied one, for the same
            // reason NebulaSmokeRunner does it: a report written to the real folder is
            // still a write to the real folder.
            if (string.IsNullOrEmpty(outputPath))
                outputPath = Platform.NebulaSmokeDataRoot.ReportPath("nebula-release-visibility.json")
                             ?? Path.Combine(Application.persistentDataPath, "nebula-release-visibility.json");

            var go = new GameObject("NebulaReleaseVisibilityProbe");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<NebulaReleaseVisibilityProbeComponent>().OutputPath = outputPath;
        }
    }

    public class NebulaReleaseVisibilityProbeComponent : MonoBehaviour
    {
        public string OutputPath;
        private readonly List<string> unhandledLogs = new List<string>();

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error) unhandledLogs.Add(condition);
        }

        private IEnumerator Start()
        {
            var report = new SmokeReport
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString()
            };

            // First and load-bearing. Every other check below is only evidence about a
            // release player if this one passed -- run against a Development build the
            // same assertions would pass for the opposite reason, and the report would
            // read identically. So the branch under test is recorded as a check, not as
            // a note somebody has to remember to read.
            Add(report, "player_is_a_non_development_build", !Debug.isDebugBuild,
                "Debug.isDebugBuild=" + Debug.isDebugBuild);

            UI.Session.NebulaSessionLauncher launcher = null;

            // Production boots to the menu with no world behind it; gameplay state does
            // not exist until the menu's own launcher makes it.
            yield return null;
            yield return null;

            try
            {
                CheckBootsToMainMenu(report, out launcher);
            }
            catch (Exception e)
            {
                Add(report, "unhandled_exception_in_menu_phase", false, e.ToString());
            }

            if (launcher != null) launcher.StartNewCampaign();

            // The menu host stands itself down in Update, so the counts below are only
            // correct on the frame after that has run.
            yield return null;
            yield return null;
            yield return null;

            try
            {
                World.Composition.NebulaContext context = ResolveContext(report);
                World.Composition.NebulaPresentationHandle presentation = PresentationOf(context);
                CheckNoDiagnosticOverlayExists(report, presentation);
                CheckTheNormalHudIsThere(report, presentation);
                CheckTheMenuStillOpens(report, presentation);
                CheckPersistenceIsIsolated(report);
                CheckSaveLoadRoundTrip(report, context);
                CheckInputBindingRoundTrip(report);
            }
            catch (Exception e)
            {
                Add(report, "unhandled_exception_in_release_probe", false, e.ToString());
            }

            if (launcher != null) launcher.ReturnToMainMenu();
            yield return null;
            yield return null;

            try
            {
                UI.Session.NebulaMainMenuHost host = FindMenuHost();
                Add(report, "returned_to_a_main_menu_that_is_on_screen",
                    host != null && host.MenuVisible, host == null ? "no host" : "");
            }
            catch (Exception e)
            {
                Add(report, "unhandled_exception_in_return_phase", false, e.ToString());
            }

            Add(report, "no_unhandled_exceptions_or_errors_logged", unhandledLogs.Count == 0,
                unhandledLogs.Count > 0 ? string.Join(" | ", unhandledLogs) : "");

            report.allPassed = true;
            foreach (SmokeCheckResult c in report.checks) if (!c.passed) report.allPassed = false;

            try
            {
                string dir = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(OutputPath, JsonUtility.ToJson(report, true));
            }
            catch { /* the exit code stays the authoritative signal even if the write fails */ }

            // Same rule as the Development smoke: a pass leaves through the menu's own
            // Quit seam, so "the player really does exit" is evidence this run produced.
            // A failure keeps the explicit non-zero code, which QuitGame() cannot carry.
            UI.Session.NebulaMainMenuHost menu = FindMenuHost();
            if (report.allPassed && menu != null && menu.Launcher != null) menu.Launcher.QuitGame();
            else Application.Quit(report.allPassed ? 0 : 1);
        }

        private static UI.Session.NebulaMainMenuHost FindMenuHost() =>
            UnityEngine.Object.FindObjectOfType<UI.Session.NebulaMainMenuHost>();

        private void CheckBootsToMainMenu(SmokeReport report, out UI.Session.NebulaSessionLauncher launcher)
        {
            launcher = null;

            var bootstraps = UnityEngine.Object.FindObjectsOfType<World.SolarSystemBootstrap>();
            Add(report, "exactly_one_solar_system_bootstrap", bootstraps.Length == 1, $"found {bootstraps.Length}");
            if (bootstraps.Length != 1) return;

            Add(report, "production_boot_mode_is_main_menu",
                bootstraps[0].BootMode == World.NebulaBootMode.MainMenu, bootstraps[0].BootMode.ToString());
            Add(report, "no_gameplay_session_behind_the_menu", !bootstraps[0].SessionActive, "");

            UI.Session.NebulaMainMenuHost host = FindMenuHost();
            Add(report, "main_menu_is_on_screen", host != null && host.MenuVisible, host == null ? "no host" : "");
            if (host != null) launcher = host.Launcher;
            Add(report, "menu_has_a_session_launcher", launcher != null, "");
        }

        private World.Composition.NebulaContext ResolveContext(SmokeReport report)
        {
            var bootstraps = UnityEngine.Object.FindObjectsOfType<World.SolarSystemBootstrap>();
            World.Composition.NebulaContext context = bootstraps.Length == 1 ? bootstraps[0].Context : null;
            Add(report, "campaign_started_from_the_menu", context != null,
                context == null ? "no context after StartNewCampaign()" : "");
            return context;
        }

        /// <summary>
        /// The presentation handle, or <c>null</c> if composition never produced one.
        /// <c>NebulaContext</c> throws on an unset property by design, and a throw here
        /// would take the diagnostic-overlay checks down with it -- which is precisely
        /// the evidence this probe exists to collect. A missing handle is recorded as a
        /// failure by each check that needed it, not raised.
        /// </summary>
        private static World.Composition.NebulaPresentationHandle PresentationOf(
            World.Composition.NebulaContext context)
        {
            if (context == null) return null;
            try { return context.Presentation; }
            catch { return null; }
        }

        /// <summary>
        /// The point of the whole exercise. Absence is asserted three independent ways,
        /// because each one can be true while the build is still wrong: the component may
        /// be gone while a stray GameObject survives, the scene may be clean while the
        /// typed handle still points at something, and both may be clean while a leftover
        /// canvas draws the counters anyway.
        /// </summary>
        private void CheckNoDiagnosticOverlayExists(SmokeReport report,
            World.Composition.NebulaPresentationHandle presentation)
        {
            int sector = UnityEngine.Object.FindObjectsOfType<UI.SectorDebugHud>(true).Length;
            Add(report, "no_sector_debug_hud_component_exists", sector == 0, $"found {sector}");

            int flight = UnityEngine.Object.FindObjectsOfType<UI.FlightTestSpeedHud>(true).Length;
            Add(report, "no_flight_test_speed_hud_component_exists", flight == 0, $"found {flight}");

            var strays = new List<string>();
            foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go.name == "Sector Debug HUD" || go.name == "Flight Test Speed HUD" ||
                    go.name == "Test Speed Panel") strays.Add(go.name);
            }
            Add(report, "no_diagnostic_overlay_gameobject_survives", strays.Count == 0,
                strays.Count > 0 ? string.Join(", ", strays) : "");

            if (presentation != null)
            {
                Add(report, "presentation_handle_holds_no_debug_huds",
                    presentation.SectorDebugHud == null && presentation.FlightTestSpeedHud == null,
                    $"sector={(presentation.SectorDebugHud == null ? "null" : "set")} " +
                    $"flight={(presentation.FlightTestSpeedHud == null ? "null" : "set")}");
            }
            else
            {
                Add(report, "presentation_handle_holds_no_debug_huds", false, "no presentation handle to read");
            }

            // The counters themselves, not the object that owns them. Reads every legacy
            // Text on the scene, so a diagnostic readout reintroduced under any other
            // class or name is still caught.
            var offenders = new List<string>();
            foreach (UnityEngine.UI.Text text in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>(true))
            {
                string body = text.text ?? "";
                if (body.Contains("SEKTÖR DEBUG") || body.Contains("Manifest cache:") ||
                    body.Contains("Son promotion:")) offenders.Add(text.name + ": " + body.Split('\n')[0]);
            }
            Add(report, "no_debug_manifest_counters_are_rendered", offenders.Count == 0,
                offenders.Count > 0 ? string.Join(" | ", offenders) : "");
        }

        /// <summary>
        /// A release build with nothing on screen would pass every absence check above.
        /// The player's own interface has to still be there, and be drawing.
        /// </summary>
        private void CheckTheNormalHudIsThere(SmokeReport report,
            World.Composition.NebulaPresentationHandle presentation)
        {
            int shells = UnityEngine.Object.FindObjectsOfType<UI.Shell.NebulaUiShell>().Length;
            Add(report, "exactly_one_ui_shell_during_gameplay", shells == 1, $"found {shells}");

            if (presentation == null)
            {
                Add(report, "every_player_facing_hud_panel_exists", false, "no presentation handle to read");
                return;
            }

            var missing = new List<string>();
            void Require(string name, Component c)
            {
                if (c == null) { missing.Add(name + " (null)"); return; }
                if (!c.gameObject.activeInHierarchy) missing.Add(name + " (inactive)");
            }

            Require("context bar", presentation.HudContextBar);
            Require("vitals", presentation.HudVitals);
            Require("navigation", presentation.HudNavigation);
            Require("alerts", presentation.HudAlerts);
            Require("planet context", presentation.HudPlanetContext);
            Require("notifications", presentation.HudNotifications);
            Require("parent menu", presentation.ParentMenu);
            Add(report, "every_player_facing_hud_panel_exists", missing.Count == 0,
                missing.Count > 0 ? string.Join(", ", missing) : "");

            // Existing is not drawing. Count the graphics actually on screen under the
            // session shell, so a HUD that composed and then rendered nothing fails here.
            int drawn = 0;
            if (presentation.UiShell != null)
            {
                foreach (UnityEngine.UI.Graphic g in
                         presentation.UiShell.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                {
                    if (g.gameObject.activeInHierarchy && g.enabled) drawn++;
                }
            }
            Add(report, "the_hud_actually_draws_something", drawn > 0, $"{drawn} active graphics under the shell");
        }

        /// <summary>The menus a player uses have to open in the build a player gets.</summary>
        private void CheckTheMenuStillOpens(SmokeReport report,
            World.Composition.NebulaPresentationHandle presentation)
        {
            UI.Menu.NebulaParentMenuScreen menu = presentation?.ParentMenu;
            if (menu == null) { Add(report, "the_parent_menu_opens_and_closes", false, "no parent menu"); return; }

            bool opened = menu.Open(UI.Design.NebulaUiAssetCatalog.Page.Overview);
            bool isOpen = menu.IsOpen;
            UI.Design.NebulaUiAssetCatalog.Page? page = menu.CurrentPage;
            menu.Close();
            Add(report, "the_parent_menu_opens_and_closes", opened && isOpen && !menu.IsOpen,
                $"open={opened} wasOpen={isOpen} page={page} closed={!menu.IsOpen}");

            // A second, different page. Overview alone would pass on a build where every
            // other page failed to compose, because the menu shell is shared and only the
            // page content differs -- and Resources is the page a player reaches for
            // first, so it is the one worth naming.
            bool openedResources = menu.Open(UI.Design.NebulaUiAssetCatalog.Page.Resources);
            UI.Design.NebulaUiAssetCatalog.Page? resourcesPage = menu.CurrentPage;
            bool onResources = menu.IsOpen &&
                resourcesPage == UI.Design.NebulaUiAssetCatalog.Page.Resources;
            menu.Close();
            Add(report, "the_resources_page_opens", openedResources && onResources,
                $"open={openedResources} page={resourcesPage} closed={!menu.IsOpen}");
        }

        /// <summary>
        /// Where this run's writes are actually going, asserted before anything writes.
        /// <para>
        /// Ordered first among the persistence checks on purpose. The two below it write
        /// files, and "the release player can save and load" is only worth having if the
        /// saving happened somewhere the operator agreed to lose. A previous smoke run
        /// with no isolation rewrote a real telemetry file that could not be restored, so
        /// the destination is now evidence the report carries rather than a property of
        /// however the run happened to be launched.
        /// </para>
        /// <para>
        /// Every folder is asserted individually. One <c>??</c> chain left un-widened is
        /// exactly the shape of the original incident: three folders redirected, one
        /// still pointing at the player's own data.
        /// </para>
        /// </summary>
        private void CheckPersistenceIsIsolated(SmokeReport report)
        {
            string sandbox = Platform.NebulaSmokeDataRoot.Root;
            Add(report, "persistent_data_is_isolated_from_the_real_user_folder",
                sandbox != null,
                sandbox ?? Platform.NebulaSmokeDataRoot.RefusalReason);
            if (sandbox == null) return;

            var strays = new List<string>();
            void Require(string label, string path)
            {
                if (!Platform.NebulaPersistentPathResolver.IsWithin(sandbox, path))
                    strays.Add(label + " -> " + path);
            }

            Require("saves", Path.GetDirectoryName(Extraction.SaveService.SlotFile(1)));
            Require("telemetry", Telemetry.TelemetryService.TelemetryDirectory);
            Require("settings", Ship.NebulaInputBindingStore.Directory);
            Require("migration active root", Platform.NebulaLegacyDataMigrator.LastReport.activeRoot);
            Require("migration legacy root", Platform.NebulaLegacyDataMigrator.LastReport.legacyRoot);

            Add(report, "every_persistence_folder_resolves_inside_the_sandbox",
                strays.Count == 0,
                strays.Count > 0 ? string.Join(" | ", strays) : "root=" + sandbox);

            // The migration is the one subsystem that reads a folder it does not write,
            // so "it wrote nowhere bad" is not enough -- it must not have *read* the
            // operator's old install either.
            Add(report, "legacy_migration_ran_against_the_sandbox_not_the_real_legacy_folder",
                Platform.NebulaLegacyDataMigrator.StartupCompleted &&
                !string.IsNullOrEmpty(Platform.NebulaLegacyDataMigrator.LastReport.legacyRoot) &&
                Platform.NebulaPersistentPathResolver.IsWithin(
                    sandbox, Platform.NebulaLegacyDataMigrator.LastReport.legacyRoot),
                "outcome=" + Platform.NebulaLegacyDataMigrator.LastReport.outcome +
                " legacy=" + Platform.NebulaLegacyDataMigrator.LastReport.legacyRoot);
        }

        /// <summary>
        /// A real save and a real load on the release player, through the same
        /// <c>ExtractionDirector</c> seam the player's own shortcut uses.
        /// <para>
        /// Refuses to run at all unless <see cref="CheckPersistenceIsIsolated"/> could
        /// find a sandbox. That refusal is the point: the Development smoke has to back
        /// up and restore slot 1 around its round trip, and a backup-and-restore is a
        /// promise, not a mechanism — it is the step that failed to protect a telemetry
        /// file once already. With a sandbox there is nothing to protect, and without one
        /// this check declines rather than reaching for the player's real slot.
        /// </para>
        /// </summary>
        private void CheckSaveLoadRoundTrip(SmokeReport report, World.Composition.NebulaContext context)
        {
            if (!Platform.NebulaSmokeDataRoot.Active)
            {
                Add(report, "real_save_and_load_round_trip", false,
                    "declined: persistence is not isolated (" +
                    Platform.NebulaSmokeDataRoot.RefusalReason + "), so this probe will not write a save");
                return;
            }
            if (context == null || context.ExtractionDirector == null)
            {
                Add(report, "real_save_and_load_round_trip", false, "no ExtractionDirector");
                return;
            }

            try
            {
                string slot = Extraction.SaveService.SlotFile(1);
                context.ExtractionDirector.SaveNow(null);
                bool written = File.Exists(slot);
                context.ExtractionDirector.LoadNow();
                Add(report, "real_save_and_load_round_trip", written,
                    written ? slot : "SaveNow() left no file at " + slot);
            }
            catch (Exception e)
            {
                Add(report, "real_save_and_load_round_trip", false, e.ToString());
            }
        }

        /// <summary>
        /// The rebind file survives a write and a read in the shipped player.
        /// <para>
        /// Worth its own check because it is the one persistent file with a startup hook
        /// of its own: it is read in <c>BeforeSceneLoad</c>, long before anything here
        /// runs, so a build where the settings folder resolves differently at startup
        /// than it does later would still pass every save check above.
        /// </para>
        /// </summary>
        private void CheckInputBindingRoundTrip(SmokeReport report)
        {
            if (!Platform.NebulaSmokeDataRoot.Active)
            {
                Add(report, "input_bindings_save_and_reload", false,
                    "declined: persistence is not isolated, so this probe will not write settings");
                return;
            }

            try
            {
                bool saved = Ship.NebulaInputBindingStore.Save();
                bool exists = Ship.NebulaInputBindingStore.Exists();
                int loaded = Ship.NebulaInputBindingStore.Load();
                Add(report, "input_bindings_save_and_reload", saved && exists && loaded >= 0,
                    $"saved={saved} exists={exists} loaded={loaded} at {Ship.NebulaInputBindingStore.FilePath}");
            }
            catch (Exception e)
            {
                Add(report, "input_bindings_save_and_reload", false, e.ToString());
            }
        }

        private static void Add(SmokeReport report, string name, bool passed, string detail) =>
            report.checks.Add(new SmokeCheckResult { name = name, passed = passed, detail = detail ?? "" });
    }
}
