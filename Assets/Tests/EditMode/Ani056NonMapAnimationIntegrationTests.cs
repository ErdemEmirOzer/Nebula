using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.6: the final non-map gate. Every presenter the animation phase
    /// built must exist, the accessibility switches must all be reachable, and
    /// the versions the phase promised not to move must not have moved.
    /// </summary>
    public class Ani056NonMapAnimationIntegrationTests
    {
        private static readonly string[] RequiredPresenters =
        {
            // ANI-01
            "Nebula.Visuals.PlayerShipAnimationPresenter",
            // ANI-02
            "Nebula.Visuals.BuildCapsulePresenter",
            "Nebula.Visuals.ProcessingAnimationPresenter",
            "Nebula.Visuals.StructureAnimationPresenter",
            // ANI-03
            "Nebula.Visuals.VehicleAnimationPresenter",
            "Nebula.Visuals.SupportOperationPresenter",
            // ANI-04
            "Nebula.Visuals.CombatAnimationBridge",
            "Nebula.Visuals.EnemyCombatAnimationPresenter",
            "Nebula.Visuals.WeaponGimbalPresenter",
            "Nebula.Visuals.ProjectileAnimationPresenter",
            "Nebula.Visuals.DefenseTurretAnimationPresenter",
            "Nebula.Visuals.PirateBaseAnimationPresenter",
            "Nebula.Visuals.RaidAnimationPresenter",
            // ANI-05
            "Nebula.Visuals.CelestialAnimationPresenter",
            "Nebula.Visuals.PoiAnimationPresenter",
            "Nebula.Visuals.DominanceMarkerPresenter",
            "Nebula.Visuals.EconomyMechanicalPresenter",
            "Nebula.Visuals.ShipAttitudeAndHangarPresenter",
            "Nebula.Visuals.UiPanelAnimator"
        };

        private static Type Resolve(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(name, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        [Test]
        public void EveryPresenterTheAnimationPhaseBuilt_Exists()
        {
            string[] missing = RequiredPresenters.Where(n => Resolve(n) == null).ToArray();
            Assert.IsEmpty(missing, "missing presenters:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void ThereIsExactlyOneAnimationDirectorType()
        {
            Assert.IsNotNull(Resolve("Nebula.Visuals.NebulaAnimationDirector"));

            int directors = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Count(t => t.Name.EndsWith("AnimationDirector") && t.Namespace == "Nebula.Visuals");

            Assert.AreEqual(1, directors, "a second director would be a second composition root");
        }

        [Test]
        public void NoAnimationTypeLivesUnderStrategicMap()
        {
            Type[] mapAnimation = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t => t.Namespace != null
                            && t.Namespace.Contains("StrategicMap")
                            && (t.Name.Contains("Animation") || t.Name.Contains("Animator")))
                .ToArray();

            Assert.IsEmpty(mapAnimation,
                "StrategicMap animation is deferred and must carry no animation type: "
                + string.Join(", ", mapAnimation.Select(t => t.FullName)));
        }

        [Test]
        public void EveryAccessibilitySwitchIsReachable()
        {
            AnimationAccessibility.ResetToDefaults();
            Assert.IsFalse(AnimationAccessibility.MotionOff);
            Assert.IsFalse(AnimationAccessibility.ReducedMotion);
            Assert.AreEqual(1f, AnimationAccessibility.MotionScale);

            AnimationAccessibility.ReducedMotion = true;
            Assert.Less(AnimationAccessibility.MotionScale, 1f);
            Assert.Greater(AnimationAccessibility.MotionScale, 0f);

            AnimationAccessibility.MotionOff = true;
            Assert.AreEqual(0f, AnimationAccessibility.MotionScale);
            Assert.IsTrue(AnimationAccessibility.SnapStateChanges);

            AnimationAccessibility.ResetToDefaults();
        }

        [Test]
        public void PhotosensitivitySafeModeIsTheStrictestSetting()
        {
            LightingAccessibility.ResetToDefaults();
            Assert.IsTrue(UiAnimationProfiles.AllowsRepeatingEmphasis);

            LightingAccessibility.FlashReduction = true;
            float reduced = UiAnimationProfiles.MaxRepeatHz;

            LightingAccessibility.PhotosensitivitySafeMode = true;
            Assert.Less(UiAnimationProfiles.MaxRepeatHz, reduced);
            Assert.IsFalse(UiAnimationProfiles.AllowsRepeatingEmphasis);

            LightingAccessibility.ResetToDefaults();
        }

        [Test]
        public void LightingAndAnimation_OwnDisjointMaterialChannels()
            => Assert.IsEmpty(MaterialPropertyChannels.LightingOwned
                .Intersect(MaterialPropertyChannels.AnimationOwned));

        [Test]
        public void TurretTransformsBelongToExactlyOneOwner()
        {
            foreach (string id in EconomyMechanicalProfiles.KnownVisualIds)
                foreach (MechanicalPart part in EconomyMechanicalProfiles.For(id))
                    Assert.IsFalse(EconomyMechanicalProfiles.IsTurretOwned(part.NodeName), $"{id}");

            foreach (string id in ShipVehicleMechanicalProfiles.KnownVehicleIds)
                foreach (MechanicalPart part in ShipVehicleMechanicalProfiles.ForVehicle(id))
                    Assert.IsFalse(ShipVehicleMechanicalProfiles.IsForeignOwned(part.NodeName), $"{id}");
        }

        [Test]
        public void TheVersionsTheAnimationPhasePromisedNotToMove_HaveNotMoved()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
