using System;
using System.Collections;
using UnityEngine;
using Nebula.UI.Shell;

namespace Nebula.UI.Brand
{
    /// <summary>
    /// Runs an operation with the loading surface up, and guarantees it comes down.
    /// <para>
    /// The overlay is shown before the work and removed in a <c>finally</c>, so an
    /// operation that throws leaves the interface exactly as it found it and the
    /// screen's own error feedback is what the player sees. A loading picture that
    /// survives its own failure is the worst outcome available here: it is
    /// indistinguishable from a hang.
    /// </para>
    /// <para>
    /// No artificial delay, anywhere. If the work returns in a millisecond the
    /// overlay is up for a millisecond — the brief is explicit that a fast operation
    /// must not be padded to make the animation legible, and padding a synchronous
    /// call would mean holding the main thread to show a spinner about nothing.
    /// </para>
    /// </summary>
    public static class NebulaLoadingPresenter
    {
        /// <summary>
        /// Nesting depth. Only the outermost scope owns the overlay, so a restart
        /// that tears a session down and starts another inside one scope shows one
        /// overlay rather than closing the first halfway through.
        /// </summary>
        private static int depth;

        /// <summary>Test-facing: whether an operation currently owns the overlay.</summary>
        public static bool Busy => depth > 0;

        /// <summary>
        /// Runs synchronous work behind the overlay.
        /// <para>
        /// Session composition is synchronous today, so this is what the menu's
        /// entries use. It is still worth routing through: the overlay exists for
        /// the duration, the teardown is guaranteed, and the day composition becomes
        /// a coroutine the call sites move to <see cref="RunAsync"/> without any of
        /// them learning about the overlay.
        /// </para>
        /// </summary>
        public static void Run(NebulaUiShell shell, string caption, Action work)
        {
            if (work == null) return;

            Begin(shell, caption);
            try { work(); }
            finally { End(); }
        }

        /// <summary>
        /// Runs multi-frame work behind the overlay. The loop actually animates on
        /// this path, because the frames the operation spans are frames the interface
        /// gets to draw.
        /// </summary>
        public static IEnumerator RunAsync(NebulaUiShell shell, string caption, IEnumerator work)
        {
            if (work == null) yield break;

            Begin(shell, caption);
            try
            {
                // Stepped by hand rather than `yield return work` so that a throw
                // inside the operation, and an early Dispose by a caller that stops
                // enumerating, both reach the finally below.
                while (work.MoveNext()) yield return work.Current;
            }
            finally { End(); }
        }

        private static void Begin(NebulaUiShell shell, string caption)
        {
            depth++;
            if (depth == 1) NebulaLoadingOverlay.Show(shell, caption ?? NebulaLoadingOverlay.DefaultCaption);
        }

        private static void End()
        {
            depth = Mathf.Max(0, depth - 1);
            if (depth == 0) NebulaLoadingOverlay.HideActive();
        }

        /// <summary>
        /// Forces the surface down and the depth back to zero.
        /// <para>
        /// For a teardown that has to be certain — returning to the main menu, a
        /// fixture between cases. Normal paths never need it, and needing it in a
        /// normal path would mean a <c>finally</c> was skipped.
        /// </para>
        /// </summary>
        public static void ForceClear()
        {
            depth = 0;
            NebulaLoadingOverlay.HideActive();
        }
    }
}
