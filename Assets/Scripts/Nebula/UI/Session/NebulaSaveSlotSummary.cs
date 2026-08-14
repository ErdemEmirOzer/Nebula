using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Progression;

namespace Nebula.UI.Session
{
    /// <summary>
    /// Why a slot cannot be started. The main menu shows the reason rather than a
    /// dead button -- an unexplained disabled control is the thing this redesign
    /// exists to remove.
    /// </summary>
    public enum SaveSlotBlockReason
    {
        /// <summary>Loadable.</summary>
        None,

        /// <summary>Nothing saved here yet.</summary>
        Empty,

        /// <summary>Written by a newer build than this one. Loading it would corrupt it.</summary>
        SchemaTooNew,

        /// <summary>Present but unreadable -- truncated, wrong JSON, or hand-edited.</summary>
        Unreadable,
    }

    /// <summary>
    /// Everything the main menu needs to draw one save slot, without loading the
    /// campaign it describes.
    /// <para>
    /// Reading the envelope alone is the point: a full <see cref="SaveService.Load"/>
    /// deserialises the whole world and would make the menu's cost scale with the
    /// size of the campaign it is only trying to label.
    /// </para>
    /// </summary>
    public sealed class SaveSlotSummary
    {
        public int Slot { get; }
        public bool Exists { get; }
        public SaveSlotBlockReason BlockReason { get; }

        /// <summary>Stable campaign identity. Shown as an identity, never as a raw id.</summary>
        public string CampaignId { get; }

        public int SchemaVersion { get; }
        public int Revision { get; }

        /// <summary>UTC timestamp the save recorded, or <c>null</c> when it did not record one.</summary>
        public DateTime? SavedAtUtc { get; }

        public int Level { get; }
        public long TotalXp { get; }

        /// <summary>Systems the player has actually visited. Signal/Located are excluded.</summary>
        public int KnownSystems { get; }

        /// <summary>Planets under a dominance state that counts as held.</summary>
        public int HeldPlanets { get; }

        public int FleetVehicles { get; }

        /// <summary>Display name of the system the ship was in, or <c>null</c> when unknown.</summary>
        public string LocationName { get; }

        /// <summary>True when this slot can be started.</summary>
        public bool CanContinue => Exists && BlockReason == SaveSlotBlockReason.None;

        private SaveSlotSummary(int slot, bool exists, SaveSlotBlockReason block, string campaignId,
            int schema, int revision, DateTime? savedAt, int level, long totalXp,
            int knownSystems, int heldPlanets, int fleet, string location)
        {
            Slot = slot;
            Exists = exists;
            BlockReason = block;
            CampaignId = campaignId;
            SchemaVersion = schema;
            Revision = revision;
            SavedAtUtc = savedAt;
            Level = level;
            TotalXp = totalXp;
            KnownSystems = knownSystems;
            HeldPlanets = heldPlanets;
            FleetVehicles = fleet;
            LocationName = location;
        }

        internal static SaveSlotSummary Empty(int slot) =>
            new SaveSlotSummary(slot, false, SaveSlotBlockReason.Empty, null, 0, 0, null, 0, 0, 0, 0, 0, null);

        internal static SaveSlotSummary Broken(int slot, SaveSlotBlockReason reason, int schema) =>
            new SaveSlotSummary(slot, true, reason, null, schema, 0, null, 0, 0, 0, 0, 0, null);

        internal static SaveSlotSummary Playable(int slot, SaveEnvelope envelope, SavePayload payload)
        {
            int level = ProgressionMath.LevelForTotalXp(payload.totalXp);

            // Only systems the player has actually reached. Counting Signal and
            // Located entries too would let the menu advertise knowledge the
            // information gate has not granted yet.
            int known = 0;
            if (payload.navigation != null && payload.navigation.systemKnowledge != null)
            {
                for (int i = 0; i < payload.navigation.systemKnowledge.Count; i++)
                {
                    SystemKnowledgeDto entry = payload.navigation.systemKnowledge[i];
                    if (entry != null && entry.level >= (int)Navigation.SystemKnowledgeLevel.Visited) known++;
                }
            }

            int held = 0;
            for (int i = 0; i < payload.dominance.Count; i++)
            {
                DominanceDto dto = payload.dominance[i];
                if (dto == null) continue;
                var state = (DominanceState)dto.state;
                if (state == DominanceState.Controlled || state == DominanceState.Developing) held++;
            }

            string location = payload.ship != null && !string.IsNullOrEmpty(payload.ship.systemStar)
                ? payload.ship.systemStar
                : null;

            return new SaveSlotSummary(slot, true, SaveSlotBlockReason.None, envelope.campaignId,
                envelope.schemaVersion, envelope.revision, ParseUtc(envelope.savedAtUtc),
                level, payload.totalXp, known, held, payload.vehicles?.Count ?? 0, location);
        }

        private static DateTime? ParseUtc(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime parsed)
                ? parsed
                : (DateTime?)null;
        }
    }

    /// <summary>
    /// Reads save slots for the main menu.
    /// <para>
    /// Deliberately read-only and side-effect free: enumerating slots must never
    /// touch <see cref="CampaignIdentity"/>, start a session, or write anything.
    /// The menu describing a campaign is not the same act as entering it, and
    /// keeping that separation is what stops the main menu from producing
    /// gameplay state.
    /// </para>
    /// </summary>
    public static class NebulaSaveSlotQuery
    {
        /// <summary>
        /// Slots the interface offers — forwarded from <see cref="SaveService"/>
        /// rather than restated, so the range the screen draws and the range the
        /// save system honours cannot drift apart again.
        /// </summary>
        public const int SlotCount = SaveService.SlotCount;

        public const int FirstSlot = SaveService.FirstSlot;

        /// <summary>Every slot, in menu order, whether or not it holds a save.</summary>
        public static List<SaveSlotSummary> All()
        {
            var slots = new List<SaveSlotSummary>(SlotCount);
            for (int slot = FirstSlot; slot < FirstSlot + SlotCount; slot++)
                slots.Add(Read(slot));
            return slots;
        }

        /// <summary>
        /// The slot "Continue" should resume: the most recently saved playable
        /// slot, or <c>null</c> when there is nothing to continue.
        /// </summary>
        public static SaveSlotSummary MostRecent()
        {
            SaveSlotSummary best = null;
            foreach (SaveSlotSummary slot in All())
            {
                if (!slot.CanContinue) continue;
                if (best == null) { best = slot; continue; }

                // A missing timestamp must never beat a real one, so fall back to
                // revision rather than treating "unknown" as "oldest" or "newest".
                if (slot.SavedAtUtc.HasValue && best.SavedAtUtc.HasValue)
                {
                    if (slot.SavedAtUtc.Value > best.SavedAtUtc.Value) best = slot;
                }
                else if (slot.SavedAtUtc.HasValue && !best.SavedAtUtc.HasValue) best = slot;
                else if (!slot.SavedAtUtc.HasValue && !best.SavedAtUtc.HasValue && slot.Revision > best.Revision) best = slot;
            }
            return best;
        }

        /// <summary>True when the main menu should offer Continue at all.</summary>
        public static bool HasAnythingToContinue() => MostRecent() != null;

        /// <summary>
        /// One slot, described without loading the campaign. Any failure is
        /// reported as a reason on the summary rather than thrown -- a corrupt
        /// slot must still let the player reach the other two.
        /// </summary>
        public static SaveSlotSummary Read(int slot)
        {
            string path;
            try { path = SaveService.SlotFile(slot); }
            catch { return SaveSlotSummary.Broken(slot, SaveSlotBlockReason.Unreadable, 0); }

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return SaveSlotSummary.Empty(slot);

            SaveEnvelope envelope;
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return SaveSlotSummary.Broken(slot, SaveSlotBlockReason.Unreadable, 0);
                envelope = JsonUtility.FromJson<SaveEnvelope>(json);
            }
            catch { return SaveSlotSummary.Broken(slot, SaveSlotBlockReason.Unreadable, 0); }

            if (envelope == null) return SaveSlotSummary.Broken(slot, SaveSlotBlockReason.Unreadable, 0);

            // Same rule SaveService.Load enforces. Checked here too so the menu can
            // explain the refusal instead of offering a slot that would be rejected.
            if (envelope.schemaVersion > SaveService.CurrentSchema)
                return SaveSlotSummary.Broken(slot, SaveSlotBlockReason.SchemaTooNew, envelope.schemaVersion);

            SavePayload payload;
            try { payload = JsonUtility.FromJson<SavePayload>(envelope.payloadJson); }
            catch { return SaveSlotSummary.Broken(slot, SaveSlotBlockReason.Unreadable, envelope.schemaVersion); }

            if (payload == null) return SaveSlotSummary.Broken(slot, SaveSlotBlockReason.Unreadable, envelope.schemaVersion);

            return SaveSlotSummary.Playable(slot, envelope, payload);
        }

        /// <summary>
        /// Player-facing reason a slot cannot be started. Returns <c>null</c> when
        /// it can -- callers use that to decide whether a tooltip is needed at all.
        /// </summary>
        public static string BlockReasonText(SaveSlotSummary summary)
        {
            if (summary == null) return "KAYIT OKUNAMADI";
            switch (summary.BlockReason)
            {
                case SaveSlotBlockReason.None: return null;
                case SaveSlotBlockReason.Empty: return "BU YUVA BOŞ";
                case SaveSlotBlockReason.SchemaTooNew:
                    return $"DAHA YENİ BİR SÜRÜMLE KAYDEDİLMİŞ (v{summary.SchemaVersion})";
                default: return "KAYIT DOSYASI OKUNAMADI";
            }
        }
    }
}
