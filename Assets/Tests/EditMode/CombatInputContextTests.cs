using NUnit.Framework;
using UnityEngine;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the VS-013.1 §3 combat key remap, the pure binding-conflict validator, and the WorldInteractionBlocked context gate.</summary>
    public class CombatBindingValidatorTests
    {
        [TearDown]
        public void ResetBindings() => CombatBindings.ResetToDefaults();

        /// <summary>
        /// Tab is gone from this list and <c>[</c> and <c>Enter</c> have taken its
        /// place. The reserved set is derived from the action catalog now, so a key is
        /// on it exactly when some non-combat owner really reads it over live
        /// gameplay: the processing panel reads <c>[</c> (ShipProcessing.cs:91), the
        /// tutorial card reads Enter with no InputGate check (TutorialHud.cs:177), and
        /// nothing at all read Tab.
        /// </summary>
        [TestCase(KeyCode.V)]
        [TestCase(KeyCode.X)]
        [TestCase(KeyCode.N)]
        [TestCase(KeyCode.F)]
        [TestCase(KeyCode.T)]
        [TestCase(KeyCode.LeftBracket)]
        [TestCase(KeyCode.RightBracket)]
        [TestCase(KeyCode.Return)]
        [TestCase(KeyCode.Escape)]
        public void ReservedSystemKeys_CanNeverBeAssignedToACombatAction(KeyCode reserved)
        {
            Assert.IsTrue(CombatBindingValidator.IsReservedBySystem(reserved));
            bool changed = CombatBindings.TrySetBinding(CombatAction.CombatBoost, reserved);
            Assert.IsFalse(changed, $"{reserved} must be rejected -- it belongs to a non-combat system");
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey, "a rejected rebind must leave the existing binding untouched");
        }

        [Test]
        public void DefaultBindings_NeverCollideWithEvacuationLogisticsDismantleDeposit()
        {
            // The exact VS-013.1 fix: EM/cycle-target/manual-target moved off V/X/N.
            Assert.AreNotEqual(KeyCode.V, CombatBindings.FireEmKey);
            Assert.AreNotEqual(KeyCode.X, CombatBindings.CycleTargetKey);
            Assert.AreNotEqual(KeyCode.N, CombatBindings.ManualTargetSelectKey);
        }

        [Test]
        public void DefaultBindings_MatchTheNewSpecifiedKeys()
        {
            Assert.AreEqual(KeyCode.Mouse0, CombatBindings.FireLaserKey);
            Assert.AreEqual(KeyCode.Mouse1, CombatBindings.FirePlasmaKey);
            Assert.AreEqual(KeyCode.Mouse2, CombatBindings.FireEmKey);
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
            Assert.AreEqual(KeyCode.Alpha4, CombatBindings.CycleTargetKey);
            Assert.AreEqual(KeyCode.Alpha5, CombatBindings.ManualTargetSelectKey);
            Assert.AreEqual(KeyCode.Alpha6, CombatBindings.RepairKey);
        }

        [Test]
        public void TrySetBinding_RejectsCollidingWithAnotherCombatAction()
        {
            bool changed = CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Alpha4); // already CycleTarget
            Assert.IsFalse(changed);
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
        }

        [Test]
        public void TrySetBinding_AllowsAFreeUnusedKey()
        {
            bool changed = CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Alpha9);
            Assert.IsTrue(changed);
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey);
        }

        [Test]
        public void TrySetBinding_ReassigningAnActionToItsOwnCurrentKey_Succeeds()
        {
            // Must not reject a "conflict" against itself.
            bool changed = CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Alpha3);
            Assert.IsTrue(changed);
        }

        [Test]
        public void ResetToDefaults_RestoresEveryBinding()
        {
            CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Alpha9);
            CombatBindings.ResetToDefaults();
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
            Assert.AreEqual(KeyCode.Mouse2, CombatBindings.FireEmKey);
        }
    }

    public class CombatInputContextTests
    {
        [TearDown]
        public void Reset()
        {
            CombatInputContext.CombatActive = false;
            CombatInputContext.TrainingActive = false;
        }

        [Test]
        public void WorldInteractionBlocked_FalseByDefault()
        {
            Assert.IsFalse(CombatInputContext.WorldInteractionBlocked);
        }

        [Test]
        public void WorldInteractionBlocked_TrueWhileCombatActive()
        {
            CombatInputContext.CombatActive = true;
            Assert.IsTrue(CombatInputContext.WorldInteractionBlocked);
        }

        [Test]
        public void WorldInteractionBlocked_TrueWhileTrainingActive()
        {
            CombatInputContext.TrainingActive = true;
            Assert.IsTrue(CombatInputContext.WorldInteractionBlocked);
        }

        [Test]
        public void WorldInteractionBlocked_ClearsWhenBothGoFalse()
        {
            CombatInputContext.CombatActive = true;
            CombatInputContext.TrainingActive = true;
            CombatInputContext.CombatActive = false;
            Assert.IsTrue(CombatInputContext.WorldInteractionBlocked, "still true -- Training is still active");
            CombatInputContext.TrainingActive = false;
            Assert.IsFalse(CombatInputContext.WorldInteractionBlocked);
        }
    }
}
