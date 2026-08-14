using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Extraction
{
    /// <summary>
    /// Ticks per-planet dominance for every planet that carries a drill or
    /// structure: evaluates the four control conditions, advances the state
    /// machine in real time, and suspends drill production on Contested/Lost
    /// planets. One central component — no per-planet Update.
    /// </summary>
    public class DominanceSystem : MonoBehaviour
    {
        /// <summary>Raised the first time (ever) a given planet reaches Controlled — a restored-Controlled planet is seeded, never raised. Used by VS-006's navigation signal reveal.</summary>
        public static event System.Action<string> PlanetFirstControlled;

        /// <summary>TF-002: clears any subscriber left over from a prior campaign session — defense in depth alongside NavigationDirector's own OnDisable unsubscribe.</summary>
        public static void ResetForSession() => PlanetFirstControlled = null;

        private readonly HashSet<string> worldIds = new HashSet<string>();
        private readonly Dictionary<string, DominanceState> prevState = new Dictionary<string, DominanceState>();

        private void Update()
        {
            float dt = Time.deltaTime;
            worldIds.Clear();
            foreach (DrillStructure d in ExtractionState.Drills) if (d != null) worldIds.Add(d.WorldId);
            foreach (SurfaceStructure s in ExtractionState.Structures) if (s != null) worldIds.Add(s.WorldId);

            foreach (string worldId in worldIds)
            {
                PlanetIdentity planet = ExtractionState.FindPlanet(worldId);
                if (planet == null) continue;
                PlanetProfile profile = ExtractionState.GetProfile(planet);

                var cond = new DominanceConditions
                {
                    Scanned = profile.Scanned,
                    ActiveDrill = ExtractionState.HasActiveDrill(worldId),
                    ActiveCommand = ExtractionState.HasActiveCommand(worldId),
                    ActiveDefense = ExtractionState.ActiveDefenseCount(worldId) > 0,
                    CommandDisabled = ExtractionState.CommandDisabled(worldId)
                };

                PlanetDominance dom = ExtractionState.GetDominance(worldId);
                DominanceState before = prevState.TryGetValue(worldId, out var s) ? s : dom.State;
                bool firstObservation = !prevState.ContainsKey(worldId);
                dom.Tick(dt, cond);
                prevState[worldId] = dom.State;

                // XP: first time this planet reaches Controlled (transition only;
                // a restored Controlled planet is seeded, never awarded).
                if (!firstObservation && before != DominanceState.Controlled && dom.State == DominanceState.Controlled)
                {
                    Progression.ProgressionService.Award(
                        Progression.ProgressionEvents.DominanceFirst(worldId), Progression.ProgressionEvents.DominanceXp);
                    PlanetFirstControlled?.Invoke(worldId);
                }

                bool suspend = dom.ProductionSuspended;
                foreach (DrillStructure drill in ExtractionState.Drills)
                    if (drill != null && drill.WorldId == worldId) drill.Runtime.ProductionSuspended = suspend;
            }
        }
    }
}
