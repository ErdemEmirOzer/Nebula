using UnityEngine;
using UnityEngine.UI;
using Nebula.Poi;
using Nebula.Ship;
using Nebula.World.Composition;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The native strategic map modal: the single owner of the M key.
    ///
    /// It does NOT freeze the game with <c>Time.timeScale = 0</c>. The simulation
    /// keeps running while the player reads the map (which is also what a future
    /// online build requires); ship/world/combat input is suppressed through the
    /// existing <see cref="InputGate"/> and <see cref="ExclusiveModal"/> instead,
    /// so opening the map can never be used as a free pause.
    ///
    /// This used to be the map's own private rule — every other screen stopped the
    /// clock. <see cref="Shell.NebulaUiShell"/> now does the same thing for the
    /// whole stack, so the map is the pattern rather than the exception. The map
    /// still raises the gate itself because it is not a shell screen: it has no
    /// entry on the back stack and nothing else would raise it.
    ///
    /// The screen owns no world data: it renders whatever the read-only
    /// <see cref="StrategicMapDataSource"/> snapshot says, on a controlled refresh
    /// interval rather than every frame.
    /// </summary>
    public sealed class StrategicMapScreen : MonoBehaviour
    {
        public const KeyCode ToggleKey = KeyCode.M;

        /// <summary>How often the snapshot is rebuilt while open. Never per-frame: a large galaxy must not be re-derived 60x a second.</summary>
        private const float RefreshIntervalSeconds = 0.5f;

        public bool IsOpen { get; private set; }
        public StrategicMapPresenter Presenter { get; } = new StrategicMapPresenter();
        public StrategicMapSnapshot Snapshot { get; private set; }

        private NebulaContext context;
        private StrategicMapDataSource dataSource;
        private Preview.IStrategicMapUniverseSource universeSource;
        /// <summary>What the current 3D geometry was built from; when the gameplay snapshot stops matching it, the universe is rebuilt.</summary>
        private int universeSignature;
        private GameObject root;
        private Text breadcrumbText;
        private Text bodyText;
        private StrategicMapResourcePanel resourcePanel;
        private StrategicMapGraphView graphView;
        private StrategicSystemStageView systemStage;
        private StrategicObjectCard card;
        private StrategicBodyPortrait bodyPortrait;
        private StrategicMapCommands commands;
        private Text commandHintText;
        private Preview.StrategicMapUniverseRenderer universeRenderer;
        private StrategicMapHud hud;
        private StrategicMapCommandDeck commandDeck;
        private RawImage universeSurface;
        private CanvasScaler canvasScaler;

        /// <summary>A located-but-unidentified star the player has clicked. Never a presenter level: there is nothing inside it to show yet.</summary>
        private string undiscoveredSelectionId;
        /// <summary>A Located+ POI selected inside the active system. Kept beside the presenter because POI is a detail subject, not a new map level.</summary>
        private string selectedPoiId;

        private bool dragging;
        private Vector2 lastDragPosition;
        private Vector2 dragPressPosition;
        private string hoveredId;
        private Vector2 dragVelocity;
        private Preview.StrategicMapPick pressedPick = Preview.StrategicMapPick.None;

        private float nextRefreshTime;
        private CursorLockMode savedCursorLock;
        private bool savedCursorVisible;
        private bool savedInputGateSuppressed;

        public static StrategicMapScreen Create(NebulaContext context)
        {
            var go = new GameObject("Strategic Map Screen");
            var screen = go.AddComponent<StrategicMapScreen>();
            screen.context = context;
            screen.dataSource = new StrategicMapDataSource(context);
            // The LIVE universe: real ids, real knowledge gates. The static HTML preview
            // source stays only as a test fixture for the renderer's own tests.
            screen.universeSource = new StrategicMapLiveUniverseSource(context);
            screen.commands = new StrategicMapCommands(context);
            screen.BuildUi();
            screen.root.SetActive(false);
            return screen;
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            // Through the router, so the Global context's own rule applies: a screen
            // raises InputGate, IsContextActive(Global) goes false, and the press never
            // reaches here at all. Sampling Input directly meant the key was read under
            // an open menu and then refused by ExclusiveModal with nothing said — the
            // player pressed M inside the command menu and the game did nothing and
            // explained nothing. Suppression while a modal owns input is the right
            // answer, and it comes back on the very frame the shell restores the gate.
            if (NebulaInputRouter.Pressed(NebulaInputActions.MapToggle)) ToggleFromMapKey();

            if (!IsOpen) return;

            ApplyUiScale();

            // Escape walks back exactly one level, and closes the map at the top.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (!string.IsNullOrEmpty(selectedPoiId))
                {
                    selectedPoiId = null;
                    card.Hide();
                    PushSelectionToMap();
                    RefreshCommandDeck();
                    return;
                }
                // Backing out of an unidentified star's card is a step in its own right:
                // it drops the selection without also leaving the galaxy the player is in.
                if (!string.IsNullOrEmpty(undiscoveredSelectionId))
                {
                    undiscoveredSelectionId = null;
                    card.Hide();
                    PushSelectionToMap();
                    RefreshCommandDeck();
                    return;
                }
                if (!Presenter.StepBack()) { SetOpen(false); return; }
                SyncCameraToLevel();
                PushSelectionToMap();
                RefreshCommandDeck();
            }

            HandleCommandKeys();
            HandleMapInput();
            HandleMapHover();
            // EnsureTarget re-allocates the render texture when the size changes, and a
            // RawImage left pointing at a destroyed texture samples flat grey. Re-syncing
            // here heals any external resize (tests force real resolutions this way).
            if (universeRenderer != null && universeSurface != null)
            {
                universeRenderer.EnsureTarget(Screen.width, Screen.height);
                if (universeSurface.texture != universeRenderer.Target)
                    universeSurface.texture = universeRenderer.Target;
            }
            if (hud != null) hud.Tick(Presenter, universeRenderer);
            PushShipPosition();
            if (Time.unscaledTime >= nextRefreshTime) Refresh();
            RenderStage();
        }

        /// <summary>
        /// Exactly what pressing the map key does, as its own callable step so the whole
        /// decision is testable without needing to synthesise a real key event: open when
        /// closed (unless another modal already owns the screen), close when open.
        /// Returns true when the map's open state actually changed.
        /// </summary>
        public bool ToggleFromMapKey()
        {
            if (IsOpen) { SetOpen(false); return true; }
            if (ExclusiveModal.IsHeldByOther(this))
            {
                // Refused, and the player is told. A refusal with no answer is the
                // defect: the map simply did not appear and nothing on screen said
                // why, so the only reading available was "the control is broken".
                NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Navigation,
                    ModalBlockedText, GameplayFeedbackTone.Rejection);
                return false;
            }
            SetOpen(true);
            return true;
        }

        /// <summary>Why the map would not open. Player-facing, and the one wording for it.</summary>
        public const string ModalBlockedText = "BAŞKA BİR EKRAN AÇIK — STRATEJİK HARİTA AÇILAMADI";

        /// <summary>
        /// Centres the map on what the player actually knows when it opens, so a campaign
        /// with one known system shows that system in the middle rather than an empty field
        /// with a marker somewhere off to the side.
        /// </summary>
        private void FrameKnownSystems()
        {
            if (Snapshot == null || Snapshot.Systems.Count == 0) return;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            int located = 0;
            foreach (StrategicSystemView system in Snapshot.Systems)
            {
                if (!system.PositionKnown) continue;
                Vector2 p = StrategicMapCamera.WorldToMap(system.AbsoluteCenter);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
                located++;
            }
            if (located == 0) return;

            graphView.Camera.FocusOn((min + max) * 0.5f, graphView.Camera.Zoom, durationSeconds: 0f);
        }

        /// <summary>Enter plots a waypoint to the selected system through the real navigation authority. No teleport, no jump.</summary>
        private void HandleCommandKeys()
        {
            if (Input.GetKeyDown(KeyCode.Home))
                RequestOverview();

            if (Input.GetKeyDown(KeyCode.F)) RequestFocusCurrent();

            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
                RequestClearRoute();

            if (Input.GetKeyDown(KeyCode.A))
                RequestAnalysis();

            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
            RequestRoute();
        }

        private void RequestRoute()
        {
            if (!TrySelectedSystem(out StrategicSystemView system))
            {
                ReportCommand(StrategicCommandResult.NoTarget, null);
                return;
            }

            bool poiSelected = TrySelectedPoi(out StrategicPoiView poi, out _);
            StrategicCommandResult result = poiSelected
                ? commands.SetWaypointToPoiSystem(system, poi)
                : commands.SetWaypoint(system);
            SyncNavigationState();
            ReportCommand(result, poiSelected
                ? $"{poi.DisplayName} için sistem rotası belirlendi: {system.DisplayName}"
                : $"Rota belirlendi: {system.DisplayName}");
            Refresh();
        }

        private void RequestClearRoute()
        {
            StrategicCommandResult cleared = commands.ClearWaypoint();
            SyncNavigationState();
            ReportCommand(cleared, "Rota temizlendi");
            RefreshCommandDeck();
        }

        private void RequestAnalysis()
        {
            StrategicCommandResult result;
            string accepted;
            if (commands.HasActiveAnalysis)
            {
                result = commands.CancelAnalysis();
                accepted = "Sinyal analizi iptal edildi";
            }
            else if (!string.IsNullOrEmpty(selectedPoiId))
            {
                hud?.SetStatus("POI işlemleri yalnız oyun dünyasında, hedefe yaklaşınca yapılır", false);
                commandDeck?.ShowStatus("POI işlemleri yalnız oyun dünyasında yapılır", false);
                return;
            }
            else if (TrySelectedSystem(out StrategicSystemView signal))
            {
                result = commands.StartAnalysis(signal);
                accepted = "Sinyal analizi başladı";
            }
            else
            {
                result = StrategicCommandResult.NoTarget;
                accepted = null;
            }
            ReportCommand(result, accepted);
            Refresh();
        }

        private void RequestOverview()
        {
            Presenter.Reset();
            undiscoveredSelectionId = null;
            selectedPoiId = null;
            card.Hide();
            universeRenderer?.ResetToOverview(universeRenderer.UniverseCenter);
            PushSelectionToMap();
            hud?.SetStatus("Evren görünümüne dönüldü");
            commandDeck?.ShowStatus("Evren görünümüne dönüldü", true);
            RefreshCommandDeck();
        }

        private void RequestFocusCurrent()
        {
            selectedPoiId = null;
            OpenAtCurrentSystem();
            hud?.SetStatus("Gemi konumuna odaklanıldı");
            commandDeck?.ShowStatus("Gemi konumuna odaklanıldı", true);
            RefreshCommandDeck();
        }

        private void OnLayerChanged(StrategicMapDisplayLayer layer, bool visible)
        {
            universeRenderer?.SetLayerVisible(layer, visible);
        }

        private bool TrySelectedSystem(out StrategicSystemView system)
        {
            system = default;
            return Snapshot != null && !string.IsNullOrEmpty(Presenter.SelectedSystemId)
                   && Snapshot.TryGetSystem(Presenter.SelectedSystemId, out system);
        }

        private bool TrySelectedPoi(out StrategicPoiView poi, out StrategicSystemView system)
        {
            poi = default;
            system = default;
            if (string.IsNullOrEmpty(selectedPoiId) || !TrySelectedSystem(out system)) return false;
            for (int i = 0; i < system.Pois.Count; i++)
            {
                if (system.Pois[i].PoiId != selectedPoiId) continue;
                poi = system.Pois[i];
                return true;
            }
            return false;
        }

        private void ReportCommand(StrategicCommandResult result, string acceptedMessage)
        {
            string message = result == StrategicCommandResult.Accepted
                ? acceptedMessage
                : (commands.LastRejectReason ?? StrategicMapCommands.Describe(result));
            if (string.IsNullOrEmpty(message)) return;
            if (commandHintText != null)
            {
                commandHintText.text = message;
                commandHintText.color = result == StrategicCommandResult.Accepted
                    ? StrategicMapTheme.Green : StrategicMapTheme.Gold;
            }
            hud?.SetStatus(message, result == StrategicCommandResult.Accepted);
            commandDeck?.ShowStatus(message, result == StrategicCommandResult.Accepted);
        }

        /// <summary>
        /// Draws whichever level the player is on.
        ///
        /// The flat node graph and the system stage are switched off for good while the 3D
        /// map is the map -- see HideLegacyChrome. Re-activating them here is what put
        /// labelled circles and link lines back over the galaxies on every refresh, however
        /// firmly they had been hidden at build time.
        /// </summary>
        private void RenderStage()
        {
            bool universe = Presenter.Level == StrategicMapLevel.Universe;
            if (universe) { card.Hide(); return; }

            // An unidentified star's card is driven by the click that selected it, not by the
            // presenter — the snapshot deliberately carries nothing about it to render from.
            if (!string.IsNullOrEmpty(undiscoveredSelectionId)) return;

            if (!string.IsNullOrEmpty(selectedPoiId))
            {
                if (TrySelectedPoi(out StrategicPoiView poi, out StrategicSystemView poiSystem))
                {
                    card.ShowPoi(poi, poiSystem, PoiLiveState.Find(poi.PoiId));
                    return;
                }

                // Knowledge can legitimately change while the map is open. A POI that is
                // no longer in the freshly gated snapshot must not leave a dangling card.
                selectedPoiId = null;
                PushSelectionToMap();
            }

            if (Snapshot == null || !Snapshot.TryGetSystem(Presenter.SelectedSystemId, out StrategicSystemView system))
            {
                // The gameplay snapshot has no row for it. If the map is drawing the system,
                // the card is built from the map's own universe instead -- which is what makes
                // every system's worlds clickable rather than only the one the player has
                // actually been to.
                if (RenderStageFromMap()) return;

                // Standing in a galaxy with nothing selected: a card left over from the system
                // the player just backed out of would be describing somewhere they have left.
                if (Presenter.Level == StrategicMapLevel.Galaxy) card.Hide();
                return;
            }

            if (Presenter.Level == StrategicMapLevel.Planet && !string.IsNullOrEmpty(Presenter.SelectedPlanetId))
            {
                for (int i = 0; i < system.Planets.Count; i++)
                {
                    if (system.Planets[i].PlanetId != Presenter.SelectedPlanetId) continue;
                    // The world's real year, straight off the geometry the map is turning it
                    // with, so the card and the motion on screen can never disagree.
                    float period = 0f;
                    if (universeRenderer != null)
                        universeRenderer.TryOrbitPeriodSeconds(Presenter.SelectedPlanetId, out period);
                    card.ShowPlanet(system.Planets[i], system, period);
                    return;
                }
                // The system is in the snapshot but its planets are not: the gameplay source
                // only fills those in once a system has been VISITED. The map knows them, so
                // the card comes from there rather than not opening at all.
                if (RenderStageFromMap()) return;
                card.Hide();
            }
            else card.ShowSystem(system);
        }

        /// <summary>
        /// Draws the card for a system the 3D map is showing but the gameplay snapshot has no
        /// row for — every system outside the handful the player has actually visited.
        ///
        /// The same card, the same layout: only the source of the facts differs, and it is
        /// the very universe already on screen. Returns false when the map does not carry the
        /// selection either, which is the caller's cue to fall back to hiding the card.
        /// </summary>
        private bool RenderStageFromMap()
        {
            if (universeRenderer == null || universeRenderer.Universe == null) return false;
            Preview.StrategicMapUniverseSnapshot universe = universeRenderer.Universe;

            if (Presenter.Level == StrategicMapLevel.Planet && !string.IsNullOrEmpty(Presenter.SelectedPlanetId))
            {
                if (!StrategicMapUniverseViews.TryPlanet(universe, Presenter.SelectedPlanetId,
                        out StrategicPlanetView planet, out StrategicSystemView planetSystem))
                    return false;

                float period = 0f;
                universeRenderer.TryOrbitPeriodSeconds(Presenter.SelectedPlanetId, out period);
                card.ShowPlanet(planet, planetSystem, period);
                return true;
            }

            if (!StrategicMapUniverseViews.TrySystem(universe, Presenter.SelectedSystemId,
                    out StrategicSystemView system))
                return false;

            card.ShowSystem(system);
            return true;
        }

        /// <summary>
        /// Tracks what the cursor is over so the map can ring it. Hover is resolved by the
        /// same picker the click uses, so what lights up under the cursor is always exactly
        /// what a click would take.
        /// </summary>
        private void HandleMapHover()
        {
            if (universeRenderer == null) return;
            Vector2 mouse = Input.mousePosition;
            Preview.StrategicMapPick pick = dragging || PointerOverChrome(mouse)
                ? Preview.StrategicMapPick.None      // nothing is hovered mid-drag or behind UI
                : PickAtScreenPoint(mouse);

            if (pick.Id != hoveredId)
            {
                hoveredId = pick.Id;
                PushSelectionToMap();
            }

            // The label is repositioned every frame even when the target has not changed:
            // the camera keeps moving and a planet keeps orbiting under it.
            if (hud == null) return;
            if (string.IsNullOrEmpty(hoveredId) ||
                !universeRenderer.TryScreenPosition(hoveredId, out Vector2 mapPixel) ||
                !TryMapPixelToScreenPoint(mapPixel, out Vector2 at))
            {
                hud.SetHoverLabel(null, Vector2.zero);
                return;
            }
            string label = universeRenderer.NameOf(hoveredId);
            if (pick.Kind == Preview.StrategicMapPickKind.System)
                label += "  ·  SİSTEMİ AÇ";
            hud.SetHoverLabel(label, at);
        }

        /// <summary>Hands the current selection and hover to the 3D map.</summary>
        private void PushSelectionToMap()
        {
            if (universeRenderer == null) return;
            string selected = Presenter.Level switch
            {
                StrategicMapLevel.Planet => Presenter.SelectedPlanetId,
                StrategicMapLevel.System => Presenter.SelectedSystemId,
                StrategicMapLevel.Galaxy => Presenter.SelectedGalaxyId,
                _ => null
            };
            // An unidentified star lives outside the presenter's hierarchy, so it carries the
            // reticle itself rather than through a level that would be revalidated away.
            if (!string.IsNullOrEmpty(undiscoveredSelectionId)) selected = undiscoveredSelectionId;
            if (!string.IsNullOrEmpty(selectedPoiId)) selected = selectedPoiId;
            universeRenderer.SetSelection(selected, hoveredId);
        }

        /// <summary>Fills the detail card for a star whose contents the player has not earned yet.</summary>
        private void ShowUndiscoveredCard(string systemId)
        {
            if (card == null || universeRenderer == null) return;
            universeRenderer.TryDescribeFromShip(systemId, out float distance, out string bearing);
            card.ShowUndiscoveredSystem(distance, bearing);
        }

        /// <summary>How far the cursor may move between press and release and still count as a click.</summary>
        private const float ClickSlopPixels = 10f;

        /// <summary>
        /// A click on the map surface walks one level down the hierarchy, on whatever the
        /// cursor is actually over: a galaxy at the overview, a system inside a galaxy, a
        /// planet inside a system. Clicking empty space does nothing rather than stepping
        /// back, so a missed click never loses the player's place.
        /// </summary>
        internal bool HandleMapClick(Vector2 screenPoint)
        {
            if (universeRenderer == null || PointerOverChrome(screenPoint)) return false;

            return HandleMapPick(PickAtScreenPoint(screenPoint));
        }

        /// <summary>Applies one already-resolved pick. Kept separate so a moving planet can
        /// retain the target captured on mouse-down until mouse-up.</summary>
        private bool HandleMapPick(Preview.StrategicMapPick pick)
        {
            if (universeRenderer == null) return false;

            switch (pick.Kind)
            {
                case Preview.StrategicMapPickKind.Galaxy:
                    undiscoveredSelectionId = null;
                    selectedPoiId = null;
                    Presenter.EnterGalaxy(pick.Id);
                    universeRenderer.FocusGalaxy(pick.Id);
                    break;
                case Preview.StrategicMapPickKind.System:
                    // A star drawn as a system target always opens its system view. Knowledge
                    // still gates the contents: Signal/Located rows open a neutral unresolved
                    // centre with no invented planets, while Visited rows expose real detail.
                    // Requiring the gameplay card snapshot here made a valid rendered target
                    // silently reject clicks whenever the two read models refreshed apart.
                    undiscoveredSelectionId = null;
                    selectedPoiId = null;
                    Presenter.EnterSystem(pick.Id, pick.GalaxyId);
                    universeRenderer.FocusSystem(pick.Id);
                    break;
                case Preview.StrategicMapPickKind.Planet:
                    undiscoveredSelectionId = null;
                    selectedPoiId = null;
                    Presenter.SelectPlanet(pick.Id);
                    break;
                case Preview.StrategicMapPickKind.Star:
                    // The star of the system the player is standing in: its own card, and the
                    undiscoveredSelectionId = null;
                    selectedPoiId = null;
                    Presenter.SelectStar();
                    break;
                case Preview.StrategicMapPickKind.Poi:
                    undiscoveredSelectionId = null;
                    // A POI is a detail selection inside the system, not a new camera level.
                    // Drop a planet card if one was open, then keep the system in place.
                    Presenter.SelectStar();
                    selectedPoiId = pick.Id;
                    break;
                default:
                    return false;
            }

            PushSelectionToMap();
            RenderText();
            RefreshCommandDeck();
            return true;
        }

        private void OnPlanetClicked(string planetId)
        {
            selectedPoiId = null;
            Presenter.SelectPlanet(planetId);
            RenderText();
        }

        /// <summary>
        /// Puts the 3D map where the presenter now says the player is. Called after any
        /// change that did not itself move the camera -- stepping back, most of all, which
        /// otherwise leaves the view inside a system the breadcrumb has already left.
        /// </summary>
        private void SyncCameraToLevel()
        {
            if (universeRenderer == null) return;
            switch (Presenter.Level)
            {
                case StrategicMapLevel.Galaxy:
                    universeRenderer.FocusGalaxy(Presenter.SelectedGalaxyId);
                    break;
                case StrategicMapLevel.System:
                case StrategicMapLevel.Planet:
                    universeRenderer.FocusSystem(Presenter.SelectedSystemId);
                    break;
                default:
                    universeRenderer.ResetToOverview(
                        universeRenderer.ContentBounds.center);
                    break;
            }
        }

        /// <summary>
        /// Drag turns the camera around the map, wheel zooms, release lets it coast.
        ///
        /// This drives the 3D camera. It used to drive the flat node graph, and once that was
        /// switched off the input went nowhere at all -- the map looked three-dimensional and
        /// answered nothing. Dragging is an orbit, not a pan: the map is a place seen from a
        /// camera, and what the player wants from a drag is to see the other side of it.
        /// </summary>
        private void HandleMapInput()
        {
            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(0))
            {
                pressedPick = Preview.StrategicMapPick.None;
                if (PointerOverChrome(mouse)) return;
                dragging = true;
                if (universeRenderer != null) universeRenderer.Interacting = true;
                lastDragPosition = mouse;
                dragPressPosition = mouse;
                dragVelocity = Vector2.zero;
                pressedPick = PickAtScreenPoint(mouse);
            }
            else if (Input.GetMouseButtonUp(0) && dragging)
            {
                dragging = false;
                bool isClick = (mouse - dragPressPosition).sqrMagnitude
                               <= ClickSlopPixels * ClickSlopPixels;
                if (universeRenderer != null)
                {
                    universeRenderer.Interacting = false;
                    // Tiny hand jitter is a click, not a camera fling.
                    universeRenderer.ReleaseDrag(isClick ? Vector2.zero : dragVelocity);
                }
                // A press and release in the same place is a click, not a very short drag.
                // Without this threshold every attempt to turn the camera would also select
                // whatever happened to be under the cursor when the button came up.
                if (isClick && !PointerOverChrome(mouse))
                {
                    // Prefer the object pressed. Planets move while the button is held, and
                    // requiring them to remain under the cursor on release made a normal
                    // click intermittently miss. If the press began on empty space, allow a
                    // target that moved under the release point instead.
                    Preview.StrategicMapPick pick = pressedPick.Kind != Preview.StrategicMapPickKind.None
                        ? pressedPick
                        : PickAtScreenPoint(mouse);
                    HandleMapPick(pick);
                }
                pressedPick = Preview.StrategicMapPick.None;
            }
            else if (dragging && Input.GetMouseButton(0))
            {
                Vector2 delta = mouse - lastDragPosition;
                lastDragPosition = mouse;
                if (Time.unscaledDeltaTime > 0.0001f) dragVelocity = delta / Time.unscaledDeltaTime;
                if (universeRenderer != null) universeRenderer.Drag(delta);
            }

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.0001f && universeRenderer != null
                && !PointerOverChrome(mouse))
            {
                // The reference works in browser wheel deltas: about +100 per notch scrolling
                // down, which pushes the camera out. Unity reports +1 per notch scrolling up,
                // so the sign flips and the scale comes back.
                universeRenderer.Zoom(-wheel * 100f, Presenter.Level >= StrategicMapLevel.System);
            }
        }

        private bool PointerOverChrome(Vector2 screenPoint) =>
            (card != null && card.ContainsScreenPoint(screenPoint))
            || (commandDeck != null && commandDeck.ContainsScreenPoint(screenPoint));

        /// <summary>
        /// Converts the pointer from the UI surface into the map camera's render-target
        /// pixels. Screen and target currently match in production, but tests, resolution
        /// changes and future safe-area layouts can legitimately make them differ.
        /// </summary>
        private Preview.StrategicMapPick PickAtScreenPoint(Vector2 screenPoint)
        {
            if (universeRenderer == null || !TryScreenPointToMapPixel(screenPoint, out Vector2 mapPixel))
                return Preview.StrategicMapPick.None;
            return universeRenderer.Pick(mapPixel, Presenter.Level);
        }

        internal bool TryScreenPointToMapPixel(Vector2 screenPoint, out Vector2 mapPixel)
        {
            mapPixel = default;
            if (universeSurface == null || universeRenderer == null || universeRenderer.Target == null)
                return false;

            RectTransform rect = universeSurface.rectTransform;
            Camera uiCamera = universeSurface.canvas != null ? universeSurface.canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, screenPoint, uiCamera, out Vector2 local))
                return false;

            Rect bounds = rect.rect;
            if (!bounds.Contains(local) || bounds.width <= 0f || bounds.height <= 0f) return false;

            float u = Mathf.InverseLerp(bounds.xMin, bounds.xMax, local.x);
            float v = Mathf.InverseLerp(bounds.yMin, bounds.yMax, local.y);
            Rect uv = universeSurface.uvRect;
            u = Mathf.Lerp(uv.xMin, uv.xMax, u);
            v = Mathf.Lerp(uv.yMin, uv.yMax, v);
            mapPixel = new Vector2(u * universeRenderer.Target.width,
                v * universeRenderer.Target.height);
            return true;
        }

        private bool TryMapPixelToScreenPoint(Vector2 mapPixel, out Vector2 screenPoint)
        {
            screenPoint = default;
            if (universeSurface == null || universeRenderer == null || universeRenderer.Target == null)
                return false;

            Rect uv = universeSurface.uvRect;
            if (Mathf.Abs(uv.width) < 0.0001f || Mathf.Abs(uv.height) < 0.0001f) return false;
            float u = Mathf.InverseLerp(uv.xMin, uv.xMax, mapPixel.x / universeRenderer.Target.width);
            float v = Mathf.InverseLerp(uv.yMin, uv.yMax, mapPixel.y / universeRenderer.Target.height);
            Rect bounds = universeSurface.rectTransform.rect;
            Vector3 world = universeSurface.rectTransform.TransformPoint(new Vector3(
                Mathf.Lerp(bounds.xMin, bounds.xMax, u),
                Mathf.Lerp(bounds.yMin, bounds.yMax, v), 0f));
            Camera uiCamera = universeSurface.canvas != null ? universeSurface.canvas.worldCamera : null;
            screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, world);
            return true;
        }

        private void OnSystemClicked(string systemId)
        {
            selectedPoiId = null;
            Presenter.SelectSystem(systemId);
            graphView.SetSelected(systemId);
            RenderText();
        }

        private void OnSystemActivated(string systemId)
        {
            selectedPoiId = null;
            Presenter.EnterSystem(systemId);
            graphView.SetSelected(systemId);
            if (Snapshot != null && Snapshot.TryGetSystem(systemId, out StrategicSystemView system)) graphView.FocusSystem(system);
            RenderText();
        }

        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;

            if (open)
            {
                // Acquire before mutating any visible/input state. Public callers are allowed
                // to race another modal; a rejected acquire must leave the map fully closed.
                if (!ExclusiveModal.TryAcquire(this)) return;
                savedCursorLock = Cursor.lockState;
                savedCursorVisible = Cursor.visible;
                savedInputGateSuppressed = InputGate.Suppressed;
                try
                {
                    IsOpen = true;
                    root.SetActive(true);
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    // Ship/world/combat input is gated; the simulation itself keeps running.
                    InputGate.Suppressed = true;

                    if (universeRenderer != null)
                    {
                        universeRenderer.Interacting = false;
                        universeRenderer.SetActiveRendering(true);
                        universeRenderer.EnsureTarget(Screen.width, Screen.height);
                        universeSurface.texture = universeRenderer.Target;
                    }

                    Presenter.Reset();
                    undiscoveredSelectionId = null;
                    selectedPoiId = null;
                    graphView.SetSelected(null);
                    graphView.Camera.Reset();
                    hud?.OnMapOpened();
                    // Opening the map is the natural moment to pull the world's current truth.
                    Refresh();
                    if (universeRenderer != null) universeRenderer.ResetToOverview(universeRenderer.UniverseCenter);
                    FrameKnownSystems();
                    OpenAtCurrentSystem();
                    SyncNavigationState();
                }
                catch
                {
                    ForceCloseState();
                    throw;
                }
            }
            else
            {
                ForceCloseState();
            }
        }

        private void ForceCloseState()
        {
            dragging = false;
            pressedPick = Preview.StrategicMapPick.None;
            selectedPoiId = null;
            if (universeRenderer != null)
            {
                universeRenderer.Interacting = false;
                universeRenderer.SetActiveRendering(false);
            }
            if (card != null) card.Hide();
            if (bodyPortrait != null) bodyPortrait.Clear();
            if (root != null) root.SetActive(false);
            Cursor.lockState = savedCursorLock;
            Cursor.visible = savedCursorVisible;
            InputGate.Suppressed = savedInputGateSuppressed;
            ExclusiveModal.Release(this);
            IsOpen = false;
        }

        /// <summary>
        /// Opens the map on the system the player is actually in.
        ///
        /// DEC-172 is explicit that the system view is the map's starting level and the
        /// galaxy view is what opens up once distant signals exist to compare. Starting at
        /// the universe overview instead put the player in front of four dust clouds with —
        /// the knowledge gate correctly hiding every undiscovered system — nothing to click
        /// and no star of their own in sight. Their own system has a star, orbits and
        /// planets, and every one of them is a legitimate target.
        ///
        /// A system whose contents are still gated is never opened into: the map would have
        /// to show an empty stage. In that case the overview stands, which is honest.
        /// </summary>
        private void OpenAtCurrentSystem()
        {
            if (Snapshot == null || universeRenderer == null) return;

            string currentId = Snapshot.CurrentSystemId;
            if (string.IsNullOrEmpty(currentId)) return;
            if (!Snapshot.TryGetSystem(currentId, out StrategicSystemView system)) return;
            if (!system.DetailsKnown) return;

            Presenter.EnterSystem(currentId, universeRenderer.GalaxyOfSystem(currentId));
            universeRenderer.FocusSystem(currentId);
            // No fly-in from the universe centre: the map must be legible the frame it opens.
            universeRenderer.SnapCamera();
            PushSelectionToMap();
            RenderText();
        }

        /// <summary>Rebuilds the read-only snapshot and re-validates the selection against it.</summary>
        public void Refresh()
        {
            nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
            Snapshot = dataSource.Build();
            if (IsOpen) hud?.PresentReconnectSummaries(Snapshot.ReconnectSummaries);

            StrategicMapLevel levelBeforeValidation = Presenter.Level;
            string galaxyBeforeValidation = Presenter.SelectedGalaxyId;
            string systemBeforeValidation = Presenter.SelectedSystemId;
            string planetBeforeValidation = Presenter.SelectedPlanetId;
            Presenter.Revalidate(Snapshot, IsDrawnByMap);

            bool selectionChanged = levelBeforeValidation != Presenter.Level
                                    || galaxyBeforeValidation != Presenter.SelectedGalaxyId
                                    || systemBeforeValidation != Presenter.SelectedSystemId
                                    || planetBeforeValidation != Presenter.SelectedPlanetId;
            if (IsOpen && selectionChanged)
            {
                // Knowledge/streaming can invalidate a selection while the map is open. The
                // renderer must fall back with the presenter; otherwise old planets remain
                // visible while the picker is already resolving universe/galaxy targets.
                selectedPoiId = null;
                undiscoveredSelectionId = null;
                card?.Hide();
                SyncCameraToLevel();
                PushSelectionToMap();
            }

            // The 3D geometry follows the gameplay snapshot: when what the player knows
            // changes (a system located, a planet scanned) while the map is open, the
            // universe is rebuilt on this same half-second cadence — never per frame.
            int signature = UniverseSignatureOf(Snapshot);
            if (IsOpen && signature != universeSignature) RefreshUniverseGeometry();

            SyncNavigationState();
            RefreshCommandDeck();
            RenderText();
        }

        /// <summary>
        /// Mirrors the one real navigation authority into the visual route. This is called on
        /// open, refresh and after commands, so a save-restored route and a newly cleared route
        /// can never diverge from the line on screen.
        /// </summary>
        private void SyncNavigationState()
        {
            if (universeRenderer == null || context == null) return;
            try
            {
                Nebula.World.NavigationDirector navigation = context.NavigationDirector;
                universeRenderer.SetWaypoint(navigation?.SelectedRouteSystemId);
                if (navigation?.Analysis != null && navigation.Analysis.Active)
                    hud?.SetStatus($"Sinyal analizi · {navigation.Analysis.RemainingSeconds:0} sn", true);
            }
            catch (System.InvalidOperationException)
            {
                universeRenderer.SetWaypoint(null);
            }
        }

        private void RefreshCommandDeck()
        {
            if (commandDeck == null) return;

            bool hasSelection = TrySelectedSystem(out StrategicSystemView selected);
            bool poiSelection = TrySelectedPoi(out StrategicPoiView selectedPoi, out _);
            string title = hasSelection ? selected.DisplayName : null;
            string meta = hasSelection
                ? $"{KnowledgeLabel(selected.Knowledge)} · {LinkLabel(selected.Link)}"
                : null;

            if (hasSelection && selected.HasRaidWarning) meta += " · BASKIN UYARISI";
            else if (hasSelection && selected.HasAttention) meta += " · İLGİ GEREKİYOR";

            if (poiSelection)
            {
                title = selectedPoi.DisplayName;
                meta = "KEŞİF NOKTASI · " + StrategicObjectCard.PoiKnowledgeName(selectedPoi.KnowledgeLevel).ToUpperInvariant();
                if (selectedPoi.TypeKnown) meta += " · " + StrategicObjectCard.PoiTypeName(selectedPoi.Type).ToUpperInvariant();
                if (selectedPoi.RiskKnown) meta += " · " + StrategicObjectCard.PoiRiskName(selectedPoi.Risk).ToUpperInvariant();
            }
            else if (hasSelection && !string.IsNullOrEmpty(Presenter.SelectedPlanetId))
                for (int i = 0; i < selected.Planets.Count; i++)
                    if (selected.Planets[i].PlanetId == Presenter.SelectedPlanetId)
                    {
                        title = selected.Planets[i].DisplayName;
                        meta = selected.Planets[i].Scanned ? "GEZEGEN · TARANDI" : "GEZEGEN · TARANMADI";
                        break;
                    }

            string route = "ROTA · YOK";
            string analysis = "ANALİZ · BEKLEMEDE";
            bool analysisActive = false;
            try
            {
                Nebula.World.NavigationDirector navigation = context?.NavigationDirector;
                if (navigation != null && !string.IsNullOrEmpty(navigation.SelectedRouteSystemId))
                {
                    string routeName = Snapshot?.Player.WaypointName ?? navigation.SelectedRouteSystemId;
                    route = $"ROTA · {routeName.ToUpperInvariant()}";
                }
                if (navigation?.Analysis != null && navigation.Analysis.Active)
                {
                    analysisActive = true;
                    analysis = $"ANALİZ · {navigation.Analysis.RemainingSeconds:0} SN";
                }
            }
            catch (System.InvalidOperationException) { }

            if (poiSelection && !analysisActive) analysis = "POI · YEREL ETKİLEŞİM";

            commandDeck.Render(new StrategicMapDeckState(
                title, meta, route, analysis,
                canRoute: hasSelection && selected.PositionKnown
                    && (!poiSelection || selectedPoi.PositionKnown),
                hasRoute: commands.HasWaypoint,
                canAnalyze: !poiSelection && hasSelection
                    && selected.Knowledge == Navigation.SystemKnowledgeLevel.Signal,
                analysisActive: analysisActive,
                routeActionLabel: poiSelection ? "SİSTEM ROTASI  [ENTER]" : null));
        }

        private static string KnowledgeLabel(Navigation.SystemKnowledgeLevel level) => level switch
        {
            Navigation.SystemKnowledgeLevel.Signal => "SİNYAL",
            Navigation.SystemKnowledgeLevel.Located => "KONUMLANDIRILDI",
            Navigation.SystemKnowledgeLevel.Visited => "ZİYARET EDİLDİ",
            _ => "BİLİNMİYOR"
        };

        private static string LinkLabel(Extraction.SystemLinkState link) => link switch
        {
            Extraction.SystemLinkState.Live => "CANLI BAĞLANTI",
            Extraction.SystemLinkState.LastKnown => "SON BİLİNEN",
            _ => "BAĞLANTI YOK"
        };

        /// <summary>
        /// Moves the ship marker to where the ship actually is, every frame.
        ///
        /// Read straight from the navigation authority rather than off the universe snapshot:
        /// that snapshot is only rebuilt when the player's KNOWLEDGE changes, so a marker fed
        /// from it would sit still while the ship flew across the sector. The map itself never
        /// writes anything back — this is a read, like every other thing the map does.
        /// </summary>
        private void PushShipPosition()
        {
            if (universeRenderer == null || context == null) return;
            try
            {
                Nebula.World.NavigationDirector navigation = context.NavigationDirector;
                if (navigation == null) return;
                universeRenderer.SetShipPosition(
                    navigation.ShipAbsolutePosition() / Preview.StrategicMapOrbits.MapUnit);
            }
            catch (System.InvalidOperationException)
            {
                // The map can be opened before the world has finished installing; a missing
                // navigation authority means there is no ship position to show yet.
            }
        }

        /// <summary>True when the 3D map is genuinely drawing this system, whatever the gameplay snapshot carries.</summary>
        private bool IsDrawnByMap(string systemId)
        {
            if (universeRenderer == null || universeRenderer.Universe == null) return false;
            System.Collections.Generic.List<Preview.StrategicMapUniverseSnapshot.SystemRow> systems =
                universeRenderer.Universe.systems;
            for (int i = 0; i < systems.Count; i++)
                if (systems[i].id == systemId) return true;
            return false;
        }

        /// <summary>
        /// Pulls a freshly built live universe and hands it to the renderer and HUD.
        /// Kept as the single path for both the map-open moment and mid-session knowledge
        /// changes, so the two can never drift apart.
        /// </summary>
        private void RefreshUniverseGeometry()
        {
            if (universeRenderer == null || universeSource == null) return;
            Preview.StrategicMapUniverseSnapshot fresh = universeSource is StrategicMapLiveUniverseSource live
                ? live.Rebuild()
                : universeSource.Universe;
            universeRenderer.RefreshUniverse(fresh);
            if (hud != null) hud.SetUniverse(fresh);
            universeSignature = UniverseSignatureOf(Snapshot);
        }

        /// <summary>
        /// What the 3D geometry depends on, folded to one int: the set of known systems,
        /// each one's knowledge level, and which planets are scanned. Dominance, drills and
        /// link states deliberately excluded — they are step-2 state visuals, not geometry.
        /// </summary>
        private static int UniverseSignatureOf(StrategicMapSnapshot snapshot)
        {
            if (snapshot == null) return 0;
            int signature = 17;
            // Which system is physically Full decides whether its planet phases come from
            // live transforms or a frozen last-known snapshot, so it is geometry input too.
            signature = signature * 31 + (snapshot.CurrentSystemId?.GetHashCode() ?? 0);
            foreach (StrategicSystemView system in snapshot.Systems)
            {
                signature = signature * 31 + (system.SystemId?.GetHashCode() ?? 0);
                signature = signature * 31 + (int)system.Knowledge;
                foreach (StrategicPlanetView planet in system.Planets)
                    signature = signature * 31 + (planet.Scanned ? 1 : 0);
                foreach (StrategicPoiView poi in system.Pois)
                {
                    signature = signature * 31 + (poi.PoiId?.GetHashCode() ?? 0);
                    signature = signature * 31 + (int)poi.KnowledgeLevel;
                    signature = signature * 31 + (poi.PositionKnown ? 1 : 0);
                    signature = signature * 31 + (poi.TypeKnown ? (int)poi.Type + 1 : 0);
                }
            }
            return signature;
        }

        private void OnDestroy()
        {
            // The 3D renderer lives at the world origin rather than under this screen, so it
            // has to be torn down explicitly — otherwise a restart would leak one per session.
            if (universeRenderer != null) Destroy(universeRenderer.gameObject);

            // TF-002 safety net: a screen destroyed while still open must not leave the
            // modal owner or the input gate stuck.
            if (IsOpen) ForceCloseState();
        }

        private void OnDisable()
        {
            if (IsOpen) ForceCloseState();
        }

        // ------------------------------------------------------------------ render

        private void RenderText()
        {
            if (Snapshot == null) return;

            var crumbs = Presenter.Breadcrumb(Snapshot);
            breadcrumbText.text = string.Join("  ›  ", crumbs);

            resourcePanel.Render(Snapshot.Player);

            switch (Presenter.Level)
            {
                case StrategicMapLevel.Universe:
                    // A brand-new campaign genuinely knows one system, so say what to do about it
                    // rather than leaving the player staring at a single unexplained marker.
                    bodyText.text = Snapshot.Systems.Count <= 1
                        ? "Bilinen sistem: 1  ·  Tara ve sinyal topla, keşfettikçe harita genişler  ·  Sisteme çift tıkla"
                        : $"Bilinen sistem: {Snapshot.Systems.Count}  ·  Sisteme çift tıkla  ·  Enter ile rota";
                    break;
                case StrategicMapLevel.System:
                    bodyText.text = Snapshot.TryGetSystem(Presenter.SelectedSystemId, out var sys)
                        ? DescribeSystem(sys) : "";
                    break;
                default:
                    bodyText.text = "";
                    break;
            }
        }

        private static string DescribeSystem(StrategicSystemView system)
        {
            if (!system.DetailsKnown) return "Bu sistem henüz ziyaret edilmedi.";
            string stale = system.IsStale ? "  ·  ESKİ VERİ" : "";
            return $"{system.PlanetCount} gezegen{stale}";
        }

        // ------------------------------------------------------------------ ui build

        private void BuildUi()
        {
            root = new GameObject("Root");
            root.transform.SetParent(transform, false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220; // above the existing HUD/screens
            canvasScaler = root.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Shared 1080p design space keeps type and controls readable at 720p while the
            // right inspector and bottom rail still fit without overlap.
            canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();

            RectTransform rootRect = root.GetComponent<RectTransform>();

            var backdrop = CreateImage(rootRect, "Backdrop", StrategicMapTheme.Backdrop);
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            // The 3D universe renders into its own texture and is shown here, behind every
            // panel. The renderer object lives outside this canvas and is switched off with
            // the map, so a closed map costs nothing.
            var surfaceGo = new GameObject("Universe Surface");
            var surfaceRect = surfaceGo.AddComponent<RectTransform>();
            surfaceRect.SetParent(rootRect, false);
            Stretch(surfaceRect);
            universeSurface = surfaceGo.AddComponent<RawImage>();
            universeSurface.raycastTarget = false;
            // Created at the world origin, not under this screen: the screen itself sits at
            // the session's floating-origin position, which would drag the map's universe
            // out of its own camera's view. This screen owns its lifetime explicitly.
            hud = StrategicMapHud.Create(rootRect, universeSource.Universe);
            universeRenderer = Preview.StrategicMapUniverseRenderer.Create(universeSource);

            breadcrumbText = CreateText(rootRect, "Breadcrumb", "", StrategicMapTheme.HeadingSize,
                StrategicMapTheme.Cyan, TextAnchor.UpperLeft);
            Anchor(breadcrumbText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(420f, -44f), new Vector2(1400f, 34f));

            bodyText = CreateText(rootRect, "Body", "", StrategicMapTheme.BodySize,
                StrategicMapTheme.MutedText, TextAnchor.UpperLeft);
            Anchor(bodyText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(420f, -88f), new Vector2(1400f, 30f));

            graphView = StrategicMapGraphView.Create(rootRect);
            graphView.SystemClicked += OnSystemClicked;
            graphView.SystemActivated += OnSystemActivated;

            systemStage = StrategicSystemStageView.Create(rootRect);
            systemStage.PlanetClicked += OnPlanetClicked;

            card = StrategicObjectCard.Create(rootRect);
            // The live body portrait: its own stage, camera and lights on their own layer,
            // parented here so a floating-origin rebase carries stage and camera together.
            bodyPortrait = StrategicBodyPortrait.Create(transform);
            card.SetPortraitSource(bodyPortrait);

            commandHintText = CreateText(rootRect, "Command Hint", "", StrategicMapTheme.CaptionSize,
                StrategicMapTheme.Gold, TextAnchor.LowerLeft);
            Anchor(commandHintText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(420f, 34f), new Vector2(1200f, 26f));

            // The panel is created last so it draws above the graph.
            resourcePanel = StrategicMapResourcePanel.Create(rootRect);

            commandDeck = StrategicMapCommandDeck.Create(rootRect);
            commandDeck.FocusCurrentRequested += RequestFocusCurrent;
            commandDeck.OverviewRequested += RequestOverview;
            commandDeck.RouteRequested += RequestRoute;
            commandDeck.ClearRouteRequested += RequestClearRoute;
            commandDeck.AnalysisRequested += RequestAnalysis;
            commandDeck.LayerChanged += OnLayerChanged;

            HideLegacyChrome();
        }

        private void ApplyUiScale()
        {
            if (canvasScaler == null) return;
            float scale = Mathf.Clamp(Shell.NebulaUiShell.UiScale,
                Shell.NebulaUiShell.MinUiScale, Shell.NebulaUiShell.MaxUiScale);
            Vector2 wanted = Shell.NebulaUiShell.ReferenceResolution / Mathf.Max(0.01f, scale);
            if (canvasScaler.referenceResolution != wanted) canvasScaler.referenceResolution = wanted;
        }

        /// <summary>
        /// Switches off the flat node-graph map and the Turkish panels that came with it.
        ///
        /// The 3D map replaced them, but they were still being drawn over the top of it: the
        /// screenshot showed labelled blue circles and link lines from the old graph laid
        /// across the galaxies, with two breadcrumbs on screen at once.
        ///
        /// They are disabled rather than deleted. Their state is still what the presenter
        /// tests and the live-data path read, and the live-data adapter will need somewhere
        /// to land -- deleting them now would take out that seam along with the visuals.
        /// </summary>
        private void HideLegacyChrome()
        {
            if (graphView != null) graphView.gameObject.SetActive(false);
            if (systemStage != null) systemStage.gameObject.SetActive(false);
            if (resourcePanel != null) resourcePanel.gameObject.SetActive(false);
            if (breadcrumbText != null) breadcrumbText.enabled = false;
            if (bodyText != null) bodyText.enabled = false;
            if (commandHintText != null) commandHintText.enabled = false;
        }

        // ------------------------------------------------------------------ ui helpers

        internal static Image CreateImage(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        internal static Text CreateText(RectTransform parent, string name, string content, int size,
            Color color, TextAnchor alignment)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
