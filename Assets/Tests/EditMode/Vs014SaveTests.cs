using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §17/§18: save v12 DTO round-trips (RaidCampaignDto, StructureDto tier/upgrade, EnemyDto/ProjectileDto extensions, ThreatPressureDto/DefenseCoordinationDto inside SystemDelta) and v11-&gt;v12 migration safe defaults.</summary>
    public class Vs014SaveTests
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
        }

        [Test]
        public void RaidCampaignDto_RoundTripsExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var dto = new RaidCampaignDto
            {
                difficulty = 2, activeMajorRaidSystemId = "system.a",
                pirateBaseSpawned = true, pirateBasePoiId = "poi.pirate_base", pirateBaseSystemId = "system.b",
                pirateBaseResolved = true, pirateBaseRewardAwarded = false
            };
            dto.taughtEnemyDefIds.Add(EnemyIds.HeavyAssaultMk1);
            dto.history.Add(new RaidHistoryEntryDto { stage = 2, doctrine = 2, leaderDefId = EnemyIds.SiegeMk1, planetWorldId = "planet.x" });
            payload.raidCampaign = dto;

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.IsNotNull(loaded.raidCampaign);
            Assert.AreEqual(2, loaded.raidCampaign.difficulty);
            Assert.AreEqual("system.a", loaded.raidCampaign.activeMajorRaidSystemId);
            Assert.IsTrue(loaded.raidCampaign.pirateBaseSpawned);
            Assert.AreEqual("poi.pirate_base", loaded.raidCampaign.pirateBasePoiId);
            Assert.AreEqual(1, loaded.raidCampaign.taughtEnemyDefIds.Count);
            Assert.AreEqual(1, loaded.raidCampaign.history.Count);
            Assert.AreEqual("planet.x", loaded.raidCampaign.history[0].planetWorldId);
        }

        [Test]
        public void StructureDto_TierAndUpgradeJob_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new SystemDelta { systemId = "system.a" };
            delta.structures.Add(new StructureDto
            {
                worldId = "planet.a", nodeId = "node.command", type = (int)SurfaceStructureType.Command, state = 3,
                shield = 100f, armor = 200f, hull = 300f, tier = 2, upgrading = true, upgradeTargetTier = 2, upgradeProgress01 = 0.42f
            });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);

            StructureDto dto = loaded.systemDeltas[0].structures[0];
            Assert.AreEqual(2, dto.tier);
            Assert.IsTrue(dto.upgrading);
            Assert.AreEqual(2, dto.upgradeTargetTier);
            Assert.AreEqual(0.42f, dto.upgradeProgress01, 0.001f);
        }

        [Test]
        public void StructureDto_PreV12MigratedDefault_TierIsOne_NotUpgrading()
        {
            var dto = new StructureDto(); // never touched, as if deserialized from a pre-v12 payload missing these keys
            Assert.AreEqual(1, dto.tier);
            Assert.IsFalse(dto.upgrading);
        }

        [Test]
        public void EnemyDto_LooterCargoDisruptTelegraph_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new SystemDelta { systemId = "system.a" };
            var enemyDto = new EnemyDto
            {
                enemyId = "system.a/enemy.0001", defId = EnemyIds.LooterTransportMk1,
                disruptRemaining = 1.5f, disruptImmunityRemaining = 6f, telegraphRemaining = 0.8f,
                structureTargetId = "planet.a/defense.0"
            };
            enemyDto.looterCargo.Add(new CargoItemDto { itemId = ProcessingCatalog.BasicPartId, amount = 40f });
            delta.enemies.Add(enemyDto);
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);

            EnemyDto dto = loaded.systemDeltas[0].enemies[0];
            Assert.AreEqual(1.5f, dto.disruptRemaining, 0.001f);
            Assert.AreEqual(6f, dto.disruptImmunityRemaining, 0.001f);
            Assert.AreEqual(0.8f, dto.telegraphRemaining, 0.001f);
            Assert.AreEqual(1, dto.looterCargo.Count);
            Assert.AreEqual(40f, dto.looterCargo[0].amount, 0.001f);
        }

        [Test]
        public void ProjectileDto_HostileFactionAndInterceptionFields_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new SystemDelta { systemId = "system.a" };
            delta.projectiles.Add(new ProjectileDto
            {
                projectileId = "system.a/projectile.0001", kind = (int)ProjectileKind.EnemySiege,
                faction = (int)ProjectileFaction.Hostile, interceptable = true, interceptionDurabilityRemaining = 22f, damageAmount = 360f
            });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);

            ProjectileDto dto = loaded.systemDeltas[0].projectiles[0];
            Assert.AreEqual((int)ProjectileFaction.Hostile, dto.faction);
            Assert.IsTrue(dto.interceptable);
            Assert.AreEqual(22f, dto.interceptionDurabilityRemaining, 0.001f);
            Assert.AreEqual(360f, dto.damageAmount, 0.001f);
        }

        [Test]
        public void ThreatPressureDto_RoundTripsExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new SystemDelta { systemId = "system.a" };
            var pressureDto = new ThreatPressureDto
            {
                pressure = 42f, graceRemainingActiveMinutes = 3f, phase = 4, stage = 0, doctrine = 0,
                targetPlanetWorldId = "planet.a", encounterSeed = "999", warningRemainingSeconds = 12f,
                resultApplied = true, salvageApplied = true
            };
            pressureDto.compositionDefIds.Add(EnemyIds.FighterMk1);
            pressureDto.spawnedEnemyIds.Add("system.a/enemy.0001");
            delta.threatPressure = pressureDto;
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);

            ThreatPressureDto dto = loaded.systemDeltas[0].threatPressure;
            Assert.IsNotNull(dto);
            Assert.AreEqual(42f, dto.pressure, 0.001f);
            Assert.AreEqual("999", dto.encounterSeed);
            Assert.IsTrue(dto.resultApplied);
            Assert.AreEqual(1, dto.compositionDefIds.Count);
            Assert.AreEqual(1, dto.spawnedEnemyIds.Count);
        }

        [Test]
        public void DefenseCoordinationDto_RoundTripsExactly()
        {
            var payload = new SavePayload { seed = 1 };
            var delta = new SystemDelta { systemId = "system.a" };
            delta.defenseCoordination.Add(new DefenseCoordinationDto { worldId = "planet.a", mode = 2, focusEnemyId = "enemy.0001" });
            payload.systemDeltas.Add(delta);

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);

            DefenseCoordinationDto dto = loaded.systemDeltas[0].defenseCoordination[0];
            Assert.AreEqual("planet.a", dto.worldId);
            Assert.AreEqual(2, dto.mode);
            Assert.AreEqual("enemy.0001", dto.focusEnemyId);
        }

        [Test]
        public void RoundTrippedPayload_WithNoRaidCampaignEverSet_StillDeserializesANonNullDefaultDto()
        {
            // JsonUtility's own well-known behavior (already relied on throughout this
            // codebase, see ExtractionDirector.RestoreCombat's own doc comment): a
            // nested [Serializable] reference field is never left null by deserialization,
            // even when the ORIGINAL payload never touched it. This is exactly why the
            // real restore logic must gate on loadedSchemaVersion, never on nullness --
            // this test documents/pins that exact hazard for raidCampaign specifically.
            var payload = new SavePayload { seed = 1 }; // raidCampaign left untouched (null in C#, pre-serialization)
            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out _);
            Assert.IsNotNull(loaded.raidCampaign, "JsonUtility default-constructs nested Serializable fields on deserialize");
        }
    }
}
