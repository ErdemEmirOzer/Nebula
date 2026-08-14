using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// LGT-01: global light ownership, the transient pool's capacity and
    /// priority rules, and the version constants this part was not allowed to
    /// move.
    /// </summary>
    public class LightingDirectorAndPoolTests
    {
        /// <summary>Answers the one question the director asks of streaming, without materializing a system.</summary>
        private sealed class FakeStarSource : IFullSystemStarSource
        {
            public string FullSystemId { get; set; }
            public Transform Star { get; set; }

            public Transform TryGetStarTransform(string systemId)
                => systemId != null && systemId == FullSystemId ? Star : null;
        }

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            LightingAccessibility.ResetToDefaults();
        }

        private Transform MakeStar(string name, Vector3 position, Color color, float range, float radius = 100f)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            go.transform.position = position;

            var star = go.AddComponent<StarLightDescriptionStub>();
            star.Configure(color, range, radius);
            return go.transform;
        }

        private NebulaLightingDirector MakeDirector(FakeStarSource source, Camera camera, LightingQualityProfile profile)
        {
            var go = new GameObject("Nebula Lighting");
            spawned.Add(go);
            var director = go.AddComponent<NebulaLightingDirector>();
            director.Initialize(source, camera, profile);
            return director;
        }

        // ---- global light ownership -------------------------------------

        [Test]
        public void WithNoFullSystem_TheDirectorLightsNothing()
        {
            NebulaLightingDirector director = MakeDirector(new FakeStarSource(), null, LightingQualityProfile.Medium);

            Assert.IsFalse(director.HasKeyLight);
            Assert.IsNull(director.LitSystemId);
            Assert.IsFalse(director.KeyLight.gameObject.activeSelf,
                "an empty sky must cost nothing, not a disabled-but-present draw");
        }

        [Test]
        public void TheDirectorOwnsExactlyOneRealtimeLight_NoMatterHowManySystemsExist()
        {
            var source = new FakeStarSource { FullSystemId = "sys-a" };
            source.Star = MakeStar("Star A", new Vector3(1000f, 0f, 0f), Color.red, 500f);

            // Three more stars in the sky, none of them Full.
            MakeStar("Warm Star", new Vector3(9000f, 0f, 0f), Color.blue, 500f);
            MakeStar("Decor Star 1", new Vector3(50000f, 0f, 0f), Color.white, 500f);
            MakeStar("Decor Star 2", new Vector3(90000f, 0f, 0f), Color.white, 500f);

            NebulaLightingDirector director = MakeDirector(source, null, LightingQualityProfile.Medium);
            director.Tick(0.016f);

            Light[] realtimeLights = director.GetComponentsInChildren<Light>(true)
                .Where(l => l.gameObject.activeInHierarchy).ToArray();

            Assert.AreEqual(1, realtimeLights.Length, "exactly one key light, whatever else is on screen");
            Assert.AreSame(director.KeyLight, realtimeLights[0]);
            Assert.AreEqual("sys-a", director.LitSystemId);
        }

        [Test]
        public void TheKeyLightTakesItsColourAndReachFromWhicheverStarIsFull()
        {
            var source = new FakeStarSource { FullSystemId = "sys-a" };
            source.Star = MakeStar("Star A", Vector3.zero, new Color(1f, 0.5f, 0.2f), 777f);

            NebulaLightingDirector director = MakeDirector(source, null, LightingQualityProfile.Medium);

            Assert.AreEqual(new Color(1f, 0.5f, 0.2f), director.KeyLight.color);
            Assert.AreEqual(777f, director.KeyLight.range, 1e-3f);
        }

        [Test]
        public void FullToWarmToFull_MovesTheOneLight_AndLeavesNothingBehindOnTheOldSystem()
        {
            var source = new FakeStarSource { FullSystemId = "sys-a" };
            source.Star = MakeStar("Star A", new Vector3(100f, 0f, 0f), Color.red, 400f);
            NebulaLightingDirector director = MakeDirector(source, null, LightingQualityProfile.Medium);
            director.Tick(0.016f);
            Assert.IsTrue(director.HasKeyLight);

            // Ship leaves: nothing is Full any more.
            source.FullSystemId = null;
            source.Star = null;
            director.Tick(0.016f);

            Assert.IsFalse(director.HasKeyLight, "a demoted system must not leave a light burning");
            Assert.IsNull(director.LitSystemId);

            // Ship arrives somewhere else.
            source.FullSystemId = "sys-b";
            source.Star = MakeStar("Star B", new Vector3(-4000f, 0f, 0f), Color.blue, 900f);
            director.Tick(0.016f);

            Assert.IsTrue(director.HasKeyLight);
            Assert.AreEqual("sys-b", director.LitSystemId);
            Assert.AreEqual(Color.blue, director.KeyLight.color);
            Assert.AreEqual(900f, director.KeyLight.range, 1e-3f);
            Assert.AreEqual(1, director.GetComponentsInChildren<Light>(true).Count(l => l.gameObject.activeInHierarchy),
                "there is still only ever one key light");
        }

        [Test]
        public void ReturningToTheSameSystem_RelightsIt_EvenThoughTheIdNeverChanged()
        {
            var source = new FakeStarSource { FullSystemId = "sys-a" };
            source.Star = MakeStar("Star A", Vector3.zero, Color.red, 400f);
            NebulaLightingDirector director = MakeDirector(source, null, LightingQualityProfile.Medium);
            director.Tick(0.016f);

            // The system is destroyed and rebuilt under the same id — what a
            // Full -> Warm -> Full round trip through one system looks like.
            Object.DestroyImmediate(source.Star.gameObject);
            source.Star = MakeStar("Star A Rebuilt", new Vector3(25f, 0f, 0f), Color.green, 650f);
            director.Tick(0.016f);

            Assert.IsTrue(director.HasKeyLight, "a rebuilt star must re-acquire the key light");
            Assert.AreEqual(Color.green, director.KeyLight.color);
            Assert.AreEqual(new Vector3(25f, 0f, 0f), director.KeyLight.transform.position);
        }

        // ---- floating origin --------------------------------------------

        [Test]
        public void TheKeyLightFollowsItsStarExactly_SoAFloatingOriginShiftCannotMakeItDrift()
        {
            var source = new FakeStarSource { FullSystemId = "sys-a" };
            Transform star = MakeStar("Star A", new Vector3(12000f, -300f, 8000f), Color.white, 500f);
            source.Star = star;

            NebulaLightingDirector director = MakeDirector(source, null, LightingQualityProfile.Medium);
            director.Tick(0.016f);
            Assert.AreEqual(star.position, director.KeyLight.transform.position);

            // A floating-origin shift moves everything in the world by -offset.
            var shift = new Vector3(-12000f, 300f, -8000f);
            star.position += shift;
            director.Tick(0.016f);

            Assert.AreEqual(star.position, director.KeyLight.transform.position,
                "the light reads the star's transform every frame, so there is no offset of its own to drift");
            Assert.AreEqual(Vector3.zero, director.KeyLight.transform.position);
        }

        // ---- quality profiles at runtime ---------------------------------

        [Test]
        public void SwitchingQualityAtRuntime_NeverAllocatesOrDestroysALight()
        {
            NebulaLightingDirector director = MakeDirector(new FakeStarSource(), null, LightingQualityProfile.Low);
            int lightsAtLow = director.GetComponentsInChildren<Light>(true).Length;

            director.SetQuality(LightingQuality.High);
            int lightsAtHigh = director.GetComponentsInChildren<Light>(true).Length;

            director.SetQuality(LightingQuality.Low);
            int lightsBackAtLow = director.GetComponentsInChildren<Light>(true).Length;

            Assert.AreEqual(lightsAtLow, lightsAtHigh);
            Assert.AreEqual(lightsAtLow, lightsBackAtLow);
            Assert.AreEqual(LightingQualityProfile.High.TransientLightCapacity, director.TransientLights.Capacity,
                "the pool is built once at the highest capacity any profile asks for");
        }

        [Test]
        public void TheActivePoolCeilingAlwaysLeavesRoomForTheKeyLight()
        {
            NebulaLightingDirector director = MakeDirector(new FakeStarSource(), null, LightingQualityProfile.Low);

            foreach (LightingQuality quality in new[] { LightingQuality.Low, LightingQuality.Medium, LightingQuality.High })
            {
                director.SetQuality(quality);
                LightingQualityProfile profile = LightingQualityProfile.Resolve(quality);

                Assert.LessOrEqual(director.TransientLights.ActiveCapacity + 1, profile.MaxRealtimeLights,
                    $"{quality}: key light plus pool must stay inside the real-time budget");
                Assert.LessOrEqual(director.TransientLights.ActiveCapacity, profile.TransientLightCapacity);
            }
        }

        [Test]
        public void DroppingQuality_HandsBackTheLightsThatNoLongerFit()
        {
            NebulaLightingDirector director = MakeDirector(new FakeStarSource(), null, LightingQualityProfile.High);
            TransientLightPool pool = director.TransientLights;

            for (int i = 0; i < pool.ActiveCapacity; i++)
                Assert.IsTrue(pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f), out _));
            Assert.AreEqual(LightingQualityProfile.High.TransientLightCapacity, pool.ActiveCount);

            director.SetQuality(LightingQuality.Low);

            Assert.LessOrEqual(pool.ActiveCount, pool.ActiveCapacity);
            Assert.LessOrEqual(pool.ActiveCount, LightingQualityProfile.Low.TransientLightCapacity);
        }

        // ---- transient pool ----------------------------------------------

        private TransientLightPool MakePool(int capacity)
        {
            var host = new GameObject("Pool Host");
            spawned.Add(host);
            return TransientLightPool.Create(host.transform, capacity);
        }

        [Test]
        public void ThePoolBuildsEveryLightUpFront_AndNoneOfThemAreOnUntilClaimed()
        {
            TransientLightPool pool = MakePool(6);

            Assert.AreEqual(6, pool.Capacity);
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(6, pool.Root.GetComponentsInChildren<Light>(true).Length,
                "a muzzle flash must never Instantiate a light");
            Assert.AreEqual(0, pool.Root.GetComponentsInChildren<Light>(false).Length);
        }

        [Test]
        public void AnExpiredLightGoesBackToThePoolAndIsHandedOutAgain()
        {
            TransientLightPool pool = MakePool(1);

            Assert.IsTrue(pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.red, 3f, 10f, 0.5f), out var first));
            Assert.AreEqual(1, pool.ActiveCount);

            pool.Tick(1f, Vector3.zero, 0f);

            Assert.AreEqual(0, pool.ActiveCount, "an expired light must return to the pool");
            Assert.IsFalse(pool.IsOwner(first), "and its old claim must stop matching");
            Assert.IsTrue(pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.blue, 3f, 10f, 0.5f), out var second));
            Assert.IsTrue(pool.IsOwner(second));
        }

        [Test]
        public void AtCapacity_ALowerPriorityEffectIsRefusedRatherThanGrowingThePool()
        {
            TransientLightPool pool = MakePool(2);

            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 5), out _);
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 5), out _);

            bool granted = pool.TryAcquire(
                new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 3), out var handle);

            Assert.IsFalse(granted, "gameplay never depends on a light, so refusing is a normal outcome");
            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(2, pool.Capacity, "the pool never grows");
            Assert.AreEqual(2, pool.ActiveCount);
        }

        [Test]
        public void AHigherPriorityEffectTakesOverTheLowestOne_AndTheOldOwnerLosesItCleanly()
        {
            TransientLightPool pool = MakePool(2);

            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 1), out var weak);
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 9), out var strong);

            Assert.IsTrue(pool.TryAcquire(
                new TransientLightRequest(Vector3.zero, Color.green, 1f, 5f, 10f, priority: 5), out var takeover));

            Assert.IsFalse(pool.IsOwner(weak), "the displaced owner's handle must stop matching immediately");
            Assert.IsTrue(pool.IsOwner(strong), "a higher-priority light is never displaced");
            Assert.IsTrue(pool.IsOwner(takeover));
            Assert.AreEqual(2, pool.ActiveCount, "a take-over reuses a light rather than adding one");
        }

        [Test]
        public void EqualPriorityNeverDisplaces_SoEffectsCannotThrashEachOther()
        {
            TransientLightPool pool = MakePool(1);
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 4), out var held);

            Assert.IsFalse(pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 4), out _));
            Assert.IsTrue(pool.IsOwner(held));
        }

        [Test]
        public void AStaleHandleCannotReleaseALightItNoLongerOwns()
        {
            TransientLightPool pool = MakePool(1);
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 1), out var stale);
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f, priority: 9), out var current);

            pool.Release(stale);

            Assert.IsTrue(pool.IsOwner(current), "the old owner must not be able to steal the light back out from under the new one");
            Assert.AreEqual(1, pool.ActiveCount);
        }

        [Test]
        public void DistantLightsStopBeingDrawnButKeepAging_SoDistanceNeverExtendsAnEffect()
        {
            TransientLightPool pool = MakePool(1);
            pool.TryAcquire(new TransientLightRequest(new Vector3(5000f, 0f, 0f), Color.white, 1f, 5f, 1f), out var handle);

            pool.Tick(0.1f, Vector3.zero, cullDistance: 100f);

            Assert.IsTrue(pool.IsOwner(handle), "culling hides a light, it does not end it");
            Assert.IsTrue(pool.TryGetLight(handle, out Light light));
            Assert.IsFalse(light.gameObject.activeSelf, "past the cull distance it must stop being drawn");

            pool.Tick(2f, Vector3.zero, cullDistance: 100f);
            Assert.IsFalse(pool.IsOwner(handle), "it still expired on schedule while culled");
        }

        [Test]
        public void FlashesRespectAccessibility_WithoutTheCallerHavingToKnow()
        {
            TransientLightPool pool = MakePool(2);
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 10f, 5f, 1f), out var normal);
            pool.TryGetLight(normal, out Light normalLight);
            float fullStrength = normalLight.intensity;

            LightingAccessibility.PhotosensitivitySafeMode = true;
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 10f, 5f, 1f), out var safe);
            pool.TryGetLight(safe, out Light safeLight);

            Assert.Less(safeLight.intensity, fullStrength);
        }

        // ---- material hygiene ---------------------------------------------

        [Test]
        public void TheLightingSubsystemOwnsNoRenderers_SoItCannotLeakMaterialInstances()
        {
            var source = new FakeStarSource { FullSystemId = "sys-a" };
            source.Star = MakeStar("Star A", Vector3.zero, Color.white, 400f);
            NebulaLightingDirector director = MakeDirector(source, null, LightingQualityProfile.High);
            director.Tick(0.016f);

            Assert.AreEqual(0, director.GetComponentsInChildren<Renderer>(true).Length,
                "lighting is lights only — with no renderer it can never touch .material and clone one");
        }

        // ---- versions this part was not allowed to move ---------------------

        [Test]
        public void LightingDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion,
                "LGT-01 is presentation only and must not touch world generation");
            Assert.AreEqual(3, ContentFingerprint.ContentRevision,
                "LGT-01 adds no content, so the content revision must stand still");
        }
    }

    /// <summary>
    /// Stands in for <c>Star</c>'s light description without dragging in the
    /// procedural mesh, textures and lens-flare registration a real star builds
    /// — none of which this fixture is testing.
    /// </summary>
    public class StarLightDescriptionStub : MonoBehaviour, IKeyLightSource
    {
        public Color KeyLightColor { get; private set; }
        public float KeyLightRange { get; private set; }
        public float KeyLightIntensityScale { get; private set; } = 1f;
        public float Radius { get; private set; }

        public void Configure(Color color, float range, float radius, float intensityScale = 1f)
        {
            KeyLightColor = color;
            KeyLightRange = range;
            Radius = radius;
            KeyLightIntensityScale = intensityScale;
        }
    }
}
