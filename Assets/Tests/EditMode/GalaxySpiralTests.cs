using NUnit.Framework;
using UnityEngine;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The pure spiral must be the SAME spiral the live galaxy draws. If these two ever
    /// drift, real star systems placed on the arms would sit somewhere the visible galaxy
    /// is not — so the agreement is pinned rather than assumed.
    /// </summary>
    public class GalaxySpiralTests
    {
        private const float Radius = 10_000_000f;
        private const int Arms = 4;
        private static readonly Vector3 Center = new Vector3(-4_500_000f, 0f, 0f);

        /// <summary>A GalaxyMap configured exactly as GalaxyGenerator configures it.</summary>
        private static GalaxyMap LiveMap(out GameObject go)
        {
            GalaxySpiral spiral = GalaxySpiral.Create(Center, Radius, Arms);
            go = new GameObject("SpiralTestGalaxy");
            go.transform.position = Center;
            var map = go.AddComponent<GalaxyMap>();
            map.Configure(spiral.Radius, spiral.BulgeRadius, spiral.R0, spiral.B,
                spiral.MaxTheta, spiral.ArmPhase0, spiral.ArmCount);
            return map;
        }

        [Test]
        public void ThePureSpiral_ProducesTheSameArmPointsAsTheLiveGalaxyMap()
        {
            GalaxyMap map = LiveMap(out GameObject go);
            try
            {
                GalaxySpiral spiral = GalaxySpiral.Create(Center, Radius, Arms);
                for (int arm = 0; arm < Arms; arm++)
                    for (int step = 0; step <= 10; step++)
                    {
                        float t = step / 10f;
                        Vector3 expected = map.ArmLocalPoint(arm, t);
                        Vector3 actual = spiral.ArmLocalPoint(arm, t);
                        Assert.AreEqual(expected.x, actual.x, 0.5f, $"arm {arm} t {t} x");
                        Assert.AreEqual(expected.y, actual.y, 0.5f, $"arm {arm} t {t} y");
                        Assert.AreEqual(expected.z, actual.z, 0.5f, $"arm {arm} t {t} z");
                    }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheFirstArm_RunsThroughTheWorldOrigin_WhichIsWhereThePlayAreaSits()
        {
            GalaxySpiral spiral = GalaxySpiral.Create(Center, Radius, Arms);

            float nearest = float.MaxValue;
            for (int step = 0; step <= 2000; step++)
                nearest = Mathf.Min(nearest, spiral.ArmPoint(0, step / 2000f).magnitude);

            // GalaxyGenerator phases arm 0 onto the origin deliberately; a real system placed
            // on that arm has to be able to land in the sector the campaign starts in.
            Assert.Less(nearest, SectorSize,
                $"arm 0 must pass within a sector of the origin, nearest was {nearest}");
        }

        private const float SectorSize = 2_000_000f;

        [Test]
        public void ArmPointsStayInsideTheDisc_AndSpreadFromCoreToRim()
        {
            GalaxySpiral spiral = GalaxySpiral.Create(Center, Radius, Arms);

            float innermost = float.MaxValue, outermost = 0f;
            for (int arm = 0; arm < Arms; arm++)
                for (int step = 0; step <= 200; step++)
                {
                    Vector3 local = spiral.ArmLocalPoint(arm, step / 200f);
                    float r = new Vector2(local.x, local.z).magnitude;
                    Assert.LessOrEqual(r, Radius * 1.001f, "no arm point may fall outside the disc");
                    innermost = Mathf.Min(innermost, r);
                    outermost = Mathf.Max(outermost, r);
                }

            Assert.Less(innermost, Radius * 0.15f, "the arms must start near the core");
            Assert.Greater(outermost, Radius * 0.95f, "and reach the rim");
        }

        [Test]
        public void TheArmsAreEvenlySpacedAroundTheGalaxy()
        {
            GalaxySpiral spiral = GalaxySpiral.Create(Center, Radius, Arms);
            const float t = 0.5f;

            for (int arm = 1; arm < Arms; arm++)
            {
                Vector3 a = spiral.ArmLocalPoint(arm - 1, t);
                Vector3 b = spiral.ArmLocalPoint(arm, t);
                float delta = Mathf.DeltaAngle(
                    Mathf.Atan2(a.z, a.x) * Mathf.Rad2Deg,
                    Mathf.Atan2(b.z, b.x) * Mathf.Rad2Deg);
                Assert.AreEqual(360f / Arms, Mathf.Abs(delta), 0.01f, $"arms {arm - 1} and {arm}");
            }
        }
    }
}
