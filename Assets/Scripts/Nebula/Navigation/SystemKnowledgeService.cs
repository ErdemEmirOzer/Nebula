using System.Collections.Generic;

namespace Nebula.Navigation
{
    /// <summary>Progressive knowledge of a real star system, from nothing known to physically visited.</summary>
    public enum SystemKnowledgeLevel { Unknown = 0, Signal = 1, Located = 2, Visited = 3 }

    /// <summary>
    /// Pure, save-friendly tracker of what the player knows about every real
    /// navigation system. Never stores system coordinates/manifest (that
    /// stays in <see cref="Nebula.World.StarSystemRegistry"/>, the single
    /// authority) — only the knowledge level per system id. The "reveal
    /// nearest three on first Controlled planet" rule (with full
    /// origin-&gt;target provenance and backfill) is <see cref="FrontierSignalService"/>'s
    /// job as of VS-010 — this class no longer owns any reveal-guard state
    /// itself, avoiding a dual authority.
    /// </summary>
    public class SystemKnowledgeService
    {
        private readonly Dictionary<string, SystemKnowledgeLevel> levels = new Dictionary<string, SystemKnowledgeLevel>();

        public SystemKnowledgeLevel GetLevel(string systemId) =>
            systemId != null && levels.TryGetValue(systemId, out var l) ? l : SystemKnowledgeLevel.Unknown;

        /// <summary>Raises the level if higher than current; never downgrades (Located/Visited are sticky).</summary>
        public void Raise(string systemId, SystemKnowledgeLevel level)
        {
            if (string.IsNullOrEmpty(systemId)) return;
            if (level > GetLevel(systemId)) levels[systemId] = level;
        }

        /// <summary>Sets the exact level regardless of current value — used only by save/load restore and migration.</summary>
        public void ForceLevel(string systemId, SystemKnowledgeLevel level)
        {
            if (string.IsNullOrEmpty(systemId)) return;
            levels[systemId] = level;
        }

        public void Clear() => levels.Clear();

        public void InitializeNewGame(string startSystemId)
        {
            Clear();
            if (!string.IsNullOrEmpty(startSystemId)) levels[startSystemId] = SystemKnowledgeLevel.Visited;
        }

        public IEnumerable<KeyValuePair<string, SystemKnowledgeLevel>> All => levels;
    }
}
