using Nebula.Extraction;

namespace Nebula.Remote
{
    /// <summary>VS-016A: converts a headless VehicleDto's raw shield/armor/hull fields to/from a real Durability instance -- same K/Z/G max lookup (VehicleDef) a live VehicleAgent already uses, so remote repair reuses EngineerFieldWork/DamageResolver exactly instead of a second HP model.</summary>
    public static class RemoteVehicleAccess
    {
        public static Durability ToDurability(VehicleDto dto, Hangar.VehicleDef def)
        {
            var d = new Durability(0f, def.Armor, def.Hull, 0f);
            float armor = dto.armor > 0f ? dto.armor : def.Armor;
            float hull = dto.hull > 0f ? dto.hull : def.Hull;
            d.RestoreFromSave(0f, armor, hull, 0f);
            return d;
        }

        public static void WriteBack(VehicleDto dto, Durability d)
        {
            dto.armor = d.Armor; dto.hull = d.Hull;
        }
    }
}
