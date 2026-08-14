using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the deterministic POI generator + the two
    /// visit-order guarantees (VS-012 §1/§2/§10.1/§10.2), driven against a
    /// REAL initialized StarSystemRegistry (not synthetic system ids) so the
    /// group-guarantee's sibling lookback exercises the actual code path.
    /// </summary>
    public class PoiManifestGeneratorTests
    {
        [TearDown]
        public void Cleanup() => StarSystemRegistry.Clear();

        /// <summary>
        /// At least <paramref name="minForeignSystems"/> real foreign systems, collected by
        /// visiting the sectors the galaxy actually occupies in a fixed deterministic order.
        /// <para>
        /// This used to take a <c>sectorCount</c> and step <c>(sx, 0, 0)</c> for sx in
        /// 0..sectorCount, with the comment "~24-42 systems" — i.e. on the dead assumption that
        /// every sector yields 4-7 systems from <c>SectorManifestGenerator.ChooseCells</c>.
        /// ChooseCells has no call sites; <see cref="SectorManifestGenerator.Generate"/> fills a
        /// sector from <see cref="GalaxyStarField"/>, which is finite and sparse (4 galaxies x
        /// 4 arms x ~6-10 stars, 128 in total) and strung along spiral arms across every axis.
        /// A straight line at y=z=0 leaves the field immediately, so six "sectors" yielded one
        /// foreign system, not 24-42: the distribution test failed its own sanity floor and the
        /// determinism test threw <c>ArgumentOutOfRangeException</c> on <c>systems[0]</c>.
        /// </para>
        /// <para>
        /// Enumerating the star field first is also what keeps this bounded: only occupied
        /// sectors are resolved, so <see cref="SectorManifestRepository"/> never caches a sector
        /// that cannot contribute, and an exhausted galaxy is a named failure, not a hang.
        /// </para>
        /// </summary>
        private static List<SystemManifest> CollectForeignSystems(ulong seed, int minForeignSystems, out string startSystemId)
        {
            StarSystemRegistry.Initialize(seed);
            var startSector = new SectorCoordinate(0, 0, 0);
            // The id production itself boots with (WorldInstaller.PrepareSession /
            // ExtractionDirector.LoadNow): sector.0.0.0's slot 0, where GalaxyStarField pins the
            // start star and never nudges it. Not a guess about what might exist.
            startSystemId = WorldIdCodec.SystemId(startSector, 0);
            StarSystemRegistry.SetStartSystem(startSector, startSystemId);

            var result = new List<SystemManifest>();
            foreach (SectorCoordinate sector in OccupiedSectors(seed))
            {
                foreach (SystemManifest sys in StarSystemRegistry.Manifests.Resolve(sector).Systems)
                    if (!sys.IsStartSystem) result.Add(sys);
                if (result.Count >= minForeignSystems) break;
            }

            Assert.GreaterOrEqual(result.Count, minForeignSystems,
                $"the galaxy for seed {seed} only yields {result.Count} foreign systems; this test needs at least {minForeignSystems}");
            return result;
        }

        /// <summary>The sectors this seed's galaxy really places stars in, start sector first then lexicographically — a fixed order, so every sample below is identical on every run.</summary>
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
            return occupied;
        }

        [Test]
        public void StartSystem_NeverHasAnyPoi()
        {
            var ledger = new PoiVisitLedger();
            CollectForeignSystems(555UL, 1, out string startId);
            SystemManifest start = StarSystemRegistry.Manifests.ResolveSystem(startId);
            Assert.IsNotNull(start, "the campaign start system must always resolve");
            Assert.IsTrue(start.IsStartSystem);
            List<PoiManifest> pois = PoiManifestGenerator.Generate(555UL, start, ledger);
            Assert.AreEqual(0, pois.Count);
        }

        [Test]
        public void Generate_IsDeterministic_SameSeedSameLedgerSameResult()
        {
            List<SystemManifest> systems = CollectForeignSystems(101UL, 8, out _);
            var ledger = new PoiVisitLedger();
            foreach (SystemManifest s in systems) ledger.RecordVisit(s.SystemId);

            SystemManifest target = systems[0];
            List<PoiManifest> a = PoiManifestGenerator.Generate(101UL, target, ledger);
            List<PoiManifest> b = PoiManifestGenerator.Generate(101UL, target, ledger);

            Assert.Greater(a.Count, 0, "test setup sanity: the target system must actually carry a POI, or this proves nothing");
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].PoiId, b[i].PoiId);
                Assert.AreEqual(a[i].Type, b[i].Type);
                Assert.AreEqual(a[i].Risk, b[i].Risk);
                Assert.AreEqual(a[i].SystemLocalPosition, b[i].SystemLocalPosition);
            }
        }

        [Test]
        public void Generate_NeverExceedsTwoPois_AcrossManySystems()
        {
            List<SystemManifest> systems = CollectForeignSystems(202UL, 16, out _);
            var ledger = new PoiVisitLedger();
            foreach (SystemManifest s in systems) ledger.RecordVisit(s.SystemId);

            foreach (SystemManifest s in systems)
            {
                List<PoiManifest> pois = PoiManifestGenerator.Generate(202UL, s, ledger);
                Assert.LessOrEqual(pois.Count, PoiCatalog.MaxPoiPerSystem, s.SystemId);
            }
        }

        [Test]
        public void CanonicalId_MatchesSystemIdSlashPoiNN()
        {
            List<SystemManifest> systems = CollectForeignSystems(303UL, 16, out _);
            var ledger = new PoiVisitLedger();
            foreach (SystemManifest s in systems) ledger.RecordVisit(s.SystemId);

            int checkedIds = 0;
            foreach (SystemManifest s in systems)
            {
                List<PoiManifest> pois = PoiManifestGenerator.Generate(303UL, s, ledger);
                for (int i = 0; i < pois.Count; i++)
                {
                    Assert.AreEqual(s.SystemId + "/poi." + i.ToString("00"), pois[i].PoiId);
                    checkedIds++;
                }
            }
            Assert.Greater(checkedIds, 0, "test setup sanity: at least one system across this sample must have rolled a POI");
        }

        [Test]
        public void CountDistribution_RoughlyMatches30_50_20_AcrossManySystems()
        {
            // 48 real foreign systems, drawn from the sectors the galaxy actually fills. The old
            // sample was "6 sectors, ~24-42 systems" under the dead 4-7-per-sector model and
            // actually produced one system; 48 is a genuinely usable sample for a 30/50/20 shape
            // at the tolerances below, and it is the whole reason the floor could be raised from
            // 15 rather than lowered.
            List<SystemManifest> systems = CollectForeignSystems(404UL, 48, out _);
            var ledger = new PoiVisitLedger(); // empty -- unvisited, so pure natural distribution (no guarantee skew)

            int zero = 0, one = 0, two = 0;
            foreach (SystemManifest s in systems)
            {
                int count = PoiManifestGenerator.Generate(404UL, s, ledger).Count;
                if (count == 0) zero++; else if (count == 1) one++; else two++;
            }
            int total = systems.Count;
            Assert.GreaterOrEqual(total, 48, "test setup sanity: need a reasonably large sample");
            // Generous tolerance -- this is a statistical check over a modest sample, not an exact match.
            Assert.Greater((float)zero / total, 0.10f); Assert.Less((float)zero / total, 0.55f);
            Assert.Greater((float)one / total, 0.25f); Assert.Less((float)one / total, 0.75f);
            Assert.Greater((float)two / total, 0.02f); Assert.Less((float)two / total, 0.45f);
        }

        [Test]
        public void FirstEverForeignVisit_AlwaysCarriesASafeWreck()
        {
            List<SystemManifest> systems = CollectForeignSystems(707UL, 4, out _);
            var ledger = new PoiVisitLedger();
            SystemManifest first = systems[0];
            ledger.RecordVisit(first.SystemId);

            List<PoiManifest> pois = PoiManifestGenerator.Generate(707UL, first, ledger);
            bool hasSafeWreck = false;
            foreach (PoiManifest p in pois) if (p.Type == PoiType.Wreck && p.Risk == PoiRisk.Safe) hasSafeWreck = true;
            Assert.IsTrue(hasSafeWreck);
            Assert.LessOrEqual(pois.Count, PoiCatalog.MaxPoiPerSystem);
        }

        [Test]
        public void FirstEverForeignVisit_UnaffectedSystemsNeverGetTheGuarantee()
        {
            // Sanity: an ordinary system (not ordinal 0, not completing a still-empty group) is
            // simply its natural roll -- the guarantee never leaks onto unrelated systems.
            List<SystemManifest> systems = CollectForeignSystems(808UL, 8, out _);
            var ledger = new PoiVisitLedger();
            foreach (SystemManifest s in systems) ledger.RecordVisit(s.SystemId);

            // Ordinal 1 (second visited) is never ordinal 0 and never a group-completor (index 1 mod 3 != 2).
            // The sample above guarantees it exists -- it used to be wrapped in `if (systems.Count > 1)`,
            // which silently skipped the entire assertion once a "sector" stopped yielding systems.
            SystemManifest second = systems[1];
            List<PoiManifest> natural = PoiManifestGenerator.Generate(808UL, second, new PoiVisitLedger());
            List<PoiManifest> guaranteed = PoiManifestGenerator.Generate(808UL, second, ledger);
            Assert.AreEqual(natural.Count, guaranteed.Count);
            for (int i = 0; i < natural.Count; i++)
                Assert.AreEqual(natural[i].PoiId, guaranteed[i].PoiId, "an unaffected system's POIs must be its natural roll, identity included");
        }

        [Test]
        public void EveryConsecutiveTripletGroup_HasAtLeastOnePoi_AcrossManySystems()
        {
            List<SystemManifest> systems = CollectForeignSystems(909UL, 48, out _);
            var ledger = new PoiVisitLedger();
            foreach (SystemManifest s in systems) ledger.RecordVisit(s.SystemId);

            var manifestById = new Dictionary<string, SystemManifest>();
            foreach (SystemManifest s in systems) manifestById[s.SystemId] = s;

            int groups = 0;
            for (int groupStart = 0; groupStart + 2 < systems.Count; groupStart += 3)
            {
                int groupPoiCount = 0;
                for (int i = groupStart; i < groupStart + 3; i++)
                    groupPoiCount += PoiManifestGenerator.Generate(909UL, manifestById[ledger.VisitOrder[i]], ledger).Count;
                Assert.GreaterOrEqual(groupPoiCount, 1, $"group starting at ordinal {groupStart} has zero POIs across all three systems");
                groups++;
            }
            Assert.GreaterOrEqual(groups, 16, "test setup sanity: the sample must contain enough complete triplet groups to mean anything");
        }

        [Test]
        public void Guarantee_NeverChangesAnAlreadyExistingPoiId_OnlyAddsOrReplacesWithinCap()
        {
            // The guarantee-forced POI for a system with an existing single natural POI must append
            // (not overwrite) as long as the 2-cap allows it -- the original POI's id/index must survive.
            List<SystemManifest> systems = CollectForeignSystems(111UL, 40, out _);
            var ledger = new PoiVisitLedger();
            foreach (SystemManifest s in systems) ledger.RecordVisit(s.SystemId);

            int appendsChecked = 0;
            foreach (SystemManifest s in systems)
            {
                List<PoiManifest> natural = PoiManifestGenerator.Generate(111UL, s, new PoiVisitLedger());
                List<PoiManifest> guaranteed = PoiManifestGenerator.Generate(111UL, s, ledger);
                if (natural.Count == 1 && guaranteed.Count == 2)
                {
                    Assert.AreEqual(natural[0].PoiId, guaranteed[0].PoiId, "the original natural POI at slot 0 must survive when the guarantee only appends");
                    Assert.AreEqual(natural[0].Type, guaranteed[0].Type, "the surviving POI must be the same POI, not a same-id replacement");
                    Assert.AreEqual(natural[0].SystemLocalPosition, guaranteed[0].SystemLocalPosition);
                    appendsChecked++;
                }
            }
            // Without this the test passed by never finding an append at all -- which is exactly
            // what happened once the sample collapsed to a single system.
            Assert.Greater(appendsChecked, 0, "test setup sanity: the sample must contain at least one system where the guarantee actually appends");
        }

        [Test]
        public void GeneratedPosition_IsOutsideStarDangerZone_AndClearOfEveryPlanet()
        {
            List<SystemManifest> systems = CollectForeignSystems(222UL, 16, out _);
            var ledger = new PoiVisitLedger();
            foreach (SystemManifest s in systems) ledger.RecordVisit(s.SystemId);

            int checkedPois = 0;
            foreach (SystemManifest s in systems)
            {
                List<PoiManifest> pois = PoiManifestGenerator.Generate(222UL, s, ledger);
                float dangerRadius = s.StarRadius * StarClassCatalog.WarningRadiusMultiplier(s.StarClass);
                foreach (PoiManifest p in pois)
                {
                    checkedPois++;
                    Assert.Greater(p.SystemLocalPosition.magnitude, dangerRadius, s.SystemId);
                    foreach (PlanetManifest planet in s.Planets)
                    {
                        // Same orbit-plane transform PoiManifestGenerator/SystemMaterializer use for a planet's starting position.
                        Quaternion plane = Quaternion.AngleAxis(planet.AscendingNodeDegrees, Vector3.up) * Quaternion.AngleAxis(planet.OrbitInclinationDegrees, Vector3.right);
                        Vector3 flat = new Vector3(Mathf.Cos(planet.StartAngleDegrees * Mathf.Deg2Rad), 0f, Mathf.Sin(planet.StartAngleDegrees * Mathf.Deg2Rad)) * planet.OrbitRadius;
                        Vector3 planetLocal = plane * flat;
                        Assert.Greater(Vector3.Distance(p.SystemLocalPosition, planetLocal), planet.PlanetRadius, s.SystemId);
                    }
                }
            }
            Assert.Greater(checkedPois, 0, "test setup sanity: no POI was placed, so no placement was actually checked");
        }
    }
}
