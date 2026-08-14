using UnityEngine;
using Nebula.Visuals;

namespace Nebula.Ship
{
    /// <summary>
    /// Drives the engine fire from the ship's actual motion: the faster the
    /// ship moves (or the harder it thrusts), the longer, denser and larger
    /// the particle flame behind it, while the engine halo swells and flickers.
    /// At rest the engine is nearly dark.
    /// </summary>
    public class ThrusterEffect : MonoBehaviour
    {
        [Tooltip("Speed (units/second) at which the flame reaches full length.")]
        [SerializeField] private float fullFlameSpeed = 2500f;

        private ShipController ship;
        private ParticleSystem flame;
        private Transform engineHalo;
        private Material haloMaterial;
        private Color haloBaseColor;
        private float haloBaseScale;
        private PlayerShipVisualController playerShipVisual;

        public void Initialize(ShipController controller, ParticleSystem flameSystem,
            Transform halo, Material haloMat, Color haloColor)
        {
            ship = controller;
            flame = flameSystem;
            engineHalo = halo;
            haloMaterial = haloMat;
            haloBaseColor = haloColor;
            haloBaseScale = halo != null ? halo.localScale.x : 1f;
            playerShipVisual = GetComponent<PlayerShipVisualController>();
            Texture2D boostTrail =
                NebulaProducedVisualResolver.VfxTexture(NebulaCommonVfx.BoostTrail);
            if (haloMaterial != null && boostTrail != null)
                haloMaterial.mainTexture = boostTrail;
        }

        private void Update()
        {
            if (ship == null || flame == null) return;
            SyncToAuthoredThrusters();

            float speedFactor = Mathf.Clamp01(ship.Velocity.magnitude / fullFlameSpeed);
            // Thrust keys give an immediate kick even before speed builds up.
            float intensity = Mathf.Max(speedFactor, ship.ThrustInput * 0.35f);

            var emission = flame.emission;
            emission.rateOverTime = intensity < 0.02f ? 0f : 40f + intensity * 240f;

            var main = flame.main;
            float speedScale = 8f + intensity * 34f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedScale * 0.7f, speedScale);
            float sizeScale = 0.7f + intensity * 1.7f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f * sizeScale, 0.9f * sizeScale);

            if (engineHalo != null)
            {
                float flicker = 1f + 0.12f * intensity * Mathf.Sin(Time.time * 23f);
                engineHalo.localScale = Vector3.one * (haloBaseScale * (0.55f + intensity * 2.3f) * flicker);
            }

            if (haloMaterial != null)
            {
                Color color = haloBaseColor;
                color.a = haloBaseColor.a * (0.35f + intensity * 0.9f);
                if (haloMaterial.HasProperty("_TintColor")) haloMaterial.SetColor("_TintColor", color);
                if (haloMaterial.HasProperty("_Color")) haloMaterial.SetColor("_Color", color);
            }
        }

        private void SyncToAuthoredThrusters()
        {
            if (playerShipVisual == null) return;
            Transform left = playerShipVisual.FindCurrentAnchor("anchor.thruster.L");
            Transform right = playerShipVisual.FindCurrentAnchor("anchor.thruster.R");
            if (left == null && right == null) return;

            Vector3 worldPosition = left != null && right != null
                ? Vector3.Lerp(left.position, right.position, 0.5f)
                : (left != null ? left.position : right.position);
            Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
            flame.transform.localPosition = localPosition;
            if (engineHalo != null) engineHalo.localPosition = localPosition;
        }
    }
}
