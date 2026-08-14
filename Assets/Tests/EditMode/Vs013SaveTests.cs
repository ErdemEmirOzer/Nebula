using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the v11 combat save round-trip and v10-&gt;v11 migration presence-detection (VS-013 §10/§11).</summary>
    public class Vs013SaveTests
    {
        private const int TestSlot = 95;

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
        }

        [Test]
        public void ShipCombatDto_WithHardpointsAndProductionJob_RoundTripsExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var combat = new ShipCombatDto
            {
                shield = 250f, armor = 600f, hull = 900f, regenTimer = 3f,
                energy = 80f, weaponsLocked = false,
                boostRemaining = 0f, boostCooldownRemaining = 2f, damaged = false,
                targetEnemyId = "enemy-007", emLockTargetId = "enemy-007", emLockElapsed = 0.6f
            };
            combat.hardpoints.Add(new HardpointDto { index = 0, equipped = true, weaponFamily = 0, tier = 1, heat = 40f, overheated = false });
            combat.hardpoints.Add(new HardpointDto { index = 1, equipped = true, weaponFamily = 1, tier = 1, heat = 100f, overheated = true, overheatCooldownRemaining = 1.5f });
            combat.productionJob = new WeaponProductionJobDto
            {
                active = true, weaponFamily = 2, targetTier = 2, reservedParts = 15, materialId = "material.refined_gas",
                reservedMaterial = 5, totalSeconds = 30f, remainingSeconds = 12f
            };
            payload.shipCombat = combat;

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.IsNotNull(loaded.shipCombat);
            Assert.AreEqual(250f, loaded.shipCombat.shield);
            Assert.AreEqual(900f, loaded.shipCombat.hull);
            Assert.AreEqual(80f, loaded.shipCombat.energy);
            Assert.AreEqual("enemy-007", loaded.shipCombat.targetEnemyId);
            Assert.AreEqual(2, loaded.shipCombat.hardpoints.Count);
            Assert.IsTrue(loaded.shipCombat.hardpoints[1].overheated);
            Assert.IsNotNull(loaded.shipCombat.productionJob);
            Assert.IsTrue(loaded.shipCombat.productionJob.active);
            Assert.AreEqual(15, loaded.shipCombat.productionJob.reservedParts);
        }

        [Test]
        public void TrainingProgressDto_StepFlagsRoundTripPerTraining()
        {
            var payload = new SavePayload { seed = 1 };
            payload.training = new TrainingProgressDto();
            payload.training.a.currentStep = 3;
            payload.training.a.stepCompleted.AddRange(new[] { true, true, true, false, false, false });
            payload.training.b.currentStep = 0;
            payload.training.c.currentStep = 4;
            payload.training.c.stepCompleted.AddRange(new[] { true, true, true, true });

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            Assert.AreEqual(3, loaded.training.a.currentStep);
            Assert.IsTrue(loaded.training.a.stepCompleted[2]);
            Assert.IsFalse(loaded.training.a.stepCompleted[3]);
            Assert.AreEqual(4, loaded.training.c.currentStep);
            Assert.AreEqual(4, loaded.training.c.stepCompleted.Count);
        }

        [Test]
        public void FirstProbeDto_RoundTripsStatusAndPendingReconEffect()
        {
            var payload = new SavePayload { seed = 1 };
            payload.firstProbe = new FirstProbeDto { status = (int)Combat.FirstProbeStatus.Resolved, warningRemainingSeconds = 0f, threatKnown = true, pendingReconEffect = "vs014.recon.pending" };

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            Assert.AreEqual((int)Combat.FirstProbeStatus.Resolved, loaded.firstProbe.status);
            Assert.IsTrue(loaded.firstProbe.threatKnown);
            Assert.AreEqual("vs014.recon.pending", loaded.firstProbe.pendingReconEffect);
        }

        [Test]
        public void EmergencyShutdownAndRecoverySoftLockDtos_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            payload.emergencyShutdown = new EmergencyShutdownDto { active = true, remainingSeconds = 4.5f };
            payload.recoverySoftLock = new RecoverySoftLockDto { totalActiveMinutesElapsed = 37f, lastVisitedControlledSystemId = "sector.0.0.0/system.002" };
            payload.recoverySoftLock.recentDefeatTimestamps.Add(10f);
            payload.recoverySoftLock.recentDefeatTimestamps.Add(25f);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            Assert.IsTrue(loaded.emergencyShutdown.active);
            Assert.AreEqual(4.5f, loaded.emergencyShutdown.remainingSeconds);
            Assert.AreEqual(37f, loaded.recoverySoftLock.totalActiveMinutesElapsed);
            Assert.AreEqual("sector.0.0.0/system.002", loaded.recoverySoftLock.lastVisitedControlledSystemId);
            Assert.AreEqual(2, loaded.recoverySoftLock.recentDefeatTimestamps.Count);
        }

        [Test]
        public void EnemyProjectileAndWreckageDtos_LiveInsideSystemDelta_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000", combatEncounterOpen = true, combatEncounterSinceLastThreat = 1.5f };
            delta.enemies.Add(new EnemyDto
            {
                enemyId = "enemy-001", defId = "enemy.fighter.mk1", px = 100f, py = 0f, pz = 200f,
                vx = 10f, vy = 0f, vz = 0f, rx = 0f, ry = 0f, rz = 0f, rw = 1f,
                aiPhase = (int)Combat.EnemyAiPhase.AttackOrScan, targetId = "player",
                scanElapsed = 0f, continuousEscapeSeconds = 0f, shield = 60f, armor = 150f, hull = 220f, regenTimer = 0f
            });
            delta.projectiles.Add(new ProjectileDto
            {
                projectileId = "proj-001", kind = (int)Combat.ProjectileKind.Em, tier = 1, ownerId = "player",
                px = 1f, py = 2f, pz = 3f, vx = 1600f, vy = 0f, vz = 0f, remainingLifetimeSeconds = 2.5f, lockedTargetId = "enemy-001"
            });
            var wreck = new CombatWreckageDto { wreckageId = "wreck-001", remainingActiveSeconds = 900f };
            wreck.nodes.Add(new CombatWreckageNodeDto { itemId = "part.basic", total = 10f, remaining = 4f });
            delta.combatWreckage.Add(wreck);
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot);

            Sectors.SystemDelta round = loaded.systemDeltas[0];
            Assert.IsTrue(round.combatEncounterOpen);
            Assert.AreEqual(1, round.enemies.Count);
            Assert.AreEqual("enemy.fighter.mk1", round.enemies[0].defId);
            Assert.AreEqual(220f, round.enemies[0].hull);
            Assert.AreEqual(1, round.projectiles.Count);
            Assert.AreEqual("enemy-001", round.projectiles[0].lockedTargetId);
            Assert.AreEqual(1, round.combatWreckage.Count);
            Assert.AreEqual(4f, round.combatWreckage[0].nodes[0].remaining);
        }

        [Test]
        public void V10ShapedPayload_LoadsWithDefaultConstructedCombatDtos_SchemaVersionIsTheRealMigrationSignal()
        {
            // Hand-written v10-shaped JSON: no "shipCombat"/"training"/"firstProbe"/
            // "emergencyShutdown"/"recoverySoftLock" keys at all -- exactly what a real
            // pre-VS-013 save looks like. JsonUtility, however, can NEVER leave a
            // nested [Serializable] class-typed field null after deserialization
            // (a verified engine quirk, unlike List<T> fields which correctly stay
            // empty when absent) -- so these fields come back non-null but
            // ALL-DEFAULT, never null. ExtractionDirector.RestoreCombat therefore
            // gates on loadedSchemaVersion (the envelope's own field, always
            // reliable), never on nullness of these nested DTOs -- this test pins
            // that exact contract down.
            string payloadJson = "{\"seed\":777,"
                + "\"moduleTiers\":[{\"moduleId\":\"defense.hull_reinforcement\",\"tier\":1}],"
                + "\"dominance\":[{\"worldId\":\"sys002/pl01\",\"state\":" + (int)DominanceState.Controlled + "}]}";
            var envelope = new SaveEnvelope
            {
                schemaVersion = 10, campaignId = "campaign-1", revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(), payloadJson = payloadJson
            };
            string json = JsonUtility.ToJson(envelope, true);
            string path = SaveService.SlotFile(TestSlot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(10, schemaVersion, "the actual saved schema must be reported, not silently upgraded");
            Assert.IsNotNull(loaded.shipCombat, "JsonUtility always default-constructs nested class fields, even when absent from JSON");
            Assert.AreEqual(0f, loaded.shipCombat.shield, "the default-constructed ShipCombatDto must never be mistaken for real saved combat data by nullness alone");
            Assert.IsNotNull(loaded.firstProbe);
            Assert.AreEqual((int)Combat.FirstProbeStatus.NotScheduled, loaded.firstProbe.status, "default enum value on the phantom DTO -- schemaVersion, not this, is what actually gates the migration branch");
            Assert.AreEqual(1, loaded.moduleTiers.Count, "pre-existing v10 state must be fully preserved");
        }

        [Test]
        public void SavingCurrentSchema_TwiceInARow_WithCombatState_IsIdempotent_NoDuplication()
        {
            var payload = new SavePayload { seed = 1 };
            payload.shipCombat = new ShipCombatDto { shield = 400f, armor = 600f, hull = 1000f, energy = 100f };
            payload.shipCombat.hardpoints.Add(new HardpointDto { index = 0, equipped = true, weaponFamily = 0, tier = 1 });
            var delta = new Sectors.SystemDelta { systemId = "sector.0.0.0/system.000" };
            delta.enemies.Add(new EnemyDto { enemyId = "enemy-001", defId = "enemy.scout_probe.mk1" });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded1 = SaveService.Load(TestSlot);
            Assert.IsTrue(SaveService.Save(TestSlot, loaded1, 2));
            SavePayload loaded2 = SaveService.Load(TestSlot);

            Assert.AreEqual(1, loaded2.shipCombat.hardpoints.Count, "a second save/load cycle must never duplicate hardpoints");
            Assert.AreEqual(1, loaded2.systemDeltas.Count);
            Assert.AreEqual(1, loaded2.systemDeltas[0].enemies.Count, "a second save/load cycle must never duplicate a saved enemy");
        }
    }
}
