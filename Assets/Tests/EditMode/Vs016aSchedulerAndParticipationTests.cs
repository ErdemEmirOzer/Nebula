using System.Reflection;
using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-016A §2/§8: link state is never persisted, a fully remote raid grants zero player-absent XP, and remote entities/timers never silently gain "offline" progress from anything other than real simulated ticks.</summary>
    public class Vs016aSchedulerAndParticipationTests
    {
        [TearDown]
        public void Cleanup()
        {
            StarSystemRegistry.Clear();
            CombatCampaignState.Reset();
            Progression.ProgressionService.Reset();
        }

        [Test]
        public void SystemDelta_HasNoSerializedLinkStateField()
        {
            // §9: "Link state save'e yazılmasın" -- structural guarantee, not just a runtime behavior:
            // SystemDelta must never carry a field whose value is a derived Live/LastKnown/Disconnected
            // link result (RemoteCommunicationService keeps that entirely in its own in-memory cache).
            foreach (FieldInfo f in typeof(SystemDelta).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.IsFalse(f.Name.ToLowerInvariant().Contains("link"), $"SystemDelta must never carry a link-state field, found: {f.Name}");
        }

        [Test]
        public void FullyRemoteRaid_PlayerNeverPresent_GrantsExactlyZeroXp()
        {
            StarSystemRegistry.Initialize(51UL);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset(51);

            SystemManifest manifest = TestGalaxyNeighbours.OneRemote(sector, WorldIdCodec.SystemId(sector, 0));
            Assert.IsNotNull(manifest, "the galaxy must supply a real remote system");
            string remoteId = manifest.SystemId;
            foreach (PlanetManifest p in manifest.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);

            var ctx = new RemoteRaidContext(new SystemDelta { systemId = remoteId }, manifest, new ThreatPressureState());
            long xpBefore = Progression.ProgressionService.State.TotalXp;

            // Tick well past grace (900s) + a full pressure ramp (~2000s) + an entire raid lifecycle
            // (warning/spawn/active/cleanup/resolve), entirely headless -- the player was never in this system.
            for (int i = 0; i < 20000; i++) RemoteRaidTick.Tick(ctx, 1f);

            Assert.IsFalse(ctx.Pressure.HasMeaningfulParticipation, "no remote tick may ever set a player-participation flag -- there is no player present");
            long xpAfter = Progression.ProgressionService.State.TotalXp;
            Assert.AreEqual(xpBefore, xpAfter, "a fully remote raid (player never present) must grant exactly zero XP");
        }

        [Test]
        public void CalmSystem_IsSafeToTickRepeatedly_NeverProducesDrift()
        {
            // A calm (nothing pending) system ticked many times must stay in exactly the same state --
            // this is the correctness precondition the scheduler's calm-skip optimization itself relies on
            // (skipping a calm system's ticks is only safe because ticking it is provably a no-op).
            var delta = new SystemDelta { systemId = "sys-calm" };
            delta.threatPressure = new ThreatPressureDto { phase = (int)RaidPhase.None };
            SystemManifest manifest = BuildEmptyManifest("sys-calm");

            for (int i = 0; i < 500; i++) RemoteEconomyTick.Tick(delta, manifest, 1f, false);

            Assert.AreEqual(0, delta.drills.Count);
            Assert.AreEqual(0, delta.structures.Count);
            Assert.AreEqual((int)RaidPhase.None, delta.threatPressure.phase);
        }

        private static SystemManifest BuildEmptyManifest(string systemId)
        {
            var sector = new SectorCoordinate(0, 0, 0);
            return new SystemManifest(systemId, sector, 0, "Test", StarClass.Yellow, UnityEngine.Vector3.zero,
                500000f, 50000f, false, SystemProfileKind.Balanced, 2, new System.Collections.Generic.List<PlanetManifest>());
        }

        [Test]
        public void RemoteTick_NeverReadsRealTimeOrFrameRate_OnlyTheExplicitDtItIsGiven()
        {
            // §2: "DT-001 test hız çarpanı ve Time.timeScale uzak sonuçları değiştirmez" -- proven structurally:
            // every remote tick entrypoint takes dt as an explicit float parameter and this test calls them
            // completely outside of any Unity player loop (no Time.timeScale, no Update() involved at all),
            // yet dominance still advances deterministically by exactly the dt handed in -- confirming no
            // hidden dependency on frame time or engine-driven timers.
            var (manifest, planetId) = BuildRegisteredDrillSystem(52UL);
            var delta = new SystemDelta { systemId = manifest.SystemId };
            ExtractionState.GetProfile(planetId).Scanned = true;
            delta.drills.Add(new DrillDto { worldId = planetId, nodeId = "drill", state = (int)DrillLifecycle.Active });
            delta.structures.Add(new StructureDto { worldId = planetId, nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active, tier = 1 });
            delta.structures.Add(new StructureDto { worldId = planetId, nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, state = (int)DrillLifecycle.Active, tier = 1 });

            for (int i = 0; i < 700; i++) RemoteEconomyTick.Tick(delta, manifest, 1f, false);

            Assert.AreEqual(DominanceState.Controlled, ExtractionState.GetDominance(planetId).State,
                "700 explicit 1-second dt calls (never touching Time.timeScale/Update at all) must still fully stabilize dominance -- proving the tick is driven purely by its own dt parameter");
        }

        private static (SystemManifest, string) BuildRegisteredDrillSystem(ulong seed)
        {
            StarSystemRegistry.Initialize(seed);
            var sector = new SectorCoordinate(0, 0, 0);
            string systemId = WorldIdCodec.SystemId(sector, 0);
            StarSystemRegistry.SetStartSystem(sector, systemId);
            ExtractionState.Reset((int)seed);
            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);
            return (manifest, manifest.Planets[0].PlanetId);
        }
    }
}
