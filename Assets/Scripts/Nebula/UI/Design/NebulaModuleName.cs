using System.Collections.Generic;
using Nebula.Modules;

namespace Nebula.UI.Design
{
    /// <summary>
    /// The player-facing name of a module, in the language the rest of the
    /// interface is written in.
    /// <para>
    /// The progression screen read <see cref="ModuleDef.DisplayName"/> straight
    /// out of the catalog, so twelve English rows — "Logistics Cargo", "Hull
    /// Reinforcement", "Nuclear Laboratory" — sat inside a screen whose every
    /// other word is Turkish, including their own rejection reasons a few pixels
    /// to the right ("GELİŞİM PUANI YETERSİZ"). It reads as an untranslated
    /// placeholder, which is exactly what it was.
    /// </para>
    /// <para>
    /// Translating the catalog itself was not an option and should not be:
    /// <c>ContentFingerprint</c> hashes every module's display name into the
    /// content identity a save is validated against, so renaming one there would
    /// move a number this pass is pinned to. The catalog name stays the stable
    /// internal one; this is the presentation layer doing presentation, the same
    /// split <see cref="NebulaPlanetName"/> already uses.
    /// </para>
    /// <para>
    /// An id with no entry falls back to the catalog name rather than to an empty
    /// row or an id: a module added later shows up in English, which is visibly
    /// wrong and therefore gets fixed, instead of showing up blank.
    /// </para>
    /// </summary>
    public static class NebulaModuleName
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "industry.logistics_cargo",       "Lojistik Kargo" },
            { "industry.resource_processing",   "Kaynak İşleme" },
            { "industry.station_construction",  "İstasyon İnşası" },
            { "exploration.scanner",            "Tarayıcı" },
            { "exploration.hangar_bot",         "Hangar Botu" },
            { "exploration.engine_navigation",  "Motor Seyrüseferi" },
            { "defense.hull_reinforcement",     "Gövde Takviyesi" },
            { "defense.shield_systems",         "Kalkan Sistemleri" },
            { "defense.weapon_coordination",    "Silah Koordinasyonu" },
            { "nuclear.star_energy_storage",    "Yıldız Enerji Deposu" },
            { "nuclear.laboratory",             "Nükleer Laboratuvar" },
            { "nuclear.interstellar_logistics", "Yıldızlararası Lojistik" },
        };

        /// <summary>Every id this resolver knows. Test-facing.</summary>
        public static IEnumerable<string> KnownIds => Names.Keys;

        public static string Of(ModuleDef module)
        {
            if (module == null) return string.Empty;
            return Names.TryGetValue(module.Id, out string name) ? name : module.DisplayName;
        }

        public static string Of(string moduleId, string fallback = null)
        {
            if (string.IsNullOrEmpty(moduleId)) return fallback ?? string.Empty;
            return Names.TryGetValue(moduleId, out string name) ? name : (fallback ?? moduleId);
        }
    }
}
