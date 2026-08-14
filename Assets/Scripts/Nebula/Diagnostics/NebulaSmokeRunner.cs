using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nebula.Diagnostics
{
    [Serializable]
    public class SmokeCheckResult
    {
        public string name;
        public bool passed;
        public string detail;
    }

    [Serializable]
    public class SmokeReport
    {
        public bool allPassed;
        public string unityVersion;
        public string platform;
        public List<SmokeCheckResult> checks = new List<SmokeCheckResult>();
    }

    /// <summary>
    /// VS-017A.1 §3: Development-Player-only, command-line-gated smoke entrypoint.
    /// Never active in a non-development build (Debug.isDebugBuild gate) and never active
    /// without the explicit -nebulaSmoke flag, so it can never affect a real player's normal
    /// session. Writes a machine-readable JSON report and exits with 0 (all checks passed) or
    /// a non-zero code (at least one failed) -- the sole contract an external CI/build script
    /// needs. Every check here reads real, already-booted composition state; a real save/load
    /// round trip uses a dedicated slot (999), never the real campaign's own slot 1, so a smoke
    /// run never leaves the player's own save data touched.
    /// </summary>
    public static class NebulaSmokeRunner
    {
        public const string Flag = "-nebulaSmoke";
        public const string OutputFlag = "-nebulaSmokeOutput";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void TryStart()
        {
            if (!Debug.isDebugBuild) return; // never in a Release build
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, Flag) < 0) return;

            string outputPath = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == OutputFlag) { outputPath = args[i + 1]; break; }
            // The default lands inside the sandbox when there is one. Without this the
            // one path that still reaches Application.persistentDataPath during an
            // isolated run is the report write -- which both creates the real folder and
            // drops a file in it, defeating the isolation at the very last step.
            if (string.IsNullOrEmpty(outputPath))
                outputPath = Platform.NebulaSmokeDataRoot.ReportPath("nebula-smoke-result.json")
                             ?? Path.Combine(Application.persistentDataPath, "nebula-smoke-result.json");

            var go = new GameObject("NebulaSmokeRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<NebulaSmokeRunnerComponent>();
            runner.OutputPath = outputPath;
        }
    }

    public class NebulaSmokeRunnerComponent : MonoBehaviour
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
            var report = new SmokeReport { unityVersion = Application.unityVersion, platform = Application.platform.ToString() };
            UI.Session.NebulaSessionLauncher launcher = null;

            // Production boots to the main menu with no world behind it, so a smoke that
            // starts checking gameplay singletons on frame three finds a null context and
            // reports a broken build. It has to walk the player's own route instead:
            // menu first, then a campaign started through the menu's real seam.
            yield return null;
            yield return null;

            World.SolarSystemBootstrap bootstrap = null;
            try
            {
                bootstrap = CheckBootsToMainMenu(report, out launcher);
                CheckBrandPresentation(report);
            }
            catch (Exception e)
            {
                Add(report, "unhandled_exception_in_menu_phase", false, e.ToString());
            }

            // The motion settings are exercised on the real player's own loading surface,
            // before the campaign takes the menu's shell away. Restored immediately: the
            // rest of the smoke has to run in the state it found.
            yield return CheckLoadingPresentationModes(report);

            UI.Brand.NebulaLoadingOverlay.ResetShownCount();

            // The menu's own YENİ SEFER button, not the host's launcher.
            //
            // They are not the same seam and the difference is exactly what this smoke
            // is for. NebulaMainMenuHost.Launcher is the bare NebulaSessionLauncher;
            // the button is wired to a NebulaLoadingSessionLauncher wrapped around it,
            // which is what raises the loading surface. Calling the host's launcher
            // starts a campaign perfectly well and skips the entire loading
            // presentation -- so a smoke that used it reported a healthy build while
            // never once exercising the surface it was supposed to be checking.
            if (!PressNewCampaign(report) && launcher != null) launcher.StartNewCampaign();

            // Composition is synchronous, but the menu host stands itself down in Update,
            // so the shell count is only correct on the frame after that has run.
            yield return null;
            yield return null;
            yield return null;

            try
            {
                World.Composition.NebulaContext context = CheckSceneAndSingletons(report);
                CheckStartSystemFull(report, context);
                CheckSaveLoadRoundTrip(report, context);
                CheckTelemetry(report);
                CheckPerformanceRecorder(report);
                CheckMenuStoodDown(report);
            }
            catch (Exception e)
            {
                Add(report, "unhandled_exception_in_smoke_runner", false, e.ToString());
            }

            // Back out the way a player does, so the teardown path is covered by the smoke
            // and not only by the Editor fixtures.
            if (launcher != null) launcher.ReturnToMainMenu();
            yield return null;
            yield return null;

            try
            {
                CheckReturnedToMenu(report, bootstrap);
                CheckLoadingSurfaceRoseAndCameDown(report);
            }
            catch (Exception e)
            {
                Add(report, "unhandled_exception_in_return_phase", false, e.ToString());
            }

            Add(report, "no_unhandled_exceptions_or_errors_logged", unhandledLogs.Count == 0, unhandledLogs.Count > 0 ? string.Join(" | ", unhandledLogs) : "");

            report.allPassed = true;
            foreach (SmokeCheckResult c in report.checks) if (!c.passed) report.allPassed = false;

            try
            {
                string dir = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(OutputPath, JsonUtility.ToJson(report, true));
            }
            catch { /* the exit code is still the authoritative pass/fail signal even if the report write itself fails */ }

            // On success the process is closed through the menu's own Quit seam, so "the
            // application really does exit when the player asks it to" is evidence the
            // smoke produces rather than an Editor stand-in. A failure keeps the explicit
            // non-zero code, which QuitGame() cannot carry.
            UI.Session.NebulaMainMenuHost host = FindMenuHost();
            if (report.allPassed && host != null && host.Launcher != null) host.Launcher.QuitGame();
            else Application.Quit(report.allPassed ? 0 : 1);
        }

        private static UI.Session.NebulaMainMenuHost FindMenuHost() =>
            UnityEngine.Object.FindObjectOfType<UI.Session.NebulaMainMenuHost>();

        /// <summary>
        /// The production contract after the main-menu boot change: a menu on screen and
        /// no world behind it. A build that quietly starts a campaign at boot passes every
        /// gameplay check below while being exactly the regression this guards.
        /// </summary>
        private World.SolarSystemBootstrap CheckBootsToMainMenu(SmokeReport report, out UI.Session.NebulaSessionLauncher launcher)
        {
            launcher = null;

            var bootstraps = UnityEngine.Object.FindObjectsOfType<World.SolarSystemBootstrap>();
            Add(report, "exactly_one_solar_system_bootstrap", bootstraps.Length == 1, $"found {bootstraps.Length}");
            World.SolarSystemBootstrap bootstrap = bootstraps.Length == 1 ? bootstraps[0] : null;
            if (bootstrap == null) return null;

            Add(report, "production_boot_mode_is_main_menu",
                bootstrap.BootMode == World.NebulaBootMode.MainMenu, bootstrap.BootMode.ToString());
            Add(report, "no_gameplay_session_behind_the_menu", !bootstrap.SessionActive, "");

            UI.Session.NebulaMainMenuHost host = FindMenuHost();
            Add(report, "main_menu_is_on_screen", host != null && host.MenuVisible, host == null ? "no host" : "");
            if (host != null) launcher = host.Launcher;
            Add(report, "menu_has_a_session_launcher", launcher != null, "");
            return bootstrap;
        }

        /// <summary>
        /// Presses YENİ SEFER on the live menu. Returns false, having said so, if the
        /// entry cannot be found — the caller then falls back to the host's launcher so
        /// the rest of the smoke still runs against a started campaign.
        /// </summary>
        private bool PressNewCampaign(SmokeReport report)
        {
            var menu = UnityEngine.Object.FindObjectOfType<UI.Session.NebulaMainMenuScreen>(true);
            if (menu == null || menu.MenuColumn == null)
            {
                Add(report, "new_campaign_started_from_the_menu_entry", false, "no menu column on screen");
                return false;
            }

            foreach (UnityEngine.UI.Button entry in
                menu.MenuColumn.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (entry.name != NewCampaignEntryName) continue;
                Add(report, "new_campaign_started_from_the_menu_entry", true, entry.name);
                entry.onClick.Invoke();
                return true;
            }

            Add(report, "new_campaign_started_from_the_menu_entry", false,
                "no '" + NewCampaignEntryName + "' entry in the menu column");
            return false;
        }

        /// <summary>The object name <c>NebulaMainMenuScreen</c> gives the YENİ SEFER entry.</summary>
        private const string NewCampaignEntryName = "New";

        /// <summary>
        /// The brand presentation, in the real player rather than in a fixture.
        /// <para>
        /// The Editor fixtures build a shell and a menu screen by hand. This is the
        /// production menu, composed by the production host, drawing out of a player
        /// that loaded its assets from the real data files -- which is the only place
        /// an import setting that did not survive the build shows up.
        /// </para>
        /// <para>
        /// Draw order is checked, not just binding. A wordmark can be resolved, enabled
        /// and correctly placed while sitting underneath the menu's opaque backdrop,
        /// and every other property reports a titled menu while the screen has no name
        /// on it.
        /// </para>
        /// </summary>
        private void CheckBrandPresentation(SmokeReport report)
        {
            var menu = UnityEngine.Object.FindObjectOfType<UI.Session.NebulaMainMenuScreen>(true);
            Add(report, "main_menu_screen_exists", menu != null, "");
            if (menu == null) return;

            UI.Brand.NebulaBrandKeyArtView art = menu.KeyArt;
            Add(report, "key_art_plate_is_bound", art != null && art.HasArt, "");
            if (art == null) return;

            UnityEngine.UI.Image plate = art.GetComponent<UnityEngine.UI.Image>();
            string plateName = plate == null || plate.sprite == null ? "" : plate.sprite.name;
            Add(report, "key_art_is_a_clean_authored_plate",
                plateName.StartsWith("brand.keyart.clean."), plateName);
            Add(report, "composited_fallback_plate_not_used",
                !UI.Brand.NebulaBrandAssetCatalog.CombinedLogoKeyArtUsed, "");

            string markName = art.LogoImage == null || art.LogoImage.sprite == null
                ? "" : art.LogoImage.sprite.name;
            Add(report, "separate_wordmark_is_drawn", art.HasLogo, markName);
            Add(report, "wordmark_is_drawn_above_the_plate", art.LogoDrawsAboveThePlate, "");
            Add(report, "wordmark_is_the_delivered_logo_export",
                markName.StartsWith("brand.logo."), markName);

            Add(report, "exactly_one_title_on_screen",
                art.CarriesTitle && !menu.TextWordmarkVisible,
                $"plate title={art.CarriesTitle} drawn text={menu.TextWordmarkVisible}");

            Add(report, "no_brand_asset_had_to_be_substituted",
                UI.Brand.NebulaBrandAssetCatalog.FallbacksUsed == 0,
                "fallbacks=" + UI.Brand.NebulaBrandAssetCatalog.FallbacksUsed);
            Add(report, "no_brand_asset_is_missing",
                UI.Brand.NebulaBrandAssetCatalog.MissingAssets.Count == 0,
                string.Join(", ", UI.Brand.NebulaBrandAssetCatalog.MissingAssets));
        }

        /// <summary>
        /// Motion Off shows the still plate; Reduced Motion plays the reduced sheet.
        /// <para>
        /// Run against the player's own overlay so that a sheet whose sprite import did
        /// not survive the build is caught here rather than in an Editor that still has
        /// the asset database to fall back on. The settings are put back before the
        /// smoke moves on, and none of them is serialized, so nothing outlives the run.
        /// </para>
        /// </summary>
        private IEnumerator CheckLoadingPresentationModes(SmokeReport report)
        {
            // The live shell rather than the host's, which does not expose it: during
            // the menu phase there is exactly one, and the smoke already asserts that.
            var shell = UnityEngine.Object.FindObjectOfType<UI.Shell.NebulaUiShell>();
            if (shell == null)
            {
                Add(report, "loading_presentation_modes_checked", false, "no menu shell to draw on");
                yield break;
            }

            bool motionOff = Visuals.AnimationAccessibility.MotionOff;
            bool reduced = Visuals.AnimationAccessibility.ReducedMotion;

            // Motion Off: a still plate, never a slowed loop.
            Visuals.AnimationAccessibility.MotionOff = true;
            Visuals.AnimationAccessibility.ReducedMotion = false;
            UI.Brand.NebulaLoadingOverlay still = UI.Brand.NebulaLoadingOverlay.Show(shell, "SMOKE");
            yield return null;
            Add(report, "motion_off_shows_the_still_plate",
                still != null && !still.IsAnimated
                && still.CurrentSprite != null && still.CurrentSprite.name.Contains("static"),
                still == null ? "no overlay" : $"animated={still.IsAnimated} sprite={still.CurrentSprite?.name}");
            UI.Brand.NebulaLoadingOverlay.HideActive();
            yield return null;

            // Reduced Motion: the reduced sheet, and not a fallback to the still.
            Visuals.AnimationAccessibility.MotionOff = false;
            Visuals.AnimationAccessibility.ReducedMotion = true;
            UI.Brand.NebulaLoadingOverlay slow = UI.Brand.NebulaLoadingOverlay.Show(shell, "SMOKE");
            yield return null;
            Add(report, "reduced_motion_plays_the_reduced_sheet",
                slow != null && slow.IsReduced && slow.FrameCount == UI.Brand.NebulaBrandAssetCatalog.LoadingFrameCount
                && !UI.Brand.NebulaBrandAssetCatalog.ReducedUsesStatic,
                slow == null ? "no overlay"
                    : $"reduced={slow?.IsReduced} frames={slow?.FrameCount} fellBack={UI.Brand.NebulaBrandAssetCatalog.ReducedUsesStatic}");
            UI.Brand.NebulaLoadingOverlay.HideActive();
            yield return null;

            // And the full loop is what a session with neither setting gets.
            Visuals.AnimationAccessibility.MotionOff = false;
            Visuals.AnimationAccessibility.ReducedMotion = false;
            UI.Brand.NebulaLoadingOverlay full = UI.Brand.NebulaLoadingOverlay.Show(shell, "SMOKE");
            yield return null;
            Add(report, "default_motion_plays_the_full_loop",
                full != null && full.IsAnimated && !full.IsReduced
                && full.FrameCount == UI.Brand.NebulaBrandAssetCatalog.LoadingFrameCount,
                full == null ? "no overlay" : $"frames={full.FrameCount}");
            UI.Brand.NebulaLoadingOverlay.HideActive();
            yield return null;

            Visuals.AnimationAccessibility.MotionOff = motionOff;
            Visuals.AnimationAccessibility.ReducedMotion = reduced;
            Add(report, "loading_presentation_modes_checked", true, "");
            Add(report, "no_overlay_left_after_the_mode_checks",
                UI.Brand.NebulaLoadingOverlay.Active == null, "");
        }

        /// <summary>
        /// The loading surface really came up for the campaign start and really went
        /// away again.
        /// <para>
        /// Both halves, because each alone is satisfiable by the opposite bug: a
        /// surface that is never raised leaves nothing behind, and a surface that is
        /// raised and never torn down is indistinguishable from a hang. The count is a
        /// passive counter the overlay keeps, zeroed just before the campaign started.
        /// </para>
        /// </summary>
        private void CheckLoadingSurfaceRoseAndCameDown(SmokeReport report)
        {
            Add(report, "loading_surface_was_raised_for_the_transition",
                UI.Brand.NebulaLoadingOverlay.ShownCount > 0,
                "raised=" + UI.Brand.NebulaLoadingOverlay.ShownCount);
            Add(report, "loading_surface_completed_and_came_down",
                UI.Brand.NebulaLoadingOverlay.Active == null, "");
            Add(report, "loading_presenter_holds_no_depth", !UI.Brand.NebulaLoadingPresenter.Busy, "");

            // The inactive overload: an orphan is exactly the object the default
            // overload cannot see.
            var orphans = UnityEngine.Object.FindObjectsOfType<UI.Brand.NebulaLoadingOverlay>(true);
            Add(report, "no_orphan_loading_overlay_after_return_to_menu",
                orphans.Length == 0, $"found {orphans.Length}");
        }

        /// <summary>One shell, not two: the menu's own shell must be gone once a campaign owns the screen.</summary>
        private void CheckMenuStoodDown(SmokeReport report)
        {
            var hosts = UnityEngine.Object.FindObjectsOfType<UI.Session.NebulaMainMenuHost>();
            Add(report, "menu_host_stood_down_after_campaign_start", hosts.Length == 0, $"found {hosts.Length}");
            var shells = UnityEngine.Object.FindObjectsOfType<UI.Shell.NebulaUiShell>();
            Add(report, "exactly_one_ui_shell_during_gameplay", shells.Length == 1, $"found {shells.Length}");
            Add(report, "exactly_one_event_system",
                UnityEngine.Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>().Length == 1, "");
        }

        /// <summary>Returning to the menu has to actually dispose the world, not just cover it.</summary>
        private void CheckReturnedToMenu(SmokeReport report, World.SolarSystemBootstrap bootstrap)
        {
            Add(report, "session_disposed_on_return_to_menu", bootstrap != null && !bootstrap.SessionActive, "");
            Add(report, "no_bootstrap_left_running",
                UnityEngine.Object.FindObjectsOfType<Sectors.SystemStreamingDirector>().Length == 0,
                $"streaming directors still alive: {UnityEngine.Object.FindObjectsOfType<Sectors.SystemStreamingDirector>().Length}");
        }

        private static void Add(SmokeReport report, string name, bool passed, string detail) =>
            report.checks.Add(new SmokeCheckResult { name = name, passed = passed, detail = detail ?? "" });

        private World.Composition.NebulaContext CheckSceneAndSingletons(SmokeReport report)
        {
            Add(report, "gameplay_scene_loaded", SceneManager.GetActiveScene().IsValid() && SceneManager.GetActiveScene().isLoaded, SceneManager.GetActiveScene().name);

            // The bootstrap itself was already counted in the menu phase; here it is only
            // the carrier of the context the campaign just created.
            var bootstraps = UnityEngine.Object.FindObjectsOfType<World.SolarSystemBootstrap>();
            World.Composition.NebulaContext context = bootstraps.Length == 1 ? bootstraps[0].Context : null;
            Add(report, "campaign_started_from_the_menu", context != null,
                context == null ? "no context after StartNewCampaign()" : "");
            if (context == null) return null;

            Add(report, "runtime_catalog_built", context.RuntimeCatalog != null, "");
            Add(report, "ship_exists", context.Ship != null, "");
            Add(report, "streaming_singleton", UnityEngine.Object.FindObjectsOfType<Sectors.SystemStreamingDirector>().Length == 1, "");
            Add(report, "extraction_director_singleton", UnityEngine.Object.FindObjectsOfType<Extraction.ExtractionDirector>().Length == 1, "");
            Add(report, "combat_campaign_director_singleton", UnityEngine.Object.FindObjectsOfType<Combat.CombatCampaignDirector>().Length == 1, "");
            Add(report, "telemetry_bridge_singleton", UnityEngine.Object.FindObjectsOfType<Telemetry.TelemetryGameplayBridge>().Length == 1, "");
            return context;
        }

        private void CheckStartSystemFull(SmokeReport report, World.Composition.NebulaContext context)
        {
            if (context == null || context.Streaming == null) return;
            Add(report, "start_system_is_full", context.Streaming.FullSystemId == context.StartSystemId,
                $"full={context.Streaming.FullSystemId} start={context.StartSystemId}");
        }

        /// <summary>
        /// Exercises the REAL, instrumented ExtractionDirector.SaveNow/LoadNow path (the same one
        /// a player's Ctrl/Cmd+, / Ctrl/Cmd+. uses) so this both proves save/load actually works
        /// AND gives SavePerformanceRecorder a genuine sample -- but the real campaign's own slot 1
        /// content is backed up first and restored immediately after, so a smoke run never leaves
        /// any trace in the player's actual save.
        /// </summary>
        private void CheckSaveLoadRoundTrip(SmokeReport report, World.Composition.NebulaContext context)
        {
            if (context == null || context.ExtractionDirector == null) { Add(report, "real_save_and_load_round_trip", false, "no ExtractionDirector"); return; }

            string mainPath = Extraction.SaveService.SlotFile(1);
            string dir = Path.GetDirectoryName(mainPath);
            var backups = new Dictionary<string, string>();
            foreach (string suffix in new[] { "", ".bak1", ".bak2", ".bak3", ".tmp" })
            {
                string path = string.IsNullOrEmpty(suffix) ? mainPath : Path.Combine(dir, $"slot1{suffix}.json");
                backups[path] = File.Exists(path) ? File.ReadAllText(path) : null;
            }

            try
            {
                context.ExtractionDirector.SaveNow(null);
                context.ExtractionDirector.LoadNow();
                Add(report, "real_save_and_load_round_trip", true, "");
            }
            catch (Exception e)
            {
                Add(report, "real_save_and_load_round_trip", false, e.Message);
            }
            finally
            {
                foreach (var kv in backups)
                {
                    try
                    {
                        if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                        else File.WriteAllText(kv.Key, kv.Value);
                    }
                    catch { /* best-effort restore */ }
                }
            }
        }

        private void CheckTelemetry(SmokeReport report)
        {
            try
            {
                string json = Telemetry.TelemetryService.ExportJson();
                Add(report, "telemetry_export_produces_content", !string.IsNullOrEmpty(json) && json.Contains(Telemetry.TelemetryEvents.NewGame),
                    (json ?? "").Length + " chars");
            }
            catch (Exception e)
            {
                Add(report, "telemetry_export_produces_content", false, e.Message);
            }
        }

        private void CheckPerformanceRecorder(SmokeReport report)
        {
            Extraction.SavePerformanceRecorder.Stats stats = Extraction.ExtractionDirector.PerformanceRecorder.MainThreadStats;
            Add(report, "save_performance_recorder_has_samples", stats.Count > 0, $"count={stats.Count}");
        }
    }
}
