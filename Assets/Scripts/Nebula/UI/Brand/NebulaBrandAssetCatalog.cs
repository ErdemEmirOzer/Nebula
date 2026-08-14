using System.Collections.Generic;
using UnityEngine;
using Nebula.Visuals;

namespace Nebula.UI.Brand
{
    /// <summary>
    /// Type-safe lookup for the delivered brand art, resolved once and cached.
    /// <para>
    /// Presentation only. Nothing here reads or writes a save, a campaign, a
    /// generation seed or a balance value, and nothing here is allowed to become
    /// content authority — a missing plate degrades the picture and never the game.
    /// The menu already had a dark backdrop and a text title before any of this
    /// existed, and that is exactly what it falls back to.
    /// </para>
    /// <para>
    /// The first pass drew a plate with the wordmark composited into it. The second
    /// delivery supplied the logo as its own transparent export and the key art in
    /// four authored aspects, so the normal path is now <b>clean plate + separate
    /// logo</b> and the combined plate survives only as the thing drawn when a real
    /// asset fails to resolve. <see cref="FallbacksUsed"/> is how a test tells the
    /// two apart without production behaving differently under test.
    /// </para>
    /// </summary>
    public static class NebulaBrandAssetCatalog
    {
        /// <summary>Root under <c>Resources/</c>. Matches the staged hierarchy.</summary>
        public const string Root = "UI/Brand";

        // ---- taxonomy --------------------------------------------------------

        /// <summary>
        /// Which cut of the mark. <see cref="Flat"/> is the unlit vector lockup the
        /// delivery ships alongside the effects version; it is what High Contrast
        /// wants, because a metallic bevel with a coloured underglow is exactly the
        /// kind of low-contrast rendering that setting exists to remove.
        /// </summary>
        public enum MarkStyle { Effects, Flat }

        /// <summary>Mark resolution tier. Logos are 2048/1024/512/256, emblems 1024/512/256/128.</summary>
        public enum MarkQuality { High, Medium, Low, Compact }

        /// <summary>Which authored key-art composition. Each is a separate export, never a stretch.</summary>
        public enum KeyArtAspect { SixteenByNine, SixteenByTen, UltraWide }

        /// <summary>The 16:9 ladder. The other two aspects have one authored size each.</summary>
        public enum KeyArtDetail { Low, Hd, Uhd }

        /// <summary>Which loading sheet. <see cref="LowMemory"/> is the 128px set.</summary>
        public enum LoadingFidelity { Full, LowMemory }

        /// <summary>
        /// What the loading surface is allowed to do. <see cref="Static"/> is a
        /// single plate and no playback at all — not a slowed-down loop.
        /// </summary>
        public enum LoadingPresentation { Animated, ReducedAnimated, Static }

        /// <summary>Splash source aspect. Build-time only; see <see cref="SplashAssetPath"/>.</summary>
        public enum SplashAspect { SixteenByNine, SixteenByTen }

        // ---- the delivery's own numbers --------------------------------------
        //
        // From nebula-loading-depth-unity.json and its Reduced Motion sibling.
        // Duplicated here rather than parsed at runtime because playback must not
        // depend on a JSON file being shipped, and asserted against both JSONs by
        // NebulaBrandAssetTests so the copies cannot drift.

        /// <summary>Frames in the loop. The same for normal and reduced.</summary>
        public const int LoadingFrameCount = 75;

        /// <summary>Columns in the sheet, left to right.</summary>
        public const int LoadingColumns = 15;

        /// <summary>Rows in the sheet, top to bottom.</summary>
        public const int LoadingRows = 5;

        /// <summary>Frames per second the loop was authored at.</summary>
        public const float LoadingFps = 30f;

        /// <summary>Wall-clock length of one loop, in seconds.</summary>
        public const float LoadingLoopSeconds = LoadingFrameCount / LoadingFps;   // 2.5

        /// <summary>
        /// How many cells each real Reduced Motion frame occupies.
        /// <para>
        /// The reduced export encodes 25 real frames and repeats each across three
        /// consecutive cells, so the sheet is still 75 cells at 30fps and playback
        /// needs no second clock — the picture simply changes a third as often. No
        /// interpolation and no invented frames.
        /// </para>
        /// </summary>
        public const int ReducedCellRepeat = 3;

        /// <summary>Distinct pictures in the reduced loop.</summary>
        public const int ReducedDistinctFrames = LoadingFrameCount / ReducedCellRepeat;   // 25

        // ---- asset paths -----------------------------------------------------

        private const string KeyArtFolder = Root + "/KeyArt";
        private const string LoadingFolder = Root + "/Loading";
        private const string LogoFolder = Root + "/Logo";
        private const string EmblemFolder = Root + "/Emblem";

        /// <summary>
        /// Where the app icon set, the splash plates and the native containers live —
        /// deliberately <b>outside</b> Resources.
        /// <para>
        /// They were under Resources in the first pass, which put roughly 5MB of icon
        /// into the player twice: once as the executable's icon and again as a
        /// loadable resource nothing ever loaded. A call-site audit found no runtime
        /// consumer, so they moved here and the runtime accessors went with them —
        /// what is left is a path, for the editor installer and the tests that check
        /// what it bound. A runtime <c>Sprite</c> accessor for an asset that is not
        /// in Resources would simply return null forever.
        /// </para>
        /// </summary>
        public const string BrandSourceFolder = "Assets/BrandSource";

        /// <inheritdoc cref="BrandSourceFolder"/>
        public const string IdentityAssetFolder = BrandSourceFolder + "/Identity";

        /// <summary>Native icon containers, for the build seam and its verification.</summary>
        public const string NativeAssetFolder = BrandSourceFolder + "/Native";

        private static int MarkPixels(bool logo, MarkQuality quality) => quality switch
        {
            MarkQuality.High => logo ? 2048 : 1024,
            MarkQuality.Medium => logo ? 1024 : 512,
            MarkQuality.Low => logo ? 512 : 256,
            _ => logo ? 256 : 128,
        };

        private static string LogoPath(MarkStyle style, MarkQuality quality) =>
            $"{LogoFolder}/brand.logo.{(style == MarkStyle.Effects ? "effects" : "flat")}.{MarkPixels(true, quality)}";

        private static string EmblemPath(MarkStyle style, MarkQuality quality) =>
            $"{EmblemFolder}/brand.emblem.{(style == MarkStyle.Effects ? "effects" : "flat")}.{MarkPixels(false, quality)}";

        private static string CleanKeyArtPath(KeyArtAspect aspect, KeyArtDetail detail) => aspect switch
        {
            KeyArtAspect.UltraWide => KeyArtFolder + "/brand.keyart.clean.ultrawide",
            KeyArtAspect.SixteenByTen => KeyArtFolder + "/brand.keyart.clean.wide16x10",
            _ => KeyArtFolder + "/brand.keyart.clean." + detail switch
            {
                KeyArtDetail.Low => "low",
                KeyArtDetail.Uhd => "uhd",
                _ => "hd",
            },
        };

        /// <summary>
        /// The pre-delivery plate with the wordmark baked in.
        /// <para>
        /// Not on the normal path any more. It is what gets drawn if a clean plate or
        /// the logo fails to resolve, because a menu showing the composition minus its
        /// title is a worse failure than one showing the older composition.
        /// </para>
        /// </summary>
        private const string CombinedFallbackPath = KeyArtFolder + "/brand.keyart.logo.hd";

        private static string LoadingSheetPath(LoadingPresentation presentation, LoadingFidelity fidelity)
        {
            string family = presentation == LoadingPresentation.ReducedAnimated ? "reduced" : "sheet";
            return $"{LoadingFolder}/brand.loading.{family}.{(fidelity == LoadingFidelity.Full ? "256" : "128")}";
        }

        // ---- miss reporting and behaviour probes ------------------------------

        private static readonly List<string> Missing = new List<string>();

        /// <summary>
        /// Brand assets asked for that did not exist. Test-facing; empty is the only
        /// passing state for the assets this integration ships.
        /// </summary>
        public static IReadOnlyList<string> MissingAssets => Missing;

        /// <summary>
        /// How many times a resolver had to substitute something for an asset that
        /// did not load.
        /// <para>
        /// The normal runtime path must leave this at zero. It is a plain counter
        /// rather than a test hook: nothing branches on it, so a fixture reading it
        /// is observing production rather than steering it.
        /// </para>
        /// </summary>
        public static int FallbacksUsed { get; private set; }

        /// <summary>Test-facing: whether anything is currently drawing the combined plate.</summary>
        public static bool CombinedLogoKeyArtUsed { get; private set; }

        /// <summary>Test-facing: whether Reduced Motion had to fall back to the still plate.</summary>
        public static bool ReducedUsesStatic { get; private set; }

        /// <summary>Motion Off always shows the still plate. Stated as a probe so a test can pin it.</summary>
        public static bool MotionOffUsesStatic =>
            PresentationFor(motionOff: true, reducedMotion: false) == LoadingPresentation.Static;

        private static T Required<T>(string path) where T : Object
        {
            var asset = Resources.Load<T>(path);
            if (asset == null && !Missing.Contains(path)) Missing.Add(path);
            return asset;
        }

        private static T Optional<T>(string path) where T : Object => Resources.Load<T>(path);

        // ---- single-resident slots -------------------------------------------
        //
        // Key art, logo and emblem each keep exactly one texture resident. The 4K
        // plate is 8.3MB on the GPU and the uncompressed 2048 logo is 4.7MB; holding
        // the whole ladder would cost more than the rest of the interface put
        // together, and only one of each is ever drawn.
        //
        // Release is always Resources.UnloadAsset on the specific texture, never
        // UnloadUnusedAssets: the sweeping version walks every live object and has
        // been timed at 64 seconds in this project's capture fixtures.

        private sealed class ResidentSlot
        {
            private Sprite resident;
            private string residentPath;

            private Sprite superseded;

            public Sprite Resolve(string path, bool required)
            {
                if (resident != null && residentPath == path) return resident;

                Sprite loaded = required ? Required<Sprite>(path) : Optional<Sprite>(path);
                if (loaded == null) return null;

                // The outgoing sprite is remembered, not freed. Whatever is drawing it
                // still holds the reference at this instant, and Unity simply declines
                // to unload an asset that is still referenced -- so releasing here
                // looks like it worked and quietly leaves the old texture resident.
                if (resident != null && resident != loaded) superseded = resident;
                resident = loaded;
                residentPath = path;
                return loaded;
            }

            /// <summary>
            /// Frees whatever the last <see cref="Resolve"/> displaced. Called by the
            /// view once its Image already points at the new sprite -- releasing a
            /// texture something is still drawing is how you get a white rectangle
            /// where the art was, and releasing it too early does nothing at all.
            /// </summary>
            public void ReleaseSuperseded()
            {
                if (superseded == null) { superseded = null; return; }
                if (superseded != resident && superseded.texture != null)
                    Resources.UnloadAsset(superseded.texture);
                superseded = null;
            }

            public void Forget() { resident = null; residentPath = null; superseded = null; }

            public string Path => residentPath;
        }

        private static readonly ResidentSlot KeyArtSlot = new ResidentSlot();
        private static readonly ResidentSlot LogoSlot = new ResidentSlot();
        private static readonly ResidentSlot EmblemSlot = new ResidentSlot();

        // ---- key art ---------------------------------------------------------

        /// <summary>
        /// The clean plate for an aspect, with the combined plate as the failure
        /// fallback. One plate resident at a time.
        /// </summary>
        public static Sprite KeyArt(KeyArtAspect aspect, KeyArtDetail detail)
        {
            Sprite clean = KeyArtSlot.Resolve(CleanKeyArtPath(aspect, detail), required: true);
            if (clean != null)
            {
                CombinedLogoKeyArtUsed = false;
                return clean;
            }

            Sprite combined = KeyArtSlot.Resolve(CombinedFallbackPath, required: true);
            if (combined == null) return null;

            FallbacksUsed++;
            CombinedLogoKeyArtUsed = true;
            return combined;
        }

        /// <summary>Test-facing: the Resources path of the plate currently resident.</summary>
        public static string ResidentKeyArtPath => KeyArtSlot.Path;

        /// <summary>
        /// Which authored composition a viewport wants.
        /// <para>
        /// Authored aspects, not a stretch: the 21:9 export is not the 16:9 one made
        /// wider, it carries a dark left extension that is new canvas, and the 16:10
        /// export adds bands above and below. Choosing by nearest authored aspect is
        /// what keeps the hero ship, the planet and the black hole at the proportions
        /// they were painted at.
        /// </para>
        /// </summary>
        public static KeyArtAspect AspectFor(int width, int height)
        {
            float aspect = height <= 0 ? 16f / 9f : (float)width / height;
            if (aspect >= 2.15f) return KeyArtAspect.UltraWide;
            if (aspect >= 1.55f && aspect <= 1.68f) return KeyArtAspect.SixteenByTen;
            return KeyArtAspect.SixteenByNine;
        }

        // ---- quality tier ----------------------------------------------------

        /// <summary>
        /// The project's own Low/Medium/High tier for a Unity quality level, resolved
        /// from the level's <b>authored name</b>.
        /// <para>
        /// Every selector below used to compare the raw ladder index — <c>qualityLevel
        /// &lt;= 3</c> — and Unity's default ladder puts High at index 3, so the level
        /// the project calls High resolved as if it were Medium. That is what made a
        /// 3840x2160 High display take the 1920x1080 plate and put the 2048 wordmark
        /// out of reach of every shipping configuration.
        /// </para>
        /// <para>
        /// An index is not a tier: it is a position in a list the project is free to
        /// reorder, extend or shorten in ProjectSettings, and nothing about that edit
        /// would announce that the brand suddenly picks different art. The name is the
        /// stable fact, so it decides. <see cref="LightingQuality"/> is the type-safe
        /// tier this project already ships; this is a second reading of the same
        /// ladder for presentation art, never a change to the lighting budget that
        /// <see cref="LightingQualityProfile.FromUnityQualityLevel"/> derives.
        /// </para>
        /// <para>
        /// A level whose name is not one we know falls back to its <i>relative</i>
        /// position on the live ladder rather than to an absolute index, so a
        /// renamed or resized ladder still degrades in the right direction instead of
        /// collapsing onto one tier.
        /// </para>
        /// </summary>
        public static LightingQuality DisplayTierFor(int qualityLevel) =>
            DisplayTierFor(qualityLevel, QualitySettings.names);

        /// <inheritdoc cref="DisplayTierFor(int)"/>
        internal static LightingQuality DisplayTierFor(int qualityLevel, string[] ladderNames)
        {
            string name = ladderNames != null && qualityLevel >= 0 && qualityLevel < ladderNames.Length
                ? ladderNames[qualityLevel]
                : null;

            if (TryTierFromName(name, out LightingQuality named)) return named;

            int rungs = ladderNames?.Length ?? 0;
            if (rungs <= 1 || qualityLevel < 0 || qualityLevel >= rungs) return LightingQuality.Medium;

            // Relative position, not an index: the top of any ladder is High and the
            // bottom is Low however many rungs the project decides to ship.
            float position = (float)qualityLevel / (rungs - 1);
            if (position <= 0.2f) return LightingQuality.Low;
            if (position < 0.6f) return LightingQuality.Medium;
            return LightingQuality.High;
        }

        /// <summary>
        /// Unity's stock ladder names, plus the legacy set, mapped onto our three
        /// tiers. Longest-first so "Very High" can never be matched by "High".
        /// </summary>
        private static bool TryTierFromName(string name, out LightingQuality tier)
        {
            tier = LightingQuality.Medium;
            if (string.IsNullOrWhiteSpace(name)) return false;

            string trimmed = name.Trim();
            if (Is(trimmed, "Very High") || Is(trimmed, "Ultra") || Is(trimmed, "Fantastic")
                || Is(trimmed, "Beautiful") || Is(trimmed, "Maximum"))
            { tier = LightingQuality.High; return true; }

            if (Is(trimmed, "Very Low") || Is(trimmed, "Fastest") || Is(trimmed, "Lowest"))
            { tier = LightingQuality.Low; return true; }

            if (Is(trimmed, "High")) { tier = LightingQuality.High; return true; }
            if (Is(trimmed, "Medium") || Is(trimmed, "Simple") || Is(trimmed, "Good")
                || Is(trimmed, "Normal"))
            { tier = LightingQuality.Medium; return true; }
            if (Is(trimmed, "Low") || Is(trimmed, "Fast")) { tier = LightingQuality.Low; return true; }

            return false;
        }

        private static bool Is(string value, string candidate) =>
            string.Equals(value, candidate, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Which rung of the 16:9 ladder. Ignored for the other two aspects, which
        /// have one authored size each.
        /// </summary>
        public static KeyArtDetail DetailFor(int screenHeight, LightingQuality tier)
        {
            if (tier == LightingQuality.Low || screenHeight <= 720) return KeyArtDetail.Low;
            if (tier == LightingQuality.Medium || screenHeight <= 1080) return KeyArtDetail.Hd;
            return KeyArtDetail.Uhd;
        }

        /// <inheritdoc cref="DetailFor(int,LightingQuality)"/>
        public static KeyArtDetail DetailFor(int screenHeight, int qualityLevel) =>
            DetailFor(screenHeight, DisplayTierFor(qualityLevel));

        // ---- named accessors -------------------------------------------------
        //
        // The flat list the integration brief asks for. Each is the same resolver
        // underneath; they exist so a call site and a test can name one asset
        // without assembling enum arguments.

        public static Sprite KeyArtLow => KeyArt(KeyArtAspect.SixteenByNine, KeyArtDetail.Low);
        public static Sprite KeyArtHd => KeyArt(KeyArtAspect.SixteenByNine, KeyArtDetail.Hd);
        public static Sprite KeyArtUhd => KeyArt(KeyArtAspect.SixteenByNine, KeyArtDetail.Uhd);
        public static Sprite KeyArtSixteenByTen => KeyArt(KeyArtAspect.SixteenByTen, KeyArtDetail.Hd);
        public static Sprite KeyArtUltraWide => KeyArt(KeyArtAspect.UltraWide, KeyArtDetail.Hd);

        // ---- logo ------------------------------------------------------------

        /// <summary>
        /// The transparent wordmark. Effects normally, flat under High Contrast, and
        /// flat again if the effects export fails to resolve — a plain mark is a
        /// legitimate picture, a missing one is not.
        /// </summary>
        public static Sprite Logo(MarkStyle style, MarkQuality quality)
        {
            Sprite mark = LogoSlot.Resolve(LogoPath(style, quality), required: true);
            if (mark != null) return mark;

            Sprite flat = LogoSlot.Resolve(LogoPath(MarkStyle.Flat, quality), required: true);
            if (flat == null) return null;

            FallbacksUsed++;
            return flat;
        }

        public static Sprite LogoEffectsHigh => Logo(MarkStyle.Effects, MarkQuality.High);
        public static Sprite LogoEffectsMedium => Logo(MarkStyle.Effects, MarkQuality.Medium);
        public static Sprite LogoEffectsLow => Logo(MarkStyle.Effects, MarkQuality.Low);
        public static Sprite LogoEffectsCompact => Logo(MarkStyle.Effects, MarkQuality.Compact);
        public static Sprite LogoFlatHigh => Logo(MarkStyle.Flat, MarkQuality.High);
        public static Sprite LogoFlatMedium => Logo(MarkStyle.Flat, MarkQuality.Medium);
        public static Sprite LogoFlatLow => Logo(MarkStyle.Flat, MarkQuality.Low);
        public static Sprite LogoFlatCompact => Logo(MarkStyle.Flat, MarkQuality.Compact);

        /// <summary>Test-facing: the Resources path of the mark currently resident.</summary>
        public static string ResidentLogoPath => LogoSlot.Path;

        // ---- emblem ----------------------------------------------------------

        /// <summary>
        /// The standalone glyph, for small brand marks — the loading surface's own
        /// sign, a status chip. Never the app icon PNG: that is a build asset with a
        /// square opaque field, and using it as a runtime mark would print a rounded
        /// icon into the interface.
        /// </summary>
        public static Sprite Emblem(MarkStyle style, MarkQuality quality)
        {
            Sprite mark = EmblemSlot.Resolve(EmblemPath(style, quality), required: true);
            if (mark != null) return mark;

            Sprite flat = EmblemSlot.Resolve(EmblemPath(MarkStyle.Flat, quality), required: true);
            if (flat == null) return null;

            FallbacksUsed++;
            return flat;
        }

        public static Sprite EmblemEffectsHigh => Emblem(MarkStyle.Effects, MarkQuality.High);
        public static Sprite EmblemEffectsMedium => Emblem(MarkStyle.Effects, MarkQuality.Medium);
        public static Sprite EmblemEffectsLow => Emblem(MarkStyle.Effects, MarkQuality.Low);
        public static Sprite EmblemEffectsCompact => Emblem(MarkStyle.Effects, MarkQuality.Compact);
        public static Sprite EmblemFlatHigh => Emblem(MarkStyle.Flat, MarkQuality.High);
        public static Sprite EmblemFlatMedium => Emblem(MarkStyle.Flat, MarkQuality.Medium);
        public static Sprite EmblemFlatLow => Emblem(MarkStyle.Flat, MarkQuality.Low);
        public static Sprite EmblemFlatCompact => Emblem(MarkStyle.Flat, MarkQuality.Compact);

        // ---- mark selection --------------------------------------------------

        /// <summary>Flat under High Contrast, effects otherwise.</summary>
        public static MarkStyle StyleFor(bool highContrast) =>
            highContrast ? MarkStyle.Flat : MarkStyle.Effects;

        /// <inheritdoc cref="StyleFor(bool)"/>
        public static MarkStyle CurrentStyle() => StyleFor(LightingAccessibility.HighContrast);

        /// <summary>
        /// Mark tier from the quality tier and the viewport. The two together, not
        /// either alone: a 4K panel at Low quality is asking for less memory, and a
        /// 720p window at Ultra does not need a 2048px wordmark.
        /// <para>
        /// The 2048 rung starts at 2160p rather than at 1440p because the mark is
        /// drawn at the plate's scale, not the screen's: on the 16:9 profile it
        /// occupies 0.3708 of the plate width, so a 3840px plate draws it at 1424px
        /// and anything below that is already served by the 1024 export. Selecting
        /// 2048 for a 1600p or 1440p screen would resident 4.7MB to draw an 845px and
        /// a 673px mark respectively.
        /// </para>
        /// <para>
        /// Compact is a viewport rung rather than a quality one: a window 600px tall
        /// cannot show more than a 256px mark at any setting, and the rung below Low
        /// that used to select it does not exist in a three-tier profile.
        /// </para>
        /// </summary>
        public static MarkQuality MarkQualityFor(int screenHeight, LightingQuality tier)
        {
            if (screenHeight <= 600) return MarkQuality.Compact;
            if (tier == LightingQuality.Low || screenHeight <= 720) return MarkQuality.Low;
            if (tier == LightingQuality.Medium || screenHeight < 2160) return MarkQuality.Medium;
            return MarkQuality.High;
        }

        /// <inheritdoc cref="MarkQualityFor(int,LightingQuality)"/>
        public static MarkQuality MarkQualityFor(int screenHeight, int qualityLevel) =>
            MarkQualityFor(screenHeight, DisplayTierFor(qualityLevel));

        // ---- loading ---------------------------------------------------------

        private static readonly Dictionary<string, Sprite[]> FrameCache =
            new Dictionary<string, Sprite[]>(4);

        /// <summary>
        /// The 75 frames in playback order.
        /// <para>
        /// Sorted by the index baked into each sprite's name, not by the order
        /// <c>Resources.LoadAll</c> hands them back — that order is unspecified and
        /// in practice follows the asset database's, which is alphabetical over
        /// unpadded names and therefore puts frame 10 immediately after frame 1. The
        /// importer zero-pads for the same reason; this parses the number rather than
        /// trusting either.
        /// </para>
        /// <para>
        /// Cached per sheet. Playback indexes the array and never rebuilds it, so a
        /// loop that runs for a minute allocates nothing.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Sprite> LoadingFrames(LoadingPresentation presentation,
            LoadingFidelity fidelity)
        {
            string path = LoadingSheetPath(presentation, fidelity);
            if (FrameCache.TryGetValue(path, out Sprite[] cached)) return cached;

            Sprite[] loaded = Resources.LoadAll<Sprite>(path);

            Sprite[] ordered;
            if (loaded == null || loaded.Length == 0)
            {
                if (!Missing.Contains(path)) Missing.Add(path);
                ordered = System.Array.Empty<Sprite>();
            }
            else
            {
                ordered = new Sprite[loaded.Length];
                System.Array.Copy(loaded, ordered, loaded.Length);
                System.Array.Sort(ordered, (a, b) => FrameIndex(a.name).CompareTo(FrameIndex(b.name)));
            }

            FrameCache[path] = ordered;
            return ordered;
        }

        public static IReadOnlyList<Sprite> LoadingNormalHigh =>
            LoadingFrames(LoadingPresentation.Animated, LoadingFidelity.Full);
        public static IReadOnlyList<Sprite> LoadingNormalLow =>
            LoadingFrames(LoadingPresentation.Animated, LoadingFidelity.LowMemory);
        public static IReadOnlyList<Sprite> LoadingReducedHigh =>
            LoadingFrames(LoadingPresentation.ReducedAnimated, LoadingFidelity.Full);
        public static IReadOnlyList<Sprite> LoadingReducedLow =>
            LoadingFrames(LoadingPresentation.ReducedAnimated, LoadingFidelity.LowMemory);

        /// <summary>
        /// The number after the last underscore, which is the frame's place in the
        /// loop. A name without one sorts last rather than throwing — a stray sprite
        /// in the sheet should not take the menu down.
        /// </summary>
        internal static int FrameIndex(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return int.MaxValue;
            int cut = spriteName.LastIndexOf('_');
            if (cut < 0 || cut == spriteName.Length - 1) return int.MaxValue;
            return int.TryParse(spriteName.Substring(cut + 1), out int index) ? index : int.MaxValue;
        }

        /// <summary>The Motion Off plate. One frame, shown still.</summary>
        public static Sprite LoadingStatic => Required<Sprite>(LoadingFolder + "/brand.loading.static");

        /// <summary>
        /// What the loading surface may do, given the accessibility state.
        /// <para>
        /// Motion Off is absolute and gets the still plate. Reduced Motion now gets
        /// the reduced sheet, which is the point of the second delivery — the first
        /// pass had to fall back to the still plate because the reduced variant
        /// shipped only as an APNG and a WebP, neither of which Unity imports as a
        /// sheet. Photosensitivity Safe Mode is routed to reduced as well: it is not
        /// the same setting, but the one thing it must never get is the full
        /// high-frequency loop.
        /// </para>
        /// </summary>
        public static LoadingPresentation PresentationFor(bool motionOff, bool reducedMotion,
            bool photosensitivitySafe = false)
        {
            if (motionOff) return LoadingPresentation.Static;
            if (reducedMotion || photosensitivitySafe) return LoadingPresentation.ReducedAnimated;
            return LoadingPresentation.Animated;
        }

        /// <inheritdoc cref="PresentationFor(bool,bool,bool)"/>
        public static LoadingPresentation CurrentPresentation() => PresentationFor(
            AnimationAccessibility.MotionOff,
            AnimationAccessibility.ReducedMotion,
            LightingAccessibility.PhotosensitivitySafeMode);

        /// <summary>
        /// Which sheet to play. The 128px set on the Low tier, where the project has
        /// already decided memory is the binding constraint. Tier rather than index,
        /// for the reason <see cref="DisplayTierFor(int)"/> gives.
        /// </summary>
        public static LoadingFidelity FidelityFor(LightingQuality tier) =>
            tier == LightingQuality.Low ? LoadingFidelity.LowMemory : LoadingFidelity.Full;

        /// <inheritdoc cref="FidelityFor(LightingQuality)"/>
        public static LoadingFidelity FidelityFor(int qualityLevel) =>
            FidelityFor(DisplayTierFor(qualityLevel));

        /// <inheritdoc cref="FidelityFor(int)"/>
        public static LoadingFidelity CurrentFidelity() => FidelityFor(QualitySettings.GetQualityLevel());

        /// <summary>
        /// Records that a reduced request could not be served by a reduced sheet.
        /// Called by the surface, so the probe reflects what was actually drawn
        /// rather than what the table says should have been.
        /// </summary>
        internal static void NoteReducedFellBackToStatic(bool fellBack)
        {
            if (fellBack) FallbacksUsed++;
            ReducedUsesStatic = fellBack;
        }

        // ---- app identity (build-time only) ----------------------------------

        /// <summary>The sizes the delivery ships, largest first — the order PlayerSettings wants.</summary>
        public static readonly int[] IconSizes = { 1024, 512, 256, 128, 64, 32, 16 };

        /// <summary>
        /// The smallest delivered icon that covers a given slot.
        /// <para>
        /// Unity's standalone slots are not the delivered list — it asks for a 48 and
        /// there is no 48 — so a slot takes the smallest icon at least as large as it
        /// and downscales. Upscaling instead would put a soft 32 in the 48 slot,
        /// which is the one thing an icon set must not do.
        /// </para>
        /// </summary>
        public static int IconSizeFor(int slot)
        {
            int best = IconSizes[0];
            foreach (int size in IconSizes)
                if (size >= slot && size < best) best = size;
            return best;
        }

        /// <summary>Project path of an app icon. Build-time; these are not in Resources.</summary>
        public static string IconAssetPath(int size) => $"{IdentityAssetFolder}/brand.icon.{size}.png";

        /// <summary>
        /// Project path of a splash plate.
        /// <para>
        /// 1920x1080 is what PlayerSettings binds, because the standalone splash takes
        /// one landscape source. The 2560x1600 export is kept and reachable through
        /// this selector for a 16:10 boot surface — there is no such surface today, so
        /// it is a build asset and nothing pretends otherwise. Inventing a custom
        /// splash screen so the file could be called "used" would mean adding a wait
        /// the player did not need.
        /// </para>
        /// </summary>
        public static string SplashAssetPath(SplashAspect aspect) =>
            aspect == SplashAspect.SixteenByTen
                ? IdentityAssetFolder + "/brand.splash.16x10.png"
                : IdentityAssetFolder + "/brand.splash.16x9.png";

        /// <summary>Which splash source an aspect would want, if a surface ever chooses.</summary>
        public static SplashAspect SplashAspectFor(int width, int height) =>
            AspectFor(width, height) == KeyArtAspect.SixteenByTen
                ? SplashAspect.SixteenByTen
                : SplashAspect.SixteenByNine;

        /// <summary>How long the Nebula plate holds in the splash sequence. Unity's floor is 2s.</summary>
        public const float SplashLogoSeconds = 2f;

        /// <summary>Native container paths, for the build seam and the tests that verify it.</summary>
        public const string WindowsIcoPath = NativeAssetFolder + "/nebula-windows.ico";

        /// <inheritdoc cref="WindowsIcoPath"/>
        public const string MacIcnsPath = NativeAssetFolder + "/Nebula.icns";

        /// <inheritdoc cref="WindowsIcoPath"/>
        public const string MacIconsetFolder = NativeAssetFolder + "/Nebula.iconset";

        // ---- lifecycle -------------------------------------------------------

        /// <summary>
        /// Drops every cached reference and clears the probes. For fixtures that need
        /// a clean slate between cases.
        /// <para>
        /// Forgets rather than unloads: a fixture calling this between cases would
        /// otherwise free textures the surface under test is still drawing.
        /// </para>
        /// </summary>
        /// <summary>
        /// Frees the textures the last resolve displaced.
        /// <para>
        /// Separate from resolving, and called by the view after its Images already
        /// point at the new sprites. Unity will not unload an asset that is still
        /// referenced, so the order is load, assign, then free — doing it inside the
        /// resolve is a no-op that reads like a release.
        /// </para>
        /// </summary>
        public static void ReleaseSupersededTextures()
        {
            KeyArtSlot.ReleaseSuperseded();
            LogoSlot.ReleaseSuperseded();
            EmblemSlot.ReleaseSuperseded();
        }

        public static void Release()
        {
            KeyArtSlot.Forget();
            LogoSlot.Forget();
            EmblemSlot.Forget();
            FrameCache.Clear();
            Missing.Clear();
            FallbacksUsed = 0;
            CombinedLogoKeyArtUsed = false;
            ReducedUsesStatic = false;
        }
    }
}
