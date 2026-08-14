using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Extraction
{
    /// <summary>Why the Quick Asset Package can't be confirmed right now (VS-010 §2).</summary>
    public enum QuickPackageRejectReason
    {
        None, PackageAlreadyActive, EvacuationInProgress, NotScanned, NotInRange, TooFast, NotStabilized, NodesOccupied,
        StationConstructionMissing, ThreatDetected, InsufficientCargo
    }

    /// <summary>
    /// One in-flight Quick Asset Package build (VS-010 §2): a Drill T1 +
    /// Command T1 + Laser T1 sent as three staggered capsules (t=0/2/4s) on
    /// one planet's drill.0/command.0/defense.0 nodes, atomically reserved up
    /// front. Command's material is always the planet's own family material;
    /// Laser's is a separately player-chosen material — they may or may not
    /// be the same item id. Pure data — <see cref="QuickAssetPackageService"/>
    /// and the MonoBehaviour adapter (<c>ShipQuickAssetPackage</c>) own all behavior.
    /// </summary>
    public class QuickAssetPackageTransaction
    {
        public string TransactionId;
        public string PlanetWorldId;
        public string CommandMaterialId;
        public string LaserMaterialId;
        public bool DrillSent, CommandSent, LaserSent;
        public bool Cancelled;
        public float ElapsedSeconds;

        public bool Completed => DrillSent && CommandSent && LaserSent;
        public bool AnySent => DrillSent || CommandSent || LaserSent;
    }

    /// <summary>
    /// Pure cost table + precondition/transaction logic for the Quick Asset
    /// Package. No discounts, no free build, no universal drill — exactly
    /// Drill T1 (20 Basic) + Command T1 (30 Basic + 10 of the planet's family
    /// material) + Laser T1 (15 Basic + 5 of the player's chosen material) =
    /// 65 Basic + 15 special (10+5, possibly two different item ids), reserved
    /// atomically at confirm and consumed per-capsule as each one actually
    /// sends. VS-009's environment duration multiplier is applied exactly
    /// once, inside DrillRuntime/StructureRuntime's own constructors — this
    /// class never touches build duration at all.
    /// </summary>
    public static class QuickAssetPackageService
    {
        public const float MaxRelativeSpeed = 25f;

        // P1-DATA-010: these were five hand-written copies of prices that already had
        // owners. Today's numbers happened to agree, so a change to Command's or
        // Laser's catalog cost would not have failed anything — it would simply have
        // sold the same structure at two prices depending on which button the player
        // pressed. They are derived now, one arrow each, always from the structure's
        // own catalog row. They stop being compile-time constants as a result; nothing
        // used them in a constant context, and a cost that can only be written in one
        // place is worth more than a cost that can be written in a switch label.
        public static readonly int DrillBasicCost = ExtractionCatalog.DrillT1PartsCost;
        public static readonly int CommandBasicCost = SurfaceStructureCatalog.Command.PartsCost;
        public static readonly int CommandSpecialCost = SurfaceStructureCatalog.Command.MaterialCost;
        public static readonly int LaserBasicCost = SurfaceStructureCatalog.Laser.PartsCost;
        public static readonly int LaserSpecialCost = SurfaceStructureCatalog.Laser.MaterialCost;
        public static readonly int TotalBasicCost = DrillBasicCost + CommandBasicCost + LaserBasicCost;   // 65 today
        public static readonly int TotalSpecialCost = CommandSpecialCost + LaserSpecialCost;              // 15 today

        public const float DrillSendDelay = 0f;
        public const float CommandSendDelay = 2f;
        public const float LaserSendDelay = 4f;

        public static string OwnerId(string transactionId) => "quick_package:" + transactionId;

        /// <summary>Basic Parts pre-summed into one line; the two special-material costs merged into one line ONLY when Command and Laser happen to share the same material id (LogisticsCargo.TryReserveAll validates each list entry independently, so a duplicate item id split across two lines would silently under-check availability).</summary>
        public static List<(string itemId, float amount)> TotalCost(string commandMaterialId, string laserMaterialId)
        {
            var list = new List<(string, float)> { (ProcessingCatalog.BasicPartId, TotalBasicCost) };
            if (commandMaterialId == laserMaterialId) list.Add((commandMaterialId, TotalSpecialCost));
            else { list.Add((commandMaterialId, CommandSpecialCost)); list.Add((laserMaterialId, LaserSpecialCost)); }
            return list;
        }

        public static QuickPackageRejectReason CheckPreconditions(bool scanned, bool inRange, float relativeSpeed,
            bool orbitStabilized, bool drillNodeFree, bool commandNodeFree, bool laserNodeFree,
            bool hasStationConstructionT1, bool threatDetected, bool hasSufficientCargo)
        {
            if (!scanned) return QuickPackageRejectReason.NotScanned;
            if (!inRange) return QuickPackageRejectReason.NotInRange;
            if (relativeSpeed > MaxRelativeSpeed) return QuickPackageRejectReason.TooFast;
            if (!orbitStabilized) return QuickPackageRejectReason.NotStabilized;
            if (!drillNodeFree || !commandNodeFree || !laserNodeFree) return QuickPackageRejectReason.NodesOccupied;
            if (!hasStationConstructionT1) return QuickPackageRejectReason.StationConstructionMissing;
            if (threatDetected) return QuickPackageRejectReason.ThreatDetected;
            if (!hasSufficientCargo) return QuickPackageRejectReason.InsufficientCargo;
            return QuickPackageRejectReason.None;
        }

        public static bool HasSufficientCargo(LogisticsCargo cargo, string commandMaterialId, string laserMaterialId)
        {
            if (cargo == null) return false;
            if (cargo.GetAvailable(ProcessingCatalog.BasicPartId) < TotalBasicCost - 0.0001f) return false;
            if (commandMaterialId == laserMaterialId) return cargo.GetAvailable(commandMaterialId) >= TotalSpecialCost - 0.0001f;
            return cargo.GetAvailable(commandMaterialId) >= CommandSpecialCost - 0.0001f
                   && cargo.GetAvailable(laserMaterialId) >= LaserSpecialCost - 0.0001f;
        }

        /// <summary>Atomically reserves the FULL 65 Basic + 15 special. Returns null (no side effects) if the reservation fails.</summary>
        public static QuickAssetPackageTransaction TryConfirm(string transactionId, string planetWorldId,
            string commandMaterialId, string laserMaterialId, LogisticsCargo cargo)
        {
            if (!cargo.TryReserveAll(OwnerId(transactionId), TotalCost(commandMaterialId, laserMaterialId))) return null;
            return new QuickAssetPackageTransaction
            {
                TransactionId = transactionId, PlanetWorldId = planetWorldId,
                CommandMaterialId = commandMaterialId, LaserMaterialId = laserMaterialId
            };
        }

        /// <summary>Permanently consumes the Drill's exact share of the reservation and returns its 75%-cancel refund amount.</summary>
        public static int SendDrill(QuickAssetPackageTransaction tx, LogisticsCargo cargo)
        {
            cargo.ConsumePartialReservation(OwnerId(tx.TransactionId), ProcessingCatalog.BasicPartId, DrillBasicCost);
            tx.DrillSent = true;
            return Mathf.FloorToInt(DrillBasicCost * SurfaceStructureCatalog.RefundFraction);
        }

        /// <summary>Permanently consumes the Command's exact share (always the planet's family material) and returns its (parts, material) 75%-cancel refund amounts.</summary>
        public static (int refundParts, int refundMaterial) SendCommand(QuickAssetPackageTransaction tx, LogisticsCargo cargo)
        {
            string owner = OwnerId(tx.TransactionId);
            cargo.ConsumePartialReservation(owner, ProcessingCatalog.BasicPartId, CommandBasicCost);
            cargo.ConsumePartialReservation(owner, tx.CommandMaterialId, CommandSpecialCost);
            tx.CommandSent = true;
            return (Mathf.FloorToInt(CommandBasicCost * SurfaceStructureCatalog.RefundFraction),
                    Mathf.FloorToInt(CommandSpecialCost * SurfaceStructureCatalog.RefundFraction));
        }

        /// <summary>Permanently consumes the Laser's exact share (the player-chosen material, independent of Command's) and returns its (parts, material) 75%-cancel refund amounts.</summary>
        public static (int refundParts, int refundMaterial) SendLaser(QuickAssetPackageTransaction tx, LogisticsCargo cargo)
        {
            string owner = OwnerId(tx.TransactionId);
            cargo.ConsumePartialReservation(owner, ProcessingCatalog.BasicPartId, LaserBasicCost);
            cargo.ConsumePartialReservation(owner, tx.LaserMaterialId, LaserSpecialCost);
            tx.LaserSent = true;
            return (Mathf.FloorToInt(LaserBasicCost * SurfaceStructureCatalog.RefundFraction),
                    Mathf.FloorToInt(LaserSpecialCost * SurfaceStructureCatalog.RefundFraction));
        }

        /// <summary>
        /// Releases whatever is STILL reserved for unsent structures — 100%
        /// back (never-consumed reservations aren't physical stock removal,
        /// just an availability lock). A safe no-op once nothing is left
        /// reserved (e.g. calling this after all three already sent).
        /// Already-sent structures are cancelled separately via their own
        /// GameObject's 75%-refund Cancel(cargo) — this never touches those.
        /// <para>
        /// A null cargo releases nothing and is not an error. Its only caller,
        /// <c>ShipQuickAssetPackage.Cancel</c>, resolves the cargo as
        /// <c>logistics != null ? logistics.Cargo : null</c> — it has always allowed
        /// the null — and then handed it straight to a method that dereferenced it
        /// unconditionally. There is nothing to release when there is no cargo to
        /// release it from, and a cancel that throws leaves the transaction neither
        /// cancelled nor running.
        /// </para>
        /// </summary>
        public static void ReleaseUnsentReservation(QuickAssetPackageTransaction tx, LogisticsCargo cargo)
        {
            if (tx == null || cargo == null) return;
            cargo.ReleaseReservation(OwnerId(tx.TransactionId));
        }

        /// <summary>Cost of only the still-missing structure(s) — for Retry UI display; nothing is re-charged, the reservation already covers it.</summary>
        public static List<(string itemId, float amount)> MissingNodeCost(QuickAssetPackageTransaction tx)
        {
            var missing = new List<(string, float)>();
            if (!tx.DrillSent) missing.Add((ProcessingCatalog.BasicPartId, DrillBasicCost));
            if (!tx.CommandSent)
            {
                missing.Add((ProcessingCatalog.BasicPartId, CommandBasicCost));
                missing.Add((tx.CommandMaterialId, CommandSpecialCost));
            }
            if (!tx.LaserSent)
            {
                missing.Add((ProcessingCatalog.BasicPartId, LaserBasicCost));
                missing.Add((tx.LaserMaterialId, LaserSpecialCost));
            }
            return missing;
        }
    }
}
