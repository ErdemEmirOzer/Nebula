using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The system view: the real star (its own produced surface + additive corona
    /// texture), its real orbits, its real planets (rendered from the actual planet
    /// prefabs via <see cref="StrategicMapVisualResolver"/>) and any known POIs.
    ///
    /// Named "stage" to avoid colliding with the <see cref="StrategicSystemView"/>
    /// view-model struct; it is the component the design calls the system view.
    ///
    /// Everything is pooled. Dominance is expressed with BOTH a colour and a real
    /// icon plus a ring, so colour is never the only carrier: a Controlled planet
    /// gets a soft green/cyan orbit ring and a controlled badge, Contested/Lost get
    /// their own badge and a warning pulse that reduced motion switches off.
    /// </summary>
    public sealed class StrategicSystemStageView : MonoBehaviour
    {
        private const int MaxPlanets = 12;
        private const int MaxPois = 4;

        public event System.Action<string> PlanetClicked;

        private RectTransform viewRect;
        private Image starSurface;
        private Image starCorona;
        private Image dominanceHalo;
        private Text starLabel;
        private Text staleLabel;

        private readonly List<PlanetWidget> planetPool = new List<PlanetWidget>();
        private readonly List<Image> orbitPool = new List<Image>();
        private readonly List<Image> poiPool = new List<Image>();

        private string selectedPlanetId;

        private sealed class PlanetWidget
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Body;
            public Image DominanceIcon;
            public Image EnvironmentIcon;
            public Text Label;
            public Button Button;
            public string PlanetId;
        }

        public static StrategicSystemStageView Create(RectTransform parent)
        {
            var go = new GameObject("System Stage");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(400f, 40f);
            rect.offsetMax = new Vector2(-40f, -110f);

            var view = go.AddComponent<StrategicSystemStageView>();
            view.viewRect = rect;
            view.BuildUi();
            go.SetActive(false);
            return view;
        }

        private void BuildUi()
        {
            // Orbits first (behind everything), then the star, then planets on top.
            for (int i = 0; i < MaxPlanets; i++) orbitPool.Add(BuildOrbit(i));

            dominanceHalo = StrategicMapScreen.CreateImage(viewRect, "Dominance Halo", Color.clear);
            dominanceHalo.sprite = StrategicMapShapes.Ring(4);
            CenterSized(dominanceHalo.rectTransform, 340f);

            starCorona = StrategicMapScreen.CreateImage(viewRect, "Star Corona", Color.white);
            CenterSized(starCorona.rectTransform, 260f);

            // The produced star SURFACE map is equirectangular (2:1), so drawing it flat gave a
            // rectangle. It lives inside a circular mask instead, which is what a star looks like
            // while still showing the real produced texture.
            var starDisc = new GameObject("Star Disc");
            var starDiscRect = starDisc.AddComponent<RectTransform>();
            starDiscRect.SetParent(viewRect, false);
            CenterSized(starDiscRect, 150f);
            var discImage = starDisc.AddComponent<Image>();
            discImage.sprite = StrategicMapShapes.Disc;
            discImage.raycastTarget = false;
            starDisc.AddComponent<Mask>().showMaskGraphic = false;

            starSurface = StrategicMapScreen.CreateImage(starDiscRect, "Star Surface", Color.white);
            starSurface.rectTransform.anchorMin = Vector2.zero;
            starSurface.rectTransform.anchorMax = Vector2.one;
            starSurface.rectTransform.offsetMin = new Vector2(-75f, 0f);
            starSurface.rectTransform.offsetMax = new Vector2(75f, 0f);

            starLabel = StrategicMapScreen.CreateText(viewRect, "Star Label", "", StrategicMapTheme.HeadingSize,
                StrategicMapTheme.Gold, TextAnchor.MiddleCenter);
            starLabel.rectTransform.anchorMin = starLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            starLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            starLabel.rectTransform.anchoredPosition = new Vector2(0f, -80f);
            starLabel.rectTransform.sizeDelta = new Vector2(520f, 30f);

            staleLabel = StrategicMapScreen.CreateText(viewRect, "Stale Label", "", StrategicMapTheme.CaptionSize,
                StrategicMapTheme.Gold, TextAnchor.MiddleCenter);
            staleLabel.rectTransform.anchorMin = staleLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            staleLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            staleLabel.rectTransform.anchoredPosition = new Vector2(0f, -112f);
            staleLabel.rectTransform.sizeDelta = new Vector2(520f, 24f);

            for (int i = 0; i < MaxPois; i++) poiPool.Add(BuildPoi(i));
            for (int i = 0; i < MaxPlanets; i++) planetPool.Add(BuildPlanet(i));
        }

        private Image BuildOrbit(int index)
        {
            Image orbit = StrategicMapScreen.CreateImage(viewRect, $"Orbit {index}", Color.clear);
            // An Image with no sprite draws a filled quad, which turned every orbit into a
            // nested translucent square. A real ring is what an orbit has to be.
            orbit.sprite = StrategicMapShapes.Ring(2);
            orbit.type = Image.Type.Simple;
            CenterSized(orbit.rectTransform, 100f);
            orbit.gameObject.SetActive(false);
            return orbit;
        }

        private Image BuildPoi(int index)
        {
            Image poi = StrategicMapScreen.CreateImage(viewRect, $"POI {index}", StrategicMapTheme.Violet);
            poi.sprite = StrategicMapShapes.Disc;
            poi.rectTransform.anchorMin = poi.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            poi.rectTransform.sizeDelta = new Vector2(20f, 20f);
            poi.gameObject.SetActive(false);
            return poi;
        }

        private PlanetWidget BuildPlanet(int index)
        {
            var go = new GameObject($"Planet {index}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(viewRect, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(56f, 56f);

            var body = go.AddComponent<Image>();
            body.sprite = StrategicMapShapes.Disc; // a planet is a disc, never a square
            var button = go.AddComponent<Button>();
            button.targetGraphic = body;

            var widget = new PlanetWidget { Root = go, Rect = rect, Body = body, Button = button };
            button.onClick.AddListener(() => { if (!string.IsNullOrEmpty(widget.PlanetId)) PlanetClicked?.Invoke(widget.PlanetId); });

            widget.DominanceIcon = StrategicMapScreen.CreateImage(rect, "Dominance", Color.white);
            widget.DominanceIcon.rectTransform.anchorMin = widget.DominanceIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            widget.DominanceIcon.rectTransform.anchoredPosition = new Vector2(22f, 22f);
            widget.DominanceIcon.rectTransform.sizeDelta = new Vector2(22f, 22f);
            widget.DominanceIcon.preserveAspect = true;

            widget.EnvironmentIcon = StrategicMapScreen.CreateImage(rect, "Environment", Color.white);
            widget.EnvironmentIcon.rectTransform.anchorMin = widget.EnvironmentIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            widget.EnvironmentIcon.rectTransform.anchoredPosition = new Vector2(-22f, 22f);
            widget.EnvironmentIcon.rectTransform.sizeDelta = new Vector2(20f, 20f);
            widget.EnvironmentIcon.preserveAspect = true;

            widget.Label = StrategicMapScreen.CreateText(rect, "Label", "", StrategicMapTheme.CaptionSize,
                StrategicMapTheme.SpaceText, TextAnchor.UpperCenter);
            widget.Label.rectTransform.anchorMin = widget.Label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            widget.Label.rectTransform.pivot = new Vector2(0.5f, 1f);
            widget.Label.rectTransform.anchoredPosition = new Vector2(0f, -32f);
            widget.Label.rectTransform.sizeDelta = new Vector2(190f, 22f);

            go.SetActive(false);
            return widget;
        }

        private static void CenterSized(RectTransform rect, float size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);
        }

        public void SetSelectedPlanet(string planetId) => selectedPlanetId = planetId;

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        /// <summary>Draws one system. Never reads world state itself — the snapshot already applied every gate.</summary>
        public void Render(StrategicSystemView system)
        {
            if (!system.DetailsKnown)
            {
                // Not visited: there is nothing the player has earned the right to see here.
                starSurface.enabled = false;
                starSurface.transform.parent.gameObject.SetActive(false);
                starCorona.enabled = false;
                starLabel.text = "Bu sistem henüz ziyaret edilmedi.";
                staleLabel.text = "";
                HideAll();
                return;
            }

            // --- the real star -------------------------------------------------------
            Sprite surface = StrategicMapVisualResolver.StarSurface(system.StarClass);
            Sprite corona = StrategicMapVisualResolver.StarCorona(system.StarClass);
            starSurface.sprite = surface;
            starSurface.enabled = surface != null;
            starSurface.transform.parent.gameObject.SetActive(surface != null);
            starCorona.sprite = corona;
            starCorona.enabled = corona != null;
            if (corona != null)
            {
                float breathe = 1f + StrategicMapTheme.Pulse(Time.unscaledTime, 0.03f);
                starCorona.rectTransform.localScale = Vector3.one * breathe;
                starCorona.color = new Color(1f, 1f, 1f, 0.55f);
            }

            starLabel.text = system.DisplayName;
            staleLabel.text = !system.IsStale
                ? ""
                : system.HasLastKnownAge
                    ? $"SON BİLİNEN VERİ · {FormatAge(system.LastKnownAgeSeconds)}"
                    : "SON BİLİNEN VERİ";

            // A fully dominant system gets a wider halo plus a clear badge colour.
            bool fullyDominant = system.Ownership == SystemOwnershipTag.DominantCore;
            dominanceHalo.color = fullyDominant
                ? new Color(StrategicMapTheme.Gold.r, StrategicMapTheme.Gold.g, StrategicMapTheme.Gold.b, 0.16f)
                : Color.clear;
            dominanceHalo.rectTransform.localScale = Vector3.one * (fullyDominant ? 1f + StrategicMapTheme.Pulse(Time.unscaledTime, 0.04f) : 1f);

            RenderPlanets(system);
            RenderPois(system);
        }

        private void RenderPlanets(StrategicSystemView system)
        {
            float maxOrbit = 1f;
            for (int i = 0; i < system.Planets.Count; i++) maxOrbit = Mathf.Max(maxOrbit, system.Planets[i].OrbitRadius);

            float available = Mathf.Min(viewRect.rect.width, viewRect.rect.height) * 0.42f;
            int used = 0;

            for (int i = 0; i < system.Planets.Count && used < planetPool.Count; i++)
            {
                StrategicPlanetView planet = system.Planets[i];
                float normalized = Mathf.Clamp01(planet.OrbitRadius / maxOrbit);
                float radius = Mathf.Lerp(available * 0.32f, available, normalized);
                float angle = planet.OrbitAngleDegrees * Mathf.Deg2Rad;
                var position = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

                // Orbit ring: green/cyan for a Controlled planet, neutral otherwise.
                Image orbit = orbitPool[used];
                orbit.gameObject.SetActive(true);
                orbit.rectTransform.sizeDelta = new Vector2(radius * 2f, radius * 2f);
                orbit.color = planet.Dominance == DominanceState.Controlled
                    ? new Color(StrategicMapTheme.Green.r, StrategicMapTheme.Green.g, StrategicMapTheme.Green.b, 0.22f)
                    : new Color(StrategicMapTheme.Blue.r, StrategicMapTheme.Blue.g, StrategicMapTheme.Blue.b, 0.09f);

                PlanetWidget widget = planetPool[used++];
                widget.PlanetId = planet.PlanetId;
                widget.Root.SetActive(true);
                widget.Rect.anchoredPosition = position;

                bool selected = planet.PlanetId == selectedPlanetId;
                float scale = selected ? 1.2f : 1f;
                bool warns = planet.Dominance == DominanceState.Contested || planet.Dominance == DominanceState.Lost;
                if (warns) scale += StrategicMapTheme.Pulse(Time.unscaledTime, 0.1f);
                widget.Rect.localScale = Vector3.one * scale;

                // The real planet prefab preview, but only once the planet is scanned --
                // the archetype itself would otherwise leak the resource family.
                // At marker size a rendered prefab preview reads as a dark smudge, so the body is
                // a clean disc carrying the planet's OWN real resource-family colour once scanned.
                // The full prefab preview still appears on the detail card, where it is large
                // enough to actually be seen.
                widget.Body.sprite = StrategicMapShapes.Disc;
                widget.Body.color = PlanetBodyColor(planet, selected);

                widget.Label.text = planet.DisplayName;
                widget.Label.color = selected ? StrategicMapTheme.PanelText : StrategicMapTheme.MutedText;

                Sprite dominanceIcon = StrategicMapVisualResolver.Dominance(planet.Dominance);
                widget.DominanceIcon.sprite = dominanceIcon;
                widget.DominanceIcon.enabled = dominanceIcon != null;

                Sprite environmentIcon = planet.Scanned ? StrategicMapVisualResolver.Environment(planet.Environment) : null;
                widget.EnvironmentIcon.sprite = environmentIcon;
                widget.EnvironmentIcon.enabled = environmentIcon != null;
            }

            for (int i = used; i < planetPool.Count; i++)
                if (planetPool[i].Root.activeSelf) { planetPool[i].Root.SetActive(false); planetPool[i].PlanetId = null; }
            for (int i = used; i < orbitPool.Count; i++)
                if (orbitPool[i].gameObject.activeSelf) orbitPool[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// A scanned planet shows its real resource-family colour (the same
        /// <see cref="ExtractionCatalog.ResourceColor"/> the rest of the game uses); an unscanned
        /// one stays a neutral silhouette, so the marker never leaks what a scan has not revealed.
        /// </summary>
        private static Color PlanetBodyColor(StrategicPlanetView planet, bool selected)
        {
            Color c = planet.Scanned
                ? ExtractionCatalog.ResourceColor(planet.Family)
                : new Color(0.42f, 0.46f, 0.56f, 1f);
            if (planet.Dominance == DominanceState.Contested || planet.Dominance == DominanceState.Lost)
                c = Color.Lerp(c, StrategicMapTheme.Red, 0.35f);
            return selected ? Color.Lerp(c, Color.white, 0.35f) : c;
        }

        private void RenderPois(StrategicSystemView system)
        {
            int used = 0;
            for (int i = 0; i < system.Pois.Count && used < poiPool.Count; i++)
            {
                Image poi = poiPool[used];
                poi.gameObject.SetActive(true);
                float angle = (used * 90f + 45f) * Mathf.Deg2Rad;
                float radius = Mathf.Min(viewRect.rect.width, viewRect.rect.height) * 0.22f;
                poi.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                poi.color = StrategicMapTheme.Violet;
                used++;
            }
            for (int i = used; i < poiPool.Count; i++)
                if (poiPool[i].gameObject.activeSelf) poiPool[i].gameObject.SetActive(false);
        }

        private void HideAll()
        {
            foreach (PlanetWidget w in planetPool) if (w.Root.activeSelf) { w.Root.SetActive(false); w.PlanetId = null; }
            foreach (Image o in orbitPool) if (o.gameObject.activeSelf) o.gameObject.SetActive(false);
            foreach (Image p in poiPool) if (p.gameObject.activeSelf) p.gameObject.SetActive(false);
            dominanceHalo.color = Color.clear;
        }

        public static string FormatAge(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "süre bilinmiyor";
            seconds = Mathf.Max(0f, seconds);
            if (seconds < 60f) return $"{seconds:0} sn önce";
            if (seconds < 3600f) return $"{seconds / 60f:0} dk önce";
            return $"{seconds / 3600f:0.0} sa önce";
        }
    }
}
