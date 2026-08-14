using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 1 / AJAN 1 — geri bildirim ve kalıcılık teşhisleri.
    /// <para>
    /// İki soru: (1) oyunun ürettiği ret nedenleri oyuncuya ulaşıyor mu,
    /// (2) ayarlar ekranındaki anahtarlar oyunu kapatınca kalıyor mu.
    /// </para>
    /// <para>
    /// Baseline: HEAD=4d16782, branch=vs-017a-2-semantic-drill-t2-integrity.
    /// </para>
    /// </summary>
    public class P1A1DiagFeedbackAndPersistenceTests
    {
        private static string ScriptRoot => Path.Combine(Application.dataPath, "Scripts", "Nebula");

        private static string[] AllProduction() =>
            Directory.GetFiles(ScriptRoot, "*.cs", SearchOption.AllDirectories);

        private static string Rel(string absolute) =>
            absolute.Substring(Application.dataPath.Length - 6).Replace('\\', '/');

        // =====================================================================
        // Ret nedenleri: üretiliyor, hiçbir yerde gösterilmiyor
        // =====================================================================

        /// <summary>
        /// Sekiz oyun sistemi bir <c>TransientMessage</c> yayımlıyor — sondaj,
        /// işleme, hızlı kurulum, tahliye, lojistik merkezi, ilgi noktası,
        /// seyrüsefer ve kayıt yöneticisi. Bunları okuyan tek bir production
        /// satırı yok: tek okuyucular test dosyaları. Yani oyuncunun F/L/K/R/T/G/P
        /// ile aldığı her ret ("SCAN THE PLANET FIRST", "MOVE CLOSER",
        /// "NEEDS AN ACTIVE COMMAND CENTER" …) hesaplanıyor, zamanlanıyor ve
        /// hiçbir ekrana çıkmıyor.
        /// </summary>
        [Test]
        public void Diag_EveryGameplayTransientMessage_IsProducedAndConsumedByNothingInProduction()
        {
            var producers = new List<string>();
            var consumers = new List<string>();

            foreach (string file in AllProduction())
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (Regex.IsMatch(lines[i], @"public\s+string\s+TransientMessage\b"))
                    {
                        producers.Add($"{Rel(file)}:{i + 1}");
                        continue;
                    }
                    // Bir okuma: `<bir şey>.TransientMessage`
                    if (Regex.IsMatch(lines[i], @"\.\s*TransientMessage\b"))
                        consumers.Add($"{Rel(file)}:{i + 1}");
                }
            }

            TestContext.WriteLine("Üreticiler (" + producers.Count + "):\n  " + string.Join("\n  ", producers));
            TestContext.WriteLine("Production tüketicileri (" + consumers.Count + "): "
                + (consumers.Count == 0 ? "YOK" : string.Join(", ", consumers)));

            Assert.GreaterOrEqual(producers.Count, 6,
                "beklenen üretici sayısı bulunamadı — tarama boşa çalışmış olabilir");
            Assert.IsEmpty(consumers,
                "BEKLENEN KUSUR KAYBOLDU: production artık TransientMessage okuyor: "
                + string.Join(", ", consumers));
        }

        /// <summary>
        /// Ve kanıtın ikinci yarısı: bunları okuyan gerçekten <b>testler</b>.
        /// Bu, "kimse okumuyor çünkü kimsenin ihtiyacı yok" yorumunu kapatır —
        /// mesajlar var, doğru, anlamlı, ve yalnızca test koşumunda okunuyor.
        /// </summary>
        [Test]
        public void Diag_TheOnlyReadersOfThoseMessagesAreTestAssertions()
        {
            string testRoot = Path.Combine(Application.dataPath, "Tests");
            var readers = new List<string>();

            foreach (string file in Directory.GetFiles(testRoot, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (Regex.IsMatch(lines[i], @"\.\s*TransientMessage\b"))
                        readers.Add($"{Path.GetFileName(file)}:{i + 1}");
            }

            TestContext.WriteLine("Test okuyucuları (" + readers.Count + "): " + string.Join(", ", readers));
            Assert.IsNotEmpty(readers,
                "BEKLENEN DURUM DEĞİŞTİ: artık testler de okumuyor — bulgu yeniden değerlendirilmeli");
        }

        /// <summary>
        /// KAPANDI (Part 3/4). HUD bağlam çubuğu ret nedenini değil, yalnızca
        /// <i>yapılabilecek eylemi</i> gösteriyordu: oyuncuya hangi tuşa basacağı
        /// söyleniyor, basınca neden olmadığı söylenmiyordu.
        /// <para>
        /// İki şey birden değişti ve ikisi de burada tutuluyor. Çubuk artık eylemini
        /// katalog satırının kimliğiyle taşıyor — bir tuşun resmi değil, gerçek bir
        /// kontrol — yani atama değişince ipucu da değişiyor. Ve engellendiğinde
        /// gerekçeyi taşıyor, yani oyuncu neden olmadığını görüyor.
        /// </para>
        /// </summary>
        [Test]
        public void TheHudContextBarCarriesItsActionIdentityAndItsRefusalReason()
        {
            string context = File.ReadAllText(
                Path.Combine(ScriptRoot, "UI", "Hud", "NebulaExtractionContext.cs"));

            StringAssert.Contains("HudAction", context);
            StringAssert.Contains("actionId:", context,
                "bağlam çubuğu eylemini katalog kimliğiyle taşımıyor — tuşun resmine geri döndü, "
                + "yani yeniden atama ipucu değiştirmez");
            StringAssert.Contains("blockedReason:", context,
                "bağlam çubuğu engellendiğinde gerekçe taşımıyor — oyuncuya yine yalnızca "
                + "çalışmayan bir tuş gösteriliyor");
        }

        // =====================================================================
        // Ayarların kalıcılığı
        // =====================================================================

        /// <summary>
        /// Ayarlar/Erişilebilirlik ekranının yazdığı her değer bir <c>static</c>
        /// alan. Projede tek bir kullanıcı-ayarı deposu var
        /// (<c>NebulaInputBindingStore</c>) ve o yalnızca savaş tuş atamalarını
        /// tutuyor; <c>PlayerPrefs</c> çağrısı hiç yok. Sonuç: Işık Duyarlılığı
        /// Güvenli Modu dahil her erişilebilirlik anahtarı her açılışta
        /// varsayılana döner.
        /// </summary>
        [Test]
        public void Diag_NoInterfaceOrAccessibilitySettingIsPersistedAnywhere()
        {
            // 1) Projede PlayerPrefs yok.
            var playerPrefs = AllProduction()
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"PlayerPrefs\s*\.\s*(Set|Get)"))
                .Select(Rel).ToList();
            Assert.IsEmpty(playerPrefs,
                "BEKLENEN KUSUR KAYBOLDU: artık PlayerPrefs ile bir ayar yazılıyor: "
                + string.Join(", ", playerPrefs));

            // 2) Tek kullanıcı-ayarı deposu yalnızca tuş atamalarını tutuyor.
            string store = File.ReadAllText(
                Path.Combine(ScriptRoot, "Ship", "NebulaInputBindingStore.cs"));
            foreach (string setting in new[]
                     {
                         "UiScale", "HudScale", "ReducedMotion", "MotionOff", "CameraComfort",
                         "FlashReduction", "PhotosensitivitySafeMode", "HighContrast",
                         "AudioVisualCueForWarnings", "MasterVolume",
                     })
                Assert.IsFalse(store.Contains(setting),
                    $"'{setting}' tuş atama deposuna sızdı — erişilebilirlik ayarlarının "
                    + "kendi deposu var (NebulaAccessibilitySettingsStore) ve ikisi "
                    + "karışırsa tuş atamaları ayar sıfırlamasıyla birlikte silinir");

            // 3) Ve ayarların kendi tanım dosyaları hiçbir kalıcılık taşımıyor.
            string[] declarations =
            {
                Path.Combine(ScriptRoot, "Visuals", "Animation", "AnimationAccessibility.cs"),
                Path.Combine(ScriptRoot, "Visuals", "Lighting", "LightingAccessibility.cs"),
                Path.Combine(ScriptRoot, "Combat", "CombatAccessibilitySettings.cs"),
            };
            foreach (string file in declarations)
            {
                Assert.IsTrue(File.Exists(file), "beklenen kaynak yok: " + file);
                string text = File.ReadAllText(file);
                // Ayar tanımları hâlâ kalıcılık taşımıyor ve taşımamalı: kalıcılık tek
                // yerde, NebulaAccessibilitySettingsStore'da. Her ayarın kendi dosyasına
                // yazması, tam da bu denetimin her yerde kapattığı şey olurdu — ikinci
                // otorite.
                Assert.IsFalse(Regex.IsMatch(text, @"File\s*\.\s*(Write|Read)|PlayerPrefs|JsonUtility"),
                    Path.GetFileName(file) + " kendi kalıcılığını açtı — kalıcılık tek "
                    + "otoritede (NebulaAccessibilitySettingsStore) kalmalı");
            }
        }

        /// <summary>
        /// Kontrast: savaş tuş atamaları gerçekten kalıcı. Yani bu bir "proje
        /// kalıcılık yapamıyor" durumu değil — bir tane kalıcılık yolu var ve
        /// erişilebilirlik ayarları o yola bağlanmamış.
        /// </summary>
        [Test]
        public void Diag_ByContrastCombatRebindsDoPersist()
        {
            string store = File.ReadAllText(
                Path.Combine(ScriptRoot, "Ship", "NebulaInputBindingStore.cs"));
            StringAssert.Contains("actionId", store);
            StringAssert.Contains("settingsVersion", store);

            // Yazma yolu: rebind kabul edildiğinde UI katmanı depoyu çağırıyor.
            string bindingsSurface = File.ReadAllText(
                Path.Combine(ScriptRoot, "UI", "Controls", "NebulaControlBindings.cs"));
            StringAssert.Contains("NebulaInputBindingStore.Save()", bindingsSurface,
                "karşılaştırma dayanağı gitti: tuş atamaları da artık kalıcı değil");
            StringAssert.Contains("NebulaInputBindingStore.Clear()", bindingsSurface);

            // Ve aynı UI katmanı hiçbir erişilebilirlik ayarı için böyle bir çağrı
            // yapmıyor: ayarlar sayfası hiçbir depoya dokunmuyor.
            // KAPANDI (Part 3/4). Bu satır eskiden ayarlar sayfasının hiçbir depoya
            // dokunmadığını ölçüyordu — kusurun kendisiydi. Sayfa artık her ayar
            // değişiminde NebulaAccessibilitySettingsStore'a yazıyor, ve round-trip
            // AuditPart4AccessibilityReachTests tarafından gerçekten sürülüyor.
            string settingsPage = File.ReadAllText(
                Path.Combine(ScriptRoot, "UI", "Menu", "NebulaSettingsPage.cs"));
            StringAssert.Contains("NebulaAccessibilitySettingsStore.Save()", settingsPage,
                "ayarlar sayfası kalıcılığı kaybetti — erişilebilirlik ayarları yine her "
                + "açılışta varsayılana döner");
        }
    }
}
