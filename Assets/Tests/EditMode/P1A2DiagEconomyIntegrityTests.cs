using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENERAL REVIEW PART 1 / AGENT 2 — diagnostic fixture. Read-only against
    /// production code: nothing here fixes anything, every test exists to pin
    /// down a behaviour the audit claims, through the same public seams the game
    /// itself uses.
    /// <para>
    /// The two subjects are (a) the atomic build reservation a
    /// <see cref="SurfaceStructure"/> takes out on <see cref="SurfaceStructure.Create"/>
    /// and what happens to it when the structure's system is demoted and
    /// re-materialized (the streaming path calls
    /// <c>SurfaceStructure.Restore</c> again with the same world/node id), and
    /// (b) the first-hour resource arithmetic, asserted against the catalogs
    /// rather than against numbers typed here.
    /// </para>
    /// </summary>
    public class P1A2DiagEconomyIntegrityTests
    {
        private const ulong Seed = 20260812UL;

        private SystemManifest system;
        private SystemStreamingDirector savedStreaming;
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            savedStreaming = StarSystemRegistry.Streaming;
            StarSystemRegistry.Streaming = null;
            ExtractionState.Reset((int)Seed);
            StarSystemRegistry.Initialize(Seed);

            SectorManifest sector = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
            system = sector.Systems.First(s => s.Planets.Count > 0);
            StarSystemRegistry.SetStartSystem(system.Sector, system.SystemId);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            ExtractionState.DestroyAllStructures();
            StarSystemRegistry.Streaming = savedStreaming;
            StarSystemRegistry.Clear();
            ExtractionState.Reset((int)Seed);
            Nebula.Progression.ProgressionService.Reset();
        }

        /// <summary>A real materialized planet, exactly as the streaming path builds one.</summary>
        private PlanetIdentity MaterializePlanet()
        {
            var go = new GameObject("P1A2 Diag Planet");
            spawned.Add(go);
            CelestialBody body = go.AddComponent<CelestialBody>();
            body.Configure(CelestialBodyType.Planet, 60f, 1f, false);
            PlanetIdentity planet = go.AddComponent<PlanetIdentity>();
            planet.Initialize(system.Planets[0].PlanetId, body, true);
            return planet;
        }

        private static LogisticsCargo StockedFor(ResourceFamily family, float multiplier)
        {
            var cargo = new LogisticsCargo(100000f);
            foreach (var (itemId, amount) in CommandBuildService.BuildT1Cost(family))
                cargo.Add(itemId, amount * multiplier);
            return cargo;
        }

        /// <summary>
        /// The demotion the streaming director performs: the structure's own
        /// GameObject goes away (it self-destroys the moment its planet does), the
        /// persisted delta keeps its state and its full build cost.
        /// </summary>
        private static void DemoteAwayFrom(SurfaceStructure structure) =>
            Object.DestroyImmediate(structure.gameObject);

        // =====================================================================
        // A. THE BUILD RESERVATION AND THE STREAMING LIFECYCLE
        // =====================================================================

        /// <summary>
        /// Leaving the system with a capsule still in flight must not strand the
        /// reservation. Nothing releases it: <c>SurfaceStructure.OnDestroy</c> only
        /// unregisters, so the stock stays physically present and permanently
        /// unspendable with no owner left alive to consume, refund or release it.
        /// </summary>
        [Test]
        public void BuildReservation_OutlivesTheStructure_WhenItsSystemIsDemoted()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            LogisticsCargo cargo = StockedFor(profile.Family, 3f);

            List<(string itemId, float amount)> cost = CommandBuildService.BuildT1Cost(profile.Family);
            SurfaceStructure structure = SurfaceStructure.Create(planet, profile,
                SurfaceStructureCatalog.Command, profile.CommandNode, Vector3.zero, cargo, cost);
            string owner = structure.BuildOwnerId;

            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost,
                cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.001f,
                "the build must reserve its parts up front");

            DemoteAwayFrom(structure);

            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost,
                cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.001f,
                "AUDIT: the reservation survives the structure that owned it — nothing releases it");
            Assert.Greater(cargo.GetOwnerReservation(owner, ProcessingCatalog.BasicPartId), 0f,
                "AUDIT: the owner id is still holding stock with no live owner behind it");
        }

        /// <summary>
        /// P1-ECO-002, fixed in Part 3. Re-entering the system re-creates the
        /// structure through <c>SurfaceStructure.Restore</c>. It used to reserve the
        /// full saved cost AGAIN under the same owner id, and
        /// <c>LogisticsCargo.TryReserveAll</c> ADDS to an existing owner entry, so
        /// every round trip stacked another copy and the completing build spent a
        /// multiple of the catalog cost. Restore now goes through
        /// <c>TrySetReservation</c>, which replaces rather than accumulates, so the
        /// reservation is idempotent no matter how many times the system is
        /// re-materialized. This is the regression that keeps it that way.
        /// </summary>
        [Test]
        public void BuildReservation_IsIdempotentAcrossReMaterialization()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            LogisticsCargo cargo = StockedFor(profile.Family, 4f);

            List<(string itemId, float amount)> cost = CommandBuildService.BuildT1Cost(profile.Family);
            SurfaceStructure structure = SurfaceStructure.Create(planet, profile,
                SurfaceStructureCatalog.Command, profile.CommandNode, Vector3.zero, cargo, cost);
            string owner = structure.BuildOwnerId;
            float single = cargo.GetReserved(ProcessingCatalog.BasicPartId);

            DemoteAwayFrom(structure);

            // The exact restore the streaming director performs on re-promotion:
            // same def, same node, still-constructing runtime, the saved full cost.
            var durability = new Durability(SurfaceStructureCatalog.Command.ShieldMax,
                SurfaceStructureCatalog.Command.ArmorMax, SurfaceStructureCatalog.Command.HullMax, 0.5f);
            var runtime = new StructureRuntime(SurfaceStructureCatalog.Command,
                DrillLifecycle.Constructing, 0f, 5f, durability, 1);
            SurfaceStructure restored = SurfaceStructure.Restore(planet, profile,
                SurfaceStructureCatalog.Command, profile.CommandNode, runtime, cargo,
                CommandBuildService.BuildT1Cost(profile.Family));
            spawned.Add(restored.gameObject);

            Assert.AreEqual(single, cargo.GetOwnerReservation(owner, ProcessingCatalog.BasicPartId), 0.001f,
                "one structure must hold exactly one copy of its own build cost after re-materialization");

            // And the consume that ends the build removes whatever is reserved, so
            // a single reservation is a single charge at the catalog price.
            float storedBefore = cargo.GetStored(ProcessingCatalog.BasicPartId);
            cargo.ConsumeReservation(owner);
            Assert.AreEqual(storedBefore - single, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f,
                "completing the build must spend the catalog cost exactly once");
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost, single, 0.001f,
                "and that one copy must be the catalog cost, not some other number");
        }

        /// <summary>
        /// P1-ECO-003, fixed in Part 3. The 1 Hz remote tick advances a demoted
        /// system's StructureDto through Constructing to Active. Coming back to an
        /// already-finished structure, <c>Setup</c> used to take a "restored active
        /// structure never re-reserves" branch that cleared the cost list — so the
        /// reservation the original <c>Create</c> took out was never consumed and
        /// never released: a free Command Center plus stock the player could see
        /// forever and never spend. <c>Setup</c> now sets the reservation and
        /// consumes it on the Active branch, so a remotely completed structure is
        /// charged exactly once and strands nothing.
        /// </summary>
        [Test]
        public void RemotelyCompletedStructure_IsChargedExactlyOnce_AndStrandsNothing()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            LogisticsCargo cargo = StockedFor(profile.Family, 3f);

            float storedAtStart = cargo.GetStored(ProcessingCatalog.BasicPartId);
            SurfaceStructure structure = SurfaceStructure.Create(planet, profile,
                SurfaceStructureCatalog.Command, profile.CommandNode, Vector3.zero, cargo,
                CommandBuildService.BuildT1Cost(profile.Family));
            string owner = structure.BuildOwnerId;

            DemoteAwayFrom(structure);

            // Remote completion: the DTO comes back Active, still carrying the cost.
            var durability = new Durability(SurfaceStructureCatalog.Command.ShieldMax,
                SurfaceStructureCatalog.Command.ArmorMax, SurfaceStructureCatalog.Command.HullMax, 0.5f);
            var runtime = new StructureRuntime(SurfaceStructureCatalog.Command,
                DrillLifecycle.Active, 0f, 0f, durability, 1);
            SurfaceStructure restored = SurfaceStructure.Restore(planet, profile,
                SurfaceStructureCatalog.Command, profile.CommandNode, runtime, cargo,
                CommandBuildService.BuildT1Cost(profile.Family));
            spawned.Add(restored.gameObject);

            Assert.AreEqual(storedAtStart - SurfaceStructureCatalog.Command.PartsCost,
                cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f,
                "an active Command Center standing on the planet must have been paid for exactly once");
            Assert.AreEqual(0f, cargo.GetOwnerReservation(owner, ProcessingCatalog.BasicPartId), 0.001f,
                "and its reservation must be consumed, not left locking stock forever");
            Assert.AreEqual(cargo.GetStored(ProcessingCatalog.BasicPartId),
                cargo.GetAvailable(ProcessingCatalog.BasicPartId), 0.001f,
                "every remaining part must be spendable — nothing is stranded behind a dead owner");
        }

        // =====================================================================
        // B. FIRST-HOUR ARITHMETIC, AGAINST THE CATALOGS
        // =====================================================================

        /// <summary>
        /// A brand-new campaign's parts must cover the Command Center and the first
        /// Laser without a single processing run — the seed exists precisely so the
        /// first structure is buildable before the bench is.
        /// </summary>
        [Test]
        public void StartingParts_CoverCommandT1PlusLaserT1()
        {
            int needed = SurfaceStructureCatalog.Command.PartsCost + SurfaceStructureCatalog.Laser.PartsCost;
            Assert.GreaterOrEqual(ShipLogistics.StartingBasicParts, needed,
                $"a new campaign starts with {ShipLogistics.StartingBasicParts} parts and the first " +
                $"Command + Laser cost {needed}");
        }

        /// <summary>
        /// The raw the first hour has to mine, and whether the starting hold can
        /// physically carry it alongside the starting parts. Every number is read
        /// from its own authority; nothing is typed twice.
        /// </summary>
        [Test]
        public void FirstHour_RawAndVolumeBudgetsAreSatisfiable()
        {
            ProcessingRecipe materialRecipe = ProcessingCatalog.Recipes
                .First(r => r.OutputItemId == ProcessingCatalog.MaterialIds[(int)ResourceFamily.Metal]);

            int commandMaterial = SurfaceStructureCatalog.Command.MaterialCost;
            int laserMaterial = SurfaceStructureCatalog.Laser.MaterialCost;
            int rawNeeded = (commandMaterial + laserMaterial) * materialRecipe.InputCount / materialRecipe.OutputCount;

            float rawVolume = rawNeeded * CargoItemVolumes.PerUnit(ExtractionCatalog.ResourceIds[0]);
            float partsVolume = ShipLogistics.StartingBasicParts
                                * CargoItemVolumes.PerUnit(ProcessingCatalog.BasicPartId);

            Assert.LessOrEqual(rawVolume + partsVolume, ShipLogistics.BaseCapacity,
                $"the hold must be able to hold {rawNeeded} raw ({rawVolume} vol) at the same time as the " +
                $"starting parts ({partsVolume} vol); capacity is {ShipLogistics.BaseCapacity}");

            // And the drill has to be able to bank that much between visits.
            float t1Storage = ExtractionCatalog.StorageCapacity(Rarity.Common, 1);
            Assert.GreaterOrEqual(t1Storage, rawNeeded,
                $"a Common T1 drill banks {t1Storage} raw; the first hour needs {rawNeeded} in one trip");
        }

        /// <summary>
        /// The Quick Asset Package re-declares the three T1 recipes as its own
        /// constants instead of reading the catalogs. They agree today; nothing in
        /// production makes them agree tomorrow, which is what this pins.
        /// </summary>
        [Test]
        public void QuickAssetPackageCosts_StillMatchTheStructureCatalog()
        {
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost, QuickAssetPackageService.CommandBasicCost,
                "QuickAssetPackageService.CommandBasicCost is a second authority on the Command Center's parts");
            Assert.AreEqual(SurfaceStructureCatalog.Command.MaterialCost, QuickAssetPackageService.CommandSpecialCost,
                "QuickAssetPackageService.CommandSpecialCost is a second authority on the Command Center's material");
            Assert.AreEqual(SurfaceStructureCatalog.Laser.PartsCost, QuickAssetPackageService.LaserBasicCost,
                "QuickAssetPackageService.LaserBasicCost is a second authority on the Laser's parts");
            Assert.AreEqual(SurfaceStructureCatalog.Laser.MaterialCost, QuickAssetPackageService.LaserSpecialCost,
                "QuickAssetPackageService.LaserSpecialCost is a second authority on the Laser's material");
        }

        // =====================================================================
        // C. THE GUARD AND THE CHARGE READ DIFFERENT NUMBERS
        // =====================================================================

        /// <summary>
        /// <c>ShipExtraction.ConfirmBuild</c> gates the 20-part second drill on
        /// <c>cargo.Get(...)</c> — physically stored, reservations ignored — and then
        /// charges with <c>cargo.TryRemove(...)</c>, which measures
        /// <c>GetAvailable</c> and returns <c>false</c> without taking anything. The
        /// return value is discarded and the drill is created regardless. This pins
        /// the two-number mismatch that makes that possible; the same
        /// stored-vs-available confusion sits in <c>FindMaterialWithAtLeast</c>,
        /// which picks the Laser's material by stored amount and then fails the
        /// availability check on it.
        /// </summary>
        [Test]
        public void AStoredButFullyReservedHold_PassesAStoredGuard_AndPaysNothingOnTryRemove()
        {
            const int cost = 20;
            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, cost);
            Assert.IsTrue(cargo.TryReserve("some-other-job", ProcessingCatalog.BasicPartId, cost));

            Assert.GreaterOrEqual(cargo.Get(ProcessingCatalog.BasicPartId), (float)cost,
                "the stored reading — which is what the drill's guard uses — still says the parts are there");
            Assert.AreEqual(0f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), 0.001f,
                "and the spendable reading says they are not");

            float storedBefore = cargo.GetStored(ProcessingCatalog.BasicPartId);
            bool charged = cargo.TryRemove(ProcessingCatalog.BasicPartId, cost);

            Assert.IsFalse(charged, "TryRemove refuses — and ConfirmBuild discards this bool");
            Assert.AreEqual(storedBefore, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f,
                "AUDIT: nothing was spent, yet the guard above already let the build through");
        }

        /// <summary>
        /// The Laser's material picker compares <c>cargo.Get</c> (stored) across the
        /// five materials and keeps the largest, then the shared cost check measures
        /// <c>GetAvailable</c> on that one item. A hold with a large reserved stack of
        /// one material and a small free stack of another therefore refuses a build
        /// it could afford.
        /// </summary>
        [Test]
        public void MaterialPickedByStoredAmount_CanBeTheOneMaterialThatIsNotSpendable()
        {
            int need = SurfaceStructureCatalog.Laser.MaterialCost;
            var cargo = new LogisticsCargo(1000f);
            string big = ProcessingCatalog.MaterialIds[0];
            string small = ProcessingCatalog.MaterialIds[1];

            cargo.Add(big, need * 4);
            cargo.Add(small, need);
            Assert.IsTrue(cargo.TryReserve("some-other-job", big, need * 4));

            // What FindMaterialWithAtLeast does: largest STORED amount at or above the cost.
            string picked = null;
            float best = 0f;
            foreach (string id in ProcessingCatalog.MaterialIds)
            {
                float have = cargo.Get(id);
                if (have >= need && have > best) { best = have; picked = id; }
            }

            Assert.AreEqual(big, picked, "the picker takes the biggest pile, reservations and all");
            Assert.Less(cargo.GetAvailable(picked), (float)need,
                "AUDIT: and that pile cannot pay, so the build is refused");
            Assert.GreaterOrEqual(cargo.GetAvailable(small), (float)need,
                "AUDIT: while another material could have paid for it in full");
        }

        // =====================================================================
        // D. EVACUATION
        // =====================================================================

        /// <summary>
        /// <c>PlanetEvacuationService</c> says it "never deletes a
        /// structure/node/durability/inventory value" and that
        /// <c>ResolveActiveBuilds</c> cancels only what is "not-yet-Active". A
        /// <see cref="DrillLifecycle.Disabled"/> structure — a Command Center a raid
        /// knocked out, which a repair would bring back — is not Active either, so it
        /// went through the same filter. <c>Cancel</c> then refused the refund (only
        /// CapsuleInTransit/Constructing/Damaged qualify) but destroyed the GameObject
        /// unconditionally — the player lost a paid, repairable building and got
        /// nothing back for it.
        /// <para>
        /// P1-ECO-009, fixed in Part 3: <c>ResolveActiveBuilds</c> now selects on
        /// <c>CanCancelForRefund</c> / <c>CanCancelForKitReturn</c> instead of
        /// "not Active", so the only things it cancels are the ones it can actually
        /// pay for. A Disabled structure is paid for and repairable, so evacuation
        /// leaves it standing.
        /// </para>
        /// </summary>
        [Test]
        public void EvacuatingAPlanet_LeavesItsPaidDisabledStructuresStanding()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            var cargo = new LogisticsCargo(100000f);

            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
            durability.RestoreFromSave(0f, 0f, 0f, 0f);
            var runtime = new StructureRuntime(def, DrillLifecycle.Disabled, 0f, 0f, durability, 1);

            // Restored, so nothing is reserved: this structure was paid for long ago.
            SurfaceStructure disabled = SurfaceStructure.Restore(planet, profile, def,
                profile.CommandNode, runtime, cargo, new List<(string itemId, float amount)>());
            spawned.Add(disabled.gameObject);

            Assert.AreEqual(1, ExtractionState.Structures.Count, "fixture must have one structure standing");
            Assert.AreEqual(planet.WorldId, disabled.WorldId, "fixture must target the structure it built");
            Assert.AreNotEqual(DrillLifecycle.Active, disabled.Runtime.State,
                "Disabled is not Active, so ResolveActiveBuilds' filter selects it");
            Assert.IsFalse(disabled.Runtime.CanCancelForRefund,
                "a Disabled structure is outside the refundable window — that is the point");

            PlanetEvacuationService.ResolveActiveBuilds(planet.WorldId, cargo);

            bool unityDestroyed = disabled == null;
            bool stillRegistered = ExtractionState.Structures.Count > 0;

            Assert.IsFalse(unityDestroyed,
                $"a paid Disabled structure must survive evacuation (destroyed={unityDestroyed}, registered={stillRegistered})");
            Assert.IsTrue(stillRegistered,
                $"and must stay in the registry so a repair can still reach it (destroyed={unityDestroyed}, registered={stillRegistered})");
            Assert.AreEqual(0f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.001f,
                "nothing is refunded, because nothing was destroyed — the building itself is what the player keeps");
        }

        /// <summary>
        /// The drill half of P1-ECO-009, and the reason the fix had to be the
        /// selector rather than the refund: <c>CanCancelForKitReturn</c> excludes
        /// Disabled exactly as the structure version does, so destroying a Disabled
        /// kit drill cost the player both the drill AND the campaign's single
        /// one-shot Universal Pioneer Build Kit. Evacuation now leaves it standing,
        /// and the kit stays Consumed because nothing was cancelled.
        /// </summary>
        [Test]
        public void EvacuatingAPlanet_LeavesADisabledKitDrillStanding_AndDoesNotTouchTheKit()
        {
            PlanetIdentity planet = MaterializePlanet();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            var cargo = new LogisticsCargo(100000f);

            ExtractionState.ForceKitState(ExtractionState.KitState.Consumed);
            var runtime = new DrillRuntime(profile, DrillLifecycle.Disabled, 0f, 0f, 0f, 0f, 1);
            DrillStructure drill = DrillStructure.Restore(planet, profile, runtime,
                DrillStructure.BuildSource.Kit, 0);
            spawned.Add(drill.gameObject);

            Assert.AreEqual(1, ExtractionState.Drills.Count, "fixture must have one drill standing");
            Assert.AreEqual(planet.WorldId, drill.WorldId, "fixture must target the drill it built");

            PlanetEvacuationService.ResolveActiveBuilds(planet.WorldId, cargo);

            bool unityDestroyed = drill == null;
            bool stillRegistered = ExtractionState.Drills.Count > 0;

            Assert.IsFalse(unityDestroyed,
                $"a paid Disabled kit drill must survive evacuation (destroyed={unityDestroyed}, registered={stillRegistered})");
            Assert.IsTrue(stillRegistered,
                $"and must stay in the registry so a repair can still reach it (destroyed={unityDestroyed}, registered={stillRegistered})");
            Assert.AreEqual(ExtractionState.KitState.Consumed, ExtractionState.Kit,
                "the campaign's one Pioneer Kit is neither returned nor spent a second time");
        }

        // =====================================================================
        // E. WHAT A PLAYER CAN REACH
        // =====================================================================

        /// <summary>
        /// The drill and the planetary defenses have no public production seam at
        /// all: both handlers are private instance methods on
        /// <see cref="ShipExtraction"/>, reachable only from that component's own
        /// <c>Update</c> key read. A control cannot call what it cannot name, and
        /// the existing PlayMode coverage reaches them by reflection, which is not
        /// evidence a player can.
        /// </summary>
        [Test]
        public void DrillAndDefenseBuildHandlers_HaveNoPublicSeamAControlCouldCall()
        {
            System.Type type = typeof(ShipExtraction);
            const System.Reflection.BindingFlags any =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;

            Assert.IsNotNull(type.GetMethod("ConfirmBuild", any), "fixture is out of date");
            Assert.IsNotNull(type.GetMethod("BuildOrUpgradeDefense", any), "fixture is out of date");

            Assert.IsFalse(type.GetMethod("ConfirmBuild", any).IsPublic,
                "AUDIT: the drill build/upgrade has no public seam, so no button can invoke it");
            Assert.IsFalse(type.GetMethod("BuildOrUpgradeDefense", any).IsPublic,
                "AUDIT: the Laser/Plasma build/upgrade has no public seam, so no button can invoke it");

            // The Command Center and the support structures both do have one, which
            // is what makes the two above an omission rather than a house style.
            Assert.IsTrue(type.GetMethod("TryBuildOrUpgradeCommand", any).IsPublic);
            Assert.IsTrue(type.GetMethod("BuildOrUpgradeSupport", any).IsPublic);
        }
    }
}
