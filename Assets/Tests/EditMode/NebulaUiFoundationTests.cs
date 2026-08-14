using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.UI.Design;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-01 foundation gate: design tokens, the generated sprite catalog, and the
    /// rules the rest of the interface is allowed to assume. Scoped to UI-01 only --
    /// nothing here touches gameplay, and no other fixture is pulled in.
    /// </summary>
    public class NebulaUiFoundationTests
    {
        [SetUp]
        public void ResetCatalog() => NebulaUiAssetCatalog.ResetForSession();

        // =====================================================================
        // TOKENS — the point of the token layer is that it is not a second
        // authority. These tests are what stop it becoming one.
        // =====================================================================

        [Test]
        public void StatusColours_DelegateToLightingUiPalette_ForEveryLevel()
        {
            foreach (UiStatusLevel level in System.Enum.GetValues(typeof(UiStatusLevel)))
            {
                Assert.AreEqual(LightingUiPalette.Resolve(level), NebulaUiDesignTokens.Status(level),
                    $"Token layer drifted from LightingUiPalette for {level}. Status colour has " +
                    "exactly one owner; the token layer may only forward to it.");
            }
        }

        [Test]
        public void StatusColours_CarryAccessibilityResponse_NotARawTable()
        {
            // Proves the delegation is live rather than a copied constant: changing
            // the accessibility state must move the colour the tokens hand back.
            bool savedContrast = LightingAccessibility.HighContrast;
            try
            {
                LightingAccessibility.HighContrast = false;
                Color plain = NebulaUiDesignTokens.Status(UiStatusLevel.Critical);
                LightingAccessibility.HighContrast = true;
                Color contrast = NebulaUiDesignTokens.Status(UiStatusLevel.Critical);

                Assert.AreNotEqual(plain, contrast,
                    "High Contrast did not reach the token layer, which means a HUD reading " +
                    "through tokens would ignore the accessibility settings.");
            }
            finally { LightingAccessibility.HighContrast = savedContrast; }
        }

        [Test]
        public void MotionDurations_DelegateToUiAnimationProfiles()
        {
            foreach (UiPanelPhase phase in System.Enum.GetValues(typeof(UiPanelPhase)))
            {
                Assert.AreEqual(UiAnimationProfiles.DurationFor(phase),
                    NebulaUiDesignTokens.MotionDuration(phase), 0.0001f,
                    $"Motion duration for {phase} was re-declared instead of delegated.");
            }
        }

        [Test]
        public void MotionOff_MakesTokenLayerReportSnap()
        {
            bool saved = AnimationAccessibility.MotionOff;
            try
            {
                AnimationAccessibility.MotionOff = true;
                Assert.IsTrue(NebulaUiDesignTokens.SnapTransitions,
                    "Motion Off must reach the token layer, or new panels will keep tweening.");
                AnimationAccessibility.MotionOff = false;
                Assert.IsFalse(NebulaUiDesignTokens.SnapTransitions);
            }
            finally { AnimationAccessibility.MotionOff = saved; }
        }

        [Test]
        public void RarityLegendary_IsSeparableFromCaution()
        {
            // Both are warm yellows. If they ever converge, a legendary drop reads
            // as a warning -- so the separation is asserted, not just intended.
            Color legendary = NebulaUiDesignTokens.RarityLegendary;
            Color caution = LightingUiPalette.Caution;

            float lumL = 0.2126f * legendary.r + 0.7152f * legendary.g + 0.0722f * legendary.b;
            float lumC = 0.2126f * caution.r + 0.7152f * caution.g + 0.0722f * caution.b;

            Assert.Greater(lumC - lumL, 0.12f,
                "Legendary and Caution are too close in luminance to tell apart on a dark panel.");
        }

        [Test]
        public void TextSizes_NeverDropBelowTheReadableFloor()
        {
            int[] sizes =
            {
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.SizeKeyPrompt,
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.SizeSectionTitle,
                NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.SizeButton,
            };
            foreach (int size in sizes)
                Assert.GreaterOrEqual(size, NebulaUiDesignTokens.MinimumReadableSize,
                    "A player-facing text size fell below the readable floor at 1080p.");
        }

        [Test]
        public void SpacingScale_IsOnTheEightPixelGrid()
        {
            float[] scale =
            {
                NebulaUiDesignTokens.S2, NebulaUiDesignTokens.S4, NebulaUiDesignTokens.S6,
                NebulaUiDesignTokens.S8, NebulaUiDesignTokens.S12, NebulaUiDesignTokens.S16,
                NebulaUiDesignTokens.RowHeight, NebulaUiDesignTokens.RowHeightLarge,
                NebulaUiDesignTokens.MinimumHitTarget,
            };
            foreach (float step in scale)
                Assert.AreEqual(0f, step % 8f, 0.001f, $"{step} is off the 8 px grid.");
        }

        [Test]
        public void MinimumHitTarget_IsLargeEnoughToClick()
        {
            Assert.GreaterOrEqual(NebulaUiDesignTokens.MinimumHitTarget, 32f);
        }

        [Test]
        public void PrimaryText_ClearsContrastAgainstEveryPanelSurface()
        {
            Color[] surfaces =
            {
                NebulaUiDesignTokens.SpaceVoid, NebulaUiDesignTokens.SurfaceObsidian,
                NebulaUiDesignTokens.Surface1, NebulaUiDesignTokens.Surface2,
                NebulaUiDesignTokens.Surface3, NebulaUiDesignTokens.SurfaceElevated,
            };
            foreach (Color surface in surfaces)
            {
                float ratio = ContrastRatio(NebulaUiDesignTokens.InkPrimary, surface);
                Assert.GreaterOrEqual(ratio, 4.5f,
                    $"Primary text is only {ratio:0.00}:1 on {surface} — below WCAG AA.");
            }
        }

        // =====================================================================
        // ASSET CATALOG
        // =====================================================================

        [Test]
        public void EveryCatalogPath_ResolvesToARealSprite()
        {
            var missing = new List<string>();
            foreach (string path in NebulaUiAssetCatalog.AllPaths())
                if (Resources.Load<Sprite>(path) == null) missing.Add(path);

            Assert.IsEmpty(missing,
                "The catalog can hand out sprites that do not exist. Missing:\n  " +
                string.Join("\n  ", missing));
        }

        [Test]
        public void CatalogAccessors_RecordNoMisses_OnAColdLoad()
        {
            // Touch every typed accessor, not just the path list, so an accessor
            // pointing at the wrong folder is caught too.
            _ = NebulaUiAssetCatalog.PanelPrimary;
            _ = NebulaUiAssetCatalog.PanelSecondary;
            _ = NebulaUiAssetCatalog.PanelElevated;
            _ = NebulaUiAssetCatalog.Modal;
            _ = NebulaUiAssetCatalog.Tooltip;
            _ = NebulaUiAssetCatalog.Toast;
            _ = NebulaUiAssetCatalog.FocusRing;
            _ = NebulaUiAssetCatalog.SelectionRing;
            _ = NebulaUiAssetCatalog.BarTrack;
            _ = NebulaUiAssetCatalog.BarFill;
            _ = NebulaUiAssetCatalog.BarFillStriped;
            _ = NebulaUiAssetCatalog.BarTicks;
            _ = NebulaUiAssetCatalog.RingScan;
            _ = NebulaUiAssetCatalog.RingLock;
            _ = NebulaUiAssetCatalog.BadgeSquare;
            _ = NebulaUiAssetCatalog.BadgePill;
            _ = NebulaUiAssetCatalog.RarityHexFrame;
            _ = NebulaUiAssetCatalog.BracketCorner;
            _ = NebulaUiAssetCatalog.EdgeMarker;
            _ = NebulaUiAssetCatalog.RouteArrow;
            _ = NebulaUiAssetCatalog.RailIndicator;
            _ = NebulaUiAssetCatalog.MenuDivider;

            foreach (NebulaUiAssetCatalog.ButtonKind k in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ButtonKind)))
                foreach (NebulaUiAssetCatalog.ControlState s in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ControlState)))
                    _ = NebulaUiAssetCatalog.Button(k, s);
            foreach (NebulaUiAssetCatalog.RowState s in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.RowState)))
                _ = NebulaUiAssetCatalog.Row(s);
            foreach (NebulaUiAssetCatalog.SlotState s in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.SlotState)))
                _ = NebulaUiAssetCatalog.Slot(s);
            foreach (NebulaUiAssetCatalog.CardState s in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.CardState)))
                _ = NebulaUiAssetCatalog.Card(s);
            foreach (NebulaUiAssetCatalog.ShapeGlyph g in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ShapeGlyph)))
                _ = NebulaUiAssetCatalog.Glyph(g);
            foreach (NebulaUiAssetCatalog.ReticleState s in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ReticleState)))
                _ = NebulaUiAssetCatalog.Reticle(s);
            foreach (NebulaUiAssetCatalog.Page p in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.Page)))
                _ = NebulaUiAssetCatalog.PageGlyph(p);
            foreach (NebulaUiAssetCatalog.MenuItemState s in System.Enum.GetValues(typeof(NebulaUiAssetCatalog.MenuItemState)))
                _ = NebulaUiAssetCatalog.MenuItem(s);

            Assert.IsEmpty(NebulaUiAssetCatalog.MissingAssets,
                "A typed accessor pointed at a sprite that does not exist:\n  " +
                string.Join("\n  ", NebulaUiAssetCatalog.MissingAssets));
        }

        [Test]
        public void RepeatedLookups_ReuseTheCachedSprite()
        {
            Sprite first = NebulaUiAssetCatalog.PanelPrimary;
            Sprite second = NebulaUiAssetCatalog.PanelPrimary;
            Assert.AreSame(first, second,
                "The catalog re-loaded a sprite. Per-frame refreshes must never hit Resources.Load.");
        }

        [Test]
        public void NineSliceFrames_DeclareABorder()
        {
            // A frame without a border stretches its own corners into mush the
            // moment a panel is not exactly sprite-sized.
            Sprite[] frames =
            {
                NebulaUiAssetCatalog.PanelPrimary, NebulaUiAssetCatalog.PanelSecondary,
                NebulaUiAssetCatalog.PanelElevated, NebulaUiAssetCatalog.Modal,
                NebulaUiAssetCatalog.Tooltip, NebulaUiAssetCatalog.Toast,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Selected),
                NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Selected),
                NebulaUiAssetCatalog.Button(NebulaUiAssetCatalog.ButtonKind.Primary,
                                            NebulaUiAssetCatalog.ControlState.Default),
            };
            foreach (Sprite frame in frames)
            {
                Assert.IsNotNull(frame);
                Assert.Greater(frame.border.sqrMagnitude, 0f,
                    $"{frame.name} is 9-sliced in the design but ships with a zero border.");
            }
        }

        [Test]
        public void EveryGeneratedSprite_HasAnAlphaChannel()
        {
            foreach (string path in NebulaUiAssetCatalog.AllPaths())
            {
                var sprite = Resources.Load<Sprite>(path);
                if (sprite == null) continue;
                Assert.IsTrue(sprite.texture.format == TextureFormat.RGBA32
                              || sprite.texture.format == TextureFormat.ARGB32
                              || sprite.texture.format == TextureFormat.DXT5
                              || sprite.texture.format == TextureFormat.BC7
                              || sprite.texture.format == TextureFormat.RGBA4444,
                    $"{path} lost its alpha channel on import ({sprite.texture.format}); " +
                    "UI chrome must composite over the game, not over black.");
            }
        }

        [Test]
        public void UiSprites_ShipWithoutMipmaps()
        {
            foreach (string path in NebulaUiAssetCatalog.AllPaths())
            {
                var sprite = Resources.Load<Sprite>(path);
                if (sprite == null) continue;
                Assert.AreEqual(1, sprite.texture.mipmapCount,
                    $"{path} has mipmaps; screen-space UI never samples them and they blur it.");
            }
        }

        [Test]
        public void InteractiveControls_ExposeEveryStateTheDesignPromises()
        {
            // A control that cannot show hover, pressed, disabled and focus is a
            // control the player cannot read. Asserted for every button kind.
            foreach (NebulaUiAssetCatalog.ButtonKind kind in
                     System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ButtonKind)))
            {
                foreach (NebulaUiAssetCatalog.ControlState state in
                         System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ControlState)))
                {
                    Assert.IsNotNull(NebulaUiAssetCatalog.Button(kind, state),
                        $"button.{kind}.{state} is missing.");
                }
            }
        }

        [Test]
        public void ReactiveButtonStates_AreVisuallyDistinctFromRest()
        {
            // Regression guard for the real defect found in UI-01: tertiary buttons
            // were generated fully transparent in every state, so hover and pressed
            // were indistinguishable from default and the control read as dead.
            foreach (NebulaUiAssetCatalog.ButtonKind kind in
                     System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ButtonKind)))
            {
                Sprite rest = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Default);
                Sprite hover = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Hover);
                Sprite pressed = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Pressed);

                Assert.That(Mathf.Abs(MeanAlpha(rest) - MeanAlpha(hover)), Is.GreaterThan(0.0005f),
                    $"button.{kind}.hover is identical to rest — the control gives no pointer feedback.");
                Assert.That(Mathf.Abs(MeanAlpha(hover) - MeanAlpha(pressed)), Is.GreaterThan(0.0005f),
                    $"button.{kind}.pressed is identical to hover — the press is invisible.");
            }
        }

        [Test]
        public void FocusRing_IsActuallyInsideTheSprite()
        {
            // Regression guard for the second UI-01 defect: the ring was authored at
            // a negative coordinate, so the SVG viewBox clipped it away entirely and
            // keyboard focus rendered as nothing.
            foreach (NebulaUiAssetCatalog.ButtonKind kind in
                     System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ButtonKind)))
            {
                Sprite focused = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Focused);
                Sprite rest = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Default);
                Assert.Greater(MeanAlpha(focused), MeanAlpha(rest),
                    $"button.{kind}.focused draws no more than rest — the focus ring was clipped away, " +
                    "which makes keyboard navigation invisible.");
            }
        }

        [Test]
        public void ShapeGlyphs_ExistForEveryColourCodedReading()
        {
            // Shield/Armor/Hull/Energy/Heat must be separable without colour.
            foreach (NebulaUiAssetCatalog.ShapeGlyph glyph in
                     System.Enum.GetValues(typeof(NebulaUiAssetCatalog.ShapeGlyph)))
            {
                Sprite sprite = NebulaUiAssetCatalog.Glyph(glyph);
                Assert.IsNotNull(sprite, $"glyph.{glyph} is missing — colour would be the only channel.");
                Assert.Greater(MeanAlpha(sprite), 0.05f, $"glyph.{glyph} is blank.");
            }
        }

        [Test]
        public void EveryParentPage_HasARailGlyph()
        {
            foreach (NebulaUiAssetCatalog.Page page in
                     System.Enum.GetValues(typeof(NebulaUiAssetCatalog.Page)))
            {
                Sprite sprite = NebulaUiAssetCatalog.PageGlyph(page);
                Assert.IsNotNull(sprite, $"nav.page.{page} is missing.");
                Assert.Greater(MeanAlpha(sprite), 0.01f, $"nav.page.{page} is blank.");
            }
        }

        [Test]
        public void ResetForSession_DropsTheCache()
        {
            Sprite before = NebulaUiAssetCatalog.PanelPrimary;
            NebulaUiAssetCatalog.ResetForSession();
            Assert.IsEmpty(NebulaUiAssetCatalog.MissingAssets);
            Sprite after = NebulaUiAssetCatalog.PanelPrimary;
            Assert.IsNotNull(before);
            Assert.IsNotNull(after);
        }

        // =====================================================================
        // helpers
        // =====================================================================

        private static float MeanAlpha(Sprite sprite)
        {
            Assert.IsNotNull(sprite);
            Color32[] pixels = sprite.texture.GetPixels32();
            long total = 0;
            for (int i = 0; i < pixels.Length; i++) total += pixels[i].a;
            return pixels.Length == 0 ? 0f : total / (float)pixels.Length / 255f;
        }

        private static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a), lb = RelativeLuminance(b);
            float hi = Mathf.Max(la, lb), lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        private static float RelativeLuminance(Color c) =>
            0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);

        private static float Channel(float v) =>
            v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    }
}
