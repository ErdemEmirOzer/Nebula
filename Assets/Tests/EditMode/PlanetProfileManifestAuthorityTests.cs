using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for PlanetProfile's manifest-only resource authority (VS-008 §7).</summary>
    public class PlanetProfileManifestAuthorityTests
    {
        [Test]
        public void ManifestConstructor_UsesTheManifestsFamilyAndRarityExactly_NeverRehashes()
        {
            var sector = new SectorCoordinate(0, 0, 0);
            string systemId = WorldIdCodec.SystemId(sector, 0);
            var manifest = new PlanetManifest(0, WorldIdCodec.PlanetId(systemId, 0), ResourceFamily.Diamond, Rarity.Legendary,
                40000f, 0f, 0f, 0f, 1200f, recommendedStart: false, EnvironmentClass.Calm, surfaceLayoutSeed: 1UL);

            var profile = new PlanetProfile(manifest);
            Assert.AreEqual(ResourceFamily.Diamond, profile.Family);
            Assert.AreEqual(Rarity.Legendary, profile.Rarity);
            Assert.AreEqual(manifest.PlanetId, profile.WorldId);
        }

        [Test]
        public void ExtractionState_GetProfile_ResolvesFromTheLiveManifest_WithoutAPlanetIdentity()
        {
            StarSystemRegistry.Initialize(555UL);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset(555);

            SystemManifest system = StarSystemRegistry.Manifests.ResolveSystem(WorldIdCodec.SystemId(sector, 0));
            Assert.Greater(system.Planets.Count, 0);
            PlanetManifest firstPlanet = system.Planets[0];

            // No PlanetIdentity was ever registered for this planet (its system was never materialized) —
            // GetProfile(string) must still resolve the exact manifest-authored family/rarity.
            PlanetProfile profile = ExtractionState.GetProfile(firstPlanet.PlanetId);
            Assert.IsNotNull(profile);
            Assert.AreEqual(firstPlanet.Family, profile.Family);
            Assert.AreEqual(firstPlanet.Rarity, profile.Rarity);

            StarSystemRegistry.Clear();
        }
    }
}
