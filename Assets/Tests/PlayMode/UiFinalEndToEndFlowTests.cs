using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Progression;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// One player, one sitting: the whole seventeen-step journey through real
    /// public callbacks and real backend seams, from a cold boot to the quit
    /// request.
    /// <para>
    /// The long test is deliberate. Every page and every action here has its own
    /// unit somewhere, and all of those passed while the journey between them did
    /// not exist — a settings page nobody can reach from the front door, a
    /// Continue that starts a new campaign, three save slots that are one file.
    /// Only a test that walks the whole way finds those.
    /// </para>
    /// <para>
    /// Nothing here completes a step by setting the state the step was supposed to
    /// produce. Every action is the control the player clicks or the public seam
    /// that control calls; where a control does not exist yet, the test says so and
    /// fails, because a journey with a hole in it is the finding.
    /// </para>
    /// <para>
    /// The one deliberate exception is the quit. <c>NebulaSessionLauncher.QuitGame</c>
    /// sets <c>EditorApplication.isPlaying = false</c>, so calling it from inside a
    /// PlayMode test does not assert the quit — it ends play mode and takes every
    /// test scheduled after it down with it. The wiring is therefore proven against
    /// a recorder in the same seam the production menu is built with, and the real
    /// exit belongs to the Player smoke run.
    /// </para>
    /// </summary>
    public class UiFinalEndToEndFlowTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;

        /// <summary>
        /// The slots start empty, not merely backed up. Continue resumes whichever
        /// slot is <b>most recent</b>, so a developer's own newer save on another
        /// slot would silently be the thing Continue restores and the test would be
        /// asserting against a campaign it never played.
        /// </summary>
        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
            UiFinalPlayMode.DeleteEverySlot();
        }

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static Transform MenuLayer()
        {
            NebulaUiShell shell = UiFinalPlayMode.Shell();
            Assert.IsNotNull(shell, "there is no interface shell on screen at all");
            return shell.Root(UiLayer.Menu);
        }

        private static bool HasReading(Transform root, string needle)
        {
            foreach (TMPro.TextMeshProUGUI text in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                if (!string.IsNullOrEmpty(text.text) && text.text.Contains(needle)) return true;
            return false;
        }

        /// <summary>
        /// Graphics, interface scale and accessibility — the three a settings screen
        /// can change with or without a session behind it. Each is checked against
        /// the thing it is supposed to move, never against the control's own label:
        /// a row that writes a field nothing reads looks identical on screen to one
        /// that works, which is exactly how the flash-reduction toggle shipped
        /// pointing at a setting no renderer consulted.
        /// </summary>
        private static void ChangeGraphicsScaleAndAccessibility(Transform menuRoot, string where)
        {
            Assert.Greater(QualitySettings.names.Length, 1,
                "this project has a single quality level, so no graphics setting can be exercised");

            int qualityBefore = QualitySettings.GetQualityLevel();
            Button qualityUp = UiFinalPlayMode.RowControl(menuRoot, "Kalite", "Up");
            Button qualityDown = UiFinalPlayMode.RowControl(menuRoot, "Kalite", "Down");
            Assert.IsNotNull(qualityUp, $"the graphics quality row has no control on {where}");
            Assert.IsNotNull(qualityDown, $"the graphics quality row has no down control on {where}");
            (qualityBefore < QualitySettings.names.Length - 1 ? qualityUp : qualityDown).onClick.Invoke();
            Assert.AreNotEqual(qualityBefore, QualitySettings.GetQualityLevel(),
                $"the graphics setting on {where} did not reach Unity");

            float scaleBefore = NebulaUiShell.UiScale;
            Canvas menuCanvas = UiFinalPlayMode.Shell().CanvasFor(UiLayer.Menu);
            var menuScaler = menuCanvas.GetComponent<CanvasScaler>();
            Vector2 referenceBefore = menuScaler != null ? menuScaler.referenceResolution : Vector2.zero;

            Button scaleDown = UiFinalPlayMode.RowControl(menuRoot, "Arayüz Ölçeği", "Down");
            Assert.IsNotNull(scaleDown, $"the interface scale row has no control on {where}");
            scaleDown.onClick.Invoke();
            Assert.AreNotEqual(scaleBefore, NebulaUiShell.UiScale,
                $"the UI scale setting on {where} did nothing");

            // Checked where the scale is actually expressed, not against
            // canvas.scaleFactor. In ScaleWithScreenSize mode the CanvasScaler owns
            // scaleFactor and recomputes it every canvas update from the reference
            // resolution and the real screen size, so scaleFactor only equals UiScale
            // when the screen happens to BE the reference resolution -- true at
            // 1920x1080 and false at every other size a test or a player might run.
            // The interface scale is a multiplier, and the reference resolution is
            // where a multiplier lives.
            Assert.IsNotNull(menuScaler, "the menu canvas has no scaler to express a scale through");
            Assert.AreNotEqual(referenceBefore, menuScaler.referenceResolution,
                "the scale was stored but never reached the canvas scaler");

            bool contrastBefore = LightingAccessibility.HighContrast;
            Button contrast = UiFinalPlayMode.RowControl(menuRoot, "Yüksek Kontrast", "Toggle");
            Assert.IsNotNull(contrast, $"the accessibility row has no control on {where}");
            contrast.onClick.Invoke();
            Assert.AreNotEqual(contrastBefore, LightingAccessibility.HighContrast,
                $"the accessibility setting on {where} did nothing");
        }

        /// <summary>
        /// The control setting. The bindings section on screen is a reading by
        /// design — the keys it quotes are compile-time constants — so what it must
        /// never do is quote a key the game does not read. The one binding surface
        /// that <b>is</b> writable is <see cref="CombatBindings"/>, and it is
        /// changed through its own public seam and read back through the accessor
        /// the combat code uses, so the change is proven where it is consumed.
        /// </summary>
        private static void ChangeAControlSetting(Transform menuRoot)
        {
            Assert.IsNull(UiFinalPlayMode.RowControl(menuRoot, "Tara", "Toggle"),
                "a bindings row became interactive with no rebinding service behind it");
            Assert.IsTrue(HasReading(menuRoot, NebulaText.UpperAscii(ShipExtraction.ScanKey.ToString())),
                "the bindings screen does not quote the scan key the game reads");

            KeyCode before = CombatBindings.RepairKey;
            // Alpha9 is the one number key nothing owns: 0/1/2 are reserved by other
            // systems and 3-8 are the other combat actions, so anything else here
            // fails as a conflict rather than as the rebinding seam refusing a free
            // key. Alpha6 is Repair's own default, which is free by definition
            // because a binding never conflicts with itself.
            KeyCode wanted = before == KeyCode.Alpha9 ? KeyCode.Alpha6 : KeyCode.Alpha9;
            Assert.IsTrue(CombatBindings.TrySetBinding(CombatAction.Repair, wanted),
                "the rebinding seam refused a free key");
            Assert.AreEqual(wanted, CombatBindings.RepairKey,
                "the control setting was accepted and then not stored");

            bool readBack = false;
            foreach ((CombatAction action, KeyCode key) in CombatBindings.CurrentBindings())
                if (action == CombatAction.Repair && key == wanted) readBack = true;
            Assert.IsTrue(readBack,
                "the combat code reads its bindings from CurrentBindings and that still quotes the old key");

            // The function keys are reserved, and the rule has to live in the service
            // every caller goes through rather than in one screen: a rejection that
            // only the rebinding UI performs is a rejection any other caller — a save
            // file, a preset, a future control screen — walks straight past. Asserted
            // here through the same seam the accepted key above used, so "reserved"
            // means the same thing to both. The literal is fine in a test: the
            // no-function-key scan covers Assets/Scripts/Nebula, which is where a
            // reserved key must never be bound, not where it may be named.
            foreach (KeyCode reserved in new[] { KeyCode.F1, KeyCode.F9, KeyCode.F12 })
            {
                Assert.IsFalse(CombatBindings.TrySetBinding(CombatAction.Repair, reserved),
                    $"the rebinding seam accepted {reserved}, and F1-F12 are reserved");
                Assert.AreEqual(wanted, CombatBindings.RepairKey,
                    $"{reserved} was refused and the previous binding was overwritten anyway");
            }
        }

        /// <summary>Opens the modal's own confirm control, when the action asked first.</summary>
        /// <summary>
        /// Confirms the shell's confirmation dialog, searching inside the dialog
        /// itself rather than across the whole menu layer.
        /// <para>
        /// "Confirm" is not a unique name on that layer — the bench's quantity box
        /// names its ONAYLA button the same thing — and <c>Find</c> returns the first
        /// match in hierarchy order. This used to work only because composition
        /// happened to build the dialog before the parent menu, so the dialog's
        /// button came first; the moment the dialog started raising itself to the
        /// front (which is what makes it visible at all) the quantity box's button
        /// won instead, and this helper clicked a control that does nothing while
        /// reporting that it had confirmed. Scoping to the dialog's own root is the
        /// fix: it cannot be re-ordered out from under the test.
        /// </para>
        /// </summary>
        private static bool ConfirmIfAsked(NebulaConfirmationDialog confirmation, Transform menuRoot)
        {
            if (confirmation == null || !confirmation.IsOpen) return false;
            UiFinalPlayMode.Require(confirmation.Root, "Confirm",
                "a confirmation is open with no way to confirm it").onClick.Invoke();
            return true;
        }

        /// <summary>
        /// The save-slot card the player would click, by the name the session page
        /// gives it — scoped to that page and not to the whole menu layer.
        /// <para>
        /// "Slot N" is not a unique name in this interface. The resources page names
        /// its processing bays the same way, so an unscoped search over the menu
        /// layer returns whichever the hierarchy happens to yield first and then
        /// reports that the save slot has no save control — which is true of a
        /// processing bay and says nothing about the page under test.
        /// </para>
        /// </summary>
        private static Transform SlotCard(Transform menuRoot, int slot)
        {
            Transform page = null;
            foreach (Transform t in menuRoot.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.name == "Session Column") { page = t; break; }

            Assert.IsNotNull(page,
                "the session page is not on screen, so its save slots cannot be reached");

            foreach (Transform t in page.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.name == "Slot " + slot) return t;
            return null;
        }

        // =====================================================================
        // The whole journey
        // =====================================================================

        [UnityTest]
        public IEnumerator TheWholeJourney_FromAColdBootToTheQuitRequest()
        {
            // ---- 1. boot to the main menu with no gameplay behind it ---------
            yield return UiFinalPlayMode.LoadSceneCold();

            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            Assert.AreEqual(NebulaBootMode.MainMenu, boot.BootMode);
            Assert.IsFalse(boot.SessionActive, "a campaign was running before the player asked for one");
            Assert.IsNull(Object.FindObjectOfType<ShipController>(), "a ship existed before any campaign");

            NebulaMainMenuHost host = UiFinalPlayMode.Host();
            Assert.IsNotNull(host, "the front door is missing");
            Assert.IsTrue(host.MenuVisible);

            NebulaMainMenuScreen frontDoor = Object.FindObjectOfType<NebulaMainMenuScreen>();
            Assert.IsNotNull(frontDoor, "the front door host has no menu screen");
            Assert.AreEqual(-1, frontDoor.ResumeSlot, "there are no saves, so nothing to continue");
            Assert.IsNotNull(frontDoor.ContinueBlockedReason,
                "Continue is unavailable and the menu does not say why");
            Assert.IsFalse(UiFinalPlayMode.Require(MenuLayer(), "Continue",
                "the front door has no Continue control").interactable,
                "Continue is offered with nothing behind it");

            // ---- 2. Settings, from the front door ----------------------------
            NebulaUiShell menuShell = UiFinalPlayMode.Shell();
            int depthBeforeSettings = menuShell.StackDepth;

            UiFinalPlayMode.Require(MenuLayer(), "Settings",
                "the front door has no AYARLAR control").onClick.Invoke();
            yield return null;

            Assert.Greater(UiFinalPlayMode.Shell().StackDepth, depthBeforeSettings,
                "AYARLAR on the main menu opened nothing. NebulaMainMenuScreen raises SettingsRequested " +
                "and the host is the only thing that can subscribe to it; until it does, the control is dead.");

            // ---- 3. graphics, interface scale, accessibility, controls -------
            ChangeGraphicsScaleAndAccessibility(MenuLayer(), "the main menu's settings screen");
            ChangeAControlSetting(MenuLayer());

            // ---- 4. close it and come back to the front door ------------------
            UiFinalPlayMode.Shell().Back();
            yield return null;
            Assert.AreEqual(depthBeforeSettings, UiFinalPlayMode.Shell().StackDepth,
                "closing the settings screen did not return the player to the menu underneath");
            Assert.IsNotNull(UiFinalPlayMode.Host(), "closing settings dismissed the whole front door");

            // ---- 5. New Campaign, through the button ---------------------------
            UiFinalPlayMode.Require(MenuLayer(), "New",
                "the front door has no YENİ SEFER control").onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            // ---- 6. exactly one ship, one context, one shell -------------------
            Assert.AreEqual(1, Object.FindObjectsOfType<ShipController>().Length, "one ship, not two");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "the menu's shell did not stand down");
            Assert.AreEqual(1, Object.FindObjectsOfType<EventSystem>(true).Length,
                "two event systems means two things deciding what the mouse hit");
            Assert.IsNull(UiFinalPlayMode.Host(),
                "the menu host is still alive alongside the session");

            NebulaContext context = UiFinalPlayMode.Ctx();
            Assert.IsNotNull(context);
            NebulaUiShell shell = context.Presentation.UiShell;

            // ---- 7. the HUD is up ---------------------------------------------
            Assert.IsNotNull(context.Presentation.HudVitals, "no vitals widget");
            Assert.IsNotNull(context.Presentation.HudNavigation, "no navigation widget");
            Assert.IsNotNull(context.Presentation.HudContextBar, "no context bar");
            Assert.IsTrue(shell.CanvasFor(UiLayer.HudStatic).enabled, "the HUD is switched off in flight");
            Assert.IsFalse(shell.AnyScreenOpen, "a screen is covering the game the moment it starts");

            // ---- 8. open the parent menu ---------------------------------------
            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsNotNull(menu);
            float clockBeforeTheMenu = Time.timeScale;
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Overview));
            Assert.IsTrue(shell.AnyScreenOpen);
            Assert.IsTrue(InputGate.Suppressed, "an open menu must hold flight input");
            Assert.AreEqual(clockBeforeTheMenu, Time.timeScale,
                "an open menu must NOT hold the clock -- the world keeps running behind it");

            // ---- 9. every one of the ten pages, through the rail ----------------
            var pages = (NebulaUiAssetCatalog.Page[])
                System.Enum.GetValues(typeof(NebulaUiAssetCatalog.Page));
            Assert.AreEqual(10, pages.Length, "the rail is meant to carry ten pages");
            Assert.AreEqual(pages.Length, menu.PageCount);

            Transform menuRoot = shell.Root(UiLayer.Menu);
            foreach (NebulaUiAssetCatalog.Page page in pages)
            {
                UiFinalPlayMode.Require(menuRoot, page + " Rail",
                    $"{page} has no rail row to click").onClick.Invoke();
                Assert.AreEqual(page, menu.CurrentPage, $"the {page} rail row did not open it");
                yield return null;
            }

            // ---- 10. the audio setting, which needs the session's own mixer ----
            menu.ShowPage(NebulaUiAssetCatalog.Page.Settings);
            Assert.IsNotNull(menu.PageOf<NebulaSettingsPage>());

            Audio.NebulaAudioService mixer = context.AudioServiceOrNull;
            Assert.IsNotNull(mixer,
                "the session composed no audio service, so no audio setting can be exercised");

            float volumeBefore = mixer.MasterVolume;
            Button volume = UiFinalPlayMode.RowControl(menuRoot, "Ana Ses",
                volumeBefore > 0.05f ? "Down" : "Up");
            Assert.IsNotNull(volume, "the audio row has no control");
            volume.onClick.Invoke();
            Assert.AreNotEqual(volumeBefore, mixer.MasterVolume, "the audio setting did nothing");
            mixer.SetMasterVolume(volumeBefore);

            // The settings the player changed before the campaign existed must
            // still be the settings the campaign runs under. A session that resets
            // them on composition is a settings screen the player has to visit
            // twice.
            Assert.IsTrue(LightingAccessibility.HighContrast != globals.HighContrast ||
                          !Mathf.Approximately(NebulaUiShell.UiScale, globals.UiScale),
                "the settings chosen at the front door did not survive starting a campaign");

            // ---- 11-13. a real action on each working surface -------------------

            // Resources: a real item in the hold becomes a real row.
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 5000f));
            menu.ShowPage(NebulaUiAssetCatalog.Page.Resources);
            NebulaResourcesPage resources = menu.PageOf<NebulaResourcesPage>();
            resources.Refresh();
            int rowsBefore = resources.VisibleItemRows;

            // Something the ship is not already carrying: a campaign starts with
            // building parts in the hold, so adding more of those would only
            // change a number on a row that already existed.
            string unheld = null;
            foreach (string material in ProcessingCatalog.MaterialIds)
                if (cargo.Get(material) < 0.5f) { unheld = material; break; }
            Assert.IsNotNull(unheld, "the ship starts holding every material");

            cargo.Add(unheld, 30f);
            resources.Refresh();
            Assert.AreEqual(rowsBefore + 1, resources.VisibleItemRows,
                $"real cargo ({unheld}) produced no row on the resources page");

            // Production: the bench on screen is the real bench. Loaded through the
            // backend so that a page keeping its own figure would fail here.
            var processing = context.Ship.GetComponent<ShipProcessing>();
            processing.ApplyTier(2);
            string benchRaw = ExtractionCatalog.ResourceIds[0];
            cargo.Add(benchRaw, 6f);
            Assert.AreEqual(BenchDepositReason.None,
                ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, benchRaw, 4),
                "the bench refused a deposit the hold could pay for");
            resources.Refresh();
            Assert.AreEqual(4, resources.BenchAmount,
                "the processing bench on screen disagrees with the real bench");
            Assert.AreEqual(benchRaw, resources.BenchItemId,
                "the page shows the wrong item on the bench");

            // Progression: the upgrade control acts or refuses with a reason.
            menu.ShowPage(NebulaUiAssetCatalog.Page.Progression);
            NebulaProgressionPage progression = menu.PageOf<NebulaProgressionPage>();
            Assert.Greater(progression.RowCount, 0, "the progression page lists no modules");
            Button upgrade = UiFinalPlayMode.Require(menuRoot, "Upgrade",
                "there is no way to upgrade a module");
            Assert.IsTrue(upgrade.interactable || progression.UpgradeBlockedReason != null,
                "the upgrade control is disabled with no reason given");

            // Hangar: the produce control acts or refuses with a reason -- and says
            // which, in words, on screen. A control that is clicked and reports
            // nothing is the failure this whole pass exists to remove.
            menu.ShowPage(NebulaUiAssetCatalog.Page.Fleet);
            NebulaFleetPage fleet = menu.PageOf<NebulaFleetPage>();
            Button produce = UiFinalPlayMode.Require(menuRoot, "Produce",
                "there is no way to produce a vehicle");
            string statusBefore = fleet.StatusText;
            produce.onClick.Invoke();
            fleet.Refresh();
            Assert.AreNotEqual(statusBefore, fleet.StatusText,
                "producing a vehicle reported neither a start nor a refusal");

            // Planet: evacuation quotes the service's own refusal.
            menu.ShowPage(NebulaUiAssetCatalog.Page.Planets);
            NebulaPlanetsPage planets = menu.PageOf<NebulaPlanetsPage>();
            planets.Refresh();
            Button evacuate = UiFinalPlayMode.Require(menuRoot, "Evacuate",
                "there is no way to evacuate a planet");
            Assert.IsTrue(evacuate.interactable || planets.EvacuateBlockedReason != null,
                "the evacuation control is disabled with no reason given");

            // Combat: the page reads the ship's real hardpoints.
            menu.ShowPage(NebulaUiAssetCatalog.Page.Combat);
            NebulaCombatPage combat = menu.PageOf<NebulaCombatPage>();
            combat.Refresh();
            Assert.GreaterOrEqual(combat.OccupiedHardpoints, 0);
            Assert.LessOrEqual(combat.OccupiedHardpoints, NebulaCombatPage.MaxHardpointRows);

            // Discovery: analysis starts or refuses with a reason.
            menu.ShowPage(NebulaUiAssetCatalog.Page.Discovery);
            NebulaDiscoveryPage discovery = menu.PageOf<NebulaDiscoveryPage>();
            discovery.Refresh();
            Button analyze = UiFinalPlayMode.Require(menuRoot, "Analyze",
                "there is no way to analyse a discovery");
            Assert.IsTrue(analyze.interactable || discovery.AnalysisBlockedReason != null,
                "the analysis control is disabled with no reason given");

            // ---- 14. Save, to the slot the player picked -----------------------
            ProgressionService.Award("test.e2e-flow", 4000);
            int savedLevel = ProgressionService.State.Level;
            Assert.Greater(savedLevel, 1, "the campaign never advanced, so a restore proves nothing");

            menu.ShowPage(NebulaUiAssetCatalog.Page.Session);
            NebulaSessionPage session = menu.PageOf<NebulaSessionPage>();
            Assert.AreEqual(NebulaSaveSlotQuery.SlotCount, session.SlotCount);

            // The second slot's own control, not the first: a page that writes
            // whatever slot the director happens to hold looks identical from the
            // first slot and is wrong from every other one.
            int chosenSlot = NebulaSaveSlotQuery.FirstSlot + 1;
            Transform secondSlot = SlotCard(menuRoot, chosenSlot);
            Assert.IsNotNull(secondSlot, "the session page has no second slot");

            UiFinalPlayMode.Require(secondSlot, "Save",
                "the second slot card has no save control").onClick.Invoke();
            ConfirmIfAsked(context.Presentation.Confirmation, menuRoot);
            yield return null;

            Assert.IsTrue(NebulaSaveSlotQuery.Read(chosenSlot).Exists,
                $"saving to slot {chosenSlot} wrote no file for that slot. Status={session.StatusText}");
            Assert.IsFalse(NebulaSaveSlotQuery.Read(NebulaSaveSlotQuery.FirstSlot).Exists,
                $"saving to slot {chosenSlot} wrote slot {NebulaSaveSlotQuery.FirstSlot} instead");
            StringAssert.Contains("KAYDEDİL", session.StatusText ?? string.Empty,
                "a save that happened told the player nothing");

            // ---- 15. back to the main menu, through the session page ------------
            UiFinalPlayMode.Require(menuRoot, "MainMenu",
                "there is no way out of a campaign: the session page offers no ANA MENÜYE DÖN control")
                .onClick.Invoke();
            Assert.IsTrue(ConfirmIfAsked(context.Presentation.Confirmation, MenuLayer()),
                "leaving a campaign discards unsaved progress and did not ask");

            yield return UiFinalPlayMode.Until(() => !UiFinalPlayMode.Boot().SessionActive,
                "the campaign to close when the player leaves it");
            yield return UiFinalPlayMode.Until(() => UiFinalPlayMode.Host() != null,
                "the main menu to be on screen again once the campaign is closed");
            yield return UiFinalPlayMode.SettleToOneShell();

            // The front door is a screen, so it holds the clock and the modal gate
            // while it is up -- exactly as it does at a cold boot. What must be true
            // is that they are held by the shell that is ON SCREEN, not by the
            // campaign's shell, which is gone: a gate owned by a destroyed object
            // blocks every future modal for the rest of the process, and no
            // screenshot of this moment could tell the two apart. The clock coming
            // back is asserted where it is felt, after Continue below.
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "leaving the campaign left the session's shell alongside the menu's");
            Assert.AreSame(UiFinalPlayMode.Shell(), ExclusiveModal.Owner,
                "the modal gate is held by a shell that is not the one on screen");

            // ---- 16. Continue restores the campaign that was saved --------------
            NebulaMainMenuScreen returned = Object.FindObjectOfType<NebulaMainMenuScreen>();
            Assert.IsNotNull(returned, "the main menu came back without its screen");
            Assert.AreEqual(chosenSlot, returned.ResumeSlot,
                "Continue offers a different slot than the one just saved");

            Button continueButton = UiFinalPlayMode.Require(MenuLayer(), "Continue",
                "the front door has no Continue control");
            Assert.IsTrue(continueButton.interactable,
                $"Continue is dead with a playable save on slot {chosenSlot}: " +
                returned.ContinueBlockedReason);

            ProgressionService.Reset();
            continueButton.onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.AreEqual(savedLevel, ProgressionService.State.Level,
                "Continue started a brand new campaign instead of restoring the save it advertised");
            Assert.AreEqual(1, Object.FindObjectsOfType<ShipController>().Length,
                "Continue added a campaign instead of resuming one");

            // The front door held the clock and the gate; handing over to a campaign
            // is where it has to give them back. A campaign that resumes frozen is
            // the failure this catches, and it is invisible in any single moment --
            // the menu looked right, the save loaded, and nothing moved.
            Assert.AreNotEqual(0f, Time.timeScale, "the campaign resumed with the clock still held");
            Assert.IsFalse(InputGate.Suppressed, "the campaign resumed with the input gate still held");
            Assert.IsNull(ExclusiveModal.Owner, "the campaign resumed with the modal gate still held");

            // ---- 17a. Restart Campaign, through the session page's own control --
            NebulaContext resumed = UiFinalPlayMode.Ctx();
            NebulaParentMenuScreen resumedMenu = resumed.Presentation.ParentMenu;
            ShipController shipBeforeRestart = resumed.Ship;

            Assert.IsTrue(resumedMenu.Open(NebulaUiAssetCatalog.Page.Session));
            UiFinalPlayMode.Require(MenuLayer(), "Restart",
                "the session page offers no SEFERİ YENİDEN BAŞLAT control").onClick.Invoke();
            Assert.IsTrue(ConfirmIfAsked(resumed.Presentation.Confirmation, MenuLayer()),
                "restarting a campaign is irreversible and did not ask");

            yield return UiFinalPlayMode.Until(() =>
            {
                ShipController now = Object.FindObjectOfType<ShipController>();
                return now != null && now != shipBeforeRestart;
            }, "SEFERİ YENİDEN BAŞLAT to actually build a new campaign. It routes through " +
               "NebulaSessionLauncher.StartNewCampaign, which calls SolarSystemBootstrap.BeginSession -- " +
               "and BeginSession is idempotent, so with a session already running it returns the same one " +
               "and nothing restarts while the page reports YENİ SEFER BAŞLATILDI");
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.AreEqual(1, Object.FindObjectsOfType<ShipController>().Length,
                "a restart added a campaign instead of replacing one");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "the restart left two shells");
            Assert.AreEqual(1, Object.FindObjectsOfType<EventSystem>(true).Length,
                "the restart left two event systems");
            Assert.IsNull(ExclusiveModal.Owner);
            Assert.AreNotEqual(0f, Time.timeScale);

            // ---- 17b. Quit ------------------------------------------------------
            // In the session first: quitting discards unsaved progress, so it has to
            // ask rather than leaving on the first click. The confirmation is opened
            // and then cancelled -- confirming it would call the real launcher and
            // end play mode.
            NebulaContext restarted = UiFinalPlayMode.Ctx();
            Assert.IsTrue(restarted.Presentation.ParentMenu.Open(NebulaUiAssetCatalog.Page.Session));
            UiFinalPlayMode.Require(MenuLayer(), "Quit",
                "the session page offers no OYUNDAN ÇIK control").onClick.Invoke();
            Assert.IsTrue(restarted.Presentation.Confirmation.IsOpen,
                "OYUNDAN ÇIK left the application without asking");
            UiFinalPlayMode.Require(MenuLayer(), "Cancel",
                "the quit confirmation has no way out").onClick.Invoke();
            restarted.Presentation.ParentMenu.Close();
            yield return null;

            // And the seam itself: the production menu screen, built with a recorder
            // in the launcher's place, so the click is proven to arrive at
            // INebulaSessionLauncher.QuitGame exactly once without leaving play mode.
            // The Player smoke run is what confirms the real exit.
            var recorder = new UiFinalPlayMode.RecordingLauncher();
            NebulaUiShell scratch = NebulaUiShell.Create();
            NebulaMainMenuScreen probe = NebulaMainMenuScreen.Create(scratch, recorder);

            UiFinalPlayMode.Require(scratch.Root(UiLayer.Menu), "Quit",
                "the main menu has no ÇIKIŞ control").onClick.Invoke();
            Assert.AreEqual(1, recorder.Quits,
                "the quit request never reached the seam that leaves the application");
            Assert.AreEqual(0, recorder.NewCampaigns + recorder.Continues + recorder.Returns,
                "ÇIKIŞ did something other than quit on its way there");

            scratch.Shutdown();
            Object.DestroyImmediate(probe.gameObject);
            Object.DestroyImmediate(scratch.gameObject);
            yield return null;

            Assert.IsNull(ExclusiveModal.Owner, "the journey finished holding the modal gate");
            Assert.AreNotEqual(0f, Time.timeScale, "the journey finished holding the clock");
        }

        // =====================================================================
        // The steps that carry their own seam, kept separate so a failure names
        // one thing rather than a seventeen-step walk.
        // =====================================================================

        /// <summary>
        /// Step 2 — the front door's three secondary entries.
        /// <para>
        /// AYARLAR, KAYIT YÜKLE and KÜNYE raise <c>SettingsRequested</c>,
        /// <c>LoadRequested</c> and <c>CreditsRequested</c> on
        /// <see cref="NebulaMainMenuScreen"/>. Whoever builds the screen is the only
        /// thing that can subscribe, and <see cref="NebulaMainMenuHost"/> is the only
        /// builder — so until the host subscribes, three controls on the front door
        /// do nothing at all when clicked, which is worse than not being there.
        /// </para>
        /// <para>
        /// Proven on the production screen through a recorder: the events fire, the
        /// wiring is the missing half. What the host has to do with them is push a
        /// screen — the shell's stack depth is what the journey above checks.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Step2_TheFrontDoorsSecondaryEntries_ReachSomething()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            NebulaUiShell shell = UiFinalPlayMode.Shell();
            Assert.IsNotNull(shell);

            var raised = new List<string>();
            NebulaMainMenuScreen screen = Object.FindObjectOfType<NebulaMainMenuScreen>();
            Assert.IsNotNull(screen, "the front door has no menu screen");
            screen.SettingsRequested += () => raised.Add("settings");
            screen.LoadRequested += () => raised.Add("load");
            screen.CreditsRequested += () => raised.Add("credits");

            int depthBefore = shell.StackDepth;

            UiFinalPlayMode.Require(MenuLayer(), "Settings", "AYARLAR is missing").onClick.Invoke();
            UiFinalPlayMode.Require(MenuLayer(), "Load", "KAYIT YÜKLE is missing").onClick.Invoke();
            UiFinalPlayMode.Require(MenuLayer(), "Credits", "KÜNYE is missing").onClick.Invoke();
            yield return null;

            CollectionAssert.AreEqual(new[] { "settings", "load", "credits" }, raised,
                "the three secondary entries do not raise the events they are documented to raise");

            Assert.Greater(shell.StackDepth, depthBefore,
                "all three entries fired their event and nothing in production listened: the shell's stack " +
                "never moved, so AYARLAR, KAYIT YÜKLE and KÜNYE are dead controls. NebulaMainMenuHost " +
                "already owns the shell and the screen and is the seam that has to subscribe.");
        }

        /// <summary>
        /// Step 14 — the slot the player picked is the slot on disk.
        /// <para>
        /// The director used to hold <c>private const int Slot = 1</c> while the
        /// page passed its slot number only into the status message: three slots on
        /// screen, one file on disk. <c>ActiveSlot</c> closed it; this keeps it shut,
        /// from a slot that is not the default one.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Step14_SavingToASlot_WritesThatSlotAndNoOther()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            NebulaContext context = UiFinalPlayMode.Ctx();
            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Session));
            yield return null;

            int chosen = NebulaSaveSlotQuery.FirstSlot + 1;
            Transform card = SlotCard(MenuLayer(), chosen);
            Assert.IsNotNull(card, "the session page has no second slot");

            UiFinalPlayMode.Require(card, "Save", "the second slot has no save control").onClick.Invoke();
            ConfirmIfAsked(context.Presentation.Confirmation, MenuLayer());
            yield return null;

            Assert.IsTrue(NebulaSaveSlotQuery.Read(chosen).Exists,
                $"saving to slot {chosen} wrote no slot-{chosen} file");
            Assert.IsFalse(NebulaSaveSlotQuery.Read(NebulaSaveSlotQuery.FirstSlot).Exists,
                $"saving to slot {chosen} wrote slot {NebulaSaveSlotQuery.FirstSlot} instead");

            menu.Close();
            yield return null;
        }

        /// <summary>
        /// Step 15 — leaving a campaign puts a working main menu back on screen.
        /// <para>
        /// The host is created in <c>SolarSystemBootstrap.Awake</c> and stands itself
        /// down when a session starts; ending the session without rebuilding it left
        /// the player looking at an empty screen with no way forward.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Step15_LeavingACampaign_ReturnsToAMainMenuThatIsOnScreen()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            NebulaContext context = UiFinalPlayMode.Ctx();
            Assert.IsTrue(context.Presentation.ParentMenu.Open(NebulaUiAssetCatalog.Page.Session));

            UiFinalPlayMode.Require(MenuLayer(), "MainMenu",
                "the session page offers no way back to the main menu").onClick.Invoke();
            Assert.IsTrue(ConfirmIfAsked(context.Presentation.Confirmation, MenuLayer()),
                "leaving a campaign discards unsaved progress and did not ask");

            yield return UiFinalPlayMode.Until(() => !UiFinalPlayMode.Boot().SessionActive,
                "the campaign to close");
            yield return UiFinalPlayMode.Until(() => UiFinalPlayMode.Host() != null,
                "a main menu to be on screen in the closed campaign's place");
            yield return UiFinalPlayMode.SettleToOneShell();

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "the session's shell survived alongside the menu's");
            Assert.IsTrue(UiFinalPlayMode.Host().MenuVisible,
                "the campaign closed onto no front door at all");

            // The front door is a screen like any other, so it holds the clock and
            // the modal gate exactly as it does at a cold boot. Asserting the clock
            // was "running again" here was asserting the opposite of the design.
            //
            // What matters is WHO holds them. A gate still owned by the campaign's
            // shell — an object that no longer exists — blocks every future modal
            // for the rest of the process with the game stuck at zero, and no
            // screenshot of this moment could tell the two apart.
            Assert.IsNotNull(ExclusiveModal.Owner,
                "the front door is on screen and nothing owns the modal gate");
            Assert.AreSame(UiFinalPlayMode.Shell(), ExclusiveModal.Owner,
                "the modal gate is held by a shell that is not the one on screen");

            // And the pause it holds is not stranded: starting a campaign from here
            // hands the clock back. This is the half that a check of the moment
            // itself can never prove, and the half a player actually feels.
            yield return UiFinalPlayMode.BeginSession();

            Assert.AreNotEqual(0f, Time.timeScale,
                "the front door kept the clock after handing over to a campaign");
            Assert.IsFalse(InputGate.Suppressed,
                "the front door kept the input gate after handing over to a campaign");
            Assert.IsNull(ExclusiveModal.Owner,
                "the front door kept the modal gate after handing over to a campaign");
        }

        /// <summary>
        /// Step 16 — Continue restores the save it advertises.
        /// <para>
        /// <c>ContinueFromSlot</c> records <c>PendingLoadSlot</c>; for a long time
        /// nothing read it, so DEVAM ET booted a fresh universe while the card beside
        /// it described the campaign it was not loading — saved at level 18, resumed
        /// at level 1. The launcher now applies the slot and runs the load after
        /// composition, and this is what keeps that true.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Step16_ContinueRestoresTheSavedCampaign_NotANewOne()
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            UiFinalPlayMode.Require(MenuLayer(), "New", "the front door has no YENİ SEFER control")
                .onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            ProgressionService.Award("test.e2e-continue", 9000);
            int savedLevel = ProgressionService.State.Level;
            Assert.Greater(savedLevel, 1, "the campaign never advanced, so a restore proves nothing");

            int chosen = NebulaSaveSlotQuery.FirstSlot + 2;
            ExtractionDirector director = UiFinalPlayMode.Ctx().ExtractionDirector;
            director.ActiveSlot = chosen;
            director.SaveNow("E2E CONTINUE");
            yield return null;

            SaveSlotSummary written = NebulaSaveSlotQuery.MostRecent();
            Assert.IsNotNull(written, "nothing was written to continue from");
            Assert.AreEqual(chosen, written.Slot, "the save landed on a slot nobody chose");
            Assert.IsTrue(written.CanContinue);

            yield return UiFinalPlayMode.EndSessionAndSettle();

            // The launcher is read back from the menu that is on screen NOW. Ending
            // a campaign builds a fresh host with a fresh launcher, so the one
            // captured at boot is not the object the Continue the player clicks acts
            // through -- asserting on it would be asserting on a bystander.
            NebulaSessionLauncher launcher = UiFinalPlayMode.Host().Launcher;
            Assert.IsNotNull(launcher, "the main menu came back without a launcher behind it");

            // Through the front door's own control, and with the progression state
            // wiped first so a level that survives is a level the load restored and
            // not one left in a static from the previous session.
            ProgressionService.Reset();
            Button continueButton = UiFinalPlayMode.Require(MenuLayer(), "Continue",
                "the front door has no Continue control");
            Assert.IsTrue(continueButton.interactable, "Continue is dead with a playable save present");
            continueButton.onClick.Invoke();
            yield return UiFinalPlayMode.SettleAfterSessionStart();

            Assert.AreEqual(chosen, launcher.LoadedSlot,
                "Continue started a session without restoring the slot it advertised");
            Assert.AreEqual(savedLevel, ProgressionService.State.Level,
                "Continue started a brand new campaign instead of restoring the save it advertised");
        }
    }
}
