using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Controls;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The link nobody could prove: a physical key press reaching the rebind
    /// capture.
    /// <para>
    /// <c>UiFinalControlBindingTests</c> drives every rule around the capture and
    /// says so plainly in its own summary — the one thing not simulated is the key
    /// press itself, because legacy <c>UnityEngine.Input</c> cannot be driven from
    /// batch mode. So the capture was always completed by the test calling
    /// <c>Apply</c>, which is the method the listener calls, and the listener's own
    /// <c>Update</c> — the only thing that ever turns a key press into that call —
    /// never ran in any test. A listener that had stopped sampling, sampled the wrong
    /// thing, or was never attached at all would have gone unnoticed.
    /// </para>
    /// <para>
    /// <b>What is substituted, and what is not.</b> <see cref="IKeyInputSource"/> has
    /// one method and it samples hardware. It knows nothing about actions, bindings,
    /// conflicts or refusals, so the fake below cannot decide any outcome — it can
    /// only decide which key was pressed. Everything the press then goes through is
    /// the shipping path: the listener's <c>Update</c>, its
    /// candidate scan, <c>NebulaControlBindingSection.Apply</c>, the validator, and
    /// <c>CombatBindings</c>. The fake never touches a binding, and every test here
    /// asserts it was really sampled, so a production path that quietly went back to
    /// reading <c>Input</c> directly would fail rather than pass silently.
    /// </para>
    /// </summary>
    public class UiFinalPhysicalKeyCaptureTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;
        private FakeKeyboard keyboard;

        /// <summary>
        /// One physical keyboard, and nothing more. It answers "did this key go down
        /// this frame" and it is the only thing about the capture that is not the
        /// production implementation.
        /// <para>
        /// A press is one-shot on purpose: <c>Input.GetKeyDown</c> is true on exactly
        /// one frame, and a fake that stayed true would rebind on every frame after
        /// the first and hide the real single-fire contract.
        /// </para>
        /// </summary>
        private sealed class FakeKeyboard : IKeyInputSource
        {
            private KeyCode pending = KeyCode.None;

            /// <summary>How many times the production listener asked this for a sample.</summary>
            public int Samples { get; private set; }

            /// <summary>How many presses were actually read out.</summary>
            public int Consumed { get; private set; }

            public void Press(KeyCode key) => pending = key;

            public bool GetKeyDown(KeyCode key)
            {
                Samples++;
                if (pending == KeyCode.None || key != pending) return false;
                pending = KeyCode.None;
                Consumed++;
                return true;
            }
        }

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
            UiFinalPlayMode.DeleteEverySlot();

            keyboard = new FakeKeyboard();
            NebulaKeyCaptureListener.Source = keyboard;
        }

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            // The real keyboard goes back first. A leaked fake source would leave
            // every later fixture's rebind screen reading a dead device, and nothing
            // about that failure would point here.
            NebulaKeyCaptureListener.ResetSourceToKeyboard();

            foreach (NebulaUiShell shell in Object.FindObjectsOfType<NebulaUiShell>(true))
                if (shell != null) shell.Shutdown();

            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            CombatBindings.ResetToDefaults();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        // =====================================================================
        // Getting to the surface, the way a player does
        // =====================================================================

        private static Transform MenuLayer() => UiFinalPlayMode.Shell().Root(UiLayer.Menu);

        private static Button Live(Transform root, string name, string why)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name && button.gameObject.activeInHierarchy) return button;
            Assert.Fail($"no control named \"{name}\" is on screen — {why}");
            return null;
        }

        private static IEnumerator OpenTheBindingSurface(System.Action<NebulaSettingsPage> then)
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            Live(MenuLayer(), "Settings", "the front door has no AYARLAR control").onClick.Invoke();
            yield return null;

            NebulaMenuPageScreen screen = UiFinalPlayMode.Host().SettingsScreen;
            Assert.IsNotNull(screen, "AYARLAR opened nothing");
            var page = screen.Page as NebulaSettingsPage;
            Assert.IsNotNull(page, "the settings screen is not hosting the settings page");

            Live(MenuLayer(), NebulaSettingsPage.OpenBindingsControlName,
                "the controls section has no way to reach the rebinding surface").onClick.Invoke();
            yield return null;

            Assert.IsTrue(page.BindingPanelOpen, "the rebinding control opened nothing");
            Assert.IsNotNull(page.Bindings.Listener,
                "the rebinding surface has no key-capture listener, so no key press could ever reach it");
            then(page);
        }

        private static Button RowControl(CombatAction action, string control) =>
            UiFinalPlayMode.RowControl(MenuLayer(),
                NebulaControlBindingSection.RowName(action), control);

        /// <summary>Arms the capture through the row's own control and checks the listener agrees.</summary>
        private static void Arm(NebulaSettingsPage page, CombatAction action)
        {
            RowControl(action, NebulaControlBindingSection.RebindControlName).onClick.Invoke();
            Assert.AreEqual(action, page.Bindings.Capturing, "the ATA control did not arm the capture");
            Assert.IsTrue(page.Bindings.Listener.Armed,
                "the section thinks it is capturing and the listener that reads the keyboard does not");
        }

        /// <summary>
        /// Presses one key and waits for the production listener to read it. Bounded,
        /// and it fails saying the sample never arrived rather than hanging.
        /// </summary>
        private IEnumerator PressAndLetTheListenerRead(KeyCode key)
        {
            int before = keyboard.Consumed;
            keyboard.Press(key);
            yield return UiFinalPlayMode.Until(() => keyboard.Consumed > before,
                $"the rebind listener's own Update to sample {key} from the keyboard");
        }

        // =====================================================================
        // The chain itself
        // =====================================================================

        /// <summary>
        /// Click rebind → the UI enters listening → one physical key-down → the same
        /// validation and Apply path runs → the row shows the new binding, and the
        /// game reads it.
        /// </summary>
        [UnityTest]
        public IEnumerator APhysicalKeyPress_ReachesTheCapture_AndIsStoredWhereTheGameReadsIt()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            KeyCode before = CombatBindings.RepairKey;
            Assert.AreNotEqual(KeyCode.Backslash, before, "this test needs a key nothing holds");

            // Nothing is sampled until the capture is armed: an always-on scan of the
            // whole enum is a cost a settings screen must not pay.
            int idleSamples = keyboard.Samples;
            yield return UiFinalPlayMode.Frames(3);
            Assert.AreEqual(idleSamples, keyboard.Samples,
                "the listener samples the keyboard while nothing is armed");

            Arm(page, CombatAction.Repair);
            Assert.AreEqual("…", page.Bindings.LabelFor(CombatAction.Repair),
                "an armed row still shows the old key as if nothing were happening");

            yield return PressAndLetTheListenerRead(KeyCode.Backslash);

            Assert.Greater(keyboard.Samples, idleSamples,
                "the armed listener never asked the keyboard for anything, so this proves nothing");
            Assert.IsNull(page.Bindings.Capturing, "the capture stayed armed after a key was taken");
            Assert.IsFalse(page.Bindings.Listener.Armed, "the listener kept reading after a key was taken");

            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey,
                "a physical key press never reached the binding service");

            bool readBack = false;
            foreach ((CombatAction action, KeyCode key) in CombatBindings.CurrentBindings())
                if (action == CombatAction.Repair && key == KeyCode.Backslash) readBack = true;
            Assert.IsTrue(readBack,
                "ShipCombat reads its bindings from CurrentBindings and that still quotes the old key");

            Assert.AreEqual(NebulaControlBindings.KeyLabel(KeyCode.Backslash),
                page.Bindings.LabelFor(CombatAction.Repair),
                "the row on screen and the binding the game reads disagree");
            StringAssert.Contains(NebulaControlBindings.KeyLabel(KeyCode.Backslash),
                page.Bindings.StatusText ?? string.Empty,
                "the accepted key was never confirmed to the player");
        }

        /// <summary>
        /// A key another combat action holds, pressed physically. The refusal comes
        /// from the validator, not from the listener, and the capture deliberately
        /// stays armed so the player is not dropped out of a mode they chose.
        /// </summary>
        [UnityTest]
        public IEnumerator APhysicalConflictingKey_IsRefused_AndLeavesTheCaptureArmed()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            KeyCode taken = CombatBindings.CombatBoostKey;
            KeyCode before = CombatBindings.RepairKey;

            Arm(page, CombatAction.Repair);
            yield return PressAndLetTheListenerRead(taken);

            Assert.AreEqual(before, CombatBindings.RepairKey, "a refused key still moved the binding");
            Assert.AreEqual(taken, CombatBindings.CombatBoostKey,
                "a refused key was stolen from the action that held it");
            Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing,
                "a refusal dropped the player out of the mode they deliberately entered");
            Assert.IsTrue(page.Bindings.Listener.Armed,
                "a refusal disarmed the listener while the screen still says it is listening");
            StringAssert.Contains("ZATEN", page.Bindings.StatusText ?? string.Empty,
                "the refusal did not say the key was already in use");

            // Still armed means still usable: a free key pressed straight after is
            // taken without re-arming.
            yield return PressAndLetTheListenerRead(KeyCode.Backslash);
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey,
                "the still-armed capture did not accept the next key");
        }

        [UnityTest]
        public IEnumerator APhysicalKeyOwnedByAnotherSystem_IsRefusedAndChangesNothing()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            // Three different owners: flight, the extraction contextual key, and the
            // manual save shortcut.
            var reserved = new[] { KeyCode.W, ShipExtraction.ScanKey, NebulaInputBindings.ManualSaveKey };

            Arm(page, CombatAction.Repair);
            foreach (KeyCode key in reserved)
            {
                KeyCode before = CombatBindings.RepairKey;
                yield return PressAndLetTheListenerRead(key);

                Assert.AreEqual(before, CombatBindings.RepairKey,
                    $"a physically pressed {key} is owned by another system and was taken anyway");
                Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing,
                    $"a refused {key} abandoned the capture");
                StringAssert.Contains("AİT", page.Bindings.StatusText ?? string.Empty,
                    $"{key} was refused without saying it belongs to another system");
            }

            page.Bindings.CancelCapture();
        }

        /// <summary>
        /// F1–F12 are the developer overlays' keys and no runtime source may even name
        /// one, so they are walked by the enum's own name rather than as a range.
        /// Physically pressing one must change nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryPhysicallyPressedFunctionKey_IsRefusedEntirely()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            Arm(page, CombatAction.Repair);
            for (int index = 1; index <= 12; index++)
            {
                var key = (KeyCode)System.Enum.Parse(typeof(KeyCode), "F" + index);
                KeyCode before = CombatBindings.RepairKey;

                yield return PressAndLetTheListenerRead(key);

                Assert.AreEqual(before, CombatBindings.RepairKey,
                    $"a physically pressed F{index} was accepted as a player binding");
                Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing,
                    $"a refused F{index} abandoned the capture");
            }

            StringAssert.Contains("F1", page.Bindings.StatusText ?? string.Empty,
                "the refusal does not tell the player function keys are off limits");

            // The plain F key is not a function key — this rule must not swallow it.
            // It is nonetheless unbindable, because the extraction and logistics
            // contextual key already owns it, and the two refusals are different
            // sentences for different reasons. Pressing it physically must produce the
            // second one and not the first.
            Assert.IsFalse(NebulaControlBindings.IsFunctionKey(KeyCode.F),
                "the plain F key was mistaken for a function key");

            KeyCode beforeF = CombatBindings.RepairKey;
            yield return PressAndLetTheListenerRead(KeyCode.F);
            Assert.AreEqual(beforeF, CombatBindings.RepairKey, "a physically pressed F moved the binding");
            StringAssert.Contains("AİT", page.Bindings.StatusText ?? string.Empty,
                "F was refused as a function key rather than as a key another system owns");

            // And a key nothing owns is still taken, so the twelve refusals above are
            // a rule and not a dead capture.
            yield return PressAndLetTheListenerRead(KeyCode.Backslash);
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey,
                "the capture stopped accepting anything after the function-key refusals");
        }

        /// <summary>
        /// Escape unwinds exactly one level and it is the <b>screen stack</b> that
        /// carries it, not the capture listener (P1-UI-006).
        /// <para>
        /// Both used to read it. The listener sampled Escape and cancelled, and the
        /// shell sampled the same press and popped, so one press abandoned the capture
        /// <i>and</i> shut the whole rebinding panel underneath it — the player lost
        /// two levels for one key. This drives the shell's real Back, then asserts the
        /// listener neither saw the press nor could have taken it as a binding.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Escape_CancelsOnlyTheCapture_AndLeavesTheRebindingPanelOpen()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            KeyCode before = CombatBindings.RepairKey;
            Arm(page, CombatAction.Repair);

            // Escape never reaches the listener: it is not in the candidate scan and
            // there is no branch for it any more, so a press of it is sampled by
            // nothing on this surface.
            int consumedByListener = keyboard.Consumed;
            keyboard.Press(KeyCode.Escape);
            yield return UiFinalPlayMode.Frames(3);
            Assert.AreEqual(consumedByListener, keyboard.Consumed,
                "the capture listener still consumes Escape — two owners for one press");
            Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing,
                "the listener cancelled the capture behind the screen stack's back");

            // The player's press, through the surface that owns it.
            Assert.IsTrue(UiFinalPlayMode.Shell().Back(), "the shell refused Back with a panel open");

            Assert.IsNull(page.Bindings.Capturing, "Back left the capture armed");
            Assert.IsFalse(page.Bindings.Listener.Armed, "Back left the listener reading");
            Assert.IsTrue(page.BindingPanelOpen,
                "one Back closed the capture AND the rebinding panel — two levels for one press");
            Assert.AreEqual(before, CombatBindings.RepairKey, "Back changed the binding");
            Assert.AreEqual(NebulaControlBindings.KeyLabel(before),
                page.Bindings.LabelFor(CombatAction.Repair),
                "Back left the row showing a key that is not the binding");

            // A key pressed after cancelling belongs to nobody: the listener is no
            // longer reading, so nothing is captured at all.
            int consumedBefore = keyboard.Consumed;
            keyboard.Press(KeyCode.Backslash);
            yield return UiFinalPlayMode.Frames(3);
            Assert.AreEqual(consumedBefore, keyboard.Consumed,
                "the listener was still sampling after Back cancelled the capture");
            Assert.AreEqual(before, CombatBindings.RepairKey, "a key after Back moved the binding");

            // The second Back is the one that closes the panel.
            Assert.IsTrue(UiFinalPlayMode.Shell().Back(), "the shell refused the second Back");
            Assert.IsFalse(page.BindingPanelOpen, "the second Back did not close the rebinding panel");
        }

        /// <summary>
        /// An armed capture with nobody pressing anything: it stays armed, it changes
        /// nothing, and it does not invent a key out of a quiet frame.
        /// <para>
        /// This is the whole of what "timeout" means on this surface, and it is
        /// stated rather than implied: the capture has <b>no</b> wall-clock expiry. It
        /// is bounded by the screen instead — the listener lives on the settings
        /// column, so hiding the page disables it and its <c>OnDisable</c> abandons
        /// the capture, which is asserted below and in
        /// <c>UiFinalControlBindingTests.ClosingTheScreenWhileArmed_AbandonsTheCapture</c>.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AnArmedCaptureWithNoKeyPressed_StaysArmedAndBindsNothing()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            KeyCode before = CombatBindings.RepairKey;
            Arm(page, CombatAction.Repair);

            int samplesBefore = keyboard.Samples;
            yield return UiFinalPlayMode.Frames(30);

            Assert.Greater(keyboard.Samples, samplesBefore,
                "the armed listener stopped sampling on its own");
            Assert.AreEqual(0, keyboard.Consumed, "a quiet keyboard produced a key press");
            Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing,
                "the capture expired on its own; nothing in this surface has a timeout");
            Assert.AreEqual(before, CombatBindings.RepairKey, "an untouched keyboard moved a binding");
            Assert.AreEqual("…", page.Bindings.LabelFor(CombatAction.Repair),
                "the armed row stopped showing that it is waiting");

            // Closing the screen is the real bound, and it abandons the capture.
            Live(MenuLayer(), "Back", "the settings screen has no way back").onClick.Invoke();
            yield return null;
            Assert.IsNull(page.Bindings.Capturing, "an armed capture survived the screen closing");
            Assert.AreEqual(before, CombatBindings.RepairKey, "closing the screen changed a binding");
        }

        /// <summary>
        /// Reset to defaults after two physical rebinds, through the section's own
        /// control.
        /// </summary>
        [UnityTest]
        public IEnumerator ResetToDefaults_UndoesEveryPhysicallyPressedRebind()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            Arm(page, CombatAction.Repair);
            yield return PressAndLetTheListenerRead(KeyCode.Backslash);
            Arm(page, CombatAction.CycleTarget);
            yield return PressAndLetTheListenerRead(KeyCode.Slash);

            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey);
            Assert.AreEqual(KeyCode.Slash, CombatBindings.CycleTargetKey);

            Live(MenuLayer(), NebulaControlBindingSection.ResetControlName,
                "there is no way back to the shipped keys").onClick.Invoke();

            Assert.AreEqual(KeyCode.Alpha6, CombatBindings.RepairKey, "Repair was not restored");
            Assert.AreEqual(KeyCode.Alpha4, CombatBindings.CycleTargetKey, "Cycle Target was not restored");
            Assert.IsNull(page.Bindings.Capturing, "reset left a capture armed");
            Assert.IsFalse(page.Bindings.Listener.Armed, "reset left the listener reading");
            Assert.AreEqual("6", page.Bindings.LabelFor(CombatAction.Repair),
                "the rows still show the keys the reset threw away");
        }

        /// <summary>
        /// A binding taken from a physical press is process-wide state no save owns,
        /// which is exactly why it needs proving: a save/load that quietly reset it
        /// would be silent, and the player would find out in combat.
        /// </summary>
        [UnityTest]
        public IEnumerator ABindingTakenFromAPhysicalPress_SurvivesSaveLoadAndRestart()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            Arm(page, CombatAction.Repair);
            yield return PressAndLetTheListenerRead(KeyCode.Backslash);
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey);

            Live(MenuLayer(), "Back", "the settings screen has no way back").onClick.Invoke();
            yield return null;

            Live(MenuLayer(), "New", "the front door has no YENİ SEFER control").onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey,
                "starting a campaign reset the player's binding");

            ExtractionDirector director = UiFinalPlayMode.Ctx().ExtractionDirector;
            director.ActiveSlot = NebulaSaveSlotQuery.FirstSlot + 1;
            director.SaveNow("PHYSICAL BINDING");
            yield return null;
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey, "saving reset the binding");

            director.LoadNow();
            yield return null;
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey, "loading reset the binding");

            UiFinalPlayMode.Boot().RestartCampaign();
            yield return UiFinalPlayMode.SettleAfterSessionStart();
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey, "a restart reset the binding");
        }

        /// <summary>
        /// The shipping source is a real keyboard read and is what is installed unless
        /// a test replaces it. Asserted here so the seam cannot quietly ship with a
        /// test double as its default.
        /// </summary>
        [UnityTest]
        public IEnumerator TheDefaultSource_IsTheRealKeyboard()
        {
            NebulaKeyCaptureListener.ResetSourceToKeyboard();
            Assert.IsInstanceOf<LegacyUnityKeyInputSource>(NebulaKeyCaptureListener.Source,
                "the shipping key-capture source is not the legacy Input reader");

            // And it cannot be left switched off: assigning null restores the keyboard
            // rather than leaving a listener that silently reads nothing.
            NebulaKeyCaptureListener.Source = null;
            Assert.IsInstanceOf<LegacyUnityKeyInputSource>(NebulaKeyCaptureListener.Source,
                "clearing the source left the rebind surface reading a dead device");
            yield break;
        }
    }
}
