using System.IO;
using NUnit.Framework;
using Nebula.Platform;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The gates that decide whether an automated run is allowed to redirect the game's
    /// persistent data — and, far more importantly, whether it is allowed to run at all
    /// when it cannot.
    /// <para>
    /// These are not hypothetical cases. A smoke run of the Development player was once
    /// started without isolation and rewrote a live telemetry file in the operator's own
    /// <c>DefaultCompany/Nebula</c> folder; only the file's hash had been recorded, so it
    /// could not be restored. Then, while this very mechanism was being built, the first
    /// isolated run <b>also</b> wrote to the real folder — the link check walked past
    /// <c>/tmp</c>, which on macOS is itself a symlink, refused a perfectly good sandbox,
    /// and the run carried on against the real one. Both of those are cases below.
    /// </para>
    /// <para>
    /// Every case drives <see cref="NebulaSmokeDataRoot.TryResolve"/>, which is a pure
    /// function of a command line and a host flag. That is the reason it is a pure
    /// function: the alternative is a fixture that has to launch a player to find out
    /// whether the player would have damaged the machine it launched on.
    /// </para>
    /// </summary>
    public class NebulaSmokeDataRootTests
    {
        private string sandbox;

        [SetUp]
        public void SetUp()
        {
            // /private/tmp rather than /tmp: the same folder either way, but spelled the
            // way the OS resolves it, so a test that fails tells us about the gate rather
            // than about the spelling.
            sandbox = Path.Combine("/private/tmp", "nebula-smoke-root-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(sandbox);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(sandbox) && Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }

        private void Stamp(string root) =>
            File.WriteAllText(Path.Combine(root, NebulaSmokeDataRoot.MarkerFileName), "test");

        private static string[] Args(string root) =>
            new[] { "/path/to/player", NebulaSmokeDataRoot.SmokeFlag, NebulaSmokeDataRoot.RootFlag, root };

        private static bool Resolve(string[] args, out string root, out string reason) =>
            NebulaSmokeDataRoot.TryResolve(args, isEditor: false, out root, out reason);

        // ---- the accepting case -------------------------------------------------

        /// <summary>A stamped folder under a temp root is what isolation is for.</summary>
        [Test]
        public void AStampedTempFolderIsAccepted()
        {
            Stamp(sandbox);
            Assert.IsTrue(Resolve(Args(sandbox), out string root, out string reason), reason);
            Assert.AreEqual(sandbox, root);
        }

        /// <summary>
        /// The regression that let the first "isolated" run write to the real folder.
        /// <para>
        /// <c>/tmp</c> on macOS is a symlink to <c>/private/tmp</c>. A link check that
        /// walked all the way to the filesystem root therefore rejected every sandbox
        /// created the ordinary way, the run fell back to
        /// <c>Application.persistentDataPath</c>, and a live telemetry file changed. The
        /// permitted roots are trusted; only what the caller chose below them is checked.
        /// </para>
        /// </summary>
        [Test]
        public void ASandboxReachedThroughTheTmpSymlinkIsStillAccepted()
        {
            string viaSymlink = "/tmp" + sandbox.Substring("/private/tmp".Length);
            Stamp(sandbox);

            Assert.IsTrue(Resolve(Args(viaSymlink), out string root, out string reason),
                "a sandbox under /tmp must be accepted -- /tmp being a symlink is macOS's " +
                "own arrangement, not an attack: " + reason);
            Assert.AreEqual(viaSymlink, root);
        }

        // ---- the refusing cases -------------------------------------------------

        /// <summary>A normal launch. No flags, nothing resolved, nothing redirected.</summary>
        [Test]
        public void ANormalLaunchIsInert()
        {
            Assert.IsFalse(Resolve(new[] { "/path/to/player" }, out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains(NebulaSmokeDataRoot.SmokeFlag, reason);
        }

        /// <summary>
        /// The interlock that makes every other gate hard to defeat by accident: without
        /// a marker the folder is somebody's data, not a sandbox, and is left alone.
        /// <para>
        /// This is also the mutation proof for
        /// <see cref="AStampedTempFolderIsAccepted"/>: the two cases differ by one file.
        /// </para>
        /// </summary>
        [Test]
        public void AnUnstampedFolderIsRefused()
        {
            Assert.IsFalse(Resolve(Args(sandbox), out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains(NebulaSmokeDataRoot.MarkerFileName, reason);
        }

        /// <summary>The Editor has its own seams and its own interlock; this one stays out.</summary>
        [Test]
        public void TheEditorIsRefusedOutright()
        {
            Stamp(sandbox);
            Assert.IsFalse(NebulaSmokeDataRoot.TryResolve(Args(sandbox), isEditor: true,
                out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains("editor", reason.ToLowerInvariant());
        }

        /// <summary>Asking for a smoke without saying where is a mistake, not a default.</summary>
        [Test]
        public void TheSmokeFlagWithoutADataRootIsRefused()
        {
            Assert.IsFalse(Resolve(new[] { "/player", NebulaSmokeDataRoot.SmokeFlag },
                out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains(NebulaSmokeDataRoot.RootFlag, reason);
        }

        /// <summary>A missing value swallowing the next flag must be caught where it happens.</summary>
        [Test]
        public void ADataRootThatIsActuallyTheNextFlagIsRefused()
        {
            Assert.IsFalse(Resolve(
                new[] { "/player", NebulaSmokeDataRoot.SmokeFlag, NebulaSmokeDataRoot.RootFlag, "-logFile", "/x" },
                out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains("not a path", reason);
        }

        [Test]
        public void ARelativePathIsRefused()
        {
            Assert.IsFalse(Resolve(Args("tmp/nebula"), out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains("absolute", reason);
        }

        /// <summary>
        /// Traversal is rejected on the literal string, before any filesystem call is made
        /// with it — normalising it away first would mean the refusal depends on where the
        /// path happened to land.
        /// </summary>
        [Test]
        public void APathWithATraversalSegmentIsRefused()
        {
            Stamp(sandbox);
            Assert.IsFalse(Resolve(Args(sandbox + "/../" + Path.GetFileName(sandbox)),
                out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains("'..' segment", reason);
        }

        /// <summary>The whole point: a stamped folder in the user's home is still refused.</summary>
        [Test]
        public void AStampedFolderOutsideEveryTempRootIsRefused()
        {
            string home = NebulaPersistentPathResolver.HomeDirectory();
            Assert.IsFalse(Resolve(Args(Path.Combine(home, "Library", "Application Support", "Erdem", "NEBULA")),
                out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains("outside every permitted temp root", reason);
        }

        /// <summary>
        /// The temp root itself is refused because the harness deletes what it is given,
        /// and what it would be given here is the whole temp directory.
        /// </summary>
        [Test]
        public void TheTempRootItselfIsRefused()
        {
            Assert.IsFalse(Resolve(Args("/private/tmp"), out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains("temp root itself", reason);
        }

        /// <summary>
        /// The attack the link check is actually for: a name inside the permitted root
        /// that points somewhere else entirely. Every string-level gate passes; only
        /// looking at the filesystem catches it.
        /// </summary>
        [Test]
        public void ALinkBelowTheTempRootIsRefused()
        {
            string target = Path.Combine(sandbox, "real");
            Directory.CreateDirectory(target);
            Stamp(target);

            string link = Path.Combine(sandbox, "link");
            var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("/bin/ln", $"-s \"{target}\" \"{link}\"")
                { UseShellExecute = false });
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, "could not create the symlink this test needs");

            Assert.IsFalse(Resolve(Args(link), out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains("symbolic link", reason);
        }

        /// <summary>
        /// A path whose interior is not in normal form is refused rather than repaired:
        /// the harness's cleanup command and the folder the game wrote to have to be the
        /// same string, or cleanup deletes one place and the data is in another.
        /// </summary>
        [Test]
        public void APathWithADoubledInteriorSeparatorIsRefused()
        {
            Stamp(sandbox);
            string doubled = "/private//tmp/" + Path.GetFileName(sandbox);
            Assert.IsFalse(Resolve(Args(doubled), out string root, out string reason));
            Assert.IsNull(root);
            StringAssert.Contains("normal form", reason);
        }

        /// <summary>
        /// The deliberate exception to the rule above, asserted so nobody "tightens" it
        /// back: a trailing separator names the same directory everywhere, and a harness
        /// that interpolates <c>"$ROOT/"</c> is not confused about where it writes.
        /// Refusing it would break correct callers for no gain.
        /// </summary>
        [Test]
        public void ATrailingSeparatorIsTolerated()
        {
            Stamp(sandbox);
            Assert.IsTrue(Resolve(Args(sandbox + "/"), out string root, out string reason), reason);
            Assert.AreEqual(sandbox, root);
        }

        // ---- the derived paths --------------------------------------------------

        /// <summary>
        /// Active and legacy are siblings inside the sandbox, so the migration has two
        /// distinct roots to work between without either being the operator's.
        /// </summary>
        [Test]
        public void TheSandboxSuppliesBothMigrationRoots()
        {
            Stamp(sandbox);
            Assert.IsTrue(Resolve(Args(sandbox), out string root, out _));

            string active = Path.Combine(root, "active");
            string legacy = Path.Combine(root, "legacy");
            Assert.AreNotEqual(active, legacy);
            Assert.IsTrue(NebulaPersistentPathResolver.IsWithin(root, active));
            Assert.IsTrue(NebulaPersistentPathResolver.IsWithin(root, legacy));
        }
    }
}
