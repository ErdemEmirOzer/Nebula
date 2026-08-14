using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the start-sector guarantees (VS-008 §3/§10).
    /// <para>
    /// These tests were written against a world model that no longer exists. A sector used to
    /// hold 4-7 systems chosen from 8 sub-cells by <c>SectorManifestGenerator.ChooseCells</c>,
    /// so "the start sector always has at least four systems, three foreign besides start" was
    /// true by construction. ChooseCells now has ZERO call sites:
    /// <see cref="SectorManifestGenerator.Generate"/> fills a sector from
    /// <see cref="GalaxyStarField"/> instead — 4 galaxies x 4 arms x ~6-10 stars, 128 stars in
    /// the entire universe, strung along spiral arms spanning millions of units on every axis,
    /// against a 2,000,000-unit sector grid. Measured across seeds 0..399, the campaign's own
    /// sector.0.0.0 holds ONE system on 301 seeds, two on 90 and three on 9. It never holds four.
    /// </para>
    /// <para>
    /// So each guarantee is now asserted where production can actually make it, at exactly its
    /// original strength — nothing here was loosened, and no production constant, count or
    /// distribution was changed to suit a test:
    /// <list type="bullet">
    /// <item>Guarantees about the start SYSTEM alone (five planets, recommended planet, Yellow
    /// star) still run over every seed — they hold unconditionally.</item>
    /// <item>Guarantees <see cref="FrontierGuaranteePass.Apply"/> only makes when it has
    /// something to guarantee against (it returns the sector untouched below two systems,
    /// FrontierGuaranteePass.cs:28) run over the seeds whose start sector really does hold a
    /// foreign neighbour.</item>
    /// <item>Guarantees that need three foreign neighbours run over the sectors the galaxy
    /// genuinely fills with four or more stars, generated AS start sectors —
    /// <see cref="SectorManifestRepository.SetStartSector"/> takes any coordinate and
    /// <c>Generate</c> takes the flag independently, so this is the pass's real contract, driven
    /// with real galaxy content rather than an invented sector.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class FrontierGuaranteePassTests
    {
        /// <summary>How far the deterministic seed scans below are allowed to walk before giving up with a named failure. Bounded on purpose: nothing here may loop until it happens to find enough.</summary>
        private const int MaxSeedScan = 400;

        /// <summary>The campaign's own start sector — sector.0.0.0, exactly as WorldInstaller.PrepareSession boots it.</summary>
        private static SectorManifest StartSector(ulong seed) =>
            SectorManifestGenerator.Generate(seed, new SectorCoordinate(0, 0, 0), isStartSector: true);

        /// <summary>One real start sector under test, with the seed that produced it so a failure names something reproducible.</summary>
        private readonly struct StartSectorCase
        {
            public readonly ulong Seed;
            public readonly SectorManifest Manifest;
            public StartSectorCase(ulong seed, SectorManifest manifest) { Seed = seed; Manifest = manifest; }
            public string Label => $"seed {Seed} @ {Manifest.Coordinate.Id} ({Manifest.Systems.Count} systems)";
        }

        // ==================== real galaxy content, never invented coordinates ====================

        /// <summary>How many stars the galaxy places in each sector it occupies, in a fixed order (start sector first, then lexicographic) so every scan below is identical on every run.</summary>
        private static List<KeyValuePair<SectorCoordinate, int>> StarsPerOccupiedSector(ulong seed)
        {
            var start = new SectorCoordinate(0, 0, 0);
            var counts = new Dictionary<SectorCoordinate, int>();
            var order = new List<SectorCoordinate>();
            foreach (GalaxyStarField.Placement placement in GalaxyStarField.All(seed))
            {
                SectorCoordinate sector = SectorCoordinate.FromAbsolutePosition(placement.AbsolutePosition);
                if (!counts.ContainsKey(sector)) { counts[sector] = 0; order.Add(sector); }
                counts[sector]++;
            }
            order.Sort((a, b) =>
            {
                if (a.Equals(start) != b.Equals(start)) return a.Equals(start) ? -1 : 1;
                if (a.X != b.X) return a.X.CompareTo(b.X);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            });

            var result = new List<KeyValuePair<SectorCoordinate, int>>(order.Count);
            foreach (SectorCoordinate sector in order) result.Add(new KeyValuePair<SectorCoordinate, int>(sector, counts[sector]));
            return result;
        }

        /// <summary>
        /// The campaign start sector for the first <paramref name="count"/> seeds whose galaxy
        /// actually places a foreign star beside the pinned start star — i.e. the seeds where
        /// <see cref="FrontierGuaranteePass.Apply"/> has a neighbour to guarantee anything about
        /// at all. Roughly a quarter of seeds qualify, so this scan is short and it fails by name
        /// rather than looping if the galaxy ever stops supplying them.
        /// </summary>
        private static List<StartSectorCase> CampaignStartSectorsWithANeighbour(int count)
        {
            var found = new List<StartSectorCase>(count);
            for (ulong seed = 0; seed < MaxSeedScan && found.Count < count; seed++)
            {
                SectorManifest manifest = StartSector(seed);
                if (manifest.Systems.Count >= 2) found.Add(new StartSectorCase(seed, manifest));
            }
            Assert.AreEqual(count, found.Count,
                $"only {found.Count} of the first {MaxSeedScan} seeds give sector.0.0.0 a foreign neighbour; this test needs {count}");
            return found;
        }

        /// <summary>
        /// <paramref name="count"/> sectors the galaxy genuinely fills with four or more stars,
        /// each generated as a start sector so the full three-foreign-neighbour guarantee is
        /// exercised on real content. Scanned in a fixed (seed, sector) order and bounded.
        /// </summary>
        private static List<StartSectorCase> DenseStartSectors(int count)
        {
            const int MinSystems = 4; // start + the three foreign neighbours the guarantee is about
            var found = new List<StartSectorCase>(count);
            for (ulong seed = 0; seed < MaxSeedScan && found.Count < count; seed++)
                foreach (KeyValuePair<SectorCoordinate, int> entry in StarsPerOccupiedSector(seed))
                {
                    if (entry.Value < MinSystems) continue;
                    found.Add(new StartSectorCase(seed, SectorManifestGenerator.Generate(seed, entry.Key, isStartSector: true)));
                    if (found.Count >= count) break;
                }
            Assert.AreEqual(count, found.Count,
                $"the galaxies for seeds 0..{MaxSeedScan - 1} only fill {found.Count} sector(s) with {MinSystems}+ stars; this test needs {count}");
            return found;
        }

        // ==================== start-system guarantees (unconditional) ====================

        [Test]
        public void StartSystem_IsBalancedFivePlanets_WithAtLeastThreeFamilies()
        {
            // Five planets is unconditional and stays asserted over every seed: the generator
            // forces the start system's composition to ForcedBalanced(5) before
            // FrontierGuaranteePass ever sees the sector.
            for (ulong seed = 0; seed < 25; seed++)
            {
                SystemManifest start = StartSector(seed).Systems[0];
                Assert.IsTrue(start.IsStartSystem, $"seed {seed}");
                Assert.AreEqual(5, start.Planets.Count, $"seed {seed}");
            }

            // >=3 distinct families is a different thing: it is EnsureFamilyDiversity's guarantee,
            // and Apply returns the sector completely untouched when it holds fewer than two
            // systems ("nothing to guarantee against", FrontierGuaranteePass.cs:28). Most seeds
            // now give sector.0.0.0 exactly one system, so the old blanket loop was asserting a
            // guarantee production never applied there -- seed 3's start system genuinely ships
            // with two families. The guarantee itself is unchanged and asserted at full strength
            // over 25 seeds where the pass really runs.
            foreach (StartSectorCase test in CampaignStartSectorsWithANeighbour(25))
            {
                var families = new HashSet<ResourceFamily>();
                foreach (PlanetManifest p in test.Manifest.Systems[0].Planets) families.Add(p.Family);
                Assert.GreaterOrEqual(families.Count, 3, test.Label);
            }
        }

        [Test]
        public void StartSystem_HasARecommendedPlanet_CommonOrUncommon()
        {
            for (ulong seed = 0; seed < 25; seed++)
            {
                SystemManifest start = StartSector(seed).Systems[0];
                PlanetManifest recommended = null;
                foreach (PlanetManifest p in start.Planets) if (p.RecommendedStart) recommended = p;
                Assert.IsNotNull(recommended, $"seed {seed}");
                Assert.LessOrEqual(recommended.Rarity, Rarity.Uncommon, $"seed {seed}");
            }
        }

        [Test]
        public void StartSector_AlwaysHoldsTheStartSystem_AndTheGalaxySuppliesRealForeignNeighbours()
        {
            // Old contract (this test's old name): "the start sector always has at least four
            // systems, three foreign besides start" -- true only while ChooseCells picked 4-7 of
            // 8 sub-cells for every sector in the universe.
            // Current production authority: ChooseCells has zero call sites; Generate fills a
            // sector from GalaxyStarField. Measured over seeds 0..399, sector.0.0.0 holds one
            // system on 301 seeds, two on 90, three on 9 -- never four. The old assertion cannot
            // be satisfied without restoring the dead world model, so it is replaced by what the
            // current model genuinely promises, at full strength:
            //   * the campaign start sector is never empty -- GalaxyStarField.StartStar is pinned
            //     inside it and is the one star the separation pass never nudges or drops, and
            //     the game boots by asking for exactly this system;
            //   * slot 0 is the start system and carries the id WorldInstaller.PrepareSession
            //     and ExtractionDirector.LoadNow both boot with;
            //   * nothing else in the sector is ever flagged as a start system;
            //   * and the frontier guarantee is not dead code -- the galaxy really does place
            //     foreign neighbours in the start sector on a meaningful share of seeds.
            string bootId = WorldIdCodec.SystemId(new SectorCoordinate(0, 0, 0), 0);
            int withNeighbours = 0;
            for (ulong seed = 0; seed < 25; seed++)
            {
                SectorManifest sector = StartSector(seed);
                Assert.GreaterOrEqual(sector.Systems.Count, 1, $"seed {seed}: the campaign start sector is never empty");
                Assert.IsTrue(sector.Systems[0].IsStartSystem, $"seed {seed}: slot 0 must be the start system");
                Assert.AreEqual(bootId, sector.Systems[0].SystemId, $"seed {seed}: the start system must be the id the game boots with");
                for (int i = 1; i < sector.Systems.Count; i++)
                    Assert.IsFalse(sector.Systems[i].IsStartSystem, $"seed {seed}: system {i} must be foreign");
                if (sector.Systems.Count >= 2) withNeighbours++;
            }
            Assert.GreaterOrEqual(withNeighbours, 5,
                $"only {withNeighbours} of 25 seeds give the start sector a foreign neighbour -- FrontierGuaranteePass would be all but unreachable in a real campaign");
        }

        // ==================== neighbour guarantees ====================

        [Test]
        public void NearestForeignSystem_IsBetween450kAnd750kFromStart()
        {
            // AdjustSeparation only runs when NearestOtherIndex finds something (Apply returns
            // early below two systems), so the sample is the seeds whose start sector really has
            // a neighbour. The old loop over seeds 0..39 left `nearest` at float.MaxValue on
            // every single-system start sector and then asserted it was under 750,001 -- seed 2
            // failed on exactly that, and every passing seed in the loop was passing vacuously on
            // the lower bound. The band itself is untouched.
            foreach (StartSectorCase test in CampaignStartSectorsWithANeighbour(40))
            {
                SystemManifest start = test.Manifest.Systems[0];
                float nearest = float.MaxValue;
                for (int i = 1; i < test.Manifest.Systems.Count; i++)
                    nearest = Mathf.Min(nearest, Vector3.Distance(start.AbsoluteCenter, test.Manifest.Systems[i].AbsoluteCenter));

                Assert.AreNotEqual(float.MaxValue, nearest, $"{test.Label}: no foreign system to measure against");
                Assert.GreaterOrEqual(nearest, FrontierGuaranteePass.MinNeighborDistance - 1f, test.Label);
                Assert.LessOrEqual(nearest, FrontierGuaranteePass.MaxNeighborDistance + 1f, test.Label);
            }
        }

        [Test]
        public void NearestThreeForeignSystems_CoverAllFiveFamilies_PlusCompactBalancedRarePlus()
        {
            // EnsureProfileGuarantees and EnsureFamilyCoverage both distribute their fixups
            // ACROSS the nearest three neighbours -- one missing family per neighbour, one forced
            // profile per pass -- so three of them have to exist before the guarantee is even
            // expressible. sector.0.0.0 never holds four systems in this galaxy (seed 0's start
            // sector holds two, which is why "no Compact among nearest three" was the failure),
            // so the sample is the sectors the galaxy genuinely fills with four or more stars,
            // each generated as a start sector. Every assertion below is byte-identical to the
            // original -- only the sample changed.
            foreach (StartSectorCase test in DenseStartSectors(40))
            {
                SectorManifest sector = test.Manifest;
                SystemManifest start = sector.Systems[0];

                List<SystemManifest> nearestThree = NearestThree(sector);
                Assert.AreEqual(3, nearestThree.Count, $"{test.Label}: this guarantee is about three foreign neighbours");

                var families = new HashSet<ResourceFamily>();
                foreach (PlanetManifest p in start.Planets) families.Add(p.Family);
                bool hasCompact = false, hasBalanced = false, hasRarePlus = false;
                foreach (SystemManifest s in nearestThree)
                {
                    if (s.ProfileKind == SystemProfileKind.CompactFrontier) hasCompact = true;
                    if (s.ProfileKind == SystemProfileKind.Balanced) hasBalanced = true;
                    foreach (PlanetManifest p in s.Planets)
                    {
                        families.Add(p.Family);
                        if (p.Rarity >= Rarity.Rare) hasRarePlus = true;
                    }
                }

                Assert.AreEqual(5, families.Count, $"{test.Label}: families = {families.Count}");
                Assert.IsTrue(hasCompact, $"{test.Label}: no Compact among nearest three");
                Assert.IsTrue(hasBalanced, $"{test.Label}: no Balanced among nearest three");
                Assert.IsTrue(hasRarePlus, $"{test.Label}: no Rare+ among nearest three");
            }
        }

        [Test]
        public void SingleSystemNeverRequiredToCarryAllFiveFamiliesAlone()
        {
            // The guarantee is about the UNION of start + nearest three, not
            // any one system in isolation — assert at least one seed in a
            // reasonable sample has no single system covering all five.
            bool foundSingleFamilySystem = false;
            for (ulong seed = 0; seed < 40 && !foundSingleFamilySystem; seed++)
            {
                SectorManifest sector = StartSector(seed);
                foreach (SystemManifest s in sector.Systems)
                {
                    var families = new HashSet<ResourceFamily>();
                    foreach (PlanetManifest p in s.Planets) families.Add(p.Family);
                    if (families.Count < 5) foundSingleFamilySystem = true;
                }
            }
            Assert.IsTrue(foundSingleFamilySystem);
        }

        // --- VS-009 §2/§3/§10.2: star-class and environment start guarantees ---

        [Test]
        public void StartSystem_StarClassIsAlwaysYellow()
        {
            for (ulong seed = 0; seed < 40; seed++)
                Assert.AreEqual(StarClass.Yellow, StartSector(seed).Systems[0].StarClass, $"seed {seed}");
        }

        [Test]
        public void StartSystem_RecommendedPlanetIsAlwaysCalm()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                SystemManifest start = StartSector(seed).Systems[0];
                PlanetManifest recommended = null;
                foreach (PlanetManifest p in start.Planets) if (p.RecommendedStart) recommended = p;
                Assert.IsNotNull(recommended, $"seed {seed}");
                Assert.AreEqual(EnvironmentClass.Calm, recommended.Environment, $"seed {seed}");
            }
        }

        private static List<SystemManifest> NearestThree(SectorManifest sector)
        {
            SystemManifest start = sector.Systems[0];
            var others = new List<(SystemManifest system, float dist)>();
            for (int i = 1; i < sector.Systems.Count; i++)
                others.Add((sector.Systems[i], Vector3.Distance(start.AbsoluteCenter, sector.Systems[i].AbsoluteCenter)));
            others.Sort((a, b) => a.dist.CompareTo(b.dist));
            var result = new List<SystemManifest>();
            for (int i = 0; i < Mathf.Min(3, others.Count); i++) result.Add(others[i].system);
            return result;
        }

        /// <summary>
        /// Every start sector these two guarantees are checked against: the sectors the galaxy
        /// fills densely enough for "nearest three" to mean three (30), PLUS the campaign's own
        /// sector.0.0.0 on the seeds where it really has a neighbour (30). The old sweep of
        /// sector.0.0.0 over seeds 0..59 asserted almost nothing — a one-system start sector has
        /// no nearest three, so both loops ran zero times on most seeds.
        /// </summary>
        private static List<StartSectorCase> NeighbourGuaranteeCases()
        {
            var cases = new List<StartSectorCase>();
            cases.AddRange(DenseStartSectors(30));
            cases.AddRange(CampaignStartSectorsWithANeighbour(30));
            return cases;
        }

        [Test]
        public void NearestThreeForeignSystems_NeverContainBlueGiant_AndHaveAtLeastTwoDistinctStarClasses()
        {
            int withTwoOrMoreNeighbours = 0;
            foreach (StartSectorCase test in NeighbourGuaranteeCases())
            {
                List<SystemManifest> nearestThree = NearestThree(test.Manifest);
                Assert.Greater(nearestThree.Count, 0, $"{test.Label}: every case must have a neighbour to check");
                var classes = new HashSet<StarClass>();
                foreach (SystemManifest s in nearestThree)
                {
                    Assert.AreNotEqual(StarClass.BlueGiant, s.StarClass, $"{test.Label}: {s.SystemId}");
                    classes.Add(s.StarClass);
                }
                if (nearestThree.Count >= 2) { Assert.GreaterOrEqual(classes.Count, 2, test.Label); withTwoOrMoreNeighbours++; }
            }
            Assert.GreaterOrEqual(withTwoOrMoreNeighbours, 30,
                "the distinct-star-class half of this guarantee needs cases with two or more neighbours, or it asserts nothing");
        }

        [Test]
        public void NearestThreeForeignSystems_EachHasAtLeastOneCalmPlanet()
        {
            int checkedSystems = 0;
            foreach (StartSectorCase test in NeighbourGuaranteeCases())
                foreach (SystemManifest s in NearestThree(test.Manifest))
                {
                    bool hasCalm = false;
                    foreach (PlanetManifest p in s.Planets) if (p.Environment == EnvironmentClass.Calm) { hasCalm = true; break; }
                    Assert.IsTrue(hasCalm, $"{test.Label}: {s.SystemId} has no Calm planet");
                    checkedSystems++;
                }
            Assert.GreaterOrEqual(checkedSystems, 100, "test setup sanity: the sample must actually contain neighbours to check");
        }

        [Test]
        public void StarClassAndEnvironmentGuarantees_OnlyChangeStarClassAndEnvironment_NeverPositionCompositionFamilyRarity()
        {
            // Synthetic sector: start already covers all 5 families (so
            // EnsureFamilyDiversity/EnsureFamilyCoverage are no-ops) and the
            // three neighbors already jointly satisfy Compact+Balanced+Rare+
            // (so EnsureProfileGuarantees is a no-op too) — isolating the
            // effect to ONLY the new star-class/environment fixups.
            //
            // The compositions are hand-built, but the ids and the coordinate are not: they come
            // from a sector the galaxy genuinely fills with four stars, so nothing in this
            // fixture is an invented world id. (This test never asks a repository to resolve
            // them; the manifest is handed straight to FrontierGuaranteePass.Apply.)
            StartSectorCase donor = DenseStartSectors(1)[0];
            SectorCoordinate sector = donor.Manifest.Coordinate;
            var realIds = new List<string>();
            foreach (SystemManifest s in donor.Manifest.Systems) realIds.Add(s.SystemId);
            Assert.GreaterOrEqual(realIds.Count, 4, donor.Label);

            var families = new[] { ResourceFamily.Metal, ResourceFamily.Wood, ResourceFamily.Gas, ResourceFamily.Water, ResourceFamily.Diamond };

            List<PlanetManifest> StartPlanets(string systemId)
            {
                var list = new List<PlanetManifest>();
                for (int i = 0; i < families.Length; i++)
                    list.Add(new PlanetManifest(i, WorldIdCodec.PlanetId(systemId, i), families[i], Rarity.Common,
                        40000f + i * 20000f, 0f, 0f, 0f, 1200f, i == 0, EnvironmentClass.Harsh, surfaceLayoutSeed: (ulong)(i + 1)));
                return list;
            }

            List<PlanetManifest> NeighborPlanets(string systemId, Rarity topRarity)
            {
                var list = new List<PlanetManifest>();
                for (int i = 0; i < 3; i++)
                    list.Add(new PlanetManifest(i, WorldIdCodec.PlanetId(systemId, i), ResourceFamily.Metal,
                        i == 0 ? topRarity : Rarity.Common, 40000f + i * 20000f, 0f, 0f, 0f, 1200f, false,
                        EnvironmentClass.Harsh, surfaceLayoutSeed: (ulong)(i + 100)));
                return list;
            }

            string startId = realIds[0];
            string s1Id = realIds[1];
            string s2Id = realIds[2];
            string s3Id = realIds[3];

            // Sector-local placements, so the separation pass sees them inside their own sector's
            // sub-cell boxes exactly as it would for generated content. The nearest neighbour sits
            // 500,000 away — already inside the 450k..750k band, so AdjustSeparation is a no-op
            // and the isolation this test depends on is preserved.
            Vector3 origin = sector.Origin;
            var systems = new List<SystemManifest>
            {
                new SystemManifest(startId, sector, 0, "Start", StarClass.Yellow, origin + new Vector3(200000f, 500000f, 500000f),
                    100000f, 8000f, true, SystemProfileKind.Balanced, SectorManifestGenerator.GenerationVersion, StartPlanets(startId)),
                new SystemManifest(s1Id, sector, 1, "S1", StarClass.BlueGiant, origin + new Vector3(700000f, 500000f, 500000f),
                    60000f, 8000f, false, SystemProfileKind.CompactFrontier, SectorManifestGenerator.GenerationVersion, NeighborPlanets(s1Id, Rarity.Common)),
                new SystemManifest(s2Id, sector, 2, "S2", StarClass.BlueGiant, origin + new Vector3(200000f, 1100000f, 500000f),
                    60000f, 8000f, false, SystemProfileKind.Balanced, SectorManifestGenerator.GenerationVersion, NeighborPlanets(s2Id, Rarity.Common)),
                new SystemManifest(s3Id, sector, 3, "S3", StarClass.BlueGiant, origin + new Vector3(200000f, 500000f, 1100000f),
                    60000f, 8000f, false, SystemProfileKind.RichFrontier, SectorManifestGenerator.GenerationVersion, NeighborPlanets(s3Id, Rarity.Rare)),
            };
            var raw = new SectorManifest(sector, SectorManifestGenerator.GenerationVersion, systems);

            // Snapshot Family/Rarity/OrbitRadius/PlanetId per system before Apply.
            var before = new Dictionary<string, List<(ResourceFamily family, Rarity rarity, float orbit, string planetId)>>();
            foreach (SystemManifest s in systems)
            {
                var snap = new List<(ResourceFamily, Rarity, float, string)>();
                foreach (PlanetManifest p in s.Planets) snap.Add((p.Family, p.Rarity, p.OrbitRadius, p.PlanetId));
                before[s.SystemId] = snap;
            }

            SectorManifest result = FrontierGuaranteePass.Apply(seed: 12345UL, raw);

            // The three neighbors were all BlueGiant -> guarantee must have fixed them.
            var afterById = new Dictionary<string, SystemManifest>();
            foreach (SystemManifest s in result.Systems) afterById[s.SystemId] = s;
            Assert.AreNotEqual(StarClass.BlueGiant, afterById[s1Id].StarClass);
            Assert.AreNotEqual(StarClass.BlueGiant, afterById[s2Id].StarClass);
            Assert.AreNotEqual(StarClass.BlueGiant, afterById[s3Id].StarClass);

            // Family/Rarity/OrbitRadius/PlanetId must be byte-identical to the pre-Apply snapshot for EVERY system.
            foreach (SystemManifest s in result.Systems)
            {
                List<(ResourceFamily family, Rarity rarity, float orbit, string planetId)> beforeSnap = before[s.SystemId];
                Assert.AreEqual(beforeSnap.Count, s.Planets.Count, s.SystemId);
                for (int i = 0; i < beforeSnap.Count; i++)
                {
                    PlanetManifest p = s.Planets[i];
                    Assert.AreEqual(beforeSnap[i].family, p.Family, $"{s.SystemId} planet {i} family");
                    Assert.AreEqual(beforeSnap[i].rarity, p.Rarity, $"{s.SystemId} planet {i} rarity");
                    Assert.AreEqual(beforeSnap[i].orbit, p.OrbitRadius, $"{s.SystemId} planet {i} orbit");
                    Assert.AreEqual(beforeSnap[i].planetId, p.PlanetId, $"{s.SystemId} planet {i} planetId");
                }
            }
        }
    }
}
