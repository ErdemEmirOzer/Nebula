using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.UI.StrategicMap.Preview;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The LIVE <see cref="IStrategicMapUniverseSource"/>: builds the 3D map's universe
    /// from the real game authorities — <see cref="StarSystemRegistry"/> manifests,
    /// <see cref="SystemKnowledgeService"/>, <see cref="ExtractionState"/> profiles and
    /// the session's own galaxies — instead of the retired HTML preview snapshot.
    ///
    /// Two things change forever with this source in place:
    /// <list type="bullet">
    /// <item>Identity: rows carry the REAL ids (<c>sysNNN</c>, <c>sysNNN/plNN</c>, lowercase
    /// galaxy names), so a click on the 3D map resolves against the same
    /// <see cref="StrategicMapSnapshot"/> the cards and commands read. The preview's
    /// HTML-era ids (<c>helios</c>, <c>helios-planet-1</c>) never matched them.</item>
    /// <item>Information gates, identical in spirit to <see cref="StrategicMapDataSource"/>:
    /// Unknown and Signal systems are not in the geometry at all (no marker may leak a
    /// position the player has not earned), Located adds the real position only, Visited
    /// adds star/orbit/planet geometry, and a planet's family/rarity appear only once it
    /// is actually scanned.</item>
    /// </list>
    ///
    /// Strictly read-only over the authorities, like every other map class: building a
    /// universe never advances knowledge, scans, XP or dominance.
    /// </summary>
    public sealed class StrategicMapLiveUniverseSource : IStrategicMapUniverseSource, IStrategicMapOperationalSource
    {
        /// <summary>Marks a live-built snapshot; the converted HTML asset is version 3.</summary>
        public const int LiveVersion = 4;

        private readonly NebulaContext context;
        private readonly StrategicMapOperationalDataSource operationalSource;
        private StrategicMapUniverseSnapshot cached;

        public StrategicMapLiveUniverseSource(NebulaContext context)
        {
            this.context = context;
            operationalSource = new StrategicMapOperationalDataSource(context);
        }

        /// <summary>The last built universe. Knowledge changes only through gameplay, so callers decide when to <see cref="Rebuild"/> — typically when the map opens and when the known-system set changes.</summary>
        public StrategicMapUniverseSnapshot Universe => cached ?? Rebuild();

        /// <summary>
        /// A fresh lightweight operational read for the renderer's 2 Hz overlay refresh.
        /// Geometry remains cached; moving gameplay state never forces a galaxy rebuild.
        /// </summary>
        public StrategicMapOperationalSnapshot BuildOperationalSnapshot(
            StrategicMapUniverseSnapshot universe) => operationalSource.Build(universe);

        public StrategicMapUniverseSnapshot Rebuild()
        {
            SystemKnowledgeService knowledge = context?.NavigationDirector?.Knowledge;
            Vector3 shipAbsolute = context?.NavigationDirector != null
                ? context.NavigationDirector.ShipAbsolutePosition() : Vector3.zero;
            List<GalaxyDescriptor> galaxies = GatherGalaxies(context);
            NebulaBootstrapConfig config = context?.Config;
            cached = Build(knowledge, galaxies, shipAbsolute, config);
            AddDecorStars(cached, galaxies, DecorStarsOf(context));
            return cached;
        }

        private static List<Vector3> DecorStarsOf(NebulaContext context)
        {
            if (context == null) return null;
            try { return context.DecorStarPositions; }
            catch { return null; } // built before the world was — nothing to draw yet
        }

        /// <summary>
        /// Copies the game's own decorative arm stars into the snapshot, scaled to map units
        /// and filed under whichever galaxy they belong to. This is what makes the map show
        /// the same populated spiral the player sees out of the window instead of an empty
        /// disc with one cluster of navigable systems in it.
        /// </summary>
        public static void AddDecorStars(StrategicMapUniverseSnapshot snapshot,
            IReadOnlyList<GalaxyDescriptor> galaxies, IReadOnlyList<Vector3> absolutePositions,
            float ignoredGravitationalConstant = DefaultGravitationalConstant)
        {
            if (snapshot == null || absolutePositions == null) return;

            for (int i = 0; i < absolutePositions.Count; i++)
            {
                Vector3 absolute = absolutePositions[i];
                snapshot.decorStars.Add(new StrategicMapUniverseSnapshot.MarkerRow
                {
                    id = "decor." + i,
                    name = string.Empty,
                    // Never null: a scenery point belongs to no system, and every consumer
                    // of MarkerRow.systemId compares or measures the string. Leaving it at
                    // its default made the one field that says "this is not a gameplay
                    // object" the one field that could throw when asked.
                    systemId = string.Empty,
                    galaxyId = NearestGalaxyId(absolute, galaxies),
                    x = absolute.x / StrategicMapOrbits.MapUnit,
                    y = absolute.y / StrategicMapOrbits.MapUnit,
                    z = absolute.z / StrategicMapOrbits.MapUnit
                });
            }
        }

        // ---------------------------------------------------------------- galaxies

        /// <summary>One session galaxy, already in absolute world units — the pure core never touches a MonoBehaviour.</summary>
        public readonly struct GalaxyDescriptor
        {
            public readonly string Name;
            public readonly Vector3 Center;
            public readonly float Radius;
            public readonly int Particles;
            public readonly float BlackHoleRadius;
            public readonly float Whiteness;
            public readonly float ArmPhase;

            public GalaxyDescriptor(string name, Vector3 center, float radius, int particles,
                float blackHoleRadius, float whiteness, float armPhase)
            {
                Name = name; Center = center; Radius = radius; Particles = particles;
                BlackHoleRadius = blackHoleRadius; Whiteness = whiteness; ArmPhase = armPhase;
            }
        }

        /// <summary>
        /// The session's galaxies from the composition config (centres are absolute and
        /// never floating-origin shifted) plus each <see cref="GalaxyMap"/>'s real spiral
        /// phase. Index-aligned with <see cref="NebulaContext.GalaxyMaps"/>: the primary
        /// first, then active (Scale &gt; 0) companions in declaration order — exactly the
        /// order <c>EnvironmentInstaller</c> creates them in.
        /// </summary>
        public static List<GalaxyDescriptor> GatherGalaxies(NebulaContext context)
        {
            var result = new List<GalaxyDescriptor>();
            if (context == null) return result;

            NebulaBootstrapConfig config = context.Config;
            List<GalaxyMap> maps = context.GalaxyMaps;
            if (config == null) return result;

            result.Add(new GalaxyDescriptor(config.GalaxyName, config.GalaxyCenter, config.GalaxyRadius,
                config.GalaxyParticleCount, config.BlackHoleRadius, whiteness: 0f,
                armPhase: PhaseOf(maps, 0)));

            if (config.CompanionGalaxies == null) return result;
            int mapIndex = 1;
            for (int i = 0; i < config.CompanionGalaxies.Length; i++)
            {
                NebulaBootstrapConfig.CompanionGalaxy companion = config.CompanionGalaxies[i];
                if (companion.Scale <= 0f) continue; // disabled entry — the world never built it
                result.Add(new GalaxyDescriptor(companion.Name, companion.Center,
                    config.GalaxyRadius * companion.Scale,
                    Mathf.RoundToInt(config.GalaxyParticleCount * companion.Scale),
                    config.BlackHoleRadius * companion.Scale, companion.CloudWhiteness,
                    PhaseOf(maps, mapIndex)));
                mapIndex++;
            }
            return result;
        }

        private static float PhaseOf(List<GalaxyMap> maps, int index) =>
            maps != null && index < maps.Count && maps[index] != null ? maps[index].ArmPhase0 : 0f;

        // ---------------------------------------------------------------- core

        /// <summary>
        /// The pure, EditMode-provable core: a function of the knowledge service, the
        /// galaxy list and the registry/extraction statics. Missing authorities degrade
        /// to an empty universe — a presentation source must never throw.
        /// </summary>
        public static StrategicMapUniverseSnapshot Build(SystemKnowledgeService knowledge,
            IReadOnlyList<GalaxyDescriptor> galaxies, Vector3 shipAbsolute, NebulaBootstrapConfig config)
        {
            var snapshot = new StrategicMapUniverseSnapshot
            {
                version = LiveVersion,
                seed = unchecked((int)ExtractionState.UniverseSeed),
                shipX = shipAbsolute.x / StrategicMapOrbits.MapUnit,
                shipY = shipAbsolute.y / StrategicMapOrbits.MapUnit,
                shipZ = shipAbsolute.z / StrategicMapOrbits.MapUnit
            };

            if (config != null)
            {
                // Informational chrome only — no layer builds geometry from these counts.
                snapshot.starfieldStarCount = config.StarfieldStarCount;
                snapshot.haloStarsPerGalaxy = config.HaloStarsPerGalaxy;
                snapshot.clustersPerGalaxy = config.ClustersPerGalaxy;
                snapshot.bridgeStarCount = config.BridgeStarCount;
                snapshot.nebulaCount = config.NebulaCount;
            }

            if (galaxies != null)
                for (int i = 0; i < galaxies.Count; i++)
                    snapshot.galaxies.Add(ToGalaxyRow(galaxies[i]));

            BuildSystems(knowledge, galaxies, shipAbsolute, snapshot, GravitationalConstantOf(config));
            BuildDiscoveryMarkers(knowledge, snapshot);
            return snapshot;
        }

        private static StrategicMapUniverseSnapshot.GalaxyRow ToGalaxyRow(GalaxyDescriptor galaxy) =>
            new StrategicMapUniverseSnapshot.GalaxyRow
            {
                id = GalaxyId(galaxy.Name),
                name = galaxy.Name,
                centerX = galaxy.Center.x / StrategicMapOrbits.MapUnit,
                centerY = galaxy.Center.y / StrategicMapOrbits.MapUnit,
                centerZ = galaxy.Center.z / StrategicMapOrbits.MapUnit,
                // Same unit convention as the converted asset: radius in map units,
                // black-hole radius left in world units.
                radius = galaxy.Radius / StrategicMapOrbits.MapUnit,
                particles = galaxy.Particles,
                blackHoleRadius = galaxy.BlackHoleRadius,
                whiteness = galaxy.Whiteness,
                phase = galaxy.ArmPhase
            };

        public static string GalaxyId(string name) =>
            string.IsNullOrEmpty(name) ? "" : name.ToLowerInvariant();

        // ---------------------------------------------------------------- systems

        /// <summary>
        /// How far around the player the map generates extra stars, in sectors. ZERO by
        /// design: the map's stars are the ones strung across the galaxies, not a cloud of
        /// systems conjured around wherever the ship happens to be floating. Raising this
        /// puts a knot of sector-generated systems back beside the player.
        /// </summary>
        public static readonly int SceneryRadiusInSectors = 0;

        /// <summary>
        /// A Signal has a direction but deliberately no distance. It is therefore placed on
        /// this fixed presentation ring around the ship; the ring is never converted back to
        /// a gameplay distance and cannot leak the real system position.
        /// </summary>
        public const float SignalDisplayRadiusMapUnits = 2.6f;

        /// <summary>
        /// Every real star the map should draw.
        ///
        /// The map shows WHERE things are and gates WHAT they are: a star is a thing the
        /// player can see out of the cockpit window, so hiding its position taught them
        /// nothing and left the map empty. Undiscovered systems therefore appear as real,
        /// clickable, unresolved points; their name, star, planets and contents stay sealed
        /// behind the knowledge levels exactly as before.
        /// </summary>
        /// <summary>A hard ceiling, so a mis-sized galaxy config can never stall the map open.</summary>
        public const int MaxDrawnStars = 24000;

        /// <summary>
        /// The scene's own gravitational constant, used only when the map is built before
        /// the environment has installed one. Kept in step with <c>SolarSystemBootstrap</c>'s
        /// serialized default and the value in <c>Assets/Scenes/Nebula.unity</c>.
        /// </summary>
        public const float DefaultGravitationalConstant = 0.015f;

        /// <summary>
        /// The world's own gravitational constant, which is what fixes how long a planet
        /// takes to go round. A universe built before the environment has installed one
        /// falls back to the scene's value, so the map still turns at the rate the game
        /// will use rather than standing still.
        /// </summary>
        private static float GravitationalConstantOf(NebulaBootstrapConfig config) =>
            config != null && config.GravitationalConstant > 0f
                ? config.GravitationalConstant
                : DefaultGravitationalConstant;

        private static void BuildSystems(SystemKnowledgeService knowledge,
            IReadOnlyList<GalaxyDescriptor> galaxies, Vector3 shipAbsolute,
            StrategicMapUniverseSnapshot snapshot, float gravitationalConstant)
        {
            SectorManifestRepository manifests = StarSystemRegistry.Manifests;
            if (manifests == null) return;

            var full = new Dictionary<string, SystemManifest>();
            var cheap = new Dictionary<string, SectorManifestGenerator.StarPlacement>();
            var signals = new List<StrategicMapUniverseSnapshot.SystemRow>();

            // Everything the player has actually learned about, wherever in the galaxy it is —
            // a known system must never drop off the map just because they flew away from it,
            // and it is the one kind that needs its whole manifest for planets.
            if (knowledge != null)
                foreach (KeyValuePair<string, SystemKnowledgeLevel> kv in knowledge.All)
                {
                    SystemManifest known = manifests.ResolveSystem(kv.Key);
                    if (known == null) continue;
                    if (kv.Value == SystemKnowledgeLevel.Signal)
                    {
                        signals.Add(ToSignalRow(known, galaxies, shipAbsolute));
                        continue;
                    }
                    if (kv.Value >= SystemKnowledgeLevel.Located) full[kv.Key] = known;
                }

            // Nothing else is generated. The stars the map shows beyond what the player
            // knows are the galaxies' own, drawn from their real positions by the decor-star
            // layer -- not a cloud of sector systems invented around the ship.
            var buffer = new List<SectorManifestGenerator.StarPlacement>();
            foreach (SectorCoordinate sector in SectorsToDraw(galaxies, shipAbsolute))
            {
                if (full.Count + cheap.Count >= MaxDrawnStars) break;

                // A sector the game has already resolved, or one the generator alone cannot
                // reproduce (the start sector's guarantee pass moves systems), must come from
                // the real manifests or the map would draw stars that are not where they are.
                SectorManifest resolved = manifests.RequiresFullResolve(sector)
                    ? manifests.Resolve(sector)
                    : manifests.TryGetResolved(sector);

                if (resolved != null)
                {
                    foreach (SystemManifest system in resolved.Systems)
                        if (!full.ContainsKey(system.SystemId)) full[system.SystemId] = system;
                    continue;
                }

                buffer.Clear();
                SectorManifestGenerator.EnumerateStars(manifests.Seed, sector, buffer);
                for (int i = 0; i < buffer.Count; i++)
                    if (!full.ContainsKey(buffer[i].SystemId)) cheap[buffer[i].SystemId] = buffer[i];
            }

            // Canonical order: node identity must never shuffle between rebuilds.
            var ids = new List<string>(full.Count + cheap.Count);
            foreach (string id in full.Keys) ids.Add(id);
            foreach (string id in cheap.Keys) ids.Add(id);
            ids.Sort(string.CompareOrdinal);

            foreach (string systemId in ids)
            {
                SystemKnowledgeLevel level = knowledge != null
                    ? knowledge.GetLevel(systemId) : SystemKnowledgeLevel.Unknown;

                snapshot.systems.Add(full.TryGetValue(systemId, out SystemManifest manifest)
                    ? ToSystemRow(manifest, galaxies, level, snapshot, gravitationalConstant)
                    : ToStarRow(cheap[systemId], galaxies, level));
            }

            signals.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            snapshot.systems.AddRange(signals);
        }

        private static StrategicMapUniverseSnapshot.SystemRow ToSignalRow(SystemManifest manifest,
            IReadOnlyList<GalaxyDescriptor> galaxies, Vector3 shipAbsolute)
        {
            Vector3 direction = QuantizedSignalDirection(manifest.AbsoluteCenter - shipAbsolute);
            Vector3 surrogate = shipAbsolute / StrategicMapOrbits.MapUnit + direction * SignalDisplayRadiusMapUnits;
            return new StrategicMapUniverseSnapshot.SystemRow
            {
                id = manifest.SystemId,
                name = "Bilinmeyen sinyal",
                knowledge = (int)SystemKnowledgeLevel.Signal,
                galaxyId = NearestGalaxyId(surrogate * StrategicMapOrbits.MapUnit, galaxies),
                x = surrogate.x,
                y = surrogate.y,
                z = surrogate.z,
                origin = "signal",
                planetFrom = 0,
                planetTo = 0
            };
        }

        private static Vector3 QuantizedSignalDirection(Vector3 raw)
        {
            Vector3 flat = new Vector3(raw.x, 0f, raw.z);
            if (flat.sqrMagnitude < 0.0001f) return Vector3.forward;
            float degrees = Mathf.Atan2(flat.z, flat.x) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(degrees / 45f) * 45f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(snapped), 0f, Mathf.Sin(snapped));
        }

        /// <summary>
        /// Every sector that holds a star worth drawing: the ship's own, plus its 26
        /// neighbours. That is 27 sectors and about 156 real systems — the scale the game
        /// itself reports through <c>StarSystemRegistry.SystemsNear</c>, and the universe the
        /// player is actually in.
        ///
        /// Sweeping instead every sector the four decorative galaxy spheres touch was tried
        /// and is wrong: those spheres are 5 sectors in radius, so the generator would put
        /// some 7,400 systems inside them and the galaxy view became a solid white wall of
        /// stars nobody has any relationship with. The galaxies are backdrop; the player's
        /// neighbourhood is the map.
        ///
        /// Yielded in a fixed order so two rebuilds always walk them identically.
        /// </summary>
        public static IEnumerable<SectorCoordinate> SectorsToDraw(
            IReadOnlyList<GalaxyDescriptor> galaxies, Vector3 shipAbsolute)
        {
            if (SceneryRadiusInSectors <= 0) yield break;
            SectorCoordinate centre = SectorCoordinate.FromAbsolutePosition(shipAbsolute);
            yield return centre;
            foreach (SectorCoordinate neighbour in centre.Neighbors26()) yield return neighbour;
        }

        /// <summary>
        /// A star known only by position and class. It carries no star radius, orbit or planet
        /// rows — not because they are gated, but because they have not been resolved: the
        /// moment the player focuses this system the map resolves its real manifest.
        /// </summary>
        private static StrategicMapUniverseSnapshot.SystemRow ToStarRow(
            SectorManifestGenerator.StarPlacement placement, IReadOnlyList<GalaxyDescriptor> galaxies,
            SystemKnowledgeLevel level) =>
            new StrategicMapUniverseSnapshot.SystemRow
            {
                id = placement.SystemId,
                name = placement.DisplayName,
                knowledge = (int)level,
                starClass = (int)placement.StarClass,
                starRadius = placement.StarRadius,
                galaxyId = NearestGalaxyId(placement.AbsoluteCenter, galaxies),
                x = placement.AbsoluteCenter.x / StrategicMapOrbits.MapUnit,
                y = placement.AbsoluteCenter.y / StrategicMapOrbits.MapUnit,
                z = placement.AbsoluteCenter.z / StrategicMapOrbits.MapUnit,
                preset = "",
                origin = "live",
                planetFrom = 0,
                planetTo = 0
            };

        private static StrategicMapUniverseSnapshot.SystemRow ToSystemRow(SystemManifest manifest,
            IReadOnlyList<GalaxyDescriptor> galaxies, SystemKnowledgeLevel level,
            StrategicMapUniverseSnapshot snapshot, float gravitationalConstant)
        {
            bool detailsKnown = level >= SystemKnowledgeLevel.Visited;
            float firstOrbit = 0f, outermostOrbit = 0f, maxInclination = 0f;
            int planetFrom = snapshot.planets.Count;

            if (detailsKnown)
            foreach (PlanetManifest pm in manifest.Planets)
            {
                {
                    snapshot.planets.Add(ToPlanetRow(manifest, pm, gravitationalConstant));
                    firstOrbit = firstOrbit <= 0f ? pm.OrbitRadius : Mathf.Min(firstOrbit, pm.OrbitRadius);
                    outermostOrbit = Mathf.Max(outermostOrbit, pm.OrbitRadius);
                    maxInclination = Mathf.Max(maxInclination, Mathf.Abs(pm.OrbitInclinationDegrees));
                }
            }

            return new StrategicMapUniverseSnapshot.SystemRow
            {
                id = manifest.SystemId,
                name = manifest.DisplayName,
                knowledge = (int)level,
                starClass = detailsKnown ? (int)manifest.StarClass : (int)StarClass.Yellow,
                galaxyId = NearestGalaxyId(manifest.AbsoluteCenter, galaxies),
                x = manifest.AbsoluteCenter.x / StrategicMapOrbits.MapUnit,
                y = manifest.AbsoluteCenter.y / StrategicMapOrbits.MapUnit,
                z = manifest.AbsoluteCenter.z / StrategicMapOrbits.MapUnit,
                starRadius = detailsKnown ? manifest.StarRadius : 0f,
                navigationRadius = detailsKnown ? manifest.NavigationRadius : 0f,
                planetCount = detailsKnown ? manifest.Planets.Count : 0,
                firstOrbitRadius = firstOrbit,
                outermostOrbit = outermostOrbit,
                maxInclination = maxInclination,
                preset = "",
                origin = "live",
                lightweight = false,
                asteroidCount = detailsKnown ? SystemMaterializer.AsteroidsPerSystem : 0,
                planetFrom = planetFrom,
                planetTo = snapshot.planets.Count
            };
        }

        /// <summary>Galaxy membership by smallest centre distance relative to each galaxy's radius — deterministic, and correct for halo systems between discs.</summary>
        public static string NearestGalaxyId(Vector3 absoluteCenter, IReadOnlyList<GalaxyDescriptor> galaxies)
        {
            if (galaxies == null || galaxies.Count == 0) return "";
            int best = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < galaxies.Count; i++)
            {
                float radius = Mathf.Max(galaxies[i].Radius, 1f);
                float score = Vector3.Distance(absoluteCenter, galaxies[i].Center) / radius;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return GalaxyId(galaxies[best].Name);
        }

        // ---------------------------------------------------------------- planets

        /// <summary>
        /// Calibrated so the presentation-only orbital pace follows Kepler's shape
        /// (speed ∝ orbit^-1.5), anchored to the retired reference's own Helios I sample
        /// (0.0601 rad/s at orbit 0.042).
        ///
        /// Kept only as the legacy <c>speed</c> column the converted HTML fixture also
        /// carries. The rate the map actually turns at is <c>orbitAngularSpeed</c> below,
        /// which is the world's own — a calibration against a retired prototype is not a
        /// simulation of this game.
        /// </summary>
        private const float KeplerCalibration = 0.000517f;

        private static StrategicMapUniverseSnapshot.PlanetRow ToPlanetRow(SystemManifest manifest, PlanetManifest pm,
            float gravitationalConstant)
        {
            // A galaxy star's planets have no runtime profile -- ExtractionState resolves
            // profiles through the sector registry, which only knows sector systems. Asking
            // it about one throws, so an unresolvable planet is simply reported unscanned.
            bool scanned = false;
            try
            {
                PlanetProfile profile = ExtractionState.GetProfile(pm.PlanetId);
                scanned = profile != null && profile.Scanned;
            }
            catch (System.InvalidOperationException) { }

            float orbitMapUnits = pm.OrbitRadius / StrategicMapOrbits.MapUnit;
            float authoritativePhase = pm.StartAngleDegrees * Mathf.Deg2Rad;
            bool phaseFrozen = true;
            double phaseEpochSeconds = 0d;

            // A materialized planet is the strongest authority: align the map to its actual
            // transform now, then advance from the same scaled simulation clock as gameplay.
            PlanetIdentity live = ExtractionState.FindPlanet(pm.PlanetId);
            if (live?.Body != null)
            {
                authoritativePhase = OrbitPhaseOf(manifest, pm,
                    live.Body.transform.position + FloatingOrigin.TotalOffset);
                phaseEpochSeconds = StrategicMapOrbitClock.Seconds;
                phaseFrozen = false;
            }
            else if (TryPlanetSnapshot(manifest.SystemId, pm.PlanetId, out PlanetSnapDto snap))
            {
                // Demoted-system positions are last-known snapshots. The remote domain does
                // not advance orbital transforms, so projecting them forward would invent a
                // precise position the authority itself does not possess.
                authoritativePhase = OrbitPhaseOf(manifest, pm, new Vector3(snap.px, snap.py, snap.pz));
            }

            var row = new StrategicMapUniverseSnapshot.PlanetRow
            {
                id = pm.PlanetId,
                worldId = pm.PlanetId,
                systemId = manifest.SystemId,
                name = StrategicMapDataSource.PlanetDisplayName(manifest, pm),
                radius = pm.PlanetRadius,
                orbit = orbitMapUnits,
                inclinationDegrees = pm.OrbitInclinationDegrees,
                ascendingNodeDegrees = pm.AscendingNodeDegrees,
                phase = authoritativePhase,
                phaseFrozen = phaseFrozen,
                phaseEpochSeconds = phaseEpochSeconds,

                // Presentation-only spin/tilt: the manifest defines none, so they are hashed
                // deterministically off the planet id — stable across sessions, never random.
                axialTilt = 30f * ExtractionCatalog.Unit01(ExtractionCatalog.Hash(pm.PlanetId + ":tilt")),
                spinSpeed = Mathf.Lerp(0.2f, 1.0f, ExtractionCatalog.Unit01(ExtractionCatalog.Hash(pm.PlanetId + ":spin"))),
                speed = KeplerCalibration / Mathf.Max(0.0001f, orbitMapUnits * Mathf.Sqrt(orbitMapUnits)),

                // The real thing: the angular rate the world itself launches this planet at,
                // from its own star's mass and its own orbit radius in WORLD units. The map
                // is a simulation of this system, so a lap of the map takes exactly as long
                // as a lap seen from the cockpit.
                orbitAngularSpeed = SystemMaterializer.OrbitalAngularSpeed(
                    gravitationalConstant, manifest.StarRadius, pm.OrbitRadius),
                materialIndex = (int)(ExtractionCatalog.Hash(pm.PlanetId + ":mat") % 24),
                trailHue = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(pm.PlanetId + ":hue")),

                resourceFamily = scanned ? pm.Family.ToString() : string.Empty,
                scanned = scanned,
                rarity = scanned ? pm.Rarity.ToString() : string.Empty,
                environment = scanned ? (int)pm.Environment : 0,
                t1Yield = scanned ? (int)ExtractionCatalog.T1EfficiencyPercent(pm.Rarity) : 0,
                productionPerMinute = scanned ? ExtractionCatalog.ProductionPerMinute(pm.Rarity, 1) : 0f,
                localStorage = scanned ? ExtractionCatalog.StorageCapacity(pm.Rarity, 1) : 0f,
                recommendedFirstSetup = scanned && pm.RecommendedStart
            };

            Vector3 systemCenterMap = manifest.AbsoluteCenter / StrategicMapOrbits.MapUnit;
            Vector3 initial = StrategicMapOrbits.OrbitPoint(systemCenterMap, row, row.phase, visualScale: 1f);
            row.initialX = initial.x; row.initialY = initial.y; row.initialZ = initial.z;
            return row;
        }

        private static bool TryPlanetSnapshot(string systemId, string planetId, out PlanetSnapDto found)
        {
            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            if (delta?.planetSnapshots != null)
                for (int i = 0; i < delta.planetSnapshots.Count; i++)
                    if (delta.planetSnapshots[i] != null && delta.planetSnapshots[i].worldId == planetId)
                    {
                        found = delta.planetSnapshots[i];
                        return true;
                    }
            found = null;
            return false;
        }

        /// <summary>Converts an authoritative absolute position back into this manifest orbit's phase.</summary>
        internal static float OrbitPhaseOf(SystemManifest system, PlanetManifest planet, Vector3 absolutePosition)
        {
            if (system == null || planet == null) return 0f;
            Quaternion plane = Quaternion.AngleAxis(planet.AscendingNodeDegrees, Vector3.up)
                               * Quaternion.AngleAxis(planet.OrbitInclinationDegrees, Vector3.right);
            Vector3 local = Quaternion.Inverse(plane) * (absolutePosition - system.AbsoluteCenter);
            return local.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(local.z, local.x)
                : planet.StartAngleDegrees * Mathf.Deg2Rad;
        }

        // ---------------------------------------------------------------- discoveries

        private static void BuildDiscoveryMarkers(SystemKnowledgeService knowledge,
            StrategicMapUniverseSnapshot snapshot)
        {
            SectorManifestRepository repository = StarSystemRegistry.Manifests;
            if (knowledge == null || repository == null || snapshot == null) return;

            for (int i = 0; i < snapshot.systems.Count; i++)
            {
                StrategicMapUniverseSnapshot.SystemRow system = snapshot.systems[i];
                if ((SystemKnowledgeLevel)system.knowledge < SystemKnowledgeLevel.Visited) continue;

                SystemManifest manifest = repository.ResolveSystem(system.id);
                if (manifest == null) continue;
                List<PoiManifest> manifests = PoiManifestRepository.Resolve(repository.Seed, manifest);
                SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(system.id);

                for (int p = 0; p < manifests.Count; p++)
                {
                    PoiManifest poi = manifests[p];
                    int level = PoiKnowledgeLevelOf(poi.PoiId, delta);
                    // A POI at Signal level is known to exist but has no earned position yet.
                    // Only Located+ may use the immutable manifest's exact local coordinate.
                    if (level < (int)PoiKnowledgeLevel.Located) continue;

                    Vector3 mapPosition = system.Position + poi.SystemLocalPosition / StrategicMapOrbits.MapUnit;
                    var row = new StrategicMapUniverseSnapshot.MarkerRow
                    {
                        id = poi.PoiId,
                        name = level >= (int)PoiKnowledgeLevel.Identified
                            ? PoiDisplayName(poi.Type)
                            : "Bilinmeyen keşif noktası",
                        systemId = system.id,
                        galaxyId = system.galaxyId,
                        knowledge = level,
                        type = level >= (int)PoiKnowledgeLevel.Identified ? (int)poi.Type : -1,
                        risk = level >= (int)PoiKnowledgeLevel.Identified ? (int)poi.Risk : -1,
                        x = mapPosition.x,
                        y = mapPosition.y,
                        z = mapPosition.z
                    };

                    if (level < (int)PoiKnowledgeLevel.Identified)
                        snapshot.discoveries.Add(row);
                    else if (poi.Type == PoiType.Anomaly)
                        snapshot.anomalies.Add(row);
                    else
                        snapshot.derelicts.Add(row);
                }
            }
        }

        private static int PoiKnowledgeLevelOf(string poiId, SystemDelta delta)
        {
            PoiState live = PoiLiveState.Find(poiId);
            if (live != null) return (int)live.Level;
            if (delta != null)
                for (int i = 0; i < delta.pois.Count; i++)
                    if (delta.pois[i] != null && delta.pois[i].poiId == poiId)
                        return delta.pois[i].level;
            return (int)PoiKnowledgeLevel.Unknown;
        }

        private static string PoiDisplayName(PoiType type) => type switch
        {
            PoiType.Anomaly => "Anomali",
            PoiType.Wreck => "Enkaz",
            PoiType.AbandonedFacility => "Terk edilmiş tesis",
            _ => "Keşif noktası"
        };
    }
}
