using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Combat;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// LGT-05.1 in the real booted scene: a defensive shot produces exactly one
    /// muzzle light and no more, and the shared UI palette drives real HUD
    /// state transitions.
    /// </summary>
    public class Lgt051UiDefenseLightingPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        private readonly List<Action<DefenseFireEvent>> handlers = new List<Action<DefenseFireEvent>>();

        private IEnumerator BootScene()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu's New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            UnityEngine.Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Action<DefenseFireEvent> handler in handlers) DefenseFireSignal.Fired -= handler;
            handlers.Clear();
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

        private static NebulaLightingDirector Director()
        {
            var director = UnityEngine.Object.FindObjectOfType<NebulaLightingDirector>();
            Assert.IsNotNull(director, "the lighting director must exist after boot");
            return director;
        }

        private static Light[] ActiveWorldLights()
            => UnityEngine.Object.FindObjectsOfType<Light>()
                .Where(l => l != null && l.gameObject.activeInHierarchy && l.cullingMask == -1)
                .ToArray();

        // ---- muzzle singularity ------------------------------------------------

        [UnityTest]
        public IEnumerator ALaserDefenceShotProducesExactlyOneMuzzleLightAndNoImpact()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            director.TransientLights.ReleaseAll();
            int activeBefore = director.TransientLights.ActiveCount;
            List<DefenseFireEvent> announced = Listen();

            DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward,
                DefenseFireOwner.PlanetaryStructure);

            Assert.AreEqual(1, announced.Count, "one accepted shot, one announcement");
            Assert.AreEqual(activeBefore + 1, director.TransientLights.ActiveCount,
                "a hitscan Laser produces exactly one light — the muzzle; it has no separate impact event");
            Assert.AreEqual(WeaponFamily.Laser, announced[0].Family);
        }

        [UnityTest]
        public IEnumerator APlasmaDefenceShotProducesOneMuzzleNowAndItsImpactOnlyWhenResolved()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            director.TransientLights.ReleaseAll();
            int activeBefore = director.TransientLights.ActiveCount;

            DefenseFireSignal.Raise(WeaponFamily.Plasma, Vector3.zero, Vector3.forward,
                DefenseFireOwner.PlanetaryStructure);

            Assert.AreEqual(activeBefore + 1, director.TransientLights.ActiveCount,
                "firing produces the muzzle and nothing else");

            // The impact arrives separately, through the projectile path.
            director.TryAcquireTransientLight(
                CombatLightingProfiles.Impact(WeaponFamily.Plasma, TransientLightPriority.PlanetaryDefense)
                    .ToRequest(new Vector3(0f, 0f, 50f), director.Profile.Quality), out _);

            Assert.AreEqual(activeBefore + 2, director.TransientLights.ActiveCount,
                "one muzzle plus one impact — never two of either");
        }

        [UnityTest]
        public IEnumerator APirateBaseTurretShotProducesExactlyOneMuzzleLight()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            director.TransientLights.ReleaseAll();
            int activeBefore = director.TransientLights.ActiveCount;
            List<DefenseFireEvent> announced = Listen();

            DefenseFireSignal.Raise(WeaponFamily.Laser, new Vector3(100f, 0f, 0f), Vector3.forward,
                DefenseFireOwner.PirateBase);

            Assert.AreEqual(1, announced.Count);
            Assert.AreEqual(DefenseFireOwner.PirateBase, announced[0].Owner);
            Assert.AreEqual(activeBefore + 1, director.TransientLights.ActiveCount);
        }

        [UnityTest]
        public IEnumerator ARefusedShotProducesNoMuzzleAtAll()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            director.TransientLights.ReleaseAll();
            int activeBefore = director.TransientLights.ActiveCount;
            List<DefenseFireEvent> announced = Listen();

            // A shot that never passed its cooldown/range checks simply never
            // reaches DefenseFireSignal.Raise — so nothing happens at all.
            yield return null;

            Assert.AreEqual(0, announced.Count, "no announcement without an accepted shot");
            Assert.AreEqual(activeBefore, director.TransientLights.ActiveCount,
                "and therefore no muzzle light");
        }

        [UnityTest]
        public IEnumerator RepeatedDefenceFireStaysInsideThePoolAndTheProfileCeiling()
        {
            yield return BootScene();

            NebulaLightingDirector director = Director();
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int capacityBefore = director.TransientLights.Capacity;

            for (int i = 0; i < 80; i++)
                DefenseFireSignal.Raise(i % 2 == 0 ? WeaponFamily.Laser : WeaponFamily.Plasma,
                    Vector3.right * i, Vector3.forward,
                    i % 3 == 0 ? DefenseFireOwner.PirateBase : DefenseFireOwner.PlanetaryStructure);

            yield return null;

            Assert.LessOrEqual(director.TransientLights.ActiveCount, director.TransientLights.ActiveCapacity,
                "eighty shots must not push the pool past its ceiling");
            Assert.AreEqual(capacityBefore, director.TransientLights.Capacity, "the pool must not grow");
            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "defence fire must never instantiate a Light");
            Assert.LessOrEqual(ActiveWorldLights().Length, director.Profile.MaxRealtimeLights);
        }

        // ---- UI palette on the real HUD --------------------------------------------

        /// <summary>
        /// The real booted HUD reads its status colours through the shared table, and a
        /// flash limit reaches that interface by bounding how often it may repeat — not by
        /// dimming what a resting readout looks like.
        /// <para>
        /// OLD CONTRACT: this test asserted <c>after.maxColorComponent &lt; before</c>, i.e.
        /// that Flash Reduction dimmed the resting Critical colour. It was measuring a real
        /// accessibility defect rather than protecting against one: the dimming ran every
        /// alarm through <c>ScaleFlashIntensity</c>, which put Critical at 1.53:1 against a
        /// row under Safe Mode — about a third of the WCAG AA 4.5:1 floor — so the one mode
        /// that exists to protect photosensitive players was the mode in which a critical
        /// state was hardest to see.
        /// </para>
        /// <para>
        /// CURRENT PRODUCTION AUTHORITY: <see cref="LightingUiPalette.Apply"/> no longer
        /// touches a resting colour (only <c>UsesStrongPalette</c> reinforces it), and the
        /// limit lives on <see cref="UiAnimationProfiles.MaxRepeatHz"/> — 2 Hz normally,
        /// 1 Hz under Flash Reduction, 0 Hz under Safe Mode — which
        /// <see cref="UiEmphasisPulse.Hz"/> is the interface's only consumer of.
        /// </para>
        /// <para>
        /// INVARIANT STILL PROTECTED: Flash Reduction must demonstrably change the interface.
        /// Asserting only that the colour survives would pass a build in which the setting
        /// had quietly stopped doing anything at all, so the repeat ceiling — read here
        /// through the same static the HUD's own emphasis reads — must come down as well.
        /// The EditMode siblings (Lgt051/Lgt052/Lgt053) assert the same two halves; what
        /// this one adds is that the table is read through the real, booted HUD's own token
        /// path in a real scene.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheRealHudDrivesItsStatusColoursFromTheSharedPalette()
        {
            yield return BootScene();

            var combatHud = UnityEngine.Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudVitals>();
            Assert.IsNotNull(combatHud, "the combat HUD must exist after boot");

            // NebulaUiDesignTokens.Status is the exact call the booted HUD makes for its
            // own readings, and it delegates straight to the shared table.
            Color before = Nebula.UI.Design.NebulaUiDesignTokens.Status(UiStatusLevel.Critical);
            Assert.AreEqual(LightingUiPalette.Resolve(UiStatusLevel.Critical), before,
                "the HUD's token must be the shared palette's colour, not a copy of it");
            float repeatBefore = UiAnimationProfiles.MaxRepeatHz;
            float pulseBefore = Nebula.UI.Accessibility.UiEmphasisPulse.Hz;
            // Fixture validity, asserted rather than assumed: a Safe Mode leaked in from
            // somewhere else would already have zeroed the ceiling, and "it did not come
            // down" would then be a lie about this test rather than about the product.
            Assert.IsFalse(LightingAccessibility.FlashReduction,
                "this test must start with Flash Reduction off");
            Assert.Greater(repeatBefore, 0f,
                "this test must start from the accessibility defaults, with a repeat ceiling to lower");

            LightingAccessibility.FlashReduction = true;
            Color after = Nebula.UI.Design.NebulaUiDesignTokens.Status(UiStatusLevel.Critical);

            Assert.AreEqual(before, after,
                "flash reduction dimmed a resting status colour on the real HUD; the limit belongs on the repeat rate");
            Assert.Less(UiAnimationProfiles.MaxRepeatHz, repeatBefore,
                "Flash Reduction must reach the interface, not only the world");
            Assert.Less(Nebula.UI.Accessibility.UiEmphasisPulse.Hz, pulseBefore,
                "and it must reach the one thing in the interface that actually repeats");
            Assert.Greater(after.r, after.b, "and the state must stay recognisable");
            Assert.Greater(after.maxColorComponent, 0.2f, "and it must stay legible");

            yield return null;
        }

        [UnityTest]
        public IEnumerator AReadingWalksNominalToWarningToCriticalAndBackAsItsValueMoves()
        {
            yield return BootScene();

            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.DurabilityLayer(100f, 100f));
            Assert.AreEqual(UiStatusLevel.Caution, LightingUiPalette.DurabilityLayer(50f, 100f));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.DurabilityLayer(30f, 100f));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.DurabilityLayer(10f, 100f));
            Assert.AreEqual(UiStatusLevel.Nominal, LightingUiPalette.DurabilityLayer(100f, 100f),
                "recovery must return the reading to nominal, not latch");

            var seen = new[]
            {
                LightingUiPalette.Resolve(UiStatusLevel.Nominal),
                LightingUiPalette.Resolve(UiStatusLevel.Caution),
                LightingUiPalette.Resolve(UiStatusLevel.Warning),
                LightingUiPalette.Resolve(UiStatusLevel.Critical)
            };
            Assert.AreEqual(4, seen.Distinct().Count(), "each step must look different from the last");

            yield return null;
        }

        [UnityTest]
        public IEnumerator RaidDominanceStorageAndLinkAllResolveThroughTheOneTable()
        {
            yield return BootScene();

            Assert.AreEqual(UiStatusLevel.Inactive, LightingUiPalette.Raid(RaidPhase.Scheduled));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Raid(RaidPhase.Active));
            Assert.AreEqual(UiStatusLevel.Nominal,
                LightingUiPalette.Dominance(Nebula.Extraction.DominanceState.Controlled));
            Assert.AreEqual(UiStatusLevel.Critical,
                LightingUiPalette.Dominance(Nebula.Extraction.DominanceState.Lost));
            Assert.AreEqual(UiStatusLevel.Warning, LightingUiPalette.Storage(85f, 100f));
            Assert.AreEqual(UiStatusLevel.Critical, LightingUiPalette.Storage(100f, 100f));
            Assert.AreEqual(UiStatusLevel.Caution,
                LightingUiPalette.CommunicationLink(live: false, hasLastKnown: true));

            yield return null;
        }

        // ---- teardown -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartLeavesOneBridgeAndNoStaleDefenceSubscriber()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<CombatLightingBridge>().Length,
                "the previous session's bridge must be disposed with it");

            NebulaLightingDirector director = Director();
            int activeBefore = director.TransientLights.ActiveCount;

            // If a disposed session's bridge were still subscribed, this one
            // shot would light up twice.
            DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward,
                DefenseFireOwner.PlanetaryStructure);

            Assert.AreEqual(activeBefore + 1, director.TransientLights.ActiveCount,
                "exactly one bridge may answer a shot after a restart");

            // The scene is now key light + hull fill + the one muzzle we just
            // lit. The permanent pair must still be exactly one of each, and
            // the whole thing must sit inside the profile's ceiling.
            Light[] world = ActiveWorldLights();
            Assert.AreEqual(1, world.Count(l => l.name == "Hull Light"),
                "a restart must not accumulate hull fills");
            Assert.AreEqual(1, world.Count(l => ReferenceEquals(l, director.KeyLight)),
                "one key light survives a restart");
            Assert.LessOrEqual(world.Length, director.Profile.MaxRealtimeLights,
                "even mid-shot the scene stays inside its real-time budget");
        }
    }
}
