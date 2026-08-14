using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Navigation;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// Where the player is, where they are heading, and how fast.
    /// <para>
    /// Replaces two panels at once. <c>ShipHud</c>'s top-left block printed the
    /// location chain plus a raw <c>X / Y / Z</c> universe coordinate readout — a
    /// debug value the player never acts on, now moved to the dev-only HUD. Its
    /// bottom-left block printed speed and throttle as text, alongside a
    /// 137-character control-hint string pinned to the screen. <c>NavigationHud</c>
    /// printed flight mode, engine tier, heading lock, route, ETA and arrival
    /// assist as six more lines of prose in a third panel.
    /// </para>
    /// <para>
    /// All of it is here as two clusters: site and route top-left, flight
    /// bottom-left. Text updates are guarded behind value changes, because the
    /// old panels rewrote every string on a timer whether anything had moved or not.
    /// </para>
    /// </summary>
    public sealed class NebulaHudNavigation : MonoBehaviour
    {
        private ShipController ship;
        private ShipCruise cruise;
        private NavigationDirector director;

        private RectTransform siteRoot;
        private NebulaUiFactory.LabelHandle siteName, siteChain;
        private NebulaUiFactory.LabelHandle routeName, routeDistance;
        private RectTransform routeRow;

        /// <summary>The heading-lock control's object name. Tests click it by name, exactly as a player clicks it by sight.</summary>
        public const string HeadingLockButtonName = "Heading Lock";

        private NebulaUiFactory.ButtonHandle headingLock;
        /// <summary>With the route row showing. Must clear the row's own bottom.</summary>
        public const float SiteFullHeight = 118f;

        /// <summary>Name and chain only, which is the usual state.</summary>
        public const float SiteShortHeight = 76f;

        /// <summary>The route row's bottom edge, measured from the panel top.</summary>
        public const float SiteRouteBottom = 104f;

        /// <summary>
        /// The off-screen route indicator: when the selected route's star is not
        /// in view, this points at it from a ring around the reticle. It is the
        /// only thing in NavigationHud that was not a text row, and it was the
        /// only part of that HUD with no home in the redesign.
        /// </summary>
        private RectTransform routeCompass;

        /// <summary>Ring radius in pixels. Unchanged from NavigationHud.</summary>
        public const float CompassRadius = 260f;

        /// <summary>Test-facing: whether the off-screen indicator is showing.</summary>
        public bool RouteCompassVisible =>
            routeCompass != null && routeCompass.gameObject.activeSelf;

        private RectTransform flightRoot;
        private NebulaUiFactory.LabelHandle modeLabel, engineLabel, speedValue, throttleValue;
        private NebulaUiFactory.BarHandle throttleBar;
        private NebulaUiFactory.LabelHandle warningLabel;

        /// <summary>Eases the throttle bar only. The printed number stays the ship's own.</summary>
        private readonly UiValueAnimator throttleAnimation = new UiValueAnimator();

        /// <summary>Test-facing: real throttle changes the bar has reacted to.</summary>
        public int ThrottleDeltasShown => throttleAnimation.HighlightsShown;

        private float refreshTimer;
        private string lastChain;

        /// <summary>Test-facing: whether the route row is currently shown.</summary>
        public bool RouteVisible => routeRow != null && routeRow.gameObject.activeSelf;

        /// <summary>Test-facing: whether the arrival-assist warning is up.</summary>
        public bool ArrivalWarningVisible =>
            warningLabel.Text != null && warningLabel.Text.gameObject.activeSelf;

        public static NebulaHudNavigation Create(NebulaUiShell shell, ShipController shipController,
            NavigationDirector navigationDirector)
        {
            var go = new GameObject("HUD Navigation");
            var hud = go.AddComponent<NebulaHudNavigation>();
            hud.ship = shipController;
            hud.cruise = shipController != null ? shipController.GetComponent<ShipCruise>() : null;
            hud.director = navigationDirector;
            hud.Build(shell);
            return hud;
        }

        private void Build(NebulaUiShell shell)
        {
            BuildSiteCluster(shell);
            BuildFlightCluster(shell);
            BuildReticle(shell);
            BuildRouteCompass(shell);
        }

        private void BuildRouteCompass(NebulaUiShell shell)
        {
            Image arrow = NebulaUiFactory.Icon(shell.Root(UiLayer.HudDynamic), "Route Compass",
                NebulaUiAssetCatalog.RouteArrow, 28f);
            routeCompass = arrow.rectTransform;
            NebulaUiFactory.Place(routeCompass, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(28f, 28f));
            arrow.color = NebulaUiDesignTokens.ActionPrimary;
            routeCompass.gameObject.SetActive(false);
        }

        /// <summary>
        /// Points at the route star while it is off screen, and gets out of the way
        /// the moment it is not. Same ring radius and same on-screen margin as the
        /// HUD this replaces, so the indicator appears and disappears where players
        /// already expect it to.
        /// </summary>
        private void RefreshRouteCompass()
        {
            if (routeCompass == null) return;

            bool hasRoute = director != null && director.SelectedRouteSystemId != null
                && director.TryGetSystem(director.SelectedRouteSystemId, out StarSystemRecord route)
                && route.StarTransform != null;

            Camera cam = hasRoute ? Camera.main : null;
            if (cam == null)
            {
                if (routeCompass.gameObject.activeSelf) routeCompass.gameObject.SetActive(false);
                return;
            }

            director.TryGetSystem(director.SelectedRouteSystemId, out StarSystemRecord target);
            Vector3 targetPos = target.StarTransform.position;
            Vector3 viewport = cam.WorldToViewportPoint(targetPos);
            bool onScreen = viewport.z > 0f
                && viewport.x > 0.05f && viewport.x < 0.95f
                && viewport.y > 0.05f && viewport.y < 0.95f;

            if (onScreen)
            {
                if (routeCompass.gameObject.activeSelf) routeCompass.gameObject.SetActive(false);
                return;
            }

            Vector3 local = cam.transform.InverseTransformDirection(
                (targetPos - cam.transform.position).normalized);
            float angle = Mathf.Atan2(local.x, local.y);
            if (local.z < 0f) angle += Mathf.PI;

            routeCompass.anchoredPosition =
                new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * CompassRadius;
            routeCompass.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
            if (!routeCompass.gameObject.activeSelf) routeCompass.gameObject.SetActive(true);
        }

        /// <summary>
        /// The centre reticle. ShipHud drew a bare 5px quad here; this is the
        /// authored one, and it honours the existing reticle accessibility
        /// settings rather than being a fixed size.
        /// </summary>
        private void BuildReticle(NebulaUiShell shell)
        {
            Image reticle = NebulaUiFactory.Icon(shell.Root(UiLayer.HudDynamic), "Reticle",
                NebulaUiAssetCatalog.Reticle(NebulaUiAssetCatalog.ReticleState.Idle), 48f);
            RectTransform rect = reticle.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            float size = 48f * Mathf.Clamp(Combat.CombatAccessibilitySettings.ReticleSize, 0.5f, 2f);
            rect.sizeDelta = new Vector2(size, size);
            reticle.color = NebulaUiDesignTokens.WithAlpha(NebulaUiDesignTokens.InkPrimary, 0.85f);
        }

        private void BuildSiteCluster(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudStatic), "Site",
                NebulaUiAssetCatalog.PanelPrimary);
            siteRoot = panel.rectTransform;
            NebulaUiFactory.Place(siteRoot, new Vector2(0f, 1f), new Vector2(32f, -26f),
                new Vector2(392f, SiteFullHeight));
            // Height follows the route row, which is usually absent. A panel padded
            // for content it does not have reads as a rendering fault, not as calm.

            // The controller lives on the layer root, never inside the visual it
            // toggles: a widget parented into its own panel is switched off
            // together with it, its Update stops, and it can never decide to
            // come back. Hiding the panel must not disable the thing that shows it.
            transform.SetParent(shell.Root(UiLayer.HudStatic), false);

            siteName = NebulaUiFactory.Label(siteRoot, "Name", "—", NebulaUiDesignTokens.SizeScreenTitle,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingScreenTitle);
            NebulaUiFactory.Place(siteName.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -14f), new Vector2(356f, 26f));

            siteChain = NebulaUiFactory.Label(siteRoot, "Chain", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(siteChain.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -44f), new Vector2(356f, 16f));

            Image divider = NebulaUiFactory.Rect(siteRoot, "Divider", NebulaUiDesignTokens.Divider);
            NebulaUiFactory.Place(divider.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -66f), new Vector2(356f, 1f));

            routeRow = NebulaUiFactory.Group(siteRoot, "Route");
            NebulaUiFactory.Place(routeRow, new Vector2(0f, 1f), new Vector2(18f, -76f),
                new Vector2(356f, 32f));

            Image arrow = NebulaUiFactory.Icon(routeRow, "Arrow", NebulaUiAssetCatalog.RouteArrow, 14f);
            NebulaUiFactory.Place(arrow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -4f),
                new Vector2(14f, 14f));
            arrow.color = NebulaUiDesignTokens.ActionPrimary;

            routeName = NebulaUiFactory.Label(routeRow, "Target", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.UpperLeft);
            NebulaUiFactory.Place(routeName.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(22f, -2f), new Vector2(180f, 18f));

            routeDistance = NebulaUiFactory.Value(routeRow, "Distance", "", NebulaUiDesignTokens.SizeCaption);
            NebulaUiFactory.Place(routeDistance.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(0f, -2f), new Vector2(200f, 18f));

            routeRow.gameObject.SetActive(false);
            siteRoot.sizeDelta = new Vector2(siteRoot.sizeDelta.x, SiteShortHeight);
        }

        private void BuildFlightCluster(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudDynamic), "Flight",
                NebulaUiAssetCatalog.PanelPrimary);
            flightRoot = panel.rectTransform;
            // 136, not 156: the throttle bar bottoms out at -121, and the extra
            // 35px read as a panel that failed to draw something.
            NebulaUiFactory.Place(flightRoot, new Vector2(0f, 0f), new Vector2(32f, 26f),
                new Vector2(300f, 136f));

            modeLabel = NebulaUiFactory.Label(flightRoot, "Mode", "YEREL", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Nominal), TextAnchor.MiddleLeft,
                FontStyle.Bold, NebulaUiDesignTokens.TrackingSectionTitle);
            NebulaUiFactory.Place(modeLabel.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -16f), new Vector2(180f, 16f));

            engineLabel = NebulaUiFactory.Label(flightRoot, "Engine", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleRight, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(engineLabel.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-18f, -16f), new Vector2(140f, 16f));

            speedValue = NebulaUiFactory.Label(flightRoot, "Speed", "0", 34,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.LowerLeft);
            NebulaUiFactory.Place(speedValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -44f), new Vector2(200f, 38f));

            NebulaUiFactory.Place(NebulaUiFactory.Label(flightRoot, "Unit", "u/sn",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.LowerLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption)
                .Text.rectTransform, new Vector2(0f, 1f), new Vector2(96f, -60f), new Vector2(80f, 16f));

            throttleValue = NebulaUiFactory.Value(flightRoot, "ThrottleValue", "%0",
                NebulaUiDesignTokens.SizeCaption);
            NebulaUiFactory.Place(throttleValue.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-18f, -92f), new Vector2(120f, 14f));

            NebulaUiFactory.Place(NebulaUiFactory.Label(flightRoot, "ThrottleLabel", "GAZ",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption)
                .Text.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -92f), new Vector2(120f, 14f));

            throttleBar = NebulaUiFactory.Bar(flightRoot, "Throttle", NebulaUiDesignTokens.ActionPrimary);
            RectTransform bar = throttleBar.Track.rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(18f, -114f - NebulaUiDesignTokens.BarHeight);
            bar.offsetMax = new Vector2(-18f, -114f);

            // Parity: ShipHud's plus-shaped widget owned throttle dragging. Losing
            // that when its canvas goes dark would be removing a control, not
            // replacing it, so the same gesture lives on this bar -- horizontally,
            // because this bar is horizontal.
            RectTransform surface = NebulaUiFactory.Group(flightRoot, "Throttle Input");
            surface.anchorMin = new Vector2(0f, 1f);
            surface.anchorMax = new Vector2(1f, 1f);
            surface.pivot = new Vector2(0.5f, 1f);
            surface.offsetMin = new Vector2(18f, -126f);
            surface.offsetMax = new Vector2(-18f, -104f);
            var surfaceImage = surface.gameObject.AddComponent<Image>();
            surfaceImage.color = new Color(0f, 0f, 0f, 0f);
            surfaceImage.raycastTarget = true;
            var drag = surface.gameObject.AddComponent<HorizontalThrottleInput>();
            drag.Ship = ship;
            drag.Track = bar;

            // A control, not a badge.
            //
            // Yön Sabitle was reachable by exactly one route -- holding H -- and this
            // line was a label that lit up after the fact to say it had happened. So
            // the feature had no button anywhere, and it died completely whenever
            // InputGate went up. It is now a real Button on the same authored
            // secondary art as every other control, it prints the current prompt for
            // the (rebindable) flight.heading_lock row, and it calls the same
            // ShipCruise.ToggleHeadingLock the key reaches through the router. Mouse,
            // keyboard focus and gamepad all land on it because it is a Selectable.
            headingLock = NebulaUiFactory.ButtonControl(flightRoot, HeadingLockButtonName, "",
                NebulaUiAssetCatalog.ButtonKind.Secondary, OnToggleHeadingLock);
            NebulaUiFactory.Place(headingLock.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(18f, 8f), new Vector2(168f, 26f));

            warningLabel = NebulaUiFactory.Label(flightRoot, "Warning", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Warning), TextAnchor.MiddleRight,
                FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(warningLabel.Text.rectTransform, new Vector2(1f, 0f),
                new Vector2(-18f, 14f), new Vector2(200f, 14f));
            warningLabel.Text.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (ship == null) return;

            // Speed and throttle move every frame; the location chain does not.
            RefreshFlight();

            // The indicator tracks the camera, so it cannot run on the slow timer.
            RefreshRouteCompass();

            refreshTimer -= Time.deltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 0.25f;

            RefreshSite();
            RefreshRoute();
        }

        /// <summary>Test-facing: whether the heading-lock control reports the lock as engaged.</summary>
        public bool HeadingLockEngagedShown =>
            cruise != null && cruise.HeadingLockEngaged;

        private void OnToggleHeadingLock()
        {
            cruise?.ToggleHeadingLock();
            RefreshHeadingLock();
        }

        /// <summary>
        /// The control states what it will do next and what is true now, in that
        /// order: the caption is the verb, the prompt is the current key. Both halves
        /// come from live state, so a rebind or a lock taken from the keyboard is
        /// reflected here without the control having its own idea of either.
        /// </summary>
        private void RefreshHeadingLock()
        {
            if (headingLock == null || cruise == null) return;

            bool locked = cruise.HeadingLockEngaged;
            string prompt = NebulaHudContextBar.KeyPrompt(
                NebulaInputActions.Key(NebulaInputActions.FlightHeadingLock));
            headingLock.SetCaption(string.IsNullOrEmpty(prompt)
                ? (locked ? "YÖN SABİT — BIRAK" : "YÖN SABİTLE")
                : (locked ? $"{prompt}  YÖN SABİT — BIRAK" : $"{prompt}  YÖN SABİTLE"));

            headingLock.Label.Text.color = locked
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Working)
                : NebulaUiDesignTokens.InkPrimary;

            // Engaging needs a heading to hold, which needs the cursor captured -- the
            // same condition HandleHeadingLockInput releases on. Never off without a
            // reason on it.
            bool usable = locked || Cursor.lockState == CursorLockMode.Locked;
            headingLock.SetAvailable(usable, "YÖN SABİTLEMEK İÇİN İMLECİ KİLİTLEYİN");
        }

        private void RefreshFlight()
        {
            speedValue.Value = ship.Velocity.magnitude.ToString("0");

            float fraction = ship.GetThrottleFraction();
            throttleValue.Value = $"%{fraction * 100f:0}";
            // Only the bar eases; the printed percentage is always the real value.
            throttleBar.SetFraction(throttleAnimation.Tick(fraction, Time.unscaledDeltaTime));

            if (cruise == null) return;

            modeLabel.Value = ModeName(cruise.Mode);
            engineLabel.Value = $"MOTOR T{cruise.EngineTier}";

            RefreshHeadingLock();
        }

        private void RefreshSite()
        {
            Vector3 position = ship.transform.position;
            string chain = BuildLocationChain(position, out string site);

            // Guarded: the old panel rewrote both strings on every timer tick even
            // when the ship had not left the planet it was orbiting.
            if (!string.Equals(chain, lastChain, System.StringComparison.Ordinal))
            {
                lastChain = chain;
                siteChain.Value = chain;
                siteName.Value = site;
            }
        }

        private void RefreshRoute()
        {
            bool hasRoute = director != null
                            && director.SelectedRouteSystemId != null
                            && director.TryGetSystem(director.SelectedRouteSystemId, out StarSystemRecord route);

            if (routeRow.gameObject.activeSelf != hasRoute)
            {
                routeRow.gameObject.SetActive(hasRoute);
                // Shrink to what is actually in it. Otherwise the panel keeps a
                // route row's worth of empty space under a rule that separates
                // nothing from nothing.
                siteRoot.sizeDelta = new Vector2(siteRoot.sizeDelta.x,
                    hasRoute ? SiteFullHeight : SiteShortHeight);
            }
            if (!hasRoute)
            {
                if (warningLabel.Text.gameObject.activeSelf) warningLabel.Text.gameObject.SetActive(false);
                return;
            }

            director.TryGetSystem(director.SelectedRouteSystemId, out StarSystemRecord target);
            float distance = director.AbsoluteDistanceTo(target);
            float speed = ship.Velocity.magnitude;
            float eta = ArrivalAssist.EtaSeconds(distance, speed);

            routeName.Value = target.DisplayName;
            routeDistance.Value = eta > 0f
                ? $"{distance:#,0} u  ·  {FormatDuration(eta)}"
                : $"{distance:#,0} u  ·  HIZ YETERSİZ";

            bool warn = cruise != null
                        && cruise.Mode != FlightMode.LocalFlight
                        && ArrivalAssist.ShouldWarn(distance, speed, cruise.LocalMaxSpeedForTier,
                            CruiseStateMachine.DecelSeconds);

            if (warningLabel.Text.gameObject.activeSelf != warn)
                warningLabel.Text.gameObject.SetActive(warn);
            if (warn) warningLabel.Value = "YAVAŞLAMAYA BAŞLA";
        }

        // ---- copy ------------------------------------------------------------

        internal static string ModeName(FlightMode mode)
        {
            switch (mode)
            {
                case FlightMode.LocalFlight: return "YEREL";
                case FlightMode.CruiseRamp: return "HIZLANMA";
                case FlightMode.DeepSpaceCruise: return "SEYİR";
                case FlightMode.CruiseDeceleration: return "YAVAŞLAMA";
                default: return "YEREL";
            }
        }

        internal static string FormatDuration(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.RoundToInt(seconds);
            int minutes = total / 60;
            return minutes > 0 ? $"{minutes}dk {total % 60:00}sn" : $"{total}sn";
        }

        /// <summary>
        /// GALAKSİ / kol / yıldız / gezegen, dropping the levels that do not apply.
        /// The deepest known level becomes the headline; the rest is the chain
        /// beneath it. No absolute coordinate appears — that is a debug value.
        /// </summary>
        private static string BuildLocationChain(Vector3 position, out string site)
        {
            site = "BİLİNMEYEN BÖLGE";
            string galaxyName = LocationRegistry.NearestGalaxy(position, out GalaxyMap galaxyMap);

            var chain = new System.Text.StringBuilder(96);
            if (!string.IsNullOrEmpty(galaxyName)) chain.Append(NebulaText.Upper(galaxyName));

            if (galaxyMap != null)
            {
                int arm = galaxyMap.ArmIndexAt(position, out bool inCore);
                if (inCore) Append(chain, "ÇEKİRDEK");
                else if (arm >= 0) Append(chain, $"KOL {arm + 1}");
            }

            LocationRegistry.GetLocation(position, out string star, out string planet);
            if (star != null)
            {
                Append(chain, NebulaText.Upper(star));
                site = planet != null ? NebulaText.Upper(planet) : NebulaText.Upper(star);
                if (planet != null) Append(chain, "YÖRÜNGE");
            }
            else
            {
                site = "YILDIZLAR ARASI";
            }

            return chain.ToString();
        }

        private static void Append(System.Text.StringBuilder builder, string value)
        {
            if (builder.Length > 0) builder.Append("  /  ");
            builder.Append(value);
        }
    }

    /// <summary>
    /// Click-and-drag on the throttle bar. The horizontal counterpart of the
    /// vertical gesture ShipHud's widget carried, kept so the control survives
    /// that panel being switched off.
    /// </summary>
    public sealed class HorizontalThrottleInput : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public ShipController Ship;
        public RectTransform Track;

        public void OnPointerDown(PointerEventData eventData) => Apply(eventData);

        public void OnDrag(PointerEventData eventData) => Apply(eventData);

        private void Apply(PointerEventData eventData)
        {
            if (Ship == null || Track == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    Track, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;

            float width = Track.rect.width;
            if (width <= 0f) return;
            Ship.SetThrottleFraction(Mathf.Clamp01((local.x - Track.rect.xMin) / width));
        }
    }
}
