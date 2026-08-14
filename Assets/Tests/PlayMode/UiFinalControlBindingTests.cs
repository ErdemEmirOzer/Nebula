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
    /// Key rebinding, from the screen the player uses.
    /// <para>
    /// <see cref="CombatBindings"/> has carried <c>TrySetBinding</c>,
    /// <c>CurrentBindings</c> and <c>ResetToDefaults</c> since the combat slice and
    /// nothing in the interface ever called any of them, so nine keys were
    /// reassignable in principle and fixed in practice. These tests drive the real
    /// controls — the row's own ATA button arms the capture, the section's own
    /// <c>Apply</c> is the seam the listener's <c>Update</c> calls on a real key
    /// press — and then read the binding back through <c>CurrentBindings</c>, which
    /// is where <c>ShipCombat</c> reads it.
    /// </para>
    /// <para>
    /// The one thing not simulated is the physical key press itself. Legacy
    /// <c>Input</c> cannot be driven from a batch-mode test, so the capture is
    /// completed through the same public method the listener calls with the key it
    /// read. Everything on either side of that — arming, refusing, storing,
    /// cancelling, resetting, labelling — is the production path.
    /// </para>
    /// </summary>
    public class UiFinalControlBindingTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
            UiFinalPlayMode.DeleteEverySlot();
        }

        /// <summary>
        /// Every screen closes first. A shell still holding the pause when the test
        /// ends restores <c>InputGate.Suppressed</c> from its own saved value at
        /// whatever moment the next scene load happens to destroy it — which hands
        /// the next test a gated world with nothing on screen, and every contextual
        /// ship component silently stops updating.
        /// </summary>
        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
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

        /// <summary>
        /// Cold boot → AYARLAR → SAVAŞ TUŞLARINI ATA, every step through the control
        /// the player clicks. Returns the section that is now on screen.
        /// </summary>
        private static IEnumerator OpenTheBindingSurface(System.Action<NebulaSettingsPage> then)
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            Live(MenuLayer(), "Settings", "the front door has no AYARLAR control").onClick.Invoke();
            yield return null;

            NebulaMenuPageScreen screen = UiFinalPlayMode.Host().SettingsScreen;
            Assert.IsNotNull(screen, "AYARLAR opened nothing");
            var page = screen.Page as NebulaSettingsPage;
            Assert.IsNotNull(page, "the settings screen is not hosting the settings page");
            Assert.IsFalse(page.BindingPanelOpen, "the rebinding panel is open before anyone asked");

            Live(MenuLayer(), NebulaSettingsPage.OpenBindingsControlName,
                "the controls section has no way to reach the rebinding surface").onClick.Invoke();
            yield return null;

            Assert.IsTrue(page.BindingPanelOpen, "the rebinding control opened nothing");
            then(page);
        }

        private static Button RowControl(CombatAction action, string control) =>
            UiFinalPlayMode.RowControl(MenuLayer(),
                NebulaControlBindingSection.RowName(action), control);

        // =====================================================================
        // The list itself
        // =====================================================================

        [UnityTest]
        public IEnumerator EveryAction_IsListedExactlyOnce_WithTheKeyTheGameReads()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            var actions = new List<CombatAction>();
            foreach ((CombatAction action, KeyCode _) in CombatBindings.CurrentBindings())
            {
                Assert.IsFalse(actions.Contains(action),
                    $"{action} is offered twice; two rows would race for the same key press");
                actions.Add(action);
            }

            // Every rebindable action, not only the nine combat ones. The count used
            // to be pinned to CombatBindings alone, which was right when the surface
            // only offered those nine; every rebindable contextual row has been on it
            // since, so the old expectation had gone stale and would have been red
            // against a screen that is doing more, not less.
            int expected = actions.Count;
            foreach (InputActionDefinition definition in NebulaInputActions.All)
                if (definition.Rebindable && !definition.Combat.HasValue) expected++;

            Assert.AreEqual(expected, page.Bindings.RowCount,
                "the screen lists a different number of actions than the catalog has rebindable");

            // Every row's label is the real binding, read back from the accessor the
            // combat code reads -- not from whatever the row was built with.
            page.Bindings.ShowGroup(InputContext.Combat);
            foreach ((CombatAction action, KeyCode key) in CombatBindings.CurrentBindings())
            {
                Assert.IsNotNull(RowControl(action, NebulaControlBindingSection.RebindControlName),
                    $"{action} has no way to be reassigned");
                Assert.AreEqual(NebulaControlBindings.KeyLabel(key), page.Bindings.LabelFor(action),
                    $"the {action} row quotes a key the game does not read");
            }
        }

        /// <summary>
        /// Every rebindable row is reachable through a group the rail offers, and the
        /// group that is showing keeps its rows inside the panel they are drawn in.
        /// <para>
        /// Thirty-one rows were laid out down one 544px column at 48px each. Two
        /// thirds of them were drawn past the bottom of the panel and off the page, so
        /// the extraction, logistics, processing and POI keys existed in the catalog,
        /// existed as rows, and could not be reached by any player. Nothing measured
        /// it because nothing had ever measured this panel's geometry at all.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryGroupOfRows_FitsInsideTheRebindingPanel()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            Assert.IsNotEmpty(page.Bindings.Groups, "the rebinding surface offers no groups at all");

            RectTransform panel = null;
            foreach (RectTransform rect in MenuLayer().GetComponentsInChildren<RectTransform>(true))
                if (rect.gameObject.name == NebulaSettingsPage.BindingPanelName) panel = rect;
            Assert.IsNotNull(panel, "the rebinding panel is not on screen");

            var offenders = new List<string>();
            foreach (InputContext group in page.Bindings.Groups)
            {
                if (page.Bindings.SelectedGroup != group)
                {
                    Button rail = Live(MenuLayer(),
                        NebulaControlBindingSection.GroupControlName(group),
                        $"the rail has no control for the {group} bindings");
                    Assert.IsTrue(rail.interactable,
                        $"the rail control for {group} is on screen but cannot be clicked");
                    rail.onClick.Invoke();
                }

                Canvas.ForceUpdateCanvases();
                yield return null;

                Assert.AreEqual(group, page.Bindings.SelectedGroup,
                    $"the rail control for {group} did not select it");

                Rect bounds = WorldRect(panel);
                foreach (RectTransform rect in panel.GetComponentsInChildren<RectTransform>(false))
                {
                    if (!rect.gameObject.name.StartsWith("Bind ")) continue;
                    Rect row = WorldRect(rect);
                    if (row.yMin < bounds.yMin - 0.5f || row.yMax > bounds.yMax + 0.5f
                        || row.xMin < bounds.xMin - 0.5f || row.xMax > bounds.xMax + 0.5f)
                        offenders.Add($"{group}/{rect.gameObject.name}");
                }
            }

            Assert.IsEmpty(offenders,
                "these rows are drawn outside the rebinding panel and cannot be reached: "
                + string.Join(", ", offenders));
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return new Rect(corners[0].x, corners[0].y,
                corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        }

        /// <summary>
        /// Six of the nine defaults are a mouse button or a digit, and both print as
        /// engine identifiers. A screen quoting "Mouse0" and "Alpha6" is a screen no
        /// player can match to their hardware.
        /// </summary>
        [UnityTest]
        public IEnumerator MouseButtonsAndDigits_ReadAsWhatThePlayerPresses()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            Assert.AreEqual("SOL FARE", page.Bindings.LabelFor(CombatAction.FireLaser));
            Assert.AreEqual("SAĞ FARE", page.Bindings.LabelFor(CombatAction.FirePlasma));
            Assert.AreEqual("ORTA FARE", page.Bindings.LabelFor(CombatAction.FireEm));
            Assert.AreEqual("6", page.Bindings.LabelFor(CombatAction.Repair));
            Assert.AreEqual("3", page.Bindings.LabelFor(CombatAction.CombatBoost));

            foreach ((CombatAction action, KeyCode _) in CombatBindings.CurrentBindings())
            {
                string label = page.Bindings.LabelFor(action);
                StringAssert.DoesNotContain("Mouse", label, $"{action} quotes an engine identifier");
                StringAssert.DoesNotContain("Alpha", label, $"{action} quotes an engine identifier");
            }
        }

        // =====================================================================
        // Rebinding
        // =====================================================================

        [UnityTest]
        public IEnumerator ARebind_ReachesTheServiceTheCombatCodeReads()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            KeyCode before = CombatBindings.RepairKey;
            Assert.AreNotEqual(KeyCode.Backslash, before, "this test needs a key nothing holds");

            RowControl(CombatAction.Repair, NebulaControlBindingSection.RebindControlName)
                .onClick.Invoke();
            Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing,
                "the ATA control did not arm the capture");
            Assert.AreEqual("…", page.Bindings.LabelFor(CombatAction.Repair),
                "an armed row still shows the old key as if nothing were happening");

            Assert.AreEqual(ControlRebindResult.Accepted,
                page.Bindings.Apply(KeyCode.Backslash), "a free key was refused");

            Assert.IsNull(page.Bindings.Capturing, "the capture stayed armed after it succeeded");
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey,
                "the rebind was accepted and then not stored");

            bool readBack = false;
            foreach ((CombatAction action, KeyCode key) in CombatBindings.CurrentBindings())
                if (action == CombatAction.Repair && key == KeyCode.Backslash) readBack = true;
            Assert.IsTrue(readBack,
                "ShipCombat reads its bindings from CurrentBindings and that still quotes the old key");

            Assert.AreEqual(NebulaControlBindings.KeyLabel(KeyCode.Backslash),
                page.Bindings.LabelFor(CombatAction.Repair),
                "the label on screen and the binding the game reads disagree");
        }

        [UnityTest]
        public IEnumerator AKeyAnotherCombatActionHolds_IsRefusedAndChangesNothing()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            KeyCode taken = CombatBindings.CombatBoostKey;
            KeyCode before = CombatBindings.RepairKey;

            RowControl(CombatAction.Repair, NebulaControlBindingSection.RebindControlName)
                .onClick.Invoke();
            Assert.AreEqual(ControlRebindResult.ConflictsWithAction,
                page.Bindings.Apply(taken), "a key another combat action holds was accepted");

            Assert.AreEqual(before, CombatBindings.RepairKey, "a refused rebind still moved the binding");
            Assert.AreEqual(taken, CombatBindings.CombatBoostKey, "a refused rebind stole the other action's key");
            Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing,
                "a refusal dropped the player out of the mode they deliberately entered");
            StringAssert.Contains("ZATEN", page.Bindings.StatusText ?? string.Empty,
                "the refusal did not say the key was already in use");
        }

        [UnityTest]
        public IEnumerator AKeyANonCombatSystemOwns_IsRefusedAndChangesNothing()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            // Five different owners: flight, the extraction contextual key, a system
            // shortcut, and the two keys the hand-kept reserved array used to miss —
            // the processing slot brackets and the tutorial card's Enter, both read
            // over live gameplay. The HUD toggle that used to stand here was removed
            // with the dead binding it named.
            var reserved = new[]
            {
                KeyCode.W,
                ShipExtraction.ScanKey,
                NebulaInputBindings.ManualSaveKey,
                KeyCode.LeftBracket,
                KeyCode.Return,
            };

            foreach (KeyCode key in reserved)
            {
                KeyCode before = CombatBindings.RepairKey;
                if (page.Bindings.Capturing == null)
                    RowControl(CombatAction.Repair, NebulaControlBindingSection.RebindControlName)
                        .onClick.Invoke();

                Assert.AreEqual(ControlRebindResult.ReservedBySystem, page.Bindings.Apply(key),
                    $"{key} is owned by another system and the screen took it anyway");
                Assert.AreEqual(before, CombatBindings.RepairKey,
                    $"a refused {key} still moved the binding");
            }

            page.Bindings.CancelCapture();
        }

        /// <summary>
        /// F1–F12 are the developer overlays' keys. No runtime source may even name
        /// one, so the rejection is expressed on the enum's own name rather than as a
        /// range, and this walks all twelve by that name.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryFunctionKey_IsRefusedEntirely()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            for (int index = 1; index <= 12; index++)
            {
                KeyCode key = (KeyCode)System.Enum.Parse(typeof(KeyCode), "F" + index);
                Assert.IsTrue(NebulaControlBindings.IsFunctionKey(key), $"F{index} was not recognised");

                KeyCode before = CombatBindings.RepairKey;
                if (page.Bindings.Capturing == null)
                    RowControl(CombatAction.Repair, NebulaControlBindingSection.RebindControlName)
                        .onClick.Invoke();

                Assert.AreEqual(ControlRebindResult.FunctionKey, page.Bindings.Apply(key),
                    $"F{index} was accepted as a player binding");
                Assert.AreEqual(before, CombatBindings.RepairKey, $"a refused F{index} still moved the binding");

                // And the authority itself refuses, not merely this screen. A rule
                // enforced only on the path the player happens to take is a rule
                // every other caller walks straight past.
                Assert.IsTrue(CombatBindingValidator.IsFunctionKey(key),
                    $"the binding validator does not recognise F{index}");
                Assert.IsFalse(CombatBindings.TrySetBinding(CombatAction.Repair, key),
                    $"CombatBindings.TrySetBinding accepted F{index} directly");
                Assert.AreEqual(before, CombatBindings.RepairKey,
                    $"a refused F{index} still moved the binding through the service");
            }

            StringAssert.Contains("F1", page.Bindings.StatusText ?? string.Empty,
                "the refusal does not tell the player function keys are off limits");

            // And the plain F key is not a function key: it is a real player binding
            // this rule must never swallow.
            Assert.IsFalse(NebulaControlBindings.IsFunctionKey(KeyCode.F),
                "the plain F key was mistaken for a function key");

            page.Bindings.CancelCapture();
        }

        [UnityTest]
        public IEnumerator Cancel_LeavesTheExistingBindingExactlyWhereItWas()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            KeyCode before = CombatBindings.RepairKey;

            RowControl(CombatAction.Repair, NebulaControlBindingSection.RebindControlName)
                .onClick.Invoke();
            Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing);

            Button cancel = RowControl(CombatAction.Repair, NebulaControlBindingSection.CancelControlName);
            Assert.IsNotNull(cancel, "an armed row offers no way out");
            Assert.IsTrue(cancel.gameObject.activeInHierarchy, "the cancel control is armed but not on screen");
            cancel.onClick.Invoke();

            Assert.IsNull(page.Bindings.Capturing, "cancel left the capture armed");
            Assert.AreEqual(before, CombatBindings.RepairKey, "cancel changed the binding");
            Assert.AreEqual(NebulaControlBindings.KeyLabel(before),
                page.Bindings.LabelFor(CombatAction.Repair),
                "cancel left the row showing a key that is not the binding");

            // A key pressed after cancelling belongs to nobody.
            Assert.AreEqual(ControlRebindResult.Refused, page.Bindings.Apply(KeyCode.Backslash),
                "a key captured after cancelling was still applied");
            Assert.AreEqual(before, CombatBindings.RepairKey, "a key after cancelling moved the binding");
        }

        [UnityTest]
        public IEnumerator ResetToDefaults_RestoresEveryShippedKey()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            RowControl(CombatAction.Repair, NebulaControlBindingSection.RebindControlName)
                .onClick.Invoke();
            page.Bindings.Apply(KeyCode.Backslash);
            RowControl(CombatAction.CycleTarget, NebulaControlBindingSection.RebindControlName)
                .onClick.Invoke();
            page.Bindings.Apply(KeyCode.Slash);

            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey);
            Assert.AreEqual(KeyCode.Slash, CombatBindings.CycleTargetKey);

            Live(MenuLayer(), NebulaControlBindingSection.ResetControlName,
                "there is no way back to the shipped keys").onClick.Invoke();

            Assert.AreEqual(KeyCode.Alpha6, CombatBindings.RepairKey, "Repair was not restored");
            Assert.AreEqual(KeyCode.Alpha4, CombatBindings.CycleTargetKey, "Cycle Target was not restored");
            Assert.AreEqual(KeyCode.Mouse0, CombatBindings.FireLaserKey);
            Assert.AreEqual(KeyCode.Mouse1, CombatBindings.FirePlasmaKey);
            Assert.AreEqual(KeyCode.Mouse2, CombatBindings.FireEmKey);
            Assert.AreEqual(KeyCode.Alpha5, CombatBindings.ManualTargetSelectKey);
            Assert.AreEqual(KeyCode.Alpha7, CombatBindings.CycleDefenseModeKey);
            Assert.AreEqual(KeyCode.Alpha8, CombatBindings.SetFocusTargetKey);

            Assert.IsNull(page.Bindings.Capturing, "reset left a capture armed");
            Assert.AreEqual("6", page.Bindings.LabelFor(CombatAction.Repair),
                "the rows still show the keys the reset threw away");
        }

        // =====================================================================
        // Persistence
        // =====================================================================

        /// <summary>
        /// The bindings are process-wide state that no save owns, which is exactly
        /// why this needs proving rather than assuming: a save/load or a restart that
        /// reset them would be silent, and the player would find out in combat.
        /// </summary>
        [UnityTest]
        public IEnumerator ARebind_SurvivesSaveLoadAndRestart()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            RowControl(CombatAction.Repair, NebulaControlBindingSection.RebindControlName)
                .onClick.Invoke();
            Assert.AreEqual(ControlRebindResult.Accepted, page.Bindings.Apply(KeyCode.Backslash));

            Live(MenuLayer(), "Back", "the settings screen has no way back").onClick.Invoke();
            yield return null;

            Live(MenuLayer(), "New", "the front door has no YENİ SEFER control").onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey,
                "starting a campaign reset the player's binding");

            int slot = NebulaSaveSlotQuery.FirstSlot + 1;
            ExtractionDirector director = UiFinalPlayMode.Ctx().ExtractionDirector;
            director.ActiveSlot = slot;
            director.SaveNow("BINDING PERSISTENCE");
            yield return null;

            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey, "saving reset the binding");

            director.LoadNow();
            yield return null;
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey, "loading reset the binding");

            UiFinalPlayMode.Boot().RestartCampaign();
            yield return UiFinalPlayMode.SettleAfterSessionStart();
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey, "a restart reset the binding");

            yield return UiFinalPlayMode.EndSessionAndSettle();
            Assert.AreEqual(KeyCode.Backslash, CombatBindings.RepairKey,
                "leaving the campaign reset the binding");
        }

        /// <summary>
        /// An armed capture that survives the screen being closed would swallow the
        /// next key the player presses anywhere else in the game.
        /// </summary>
        [UnityTest]
        public IEnumerator ClosingTheScreenWhileArmed_AbandonsTheCapture()
        {
            NebulaSettingsPage page = null;
            yield return OpenTheBindingSurface(p => page = p);

            KeyCode before = CombatBindings.RepairKey;
            RowControl(CombatAction.Repair, NebulaControlBindingSection.RebindControlName)
                .onClick.Invoke();
            Assert.AreEqual(CombatAction.Repair, page.Bindings.Capturing);

            Live(MenuLayer(), "Back", "the settings screen has no way back").onClick.Invoke();
            yield return null;

            Assert.IsNull(page.Bindings.Capturing, "an armed capture survived the screen closing");
            Assert.AreEqual(before, CombatBindings.RepairKey, "closing the screen changed a binding");
        }
    }
}
