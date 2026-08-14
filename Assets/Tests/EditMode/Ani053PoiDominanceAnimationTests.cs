using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.3: the information gate on points of interest, and how each real
    /// dominance state is allowed to read.
    /// </summary>
    public class Ani053PoiDominanceAnimationTests
    {
        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
        }

        // --- the information gate --------------------------------------------

        [Test]
        public void TypeStaysHidden_UntilTheGameHasIdentifiedIt()
        {
            Assert.IsTrue(PoiDominanceAnimationProfiles.TypeIsHidden(PoiKnowledgeLevel.Unknown));
            Assert.IsTrue(PoiDominanceAnimationProfiles.TypeIsHidden(PoiKnowledgeLevel.Signal));
            Assert.IsTrue(PoiDominanceAnimationProfiles.TypeIsHidden(PoiKnowledgeLevel.Located));
            Assert.IsFalse(PoiDominanceAnimationProfiles.TypeIsHidden(PoiKnowledgeLevel.Identified));
        }

        [Test]
        public void AllFourTypes_MoveIdenticallyWhileTheirTypeIsHidden()
        {
            foreach (PoiKnowledgeLevel level in new[] { PoiKnowledgeLevel.Signal, PoiKnowledgeLevel.Located })
            {
                PoiVisualCharacter character =
                    PoiDominanceAnimationProfiles.Resolve(level, PoiPauseReason.None);
                Assert.AreEqual(PoiVisualCharacter.NeutralContact, character, $"{level}");

                float[] rates = Enum.GetValues(typeof(PoiType)).Cast<PoiType>()
                    .Select(t => PoiDominanceAnimationProfiles.SpinDegreesPerSecond(t, character))
                    .ToArray();

                Assert.AreEqual(1, rates.Distinct().Count(),
                    $"at {level} the four types must be indistinguishable by their motion");
            }
        }

        [Test]
        public void OnceIdentified_TheTypesDiffer()
        {
            float[] rates = Enum.GetValues(typeof(PoiType)).Cast<PoiType>()
                .Select(t => PoiDominanceAnimationProfiles.SpinDegreesPerSecond(t, PoiVisualCharacter.IdleIdentified))
                .ToArray();

            Assert.Greater(rates.Distinct().Count(), 1, "an identified POI may show its own character");
        }

        [Test]
        public void APausedOperation_StopsMoving()
        {
            foreach (PoiPauseReason reason in Enum.GetValues(typeof(PoiPauseReason)).Cast<PoiPauseReason>())
            {
                PoiVisualCharacter character =
                    PoiDominanceAnimationProfiles.Resolve(PoiKnowledgeLevel.Active, reason);

                if (reason == PoiPauseReason.None)
                {
                    Assert.AreEqual(PoiVisualCharacter.Working, character);
                    Assert.IsTrue(PoiDominanceAnimationProfiles.IsAnimated(character));
                }
                else
                {
                    Assert.AreEqual(PoiVisualCharacter.Paused, character, $"{reason}");
                    Assert.IsFalse(PoiDominanceAnimationProfiles.IsAnimated(character),
                        "if the timer is not advancing, neither is the animation");
                }
            }
        }

        [Test]
        public void AResolvedPoi_ComesToRestAndStaysThere()
        {
            PoiVisualCharacter character =
                PoiDominanceAnimationProfiles.Resolve(PoiKnowledgeLevel.Resolved, PoiPauseReason.None);

            Assert.AreEqual(PoiVisualCharacter.Resolved, character);
            Assert.IsFalse(PoiDominanceAnimationProfiles.IsAnimated(character), "and never replays anything");
            foreach (PoiType type in Enum.GetValues(typeof(PoiType)).Cast<PoiType>())
                Assert.AreEqual(0f, PoiDominanceAnimationProfiles.SpinDegreesPerSecond(type, character), $"{type}");
        }

        [Test]
        public void TheScanEffect_OnlyAppearsOnARealRunningAnomalyAnalysis()
        {
            Assert.IsTrue(PoiDominanceAnimationProfiles.ShowsScanEffect(PoiType.Anomaly, PoiVisualCharacter.Working));
            Assert.IsFalse(PoiDominanceAnimationProfiles.ShowsScanEffect(PoiType.Anomaly, PoiVisualCharacter.Paused));
            Assert.IsFalse(PoiDominanceAnimationProfiles.ShowsScanEffect(PoiType.Anomaly, PoiVisualCharacter.NeutralContact),
                "a scan effect at Signal would announce the type");
            Assert.IsFalse(PoiDominanceAnimationProfiles.ShowsScanEffect(PoiType.Wreck, PoiVisualCharacter.Working));
        }

        [Test]
        public void AnUndecidedFacility_LeaksNeitherBranch()
        {
            float undecided = PoiDominanceAnimationProfiles.FacilityCadence(FacilityChoice.None);
            float dismantle = PoiDominanceAnimationProfiles.FacilityCadence(FacilityChoice.Dismantle);
            float relay = PoiDominanceAnimationProfiles.FacilityCadence(FacilityChoice.SensorRelay);

            Assert.AreEqual(1f, undecided, "before the player chooses, the facility is neutral");
            Assert.AreNotEqual(dismantle, relay, "after the choice, the two read differently");
        }

        [Test]
        public void EveryKnowledgeLevel_ResolvesToARealCharacter()
        {
            foreach (PoiKnowledgeLevel level in Enum.GetValues(typeof(PoiKnowledgeLevel)).Cast<PoiKnowledgeLevel>())
            {
                PoiVisualCharacter character = PoiDominanceAnimationProfiles.Resolve(level, PoiPauseReason.None);
                Assert.IsTrue(Enum.IsDefined(typeof(PoiVisualCharacter), character), $"{level}");
            }
        }

        // --- dominance ------------------------------------------------------------

        [Test]
        public void AnUnownedWorld_IsNotMarked()
        {
            Assert.IsNull(PoiDominanceAnimationProfiles.DominanceVfx(DominanceState.Unowned, abandoned: false));
            Assert.IsFalse(PoiDominanceAnimationProfiles.HasMarker(DominanceState.Unowned, abandoned: false));
        }

        [Test]
        public void EveryOwnedState_HasItsOwnMarker()
        {
            var marked = new[] { DominanceState.Developing, DominanceState.Controlled,
                                 DominanceState.Contested, DominanceState.Lost };

            var sprites = marked.Select(s => PoiDominanceAnimationProfiles.DominanceVfx(s, false).Value).ToArray();
            Assert.AreEqual(marked.Length, sprites.Distinct().Count(),
                "the four states must not be confusable with each other");
        }

        [Test]
        public void AnAbandonedWorld_NeverLooksControlled()
        {
            NebulaCommonVfx? abandoned = PoiDominanceAnimationProfiles.DominanceVfx(DominanceState.Controlled, abandoned: true);
            NebulaCommonVfx? controlled = PoiDominanceAnimationProfiles.DominanceVfx(DominanceState.Controlled, abandoned: false);

            Assert.IsNotNull(abandoned);
            Assert.AreNotEqual(controlled.Value, abandoned.Value);
        }

        [Test]
        public void ContestedIsSteady_NotBlinking()
        {
            float contested = PoiDominanceAnimationProfiles.MarkerMotion(DominanceState.Contested);
            float developing = PoiDominanceAnimationProfiles.MarkerMotion(DominanceState.Developing);

            Assert.Less(contested, developing, "a contested warning must not be the busiest thing on screen");
            Assert.Greater(contested, 0f, "but it must still be visible");
            Assert.AreEqual(0f, PoiDominanceAnimationProfiles.MarkerMotion(DominanceState.Lost),
                "a lost world is still and dim");
        }

        [Test]
        public void RecoveryIsShownOnlyWhenTheRealClockIsRunning()
        {
            Assert.IsTrue(PoiDominanceAnimationProfiles.IsRecovering(DominanceState.Lost, 12f));
            Assert.IsFalse(PoiDominanceAnimationProfiles.IsRecovering(DominanceState.Lost, 0f));
            Assert.IsFalse(PoiDominanceAnimationProfiles.IsRecovering(DominanceState.Controlled, 12f),
                "no new state is invented for recovery");
        }

        [Test]
        public void ASystemIsDominant_OnlyWhenEveryRealPlanetIsControlled()
        {
            Assert.IsTrue(PoiDominanceAnimationProfiles.SystemIsDominant(
                new[] { DominanceState.Controlled, DominanceState.Controlled }));
            Assert.IsFalse(PoiDominanceAnimationProfiles.SystemIsDominant(
                new[] { DominanceState.Controlled, DominanceState.Contested }));
            Assert.IsFalse(PoiDominanceAnimationProfiles.SystemIsDominant(new DominanceState[0]),
                "a system with no planets is not dominant");
        }

        [Test]
        public void EveryDominanceState_IsHandled()
        {
            foreach (DominanceState state in Enum.GetValues(typeof(DominanceState)).Cast<DominanceState>())
            {
                float motion = PoiDominanceAnimationProfiles.MarkerMotion(state);
                Assert.GreaterOrEqual(motion, 0f, $"{state}");
                Assert.LessOrEqual(motion, 1f, $"{state}");
            }
        }

        // --- versions ---------------------------------------------------------------

        [Test]
        public void TheSaveAndContentVersions_AreUntouched()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
