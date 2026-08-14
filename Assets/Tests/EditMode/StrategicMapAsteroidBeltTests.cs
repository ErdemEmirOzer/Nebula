using Nebula.UI.StrategicMap.Preview;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The asteroid belt is generated, not stored: the snapshot only records how many rocks a
    /// system has. These check the generation against the reference's own rules, since a
    /// belt that is merely "plausible" would drift away from the map it is meant to reproduce.
    /// </summary>
    public sealed class StrategicMapAsteroidBeltTests
    {
        private static StrategicMapUniverseSnapshot Load()
        {
            var snapshot = StrategicMapStaticPreviewSource.Load();
            Assert.IsNotNull(snapshot, "universe snapshot must load from Resources");
            return snapshot;
        }

        /// <summary>
        /// A system with the shape the live world actually produces. The retired HTML
        /// fixture carries no navigation radius, and the belt is measured from that — so
        /// the geometry has to be checked against real values, not the prototype's.
        /// </summary>
        private static StrategicMapUniverseSnapshot LiveLikeUniverse(out StrategicMapUniverseSnapshot.SystemRow system)
        {
            const float outermost = 110000f;
            var universe = new StrategicMapUniverseSnapshot();
            system = new StrategicMapUniverseSnapshot.SystemRow
            {
                id = "sector.0.0.0/system.000",
                name = "Albion",
                galaxyId = "aetheria",
                starRadius = 7200f,
                // The world's own rule: NavigationRadius is the outermost orbit x 2.5.
                navigationRadius = outermost * 2.5f,
                outermostOrbit = outermost,
                firstOrbitRadius = 34000f,
                asteroidCount = Nebula.Sectors.SystemMaterializer.AsteroidsPerSystem,
                planetFrom = 0, planetTo = 0
            };
            universe.systems.Add(system);
            return universe;
        }

        [Test]
        public void Belt_IsCappedAtTheDrawnSampleCount()
        {
            StrategicMapUniverseSnapshot universe = LiveLikeUniverse(out var system);

            // A system really does carry 500 asteroids; the map still only ever draws 54.
            Assert.Greater(system.asteroidCount, StrategicMapAsteroidBelt.MaxSamplesPerSystem);

            StrategicMapAsteroidBelt.Rock[] rocks = StrategicMapAsteroidBelt.Sample(universe, system.id);
            Assert.AreEqual(StrategicMapAsteroidBelt.MaxSamplesPerSystem, rocks.Length);
        }

        [Test]
        public void Belt_RingsTheSystemExactlyWhereTheWorldRingsIt()
        {
            StrategicMapUniverseSnapshot universe = LiveLikeUniverse(out var helios);
            float visualScale = StrategicMapOrbits.VisualScale(helios);
            float inner = helios.navigationRadius * Nebula.Sectors.SystemMaterializer.BeltInnerRadiusFactor
                          / StrategicMapOrbits.MapUnit * visualScale;
            float outer = helios.outermostOrbit * Nebula.Sectors.SystemMaterializer.BeltOuterRadiusFactor
                          / StrategicMapOrbits.MapUnit * visualScale;

            Assert.Greater(inner, helios.firstOrbitRadius / StrategicMapOrbits.MapUnit * visualScale,
                "the belt must sit outside the inner planets, which measuring from the first orbit did not");

            StrategicMapAsteroidBelt.Rock[] rocks = StrategicMapAsteroidBelt.Sample(universe, helios.id);

            float nearest = float.MaxValue, furthest = 0f;
            foreach (StrategicMapAsteroidBelt.Rock rock in rocks)
            {
                float radial = new Vector2(rock.Offset.x, rock.Offset.z).magnitude;
                Assert.GreaterOrEqual(radial, inner - 1e-4f, "no rock may fall inside the belt");
                Assert.LessOrEqual(radial, outer + 1e-4f, "no rock may fall outside the belt");
                nearest = Mathf.Min(nearest, radial);
                furthest = Mathf.Max(furthest, radial);
            }

            // sqrt-distributed radii crowd the outer edge, so 54 samples should reach most of it.
            Assert.Greater(furthest, outer * 0.9f, "the belt should reach its outer edge");
            Assert.Less(nearest, inner + (outer - inner) * 0.5f, "the belt should reach its inner half");
        }

        [Test]
        public void Belt_DependsOnEverySystemSampledBeforeIt()
        {
            StrategicMapUniverseSnapshot universe = Load();

            // One shared stream means a later system's rocks are a function of the earlier
            // draws. If two systems ever produced the same first rock, the stream would not
            // be being carried between them and the whole generation would be wrong.
            StrategicMapAsteroidBelt.Rock[] first = StrategicMapAsteroidBelt.Sample(universe, universe.systems[0].id);
            StrategicMapAsteroidBelt.Rock[] second = StrategicMapAsteroidBelt.Sample(universe, universe.systems[1].id);

            Assert.Greater(first.Length, 0);
            Assert.Greater(second.Length, 0);
            Assert.AreNotEqual(first[0].Drift, second[0].Drift);
        }

        [Test]
        public void Belt_IsDeterministic()
        {
            StrategicMapUniverseSnapshot universe = Load();
            string id = universe.systems[3].id;

            StrategicMapAsteroidBelt.Rock[] a = StrategicMapAsteroidBelt.Sample(universe, id);
            StrategicMapAsteroidBelt.Rock[] b = StrategicMapAsteroidBelt.Sample(universe, id);

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].Offset, b[i].Offset);
                Assert.AreEqual(a[i].Drift, b[i].Drift);
                Assert.AreEqual(a[i].VisualVariant, b[i].VisualVariant);
            }
        }

        [Test]
        public void Belt_DrawSizeStaysInsideTheDrawnRange()
        {
            StrategicMapUniverseSnapshot universe = Load();
            StrategicMapAsteroidBelt.Rock[] rocks = StrategicMapAsteroidBelt.Sample(universe, universe.systems[0].id);

            // The prototype's 0.42..1.45 was quoted against a frame about 2.25 times shorter
            // than the one the map renders at, where those rocks came out sub-pixel and the
            // belt simply did not appear. Same correction the galaxy dust carries.
            const float prominence = 2.25f;
            foreach (StrategicMapAsteroidBelt.Rock rock in rocks)
            {
                Assert.GreaterOrEqual(rock.DrawSize, 0.42f * prominence);
                Assert.LessOrEqual(rock.DrawSize, 1.45f * prominence);
                Assert.GreaterOrEqual(rock.Radius, 6f);
                Assert.LessOrEqual(rock.Radius, 110f);
                Assert.That(rock.VisualVariant, Is.EqualTo(0).Or.EqualTo(2));
            }
        }

        [Test]
        public void Belt_IsEmptyForAnUnknownSystem()
        {
            StrategicMapUniverseSnapshot universe = Load();
            Assert.AreEqual(0, StrategicMapAsteroidBelt.Sample(universe, "no-such-system").Length);
        }

        [Test]
        public void MissingPresentationComponents_DegradeToInvisibleWithoutBreakingSystemFocus()
        {
            StrategicMapUniverseSnapshot universe = LiveLikeUniverse(out var system);
            var go = new GameObject("AsteroidBelt.MissingPresentation");
            try
            {
                // This is deliberately more severe than a missing shader: it models an
                // incomplete/legacy instance where Initialise never installed either the
                // renderer or MeshFilter. The map must still accept a focus transition.
                StrategicMapAsteroidBelt belt = go.AddComponent<StrategicMapAsteroidBelt>();

                Assert.DoesNotThrow(() => belt.ShowSystem(universe, system.id));
                Assert.Greater(belt.RockCount, 0,
                    "the pure belt sample remains available even when its optional visual layer cannot render");
                Assert.IsNull(go.GetComponent<MeshRenderer>());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
