using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Platform;
using Nebula.Ship;
using Nebula.UI.Session;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Records the order the engine really ran its startup phases in.
    /// <para>
    /// Here rather than in the fixture because a <c>[RuntimeInitializeOnLoadMethod]</c>
    /// fires when play mode starts, long before any test method does. It exists so the
    /// ordering claim can be <b>observed</b> instead of asserted: reading the attribute
    /// off the migrator would only prove the attribute is spelled the way the test
    /// spells it, and would stay green if Unity's phase order were the opposite of what
    /// the comment says.
    /// </para>
    /// </summary>
    public static class NebulaBootPhaseProbe
    {
        public static readonly List<string> PhasesInOrder = new List<string>();

        /// <summary>Whether the migrator had already finished by the time the phase the binding store loads in began.</summary>
        public static bool MigrationFinishedBeforeSceneLoadPhase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void AtSubsystemRegistration() => PhasesInOrder.Add("SubsystemRegistration");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void AtAfterAssembliesLoaded() => PhasesInOrder.Add("AfterAssembliesLoaded");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AtBeforeSceneLoad()
        {
            PhasesInOrder.Add("BeforeSceneLoad");
            MigrationFinishedBeforeSceneLoadPhase = NebulaLegacyDataMigrator.StartupCompleted;
        }
    }

    /// <summary>
    /// The rename seen from the player's side: does Continue come back, does the campaign
    /// load, are the keys the ones they bound, and does none of it happen twice.
    /// <para>
    /// Every path is an injected temp root under <c>/private/tmp</c>. The Editor interlock
    /// inside <see cref="NebulaLegacyDataMigrator.Compose"/> refuses anything else, and
    /// <see cref="TheRealApplicationSupportFoldersAreUntouched"/> checks the machine rather
    /// than trusting the arrangement.
    /// </para>
    /// </summary>
    public class NebulaLegacyDataMigrationPlayModeTests
    {
        private const string TestRootName = "nebula-legacy-migration-playmode";

        private string testRoot;
        private string legacyRoot;
        private string activeRoot;

        private string savedSaveDirectory;
        private string savedBindingDirectory;
        private string savedTelemetryDirectory;
        private string savedLogDirectory;
        private KeyCode savedRepairKey;

        [SetUp]
        public void SetUp()
        {
            testRoot = Path.Combine("/private/tmp", TestRootName, Guid.NewGuid().ToString("N"));
            legacyRoot = Path.Combine(testRoot, "legacy");
            activeRoot = Path.Combine(testRoot, "active");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(activeRoot);

            savedSaveDirectory = SaveService.DirectoryOverride;
            savedBindingDirectory = NebulaInputBindingStore.DirectoryOverride;
            savedTelemetryDirectory = Telemetry.TelemetryService.DirectoryOverride;
            savedLogDirectory = Progression.SessionLog.DirectoryOverride;
            savedRepairKey = CombatBindings.RepairKey;

            // Pointed at the *new* install everywhere, which is the state a player is in
            // the first time they launch the renamed build.
            SaveService.DirectoryOverride = ActivePath("saves");
            NebulaInputBindingStore.DirectoryOverride = ActivePath("settings");
            Telemetry.TelemetryService.DirectoryOverride = ActivePath("telemetry");
            Progression.SessionLog.DirectoryOverride = ActivePath("logs");
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.DirectoryOverride = savedSaveDirectory;
            NebulaInputBindingStore.DirectoryOverride = savedBindingDirectory;
            Telemetry.TelemetryService.DirectoryOverride = savedTelemetryDirectory;
            Progression.SessionLog.DirectoryOverride = savedLogDirectory;
            CombatBindings.RepairKey = savedRepairKey;
            NebulaLegacyDataMigrator.CommitObserver = null;

            try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); }
            catch (Exception) { /* a leaked temp folder is not worth failing a test over */ }
        }

        // ---- composition order -------------------------------------------------

        /// <summary>
        /// The migration is finished before anything that reads persistent data starts.
        /// <para>
        /// Two claims, both from what the engine actually did this run. First, that
        /// <c>SubsystemRegistration</c> really does precede <c>BeforeSceneLoad</c> — the
        /// assumption the hook's placement rests on. Second, that the migrator's startup
        /// work was already complete when the <c>BeforeSceneLoad</c> phase began, which
        /// covers <b>every</b> method in that phase including
        /// <see cref="NebulaInputBindingStore.LoadOnStartup"/>, and so does not depend on
        /// the undefined ordering between two methods that share a phase.
        /// </para>
        /// </summary>
        [Test]
        public void TheMigrationFinishesBeforeAnythingReadsPersistentData()
        {
            List<string> phases = NebulaBootPhaseProbe.PhasesInOrder;
            int subsystem = phases.IndexOf("SubsystemRegistration");
            int beforeScene = phases.IndexOf("BeforeSceneLoad");

            Assert.GreaterOrEqual(subsystem, 0, "the SubsystemRegistration probe never ran");
            Assert.GreaterOrEqual(beforeScene, 0, "the BeforeSceneLoad probe never ran");
            Assert.Less(subsystem, beforeScene,
                "SubsystemRegistration did not run before BeforeSceneLoad, so the hook's placement is wrong");

            Assert.IsTrue(NebulaLegacyDataMigrator.StartupCompleted,
                "the migrator's startup hook never ran");
            Assert.IsTrue(NebulaBootPhaseProbe.MigrationFinishedBeforeSceneLoadPhase,
                "the migration had not finished when the BeforeSceneLoad phase began, so the input " +
                "binding store could have read settings before they were imported");
        }

        /// <summary>
        /// The two hooks are in the phases the design says they are.
        /// <para>
        /// A supplement to the behavioural proof above, not a replacement for it: it is
        /// what catches somebody moving the migrator down into <c>BeforeSceneLoad</c>
        /// "because it works", which the observation alone would keep passing on the runs
        /// where the coin landed the right way up.
        /// </para>
        /// </summary>
        [Test]
        public void TheMigratorHooksAStrictlyEarlierPhaseThanTheBindingStore()
        {
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration,
                PhaseOf(typeof(NebulaLegacyDataMigrator), "RunAtStartup"), "the migrator's startup phase");
            Assert.AreEqual(RuntimeInitializeLoadType.BeforeSceneLoad,
                PhaseOf(typeof(NebulaInputBindingStore), nameof(NebulaInputBindingStore.LoadOnStartup)),
                "the binding store's startup phase");
        }

        // ---- what the player sees ---------------------------------------------

        /// <summary>
        /// Continue comes back. This is the whole point of the exercise: without the
        /// migration the renamed build looks at an empty folder and offers a grey button
        /// to somebody with a campaign on disk.
        /// </summary>
        [Test]
        public void ContinueIsUnavailableBeforeTheMigrationAndAvailableAfterIt()
        {
            SeedLegacySave(1, totalXp: 3000);

            Assert.IsFalse(NebulaSaveSlotQuery.HasAnythingToContinue(),
                "the fixture is wrong: the new install already had something to continue");

            NebulaLegacyMigrationReport report = Run();
            Assert.AreEqual(LegacyMigrationOutcome.Migrated, report.Outcome, report.note);

            Assert.IsTrue(NebulaSaveSlotQuery.HasAnythingToContinue(),
                "Continue is still unavailable after a legacy campaign was migrated");
            SaveSlotSummary resume = NebulaSaveSlotQuery.MostRecent();
            Assert.IsNotNull(resume);
            Assert.AreEqual(1, resume.Slot);
            Assert.AreEqual(SaveSlotBlockReason.None, resume.BlockReason);
        }

        /// <summary>Continue would actually resume: the migrated campaign loads and is the same campaign.</summary>
        [Test]
        public void TheMigratedCampaignLoads()
        {
            SeedLegacySave(1, totalXp: 8080);
            Run();

            SavePayload payload = SaveService.Load(1, out int schema, out string campaignId);

            Assert.IsNotNull(payload, "the migrated save did not load");
            Assert.AreEqual(8080, payload.totalXp, "a different campaign came across");
            Assert.AreEqual(SaveService.CurrentSchema, schema);
            Assert.IsNotEmpty(campaignId, "the migrated save lost its campaign identity");
        }

        /// <summary>The player's own key layout survives the rename.</summary>
        [Test]
        public void TheLegacyKeyLayoutIsRestoredAfterTheMigration()
        {
            KeyCode rebound = FirstAcceptableRepairKey();
            Assert.AreNotEqual(KeyCode.None, rebound, "no candidate key was accepted by the validator");

            // Written by the real store into the old folder, so the file under test is the
            // one the previous build would actually have left behind.
            NebulaInputBindingStore.DirectoryOverride = LegacyPath("settings");
            Assert.IsTrue(NebulaInputBindingStore.Save());

            CombatBindings.RepairKey = savedRepairKey;
            NebulaInputBindingStore.DirectoryOverride = ActivePath("settings");
            Assert.AreEqual(0, NebulaInputBindingStore.Load(), "the new install started with a layout it should not have");

            Run();
            NebulaInputBindingStore.Load();

            Assert.AreEqual(rebound, CombatBindings.RepairKey,
                "the rebound key did not come back after the migration");
        }

        // ---- doing it twice ----------------------------------------------------

        /// <summary>Relaunching does not import again — the marker is a fact about the install.</summary>
        [Test]
        public void ARestartDoesNotRunTheMigrationAgain()
        {
            SeedLegacySave(1, totalXp: 12);
            Assert.AreEqual(LegacyMigrationOutcome.Migrated, Run().Outcome);

            string marker = NebulaLegacyDataMigrator.MarkerPath(activeRoot);
            string markerBefore = File.ReadAllText(marker);
            string importedBefore = HashOf(ActivePath("saves", "slot1.json"));

            NebulaLegacyMigrationReport second = Run();

            Assert.AreEqual(LegacyMigrationOutcome.AlreadyMigrated, second.Outcome, second.note);
            Assert.AreEqual(markerBefore, File.ReadAllText(marker), "the second launch rewrote the marker");
            Assert.AreEqual(importedBefore, HashOf(ActivePath("saves", "slot1.json")));
        }

        /// <summary>
        /// Starting a new campaign is a campaign-lifetime act and the migration is an
        /// install-lifetime one. Neither may reach into the other: the new campaign must
        /// not re-trigger the import, and it must not overwrite what was imported.
        /// </summary>
        [Test]
        public void ANewCampaignNeitherRerunsTheMigrationNorClobbersWhatItImported()
        {
            SeedLegacySave(1, totalXp: 4004);
            Run();
            string importedBefore = HashOf(ActivePath("saves", "slot1.json"));
            DateTime markerBefore = File.GetLastWriteTimeUtc(NebulaLegacyDataMigrator.MarkerPath(activeRoot));

            string previous = CampaignIdentity.Current;
            try
            {
                CampaignIdentity.StartNewCampaign();
                Assert.IsTrue(SaveService.Save(2, new SavePayload { totalXp = 1 }, revision: 1));
            }
            finally
            {
                if (previous == null) CampaignIdentity.ClearForTests();
            }

            Assert.AreEqual(importedBefore, HashOf(ActivePath("saves", "slot1.json")),
                "a new campaign overwrote the migrated one");
            Assert.AreEqual(markerBefore, File.GetLastWriteTimeUtc(NebulaLegacyDataMigrator.MarkerPath(activeRoot)),
                "a new campaign touched the migration marker");
            Assert.AreEqual(LegacyMigrationOutcome.AlreadyMigrated, Run().Outcome,
                "a new campaign made the migration runnable again");
        }

        /// <summary>
        /// One live save folder and one live settings file, with the preserved legacy
        /// copies parked under the import folder where nothing reads them.
        /// </summary>
        [Test]
        public void ThereIsExactlyOneLiveSaveFolderAndOneLiveSettingsFile()
        {
            SeedLegacySave(1, totalXp: 6);
            Directory.CreateDirectory(LegacyPath("settings"));
            NebulaInputBindingStore.DirectoryOverride = LegacyPath("settings");
            Assert.IsTrue(NebulaInputBindingStore.Save());
            NebulaInputBindingStore.DirectoryOverride = ActivePath("settings");

            Directory.CreateDirectory(LegacyPath("logs"));
            File.WriteAllText(LegacyPath("logs", "sessions.jsonl"), "{\"session\":\"old\"}\n");

            Run();

            Assert.AreEqual(1, LiveFilesNamed("input-bindings.json").Count,
                "there must be exactly one input-settings authority outside the import archive");
            Assert.AreEqual(1, LiveFilesNamed("slot1.json").Count,
                "there must be exactly one live copy of a save slot");
            Assert.AreEqual(SaveService.SlotFile(1), LiveFilesNamed("slot1.json")[0],
                "the one live save is not the one SaveService reads");
            Assert.AreEqual(0, LiveFilesNamed("sessions.jsonl").Count,
                "a legacy session log was placed live beside the active install's own");
        }

        // ---- the operator's machine --------------------------------------------

        /// <summary>
        /// Neither real Application Support folder is read into, written to or created.
        /// Checked against the machine, because the guarantee is about the machine.
        /// </summary>
        [Test]
        public void TheRealApplicationSupportFoldersAreUntouched()
        {
            string support = Path.Combine(NebulaPersistentPathResolver.HomeDirectory(),
                "Library", "Application Support");
            string legacyReal = Path.Combine(support, NebulaAppIdentity.LegacyCompany, NebulaAppIdentity.LegacyProduct);
            string activeReal = Path.Combine(support, NebulaAppIdentity.Company, NebulaAppIdentity.Product);

            Dictionary<string, string> legacyBefore = Fingerprint(legacyReal);
            Dictionary<string, string> activeBefore = Fingerprint(activeReal);

            SeedLegacySave(1, totalXp: 17);
            Run();

            CollectionAssert.AreEquivalent(legacyBefore, Fingerprint(legacyReal),
                "the migration changed the real legacy data folder");
            CollectionAssert.AreEquivalent(activeBefore, Fingerprint(activeReal),
                "the migration changed the real active data folder");
            // Absence is the wrong assertion here, and it was wrong the moment anyone ran
            // the shipped build on this machine: a real player legitimately migrates and
            // legitimately leaves a marker in the active root. The Fingerprint comparisons
            // above already carry the real guarantee -- that this run changed nothing --
            // and they cover the marker along with everything else. Asserting absence made
            // the test a statement about whether the operator has ever played the game.
            Assert.IsFalse(File.Exists(NebulaLegacyDataMigrator.MarkerPath(legacyReal)),
                "a marker in the legacy root would mean the migration wrote into its own source");
        }

        // ---- fixture helpers ----------------------------------------------------

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

        /// <summary>Writes a real save through <see cref="SaveService"/> into the old folder.</summary>
        private void SeedLegacySave(int slot, long totalXp)
        {
            string restore = SaveService.DirectoryOverride;
            SaveService.DirectoryOverride = LegacyPath("saves");
            try
            {
                Assert.IsTrue(SaveService.Save(slot, new SavePayload { totalXp = totalXp + 1 }, 1));
                Assert.IsTrue(SaveService.Save(slot, new SavePayload { totalXp = totalXp }, 2));
            }
            finally { SaveService.DirectoryOverride = restore; }
        }

        /// <summary>
        /// A key the rebinding validator will actually accept for Repair.
        /// <para>
        /// Searched exhaustively rather than picked from a shortlist. The reserved set is
        /// derived from the entire action catalog, so any hardcoded choice is a guess that
        /// rots the moment a new action claims that key — a shortlist of seven letters was
        /// the first version of this helper and every one of them was already spoken for.
        /// </para>
        /// </summary>
        private static KeyCode FirstAcceptableRepairKey()
        {
            foreach (KeyCode candidate in (KeyCode[])Enum.GetValues(typeof(KeyCode)))
            {
                if (candidate == KeyCode.None || candidate == CombatBindings.RepairKey) continue;
                if (CombatBindings.TrySetBinding(CombatAction.Repair, candidate)) return candidate;
            }
            return KeyCode.None;
        }

        /// <summary>Files with this name under the active root but outside the import archive.</summary>
        private List<string> LiveFilesNamed(string fileName)
        {
            var live = new List<string>();
            string archive = ActivePath(NebulaLegacyDataMigrator.ImportFolder);
            if (!Directory.Exists(activeRoot)) return live;

            foreach (string file in Directory.GetFiles(activeRoot, fileName, SearchOption.AllDirectories))
                if (!NebulaPersistentPathResolver.IsWithin(archive, file))
                    live.Add(file);
            return live;
        }

        private static RuntimeInitializeLoadType PhaseOf(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"{type.Name}.{methodName} does not exist");

            var attribute = (RuntimeInitializeOnLoadMethodAttribute)Attribute.GetCustomAttribute(
                method, typeof(RuntimeInitializeOnLoadMethodAttribute));
            Assert.IsNotNull(attribute, $"{type.Name}.{methodName} is not a startup hook at all");
            return attribute.loadType;
        }

        private static string HashOf(string path)
        {
            Assert.IsTrue(File.Exists(path), "expected a file at " + path);
            using (var hasher = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hasher.ComputeHash(stream));
        }

        private static Dictionary<string, string> Fingerprint(string root)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(root)) return map;
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                map[file] = HashOf(file);
            return map;
        }
    }
}
