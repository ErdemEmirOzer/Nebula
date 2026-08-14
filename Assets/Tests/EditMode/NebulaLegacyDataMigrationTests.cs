using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Platform;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The one-shot import that runs when the app's identity changes.
    /// <para>
    /// Renaming <c>companyName</c>/<c>productName</c> moves
    /// <c>Application.persistentDataPath</c>, so every one of these cases is a way a
    /// player could lose a campaign to a rename. They are written against injected
    /// temp roots under <c>/private/tmp</c> and never against the machine's real
    /// Application Support folder — <see cref="TheOperatorsRealDataFolderIsNeverTouched"/>
    /// checks that claim rather than asserting it.
    /// </para>
    /// </summary>
    public class NebulaLegacyDataMigrationTests
    {
        private const string TestRootName = "nebula-legacy-migration-tests";

        private string testRoot;
        private string legacyRoot;
        private string activeRoot;

        private string savedSaveDirectory;
        private string savedBindingDirectory;

        [SetUp]
        public void SetUp()
        {
            // /private/tmp and not Path.GetTempPath(): the brief pins the tests to a
            // named location an operator can inspect afterwards, and /var/folders/... is
            // neither stable nor findable.
            testRoot = Path.Combine("/private/tmp", TestRootName, Guid.NewGuid().ToString("N"));
            legacyRoot = Path.Combine(testRoot, "legacy");
            activeRoot = Path.Combine(testRoot, "active");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(activeRoot);

            savedSaveDirectory = SaveService.DirectoryOverride;
            savedBindingDirectory = NebulaInputBindingStore.DirectoryOverride;
            NebulaLegacyDataMigrator.CommitObserver = null;

            // Pointed inside the temp tree for the whole fixture, so there is never a
            // window in which a SaveService or binding-store call falls through to
            // Application.persistentDataPath and writes on the operator's machine.
            SaveService.DirectoryOverride = ActivePath("saves");
            NebulaInputBindingStore.DirectoryOverride = ActivePath("settings");
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.DirectoryOverride = savedSaveDirectory;
            NebulaInputBindingStore.DirectoryOverride = savedBindingDirectory;
            NebulaLegacyDataMigrator.CommitObserver = null;

            try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); }
            catch (Exception) { /* a leaked temp folder is not worth failing a test over */ }
        }

        // ---- path resolution --------------------------------------------------

        /// <summary>
        /// The macOS layout, checked against the one folder on this machine that was
        /// demonstrably produced by it.
        /// </summary>
        [Test]
        public void TheMacOsRootsFollowTheLayoutTheOldIdentityActuallyProduced()
        {
            Assert.IsTrue(NebulaPersistentPathResolver.TryResolveRoot(PersistentDataPlatform.MacOS,
                "/Users/tester", NebulaAppIdentity.LegacyCompany, NebulaAppIdentity.LegacyProduct,
                out string legacy, out _));
            Assert.AreEqual("/Users/tester/Library/Application Support/DefaultCompany/Nebula", legacy);

            Assert.IsTrue(NebulaPersistentPathResolver.TryResolveRoot(PersistentDataPlatform.MacOS,
                "/Users/tester", NebulaAppIdentity.Company, NebulaAppIdentity.Product,
                out string active, out _));
            Assert.AreEqual("/Users/tester/Library/Application Support/Erdem/NEBULA", active);

            Assert.AreNotEqual(legacy, active,
                "if the rename did not move the folder there would be nothing to migrate");
        }

        /// <summary>
        /// The Windows layout, built with Windows separators even though this test runs
        /// on a Mac — otherwise "the Windows path" would be a POSIX string and the
        /// assertion would prove nothing about Windows.
        /// </summary>
        [Test]
        public void TheWindowsRootsUseTheWindowsLayoutRegardlessOfTheHost()
        {
            Assert.IsTrue(NebulaPersistentPathResolver.TryResolveRoot(PersistentDataPlatform.Windows,
                @"C:\Users\tester", NebulaAppIdentity.LegacyCompany, NebulaAppIdentity.LegacyProduct,
                out string legacy, out _));
            Assert.AreEqual(@"C:\Users\tester\AppData\LocalLow\DefaultCompany\Nebula", legacy);

            Assert.IsTrue(NebulaPersistentPathResolver.TryResolveRoot(PersistentDataPlatform.Windows,
                @"C:\Users\tester", NebulaAppIdentity.Company, NebulaAppIdentity.Product,
                out string active, out _));
            Assert.AreEqual(@"C:\Users\tester\AppData\LocalLow\Erdem\NEBULA", active);
        }

        /// <summary>A platform this project has never shipped a build for gets no invented path.</summary>
        [Test]
        public void AnUnsupportedPlatformClaimsNoRootAtAll()
        {
            Assert.IsFalse(NebulaPersistentPathResolver.TryResolveRoot(PersistentDataPlatform.Unsupported,
                "/Users/tester", "DefaultCompany", "Nebula", out string root, out string problem));
            Assert.IsNull(root);
            Assert.IsNotEmpty(problem);
        }

        /// <summary>An identity that is not one folder name is refused, never quietly repaired.</summary>
        [Test]
        public void AnIdentityThatIsNotOneFolderNameIsRefused()
        {
            foreach (string hostile in new[] { "..", "../..", "Default/Company", @"Default\Company", "", "   " })
                Assert.IsFalse(NebulaPersistentPathResolver.TryResolveRoot(PersistentDataPlatform.MacOS,
                        "/Users/tester", hostile, "Nebula", out _, out _),
                    "accepted a company name that is not a single folder: " + hostile);
        }

        /// <summary>
        /// The containment guard refuses every traversal spelling, which is what stops a
        /// crafted name from writing outside the folder being migrated into.
        /// </summary>
        [Test]
        public void TheContainmentGuardRefusesTraversalSpellings()
        {
            Assert.IsTrue(NebulaPersistentPathResolver.IsWithin(activeRoot, Path.Combine(activeRoot, "nebula", "saves")));
            Assert.IsTrue(NebulaPersistentPathResolver.IsWithin(activeRoot, activeRoot));

            Assert.IsFalse(NebulaPersistentPathResolver.IsWithin(activeRoot,
                Path.Combine(activeRoot, "..", "..", "etc", "passwd")));
            Assert.IsFalse(NebulaPersistentPathResolver.IsWithin(activeRoot,
                Path.Combine(activeRoot, "nebula", "..", "..", "elsewhere")));
            Assert.IsFalse(NebulaPersistentPathResolver.IsWithin(activeRoot, testRoot),
                "the parent of the active root is not inside it");
            Assert.IsFalse(NebulaPersistentPathResolver.IsWithin(activeRoot, activeRoot + "-sibling"),
                "a sibling sharing a name prefix is not inside the root");
        }

        // ---- nothing to do ----------------------------------------------------

        /// <summary>No old folder: recorded once so the next boot does not go looking again.</summary>
        [Test]
        public void NoLegacyFolderIsRecordedRatherThanRediscoveredEveryBoot()
        {
            NebulaLegacyMigrationReport report = Run();

            Assert.AreEqual(LegacyMigrationOutcome.NoLegacyData, report.Outcome, report.note);
            Assert.IsTrue(File.Exists(NebulaLegacyDataMigrator.MarkerPath(activeRoot)), "no marker was written");
            Assert.AreEqual(LegacyMigrationOutcome.AlreadyMigrated, Run().Outcome,
                "a second boot must read the marker rather than walk the filesystem again");
        }

        /// <summary>An identity that did not actually move has nothing to import.</summary>
        [Test]
        public void TheSameRootOnBothSidesImportsNothing()
        {
            SeedLegacySave(1, totalXp: 500);
            var report = NebulaLegacyDataMigrator.Compose(new NebulaPersistentPaths(legacyRoot, legacyRoot));
            Assert.AreEqual(LegacyMigrationOutcome.NoLegacyData, report.Outcome, report.note);
            Assert.IsEmpty(report.imported);
        }

        /// <summary>
        /// The Editor interlock. Every EditMode and PlayMode test runs inside the Editor,
        /// so this is the mechanism that stops one of them from importing into — or out of
        /// — the operator's real Application Support folder.
        /// </summary>
        [Test]
        public void TheEditorRefusesAnyRootOutsideTheTempDirectory()
        {
            string pretendReal = Path.Combine(NebulaPersistentPathResolver.HomeDirectory(),
                "Library", "Application Support", "Erdem", "NEBULA");

            // Captured before, compared after — not asserted absent.
            //
            // A real player legitimately migrates and legitimately leaves a marker here;
            // once anyone runs the shipped build on this machine, "no marker exists" stops
            // being a statement about the interlock and becomes a statement about whether
            // the operator has ever played the game. What the interlock actually promises
            // is that *this call* wrote nothing, so that is what gets measured.
            string marker = NebulaLegacyDataMigrator.MarkerPath(pretendReal);
            string before = File.Exists(marker) ? File.ReadAllText(marker) : null;

            var report = NebulaLegacyDataMigrator.Compose(new NebulaPersistentPaths(legacyRoot, pretendReal));

            Assert.AreEqual(LegacyMigrationOutcome.Skipped, report.Outcome, report.note);
            StringAssert.Contains("temp", report.note);
            string after = File.Exists(marker) ? File.ReadAllText(marker) : null;
            Assert.AreEqual(before, after,
                "the interlock let a marker be written or changed outside the temp directory");
        }

        // ---- the ordinary case ------------------------------------------------

        /// <summary>
        /// A campaign in the old folder and nothing in the new one: everything comes
        /// across, byte for byte, and the marker is written.
        /// </summary>
        [Test]
        public void ALegacyCampaignLandsInAnEmptyInstallByteForByte()
        {
            SeedLegacySave(1, totalXp: 1234, saves: 4);   // four saves leaves slot1 plus bak1..bak3
            string sourceHash = HashOf(LegacyPath("saves", "slot1.json"));

            NebulaLegacyMigrationReport report = Run();

            Assert.AreEqual(LegacyMigrationOutcome.Migrated, report.Outcome, report.note);
            Assert.AreEqual(sourceHash, HashOf(ActivePath("saves", "slot1.json")),
                "the imported save is not byte-identical to the legacy one");
            Assert.IsTrue(File.Exists(NebulaLegacyDataMigrator.MarkerPath(activeRoot)));

            foreach (string recorded in report.imported)
                Assert.IsTrue(NebulaPersistentPathResolver.IsWithin(activeRoot, Path.Combine(activeRoot, recorded)),
                    "an imported file was recorded outside the active root: " + recorded);
        }

        /// <summary>
        /// The save actually loads afterwards. Copying the bytes is only half the claim —
        /// the envelope's <c>payloadHash</c> has to still describe the payload, which it
        /// only does if nothing re-serialised it on the way across.
        /// </summary>
        [Test]
        public void TheImportedSaveStillPassesTheSaveReader()
        {
            SeedLegacySave(1, totalXp: 4242);
            Run();

            SaveService.DirectoryOverride = ActivePath("saves");
            SavePayload loaded = SaveService.Load(1, out int schema);

            Assert.IsNotNull(loaded, "the imported save did not load");
            Assert.AreEqual(4242, loaded.totalXp, "the imported save is not the one that was written");
            Assert.AreEqual(SaveService.CurrentSchema, schema);
        }

        /// <summary>The three rotating backups come across with their slot — they are the recovery path.</summary>
        [Test]
        public void TheRotatingBackupsComeAcrossWithTheirSlot()
        {
            SeedLegacySave(1, totalXp: 10, saves: 4);
            foreach (string name in new[] { "slot1.bak1.json", "slot1.bak2.json", "slot1.bak3.json" })
                Assert.IsTrue(File.Exists(LegacyPath("saves", name)), "the fixture did not produce " + name);

            Run();

            foreach (string name in new[] { "slot1.json", "slot1.bak1.json", "slot1.bak2.json", "slot1.bak3.json" })
                Assert.AreEqual(HashOf(LegacyPath("saves", name)), HashOf(ActivePath("saves", name)),
                    name + " did not come across intact");
        }

        /// <summary>The save system's own temp file is never a player's save and never crosses.</summary>
        [Test]
        public void AnUncommittedTempSaveIsNotImported()
        {
            SeedLegacySave(1, totalXp: 7);
            File.WriteAllText(LegacyPath("saves", "slot1.tmp.json"), "{\"schemaVersion\":14}");

            Run();

            Assert.IsFalse(File.Exists(ActivePath("saves", "slot1.tmp.json")),
                "a half-written save was carried into a clean install");
        }

        /// <summary>A slot the active install does not have is filled; the ones it has are left alone.</summary>
        [Test]
        public void AMissingSlotIsFilledAndTheOccupiedOnesAreLeftAlone()
        {
            SeedLegacySave(1, totalXp: 111);
            SeedLegacySave(2, totalXp: 222);
            SeedActiveSave(1, totalXp: 999);
            string activeSlot1 = HashOf(ActivePath("saves", "slot1.json"));

            NebulaLegacyMigrationReport report = Run();

            Assert.AreEqual(activeSlot1, HashOf(ActivePath("saves", "slot1.json")),
                "the active install's own slot 1 was modified");
            Assert.AreEqual(HashOf(LegacyPath("saves", "slot2.json")), HashOf(ActivePath("saves", "slot2.json")),
                "the empty slot 2 was not filled from the legacy folder");
            Assert.IsNotEmpty(report.conflicts, "the slot-1 clash was not reported");
        }

        // ---- conflicts --------------------------------------------------------

        /// <summary>Both sides identical: nothing to do, and nothing done.</summary>
        [Test]
        public void IdenticalDataOnBothSidesCopiesNothing()
        {
            SeedLegacySave(1, totalXp: 55);
            Directory.CreateDirectory(ActivePath("saves"));
            foreach (string file in Directory.GetFiles(LegacyPath("saves")))
                File.Copy(file, Path.Combine(ActivePath("saves"), Path.GetFileName(file)));
            DateTime before = File.GetLastWriteTimeUtc(ActivePath("saves", "slot1.json"));

            NebulaLegacyMigrationReport report = Run();

            Assert.AreEqual(LegacyMigrationOutcome.NothingToImport, report.Outcome,
                "an install that already holds the same bytes has nothing to import: " + report.note);
            Assert.AreEqual(before, File.GetLastWriteTimeUtc(ActivePath("saves", "slot1.json")),
                "an identical file was rewritten rather than recognised");
            Assert.IsEmpty(report.conflicts);
        }

        /// <summary>
        /// Both sides hold slot 1 and they differ: the active install wins, and the legacy
        /// family is preserved whole rather than merged.
        /// </summary>
        [Test]
        public void AConflictingSlotKeepsTheActiveSaveAndPreservesTheLegacyOne()
        {
            SeedLegacySave(1, totalXp: 100, saves: 2);
            SeedActiveSave(1, totalXp: 900);
            string activeHash = HashOf(ActivePath("saves", "slot1.json"));
            string activeBackupHash = HashOf(ActivePath("saves", "slot1.bak1.json"));
            string legacyHash = HashOf(LegacyPath("saves", "slot1.json"));
            string legacyBackupHash = HashOf(LegacyPath("saves", "slot1.bak1.json"));
            Assert.AreNotEqual(activeHash, legacyHash, "the fixture produced two identical saves");

            NebulaLegacyMigrationReport report = Run();

            Assert.AreEqual(activeHash, HashOf(ActivePath("saves", "slot1.json")),
                "the newer save was overwritten by the legacy one");
            string archived = ActivePath(NebulaLegacyDataMigrator.ImportFolder, "conflicts", "saves", "slot1.json");
            Assert.IsTrue(File.Exists(archived), "the legacy save was not preserved under the archive");
            Assert.AreEqual(legacyHash, HashOf(archived), "the archived copy is not the legacy save");
            Assert.IsNotEmpty(report.conflicts, "the conflict was not reported");

            // The whole family, not just the primary: SaveService.Load falls through to the
            // backups, so a legacy bak1 sitting beside a different campaign's primary is a
            // slot that silently resumes the wrong campaign the first time the primary tears.
            Assert.AreEqual(activeBackupHash, HashOf(ActivePath("saves", "slot1.bak1.json")),
                "a legacy backup was mixed into a slot owned by another campaign");
            Assert.AreEqual(legacyBackupHash,
                HashOf(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "conflicts", "saves", "slot1.bak1.json")),
                "the legacy backup was neither imported nor preserved");
        }

        /// <summary>An existing rebind layout wins, and the old one is kept where the player can find it.</summary>
        [Test]
        public void ExistingInputBindingsWinAndTheLegacyLayoutIsPreserved()
        {
            WriteBindings(LegacyPath("settings", "input-bindings.json"), NebulaInputBindingStore.CurrentVersion, "fire", "Alpha1");
            WriteBindings(ActivePath("settings", "input-bindings.json"), NebulaInputBindingStore.CurrentVersion, "fire", "Alpha9");
            string activeHash = HashOf(ActivePath("settings", "input-bindings.json"));

            Run();

            Assert.AreEqual(activeHash, HashOf(ActivePath("settings", "input-bindings.json")),
                "the player's current layout was replaced by the legacy one");
            Assert.IsTrue(File.Exists(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "conflicts", "settings", "input-bindings.json")),
                "the legacy layout was not preserved");
        }

        /// <summary>With no layout on this install, the old one is adopted as it stands.</summary>
        [Test]
        public void LegacyInputBindingsAreAdoptedWhenThisInstallHasNone()
        {
            WriteBindings(LegacyPath("settings", "input-bindings.json"), NebulaInputBindingStore.CurrentVersion, "fire", "Alpha1");
            string legacyHash = HashOf(LegacyPath("settings", "input-bindings.json"));

            Run();

            Assert.AreEqual(legacyHash, HashOf(ActivePath("settings", "input-bindings.json")));
        }

        // ---- refusals ---------------------------------------------------------

        /// <summary>
        /// A truncated save is preserved and never installed. Parseable JSON is not the
        /// bar: this file parses and still fails the payload-hash check the loader runs.
        /// </summary>
        [Test]
        public void ACorruptLegacySaveIsPreservedButNeverInstalled()
        {
            SeedLegacySave(1, totalXp: 321);
            string path = LegacyPath("saves", "slot1.json");
            string truncated = File.ReadAllText(path);
            File.WriteAllText(path, truncated.Substring(0, truncated.Length / 2));
            Assert.IsFalse(SaveService.IsReadableSaveFile(path), "the fixture did not actually break the save");

            Run();

            Assert.IsFalse(File.Exists(ActivePath("saves", "slot1.json")),
                "a save the loader would reject was installed as a live slot");
            Assert.IsTrue(File.Exists(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "unreadable", "saves", "slot1.json")),
                "the unreadable save was discarded instead of preserved");
        }

        /// <summary>
        /// A save whose envelope parses but whose hash no longer matches — the exact shape
        /// of a hand-edited file — never lands on top of a valid active save.
        /// </summary>
        [Test]
        public void AHandEditedLegacySaveNeverLandsOnAValidActiveSave()
        {
            SeedLegacySave(1, totalXp: 5);
            SeedActiveSave(1, totalXp: 6);
            string activeHash = HashOf(ActivePath("saves", "slot1.json"));

            // Edited through the envelope so the payload no longer matches its recorded
            // payloadHash while the file still parses perfectly — the exact shape of a
            // hand-edited save, and the reason "is it valid JSON" is not the question.
            string path = LegacyPath("saves", "slot1.json");
            var envelope = JsonUtility.FromJson<SaveEnvelope>(File.ReadAllText(path));
            envelope.payloadJson += " ";
            File.WriteAllText(path, JsonUtility.ToJson(envelope, true));
            Assert.IsFalse(SaveService.IsReadableSaveFile(path), "the fixture did not actually break the save");

            Run();

            Assert.AreEqual(activeHash, HashOf(ActivePath("saves", "slot1.json")));
            Assert.IsTrue(File.Exists(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "conflicts", "saves", "slot1.json")),
                "the legacy save was neither installed nor preserved");
        }

        /// <summary>A settings file this build cannot read is preserved, not installed.</summary>
        [Test]
        public void CorruptInputSettingsArePreservedButNeverInstalled()
        {
            Directory.CreateDirectory(LegacyPath("settings"));
            File.WriteAllText(LegacyPath("settings", "input-bindings.json"), "{ this is not json");

            Run();

            Assert.IsFalse(File.Exists(ActivePath("settings", "input-bindings.json")),
                "an unreadable settings file was installed");
            Assert.IsTrue(File.Exists(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "unreadable", "settings", "input-bindings.json")));
        }

        /// <summary>A layout written by a settings version this build does not read is declined the same way the store declines it.</summary>
        [Test]
        public void ASettingsFileFromAnUnknownVersionIsNotInstalled()
        {
            WriteBindings(LegacyPath("settings", "input-bindings.json"),
                NebulaInputBindingStore.CurrentVersion + 99, "fire", "Alpha1");

            Run();

            Assert.IsFalse(File.Exists(ActivePath("settings", "input-bindings.json")));
            Assert.IsTrue(File.Exists(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "unreadable", "settings", "input-bindings.json")));
        }

        /// <summary>
        /// Logs and telemetry are preserved whole and never placed live, so the active
        /// install keeps exactly one session log and exactly one funnel dedup index.
        /// </summary>
        [Test]
        public void LogsAndTelemetryArePreservedAndNeverMerged()
        {
            Directory.CreateDirectory(LegacyPath("logs"));
            Directory.CreateDirectory(LegacyPath("telemetry"));
            File.WriteAllText(LegacyPath("logs", "sessions.jsonl"), "{\"session\":\"old\"}\n");
            File.WriteAllText(LegacyPath("telemetry", "funnel-index.json"), "{\"entries\":[{\"campaignId\":\"old\"}]}");

            Directory.CreateDirectory(ActivePath("logs"));
            Directory.CreateDirectory(ActivePath("telemetry"));
            File.WriteAllText(ActivePath("logs", "sessions.jsonl"), "{\"session\":\"new\"}\n");
            File.WriteAllText(ActivePath("telemetry", "funnel-index.json"), "{\"entries\":[]}");

            Run();

            Assert.AreEqual("{\"session\":\"new\"}\n", File.ReadAllText(ActivePath("logs", "sessions.jsonl")),
                "the legacy log was concatenated into the active one");
            Assert.AreEqual("{\"entries\":[]}", File.ReadAllText(ActivePath("telemetry", "funnel-index.json")),
                "the legacy funnel index was merged into the active authority");

            Assert.IsTrue(File.Exists(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "logs", "sessions.jsonl")),
                "the legacy session log was not preserved");
            Assert.IsTrue(File.Exists(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "telemetry", "funnel-index.json")),
                "the legacy telemetry index was not preserved");
        }

        /// <summary>
        /// The platform really does report a symlink as a reparse point.
        /// <para>
        /// The link guard is one flag test, and if this runtime did not set that flag the
        /// guard would silently pass everything through while every other test here stayed
        /// green. This is the test that would fail instead.
        /// </para>
        /// </summary>
        [Test]
        public void ThisRuntimeReportsASymlinkAsAReparsePoint()
        {
            RequirePosixLinks();
            string target = Path.Combine(testRoot, "target.txt");
            string link = Path.Combine(testRoot, "link.txt");
            File.WriteAllText(target, "x");
            Symlink(target, link);

            Assert.IsTrue(File.Exists(link), "the fixture did not create a usable link");
            Assert.AreNotEqual(0, (int)(File.GetAttributes(link) & FileAttributes.ReparsePoint),
                "this runtime does not flag symlinks, so the migrator's link guard is inert");
        }

        /// <summary>A link out of the legacy folder is refused, so its target never crosses.</summary>
        [Test]
        public void ALinkedFilePointingOutsideTheLegacyRootIsRefused()
        {
            RequirePosixLinks();
            SeedLegacySave(1, totalXp: 1);

            string secret = Path.Combine(testRoot, "outside-secret.json");
            File.WriteAllText(secret, "{\"secret\":true}");
            Symlink(secret, LegacyPath("saves", "slot2.json"));

            NebulaLegacyMigrationReport report = Run();

            Assert.IsFalse(File.Exists(ActivePath("saves", "slot2.json")),
                "a link was followed out of the legacy folder");
            Assert.IsNotEmpty(report.refused, "the refusal was not reported");
            Assert.IsTrue(File.Exists(secret), "the link target must be left exactly as it was");
        }

        /// <summary>A linked directory that climbs out of the legacy folder is refused as a whole.</summary>
        [Test]
        public void ALinkedDirectoryThatClimbsOutOfTheLegacyRootIsRefused()
        {
            RequirePosixLinks();
            Directory.CreateDirectory(LegacyPath("logs"));
            File.WriteAllText(LegacyPath("logs", "sessions.jsonl"), "{}\n");

            string outside = Path.Combine(testRoot, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "private.jsonl"), "{}\n");
            Symlink(outside, Path.Combine(LegacyPath("logs"), "escape"));

            NebulaLegacyMigrationReport report = Run();

            Assert.IsFalse(File.Exists(ActivePath(NebulaLegacyDataMigrator.ImportFolder, "logs", "escape", "private.jsonl")),
                "a linked directory was walked out of the legacy folder");
            Assert.IsNotEmpty(report.refused);
        }

        // ---- crash safety and idempotency -------------------------------------

        /// <summary>
        /// The marker is the last write, so a run that dies mid-copy leaves no marker, no
        /// staging folder and an untouched legacy folder — and the retry finishes the job.
        /// </summary>
        [Test]
        public void AnInterruptedMigrationLeavesNoMarkerAndIsRunnableAgain()
        {
            SeedLegacySave(1, totalXp: 60, saves: 4);
            string legacyBefore = HashOf(LegacyPath("saves", "slot1.json"));

            int placed = 0;
            NebulaLegacyDataMigrator.CommitObserver = _ =>
            {
                if (++placed == 2) throw new IOException("simulated power loss");
            };

            NebulaLegacyMigrationReport interrupted = Run();

            Assert.AreEqual(LegacyMigrationOutcome.Failed, interrupted.Outcome);
            Assert.IsFalse(File.Exists(NebulaLegacyDataMigrator.MarkerPath(activeRoot)),
                "a marker was written for a migration that did not finish");
            Assert.AreEqual(legacyBefore, HashOf(LegacyPath("saves", "slot1.json")),
                "the legacy folder was modified by a failed run");
            Assert.IsEmpty(StagingFolders(), "the staging folder outlived the failed run");

            NebulaLegacyDataMigrator.CommitObserver = null;
            NebulaLegacyMigrationReport retry = Run();

            Assert.AreEqual(LegacyMigrationOutcome.Migrated, retry.Outcome, retry.note);
            Assert.IsTrue(File.Exists(NebulaLegacyDataMigrator.MarkerPath(activeRoot)));
            foreach (string name in new[] { "slot1.json", "slot1.bak1.json", "slot1.bak2.json", "slot1.bak3.json" })
                Assert.AreEqual(HashOf(LegacyPath("saves", name)), HashOf(ActivePath("saves", name)),
                    name + " did not survive the retry");
        }

        /// <summary>Staging is transient: after a clean run there is nothing left of it.</summary>
        [Test]
        public void TheStagingFolderDoesNotOutliveASuccessfulMigration()
        {
            SeedLegacySave(1, totalXp: 3, saves: 3);
            Assert.AreEqual(LegacyMigrationOutcome.Migrated, Run().Outcome);
            Assert.IsEmpty(StagingFolders(), "a staging folder was left behind");
        }

        /// <summary>The second boot copies nothing and reads no folders.</summary>
        [Test]
        public void ASecondMigrationIsANoOp()
        {
            SeedLegacySave(1, totalXp: 8);
            Assert.AreEqual(LegacyMigrationOutcome.Migrated, Run().Outcome);
            DateTime importedAt = File.GetLastWriteTimeUtc(ActivePath("saves", "slot1.json"));

            NebulaLegacyMigrationReport second = Run();

            Assert.AreEqual(LegacyMigrationOutcome.AlreadyMigrated, second.Outcome);
            Assert.IsEmpty(second.imported);
            Assert.AreEqual(importedAt, File.GetLastWriteTimeUtc(ActivePath("saves", "slot1.json")),
                "the second run rewrote a file it should not have looked at");
        }

        /// <summary>
        /// A marker that cannot be read costs a re-inspection and nothing else. Never a
        /// deletion, and never a legacy file landing on top of newer data.
        /// </summary>
        [Test]
        public void ACorruptMarkerCausesASafeReInspectionAndLosesNothing()
        {
            SeedLegacySave(1, totalXp: 12);
            Run();
            string imported = HashOf(ActivePath("saves", "slot1.json"));

            File.WriteAllText(NebulaLegacyDataMigrator.MarkerPath(activeRoot), "}{ not json");

            NebulaLegacyMigrationReport report = Run();

            Assert.AreNotEqual(LegacyMigrationOutcome.Failed, report.Outcome, report.note);
            Assert.AreEqual(imported, HashOf(ActivePath("saves", "slot1.json")),
                "the re-inspection modified data it had already imported");
            Assert.IsTrue(File.Exists(LegacyPath("saves", "slot1.json")), "the legacy folder lost a file");
            Assert.IsTrue(File.Exists(NebulaLegacyDataMigrator.MarkerPath(activeRoot)),
                "the re-inspection did not leave a readable marker behind");
        }

        /// <summary>A marker from another migration version is not this migration's marker.</summary>
        [Test]
        public void AMarkerFromADifferentMigrationVersionIsNotHonoured()
        {
            Directory.CreateDirectory(ActivePath(NebulaLegacyDataMigrator.ImportFolder));
            File.WriteAllText(NebulaLegacyDataMigrator.MarkerPath(activeRoot),
                JsonUtility.ToJson(new NebulaLegacyMigrationMarker
                {
                    migrationVersion = NebulaLegacyDataMigrator.CurrentMigrationVersion + 1,
                    completedAtUtc = DateTime.UtcNow.ToString("o"),
                }));

            Assert.IsFalse(NebulaLegacyDataMigrator.HasValidMarker(activeRoot, out _));

            SeedLegacySave(1, totalXp: 77);
            Assert.AreEqual(LegacyMigrationOutcome.Migrated, Run().Outcome);
        }

        /// <summary>
        /// Saving and loading a campaign never touch the marker. It is a fact about the
        /// install, not about a run, so no amount of play can make the game import twice.
        /// </summary>
        [Test]
        public void SavingAndLoadingNeverTouchTheMarker()
        {
            SeedLegacySave(1, totalXp: 21);
            Run();

            string markerPath = NebulaLegacyDataMigrator.MarkerPath(activeRoot);
            string markerBefore = File.ReadAllText(markerPath);
            DateTime stampBefore = File.GetLastWriteTimeUtc(markerPath);

            SaveService.DirectoryOverride = ActivePath("saves");
            Assert.IsTrue(SaveService.Save(2, new SavePayload { totalXp = 4 }, revision: 1));
            Assert.IsNotNull(SaveService.Load(1));

            Assert.AreEqual(markerBefore, File.ReadAllText(markerPath), "saving rewrote the migration marker");
            Assert.AreEqual(stampBefore, File.GetLastWriteTimeUtc(markerPath), "saving touched the migration marker");
        }

        // ---- the operator's machine -------------------------------------------

        /// <summary>
        /// The real Application Support folders are not read from, written to or created
        /// by any of this. Checked by hashing what is there before and after a full
        /// migration rather than by trusting that the temp roots were used.
        /// </summary>
        [Test]
        public void TheOperatorsRealDataFolderIsNeverTouched()
        {
            string home = NebulaPersistentPathResolver.HomeDirectory();
            string legacyReal = Path.Combine(home, "Library", "Application Support",
                NebulaAppIdentity.LegacyCompany, NebulaAppIdentity.LegacyProduct);
            string activeReal = Path.Combine(home, "Library", "Application Support",
                NebulaAppIdentity.Company, NebulaAppIdentity.Product);

            Dictionary<string, string> before = Fingerprint(legacyReal);
            // Same reasoning as TheEditorRefusesAnyRootOutsideTheTempDirectory: the real
            // active folder legitimately carries a marker once the shipped player has run
            // on this machine, so the guarantee is "unchanged by this run", not "absent".
            Dictionary<string, string> activeBefore = Fingerprint(activeReal);

            SeedLegacySave(1, totalXp: 99, saves: 2);
            WriteBindings(LegacyPath("settings", "input-bindings.json"), NebulaInputBindingStore.CurrentVersion, "fire", "Alpha1");
            Directory.CreateDirectory(LegacyPath("logs"));
            File.WriteAllText(LegacyPath("logs", "sessions.jsonl"), "{}\n");
            Run();

            CollectionAssert.AreEquivalent(before, Fingerprint(legacyReal),
                "the migration changed the operator's real legacy data folder");
            CollectionAssert.AreEquivalent(activeBefore, Fingerprint(activeReal),
                "the migration changed the operator's real active data folder");
        }

        // ---- version pins -----------------------------------------------------

        /// <summary>
        /// Nothing about an app rename or a folder move is allowed to move a version that
        /// describes content or a file format. Pinned here because a migration is exactly
        /// the kind of change that invites somebody to "just bump the schema".
        /// </summary>
        [Test]
        public void TheVersionPinsAreUnmoved()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema, "SaveService.CurrentSchema");
            // P2-SAVE-201: the default was 13, which made an envelope with no
            // schemaVersion at all impersonate a historical v13 save and walk
            // straight through the modern loader's gates. Unknown is 0, and the
            // loader refuses anything below 6. Pinned so nobody restores 13.
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion, "SaveEnvelope.schemaVersion default");
            Assert.AreEqual(3, Sectors.SectorManifestGenerator.GenerationVersion, "SectorManifestGenerator.GenerationVersion");
            Assert.AreEqual(3, Content.ContentFingerprint.ContentRevision, "ContentFingerprint.ContentRevision");
        }

        /// <summary>The migration carries no schema of its own into the save format.</summary>
        [Test]
        public void TheMigrationIsVersionedSeparatelyFromTheSaveFormat()
        {
            Assert.AreEqual(1, NebulaLegacyDataMigrator.CurrentMigrationVersion);
            Assert.AreNotEqual(SaveService.CurrentSchema, NebulaLegacyDataMigrator.CurrentMigrationVersion,
                "the migration must not be versioned by the save schema");
        }

        // ---- fixture helpers --------------------------------------------------

        private NebulaLegacyMigrationReport Run() =>
            NebulaLegacyDataMigrator.Compose(new NebulaPersistentPaths(legacyRoot, activeRoot));

        private string LegacyPath(params string[] parts) => Under(legacyRoot, parts);
        private string ActivePath(params string[] parts) => Under(activeRoot, parts);

        private static string Under(string root, string[] parts)
        {
            string path = Path.Combine(root, NebulaAppIdentity.PayloadFolder);
            foreach (string part in parts) path = Path.Combine(path, part);
            return path;
        }

        /// <summary>
        /// Writes a genuinely valid save through <see cref="SaveService"/> itself, so the
        /// envelope, the payload hash and the backup rotation are the real ones. A
        /// hand-written fixture would let a migration bug hide behind a fixture bug.
        /// </summary>
        private void SeedLegacySave(int slot, long totalXp, int saves = 1) =>
            SeedSave(LegacyPath("saves"), slot, totalXp, saves);

        private void SeedActiveSave(int slot, long totalXp, int saves = 1) =>
            SeedSave(ActivePath("saves"), slot, totalXp, saves);

        private void SeedSave(string directory, int slot, long totalXp, int saves)
        {
            string restore = SaveService.DirectoryOverride;
            SaveService.DirectoryOverride = directory;
            try
            {
                for (int revision = 1; revision <= saves; revision++)
                    Assert.IsTrue(SaveService.Save(slot, new SavePayload { totalXp = totalXp + revision }, revision),
                        "the fixture could not write a save");

                // The last write is the one the assertions describe, so it carries the
                // number the caller asked for rather than the loop's offset.
                Assert.IsTrue(SaveService.Save(slot, new SavePayload { totalXp = totalXp }, saves + 1));
            }
            finally { SaveService.DirectoryOverride = restore; }
        }

        private static void WriteBindings(string path, int version, string actionId, string key)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var document = new InputBindingDocument { settingsVersion = version };
            document.bindings.Add(new InputBindingEntry { actionId = actionId, key = key });
            File.WriteAllText(path, JsonUtility.ToJson(document, true));
        }

        private IEnumerable<string> StagingFolders()
        {
            string importRoot = ActivePath(NebulaLegacyDataMigrator.ImportFolder);
            if (!Directory.Exists(importRoot)) return Array.Empty<string>();
            return Directory.GetDirectories(importRoot, ".staging-*", SearchOption.TopDirectoryOnly);
        }

        private static string HashOf(string path)
        {
            Assert.IsTrue(File.Exists(path), "expected a file at " + path);
            using (var hasher = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hasher.ComputeHash(stream));
        }

        /// <summary>Every file under a folder with its hash, or an empty map when the folder is absent.</summary>
        private static Dictionary<string, string> Fingerprint(string root)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(root)) return map;
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                map[file] = HashOf(file);
            return map;
        }

        private static void RequirePosixLinks()
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
                Assert.Ignore("this fixture creates POSIX symlinks with /bin/ln, which needs a macOS editor host");
        }

        private static void Symlink(string target, string link)
        {
            using (Process process = Process.Start(new ProcessStartInfo("/bin/ln", $"-s \"{target}\" \"{link}\"")
            { UseShellExecute = false, CreateNoWindow = true }))
            {
                process.WaitForExit();
                Assert.AreEqual(0, process.ExitCode, "could not create the test symlink");
            }
        }
    }

    /// <summary>
    /// The sentence the main menu shows a player whose data was carried across.
    /// <para>
    /// Split from the migration fixture because it is about words rather than files:
    /// the migrator's report is written for whoever has to debug an import, and the
    /// one case a player must not be shown in that vocabulary is the one where their
    /// old save was deliberately kept back. "Conflict" reads as damage; it isn't.
    /// </para>
    /// </summary>
    public class NebulaLegacyMigrationNoticeTests
    {
        private static NebulaLegacyMigrationReport Report(
            LegacyMigrationOutcome outcome, int imported = 0, int archived = 0, int conflicts = 0)
        {
            var report = new NebulaLegacyMigrationReport { Outcome = outcome };
            for (int i = 0; i < imported; i++) report.imported.Add($"nebula/saves/slot{i + 1}.json");
            for (int i = 0; i < archived; i++) report.archived.Add($"nebula/legacy-import/conflicts/saves/slot{i + 1}.json");
            for (int i = 0; i < conflicts; i++) report.conflicts.Add("slot1 differs on both sides");
            return report;
        }

        [Test]
        public void NothingIsSaidWhenNothingWasCarriedAcross()
        {
            Assert.IsNull(UI.Session.NebulaMainMenuScreen.MigrationNoticeFor(null),
                "no report at all is the ordinary case and must stay silent");
            foreach (LegacyMigrationOutcome outcome in new[]
            {
                LegacyMigrationOutcome.NotAttempted, LegacyMigrationOutcome.Skipped,
                LegacyMigrationOutcome.AlreadyMigrated, LegacyMigrationOutcome.NoLegacyData,
                LegacyMigrationOutcome.NothingToImport,
            })
                Assert.IsNull(UI.Session.NebulaMainMenuScreen.MigrationNoticeFor(Report(outcome, imported: 3)),
                    $"{outcome} moved nothing on this boot, so the menu has nothing to announce");
        }

        [Test]
        public void AFailedMigrationStaysQuietBecauseTheOldDataIsStillThere()
        {
            // Given files it did place before failing, so passing cannot mean merely
            // "there was nothing to talk about" -- the outcome has to be what silences it.
            Assert.IsNull(
                UI.Session.NebulaMainMenuScreen.MigrationNoticeFor(
                    Report(LegacyMigrationOutcome.Failed, imported: 2, archived: 1)),
                "a failed migration deletes nothing and retries next boot; announcing it would " +
                "only worry a player about a folder that is still intact");
        }

        [Test]
        public void APlainImportIsReportedInOneCalmSentence()
        {
            string notice = UI.Session.NebulaMainMenuScreen.MigrationNoticeFor(
                Report(LegacyMigrationOutcome.Migrated, imported: 4));
            Assert.IsNotNull(notice, "a player whose save moved should be told it moved");
            StringAssert.Contains("taşındı", notice);
        }

        [Test]
        public void AKeptBackCopyIsDescribedAsKeptAndNotAsAConflict()
        {
            foreach (NebulaLegacyMigrationReport report in new[]
            {
                Report(LegacyMigrationOutcome.Migrated, imported: 1, archived: 2),
                Report(LegacyMigrationOutcome.Migrated, imported: 1, conflicts: 1),
            })
            {
                string notice = UI.Session.NebulaMainMenuScreen.MigrationNoticeFor(report);
                Assert.IsNotNull(notice);
                StringAssert.Contains("silinmedi", notice,
                    "the one thing this case has to say is that nothing was lost");
                foreach (string jargon in new[] { "conflict", "çakış", "archive", "arşiv", "legacy-import", "hash" })
                    StringAssert.DoesNotContain(jargon, notice.ToLowerInvariant(),
                        "the report's vocabulary must not reach the menu");
            }
        }
    }
}
