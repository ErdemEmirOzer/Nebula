using System.Collections.Generic;

namespace Nebula.Extraction
{
    /// <summary>Communication state of one system (VS-010 §1) — never persisted as its own save authority; always re-derived from route+Command+delta.</summary>
    public enum SystemLinkState { Live, LastKnown, Disconnected }

    /// <summary>
    /// Pure resolver for a system's communication state. "Live" models a
    /// Command-relay mesh: any two systems that each have an Active Command
    /// (on a Controlled planet) and are both at least Located-known to the
    /// player are considered mutually reachable — a discovered route with a
    /// working Command chain, without needing a separate hop-adjacency graph
    /// (no such concept exists yet). The ship's own current system always
    /// counts as Live for itself.
    /// </summary>
    public static class SystemCommunicationResolver
    {
        public static SystemLinkState Resolve(string targetSystemId, string shipCurrentSystemId,
            bool targetHasActiveCommand, bool shipSystemHasActiveCommand, bool targetIsKnown, bool targetHasValidSnapshot)
        {
            bool live = targetSystemId == shipCurrentSystemId
                        || (targetHasActiveCommand && shipSystemHasActiveCommand && targetIsKnown);
            if (live) return SystemLinkState.Live;
            return targetHasValidSnapshot ? SystemLinkState.LastKnown : SystemLinkState.Disconnected;
        }

        /// <summary>Convenience overload for a whole reachability set, used by HUD/queue code that needs many systems' link state at once.</summary>
        public static Dictionary<string, SystemLinkState> ResolveMany(IEnumerable<string> systemIds, string shipCurrentSystemId,
            System.Func<string, bool> hasActiveCommand, System.Func<string, bool> isKnown, System.Func<string, bool> hasValidSnapshot)
        {
            bool shipSystemHasCommand = hasActiveCommand(shipCurrentSystemId);
            var result = new Dictionary<string, SystemLinkState>();
            foreach (string id in systemIds)
                result[id] = Resolve(id, shipCurrentSystemId, hasActiveCommand(id), shipSystemHasCommand, isKnown(id), hasValidSnapshot(id));
            return result;
        }
    }
}
