using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Controls;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// One press, one command — and a rebind that is still there tomorrow.
    /// <para>
    /// Two defects, one fixture, because they are the same seam. <c>U</c> was read by
    /// <see cref="NebulaParentMenuScreen"/> and by <see cref="ShipQuickAssetPackage"/>
    /// in their own <c>Update</c>s, so pressing it during a package cancelled the
    /// package <b>and</b> opened the command menu; and the nine reassignable combat
    /// keys were held only in static properties, so every one of them was gone the next
    /// time the game started.
    /// </para>
    /// <para>
    /// The physical press cannot be simulated: legacy <c>Input</c> is not drivable in
    /// batch mode. What is driven instead is the router's own key sample —
    /// <see cref="NebulaInputRouter.Source"/> — which sits <i>below</i> every rule that
    /// matters. Both components' real <c>Update</c> methods run, the real arbitration
    /// decides, and the real <c>Cancel</c> and <c>Open</c> are what happen or do not.
    /// Nothing here reaches past a component to assert an outcome it arranged itself.
    /// </para>
    /// </summary>
    public class UiFinalInputRoutingTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;
        private string sandbox;

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();

            // The operator's own key layout is not this run's to overwrite.
            sandbox = Path.Combine(Path.GetTempPath(), "nebula-input-routing-" + Path.GetRandomFileName());
            Directory.CreateDirectory(sandbox);
            NebulaInputBindingStore.DirectoryOverride = sandbox;
            NebulaInputBindingStore.WritesEnabled = true;
        }

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            NebulaInputRouter.ResetSourceToKeyboard();
            NebulaInputRouter.ResetContexts();

            foreach (NebulaUiShell shell in Object.FindObjectsOfType<NebulaUiShell>(true))
                if (shell != null) shell.Shutdown();

            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            CombatBindings.ResetToDefaults();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);

            NebulaInputBindingStore.DirectoryOverride = null;
            NebulaInputBindingStore.WritesEnabled = true;
            try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); } catch { /* best effort */ }
        }

        // =====================================================================
        // Driving the one key
        // =====================================================================

        /// <summary>Holds a single key down for exactly the frames the caller runs.</summary>
        private static void PressAndHold(KeyCode key) => NebulaInputRouter.Source = k => k == key;

        private static void ReleaseEverything() => NebulaInputRouter.Source = _ => false;

        private static NebulaParentMenuScreen Menu() =>
            UiFinalPlayMode.Ctx()?.Presentation?.ParentMenu;

        /// <summary>
        /// A Quick Asset Package with a live transaction and no world behind it.
        /// <para>
        /// Built through <c>RestoreTransaction</c>, the component's own public restore
        /// path, with nothing sent — so the transaction is genuinely active and every
        /// send is a no-op for want of a target planet. That is the exact state the
        /// collision needed: cancel does something, and the menu key is the same key.
        /// </para>
        /// </summary>
        private static ShipQuickAssetPackage LivePackage(out System.Func<int> saveRequests)
        {
            // A real planet from the running session, because the restore path resolves
            // the world id through the planet manifest and an invented one is refused
            // outright — correctly, and it took a red test to notice.
            PlanetIdentity planet = Object.FindObjectOfType<PlanetIdentity>();
            Assert.IsNotNull(planet, "the running session streamed no planet to hang a package on");

            // The ship's own package component, not a fresh GameObject carrying a second
            // one. ShipQuickAssetPackage reads its cargo off a ShipLogistics on the same
            // object, so a bare GameObject has no cargo at all — and since Part 3 a
            // restore re-reserves what the package still owes, which a null cargo can
            // never satisfy. The old fixture therefore produced a package that was
            // refused before the test began, and the refusal was reported as the
            // fixture's own failure. Using the composed ship also stops the test from
            // standing up a second package authority beside the real one.
            ShipQuickAssetPackage package = Object.FindObjectOfType<ShipQuickAssetPackage>();
            Assert.IsNotNull(package, "the running session composed no quick asset package");

            LogisticsCargo hold = package.GetComponent<ShipLogistics>()?.Cargo;
            Assert.IsNotNull(hold, "the ship carries no cargo hold to reserve against");

            int count = 0;
            package.RequestSave = () => count++;
            saveRequests = () => count;

            // Stocked the way the real load path stocks it: ExtractionDirector restores
            // cargo before it restores the package, precisely so the re-reservation can
            // succeed. A test that skips that step is testing a state the game never
            // reaches.
            foreach (string itemId in new[] { ProcessingCatalog.BasicPartId, ProcessingCatalog.MaterialIds[0] })
                hold.LoadItem(itemId, 500f);

            package.RestoreTransaction(new QuickPackageDto
            {
                active = true,
                transactionId = "test-transaction",
                planetWorldId = planet.WorldId,
                commandMaterialId = ProcessingCatalog.MaterialIds[0],
                laserMaterialId = ProcessingCatalog.MaterialIds[0],
                drillSent = false, commandSent = false, laserSent = false,
                elapsedSeconds = 0f,
            });

            Assert.IsTrue(package.CancelAvailable, "the fixture failed to produce a cancellable package");
            return package;
        }

        // =====================================================================
        // The U collision
        // =====================================================================

        [UnityTest]
        public IEnumerator UPressWithNoPackageInFlight_OpensTheCommandMenu()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            NebulaParentMenuScreen menu = Menu();
            Assert.IsNotNull(menu, "the session composed no command menu");
            Assert.IsFalse(menu.IsOpen);

            PressAndHold(NebulaParentMenuScreen.ProgressionKey);
            yield return null;
            ReleaseEverything();

            Assert.IsTrue(menu.IsOpen, "U no longer opens the command menu");
            Assert.AreEqual(NebulaUiAssetCatalog.Page.Progression, menu.CurrentPage);
            menu.Close();
        }

        [UnityTest]
        public IEnumerator UPressWithAPackageInFlight_CancelsThePackageAndLeavesTheMenuClosed()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            NebulaParentMenuScreen menu = Menu();
            Assert.IsFalse(menu.IsOpen);

            ShipQuickAssetPackage package = LivePackage(out System.Func<int> saves);
            yield return null; // let OnEnable register the context before the press

            PressAndHold(ShipQuickAssetPackage.CancelKey);
            yield return null;
            ReleaseEverything();

            Assert.IsTrue(package.ActiveTransaction.Cancelled, "the package was not cancelled");
            Assert.IsFalse(menu.IsOpen,
                "the same press opened the command menu as well — the double-fire is back");
            Assert.AreEqual(1, saves(), "cancel ran more than once for one press");

            // Nothing to destroy: the package under test is the ship's own component,
            // and destroying its GameObject would take the ShipController with it —
            // which is exactly how this fixture earned a MissingReferenceException.
            // TearDown ends the session, and the ship and its package go with it.
        }

        /// <summary>
        /// One press, one command, and no second command on the frames after it.
        /// <para>
        /// The press is one frame long, because that is what <c>GetKeyDown</c> is —
        /// an earlier draft held the sample true for five frames, which no keyboard
        /// ever produces, and the menu correctly opened on frame two once the cancel
        /// had ended its own context. What is worth asserting is the real shape: the
        /// key goes down once, and nothing else happens as the frames run on.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator OneUPress_ProducesOneCommandAndNoAftershock()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            NebulaParentMenuScreen menu = Menu();

            ShipQuickAssetPackage package = LivePackage(out System.Func<int> saves);
            yield return null;

            PressAndHold(ShipQuickAssetPackage.CancelKey);
            yield return null;              // the single frame the key is down
            ReleaseEverything();
            yield return UiFinalPlayMode.Frames(5);

            Assert.AreEqual(1, saves(), "cancel ran more than once for one press");
            Assert.IsTrue(package.ActiveTransaction.Cancelled);
            Assert.IsFalse(menu.IsOpen, "the menu opened on a frame after the press");

            // Nothing to destroy: the package under test is the ship's own component,
            // and destroying its GameObject would take the ShipController with it —
            // which is exactly how this fixture earned a MissingReferenceException.
            // TearDown ends the session, and the ship and its package go with it.
        }

        /// <summary>
        /// The other half of the contract: the contextual owner gives the key back.
        /// A cancel that kept U forever would be the same defect pointing the other
        /// way — the command menu unreachable for the rest of the session.
        /// </summary>
        [UnityTest]
        public IEnumerator OnceThePackageIsOver_TheMenuHasUBack()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            NebulaParentMenuScreen menu = Menu();

            ShipQuickAssetPackage package = LivePackage(out _);
            yield return null;

            PressAndHold(ShipQuickAssetPackage.CancelKey);
            yield return null;
            ReleaseEverything();
            Assert.IsTrue(package.ActiveTransaction.Cancelled);
            Assert.IsFalse(menu.IsOpen);

            yield return null;
            PressAndHold(NebulaParentMenuScreen.ProgressionKey);
            yield return null;
            ReleaseEverything();

            Assert.IsTrue(menu.IsOpen, "the finished package is still holding U");
            menu.Close();
        }

        [UnityTest]
        public IEnumerator WithBothClaimantsLive_TheRouterNamesExactlyOneOwner()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            ShipQuickAssetPackage package = LivePackage(out _);
            yield return null;

            InputActionDefinition owner = NebulaInputRouter.OwnerOf(ShipQuickAssetPackage.CancelKey);
            Assert.IsNotNull(owner);
            Assert.AreEqual(NebulaInputActions.QuickPackageCancel, owner.Id,
                "the live contextual claim did not outrank the global one");

            // The claim is retired the way the game retires it — the package is
            // cancelled — rather than by destroying the object underneath it. The
            // package under test is the ship's own component now, so destroying its
            // GameObject would take the ShipController with it; and cancelling is in any
            // case the state this assertion is about, since it is the end of the
            // transaction and not the end of the component that frees the key.
            package.Cancel();
            yield return null;

            Assert.IsFalse(package.CancelAvailable,
                "baseline: the package still has a live transaction, so its claim is correct to stand");
            Assert.AreEqual(NebulaInputActions.MenuProgression,
                NebulaInputRouter.OwnerOf(NebulaParentMenuScreen.ProgressionKey)?.Id,
                "with the package's transaction over, the global intent must own the key again");
        }

        // =====================================================================
        // Menu navigation
        // =====================================================================

        /// <summary>
        /// Enter, Escape and the arrows have to keep working. Their physical presses
        /// are read with legacy <c>Input</c> and cannot be driven here, so what is
        /// asserted is the part a test can actually prove: the menu's own back and page
        /// walk — the methods those key handlers call — and that nothing may take those
        /// keys away from the interface. The physical press is on the manual smoke list.
        /// </summary>
        [UnityTest]
        public IEnumerator BackAndPageWalkStillWork_AndNoGameplayActionMayClaimTheirKeys()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            NebulaParentMenuScreen menu = Menu();

            menu.Open(NebulaUiAssetCatalog.Page.Progression);
            yield return null;
            Assert.IsTrue(menu.IsOpen);

            NebulaUiAssetCatalog.Page? first = menu.CurrentPage;
            menu.StepPage(1);
            yield return null;
            Assert.AreNotEqual(first, menu.CurrentPage, "the right arrow's own handler moved nothing");

            menu.StepPage(-1);
            yield return null;
            Assert.AreEqual(first, menu.CurrentPage, "the left arrow's own handler did not come back");

            menu.Close();
            yield return null;
            Assert.IsFalse(menu.IsOpen, "Escape's own handler did not close the menu");

            // Enter and Escape are uGUI's Submit and Cancel; nothing in combat may hold
            // them. The arrows are the menu's alone and are free, with a note.
            Assert.IsFalse(CombatBindings.TrySetBinding(CombatAction.Repair, KeyCode.Return));
            Assert.IsFalse(CombatBindings.TrySetBinding(CombatAction.Repair, KeyCode.Escape));
            // No note, and that is the design rather than a gap. P1-UI-005 moved the
            // page stepper to PageUp/PageDown precisely so the arrows belong to uGUI's
            // Horizontal/Vertical navigation alone; with no second Nebula owner there is
            // nothing to warn a rebinding player about, and inventing a note would be
            // warning without evidence. NebulaInputActionCatalogTests
            // .ScreenOnlyKeys_AreContextuallyNotedRatherThanBlocked holds the same line
            // from the other side, and the two used to contradict each other.
            Assert.IsNull(NebulaControlBindings.ContextNote(KeyCode.LeftArrow),
                "the arrows regained a Nebula owner — the page stepper must stay on PageUp/PageDown");
        }

        // =====================================================================
        // The Tab decision
        // =====================================================================

        [UnityTest]
        public IEnumerator TabOwnsNothingAndIsOfferedToThePlayer()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            foreach (InputActionDefinition action in NebulaInputActions.All)
                Assert.AreNotEqual(KeyCode.Tab, action.Key, $"{action.Id} claims Tab");

            Assert.AreEqual(ControlRebindResult.Accepted,
                NebulaControlBindings.Check(CombatAction.Repair, KeyCode.Tab),
                "Tab is still refused although nothing owns it");

            // And the HUD's one authority is untouched: a screen still hides it and
            // closing the screen still brings it back, with no second toggle anywhere.
            NebulaParentMenuScreen menu = Menu();
            Canvas hud = UiFinalPlayMode.Shell().CanvasFor(UiLayer.HudStatic);
            Assert.IsNotNull(hud);

            bool before = hud.enabled;
            menu.Open(NebulaUiAssetCatalog.Page.Progression);
            yield return null;
            Assert.IsFalse(hud.enabled, "a screen no longer hides the HUD");

            menu.Close();
            yield return null;
            Assert.AreEqual(before, hud.enabled, "closing the screen did not restore the HUD");
        }

        // =====================================================================
        // Persistence
        // =====================================================================

        /// <summary>
        /// A restart, as closely as a PlayMode test can stand in for one: the layout is
        /// written, the in-memory service is wiped back to shipped defaults exactly as a
        /// fresh process would start, and the store's own
        /// <c>[RuntimeInitializeOnLoadMethod]</c> entry point is what puts it back.
        /// Nothing but the process boundary is faked.
        /// </summary>
        [UnityTest]
        public IEnumerator ARebindSurvivesTheRestartPath()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            Assert.AreEqual(ControlRebindResult.Accepted,
                NebulaControlBindings.TryRebind(CombatAction.CombatBoost, KeyCode.Alpha9));

            CombatBindings.ResetToDefaults();               // the process forgetting
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);

            NebulaInputBindingStore.LoadOnStartup();        // the process coming back
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey,
                "the rebind did not survive a restart");
        }

        [UnityTest]
        public IEnumerator ARebindSurvivesACampaignSaveAndLoad()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            UiFinalPlayMode.DeleteEverySlot();

            Assert.AreEqual(ControlRebindResult.Accepted,
                NebulaControlBindings.TryRebind(CombatAction.CombatBoost, KeyCode.Alpha9));

            ExtractionDirector director = Object.FindObjectOfType<ExtractionDirector>();
            Assert.IsNotNull(director, "the session has no save director to drive");

            director.SaveNow("test save");
            yield return null;
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey, "saving moved a key binding");

            director.LoadNow();
            yield return null;
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey,
                "loading a campaign overwrote the player's key layout");
        }

        [UnityTest]
        public IEnumerator ANewCampaignKeepsThePlayersLayout()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            Assert.AreEqual(ControlRebindResult.Accepted,
                NebulaControlBindings.TryRebind(CombatAction.CombatBoost, KeyCode.Alpha9));

            yield return UiFinalPlayMode.EndSessionAndSettle();
            yield return UiFinalPlayMode.BeginSession();

            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey,
                "starting a new campaign reset the player's keys");
        }

        [UnityTest]
        public IEnumerator RestoreDefaultsReturnsTheShippedKeyAndForgetsTheFile()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            NebulaControlBindings.TryRebind(CombatAction.CombatBoost, KeyCode.Alpha9);
            Assert.IsTrue(NebulaInputBindingStore.Exists());

            NebulaControlBindings.ResetToDefaults();

            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
            Assert.IsFalse(NebulaInputBindingStore.Exists());

            NebulaInputBindingStore.LoadOnStartup();
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey,
                "the forgotten layout came back on the next start");
        }

        // =====================================================================
        // Conflict feedback and debug bindings
        // =====================================================================

        [UnityTest]
        public IEnumerator ADuplicateBindingIsRefusedAndTheScreenSaysWhy()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            KeyCode taken = CombatBindings.CycleTargetKey;
            Assert.AreEqual(ControlRebindResult.ConflictsWithAction,
                NebulaControlBindings.TryRebind(CombatAction.CombatBoost, taken));
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey, "a refused rebind moved the key");

            string message = NebulaControlBindings.ResultText(
                ControlRebindResult.ConflictsWithAction, CombatAction.CombatBoost, taken);
            StringAssert.Contains("ZATEN", message, "the refusal does not tell the player the key is taken");
            StringAssert.Contains(
                Nebula.UI.Design.NebulaText.Upper(NebulaControlBindings.ActionName(CombatAction.CycleTarget)),
                message, "the refusal does not name who has the key");
        }

        /// <summary>
        /// The standing warning, proved by forcing the state it warns about.
        /// <c>CombatBindings</c> exposes every key as a settable property, so a caller
        /// that skips the validator can create a duplicate — and that is exactly the
        /// case a warning nobody can otherwise reach has to catch.
        /// </summary>
        [UnityTest]
        public IEnumerator TwoActionsOnOneKey_RaiseAVisibleWarning()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            Assert.IsEmpty(NebulaControlBindings.ConflictWarnings(),
                "the shipped layout already warns about something");

            CombatBindings.CombatBoostKey = CombatBindings.CycleTargetKey;   // past the validator

            IReadOnlyList<string> warnings = NebulaControlBindings.ConflictWarnings();
            Assert.IsNotEmpty(warnings, "two actions share a key and the screen says nothing");
            StringAssert.Contains(
                Nebula.UI.Design.NebulaText.Upper(NebulaControlBindings.ActionName(CombatAction.CombatBoost)),
                string.Join(" ", warnings));

            CombatBindings.ResetToDefaults();
            Assert.IsEmpty(NebulaControlBindings.ConflictWarnings());
        }

        [UnityTest]
        public IEnumerator NoDebugOnlyBindingIsOfferedOrStatedInARelease()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            InputActionDefinition[] release =
                NebulaInputActions.PlayerVisible(includeDebugOnly: false).ToArray();

            Assert.IsNotEmpty(release);
            foreach (InputActionDefinition action in release)
                Assert.IsFalse(action.DebugOnly, $"{action.Id} would be shown in a release build");

            // And none of them is rebindable, so a debug key can never reach the store.
            foreach (InputActionDefinition action in NebulaInputActions.All)
                if (action.DebugOnly)
                    Assert.IsFalse(action.Persisted, $"{action.Id} would be written to the settings file");
        }
    }
}
