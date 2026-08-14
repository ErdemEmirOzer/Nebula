using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;

namespace Nebula.Platform
{
    /// <summary>What one migration attempt concluded. Recorded, not inferred from side effects.</summary>
    public enum LegacyMigrationOutcome
    {
        /// <summary>No attempt has been made in this process.</summary>
        NotAttempted = 0,

        /// <summary>Deliberately declined — see the note. Not a failure and not a success.</summary>
        Skipped,

        /// <summary>A valid marker was already present, so nothing was read or written.</summary>
        AlreadyMigrated,

        /// <summary>There is no legacy folder to bring across. Recorded so the next boot does not look again.</summary>
        NoLegacyData,

        /// <summary>
        /// The legacy folder is there and holds nothing this install needs — everything in
        /// it is already here, or is data this migration deliberately does not carry.
        /// Kept distinct from <see cref="NoLegacyData"/> because "there was no old folder"
        /// and "the old folder had nothing new in it" are different answers to give
        /// somebody asking where their save went.
        /// </summary>
        NothingToImport,

        /// <summary>Legacy data was copied and verified, and the marker was written.</summary>
        Migrated,

        /// <summary>Something went wrong. No marker was written; the next boot tries again.</summary>
        Failed,
    }

    /// <summary>
    /// What one migration attempt did, file by file. Serialised next to the marker so a
    /// player who asks "where did my old save go" can be answered from the machine
    /// rather than from a guess.
    /// </summary>
    [Serializable]
    public sealed class NebulaLegacyMigrationReport
    {
        public int migrationVersion;
        public string outcome = LegacyMigrationOutcome.NotAttempted.ToString();
        public string note = "";
        public string startedAtUtc = "";
        public string completedAtUtc = "";
        public string legacyRoot = "";
        public string activeRoot = "";

        /// <summary>Files placed into the live folders, as paths relative to the active root.</summary>
        public List<string> imported = new List<string>();

        /// <summary>Files preserved under <c>nebula/legacy-import/</c> instead of going live.</summary>
        public List<string> archived = new List<string>();

        /// <summary>Human-readable reasons a legacy file was kept back rather than imported.</summary>
        public List<string> conflicts = new List<string>();

        /// <summary>Entries the copier refused to touch at all — links, traversal, oversize, budget.</summary>
        public List<string> refused = new List<string>();

        /// <summary>The typed form of <see cref="outcome"/>. A property, so JsonUtility keeps one spelling of it on disk.</summary>
        public LegacyMigrationOutcome Outcome
        {
            get => Enum.TryParse(outcome, out LegacyMigrationOutcome parsed) ? parsed : LegacyMigrationOutcome.NotAttempted;
            set => outcome = value.ToString();
        }
    }

    /// <summary>
    /// The "this install has already taken its inheritance" flag. Tiny on purpose: the
    /// less it holds, the fewer ways it can be half-true.
    /// </summary>
    [Serializable]
    public sealed class NebulaLegacyMigrationMarker
    {
        public int migrationVersion;
        public string completedAtUtc = "";
        public string legacyRoot = "";
        public int importedFiles;
        public int archivedFiles;
    }

    /// <summary>
    /// Brings a player's data across when the app's identity changes.
    /// <para>
    /// Renaming <c>companyName</c>/<c>productName</c> moves
    /// <c>Application.persistentDataPath</c>, and everything this game persists lives
    /// under it: saves and their three backups, the input rebinds, the session log and
    /// the telemetry files. Without this the rename is indistinguishable from a wiped
    /// machine — Continue goes grey, the player's keyboard layout reverts to shipped
    /// defaults, and the old folder sits on disk being invisible.
    /// </para>
    /// <para>
    /// <b>It copies. It never moves, never deletes, and never writes a byte inside the
    /// legacy folder.</b> The old install stays exactly as it was, so the worst outcome
    /// of any bug in here is a redundant copy, never a lost campaign. That is also why
    /// there is no "clean up the old folder" step and no plan to add one.
    /// </para>
    /// <para>
    /// The order is temp → verify → commit → mark. Every file is copied into a staging
    /// folder <i>inside the destination volume</i>, checked against the source by length
    /// and SHA-256, and only then renamed into place; a rename within a volume is the
    /// one filesystem operation that is atomic, so a file is either absent or complete
    /// and never half. The marker is the very last write. A crash at any point leaves
    /// the legacy data untouched, leaves no marker, and leaves the next boot free to
    /// try again — and the retry is safe because every decision is made by comparing
    /// what is already there, so files that did land are recognised as identical and
    /// skipped.
    /// </para>
    /// <para>
    /// Nothing here touches the save schema, <c>PlayerPrefs</c> (of which this project
    /// has none) or campaign state. The marker is install-scoped, not campaign-scoped:
    /// starting a new campaign, deleting every slot or loading an old save all leave it
    /// exactly where it is, because "this install already imported the old folder" is a
    /// fact about the machine and not about a run.
    /// </para>
    /// </summary>
    public static class NebulaLegacyDataMigrator
    {
        /// <summary>
        /// Bumped only if the *shape* of what gets imported changes. A marker written by
        /// a different version is not honoured, so a later, wider import can run once on
        /// a machine that already took the narrow one.
        /// </summary>
        public const int CurrentMigrationVersion = 1;

        /// <summary>Everything this migration produces lives under one folder, so it is obvious what it did and trivial to ignore.</summary>
        public const string ImportFolder = "legacy-import";

        public const string MarkerFileName = "migration-complete.json";
        public const string ReportFileName = "import-report.json";

        private const string ConflictsFolder = "conflicts";
        private const string UnreadableFolder = "unreadable";
        private const string StagingPrefix = ".staging-";

        // Bounds. A startup task must have a worst case, and "however big the folder is"
        // is not one -- a stray 4 GB capture in the old folder must not turn launching
        // the game into a five-minute copy.
        private const int MaxFiles = 4096;
        private const long MaxFileBytes = 64L * 1024 * 1024;
        private const long MaxTotalBytes = 512L * 1024 * 1024;
        private const int MaxDepth = 12;

        /// <summary>
        /// Name shapes that are never a player's data: the save system's own temp file,
        /// OS droppings, crash dumps and captures. Excluding them is not tidiness — a
        /// <c>slot1.tmp.json</c> is by definition a write that had not been validated yet,
        /// and importing one would carry a half-written save into a clean install.
        /// </summary>
        private static readonly string[] ExcludedSuffixes =
        {
            ".tmp.json", ".tmp", ".temp", ".lock", ".dmp", ".crash", ".log",
            ".png", ".jpg", ".jpeg", ".mp4",
        };

        private static readonly string[] ExcludedNames = { ".DS_Store", "Thumbs.db", "desktop.ini" };

        /// <summary>Folder names that hold regenerable or foreign data, never a player's own record.</summary>
        private static readonly string[] ExcludedFolders = { "cache", "caches", "crashes", "screenshots", "unity", ImportFolder };

        /// <summary><c>slot3.bak2.json</c> and friends. Case-insensitive because the volume underneath may be.</summary>
        private static readonly Regex SlotFilePattern =
            new Regex(@"^slot(?<slot>\d{1,3})(?:\.bak(?<backup>\d{1,2}))?\.json$", RegexOptions.IgnoreCase);

        /// <summary>What the last attempt in this process concluded. Never null after startup.</summary>
        public static NebulaLegacyMigrationReport LastReport { get; private set; } =
            new NebulaLegacyMigrationReport { migrationVersion = CurrentMigrationVersion };

        /// <summary>
        /// True once the startup hook has run to completion in this process, whatever it
        /// concluded.
        /// <para>
        /// This is what the ordering test reads. Asserting the attribute literal would
        /// only prove the attribute is spelled the way the test spells it; asserting that
        /// this is already true when a <c>BeforeSceneLoad</c> method runs proves the
        /// migration finished before <i>anything</i> in that phase started, which is the
        /// property that actually matters.
        /// </para>
        /// </summary>
        public static bool StartupCompleted { get; private set; }

        /// <summary>
        /// Test seam: called once per file as the commit places it, with the path being
        /// placed. Throwing from it stops the commit exactly where a power cut would, so
        /// the "a half-finished migration leaves no marker and is re-runnable" claim can
        /// be observed instead of asserted. Left null in a build.
        /// </summary>
        internal static Action<string> CommitObserver { get; set; }

        /// <summary>
        /// Startup entry point, at the earliest phase Unity offers.
        /// <para>
        /// <c>SubsystemRegistration</c> and not <c>BeforeSceneLoad</c> because
        /// <see cref="NebulaInputBindingStore.LoadOnStartup"/> is itself a
        /// <c>BeforeSceneLoad</c> method, and Unity gives no order between two methods in
        /// the same phase — sharing a phase with the thing you must precede is a coin
        /// toss, not a design. <c>SubsystemRegistration</c> is the first phase the engine
        /// runs, strictly before <c>AfterAssembliesLoaded</c>, <c>BeforeSplashScreen</c>
        /// and <c>BeforeSceneLoad</c>, so every reader of persistent data — the binding
        /// store, the save service through the main menu, telemetry — is downstream of it
        /// by construction rather than by luck.
        /// </para>
        /// <para>
        /// It is also a once-per-process hook and not an <c>Update</c>: there is no state
        /// on disk that can start being stale mid-session, so re-scanning the filesystem
        /// per frame would buy nothing and cost a stat call on every frame of the game.
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RunAtStartup()
        {
            try
            {
                if (Application.isEditor)
                {
                    // The Editor's persistent folder is a developer's scratch identity, not
                    // a player's install, so there is nothing here to inherit. Returning
                    // before Compose is deliberate and not merely tidy: reading
                    // Application.persistentDataPath *creates* the folder, and an editor
                    // session has no business creating -- let alone importing into -- the
                    // shipped app's user data folder on somebody's machine.
                    LastReport = Conclude(NewReport(), LegacyMigrationOutcome.Skipped,
                        "editor host — migration is a player-install concern");
                    return;
                }

                Compose(NebulaPersistentPaths.CreateForRunningBuild());
            }
            catch (Exception e)
            {
                // A migration that throws must not stop the game from starting. The old
                // folder is untouched by definition, so the honest failure mode is
                // "launches without the old data" and never "does not launch".
                LastReport = Conclude(NewReport(), LegacyMigrationOutcome.Failed, "startup: " + e.Message);
                Debug.LogWarning("[Nebula] Legacy data migration failed at startup: " + e.Message);
            }
            finally
            {
                StartupCompleted = true;
            }
        }

        /// <summary>
        /// The one place migration is wired into startup. Startup calls it with the real
        /// pair of roots; tests call it with temp ones. Removing the <see cref="Migrate"/>
        /// call from here is what makes the "Continue survives the rename" test go red,
        /// which is the point of having a single named seam rather than an inline call.
        /// </summary>
        public static NebulaLegacyMigrationReport Compose(IPersistentPathProvider paths)
        {
            if (Application.isEditor && !BothRootsAreTemporary(paths))
            {
                // Hard interlock. Inside the Editor -- which is where every EditMode and
                // PlayMode test runs -- this refuses to work on anything but a temporary
                // folder. A test that pointed the migrator at the operator's real
                // Application Support folder would rewrite data no test is entitled to
                // touch, and "we were careful" is not a mechanism.
                LastReport = Conclude(NewReport(), LegacyMigrationOutcome.Skipped,
                    "editor host: refusing a root outside the temp directory");
                return LastReport;
            }

            LastReport = Migrate(paths);
            return LastReport;
        }

        /// <summary>
        /// Copies the legacy folder into the active one, once, and reports what it did.
        /// Safe to call twice: the second call reads the marker and returns without
        /// touching either folder.
        /// </summary>
        public static NebulaLegacyMigrationReport Migrate(IPersistentPathProvider paths)
        {
            NebulaLegacyMigrationReport report = NewReport();

            string active = NebulaPersistentPathResolver.Normalise(paths?.ActiveRoot);
            if (active == null)
                return Conclude(report, LegacyMigrationOutcome.Failed, "no active persistent root was supplied");
            report.activeRoot = active;

            string legacy = NebulaPersistentPathResolver.Normalise(paths.LegacyRoot);
            report.legacyRoot = legacy ?? "";

            if (HasValidMarker(active, out NebulaLegacyMigrationMarker existing))
            {
                report.completedAtUtc = existing.completedAtUtc;
                return Conclude(report, LegacyMigrationOutcome.AlreadyMigrated,
                    "marker written " + existing.completedAtUtc, writeArtifacts: false);
            }

            if (legacy == null)
                return Complete(report, LegacyMigrationOutcome.NoLegacyData,
                    "this platform claims no legacy data root");

            if (NebulaPersistentPathResolver.SamePath(legacy, active))
                return Complete(report, LegacyMigrationOutcome.NoLegacyData,
                    "the legacy root is the active root — the identity did not move");

            // Nested roots would make the destination part of the source tree, so a copy
            // could feed itself. Refused rather than special-cased: no real identity
            // change produces this shape, so meeting it means the inputs are wrong.
            if (NebulaPersistentPathResolver.IsWithin(legacy, active) ||
                NebulaPersistentPathResolver.IsWithin(active, legacy))
                return Conclude(report, LegacyMigrationOutcome.Failed,
                    "the legacy and active roots are nested; refusing to copy a tree into itself");

            string legacyPayload = Path.Combine(legacy, NebulaAppIdentity.PayloadFolder);
            if (!Directory.Exists(legacyPayload))
                return Complete(report, LegacyMigrationOutcome.NoLegacyData,
                    "no legacy data folder at " + legacyPayload);

            var plan = new List<PlannedCopy>();
            var budget = new Budget();
            try
            {
                PlanSaves(legacyPayload, active, plan, budget, report);
                PlanSettings(legacyPayload, active, plan, budget, report);
                PlanArchiveOnly(legacyPayload, active, "logs", plan, budget, report);
                PlanArchiveOnly(legacyPayload, active, "telemetry", plan, budget, report);
            }
            catch (Exception e)
            {
                return Conclude(report, LegacyMigrationOutcome.Failed, "planning: " + e.Message);
            }

            if (plan.Count == 0)
                return Complete(report, LegacyMigrationOutcome.NothingToImport,
                    "the legacy folder holds nothing this install is missing");

            string staging = Path.Combine(active, NebulaAppIdentity.PayloadFolder, ImportFolder,
                StagingPrefix + Guid.NewGuid().ToString("N"));
            try
            {
                Stage(plan, staging, active);
                Commit(plan, staging, active, report);
            }
            catch (Exception e)
            {
                DiscardStaging(staging);
                return Conclude(report, LegacyMigrationOutcome.Failed, e.Message);
            }

            DiscardStaging(staging);
            return Complete(report, LegacyMigrationOutcome.Migrated,
                $"imported {report.imported.Count}, archived {report.archived.Count}");
        }

        // ---- planning ---------------------------------------------------------

        /// <summary>One file the migration intends to place, and where.</summary>
        private sealed class PlannedCopy
        {
            public string SourcePath;

            /// <summary>Destination relative to the active root. Kept relative so staging and the final tree share one layout.</summary>
            public string RelativeDestination;

            /// <summary>True when it lands in a live folder; false when it lands in the archive.</summary>
            public bool Live;
        }

        /// <summary>Running totals, so the bounds apply across the whole plan rather than per folder.</summary>
        private sealed class Budget
        {
            public int Files;
            public long Bytes;
        }

        /// <summary>
        /// Saves, planned a slot at a time rather than a file at a time.
        /// <para>
        /// The unit is the slot family — <c>slotN.json</c> plus its three backups —
        /// because <see cref="SaveService.Load"/> falls back through the backups when the
        /// primary will not read. Mixing one campaign's primary with another campaign's
        /// backups therefore does not produce a messy folder, it produces a slot that
        /// silently resumes a different campaign the first time the primary is damaged.
        /// So the primary decides: if the active install has one for this slot and it is
        /// not byte-identical to the legacy one, the slot belongs to the active install
        /// and the entire legacy family is preserved instead of merged. A slot with no
        /// active primary is free, and the legacy family fills whatever names are empty —
        /// which is also what makes a migration interrupted halfway able to finish on the
        /// next boot rather than reclassify its own half-finished work as a conflict.
        /// </para>
        /// </summary>
        private static void PlanSaves(string legacyPayload, string active, List<PlannedCopy> plan,
            Budget budget, NebulaLegacyMigrationReport report)
        {
            string legacyDir = Path.Combine(legacyPayload, "saves");
            if (!Directory.Exists(legacyDir)) return;

            string activeDir = Path.Combine(active, NebulaAppIdentity.PayloadFolder, "saves");
            var families = new Dictionary<int, List<string>>();

            foreach (string file in ReadableFilesIn(legacyDir, report))
            {
                string name = Path.GetFileName(file);
                Match match = SlotFilePattern.Match(name);
                if (!match.Success)
                {
                    // A save-folder file this build does not recognise -- an index or a
                    // metadata sidecar a later version added. Carried across when the
                    // active folder has no file by that name, kept back when it does:
                    // we cannot merge a format we cannot read, and overwriting one would
                    // be worse than not having it.
                    PlanOpaque(file, Path.Combine(activeDir, name), "saves/" + name, plan, budget, report);
                    continue;
                }

                int slot = int.Parse(match.Groups["slot"].Value);
                if (!families.TryGetValue(slot, out List<string> family))
                    families[slot] = family = new List<string>();
                family.Add(file);
            }

            foreach (KeyValuePair<int, List<string>> entry in families)
                PlanSlotFamily(entry.Key, entry.Value, activeDir, plan, budget, report);
        }

        private static void PlanSlotFamily(int slot, List<string> legacyFiles, string activeDir,
            List<PlannedCopy> plan, Budget budget, NebulaLegacyMigrationReport report)
        {
            string primaryName = $"slot{slot}.json";
            string activeMain = Path.Combine(activeDir, primaryName);
            string legacyMain = null;
            foreach (string file in legacyFiles)
                if (string.Equals(Path.GetFileName(file), primaryName, StringComparison.OrdinalIgnoreCase))
                    legacyMain = file;

            // No active primary means the slot is free, whatever else is lying in it.
            bool slotIsFree = !File.Exists(activeMain);
            bool sameCampaign = slotIsFree || (legacyMain != null && SameContents(legacyMain, activeMain));

            foreach (string file in legacyFiles)
            {
                string name = Path.GetFileName(file);
                string destination = Path.Combine(activeDir, name);
                string relative = $"{NebulaAppIdentity.PayloadFolder}/saves/{name}";

                if (!sameCampaign)
                {
                    // Different campaign, or a primary we cannot compare. New data is
                    // authoritative, so the whole legacy family is preserved rather than
                    // merged, under a name that says which slot it came from.
                    Archive(file, ConflictsFolder + "/saves/" + name, plan, budget, report,
                        $"slot {slot}: the active install already holds this slot with different content");
                    continue;
                }

                if (File.Exists(destination))
                {
                    // Same campaign and the file is already here. Byte-identical is the
                    // common case on a re-run after an interrupted migration.
                    if (SameContents(file, destination)) continue;
                    Archive(file, ConflictsFolder + "/saves/" + name, plan, budget, report,
                        $"{name}: present in the active install with different content");
                    continue;
                }

                // Parseable JSON is not enough. This is SaveService's own reader --
                // envelope shape, payload hash and the newer-schema refusal -- so a
                // truncated or hand-edited legacy file is recognised by exactly the rule
                // that would reject it at load time, and is preserved instead of installed.
                if (!SaveService.IsReadableSaveFile(file))
                {
                    Archive(file, UnreadableFolder + "/saves/" + name, plan, budget, report,
                        $"{name}: does not pass the save reader, so it is preserved rather than installed");
                    continue;
                }

                Add(plan, budget, report, file, relative, live: true);
            }
        }

        /// <summary>
        /// User settings. The rebinds get the binding store's own validation; anything
        /// else in the folder is carried opaquely, because a setting this build has never
        /// heard of is still the player's.
        /// </summary>
        private static void PlanSettings(string legacyPayload, string active, List<PlannedCopy> plan,
            Budget budget, NebulaLegacyMigrationReport report)
        {
            string legacyDir = Path.Combine(legacyPayload, "settings");
            if (!Directory.Exists(legacyDir)) return;

            string activeDir = Path.Combine(active, NebulaAppIdentity.PayloadFolder, "settings");
            const string bindings = "input-bindings.json";

            foreach (string file in ReadableFilesIn(legacyDir, report))
            {
                string name = Path.GetFileName(file);
                string destination = Path.Combine(activeDir, name);

                if (!string.Equals(name, bindings, StringComparison.OrdinalIgnoreCase))
                {
                    PlanOpaque(file, destination, "settings/" + name, plan, budget, report);
                    continue;
                }

                if (File.Exists(destination))
                {
                    if (SameContents(file, destination)) continue;

                    // The player has already rebound keys on this install. Those are the
                    // ones in front of them, so those win; settingsVersion is deliberately
                    // untouched -- the file's shape did not change, only which copy is kept.
                    Archive(file, ConflictsFolder + "/settings/" + name, plan, budget, report,
                        "input bindings: the active install already has a layout, which wins");
                    continue;
                }

                if (!IsReadableBindingFile(file))
                {
                    Archive(file, UnreadableFolder + "/settings/" + name, plan, budget, report,
                        "input bindings: unreadable or written by a settings version this build does not read");
                    continue;
                }

                Add(plan, budget, report, file, $"{NebulaAppIdentity.PayloadFolder}/settings/{name}", live: true);
            }
        }

        /// <summary>
        /// Logs and telemetry: preserved whole, never placed live.
        /// <para>
        /// A session log is an append-only record and the telemetry funnel index is a
        /// dedup authority. Concatenating two logs would invent a session history that
        /// never happened, and merging two funnel indexes would produce a second authority
        /// disagreeing with the first about what has already been reported. Neither is
        /// worth the player's old data being destroyed by the alternative, so both are
        /// kept, readable, under the archive — and the active install's own files stay the
        /// only ones anything reads.
        /// </para>
        /// </summary>
        private static void PlanArchiveOnly(string legacyPayload, string active, string folder,
            List<PlannedCopy> plan, Budget budget, NebulaLegacyMigrationReport report)
        {
            string legacyDir = Path.Combine(legacyPayload, folder);
            if (!Directory.Exists(legacyDir)) return;

            foreach (string file in ReadableFilesUnder(legacyDir, legacyDir, 0, report))
            {
                string relative = RelativeUnder(legacyDir, file);
                Archive(file, folder + "/" + relative, plan, budget, report, null);
            }
        }

        /// <summary>A file whose format this build does not know: imported only into a gap, archived on any clash.</summary>
        private static void PlanOpaque(string source, string destination, string relativeLabel,
            List<PlannedCopy> plan, Budget budget, NebulaLegacyMigrationReport report)
        {
            if (File.Exists(destination))
            {
                if (SameContents(source, destination)) return;
                Archive(source, ConflictsFolder + "/" + relativeLabel, plan, budget, report,
                    relativeLabel + ": present in the active install with different content");
                return;
            }

            Add(plan, budget, report, source, NebulaAppIdentity.PayloadFolder + "/" + relativeLabel, live: true);
        }

        private static void Archive(string source, string relativeInsideImport, List<PlannedCopy> plan,
            Budget budget, NebulaLegacyMigrationReport report, string conflictNote)
        {
            if (conflictNote != null) report.conflicts.Add(conflictNote);
            Add(plan, budget, report,
                source,
                $"{NebulaAppIdentity.PayloadFolder}/{ImportFolder}/{relativeInsideImport}",
                live: false);
        }

        private static void Add(List<PlannedCopy> plan, Budget budget, NebulaLegacyMigrationReport report,
            string source, string relativeDestination, bool live)
        {
            long length;
            try { length = new FileInfo(source).Length; }
            catch (Exception e) { report.refused.Add($"{source}: cannot be measured ({e.Message})"); return; }

            if (length > MaxFileBytes)
            {
                report.refused.Add($"{source}: {length} bytes exceeds the {MaxFileBytes}-byte per-file limit");
                return;
            }
            if (budget.Files + 1 > MaxFiles || budget.Bytes + length > MaxTotalBytes)
            {
                report.refused.Add($"{source}: the migration budget ({MaxFiles} files / {MaxTotalBytes} bytes) is spent");
                return;
            }

            budget.Files++;
            budget.Bytes += length;
            plan.Add(new PlannedCopy { SourcePath = source, RelativeDestination = relativeDestination, Live = live });
        }

        // ---- staging and commit ------------------------------------------------

        /// <summary>
        /// Copies every planned file into the staging folder and proves each copy.
        /// <para>
        /// Length and SHA-256, not length alone: a truncated copy is caught by length, but
        /// a copy that failed mid-block on a failing disk is the same length and different
        /// bytes, and a save whose payload hash no longer matches its envelope is a save
        /// the loader will refuse. Bytes are copied verbatim — a save is never parsed and
        /// re-serialised, so an identical file stays identical down to its whitespace and
        /// its <c>payloadHash</c> still describes it.
        /// </para>
        /// </summary>
        private static void Stage(List<PlannedCopy> plan, string staging, string active)
        {
            Directory.CreateDirectory(staging);

            foreach (PlannedCopy item in plan)
            {
                string stagedPath = Path.Combine(staging, item.RelativeDestination);
                if (!NebulaPersistentPathResolver.IsWithin(staging, stagedPath))
                    throw new InvalidOperationException("staged path escapes the staging folder: " + item.RelativeDestination);
                if (!NebulaPersistentPathResolver.IsWithin(active, Path.Combine(active, item.RelativeDestination)))
                    throw new InvalidOperationException("destination escapes the active root: " + item.RelativeDestination);

                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath) ?? staging);
                File.Copy(item.SourcePath, stagedPath, overwrite: false);

                var source = new FileInfo(item.SourcePath);
                var staged = new FileInfo(stagedPath);
                if (source.Length != staged.Length)
                    throw new IOException($"copy of {item.SourcePath} is {staged.Length} bytes, source is {source.Length}");
                if (!string.Equals(Sha256(item.SourcePath), Sha256(stagedPath), StringComparison.Ordinal))
                    throw new IOException("copy of " + item.SourcePath + " does not hash to the source");
            }
        }

        /// <summary>
        /// Renames each verified file into its final place.
        /// <para>
        /// A rename inside one volume is atomic, which is why staging sits under the
        /// active root rather than in the OS temp directory: across volumes the runtime
        /// falls back to copy-then-delete and the atomicity this depends on quietly stops
        /// existing. A destination that appeared since planning is never overwritten — it
        /// is recorded as a conflict and the legacy copy is left in staging, which is
        /// discarded, so the legacy original remains the only copy of it and is still
        /// where it always was.
        /// </para>
        /// </summary>
        private static void Commit(List<PlannedCopy> plan, string staging, string active,
            NebulaLegacyMigrationReport report)
        {
            foreach (PlannedCopy item in plan)
            {
                string stagedPath = Path.Combine(staging, item.RelativeDestination);
                if (!File.Exists(stagedPath)) continue;

                string finalPath = Path.Combine(active, item.RelativeDestination);
                if (!NebulaPersistentPathResolver.IsWithin(active, finalPath))
                    throw new InvalidOperationException("destination escapes the active root: " + item.RelativeDestination);

                CommitObserver?.Invoke(finalPath);

                Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? active);

                if (File.Exists(finalPath))
                {
                    if (item.Live)
                    {
                        report.conflicts.Add(item.RelativeDestination +
                            ": appeared in the active install after planning, so the legacy copy was not placed");
                        continue;
                    }

                    // An archive slot that is already taken. Identical means a previous
                    // interrupted run already preserved it; different means we would be
                    // destroying an earlier preservation, so it gets its own name.
                    if (SameContents(stagedPath, finalPath)) continue;
                    finalPath = NextFreeName(finalPath);
                }

                File.Move(stagedPath, finalPath);
                string recorded = RelativeUnder(active, finalPath);
                if (item.Live) report.imported.Add(recorded);
                else report.archived.Add(recorded);
            }
        }

        private static void DiscardStaging(string staging)
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception e) { Debug.LogWarning("[Nebula] Could not remove migration staging folder: " + e.Message); }
        }

        // ---- marker and report -------------------------------------------------

        /// <summary>Where the "already done" flag lives for a given active root.</summary>
        public static string MarkerPath(string activeRoot) =>
            Path.Combine(activeRoot, NebulaAppIdentity.PayloadFolder, ImportFolder, MarkerFileName);

        /// <summary>Where the file-by-file record of the last completed migration lives.</summary>
        public static string ReportPath(string activeRoot) =>
            Path.Combine(activeRoot, NebulaAppIdentity.PayloadFolder, ImportFolder, ReportFileName);

        /// <summary>
        /// True when this install has a marker it can trust.
        /// <para>
        /// Unreadable, empty or written by a different migration version all count as
        /// "no marker", which re-runs the inspection. That is safe by construction: a
        /// re-run compares before it copies, so on a machine that really was migrated it
        /// finds every file already present and identical and copies nothing. Nothing is
        /// ever deleted on the strength of a marker, so a corrupt one cannot cost data —
        /// at worst it costs one directory walk at startup.
        /// </para>
        /// </summary>
        public static bool HasValidMarker(string activeRoot, out NebulaLegacyMigrationMarker marker)
        {
            marker = null;
            try
            {
                string path = MarkerPath(activeRoot);
                if (!File.Exists(path)) return false;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;

                marker = JsonUtility.FromJson<NebulaLegacyMigrationMarker>(json);
                if (marker == null || marker.migrationVersion != CurrentMigrationVersion) { marker = null; return false; }
                return true;
            }
            catch (Exception)
            {
                marker = null;
                return false;
            }
        }

        private static NebulaLegacyMigrationReport NewReport() => new NebulaLegacyMigrationReport
        {
            migrationVersion = CurrentMigrationVersion,
            startedAtUtc = DateTime.UtcNow.ToString("o"),
        };

        /// <summary>Finishes a run that got far enough to be worth remembering: report first, then the marker.</summary>
        private static NebulaLegacyMigrationReport Complete(NebulaLegacyMigrationReport report,
            LegacyMigrationOutcome outcome, string note) =>
            Conclude(report, outcome, note, writeArtifacts: true);

        private static NebulaLegacyMigrationReport Conclude(NebulaLegacyMigrationReport report,
            LegacyMigrationOutcome outcome, string note, bool writeArtifacts = false)
        {
            report.Outcome = outcome;
            report.note = note ?? "";
            report.completedAtUtc = string.IsNullOrEmpty(report.completedAtUtc)
                ? DateTime.UtcNow.ToString("o")
                : report.completedAtUtc;

            if (writeArtifacts && !string.IsNullOrEmpty(report.activeRoot))
            {
                try
                {
                    Directory.CreateDirectory(Path.Combine(report.activeRoot, NebulaAppIdentity.PayloadFolder, ImportFolder));
                    File.WriteAllText(ReportPath(report.activeRoot), JsonUtility.ToJson(report, true));

                    // Last write of the whole operation, and only reached once every copy
                    // above has been verified and renamed into place. Anything that moves
                    // this earlier turns "the marker means the data is here" into "the
                    // marker means somebody started".
                    var marker = new NebulaLegacyMigrationMarker
                    {
                        migrationVersion = CurrentMigrationVersion,
                        completedAtUtc = report.completedAtUtc,
                        legacyRoot = report.legacyRoot,
                        importedFiles = report.imported.Count,
                        archivedFiles = report.archived.Count,
                    };
                    File.WriteAllText(MarkerPath(report.activeRoot), JsonUtility.ToJson(marker, true));
                }
                catch (Exception e)
                {
                    report.Outcome = LegacyMigrationOutcome.Failed;
                    report.note = "could not record the migration: " + e.Message;
                }
            }

            return report;
        }

        // ---- filesystem helpers ------------------------------------------------

        /// <summary>Files directly inside a folder that this migration is willing to read.</summary>
        private static IEnumerable<string> ReadableFilesIn(string directory, NebulaLegacyMigrationReport report)
        {
            string[] entries;
            try { entries = Directory.GetFiles(directory); }
            catch (Exception e) { report.refused.Add($"{directory}: cannot be listed ({e.Message})"); yield break; }

            Array.Sort(entries, StringComparer.Ordinal);
            foreach (string entry in entries)
            {
                if (IsExcludedFile(entry)) continue;
                if (IsLink(entry))
                {
                    report.refused.Add(entry + ": is a link, and links are not followed");
                    continue;
                }
                yield return entry;
            }
        }

        /// <summary>The same, recursively, with a depth cap so a link cycle cannot become an infinite walk.</summary>
        private static IEnumerable<string> ReadableFilesUnder(string root, string directory, int depth,
            NebulaLegacyMigrationReport report)
        {
            if (depth > MaxDepth)
            {
                report.refused.Add(directory + $": deeper than the {MaxDepth}-level limit");
                yield break;
            }

            foreach (string file in ReadableFilesIn(directory, report)) yield return file;

            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch (Exception e) { report.refused.Add($"{directory}: cannot be listed ({e.Message})"); yield break; }

            Array.Sort(children, StringComparer.Ordinal);
            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (Array.IndexOf(ExcludedFolders, name.ToLowerInvariant()) >= 0) continue;
                if (IsLink(child))
                {
                    report.refused.Add(child + ": is a linked directory, and links are not followed");
                    continue;
                }
                if (!NebulaPersistentPathResolver.IsWithin(root, child))
                {
                    report.refused.Add(child + ": resolves outside the folder being migrated");
                    continue;
                }

                foreach (string file in ReadableFilesUnder(root, child, depth + 1, report)) yield return file;
            }
        }

        /// <summary>
        /// A link is refused, not resolved.
        /// <para>
        /// Following one and then checking where it landed means the check happens after
        /// the read, and the only reason a link would be sitting inside a save folder is
        /// to make a copier read something it was not pointed at. There is no legitimate
        /// use for one here, so the cheap, total answer is the right one: refuse every
        /// link, escaping or not, and say so in the report. The escaping case — the one
        /// this is actually defending against — is covered by construction.
        /// </para>
        /// </summary>
        private static bool IsLink(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch (Exception) { return true; }   // unreadable attributes: treat as unsafe
        }

        private static bool IsExcludedFile(string path)
        {
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return true;
            if (name.StartsWith(StagingPrefix, StringComparison.Ordinal)) return true;
            if (Array.IndexOf(ExcludedNames, name) >= 0) return true;

            string lower = name.ToLowerInvariant();
            foreach (string suffix in ExcludedSuffixes)
                if (lower.EndsWith(suffix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Byte equality, decided by length first and hash second so the common "different size" case costs one stat.</summary>
        private static bool SameContents(string left, string right)
        {
            try
            {
                var a = new FileInfo(left);
                var b = new FileInfo(right);
                if (!a.Exists || !b.Exists) return false;
                if (a.Length != b.Length) return false;
                return string.Equals(Sha256(left), Sha256(right), StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }

        private static string Sha256(string path)
        {
            using (var hasher = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] digest = hasher.ComputeHash(stream);
                var text = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        /// <summary><c>slot1.legacy.json</c> → <c>slot1.legacy-2.json</c>, so an archive slot is never overwritten.</summary>
        private static string NextFreeName(string path)
        {
            string directory = Path.GetDirectoryName(path) ?? "";
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int index = 2; index < 1000; index++)
            {
                string candidate = Path.Combine(directory, $"{stem}-{index}{extension}");
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException("no free archive name for " + path);
        }

        private static string RelativeUnder(string root, string path)
        {
            string normalisedRoot = NebulaPersistentPathResolver.Normalise(root) ?? root;
            string normalisedPath = NebulaPersistentPathResolver.Normalise(path) ?? path;
            if (normalisedPath.Length <= normalisedRoot.Length) return Path.GetFileName(normalisedPath);
            return normalisedPath.Substring(normalisedRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>The binding store's own contract: readable, and a settings version this build actually reads.</summary>
        private static bool IsReadableBindingFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;
                var document = JsonUtility.FromJson<InputBindingDocument>(json);
                return document != null && document.settingsVersion == NebulaInputBindingStore.CurrentVersion;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Both roots sit under a temp directory this host would hand out. The Editor
        /// interlock; see <see cref="Compose"/> for why it exists.
        /// </summary>
        private static bool BothRootsAreTemporary(IPersistentPathProvider paths)
        {
            if (paths == null) return false;
            return IsTemporary(paths.ActiveRoot) && IsTemporary(paths.LegacyRoot);
        }

        private static bool IsTemporary(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // "/tmp" and "/private/tmp" are named as well as Path.GetTempPath() because on
            // macOS the former is a symlink to the latter and GetTempPath() returns neither
            // -- it returns the per-process TMPDIR under /var/folders. Normalising does not
            // resolve the symlink, so all three spellings have to be listed to be accepted.
            var roots = new List<string> { Path.GetTempPath() };
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
            {
                roots.Add("/tmp");
                roots.Add("/private/tmp");
                roots.Add("/private/var/folders");
                roots.Add("/var/folders");
            }

            foreach (string root in roots)
                if (NebulaPersistentPathResolver.IsWithin(root, path)) return true;
            return false;
        }
    }
}
