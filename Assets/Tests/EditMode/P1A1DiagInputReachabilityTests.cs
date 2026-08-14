using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction; // A3 note: added to unblock the shared EditMode assembly (ShipExtraction lives here)
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 1 / AJAN 1 — teşhis fixture'ı, Part 4'te bekçiye dönüştü.
    /// <para>
    /// Yazıldığında her test kusurun <b>durduğunu</b> ölçüyordu: yeşil "kusur yok"
    /// değil, "kusur raporda yazıldığı şekilde hâlâ orada" demekti, ve kusur
    /// düzeltilince kırmızıya dönmesi kasıtlıydı — kırmızıya dönmeyen bir teşhis
    /// testi hiçbir şey kanıtlamaz.
    /// </para>
    /// <para>
    /// Part 3 ve Part 4 kusurları kapattı, dolayısıyla bu testler görevlerini
    /// yaparak kırmızıya döndüler. Silinmediler, <b>ters çevrildiler</b>: aynı
    /// tarama artık düzeltilmiş durumu ölçüyor, yani kusurun geri gelmesini
    /// yakalıyor. Teşhisi silmek, düzeltmenin tek kanıtını da silerdi; olduğu gibi
    /// bırakmak ise paketi kalıcı kırmızıda tutardı.
    /// </para>
    /// <para>
    /// Baseline: HEAD=4d16782 (teşhis), ters çevirme: Part 4.
    /// branch=vs-017a-2-semantic-drill-t2-integrity.
    /// </para>
    /// </summary>
    public class P1A1DiagInputReachabilityTests
    {
        private static string ScriptRoot => Path.Combine(Application.dataPath, "Scripts", "Nebula");

        private static string[] ProductionFiles(params string[] subFolders)
        {
            string root = subFolders.Length == 0
                ? ScriptRoot
                : Path.Combine(new[] { ScriptRoot }.Concat(subFolders).ToArray());
            return Directory.Exists(root)
                ? Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                : Array.Empty<string>();
        }

        /// <summary>
        /// Yalnızca <c>Input.GetKey*(KeyCode.X)</c> biçimindeki <b>literal</b>
        /// okumalar. Sabit üzerinden okunanlar burada aranmaz; amaç kataloğun
        /// hiç görmediği okumaları bulmak, kataloğun bildiklerini yeniden
        /// saymak değil.
        /// </summary>
        private static Dictionary<KeyCode, List<string>> LiteralKeyReads()
        {
            var found = new Dictionary<KeyCode, List<string>>();
            foreach (string file in ProductionFiles())
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in Regex.Matches(lines[i],
                                 @"Input\.GetKey(?:Down|Up)?\(\s*KeyCode\.(\w+)\s*\)"))
                    {
                        if (!Enum.TryParse(m.Groups[1].Value, out KeyCode key)) continue;
                        if (!found.TryGetValue(key, out List<string> sites))
                            found[key] = sites = new List<string>();
                        sites.Add($"{file.Substring(Application.dataPath.Length - 6)}:{i + 1}");
                    }
                }
            }
            return found;
        }

        // =====================================================================
        // P1-DATA-001 — katalogda karşılığı olmayan gamepad okumaları
        // =====================================================================

        /// <summary>
        /// KAPANDI (Part 3/4). <c>NebulaQuantityModal</c> omuz düğmelerini
        /// (JoystickButton4/5) doğrudan okuyordu ve <see cref="NebulaInputActions"/>
        /// içinde tek bir gamepad satırı yoktu: iki tuş da çakışma taramasına
        /// girmiyor, rezerve edilmiyor, ayarlar ekranında görünmüyordu.
        /// <para>
        /// Artık ikisi de katalogda. Bu test, teşhis hâlinden bekçi hâline çevrildi:
        /// eskiden "kusur duruyor mu" diye soruyordu, şimdi "kusur geri geldi mi" diye
        /// soruyor. Ters çevirmek yerine silmek, düzeltmenin tek kanıtını da silerdi.
        /// </para>
        /// </summary>
        [Test]
        public void TheGamepadKeysProductionReadsAreAllInTheCatalog()
        {
            var catalogued = new HashSet<KeyCode>(NebulaInputActions.All.Select(a => a.Key));
            Dictionary<KeyCode, List<string>> reads = LiteralKeyReads();

            var uncataloguedPad = reads
                .Where(kv => kv.Key.ToString().StartsWith("Joystick", StringComparison.Ordinal))
                .Where(kv => !catalogued.Contains(kv.Key))
                .OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
                .ToList();

            Assert.IsEmpty(uncataloguedPad,
                "production bir gamepad tuşunu katalog dışından okuyor — çakışma taramasına " +
                "girmez, ayarlarda görünmez, oyuncuya hiçbir yerde söylenmez:\n  " +
                string.Join("\n  ", uncataloguedPad.Select(kv => $"{kv.Key} <- {string.Join(", ", kv.Value)}")));

            Assert.IsTrue(catalogued.Contains(KeyCode.JoystickButton4)
                          && catalogued.Contains(KeyCode.JoystickButton5),
                "omuz düğmeleri katalogdan düştü — tarama körleşir");
        }

        /// <summary>
        /// KAPANDI (Part 3/4). Kataloğun kendi bütünlük değişmezi — production'ın
        /// okuduğu her tuşun bir satırı olması — ihlal ediliyordu; aynı taramayı
        /// burada bağımsız olarak tekrarlıyoruz ki değişmezin gerçekten tutuyor
        /// olduğu bu fixture'ın XML'inden de okunabilsin.
        /// <para>
        /// Değiştiriciler (Ctrl/Cmd) hariç tutulur: tek başına bir eylem değil, başka
        /// bir tuşun niteleyicisidirler, dolayısıyla katalog satırı taşımazlar.
        /// </para>
        /// </summary>
        [Test]
        public void TheCatalogsOwnCompletenessInvariantHolds()
        {
            var catalogued = new HashSet<KeyCode>(NebulaInputActions.All.Select(a => a.Key));
            var modifiers = new HashSet<KeyCode>
            {
                KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftCommand, KeyCode.RightCommand,
                KeyCode.LeftApple, KeyCode.RightApple, KeyCode.LeftAlt, KeyCode.RightAlt,
                KeyCode.LeftShift, KeyCode.RightShift,
            };

            List<KeyCode> violations = LiteralKeyReads().Keys
                .Where(k => !modifiers.Contains(k) && !catalogued.Contains(k))
                .OrderBy(k => k.ToString(), StringComparer.Ordinal)
                .ToList();

            Assert.IsEmpty(violations,
                "production bu tuşları katalogda karşılığı olmadan okuyor: "
                + string.Join(", ", violations));
        }

        // =====================================================================
        // P1-DATA-002 — menu.submit / menu.navigate: Nebula kodunda tüketici yok
        // =====================================================================

        /// <summary>
        /// Katalog <c>menu.submit</c>'i Return'e, <c>menu.navigate</c>'i bir eksene
        /// bağlıyor. Menü/oturum kodunun hiçbir yerinde bu ikisini okuyan bir satır
        /// yok: ikisini de Unity'nin <c>StandaloneInputModule</c>'ü
        /// <c>ProjectSettings/InputManager.asset</c> üzerinden karşılıyor. Katalog
        /// bunu söylemiyor, dolayısıyla "bu action'ın sahibi kim" sorusunun cevabı
        /// katalogda yanlış.
        /// </summary>
        [Test]
        public void Diag_MenuSubmitAndNavigate_HaveNoConsumerAnywhereInMenuOrSessionCode()
        {
            string[] surfaces = ProductionFiles("UI", "Menu")
                .Concat(ProductionFiles("UI", "Session"))
                .Concat(ProductionFiles("UI", "Shell"))
                .ToArray();
            Assert.Greater(surfaces.Length, 5, "menü/oturum kaynakları bulunamadı — tarama boşa çalıştı");

            var readers = new List<string>();
            foreach (string file in surfaces)
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (Regex.IsMatch(lines[i],
                            @"Input\.GetKey(?:Down|Up)?\(\s*KeyCode\.(Return|KeypadEnter)\s*\)")
                        || Regex.IsMatch(lines[i], @"Input\.GetAxis(?:Raw)?\(\s*""(Horizontal|Vertical|Submit)"""))
                        readers.Add($"{Path.GetFileName(file)}:{i + 1}");
            }

            Assert.IsEmpty(readers,
                "BEKLENEN KUSUR KAYBOLDU: menü kodu artık Return/eksen okuyor: " + string.Join(", ", readers));

            // Ve katalog yine de bu iki satırı taşıyor.
            Assert.IsNotNull(NebulaInputActions.Find(NebulaInputActions.MenuSubmit));
            Assert.IsNotNull(NebulaInputActions.Find(NebulaInputActions.MenuNavigate));
            Assert.AreEqual(KeyCode.Return, NebulaInputActions.Key(NebulaInputActions.MenuSubmit));
        }

        /// <summary>
        /// KAPANDI (Part 3/4). Submit'in gerçek sahibi proje ayarıdır:
        /// return/enter/space/pad-0, Cancel ise escape/pad-1. Bunların hiçbiri
        /// katalogda yoktu — space ve return başka satırlar sayesinde tesadüfen
        /// rezerveydi, joystick button 0/1 hiç değildi.
        /// <para>
        /// Artık dördü de katalogda ve <c>UnityAxisOwned</c> işaretini taşıyor. İşaret
        /// önemli: satırın var olması yetmez, "bu tuşu Unity okuyor, buradan yeniden
        /// atayamazsın" bilgisi de katalogda durmalı, yoksa rebind paneli oyuncuya
        /// tutamayacağı bir söz verir.
        /// </para>
        /// </summary>
        [Test]
        public void TheProjectInputManagersOwnKeysAreCataloguedAndMarkedUnityOwned()
        {
            string asset = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "ProjectSettings", "InputManager.asset");
            Assert.IsTrue(File.Exists(asset), "ProjectSettings/InputManager.asset bulunamadı: " + asset);

            string text = File.ReadAllText(asset);
            StringAssert.Contains("m_Name: Submit", text);
            StringAssert.Contains("positiveButton: return", text);
            StringAssert.Contains("altPositiveButton: joystick button 0", text);
            StringAssert.Contains("m_Name: Cancel", text);
            StringAssert.Contains("altPositiveButton: joystick button 1", text);

            foreach (KeyCode key in new[] { KeyCode.JoystickButton0, KeyCode.JoystickButton1 })
            {
                InputActionDefinition row = NebulaInputActions.All.FirstOrDefault(a => a.Key == key);
                Assert.IsNotNull(row, $"{key} katalogda yok — Unity okuyor, katalog bilmiyor");
                Assert.IsTrue(row.UnityAxisOwned,
                    $"{row.Id} katalogda ama Unity sahipli işaretli değil — rebind paneli " +
                    "oyuncuya tutamayacağı bir söz verir");
                Assert.IsFalse(row.Rebindable,
                    $"{row.Id} yeniden atanabilir görünüyor ama tuşu InputManager.asset'te");
            }
        }

        // =====================================================================
        // P1-UI — ok tuşları iki sahipli
        // =====================================================================

        /// <summary>
        /// KAPANDI (Part 3/4). Parent menü sol/sağ oku "sayfa değiştir" diye okuyordu
        /// (<c>NebulaParentMenuScreen.Update</c>) ve Unity'nin <c>StandaloneInputModule</c>'ü
        /// aynı fiziksel tuşları "Horizontal" ekseni olarak Selectable gezinmesi için
        /// okuyordu: iki sahip, tek basış — ve katalog bunu çakışma olarak göremiyordu,
        /// çünkü ikinci sahip <c>Input.GetKey</c> ile değil bir eksenle okuyor.
        /// <para>
        /// Çözüm ok tuşlarını tek sahibe, EventSystem gezinmesine bırakmak ve sayfa
        /// adımlamayı kendi satırlarına (PageUp/PageDown) taşımak oldu. Bu test artık
        /// üç şeyi birden tutuyor, çünkü üçü ayrışırsa kusur geri gelir: ham ok okuması
        /// yok · Horizontal ekseni hâlâ oklara bağlı (yani oklar gerçekten gezinme) ·
        /// ekrandaki ipucu sayfa satırlarının gerçek tuşlarını yazıyor.
        /// </para>
        /// <para>
        /// Üçüncüsü boş bir titizlik değil: ipucu "←→ SAYFA" yazmaya devam etti ve
        /// oyuncuya hiçbir şey yapmayan bir tuşu işaret etti.
        /// </para>
        /// </summary>
        [Test]
        public void TheArrowKeysHaveOneOwner_AndThePromptNamesTheRealPageKeys()
        {
            string screen = Path.Combine(ScriptRoot, "UI", "Menu", "NebulaParentMenuScreen.cs");
            Assert.IsTrue(File.Exists(screen));
            string source = File.ReadAllText(screen);

            StringAssert.DoesNotContain("Input.GetKeyDown(KeyCode.RightArrow)", source,
                "sayfa adımlama ham ok okumasına geri döndü — oklar yine iki sahipli");
            StringAssert.DoesNotContain("Input.GetKeyDown(KeyCode.LeftArrow)", source,
                "sayfa adımlama ham ok okumasına geri döndü — oklar yine iki sahipli");

            string asset = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "ProjectSettings", "InputManager.asset");
            string axes = File.ReadAllText(asset);

            // Stok Unity "Horizontal": negativeButton left / positiveButton right.
            Assert.IsTrue(Regex.IsMatch(axes,
                    @"m_Name: Horizontal[\s\S]{0,200}?negativeButton: left[\s\S]{0,60}?positiveButton: right"),
                "Horizontal ekseni artık ok tuşlarına bağlı değil — oklar gezinmeyi de kaybetti");

            // Ve ipucu, sayfayı gerçekten değiştiren satırların tuşlarını yazıyor.
            string prompt = UI.Menu.NebulaParentMenuScreen.PageStepPrompt();
            StringAssert.Contains(
                Nebula.UI.Controls.NebulaControlBindings.KeyLabel(
                    NebulaInputActions.Key(NebulaInputActions.MenuNextPage)), prompt);
            StringAssert.DoesNotContain("←→", prompt,
                "ipucu hâlâ okları sayfa tuşu diye gösteriyor");
        }

        // =====================================================================
        // P1-UI — sondaj (F) ve savunma (L): tıklanabilir kontrol yok
        // =====================================================================

        /// <summary>
        /// Koordinatörün özellikle istediği kanıt. Sondaj kurulumu ve
        /// Lazer/Plazma kurulumu/yükseltmesi <see cref="ShipExtraction"/> üzerinde
        /// <c>private</c>; tek production çağıranları kendi <c>Update</c>'lerindeki
        /// <c>Input.GetKeyDown</c> satırları. Karşılaştırma için Komuta Merkezi
        /// aynı sınıfta <c>public</c> ve HUD'dan çağrılıyor — yani bu bir "seam
        /// açılamaz" durumu değil, sadece açılmamış bir durum.
        /// </summary>
        [Test]
        public void Diag_DrillAndDefenseBuildHandlers_ArePrivateAndHaveNoUiCallSiteAtAll()
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            MethodInfo drill = typeof(ShipExtraction).GetMethod("ConfirmBuild", any);
            MethodInfo defense = typeof(ShipExtraction).GetMethod("BuildOrUpgradeDefense", any);
            MethodInfo command = typeof(ShipExtraction).GetMethod("TryBuildOrUpgradeCommand", any);

            Assert.IsNotNull(drill, "ShipExtraction.ConfirmBuild kayboldu — bulgu taşınmış olabilir");
            Assert.IsNotNull(defense, "ShipExtraction.BuildOrUpgradeDefense kayboldu");
            Assert.IsNotNull(command, "ShipExtraction.TryBuildOrUpgradeCommand kayboldu");

            Assert.IsFalse(drill.IsPublic, "BEKLENEN KUSUR KAYBOLDU: ConfirmBuild artık public");
            Assert.IsFalse(defense.IsPublic, "BEKLENEN KUSUR KAYBOLDU: BuildOrUpgradeDefense artık public");
            Assert.IsTrue(command.IsPublic,
                "kontrast kayboldu: Komuta Merkezi seam'i de private olmuş");

            // Ve UI katmanında tek bir çağrı yok.
            var uiReferences = new List<string>();
            foreach (string file in ProductionFiles("UI"))
            {
                string text = File.ReadAllText(file);
                if (Regex.IsMatch(text, @"\bConfirmBuild\s*\(") || Regex.IsMatch(text, @"\bBuildOrUpgradeDefense\s*\("))
                    uiReferences.Add(Path.GetFileName(file));
            }
            Assert.IsEmpty(uiReferences,
                "BEKLENEN KUSUR KAYBOLDU: UI artık sondaj/savunma kurulumunu çağırıyor: "
                + string.Join(", ", uiReferences));

            // Kontrast: Komuta Merkezi'nin UI çağrısı vardır (vs-017a bunu genişletiyor).
            bool commandHasUiCaller = ProductionFiles("UI")
                .Any(f => Regex.IsMatch(File.ReadAllText(f), @"\.TryBuildOrUpgradeCommand\s*\("));
            Assert.IsTrue(commandHasUiCaller,
                "Komuta Merkezi'nin UI çağrısı da kaybolmuş — karşılaştırma dayanağı gitti");
        }

        /// <summary>
        /// KAPANDI (Part 3, <c>1354df4</c>). Uçuş HUD'unun tamamı altı düğmeden
        /// ibaretti ve altısı da tek bir dosyadaydı; sondaj, savunma, tarama (R),
        /// aktarım (T) ve işleme paneli (G) için tıklanabilir tek bir kontrol yoktu —
        /// bu eylemler yalnızca klavyeden erişilebiliyordu.
        /// <para>
        /// Bu test artık envanterin küçülmediğini bekliyor. Kontrollerin kesin sayısını
        /// veya hangi dosyada olduğunu <b>pinlemiyor</b>: o sayı her yeni HUD kartıyla
        /// değişir ve değiştiği için kırmızıya dönen bir test, insanları sayıyı
        /// güncellemeye alıştırmaktan başka bir şey yapmaz. Fare/klavye/gamepad
        /// paritesinin gerçek kanıtı canlı kontrolleri süren
        /// <c>P1A3DiagReachabilityTests</c> ve <c>AuditPart4AccessibilityReachTests</c>
        /// içindedir; buradaki kontrol yalnızca dikişlerin HUD'dan görünür kalmasıdır.
        /// </para>
        /// </summary>
        [Test]
        public void TheInFlightHudCarriesControlsForDefenseAndScan()
        {
            var byFile = new Dictionary<string, int>();
            foreach (string file in ProductionFiles("UI", "Hud"))
            {
                int count = Regex.Matches(File.ReadAllText(file), @"NebulaUiFactory\.ButtonControl\(").Count;
                if (count > 0) byFile[Path.GetFileName(file)] = count;
            }

            TestContext.WriteLine("HUD düğmeleri: "
                + string.Join(", ", byFile.Select(kv => $"{kv.Key}={kv.Value}")));

            Assert.IsTrue(byFile.ContainsKey("NebulaHudPlanetContext.cs"),
                "gezegen kartı düğmelerini kaybetti");

            // Klavyeden başka yolu olmayan iki eylem, artık HUD'un sürdüğü dikişlerden
            // geçiyor. Kaybolmaları, kontrolün geri alındığı anlamına gelir.
            //
            // Yorumlar önce siliniyor. Bunu yazarken "TickScan" HUD'da yalnızca bir
            // yorum cümlesinin içinde geçiyordu: yorumları okuyan bir tarama, kontrolün
            // varlığını anlatan bir cümleyi kontrolün kendisi sanar. Aranan adlar da
            // gerçek sembollerin tamamı — dikiş `TryBuildOrUpgradeDefense`, ve
            // `\bBuildOrUpgradeDefense` ona hiç uymaz, çünkü "Try" ile arasında kelime
            // sınırı yoktur. Teşhis hâli tam olarak bu yüzden kör kalıyordu: HUD
            // çağrıyı kazandıktan sonra da "çağırmıyor" diyebiliyordu.
            foreach (string seam in new[]
                     {
                         "TryBuildOrUpgradeDefense(",
                         "actionId: NebulaInputActions.ExtractionScan",
                     })
            {
                bool present = ProductionFiles("UI", "Hud")
                    .Any(f => StripComments(File.ReadAllText(f)).Contains(seam, StringComparison.Ordinal));
                Assert.IsTrue(present,
                    $"HUD '{seam}' dikişini artık kullanmıyor — eylem yine yalnız klavyede kaldı");
            }
        }

        /// <summary>Blok ve satır yorumlarını siler; string içindeki // korunur.</summary>
        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*[\s\S]*?\*/", " ");
            return Regex.Replace(source, @"(?<!:)//[^\n]*", " ");
        }

        // =====================================================================
        // P1-UI — Escape'i aynı karede iki sahip birden tüketiyor
        // =====================================================================

        /// <summary>
        /// P1-UI-006 — <b>KAPANDI</b>. Escape'in tuş yakalama tarafında artık hiçbir
        /// tüketicisi yok; presi ekran yığını taşıyor ve tek seviye geri gidiyor.
        /// <para>
        /// Bulgu şuydu: bir atama silahlanmışken Escape'e basmak iki şeyi birden
        /// yapıyordu — <c>NebulaKeyCaptureListener.Update</c> yakalamayı iptal
        /// ediyor, aynı karede <c>NebulaMainMenuHost</c>/<c>NebulaParentMenuScreen</c>
        /// bir seviye geri gidiyordu. Katalog bunu <c>sharedIntent</c> ile çakışma
        /// taramasından muaf tutuyordu ve muafiyetin gerekçesi "her sahip yalnızca
        /// kendi açık olan şeyi kapatır"dı; burada iki sahibin de kapatacak bir şeyi
        /// vardı, yani muafiyetin koşulu sağlanmıyordu.
        /// </para>
        /// <para>
        /// Bu test kaynak kanıtıdır ve tek başına yeterli değildir: davranış kanıtı
        /// <c>UiFinalPhysicalKeyCaptureTests.Escape_CancelsOnlyTheCapture_AndLeavesTheRebindingPanelOpen</c>
        /// içindedir ve gerçek ekran yığınını sürer.
        /// </para>
        /// </summary>
        [Test]
        public void Escape_HasExactlyOneConsumerOnTheRebindSurface_AndItIsTheScreenStack()
        {
            string listener = File.ReadAllText(
                Path.Combine(ScriptRoot, "UI", "Controls", "NebulaControlBindingSection.cs"));
            string settings = File.ReadAllText(
                Path.Combine(ScriptRoot, "UI", "Menu", "NebulaSettingsPage.cs"));

            // Yakalama dinleyicisi Escape'i ne iptal olarak okuyor ne de aday
            // tuşlar arasında sayıyor.
            Assert.IsFalse(listener.Contains("keyboard.GetKeyDown(KeyCode.Escape)"),
                "tuş yakalama dinleyicisi Escape'i yeniden tüketiyor — iki sahip, tek press");
            StringAssert.Contains("if (key == KeyCode.Escape) continue;", listener,
                "Escape aday tuşlar arasında; yakalama onu bir atama olarak almaya çalışır");

            // Ve press'i taşıyan yer, açık olan en içteki şeyde duruyor.
            StringAssert.Contains("bindings.CapturingActionId != null", settings,
                "ayarlar sayfası Back'i silahlı yakalamada durdurmuyor — tek press iki seviye kapatır");

            // Katalog satırı hâlâ gerçek: ekranın oyuncuya gösterdiği iptal tuşu
            // satırdan okunuyor, sabit yazılmıyor.
            InputActionDefinition rebindCancel = NebulaInputActions.Find(NebulaInputActions.RebindCancel);
            Assert.IsNotNull(rebindCancel, "rebind.cancel satırı katalogdan kayboldu");
            StringAssert.Contains("NebulaInputActions.RebindCancel", listener,
                "ekran iptal tuşunu katalog satırından değil sabitten okuyor");
        }
    }
}
