using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.UI.Menu;
using Nebula.World;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter E -- Gelişim ve Gemi Modülleri (curriculum steps 58-73).
    /// <para>
    /// The module tables (<see cref="ModuleCatalog.SecondsByTier"/>,
    /// <see cref="ModuleDef.TierCosts"/>, the slice caps) are compile-time
    /// constants, so a step that merely asserted them would be complete before
    /// the player did anything. Every reading step below therefore starts from
    /// the state in which the reading is not yet available -- no module owned,
    /// no points, no milestone -- and completes on the real transition the
    /// player's own command produces, with the constant read as the second
    /// clause. That keeps the lesson and satisfies the rule that
    /// <c>Prepare</c> may never satisfy a step's own success condition.
    /// </para>
    /// </summary>
    public static class ChapterESteps
    {
        private const string DemoModuleId = "industry.station_construction";
        private const string NuclearModuleId = "nuclear.star_energy_storage";
        private const string WeaponCoordinationId = "defense.weapon_coordination";
        private const string HangarBotId = "exploration.hangar_bot";
        private const string ScannerId = "exploration.scanner";
        private const string CargoModuleId = "industry.logistics_cargo";
        private const string ProcessingModuleId = "industry.resource_processing";

        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return Step("E-01", "İlk deneyim puanını kazan",
                "Daha önce yapmadığın bir işi tamamla ve deneyim puanı kazan.",
                "İlk kez yapılan bir iş için deneyim puanı kazan.",
                site =>
                {
                    long xpBaseline = -1L;
                    string eventId = null;
                    return TutorialSteps.Poll(
                        complete: _ => eventId != null && xpBaseline >= 0L
                                       && ProgressionService.Ledger.Contains(eventId)
                                       && ProgressionService.State.TotalXp == xpBaseline + ProgressionEvents.DrillXp,
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            eventId = ProgressionEvents.DrillFirst(site.WorldId);
                            TutorialSite.ForgetAwards(eventId);
                            xpBaseline = ProgressionService.State.TotalXp;
                        },
                        reject: _ => eventId != null && ProgressionService.Ledger.Contains(eventId)
                                     && ProgressionService.State.TotalXp > xpBaseline + ProgressionEvents.DrillXp
                            ? "BU BAŞARIM ZATEN KAZANILDI" : null);
                },
                checklist: new[] { "Olay defterine yazılsın", "Toplam XP artsın", "Aynı olay ikinci kez puan vermesin" });

            yield return Step("E-02", "Seviye atla ve gelişim puanı kazan",
                "Yeterli deneyim biriktir ve kazandığın gelişim puanını gör.",
                "Bir seviye atla; gelişim puanı kazan.",
                site =>
                {
                    int levelBaseline = -1, pointsBaseline = 0;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            if (levelBaseline < 0) return false;
                            ProgressionState state = ProgressionService.State;
                            return state.Level > levelBaseline
                                   && state.DevelopmentPoints == pointsBaseline + (state.Level - levelBaseline)
                                   && state.Level == ProgressionMath.LevelForTotalXp(state.TotalXp);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            // One XP short of the next threshold: crossing it is the step.
                            ProgressionService.Restore(ProgressionMath.TotalXpToReach(2) - 1,
                                ProgressionService.State.DevelopmentPoints, TutorialSite.LedgerSnapshot());
                            levelBaseline = ProgressionService.State.Level;
                            pointsBaseline = ProgressionService.State.DevelopmentPoints;
                        },
                        reject: _ => ProgressionService.State.IsMaxLevel ? "AZAMİ SEVİYEDE" : null);
                },
                checklist: new[] { "Seviye artsın", "Her seviye için bir gelişim puanı gelsin" });

            yield return Step("E-03", "Modül dallarını tanı",
                "Ücretsiz başlangıç modüllerini devral ve dört dalı incele.",
                "On iki modülü ve dört dalı tanı.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (ModuleCatalog.Modules.Length != 12) return false;
                        for (int i = 0; i < ModuleCatalog.FreeStartingModuleIds.Length; i++)
                            if (site.ModuleTiers.GetTier(ModuleCatalog.FreeStartingModuleIds[i]) < 1) return false;
                        return AllBranchesPresent();
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ModuleTiers.Reset();
                    },
                    reject: _ => site.ModuleTiers.GetTier(ModuleCatalog.FreeStartingModuleIds[0]) < 1
                        ? "HENÜZ HİÇBİR MODÜL DEVREDE DEĞİL" : null),
                checklist: new[] { "On iki modül listelensin", "Her modülün dalı okunsun" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("module.industry.logistics_cargo.t1",
                    "one authored module body per branch")));

            yield return Step("E-04", "Kademe ve kilometre taşını oku",
                "Bir gezegeni kontrol altına al ve Hangar Bot kilometre taşının sağlandığını gör.",
                "Modül kademesini ve kilometre taşı şartını oku.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        ModuleDef def = ModuleCatalog.Get(DemoModuleId);
                        if (def == null) return false;
                        if (def.RequiredLevelForTier(2) != 21 || def.RequiredLevelForTier(3) != 41
                            || def.RequiredLevelForTier(4) != 61 || def.RequiredLevelForTier(5) != 81) return false;
                        return ModuleMilestones.IsSatisfied(HangarBotId);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ModuleTiers.GrantFreeStartingModules();
                        // No controlled planet yet: satisfying the milestone is the step.
                        ExtractionState.GetDominance(site.WorldId)
                            .Restore(DominanceState.Unowned, 0f, 0f);
                    },
                    reject: _ => ModuleMilestones.IsSatisfied(HangarBotId)
                        ? null : "GEREKLİ: " + ModuleCatalog.Get(HangarBotId).MilestoneLabel),
                checklist: new[] { "Mevcut kademe okunsun", "Gerekli seviye okunsun", "Kilometre taşı etiketi okunsun" });

            yield return Step("E-05", "Yükseltme maliyetini oku",
                "Seçtiğin modülün bir sonraki kademesinin maliyetini oku ve malzemeyi topla.",
                "Puan, malzeme ve süre maliyetini oku.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        ModuleDef def = ModuleCatalog.Get(DemoModuleId);
                        if (def == null || site.Cargo == null) return false;
                        if (!CostTablesReadCorrectly(def)) return false;
                        ModuleTierCost cost = def.TierCosts[1];
                        foreach (ModuleResourceCost line in cost.Resources)
                            if (site.Cargo.GetAvailable(line.ItemId) < line.Amount - 0.0001f) return false;
                        return cost.Resources.Length > 0;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        ModuleDef def = ModuleCatalog.Get(DemoModuleId);
                        if (def == null) return;
                        foreach (ModuleResourceCost line in def.TierCosts[1].Resources) site.Strip(line.ItemId);
                    }),
                checklist: new[] { "Gelişim puanı maliyeti okunsun", "Malzeme listesi okunsun", "Süre okunsun" },
                assets: TutorialAssets.Of(
                    TutorialAssets.Visual("part.basic", "a cost line item"),
                    TutorialAssets.Visual("material.metal_alloy", "a cost line item")));

            yield return Step("E-06", "Kilitli modülü tanı",
                "Nükleer bir modülü seç ve neden kilitli olduğunu, açık bir modülle karşılaştırarak oku.",
                "Bu sürümde açılmayan modülü tanı.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        ModuleDef nuclear = ModuleCatalog.Get(NuclearModuleId);
                        if (nuclear == null) return false;
                        UpgradeRejectReason locked = ModuleUpgradeValidator.Validate(nuclear, 0, 1,
                            ProgressionService.State.Level, true, false, 99, site.Cargo);
                        if (locked != UpgradeRejectReason.NotImplemented) return false;
                        if (NebulaProgressionPage.RejectText(locked, nuclear) != "BU SÜRÜMDE YOK") return false;
                        // The comparison the step is about only exists once a functional
                        // module is genuinely owned next to the locked one.
                        return site.ModuleTiers.GetTier(DemoModuleId) >= 1;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ModuleTiers.Reset();
                    },
                    reject: _ =>
                    {
                        ModuleDef nuclear = ModuleCatalog.Get(NuclearModuleId);
                        return nuclear != null ? nuclear.LockedMessage : null;
                    }),
                checklist: new[] { "Satır kilitli görünsün", "Kilit gerekçesi okunsun" });

            yield return Step("E-07", "Yükseltilebilir modülü tanı",
                "Yükseltmeye hazır bir modül bul ve her kapının açık olduğunu gör.",
                "Yükseltilebilir modülü tanı.",
                site => TutorialSteps.Poll(
                    complete: _ => Validate(site, DemoModuleId) == UpgradeRejectReason.None,
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ModuleTiers.SetTier(DemoModuleId, 1);
                        TutorialSite.SetLevel(ModuleCatalog.Get(DemoModuleId).RequiredLevelForTier(2));
                        ProgressionService.Restore(ProgressionService.State.TotalXp, 0, TutorialSite.LedgerSnapshot());
                        foreach (ModuleResourceCost line in ModuleCatalog.Get(DemoModuleId).TierCosts[1].Resources)
                            site.Strip(line.ItemId);
                    },
                    reject: _ => TutorialTr.Module(Validate(site, DemoModuleId))),
                checklist: new[] { "Satır yükseltilebilir göstersin", "Yükselt düğmesi etkin olsun" });

            yield return Step("E-08", "Modül yükseltmesini başlat",
                "Yükselt düğmesine bas ve modül yükseltmesini başlat.",
                "Yükseltmeyi başlat; puan ve malzeme ayrılsın.",
                site =>
                {
                    int pointsBaseline = -1;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            ModuleUpgradeJob job = site.Upgrades?.ActiveJob;
                            if (job == null || pointsBaseline < 0) return false;
                            ModuleDef def = ModuleCatalog.Get(DemoModuleId);
                            ModuleTierCost cost = def.TierCosts[job.ToTier - 1];
                            if (job.ModuleId != DemoModuleId || job.ToTier != job.FromTier + 1) return false;
                            if (!Mathf.Approximately(job.RemainingSeconds, job.TotalSeconds)) return false;
                            if (ProgressionService.State.DevelopmentPoints != pointsBaseline - cost.DevelopmentPoints)
                                return false;
                            foreach (ModuleResourceCost line in cost.Resources)
                                if (!Mathf.Approximately(
                                    site.Cargo.GetOwnerReservation(ModuleUpgradeJob.CargoOwnerId, line.ItemId), line.Amount))
                                    return false;
                            return true;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            PrepareAffordableUpgrade(site);
                            pointsBaseline = ProgressionService.State.DevelopmentPoints;
                        },
                        reject: _ => TutorialTr.Module(Validate(site, DemoModuleId)));
                },
                checklist: new[] { "Gelişim puanı harcansın", "Malzeme rezerve edilsin", "İş kuyruğa girsin" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("module.industry.station_construction.t2",
                    "the next-tier module body")));

            yield return Step("E-09", "Kuyruğun meşgul olduğunu gör",
                "Bir yükseltme sürerken ikinci bir modülü yükseltmeyi dene.",
                "İkinci yükseltmenin reddedildiğini gör.",
                site =>
                {
                    int pointsAfterStart = -1;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            ModuleUpgradeJob job = site.Upgrades?.ActiveJob;
                            if (job == null) return false;
                            if (pointsAfterStart < 0) pointsAfterStart = ProgressionService.State.DevelopmentPoints;
                            ModuleDef second = ModuleCatalog.Get(ProcessingModuleId);
                            UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(second,
                                site.ModuleTiers.GetTier(ProcessingModuleId),
                                site.ModuleTiers.GetTier(ProcessingModuleId) + 1,
                                ProgressionService.State.Level, true, true,
                                ProgressionService.State.DevelopmentPoints, site.Cargo);
                            return reason == UpgradeRejectReason.QueueBusy
                                   && job.ModuleId == DemoModuleId
                                   && ProgressionService.State.DevelopmentPoints == pointsAfterStart;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            pointsAfterStart = -1;
                            site.Upgrades?.CancelActiveJob();
                            PrepareAffordableUpgrade(site);
                            // The second module must otherwise pass every gate, or the
                            // validator answers with an earlier reason and teaches the wrong one.
                            site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                            TutorialSite.GrantPoints(6);
                            foreach (ModuleResourceCost line in ModuleCatalog.Get(ProcessingModuleId).TierCosts[1].Resources)
                                site.Grant(line.ItemId, line.Amount);
                        },
                        reject: _ => site.Upgrades?.ActiveJob != null ? "KUYRUK MEŞGUL" : null);
                },
                checklist: new[] { "Aktif iş kartı ilerlesin", "İkinci yükseltme reddedilsin" });

            yield return Step("E-10", "Ret gerekçelerini oku",
                "Reddedilen bir yükseltmenin gerekçesini modül satırından oku.",
                "Her ret gerekçesinin Türkçe karşılığını oku.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (!EveryRejectReasonHasTurkishText()) return false;
                        // A refusal the player actually caused, rendered by the real table.
                        ModuleDef second = ModuleCatalog.Get(ProcessingModuleId);
                        UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(second,
                            site.ModuleTiers.GetTier(ProcessingModuleId),
                            site.ModuleTiers.GetTier(ProcessingModuleId) + 1,
                            ProgressionService.State.Level, true, site.Upgrades?.ActiveJob != null,
                            ProgressionService.State.DevelopmentPoints, site.Cargo);
                        return reason != UpgradeRejectReason.None
                               && !string.IsNullOrEmpty(NebulaProgressionPage.RejectText(reason, second));
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.Upgrades?.CancelActiveJob();
                        // Nothing is refused yet: the player has to produce a refusal.
                        site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                        TutorialSite.SetLevel(ModuleCatalog.Get(ProcessingModuleId).RequiredLevelForTier(2));
                        TutorialSite.GrantPoints(9);
                        foreach (ModuleResourceCost line in ModuleCatalog.Get(ProcessingModuleId).TierCosts[1].Resources)
                            site.Grant(line.ItemId, line.Amount);
                    }),
                checklist: new[] { "Seviye, kilometre taşı, puan, malzeme ve kuyruk gerekçeleri ayırt edilsin" });

            yield return Step("E-11", "Yükseltmeyi tamamla",
                "Yükseltme bitene kadar bekle ve modülün yeni kademesini gör.",
                "Yükseltme tamamlansın; kademe bir artsın.",
                site =>
                {
                    int tierBaseline = -1;
                    float storedBaseline = 0f;
                    int completions = 0;
                    string costItemId = null;
                    float costAmount = 0f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            if (tierBaseline < 0 || costItemId == null) return false;
                            return site.ModuleTiers.GetTier(DemoModuleId) == tierBaseline + 1
                                   && site.Upgrades?.ActiveJob == null
                                   && completions == 1
                                   && Mathf.Approximately(
                                       site.Cargo.GetOwnerReservation(ModuleUpgradeJob.CargoOwnerId, costItemId), 0f)
                                   && Mathf.Approximately(site.Cargo.GetStored(costItemId), storedBaseline - costAmount);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            completions = 0;
                            PrepareAffordableUpgrade(site);
                            tierBaseline = site.ModuleTiers.GetTier(DemoModuleId);
                            ModuleTierCost cost = ModuleCatalog.Get(DemoModuleId).TierCosts[tierBaseline];
                            costItemId = cost.Resources.Length > 0 ? cost.Resources[0].ItemId : ProcessingCatalog.BasicPartId;
                            costAmount = cost.Resources.Length > 0 ? cost.Resources[0].Amount : 0f;
                            storedBaseline = site.Cargo.GetStored(costItemId);

                            ModuleUpgradeService upgrades = site.Upgrades;
                            if (upgrades == null) return;
                            upgrades.ScreenPaused = false;
                            Action<ModuleUpgradeJob> onCompleted = _ => completions++;
                            upgrades.JobCompleted += onCompleted;
                            ctx.Scope.OnCleanup(() => upgrades.JobCompleted -= onCompleted);
                            upgrades.TryStartUpgrade(DemoModuleId);
                            storedBaseline = site.Cargo.GetStored(costItemId);
                        },
                        reject: _ => site.Upgrades != null && site.Upgrades.ActiveJob != null
                                     && site.Upgrades.ActiveJob.Paused ? "DURAKLATILDI" : null);
                },
                checklist: new[] { "Ayrılan malzeme kalıcı tüketilsin", "Kademe bir artsın", "Etki hemen uygulansın" });

            yield return Step("E-12", "Geminin görünümünün değiştiğini gör",
                "Yükseltme bittikten sonra modülün gemi üzerindeki görünümünü incele.",
                "Modül gövdesinin kademeyle değiştiğini gör.",
                site =>
                {
                    ShipModuleVisuals visuals = null;
                    return TutorialSteps.OnEvent(
                        subscribe: (ctx, fired) =>
                        {
                            if (visuals == null) return null;
                            string anchorId = ModuleCatalog.Get(DemoModuleId).VisualAnchorId;
                            void Handler(ModuleVisualHookArgs args)
                            {
                                if (args.AnchorId == anchorId
                                    && args.AssetId == $"module.{DemoModuleId}.t{site.ModuleTiers.GetTier(DemoModuleId)}")
                                    fired();
                            }
                            visuals.TierVisualChanged += Handler;
                            return () => visuals.TierVisualChanged -= Handler;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            if (site.Ship == null) return;
                            // The anchors have to exist by name before Initialize, or the
                            // module is skipped entirely and nothing can ever be reported.
                            for (int i = 0; i < ShipModuleVisuals.VisualizedModuleIds.Length; i++)
                            {
                                ModuleDef def = ModuleCatalog.Get(ShipModuleVisuals.VisualizedModuleIds[i]);
                                if (def == null || site.Ship.transform.Find(def.VisualAnchorId) != null) continue;
                                var anchor = new GameObject(def.VisualAnchorId);
                                anchor.transform.SetParent(site.Ship.transform, false);
                            }
                            visuals = site.Ship.GetComponent<ShipModuleVisuals>()
                                      ?? site.Ship.gameObject.AddComponent<ShipModuleVisuals>();
                            site.ModuleTiers.SetTier(DemoModuleId, 1);
                            visuals.Initialize(site.ModuleTiers, site.Upgrades);
                        });
                },
                checklist: new[] { "Modül gövdesi yeniden kurulsun", "Kademe işaretleri artsın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("module.exploration.scanner.t2",
                    "a tier-swapped module body")));

            yield return Step("E-13", "Azami kademeyi tanı",
                "Bu sürümde azami kademeye ulaşmış bir modülü seç ve daha fazla yükseltilemediğini gör.",
                "Dilim üst sınırına ulaşan modülü tanı.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        ModuleDef def = ModuleCatalog.Get(DemoModuleId);
                        if (def == null) return false;
                        int tier = site.ModuleTiers.GetTier(DemoModuleId);
                        if (tier < def.MaxTierThisSlice) return false;
                        UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(def, tier, tier + 1,
                            ProgressionService.State.Level, true, false, 99, site.Cargo);
                        return reason == UpgradeRejectReason.NotImplemented
                               || reason == UpgradeRejectReason.AlreadyMaxTier;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ModuleTiers.SetTier(DemoModuleId, 1);
                    },
                    reject: _ =>
                    {
                        ModuleDef def = ModuleCatalog.Get(DemoModuleId);
                        int tier = site.ModuleTiers.GetTier(DemoModuleId);
                        return def != null && tier >= def.MaxTierThisSlice ? "AZAMİ SEVİYEDE" : null;
                    }),
                checklist: new[] { "Satır AZAMİ SEVİYEDE göstersin", "Yükselt düğmesi kapalı olsun" });

            yield return Step("E-14", "İstasyon İnşası kapısını öğren",
                "İstasyon İnşası'nı T2'ye çıkar ve sondaj T2 yükseltmesinin açıldığını gör.",
                "Modül kapısının aşağı akışta ne açtığını gör.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        if (drill == null) return false;
                        int gate = site.ModuleTiers.GetTier(DrillUpgradeCatalog.GateModuleId);
                        return gate >= DrillUpgradeCatalog.RequiredStationConstructionTier
                               && DrillUpgradeService.CheckStart(drill.Runtime, gate, site.Cargo)
                                  == DrillUpgradeRejectReason.None;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.InstallDrill(DrillLifecycle.Active);
                        site.ModuleTiers.SetTier(DrillUpgradeCatalog.GateModuleId, 1);
                        if (site.Profile != null) site.GrantAll(DrillUpgradeCatalog.Cost(site.Profile.Family));
                        TutorialSite.SetLevel(ModuleCatalog.Get(DemoModuleId).RequiredLevelForTier(2));
                    },
                    reject: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        if (drill == null) return null;
                        int gate = site.ModuleTiers.GetTier(DrillUpgradeCatalog.GateModuleId);
                        return TutorialTr.Drill(DrillUpgradeService.CheckStart(drill.Runtime, gate, site.Cargo));
                    }),
                checklist: new[] { "Modül T2 olsun", "Sondaj T2 yükseltmesi kabul edilsin" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("module.industry.station_construction.t2",
                    "the gate module at T2")));

            yield return Step("E-15", "Silah Koordinasyonu kapısını öğren",
                "Sistemde aktif bir komuta merkezi kur ve Silah Koordinasyonu kilometre taşının açıldığını gör.",
                "Kilometre taşının gerçek bir yapıya bağlı olduğunu gör.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        string fullId = StarSystemRegistry.Streaming != null
                            ? StarSystemRegistry.Streaming.FullSystemId : site.SystemId;
                        bool commandUp = ExtractionState.HasActiveCommand(site.WorldId)
                                         || SystemStatusQuery.HasActiveCommandAnywhere(fullId);
                        if (!commandUp) return false;
                        return StarSystemRegistry.Streaming == null
                               || ModuleMilestones.IsSatisfied(WeaponCoordinationId);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        TutorialSite.SetLevel(ModuleCatalog.Get(WeaponCoordinationId).UnlockLevel);
                    },
                    reject: _ => ExtractionState.HasActiveCommand(site.WorldId)
                        ? null : "GEREKLİ: " + ModuleCatalog.Get(WeaponCoordinationId).MilestoneLabel),
                checklist: new[] { "Kilometre taşı sağlanmadan ret alınsın", "Komuta merkezi aktifken ret kalksın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.command.t1",
                    "the Command Center the milestone reads")));

            yield return Step("E-16", "Dalların gerçek etkisini öğren",
                "Yükselttiğin modüllerin etkisini uygula ve gemide neyin değiştiğini oku.",
                "Modül etkilerinin gerçek çalışma değerlerini gör.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (site.Cargo == null || site.Processing == null || site.Extraction == null) return false;
                        int cargoTier = site.ModuleTiers.GetTier(CargoModuleId);
                        int processingTier = site.ModuleTiers.GetTier(ProcessingModuleId);
                        int scannerTier = site.ModuleTiers.GetTier(ScannerId);
                        if (cargoTier < 1 || processingTier < 1 || scannerTier < 1) return false;
                        return Mathf.Approximately(site.Cargo.Capacity, ModuleCatalog.CargoCapacityByTier[cargoTier - 1])
                               // Processing's tier is recorded and buys nothing: with one
                               // bench and no duration there is no slot count and no speed
                               // for it to multiply. The reading is that the effect ran and
                               // the bench is there to be used, not an invented benefit.
                               && site.Processing.Tier == processingTier
                               && site.Processing.Bench != null
                               && Mathf.Approximately(site.Extraction.ScannerScanSeconds,
                                   ModuleCatalog.ScanSecondsByTier[scannerTier - 1])
                               && Mathf.Approximately(site.Extraction.ScannerProximityR,
                                   ModuleCatalog.ScanRadiusMultiplierByTier[scannerTier - 1]);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        // Tiers owned, effects deliberately not applied yet: applying them
                        // is the action, and the readings are what it changes.
                        site.ModuleTiers.SetTier(CargoModuleId, 1);
                        site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                        site.ModuleTiers.SetTier(ScannerId, 0);
                        site.Cargo.Clear();
                        site.Cargo.SetCapacity(1f);
                    },
                    reject: _ =>
                    {
                        ModuleDef nuclear = ModuleCatalog.Get(NuclearModuleId);
                        return site.ModuleTiers.GetTier(ScannerId) < 1 && nuclear != null
                            ? ModuleCatalog.LockedMessageNotImplemented : null;
                    }),
                checklist: new[] { "Kargo kapasitesi", "İşleme tezgâhı", "Tarama", "Menzil çarpanı" });
        }

        // ---- shared helpers ---------------------------------------------------

        private static bool AllBranchesPresent()
        {
            bool industry = false, exploration = false, defense = false, nuclear = false;
            for (int i = 0; i < ModuleCatalog.Modules.Length; i++)
            {
                switch (ModuleCatalog.Modules[i].Branch)
                {
                    case ModuleBranch.Industry: industry = true; break;
                    case ModuleBranch.Exploration: exploration = true; break;
                    case ModuleBranch.Defense: defense = true; break;
                    case ModuleBranch.Nuclear: nuclear = true; break;
                }
            }
            return industry && exploration && defense && nuclear;
        }

        private static bool CostTablesReadCorrectly(ModuleDef def)
        {
            int[] expectedPoints = { 1, 1, 1, 2, 3 };
            float[] expectedSeconds = { 30f, 90f, 180f, 360f, 720f };
            for (int t = 0; t < def.TierCosts.Length && t < expectedPoints.Length; t++)
            {
                if (def.TierCosts[t].DevelopmentPoints != expectedPoints[t]) return false;
                if (!Mathf.Approximately(def.TierCosts[t].Seconds, expectedSeconds[t])) return false;
            }
            return ModuleCatalog.TotalSpendablePoints() == 93;
        }

        private static bool EveryRejectReasonHasTurkishText()
        {
            ModuleDef def = ModuleCatalog.Get(DemoModuleId);
            foreach (UpgradeRejectReason reason in Enum.GetValues(typeof(UpgradeRejectReason)))
            {
                if (reason == UpgradeRejectReason.None) continue;
                string text = NebulaProgressionPage.RejectText(reason, def);
                if (string.IsNullOrEmpty(text) || text == reason.ToString()) return false;
            }
            return true;
        }

        private static UpgradeRejectReason Validate(TutorialSite site, string moduleId)
        {
            ModuleDef def = ModuleCatalog.Get(moduleId);
            int current = site.ModuleTiers.GetTier(moduleId);
            return ModuleUpgradeValidator.Validate(def, current, current + 1, ProgressionService.State.Level,
                ModuleMilestones.IsSatisfied(moduleId), site.Upgrades?.ActiveJob != null,
                ProgressionService.State.DevelopmentPoints, site.Cargo);
        }

        /// <summary>Everything the demo module's next tier needs, and nothing started yet.</summary>
        internal static void PrepareAffordableUpgrade(TutorialSite site)
        {
            ModuleDef def = ModuleCatalog.Get(DemoModuleId);
            site.Upgrades?.CancelActiveJob();
            site.Cargo?.ReleaseReservation(ModuleUpgradeJob.CargoOwnerId);
            site.ModuleTiers.SetTier(DemoModuleId, 1);
            TutorialSite.SetLevel(def.RequiredLevelForTier(2));
            TutorialSite.GrantPoints(def.TierCosts[1].DevelopmentPoints + 4);
            foreach (ModuleResourceCost line in def.TierCosts[1].Resources) site.Grant(line.ItemId, line.Amount);
        }

        private static TutorialStepDefinition Step(string id, string title, string instruction, string summary,
            Func<TutorialSite, ITutorialStepRuntime> build, string[] checklist = null,
            TutorialAssetRequirement[] assets = null, Func<string> keyHint = null) =>
            new TutorialStepDefinition(TutorialChapterId.Progression, id, title, instruction, summary,
                () => build(new TutorialSite()), checklist, assets, keyHint);
    }
}
