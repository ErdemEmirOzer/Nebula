using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Sectors
{
    /// <summary>
    /// Pure, deterministic system composition (VS-008 §2): planet count,
    /// generation profile, per-planet single resource family, rarity and
    /// orbit geometry — all derived from independent hash keys (never
    /// UnityEngine.Random, never a sequential stream), so a materializer
    /// never needs to replay call order to get the same result.
    /// </summary>
    public static class SystemCompositionGenerator
    {
        // (profile, planetCount, weight) — see VS-008 §2. Weights sum to 100
        // and independently reproduce both the 3..8 planet-count distribution
        // and the 4-way profile distribution when sampled together.
        private static readonly (SystemProfileKind profile, int count, float weight)[] JointTable =
        {
            (SystemProfileKind.CompactFrontier, 3, 10f), (SystemProfileKind.CompactFrontier, 4, 15f),
            (SystemProfileKind.Balanced, 4, 5f), (SystemProfileKind.Balanced, 5, 25f), (SystemProfileKind.Balanced, 6, 10f),
            (SystemProfileKind.ResourceCluster, 5, 5f), (SystemProfileKind.ResourceCluster, 6, 13f), (SystemProfileKind.ResourceCluster, 7, 7f),
            (SystemProfileKind.RichFrontier, 6, 2f), (SystemProfileKind.RichFrontier, 7, 3f), (SystemProfileKind.RichFrontier, 8, 5f),
        };

        private const float ClusterDominantMin = 0.35f;
        private const float ClusterDominantMax = 0.50f;

        public readonly struct Composition
        {
            public readonly SystemProfileKind Profile;
            public readonly int PlanetCount;
            public Composition(SystemProfileKind profile, int planetCount) { Profile = profile; PlanetCount = planetCount; }
        }

        /// <summary>Picks (profile, planetCount) for one system from the joint table.</summary>
        public static Composition PickComposition(ulong seed, string systemId)
        {
            float roll = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, systemId + "/composition")) * 100f;
            float cumulative = 0f;
            foreach (var entry in JointTable)
            {
                cumulative += entry.weight;
                if (roll < cumulative) return new Composition(entry.profile, entry.count);
            }
            var last = JointTable[JointTable.Length - 1];
            return new Composition(last.profile, last.count); // float rounding guard
        }

        /// <summary>Force a specific composition — used by FrontierGuaranteePass for the start system (exact 5-planet Balanced) without touching the hash-driven path for anything else.</summary>
        public static Composition ForcedBalanced(int planetCount) => new Composition(SystemProfileKind.Balanced, planetCount);

        /// <summary>Builds the full planet list for a system, given its already-decided composition.</summary>
        public static List<PlanetManifest> BuildPlanets(ulong seed, string systemId, Composition composition, float starRadius)
        {
            var families = AssignFamilies(seed, systemId, composition);
            var planets = new List<PlanetManifest>(composition.PlanetCount);

            float firstOrbit = starRadius * LerpUnit(ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, systemId + "/firstorbit")), 4.5f, 5.5f);
            float orbit = firstOrbit;

            for (int i = 0; i < composition.PlanetCount; i++)
            {
                string planetId = WorldIdCodec.PlanetId(systemId, i);
                ulong pHash(string tag) => ExtractionCatalog.Hash(seed, planetId + "/" + tag);
                float pUnit(string tag) => ExtractionCatalog.Unit01(pHash(tag));

                Rarity rarity = ExtractionCatalog.RarityFromUnit(pUnit("rarity"));

                float planetRadius = LerpUnit(pUnit("radius"), 800f, 2600f);
                float inclination = LerpUnit(pUnit("inclination"), -10f, 10f);
                float ascendingNode = LerpUnit(pUnit("node"), 0f, 360f);
                float startAngle = LerpUnit(pUnit("angle"), 0f, 360f);

                // VS-009 §3: environment is its own hash channel, fully
                // independent of family/rarity above. SurfaceLayoutSeed is
                // the sole (seed-derived) input for this planet's 12 node
                // directions — never re-derived from family/rarity/environment.
                EnvironmentClass environment = EnvironmentClassCatalog.Pick(seed, planetId);
                ulong surfaceLayoutSeed = pHash("layoutseed");

                planets.Add(new PlanetManifest(i, planetId, families[i], rarity, orbit, inclination, ascendingNode,
                    startAngle, planetRadius, recommendedStart: false, environment, surfaceLayoutSeed));

                orbit *= LerpUnit(pUnit("spacing"), 1.18f, 1.3f);
            }

            if (composition.Profile == SystemProfileKind.RichFrontier) planets = EnsureRichFrontierHasRarePlus(planets);
            return planets;
        }

        private static ResourceFamily[] AssignFamilies(ulong seed, string systemId, Composition composition)
        {
            var families = new ResourceFamily[composition.PlanetCount];
            const int familyCount = 5; // Metal/Wood/Gas/Water/Diamond

            if (composition.Profile != SystemProfileKind.ResourceCluster)
            {
                for (int i = 0; i < families.Length; i++)
                {
                    string planetId = WorldIdCodec.PlanetId(systemId, i);
                    float unit = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, planetId + "/family"));
                    families[i] = (ResourceFamily)Mathf.Clamp(Mathf.FloorToInt(unit * familyCount), 0, familyCount - 1);
                }
                return families;
            }

            // Resource Cluster: one dominant family covers 35-50% of the planets (VS-008 §2); the
            // rest are deterministically spread across the other four families, planet by planet.
            var dominant = (ResourceFamily)Mathf.Clamp(
                Mathf.FloorToInt(ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, systemId + "/dominant")) * familyCount), 0, familyCount - 1);
            float fraction = LerpUnit(ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, systemId + "/dominantfraction")), ClusterDominantMin, ClusterDominantMax);
            // Round-to-nearest can push the ACHIEVED fraction (dominantCount /
            // planetCount) outside [0.35, 0.50] when planetCount doesn't divide
            // evenly (e.g. 2/6 = 0.333) — clamp to the count range that keeps
            // the achieved fraction itself inside the band, not just the raw multiply.
            int loCount = Mathf.CeilToInt(composition.PlanetCount * ClusterDominantMin);
            int hiCount = Mathf.FloorToInt(composition.PlanetCount * ClusterDominantMax);
            if (hiCount < loCount) hiCount = loCount;
            int dominantCount = Mathf.Clamp(Mathf.RoundToInt(composition.PlanetCount * fraction), loCount, hiCount);
            dominantCount = Mathf.Clamp(dominantCount, 1, composition.PlanetCount - 1);
            // At least (and at most) one non-dominant planet must exist for a Cluster system with >=2
            // planets; the 5-planet minimum for this profile guarantees room for both.

            var dominantSlots = new bool[composition.PlanetCount];
            // Deterministic slot selection: hash-rank every index, take the lowest `dominantCount` ranks.
            var ranked = new List<(int index, float rank)>(composition.PlanetCount);
            for (int i = 0; i < composition.PlanetCount; i++)
                ranked.Add((i, ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, WorldIdCodec.PlanetId(systemId, i) + "/clusterslot"))));
            ranked.Sort((a, b) => a.rank.CompareTo(b.rank));
            for (int i = 0; i < dominantCount; i++) dominantSlots[ranked[i].index] = true;

            for (int i = 0; i < families.Length; i++)
            {
                if (dominantSlots[i]) { families[i] = dominant; continue; }
                string planetId = WorldIdCodec.PlanetId(systemId, i);
                float unit = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, planetId + "/nondominant"));
                int offset = 1 + Mathf.Clamp(Mathf.FloorToInt(unit * (familyCount - 1)), 0, familyCount - 2); // 1..4
                families[i] = (ResourceFamily)(((int)dominant + offset) % familyCount);
            }
            return families;
        }

        private static List<PlanetManifest> EnsureRichFrontierHasRarePlus(List<PlanetManifest> planets)
        {
            for (int i = 0; i < planets.Count; i++) if (planets[i].Rarity >= Rarity.Rare) return planets;
            // Deterministic bump: the last planet (stable choice, no extra hash draw) becomes Rare.
            int lastIndex = planets.Count - 1;
            PlanetManifest p = planets[lastIndex];
            planets[lastIndex] = new PlanetManifest(p.PlanetIndex, p.PlanetId, p.Family, Rarity.Rare, p.OrbitRadius,
                p.OrbitInclinationDegrees, p.AscendingNodeDegrees, p.StartAngleDegrees, p.PlanetRadius, p.RecommendedStart,
                p.Environment, p.SurfaceLayoutSeed);
            return planets;
        }

        private static float LerpUnit(float unit01, float min, float max) => min + Mathf.Clamp01(unit01) * (max - min);
    }
}
