using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for manifest caching and lookup (VS-008 §1/§4/§10). Every sector is pure
    /// procedural generation — there is no persisted/frozen variant.
    /// <para>
    /// Every coordinate used here now comes from <see cref="GalaxyStarField"/> rather than being
    /// invented. Sector content is filled from that field, which is finite and sparse (4 galaxies
    /// x 4 arms x ~6-10 stars, 128 in total, strung along spiral arms spanning millions of units
    /// on every axis) against a 2,000,000-unit sector grid — so the overwhelming majority of
    /// coordinates hold nothing at all. The invented ones these tests used to pick, (3,3,3),
    /// (5,5,5) and (1,0,0), are all empty for their own seeds: one test failed outright on a null
    /// system, and two others were quietly asserting over empty collections.
    /// </para>
    /// </summary>
    public class SectorManifestRepositoryTests
    {
        /// <summary>
        /// The sectors this seed's galaxy actually places stars in, ordered so the choice is
        /// identical on every run: the campaign start sector first, then lexicographically.
        /// Bounded by the star field itself — resolving a sector caches it forever, so a walk
        /// that cannot tell an empty sector from an unreached one cannot be bounded at all.
        /// </summary>
        private static List<SectorCoordinate> OccupiedSectors(ulong seed)
        {
            var start = new SectorCoordinate(0, 0, 0);
            var seen = new HashSet<SectorCoordinate>();
            var occupied = new List<SectorCoordinate>();
            foreach (GalaxyStarField.Placement placement in GalaxyStarField.All(seed))
            {
                SectorCoordinate sector = SectorCoordinate.FromAbsolutePosition(placement.AbsolutePosition);
                if (seen.Add(sector)) occupied.Add(sector);
            }
            occupied.Sort((a, b) =>
            {
                if (a.Equals(start) != b.Equals(start)) return a.Equals(start) ? -1 : 1;
                if (a.X != b.X) return a.X.CompareTo(b.X);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            });
            Assert.GreaterOrEqual(occupied.Count, 2,
                $"the galaxy for seed {seed} occupies {occupied.Count} sector(s); these tests need at least two real ones");
            return occupied;
        }

        /// <summary>The first sector the galaxy fills that is NOT the campaign start sector — a real, ordinary, procedurally generated sector.</summary>
        private static SectorCoordinate FirstForeignOccupiedSector(ulong seed) => OccupiedSectors(seed)[1];

        /// <summary>
        /// A sector the galaxy leaves empty, found rather than assumed. Used to pin the
        /// empty-sector contract down explicitly instead of letting an empty sector silently
        /// turn some other assertion into a no-op.
        /// </summary>
        private static SectorCoordinate AnEmptySector(ulong seed)
        {
            var occupied = new HashSet<SectorCoordinate>(OccupiedSectors(seed));
            for (int x = 0; x < 64; x++)
            {
                var candidate = new SectorCoordinate(x, 7, 7);
                if (!occupied.Contains(candidate)) return candidate;
            }
            Assert.Fail($"seed {seed}: expected the galaxy to leave at least one of the 64 probed sectors empty");
            return default;
        }

        [Test]
        public void Resolve_CachesAndReturnsTheSameManifestInstanceOnRepeatCalls()
        {
            var repo = new SectorManifestRepository(1UL);
            var sector = new SectorCoordinate(0, 0, 0);
            SectorManifest a = repo.Resolve(sector);
            SectorManifest b = repo.Resolve(sector);
            Assert.AreSame(a, b);
            Assert.AreEqual(1, repo.CachedSectorCount);
            // The campaign start sector always holds at least the pinned start star
            // (GalaxyStarField.StartStar), so this is caching real content, not an empty list.
            Assert.GreaterOrEqual(a.Systems.Count, 1, "the start sector always holds at least one real system");
        }

        [Test]
        public void ResolveSystem_FindsASystemWithoutResolvingItsSectorFirst()
        {
            const ulong Seed = 2UL;
            var repo = new SectorManifestRepository(Seed);
            SectorCoordinate sector = FirstForeignOccupiedSector(Seed);

            // A REAL system id, taken from the generator's own output for that sector rather
            // than synthesised as SystemId(sector, n). Generating here does not touch the
            // repository's cache, so the lookup below still has to resolve the sector itself --
            // which is the whole point of the test.
            string systemId = SectorManifestGenerator.Generate(Seed, sector, isStartSector: false).Systems[0].SystemId;
            Assert.AreEqual(0, repo.CachedSectorCount, "the repository must not have seen this sector yet");

            SystemManifest found = repo.ResolveSystem(systemId);
            Assert.IsNotNull(found, $"{systemId} is a real system of {sector.Id} and must resolve without its sector being resolved first");
            Assert.AreEqual(systemId, found.SystemId);
            Assert.AreEqual(1, repo.CachedSectorCount, "resolving a system must pull in exactly its own sector, nothing else");
            Assert.AreSame(found, repo.Resolve(sector).FindSystem(systemId), "the id lookup and the sector lookup must be the same instance, never a second generation");
        }

        [Test]
        public void ASectorTheGalaxyLeavesEmpty_ResolvesToAnEmptyManifest_AndItsSystemIdsResolveToNull()
        {
            // Most of this universe is the space between the spiral arms. That is a real,
            // load-bearing state — navigation and streaming ask about sectors the ship merely
            // passes through — so it gets its own assertion instead of being the accidental
            // subject of tests that meant to exercise real content.
            const ulong Seed = 2UL;
            var repo = new SectorManifestRepository(Seed);
            SectorCoordinate empty = AnEmptySector(Seed);

            SectorManifest manifest = repo.Resolve(empty);
            Assert.IsNotNull(manifest, "an empty sector still resolves to a real manifest, never null");
            Assert.AreEqual(0, manifest.Systems.Count, $"{empty.Id} holds no stars, so it must hold no systems");
            Assert.AreEqual(empty, manifest.Coordinate);
            Assert.AreSame(manifest, repo.Resolve(empty), "an empty sector is cached exactly like a full one");

            Assert.IsNull(repo.ResolveSystem(WorldIdCodec.SystemId(empty, 0)),
                "there is no system 0 in an empty sector -- the lookup must answer null, never invent one");
            Assert.IsNull(repo.ResolveSystem("not-a-world-id"), "an unparseable id must answer null, never throw");
        }

        [Test]
        public void Resolve_IsDeterministicAcrossFreshRepositoryInstances()
        {
            const ulong Seed = 3UL;
            SectorCoordinate sector = FirstForeignOccupiedSector(Seed);

            SectorManifest fromFirstRepo = new SectorManifestRepository(Seed).Resolve(sector);
            SectorManifest fromSecondRepo = new SectorManifestRepository(Seed).Resolve(sector);

            Assert.GreaterOrEqual(fromFirstRepo.Systems.Count, 1, $"{sector.Id} is an occupied sector -- comparing two empty lists proves nothing");
            Assert.AreEqual(fromFirstRepo.Systems.Count, fromSecondRepo.Systems.Count);
            for (int i = 0; i < fromFirstRepo.Systems.Count; i++)
            {
                Assert.AreEqual(fromFirstRepo.Systems[i].SystemId, fromSecondRepo.Systems[i].SystemId);
                Assert.AreEqual(fromFirstRepo.Systems[i].AbsoluteCenter, fromSecondRepo.Systems[i].AbsoluteCenter, "a system's position must regenerate identically -- save data only carries the mutable delta on top of this");
                Assert.AreEqual(fromFirstRepo.Systems[i].Planets.Count, fromSecondRepo.Systems[i].Planets.Count);
            }
        }

        [Test]
        public void SetStartSector_OnlyAffectsGenerationForThatCoordinate()
        {
            const ulong Seed = 4UL;
            var repo = new SectorManifestRepository(Seed);
            var startSector = new SectorCoordinate(0, 0, 0);
            repo.SetStartSector(startSector);

            SectorManifest start = repo.Resolve(startSector);
            Assert.IsTrue(start.Systems[0].IsStartSystem);

            SectorManifest other = repo.Resolve(FirstForeignOccupiedSector(Seed));
            Assert.GreaterOrEqual(other.Systems.Count, 1, "an occupied sector -- an empty one would make the loop below vacuous");
            foreach (SystemManifest s in other.Systems) Assert.IsFalse(s.IsStartSystem);
            Assert.IsTrue(repo.RequiresFullResolve(startSector), "only the start sector runs through FrontierGuaranteePass");
            Assert.IsFalse(repo.RequiresFullResolve(other.Coordinate));
        }
    }
}
