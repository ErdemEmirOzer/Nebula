using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// LGT-05.2: every HUD that shows status reads its colours from the one
    /// shared palette, and none of them still keeps a private status-colour
    /// table of its own.
    /// </summary>
    public class Lgt052HudPaletteIntegrationTests
    {
        /// <summary>
        /// The surfaces that show status, by source path.
        /// <para>
        /// Re-pointed from the legacy HUDs to the redesigned interface (UI-09).
        /// The contract is unchanged and is the whole point of the fixture: status
        /// colour has exactly one owner and no surface invents its own. What
        /// changed is that these reach <see cref="LightingUiPalette"/> through
        /// <c>NebulaUiDesignTokens.Status</c> rather than calling it directly —
        /// the token layer forwards and is forbidden from copying, which
        /// <c>NebulaUiFoundationTests</c> proves by flipping High Contrast and
        /// checking the colour actually moves.
        /// </para>
        /// </summary>
        private static readonly string[] StatusHudPaths =
        {
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudVitals.cs",
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudNavigation.cs",
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudAlerts.cs",
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudContextBar.cs",
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudPlanetContext.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaOverviewPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaFleetPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaPlanetsPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaCombatPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaDiscoveryPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaProgressionPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaResourcesPage.cs"
        };

        /// <summary>
        /// How the redesigned interface reaches the palette. Naming it here keeps
        /// the assertion honest about the indirection rather than hiding it.
        /// </summary>
        private const string PaletteAccessor = "NebulaUiDesignTokens.Status";

        /// <summary>
        /// Developer tools, deliberately left alone. Their green identity is
        /// how a tester tells a debug overlay from the real interface at a
        /// glance, so folding them into the status palette would remove
        /// information rather than unify it.
        /// </summary>
        private static readonly string[] DeveloperToolPaths =
        {
            "Assets/Scripts/Nebula/UI/SectorDebugHud.cs",
            "Assets/Scripts/Nebula/UI/FlightTestSpeedHud.cs"
        };

        /// <summary>
        /// Colour names that carry status. Panel, frame, bar, arrow and similar
        /// names are layout chrome, not state, and this part deliberately left
        /// them alone.
        /// </summary>
        private static readonly string[] StatusColorNames = { "TextColor", "WarnColor", "CritColor", "CriticalColor", "DimColor" };

        [TearDown]
        public void TearDown() => LightingAccessibility.ResetToDefaults();

        private static string ReadHud(string relativePath)
        {
            string full = Path.Combine(Application.dataPath, "..", relativePath);
            Assert.IsTrue(File.Exists(full), $"{relativePath} is missing — the HUD list is stale");
            return File.ReadAllText(full);
        }

        private static UiStatusLevel[] AllLevels => (UiStatusLevel[])Enum.GetValues(typeof(UiStatusLevel));

        // ---- every status HUD is connected -------------------------------------

        [Test]
        public void EveryStatusShowingHudResolvesItsColoursThroughTheSharedPalette()
        {
            foreach (string path in StatusHudPaths)
            {
                string source = ReadHud(path);
                Assert.IsTrue(source.Contains(PaletteAccessor) || source.Contains("LightingUiPalette"),
                    $"{Path.GetFileName(path)} does not read the shared status palette");
            }
        }

        [Test]
        public void TheTokenLayerForwardsToThePaletteRatherThanCopyingIt()
        {
            // The indirection above is only sound while the token layer really
            // delegates. If it ever hard-codes a colour, every surface would still
            // pass the check above while quietly drifting from the palette.
            string tokens = ReadHud("Assets/Scripts/Nebula/UI/Design/NebulaUiDesignTokens.cs");
            Assert.IsTrue(tokens.Contains("LightingUiPalette.Resolve"),
                "NebulaUiDesignTokens no longer forwards status colour to LightingUiPalette");

            foreach (UiStatusLevel level in AllLevels)
                Assert.AreEqual(LightingUiPalette.Resolve(level),
                    Nebula.UI.Design.NebulaUiDesignTokens.Status(level),
                    $"the token layer drifted from the palette for {level}");
        }

        [Test]
        public void NoHudStillDeclaresItsOwnStatusColourConstant()
        {
            var offenders = new List<string>();

            foreach (string path in StatusHudPaths)
            {
                string source = ReadHud(path);
                foreach (string name in StatusColorNames)
                {
                    // A private status-colour constant looks like:
                    //   static readonly Color TextColor = new Color(...)
                    var declaration = new Regex(
                        @"static\s+readonly\s+Color\s+" + name + @"\s*=", RegexOptions.None);
                    if (declaration.IsMatch(source))
                        offenders.Add($"{Path.GetFileName(path)}.{name}");
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "these HUDs still invent their own status colours instead of reading the palette: "
                + string.Join(", ", offenders));
        }

        [Test]
        public void DeveloperToolsKeepTheirOwnDistinctIdentity()
        {
            foreach (string path in DeveloperToolPaths)
            {
                string source = ReadHud(path);
                Assert.IsTrue(Regex.IsMatch(source, @"static\s+readonly\s+Color\s+TextColor\s*="),
                    $"{Path.GetFileName(path)} is a developer tool and must stay visually distinct from the real HUD");
            }
        }

        [Test]
        public void LayoutChromeWasLeftAlone()
        {
            // Panel/frame/surface colours are not status and must not be swept into
            // the palette. In the redesign that separation lives in the token file:
            // surfaces and borders are plain constants there, while status forwards
            // to the palette. Folding the two together would make every panel
            // change colour with the ship's condition.
            string tokens = ReadHud("Assets/Scripts/Nebula/UI/Design/NebulaUiDesignTokens.cs");

            // Asserted on what the token resolves THROUGH, not on how it is stored.
            //
            // This used to require the literal form `static readonly Color X =`, as a
            // stand-in for "not a status lookup". High Contrast made that stand-in
            // wrong in both directions at once: chrome legitimately became
            // `static Color X => Pick(normal, contrast)` -- an accessibility switch
            // that has nothing to do with the ship's condition -- so the proxy failed
            // on correct code, while a hypothetical
            // `static readonly Color X = LightingUiPalette.Resolve(...)` would have
            // sailed straight through it. The rule the comment above states is the
            // one checked now, and it holds whatever the storage form.
            foreach (string chrome in new[] { "SurfaceObsidian", "BorderSubtle" })
            {
                Assert.IsTrue(Regex.IsMatch(tokens, @"Color\s+" + chrome + @"\s*(=>|=)"),
                    chrome + " is gone from the token file; this guard needs to follow it");
                Assert.IsFalse(
                    Regex.IsMatch(tokens, @"Color\s+" + chrome + @"\s*(=>|=)[^;]*LightingUiPalette"),
                    chrome + " chrome was folded into the status palette");
            }
        }

        // ---- the resolver mappings the HUDs rely on --------------------------------

        [Test]
        public void EveryStatusLevelStillResolvesToItsOwnLegibleColour()
        {
            var colors = AllLevels.Select(l => LightingUiPalette.Resolve(l)).ToArray();

            Assert.AreEqual(AllLevels.Length, colors.Distinct().Count());
            foreach (Color color in colors)
                Assert.Greater(color.maxColorComponent, 0.2f);
        }

        [Test]
        public void TheAlphaOverloadKeepsTheHueAndOnlyChangesOpacity()
        {
            Color opaque = LightingUiPalette.Resolve(UiStatusLevel.Warning);
            Color faded = LightingUiPalette.Resolve(UiStatusLevel.Warning, 0.55f);

            Assert.AreEqual(opaque.r, faded.r, 1e-5f);
            Assert.AreEqual(opaque.g, faded.g, 1e-5f);
            Assert.AreEqual(opaque.b, faded.b, 1e-5f);
            Assert.AreEqual(0.55f, faded.a, 1e-5f);
        }

        [Test]
        public void ShipHudStatesMapOntoTheSharedLevels()
        {
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Ship(ShipLightingState.Idle));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Ship(ShipLightingState.Damaged));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Ship(ShipLightingState.Disabled));
            Assert.AreEqual(UiStatusLevel.Working, LightingUiPalette.Ship(ShipLightingState.Repair));
        }

        [Test]
        public void ExtractionHudReadingsMapOntoTheSharedLevels()
        {
            Assert.AreEqual(UiStatusLevel.Working, LightingUiPalette.Structure(DrillLifecycle.Constructing));
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Structure(DrillLifecycle.Active));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Structure(DrillLifecycle.Disabled));

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Storage(10f, 100f));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Storage(80f, 100f));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Storage(100f, 100f));

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Dominance(DominanceState.Controlled));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Dominance(DominanceState.Contested));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Dominance(DominanceState.Lost));
        }

        [Test]
        public void NavigationHudReadingsMapOntoTheSharedLevels()
        {
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.CommunicationLink(true, true));
            Assert.AreEqual(UiStatusLevel.Caution, LightingUiPalette.CommunicationLink(false, true));
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.CommunicationLink(false, false));

            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Raid(RaidPhase.Warning));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Raid(RaidPhase.Active));
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Raid(RaidPhase.Resolved));
        }

        [Test]
        public void AttentionCardPrioritiesMapOntoTheSharedLevels()
        {
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Attention(0));
            Assert.AreEqual(UiStatusLevel.Caution, LightingUiPalette.Attention(1));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Attention(2));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Attention(3));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Attention(9));
        }

        // ---- information gates -----------------------------------------------------

        [Test]
        public void NoHudReadingRevealsSomethingThePlayerHasNotEarned()
        {
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Raid(RaidPhase.Scheduled),
                "a scheduled raid must not surface in any HUD");
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Dominance(DominanceState.Unowned),
                "an unowned system must read as nothing, not as a state");
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.CommunicationLink(false, false),
                "a disconnected link shows no data, so it shows no status colour either");

            // Inactive must be visually quiet so an unknown never draws the eye
            // the way a real state does.
            Assert.Less(LightingUiPalette.Resolve(UiStatusLevel.Inactive).maxColorComponent,
                LightingUiPalette.Resolve(UiStatusLevel.Warning).maxColorComponent);
        }

        // ---- colour is never the only carrier -----------------------------------------

        [Test]
        public void EveryConnectedHudStillWritesTextAlongsideItsColour()
        {
            foreach (string path in StatusHudPaths)
            {
                string source = ReadHud(path);

                // The contract is unchanged: colour must never be the only carrier.
                // Only the idiom moved. The legacy HUDs assigned .text or appended
                // to a StringBuilder; the redesign writes through a label handle
                // created by the one text factory, so both vocabularies count.
                bool writesText = source.Contains(".text")
                                  || source.Contains("Append")
                                  || source.Contains("SetText")
                                  || source.Contains(".Value =")
                                  || source.Contains("SetCaption")
                                  || source.Contains("NebulaUiFactory.Label");

                Assert.IsTrue(writesText,
                    $"{Path.GetFileName(path)} appears to have lost its text output — colour must never be the only carrier");
            }
        }

        [Test]
        public void DurabilityReadingsCarryAShapeAsWellAsAColour()
        {
            // Stronger than the text rule, and specific to the one place three
            // readings share a layout: Shield, Armor and Hull must be separable
            // without colour at all, which is what the shape glyphs are for.
            string vitals = ReadHud("Assets/Scripts/Nebula/UI/Hud/NebulaHudVitals.cs");

            foreach (string glyph in new[] { "ShapeGlyph.Shield", "ShapeGlyph.Armor", "ShapeGlyph.Hull" })
                Assert.IsTrue(vitals.Contains(glyph),
                    $"the vitals cluster no longer draws {glyph} — colour would be the only channel");
        }

        // ---- accessibility ---------------------------------------------------------------

        [Test]
        public void AccessibilitySettingsReachEveryHudThroughTheSharedPalette()
        {
            // Driven through High Contrast rather than Flash Reduction. The point of
            // this test is that an accessibility setting reaches the HUDs through the
            // one shared palette instead of each HUD keeping its own copy -- Flash
            // Reduction was only ever the vehicle, and it is deliberately no longer
            // one, because a flash limit now bounds the repeat rate and leaves
            // resting colours alone (see Lgt051). High Contrast is a setting that
            // genuinely does move the palette, so it can still carry the question.
            Color normalCritical = LightingUiPalette.Resolve(UiStatusLevel.Critical, 0.95f);
            LightingAccessibility.HighContrast = true;
            Color strongCritical = LightingUiPalette.Resolve(UiStatusLevel.Critical, 0.95f);

            Assert.AreNotEqual(normalCritical, strongCritical,
                "an accessibility setting did not reach the HUDs through the shared palette");
            Assert.AreEqual(0.95f, strongCritical.a, 1e-5f, "opacity is the HUD's business, not accessibility's");
            Assert.Greater(strongCritical.r, strongCritical.b, "the state stays recognisable");

            // And the setting that must NOT change a resting colour still does not.
            LightingAccessibility.HighContrast = false;
            LightingAccessibility.FlashReduction = true;
            Assert.AreEqual(normalCritical, LightingUiPalette.Resolve(UiStatusLevel.Critical, 0.95f),
                "flash reduction dimmed a resting critical readout instead of bounding the repeat rate");
        }

        [Test]
        public void NoAccessibilityCombinationMakesAHudUnreadable()
        {
            LightingAccessibility.PhotosensitivitySafeMode = true;
            LightingAccessibility.FlashReduction = true;
            LightingAccessibility.HighContrast = true;
            LightingAccessibility.LightIntensityScale = 0f; // clamped on write

            foreach (UiStatusLevel level in AllLevels)
                Assert.Greater(LightingUiPalette.Resolve(level, 0.95f).maxColorComponent, 0.2f, $"{level}");
        }

        // ---- preserved authorities ------------------------------------------------------------

        [Test]
        public void HudPaletteIntegrationDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
