using Nebula.Extraction;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A: converts a headless <see cref="StructureDto"/> to/from a real
    /// <see cref="Durability"/> instance so remote damage/repair can reuse
    /// <see cref="Combat.DamageResolver"/> and <see cref="Hangar.EngineerFieldWork"/>
    /// exactly like a live <see cref="SurfaceStructure"/> does -- max KZG always
    /// comes from the same catalogs a live structure reads (SurfaceStructureCatalog
    /// for T1, PlanetaryDefenseCatalog for T2+), never a second number set.
    /// </summary>
    public static class RemoteStructureAccess
    {
        public static (float shieldMax, float armorMax, float hullMax) MaxFor(SurfaceStructureType type, int tier)
        {
            StructureTierDef tierDef = tier > 1 ? PlanetaryDefenseCatalog.Get(type, tier) : null;
            if (tierDef != null) return (tierDef.ShieldMax, tierDef.ArmorMax, tierDef.HullMax);
            SurfaceStructureDef def = SurfaceStructureCatalog.Get(type);
            return (def.ShieldMax, def.ArmorMax, def.HullMax);
        }

        public static Durability ToDurability(StructureDto dto)
        {
            (float shieldMax, float armorMax, float hullMax) = MaxFor((SurfaceStructureType)dto.type, dto.tier);
            var d = new Durability(shieldMax, armorMax, hullMax, 0f);
            d.RestoreFromSave(dto.shield, dto.armor, dto.hull, dto.regenTimer);
            return d;
        }

        public static void WriteBack(StructureDto dto, Durability d)
        {
            dto.shield = d.Shield; dto.armor = d.Armor; dto.hull = d.Hull; dto.regenTimer = d.SavedRegenTimer;
            if (d.Disabled) dto.state = (int)DrillLifecycle.Disabled;
            else if (dto.state == (int)DrillLifecycle.Disabled) dto.state = (int)DrillLifecycle.Active; // repaired back up
        }
    }
}
