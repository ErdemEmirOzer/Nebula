namespace Nebula.Extraction
{
    /// <summary>VS-014/VS-015: Plasma/SensorArray/MaintenancePlatform are purely additive (never a save-facing ordinal reorder) — existing Command=0/Laser=1/Plasma=2 values are untouched, so every pre-v13 StructureDto.type value keeps resolving unchanged.</summary>
    public enum SurfaceStructureType { Command = 0, Laser = 1, Plasma = 2, SensorArray = 3, MaintenancePlatform = 4 }

    /// <summary>
    /// Definition of a buildable surface structure (beyond the drill). Cost is
    /// Basic Building Parts plus special materials — either the planet's own
    /// family material (Command) or any single material family (Laser). Stable,
    /// data-driven, no random quality.
    /// </summary>
    public class SurfaceStructureDef
    {
        public SurfaceStructureType Type { get; }
        public string Name { get; }
        public SurfaceNodeKind NodeKind { get; }
        public int PartsCost { get; }
        public int MaterialCost { get; }
        /// <summary>True = any one material family (Laser); false = the planet's family material (Command).</summary>
        public bool AnyMaterial { get; }
        public float BuildSeconds { get; }
        public float ShieldMax { get; }
        public float ArmorMax { get; }
        public float HullMax { get; }

        public SurfaceStructureDef(SurfaceStructureType type, string name, SurfaceNodeKind nodeKind,
            int partsCost, int materialCost, bool anyMaterial, float buildSeconds,
            float shieldMax, float armorMax, float hullMax)
        {
            Type = type; Name = name; NodeKind = nodeKind;
            PartsCost = partsCost; MaterialCost = materialCost; AnyMaterial = anyMaterial;
            BuildSeconds = buildSeconds;
            ShieldMax = shieldMax; ArmorMax = armorMax; HullMax = hullMax;
        }
    }

    public static class SurfaceStructureCatalog
    {
        public const float RefundFraction = 0.75f;

        /// <summary>
        /// VS-013.1 §4/VS-014 §2: whether this structure type counts as a real
        /// active planetary DEFENSE WEAPON for First Probe trigger purposes —
        /// Command Center alone (no weapon) must never qualify. Laser and
        /// Plasma both are; Command never is.
        /// </summary>
        public static bool IsActiveDefenseWeapon(SurfaceStructureType type) => type == SurfaceStructureType.Laser || type == SurfaceStructureType.Plasma;

        public static readonly SurfaceStructureDef Command = new SurfaceStructureDef(
            SurfaceStructureType.Command, "Command Center", SurfaceNodeKind.Command,
            partsCost: 30, materialCost: 10, anyMaterial: false, buildSeconds: 60f,
            shieldMax: 0f, armorMax: 600f, hullMax: 1000f);

        public static readonly SurfaceStructureDef Laser = new SurfaceStructureDef(
            SurfaceStructureType.Laser, "Laser Defense", SurfaceNodeKind.Defense,
            partsCost: 15, materialCost: 5, anyMaterial: true, buildSeconds: 20f,
            shieldMax: 100f, armorMax: 250f, hullMax: 400f);

        /// <summary>
        /// VS-014.1: Plasma's T1 base def (node/refund shape plus the actual
        /// parts/build/KZG numbers) — the single source of truth for these
        /// values. PlanetaryDefenseCatalog.PlasmaT1 reads FROM this field
        /// (the same one-way direction LaserT1 already uses for
        /// SurfaceStructureCatalog.Laser), never the other way around: a
        /// static field initializer here must never reach into
        /// PlanetaryDefenseCatalog, or the two types' eager static
        /// constructors deadlock-race into reading each other's
        /// not-yet-assigned fields as null (VS-014.1 §1).
        /// </summary>
        public static readonly SurfaceStructureDef Plasma = new SurfaceStructureDef(
            SurfaceStructureType.Plasma, "Plasma Defense", SurfaceNodeKind.Defense,
            partsCost: 25, materialCost: 0, anyMaterial: false,
            buildSeconds: 30f,
            shieldMax: 100f, armorMax: 450f, hullMax: 600f);

        /// <summary>VS-015 §7: Sensor Array T1 base def — PlanetaryDefenseCatalog.SensorArrayT1 reads FROM this field, same one-way direction as Plasma above.</summary>
        public static readonly SurfaceStructureDef SensorArray = new SurfaceStructureDef(
            SurfaceStructureType.SensorArray, "Sensor Array", SurfaceNodeKind.Support,
            partsCost: 35, materialCost: 0, anyMaterial: false,
            buildSeconds: 60f,
            shieldMax: 200f, armorMax: 250f, hullMax: 350f);

        /// <summary>VS-015 §7: Maintenance Platform T1 base def.</summary>
        public static readonly SurfaceStructureDef MaintenancePlatform = new SurfaceStructureDef(
            SurfaceStructureType.MaintenancePlatform, "Maintenance Platform", SurfaceNodeKind.Support,
            partsCost: 40, materialCost: 0, anyMaterial: false,
            buildSeconds: 75f,
            shieldMax: 100f, armorMax: 400f, hullMax: 600f);

        public static SurfaceStructureDef Get(SurfaceStructureType type) => type switch
        {
            SurfaceStructureType.Command => Command,
            SurfaceStructureType.Plasma => Plasma,
            SurfaceStructureType.SensorArray => SensorArray,
            SurfaceStructureType.MaintenancePlatform => MaintenancePlatform,
            _ => Laser
        };
    }
}
