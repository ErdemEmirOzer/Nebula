using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Modules;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// LGT-05.3: progression readings resolve through the shared status
    /// palette, and the final audit — no player-facing panel still keeps a
    /// status-colour table of its own.
    /// </summary>
    public class Lgt053ProgressionPaletteTests
    {
        /// <summary>
        /// Every player-facing surface that shows status. Re-pointed from the legacy
        /// panels to the redesigned interface (UI-09); the contract is unchanged.
        /// </summary>
        private static readonly string[] PlayerFacingStatusUi =
        {
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudVitals.cs",
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudNavigation.cs",
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudAlerts.cs",
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudContextBar.cs",
            "Assets/Scripts/Nebula/UI/Hud/NebulaHudPlanetContext.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaOverviewPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaProgressionPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaFleetPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaPlanetsPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaCombatPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaDiscoveryPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaResourcesPage.cs"
        };

        /// <summary>The two surfaces that carry progression readings after the redesign.</summary>
        private static readonly string[] ProgressionSurfaces =
        {
            "Assets/Scripts/Nebula/UI/Menu/NebulaProgressionPage.cs",
            "Assets/Scripts/Nebula/UI/Menu/NebulaOverviewPage.cs"
        };

        /// <summary>How the redesign reaches the palette; the token layer forwards rather than copies.</summary>
        private const string PaletteAccessor = "NebulaUiDesignTokens.Status";

        /// <summary>Developer tools. Their distinct identity is the point, so they stay out of the status palette.</summary>
        private static readonly string[] DeveloperToolPaths =
        {
            "Assets/Scripts/Nebula/UI/SectorDebugHud.cs",
            "Assets/Scripts/Nebula/UI/FlightTestSpeedHud.cs"
        };

        /// <summary>Colour names that carry state, as opposed to layout chrome.</summary>
        private static readonly string[] StatusColorNames =
        {
            "TextColor", "WarnColor", "CritColor", "CriticalColor", "DimColor"
        };

        [TearDown]
        public void TearDown() => LightingAccessibility.ResetToDefaults();

        private static string ReadUi(string relativePath)
        {
            string full = Path.Combine(Application.dataPath, "..", relativePath);
            Assert.IsTrue(File.Exists(full), $"{relativePath} is missing — the UI list is stale");
            return File.ReadAllText(full);
        }

        private static UpgradeRejectReason[] AllReasons => (UpgradeRejectReason[])Enum.GetValues(typeof(UpgradeRejectReason));

        // ---- progression is connected ---------------------------------------

        [Test]
        public void BothProgressionPanelsResolveThroughTheSharedPalette()
        {
            foreach (string path in ProgressionSurfaces)
            {
                string source = ReadUi(path);
                Assert.IsTrue(source.Contains(PaletteAccessor) || source.Contains("LightingUiPalette"),
                    $"{Path.GetFileName(path)} does not read the shared status palette");
            }
        }

        // ---- the real progression states ---------------------------------------

        [Test]
        public void EveryRealUpgradeRejectReasonHasAStatusReading()
        {
            Assert.AreEqual(11, AllReasons.Length,
                "the real UpgradeRejectReason set — if this changed, the mapping needs revisiting");

            var levels = (UiStatusLevel[])Enum.GetValues(typeof(UiStatusLevel));
            foreach (UpgradeRejectReason reason in AllReasons)
                Assert.IsTrue(levels.Contains(LightingUiPalette.Progression(reason)),
                    $"{reason} has no status reading");
        }

        [Test]
        public void AnAvailableOrMaxedUpgradeBothReadAsNominal()
        {
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Progression(UpgradeRejectReason.None),
                "an affordable upgrade is simply available");
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Progression(UpgradeRejectReason.AlreadyMaxTier),
                "a maxed module is finished, not blocked");
        }

        [Test]
        public void ALockedBranchIsQuietRatherThanAlarming()
        {
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Progression(UpgradeRejectReason.LevelTooLow),
                "a branch the player has not reached yet must not shout");

            Assert.Less(
                LightingUiPalette.Resolve(UiStatusLevel.Inactive).maxColorComponent,
                LightingUiPalette.Resolve(UiStatusLevel.Warning).maxColorComponent,
                "locked must read quieter than a real warning");
        }

        [Test]
        public void AMissingRequirementCautionsAndMissingResourcesWarn()
        {
            Assert.AreEqual(UiStatusLevel.Caution, LightingUiPalette.Progression(UpgradeRejectReason.MilestoneNotMet),
                "a missing milestone is something to work toward");
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Progression(UpgradeRejectReason.InsufficientPoints));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Progression(UpgradeRejectReason.InsufficientResources),
                "the player understands this one and can act on it");
        }

        [Test]
        public void OnlyAGenuinelyBrokenRequestIsCritical()
        {
            foreach (UpgradeRejectReason broken in new[]
                     {
                         UpgradeRejectReason.UnknownModule,
                         UpgradeRejectReason.TierSkip,
                         UpgradeRejectReason.NotImplemented
                     })
                Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Progression(broken), $"{broken}");

            // Everything a player can resolve by playing must not be critical.
            foreach (UpgradeRejectReason playable in new[]
                     {
                         UpgradeRejectReason.None,
                         UpgradeRejectReason.AlreadyMaxTier,
                         UpgradeRejectReason.LevelTooLow,
                         UpgradeRejectReason.MilestoneNotMet,
                         UpgradeRejectReason.InsufficientPoints,
                         UpgradeRejectReason.InsufficientResources,
                         UpgradeRejectReason.QueueBusy,
                         UpgradeRejectReason.ScreenPaused
                     })
                Assert.AreNotEqual(UiStatusLevel.Critical, LightingUiPalette.Progression(playable),
                    $"{playable} is an ordinary play state, not an error");
        }

        [Test]
        public void AnUpgradeInProgressReadsAsWorkingWhateverElseWouldHaveBlockedIt()
        {
            foreach (UpgradeRejectReason reason in AllReasons)
                Assert.AreEqual(UiStatusLevel.Working,
                    LightingUiPalette.Progression(reason, upgradeActive: true),
                    $"{reason}: a committed upgrade must read as busy, not as barred");

            Assert.AreEqual(UiStatusLevel.Working, LightingUiPalette.Progression(UpgradeRejectReason.QueueBusy),
                "another job holding the queue is work in progress too");
        }

        // ---- final audit -------------------------------------------------------------

        [Test]
        public void NoPlayerFacingPanelStillDeclaresItsOwnStatusColour()
        {
            var offenders = new List<string>();

            foreach (string path in PlayerFacingStatusUi)
            {
                string source = ReadUi(path);
                foreach (string name in StatusColorNames)
                    if (Regex.IsMatch(source, @"static\s+readonly\s+Color\s+" + name + @"\s*="))
                        offenders.Add($"{Path.GetFileName(path)}.{name}");
            }

            CollectionAssert.IsEmpty(offenders,
                "these player-facing panels still invent their own status colours: " + string.Join(", ", offenders));
        }

        [Test]
        public void EveryPlayerFacingStatusPanelReadsTheSharedPalette()
        {
            foreach (string path in PlayerFacingStatusUi)
            {
                string source = ReadUi(path);
                Assert.IsTrue(source.Contains(PaletteAccessor) || source.Contains("LightingUiPalette"),
                    $"{Path.GetFileName(path)} is not connected to the shared palette");
            }
        }

        [Test]
        public void DeveloperToolsKeepTheirOwnDeliberatelyDistinctIdentity()
        {
            foreach (string path in DeveloperToolPaths)
                Assert.IsTrue(Regex.IsMatch(ReadUi(path), @"static\s+readonly\s+Color\s+TextColor\s*="),
                    $"{Path.GetFileName(path)} is a developer tool and must stay visually distinct from the real HUD");
        }

        [Test]
        public void LayoutChromeWasLeftAloneAcrossTheProgressionPanels()
        {
            // Row, surface and border colours are chrome, not state. In the redesign
            // that separation lives in the token file: chrome is a plain constant
            // there, status forwards to the palette. Folding them together would
            // make every row change colour with the ship's condition.
            // Checked on what each token resolves THROUGH rather than on how it is
            // stored -- see the matching guard in Lgt052. High Contrast turned this
            // chrome into `static Color X => Pick(normal, contrast)`, an accessibility
            // switch that has nothing to do with the ship's condition, so a form-based
            // proxy failed on correct code while still admitting a status lookup
            // written as a readonly field.
            string tokens = ReadUi("Assets/Scripts/Nebula/UI/Design/NebulaUiDesignTokens.cs");
            foreach (string chrome in new[] { "Surface2", "Surface3", "SelectedFill", "HoverFill", "Celebration" })
            {
                Assert.IsTrue(Regex.IsMatch(tokens, @"Color\s+" + chrome + @"\s*(=>|=)"),
                    $"{chrome} is gone from the token file; this guard needs to follow it");
                Assert.IsFalse(
                    Regex.IsMatch(tokens, @"Color\s+" + chrome + @"\s*(=>|=)[^;]*LightingUiPalette"),
                    $"{chrome} is layout chrome and was folded into the status palette");
            }
        }

        // ---- colour is never alone ------------------------------------------------------

        [Test]
        public void TheProgressionPanelsStillWriteTextAlongsideTheirColour()
        {
            foreach (string path in ProgressionSurfaces)
            {
                string source = ReadUi(path);

                // Same contract, moved idiom: the legacy panels appended to a
                // StringBuilder or assigned .text; the redesign writes through a
                // label handle from the one text factory.
                bool writesText = source.Contains("Append")
                                  || source.Contains(".text")
                                  || source.Contains(".Value =")
                                  || source.Contains("SetCaption")
                                  || source.Contains("NebulaUiFactory.Label");

                Assert.IsTrue(writesText,
                    $"{Path.GetFileName(path)} must keep its text — colour is never the only carrier");
            }
        }

        // ---- accessibility ----------------------------------------------------------------

        [Test]
        public void AccessibilityReachesTheProgressionReadingsToo()
        {
            // High Contrast is the vehicle, for the reason given in Lgt051: a flash
            // limit now bounds the repeat rate and deliberately leaves resting
            // colours alone, so Flash Reduction can no longer answer "does a setting
            // reach these readings". Both halves are still asserted.
            Color normal = LightingUiPalette.Resolve(UiStatusLevel.Warning, 0.95f);
            LightingAccessibility.HighContrast = true;
            Color strong = LightingUiPalette.Resolve(UiStatusLevel.Warning, 0.95f);

            Assert.AreNotEqual(normal, strong,
                "an accessibility setting never reached the progression readings");
            Assert.AreEqual(0.95f, strong.a, 1e-5f);
            Assert.Greater(strong.maxColorComponent, 0.2f, "an unaffordable upgrade must still be readable");

            LightingAccessibility.HighContrast = false;
            LightingAccessibility.FlashReduction = true;
            Assert.AreEqual(normal, LightingUiPalette.Resolve(UiStatusLevel.Warning, 0.95f),
                "flash reduction dimmed a resting progression readout");
        }

        [Test]
        public void EveryProgressionStateStaysLegibleUnderEveryAccessibilityCombination()
        {
            LightingAccessibility.PhotosensitivitySafeMode = true;
            LightingAccessibility.FlashReduction = true;
            LightingAccessibility.HighContrast = true;
            LightingAccessibility.LightIntensityScale = 0f; // clamped on write

            foreach (UpgradeRejectReason reason in AllReasons)
                Assert.Greater(
                    LightingUiPalette.Resolve(LightingUiPalette.Progression(reason), 0.95f).maxColorComponent, 0.2f,
                    $"{reason} became unreadable");
        }

        // ---- preserved authorities -------------------------------------------------------------

        [Test]
        public void ProgressionPaletteWorkDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
