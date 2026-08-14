using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §9: pure, deterministic (seed-hash only, never UnityEngine.Random)
    /// raid stage escalation, budget/composition, and target-planet selection.
    /// Never reads live enemy/structure MonoBehaviours directly — every input
    /// is a plain value the caller (the not-yet-built scheduler, VS-014 §11)
    /// already resolved from real state, keeping this class unit-testable
    /// without any scene.
    /// </summary>
    public static class RaidComposer
    {
        public const int MaxConcurrentEnemies = 24;
        public const float FighterBudgetCapFraction = 0.50f;
        public const float DisruptorBudgetCapFraction = 0.25f;
        public const float SiegeBudgetCapFraction = 0.30f;

        public static (int min, int max) BudgetRange(RaidStage stage) => stage switch
        {
            RaidStage.Pirate => (6, 10),
            RaidStage.Assault => (18, 28),
            RaidStage.Siege => (45, 65),
            _ => (0, 0)
        };

        public static float DifficultyBudgetMultiplier(RaidDifficulty difficulty) => difficulty switch
        {
            RaidDifficulty.Explorer => 0.75f,
            RaidDifficulty.Conqueror => 1.25f,
            _ => 1f
        };

        public static float DifficultyPressureMultiplier(RaidDifficulty difficulty) => difficulty switch
        {
            RaidDifficulty.Explorer => 0.8f,
            RaidDifficulty.Conqueror => 1.15f,
            _ => 1f
        };

        public static float DifficultyWarningOffsetSeconds(RaidDifficulty difficulty) => difficulty switch
        {
            RaidDifficulty.Explorer => 30f,
            RaidDifficulty.Conqueror => -15f,
            _ => 0f
        };

        /// <summary>Base enemy stats never change by difficulty (§9) — only budget/pressure-rate/warning do, which is exactly and only what the three multipliers above touch.</summary>
        public static RaidDoctrine DoctrineFor(RaidStage stage) => stage switch
        {
            RaidStage.Pirate => RaidDoctrine.Loot,
            RaidStage.Assault => RaidDoctrine.Suppression,
            _ => RaidDoctrine.Siege
        };

        /// <summary>
        /// VS-014 §9 escalation ladder: Siege is only eligible once the system is
        /// Dominant AND the Disruptor is already taught; if Dominance arrives
        /// before that, an Assault is scheduled instead specifically to teach it
        /// first (never two unknown classes at once, and Siege never opens with
        /// an untaught Disruptor). Assault needs >=3 Controlled planets in the
        /// system; otherwise Pirate is the only eligible stage.
        /// </summary>
        public static RaidStage NextEligibleStage(bool systemIsDominant, int controlledPlanetsInSystem, bool disruptorTaught)
        {
            if (systemIsDominant) return disruptorTaught ? RaidStage.Siege : RaidStage.Assault;
            if (controlledPlanetsInSystem >= 3) return RaidStage.Assault;
            return RaidStage.Pirate;
        }

        /// <summary>Returns the one new class this exact encounter should introduce, or null if none — never two unknowns in one encounter, and each stage only ever offers its own single next teaching slot.</summary>
        public static string WhichNewClassToTeach(RaidStage stage, RaidCampaignState campaign)
        {
            if (campaign == null) return null;
            switch (stage)
            {
                case RaidStage.Pirate:
                    return campaign.IsTaught(EnemyIds.LooterTransportMk1) ? null : EnemyIds.LooterTransportMk1;
                case RaidStage.Assault:
                    if (!campaign.IsTaught(EnemyIds.HeavyAssaultMk1)) return EnemyIds.HeavyAssaultMk1;
                    return campaign.IsTaught(EnemyIds.EmDisruptorMk1) ? null : EnemyIds.EmDisruptorMk1;
                case RaidStage.Siege:
                    // Defensive only -- NextEligibleStage already guarantees Siege never fires before the Disruptor is taught.
                    return campaign.IsTaught(EnemyIds.EmDisruptorMk1) ? null : EnemyIds.EmDisruptorMk1;
                default:
                    return null;
            }
        }

        public static int ResolveBudget(RaidStage stage, RaidDifficulty difficulty, ulong seed)
        {
            (int min, int max) = BudgetRange(stage);
            float t = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "raid/budget"));
            int baseBudget = min + Mathf.RoundToInt((max - min) * t);
            return Mathf.Max(1, Mathf.RoundToInt(baseBudget * DifficultyBudgetMultiplier(difficulty)));
        }

        /// <summary>
        /// Deterministic composition with up to 6 seed-perturbed retries to avoid
        /// an exact repeat of the campaign's most recent composition (§9/§18) --
        /// never a randomness source, purely a different derived sub-seed.
        /// </summary>
        public static List<string> Compose(RaidStage stage, int budget, ulong seed, string newClassToTeachDefId, RaidCampaignState campaign)
        {
            List<string> composition = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                composition = ComposeOnce(stage, budget, seed + (ulong)attempt, newClassToTeachDefId);
                if (!IsExactRepeatOfMostRecent(composition, campaign)) return composition;
            }
            return composition; // 6 deterministic retries exhausted -- accept rather than loop forever
        }

        private static bool IsExactRepeatOfMostRecent(List<string> composition, RaidCampaignState campaign)
        {
            if (campaign == null || campaign.History.Count == 0) return false;
            RaidHistoryEntry last = campaign.History[campaign.History.Count - 1];
            if (last.CompositionDefIds.Count != composition.Count) return false;
            var a = new List<string>(last.CompositionDefIds); a.Sort();
            var b = new List<string>(composition); b.Sort();
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static List<string> ComposeOnce(RaidStage stage, int budget, ulong seed, string newClassToTeachDefId)
        {
            var composition = new List<string>();
            int remaining = budget;

            void Add(string defId)
            {
                EnemyDef def = EnemyCatalog.Get(defId);
                if (def == null) return;
                composition.Add(defId);
                remaining -= def.ThreatBudget;
            }

            // Mandatory units first -- never optional, always included even if they consume the whole budget.
            switch (stage)
            {
                case RaidStage.Pirate:
                    Add(EnemyIds.FighterMk1);
                    Add(EnemyIds.LooterTransportMk1);
                    break;
                case RaidStage.Assault:
                    Add(EnemyIds.HeavyAssaultMk1);
                    break;
                case RaidStage.Siege:
                    Add(EnemyIds.SiegeMk1);
                    Add(EnemyIds.EmDisruptorMk1);
                    Add(EnemyIds.FighterMk1); // escort 1 of the required >=2
                    Add(EnemyIds.FighterMk1); // escort 2 of the required >=2
                    break;
            }
            if (!string.IsNullOrEmpty(newClassToTeachDefId) && !composition.Contains(newClassToTeachDefId)) Add(newClassToTeachDefId);

            string[] fillerPool = stage switch
            {
                RaidStage.Pirate => new[] { EnemyIds.FighterMk1, EnemyIds.LooterTransportMk1 },
                RaidStage.Assault => new[] { EnemyIds.FighterMk1, EnemyIds.HeavyAssaultMk1, EnemyIds.LooterTransportMk1 },
                RaidStage.Siege => new[] { EnemyIds.FighterMk1, EnemyIds.HeavyAssaultMk1 },
                _ => System.Array.Empty<string>()
            };

            int fighterSpent = RoleBudgetSpent(composition, EnemyRole.Fighter);
            int disruptorSpent = RoleBudgetSpent(composition, EnemyRole.Disruptor);
            int siegeSpent = RoleBudgetSpent(composition, EnemyRole.Siege);

            int guard = 0;
            while (remaining > 0 && composition.Count < MaxConcurrentEnemies && guard < 64 && fillerPool.Length > 0)
            {
                guard++;
                ulong pickHash = ExtractionCatalog.Hash(seed, "raid/fill/" + guard);
                int index = Mathf.FloorToInt(ExtractionCatalog.Unit01(pickHash) * fillerPool.Length) % fillerPool.Length;
                EnemyDef candidate = EnemyCatalog.Get(fillerPool[index]);
                if (candidate == null || candidate.ThreatBudget > remaining) break; // nothing left this pass can afford -- stop, never exceed budget

                if (candidate.Role == EnemyRole.Fighter && fighterSpent + candidate.ThreatBudget > budget * FighterBudgetCapFraction) continue;
                if (candidate.Role == EnemyRole.Disruptor && disruptorSpent + candidate.ThreatBudget > budget * DisruptorBudgetCapFraction) continue;
                if (candidate.Role == EnemyRole.Siege && siegeSpent + candidate.ThreatBudget > budget * SiegeBudgetCapFraction) continue;

                composition.Add(candidate.Id);
                remaining -= candidate.ThreatBudget;
                if (candidate.Role == EnemyRole.Fighter) fighterSpent += candidate.ThreatBudget;
                if (candidate.Role == EnemyRole.Disruptor) disruptorSpent += candidate.ThreatBudget;
                if (candidate.Role == EnemyRole.Siege) siegeSpent += candidate.ThreatBudget;
            }

            return composition;
        }

        private static int RoleBudgetSpent(List<string> composition, EnemyRole role)
        {
            int total = 0;
            foreach (string id in composition)
            {
                EnemyDef def = EnemyCatalog.Get(id);
                if (def != null && def.Role == role) total += def.ThreatBudget;
            }
            return total;
        }

        /// <summary>
        /// Highest-economic-value candidate wins; if that candidate is also the
        /// last TWO consecutive raids' target in this system (§9: "at most twice
        /// consecutively when an alternative exists"), the next-highest candidate
        /// is used instead when one exists.
        /// </summary>
        public static string SelectTargetPlanet(IReadOnlyList<(string worldId, float economicValue)> candidates, RaidCampaignState campaign)
        {
            if (candidates == null || candidates.Count == 0) return null;
            var sorted = new List<(string worldId, float economicValue)>(candidates);
            sorted.Sort((a, b) => b.economicValue.CompareTo(a.economicValue));
            string top = sorted[0].worldId;

            if (sorted.Count > 1 && campaign != null && campaign.History.Count >= 2)
            {
                RaidHistoryEntry last = campaign.History[campaign.History.Count - 1];
                RaidHistoryEntry secondLast = campaign.History[campaign.History.Count - 2];
                if (last.PlanetWorldId == top && secondLast.PlanetWorldId == top) return sorted[1].worldId;
            }
            return top;
        }
    }
}
