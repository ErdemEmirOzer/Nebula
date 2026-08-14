using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-02: the main menu's promise. Continue is the strongest action, so the
    /// thing worth testing hardest is that it never offers something it cannot do.
    /// </summary>
    public class NebulaMainMenuTests
    {
        private NebulaUiShell shell;
        private NebulaMainMenuScreen menu;
        private FakeLauncher launcher;
        private float enteredTimeScale;

        private sealed class FakeLauncher : INebulaSessionLauncher
        {
            public int NewCampaigns;
            public readonly List<int> ContinuedSlots = new List<int>();
            public bool Quit;

            public int ReturnsToMenu;

            public void StartNewCampaign() => NewCampaigns++;
            public void ContinueFromSlot(int slot) => ContinuedSlots.Add(slot);
            public void QuitGame() => Quit = true;
            public void ReturnToMainMenu() => ReturnsToMenu++;
            public int Restarts;
            public void RestartCampaign() => Restarts++;
        }

        private string saveRoot;
        private string savedSaveDirectory;

        [SetUp]
        public void Setup()
        {
            // Redirect the save folder before anything clears a slot.
            //
            // ClearSlots() deletes SaveService.SlotFile(n). With no override that
            // resolves through Application.persistentDataPath, which in the Editor is
            // now a real folder holding a real campaign -- so this fixture was deleting
            // the operator's saves on every run. It went unnoticed while the Editor's
            // persistent folder was an empty scratch identity; VS-017A.2 gave it real
            // migrated content, and a full-suite run destroyed slot 1.
            savedSaveDirectory = SaveService.DirectoryOverride;
            saveRoot = Path.Combine("/private/tmp", "nebula-main-menu-tests",
                                    System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            SaveService.DirectoryOverride = saveRoot;

            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
            ClearSlots();
            shell = NebulaUiShell.Create();
            launcher = new FakeLauncher();
        }

        [TearDown]
        public void Teardown()
        {
            if (menu != null) Object.DestroyImmediate(menu.gameObject);
            if (shell != null) Object.DestroyImmediate(shell.gameObject);
            menu = null;
            shell = null;
            ClearSlots();
            ExclusiveModal.ResetForSession();
            // Never restore a zero: in edit mode Time.timeScale persists into
            // ProjectSettings/TimeManager.asset, and a committed 0 freezes the
            // project for whoever opens it next.
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;

            SaveService.DirectoryOverride = savedSaveDirectory;
            try { if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true); }
            catch { /* a leftover temp folder is not worth failing a test over */ }
        }

        private static void ClearSlots()
        {
            for (int slot = NebulaSaveSlotQuery.FirstSlot;
                 slot < NebulaSaveSlotQuery.FirstSlot + NebulaSaveSlotQuery.SlotCount; slot++)
            {
                string path = SaveService.SlotFile(slot);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void WriteSlot(int slot, int schema, string savedAtUtc, long totalXp = 0)
        {
            var payload = new SavePayload
            {
                ship = new ShipDto { systemStar = "VEGA-7" },
                navigation = new NavigationDto(),
                totalXp = totalXp,
            };
            var envelope = new SaveEnvelope
            {
                schemaVersion = schema,
                campaignId = "campaign-test",
                revision = 1,
                savedAtUtc = savedAtUtc,
                payloadJson = JsonUtility.ToJson(payload),
            };
            string path = SaveService.SlotFile(slot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(envelope));
        }

        private void BuildMenu() => menu = NebulaMainMenuScreen.Create(shell, launcher);

        // ---- Continue availability ------------------------------------------

        // ---- the host, which is what makes the menu reachable at all -----------

        /// <summary>
        /// The menu had no owner. PresentationInstaller builds the rest of the
        /// interface, and it only runs once a session is composed, so a menu-first
        /// launch had nowhere to live -- DeferToMainMenu documented that flow and
        /// on its own produced a black screen. The host carries its own shell.
        /// </summary>
        [Test]
        public void TheHostBuildsItsOwnShell_SoTheMenuExistsBeforeAnySession()
        {
            // The fixture already stands a shell up, so what matters is the delta:
            // the host brings exactly one of its own, and takes it away again.
            int before = Object.FindObjectsOfType<NebulaUiShell>(true).Length;

            NebulaMainMenuHost host = NebulaMainMenuHost.Create(null);
            try
            {
                Assert.IsTrue(host.MenuVisible, "a menu-first launch shows a menu");
                Assert.IsNotNull(host.Launcher, "and it can start a campaign");
                Assert.AreEqual(before + 1, Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                    "the host carries exactly one shell of its own");
            }
            finally { host.Dismiss(); }

            Assert.AreEqual(before, Object.FindObjectsOfType<NebulaUiShell>(true).Length,
                "and leaves none behind, so the session's shell is the only one");
        }

        /// <summary>
        /// Two shells would mean two owners of the modal gate and two pause blocks,
        /// so the host stands down rather than coexisting with the session's.
        /// </summary>
        [Test]
        public void DismissingTheHost_LeavesNoShellAndNoModalOwner()
        {
            NebulaMainMenuHost host = NebulaMainMenuHost.Create(null);
            host.Dismiss();

            Assert.IsFalse(host.MenuVisible);
            Assert.IsNull(ExclusiveModal.Owner, "the gate is free for the session's shell");
        }

        [Test]
        public void DismissingTwice_IsANoOpRatherThanAFailure()
        {
            NebulaMainMenuHost host = NebulaMainMenuHost.Create(null);
            host.Dismiss();

            Assert.DoesNotThrow(() => host.Dismiss(),
                "Update and an explicit teardown can both reach it");
        }

        [Test]
        public void WithNoSave_ContinueIsUnavailable_AndSaysWhy()
        {
            BuildMenu();

            Assert.AreEqual(-1, menu.ResumeSlot);
            Assert.IsNotNull(menu.ContinueBlockedReason,
                "A disabled Continue with no explanation is exactly the dead control this redesign removes.");
            StringAssert.Contains("KAYIT", menu.ContinueBlockedReason.ToUpperInvariant());
        }

        [Test]
        public void WithASave_ContinueTargetsTheMostRecentSlot()
        {
            WriteSlot(1, SaveService.CurrentSchema, "2026-08-01T10:00:00Z");
            WriteSlot(3, SaveService.CurrentSchema, "2026-08-04T18:00:00Z");
            BuildMenu();

            Assert.AreEqual(3, menu.ResumeSlot);
            Assert.IsNull(menu.ContinueBlockedReason);
        }

        [Test]
        public void ASaveFromANewerBuild_IsExplained_NotReportedAsMissing()
        {
            // Telling a player "no save" when their save is simply from a newer
            // build is the difference between a clear message and a lost campaign.
            WriteSlot(2, SaveService.CurrentSchema + 3, "2026-08-04T18:00:00Z");
            BuildMenu();

            Assert.AreEqual(-1, menu.ResumeSlot);
            StringAssert.Contains((SaveService.CurrentSchema + 3).ToString(), menu.ContinueBlockedReason);
        }

        [Test]
        public void Refresh_PicksUpASaveWrittenAfterTheMenuWasBuilt()
        {
            BuildMenu();
            Assert.AreEqual(-1, menu.ResumeSlot);

            WriteSlot(1, SaveService.CurrentSchema, "2026-08-04T18:00:00Z");
            menu.Refresh();

            Assert.AreEqual(1, menu.ResumeSlot,
                "Returning to the menu must re-read the slots, not show a cached answer.");
        }

        [Test]
        public void BeingRevealedAgain_RefreshesTheSummary()
        {
            BuildMenu();
            WriteSlot(1, SaveService.CurrentSchema, "2026-08-04T18:00:00Z");

            menu.OnRevealed();

            Assert.AreEqual(1, menu.ResumeSlot);
        }

        // ---- the menu must not create gameplay state ------------------------

        [Test]
        public void BuildingTheMenu_StartsNoSession()
        {
            WriteSlot(1, SaveService.CurrentSchema, "2026-08-04T18:00:00Z");
            BuildMenu();

            Assert.AreEqual(0, launcher.NewCampaigns);
            Assert.IsEmpty(launcher.ContinuedSlots,
                "Drawing the main menu must never launch a campaign.");
        }

        [Test]
        public void BuildingTheMenu_DoesNotAdoptACampaignIdentity()
        {
            WriteSlot(1, SaveService.CurrentSchema, "2026-08-04T18:00:00Z");
            string before = CampaignIdentity.Current;

            BuildMenu();

            Assert.AreEqual(before, CampaignIdentity.Current,
                "The menu adopted a campaign identity just by listing saves; " +
                "New Campaign would then inherit it.");
        }

        [Test]
        public void MenuLivesOnTheMenuLayer()
        {
            BuildMenu();
            Assert.IsTrue(menu.transform.IsChildOf(shell.Root(UiLayer.Menu)),
                "A session screen on the HUD layer would sort under the HUD.");
        }

        // ---- relative time ---------------------------------------------------

        [Test]
        public void SaveAge_ReadsAsRelativeTime_NotATimestamp()
        {
            string minutes = NebulaMainMenuScreen.RelativeTime(System.DateTime.UtcNow.AddMinutes(-4));
            StringAssert.Contains("DAKİKA", minutes);

            string hours = NebulaMainMenuScreen.RelativeTime(System.DateTime.UtcNow.AddHours(-3));
            StringAssert.Contains("SAAT", hours);

            string days = NebulaMainMenuScreen.RelativeTime(System.DateTime.UtcNow.AddDays(-2));
            StringAssert.Contains("GÜN", days);
        }

        [Test]
        public void AFutureTimestamp_DoesNotProduceNegativeAge()
        {
            // Clock skew between machines is real; "-3 dakika önce" is not.
            string text = NebulaMainMenuScreen.RelativeTime(System.DateTime.UtcNow.AddMinutes(30));
            Assert.IsFalse(text.Contains("-"), $"Negative age leaked into the card: '{text}'");
        }
    }
}
