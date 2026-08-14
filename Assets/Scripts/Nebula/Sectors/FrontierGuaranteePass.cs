using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Sectors
{
    /// <summary>
    /// Deterministic, minimal-edit adjustment of a freshly generated start
    /// sector (VS-008 §3) — never a reroll loop. Applied exactly once, right
    /// after <see cref="SectorManifestGenerator.Generate"/> builds the raw
    /// sector and before it is ever cached or persisted, so the result the
    /// player actually sees is always already final.
    /// </summary>
    public static class FrontierGuaranteePass
    {
        public const float MinNeighborDistance = 450000f;
        public const float MaxNeighborDistance = 750000f;

        private static readonly ResourceFamily[] AllFamilies =
        {
            ResourceFamily.Metal, ResourceFamily.Wood, ResourceFamily.Gas, ResourceFamily.Water, ResourceFamily.Diamond
        };

        public static SectorManifest Apply(ulong seed, SectorManifest sector)
        {
            var systems = new List<SystemManifest>(sector.Systems);
            int startIdx = systems.FindIndex(s => s.IsStartSystem);
            if (startIdx < 0 || systems.Count < 2) return sector; // nothing to guarantee against

            systems[startIdx] = EnsureFamilyDiversity(systems[startIdx], minFamilies: 3);

            int nearestIdx = NearestOtherIndex(systems, startIdx);
            if (nearestIdx >= 0)
            {
                (SystemManifest adjustedStart, SystemManifest adjustedNeighbor) = AdjustSeparation(systems[startIdx], systems[nearestIdx]);
                systems[startIdx] = adjustedStart;
                systems[nearestIdx] = adjustedNeighbor;
            }

            List<int> nearestThree = NearestOtherIndices(systems, startIdx, 3);
            EnsureProfileGuarantees(systems, nearestThree, seed);
            EnsureFamilyCoverage(systems, startIdx, nearestThree);
            EnsureStarClassGuarantees(systems, nearestThree, seed);
            EnsureEnvironmentGuarantees(systems, nearestThree);

            return new SectorManifest(sector.Coordinate, sector.GenerationVersion, systems);
        }

        // --- Nearest three foreign systems: no BlueGiant, >=2 distinct star classes (VS-009 §2) ---
        /// <summary>Only ever touches StarClass — never position/composition/family/rarity.</summary>
        private static void EnsureStarClassGuarantees(List<SystemManifest> systems, List<int> nearestThree, ulong seed)
        {
            if (nearestThree.Count == 0) return;

            foreach (int idx in nearestThree)
            {
                if (systems[idx].StarClass == StarClass.BlueGiant)
                {
                    StarClass replacement = StarClassCatalog.PickNonBlueGiant(seed, systems[idx].SystemId);
                    systems[idx] = systems[idx].WithStarClass(replacement);
                }
            }

            var distinct = new HashSet<StarClass>();
            foreach (int idx in nearestThree) distinct.Add(systems[idx].StarClass);
            if (distinct.Count < 2 && nearestThree.Count >= 2)
            {
                int target = nearestThree[0];
                StarClass current = systems[target].StarClass;
                StarClass alternate = current == StarClass.RedDwarf ? StarClass.Yellow : StarClass.RedDwarf;
                systems[target] = systems[target].WithStarClass(alternate);
            }
        }

        // --- Nearest three foreign systems: each has >=1 Calm planet (VS-009 §3) ---
        /// <summary>Only ever touches Environment — never family/rarity/position.</summary>
        private static void EnsureEnvironmentGuarantees(List<SystemManifest> systems, List<int> nearestThree)
        {
            foreach (int idx in nearestThree)
            {
                SystemManifest sys = systems[idx];
                bool hasCalm = false;
                foreach (PlanetManifest p in sys.Planets) if (p.Environment == EnvironmentClass.Calm) { hasCalm = true; break; }
                if (hasCalm || sys.Planets.Count == 0) continue;

                PlanetManifest last = sys.Planets[sys.Planets.Count - 1];
                systems[idx] = sys.WithReplacedPlanet(last.WithEnvironment(EnvironmentClass.Calm));
            }
        }

        // --- Start system: >=3 distinct families among its own planets -----------
        private static SystemManifest EnsureFamilyDiversity(SystemManifest sys, int minFamilies)
        {
            var planets = new List<PlanetManifest>(sys.Planets);
            var seen = new HashSet<ResourceFamily>();
            foreach (PlanetManifest p in planets) seen.Add(p.Family);
            if (seen.Count >= minFamilies) return sys;

            var missing = new List<ResourceFamily>();
            foreach (ResourceFamily f in AllFamilies) if (!seen.Contains(f)) missing.Add(f);

            seen.Clear();
            int cursor = 0;
            for (int i = 0; i < planets.Count && cursor < missing.Count; i++)
            {
                if (seen.Contains(planets[i].Family))
                {
                    planets[i] = planets[i].WithFrozenResource(missing[cursor], planets[i].Rarity);
                    seen.Add(missing[cursor]);
                    cursor++;
                }
                else seen.Add(planets[i].Family);
            }

            SystemManifest result = sys;
            foreach (PlanetManifest p in planets) result = result.WithReplacedPlanet(p);
            return result;
        }

        // --- Nearest-neighbor distance band ---------------------------------------
        private static (SystemManifest, SystemManifest) AdjustSeparation(SystemManifest start, SystemManifest neighbor)
        {
            (Vector3 min, Vector3 max) startBox = BoxFor(start.Sector, start.AbsoluteCenter);
            (Vector3 min, Vector3 max) neighborBox = BoxFor(neighbor.Sector, neighbor.AbsoluteCenter);
            Vector3 s = start.AbsoluteCenter;
            Vector3 n = neighbor.AbsoluteCenter;

            // Iterative, deterministic convergence: alternately pull/push each
            // point along the current connecting line, re-clamped to its own
            // box every step. A handful of passes is enough even when the two
            // boxes start diagonally far apart (single-shot placement can
            // undershoot the band by a wide margin in that geometry).
            for (int i = 0; i < 8; i++)
            {
                float distance = Vector3.Distance(s, n);
                if (distance >= MinNeighborDistance && distance <= MaxNeighborDistance) break;

                Vector3 dir = distance > 0.001f ? (n - s) / distance : Vector3.forward;
                float target = Mathf.Clamp(distance, MinNeighborDistance, MaxNeighborDistance);

                s = ClampToBox(n - dir * target, startBox);
                float achieved = Vector3.Distance(s, n);
                if (achieved < MinNeighborDistance - 0.5f || achieved > MaxNeighborDistance + 0.5f)
                    n = ClampToBox(s + dir * target, neighborBox);
            }

            return (start.WithCenter(s), neighbor.WithCenter(n));
        }

        private static (Vector3 min, Vector3 max) BoxFor(SectorCoordinate sector, Vector3 position)
        {
            Vector3 local = position - sector.Origin;
            int cx = Mathf.FloorToInt(local.x / SectorManifestGenerator.CellSize);
            int cy = Mathf.FloorToInt(local.y / SectorManifestGenerator.CellSize);
            int cz = Mathf.FloorToInt(local.z / SectorManifestGenerator.CellSize);
            Vector3 cellOrigin = sector.Origin + new Vector3(cx, cy, cz) * SectorManifestGenerator.CellSize;
            Vector3 marginVec = Vector3.one * SectorManifestGenerator.Margin;
            return (cellOrigin + marginVec, cellOrigin + new Vector3(SectorManifestGenerator.CellSize, SectorManifestGenerator.CellSize, SectorManifestGenerator.CellSize) - marginVec);
        }

        private static Vector3 ClampToBox(Vector3 v, (Vector3 min, Vector3 max) box) => new Vector3(
            Mathf.Clamp(v.x, box.min.x, box.max.x), Mathf.Clamp(v.y, box.min.y, box.max.y), Mathf.Clamp(v.z, box.min.z, box.max.z));

        // --- Nearest three foreign systems: Compact + Balanced + Rare+ presence ---
        /// <summary>
        /// Iterates (bounded) rather than forcing all three gaps in one pass:
        /// with only 3 candidate systems, blindly forcing a fixed index for
        /// each missing category can overwrite the very system that was
        /// already (naturally) satisfying a DIFFERENT, already-met category —
        /// e.g. forcing index 0 to Compact when index 0 was the sector's only
        /// naturally-Balanced system silently erases that guarantee. Each
        /// pass re-checks all three and prefers a target that isn't the sole
        /// provider of a currently-satisfied need; if none is available, it
        /// picks the best available candidate and the NEXT pass corrects
        /// whatever that broke (all 3 categories converge within a few passes).
        /// </summary>
        private static void EnsureProfileGuarantees(List<SystemManifest> systems, List<int> nearestThree, ulong seed)
        {
            if (nearestThree.Count == 0) return;

            for (int pass = 0; pass < 6; pass++)
            {
                bool hasCompact = false, hasBalanced = false;
                int compactIdx = -1, balancedIdx = -1;
                var rarePlusProviders = new List<int>();
                foreach (int idx in nearestThree)
                {
                    if (systems[idx].ProfileKind == SystemProfileKind.CompactFrontier) { hasCompact = true; compactIdx = idx; }
                    if (systems[idx].ProfileKind == SystemProfileKind.Balanced) { hasBalanced = true; balancedIdx = idx; }
                    foreach (PlanetManifest p in systems[idx].Planets)
                        if (p.Rarity >= Rarity.Rare) { rarePlusProviders.Add(idx); break; }
                }
                bool hasRarePlus = rarePlusProviders.Count > 0;
                if (hasCompact && hasBalanced && hasRarePlus) return;

                SystemProfileKind targetProfile; int targetPlanetCount;
                if (!hasCompact) { targetProfile = SystemProfileKind.CompactFrontier; targetPlanetCount = 4; }
                else if (!hasBalanced) { targetProfile = SystemProfileKind.Balanced; targetPlanetCount = 5; }
                else { targetProfile = SystemProfileKind.RichFrontier; targetPlanetCount = 6; }

                int target = -1;
                foreach (int idx in nearestThree)
                {
                    bool breaksCompact = hasCompact && idx == compactIdx && targetProfile != SystemProfileKind.CompactFrontier;
                    bool breaksBalanced = hasBalanced && idx == balancedIdx && targetProfile != SystemProfileKind.Balanced;
                    bool breaksRarePlus = hasRarePlus && rarePlusProviders.Count == 1 && idx == rarePlusProviders[0] && targetProfile != SystemProfileKind.RichFrontier;
                    if (breaksCompact || breaksBalanced || breaksRarePlus) continue;
                    target = idx;
                    break;
                }
                if (target < 0) target = nearestThree[0]; // no safe slot — unavoidable overwrite, corrected by a later pass

                ForceProfile(systems, target, targetProfile, targetPlanetCount, seed);
            }
        }

        private static void ForceProfile(List<SystemManifest> systems, int index, SystemProfileKind profile, int planetCount, ulong seed)
        {
            SystemManifest sys = systems[index];
            var composition = new SystemCompositionGenerator.Composition(profile, planetCount);
            List<PlanetManifest> planets = SystemCompositionGenerator.BuildPlanets(seed, sys.SystemId, composition, sys.StarRadius);
            float outermost = planets.Count > 0 ? planets[planets.Count - 1].OrbitRadius : sys.NavigationRadius / 2.5f;
            systems[index] = sys.WithForcedComposition(profile, planets, outermost * 2.5f);
        }

        // --- All five families present across start + nearest three ---------------
        private static void EnsureFamilyCoverage(List<SystemManifest> systems, int startIdx, List<int> nearestThree)
        {
            var present = new HashSet<ResourceFamily>();
            foreach (PlanetManifest p in systems[startIdx].Planets) present.Add(p.Family);
            foreach (int idx in nearestThree) foreach (PlanetManifest p in systems[idx].Planets) present.Add(p.Family);

            var missing = new List<ResourceFamily>();
            foreach (ResourceFamily f in AllFamilies) if (!present.Contains(f)) missing.Add(f);
            if (missing.Count == 0 || nearestThree.Count == 0) return;

            int cursor = 0;
            for (int i = 0; i < nearestThree.Count && cursor < missing.Count; i++)
            {
                int idx = nearestThree[i];
                IReadOnlyList<PlanetManifest> planets = systems[idx].Planets;
                if (planets.Count == 0) continue;
                PlanetManifest last = planets[planets.Count - 1];
                systems[idx] = systems[idx].WithReplacedPlanet(last.WithFrozenResource(missing[cursor], last.Rarity));
                cursor++;
            }
        }

        // --- Distance helpers -------------------------------------------------------
        private static int NearestOtherIndex(List<SystemManifest> systems, int selfIndex)
        {
            int best = -1; float bestSqr = float.MaxValue;
            Vector3 origin = systems[selfIndex].AbsoluteCenter;
            for (int i = 0; i < systems.Count; i++)
            {
                if (i == selfIndex) continue;
                float sqr = (systems[i].AbsoluteCenter - origin).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = i; }
            }
            return best;
        }

        private static List<int> NearestOtherIndices(List<SystemManifest> systems, int selfIndex, int count)
        {
            var candidates = new List<(int index, float sqr)>(systems.Count);
            Vector3 origin = systems[selfIndex].AbsoluteCenter;
            for (int i = 0; i < systems.Count; i++)
            {
                if (i == selfIndex) continue;
                candidates.Add((i, (systems[i].AbsoluteCenter - origin).sqrMagnitude));
            }
            candidates.Sort((a, b) => a.sqr.CompareTo(b.sqr));
            var result = new List<int>(count);
            for (int i = 0; i < candidates.Count && i < count; i++) result.Add(candidates[i].index);
            return result;
        }
    }
}
