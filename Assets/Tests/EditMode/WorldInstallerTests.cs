using NUnit.Framework;
using Nebula.Sectors;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for WorldInstaller's non-RNG campaign-reset/start-manifest phase (TF-002 §9).</summary>
    public class WorldInstallerTests
    {
        [TearDown]
        public void Cleanup() => StarSystemRegistry.Clear();

        [Test]
        public void PrepareSession_SameSeed_ProducesTheSameStartSystemId_AndSameStartManifest()
        {
            var context1 = new NebulaContext();
            new WorldInstaller().PrepareSession(4242, context1);
            string firstId = context1.StartSystemId;
            SystemManifest firstManifest = context1.StartSystem;

            var context2 = new NebulaContext();
            new WorldInstaller().PrepareSession(4242, context2);
            string secondId = context2.StartSystemId;
            SystemManifest secondManifest = context2.StartSystem;

            Assert.AreEqual(firstId, secondId);
            Assert.AreEqual(firstManifest.Planets.Count, secondManifest.Planets.Count);
            Assert.AreEqual(firstManifest.StarClass, secondManifest.StarClass);
            Assert.AreEqual(firstManifest.AbsoluteCenter, secondManifest.AbsoluteCenter);
        }

        [Test]
        public void PrepareSession_NeverProducesAPoiOnTheStartSystem()
        {
            var context = new NebulaContext();
            new WorldInstaller().PrepareSession(777, context);
            Assert.IsTrue(context.StartSystem.IsStartSystem);
        }

        [Test]
        public void PrepareSession_ResetsCampaignRegistries_EachCall()
        {
            var context1 = new NebulaContext();
            new WorldInstaller().PrepareSession(1, context1);
            Poi.PoiManifestRepository.Ledger.RecordVisit("some-other-system");
            Assert.Greater(Poi.PoiManifestRepository.Ledger.Count, 0);

            var context2 = new NebulaContext();
            new WorldInstaller().PrepareSession(2, context2);
            Assert.AreEqual(0, Poi.PoiManifestRepository.Ledger.Count, "a fresh PrepareSession must reset the POI visit ledger exactly like the original monolith's Awake() did");
        }
    }
}
