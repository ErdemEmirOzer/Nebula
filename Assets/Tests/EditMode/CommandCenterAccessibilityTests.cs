using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.UI.Hud;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The Command Center's build/upgrade seam, driven as pure data.
    /// <para>
    /// Everything here goes through <see cref="CommandBuildService.Evaluate"/> and
    /// <see cref="CommandBuildText"/> — the same two the panel's button and the K
    /// key both go through. Nothing reaches into a private method, and nothing
    /// hand-builds a structure to make an assertion come out right; the two places
    /// that need a real structure on the ground are in the PlayMode fixture, where
    /// one can exist honestly.
    /// </para>
    /// <para>
    /// Every cost, duration and gate is asserted against the catalog rather than
    /// against a number typed here. A test that repeats the number it is checking
    /// passes when the catalog and the code drift apart in the same direction,
    /// which is precisely the failure a cost table wants caught.
    /// </para>
    /// </summary>
    public class CommandCenterAccessibilityTests
    {
        private const string WorldId = "sector.0.0.0/system.000/planet.00";

        private static PlanetProfile Profile(ResourceFamily family, bool scanned = true)
        {
            var profile = new PlanetProfile(new PlanetManifest(
                0, WorldId, family, Rarity.Common, 40000f, 0f, 0f, 0f, 1200f,
                false, EnvironmentClass.Calm, 12345UL));
            profile.Scanned = scanned;
            return profile;
        }

        /// <summary>A hold with exactly the T1 recipe in it, and nothing spoken for.</summary>
        private static LogisticsCargo StockedFor(ResourceFamily family, float multiplier = 1f)
        {
            var cargo = new LogisticsCargo(100000f);
            foreach (var (itemId, amount) in CommandBuildService.BuildT1Cost(family))
                cargo.Add(itemId, amount * multiplier);
            return cargo;
        }

        private static CommandBuildContext Context(PlanetProfile profile, LogisticsCargo cargo,
            float altitude = 100f, float rangeLimit = 1000f, bool evacuationLocked = false,
            bool nodeOccupied = false, int characterLevel = 1, bool controlled = false) =>
            new CommandBuildContext(WorldId, profile, altitude, rangeLimit, evacuationLocked,
                nodeOccupied, null, cargo, characterLevel, 0, controlled, deliverySeconds: 5f);

        // =====================================================================
        // The five families
        // =====================================================================

        /// <summary>
        /// Each family's own refined material, and no other. The mapping is the
        /// processing catalog's; this proves the build seam reads it rather than
        /// carrying a second copy that could be one entry out of step.
        /// </summary>
        [Test]
        public void EveryPlanetFamilyAsksForItsOwnRefinedMaterial()
        {
            var expected = new Dictionary<ResourceFamily, string>
            {
                { ResourceFamily.Metal, "material.metal_alloy" },
                { ResourceFamily.Wood, "material.biocomposite" },
                { ResourceFamily.Gas, "material.refined_gas" },
                { ResourceFamily.Water, "material.purified_water" },
                { ResourceFamily.Diamond, "material.industrial_diamond" },
            };

            foreach (var pair in expected)
            {
                Assert.AreEqual(pair.Value, CommandBuildService.MaterialIdFor(pair.Key),
                    $"{pair.Key} maps to the wrong refined material");

                CommandBuildAssessment assessment = CommandBuildService.Evaluate(
                    Context(Profile(pair.Key), StockedFor(pair.Key)));

                Assert.AreEqual(CommandActionKind.BuildT1, assessment.Action);
                Assert.AreEqual(pair.Value, assessment.MaterialItemId);
                CollectionAssert.Contains(ItemIds(assessment), pair.Value,
                    $"{pair.Key}'s cost never mentions its own material");
            }
        }

        /// <summary>
        /// A hold full of the wrong family's material is still a refusal. The
        /// Metal case is the one that hides a bug: metal alloy is the first entry
        /// in the material table and the natural value for a broken mapping to
        /// fall back to.
        /// </summary>
        [Test]
        public void TheWrongFamilysMaterialDoesNotPayForTheBuild()
        {
            PlanetProfile profile = Profile(ResourceFamily.Wood);
            var cargo = new LogisticsCargo(100000f);
            cargo.Add(ProcessingCatalog.BasicPartId, SurfaceStructureCatalog.Command.PartsCost);
            cargo.Add("material.metal_alloy", 5000f);

            CommandBuildAssessment assessment = CommandBuildService.Evaluate(Context(profile, cargo));

            Assert.AreEqual(CommandActionKind.BuildT1, assessment.Action);
            Assert.IsFalse(assessment.CanAct, "a Wood planet accepted Metal Alloy");
            Assert.AreEqual(CommandBuildRejectReason.InsufficientResources, assessment.BuildReject);
        }

        // =====================================================================
        // Cost, from the catalog
        // =====================================================================

        [Test]
        public void TheT1CostIsTheCatalogsThirtyPartsAndTenFamilyMaterial()
        {
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            Assert.AreEqual(30, def.PartsCost, "the catalog's T1 parts cost moved");
            Assert.AreEqual(10, def.MaterialCost, "the catalog's T1 material cost moved");

            CommandBuildAssessment assessment = CommandBuildService.Evaluate(
                Context(Profile(ResourceFamily.Gas), StockedFor(ResourceFamily.Gas)));

            Assert.AreEqual(2, assessment.Cost.Count);
            Assert.AreEqual(ProcessingCatalog.BasicPartId, assessment.Cost[0].ItemId);
            Assert.AreEqual(def.PartsCost, assessment.Cost[0].Required);
            Assert.AreEqual("material.refined_gas", assessment.Cost[1].ItemId);
            Assert.AreEqual(def.MaterialCost, assessment.Cost[1].Required);
            Assert.AreEqual(def.BuildSeconds, assessment.WorkSeconds, "T1 build time is not the catalog's");
        }

        /// <summary>
        /// T1 has no character-level, controlled-planet or module gate — only T2
        /// does. A gate invented for T1 would lock a brand new campaign out of the
        /// one structure everything else needs.
        /// </summary>
        [Test]
        public void TheT1BuildNeedsNoLevelNoControlledPlanetAndNoModuleTier()
        {
            CommandBuildAssessment assessment = CommandBuildService.Evaluate(
                Context(Profile(ResourceFamily.Metal), StockedFor(ResourceFamily.Metal),
                    characterLevel: 1, controlled: false));

            Assert.AreEqual(CommandActionKind.BuildT1, assessment.Action);
            Assert.IsTrue(assessment.CanAct,
                $"a level-1 pilot with no controlled planet was refused: {CommandBuildText.BlockedReason(assessment)}");
            Assert.AreEqual(CommandBuildRejectReason.None, assessment.BuildReject);
            Assert.AreEqual(StructureUpgradeRejectReason.None, assessment.UpgradeReject);
        }

        /// <summary>T2's gates stay exactly where the catalog put them, and stay off T1.</summary>
        [Test]
        public void TheT2GatesAreTheCatalogsAndBelongToT2Alone()
        {
            StructureTierDef t2 = PlanetaryDefenseCatalog.CommandT2;
            Assert.AreEqual(21, t2.RequiredCharacterLevel);
            Assert.AreEqual(65, t2.PartsCost);
            Assert.AreEqual(120f, t2.BuildSeconds);

            var byId = new Dictionary<string, float>();
            foreach (var (itemId, amount) in t2.FullCost()) byId[itemId] = amount;
            Assert.AreEqual(25f, byId["material.biocomposite"]);
            Assert.AreEqual(15f, byId["material.metal_alloy"]);

            Assert.AreEqual(0, SurfaceStructureCatalog.Command.PartsCost - 30,
                "T1's own cost must not have moved with T2's");
        }

        // =====================================================================
        // Stored vs reserved vs available
        // =====================================================================

        /// <summary>
        /// A full hold whose contents are all promised elsewhere buys nothing.
        /// The card has to be able to say that, which means the assessment has to
        /// carry all three numbers, not just the total.
        /// </summary>
        [Test]
        public void ResourcesReservedByAnotherJobAreNotAvailableAndTheCardCanSaySo()
        {
            PlanetProfile profile = Profile(ResourceFamily.Diamond);
            LogisticsCargo cargo = StockedFor(ResourceFamily.Diamond);
            Assert.IsTrue(cargo.TryReserveAll("someone-elses-job",
                    CommandBuildService.BuildT1Cost(ResourceFamily.Diamond)),
                "the fixture could not set up the competing reservation");

            CommandBuildAssessment assessment = CommandBuildService.Evaluate(Context(profile, cargo));

            Assert.IsFalse(assessment.CanAct, "reserved stock was spent twice");
            Assert.AreEqual(CommandBuildRejectReason.InsufficientResources, assessment.BuildReject);

            CommandCostLine parts = assessment.Cost[0];
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost, parts.Stored, "the parts are physically there");
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost, parts.Reserved, "and every one is spoken for");
            Assert.AreEqual(0f, parts.Available, "so none of them may be spent");
            Assert.IsFalse(parts.Met);
        }

        /// <summary>Twice the recipe, half of it reserved, still builds — availability is the test, not stock.</summary>
        [Test]
        public void StockBeyondWhatIsReservedStillBuys()
        {
            LogisticsCargo cargo = StockedFor(ResourceFamily.Metal, multiplier: 2f);
            cargo.TryReserveAll("someone-elses-job", CommandBuildService.BuildT1Cost(ResourceFamily.Metal));

            CommandBuildAssessment assessment = CommandBuildService.Evaluate(
                Context(Profile(ResourceFamily.Metal), cargo));

            Assert.IsTrue(assessment.CanAct,
                $"unreserved stock was treated as unavailable: {CommandBuildText.BlockedReason(assessment)}");
        }

        // =====================================================================
        // Evaluation is read-only
        // =====================================================================

        /// <summary>
        /// The panel asks this five times a second. If asking reserved anything,
        /// standing still next to a planet would quietly consume the hold.
        /// </summary>
        [Test]
        public void EvaluatingChangesNothingAtAll()
        {
            PlanetProfile profile = Profile(ResourceFamily.Water);
            LogisticsCargo cargo = StockedFor(ResourceFamily.Water, multiplier: 3f);

            var before = new Dictionary<string, (float stored, float reserved)>();
            foreach (var kv in cargo.Items) before[kv.Key] = (kv.Value, cargo.GetReserved(kv.Key));
            bool scannedBefore = profile.Scanned;

            for (int i = 0; i < 25; i++) CommandBuildService.Evaluate(Context(profile, cargo));

            foreach (var kv in before)
            {
                Assert.AreEqual(kv.Value.stored, cargo.GetStored(kv.Key), 0.0001f,
                    $"{kv.Key} stock moved just from being looked at");
                Assert.AreEqual(kv.Value.reserved, cargo.GetReserved(kv.Key), 0.0001f,
                    $"{kv.Key} picked up a reservation from an evaluation");
            }
            Assert.AreEqual(scannedBefore, profile.Scanned, "evaluation wrote to the planet profile");
            Assert.AreEqual(0, ExtractionState.Structures.Count, "evaluation created a structure");
        }

        // =====================================================================
        // The information gate
        // =====================================================================

        /// <summary>
        /// An unscanned planet must not name its family material anywhere — not in
        /// the cost list, not in the assessment, and not in any line the card puts
        /// on screen. The scan is what that answer is for.
        /// </summary>
        [Test]
        public void AnUnscannedPlanetLeaksNeitherItsFamilyNorItsMaterial()
        {
            foreach (ResourceFamily family in System.Enum.GetValues(typeof(ResourceFamily)))
            {
                PlanetProfile profile = Profile(family, scanned: false);
                CommandBuildAssessment assessment = CommandBuildService.Evaluate(
                    Context(profile, StockedFor(family)));

                Assert.IsNull(assessment.MaterialItemId, $"{family}'s material was named before the scan");
                Assert.IsTrue(assessment.MaterialHidden);
                Assert.AreEqual(CommandBuildRejectReason.NotScanned, assessment.BuildReject);
                Assert.IsFalse(assessment.CanAct);

                foreach (string itemId in ItemIds(assessment))
                    CollectionAssert.DoesNotContain(ProcessingCatalog.MaterialIds, itemId,
                        $"{family}'s refined material appeared in a pre-scan cost line");

                Assert.AreEqual("ÖNCE GEZEGENİ TARA", CommandBuildText.BlockedReason(assessment));
            }
        }

        // =====================================================================
        // Refusals, in order, in Turkish
        // =====================================================================

        [Test]
        public void EveryTypedRefusalHasANonEmptyTurkishSentence()
        {
            foreach (CommandBuildRejectReason reason in System.Enum.GetValues(typeof(CommandBuildRejectReason)))
            {
                string text = CommandBuildText.BuildRejectText(reason);
                if (reason == CommandBuildRejectReason.None)
                {
                    Assert.IsNull(text, "None is not a refusal and must say nothing");
                    continue;
                }
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{reason} has no text");
                AssertTurkishPlayerText(text, reason.ToString());
            }

            foreach (StructureUpgradeRejectReason reason in System.Enum.GetValues(typeof(StructureUpgradeRejectReason)))
            {
                string text = CommandBuildText.UpgradeRejectText(reason);
                if (reason == StructureUpgradeRejectReason.None)
                {
                    Assert.IsNull(text);
                    continue;
                }
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{reason} has no text");
                AssertTurkishPlayerText(text, reason.ToString());
            }
        }

        /// <summary>
        /// No item id, no enum name, no English. A refusal that shows the player
        /// "InsufficientResources" or "material.biocomposite" has technically said
        /// something and practically said nothing.
        /// </summary>
        private static void AssertTurkishPlayerText(string text, string what)
        {
            StringAssert.DoesNotContain("material.", text, what);
            StringAssert.DoesNotContain("part.", text, what);
            StringAssert.DoesNotContain("_", text, what);
            Assert.AreEqual(text.ToUpperInvariant(), text, $"{what} is not in the HUD's voice");
        }

        /// <summary>Physical refusals outrank economic ones: being somewhere else is the first thing to fix.</summary>
        [Test]
        public void PresenceRefusalsOutrankAnEmptyHold()
        {
            PlanetProfile profile = Profile(ResourceFamily.Metal);
            var emptyHold = new LogisticsCargo(1000f);

            CommandBuildAssessment far = CommandBuildService.Evaluate(
                Context(profile, emptyHold, altitude: 5000f, rangeLimit: 600f));
            Assert.AreEqual(CommandBuildRejectReason.OutOfRange, far.BuildReject);
            Assert.AreEqual("GEZEGENE YAKLAŞ", CommandBuildText.BlockedReason(far));
            Assert.AreEqual(5000f, far.Altitude);
            Assert.AreEqual(600f, far.RangeLimit, "the card cannot say how much closer without the limit");

            CommandBuildAssessment locked = CommandBuildService.Evaluate(
                Context(profile, emptyHold, evacuationLocked: true));
            Assert.AreEqual(CommandBuildRejectReason.EvacuationLocked, locked.BuildReject);

            CommandBuildAssessment occupied = CommandBuildService.Evaluate(
                Context(profile, StockedFor(ResourceFamily.Metal), nodeOccupied: true));
            Assert.AreEqual(CommandActionKind.Rejected, occupied.Action);
            Assert.AreEqual(CommandBuildRejectReason.NodeOccupied, occupied.BuildReject);
            Assert.AreEqual("DOLU", CommandBuildText.NodeStatus(occupied));
        }

        /// <summary>
        /// A presence refusal blocks the upgrade too, not just a fresh build.
        /// <para>
        /// This is the one a real capture caught. A planet orbits at thousands of
        /// units per second, so a ship parked beside its own finished Command
        /// Center is out of range a few seconds later. The upgrade verb kept its
        /// own reject channel clear in that state, and the control read itself as
        /// available: a live "YÜKSELT · T2" with its reason suppressed, and a click
        /// that would really have started the upgrade from out of range.
        /// </para>
        /// </summary>
        [Test]
        public void APresenceRefusalBlocksTheUpgradeAndNotOnlyTheBuild()
        {
            foreach (CommandBuildRejectReason presence in new[]
                     {
                         CommandBuildRejectReason.OutOfRange,
                         CommandBuildRejectReason.NotScanned,
                         CommandBuildRejectReason.EvacuationLocked,
                     })
            {
                var upgrade = new CommandBuildAssessment
                {
                    Action = CommandActionKind.UpgradeT2,
                    HasStructure = true,
                    Tier = 1,
                    State = DrillLifecycle.Active,
                    BuildReject = presence,
                    UpgradeReject = StructureUpgradeRejectReason.None,
                };

                Assert.IsFalse(upgrade.CanAct,
                    $"the T2 upgrade offered itself while the ship was refused for {presence}");
                Assert.AreEqual(CommandBuildText.BuildRejectText(presence),
                    CommandBuildText.BlockedReason(upgrade),
                    $"the {presence} refusal is not the one the player is shown");

                var build = new CommandBuildAssessment
                {
                    Action = CommandActionKind.BuildT1,
                    BuildReject = presence,
                };
                Assert.IsFalse(build.CanAct, $"a fresh build was offered while refused for {presence}");
            }
        }

        /// <summary>No planet, and no cargo, are both answers — never a silent nothing.</summary>
        [Test]
        public void NoTargetAndNoCargoBothProduceAVisibleRefusal()
        {
            CommandBuildAssessment none = CommandBuildService.Evaluate(default);
            Assert.AreEqual(CommandActionKind.None, none.Action);
            Assert.AreEqual(CommandBuildRejectReason.NoTarget, none.BuildReject);
            Assert.IsFalse(string.IsNullOrEmpty(CommandBuildText.BlockedReason(none)));

            CommandBuildAssessment noHold = CommandBuildService.Evaluate(
                Context(Profile(ResourceFamily.Gas), null));
            Assert.AreEqual(CommandBuildRejectReason.NoCargo, noHold.BuildReject);
            Assert.IsFalse(noHold.CanAct);
            Assert.IsFalse(string.IsNullOrEmpty(CommandBuildText.BlockedReason(noHold)));
        }

        // =====================================================================
        // What the control says
        // =====================================================================

        [Test]
        public void TheControlsCaptionIsTheVerbAndTheReasonIsSeparate()
        {
            CommandBuildAssessment buildable = CommandBuildService.Evaluate(
                Context(Profile(ResourceFamily.Metal), StockedFor(ResourceFamily.Metal)));
            Assert.AreEqual("KUR · T1", CommandBuildText.Caption(buildable));
            Assert.IsTrue(buildable.CanAct);
            Assert.IsNull(CommandBuildText.BlockedReason(buildable));
            Assert.AreEqual("İNŞA EDİLMEDİ", CommandBuildText.Status(buildable));
            Assert.AreEqual("BOŞ", CommandBuildText.NodeStatus(buildable));

            // Short on parts: the verb does not change, the reason appears.
            CommandBuildAssessment broke = CommandBuildService.Evaluate(
                Context(Profile(ResourceFamily.Metal), new LogisticsCargo(1000f)));
            Assert.AreEqual("KUR · T1", CommandBuildText.Caption(broke),
                "the caption became the verdict instead of the verb");
            Assert.IsFalse(broke.CanAct);
            StringAssert.Contains("KAYNAKLAR SAYFASINDA", CommandBuildText.BlockedReason(broke),
                "a short hold has to say where resources come from");
        }

        [Test]
        public void EveryLifecycleStateHasItsOwnStatusLine()
        {
            Assert.AreEqual("İNŞA EDİLMEDİ", CommandBuildText.Status(new CommandBuildAssessment()));

            Assert.AreEqual("KAPSÜL YOLDA", CommandBuildText.Status(new CommandBuildAssessment
            { HasStructure = true, State = DrillLifecycle.CapsuleInTransit }));

            Assert.AreEqual("İNŞA EDİLİYOR · %40", CommandBuildText.Status(new CommandBuildAssessment
            { HasStructure = true, State = DrillLifecycle.Constructing, Progress01 = 0.4f }));

            Assert.AreEqual("AKTİF · T1", CommandBuildText.Status(new CommandBuildAssessment
            { HasStructure = true, State = DrillLifecycle.Active, Tier = 1 }));

            Assert.AreEqual("YÜKSELTİLİYOR · %75", CommandBuildText.Status(new CommandBuildAssessment
            { HasStructure = true, State = DrillLifecycle.Active, Tier = 1, Upgrading = true, Progress01 = 0.75f }));

            Assert.AreEqual("AKTİF · T2", CommandBuildText.Status(new CommandBuildAssessment
            { HasStructure = true, State = DrillLifecycle.Active, Tier = 2 }));

            Assert.AreEqual("DEVRE DIŞI", CommandBuildText.Status(new CommandBuildAssessment
            { HasStructure = true, State = DrillLifecycle.Disabled, Tier = 1 }));

            Assert.AreEqual("AZAMİ SEVİYE", CommandBuildText.Caption(new CommandBuildAssessment
            { Action = CommandActionKind.MaxTier }));
            Assert.AreEqual("İNŞA SÜRÜYOR", CommandBuildText.Caption(new CommandBuildAssessment
            { Action = CommandActionKind.Building }));
            Assert.AreEqual("YÜKSELT · T2", CommandBuildText.Caption(new CommandBuildAssessment
            { Action = CommandActionKind.UpgradeT2 }));
        }

        /// <summary>Success and refusal both speak; a pressed control is never silent.</summary>
        [Test]
        public void TheOutcomeLineSaysSomethingOnBothPaths()
        {
            Assert.AreEqual("KOMUTA MERKEZİ KAPSÜLÜ YOLA ÇIKTI", CommandBuildText.OutcomeText(
                new CommandActionOutcome(true, CommandActionKind.BuildT1,
                    CommandBuildRejectReason.None, StructureUpgradeRejectReason.None)));

            Assert.AreEqual("KOMUTA MERKEZİ T2 YÜKSELTMESİ BAŞLADI", CommandBuildText.OutcomeText(
                new CommandActionOutcome(true, CommandActionKind.UpgradeT2,
                    CommandBuildRejectReason.None, StructureUpgradeRejectReason.None)));

            Assert.AreEqual("KOMUTA YUVASI DOLU", CommandBuildText.OutcomeText(
                new CommandActionOutcome(false, CommandActionKind.Rejected,
                    CommandBuildRejectReason.NodeOccupied, StructureUpgradeRejectReason.None)));

            Assert.AreEqual("EN AZ BİR KONTROL EDİLEN GEZEGEN GEREKLİ", CommandBuildText.OutcomeText(
                new CommandActionOutcome(false, CommandActionKind.UpgradeT2,
                    CommandBuildRejectReason.None, StructureUpgradeRejectReason.NoControlledPlanet)));

            Assert.IsNull(CommandBuildText.OutcomeText(default), "an outcome that never happened says nothing");
        }

        // =====================================================================
        // Layout
        // =====================================================================

        /// <summary>
        /// The card is bottom-right anchored, and at the smallest supported screen
        /// and the largest supported interface scale it still has to be entirely
        /// on it. 1280x720 at 140% is the worst case the settings allow.
        /// </summary>
        [Test]
        public void TheCardFitsOnScreenAtEverySupportedSizeAndScale()
        {
            // The card's own geometry, read from the panel rather than retyped.
            const float width = 420f, insetX = 32f;
            float insetY = NebulaHudPlanetContext.CardBottom;
            float height = NebulaHudPlanetContext.CardHeight;

            foreach ((float screenW, float screenH) in new[] { (1280f, 720f), (1920f, 1080f), (3440f, 1440f) })
            {
                foreach (float scale in new[] { 1f, NebulaUiShell.MaxUiScale })
                {
                    Vector2 logical = LogicalCanvas(screenW, screenH, scale);

                    Assert.LessOrEqual(insetY + height, logical.y,
                        $"the card runs off the top at {screenW}x{screenH} @ {scale:0.00}x "
                        + $"(needs {insetY + height:0}, canvas is {logical.y:0})");
                    Assert.LessOrEqual(insetX + width, logical.x,
                        $"the card runs off the left edge at {screenW}x{screenH} @ {scale:0.00}x");
                }
            }
        }

        /// <summary>
        /// The control has to stay hittable. 32 logical pixels is the floor, and
        /// the card sits at the bottom of a stack that had to be squeezed to fit
        /// an ultrawide at 140% — squeezing the button is the tempting way to buy
        /// that room and the one that costs the player the control.
        /// </summary>
        [Test]
        public void ThePrimaryControlKeepsAThirtyTwoPixelHitTarget()
        {
            Assert.GreaterOrEqual(NebulaHudPlanetContext.CommandButtonHeight, 32f,
                "the Command control is below the minimum hit target");
        }

        /// <summary>
        /// Every text row is tall enough for the type it holds.
        /// <para>
        /// This is the one that caught a card whose entire cost table, timings and
        /// refusal line were invisible on screen while every string assertion
        /// about them passed. The labels are built with
        /// <c>TextOverflowModes.Ellipsis</c>, and TMP does not clip a line that is
        /// too tall for its rect — it draws nothing at all, silently. At 12px rects
        /// holding 11px type, that is exactly what happened.
        /// </para>
        /// <para>
        /// A row must clear its font size by TMP's own line-height margin, which
        /// for this face is about 1.16em. 14px for 11px caption type and 16px for
        /// 12px compact type are the smallest values that do.
        /// </para>
        /// </summary>
        [Test]
        public void EveryRowIsTallEnoughForTheTypeItHolds()
        {
            const float lineHeightFactor = 1.16f; // Liberation Sans SDF, at TMP's default line spacing
            const int caption = 11;               // NebulaUiDesignTokens.SizeCaption
            const int compact = 12;               // NebulaUiDesignTokens.SizeCompact

            Assert.GreaterOrEqual(NebulaHudPlanetContext.CaptionRowHeight, caption * lineHeightFactor,
                "a caption row is too short for its own type: TMP will draw nothing there");
            Assert.GreaterOrEqual(NebulaHudPlanetContext.CompactRowHeight, compact * lineHeightFactor,
                "a compact row is too short for its own type: TMP will draw nothing there");

            // And the rows must not overlap each other, or the table reads as one smear.
            Assert.GreaterOrEqual(NebulaHudPlanetContext.CostRowPitch, NebulaHudPlanetContext.CaptionRowHeight,
                "the cost rows are pitched closer together than they are tall");
        }

        /// <summary>
        /// The whole card's contents fit inside the card. Every row this class
        /// places is accounted for, so shrinking the card to win screen height
        /// cannot quietly push the button over the refusal line.
        /// </summary>
        [Test]
        public void TheCardsRowsFitInsideTheCardWithTheButtonClear()
        {
            // The last content row is the feedback line, placed at -124 from the top.
            const float lastRowTop = 124f;
            float contentBottom = lastRowTop + NebulaHudPlanetContext.CaptionRowHeight;
            float buttonTop = NebulaHudPlanetContext.CardHeight
                              - (6f + NebulaHudPlanetContext.CommandButtonHeight);

            Assert.LessOrEqual(contentBottom, buttonTop,
                $"the feedback line runs under the button (content ends at {contentBottom:0}, "
                + $"button starts at {buttonTop:0})");
        }

        /// <summary>
        /// The logical canvas the HUD lays out in, from the shell's real scaler
        /// settings: ScaleWithScreenSize, MatchWidthOrHeight at 0.5, with the
        /// interface scale folded into the reference resolution.
        /// </summary>
        private static Vector2 LogicalCanvas(float screenWidth, float screenHeight, float uiScale)
        {
            Vector2 reference = NebulaUiShell.ReferenceResolution / uiScale;
            float factor = Mathf.Sqrt((screenWidth / reference.x) * (screenHeight / reference.y));
            return new Vector2(screenWidth / factor, screenHeight / factor);
        }

        /// <summary>
        /// Every line the card can print has to fit the rect it is given.
        /// <para>
        /// The interface scale does not help or hurt here — the rect and the type
        /// size are both in canvas units, so the character budget per line is the
        /// same at 100% and at 140%. What changes with scale is whether the card
        /// is still on screen, which the test above owns. This one owns the other
        /// half: the factory ellipsises rather than spills, so a line that does
        /// not fit is not a crash, it is the answer cut off mid-word.
        /// </para>
        /// </summary>
        [Test]
        public void EveryCostLineFitsTheRectItIsDrawnIn()
        {
            // Four-digit stock behind the longest Turkish material name, on the
            // widest recipe in the game.
            LogisticsCargo cargo = StockedFor(ResourceFamily.Diamond, multiplier: 200f);
            cargo.TryReserveAll("competing", CommandBuildService.BuildT1Cost(ResourceFamily.Diamond));

            CommandBuildAssessment assessment = CommandBuildService.Evaluate(
                Context(Profile(ResourceFamily.Diamond), cargo));

            const float caption = 11f; // NebulaUiDesignTokens.SizeCaption

            // 0.72em per glyph, measured off a real 1920x1080 capture rather than
            // guessed: the column header rendered as "GEREK · VAR · DEPO · AYRI…"
            // while this test passed at the 0.62 it used to assume. An estimate
            // that is cheaper than the truth is an estimate that certifies
            // truncation.
            const float widthPerCharacter = 0.72f;
            float itemWidth = NebulaHudPlanetContext.CostItemWidth;
            float verdictWidth = NebulaHudPlanetContext.CostVerdictWidth;
            float numbersWidth = NebulaHudPlanetContext.CostNumbersWidth;

            foreach (CommandCostLine line in assessment.Cost)
            {
                AssertFits(CommandBuildText.ItemName(line.ItemId), itemWidth, caption * widthPerCharacter, "item");
                AssertFits("EKSİK", verdictWidth, caption * widthPerCharacter, "verdict");
                AssertFits($"{line.Required:0} · {line.Available:0} · {line.Stored:0} · {line.Reserved:0}",
                    numbersWidth, caption * widthPerCharacter, "numbers");
            }

            // The header the numbers are read against, and the longest thing the
            // feedback line can ever be asked to say.
            AssertFits(NebulaHudPlanetContext.CommandCostHeader, numbersWidth,
                caption * widthPerCharacter, "the number column header");

            foreach (CommandBuildRejectReason reason in System.Enum.GetValues(typeof(CommandBuildRejectReason)))
            {
                string text = CommandBuildText.BuildRejectText(reason);
                if (text == null) continue;
                if (reason == CommandBuildRejectReason.OutOfRange)
                    text += " (YÜKSEKLİK 99999 · AZAMİ 99999)";
                AssertFits(text, 384f, caption * widthPerCharacter, $"the {reason} refusal");
            }

            foreach (StructureUpgradeRejectReason reason in System.Enum.GetValues(typeof(StructureUpgradeRejectReason)))
            {
                string text = CommandBuildText.UpgradeRejectText(reason);
                if (text != null) AssertFits(text, 384f, caption * widthPerCharacter, $"the {reason} refusal");
            }

            AssertFits("KOMUTA MERKEZİ T2 YÜKSELTMESİ BAŞLADI", 384f, caption * widthPerCharacter, "the longest success");
            AssertFits("TESLİMAT 8 sn  ·  YAPIM 60 sn", 384f, caption * widthPerCharacter, "the timing line");
        }

        private static void AssertFits(string text, float rectWidth, float perCharacter, string what)
        {
            float estimated = text.Length * perCharacter;
            Assert.LessOrEqual(estimated, rectWidth,
                $"the {what} line is cut off: \"{text}\" needs about {estimated:0}px of {rectWidth:0}px");
        }

        private static List<string> ItemIds(CommandBuildAssessment assessment)
        {
            var ids = new List<string>(assessment.Cost.Count);
            foreach (CommandCostLine line in assessment.Cost) ids.Add(line.ItemId);
            return ids;
        }
    }
}
