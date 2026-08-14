using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Hangar
{
    /// <summary>
    /// Single-slot Hangar production queue (VS-007 §1-2), independent of the
    /// module-upgrade queue — both tick only on active simulation time
    /// (Time.timeScale = 0 while any modal is open naturally zeroes dt, the
    /// same mechanism every other pure Tick(dt) system in this project
    /// relies on). Start is atomic: validate against a single snapshot, then
    /// reserve the full parts+material cost under one stable owner id, or
    /// touch nothing at all. Completion is idempotent and creates exactly one
    /// vehicle with a stable id; cancel returns floor(75%) of both resources.
    /// VS-011 §5 extends the same single slot with a Mk I-&gt;Mk II in-place
    /// upgrade job — direct-build and upgrade share the slot and the
    /// QueueBusy rule, never run concurrently.
    /// </summary>
    public class HangarProductionService
    {
        private readonly LogisticsCargo cargo;
        private int nextVehicleSequence;

        public HangarProductionJob ActiveJob { get; private set; }

        public event Action<HangarProductionJob> JobStarted;
        /// <summary>Fired exactly once per DIRECT-BUILD job, with the newly minted vehicle id, after resources are already consumed. Never fires for an upgrade job — see <see cref="JobUpgradeCompleted"/>.</summary>
        public event Action<HangarProductionJob, string> JobCompleted;
        /// <summary>Fired exactly once per UPGRADE job, after resources are already consumed — the caller mutates the existing vehicle's Def in place.</summary>
        public event Action<HangarProductionJob> JobUpgradeCompleted;
        public event Action<HangarProductionJob> JobCancelled;

        public HangarProductionService(LogisticsCargo shipCargo, int startingVehicleSequence = 0)
        {
            cargo = shipCargo;
            nextVehicleSequence = Mathf.Max(0, startingVehicleSequence);
        }

        public HangarProductionRejectReason TryStartProduction(VehicleClass vehicleClass, string chosenMaterialId,
            int characterLevel, int hangarTier, int mk = 1, bool hasAnyControlledPlanet = true)
        {
            VehicleDef def = VehicleCatalog.Get(vehicleClass, mk);
            if (def == null) return HangarProductionRejectReason.UnknownVehicle;
            if (ActiveJob != null) return HangarProductionRejectReason.QueueBusy;
            if (characterLevel < def.UnlockLevel) return HangarProductionRejectReason.LevelTooLow;
            if (hangarTier < def.RequiredHangarTier) return HangarProductionRejectReason.HangarTierTooLow;
            if (def.RequiresControlledPlanet && !hasAnyControlledPlanet) return HangarProductionRejectReason.MissingControlledPlanet;
            if (string.IsNullOrEmpty(chosenMaterialId) || Array.IndexOf(VehicleCatalog.SpecialMaterialIds, chosenMaterialId) < 0)
                return HangarProductionRejectReason.MaterialNotSelected;

            var costs = new List<(string itemId, float amount)>
            {
                (ProcessingCatalog.BasicPartId, def.BasicPartCost),
                (chosenMaterialId, def.SpecialMaterialCost)
            };
            if (cargo == null || !cargo.TryReserveAll(HangarProductionJob.CargoOwnerId, costs))
                return HangarProductionRejectReason.InsufficientResources;

            ActiveJob = new HangarProductionJob(vehicleClass, chosenMaterialId, def.BasicPartCost, def.SpecialMaterialCost, def.BuildSeconds, mk);
            JobStarted?.Invoke(ActiveJob);
            return HangarProductionRejectReason.None;
        }

        /// <summary>VS-015 §1: starts a Mk II vehicle's fixed multi-item DIRECT build (Engineer Mk II / Escort Mk II — no player-chosen material).</summary>
        public HangarProductionRejectReason TryStartFixedProduction(string vehicleId, int characterLevel, int hangarTier)
        {
            if (!VehicleCatalog.TryGetFixedDirectBuildRecipe(vehicleId, out VehicleUpgradeRecipe recipe))
                return HangarProductionRejectReason.UnknownVehicle;
            VehicleDef def = VehicleCatalog.Get(vehicleId);
            if (def == null) return HangarProductionRejectReason.UnknownVehicle;
            if (ActiveJob != null) return HangarProductionRejectReason.QueueBusy;
            if (characterLevel < def.UnlockLevel) return HangarProductionRejectReason.LevelTooLow;
            if (hangarTier < def.RequiredHangarTier) return HangarProductionRejectReason.HangarTierTooLow;

            if (cargo == null || !cargo.TryReserveAll(HangarProductionJob.CargoOwnerId, recipe.FullCost()))
                return HangarProductionRejectReason.InsufficientResources;

            ActiveJob = new HangarProductionJob(false, def.Class, def.Mk, def.Id, null, recipe.PartsCost, recipe.MaterialCosts, def.BuildSeconds);
            JobStarted?.Invoke(ActiveJob);
            return HangarProductionRejectReason.None;
        }

        /// <summary>VS-011 §5: starts an in-place Mk I-&gt;Mk II upgrade for the Collector. Eligibility (hangar-parked/empty-cargo/no-mission/no-route) is checked by the caller (ShipHangar), which knows about VehicleAgent/CollectorRoute state this pure service doesn't.</summary>
        public HangarUpgradeRejectReason TryStartUpgrade(string vehicleId, bool vehicleExistsAndIsMkI, int characterLevel, int hangarTier) =>
            StartUpgradeInternal(vehicleId, VehicleCatalog.CollectorId, vehicleExistsAndIsMkI, characterLevel, hangarTier);

        /// <summary>VS-015 §1: generic, stable vehicle-id/recipe-based Mk I-&gt;Mk II upgrade — used by Engineer and Escort (and reusable by any future class), generalizing beyond the Collector-only code path above.</summary>
        public HangarUpgradeRejectReason TryStartUpgrade(string vehicleId, string fromVehicleDefId, bool vehicleExistsAndIsSourceMk, int characterLevel, int hangarTier) =>
            StartUpgradeInternal(vehicleId, fromVehicleDefId, vehicleExistsAndIsSourceMk, characterLevel, hangarTier);

        private HangarUpgradeRejectReason StartUpgradeInternal(string vehicleId, string fromVehicleDefId, bool vehicleExistsAndIsSourceMk, int characterLevel, int hangarTier)
        {
            if (!vehicleExistsAndIsSourceMk) return HangarUpgradeRejectReason.NotMkI;
            if (!VehicleCatalog.TryGetUpgradeRecipe(fromVehicleDefId, out VehicleUpgradeRecipe recipe)) return HangarUpgradeRejectReason.UnknownVehicle;
            VehicleDef toDef = VehicleCatalog.Get(recipe.ToVehicleId);
            if (toDef == null) return HangarUpgradeRejectReason.UnknownVehicle;
            if (ActiveJob != null) return HangarUpgradeRejectReason.QueueBusy;
            if (characterLevel < toDef.UnlockLevel) return HangarUpgradeRejectReason.LevelTooLow;
            if (hangarTier < toDef.RequiredHangarTier) return HangarUpgradeRejectReason.HangarTierTooLow;

            if (cargo == null || !cargo.TryReserveAll(HangarProductionJob.UpgradeCargoOwnerId, recipe.FullCost()))
                return HangarUpgradeRejectReason.InsufficientResources;

            ActiveJob = new HangarProductionJob(true, toDef.Class, toDef.Mk, toDef.Id, vehicleId, recipe.PartsCost, recipe.MaterialCosts, recipe.Seconds);
            JobStarted?.Invoke(ActiveJob);
            return HangarUpgradeRejectReason.None;
        }

        /// <summary>Advances the active job by dt (active-simulation time only). Returns the newly built vehicle id for a direct build, or null (including for a completed upgrade — see JobUpgradeCompleted).</summary>
        public string Tick(float dt)
        {
            if (ActiveJob == null || ActiveJob.Cancelled || ActiveJob.Completed) return null;
            ActiveJob.RemainingSeconds -= dt;
            if (ActiveJob.RemainingSeconds > 0f) return null;
            return CompleteActiveJob();
        }

        private string CompleteActiveJob()
        {
            HangarProductionJob job = ActiveJob;
            if (job == null || job.Completed || job.Cancelled) return null; // idempotency guard

            job.Completed = true;
            job.RemainingSeconds = 0f;

            if (job.IsUpgrade)
            {
                cargo?.ConsumeReservation(HangarProductionJob.UpgradeCargoOwnerId);
                ActiveJob = null;
                JobUpgradeCompleted?.Invoke(job);
                return null;
            }

            cargo?.ConsumeReservation(HangarProductionJob.CargoOwnerId);
            string vehicleId = $"vehicle-{nextVehicleSequence:0000}";
            nextVehicleSequence++;
            ActiveJob = null;
            JobCompleted?.Invoke(job, vehicleId);
            return vehicleId;
        }

        /// <summary>Cancels the active job: floor(75%) refund of both/all reserved resources. No-op if already cancelled/completed.</summary>
        public bool CancelActiveJob()
        {
            HangarProductionJob job = ActiveJob;
            if (job == null || job.Cancelled || job.Completed) return false;

            job.Cancelled = true;
            string owner = job.IsUpgrade ? HangarProductionJob.UpgradeCargoOwnerId : HangarProductionJob.CargoOwnerId;
            cargo?.ConsumeReservationForRefund(owner, 0.75f);
            ActiveJob = null;
            JobCancelled?.Invoke(job);
            return true;
        }

        /// <summary>Rebuilds the active DIRECT-BUILD job from a save snapshot without re-validating; re-derives the reservation from the already-restored cargo.</summary>
        public bool RestoreActiveJob(VehicleClass vehicleClass, string chosenMaterialId, int reservedParts,
            int reservedMaterial, float totalSeconds, float remainingSeconds, int mk = 1)
        {
            var job = new HangarProductionJob(vehicleClass, chosenMaterialId, reservedParts, reservedMaterial, totalSeconds, mk)
            {
                RemainingSeconds = Mathf.Max(0f, remainingSeconds)
            };
            if (reservedParts > 0 || reservedMaterial > 0)
            {
                var costs = new List<(string, float)> { (ProcessingCatalog.BasicPartId, reservedParts) };
                if (!string.IsNullOrEmpty(chosenMaterialId) && reservedMaterial > 0) costs.Add((chosenMaterialId, reservedMaterial));
                if (cargo == null || !cargo.TrySetReservation(HangarProductionJob.CargoOwnerId, costs))
                { ActiveJob = null; return false; }
            }
            ActiveJob = job;
            return true;
        }

        /// <summary>Rebuilds the active UPGRADE job from a save snapshot without re-validating.</summary>
        public bool RestoreActiveUpgradeJob(string upgradeVehicleId, int reservedParts, int reservedMetalAlloy,
            int reservedBiocomposite, float totalSeconds, float remainingSeconds)
        {
            var job = new HangarProductionJob(upgradeVehicleId, reservedParts, reservedMetalAlloy, reservedBiocomposite, totalSeconds)
            {
                RemainingSeconds = Mathf.Max(0f, remainingSeconds)
            };
            var costs = new List<(string, float)>
            {
                (ProcessingCatalog.BasicPartId, reservedParts),
                (VehicleCatalog.MetalAlloyId, reservedMetalAlloy),
                (VehicleCatalog.BiocompositeId, reservedBiocomposite)
            };
            if (cargo == null || !cargo.TrySetReservation(HangarProductionJob.UpgradeCargoOwnerId, costs))
            { ActiveJob = null; return false; }
            ActiveJob = job;
            return true;
        }

        /// <summary>VS-015 §1: rebuilds a generic fixed-recipe job (Mk II direct build or any class's Mk I-&gt;Mk II upgrade) from a save snapshot without re-validating; re-derives the reservation from the already-restored cargo using the exact item ids the DTO carried.</summary>
        public bool RestoreGenericFixedJob(bool isUpgrade, VehicleClass vehicleClass, int mk, string targetVehicleId,
            string upgradeVehicleId, int reservedParts, IReadOnlyList<(string itemId, float amount)> reservedMaterials,
            float totalSeconds, float remainingSeconds)
        {
            var job = new HangarProductionJob(isUpgrade, vehicleClass, mk, targetVehicleId, upgradeVehicleId, reservedParts, reservedMaterials, totalSeconds)
            {
                RemainingSeconds = Mathf.Max(0f, remainingSeconds)
            };
            var costs = new List<(string, float)> { (ProcessingCatalog.BasicPartId, reservedParts) };
            if (reservedMaterials != null) costs.AddRange(reservedMaterials);
            string owner = isUpgrade ? HangarProductionJob.UpgradeCargoOwnerId : HangarProductionJob.CargoOwnerId;
            if (cargo == null || !cargo.TrySetReservation(owner, costs))
            { ActiveJob = null; return false; }
            ActiveJob = job;
            return true;
        }

        public int NextVehicleSequenceForSave => nextVehicleSequence;

        /// <summary>Bumps the sequence counter so restored vehicle ids never collide with a fresh production run (used only if the save didn't carry an explicit counter, e.g. a hand-edited file).</summary>
        public void EnsureSequenceAtLeast(int minimum) => nextVehicleSequence = Mathf.Max(nextVehicleSequence, minimum);
    }
}
