using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-02/03: the session page. Until it existed the only way to save was an
    /// undocumented Ctrl+comma chord, which meant a player who had not read the
    /// source could not save at all.
    /// </summary>
    public class NebulaSessionPageTests
    {
        private NebulaUiShell shell;
        private NebulaConfirmationDialog dialog;
        private float enteredTimeScale;

        private string saveRoot;
        private string savedSaveDirectory;

        [SetUp]
        public void Setup()
        {
            // See NebulaMainMenuTests.Setup: ClearSlots() resolves through
            // Application.persistentDataPath without an override, which in the Editor is
            // now a real folder holding a real campaign. This fixture was deleting the
            // operator's saves on every run.
            savedSaveDirectory = SaveService.DirectoryOverride;
            saveRoot = System.IO.Path.Combine("/private/tmp", "nebula-session-page-tests",
                                              System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            SaveService.DirectoryOverride = saveRoot;

            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
            ClearSlots();
            shell = NebulaUiShell.Create();
            dialog = NebulaConfirmationDialog.Create(shell);
        }

        [TearDown]
        public void Teardown()
        {
            if (dialog != null) Object.DestroyImmediate(dialog.gameObject);
            if (shell != null)
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
            }
            ClearSlots();
            ExclusiveModal.ResetForSession();
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

        private static void WriteSlot(int slot, int schema)
        {
            var payload = new SavePayload { ship = new ShipDto(), navigation = new NavigationDto() };
            var envelope = new SaveEnvelope
            {
                schemaVersion = schema,
                campaignId = "campaign-test",
                revision = 1,
                savedAtUtc = "2026-08-05T10:00:00Z",
                payloadJson = JsonUtility.ToJson(payload),
            };
            string path = SaveService.SlotFile(slot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(envelope));
        }

        private RectTransform Host()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            go.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private NebulaSessionPage BuildPage()
        {
            var page = new NebulaSessionPage(null, dialog);
            page.Build(Host());
            page.Refresh();
            return page;
        }

        [Test]
        public void EverySlotGetsARow()
        {
            Assert.AreEqual(NebulaSaveSlotQuery.SlotCount, BuildPage().SlotCount,
                "A slot with no row is a slot the player cannot use.");
        }

        [Test]
        public void BuildsAndRefreshesWithoutASession()
        {
            Assert.DoesNotThrow(() => BuildPage());
        }

        [Test]
        public void AnEmptySlotSaysSo()
        {
            SaveSlotSummary empty = NebulaSaveSlotQuery.Read(1);
            Assert.AreEqual("BOŞ", NebulaSessionPage.Describe(empty));
        }

        [Test]
        public void AFilledSlotDescribesTheCampaign()
        {
            WriteSlot(1, SaveService.CurrentSchema);
            string text = NebulaSessionPage.Describe(NebulaSaveSlotQuery.Read(1));

            StringAssert.Contains("SEVİYE", text);
            StringAssert.Contains("SİSTEM", text);
        }

        [Test]
        public void AnIncompatibleSlotExplainsItselfRatherThanLookingEmpty()
        {
            // Reporting a newer-schema save as "empty" would tell the player their
            // campaign is gone when it is merely from a newer build.
            WriteSlot(2, SaveService.CurrentSchema + 2);
            string text = NebulaSessionPage.Describe(NebulaSaveSlotQuery.Read(2));

            Assert.AreNotEqual("BOŞ", text);
            StringAssert.Contains((SaveService.CurrentSchema + 2).ToString(), text);
        }

        [Test]
        public void LoadingAnUnusableSlotIsRefusedWithItsReason()
        {
            WriteSlot(3, SaveService.CurrentSchema + 5);
            NebulaSessionPage page = BuildPage();

            // The load button must be disabled with the schema reason, not merely
            // dead, and nothing may be confirmed.
            Assert.AreEqual(0, dialog.ConfirmedCount);
            Assert.IsFalse(dialog.IsOpen);
            StringAssert.Contains((SaveService.CurrentSchema + 5).ToString(),
                NebulaSessionPage.Describe(NebulaSaveSlotQuery.Read(3)));
        }

        [Test]
        public void RefreshingDoesNotWriteAnySaveFile()
        {
            WriteSlot(1, SaveService.CurrentSchema);
            string path = SaveService.SlotFile(1);
            System.DateTime before = File.GetLastWriteTimeUtc(path);

            NebulaSessionPage page = BuildPage();
            for (int i = 0; i < 20; i++) page.Refresh();

            Assert.AreEqual(before, File.GetLastWriteTimeUtc(path),
                "Merely looking at the session page rewrote a save.");
        }

        [Test]
        public void SessionActionsRaiseNoAttentionBadge()
        {
            Assert.AreEqual(0, BuildPage().AttentionCount);
        }
    }
}
