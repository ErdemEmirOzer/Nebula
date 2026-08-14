using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The strategic map's own 3D view, rendered by a dedicated camera into a
    /// <see cref="RenderTexture"/> that the map screen shows in a RawImage.
    ///
    /// Completely isolated from the game's own camera and world: it lives on its own
    /// layer, renders only its own objects, never writes to the depth of the played
    /// scene, and is switched off entirely while the map is closed so a shut map costs
    /// nothing.
    ///
    /// The camera reproduces the reference map's rig exactly — a 50° perspective orbit
    /// at yaw 35° / pitch 55° / distance 55 around the universe centre, with the same
    /// exponential smoothing and the same slow cinematic drift while idle.
    /// </summary>
    public sealed class StrategicMapUniverseRenderer : MonoBehaviour
    {
        // --- reference rig values, taken from the HTML, not guessed --------------------
        public const float FieldOfView = 50f;
        public const float InitialYawDegrees = 35f;
        public const float InitialPitchDegrees = 55f;
        public const float OverviewDistance = 55f;
        public const float MinPitchRadians = -Mathf.PI * 0.49f;
        public const float MaxPitchRadians = Mathf.PI * 0.49f;
        /// <summary>
        /// How close the wheel may bring the camera.
        ///
        /// Both floors were inherited from the reference and both stopped short of what the
        /// map is for. 1.3 at the galaxy level halted the zoom while a spiral still filled
        /// only part of the frame, so the arms could never be read; 0.76 inside a system left
        /// every world at a few dozen pixels, which is why they read as coloured dots rather
        /// than as places. They come down to where the camera can genuinely fly between the
        /// arms and sit beside a planet, still well clear of the 0.05 near plane.
        /// </summary>
        public const float MinDistanceUniverse = 0.35f;
        public const float MinDistanceSystem = 0.14f;
        public const float MaxDistance = 180f;
        /// <summary>Reference: yaw -= dx * 0.0055, pitch += dy * 0.0055.</summary>
        public const float DragSensitivity = 0.0055f;

        /// <summary>What fraction of the release speed survives one second of coasting.</summary>
        public const float DragDecayPerSecond = 0.02f;
        /// <summary>Reference: distance *= exp(deltaY * 0.001).</summary>
        public const float WheelZoomFactor = 0.001f;
        /// <summary>Reference smoothing: lerp(a, b, 1 - pow(0.002, dt)).</summary>
        private const float SmoothingBase = 0.002f;

        /// <summary>
        /// The system is composed inside the interactive viewport, not the full texture:
        /// the layer rail occupies the left edge, the detail card the right, and commands
        /// the bottom. A small left/up bias keeps outer worlds visible and clickable rather
        /// than placing them under those panels. Universe and galaxy views remain centred.
        /// </summary>
        private static readonly Vector2 SystemCompositionOffset = new Vector2(-0.09f, 0.08f);
        private const float SystemFocusDistance = 1.72f;

        /// <summary>A dedicated layer so the map camera and the game camera can never see each other's objects.</summary>
        public const int MapLayer = 30;

        private Camera mapCamera;
        private RenderTexture target;
        private Transform contentRoot;
        private StrategicMapGalaxyCloud galaxyCloud;
        private StrategicMapGalaxyCloud starfield;
        private StrategicMapGalaxyCloud haloLayer;
        private StrategicMapGalaxyCloud dustLayer;
        private StrategicMapNebulaField nebulaField;
        private StrategicMapBlackHoles blackHoles;
        private StrategicMapGrid grid;
        private StrategicMapSystemMarkers systemMarkers;
        private StrategicMapShipMarker shipMarker;
        private StrategicMapDecorStars decorStars;
        private StrategicMapOrbits orbits;
        private StrategicMapAsteroidBelt asteroidBelt;
        private StrategicMapDiscoveryMarkers discoveryMarkers;
        private StrategicMapOperationalMarkers operationalMarkers;
        private StrategicMapPlanetBodies planets;
        private StrategicMapPostProcess postProcess;
        private StrategicMapForegroundDust foregroundDust;
        private StrategicMapFlares flares;
        private StrategicMapSelectionOverlay selectionOverlay;
        private StrategicMapRoute route;
        private StrategicMapSystemStar systemStar;
        private StrategicMapUniverseSnapshot universe;
        private IStrategicMapOperationalSource operationalSource;
        private string selectedId;
        private string hoveredId;
        private string waypointId;
        private bool gridVisible = true;
        private bool routeVisible = true;
        private bool discoveriesVisible = true;
        private bool ambientVisible = true;
        private bool fleetVisible;
        private bool logisticsVisible;
        private bool threatVisible = true;
        private float nextOperationalRefreshTime;

        private const float OperationalRefreshIntervalSeconds = 0.5f;

        private Vector2 dragVelocity;
        private float yaw = InitialYawDegrees * Mathf.Deg2Rad;
        private float pitch = InitialPitchDegrees * Mathf.Deg2Rad;
        private float distance = OverviewDistance;
        private float desiredDistance = OverviewDistance;
        private Vector3 cameraTarget;
        private Vector3 desiredTarget;
        private float elapsed;
        private bool interacting;

        public RenderTexture Target => target;
        public Camera Camera => mapCamera;
        public float Distance => distance;
        public float YawDegrees => Mathf.Repeat(yaw * Mathf.Rad2Deg, 360f);
        public float PitchDegrees => pitch * Mathf.Rad2Deg;
        /// <summary>True while the player is dragging, which suspends the cinematic drift exactly as the reference does.</summary>
        public bool Interacting { get => interacting; set => interacting = value; }

        /// <summary>
        /// Deliberately created at the scene root, NOT parented to the map screen.
        ///
        /// The screen's own GameObject sits wherever the session put it — which, with a
        /// floating origin, is hundreds of thousands of units from zero. Parenting the map's
        /// 3D content under it dragged the whole universe out there with it, far outside the
        /// map camera's view. The map's own space is always anchored at the world origin;
        /// the screen owns its lifetime explicitly instead of by parenting.
        /// </summary>
        public static StrategicMapUniverseRenderer Create(IStrategicMapUniverseSource source)
        {
            var go = new GameObject("Strategic Map Universe Renderer");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderer = go.AddComponent<StrategicMapUniverseRenderer>();
            renderer.Build(source);
            go.SetActive(false); // a closed map renders nothing at all
            return renderer;
        }

        private void Build(IStrategicMapUniverseSource source)
        {
            StrategicMapUniverseSnapshot universe = source?.Universe ?? new StrategicMapUniverseSnapshot();
            operationalSource = source as IStrategicMapOperationalSource;
            cameraTarget = desiredTarget = universe.UniverseCenter;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(transform, false);
            contentRoot = contentGo.transform;

            var cameraGo = new GameObject("Map Camera");
            cameraGo.transform.SetParent(transform, false);
            mapCamera = cameraGo.AddComponent<Camera>();
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = StrategicMapTheme.Space;
            mapCamera.fieldOfView = FieldOfView;
            mapCamera.nearClipPlane = 0.05f;
            mapCamera.farClipPlane = 4000f;
            mapCamera.cullingMask = 1 << MapLayer;
            mapCamera.allowHDR = false;
            mapCamera.depth = -50; // renders before the game camera, into its own texture

            // Back to front: distant starfield, then the wide halo/bridge shell, then the
            // dust lanes that sit inside the arms, then the galaxy stars themselves.
            starfield = StrategicMapGalaxyCloud.CreateStarfield(contentRoot, universe.UniverseCenter);
            nebulaField = StrategicMapNebulaField.Create(contentRoot, universe);
            grid = StrategicMapGrid.Create(contentRoot, universe);
            haloLayer = StrategicMapGalaxyCloud.CreateSpaceLayer(contentRoot, universe, dustLanes: false);
            dustLayer = StrategicMapGalaxyCloud.CreateSpaceLayer(contentRoot, universe, dustLanes: true);
            galaxyCloud = StrategicMapGalaxyCloud.Create(contentRoot, universe);
            // Cores last: they draw over their own galaxy's stars, as the reference does.
            // The galaxies' own arm stars, always on: they are what a galaxy looks like.
            decorStars = StrategicMapDecorStars.Create(contentRoot, universe);
            systemMarkers = StrategicMapSystemMarkers.Create(contentRoot, universe);
            // Nothing is active until the player drills into a galaxy.
            systemMarkers.SetActiveGalaxy(null);
            orbits = StrategicMapOrbits.Create(contentRoot);
            asteroidBelt = StrategicMapAsteroidBelt.Create(contentRoot);
            discoveryMarkers = StrategicMapDiscoveryMarkers.Create(contentRoot, universe);
            operationalMarkers = StrategicMapOperationalMarkers.Create(contentRoot,
                ReadOperationalSnapshot(universe));
            planets = StrategicMapPlanetBodies.Create(contentRoot);
            flares = StrategicMapFlares.Create(contentRoot, universe);
            selectionOverlay = StrategicMapSelectionOverlay.Create(contentRoot, universe);
            route = StrategicMapRoute.Create(contentRoot, universe);
            systemStar = StrategicMapSystemStar.Create(contentRoot);
            // Where the player is standing. Created after the camera so it can size itself
            // against the real view, and never behind the foreground dust.
            shipMarker = StrategicMapShipMarker.Create(contentRoot, mapCamera, universe.ShipPosition);
            foregroundDust = StrategicMapForegroundDust.Create(contentRoot, universe);
            postProcess = StrategicMapPostProcess.Attach(mapCamera);
            blackHoles = StrategicMapBlackHoles.Create(contentRoot, universe);
            this.universe = universe;
            nextOperationalRefreshTime = Time.unscaledTime + OperationalRefreshIntervalSeconds;
            ApplyCamera();
        }

        private void OnDestroy() => ReleaseTarget();

        private void ReleaseTarget()
        {
            if (target == null) return;
            if (mapCamera != null) mapCamera.targetTexture = null;
            target.Release();
            Destroy(target);
            target = null;
        }

        /// <summary>Sizes the render texture to the map area. Re-allocates only when the size genuinely changes.</summary>
        public void EnsureTarget(int width, int height)
        {
            width = Mathf.Max(64, width);
            height = Mathf.Max(64, height);
            if (target != null && target.width == width && target.height == height) return;

            ReleaseTarget();
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "StrategicMapTarget",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            target.Create();
            if (mapCamera != null) mapCamera.targetTexture = target;
        }

        /// <summary>
        /// Renders one frame into the target immediately. The camera also renders on its own
        /// each frame, but a headless/batch context has no guaranteed frame loop — a capture
        /// or a test needs to force the draw and know it happened.
        /// </summary>
        public void RenderNow()
        {
            if (mapCamera == null || target == null) return;
            AnchorToOrigin();
            ApplyCamera();
            mapCamera.Render();
        }

        /// <summary>
        /// Keeps the map's universe pinned to the world origin.
        ///
        /// The game shifts every scene root when its floating origin rebases, which would
        /// otherwise drag this whole universe hundreds of thousands of units away from its
        /// own camera — the map would render pure black with no error anywhere. The map's
        /// space is its own and must never follow the ship.
        /// </summary>
        private void AnchorToOrigin()
        {
            if (transform.position != Vector3.zero || transform.rotation != Quaternion.identity)
                transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary>True once the target holds a genuinely rendered frame — used to prove the pipeline produces pixels.</summary>
        public bool HasRenderedTarget => target != null && target.IsCreated();

        /// <summary>The universe currently being drawn — the live screen reads its centre instead of reloading the static preview.</summary>
        public StrategicMapUniverseSnapshot Universe => universe;

        public Vector3 UniverseCenter => universe != null ? universe.UniverseCenter : Vector3.zero;

        /// <summary>
        /// Swaps in a freshly built universe and rebuilds only what depends on it: the
        /// system markers, flares and discovery markers, the route's ship end, and — via a
        /// cleared cache — whatever system is currently focused. The heavy scenery (galaxy
        /// cloud, starfield, halo, dust, nebulae, grid, black holes) is deliberately left
        /// standing: galaxies never change within a session, and rebuilding a million-point
        /// mesh because one planet got scanned would be absurd.
        /// </summary>
        public void RefreshUniverse(StrategicMapUniverseSnapshot fresh)
        {
            if (fresh == null || contentRoot == null) return;
            universe = fresh;

            string activeGalaxy = ActiveGalaxyId;
            string activeSystem = ActiveSystemId;

            if (decorStars != null) Destroy(decorStars.gameObject);
            decorStars = StrategicMapDecorStars.Create(contentRoot, fresh);

            if (systemMarkers != null) Destroy(systemMarkers.gameObject);
            systemMarkers = StrategicMapSystemMarkers.Create(contentRoot, fresh);
            systemMarkers.SetActiveGalaxy(activeGalaxy);

            if (flares != null) Destroy(flares.gameObject);
            flares = StrategicMapFlares.Create(contentRoot, fresh);
            flares.SetActiveGalaxy(activeGalaxy);
            if (!ambientVisible) flares.gameObject.SetActive(false);

            if (discoveryMarkers != null) Destroy(discoveryMarkers.gameObject);
            discoveryMarkers = StrategicMapDiscoveryMarkers.Create(contentRoot, fresh);
            discoveryMarkers.gameObject.SetActive(discoveriesVisible && discoveryMarkers.MarkerCount > 0);

            RefreshOperationalLayers();

            if (decorStars != null) decorStars.gameObject.SetActive(ambientVisible && decorStars.StarCount > 0);

            if (route != null) route.SetShipPosition(fresh.ShipPosition);
            if (shipMarker != null) shipMarker.SetPosition(fresh.ShipPosition);

            // The orbit-family layers cache by system id; showing null clears that cache so
            // a re-show rebuilds from the fresh rows (a newly scanned planet must appear).
            // No FocusSystem here: refreshing data must never move the camera or fire the pulse.
            if (orbits != null) orbits.ShowSystem(fresh, null);
            if (asteroidBelt != null) asteroidBelt.ShowSystem(fresh, null);
            if (planets != null) planets.ShowSystem(fresh, null);
            if (systemStar != null) systemStar.ShowSystem(fresh, null);
            if (!string.IsNullOrEmpty(activeSystem))
            {
                if (orbits != null) orbits.ShowSystem(fresh, activeSystem);
                if (asteroidBelt != null) asteroidBelt.ShowSystem(fresh, activeSystem);
                if (planets != null) planets.ShowSystem(fresh, activeSystem);
                if (systemStar != null) systemStar.ShowSystem(fresh, activeSystem);
            }
        }

        private StrategicMapOperationalSnapshot ReadOperationalSnapshot(
            StrategicMapUniverseSnapshot fromUniverse)
        {
            if (operationalSource == null) return StrategicMapOperationalSnapshot.Empty;
            try
            {
                return operationalSource.BuildOperationalSnapshot(fromUniverse)
                       ?? StrategicMapOperationalSnapshot.Empty;
            }
            catch (System.InvalidOperationException)
            {
                // Composition is allowed to finish after the map renderer is created. An
                // unavailable authority means an empty layer, never a guessed contact.
                return StrategicMapOperationalSnapshot.Empty;
            }
        }

        private void RefreshOperationalLayers()
        {
            if (operationalMarkers == null) return;
            operationalMarkers.Refresh(ReadOperationalSnapshot(universe));
            operationalMarkers.SetScope(ActiveGalaxyId, ActiveSystemId);
            nextOperationalRefreshTime = Time.unscaledTime + OperationalRefreshIntervalSeconds;
        }

        /// <summary>World-space bounds of everything the map draws — lets a diagnostic prove the camera is aimed at real geometry.</summary>
        public Bounds ContentBounds
        {
            get
            {
                var bounds = new Bounds(cameraTarget, Vector3.zero);
                bool any = false;
                foreach (Renderer r in contentRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                return bounds;
            }
        }

        public void SetActiveRendering(bool active)
        {
            if (gameObject.activeSelf == active) return;
            gameObject.SetActive(active);
        }

        // ------------------------------------------------------------------ input

        public void Drag(Vector2 delta)
        {
            // A new grab cancels whatever the camera was still coasting through.
            dragVelocity = Vector2.zero;
            yaw -= delta.x * DragSensitivity;
            pitch = Mathf.Clamp(pitch + delta.y * DragSensitivity, MinPitchRadians, MaxPitchRadians);
        }

        /// <summary>
        /// Lets go of the camera with the speed it was moving at, so it coasts to a stop
        /// instead of stopping dead under the finger. The velocity is in pixels per second,
        /// which is what the screen measures the drag in.
        /// </summary>
        public void ReleaseDrag(Vector2 velocity)
        {
            dragVelocity = StrategicMapTheme.ReducedMotion ? Vector2.zero : velocity;
        }

        public void Zoom(float wheelDelta, bool inSystemView)
        {
            float min = inSystemView ? MinDistanceSystem : MinDistanceUniverse;
            desiredDistance = Mathf.Clamp(desiredDistance * Mathf.Exp(wheelDelta * WheelZoomFactor), min, MaxDistance);
        }

        /// <summary>
        /// The galaxy whose systems are shown. Null both at the universe overview and inside
        /// a system, which is the reference's own behaviour: it draws no system markers until
        /// a galaxy is active, and none at all once a system is.
        /// </summary>
        public string ActiveGalaxyId { get; private set; }

        /// <summary>
        /// Focuses a galaxy: its systems appear and the camera moves in to frame it. The
        /// distance is set from the galaxy's own radius so a large and a small galaxy both
        /// fill the same share of the frame.
        /// </summary>
        public void FocusGalaxy(string galaxyId)
        {
            if (universe == null) return;
            // Leaving a system on the way into its galaxy, so the system's own contents stop
            // being drawn as the camera pulls back out.
            if (!string.IsNullOrEmpty(ActiveSystemId)) FocusSystem(null);
            SetActiveGalaxy(galaxyId);
            if (string.IsNullOrEmpty(galaxyId)) return;
            PulseFocus();

            for (int i = 0; i < universe.galaxies.Count; i++)
            {
                if (universe.galaxies[i].id != galaxyId) continue;
                // Half-height at distance d is d * tan(fov/2); framing a radius r with a
                // little margin means d = r * 1.35 / tan(fov/2).
                float distance = universe.galaxies[i].radius * 1.35f /
                                 Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
                FocusOn(universe.galaxies[i].Center, distance);
                return;
            }
        }

        /// <summary>Fires the reference's 900ms focus lock: a ring expanding from the centre.</summary>
        private void PulseFocus()
        {
            if (postProcess != null) postProcess.TriggerFocusPulse();
        }

        private void SetActiveGalaxy(string galaxyId)
        {
            ActiveGalaxyId = galaxyId;
            if (systemMarkers != null) systemMarkers.SetActiveGalaxy(galaxyId);
            if (operationalMarkers != null) operationalMarkers.SetScope(galaxyId, ActiveSystemId);
            if (flares != null)
            {
                flares.SetActiveGalaxy(galaxyId);
                if (!ambientVisible) flares.gameObject.SetActive(false);
            }
        }

        /// <summary>The system whose contents are on screen, or null.</summary>
        public string ActiveSystemId { get; private set; }

        /// <summary>
        /// Resolves a point on the map surface to the object under it. The point is in the
        /// map camera's own pixels, which is what the screen converts a mouse position into.
        /// </summary>
        public StrategicMapPick Pick(Vector2 mapPixel, StrategicMapLevel level)
        {
            float visualScale = 1f;
            if (!string.IsNullOrEmpty(ActiveSystemId) && universe != null)
                for (int i = 0; i < universe.systems.Count; i++)
                    if (universe.systems[i].id == ActiveSystemId)
                    { visualScale = StrategicMapOrbits.VisualScale(universe.systems[i]); break; }

            return StrategicMapPicker.Pick(mapCamera, universe, mapPixel, level,
                ActiveGalaxyId, ActiveSystemId, visualScale);
        }

        /// <summary>
        /// Marks what is selected and what the cursor is over. Ids are resolved to positions
        /// here rather than by the caller, so the overlay can follow a planet along its orbit
        /// instead of being pinned where it was when it was clicked.
        /// </summary>
        public void SetSelection(string selectedId, string hoveredId)
        {
            this.selectedId = selectedId;
            this.hoveredId = hoveredId;
            if (selectionOverlay == null) return;
            selectionOverlay.SetSelected(ResolvePosition(selectedId));
            selectionOverlay.SetHovered(ResolvePosition(hoveredId));
        }

        /// <summary>
        /// Moves the ship marker to where the ship actually is right now, in map units. Pushed
        /// every frame by the screen from the navigation authority: the universe snapshot only
        /// carries a ship position from whenever it was last rebuilt, which is a knowledge
        /// event, not a movement one.
        /// </summary>
        public void SetShipPosition(Vector3 mapPosition)
        {
            if (shipMarker != null) shipMarker.SetPosition(mapPosition);
            if (route != null) route.SetShipPosition(mapPosition);
        }

        /// <summary>Plots the navigation route to an object, or clears it when given nothing.</summary>
        public void SetWaypoint(string targetId)
        {
            waypointId = targetId;
            if (route != null) route.SetDestination(ResolvePosition(targetId));
        }

        /// <summary>Changes presentation layers only; gameplay state and cached routes survive.</summary>
        public void SetLayerVisible(StrategicMapDisplayLayer layer, bool visible)
        {
            switch (layer)
            {
                case StrategicMapDisplayLayer.Grid:
                    gridVisible = visible;
                    if (grid != null) grid.gameObject.SetActive(visible);
                    break;
                case StrategicMapDisplayLayer.Route:
                    routeVisible = visible;
                    if (route != null) route.SetDisplayEnabled(visible);
                    break;
                case StrategicMapDisplayLayer.Discoveries:
                    discoveriesVisible = visible;
                    if (discoveryMarkers != null)
                        discoveryMarkers.gameObject.SetActive(visible && discoveryMarkers.MarkerCount > 0);
                    break;
                case StrategicMapDisplayLayer.Fleet:
                    fleetVisible = visible;
                    if (operationalMarkers != null)
                        operationalMarkers.SetLayerVisible(StrategicMapOperationalKind.Fleet, visible);
                    break;
                case StrategicMapDisplayLayer.Logistics:
                    logisticsVisible = visible;
                    if (operationalMarkers != null)
                        operationalMarkers.SetLayerVisible(StrategicMapOperationalKind.Logistics, visible);
                    break;
                case StrategicMapDisplayLayer.Threat:
                    threatVisible = visible;
                    if (operationalMarkers != null)
                        operationalMarkers.SetLayerVisible(StrategicMapOperationalKind.Threat, visible);
                    break;
                case StrategicMapDisplayLayer.Ambient:
                    ambientVisible = visible;
                    if (decorStars != null) decorStars.gameObject.SetActive(visible && decorStars.StarCount > 0);
                    if (foregroundDust != null) foregroundDust.gameObject.SetActive(visible);
                    if (flares != null) flares.gameObject.SetActive(visible);
                    break;
            }
        }

        /// <summary>Where an object is on screen right now, for chrome that has to sit beside it.</summary>
        public bool TryScreenPosition(string id, out Vector2 screenPoint)
        {
            screenPoint = default;
            Vector3? world = ResolvePosition(id);
            if (!world.HasValue || mapCamera == null) return false;
            Vector3 projected = mapCamera.WorldToScreenPoint(world.Value);
            if (projected.z <= 0f) return false;
            screenPoint = new Vector2(projected.x, projected.y);
            return true;
        }

        /// <summary>Label shown for a star the player has seen but not yet identified.</summary>
        public const string UndiscoveredLabel = "UNDISCOVERED";

        /// <summary>True when the id names a real star the player has not located yet.</summary>
        public bool IsUndiscoveredSystem(string systemId)
        {
            if (string.IsNullOrEmpty(systemId) || universe == null) return false;
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == systemId) return !universe.systems[i].Discovered;
            return false;
        }

        /// <summary>The display name of a galaxy, system or planet.</summary>
        public string NameOf(string id)
        {
            if (string.IsNullOrEmpty(id) || universe == null) return null;
            for (int i = 0; i < universe.galaxies.Count; i++)
                if (universe.galaxies[i].id == id) return universe.galaxies[i].name;
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == id)
                    // Every system hovers as itself; the map states nothing about where the
                    // player has or has not been.
                    return universe.systems[i].name;
            for (int i = 0; i < universe.planets.Count; i++)
                if (universe.planets[i].id == id) return universe.planets[i].name;
            if (TryFindPoi(id, out StrategicMapUniverseSnapshot.MarkerRow poi)) return poi.name;
            return null;
        }

        /// <summary>Finds a galaxy, system or planet by id and returns where it is right now.</summary>
        private Vector3? ResolvePosition(string id)
        {
            if (string.IsNullOrEmpty(id) || universe == null) return null;

            for (int i = 0; i < universe.galaxies.Count; i++)
                if (universe.galaxies[i].id == id) return universe.galaxies[i].Center;

            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == id) return universe.systems[i].Position;

            for (int i = 0; i < universe.systems.Count; i++)
            {
                StrategicMapUniverseSnapshot.SystemRow system = universe.systems[i];
                for (int p = system.planetFrom; p < system.planetTo; p++)
                {
                    if (universe.planets[p].id != id) continue;
                    // The same orbit maths and the same clock the world itself is placed with,
                    // which is what lets the reticle and the route ride along with a planet
                    // instead of staying pinned where it was when it was clicked.
                    return StrategicMapOrbits.OrbitPoint(system.Position, universe.planets[p],
                        StrategicMapOrbitClock.PhaseOf(universe.planets[p]),
                        StrategicMapOrbits.VisualScale(system));
                }
            }

            if (TryFindPoi(id, out StrategicMapUniverseSnapshot.MarkerRow poi)) return poi.Position;

            return null;
        }

        private bool TryFindPoi(string id, out StrategicMapUniverseSnapshot.MarkerRow marker)
        {
            marker = null;
            if (universe == null || string.IsNullOrEmpty(id)) return false;
            if (TryFindMarker(universe.discoveries, id, out marker)) return true;
            if (TryFindMarker(universe.derelicts, id, out marker)) return true;
            return TryFindMarker(universe.anomalies, id, out marker);
        }

        private static bool TryFindMarker(
            System.Collections.Generic.IReadOnlyList<StrategicMapUniverseSnapshot.MarkerRow> rows,
            string id, out StrategicMapUniverseSnapshot.MarkerRow marker)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].id == id) { marker = rows[i]; return true; }
            marker = null;
            return false;
        }

        public void FocusOn(Vector3 target, float wantedDistance)
        {
            desiredTarget = target;
            desiredDistance = Mathf.Clamp(wantedDistance, MinDistanceSystem, MaxDistance);
        }

        /// <summary>
        /// Puts the camera where it is heading immediately instead of easing into it.
        /// Used when the map opens: the smoothing that makes a mid-session move feel
        /// cinematic would otherwise spend its first second flying in from the universe
        /// centre, so the player opens the map onto empty space.
        /// </summary>
        public void SnapCamera()
        {
            cameraTarget = desiredTarget;
            distance = desiredDistance;
            ApplyCamera();
        }

        /// <summary>
        /// Where a system sits relative to the ship, for chrome that has to describe a star
        /// the gameplay snapshot does not carry. Distance comes back in world units so the
        /// caller can round it through the project's own imprecise-display helper.
        /// </summary>
        public bool TryDescribeFromShip(string systemId, out float worldDistance, out string bearingLabel)
        {
            worldDistance = 0f; bearingLabel = null;
            if (universe == null || string.IsNullOrEmpty(systemId)) return false;
            for (int i = 0; i < universe.systems.Count; i++)
            {
                if (universe.systems[i].id != systemId) continue;
                Vector3 ship = universe.ShipPosition;
                Vector3 star = universe.systems[i].Position;
                worldDistance = Vector3.Distance(ship, star) * StrategicMapOrbits.MapUnit;
                bearingLabel = Navigation.NavigationFormat.ApproxBearingLabel(ship, star);
                return true;
            }
            return false;
        }

        /// <summary>
        /// How long this world takes to go once round its star, in seconds — the world's own
        /// figure, and the same one the map is animating it at. False when the id is not a
        /// planet the map is carrying.
        /// </summary>
        public bool TryOrbitPeriodSeconds(string planetId, out float seconds)
        {
            seconds = 0f;
            if (universe == null || string.IsNullOrEmpty(planetId)) return false;
            for (int i = 0; i < universe.planets.Count; i++)
            {
                if (universe.planets[i].id != planetId) continue;
                seconds = universe.planets[i].OrbitPeriodSeconds;
                return seconds > 0f;
            }
            return false;
        }

        /// <summary>Which galaxy a system belongs to, so the map can open inside one without guessing.</summary>
        public string GalaxyOfSystem(string systemId)
        {
            if (universe == null || string.IsNullOrEmpty(systemId)) return null;
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == systemId) return universe.systems[i].galaxyId;
            return null;
        }

        /// <summary>
        /// Focuses a system: the camera moves in and its orbits appear. The reference uses
        /// distance 1.72 for a system, and its own smoothing carries the move.
        /// </summary>
        public void FocusSystem(string systemId)
        {
            if (universe == null || orbits == null) return;
            if (string.IsNullOrEmpty(systemId))
            {
                ActiveSystemId = null;
                if (operationalMarkers != null)
                    operationalMarkers.SetScope(ActiveGalaxyId, null);
                if (systemStar != null) systemStar.ShowSystem(universe, null);
                orbits.ShowSystem(universe, null);
                if (asteroidBelt != null) asteroidBelt.ShowSystem(universe, null);
                if (planets != null) planets.ShowSystem(universe, null);
                if (postProcess != null) postProcess.SystemView = false;
                return;
            }

            for (int i = 0; i < universe.systems.Count; i++)
            {
                if (universe.systems[i].id != systemId) continue;
                // No sibling markers or flares in the system view. drawObjects returns
                // immediately once a system is active, so the only thing the reference draws
                // at this level is the system's own contents -- its star, orbits, worlds and
                // belt. Leaving the siblings on laid horizontal flare streaks across the
                // frame from stars that are not even in it.
                SetActiveGalaxy(null);
                ActiveSystemId = systemId;
                if (operationalMarkers != null)
                    operationalMarkers.SetScope(null, systemId);
                if (systemStar != null) systemStar.ShowSystem(universe, systemId);
                orbits.ShowSystem(universe, systemId);
                if (asteroidBelt != null) asteroidBelt.ShowSystem(universe, systemId);
                if (planets != null) planets.ShowSystem(universe, systemId);
                if (postProcess != null) postProcess.SystemView = true;
                PulseFocus();
                FocusOn(universe.systems[i].Position, SystemFramedDistance());
                return;
            }

            // A stale/invalid id must never leave the previously focused system visible.
            // The presenter may already have fallen back, so keeping old planets here would
            // make the screen show one level while the picker resolves another.
            FocusSystem(null);
        }

        public void ResetToOverview(Vector3 universeCenter)
        {
            yaw = InitialYawDegrees * Mathf.Deg2Rad;
            pitch = InitialPitchDegrees * Mathf.Deg2Rad;
            dragVelocity = Vector2.zero;
            desiredTarget = universeCenter;
            desiredDistance = OverviewDistance;
            ActiveSystemId = null;
            SetActiveGalaxy(null);
            if (operationalMarkers != null) operationalMarkers.SetScope(null, null);
            if (orbits != null && universe != null) orbits.ShowSystem(universe, null);
            if (asteroidBelt != null && universe != null) asteroidBelt.ShowSystem(universe, null);
            if (planets != null && universe != null) planets.ShowSystem(universe, null);
            if (systemStar != null && universe != null) systemStar.ShowSystem(universe, null);
            if (postProcess != null) postProcess.SystemView = false;
        }

        // ------------------------------------------------------------------ tick

        private void Update()
        {
            // Map animation is always unscaled: it must read the same whatever the
            // simulation is doing, and the map never pauses the game.
            AnchorToOrigin();

            float dt = Time.unscaledDeltaTime;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) dt = 0f;
            dt = Mathf.Min(dt, 0.1f);
            elapsed += dt;

            if (Time.unscaledTime >= nextOperationalRefreshTime)
                RefreshOperationalLayers();

            if (StrategicMapTheme.MotionOff)
            {
                dragVelocity = Vector2.zero;
                cameraTarget = desiredTarget;
                distance = desiredDistance;
                ApplyCamera();
                RefreshDynamicTargets();
                return;
            }

            // Coast on after the drag is released, shedding speed exponentially so the stop
            // is smooth however fast the camera was turning.
            if (dragVelocity.sqrMagnitude > 0.01f)
            {
                yaw -= dragVelocity.x * DragSensitivity * dt;
                pitch = Mathf.Clamp(pitch + dragVelocity.y * DragSensitivity * dt,
                    MinPitchRadians, MaxPitchRadians);
                dragVelocity *= Mathf.Pow(DragDecayPerSecond, dt);
            }
            else dragVelocity = Vector2.zero;

            float k = 1f - Mathf.Pow(SmoothingBase, dt);
            cameraTarget = Vector3.Lerp(cameraTarget, desiredTarget, k);
            distance = Mathf.Lerp(distance, desiredDistance, k);

            ApplyCamera();
            RefreshDynamicTargets();
        }

        /// <summary>
        /// Planets move while the map is open. Selection, hover and a possible visual target
        /// therefore keep ids—not one-time coordinates—and are re-resolved from the shared
        /// orbit clock every frame.
        /// </summary>
        private void RefreshDynamicTargets()
        {
            if (selectionOverlay != null)
            {
                selectionOverlay.SetSelected(ResolvePosition(selectedId));
                selectionOverlay.SetHovered(ResolvePosition(hoveredId));
            }
            if (route != null && !string.IsNullOrEmpty(waypointId))
                route.SetDestination(ResolvePosition(waypointId));
        }

        private void ApplyCamera()
        {
            // The reference adds a slow drift only while idle — not while dragging, and not
            // with reduced motion on.
            bool cinematicIdle = !StrategicMapTheme.ReducedMotion && !interacting;
            float viewYaw = yaw + (cinematicIdle ? Mathf.Sin(elapsed * 0.075f) * 0.022f : 0f);
            float viewPitch = pitch + (cinematicIdle ? Mathf.Sin(elapsed * 0.052f + 0.8f) * 0.009f : 0f);

            float horizontal = Mathf.Cos(viewPitch) * distance;
            var position = new Vector3(
                cameraTarget.x + Mathf.Sin(viewYaw) * horizontal,
                cameraTarget.y + Mathf.Sin(viewPitch) * distance,
                cameraTarget.z + Mathf.Cos(viewYaw) * horizontal);

            if (mapCamera == null) return;

            // Compose by moving the camera, not by shearing its lens.
            //
            // This used to assign a sheared perspective matrix (m02/m12) to bias the
            // subject left and up, away from the left rail and the right inspector. It
            // framed correctly and broke everything that asks the camera where a point
            // is: shifting the optical centre up by 0.16 pushed the bottom of the
            // viewport outside the frustum, and Unity logged "Screen position out of
            // view frustum" for the two bottom corners at the far plane on every frame a
            // system was focused. Only the two PlayMode tests that enter a system saw it,
            // which is exactly how it reached this point.
            //
            // A translation gives the same composition with a camera that stays
            // self-consistent -- so the picker's WorldToScreenPoint, the reticle and the
            // selection overlay all keep agreeing with what is actually drawn, which a
            // sheared projection quietly stopped doing.
            Vector3 eye = position;
            Vector3 lookAt = cameraTarget;
            Vector3 toTarget = lookAt - eye;
            if (toTarget.sqrMagnitude > 0.0000001f && !string.IsNullOrEmpty(ActiveSystemId))
            {
                Vector3 ahead = toTarget.normalized;
                Vector3 right = Vector3.Cross(Vector3.up, ahead);
                if (right.sqrMagnitude > 0.0000001f)
                {
                    right.Normalize();
                    Vector3 up = Vector3.Cross(ahead, right);

                    // The subject moves opposite to the camera, so the offset is negated.
                    float visibleHeight = 2f * toTarget.magnitude *
                        Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
                    float visibleWidth = visibleHeight * CameraAspect();
                    Vector3 shift = right * (-SystemCompositionOffset.x * visibleWidth)
                                  + up * (-SystemCompositionOffset.y * visibleHeight);
                    eye += shift;
                    lookAt += shift;
                }
            }

            // Unity's own aspect handling, always. A camera left holding an assigned
            // matrix stops reacting to a resized render target, and the universe and
            // galaxy levels -- which apply no offset at all -- were still carrying one.
            mapCamera.ResetProjectionMatrix();
            mapCamera.transform.position = eye;
            Vector3 forward = lookAt - eye;
            if (forward.sqrMagnitude > 0.0000001f)
            {
                mapCamera.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                // The planet fill light follows the eye, so a world is never dark simply
                // because the camera has been dragged round behind it.
                if (planets != null) planets.SetViewDirection(forward);
            }
        }

        /// <summary>
        /// The render target's aspect, never <c>Camera.aspect</c> when a matrix has been
        /// assigned — that reads back out of the matrix and is self-referential.
        /// </summary>
        private float CameraAspect()
        {
            if (target != null && target.height > 0) return target.width / (float)target.height;
            if (mapCamera != null && mapCamera.pixelHeight > 0)
                return mapCamera.pixelWidth / (float)mapCamera.pixelHeight;
            return 16f / 9f;
        }


        private float SystemFramedDistance()
        {
            float aspect = target != null && target.height > 0
                ? target.width / (float)target.height
                : Mathf.Max(0.01f, mapCamera != null ? mapCamera.aspect : 16f / 9f);
            // Narrow screens have less room between the left rail and right inspector.
            float narrowScreen = Mathf.Max(1f, (16f / 9f) / aspect);
            float uiScale = Mathf.Clamp(Shell.NebulaUiShell.UiScale,
                Shell.NebulaUiShell.MinUiScale, Shell.NebulaUiShell.MaxUiScale);
            float largeUi = 1f + Mathf.Max(0f, uiScale - 1f) * 0.35f;
            return SystemFocusDistance * narrowScreen * largeUi;
        }
    }
}
