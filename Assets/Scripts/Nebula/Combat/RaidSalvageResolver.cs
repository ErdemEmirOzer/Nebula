using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §13: deterministic (encounter-seed only, never UnityEngine.Random)
    /// post-raid salvage — 10-20% of the composition's total material value,
    /// ×1.25 on a Decisive Victory, paid out only as existing Basic Parts and
    /// processed materials (never a new item/currency), as one physical
    /// CombatWreckage the caller adds to CombatEncounterDirector.Wreckage
    /// exactly once per resolved raid. A raid enemy has no standalone "gold"
    /// stat, so its own ThreatBudget (VS-014 §9's already-established, only
    /// existing per-unit value number) is reused as the value basis rather
    /// than inventing a second parallel one.
    /// </summary>
    public static class RaidSalvageResolver
    {
        public const float MinFraction = 0.10f;
        public const float MaxFraction = 0.20f;
        public const float DecisiveVictoryMultiplier = 1.25f;

        /// <summary>ThreatBudget "value units" converted into real Basic Parts / one processed material — centralized here so the ratio is never re-typed at a call site.</summary>
        public const float BasicPartsPerValueUnit = 4f;
        public const float MaterialUnitsPerValueUnit = 1.5f;

        public static CombatWreckageState Resolve(string wreckageId, ulong encounterSeed, RaidOutcome outcome, IReadOnlyList<string> compositionDefIds)
        {
            var wreck = new CombatWreckageState(wreckageId);
            if (outcome == RaidOutcome.Failure) return wreck; // no reward for a lost defense

            int totalThreatValue = 0;
            if (compositionDefIds != null)
                foreach (string defId in compositionDefIds)
                {
                    EnemyDef def = EnemyCatalog.Get(defId);
                    if (def != null) totalThreatValue += def.ThreatBudget;
                }
            if (totalThreatValue <= 0) return wreck;

            float fractionRoll = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(encounterSeed, "raid/salvage/fraction"));
            float fraction = Mathf.Lerp(MinFraction, MaxFraction, fractionRoll);
            if (outcome == RaidOutcome.DecisiveVictory) fraction *= DecisiveVictoryMultiplier;

            float totalValue = totalThreatValue * fraction;

            int basicParts = Mathf.RoundToInt(totalValue * BasicPartsPerValueUnit);
            if (basicParts > 0) wreck.Nodes.Add(new CombatWreckageNode(ProcessingCatalog.BasicPartId, basicParts));

            int materialIndex = Mathf.FloorToInt(ExtractionCatalog.Unit01(ExtractionCatalog.Hash(encounterSeed, "raid/salvage/material")) * ProcessingCatalog.MaterialIds.Length) % ProcessingCatalog.MaterialIds.Length;
            int materialAmount = Mathf.RoundToInt(totalValue * MaterialUnitsPerValueUnit);
            if (materialAmount > 0) wreck.Nodes.Add(new CombatWreckageNode(ProcessingCatalog.MaterialIds[materialIndex], materialAmount));

            return wreck;
        }
    }
}
