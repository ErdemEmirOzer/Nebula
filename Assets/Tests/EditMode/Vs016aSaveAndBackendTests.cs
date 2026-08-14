using System.IO;
using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-016A §12/§9/§10/§11: save v14 schema/migration, pending-attack conservation, communication/command/attention backends.</summary>
    public class Vs016aSaveAndBackendTests
    {
        private const int TestSlot = 96;

        [TearDown]
        public void Cleanup()
        {
            string main = SaveService.SlotFile(TestSlot);
            string dir = Path.GetDirectoryName(main);
            if (dir == null) return;
            foreach (string f in new[]
            {
                main, Path.Combine(dir, $"slot{TestSlot}.bak1.json"),
                Path.Combine(dir, $"slot{TestSlot}.bak2.json"), Path.Combine(dir, $"slot{TestSlot}.bak3.json"),
                Path.Combine(dir, $"slot{TestSlot}.tmp.json")
            })
            {
                if (File.Exists(f)) File.Delete(f);
            }
            StarSystemRegistry.Clear();
            RemoteCommunicationService.ResetForSession();
        }

        [Test]
        public void CurrentSchema_IsPinned()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
        }

        [Test]
        public void PreV14SystemDelta_SafeDefaults_NoRemoteState()
        {
            var delta = new SystemDelta();
            Assert.AreEqual(0, delta.pendingAttacks.Count);
            Assert.IsTrue(delta.remoteProbe == null || delta.remoteProbe.phase == 0);
            Assert.IsNull(delta.pendingReconnectSummary);
            Assert.IsTrue(delta.IsEmpty, "an otherwise-empty pre-v14 delta must stay Empty -- the new v14 fields must never make an old delta look non-empty");
        }

        [Test]
        public void SystemDelta_PendingAttackAndProbe_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new SystemDelta { systemId = "sys001" };
            delta.pendingAttacks.Add(new PendingAttackDto
            {
                attackId = "atk-1", attackerId = "enemy-1", targetId = "structure-1", kind = (int)ProjectileKind.Plasma,
                faction = (int)ProjectileFaction.Hostile, damageAmount = 42f, remainingFlightSeconds = 1.5f,
                splashRadius = 120f, splashMaxFractionOfDirect = 0.25f, isEm = false, interceptable = true,
                interceptionDurabilityRemaining = 10f, applied = false
            });
            delta.remoteProbe = new RemoteProbeDto { phase = (int)RemoteProbePhase.Scanning, enemyId = "probe-1", scanCompleted = false };
            delta.pendingReconnectSummary = new ReconnectSummaryDto { systemId = "sys001", raidResultText = "test result" };
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            SystemDelta restored = loaded.systemDeltas[0];
            Assert.AreEqual(1, restored.pendingAttacks.Count);
            Assert.AreEqual("atk-1", restored.pendingAttacks[0].attackId);
            Assert.AreEqual(42f, restored.pendingAttacks[0].damageAmount);
            Assert.AreEqual((int)RemoteProbePhase.Scanning, restored.remoteProbe.phase);
            Assert.AreEqual("probe-1", restored.remoteProbe.enemyId);
            Assert.IsNotNull(restored.pendingReconnectSummary);
            Assert.AreEqual("test result", restored.pendingReconnectSummary.raidResultText);
        }

        [Test]
        public void Reload_IsIdempotent_NeverDuplicatesPendingAttacksOnRepeatedLoad()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new SystemDelta { systemId = "sys001" };
            delta.pendingAttacks.Add(new PendingAttackDto { attackId = "atk-1", damageAmount = 10f, remainingFlightSeconds = 1f });
            payload.systemDeltas.Add(delta);
            SaveService.Save(TestSlot, payload, 1);

            SavePayload firstLoad = SaveService.Load(TestSlot);
            SavePayload secondLoad = SaveService.Load(TestSlot);

            Assert.AreEqual(1, firstLoad.systemDeltas[0].pendingAttacks.Count);
            Assert.AreEqual(1, secondLoad.systemDeltas[0].pendingAttacks.Count, "loading the same save twice must never duplicate a pending attack");
        }

        // --- PendingAttack conservation (§5) ------------------------------------------

        [Test]
        public void PendingAttack_Tick_NeverAppliesBeforeFlightTimeElapses()
        {
            var attack = PendingAttack.CaptureFromLive("a1", "enemy-1", "ship", ProjectileKind.Plasma, ProjectileFaction.Hostile,
                50f, remainingFlightSeconds: 3f, interceptable: false, interceptionDurabilityRemaining: 0f);

            Assert.IsFalse(attack.Tick(1f));
            Assert.IsFalse(attack.Tick(1f));
            Assert.IsFalse(attack.Applied);
            Assert.IsTrue(attack.Tick(1f), "flight time must be fully elapsed before the attack is ready to resolve");
        }

        [Test]
        public void PendingAttack_MarkApplied_IsIdempotent_NeverAppliesTwice()
        {
            var attack = PendingAttack.CaptureFromLive("a1", "enemy-1", "ship", ProjectileKind.Plasma, ProjectileFaction.Hostile,
                50f, 0f, false, 0f);
            attack.MarkApplied();
            Assert.IsTrue(attack.Applied);
            attack.MarkApplied(); // safe no-op
            Assert.IsTrue(attack.Applied);
        }

        [Test]
        public void PendingAttack_Interceptable_ConsumedDownNeverDealsDamage()
        {
            var attack = PendingAttack.CaptureFromLive("a1", "enemy-1", "ship", ProjectileKind.EnemyPlasma, ProjectileFaction.Hostile,
                50f, 5f, interceptable: true, interceptionDurabilityRemaining: 10f);
            attack.ApplyInterceptionHit(6f);
            Assert.IsFalse(attack.InterceptedDown);
            attack.ApplyInterceptionHit(6f);
            Assert.IsTrue(attack.InterceptedDown);
            Assert.IsTrue(attack.Applied, "a fully-intercepted attack must be marked Applied -- consumed without ever dealing its damage");
        }

        [Test]
        public void PendingAttack_CaptureFromLive_ResolvesSplashAndEmFromSharedWeaponCatalog()
        {
            var plasma = PendingAttack.CaptureFromLive("a1", "e", "t", ProjectileKind.Plasma, ProjectileFaction.Player, 10f, 1f, false, 0f);
            Assert.AreEqual(WeaponCatalog.PlasmaSplashRadius, plasma.SplashRadius);
            Assert.AreEqual(WeaponCatalog.PlasmaSplashMaxFractionOfDirect, plasma.SplashMaxFractionOfDirect);
            Assert.IsFalse(plasma.IsEm);

            var em = PendingAttack.CaptureFromLive("a2", "e", "t", ProjectileKind.Em, ProjectileFaction.Player, 10f, 1f, false, 0f);
            Assert.IsTrue(em.IsEm);
            Assert.AreEqual(WeaponCatalog.EmDisruptSeconds, em.EmDisruptSeconds);
            Assert.AreEqual(WeaponCatalog.EmImmunityAfterDisruptSeconds, em.EmImmunityAfterDisruptSeconds);
        }

        // --- Communication / link state (§9) ------------------------------------------

        [Test]
        public void RemoteCommunication_Disconnected_NeverLeaksLiveData_ReconnectProducesExactlyOneSummary()
        {
            StarSystemRegistry.Initialize(21UL);
            var sector = new SectorCoordinate(0, 0, 0);
            string startId = WorldIdCodec.SystemId(sector, 0);
            StarSystemRegistry.SetStartSystem(sector, startId);
            string remoteId = TestGalaxyNeighbours.OneRemote(sector, startId)?.SystemId;
            Assert.IsNotNull(remoteId, "the galaxy must supply a real remote system");

            var knowledge = new SystemKnowledgeService();
            knowledge.InitializeNewGame(startId);
            knowledge.Raise(remoteId, SystemKnowledgeLevel.Located);

            SystemDelta delta = World.StarSystemRegistry.Deltas.GetOrCreate(remoteId);
            delta.threatPressure = new ThreatPressureDto { resultApplied = true, stage = (int)RaidStage.Pirate, targetPlanetWorldId = "x" };

            // No active Command anywhere -> not Live (a valid delta snapshot already exists, so this correctly
            // resolves to LastKnown rather than Disconnected -- either way, live data must never leak here).
            SystemLinkState link = RemoteCommunicationService.GetLink(remoteId, startId, knowledge);
            Assert.AreNotEqual(SystemLinkState.Live, link);
            Assert.IsNull(delta.pendingReconnectSummary, "a not-Live system must never produce a reconnect summary -- there was no reconnect yet");

            // Simulate reconnect: both ends now have active Command (drive it through the resolver's own live/delta dual path via structures).
            delta.structures.Add(new StructureDto { worldId = "x", nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active });
            SystemDelta startDelta = World.StarSystemRegistry.Deltas.GetOrCreate(startId);
            startDelta.structures.Add(new StructureDto { worldId = "y", nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active });

            SystemLinkState after = RemoteCommunicationService.GetLink(remoteId, startId, knowledge);
            Assert.AreEqual(SystemLinkState.Live, after);
            Assert.IsNotNull(delta.pendingReconnectSummary, "reconnecting (Disconnected -> Live) must produce exactly one merged summary");

            ReconnectSummaryDto consumed = RemoteCommunicationService.ConsumeReconnectSummary(remoteId);
            Assert.IsNotNull(consumed);
            Assert.IsNull(delta.pendingReconnectSummary, "the summary must be cleared once shown -- never shown twice");
            Assert.IsNull(RemoteCommunicationService.ConsumeReconnectSummary(remoteId), "a second consume call must return null");
        }

        // --- Command API (§10) --------------------------------------------------------

        [Test]
        public void RemoteCommand_Disconnected_RejectsEveryNewOrder()
        {
            var delta = new SystemDelta { systemId = "sys001" };
            RemoteCommandRejectReason r1 = RemoteCommandService.SetInterventionPolicy(delta, SystemLinkState.Disconnected, InterventionPolicy.SystemIntervention);
            RemoteCommandRejectReason r2 = RemoteCommandService.SetRoutePaused(delta, SystemLinkState.Disconnected, "route-1", true);
            RemoteCommandRejectReason r3 = RemoteCommandService.SetMaintenancePolicy(delta, SystemLinkState.Disconnected, "cmd-1", MaintenancePolicy.Emergency);

            Assert.AreEqual(RemoteCommandRejectReason.Disconnected, r1);
            Assert.AreEqual(RemoteCommandRejectReason.Disconnected, r2);
            Assert.AreEqual(RemoteCommandRejectReason.Disconnected, r3);
        }

        [Test]
        public void RemoteCommand_Live_AllowedOrdersSucceed()
        {
            var delta = new SystemDelta { systemId = "sys001" };
            RemoteCommandRejectReason reason = RemoteCommandService.SetInterventionPolicy(delta, SystemLinkState.Live, InterventionPolicy.DefendInPlace);
            Assert.AreEqual(RemoteCommandRejectReason.None, reason);
            Assert.AreEqual((int)InterventionPolicy.DefendInPlace, delta.interventionGroup.policy);
        }

        [Test]
        public void RemoteCommand_ForbiddenActions_AlwaysRejectRegardlessOfLinkState()
        {
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectBuildOrUpgrade());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectDirectRepair());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectDismantle());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectTransfer());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectProduction());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectScanOrPoi());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectEvacuateOrReclaim());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectVehicleProduction());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectCrossSystemFleet());
            Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely, RemoteCommandService.RejectTeleport());
        }

        // --- Attention (§11) -----------------------------------------------------------
        // VS-016A reuses the existing VS-010 §4 attention backend (World.AttentionQuery /
        // Extraction.AttentionResolver -- already one card per system, priority-ordered,
        // capped at 3, purely derived) rather than a second one; the only real gap fixed
        // here is that AttentionQuery.BuildInputs never read a remote system's own
        // ThreatPressureDto at all, so a raid frozen/running in a demoted system never
        // surfaced as an attention card. These tests confirm that fix.

        [Test]
        public void AttentionQuery_RemoteSystemActiveRaid_NowSurfacesAsAnAttentionCard()
        {
            StarSystemRegistry.Initialize(31UL);
            var sector = new SectorCoordinate(0, 0, 0);
            string startId = WorldIdCodec.SystemId(sector, 0);
            StarSystemRegistry.SetStartSystem(sector, startId);
            string remoteId = TestGalaxyNeighbours.OneRemote(sector, startId)?.SystemId;
            Assert.IsNotNull(remoteId, "the galaxy must supply a real remote system");

            SystemDelta d = World.StarSystemRegistry.Deltas.GetOrCreate(remoteId);
            d.threatPressure = new ThreatPressureDto { phase = (int)RaidPhase.Active };

            var knowledge = new SystemKnowledgeService();
            knowledge.InitializeNewGame(startId);
            SystemAttentionInputs inputs = World.AttentionQuery.BuildInputs(remoteId, startId, knowledge);

            Assert.IsTrue(inputs.RaidDetected, "a remote system's own Active raid must now surface through BuildInputs");
            Assert.AreEqual(0f, inputs.RaidEtaSeconds);
        }

        [Test]
        public void AttentionQuery_RemoteSystemNoRaid_ReportsNoRaid()
        {
            StarSystemRegistry.Initialize(32UL);
            var sector = new SectorCoordinate(0, 0, 0);
            string startId = WorldIdCodec.SystemId(sector, 0);
            StarSystemRegistry.SetStartSystem(sector, startId);
            string remoteId = TestGalaxyNeighbours.OneRemote(sector, startId)?.SystemId;
            Assert.IsNotNull(remoteId, "the galaxy must supply a real remote system");

            SystemDelta d = World.StarSystemRegistry.Deltas.GetOrCreate(remoteId);
            d.threatPressure = new ThreatPressureDto { phase = (int)RaidPhase.None };

            var knowledge = new SystemKnowledgeService();
            knowledge.InitializeNewGame(startId);
            SystemAttentionInputs inputs = World.AttentionQuery.BuildInputs(remoteId, startId, knowledge);

            Assert.IsFalse(inputs.RaidDetected, "a remote system with no active raid must never fabricate one");
        }
    }
}
