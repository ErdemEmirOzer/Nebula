using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §6: the readiness label is a pure function of already-existing data — never its own stored stat/state.</summary>
    public class Vs015ReadinessLabelTests
    {
        [Test]
        public void Undefended_WhenCommandInactive()
        {
            Assert.AreEqual(ReadinessLabel.Undefended,
                ReadinessLabelResolver.Resolve(commandActive: false, hasAnyActiveWeapon: true, true, true, 1f, true, true, true));
        }

        [Test]
        public void Undefended_WhenNoActiveWeapon()
        {
            Assert.AreEqual(ReadinessLabel.Undefended,
                ReadinessLabelResolver.Resolve(commandActive: true, hasAnyActiveWeapon: false, true, true, 1f, true, true, true));
        }

        [Test]
        public void BasicDefense_WhenCommandAndOneWeaponButNothingElse()
        {
            Assert.AreEqual(ReadinessLabel.BasicDefense,
                ReadinessLabelResolver.Resolve(true, true, hasUnlockedDefenseLayersActive: false, false, 0f, false, false, false));
        }

        [Test]
        public void Ready_WhenLayersActiveAndCriticalStructuresStandingAndReserveAtLeast25Percent()
        {
            Assert.AreEqual(ReadinessLabel.Ready,
                ReadinessLabelResolver.Resolve(true, true, true, true, reserveFillFraction: 0.25f, false, false, false));
        }

        [Test]
        public void NotReady_WhenReserveBelow25Percent()
        {
            Assert.AreEqual(ReadinessLabel.BasicDefense,
                ReadinessLabelResolver.Resolve(true, true, true, true, reserveFillFraction: 0.24f, false, false, false));
        }

        [Test]
        public void Fortified_RequiresAllUnlockedNodesReserve50PercentEngineerAndDefenseGroup()
        {
            Assert.AreEqual(ReadinessLabel.Fortified,
                ReadinessLabelResolver.Resolve(true, true, true, true, 0.50f, hasLocalEngineer: true, hasAssignedDefenseGroup: true, allUnlockedNodesComplete: true));
        }

        [Test]
        public void NotFortified_WithoutLocalEngineer()
        {
            Assert.AreEqual(ReadinessLabel.Ready,
                ReadinessLabelResolver.Resolve(true, true, true, true, 0.50f, hasLocalEngineer: false, hasAssignedDefenseGroup: true, allUnlockedNodesComplete: true));
        }

        [Test]
        public void NotFortified_WithoutAssignedDefenseGroup()
        {
            Assert.AreEqual(ReadinessLabel.Ready,
                ReadinessLabelResolver.Resolve(true, true, true, true, 0.50f, hasLocalEngineer: true, hasAssignedDefenseGroup: false, allUnlockedNodesComplete: true));
        }

        [Test]
        public void NotFortified_WithReserveJustUnder50Percent()
        {
            Assert.AreEqual(ReadinessLabel.Ready,
                ReadinessLabelResolver.Resolve(true, true, true, true, 0.49f, true, true, true));
        }

        [Test]
        public void PureFunction_SameInputsAlwaysProduceSameOutput()
        {
            ReadinessLabel a = ReadinessLabelResolver.Resolve(true, true, true, true, 0.30f, false, false, false);
            ReadinessLabel b = ReadinessLabelResolver.Resolve(true, true, true, true, 0.30f, false, false, false);
            Assert.AreEqual(a, b);
        }
    }
}
