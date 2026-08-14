using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Progression;
using Nebula.UI.Session;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-02: the main menu's read of the save slots. Scoped to that read only --
    /// nothing here starts a session or touches gameplay.
    /// </summary>
    public class NebulaSaveSlotQueryTests
    {
        private readonly List<string> written = new List<string>();

        [TearDown]
        public void RemoveWrittenSlots()
        {
            foreach (string path in written)
                if (File.Exists(path)) File.Delete(path);
            written.Clear();
        }

        // ---- helpers ---------------------------------------------------------

        private void WriteRaw(int slot, string contents)
        {
            string path = SaveService.SlotFile(slot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents);
            written.Add(path);
        }

        private void WriteSlot(int slot, Action<SaveEnvelope, SavePayload> configure)
        {
            var payload = new SavePayload { ship = new ShipDto(), navigation = new NavigationDto() };
            var envelope = new SaveEnvelope
            {
                schemaVersion = SaveService.CurrentSchema,
                campaignId = "campaign-test",
                revision = 1,
                savedAtUtc = "2026-08-04T10:00:00Z",
            };
            configure?.Invoke(envelope, payload);
            envelope.payloadJson = JsonUtility.ToJson(payload);
            WriteRaw(slot, JsonUtility.ToJson(envelope));
        }

        private void ClearAllSlots()
        {
            for (int slot = NebulaSaveSlotQuery.FirstSlot;
                 slot < NebulaSaveSlotQuery.FirstSlot + NebulaSaveSlotQuery.SlotCount; slot++)
            {
                string path = SaveService.SlotFile(slot);
                if (File.Exists(path)) { File.Delete(path); }
            }
        }

        // ---- empty / present -------------------------------------------------

        [Test]
        public void MissingSlot_ReportsEmpty_AndCannotContinue()
        {
            ClearAllSlots();
            SaveSlotSummary summary = NebulaSaveSlotQuery.Read(1);

            Assert.IsFalse(summary.Exists);
            Assert.IsFalse(summary.CanContinue);
            Assert.AreEqual(SaveSlotBlockReason.Empty, summary.BlockReason);
            Assert.IsNotNull(NebulaSaveSlotQuery.BlockReasonText(summary),
                "An unusable slot must always be able to say why -- never a dead control.");
        }

        [Test]
        public void EveryQuery_ReturnsOneSummaryPerSlot_EvenWhenAllEmpty()
        {
            ClearAllSlots();
            List<SaveSlotSummary> all = NebulaSaveSlotQuery.All();
            Assert.AreEqual(NebulaSaveSlotQuery.SlotCount, all.Count);
            for (int i = 0; i < all.Count; i++)
                Assert.AreEqual(NebulaSaveSlotQuery.FirstSlot + i, all[i].Slot,
                    "Slots must come back in menu order so the list never reshuffles under the cursor.");
        }

        [Test]
        public void PlayableSlot_IsSummarisedWithoutLoadingTheCampaign()
        {
            ClearAllSlots();
            WriteSlot(1, (envelope, payload) =>
            {
                envelope.campaignId = "campaign-kepler";
                envelope.revision = 7;
                payload.totalXp = ProgressionMath.TotalXpToReach(12);
                payload.ship.systemStar = "VEGA-7";
                payload.vehicles = new List<VehicleDto> { new VehicleDto(), new VehicleDto() };
                payload.dominance = new List<DominanceDto>
                {
                    new DominanceDto { worldId = "a", state = (int)DominanceState.Controlled },
                    new DominanceDto { worldId = "b", state = (int)DominanceState.Developing },
                    new DominanceDto { worldId = "c", state = (int)DominanceState.Lost },
                };
            });

            SaveSlotSummary summary = NebulaSaveSlotQuery.Read(1);

            Assert.IsTrue(summary.CanContinue);
            Assert.AreEqual("campaign-kepler", summary.CampaignId);
            Assert.AreEqual(7, summary.Revision);
            Assert.AreEqual(12, summary.Level);
            Assert.AreEqual("VEGA-7", summary.LocationName);
            Assert.AreEqual(2, summary.FleetVehicles);
            Assert.AreEqual(2, summary.HeldPlanets, "Lost dominance must not count as held.");
            Assert.IsNull(NebulaSaveSlotQuery.BlockReasonText(summary));
        }

        [Test]
        public void KnownSystems_CountsOnlyVisited_NotSignalOrLocated()
        {
            // The main menu must not advertise knowledge the information gate has
            // not granted. A Signal is not a system the player has been to.
            ClearAllSlots();
            WriteSlot(1, (_, payload) =>
            {
                payload.navigation.systemKnowledge = new List<SystemKnowledgeDto>
                {
                    new SystemKnowledgeDto { systemId = "s1", level = (int)SystemKnowledgeLevel.Signal },
                    new SystemKnowledgeDto { systemId = "s2", level = (int)SystemKnowledgeLevel.Located },
                    new SystemKnowledgeDto { systemId = "s3", level = (int)SystemKnowledgeLevel.Visited },
                    new SystemKnowledgeDto { systemId = "s4", level = (int)SystemKnowledgeLevel.Visited },
                };
            });

            Assert.AreEqual(2, NebulaSaveSlotQuery.Read(1).KnownSystems,
                "Signal/Located leaked into the visited count, which would show the player " +
                "systems they have not actually reached.");
        }

        // ---- refusal paths ---------------------------------------------------

        [Test]
        public void NewerSchema_IsRefusedWithItsOwnReason()
        {
            ClearAllSlots();
            WriteSlot(1, (envelope, _) => envelope.schemaVersion = SaveService.CurrentSchema + 1);

            SaveSlotSummary summary = NebulaSaveSlotQuery.Read(1);

            Assert.IsTrue(summary.Exists);
            Assert.IsFalse(summary.CanContinue);
            Assert.AreEqual(SaveSlotBlockReason.SchemaTooNew, summary.BlockReason);
            StringAssert.Contains((SaveService.CurrentSchema + 1).ToString(),
                NebulaSaveSlotQuery.BlockReasonText(summary),
                "The refusal should name the version it found, not just say no.");
        }

        [Test]
        public void CorruptSlot_IsReportedNotThrown()
        {
            ClearAllSlots();
            WriteRaw(1, "{ this is not json");

            SaveSlotSummary summary = null;
            Assert.DoesNotThrow(() => summary = NebulaSaveSlotQuery.Read(1),
                "A corrupt slot must never take the main menu down.");
            Assert.AreEqual(SaveSlotBlockReason.Unreadable, summary.BlockReason);
            Assert.IsFalse(summary.CanContinue);
        }

        [Test]
        public void EmptyFile_IsReportedAsUnreadable()
        {
            ClearAllSlots();
            WriteRaw(1, "   ");
            Assert.AreEqual(SaveSlotBlockReason.Unreadable, NebulaSaveSlotQuery.Read(1).BlockReason);
        }

        [Test]
        public void OneCorruptSlot_DoesNotHideTheHealthyOnes()
        {
            ClearAllSlots();
            WriteRaw(1, "{ broken");
            WriteSlot(2, (envelope, _) => envelope.campaignId = "campaign-good");

            List<SaveSlotSummary> all = NebulaSaveSlotQuery.All();

            Assert.AreEqual(SaveSlotBlockReason.Unreadable, all[0].BlockReason);
            Assert.IsTrue(all[1].CanContinue, "A broken slot 1 must not block reaching slot 2.");
        }

        // ---- Continue selection ---------------------------------------------

        [Test]
        public void Continue_IsOfferedOnlyWhenSomethingIsPlayable()
        {
            ClearAllSlots();
            Assert.IsFalse(NebulaSaveSlotQuery.HasAnythingToContinue());
            Assert.IsNull(NebulaSaveSlotQuery.MostRecent());

            WriteSlot(2, null);
            Assert.IsTrue(NebulaSaveSlotQuery.HasAnythingToContinue());
            Assert.AreEqual(2, NebulaSaveSlotQuery.MostRecent().Slot);
        }

        [Test]
        public void Continue_PicksTheMostRecentlySavedSlot()
        {
            ClearAllSlots();
            WriteSlot(1, (envelope, _) => envelope.savedAtUtc = "2026-08-01T10:00:00Z");
            WriteSlot(2, (envelope, _) => envelope.savedAtUtc = "2026-08-04T18:30:00Z");
            WriteSlot(3, (envelope, _) => envelope.savedAtUtc = "2026-08-03T09:00:00Z");

            Assert.AreEqual(2, NebulaSaveSlotQuery.MostRecent().Slot);
        }

        [Test]
        public void Continue_NeverPrefersAnUnreadableSlotOverAPlayableOne()
        {
            ClearAllSlots();
            WriteRaw(1, "{ broken");
            WriteSlot(3, (envelope, _) => envelope.savedAtUtc = "2026-08-02T09:00:00Z");

            SaveSlotSummary chosen = NebulaSaveSlotQuery.MostRecent();
            Assert.IsNotNull(chosen);
            Assert.AreEqual(3, chosen.Slot);
        }

        [Test]
        public void Continue_FallsBackToRevision_WhenNoSlotRecordedATimestamp()
        {
            ClearAllSlots();
            WriteSlot(1, (envelope, _) => { envelope.savedAtUtc = null; envelope.revision = 3; });
            WriteSlot(2, (envelope, _) => { envelope.savedAtUtc = null; envelope.revision = 9; });

            Assert.AreEqual(2, NebulaSaveSlotQuery.MostRecent().Slot,
                "With no timestamps, the higher revision is the later save.");
        }

        [Test]
        public void Continue_PrefersARealTimestampOverAMissingOne()
        {
            ClearAllSlots();
            WriteSlot(1, (envelope, _) => { envelope.savedAtUtc = null; envelope.revision = 99; });
            WriteSlot(2, (envelope, _) => { envelope.savedAtUtc = "2026-08-04T12:00:00Z"; envelope.revision = 1; });

            Assert.AreEqual(2, NebulaSaveSlotQuery.MostRecent().Slot,
                "A slot with an unknown save time must not win on revision alone.");
        }

        // ---- side-effect freedom --------------------------------------------

        [Test]
        public void ReadingSlots_DoesNotTouchCampaignIdentity()
        {
            // Describing a campaign is not the same act as entering it. If the menu
            // adopted the identity while merely listing, "New Campaign" would
            // inherit the previous campaign's id.
            ClearAllSlots();
            WriteSlot(1, (envelope, _) => envelope.campaignId = "campaign-should-not-be-adopted");

            string before = CampaignIdentity.Current;
            NebulaSaveSlotQuery.All();
            Assert.AreEqual(before, CampaignIdentity.Current,
                "Enumerating save slots mutated the live campaign identity.");
        }

        [Test]
        public void ReadingSlots_DoesNotWriteAnything()
        {
            ClearAllSlots();
            WriteSlot(1, null);
            string path = SaveService.SlotFile(1);
            DateTime before = File.GetLastWriteTimeUtc(path);

            NebulaSaveSlotQuery.All();
            NebulaSaveSlotQuery.MostRecent();

            Assert.AreEqual(before, File.GetLastWriteTimeUtc(path),
                "The main menu rewrote a save file just by listing it.");
        }
    }
}
