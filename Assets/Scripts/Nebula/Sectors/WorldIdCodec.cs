using System;
using System.Globalization;

namespace Nebula.Sectors
{
    /// <summary>
    /// Single authority for building and parsing canonical world ids
    /// (VS-008 §1): a system is "sector.x.y.z/system.NNN" (one slash inside
    /// the sector id itself, one more before "system.NNN" — two slashes
    /// total), a planet extends it with "/planet.NN" (three slashes total).
    /// Every consumer (Navigation, Extraction, Dominance, Hangar, save) MUST
    /// route through here — the old "take everything before the FIRST
    /// slash" trick (correct for the legacy "sysNNN/plMM" shape, which only
    /// ever had one slash) silently returns just "sector.x.y.z" for a
    /// canonical id and is never valid here.
    /// </summary>
    public static class WorldIdCodec
    {
        private const string SystemSegmentPrefix = "system.";
        private const string PlanetSegmentPrefix = "planet.";
        private const string PoiSegmentPrefix = "poi.";

        public static string SystemId(SectorCoordinate sector, int systemIndex) =>
            string.Format(CultureInfo.InvariantCulture, "{0}/{1}{2:000}", sector.Id, SystemSegmentPrefix, systemIndex);

        public static string PlanetId(string systemId, int planetIndex) =>
            string.Format(CultureInfo.InvariantCulture, "{0}/{1}{2:00}", systemId, PlanetSegmentPrefix, planetIndex);

        /// <summary>True for both a canonical system id and a canonical planet id (a planet id's system prefix parses fine too).</summary>
        public static bool TryParseSystemId(string id, out SectorCoordinate sector, out int systemIndex)
        {
            sector = default; systemIndex = -1;
            if (string.IsNullOrEmpty(id)) return false;

            string[] parts = id.Split('/');
            if (parts.Length < 2) return false;
            if (!SectorCoordinate.TryParse(parts[0], out sector)) return false;
            if (!parts[1].StartsWith(SystemSegmentPrefix, StringComparison.Ordinal)) return false;
            return int.TryParse(parts[1].Substring(SystemSegmentPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out systemIndex);
        }

        public static bool TryParsePlanetIndex(string planetWorldId, out int planetIndex)
        {
            planetIndex = -1;
            if (string.IsNullOrEmpty(planetWorldId)) return false;
            int slash = planetWorldId.LastIndexOf('/');
            if (slash < 0 || slash + 1 >= planetWorldId.Length) return false;
            string segment = planetWorldId.Substring(slash + 1);
            if (!segment.StartsWith(PlanetSegmentPrefix, StringComparison.Ordinal)) return false;
            return int.TryParse(segment.Substring(PlanetSegmentPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out planetIndex);
        }

        /// <summary>"sector.x.y.z/system.NNN/planet.NN" -> "sector.x.y.z/system.NNN". Uses the LAST slash, not the first (see class remarks).</summary>
        public static string SystemIdFromPlanetWorldId(string planetWorldId)
        {
            if (string.IsNullOrEmpty(planetWorldId)) return null;
            int slash = planetWorldId.LastIndexOf('/');
            if (slash <= 0) return null;
            string candidate = planetWorldId.Substring(0, slash);
            // Guard against being handed a system id (no trailing "/planet.NN") — return it unchanged, not truncated further.
            return TryParsePlanetIndex(planetWorldId, out _) ? candidate : planetWorldId;
        }

        public static bool IsPlanetId(string worldId) => TryParsePlanetIndex(worldId, out _);
        public static bool IsSystemId(string worldId) => !IsPlanetId(worldId) && TryParseSystemId(worldId, out _, out _);

        // --- VS-012: POI ids ("sector.x.y.z/system.NNN/poi.NN") --------------------
        public static string PoiId(string systemId, int poiIndex) =>
            string.Format(CultureInfo.InvariantCulture, "{0}/{1}{2:00}", systemId, PoiSegmentPrefix, poiIndex);

        public static bool TryParsePoiIndex(string poiWorldId, out int poiIndex)
        {
            poiIndex = -1;
            if (string.IsNullOrEmpty(poiWorldId)) return false;
            int slash = poiWorldId.LastIndexOf('/');
            if (slash < 0 || slash + 1 >= poiWorldId.Length) return false;
            string segment = poiWorldId.Substring(slash + 1);
            if (!segment.StartsWith(PoiSegmentPrefix, StringComparison.Ordinal)) return false;
            return int.TryParse(segment.Substring(PoiSegmentPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out poiIndex);
        }

        /// <summary>"sector.x.y.z/system.NNN/poi.NN" -&gt; "sector.x.y.z/system.NNN".</summary>
        public static string SystemIdFromPoiWorldId(string poiWorldId)
        {
            if (string.IsNullOrEmpty(poiWorldId)) return null;
            int slash = poiWorldId.LastIndexOf('/');
            if (slash <= 0) return null;
            string candidate = poiWorldId.Substring(0, slash);
            return TryParsePoiIndex(poiWorldId, out _) ? candidate : poiWorldId;
        }

        public static bool IsPoiId(string worldId) => TryParsePoiIndex(worldId, out _);
    }
}
