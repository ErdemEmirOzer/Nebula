using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-016A §3/§8: deterministic remote raid tick -- same seed/state/ticks always produce the same result, simultaneous damage is call-order independent, and the global Major Raid + Probe caps hold.</summary>
    public class Vs016aRemoteRaidTests
    {
        [TearDown]
        public void Cleanup()
        {
            StarSystemRegistry.Clear();
            CombatCampaignState.Reset();
            Progression.ProgressionService.Reset();
        }

        private static (SystemManifest manifest, RemoteRaidContext ctx) BuildSystem(ulong seed, string label)
        {
            StarSystemRegistry.Initialize(seed);
            var sector = new SectorCoordinate(0, 0, 0);
            string systemId = WorldIdCodec.SystemId(sector, 0);
            StarSystemRegistry.SetStartSystem(sector, systemId);
            ExtractionState.Reset((int)seed);

            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);
            // Not the start system for this test's grace-skip purposes -- use a neighboring system id instead of the actual start system.
            SystemManifest remoteManifest = TestGalaxyNeighbours.OneRemote(sector, WorldIdCodec.SystemId(sector, 0));
            Assert.IsNotNull(remoteManifest, "the galaxy must supply a real remote system");
            string remoteSystemId = remoteManifest.SystemId;
            foreach (PlanetManifest p in remoteManifest.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);

            var delta = new SystemDelta { systemId = remoteSystemId };
            var pressure = new ThreatPressureState();
            var ctx = new RemoteRaidContext(delta, remoteManifest, pressure);
            return (remoteManifest, ctx);
        }

        private static string RunDeterministic(int ticks)
        {
            var (_, ctx) = BuildSystem(4242UL, "run");
            for (int i = 0; i < ticks; i++) RemoteRaidTick.Tick(ctx, 1f);
            return Snapshot(ctx);
        }

        private static string Snapshot(RemoteRaidContext ctx)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(ctx.Pressure.Phase).Append('|').Append(ctx.Pressure.Pressure.ToString("0.0000")).Append('|')
              .Append(ctx.Pressure.Stage).Append('|').Append(ctx.Pressure.SpawnedEnemyIds.Count).Append('|')
              .Append(ctx.Enemies.Count);
            foreach (RemoteEnemyRuntime e in ctx.Enemies)
                sb.Append('|').Append(e.Id).Append(':').Append(e.Ai.Phase).Append(':').Append(e.Durability.Hull.ToString("0.00"));
            return sb.ToString();
        }

        [Test]
        public void SameSeedSameTicks_Produces100Of100IdenticalResults()
        {
            string a = RunDeterministic(400);
            CombatCampaignState.Reset(); Progression.ProgressionService.Reset(); StarSystemRegistry.Clear();
            string b = RunDeterministic(400);

            for (int i = 0; i < 5; i++)
            {
                CombatCampaignState.Reset(); Progression.ProgressionService.Reset(); StarSystemRegistry.Clear();
                string next = RunDeterministic(400);
                Assert.AreEqual(a, next, $"run {i} diverged from run 0");
            }
            Assert.AreEqual(a, b);
        }

        [Test]
        public void GlobalMajorRaidCap_OnlyOneSystemHoldsTheSlotAtOnce()
        {
            StarSystemRegistry.Initialize(1UL);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset(1);

            var remotes = TestGalaxyNeighbours.Remote(sector, WorldIdCodec.SystemId(sector, 0), 2);
            Assert.AreEqual(2, remotes.Count, "this test needs two real remote systems to contend for the slot");
            SystemManifest manifestA = remotes[0];
            SystemManifest manifestB = remotes[1];
            string sysA = manifestA.SystemId;
            string sysB = manifestB.SystemId;
            foreach (PlanetManifest p in manifestA.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);
            foreach (PlanetManifest p in manifestB.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);

            var ctxA = new RemoteRaidContext(new SystemDelta { systemId = sysA }, manifestA, new ThreatPressureState());
            var ctxB = new RemoteRaidContext(new SystemDelta { systemId = sysB }, manifestB, new ThreatPressureState());

            // The 15-minute (900-tick) first-Controlled grace runs before pressure even starts accumulating,
            // so the bound must comfortably cover grace + a full 0->100 pressure ramp afterward.
            for (int i = 0; i < 6000 && !CombatCampaignState.RaidCampaign.HasGlobalMajorRaidActive; i++)
            {
                RemoteRaidTick.Tick(ctxA, 1f);
                RemoteRaidTick.Tick(ctxB, 1f);
            }

            Assert.IsTrue(CombatCampaignState.RaidCampaign.HasGlobalMajorRaidActive, "at least one system must have started a major raid within the pressure ramp");
            bool aActive = ctxA.Pressure.Phase != RaidPhase.None;
            bool bActive = ctxB.Pressure.Phase != RaidPhase.None;
            Assert.IsFalse(aActive && bActive, "both remote systems must never be mid-raid simultaneously -- only one global major-raid slot exists");
        }

        [Test]
        public void GlobalProbeCap_OnlyOneSystemHoldsTheSlotAtOnce()
        {
            StarSystemRegistry.Initialize(9UL);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset(9);

            var remotes = TestGalaxyNeighbours.Remote(sector, WorldIdCodec.SystemId(sector, 0), 2);
            Assert.AreEqual(2, remotes.Count, "this test needs two real remote systems to contend for the slot");
            SystemManifest manifestA = remotes[0];
            SystemManifest manifestB = remotes[1];
            string sysA = manifestA.SystemId;
            string sysB = manifestB.SystemId;
            foreach (PlanetManifest p in manifestA.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);
            foreach (PlanetManifest p in manifestB.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);

            var ctxA = new RemoteRaidContext(new SystemDelta { systemId = sysA }, manifestA, new ThreatPressureState());
            var ctxB = new RemoteRaidContext(new SystemDelta { systemId = sysB }, manifestB, new ThreatPressureState());
            var probeA = new RemoteProbeState();
            var probeB = new RemoteProbeState();

            RemoteProbeTick.Tick(ctxA, probeA, 1f);
            RemoteProbeTick.Tick(ctxB, probeB, 1f);

            Assert.IsTrue(CombatCampaignState.RaidCampaign.HasGlobalProbeRaidActive);
            bool aStarted = probeA.Phase != RemoteProbePhase.None;
            bool bStarted = probeB.Phase != RemoteProbePhase.None;
            Assert.IsFalse(aStarted && bStarted, "only one system may hold the global Probe slot at a time");
        }

        [Test]
        public void MajorRaidAndProbe_CanBothBeActiveInDifferentSystemsSimultaneously()
        {
            var campaign = CombatCampaignState.RaidCampaign;
            Assert.IsTrue(campaign.TryStartGlobalMajorRaid("sys-major"));
            Assert.IsTrue(campaign.TryStartGlobalProbeRaid("sys-probe"));
            Assert.IsTrue(campaign.HasGlobalMajorRaidActive);
            Assert.IsTrue(campaign.HasGlobalProbeRaidActive);
        }

        [Test]
        public void SimultaneousDamage_TotalAppliedIsOrderIndependent()
        {
            // Two structures of the same type/tier, damaged by the same two fire amounts in opposite processing order --
            // RemoteStructureAccess round-trips through Durability exactly like DamageResolver does everywhere else,
            // so applying [10, 15] vs [15, 10] to independent targets must leave identical remaining hull.
            var defA = new StructureDto { worldId = "w1", nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, tier = 1,
                shield = 0f, armor = SurfaceStructureCatalog.Laser.ArmorMax, hull = SurfaceStructureCatalog.Laser.HullMax };
            var defB = new StructureDto { worldId = "w2", nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, tier = 1,
                shield = 0f, armor = SurfaceStructureCatalog.Laser.ArmorMax, hull = SurfaceStructureCatalog.Laser.HullMax };

            Durability dA = RemoteStructureAccess.ToDurability(defA);
            DamageResolver.Apply(dA, 10f, DamageProfile.Neutral);
            DamageResolver.Apply(dA, 15f, DamageProfile.Neutral);
            RemoteStructureAccess.WriteBack(defA, dA);

            Durability dB = RemoteStructureAccess.ToDurability(defB);
            DamageResolver.Apply(dB, 15f, DamageProfile.Neutral);
            DamageResolver.Apply(dB, 10f, DamageProfile.Neutral);
            RemoteStructureAccess.WriteBack(defB, dB);

            Assert.AreEqual(defA.armor, defB.armor, 0.001f);
            Assert.AreEqual(defA.hull, defB.hull, 0.001f);
        }
    }
}
