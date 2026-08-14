using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-10: the interface must survive being used.
    /// <para>
    /// The explicit acceptance bar is a thousand open/close cycles with no growth.
    /// The failure this guards against is the ordinary one — a widget created per
    /// open, a listener added per refresh, a row appended instead of reused —
    /// which never shows up in a screenshot and only bites after an hour of play.
    /// </para>
    /// </summary>
    public class NebulaUiLifecycleTests
    {
        private const int Cycles = 1000;

        private NebulaUiShell shell;
        private float enteredTimeScale;

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void Teardown()
        {
            if (shell != null)
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
            }
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
        }

        private NebulaParentMenuScreen BuildMenu()
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            menu.AddPage(new NebulaOverviewPage(null));
            menu.AddPage(new NebulaProgressionPage(null));
            menu.AddPage(new NebulaFleetPage(null));
            menu.AddPage(new NebulaPlanetsPage(null));
            menu.AddPage(new NebulaCombatPage(null));
            menu.AddPage(new NebulaDiscoveryPage(null));
            menu.AddPage(new NebulaResourcesPage(null));
            menu.AddPage(new NebulaSettingsPage());
            menu.AddPage(new NebulaSessionPage(null));
            return menu;
        }

        private static int CountObjects(Transform root) =>
            root.GetComponentsInChildren<Transform>(true).Length;

        // ---- the controller must outlive the thing it hides -------------------

        /// <summary>
        /// Every HUD widget starts with its panel hidden, because a HUD element that
        /// has nothing to say should say nothing. That is only survivable if the
        /// controller is somewhere else: a controller parented into its own panel is
        /// deactivated along with it, its Update stops, and it can never decide to
        /// come back — the widget is dead for the rest of the session and only a
        /// screenshot of an empty screen would ever show it.
        /// <para>
        /// This shipped. Vitals, navigation, alerts, planet context, the context bar
        /// and notifications all sat inside their own panels. It survived the EditMode
        /// suite because those tests drive the widgets directly, and surfaced only
        /// when PlayMode asked the scene for them and the scene had none.
        /// </para>
        /// </summary>
        [Test]
        public void EveryHudController_StaysActiveWhenItsPanelIsHidden()
        {
            var built = new List<Component>
            {
                NebulaHudVitals.Create(shell, null),
                NebulaHudNavigation.Create(shell, null, null),
                NebulaHudAlerts.Create(shell, null, null),
                NebulaHudPlanetContext.Create(shell, null, null),
                NebulaHudContextBar.Create(shell),
                NebulaHudNotifications.Create(shell),
            };

            var layers = new HashSet<Transform>();
            foreach (UiLayer layer in System.Enum.GetValues(typeof(UiLayer)))
                layers.Add(shell.Root(layer));

            // The screens matter more than the widgets here: each reads its own
            // opening key in Update, so a screen switched off by its own root can
            // never be opened again -- the parent command menu was exactly that.
            built.Add(BuildMenu());
            built.Add(NebulaConfirmationDialog.Create(shell));

            foreach (Component widget in built)
            {
                Assert.IsTrue(widget.gameObject.activeInHierarchy,
                    widget.GetType().Name + " was switched off by its own panel");

                // The structural rule behind the symptom, so the next widget cannot
                // reintroduce it by hiding a different root.
                Assert.Contains(widget.transform.parent, new List<Transform>(layers),
                    widget.GetType().Name + " must hang off a shell layer, not off a panel");
            }
        }

        /// <summary>
        /// What the PlayMode fixtures actually do to reach a widget. Unity's
        /// FindObjectOfType skips inactive objects, so this is the same assertion
        /// as above stated in the form that broke.
        /// </summary>
        [Test]
        public void EveryHudController_IsReachableFromTheScene()
        {
            NebulaHudVitals.Create(shell, null);
            NebulaHudNavigation.Create(shell, null, null);
            NebulaHudAlerts.Create(shell, null, null);
            NebulaHudPlanetContext.Create(shell, null, null);
            NebulaHudContextBar.Create(shell);
            NebulaHudNotifications.Create(shell);

            Assert.IsNotNull(Object.FindObjectOfType<NebulaHudVitals>());
            Assert.IsNotNull(Object.FindObjectOfType<NebulaHudNavigation>());
            Assert.IsNotNull(Object.FindObjectOfType<NebulaHudAlerts>());
            Assert.IsNotNull(Object.FindObjectOfType<NebulaHudPlanetContext>());
            Assert.IsNotNull(Object.FindObjectOfType<NebulaHudContextBar>());
            Assert.IsNotNull(Object.FindObjectOfType<NebulaHudNotifications>());
        }

        // ---- the world-space level tag ---------------------------------------

        /// <summary>
        /// ProgressionHud was screen-space except for one thing: the level tag over
        /// the ship. Deleting that class took the tag with it and nothing replaced
        /// it, because the notification line is a HUD row and this is a TextMesh in
        /// world space. Caught by re-reading the deleted file, not by any test --
        /// hence this one.
        /// </summary>
        [Test]
        public void TheShipKeepsItsWorldSpaceLevelTag()
        {
            var ship = new GameObject("Ship");
            try
            {
                NebulaShipLevelTag levelTag = NebulaShipLevelTag.Create(ship.transform);

                var text = levelTag.GetComponent<TMPro.TextMeshPro>();
                Assert.IsNotNull(text, "the tag is world-space text");
                Assert.AreSame(NebulaUiFactory.SharedFontAsset, text.font,
                    "on the same SDF face every other player-facing label uses -- "
                    + "a world-space tag takes an SDF asset, which is why the "
                    + "TextMesh exemption did not survive being checked");
                Assert.IsNotNull(levelTag.GetComponent<Billboard>(), "and it faces the camera");
                Assert.AreEqual(new Vector3(0f, NebulaShipLevelTag.Height, 0f),
                    levelTag.transform.localPosition, "at the height it always sat at");
                Assert.AreSame(ship.transform, levelTag.transform.parent,
                    "parented to the ship, so it travels with it");

                StringAssert.StartsWith("L", levelTag.Text, "it reads L<level>");
            }
            finally { Object.DestroyImmediate(ship); }
        }

        // ---- the acceptance bar ----------------------------------------------

        [Test]
        public void AThousandOpenCloseCycles_CreateNothingNew()
        {
            NebulaParentMenuScreen menu = BuildMenu();

            // Open once first so anything built lazily on first open is already
            // there — otherwise the baseline would absorb the very growth we want
            // to measure.
            menu.Open(NebulaUiAssetCatalog.Page.Overview);
            menu.Close();

            int baseline = CountObjects(shell.transform);

            for (int i = 0; i < Cycles; i++)
            {
                menu.Open(NebulaUiAssetCatalog.Page.Overview);
                menu.Close();
            }

            Assert.AreEqual(baseline, CountObjects(shell.transform),
                $"The interface grew over {Cycles} open/close cycles. Something is created " +
                "per open instead of being reused.");
        }

        [Test]
        public void AThousandPageSwitches_CreateNothingNew()
        {
            NebulaParentMenuScreen menu = BuildMenu();
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            // Walk every page once so each has built its content.
            for (int i = 0; i < menu.PageCount; i++) menu.StepPage(1);
            int baseline = CountObjects(shell.transform);

            for (int i = 0; i < Cycles; i++) menu.StepPage(1);

            Assert.AreEqual(baseline, CountObjects(shell.transform),
                "Switching pages allocated new objects. Page content must be built once and reshown.");
        }

        [Test]
        public void AThousandRefreshes_CreateNothingNew()
        {
            var pages = new List<INebulaMenuPage>();
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            var overview = new NebulaOverviewPage(null);
            var fleet = new NebulaFleetPage(null);
            var resources = new NebulaResourcesPage(null);
            menu.AddPage(overview);
            menu.AddPage(fleet);
            menu.AddPage(resources);
            pages.Add(overview);
            pages.Add(fleet);
            pages.Add(resources);

            foreach (INebulaMenuPage page in pages) page.Refresh();
            int baseline = CountObjects(shell.transform);

            for (int i = 0; i < Cycles; i++)
                foreach (INebulaMenuPage page in pages)
                    page.Refresh();

            Assert.AreEqual(baseline, CountObjects(shell.transform),
                "A page refresh created objects. Rows must be keyed and reused, not rebuilt.");
        }

        [Test]
        public void AThousandContextBarUpdates_CreateNothingNew()
        {
            NebulaHudContextBar bar = NebulaHudContextBar.Create(shell);
            var secondaries = new List<HudAction> { new HudAction("İKİNCİL", KeyCode.B) };

            bar.Show("BAĞLAM", new HudAction("BİRİNCİL", KeyCode.F, "30 PARÇA"), secondaries);
            int baseline = CountObjects(shell.transform);

            for (int i = 0; i < Cycles; i++)
            {
                // Alternate the shape of what is shown, so a naive implementation
                // that rebuilds on change is caught rather than one that happens to
                // be stable because nothing ever varies.
                if ((i & 1) == 0)
                    bar.Show("BAĞLAM", new HudAction("BİRİNCİL", KeyCode.F, "30 PARÇA"), secondaries);
                else
                    bar.Show("DİĞER", new HudAction("TARA", KeyCode.R, "%48"));
            }

            Assert.AreEqual(baseline, CountObjects(shell.transform),
                "The context bar allocated widgets while updating. Its slots are fixed and must be reused.");
        }

        // ---- pause and gate never leak ---------------------------------------

        [Test]
        public void AThousandCycles_LeaveTheGameRunningAndTheGateFree()
        {
            NebulaParentMenuScreen menu = BuildMenu();
            Time.timeScale = 1f;

            for (int i = 0; i < Cycles; i++)
            {
                menu.Open(NebulaUiAssetCatalog.Page.Overview);
                menu.Close();
            }

            Assert.AreEqual(1f, Time.timeScale, "Repeated open/close drifted the pause.");
            Assert.IsFalse(InputGate.Suppressed, "Repeated open/close left input suppressed.");
            Assert.IsNull(ExclusiveModal.Owner, "Repeated open/close leaked the modal gate.");
            Assert.AreEqual(0, shell.StackDepth);
        }

        [Test]
        public void RestartingTheShell_LeavesExactlyOneOfEverything()
        {
            // A restart that leaves the previous shell's canvases behind is the
            // classic way two HUDs end up drawn on top of each other.
            BuildMenu();
            shell.Shutdown();
            Object.DestroyImmediate(shell.gameObject);

            shell = NebulaUiShell.Create();
            BuildMenu();

            var canvases = Object.FindObjectsOfType<Canvas>(true);
            int shellCanvases = 0;
            foreach (Canvas canvas in canvases)
                if (canvas.GetComponentInParent<NebulaUiShell>() != null) shellCanvases++;

            Assert.AreEqual(4, shellCanvases,
                "A restarted session left more than one shell's canvases alive.");
        }

        [Test]
        public void OnlyOneShellEverHoldsTheModalGate()
        {
            NebulaParentMenuScreen first = BuildMenu();
            first.Open(NebulaUiAssetCatalog.Page.Overview);

            var second = NebulaUiShell.Create();
            try
            {
                NebulaParentMenuScreen other = NebulaParentMenuScreen.Create(second);
                other.AddPage(new NebulaOverviewPage(null));

                Assert.IsFalse(other.Open(NebulaUiAssetCatalog.Page.Overview),
                    "A second shell opened a screen while the first still held the gate.");
            }
            finally
            {
                second.Shutdown();
                Object.DestroyImmediate(second.gameObject);
            }
        }
    }
}
