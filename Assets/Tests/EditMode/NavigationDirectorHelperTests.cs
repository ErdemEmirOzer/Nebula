using NUnit.Framework;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for NavigationDirector's pure static helper — a thin forward to <see cref="WorldIdCodec"/> (VS-008 §1).</summary>
    public class NavigationDirectorHelperTests
    {
        [Test]
        public void SystemIdFromPlanetWorldId_ExtractsTheCanonicalSystemId()
        {
            var sector = new SectorCoordinate(0, 0, 0);
            string systemId = WorldIdCodec.SystemId(sector, 2);
            string planetId = WorldIdCodec.PlanetId(systemId, 5);

            Assert.AreEqual(systemId, NavigationDirector.SystemIdFromPlanetWorldId(planetId));
        }

        [Test]
        public void SystemIdFromPlanetWorldId_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(NavigationDirector.SystemIdFromPlanetWorldId(null));
            Assert.IsNull(NavigationDirector.SystemIdFromPlanetWorldId(""));
        }
    }
}
