using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Progression;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The front door's three secondary entries: AYARLAR, KAYIT YÜKLE, KÜNYE.
    /// <para>
    /// All three raised an event and nothing in production listened, so all three
    /// were controls that answered a click with nothing. Every test here clicks the
    /// real button on the real screen and then asserts on what is <b>on screen</b>
    /// afterwards — the stack moved, the right screen is up, Back put it away — and
    /// never on the event having been raised, because the event was never the part
    /// that was broken.
    /// </para>
    /// </summary>
    public class UiFinalMainMenuFlowTests
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
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        private static Transform MenuLayer()
        {
            NebulaUiShell shell = UiFinalPlayMode.Shell();
            Assert.IsNotNull(shell, "there is no interface shell on screen at all");
            return shell.Root(UiLayer.Menu);
        }

        /// <summary>
        /// The control the player could actually click.
        /// <para>
        /// Four screens live on the menu layer at once and three of them carry a
        /// control called "Back". <c>UiFinalPlayMode.Require</c> searches inactive
        /// objects too, so it would happily hand back the Back button of a screen
        /// that is closed — a click that does nothing, asserted as a click that did.
        /// </para>
        /// </summary>
        private static Button Live(Transform root, string name, string why)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name && button.gameObject.activeInHierarchy) return button;

            Assert.Fail($"no control named \"{name}\" is on screen — {why}");
            return null;
        }

        private static bool HasReading(Transform root, string needle)
        {
            foreach (TMPro.TextMeshProUGUI text in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                if (!string.IsNullOrEmpty(text.text) && text.text.Contains(needle)) return true;
            return false;
        }

        /// <summary>Only the text that is actually drawn — a hidden label proves nothing.</summary>
        private static bool HasVisibleReading(Transform root, string needle)
        {
            foreach (TMPro.TextMeshProUGUI text in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if (!text.gameObject.activeInHierarchy) continue;
                if (!string.IsNullOrEmpty(text.text) && text.text.Contains(needle)) return true;
            }
            return false;
        }

        // =====================================================================
        // AYARLAR
        // =====================================================================

        [UnityTest]
        public IEnumerator Settings_OpensTheRealPage_AndBackReturnsToTheMenu()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            NebulaMainMenuHost host = UiFinalPlayMode.Host();
            Assert.IsNotNull(host, "the front door is missing");
            Assert.IsNull(host.SettingsScreen, "the settings screen exists before anyone asked for it");

            NebulaUiShell shell = UiFinalPlayMode.Shell();
            int depth = shell.StackDepth;

            Live(MenuLayer(), "Settings",
                "the front door has no AYARLAR control").onClick.Invoke();
            yield return null;

            Assert.AreEqual(depth + 1, shell.StackDepth, "AYARLAR opened nothing");
            Assert.IsNotNull(host.SettingsScreen, "AYARLAR pushed something that is not the settings screen");

            // The real page, not a menu-only copy of it: the same class the campaign's
            // rail hosts, identified by type rather than by anything on screen.
            Assert.IsInstanceOf<NebulaSettingsPage>(host.SettingsScreen.Page,
                "the front door built its own settings surface instead of reusing the page");

            // And it works with no session behind it. Quality is Unity's own, so the
            // assertion is against Unity and not against the row's label.
            Assert.Greater(QualitySettings.names.Length, 1,
                "this project has a single quality level, so no graphics setting can be exercised");
            int qualityBefore = QualitySettings.GetQualityLevel();
            Button step = qualityBefore < QualitySettings.names.Length - 1
                ? UiFinalPlayMode.RowControl(MenuLayer(), "Kalite", "Up")
                : UiFinalPlayMode.RowControl(MenuLayer(), "Kalite", "Down");
            Assert.IsNotNull(step, "the settings screen has no graphics quality control");
            step.onClick.Invoke();
            Assert.AreNotEqual(qualityBefore, QualitySettings.GetQualityLevel(),
                "the graphics setting on the front door's settings screen did not reach Unity");

            // Back, through the screen's own control.
            Live(MenuLayer(), "Back",
                "the settings screen has no way back").onClick.Invoke();
            yield return null;

            Assert.AreEqual(depth, shell.StackDepth, "Back did not close the settings screen");
            Assert.IsNotNull(UiFinalPlayMode.Host(), "Back dismissed the whole front door");
            Assert.IsTrue(UiFinalPlayMode.Host().MenuVisible, "the menu is not on screen again");
            Assert.IsNotNull(Object.FindObjectOfType<NebulaMainMenuScreen>(),
                "the main menu screen went away with the settings screen");
        }

        /// <summary>
        /// ERİŞİLEBİLİRLİK is its own front-door entry, and it reaches switches that
        /// really move.
        /// <para>
        /// The approved main menu has seven entries and the built one had six: the
        /// accessibility rows existed only as a section partway down AYARLAR, below
        /// interface scale and motion. Reachable, in the sense that a maze is
        /// reachable. This asserts the entry exists, opens its own screen, carries
        /// the switches a player comes for, and does not drag the graphics, audio and
        /// rebinding rows along with them.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Accessibility_IsItsOwnEntry_AndItsSwitchesReachTheEngine()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            NebulaMainMenuHost host = UiFinalPlayMode.Host();
            Assert.IsNotNull(host, "the front door is missing");
            Assert.IsNull(host.AccessibilityScreen,
                "the accessibility screen exists before anyone asked for it");

            NebulaUiShell shell = UiFinalPlayMode.Shell();
            int depth = shell.StackDepth;

            Live(MenuLayer(), "Accessibility",
                "the front door has no ERİŞİLEBİLİRLİK control").onClick.Invoke();
            yield return null;

            Assert.AreEqual(depth + 1, shell.StackDepth, "ERİŞİLEBİLİRLİK opened nothing");
            Assert.IsNotNull(host.AccessibilityScreen,
                "ERİŞİLEBİLİRLİK pushed something that is not the accessibility screen");
            Assert.AreNotSame(host.SettingsScreen, host.AccessibilityScreen,
                "the two front-door pages are the same screen, so one of them is a lie");

            // Same page class as AYARLAR on purpose — one settings authority — but a
            // different screen, so the two cannot be confused by id.
            Assert.IsInstanceOf<NebulaSettingsPage>(host.AccessibilityScreen.Page);
            StringAssert.Contains("accessibility", host.AccessibilityScreen.ScreenId,
                "the accessibility screen shares the settings screen's id");

            // A switch that reaches the engine, not just a row that redraws.
            bool safeModeBefore = LightingAccessibility.PhotosensitivitySafeMode;
            Button safeMode = UiFinalPlayMode.RowControl(MenuLayer(),
                "Işık Duyarlılığı Güvenli Modu", "Toggle");
            Assert.IsNotNull(safeMode,
                "the accessibility screen has no photosensitivity control");
            safeMode.onClick.Invoke();
            Assert.AreNotEqual(safeModeBefore, LightingAccessibility.PhotosensitivitySafeMode,
                "the photosensitivity switch did not reach LightingAccessibility");
            safeMode.onClick.Invoke();

            // And it is the accessibility door, not a second full settings page.
            Assert.IsNull(UiFinalPlayMode.RowControl(MenuLayer(), "Kalite", "Up"),
                "the accessibility page dragged the graphics rows in with it");

            Live(MenuLayer(), "Back", "the accessibility screen has no way back").onClick.Invoke();
            yield return null;

            Assert.AreEqual(depth, shell.StackDepth, "Back did not close the accessibility screen");
            Assert.IsTrue(UiFinalPlayMode.Host().MenuVisible, "the menu is not on screen again");
        }

        /// <summary>
        /// A settings screen the player has to visit twice is a settings screen that
        /// does not work. What is set before a campaign exists has to be what the
        /// campaign runs under — and a restart must not quietly undo it either.
        /// </summary>
        [UnityTest]
        public IEnumerator Settings_ChosenBeforeACampaign_SurviveIntoItAndThroughARestart()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            Live(MenuLayer(), "Settings", "AYARLAR is missing").onClick.Invoke();
            yield return null;

            bool contrastBefore = LightingAccessibility.HighContrast;
            Live(MenuLayer(), "Back", "no way back").onClick.Invoke();
            yield return null;

            // Re-open and flip it through the row's own control, so the change is the
            // player's click and not a direct write.
            Live(MenuLayer(), "Settings", "AYARLAR is missing").onClick.Invoke();
            yield return null;

            Button contrast = UiFinalPlayMode.RowControl(MenuLayer(), "Yüksek Kontrast", "Toggle");
            Assert.IsNotNull(contrast, "the accessibility row has no control");
            contrast.onClick.Invoke();
            Assert.AreNotEqual(contrastBefore, LightingAccessibility.HighContrast,
                "the accessibility control on the front door did nothing");
            bool wanted = LightingAccessibility.HighContrast;

            float scaleBefore = NebulaUiShell.UiScale;
            Live(MenuLayer(), "Back", "no way back").onClick.Invoke();
            yield return null;

            Live(MenuLayer(), "New",
                "the front door has no YENİ SEFER control").onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.AreEqual(wanted, LightingAccessibility.HighContrast,
                "the setting chosen at the front door was reset by starting a campaign");
            Assert.AreEqual(scaleBefore, NebulaUiShell.UiScale, 0.001f,
                "starting a campaign moved the interface scale");

            UiFinalPlayMode.Boot().RestartCampaign();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.AreEqual(wanted, LightingAccessibility.HighContrast,
                "SEFERİ YENİDEN BAŞLAT wiped the player's accessibility setting");
        }

        // =====================================================================
        // KAYIT YÜKLE
        // =====================================================================

        [UnityTest]
        public IEnumerator Load_WithNoSaves_ShowsADisabledStateWithItsReason()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            NebulaUiShell shell = UiFinalPlayMode.Shell();
            int depth = shell.StackDepth;

            Live(MainMenuRoot(), "Load",
                "the front door has no KAYIT YÜKLE control").onClick.Invoke();
            yield return null;

            Assert.AreEqual(depth + 1, shell.StackDepth, "KAYIT YÜKLE opened nothing");

            NebulaSaveSlotScreen screen = UiFinalPlayMode.Host().LoadScreen;
            Assert.IsNotNull(screen, "KAYIT YÜKLE pushed something that is not the save-slot screen");
            Assert.AreEqual(NebulaSaveSlotQuery.SlotCount, screen.SlotCount,
                "the screen does not offer the slots the save system has");
            Assert.AreEqual(0, screen.PlayableSlots, "the slots were deleted, so none can be playable");
            Assert.IsNotNull(screen.NothingToLoadReason,
                "an empty save-slot screen must say why it is empty");

            // Every slot's own control is disabled and carries the reason, rather than
            // being a live button that would fail on click.
            for (int slot = NebulaSaveSlotQuery.FirstSlot;
                 slot < NebulaSaveSlotQuery.FirstSlot + NebulaSaveSlotQuery.SlotCount; slot++)
            {
                Transform card = FindByName(MenuLayer(), "Slot " + slot);
                Assert.IsNotNull(card, $"the save screen has no card for slot {slot}");
                Button load = UiFinalPlayMode.Require(card, "Load", $"slot {slot} has no load control");
                Assert.IsFalse(load.interactable, $"slot {slot} is empty and still offers to load");
            }

            Assert.IsTrue(HasVisibleReading(MenuLayer(), "BU YUVA BOŞ"),
                "an empty slot does not say so on screen");

            Live(MenuLayer(), "Back", "no way back").onClick.Invoke();
            yield return null;
            Assert.AreEqual(depth, shell.StackDepth, "Back did not close the save-slot screen");
        }

        [UnityTest]
        public IEnumerator Load_ChoosingAValidSave_StartsExactlyOneSessionThatRestoresIt()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            // A real campaign, saved to a slot that is not the first one, then closed.
            Live(MenuLayer(), "New", "no YENİ SEFER control").onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            ProgressionService.Award("test.load-screen", 9000);
            int savedLevel = ProgressionService.State.Level;
            Assert.Greater(savedLevel, 1, "the campaign never advanced, so a restore proves nothing");

            int chosen = NebulaSaveSlotQuery.FirstSlot + 1;
            ExtractionDirector director = UiFinalPlayMode.Ctx().ExtractionDirector;
            director.ActiveSlot = chosen;
            director.SaveNow("LOAD SCREEN");
            yield return null;

            yield return UiFinalPlayMode.EndSessionAndSettle();
            ProgressionService.Reset();

            Live(MainMenuRoot(), "Load", "no KAYIT YÜKLE control").onClick.Invoke();
            yield return null;

            NebulaSaveSlotScreen screen = UiFinalPlayMode.Host().LoadScreen;
            Assert.AreEqual(1, screen.PlayableSlots, "exactly one slot was written");

            Transform card = FindByName(MenuLayer(), "Slot " + chosen);
            Assert.IsNotNull(card, $"the save screen has no card for slot {chosen}");
            Button load = UiFinalPlayMode.Require(card, "Load", "the saved slot has no load control");
            Assert.IsTrue(load.interactable, "a playable slot is offered as dead");

            load.onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.AreEqual(savedLevel, ProgressionService.State.Level,
                "loading a slot started a brand new campaign instead of restoring it");
            Assert.AreEqual(1, Object.FindObjectsOfType<ShipController>().Length,
                "loading a slot built a second ship");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "the menu's own shell did not stand down");
            Assert.AreEqual(1, Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>(true).Length,
                "two event systems means two things deciding what the mouse hit");
            Assert.IsNull(UiFinalPlayMode.Host(),
                "the menu host is still alive alongside the session it started");
            Assert.AreEqual(1, Object.FindObjectsOfType<ExtractionDirector>().Length,
                "loading a slot left two extraction directors running");
        }

        /// <summary>
        /// A save written by a newer build must be refused with the reason, not
        /// loaded. The file is written by hand because there is no way to produce a
        /// future schema from inside this build — which is the point.
        /// </summary>
        [UnityTest]
        public IEnumerator Load_ASaveFromANewerBuild_IsRefusedWithItsReason()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            int slot = NebulaSaveSlotQuery.FirstSlot;
            var envelope = new SaveEnvelope
            {
                schemaVersion = SaveService.CurrentSchema + 5,
                campaignId = "from-the-future",
                revision = 99,
                savedAtUtc = System.DateTime.UtcNow.ToString("o"),
                payloadJson = "{}",
            };
            File.WriteAllText(SaveService.SlotFile(slot), JsonUtility.ToJson(envelope));

            // And a genuinely broken one beside it, so "unreadable" and "too new" are
            // proven to be different answers rather than one catch-all.
            File.WriteAllText(SaveService.SlotFile(slot + 1), "{ this is not json");

            Live(MainMenuRoot(), "Load", "no KAYIT YÜKLE control").onClick.Invoke();
            yield return null;

            NebulaSaveSlotScreen screen = UiFinalPlayMode.Host().LoadScreen;
            Assert.AreEqual(0, screen.PlayableSlots, "a future-schema save was offered as playable");

            Transform card = FindByName(MenuLayer(), "Slot " + slot);
            Button load = UiFinalPlayMode.Require(card, "Load", "the slot has no load control");
            Assert.IsFalse(load.interactable, "a save from a newer build was offered as loadable");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "DAHA YENİ BİR SÜRÜMLE"),
                "the screen does not say the save came from a newer build");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "OKUNAMADI"),
                "the screen does not distinguish an unreadable save from a future one");

            // And clicking it anyway changes nothing.
            load.onClick.Invoke();
            yield return null;
            Assert.IsFalse(UiFinalPlayMode.Boot().SessionActive,
                "a refused save still started a campaign");
            Assert.AreEqual(-1, screen.LastRequestedSlot,
                "the screen passed a refused slot to the launcher");
        }

        // =====================================================================
        // KÜNYE
        // =====================================================================

        [UnityTest]
        public IEnumerator Credits_OpensRealCopy_AndBackReturnsToTheMenu()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            NebulaUiShell shell = UiFinalPlayMode.Shell();
            int depth = shell.StackDepth;

            Live(MenuLayer(), "Credits",
                "the front door has no KÜNYE control").onClick.Invoke();
            yield return null;

            Assert.AreEqual(depth + 1, shell.StackDepth, "KÜNYE opened nothing");
            NebulaCreditsScreen credits = UiFinalPlayMode.Host().CreditsScreen;
            Assert.IsNotNull(credits, "KÜNYE pushed something that is not the credits screen");
            Assert.Greater(credits.BlockCount, 0, "the credits panel has no content");

            // No placeholder, no debug text, and no empty block.
            foreach ((string heading, string body) in NebulaCreditsScreen.Blocks())
            {
                Assert.IsNotEmpty(heading);
                Assert.IsNotEmpty(body, $"the {heading} block is a heading over nothing");
                foreach (string banned in new[] { "TODO", "TBD", "Lorem", "PLACEHOLDER", "XXX", "FIXME" })
                    StringAssert.DoesNotContain(banned, heading + body,
                        $"the credits panel ships placeholder text in {heading}");
            }

            // Composed from what this build actually is.
            Assert.IsTrue(HasVisibleReading(MenuLayer(), SaveService.CurrentSchema.ToString()),
                "the credits do not state the build's own save schema");

            // Turkish characters survive to the screen, and long strings wrap rather
            // than run out of the panel.
            Assert.IsTrue(HasReading(MenuLayer(), "TEŞEKKÜR"),
                "Turkish characters did not reach the credits panel");

            RectTransform panel = (RectTransform)FindByName(MenuLayer(), "Panel");
            Assert.IsNotNull(panel, "the credits panel has no card");
            Canvas.ForceUpdateCanvases();
            foreach (TMPro.TextMeshProUGUI text in panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if (!text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) continue;
                Assert.LessOrEqual(text.rectTransform.rect.width, panel.rect.width + 1f,
                    $"{text.name} is wider than the panel it sits in");
                Assert.IsTrue(text.enableWordWrapping || text.rectTransform.rect.width >= 100f,
                    $"{text.name} neither wraps nor has room not to");
            }

            Live(MenuLayer(), "Back", "no way back").onClick.Invoke();
            yield return null;
            Assert.AreEqual(depth, shell.StackDepth, "Back did not close the credits screen");
            Assert.IsNotNull(Object.FindObjectOfType<NebulaMainMenuScreen>(),
                "the main menu screen went away with the credits");
        }

        /// <summary>
        /// All three, one after another, with Back between each. A host that pushes
        /// without popping, or builds a second screen per open, shows up here and
        /// nowhere else.
        /// </summary>
        [UnityTest]
        public IEnumerator AllThreeEntries_OpenAndCloseRepeatedly_WithoutStackingUp()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            NebulaUiShell shell = UiFinalPlayMode.Shell();
            int depth = shell.StackDepth;

            for (int round = 0; round < 3; round++)
            {
                foreach (string entry in new[] { "Settings", "Load", "Credits" })
                {
                    Live(MainMenuRoot(), entry, $"{entry} is missing").onClick.Invoke();
                    yield return null;
                    Assert.AreEqual(depth + 1, shell.StackDepth,
                        $"{entry} did not open exactly one screen on round {round}");

                    Live(MenuLayer(), "Back", $"{entry} has no way back").onClick.Invoke();
                    yield return null;
                    Assert.AreEqual(depth, shell.StackDepth,
                        $"{entry} did not close on round {round}");
                }
            }

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaSaveSlotScreen>(true).Length,
                "opening KAYIT YÜKLE three times built three screens");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaCreditsScreen>(true).Length,
                "opening KÜNYE three times built three screens");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaMenuPageScreen>(true).Length,
                "opening AYARLAR three times built three settings screens");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "the front door grew a second shell");
        }

        /// <summary>The front door's own root, so a control name it shares with a screen resolves to the menu's.</summary>
        private static Transform MainMenuRoot()
        {
            Transform root = FindByName(MenuLayer(), "Main Menu Root");
            Assert.IsNotNull(root, "the main menu screen is not on the menu layer");
            return root;
        }

        private static Transform FindByName(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.name == name) return t;
            return null;
        }
    }
}
