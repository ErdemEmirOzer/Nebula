using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the ResourceFamily->archetype->anchor-category mapping (VS-009 §3/§5/§10.6).</summary>
    public class PlanetArchetypeCatalogTests
    {
        [Test]
        public void ForFamily_IsATotalOneToOneMapping_ForAllFiveFamilies()
        {
            Assert.AreEqual(PlanetArchetype.MetallicRocky, PlanetArchetypeCatalog.ForFamily(ResourceFamily.Metal));
            Assert.AreEqual(PlanetArchetype.Biomass, PlanetArchetypeCatalog.ForFamily(ResourceFamily.Wood));
            Assert.AreEqual(PlanetArchetype.UpperAtmospherePlatform, PlanetArchetypeCatalog.ForFamily(ResourceFamily.Gas));
            Assert.AreEqual(PlanetArchetype.FloatingOceanic, PlanetArchetypeCatalog.ForFamily(ResourceFamily.Water));
            Assert.AreEqual(PlanetArchetype.CrystalShell, PlanetArchetypeCatalog.ForFamily(ResourceFamily.Diamond));
        }

        [Test]
        public void AnchorRadiusMultiplier_MatchesSpec_SolidWaterGas()
        {
            Assert.AreEqual(1.01f, PlanetArchetypeCatalog.AnchorRadiusMultiplier(SurfaceAnchorCategory.Solid), 0.0001f);
            Assert.AreEqual(1.03f, PlanetArchetypeCatalog.AnchorRadiusMultiplier(SurfaceAnchorCategory.Water), 0.0001f);
            Assert.AreEqual(1.08f, PlanetArchetypeCatalog.AnchorRadiusMultiplier(SurfaceAnchorCategory.Gas), 0.0001f);
        }

        [Test]
        public void AnchorCategory_OnlyWaterAndGasArchetypesAreNonSolid()
        {
            Assert.AreEqual(SurfaceAnchorCategory.Solid, PlanetArchetypeCatalog.AnchorCategory(PlanetArchetype.MetallicRocky));
            Assert.AreEqual(SurfaceAnchorCategory.Solid, PlanetArchetypeCatalog.AnchorCategory(PlanetArchetype.Biomass));
            Assert.AreEqual(SurfaceAnchorCategory.Solid, PlanetArchetypeCatalog.AnchorCategory(PlanetArchetype.CrystalShell));
            Assert.AreEqual(SurfaceAnchorCategory.Water, PlanetArchetypeCatalog.AnchorCategory(PlanetArchetype.FloatingOceanic));
            Assert.AreEqual(SurfaceAnchorCategory.Gas, PlanetArchetypeCatalog.AnchorCategory(PlanetArchetype.UpperAtmospherePlatform));
        }
    }
}
