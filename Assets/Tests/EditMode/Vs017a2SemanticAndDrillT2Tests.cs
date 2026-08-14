using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;
using Nebula.Telemetry;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-017A.2 §D: the telemetry-semantics fixes (campaign identity lifecycle,
    /// new_game uniqueness, second-system Asset, load/restart-safe second drill)
    /// and the real Drill T1→T2 implementation (gate, cost, transaction, refund,
    /// production/storage formulas, Full↔1 Hz equivalence), plus the §C Logistics
    /// Center fill measurement.
    /// </summary>
    public class Vs017a2SemanticAndDrillT2Tests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "nebula-vs017a2-" + System.Guid.NewGuid().ToString("N"));
            TelemetryService.InvalidateFunnelIndexCache();
            CampaignIdentity.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            TelemetryService.EndSession(tempDir);
            TelemetryService.InvalidateFunnelIndexCache();
            CampaignIdentity.ClearForTests();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            StarSystemRegistry.Clear();
        }

        // ==================== §A2: campaign identity lifecycle ====================

        [Test]
        public void NewCampaign_MintsAUniqueRealCampaignId_NeverTheLegacyPlaceholder()
        {
            string first = CampaignIdentity.StartNewCampaign();
            string second = CampaignIdentity.StartNewCampaign();

            Assert.IsTrue(CampaignIdentity.IsRealId(first));
            Assert.AreNotEqual(CampaignIdentity.LegacyPlaceholderId, first, "the hardcoded 'campaign-1' placeholder must never be a real identity again");
            Assert.AreNotEqual(first, second, "every genuinely new campaign must get its own unique identity");
            Assert.AreEqual(second, CampaignIdentity.Current);
        }

        [Test]
        public void LoadingASave_AdoptsItsOwnCampaignId_AndIsNeverTreatedAsANewCampaign()
        {
            CampaignIdentity.StartNewCampaign();
            CampaignIdentity.ConsumePendingNewCampaign(); // the boot campaign announces itself once

            const string savedId = "3f2a9c7b41d84e6fa0c1d2e3f4a5b6c7";
            string adopted = CampaignIdentity.AdoptFromSave(savedId);

            Assert.AreEqual(savedId, adopted, "a load must adopt the envelope's identity verbatim");
            Assert.AreEqual(savedId, CampaignIdentity.Current);
            Assert.IsFalse(CampaignIdentity.ConsumePendingNewCampaign(), "adopting a save is never a new-campaign transition");
        }

        [Test]
        public void LoadingALegacySave_UpgradesToOneStableRealId_NeverRegeneratedPerLoad()
        {
            string upgraded = CampaignIdentity.AdoptFromSave(CampaignIdentity.LegacyPlaceholderId);
            Assert.IsTrue(CampaignIdentity.IsRealId(upgraded), "a legacy 'campaign-1' save must be upgraded to a real unique identity");

            // That upgraded id is what the next save writes -- so re-loading it keeps it stable.
            string writtenBySave = CampaignIdentity.ForSave();
            Assert.AreEqual(upgraded, writtenBySave);
            Assert.AreEqual(upgraded, CampaignIdentity.AdoptFromSave(writtenBySave), "re-loading must keep the same identity, never mint a new one each load");
        }

        [Test]
        public void EmptyCampaignId_IsNotARealId_AndForSaveNeverWritesAnEmptyIdentity()
        {
            Assert.IsFalse(CampaignIdentity.IsRealId(null));
            Assert.IsFalse(CampaignIdentity.IsRealId(""));
            Assert.IsFalse(CampaignIdentity.IsRealId(CampaignIdentity.LegacyPlaceholderId));

            CampaignIdentity.ClearForTests();
            Assert.IsTrue(CampaignIdentity.IsRealId(CampaignIdentity.ForSave()), "a save must never write an empty campaign identity");
        }

        [Test]
        public void IdentityChanged_ReportsNewCampaignTrueOnlyForAMintedCampaign()
        {
            var observed = new List<(string id, bool isNew)>();
            void Handler(string id, bool isNew) => observed.Add((id, isNew));

            CampaignIdentity.IdentityChanged += Handler;
            try
            {
                CampaignIdentity.StartNewCampaign();
                CampaignIdentity.AdoptFromSave("aabbccddeeff00112233445566778899");
            }
            finally { CampaignIdentity.IdentityChanged -= Handler; }

            Assert.AreEqual(2, observed.Count);
            Assert.IsTrue(observed[0].isNew, "minting is a new campaign");
            Assert.IsFalse(observed[1].isNew, "adopting a save is not");
        }

        // ==================== §A3: new_game uniqueness ====================

        [Test]
        public void NewGame_IsRecordedExactlyOncePerCampaign_EvenIfAttemptedRepeatedly()
        {
            string campaign = CampaignIdentity.StartNewCampaign();
            TelemetryService.StartSession(campaign);

            for (int i = 0; i < 5; i++)
                TelemetryService.RecordFunnelEventOnce(TelemetryEvents.NewGame, i, 0f, 0f, directoryOverride: tempDir);

            Assert.AreEqual(1, CountRecorded(TelemetryEvents.NewGame), "new_game must be recorded exactly once per campaign");
        }

        [Test]
        public void NewGame_ForASecondCampaign_IsRecordedAgainUnderItsOwnId()
        {
            string first = CampaignIdentity.StartNewCampaign();
            TelemetryService.StartSession(first);
            TelemetryService.RecordFunnelEventOnce(TelemetryEvents.NewGame, 0, 0f, 0f, directoryOverride: tempDir);
            Assert.AreEqual(1, CountRecorded(TelemetryEvents.NewGame));

            // A RestartCampaign mints a genuinely different campaign -- its own new_game is correct.
            string second = CampaignIdentity.StartNewCampaign();
            Assert.AreNotEqual(first, second);
            TelemetryService.SetCampaignId(second);
            TelemetryService.RecordFunnelEventOnce(TelemetryEvents.NewGame, 1, 0f, 0f, directoryOverride: tempDir);

            Assert.AreEqual(2, CountRecorded(TelemetryEvents.NewGame), "a genuinely new campaign gets its own new_game");
            Assert.IsTrue(TelemetryService.HasFunnelEventBeenRecorded(first, TelemetryEvents.NewGame, tempDir));
            Assert.IsTrue(TelemetryService.HasFunnelEventBeenRecorded(second, TelemetryEvents.NewGame, tempDir));
        }

        [Test]
        public void AdoptedCampaign_ThatAlreadyRecordedNewGame_NeverRecordsASecondOne()
        {
            string campaign = CampaignIdentity.StartNewCampaign();
            TelemetryService.StartSession(campaign);
            TelemetryService.RecordFunnelEventOnce(TelemetryEvents.NewGame, 0, 0f, 0f, directoryOverride: tempDir);
            TelemetryService.EndSession(tempDir);

            // A later session loads that same campaign back.
            TelemetryService.InvalidateFunnelIndexCache();
            string adopted = CampaignIdentity.AdoptFromSave(campaign);
            TelemetryService.StartSession(adopted);
            TelemetryService.RecordFunnelEventOnce(TelemetryEvents.NewGame, 1, 0f, 0f, directoryOverride: tempDir);

            Assert.AreEqual(0, CountRecorded(TelemetryEvents.NewGame), "a load of an existing campaign must never produce another new_game");
        }

        [Test]
        public void SecondSystemAssetEventId_IsTheNewOne_AndTheOldMisMeasuredIdIsGone()
        {
            Assert.AreEqual("second_system_asset", TelemetryEvents.SecondSystemAsset);
            foreach (System.Reflection.FieldInfo f in typeof(TelemetryEvents).GetFields())
                Assert.AreNotEqual("second_entity", f.GetValue(null) as string,
                    "the mis-measured 'second_entity' id must be gone entirely, never silently migrated onto the new event");
        }

        // ==================== §B1/§B2: Drill T2 gate, cost, family mapping ====================

        [Test]
        public void DrillT2_Gate_IsStationConstructionT2_AndNothingElse()
        {
            Assert.AreEqual("industry.station_construction", DrillUpgradeCatalog.GateModuleId);
            Assert.AreEqual(2, DrillUpgradeCatalog.RequiredStationConstructionTier);
            Assert.AreEqual(2, DrillUpgradeCatalog.MaxTier, "only T1->T2 exists in this slice");
        }

        [Test]
        public void DrillT2_Cost_Is35BasicPartsPlus10OfThePlanetsOwnFamilyMaterial()
        {
            var expected = new Dictionary<ResourceFamily, string>
            {
                { ResourceFamily.Metal, "material.metal_alloy" },
                { ResourceFamily.Wood, "material.biocomposite" },
                { ResourceFamily.Gas, "material.refined_gas" },
                { ResourceFamily.Water, "material.purified_water" },
                { ResourceFamily.Diamond, "material.industrial_diamond" }
            };

            foreach (var kv in expected)
            {
                Assert.AreEqual(kv.Value, DrillUpgradeCatalog.MaterialIdFor(kv.Key), $"{kv.Key} must map to its real existing material id -- never a new item id");
                List<(string itemId, float amount)> cost = DrillUpgradeCatalog.Cost(kv.Key);
                Assert.AreEqual(2, cost.Count);
                Assert.AreEqual(ProcessingCatalog.BasicPartId, cost[0].itemId);
                Assert.AreEqual(35f, cost[0].amount);
                Assert.AreEqual(kv.Value, cost[1].itemId);
                Assert.AreEqual(10f, cost[1].amount);
            }
        }

        [Test]
        public void DrillT2_UpgradeDuration_IsExactlySixtySeconds()
        {
            Assert.AreEqual(60f, DrillUpgradeCatalog.UpgradeSeconds);
        }

        // ==================== §B4: production and storage formulas ====================

        [Test]
        public void DrillT2_Production_IsExactlyOnePointFiveTimesTheSameRaritysT1Rate()
        {
            foreach (Rarity rarity in System.Enum.GetValues(typeof(Rarity)))
            {
                float t1 = ExtractionCatalog.T1ProductionPerMinute(rarity);
                float t2 = ExtractionCatalog.T2ProductionPerMinute(rarity);
                Assert.AreEqual(t1 * 1.5f, t2, 0.0001f, $"{rarity}: T2 output must be exactly 1.5x its own T1 rate");
                Assert.AreEqual(t1, ExtractionCatalog.ProductionPerMinute(rarity, 1), 0.0001f);
                Assert.AreEqual(t2, ExtractionCatalog.ProductionPerMinute(rarity, 2), 0.0001f);
            }
        }

        [Test]
        public void DrillT2_StorageCapacity_IsExactlyNineHundredSecondsOfItsOwnT2Output()
        {
            foreach (Rarity rarity in System.Enum.GetValues(typeof(Rarity)))
            {
                float perSecond = ExtractionCatalog.T2ProductionPerMinute(rarity) / 60f;
                Assert.AreEqual(perSecond * 900f, ExtractionCatalog.T2StorageCapacity(rarity), 0.0001f,
                    $"{rarity}: T2 storage must hold exactly 15 minutes (900s) of its own effective T2 output");
                Assert.AreEqual(ExtractionCatalog.T2StorageCapacity(rarity), ExtractionCatalog.StorageCapacity(rarity, 2), 0.0001f);
                Assert.AreEqual(ExtractionCatalog.T1StorageCapacity(rarity), ExtractionCatalog.StorageCapacity(rarity, 1), 0.0001f);
            }
        }

        [Test]
        public void UnknownTier_ReadsAsT1_NeverAsSomethingUndefined()
        {
            Assert.AreEqual(ExtractionCatalog.T1ProductionPerMinute(Rarity.Common), ExtractionCatalog.ProductionPerMinute(Rarity.Common, 0), 0.0001f);
            Assert.AreEqual(ExtractionCatalog.T1StorageCapacity(Rarity.Common), ExtractionCatalog.StorageCapacity(Rarity.Common, 0), 0.0001f);
        }

        // ==================== §B3: transaction, halted production, refund ====================

        private static LogisticsCargo CargoWith(float parts, string materialId, float material)
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, parts);
            cargo.Add(materialId, material);
            return cargo;
        }

        /// <summary>A deterministic manifest-backed profile — the same shape every other EditMode suite here builds.</summary>
        private static PlanetProfile Profile(ResourceFamily family, Rarity rarity) => new PlanetProfile(
            new PlanetManifest(0, "sys917/pl00", family, rarity, 40000f, 0f, 0f, 0f, 1200f,
                recommendedStart: false, EnvironmentClass.Calm, surfaceLayoutSeed: 1UL));

        /// <summary>A pure Active T1 drill runtime for a given rarity/family, with no scene involved.</summary>
        private static DrillRuntime ActiveDrill(ResourceFamily family, Rarity rarity, float stored = 0f) =>
            new DrillRuntime(Profile(family, rarity), DrillLifecycle.Active, 0f, 0f, stored, 0f);

        [Test]
        public void DrillT2_StartRejects_WhenStationConstructionTierIsBelowTwo()
        {
            DrillRuntime runtime = ActiveDrill(ResourceFamily.Metal, Rarity.Common);
            LogisticsCargo cargo = CargoWith(100f, "material.metal_alloy", 100f);

            Assert.AreEqual(DrillUpgradeRejectReason.ModuleTierNotMet, DrillUpgradeService.CheckStart(runtime, 1, cargo));
            Assert.AreEqual(DrillUpgradeRejectReason.None, DrillUpgradeService.CheckStart(runtime, 2, cargo));
        }

        [Test]
        public void DrillT2_StartRejects_WhenResourcesAreShort_AndReservesNothing()
        {
            DrillRuntime runtime = ActiveDrill(ResourceFamily.Metal, Rarity.Common);
            LogisticsCargo cargo = CargoWith(34f, "material.metal_alloy", 100f); // one part short

            Assert.AreEqual(DrillUpgradeRejectReason.InsufficientResources, DrillUpgradeService.CheckStart(runtime, 2, cargo));
            Assert.IsNull(DrillUpgradeService.TryStart(runtime, "w", "drill", 2, cargo));
            Assert.AreEqual(34f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), 0.0001f, "a rejected start must never partially reserve");
        }

        [Test]
        public void DrillT2_StartRejects_OnANonActiveDrill_AndOnAnAlreadyT2Drill()
        {
            PlanetProfile profile = Profile(ResourceFamily.Metal, Rarity.Common);
            LogisticsCargo cargo = CargoWith(100f, "material.metal_alloy", 100f);

            var constructing = new DrillRuntime(profile, DrillLifecycle.Constructing, 0f, 10f, 0f, 0f);
            Assert.AreEqual(DrillUpgradeRejectReason.NotActive, DrillUpgradeService.CheckStart(constructing, 2, cargo));

            var alreadyT2 = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f, tier: 2);
            Assert.AreEqual(DrillUpgradeRejectReason.MaxTier, DrillUpgradeService.CheckStart(alreadyT2, 2, cargo));
        }

        [Test]
        public void DrillT2_Upgrade_TakesSixtySeconds_AndProducesAbsolutelyNothingMeanwhile()
        {
            DrillRuntime runtime = ActiveDrill(ResourceFamily.Metal, Rarity.Common);
            LogisticsCargo cargo = CargoWith(100f, "material.metal_alloy", 100f);
            DrillUpgradeJob job = DrillUpgradeService.TryStart(runtime, "w", "drill", 2, cargo);
            Assert.IsNotNull(job);
            runtime.BeginUpgrade(job);

            float storedAtStart = runtime.StoredAmount;
            bool completed = false;
            for (int i = 0; i < 59; i++) completed |= runtime.Tick(1f);
            Assert.IsFalse(completed, "the upgrade must not finish before its full 60s");
            Assert.AreEqual(storedAtStart, runtime.StoredAmount, 0.0001f, "an upgrading drill must produce absolutely nothing");
            Assert.IsFalse(runtime.IsProducing);

            completed = runtime.Tick(1f);
            Assert.IsTrue(completed, "the 60th second must complete the upgrade");
        }

        [Test]
        public void DrillT2_Complete_ConsumesTheReservationExactlyOnce_AndPreservesStoredResource()
        {
            DrillRuntime runtime = ActiveDrill(ResourceFamily.Metal, Rarity.Common, stored: 42f);
            LogisticsCargo cargo = CargoWith(100f, "material.metal_alloy", 100f);
            DrillUpgradeJob job = DrillUpgradeService.TryStart(runtime, "w", "drill", 2, cargo);
            runtime.BeginUpgrade(job);

            DrillUpgradeService.Complete(runtime, "w", "drill", job, cargo);

            Assert.AreEqual(2, runtime.Tier);
            Assert.AreEqual(42f, runtime.StoredAmount, 0.0001f, "an upgrade must never wipe or duplicate the drill's existing stock");
            Assert.AreEqual(65f, cargo.Get(ProcessingCatalog.BasicPartId), 0.0001f, "100 - 35 parts consumed exactly once");
            Assert.AreEqual(90f, cargo.Get("material.metal_alloy"), 0.0001f, "100 - 10 material consumed exactly once");
            Assert.IsNull(runtime.ActiveUpgradeJob);

            // A second Complete must not be able to charge again (the reservation is gone).
            DrillUpgradeService.Complete(runtime, "w", "drill", job, cargo);
            Assert.AreEqual(65f, cargo.Get(ProcessingCatalog.BasicPartId), 0.0001f, "the cost must never be consumed twice");
        }

        [Test]
        public void DrillT2_CapacityGrowsOnUpgrade_WhileTheExistingStockIsCarriedOverUntouched()
        {
            DrillRuntime runtime = ActiveDrill(ResourceFamily.Metal, Rarity.Common, stored: 50f);
            float t1Capacity = runtime.StorageCapacity;
            runtime.ApplyTierUpgrade(2);

            Assert.Greater(runtime.StorageCapacity, t1Capacity, "T2 storage must be larger than T1's");
            Assert.AreEqual(50f, runtime.StoredAmount, 0.0001f, "existing stock survives the capacity change exactly");
        }

        [Test]
        public void DrillT2_Cancel_RefundsFloorSeventyFivePercentOfEveryReservedItem()
        {
            DrillRuntime runtime = ActiveDrill(ResourceFamily.Metal, Rarity.Common);
            LogisticsCargo cargo = CargoWith(100f, "material.metal_alloy", 100f);
            DrillUpgradeJob job = DrillUpgradeService.TryStart(runtime, "w", "drill", 2, cargo);
            runtime.BeginUpgrade(job);
            runtime.Tick(20f); // partway through -- the whole cost is still reserved, nothing consumed yet

            runtime.AbortUpgrade();
            Dictionary<string, float> refunded = DrillUpgradeService.Cancel("w", "drill", cargo);

            // floor(35 * 0.75) = 26, floor(10 * 0.75) = 7
            Assert.AreEqual(26f, refunded[ProcessingCatalog.BasicPartId], 0.0001f);
            Assert.AreEqual(7f, refunded["material.metal_alloy"], 0.0001f);
            Assert.AreEqual(100f - 35f + 26f, cargo.Get(ProcessingCatalog.BasicPartId), 0.0001f);
            Assert.AreEqual(100f - 10f + 7f, cargo.Get("material.metal_alloy"), 0.0001f);
            Assert.AreEqual(1, runtime.Tier, "a cancelled upgrade must leave the drill at its current tier");
        }

        [Test]
        public void DrillT2_RestoringAnInFlightUpgrade_ReReservesOnlyTheRealRemainingCost()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 35f);
            cargo.Add("material.metal_alloy", 10f);

            var remaining = new List<(string itemId, float amount)>
            {
                (ProcessingCatalog.BasicPartId, 35f), ("material.metal_alloy", 10f)
            };
            Assert.IsTrue(DrillUpgradeService.TryRestoreReservation("w", "drill", cargo, remaining));
            Assert.AreEqual(0f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), 0.0001f, "the restored reservation must lock exactly the remaining cost");
            Assert.AreEqual(35f, cargo.Get(ProcessingCatalog.BasicPartId), 0.0001f, "restoring a reservation must never itself consume stock");
        }

        [Test]
        public void DrillT2_RestoringAnInFlightUpgrade_FailsExplicitly_WhenTheResourcesAreGone()
        {
            var cargo = new LogisticsCargo(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 5f); // nowhere near the 35 still owed

            var remaining = new List<(string itemId, float amount)> { (ProcessingCatalog.BasicPartId, 35f) };
            Assert.IsFalse(DrillUpgradeService.TryRestoreReservation("w", "drill", cargo, remaining),
                "an unfundable upgrade must fail explicitly rather than continue for free");
        }

        // ==================== §B4/§B5: Full ↔ remote 1 Hz equivalence ====================

        [Test]
        public void DrillProduction_FullPathAndRemoteDataOnlyPath_AgreeExactlyAtBothTiers()
        {
            foreach (int tier in new[] { 1, 2 })
            {
                PlanetProfile profile = Profile(ResourceFamily.Metal, Rarity.Common);
                var live = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f, tier);

                // The remote path's own arithmetic, reading the identical single authority.
                float remoteStored = 0f, remoteAccumulator = 0f;
                float perMinute = ExtractionCatalog.ProductionPerMinute(profile.Rarity, tier);
                float capacity = ExtractionCatalog.StorageCapacity(profile.Rarity, tier);

                for (int second = 0; second < 600; second++)
                {
                    live.Tick(1f);
                    remoteAccumulator += perMinute / 60f * 1f;
                    float whole = Mathf.Floor(remoteAccumulator);
                    if (whole > 0f)
                    {
                        float deposited = Mathf.Min(whole, Mathf.Max(0f, capacity - remoteStored));
                        remoteStored += deposited;
                        remoteAccumulator -= deposited;
                    }
                }

                Assert.AreEqual(live.StoredAmount, remoteStored, 0.0001f, $"tier {tier}: Full and remote 1 Hz production must agree exactly");
                Assert.AreEqual(live.StorageCapacity, capacity, 0.0001f, $"tier {tier}: both paths must read the same capacity authority");
            }
        }

        // ==================== §B5: save round trip ====================

        [Test]
        public void DrillDto_LegacySaveWithoutTierFields_RoundTripsAsAnUpgradeFreeT1()
        {
            // Exactly what a pre-VS-017A.2 save's drill JSON looks like: no tier/upgrade fields at all.
            const string legacyJson = "{\"worldId\":\"w\",\"nodeId\":\"drill\",\"state\":3,\"stored\":12.5,\"accumulator\":0.25,\"source\":0,\"refundParts\":0}";
            DrillDto dto = JsonUtility.FromJson<DrillDto>(legacyJson);

            Assert.AreEqual(1, dto.tier, "a legacy drill must load as T1 -- the field initializer, with zero migration code");
            Assert.IsFalse(dto.upgradeActive, "a legacy drill must load with no in-flight upgrade");
            Assert.AreEqual(12.5f, dto.stored, 0.0001f, "its real stored amount must survive untouched");
            Assert.IsNotNull(dto.upgradeRemainingCost);
            Assert.AreEqual(0, dto.upgradeRemainingCost.Count);
        }

        [Test]
        public void DrillDto_T2WithAnInFlightUpgrade_SurvivesAJsonRoundTripExactly()
        {
            var original = new DrillDto
            {
                worldId = "w", nodeId = "drill", state = (int)DrillLifecycle.Active,
                stored = 40f, accumulator = 0.5f, tier = 1,
                upgradeActive = true, upgradeTotalSeconds = 60f, upgradeRemainingSeconds = 23.5f
            };
            original.upgradeRemainingCost.Add(new ItemAmountDto { itemId = ProcessingCatalog.BasicPartId, amount = 35f });
            original.upgradeRemainingCost.Add(new ItemAmountDto { itemId = "material.metal_alloy", amount = 10f });

            DrillDto restored = JsonUtility.FromJson<DrillDto>(JsonUtility.ToJson(original));

            Assert.AreEqual(1, restored.tier);
            Assert.IsTrue(restored.upgradeActive);
            Assert.AreEqual(23.5f, restored.upgradeRemainingSeconds, 0.0001f, "the upgrade must resume at its exact saved progress");
            Assert.AreEqual(2, restored.upgradeRemainingCost.Count);
            Assert.AreEqual(35f, restored.upgradeRemainingCost[0].amount, 0.0001f);
            Assert.AreEqual(10f, restored.upgradeRemainingCost[1].amount, 0.0001f);
        }

        [Test]
        public void SaveSchemaGenerationAndContentRevision_AreUnchangedByThisSlice()
        {
            // The Drill T2 additions are purely additive DrillDto fields with safe initializers,
            // and no RuntimeCatalog content entry changed (drills were never in that snapshot),
            // so none of these three numbers legitimately moves.
            Assert.AreEqual(15, SaveService.CurrentSchema);
            // Old contract: 2, the generation version in force when this slice was written.
            // Current production authority: SectorManifestGenerator.GenerationVersion = 3
            // (Assets/Scripts/Nebula/Sectors/SectorManifestGenerator.cs) -- 9d33695 moved systems off the
            // uniform 2x2x2 sub-cell grid onto the galaxy spiral arms, a deliberate world-generation
            // change that regenerates every existing campaign's universe and was approved as such.
            // Invariant still protected: the Drill T2 / telemetry-semantics work is additive DTO shape
            // only and may not move it. The literal is deliberate -- reading the constant back would
            // assert nothing, so a bump has to break this test and be re-pinned alongside the world-gen
            // change that earned it.
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, Content.ContentFingerprint.ContentRevision);
        }

        // ==================== §C: Logistics Center T2 fill measurement ====================

        [Test]
        public void FiveCommonDrillT2s_FillAnEmptyLogisticsCenterT2_WithinTheApprovedTwentyFiveToThirtyFiveMinuteWindow()
        {
            LogisticsFillEstimate estimate = LogisticsFillEstimator.EstimateUniform(
                LogisticsCenterTier.T2, drillCount: 5, rarity: Rarity.Common, drillTier: 2);

            TestContext.WriteLine($"[VS-017A.2 §C] {LogisticsFillEstimator.MetricId} = {estimate.FillMinutes:0.00} min :: {estimate}");

            Assert.GreaterOrEqual(estimate.FillMinutes, 25f, $"real measured fill time was {estimate.FillMinutes:0.00} min :: {estimate}");
            Assert.LessOrEqual(estimate.FillMinutes, 35f, $"real measured fill time was {estimate.FillMinutes:0.00} min :: {estimate}");
            Assert.IsFalse(estimate.TransferLimited, $"drill output, not the center's inbound transfer ceiling, must be the binding constraint :: {estimate}");
        }

        [Test]
        public void LogisticsFillEstimator_ReadsOnlyTheRealShippedCatalogNumbers()
        {
            LogisticsFillEstimate estimate = LogisticsFillEstimator.EstimateUniform(
                LogisticsCenterTier.T2, drillCount: 5, rarity: Rarity.Common, drillTier: 2);

            Assert.AreEqual(LogisticsCenterCatalog.T2.Capacity, estimate.CenterCapacity, 0.0001f);
            Assert.AreEqual(LogisticsCenterCatalog.T2.TransferVolumePerSecond * 60f, estimate.TransferCeilingPerMinute, 0.0001f);
            Assert.AreEqual(ExtractionCatalog.T2ProductionPerMinute(Rarity.Common) * 5f, estimate.TotalProductionPerMinute, 0.0001f);
        }

        [Test]
        public void LogisticsFill_IsADiagnosticsMetric_NotOneOfTheFourteenTelemetryFunnelEvents()
        {
            foreach (System.Reflection.FieldInfo f in typeof(TelemetryEvents).GetFields())
                Assert.AreNotEqual(LogisticsFillEstimator.MetricId, f.GetValue(null) as string,
                    "the logistics fill measurement must never leak into the telemetry funnel's own event set");
        }

        // ==================== §D20: no F1-F12 references ====================

        [Test]
        public void NoFunctionKeyReferences_InAnyOfThisSlicesNewSourceFiles()
        {
            string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                for (int n = 1; n <= 12; n++)
                    if (text.Contains($"KeyCode.F{n}")) offenders.Add($"{Path.GetFileName(file)}:KeyCode.F{n}");
            }
            Assert.IsEmpty(offenders, "no source file may reference an F1-F12 key: " + string.Join(", ", offenders));
        }

        // ==================== helpers ====================

        private static int CountRecorded(string eventId)
        {
            int count = 0;
            foreach (TelemetryEventRecord e in TelemetryService.SessionEventsSnapshot)
                if (e.eventId == eventId) count++;
            return count;
        }
    }
}
