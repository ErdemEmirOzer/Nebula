using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nebula.Visuals;

namespace Nebula.World
{
    /// <summary>
    /// Fills the space between galaxies so long crossings don't feel empty:
    /// a sparse spherical halo of old stars around every galaxy, a few
    /// globular clusters seeded in each halo, and a very thin sprinkle of
    /// "bridge" stars across the whole galaxy group. Everything is baked into
    /// two static meshes (halo + bridge, clusters) — two draw calls total.
    /// Unlike the baked skybox these are real geometry, so they parallax and
    /// sell the sense of motion on intergalactic trips.
    /// </summary>
    public static class IntergalacticHaloGenerator
    {
        private const int ClusterStarCount = 200;

        private struct Host
        {
            public Vector3 center;
            public float radius;
            public int haloStars;
            public int clusters;
        }

        public static GameObject Generate(IReadOnlyList<GalaxyMap> galaxies, int haloStarsPerGalaxy,
            int clustersPerGalaxy, int bridgeStarCount)
        {
            if (galaxies == null || galaxies.Count == 0) return null;

            // Counts scale with each galaxy's size so small companions get
            // proportionally thinner halos, like the galaxies themselves.
            float primaryRadius = Mathf.Max(1f, galaxies[0].Radius);
            var hosts = new List<Host>(galaxies.Count);
            Vector3 centroid = Vector3.zero;
            foreach (GalaxyMap map in galaxies)
            {
                if (map == null) continue;
                float scale = map.Radius / primaryRadius;
                hosts.Add(new Host
                {
                    center = map.transform.position,
                    radius = map.Radius,
                    haloStars = Mathf.RoundToInt(haloStarsPerGalaxy * scale),
                    clusters = Mathf.Max(1, Mathf.RoundToInt(clustersPerGalaxy * scale))
                });
                centroid += map.transform.position;
            }
            if (hosts.Count == 0) return null;
            centroid /= hosts.Count;

            // Sphere containing every halo, for the bridge sprinkle and bounds.
            float groupRadius = 0f;
            foreach (Host host in hosts)
            {
                groupRadius = Mathf.Max(groupRadius, Vector3.Distance(centroid, host.center) + host.radius * 2.6f);
            }

            var root = new GameObject("Intergalactic Halo");
            var bounds = new Bounds(centroid, Vector3.one * (groupRadius * 2.2f));

            BuildHaloAndBridge(root.transform, hosts, centroid, groupRadius, bridgeStarCount, primaryRadius, bounds);
            BuildClusters(root.transform, hosts, bounds);
            return root;
        }

        /// <summary>Sparse old stars: a spherical shell hugging each galaxy plus a thin group-wide sprinkle.</summary>
        private static void BuildHaloAndBridge(Transform root, List<Host> hosts, Vector3 centroid,
            float groupRadius, int bridgeStarCount, float primaryRadius, Bounds bounds)
        {
            int total = bridgeStarCount;
            foreach (Host host in hosts) total += host.haloStars;
            if (total <= 0) return;

            var vertices = new Vector3[total * 4];
            var colors = new Color32[total * 4];
            var uvs = new Vector2[total * 4];
            var centers = new Vector3[total * 4];
            var triangles = new int[total * 6];

            Color haloCool = new Color(0.75f, 0.82f, 1f);
            Color haloWarm = new Color(1f, 0.88f, 0.72f);

            int index = 0;
            foreach (Host host in hosts)
            {
                for (int i = 0; i < host.haloStars; i++)
                {
                    // Density falls off away from the rim: most stars hug the
                    // galaxy, a few drift far out.
                    float distance = host.radius * (1.15f + 1.35f * Mathf.Pow(Random.value, 1.6f));
                    Vector3 pos = host.center + Random.onUnitSphere * distance;

                    Color color = Color.Lerp(haloCool, haloWarm, Random.value);
                    color.a = Random.Range(0.2f, 0.45f);
                    float size = host.radius * Random.Range(0.0012f, 0.003f);
                    AddSprite(index++, pos, size, color, vertices, colors, uvs, centers, triangles);
                }
            }

            for (int i = 0; i < bridgeStarCount; i++)
            {
                Vector3 pos = centroid + Random.insideUnitSphere * groupRadius;
                Color color = Color.Lerp(haloCool, Color.white, Random.value);
                color.a = Random.Range(0.12f, 0.3f);
                float size = primaryRadius * Random.Range(0.0012f, 0.0028f);
                AddSprite(index++, pos, size, color, vertices, colors, uvs, centers, triangles);
            }

            CreateMeshObject(root, "Halo Stars", vertices, colors, uvs, centers, triangles, bounds,
                fadeNear: 25000f, fadeFar: 100000f);
        }

        /// <summary>Globular clusters: a soft core glow plus ~200 gauss-packed old stars each.</summary>
        private static void BuildClusters(Transform root, List<Host> hosts, Bounds bounds)
        {
            int clusterTotal = 0;
            foreach (Host host in hosts) clusterTotal += host.clusters;
            if (clusterTotal <= 0) return;

            int total = clusterTotal * (ClusterStarCount + 1); // +1 core glow sprite per cluster
            var vertices = new Vector3[total * 4];
            var colors = new Color32[total * 4];
            var uvs = new Vector2[total * 4];
            var centers = new Vector3[total * 4];
            var triangles = new int[total * 6];

            Color oldStarWarm = new Color(1f, 0.9f, 0.68f);
            Color oldStarWhite = new Color(1f, 0.97f, 0.9f);

            int index = 0;
            foreach (Host host in hosts)
            {
                for (int c = 0; c < host.clusters; c++)
                {
                    Vector3 clusterCenter = host.center
                                            + Random.onUnitSphere * (host.radius * Random.Range(1.25f, 2.2f));
                    float clusterRadius = host.radius * Random.Range(0.018f, 0.032f);

                    // One large faint glow makes the cluster read as a ball
                    // long before the individual stars resolve.
                    Color glow = oldStarWarm;
                    glow.a = 0.09f;
                    AddSprite(index++, clusterCenter, clusterRadius * 4.5f, glow,
                        vertices, colors, uvs, centers, triangles);

                    for (int i = 0; i < ClusterStarCount; i++)
                    {
                        Vector3 offset = new Vector3(GalaxyGenerator.Gauss(), GalaxyGenerator.Gauss(),
                            GalaxyGenerator.Gauss()) * (clusterRadius * 0.5f);
                        Color color = Color.Lerp(oldStarWarm, oldStarWhite, Random.value);
                        color.a = Random.Range(0.3f, 0.6f);
                        float size = host.radius * Random.Range(0.0005f, 0.0013f);
                        AddSprite(index++, clusterCenter + offset, size, color,
                            vertices, colors, uvs, centers, triangles);
                    }
                }
            }

            CreateMeshObject(root, "Globular Clusters", vertices, colors, uvs, centers, triangles, bounds,
                fadeNear: 12000f, fadeFar: 50000f);
        }

        /// <summary>Randomly oriented quad (billboarding thousands of sprites would cost every frame).</summary>
        private static void AddSprite(int index, Vector3 pos, float size, Color color,
            Vector3[] vertices, Color32[] colors, Vector2[] uvs, Vector3[] centers, int[] triangles)
        {
            Vector3 normal = Random.onUnitSphere;
            Vector3 right = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(right, normal);
            GalaxyGenerator.WriteQuad(index, pos, size, color, right, up, vertices, colors, uvs, centers, triangles);
        }

        private static void CreateMeshObject(Transform root, string name, Vector3[] vertices, Color32[] colors,
            Vector2[] uvs, Vector3[] centers, int[] triangles, Bounds bounds, float fadeNear, float fadeFar)
        {
            var mesh = RuntimeResourceScope.Own(
                new Mesh { name = name, indexFormat = IndexFormat.UInt32 });
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.SetUVs(1, centers);
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.bounds = bounds;

            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            // Near fade only: hides the sprites immediately around the camera
            // so flying through the halo never flashes the screen.
            meshRenderer.sharedMaterial = MaterialLibrary.CreateAdditiveFade(Color.white,
                MaterialLibrary.RadialGlowTexture, fadeNear, fadeFar);
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }
    }
}
