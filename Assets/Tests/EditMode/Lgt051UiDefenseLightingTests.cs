using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// LGT-05.1: the shared UI status palette covers every reading the HUD
    /// makes, and a defensive weapon announces exactly the shots it actually
    /// fired.
    /// </summary>
    public class Lgt051UiDefenseLightingTests
    {
        private readonly List<Action<DefenseFireEvent>> handlers = new List<Action<DefenseFireEvent>>();

        [TearDown]
        public void TearDown()
        {
            foreach (Action<DefenseFireEvent> handler in handlers) DefenseFireSignal.Fired -= handler;
            handlers.Clear();
            DefenseFireSignal.ClearSubscribers();
            LightingAccessibility.ResetToDefaults();
        }

        private List<DefenseFireEvent> Listen()
        {
            var received = new List<DefenseFireEvent>();
            Action<DefenseFireEvent> handler = e => received.Add(e);
            handlers.Add(handler);
            DefenseFireSignal.Fired += handler;
            return received;
        }

        private static UiStatusLevel[] AllLevels => (UiStatusLevel[])Enum.GetValues(typeof(UiStatusLevel));

        // ---- palette coverage -------------------------------------------------

        [Test]
        public void EveryStatusLevelResolvesToItsOwnVisibleColour()
        {
            // Explicit lambda: Resolve now has an alpha overload, so a method
            // group here would be ambiguous with Select's indexed form.
            var colors = AllLevels.Select(level => LightingUiPalette.Resolve(level)).ToArray();

            Assert.AreEqual(AllLevels.Length, colors.Distinct().Count(),
                "each status must be distinguishable from the others");
            foreach (Color color in colors)
                Assert.Greater(color.maxColorComponent, 0.2f, "every status must stay legible on a dark panel");
        }

        [Test]
        public void TheStatusColoursComeFromTheSamePaletteAsTheWorldLighting()
        {
            Assert.AreEqual(ShipLightingProfiles.Industrial, LightingUiPalette.Warning,
                "a HUD warning must be the same amber as a warning structure");
            Assert.AreEqual(ShipLightingProfiles.Warning, LightingUiPalette.Critical,
                "critical is the same restrained red the hull uses");
            Assert.AreEqual(ShipLightingProfiles.Engineering, LightingUiPalette.Working,
                "work in progress is the same turquoise as repair");
            Assert.Greater(LightingUiPalette.Nominal.b, LightingUiPalette.Nominal.r,
                "nominal reads as friendly cold white-cyan");
        }

        [TestCase(1f, UiStatusLevel.Nominal)]
        [TestCase(0.7f, UiStatusLevel.Nominal)]
        [TestCase(0.5f, UiStatusLevel.Caution)]
        [TestCase(0.3f, UiStatusLevel.Warning)]
        [TestCase(0.1f, UiStatusLevel.Critical)]
        [TestCase(0f, UiStatusLevel.Critical)]
        public void DurabilityLayersCrossTheirThresholdsDeterministically(float fraction, UiStatusLevel expected)
        {
            Assert.AreEqual(expected, LightingUiPalette.DurabilityLayer(fraction * 100f, 100f));
            Assert.AreEqual(expected, LightingUiPalette.Energy(fraction * 100f, 100f),
                "combat energy fails the same way a durability layer does");
        }

        [Test]
        public void AMissingMaximumReadsAsInactiveRatherThanCritical()
        {
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.DurabilityLayer(0f, 0f),
                "a layer the ship does not have is absent, not failing");
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Storage(0f, 0f));
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Heat(0f, 0f));
        }

        [TestCase(0f, UiStatusLevel.Nominal)]
        [TestCase(0.6f, UiStatusLevel.Caution)]
        [TestCase(0.8f, UiStatusLevel.Warning)]
        [TestCase(1f, UiStatusLevel.Critical)]
        public void HeatRisesTowardCriticalInsteadOfFalling(float fraction, UiStatusLevel expected)
        {
            Assert.AreEqual(expected, LightingUiPalette.Heat(fraction * 100f, 100f));
        }

        [TestCase(0f, UiStatusLevel.Nominal)]
        [TestCase(0.79f, UiStatusLevel.Nominal)]
        [TestCase(0.8f, UiStatusLevel.Warning)]
        [TestCase(1f, UiStatusLevel.Critical)]
        public void StorageWarnsAtEightyPercentAndGoesCriticalWhenFull(float fraction, UiStatusLevel expected)
        {
            Assert.AreEqual(expected, LightingUiPalette.Storage(fraction * 100f, 100f),
                "a full store means production has stopped");
            Assert.AreEqual(0.8f, LightingUiPalette.StorageWarningFraction, 1e-4f);
        }

        [Test]
        public void EveryShipStateHasAHudReading()
        {
            foreach (ShipLightingState state in (ShipLightingState[])Enum.GetValues(typeof(ShipLightingState)))
                Assert.IsTrue(AllLevels.Contains(LightingUiPalette.Ship(state)), $"{state}");

            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Ship(ShipLightingState.Damaged));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Ship(ShipLightingState.Disabled));
            Assert.AreEqual(UiStatusLevel.Working, LightingUiPalette.Ship(ShipLightingState.Repair));
        }

        [Test]
        public void EveryStructureLifecycleHasAHudReading()
        {
            foreach (DrillLifecycle state in (DrillLifecycle[])Enum.GetValues(typeof(DrillLifecycle)))
                Assert.IsTrue(AllLevels.Contains(LightingUiPalette.Structure(state)), $"{state}");

            Assert.AreEqual(UiStatusLevel.Working, LightingUiPalette.Structure(DrillLifecycle.Constructing));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Structure(DrillLifecycle.Disabled));
        }

        [Test]
        public void EveryRaidPhaseHasAHudReadingAndAScheduledRaidStaysHidden()
        {
            foreach (RaidPhase phase in (RaidPhase[])Enum.GetValues(typeof(RaidPhase)))
                Assert.IsTrue(AllLevels.Contains(LightingUiPalette.Raid(phase)), $"{phase}");

            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Raid(RaidPhase.Scheduled),
                "a raid the player has not been warned about must not appear in the HUD");
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Raid(RaidPhase.Warning));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Raid(RaidPhase.Active));
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Raid(RaidPhase.Resolved));
        }

        [Test]
        public void EveryDominanceStateHasAHudReading()
        {
            foreach (DominanceState state in (DominanceState[])Enum.GetValues(typeof(DominanceState)))
                Assert.IsTrue(AllLevels.Contains(LightingUiPalette.Dominance(state)), $"{state}");

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Dominance(DominanceState.Controlled));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Dominance(DominanceState.Contested));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Dominance(DominanceState.Lost));
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Dominance(DominanceState.Unowned),
                "an unknown system must not be revealed by its HUD colour");
        }

        [Test]
        public void AttentionPriorityAndCommunicationLinkBothHaveReadings()
        {
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.Attention(0));
            Assert.AreEqual(UiStatusLevel.Caution, LightingUiPalette.Attention(1));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Attention(2));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Attention(5));

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.CommunicationLink(live: true, hasLastKnown: true));
            Assert.AreEqual(UiStatusLevel.Caution, LightingUiPalette.CommunicationLink(live: false, hasLastKnown: true),
                "last-known data is stale, not wrong");
            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.CommunicationLink(live: false, hasLastKnown: false));
        }

        // ---- accessibility --------------------------------------------------------

        /// <summary>
        /// A flash limit bounds how often the interface may change, not how bright a
        /// resting readout is allowed to be.
        /// <para>
        /// This test used to assert the opposite — that Flash Reduction dimmed the
        /// alarm colours — and it was measuring a real accessibility defect rather
        /// than protecting against one. The dimming ran every alarm through
        /// <c>ScaleFlashIntensity</c>, so the harder a photosensitive player asked
        /// the game to protect them the darker their critical readouts became; Safe
        /// Mode put Critical at 1.53:1 against a row, about a third of the WCAG AA
        /// floor. The one mode that exists to make the game safe was the mode in
        /// which a critical state was hardest to see.
        /// </para>
        /// <para>
        /// So the reduction moved to the only thing in the interface that actually
        /// repeats — <see cref="UiAnimationProfiles.MaxRepeatHz"/> — and both halves
        /// are asserted here: the resting colour must survive untouched, and the
        /// repeat ceiling must genuinely come down. Asserting only the first would
        /// pass a build in which Flash Reduction had quietly stopped doing anything
        /// at all.
        /// </para>
        /// </summary>
        [Test]
        public void FlashReductionBoundsTheRepeatRate_AndLeavesRestingAlarmsLegible()
        {
            Color normal = LightingUiPalette.Resolve(UiStatusLevel.Critical);
            float normalHz = UiAnimationProfiles.MaxRepeatHz;

            LightingAccessibility.FlashReduction = true;

            Color reduced = LightingUiPalette.Resolve(UiStatusLevel.Critical);

            Assert.AreEqual(normal, reduced,
                "flash reduction dimmed a resting status colour; the limit belongs on the repeat rate");
            Assert.Less(UiAnimationProfiles.MaxRepeatHz, normalHz,
                "flash reduction left the repeat ceiling alone, so it now does nothing at all");
            Assert.Greater(reduced.r, reduced.b, "it must still be recognisably the critical colour");
            Assert.Greater(reduced.maxColorComponent, 0.2f, "and it must stay legible");
        }

        [Test]
        public void SteadyNominalReadingsAreNotTreatedAsAlarms()
        {
            Color before = LightingUiPalette.Resolve(UiStatusLevel.Nominal);
            LightingAccessibility.FlashReduction = true;
            Color after = LightingUiPalette.Resolve(UiStatusLevel.Nominal);

            Assert.AreEqual(before, after, "Flash Reduction is about alarms, not about a healthy readout");
        }

        [Test]
        public void HighContrastBrightensStatusesInsteadOfDimmingThem()
        {
            Color normal = LightingUiPalette.Resolve(UiStatusLevel.Warning);
            LightingAccessibility.HighContrast = true;
            Color contrasted = LightingUiPalette.Resolve(UiStatusLevel.Warning);

            Assert.GreaterOrEqual(contrasted.maxColorComponent, normal.maxColorComponent,
                "High Contrast must separate a status from a dark panel, not hide it");
        }

        [Test]
        public void NoAccessibilityCombinationCanDriveTheInterfaceBelowItsReadabilityFloor()
        {
            LightingAccessibility.PhotosensitivitySafeMode = true;
            LightingAccessibility.FlashReduction = true;
            LightingAccessibility.LightIntensityScale = 0f; // clamped on write

            foreach (UiStatusLevel level in AllLevels)
                Assert.Greater(LightingUiPalette.Resolve(level).maxColorComponent, 0.2f,
                    $"{level} became unreadable under accessibility settings");
        }

        // ---- defense fire signal ------------------------------------------------------

        [Test]
        public void TheSignalCarriesTheFamilyPositionDirectionAndOwnerAndNothingElse()
        {
            List<DefenseFireEvent> received = Listen();
            var muzzle = new Vector3(10f, 20f, 30f);

            DefenseFireSignal.Raise(WeaponFamily.Plasma, muzzle, Vector3.forward, DefenseFireOwner.PlanetaryStructure);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(WeaponFamily.Plasma, received[0].Family);
            Assert.AreEqual(muzzle, received[0].MuzzlePosition);
            Assert.AreEqual(Vector3.forward, received[0].Direction);
            Assert.AreEqual(DefenseFireOwner.PlanetaryStructure, received[0].Owner);
        }

        [Test]
        public void EachAcceptedShotAnnouncesExactlyOnce()
        {
            List<DefenseFireEvent> received = Listen();

            DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward, DefenseFireOwner.PlanetaryStructure);
            DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward, DefenseFireOwner.PirateBase);

            Assert.AreEqual(2, received.Count, "two shots, two announcements — never doubled");
            Assert.AreEqual(2, DefenseFireSignal.RaisedCount);
        }

        [Test]
        public void ASignalWithNoSubscriberIsHarmless()
        {
            DefenseFireSignal.ClearSubscribers();

            Assert.DoesNotThrow(() => DefenseFireSignal.Raise(
                WeaponFamily.Laser, Vector3.zero, Vector3.forward, DefenseFireOwner.PirateBase),
                "combat must never depend on anything listening for its lights");
        }

        [Test]
        public void ClearSubscribersDropsEveryListenerSoARestartCannotLeaveOneBehind()
        {
            List<DefenseFireEvent> received = Listen();
            DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward, DefenseFireOwner.PirateBase);
            Assert.AreEqual(1, received.Count);

            DefenseFireSignal.ClearSubscribers();
            DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward, DefenseFireOwner.PirateBase);

            Assert.AreEqual(1, received.Count, "a cleared subscriber must stop receiving");
            Assert.AreEqual(1, DefenseFireSignal.RaisedCount, "and the counter resets with it");
        }

        [Test]
        public void AHostileTurretOutranksThePlayersOwnBatteryForALight()
        {
            // The bridge maps owner to priority; this pins the ordering it relies on.
            Assert.Greater((int)TransientLightPriority.CriticalPlayerImpact,
                (int)TransientLightPriority.PlanetaryDefense,
                "a Pirate Base shooting at the player must outrank the player's own turret");
        }

        [Test]
        public void EveryDefenceMuzzleFlashIsBoundedLikeEveryOtherCombatFlash()
        {
            foreach (WeaponFamily family in (WeaponFamily[])Enum.GetValues(typeof(WeaponFamily)))
                foreach (TransientLightPriority priority in new[]
                         { TransientLightPriority.PlanetaryDefense, TransientLightPriority.CriticalPlayerImpact })
                    Assert.IsTrue(
                        CombatLightingProfiles.IsWithinCombatCeiling(CombatLightingProfiles.Muzzle(family, priority)),
                        $"{family}/{priority}");
        }

        // ---- preserved authorities ----------------------------------------------------------

        [Test]
        public void UiAndDefenceLightingDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
