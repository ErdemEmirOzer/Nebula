using System.Collections.Generic;
using UnityEngine;
using Nebula.Visuals;

namespace Nebula.World
{
    /// <summary>
    /// Visual/exploration points of interest: derelict wrecks and strange
    /// pulsing glows adrift between the galaxy layers. They register in the
    /// <see cref="LocationRegistry"/> so the HUD names them; they carry no
    /// gameplay systems.
    /// </summary>
    public static class PointOfInterestGenerator
    {
        /// <summary>
        /// Scatters the decor wrecks and anomalies and hands back every root it
        /// created, so the session that asked for them can take them down again.
        /// <para>
        /// This used to return nothing, and each wreck/anomaly is its own scene
        /// root: a session that ended left all of them adrift, and the next one
        /// scattered a fresh set beside them. Returning the roots rather than
        /// re-parenting them keeps their world positions, their floating-origin
        /// behaviour and their <see cref="LocationRegistry"/> names exactly as
        /// they were — the only thing that changes is that somebody now owns them.
        /// </para>
        /// </summary>
        public static List<GameObject> GenerateAnomalies(IReadOnlyList<GalaxyMap> galaxies, int count)
        {
            var created = new List<GameObject>();
            if (galaxies == null || galaxies.Count == 0 || count <= 0) return created;

            Vector3 centroid = Vector3.zero;
            int hostCount = 0;
            foreach (GalaxyMap map in galaxies)
            {
                if (map == null) continue;
                centroid += map.transform.position;
                hostCount++;
            }
            if (hostCount == 0) return created;
            centroid /= hostCount;

            float groupRadius = 0f;
            foreach (GalaxyMap map in galaxies)
            {
                if (map == null) continue;
                groupRadius = Mathf.Max(groupRadius,
                    Vector3.Distance(centroid, map.transform.position) + map.Radius * 1.6f);
            }

            // One shared dark hull material: wrecks are unlit silhouettes
            // against the sky (no star light reaches intergalactic space).
            Material hullMaterial = MaterialLibrary.CreateUnlit(new Color(0.09f, 0.1f, 0.12f));

            int wreckIndex = 0;
            int anomalyIndex = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryFindDeepSpaceSpot(galaxies, centroid, groupRadius, out Vector3 world)) continue;

                if (i % 2 == 0)
                {
                    created.Add(CreateWreck(world, ++wreckIndex, hullMaterial));
                }
                else
                {
                    created.Add(CreateAnomaly(world, ++anomalyIndex));
                }
            }

            return created;
        }

        /// <summary>Random spot in the group volume, outside every galaxy's disc-plus-halo bubble.</summary>
        private static bool TryFindDeepSpaceSpot(IReadOnlyList<GalaxyMap> galaxies, Vector3 centroid,
            float groupRadius, out Vector3 world)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                world = centroid + Random.insideUnitSphere * (groupRadius * 0.8f);

                bool clear = true;
                foreach (GalaxyMap map in galaxies)
                {
                    if (map == null) continue;
                    if (Vector3.Distance(world, map.transform.position) < map.Radius * 1.35f)
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear) return true;
            }
            world = Vector3.zero;
            return false;
        }

        /// <summary>Tumbling cluster of dark hull pieces with a pulsing red beacon.</summary>
        private static GameObject CreateWreck(Vector3 position, int index, Material hullMaterial)
        {
            string wreckName = $"Derelict {index:00}";
            var root = new GameObject(wreckName);
            root.transform.position = position;
            var tumble = root.AddComponent<RotateSelf>();
            tumble.axis = Random.onUnitSphere;
            tumble.degreesPerSecond = Random.Range(0.3f, 1.5f);

            int pieceCount = Random.Range(5, 9);
            for (int i = 0; i < pieceCount; i++)
            {
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = "Hull Piece " + i;
                Object.Destroy(piece.GetComponent<Collider>());
                piece.transform.SetParent(root.transform, false);
                piece.transform.localPosition = Random.insideUnitSphere * 90f;
                piece.transform.localRotation = Random.rotation;
                piece.transform.localScale = new Vector3(
                    Random.Range(25f, 110f), Random.Range(8f, 28f), Random.Range(8f, 28f));

                var pieceRenderer = piece.GetComponent<MeshRenderer>();
                pieceRenderer.sharedMaterial = hullMaterial;
                pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pieceRenderer.receiveShadows = false;
            }

            // Wide faint marker so the site can be spotted from afar, plus the
            // tight blinking distress beacon.
            CreateGlowQuad(root.transform, "Marker Glow", Vector3.zero, 4000f,
                new Color(0.6f, 0.75f, 1f, 0.06f), null);
            GameObject beacon = CreateGlowQuad(root.transform, "Beacon", Vector3.zero, 140f,
                new Color(1f, 0.25f, 0.15f, 0.5f), null);
            beacon.AddComponent<BeaconPulse>().Configure(1.6f, 0.25f);

            LocationRegistry.RegisterStar(wreckName, root.transform, 50000f);
            return root;
        }

        /// <summary>Layered eerie glow that slowly breathes — clearly not a star.</summary>
        private static GameObject CreateAnomaly(Vector3 position, int index)
        {
            string anomalyName = $"Anomaly {index:00}";
            var root = new GameObject(anomalyName);
            root.transform.position = position;

            CreateGlowQuad(root.transform, "Outer Glow", Vector3.zero, Random.Range(2200f, 3200f),
                new Color(0.35f, 1f, 0.6f, 0.1f), null);
            GameObject mid = CreateGlowQuad(root.transform, "Mid Glow", Vector3.zero, Random.Range(1000f, 1500f),
                new Color(0.7f, 0.4f, 1f, 0.16f), null);
            mid.AddComponent<BeaconPulse>().Configure(Random.Range(2.5f, 4f), 0.6f);
            GameObject core = CreateGlowQuad(root.transform, "Core Glow", Vector3.zero, Random.Range(350f, 550f),
                new Color(0.9f, 1f, 0.95f, 0.3f), null);
            core.AddComponent<BeaconPulse>().Configure(Random.Range(1.2f, 2f), 0.45f);

            LocationRegistry.RegisterStar(anomalyName, root.transform, 50000f);
            return root;
        }

        private static GameObject CreateGlowQuad(Transform parent, string quadName, Vector3 localPosition,
            float size, Color tint, Texture2D texture)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = quadName;
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localScale = Vector3.one * size;
            quad.AddComponent<Billboard>();

            var quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(tint,
                texture != null ? texture : MaterialLibrary.RadialGlowTexture);
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            return quad;
        }
    }
}
