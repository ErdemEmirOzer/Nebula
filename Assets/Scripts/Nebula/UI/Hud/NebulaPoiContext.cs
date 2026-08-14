using System.Collections.Generic;
using Nebula.Poi;
using Nebula.Ship;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// Turns the ship's current point of interest into the same context line plus
    /// actions the extraction path produces, so both share one bar and one widget
    /// contract.
    /// <para>
    /// It exists because POI was the last physical interaction with no interface at
    /// all. <c>ShipPoi.TryStartPrimaryAction</c> and
    /// <c>ShipPoi.TryStartSensorRelay</c> have been public and correct since VS-012,
    /// and the only way to reach either was to be standing in the right place with a
    /// key held — nothing on screen named the POI, the verb, the choice or the fact
    /// that the Sensor Relay lock is irreversible. A grep for <c>ShipPoi</c> under
    /// <c>Assets/Scripts/Nebula/UI</c> returned the fleet page's dispatch and nothing
    /// else.
    /// </para>
    /// <para>
    /// Pure and allocation-light in the same shape as
    /// <see cref="NebulaExtractionContext"/>: the caller owns the list, this decides
    /// what goes in it, and the driver decides when to ask. Every action carries a
    /// semantic action id, so the widget it becomes is a real button whose click ends
    /// up in exactly the method the key ends up in.
    /// </para>
    /// </summary>
    public static class NebulaPoiContext
    {
        /// <summary>
        /// The primary action and any secondaries for the ship's current POI.
        /// <paramref name="secondaries"/> is refilled by the caller's own list.
        /// </summary>
        public static HudAction Build(ShipPoi poi, List<HudAction> secondaries, out string context)
        {
            secondaries.Clear();
            context = string.Empty;

            PoiObject target = poi != null ? poi.TargetPoi : null;
            if (target == null || target.Manifest == null || target.State == null) return default;

            context = BuildContextLine(target);

            // Before it is identified there is nothing to commit to, and saying what
            // the thing is would leak the identification the player has not earned.
            // The knowledge gate belongs to PoiState; this only declines to print.
            if (target.State.Level != PoiKnowledgeLevel.Identified
                && target.State.Level != PoiKnowledgeLevel.Active)
                return default;

            if (target.State.Level == PoiKnowledgeLevel.Active)
            {
                // A running operation offers exactly one thing: abandoning it. The
                // cancel seam refuses a completed, irreversible Facility choice on its
                // own, so this never has to know that rule.
                return new HudAction("OPERASYONU İPTAL ET",
                    NebulaInputActions.Key(NebulaInputActions.PoiCancel),
                    actionId: NebulaInputActions.PoiCancel);
            }

            string primaryBlocked = poi.PrimaryActionBlockedReason();
            var primary = new HudAction(poi.PrimaryActionCaption(),
                NebulaInputActions.Key(NebulaInputActions.PoiStart),
                cost: OperationCost(target),
                available: primaryBlocked == null,
                blockedReason: primaryBlocked,
                actionId: NebulaInputActions.PoiStart);

            // The relay is the Facility's other, mutually exclusive choice, so it only
            // earns a slot where that choice exists at all.
            if (target.Manifest.Type == PoiType.AbandonedFacility)
            {
                string relayBlocked = poi.SensorRelayBlockedReason();
                secondaries.Add(new HudAction("SENSÖR RÖLESİ KUR",
                    NebulaInputActions.Key(NebulaInputActions.PoiSensorRelay),
                    cost: "GERİ DÖNÜŞÜMSÜZ",
                    available: relayBlocked == null,
                    blockedReason: relayBlocked,
                    actionId: NebulaInputActions.PoiSensorRelay));
            }

            return primary;
        }

        /// <summary>
        /// The one descriptive line: what the ship is standing at, and how far.
        /// Pre-identification it names the knowledge level and nothing else, which is
        /// the same rule the strategic map's object card follows.
        /// </summary>
        private static string BuildContextLine(PoiObject target)
        {
            string level = LevelName(target.State.Level);
            if (target.State.Level == PoiKnowledgeLevel.Signal
                || target.State.Level == PoiKnowledgeLevel.Located)
                return "İLGİ NOKTASI   ·   " + level;

            // Assembled rather than interpolated in one literal, exactly as
            // NebulaExtractionContext does it: a single format string holding three
            // separated fields is the shape NebulaUiHygieneTests reads as a pinned
            // multi-field hint, and the rule is worth more than the one line it costs.
            var line = new System.Text.StringBuilder(64);
            line.Append(TypeName(target.Manifest.Type));
            line.Append("   ·   ").Append(RiskName(target.Manifest.Risk));
            line.Append("   ·   ").Append(level);
            return line.ToString();
        }

        /// <summary>The commitment's price, where it has one. The relay's cargo cost is stated on its own row.</summary>
        private static string OperationCost(PoiObject target) =>
            target.Manifest.Type == PoiType.AbandonedFacility ? "GERİ DÖNÜŞÜMSÜZ" : null;

        private static string TypeName(PoiType type) => type switch
        {
            PoiType.Anomaly => "ANOMALİ",
            PoiType.AbandonedFacility => "TERK EDİLMİŞ TESİS",
            PoiType.Wreck => "ENKAZ",
            _ => "İLGİ NOKTASI"
        };

        private static string RiskName(PoiRisk risk) => risk switch
        {
            PoiRisk.Safe => "GÜVENLİ",
            PoiRisk.Unstable => "KARARSIZ",
            _ => "BİLİNMİYOR"
        };

        private static string LevelName(PoiKnowledgeLevel level) => level switch
        {
            PoiKnowledgeLevel.Signal => "SİNYAL",
            PoiKnowledgeLevel.Located => "KONUM BELİRLENDİ",
            PoiKnowledgeLevel.Identified => "TANIMLANDI",
            PoiKnowledgeLevel.Active => "ÇALIŞMA SÜRÜYOR",
            PoiKnowledgeLevel.Resolved => "TAMAMLANDI",
            _ => "BİLİNMİYOR"
        };
    }
}
