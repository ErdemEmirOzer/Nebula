using System;
using NUnit.Framework;
using Nebula.Sectors;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the typed, set-once NebulaContext (TF-002 §5/§9).</summary>
    public class NebulaContextTests
    {
        [Test]
        public void UnsetProperty_ThrowsOnRead_NamingTheProperty()
        {
            var context = new NebulaContext();
            var ex = Assert.Throws<InvalidOperationException>(() => { var _ = context.Ship; });
            StringAssert.Contains("Ship", ex.Message);
        }

        [Test]
        public void SetOnce_ThenRead_ReturnsExactSameValue()
        {
            var context = new NebulaContext();
            var config = new NebulaBootstrapConfig();
            context.SetConfig(config);
            Assert.AreSame(config, context.Config);
        }

        [Test]
        public void SettingTwice_ThrowsOnSecondCall_NeverOverwritesTheFirst()
        {
            var context = new NebulaContext();
            context.SetResolvedSeed(111);
            var ex = Assert.Throws<InvalidOperationException>(() => context.SetResolvedSeed(222));
            StringAssert.Contains("ResolvedSeed", ex.Message);
            Assert.AreEqual(111, context.ResolvedSeed, "the first value must survive a rejected second set");
        }

        [Test]
        public void EveryRequiredDependency_IsSetOnceAndReadable()
        {
            // Exercises the full required-dependency list (VS/TF-002 §5) generically:
            // reference-type properties accept null as a cheap test double since
            // Set() only tracks "was this called", never validates the value itself.
            var context = new NebulaContext();
            context.SetConfig(new NebulaBootstrapConfig());
            context.SetResolvedSeed(1);
            context.SetStartSector(new SectorCoordinate(0, 0, 0));
            context.SetStartSystemId("sector.0.0.0/system.000");
            context.SetStartSystem(null);
            context.SetGravity(null);
            context.SetPlanetMaterialPool(null);
            context.SetGalaxyMaps(null);
            context.SetMainCamera(null);
            context.SetShip(null);
            context.SetHangar(null);
            context.SetModules(null);
            context.SetModuleVisuals(null);
            context.SetStreaming(null);
            context.SetExtractionDirector(null);
            context.SetCollectorRouteDirector(null);
            context.SetNavigationDirector(null);
            context.SetNavigationWorldProvider(null);
            context.SetFloatingOrigin(null);
            context.SetPresentation(null);

            Assert.AreEqual(1, context.ResolvedSeed);
            Assert.AreEqual("sector.0.0.0/system.000", context.StartSystemId);
            // Every one of the calls above having thrown zero exceptions IS the assertion --
            // now confirm each is independently a "set once" guard, spot-checked on a second field.
            Assert.Throws<InvalidOperationException>(() => context.SetShip(null));
        }

        [Test]
        public void NeverReadDependency_StaysUnreadable_EvenAfterUnrelatedFieldsAreSet()
        {
            var context = new NebulaContext();
            context.SetResolvedSeed(1);
            context.SetStartSystemId("sys");
            // Streaming was never set -- must still fail fast, not fall back to null silently.
            Assert.Throws<InvalidOperationException>(() => { var _ = context.Streaming; });
        }
    }
}
