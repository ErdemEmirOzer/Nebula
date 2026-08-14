using Nebula.Sectors;
using Nebula.World;

namespace Nebula.UI.Design
{
    /// <summary>
    /// Names a planet, from data that already exists.
    /// <para>
    /// L-05: internal ids were reaching the player. Everything else in the game
    /// resolves to a name through a catalog; planets could not, because
    /// <see cref="PlanetManifest"/> carries an index and an id and only systems
    /// have a <c>DisplayName</c>. So the interface printed
    /// <c>0.0.0/S03/P02</c> at people.
    /// </para>
    /// <para>
    /// The way out is composition, not invention: a planet id is its system's id
    /// plus an orbit index, and the system is named. "AETHERİA III" is the third
    /// orbit of Aetheria — which is how planets are named in the real sky, and
    /// costs no new content. A system that cannot be resolved falls back to the
    /// orbit alone rather than to the id, because a number the player can at least
    /// count beats a string they can only be confused by.
    /// </para>
    /// </summary>
    public static class NebulaPlanetName
    {
        /// <summary>Roman numerals for the orbit. No system in this game has enough planets to exhaust these.</summary>
        private static readonly string[] Orbits =
        {
            "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII",
        };

        /// <summary>
        /// The display name for a planet world id, upper-cased for the interface.
        /// Never returns the raw id.
        /// </summary>
        public static string Of(string planetWorldId)
        {
            if (string.IsNullOrEmpty(planetWorldId)) return "GEZEGEN";

            string orbit = OrbitNumeral(planetWorldId);
            string system = SystemName(planetWorldId);

            if (system == null) return orbit != null ? $"YÖRÜNGE {orbit}" : "GEZEGEN";
            return orbit != null ? $"{system} {orbit}" : system;
        }

        private static string OrbitNumeral(string planetWorldId)
        {
            if (!WorldIdCodec.TryParsePlanetIndex(planetWorldId, out int index)) return null;
            if (index < 0 || index >= Orbits.Length) return null;
            return Orbits[index];
        }

        /// <summary>
        /// The owning system's name, or <c>null</c>. Resolution can legitimately
        /// fail: a planet in a sector the player has never had streamed in has no
        /// manifest loaded, and asking for one is not worth a hitch.
        /// </summary>
        private static string SystemName(string planetWorldId)
        {
            int slash = planetWorldId.LastIndexOf('/');
            if (slash <= 0) return null;

            string systemId = planetWorldId.Substring(0, slash);
            if (StarSystemRegistry.Manifests == null) return null;

            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);
            if (manifest == null || string.IsNullOrEmpty(manifest.DisplayName)) return null;

            return NebulaText.Upper(manifest.DisplayName);
        }
    }

    /// <summary>
    /// Names a system. The counterpart to <see cref="NebulaPlanetName"/>, and the
    /// simpler half: systems have carried a <c>DisplayName</c> all along, so the
    /// only reason an id ever reached the player was that nothing asked for it.
    /// </summary>
    public static class NebulaSystemName
    {
        public static string Of(string systemId)
        {
            if (string.IsNullOrEmpty(systemId)) return "SİSTEM";
            if (StarSystemRegistry.Manifests == null) return "SİSTEM";

            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);
            return manifest == null || string.IsNullOrEmpty(manifest.DisplayName)
                ? "BİLİNMEYEN SİSTEM"
                : NebulaText.Upper(manifest.DisplayName);
        }
    }
}
