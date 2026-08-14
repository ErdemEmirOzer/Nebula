using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.UI.Bench;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter D -- İşleme, Ekonomi ve Lojistik (curriculum steps 42-57).
    /// <para>
    /// Three production facts shape the processing half of this chapter. First,
    /// there is no timer: a conversion is one atomic transaction in
    /// <see cref="ProcessingBenchCommands.TryConvert"/> that debits the bench and
    /// credits the hold before it returns. Nothing here may wait for seconds to
    /// pass, and a step that "completes when the bar fills" would never complete at
    /// all. Second, input is physically <i>moved</i> onto
    /// <see cref="ProcessingBenchRuntime"/> rather than reserved in the hold, so
    /// every step below watches the pair — what left the hold and what arrived on
    /// the bench — instead of a reservation that no longer exists. Third, the
    /// tutorial's <c>NebulaContext</c> never composes <c>Presentation</c>, so no
    /// step can read a page's row count; each step validates the real transaction
    /// the page performs instead of the page having been opened.
    /// </para>
    /// <para>
    /// That last point is the rule the processing steps are written to: every one
    /// of them advances on a <b>transaction</b> — a deposit, a recipe actually run,
    /// a withdrawal — and none of them advances because a screen appeared. Several
    /// watch a single poll-to-poll jump rather than an end state, because "the
    /// player chose an amount" and "the player pressed the same button twice" reach
    /// the same end state and are not the same lesson.
    /// </para>
    /// </summary>
    public static class ChapterDSteps
    {
        private const string ProcessingModuleId = "industry.resource_processing";
        private const string CargoModuleId = "industry.logistics_cargo";

        /// <summary>The bench on the tutorial's ship, or <c>null</c> before one exists.</summary>
        private static ProcessingBenchRuntime BenchOf(TutorialSite site) =>
            site.Processing != null ? site.Processing.Bench : null;

        /// <summary>The two recipes a raw family feeds: parts at index <c>f*2</c>, material at <c>f*2+1</c>.</summary>
        private static ProcessingRecipe PartsRecipe(int family) => ProcessingCatalog.Recipes[family * 2];

        /// <inheritdoc cref="PartsRecipe"/>
        private static ProcessingRecipe MaterialRecipe(int family) => ProcessingCatalog.Recipes[family * 2 + 1];

        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return Step("D-01", "Kargodan tezgâha kaynak taşı",
                "Kargo satırındaki ham kaynağı işleme tezgâhına sürükle — ya da satırı seçip TEZGÂHA EKLE'ye bas.",
                "Ham kaynağı gemi kargosundan tezgâha taşı.",
                site =>
                {
                    string rawId = null;
                    float rawBaseline = -1f, partsBaseline = 0f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            ProcessingBenchRuntime bench = BenchOf(site);
                            if (bench == null || rawId == null || rawBaseline < 0f || site.Cargo == null)
                                return false;
                            if (bench.InputItemId != rawId || bench.InputAmount < 1) return false;

                            // Moved, not copied and not reserved: exactly what the bench
                            // gained is what the hold lost, and nothing was produced on
                            // the way -- loading the bench is not a conversion.
                            float leftTheHold = rawBaseline - site.Cargo.GetStored(rawId);
                            return Mathf.Approximately(leftTheHold, bench.InputAmount)
                                   && site.Cargo.Get(ProcessingCatalog.BasicPartId) <= partsBaseline + 0.0001f;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                            BenchOf(site)?.Clear();
                            rawId = ExtractionCatalog.ResourceIds[0];
                            site.Strip(rawId);
                            site.Grant(rawId, 10f);
                            rawBaseline = site.Cargo.GetStored(rawId);
                            partsBaseline = site.Cargo.Get(ProcessingCatalog.BasicPartId);
                        },
                        reject: _ => rawId != null && site.Cargo != null
                                     && ProcessingBenchCommands.WholeUnitsAvailable(site.Cargo, rawId) < 1
                            ? NebulaBenchMessages.Deposit(BenchDepositReason.InsufficientCargo) : null);
                },
                checklist: new[]
                {
                    "Satır tezgâha sürüklensin (ya da TEZGÂHA EKLE ile aynı kutu açılsın)",
                    "Miktar kutusu onaylanmadan hiçbir şey hareket etmesin",
                    "Kargodan çıkan miktar tezgâha giren miktara eşit olsun",
                },
                assets: TutorialAssets.Of(TutorialAssets.Visual("resource.metal", "the raw resource being carried to the bench")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipProcessing.MakePartsKey));

            yield return Step("D-02", "Miktarı seç",
                "Miktar kutusuna bir sayı gir (−/+/1/MAKS) ve tek onayla birden fazla birim yükle.",
                "Tek bir onayla tezgâha en az üç birim koy.",
                site =>
                {
                    string rawId = null;
                    int seen = 0, biggestJump = 0;
                    float rawBefore = -1f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            ProcessingBenchRuntime bench = BenchOf(site);
                            if (bench == null || rawId == null || site.Cargo == null) return false;

                            int now = bench.InputItemId == rawId ? bench.InputAmount : 0;
                            float holdNow = site.Cargo.GetStored(rawId);

                            // One poll-to-poll jump, not the end total. Three presses of
                            // "1" reach the same bench amount as one confirmation of "3"
                            // and are a different lesson; only the second is a quantity
                            // being chosen, and only the second moves three units at once.
                            if (now > seen && rawBefore >= 0f
                                && Mathf.Approximately(rawBefore - holdNow, now - seen))
                                biggestJump = Mathf.Max(biggestJump, now - seen);

                            seen = now;
                            rawBefore = holdNow;
                            return biggestJump >= 3;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                            BenchOf(site)?.Clear();
                            rawId = ExtractionCatalog.ResourceIds[0];
                            site.Strip(rawId);
                            site.Grant(rawId, 12f);
                            seen = 0;
                            biggestJump = 0;
                            rawBefore = site.Cargo.GetStored(rawId);
                        },
                        reject: _ => rawId != null && site.Cargo != null
                                     && ProcessingBenchCommands.WholeUnitsAvailable(site.Cargo, rawId) < 3
                            ? "TEK SEFERDE ÜÇ BİRİM İÇİN YETERLİ KAYNAK YOK" : null);
                },
                checklist: new[]
                {
                    "MAKS ve 1 kısayolları çalışsın",
                    "Boş kutu onaylanmasın",
                    "Tek onayda en az üç birim tezgâha geçsin",
                });

            yield return Step("D-03", "Tarifi seç",
                "Tezgâhtaki girdinin iki tarifi de listelenir. Yapı parçası tarifini seç ve DÖNÜŞTÜR'e bas.",
                "İki tarif arasından yapı parçası tarifini çalıştır.",
                site =>
                {
                    int family = 0;
                    float partsBaseline = 0f, materialBaseline = 0f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            if (site.Cargo == null) return false;
                            ProcessingRecipe parts = PartsRecipe(family);
                            ProcessingRecipe material = MaterialRecipe(family);

                            // The choice is the lesson: the parts recipe ran and the
                            // material recipe did not. Both were offered for the same
                            // bench input, so producing one and not the other is the
                            // only evidence a selection actually happened.
                            return site.Cargo.Get(parts.OutputItemId) >= partsBaseline + 1f - 0.0001f
                                   && site.Cargo.Get(material.OutputItemId) <= materialBaseline + 0.0001f;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                            family = 0;
                            BenchOf(site)?.Clear();
                            site.Strip(ProcessingCatalog.MaterialIds[family]);
                            site.Strip(ProcessingCatalog.BasicPartId);
                            site.Grant(ExtractionCatalog.ResourceIds[family], 12f);
                            partsBaseline = site.Cargo.Get(ProcessingCatalog.BasicPartId);
                            materialBaseline = site.Cargo.Get(ProcessingCatalog.MaterialIds[family]);
                        },
                        reject: _ =>
                        {
                            ProcessingBenchRuntime bench = BenchOf(site);
                            return bench == null || bench.IsEmpty
                                ? NebulaBenchMessages.BenchEmptyHint : null;
                        });
                },
                checklist: new[]
                {
                    "Girdiye uyan bütün tarifler kart olarak görünsün",
                    "Her kart oranını ve çıktısını yazsın",
                    "Seçilen tarifin çıktısı kargoya gelsin",
                },
                assets: TutorialAssets.Of(
                    TutorialAssets.Visual("part.basic", "the parts recipe output"),
                    TutorialAssets.Visual("material.metal_alloy", "the material recipe output")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipProcessing.MakeMaterialKey));

            yield return Step("D-04", "Dönüşüm miktarını seç ve anında sonucu gör",
                "İşlenmiş malzeme tarifinde tek onayla iki partilik girdi harca; sonuç anında kargoya düşer.",
                "Tek dönüşümde en az iki çıktı üret.",
                site =>
                {
                    int family = 0;
                    int benchSeen = 0;
                    float outputSeen = -1f;
                    bool done = false;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            ProcessingBenchRuntime bench = BenchOf(site);
                            ProcessingRecipe recipe = MaterialRecipe(family);
                            if (bench == null || site.Cargo == null) return false;

                            int benchNow = bench.InputItemId == recipe.InputItemId ? bench.InputAmount : 0;
                            float outputNow = site.Cargo.Get(recipe.OutputItemId);

                            if (outputSeen >= 0f)
                            {
                                int produced = Mathf.RoundToInt(outputNow - outputSeen);
                                int spent = benchSeen - benchNow;
                                // Input and output move in the same observation because
                                // they move in the same transaction. A model with a
                                // duration could not satisfy this: the bench would fall
                                // first and the hold would rise seconds later.
                                if (produced >= 2 && spent == produced * recipe.InputCount) done = true;
                            }

                            benchSeen = benchNow;
                            outputSeen = outputNow;
                            return done;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                            family = 0;
                            done = false;
                            BenchOf(site)?.Clear();
                            site.Strip(ProcessingCatalog.MaterialIds[family]);
                            site.Grant(ExtractionCatalog.ResourceIds[family], 12f);
                            benchSeen = 0;
                            outputSeen = -1f;
                            TutorialSite.ForgetAwards(
                                ProgressionEvents.RecipeFirst(MaterialRecipe(family).Id));
                        },
                        reject: _ =>
                        {
                            ProcessingBenchRuntime bench = BenchOf(site);
                            ProcessingRecipe recipe = MaterialRecipe(family);
                            if (bench == null || bench.IsEmpty) return NebulaBenchMessages.BenchEmptyHint;
                            return bench.InputAmount < recipe.InputCount * 2
                                ? NebulaBenchMessages.Convert(
                                    BenchConvertReason.InsufficientBenchInput, recipe)
                                : null;
                        });
                },
                checklist: new[]
                {
                    "Miktar tarifin girdi sayısının tam katı olsun",
                    "Çıktı ve tezgâhta kalacak miktar önizlensin",
                    "Sonuç anında görünsün — sayaç ya da geri sayım yok",
                },
                assets: TutorialAssets.Of(TutorialAssets.Visual("material.metal_alloy",
                    "the material the conversion produces")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipProcessing.MakeMaterialKey));

            yield return Step("D-05", "Kalan girdiyi kargoya geri al",
                "Dönüşmeyen girdi tezgâhta kalır. KARGOYA GERİ AL ile bir kısmını, TÜMÜNÜ GERİ AL ile hepsini geri çek.",
                "Tezgâhta kalan girdiyi kargoya geri al.",
                site =>
                {
                    string rawId = null;
                    int benchSeen = -1;
                    float holdSeen = -1f;
                    bool returned = false;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            ProcessingBenchRuntime bench = BenchOf(site);
                            if (bench == null || rawId == null || site.Cargo == null) return false;

                            int benchNow = bench.InputItemId == rawId ? bench.InputAmount : 0;
                            float holdNow = site.Cargo.GetStored(rawId);

                            // A withdrawal is the deposit run backwards, and conservation
                            // is what proves it: the bench falls by exactly what the hold
                            // gains, in one observation, with nothing produced.
                            if (benchSeen > benchNow && holdSeen >= 0f
                                && Mathf.Approximately(holdNow - holdSeen, benchSeen - benchNow))
                                returned = true;

                            benchSeen = benchNow;
                            holdSeen = holdNow;
                            return returned;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                            rawId = ExtractionCatalog.ResourceIds[0];
                            returned = false;
                            site.Strip(rawId);
                            site.Cargo.SetCapacity(ModuleCatalog.CargoCapacityByTier[2]);
                            // The bench starts loaded, which is the state the step is
                            // about: leftover input after a conversion that did not use
                            // all of it. Restore is the save path and writes the bench
                            // without inventing stock in the hold.
                            BenchOf(site)?.Restore(rawId, 5);
                            benchSeen = 5;
                            holdSeen = site.Cargo.GetStored(rawId);
                        },
                        reject: _ =>
                        {
                            ProcessingBenchRuntime bench = BenchOf(site);
                            BenchWithdrawReason reason =
                                ProcessingBenchCommands.ValidateWithdraw(bench, site.Cargo, 1);
                            return reason == BenchWithdrawReason.None
                                ? null : NebulaBenchMessages.Withdraw(reason);
                        });
                },
                checklist: new[]
                {
                    "Tezgâhtaki miktar azalsın",
                    "Kargodaki miktar aynı kadar artsın",
                    "Hiçbir kaynak kaybolmasın",
                });

            yield return Step("D-06", "Beş işlenmiş malzemeyi tanı",
                "Her ham aileyi sırayla tezgâha koy, malzeme tarifini çalıştır ve beş aileyi tanı.",
                "Beş işlenmiş malzemenin hepsini üret.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (site.Cargo == null) return false;
                        for (int f = 0; f < ProcessingCatalog.MaterialIds.Length; f++)
                        {
                            string id = ProcessingCatalog.MaterialIds[f];
                            if (site.Cargo.Get(id) < 1f) return false;
                            if (!Mathf.Approximately(LogisticsCargo.VolumePerUnit(id), 2f)) return false;
                        }
                        return true;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                        BenchOf(site)?.Clear();
                        for (int f = 0; f < ProcessingCatalog.MaterialIds.Length; f++)
                            site.Strip(ProcessingCatalog.MaterialIds[f]);
                        // Five materials at 2 volume plus 15 raw will not fit in the 150
                        // base hold, so the hold is sized first through the real seam.
                        site.Cargo.SetCapacity(ModuleCatalog.CargoCapacityByTier[2]);
                        for (int f = 0; f < ExtractionCatalog.ResourceIds.Length; f++)
                            site.Grant(ExtractionCatalog.ResourceIds[f], 3f);
                    },
                    reject: _ =>
                    {
                        if (site.Cargo == null) return null;
                        ProcessingBenchRuntime bench = BenchOf(site);
                        for (int f = 0; f < ExtractionCatalog.ResourceIds.Length; f++)
                        {
                            if (site.Cargo.Get(ProcessingCatalog.MaterialIds[f]) >= 1f) continue;
                            string rawId = ExtractionCatalog.ResourceIds[f];
                            int onBench = bench != null && bench.InputItemId == rawId ? bench.InputAmount : 0;
                            if (ProcessingBenchCommands.WholeUnitsAvailable(site.Cargo, rawId) + onBench < 3)
                                return NebulaBenchMessages.Deposit(BenchDepositReason.InsufficientCargo);
                        }
                        // Single-type by design: the next family cannot go on until the
                        // current one has been converted or taken back.
                        return bench != null && !bench.IsEmpty
                               && site.Cargo.Get(ProcessingCatalog.MaterialIds[
                                   RawIndex(bench.InputItemId)]) >= 1f
                            ? NebulaBenchMessages.Deposit(BenchDepositReason.DifferentInputAlreadyLoaded)
                            : null;
                    }),
                checklist: new[] { "Metal Alaşım", "Biyokompozit", "Rafine Gaz", "Arıtılmış Su", "Endüstriyel Elmas" },
                assets: TutorialAssets.Of(
                    TutorialAssets.Visual("material.metal_alloy", "processed material 1"),
                    TutorialAssets.Visual("material.biocomposite", "processed material 2"),
                    TutorialAssets.Visual("material.refined_gas", "processed material 3"),
                    TutorialAssets.Visual("material.purified_water", "processed material 4"),
                    TutorialAssets.Visual("material.industrial_diamond", "processed material 5")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipProcessing.MakeMaterialKey));

            yield return Step("D-07", "Çıktıyı topla",
                "Ürettiğin parçanın ve malzemenin kargo listesinde ayrı satırlar olduğunu doğrula.",
                "Üretilen kalemleri kargo listesinden oku.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (site.Cargo == null) return false;
                        // The hold hides anything under half a unit as rounding noise, so
                        // "a readable row" means at least that much of both outputs.
                        return site.Cargo.Get(ProcessingCatalog.BasicPartId) >= 0.5f
                               && site.Cargo.Get(ProcessingCatalog.MaterialIds[0]) >= 0.5f;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ModuleTiers.SetTier(ProcessingModuleId, 1);
                        BenchOf(site)?.Clear();
                        site.Strip(ProcessingCatalog.BasicPartId);
                        site.Strip(ProcessingCatalog.MaterialIds[0]);
                        site.Grant(ExtractionCatalog.ResourceIds[0], 12f);
                    }),
                checklist: new[] { "Kargo satırı görünsün", "Mevcut / rezerve / kullanılabilir miktarlar okunsun" });

            yield return Step("D-08", "Gemi kargo kapasitesini öğren",
                "Lojistik Kargo modülünü devreye al ve kapasitenin kademeden geldiğini gör.",
                "Kargo kapasitesinin modül etkisi olduğunu gör.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (site.Cargo == null) return false;
                        int tier = site.ModuleTiers.GetTier(CargoModuleId);
                        if (tier < 1) return false;
                        return Mathf.Approximately(site.Cargo.Capacity, ModuleCatalog.CargoCapacityByTier[tier - 1])
                               && !site.Cargo.OverCapacity;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.Cargo.Clear();
                        site.ModuleTiers.SetTier(CargoModuleId, 0);
                        site.Cargo.SetCapacity(1f);
                    },
                    reject: _ => site.Cargo != null && site.Cargo.OverCapacity
                        ? "KAPASİTE AŞILDI — TOPLAMA DURDU" : null),
                checklist: new[] { "Kullanılan hacim / kapasite okunsun", "Kapasite aşımı uyarısı tanınsın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("module.industry.logistics_cargo.t1",
                    "the cargo module whose tier sets capacity")));

            yield return Step("D-09", "Ayrılan ve kullanılabilir kaynağı ayırt et",
                "Bir işe kaynak ayır ve fiziksel stokun yerinde kaldığını gör.",
                "Ayrılan ve harcanabilir kaynağı ayırt et.",
                site =>
                {
                    float storedBaseline = -1f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            if (site.Cargo == null || storedBaseline < 0f) return false;
                            float stored = site.Cargo.GetStored(ProcessingCatalog.BasicPartId);
                            float reserved = site.Cargo.GetReserved(ProcessingCatalog.BasicPartId);
                            float available = site.Cargo.GetAvailable(ProcessingCatalog.BasicPartId);
                            return reserved > 0.0001f
                                   && Mathf.Approximately(stored, storedBaseline)
                                   && Mathf.Approximately(available, stored - reserved);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.Cargo.ReleaseReservation(TutorialReserveOwner);
                            site.Grant(ProcessingCatalog.BasicPartId, 40f);
                            storedBaseline = site.Cargo.GetStored(ProcessingCatalog.BasicPartId);
                        },
                        reject: _ => site.Cargo != null
                                     && site.Cargo.GetReserved(ProcessingCatalog.BasicPartId) > 0.0001f
                                     && site.Cargo.GetAvailable(ProcessingCatalog.BasicPartId) <= 0.0001f
                            ? "AYRILAN KAYNAK KULLANILAMAZ" : null);
                },
                checklist: new[] { "Fiziksel stok değişmesin", "Kullanılabilir miktar düşsün" });

            yield return Step("D-10", "Lojistik merkezi inşa et",
                "Sistem çapasına yaklaş ve lojistik merkezinin T1 inşasını başlat.",
                "Sistem lojistik merkezini kur.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                        return center != null && center.Runtime.State == LogisticsCenterState.Constructing;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.RemoveCenter();
                        TutorialSite.SetLevel(LogisticsCenterCatalog.T1.RequiredLevel);
                        site.ModuleTiers.SetTier(LogisticsCenterCatalog.StationConstructionModuleId, 1);
                        site.GrantAll(LogisticsCenterCatalog.T1.Cost());
                    },
                    reject: ctx =>
                    {
                        var access = site.Ship != null ? site.Ship.GetComponent<ShipLogisticsCenter>() : null;
                        return access != null ? TutorialTr.CenterBuild(access.CheckBuild()) : null;
                    }),
                checklist: new[] { "Çapa menziline gir", "Maliyet düşülsün", "İnşaat başlasın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("station.system_logistics.t1",
                    "the orbital logistics center")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipLogisticsCenter.BuildOrUpgradeKey));

            yield return Step("D-11", "Merkeze yükle ve merkezden al",
                "Aktif merkeze kaynak yükle, sonra bir kısmını geri al.",
                "Merkez envanteriyle gemi kargosu arasında kaynak taşı.",
                site =>
                {
                    string itemId = ProcessingCatalog.BasicPartId;
                    float shipBaseline = -1f, shipLowWater = float.MaxValue;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                            if (center == null || site.Cargo == null || shipBaseline < 0f) return false;
                            float inCenter = center.Inventory.Get(itemId);
                            float onShip = site.Cargo.Get(itemId);
                            // The outbound leg pushes the ship's own holding down; the return
                            // leg is the only thing that can push it back up again.
                            if (inCenter > 0.0001f && onShip < shipLowWater) shipLowWater = onShip;
                            return shipLowWater < shipBaseline - 0.0001f
                                   && onShip > shipLowWater + 0.0001f
                                   && inCenter > 0.0001f
                                   && Mathf.Approximately(inCenter + onShip, shipBaseline);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            shipLowWater = float.MaxValue;
                            site.InstallCenter(LogisticsCenterState.Active);
                            site.Strip(itemId);
                            site.Grant(itemId, 40f);
                            shipBaseline = site.Cargo.Get(itemId);
                        },
                        reject: _ =>
                        {
                            LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                            if (center == null) return null;
                            if (!center.Runtime.IsActive) return "MERKEZ AKTİF DEĞİL";
                            return site.Cargo != null && site.Cargo.GetAvailable(itemId) <= 0.0001f
                                ? "AKTARILACAK KABUL EDİLEN KAYNAK YOK" : null;
                        });
                },
                checklist: new[] { "Merkez envanteri artsın", "Gemi kargosu azalsın", "Ters yönde de çalışsın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("station.system_logistics.t1", "the active center")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipLogisticsCenter.DepositKey));

            yield return Step("D-12", "Merkezi T2'ye yükselt",
                "Şartlar hazırken lojistik merkezinin T2 yükseltmesini başlat.",
                "T2 yükseltmesini başlat; T1 çalışmaya devam etsin.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                        return center != null && center.Runtime.Upgrading
                               && Mathf.Approximately(center.Runtime.UpgradeRemaining,
                                   LogisticsCenterCatalog.T2.BuildSeconds)
                               && center.Runtime.Tier == LogisticsCenterTier.T1
                               && center.Runtime.IsActive;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.InstallCenter(LogisticsCenterState.Active);
                        TutorialSite.SetLevel(LogisticsCenterCatalog.T2.RequiredLevel);
                        site.ModuleTiers.SetTier(LogisticsCenterCatalog.StationConstructionModuleId, 2);
                        site.GrantAll(LogisticsCenterCatalog.T2.Cost());
                    },
                    reject: _ =>
                    {
                        var access = site.Ship != null ? site.Ship.GetComponent<ShipLogisticsCenter>() : null;
                        return access != null ? TutorialTr.CenterUpgrade(access.CheckUpgrade()) : null;
                    }),
                checklist: new[] { "T2 maliyeti düşülsün", "T1 çalışmaya devam etsin", "Yükseltme sayacı başlasın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("station.system_logistics.t2",
                    "the T2 center the upgrade produces")));

            yield return Step("D-13", "Yükseltmeyi iptal et",
                "Süren T2 yükseltmesini iptal et ve %75 iadeyi doğrula.",
                "Yükseltmeyi iptal et; iade kargoya gelsin.",
                site =>
                {
                    float parts = 0f, alloy = 0f, bio = 0f;
                    int refundParts = 0, refundAlloy = 0, refundBio = 0;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                            if (center == null || site.Cargo == null || refundParts == 0) return false;
                            return !center.Runtime.Upgrading
                                   && center.Runtime.Tier == LogisticsCenterTier.T1
                                   && center.Runtime.IsActive
                                   && Mathf.Approximately(site.Cargo.Get(ProcessingCatalog.BasicPartId) - parts, refundParts)
                                   && Mathf.Approximately(site.Cargo.Get("material.metal_alloy") - alloy, refundAlloy)
                                   && Mathf.Approximately(site.Cargo.Get("material.biocomposite") - bio, refundBio);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            LogisticsCenter center = site.InstallCenter(LogisticsCenterState.Active);
                            if (center == null) return;
                            // The refund is a real Add, so the hold has to have room for it
                            // or the cancel would silently return less than it owes.
                            site.Cargo.SetCapacity(ModuleCatalog.CargoCapacityByTier[4]);
                            center.StartUpgrade();
                            refundParts = center.UpgradeRefundParts;
                            refundAlloy = center.UpgradeRefundMetalAlloy;
                            refundBio = center.UpgradeRefundBiocomposite;
                            parts = site.Cargo.Get(ProcessingCatalog.BasicPartId);
                            alloy = site.Cargo.Get("material.metal_alloy");
                            bio = site.Cargo.Get("material.biocomposite");
                        },
                        reject: _ =>
                        {
                            LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                            return center != null && !center.Runtime.Upgrading
                                ? "İPTAL EDİLECEK YÜKSELTME YOK" : null;
                        });
                },
                checklist: new[] { "Yükseltme dursun", "T1 kesintisiz çalışsın", "İade kargoya gelsin" });

            yield return Step("D-14", "Toplayıcı rotası kur",
                "Bir toplayıcı aracı seç ve aktif bir sondajdan merkeze rota kur.",
                "Sondajdan merkeze kalıcı toplayıcı rotası kur.",
                site =>
                {
                    int routeBaseline = 0;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            CollectorRoute route = LogisticsState.FindRouteByDrill(site.WorldId);
                            return route != null && route.Phase == RoutePhase.ToSource
                                   && !string.IsNullOrEmpty(route.VehicleId)
                                   && LogisticsState.RouteCountForSystem(site.SystemId) == routeBaseline + 1;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.InstallDrill(DrillLifecycle.Active, stored: 30f);
                            site.InstallCenter(LogisticsCenterState.Active);
                            CollectorRoute existing = LogisticsState.FindRouteByDrill(site.WorldId);
                            if (existing != null) LogisticsState.RemoveRoute(existing);
                            routeBaseline = LogisticsState.RouteCountForSystem(site.SystemId);
                        },
                        reject: _ => TutorialTr.Route(CheckRoute(site, "tutorial-collector", routed: false)));
                },
                checklist: new[] { "Kaynak sondajı listelensin", "Komuta kapasitesi ayrılsın", "Rota kaynağa yönelsin" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("vehicle.collector.mk1",
                    "the collector the route is assigned to")));

            yield return Step("D-15", "Yinelenen rota reddini gör",
                "Aynı araca ve aynı sondaja ikinci bir rota kurmayı dene, reddi oku.",
                "Yinelenen rota reddini gör.",
                site =>
                {
                    int routeBaseline = -1;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            if (routeBaseline < 0) return false;
                            CollectorRoute route = LogisticsState.FindRouteByDrill(site.WorldId);
                            if (route == null) return false;
                            RouteCreateRejectReason sameVehicle = CheckRoute(site, route.VehicleId, routed: true);
                            RouteCreateRejectReason sameDrill = CheckRoute(site, "tutorial-second-collector", routed: true);
                            return sameVehicle == RouteCreateRejectReason.VehicleAlreadyRouted
                                   && sameDrill == RouteCreateRejectReason.DrillAlreadyRouted
                                   && LogisticsState.Routes.Count == routeBaseline + 1;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.InstallDrill(DrillLifecycle.Active, stored: 30f);
                            site.InstallCenter(LogisticsCenterState.Active);
                            CollectorRoute existing = LogisticsState.FindRouteByDrill(site.WorldId);
                            if (existing != null) LogisticsState.RemoveRoute(existing);
                            routeBaseline = LogisticsState.Routes.Count;
                        },
                        reject: _ =>
                        {
                            CollectorRoute route = LogisticsState.FindRouteByDrill(site.WorldId);
                            return route == null ? null
                                : TutorialTr.Route(RouteCreateRejectReason.DrillAlreadyRouted);
                        });
                },
                checklist: new[] { "Aynı araçla ikinci rota reddedilsin", "Aynı sondajla ikinci rota reddedilsin" });

            yield return Step("D-16", "Merkezi sök",
                "Envanteri boşalt, sonra merkezi sök.",
                "Boşaltılmış merkezi sök.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                        return center == null || center.Runtime.State == LogisticsCenterState.Dismantled;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        LogisticsCenter center = site.InstallCenter(LogisticsCenterState.Active);
                        // Non-empty on purpose: dismantle is refused until the player clears it.
                        center?.Inventory.Add(ProcessingCatalog.BasicPartId, 12f);
                    },
                    reject: _ =>
                    {
                        LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                        return center != null && !center.CanDismantle()
                            ? "SÖKÜM İÇİN ENVANTER BOŞ VE DOCK'LAR BOŞ OLMALI" : null;
                    }),
                checklist: new[] { "Envanter boşalsın", "Dock'lar boş olsun", "Merkez sökülsün" },
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipLogisticsCenter.DismantleKey));
        }

        /// <summary>The owner id a tutorial demonstration reservation is made under.</summary>
        internal const string TutorialReserveOwner = "tutorial-demo";

        /// <summary>
        /// The same gate <c>ShipHangar.TryCreateRoute</c> runs, fed from the real
        /// drill, center and route registry. The vehicle-side facts are supplied
        /// by the caller because a tutorial site has no hangar of its own.
        /// </summary>
        private static RouteCreateRejectReason CheckRoute(TutorialSite site, string vehicleId, bool routed)
        {
            LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
            DrillStructure drill = site.FindDrill();
            bool centerActive = center != null && center.Runtime.IsActive;
            int routeCount = LogisticsState.RouteCountForSystem(site.SystemId);
            int maxRoutes = center != null ? center.Runtime.Def.MaxRoutes : 0;
            bool vehicleAlreadyRouted = routed && LogisticsState.FindRouteByVehicle(vehicleId) != null;
            bool drillAlreadyRouted = drill != null && LogisticsState.FindRouteByDrill(drill.WorldId) != null;
            bool drillActive = drill != null && drill.Runtime.State == DrillLifecycle.Active;
            return CollectorRouteService.CheckCreate(centerActive, routeCount, maxRoutes,
                vehicleIsCollector: true, vehicleEligible: true, vehicleAlreadyRouted, drillAlreadyRouted,
                drillActive, sameSystem: drill != null && center != null, hasCommandCapacity: true);
        }

        private static int RawIndex(string rawId)
        {
            for (int i = 0; i < ExtractionCatalog.ResourceIds.Length; i++)
                if (ExtractionCatalog.ResourceIds[i] == rawId) return i;
            return 0;
        }

        private static TutorialStepDefinition Step(string id, string title, string instruction, string summary,
            Func<TutorialSite, ITutorialStepRuntime> build, string[] checklist = null,
            TutorialAssetRequirement[] assets = null, Func<string> keyHint = null) =>
            new TutorialStepDefinition(TutorialChapterId.Economy, id, title, instruction, summary,
                () => build(new TutorialSite()), checklist, assets, keyHint);
    }
}
