using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Hud;

namespace Nebula.Tests.EditMode
{
    public sealed class AuditPart3A3RegressionTests
    {
        private string settingsDirectory;

        [SetUp]
        public void SetUp()
        {
            settingsDirectory = Path.Combine(Path.GetTempPath(), "nebula-part3-a3-" + System.Guid.NewGuid().ToString("N"));
            NebulaInputBindingStore.DirectoryOverride = settingsDirectory;
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
            NebulaGameplayFeedback.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
            NebulaInputBindingStore.DirectoryOverride = null;
            if (Directory.Exists(settingsDirectory)) Directory.Delete(settingsDirectory, true);
        }

        [Test]
        public void ContextualBinding_IsLiveAndPersistsBySemanticActionId()
        {
            Assert.IsTrue(NebulaInputActions.TrySetBinding(NebulaInputActions.ExtractionConfirm, KeyCode.Alpha9));
            Assert.AreEqual(KeyCode.Alpha9, NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm));
            Assert.IsTrue(NebulaInputBindingStore.Save());

            NebulaInputActions.ResetRebindableToDefaults();
            Assert.AreEqual(KeyCode.F, NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm));
            Assert.Greater(NebulaInputBindingStore.Load(), 0);
            Assert.AreEqual(KeyCode.Alpha9, NebulaInputActions.Key(NebulaInputActions.ExtractionConfirm));
        }

        [Test]
        public void SharedF_GoesToTheMoreSpecificLivePoiContext_AndNeverDoubleFires()
        {
            NebulaInputRouter.SetContextSource(InputContext.Extraction, () => true);
            NebulaInputRouter.SetContextSource(InputContext.Poi, () => true);
            NebulaInputRouter.Source = key => key == KeyCode.F;

            InputActionDefinition owner = NebulaInputRouter.OwnerOf(KeyCode.F);
            Assert.IsNotNull(owner);
            Assert.AreEqual(NebulaInputActions.PoiStart, owner.Id);
            Assert.IsTrue(NebulaInputRouter.Pressed(NebulaInputActions.PoiStart));
            Assert.IsFalse(NebulaInputRouter.Pressed(NebulaInputActions.ExtractionConfirm));
        }

        /// <summary>
        /// The F tie the ownership audit found: POI and the logistics centre both
        /// claimed F at rank 20, <c>OwnerOf</c> refused to guess, and with a POI and a
        /// centre both in range F did <b>nothing at all</b>. No double-fire, but a
        /// silent no-op is not a resolution — it is the same gap wearing a different
        /// face. POI is the narrower claim, so it takes the press.
        /// </summary>
        [Test]
        public void SharedF_ResolvesTheLogisticsPoiTie_InsteadOfDoingNothing()
        {
            NebulaInputRouter.SetContextSource(InputContext.Logistics, () => true);
            NebulaInputRouter.SetContextSource(InputContext.Poi, () => true);
            NebulaInputRouter.Source = key => key == KeyCode.F;

            InputActionDefinition owner = NebulaInputRouter.OwnerOf(KeyCode.F);
            Assert.IsNotNull(owner, "F still resolves to nobody when a POI and a logistics centre are both in range");
            Assert.AreEqual(NebulaInputActions.PoiStart, owner.Id);

            Assert.IsTrue(NebulaInputRouter.Pressed(NebulaInputActions.PoiStart));
            Assert.IsFalse(NebulaInputRouter.Pressed(NebulaInputActions.LogisticsBuild));
        }

        /// <summary>
        /// The C collision, end to end.
        /// <para>
        /// Down-thrust reads C every frame the ship is flown and reclaim reads it too;
        /// both used to sample <c>Input</c> for themselves, so one press at an
        /// abandoned planet dropped the nose <b>and</b> reclaimed the world. The fix
        /// has to satisfy three things at once and this asserts all three: where
        /// reclaim exists it gets the press and flight does not; where it does not,
        /// flight keeps C; and neither case is a tie that resolves to nobody.
        /// </para>
        /// </summary>
        [Test]
        public void SharedC_GoesToReclaimOnlyWhereReclaimExists_AndNeverToBoth()
        {
            const string VerticalDown = "flight.vertical_down";
            NebulaInputRouter.SetContextSource(InputContext.Flight, () => true);
            NebulaInputRouter.Source = key => key == KeyCode.C;
            NebulaInputRouter.HeldSource = key => key == KeyCode.C;

            // 1. Flying, no abandoned planet under the ship: C is down-thrust, whole.
            NebulaInputRouter.SetContextSource(InputContext.Evacuation, () => false);
            InputActionDefinition flightOwner = NebulaInputRouter.OwnerOf(KeyCode.C);
            Assert.IsNotNull(flightOwner, "C resolves to nobody while simply flying");
            Assert.AreEqual(VerticalDown, flightOwner.Id);
            Assert.IsTrue(NebulaInputRouter.Held(VerticalDown));
            Assert.IsFalse(NebulaInputRouter.Pressed(NebulaInputActions.EvacuationReclaim));

            // 2. At a planet the ship has abandoned: reclaim takes it, and down-thrust
            // is told no on the same frame -- one press, one action.
            NebulaInputRouter.SetContextSource(InputContext.Evacuation, () => true);
            InputActionDefinition reclaimOwner = NebulaInputRouter.OwnerOf(KeyCode.C);
            Assert.IsNotNull(reclaimOwner, "C resolves to nobody where reclaim is live — the tie is back");
            Assert.AreEqual(NebulaInputActions.EvacuationReclaim, reclaimOwner.Id);
            Assert.IsTrue(NebulaInputRouter.Pressed(NebulaInputActions.EvacuationReclaim));
            Assert.IsFalse(NebulaInputRouter.Held(VerticalDown),
                "one press of C produced both a reclaim and a down-thrust");
        }

        /// <summary>
        /// The rule behind both fixes above, stated once: no two arbitrated rows that
        /// share a key and can be listening on the same frame may carry the same rank.
        /// <para>
        /// An equal rank is exactly what makes <c>OwnerOf</c> hand a key to nobody, and
        /// that silent no-op is what F was doing with a POI and a logistics centre both
        /// in range. Scanning every shared key rather than re-listing today's pairs is
        /// the point: the next row added on an already-claimed key fails here instead of
        /// shipping as a key that quietly stopped working in one particular situation.
        /// </para>
        /// <para>
        /// Restricted to arbitrated rows on purpose. Rank only decides anything inside
        /// <c>OwnerOf</c>, which considers nothing else, so two unarbitrated contexts
        /// sharing a number is not a defect — <c>Collisions()</c> is what covers those,
        /// and it is asserted separately.
        /// </para>
        /// </summary>
        [Test]
        public void NoTwoArbitratedClaimantsOfOneKey_ShareARank()
        {
            IReadOnlyList<InputActionDefinition> catalog = NebulaInputActions.All;
            for (int i = 0; i < catalog.Count; i++)
            {
                InputActionDefinition a = catalog[i];
                if (!a.Arbitrated || a.Key == KeyCode.None) continue;

                for (int j = i + 1; j < catalog.Count; j++)
                {
                    InputActionDefinition b = catalog[j];
                    if (!b.Arbitrated || b.Key != a.Key) continue;
                    if (!NebulaInputActions.CanCoOccur(a.Context, b.Context)) continue;

                    Assert.AreNotEqual(
                        NebulaInputActions.Rank(a.Context), NebulaInputActions.Rank(b.Context),
                        $"{a.Key}: {a.Id} [{a.Context}] and {b.Id} [{b.Context}] can both be listening "
                        + "and rank equally — OwnerOf would hand the press to nobody");
                }
            }
        }

        /// <summary>
        /// Heading lock answers its catalog row, not a literal H.
        /// <para>
        /// <c>ShipCruise.HandleHeadingLockInput</c> read <c>Input.GetKeyDown(KeyCode.H)</c>
        /// while the catalog called that row "Seyir Kipi" — so the settings screen named
        /// a cruise toggle that does not exist, and Yön Sabitle had no name and no
        /// rebind anywhere. The row is now named for what it does and a rebind moves the
        /// real read.
        /// </para>
        /// </summary>
        [Test]
        public void HeadingLock_IsARebindableRowNamedForWhatItDoes()
        {
            InputActionDefinition row = NebulaInputActions.Find(NebulaInputActions.FlightHeadingLock);
            Assert.IsNotNull(row, "heading lock has no catalog row");
            Assert.AreEqual(KeyCode.H, row.DefaultKey);
            Assert.IsTrue(row.Rebindable, "heading lock cannot be reassigned");
            StringAssert.DoesNotContain("Seyir", row.LabelTr);

            NebulaInputRouter.SetContextSource(InputContext.Flight, () => true);
            Assert.IsTrue(NebulaInputActions.TrySetBinding(NebulaInputActions.FlightHeadingLock, KeyCode.Alpha9));

            NebulaInputRouter.Source = key => key == KeyCode.Alpha9;
            Assert.IsTrue(NebulaInputRouter.Pressed(NebulaInputActions.FlightHeadingLock),
                "the rebind was accepted but the runtime read still answers the old key");

            NebulaInputRouter.Source = key => key == KeyCode.H;
            Assert.IsFalse(NebulaInputRouter.Pressed(NebulaInputActions.FlightHeadingLock),
                "H still fires heading lock after being rebound away");
        }

        /// <summary>
        /// Every action this slice routed is genuinely rebindable and genuinely
        /// persisted — the pair of failures P1-ECO-006 was.
        /// </summary>
        [Test]
        public void EveryNewlyRoutedAction_IsRebindableAndSurvivesARestart()
        {
            string[] routed =
            {
                NebulaInputActions.ProcessingPanel,
                NebulaInputActions.ExtractionPrevDefenseNode,
                NebulaInputActions.ExtractionNextDefenseNode,
                NebulaInputActions.QuickPackageConfirm,
                NebulaInputActions.QuickPackageCycleMaterial,
                NebulaInputActions.FlightHeadingLock,
            };

            foreach (string id in routed)
            {
                InputActionDefinition action = NebulaInputActions.Find(id);
                Assert.IsNotNull(action, id + " is not in the catalog");
                Assert.IsTrue(action.Rebindable, id + " still cannot be reassigned");
                Assert.IsTrue(action.Arbitrated, id + " is rebindable but its runtime read is not routed");
                Assert.IsTrue(action.Persisted, id + " is never written to the settings store");
            }

            Assert.IsTrue(NebulaInputActions.TrySetBinding(NebulaInputActions.ProcessingPanel, KeyCode.Alpha9));
            Assert.IsTrue(NebulaInputBindingStore.Save());

            NebulaInputActions.ResetRebindableToDefaults();
            Assert.AreEqual(Extraction.ShipProcessing.PanelKey,
                NebulaInputActions.Key(NebulaInputActions.ProcessingPanel));

            Assert.Greater(NebulaInputBindingStore.Load(), 0);
            Assert.AreEqual(KeyCode.Alpha9, NebulaInputActions.Key(NebulaInputActions.ProcessingPanel),
                "the rebind did not survive a restart");
        }

        // =====================================================================
        // Reachability — a widget with no action id is a picture of a key
        // =====================================================================

        /// <summary>The state the bar is in at a working drill: scanned planet, active drill, something to take.</summary>
        private static ExtractionContextState WorkingDrill() => new ExtractionContextState
        {
            HasTargetPlanet = true,
            PlanetDisplayName = "TEST",
            Scanned = true,
            HasDrill = true,
            DrillActive = true,
            CanTransfer = true,
            StationConstructionGateMet = true,
        };

        /// <summary>
        /// Aktarım is on the bar, and it is a control.
        /// <para>
        /// Two defects in one row. It carried no <c>actionId</c>, so the widget was a
        /// caption and a key prompt with a dead button under it; and it was added third
        /// in a list the bar caps at two, so it was trimmed on literally every frame it
        /// applied — the one action in the moment-to-moment extraction loop was the one
        /// action the bar could never show.
        /// </para>
        /// </summary>
        [Test]
        public void Transfer_SurvivesTheBarsCap_AndCarriesItsActionId()
        {
            var secondaries = new List<HudAction>();
            NebulaExtractionContext.Build(WorkingDrill(), null, secondaries, out _);

            Assert.LessOrEqual(secondaries.Count, NebulaHudContextBar.MaxSecondaryActions);

            HudAction transfer = default;
            foreach (HudAction action in secondaries)
                if (action.ActionId == NebulaInputActions.ExtractionTransfer) transfer = action;

            Assert.IsTrue(transfer.IsValid,
                "the transfer action was trimmed by the cap — it is offered after two actions that outrank it");
            Assert.AreEqual(NebulaInputActions.Key(NebulaInputActions.ExtractionTransfer), transfer.Key,
                "the widget prints a key the action no longer answers to");
        }

        /// <summary>Tarama likewise: the pre-scan primary was a caption with no seam behind it.</summary>
        [Test]
        public void Scan_IsTheUnscannedPrimary_AndCarriesItsActionId()
        {
            var secondaries = new List<HudAction>();
            var state = new ExtractionContextState { HasTargetPlanet = true, PlanetDisplayName = "TEST" };

            HudAction primary = NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.IsTrue(primary.IsValid, "an unscanned planet offers no primary action at all");
            Assert.AreEqual(NebulaInputActions.ExtractionScan, primary.ActionId,
                "the scan widget carries no semantic action id, so its button can never be interactable");
        }

        /// <summary>
        /// The general rule rather than two examples of it: an action the bar says is
        /// available must be clickable, and <c>NebulaHudContextBar.Apply</c> only makes
        /// a widget interactable when it carries an action id. An available action
        /// without one is a control that looks alive and does nothing.
        /// </summary>
        [Test]
        public void EveryAvailableExtractionAction_CarriesAnActionId()
        {
            var secondaries = new List<HudAction>();
            HudAction primary = NebulaExtractionContext.Build(WorkingDrill(), null, secondaries, out _);

            var offered = new List<HudAction>(secondaries);
            if (primary.IsValid) offered.Add(primary);

            foreach (HudAction action in offered)
            {
                if (!action.Available) continue; // a blocked action is allowed to be a statement
                Assert.IsFalse(string.IsNullOrEmpty(action.ActionId),
                    $"\"{action.Caption}\" is offered as available but carries no action id — "
                    + "its button cannot become interactable");
            }
        }

        /// <summary>
        /// Transfer refused four ways with a bare <c>return</c> each. Every one of them
        /// now has a sentence, in Turkish, that is not the enum's own name.
        /// </summary>
        [Test]
        public void TransferRejectTable_HasTurkishTextForEveryReject()
        {
            foreach (ShipExtraction.TransferRejectReason reason
                     in System.Enum.GetValues(typeof(ShipExtraction.TransferRejectReason)))
            {
                if (reason == ShipExtraction.TransferRejectReason.None) continue;
                string text = ShipExtraction.TransferRejectText(reason);
                Assert.IsNotEmpty(text, reason.ToString());
                Assert.AreNotEqual(reason.ToString(), text,
                    "the player is being shown the enum's own name");
            }
        }

        /// <summary>
        /// No producer hands the feedback channel an English sentence.
        /// <para>
        /// This test used to prove the opposite thing. <c>Turkish()</c> was a 33-entry
        /// table inside the channel that rewrote English strings on their way to the
        /// player, and this test pinned two of its entries — so it was asserting that
        /// the second authority over player-facing text still worked. Part 4 deleted
        /// that table (P1-ECO-018): a translation layer downstream of the producer means
        /// fixing the producer is not enough, and a phrase the table has never been
        /// taught reaches the player in English anyway.
        /// </para>
        /// <para>
        /// The invariant that survives is the one the old name was reaching for, so it
        /// is what is measured now: every string a runtime file publishes is already
        /// Turkish where it is written. Read from source rather than by calling, because
        /// the failure is a literal somebody typed, and no reachability argument is
        /// needed to see it.
        /// </para>
        /// </summary>
        [Test]
        public void NoRuntimeFilePublishesAnEnglishSentenceToThePlayer()
        {
            string root = System.IO.Path.Combine(Application.dataPath, "Scripts", "Nebula");
            var publishCall = new Regex(@"NebulaGameplayFeedback\.Publish\s*\(\s*[^;]*?""([^""]{4,})""",
                RegexOptions.Singleline);
            var offenders = new List<string>();

            foreach (string file in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string source = System.IO.File.ReadAllText(file);
                foreach (Match match in publishCall.Matches(source))
                {
                    string literal = match.Groups[1].Value;
                    foreach (string word in literal.ToUpperInvariant().Split(
                        new[] { ' ', '.', ',', ':', ';', '!', '?', '(', ')', '-', '/' },
                        System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (EnglishGiveaways.Contains(word))
                            offenders.Add($"{System.IO.Path.GetFileName(file)}: \"{literal}\" ({word})");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "these publish an English sentence straight to the player:\n  "
                + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The words below have no Turkish homograph, so a whole-word hit is English
        /// and never a false alarm. Kept deliberately short for that reason.
        /// </summary>
        private static readonly HashSet<string> EnglishGiveaways = new HashSet<string>
        {
            "INSUFFICIENT", "RESOURCE", "RESOURCES", "REQUIRED", "REQUIRES", "AVAILABLE",
            "UNAVAILABLE", "MISSING", "INVALID", "FAILED", "FAILURE", "CANNOT", "DENIED",
            "REJECTED", "BUSY", "EMPTY", "FULL", "NEED", "NEEDS", "ENOUGH", "ALREADY",
            "WITHOUT", "SELECTED", "TARGET", "REASON", "UNKNOWN", "NONE", "BASIC",
            "BUILDING", "PARTS",
        };

        [Test]
        public void RepairRejectTable_HasTurkishTextForEveryReject()
        {
            foreach (RepairRejectReason reason in System.Enum.GetValues(typeof(RepairRejectReason)))
            {
                if (reason == RepairRejectReason.None) continue;
                Assert.IsNotEmpty(NebulaGameplayFeedback.RepairText(reason), reason.ToString());
            }
        }

        [Test]
        public void Part3Runner_RequiresVerifiedHashStableSerialExecution()
        {
            string runner = File.ReadAllText(Path.Combine(Application.dataPath, "..", "UIDesign", "runscoped.sh"));
            StringAssert.Contains("typecheck.sh\" --all", runner);
            StringAssert.Contains("NOT VERIFIED : (none)", runner);
            StringAssert.Contains("source_hash", runner);
            StringAssert.Contains("RUN INVALID -- assembly sources changed", runner);
            StringAssert.Contains("NEBULA_UNITY_TEST_TIMEOUT_SECONDS:-900", runner);
        }
    }
}
