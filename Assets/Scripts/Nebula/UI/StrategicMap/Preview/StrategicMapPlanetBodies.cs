using System.Collections.Generic;
using Nebula.Sectors;
using Nebula.Visuals;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The planets of the focused system, as the game's own 3D bodies rather than painted
    /// discs: each world is the real prefab for its resource family, lit by a real light at
    /// the system's star.
    ///
    /// This deliberately goes through the existing visual seam --
    /// <see cref="PlanetArchetypeCatalog.ForFamily"/> to pick the archetype and
    /// <see cref="NebulaVisualResolver"/> to instantiate it -- so the map shows exactly the
    /// asset the game shows, and a future art change reaches the map for free. The map does
    /// not get its own copy of the planet-to-asset table.
    ///
    /// The star is a real point light, so the day and night sides are genuinely lit rather
    /// than painted on. It only illuminates the map layer, so nothing in the game scene sees it.
    /// </summary>
    public sealed class StrategicMapPlanetBodies : MonoBehaviour
    {
        /// <summary>
        /// Map units per unit of the reference's own size term.
        ///
        /// The reference sizes a planet by (0.023 + radius / 158000), which compresses the
        /// real radius range to about 1.6:1 so small worlds stay visible. That law is kept;
        /// only the conversion to map units is ours, since a real mesh cannot hold a constant
        /// pixel size the way the reference's painted disc does.
        ///
        /// Chosen so the largest world spans roughly the gap between mid orbits -- about 0.06
        /// map units, which at the focus distance of 1.72 is near 40px of a 1080-tall view.
        /// Sized from the orbits rather than picked by eye: too large and neighbouring worlds
        /// collide, too small and they vanish. At 0.38 they rendered at about 12px and read
        /// as empty space.
        /// </summary>
        private const float SizeTermToMapUnits = 0.77f;

        /// <summary>Reference: the on-screen radius term, before its 5..12 pixel clamp.</summary>
        private const float RadiusDivisor = 158000f;
        private const float RadiusBase = 0.023f;

        /// <summary>One materialized world, with everything needed to keep it on its orbit.</summary>
        private sealed class Body
        {
            public GameObject Root;
            public StrategicMapUniverseSnapshot.PlanetRow Row;
            public float SpinSpeed;
            public float AxialTilt;
        }

        private readonly List<Body> instances = new List<Body>();
        private Light starLight;
        private Light fillLight;
        private string builtForSystemId;
        private Vector3 systemPosition;
        private float visualScale = 1f;
        private float elapsed;

        public int BodyCount => instances.Count;

        public static StrategicMapPlanetBodies Create(Transform parent)
        {
            var go = new GameObject("Planet Bodies") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var bodies = go.AddComponent<StrategicMapPlanetBodies>();
            bodies.Initialise();
            go.SetActive(false);
            return bodies;
        }

        private void Initialise()
        {
            // A real star, and a fill so the night side is readable rather than pure black.
            // Both are confined to the map layer -- the game scene never sees them.
            //
            // The map cannot raise the ambient probe to help here: that setting is global,
            // and the map is an overlay on a live scene it must not relight. So the two lights
            // it owns carry the whole exposure themselves.
            //
            // These contribute far less than they look like they should, and that is measured,
            // not assumed: with BOTH lights at zero the system view still clips 0.201% of its
            // pixels to white, against 0.203% at 1.25 and 0.227% at 2.6. The planet prefabs
            // light themselves, and the terminator visible on them is the material's own
            // shading rather than anything this star does.
            //
            // The exposure is handled by DimForMap instead.
            //
            // The lights stay small and real, for the day the art changes to something that
            // does need them.
            int mapOnly = 1 << StrategicMapUniverseRenderer.MapLayer;

            var starGo = new GameObject("Star Light") { layer = StrategicMapUniverseRenderer.MapLayer };
            starGo.transform.SetParent(transform, false);
            starLight = starGo.AddComponent<Light>();
            starLight.type = LightType.Point;
            starLight.color = StrategicMapTheme.Gold;
            starLight.intensity = 2.4f;
            // The old range of 6 stopped short of the outer orbits of a widely-scaled system,
            // so the furthest worlds got nothing from their own star at all.
            starLight.range = 40f;
            starLight.shadows = LightShadows.None;
            starLight.cullingMask = mapOnly;

            // The fill is a headlight: it is re-aimed down the map camera's own forward vector
            // every frame (see SetViewDirection). A fixed direction is what left whole worlds
            // in shadow depending on where they happened to be in their orbit and where the
            // player had turned the camera to — the "some planets are still dark" case. Lit
            // from the eye, no world the player is looking at can be facing away from a light.
            var fillGo = new GameObject("View Fill Light") { layer = StrategicMapUniverseRenderer.MapLayer };
            fillGo.transform.SetParent(transform, false);
            fillLight = fillGo.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.86f, 0.91f, 1f);
            fillLight.intensity = 1.15f;
            fillLight.shadows = LightShadows.None;
            fillLight.cullingMask = mapOnly;
            fillGo.transform.rotation = Quaternion.Euler(38f, -140f, 0f);
        }

        /// <summary>
        /// Aims the fill light along the view direction. Called by the renderer each frame,
        /// which is the only thing that knows where the camera has been dragged to.
        /// </summary>
        public void SetViewDirection(Vector3 forward)
        {
            if (fillLight == null || forward.sqrMagnitude < 0.000001f) return;
            // A few degrees off-axis so the terminator is still visible and a sphere still
            // reads as a sphere -- a light exactly down the view axis flattens everything.
            fillLight.transform.rotation = Quaternion.LookRotation(forward.normalized) *
                                           Quaternion.Euler(14f, -18f, 0f);
        }

        /// <summary>Shows one system's worlds. Rebuilds only when the focused system changes.</summary>
        public void ShowSystem(StrategicMapUniverseSnapshot universe, string systemId)
        {
            if (string.IsNullOrEmpty(systemId))
            {
                builtForSystemId = null;
                gameObject.SetActive(false);
                return;
            }
            if (systemId == builtForSystemId) { gameObject.SetActive(true); return; }

            StrategicMapUniverseSnapshot.SystemRow system = null;
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == systemId) { system = universe.systems[i]; break; }
            if (system == null) { gameObject.SetActive(false); return; }

            Build(universe, system);
            builtForSystemId = systemId;
            gameObject.SetActive(instances.Count > 0);
        }

        private void Build(StrategicMapUniverseSnapshot universe, StrategicMapUniverseSnapshot.SystemRow system)
        {
            Clear();

            // Held for the whole time this system is shown: every frame puts each world back
            // on its orbit from these, so the layer never has to look its system up again.
            visualScale = StrategicMapOrbits.VisualScale(system);
            systemPosition = system.Position;
            starLight.transform.position = system.Position;

            for (int p = system.planetFrom; p < system.planetTo; p++)
            {
                StrategicMapUniverseSnapshot.PlanetRow planet = universe.planets[p];

                PlanetArchetype archetype = ArchetypeFor(planet.resourceFamily);
                if (!NebulaVisualCatalog.TryGetPlanet(archetype, out NebulaVisualDefinition definition)) continue;

                float diameter = VisualDiameterMapUnits(planet);
                GameObject body = NebulaVisualResolver.Instantiate(definition, transform, diameter,
                    "Planet :: " + planet.id);
                if (body == null) continue;

                body.transform.position = StrategicMapOrbits.OrbitPoint(
                    system.Position, planet, StrategicMapOrbitClock.PhaseOf(planet), visualScale);
                // The world's own axial tilt, so a gas giant's bands lie where they should.
                body.transform.rotation = Quaternion.Euler(planet.axialTilt, 0f, 0f);

                SetLayerRecursively(body.transform, StrategicMapUniverseRenderer.MapLayer);
                // An unscanned world is the right shape but not yet a known quantity, so it
                // sits a touch back from the scanned ones rather than being hidden or faked.
                DimForMap(body, planet.scanned ? MapExposure : MapExposure * UnscannedExposure);

                instances.Add(new Body
                {
                    Root = body,
                    Row = planet,
                    SpinSpeed = planet.spinSpeed,
                    AxialTilt = planet.axialTilt
                });
            }
        }

        /// <summary>
        /// Takes the map's copies down to map exposure.
        ///
        /// The prefabs are authored for the game's own scene, where a planet fills the view
        /// and is meant to be brilliant. On the map ten of them share the frame at forty
        /// pixels each, and at full strength a third of every lit face clips flat to white --
        /// which costs exactly the terminator that makes them read as spheres.
        ///
        /// The property names are the glTF importer's, and they are not Unity's usual ones:
        /// baseColorFactor and emissiveFactor, camelCase and with no leading underscore. An
        /// earlier attempt at this scaled _BaseColorFactor and _EmissiveFactor, which simply
        /// do not exist on these materials -- HasProperty returned false, nothing was written,
        /// and the measurement came back unchanged. Reading the real property list off the
        /// shader is what settled it.
        ///
        /// Only the map's instances are touched: each renderer gets its own material copies,
        /// so nothing the game renders changes.
        /// </summary>
        private static void DimForMap(GameObject body, float exposure)
        {
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.materials;   // instances, not the shared assets
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) continue;
                    if (material.HasProperty(BaseColorFactor))
                        material.SetColor(BaseColorFactor, Scaled(material.GetColor(BaseColorFactor), exposure));
                    if (material.HasProperty(EmissiveFactor))
                        material.SetColor(EmissiveFactor, Scaled(material.GetColor(EmissiveFactor), exposure));
                }
                renderer.materials = materials;
            }
        }

        /// <summary>Scales colour but never alpha -- dimming a planet must not make it transparent.</summary>
        private static Color Scaled(Color color, float exposure) =>
            new Color(color.r * exposure, color.g * exposure, color.b * exposure, color.a);

        /// <summary>
        /// How much of the prefab's authored brightness the map keeps.
        ///
        /// This was 0.5, held down to protect the terminator from clipping, with a further
        /// 0.55 on top for anything unscanned — so an unscanned world was drawn at barely a
        /// quarter brightness against a near-black sky. Since almost nothing is scanned early
        /// on, that meant most of the worlds in most systems were unlit smudges.
        ///
        /// It is now 1: the map shows the art at the brightness it was authored at. Scan
        /// state is carried by the card's own words, never by making a world too dark to find.
        /// </summary>
        private const float MapExposure = 1f;

        /// <summary>
        /// Unscanned worlds are drawn at full brightness too. The dimming that used to mark
        /// them was the single biggest reason planets read as dark, and darkness is a terrible
        /// carrier for "not yet surveyed" — it looks like a rendering fault, not a state.
        /// </summary>
        private const float UnscannedExposure = 1f;

        /// <summary>
        /// The exact diameter used to instantiate a planet on the map.
        ///
        /// Picking must use this same law. A fixed pixel hit target only covered the centre
        /// of a world once the camera zoomed in, leaving most of the visibly rendered disc
        /// unable to be clicked.
        /// </summary>
        public static float VisualDiameterMapUnits(StrategicMapUniverseSnapshot.PlanetRow planet) =>
            planet == null
                ? 0f
                : 2f * (RadiusBase + Mathf.Max(0f, planet.radius) / RadiusDivisor) * SizeTermToMapUnits;

        private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
        private static readonly int EmissiveFactor = Shader.PropertyToID("emissiveFactor");

        /// <summary>
        /// The snapshot carries the family as the reference's own string. Parsing it here
        /// keeps the map's data format independent of the game's enum ordering, which is
        /// save-facing and must not be reordered to suit a preview.
        /// </summary>
        public static PlanetArchetype ArchetypeFor(string resourceFamily)
        {
            switch (resourceFamily)
            {
                case "Metal": return PlanetArchetype.MetallicRocky;
                case "Wood": return PlanetArchetype.Biomass;
                case "Gas": return PlanetArchetype.UpperAtmospherePlatform;
                case "Water": return PlanetArchetype.FloatingOceanic;
                default: return PlanetArchetype.CrystalShell;
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }

        private void Clear()
        {
            for (int i = 0; i < instances.Count; i++)
                if (instances[i]?.Root != null) Destroy(instances[i].Root);
            instances.Clear();
        }

        /// <summary>
        /// Walks every world round its star and spins it on its axis.
        ///
        /// The orbit is not integrated here: each world is placed from
        /// <see cref="StrategicMapOrbitClock"/>, the same pure function the click picker and
        /// the selection reticle read, so what the eye is following and what a click resolves
        /// can never be two different places.
        ///
        /// Reduced motion silences the axial spin, which is decoration. It deliberately does
        /// NOT stop the orbits: where a world is in its year is the simulation the player
        /// came here to read, and freezing it would also strand every planet away from the
        /// position the rest of the map is picking against.
        /// </summary>
        private void Update()
        {
            if (instances.Count == 0) return;

            elapsed += Time.unscaledDeltaTime;
            bool spin = !StrategicMapTheme.ReducedMotion;

            for (int i = 0; i < instances.Count; i++)
            {
                Body body = instances[i];
                if (body?.Root == null) continue;

                body.Root.transform.position = StrategicMapOrbits.OrbitPoint(
                    systemPosition, body.Row, StrategicMapOrbitClock.PhaseOf(body.Row), visualScale);

                // Tilt the axis first, then spin about it -- composed from the stored values
                // each frame rather than read back off the transform, which would compound
                // the tilt into the spin a little more every frame.
                body.Root.transform.rotation = spin
                    ? Quaternion.Euler(body.AxialTilt, 0f, 0f) * Quaternion.Euler(0f, elapsed * body.SpinSpeed, 0f)
                    : Quaternion.Euler(body.AxialTilt, 0f, 0f);
            }
        }

        private void OnDestroy() => Clear();
    }
}
