using System;
using System.IO;
using UnityEngine;

namespace Nebula.Platform
{
    /// <summary>
    /// The desktop conventions this game actually ships against.
    /// <para>
    /// Deliberately not <c>RuntimePlatform</c>: this resolver has to be callable for a
    /// platform the test is not running on, and a test that can only ask about its own
    /// host cannot check the Windows layout at all. Linux is absent on purpose — the
    /// build automation resolves Windows x86_64 or macOS and nothing else
    /// (<c>NebulaBuildAutomation.ResolveTarget</c>), so a Linux root here would be a
    /// path this project has never produced and cannot verify.
    /// </para>
    /// </summary>
    public enum PersistentDataPlatform
    {
        /// <summary>A host whose convention this project has not shipped against, so no root is claimed.</summary>
        Unsupported = 0,
        MacOS,
        Windows,
    }

    /// <summary>
    /// The two roots the migration works between: where the data used to live and
    /// where the running build keeps it now.
    /// <para>
    /// An interface rather than two strings so a test can supply temp folders without
    /// the migrator ever reading <c>Application.persistentDataPath</c> — reading that
    /// property does not merely report a path, it creates the folder, which is not
    /// something a test is entitled to do to the operator's machine.
    /// </para>
    /// </summary>
    public interface IPersistentPathProvider
    {
        /// <summary>Per-app root the previous identity wrote to, or <c>null</c> when this platform has none to claim.</summary>
        string LegacyRoot { get; }

        /// <summary>Per-app root this build writes to. Never computed by us in a player — it is what the engine reports.</summary>
        string ActiveRoot { get; }
    }

    /// <summary>Concrete pair of roots. The only implementation the game and its tests need.</summary>
    public sealed class NebulaPersistentPaths : IPersistentPathProvider
    {
        public string LegacyRoot { get; }
        public string ActiveRoot { get; }

        public NebulaPersistentPaths(string legacyRoot, string activeRoot)
        {
            LegacyRoot = legacyRoot;
            ActiveRoot = activeRoot;
        }

        /// <summary>
        /// The real pair for the running build.
        /// <para>
        /// A method and not a property because it has a side effect worth seeing at the
        /// call site: <c>Application.persistentDataPath</c> creates its directory. It is
        /// called from the player's startup hook and from nowhere else.
        /// </para>
        /// <para>
        /// An automated smoke run with a stamped sandbox gets the sandbox's pair instead,
        /// and this is the only place that substitution happens. It has to be here rather
        /// than at the migrator's call site because <b>both</b> roots have to move
        /// together: redirecting only the destination would leave a migration reading the
        /// operator's real <c>DefaultCompany/Nebula</c> folder on every smoke boot, which
        /// is the folder a previous unisolated run damaged. Neither term is evaluated
        /// when the sandbox answers — the engine property is not read at all, so an
        /// isolated run does not even create the real folder.
        /// </para>
        /// </summary>
        public static NebulaPersistentPaths CreateForRunningBuild()
        {
            IPersistentPathProvider sandbox = NebulaSmokeDataRoot.PathsOrNull();
            if (sandbox != null) return new NebulaPersistentPaths(sandbox.LegacyRoot, sandbox.ActiveRoot);

            string legacy = NebulaPersistentPathResolver.LegacyRootForThisHost();
            return new NebulaPersistentPaths(legacy, Application.persistentDataPath);
        }
    }

    /// <summary>
    /// Where a Unity desktop build keeps per-user data, as a pure function of
    /// (platform, home directory, company, product).
    /// <para>
    /// Pure on purpose. The active root is never computed — in a player it is whatever
    /// <c>Application.persistentDataPath</c> says, because the engine is the authority on
    /// its own path and guessing it would be a second authority that can disagree. What
    /// this function is *for* is the <b>legacy</b> root: the folder the previous identity
    /// wrote to, which no longer has an engine willing to report it, and which therefore
    /// has to be derived from the old company and product names.
    /// </para>
    /// <para>
    /// The macOS formula is not inferred from documentation alone: this machine holds
    /// <c>~/Library/Application Support/DefaultCompany/Nebula/nebula/saves/slot1.json</c>,
    /// written by this project under <c>companyName: DefaultCompany</c> and
    /// <c>productName: Nebula</c>, which is the formula below with those two values
    /// substituted.
    /// </para>
    /// </summary>
    public static class NebulaPersistentPathResolver
    {
        /// <summary>Characters that would make a company or product name mean something other than one folder name.</summary>
        private static readonly char[] Forbidden = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

        /// <summary>Which convention this host follows, or <see cref="PersistentDataPlatform.Unsupported"/>.</summary>
        public static PersistentDataPlatform PlatformFor(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    return PersistentDataPlatform.MacOS;
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return PersistentDataPlatform.Windows;
                default:
                    return PersistentDataPlatform.Unsupported;
            }
        }

        /// <summary>This host's convention.</summary>
        public static PersistentDataPlatform Current => PlatformFor(Application.platform);

        /// <summary>
        /// The user's home directory as this host reports it, or <c>null</c>.
        /// <para>
        /// Separate from <see cref="TryResolveRoot"/> so the formula stays testable: a
        /// test supplies a fake home and gets a deterministic answer, and only the one
        /// caller that wants the real machine asks for the real home.
        /// </para>
        /// </summary>
        public static string HomeDirectory()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) return home;

            // Mono has returned empty for UserProfile on some Unix configurations, so the
            // environment is the fallback rather than the first choice — HOME can be unset
            // or point somewhere unhelpful under a service account.
            home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home)) return home;

            return Environment.GetEnvironmentVariable("USERPROFILE");
        }

        /// <summary>
        /// The per-app root for one identity, built with the target platform's own
        /// separator rather than the host's — otherwise "the Windows layout" checked on
        /// a Mac would silently be a POSIX string and prove nothing.
        /// </summary>
        /// <param name="root">The resolved root, or <c>null</c> when <paramref name="problem"/> explains why there is none.</param>
        public static bool TryResolveRoot(PersistentDataPlatform platform, string home,
            string company, string product, out string root, out string problem)
        {
            root = null;
            problem = null;

            if (string.IsNullOrWhiteSpace(home)) { problem = "no home directory"; return false; }
            if (!IsUsableFolderName(company, out problem)) { problem = "company name: " + problem; return false; }
            if (!IsUsableFolderName(product, out problem)) { problem = "product name: " + problem; return false; }

            switch (platform)
            {
                case PersistentDataPlatform.MacOS:
                    root = Join('/', home, "Library", "Application Support", company, product);
                    return true;

                case PersistentDataPlatform.Windows:
                    root = Join('\\', home, "AppData", "LocalLow", company, product);
                    return true;

                default:
                    problem = "no per-user data convention is claimed for this platform";
                    return false;
            }
        }

        /// <summary>The root for this host and the identity every build before this one used, or <c>null</c>.</summary>
        public static string LegacyRootForThisHost() =>
            TryResolveRoot(Current, HomeDirectory(),
                NebulaAppIdentity.LegacyCompany, NebulaAppIdentity.LegacyProduct,
                out string root, out _)
                ? root
                : null;

        /// <summary>
        /// A name that means exactly one folder.
        /// <para>
        /// Rejects rather than sanitises. Unity has its own rules for turning an awkward
        /// product name into a folder name, and a second, guessed set of rules here would
        /// produce a path that looks plausible and points at the wrong place — which for a
        /// migration means either copying nothing or copying somebody else's data.
        /// </para>
        /// </summary>
        public static bool IsUsableFolderName(string name, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(name)) { problem = "is empty"; return false; }
            if (name.IndexOfAny(Forbidden) >= 0) { problem = "contains a path separator or a reserved character"; return false; }
            if (name == "." || name == "..") { problem = "is a relative path segment"; return false; }
            if (name.Contains("..")) { problem = "contains a parent-directory segment"; return false; }
            return true;
        }

        /// <summary>Full path with any trailing separator removed, or <c>null</c> when the input cannot be one.</summary>
        public static string Normalise(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string full = Path.GetFullPath(path);
                if (full.Length > 1) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return full.Length == 0 ? Path.DirectorySeparatorChar.ToString() : full;
            }
            catch (Exception) { return null; }
        }

        /// <summary>True when both strings name the same directory once normalised.</summary>
        public static bool SamePath(string a, string b)
        {
            string left = Normalise(a), right = Normalise(b);
            if (left == null || right == null) return false;
            return string.Equals(left, right, ComparisonForThisHost);
        }

        /// <summary>
        /// True when <paramref name="candidate"/> is <paramref name="root"/> or lies
        /// under it, compared on normalised full paths.
        /// <para>
        /// This is the traversal guard: <c>root/saves/../../../etc/passwd</c> normalises
        /// to something outside <c>root</c> and fails here, which is the whole point of
        /// normalising before comparing rather than matching the raw text.
        /// </para>
        /// <para>
        /// It is <b>not</b> a link guard. Normalisation does not resolve symlinks, so a
        /// link inside the root that points outside it still satisfies this check — that
        /// case is handled separately, by refusing links outright.
        /// </para>
        /// </summary>
        public static bool IsWithin(string root, string candidate)
        {
            string normalisedRoot = Normalise(root), normalisedCandidate = Normalise(candidate);
            if (normalisedRoot == null || normalisedCandidate == null) return false;
            if (string.Equals(normalisedRoot, normalisedCandidate, ComparisonForThisHost)) return true;

            string prefix = normalisedRoot.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? normalisedRoot
                : normalisedRoot + Path.DirectorySeparatorChar;
            return normalisedCandidate.StartsWith(prefix, ComparisonForThisHost);
        }

        /// <summary>
        /// Windows paths are case-insensitive and macOS volumes are usually
        /// case-insensitive but may not be, so the safe answer for a *containment* test
        /// is the case-insensitive one: it can only ever refuse more than a
        /// case-sensitive comparison would, and refusing is the failure direction that
        /// does not leak a copy outside its root.
        /// </summary>
        private const StringComparison ComparisonForThisHost = StringComparison.OrdinalIgnoreCase;

        private static string Join(char separator, params string[] parts)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].TrimEnd('/', '\\');
                if (i > 0) builder.Append(separator);
                builder.Append(part);
            }
            return builder.ToString();
        }
    }
}
