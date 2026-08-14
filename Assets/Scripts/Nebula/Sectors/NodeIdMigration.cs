using System;
using System.Globalization;

namespace Nebula.Sectors
{
    /// <summary>
    /// Pure string remap from the pre-VS-009 (schema v6) node id shape to the
    /// canonical VS-009 shape (VS-009 §5/§7): a v6 save's DrillDto/StructureDto
    /// nodeId values must be rewritten losslessly on load, with no duplicates
    /// or orphans. Old shapes: "{worldId}/node/drill", "{worldId}/node/command",
    /// "{worldId}/node/defense/01".."07", "{worldId}/node/support/01".."03".
    /// New shapes: "{worldId}/node/drill.0", ".../command.0",
    /// ".../defense.0".."6", ".../support.0".."2".
    /// </summary>
    public static class NodeIdMigration
    {
        private const string NodeSegment = "/node/";

        /// <summary>Returns the migrated id, or the input unchanged if it doesn't match a recognized v6 shape (already-canonical / unknown ids pass through).</summary>
        public static string Migrate(string oldNodeId)
        {
            if (string.IsNullOrEmpty(oldNodeId)) return oldNodeId;

            int at = oldNodeId.LastIndexOf(NodeSegment, StringComparison.Ordinal);
            if (at < 0) return oldNodeId;

            string worldId = oldNodeId.Substring(0, at);
            string suffix = oldNodeId.Substring(at + NodeSegment.Length);

            if (suffix == "drill") return worldId + NodeSegment + SurfaceSlot.Drill;
            if (suffix == "command") return worldId + NodeSegment + SurfaceSlot.Command;

            if (TryMigrateIndexed(suffix, "defense/", SurfaceSlot.DefenseCount, out int defenseIndex))
                return worldId + NodeSegment + SurfaceSlot.Defense(defenseIndex);
            if (TryMigrateIndexed(suffix, "support/", SurfaceSlot.SupportCount, out int supportIndex))
                return worldId + NodeSegment + SurfaceSlot.Support(supportIndex);

            return oldNodeId; // already canonical or unrecognized — passthrough
        }

        private static bool TryMigrateIndexed(string suffix, string prefix, int count, out int newIndex)
        {
            newIndex = -1;
            if (!suffix.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string numberPart = suffix.Substring(prefix.Length);
            if (!int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oldOneBased)) return false;
            if (oldOneBased < 1 || oldOneBased > count) return false;
            newIndex = oldOneBased - 1;
            return true;
        }
    }
}
