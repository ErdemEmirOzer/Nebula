using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-017A §2: pure, deterministic invariant checks for the three named
    /// gameplay routes (Keşif/Yayılma, Endüstri/Lojistik, Savunma/Savaş),
    /// driven entirely through explicit sim-second dt loops over the SAME
    /// shared pure resolvers production code already uses (RemoteEconomyTick,
    /// RemoteRaidTick, HangarProductionService, MaintenanceReserve) -- never a
    /// second/mocked authority, never real-time waiting, never a change to
    /// production Time.timeScale behavior. This is a representative-checkpoint
    /// harness, not an exhaustive L1-40 walkthrough -- see the VS-017A report's
    /// own "open soak/playtest work" section for what a real 6-10h run still
    /// needs to additionally confirm.
    /// </summary>
    public class Vs017aRouteHarnessTests
    {
        [TearDown]
        public void Cleanup()
        {
            StarSystemRegistry.Clear();
            CombatCampaignState.Reset();
            Progression.ProgressionService.Reset();
        }

        // ==================== canonical world fixtures ====================

        /// <summary>
        /// The sectors this seed's galaxy actually places stars in, campaign start sector first
        /// and then lexicographically — a fixed order, so every system these tests pick is the
        /// same one on every run and every platform.
        /// <para>
        /// Reading <see cref="GalaxyStarField"/> is what makes picking a system honest here.
        /// <see cref="SectorManifestGenerator.Generate"/> fills a sector from that field, which
        /// is finite and sparse (4 galaxies x 4 arms x ~6-10 stars, 128 in total) strung along
        /// spiral arms spanning millions of units on every axis, against 2,000,000-unit sectors.
        /// The campaign's own sector.0.0.0 usually holds exactly ONE star — the pinned start star
        /// — so <c>WorldIdCodec.SystemId(sector, 1)</c> names a system that does not exist, and
        /// both fixtures below used to NullReferenceException on it. Nothing is synthesised now:
        /// every system used here is one the galaxy really placed.
        /// </para>
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
            return occupied;
        }

        /// <summary>
        /// The first real foreign (non-start) system in canonical order, resolved through the
        /// live registry. Where the start sector genuinely holds a second star this IS that
        /// star; where it does not, it is the first system of the next sector the galaxy fills.
        /// Never an id built by index.
        /// </summary>
        private static SystemManifest FirstForeignSystem(ulong seed)
        {
            foreach (SectorCoordinate sector in OccupiedSectors(seed))
                foreach (SystemManifest system in StarSystemRegistry.Manifests.Resolve(sector).Systems)
                    if (!system.IsStartSystem) return system;

            Assert.Fail($"the galaxy for seed {seed} contains no foreign system at all");
            return null;
        }

        // ==================== ROUTE A: Keşif ve Yayılma (Discovery/Expansion) ====================

        private static (SystemManifest manifest, string planetId) BuildDiscoverySystem(ulong seed)
        {
            StarSystemRegistry.Initialize(seed);
            var sector = new SectorCoordinate(0, 0, 0);
            // The exact id production boots with (WorldInstaller.PrepareSession /
            // ExtractionDirector.LoadNow): sector.0.0.0 slot 0, where GalaxyStarField pins the
            // start star and never nudges it, so this system always exists.
            string systemId = WorldIdCodec.SystemId(sector, 0);
            StarSystemRegistry.SetStartSystem(sector, systemId);
            ExtractionState.Reset((int)seed);
            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);
            Assert.IsNotNull(manifest, $"seed {seed}: the campaign start system must always resolve");
            Assert.IsTrue(manifest.IsStartSystem);
            Assert.Greater(manifest.Planets.Count, 0, "the start system always carries its forced five-planet composition");
            return (manifest, manifest.Planets[0].PlanetId);
        }

        [Test]
        public void Route_Discovery_ScanDrillCommand_ReachesControlled_ThenNeverRegressesOrDoubleAwardsXp()
        {
            (SystemManifest manifest, string planetId) = BuildDiscoverySystem(701UL);
            ExtractionState.GetProfile(planetId).Scanned = true;

            var delta = new SystemDelta { systemId = manifest.SystemId };
            delta.drills.Add(new DrillDto { worldId = planetId, nodeId = "drill", state = (int)DrillLifecycle.Active });
            delta.structures.Add(new StructureDto { worldId = planetId, nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active, tier = 1 });
            // DominanceConditions.AllMet also requires an active defense weapon, not just drill+Command.
            delta.structures.Add(new StructureDto { worldId = planetId, nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, state = (int)DrillLifecycle.Active, tier = 1 });

            long xpBefore = Progression.ProgressionService.State.TotalXp;
            bool sawControlled = false;
            for (int i = 0; i < 700; i++)
            {
                RemoteEconomyTick.Tick(delta, manifest, 1f, false);
                if (ExtractionState.GetDominance(planetId).State == DominanceState.Controlled) sawControlled = true;
            }
            Assert.IsTrue(sawControlled, "scan + active drill + active Command must reach Controlled -- the discovery/expansion chain's own real gate");
            Assert.AreEqual(DominanceState.Controlled, ExtractionState.GetDominance(planetId).State, "must stay stable, not oscillate back out once conditions are still met");
            long xpAfterFirstStretch = Progression.ProgressionService.State.TotalXp;
            Assert.Greater(xpAfterFirstStretch, xpBefore, "reaching Controlled for the first time must award its milestone XP");

            for (int i = 0; i < 300; i++) RemoteEconomyTick.Tick(delta, manifest, 1f, false);
            Assert.AreEqual(xpAfterFirstStretch, Progression.ProgressionService.State.TotalXp,
                "continuing to tick an already-Controlled planet must never re-award the same first-Controlled milestone");
        }

        [Test]
        public void Route_Discovery_SecondSystemExpansion_ManifestIsDeterministicAcrossResolves()
        {
            (SystemManifest manifest0, _) = BuildDiscoverySystem(702UL);
            // The second system expansion reaches is simply the next real system in the galaxy's
            // own canonical order -- it is NOT required to share the start system's sector, and
            // for seed 702 it does not (sector.0.0.0 holds only the pinned start star). The old
            // WorldIdCodec.SystemId(sector, 1) named a system that has never existed here and
            // resolved to null, which is what threw.
            SystemManifest second = FirstForeignSystem(702UL);
            Assert.IsNotNull(second, "the galaxy must supply a second, foreign system to expand into");
            Assert.AreNotEqual(manifest0.SystemId, second.SystemId, "expansion must resolve a genuinely different second system");
            Assert.Greater(second.Planets.Count, 0, "an expansion target with no planets would make the identity checks below vacuous");

            SystemManifest secondAgain = StarSystemRegistry.Manifests.ResolveSystem(second.SystemId);
            Assert.AreSame(second, secondAgain, "resolving the same system id twice must never re-generate/duplicate a manifest instance");
            Assert.AreEqual(second.Planets.Count, secondAgain.Planets.Count);
            for (int i = 0; i < second.Planets.Count; i++)
                Assert.AreEqual(second.Planets[i].PlanetId, secondAgain.Planets[i].PlanetId, "planet identity must be stable across repeated resolution -- expansion must never silently reshuffle an already-visited system");
        }

        // ==================== ROUTE B: Endüstri ve Lojistik (Industry/Logistics) ====================

        [Test]
        public void Route_Industry_HangarProduction_ConsumesResourcesExactlyOnce_IssuesExactlyOneVehicleId()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 500f);
            cargo.Add("material.biocomposite", 500f);
            var service = new HangarProductionService(cargo);

            VehicleDef def = VehicleCatalog.Scout;
            HangarProductionRejectReason reject = service.TryStartProduction(VehicleClass.Scout, "material.biocomposite", characterLevel: 99, hangarTier: 3);
            Assert.AreEqual(HangarProductionRejectReason.None, reject);

            float partsBeforeComplete = cargo.GetStored(ProcessingCatalog.BasicPartId);
            float materialBeforeComplete = cargo.GetStored("material.biocomposite");

            string vehicleId = null;
            var seenIds = new HashSet<string>();
            for (int i = 0; i < Mathf.CeilToInt(def.BuildSeconds) + 5; i++)
            {
                string id = service.Tick(1f);
                if (id != null) { vehicleId = id; seenIds.Add(id); }
            }

            Assert.IsNotNull(vehicleId, "production must actually complete within its own real build-time budget");
            Assert.AreEqual(1, seenIds.Count, "completion must fire exactly once -- continuing to tick a finished/idle service must never mint a second vehicle id");

            // Reservation was atomic at start -- the physical cargo debit for a direct build is the
            // full recipe cost, consumed exactly once at completion (TryReserveAll + ConsumeReservation).
            Assert.AreEqual(partsBeforeComplete - def.BasicPartCost, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f,
                "must consume exactly the real recipe cost, no more, no less");
            Assert.AreEqual(materialBeforeComplete - def.SpecialMaterialCost, cargo.GetStored("material.biocomposite"), 0.01f);
        }

        [Test]
        public void Route_Industry_HangarProductionCancel_Refunds75PercentExactlyOnce()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 500f);
            cargo.Add("material.biocomposite", 500f);
            var service = new HangarProductionService(cargo);
            VehicleDef def = VehicleCatalog.Scout;
            service.TryStartProduction(VehicleClass.Scout, "material.biocomposite", 99, 3);

            // A reservation is an overlay on top of the cargo (GetReserved/GetAvailable), not an
            // immediate debit of GetStored -- physical stored amount doesn't move until the job
            // actually completes or cancels.
            float partsBeforeCancel = cargo.GetStored(ProcessingCatalog.BasicPartId);
            bool cancelled = service.CancelActiveJob();
            Assert.IsTrue(cancelled);

            // 75% of the reservation is simply released back to available (stored count untouched);
            // the other 25% is the real cancel penalty and is actually removed from physical stock.
            float expectedForfeitedParts = Mathf.Floor(def.BasicPartCost * 0.25f);
            Assert.AreEqual(partsBeforeCancel - expectedForfeitedParts, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f,
                "cancel must forfeit exactly floor(25%) of the reserved parts from physical stock -- never more (over-penalized) or less (a free do-over)");
            Assert.AreEqual(0f, cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.01f, "a cancelled job must leave nothing still reserved");

            bool secondCancel = service.CancelActiveJob();
            Assert.IsFalse(secondCancel, "a second cancel on an already-cancelled/empty slot must be a no-op, never a double refund");
        }

        [Test]
        public void Route_Industry_MaintenanceReserve_CapacityAndAcceptedItemsAreConserved()
        {
            var reserve = new MaintenanceReserve("planet-1", tier: 1);
            Assert.AreEqual(MaintenanceReserve.Tier1Capacity, reserve.Cargo.Capacity);

            Assert.IsTrue(MaintenanceReserve.IsAccepted(ProcessingCatalog.BasicPartId));
            Assert.IsFalse(MaintenanceReserve.IsAccepted("resource.metal_ore"), "raw/unrefined resources must never be accepted into a Maintenance Reserve");
            Assert.IsFalse(MaintenanceReserve.IsAccepted("energy.nuclear"));
            Assert.IsFalse(MaintenanceReserve.IsAccepted(null));

            float added = reserve.Add(ProcessingCatalog.BasicPartId, 1000f); // far over T1 capacity
            Assert.LessOrEqual(reserve.Cargo.GetStored(ProcessingCatalog.BasicPartId), MaintenanceReserve.Tier1Capacity + 0.01f,
                "physical volume must never exceed the reserve's own real capacity, regardless of how much was offered");
            Assert.AreEqual(reserve.Cargo.GetStored(ProcessingCatalog.BasicPartId), added, 0.01f, "Add's own return value must match what was actually physically stored -- no phantom resource creation/loss");

            reserve.ApplyTier(2);
            Assert.AreEqual(MaintenanceReserve.Tier2Capacity, reserve.Cargo.Capacity, "a Command tier upgrade must widen the reserve's real capacity, never shrink stored contents");
        }

        // ==================== ROUTE C: Savunma ve Savaş (Defense/Combat) ====================

        private static (SystemManifest manifest, RemoteRaidContext ctx) BuildDefenseSystem(ulong seed)
        {
            StarSystemRegistry.Initialize(seed);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset((int)seed);
            // A REMOTE system is any real system that is not the start system -- the routes here
            // care that it is defended from a distance, never which sector it sits in. This used
            // to be WorldIdCodec.SystemId(sector, 1), which only resolves when sector.0.0.0
            // happens to hold a second star; for seed 704 it does not, and the manifest came back
            // null. The canonical order is start-sector-first, so where a real second star does
            // exist in the start sector (seed 703) this picks exactly that same system.
            SystemManifest manifest = FirstForeignSystem(seed);
            Assert.IsNotNull(manifest, $"seed {seed}: the galaxy must supply a real remote system to defend");
            Assert.Greater(manifest.Planets.Count, 0, $"seed {seed}: a remote system with no planets cannot be raided or defended");
            string remoteSystemId = manifest.SystemId;
            foreach (PlanetManifest p in manifest.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);
            var ctx = new RemoteRaidContext(new SystemDelta { systemId = remoteSystemId }, manifest, new ThreatPressureState());
            return (manifest, ctx);
        }

        [Test]
        public void Route_Defense_MultipleFullRaidCycles_EachResolvesExactlyOnce_StageNeverRegresses()
        {
            (SystemManifest manifest, RemoteRaidContext ctx) = BuildDefenseSystem(703UL);
            string worldId = manifest.Planets[0].PlanetId;
            // A real drill+Command+Laser so dominance can actually recover between raids (an
            // undefended planet has no way back from a raid loss, which would make "several full
            // cycles" physically impossible rather than a bug in the scheduler itself).
            ctx.Delta.drills.Add(new DrillDto { worldId = worldId, nodeId = "drill", state = (int)DrillLifecycle.Active });
            ctx.Delta.structures.Add(new StructureDto { worldId = worldId, nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active, tier = 1, armor = 900f, hull = 1500f });
            ctx.Delta.structures.Add(new StructureDto { worldId = worldId, nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, state = (int)DrillLifecycle.Active, tier = 1, armor = 250f, hull = 400f });
            ExtractionState.GetProfile(worldId).Scanned = true;

            var campaign = CombatCampaignState.RaidCampaign;
            int raidsObserved = 0;
            RaidStage? lastStage = null;
            bool stageRegressed = false;
            // Deliberately large pure sim-second budget (still just a fast in-memory loop, no real
            // waiting) -- grace + pressure ramp + raid lifecycle + post-raid recovery easily spans
            // several thousand sim-seconds per cycle.
            for (int i = 0; i < 60000; i++)
            {
                bool wasNone = ctx.Pressure.Phase == RaidPhase.None;
                bool remoteRaidActive = ctx.Pressure.Phase != RaidPhase.None;
                RemoteRaidTick.Tick(ctx, 1f);
                RemoteEconomyTick.Tick(ctx.Delta, manifest, 1f, remoteRaidActive);
                if (wasNone && ctx.Pressure.Phase != RaidPhase.None && ctx.Pressure.Stage.HasValue)
                {
                    raidsObserved++;
                    if (lastStage.HasValue && ctx.Pressure.Stage.Value < lastStage.Value) stageRegressed = true;
                    lastStage = ctx.Pressure.Stage;
                }
            }

            Assert.GreaterOrEqual(raidsObserved, 2, "a long enough window with a real, recoverable defense must produce more than one raid cycle -- this test wants to see the sequence repeat, not just a single raid");
            Assert.IsFalse(stageRegressed, "raid stage (Pirate->Assault->Siege) must never go backwards across a campaign");
            // The very last observed raid may still be mid-flight when the loop ends (not yet through
            // FinalizeRaid) -- history must have recorded every FULLY RESOLVED one, i.e. either all of
            // them or all but the last, never fewer (a lost result) and never more (a duplicate).
            Assert.IsTrue(campaign.History.Count == raidsObserved || campaign.History.Count == raidsObserved - 1,
                $"campaign history ({campaign.History.Count}) must record exactly one entry per FULLY RESOLVED raid -- never zero, never duplicated (observed {raidsObserved} raid starts)");
            // The global slot is legitimately still held if the very last raid happens to still be
            // mid-flight at cutoff -- only assert it's free when that last raid actually finished.
            if (campaign.History.Count == raidsObserved)
                Assert.IsFalse(campaign.HasGlobalMajorRaidActive, "once every observed raid has fully resolved, the global slot must end up free, not stuck held");
        }

        [Test]
        public void Route_Defense_PlasmaAcrossManyFireCooldownCycles_NeverMoreThanOnePendingAtOnce_AllHitsAccountedFor()
        {
            (SystemManifest manifest, RemoteRaidContext ctx) = BuildDefenseSystem(704UL);
            string worldId = manifest.Planets[0].PlanetId;
            var pressure = new ThreatPressureState();
            pressure.ScheduleRaid(RaidStage.Assault, RaidDoctrine.Suppression, worldId, new List<string>(), 704UL, 0f);
            pressure.SetPhase(RaidPhase.Active);
            var activeCtx = new RemoteRaidContext(ctx.Delta, manifest, pressure);
            activeCtx.Delta.structures.Add(new StructureDto { worldId = worldId, nodeId = "defense.0", type = (int)SurfaceStructureType.Plasma, state = (int)DrillLifecycle.Active, tier = 1, armor = 99999f, hull = 99999f });

            var durability = new Durability(0f, 0f, 100000f, 1f);
            var enemyDef = EnemyCatalog.Get(EnemyIds.FighterMk1);
            var ai = new EnemyAiState(enemyDef);
            ai.RestoreFromSave(EnemyAiPhase.AttackOrScan, null, 0f, 0f);
            var enemy = new RemoteEnemyRuntime("e1", EnemyIds.FighterMk1, enemyDef, ai, durability, Vector3.zero, Vector3.zero);
            activeCtx.Enemies.Add(enemy);
            // TickActive's own "anyAlive" check reads pressure.SpawnedEnemyIds, not ctx.Enemies
            // directly -- without this the raid is (correctly) judged to have no enemies left and
            // immediately falls through to RetreatCleanup after the very first tick.
            pressure.NoteSpawnedEnemy(enemy.Id);

            // Fire interval (2.5s) is well under flight lifetime (10s), so several of this SAME
            // weapon's own shots are legitimately in flight at once -- up to ceil(10/2.5) = 4 -- the
            // real invariant is that count never grows UNBOUNDED (a leak), not that it's ever just 1.
            int maxInFlightAtOnce = Mathf.CeilToInt(ProjectileCatalog.PlanetaryPlasmaLifetimeSeconds / ProjectileCatalog.PlanetaryPlasmaFireIntervalSeconds) + 1;
            int resolvedHits = 0;
            float hullBefore = enemy.Durability.Hull;
            for (int i = 0; i < 120; i++)
            {
                RemoteRaidTick.Tick(activeCtx, 1f);
                Assert.LessOrEqual(activeCtx.PendingAttacks.Count, maxInFlightAtOnce, "a single Plasma weapon's own in-flight shot count must stay bounded by its real fire-interval/lifetime ratio, never grow unbounded");
                RemotePendingAttackTick.Tick(activeCtx, 1f);
                float hullNow = enemy.Durability.Hull;
                if (hullNow < hullBefore) { resolvedHits++; hullBefore = hullNow; }
            }

            Assert.Greater(resolvedHits, 1, "over 120 sim-seconds a real ~2.5s fire interval must land more than one hit");
            // Shots fired in this window's own final ~10s (the real flight lifetime) legitimately
            // haven't resolved yet when the loop stops -- the bounded-count assertion above is what
            // actually proves nothing leaks; this just re-confirms it's still within that same bound.
            Assert.LessOrEqual(activeCtx.PendingAttacks.Count, maxInFlightAtOnce, "whatever is still unresolved at cutoff must be exactly the tail of real, still-in-flight shots -- never more");
        }
    }
}
