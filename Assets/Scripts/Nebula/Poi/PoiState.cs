using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Poi
{
    /// <summary>One deterministic salvage node inside a Wreck — a fixed item+amount slice of the Wreck's total reward.</summary>
    public class SalvageNode
    {
        public string ItemId;
        public float Total;
        public float Remaining;
        public SalvageNode(string itemId, float total) { ItemId = itemId; Total = total; Remaining = total; }
    }

    /// <summary>
    /// Pure, mutable per-POI runtime state (VS-012 §1 "mutable delta"): the
    /// information-gate level, locate/identify/operation timers (pause, never
    /// reset, exactly like <see cref="Hangar.ScoutScanWork"/>/
    /// <see cref="Navigation.SignalAnalysisState"/>), Wreck salvage nodes, and
    /// the Abandoned Facility's exclusive irreversible choice. One class for
    /// all three types (unused fields for a given <see cref="PoiManifest.Type"/>
    /// simply stay at their defaults) — matches this project's existing
    /// flat-DTO-shape convention rather than a polymorphic hierarchy.
    /// </summary>
    public class PoiState
    {
        public string PoiId { get; }

        /// <summary>
        /// Where this POI sits on the information ladder. Never
        /// <see cref="PoiKnowledgeLevel.Unknown"/>: the existence of this object is
        /// itself the record whose absence <c>Unknown</c> denotes, so the first rung a
        /// live state can occupy is <see cref="PoiKnowledgeLevel.Signal"/>
        /// (P1-WORLD-005 — see the enum's own comment for the decision and why it is
        /// not a gap). <see cref="RestoreCore"/> enforces this for saved data too,
        /// so a hand-edited or legacy file cannot introduce a state the rest of the
        /// game has no path out of.
        /// </summary>
        public PoiKnowledgeLevel Level { get; private set; } = PoiKnowledgeLevel.Signal;
        public float LocateElapsed { get; private set; }
        public float IdentifyElapsed { get; private set; }
        public float OperationElapsed { get; private set; }
        public PoiPauseReason PauseReason { get; private set; } = PoiPauseReason.None;
        /// <summary>Null = the main ship is (or was last) the worker; otherwise a Scout/Collector vehicle id.</summary>
        public string AssignedVehicleId { get; private set; }

        // --- Anomaly ---------------------------------------------------------------
        public bool AnomalyXpAwarded { get; private set; }

        // --- Wreck -------------------------------------------------------------------
        public List<SalvageNode> SalvageNodes { get; } = new List<SalvageNode>();
        public bool WreckXpAwarded { get; private set; }
        public bool Depleted => SalvageNodes.Count > 0 && AllNodesEmpty();
        private bool AllNodesEmpty() { foreach (SalvageNode n in SalvageNodes) if (n.Remaining > 0.0001f) return false; return true; }

        // --- Abandoned Facility --------------------------------------------------------
        public FacilityChoice Choice { get; private set; } = FacilityChoice.None;
        public float FacilityOutputBasicRemaining { get; private set; }
        public float FacilityOutputSpecialRemaining { get; private set; }
        public string FacilityOutputSpecialItemId { get; private set; }
        public bool FacilityXpAwarded { get; private set; }
        public bool FacilitySignalApplied { get; private set; }

        public PoiState(string poiId) { PoiId = poiId; }

        // --- Locate / Identify (VS-012 §3) -------------------------------------------
        /// <summary>Advances only while conditionMet; never resets on loss. Returns true the tick that crosses the threshold.</summary>
        public bool TickLocate(float dt, bool conditionMet, float requiredSeconds)
        {
            if (Level != PoiKnowledgeLevel.Signal) return false;
            if (!conditionMet) { PauseReason = PoiPauseReason.OutOfRange; return false; }
            PauseReason = PoiPauseReason.None;
            LocateElapsed += dt;
            if (LocateElapsed < requiredSeconds) return false;
            LocateElapsed = requiredSeconds;
            Level = PoiKnowledgeLevel.Located;
            return true;
        }

        public bool TickIdentify(float dt, bool conditionMet)
        {
            if (Level != PoiKnowledgeLevel.Located) return false;
            if (!conditionMet) { PauseReason = PoiPauseReason.OutOfRange; return false; }
            PauseReason = PoiPauseReason.None;
            IdentifyElapsed += dt;
            if (IdentifyElapsed < PoiCatalog.IdentifySeconds) return false;
            IdentifyElapsed = PoiCatalog.IdentifySeconds;
            Level = PoiKnowledgeLevel.Identified;
            return true;
        }

        // --- Active operation (Anomaly work / Wreck collection is instantaneous-transfer, not timed / Facility operation) ---
        public bool StartOperation(string vehicleId)
        {
            if (Level != PoiKnowledgeLevel.Identified) return false;
            Level = PoiKnowledgeLevel.Active;
            AssignedVehicleId = vehicleId;
            OperationElapsed = 0f;
            PauseReason = PoiPauseReason.None;
            return true;
        }

        /// <summary>Advances the Active operation timer; pauses (never resets) when conditionMet is false. Returns true the tick that crosses requiredSeconds.</summary>
        public bool TickOperation(float dt, bool conditionMet, float requiredSeconds, PoiPauseReason reasonIfPaused)
        {
            if (Level != PoiKnowledgeLevel.Active) return false;
            if (!conditionMet) { PauseReason = reasonIfPaused; return false; }
            PauseReason = PoiPauseReason.None;
            OperationElapsed += dt;
            if (OperationElapsed < requiredSeconds) return false;
            OperationElapsed = requiredSeconds;
            return true;
        }

        public void Resolve() { Level = PoiKnowledgeLevel.Resolved; AssignedVehicleId = null; PauseReason = PoiPauseReason.None; }

        public void CancelOperationBackToIdentified()
        {
            if (Level != PoiKnowledgeLevel.Active) return;
            Level = PoiKnowledgeLevel.Identified;
            OperationElapsed = 0f;
            AssignedVehicleId = null;
            PauseReason = PoiPauseReason.None;
        }

        public void AssignVehicle(string vehicleId) => AssignedVehicleId = vehicleId;

        // --- Type-specific one-shot flags ------------------------------------------
        public void MarkAnomalyXpAwarded() => AnomalyXpAwarded = true;
        public void MarkWreckXpAwarded() => WreckXpAwarded = true;
        public void MarkFacilityXpAwarded() => FacilityXpAwarded = true;
        public void MarkFacilitySignalApplied() => FacilitySignalApplied = true;

        public void SetSalvageNodes(IEnumerable<SalvageNode> nodes) { SalvageNodes.Clear(); foreach (SalvageNode n in nodes) SalvageNodes.Add(n); }

        public void LockFacilityChoice(FacilityChoice choice) { if (Choice == FacilityChoice.None) Choice = choice; }

        public void SetFacilityDismantleOutput(float basicRemaining, float specialRemaining, string specialItemId)
        {
            FacilityOutputBasicRemaining = basicRemaining;
            FacilityOutputSpecialRemaining = specialRemaining;
            FacilityOutputSpecialItemId = specialItemId;
        }

        /// <summary>Withdraws up to the requested amounts from the Dismantle output store, bounded by what's left; never creates or deletes resources.</summary>
        public (float basic, float special) WithdrawFacilityOutput(float wantBasic, float wantSpecial)
        {
            float basic = Mathf.Min(wantBasic, FacilityOutputBasicRemaining);
            float special = Mathf.Min(wantSpecial, FacilityOutputSpecialRemaining);
            FacilityOutputBasicRemaining -= basic;
            FacilityOutputSpecialRemaining -= special;
            return (basic, special);
        }

        // --- Save/load restore ---------------------------------------------------------
        public void RestoreCore(PoiKnowledgeLevel level, float locateElapsed, float identifyElapsed, float operationElapsed,
            PoiPauseReason pauseReason, string assignedVehicleId)
        {
            // A saved Unknown would mean "we have no record of this POI" written into a
            // record of that POI. It cannot be produced by any live path, and a state
            // that arrived that way would sit below Signal with no tick able to advance
            // it — a POI frozen out of its own ladder. It reads as the first real rung.
            Level = level == PoiKnowledgeLevel.Unknown ? PoiKnowledgeLevel.Signal : level;
            LocateElapsed = locateElapsed; IdentifyElapsed = identifyElapsed; OperationElapsed = operationElapsed;
            PauseReason = pauseReason; AssignedVehicleId = assignedVehicleId;
        }

        public void RestoreAnomaly(bool xpAwarded) => AnomalyXpAwarded = xpAwarded;
        public void RestoreWreck(bool xpAwarded) => WreckXpAwarded = xpAwarded;

        public void RestoreFacility(FacilityChoice choice, float outputBasic, float outputSpecial, string outputSpecialItemId,
            bool xpAwarded, bool signalApplied)
        {
            Choice = choice;
            FacilityOutputBasicRemaining = outputBasic; FacilityOutputSpecialRemaining = outputSpecial; FacilityOutputSpecialItemId = outputSpecialItemId;
            FacilityXpAwarded = xpAwarded; FacilitySignalApplied = signalApplied;
        }
    }
}
