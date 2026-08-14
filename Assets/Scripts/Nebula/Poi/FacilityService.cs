using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;

namespace Nebula.Poi
{
    public enum FacilityStartRejectReason { None, NotIdentified, ChoiceAlreadyLocked, InsufficientCargo }

    /// <summary>Pure Abandoned Facility logic (VS-012 §7): two mutually exclusive, irreversible outcomes.</summary>
    public static class FacilityService
    {
        public static float DismantleSeconds(PoiRisk risk) => risk == PoiRisk.Unstable ? PoiCatalog.FacilityDismantleUnstableSeconds : PoiCatalog.FacilityDismantleBaseSeconds;
        public static float RelaySeconds(PoiRisk risk) => risk == PoiRisk.Unstable ? PoiCatalog.FacilityRelayUnstableSeconds : PoiCatalog.FacilityRelayBaseSeconds;

        public static List<(string itemId, float amount)> RelayCost() => new List<(string, float)>
        {
            (ProcessingCatalog.BasicPartId, PoiCatalog.FacilityRelayPartsCost),
            (PoiCatalog.FacilityRelayDiamondCompositeId, PoiCatalog.FacilityRelayDiamondCompositeCost),
            (PoiCatalog.FacilityRelayBiocompositeId, PoiCatalog.FacilityRelayBiocompositeCost)
        };

        public static FacilityStartRejectReason CheckStartDismantle(PoiState state)
        {
            if (state.Level != PoiKnowledgeLevel.Identified) return FacilityStartRejectReason.NotIdentified;
            if (state.Choice != FacilityChoice.None) return FacilityStartRejectReason.ChoiceAlreadyLocked;
            return FacilityStartRejectReason.None;
        }

        public static FacilityStartRejectReason CheckStartRelay(PoiState state, LogisticsCargo shipCargo)
        {
            if (state.Level != PoiKnowledgeLevel.Identified) return FacilityStartRejectReason.NotIdentified;
            if (state.Choice != FacilityChoice.None) return FacilityStartRejectReason.ChoiceAlreadyLocked;
            if (shipCargo == null) return FacilityStartRejectReason.InsufficientCargo;
            foreach (var (itemId, amount) in RelayCost()) if (shipCargo.GetAvailable(itemId) < amount - 0.0001f) return FacilityStartRejectReason.InsufficientCargo;
            return FacilityStartRejectReason.None;
        }

        public const string RelayCargoOwnerPrefix = "poi_facility_relay:";
        public static string RelayOwnerId(string poiId) => RelayCargoOwnerPrefix + poiId;

        /// <summary>Locks the Dismantle choice and enters Active. Cost is only ever the ship's TIME, not cargo.</summary>
        public static bool StartDismantle(PoiState state, string vehicleId)
        {
            if (CheckStartDismantle(state) != FacilityStartRejectReason.None) return false;
            state.LockFacilityChoice(FacilityChoice.Dismantle);
            state.StartOperation(vehicleId);
            return true;
        }

        /// <summary>Atomically reserves the Relay's fixed cost from the ship's cargo, locks the choice, and enters Active.</summary>
        public static bool StartRelay(PoiState state, string poiId, LogisticsCargo shipCargo, string vehicleId)
        {
            if (CheckStartRelay(state, shipCargo) != FacilityStartRejectReason.None) return false;
            if (!shipCargo.TryReserveAll(RelayOwnerId(poiId), RelayCost())) return false;
            state.LockFacilityChoice(FacilityChoice.SensorRelay);
            state.StartOperation(vehicleId);
            return true;
        }

        /// <summary>Dismantle completion: deterministic reward from the POI's own reward seed, stored as recoverable output (never lost if the ship's cargo can't take it all right away). 300 XP once.</summary>
        public static void CompleteDismantle(PoiState state, PoiManifest manifest)
        {
            if (state.Choice != FacilityChoice.Dismantle) return;
            int basic = RangeRoll(manifest.RewardSeed, "facility/basic", PoiCatalog.FacilityDismantleMinBasic, PoiCatalog.FacilityDismantleMaxBasic);
            int special = RangeRoll(manifest.RewardSeed, "facility/special", PoiCatalog.FacilityDismantleMinSpecial, PoiCatalog.FacilityDismantleMaxSpecial);
            string materialId = ProcessingCatalog.MaterialIds[Mathf.FloorToInt(Unit(manifest.RewardSeed, "facility/material") * ProcessingCatalog.MaterialIds.Length) % ProcessingCatalog.MaterialIds.Length];
            state.SetFacilityDismantleOutput(basic, special, materialId);
            AwardXpOnce(state, manifest.PoiId);
            state.Resolve();
        }

        /// <summary>Relay completion: permanently consumes the already-reserved cost, opens up to 2 one-shot signals from the nearest-available still-Unknown candidates (0/1 available -&gt; that many, never fake/duplicate), 300 XP once.</summary>
        public static void CompleteRelay(PoiState state, PoiManifest manifest, LogisticsCargo shipCargo,
            List<(string systemId, float distance)> sortedUnknownCandidates, SystemKnowledgeService knowledge, FrontierSignalService frontierSignal)
        {
            if (state.Choice != FacilityChoice.SensorRelay) return;
            shipCargo?.ConsumeReservation(RelayOwnerId(manifest.PoiId));

            if (!state.FacilitySignalApplied)
            {
                frontierSignal?.TryOpenOneShot(manifest.PoiId, sortedUnknownCandidates, knowledge, PoiCatalog.FacilityRelaySignalCount);
                state.MarkFacilitySignalApplied();
            }
            AwardXpOnce(state, manifest.PoiId);
            state.Resolve();
        }

        /// <summary>Bounded physical transfer of Dismantle output into the ship's cargo — capacity-limited, never overflows/loses the remainder.</summary>
        public static float TransferDismantleOutput(PoiState state, LogisticsCargo shipCargo, float ratePerSecond, float dt)
        {
            if (shipCargo == null || dt <= 0f) return 0f;
            float budget = ratePerSecond * dt;
            float delivered = 0f;

            if (state.FacilityOutputBasicRemaining > 0f)
            {
                float want = Mathf.Min(budget, state.FacilityOutputBasicRemaining);
                float added = shipCargo.Add(ProcessingCatalog.BasicPartId, want);
                var (basicTaken, _) = state.WithdrawFacilityOutput(added, 0f);
                delivered += basicTaken; budget -= added;
            }
            if (budget > 0.0001f && state.FacilityOutputSpecialRemaining > 0f && state.FacilityOutputSpecialItemId != null)
            {
                float want = Mathf.Min(budget, state.FacilityOutputSpecialRemaining);
                float added = shipCargo.Add(state.FacilityOutputSpecialItemId, want);
                var (_, specialTaken) = state.WithdrawFacilityOutput(0f, added);
                delivered += specialTaken;
            }
            return delivered;
        }

        private static void AwardXpOnce(PoiState state, string poiId)
        {
            if (state.FacilityXpAwarded) return;
            Progression.ProgressionService.Award("poi.facility.xp." + poiId, PoiCatalog.FacilityXp);
            state.MarkFacilityXpAwarded();
        }

        private static int RangeRoll(ulong seed, string tag, int min, int max) =>
            min + Mathf.FloorToInt(Unit(seed, tag) * (max - min + 1) * 0.999999f);

        private static float Unit(ulong seed, string tag) => ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, tag));
    }
}
