using System.Collections.Generic;

namespace Nebula.Poi
{
    /// <summary>
    /// Central runtime registry for the currently-Full system's live POI
    /// state (VS-012), mirroring <see cref="Logistics.LogisticsState"/>.
    /// Only ever holds entries for the ONE currently-Full system — every
    /// other known system's POI state lives inside its
    /// <see cref="Sectors.SystemDelta"/> instead. Reset alongside
    /// ExtractionState/LogisticsState.
    /// </summary>
    public static class PoiLiveState
    {
        private static readonly Dictionary<string, PoiState> states = new Dictionary<string, PoiState>();

        public static IReadOnlyDictionary<string, PoiState> States => states;

        public static void Reset() => states.Clear();

        public static void Register(PoiState state) { if (state != null) states[state.PoiId] = state; }
        /// <summary>
        /// Removes a live state only when the caller still owns the entry. System
        /// rebuilds destroy the outgoing hierarchy at the end of the frame, after
        /// an equal-id replacement has already registered; key-only removal would
        /// therefore delete the replacement's save authority.
        /// </summary>
        public static void Unregister(string poiId, PoiState expected)
        {
            if (poiId != null && expected != null
                && states.TryGetValue(poiId, out PoiState current)
                && ReferenceEquals(current, expected))
                states.Remove(poiId);
        }

        public static PoiState Find(string poiId) => poiId != null && states.TryGetValue(poiId, out PoiState s) ? s : null;

        public static PoiState FindByAssignedVehicle(string vehicleId)
        {
            if (vehicleId == null) return null;
            foreach (PoiState s in states.Values) if (s.AssignedVehicleId == vehicleId) return s;
            return null;
        }
    }
}
