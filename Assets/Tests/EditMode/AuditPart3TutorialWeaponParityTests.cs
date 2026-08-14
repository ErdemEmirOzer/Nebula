using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
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
    /// GENEL DENETİM PART 3 / A3 — P2-DEAD-003, P1-COMBAT-001.
    /// <para>
    /// Chapter H'nin H-04…H-07 adımları ölü bir üretim yoluna bağlıydı. Kök neden
    /// tek satırda: Hull Form I <b>tek</b> etkin hardpoint taşır
    /// (<c>CombatCatalog.ActiveHardpointSlots</c>), doğuştan gelen bedava Laser o
    /// tek yuvayı doldurur, dolayısıyla <c>ShipCombat.TryStartProduction</c>
    /// kampanyanın tamamı boyunca <c>NoHardpointAvailable</c> döndürür. Tutorial
    /// bunu eskiden doğrudan <c>Hardpoint.Equip</c> çağırarak gizliyordu; o bypass
    /// kaldırılınca adımlar tamamlanamaz hâle geldi.
    /// </para>
    /// <para>
    /// Bu fixture iki şeyi ölçer: (1) H-04…H-07 gerçek üretim işiyle kendi silahını
    /// gerçekten kuruyor mu, (2) tutorial yolu ile normal oyun yolu <b>aynı</b> tier
    /// kapısını, <b>aynı</b> katalog maliyetini, <b>aynı</b> katalog süresini,
    /// <b>aynı</b> hardpoint kuralını ve <b>aynı</b> ret enum'ını mı kullanıyor.
    /// Reddedilen bir üretim adımı tamamlamamalı ve çıkışta ödünç alınan her şey
    /// geri verilmelidir.
    /// </para>
    /// </summary>
    [Category("AuditPart3")]
    public class AuditPart3TutorialWeaponParityTests
    {
        private const int Seed = 20260812;
        private const int TrainingTier = 1;

        // =====================================================================
        // Harness — the tutorial's own ship slice, nothing more
        // =====================================================================

        private sealed class Harness : IDisposable
        {
            internal NebulaContext Gameplay { get; }
            internal ShipController Ship { get; }
            internal ShipCombat Combat { get; }
            internal ShipLogistics Logistics { get; }
            internal ShipModules Modules { get; }
            internal CombatEncounterDirector Director { get; }
            internal TutorialStepScope Scope { get; private set; }
            internal TutorialStepContext Context { get; private set; }
            internal LogisticsCargo Cargo => Logistics.Cargo;

            private readonly List<GameObject> owned = new List<GameObject>();

            /// <param name="withModules">
            /// False builds a ship with no module system at all, which is how the
            /// weapon-coordination tier gate is made to refuse without touching a
            /// single rule: <c>ShipCombat.WeaponCoordinationTier</c> reads 0 and the
            /// service answers <c>FamilyLocked</c>.
            /// </param>
            internal Harness(bool withModules = true)
            {
                ProgressionService.Reset();
                ExtractionState.Reset(Seed);
                LogisticsState.Reset();
                CombatCampaignState.Reset();
                EnemyAgent.ClearRegistry();
                VehicleAgent.Registry.Clear();
                StarSystemRegistry.Initialize(unchecked((ulong)Seed));

                SectorManifest sector = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
                SystemManifest system = sector.Systems.First(s => s.Planets.Count > 0);
                StarSystemRegistry.SetStartSystem(system.Sector, system.SystemId);

                var star = new GameObject("Parity Star");
                star.transform.position = system.AbsoluteCenter;
                owned.Add(star);

                var shipObject = new GameObject("Parity Ship");
                shipObject.transform.position = system.AbsoluteCenter;
                owned.Add(shipObject);
                Ship = shipObject.AddComponent<ShipController>();
                Logistics = shipObject.AddComponent<ShipLogistics>();
                Combat = shipObject.AddComponent<ShipCombat>();
                if (withModules) Modules = shipObject.AddComponent<ShipModules>();

                // EditMode runs no Awake on a runtime-added component.
                TutorialAwake.WakeAll(shipObject);

                Logistics.InitializeNewCampaign();
                if (withModules) Modules.InitializeNewCampaign();
                Combat.InitializeNewCampaign();

                Director = CombatEncounterDirector.Create(system.SystemId, star.transform, Ship, unchecked((ulong)Seed));
                owned.Add(Director.gameObject);
                Combat.SetDirector(Director);

                Gameplay = new NebulaContext();
                Gameplay.SetShip(Ship);
                Gameplay.SetShipCombat(Combat);
            }

            internal void Begin(string stepId)
            {
                Scope = TutorialStepScope.Create(stepId, null);
                Context = new TutorialStepContext(Gameplay, Scope, TutorialRunMode.Standalone,
                    new TutorialWorldFactory(() => Scope, Gameplay));
            }

            internal void EndStep()
            {
                Scope?.Dispose();
                Scope = null;
                Context = null;
            }

            internal int WeaponCoordinationTier => Combat.WeaponCoordinationTier;

            internal HardpointState ActiveHardpointWith(WeaponFamily family)
            {
                int active = Mathf.Min(Combat.ActiveHardpointCount, Combat.Hardpoints.Length);
                for (int i = 0; i < active; i++)
                    if (Combat.Hardpoints[i] != null && Combat.Hardpoints[i].Weapon == family) return Combat.Hardpoints[i];
                return null;
            }

            public void Dispose()
            {
                Scope?.Dispose();
                foreach (GameObject go in owned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                owned.Clear();
                EnemyAgent.ClearRegistry();
                ProgressionService.Reset();
            }
        }

        private static TutorialStepDefinition Step(string stepId) =>
            ChapterHSteps.Build().Single(s => s.StepId == stepId);

        /// <summary>Runs one Chapter H step's own Prepare, exactly as the director does.</summary>
        private static ITutorialStepRuntime Prepare(Harness harness, string stepId)
        {
            TutorialStepDefinition step = Step(stepId);
            ITutorialStepRuntime runtime = step.CreateRuntime();
            harness.Begin(stepId);
            runtime.Prepare(harness.Context);
            return runtime;
        }

        private static int UnlockTierFor(WeaponFamily family) => family switch
        {
            WeaponFamily.Em => CombatCatalog.EmUnlockWeaponCoordinationTier,
            WeaponFamily.Plasma => CombatCatalog.PlasmaUnlockWeaponCoordinationTier,
            _ => 0
        };

        // =====================================================================
        // 1. The root cause, stated as a regression
        // =====================================================================

        /// <summary>
        /// The exact shape of the dead flow. Form I carries one active hardpoint and
        /// the birth Laser fills it, so with no way to free a slot every production
        /// request is refused forever — for the tutorial and for the player alike.
        /// </summary>
        [Test]
        public void HullFormI_HasOneActiveHardpoint_AndTheBirthLaserFillsIt()
        {
            using var harness = new Harness();

            Assert.AreEqual(1, CombatCatalog.HullFormForLevel(ProgressionService.State.Level),
                "yeni kampanya Hull Form I ile başlar");
            Assert.AreEqual(1, harness.Combat.ActiveHardpointCount,
                "Hull Form I tam olarak bir etkin hardpoint taşır");
            Assert.AreEqual(WeaponFamily.Laser, harness.Combat.Hardpoints[0].Weapon,
                "doğuştan gelen bedava Laser o tek yuvayı doldurur");

            harness.Modules.Modules.SetTier("defense.weapon_coordination", CombatCatalog.PlasmaUnlockWeaponCoordinationTier);
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);
            harness.Cargo.SetCapacity(10000f);
            harness.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts);
            harness.Cargo.Add(cost.MaterialId, cost.Material);

            Assert.AreEqual(WeaponProductionRejectReason.NoHardpointAvailable,
                harness.Combat.TryStartProduction(WeaponFamily.Plasma, TrainingTier),
                "P2-DEAD-003/P1-COMBAT-001 kökü: dolu tek yuvayla üretim sonsuza dek reddedilir");

            // The refit seam is the fix: freeing the slot makes the SAME request succeed.
            Assert.AreEqual(HardpointReleaseRejectReason.None,
                WeaponLoadoutService.TryRelease(harness.Combat.ActiveProductionJob, harness.Combat.Hardpoints[0], harness.Combat.InCombat));
            Assert.AreEqual(WeaponProductionRejectReason.None,
                harness.Combat.TryStartProduction(WeaponFamily.Plasma, TrainingTier));
        }

        // =====================================================================
        // 2. H-04…H-07 end to end, through the real production job
        // =====================================================================

        [Test]
        public void H04_H05_H06_H07_EachInstallItsOwnWeaponThroughARealProductionJob()
        {
            using var harness = new Harness();

            var expected = new (string stepId, WeaponFamily family)[]
            {
                ("H-04", WeaponFamily.Plasma),
                ("H-05", WeaponFamily.Em),
                ("H-06", WeaponFamily.Plasma),
                ("H-07", WeaponFamily.Plasma)
            };

            foreach ((string stepId, WeaponFamily family) in expected)
            {
                ITutorialStepRuntime runtime = Prepare(harness, stepId);

                Assert.IsNotNull(harness.ActiveHardpointWith(family),
                    $"{stepId} kendi silahını ({family}) kuramadı — adım tamamlanamaz");
                Assert.AreEqual(TrainingTier, harness.ActiveHardpointWith(family).Tier,
                    $"{stepId} katalog dışı bir tier kurdu");
                Assert.IsNull(harness.Combat.ActiveProductionJob,
                    $"{stepId} yarım kalmış bir üretim işi bıraktı");
                Assert.AreEqual(1, harness.Combat.ActiveHardpointCount,
                    $"{stepId} gemiye fazladan yuva verdi — Hull Form I bir yuva taşır");

                runtime.Cleanup();
                harness.EndStep();
            }
        }

        /// <summary>
        /// The positive half of <see cref="RefusedProduction_LeavesTheStepIncomplete_AndSpendsNothing"/>.
        /// After preparation nothing stands between the player and the lesson: the
        /// step's own <c>reject</c> — the one that answers "PLAZMA TAKILI DEĞİL"
        /// while the weapon is missing — reports nothing at all, so what remains is
        /// the real action the step asks for. Measured through <c>Evaluate</c> and
        /// the report the card actually shows, not through the fixture's own idea of
        /// readiness.
        /// </summary>
        [Test]
        public void PreparedSteps_H04ToH07_ReportNoBlockingRefusal_AndCarryTheirOwnWeapon()
        {
            foreach ((string stepId, WeaponFamily family) in new[]
                     {
                         ("H-04", WeaponFamily.Plasma),
                         ("H-05", WeaponFamily.Em),
                         ("H-06", WeaponFamily.Plasma),
                         ("H-07", WeaponFamily.Plasma)
                     })
            {
                using var harness = new Harness();
                TutorialStepDefinition step = Step(stepId);
                ITutorialStepRuntime runtime = step.CreateRuntime();
                var report = new TutorialStepReport();

                harness.Begin(stepId);
                runtime.Prepare(harness.Context);
                report.Reset(step.ChecklistTr.Count);
                runtime.Evaluate(report);

                Assert.IsNotNull(harness.ActiveHardpointWith(family),
                    $"{stepId} hazırlıktan sonra kendi silahını ({family}) taşımıyor");
                Assert.AreNotEqual(TutorialFeedbackKind.Reject, report.FeedbackKind,
                    $"{stepId} hazırlandıktan sonra hâlâ bir engel bildiriyor: \"{report.Feedback}\" — " +
                    "adım oyuncunun yapacağı gerçek eylemi bekliyor olmalı");

                runtime.Cleanup();
                harness.EndStep();
            }
        }

        /// <summary>
        /// The weapon is on the ship because a job produced it — not because the
        /// step wrote it there. Proven on the resources: production is the only
        /// thing in this project that consumes a weapon reservation.
        /// </summary>
        [Test]
        public void H04_SpendsExactlyTheCatalogCost_AndLeavesNoReservationBehind()
        {
            using var harness = new Harness();
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);

            harness.Cargo.SetCapacity(10000f);
            harness.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts * 4f);
            harness.Cargo.Add(cost.MaterialId, cost.Material * 4f);
            float partsBefore = harness.Cargo.GetStored(ProcessingCatalog.BasicPartId);
            float materialBefore = harness.Cargo.GetStored(cost.MaterialId);

            ITutorialStepRuntime runtime = Prepare(harness, "H-04");

            Assert.IsNotNull(harness.ActiveHardpointWith(WeaponFamily.Plasma));
            Assert.AreEqual(partsBefore - cost.Parts, harness.Cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "tutorial katalog parça maliyetini birebir harcamalı");
            Assert.AreEqual(materialBefore - cost.Material, harness.Cargo.GetStored(cost.MaterialId), 0.0001f,
                "tutorial katalog materyal maliyetini birebir harcamalı");
            Assert.AreEqual(0f, harness.Cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.0001f,
                "tamamlanan iş rezervasyonunu bırakmamalı");

            runtime.Cleanup();
        }

        // =====================================================================
        // 3. Parity — tier, cost, duration, hardpoint rule, outcome
        // =====================================================================

        /// <summary>
        /// The Silahlar page path and the tutorial path, run on two identical ships,
        /// must end in the same loadout and the same spend.
        /// </summary>
        [Test]
        public void Parity_TutorialAndNormalPlayEndWithTheSameLoadoutAndTheSameSpend()
        {
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);

            float normalParts, normalMaterial;
            int normalTier;
            using (var normal = new Harness())
            {
                // Exactly what the player does: buy the unlock tier, free the slot,
                // pay the catalog cost, start the job, let it run its own duration.
                normal.Modules.Modules.SetTier("defense.weapon_coordination", UnlockTierFor(WeaponFamily.Plasma));
                normal.Cargo.SetCapacity(10000f);
                normal.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts * 4f);
                normal.Cargo.Add(cost.MaterialId, cost.Material * 4f);
                float partsBefore = normal.Cargo.GetStored(ProcessingCatalog.BasicPartId);
                float materialBefore = normal.Cargo.GetStored(cost.MaterialId);

                Assert.AreEqual(HardpointReleaseRejectReason.None,
                    WeaponLoadoutService.TryRelease(normal.Combat.ActiveProductionJob, normal.Combat.Hardpoints[0], normal.Combat.InCombat));
                Assert.AreEqual(WeaponProductionRejectReason.None,
                    normal.Combat.TryStartProduction(WeaponFamily.Plasma, TrainingTier));
                Assert.IsTrue(normal.Combat.AdvanceProduction(cost.Seconds));

                Assert.IsNotNull(normal.ActiveHardpointWith(WeaponFamily.Plasma));
                normalParts = partsBefore - normal.Cargo.GetStored(ProcessingCatalog.BasicPartId);
                normalMaterial = materialBefore - normal.Cargo.GetStored(cost.MaterialId);
                normalTier = normal.ActiveHardpointWith(WeaponFamily.Plasma).Tier;
            }

            using (var tutorial = new Harness())
            {
                tutorial.Cargo.SetCapacity(10000f);
                tutorial.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts * 4f);
                tutorial.Cargo.Add(cost.MaterialId, cost.Material * 4f);
                float partsBefore = tutorial.Cargo.GetStored(ProcessingCatalog.BasicPartId);
                float materialBefore = tutorial.Cargo.GetStored(cost.MaterialId);

                ITutorialStepRuntime runtime = Prepare(tutorial, "H-04");

                Assert.IsNotNull(tutorial.ActiveHardpointWith(WeaponFamily.Plasma));
                Assert.AreEqual(normalTier, tutorial.ActiveHardpointWith(WeaponFamily.Plasma).Tier,
                    "tutorial normal oyundan farklı bir tier kuruyor");
                Assert.AreEqual(normalParts, partsBefore - tutorial.Cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                    "tutorial normal oyundan farklı bir parça maliyeti ödüyor");
                Assert.AreEqual(normalMaterial, materialBefore - tutorial.Cargo.GetStored(cost.MaterialId), 0.0001f,
                    "tutorial normal oyundan farklı bir materyal maliyeti ödüyor");
                runtime.Cleanup();
            }
        }

        /// <summary>
        /// P2-DEAD-003'ün asıl şikâyeti: tutorial tier kilidini atlıyordu. Artık
        /// katalog kilidinin kendisini, birebir o değere kadar hazırlıyor ve
        /// kilidin altındaki bir tier gerçekten reddediliyor.
        /// </summary>
        [Test]
        public void Parity_TierGateIsTheCatalogUnlockTier_AndTheTutorialNeverExceedsIt()
        {
            foreach ((string stepId, WeaponFamily family) in new[]
                     {
                         ("H-04", WeaponFamily.Plasma),
                         ("H-05", WeaponFamily.Em)
                     })
            {
                using var harness = new Harness();
                int required = UnlockTierFor(family);

                // One tier below the catalog's own unlock tier: the service refuses,
                // and it refuses identically no matter who is asking.
                harness.Modules.Modules.SetTier("defense.weapon_coordination", required - 1);
                Assert.IsFalse(WeaponLoadoutService.IsFamilyUnlocked(family, required - 1),
                    $"{family} kilidi katalog değerinden düşük tier'da açık görünüyor");
                Assert.AreEqual(WeaponProductionRejectReason.FamilyLocked,
                    harness.Combat.TryStartProduction(family, TrainingTier));

                ITutorialStepRuntime runtime = Prepare(harness, stepId);
                Assert.AreEqual(required, harness.WeaponCoordinationTier,
                    $"{stepId} silah koordinasyonunu katalog kilidinden farklı bir tier'a çekti");
                Assert.IsNotNull(harness.ActiveHardpointWith(family));
                runtime.Cleanup();
            }
        }

        /// <summary>
        /// Duration parity is a property of the job, not of who started it: the job
        /// carries the catalog's own seconds and equips nothing until they are spent.
        /// </summary>
        [Test]
        public void Parity_DurationIsTheCatalogDuration_AndAPartialAdvanceEquipsNothing()
        {
            using var harness = new Harness();
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);

            harness.Modules.Modules.SetTier("defense.weapon_coordination", UnlockTierFor(WeaponFamily.Plasma));
            harness.Cargo.SetCapacity(10000f);
            harness.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts);
            harness.Cargo.Add(cost.MaterialId, cost.Material);

            Assert.AreEqual(HardpointReleaseRejectReason.None,
                WeaponLoadoutService.TryRelease(harness.Combat.ActiveProductionJob, harness.Combat.Hardpoints[0], harness.Combat.InCombat));
            Assert.AreEqual(WeaponProductionRejectReason.None,
                harness.Combat.TryStartProduction(WeaponFamily.Plasma, TrainingTier));

            Assert.AreEqual(cost.Seconds, harness.Combat.ActiveProductionJob.TotalSeconds, 0.0001f,
                "iş katalog süresinden farklı bir süre taşıyor");

            Assert.IsFalse(harness.Combat.AdvanceProduction(cost.Seconds - 0.01f),
                "katalog süresi dolmadan iş tamamlandı");
            Assert.IsNull(harness.ActiveHardpointWith(WeaponFamily.Plasma),
                "süre dolmadan silah kuruldu");

            // Not 0.01f: `(cost.Seconds - 0.01f) + 0.01f` is not cost.Seconds in float
            // arithmetic, it is a hair under, so advancing back by exactly what was
            // withheld leaves the job one ulp short and the assert measures rounding
            // rather than the rule. The rule — nothing equips before the catalog
            // duration — is already pinned by the two asserts above.
            Assert.IsTrue(harness.Combat.AdvanceProduction(0.02f),
                "katalog süresi dolduğu hâlde iş tamamlanmadı");
            Assert.IsNotNull(harness.ActiveHardpointWith(WeaponFamily.Plasma));
        }

        [Test]
        public void Parity_HardpointAvailabilityUsesTheSameActiveSlotRuleForBothPaths()
        {
            using var harness = new Harness();
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);
            harness.Modules.Modules.SetTier("defense.weapon_coordination", UnlockTierFor(WeaponFamily.Plasma));
            harness.Cargo.SetCapacity(10000f);
            harness.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts * 4f);
            harness.Cargo.Add(cost.MaterialId, cost.Material * 4f);

            Assert.AreEqual(CombatCatalog.ActiveHardpointSlots(harness.Combat.HullFormValue, harness.Combat.WeaponCoordinationTier),
                harness.Combat.ActiveHardpointCount,
                "etkin yuva sayısı katalogdan başka bir yerden geliyor");

            // Slot 4 exists in the array but is outside the active window: filling it
            // must not make production think a slot is free.
            Assert.AreEqual(WeaponProductionRejectReason.NoHardpointAvailable,
                harness.Combat.TryStartProduction(WeaponFamily.Plasma, TrainingTier),
                "dolu etkin yuvaya rağmen üretim kabul edildi");

            ITutorialStepRuntime runtime = Prepare(harness, "H-04");
            Assert.AreEqual(1, harness.Combat.ActiveHardpointCount,
                "tutorial etkin yuva kuralını kendi lehine değiştirdi");
            Assert.IsNotNull(harness.ActiveHardpointWith(WeaponFamily.Plasma));
            runtime.Cleanup();
        }

        [Test]
        public void Parity_TheLoadoutIsOnlyChangeableOutsideCombat_ForBothPaths()
        {
            using var harness = new Harness();
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);
            harness.Modules.Modules.SetTier("defense.weapon_coordination", UnlockTierFor(WeaponFamily.Plasma));
            harness.Cargo.SetCapacity(10000f);
            harness.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts);
            harness.Cargo.Add(cost.MaterialId, cost.Material);

            harness.Combat.SetEncounterOpen(true);
            Assert.AreEqual(HardpointReleaseRejectReason.CombatActive,
                WeaponLoadoutService.TryRelease(harness.Combat.ActiveProductionJob, harness.Combat.Hardpoints[0], harness.Combat.InCombat),
                "muharebe sırasında hardpoint boşaltılabiliyor");
            Assert.AreEqual(WeaponProductionRejectReason.CombatActive,
                harness.Combat.TryStartProduction(WeaponFamily.Plasma, TrainingTier),
                "muharebe sırasında üretim başlatılabiliyor");

            harness.Combat.SetEncounterOpen(false);
            Assert.AreEqual(HardpointReleaseRejectReason.None,
                WeaponLoadoutService.TryRelease(harness.Combat.ActiveProductionJob, harness.Combat.Hardpoints[0], harness.Combat.InCombat));
            Assert.AreEqual(WeaponProductionRejectReason.None,
                harness.Combat.TryStartProduction(WeaponFamily.Plasma, TrainingTier));

            // A job in flight is the second half of the same rule.
            Assert.AreEqual(HardpointReleaseRejectReason.QueueBusy,
                WeaponLoadoutService.TryRelease(harness.Combat.ActiveProductionJob, harness.Combat.Hardpoints[0], harness.Combat.InCombat));
            Assert.AreEqual(WeaponProductionRejectReason.QueueBusy,
                harness.Combat.TryStartProduction(WeaponFamily.Em, TrainingTier));
        }

        // =====================================================================
        // 4. A refused production does not complete the step
        // =====================================================================

        /// <summary>
        /// A ship with no module system reads weapon-coordination tier 0, so the
        /// catalog's own gate refuses Plasma. The step must then stay incomplete —
        /// forever, not merely on the first poll — and nothing may be spent.
        /// </summary>
        [Test]
        public void RefusedProduction_LeavesTheStepIncomplete_AndSpendsNothing()
        {
            using var harness = new Harness(withModules: false);
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);
            harness.Cargo.SetCapacity(10000f);
            harness.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts * 4f);
            harness.Cargo.Add(cost.MaterialId, cost.Material * 4f);
            float partsBefore = harness.Cargo.GetStored(ProcessingCatalog.BasicPartId);
            float materialBefore = harness.Cargo.GetStored(cost.MaterialId);

            Assert.AreEqual(0, harness.Combat.WeaponCoordinationTier);
            Assert.AreEqual(WeaponProductionRejectReason.FamilyLocked,
                harness.Combat.TryStartProduction(WeaponFamily.Plasma, TrainingTier));

            TutorialStepDefinition step = Step("H-04");
            ITutorialStepRuntime runtime = step.CreateRuntime();
            var report = new TutorialStepReport();
            harness.Begin("H-04");
            runtime.Prepare(harness.Context);
            report.Reset(step.ChecklistTr.Count);

            Assert.IsNull(harness.ActiveHardpointWith(WeaponFamily.Plasma),
                "reddedilen üretim yine de silah kurdu");

            for (int poll = 0; poll < 120; poll++)
            {
                runtime.Evaluate(report);
                Assert.IsFalse(report.IsComplete,
                    $"üretim reddedilmişken H-04 {poll + 1}. yoklamada tamamlandı");
            }

            Assert.AreEqual(partsBefore, harness.Cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "reddedilen üretim parça harcadı");
            Assert.AreEqual(materialBefore, harness.Cargo.GetStored(cost.MaterialId), 0.0001f,
                "reddedilen üretim materyal harcadı");
            Assert.AreEqual(0f, harness.Cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.0001f,
                "reddedilen üretim rezervasyon bıraktı");
            runtime.Cleanup();
        }

        // =====================================================================
        // 5. Cleanup
        // =====================================================================

        /// <summary>
        /// Everything the training scene borrowed goes back when the step ends: the
        /// hardpoint arrangement, the module tier, and any reservation still held.
        /// </summary>
        [Test]
        public void StepExit_ReturnsTheBorrowedLoadout_TheBorrowedTierAndEveryReservation()
        {
            using var harness = new Harness();
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);
            harness.Cargo.SetCapacity(10000f);
            harness.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts * 4f);
            harness.Cargo.Add(cost.MaterialId, cost.Material * 4f);

            int tierBefore = harness.WeaponCoordinationTier;
            var loadoutBefore = new (WeaponFamily? weapon, int tier)[harness.Combat.Hardpoints.Length];
            for (int i = 0; i < loadoutBefore.Length; i++)
                loadoutBefore[i] = (harness.Combat.Hardpoints[i].Weapon, harness.Combat.Hardpoints[i].Tier);

            ITutorialStepRuntime runtime = Prepare(harness, "H-04");
            Assert.IsNotNull(harness.ActiveHardpointWith(WeaponFamily.Plasma), "adım silahını kuramadı");
            Assert.AreNotEqual(tierBefore, harness.WeaponCoordinationTier, "adım hiçbir şey ödünç almadı — test bir şey ölçmüyor");

            runtime.Cleanup();
            harness.EndStep();

            for (int i = 0; i < loadoutBefore.Length; i++)
            {
                Assert.AreEqual(loadoutBefore[i].weapon, harness.Combat.Hardpoints[i].Weapon,
                    $"hardpoint {i} çıkışta iade edilmedi");
                Assert.AreEqual(loadoutBefore[i].tier, harness.Combat.Hardpoints[i].Tier,
                    $"hardpoint {i} tier'ı çıkışta iade edilmedi");
            }
            Assert.AreEqual(tierBefore, harness.WeaponCoordinationTier,
                "ödünç alınan silah koordinasyonu tier'ı çıkışta iade edilmedi");
            Assert.IsNull(harness.Combat.ActiveProductionJob, "çıkışta yarım iş kaldı");
            Assert.AreEqual(0f, harness.Cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.0001f,
                "çıkışta rezervasyon kaldı");
            Assert.AreEqual(0f, harness.Cargo.GetReserved(cost.MaterialId), 0.0001f,
                "çıkışta materyal rezervasyonu kaldı");
        }

        /// <summary>
        /// A step abandoned mid-production — the player leaves while the job is in
        /// flight — releases the reservation through the real cancel/refund path
        /// rather than stranding it.
        /// </summary>
        [Test]
        public void StepExit_CancelsAJobStillInFlight_InsteadOfStrandingItsReservation()
        {
            using var harness = new Harness();
            WeaponTierCost cost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, TrainingTier);
            harness.Cargo.SetCapacity(10000f);
            harness.Cargo.Add(ProcessingCatalog.BasicPartId, cost.Parts * 4f);
            harness.Cargo.Add(cost.MaterialId, cost.Material * 4f);

            ITutorialStepRuntime runtime = Prepare(harness, "H-04");

            // The player starts a second, real job and walks away mid-build.
            harness.Combat.SetEncounterOpen(false);
            Assert.AreEqual(HardpointReleaseRejectReason.None,
                WeaponLoadoutService.TryRelease(harness.Combat.ActiveProductionJob, harness.Combat.Hardpoints[0], harness.Combat.InCombat));
            Assert.AreEqual(WeaponProductionRejectReason.None,
                harness.Combat.TryStartProduction(WeaponFamily.Laser, TrainingTier));
            Assert.IsNotNull(harness.Combat.ActiveProductionJob);
            Assert.Greater(harness.Cargo.GetReserved(ProcessingCatalog.BasicPartId), 0f);

            runtime.Cleanup();
            harness.EndStep();

            Assert.IsNull(harness.Combat.ActiveProductionJob, "yarım iş çıkışta iptal edilmedi");
            Assert.AreEqual(0f, harness.Cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.0001f,
                "iptal edilen işin rezervasyonu askıda kaldı");
        }
    }
}
