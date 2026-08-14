using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the environment duration resolver and its ONLY two
    /// live consumers, DrillRuntime/StructureRuntime build duration (VS-009
    /// §3/§10.7) — asserting production/storage/rarity/cost are untouched.
    /// </summary>
    public class EnvironmentDurationResolverTests
    {
        [Test]
        public void ScaleDuration_MatchesSpecMultipliers()
        {
            Assert.AreEqual(100f, EnvironmentDurationResolver.ScaleDuration(100f, EnvironmentClass.Calm), 0.001f);
            Assert.AreEqual(115f, EnvironmentDurationResolver.ScaleDuration(100f, EnvironmentClass.Harsh), 0.001f);
            Assert.AreEqual(130f, EnvironmentDurationResolver.ScaleDuration(100f, EnvironmentClass.Extreme), 0.001f);
        }

        private static PlanetProfile Profile(EnvironmentClass environment) => new PlanetProfile(
            new PlanetManifest(0, "sector.0.0.0/system.000/planet.00", ResourceFamily.Metal, Rarity.Rare,
                40000f, 0f, 0f, 0f, 1200f, recommendedStart: false, environment, surfaceLayoutSeed: 1UL));

        [Test]
        public void DrillRuntime_FreshBuild_BuildDurationScalesWithEnvironment_ProductionAndStorageUnaffected()
        {
            var calm = new DrillRuntime(Profile(EnvironmentClass.Calm), capsuleSeconds: 3f);
            var harsh = new DrillRuntime(Profile(EnvironmentClass.Harsh), capsuleSeconds: 3f);
            var extreme = new DrillRuntime(Profile(EnvironmentClass.Extreme), capsuleSeconds: 3f);

            Assert.AreEqual(30f, calm.BuildRemaining, 0.001f);
            Assert.AreEqual(34.5f, harsh.BuildRemaining, 0.001f);
            Assert.AreEqual(39f, extreme.BuildRemaining, 0.001f);

            // Production/storage are Rarity-driven only — identical across all three.
            Assert.AreEqual(calm.ProductionPerMinute, harsh.ProductionPerMinute);
            Assert.AreEqual(calm.ProductionPerMinute, extreme.ProductionPerMinute);
            Assert.AreEqual(calm.StorageCapacity, harsh.StorageCapacity);
            Assert.AreEqual(calm.StorageCapacity, extreme.StorageCapacity);
        }

        [Test]
        public void StructureRuntime_FreshBuild_BuildDurationScalesWithEnvironment()
        {
            SurfaceStructureDef def = SurfaceStructureCatalog.Command; // BuildSeconds = 60f
            var calm = new StructureRuntime(def, capsuleSeconds: 3f, EnvironmentClass.Calm);
            var harsh = new StructureRuntime(def, capsuleSeconds: 3f, EnvironmentClass.Harsh);
            var extreme = new StructureRuntime(def, capsuleSeconds: 3f, EnvironmentClass.Extreme);

            Assert.AreEqual(60f, calm.BuildRemaining, 0.001f);
            Assert.AreEqual(69f, harsh.BuildRemaining, 0.001f);
            Assert.AreEqual(78f, extreme.BuildRemaining, 0.001f);
        }
    }
}
