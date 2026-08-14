using System;
using System.Collections.Generic;

namespace Nebula.Hangar
{
    public enum HangarProductionRejectReason
    {
        None,
        UnknownVehicle,
        LevelTooLow,
        HangarTierTooLow,
        QueueBusy,
        MaterialNotSelected,
        MissingControlledPlanet,
        InsufficientResources
    }

    /// <summary>VS-011 §5: why a Mk I-&gt;Mk II upgrade can't start right now.</summary>
    public enum HangarUpgradeRejectReason
    {
        None,
        UnknownVehicle,
        NotMkI,
        LevelTooLow,
        HangarTierTooLow,
        QueueBusy,
        VehicleNotInHangar,
        VehicleCargoNotEmpty,
        VehicleHasActiveRoute,
        InsufficientResources
    }

    /// <summary>
    /// Read-only-from-outside record of the single active Hangar production
    /// job. Only one can ever be active (VS-007 §1: production stays a
    /// single slot at every tier), so the cargo reservation owner id is a
    /// stable constant, mirroring <see cref="Modules.ModuleUpgradeJob.CargoOwnerId"/>.
    /// VS-011 §5 extends this same single slot to also run a Mk I-&gt;Mk II
    /// in-place upgrade job (<see cref="IsUpgrade"/>) — direct-build and
    /// upgrade jobs never run concurrently, so they safely share one slot
    /// and one queue-busy rule.
    /// </summary>
    public class HangarProductionJob
    {
        public const string CargoOwnerId = "hangar-production";
        /// <summary>A Mk I-&gt;Mk II upgrade job reserves under its own owner id, distinct from a direct-build job's.</summary>
        public const string UpgradeCargoOwnerId = "hangar-production-upgrade";

        public VehicleClass VehicleClass { get; }
        /// <summary>1 (default) or 2 — which VehicleCatalog def a completed direct-build job spawns. Meaningless for an upgrade job (always effectively "becomes Mk II").</summary>
        public int Mk { get; }
        public string ChosenMaterialId { get; }
        public int ReservedParts { get; }
        public int ReservedMaterial { get; }
        public float TotalSeconds { get; }

        /// <summary>True for a Mk I-&gt;Mk II in-place upgrade job (<see cref="UpgradeVehicleId"/> identifies the existing vehicle; no new vehicle is spawned on completion).</summary>
        public bool IsUpgrade { get; }
        public string UpgradeVehicleId { get; }
        /// <summary>Upgrade-only: the second reserved material (parts + metal_alloy + biocomposite — a fixed recipe, no player choice).</summary>
        public int ReservedSecondMaterial { get; }

        /// <summary>VS-015 §1: the vehicle-catalog id this job produces (direct build) or upgrades its target into (upgrade). Always populated, including for legacy Collector jobs.</summary>
        public string TargetVehicleId { get; }

        /// <summary>VS-015 §1: the full fixed multi-item recipe (parts + every material) for a generic fixed-recipe job — direct-build Mk II or any class's Mk I-&gt;Mk II upgrade. Empty for a player-chosen-material direct build.</summary>
        public IReadOnlyList<(string itemId, float amount)> FixedMaterialCosts { get; }

        public float RemainingSeconds { get; internal set; }
        public bool Cancelled { get; internal set; }
        public bool Completed { get; internal set; }

        public float Progress01 => TotalSeconds > 0f
            ? UnityEngine.Mathf.Clamp01(1f - RemainingSeconds / TotalSeconds)
            : 1f;

        /// <summary>Direct-build job (Scout, Collector Mk I, or Collector Mk II) with a single player-chosen material.</summary>
        public HangarProductionJob(VehicleClass vehicleClass, string chosenMaterialId, int reservedParts,
            int reservedMaterial, float totalSeconds, int mk = 1)
        {
            VehicleClass = vehicleClass; ChosenMaterialId = chosenMaterialId;
            ReservedParts = reservedParts; ReservedMaterial = reservedMaterial;
            TotalSeconds = totalSeconds; RemainingSeconds = totalSeconds;
            Mk = mk;
            TargetVehicleId = VehicleCatalog.Get(vehicleClass, mk)?.Id;
            FixedMaterialCosts = Array.Empty<(string, float)>();
        }

        /// <summary>Mk I-&gt;Mk II in-place upgrade job (Collector-specific shape, kept for backward compatibility). ReservedMaterial holds metal_alloy, ReservedSecondMaterial holds biocomposite.</summary>
        public HangarProductionJob(string upgradeVehicleId, int reservedParts, int reservedMetalAlloy,
            int reservedBiocomposite, float totalSeconds)
        {
            VehicleClass = VehicleClass.Collector; Mk = 2; ChosenMaterialId = null;
            ReservedParts = reservedParts; ReservedMaterial = reservedMetalAlloy; ReservedSecondMaterial = reservedBiocomposite;
            TotalSeconds = totalSeconds; RemainingSeconds = totalSeconds;
            IsUpgrade = true; UpgradeVehicleId = upgradeVehicleId;
            TargetVehicleId = VehicleCatalog.CollectorMk2Id;
            FixedMaterialCosts = new (string, float)[] { (VehicleCatalog.MetalAlloyId, reservedMetalAlloy), (VehicleCatalog.BiocompositeId, reservedBiocomposite) };
        }

        /// <summary>
        /// VS-015 §1: generic fixed-recipe job, stable vehicle-id/recipe based instead of Collector-specific.
        /// Used for a Mk II vehicle's fixed multi-item DIRECT build (isUpgrade=false, upgradeVehicleId=null) and for
        /// any class's Mk I-&gt;Mk II in-place UPGRADE (isUpgrade=true, upgradeVehicleId=the existing vehicle instance id).
        /// </summary>
        public HangarProductionJob(bool isUpgrade, VehicleClass vehicleClass, int mk, string targetVehicleId,
            string upgradeVehicleId, int reservedParts, IReadOnlyList<(string itemId, float amount)> reservedMaterials, float totalSeconds)
        {
            VehicleClass = vehicleClass; Mk = mk; ChosenMaterialId = null;
            TargetVehicleId = targetVehicleId;
            ReservedParts = reservedParts;
            FixedMaterialCosts = reservedMaterials ?? Array.Empty<(string, float)>();
            ReservedMaterial = FixedMaterialCosts.Count > 0 ? (int)FixedMaterialCosts[0].amount : 0;
            ReservedSecondMaterial = FixedMaterialCosts.Count > 1 ? (int)FixedMaterialCosts[1].amount : 0;
            TotalSeconds = totalSeconds; RemainingSeconds = totalSeconds;
            IsUpgrade = isUpgrade; UpgradeVehicleId = upgradeVehicleId;
        }
    }
}
