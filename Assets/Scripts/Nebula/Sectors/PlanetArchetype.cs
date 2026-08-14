using Nebula.Extraction;

namespace Nebula.Sectors
{
    /// <summary>Cosmetic/placeholder-visual archetype, a fixed 1:1 mapping from ResourceFamily (VS-009 §3/§6).</summary>
    public enum PlanetArchetype { MetallicRocky, Biomass, UpperAtmospherePlatform, FloatingOceanic, CrystalShell }

    /// <summary>Which surface-anchor height band a materialized archetype uses.</summary>
    public enum SurfaceAnchorCategory { Solid, Water, Gas }

    public static class PlanetArchetypeCatalog
    {
        public static PlanetArchetype ForFamily(ResourceFamily family) => family switch
        {
            ResourceFamily.Metal => PlanetArchetype.MetallicRocky,
            ResourceFamily.Wood => PlanetArchetype.Biomass,
            ResourceFamily.Gas => PlanetArchetype.UpperAtmospherePlatform,
            ResourceFamily.Water => PlanetArchetype.FloatingOceanic,
            ResourceFamily.Diamond => PlanetArchetype.CrystalShell,
            _ => PlanetArchetype.MetallicRocky
        };

        public static SurfaceAnchorCategory AnchorCategory(PlanetArchetype archetype) => archetype switch
        {
            PlanetArchetype.FloatingOceanic => SurfaceAnchorCategory.Water,
            PlanetArchetype.UpperAtmospherePlatform => SurfaceAnchorCategory.Gas,
            _ => SurfaceAnchorCategory.Solid
        };

        public const float SolidAnchorRadiusMultiplier = 1.01f;
        public const float WaterAnchorRadiusMultiplier = 1.03f;
        public const float GasAnchorRadiusMultiplier = 1.08f;

        /// <summary>Total anchor distance from the planet center, expressed as a multiple of planet radius.</summary>
        public static float AnchorRadiusMultiplier(SurfaceAnchorCategory category) => category switch
        {
            SurfaceAnchorCategory.Water => WaterAnchorRadiusMultiplier,
            SurfaceAnchorCategory.Gas => GasAnchorRadiusMultiplier,
            _ => SolidAnchorRadiusMultiplier
        };

        public static string Label(PlanetArchetype archetype) => archetype switch
        {
            PlanetArchetype.MetallicRocky => "Metalik/Kayalık",
            PlanetArchetype.Biomass => "Biyokütle",
            PlanetArchetype.UpperAtmospherePlatform => "Üst Atmosfer Platformu",
            PlanetArchetype.FloatingOceanic => "Yüzer Okyanus",
            PlanetArchetype.CrystalShell => "Kristal Kabuk",
            _ => "?"
        };
    }
}
