using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Progression;
using Nebula.Ship;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The one harness every <c>UiFinal*</c> PlayMode fixture boots through.
    /// <para>
    /// Three separate fixtures had each grown their own copy of "load the scene,
    /// count sixteen frames, hope". Counting frames is not waiting: batch mode runs
    /// the loop as fast as the machine allows, so sixteen frames can be sixteen
    /// milliseconds, and anything else that moves <c>Time.timeScale</c> — a
    /// tutorial step, another fixture that did not clean up — makes anything
    /// measured on scaled time advance at some other rate or not at all. Every wait
    /// here is therefore bounded twice — by wall-clock seconds and by a frame
    /// ceiling — and when it runs out it says what it was waiting for instead of
    /// hanging the run or failing later somewhere unrelated.
    /// </para>
    /// <para>
    /// The other half is isolation. These tests write real save files, flip real
    /// global accessibility switches, and leave a real universe running; a fixture
    /// that does not put those back changes what the <i>next</i> fixture measures,
    /// and the suite starts depending on its own running order.
    /// </para>
    /// </summary>
    internal static class UiFinalPlayMode
    {
        public const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>Wall clock ceiling for any single wait. Generous, because it is a failure bound and not a timing assertion.</summary>
        public const float WaitSeconds = 30f;

        /// <summary>Frame ceiling for the same wait, for the case where the editor is stalled and the clock is not.</summary>
        public const int WaitFrames = 1800;

        // =====================================================================
        // Bounded waiting
        // =====================================================================

        /// <summary>
        /// Waits for a condition, or fails saying what never happened. Never
        /// <c>WaitUntil</c>: that one waits forever, and a PlayMode test that hangs
        /// takes the whole run with it rather than reporting one red test.
        /// </summary>
        public static IEnumerator Until(Func<bool> ready, string what,
            float seconds = WaitSeconds, int frames = WaitFrames)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            int spent = 0;

            while (!ready())
            {
                if (Time.realtimeSinceStartup > deadline || spent >= frames)
                {
                    Assert.Fail($"timed out after {spent} frames / " +
                                $"{Time.realtimeSinceStartup - (deadline - seconds):0.##}s waiting for {what}");
                }

                spent++;
                yield return null;
            }
        }

        /// <summary>A fixed number of frames, for "let a deferred destroy flush" rather than for waiting on a condition.</summary>
        public static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        // =====================================================================
        // The scene and the session
        // =====================================================================

        public static SolarSystemBootstrap Boot() =>
            UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();

        public static NebulaMainMenuHost Host() =>
            UnityEngine.Object.FindObjectOfType<NebulaMainMenuHost>();

        public static NebulaContext Ctx()
        {
            SolarSystemBootstrap boot = Boot();
            return boot != null && boot.SessionActive ? boot.Context : null;
        }

        /// <summary>
        /// Loads the scene cold and waits until it is genuinely the new one.
        /// <para>
        /// The check is that the bootstrap found is a <b>different instance</b> in
        /// the <b>active</b> scene, not merely that one exists: a single-mode load
        /// completes at the end of the frame, so for one frame the previous scene's
        /// bootstrap is still what <c>FindObjectOfType</c> returns, and a test that
        /// starts talking to it is configuring a world that is about to be deleted.
        /// </para>
        /// </summary>
        public static IEnumerator LoadSceneCold()
        {
            SolarSystemBootstrap stale = Boot();

#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif

            yield return Until(() =>
            {
                SolarSystemBootstrap fresh = Boot();
                return fresh != null && fresh != stale &&
                       fresh.gameObject.scene == SceneManager.GetActiveScene();
            }, "the scene to finish loading and its own bootstrap to wake");

            if (Boot().BootMode == NebulaBootMode.MainMenu)
                yield return Until(() => Host() != null && Host().MenuVisible,
                    "the main menu the MainMenu boot mode promises to put on screen");
        }

        /// <summary>
        /// Loads the scene cold and starts a campaign through the same idempotent
        /// entry the New Campaign button calls. A test that wants the world asks
        /// for it; it never flips the production boot mode.
        /// </summary>
        public static IEnumerator LoadSceneWithSession()
        {
            yield return LoadSceneCold();
            yield return BeginSession();
        }

        public static IEnumerator BeginSession()
        {
            SolarSystemBootstrap boot = Boot();
            Assert.IsNotNull(boot, "there is no bootstrap in the scene to start a session on");
            boot.BeginSession();
            yield return SettleAfterSessionStart();
        }

        /// <summary>
        /// "The session is running" is one frame short of "the interface is in the
        /// state the player would see": the menu host stands itself down from its
        /// own <c>Update</c>, and <c>Destroy</c> is deferred, so a census taken too
        /// early counts a second shell that is already doomed.
        /// </summary>
        public static IEnumerator SettleAfterSessionStart()
        {
            yield return Until(() =>
            {
                SolarSystemBootstrap boot = Boot();
                return boot != null && boot.SessionActive &&
                       boot.Context != null &&
                       boot.Context.Presentation != null &&
                       boot.Context.Presentation.ParentMenu != null &&
                       boot.Context.Presentation.UiShell != null;
            }, "the campaign to finish composing its presentation");

            yield return Until(() => Host() == null,
                "the main menu host to stand down once the campaign is running");

            yield return Until(
                () => UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true).Length == 1,
                "the menu's own shell to finish being destroyed, leaving exactly one");
        }

        /// <summary>Ends the running campaign and waits for the menu the contract promises in its place.</summary>
        public static IEnumerator EndSessionAndSettle()
        {
            SolarSystemBootstrap boot = Boot();
            if (boot == null) yield break;

            boot.EndSession();
            yield return Until(() => Boot() == null || !Boot().SessionActive,
                "the campaign to close");

            if (Boot() == null || Boot().BootMode != NebulaBootMode.MainMenu) yield break;

            yield return Until(() => Host() != null && Host().MenuVisible,
                "the main menu to come back once the campaign is closed");

            // The disposed session's shell is destroyed with a deferred Destroy and
            // the replacement menu builds its own in the same frame, so for one
            // frame there are genuinely two. Anything counted before this settles is
            // counting the handover, not a leak.
            yield return SettleToOneShell();
        }

        /// <summary>Waits out the frame in which an outgoing shell and its replacement both exist.</summary>
        public static IEnumerator SettleToOneShell()
        {
            yield return Until(
                () => UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true).Length == 1,
                "the outgoing shell to finish being destroyed, leaving exactly one");
        }

        // =====================================================================
        // Global state — captured on the way in, put back on the way out
        // =====================================================================

        /// <summary>
        /// Every global a UI test can move. All of these are process-wide statics or
        /// engine settings: whatever a test leaves here, the next test starts with.
        /// </summary>
        public struct Globals
        {
            public float TimeScale, UiScale, HudScale;
            public int QualityLevel, VSyncCount;
            public bool HighContrast, FlashReduction, PhotosensitivitySafe, WarningCue;
            public bool ReducedMotion, MotionOff, CameraComfort;
            public bool InputSuppressed;
            public List<(CombatAction action, KeyCode key)> Bindings;
        }

        public static Globals CaptureGlobals() => new Globals
        {
            TimeScale = Time.timeScale,
            UiScale = NebulaUiShell.UiScale,
            HudScale = CombatAccessibilitySettings.HudScale,
            QualityLevel = QualitySettings.GetQualityLevel(),
            VSyncCount = QualitySettings.vSyncCount,
            HighContrast = LightingAccessibility.HighContrast,
            FlashReduction = LightingAccessibility.FlashReduction,
            PhotosensitivitySafe = LightingAccessibility.PhotosensitivitySafeMode,
            WarningCue = CombatAccessibilitySettings.AudioVisualCueForWarnings,
            ReducedMotion = AnimationAccessibility.ReducedMotion,
            MotionOff = AnimationAccessibility.MotionOff,
            CameraComfort = AnimationAccessibility.CameraComfort,
            InputSuppressed = InputGate.Suppressed,
            Bindings = new List<(CombatAction, KeyCode)>(CombatBindings.CurrentBindings()),
        };

        /// <summary>
        /// Puts every global back. Runs from <c>TearDown</c> rather than from the
        /// end of the test body on purpose: a test that fails halfway is exactly the
        /// test that leaves the quality level, the interface scale and the clock
        /// somewhere the next fixture did not ask for.
        /// </summary>
        public static void RestoreGlobals(Globals saved)
        {
            Time.timeScale = saved.TimeScale <= 0f ? 1f : saved.TimeScale;
            NebulaUiShell.UiScale = saved.UiScale;
            CombatAccessibilitySettings.HudScale = saved.HudScale;
            if (QualitySettings.GetQualityLevel() != saved.QualityLevel)
                QualitySettings.SetQualityLevel(saved.QualityLevel, true);
            QualitySettings.vSyncCount = saved.VSyncCount;
            LightingAccessibility.HighContrast = saved.HighContrast;
            LightingAccessibility.FlashReduction = saved.FlashReduction;
            LightingAccessibility.PhotosensitivitySafeMode = saved.PhotosensitivitySafe;
            CombatAccessibilitySettings.AudioVisualCueForWarnings = saved.WarningCue;
            AnimationAccessibility.ReducedMotion = saved.ReducedMotion;
            AnimationAccessibility.MotionOff = saved.MotionOff;
            AnimationAccessibility.CameraComfort = saved.CameraComfort;
            InputGate.Suppressed = saved.InputSuppressed;

            if (saved.Bindings != null)
                foreach ((CombatAction action, KeyCode key) in saved.Bindings)
                    CombatBindings.TrySetBinding(action, key);
        }

        /// <summary>
        /// The static state a disposed session has no other owner to clean up.
        /// <para>
        /// Called after the session is gone, never while one is live: nulling
        /// <c>ProgressionService.Awarded</c> under a running HUD would silence a
        /// widget that is still on screen. Any residue found here is reported rather
        /// than swallowed, because a subscriber that outlives its session is the
        /// exact leak the lifecycle census exists to catch — clearing it silently
        /// would hide the finding from the next test while pretending to isolate it.
        /// </para>
        /// </summary>
        public static void ClearSessionStatics()
        {
            int awarded = StaticSubscribers(typeof(ProgressionService), "Awarded");
            int collected = StaticSubscribers(typeof(ResourceTransferService), "Collected");
            if (awarded > 0 || collected > 0)
            {
                Debug.Log($"[uifinal] static event residue after teardown: " +
                          $"ProgressionService.Awarded={awarded} ResourceTransferService.Collected={collected}");
            }

            ProgressionService.Reset();
            ResourceTransferService.ResetForSession();
            CampaignIdentity.ResetSubscribersForSession();
            DominanceSystem.ResetForSession();
            ExclusiveModal.ResetForSession();
            InputGate.Suppressed = false;
        }

        /// <summary>
        /// How many listeners a static event is carrying. A restart that leaves a
        /// dead widget subscribed keeps that widget alive and reports every award
        /// twice, and nothing about the object graph shows it.
        /// </summary>
        public static int StaticSubscribers(Type owner, string eventField)
        {
            FieldInfo field = owner.GetField(eventField,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(field, $"{owner.Name}.{eventField} no longer exists; this counter is blind");
            return field.GetValue(null) is Delegate handler ? handler.GetInvocationList().Length : 0;
        }

        // =====================================================================
        // Save slots — real files on the developer's disk
        // =====================================================================

        /// <summary>Every file any slot can own, including the rolling backups and the write-temp.</summary>
        public static IEnumerable<string> AllSlotFiles()
        {
            for (int slot = SaveService.FirstSlot; slot < SaveService.FirstSlot + SaveService.SlotCount; slot++)
            {
                string main = SaveService.SlotFile(slot);
                string dir = Path.GetDirectoryName(main);
                yield return main;
                yield return Path.Combine(dir, $"slot{slot}.bak1.json");
                yield return Path.Combine(dir, $"slot{slot}.bak2.json");
                yield return Path.Combine(dir, $"slot{slot}.bak3.json");
                yield return Path.Combine(dir, $"slot{slot}.tmp.json");
            }
        }

        public static Dictionary<string, string> BackupSlots()
        {
            var backup = new Dictionary<string, string>();
            foreach (string f in AllSlotFiles()) backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
            return backup;
        }

        public static void RestoreSlots(Dictionary<string, string> backup)
        {
            if (backup == null) return;
            foreach (KeyValuePair<string, string> kv in backup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        public static void DeleteEverySlot()
        {
            foreach (string f in AllSlotFiles()) if (File.Exists(f)) File.Delete(f);
        }

        // =====================================================================
        // Finding the controls the player actually clicks
        // =====================================================================

        public static UnityEngine.UI.Button Find(Transform root, string name)
        {
            if (root == null) return null;
            foreach (UnityEngine.UI.Button button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if (button.gameObject.name == name) return button;
            return null;
        }

        public static UnityEngine.UI.Button Require(Transform root, string name, string why)
        {
            UnityEngine.UI.Button button = Find(root, name);
            Assert.IsNotNull(button, $"no control named \"{name}\" — {why}");
            return button;
        }

        /// <summary>A control inside a named row; settings rows all name their controls the same.</summary>
        public static UnityEngine.UI.Button RowControl(Transform root, string rowLabel, string controlName)
        {
            if (root == null) return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.name != rowLabel) continue;
                UnityEngine.UI.Button found = Find(child, controlName);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>The one shell the session or the menu host owns, inactive included.</summary>
        public static NebulaUiShell Shell()
        {
            NebulaUiShell[] all = UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true);
            return all.Length == 0 ? null : all[0];
        }

        /// <summary>The menu layer of whichever shell is currently on screen.</summary>
        public static Transform MenuRoot()
        {
            NebulaUiShell shell = Shell();
            return shell == null ? null : shell.Root(UiLayer.Menu);
        }

        /// <summary>
        /// Test-side quit seam. <c>NebulaSessionLauncher.QuitGame</c> sets
        /// <c>EditorApplication.isPlaying = false</c>, which from inside a PlayMode
        /// test does not "assert the quit" — it ends play mode and takes every test
        /// after it down with it. So the wiring is proven against a recorder placed
        /// in the same seam the production menu is constructed with, and the real
        /// exit is left to the Player smoke run.
        /// </summary>
        public sealed class RecordingLauncher : INebulaSessionLauncher
        {
            public int NewCampaigns, Continues, Returns, Quits, Restarts;
            public int LastContinuedSlot = -1;

            public void StartNewCampaign() => NewCampaigns++;
            public void ContinueFromSlot(int slot) { Continues++; LastContinuedSlot = slot; }
            public void QuitGame() => Quits++;
            public void ReturnToMainMenu() => Returns++;

            /// <summary>
            /// Counted separately from <see cref="StartNewCampaign"/> on purpose: the
            /// in-session restart used to route there, and because BeginSession is
            /// idempotent it did nothing while reporting success. Two counters is how
            /// that regression stays visible.
            /// </summary>
            public void RestartCampaign() => Restarts++;
        }
    }

    /// <summary>
    /// The production boot flow: the game opens on the main menu, and no world
    /// exists until the player asks for one.
    /// <para>
    /// This was the last thing standing between "the main menu is built" and "the
    /// main menu is the game's front door". The flag that chose between them
    /// defaulted to gameplay because dozens of tests loaded the scene and expected
    /// a world one frame later — which is a reason to change the tests, not a
    /// reason to ship the wrong front door.
    /// </para>
    /// </summary>
    public class UiFinalBootFlowPlayModeTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slots;

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slots = UiFinalPlayMode.BackupSlots();
        }

        /// <summary>
        /// Every test here leaves a live universe behind. Closing it explicitly is
        /// what makes the fixture order-independent: the statics a session owns are
        /// only safe to clear once the session that owns them is gone.
        /// </summary>
        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slots);
        }

        [UnityTest]
        public IEnumerator TheSceneBootsToTheMenu_WithNoWorldBehindIt()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            SolarSystemBootstrap bootstrap = UiFinalPlayMode.Boot();
            Assert.AreEqual(NebulaBootMode.MainMenu, bootstrap.BootMode,
                "production opens on the menu; a test that wants the world asks for it");
            Assert.IsFalse(bootstrap.SessionActive, "no campaign has been started yet");

            // The point of a main menu is that nothing is running behind it. A ship
            // spawned before "New Campaign" is pressed means the campaign the player
            // then starts is the second one.
            Assert.IsNull(UnityEngine.Object.FindObjectOfType<ShipController>(),
                "no ship exists until a campaign does");
            Assert.IsNull(bootstrap.Context, "and no context either");

            Assert.IsNotNull(UiFinalPlayMode.Host(), "the menu is what is on screen instead");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "the menu host owns exactly one shell before any session exists");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<EventSystem>(true).Length,
                "and exactly one event system");
        }

        [UnityTest]
        public IEnumerator StartingACampaign_BuildsExactlyOneOfEverything()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            NebulaMainMenuHost host = UiFinalPlayMode.Host();
            Assert.IsNotNull(host);
            Assert.IsTrue(host.MenuVisible);

            // Through the control the player clicks, not through a back door: the
            // button's own callback is the thing under test.
            UiFinalPlayMode.Require(UiFinalPlayMode.MenuRoot(), "New",
                "the main menu has no New Campaign control").onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.IsTrue(UiFinalPlayMode.Boot().SessionActive, "the campaign is running");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<ShipController>().Length,
                "one ship, not two");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "one shell -- the menu's own must have stood down");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<EventSystem>(true).Length,
                "one event system -- two would mean two things deciding what the mouse hit");
        }

        /// <summary>
        /// Two shells would mean two owners of the modal gate and two pause blocks,
        /// which is how a game ends up stuck at timeScale zero with no way back.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMenuStandsDownOnceTheCampaignIsRunning()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            Assert.IsNull(UiFinalPlayMode.Host(),
                "the host removes itself rather than coexisting with the session");
            Assert.AreNotEqual(0f, Time.timeScale, "and leaves the game running");
            Assert.IsNull(ExclusiveModal.Owner, "and the modal gate free");
            Assert.IsFalse(InputGate.Suppressed, "and input un-gated");
        }

        [UnityTest]
        public IEnumerator RestartingACampaign_LeavesOneSessionNotTwo()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            ShipController first = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(first);

            UiFinalPlayMode.Boot().RestartCampaign();
            yield return UiFinalPlayMode.Until(
                () => UnityEngine.Object.FindObjectOfType<ShipController>() != null &&
                      UnityEngine.Object.FindObjectOfType<ShipController>() != first,
                "the restart to hand back a genuinely new ship");
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<ShipController>().Length,
                "a restart replaces the campaign; it does not add one");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true).Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<EventSystem>(true).Length);
        }

        [UnityTest]
        public IEnumerator EndingTheSession_ReturnsToAMenuThatWorks()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            Assert.IsTrue(UiFinalPlayMode.Boot().SessionActive);

            yield return UiFinalPlayMode.EndSessionAndSettle();

            Assert.IsFalse(UiFinalPlayMode.Boot().SessionActive, "the campaign is closed");
            Assert.IsNotNull(UiFinalPlayMode.Host(),
                "and the menu the contract promises in a closed session's place is on screen");

            // This used to demand `ExclusiveModal.Owner == null` the instant the campaign
            // closed. That contract cannot hold, and never did:
            // SolarSystemBootstrap.EndSession disposes the session and then calls
            // NebulaMainMenuHost.Create, whose Build() ends in shell.Push(menu), and
            // NebulaUiShell.Push takes ExclusiveModal.TryAcquire(this) plus HoldInput()
            // on the first push. The front door IS a screen, so it holds the modal gate
            // and the input gate exactly as it does at a cold boot -- production
            // authority is NebulaUiShell.Push/HoldInput, and this fixture is not the
            // place that gets to redefine it. (The clock was in that list too until
            // screens stopped stopping the world; it is no longer held by anybody.)
            //
            // The invariant that actually matters is unchanged and is asserted instead:
            // the gate must be held by the shell that is ON SCREEN, never by the destroyed
            // campaign's shell. A gate owned by a destroyed object blocks every future
            // modal for the rest of the process, and no census of object counts can tell
            // the two apart. Same assertion the sibling walk in UiFinalEndToEndFlowTests
            // makes at this exact point of the flow.
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "leaving the campaign left the session's shell alongside the menu's");
            Assert.AreSame(UiFinalPlayMode.Shell(), ExclusiveModal.Owner,
                "the modal gate is held by something other than the shell that is on screen");

            // Leaving a campaign has to be repeatable. The first return was the one
            // the host was built for; the second is where a host that forgot to
            // stand down, or an EndSession that builds a host per call, shows up.
            yield return UiFinalPlayMode.BeginSession();

            // And the clock is asserted where it is felt: handing the screen over to a
            // campaign is where the front door has to give the clock and the gate back. A
            // campaign that resumes frozen is the real failure this ever protected against.
            Assert.AreNotEqual(0f, Time.timeScale, "the campaign started with the clock still held");
            Assert.IsNull(ExclusiveModal.Owner, "the campaign started with the modal gate still held");

            yield return UiFinalPlayMode.EndSessionAndSettle();

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaMainMenuHost>(true).Length,
                "leaving a campaign twice left two main menus stacked on each other");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "and two shells with it");
        }
    }
}
