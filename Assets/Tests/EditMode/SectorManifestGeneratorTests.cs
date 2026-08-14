using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for deterministic sector placement (VS-008 §1/§10).</summary>
    public class SectorManifestGeneratorTests
    {

        /// <summary>
        /// The sectors the galaxies' arms actually pass through. Systems live on the arms
        /// now, so most of the universe is empty space between them — a test that samples
        /// arbitrary sector coordinates finds nothing and proves nothing.
        /// </summary>
        private static List<SectorCoordinate> PopulatedSectors(ulong seed, int max)
        {
            var found = new List<SectorCoordinate>();
            foreach (GalaxyStarField.Placement p in GalaxyStarField.All(seed))
            {
                SectorCoordinate sector = SectorCoordinate.FromAbsolutePosition(p.AbsolutePosition);
                if (found.Contains(sector)) continue;
                found.Add(sector);
                if (found.Count >= max) break;
            }
            return found;
        }

        [Test]
        public void Generate_SameSeedVersionCoordinate_ProducesIdenticalManifest()
        {
            var sector = new SectorCoordinate(4, -2, 9);
            SectorManifest a = SectorManifestGenerator.Generate(555UL, sector, isStartSector: false);
            SectorManifest b = SectorManifestGenerator.Generate(555UL, sector, isStartSector: false);

            Assert.AreEqual(a.Systems.Count, b.Systems.Count);
            for (int i = 0; i < a.Systems.Count; i++)
            {
                Assert.AreEqual(a.Systems[i].SystemId, b.Systems[i].SystemId);
                Assert.AreEqual(a.Systems[i].DisplayName, b.Systems[i].DisplayName);
                Assert.AreEqual(a.Systems[i].AbsoluteCenter, b.Systems[i].AbsoluteCenter);
                Assert.AreEqual(a.Systems[i].Planets.Count, b.Systems[i].Planets.Count);
                for (int p = 0; p < a.Systems[i].Planets.Count; p++)
                {
                    Assert.AreEqual(a.Systems[i].Planets[p].Family, b.Systems[i].Planets[p].Family);
                    Assert.AreEqual(a.Systems[i].Planets[p].Rarity, b.Systems[i].Planets[p].Rarity);
                    Assert.AreEqual(a.Systems[i].Planets[p].OrbitRadius, b.Systems[i].Planets[p].OrbitRadius);
                }
            }
        }

        [Test]
        public void Generate_DifferentSeed_UsuallyDiffers()
        {
            // A sector both seeds actually populate -- the arms move with the seed, so an
            // arbitrary coordinate may be empty in one field and not the other.
            SectorCoordinate sector = PopulatedSectors(1UL, 1)[0];
            SectorManifest a = SectorManifestGenerator.Generate(1UL, sector, false);
            SectorManifest b = SectorManifestGenerator.Generate(2UL, sector, false);

            bool differs = a.Systems.Count != b.Systems.Count;
            if (!differs)
                for (int i = 0; i < a.Systems.Count && !differs; i++)
                    differs = a.Systems[i].AbsoluteCenter != b.Systems[i].AbsoluteCenter;
            Assert.IsTrue(differs, "two seeds must not produce the same sector");
        }

        [Test]
        public void Generate_PopulatesOnlyTheSectorsTheArmsPassThrough()
        {
            const ulong Seed = 4242UL;
            List<SectorCoordinate> populated = PopulatedSectors(Seed, 12);
            Assert.Greater(populated.Count, 0, "the arms must pass through some sectors");

            foreach (SectorCoordinate sector in populated)
                Assert.Greater(SectorManifestGenerator.Generate(Seed, sector, false).Systems.Count, 0,
                    $"{sector} holds an arm star and must hold a system");

            // Far outside every galaxy there is nothing at all, which is what the space
            // between the arms should look like.
            var empty = new SectorCoordinate(400, 400, 400);
            Assert.AreEqual(0, SectorManifestGenerator.Generate(Seed, empty, false).Systems.Count,
                "a sector no arm passes through must hold no systems");
        }

        [Test]
        public void Generate_CanonicalIdsMatchWorldIdCodec()
        {
            var sector = new SectorCoordinate(-1, 2, -3);
            SectorManifest m = SectorManifestGenerator.Generate(99UL, sector, false);
            for (int i = 0; i < m.Systems.Count; i++)
            {
                Assert.AreEqual(WorldIdCodec.SystemId(sector, i), m.Systems[i].SystemId);
                foreach (PlanetManifest p in m.Systems[i].Planets)
                    Assert.AreEqual(WorldIdCodec.PlanetId(m.Systems[i].SystemId, p.PlanetIndex), p.PlanetId);
            }
        }

        [Test]
        public void Generate_MinimumSeparationHoldsWithinASector()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var sector = new SectorCoordinate(0, 0, seed);
                SectorManifest m = SectorManifestGenerator.Generate((ulong)seed, sector, false);
                for (int i = 0; i < m.Systems.Count; i++)
                    for (int j = i + 1; j < m.Systems.Count; j++)
                    {
                        float d = Vector3.Distance(m.Systems[i].AbsoluteCenter, m.Systems[j].AbsoluteCenter);
                        Assert.GreaterOrEqual(d, SectorManifestGenerator.RequiredSeparation - 1f, $"seed {seed} systems {i}/{j}");
                    }
            }
        }

        [Test]
        public void Generate_MinimumSeparationHoldsAcrossASectorBoundary()
        {
            // The cell-grid design's core guarantee: a system at the far edge
            // of one sector and one at the near edge of its neighbor are
            // still >= the required separation apart, without ever having to
            // jointly resolve both sectors during generation.
            for (int seed = 0; seed < 20; seed++)
            {
                var a = new SectorCoordinate(seed, 0, 0);
                var b = new SectorCoordinate(seed + 1, 0, 0);
                SectorManifest ma = SectorManifestGenerator.Generate(777UL, a, false);
                SectorManifest mb = SectorManifestGenerator.Generate(777UL, b, false);

                foreach (SystemManifest sa in ma.Systems)
                    foreach (SystemManifest sb in mb.Systems)
                    {
                        float d = Vector3.Distance(sa.AbsoluteCenter, sb.AbsoluteCenter);
                        Assert.GreaterOrEqual(d, SectorManifestGenerator.RequiredSeparation - 1f);
                    }
            }
        }

        [Test]
        public void Generate_StartSector_FirstSystemIsStartAndBalancedFivePlanets()
        {
            var sector = new SectorCoordinate(0, 0, 0);
            SectorManifest m = SectorManifestGenerator.Generate(12345UL, sector, isStartSector: true);
            SystemManifest start = m.Systems[0];
            Assert.IsTrue(start.IsStartSystem);
            Assert.AreEqual(5, start.Planets.Count);
        }

        [Test]
        public void Generate_ProceduralSector_HasNoStartSystem()
        {
            var sector = new SectorCoordinate(3, 3, 3);
            SectorManifest m = SectorManifestGenerator.Generate(1UL, sector, isStartSector: false);
            foreach (SystemManifest s in m.Systems) Assert.IsFalse(s.IsStartSystem);
        }

        [Test]
        public void PlanetManifest_CarriesExactlyOneResourceFamily_NeverAList()
        {
            var sector = new SectorCoordinate(9, 9, 9);
            SectorManifest m = SectorManifestGenerator.Generate(42UL, sector, false);
            foreach (SystemManifest s in m.Systems)
                foreach (PlanetManifest p in s.Planets)
                {
                    // Family is a single enum-typed property — this is a
                    // compile-time guarantee, but assert its value is always
                    // one of the five valid families (never some sentinel
                    // "multi" value) as the runtime half of that contract.
                    Assert.IsTrue(p.Family == Extraction.ResourceFamily.Metal || p.Family == Extraction.ResourceFamily.Wood
                        || p.Family == Extraction.ResourceFamily.Gas || p.Family == Extraction.ResourceFamily.Water
                        || p.Family == Extraction.ResourceFamily.Diamond);
                }
        }

        [Test]
        public void ResourceCluster_DominantFamilyCoversThirtyFiveToFiftyPercent()
        {
            bool foundCluster = false;
            for (int seed = 0; seed < 200 && !foundCluster; seed++)
            {
                var sector = new SectorCoordinate(seed, -seed, 0);
                SectorManifest m = SectorManifestGenerator.Generate((ulong)seed, sector, false);
                foreach (SystemManifest s in m.Systems)
                {
                    if (s.ProfileKind != SystemProfileKind.ResourceCluster) continue;
                    foundCluster = true;

                    var counts = new Dictionary<Extraction.ResourceFamily, int>();
                    foreach (PlanetManifest p in s.Planets) counts[p.Family] = counts.TryGetValue(p.Family, out int c) ? c + 1 : 1;
                    int dominantCount = 0;
                    foreach (var kv in counts) if (kv.Value > dominantCount) dominantCount = kv.Value;
                    float fraction = (float)dominantCount / s.Planets.Count;

                    Assert.GreaterOrEqual(fraction, 0.34f, s.SystemId); // small float-rounding tolerance
                    Assert.LessOrEqual(fraction, 0.51f, s.SystemId);
                    break;
                }
            }
            Assert.IsTrue(foundCluster, "sample must contain at least one Resource Cluster system to validate");
        }

        [Test]
        public void RichFrontier_AlwaysHasAtLeastOneRarePlusPlanet()
        {
            bool foundRich = false;
            for (int seed = 0; seed < 40; seed++)
            foreach (SectorCoordinate sector in PopulatedSectors((ulong)seed, 40))
            {
                SectorManifest m = SectorManifestGenerator.Generate((ulong)seed, sector, false);
                foreach (SystemManifest s in m.Systems)
                {
                    if (s.ProfileKind != SystemProfileKind.RichFrontier) continue;
                    foundRich = true;
                    bool hasRarePlus = false;
                    foreach (PlanetManifest p in s.Planets) if (p.Rarity >= Extraction.Rarity.Rare) hasRarePlus = true;
                    Assert.IsTrue(hasRarePlus, s.SystemId);
                    Assert.GreaterOrEqual(s.Planets.Count, 6, s.SystemId);
                }
            }
            Assert.IsTrue(foundRich, "sample must contain at least one Rich Frontier system to validate");
        }

        [Test]
        public void PlanetCountDistribution_AcrossManySystems_MatchesRoughTargets()
        {
            var counts = new Dictionary<int, int>();
            int total = 0;
            for (int seed = 0; seed < 40; seed++)
            foreach (SectorCoordinate sector in PopulatedSectors((ulong)seed * 7919UL, 40))
            {
                SectorManifest m = SectorManifestGenerator.Generate((ulong)seed * 7919UL, sector, false);
                foreach (SystemManifest s in m.Systems)
                {
                    counts[s.Planets.Count] = counts.TryGetValue(s.Planets.Count, out int c) ? c + 1 : 1;
                    total++;
                }
            }
            // Every count in 3..8 must appear at least once in a large enough sample.
            for (int n = 3; n <= 8; n++) Assert.IsTrue(counts.ContainsKey(n), $"planet count {n} never appeared in {total} samples");
        }
    }
}
