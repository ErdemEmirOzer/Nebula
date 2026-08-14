using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 1 / AJAN 1 — klavye/gamepad odağı, <b>davranış</b> yarısı.
    /// <para>
    /// Ölçülen tek şey şu: ekran açıldıktan sonra <see cref="EventSystem"/>'de
    /// seçili bir nesne var mı? Unity'nin <c>StandaloneInputModule</c>'ü seçili
    /// nesne yokken hiçbir hareket olayı üretmez
    /// (<c>SendMoveEventToSelectedObject</c> doğrudan <c>false</c> döner) ve
    /// <c>EventSystem.firstSelectedGameObject</c> hiçbir yerde kurulmuyor —
    /// dolayısıyla "seçim boş" demek, "klavye oku ve gamepad çubuğu hiçbir yere
    /// ulaşmıyor" demektir.
    /// </para>
    /// <para>
    /// Mevcut <c>NebulaMenuNavigationPlayModeTests</c> bu adımı atlıyor: ilk
    /// seçimi <c>Focus(options[0])</c> ile kendisi kuruyor, sonra gezinmeyi
    /// kanıtlıyor. Bu fixture yalnızca atlanan adımı ölçer.
    /// </para>
    /// <para>
    /// Baseline: HEAD=4d16782, branch=vs-017a-2-semantic-drill-t2-integrity.
    /// </para>
    /// </summary>
    public class P1A1DiagFocusPlayModeTests
    {
        private NebulaUiShell shell;
        private NebulaMainMenuScreen menu;
        private NebulaParentMenuScreen parentMenu;
        private bool enteredGate;

        private sealed class NoopLauncher : INebulaSessionLauncher
        {
            public void StartNewCampaign() { }
            public void ContinueFromSlot(int slot) { }
            public void QuitGame() { }
            public void ReturnToMainMenu() { }
            public void RestartCampaign() { }
        }

        private sealed class StubPage : INebulaMenuPage
        {
            public StubPage(NebulaUiAssetCatalog.Page page) => Page = page;
            public NebulaUiAssetCatalog.Page Page { get; }
            public string Title => Page.ToString();
            public int AttentionCount => 0;
            public void Build(RectTransform host) { }
            public void Refresh() { }
            public bool HandleBack() => false;
        }

        [SetUp]
        public void Setup()
        {
            enteredGate = InputGate.Suppressed;
            ExclusiveModal.ResetForSession();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        [TearDown]
        public void Teardown()
        {
            if (menu != null) Object.Destroy(menu.gameObject);
            if (parentMenu != null) Object.Destroy(parentMenu.gameObject);
            if (shell != null)
            {
                shell.Shutdown();
                Object.Destroy(shell.gameObject);
            }
            menu = null;
            parentMenu = null;
            shell = null;

            ExclusiveModal.ResetForSession();
            InputGate.Suppressed = enteredGate;
        }

        /// <summary>
        /// Ölçümün dayanağı: oyun modunda gerçekten bir <see cref="EventSystem"/>
        /// var. Bu doğru değilse aşağıdaki her "seçim boş" iddiası anlamsızdır ve
        /// sahte yeşil olur — bu yüzden ayrı bir test olarak sabitlendi.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShellReallyStandsUpAnEventSystemInPlayMode()
        {
            shell = NebulaUiShell.Create();
            yield return null;

            Assert.IsNotNull(EventSystem.current,
                "shell bir EventSystem kurmadı — aşağıdaki odak ölçümleri dayanaksız olurdu");
            Assert.IsNotNull(EventSystem.current.GetComponent<StandaloneInputModule>(),
                "EventSystem'de girdi modülü yok — hareket olayları hiç işlenmezdi");
        }

        /// <summary>
        /// Ana menü açılır, gezilecek birden fazla kontrol vardır ve hiçbiri
        /// seçili değildir. Fare olmadan bu menüde tek bir düğme etkinleştirilemez.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMainMenuOpensWithNothingSelected_SoKeyboardAndPadReachNothing()
        {
            shell = NebulaUiShell.Create();
            menu = NebulaMainMenuScreen.Create(shell, new NoopLauncher());
            Assert.IsTrue(shell.Push(menu), "ana menü shell'e itilemedi");
            yield return null;

            Assert.IsNotNull(EventSystem.current, "EventSystem yok — ölçüm dayanaksız");

            Selectable[] options = menu.MenuColumn.GetComponentsInChildren<Selectable>(true)
                .Where(s => s.gameObject.activeInHierarchy && s.IsInteractable())
                .ToArray();
            Assert.Greater(options.Length, 1,
                "menüde iki'den az etkileşilebilir kontrol var — ölçüm trivially geçerdi");

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(selected, "ana menü production açılışında ilk kullanılabilir kontrolü seçmedi");
            Assert.IsTrue(options.Any(option => option.gameObject == selected),
                "seçilen nesne ana menünün kullanılabilir kontrollerinden biri değil");
        }

        /// <summary>
        /// Ve seçim boşken bir hareket olayı hiçbir yere gitmez. Burada
        /// <c>StandaloneInputModule</c>'ün kendi kararı taklit edilmiyor: seçili
        /// nesne <c>null</c> olduğu için gönderilecek bir hedef yoktur, ve
        /// gönderimden sonra seçim hâlâ boştur.
        /// </summary>
        [UnityTest]
        public IEnumerator WithNothingSelected_AMoveEventLandsNowhereAndSelectsNothing()
        {
            shell = NebulaUiShell.Create();
            menu = NebulaMainMenuScreen.Create(shell, new NoopLauncher());
            shell.Push(menu);
            yield return null;

            EventSystem events = EventSystem.current;
            Assert.IsNotNull(events);
            events.SetSelectedGameObject(null);
            yield return null;

            GameObject target = events.currentSelectedGameObject;
            Assert.IsNull(target, "ön koşul bozuldu: bir şey zaten seçiliydi");

            // StandaloneInputModule.SendMoveEventToSelectedObject'ün yaptığı çağrı,
            // hedefi olmadığı için hiçbir şeye ulaşmaz.
            bool handled = target != null && ExecuteEvents.Execute(
                target, new AxisEventData(events) { moveDir = MoveDirection.Down },
                ExecuteEvents.moveHandler);

            Assert.IsFalse(handled, "seçim boşken bir hareket olayı işlendi — beklenmiyordu");
            Assert.IsNull(events.currentSelectedGameObject,
                "BEKLENEN KUSUR KAYBOLDU: boş seçimden sonra bir kontrol seçildi");
        }

        /// <summary>
        /// Aynı boşluk komuta menüsünde de var: odak kurmayan bir sayfa açıldığında
        /// rayda tıklanabilir düğmeler durur ve seçim boş kalır.
        /// </summary>
        [UnityTest]
        public IEnumerator OpeningANonFocusablePageLeavesTheSelectionEmpty()
        {
            shell = NebulaUiShell.Create();
            parentMenu = NebulaParentMenuScreen.Create(shell);
            parentMenu.AddPage(new StubPage(NebulaUiAssetCatalog.Page.Overview));
            parentMenu.AddPage(new StubPage(NebulaUiAssetCatalog.Page.Settings));
            yield return null;

            EventSystem.current.SetSelectedGameObject(null);
            Assert.IsTrue(parentMenu.Open(NebulaUiAssetCatalog.Page.Overview), "komuta menüsü açılamadı");
            yield return null;

            Assert.AreEqual(NebulaUiAssetCatalog.Page.Overview, parentMenu.CurrentPage);

            // Rayın düğmeleri bileşenin kendi GameObject'inde değil, shell'in Menu
            // katmanı kökünde duruyor: ekran bileşeni ile çizdiği kök ayrı nesneler.
            Button[] rail = shell.Root(UiLayer.Menu).GetComponentsInChildren<Button>(true)
                .Where(b => b.gameObject.activeInHierarchy && b.IsInteractable()).ToArray();
            Assert.Greater(rail.Length, 0, "rayda etkileşilebilir düğme yok — ölçüm anlamsız olurdu");

            Assert.IsNotNull(EventSystem.current.currentSelectedGameObject,
                "komuta menüsü sayfa veya ray odağını kurmadı");
        }

        /// <summary>
        /// Sayfa adımlaması da kurmuyor: ok tuşunun yaptığı çağrı yeni sayfayı
        /// açar, seçimi kurmaz. Yani ←→ ile sayfa gezen bir oyuncu, açtığı hiçbir
        /// sayfanın içine klavyeyle giremez.
        /// </summary>
        [UnityTest]
        public IEnumerator SteppingPagesNeverEstablishesFocusEither()
        {
            shell = NebulaUiShell.Create();
            parentMenu = NebulaParentMenuScreen.Create(shell);
            parentMenu.AddPage(new StubPage(NebulaUiAssetCatalog.Page.Overview));
            parentMenu.AddPage(new StubPage(NebulaUiAssetCatalog.Page.Combat));
            parentMenu.Open(NebulaUiAssetCatalog.Page.Overview);
            yield return null;

            EventSystem.current.SetSelectedGameObject(null);
            parentMenu.StepPage(1);
            yield return null;

            Assert.AreEqual(NebulaUiAssetCatalog.Page.Combat, parentMenu.CurrentPage,
                "sayfa adımlaması çalışmadı — ölçüm anlamsız");
            Assert.IsNotNull(EventSystem.current.currentSelectedGameObject,
                "sayfa adımlaması yeni sayfada veya rayda odak kurmadı");
        }

        /// <summary>
        /// Kontrast: aynı shell üzerinde eğitim mod ekranı odağı kuruyor. Bu, ana
        /// menünün odak kurmamasının bir platform kısıtı değil, bir eksik olduğunu
        /// gösterir — aynı çerçevede, aynı EventSystem ile, yapılabiliyor.
        /// </summary>
        [UnityTest]
        public IEnumerator ATutorialScreenOnTheSameShellDoesEstablishFocus()
        {
            shell = NebulaUiShell.Create();
            yield return null;

            var modes = Nebula.UI.Tutorial.TutorialModeScreen.Create(shell, null);
            try
            {
                EventSystem.current.SetSelectedGameObject(null);
                Assert.IsTrue(shell.Push(modes), "eğitim mod ekranı itilemedi");
                yield return null;

                Assert.IsNotNull(EventSystem.current.currentSelectedGameObject,
                    "karşılaştırma dayanağı gitti: eğitim ekranı da artık odak kurmuyor");
            }
            finally
            {
                if (modes != null) Object.Destroy(modes.gameObject);
            }
        }
    }
}
