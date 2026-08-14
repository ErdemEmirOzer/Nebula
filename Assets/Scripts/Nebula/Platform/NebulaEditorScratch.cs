using System.IO;
using UnityEngine;

namespace Nebula.Platform
{
    /// <summary>
    /// A throwaway persistent-data root for the Editor, so a test run can never write
    /// into the folder a player's install owns.
    /// <para>
    /// This is a data-loss interlock, and it exists because the loss happened. Every
    /// persistent-data consumer resolves its folder through a <c>??</c> chain ending in
    /// <c>Application.persistentDataPath</c>. That was harmless for as long as the
    /// Editor's persistent folder was an empty scratch identity — and stopped being
    /// harmless the moment VS-017A.2 renamed the app and migrated a real campaign into
    /// it. A full EditMode run then deleted slot 1 off this machine, because two UI
    /// fixtures clear every save slot in <c>[SetUp]</c> and neither had set a directory
    /// override. A survey found 54 fixtures reaching the save service with no override
    /// at all.
    /// </para>
    /// <para>
    /// Placed at the seam rather than in the fixtures on purpose. Patching 54 files owned
    /// by several workstreams protects only the call sites somebody remembered to patch;
    /// one guard here protects the ones nobody has written yet. A fixture that genuinely
    /// wants a particular folder still says so with its own <c>DirectoryOverride</c>,
    /// which is checked first and still wins.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> a general "redirect data in the editor" feature. It sits
    /// last in each chain, below <see cref="NebulaSmokeDataRoot"/>, and returns
    /// <c>null</c> outside the Editor — so a shipped player's chain is byte-for-byte the
    /// behaviour it had before this file existed, and a real campaign is untouched by any
    /// of it.
    /// </para>
    /// <para>
    /// One root per Editor process, not per call: a test that saves and then loads has to
    /// see its own file, and a fresh folder per read would quietly make every round trip
    /// fail. It is left on disk rather than cleaned up, because an operator who wants to
    /// know what a run wrote has to be able to go and look.
    /// </para>
    /// </summary>
    public static class NebulaEditorScratch
    {
        /// <summary>The folder name under the temp root, so a stray directory is identifiable.</summary>
        private const string RootName = "nebula-editor-data";

        private static string root;

        /// <summary>
        /// <paramref name="leaf"/> inside this Editor process's scratch root, or
        /// <c>null</c> in a player — which is what makes the <c>??</c> chains fall
        /// through to the engine's own folder in a real launch.
        /// </summary>
        public static string Folder(string leaf)
        {
            string owned = Root;
            return owned == null ? null : Path.Combine(owned, "nebula", leaf);
        }

        /// <summary>
        /// The scratch root, or <c>null</c> outside the Editor.
        /// <para>
        /// Keyed by process id so two Editors — a batchmode test run and an open one —
        /// cannot share a folder and race each other's files.
        /// </para>
        /// </summary>
        public static string Root
        {
            get
            {
                if (!Application.isEditor) return null;
                if (root == null)
                {
                    int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    root = Path.Combine(Path.GetTempPath(), RootName, pid.ToString());
                }
                return root;
            }
        }
    }
}
