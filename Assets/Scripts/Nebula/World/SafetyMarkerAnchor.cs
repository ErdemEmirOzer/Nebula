using UnityEngine;
using Nebula.Visuals;

namespace Nebula.World
{
    /// <summary>
    /// Single physical placeholder marking the campaign's Safety Marker
    /// (VS-013.2 §2) — the one, shared, deterministic position authority
    /// consumed by ShipCombat's physical repair safe-zone check,
    /// ShipEmergencyRecovery's final defeat-recovery fallback, and
    /// TrainingArea's placement. Never the star/system center: its position
    /// is derived by the exact same formula PlayerInstaller.CreateShip
    /// already uses for the ship's own fresh-campaign spawn point, which is
    /// itself proof the point is outside the star's Warning/Critical
    /// stellar-risk radius (every fresh campaign already starts a player
    /// there safely). Pure function of the start SystemManifest alone, so
    /// it is trivially deterministic and reproducible on load without ever
    /// being written to the save file.
    /// </summary>
    public class SafetyMarkerAnchor : MonoBehaviour
    {
        public static SafetyMarkerAnchor Create(Sectors.SystemManifest startSystem)
        {
            var go = new GameObject("Safety Marker Anchor");
            go.transform.position = ComputePosition(startSystem);
            var anchor = go.AddComponent<SafetyMarkerAnchor>();
            BuildVisual(go);
            return anchor;
        }

        /// <summary>
        /// Same formula as PlayerInstaller.CreateShip's fresh-campaign ship
        /// spawn (0.85x the first planet's orbit radius out from the star,
        /// with a small vertical offset) — deliberately NOT re-derived
        /// independently, so the Safety Marker always sits exactly where a
        /// new campaign's ship itself first appears.
        /// </summary>
        public static Vector3 ComputePosition(Sectors.SystemManifest startSystem)
        {
            float firstOrbit = startSystem.Planets.Count > 0 ? startSystem.Planets[0].OrbitRadius : 40000f;
            return startSystem.AbsoluteCenter + new Vector3(0f, firstOrbit * 0.15f, -firstOrbit * 0.85f);
        }

        private static void BuildVisual(GameObject go)
        {
            if (NebulaVisualResolver.Instantiate(NebulaVisualCatalog.SafetyMarker, go.transform,
                    instanceName: "Authored Safety Marker") != null)
                return;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Safety Marker Beacon (Fallback)";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * 60f;
            if (Application.isPlaying) Destroy(body.GetComponent<Collider>()); else DestroyImmediate(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().sharedMaterial = Visuals.MaterialLibrary.CreateAdditive(new Color(0.35f, 0.95f, 1f, 0.5f));
        }
    }
}
