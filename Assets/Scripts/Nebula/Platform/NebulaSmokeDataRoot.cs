using System;
using System.IO;
using UnityEngine;

namespace Nebula.Platform
{
    /// <summary>
    /// Redirects everything this game persists into a throwaway sandbox, for the
    /// duration of an automated smoke run, and for nothing else.
    /// <para>
    /// <b>This exists because of a real incident, not a hypothetical one.</b> A smoke
    /// run of the Development player was started with <c>-batchmode -nographics</c> and
    /// no data redirection. It booted, migrated, started a campaign and exported
    /// telemetry — all against the operator's own live folders. It rewrote
    /// <c>~/Library/Application Support/DefaultCompany/Nebula/nebula/telemetry/funnel-index.json</c>,
    /// and because only that file's hash had been recorded and not its contents, the
    /// original could not be put back. The saves happened to survive byte-identical,
    /// which was luck rather than design: the same run holds a live
    /// <c>SaveService</c> pointed at the same folder.
    /// </para>
    /// <para>
    /// The lesson taken from it is that "the harness will pass the right flags" is not a
    /// mechanism. A run that forgets to redirect must fail closed by writing into a
    /// sandbox or not run at all — it must never quietly fall back to the real folder.
    /// So the redirection is not something the harness *applies*; it is something every
    /// persistence seam *asks for*, before it computes a path.
    /// </para>
    ///
    /// <para><b>Why a lazily-read static and not a startup hook.</b>
    /// The obvious shape — a <c>RuntimeInitializeOnLoadMethod</c> that assigns the four
    /// <c>DirectoryOverride</c> properties — loses to ordering. Unity gives no order
    /// between two methods in the same initialisation phase, and
    /// <see cref="NebulaLegacyDataMigrator"/> already occupies the earliest phase there
    /// is (<c>SubsystemRegistration</c>). An override applied in that same phase would
    /// win or lose by coin toss, and losing means the migration runs against the real
    /// folder — which is precisely the incident above. Resolved once, on first read,
    /// there is no window: whoever asks first triggers the resolution, and every asker
    /// after that gets the same answer.
    /// </para>
    ///
    /// <para><b>The gates, and why each one is load-bearing.</b>
    /// Every one of these must hold. They are ordered cheapest-first, and the first
    /// failure is recorded in <see cref="RefusalReason"/> so a harness that thinks it
    /// enabled isolation and did not can be told exactly which gate it missed.
    /// <list type="number">
    /// <item><description><b>Player host.</b> The Editor is refused outright. Editor
    /// tests already have the <c>DirectoryOverride</c> seams and the migrator's own
    /// temp-root interlock; a second mechanism there would be a second thing to get
    /// wrong.</description></item>
    /// <item><description><b><c>-nebulaSmoke</c> present.</b> The same flag the smoke
    /// runner gates on, so isolation and the smoke cannot be enabled
    /// independently.</description></item>
    /// <item><description><b><c>-nebulaDataRoot &lt;path&gt;</c> present.</b> Explicit
    /// and never defaulted. There is no "pick a temp folder for me" branch: a default
    /// would mean a run that meant to use the real folder silently stopped doing so,
    /// which is the same class of surprise in the other direction.</description></item>
    /// <item><description><b>The path is absolute and free of relative segments.</b>
    /// Rejected, not normalised away. A caller passing <c>../..</c> has a broken idea of
    /// where it is writing, and quietly repairing the path hides that.</description></item>
    /// <item><description><b>The path lies under a permitted temp root, and is not the
    /// temp root itself.</b> <c>/tmp</c> as the data root would put the sandbox's
    /// cleanup one <c>rm -rf</c> away from the whole temp directory.</description></item>
    /// <item><description><b>No component of the path is a symbolic link.</b>
    /// Normalisation does not resolve links, so <c>/tmp/root</c> can satisfy every check
    /// above while pointing at the operator's home directory. This is the one gate that
    /// has to look at the filesystem rather than at the string.</description></item>
    /// <item><description><b>The root already exists and carries
    /// <see cref="MarkerFileName"/>.</b> The interlock that makes the other gates hard
    /// to defeat by accident: the sandbox must have been deliberately created and
    /// stamped by a harness. A stray <c>-nebulaDataRoot /tmp/x</c> on a real player
    /// activates nothing, because nobody stamped <c>/tmp/x</c>.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para><b>What "inert" means here.</b> In a normally launched player none of this
    /// runs beyond one <c>Array.IndexOf</c> over the command line, once, the first time
    /// something needs a path. <see cref="Root"/> is <c>null</c>, every
    /// <c>Folder(...)</c> call is <c>null</c>, and the <c>??</c> chains in
    /// <c>SaveService</c>, <c>TelemetryService</c>, <c>SessionLog</c> and
    /// <c>NebulaInputBindingStore</c> fall through to
    /// <c>Application.persistentDataPath</c> exactly as before. The engine property is
    /// not even read on the sandboxed path — <c>??</c> short-circuits — which matters
    /// because reading it *creates* the real folder.
    /// </para>
    ///
    /// <para><b>Not a game feature.</b> There is deliberately no save flag, no campaign
    /// flag and no setting anywhere that says "this is a test run". The game's own code
    /// cannot observe that it is sandboxed; it only ever asks for a folder and is given
    /// one. A save written under the sandbox is a completely ordinary save that happens
    /// to be somewhere else.
    /// </para>
    /// </summary>
    public static class NebulaSmokeDataRoot
    {
        /// <summary>The smoke's own gate, restated here so the two cannot be enabled apart.</summary>
        public const string SmokeFlag = "-nebulaSmoke";

        /// <summary>Takes one argument: the absolute sandbox root.</summary>
        public const string RootFlag = "-nebulaDataRoot";

        /// <summary>
        /// The harness's stamp. Its presence is the claim "I created this folder for a
        /// smoke run and I am willing to delete it afterwards"; its absence means the
        /// path is somebody's real data and is left alone.
        /// </summary>
        public const string MarkerFileName = "nebula-smoke-root.marker";

        /// <summary>Stands in for <c>persistentDataPath</c> inside the sandbox.</summary>
        private const string ActiveFolderName = "active";

        /// <summary>Stands in for the previous identity's per-app root inside the sandbox.</summary>
        private const string LegacyFolderName = "legacy";

        private static bool resolved;
        private static string root;
        private static string refusal = "not resolved yet";
        private static bool demandedButRefused;

        /// <summary>
        /// The sandbox root, or <c>null</c> — which is the answer in every normal player.
        /// Resolved once per process, on first read.
        /// </summary>
        public static string Root
        {
            get { Resolve(); return root; }
        }

        /// <summary>True when persistence is sandboxed. False in every shipped launch.</summary>
        public static bool Active => Root != null;

        /// <summary>
        /// Why isolation is off, in the words of the gate that refused. Always populated,
        /// including on the ordinary "no <c>-nebulaSmoke</c> flag" path, so a harness
        /// that believes it enabled isolation can be shown which gate it missed rather
        /// than left to guess.
        /// </summary>
        public static string RefusalReason
        {
            get { Resolve(); return refusal; }
        }

        /// <summary>
        /// What <c>Application.persistentDataPath</c> is replaced by, or <c>null</c>.
        /// A child of the sandbox rather than the sandbox itself, so the run's own
        /// artefacts (its marker, its reports) sit beside the data instead of inside it
        /// and cannot be mistaken for something the game wrote.
        /// </summary>
        public static string ActiveRoot => Active ? Path.Combine(root, ActiveFolderName) : null;

        /// <summary>
        /// The stand-in for the previous identity's folder, or <c>null</c>.
        /// <para>
        /// Redirecting this is the whole reason the migration is safe to exercise. The
        /// real legacy root is the operator's <c>DefaultCompany/Nebula</c> folder — the
        /// exact folder the incident damaged — and a migration pointed at it reads it on
        /// every boot. Pointed here instead, a harness can seed whatever legacy shape it
        /// wants to test and the real folder is never opened.
        /// </para>
        /// </summary>
        public static string LegacyRoot => Active ? Path.Combine(root, LegacyFolderName) : null;

        /// <summary>
        /// True when the command line asked for a smoke run and isolation could not be
        /// granted. The state in which nothing may write anywhere.
        /// <para>
        /// This is the lesson of the incident stated as a variable. "Isolation is off" and
        /// "isolation was requested and refused" look identical from inside a save, and
        /// treating them the same is what let a run that meant to be sandboxed proceed
        /// against the operator's live folders. They are now different states, and the
        /// second one is fatal rather than merely noted.
        /// </para>
        /// </summary>
        public static bool IsolationDemandedButRefused
        {
            get { Resolve(); return demandedButRefused; }
        }

        /// <summary>
        /// The sandboxed equivalent of <c>persistentDataPath/nebula/&lt;leaf&gt;</c>, or
        /// <c>null</c> when isolation is off — which is what makes it usable as the left
        /// operand of a <c>??</c> chain without disturbing the normal path.
        /// <para>
        /// <b>Throws</b> when isolation was demanded and refused. Returning <c>null</c>
        /// there would let the <c>??</c> chain fall through to
        /// <c>Application.persistentDataPath</c>, which is precisely the fall-through that
        /// damaged a real telemetry file: the run asked to be sandboxed, was told it could
        /// not be, and carried on writing to the player's own folder anyway. A throw is the
        /// only answer that cannot be ignored by a caller that was written before this
        /// existed.
        /// </para>
        /// </summary>
        public static string Folder(string leaf)
        {
            ThrowIfDemandedButRefused("a persistent-data folder (" + leaf + ")");
            return Active ? Path.Combine(ActiveRoot, NebulaAppIdentity.PayloadFolder, leaf) : null;
        }

        /// <summary>
        /// The sandbox's pair of roots for <see cref="NebulaLegacyDataMigrator"/>, or
        /// <c>null</c> so the caller falls back to the real pair. Throws in the
        /// demanded-but-refused state for the reason <see cref="Folder"/> does — the
        /// migration is the one caller that would go on to <i>read</i> the operator's old
        /// install as well as write to the new one.
        /// </summary>
        public static IPersistentPathProvider PathsOrNull()
        {
            ThrowIfDemandedButRefused("the persistent-data roots for the legacy migration");
            return Active ? new NebulaPersistentPaths(LegacyRoot, ActiveRoot) : null;
        }

        private static void ThrowIfDemandedButRefused(string what)
        {
            if (!IsolationDemandedButRefused) return;
            throw new InvalidOperationException(
                "[Nebula] " + SmokeFlag + " was passed but persistent-data isolation was refused (" +
                refusal + "). Refusing to hand out " + what + ": an unisolated smoke run has " +
                "already destroyed a real telemetry file on this machine once. Fix the " +
                RootFlag + " argument, or launch without " + SmokeFlag + ".");
        }

        /// <summary>
        /// Where a smoke run's own report belongs when isolation is on: inside the
        /// sandbox, so <c>-nebulaSmokeOutput</c> defaulting to
        /// <c>persistentDataPath</c> cannot drop a file into the player's real folder.
        /// </summary>
        public static string ReportPath(string fileName) =>
            Active ? Path.Combine(root, fileName) : null;

        /// <summary>
        /// Test seam. Puts the resolver back to "never asked", so a fixture can drive
        /// <see cref="TryResolve"/> repeatedly with different command lines. Not called
        /// by the game, which resolves exactly once and never changes its mind.
        /// </summary>
        internal static void ResetForTests()
        {
            resolved = false;
            root = null;
            refusal = "not resolved yet";
            demandedButRefused = false;
        }

        /// <summary>
        /// Brings the resolution forward to the earliest moment the engine offers, so a
        /// run that cannot be isolated stops <i>before</i> the first frame rather than at
        /// the first write.
        /// <para>
        /// Not what makes the guarantee — the lazy resolution does, and it is immune to
        /// initialisation order in a way no hook can be. This exists so the failure is
        /// prompt and legible: without it, a misconfigured harness sees a player that
        /// starts normally and then throws somewhere deep in a save.
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResolveAtStartup()
        {
            Resolve();
            if (!demandedButRefused) return;

            Debug.LogError("[Nebula] " + SmokeFlag + " was passed but persistent-data isolation " +
                           "was refused: " + refusal + ". Quitting rather than writing to the " +
                           "real user data folder.");
            Application.Quit(3);
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch (Exception e)
            {
                refusal = "command line unavailable: " + e.Message;
                return;
            }

            bool accepted = TryResolve(args, Application.isEditor, out root, out refusal);
            bool wanted = args != null && Array.IndexOf(args, SmokeFlag) >= 0;

            // The Editor is the one place a refusal is not a failure: EditMode and
            // PlayMode fixtures set the DirectoryOverride seams directly and legitimately
            // run with whatever command line the test runner was given.
            demandedButRefused = wanted && !accepted && !Application.isEditor;

            if (accepted)
            {
                Debug.Log("[Nebula] Persistent data is isolated under " + root +
                          " for this smoke run; the real user folder is not opened.");
            }
        }

        /// <summary>
        /// The whole decision as a pure function of a command line and a host flag, so
        /// every gate above is testable without a player, a temp folder or a smoke run.
        /// </summary>
        /// <param name="args">The process command line, as the engine reports it.</param>
        /// <param name="isEditor">True inside the Editor, which is refused outright.</param>
        /// <param name="resolvedRoot">The accepted sandbox root, or <c>null</c>.</param>
        /// <param name="reason">Why it was refused, or a note describing what was accepted.</param>
        public static bool TryResolve(string[] args, bool isEditor,
            out string resolvedRoot, out string reason)
        {
            resolvedRoot = null;

            if (isEditor)
            {
                reason = "editor host: the Editor uses the DirectoryOverride seams instead";
                return false;
            }
            if (args == null || Array.IndexOf(args, SmokeFlag) < 0)
            {
                reason = "no " + SmokeFlag + " on the command line";
                return false;
            }

            string requested = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == RootFlag) { requested = args[i + 1]; break; }

            if (string.IsNullOrWhiteSpace(requested))
            {
                reason = SmokeFlag + " was passed without " + RootFlag + " <absolute-temp-path>";
                return false;
            }
            if (requested.StartsWith("-", StringComparison.Ordinal))
            {
                // "-nebulaDataRoot -nebulaSmokeOutput /x" -- a missing value that would
                // otherwise be read as the next flag and then fail a later gate with a
                // confusing message.
                reason = RootFlag + " was followed by another flag (" + requested + "), not a path";
                return false;
            }

            if (!IsRootedAndLiteral(requested, out string shapeProblem))
            {
                reason = RootFlag + " " + requested + ": " + shapeProblem;
                return false;
            }

            string normalised = NebulaPersistentPathResolver.Normalise(requested);
            if (normalised == null)
            {
                reason = RootFlag + " " + requested + ": cannot be normalised to a full path";
                return false;
            }

            if (!IsInsideAPermittedTempRoot(normalised, out string permittedRoot, out string tempProblem))
            {
                reason = RootFlag + " " + normalised + ": " + tempProblem;
                return false;
            }

            if (ContainsALinkBelow(permittedRoot, normalised, out string linkProblem))
            {
                reason = RootFlag + " " + normalised + ": " + linkProblem;
                return false;
            }

            string marker = Path.Combine(normalised, MarkerFileName);
            bool stamped;
            try { stamped = File.Exists(marker); }
            catch (Exception e)
            {
                reason = RootFlag + " " + normalised + ": marker unreadable (" + e.Message + ")";
                return false;
            }
            if (!stamped)
            {
                reason = RootFlag + " " + normalised + ": no " + MarkerFileName +
                         " -- the folder was not prepared by a smoke harness, so it is left alone";
                return false;
            }

            resolvedRoot = normalised;
            reason = "isolated under " + normalised;
            return true;
        }

        /// <summary>
        /// Absolute, and spelled without any segment that means "somewhere else".
        /// <para>
        /// <c>Path.IsPathRooted</c> alone is not enough: <c>/tmp/x/../../etc</c> is
        /// rooted. The segment scan is what makes the traversal rejection independent of
        /// normalisation, so the refusal happens before any filesystem call is made with
        /// the caller's string.
        /// </para>
        /// </summary>
        private static bool IsRootedAndLiteral(string path, out string problem)
        {
            problem = null;

            if (path.IndexOf('\0') >= 0) { problem = "contains a NUL character"; return false; }
            if (!Path.IsPathRooted(path)) { problem = "is a relative path; an absolute one is required"; return false; }

            foreach (string segment in path.Split('/', '\\'))
            {
                if (segment == "." || segment == "..")
                {
                    problem = "contains a '" + segment + "' segment; only literal paths are accepted";
                    return false;
                }
            }

            // A path whose *interior* changes when normalised is not the path the caller
            // believes it passed -- a doubled separator, a bare "." component, a spelling
            // the OS will collapse. Accepting one would mean the string in the harness's
            // cleanup command and the string the game actually wrote to are two different
            // strings, which is how a cleanup deletes an empty folder and leaves the data
            // somewhere else.
            //
            // Trailing separators are the deliberate exception, and the comparison trims
            // them on both sides for that reason. "/tmp/sandbox" and "/tmp/sandbox/" name
            // the same directory in every shell and every filesystem API; a harness that
            // interpolates "$ROOT/" is not confused about where it is writing, and
            // refusing it would be pedantry that breaks correct callers.
            string normalised = NebulaPersistentPathResolver.Normalise(path);
            if (normalised == null) { problem = "cannot be resolved to a full path"; return false; }
            if (!string.Equals(normalised, path.TrimEnd('/', '\\'), StringComparison.Ordinal))
            {
                problem = "is not already in normal form (normalises to " + normalised + ")";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Under a temp root this host would hand out, and strictly under it.
        /// <para>
        /// The macOS spellings are listed individually for the reason
        /// <see cref="NebulaLegacyDataMigrator"/> lists them: <c>/tmp</c> is a symlink to
        /// <c>/private/tmp</c>, <c>Path.GetTempPath()</c> returns neither, and
        /// normalisation resolves none of it.
        /// </para>
        /// </summary>
        /// <param name="matchedRoot">The permitted root the path was found under, which
        /// the link check below needs so it knows where to stop walking.</param>
        private static bool IsInsideAPermittedTempRoot(string normalised, out string matchedRoot,
            out string problem)
        {
            matchedRoot = null;

            foreach (string permitted in PermittedTempRoots())
            {
                string root = NebulaPersistentPathResolver.Normalise(permitted);
                if (root == null) continue;

                if (NebulaPersistentPathResolver.SamePath(root, normalised))
                {
                    problem = "is a temp root itself; the sandbox must be a folder inside one";
                    return false;
                }
                if (NebulaPersistentPathResolver.IsWithin(root, normalised))
                {
                    matchedRoot = root;
                    problem = null;
                    return true;
                }
            }

            problem = "is outside every permitted temp root (" +
                      string.Join(", ", PermittedTempRoots()) + ")";
            return false;
        }

        private static string[] PermittedTempRoots()
        {
            string system = null;
            try { system = Path.GetTempPath(); } catch (Exception) { /* refused below */ }

            bool mac = Application.platform == RuntimePlatform.OSXPlayer ||
                       Application.platform == RuntimePlatform.OSXEditor;

            if (!mac)
                return string.IsNullOrEmpty(system) ? Array.Empty<string>() : new[] { system };

            return string.IsNullOrEmpty(system)
                ? new[] { "/tmp", "/private/tmp", "/var/folders", "/private/var/folders" }
                : new[] { system, "/tmp", "/private/tmp", "/var/folders", "/private/var/folders" };
        }

        /// <summary>
        /// True when any component <b>below</b> <paramref name="permittedRoot"/> is a link.
        /// <para>
        /// Below, and not all the way to <c>/</c>, because on macOS <c>/tmp</c> <i>is</i> a
        /// symlink to <c>/private/tmp</c> — that is the operating system's own arrangement,
        /// not an attack, and a check that walked past the permitted root rejected every
        /// well-formed sandbox on the platform this ships from. That is not a theoretical
        /// correction: the first isolated smoke run refused for exactly this reason,
        /// fell back to the real folder, and rewrote a live telemetry file.
        /// </para>
        /// <para>
        /// Stopping at the permitted root loses nothing, because the permitted roots are
        /// the ones the OS hands out and the containment test has already established the
        /// path is under one of them. What matters is the part the <i>caller</i> chose:
        /// <c>/tmp/sandbox</c> where <c>sandbox</c> links to the operator's home directory
        /// satisfies every string-level check above and is caught here.
        /// </para>
        /// <para>
        /// Unreadable attributes count as a link: the refusal direction is the one that
        /// cannot damage anything.
        /// </para>
        /// </summary>
        private static bool ContainsALinkBelow(string permittedRoot, string normalised, out string problem)
        {
            problem = null;
            var directory = new DirectoryInfo(normalised);
            while (directory != null && !NebulaPersistentPathResolver.SamePath(permittedRoot, directory.FullName))
            {
                try
                {
                    if (directory.Exists &&
                        (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        problem = "passes through a symbolic link at " + directory.FullName;
                        return true;
                    }
                }
                catch (Exception e)
                {
                    problem = "attributes of " + directory.FullName + " are unreadable (" +
                              e.Message + "), so it is treated as a link";
                    return true;
                }
                directory = directory.Parent;
            }
            return false;
        }
    }
}
