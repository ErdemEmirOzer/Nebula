using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// Everything the extraction context bar needs, as plain values.
    /// <para>
    /// Kept as a flat snapshot rather than a live reference so the decision of
    /// "what can the player do at this planet" is pure and testable. The old HUD
    /// interleaved that decision with string building across nine methods, which
    /// is why nobody could tell what it would print without running the game.
    /// </para>
    /// </summary>
    public struct ExtractionContextState
    {
        public bool HasTargetPlanet;
        public string PlanetDisplayName;
        public float Altitude;

        public bool Scanned;
        public float ScanProgress01;
        public string ScanBlockedReason;

        public string FamilyName;
        public string RarityName;
        public string EnvironmentTag;

        public bool KitConsumed;
        public bool HasDrill;
        public int DrillTier;
        public bool DrillActive;
        public bool DrillUpgrading;
        public bool DrillAtMaxTier;

        public bool StationConstructionGateMet;
        public int UpgradePartsCost;
        public int UpgradeMaterialCost;
        public string UpgradeMaterialName;

        public bool BuildModeEngaged;
        public bool CanTransfer;

        /// <summary>A latched transfer is running — the control says "stop", not "start".</summary>
        public bool TransferLatched;

        /// <summary>A latched scan is running, for the same reason.</summary>
        public bool ScanLatched;

        // ---- Command Center (§6) ---------------------------------------------
        // The panel is the way in; this is the signpost. Without it the HUD named
        // the drill, the stabilizer and the transfer, and said nothing at all
        // about the one structure the whole planet's economy hangs on.

        /// <summary>There is a Command verb worth a slot: a fresh T1, or a T2 upgrade.</summary>
        public bool CommandActionOffered;

        /// <summary>"KOMUTA KUR" or "KOMUTA T2" — the verb only; the key rides in its own prompt.</summary>
        public string CommandCaption;

        /// <summary>Compact price, from the same assessment the panel prices from.</summary>
        public string CommandCost;

        public bool CommandAvailable;

        /// <summary>Required whenever <see cref="CommandAvailable"/> is false.</summary>
        public string CommandBlockedReason;

        // ---- evacuation (VS-010 §6) ------------------------------------------
        // Abandoning a planet is irreversible and confirmed by pressing the key a
        // second time. ExtractionHud showed the prompt, the armed window and the
        // refusal; nothing in the redesign did, so for a while the ship could arm
        // itself for an irreversible action with nothing on screen saying so.

        /// <summary>A transaction is running: vehicles are still on their way home.</summary>
        public bool EvacuationPending;

        /// <summary>Vehicles still to return before the evacuation can finalize.</summary>
        public int EvacuationPendingVehicles;

        /// <summary>The first press has landed and the confirm window is open.</summary>
        public bool EvacuationArmed;

        /// <summary>This planet has already been abandoned, so the verb is reclaim.</summary>
        public bool PlanetAbandoned;

        /// <summary>Null when the current evacuate/reclaim is allowed.</summary>
        public string EvacuationBlockedReason;
    }

    /// <summary>
    /// Turns extraction state into one context line plus the actions that belong
    /// on the HUD.
    /// <para>
    /// This is the direct replacement for <c>ExtractionHud.BuildText</c>, which
    /// concatenated up to twelve lines -- planet, scan, drill prompt, stabilize,
    /// command, laser, dominance, node layout, quick package, evacuation, cargo,
    /// processing -- into a single overflowing <c>Text</c>. Everything that is not
    /// an action the player can take right now moves to the Planets page or a
    /// tooltip; what stays is the verb, the key and the price.
    /// </para>
    /// <para>
    /// Pure and allocation-light: the caller owns the list, so a per-frame refresh
    /// does not allocate.
    /// </para>
    /// </summary>
    public static class NebulaExtractionContext
    {
        /// <summary>
        /// Builds the context line and actions. <paramref name="secondaries"/> is
        /// cleared and refilled by the caller's own list.
        /// </summary>
        public static HudAction Build(in ExtractionContextState state, string contextLine,
            List<HudAction> secondaries, out string context)
        {
            secondaries.Clear();
            context = string.Empty;

            if (!state.HasTargetPlanet) return default;

            context = BuildContextLine(state);

            if (!state.Scanned)
            {
                // Before a scan the only thing on offer is the scan. Nothing about
                // family, rarity, yield or node layout may appear -- that is the
                // information gate, and the HUD is where it used to leak.
                bool blocked = !string.IsNullOrEmpty(state.ScanBlockedReason);
                // Carries the semantic action id, so the widget is a real control and
                // not a picture of a key. Scanning is a hold and a Button cannot be
                // held by a gamepad, so the control latches the same TickScan the key
                // feeds -- see ShipExtraction.ScanLatched.
                return new HudAction(state.ScanLatched ? "TARAMAYI DURDUR" : "TARA",
                    NebulaInputActions.Key(NebulaInputActions.ExtractionScan),
                    cost: $"%{state.ScanProgress01 * 100f:0}",
                    available: !blocked,
                    blockedReason: state.ScanBlockedReason,
                    actionId: NebulaInputActions.ExtractionScan);
            }

            HudAction primary = BuildDrillAction(state);

            // Transfer first, ahead of the defense and stabilize prompts.
            //
            // The bar shows two secondaries and this list used to be built in
            // declaration order with the transfer third, so `AKTAR` was trimmed by the
            // cap on literally every frame it applied -- the one action in the
            // moment-to-moment loop was the one action the bar could never show. It
            // also carried no action id, which made it a label rather than a control.
            // Both are fixed here; the defense verb keeps a control of its own on the
            // surface-construction card, so it loses nothing by moving down.
            if (state.CanTransfer || state.TransferLatched)
                secondaries.Add(new HudAction(
                    state.TransferLatched ? "AKTARIMI DURDUR" : "AKTAR",
                    NebulaInputActions.Key(NebulaInputActions.ExtractionTransfer),
                    actionId: NebulaInputActions.ExtractionTransfer));

            // The selected real surface-defense node. A visible button shares the
            // same semantic action id and ShipExtraction seam as the keyboard path.
            secondaries.Add(new HudAction("SAVUNMA KUR / YÜKSELT",
                NebulaInputActions.Key(NebulaInputActions.ExtractionLaser),
                actionId: NebulaInputActions.ExtractionLaser));

            secondaries.Add(new HudAction(
                state.BuildModeEngaged ? "YÖRÜNGE SABİT" : "YÖRÜNGE SABİTLE",
                NebulaInputActions.Key(NebulaInputActions.ExtractionBuildMode),
                actionId: NebulaInputActions.ExtractionBuildMode));

            // Last, on purpose. The bar shows two secondaries and the cap is the
            // point of it; the Command Center's real home is the surface-construction
            // card, so this signpost takes a slot only when one is going spare
            // rather than pushing an evacuation or a transfer off the bar.
            // Two corrections to what was here, both of the same kind as the transfer
            // row above. The prompt came from ShipExtraction.CommandKey — a constant
            // frozen at this call site — while extraction.command is a rebindable row,
            // so after the player moved Komuta this widget went on printing the key it
            // used to answer to. And it carried no action id, which is what
            // NebulaHudContextBar.Apply reads to decide whether a widget is
            // interactable: an offered, affordable Command Center therefore drew a
            // control-shaped thing that could not be clicked and, being available, had
            // no blocked reason to explain itself either. The id routes the click to
            // ShipExtraction.TryBuildOrUpgradeCommand — the same seam K reaches through
            // the router and the same one the planet panel's Komuta card calls.
            if (state.CommandActionOffered)
                secondaries.Add(new HudAction(state.CommandCaption,
                    NebulaInputActions.Key(NebulaInputActions.ExtractionCommand),
                    state.CommandCost,
                    available: state.CommandAvailable,
                    blockedReason: state.CommandBlockedReason,
                    actionId: NebulaInputActions.ExtractionCommand));

            AppendEvacuation(state, secondaries);

            // Trimmed here rather than left for the bar to drop, so what the bar is
            // given is what the bar shows and DroppedActionCount stays meaningful.
            while (secondaries.Count > NebulaHudContextBar.MaxSecondaryActions)
                secondaries.RemoveAt(secondaries.Count - 1);

            return primary;
        }

        /// <summary>
        /// Evacuation and reclaim. The armed state is the important one: the first
        /// press does nothing visible on its own, and a player who cannot see that
        /// the ship is waiting for a second press has no way to know an
        /// irreversible action is one keystroke away.
        /// </summary>
        private static void AppendEvacuation(in ExtractionContextState state,
            List<HudAction> secondaries)
        {
            // The bar shows one primary and two secondary, and that cap is the whole
            // point of replacing the text wall. So evacuation only takes a slot when
            // it is actually happening -- armed, running, or already abandoned. The
            // ordinary "you could evacuate this planet" affordance is not an urgent
            // action and lives on the Gezegenler page instead.
            if (!state.EvacuationPending && !state.EvacuationArmed && !state.PlanetAbandoned)
                return;

            if (state.EvacuationPending)
            {
                // Nothing to press while it runs; this is a status, and it says why.
                secondaries.Insert(0, new HudAction("TAHLİYE SÜRÜYOR",
                    ShipPlanetEvacuation.EvacuateKey,
                    cost: $"{state.EvacuationPendingVehicles} ARAÇ DÖNÜYOR",
                    available: false,
                    blockedReason: "ARAÇLAR DÖNENE KADAR BEKLENİYOR"));
                return;
            }

            if (state.PlanetAbandoned)
            {
                secondaries.Insert(0, new HudAction("GEZEGENİ GERİ AL",
                    ShipPlanetEvacuation.ReclaimKey,
                    available: state.EvacuationBlockedReason == null,
                    blockedReason: state.EvacuationBlockedReason));
                return;
            }

            secondaries.Insert(0, new HudAction(
                state.EvacuationArmed ? "ONAYLAMAK İÇİN TEKRAR BAS" : "GEZEGENİ TAHLİYE ET",
                ShipPlanetEvacuation.EvacuateKey,
                cost: state.EvacuationArmed ? "GERİ ALINAMAZ" : null,
                available: state.EvacuationBlockedReason == null,
                blockedReason: state.EvacuationBlockedReason));
        }

        /// <summary>
        /// The one descriptive line. Post-scan it names what the planet is; before a
        /// scan it says only where the player is and how high.
        /// </summary>
        private static string BuildContextLine(in ExtractionContextState state)
        {
            string name = string.IsNullOrEmpty(state.PlanetDisplayName)
                ? "GEZEGEN" : Design.NebulaText.Upper(state.PlanetDisplayName);

            if (!state.Scanned) return $"{name}   ·   YÜKSEKLİK {state.Altitude:0}   ·   TARANMADI";

            var line = new System.Text.StringBuilder(96);
            line.Append(name);
            if (!string.IsNullOrEmpty(state.FamilyName)) line.Append("   ·   ").Append(Design.NebulaText.Upper(state.FamilyName));
            if (!string.IsNullOrEmpty(state.RarityName)) line.Append("   ·   ").Append(Design.NebulaText.Upper(state.RarityName));
            if (!string.IsNullOrEmpty(state.EnvironmentTag)) line.Append("   ·   ").Append(Design.NebulaText.Upper(state.EnvironmentTag));
            return line.ToString();
        }

        /// <summary>
        /// The drill action: deploy the kit, build, or upgrade. Mirrors the rule
        /// <c>ExtractionHud.AppendDrillPrompt</c> encoded, but as one action with a
        /// price and a reason instead of a sentence.
        /// </summary>
        private static HudAction BuildDrillAction(in ExtractionContextState state)
        {
            if (!state.HasDrill)
            {
                // P1-DATA-011: the price is read from the drill's catalog row, not typed
                // here. This literal used to be the third independent copy of the number
                // (ShipExtraction and the Quick Asset Package held the other two), and
                // the one the player actually reads before paying.
                return state.KitConsumed
                    ? new HudAction("SONDAJ KUR", NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm),
                        $"{ExtractionCatalog.DrillT1PartsCost} PARÇA", actionId: NebulaInputActions.ExtractionConfirm)
                    : new HudAction("ÖNCÜ KİTİNİ KUR", NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm), actionId: NebulaInputActions.ExtractionConfirm);
            }

            if (state.DrillAtMaxTier)
                return new HudAction("SONDAJ T2", NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm),
                    available: false, blockedReason: "AZAMİ SEVİYEDE");

            if (state.DrillUpgrading)
                return new HudAction("SONDAJ YÜKSELTİLİYOR", NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm),
                    available: false, blockedReason: "ÜRETİM DURDU");

            if (!state.DrillActive)
                return new HudAction("SONDAJ T2'YE YÜKSELT", NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm),
                    available: false, blockedReason: "SONDAJ HENÜZ AKTİF DEĞİL");

            string cost = $"{state.UpgradePartsCost} PARÇA + {state.UpgradeMaterialCost} " +
                          Design.NebulaText.Upper(state.UpgradeMaterialName ?? string.Empty);

            return new HudAction("SONDAJ T2'YE YÜKSELT", NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm), cost,
                available: state.StationConstructionGateMet,
                blockedReason: $"İSTASYON İNŞASI T{DrillUpgradeCatalog.RequiredStationConstructionTier} GEREKLİ",
                actionId: NebulaInputActions.ExtractionConfirm);
        }

        /// <summary>
        /// Reads the live state into a snapshot. Kept separate from
        /// <see cref="Build"/> so the decision logic can be tested without a ship.
        /// </summary>
        public static ExtractionContextState Capture(ShipExtraction extraction)
        {
            var state = new ExtractionContextState();
            if (extraction == null || extraction.TargetPlanet == null) return state;

            state.HasTargetPlanet = true;
            state.PlanetDisplayName = Design.NebulaPlanetName.Of(extraction.TargetPlanet.WorldId);
            state.Altitude = extraction.Altitude(extraction.TargetPlanet);

            PlanetProfile profile = extraction.TargetProfile;
            state.Scanned = profile != null && profile.Scanned;
            state.ScanProgress01 = extraction.ScanProgress;
            state.ScanBlockedReason = extraction.ScanBlockedReason;

            if (state.Scanned)
            {
                state.FamilyName = ExtractionCatalog.ResourceNames[(int)profile.Family];
                state.RarityName = ExtractionCatalog.RarityNames[(int)profile.Rarity];
                state.EnvironmentTag = Sectors.EnvironmentClassCatalog.ShortTag(profile.Environment);
                state.UpgradeMaterialName = ProcessingCatalog.MaterialNames[(int)profile.Family];
            }

            state.KitConsumed = ExtractionState.Kit == ExtractionState.KitState.Consumed;
            state.BuildModeEngaged = extraction.BuildMode;
            state.TransferLatched = extraction.TransferLatched;
            state.ScanLatched = extraction.ScanLatched;

            DrillStructure drill = null;
            foreach (DrillStructure candidate in ExtractionState.Drills)
            {
                if (candidate != null && candidate.WorldId == extraction.TargetPlanet.WorldId)
                {
                    drill = candidate;
                    break;
                }
            }

            if (drill != null)
            {
                state.HasDrill = true;
                state.DrillTier = drill.Runtime.Tier;
                state.DrillActive = drill.Runtime.State == DrillLifecycle.Active;
                state.DrillUpgrading = drill.ActiveUpgradeJob != null;
                state.DrillAtMaxTier = drill.Runtime.Tier >= DrillUpgradeCatalog.MaxTier;
                state.CanTransfer = state.DrillActive && drill.Runtime.StoredAmount > 0f;
            }

            state.StationConstructionGateMet =
                extraction.StationConstructionTierForHud >= DrillUpgradeCatalog.RequiredStationConstructionTier;
            state.UpgradePartsCost = DrillUpgradeCatalog.PartsCost;
            state.UpgradeMaterialCost = DrillUpgradeCatalog.MaterialCost;

            CaptureCommand(extraction, ref state);
            return state;
        }

        /// <summary>
        /// The Command signpost, read from the same assessment the panel reads —
        /// never a second opinion about cost or availability. The assessment is
        /// pure, so asking here costs nothing and reserves nothing.
        /// </summary>
        private static void CaptureCommand(ShipExtraction extraction, ref ExtractionContextState state)
        {
            CommandBuildAssessment assessment = extraction.AssessCommandAction();

            // Only a verb the player can press earns a slot. "Building" and "max
            // tier" are states, and the card says them properly.
            bool build = assessment.Action == CommandActionKind.BuildT1;
            bool upgrade = assessment.Action == CommandActionKind.UpgradeT2;
            if (!build && !upgrade) return;

            state.CommandActionOffered = true;
            state.CommandCaption = build ? "KOMUTA KUR" : "KOMUTA T2";
            state.CommandCost = CommandCostSummary(assessment);
            state.CommandAvailable = assessment.CanAct;
            state.CommandBlockedReason = CommandBuildText.BlockedReason(assessment);
        }

        /// <summary>
        /// The price in one line. Built from the assessment's own cost lines, so a
        /// withheld pre-scan material simply is not there rather than being named
        /// with a question mark.
        /// </summary>
        private static string CommandCostSummary(CommandBuildAssessment assessment)
        {
            var summary = new System.Text.StringBuilder(48);
            IReadOnlyList<CommandCostLine> cost = assessment.Cost;
            for (int i = 0; i < cost.Count; i++)
            {
                if (summary.Length > 0) summary.Append(" + ");
                summary.Append($"{cost[i].Required:0} ")
                    .Append(Design.NebulaText.Upper(ProcessingCatalog.ItemDisplayName(cost[i].ItemId)));
            }
            return summary.ToString();
        }
    }
}
