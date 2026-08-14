using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nebula.Extraction;

namespace Nebula.Content
{
    /// <summary>
    /// TF-003: a deterministic diagnostic/test fingerprint over a
    /// <see cref="RuntimeCatalog"/>'s authoritative content — never a save or
    /// generation gate (see <see cref="ContentRevision"/> for the actual,
    /// separate, deliberately-bumped-by-hand revision number). Every
    /// authoritative field from every domain is formatted into a canonical
    /// "domain|id|field=value" line using <see cref="CultureInfo.InvariantCulture"/>
    /// (never the current thread culture — Turkish locale alone formats
    /// floats with a comma decimal separator, which would silently change
    /// the fingerprint depending on the machine's regional settings), all
    /// lines are sorted ordinally before hashing (so dictionary/list
    /// enumeration order never matters), and the whole buffer is hashed with
    /// the project's existing deterministic FNV-1a
    /// (<see cref="ExtractionCatalog.Hash(string)"/>) rather than inventing a
    /// second hash algorithm. Visual bindings are never part of this — this
    /// type has no dependency on <see cref="IVisualRegistry"/> at all.
    /// </summary>
    public static class ContentFingerprint
    {
        /// <summary>Runtime content revision — separate from save schema and generation version, bumped by hand only when this content layer's own shape changes. Never a save field, never a gameplay gate. VS-014 §1: 2 (Command T2/Laser T2/Plasma T1-T2, 4 new enemies, Pirate Base POI type added). VS-015 §2: 3 (Engineer/Escort Mk I-II vehicles, Sensor Array/Maintenance Platform T1-T2 structures).</summary>
        public const int ContentRevision = 3;

        public static ulong Compute(RuntimeCatalog catalog)
        {
            var lines = new List<string>();

            foreach (ContentItem v in catalog.Items.Values)
                lines.Add(Line("item", v.Id, F("name", v.DisplayName), F("volume", v.VolumePerUnit)));

            foreach (ContentRecipe v in catalog.Recipes.Values)
                lines.Add(Line("recipe", v.Id, F("in", v.InputItemId), F("inCount", v.InputCount), F("out", v.OutputItemId), F("outCount", v.OutputCount), F("seconds", v.Seconds)));

            foreach (ContentModule v in catalog.Modules.Values)
            {
                var fields = new List<string> { F("name", v.DisplayName), F("unlock", v.UnlockLevel), F("maxTier", v.MaxTierThisSlice), F("free", v.FreeStarting) };
                foreach (ContentModuleTierCost tc in v.TierCosts)
                {
                    fields.Add(F($"t{tc.Tier}.points", tc.DevelopmentPoints));
                    fields.Add(F($"t{tc.Tier}.seconds", tc.Seconds));
                    foreach (ContentResourceCost c in tc.Resources) fields.Add(F($"t{tc.Tier}.{c.ItemId}", c.Amount));
                }
                lines.Add(Line("module", v.Id, fields.ToArray()));
            }

            foreach (ContentVehicle v in catalog.Vehicles.Values)
            {
                var fields = new List<string>
                {
                    F("name", v.DisplayName), F("unlock", v.UnlockLevel), F("hangarTier", v.RequiredHangarTier), F("mk", v.Mk),
                    F("buildSeconds", v.BuildSeconds), F("speed", v.Speed), F("shield", v.Shield), F("armor", v.Armor), F("hull", v.Hull),
                    F("scanRange", v.ScanRange), F("scanSeconds", v.ScanSeconds), F("cargo", v.CargoCapacity), F("transfer", v.TransferVolumePerSecond),
                    F("basicPart", v.BasicPartCost), F("specialMaterial", v.SpecialMaterialCost),
                    F("commandCost", v.CommandCost), F("repairRate", v.RepairRatePerSecond), F("recoverSeconds", v.DisabledVehicleRecoverSeconds),
                    F("laserDps", v.FixedLaserDps), F("laserRange", v.FixedLaserRange)
                };
                foreach (ContentResourceCost c in v.UpgradeCost) fields.Add(F($"upgrade.{c.ItemId}", c.Amount));
                foreach (ContentResourceCost c in v.FixedDirectBuildCost) fields.Add(F($"fixedDirect.{c.ItemId}", c.Amount));
                lines.Add(Line("vehicle", v.Id, fields.ToArray()));
            }

            foreach (ContentStructure v in catalog.Structures.Values)
            {
                var fields = new List<string>
                {
                    F("name", v.DisplayName), F("parts", v.PartsCost), F("material", v.MaterialCost),
                    F("anyMaterial", v.AnyMaterial), F("buildSeconds", v.BuildSeconds), F("shield", v.ShieldMax), F("armor", v.ArmorMax), F("hull", v.HullMax),
                    F("tier", v.Tier), F("reqCommandTier", v.RequiredCommandTier), F("reqWeaponCoordTier", v.RequiredWeaponCoordinationTier),
                    F("dps", v.Dps), F("range", v.Range), F("turnRate", v.TurnDegreesPerSecond),
                    F("overheatFireSeconds", v.ContinuousFireSecondsBeforeOverheat), F("overheatCooldown", v.OverheatCooldownSeconds),
                    F("splashRadius", v.SplashRadius), F("splashMaxFraction", v.SplashMaxFractionOfDirect),
                    F("warningBonus", v.WarningBonusSeconds), F("engineerRepairMult", v.EngineerRepairMultiplier),
                    F("recoveryTimeMult", v.RecoveryTimeMultiplier), F("requiresEngineer", v.RequiresLocalEngineer)
                };
                foreach (ContentResourceCost c in v.MaterialCosts) fields.Add(F($"cost.{c.ItemId}", c.Amount));
                lines.Add(Line("structure", v.Id, fields.ToArray()));
            }

            foreach (ContentLogisticsCenterTier v in catalog.LogisticsCenterTiers.Values)
            {
                var fields = new List<string>
                {
                    F("name", v.DisplayName), F("tier", v.Tier), F("level", v.RequiredLevel), F("moduleTier", v.RequiredModuleTier),
                    F("buildSeconds", v.BuildSeconds), F("capacity", v.Capacity), F("routes", v.MaxRoutes), F("docks", v.DockCount),
                    F("transfer", v.TransferVolumePerSecond), F("shield", v.ShieldMax), F("armor", v.ArmorMax), F("hull", v.HullMax)
                };
                foreach (ContentResourceCost c in v.Costs) fields.Add(F($"cost.{c.ItemId}", c.Amount));
                lines.Add(Line("logisticsCenterTier", v.Id, fields.ToArray()));
            }

            foreach (ContentWeapon v in catalog.Weapons.Values)
            {
                var fields = new List<string> { F("name", v.DisplayName), F("maxTier", v.MaxTierThisSlice), F("dps", v.BaseDps), F("range", v.BaseRange) };
                foreach (ContentWeaponTierCost tc in v.TierCosts)
                {
                    fields.Add(F($"t{tc.Tier}.seconds", tc.Seconds));
                    foreach (ContentResourceCost c in tc.Resources) fields.Add(F($"t{tc.Tier}.{c.ItemId}", c.Amount));
                }
                lines.Add(Line("weapon", v.Id, fields.ToArray()));
            }

            foreach (ContentEnemy v in catalog.Enemies.Values)
                lines.Add(Line("enemy", v.Id, F("name", v.DisplayName), F("speed", v.Speed), F("shield", v.ShieldMax), F("armor", v.ArmorMax), F("hull", v.HullMax),
                    F("combatCapable", v.IsCombatCapable), F("dps", v.Dps), F("attackRange", v.AttackRange),
                    F("bandMin", v.EngageBandMin), F("bandMax", v.EngageBandMax), F("break", v.BreakDistance), F("approach", v.ApproachThreshold),
                    F("retreat", v.RetreatHullFraction), F("scan", v.ScanSeconds),
                    F("role", v.Role ?? ""), F("threatBudget", v.ThreatBudget), F("cargoCapacity", v.CargoCapacityVolume),
                    F("cargoTransfer", v.CargoTransferVolumePerSecond), F("telegraph", v.TelegraphSeconds),
                    F("disrupt", v.DisruptSeconds), F("disruptImmunity", v.DisruptImmunitySeconds)));

            foreach (ContentPoiType v in catalog.PoiTypes.Values)
            {
                var fields = new List<string> { F("name", v.DisplayName), F("safe", v.SafeWeight), F("unstable", v.UnstableWeight), F("xp", v.XpReward) };
                foreach (ContentResourceCost c in v.CostReferences) fields.Add(F($"cost.{c.ItemId}", c.Amount));
                lines.Add(Line("poiType", v.Id, fields.ToArray()));
            }

            lines.Sort(System.StringComparer.Ordinal);
            return ExtractionCatalog.Hash(string.Join("\n", lines));
        }

        private static string Line(string domain, string id, params string[] fields) =>
            $"{domain}|{id}|" + string.Join("|", fields.OrderBy(f => f, System.StringComparer.Ordinal));

        private static string F(string key, string value) => $"{key}={value}";
        private static string F(string key, bool value) => $"{key}={(value ? "1" : "0")}";
        private static string F(string key, int value) => $"{key}={value.ToString(CultureInfo.InvariantCulture)}";
        private static string F(string key, float value) => $"{key}={value.ToString("R", CultureInfo.InvariantCulture)}";
    }
}
