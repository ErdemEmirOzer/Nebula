using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.UI;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the F1-F12 -&gt; accessible-key remap (§2/§7/§8).</summary>
    public class NebulaInputBindingsTests
    {
        /// <summary>
        /// Every process-wide input static back to its shipped state before each test.
        /// <para>
        /// The rebindable catalog, the router's registered contexts and its substituted
        /// key source are all statics that outlive a fixture. Without a reset on entry
        /// this fixture measured whatever the previous fixture in the same Unity process
        /// happened to leave behind, which is what made a batched run disagree with the
        /// same tests run alone. The real public reset contract, never reflection.
        /// </para>
        /// </summary>
        [SetUp]
        public void ResetProcessWideInputState()
        {
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
        }

        [TearDown]
        public void RestoreProcessWideInputState()
        {
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
        }

        /// <summary>
        /// The old contract here was <c>HudToggleKey == Tab</c>, and it was green for
        /// four slices while being worth nothing: <c>NebulaInputBindings</c> declared
        /// the key, <c>CombatBindingValidator</c> reserved it, the shipping README
        /// documented it, and no file under <c>Assets/Scripts</c> ever read it. The
        /// four HUD classes its summary named no longer exist and
        /// <c>TutorialKeyPrompts.HudToggle()</c>, the one thing that could have printed
        /// it, was itself never called. A test asserting a constant equals a literal
        /// cannot tell that apart from a working feature.
        /// <para>
        /// The binding was deleted rather than wired to a HUD seam, because
        /// <c>NebulaUiShell.SetHudVisible</c> is already the single authority over the
        /// two HUD canvases and a player toggle would be a second one. So the contract
        /// this replaces it with is the one that would actually have caught the defect:
        /// every fixed system shortcut must have a runtime consumer, and Tab — owning
        /// nothing — must be free for a player to bind.
        /// </para>
        /// </summary>
        [Test]
        public void TabIsNoLongerReserved_BecauseNothingOwnsIt()
        {
            Assert.IsFalse(CombatBindingValidator.IsReservedBySystem(KeyCode.Tab),
                "Tab is reserved against combat rebinding but no runtime code reads it");

            foreach (InputActionDefinition action in NebulaInputActions.All)
                Assert.AreNotEqual(KeyCode.Tab, action.Key,
                    $"{action.Id} claims Tab — if that is real, Tab must go back on the reserved list");

            Assert.IsTrue(CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Tab),
                "a key nothing owns must be assignable");
            CombatBindings.ResetToDefaults();
        }

        [Test]
        public void SectorDebugToggleKey_IsAlpha0()
        {
            Assert.AreEqual(KeyCode.Alpha0, NebulaInputBindings.SectorDebugToggleKey);
        }

        [Test]
        public void ManualSaveKey_IsComma()
        {
            Assert.AreEqual(KeyCode.Comma, NebulaInputBindings.ManualSaveKey);
        }

        [Test]
        public void ManualLoadKey_IsPeriod()
        {
            Assert.AreEqual(KeyCode.Period, NebulaInputBindings.ManualLoadKey);
        }

        [Test]
        public void FlightTestSpeedPanelKey_IsStillJ()
        {
            Assert.AreEqual(KeyCode.J, FlightTestSpeedHud.ToggleKey);
        }

        [Test]
        public void SensorRelayKey_IsStillZ()
        {
            Assert.AreEqual(KeyCode.Z, ShipPoi.RelayChoiceKey);
        }

        [Test]
        public void AllPrimaryKeyOwners_AreMutuallyDistinct()
        {
            var keys = new[]
            {
                NebulaInputBindings.SectorDebugToggleKey,
                NebulaInputBindings.ManualSaveKey,
                NebulaInputBindings.ManualLoadKey,
                FlightTestSpeedHud.ToggleKey,
                ShipPoi.RelayChoiceKey,
                ShipPoi.StartOperationKey,
                ShipPoi.CancelKey
            };
            for (int i = 0; i < keys.Length; i++)
                for (int j = i + 1; j < keys.Length; j++)
                    Assert.AreNotEqual(keys[i], keys[j], $"keys[{i}]={keys[i]} collides with keys[{j}]={keys[j]}");
        }

        // --- Pure modifier/chord logic -- no Input simulation needed -----------

        [Test]
        public void IsControlHeld_TrueWithEitherSide()
        {
            Assert.IsTrue(NebulaInputBindings.IsControlHeld(true, false));
            Assert.IsTrue(NebulaInputBindings.IsControlHeld(false, true));
            Assert.IsFalse(NebulaInputBindings.IsControlHeld(false, false));
        }

        [Test]
        public void IsCommandHeld_TrueWithEitherSide()
        {
            Assert.IsTrue(NebulaInputBindings.IsCommandHeld(true, false));
            Assert.IsTrue(NebulaInputBindings.IsCommandHeld(false, true));
            Assert.IsFalse(NebulaInputBindings.IsCommandHeld(false, false));
        }

        [TestCase(true, false, false, false, ExpectedResult = true)]  // left control
        [TestCase(false, true, false, false, ExpectedResult = true)]  // right control
        [TestCase(false, false, true, false, ExpectedResult = true)]  // left command
        [TestCase(false, false, false, true, ExpectedResult = true)]  // right command
        [TestCase(false, false, false, false, ExpectedResult = false)]
        public bool IsCommonModifierHeld_AnySingleModifierSideIsEnough(bool lc, bool rc, bool lcmd, bool rcmd) =>
            NebulaInputBindings.IsCommonModifierHeld(lc, rc, lcmd, rcmd);

        [Test]
        public void IsManualSaveChord_RequiresBothModifierAndKeyThisFrame()
        {
            Assert.IsTrue(NebulaInputBindings.IsManualSaveChord(commonModifierHeld: true, saveKeyPressedThisFrame: true));
            Assert.IsFalse(NebulaInputBindings.IsManualSaveChord(commonModifierHeld: false, saveKeyPressedThisFrame: true),
                "comma alone (no modifier) must never fire manual save");
            Assert.IsFalse(NebulaInputBindings.IsManualSaveChord(commonModifierHeld: true, saveKeyPressedThisFrame: false));
        }

        [Test]
        public void IsManualLoadChord_RequiresBothModifierAndKeyThisFrame()
        {
            Assert.IsTrue(NebulaInputBindings.IsManualLoadChord(commonModifierHeld: true, loadKeyPressedThisFrame: true));
            Assert.IsFalse(NebulaInputBindings.IsManualLoadChord(commonModifierHeld: false, loadKeyPressedThisFrame: true),
                "period alone (no modifier) must never fire manual load");
            Assert.IsFalse(NebulaInputBindings.IsManualLoadChord(commonModifierHeld: true, loadKeyPressedThisFrame: false));
        }

        [Test]
        public void SaveSchema_IsPinned_UnaffectedByTheRemap()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
        }

        /// <summary>
        /// Old contract: this pinned 2, the generation version in force when the control remap landed.
        /// Current production authority: <see cref="SectorManifestGenerator.GenerationVersion"/> = 3
        /// (Assets/Scripts/Nebula/Sectors/SectorManifestGenerator.cs) -- 9d33695 moved systems off the
        /// uniform 2x2x2 sub-cell grid onto the galaxy spiral arms, a deliberate world-generation change
        /// that regenerates every existing campaign's universe and was approved as such.
        /// Invariant still protected: rebinding keys touches no world generation, so the remap must leave
        /// that number where it is. The literal is deliberate -- reading the constant back would assert
        /// nothing, so a bump has to break this test and be re-pinned alongside the world-gen change
        /// that earned it.
        /// </summary>
        [Test]
        public void GenerationVersion_IsPinnedToThree_UnaffectedByTheRemap()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
        }
    }
}
