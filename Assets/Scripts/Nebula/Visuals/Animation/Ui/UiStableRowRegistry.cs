using System.Collections.Generic;

namespace Nebula.Visuals
{
    /// <summary>What a list row is doing this refresh.</summary>
    public enum UiRowPhase
    {
        /// <summary>New this refresh: it plays an entry.</summary>
        Entering,

        /// <summary>Already there last refresh: it plays nothing.</summary>
        Held,

        /// <summary>Gone this refresh: it plays an exit.</summary>
        Exiting
    }

    /// <summary>
    /// Remembers which rows were on screen last refresh, keyed by the id the
    /// game already owns.
    /// <para>
    /// A rebuilt list must not look like a list that was just created. A vehicle
    /// that was there a frame ago is the same vehicle, so its row holds still
    /// while genuinely new ones arrive. The canonical order is the panel's and
    /// is never read or written here — this only answers "have I seen this id
    /// before".
    /// </para>
    /// </summary>
    public sealed class UiStableRowRegistry
    {
        private readonly HashSet<string> known = new HashSet<string>();
        private readonly HashSet<string> thisRefresh = new HashSet<string>();
        private readonly List<string> departed = new List<string>();

        /// <summary>How many rows were on screen after the last refresh. Test-facing.</summary>
        public int KnownCount => known.Count;

        /// <summary>Rows that entered on the last refresh. Test-facing.</summary>
        public int EnteredLastRefresh { get; private set; }

        /// <summary>Starts a refresh. Reuses its own buffers; nothing is allocated per frame.</summary>
        public void BeginRefresh()
        {
            thisRefresh.Clear();
            departed.Clear();
            EnteredLastRefresh = 0;
        }

        /// <summary>
        /// Records a row and says what it should do. An id seen last refresh is
        /// held, so a rebuild never replays every entry.
        /// </summary>
        public UiRowPhase Observe(string rowId)
        {
            if (string.IsNullOrEmpty(rowId)) return UiRowPhase.Held;

            thisRefresh.Add(rowId);
            if (known.Contains(rowId)) return UiRowPhase.Held;

            EnteredLastRefresh++;
            return UiRowPhase.Entering;
        }

        /// <summary>
        /// Closes the refresh and reports the ids that are gone. Those rows play
        /// an exit; everything else keeps its pose.
        /// </summary>
        public IReadOnlyList<string> EndRefresh()
        {
            foreach (string id in known)
                if (!thisRefresh.Contains(id)) departed.Add(id);

            known.Clear();
            foreach (string id in thisRefresh) known.Add(id);

            return departed;
        }

        /// <summary>True when this id was on screen after the last completed refresh.</summary>
        public bool IsKnown(string rowId) => rowId != null && known.Contains(rowId);

        /// <summary>Forgets everything — a new session, or a panel that closed.</summary>
        public void Clear()
        {
            known.Clear();
            thisRefresh.Clear();
            departed.Clear();
            EnteredLastRefresh = 0;
        }
    }
}
