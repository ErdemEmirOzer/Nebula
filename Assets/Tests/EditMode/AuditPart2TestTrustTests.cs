using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 2 / A3 — test güvenilirliği kapıları.
    /// <para>
    /// İki grup test var ve karıştırılmamalı:
    /// </para>
    /// <list type="bullet">
    /// <item><b>ALTYAPI KAPILARI</b> (<c>TypecheckTool*</c>, <c>NoFixtureUses*</c>) — <b>yeşil kalmalı</b>.
    /// Bunlar Part 2'de onarılan <c>UIDesign/typecheck.sh</c>'ın ve paylaşılan EditMode assembly'sinin
    /// bir daha sessizce bozulmamasını sağlar. Kırmızıya dönerlerse denetim aracı yine körleşmiş demektir.</item>
    /// <item><b>ULAŞILABİLİRLİK BULGULARI</b> (<c>Finding_*</c>) — kırmızı kanıt XML'i alındıktan
    /// sonra bug ID'siyle <see cref="ExplicitAttribute"/> olarak normal paketten ayrılmıştı.
    /// Part 3'te hepsi üretim düzeltmesiyle birlikte normal regresyona dönüştürüldü; bu dosyada
    /// ve <c>Assets/Tests</c> ağacının tamamında artık tek bir <c>[Explicit]</c> yoktur.</item>
    /// </list>
    /// <para>
    /// Bu fixture kaynak tarar; hiçbir sahne, session veya statik durum kurmaz ve hiçbirini kirletmez.
    /// </para>
    /// </summary>
    [Category("AuditPart2")]
    public class AuditPart2TestTrustTests
    {
        private const string SelfFileName = "AuditPart2TestTrustTests.cs";

        private static string ProjectRoot =>
            Directory.GetParent(UnityEngine.Application.dataPath)!.FullName;

        private static string ReadProjectFile(string relativePath)
        {
            string full = Path.Combine(ProjectRoot, relativePath);
            Assert.IsTrue(File.Exists(full), $"beklenen dosya yok: {relativePath}");
            return File.ReadAllText(full);
        }

        private static IEnumerable<string> TestSourceFiles()
        {
            string tests = Path.Combine(UnityEngine.Application.dataPath, "Tests");
            return Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p) != SelfFileName);
        }

        // =====================================================================
        // ALTYAPI KAPILARI — yeşil kalmalı
        // =====================================================================

        /// <summary>
        /// Part 2'nin kök bulgusu: araç, Unity'nin derlediği 6 assembly'den yalnız 1-2'sini derliyordu
        /// ve bunu söylemiyordu. Yeni bir <c>.asmdef</c> eklenip araca eklenmezse o assembly sessizce
        /// doğrulanmamaya başlar — tam olarak Part 1'i sakatlayan durum. Bu test o kapıdır.
        /// </summary>
        [Test]
        public void TypecheckToolCoversEveryAssemblyUnityCompiles()
        {
            string tool = ReadProjectFile(Path.Combine("UIDesign", "typecheck.sh"));

            string[] asmdefNames = Directory
                .EnumerateFiles(UnityEngine.Application.dataPath, "*.asmdef", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.IsNotEmpty(asmdefNames, "projede hiç asmdef bulunamadı — tarama yolu yanlış");

            string[] uncovered = asmdefNames.Where(n => !tool.Contains(n)).ToArray();

            Assert.IsEmpty(uncovered,
                "UIDesign/typecheck.sh şu assembly'leri hiç derlemiyor, dolayısıyla 'TYPECHECK OK' " +
                "onlar hakkında hiçbir şey söylemiyor: " + string.Join(", ", uncovered) +
                "\nAraca bir aşama ekleyin (ALL_STAGES tablosu). Part 1'i sakatlayan kusur tam olarak buydu.");
        }

        /// <summary>
        /// Predefined <c>Assembly-CSharp-Editor</c> (<c>Assets/Editor/Nebula/*</c>) bir asmdef taşımaz,
        /// bu yüzden yukarıdaki tarama onu görmez. Part 2'ye kadar hiçbir tip kontrolünden geçmemişti.
        /// </summary>
        [Test]
        public void TypecheckToolAlsoCoversThePredefinedEditorAssembly()
        {
            string tool = ReadProjectFile(Path.Combine("UIDesign", "typecheck.sh"));

            Assert.IsTrue(tool.Contains("Assembly-CSharp-Editor"),
                "Assets/Editor/Nebula altındaki editor araçları hiçbir tip kontrolüne girmiyor");
            Assert.IsTrue(tool.Contains("--all"),
                "her assembly'yi tek komutta doğrulayan bir mod olmalı; aksi hâlde kimse hepsini koşmaz");
        }

        /// <summary>
        /// "Yeşil" ancak neyin doğrulandığını söylüyorsa bir şey ifade eder. Araç her koşuyu
        /// VERIFIED / NOT VERIFIED listesiyle bitirmeli ve bilinmeyen bayrağı sessizce yutmamalı.
        /// </summary>
        [Test]
        public void TypecheckToolStatesWhatItDidNotVerify()
        {
            string tool = ReadProjectFile(Path.Combine("UIDesign", "typecheck.sh"));

            Assert.IsTrue(tool.Contains("NOT VERIFIED"),
                "araç hangi assembly'yi doğrulamadığını bildirmiyor — 'TYPECHECK OK' kapsamı belirsiz kalıyor");
            Assert.IsTrue(tool.Contains("ASSEMBLY VERIFIED") && tool.Contains("ASSEMBLY NOT VERIFIED"),
                "her assembly başlığının kesin VERIFIED / NOT VERIFIED sonucu olmalı");
            Assert.IsTrue(tool.Contains("COMPILE_TIMEOUT_SECONDS") && tool.Contains("TimeoutExpired"),
                "yarım kalan compiler süreci için watchdog yok; footer gelmeden asılabilir");
            Assert.IsTrue(tool.Contains("COMPILE_STATUS") && tool.Contains("$COMPILE_STATUS -ne 0") &&
                          tool.Contains("exit 1"),
                "compiler exit code kaydedilip koşunun exit code'una yansıtılmıyor");
            Assert.IsTrue(tool.Contains("unknown option"),
                "bilinmeyen bayrak sessizce runtime-only kontrole düşüp TYPECHECK OK yazmamalı");
            Assert.IsTrue(tool.Contains("declaration phase"),
                "hata varken Roslyn'in gövde fazını atladığı uyarılmalı; aksi hâlde eksik liste tam sanılır");
        }

        /// <summary>
        /// <c>com.unity.ext.nunit@1.0.6</c> bu API'leri taşımıyor. Aynı çağrı Part 1'de ve Part 2'de
        /// birer kez, paylaşılan EditMode assembly'sini derlenemez hâle getirip **bütün ajanların**
        /// Unity koşusunu <c>RUN INVALID</c> yaptı. Derleyici bunu ancak assembly'yi düşürerek söyler;
        /// bu test dosya tarayarak, koşudan önce söyler.
        /// </summary>
        [Test]
        public void NoFixtureUsesAnNUnitApiThisPackageDoesNotHave()
        {
            // NUnit 3.5 (Unity ext 1.0.6) yüzeyinde bulunmayan, geçmişte gerçekten koşu düşürmüş çağrılar.
            string[] unavailable = { "Assert" + ".Multiple(", "Assert" + ".Enter" + "MultipleScope(" };

            var offenders = new List<string>();
            foreach (string path in TestSourceFiles())
            {
                string text = File.ReadAllText(path);
                foreach (string api in unavailable)
                {
                    if (!text.Contains(api)) continue;
                    offenders.Add($"{Path.GetFileName(path)} → {api.TrimEnd('(')}");
                }
            }

            Assert.IsEmpty(offenders,
                "bu NUnit sürümünde olmayan API kullanılmış; assembly derlenmez ve HER ajanın koşusu " +
                "RUN INVALID olur:\n  " + string.Join("\n  ", offenders) +
                "\nÇoklu iddia için ayrı [Test] metotları veya tek tek Assert çağrıları kullanın.");
        }

        // =====================================================================
        // ULAŞILABİLİRLİK BULGULARI — kırmızı XML saklandı; normal pakette Explicit
        // =====================================================================

        private static IEnumerable<string> RuntimeSourceFiles(string subPath = "")
        {
            string root = Path.Combine(UnityEngine.Application.dataPath, "Scripts", "Nebula", subPath);
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories);
        }

        /// <summary>
        /// P2-DEAD-001. <c>ShipCombat.TryStartProduction(WeaponFamily, int)</c> silah üretiminin tek
        /// giriş kapısı; arkasında <c>WeaponLoadoutService</c>, <c>WeaponCatalog</c> maliyet tablosu ve
        /// <c>WeaponProductionRejectReason</c> duruyor. Production'da **hiçbir** çağrısı yok.
        /// </summary>
        [Test]
        public void Finding_WeaponProductionIsReachableFromTheGame()
        {
            int callSites = 0;
            foreach (string path in RuntimeSourceFiles())
            {
                if (path.EndsWith("ShipCombat.cs", StringComparison.Ordinal)) continue; // bildirimin kendisi
                string text = File.ReadAllText(path);
                callSites += Regex.Matches(text, @"\bTryStartProduction\s*\(\s*(WeaponFamily|weaponFamily|family)\b").Count;
            }

            Assert.Greater(callSites, 0,
                "P2-DEAD-001: silah üretimi hattının oyuncuya açılan tek çağrısı yok. " +
                "NebulaCombatPage gerçek ShipCombat üretim seam'ini çağırmalıdır.");
        }

        /// <summary>
        /// P2-DEAD-002. <c>ShipPoi.TrySendVehicle</c> araç görevlendirmenin API'si; bildirimi dışında
        /// production ağacında tek satır çağrısı yok, yalnız Part 1'in teşhis fixture'ı çağırıyor.
        /// </summary>
        [Test]
        public void Finding_VehicleDispatchIsReachableFromTheGame()
        {
            int callSites = 0;
            foreach (string path in RuntimeSourceFiles())
            {
                if (path.EndsWith("ShipPoi.cs", StringComparison.Ordinal)) continue; // bildirimin kendisi
                callSites += Regex.Matches(File.ReadAllText(path), @"\bTrySendVehicle\s*\(").Count;
            }

            Assert.Greater(callSites, 0,
                "P2-DEAD-002: ShipPoi.TrySendVehicle'ın production'da hiçbir çağrısı yok. " +
                "POI'ye araç göndermenin backend'i hazır, UI/input zinciri eksik.");
        }

        // =====================================================================
        // P2-TEST-001 — CRUISE GİRİŞİ: MENÜ GİRDİYİ SUSTURUR, DÜNYAYI DURDURMAZ
        // Part 3'te normal regresyona dönüştürüldü (artık [Explicit] değil).
        // =====================================================================

        private static string ShipCruiseSource() => ReadProjectFile(Path.Combine(
            "Assets", "Scripts", "Nebula", "Ship", "ShipCruise.cs"));

        /// <summary>
        /// P2-TEST-001. <c>CruiseEntryConditions.InputFree</c> tek production kurulum noktasında
        /// sabit <c>true</c> yazılıyordu. Alan hiçbir zaman cruise'u engelleyemediği için onu
        /// doğrulayan her test bir totolojiyi ölçüyordu. Artık canlı girdi durumundan hesaplanır.
        /// </summary>
        [Test]
        public void Finding_CruiseInputFreeIsComputedNotHardcoded()
        {
            string cruise = ShipCruiseSource();

            bool hardcoded = Regex.IsMatch(cruise, @"InputFree\s*=\s*true\s*,");

            Assert.IsFalse(hardcoded,
                "P2-TEST-001: ShipCruise, InputFree'yi sabit true yazıyor. Alan hiçbir girdi durumunu " +
                "temsil etmediği için cruise'u bloklayamaz ve alanı ölçen testler sabiti ölçer. " +
                "Gerçek girdi durumundan (InputGate) hesaplanmalıdır.");

            Assert.IsTrue(Regex.IsMatch(cruise, @"InputFree\s*=\s*!\s*InputGate\.Suppressed"),
                "InputFree canlı girdi durumundan hesaplanmalı: menü açıkken false, kapalıyken true.");
        }

        /// <summary>
        /// <c>BuildConditions</c> her kare, gate'in önünde çalışır — dünya durumunu oradan
        /// okumak doğrudur, oyuncu girdisini oradan okumak değildir. Menü açılırken basılı
        /// olan bir tuş, gate'in önünde okunursa "aşağıda" donar: bırakıldığını hiçbir kare
        /// göremez ve cruise oturumun sonuna kadar bloklu kalır.
        /// </summary>
        [Test]
        public void CruiseSamplesNoPlayerInputInFrontOfTheGate()
        {
            string body = MethodBody(ShipCruiseSource(), "BuildConditions");

            // Statement by statement, not line by line: the guard and the read are
            // deliberately written on two lines of one expression.
            foreach (string statement in body.Split(';'))
            {
                if (!Regex.IsMatch(statement, @"\bInput\.Get\w+\s*\(|\bNebulaInputRouter\.(Held|Pressed)\s*\(")) continue;
                Assert.IsTrue(statement.Contains("InputGate.Suppressed"),
                    "ShipCruise.BuildConditions girdi kapısının önünde oyuncu girdisi okuyor:\n  " +
                    statement.Trim() + "\nMenü açıkken okunan her tuş, oyuncunun o kareyi hiç yaşamadığı bir girdidir.");
            }
        }

        /// <summary>
        /// Aynı koşulun iki yarısı ayrı tutulmalı: <b>basılı tuş</b> girdidir ve yalnız girdi
        /// canlıyken örneklenir; <b>süren tarama</b> dünya durumudur ve menü arkasında da
        /// doğrudur. İkisi tek terime çökerse ya menü taramayı yok sayar ya da basılı tuş
        /// kalıcı engel bırakır.
        /// </summary>
        [Test]
        public void TheHeldScanKeyIsInputAndTheRunningScanIsWorldState()
        {
            string body = MethodBody(ShipCruiseSource(), "BuildConditions");

            Assert.IsTrue(Regex.IsMatch(body, @"InputGate\.Suppressed[\s\S]{0,120}?NebulaInputRouter\.Held\s*\(\s*NebulaInputActions\.ExtractionScan"),
                "basılı tarama tuşu girdi kapısının arkasında örneklenmiyor");
            Assert.IsTrue(body.Contains("ScanLatched"),
                "süren tarama dünya durumu olarak okunmuyor; menü açıkken devam eden bir tarama cruise'u serbest bırakır");
        }

        /// <summary>
        /// Yalnız girdi okuyucusu gate'in arkasında olmalı — ve gerçekten orada olmalı.
        /// <para>
        /// Bu kontrol tek satırlık <c>if (...) HandleHeadingLockInput()</c> yazımını
        /// birebir arıyordu. Gate'in arkasına ikinci bir girdi okuması (cruise aç/kapa)
        /// eklenince blok süslü paranteze dönüştü ve test, davranış hiç değişmediği
        /// hâlde kırmızıya döndü: ölçtüğü şey değişmezin kendisi değil, o gün yazılmış
        /// biçimdi. Artık çağrının gate bloğunun <b>içinde</b> olduğunu sayarak
        /// doğruluyor — kardeş ifade eklemek serbest, gate'in dışına çıkmak değil.
        /// </para>
        /// </summary>
        [Test]
        public void TheHeadingLockInputReaderStaysBehindTheGate()
        {
            string update = MethodBody(ShipCruiseSource(), "Update");

            Match gate = Regex.Match(update, @"if\s*\(\s*!\s*InputGate\.Suppressed\s*\)\s*\{");
            Assert.IsTrue(gate.Success,
                "ShipCruise.Update artık girdiyi InputGate arkasına almıyor");

            int open = update.IndexOf('{', gate.Index);
            int depth = 0, close = -1;
            for (int i = open; i < update.Length; i++)
            {
                if (update[i] == '{') depth++;
                else if (update[i] == '}' && --depth == 0) { close = i; break; }
            }
            Assert.Greater(close, open, "gate bloğu kapanmıyor");

            string guarded = update.Substring(open + 1, close - open - 1);
            StringAssert.Contains("HandleHeadingLockInput()", guarded,
                "yön sabitleme girdisi menü açıkken de okunuyor — çağrı InputGate bloğunun dışında");
        }

        /// <summary>Bir metodun gövdesini kaynaktan çıkarır (süslü parantez sayarak).</summary>
        private static string MethodBody(string source, string methodName)
        {
            int signature = source.IndexOf(methodName + "(", StringComparison.Ordinal);
            Assert.Greater(signature, 0, methodName + " kaynakta bulunamadı — tarama körleşmiş");

            int open = source.IndexOf('{', signature);
            Assert.Greater(open, 0, methodName + " gövdesi bulunamadı");

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open + 1, i - open - 1);
            }
            Assert.Fail(methodName + " gövdesi kapanmıyor");
            return string.Empty;
        }

        /// <summary>
        /// Dünya saati menünün arkasında işlemeye devam eder; okunmayan tek şey oyuncunun
        /// girdisidir. Bu yüzden <c>stateMachine.Tick</c> ve <c>RotationLocked</c> yayını
        /// gate'in <b>önünde</b>, girdi okuyucusu <b>arkasında</b> kalmalıdır.
        /// </summary>
        [Test]
        public void CruiseWorldTickStaysInFrontOfTheInputGate()
        {
            string cruise = ShipCruiseSource();

            Assert.IsFalse(Regex.IsMatch(cruise, @"if\s*\(\s*InputGate\.Suppressed\s*\)\s*return\s*;"),
                "ShipCruise.Update erken dönüyor: menü açıkken cruise rampası, yavaşlaması ve " +
                "RotationLocked yayını da duruyor. Menü girdiyi susturur, dünya saatini değil.");

            Assert.IsTrue(cruise.Contains("stateMachine.Tick("),
                "cruise durum makinesi her kare tick edilmeli");
        }

        // ---- entry/sustain ayrımı, saf durum makinesi üzerinde ---------------

        private static Nebula.Navigation.CruiseEntryConditions Ready(bool inputFree) =>
            new Nebula.Navigation.CruiseEntryConditions
            {
                PlayerArmed = true,
                ThrottleHigh = true, NotStabilized = true, BuildModeOff = true,
                NoPhysicalInteractionActive = true, NotNearHazard = true, NoSharpTurn = true,
                NotInStellarRisk = true, NotDamaged = true, InputFree = inputFree
            };

        private const float CruiseLocalMax = 180f;
        private const float CruiseTargetT1 = 1800f;
        private const float CruiseFrame = 1f / 60f;

        private static Nebula.Navigation.CruiseStateMachine EngineT1()
        {
            var machine = new Nebula.Navigation.CruiseStateMachine();
            machine.SetEngineTier(1);
            return machine;
        }

        private static void Run(Nebula.Navigation.CruiseStateMachine machine,
            Nebula.Navigation.CruiseEntryConditions conditions, float seconds)
        {
            for (float t = 0f; t < seconds; t += CruiseFrame)
                machine.Tick(CruiseFrame, conditions, CruiseLocalMax, CruiseTargetT1);
        }

        /// <summary>Menü açık, gaz sonuna kadar basılı, diğer her koşul sağlanmış: yeni cruise başlamaz.</summary>
        [Test]
        public void AScreenOpen_NeverStartsANewCruise_HoweverLongTheThrottleIsHeld()
        {
            Nebula.Navigation.CruiseStateMachine machine = EngineT1();

            Run(machine, Ready(inputFree: false), 30f);

            Assert.AreEqual(Nebula.Navigation.FlightMode.LocalFlight, machine.Mode,
                "P2-TEST-001: ekran açıkken basılı tutulan gaz yeni bir cruise başlattı");
            Assert.AreEqual(1f, machine.SpeedMultiplier, 0.0001f);
        }

        /// <summary>Menü açılmadan önce başlamış bir cruise, menü açıkken de ilerlemeye devam eder.</summary>
        [Test]
        public void ACruiseBegunBeforeTheScreenOpened_KeepsRampingAndReachesDeepSpaceCruise()
        {
            Nebula.Navigation.CruiseStateMachine machine = EngineT1();

            Run(machine, Ready(inputFree: true), 1f);
            Assert.AreEqual(Nebula.Navigation.FlightMode.CruiseRamp, machine.Mode, "cruise hiç başlamadı — test bir şey ölçmüyor");
            float rampedBefore = machine.RampElapsed;

            // Screen opens mid-ramp. The world clock keeps running.
            Run(machine, Ready(inputFree: false), Nebula.Navigation.CruiseStateMachine.RampSeconds + 1f);

            Assert.Greater(machine.RampElapsed + 1f, rampedBefore, "rampa menü açılınca ilerlemeyi bıraktı");
            Assert.AreEqual(Nebula.Navigation.FlightMode.DeepSpaceCruise, machine.Mode,
                "menü açıkken devam eden cruise Deep Space Cruise'a ulaşamadı");
            Assert.Greater(machine.SpeedMultiplier, 1f);
        }

        /// <summary>Ekran kapanır kapanmaz giriş yeniden normal çalışır — bir sonraki karede.</summary>
        [Test]
        public void WhenTheScreenCloses_CruiseEntryWorksOnTheVeryNextFrame()
        {
            Nebula.Navigation.CruiseStateMachine machine = EngineT1();

            Run(machine, Ready(inputFree: false), 5f);
            Assert.AreEqual(Nebula.Navigation.FlightMode.LocalFlight, machine.Mode);

            machine.Tick(CruiseFrame, Ready(inputFree: true), CruiseLocalMax, CruiseTargetT1);

            Assert.AreEqual(Nebula.Navigation.FlightMode.CruiseRamp, machine.Mode,
                "ekran kapandıktan sonra girdi hâlâ ölü: cruise girişi çalışmıyor");
        }

        /// <summary>Yavaşlarken ekran açıksa rampaya geri tırmanmaz; girdi canlanınca tırmanır.</summary>
        [Test]
        public void DeceleratingWithAScreenOpen_DoesNotClimbBackIntoTheRampUntilInputIsLive()
        {
            Nebula.Navigation.CruiseStateMachine machine = EngineT1();

            Run(machine, Ready(inputFree: true), Nebula.Navigation.CruiseStateMachine.RampSeconds + 1f);
            Assert.AreEqual(Nebula.Navigation.FlightMode.DeepSpaceCruise, machine.Mode);

            Nebula.Navigation.CruiseEntryConditions hazard = Ready(inputFree: true);
            hazard.NotNearHazard = false;
            machine.Tick(CruiseFrame, hazard, CruiseLocalMax, CruiseTargetT1);
            Assert.AreEqual(Nebula.Navigation.FlightMode.CruiseDeceleration, machine.Mode);

            // Hazard cleared, but a screen is open: the re-acceleration is a player
            // transition and must wait for live input.
            Run(machine, Ready(inputFree: false), 1f);
            Assert.AreEqual(Nebula.Navigation.FlightMode.CruiseDeceleration, machine.Mode,
                "ekran açıkken yavaşlama rampaya geri tırmandı");

            machine.Tick(CruiseFrame, Ready(inputFree: true), CruiseLocalMax, CruiseTargetT1);
            Assert.AreEqual(Nebula.Navigation.FlightMode.CruiseRamp, machine.Mode,
                "girdi canlandığında yavaşlama rampaya dönmedi");
        }

        /// <summary>
        /// Ekran açılırken basılı tutulan tarama tuşu kalıcı bir engel bırakmamalı. Menü
        /// boyunca tuş hiç örneklenmediği için koşul dünya durumundan gelir; menü kapanınca
        /// koşul yeniden hesaplanır ve cruise girişi ilk karede çalışır.
        /// </summary>
        [Test]
        public void AScanKeyHeldAsTheScreenOpens_DoesNotPermanentlyBlockCruise()
        {
            Nebula.Navigation.CruiseStateMachine machine = EngineT1();

            // The key really is down as the screen opens: while it is open the reader
            // never runs, so the condition is whatever the WORLD says (no transfer, no
            // latched scan) and the only thing holding cruise back is InputFree.
            Nebula.Navigation.CruiseEntryConditions screenOpenKeyDown = Ready(inputFree: false);
            Run(machine, screenOpenKeyDown, 5f);
            Assert.AreEqual(Nebula.Navigation.FlightMode.LocalFlight, machine.Mode,
                "ekran açıkken cruise başladı");

            // Screen closes with the key now released: nothing latched, entry on frame one.
            machine.Tick(CruiseFrame, Ready(inputFree: true), CruiseLocalMax, CruiseTargetT1);
            Assert.AreEqual(Nebula.Navigation.FlightMode.CruiseRamp, machine.Mode,
                "ekran kapandıktan sonra cruise hâlâ bloklu — basılı tuş kalıcı bir engel bıraktı");

            // And a scan that is genuinely still running is world state, so it keeps
            // blocking with the screen shut -- the two halves are not the same thing.
            Nebula.Navigation.CruiseStateMachine second = EngineT1();
            Nebula.Navigation.CruiseEntryConditions scanRunning = Ready(inputFree: true);
            scanRunning.NoPhysicalInteractionActive = false;
            Run(second, scanRunning, 5f);
            Assert.AreEqual(Nebula.Navigation.FlightMode.LocalFlight, second.Mode,
                "süren tarama cruise'u bloklamıyor");
        }

        /// <summary>
        /// Ayrımın kendisi: <c>AllMet</c> saf dünya durumudur (sürdürme), <c>CanEngage</c>
        /// ek olarak canlı girdi ister (giriş). İkisi aynı şey olursa menü açık bir cruise'u
        /// yavaşlatmaya zorlar ve "dünya menü arkasında işler" hükmü bozulur.
        /// </summary>
        [Test]
        public void SustainAndEntryAreTwoDifferentQuestions()
        {
            Nebula.Navigation.CruiseEntryConditions screenOpen = Ready(inputFree: false);

            Assert.IsTrue(screenOpen.AllMet,
                "AllMet girdi durumunu içeriyor: menü açıldığı anda uçuştaki cruise sürdürülemez sayılır");
            Assert.IsFalse(screenOpen.CanEngage,
                "CanEngage girdi durumunu içermiyor: menü arkasında yeni cruise başlatılabilir");
            Assert.IsTrue(Ready(inputFree: true).CanEngage);
        }

        /// <summary>
        /// P2-TEST-002 / P2-DEAD-003. Tutorial, <c>WeaponLoadoutService</c>'in maliyet ve tier kilidi
        /// kontrollerini atlayıp doğrudan <c>Hardpoint.Equip</c> çağırıyordu. Tutorial'ın "plazma tak"
        /// adımını doğrulayan testler bu yüzden normal oyunda var olmayan bir yolu ölçüyordu.
        /// <para>
        /// Tarama tek dosyada değil, <b>bütün tutorial ağacında</b> yapılır: kusuru bir chapter'dan
        /// diğerine taşımak da düzeltme sayılmaz.
        /// </para>
        /// </summary>
        [Test]
        public void Finding_TutorialGrantsWeaponsThroughTheSameServiceAsTheGame()
        {
            var offenders = new List<string>();
            foreach (string path in RuntimeSourceFiles("Tutorial"))
            {
                string text = File.ReadAllText(path);
                foreach (Match match in Regex.Matches(text, @"^.*\.\s*Equip\s*\(.*$", RegexOptions.Multiline))
                    offenders.Add($"{Path.GetFileName(path)} → {match.Value.Trim()}");
            }

            Assert.IsEmpty(offenders,
                "P2-TEST-002/P2-DEAD-003: tutorial Hardpoint.Equip'i doğrudan çağırarak WeaponLoadoutService'in " +
                "tier kilidini (PlasmaUnlockWeaponCoordinationTier), katalog maliyetini ve katalog süresini " +
                "atlıyor. Tutorial oyuncuya, normal oyunda ulaşamayacağı bir silahı bedavaya veriyor:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// P2-DEAD-003'ün diğer yarısı: bir hardpoint'e silah kuran <b>tek</b> yer
        /// <c>WeaponLoadoutService.Complete</c> (ücretli, rezerve edilmiş işin tamamlanması) ve
        /// <c>ShipCombat.InitializeNewCampaign</c> (doğuştan gelen bedava Laser) olmalıdır.
        /// Üçüncü bir çağıran, ücretsiz silah demektir.
        /// </summary>
        [Test]
        public void OnlyAPaidProductionJobOrTheBirthLaserEverEquipsAHardpoint()
        {
            var callSites = new List<string>();
            foreach (string path in RuntimeSourceFiles())
            {
                string file = Path.GetFileName(path);
                if (file == "HardpointState.cs") continue; // bildirimin kendisi
                string text = File.ReadAllText(path);
                foreach (Match match in Regex.Matches(text, @"^.*\.\s*Equip\s*\(.*$", RegexOptions.Multiline))
                    callSites.Add($"{file} → {match.Value.Trim()}");
            }

            var unexpected = callSites
                .Where(c => !c.StartsWith("WeaponLoadoutService.cs", StringComparison.Ordinal)
                            && !c.StartsWith("ShipCombat.cs", StringComparison.Ordinal))
                .ToArray();

            Assert.IsEmpty(unexpected,
                "hardpoint'e silah kuran beklenmedik bir çağrı var; ücretli üretim yolunu atlıyor olabilir:\n  " +
                string.Join("\n  ", unexpected));
            Assert.IsNotEmpty(callSites, "tarama hiçbir Equip çağrısı bulamadı — desen artık kör");
        }
    }
}
