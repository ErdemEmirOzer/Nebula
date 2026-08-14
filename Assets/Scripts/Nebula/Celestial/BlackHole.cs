using UnityEngine;
using Nebula.Visuals;

namespace Nebula.Celestial
{
    /// <summary>
    /// Supermassive black hole at the galactic core: pure-black event horizon,
    /// slowly rotating hot accretion disk and a warm core glow. Registered as a
    /// celestial body with near-zero density so the ship collides with it but
    /// it does not disturb the planetary orbits far away.
    /// </summary>
    public class BlackHole : CelestialBody
    {
        public void InitializeBlackHole(float bodyRadius)
        {
            Configure(CelestialBodyType.BlackHole, bodyRadius, bodyDensity: 1e-6f, movedByGravity: false);

            GameObject authoredVisual = NebulaVisualResolver.Instantiate(
                NebulaVisualCatalog.BlackHole, transform, bodyRadius * 2f, "Authored Black Hole Visual");

            if (authoredVisual != null)
            {
                // ANI-05.3: presentation only — the disk turns, the event
                // horizon does not. The fallback path below keeps its own
                // RotateSelf, so the disk never has two writers.
                gameObject.AddComponent<Nebula.Visuals.CelestialAnimationPresenter>()
                    .Configure(CelestialBodyType.BlackHole, default, default,
                        Sectors.EnvironmentClass.Calm, name);
            }

            if (authoredVisual == null)
            {
                GameObject horizon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                horizon.name = "Event Horizon (Fallback)";
                horizon.transform.SetParent(transform, false);
                horizon.transform.localScale = Vector3.one * (bodyRadius * 2f);
                horizon.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateUnlit(Color.black);

                // Procedural geometry remains strictly as the missing-asset
                // fallback; it is not created alongside the authored model.
                var diskPivot = new GameObject("Accretion Disk (Fallback)");
                diskPivot.transform.SetParent(transform, false);
                var diskSpin = diskPivot.AddComponent<RotateSelf>();
                diskSpin.axis = Vector3.up;
                diskSpin.degreesPerSecond = 1.5f;

                Material diskMaterial = MaterialLibrary.CreateAdditive(
                    new Color(1f, 0.85f, 0.6f, 0.9f), MaterialLibrary.CreateAccretionDiskTexture(256));
                for (int side = 0; side < 2; side++)
                {
                    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = side == 0 ? "Disk Top" : "Disk Bottom";
                    Object.Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(diskPivot.transform, false);
                    quad.transform.localRotation = Quaternion.Euler(side == 0 ? 90f : -90f, 0f, 0f);
                    quad.transform.localScale = Vector3.one * (bodyRadius * 7f);
                    var quadRenderer = quad.GetComponent<MeshRenderer>();
                    quadRenderer.sharedMaterial = diskMaterial;
                    quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    quadRenderer.receiveShadows = false;
                }

                GameObject photonRing = GameObject.CreatePrimitive(PrimitiveType.Quad);
                photonRing.name = "Photon Ring (Fallback)";
                Object.Destroy(photonRing.GetComponent<Collider>());
                photonRing.transform.SetParent(transform, false);
                photonRing.transform.localScale = Vector3.one * (bodyRadius * 2.7f);
                photonRing.AddComponent<Billboard>();
                var ringRenderer = photonRing.GetComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(
                    new Color(1f, 0.9f, 0.7f, 0.9f), MaterialLibrary.CreateRingTexture(256));
                ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ringRenderer.receiveShadows = false;

                GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                glow.name = "Core Glow (Fallback)";
                Object.Destroy(glow.GetComponent<Collider>());
                glow.transform.SetParent(transform, false);
                glow.transform.localScale = Vector3.one * (bodyRadius * 5f);
                glow.AddComponent<Billboard>();
                var glowRenderer = glow.GetComponent<MeshRenderer>();
                glowRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(
                    new Color(1f, 0.75f, 0.4f, 0.5f), MaterialLibrary.RadialGlowTexture);
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glowRenderer.receiveShadows = false;
            }

            // LGT-01: no real-time light. Every black hole in the game is a
            // galaxy core millions of units from anything the player can
            // touch, so its disk, photon ring and core glow carry it entirely
            // through additive emissive geometry — the same rule that took the
            // real-time light off decorative and Warm stars.
        }
    }
}
