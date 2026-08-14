namespace Nebula.Sectors
{
    /// <summary>
    /// Canonical logical surface-slot ids (VS-009 §5): the same 12-slot
    /// contract on every planet — 1 Drill + 1 Command + 7 Defense + 3
    /// Support. A node's persistent identity is
    /// "{planetId}/node/{logicalSlotId}"; direction is derived only from
    /// (SurfaceLayoutSeed, logicalSlotId) — never from free placement, and a
    /// tier upgrade always stays on the same node.
    /// </summary>
    public static class SurfaceSlot
    {
        public const string Drill = "drill.0";
        public const string Command = "command.0";
        public const int DefenseCount = 7;
        public const int SupportCount = 3;

        public static string Defense(int index) => $"defense.{index}";
        public static string Support(int index) => $"support.{index}";
    }
}
