using UnityEngine;
using UnityEngine.UI;
using Nebula.Visuals;

namespace Nebula.UI.Brand
{
    /// <summary>
    /// Draws the key art behind a screen and lays the wordmark over it.
    /// <para>
    /// The composition is <b>clean plate + separate logo</b>. The first pass drew a
    /// plate with the wordmark composited in, because that was the only export that
    /// existed; the second delivery supplied the logo as its own transparent asset
    /// and the plate in four authored aspects, so the two are now independent and
    /// the logo can be placed per aspect instead of riding wherever the crop put it.
    /// </para>
    /// <para>
    /// Aspect is chosen, never stretched. The 21:9 export is not the 16:9 one made
    /// wider — it carries 880px of new dark canvas on the left that the 16:9 plate
    /// does not have, which is why the logo needs a layout profile per aspect rather
    /// than one normalized box applied blindly to all three.
    /// </para>
    /// <para>
    /// Presentation only, and passive: it owns two Images, no camera, no input, no
    /// modal claim. It never starts, stops or reads a session.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class NebulaBrandKeyArtView : MonoBehaviour
    {
        /// <summary>
        /// Where the wordmark sits on one authored composition, in normalized plate
        /// coordinates with y measured downward from the top.
        /// <para>
        /// Height is deliberately absent. It is derived from the logo sprite's own
        /// aspect at layout time, which is what makes "do not compress the logo
        /// vertically" true by construction rather than by choosing careful numbers.
        /// </para>
        /// </summary>
        public readonly struct LogoProfile
        {
            public readonly float XMin;
            public readonly float YMin;
            public readonly float Width;

            public LogoProfile(float xMin, float yMin, float width)
            {
                XMin = xMin; YMin = yMin; Width = width;
            }
        }

        /// <summary>
        /// 16:9 keeps the footprint the composited wordmark occupied.
        /// <para>
        /// Measured rather than eyeballed: the logo-bearing and clean 1672x941
        /// masters are identical except for the logo, so differencing them gave its
        /// exact box — x 46..665, y 56..221. Preserving it is what makes the switch
        /// from composited to separate invisible at this aspect.
        /// </para>
        /// </summary>
        public static readonly LogoProfile SixteenByNineLogo = new LogoProfile(0.0275f, 0.0595f, 0.3708f);

        /// <summary>
        /// 16:10 keeps the same width and resolves y against the authored band.
        /// <para>
        /// The 2560x1600 export is the 2560x1440 authored image at y=80 with new
        /// canvas above and below, so a point at 16:9 normalized v sits at
        /// 0.05 + 0.9v here. Applying the 16:9 y directly would drift the logo up
        /// into the new top band, which is empty extension rather than composition.
        /// </para>
        /// <para>
        /// The width is 0.33 rather than 16:9's 0.3708, and that is not a taste
        /// decision. Height follows the mark's own 3.58:1 aspect from the width, and
        /// this plate is taller relative to its width, so the same normalized width
        /// renders a taller mark in canvas units — at 2560x1600 it reached 307 units
        /// down a canvas whose menu column starts at 300. Narrowing to 0.33 brings the
        /// bottom to about 286 and keeps the authored vertical position exactly, which
        /// is the number the delivery documents; moving y instead would have traded a
        /// documented value for an undocumented one.
        /// </para>
        /// </summary>
        public static readonly LogoProfile SixteenByTenLogo =
            new LogoProfile(0.0275f, 0.05f + 0.9f * 0.0595f, 0.33f);

        /// <summary>
        /// 21:9 puts the wordmark inside the dark left extension.
        /// <para>
        /// That extension is x 0..880 of 3440, i.e. everything left of 0.2558, and it
        /// is the only part of this composition that is not painted scene. The logo
        /// is aligned to the menu column's own left inset (88 of a 2226 unit canvas =
        /// 0.0395) and stops at 0.235, leaving 21px of margin before the scene
        /// begins — so it never lands on the planet, the hero ship or the black hole,
        /// and never drifts toward the middle of an ultrawide screen.
        /// </para>
        /// </summary>
        public static readonly LogoProfile UltraWideLogo = new LogoProfile(0.0395f, 0.06f, 0.1955f);

        /// <summary>The layout profile for an authored aspect.</summary>
        public static LogoProfile LogoProfileFor(NebulaBrandAssetCatalog.KeyArtAspect aspect) => aspect switch
        {
            NebulaBrandAssetCatalog.KeyArtAspect.UltraWide => UltraWideLogo,
            NebulaBrandAssetCatalog.KeyArtAspect.SixteenByTen => SixteenByTenLogo,
            _ => SixteenByNineLogo,
        };

        /// <summary>
        /// What the crop may never eat into, per aspect.
        /// <para>
        /// At each of the four target resolutions the authored plate matches the
        /// viewport exactly and nothing is cropped at all — this only binds on a
        /// screen between two authored aspects. The 16:10 band is the 16:9 area
        /// mapped through the same 0.05 + 0.9v the logo uses; the 21:9 area runs from
        /// the logo's left margin across to the scene's right edge.
        /// </para>
        /// </summary>
        public static Rect CriticalSafeAreaFor(NebulaBrandAssetCatalog.KeyArtAspect aspect) => aspect switch
        {
            NebulaBrandAssetCatalog.KeyArtAspect.UltraWide => Rect.MinMaxRect(0.02f, 0.04f, 0.92f, 0.775f),
            NebulaBrandAssetCatalog.KeyArtAspect.SixteenByTen =>
                Rect.MinMaxRect(0.02f, 0.05f + 0.9f * 0.04f, 0.89f, 0.05f + 0.9f * 0.775f),
            _ => Rect.MinMaxRect(0.02f, 0.04f, 0.89f, 0.775f),
        };

        private Image image;
        private Image logoImage;
        private RectTransform rect;
        private RectTransform logoRect;
        private RectTransform parent;

        private Vector2 lastParentSize;
        private int lastQualityLevel = -1;
        private bool lastHighContrast;
        private bool resolved;

        /// <summary>Test-facing: whether the last layout could honour the safe area in full.</summary>
        public bool SafeAreaHonoured { get; private set; } = true;

        /// <summary>Test-facing: the visible window onto the plate, in normalized plate coordinates.</summary>
        public Rect VisibleWindow { get; private set; } = new Rect(0f, 0f, 1f, 1f);

        /// <summary>Test-facing: the authored composition currently bound.</summary>
        public NebulaBrandAssetCatalog.KeyArtAspect SelectedAspect { get; private set; }

        /// <summary>Test-facing: the 16:9 rung currently bound. Meaningless at the other aspects.</summary>
        public NebulaBrandAssetCatalog.KeyArtDetail SelectedDetail { get; private set; }

        /// <summary>Test-facing: the project tier the live quality level resolved to.</summary>
        public LightingQuality SelectedQualityTier { get; private set; } = LightingQuality.Medium;

        /// <summary>Test-facing: which cut of the mark is drawn.</summary>
        public NebulaBrandAssetCatalog.MarkStyle SelectedMarkStyle { get; private set; }

        /// <summary>Test-facing: how many times the plate has been re-resolved.</summary>
        public int Resolutions { get; private set; }

        /// <summary>True when a plate is actually bound and drawing.</summary>
        public bool HasArt => image != null && image.sprite != null && image.enabled;

        /// <summary>
        /// True when the separate wordmark is bound and actually reaches the screen.
        /// <para>
        /// Draw order is part of the claim, not a detail. A wordmark with a sprite, an
        /// enabled Image and an active object that happens to sit under the menu's
        /// opaque backdrop is invisible, and a property that called that "drawing"
        /// reported a titled menu for every configuration while the top left was empty.
        /// </para>
        /// </summary>
        public bool HasLogo => logoImage != null && logoImage.sprite != null
            && logoImage.enabled && logoImage.gameObject.activeSelf && LogoDrawsAboveThePlate;

        /// <summary>
        /// True when something on this background is showing the brand's name — either
        /// the separate wordmark over a clean plate, or the composited plate that has
        /// the wordmark baked into it.
        /// <para>
        /// What the screen's own drawn title should defer to. Deferring to
        /// <see cref="HasArt"/> instead means a plate that binds while its wordmark
        /// does not leaves the menu with no title at all, which is worse than either
        /// title on its own.
        /// </para>
        /// </summary>
        public bool CarriesTitle =>
            HasArt && (HasLogo || NebulaBrandAssetCatalog.CombinedLogoKeyArtUsed);

        /// <summary>Test-facing: the wordmark's Image, for raycast and rect assertions.</summary>
        public Image LogoImage => logoImage;

        /// <summary>
        /// Builds the background and its wordmark under <paramref name="host"/>.
        /// The Images are created here rather than by the UI factory because the
        /// factory's surfaces are 9-sliced chrome and these are photographs.
        /// </summary>
        public static NebulaBrandKeyArtView Create(RectTransform host, string name = "Key Art")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var view = go.AddComponent<NebulaBrandKeyArtView>();
            view.rect = (RectTransform)go.transform;
            view.rect.SetParent(host, false);
            view.rect.SetAsFirstSibling();          // behind everything the screen draws

            view.image = go.GetComponent<Image>();
            view.image.raycastTarget = false;       // the backdrop below it already blocks
            view.image.preserveAspect = false;      // the rect is sized to the aspect instead
            view.image.type = Image.Type.Simple;

            var logo = new GameObject("Wordmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            view.logoRect = (RectTransform)logo.transform;
            view.logoRect.SetParent(host, false);
            view.logoRect.SetSiblingIndex(view.rect.GetSiblingIndex() + 1);
            view.logoImage = logo.GetComponent<Image>();
            // Never a raycast target. A full-width wordmark that ate clicks would
            // make the top of the menu dead without changing how it looks.
            view.logoImage.raycastTarget = false;
            view.logoImage.preserveAspect = true;
            view.logoImage.type = Image.Type.Simple;

            view.parent = host;
            view.Bind();
            return view;
        }

        /// <summary>
        /// Resolves the plate and the mark for the current viewport, quality and
        /// contrast setting, and frees whatever they replaced.
        /// </summary>
        public void Bind()
        {
            if (image == null || parent == null) return;

            int width = ViewportPixels(out int height);
            int quality = QualitySettings.GetQualityLevel();
            bool highContrast = LightingAccessibility.HighContrast;

            // Resolved once, from the level's authored name rather than its index --
            // see NebulaBrandAssetCatalog.DisplayTierFor. Both selections read the
            // same tier, so a plate and a mark can never come from different readings
            // of the same setting.
            SelectedQualityTier = NebulaBrandAssetCatalog.DisplayTierFor(quality);

            SelectedAspect = NebulaBrandAssetCatalog.AspectFor(width, height);
            SelectedDetail = NebulaBrandAssetCatalog.DetailFor(height, SelectedQualityTier);
            SelectedMarkStyle = NebulaBrandAssetCatalog.StyleFor(highContrast);

            Sprite art = NebulaBrandAssetCatalog.KeyArt(SelectedAspect, SelectedDetail);
            image.sprite = art;
            image.enabled = art != null;

            Sprite mark = NebulaBrandAssetCatalog.Logo(SelectedMarkStyle,
                NebulaBrandAssetCatalog.MarkQualityFor(height, SelectedQualityTier));

            // The mark is hidden when the plate had to fall back to the composited
            // version, which already carries a wordmark. Two wordmarks is the one
            // outcome worse than an old-looking one.
            bool composited = NebulaBrandAssetCatalog.CombinedLogoKeyArtUsed;
            logoImage.sprite = mark;
            logoImage.enabled = mark != null && !composited;
            logoRect.gameObject.SetActive(mark != null && !composited);

            // Both Images now point at the new sprites, so whatever they displaced is
            // genuinely unreferenced and can actually be freed.
            NebulaBrandAssetCatalog.ReleaseSupersededTextures();

            EnsureDrawOrder();

            lastQualityLevel = quality;
            lastHighContrast = highContrast;
            resolved = true;
            Resolutions++;

            Layout();
        }

        /// <summary>
        /// Puts the wordmark immediately above the plate, every bind.
        /// <para>
        /// It is not enough to place it once at construction. <see cref="Create"/>
        /// parents both under the host and orders them correctly — and then the screen
        /// that owns them moves the plate, to sit above its own backdrop, and Unity's
        /// sibling arithmetic slides the wordmark down underneath both. The result is a
        /// menu with a bound, enabled, correctly positioned wordmark that is painted
        /// over by an opaque backdrop before anyone sees it, which is why every
        /// property a test could read said the logo was there while the top left of the
        /// screen was empty sky.
        /// </para>
        /// <para>
        /// Re-asserted here rather than fixed in the screen, because any future caller
        /// that reorders the plate would reintroduce it: the invariant belongs to the
        /// pair, so the pair maintains it.
        /// </para>
        /// </summary>
        private void EnsureDrawOrder()
        {
            if (rect == null || logoRect == null) return;
            if (rect.parent == null || logoRect.parent != rect.parent) return;

            int plate = rect.GetSiblingIndex();
            int mark = logoRect.GetSiblingIndex();
            if (mark == plate + 1) return;

            // SetSiblingIndex removes before it inserts, so moving up from below the
            // plate shifts the plate down one and the target index is the plate's
            // current one; moving down from above it does not.
            logoRect.SetSiblingIndex(mark < plate ? plate : plate + 1);
        }

        /// <summary>Test-facing: whether the wordmark is drawn after the plate rather than under it.</summary>
        public bool LogoDrawsAboveThePlate =>
            rect != null && logoRect != null && logoRect.parent == rect.parent
            && logoRect.GetSiblingIndex() > rect.GetSiblingIndex();

        /// <summary>
        /// The viewport in real pixels.
        /// <para>
        /// Taken from the canvas rather than <c>Screen</c>, because a canvas rendering
        /// through a camera into a RenderTexture — which is how this project
        /// photographs the interface at resolutions the machine does not have — is
        /// sized by that target and not by the desktop. Reading Screen there would
        /// pick one aspect for every capture and the selection would never be tested.
        /// </para>
        /// </summary>
        private int ViewportPixels(out int height)
        {
            Canvas canvas = parent == null ? null : parent.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                height = Mathf.Max(Screen.height, 1);
                return Mathf.Max(Screen.width, 1);
            }

            float scale = canvas.scaleFactor <= 0f ? 1f : canvas.scaleFactor;
            height = Mathf.Max(Mathf.RoundToInt(parent.rect.height * scale), 1);
            return Mathf.Max(Mathf.RoundToInt(parent.rect.width * scale), 1);
        }

        private void OnEnable() => Layout();

        /// <summary>
        /// Re-resolves only when something that feeds the choice actually changed:
        /// the viewport, the quality level or the contrast setting. Everything else
        /// is a layout, and a layout only runs when the rect moved.
        /// <para>
        /// Not a per-frame <c>Resources.Load</c>: at 60fps that would be sixty
        /// asset-database lookups a second for a picture that changes when the player
        /// resizes a window.
        /// </para>
        /// </summary>
        private void Update()
        {
            if (parent == null || image == null) return;

            // Two sibling-index reads and an int compare, every frame, on purpose: the
            // pair's draw order can be broken by anything that reorders the plate, and
            // the symptom -- a wordmark that every property reports as drawn and that
            // nobody can see -- is invisible to everything except the pixels.
            EnsureDrawOrder();

            bool viewportChanged = parent.rect.size != lastParentSize;
            bool settingsChanged = !resolved
                || QualitySettings.GetQualityLevel() != lastQualityLevel
                || LightingAccessibility.HighContrast != lastHighContrast;

            if (settingsChanged || viewportChanged) { Bind(); return; }
        }

        /// <summary>Recomputes the cover size and the clamped pan. Public so a fixture need not wait a frame.</summary>
        public void Layout()
        {
            if (image == null || rect == null || parent == null) return;
            if (image.sprite == null) return;

            Vector2 viewport = parent.rect.size;
            if (viewport.x <= 0f || viewport.y <= 0f) return;

            lastParentSize = viewport;

            Rect spriteRect = image.sprite.rect;
            float artAspect = spriteRect.width / Mathf.Max(spriteRect.height, 1f);
            float viewAspect = viewport.x / viewport.y;

            // Cover: match the axis that would otherwise leave a gap. With an
            // authored plate per aspect this is usually an exact fit -- at all four
            // target resolutions the two aspects are identical and nothing is cropped.
            Vector2 size = viewAspect > artAspect
                ? new Vector2(viewport.x, viewport.x / artAspect)
                : new Vector2(viewport.y * artAspect, viewport.y);

            float visibleX = Mathf.Clamp01(viewport.x / size.x);
            float visibleY = Mathf.Clamp01(viewport.y / size.y);

            Rect safe = CriticalSafeAreaFor(SelectedAspect);
            float left = ClampWindow(visibleX, safe.xMin, safe.xMax, out bool okX);
            float top = ClampWindow(visibleY, safe.yMin, safe.yMax, out bool okY);

            SafeAreaHonoured = okX && okY;
            VisibleWindow = new Rect(left, top, visibleX, visibleY);

            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;

            // Normalized plate x maps to local x as (u - 0.5) * width; normalized y is
            // measured downward, so it maps to (0.5 - v) * height. Placing the
            // window's centre at the viewport's centre inverts both.
            rect.anchoredPosition = new Vector2(
                -(left + visibleX * 0.5f - 0.5f) * size.x,
                ((top + visibleY * 0.5f) - 0.5f) * size.y);

            LayoutLogo(size);
        }

        /// <summary>
        /// Where the visible window starts along one axis.
        /// <para>
        /// Centred if it can be. If centring would cut into the safe span, it slides
        /// back to the nearest position that does not. If the safe span is simply
        /// wider than the window — an aspect this composition cannot serve —
        /// the safe span is centred instead and <paramref name="honoured"/> reports
        /// that, so a test fails rather than a player quietly losing the logo.
        /// </para>
        /// </summary>
        internal static float ClampWindow(float windowSpan, float safeMin, float safeMax, out bool honoured)
        {
            float slack = 1f - windowSpan;
            if (slack <= 0f) { honoured = safeMin >= 0f && safeMax <= 1f; return 0f; }

            float safeSpan = safeMax - safeMin;
            if (safeSpan > windowSpan)
            {
                honoured = false;
                return Mathf.Clamp((safeMin + safeMax) * 0.5f - windowSpan * 0.5f, 0f, slack);
            }

            honoured = true;
            float lower = Mathf.Max(0f, safeMax - windowSpan);   // far enough right to include safeMax
            float upper = Mathf.Min(slack, safeMin);             // far enough left to include safeMin
            return Mathf.Clamp(slack * 0.5f, lower, upper);
        }

        /// <summary>
        /// Places the wordmark inside its aspect's profile, at the plate's own scale
        /// and through the plate's own pan, so it stays locked to the composition
        /// rather than to the screen.
        /// <para>
        /// The height comes from the sprite's aspect and the width from the profile,
        /// which is what guarantees the mark is never squashed to fit a box.
        /// </para>
        /// </summary>
        private void LayoutLogo(Vector2 artSize)
        {
            if (logoRect == null || logoImage == null) return;
            if (!logoRect.gameObject.activeSelf || logoImage.sprite == null) return;

            LogoProfile profile = LogoProfileFor(SelectedAspect);
            Rect markRect = logoImage.sprite.rect;
            float markAspect = markRect.width / Mathf.Max(markRect.height, 1f);

            float width = profile.Width * artSize.x;
            float height = width / Mathf.Max(markAspect, 0.0001f);

            logoRect.anchorMin = logoRect.anchorMax = new Vector2(0.5f, 0.5f);
            logoRect.pivot = new Vector2(0f, 1f);          // top-left of the profile box
            logoRect.sizeDelta = new Vector2(width, height);
            logoRect.anchoredPosition = rect.anchoredPosition + new Vector2(
                (profile.XMin - 0.5f) * artSize.x,
                (0.5f - profile.YMin) * artSize.y);
        }
    }
}
