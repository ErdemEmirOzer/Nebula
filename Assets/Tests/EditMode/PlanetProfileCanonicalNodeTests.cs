using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the canonical 12-node scheme and anchor multiplier (VS-009 §5/§10.6).</summary>
    public class PlanetProfileCanonicalNodeTests
    {
        private static PlanetProfile Profile(string planetId, ResourceFamily family, ulong surfaceLayoutSeed) => new PlanetProfile(
            new PlanetManifest(0, planetId, family, Rarity.Common, 40000f, 0f, 0f, 0f, 1200f,
                recommendedStart: false, EnvironmentClass.Calm, surfaceLayoutSeed));

        [Test]
        public void Nodes_HasExactlyTwelve_OneDrillOneCommandSevenDefenseThreeSupport()
        {
            PlanetProfile p = Profile("sector.0.0.0/system.000/planet.00", ResourceFamily.Metal, 1UL);
            Assert.AreEqual(12, p.Nodes.Count);

            int drill = 0, command = 0, defense = 0, support = 0;
            foreach (SurfaceNode n in p.Nodes)
            {
                switch (n.Kind)
                {
                    case SurfaceNodeKind.Drill: drill++; break;
                    case SurfaceNodeKind.Command: command++; break;
                    case SurfaceNodeKind.Defense: defense++; break;
                    case SurfaceNodeKind.Support: support++; break;
                }
            }
            Assert.AreEqual(1, drill); Assert.AreEqual(1, command); Assert.AreEqual(7, defense); Assert.AreEqual(3, support);
        }

        [Test]
        public void NodeIds_AreCanonical_PlanetIdSlashNodeSlashLogicalSlotId()
        {
            string planetId = "sector.0.0.0/system.000/planet.00";
            PlanetProfile p = Profile(planetId, ResourceFamily.Metal, 1UL);

            Assert.AreEqual(planetId + "/node/drill.0", p.DrillNode.Id);
            Assert.AreEqual(planetId + "/node/command.0", p.CommandNode.Id);
            Assert.AreEqual(planetId + "/node/defense.0", p.FirstDefenseNode.Id);

            var ids = new HashSet<string>();
            foreach (SurfaceNode n in p.Nodes) Assert.IsTrue(ids.Add(n.Id), $"duplicate node id {n.Id}");
        }

        [Test]
        public void NodeDirections_DependOnlyOnSurfaceLayoutSeedAndSlotId_NotOnPlanetId()
        {
            // Two different planet ids sharing the SAME SurfaceLayoutSeed must
            // produce identical local directions (only the id LABEL differs) —
            // proves direction is derived purely from (SurfaceLayoutSeed, slot).
            PlanetProfile a = Profile("sector.0.0.0/system.000/planet.00", ResourceFamily.Metal, 777UL);
            PlanetProfile b = Profile("sector.5.5.5/system.003/planet.02", ResourceFamily.Diamond, 777UL);

            for (int i = 0; i < 12; i++)
                Assert.AreEqual(a.Nodes[i].LocalNormal, b.Nodes[i].LocalNormal, $"slot {i}");
        }

        [Test]
        public void NodeDirections_DifferentSurfaceLayoutSeed_UsuallyDiffers()
        {
            PlanetProfile a = Profile("sector.0.0.0/system.000/planet.00", ResourceFamily.Metal, 1UL);
            PlanetProfile b = Profile("sector.0.0.0/system.000/planet.00", ResourceFamily.Metal, 2UL);
            Assert.AreNotEqual(a.DrillNode.LocalNormal, b.DrillNode.LocalNormal);
        }

        [TestCase(ResourceFamily.Metal, 1.01f)]
        [TestCase(ResourceFamily.Wood, 1.01f)]
        [TestCase(ResourceFamily.Diamond, 1.01f)]
        [TestCase(ResourceFamily.Water, 1.03f)]
        [TestCase(ResourceFamily.Gas, 1.08f)]
        public void AnchorRadiusMultiplier_MatchesArchetypeCategory(ResourceFamily family, float expectedMultiplier)
        {
            PlanetProfile p = Profile("sector.0.0.0/system.000/planet.00", family, 1UL);
            Assert.AreEqual(expectedMultiplier, p.AnchorRadiusMultiplier, 0.0001f);
        }

        [Test]
        public void SinglePlanet_AlwaysExactlyOneResourceFamily()
        {
            // Compile-time guarantee (Family is a single enum-typed property) — assert
            // it always resolves to one of the five defined values, never a sentinel.
            foreach (ResourceFamily f in new[] { ResourceFamily.Metal, ResourceFamily.Wood, ResourceFamily.Gas, ResourceFamily.Water, ResourceFamily.Diamond })
            {
                PlanetProfile p = Profile("sector.0.0.0/system.000/planet.00", f, 1UL);
                Assert.AreEqual(f, p.Family);
            }
        }
    }
}
