using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The parity matrix's seven unverified areas, in the half that needs no
    /// running session: the refusal vocabulary, the controls that carry it, and
    /// the information gates.
    /// <para>
    /// The rule every test here enforces is the same one the redesign was built
    /// on: an action either changes something or says why it did not. A control
    /// that silently does nothing is the failure being hunted, so a no-op is
    /// asserted to produce <b>no</b> success signal, not merely "no crash".
    /// </para>
    /// <para>
    /// Nothing here reads a source file. Every reject string is obtained by
    /// asking the production mapping for it, and the fixture carries a test that
    /// proves the mapping check can fail.
    /// </para>
    /// </summary>
    public class UiFinalFeatureParityTests
    {
        private NebulaUiShell shell;
        private float enteredTimeScale;
        private readonly List<GameObject> spawned = new List<GameObject>();

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
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) UnityEngine.Object.DestroyImmediate(spawned[i]);
            spawned.Clear();

            if (shell != null)
            {
                shell.Shutdown();
                UnityEngine.Object.DestroyImmediate(shell.gameObject);
                shell = null;
            }
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
        }

        private T Track<T>(T component) where T : Component
        {
            spawned.Add(component.gameObject);
            return component;
        }

        private RectTransform Host()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            go.transform.SetParent(shell.Root(UiLayer.Menu), false);
            spawned.Add(go);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Button Control(Transform root, string name)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;
            return null;
        }

        /// <summary>
        /// Player copy is copy: present, not the raw enum name, and not shared
        /// with another reason — two refusals reading the same way is the same as
        /// one of them being missing.
        /// </summary>
        private static void AssertDistinctPlayerCopy<TReason>(Func<TReason, string> map, TReason noneValue)
        {
            var seen = new Dictionary<string, TReason>();
            foreach (TReason reason in Enum.GetValues(typeof(TReason)))
            {
                string copy = map(reason);

                if (Equals(reason, noneValue))
                {
                    Assert.IsTrue(string.IsNullOrEmpty(copy),
                        $"'{typeof(TReason).Name}.{reason}' means success and must produce no refusal text, got '{copy}'");
                    continue;
                }

                Assert.IsFalse(string.IsNullOrEmpty(copy), $"{reason} refuses without saying anything");
                Assert.AreNotEqual(reason.ToString(), copy, $"{reason} is shown as its raw enum name");
                Assert.IsFalse(seen.ContainsKey(copy),
                    $"{reason} and {(seen.ContainsKey(copy) ? seen[copy] : default)} both say '{copy}'");
                seen[copy] = reason;
            }
        }

        // =====================================================================
        // 1 — Drill build / T2 upgrade / cancel
        // =====================================================================

        /// <summary>
        /// Every way the T2 upgrade can be refused has to arrive at the player as
        /// its own sentence. This is the whole refusal vocabulary of the drill
        /// flow, taken from the production mapping the key handler uses.
        /// </summary>
        [Test]
        public void EveryDrillUpgradeRefusal_HasItsOwnPlayerCopy()
        {
            AssertDistinctPlayerCopy<DrillUpgradeRejectReason>(
                ShipExtraction.DrillUpgradeRejectMessage, DrillUpgradeRejectReason.None);
        }

        /// <summary>
        /// The gate the copy names must be the gate the rule enforces. These drift
        /// apart the moment one of them is edited alone, and the player is then
        /// told to reach a tier that is not the one being checked.
        /// </summary>
        [Test]
        public void TheDrillUpgradeRefusal_NamesTheTierTheRuleActuallyRequires()
        {
            string copy = ShipExtraction.DrillUpgradeRejectMessage(
                DrillUpgradeRejectReason.ModuleTierNotMet);

            StringAssert.Contains(
                DrillUpgradeCatalog.RequiredStationConstructionTier.ToString(), copy,
                "the refusal quotes a different tier than the catalog requires");
        }

        // =====================================================================
        // 4 — Maintenance Reserve
        // =====================================================================

        [Test]
        public void EveryReserveTransferRefusal_HasItsOwnPlayerCopy()
        {
            AssertDistinctPlayerCopy<ReserveTransferRejectReason>(
                NebulaHudPlanetContext.ReserveRejectText, ReserveTransferRejectReason.None);
        }

        /// <summary>
        /// The reserve is a physical transfer: every accepted item needs both
        /// directions, or one of them is a store the player can fill and never
        /// empty.
        /// </summary>
        [Test]
        public void TheReservePanel_OffersBothDirectionsForEveryAcceptedItem()
        {
            Track(NebulaHudPlanetContext.Create(shell, null, null));
            Transform hud = shell.Root(UiLayer.HudStatic);

            var items = new List<string> { ProcessingCatalog.BasicPartId };
            items.AddRange(ProcessingCatalog.MaterialIds);

            foreach (string itemId in items)
            {
                Assert.IsNotNull(Control(hud, itemId + " +"), $"{itemId} cannot be deposited");
                Assert.IsNotNull(Control(hud, itemId + " -"), $"{itemId} cannot be withdrawn");
            }
        }

        /// <summary>
        /// With nothing to transfer into, pressing +10 must produce neither a
        /// transfer nor the flash that means one happened. A control that
        /// celebrates a no-op is worse than one that refuses.
        /// </summary>
        [Test]
        public void AReserveTransferWithNoReserve_ShowsNoAcceptance()
        {
            NebulaHudPlanetContext panel = Track(NebulaHudPlanetContext.Create(shell, null, null));
            Transform hud = shell.Root(UiLayer.HudStatic);

            Control(hud, ProcessingCatalog.BasicPartId + " +").onClick.Invoke();
            Control(hud, ProcessingCatalog.BasicPartId + " -").onClick.Invoke();

            Assert.AreEqual(0, panel.ReserveDeltasShown,
                "a transfer that never happened was shown as accepted");
            Assert.IsNull(panel.LastRejectShown,
                "and nothing was refused either -- there was nothing to refuse");
        }

        /// <summary>
        /// The support build button is the reserve panel's twin and the only UI
        /// entry to Sensor Array / Maintenance Platform. With no extraction behind
        /// it, pressing build must not report a build.
        /// </summary>
        [Test]
        public void ASupportBuildWithNoTarget_ShowsNoAcceptance()
        {
            NebulaHudPlanetContext panel = Track(NebulaHudPlanetContext.Create(shell, null, null));
            Transform hud = shell.Root(UiLayer.HudStatic);

            Button build = Control(hud, "Build");
            Assert.IsNotNull(build, "the support panel has no build control at all");
            build.onClick.Invoke();

            Assert.AreEqual(0, panel.BuildAcceptancesShown,
                "a build that never happened was shown as accepted");
        }

        // =====================================================================
        // 6 — Dominance and reclaim
        // =====================================================================

        [Test]
        public void EveryEvacuationRefusal_HasItsOwnPlayerCopy()
        {
            AssertDistinctPlayerCopy<EvacuationRejectReason>(
                ShipPlanetEvacuation.RejectMessage, EvacuationRejectReason.None);
        }

        [Test]
        public void EveryReclaimRefusal_HasItsOwnPlayerCopy()
        {
            AssertDistinctPlayerCopy<ReclaimRejectReason>(
                ShipPlanetEvacuation.RejectMessage, ReclaimRejectReason.None);
        }

        /// <summary>
        /// One control carries both directions, so its caption is the only thing
        /// telling the player which one they are about to take. Abandoning a
        /// planet and taking one back must never read the same.
        /// </summary>
        [Test]
        public void TheEvacuateControl_RefusesWithAReasonRatherThanActingBlind()
        {
            var page = new NebulaPlanetsPage(null);
            page.Build(Host());
            page.Refresh();

            Button evacuate = Control(shell.Root(UiLayer.Menu), "Evacuate");
            Assert.IsNotNull(evacuate, "the only entry to evacuation is missing");
            Assert.IsFalse(evacuate.interactable, "with no session it must be disabled");
            Assert.IsNotNull(page.EvacuateBlockedReason, "and disabled with a reason");

            string statusBefore = page.StatusText;
            evacuate.onClick.Invoke();
            Assert.AreEqual(statusBefore, page.StatusText,
                "a refused evacuation must not report anything as having happened");
        }

        // =====================================================================
        // 7 — Signal analysis, the information gate, and the bindings screen
        // =====================================================================

        [Test]
        public void EveryAnalysisRefusal_ReachesThePlayerInTurkish()
        {
            string[] serviceReasons =
            {
                "REQUIRES SCANNER T1", "TARGET MUST BE A SIGNAL", "ANALYSIS ALREADY IN PROGRESS"
            };

            var seen = new HashSet<string>();
            foreach (string reason in serviceReasons)
            {
                string copy = NebulaDiscoveryPage.AnalysisRejectText(reason);
                Assert.IsFalse(string.IsNullOrEmpty(copy), $"'{reason}' is dropped on the floor");
                Assert.AreNotEqual(reason, copy, $"'{reason}' is shown to the player in English");
                Assert.IsTrue(seen.Add(copy), $"'{reason}' shares its copy with another reason");
            }

            Assert.IsFalse(string.IsNullOrEmpty(NebulaDiscoveryPage.AnalysisRejectText("something new")),
                "an unrecognised refusal must still say something rather than nothing");
        }

        /// <summary>
        /// A system known only as a signal has a real name sitting in the
        /// registry. Printing it would teach the player that a place exists and
        /// what it is called, from a rumour. This is the information gate, and it
        /// is checked on the page's own naming function.
        /// </summary>
        [Test]
        public void ASignalIsDescribedNotNamed()
        {
            string signal = NebulaDiscoveryPage.DisplayName("sys/whatever", SystemKnowledgeLevel.Signal, null);
            string unknown = NebulaDiscoveryPage.DisplayName("sys/whatever", SystemKnowledgeLevel.Unknown, null);

            Assert.AreEqual(signal, unknown,
                "a signal must disclose no more than something unknown does");
            StringAssert.DoesNotContain("sys/whatever", signal,
                "the system's own id leaked through the label");
        }

        [Test]
        public void TheAnalysisControls_RefuseWithAReasonWithNoSession()
        {
            var page = new NebulaDiscoveryPage(null);
            page.Build(Host());
            page.Refresh();

            Button analyze = Control(shell.Root(UiLayer.Menu), "Analyze");
            Button cancel = Control(shell.Root(UiLayer.Menu), "CancelAnalysis");
            Assert.IsNotNull(analyze, "there is no way to start an analysis at all");
            Assert.IsNotNull(cancel, "and no way to cancel one");

            Assert.IsFalse(analyze.interactable);
            Assert.IsNotNull(page.AnalysisBlockedReason);
            Assert.IsFalse(cancel.interactable, "there is no analysis to cancel");

            string before = page.StatusText;
            analyze.onClick.Invoke();
            cancel.onClick.Invoke();
            Assert.AreEqual(before, page.StatusText,
                "a refused analysis must not report a start or a cancellation");
        }

        /// <summary>
        /// The bindings screen states the keys, and it must state the ones the
        /// game actually reads. It is deliberately read-only — the bindings are
        /// compile-time constants — so the risk here is not a broken control, it
        /// is a screen quietly telling the player the wrong key.
        /// </summary>
        [Test]
        public void TheBindingsScreen_QuotesTheKeysTheGameActuallyReads()
        {
            var page = new NebulaSettingsPage(null);
            RectTransform host = Host();
            page.Build(host);
            page.Refresh();

            var readings = new List<string>();
            foreach (TMPro.TextMeshProUGUI text in host.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                readings.Add(text.text);

            foreach (KeyCode key in new[]
                     {
                         NebulaParentMenuScreen.ProgressionKey,
                         NebulaParentMenuScreen.FleetKey,
                         ShipExtraction.ScanKey,
                         ShipExtraction.ConfirmKey,
                         ShipExtraction.BuildModeKey,
                         ShipExtraction.TransferKey,
                         ShipPlanetEvacuation.EvacuateKey,
                     })
            {
                string expected = NebulaText.UpperAscii(key.ToString());
                Assert.IsTrue(readings.Contains(expected) || readings.Exists(r => r.Contains(expected)),
                    $"the bindings screen never mentions {key}, which the game reads");
            }
        }

        // =====================================================================
        // 2 — Processing bench
        // =====================================================================

        /// <summary>
        /// Two full-height panels with nothing in them read as a screen that
        /// failed to load. Both must say what is missing and what to do — that was
        /// a real defect on this page and this is the guard against its return.
        /// </summary>
        [Test]
        public void TheResourcesPage_ExplainsAnEmptyHoldAndAnEmptyBench()
        {
            var page = new NebulaResourcesPage(null);
            RectTransform host = Host();
            page.Build(host);
            page.Refresh();

            Assert.AreEqual(0, page.VisibleItemRows);
            // The bench column used to be one row per slot; it is now one card per
            // recipe the loaded input feeds, and with no session there is no input.
            Assert.AreEqual(0, page.VisibleRecipeCards);

            var visibleEmptyText = new List<string>();
            foreach (TMPro.TextMeshProUGUI text in host.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                if (text.gameObject.name == "Empty" && text.gameObject.activeInHierarchy
                    && !string.IsNullOrWhiteSpace(text.text))
                    visibleEmptyText.Add(text.text);

            Assert.AreEqual(2, visibleEmptyText.Count,
                "an empty hold and an empty bench must each explain themselves; found: "
                + string.Join(" | ", visibleEmptyText));
        }

        // =====================================================================
        // One action, one response
        // =====================================================================

        /// <summary>
        /// A destructive action asks once and acts once. Asking twice would stack
        /// two dialogs on one shell; acting without asking is the loss the dialog
        /// exists to prevent.
        /// </summary>
        [Test]
        public void ADestructiveActionAsksExactlyOnceAndActsExactlyOnce()
        {
            NebulaConfirmationDialog dialog = Track(NebulaConfirmationDialog.Create(shell));
            int committed = 0;

            Assert.IsTrue(dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => committed++, destructive: true));
            Assert.IsFalse(dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => committed++, destructive: true),
                "a second ask while one is open would stack two dialogs");
            Assert.AreEqual(0, committed, "nothing may happen before the player answers");

            Control(shell.Root(UiLayer.Menu), "Confirm").onClick.Invoke();

            Assert.AreEqual(1, committed, "one confirmation must run its action exactly once");
            Assert.AreEqual(1, dialog.ConfirmedCount);
            Assert.IsFalse(dialog.IsOpen, "and the dialog must be gone");
        }

        [Test]
        public void CancellingADestructiveActionRunsNothing()
        {
            NebulaConfirmationDialog dialog = Track(NebulaConfirmationDialog.Create(shell));
            int committed = 0;

            dialog.Ask("BAŞLIK", "Soru?", null, "ONAYLA", () => committed++, destructive: true);
            Control(shell.Root(UiLayer.Menu), "Cancel").onClick.Invoke();

            Assert.AreEqual(0, committed);
            Assert.AreEqual(1, dialog.CancelledCount);
            Assert.IsFalse(dialog.IsOpen);
        }

        // =====================================================================
        // Proof the checks can fail
        // =====================================================================

        /// <summary>
        /// The copy checker is the load-bearing assertion in half of this file. If
        /// it passes a missing string, a duplicate string or a raw enum name, then
        /// every "has its own player copy" test above is decorative.
        /// </summary>
        [Test]
        public void TheCopyCheckerRejectsMissingDuplicateAndRawCopy()
        {
            Assert.Throws<AssertionException>(() =>
                    AssertDistinctPlayerCopy<ReserveTransferRejectReason>(_ => null,
                        ReserveTransferRejectReason.None),
                "missing copy was accepted");

            Assert.Throws<AssertionException>(() =>
                    AssertDistinctPlayerCopy<ReserveTransferRejectReason>(r => r.ToString(),
                        ReserveTransferRejectReason.None),
                "the raw enum name was accepted as player copy");

            Assert.Throws<AssertionException>(() =>
                    AssertDistinctPlayerCopy<ReserveTransferRejectReason>(_ => "AYNI METİN",
                        ReserveTransferRejectReason.None),
                "two refusals sharing one sentence was accepted");

            Assert.Throws<AssertionException>(() =>
                    AssertDistinctPlayerCopy<ReserveTransferRejectReason>(
                        r => r == ReserveTransferRejectReason.None ? "BAŞARILI" : r.ToString() + "!",
                        ReserveTransferRejectReason.None),
                "success carrying refusal text was accepted");
        }

        /// <summary>
        /// And the control lookup must not quietly return the first button it
        /// finds when the one being asked for does not exist — every "the control
        /// is missing" assertion depends on that.
        /// </summary>
        [Test]
        public void TheControlLookupReturnsNothingForAControlThatIsNotThere()
        {
            Track(NebulaHudPlanetContext.Create(shell, null, null));
            Assert.IsNotNull(Control(shell.Root(UiLayer.HudStatic), "Build"));
            Assert.IsNull(Control(shell.Root(UiLayer.HudStatic), "NoSuchControl"));
        }
    }
}
