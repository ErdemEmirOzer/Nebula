using System.Collections.Generic;

namespace Nebula.Logistics
{
    /// <summary>
    /// Central runtime registry for the Logistics slice (VS-011), mirroring
    /// <see cref="Extraction.ExtractionState"/>'s drill/structure registries.
    /// Only the CURRENTLY FULL system ever has live entries here — every
    /// other known system's center/routes live inside its
    /// <see cref="Sectors.SystemDelta"/> instead. Reset by the bootstrap
    /// alongside ExtractionState.Reset so a disabled domain reload never
    /// leaks a previous run's state.
    /// </summary>
    public static class LogisticsState
    {
        private static readonly List<LogisticsCenter> centers = new List<LogisticsCenter>();
        private static readonly List<CollectorRoute> routes = new List<CollectorRoute>();
        private static int nextRouteSequence;

        public static IReadOnlyList<LogisticsCenter> Centers => centers;
        public static IReadOnlyList<CollectorRoute> Routes => routes;

        public static void Reset()
        {
            centers.Clear();
            routes.Clear();
            nextRouteSequence = 0;
        }

        public static void RegisterCenter(LogisticsCenter center) { if (!centers.Contains(center)) centers.Add(center); }
        public static void UnregisterCenter(LogisticsCenter center) => centers.Remove(center);

        public static LogisticsCenter FindCenter(string systemId)
        {
            for (int i = 0; i < centers.Count; i++)
                if (centers[i] != null && centers[i].SystemId == systemId) return centers[i];
            return null;
        }

        public static void AddRoute(CollectorRoute route) { if (route != null && !routes.Contains(route)) routes.Add(route); }
        public static void RemoveRoute(CollectorRoute route) => routes.Remove(route);

        public static CollectorRoute FindRouteByVehicle(string vehicleId)
        {
            for (int i = 0; i < routes.Count; i++)
                if (routes[i] != null && routes[i].VehicleId == vehicleId) return routes[i];
            return null;
        }

        public static CollectorRoute FindRouteByDrill(string drillWorldId)
        {
            for (int i = 0; i < routes.Count; i++)
                if (routes[i] != null && routes[i].DrillWorldId == drillWorldId) return routes[i];
            return null;
        }

        public static int RouteCountForSystem(string systemId)
        {
            int count = 0;
            for (int i = 0; i < routes.Count; i++)
                if (routes[i] != null && routes[i].SystemId == systemId) count++;
            return count;
        }

        public static string NextRouteId()
        {
            string id = CollectorRouteService.NextRouteId(nextRouteSequence);
            nextRouteSequence++;
            return id;
        }

        public static int NextRouteSequenceForSave => nextRouteSequence;
        public static void EnsureRouteSequenceAtLeast(int minimum) { if (minimum > nextRouteSequence) nextRouteSequence = minimum; }
    }
}
