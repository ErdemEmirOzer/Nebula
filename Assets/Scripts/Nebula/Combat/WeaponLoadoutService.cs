using System.Collections.Generic;
using Nebula.Extraction;

namespace Nebula.Combat
{
    public enum WeaponProductionRejectReason { None, QueueBusy, TargetTierInvalid, FamilyLocked, InsufficientResources, CombatActive, NoHardpointAvailable }

    /// <summary>Why a hardpoint could not be freed for a refit. Mirrors <see cref="WeaponProductionRejectReason"/>'s shape so one UI switch can present both.</summary>
    public enum HardpointReleaseRejectReason { None, CombatActive, QueueBusy, NoSuchHardpoint, AlreadyEmpty }

    /// <summary>Single active ship-weapon production/upgrade job — mirrors Hangar.HangarProductionJob's shape (VS-013 §2/§3).</summary>
    public class WeaponProductionJob
    {
        public const string CargoOwnerId = "weapon-production";

        public WeaponFamily Family { get; }
        public int TargetTier { get; }
        public int ReservedParts { get; }
        public string MaterialId { get; }
        public int ReservedMaterial { get; }
        public float TotalSeconds { get; }
        public float RemainingSeconds { get; private set; }

        public float Progress01 => TotalSeconds > 0f ? UnityEngine.Mathf.Clamp01(1f - RemainingSeconds / TotalSeconds) : 1f;

        public WeaponProductionJob(WeaponFamily family, int targetTier, int reservedParts, string materialId, int reservedMaterial, float totalSeconds, float remainingSeconds)
        {
            Family = family; TargetTier = targetTier; ReservedParts = reservedParts;
            MaterialId = materialId; ReservedMaterial = reservedMaterial;
            TotalSeconds = totalSeconds; RemainingSeconds = remainingSeconds;
        }

        /// <summary>Returns true the tick that completes the job.</summary>
        public bool Tick(float dt)
        {
            if (RemainingSeconds <= 0f) return false;
            RemainingSeconds -= dt;
            if (RemainingSeconds > 0f) return false;
            RemainingSeconds = 0f;
            return true;
        }
    }

    /// <summary>
    /// Pure, stateless orchestration for ship weapon production/upgrade and
    /// hardpoint equip (VS-013 §2/§3). A single reservation owner id means
    /// only one weapon job can ever be in flight at a time (mirrors the
    /// Hangar/Module single-slot convention) -- fully rezervasyonlu, tek
    /// otoriteli, save-safe. Never applied twice: <see cref="Complete"/>
    /// consumes the reservation exactly once. Combat-lockout ("Loadout
    /// yalnız combat dışında değiştirilebilir") is enforced by the caller
    /// passing <c>combatActive: true</c> — this class has no notion of
    /// combat state itself.
    /// </summary>
    public static class WeaponLoadoutService
    {
        public static WeaponProductionRejectReason CheckStart(WeaponProductionJob activeJob, WeaponFamily family,
            int targetTier, int weaponCoordinationTier, LogisticsCargo cargo, bool combatActive)
        {
            if (combatActive) return WeaponProductionRejectReason.CombatActive;
            if (activeJob != null) return WeaponProductionRejectReason.QueueBusy;
            if (targetTier < 1 || targetTier > WeaponCatalog.MaxTierThisSlice) return WeaponProductionRejectReason.TargetTierInvalid;
            if (!IsFamilyUnlocked(family, weaponCoordinationTier)) return WeaponProductionRejectReason.FamilyLocked;

            WeaponTierCost cost = WeaponCatalog.CostForTier(family, targetTier);
            if (cargo == null) return WeaponProductionRejectReason.InsufficientResources;
            if (cargo.GetAvailable(ProcessingCatalog.BasicPartId) < cost.Parts - 0.0001f) return WeaponProductionRejectReason.InsufficientResources;
            if (cargo.GetAvailable(cost.MaterialId) < cost.Material - 0.0001f) return WeaponProductionRejectReason.InsufficientResources;
            return WeaponProductionRejectReason.None;
        }

        /// <summary>Laser is the free starting weapon (never locked); Plasma needs Weapon Coordination T1+, EM needs T2+.</summary>
        public static bool IsFamilyUnlocked(WeaponFamily family, int weaponCoordinationTier) => family switch
        {
            WeaponFamily.Laser => true,
            WeaponFamily.Plasma => weaponCoordinationTier >= CombatCatalog.PlasmaUnlockWeaponCoordinationTier,
            WeaponFamily.Em => weaponCoordinationTier >= CombatCatalog.EmUnlockWeaponCoordinationTier,
            _ => false
        };

        public static WeaponProductionJob TryStart(WeaponProductionJob activeJob, WeaponFamily family, int targetTier,
            int weaponCoordinationTier, LogisticsCargo cargo, bool combatActive)
        {
            if (CheckStart(activeJob, family, targetTier, weaponCoordinationTier, cargo, combatActive) != WeaponProductionRejectReason.None) return null;
            WeaponTierCost cost = WeaponCatalog.CostForTier(family, targetTier);
            var costs = new List<(string itemId, float amount)> { (ProcessingCatalog.BasicPartId, cost.Parts), (cost.MaterialId, cost.Material) };
            if (!cargo.TryReserveAll(WeaponProductionJob.CargoOwnerId, costs)) return null;
            return new WeaponProductionJob(family, targetTier, cost.Parts, cost.MaterialId, cost.Material, cost.Seconds, cost.Seconds);
        }

        /// <summary>Permanently consumes the reservation and equips the hardpoint. Caller-provided hardpointIndex must already be validated as a real, in-range, currently-active slot.</summary>
        public static void Complete(WeaponProductionJob job, LogisticsCargo cargo, HardpointState hardpoint)
        {
            cargo.ConsumeReservation(WeaponProductionJob.CargoOwnerId);
            hardpoint.Equip(job.Family, job.TargetTier);
        }

        /// <summary>75% resource refund (SurfaceStructureCatalog.RefundFraction), same standard as every other cancellable job in this project.</summary>
        public static Dictionary<string, float> Cancel(LogisticsCargo cargo) =>
            cargo.ConsumeReservationForRefund(WeaponProductionJob.CargoOwnerId, WeaponCatalog.ProductionRefundFraction);

        // --- Refit: freeing an occupied hardpoint ---------------------------------

        /// <summary>
        /// Gates freeing one equipped hardpoint so a different family can be
        /// produced into it. VS-013 §2 already says the loadout is changeable
        /// outside combat; until this seam existed the loadout could only ever be
        /// <i>filled</i>, never changed, so a Hull Form I ship -- one active
        /// hardpoint, occupied at birth by the free Laser -- could never start any
        /// weapon production at all and answered every request with
        /// <see cref="WeaponProductionRejectReason.NoHardpointAvailable"/>.
        /// <para>
        /// Releasing carries no cost, no duration and no refund of its own: the
        /// weapon that occupied the slot is simply gone, and its replacement is
        /// paid for in full, at catalog cost and catalog duration, by the ordinary
        /// production job that follows. The combat lockout and the single-job rule
        /// are the same two the production path enforces, and they live here rather
        /// than at any call site so tutorial and normal play cannot drift apart.
        /// </para>
        /// </summary>
        public static HardpointReleaseRejectReason CheckRelease(WeaponProductionJob activeJob, HardpointState hardpoint, bool combatActive)
        {
            if (combatActive) return HardpointReleaseRejectReason.CombatActive;
            if (activeJob != null) return HardpointReleaseRejectReason.QueueBusy;
            if (hardpoint == null) return HardpointReleaseRejectReason.NoSuchHardpoint;
            if (hardpoint.IsEmpty) return HardpointReleaseRejectReason.AlreadyEmpty;
            return HardpointReleaseRejectReason.None;
        }

        /// <summary>Frees the hardpoint when <see cref="CheckRelease"/> allows it, and returns the same reason either way.</summary>
        public static HardpointReleaseRejectReason TryRelease(WeaponProductionJob activeJob, HardpointState hardpoint, bool combatActive)
        {
            HardpointReleaseRejectReason reject = CheckRelease(activeJob, hardpoint, combatActive);
            if (reject != HardpointReleaseRejectReason.None) return reject;
            hardpoint.Unequip();
            return HardpointReleaseRejectReason.None;
        }
    }
}
