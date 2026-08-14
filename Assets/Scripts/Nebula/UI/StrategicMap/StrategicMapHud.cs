using System.Globalization;
using System.Collections.Generic;
using System.Text;
using Nebula.Remote;
using Nebula.UI.StrategicMap.Preview;
using UnityEngine;
using UnityEngine.UI;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The map's heads-up chrome: breadcrumb, clock, telemetry, camera distance, legend and
    /// status line.
    ///
    /// Every position, size and colour is taken from the reference's own stylesheet, and
    /// from the LATER of its two rule blocks where they disagree -- the same override the
    /// finishing pass had to be built from. Offsets are in CSS pixels against a 1080-tall
    /// reference and scaled to the real height, so the chrome keeps its proportions at
    /// 1440 and above instead of shrinking into a corner.
    ///
    /// The reference's own chrome was English ("ENTIRE UNIVERSE", "CAMERA DISTANCE"). The
    /// game is Turkish-facing and every one of these lines is now player-facing Turkish,
    /// as the rest of the shell is: the breadcrumb root, the clock, the depth label, the
    /// legend, the status line and the control hint.
    /// </summary>
    public sealed class StrategicMapHud : MonoBehaviour
    {
        /// <summary>
        /// The breadcrumb's root — the level above every other, and the whole trail when
        /// nothing has been opened. Named rather than typed out at each of its four sites so
        /// the drawn chrome and the trail builder can never drift apart, and so a test pins
        /// "the trail starts at the top level" rather than one particular spelling of it.
        /// </summary>
        public const string UniverseCrumb = "TÜM EVREN";

        /// <summary>The separator between levels of the trail.</summary>
        public const string CrumbSeparator = "  ›  ";

        /// <summary>The stylesheet's own pixel offsets are against a 1080-tall viewport.</summary>
        private const float ReferenceHeight = 1080f;

        // Straight from the reference's later rule block.
        private static readonly Color TopHudColor = new Color(236f / 255f, 243f / 255f, 255f / 255f, 0.82f);
        private static readonly Color TelemetryColor = new Color(198f / 255f, 213f / 255f, 235f / 255f, 0.38f);
        private static readonly Color DepthColor = new Color(193f / 255f, 210f / 255f, 234f / 255f, 0.38f);
        private static readonly Color LegendColor = new Color(200f / 255f, 214f / 255f, 235f / 255f, 0.46f);
        private static readonly Color StatusColor = new Color(222f / 255f, 232f / 255f, 248f / 255f, 0.68f);

        private Text breadcrumb;
        private Text clock;
        private Text telemetry;
        private Text depthLabel;
        private Text depthValue;
        private Text legend;
        private Text status;
        private Text hint;
        private Text hoverLabel;
        private Image hoverBackdrop;
        private RectTransform rootRect;

        private StrategicMapUniverseSnapshot universe;
        private float openedAt;

        public static StrategicMapHud Create(RectTransform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Map HUD");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);

            var hud = go.AddComponent<StrategicMapHud>();
            hud.universe = universe;
            hud.rootRect = rect;
            hud.Build(rect);
            return hud;
        }

        /// <summary>The live screen hands a freshly built universe over whenever knowledge changes, so names and counters stay true.</summary>
        public void SetUniverse(StrategicMapUniverseSnapshot fresh)
        {
            if (fresh == null) return;
            universe = fresh;
            WriteFixedTelemetry();
        }

        private void Build(RectTransform root)
        {
            // CanvasScaler already handles resolution. Scaling a second time made the 720p
            // and 1080p HUD effectively one-to-seven pixels tall.
            const float edge = 28f;

            // top: 17px, left/right: 19px -- breadcrumb left, clock right.
            breadcrumb = Line(root, "Breadcrumb", UniverseCrumb, 18f, TopHudColor,
                TextAnchor.UpperLeft, new Vector2(edge, -24f), new Vector2(0f, 1f));
            clock = Line(root, "Live Clock", "CANLI · T+0.0", 16f, TopHudColor,
                TextAnchor.UpperRight, new Vector2(-edge, -24f), new Vector2(1f, 1f));

            // top: 50px -- the fixed facts about the map, not live state.
            telemetry = Line(root, "Telemetry", "", 14f, TelemetryColor,
                TextAnchor.UpperLeft, new Vector2(edge, -58f), new Vector2(0f, 1f));

            // top: 78px, width 126px -- a label above its value.
            depthLabel = Line(root, "Depth Label", "KAMERA UZAKLIĞI", 13f, DepthColor,
                TextAnchor.UpperLeft, new Vector2(edge, -92f), new Vector2(0f, 1f));
            depthValue = Line(root, "Depth Value", "55.0 HB", 16f, StatusColor,
                TextAnchor.UpperLeft, new Vector2(edge, -111f), new Vector2(0f, 1f));

            // bottom: 48px.
            //
            // Five of these six markers used to be ◆ ◇ △ ⟡ ┄, and five of them
            // drew as empty boxes. LiberationSans.ttf is the only face in the
            // project — it is what the SDF atlas bakes from and what the fallback
            // loads — and its cmap has no glyph for U+25C6, U+25C7, U+25B3,
            // U+27E1 or U+2504. A legend whose whole job is telling six things
            // apart at a glance was showing one shape (▲) and five identical
            // boxes. Do not restore them; they cannot be drawn.
            //
            // The replacements keep the original's logic rather than picking
            // pretty characters: filled versus hollow diamond still separates a
            // system from a signal, the hollow triangle is still the filled
            // ship triangle's counterpart, and a route still looks like a line.
            // ○ takes over discovery because a third diamond variant next to ♦
            // and ◊ would defeat the point of the line. Same reasoning as
            // NebulaHudPlanetContext, which dropped its own "✓"/"✕" pair for
            // being font-dependent rather than the project's own iconography.
            legend = Line(root, "Legend",
                "♦ SİSTEM   ◊ SİNYAL   ∆ TEHDİT   ○ KEŞİF   ─ ROTA   ▲ GEMİ",
                14f, LegendColor, TextAnchor.LowerLeft,
                new Vector2(edge, 64f), new Vector2(0f, 0f));

            // bottom: 15px -- counts on the left, the control hint on the right.
            status = Line(root, "Status", "Harita hazırlanıyor…", 16f, StatusColor,
                TextAnchor.LowerLeft, new Vector2(edge, 24f), new Vector2(0f, 0f));
            hint = Line(root, "Hint", "Sürükle: döndür · Tekerlek: yakınlaştır · Tıkla: incele",
                14f, StatusColor, TextAnchor.LowerRight,
                new Vector2(-edge, 24f), new Vector2(1f, 0f));

            // The hover label follows the cursor's target, so it is positioned per frame
            // rather than anchored. Its backdrop is created first so the text draws over it.
            hoverBackdrop = StrategicMapScreen.CreateImage(root, "Hover Backdrop", HoverBackdropColor);
            hoverBackdrop.rectTransform.anchorMin = Vector2.zero;
            hoverBackdrop.rectTransform.anchorMax = Vector2.zero;
            hoverBackdrop.rectTransform.pivot = new Vector2(0f, 0.5f);
            hoverLabel = Line(root, "Hover Label", "", 16f, StrategicMapTheme.PanelText,
                TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            hoverLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
            SetHoverLabel(null, Vector2.zero);

            WriteFixedTelemetry();
            openedAt = Time.unscaledTime;
        }

        public void OnMapOpened()
        {
            openedAt = Time.unscaledTime;
            WriteFixedTelemetry();
        }

        public void SetStatus(string message, bool success = true)
        {
            if (status == null || string.IsNullOrEmpty(message)) return;
            status.text = message;
            status.color = success ? StrategicMapTheme.Green : StrategicMapTheme.Gold;
        }

        /// <summary>
        /// Shows every currently pending reconnect report as one compact status line, then
        /// acknowledges it through the domain service. Nothing is consumed while the map is
        /// closed, and raw task descriptions/stable ids never enter player-facing text.
        /// </summary>
        public int PresentReconnectSummaries(IReadOnlyList<StrategicReconnectSummaryView> summaries)
        {
            if (status == null || summaries == null || summaries.Count == 0) return 0;

            var text = new StringBuilder();
            int shown = Mathf.Min(3, summaries.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) text.Append("   ·   ");
                text.Append(FormatReconnectSummary(summaries[i]));
            }
            if (summaries.Count > shown) text.Append("   ·   +").Append(summaries.Count - shown).Append(" sistem");

            status.text = text.ToString();
            status.color = StrategicMapTheme.Gold;

            // The backend contract clears a report the moment it has actually been shown.
            for (int i = 0; i < summaries.Count; i++)
                RemoteCommunicationService.ConsumeReconnectSummary(summaries[i].SystemId);
            return summaries.Count;
        }

        internal static string FormatReconnectSummary(in StrategicReconnectSummaryView summary)
        {
            var details = new List<string>(6);
            if (summary.RaidResolved) details.Add("baskın sonucu");
            if (summary.WasDamagedOrDisabled) details.Add("hasar bildirimi");
            if (summary.StolenResourceKinds > 0) details.Add($"{summary.StolenResourceKinds} kaynak kaybı");
            if (summary.SalvagedResourceKinds > 0) details.Add($"{summary.SalvagedResourceKinds} kurtarım");
            if (summary.CompletedTaskCount > 0) details.Add($"{summary.CompletedTaskCount} görev tamamlandı");
            if (summary.StalledTaskCount > 0) details.Add($"{summary.StalledTaskCount} görev bekliyor");
            if (summary.HasLogisticsStatus) details.Add("lojistik güncellendi");
            string result = details.Count > 0 ? string.Join(" · ", details) : "değişiklik yok";
            return $"BAĞLANTI GERİ GELDİ · {summary.SystemName} · {result}";
        }

        /// <summary>Reference: rgba(7, 11, 17, 0.9) behind the hover label.</summary>
        private static readonly Color HoverBackdropColor = new Color(7f / 255f, 11f / 255f, 17f / 255f, 0.9f);

        /// <summary>
        /// Names whatever the cursor is over, beside it. Given no name the label is switched
        /// off outright rather than emptied: an empty label still draws its backdrop, which
        /// leaves a small dark rectangle stuck to the last thing that was hovered.
        /// </summary>
        public void SetHoverLabel(string name, Vector2 screenPoint)
        {
            if (hoverLabel == null) return;
            bool show = !string.IsNullOrEmpty(name);
            hoverLabel.gameObject.SetActive(show);
            hoverBackdrop.gameObject.SetActive(show);
            if (!show) return;

            hoverLabel.text = name;
            float width = hoverLabel.preferredWidth + 22f;
            float height = hoverLabel.preferredHeight + 14f;

            // Offset up and right of the cursor so the label never sits under it.
            Vector2 canvasPoint = screenPoint;
            if (rootRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootRect, screenPoint, null, out Vector2 local))
                canvasPoint = local - rootRect.rect.min;

            var position = canvasPoint + new Vector2(16f, 14f);
            if (rootRect != null)
            {
                // Keep the label clear of both screen edges and the right inspector drawer.
                float usableRight = Mathf.Max(160f, rootRect.rect.width - 540f);
                position.x = Mathf.Clamp(position.x, 16f, Mathf.Max(16f, usableRight - width - 16f));
                position.y = Mathf.Clamp(position.y, height * 0.5f + 16f,
                    Mathf.Max(height * 0.5f + 16f, rootRect.rect.height - height * 0.5f - 16f));
            }
            hoverLabel.rectTransform.anchoredPosition = position + new Vector2(11f, 0f);
            hoverBackdrop.rectTransform.anchoredPosition = position;
            hoverBackdrop.rectTransform.sizeDelta = new Vector2(width, height);
        }

        /// <summary>The counts never change while the map is open, so they are written once.</summary>
        private void WriteFixedTelemetry()
        {
            if (universe == null) return;
            var line = new StringBuilder();
            line.Append(universe.galaxies.Count).Append(" GALAKSİ     ");
            line.Append(universe.systems.Count).Append(" BİLİNEN HEDEF     ");
            line.Append("1 HB = 1M BİRİM     YÖRÜNGE · SİM ZAMANI");
            telemetry.text = line.ToString();

            status.text = $"{universe.systems.Count} hedef · {universe.planets.Count} gezegen · " +
                          $"{universe.discoveries.Count + universe.derelicts.Count + universe.anomalies.Count} keşif noktası";
        }

        /// <summary>
        /// Refreshes the parts that track the map's state. Called every frame: the clock and
        /// the camera distance are both continuous, and a breadcrumb that lagged a level
        /// behind would be worse than none at all.
        /// </summary>
        public void Tick(StrategicMapPresenter presenter, StrategicMapUniverseRenderer renderer)
        {
            if (presenter == null) return;

            clock.text = $"CANLI · T+{(Time.unscaledTime - openedAt).ToString("F1", CultureInfo.InvariantCulture)}";
            breadcrumb.text = BuildBreadcrumb(presenter, universe);

            if (renderer != null)
                depthValue.text = renderer.Distance.ToString("F1", CultureInfo.InvariantCulture) + " HB";
        }

        /// <summary>
        /// The breadcrumb: <see cref="UniverseCrumb"/> at the top, then the names of the
        /// levels the player has actually opened, separated by a chevron.
        ///
        /// Static and pure so the trail can be checked without building a screen: the thing
        /// worth testing is which levels appear and in what order, not how the text is drawn.
        /// </summary>
        public static string BuildBreadcrumb(StrategicMapPresenter presenter,
            StrategicMapUniverseSnapshot universe)
        {
            if (presenter == null || universe == null) return UniverseCrumb;
            var crumbs = new StringBuilder(UniverseCrumb);

            if (!string.IsNullOrEmpty(presenter.SelectedGalaxyId))
                Append(crumbs, NameOfGalaxy(universe, presenter.SelectedGalaxyId));
            if (!string.IsNullOrEmpty(presenter.SelectedSystemId))
                Append(crumbs, NameOfSystem(universe, presenter.SelectedSystemId));
            if (!string.IsNullOrEmpty(presenter.SelectedPlanetId))
                Append(crumbs, NameOfPlanet(universe, presenter.SelectedPlanetId));

            return crumbs.ToString();
        }

        private static void Append(StringBuilder builder, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            builder.Append(CrumbSeparator).Append(name.ToUpperInvariant());
        }

        private static string NameOfGalaxy(StrategicMapUniverseSnapshot universe, string id)
        {
            for (int i = 0; i < universe.galaxies.Count; i++)
                if (universe.galaxies[i].id == id) return universe.galaxies[i].name;
            return null;
        }

        private static string NameOfSystem(StrategicMapUniverseSnapshot universe, string id)
        {
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == id) return universe.systems[i].name;
            return null;
        }

        private static string NameOfPlanet(StrategicMapUniverseSnapshot universe, string id)
        {
            for (int i = 0; i < universe.planets.Count; i++)
                if (universe.planets[i].id == id) return universe.planets[i].name;
            return null;
        }

        private static Text Line(RectTransform parent, string name, string content, float size,
            Color color, TextAnchor alignment, Vector2 offset, Vector2 anchor)
        {
            Text text = StrategicMapScreen.CreateText(parent, name, content,
                Mathf.Max(1, Mathf.RoundToInt(size)), color, alignment);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y);
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(900f, 30f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
