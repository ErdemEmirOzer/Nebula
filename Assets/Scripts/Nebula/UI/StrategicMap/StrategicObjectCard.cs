using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Poi;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The shared detail card. Star, planet, POI and system all use this same glass
    /// layout and the same field grammar — only the fields themselves change, so the
    /// map reads as one designed surface rather than four different panels.
    ///
    /// The portrait is a LIVE render of the body (see <see cref="StrategicBodyPortrait"/>):
    /// the real planet prefab turning under its own light, or the real star with its corona
    /// and rays, framed whole and never cropped. Over it, a planet's surface slots are drawn
    /// as illustrated marks on their own real surface normals — what is built, what is going
    /// up, what is damaged and what is still free — each with its own shape and its own
    /// motion, so the state of a world is legible before a single word of it is read.
    ///
    /// It shows the player exactly what the snapshot already permits, and never a
    /// stable id, seed, DTO or developer field. A stale (non-Live) system is labelled
    /// as such with its own age, and a disconnected one never shows a live number.
    /// </summary>
    public sealed class StrategicObjectCard : MonoBehaviour
    {
        private const int MaxFieldRows = 12;

        /// <summary>The card's own geometry. Named because the portrait, the slot ring and the first field row all key off it.</summary>
        private const float CardWidth = 500f;
        private const float CardHeight = 900f;
        /// <summary>The body itself is the point of the card, so it takes almost the full width.</summary>
        private const float PortraitSize = 360f;
        private const float PortraitTop = -26f;
        private const float FieldsTop = PortraitTop - PortraitSize - 116f;

        private RectTransform viewRect;
        private CanvasGroup group;
        private RectTransform portraitFrame;
        private RawImage livePortrait;
        private Image flatPortrait;
        private Image portraitRim;
        private StrategicBodyPortrait portraitSource;
        private Text titleText;
        private Text subtitleText;
        private Text staleText;
        private StrategicPlanetSlotsView slotsView;
        private readonly List<FieldRow> fieldPool = new List<FieldRow>();

        /// <summary>What is currently shown, so re-rendering the same body each frame does not restart its entrance.</summary>
        private string shownKey;
        private float shownSince;

        private sealed class FieldRow
        {
            public GameObject Root;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Icon;
            public Text Label;
            public Text Value;
        }

        public static StrategicObjectCard Create(RectTransform parent)
        {
            var go = new GameObject("Object Card");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-40f, 0f);
            rect.sizeDelta = new Vector2(CardWidth, CardHeight);

            var card = go.AddComponent<StrategicObjectCard>();
            card.viewRect = rect;
            card.BuildUi();
            go.SetActive(false);
            return card;
        }

        /// <summary>
        /// Hands the card the live portrait stage. Optional on purpose: without one (or
        /// without a GPU) the card falls back to its flat sprite and every other part of it
        /// behaves identically.
        /// </summary>
        public void SetPortraitSource(StrategicBodyPortrait source) => portraitSource = source;

        private void BuildUi()
        {
            group = gameObject.AddComponent<CanvasGroup>();

            // Rounded glass with a hairline edge, rather than a hard-cornered quad.
            Image glass = StrategicMapScreen.CreateImage(viewRect, "Glass", StrategicMapTheme.PanelFill);
            glass.sprite = StrategicMapShapes.Panel(22);
            glass.type = Image.Type.Sliced;
            StrategicMapScreen.Stretch(glass.rectTransform);
            glass.raycastTarget = true;

            Image edge = StrategicMapScreen.CreateImage(viewRect, "Edge", StrategicMapTheme.PanelStroke);
            edge.sprite = StrategicMapShapes.Panel(22, strokePixels: 2);
            edge.type = Image.Type.Sliced;
            StrategicMapScreen.Stretch(edge.rectTransform);

            // The portrait is masked to a circle: a body is round, and the live render behind
            // it is a square frame of space.
            var frameGo = new GameObject("Portrait Frame");
            portraitFrame = frameGo.AddComponent<RectTransform>();
            portraitFrame.SetParent(viewRect, false);
            portraitFrame.anchorMin = portraitFrame.anchorMax = new Vector2(0.5f, 1f);
            portraitFrame.pivot = new Vector2(0.5f, 1f);
            portraitFrame.anchoredPosition = new Vector2(0f, PortraitTop);
            portraitFrame.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            var frameImage = frameGo.AddComponent<Image>();
            frameImage.sprite = StrategicMapShapes.Disc;
            frameImage.raycastTarget = false;
            frameGo.AddComponent<Mask>().showMaskGraphic = false;

            // The live render fills the whole circle; the flat sprite behind it is the
            // fallback for a headless run or a body with no prefab.
            flatPortrait = StrategicMapScreen.CreateImage(portraitFrame, "Portrait Fallback", Color.white);
            StrategicMapScreen.Stretch(flatPortrait.rectTransform);

            var liveGo = new GameObject("Portrait Live");
            var liveRect = liveGo.AddComponent<RectTransform>();
            liveRect.SetParent(portraitFrame, false);
            StrategicMapScreen.Stretch(liveRect);
            livePortrait = liveGo.AddComponent<RawImage>();
            livePortrait.raycastTarget = false;
            livePortrait.enabled = false;

            // A thin ring around the portrait, outside the mask so it is never clipped.
            portraitRim = StrategicMapScreen.CreateImage(viewRect, "Portrait Rim", StrategicMapTheme.PanelStroke);
            portraitRim.sprite = StrategicMapShapes.Ring(2);
            portraitRim.rectTransform.anchorMin = portraitRim.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            portraitRim.rectTransform.pivot = new Vector2(0.5f, 1f);
            portraitRim.rectTransform.anchoredPosition = new Vector2(0f, PortraitTop);
            portraitRim.rectTransform.sizeDelta = new Vector2(PortraitSize, PortraitSize);

            // The slot ring sits exactly over the portrait, so a structure mark lands on the
            // world it is built on rather than beside it.
            slotsView = StrategicPlanetSlotsView.Create(viewRect,
                new Vector2(0f, CardHeight * 0.5f + PortraitTop - PortraitSize * 0.5f), PortraitSize);

            titleText = StrategicMapScreen.CreateText(viewRect, "Title", "", StrategicMapTheme.TitleSize,
                StrategicMapTheme.PanelText, TextAnchor.MiddleCenter);
            StrategicMapScreen.Anchor(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, PortraitTop - PortraitSize - 14f), new Vector2(-40f, 40f));

            subtitleText = StrategicMapScreen.CreateText(viewRect, "Subtitle", "", StrategicMapTheme.BodySize,
                StrategicMapTheme.Cyan, TextAnchor.MiddleCenter);
            StrategicMapScreen.Anchor(subtitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, PortraitTop - PortraitSize - 56f), new Vector2(-40f, 28f));

            staleText = StrategicMapScreen.CreateText(viewRect, "Stale", "", StrategicMapTheme.CaptionSize,
                StrategicMapTheme.Gold, TextAnchor.MiddleCenter);
            StrategicMapScreen.Anchor(staleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, PortraitTop - PortraitSize - 86f), new Vector2(-40f, 24f));

            for (int i = 0; i < MaxFieldRows; i++) fieldPool.Add(BuildFieldRow(i));
        }

        private FieldRow BuildFieldRow(int index)
        {
            var go = new GameObject($"Field {index}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(viewRect, false);
            StrategicMapScreen.Anchor(rect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, FieldsTop - index * 36f), new Vector2(-52f, 32f));

            var rowGroup = go.AddComponent<CanvasGroup>();

            // A faint tray behind every other row, so twelve values do not read as one block.
            Image tray = StrategicMapScreen.CreateImage(rect, "Tray",
                StrategicMapTheme.WithAlpha(StrategicMapTheme.Cyan, index % 2 == 0 ? 0.05f : 0.02f));
            tray.sprite = StrategicMapShapes.Panel(10);
            tray.type = Image.Type.Sliced;
            StrategicMapScreen.Stretch(tray.rectTransform);

            Image icon = StrategicMapScreen.CreateImage(rect, "Icon", Color.white);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(10f, 0f);
            icon.rectTransform.sizeDelta = new Vector2(22f, 22f);
            icon.preserveAspect = true;

            Text label = StrategicMapScreen.CreateText(rect, "Label", "", StrategicMapTheme.BodySize,
                StrategicMapTheme.MutedText, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.6f, 1f);
            label.rectTransform.offsetMin = new Vector2(40f, 0f);
            label.rectTransform.offsetMax = Vector2.zero;

            Text value = StrategicMapScreen.CreateText(rect, "Value", "", StrategicMapTheme.BodySize,
                StrategicMapTheme.PanelText, TextAnchor.MiddleRight);
            value.rectTransform.anchorMin = new Vector2(0.6f, 0f);
            value.rectTransform.anchorMax = new Vector2(1f, 1f);
            value.rectTransform.offsetMin = Vector2.zero;
            value.rectTransform.offsetMax = new Vector2(-12f, 0f);

            go.SetActive(false);
            return new FieldRow { Root = go, Rect = rect, Group = rowGroup, Icon = icon, Label = label, Value = value };
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            shownKey = null;
            if (portraitSource != null) portraitSource.Clear();
        }

        /// <summary>True when a screen-space pointer is over the visible inspector drawer.</summary>
        public bool ContainsScreenPoint(Vector2 screenPoint) =>
            gameObject.activeInHierarchy && viewRect != null
            && RectTransformUtility.RectangleContainsScreenPoint(viewRect, screenPoint, null);

        // ------------------------------------------------------------------ entrance

        /// <summary>
        /// Opens the card on a new subject, or leaves a running entrance alone when the same
        /// subject is simply being re-rendered. Without the key check the card would restart
        /// its animation on every refresh and never actually finish arriving.
        /// </summary>
        private void Begin(string key)
        {
            gameObject.SetActive(true);
            if (key == shownKey) return;
            shownKey = key;
            shownSince = Time.unscaledTime;
        }

        /// <summary>0..1 through the entrance.</summary>
        private float Entrance01 => Mathf.Clamp01(
            (Time.unscaledTime - shownSince) / StrategicMapTheme.NormalTransitionSeconds);

        /// <summary>
        /// Slides the card in from the edge it is anchored to and fades it up, then staggers
        /// the field rows in behind it. Every frame recomputes from the elapsed time, so a
        /// dropped frame cannot leave the card stranded half-open.
        /// </summary>
        private void AnimateEntrance(int visibleRows)
        {
            float t = Entrance01;
            float eased = StrategicMapTheme.EaseArrive(t);

            if (group != null) group.alpha = StrategicMapTheme.EaseOut(Mathf.Clamp01(t * 1.6f));
            viewRect.anchoredPosition = new Vector2(Mathf.Lerp(24f, -40f, eased), 0f);

            for (int i = 0; i < visibleRows && i < fieldPool.Count; i++)
            {
                // Each row starts a little after the one above it, which is what makes a list
                // read as arriving rather than as appearing all at once.
                float rowStart = 0.25f + i * 0.045f;
                float rowT = Mathf.Clamp01((t - rowStart) / Mathf.Max(0.0001f, 1f - rowStart));
                fieldPool[i].Group.alpha = StrategicMapTheme.EaseOut(rowT);
                fieldPool[i].Rect.anchoredPosition = new Vector2(
                    26f + (1f - StrategicMapTheme.EaseOut(rowT)) * 18f,
                    FieldsTop - i * 36f);
            }

            // The portrait's rim breathes while the card is open — a quiet sign the reading
            // is live rather than a still.
            if (portraitRim != null)
                portraitRim.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Cyan,
                    Mathf.Lerp(0.18f, 0.42f, StrategicMapTheme.Breathe(Time.unscaledTime, 0.28f)) * group.alpha);
        }

        // ------------------------------------------------------------------ portrait

        /// <summary>Points the card at the live render of a planet, falling back to the flat sprite when there is none.</summary>
        private void ShowPlanetPortrait(StrategicPlanetView planet)
        {
            if (portraitSource != null) portraitSource.ShowPlanet(planet.Archetype);
            if (portraitSource != null && portraitSource.HasBody)
            {
                livePortrait.texture = portraitSource.Target;
                livePortrait.enabled = true;
                livePortrait.color = Color.white;
                flatPortrait.enabled = false;
                return;
            }

            livePortrait.enabled = false;
            flatPortrait.enabled = true;
            Sprite body = StrategicMapVisualResolver.PlanetThumbnail(planet.Archetype);
            flatPortrait.sprite = body != null ? body : StrategicMapShapes.Disc;
            flatPortrait.color = body != null
                ? Color.white
                : (planet.Scanned ? ExtractionCatalog.ResourceColor(planet.Family) : new Color(0.42f, 0.46f, 0.56f, 1f));
        }

        /// <summary>Points the card at the live render of a star, falling back to its produced surface texture.</summary>
        private void ShowStarPortrait(Sectors.StarClass starClass, bool known)
        {
            if (!known)
            {
                if (portraitSource != null) portraitSource.Clear();
                livePortrait.enabled = false;
                flatPortrait.enabled = true;
                flatPortrait.sprite = StrategicMapShapes.Disc;
                flatPortrait.color = new Color(0.30f, 0.34f, 0.44f, 1f);
                return;
            }

            if (portraitSource != null) portraitSource.ShowStar(starClass);
            if (portraitSource != null && portraitSource.HasBody)
            {
                livePortrait.texture = portraitSource.Target;
                livePortrait.enabled = true;
                livePortrait.color = Color.white;
                flatPortrait.enabled = false;
                return;
            }

            livePortrait.enabled = false;
            flatPortrait.enabled = true;
            Sprite star = StrategicMapVisualResolver.StarSurface(starClass);
            flatPortrait.sprite = star != null ? star : StrategicMapShapes.Disc;
            flatPortrait.color = star != null ? Color.white : StrategicMapTheme.StarColor(starClass);
        }

        private void ShowPoiPortrait(StrategicPoiView poi)
        {
            if (portraitSource != null) portraitSource.Clear();
            livePortrait.enabled = false;
            flatPortrait.enabled = true;

            if (!poi.TypeKnown)
            {
                flatPortrait.sprite = StrategicMapShapes.SoftDisc;
                flatPortrait.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Cyan, 0.64f);
                return;
            }

            Sprite thumbnail = StrategicMapVisualResolver.PoiThumbnail(poi.Type);
            flatPortrait.sprite = thumbnail != null ? thumbnail : StrategicMapShapes.Disc;
            flatPortrait.color = thumbnail != null ? Color.white : PoiRiskColor(poi.Risk);
        }

        // ------------------------------------------------------------------ planet

        /// <summary>Shows a planet, with its live portrait and its canonical 12 slots drawn on it.</summary>
        public void ShowPlanet(StrategicPlanetView planet, StrategicSystemView system) =>
            ShowPlanet(planet, system, orbitPeriodSeconds: 0f);

        /// <summary>
        /// Shows a planet. <paramref name="orbitPeriodSeconds"/> is the world's real year —
        /// how long it takes to go once round its star, which is the same figure the map is
        /// animating it at. Zero means the map does not know it, and the row is simply left out.
        /// </summary>
        public void ShowPlanet(StrategicPlanetView planet, StrategicSystemView system, float orbitPeriodSeconds)
        {
            Begin("planet:" + planet.PlanetId);

            ShowPlanetPortrait(planet);

            titleText.text = planet.DisplayName;
            subtitleText.text = planet.Scanned
                ? $"{ResourceName(planet.Family)} · {RarityName(planet.Rarity)}"
                : "Taranmamış";
            staleText.text = StaleLabel(system);

            slotsView.gameObject.SetActive(true);
            slotsView.Render(planet);

            int row = 0;
            row = Field(row, "Hâkimiyet", DominanceName(planet.Dominance),
                StrategicMapTheme.Dominance(planet.Dominance), StrategicMapVisualResolver.Dominance(planet.Dominance));
            row = Field(row, "Bağlantı", LinkName(system.Link),
                StrategicMapTheme.Link(system.Link), StrategicMapVisualResolver.Link(system.Link));

            // Where the world is and how it moves: true of every planet, scanned or not, and
            // the first thing a player picking somewhere to fly actually wants.
            if (orbitPeriodSeconds > 0f)
                row = Field(row, "Yörünge turu", FormatDuration(orbitPeriodSeconds), StrategicMapTheme.PanelText, null);
            row = Field(row, "Yıldıza uzaklık", $"{Navigation.NavigationFormat.ApproxDistance(planet.OrbitRadius):N0} birim",
                StrategicMapTheme.PanelText, null);
            row = Field(row, "Büyüklük", SizeName(planet.PlanetRadius), StrategicMapTheme.PanelText, null);

            if (planet.Scanned)
            {
                row = Field(row, "Çevre", EnvironmentName(planet.Environment),
                    StrategicMapTheme.PanelText, StrategicMapVisualResolver.Environment(planet.Environment));
                row = Field(row, "Kaynak", ResourceName(planet.Family),
                    StrategicMapTheme.PanelText, StrategicMapVisualResolver.Resource(planet.Family));
                row = Field(row, "Nadirlik", RarityName(planet.Rarity),
                    StrategicMapTheme.PanelText, StrategicMapVisualResolver.Rarity(planet.Rarity));

                if (planet.HasDrill)
                {
                    row = Field(row, "Sondaj", $"T{planet.DrillTier}", StrategicMapTheme.PanelText, null);
                    // A disconnected system never reports a live stock figure.
                    row = system.Link == SystemLinkState.Disconnected
                        ? Field(row, "Depo", "—", StrategicMapTheme.MutedText, null)
                        : Field(row, "Depo", $"%{planet.DrillFill01 * 100f:0}", StrategicMapTheme.PanelText, null);
                }

                row = Field(row, "Kurulu yapı", $"{CountBuilt(planet)}/{planet.Slots.Count}",
                    StrategicMapTheme.Green, null);
                row = Field(row, "Boş yuva", CountBuildable(planet).ToString(), StrategicMapTheme.Cyan, null);
            }
            else
            {
                row = Field(row, "Yüzey", "Tarama gerekli", StrategicMapTheme.MutedText, null);
            }

            if (planet.Abandoned) row = Field(row, "Durum", "Tahliye edilmiş", StrategicMapTheme.Gold, null);
            HideRowsFrom(row);
            AnimateEntrance(row);
        }

        // ------------------------------------------------------------------ POI

        /// <summary>
        /// Shows a real discovery point. Immutable position/type/risk come from the gated
        /// StrategicPoiView (which is built from PoiManifest); optional live progress comes
        /// only from the matching PoiLiveState. No operation is exposed here because POI
        /// work is a physical, local ShipPoi interaction rather than a remote map command.
        /// </summary>
        public void ShowPoi(StrategicPoiView poi, StrategicSystemView system, PoiState liveState)
        {
            Begin("poi:" + poi.PoiId);
            slotsView.gameObject.SetActive(false);
            ShowPoiPortrait(poi);

            PoiKnowledgeLevel level = liveState != null && liveState.PoiId == poi.PoiId
                ? liveState.Level : poi.KnowledgeLevel;
            titleText.text = poi.DisplayName;
            subtitleText.text = poi.TypeKnown
                ? $"{PoiTypeName(poi.Type)} · {(poi.RiskKnown ? PoiRiskName(poi.Risk) : "Risk bilinmiyor")}"
                : poi.PositionKnown ? "Tanımlanamayan temas · tanımlama bekliyor" : "Bilinmeyen sinyal";
            staleText.text = StaleLabel(system);

            int row = 0;
            row = Field(row, "Bilgi", PoiKnowledgeName(level), PoiKnowledgeColor(level), null);
            row = Field(row, "Bağlantı", LinkName(system.Link),
                StrategicMapTheme.Link(system.Link), StrategicMapVisualResolver.Link(system.Link));

            if (poi.PositionKnown)
                row = Field(row, "Yıldıza uzaklık",
                    $"{Navigation.NavigationFormat.ApproxDistance(poi.LocalOffset.magnitude):N0} birim",
                    StrategicMapTheme.PanelText, null);

            if (poi.TypeKnown)
            {
                row = Field(row, "Tür", PoiTypeName(poi.Type), StrategicMapTheme.PanelText, null);
                if (poi.RiskKnown)
                    row = Field(row, "Risk", PoiRiskName(poi.Risk), PoiRiskColor(poi.Risk), null);
            }
            else
            {
                row = Field(row, "Ayrıntı", poi.PositionKnown ? "Tanımlama gerekli" : "Konumlandırma gerekli",
                    StrategicMapTheme.MutedText, null);
            }

            if (TryPoiProgress01(poi, liveState, out float progress))
                row = Field(row, "İlerleme", $"%{progress * 100f:0}", StrategicMapTheme.Cyan, null);

            if (liveState != null && liveState.PoiId == poi.PoiId)
            {
                if (!string.IsNullOrEmpty(liveState.AssignedVehicleId))
                    row = Field(row, "Görevli", "Atanmış araç", StrategicMapTheme.PanelText, null);
                if (liveState.PauseReason != PoiPauseReason.None)
                    row = Field(row, "Bekleme", PoiPauseName(liveState.PauseReason), StrategicMapTheme.Gold, null);

                if (poi.TypeKnown && poi.Type == PoiType.Wreck && liveState.SalvageNodes.Count > 0)
                {
                    int remaining = 0;
                    for (int i = 0; i < liveState.SalvageNodes.Count; i++)
                        if (liveState.SalvageNodes[i].Remaining > 0.0001f) remaining++;
                    row = Field(row, "Kurtarma düğümü", $"{remaining}/{liveState.SalvageNodes.Count}",
                        remaining > 0 ? StrategicMapTheme.Gold : StrategicMapTheme.Green, null);
                }

                if (poi.TypeKnown && poi.Type == PoiType.AbandonedFacility
                    && liveState.Choice != FacilityChoice.None)
                    row = Field(row, "Tesis kararı", FacilityChoiceName(liveState.Choice),
                        StrategicMapTheme.Violet, null);
            }

            row = Field(row, "Eylem", system.IsCurrentSystem
                    ? "Yaklaşarak yerel etkileşim"
                    : "Önce sisteme rota belirle",
                StrategicMapTheme.MutedText, null);
            HideRowsFrom(row);
            AnimateEntrance(row);
        }

        public static string PoiTypeName(PoiType type) => type switch
        {
            PoiType.Anomaly => "Anomali",
            PoiType.Wreck => "Enkaz",
            PoiType.AbandonedFacility => "Terk edilmiş tesis",
            _ => "Korunan bölge"
        };

        public static string PoiRiskName(PoiRisk risk) => risk switch
        {
            PoiRisk.Safe => "Güvenli",
            PoiRisk.Unstable => "Kararsız",
            _ => "Korunan"
        };

        private static string StaleLabel(in StrategicSystemView system)
        {
            if (!system.IsStale) return "";
            return system.HasLastKnownAge
                ? $"SON BİLİNEN VERİ · {StrategicSystemStageView.FormatAge(system.LastKnownAgeSeconds)}"
                : "SON BİLİNEN VERİ";
        }

        public static string PoiKnowledgeName(PoiKnowledgeLevel level) => level switch
        {
            PoiKnowledgeLevel.Signal => "Sinyal",
            PoiKnowledgeLevel.Located => "Konumu bulundu",
            PoiKnowledgeLevel.Identified => "Tanımlandı",
            PoiKnowledgeLevel.Active => "İşlem sürüyor",
            PoiKnowledgeLevel.Resolved => "Çözüldü",
            _ => "Bilinmiyor"
        };

        public static bool TryPoiProgress01(StrategicPoiView poi, PoiState state, out float progress)
        {
            progress = 0f;
            if (state == null || state.PoiId != poi.PoiId) return false;
            if (state.Level == PoiKnowledgeLevel.Resolved) { progress = 1f; return true; }
            if (state.Level == PoiKnowledgeLevel.Located)
            {
                progress = Mathf.Clamp01(state.IdentifyElapsed / PoiCatalog.IdentifySeconds);
                return true;
            }
            if (state.Level != PoiKnowledgeLevel.Active || !poi.TypeKnown) return false;

            float required = 0f;
            if (poi.Type == PoiType.Anomaly)
                required = AnomalyService.RequiredSeconds(!string.IsNullOrEmpty(state.AssignedVehicleId), poi.Risk);
            else if (poi.Type == PoiType.AbandonedFacility)
                required = state.Choice == FacilityChoice.SensorRelay
                    ? FacilityService.RelaySeconds(poi.Risk)
                    : FacilityService.DismantleSeconds(poi.Risk);
            else if (poi.Type == PoiType.Wreck && state.SalvageNodes.Count > 0)
            {
                float total = 0f, remaining = 0f;
                for (int i = 0; i < state.SalvageNodes.Count; i++)
                {
                    total += state.SalvageNodes[i].Total;
                    remaining += state.SalvageNodes[i].Remaining;
                }
                if (total <= 0.0001f) return false;
                progress = Mathf.Clamp01(1f - remaining / total);
                return true;
            }

            if (required <= 0f) return false;
            progress = Mathf.Clamp01(state.OperationElapsed / required);
            return true;
        }

        private static string PoiPauseName(PoiPauseReason reason) => reason switch
        {
            PoiPauseReason.OutOfRange => "Menzil dışında",
            PoiPauseReason.NoAssignedWorker => "Görevli bekleniyor",
            PoiPauseReason.TargetCargoFull => "Hedef kargosu dolu",
            _ => "—"
        };

        private static string FacilityChoiceName(FacilityChoice choice) => choice switch
        {
            FacilityChoice.Dismantle => "Söküm",
            FacilityChoice.SensorRelay => "Sensör rölesi",
            _ => "—"
        };

        private static Color PoiKnowledgeColor(PoiKnowledgeLevel level) => level switch
        {
            PoiKnowledgeLevel.Signal => StrategicMapTheme.Violet,
            PoiKnowledgeLevel.Located => StrategicMapTheme.Cyan,
            PoiKnowledgeLevel.Identified => StrategicMapTheme.Gold,
            PoiKnowledgeLevel.Active => StrategicMapTheme.Green,
            PoiKnowledgeLevel.Resolved => StrategicMapTheme.Green,
            _ => StrategicMapTheme.MutedText
        };

        private static Color PoiRiskColor(PoiRisk risk) => risk switch
        {
            PoiRisk.Safe => StrategicMapTheme.Green,
            PoiRisk.Unstable => StrategicMapTheme.Gold,
            _ => StrategicMapTheme.Red
        };

        // ------------------------------------------------------------------ star / system

        /// <summary>Shows a system using the exact same layout grammar as the planet card.</summary>
        public void ShowSystem(StrategicSystemView system)
        {
            Begin("system:" + system.SystemId);
            slotsView.gameObject.SetActive(false);

            ShowStarPortrait(system.StarClass, system.DetailsKnown);

            titleText.text = system.PositionKnown ? system.DisplayName : "Bilinmeyen sinyal";
            subtitleText.text = system.DetailsKnown
                ? $"{StarName(system.StarClass)} yıldız · {system.PlanetCount} gezegen"
                : system.Knowledge == Navigation.SystemKnowledgeLevel.Signal
                    ? "Sınır sinyali · analiz bekliyor"
                    : KnowledgeName(system.Knowledge);
            staleText.text = StaleLabel(system);

            int row = 0;
            row = Field(row, "Bilgi", KnowledgeName(system.Knowledge),
                StrategicMapTheme.Knowledge(system.Knowledge), StrategicMapVisualResolver.Knowledge(system.Knowledge));
            row = Field(row, "Bağlantı", LinkName(system.Link),
                StrategicMapTheme.Link(system.Link), StrategicMapVisualResolver.Link(system.Link));

            if (system.DetailsKnown)
            {
                row = Field(row, "Yıldız", StarName(system.StarClass), StrategicMapTheme.StarColor(system.StarClass), null);
                row = Field(row, "Gezegen", system.PlanetCount.ToString(), StrategicMapTheme.PanelText, null);
                row = Field(row, "Sahiplik", OwnershipName(system.Ownership),
                    StrategicMapTheme.Ownership(system.Ownership), null);
            }
            else
            {
                if (system.Knowledge == Navigation.SystemKnowledgeLevel.Signal)
                {
                    string bearing = system.ApproximateDirection.sqrMagnitude > 0.0001f
                        ? Navigation.NavigationFormat.ApproxBearingLabel(Vector3.zero, system.ApproximateDirection)
                        : "—";
                    row = Field(row, "Yaklaşık yön", bearing, StrategicMapTheme.Violet, null);
                    row = Field(row, "Ayrıntı", "Sinyal analizi gerekli", StrategicMapTheme.MutedText, null);
                }
                else
                    row = Field(row, "Ayrıntı", "Ziyaret gerekli", StrategicMapTheme.MutedText, null);
            }

            if (system.HasRaidWarning)
                row = Field(row, "Uyarı", "Baskın", StrategicMapTheme.Red, StrategicMapVisualResolver.RaidWarning());
            else if (system.HasAttention)
                row = Field(row, "Uyarı", "İlgi gerekiyor", StrategicMapTheme.Gold, StrategicMapVisualResolver.Attention());

            HideRowsFrom(row);
            AnimateEntrance(row);
        }

        /// <summary>
        /// A real star the player can see but has not identified. The card exists so the
        /// click leads somewhere honest: it states plainly that the position is all the map
        /// knows, and what would resolve it — never a placeholder name, never invented
        /// contents, never a hint of what is actually in there.
        /// </summary>
        public void ShowUndiscoveredSystem(float exactDistanceWorldUnits, string bearingLabel)
        {
            Begin("undiscovered");
            slotsView.gameObject.SetActive(false);

            if (portraitSource != null) portraitSource.Clear();
            livePortrait.enabled = false;
            flatPortrait.enabled = true;
            flatPortrait.sprite = StrategicMapShapes.SoftDisc;
            flatPortrait.color = new Color(0.30f, 0.34f, 0.44f, 1f);

            titleText.text = "BİLİNMEYEN SİNYAL";
            subtitleText.text = "Yaklaşık yön biliniyor";
            staleText.text = "";

            // Rounded through the project's own imprecise-display helper rather than shown
            // exactly: the player has seen this star, not measured it.
            int row = 0;
            row = Field(row, "Bilgi", "Sinyal", StrategicMapTheme.Violet, null);
            row = Field(row, "Yön", bearingLabel ?? "—", StrategicMapTheme.PanelText, null);
            row = Field(row, "Ayrıntı", "Sinyal analizi gerekli", StrategicMapTheme.MutedText, null);
            HideRowsFrom(row);
            AnimateEntrance(row);
        }

        // ------------------------------------------------------------------ rows

        private int Field(int index, string label, string value, Color valueColor, Sprite icon)
        {
            if (index >= fieldPool.Count) return index;
            FieldRow row = fieldPool[index];
            row.Root.SetActive(true);
            row.Label.text = label;
            row.Value.text = value;
            row.Value.color = valueColor;
            row.Icon.sprite = icon;
            row.Icon.enabled = icon != null;
            return index + 1;
        }

        private void HideRowsFrom(int index)
        {
            for (int i = index; i < fieldPool.Count; i++)
                if (fieldPool[i].Root.activeSelf) fieldPool[i].Root.SetActive(false);
        }

        private static int CountBuildable(StrategicPlanetView planet)
        {
            int n = 0;
            for (int i = 0; i < planet.Slots.Count; i++) if (planet.Slots[i].State == StrategicSlotState.Buildable) n++;
            return n;
        }

        private static int CountBuilt(StrategicPlanetView planet)
        {
            int n = 0;
            for (int i = 0; i < planet.Slots.Count; i++)
            {
                StrategicSlotState state = planet.Slots[i].State;
                if (state != StrategicSlotState.Buildable && state != StrategicSlotState.Locked) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ names

        /// <summary>A duration in the words a player uses, not in seconds with four digits.</summary>
        public static string FormatDuration(float seconds)
        {
            if (seconds <= 0f) return "—";
            if (seconds < 90f) return $"{seconds:0} sn";
            if (seconds < 5400f) return $"{seconds / 60f:0} dk";
            return $"{seconds / 3600f:0.0} sa";
        }

        /// <summary>
        /// A world's size in words rather than a radius in engine units. The bands are the
        /// generator's own range (roughly 300-1600 units) split into three, so "büyük"
        /// genuinely means large for this game rather than large for the solar system.
        /// </summary>
        public static string SizeName(float planetRadius)
        {
            if (planetRadius <= 0f) return "—";
            if (planetRadius < 700f) return "Küçük";
            if (planetRadius < 1150f) return "Orta";
            return "Büyük";
        }

        public static string DominanceName(DominanceState state) => state switch
        {
            DominanceState.Controlled => "Kontrol altında",
            DominanceState.Developing => "Gelişiyor",
            DominanceState.Contested => "Çekişmeli",
            DominanceState.Lost => "Kaybedildi",
            _ => "Sahipsiz"
        };

        public static string LinkName(SystemLinkState link) => link switch
        {
            SystemLinkState.Live => "Canlı",
            SystemLinkState.LastKnown => "Son bilinen",
            _ => "Bağlantı yok"
        };

        public static string KnowledgeName(Navigation.SystemKnowledgeLevel level) => level switch
        {
            Navigation.SystemKnowledgeLevel.Visited => "Ziyaret edildi",
            Navigation.SystemKnowledgeLevel.Located => "Konumu bulundu",
            Navigation.SystemKnowledgeLevel.Signal => "Sinyal",
            _ => "Bilinmiyor"
        };

        public static string OwnershipName(SystemOwnershipTag tag) => tag switch
        {
            SystemOwnershipTag.DominantCore => "Tam hâkimiyet",
            SystemOwnershipTag.EstablishedBase => "Kurulu üs",
            SystemOwnershipTag.PioneerPresence => "Öncü varlık",
            SystemOwnershipTag.Discovered => "Keşfedildi",
            _ => "Keşfedilmedi"
        };

        public static string StarName(Sectors.StarClass starClass) => starClass switch
        {
            Sectors.StarClass.RedDwarf => "Kızıl Cüce",
            Sectors.StarClass.Yellow => "Sarı",
            Sectors.StarClass.White => "Beyaz",
            _ => "Mavi Dev"
        };

        public static string ResourceName(ResourceFamily family) => family switch
        {
            ResourceFamily.Metal => "Metal",
            ResourceFamily.Wood => "Biyokütle",
            ResourceFamily.Gas => "Gaz",
            ResourceFamily.Water => "Su",
            _ => "Elmas"
        };

        public static string RarityName(Rarity rarity) => rarity switch
        {
            Rarity.Common => "Yaygın",
            Rarity.Uncommon => "Az bulunur",
            Rarity.Rare => "Nadir",
            Rarity.VeryRare => "Çok nadir",
            _ => "Efsanevi"
        };

        public static string EnvironmentName(Sectors.EnvironmentClass environment) => environment switch
        {
            Sectors.EnvironmentClass.Calm => "Sakin",
            Sectors.EnvironmentClass.Harsh => "Zorlu",
            _ => "Aşırı"
        };
    }
}
