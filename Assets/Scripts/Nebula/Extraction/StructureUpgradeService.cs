using System.Collections.Generic;

namespace Nebula.Extraction
{
    public enum StructureUpgradeRejectReason { None, AlreadyUpgrading, NotActive, MaxTier, LevelTooLow, CommandTierNotMet, WeaponCoordinationTierNotMet, NoControlledPlanet, InsufficientResources }

    /// <summary>
    /// VS-014 §2: pure, stateless orchestration for Command T1→T2, Laser
    /// T1→T2, and Plasma T1→T2 — a single active job per structure (its own
    /// WorldId+NodeId keys the reservation owner), atomic reserve, 75%
    /// cancel-of-remaining-reservation, and an atomic percentage-preserving
    /// tier swap on completion. Mirrors WeaponLoadoutService's exact shape.
    /// A brand-new Plasma T1 on an empty node is a normal fresh
    /// SurfaceStructure.Create build (like Command/Laser T1 always were),
    /// never routed through this service — this is upgrade-only.
    /// </summary>
    public static class StructureUpgradeService
    {
        public static StructureUpgradeRejectReason CheckStart(SurfaceStructure structure, int characterLevel,
            int planetCommandTier, int weaponCoordinationTier, bool hasAnyControlledPlanet, LogisticsCargo cargo, out StructureTierDef tierDef)
        {
            tierDef = null;
            if (structure == null || structure.ActiveUpgradeJob != null) return StructureUpgradeRejectReason.AlreadyUpgrading;
            if (structure.Runtime.State != DrillLifecycle.Active) return StructureUpgradeRejectReason.NotActive;

            int nextTier = structure.Runtime.Tier + 1;
            tierDef = PlanetaryDefenseCatalog.Get(structure.Type, nextTier);
            if (tierDef == null) return StructureUpgradeRejectReason.MaxTier;

            if (characterLevel < tierDef.RequiredCharacterLevel) return StructureUpgradeRejectReason.LevelTooLow;
            if (tierDef.RequiredCommandTier > 0 && planetCommandTier < tierDef.RequiredCommandTier) return StructureUpgradeRejectReason.CommandTierNotMet;
            if (tierDef.RequiredWeaponCoordinationTier > 0 && weaponCoordinationTier < tierDef.RequiredWeaponCoordinationTier) return StructureUpgradeRejectReason.WeaponCoordinationTierNotMet;
            if (structure.Type == SurfaceStructureType.Command && nextTier == 2 && !hasAnyControlledPlanet) return StructureUpgradeRejectReason.NoControlledPlanet;
            if (!HasCost(cargo, tierDef)) return StructureUpgradeRejectReason.InsufficientResources;
            return StructureUpgradeRejectReason.None;
        }

        private static bool HasCost(LogisticsCargo cargo, StructureTierDef tierDef)
        {
            if (cargo == null) return false;
            foreach (var (itemId, amount) in tierDef.FullCost()) if (cargo.GetAvailable(itemId) < amount - 0.0001f) return false;
            return true;
        }

        public static string OwnerId(SurfaceStructure structure) => StructureUpgradeJob.CargoOwnerPrefix + structure.WorldId + "/" + structure.NodeId;

        public static StructureUpgradeJob TryStart(SurfaceStructure structure, int characterLevel, int planetCommandTier,
            int weaponCoordinationTier, bool hasAnyControlledPlanet, LogisticsCargo cargo)
        {
            if (CheckStart(structure, characterLevel, planetCommandTier, weaponCoordinationTier, hasAnyControlledPlanet, cargo, out StructureTierDef tierDef) != StructureUpgradeRejectReason.None)
                return null;
            if (!cargo.TryReserveAll(OwnerId(structure), tierDef.FullCost())) return null;
            return new StructureUpgradeJob(structure.Type, tierDef.Tier, tierDef.BuildSeconds, tierDef.BuildSeconds);
        }

        /// <summary>Permanently consumes the reservation and atomically swaps in the new tier's KZG (percentage-preserved) — the structure's own State/activity is never touched.</summary>
        public static void Complete(SurfaceStructure structure, StructureUpgradeJob job, LogisticsCargo cargo)
        {
            cargo?.ConsumeReservation(OwnerId(structure));
            StructureTierDef tierDef = PlanetaryDefenseCatalog.Get(job.Type, job.TargetTier);
            if (tierDef != null) structure.Runtime.ApplyTierUpgrade(tierDef);
        }

        /// <summary>75% refund (PlanetaryDefenseCatalog.RefundFraction) of whatever remains reserved-but-unconsumed for the in-flight upgrade only — the structure's CURRENT tier/KZG is untouched.</summary>
        public static Dictionary<string, float> Cancel(SurfaceStructure structure, LogisticsCargo cargo) =>
            cargo?.ConsumeReservationForRefund(OwnerId(structure), PlanetaryDefenseCatalog.RefundFraction);
    }
}
