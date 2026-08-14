using NUnit.Framework;
using UnityEngine;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the global O orbit-ring visibility state (VS-008.1
    /// §10.10) — exercised through the public, Input-independent API so no
    /// key-simulation is required. Full/Warm-materialization propagation is
    /// covered by the manual smoke checklist (needs a real scene + Legacy Input).
    /// </summary>
    public class OrbitGuideVisibilityTests
    {
        [Test]
        public void Initialize_SeedsVisibilityFromOrbitRingsArgument()
        {
            var go = new GameObject("StreamingDirectorTest");
            try
            {
                var streaming = go.AddComponent<SystemStreamingDirector>();
                streaming.Initialize(null, null, null, new System.Collections.Generic.List<Material>(), orbitRings: true, trails: false);
                Assert.IsTrue(streaming.OrbitGuidesVisible);

                streaming.Initialize(null, null, null, new System.Collections.Generic.List<Material>(), orbitRings: false, trails: false);
                Assert.IsFalse(streaming.OrbitGuidesVisible);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SetOrbitGuidesVisible_TogglesStateWithoutRequiringAnyMaterializedSystem()
        {
            var go = new GameObject("StreamingDirectorTest");
            try
            {
                var streaming = go.AddComponent<SystemStreamingDirector>();
                streaming.Initialize(null, null, null, new System.Collections.Generic.List<Material>(), orbitRings: true, trails: false);

                streaming.SetOrbitGuidesVisible(false);
                Assert.IsFalse(streaming.OrbitGuidesVisible);

                streaming.SetOrbitGuidesVisible(true);
                Assert.IsTrue(streaming.OrbitGuidesVisible);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
