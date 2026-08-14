using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-016A §7: the pieces of remote economy VS-008/VS-011 did NOT already
    /// cover -- dominance stabilization progression and structure tier
    /// upgrade completion. Drill production and Collector route movement are
    /// deliberately NOT re-tested here: SystemStreamingDirector.TickDeltaDataOnly/
    /// TickLogisticsDataOnly already remote-tick those (VS-008 §6 / VS-011),
    /// and RemoteEconomyTick calls alongside that mechanism rather than
    /// duplicating it -- re-testing it here would test the wrong file.
    /// </summary>
    public class Vs016aRemoteEconomyTests
    {
        [TearDown]
        public void Cleanup() => StarSystemRegistry.Clear();

        private static (SystemManifest manifest, string planetId) BuildRegisteredSystem(ulong seed)
        {
            StarSystemRegistry.Initialize(seed);
            var sector = new SectorCoordinate(0, 0, 0);
            string systemId = WorldIdCodec.SystemId(sector, 0);
            StarSystemRegistry.SetStartSystem(sector, systemId);
            ExtractionState.Reset((int)seed);

            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);
            return (manifest, manifest.Planets[0].PlanetId);
        }

        [Test]
        public void RemoteDominance_StabilizesToControlled_WhenAllConditionsMet()
        {
            var (manifest, planetId) = BuildRegisteredSystem(11UL);
            var delta = new SystemDelta { systemId = manifest.SystemId };
            ExtractionState.GetProfile(planetId).Scanned = true;
            delta.drills.Add(new DrillDto { worldId = planetId, nodeId = "drill", state = (int)DrillLifecycle.Active });
            delta.structures.Add(new StructureDto { worldId = planetId, nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active, tier = 1 });
            delta.structures.Add(new StructureDto { worldId = planetId, nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, state = (int)DrillLifecycle.Active, tier = 1 });

            Assert.AreEqual(DominanceState.Unowned, ExtractionState.GetDominance(planetId).State);

            for (int i = 0; i < 700; i++) RemoteEconomyTick.Tick(delta, manifest, 1f, false);

            Assert.AreEqual(DominanceState.Controlled, ExtractionState.GetDominance(planetId).State,
                "dominance must stabilize to Controlled purely from a remote system's own tick -- it was frozen the instant a system left Full before VS-016A");
        }

        [Test]
        public void RemoteStructureUpgrade_CompletesAndPreservesHealthPercentage()
        {
            var (manifest, planetId) = BuildRegisteredSystem(12UL);
            var delta = new SystemDelta { systemId = manifest.SystemId };
            var command = new StructureDto
            {
                worldId = planetId, nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active,
                tier = 1, upgrading = true, upgradeTargetTier = 2, upgradeProgress01 = 0f,
                armor = SurfaceStructureCatalog.Command.ArmorMax * 0.5f, hull = SurfaceStructureCatalog.Command.HullMax * 0.5f
            };
            delta.structures.Add(command);

            float totalSeconds = PlanetaryDefenseCatalog.CommandT2.BuildSeconds;
            for (int i = 0; i < (int)totalSeconds + 2; i++) RemoteEconomyTick.Tick(delta, manifest, 1f, false);

            Assert.IsFalse(command.upgrading, "the upgrade job must complete within its own real duration");
            Assert.AreEqual(2, command.tier);
            Assert.AreEqual(PlanetaryDefenseCatalog.CommandT2.ArmorMax * 0.5f, command.armor, 1f,
                "tier upgrade must preserve the exact armor PERCENTAGE (50%) at the new tier's max -- never a free full heal");
            Assert.AreEqual(PlanetaryDefenseCatalog.CommandT2.HullMax * 0.5f, command.hull, 1f);
        }
    }
}
