using UnityEngine;
using Nebula.Visuals;

namespace Nebula.World
{
    /// <summary>
    /// Infinite space backdrop that follows the camera, styled after a real
    /// milky-way photograph: a dense band of thousands of fine stars with
    /// puffy white core clouds along it, a violet region on one side of the
    /// band, warm pink-salmon on the other and deep blue away from it.
    /// Everything is additive and kept faint enough that foreground planets,
    /// stars and asteroids always read clearly against it.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class StarfieldGenerator : MonoBehaviour
    {
        private Transform followCamera;
        private Vector3 bandNormal;
        private Vector3 coreDirection; // direction of the galactic core within the band plane

        public void Initialize(int starCount, float shellRadius, int nebulaCount = 0)
        {
            bandNormal = Random.onUnitSphere;
            coreDirection = Vector3.Cross(bandNormal, Random.onUnitSphere);
            if (coreDirection.sqrMagnitude < 1e-4f) coreDirection = Vector3.Cross(bandNormal, Vector3.right);
            coreDirection.Normalize();

            BuildStars(starCount, shellRadius);
            CreateNebulae(nebulaCount, shellRadius);
            CreateColorZones(shellRadius);
            CreateGalacticGlow(shellRadius);
        }

        private void BuildStars(int starCount, float shellRadius)
        {
            starCount = Mathf.Clamp(starCount, 100, 15000); // 4 verts per star, keep under the 16-bit index limit

            var vertices = new Vector3[starCount * 4];
            var colors = new Color32[starCount * 4];
            var uvs = new Vector2[starCount * 4];
            var triangles = new int[starCount * 6];

            for (int i = 0; i < starCount; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                bool inBand = i % 3 < 2; // two thirds of the stars crowd into the band
                if (inBand)
                {
                    dir = (dir - bandNormal * (Vector3.Dot(dir, bandNormal) * 0.93f)).normalized;
                }

                float size = shellRadius * Random.Range(0.0008f, 0.0028f);
                if (Random.value < 0.03f) size *= 1.8f;

                float brightness = Mathf.Pow(Random.value, 2.2f) * 0.85f + 0.15f;
                if (inBand) brightness = Mathf.Min(1f, brightness * 1.25f);
                // Mostly white with a subtle cool/warm spread, like the photo.
                Color tint = Color.Lerp(new Color(0.8f, 0.85f, 1f), new Color(1f, 0.92f, 0.8f), Random.value);
                Color32 color = new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, brightness);

                Vector3 right = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
                Vector3 up = Vector3.Cross(right, dir);
                Vector3 center = dir * shellRadius;
                float half = size * 0.5f;

                int v = i * 4;
                vertices[v] = center + (-right - up) * half;
                vertices[v + 1] = center + (-right + up) * half;
                vertices[v + 2] = center + (right + up) * half;
                vertices[v + 3] = center + (right - up) * half;

                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);

                colors[v] = color;
                colors[v + 1] = color;
                colors[v + 2] = color;
                colors[v + 3] = color;

                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            var mesh = RuntimeResourceScope.Own(new Mesh { name = "Starfield" });
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            // Oversized bounds so the shell is never frustum-culled while it follows the camera.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (shellRadius * 2.2f));

            GetComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(Color.white, MaterialLibrary.RadialGlowTexture);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>
        /// Cloud quads on the shell. Band clouds are mostly whitish puffs (the
        /// milky core texture of the photo); the scattered ones use the photo's
        /// violet / blue / warm palette.
        /// </summary>
        private void CreateNebulae(int nebulaCount, float shellRadius)
        {
            if (nebulaCount <= 0) return;

            Color[] palette =
            {
                new Color(0.5f, 0.32f, 0.85f),  // violet
                new Color(0.68f, 0.35f, 0.75f), // purple-magenta
                new Color(0.25f, 0.45f, 0.95f), // deep blue
                new Color(0.4f, 0.55f, 0.9f),   // steel blue
                new Color(0.95f, 0.55f, 0.45f), // warm salmon
                new Color(0.9f, 0.45f, 0.55f)   // dusty pink
            };
            Color[] corePalette =
            {
                new Color(0.85f, 0.87f, 0.95f), // milky white-blue
                new Color(0.95f, 0.9f, 0.85f)   // milky warm white
            };

            for (int i = 0; i < nebulaCount; i++)
            {
                bool inBand = i % 5 < 3;
                Vector3 dir = Random.onUnitSphere;
                if (inBand)
                {
                    dir = (dir - bandNormal * (Vector3.Dot(dir, bandNormal) * 0.93f)).normalized;
                }

                bool milkyCloud = inBand && Random.value < 0.5f;
                Color colorA = milkyCloud
                    ? corePalette[Random.Range(0, corePalette.Length)]
                    : palette[Random.Range(0, palette.Length)];
                Color colorB = milkyCloud
                    ? corePalette[Random.Range(0, corePalette.Length)]
                    : palette[Random.Range(0, palette.Length)];

                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = milkyCloud ? "Core Cloud " + i : "Nebula " + i;
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                quad.transform.localPosition = dir * (shellRadius * 0.96f);
                quad.transform.localRotation = Quaternion.LookRotation(dir)
                                               * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                quad.transform.localScale = Vector3.one * (shellRadius * (milkyCloud
                    ? Random.Range(0.2f, 0.45f)
                    : inBand ? Random.Range(0.4f, 0.8f) : Random.Range(0.55f, 1.15f)));

                var quadRenderer = quad.GetComponent<MeshRenderer>();
                float intensity = milkyCloud
                    ? Random.Range(0.1f, 0.18f)
                    : inBand ? Random.Range(0.07f, 0.13f) : Random.Range(0.05f, 0.1f);
                quadRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(
                    new Color(1f, 1f, 1f, intensity),
                    MaterialLibrary.CreateNebulaTexture(256, colorA, colorB));
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;
            }
        }

        /// <summary>
        /// Three huge tinted regions like the reference photo: warm pink toward
        /// the galactic core, violet on the opposite side of the band, deep
        /// blue away from the band.
        /// </summary>
        private void CreateColorZones(float shellRadius)
        {
            Vector3 blueDirection = (Vector3.Cross(bandNormal, coreDirection) * 0.7f - bandNormal * 0.7f).normalized;

            (Vector3 dir, Color a, Color b, float size, float alpha)[] zones =
            {
                (coreDirection, new Color(0.95f, 0.5f, 0.45f), new Color(0.85f, 0.4f, 0.55f), 1.25f, 0.1f),  // warm pink
                (-coreDirection, new Color(0.5f, 0.32f, 0.85f), new Color(0.62f, 0.35f, 0.8f), 1.25f, 0.1f), // violet
                (blueDirection, new Color(0.2f, 0.4f, 0.95f), new Color(0.3f, 0.5f, 0.9f), 1.1f, 0.1f)       // deep blue
            };

            for (int i = 0; i < zones.Length; i++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Color Zone " + i;
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                quad.transform.localPosition = zones[i].dir * (shellRadius * 0.97f);
                quad.transform.localRotation = Quaternion.LookRotation(zones[i].dir)
                                               * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                quad.transform.localScale = Vector3.one * (shellRadius * zones[i].size);

                var quadRenderer = quad.GetComponent<MeshRenderer>();
                quadRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(
                    new Color(1f, 1f, 1f, zones[i].alpha),
                    MaterialLibrary.CreateNebulaTexture(256, zones[i].a, zones[i].b));
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;
            }
        }

        /// <summary>Warm elongated glow marking the galactic core inside the band.</summary>
        private void CreateGalacticGlow(float shellRadius)
        {
            float[] sizes = { 1.2f, 0.8f, 0.5f };
            float[] alphas = { 0.06f, 0.07f, 0.09f };

            for (int i = 0; i < sizes.Length; i++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Galactic Core " + i;
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                quad.transform.localPosition = coreDirection * (shellRadius * 0.94f);
                // Local Y aligned with the band normal so the X stretch lies in the band plane.
                quad.transform.localRotation = Quaternion.LookRotation(coreDirection, bandNormal);
                quad.transform.localScale = new Vector3(
                    shellRadius * sizes[i] * 1.9f, shellRadius * sizes[i] * 0.75f, 1f);

                var quadRenderer = quad.GetComponent<MeshRenderer>();
                quadRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(
                    new Color(1f, 0.92f, 0.78f, alphas[i]), MaterialLibrary.RadialGlowTexture);
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;
            }
        }

        private void LateUpdate()
        {
            if (followCamera == null)
            {
                Camera main = Camera.main;
                if (main == null) return;
                followCamera = main.transform;
            }
            transform.position = followCamera.position;
        }
    }
}
