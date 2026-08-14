using UnityEngine;
using Nebula.Celestial;
using Nebula.Visuals;

namespace Nebula.World
{
    /// <summary>
    /// Scatters drifting asteroids through a star system: a thick disc between
    /// the given radii plus a fraction of strays anywhere on the sphere. Rocks
    /// are deformed sphere meshes (a few shared variants, GPU-instanced
    /// materials) so hundreds of them stay cheap to render.
    /// </summary>
    public static class AsteroidFieldGenerator
    {
        // Shared across every field: with ~150 belts in the universe,
        // per-field meshes and materials would fragment GPU instancing and
        // waste memory. One set serves all rocks everywhere.
        private static Mesh[] sharedMeshes;
        private static Material[] sharedMaterials;

        public static GameObject Generate(string name, Vector3 center, float innerRadius, float outerRadius,
            int count, Vector2 sizeRange, Vector2 driftSpeedRange)
        {
            var root = new GameObject(name);
            root.transform.position = center;

            Mesh[] meshes = null;
            Material[] materials = null;

            for (int i = 0; i < count; i++)
            {
                Vector3 offset;
                if (Random.value < 0.15f)
                {
                    // stray rock anywhere around the system
                    offset = Random.onUnitSphere * Random.Range(innerRadius, outerRadius);
                }
                else
                {
                    // thick disc around the ecliptic, sqrt for even area coverage
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float distance = Mathf.Lerp(innerRadius, outerRadius, Mathf.Sqrt(Random.value));
                    offset = new Vector3(
                        Mathf.Cos(angle) * distance,
                        Random.Range(-0.08f, 0.08f) * distance,
                        Mathf.Sin(angle) * distance);
                }

                // Squared bias: lots of small rocks, occasional giants.
                float radius = Mathf.Lerp(sizeRange.x, sizeRange.y, Mathf.Pow(Random.value, 2.2f));

                var rock = new GameObject("Asteroid " + i);
                rock.transform.SetParent(root.transform, false);
                rock.transform.localPosition = offset;
                rock.transform.rotation = Random.rotation;
                int authoredVariant = i % 3;
                if (!AsteroidVisualLibrary.Apply(rock, authoredVariant, radius * 2f, out _))
                {
                    // Fail-safe only: the procedural shared mesh/material
                    // path remains available if an imported visual is absent.
                    if (sharedMeshes == null) sharedMeshes = CreateRockMeshes(6);
                    if (sharedMaterials == null) sharedMaterials = CreateRockMaterials();
                    meshes = sharedMeshes;
                    materials = sharedMaterials;
                    rock.transform.localScale = Vector3.one * (radius * 2f);
                    rock.AddComponent<MeshFilter>().sharedMesh = meshes[Random.Range(0, meshes.Length)];
                    var meshRenderer = rock.AddComponent<MeshRenderer>();
                    meshRenderer.sharedMaterial = materials[Random.Range(0, materials.Length)];
                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;
                }

                rock.AddComponent<Asteroid>().Initialize(
                    radius * 1.05f,
                    Random.onUnitSphere * Random.Range(driftSpeedRange.x, driftSpeedRange.y),
                    Random.onUnitSphere,
                    Random.Range(2f, 20f));
            }

            return root;
        }

        /// <summary>
        /// Lumpy rock meshes made by radially displacing a sphere with noise.
        /// <para>
        /// Deliberately NOT registered with <see cref="RuntimeResourceScope"/>, for
        /// the same reason <see cref="CreateRockMaterials"/> wraps itself in
        /// <c>SuppressOwnership</c>: <see cref="sharedMeshes"/> is an
        /// application-lifetime cache built once for the whole process, and a
        /// session-scoped owner would destroy it at the first campaign end while the
        /// static field still pointed at it — after which the <c>== null</c> guard on
        /// the array itself would stay false and every later rock would be handed a
        /// destroyed mesh. <c>Object.Instantiate</c> never routes through the scope,
        /// so this is already true; it is written down because the asymmetry with the
        /// materials below is otherwise indistinguishable from an oversight.
        /// </para>
        /// <para>
        /// <c>baseMesh</c> is Unity's built-in primitive sphere — a shared imported
        /// asset. It is copied, never modified and never destroyed.
        /// </para>
        /// </summary>
        private static Mesh[] CreateRockMeshes(int variantCount)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh baseMesh = temp.GetComponent<MeshFilter>().sharedMesh;

            var meshes = new Mesh[variantCount];
            for (int v = 0; v < variantCount; v++)
            {
                Mesh mesh = Object.Instantiate(baseMesh);
                mesh.name = "Rock " + v;

                float seed1 = Random.Range(0f, 100f);
                float seed2 = Random.Range(100f, 200f);
                float seed3 = Random.Range(200f, 300f);
                float frequency = Random.Range(1.2f, 2.4f);

                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 direction = vertices[i].normalized;
                    float noise = (Mathf.PerlinNoise(direction.x * frequency + seed1, direction.y * frequency + seed1)
                                   + Mathf.PerlinNoise(direction.y * frequency + seed2, direction.z * frequency + seed2)
                                   + Mathf.PerlinNoise(direction.z * frequency + seed3, direction.x * frequency + seed3)) / 3f;
                    // Seam-duplicated vertices share positions, so they get the
                    // same noise value and the mesh never cracks open.
                    vertices[i] = direction * (0.5f * (0.9f + (noise - 0.5f) * 0.65f));
                }

                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                meshes[v] = mesh;
            }

            Object.Destroy(temp);
            return meshes;
        }

        private static Material[] CreateRockMaterials()
        {
            using (RuntimeResourceScope.SuppressOwnership())
            {
            var materials = new Material[]
            {
                MaterialLibrary.CreateLit(Color.white, 0.05f, 0f,
                    MaterialLibrary.CreatePerlinTexture(128, new Color(0.25f, 0.22f, 0.2f), new Color(0.5f, 0.45f, 0.4f), 5f)),
                MaterialLibrary.CreateLit(Color.white, 0.05f, 0f,
                    MaterialLibrary.CreatePerlinTexture(128, new Color(0.2f, 0.2f, 0.22f), new Color(0.45f, 0.47f, 0.5f), 5f)),
                MaterialLibrary.CreateLit(Color.white, 0.05f, 0f,
                    MaterialLibrary.CreatePerlinTexture(128, new Color(0.28f, 0.23f, 0.18f), new Color(0.55f, 0.42f, 0.3f), 5f))
            };
            foreach (Material material in materials)
            {
                material.enableInstancing = true;
            }
            return materials;
            }
        }
    }
}
