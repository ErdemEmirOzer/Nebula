using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Modules;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 4 / AJAN 2 — ekonomi, veri otoritesi ve zaman satırlarının
    /// regresyon nöbetçileri.
    /// <para>
    /// Kapsanan satırlar: P1-ECO-013, P1-ECO-014, P1-ECO-015, P1-ECO-016, P1-ECO-018,
    /// P1-ECO-019, P1-DATA-010, P1-DATA-011, P1-DATA-012, P1-DATA-020, P2-DATA-301,
    /// P2-TIME-002. Her test, düzeltmeden ÖNCEKİ davranışta kırmızı olacak biçimde
    /// yazılmıştır: bir sabitin bugünkü değerini değil, iki otoritenin ayrışamayacağını
    /// ölçer.
    /// </para>
    /// </summary>
    [Category("AuditPart4")]
    public class AuditPart4EconomyDataTests
    {
        // =====================================================================
        // P1-DATA-010 / P1-DATA-011 — tek maliyet otoritesi
        // =====================================================================

        /// <summary>
        /// Quick Asset Package'ın üç T1 fiyatı kendi sabitleri değil, yapının kendi
        /// katalog satırıdır. Eskiden beş sabit elle yazılmıştı ve bugün uyuşuyordu;
        /// bu test uyuşmayı değil, <b>türetilmiş olmayı</b> ölçer: katalog satırını
        /// değiştirirsem paket de değişmek zorundadır.
        /// </summary>
        [Test]
        public void QuickPackagePrices_AreTheStructureCatalogsOwnNumbers()
        {
            Assert.AreEqual(ExtractionCatalog.DrillT1PartsCost, QuickAssetPackageService.DrillBasicCost,
                "paket sondajı katalog dışı bir fiyattan satıyor");
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost, QuickAssetPackageService.CommandBasicCost);
            Assert.AreEqual(SurfaceStructureCatalog.Command.MaterialCost, QuickAssetPackageService.CommandSpecialCost);
            Assert.AreEqual(SurfaceStructureCatalog.Laser.PartsCost, QuickAssetPackageService.LaserBasicCost);
            Assert.AreEqual(SurfaceStructureCatalog.Laser.MaterialCost, QuickAssetPackageService.LaserSpecialCost);

            Assert.AreEqual(QuickAssetPackageService.DrillBasicCost + QuickAssetPackageService.CommandBasicCost
                            + QuickAssetPackageService.LaserBasicCost, QuickAssetPackageService.TotalBasicCost);
            Assert.AreEqual(QuickAssetPackageService.CommandSpecialCost + QuickAssetPackageService.LaserSpecialCost,
                QuickAssetPackageService.TotalSpecialCost);
        }

        /// <summary>
        /// P1-DATA-011: sondaj T1 fiyatı üç yerde yaşıyordu ve hiçbiri otorite değildi.
        /// Katalog satırı artık var; iade de elle yazılmış 15 değil, her yapının
        /// kullandığı aynı %75 kuralının sonucu.
        /// </summary>
        [Test]
        public void DrillT1Price_HasOneCatalogRow_AndItsRefundFollowsTheSharedRule()
        {
            Assert.Greater(ExtractionCatalog.DrillT1PartsCost, 0);
            Assert.AreEqual(Mathf.FloorToInt(ExtractionCatalog.DrillT1PartsCost * SurfaceStructureCatalog.RefundFraction),
                ExtractionCatalog.DrillT1RefundParts,
                "sondaj iadesi ortak %75 kuralından türemiyor");
        }

        /// <summary>
        /// Aynı satırın ikinci yarısı: kartın oyuncuya gösterdiği fiyat, işlemin
        /// tahsil ettiği fiyattır. HUD'daki "20 PARÇA" metni bunu ihlal ediyordu.
        /// </summary>
        [Test]
        public void TheDrillCardPrintsTheSameNumberTheTransactionCharges()
        {
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, ExtractionCatalog.DrillT1PartsCost);

            Assert.IsTrue(ShipExtraction.TryPaySecondDrillCost(cargo),
                "kart fiyatı kadar parça tam olarak yetmedi");
            Assert.AreEqual(0f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), 0.0001f,
                "işlem kartın yazdığından farklı bir miktar düştü");
        }

        // =====================================================================
        // P1-DATA-012 — modül seviye merdiveni monoton
        // =====================================================================

        /// <summary>
        /// Hiçbir modülün T(n) kapısı T(n-1)'den düşük olamaz. Eskiden global tablo
        /// T2 için 21 diyordu ve Silah Koordinasyonu'nun T1 kapısı 30'du: İlerleme
        /// sayfası aynı kartta "T1: 30" ile "T2: 21" yazıyordu.
        /// </summary>
        [Test]
        public void EveryModuleLevelLadderIsMonotone()
        {
            foreach (ModuleDef def in ModuleCatalog.Modules)
                for (int tier = 2; tier <= ModuleDef.MaxTier; tier++)
                    Assert.GreaterOrEqual(def.RequiredLevelForTier(tier), def.RequiredLevelForTier(tier - 1),
                        $"{def.Id}: T{tier} kapısı T{tier - 1}'den düşük");
        }

        /// <summary>Kuralın kendisi kataloğun doğrulayıcısında da var — bir gelecekteki içerik değişikliği testsiz geçmesin.</summary>
        [Test]
        public void CatalogValidator_RejectsANonMonotoneLadder()
        {
            Assert.IsEmpty(ModuleCatalog.Validate(), "shipped katalog kendi doğrulayıcısını geçmiyor");

            var broken = new ModuleDef("test.non_monotone", ModuleBranch.Defense, "Test", "Test",
                unlockLevel: 30, milestoneLabel: null, maxTierThisSlice: 2, lockedMessage: null,
                tierCosts: ModuleCatalog.Get("industry.logistics_cargo").TierCosts,
                runtimeConsumerDescription: "test", visualAnchorId: "anchor.test");
            Assert.AreEqual(30, broken.RequiredLevelForTier(2),
                "T1 kapısı 30 olan bir modül T2'yi 21'e düşürebiliyor — merdiven yine monoton değil");
        }

        // =====================================================================
        // P1-ECO-013 — 'en az bir Controlled gezegen' kapısı gerçekten ateşleniyor
        // =====================================================================

        /// <summary>
        /// Kapı katalogda tanımlı, serviste uygulanmış, ret sebebi ve Türkçe metni
        /// hazırdı; tek çağıran gerçeği geçirmediği için hiç ateşlenmiyordu.
        /// </summary>
        [Test]
        public void EngineerProduction_IsRefusedWithoutAControlledPlanet_AndAllowedWithOne()
        {
            VehicleDef engineer = VehicleCatalog.Get(VehicleClass.Engineer, 1);
            Assert.IsTrue(engineer.RequiresControlledPlanet, "bu test yanlış aracı ölçüyor");

            var cargo = new LogisticsCargo(5000f);
            cargo.Add(ProcessingCatalog.BasicPartId, engineer.BasicPartCost);
            cargo.Add(VehicleCatalog.MetalAlloyId, engineer.SpecialMaterialCost);
            var service = new HangarProductionService(cargo);

            Assert.AreEqual(HangarProductionRejectReason.MissingControlledPlanet,
                service.TryStartProduction(VehicleClass.Engineer, VehicleCatalog.MetalAlloyId,
                    engineer.UnlockLevel, engineer.RequiredHangarTier, mk: 1, hasAnyControlledPlanet: false),
                "kapı hiç ateşlenmedi");
            Assert.IsNull(service.ActiveJob, "reddedilen üretim yine de iş açtı");
            Assert.AreEqual(engineer.BasicPartCost, cargo.GetAvailable(ProcessingCatalog.BasicPartId), 0.0001f,
                "reddedilen üretim kaynak rezerve etti");

            Assert.AreEqual(HangarProductionRejectReason.None,
                service.TryStartProduction(VehicleClass.Engineer, VehicleCatalog.MetalAlloyId,
                    engineer.UnlockLevel, engineer.RequiredHangarTier, mk: 1, hasAnyControlledPlanet: true));
        }

        // =====================================================================
        // P1-ECO-016 — yutulan dönüş değerleri
        // =====================================================================

        /// <summary>
        /// Çok kalemli bir maliyet ya tamamen ödenir ya hiç: listenin ilk iki satırı
        /// düşülüp üçüncüsü başarısız olamaz.
        /// </summary>
        [Test]
        public void AMultiItemChargeIsAllOrNothing()
        {
            string metal = ProcessingCatalog.MaterialIds[0];
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 40f);
            cargo.Add(metal, 3f);

            var cost = new List<(string, float)> { (ProcessingCatalog.BasicPartId, 40f), (metal, 10f) };
            Assert.IsFalse(cargo.TryConsumeAllImmediate(cost), "eksik malzemeyle ödeme kabul edildi");
            Assert.AreEqual(40f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "reddedilen işlem yine de parça harcadı");
            Assert.AreEqual(3f, cargo.GetStored(metal), 0.0001f);
        }

        /// <summary>Aynı kalem iki satırda geçtiğinde toplanır — 6 birim stok, 5+5 isteyen bir fiyatı ödeyemez.</summary>
        [Test]
        public void ADuplicatedCostLineIsSummed_NeverCheckedTwiceAgainstTheSameStock()
        {
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 6f);

            Assert.IsFalse(cargo.TryConsumeAllImmediate(new List<(string, float)>
            {
                (ProcessingCatalog.BasicPartId, 5f), (ProcessingCatalog.BasicPartId, 5f)
            }), "aynı stok iki satır için ayrı ayrı sayıldı");
            Assert.AreEqual(6f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f);
        }

        /// <summary>Kabul edilen işlem tam bir kez tahsil eder.</summary>
        [Test]
        public void AnAcceptedMultiItemChargeConsumesExactlyOnce()
        {
            string metal = ProcessingCatalog.MaterialIds[0];
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 40f);
            cargo.Add(metal, 10f);

            Assert.IsTrue(cargo.TryConsumeAllImmediate(new List<(string, float)>
            {
                (ProcessingCatalog.BasicPartId, 40f), (metal, 10f)
            }));
            Assert.AreEqual(0f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f);
            Assert.AreEqual(0f, cargo.GetStored(metal), 0.0001f);
        }

        /// <summary>
        /// Rezerve edilmiş stok başkasının değildir: bir sahip 40 parçayı kilitlemişse,
        /// çok kalemli bir tahsilat onu görmemelidir.
        /// </summary>
        [Test]
        public void AMultiItemChargeNeverSpendsAnotherOwnersReservation()
        {
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 40f);
            cargo.TryReserve("someone-else", ProcessingCatalog.BasicPartId, 40f);

            Assert.IsFalse(cargo.TryConsumeAllImmediate(
                new List<(string, float)> { (ProcessingCatalog.BasicPartId, 40f) }));
            Assert.AreEqual(40f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f);
        }

        // =====================================================================
        // P2-DATA-301 — ikinci defter yok
        // =====================================================================

        /// <summary>
        /// Kargonun kullanılan hacmi fiziksel stoktan ibarettir. Kaydedilemeyen ikinci
        /// bir defterin kapasiteye sayılması, ilk save/load'da anlamını değiştirirdi.
        /// </summary>
        [Test]
        public void CargoUsedVolumeHasNoSecondUnsaveableLedger()
        {
            Assert.IsEmpty(typeof(LogisticsCargo).GetMethods()
                    .Where(m => m.Name.Contains("PendingClaim"))
                    .Select(m => m.Name),
                "kargoda yeniden kaydedilemeyen bir 'pending claim' defteri belirdi");

            var cargo = new LogisticsCargo(100f);
            cargo.Add(ProcessingCatalog.BasicPartId, 10f);
            cargo.TryReserve("owner", ProcessingCatalog.BasicPartId, 10f);
            Assert.AreEqual(cargo.Total, cargo.UsedVolume, 0.0001f);
        }

        // =====================================================================
        // P1-ECO-018 — ret metinleri: tek yer, Türkçe, asla sessiz
        // =====================================================================

        /// <summary>
        /// Her tipli ret bir cümle üretir; <c>None</c> ret değildir ve cümlesi yoktur.
        /// Tanınmayan bir değer bile enum adı değil, dürüst bir Türkçe cümle döner.
        /// </summary>
        [Test]
        public void EveryTypedRejectionProducesATurkishSentence_AndNoneProducesNothing()
        {
            Assert.IsNull(NebulaGameplayFeedback.RepairText(RepairRejectReason.None));
            foreach (RepairRejectReason reason in System.Enum.GetValues(typeof(RepairRejectReason)))
            {
                if (reason == RepairRejectReason.None) continue;
                AssertTurkishSentence(NebulaGameplayFeedback.RepairText(reason), reason.ToString());
            }

            Assert.IsNull(NebulaGameplayFeedback.DefenseCoordinationText(DefenseCoordinationRejectReason.None));
            foreach (DefenseCoordinationRejectReason reason in System.Enum.GetValues(typeof(DefenseCoordinationRejectReason)))
            {
                if (reason == DefenseCoordinationRejectReason.None) continue;
                AssertTurkishSentence(NebulaGameplayFeedback.DefenseCoordinationText(reason), reason.ToString());
            }

            Assert.IsNull(ShipQuickAssetPackage.RejectMessage(QuickPackageRejectReason.None));
            foreach (QuickPackageRejectReason reason in System.Enum.GetValues(typeof(QuickPackageRejectReason)))
            {
                if (reason == QuickPackageRejectReason.None) continue;
                AssertTurkishSentence(ShipQuickAssetPackage.RejectMessage(reason), reason.ToString());
            }
        }

        /// <summary>Tabloya öğretilmemiş bir değer bile sessiz kalmaz — <c>(RepairRejectReason)999</c> bir cümle döndürür.</summary>
        [Test]
        public void AnUnknownRejectionStillAnswersInTurkish_NeverWithAnEnumName()
        {
            AssertTurkishSentence(NebulaGameplayFeedback.RepairText((RepairRejectReason)999), "999");
            AssertTurkishSentence(NebulaGameplayFeedback.DefenseCoordinationText((DefenseCoordinationRejectReason)999), "999");
            AssertTurkishSentence(ShipQuickAssetPackage.RejectMessage((QuickPackageRejectReason)999), "999");
        }

        /// <summary>
        /// Geri bildirim kanalı artık çevirmiyor, taşıyor. Üreticinin yazdığı cümle
        /// oyuncuya değişmeden ulaşır; kanalda ikinci bir sözlük olsaydı üreticiyi
        /// düzeltmek yetmezdi.
        /// </summary>
        [Test]
        public void TheFeedbackChannelCarriesTheProducersSentenceUnchanged()
        {
            NebulaGameplayFeedback.Clear();
            NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Extraction, "YETERSİZ KAYNAK",
                GameplayFeedbackTone.Rejection);
            Assert.AreEqual("YETERSİZ KAYNAK", NebulaGameplayFeedback.Latest.Text);

            NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Extraction, "INSUFFICIENT RESOURCES");
            Assert.AreEqual("INSUFFICIENT RESOURCES", NebulaGameplayFeedback.Latest.Text,
                "kanal hâlâ kendi çeviri tablosunu uyguluyor — üretici tek otorite değil");
            NebulaGameplayFeedback.Clear();
        }

        /// <summary>Kargo yetersizliği reddi, işlemin gerçekten istediği toplamı yazar.</summary>
        [Test]
        public void TheQuickPackageRefusalQuotesTheTotalItActuallyReserves()
        {
            string text = ShipQuickAssetPackage.RejectMessage(QuickPackageRejectReason.InsufficientCargo);
            StringAssert.Contains(QuickAssetPackageService.TotalBasicCost.ToString(), text);
            StringAssert.Contains(QuickAssetPackageService.TotalSpecialCost.ToString(), text);
        }

        /// <summary>
        /// İngilizce sızıntısını gerçekten yakalayan kontrol. Önceki hâli yalnız
        /// "boş değil ve enum adı değil" diyordu — yani adının vaat ettiği tek şeyi,
        /// metnin Türkçe olduğunu, hiç ölçmüyordu: "INSUFFICIENT RESOURCES" bu
        /// kontrolden geçerdi. Türkçeliği kanıtlamak yerine İngilizceliği çürütüyoruz;
        /// listedeki sözcüklerin hiçbirinin Türkçede aynı yazılan bir karşılığı yok,
        /// bu yüzden yanlış alarm vermez.
        /// </summary>
        private static readonly HashSet<string> EnglishGiveaways = new HashSet<string>
        {
            "INSUFFICIENT", "RESOURCE", "RESOURCES", "REQUIRED", "REQUIRES", "AVAILABLE",
            "UNAVAILABLE", "MISSING", "INVALID", "FAILED", "FAILURE", "CANNOT", "DENIED",
            "REJECTED", "BUSY", "EMPTY", "FULL", "NEED", "NEEDS", "ENOUGH", "ALREADY",
            "WITHOUT", "SELECTED", "TARGET", "REASON", "UNKNOWN", "NONE",
        };

        private static void AssertTurkishSentence(string text, string label)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{label}: ret sessiz kaldı");
            Assert.AreNotEqual(label, text, $"{label}: oyuncuya enum adı gösterildi");

            foreach (string word in text.ToUpperInvariant().Split(
                new[] { ' ', '.', ',', ':', ';', '!', '?', '(', ')', '-', '/', '\n', '\r', '\t' },
                System.StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.IsFalse(EnglishGiveaways.Contains(word),
                    $"{label}: oyuncuya İngilizce gösteriliyor — \"{text}\" içinde \"{word}\"");
            }
        }

        // =====================================================================
        // P1-ECO-019 — 'eşleşen sondaj' koşulu artık ayırt ettiğini söylüyor
        // =====================================================================

        /// <summary>
        /// Dominance koşulu dört alandan biri olarak "eşleşen sondaj" diyordu, ama
        /// sondajın ailesi her zaman gezegenin kendi ailesidir: koşul "sondaj var"
        /// ile özdeşti. Alan artık adının hakkını veriyor ve uzak yol ile aynı adı
        /// taşıyor.
        /// </summary>
        [Test]
        public void DominanceNamesTheDrillConditionForWhatItMeasures()
        {
            Assert.IsNull(typeof(DominanceConditions).GetField("ActiveMatchingDrill"),
                "ayırt etmediği bir şeyi vaat eden alan geri geldi");
            Assert.IsNotNull(typeof(DominanceConditions).GetField("ActiveDrill"));
            Assert.IsNull(typeof(ExtractionState).GetMethod("HasActiveMatchingDrill"),
                "aile parametresi hiçbir şeyi elemediği hâlde geri geldi");

            var conditions = new DominanceConditions
            { Scanned = true, ActiveDrill = true, ActiveCommand = true, ActiveDefense = true };
            Assert.IsTrue(conditions.AllMet);
            conditions.ActiveDrill = false;
            Assert.IsFalse(conditions.AllMet, "sondaj koşulu artık hiçbir şeyi engellemiyor");
        }

        // =====================================================================
        // P1-ECO-015 — tüketicisi olmayan alanlar
        // =====================================================================

        /// <summary>
        /// Etki üretmeyen bir kademe satın alınabilir olabilir, ama oyuncuya söylemek
        /// zorundadır. İşleme modülünün T2-T5'i hiçbir şey vermiyor ve bunu artık
        /// katalog alanı taşıyor.
        /// </summary>
        [Test]
        public void AModuleTierThatGrantsNothingSaysSo()
        {
            ModuleDef processing = ModuleCatalog.Get(ModuleCatalog.ResourceProcessingId);
            Assert.AreEqual(1, processing.MaxEffectiveTierThisSlice,
                "işleme modülünün etkili kademesi değişti — sayfa metni de değişmeli");
            Assert.Less(processing.MaxEffectiveTierThisSlice, processing.MaxTierThisSlice,
                "etkisiz kademeler kaldırıldıysa bu testin de kalkması gerekir");

            foreach (ModuleDef def in ModuleCatalog.Modules)
                Assert.LessOrEqual(def.MaxEffectiveTierThisSlice, def.MaxTierThisSlice,
                    $"{def.Id}: satın alınamayan bir kademe etkili sayılıyor");
        }

        /// <summary>
        /// Lazer T1 gerçekte 15 parça + 5 (oyuncunun seçtiği) malzemedir.
        /// <c>FullCost()</c> sabit kalemleri döndürür; eksik kalan kalem artık
        /// kayıp değil, adı konmuş ve <see cref="StructureTierDef.IsFullPrice"/> ile
        /// sorulabilir.
        /// </summary>
        [Test]
        public void LaserT1DeclaresItsPlayerChosenMaterial_SoNoCostScreenCanShowItFree()
        {
            StructureTierDef laser = PlanetaryDefenseCatalog.LaserT1;
            Assert.AreEqual(SurfaceStructureCatalog.Laser.MaterialCost, laser.PlayerChosenMaterialCost,
                "tier tablosu ile yapı kataloğu lazerin malzeme maliyetinde ayrıştı");
            Assert.IsFalse(laser.IsFullPrice, "FullCost() lazerin tamamını içeriyormuş gibi görünüyor");
            Assert.IsTrue(PlanetaryDefenseCatalog.LaserT2.IsFullPrice,
                "sabit malzemeli bir tier yanlışlıkla 'eksik fiyat' işaretlendi");
        }

        /// <summary>
        /// <c>ProcessingRecipe.Seconds</c> artık süre değil, içerik kimliğidir ve
        /// gerçek bir tüketicisi vardır: içerik parmak izi. Değeri değişirse parmak
        /// izi de değişmelidir — okunmayan bir alan olmadığının kanıtı budur.
        /// </summary>
        [Test]
        public void ProcessingRecipeSeconds_IsReadByTheContentFingerprint()
        {
            Assert.AreNotEqual(FingerprintOfRecipeWithSeconds(2f), FingerprintOfRecipeWithSeconds(3f),
                "tarif süresi parmak izine girmiyor — alanın gerçekten tüketicisi yok");
            Assert.AreEqual(FingerprintOfRecipeWithSeconds(2f), FingerprintOfRecipeWithSeconds(2f));
        }

        private static ulong FingerprintOfRecipeWithSeconds(float seconds)
        {
            var source = new FakeContentCatalog();
            source.Recipes.Add(new Content.ContentRecipe("recipe.test", "resource.metal", 2,
                ProcessingCatalog.BasicPartId, 1, seconds, "test"));
            return Content.ContentFingerprint.Compute(Content.RuntimeCatalogBuilder.Build(source));
        }

        // =====================================================================
        // P1-DATA-020 / P1-WORLD-004 — tarayıcı kademesi POI Locate'i gerçekten değiştiriyor
        // =====================================================================

        /// <summary>
        /// Tier 0 satırı yoktu: hiç tarayıcısı olmayan oyuncu da T1 hızında (12 sn)
        /// Locate ediyordu, yani satın alınan yükseltmenin POI Locate'e etkisi yoktu.
        /// Merdiven artık her kademede kısalıyor.
        /// </summary>
        [Test]
        public void BuyingScannerT1_ActuallyShortensPoiLocate()
        {
            Assert.Greater(PoiCatalog.ShipLocateSeconds(0), PoiCatalog.ShipLocateSeconds(1),
                "Scanner T1 satın almak POI Locate süresini hiç değiştirmiyor");
            Assert.Greater(PoiCatalog.ShipLocateSeconds(1), PoiCatalog.ShipLocateSeconds(2));
            Assert.AreEqual(PoiCatalog.ScannerT0LocateSeconds, PoiCatalog.ShipLocateSeconds(0), 0.0001f);
            Assert.AreEqual(PoiCatalog.ScannerT1LocateSeconds, PoiCatalog.ShipLocateSeconds(1), 0.0001f);
            Assert.AreEqual(PoiCatalog.ScannerT2LocateSeconds, PoiCatalog.ShipLocateSeconds(2), 0.0001f);
        }

        /// <summary>Aynı yükseltme menzili de büyütür; kademe 0 çıplak çalışma menzilidir.</summary>
        [Test]
        public void BuyingScannerT1_AlsoWidensTheLocateRange()
        {
            Assert.AreEqual(PoiCatalog.IdentifyRange, PoiCatalog.ShipLocateRange(0), 0.0001f);
            Assert.Greater(PoiCatalog.ShipLocateRange(1), PoiCatalog.ShipLocateRange(0));
            Assert.Greater(PoiCatalog.ShipLocateRange(2), PoiCatalog.ShipLocateRange(1));
        }

        /// <summary>
        /// Locate'in mesafe koşulu vardır: menzil dışındayken sayaç ilerlemez ve sebep
        /// söylenir; sıfırlanmaz (duraklar), çünkü merdiven hiçbir zaman geri gitmez.
        /// </summary>
        [Test]
        public void LocateProgressesOnlyInRange_AndPausesRatherThanResets()
        {
            var state = new PoiState("poi-locate");
            float required = PoiCatalog.ShipLocateSeconds(1);

            Assert.IsFalse(state.TickLocate(required, conditionMet: false, required));
            Assert.AreEqual(PoiKnowledgeLevel.Signal, state.Level, "menzil dışında Locate tamamlandı");
            Assert.AreEqual(PoiPauseReason.OutOfRange, state.PauseReason, "duraklama sebebi söylenmedi");

            Assert.IsFalse(state.TickLocate(required * 0.5f, conditionMet: true, required));
            Assert.IsFalse(state.TickLocate(0f, conditionMet: false, required));
            Assert.IsTrue(state.TickLocate(required * 0.5f, conditionMet: true, required),
                "duraklama ilerlemeyi sıfırladı");
            Assert.AreEqual(PoiKnowledgeLevel.Located, state.Level);
        }

        // =====================================================================
        // P1-WORLD-005 — Unknown basamağı (spec kararı)
        // =====================================================================

        /// <summary>
        /// Karar: <c>Unknown</c> "kaydı yok" basamağıdır ve canlı bir
        /// <see cref="PoiState"/> hiçbir zaman onu taşımaz — nesnenin varlığı zaten
        /// kaydın kendisidir. Kaydedilmiş bir Unknown bile ilk gerçek basamağa
        /// çekilir, yoksa hiçbir tick'in çıkaramayacağı bir durumda donardı.
        /// </summary>
        [Test]
        public void PoiStateNeverHoldsUnknown_EvenFromASave()
        {
            Assert.AreEqual(PoiKnowledgeLevel.Signal, new PoiState("poi-fresh").Level);

            var restored = new PoiState("poi-restored");
            restored.RestoreCore(PoiKnowledgeLevel.Unknown, 0f, 0f, 0f, PoiPauseReason.None, null);
            Assert.AreEqual(PoiKnowledgeLevel.Signal, restored.Level,
                "kaydedilmiş bir Unknown canlı state'e sızdı");

            var identified = new PoiState("poi-identified");
            identified.RestoreCore(PoiKnowledgeLevel.Identified, 0f, 0f, 0f, PoiPauseReason.None, null);
            Assert.AreEqual(PoiKnowledgeLevel.Identified, identified.Level, "gerçek bir seviye ezildi");
        }

        // =====================================================================
        // P1-WORLD-007 — hırsız hedeflediği sondajdan çalar
        // =====================================================================

        /// <summary>
        /// Hedef kimliği gezegen değil, sondajdır: aynı gezegende iki sondaj taşıyan
        /// bir kayıt yüklendiğinde, seçilen sondaj ile soyulan sondaj aynı olmalıdır.
        /// </summary>
        [Test]
        public void ADrillLootTargetNamesTheDrill_NotJustItsPlanet()
        {
            Assert.IsNull(LooterCargoService.DrillTargetId(null));
            Assert.IsNull(LooterCargoService.FindDrillByTargetId(null));
            Assert.IsNull(LooterCargoService.FindDrillByTargetId("no-such-drill"));
        }

        // =====================================================================
        // P2-TIME-002 — uzak tick takvimi (spec kararı)
        // =====================================================================

        /// <summary>
        /// Meşgul sistem her döngü, sakin sistem on döngüde bir değerlendirilir —
        /// 1 Hz ve 0.1 Hz. Belge, yorum ve kod artık aynı şeyi söylüyor.
        /// </summary>
        [Test]
        public void ABusySystemIsEvaluatedEveryCycle_AndACalmOneEveryTenth()
        {
            var schedule = new SparseTickScheduler();
            const string id = "sys-busy";
            for (int cycle = 1; cycle <= 30; cycle++)
            {
                Assert.IsTrue(schedule.ShouldRun(id, cycle, 1f, out float seconds), $"meşgul sistem {cycle}. döngüde atlandı");
                Assert.AreEqual(1f, seconds, 0.0001f);
                schedule.NoteEvaluated(id, calm: false);
            }
            Assert.IsFalse(schedule.IsSlowRate(id));

            const string calm = "sys-calm";
            for (int cycle = 1; cycle <= SparseTickScheduler.CalmSkipThreshold; cycle++)
            {
                Assert.IsTrue(schedule.ShouldRun(calm, cycle, 1f, out _));
                schedule.NoteEvaluated(calm, calm: true);
            }
            Assert.IsTrue(schedule.IsSlowRate(calm), "sakin sistem yavaş orana hiç geçmedi");

            int ran = 0;
            for (int cycle = SparseTickScheduler.CalmSkipThreshold + 1; cycle <= SparseTickScheduler.CalmSkipThreshold + 100; cycle++)
                if (schedule.ShouldRun(calm, cycle, 1f, out _)) { ran++; schedule.NoteEvaluated(calm, calm: true); }
            Assert.AreEqual(10, ran, "sakin sistemin değerlendirme oranı 0.1 Hz değil");
        }

        /// <summary>
        /// Atlanan döngülerin saniyeleri saklanır: 100 döngüde uygulanan toplam
        /// simülasyon süresi, geçen gerçek süreye eşittir. Eski kod sakin duruma
        /// GİRİŞTE oranı sabit 10 varsaydığı için bir kereliğine 9 saniye fazla
        /// veriyordu.
        /// </summary>
        [Test]
        public void SkippedCyclesDeferTime_TheyNeitherLoseNorInventIt()
        {
            var schedule = new SparseTickScheduler();
            const string id = "sys";
            const float perCycle = 1f;

            float applied = 0f;
            for (int cycle = 1; cycle <= 100; cycle++)
            {
                // Üç döngü meşgul, sonrası sakin: streak eşiği tam olarak 10'un katı
                // OLMAYAN bir döngüde aşılır — eski çarpma hatasının göründüğü yer.
                bool calm = cycle > 3;
                if (schedule.ShouldRun(id, cycle, perCycle, out float seconds))
                {
                    applied += seconds;
                    schedule.NoteEvaluated(id, calm);
                }
            }

            Assert.AreEqual(100f * perCycle, applied + schedule.DeferredSecondsOf(id), 0.0001f,
                "uygulanan + bekleyen simülasyon süresi geçen gerçek süreye eşit değil");
            Assert.LessOrEqual(applied, 100f * perCycle,
                "sakin duruma geçişte fazladan simülasyon süresi verildi");
        }

        /// <summary>Aynı döngü dizisi iki kez sürüldüğünde aynı saniyeleri üretir — uzak ekonomi deterministik kalır.</summary>
        [Test]
        public void TheScheduleIsDeterministic()
        {
            Assert.AreEqual(RunSchedule(), RunSchedule());
        }

        private static string RunSchedule()
        {
            var schedule = new SparseTickScheduler();
            var log = new List<string>();
            for (int cycle = 1; cycle <= 60; cycle++)
                if (schedule.ShouldRun("sys", cycle, 1f, out float seconds))
                {
                    log.Add($"{cycle}:{seconds:0.###}");
                    schedule.NoteEvaluated("sys", calm: cycle > 2);
                }
            return string.Join(",", log);
        }

        /// <summary>Bir sistem unutulduğunda bekleyen saniyeleri de gider; yeniden keşfedildiğinde eski borcu taşımaz.</summary>
        [Test]
        public void ForgettingASystemDropsItsDeferredSeconds()
        {
            var schedule = new SparseTickScheduler();
            const string id = "sys";
            for (int cycle = 1; cycle <= SparseTickScheduler.CalmSkipThreshold; cycle++)
            {
                schedule.ShouldRun(id, cycle, 1f, out _);
                schedule.NoteEvaluated(id, calm: true);
            }
            schedule.ShouldRun(id, SparseTickScheduler.CalmSkipThreshold + 1, 1f, out _);
            Assert.Greater(schedule.DeferredSecondsOf(id), 0f);

            schedule.Forget(id);
            Assert.AreEqual(0f, schedule.DeferredSecondsOf(id), 0.0001f);
            Assert.IsFalse(schedule.IsSlowRate(id));
        }
    }
}
