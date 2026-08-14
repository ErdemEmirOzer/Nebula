using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for star-class/environment generation through the real
    /// SectorManifestGenerator pipeline (VS-009 §10.1/§10.3): distribution
    /// across many systems/planets, and independence from Family/Rarity.
    /// </summary>
    public class StarClassAndEnvironmentGenerationTests
    {
        /// <summary>
        /// How many deterministic seeds one sample spans.
        /// <para>
        /// These tests used to sweep 400 invented sector coordinates -- <c>(seed, 0, 1)</c>,
        /// <c>(seed, 1, 0)</c>, <c>(seed, 2, 0)</c> -- on the assumption that any sector yields
        /// 4-7 systems (<c>SectorManifestGenerator.ChooseCells</c>). That assumption is dead:
        /// ChooseCells has no call sites, and <see cref="SectorManifestGenerator.Generate"/>
        /// fills a sector from <see cref="GalaxyStarField"/> instead. The field is finite and
        /// sparse -- 4 galaxies x 4 arms x ~6-10 stars, 128 stars in total -- strung along
        /// spiral arms that span millions of units on every axis, so those 400 straight-line
        /// coordinates are almost all empty space and the sweep collected a handful of systems,
        /// far under the 1000 the distribution assertions need ("sample too small").
        /// </para>
        /// <para>
        /// One galaxy is only 128 systems, so no single seed can prove a 5%-weight bucket to
        /// +/-0.02. The sample therefore spans <see cref="SampleSeeds"/> whole galaxies, which
        /// is 5,120 systems and ~26,700 planets -- large enough that the tightest assertion
        /// here (BlueGiant at 0.05 +/- 0.02) sits several standard errors from its bound rather
        /// than passing by luck. No threshold was loosened to accommodate the new sample.
        /// </para>
        /// </summary>
        private const int SampleSeeds = 40;

        /// <summary>Floors, not targets: they exist so a shrinking galaxy fails loudly instead of quietly making the distribution assertions meaningless. The real sample is comfortably larger.</summary>
        private const int MinSystems = 4000;
        private const int MinPlanets = 20000;
        private const int MinMetalPlanets = 3000;

        /// <summary>
        /// The sectors this seed's galaxy actually places stars in, in a fixed order so the
        /// sample is identical on every run and every platform. Reading the star field is what
        /// bounds the walk -- an arbitrary coordinate sweep cannot tell an empty sector from an
        /// unreached one, and most sectors in this universe are empty.
        /// </summary>
        private static List<SectorCoordinate> OccupiedSectors(ulong seed)
        {
            var seen = new HashSet<SectorCoordinate>();
            var occupied = new List<SectorCoordinate>();
            foreach (GalaxyStarField.Placement placement in GalaxyStarField.All(seed))
            {
                SectorCoordinate sector = SectorCoordinate.FromAbsolutePosition(placement.AbsolutePosition);
                if (seen.Add(sector)) occupied.Add(sector);
            }
            occupied.Sort((a, b) =>
            {
                if (a.X != b.X) return a.X.CompareTo(b.X);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            });
            return occupied;
        }

        /// <summary>
        /// Every real system across <see cref="SampleSeeds"/> whole galaxies.
        /// <para>
        /// Deliberately generated with <c>isStartSector: false</c> throughout: the start sector
        /// runs through <see cref="FrontierGuaranteePass"/>, which forces the start system to
        /// Yellow and rewrites its neighbours' star classes and environments on purpose. Those
        /// are guarantees, not draws, and folding them in would measure the guarantee rather
        /// than the generator's own weighted distribution -- which is what these tests exist for.
        /// </para>
        /// </summary>
        private static List<SystemManifest> SampleSystems()
        {
            var systems = new List<SystemManifest>();
            for (ulong seed = 0; seed < (ulong)SampleSeeds; seed++)
                foreach (SectorCoordinate sector in OccupiedSectors(seed))
                    foreach (SystemManifest system in SectorManifestGenerator.Generate(seed, sector, isStartSector: false).Systems)
                        systems.Add(system);

            Assert.GreaterOrEqual(systems.Count, MinSystems,
                $"the galaxies for seeds 0..{SampleSeeds - 1} only yield {systems.Count} systems; " +
                "these distribution tests need a genuinely large sample");
            return systems;
        }

        [Test]
        public void StarClass_AcrossManySystems_MatchesFortyThirtyFiveTwentyFiveWeights()
        {
            var counts = new Dictionary<StarClass, int>();
            int total = 0;
            foreach (SystemManifest s in SampleSystems())
            {
                counts[s.StarClass] = counts.TryGetValue(s.StarClass, out int c) ? c + 1 : 1;
                total++;
            }

            Assert.Greater(total, 1000, "sample too small to validate distribution");
            Assert.AreEqual(0.40f, counts.GetValueOrDefault(StarClass.RedDwarf) / (float)total, 0.03f, "RedDwarf");
            Assert.AreEqual(0.35f, counts.GetValueOrDefault(StarClass.Yellow) / (float)total, 0.03f, "Yellow");
            Assert.AreEqual(0.20f, counts.GetValueOrDefault(StarClass.White) / (float)total, 0.03f, "White");
            Assert.AreEqual(0.05f, counts.GetValueOrDefault(StarClass.BlueGiant) / (float)total, 0.02f, "BlueGiant");
        }

        [Test]
        public void EnvironmentClass_AcrossManyPlanets_MatchesSixtyThirtyTenWeights()
        {
            var counts = new Dictionary<EnvironmentClass, int>();
            int total = 0;
            foreach (SystemManifest s in SampleSystems())
                foreach (PlanetManifest p in s.Planets)
                {
                    counts[p.Environment] = counts.TryGetValue(p.Environment, out int c) ? c + 1 : 1;
                    total++;
                }

            Assert.GreaterOrEqual(total, MinPlanets, "sample too small to validate distribution");
            Assert.AreEqual(0.60f, counts.GetValueOrDefault(EnvironmentClass.Calm) / (float)total, 0.03f, "Calm");
            Assert.AreEqual(0.30f, counts.GetValueOrDefault(EnvironmentClass.Harsh) / (float)total, 0.03f, "Harsh");
            Assert.AreEqual(0.10f, counts.GetValueOrDefault(EnvironmentClass.Extreme) / (float)total, 0.02f, "Extreme");
        }

        [Test]
        public void Environment_IsIndependentOfFamilyAndRarity()
        {
            // For a fixed ResourceFamily, the Environment distribution across
            // many matching planets must still land near 60/30/10 — proving
            // it isn't coupled to (or skewed by) the family hash channel.
            var counts = new Dictionary<EnvironmentClass, int>();
            int total = 0;
            foreach (SystemManifest s in SampleSystems())
                foreach (PlanetManifest p in s.Planets)
                {
                    if (p.Family != ResourceFamily.Metal) continue;
                    counts[p.Environment] = counts.TryGetValue(p.Environment, out int c) ? c + 1 : 1;
                    total++;
                }

            Assert.GreaterOrEqual(total, MinMetalPlanets, "sample too small (Metal-only subset)");
            Assert.AreEqual(0.60f, counts.GetValueOrDefault(EnvironmentClass.Calm) / (float)total, 0.08f, "Calm|Metal");
            // The whole point is independence, so the other two buckets are held to the same
            // 60/30/10 shape as the unfiltered population -- a family-coupled Environment would
            // have to show up somewhere, and Calm alone cannot see a Harsh/Extreme swap.
            Assert.AreEqual(0.30f, counts.GetValueOrDefault(EnvironmentClass.Harsh) / (float)total, 0.08f, "Harsh|Metal");
            Assert.AreEqual(0.10f, counts.GetValueOrDefault(EnvironmentClass.Extreme) / (float)total, 0.05f, "Extreme|Metal");
        }

        [Test]
        public void SystemManifest_GenerationVersionMatchesTheShippedGenerationVersion()
        {
            // Old contract: GenerationVersion == 2, asserted as a bare literal.
            // Current production authority: SectorManifestGenerator.GenerationVersion == 3
            // (SectorManifestGenerator.cs:24) -- bumped when sector content moved off the
            // uniform sub-cell grid onto GalaxyStarField's spiral arms, which is exactly the
            // kind of change the version exists to invalidate caches/saves for.
            // The literal is pinned deliberately rather than compared to the constant alone:
            // a test that only says "the constant equals itself" cannot notice an accidental
            // bump, and the invariant being protected is that the number does not move without
            // somebody deciding it should. The production constant is NOT changed here.
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion, "the shipped generation version");

            // ...and every manifest the generator hands out must actually carry it, sector and
            // system alike -- the version is worthless if the stamped value can drift from the
            // constant that is supposed to define it.
            var sector = new SectorCoordinate(0, 0, 0);
            SectorManifest manifest = SectorManifestGenerator.Generate(0UL, sector, isStartSector: true);
            Assert.AreEqual(SectorManifestGenerator.GenerationVersion, manifest.GenerationVersion, "sector manifest");
            Assert.GreaterOrEqual(manifest.Systems.Count, 1, "the campaign start sector always holds at least the start system");
            foreach (SystemManifest system in manifest.Systems)
                Assert.AreEqual(SectorManifestGenerator.GenerationVersion, system.GenerationVersion, system.SystemId);
        }
    }
}
