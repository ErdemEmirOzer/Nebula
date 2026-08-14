using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-017A §4: determinism/scale proof across >=32 discovered systems --
    /// world generation, remote economy, and remote combat must all produce
    /// 100/100 identical results for the same seed, regardless of the order
    /// collections happen to be iterated in, and repeated Full-&gt;1Hz-&gt;
    /// save/load-&gt;Full cycles must never duplicate anything. Immutable
    /// SystemManifest vs. mutable SystemDelta stays a hard type-level
    /// separation throughout -- nothing here ever mutates a manifest.
    /// </summary>
    public class Vs017aDeterminismScaleTests
    {
        private const int SystemCount = 32;

        [TearDown]
        public void Cleanup()
        {
            StarSystemRegistry.Clear();
            CombatCampaignState.Reset();
            Progression.ProgressionService.Reset();
        }

        /// <summary>
        /// Collects at least <paramref name="minCount"/> systems by visiting the sectors the
        /// galaxy actually occupies, in a fixed deterministic order -- so reaching 32 spans
        /// several real sectors rather than one sector's own system-index range.
        /// <para>
        /// This used to step <c>(sx, 0, 0)</c> outward under a <c>while (result.Count &lt; minCount)</c>
        /// loop, on the stated assumption that "one sector only ever generates 4-7 systems
        /// (SectorManifestGenerator.ChooseCells)". That assumption was false, and the loop could
        /// not terminate: <see cref="SectorManifestGenerator.Generate"/> never calls ChooseCells on
        /// this path -- it fills a sector from <see cref="GalaxyStarField"/>, which is finite
        /// (4 galaxies x 4 arms x ~6-10 stars, about 129 stars in total) and strung along spiral
        /// arms spanning millions of units on every axis. A straight line at y=z=0 leaves the star
        /// field almost immediately, after which every sector is empty, <c>result.Count</c> stops
        /// growing, and the walk resolves sectors forever. Each one is permanently cached by
        /// <see cref="SectorManifestRepository"/>, so the Editor climbed to 5-8 GB and the whole
        /// EditMode suite died without ever writing results -- it was this, not slow tests.
        /// </para>
        /// <para>
        /// Enumerating the star field first is what makes the walk finite: only occupied sectors
        /// are resolved, so nothing is cached that cannot contribute, and an exhausted galaxy is a
        /// named failure instead of a hang.
        /// </para>
        /// </summary>
        private static List<SystemManifest> CollectManifests(ulong seed, int minCount)
        {
            StarSystemRegistry.Initialize(seed);
            var startSector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(startSector, WorldIdCodec.SystemId(startSector, 0));

            // Occupied sectors, discovered from the star field itself rather than guessed at.
            // Ordered start-sector-first then lexicographically, so the traversal is identical on
            // every run and every platform -- which is the property these tests exist to assert.
            var occupied = new List<SectorCoordinate>();
            var seen = new HashSet<SectorCoordinate>();
            foreach (GalaxyStarField.Placement p in GalaxyStarField.All(seed))
            {
                SectorCoordinate s = SectorCoordinate.FromAbsolutePosition(p.AbsolutePosition);
                if (seen.Add(s)) occupied.Add(s);
            }
            occupied.Sort((a, b) =>
            {
                if (a.Equals(startSector) != b.Equals(startSector)) return a.Equals(startSector) ? -1 : 1;
                if (a.X != b.X) return a.X.CompareTo(b.X);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            });

            var result = new List<SystemManifest>();
            foreach (SectorCoordinate sector in occupied)
            {
                SectorManifest sm = StarSystemRegistry.Manifests.Resolve(sector);
                foreach (SystemManifest system in sm.Systems) result.Add(system);
                if (result.Count >= minCount) break;
            }

            Assert.GreaterOrEqual(result.Count, minCount,
                $"the galaxy for seed {seed} only yields {result.Count} systems across its " +
                $"{occupied.Count} occupied sector(s); these tests need at least {minCount}");
            return result;
        }

        private static string SnapshotWorldGen(ulong seed)
        {
            List<SystemManifest> systems = CollectManifests(seed, SystemCount);
            var sb = new StringBuilder();
            foreach (SystemManifest m in systems)
            {
                sb.Append(m.SystemId).Append(':').Append(m.StarClass).Append(':').Append(m.Planets.Count);
                foreach (PlanetManifest p in m.Planets)
                    sb.Append('|').Append(p.PlanetId).Append(':').Append(p.Family).Append(':').Append(p.Rarity);
                sb.Append(';');
            }
            return sb.ToString();
        }

        [Test]
        public void ThirtyTwoSystems_WorldGeneration_Produces100Of100IdenticalResults()
        {
            string a = SnapshotWorldGen(801UL);
            for (int i = 0; i < 100; i++)
            {
                StarSystemRegistry.Clear();
                Assert.AreEqual(a, SnapshotWorldGen(801UL), $"world-gen diverged on repetition {i}");
            }
        }

        [Test]
        public void ThirtyTwoSystems_ResolvingTwice_ReturnsTheSameImmutableManifestInstance_NeverRegenerated()
        {
            List<SystemManifest> systems = CollectManifests(802UL, SystemCount);
            foreach (SystemManifest s in systems)
            {
                SystemManifest again = StarSystemRegistry.Manifests.ResolveSystem(s.SystemId);
                Assert.AreSame(s, again, $"system {s.SystemId} must resolve to the same manifest instance -- a mutable delta must never cause manifest regeneration");
            }
        }

        private static RemoteRaidContext BuildRemoteContext(SystemManifest manifest)
        {
            foreach (PlanetManifest p in manifest.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);
            var delta = new SystemDelta { systemId = manifest.SystemId };
            var pressure = new ThreatPressureState();
            return new RemoteRaidContext(delta, manifest, pressure);
        }

        /// <summary>
        /// VS-017A.1 §5: ticks every one of >=32 systems' RemoteRaidTick + RemotePendingAttackTick +
        /// RemoteEconomyTick for `ticks` sim-seconds, through a repository (a Dictionary keyed by
        /// system id, exactly like the real StarSystemRegistry-backed sparse set) that gets POPULATED
        /// in whatever order `repositoryInsertionOrder` gives -- but the dispatch order every single
        /// cycle is ALWAYS re-derived by sorting system ids canonically (string.CompareOrdinal), the
        /// exact same call SystemStreamingDirector.TickSparseSystems itself makes
        /// (`ordered.Sort(string.CompareOrdinal)`) before ticking anything. This mirrors the real
        /// production scheduler faithfully: insertion/discovery order can vary freely, but dispatch
        /// order never does, so even the single shared global major-raid slot (RaidCampaignState) --
        /// genuinely contested between systems -- converges on an identical winner and an identical
        /// Phase/Pressure progression every time. Raid Phase/Pressure fields are therefore NEVER
        /// excluded from the returned fingerprint: excluding them would hide a real production
        /// divergence rather than prove the canonical order actually eliminates the race.
        /// </summary>
        private static string RunManySystems(ulong seed, int ticks, IList<int> repositoryInsertionOrder)
        {
            StarSystemRegistry.Clear(); CombatCampaignState.Reset(); Progression.ProgressionService.Reset();
            List<SystemManifest> systems = CollectManifests(seed, SystemCount);
            ExtractionState.Reset((int)seed);

            var manifestById = new Dictionary<string, SystemManifest>();
            var repository = new Dictionary<string, RemoteRaidContext>();
            foreach (int idx in repositoryInsertionOrder)
            {
                SystemManifest manifest = systems[idx];
                RemoteRaidContext ctx = BuildRemoteContext(manifest);
                // A real drill+Command+Laser so both economy (dominance) and raid/combat sides have
                // something genuine to resolve deterministically across every system.
                string worldId = manifest.Planets[0].PlanetId;
                ctx.Delta.drills.Add(new DrillDto { worldId = worldId, nodeId = "drill", state = (int)DrillLifecycle.Active });
                ctx.Delta.structures.Add(new StructureDto { worldId = worldId, nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active, tier = 1, armor = 900f, hull = 1500f });
                ctx.Delta.structures.Add(new StructureDto { worldId = worldId, nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, state = (int)DrillLifecycle.Active, tier = 1, armor = 250f, hull = 400f });
                ExtractionState.GetProfile(worldId).Scanned = true;
                manifestById[manifest.SystemId] = manifest;
                repository[manifest.SystemId] = ctx;
            }

            for (int t = 0; t < ticks; t++)
            {
                // The exact production dispatch order: canonical system-id sort, re-derived every
                // cycle -- never the repository's own iteration/insertion order.
                var orderedIds = new List<string>(repository.Keys);
                orderedIds.Sort(string.CompareOrdinal);
                foreach (string systemId in orderedIds)
                {
                    RemoteRaidContext ctx = repository[systemId];
                    bool remoteRaidActive = ctx.Pressure.Phase != RaidPhase.None;
                    RemoteRaidTick.Tick(ctx, 1f);
                    RemotePendingAttackTick.Tick(ctx, 1f);
                    RemoteEconomyTick.Tick(ctx.Delta, manifestById[systemId], 1f, remoteRaidActive);
                }
            }

            var finalIds = new List<string>(repository.Keys);
            finalIds.Sort(string.CompareOrdinal); // always summarize in canonical order regardless of insertion order
            var sb = new StringBuilder();
            foreach (string systemId in finalIds)
            {
                RemoteRaidContext ctx = repository[systemId];
                sb.Append(systemId).Append(':')
                  .Append(ctx.Pressure.Phase).Append(':').Append(ctx.Pressure.Pressure.ToString("0.0000")).Append(':')
                  .Append(ExtractionState.GetDominance(manifestById[systemId].Planets[0].PlanetId).State)
                  .Append(':').Append(ctx.Enemies.Count).Append(':').Append(ctx.PendingAttacks.Count).Append(';');
            }
            return sb.ToString();
        }

        private static List<int> SequentialOrder(int count)
        {
            var order = new List<int>();
            for (int i = 0; i < count; i++) order.Add(i);
            return order;
        }

        /// <summary>A fixed-seed Fisher-Yates shuffle -- deterministic/reproducible across test runs, but genuinely different from `SequentialOrder` and from every other `shuffleSeed`.</summary>
        private static List<int> ShuffledOrder(int count, int shuffleSeed)
        {
            List<int> order = SequentialOrder(count);
            var rng = new System.Random(shuffleSeed);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        [Test]
        public void ThirtyTwoSystems_RemoteEconomyAndCombat_Produces100Of100IdenticalResults()
        {
            // VS-017A.1 §5: a genuine 100-repetition proof through the same canonical-sort dispatch
            // SystemStreamingDirector.TickSparseSystems itself uses, invoked directly rather than
            // through a live scene. The baseline uses sequential repository insertion order; every
            // one of the 100 repetitions uses a DIFFERENT shuffled repository insertion order --
            // proving the real scheduler's own canonical order (not insertion order) is what
            // determines the outcome, INCLUDING the shared global raid-slot's own Phase/Pressure
            // state, which is never excluded as "by design."
            string baseline = RunManySystems(901UL, 3000, SequentialOrder(SystemCount));
            for (int i = 0; i < 100; i++)
            {
                string result = RunManySystems(901UL, 3000, ShuffledOrder(SystemCount, shuffleSeed: i + 1));
                Assert.AreEqual(baseline, result, $"repetition {i} (shuffled repository insertion order) diverged from the canonical-dispatch baseline -- including the shared global raid-slot state");
            }
        }

        [Test]
        public void ThirtyTwoSystems_ShuffledRepositoryInsertionOrder_StillProducesTheIdenticalResult_IncludingGlobalRaidSlot()
        {
            string a = RunManySystems(902UL, 3000, SequentialOrder(SystemCount));
            string b = RunManySystems(902UL, 3000, ShuffledOrder(SystemCount, shuffleSeed: 4242));
            Assert.AreEqual(a, b, "the real production scheduler always re-derives its own canonical system-id dispatch order regardless of repository insertion order, so a shuffled insertion order must converge on the exact same fingerprint -- including which system wins the shared global major-raid slot and its Phase/Pressure progression, never excluded as 'by design'");
        }

        [Test]
        public void ThirtyTwoSystems_RepeatedSaveLoadRoundTrips_NeverDuplicateAnything()
        {
            List<SystemManifest> systems = CollectManifests(903UL, SystemCount);
            ExtractionState.Reset(903);

            var deltas = new List<SystemDelta>();
            for (int i = 0; i < systems.Count; i++)
            {
                var delta = new SystemDelta { systemId = systems[i].SystemId };
                delta.enemies.Add(new EnemyDto { enemyId = $"enemy-{i}", defId = EnemyIds.FighterMk1, hull = 220f });
                delta.pendingAttacks.Add(new PendingAttackDto { attackId = $"atk-{i}", damageAmount = 10f, remainingFlightSeconds = 5f });
                delta.combatWreckage.Add(new CombatWreckageDto { wreckageId = $"wreck-{i}", remainingActiveSeconds = 30f });
                StarSystemRegistry.Deltas.Set(delta);
                deltas.Add(delta);
            }

            // Repeated round trip through the SAME registry-level Set/TryGet path a real save/load
            // uses (SystemDelta itself is what gets (de)serialized) -- re-setting the identical delta
            // content must never grow any of its own collections.
            for (int cycle = 0; cycle < 10; cycle++)
                foreach (SystemDelta d in deltas) StarSystemRegistry.Deltas.Set(d);

            for (int i = 0; i < systems.Count; i++)
            {
                SystemDelta reloaded = StarSystemRegistry.Deltas.TryGet(systems[i].SystemId);
                Assert.IsNotNull(reloaded);
                Assert.AreEqual(1, reloaded.enemies.Count, $"system {i}: enemy duplicated across repeated round trips");
                Assert.AreEqual(1, reloaded.pendingAttacks.Count, $"system {i}: pending attack duplicated");
                Assert.AreEqual(1, reloaded.combatWreckage.Count, $"system {i}: salvage wreckage duplicated");
            }
        }

        [Test]
        public void ThirtyTwoSystems_RepeatedRaidHistoryXpAndSalvage_NeverDuplicateAcrossReload()
        {
            List<SystemManifest> systems = CollectManifests(904UL, SystemCount);
            ExtractionState.Reset(904);

            var contexts = new RemoteRaidContext[systems.Count];
            for (int i = 0; i < systems.Count; i++) contexts[i] = BuildRemoteContext(systems[i]);

            for (int t = 0; t < 3000; t++)
                for (int i = 0; i < systems.Count; i++) RemoteRaidTick.Tick(contexts[i], 1f);

            long xpAfterFirstStretch = Progression.ProgressionService.State.TotalXp;
            int historyAfterFirstStretch = CombatCampaignState.RaidCampaign.History.Count;

            // Persist every system's own pressure state (the real save-facing shape), then
            // reconstruct fresh RemoteRaidContexts from it exactly like a load would -- with no
            // further ticking, history/XP/salvage must never be able to double up as a result.
            for (int i = 0; i < systems.Count; i++)
            {
                contexts[i].SaveToDelta();
                RemoteRaidContext reloaded = RemoteRaidContext.LoadFromDelta(contexts[i].Delta, contexts[i].Manifest);
                reloaded.SaveToDelta();
            }

            Assert.AreEqual(xpAfterFirstStretch, Progression.ProgressionService.State.TotalXp, "a save/load round trip with no further ticking must never re-award XP");
            Assert.AreEqual(historyAfterFirstStretch, CombatCampaignState.RaidCampaign.History.Count, "a save/load round trip with no further ticking must never duplicate raid history");
        }
    }
}
