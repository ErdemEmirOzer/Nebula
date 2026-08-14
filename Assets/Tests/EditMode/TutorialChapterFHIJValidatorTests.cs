using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Tutorial;
using Nebula.Tutorial.Steps;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Chapters F, H, I and J -- every step, three ways.
    /// <para>
    /// The three <c>[TestCaseSource]</c> methods below are the contract the
    /// tutorial design asks for, applied without exception: <b>each</b> step
    /// completes when the real command seam is driven, does <b>not</b> complete
    /// when a wrong or refused action is taken instead, and is <b>not</b>
    /// already complete the instant its own <c>Prepare</c> has run. A green
    /// step is one that passed all three, so a validator that quietly asserts
    /// nothing cannot hide here: the negative case would pass, but the positive
    /// one would fail, and a validator that completes on its own preparation
    /// fails the third.
    /// </para>
    /// <para>
    /// Everything is real. The harness composes the project's own
    /// <c>ShipController</c>/<c>ShipLogistics</c>/<c>ShipCombat</c>/<c>ShipHangar</c>,
    /// a real <c>CombatEncounterDirector</c>, a real procedurally generated
    /// system manifest, and -- where a step needs one -- a real
    /// <c>PlanetIdentity</c> with a real <c>CelestialBody</c>, real
    /// <c>SurfaceStructure</c>s and a real <c>DrillStructure</c>. No mock, no
    /// stub, and no fabricated flag: a disabled vehicle is disabled because it
    /// really lost its hull, and a stripped shield is stripped because damage
    /// really went through <c>DamageResolver</c>.
    /// </para>
    /// </summary>
    public class TutorialChapterFHIJValidatorTests
    {
        private const int Seed = 987654;

        // =====================================================================
        // Harness
        // =====================================================================

        /// <summary>
        /// One disposable slice of the real game: registries reset, a real
        /// system resolved from the real manifest generator, a real ship, and a
        /// real encounter director -- plus a tutorial step scope and a
        /// <see cref="NebulaContext"/> carrying exactly the four services a
        /// chapter F/H/I/J step is allowed to read. Presentation, Telemetry,
        /// Audio and the audio bridge are deliberately left unset, matching the
        /// tutorial session, so a step that reached for one would throw here.
        /// </summary>
        public sealed class Harness : IDisposable
        {
            internal NebulaContext Gameplay { get; }
            internal ShipController Ship { get; }
            internal ShipHangar Hangar { get; }
            internal ShipCombat Combat { get; }
            internal ShipLogistics Logistics { get; }
            internal InterventionGroupDirector Intervention { get; }
            internal CombatEncounterDirector Director { get; }
            internal SystemManifest System { get; }
            internal string SystemId => System.SystemId;
            internal string WorldId { get; }
            internal PlanetIdentity Planet { get; private set; }
            internal TutorialStepScope Scope { get; private set; }
            internal TutorialStepContext Context { get; private set; }

            internal LogisticsCargo Cargo => Logistics.Cargo;

            private readonly List<GameObject> owned = new List<GameObject>();
            private readonly GameObject star;

            internal Harness()
            {
                ProgressionService.Reset();
                ExtractionState.Reset(Seed);
                LogisticsState.Reset();
                CombatCampaignState.Reset();
                EnemyAgent.ClearRegistry();
                VehicleAgent.Registry.Clear();
                StarSystemRegistry.Initialize(unchecked((ulong)Seed));

                SectorManifest sector = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
                System = sector.Systems.First(s => s.Planets.Count > 0);
                StarSystemRegistry.SetStartSystem(System.Sector, System.SystemId);
                WorldId = System.Planets[0].PlanetId;

                star = new GameObject("Tutorial Test Star");
                star.transform.position = System.AbsoluteCenter;
                owned.Add(star);

                var shipObject = new GameObject("Tutorial Test Ship");
                shipObject.transform.position = System.AbsoluteCenter;
                owned.Add(shipObject);
                Ship = shipObject.AddComponent<ShipController>();
                Logistics = shipObject.AddComponent<ShipLogistics>();
                Combat = shipObject.AddComponent<ShipCombat>();
                Hangar = shipObject.AddComponent<ShipHangar>();

                // EditMode runs no Awake on a runtime-added component, so every
                // field those Awakes set is still null and the three
                // InitializeNewCampaign calls below throw on their first line.
                Nebula.Tutorial.Steps.TutorialAwake.WakeAll(shipObject);

                Logistics.InitializeNewCampaign();
                Combat.InitializeNewCampaign();
                Hangar.InitializeNewCampaign();

                Director = CombatEncounterDirector.Create(System.SystemId, star.transform, Ship, unchecked((ulong)Seed));
                owned.Add(Director.gameObject);
                Combat.SetDirector(Director);

                var interventionObject = new GameObject("Tutorial Test Intervention");
                owned.Add(interventionObject);
                Intervention = interventionObject.AddComponent<InterventionGroupDirector>();
                Intervention.Initialize(Hangar);

                Gameplay = new NebulaContext();
                Gameplay.SetShip(Ship);
                Gameplay.SetHangar(Hangar);
                Gameplay.SetShipCombat(Combat);
                Gameplay.SetInterventionGroupDirector(Intervention);
            }

            internal void Begin(string stepId)
            {
                Scope = TutorialStepScope.Create(stepId, null);
                Context = new TutorialStepContext(Gameplay, Scope, TutorialRunMode.Standalone,
                    new TutorialWorldFactory(() => Scope, Gameplay));
            }

            // ---- real world objects, built through real seams ----------------

            /// <summary>A real materialized planet: a real <see cref="CelestialBody"/> and the real <see cref="PlanetIdentity"/> registration the streaming path performs.</summary>
            internal PlanetIdentity MaterializePlanet()
            {
                if (Planet != null) return Planet;
                var planetObject = new GameObject("Tutorial Test Planet");
                planetObject.transform.position = System.AbsoluteCenter + new Vector3(4000f, 0f, 0f);
                owned.Add(planetObject);
                CelestialBody body = planetObject.AddComponent<CelestialBody>();
                body.Configure(CelestialBodyType.Planet, 300f, 1f, false);
                Planet = planetObject.AddComponent<PlanetIdentity>();
                Planet.Initialize(WorldId, body, true);
                return Planet;
            }

            internal PlanetProfile Profile() => ExtractionState.GetProfile(MaterializePlanet());

            /// <summary>A real active surface structure, restored straight into <see cref="DrillLifecycle.Active"/> the way a loaded save does. Idempotent: one of each type per planet.</summary>
            internal SurfaceStructure Structure(SurfaceStructureType type, int tier = 1)
            {
                SurfaceStructure existing = ExtractionState.FindStructure(WorldId, type);
                if (existing != null) return existing;
                PlanetIdentity planet = MaterializePlanet();
                PlanetProfile profile = Profile();
                SurfaceStructureDef def = SurfaceStructureCatalog.Get(type);
                SurfaceNode node = NodeFor(profile, type);
                var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, startFraction: 1f);
                durability.RestoreFromSave(def.ShieldMax, def.ArmorMax, def.HullMax, 0f);
                var runtime = new StructureRuntime(def, DrillLifecycle.Active, 0f, 0f, durability, tier);
                SurfaceStructure structure = SurfaceStructure.Restore(planet, profile, def, node, runtime, Cargo,
                    new List<(string itemId, float amount)>());
                owned.Add(structure.gameObject);
                return structure;
            }

            private static SurfaceNode NodeFor(PlanetProfile profile, SurfaceStructureType type) => type switch
            {
                SurfaceStructureType.Command => profile.CommandNode,
                SurfaceStructureType.Laser => profile.DefenseNode(0),
                SurfaceStructureType.Plasma => profile.DefenseNode(1),
                SurfaceStructureType.SensorArray => profile.SupportNode(0),
                _ => profile.SupportNode(1)
            };

            /// <summary>A real active drill on the real planet, with a real stored amount. Idempotent.</summary>
            internal DrillStructure Drill(float stored = 40f)
            {
                DrillStructure existing = ExtractionState.Drills.FirstOrDefault(d => d != null && d.WorldId == WorldId);
                if (existing != null) return existing;
                PlanetIdentity planet = MaterializePlanet();
                PlanetProfile profile = Profile();
                var runtime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, stored, 0f);
                DrillStructure drill = DrillStructure.Restore(planet, profile, runtime, DrillStructure.BuildSource.Parts, 0);
                owned.Add(drill.gameObject);
                return drill;
            }

            /// <summary>Everything the four dominance conditions need, all of it real.</summary>
            internal void DominanceStructures()
            {
                Drill();
                Structure(SurfaceStructureType.Command);
                Structure(SurfaceStructureType.Laser);
            }

            internal void RemoveStructures(SurfaceStructureType type)
            {
                foreach (SurfaceStructure s in ExtractionState.Structures.Where(s => s != null && s.Type == type).ToList())
                    UnityEngine.Object.DestroyImmediate(s.gameObject);
            }

            internal void ClearEnemies()
            {
                foreach (EnemyAgent agent in EnemyAgent.All.Values.ToList())
                    if (agent != null) UnityEngine.Object.DestroyImmediate(agent.gameObject);
                EnemyAgent.ClearRegistry();
            }

            // ---- readings the cases share -----------------------------------

            internal EnemyAgent AnyEnemy(string defId = null) =>
                EnemyAgent.All.Values.FirstOrDefault(a => a != null && !a.Destroyed
                    && (defId == null || (a.Def != null && a.Def.Id == defId)));

            internal IEnumerable<EnemyAgent> Enemies(string defId) =>
                EnemyAgent.All.Values.Where(a => a != null && !a.Destroyed
                    && (defId == null || (a.Def != null && a.Def.Id == defId))).ToList();

            internal VehicleAgent Vehicle(VehicleClass vehicleClass, int mk = 0) =>
                Hangar.Vehicles.FirstOrDefault(v => v != null && v.Def != null && v.Def.Class == vehicleClass
                                                    && (mk == 0 || v.Def.Mk == mk));

            internal PlanetDominance Dominance() => ExtractionState.GetDominance(WorldId);

            internal DominanceConditions Conditions()
            {
                PlanetProfile profile = Profile();
                return new DominanceConditions
                {
                    Scanned = profile.Scanned,
                    ActiveDrill = ExtractionState.HasActiveDrill(WorldId),
                    ActiveCommand = ExtractionState.HasActiveCommand(WorldId),
                    ActiveDefense = ExtractionState.ActiveDefenseCount(WorldId) > 0,
                    CommandDisabled = ExtractionState.CommandDisabled(WorldId)
                };
            }

            internal void TickDominance(float seconds, float step = 5f)
            {
                PlanetDominance dominance = Dominance();
                for (float t = 0f; t < seconds; t += step) dominance.Tick(step, Conditions());
            }

            internal CollectorRoute CreateRoute(VehicleAgent collector)
            {
                var route = new CollectorRoute(LogisticsState.NextRouteId(), SystemId, WorldId,
                    collector.VehicleId, RoutePriority.Normal);
                LogisticsState.AddRoute(route);
                collector.AssignRoute(route);
                return route;
            }

            internal ProjectileObject SpawnProjectile(ProjectileKind kind, ProjectileFaction faction,
                float damage = 100f, float interceptionDurability = 0f)
            {
                ProjectileObject projectile = ProjectileObject.Spawn(kind, 1, "tutorial-test",
                    Ship.transform.position + new Vector3(0f, 0f, 300f), Vector3.forward * 100f, 10f, null,
                    Director, faction, interceptionDurability > 0f, interceptionDurability, damage, "ship");
                owned.Add(projectile.gameObject);
                return projectile;
            }

            public void Dispose()
            {
                Scope?.Dispose();
                ClearEnemies();
                foreach (DrillStructure drill in ExtractionState.Drills.Where(d => d != null).ToList())
                    UnityEngine.Object.DestroyImmediate(drill.gameObject);
                foreach (SurfaceStructure structure in ExtractionState.Structures.Where(s => s != null).ToList())
                    UnityEngine.Object.DestroyImmediate(structure.gameObject);
                foreach (VehicleAgent vehicle in Hangar.Vehicles.Where(v => v != null).ToList())
                    UnityEngine.Object.DestroyImmediate(vehicle.gameObject);
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
                owned.Clear();

                VehicleAgent.Registry.Clear();
                EnemyAgent.ClearRegistry();
                LogisticsState.Reset();
                ExtractionState.Reset(0);
                StarSystemRegistry.Clear();
                CombatCampaignState.Reset();
                ProgressionService.Reset();
            }
        }

        // =====================================================================
        // Case table
        // =====================================================================

        /// <summary>One step, and the three things a test needs to say about it.</summary>
        public sealed class StepCase
        {
            public string StepId;
            /// <summary>World setup the step's own Prepare cannot do (a structure, a drill). Runs before Prepare.</summary>
            public Action<Harness> Arrange;
            /// <summary>The player's real action, through the real command seam.</summary>
            public Action<Harness> Drive;
            /// <summary>
            /// The same, for a step whose lesson is a <i>transition</i> rather than a
            /// resting state. The second argument runs one real <c>Evaluate</c> -- one
            /// director frame -- so the drive can be observed while it happens. H-10 is
            /// the only case that needs it: once the overheat lockout expires,
            /// <c>HardpointState</c> resets Heat to 0 and Overheated to false, leaving
            /// it indistinguishable from a hardpoint that never overheated, so nothing
            /// polled only at the end could ever see it.
            /// </summary>
            public Action<Harness, Action> DrivePolled;
            /// <summary>Something wrong, refused, or simply not the asked-for action.</summary>
            public Action<Harness> WrongMove;
            public override string ToString() => StepId;
        }

        private static readonly Dictionary<string, TutorialStepDefinition> Definitions = BuildDefinitions();

        private static Dictionary<string, TutorialStepDefinition> BuildDefinitions()
        {
            var map = new Dictionary<string, TutorialStepDefinition>(StringComparer.Ordinal);
            foreach (TutorialStepDefinition step in ChapterFSteps.Build()
                         .Concat(ChapterHSteps.Build())
                         .Concat(ChapterISteps.Build())
                         .Concat(ChapterJSteps.Build()))
                map[step.StepId] = step;
            return map;
        }

        /// <summary>Steps whose only honest drive needs real frames; they live in the PlayMode fixture.</summary>
        private static readonly string[] PlayModeOnly = { "F-04", "F-18", "F-20", "H-18", "H-21", "I-20" };

        public static IEnumerable<StepCase> Cases()
        {
            foreach (StepCase c in ChapterFCases()) yield return c;
            foreach (StepCase c in ChapterHCases()) yield return c;
            foreach (StepCase c in ChapterICases()) yield return c;
            foreach (StepCase c in ChapterJCases()) yield return c;
        }

        // ---- chapter F --------------------------------------------------------

        private static IEnumerable<StepCase> ChapterFCases()
        {
            yield return new StepCase
            {
                StepId = "F-01",
                Drive = h => h.Hangar.ApplyHangarTier(1),
                WrongMove = h => h.Hangar.ApplyHangarTier(0)
            };
            yield return new StepCase
            {
                StepId = "F-02",
                Drive = h => Assert.AreEqual(HangarProductionRejectReason.None,
                    h.Hangar.TryStartProduction(VehicleClass.Scout, VehicleCatalog.MetalAlloyId)),
                // A build ordered with no material chosen. Driven at the service,
                // which is where the refusal lives and where the step's validator
                // reads it -- the Fleet page's picker now chooses a real material
                // before it calls this, so the page can no longer produce the
                // refusal and is the wrong place to ask for it.
                WrongMove = h => Assert.AreEqual(HangarProductionRejectReason.MaterialNotSelected,
                    h.Hangar.TryStartProduction(VehicleClass.Scout, null))
            };
            yield return new StepCase
            {
                StepId = "F-03",
                Drive = h => Assert.AreEqual(ShipHangar.LaunchRejectReason.None,
                    h.Hangar.TryLaunch(h.Vehicle(VehicleClass.Scout), MissionKind.Scout, h.WorldId)),
                WrongMove = h => Assert.AreEqual(ShipHangar.LaunchRejectReason.TargetNotInShipSystem,
                    h.Hangar.TryLaunch(h.Vehicle(VehicleClass.Scout), MissionKind.Scout, "sector.0.0.0/system.999/planet.0"))
            };
            yield return new StepCase
            {
                StepId = "F-05",
                Drive = h => Assert.AreEqual(HangarProductionRejectReason.None,
                    h.Hangar.TryStartProduction(VehicleClass.Collector, VehicleCatalog.MetalAlloyId)),
                WrongMove = h => Assert.AreEqual(HangarProductionRejectReason.MaterialNotSelected,
                    h.Hangar.TryStartProduction(VehicleClass.Collector, null))
            };
            yield return new StepCase
            {
                StepId = "F-06",
                Arrange = h => h.Drill(),
                Drive = h => h.CreateRoute(h.Vehicle(VehicleClass.Collector)),
                // The real service check, with no active Logistics Center to route to.
                WrongMove = h => Assert.AreNotEqual(RouteCreateRejectReason.None,
                    h.Hangar.TryCreateRoute(h.Vehicle(VehicleClass.Collector), h.Drill(), null, RoutePriority.Normal))
            };
            yield return new StepCase
            {
                StepId = "F-07",
                Drive = h => Assert.AreEqual(HangarUpgradeRejectReason.None,
                    h.Hangar.TryStartVehicleUpgrade(h.Vehicle(VehicleClass.Collector, 1))),
                WrongMove = h =>
                {
                    VehicleAgent collector = h.Vehicle(VehicleClass.Collector, 1);
                    h.Hangar.TryLaunch(collector, MissionKind.Collector, h.WorldId);
                    Assert.AreEqual(HangarUpgradeRejectReason.VehicleNotInHangar,
                        h.Hangar.TryStartVehicleUpgrade(collector));
                }
            };
            yield return new StepCase
            {
                StepId = "F-08",
                Arrange = h => h.Structure(SurfaceStructureType.Command),
                Drive = h => Assert.AreEqual(ShipHangar.MaintenanceResupplyRejectReason.None,
                    h.Hangar.TryStartQuickResupply(h.Vehicle(VehicleClass.Collector, 2))),
                WrongMove = h =>
                {
                    h.RemoveStructures(SurfaceStructureType.Command);
                    Assert.AreNotEqual(ShipHangar.MaintenanceResupplyRejectReason.None,
                        h.Hangar.TryStartQuickResupply(h.Vehicle(VehicleClass.Collector, 2)));
                }
            };
            yield return new StepCase
            {
                StepId = "F-09",
                Drive = h => Assert.AreEqual(HangarProductionRejectReason.None,
                    h.Hangar.TryStartProduction(VehicleClass.Engineer, VehicleCatalog.MetalAlloyId)),
                WrongMove = h => Assert.AreEqual(HangarProductionRejectReason.MaterialNotSelected,
                    h.Hangar.TryStartProduction(VehicleClass.Engineer, null))
            };
            yield return new StepCase
            {
                StepId = "F-10",
                Arrange = h => DamagedCommand(h),
                Drive = h => Assert.AreEqual(ShipHangar.EngineerSendRejectReason.None,
                    h.Hangar.TrySendEngineer(h.Vehicle(VehicleClass.Engineer), EngineerTargetKind.Structure,
                        h.Structure(SurfaceStructureType.Command) != null
                            ? ExtractionState.FindStructure(h.WorldId, SurfaceStructureType.Command).TargetId : null,
                        EngineerSourceKind.ShipCargo, VehicleCatalog.MetalAlloyId)),
                WrongMove = h => Assert.AreEqual(ShipHangar.EngineerSendRejectReason.TargetHasNoDurability,
                    h.Hangar.TrySendEngineer(h.Vehicle(VehicleClass.Engineer), EngineerTargetKind.Drill,
                        h.Drill().WorldId, EngineerSourceKind.ShipCargo, VehicleCatalog.MetalAlloyId))
            };
            yield return new StepCase
            {
                StepId = "F-11",
                Arrange = h => DamagedCommand(h),
                Drive = h => Assert.IsTrue(EngineerTaskOf(h).TickSafety(EngineerMissionState.RequiredSafeSeconds + 0.5f, true)),
                WrongMove = h =>
                {
                    EngineerMissionState task = EngineerTaskOf(h);
                    task.TickSafety(4f, true);
                    task.RegisterThreatEvent();
                }
            };
            yield return new StepCase
            {
                StepId = "F-12",
                Arrange = h => DamagedCommand(h),
                Drive = h =>
                {
                    EngineerMissionState task = EngineerTaskOf(h);
                    task.TickSafety(EngineerMissionState.RequiredSafeSeconds + 0.5f, true);
                    Durability durability = ExtractionState.FindStructure(h.WorldId, SurfaceStructureType.Command).Runtime.Durability;
                    EngineerFieldWork.TickStructureRepair(durability, task, VehicleCatalog.EngineerMk1.RepairRatePerSecond,
                        EngineerRepairCatalog.StructureFieldCapFraction, h.Cargo,
                        "engineer-mission:" + task.MissionId, 2f);
                },
                WrongMove = h => EngineerTaskOf(h).TickSafety(EngineerMissionState.RequiredSafeSeconds + 0.5f, true)
            };
            yield return new StepCase
            {
                StepId = "F-13",
                Drive = h =>
                {
                    VehicleAgent collector = h.Vehicle(VehicleClass.Collector);
                    VehicleAgent engineer = h.Vehicle(VehicleClass.Engineer);
                    Assert.AreEqual(ShipHangar.EngineerSendRejectReason.None,
                        h.Hangar.TrySendEngineer(engineer, EngineerTargetKind.DisabledVehicle, collector.VehicleId,
                            EngineerSourceKind.ShipCargo, VehicleCatalog.MetalAlloyId));
                    EngineerMissionState task = engineer.EngineerTask;
                    for (int i = 0; i < 40 && collector.Stranded; i++)
                        EngineerFieldWork.TickVehicleRecovery(collector, task, 10f, h.Cargo,
                            "engineer-mission:" + task.MissionId);
                },
                WrongMove = h => h.Hangar.TrySendEngineer(h.Vehicle(VehicleClass.Engineer),
                    EngineerTargetKind.DisabledVehicle, h.Vehicle(VehicleClass.Collector).VehicleId,
                    EngineerSourceKind.ShipCargo, VehicleCatalog.MetalAlloyId)
            };
            yield return new StepCase
            {
                StepId = "F-14",
                Arrange = h => h.Drill(),
                Drive = h =>
                {
                    Assert.AreEqual(HangarProductionRejectReason.None,
                        h.Hangar.TryStartProduction(VehicleClass.Engineer, VehicleCatalog.MetalAlloyId));
                    h.Hangar.Production.Tick(VehicleCatalog.EngineerMk1.BuildSeconds + 1f);
                },
                WrongMove = h =>
                {
                    h.Hangar.TryStartProduction(VehicleClass.Scout, VehicleCatalog.MetalAlloyId);
                    h.Hangar.Production.Tick(VehicleCatalog.Scout.BuildSeconds + 1f);
                }
            };
            yield return new StepCase
            {
                StepId = "F-15",
                Drive = h => Assert.AreEqual(HangarProductionRejectReason.None,
                    h.Hangar.TryStartProduction(VehicleClass.Escort, VehicleCatalog.MetalAlloyId)),
                WrongMove = h => Assert.AreEqual(HangarProductionRejectReason.MaterialNotSelected,
                    h.Hangar.TryStartProduction(VehicleClass.Escort, null))
            };
            yield return new StepCase
            {
                StepId = "F-16",
                Drive = h => Assert.AreEqual(ShipHangar.EscortAssignRejectReason.None,
                    h.Hangar.TryAssignEscort(h.Vehicle(VehicleClass.Escort), "ship", TargetCategory.Ship)),
                WrongMove = h =>
                {
                    h.Hangar.TryStartProduction(VehicleClass.Scout, VehicleCatalog.MetalAlloyId);
                    h.Hangar.Production.Tick(VehicleCatalog.Scout.BuildSeconds + 1f);
                    Assert.AreEqual(ShipHangar.EscortAssignRejectReason.VehicleNotEligible,
                        h.Hangar.TryAssignEscort(h.Vehicle(VehicleClass.Scout), "ship", TargetCategory.Ship));
                }
            };
            yield return new StepCase
            {
                StepId = "F-17",
                Arrange = null,
                Drive = h => h.Hangar.SetEscortStance(AssignedEscort(h), EscortStance.Safe),
                WrongMove = h => h.Hangar.SetEscortStance(AssignedEscort(h), EscortStance.Balanced)
            };
            yield return new StepCase
            {
                StepId = "F-19",
                Drive = h =>
                {
                    VehicleAgent escort = h.Vehicle(VehicleClass.Escort);
                    Assert.IsTrue(h.Intervention.AddMember(escort.VehicleId));
                    h.Intervention.SetPolicy(InterventionPolicy.SystemIntervention);
                },
                WrongMove = h => h.Intervention.AddMember(h.Vehicle(VehicleClass.Escort).VehicleId)
            };
        }

        // ---- chapter H --------------------------------------------------------

        private static IEnumerable<StepCase> ChapterHCases()
        {
            yield return new StepCase
            {
                StepId = "H-01",
                Drive = h => Assert.IsTrue(h.Combat.TrySelectNearestTarget()),
                WrongMove = h => { h.ClearEnemies(); Assert.IsFalse(h.Combat.TrySelectNearestTarget()); }
            };
            yield return new StepCase
            {
                StepId = "H-02",
                Drive = h => h.Director.DefenseCoordinator.SetFocus(h.WorldId, h.AnyEnemy().Id),
                WrongMove = h => h.Director.DefenseCoordinator.SetFocus(h.WorldId, "no-such-enemy")
            };
            yield return new StepCase
            {
                StepId = "H-03",
                Drive = h => h.AnyEnemy().ApplyDamage(WeaponCatalog.LaserDamagePerTick(1, 1f), DamageProfile.Laser),
                WrongMove = h => h.Combat.State.TryConsumeEnergy(WeaponCatalog.LaserBaseEnergyPerSecond)
            };
            yield return new StepCase
            {
                StepId = "H-04",
                Drive = h => h.SpawnProjectile(ProjectileKind.Plasma, ProjectileFaction.Player),
                WrongMove = h => h.SpawnProjectile(ProjectileKind.EnemySiege, ProjectileFaction.Hostile)
            };
            yield return new StepCase
            {
                StepId = "H-05",
                Drive = h =>
                {
                    EnemyAgent enemy = h.AnyEnemy();
                    Assert.IsTrue(enemy.TryDisrupt(WeaponCatalog.EmDisruptSeconds));
                    enemy.ApplyDamage(WeaponCatalog.EmDamagePerShot(1), DamageProfile.Em);
                },
                WrongMove = h => h.AnyEnemy().ApplyDamage(10f, DamageProfile.Laser)
            };
            yield return new StepCase
            {
                StepId = "H-06",
                Drive = h =>
                {
                    h.AnyEnemy().ApplyDamage(WeaponCatalog.LaserDamagePerTick(1, 1f), DamageProfile.Laser);
                    h.SpawnProjectile(ProjectileKind.Plasma, ProjectileFaction.Player);
                },
                WrongMove = h => h.SpawnProjectile(ProjectileKind.Plasma, ProjectileFaction.Player)
            };
            yield return new StepCase
            {
                StepId = "H-07",
                Drive = h =>
                {
                    float direct = WeaponCatalog.PlasmaDirectDamage(1);
                    foreach (EnemyAgent enemy in h.Enemies(EnemyIds.FighterMk1))
                        enemy.ApplyDamage(WeaponCatalog.PlasmaSplashDamageForDirect(direct, 60f), DamageProfile.Plasma);
                },
                WrongMove = h => h.Combat.State.TryConsumeEnergy(WeaponCatalog.PlasmaEnergyPerShot)
            };
            yield return new StepCase
            {
                StepId = "H-08",
                Drive = h => Assert.IsTrue(h.Combat.State.TryConsumeEnergy(WeaponCatalog.PlasmaEnergyPerShot)),
                WrongMove = h => h.Combat.State.TryConsumeEnergy(0f)
            };
            yield return new StepCase
            {
                StepId = "H-09",
                Drive = h => h.Combat.Hardpoints[0].AddHeat(WeaponCatalog.LaserHeatPerSecond * 0.5f),
                WrongMove = h => h.Combat.Hardpoints[0].Tick(1f, 0f)
            };
            yield return new StepCase
            {
                StepId = "H-10",
                DrivePolled = (h, frame) =>
                {
                    HardpointState hardpoint = h.Combat.Hardpoints[0];
                    hardpoint.AddHeat(WeaponCatalog.HardpointMaxHeat);
                    Assert.IsTrue(hardpoint.Overheated, "Reaching the heat cap must lock the hardpoint out.");
                    Assert.IsFalse(hardpoint.CanFire);
                    frame(); // the frame on which the player sees the lockout

                    // Real cooldown, real constants: OverheatLockoutSeconds is 3s, so
                    // 0.5s steps clear it well inside this loop without the loop's
                    // length being what makes the step pass.
                    for (int i = 0; i < 20 && hardpoint.Overheated; i++)
                    {
                        hardpoint.Tick(0.5f, 0f);
                        frame();
                    }
                    Assert.IsFalse(hardpoint.Overheated, "The lockout must clear on its own after the real cooldown.");
                    Assert.IsTrue(hardpoint.CanFire);
                },
                WrongMove = h => h.Combat.Hardpoints[0].AddHeat(WeaponCatalog.HardpointMaxHeat * 0.5f)
            };
            yield return new StepCase
            {
                StepId = "H-11",
                Drive = h => h.Ship.transform.rotation = Quaternion.Euler(0f, 90f, 0f),
                WrongMove = h => h.Ship.transform.rotation = Quaternion.Euler(0f, 5f, 0f)
            };
            yield return new StepCase
            {
                StepId = "H-12",
                Drive = h => h.Ship.transform.position -= h.Ship.transform.forward * (WeaponCatalog.LaserBaseRange * 2f),
                WrongMove = h => h.Ship.transform.position -= h.Ship.transform.forward * 50f
            };
            yield return new StepCase
            {
                StepId = "H-13",
                Drive = h => InterceptableProjectile(h).ApplyInterceptionHit(ProjectileCatalog.InterceptionDurability),
                WrongMove = h => InterceptableProjectile(h).ApplyInterceptionHit(1f)
            };
            yield return new StepCase
            {
                StepId = "H-14",
                Drive = h => h.AnyEnemy().ApplyDamage(20f, DamageProfile.Laser),
                WrongMove = h => h.Combat.State.TryConsumeEnergy(WeaponCatalog.LaserBaseEnergyPerSecond)
            };
            yield return new StepCase
            {
                StepId = "H-15",
                Drive = h => h.AnyEnemy().ApplyDamage(20f, DamageProfile.Plasma),
                WrongMove = h => h.Combat.State.TryConsumeEnergy(WeaponCatalog.PlasmaEnergyPerShot)
            };
            yield return new StepCase
            {
                StepId = "H-16",
                Drive = h => h.AnyEnemy().ApplyDamage(20f, DamageProfile.Laser),
                WrongMove = h => h.Combat.State.TryConsumeEnergy(WeaponCatalog.LaserBaseEnergyPerSecond)
            };
            yield return new StepCase
            {
                StepId = "H-17",
                Drive = h => h.Combat.ApplyDamage(1_000_000f, DamageProfile.Neutral),
                WrongMove = h => h.Combat.ApplyDamage(1f, DamageProfile.Neutral)
            };
            yield return new StepCase
            {
                StepId = "H-19",
                Drive = h => h.AnyEnemy().ApplyDamage(1_000_000f, DamageProfile.Neutral),
                WrongMove = h => h.AnyEnemy().ApplyDamage(1f, DamageProfile.Neutral)
            };
            yield return new StepCase
            {
                StepId = "H-20",
                Drive = h =>
                {
                    CollectionAssert.IsNotEmpty(h.Director.Wreckage, "Prepare should have produced real raid salvage.");
                    CombatWreckageState wreck = h.Director.Wreckage.Last();
                    CollectionAssert.IsNotEmpty(wreck.Nodes, "Real salvage always carries at least one item node.");
                    Assert.Greater(wreck.Withdraw(wreck.Nodes[0].ItemId, 1f), 0f);
                },
                WrongMove = h => h.Director.Wreckage.Last().Tick(1f)
            };
        }

        // ---- chapter I --------------------------------------------------------

        private static IEnumerable<StepCase> ChapterICases()
        {
            yield return new StepCase
            {
                StepId = "I-01",
                Drive = h => h.AnyEnemy(EnemyIds.ScoutProbeMk1).Ai
                    .TickScan(EnemyCatalog.ScoutProbeMk1.ScanSeconds + 1f, true),
                WrongMove = h => h.AnyEnemy(EnemyIds.ScoutProbeMk1).Ai.TickScan(120f, false)
            };
            yield return new StepCase
            {
                StepId = "I-02",
                Drive = h => h.Combat.ApplyDamage(EnemyCatalog.FighterMk1.Dps),
                WrongMove = h => h.AnyEnemy(EnemyIds.FighterMk1).ApplyDamage(1f, DamageProfile.Neutral)
            };
            yield return new StepCase
            {
                StepId = "I-03",
                Arrange = h => h.Drill(),
                Drive = h => Assert.Greater(LooterCargoService.StealFromDrill(
                    h.AnyEnemy(EnemyIds.LooterTransportMk1).LooterCargo, h.Drill(),
                    EnemyCatalog.LooterTransferVolumePerSecond, 2f), 0f),
                WrongMove = h => LooterCargoService.StealFromDrill(
                    h.AnyEnemy(EnemyIds.LooterTransportMk1).LooterCargo, h.Drill(),
                    EnemyCatalog.LooterTransferVolumePerSecond, 0f)
            };
            yield return new StepCase
            {
                StepId = "I-04",
                Drive = h => Assert.IsTrue(h.Combat.State.TryDisrupt(
                    EnemyCatalog.EmDisruptorMk1.DisruptSeconds, EnemyCatalog.EmDisruptorMk1.DisruptImmunitySeconds)),
                WrongMove = h => h.AnyEnemy(EnemyIds.EmDisruptorMk1).TryDisrupt(2f)
            };
            yield return new StepCase
            {
                StepId = "I-05",
                Arrange = h => h.Structure(SurfaceStructureType.Laser),
                Drive = h => ExtractionState.FindStructure(h.WorldId, SurfaceStructureType.Laser)
                    .ApplyDamage(EnemyCatalog.HeavyAssaultMk1.Dps, DamageProfile.Neutral),
                WrongMove = h => h.Combat.ApplyDamage(EnemyCatalog.HeavyAssaultMk1.Dps)
            };
            yield return new StepCase
            {
                StepId = "I-06",
                Drive = h => h.SpawnProjectile(ProjectileKind.EnemySiege, ProjectileFaction.Hostile,
                    EnemyCatalog.SiegeMk1.Dps * 2f, ProjectileCatalog.InterceptionDurability),
                WrongMove = h => h.SpawnProjectile(ProjectileKind.Plasma, ProjectileFaction.Player)
            };
            yield return new StepCase
            {
                StepId = "I-07",
                // I-07 is an InformationAcknowledgement: the raid clock is entirely
                // the scheduler's and there is no command the player can give. So
                // this "drive" is the world's own transition, and what it proves is
                // that the validator really observes it -- that there is something
                // to read. The player's half of the step is the confirmation, and
                // the director's refusal to finish it without one is asserted in
                // TutorialLifecycleTests and TutorialAcknowledgementTests.
                Drive = h => RaidScheduler.Tick(h.Director, 0f),
                WrongMove = h => h.Director.Pressure.TickPressure(60f, 3f, 1f)
            };
            yield return new StepCase
            {
                StepId = "I-08",
                Drive = h => h.Director.Pressure.TickPressure(120f, 3f, 1f),
                WrongMove = h =>
                {
                    h.Director.Pressure.StartGrace(5f);
                    h.Director.Pressure.TickPressure(120f, 3f, 1f);
                }
            };
            yield return new StepCase
            {
                StepId = "I-09",
                Drive = h => Assert.AreEqual(ShipHangar.LaunchRejectReason.None,
                    h.Hangar.TryLaunch(h.Vehicle(VehicleClass.Scout), MissionKind.Scout, h.WorldId)),
                WrongMove = h => h.Hangar.TryLaunch(h.Vehicle(VehicleClass.Scout), MissionKind.Scout,
                    "sector.0.0.0/system.999/planet.0")
            };
            yield return new StepCase
            {
                StepId = "I-10",
                Arrange = h => h.MaterializePlanet(),
                Drive = h => { for (int i = 0; i < 6; i++) RaidScheduler.Tick(h.Director, 200f); },
                WrongMove = h => RaidScheduler.Tick(h.Director, 0f)
            };
            yield return new StepCase
            {
                StepId = "I-11",
                Drive = h =>
                {
                    EnemyAgent fighter = h.AnyEnemy(EnemyIds.FighterMk1);
                    fighter.ApplyDamage(fighter.Combat.ShieldMax + fighter.Combat.ArmorMax
                                        + fighter.Combat.HullMax * 0.85f, DamageProfile.Neutral);
                    for (int i = 0; i < 4; i++) fighter.Ai.MakeDecision(700f, false, true);
                },
                WrongMove = h =>
                {
                    EnemyAgent fighter = h.AnyEnemy(EnemyIds.FighterMk1);
                    for (int i = 0; i < 4; i++) fighter.Ai.MakeDecision(700f, false, false);
                }
            };
            yield return new StepCase
            {
                StepId = "I-12",
                Arrange = h => h.MaterializePlanet(),
                Drive = h =>
                {
                    // Warning -> Spawning -> Active, then the raiders are really
                    // destroyed, which is what lets TickActive close the raid out.
                    for (int i = 0; i < 3; i++) RaidScheduler.Tick(h.Director, 200f);
                    foreach (EnemyAgent raider in h.Enemies(null))
                        raider.ApplyDamage(1_000_000f, DamageProfile.Neutral);
                    for (int i = 0; i < 4; i++) RaidScheduler.Tick(h.Director, 200f);
                },
                WrongMove = h => RaidScheduler.Tick(h.Director, 0f)
            };
            yield return new StepCase
            {
                StepId = "I-13",
                Drive = h => h.Director.Wreckage.Add(RaidSalvageResolver.Resolve("tutorial/wreck.i13", 42UL,
                    RaidOutcome.DecisiveVictory, new List<string> { EnemyIds.FighterMk1 })),
                WrongMove = h => RaidSalvageResolver.Resolve("tutorial/wreck.i13", 42UL,
                    RaidOutcome.DecisiveVictory, new List<string> { EnemyIds.FighterMk1 })
            };
            yield return new StepCase
            {
                StepId = "I-14",
                Arrange = h => h.Structure(SurfaceStructureType.Laser),
                Drive = h => h.Structure(SurfaceStructureType.Command),
                WrongMove = h => h.Structure(SurfaceStructureType.SensorArray)
            };
            yield return new StepCase
            {
                StepId = "I-15",
                Arrange = h => h.Structure(SurfaceStructureType.Command),
                Drive = h => h.Director.DefenseCoordinator.CycleMode(h.WorldId),
                WrongMove = h => h.Director.DefenseCoordinator.SetMode(h.WorldId, DefenseMode.RolePriority)
            };
            yield return new StepCase
            {
                StepId = "I-16",
                Arrange = h => { h.Structure(SurfaceStructureType.Laser); h.Structure(SurfaceStructureType.Command); },
                Drive = h => h.Structure(SurfaceStructureType.Plasma),
                WrongMove = h => h.Structure(SurfaceStructureType.SensorArray)
            };
            yield return new StepCase
            {
                StepId = "I-17",
                Arrange = h => h.Structure(SurfaceStructureType.Laser),
                Drive = h => h.AnyEnemy(EnemyIds.FighterMk1)
                    .ApplyDamage(PlanetaryDefenseCatalog.LaserT1.Dps, DamageProfile.Laser),
                WrongMove = h => h.AnyEnemy(EnemyIds.FighterMk1).Ai.TickScan(1f, true)
            };
            yield return new StepCase
            {
                StepId = "I-18",
                Arrange = h => h.Structure(SurfaceStructureType.Plasma),
                Drive = h => h.SpawnProjectile(ProjectileKind.Plasma, ProjectileFaction.Player,
                    PlanetaryDefenseCatalog.PlasmaT1.Dps * ProjectileCatalog.PlanetaryPlasmaFireIntervalSeconds),
                WrongMove = h => h.SpawnProjectile(ProjectileKind.EnemySiege, ProjectileFaction.Hostile)
            };
            yield return new StepCase
            {
                StepId = "I-19",
                Drive = h => h.Director.PirateBase.ApplyDamage(1_000_000f, DamageProfile.Neutral),
                WrongMove = h => h.Director.PirateBase.ApplyDamage(1f, DamageProfile.Neutral)
            };
        }

        // ---- chapter J --------------------------------------------------------

        private static IEnumerable<StepCase> ChapterJCases()
        {
            yield return new StepCase
            {
                StepId = "J-01",
                Drive = h => h.DominanceStructures(),
                WrongMove = h => h.Structure(SurfaceStructureType.Command)
            };
            yield return new StepCase
            {
                StepId = "J-02",
                Arrange = h => h.DominanceStructures(),
                Drive = h => h.Dominance().Tick(1f, h.Conditions()),
                WrongMove = h => h.Dominance().Tick(1f, default)
            };
            yield return new StepCase
            {
                StepId = "J-03",
                Arrange = h => h.DominanceStructures(),
                Drive = h => h.TickDominance(PlanetDominance.StabilizeSeconds + 10f),
                WrongMove = h => h.TickDominance(30f)
            };
            yield return new StepCase
            {
                StepId = "J-04",
                Arrange = h => h.DominanceStructures(),
                Drive = h => { DisableCommand(h); h.TickDominance(2f, 1f); },
                WrongMove = h => h.TickDominance(2f, 1f)
            };
            yield return new StepCase
            {
                StepId = "J-05",
                Arrange = h => h.DominanceStructures(),
                Drive = h =>
                {
                    DisableCommand(h);
                    h.TickDominance(PlanetDominance.RecoverySeconds + 20f);
                },
                WrongMove = h => { DisableCommand(h); h.TickDominance(2f, 1f); }
            };
            yield return new StepCase
            {
                StepId = "J-06",
                Drive = h => h.Dominance().Reclaim(),
                WrongMove = h => h.Dominance().SetAbandoned(true)
            };
            yield return new StepCase
            {
                StepId = "J-07",
                Drive = h =>
                {
                    foreach (PlanetManifest planet in h.System.Planets)
                        ExtractionState.GetDominance(planet.PlanetId)
                            .Restore(DominanceState.Controlled, PlanetDominance.StabilizeSeconds, 0f);
                },
                WrongMove = h =>
                {
                    for (int i = 1; i < h.System.Planets.Count; i++)
                        ExtractionState.GetDominance(h.System.Planets[i].PlanetId)
                            .Restore(DominanceState.Controlled, PlanetDominance.StabilizeSeconds, 0f);
                }
            };
            yield return new StepCase
            {
                StepId = "J-08",
                Arrange = h => h.DominanceStructures(),
                Drive = h =>
                {
                    DisableCommand(h);
                    h.TickDominance(2f, 1f);
                    for (int i = 0; i < 6; i++) RaidScheduler.Tick(h.Director, 200f);
                },
                WrongMove = h => { for (int i = 0; i < 6; i++) RaidScheduler.Tick(h.Director, 200f); }
            };
            yield return new StepCase
            {
                StepId = "J-09",
                Drive = h =>
                {
                    CollectorRoute route = h.CreateRoute(h.Vehicle(VehicleClass.Collector));
                    route.SetWaitReason(RouteWaitReason.SourceDrillContested);
                },
                WrongMove = h =>
                {
                    CollectorRoute route = h.CreateRoute(h.Vehicle(VehicleClass.Collector));
                    route.SetWaitReason(RouteWaitReason.CenterFull);
                }
            };
            yield return new StepCase
            {
                StepId = "J-10",
                Drive = h =>
                {
                    h.Structure(SurfaceStructureType.SensorArray);
                    h.Structure(SurfaceStructureType.MaintenancePlatform);
                },
                WrongMove = h => h.Structure(SurfaceStructureType.SensorArray)
            };
        }

        // ---- case helpers -----------------------------------------------------

        private static void DamagedCommand(Harness h)
        {
            SurfaceStructure command = h.Structure(SurfaceStructureType.Command);
            Durability durability = command.Runtime.Durability;
            // Real damage, so the repair target is genuinely below its field cap.
            command.ApplyDamage((durability.ShieldMax + durability.ArmorMax) + durability.HullMax * 0.5f,
                DamageProfile.Neutral);
        }

        private static void DisableCommand(Harness h)
        {
            SurfaceStructure command = ExtractionState.FindStructure(h.WorldId, SurfaceStructureType.Command);
            Durability durability = command.Runtime.Durability;
            command.ApplyDamage((durability.ShieldMax + durability.ArmorMax + durability.HullMax) * 4f,
                DamageProfile.Neutral);
        }

        private static EngineerMissionState EngineerTaskOf(Harness h)
        {
            VehicleAgent engineer = h.Hangar.Vehicles.First(v => v != null && v.Def.Class == VehicleClass.Engineer
                                                                 && v.EngineerTask != null);
            return engineer.EngineerTask;
        }

        private static VehicleAgent AssignedEscort(Harness h) =>
            h.Hangar.Vehicles.First(v => v != null && v.Def.Class == VehicleClass.Escort);

        private static ProjectileState InterceptableProjectile(Harness h) =>
            h.Director.Projectiles.First(p => p != null && p.State != null && p.State.Interceptable).State;

        // =====================================================================
        // The three assertions, applied to every step
        // =====================================================================

        /// <summary>
        /// How many times a prepared step is polled with nothing happening between
        /// the polls.
        /// <para>
        /// One evaluation immediately after Prepare is too weak, and that weakness
        /// is what let reading steps ship: a validator can be false on the poll the
        /// step opens and true on the next one because it latches, because it arms
        /// itself on the first poll, or because its own objectives callback writes
        /// the state the completion test reads. EditMode has no frame loop, so this
        /// is what "several ticks" means here; the real-frame version of the same
        /// sweep runs over the whole catalog in <c>TutorialLifecycleTests</c>.
        /// </para>
        /// </summary>
        private const int IdlePolls = 8;

        /// <summary>
        /// Polls a prepared step with nothing happening in between. Returns the
        /// zero-based poll it completed on, or -1 when it stayed honest.
        /// </summary>
        private static int Idle(ITutorialStepRuntime runtime, TutorialStepReport report)
        {
            for (int poll = 0; poll < IdlePolls; poll++)
            {
                runtime.Evaluate(report);
                if (report.IsComplete) return poll;
            }
            return -1;
        }

        [TestCaseSource(nameof(Cases))]
        public void Step_CompletesOnTheRealPlayerAction_AndOnNothingBeforeIt(StepCase testCase)
        {
            using var harness = new Harness();
            TutorialStepDefinition step = Step(testCase.StepId);
            ITutorialStepRuntime runtime = step.CreateRuntime();
            var report = new TutorialStepReport();

            testCase.Arrange?.Invoke(harness);
            harness.Begin(step.StepId);
            runtime.Prepare(harness.Context);
            report.Reset(step.ChecklistTr.Count);

            // Idled first, in the same run: so "it completed" below can only mean
            // the command seam did it, and not the step finding its own way there.
            int completedOn = Idle(runtime, report);
            Assert.AreEqual(-1, completedOn,
                $"{testCase.StepId} completed on poll {completedOn + 1}, before its command seam was driven.");

            if (testCase.DrivePolled != null) testCase.DrivePolled(harness, () => runtime.Evaluate(report));
            else testCase.Drive(harness);
            runtime.Evaluate(report);

            Assert.IsTrue(report.IsComplete,
                $"{testCase.StepId} did not complete after the real command seam was driven. " +
                $"Feedback: {report.Feedback ?? "<none>"}");
            runtime.Cleanup();
        }

        [TestCaseSource(nameof(Cases))]
        public void Step_DoesNotCompleteOnTheWrongAction(StepCase testCase)
        {
            using var harness = new Harness();
            TutorialStepDefinition step = Step(testCase.StepId);
            ITutorialStepRuntime runtime = step.CreateRuntime();
            var report = new TutorialStepReport();

            testCase.Arrange?.Invoke(harness);
            harness.Begin(step.StepId);
            runtime.Prepare(harness.Context);
            report.Reset(step.ChecklistTr.Count);

            testCase.WrongMove(harness);
            runtime.Evaluate(report);

            Assert.IsFalse(report.IsComplete,
                $"{testCase.StepId} completed on an action that is not the one it asks for.");

            // And still refused a few polls later: accepted-on-the-next-poll is
            // accepted.
            int completedOn = Idle(runtime, report);
            Assert.AreEqual(-1, completedOn,
                $"{testCase.StepId} accepted the wrong action {completedOn + 1} poll(s) after it happened.");
            runtime.Cleanup();
        }

        [TestCaseSource(nameof(Cases))]
        public void Step_NeverCompletesWhileThePlayerDoesNothing(StepCase testCase)
        {
            using var harness = new Harness();
            TutorialStepDefinition step = Step(testCase.StepId);
            ITutorialStepRuntime runtime = step.CreateRuntime();
            var report = new TutorialStepReport();

            testCase.Arrange?.Invoke(harness);
            harness.Begin(step.StepId);
            runtime.Prepare(harness.Context);
            report.Reset(step.ChecklistTr.Count);

            int completedOn = Idle(runtime, report);
            Assert.AreEqual(-1, completedOn,
                $"{testCase.StepId} completed on poll {completedOn + 1} with the player doing nothing at all.");
            runtime.Cleanup();
        }

        private static TutorialStepDefinition Step(string stepId)
        {
            Assert.IsTrue(Definitions.ContainsKey(stepId), $"No step definition registered for '{stepId}'.");
            return Definitions[stepId];
        }

        // =====================================================================
        // Structural coverage
        // =====================================================================

        [Test]
        public void EveryStepInTheFourChaptersIsCoveredByACaseOrByThePlayModeFixture()
        {
            var covered = new HashSet<string>(Cases().Select(c => c.StepId), StringComparer.Ordinal);
            covered.UnionWith(PlayModeOnly);

            string[] missing = Definitions.Keys.Where(id => !covered.Contains(id)).OrderBy(id => id).ToArray();
            CollectionAssert.IsEmpty(missing, "Steps with no positive/negative/prepare coverage: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryCaseDeclaresExactlyOnePositiveDriveAndOneWrongMove()
        {
            // A case with neither drive would report as a green positive test while
            // doing nothing at all -- the exact shape of a test that asserts nothing.
            foreach (StepCase testCase in Cases())
            {
                bool hasDrive = testCase.Drive != null;
                bool hasPolledDrive = testCase.DrivePolled != null;
                Assert.IsTrue(hasDrive ^ hasPolledDrive,
                    $"{testCase.StepId} must declare exactly one of Drive / DrivePolled.");
                Assert.IsNotNull(testCase.WrongMove, $"{testCase.StepId} has no negative case.");
            }
        }

        [Test]
        public void TheFourChaptersRegisterExactlyTheCurriculumsStepCounts()
        {
            Assert.AreEqual(20, ChapterFSteps.Build().Count(), "Chapter F is steps 74-93.");
            Assert.AreEqual(21, ChapterHSteps.Build().Count(), "Chapter H is steps 113-133.");
            Assert.AreEqual(20, ChapterISteps.Build().Count(), "Chapter I is steps 134-153.");
            Assert.AreEqual(10, ChapterJSteps.Build().Count(), "Chapter J is steps 154-163 -- dominance only, no nuclear step.");
        }

        [Test]
        public void ChapterJContainsNoNuclearStep()
        {
            // There is no buildable nuclear structure, no purchasable nuclear
            // module and no producer for energy.nuclear/core.nuclear, so a
            // nuclear step could only teach something the game does not have.
            foreach (TutorialStepDefinition step in ChapterJSteps.Build())
            {
                StringAssert.DoesNotContain("Nükleer", step.TitleTr);
                StringAssert.DoesNotContain("nükleer", step.InstructionTr);
            }
        }

        [Test]
        public void EveryStepIdIsUniqueAndCarriesRealTurkishText()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TutorialStepDefinition step in Definitions.Values)
            {
                Assert.IsTrue(seen.Add(step.StepId), $"Duplicate step id '{step.StepId}'.");
                Assert.IsNotEmpty(step.TitleTr, step.StepId);
                Assert.IsNotEmpty(step.InstructionTr, step.StepId);
                Assert.IsNotEmpty(step.SummaryTr, step.StepId);
            }
        }

        // =====================================================================
        // The refusal vocabulary, read off real refusals
        // =====================================================================

        /// <summary>
        /// Reads what the card would actually show, by running the step against a
        /// world in the refusing state. This is the only honest way to test the
        /// Turkish table: restating it in the fixture would assert nothing.
        /// </summary>
        private static string FeedbackFor(string stepId, Action<Harness> arrange, Action<Harness> beforeEvaluate = null)
        {
            using var harness = new Harness();
            TutorialStepDefinition step = Step(stepId);
            ITutorialStepRuntime runtime = step.CreateRuntime();
            var report = new TutorialStepReport();

            arrange?.Invoke(harness);
            harness.Begin(step.StepId);
            runtime.Prepare(harness.Context);
            report.Reset(step.ChecklistTr.Count);
            beforeEvaluate?.Invoke(harness);
            runtime.Evaluate(report);
            runtime.Cleanup();

            Assert.AreEqual(TutorialFeedbackKind.Reject, report.FeedbackKind,
                $"{stepId} was expected to be reporting a refusal here.");
            return report.Feedback;
        }

        [Test]
        public void ClosedCommandCapacityIsRefusedWithTheHangarTierWording()
        {
            Assert.AreEqual("HANGAR SEVİYESİ YETERSİZ", FeedbackFor("F-01", null));
        }

        [Test]
        public void ABuildWithNoProcessedMaterialInCargoIsRefusedAsMaterialNotSelected()
        {
            // An empty cargo, so there is no special material to choose. The words
            // are production's own, switched on production's own enum -- which is
            // what keeps them right through an interface rewrite.
            string feedback = FeedbackFor("F-02", null, h => h.Cargo.Clear());
            Assert.AreEqual("MALZEME SEÇİLMEDİ", feedback);
        }

        [Test]
        public void AnOverheatedHardpointIsRefusedWithTheOverheatWording()
        {
            string feedback = FeedbackFor("H-10", null,
                h => h.Combat.Hardpoints[0].AddHeat(WeaponCatalog.HardpointMaxHeat));
            Assert.AreEqual("HARDPOINT AŞIRI ISINDI", feedback);
        }

        [Test]
        public void MissingDominanceConditionsAreReportedInTurkish()
        {
            Assert.AreEqual("HÂKİMİYET ŞARTLARI EKSİK", FeedbackFor("J-01", null));
        }

        [Test]
        public void ANonAbandonedPlanetUsesTheProductionsOwnReclaimRefusalText()
        {
            // J-06's own Prepare abandons the planet, so the refusal is produced by
            // reclaiming a planet that has no active Command/defence yet.
            string feedback = FeedbackFor("J-06", null);
            Assert.AreEqual(ShipPlanetEvacuation.RejectMessage(ReclaimRejectReason.DominanceConditionsNotMet), feedback);
        }

        [Test]
        public void AResupplyWithNoCommandCentreInSystemIsRefusedInTurkish()
        {
            Assert.AreEqual("SİSTEMDE AKTİF KOMUTA MERKEZİ YOK", FeedbackFor("F-08", null));
        }
    }
}
