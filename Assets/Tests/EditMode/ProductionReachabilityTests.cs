using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Navigation;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Production defects a tutorial audit found and left unfixed, each driven
    /// through the seam a real player reaches, never through a test-only shortcut:
    /// <list type="number">
    /// <item>the interstellar attention query — <see cref="SystemStatusQuery.Link"/>
    /// with the ship inside no system at all, which is every crossing between two
    /// stars and used to throw <c>ArgumentNullException</c> once per candidate
    /// system per frame;</item>
    /// <item>Fleet-page production — the page's own Produce control, which could not
    /// build a Scout (it passed a hardcoded null material the service always
    /// refuses) and offered no control at all for Collector, Engineer or Escort;</item>
    /// <item>five backends whose commands, validators and gameplay effects all
    /// worked and all tested, with no control anywhere to reach them: Engineer
    /// target choice, Escort protect-target choice, Escort stance, the intervention
    /// group's membership and policy, and a Command Center's Maintenance Reserve
    /// policy.</item>
    /// </list>
    /// Every test here was run against the pre-fix production code first: the link
    /// tests threw out of <c>SectorManifestRepository.ResolveSystem</c>'s dictionary
    /// lookup, the production tests failed on <c>MaterialNotSelected</c>, and the
    /// control tests failed to compile against a page that had no such control.
    /// </summary>
    public class ProductionReachabilityTests
    {
        private const ulong Seed = 20260809UL;

        private SystemManifest system;
        private SystemKnowledgeService knowledge;
        private SystemStreamingDirector savedStreaming;

        [SetUp]
        public void SetUp()
        {
            savedStreaming = StarSystemRegistry.Streaming;
            // Nothing is materialized in EditMode, so no system is Full or Warm and
            // every query below takes its real "read the persisted delta" branch.
            StarSystemRegistry.Streaming = null;

            ExtractionState.Reset((int)Seed);
            StarSystemRegistry.Initialize(Seed);

            SectorManifest sector = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
            system = sector.Systems.First(s => s.Planets.Count > 0);
            StarSystemRegistry.SetStartSystem(system.Sector, system.SystemId);

            knowledge = new SystemKnowledgeService();
            knowledge.InitializeNewGame(system.SystemId);
        }

        [TearDown]
        public void TearDown()
        {
            StarSystemRegistry.Streaming = savedStreaming;
            StarSystemRegistry.Clear();
            ExtractionState.Reset((int)Seed);
        }

        /// <summary>A real persisted snapshot for the system: one planet, in a real dominance state.</summary>
        private void SeedSnapshot(DominanceState state)
        {
            var delta = new SystemDelta { systemId = system.SystemId };
            delta.dominance.Add(new DominanceDto
            {
                worldId = system.Planets[0].PlanetId,
                state = (int)state,
                stabilizeTimer = 0f,
                recoveryTimer = 0f
            });
            StarSystemRegistry.Deltas.Set(delta);
        }

        // =====================================================================
        // A. THE SHIP INSIDE NO SYSTEM AT ALL
        // =====================================================================

        /// <summary>
        /// <c>NavigationDirector.CurrentSystemId</c> is null whenever the ship is
        /// outside every system's navigation radius, and
        /// <c>NebulaHudAlerts.RefreshAttention</c> feeds that id, unchanged, into this
        /// query for every Visited system on a 5Hz timer. The id used to reach
        /// <c>SectorManifestRepository.ResolveSystem</c> as a raw Dictionary key.
        /// </summary>
        [Test]
        public void LinkFromInterstellarSpace_DoesNotThrowAndIsNeverLive()
        {
            SeedSnapshot(DominanceState.Controlled);

            SystemLinkState link = SystemLinkState.Live;
            Assert.DoesNotThrow(() => link = SystemStatusQuery.Link(system.SystemId, null, knowledge),
                "a ship inside no system is a normal flight state, not an error");

            Assert.AreNotEqual(SystemLinkState.Live, link,
                "Live models a Command relay that has to originate somewhere; a ship " +
                "between stars has no local Command to originate from");
        }

        /// <summary>
        /// The honest answer, not a blank one: real data exists for this system, and
        /// the player is simply out of contact with it, which is exactly what
        /// LastKnown means. HasValidSnapshot is what separates it from Disconnected.
        /// </summary>
        [Test]
        public void LinkFromInterstellarSpace_ReadsLastKnownWhereARealSnapshotExists()
        {
            SeedSnapshot(DominanceState.Controlled);

            Assert.IsTrue(SystemStatusQuery.HasValidSnapshot(system.SystemId),
                "the fixture must seed real persisted data, or this proves nothing");
            Assert.AreEqual(SystemLinkState.LastKnown,
                SystemStatusQuery.Link(system.SystemId, null, knowledge));
        }

        /// <summary>And Disconnected — never LastKnown — where there is no snapshot to be last-known ABOUT.</summary>
        [Test]
        public void LinkFromInterstellarSpace_ReadsDisconnectedWithNoSnapshot()
        {
            Assert.IsFalse(SystemStatusQuery.HasValidSnapshot(system.SystemId),
                "no delta was seeded, so there is nothing real to report");
            Assert.AreEqual(SystemLinkState.Disconnected,
                SystemStatusQuery.Link(system.SystemId, null, knowledge));
        }

        /// <summary>
        /// The whole HUD path, not just the resolver: the same call
        /// <c>NebulaHudAlerts.RefreshAttention</c> makes every refresh, with the ship's
        /// real null system id. The card must exist, must be flagged last-known (the
        /// HUD prints "· SON BİLİNEN" from that flag), and must never read as live.
        /// </summary>
        [Test]
        public void AttentionCardsFromInterstellarSpace_AreMarkedLastKnown()
        {
            SeedSnapshot(DominanceState.Contested);

            List<AttentionCard> cards = null;
            Assert.DoesNotThrow(
                () => cards = AttentionQuery.TopThreeCards(new[] { system.SystemId }, null, knowledge),
                "the attention HUD ran this once per Visited system per refresh");

            Assert.AreEqual(1, cards.Count, "a contested planet in a snapshotted system earns a card");
            Assert.IsTrue(cards[0].IsLastKnown,
                "showing out-of-contact state as if it were live is the one thing this card cannot do");
            Assert.AreEqual(AttentionPriority.Critical, cards[0].Priority);
        }

        /// <summary>
        /// Nothing unknown leaks in: with no snapshot the system is Disconnected, and
        /// a Disconnected system is skipped rather than given an empty card.
        /// </summary>
        [Test]
        public void AttentionCardsFromInterstellarSpace_ShowNothingForADisconnectedSystem()
        {
            List<AttentionCard> cards = null;
            Assert.DoesNotThrow(
                () => cards = AttentionQuery.TopThreeCards(new[] { system.SystemId }, null, knowledge));
            Assert.IsEmpty(cards, "a system we have no data for must produce no card, not a blank one");
        }

        /// <summary>
        /// Repeated exactly as the HUD repeats it. A guard that only survives the
        /// first call would still spam the player's log at 5Hz.
        /// </summary>
        [Test]
        public void RepeatedInterstellarRefreshes_StayQuiet()
        {
            SeedSnapshot(DominanceState.Contested);
            for (int frame = 0; frame < 30; frame++)
            {
                int captured = frame;
                Assert.DoesNotThrow(
                    () => AttentionQuery.TopThreeCards(new[] { system.SystemId }, null, knowledge),
                    $"refresh {captured} threw — the original defect recurred every frame");
            }
        }

        /// <summary>
        /// The same query with the ship really inside the system still resolves Live,
        /// so the guard did not flatten the distinction it was protecting.
        /// </summary>
        [Test]
        public void LinkFromInsideTheSystem_IsStillLive()
        {
            SeedSnapshot(DominanceState.Controlled);
            Assert.AreEqual(SystemLinkState.Live,
                SystemStatusQuery.Link(system.SystemId, system.SystemId, knowledge),
                "the ship's own system always counts as Live for itself");
        }

        // =====================================================================
        // B. BUILDING A VEHICLE FROM THE FLEET PAGE
        // =====================================================================

        private sealed class FleetHarness : IDisposable
        {
            internal readonly NebulaUiShell Shell;
            internal readonly ShipHangar Hangar;
            internal readonly NebulaFleetPage Page;
            internal readonly ShipCombat Combat;
            internal readonly InterventionGroupDirector Intervention;
            private readonly GameObject shipObject;
            private readonly GameObject interventionObject;
            private readonly GameObject host;
            private readonly List<GameObject> extras = new List<GameObject>();
            private readonly string planetWorldId;
            private NebulaHudPlanetContext reservePanel;
            private readonly float enteredTimeScale;

            internal FleetHarness(SystemManifest system)
            {
                planetWorldId = system.Planets[0].PlanetId;
                enteredTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                ExclusiveModal.ResetForSession();
                Shell = NebulaUiShell.Create();

                ProgressionService.Reset();
                // Level 25 clears every UnlockLevel in the catalog (the highest is 21),
                // through the real restore path rather than by writing the field.
                ProgressionService.Restore(ProgressionMath.TotalXpToReach(25), 0, null);

                VehicleAgent.Registry.Clear();

                shipObject = new GameObject("Fleet Test Ship");
                // Absolute position is transform.position + FloatingOrigin.TotalOffset,
                // and ShipHangar.CurrentSystemId is an absolute-position lookup, so this
                // puts the ship really inside the real system every command needs.
                shipObject.transform.position = system.AbsoluteCenter - FloatingOrigin.TotalOffset;

                shipObject.AddComponent<ShipController>();
                ShipLogistics logistics = shipObject.AddComponent<ShipLogistics>();
                Combat = shipObject.AddComponent<ShipCombat>();
                Hangar = shipObject.AddComponent<ShipHangar>();
                WakeAll(shipObject);

                logistics.InitializeNewCampaign();
                Combat.InitializeNewCampaign();
                Hangar.InitializeNewCampaign();
                Hangar.ApplyHangarTier(2);

                interventionObject = new GameObject("Fleet Test Intervention");
                Intervention = interventionObject.AddComponent<InterventionGroupDirector>();
                WakeAll(interventionObject);
                Intervention.Initialize(Hangar);

                // Real stock through the real restore seam, generously above every
                // catalog cost so InsufficientResources is never the reason a build
                // is refused unless a test deliberately empties the hold.
                LogisticsCargo cargo = Hangar.Cargo;
                cargo.SetCapacity(100000f);
                cargo.LoadItem(ProcessingCatalog.BasicPartId, 500f);
                foreach (string materialId in VehicleCatalog.SpecialMaterialIds)
                    cargo.LoadItem(materialId, 500f);

                // P1-ECO-013: Engineer Mk I really does require a Controlled planet now
                // — VS-015 §1 always said so, and the gate finally fires. A harness that
                // means to measure "can a player build this from the Fleet page" has to
                // give the player the world a player at that level would have, through
                // the same restore seam the save path uses. The gate itself is untouched.
                ExtractionState.GetDominance(planetWorldId)
                    .Restore(DominanceState.Controlled, 0f, 0f);

                var context = new NebulaContext();
                context.SetHangar(Hangar);
                context.SetInterventionGroupDirector(Intervention);

                host = new GameObject("Host", typeof(RectTransform));
                host.transform.SetParent(Shell.Root(UiLayer.Menu), false);
                var rect = (RectTransform)host.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;

                Page = new NebulaFleetPage(context);
                Page.Build(rect);
                Page.Refresh();
            }

            /// <summary>
            /// EditMode never runs Awake on a runtime-added component, so every field
            /// those Awakes set would still be null. Reflection, not SendMessage:
            /// SendMessage asserts on a component the player loop is not running and
            /// that assertion reaches the runner as a failure.
            /// </summary>
            private static void WakeAll(GameObject go)
            {
                if (Application.isPlaying) return;
                foreach (MonoBehaviour behaviour in go.GetComponents<MonoBehaviour>())
                    for (Type type = behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                    {
                        MethodInfo awake = type.GetMethod("Awake",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        if (awake == null) continue;
                        awake.Invoke(behaviour, null);
                        break;
                    }
            }

            /// <summary>Drives the page's real Produce control all the way to a started job.</summary>
            internal void Build(VehicleClass vehicleClass, int mk, string chosenMaterialId)
            {
                Page.OpenProductionPickerForTest();
                int vehicleIndex = Page.IndexOfBuildOption(vehicleClass, mk);
                Assert.GreaterOrEqual(vehicleIndex, 0,
                    $"the production picker offers no {vehicleClass} Mk {mk}");
                Page.ChooseBuildOptionForTest(vehicleIndex);

                if (chosenMaterialId == null) return; // fixed-recipe build: no second stage

                Assert.AreEqual(vehicleClass, Page.PendingBuildClass,
                    "the material stage lost track of which vehicle it is buying for");
                int materialIndex = Page.IndexOfBuildMaterial(chosenMaterialId);
                Assert.GreaterOrEqual(materialIndex, 0, "the picker offers no such material");
                Page.ChooseBuildOptionForTest(materialIndex);
            }

            /// <summary>Runs the real production service to completion, the way ShipHangar.Update does.</summary>
            internal void FinishJob()
            {
                HangarProductionJob job = Hangar.Production.ActiveJob;
                Assert.IsNotNull(job, "nothing is in the queue to finish");
                Hangar.Production.Tick(job.RemainingSeconds + 1f);
            }

            /// <summary>Produces a real vehicle of a class through the page, and selects it.</summary>
            internal VehicleAgent BuildAndSelect(VehicleClass vehicleClass)
            {
                Build(vehicleClass, 1, VehicleCatalog.MetalAlloyId);
                FinishJob();
                VehicleAgent vehicle = Hangar.Vehicles[Hangar.Vehicles.Count - 1];
                Page.Select(vehicle.VehicleId);
                return vehicle;
            }

            /// <summary>Damages the main ship through its real Durability, so it becomes a genuine Engineer target.</summary>
            internal void DamageTheShip()
            {
                Durability d = Combat.State.Durability;
                d.RestoreFromSave(d.ShieldMax, d.ArmorMax * 0.4f, d.HullMax * 0.4f, 0f);
            }

            /// <summary>A real active Command Center on a real materialized planet, close enough for the ship's reserve access to see it.</summary>
            internal SurfaceStructure CommandStructure()
            {
                var planetObject = new GameObject("Fleet Test Planet");
                planetObject.transform.position = shipObject.transform.position + new Vector3(150f, 0f, 0f);
                extras.Add(planetObject);
                CelestialBody body = planetObject.AddComponent<CelestialBody>();
                body.Configure(CelestialBodyType.Planet, 60f, 1f, false);
                PlanetIdentity planet = planetObject.AddComponent<PlanetIdentity>();
                planet.Initialize(planetWorldId, body, true);

                PlanetProfile profile = ExtractionState.GetProfile(planet);
                SurfaceStructureDef def = SurfaceStructureCatalog.Get(SurfaceStructureType.Command);
                var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, startFraction: 1f);
                durability.RestoreFromSave(def.ShieldMax, def.ArmorMax, def.HullMax, 0f);
                var runtime = new StructureRuntime(def, DrillLifecycle.Active, 0f, 0f, durability, 1);
                SurfaceStructure structure = SurfaceStructure.Restore(planet, profile, def, profile.CommandNode,
                    runtime, Hangar.Cargo, new List<(string itemId, float amount)>());
                extras.Add(structure.gameObject);
                return structure;
            }

            /// <summary>The ship's own reserve-proximity component and the HUD panel that owns the reserve's policy control.</summary>
            internal NebulaHudPlanetContext ReservePanel()
            {
                ShipMaintenanceReserveAccess access = shipObject.GetComponent<ShipMaintenanceReserveAccess>();
                if (access == null)
                {
                    access = shipObject.AddComponent<ShipMaintenanceReserveAccess>();
                    Invoke(access, "Awake");
                }
                if (reservePanel == null)
                {
                    reservePanel = NebulaHudPlanetContext.Create(Shell, access, null);
                    extras.Add(reservePanel.gameObject);
                }
                // Proximity and the panel's own projection both live in Update, which
                // EditMode never runs; driven here rather than faked.
                Invoke(access, "Update");
                Invoke(reservePanel, "Update");
                return reservePanel;
            }

            private static void Invoke(MonoBehaviour behaviour, string methodName)
            {
                for (Type type = behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                {
                    MethodInfo method = type.GetMethod(methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (method == null) continue;
                    method.Invoke(behaviour, null);
                    return;
                }
            }

            public void Dispose()
            {
                foreach (GameObject extra in extras) if (extra != null) UnityEngine.Object.DestroyImmediate(extra);
                extras.Clear();
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (interventionObject != null) UnityEngine.Object.DestroyImmediate(interventionObject);
                if (shipObject != null) UnityEngine.Object.DestroyImmediate(shipObject);
                if (Shell != null)
                {
                    Shell.Shutdown();
                    UnityEngine.Object.DestroyImmediate(Shell.gameObject);
                }
                VehicleAgent.Registry.Clear();
                ProgressionService.Reset();
                ExclusiveModal.ResetForSession();
                Time.timeScale = enteredTimeScale;
            }
        }

        /// <summary>
        /// The defect in one assertion: the button used to call
        /// <c>TryStartProduction(VehicleClass.Scout, null)</c>, which the service
        /// refuses with MaterialNotSelected every time, so this reported
        /// "MALZEME SEÇİLMEDİ" and nothing ever entered the queue.
        /// </summary>
        [Test]
        public void TheProduceControlStartsARealScoutBuild()
        {
            using (var h = new FleetHarness(system))
            {
                h.Build(VehicleClass.Scout, 1, VehicleCatalog.MetalAlloyId);

                Assert.IsNotNull(h.Hangar.Production.ActiveJob,
                    "the Fleet page's own Produce control started no job at all");
                Assert.AreEqual(VehicleClass.Scout, h.Hangar.Production.ActiveJob.VehicleClass);
                Assert.AreEqual("ÜRETİM BAŞLADI — KEŞİF · MK I", h.Page.StatusText);
            }
        }

        /// <summary>
        /// Every class a player can build, end to end: the picker offers it, the real
        /// service accepts it, the job carries the catalog's own cost and duration,
        /// and a real vehicle with the right definition exists when it finishes.
        /// Collector, Engineer and Escort had no control on this page at all.
        /// </summary>
        [Test]
        public void EveryVehicleClassCanBeBuiltFromTheFleetPage()
        {
            var classes = new[]
            {
                VehicleClass.Scout, VehicleClass.Collector, VehicleClass.Engineer, VehicleClass.Escort
            };

            foreach (VehicleClass vehicleClass in classes)
            {
                using (var h = new FleetHarness(system))
                {
                    VehicleDef expected = VehicleCatalog.Get(vehicleClass, 1);
                    h.Build(vehicleClass, 1, VehicleCatalog.MetalAlloyId);

                    HangarProductionJob job = h.Hangar.Production.ActiveJob;
                    Assert.IsNotNull(job, $"{vehicleClass} could not be started from the Fleet page");
                    Assert.AreEqual(vehicleClass, job.VehicleClass);
                    Assert.AreEqual(1, job.Mk);
                    Assert.AreEqual(expected.BuildSeconds, job.TotalSeconds, 0.001f,
                        "the job must carry the catalog's own duration, not a UI copy of it");

                    h.FinishJob();

                    Assert.AreEqual(1, h.Hangar.Vehicles.Count,
                        $"{vehicleClass} finished production without producing a vehicle");
                    Assert.AreSame(expected, h.Hangar.Vehicles[0].Def,
                        "the produced vehicle is bound to the wrong catalog definition");

                    h.Page.Refresh();
                    Assert.AreEqual(1, h.Page.VisibleRows, "the new vehicle never reached a row");
                }
            }
        }

        /// <summary>
        /// A Mk II vehicle's build is a fixed multi-item recipe with no player-chosen
        /// material, and <c>ShipHangar.TryStartFixedProduction</c> had zero callers
        /// anywhere in the project, so this whole path was unreachable.
        /// </summary>
        [Test]
        public void AFixedRecipeMk2BuildRunsThroughTheSamePicker()
        {
            using (var h = new FleetHarness(system))
            {
                h.Build(VehicleClass.Engineer, 2, null);

                HangarProductionJob job = h.Hangar.Production.ActiveJob;
                Assert.IsNotNull(job, "the Mk II fixed-recipe build never started");
                Assert.AreEqual(2, job.Mk);
                Assert.AreEqual(VehicleCatalog.EngineerMk2DirectRecipe.PartsCost, job.ReservedParts,
                    "the fixed recipe's own parts cost must be what was reserved");

                h.FinishJob();
                Assert.AreEqual(1, h.Hangar.Vehicles.Count);
                Assert.AreSame(VehicleCatalog.EngineerMk2, h.Hangar.Vehicles[0].Def);
            }
        }

        /// <summary>
        /// Rows are wired with a captured index, so choosing the second option must
        /// buy the second vehicle — not the last one the build loop happened to see.
        /// </summary>
        [Test]
        public void EachPickerRowBuysItsOwnVehicle()
        {
            using (var h = new FleetHarness(system))
            {
                h.Page.OpenProductionPickerForTest();
                Assert.IsTrue(h.Page.BuildPickerOpen);
                Assert.AreEqual(VehicleCatalog.All.Length, h.Page.VisibleBuildOptions,
                    "every real vehicle definition must be offered");

                // Deliberately NOT the last row: a closure over the loop variable
                // would have made every row start whatever the final row was.
                int index = h.Page.IndexOfBuildOption(VehicleClass.Collector, 1);
                Assert.Less(index, VehicleCatalog.All.Length - 1);
                h.Page.ChooseBuildOptionForTest(index);
                h.Page.ChooseBuildOptionForTest(h.Page.IndexOfBuildMaterial(VehicleCatalog.BiocompositeId));

                Assert.AreEqual(VehicleClass.Collector, h.Hangar.Production.ActiveJob.VehicleClass);
                Assert.AreEqual(VehicleCatalog.BiocompositeId, h.Hangar.Production.ActiveJob.ChosenMaterialId,
                    "the material row the player chose is the one that must be reserved");
            }
        }

        /// <summary>
        /// The refusal has to reach the player. It is the reason the old call was a
        /// silent no-op: the service answered, and the page threw the answer away.
        /// </summary>
        [Test]
        public void ARefusedBuildReportsTheServicesOwnReason()
        {
            using (var h = new FleetHarness(system))
            {
                h.Hangar.Cargo.Clear();
                h.Build(VehicleClass.Escort, 1, VehicleCatalog.MetalAlloyId);

                Assert.IsNull(h.Hangar.Production.ActiveJob, "an unaffordable build must reserve nothing");
                Assert.AreEqual(
                    NebulaFleetPage.ProductionRejectText(HangarProductionRejectReason.InsufficientResources),
                    h.Page.StatusText);
            }
        }

        /// <summary>The single slot is still a single slot, and the page says so before opening a list the player cannot act on.</summary>
        [Test]
        public void ASecondBuildIsRefusedWhileTheSlotIsBusy()
        {
            using (var h = new FleetHarness(system))
            {
                h.Build(VehicleClass.Scout, 1, VehicleCatalog.MetalAlloyId);
                Assert.IsNotNull(h.Hangar.Production.ActiveJob);

                h.Page.OpenProductionPickerForTest();

                Assert.IsFalse(h.Page.BuildPickerOpen, "a busy slot must not offer a list of builds");
                Assert.AreEqual(
                    NebulaFleetPage.ProductionRejectText(HangarProductionRejectReason.QueueBusy),
                    h.Page.StatusText);
            }
        }

        // =====================================================================
        // C. SIX BACKENDS THAT HAD NO CONTROL
        // =====================================================================
        //
        // Each of these has a working public command, a validator, a real state
        // change and a real gameplay effect -- and, until now, no way for a player
        // to reach any of it. Every test below drives the page's own control and
        // asserts the real state, never that a button exists.

        /// <summary>
        /// <c>ShipHangar.BuildEngineerTargetCandidates</c> had no production caller:
        /// the rail sent every Engineer to <c>EngineerTargetKind.Ship</c> with the
        /// literal id <c>"ship"</c>, so a damaged Command Center — which the service
        /// accepts and repairs — could not be chosen at all.
        /// </summary>
        [Test]
        public void TheEngineerRailSendsToTheChosenTargetNotAlwaysTheShip()
        {
            using (var h = new FleetHarness(system))
            {
                SurfaceStructure command = h.CommandStructure();
                Durability d = command.Runtime.Durability;
                d.RestoreFromSave(d.ShieldMax, d.ArmorMax * 0.3f, d.HullMax * 0.3f, 0f);
                h.DamageTheShip();

                VehicleAgent engineer = h.BuildAndSelect(VehicleClass.Engineer);
                h.Page.OpenEngineerTargetPickerForTest();

                Assert.IsTrue(h.Page.EngineerTargetPickerOpen);
                Assert.GreaterOrEqual(h.Page.VisibleTargets, 2,
                    "the ship AND the damaged structure are both real candidates");

                int index = h.Page.IndexOfTarget(command.TargetId);
                Assert.GreaterOrEqual(index, 0, "the damaged Command Center was not offered");
                h.Page.ChooseTargetForTest(index);

                Assert.IsNotNull(engineer.EngineerTask, "no Engineer mission was started");
                Assert.AreEqual(command.TargetId, engineer.EngineerTask.TargetId,
                    "the Engineer went somewhere other than the target the player picked");
                Assert.AreNotEqual(EngineerTargetKind.Ship, engineer.EngineerTask.TargetKind);
            }
        }

        /// <summary>
        /// <c>ShipHangar.BuildEscortProtectableTargets</c> had no production caller
        /// either: the rail always passed <c>("ship", TargetCategory.Ship)</c>, though
        /// <c>VehicleAgent.TryResolveEscortAnchorPosition</c> genuinely resolves the
        /// Vehicle and Structure categories.
        /// </summary>
        [Test]
        public void TheEscortRailProtectsTheChosenTargetNotAlwaysTheShip()
        {
            using (var h = new FleetHarness(system))
            {
                SurfaceStructure command = h.CommandStructure();
                VehicleAgent escort = h.BuildAndSelect(VehicleClass.Escort);

                h.Page.OpenEscortTargetPickerForTest();
                Assert.IsTrue(h.Page.EscortTargetPickerOpen);
                Assert.GreaterOrEqual(h.Page.VisibleTargets, 2,
                    "the ship AND this system's active Command Center are both protectable");

                int index = h.Page.IndexOfTarget(command.TargetId);
                Assert.GreaterOrEqual(index, 0, "the Command Center was not offered as a protectable target");
                h.Page.ChooseTargetForTest(index);

                Assert.IsNotNull(escort.EscortState, "the Escort was never assigned");
                Assert.AreEqual(command.TargetId, escort.EscortState.ProtectedTargetId,
                    "the Escort protected something other than what the player chose");
                Assert.AreEqual(Nebula.Combat.TargetCategory.Structure, escort.EscortState.ProtectedTargetCategory);
                Assert.AreEqual(EscortCommand.Protect, escort.EscortState.Command);
            }
        }

        /// <summary>
        /// <c>ShipHangar.SetEscortStance</c>'s only caller was an EditMode test, so
        /// every Escort in the game sat at Balanced forever. The stance is not
        /// cosmetic: it IS the retreat threshold both the live and the remote combat
        /// resolver read.
        /// </summary>
        [Test]
        public void TheEscortStanceControlChangesTheRealRetreatThreshold()
        {
            using (var h = new FleetHarness(system))
            {
                h.CommandStructure();
                VehicleAgent escort = h.BuildAndSelect(VehicleClass.Escort);
                h.Page.OpenEscortTargetPickerForTest();
                h.Page.ChooseTargetForTest(0);

                Assert.AreEqual(EscortStance.Balanced, escort.EscortState.Stance, "the default the catalog gives");
                Assert.AreEqual(EscortState.BalancedRetreatFraction, escort.EscortState.RetreatThresholdFraction, 0.0001f);

                h.Page.CycleEscortStanceForTest();
                Assert.AreEqual(EscortStance.Persistent, escort.EscortState.Stance);
                Assert.AreEqual(EscortState.PersistentRetreatFraction, escort.EscortState.RetreatThresholdFraction, 0.0001f,
                    "the stance must move the real threshold, not just a label");

                h.Page.CycleEscortStanceForTest();
                Assert.AreEqual(EscortStance.Safe, escort.EscortState.Stance);
                Assert.AreEqual(EscortState.SafeRetreatFraction, escort.EscortState.RetreatThresholdFraction, 0.0001f);
            }
        }

        /// <summary>An unassigned Escort has no stance to change, and the rail says so rather than doing nothing.</summary>
        [Test]
        public void TheEscortStanceControlRefusesBeforeAnyAssignment()
        {
            using (var h = new FleetHarness(system))
            {
                VehicleAgent escort = h.BuildAndSelect(VehicleClass.Escort);
                Assert.IsNull(escort.EscortState);

                h.Page.CycleEscortStanceForTest();
                Assert.AreEqual("ÖNCE BİR KORUMA HEDEFİ ATAYIN", h.Page.StatusText);
            }
        }

        /// <summary>
        /// <c>InterventionGroupDirector.AddMember</c> and <c>SetPolicy</c> had no
        /// caller at all, and <c>TickInterventionGroup</c> returns on its first line
        /// while the group is empty or Manual — so the whole VS-015 §6 mechanic was
        /// dormant no matter what the player did.
        /// </summary>
        [Test]
        public void TheInterventionGroupControlsFillAndPolicyTheRealGroup()
        {
            using (var h = new FleetHarness(system))
            {
                VehicleAgent engineer = h.BuildAndSelect(VehicleClass.Engineer);

                h.Page.ToggleGroupMembershipForTest();
                Assert.IsNotNull(h.Intervention.Group, "no group was created for the ship's own system");
                CollectionAssert.Contains(h.Intervention.Group.MemberVehicleIds, engineer.VehicleId);
                Assert.IsFalse(h.Intervention.Group.IsEmpty,
                    "an empty group is exactly what made TickInterventionGroup a no-op");

                Assert.AreEqual(Nebula.Combat.InterventionPolicy.Manual, h.Intervention.Group.Policy);
                h.Page.CycleGroupPolicyForTest();
                Assert.AreEqual(Nebula.Combat.InterventionPolicy.DefendInPlace, h.Intervention.Group.Policy);
                h.Page.CycleGroupPolicyForTest();
                Assert.AreEqual(Nebula.Combat.InterventionPolicy.SystemIntervention, h.Intervention.Group.Policy);

                h.Page.ToggleGroupMembershipForTest();
                CollectionAssert.DoesNotContain(h.Intervention.Group.MemberVehicleIds, engineer.VehicleId,
                    "membership must be reversible from the same control");
            }
        }

        /// <summary>
        /// <c>MaintenanceReserve.SetPolicy</c>'s only production caller was the save
        /// restore path, so every reserve stayed Manual and the automatic Engineer
        /// dispatch it drives never ran. The control belongs on the panel that already
        /// owns the reserve — and that panel only appears with the ship physically at
        /// the Command Center, which is the gate the design already chose.
        /// </summary>
        [Test]
        public void TheReservePanelCyclesTheRealMaintenancePolicy()
        {
            using (var h = new FleetHarness(system))
            {
                SurfaceStructure command = h.CommandStructure();
                NebulaHudPlanetContext panel = h.ReservePanel();

                Assert.IsTrue(panel.ReserveVisible, "the ship must really be at the Command Center");
                Assert.AreEqual(MaintenancePolicy.Manual, command.MaintenanceReserve.Policy);
                Assert.AreEqual("POLİTİKA: ELLE", panel.MaintenancePolicyText);

                panel.CycleMaintenancePolicyForTest();
                Assert.AreEqual(MaintenancePolicy.Emergency, command.MaintenanceReserve.Policy);
                Assert.AreEqual("POLİTİKA: ACİL", panel.MaintenancePolicyText);

                panel.CycleMaintenancePolicyForTest();
                Assert.AreEqual(MaintenancePolicy.FullField, command.MaintenanceReserve.Policy);

                panel.CycleMaintenancePolicyForTest();
                Assert.AreEqual(MaintenancePolicy.Manual, command.MaintenanceReserve.Policy,
                    "the control must cycle, not dead-end on the last policy");
            }
        }

        /// <summary>
        /// Command capacity is spent at launch, not at build, and the produced vehicle
        /// must really consume its catalog cost from the ledger the page displays.
        /// </summary>
        [Test]
        public void AProducedVehicleSpendsItsCommandCostOnlyWhenLaunched()
        {
            using (var h = new FleetHarness(system))
            {
                h.Build(VehicleClass.Escort, 1, VehicleCatalog.MetalAlloyId);
                h.FinishJob();

                Assert.AreEqual(0, h.Hangar.Command.Used,
                    "producing a vehicle must not consume a command slot on its own");
                Assert.AreEqual(2, h.Hangar.Vehicles[0].Def.CommandCost,
                    "the Escort's real command cost comes from the catalog");
                Assert.Greater(h.Hangar.Command.Capacity, 0,
                    "a Hangar T2 ledger has real capacity to spend");
            }
        }
    }
}
