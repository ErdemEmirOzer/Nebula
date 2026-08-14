using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;

namespace Nebula.Extraction
{
    /// <summary>
    /// Tags a planet with its deterministic world ID and keeps a static
    /// registry so the extraction systems can resolve planets by ID across a
    /// save/load (the universe is rebuilt from the same seed, this component
    /// re-registers on Awake).
    /// </summary>
    public class PlanetIdentity : MonoBehaviour
    {
        public string WorldId { get; private set; }
        public CelestialBody Body { get; private set; }
        public bool IsStartSystem { get; private set; }

        public void Initialize(string worldId, CelestialBody body, bool isStartSystem)
        {
            WorldId = worldId;
            Body = body;
            IsStartSystem = isStartSystem;
            ExtractionState.RegisterPlanet(this);
        }

        private void OnDestroy() => ExtractionState.UnregisterPlanet(this);
    }

    /// <summary>
    /// Central runtime state for the extraction slice: the universe seed, the
    /// lazy planet-profile cache, the planet registry, the build-kit state and
    /// the active drills. Reset by the bootstrap on every scene start so
    /// disabled domain reloads never leak a previous run into a new one.
    /// </summary>
    public static class ExtractionState
    {
        public enum KitState { Available, Reserved, Consumed }

        public static ulong UniverseSeed { get; private set; }
        public static KitState Kit { get; private set; } = KitState.Available;

        private static readonly Dictionary<string, PlanetProfile> profiles = new Dictionary<string, PlanetProfile>();
        private static readonly Dictionary<string, PlanetIdentity> planets = new Dictionary<string, PlanetIdentity>();
        private static readonly List<DrillStructure> drills = new List<DrillStructure>();
        private static readonly List<SurfaceStructure> structures = new List<SurfaceStructure>();
        private static readonly Dictionary<string, PlanetDominance> dominance = new Dictionary<string, PlanetDominance>();
        private static readonly HashSet<string> evacuationLocked = new HashSet<string>();

        public static IReadOnlyList<DrillStructure> Drills => drills;
        public static IReadOnlyList<SurfaceStructure> Structures => structures;

        public static void Reset(int seed)
        {
            UniverseSeed = unchecked((ulong)seed);
            Kit = KitState.Available;
            profiles.Clear();
            planets.Clear();
            drills.Clear();
            structures.Clear();
            dominance.Clear();
            evacuationLocked.Clear();
        }

        /// <summary>VS-010 §5 step 1: while an evacuation transaction is pending on a planet, no new capsule/job may be submitted there (manual build or Quick Asset Package).</summary>
        public static void SetEvacuationLocked(string worldId, bool locked)
        {
            if (string.IsNullOrEmpty(worldId)) return;
            if (locked) evacuationLocked.Add(worldId); else evacuationLocked.Remove(worldId);
        }

        public static bool IsEvacuationLocked(string worldId) => worldId != null && evacuationLocked.Contains(worldId);

        public static void RegisterPlanet(PlanetIdentity planet)
        {
            if (planet != null && !string.IsNullOrEmpty(planet.WorldId)) planets[planet.WorldId] = planet;
        }

        public static void UnregisterPlanet(PlanetIdentity planet)
        {
            if (planet != null && planet.WorldId != null && planets.TryGetValue(planet.WorldId, out var found)
                && found == planet)
            {
                planets.Remove(planet.WorldId);
            }
        }

        public static PlanetIdentity FindPlanet(string worldId)
        {
            return worldId != null && planets.TryGetValue(worldId, out var p) ? p : null;
        }

        /// <summary>Every registered planet (used by the Hangar to list same-system Scout targets, VS-007 §4).</summary>
        public static IEnumerable<PlanetIdentity> AllPlanets => planets.Values;

        /// <summary>Lazily builds and caches the profile for a planet (deterministic; VS-008 §7: family/rarity come from its manifest, never re-hashed).</summary>
        public static PlanetProfile GetProfile(PlanetIdentity planet)
        {
            if (planet == null) return null;
            if (!profiles.TryGetValue(planet.WorldId, out PlanetProfile profile))
            {
                profile = BuildProfile(planet.WorldId);
                if (planet.IsStartSystem) profile.RecommendedStart = true;
                profiles[planet.WorldId] = profile;
            }
            return profile;
        }

        /// <summary>
        /// Resolves (and caches) a profile from its manifest even when the
        /// planet has no live PlanetIdentity right now (its system is Warm
        /// or fully demoted) — the distant tick needs this to read a
        /// drill's production/capacity numbers without materializing anything.
        /// VS-009 §1: there is no hash-based fallback — a runtime planet
        /// profile always comes from a PlanetManifest, or this is a bug.
        /// </summary>
        private static PlanetProfile BuildProfile(string worldId)
        {
            string systemId = Sectors.WorldIdCodec.SystemIdFromPlanetWorldId(worldId);
            Sectors.SystemManifest system = systemId != null ? World.StarSystemRegistry.Manifests?.ResolveSystem(systemId) : null;
            if (system != null && Sectors.WorldIdCodec.TryParsePlanetIndex(worldId, out int index))
            {
                Sectors.PlanetManifest planetManifest = system.FindPlanet(index);
                if (planetManifest != null) return new PlanetProfile(planetManifest);
            }
            throw new System.InvalidOperationException(
                $"No PlanetManifest resolves for world id '{worldId}' — every runtime planet profile must come from a manifest.");
        }

        public static PlanetProfile GetProfile(string worldId)
        {
            if (worldId == null) return null;
            if (profiles.TryGetValue(worldId, out PlanetProfile cached)) return cached;
            var planet = FindPlanet(worldId);
            if (planet != null) return GetProfile(planet);
            PlanetProfile profile = BuildProfile(worldId);
            profiles[worldId] = profile;
            return profile;
        }

        // --- Build kit -------------------------------------------------------
        public static bool ReserveKit()
        {
            if (Kit != KitState.Available) return false;
            Kit = KitState.Reserved;
            return true;
        }

        public static void ConsumeKit() => Kit = KitState.Consumed;
        public static void ReturnKit() { if (Kit == KitState.Reserved) Kit = KitState.Available; }
        public static void ForceKitState(KitState state) => Kit = state;

        // --- Drill registry --------------------------------------------------
        public static void RegisterDrill(DrillStructure drill) { if (!drills.Contains(drill)) drills.Add(drill); }
        public static void UnregisterDrill(DrillStructure drill) => drills.Remove(drill);

        // --- Save/load helpers ----------------------------------------------
        public static IEnumerable<PlanetProfile> CachedProfiles => profiles.Values;

        public static void MarkScanned(string worldId)
        {
            var profile = GetProfile(worldId);
            if (profile != null) profile.Scanned = true;
        }

        public static void DestroyAllDrills()
        {
            var copy = new List<DrillStructure>(drills);
            for (int i = 0; i < copy.Count; i++) if (copy[i] != null) Object.Destroy(copy[i].gameObject);
            drills.Clear();
        }

        public static void DestroyAllStructures()
        {
            var copy = new List<SurfaceStructure>(structures);
            for (int i = 0; i < copy.Count; i++) if (copy[i] != null) Object.Destroy(copy[i].gameObject);
            structures.Clear();
        }

        // --- Surface structure registry & dominance queries ------------------
        public static void RegisterStructure(SurfaceStructure s) { if (!structures.Contains(s)) structures.Add(s); }
        public static void UnregisterStructure(SurfaceStructure s) => structures.Remove(s);

        public static bool IsNodeOccupied(string worldId, string nodeId)
        {
            for (int i = 0; i < structures.Count; i++)
                if (structures[i] != null && structures[i].WorldId == worldId && structures[i].NodeId == nodeId) return true;
            for (int i = 0; i < drills.Count; i++)
                if (drills[i] != null && drills[i].WorldId == worldId && drills[i].NodeId == nodeId) return true;
            return false;
        }

        public static bool HasStructureType(string worldId, SurfaceStructureType type)
        {
            for (int i = 0; i < structures.Count; i++)
                if (structures[i] != null && structures[i].WorldId == worldId && structures[i].Type == type) return true;
            return false;
        }

        public static SurfaceStructure FindStructure(string worldId, SurfaceStructureType type)
        {
            for (int i = 0; i < structures.Count; i++)
                if (structures[i] != null && structures[i].WorldId == worldId && structures[i].Type == type) return structures[i];
            return null;
        }

        /// <summary>VS-010 §5: an Abandoned planet's structures stay physically intact (never destroyed) but read as inactive everywhere outside — "pasifleştir" without deleting anything.</summary>
        public static bool IsPlanetAbandoned(string worldId) =>
            dominance.TryGetValue(worldId, out PlanetDominance d) && d.Abandoned;

        public static bool HasActiveCommand(string worldId)
        {
            if (IsPlanetAbandoned(worldId)) return false;
            var s = FindStructure(worldId, SurfaceStructureType.Command);
            return s != null && s.Runtime.IsActive;
        }

        public static bool CommandDisabled(string worldId)
        {
            var s = FindStructure(worldId, SurfaceStructureType.Command);
            return s != null && s.Runtime.IsDisabled;
        }

        /// <summary>VS-014: counts every ACTIVE real defense weapon structure (Laser or Plasma) — was hardcoded to Laser only before Plasma existed, which would have silently under-counted a Plasma-only planet's real defense for dominance purposes.</summary>
        public static int ActiveDefenseCount(string worldId)
        {
            if (IsPlanetAbandoned(worldId)) return 0;
            int count = 0;
            for (int i = 0; i < structures.Count; i++)
                if (structures[i] != null && structures[i].WorldId == worldId
                    && SurfaceStructureCatalog.IsActiveDefenseWeapon(structures[i].Type) && structures[i].Runtime.IsActive) count++;
            return count;
        }

        /// <summary>
        /// True when this planet carries an active drill (and has not been abandoned).
        /// <para>
        /// Was <c>HasActiveMatchingDrill(worldId, family)</c>. The family argument was
        /// compared against <c>DrillRuntime.Family</c>, which is assigned from that
        /// same planet's profile in both constructors, so the comparison could not
        /// fail and the caller's only reachable argument was the value it was compared
        /// to (P1-ECO-019). Dropping the parameter removes a filter that filtered
        /// nothing rather than leaving a check that reads as though it decides
        /// something.
        /// </para>
        /// </summary>
        public static bool HasActiveDrill(string worldId)
        {
            if (IsPlanetAbandoned(worldId)) return false;
            for (int i = 0; i < drills.Count; i++)
                if (drills[i] != null && drills[i].WorldId == worldId
                    && drills[i].Runtime.State == DrillLifecycle.Active) return true;
            return false;
        }

        // --- Dominance -------------------------------------------------------
        public static PlanetDominance GetDominance(string worldId)
        {
            if (!dominance.TryGetValue(worldId, out PlanetDominance d))
            {
                d = new PlanetDominance();
                dominance[worldId] = d;
            }
            return d;
        }

        public static IEnumerable<KeyValuePair<string, PlanetDominance>> AllDominance => dominance;

        /// <summary>True once at least one planet has reached Controlled — the Hangar-Bot T1 milestone gate (VS-007 §1).</summary>
        public static bool HasAnyControlledPlanet()
        {
            foreach (var kv in dominance)
                if (kv.Value.State == DominanceState.Controlled) return true;
            return false;
        }

        /// <summary>
        /// Single idempotent authority for "a planet's scan finished": marks
        /// the profile Scanned and awards the Scan + Family XP exactly once
        /// (the XP ledger already guards duplicates, but every caller — the
        /// ship's own R-scan and a Scout vehicle's remote scan, VS-007 §4 —
        /// must route through here instead of re-inlining the award calls).
        /// Returns false with no side effects if the planet was already scanned.
        /// </summary>
        public static bool CompletePlanetScan(PlanetIdentity planet)
        {
            if (planet == null) return false;
            PlanetProfile profile = GetProfile(planet);
            if (profile == null || profile.Scanned) return false;

            profile.Scanned = true;
            Progression.ProgressionService.Award(
                Progression.ProgressionEvents.Scan(planet.WorldId), Progression.ProgressionEvents.ScanXp);
            Progression.ProgressionService.Award(
                Progression.ProgressionEvents.Family(ExtractionCatalog.ResourceIds[(int)profile.Family]),
                Progression.ProgressionEvents.FamilyXp);
            return true;
        }
    }
}
