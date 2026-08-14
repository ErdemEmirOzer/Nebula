using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 3 / A3 — P1-TEST-003, P2-TEST-005, P1-UI-002.
    /// <para>
    /// Bulgu şuydu: <c>NebulaMenuNavigationPlayModeTests</c>'in dokuz testinden beşi
    /// odağı <b>kendi eliyle</b> kuruyor (<c>Focus(options[0])</c>) ve <b>hiçbiri</b>
    /// ürünün başlangıç odağını kurduğunu ölçmüyordu. Suite 9/9 yeşilken menü faresiz
    /// kullanılamıyordu — yanlış-yeşilin ders kitabı örneği.
    /// </para>
    /// <para>
    /// Bu fixture o eksik iddiadır ve tek bir yerde bile odağı kendisi kurmaz. Ürün
    /// odağı kurmazsa buradaki her test kırmızıya döner; testin kendisi hiçbir eksiği
    /// telafi etmez.
    /// </para>
    /// </summary>
    public class AuditPart3MenuFocusTests
    {
        private NebulaUiShell shell;
        private EventSystem eventSystem;
        private GameObject eventSystemObject;
        private bool ownsEventSystem;
        private NebulaParentMenuScreen menu;

        [SetUp]
        public void Setup()
        {
            ExclusiveModal.ResetForSession();

            // Exactly one EventSystem in the scene, ever: NebulaUiShell.Create
            // installs one when the scene has none.
            eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystemObject = new GameObject("Part3 Focus EventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
                ownsEventSystem = true;
            }

            shell = NebulaUiShell.Create();
            eventSystem = EventSystem.current;
            Assert.IsNotNull(eventSystem, "there is no EventSystem — focus cannot be observed at all");
        }

        [TearDown]
        public void Teardown()
        {
            if (eventSystem != null) eventSystem.SetSelectedGameObject(null);
            if (menu != null) { menu.Close(); menu = null; }
            if (shell != null)
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
                shell = null;
            }
            if (ownsEventSystem && eventSystemObject != null) Object.DestroyImmediate(eventSystemObject);
            eventSystemObject = null;
            ownsEventSystem = false;
            eventSystem = null;
        }

        private NebulaParentMenuScreen BuildMenu()
        {
            NebulaParentMenuScreen screen = NebulaParentMenuScreen.Create(shell);
            screen.AddPage(new NebulaOverviewPage(null));
            screen.AddPage(new NebulaProgressionPage(null));
            screen.AddPage(new NebulaFleetPage(null));
            screen.AddPage(new NebulaPlanetsPage(null));
            screen.AddPage(new NebulaResourcesPage(null));
            screen.AddPage(new NebulaCombatPage(null));
            screen.AddPage(new NebulaDiscoveryPage(null));
            screen.AddPage(new NebulaSettingsPage());
            return screen;
        }

        private static readonly NebulaUiAssetCatalog.Page[] Pages =
        {
            NebulaUiAssetCatalog.Page.Overview,
            NebulaUiAssetCatalog.Page.Progression,
            NebulaUiAssetCatalog.Page.Fleet,
            NebulaUiAssetCatalog.Page.Planets,
            NebulaUiAssetCatalog.Page.Resources,
            NebulaUiAssetCatalog.Page.Combat,
            NebulaUiAssetCatalog.Page.Discovery,
            NebulaUiAssetCatalog.Page.Settings,
        };

        /// <summary>A selection a keyboard can actually stand on: real, alive, and usable.</summary>
        private void AssertFocusIsUsable(string where)
        {
            GameObject selected = eventSystem.currentSelectedGameObject;
            Assert.IsNotNull(selected,
                $"{where}: ürün hiçbir kontrole odak kurmadı — menü faresiz kullanılamaz (P1-UI-002).");
            Assert.IsTrue(selected.activeInHierarchy,
                $"{where}: odak kapalı bir nesnede duruyor; Unity navigasyonu oradan hiçbir yere gidemez.");

            var selectable = selected.GetComponent<Selectable>();
            Assert.IsNotNull(selectable, $"{where}: odaktaki nesne bir kontrol değil.");
            Assert.IsTrue(selectable.IsInteractable(),
                $"{where}: odak devre dışı bir kontrolde — oyuncunun bastığı hiçbir tuş bir şey yapmaz.");
        }

        // =====================================================================

        /// <summary>
        /// P1-TEST-003 / P2-TEST-005'in tam olarak eksik olan iddiası: ekran açıldığında
        /// odağı <b>ürün</b> kurar. Test hiçbir yerde <c>SetSelectedGameObject</c> çağırmaz.
        /// </summary>
        [UnityTest]
        public IEnumerator OpeningTheMenu_EstablishesFocusWithoutTheTestSettingIt()
        {
            menu = BuildMenu();
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Overview), "menü açılmadı");
            yield return null;
            yield return null;

            AssertFocusIsUsable("menü açılışı");
        }

        /// <summary>Her sayfa kendi başına odaklanabilir olmalı — biri boşsa klavye orada takılır.</summary>
        [UnityTest]
        public IEnumerator EveryPage_EstablishesItsOwnFocusOnOpen()
        {
            menu = BuildMenu();
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Overview), "menü açılmadı");
            yield return null;

            var landed = new List<string>();
            foreach (NebulaUiAssetCatalog.Page page in Pages)
            {
                menu.ShowPage(page);
                yield return null;
                AssertFocusIsUsable(page + " sayfası");
                landed.Add(page + " → " + eventSystem.currentSelectedGameObject.name);
            }

            Assert.AreEqual(Pages.Length, landed.Count,
                "her sayfa için bir odak kaydı beklenirdi:\n  " + string.Join("\n  ", landed));
        }

        /// <summary>
        /// Bayat seçim, seçimsizlik kadar ölümcüldür: önceki sayfanın kontrolü hâlâ
        /// EventSystem'de durur ama nesnesi kapatılmıştır ve navigasyon oradan hiçbir
        /// yere yürüyemez. Ürün bunu da toparlamalı.
        /// </summary>
        [UnityTest]
        public IEnumerator AStaleSelectionFromThePreviousPage_IsReplacedNotKept()
        {
            menu = BuildMenu();
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Overview), "menü açılmadı");
            yield return null;

            menu.ShowPage(NebulaUiAssetCatalog.Page.Fleet);
            yield return null;
            AssertFocusIsUsable("Fleet sayfası");
            GameObject first = eventSystem.currentSelectedGameObject;

            menu.ShowPage(NebulaUiAssetCatalog.Page.Combat);
            yield return null;

            AssertFocusIsUsable("Combat sayfası");
            if (first != null && !first.activeInHierarchy)
                Assert.AreNotSame(first, eventSystem.currentSelectedGameObject,
                    "odak, kapatılmış önceki sayfanın kontrolünde bırakıldı");
        }

        /// <summary>
        /// Odağın gerçekten <i>çalıştığı</i>: ürünün kurduğu seçim üzerinden gönderilen
        /// bir submit, kontrolün kendi handler'ına ulaşır. Odak "bir yerde duruyor"
        /// değil, "kullanılabilir" demektir.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFocusTheProductEstablished_AcceptsASubmit()
        {
            menu = BuildMenu();
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Overview), "menü açılmadı");
            yield return null;
            AssertFocusIsUsable("menü açılışı");

            GameObject selected = eventSystem.currentSelectedGameObject;
            bool clicked = false;
            var button = selected.GetComponent<Button>();
            Assert.IsNotNull(button, "ürünün kurduğu odak bir düğmede değil — submit gönderilecek yer yok");
            button.onClick.AddListener(() => clicked = true);

            ExecuteEvents.Execute(selected, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
            yield return null;

            Assert.IsTrue(clicked,
                "ürünün kurduğu odağa gönderilen submit hiçbir yere ulaşmadı: odak görsel bir süsten ibaret.");
        }
    }
}
