using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Hud;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-04: the context bar and the extraction context that replaces the old
    /// bottom-centre text wall. These are the tests that hold the wall down.
    /// </summary>
    public class NebulaHudContextTests
    {
        private readonly List<HudAction> secondaries = new List<HudAction>();

        private static ExtractionContextState ScannedPlanet()
        {
            return new ExtractionContextState
            {
                HasTargetPlanet = true,
                PlanetDisplayName = "VEGA-7-II",
                Altitude = 320f,
                Scanned = true,
                FamilyName = "Metal",
                RarityName = "Rare",
                EnvironmentTag = "Sakin",
                KitConsumed = true,
                StationConstructionGateMet = true,
                UpgradePartsCost = 30,
                UpgradeMaterialCost = 40,
                UpgradeMaterialName = "Metal Alaşım",
            };
        }

        // =====================================================================
        // THE INFORMATION GATE
        // =====================================================================

        // ---- the driver ------------------------------------------------------

        /// <summary>
        /// The bar had a Show and nothing that called it. This is the wire, so what
        /// is worth asserting is that it runs at all and that it clears the bar
        /// rather than leaving the last frame's actions up when nothing applies.
        /// </summary>
        [Test]
        public void TheDriver_HidesTheBarWhenThereIsNoTargetPlanet()
        {
            var shell = NebulaUiShell.Create();
            try
            {
                NebulaHudContextBar bar = NebulaHudContextBar.Create(shell);
                bar.Show("BAĞLAM", new HudAction("BİRİNCİL", KeyCode.F));
                Assert.AreEqual(1, bar.VisibleActionCount);

                NebulaExtractionContextDriver driver =
                    NebulaExtractionContextDriver.Create(bar, null, null);
                driver.Refresh();

                Assert.AreEqual(1, driver.RefreshesHidden);
                Assert.AreEqual(0, driver.RefreshesShown);
                Assert.AreEqual(0, bar.VisibleActionCount,
                    "no target planet means the bar says nothing, not the last thing it said");
            }
            finally
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
            }
        }

        // ---- evacuation, and the wire that was never connected ----------------

        /// <summary>
        /// Abandoning a planet is irreversible and takes two presses. ExtractionHud
        /// showed the prompt, the armed window and the refusal; the redesign showed
        /// none of it, so for a while the ship could arm itself for an irreversible
        /// action with nothing on screen saying so.
        /// </summary>
        [Test]
        public void AnArmedEvacuation_SaysSoAndSaysItIsIrreversible()
        {
            var secondaries = new List<HudAction>();
            ExtractionContextState state = ScannedPlanet();
            state.EvacuationArmed = true;

            NebulaExtractionContext.Build(state, null, secondaries, out _);

            HudAction evacuate = FindByKey(secondaries, ShipPlanetEvacuation.EvacuateKey);
            Assert.IsTrue(evacuate.IsValid, "the evacuate action must be offered");
            StringAssert.Contains("TEKRAR BAS", evacuate.Caption,
                "an armed confirm must say that a second press is what commits it");
            StringAssert.Contains("GERİ ALINAMAZ", evacuate.Cost ?? "",
                "and that it cannot be undone");
        }

        /// <summary>
        /// The bar shows one primary and two secondary, and that cap is the whole
        /// reason the text wall went. An idle "you could evacuate this planet"
        /// affordance is not urgent enough to spend a slot on, so it belongs on the
        /// Gezegenler page; only an evacuation that is actually happening takes one.
        /// </summary>
        [Test]
        public void AnIdleEvacuationOffer_DoesNotSpendAHudSlot()
        {
            var secondaries = new List<HudAction>();
            NebulaExtractionContext.Build(ScannedPlanet(), null, secondaries, out _);

            Assert.IsFalse(FindByKey(secondaries, ShipPlanetEvacuation.EvacuateKey).IsValid,
                "an idle offer stays off the HUD");
        }

        /// <summary>
        /// And when it does take one it takes the first, so the cap can never be
        /// what drops an irreversible action.
        /// </summary>
        [Test]
        public void AnArmedEvacuation_IsNeverTheActionThatGetsDropped()
        {
            var secondaries = new List<HudAction>();
            ExtractionContextState state = ScannedPlanet();
            state.HasDrill = true;
            state.DrillActive = true;
            state.CanTransfer = true;   // fills both ordinary slots
            state.EvacuationArmed = true;

            NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.LessOrEqual(secondaries.Count, NebulaHudContextBar.MaxSecondaryActions);
            Assert.AreEqual(ShipPlanetEvacuation.EvacuateKey, secondaries[0].Key,
                "the armed confirm comes first, ahead of stabilize and transfer");
        }

        [Test]
        public void AnAbandonedPlanet_OffersReclaimInsteadOfEvacuate()
        {
            var secondaries = new List<HudAction>();
            ExtractionContextState state = ScannedPlanet();
            state.PlanetAbandoned = true;

            NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.IsTrue(FindByKey(secondaries, ShipPlanetEvacuation.ReclaimKey).IsValid,
                "an abandoned planet is reclaimed, not evacuated again");
            Assert.IsFalse(FindByKey(secondaries, ShipPlanetEvacuation.EvacuateKey).IsValid);
        }

        [Test]
        public void APendingEvacuation_IsAStatusAndCannotBePressed()
        {
            var secondaries = new List<HudAction>();
            ExtractionContextState state = ScannedPlanet();
            state.EvacuationPending = true;
            state.EvacuationPendingVehicles = 3;

            NebulaExtractionContext.Build(state, null, secondaries, out _);

            HudAction pending = FindByKey(secondaries, ShipPlanetEvacuation.EvacuateKey);
            Assert.IsTrue(pending.IsValid);
            Assert.IsFalse(pending.Available, "there is nothing to press while it runs");
            StringAssert.Contains("3", pending.Cost ?? "", "it says how many are still out");
        }

        [Test]
        public void ARefusedEvacuation_CarriesTheServicesOwnReason()
        {
            var secondaries = new List<HudAction>();
            ExtractionContextState state = ScannedPlanet();
            // Abandoned, so reclaim is the offer -- and the offer is on screen,
            // which is the only situation where a refusal has anywhere to appear.
            state.PlanetAbandoned = true;
            state.EvacuationBlockedReason = "TEST SEBEBİ";

            NebulaExtractionContext.Build(state, null, secondaries, out _);

            HudAction reclaim = FindByKey(secondaries, ShipPlanetEvacuation.ReclaimKey);
            Assert.IsFalse(reclaim.Available);
            Assert.AreEqual("TEST SEBEBİ", reclaim.BlockedReason,
                "the refusal shown is the service's, never the UI's guess");
        }

        private static HudAction FindByKey(List<HudAction> actions, KeyCode key)
        {
            for (int i = 0; i < actions.Count; i++)
                if (actions[i].IsValid && actions[i].Key == key) return actions[i];
            return default;
        }

        [Test]
        public void BeforeAScan_NothingAboutThePlanetIsRevealed()
        {
            var state = ScannedPlanet();
            state.Scanned = false;
            state.ScanProgress01 = 0.4f;

            NebulaExtractionContext.Build(state, null, secondaries, out string context);

            string upper = context.ToUpperInvariant();
            Assert.IsFalse(upper.Contains("METAL"), $"Family leaked before the scan: '{context}'");
            Assert.IsFalse(upper.Contains("RARE"), $"Rarity leaked before the scan: '{context}'");
            Assert.IsFalse(upper.Contains("SAKİN"), $"Environment leaked before the scan: '{context}'");
        }

        [Test]
        public void BeforeAScan_TheOnlyActionIsScanning()
        {
            var state = ScannedPlanet();
            state.Scanned = false;

            HudAction primary = NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.AreEqual("TARA", primary.Caption);
            Assert.IsEmpty(secondaries,
                "Offering build or transfer before a scan would let the player act on data they do not have.");
        }

        [Test]
        public void AfterAScan_ThePlanetIsDescribed()
        {
            HudAction primary = NebulaExtractionContext.Build(ScannedPlanet(), null, secondaries, out string context);

            StringAssert.Contains("METAL", context.ToUpperInvariant());
            StringAssert.Contains("RARE", context.ToUpperInvariant());
            Assert.IsTrue(primary.IsValid);
        }

        [Test]
        public void ABlockedScan_ReportsItsReason()
        {
            var state = ScannedPlanet();
            state.Scanned = false;
            state.ScanBlockedReason = "ÇOK UZAK";

            HudAction primary = NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.IsFalse(primary.Available);
            Assert.AreEqual("ÇOK UZAK", primary.BlockedReason);
        }

        // =====================================================================
        // DRILL ACTION — the old AppendDrillPrompt, as one action
        // =====================================================================

        [Test]
        public void WithNoKitConsumed_ThePromptIsToDeployTheKit()
        {
            var state = ScannedPlanet();
            state.KitConsumed = false;

            Assert.AreEqual("ÖNCÜ KİTİNİ KUR",
                NebulaExtractionContext.Build(state, null, secondaries, out _).Caption);
        }

        [Test]
        public void WithNoDrill_ThePromptIsToBuildOne_WithItsPrice()
        {
            HudAction primary = NebulaExtractionContext.Build(ScannedPlanet(), null, secondaries, out _);

            Assert.AreEqual("SONDAJ KUR", primary.Caption);
            StringAssert.Contains("20", primary.Cost);
        }

        [Test]
        public void AnActiveDrill_OffersTheUpgrade_WithItsRealCost()
        {
            var state = ScannedPlanet();
            state.HasDrill = true;
            state.DrillActive = true;
            state.DrillTier = 1;

            HudAction primary = NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.IsTrue(primary.Available);
            StringAssert.Contains("30", primary.Cost);
            StringAssert.Contains("40", primary.Cost);
            // Turkish casing, not invariant: ToUpperInvariant leaves the dotless
            // "ı" alone and would render "METAL ALAŞıM".
            StringAssert.Contains("ALAŞIM", primary.Cost);
        }

        [Test]
        public void AnUngatedUpgrade_IsBlockedWithTheMilestoneAsTheReason()
        {
            var state = ScannedPlanet();
            state.HasDrill = true;
            state.DrillActive = true;
            state.StationConstructionGateMet = false;

            HudAction primary = NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.IsFalse(primary.Available);
            StringAssert.Contains("İSTASYON İNŞASI", primary.BlockedReason);
        }

        [Test]
        public void AnUpgradingDrill_SaysProductionHasStopped()
        {
            var state = ScannedPlanet();
            state.HasDrill = true;
            state.DrillActive = true;
            state.DrillUpgrading = true;

            HudAction primary = NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.IsFalse(primary.Available);
            StringAssert.Contains("ÜRETİM", primary.BlockedReason.ToUpperInvariant());
        }

        [Test]
        public void AMaxTierDrill_SaysSo_RatherThanOfferingNothing()
        {
            var state = ScannedPlanet();
            state.HasDrill = true;
            state.DrillActive = true;
            state.DrillAtMaxTier = true;

            HudAction primary = NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.IsTrue(primary.IsValid, "A maxed drill must still occupy the primary slot with an explanation.");
            Assert.IsFalse(primary.Available);
            Assert.IsNotNull(primary.BlockedReason);
        }

        [Test]
        public void EveryUnavailableAction_CarriesAReason()
        {
            // The rule the whole redesign rests on, asserted across the state space
            // rather than trusted per call site.
            var permutations = new List<ExtractionContextState>();
            foreach (bool drill in new[] { false, true })
            foreach (bool active in new[] { false, true })
            foreach (bool upgrading in new[] { false, true })
            foreach (bool maxed in new[] { false, true })
            foreach (bool gate in new[] { false, true })
            {
                var state = ScannedPlanet();
                state.HasDrill = drill;
                state.DrillActive = active;
                state.DrillUpgrading = upgrading;
                state.DrillAtMaxTier = maxed;
                state.StationConstructionGateMet = gate;
                permutations.Add(state);
            }

            foreach (ExtractionContextState state in permutations)
            {
                HudAction primary = NebulaExtractionContext.Build(state, null, secondaries, out _);
                if (primary.IsValid && !primary.Available)
                    Assert.IsFalse(string.IsNullOrEmpty(primary.BlockedReason),
                        "An unavailable action with no reason is the dead control this redesign removes.");
                foreach (HudAction secondary in secondaries)
                    if (!secondary.Available)
                        Assert.IsFalse(string.IsNullOrEmpty(secondary.BlockedReason));
            }
        }

        [Test]
        public void NoTargetPlanet_ShowsNothingAtAll()
        {
            HudAction primary = NebulaExtractionContext.Build(default, null, secondaries, out string context);

            Assert.IsFalse(primary.IsValid);
            Assert.IsEmpty(secondaries);
            Assert.IsEmpty(context, "With no target the HUD shows nothing, not a stale line.");
        }

        // =====================================================================
        // THE CAP — this is what stops the wall growing back
        // =====================================================================

        [Test]
        public void ExtractionNeverOffersMoreThanTheBarCanShow()
        {
            var state = ScannedPlanet();
            state.HasDrill = true;
            state.DrillActive = true;
            state.CanTransfer = true;

            NebulaExtractionContext.Build(state, null, secondaries, out _);

            Assert.LessOrEqual(secondaries.Count, NebulaHudContextBar.MaxSecondaryActions,
                "Extraction offered more secondary actions than the HUD is allowed to show.");
        }

        [Test]
        public void TheBarEnforcesItsOwnCap_RatherThanTrustingCallers()
        {
            var shell = NebulaUiShell.Create();
            try
            {
                NebulaHudContextBar bar = NebulaHudContextBar.Create(shell);
                var many = new List<HudAction>
                {
                    new HudAction("BİR", KeyCode.B),
                    new HudAction("İKİ", KeyCode.N),
                    new HudAction("ÜÇ", KeyCode.T),
                    new HudAction("DÖRT", KeyCode.G),
                };

                bar.Show("BAĞLAM", new HudAction("BİRİNCİL", KeyCode.F), many);

                Assert.AreEqual(1 + NebulaHudContextBar.MaxSecondaryActions, bar.VisibleActionCount);
                Assert.AreEqual(2, bar.DroppedActionCount,
                    "The cap must be enforced here; 'just this once' is how the old wall grew.");
            }
            finally
            {
                Object.DestroyImmediate(shell.gameObject);
            }
        }

        [Test]
        public void ClearingTheBar_LeavesNoStaleAction()
        {
            var shell = NebulaUiShell.Create();
            try
            {
                NebulaHudContextBar bar = NebulaHudContextBar.Create(shell);
                bar.Show("BAĞLAM", new HudAction("BİRİNCİL", KeyCode.F));
                Assert.AreEqual(1, bar.VisibleActionCount);

                bar.Clear();
                Assert.AreEqual(0, bar.VisibleActionCount);
                Assert.IsEmpty(bar.ContextText);
            }
            finally
            {
                Object.DestroyImmediate(shell.gameObject);
            }
        }

        // =====================================================================
        // KEY PROMPTS
        // =====================================================================

        [Test]
        public void KeyPrompts_AreReadable_NotUnityEnumNames()
        {
            Assert.AreEqual("1", NebulaHudContextBar.KeyPrompt(KeyCode.Alpha1));
            Assert.AreEqual("[", NebulaHudContextBar.KeyPrompt(KeyCode.LeftBracket));
            Assert.AreEqual("ESC", NebulaHudContextBar.KeyPrompt(KeyCode.Escape));
            Assert.AreEqual("F", NebulaHudContextBar.KeyPrompt(KeyCode.F));
            Assert.AreEqual(string.Empty, NebulaHudContextBar.KeyPrompt(KeyCode.None));
        }
    }
}
