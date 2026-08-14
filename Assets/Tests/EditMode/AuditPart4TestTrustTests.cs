using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Nebula.Ship;
using Nebula.UI.Controls;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 4 / AJAN 1 — testin kendisine duyulan güven.
    /// <para>
    /// Buradaki testlerin çoğu ürünü değil, <b>test paketini</b> ölçer. Gerekçe:
    /// yeşil bir paket ancak ölçtüğü şey gerçekse bir şey söyler, ve Part 1 üç ayrı
    /// yanlış-yeşil ailesi buldu — literal'i literal'e doğrulayan maliyet testleri
    /// (P1-TEST-030), davranış yerine <c>Enum.IsDefined</c> (P1-TEST-031), ve
    /// EditMode'da hiç çalışamayacak odak testleri (P1-TEST-002). Bir kuralı bir
    /// kez düzeltmek yetmez; kuralın kendisi zorunlu kılınmalıdır, yoksa bir
    /// sonraki dosyada geri gelir.
    /// </para>
    /// </summary>
    public class AuditPart4TestTrustTests
    {
        private static string TestRoot(string mode) =>
            Path.Combine(Application.dataPath, "Tests", mode);

        private static IEnumerable<(string name, string source)> Fixtures(string mode)
        {
            foreach (string file in Directory.GetFiles(TestRoot(mode), "*.cs", SearchOption.AllDirectories))
                yield return (Path.GetFileName(file), File.ReadAllText(file));
        }

        // =====================================================================
        // P1-TEST-002 — odak testleri yalnızca PlayMode'da
        // =====================================================================

        /// <summary>
        /// EditMode'da hiçbir test bir şeyin <b>odakta olduğunu</b> iddia edemez.
        /// <para>
        /// <see cref="UnityEngine.EventSystems.EventSystem"/> <c>[ExecuteAlways]</c>
        /// taşımaz: oyun modu dışında <c>OnEnable</c>'ı hiç çalışmaz ve
        /// <c>EventSystem.current</c> daima <c>null</c> kalır. Yani EditMode'da
        /// yazılmış bir "şu kontrol seçili" iddiası, seçim mekanizması hiç
        /// çalışmadığı için ya boşta geçer ya da yanlış sebeple kırmızı olur — ikisi
        /// de kanıt değildir. <c>P1A1DiagFocusReachabilityTests</c> kısıtın kendisini
        /// ölçer; bu test kuralı zorunlu kılar.
        /// </para>
        /// <para>
        /// Aranan şey davranış iddiasıdır, tip adı değil: bir EditMode dosyası
        /// <c>EventSystem</c>'den söz edebilir (kısıtı belgelemek için), ama
        /// <c>currentSelectedGameObject</c> üzerinde bir <c>Assert</c> kuramaz.
        /// </para>
        /// </summary>
        [Test]
        public void NoEditModeTest_AssertsThatSomethingIsFocused()
        {
            var offenders = new List<string>();
            // Üye erişimi aranır, dizge değil: bir dosya kısıtı belgelemek için
            // "currentSelectedGameObject" adını bir dizge içinde geçirebilir
            // (P1A1DiagFocusReachabilityTests tam olarak bunu yapar) ve bu bir odak
            // iddiası değildir. İddia, EventSystem'in alanını gerçekten okuyandır.
            var assertion = new Regex(
                @"(Assert|StringAssert|CollectionAssert)\.\w+\([^;]*\.currentSelectedGameObject",
                RegexOptions.Singleline);

            foreach ((string name, string source) in Fixtures("EditMode"))
            {
                // Bu dosya kuralın kendisini taşıyor; deseni tarif etmesi ihlal değil.
                if (name == nameof(AuditPart4TestTrustTests) + ".cs") continue;
                if (assertion.IsMatch(source)) offenders.Add(name);
            }

            Assert.IsEmpty(offenders,
                "bu EditMode fixture'ları odak iddiasında bulunuyor; EditMode'da EventSystem.current "
                + "hiçbir zaman kurulmaz, dolayısıyla iddia BOŞTA geçer. Odak testleri PlayMode'a "
                + "aittir (P1-TEST-002): " + string.Join(", ", offenders));
        }

        /// <summary>
        /// Ve kuralın kaçış kapısı yok: odak davranışını gerçekten ölçen en az bir
        /// PlayMode fixture'ı var. Kural yalnızca "EditMode'da yazma" derse ve
        /// kimse PlayMode'da yazmazsa, sonuç kapsam kaybıdır.
        /// </summary>
        [Test]
        public void FocusBehaviour_IsMeasuredInPlayMode()
        {
            var measuring = new List<string>();
            foreach ((string name, string source) in Fixtures("PlayMode"))
                if (source.Contains("currentSelectedGameObject")) measuring.Add(name);

            Assert.IsNotEmpty(measuring,
                "hiçbir PlayMode fixture'ı odağı ölçmüyor — kural EditMode'dan çıkardı ve "
                + "yerine hiçbir şey koymadı");
        }

        // =====================================================================
        // P2-TEST-009 — Assert.Ignore
        // =====================================================================

        /// <summary>
        /// Atlanan her test <b>neden</b> atlandığını söylemek ve atlamayı bir ortam
        /// koşuluna bağlamak zorundadır.
        /// <para>
        /// <c>Assert.Ignore</c>'un kendisi yasak değildir: grafik aygıtı olmayan bir
        /// koşuda ekran görüntüsü almak gerçekten imkânsızdır ve "imkânsızı sessizce
        /// geç" ile "imkânsızı adıyla bildir" arasındaki fark tam olarak budur. Yasak
        /// olan <b>gerekçesiz</b> ve <b>koşulsuz</b> atlamadır: koşulsuz bir
        /// <c>Ignore</c> testi kalıcı olarak ölü bırakır ve raporda yeşil bir satırdan
        /// ayırt edilemez.
        /// </para>
        /// <para>
        /// Ölçülen iki şey: her <c>Ignore</c>/<c>Inconclusive</c> boş olmayan bir
        /// gerekçe metni taşır, ve bir <c>if</c> koşulunun içindedir. İkisi birden
        /// sağlanmıyorsa test atlanmıyor, kayboluyordur.
        /// </para>
        /// </summary>
        [Test]
        public void EverySkippedTest_SaysWhy_AndSkipsOnlyOnACondition()
        {
            var offenders = new List<string>();
            var call = new Regex(@"Assert\.(Ignore|Inconclusive)\s*\(");

            foreach (string mode in new[] { "EditMode", "PlayMode" })
            foreach ((string name, string source) in Fixtures(mode))
            {
                if (name == nameof(AuditPart4TestTrustTests) + ".cs") continue;

                foreach (Match m in call.Matches(source))
                {
                    string reason = ArgumentText(source, m.Index + m.Length - 1);
                    if (string.IsNullOrWhiteSpace(reason) || !reason.TrimStart().StartsWith("\"")
                        && !reason.Contains("$\""))
                        offenders.Add($"{mode}/{name}: gerekçesiz atlama");

                    if (!GuardedByCondition(source, m.Index))
                        offenders.Add($"{mode}/{name}: koşulsuz atlama (satır {LineOf(source, m.Index)})");
                }
            }

            Assert.IsEmpty(offenders,
                "atlanan testler gerekçesiz veya koşulsuz — koşulsuz bir atlama kalıcı ölü testtir "
                + "ve raporda geçmiş gibi okunur (P2-TEST-009):\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>Bir çağrının parantez içindeki ham metni.</summary>
        private static string ArgumentText(string source, int openParen)
        {
            int depth = 0;
            for (int i = openParen; i < source.Length; i++)
            {
                if (source[i] == '(') depth++;
                else if (source[i] == ')')
                {
                    depth--;
                    if (depth == 0) return source.Substring(openParen + 1, i - openParen - 1);
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Atlama bir koşulun içinde mi? Aynı satırdaki <c>if (…)</c> ya da onu
        /// çevreleyen bloğun başındaki <c>if</c> kabul edilir; ikisi de yoksa atlama
        /// koşulsuzdur ve test hiçbir zaman koşmaz.
        /// </summary>
        private static bool GuardedByCondition(string source, int at)
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
            string line = source.Substring(lineStart, at - lineStart);
            if (line.Contains("if (") || line.Contains("if(")) return true;

            // Blok formu: geriye doğru en fazla altı satır bakılır; açılan süslü
            // parantezin hemen üstünde bir if olmalıdır.
            int cursor = lineStart;
            for (int back = 0; back < 6 && cursor > 0; back++)
            {
                int previousStart = source.LastIndexOf('\n', cursor - 2) + 1;
                string previous = source.Substring(previousStart, cursor - previousStart);
                if (previous.Contains("if (") || previous.Contains("if(")) return true;
                if (previous.Contains("[Test]") || previous.Contains("[UnityTest]")) return false;
                cursor = previousStart;
                if (cursor == 0) break;
            }
            return false;
        }

        private static int LineOf(string source, int index) =>
            source.Take(index).Count(c => c == '\n') + 1;

        // =====================================================================
        // P2-TEST-008 — statik çapraz kirlenme
        // =====================================================================

        /// <summary>
        /// Süreç kapsamlı bir statiğe <b>yazan</b> her fixture, yazdığı her statiği
        /// geri bırakmak zorundadır.
        /// <para>
        /// Üç kabul edilebilir yol vardır ve üçü de gerçek: üretimin kendi sözleşmesi
        /// (<c>NebulaSessionLifetime.ResetProcessWideState</c> — oyunun <c>Dispose</c>'unun
        /// çağırdığı, "oturum bitti"nin tek ifadesi), bir <c>[TearDown]</c> içinde
        /// statiğe yapılan atama, ya da saklanmış bir değerden geri yazma
        /// (<c>saved</c> deseni, <c>finally</c> içinde de olabilir). Kabul edilmeyen
        /// tek şey hiçbiridir: statiği literal bir değere çevirip orada bırakmak.
        /// </para>
        /// <para>
        /// Bunun neden bir kusur olduğu ölçülmüştür: Part 3'te
        /// <c>CombatBindingValidator</c>'ın rezerve tuş cache'i tek bir fixture'ın
        /// rebind'ini süreç boyunca dondurdu ve <b>aynı süreçteki</b> altı input
        /// fixture'ında 25 kırmızı üretti. Bırakılan statik, bir sonraki fixture'ın
        /// ölçtüğü şeydir.
        /// </para>
        /// </summary>
        [Test]
        public void EveryFixtureThatWritesAProcessWideStatic_PutsItBack()
        {
            string[] statics =
            {
                "InputGate.Suppressed", "CombatInputContext.", "CombatBindings.",
                "NebulaInputRouter.", "ShipController.FlightInput", "NebulaKeyCaptureListener.Source",
                "NebulaUiShell.UiScale", "AnimationAccessibility.", "LightingAccessibility.",
                "CombatAccessibilitySettings.",
            };
            var resets = new Regex(
                @"ResetProcessWideState\s*\(|ClearSessionStatics\s*\(|RestoreGlobals\s*\(|Reset(ForSession|"
                + @"ToDefaults|SubscribersForSession|SourceToKeyboard|Contexts|FlightInputToKeyboard|"
                + @"PointerAim)\s*\(");

            var offenders = new List<string>();
            int scanned = 0;
            foreach (string mode in new[] { "EditMode", "PlayMode" })
            foreach ((string name, string source) in Fixtures(mode))
            {
                if (name == nameof(AuditPart4TestTrustTests) + ".cs") continue;

                foreach (string statik in statics)
                    if (Regex.IsMatch(source, @"\b" + Regex.Escape(statik) + @"\w*\s*=[^=]")) { scanned++; break; }

                if (resets.IsMatch(source)) continue;

                string teardown = TeardownBodies(source);
                foreach (string statik in statics)
                {
                    var assignment = new Regex(@"\b" + Regex.Escape(statik) + @"(\w*)\s*=\s*([^=;]+);");
                    MatchCollection written = assignment.Matches(source);
                    if (written.Count == 0) continue;

                    bool putBack = false;
                    foreach (Match m in written)
                    {
                        string member = statik + m.Groups[1].Value;
                        string value = m.Groups[2].Value.Trim();

                        // Teardown'daki her atama geri bırakmadır: fixture orada
                        // kendi bıraktığını temizliyordur.
                        if (teardown.Contains(member + " =") || teardown.Contains(member + "=")) putBack = true;

                        // Ya da saklanmış bir değerden geri yazma. Literal bir değer
                        // (true/false/sayı) geri yazma değildir; bir tanımlayıcı öyledir.
                        if (Regex.IsMatch(value, @"^[A-Za-z_]\w*$")
                            && value != "true" && value != "false" && value != "null")
                            putBack = true;
                    }

                    if (!putBack) offenders.Add($"{mode}/{name}: {statik}");
                }
            }

            // Tarayıcının kendisi kör olabilir. Sıfır ihlal, "hiçbir fixture bu
            // statiklere dokunmuyor" ile "tarama hiçbir şey görmedi" arasında ayrım
            // yapmaz; bu sayaç ikisini ayırır.
            Assert.Greater(scanned, 20,
                $"tarama yalnızca {scanned} fixture'da statik yazımı gördü — bu sayı gerçek "
                + "olamayacak kadar düşük, tarayıcı bozulmuş ve sonucu hiçbir şey kanıtlamıyor");

            Assert.IsEmpty(offenders,
                "bu fixture'lar süreç kapsamlı bir statiği literal bir değere çevirip orada "
                + "bırakıyor; bıraktıkları değer bir sonraki fixture'ın ölçtüğü şeydir "
                + "(P2-TEST-008):\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Her <c>[TearDown]</c> / <c>[OneTimeTearDown]</c> gövdesi, birleştirilmiş.
        /// Gövde, öznitelikten bir sonraki NUnit özniteliğine kadar okunur — süslü
        /// parantez saymaktan daha kaba ama bu tarama için yeterli ve yanlış tarafa,
        /// yani daha az şey kabul etmeye doğru hata yapar.
        /// </summary>
        private static string TeardownBodies(string source)
        {
            var body = new System.Text.StringBuilder();
            var attribute = new Regex(@"\[(TearDown|OneTimeTearDown)\]");
            var next = new Regex(@"\[(Test|UnityTest|SetUp|OneTimeSetUp|TearDown|OneTimeTearDown)\]");

            foreach (Match m in attribute.Matches(source))
            {
                Match end = next.Match(source, m.Index + m.Length);
                int stop = end.Success ? end.Index : source.Length;
                body.Append(source, m.Index, stop - m.Index).Append('\n');
            }
            return body.ToString();
        }

        /// <summary>
        /// Ve sözleşmenin kendisi gerçek: üretim yolu onu çağırıyor. Yalnızca testin
        /// çağırdığı bir "reset" test-only bir arka kapıdır ve bayatlamasını hiçbir
        /// şey engellemez.
        /// </summary>
        [Test]
        public void TheResetContract_IsCalledByProductionTeardown()
        {
            string lifetime = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "Nebula",
                "World", "Composition", "NebulaSessionLifetime.cs"));

            Assert.IsTrue(Regex.IsMatch(lifetime, @"public\s+void\s+Dispose\(\)[\s\S]{0,2000}?ResetProcessWideState\(\)"),
                "NebulaSessionLifetime.Dispose artık ResetProcessWideState'i çağırmıyor — sözleşme "
                + "yalnızca testlerin çağırdığı bir kabuk hâline geldi");
        }

        // =====================================================================
        // P1-DATA-002 / P1-TEST-005 — katalog dürüstlüğü
        // =====================================================================

        /// <summary>
        /// Unity ekseni sahipli satırlar katalogda işaretlidir ve gerçekten Unity'nin
        /// kendi eksenlerine karşılık gelir.
        /// </summary>
        [Test]
        public void UnityAxisOwnedRows_AreMarkedAndMatchTheProjectsOwnAxes()
        {
            var marked = NebulaInputActions.All.Where(a => a.UnityAxisOwned).Select(a => a.Id).ToList();
            CollectionAssert.AreEquivalent(
                new[]
                {
                    NebulaInputActions.MenuNavigate, NebulaInputActions.MenuSubmit,
                    NebulaInputActions.GamepadSubmit, NebulaInputActions.GamepadCancel,
                },
                marked,
                "Unity ekseni sahipli satır kümesi değişti; katalog işareti ile "
                + "ProjectSettings/InputManager.asset ayrıştı");

            string axes = File.ReadAllText(Path.Combine(Application.dataPath, "..",
                "ProjectSettings", "InputManager.asset"));
            StringAssert.Contains("m_Name: Submit", axes,
                "menu.submit Unity ekseni sahipli olarak işaretli ama projede Submit ekseni yok");
            StringAssert.Contains("m_Name: Horizontal", axes,
                "menu.navigate Unity ekseni sahipli olarak işaretli ama projede Horizontal ekseni yok");
        }

        /// <summary>
        /// Ve işaret oyuncuya ulaşıyor: tuş atama ekranı bu satırları "değiştirilemez"
        /// diye adıyla sayıyor. Yalnızca katalogda duran bir işaret, oyuncunun
        /// listede aradığı ve bulamadığı satırı açıklamaz.
        /// </summary>
        [Test]
        public void TheRebindScreen_NamesTheActionsThePlayerMayNotReassign()
        {
            string section = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "Nebula",
                "UI", "Controls", "NebulaControlBindingSection.cs"));
            StringAssert.Contains("UnityAxisOwned", section,
                "tuş atama ekranı Unity ekseni sahipli satırları katalogdan okumuyor");

            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                if (!action.UnityAxisOwned) continue;
                Assert.IsFalse(action.Rebindable,
                    $"{action.Id} Unity ekseni sahipli ama yeniden atanabilir işaretli — "
                    + "ekran değiştirilebileceğini söyler, tuş değişmez");
            }
        }

        // =====================================================================
        // Oyuncuya asla teknik ad gösterilmez
        // =====================================================================

        /// <summary>
        /// Tuş atama rayındaki her grup adı Türkçedir ve hiçbiri enum adı değildir.
        /// <para>
        /// <c>QuickAssetPackage</c> bu kod tabanında bir tiptir, "Hızlı Kurulum" ise
        /// oyunda bir şeydir; ekranda yalnızca ikincisi bulunabilir. Tablo eksik
        /// kalırsa yedek metin devreye girer ve oyuncu "Diğer" görür — bu da bir
        /// kusurdur, o yüzden tablonun tamlığı ölçülür.
        /// </para>
        /// </summary>
        [Test]
        public void EveryBindingGroupName_IsTurkishAndNeverAnEnumName()
        {
            foreach (InputContext context in Enum.GetValues(typeof(InputContext)))
            {
                string name = NebulaControlBindings.GroupName(context);
                Assert.IsNotNull(name, $"{context} için grup adı yok");
                Assert.AreNotEqual("Diğer", name,
                    $"{context} grup adı tablosunda yok; oyuncu yedek metni görür");
                Assert.AreNotEqual(context.ToString(), name,
                    $"{context} grup adı enum adının kendisi — oyuncuya sınıf adı gösteriliyor");
            }
        }

        // =====================================================================
        // P2-TIME-001 — geçici mesajlar duvar saatiyle söner
        // =====================================================================

        /// <summary>
        /// Hiçbir geçici mesaj ömrü ölçekli zamandan okunmaz.
        /// <para>
        /// Bir menü açıldığında <c>Time.timeScale</c> sıfırlanır; ömrünü
        /// <c>Time.time</c> ile ölçen bir mesaj o anda donar ve oyuncuya kalıcıymış
        /// gibi görünür — menüyü kapatana kadar ekranda durur. Ledger bu satırı
        /// yalnız kaynağa bakarak kapatmıştı; kural artık kendini savunuyor.
        /// </para>
        /// <para>
        /// Kaynaktan ölçülüyor, çünkü ölçülen şey bir karar: hangi saatin okunduğu.
        /// Bir testin <c>unscaledTime</c>'ı ilerletmesi mümkün değil, ama yanlış saati
        /// okuyan satırı görmek mümkün.
        /// </para>
        /// </summary>
        [Test]
        public void NoTransientMessageLifetimeReadsScaledTime()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "Nebula");
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int comment = line.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) line = line.Substring(0, comment);

                    if (!line.Contains("messageUntil") && !line.Contains("TransientMessage")) continue;
                    if (!Regex.IsMatch(line, @"\bTime\.(time|fixedTime)\b")) continue;

                    offenders.Add($"{Path.GetFileName(file)}:{i + 1} — {line.Trim()}");
                }
            }

            Assert.IsEmpty(offenders,
                "bu satırlar geçici mesaj ömrünü ölçekli zamandan okuyor; menü açıkken mesaj "
                + "donar ve kalıcı görünür:\n  " + string.Join("\n  ", offenders));

            // Ve kural boşlukta çalışmıyor: gerçekten ölçülen satırlar var.
            int measured = 0;
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                foreach (string line in File.ReadAllLines(file))
                    if (line.Contains("TransientMessage") && line.Contains("Time.unscaledTime")) measured++;

            Assert.Greater(measured, 0,
                "hiçbir TransientMessage ömrü bulunamadı — tarama körleşmiş, kural hiçbir şeyi korumuyor");
        }
    }
}
