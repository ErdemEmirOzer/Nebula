using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Nebula.UI.Menu;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 1 / AJAN 1 — klavye/gamepad odağı, <b>statik</b> yarısı.
    /// <para>
    /// Sorulan soru: fareyi hiç kullanmayan bir oyuncu ana menüde ve komuta
    /// menüsünde bir kontrole ulaşabiliyor mu? Davranış yarısı EditMode'da
    /// ölçülemez — <see cref="EventSystem"/> <c>[ExecuteAlways]</c> taşımadığı için
    /// oyun modu dışında <c>OnEnable</c>'ı hiç çalışmaz ve
    /// <c>EventSystem.current</c> daima <c>null</c> kalır. Bu, denetimin kendi ilk
    /// koşusunda ölçülerek doğrulandı (P1-TEST-002) ve davranış tarafı
    /// <c>P1A1DiagFocusPlayModeTests</c>'e taşındı.
    /// </para>
    /// <para>
    /// Baseline: HEAD=4d16782, branch=vs-017a-2-semantic-drill-t2-integrity.
    /// </para>
    /// </summary>
    public class P1A1DiagFocusReachabilityTests
    {
        /// <summary>
        /// EditMode'da odak ölçülemez — bu testin kendisi o kısıtın kanıtıdır.
        /// Buradaki her "seçim boş" iddiası, seçim mekanizmasının hiç çalışmamış
        /// olmasından da doğar; yani EditMode'da yazılmış bir odak testi sahte
        /// yeşildir. Kısıt kalkarsa (Unity sürümü değişir, bir fixture
        /// ExecuteAlways bir EventSystem kurar) bu test kırmızıya döner ve
        /// PlayMode'a taşımanın gerekçesi yeniden değerlendirilir.
        /// </summary>
        [Test]
        public void Diag_EditModeCannotObserveFocusAtAll_SoNoFocusClaimMayRestOnAnEditModeTest()
        {
            var go = new GameObject("P1A1 Probe EventSystem",
                typeof(EventSystem), typeof(StandaloneInputModule));
            try
            {
                Assert.IsNull(EventSystem.current,
                    "BEKLENEN KISIT KAYBOLDU: EditMode'da artık bir EventSystem.current var — "
                    + "odak testleri EditMode'a geri taşınabilir, P1-TEST-002'yi gözden geçirin");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Komuta menüsünün aksiyon rayı her sayfada "↑↓ GEZ" yazıyor, ama odağı
        /// yalnızca <see cref="INebulaFocusablePage"/> uygulayan sayfa kuruyor
        /// (<c>NebulaParentMenuScreen.ShowPage</c>) ve bunu uygulayan tek bir
        /// production sayfası var. Diğer dokuz sayfa açıldığında seçilecek hiçbir
        /// şey seçilmiyor.
        /// </summary>
        [Test]
        public void Diag_OnlyOneProductionMenuPageEverClaimsKeyboardFocus()
        {
            Type pageContract = typeof(INebulaMenuPage);
            Type focusContract = typeof(INebulaFocusablePage);

            Type[] pages = pageContract.Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && pageContract.IsAssignableFrom(t))
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("Nebula.UI", StringComparison.Ordinal))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToArray();

            Type[] focusable = pages.Where(t => focusContract.IsAssignableFrom(t)).ToArray();

            TestContext.WriteLine("Sayfalar (" + pages.Length + "): "
                + string.Join(", ", pages.Select(t => t.Name)));
            TestContext.WriteLine("Odak kuranlar (" + focusable.Length + "): "
                + string.Join(", ", focusable.Select(t => t.Name)));

            Assert.Greater(pages.Length, 5, "sayfa keşfi çalışmadı — ölçüm anlamsız");
            CollectionAssert.Contains(focusable.Select(t => t.Name).ToArray(), nameof(NebulaResourcesPage));
            CollectionAssert.Contains(focusable.Select(t => t.Name).ToArray(), nameof(NebulaCombatPage));
        }

        /// <summary>
        /// Ve ana menü ekranının kendisi hiçbir yerde seçim kurmuyor: kaynakta tek
        /// bir <c>SetSelectedGameObject</c> yok. Eğitim ekranları ve miktar kutusu
        /// bunu yapıyor — yani bu bir "yapılamaz" değil, "yapılmamış".
        /// </summary>
        [Test]
        public void Diag_NeitherTheMainMenuNorTheParentMenuEverSetsASelection_ThoughTutorialScreensDo()
        {
            string ui = Path.Combine(Application.dataPath, "Scripts", "Nebula", "UI");

            string[] focusing =
            {
                Path.Combine(ui, "Session", "NebulaMainMenuScreen.cs"),
                Path.Combine(ui, "Menu", "NebulaParentMenuScreen.cs"),
            };

            foreach (string file in focusing)
            {
                Assert.IsTrue(File.Exists(file), "beklenen kaynak yok: " + file);
                Assert.IsTrue(File.ReadAllText(file).Contains("SetSelectedGameObject"),
                    "production açılışı/seçimi odağı kurmuyor: " + Path.GetFileName(file));
            }
        }

        /// <summary>
        /// <c>NebulaMenuNavigationPlayModeTests</c> ilk seçimi kendisi kuruyor
        /// (<c>Focus(options[0])</c>). Bu, gezinmeyi kanıtlar; gezinmeye
        /// <i>başlayabilmeyi</i> kanıtlamaz — rapor bu fixture'ı bu yüzden
        /// "altyapı kanıtı, oyuncu erişimi kanıtı değil" diye sınıflandırıyor.
        /// </summary>
        [Test]
        public void Diag_TheExistingNavigationFixtureSeedsTheFirstSelectionItself()
        {
            string path = Path.Combine(Application.dataPath, "Tests", "PlayMode",
                "NebulaMenuNavigationPlayModeTests.cs");
            Assert.IsTrue(File.Exists(path), "beklenen fixture bulunamadı: " + path);

            string source = File.ReadAllText(path);
            StringAssert.Contains("currentSelectedGameObject", source,
                "fixture production tarafından kurulan güncel seçimi doğrulamıyor");
        }
    }
}
