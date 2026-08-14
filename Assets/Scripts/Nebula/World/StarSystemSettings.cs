using UnityEngine;

namespace Nebula.World
{
    /// <summary>
    /// Everything that describes one star system: where it sits, how its star
    /// looks, and how its planet family is laid out. The bootstrap spawns one
    /// system per entry, so adding a third star is just adding an element.
    /// </summary>
    [System.Serializable]
    public class StarSystemSettings
    {
        public string name = "System";
        [Tooltip("World position of the star. Keep systems very far apart.")]
        public Vector3 position = Vector3.zero;

        [Header("Star")]
        public float starRadius = 800f;
        public float starDensity = 6f;
        public Color lightColor = new Color(1f, 0.88f, 0.66f);
        [Tooltip("Base color of the halo glow and lens flare.")]
        public Color glowTint = new Color(1f, 0.55f, 0.18f);
        public Color surfaceColorLow = new Color(1f, 0.58f, 0.2f);
        public Color surfaceColorHigh = new Color(1f, 0.98f, 0.86f);

        [Header("Planets")]
        public int planetCount = 10;
        public float firstOrbitRadius = 6000f;
        [Tooltip("Each orbit is the previous one times a random value in this range.")]
        public Vector2 orbitSpacingMultiplierRange = new Vector2(1.3f, 1.45f);
        public float maxOrbitInclinationDegrees = 7f;
    }
}
