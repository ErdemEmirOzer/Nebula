using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the VS-013.1 §6 additive save v11 repair/damage-gate fields — schema stays 11, generation stays 2, and an old vs-013-auto-pass-shaped v11 save must load with safe defaults (never a JsonUtility-nullness assumption).</summary>
    public class Vs0131SaveTests
    {
        private const int TestSlot = 94;

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
        public void SchemaAndGeneration_UnchangedByVs0131()
        {
            // VS-013.1 itself never bumped this (still true) -- VS-014 has since legitimately
            // consumed the reserved v12 for planetary defense tiers/raids/Pirate Base.
            Assert.AreEqual(15, SaveService.CurrentSchema, "VS-013.1 never bumped the schema itself; later slices legitimately did");
        }

        [Test]
        public void RepairFields_RoundTripExactly()
        {
            var payload = new SavePayload { seed = 1 };
            payload.shipCombat = new ShipCombatDto
            {
                shield = 100f, armor = 200f, hull = 300f, energy = 50f, damaged = true,
                repairActive = true, repairPaused = true, repairHullForm = 2, repairPackageProgress01 = 0.42f,
                sinceLastDamageSeconds = 3.5f
            };

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.AreEqual(SaveService.CurrentSchema, schemaVersion);
            Assert.IsTrue(loaded.shipCombat.repairActive);
            Assert.IsTrue(loaded.shipCombat.repairPaused);
            Assert.AreEqual(2, loaded.shipCombat.repairHullForm);
            Assert.AreEqual(0.42f, loaded.shipCombat.repairPackageProgress01, 0.001f);
            Assert.AreEqual(3.5f, loaded.shipCombat.sinceLastDamageSeconds, 0.001f);
            Assert.IsTrue(loaded.shipCombat.damaged);
        }

        [Test]
        public void OldVs013AutoPassShapedV11Save_LoadsRepairFieldsWithSafeDefaults()
        {
            // Hand-written v11 JSON in the EXACT shape the original vs-013-auto-pass tag would
            // have written -- shipCombat present, but none of the VS-013.1 repair/damage-gate
            // keys exist at all (they weren't invented yet). Schema stays 11 in both cases, so
            // this must NOT be distinguished by schemaVersion -- only by the fields' own safe
            // JsonUtility defaults (repairActive/Paused/progress -> false/false/0, exactly "no
            // job in flight"; sinceLastDamageSeconds -> its 999f field-initializer default,
            // exactly "not recently damaged", never a misleading 0).
            string payloadJson = "{\"seed\":777,"
                + "\"shipCombat\":{\"shield\":150.0,\"armor\":250.0,\"hull\":500.0,\"regenTimer\":0.0,"
                + "\"energy\":80.0,\"weaponsLocked\":false,\"boostRemaining\":0.0,\"boostCooldownRemaining\":0.0,"
                + "\"damaged\":true,\"hardpoints\":[],\"targetEnemyId\":null,\"emLockTargetId\":null,\"emLockElapsed\":0.0}}";
            var envelope = new SaveEnvelope
            {
                schemaVersion = 11, campaignId = "campaign-1", revision = 1,
                payloadHash = ExtractionCatalog.Hash(payloadJson).ToString(), payloadJson = payloadJson
            };
            string json = JsonUtility.ToJson(envelope, true);
            string path = SaveService.SlotFile(TestSlot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);

            SavePayload loaded = SaveService.Load(TestSlot, out int schemaVersion);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(11, schemaVersion);
            Assert.IsNotNull(loaded.shipCombat, "the old shipCombat DTO itself was genuinely present and populated");
            Assert.AreEqual(150f, loaded.shipCombat.shield, 0.001f, "pre-existing v11 fields must round-trip exactly");
            Assert.IsTrue(loaded.shipCombat.damaged);
            Assert.IsFalse(loaded.shipCombat.repairActive, "never-written key -> safe default false, never treated as a phantom active job");
            Assert.IsFalse(loaded.shipCombat.repairPaused);
            Assert.AreEqual(0f, loaded.shipCombat.repairPackageProgress01);
            Assert.AreEqual(999f, loaded.shipCombat.sinceLastDamageSeconds, 0.001f, "never-written key -> the 999f safe default, never a misleading 0 that would wrongly block repair for 8s after load");
        }

        [Test]
        public void SavingCurrentSchema_TwiceInARow_WithRepairState_IsIdempotent_NoDuplication()
        {
            var payload = new SavePayload { seed = 1 };
            payload.shipCombat = new ShipCombatDto { repairActive = true, repairHullForm = 1, repairPackageProgress01 = 0.2f, sinceLastDamageSeconds = 5f };

            Assert.IsTrue(SaveService.Save(TestSlot, payload, 1));
            SavePayload loaded1 = SaveService.Load(TestSlot);
            Assert.IsTrue(SaveService.Save(TestSlot, loaded1, 2));
            SavePayload loaded2 = SaveService.Load(TestSlot);

            Assert.IsTrue(loaded2.shipCombat.repairActive);
            Assert.AreEqual(0.2f, loaded2.shipCombat.repairPackageProgress01, 0.001f);
        }
    }
}
