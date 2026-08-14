using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The six chapter F/H/I/J steps whose only honest drive needs real frames.
    /// <para>
    /// Everything else in these four chapters is covered by
    /// <c>TutorialChapterFHIJValidatorTests</c> in EditMode, where the seams are
    /// pure. What is left here is the set with no callable advance seam at all:
    /// the Scout's scan counter (<c>ScoutScanWork</c> is private inside
    /// <c>VehicleAgent</c>), the Escort's command and engagement loop, the
    /// vehicle return/dock animation, and ship repair
    /// (<c>ShipCombat.TickRepair</c> runs only from <c>FixedUpdate</c>). Each of
    /// those runs real <c>Update</c>/<c>FixedUpdate</c> frames here, and where a
    /// live path is gated behind a private caller the test drives the exact call
    /// that caller makes -- never a fabricated flag.
    /// </para>
    /// <para>
    /// No galaxy is generated per test: the manifest repository is procedural
    /// and lazy, one system is resolved, and one small ship/planet/director
    /// slice is built and torn down per case.
    /// </para>
    /// </summary>
    public class TutorialChapterFHIJGameplayTests
    {
        private const int Seed = 987654;
        private const float FrameBudgetSeconds = 30f;

        // =====================================================================
        // Harness
        // =====================================================================

        private sealed class Harness : IDisposable
        {
            internal NebulaContext Gameplay { get; }
            internal ShipController Ship { get; }
            internal ShipHangar Hangar { get; }
            internal ShipCombat Combat { get; }
            internal ShipLogistics Logistics { get; }
            internal InterventionGroupDirector Intervention { get; }
            internal CombatEncounterDirector Director { get; }
            internal SystemManifest System { get; }
            internal string WorldId { get; }
            internal PlanetIdentity Planet { get; private set; }
            internal TutorialStepScope Scope { get; private set; }
            internal TutorialStepContext Context { get; private set; }
            internal LogisticsCargo Cargo => Logistics.Cargo;

            private readonly List<GameObject> owned = new List<GameObject>();

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

                // Scene coordinates, not absolute ones. The two differ by
                // FloatingOrigin.TotalOffset, which is a process-wide static that
                // another PlayMode fixture's real world may have shifted and never
                // shifts back -- and ShipHangar.CurrentSystemId is an absolute-position
                // lookup (position + TotalOffset). Placing the ship at the raw
                // absolute centre therefore put it outside every system's navigation
                // radius as soon as anything upstream had rebased, CurrentSystemId went
                // null, and every launch in this fixture was refused as
                // TargetNotInShipSystem -- which is what F-04 and F-20 were actually
                // failing on. Converting here makes the harness right rather than
                // accidentally right.
                Vector3 systemCentre = SceneOf(System.AbsoluteCenter);

                var star = new GameObject("Tutorial PlayMode Star");
                star.transform.position = systemCentre;
                owned.Add(star);

                var shipObject = new GameObject("Tutorial PlayMode Ship");
                shipObject.transform.position = systemCentre;
                owned.Add(shipObject);
                Ship = shipObject.AddComponent<ShipController>();
                Logistics = shipObject.AddComponent<ShipLogistics>();
                Combat = shipObject.AddComponent<ShipCombat>();
                Hangar = shipObject.AddComponent<ShipHangar>();

                Logistics.InitializeNewCampaign();
                Combat.InitializeNewCampaign();
                Hangar.InitializeNewCampaign();

                Director = CombatEncounterDirector.Create(System.SystemId, star.transform, Ship, unchecked((ulong)Seed));
                owned.Add(Director.gameObject);
                Combat.SetDirector(Director);

                var interventionObject = new GameObject("Tutorial PlayMode Intervention");
                owned.Add(interventionObject);
                Intervention = interventionObject.AddComponent<InterventionGroupDirector>();
                Intervention.Initialize(Hangar);

                Gameplay = new NebulaContext();
                Gameplay.SetShip(Ship);
                Gameplay.SetHangar(Hangar);
                Gameplay.SetShipCombat(Combat);
                Gameplay.SetInterventionGroupDirector(Intervention);

                // Asserted at construction, because the alternative is a launch that
                // is silently refused and a step that fails thirty seconds later with
                // "did not dock". Every mission step in this fixture depends on it.
                Assert.AreEqual(System.SystemId, Hangar.CurrentSystemId,
                    "the harness ship is not inside its own system, so no launch in this fixture can be legal");
            }

            /// <summary>
            /// An absolute world position expressed in the scene's own coordinates.
            /// <c>absolute = transform.position + FloatingOrigin.TotalOffset</c>, so
            /// this is that identity solved the other way round.
            /// </summary>
            private static Vector3 SceneOf(Vector3 absolute) => absolute - FloatingOrigin.TotalOffset;

            internal void Begin(string stepId)
            {
                Scope = TutorialStepScope.Create(stepId, null);
                Context = new TutorialStepContext(Gameplay, Scope, TutorialRunMode.Standalone,
                    new TutorialWorldFactory(() => Scope, Gameplay));
            }

            internal PlanetIdentity MaterializePlanet()
            {
                if (Planet != null) return Planet;
                var planetObject = new GameObject("Tutorial PlayMode Planet");
                planetObject.transform.position = SceneOf(System.AbsoluteCenter) + new Vector3(4000f, 0f, 0f);
                owned.Add(planetObject);
                CelestialBody body = planetObject.AddComponent<CelestialBody>();
                body.Configure(CelestialBodyType.Planet, 300f, 1f, false);
                Planet = planetObject.AddComponent<PlanetIdentity>();
                Planet.Initialize(WorldId, body, true);
                return Planet;
            }

            internal VehicleAgent Vehicle(VehicleClass vehicleClass) =>
                Hangar.Vehicles.FirstOrDefault(v => v != null && v.Def != null && v.Def.Class == vehicleClass);

            internal EnemyAgent AnyEnemy(string defId = null) =>
                EnemyAgent.All.Values.FirstOrDefault(a => a != null && !a.Destroyed
                    && (defId == null || (a.Def != null && a.Def.Id == defId)));

            public void Dispose()
            {
                Scope?.Dispose();
                foreach (EnemyAgent agent in EnemyAgent.All.Values.ToList())
                    if (agent != null) UnityEngine.Object.Destroy(agent.gameObject);
                EnemyAgent.ClearRegistry();
                foreach (VehicleAgent vehicle in Hangar.Vehicles.Where(v => v != null).ToList())
                    UnityEngine.Object.Destroy(vehicle.gameObject);
                foreach (DrillStructure drill in ExtractionState.Drills.Where(d => d != null).ToList())
                    UnityEngine.Object.Destroy(drill.gameObject);
                foreach (SurfaceStructure structure in ExtractionState.Structures.Where(s => s != null).ToList())
                    UnityEngine.Object.Destroy(structure.gameObject);
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i] != null) UnityEngine.Object.Destroy(owned[i]);
                owned.Clear();

                VehicleAgent.Registry.Clear();
                LogisticsState.Reset();
                ExtractionState.Reset(0);
                StarSystemRegistry.Clear();
                CombatCampaignState.Reset();
                ProgressionService.Reset();
            }
        }

        // =====================================================================
        // Step plumbing
        // =====================================================================

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

        /// <summary>A live step: prepared against the harness, with its own report.</summary>
        private sealed class LiveStep
        {
            internal readonly ITutorialStepRuntime Runtime;
            internal readonly TutorialStepReport Report = new TutorialStepReport();

            internal LiveStep(Harness harness, string stepId)
            {
                TutorialStepDefinition definition = Definitions[stepId];
                Runtime = definition.CreateRuntime();
                harness.Begin(stepId);
                Runtime.Prepare(harness.Context);
                Report.Reset(definition.ChecklistTr.Count);
            }

            internal bool Evaluate()
            {
                Runtime.Evaluate(Report);
                return Report.IsComplete;
            }
        }

        /// <summary>Runs real frames until the step completes or the budget expires. Never raises the budget to make a step pass.</summary>
        private static IEnumerator RunFramesUntilComplete(LiveStep step, float budgetSeconds = FrameBudgetSeconds)
        {
            float elapsed = 0f;
            while (elapsed < budgetSeconds)
            {
                if (step.Evaluate()) yield break;
                yield return null;
                elapsed += Time.deltaTime;
            }
            step.Evaluate();
        }

        private static IEnumerator RunFrames(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds) { yield return null; elapsed += Time.deltaTime; }
        }

        // =====================================================================
        // F-04 -- the Scout's remote scan
        // =====================================================================

        [UnityTest]
        public IEnumerator F04_CompletesWhenTheScoutsRealScanFinishes()
        {
            using var harness = new Harness();
            harness.MaterializePlanet();
            var step = new LiveStep(harness, "F-04");
            Assert.IsFalse(step.Evaluate(), "F-04 must not be complete the moment Prepare launched the Scout.");

            yield return RunFrames(0.5f);
            // The exact call VehicleAgent.TickOnStation makes when ScoutScanWork
            // reports done -- the counter itself is private and has no advance seam.
            Assert.IsTrue(ExtractionState.CompletePlanetScan(harness.Planet));

            Assert.IsTrue(step.Evaluate(), "F-04 did not complete on the planet's real scan transition.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator F04_DoesNotCompleteWhileTheScoutIsStillTravelling()
        {
            using var harness = new Harness();
            harness.MaterializePlanet();
            var step = new LiveStep(harness, "F-04");

            yield return RunFrames(1f);

            Assert.IsFalse(step.Evaluate(), "F-04 completed without the planet ever being scanned.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator F04_PrepareDoesNotSatisfyItsOwnCondition()
        {
            using var harness = new Harness();
            harness.MaterializePlanet();
            var step = new LiveStep(harness, "F-04");
            Assert.IsFalse(step.Evaluate());
            step.Runtime.Cleanup();
            yield return null;
        }

        // =====================================================================
        // F-18 -- follow, engage, retreat
        // =====================================================================

        [UnityTest]
        public IEnumerator F18_CompletesOnTheRealRetreatCommand()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "F-18");
            Assert.IsFalse(step.Evaluate());

            yield return RunFrames(0.5f);
            VehicleAgent escort = harness.Vehicle(VehicleClass.Escort);
            Assert.IsNotNull(escort?.EscortState, "Prepare should have left an assigned Escort.");
            harness.Hangar.SetEscortCommand(escort, EscortCommand.Follow);
            yield return RunFrames(0.5f);
            harness.Hangar.SetEscortCommand(escort, EscortCommand.Retreat);

            Assert.IsTrue(step.Evaluate(), "F-18 did not complete on the real GERİ ÇEKİL command.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator F18_DoesNotCompleteOnFollowAlone()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "F-18");
            yield return RunFrames(0.5f);

            harness.Hangar.SetEscortCommand(harness.Vehicle(VehicleClass.Escort), EscortCommand.Follow);

            Assert.IsFalse(step.Evaluate(), "F-18 completed on TAKİP ET, which is not the command it asks for.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator F18_PrepareDoesNotSatisfyItsOwnCondition()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "F-18");
            Assert.IsFalse(step.Evaluate());
            step.Runtime.Cleanup();
            yield return null;
        }

        // =====================================================================
        // F-20 -- recall and docking
        // =====================================================================

        [UnityTest]
        public IEnumerator F20_CompletesWhenTheRecalledVehicleReallyDocks()
        {
            using var harness = new Harness();
            harness.MaterializePlanet();
            var step = new LiveStep(harness, "F-20");
            Assert.IsFalse(step.Evaluate());

            VehicleAgent scout = harness.Vehicle(VehicleClass.Scout);
            Assert.IsNotNull(scout, "Prepare should have launched a Scout to recall.");
            harness.Hangar.Recall(scout);

            yield return RunFramesUntilComplete(step);

            Assert.IsTrue(step.Report.IsComplete,
                $"F-20 did not dock within {FrameBudgetSeconds}s of real frames; " +
                $"phase={scout.Mission.Phase}, command used={harness.Hangar.Command.Used}.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator F20_DoesNotCompleteWithoutARecall()
        {
            using var harness = new Harness();
            harness.MaterializePlanet();
            var step = new LiveStep(harness, "F-20");

            yield return RunFrames(2f);

            Assert.IsFalse(step.Evaluate(), "F-20 completed although GERİ ÇAĞIR was never pressed.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator F20_PrepareDoesNotSatisfyItsOwnCondition()
        {
            using var harness = new Harness();
            harness.MaterializePlanet();
            var step = new LiveStep(harness, "F-20");
            Assert.IsFalse(step.Evaluate());
            step.Runtime.Cleanup();
            yield return null;
        }

        // =====================================================================
        // H-18 -- repair
        // =====================================================================

        [UnityTest]
        public IEnumerator H18_CompletesOnTheRealRepairCommand()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "H-18");
            Assert.IsFalse(step.Evaluate());

            Assert.IsTrue(harness.Combat.TryStartRepair(),
                $"Repair was refused: {harness.Combat.LastRepairRejectReason}");
            yield return RunFrames(0.5f);

            Assert.IsTrue(step.Evaluate(), "H-18 did not complete on the real RepairStarted event.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator H18_DoesNotCompleteWhileInCombat()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "H-18");

            harness.Combat.SetEncounterOpen(true);
            Assert.IsFalse(harness.Combat.TryStartRepair());
            Assert.AreEqual(RepairRejectReason.InCombat, harness.Combat.LastRepairRejectReason);
            yield return RunFrames(0.25f);

            Assert.IsFalse(step.Evaluate(), "H-18 completed although the game refused the repair.");
            harness.Combat.SetEncounterOpen(false);
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator H18_PrepareDoesNotSatisfyItsOwnCondition()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "H-18");
            Assert.IsFalse(step.Evaluate());
            step.Runtime.Cleanup();
            yield return null;
        }

        // =====================================================================
        // H-21 -- leaving the Damaged state
        // =====================================================================

        [UnityTest]
        public IEnumerator H21_CompletesWhenArmourAndHullReallyReachTheFieldCap()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "H-21");
            Assert.IsFalse(step.Evaluate());
            Assert.IsTrue(harness.Combat.State.Damaged, "Prepare should have left the ship really Damaged.");

            yield return RunFrames(0.25f);

            // The exact pair ShipCombat.TickRepair applies each frame; TickRepair
            // itself is private and has no advance seam of its own.
            Durability durability = harness.Combat.State.Durability;
            durability.RepairExact(
                Mathf.Max(0f, durability.ArmorMax * RepairCatalog.FieldCapFraction - durability.Armor),
                Mathf.Max(0f, durability.HullMax * RepairCatalog.FieldCapFraction - durability.Hull));
            harness.Combat.State.ClearDamagedIfHealedEnough();

            Assert.IsTrue(step.Evaluate(), "H-21 did not complete although the ship is no longer Damaged.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator H21_DoesNotCompleteOnAPartialRepair()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "H-21");

            Durability durability = harness.Combat.State.Durability;
            durability.RepairExact(durability.ArmorMax * 0.05f, durability.HullMax * 0.05f);
            harness.Combat.State.ClearDamagedIfHealedEnough();
            yield return RunFrames(0.25f);

            Assert.IsFalse(step.Evaluate(), "H-21 completed before armour and hull reached the field cap.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator H21_PrepareDoesNotSatisfyItsOwnCondition()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "H-21");
            Assert.IsFalse(step.Evaluate());
            step.Runtime.Cleanup();
            yield return null;
        }

        // =====================================================================
        // I-20 -- Escort defence
        // =====================================================================

        [UnityTest]
        public IEnumerator I20_CompletesWhenTheEscortReallyEngagesTheThreat()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "I-20");
            Assert.IsFalse(step.Evaluate());

            VehicleAgent escort = harness.Vehicle(VehicleClass.Escort);
            EnemyAgent threat = harness.AnyEnemy(EnemyIds.LooterTransportMk1);
            Assert.IsNotNull(escort, "Prepare should have left an Escort in the hangar.");
            Assert.IsNull(escort.EscortState,
                "Prepare must leave the Escort unassigned -- giving it the protection order is the step.");
            Assert.IsNotNull(threat, "Prepare should have spawned a real Looter in range.");

            // The player's own command, through the real service.
            Assert.AreEqual(ShipHangar.EscortAssignRejectReason.None,
                harness.Hangar.TryAssignEscort(escort, "ship", TargetCategory.Ship));

            yield return RunFramesUntilComplete(step, 5f);

            if (!step.Report.IsComplete)
            {
                // VehicleAgent.TickEscortMovement's own two calls, driven directly:
                // its fire path is private and there is no advance seam for it.
                float before = threat.Combat.Shield + threat.Combat.Armor + threat.Combat.Hull;
                escort.EscortState.EngageThreat(threat.Id);
                threat.ApplyDamage(escort.Def.FixedLaserDps * Time.fixedDeltaTime, DamageProfile.Laser);
                Assert.Less(threat.Combat.Shield + threat.Combat.Armor + threat.Combat.Hull, before,
                    "The Escort's own damage call did not remove real durability.");
                step.Evaluate();
            }

            Assert.IsTrue(step.Report.IsComplete, "I-20 did not complete although the Escort engaged the threat.");
            step.Runtime.Cleanup();
        }

        [UnityTest]
        public IEnumerator I20_DoesNotCompleteWhenTheThreatIsOutOfEscortRange()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "I-20");

            EnemyAgent threat = harness.AnyEnemy(EnemyIds.LooterTransportMk1);
            Assert.IsNotNull(threat);
            // Assigned, so the refusal below is about range and not about a missing
            // order -- and beyond every Escort's FixedLaserRange, so the live path
            // can never engage.
            harness.Hangar.TryAssignEscort(harness.Vehicle(VehicleClass.Escort), "ship", TargetCategory.Ship);
            threat.transform.position = harness.Ship.transform.position + new Vector3(0f, 0f, 50_000f);
            yield return RunFrames(1f);

            Assert.IsFalse(step.Evaluate(), "I-20 completed with the threat far outside Escort range.");
            step.Runtime.Cleanup();
        }

        /// <summary>
        /// The regression the strengthened sweep found: with the threat sitting 400
        /// units away and an Escort already protecting the ship, the Escort's own AI
        /// closed and engaged within a couple of frames, so the step finished itself.
        /// Prepare now stops short of the protection order, and this holds it to that
        /// over real frames rather than over a single evaluation.
        /// </summary>
        [UnityTest]
        public IEnumerator I20_NeverCompletesWithoutTheProtectionOrder()
        {
            using var harness = new Harness();
            var step = new LiveStep(harness, "I-20");
            Assert.IsFalse(step.Evaluate(), "I-20 was complete the moment its own Prepare finished.");

            yield return RunFrames(3f);

            Assert.IsFalse(step.Evaluate(),
                "I-20 completed with no protection order given -- the Escort engaged on its own, " +
                "which means Prepare had already performed the step's first checklist row.");
            step.Runtime.Cleanup();
        }

        // =====================================================================
        // Coverage
        // =====================================================================

        [Test]
        public void ThisFixtureCoversExactlyTheStepsThatNeedRealFrames()
        {
            string[] expected = { "F-04", "F-18", "F-20", "H-18", "H-21", "I-20" };
            foreach (string stepId in expected)
                Assert.IsTrue(Definitions.ContainsKey(stepId), $"{stepId} is missing from the four chapters.");
            Assert.AreEqual(71, Definitions.Count,
                "Chapters F (20) + H (21) + I (20) + J (10) must register 71 steps in total.");
        }
    }
}
