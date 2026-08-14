using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Navigation;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-07: combat loadout and the discovery log. Discovery is where the
    /// information gate is easiest to break, because every field it could show
    /// already exists in memory — so that is what these tests aim at.
    /// </summary>
    public class NebulaCombatDiscoveryPageTests
    {
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

        private RectTransform Host()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            go.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        // ---- the information gate --------------------------------------------

        [Test]
        public void ASignalIsNeverNamed()
        {
            // A system at Signal knowledge has a real display name in the registry.
            // Printing it would let the player learn a place exists, and what it is
            // called, from a rumour — the easiest leak in the game.
            string shown = NebulaDiscoveryPage.DisplayName(
                "system-that-really-exists", SystemKnowledgeLevel.Signal, null);

            Assert.IsFalse(shown.Contains("system-that-really-exists"),
                $"A signal disclosed its system id: '{shown}'");
            StringAssert.Contains("SİNYAL", shown);
        }

        [Test]
        public void AnUnknownSystemIsNeverNamed()
        {
            string shown = NebulaDiscoveryPage.DisplayName(
                "hidden-system", SystemKnowledgeLevel.Unknown, null);
            Assert.IsFalse(shown.Contains("hidden-system"));
        }

        [Test]
        public void ALocatedSystemWithNoRecordStillDoesNotLeakItsId()
        {
            // Falling back to the raw id would be the obvious shortcut and the
            // obvious leak.
            string shown = NebulaDiscoveryPage.DisplayName(
                "some-internal-id", SystemKnowledgeLevel.Located, null);
            Assert.IsFalse(shown.Contains("some-internal-id"), $"Fallback leaked the id: '{shown}'");
        }

        [Test]
        public void EveryKnowledgeLevel_HasPlayerFacingCopy()
        {
            foreach (SystemKnowledgeLevel level in System.Enum.GetValues(typeof(SystemKnowledgeLevel)))
            {
                string name = NebulaDiscoveryPage.LevelName(level);
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.AreNotEqual(level.ToString(), name,
                    $"{level} is being shown as its raw enum name.");
            }
        }

        // ---- combat copy -----------------------------------------------------

        [Test]
        public void EveryWeaponFamily_HasPlayerFacingCopy()
        {
            foreach (WeaponFamily family in System.Enum.GetValues(typeof(WeaponFamily)))
            {
                string name = NebulaCombatPage.WeaponName(family);
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.AreNotEqual(family.ToString(), name);
            }
            Assert.AreEqual("BOŞ", NebulaCombatPage.WeaponName(null));
        }

        [Test]
        public void EveryRaidPhase_HasPlayerFacingCopy()
        {
            foreach (RaidPhase phase in System.Enum.GetValues(typeof(RaidPhase)))
            {
                string name = NebulaCombatPage.PhaseName(phase);
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.AreNotEqual(phase.ToString(), name,
                    $"{phase} is being shown as its raw enum name.");
            }
        }

        // ---- null-session safety and stability -------------------------------

        [Test]
        public void TargetingReplacesBothHalvesOfTheCombatHudsTextCorner()
        {
            // CombatHud drew the target as three stacked lines on the reticle and
            // the other hostiles as a six-line list in a corner, rebuilding two
            // Lists every frame (L-09). Neither had any home in the redesign, so
            // deleting that class without this would have removed both.
            var shell = NebulaUiShell.Create();
            try
            {
                NebulaHudTargeting targeting = NebulaHudTargeting.Create(shell, null, null);

                Assert.IsFalse(targeting.TargetVisible, "nothing targeted, no bracket");
                Assert.AreEqual(0, targeting.VisibleMarkers, "no hostiles, no markers");
                Assert.AreEqual(0, targeting.OverflowCount);
                Assert.AreEqual(6, NebulaHudTargeting.MaxMarkers,
                    "the same six the old list capped at");
            }
            finally
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
            }
        }

        [Test]
        public void TargetingPoolsItsMarkersInsteadOfRebuildingThem()
        {
            // The point of L-09: the markers exist up front and are shown or hidden,
            // so a frame with six hostiles allocates nothing.
            var shell = NebulaUiShell.Create();
            try
            {
                NebulaHudTargeting targeting = NebulaHudTargeting.Create(shell, null, null);
                int markers = 0;
                foreach (Transform child in shell.Root(UiLayer.HudDynamic)
                             .GetComponentsInChildren<Transform>(true))
                    if (child.name.StartsWith("Marker ")) markers++;

                Assert.AreEqual(NebulaHudTargeting.MaxMarkers, markers,
                    "every marker is built once, at build time");
            }
            finally
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
            }
        }

        [Test]
        public void SignalAnalysis_IsReachableFromTheDiscoveryPage()
        {
            // Starting an analysis lived only in NavigationScreen, which is being
            // deleted. The discovery page displayed a running analysis but offered
            // no way to begin one, so the action would have left the game with the
            // screen. This asserts the control exists and refuses with a reason.
            var page = new NebulaDiscoveryPage(null);
            page.Build(Host());
            page.Refresh();

            Assert.AreEqual("OTURUM YOK", page.AnalysisBlockedReason,
                "with no session the analysis action must say why it cannot run");
        }

        [Test]
        public void SelectingNothing_BlocksAnalysisWithItsOwnReason()
        {
            var page = new NebulaDiscoveryPage(null);
            page.Build(Host());
            page.Select(null);
            page.Refresh();

            Assert.IsNotNull(page.AnalysisBlockedReason);
            Assert.AreEqual(NebulaText.Upper(page.AnalysisBlockedReason), page.AnalysisBlockedReason,
                "disabled-reason copy is upper-case Turkish like every other refusal");
        }

        [Test]
        public void EveryAnalysisRefusal_HasTurkishCopy()
        {
            // The director answers in English because it predates the redesign.
            // Every reason it can give must map to something the player can read.
            string[] fromService =
            {
                "REQUIRES SCANNER T1",
                "TARGET MUST BE A SIGNAL",
                "ANALYSIS ALREADY IN PROGRESS",
                "CANNOT START",
            };

            foreach (string reject in fromService)
            {
                string copy = NebulaDiscoveryPage.AnalysisRejectText(reject);
                Assert.IsFalse(string.IsNullOrWhiteSpace(copy), $"{reject} has no Turkish copy");
                Assert.AreNotEqual(reject, copy, $"{reject} was passed through untranslated");
            }
        }

        [Test]
        public void ASelectionThatDoesNotResolve_IsAQuietNoOp()
        {
            var page = new NebulaDiscoveryPage(null);
            page.Build(Host());

            Assert.DoesNotThrow(() => page.Select("no-such-system"));
            Assert.DoesNotThrow(() => page.Refresh());
        }

        [Test]
        public void BothPagesBuildAndRefreshWithoutASession()
        {
            var combat = new NebulaCombatPage(null);
            var discovery = new NebulaDiscoveryPage(null);

            Assert.DoesNotThrow(() => combat.Build(Host()));
            Assert.DoesNotThrow(() => combat.Refresh());
            Assert.DoesNotThrow(() => discovery.Build(Host()));
            Assert.DoesNotThrow(() => discovery.Refresh());

            Assert.AreEqual(0, combat.OccupiedHardpoints);
            Assert.AreEqual(0, discovery.VisibleRows);
        }

        [Test]
        public void RepeatedRefreshesCreateNothing()
        {
            var combat = new NebulaCombatPage(null);
            var discovery = new NebulaDiscoveryPage(null);
            RectTransform host = Host();
            combat.Build(host);
            discovery.Build(host);
            combat.Refresh();
            discovery.Refresh();

            int baseline = host.GetComponentsInChildren<Transform>(true).Length;
            for (int i = 0; i < 300; i++) { combat.Refresh(); discovery.Refresh(); }

            Assert.AreEqual(baseline, host.GetComponentsInChildren<Transform>(true).Length,
                "A refresh rebuilt rows instead of repainting them.");
        }

        [Test]
        public void DiscoveryRowsAreCapped()
        {
            var discovery = new NebulaDiscoveryPage(null);
            discovery.Build(Host());
            discovery.Refresh();
            Assert.LessOrEqual(discovery.VisibleRows, NebulaDiscoveryPage.MaxSystemRows);
        }
    }
}
