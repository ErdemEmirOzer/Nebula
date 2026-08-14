using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Poi
{
    /// <summary>
    /// Pure, deterministic per-system POI generation (VS-012 §1/§2). A
    /// system's manifest is never persisted directly — like every other
    /// generation-time structure in this project it is reproducible from
    /// (seed, systemId) alone, EXCEPT for the two visit-order guarantees,
    /// which are a pure function of (seed, systemId, the already-visited
    /// systems recorded in <see cref="PoiVisitLedger"/>). Because the ledger
    /// only ever grows and a system's own guarantee decision depends only on
    /// entries strictly before it, re-running <see cref="Generate"/> against
    /// a stable ledger always reproduces the exact same result — "frozen"
    /// falls out of determinism, no separate persisted manifest is needed
    /// (see <see cref="PoiManifestRepository"/> for the caching layer this
    /// still benefits from for performance).
    /// </summary>
    public static class PoiManifestGenerator
    {
        // Not specified anywhere in the design brief — reasonable, documented assumptions (see delivery report).
        private const float DangerClearanceMultiplier = 1.15f; // beyond the star's Warning-radius boundary
        private const float PlanetClearanceMultiplier = 4f;    // multiple of each planet's own radius
        private const float MinPoiSeparation = 6000f;
        private const int MaxPlacementAttempts = 24;

        public static List<PoiManifest> Generate(ulong seed, SystemManifest system, PoiVisitLedger ledger)
        {
            if (system == null || system.IsStartSystem) return new List<PoiManifest>();

            List<PoiManifest> natural = GenerateNatural(seed, system);
            int ordinal = ledger?.OrdinalOf(system.SystemId) ?? -1;
            if (ordinal < 0) return natural; // not yet visited -- no guarantee decided yet

            List<PoiManifest> result = natural;
            if (ordinal == 0) result = EnsureSafeWreck(seed, system, result);

            if (ordinal % 3 == 2)
            {
                int groupStart = ordinal - 2;
                List<string> group = ledger.GroupMembers(ordinal);
                bool groupHasPoi = result.Count > 0;
                for (int i = 0; i < group.Count - 1 && !groupHasPoi; i++)
                {
                    int memberOrdinal = groupStart + i;
                    if (memberOrdinal == 0) { groupHasPoi = true; break; } // ordinal 0 is always guaranteed >=1 via its own rule
                    SystemManifest memberManifest = World.StarSystemRegistry.Manifests?.ResolveSystem(group[i]);
                    if (memberManifest != null && GenerateNatural(seed, memberManifest).Count > 0) groupHasPoi = true;
                }
                if (!groupHasPoi) result = EnsureAnyPoi(seed, system);
            }

            return result;
        }

        // --- Natural (no guarantee) generation ---------------------------------------
        private static List<PoiManifest> GenerateNatural(ulong seed, SystemManifest system)
        {
            var list = new List<PoiManifest>();
            float countRoll = Unit(seed, system.SystemId, "poi/count") * 100f;
            int count = countRoll < PoiCatalog.CountZeroWeight * 100f ? 0
                : countRoll < (PoiCatalog.CountZeroWeight + PoiCatalog.CountOneWeight) * 100f ? 1 : 2;
            count = Mathf.Min(count, PoiCatalog.MaxPoiPerSystem);

            for (int i = 0; i < count; i++)
            {
                PoiType type = PickType(seed, system.SystemId, "poi/" + i);
                PoiRisk risk = PickRisk(seed, system.SystemId, type, "poi/" + i);
                PoiManifest poi = BuildOne(seed, system, i, type, risk, list);
                list.Add(poi);
            }
            return list;
        }

        private static List<PoiManifest> EnsureSafeWreck(ulong seed, SystemManifest system, List<PoiManifest> list)
        {
            foreach (PoiManifest p in list) if (p.Type == PoiType.Wreck && p.Risk == PoiRisk.Safe) return list;

            var copy = new List<PoiManifest>(list);
            bool appending = copy.Count < PoiCatalog.MaxPoiPerSystem;
            int slot = appending ? copy.Count : 0;
            // Collision-check against whatever POI(s) survive alongside the forced one (excludes the slot being overwritten).
            var survivors = new List<PoiManifest>();
            for (int i = 0; i < copy.Count; i++) if (i != slot) survivors.Add(copy[i]);
            PoiManifest forced = BuildOne(seed, system, slot, PoiType.Wreck, PoiRisk.Safe, survivors, "guarantee/firstvisit");
            if (appending) copy.Add(forced); else copy[0] = forced; // never exceed the 2-POI cap
            return copy;
        }

        private static List<PoiManifest> EnsureAnyPoi(ulong seed, SystemManifest system)
        {
            PoiType type = PickType(seed, system.SystemId, "guarantee/group");
            PoiRisk risk = PickRisk(seed, system.SystemId, type, "guarantee/group");
            return new List<PoiManifest> { BuildOne(seed, system, 0, type, risk, new List<PoiManifest>(), "guarantee/group") };
        }

        private static PoiManifest BuildOne(ulong seed, SystemManifest system, int index, PoiType type, PoiRisk risk,
            List<PoiManifest> alreadyPlaced, string tagOverride = null)
        {
            string tag = tagOverride ?? ("poi/" + index);
            string poiId = WorldIdCodec.PoiId(system.SystemId, index);
            WreckSize size = type == PoiType.Wreck ? PickWreckSize(seed, system.SystemId, tag) : WreckSize.Small;
            ulong rewardSeed = ExtractionCatalog.Hash(seed, system.SystemId + "/" + tag + "/reward");
            ulong visualSeed = ExtractionCatalog.Hash(seed, system.SystemId + "/" + tag + "/visual");
            Vector3 position = PickPosition(seed, system, tag, alreadyPlaced);
            return new PoiManifest(poiId, system.SystemId, index, type, risk, position, size, rewardSeed, visualSeed);
        }

        private static PoiType PickType(ulong seed, string systemId, string tag)
        {
            float totalWeight = 0f;
            foreach (var (_, weight) in PoiCatalog.TypeWeights) totalWeight += weight;
            float roll = Unit(seed, systemId, tag + "/type") * totalWeight;
            float cumulative = 0f;
            foreach (var (type, weight) in PoiCatalog.TypeWeights)
            {
                cumulative += weight;
                if (roll < cumulative) return type;
            }
            return PoiCatalog.TypeWeights[PoiCatalog.TypeWeights.Length - 1].type;
        }

        private static PoiRisk PickRisk(ulong seed, string systemId, PoiType type, string tag)
        {
            (float safeWeight, float unstableWeight) = type switch
            {
                PoiType.Anomaly => (PoiCatalog.AnomalySafeWeight, PoiCatalog.AnomalyUnstableWeight),
                PoiType.Wreck => (PoiCatalog.WreckSafeWeight, PoiCatalog.WreckUnstableWeight),
                _ => (PoiCatalog.FacilitySafeWeight, PoiCatalog.FacilityUnstableWeight)
            };
            float roll = Unit(seed, systemId, tag + "/risk") * (safeWeight + unstableWeight);
            return roll < safeWeight ? PoiRisk.Safe : PoiRisk.Unstable;
        }

        private static WreckSize PickWreckSize(ulong seed, string systemId, string tag)
        {
            float roll = Unit(seed, systemId, tag + "/size") * 3f;
            return roll < 1f ? WreckSize.Small : roll < 2f ? WreckSize.Medium : WreckSize.Large;
        }

        // --- Deterministic, safety-validated system-local position ------------------
        private static Vector3 PickPosition(ulong seed, SystemManifest system, string tag, List<PoiManifest> alreadyPlaced)
        {
            float dangerRadius = system.StarRadius * StarClassCatalog.WarningRadiusMultiplier(system.StarClass) * DangerClearanceMultiplier;
            float outerBound = Mathf.Max(dangerRadius * 1.5f, system.NavigationRadius * 0.9f);

            float baseAngle = Unit(seed, system.SystemId, tag + "/angle") * 360f;
            float baseRadiusUnit = Unit(seed, system.SystemId, tag + "/radius");
            float baseHeightUnit = Unit(seed, system.SystemId, tag + "/height");

            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                // Deterministic bounded retry sequence: golden-angle rotation + growing radius jitter per attempt -- never a re-roll of the base hash.
                float angle = baseAngle + attempt * 137.507f;
                float radius = Mathf.Lerp(dangerRadius, outerBound, Mathf.Repeat(baseRadiusUnit + attempt * 0.061803f, 1f));
                float height = Mathf.Lerp(-0.15f, 0.15f, baseHeightUnit) * radius;
                float rad = angle * Mathf.Deg2Rad;
                var candidate = new Vector3(Mathf.Cos(rad) * radius, height, Mathf.Sin(rad) * radius);

                if (IsPositionSafe(candidate, system, alreadyPlaced)) return candidate;
            }

            // Exhausted the deterministic retry budget (astronomically unlikely given system scale) -- a safe deterministic fallback that is always clear.
            return new Vector3(outerBound, 0f, 0f);
        }

        private static bool IsPositionSafe(Vector3 candidate, SystemManifest system, List<PoiManifest> alreadyPlaced)
        {
            float dangerRadius = system.StarRadius * StarClassCatalog.WarningRadiusMultiplier(system.StarClass) * DangerClearanceMultiplier;
            if (candidate.magnitude < dangerRadius) return false;

            foreach (PlanetManifest planet in system.Planets)
            {
                Vector3 planetLocalPos = PlanetLocalPosition(planet);
                float clearance = planet.PlanetRadius * PlanetClearanceMultiplier;
                if ((candidate - planetLocalPos).sqrMagnitude < clearance * clearance) return false;
            }

            foreach (PoiManifest other in alreadyPlaced)
                if ((candidate - other.SystemLocalPosition).sqrMagnitude < MinPoiSeparation * MinPoiSeparation) return false;

            return true;
        }

        /// <summary>Same orbit-plane math SystemMaterializer uses for a planet's starting position — an approximate, static exclusion point (planets keep orbiting; POIs don't chase them).</summary>
        private static Vector3 PlanetLocalPosition(PlanetManifest p)
        {
            Quaternion plane = Quaternion.AngleAxis(p.AscendingNodeDegrees, Vector3.up) * Quaternion.AngleAxis(p.OrbitInclinationDegrees, Vector3.right);
            Vector3 localPos = new Vector3(Mathf.Cos(p.StartAngleDegrees * Mathf.Deg2Rad), 0f, Mathf.Sin(p.StartAngleDegrees * Mathf.Deg2Rad)) * p.OrbitRadius;
            return plane * localPos;
        }

        private static float Unit(ulong seed, string id, string tag) => ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, id + "/" + tag));
    }
}
