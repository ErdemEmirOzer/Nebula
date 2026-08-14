using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>
    /// Pure decision function for the VS-013.2 physical repair safe-zone rule
    /// — mirrors StellarRiskResolver's shape: the MonoBehaviour side
    /// (ShipCombat) gathers real absolute positions from the live Safety
    /// Marker anchor and the live active-Command-Center registry, this side
    /// only ever compares already-resolved distances. Safe means the ship is
    /// within RepairCatalog.RepairSafeRadius of the Safety Marker OR at
    /// least one live active Command Center — never based on system
    /// ownership/identity alone.
    /// </summary>
    public static class RepairSafeZoneResolver
    {
        public static bool IsSafe(Vector3 shipAbsolute, Vector3? safetyMarkerAbsolute, IReadOnlyList<Vector3> activeCommandCenterAbsolutePositions)
        {
            if (safetyMarkerAbsolute.HasValue && Vector3.Distance(shipAbsolute, safetyMarkerAbsolute.Value) <= RepairCatalog.RepairSafeRadius) return true;
            if (activeCommandCenterAbsolutePositions != null)
                foreach (Vector3 pos in activeCommandCenterAbsolutePositions)
                    if (Vector3.Distance(shipAbsolute, pos) <= RepairCatalog.RepairSafeRadius) return true;
            return false;
        }

        public static float CapFraction(bool isSafe) => isSafe ? RepairCatalog.SafeZoneCapFraction : RepairCatalog.FieldCapFraction;
    }
}
