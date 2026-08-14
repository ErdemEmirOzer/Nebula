using System.Collections.Generic;
using System.Linq;
using Nebula.Extraction;

namespace Nebula.Content
{
    /// <summary>
    /// TF-003: validates an <see cref="IContentCatalog"/>'s RAW (pre-dedup)
    /// content — stable-id format, uniqueness (including across different
    /// domains), and every cross-reference to a real item id. Runs on the raw
    /// per-domain lists, never on an already-built <see cref="RuntimeCatalog"/>
    /// (a duplicate id would already have silently collapsed to one entry by
    /// then — "last write wins" — so duplicate detection would be impossible
    /// after the fact). Every error is collected; nothing here throws by
    /// itself — the caller (<see cref="RuntimeCatalogValidationBoot"/> in
    /// composition, or a test) decides whether to raise
    /// <see cref="ContentValidationException"/>. Errors are returned in a
    /// deterministic order: by domain, then by the order items were
    /// enumerated within that domain — never by a Dictionary's hash order.
    /// </summary>
    public static class ContentCatalogValidator
    {
        public static List<string> Validate(IContentCatalog source)
        {
            var errors = new List<string>();
            var allIds = new Dictionary<string, string>(); // id -> the domain that first claimed it

            void CheckId(string id, string domain, string context)
            {
                if (!ContentIdRules.IsValidFormat(id, out string reason))
                {
                    errors.Add($"[{domain}] {context}: invalid id '{id ?? "<null>"}' ({reason}).");
                    return;
                }
                if (allIds.TryGetValue(id, out string firstDomain))
                    errors.Add($"[{domain}] {context}: duplicate id '{id}' (already used in [{firstDomain}]).");
                else
                    allIds[id] = domain;
            }

            var itemIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ContentItem item in source.Items)
            {
                CheckId(item.Id, "Items", item.Id ?? "<null>");
                if (item.Id != null) itemIds.Add(item.Id);
                if (item.VolumePerUnit < 0f) errors.Add($"[Items] {item.Id}: negative VolumePerUnit.");
            }

            void CheckItemRef(string itemId, string domain, string context)
            {
                if (string.IsNullOrEmpty(itemId)) { errors.Add($"[{domain}] {context}: empty item reference."); return; }
                if (!itemIds.Contains(itemId)) errors.Add($"[{domain}] {context}: references unknown item id '{itemId}'.");
            }

            foreach (ContentRecipe r in source.Recipes)
            {
                CheckId(r.Id, "Recipes", r.Id ?? "<null>");
                CheckItemRef(r.InputItemId, "Recipes", $"{r.Id} input");
                CheckItemRef(r.OutputItemId, "Recipes", $"{r.Id} output");
                if (r.InputCount < 0) errors.Add($"[Recipes] {r.Id}: negative InputCount.");
                if (r.OutputCount < 0) errors.Add($"[Recipes] {r.Id}: negative OutputCount.");
                if (r.Seconds < 0f) errors.Add($"[Recipes] {r.Id}: negative Seconds.");
            }

            var moduleIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ContentModule m in source.Modules)
            {
                CheckId(m.Id, "Modules", m.Id ?? "<null>");
                if (m.Id != null) moduleIds.Add(m.Id);
                if (m.UnlockLevel < 0) errors.Add($"[Modules] {m.Id}: negative UnlockLevel.");
                if (m.MaxTierThisSlice < 0) errors.Add($"[Modules] {m.Id}: negative MaxTierThisSlice.");
                if (m.TierCosts.Count != 5) errors.Add($"[Modules] {m.Id}: expected 5 tier costs (T1-T5), found {m.TierCosts.Count}.");
                foreach (ContentModuleTierCost tc in m.TierCosts)
                {
                    if (tc.DevelopmentPoints < 0) errors.Add($"[Modules] {m.Id} T{tc.Tier}: negative DevelopmentPoints.");
                    if (tc.Seconds < 0f) errors.Add($"[Modules] {m.Id} T{tc.Tier}: negative Seconds.");
                    foreach (ContentResourceCost cost in tc.Resources)
                    {
                        CheckItemRef(cost.ItemId, "Modules", $"{m.Id} T{tc.Tier} cost");
                        if (cost.Amount < 0f) errors.Add($"[Modules] {m.Id} T{tc.Tier}: negative amount for '{cost.ItemId}'.");
                    }
                }
            }
            foreach (string freeId in source.FreeStartingModuleIds)
                if (!moduleIds.Contains(freeId)) errors.Add($"[Modules] FreeStartingModuleIds: '{freeId}' does not correspond to any real module.");

            foreach (ContentVehicle v in source.Vehicles)
            {
                CheckId(v.Id, "Vehicles", v.Id ?? "<null>");
                if (v.UnlockLevel < 0) errors.Add($"[Vehicles] {v.Id}: negative UnlockLevel.");
                if (v.RequiredHangarTier < 0) errors.Add($"[Vehicles] {v.Id}: negative RequiredHangarTier.");
                if (v.BuildSeconds < 0f) errors.Add($"[Vehicles] {v.Id}: negative BuildSeconds.");
                if (v.Speed < 0f || v.Shield < 0f || v.Armor < 0f || v.Hull < 0f) errors.Add($"[Vehicles] {v.Id}: negative speed/shield/armor/hull.");
                if (v.ScanRange < 0f || v.ScanSeconds < 0f) errors.Add($"[Vehicles] {v.Id}: negative scan range/seconds.");
                if (v.CargoCapacity < 0f || v.TransferVolumePerSecond < 0f) errors.Add($"[Vehicles] {v.Id}: negative cargo capacity/transfer rate.");
                if (v.BasicPartCost < 0) errors.Add($"[Vehicles] {v.Id}: negative BasicPartCost.");
                if (v.BasicPartCost > 0) CheckItemRef(ProcessingCatalog.BasicPartId, "Vehicles", $"{v.Id} basic part cost");
                if (v.SpecialMaterialCost < 0) errors.Add($"[Vehicles] {v.Id}: negative SpecialMaterialCost.");
                foreach (ContentResourceCost cost in v.UpgradeCost)
                {
                    CheckItemRef(cost.ItemId, "Vehicles", $"{v.Id} upgrade cost");
                    if (cost.Amount < 0f) errors.Add($"[Vehicles] {v.Id}: negative upgrade amount for '{cost.ItemId}'.");
                }
                // VS-015 §1/§2: Engineer/Escort additive fields.
                if (v.CommandCost < 1) errors.Add($"[Vehicles] {v.Id}: CommandCost must be at least 1.");
                if (v.RepairRatePerSecond < 0f || v.DisabledVehicleRecoverSeconds < 0f) errors.Add($"[Vehicles] {v.Id}: negative repair rate/recover seconds.");
                if (v.FixedLaserDps < 0f || v.FixedLaserRange < 0f) errors.Add($"[Vehicles] {v.Id}: negative fixed laser dps/range.");
                foreach (ContentResourceCost cost in v.FixedDirectBuildCost)
                {
                    CheckItemRef(cost.ItemId, "Vehicles", $"{v.Id} fixed direct-build cost");
                    if (cost.Amount < 0f) errors.Add($"[Vehicles] {v.Id}: negative fixed direct-build amount for '{cost.ItemId}'.");
                }
            }

            foreach (ContentStructure s in source.Structures)
            {
                CheckId(s.Id, "Structures", s.Id ?? "<null>");
                if (s.PartsCost < 0) errors.Add($"[Structures] {s.Id}: negative PartsCost.");
                if (s.PartsCost > 0) CheckItemRef(ProcessingCatalog.BasicPartId, "Structures", $"{s.Id} parts cost");
                if (s.MaterialCost < 0) errors.Add($"[Structures] {s.Id}: negative MaterialCost.");
                if (s.BuildSeconds < 0f) errors.Add($"[Structures] {s.Id}: negative BuildSeconds.");
                if (s.ShieldMax < 0f || s.ArmorMax < 0f || s.HullMax < 0f) errors.Add($"[Structures] {s.Id}: negative shield/armor/hull.");
                if (s.Tier < 1) errors.Add($"[Structures] {s.Id}: invalid Tier {s.Tier}.");
                if (s.RequiredCommandTier < 0 || s.RequiredWeaponCoordinationTier < 0) errors.Add($"[Structures] {s.Id}: negative required tier.");
                if (s.Dps < 0f || s.Range < 0f || s.TurnDegreesPerSecond < 0f) errors.Add($"[Structures] {s.Id}: negative combat stat.");
                foreach (ContentResourceCost cost in s.MaterialCosts)
                {
                    CheckItemRef(cost.ItemId, "Structures", $"{s.Id} material cost");
                    if (cost.Amount < 0f) errors.Add($"[Structures] {s.Id}: negative amount for '{cost.ItemId}'.");
                }
                // VS-015 §7: Sensor Array / Maintenance Platform additive fields.
                if (s.WarningBonusSeconds < 0f) errors.Add($"[Structures] {s.Id}: negative WarningBonusSeconds.");
                if (s.EngineerRepairMultiplier < 0f) errors.Add($"[Structures] {s.Id}: negative EngineerRepairMultiplier.");
                if (s.RecoveryTimeMultiplier < 0f) errors.Add($"[Structures] {s.Id}: negative RecoveryTimeMultiplier.");
            }

            foreach (ContentLogisticsCenterTier l in source.LogisticsCenterTiers)
            {
                CheckId(l.Id, "LogisticsCenterTiers", l.Id ?? "<null>");
                if (l.Tier < 1) errors.Add($"[LogisticsCenterTiers] {l.Id}: invalid tier {l.Tier}.");
                if (l.RequiredLevel < 0) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative RequiredLevel.");
                if (l.RequiredModuleTier < 0) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative RequiredModuleTier.");
                if (l.BuildSeconds < 0f) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative BuildSeconds.");
                if (l.Capacity < 0f) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative Capacity.");
                if (l.MaxRoutes < 0) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative MaxRoutes.");
                if (l.DockCount < 0) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative DockCount.");
                if (l.TransferVolumePerSecond < 0f) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative TransferVolumePerSecond.");
                if (l.ShieldMax < 0f || l.ArmorMax < 0f || l.HullMax < 0f) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative shield/armor/hull.");
                foreach (ContentResourceCost cost in l.Costs)
                {
                    CheckItemRef(cost.ItemId, "LogisticsCenterTiers", $"{l.Id} cost");
                    if (cost.Amount < 0f) errors.Add($"[LogisticsCenterTiers] {l.Id}: negative amount for '{cost.ItemId}'.");
                }
            }

            foreach (ContentWeapon w in source.Weapons)
            {
                CheckId(w.Id, "Weapons", w.Id ?? "<null>");
                if (w.MaxTierThisSlice < 0) errors.Add($"[Weapons] {w.Id}: negative MaxTierThisSlice.");
                if (w.BaseDps < 0f) errors.Add($"[Weapons] {w.Id}: negative BaseDps.");
                if (w.BaseRange < 0f) errors.Add($"[Weapons] {w.Id}: negative BaseRange.");
                if (w.TierCosts.Count != w.MaxTierThisSlice)
                    errors.Add($"[Weapons] {w.Id}: expected {w.MaxTierThisSlice} tier cost(s), found {w.TierCosts.Count}.");
                foreach (ContentWeaponTierCost tc in w.TierCosts)
                {
                    if (tc.Seconds < 0f) errors.Add($"[Weapons] {w.Id} T{tc.Tier}: negative Seconds.");
                    foreach (ContentResourceCost cost in tc.Resources)
                    {
                        CheckItemRef(cost.ItemId, "Weapons", $"{w.Id} T{tc.Tier} cost");
                        if (cost.Amount < 0f) errors.Add($"[Weapons] {w.Id} T{tc.Tier}: negative amount for '{cost.ItemId}'.");
                    }
                }
            }

            foreach (ContentEnemy e in source.Enemies)
            {
                CheckId(e.Id, "Enemies", e.Id ?? "<null>");
                if (e.Speed < 0f) errors.Add($"[Enemies] {e.Id}: negative Speed.");
                if (e.ShieldMax < 0f || e.ArmorMax < 0f || e.HullMax < 0f) errors.Add($"[Enemies] {e.Id}: negative shield/armor/hull.");
                if (e.Dps < 0f) errors.Add($"[Enemies] {e.Id}: negative Dps.");
                if (e.AttackRange < 0f) errors.Add($"[Enemies] {e.Id}: negative AttackRange.");
                if (e.EngageBandMin < 0f || e.EngageBandMax < 0f) errors.Add($"[Enemies] {e.Id}: negative engage band.");
                if (e.RetreatHullFraction < 0f || e.RetreatHullFraction > 1f) errors.Add($"[Enemies] {e.Id}: RetreatHullFraction out of [0,1].");
                if (e.ScanSeconds < 0f) errors.Add($"[Enemies] {e.Id}: negative ScanSeconds.");
                if (e.ThreatBudget < 0) errors.Add($"[Enemies] {e.Id}: negative ThreatBudget.");
                if (e.CargoCapacityVolume < 0f || e.CargoTransferVolumePerSecond < 0f) errors.Add($"[Enemies] {e.Id}: negative cargo capacity/transfer rate.");
                if (e.TelegraphSeconds < 0f || e.DisruptSeconds < 0f || e.DisruptImmunitySeconds < 0f) errors.Add($"[Enemies] {e.Id}: negative telegraph/disrupt seconds.");
            }

            foreach (ContentPoiType p in source.PoiTypes)
            {
                CheckId(p.Id, "PoiTypes", p.Id ?? "<null>");
                if (p.SafeWeight < 0f || p.UnstableWeight < 0f) errors.Add($"[PoiTypes] {p.Id}: negative risk weight.");
                if (p.XpReward < 0) errors.Add($"[PoiTypes] {p.Id}: negative XpReward.");
                foreach (ContentResourceCost cost in p.CostReferences)
                {
                    CheckItemRef(cost.ItemId, "PoiTypes", $"{p.Id} cost");
                    if (cost.Amount < 0f) errors.Add($"[PoiTypes] {p.Id}: negative amount for '{cost.ItemId}'.");
                }
            }

            return errors;
        }
    }
}
