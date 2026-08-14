using System;
using NUnit.Framework;
using UnityEngine;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for TF-002 config fail-fast validation (§4/§9).</summary>
    public class NebulaBootstrapConfigTests
    {
        /// <summary>Mirrors Assets/Scenes/Nebula.unity's actual current serialized values — the hard requirement that "everything today's scene uses must pass validation".</summary>
        private static NebulaBootstrapConfig DefaultSceneConfig() => new NebulaBootstrapConfig
        {
            Seed = 12345,
            GravitationalConstant = 0.015f,
            StarsPerArm = 10,
            ArmStarRadiusRange = new Vector2(3000f, 7000f),
            GalaxyName = "Aetheria",
            GalaxyCenter = new Vector3(-4500000f, 0f, 0f),
            GalaxyRadius = 10000000f,
            GalaxyParticleCount = 150000,
            GalaxyArmCount = 4,
            GalaxyRotationSpeedDegPerSec = 0.05f,
            BlackHoleRadius = 120000f,
            CompanionGalaxies = new[]
            {
                new NebulaBootstrapConfig.CompanionGalaxy { Name = "Elysia", Scale = 0.75f, Center = new Vector3(-4500000f, 4000000f, 0f), CloudWhiteness = 0.35f },
                new NebulaBootstrapConfig.CompanionGalaxy { Name = "Nyxara", Scale = 0.6f, Center = new Vector3(10500000f, 4000000f, 0f), CloudWhiteness = 0.1f },
                new NebulaBootstrapConfig.CompanionGalaxy { Name = "Thalassa", Scale = 0.8f, Center = new Vector3(14500000f, 0f, 0f), CloudWhiteness = 0.2f }
            },
            HaloStarsPerGalaxy = 2200,
            ClustersPerGalaxy = 4,
            BridgeStarCount = 1200,
            AnomalyCount = 8,
            ShowOrbitRings = true,
            ShowPlanetTrails = true,
            StarfieldStarCount = 12000,
            StarfieldRadius = 500000f,
            NebulaCount = 26
        };

        [Test]
        public void DefaultSceneValues_PassValidation()
        {
            Assert.DoesNotThrow(() => DefaultSceneConfig().Validate());
        }

        [Test]
        public void Seed_ZeroIsAValidSentinel_NeverRejected()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.Seed = 0;
            Assert.DoesNotThrow(() => config.Validate());
        }

        [Test]
        public void EmptyGalaxyName_FailsFast_NamingTheField()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.GalaxyName = "  ";
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            StringAssert.Contains("GalaxyName", ex.Message);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void NonPositiveGravitationalConstant_FailsFast(float value)
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.GravitationalConstant = value;
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            StringAssert.Contains("GravitationalConstant", ex.Message);
        }

        [Test]
        public void NonPositiveGalaxyRadius_FailsFast()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.GalaxyRadius = 0f;
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            StringAssert.Contains("GalaxyRadius", ex.Message);
        }

        [Test]
        public void NonPositiveBlackHoleRadius_FailsFast()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.BlackHoleRadius = -5f;
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            StringAssert.Contains("BlackHoleRadius", ex.Message);
        }

        [Test]
        public void InvertedArmStarRadiusRange_FailsFast()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.ArmStarRadiusRange = new Vector2(9000f, 1000f);
            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [TestCase(-1)]
        public void NegativeStarsPerArm_FailsFast(int value)
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.StarsPerArm = value;
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            StringAssert.Contains("StarsPerArm", ex.Message);
        }

        [Test]
        public void NegativeAnomalyCount_FailsFast()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.AnomalyCount = -3;
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            StringAssert.Contains("AnomalyCount", ex.Message);
        }

        [Test]
        public void NegativeCompanionScale_FailsFast()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            NebulaBootstrapConfig.CompanionGalaxy[] companions = config.CompanionGalaxies;
            companions[0].Scale = -0.1f;
            config.CompanionGalaxies = companions;
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            StringAssert.Contains("Scale", ex.Message);
        }

        [Test]
        public void EmptyNameOnActiveCompanion_FailsFast()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            NebulaBootstrapConfig.CompanionGalaxy[] companions = config.CompanionGalaxies;
            companions[0].Name = "";
            config.CompanionGalaxies = companions;
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            StringAssert.Contains("Name", ex.Message);
        }

        [Test]
        public void EmptyNameOnDisabledCompanion_NeverRejected()
        {
            // scale <= 0 means the entry is inert (skipped by every installer) -- its name is irrelevant.
            NebulaBootstrapConfig config = DefaultSceneConfig();
            NebulaBootstrapConfig.CompanionGalaxy[] companions = config.CompanionGalaxies;
            companions[0].Scale = 0f;
            companions[0].Name = "";
            config.CompanionGalaxies = companions;
            Assert.DoesNotThrow(() => config.Validate());
        }

        [Test]
        public void Validate_NeverClampsOrSubstitutes_ThrowsInsteadOfMutating()
        {
            NebulaBootstrapConfig config = DefaultSceneConfig();
            config.GalaxyRadius = -1f;
            try { config.Validate(); } catch (InvalidOperationException) { /* expected */ }
            Assert.AreEqual(-1f, config.GalaxyRadius, "Validate must never silently clamp a field to a default -- only ever throw");
        }
    }
}
