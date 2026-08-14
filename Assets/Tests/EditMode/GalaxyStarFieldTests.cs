using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The star field that will replace the sector grid's uniform placement.
    ///
    /// The decisive question these answer is whether stars strung along the arms can satisfy
    /// the world's own <see cref="SectorManifestGenerator.RequiredSeparation"/>. The visible
    /// galaxy only ever enforced 250,000 between its decorative stars, so this is a real
    /// unknown and has to be measured before the generator is changed rather than after.
    /// </summary>
    public class GalaxyStarFieldTests
    {
        private const ulong Seed = 12345UL;

        /// <summary>The scene's own four galaxies, at their configured centres, radii and star densities.</summary>
        private static List<GalaxyStarField.GalaxySpec> SceneGalaxies()
        {
            const float primaryRadius = 10_000_000f;
            const int arms = 4;
            const int starsPerArm = 10;
            return new List<GalaxyStarField.GalaxySpec>
            {
                new GalaxyStarField.GalaxySpec(
                    GalaxySpiral.Create(new Vector3(-4_500_000f, 0f, 0f), primaryRadius, arms), starsPerArm),
                new GalaxyStarField.GalaxySpec(
                    GalaxySpiral.Create(new Vector3(-4_500_000f, 4_000_000f, 0f), primaryRadius * 0.75f, arms),
                    Mathf.RoundToInt(starsPerArm * 0.75f)),
                new GalaxyStarField.GalaxySpec(
                    GalaxySpiral.Create(new Vector3(10_500_000f, 4_000_000f, 0f), primaryRadius * 0.6f, arms),
                    Mathf.RoundToInt(starsPerArm * 0.6f)),
                new GalaxyStarField.GalaxySpec(
                    GalaxySpiral.Create(new Vector3(14_500_000f, 0f, 0f), primaryRadius * 0.8f, arms),
                    Mathf.RoundToInt(starsPerArm * 0.8f))
            };
        }

        private static List<GalaxyStarField.Placement> Field()
        {
            var placements = new List<GalaxyStarField.Placement>();
            GalaxyStarField.Enumerate(Seed, SceneGalaxies(), placements);
            return placements;
        }

        [Test]
        public void TheFieldHoldsRoughlyTheCountTheGalaxiesAlreadyShow()
        {
            List<GalaxyStarField.Placement> field = Field();
            // 4 arms x (10 + 8 + 6 + 8) stars per arm.
            Assert.AreEqual(4 * (10 + 8 + 6 + 8), field.Count);
            TestContext.WriteLine($"[FIELD] stars = {field.Count}");
        }

        [Test]
        public void EveryGalaxyCarriesItsOwnStars()
        {
            var perGalaxy = new Dictionary<int, int>();
            foreach (GalaxyStarField.Placement p in Field())
            {
                perGalaxy.TryGetValue(p.GalaxyIndex, out int n);
                perGalaxy[p.GalaxyIndex] = n + 1;
            }

            Assert.AreEqual(4, perGalaxy.Count, "all four galaxies must be populated");
            foreach (KeyValuePair<int, int> kv in perGalaxy)
            {
                Assert.Greater(kv.Value, 0);
                TestContext.WriteLine($"[FIELD] galaxy {kv.Key} = {kv.Value}");
            }
        }

        [Test]
        public void TheField_IsReproducibleFromTheSeedAlone()
        {
            List<GalaxyStarField.Placement> a = Field();
            List<GalaxyStarField.Placement> b = Field();

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Key, b[i].Key);
                Assert.AreEqual(a[i].AbsolutePosition, b[i].AbsolutePosition);
            }
        }

        /// <summary>
        /// The measurement that decides whether the generator can move onto the arms at all.
        /// Reported rather than merely asserted, so the real figure is on the record.
        /// </summary>
        [Test]
        public void MeasureTheClosestPairOfStars_AgainstTheWorldsRequiredSeparation()
        {
            List<GalaxyStarField.Placement> field = Field();

            float closest = float.MaxValue;
            string pair = "";
            for (int i = 0; i < field.Count; i++)
                for (int j = i + 1; j < field.Count; j++)
                {
                    float d = Vector3.Distance(field[i].AbsolutePosition, field[j].AbsolutePosition);
                    if (d >= closest) continue;
                    closest = d;
                    pair = $"{field[i].Key} <-> {field[j].Key}";
                }

            TestContext.WriteLine($"[FIELD] closest pair = {closest:N0} units ({pair})");
            TestContext.WriteLine($"[FIELD] required separation = {SectorManifestGenerator.RequiredSeparation:N0}");

            Assert.Greater(closest, 0f, "two stars must never occupy the same point");
        }

        [Test]
        public void StarsSitInsideTheirOwnGalaxysDisc()
        {
            List<GalaxyStarField.GalaxySpec> galaxies = SceneGalaxies();
            foreach (GalaxyStarField.Placement p in Field())
            {
                GalaxySpiral spiral = galaxies[p.GalaxyIndex].Spiral;
                Vector3 local = p.AbsolutePosition - spiral.Center;
                float radial = new Vector2(local.x, local.z).magnitude;
                Assert.LessOrEqual(radial, spiral.Radius * 1.05f,
                    $"{p.Key} sits outside its galaxy");
            }
        }

        [Test]
        public void SomeStarLandsNearTheWorldOrigin_SoTheCampaignHasSomewhereToStart()
        {
            float nearest = float.MaxValue;
            string which = "";
            foreach (GalaxyStarField.Placement p in Field())
            {
                float d = p.AbsolutePosition.magnitude;
                if (d >= nearest) continue;
                nearest = d; which = p.Key;
            }

            TestContext.WriteLine($"[FIELD] nearest star to origin = {nearest:N0} units ({which})");
            // A star is pinned to the first arm's origin crossing, because the campaign starts
            // there. Left to the ordinary spread the nearest was 1,900,237 units away -- most
            // of a sector from where the ship spawns.
            Assert.Less(nearest, SectorCoordinate.SectorSize * 0.5f,
                "a star must sit near the world origin for the campaign to start at");
            Assert.AreEqual("g0a0s0", which, "the pinned origin star must be the nearest one");
        }
    }
}
