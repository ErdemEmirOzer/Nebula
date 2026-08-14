using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The player ship as it is actually built, in a running scene: the normal
    /// catalog hull, the fail-safe hull behind it, and the promise that only one
    /// of them is ever on screen.
    /// <para>
    /// Part 4 took a 25.7 MB GLB out of <c>Resources/</c> — 70.0 MB expanded, the
    /// largest single asset in the player — by pointing the fail-safe at
    /// <c>PlayerShip_MkI_Base</c>, a wrapper prefab the build already carried. The
    /// saving is only real if the fail-safe still draws a readable ship, so the
    /// interesting assertions here are the ones about the degraded path rather
    /// than the healthy one.
    /// </para>
    /// <para>
    /// Everything is built through <c>ShipFactory.Create</c> rather than by adding
    /// components by hand, because the ordering inside that method is part of what
    /// is being tested: the fail-safe is chosen by control flow there, and a probe
    /// that constructs the controller directly would pass whatever the factory did.
    /// </para>
    /// </summary>
    public class PlayerShipFallbackPlayModeTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.Destroy(go);
            spawned.Clear();
            NebulaVisualResolver.ClearCache();
        }

        private ShipController CreateShip()
        {
            ShipController controller = ShipFactory.Create(Vector3.zero, Quaternion.identity);
            spawned.Add(controller.gameObject);
            return controller;
        }

        // ---- the normal path -------------------------------------------------

        /// <summary>
        /// A ship built the ordinary way gets a Part 24 catalog hull, and no
        /// fail-safe is created at all.
        /// <para>
        /// The second half is the load-bearing one. A fail-safe that is created
        /// and then hidden costs a full prefab instantiation on every spawn and
        /// leaves a second copy of every mesh resident — which is exactly the
        /// shape of waste this pass exists to remove, moved rather than deleted.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheNormalPathDrawsTheCatalogHullAndBuildsNoFailSafe()
        {
            ShipController ship = CreateShip();
            yield return null;

            var visual = ship.GetComponent<PlayerShipVisualController>();
            Assert.IsNotNull(visual.CurrentVisual, "the Part 24 catalog hull did not load");
            Assert.IsNull(visual.CurrentFallback,
                "a fail-safe hull was built even though the catalog path succeeded");
            Assert.IsFalse(visual.CatalogUnavailable);

            Assert.Greater(visual.CurrentVisual.GetComponentsInChildren<Renderer>(true).Length, 0,
                "the catalog hull drew nothing");
            Assert.AreEqual(1, MountedHulls(ship.gameObject),
                "more than one hull is mounted under the ship root");
        }

        /// <summary>
        /// The raw legacy GLB is not reachable through <c>Resources</c> any more,
        /// and nothing in a live scene pulled it in.
        /// <para>
        /// Checked in play mode as well as in the editor because the two answer
        /// different questions. The editor test proves the asset is not under a
        /// <c>Resources</c> folder; this one proves the running game does not ask
        /// for it, which is what would put it back in the build if a path load
        /// were reintroduced.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheRawLegacyHullIsUnreachableFromARunningScene()
        {
            ShipController ship = CreateShip();
            yield return null;

            Assert.IsNull(Resources.Load<GameObject>("Ship/PlayerShip"),
                "the raw legacy GLB still resolves through Resources at runtime");

            foreach (Transform node in ship.GetComponentsInChildren<Transform>(true))
                Assert.IsFalse(node.name.IndexOf("Legacy Hull", StringComparison.Ordinal) >= 0,
                    "the retired legacy hull is mounted on the ship: " + node.name);
        }

        // ---- the degraded path -----------------------------------------------

        /// <summary>
        /// With no catalog hull on screen, the fail-safe mounts a real, readable
        /// ship — geometry, materials and all — without touching a raw GLB.
        /// <para>
        /// This is the assertion the whole 70 MB saving rests on. If it goes red,
        /// the degraded picture is a primitive capsule rather than a ship, and the
        /// asset should not have left the build.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheFailSafeDrawsARealShipWithoutTheRawGlb()
        {
            PlayerShipVisualController controller = DegradedProbe("fail-safe scene probe");
            yield return null;

            GameObject hull = controller.CreateFallbackHullVisual();
            Assert.IsNotNull(hull, "the fail-safe hull did not mount");
            yield return null;

            Renderer[] renderers = hull.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, "the fail-safe hull has no renderers");
            Assert.IsFalse(renderers.SelectMany(r => r.sharedMaterials).Any(m => m == null),
                "the fail-safe hull has a missing material");
            Assert.IsFalse(renderers.SelectMany(r => r.sharedMaterials)
                    .Any(m => m != null && (m.shader == null
                                            || m.shader.name.IndexOf("InternalError", StringComparison.Ordinal) >= 0)),
                "the fail-safe hull is drawing with the error shader");

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            Assert.Greater(bounds.size.magnitude, 1f,
                "the fail-safe hull collapsed to nothing, so the degraded picture is empty");
        }

        /// <summary>
        /// However the ship is built, it ends up with exactly one visible hull.
        /// <para>
        /// Deliberately indifferent to which path produced it. That is what makes
        /// this the test to watch while the catalog is broken on purpose: the
        /// path-specific tests above are <i>supposed</i> to go red in that state,
        /// and if this one went red with them the fail-safe would not be a
        /// fail-safe at all. It is also the assertion that would have caught the
        /// old behaviour, where a broken catalog threw out of
        /// <c>ShipFactory.Create</c> and the ship simply failed to exist.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AShipAlwaysEndsUpWithExactlyOneVisibleHull()
        {
            ShipController ship = CreateShip();
            yield return null;

            var visual = ship.GetComponent<PlayerShipVisualController>();
            GameObject hull = visual.CurrentVisual != null ? visual.CurrentVisual : visual.CurrentFallback;
            Assert.IsNotNull(hull,
                "the ship has neither a catalog hull nor a fail-safe hull");
            Assert.AreEqual(1, MountedHulls(ship.gameObject),
                "the ship has more than one hull mounted");

            Renderer[] renderers = hull.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, "the ship's hull draws nothing");

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            Assert.Greater(bounds.size.magnitude, 1f, "the ship's hull collapsed to nothing");

            Assert.IsNull(Resources.Load<GameObject>("Ship/PlayerShip"),
                "whatever drew the ship, the raw legacy GLB must not be reachable");
        }

        /// <summary>
        /// The fail-safe carries no collider, camera or light into the scene, and
        /// leaves the gameplay root exactly as it found it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFailSafeIsPresentationOnly()
        {
            PlayerShipVisualController controller = DegradedProbe("fail-safe sanitation probe");
            GameObject host = controller.gameObject;
            Component[] before = host.GetComponents<Component>();

            GameObject hull = controller.CreateFallbackHullVisual();
            Assert.IsNotNull(hull);
            yield return null;

            Assert.IsFalse(hull.GetComponentsInChildren<Collider>(true).Any(c => c.enabled),
                "the fail-safe brought an enabled collider into physics");
            Assert.IsFalse(hull.GetComponentsInChildren<Camera>(true).Any(c => c.enabled),
                "the fail-safe brought an enabled camera");
            Assert.IsFalse(hull.GetComponentsInChildren<Light>(true).Any(l => l.enabled),
                "the fail-safe brought an enabled light into the lighting budget");
            CollectionAssert.AreEqual(before, host.GetComponents<Component>(),
                "the fail-safe changed the component set on the gameplay root");
        }

        /// <summary>
        /// Asking for the fail-safe repeatedly, and asking for it after the catalog
        /// hull already exists, never produces a second hull.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFailSafeNeverDuplicatesAHull()
        {
            ShipController ship = CreateShip();
            yield return null;

            var visual = ship.GetComponent<PlayerShipVisualController>();
            Assert.IsNotNull(visual.CurrentVisual);

            for (int attempt = 0; attempt < 3; attempt++)
                Assert.IsNull(visual.CreateFallbackHullVisual(),
                    "the fail-safe mounted on top of the catalog hull");
            yield return null;

            Assert.AreEqual(1, MountedHulls(ship.gameObject));

            PlayerShipVisualController lone = DegradedProbe("idempotence probe");
            GameObject host = lone.gameObject;
            GameObject first = lone.CreateFallbackHullVisual();
            Assert.AreSame(first, lone.CreateFallbackHullVisual());
            Assert.AreSame(first, lone.CreateFallbackHullVisual());
            yield return null;
            Assert.AreEqual(1, MountedHulls(host));
        }

        // ---- repetition ------------------------------------------------------

        /// <summary>
        /// Building and destroying the ship repeatedly does not grow the resident
        /// renderer, material or texture population.
        /// <para>
        /// The failure this catches is not a leak in the ordinary sense — it is a
        /// fail-safe that mounts alongside the catalog hull on the second spawn,
        /// or a resolver that instantiates rather than reuses. Both show up as a
        /// count that climbs and as nothing else.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator RepeatedShipSpawnsDoNotGrowTheResidentVisualPopulation()
        {
            var hullCounts = new List<int>();
            var rendererCounts = new List<int>();
            var materialCounts = new List<int>();

            for (int round = 0; round < 3; round++)
            {
                ShipController ship = ShipFactory.Create(Vector3.zero, Quaternion.identity);
                yield return null;

                hullCounts.Add(MountedHulls(ship.gameObject));
                Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(true);
                rendererCounts.Add(renderers.Length);
                materialCounts.Add(renderers
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .Select(material => material.GetInstanceID())
                    .Distinct().Count());

                UnityEngine.Object.Destroy(ship.gameObject);
                yield return null;
            }

            CollectionAssert.AreEqual(new[] { hullCounts[0], hullCounts[0], hullCounts[0] }, hullCounts,
                "the number of mounted hulls changed between spawns");
            Assert.AreEqual(rendererCounts[0], rendererCounts[2],
                "the ship's renderer count grew across spawns: " + string.Join(", ", rendererCounts));
            Assert.AreEqual(materialCounts[0], materialCounts[2],
                "the ship's distinct material count grew across spawns: " + string.Join(", ", materialCounts));
        }

        // ---- every shipping group --------------------------------------------

        /// <summary>
        /// Every visual the catalog can hand out instantiates in a live scene,
        /// draws something, and draws it with a real shader.
        /// <para>
        /// The editor suite already loads every prefab; this is the play-mode
        /// counterpart, and it differs in the one way that matters — it asks the
        /// running renderer whether the material resolved, which is where a
        /// stripped shader variant or a platform-specific import failure shows up
        /// and nowhere else.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryCatalogVisualInstantiatesAndDrawsInAScene()
        {
            var host = new GameObject("catalog sweep");
            spawned.Add(host);

            var problems = new List<string>();
            int drawn = 0;

            foreach (NebulaVisualDefinition definition in NebulaVisualCatalog.All)
            {
                GameObject instance = NebulaVisualResolver.Instantiate(definition, host.transform);
                if (instance == null) { problems.Add(definition.VisualId + ": did not instantiate"); continue; }

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) problems.Add(definition.VisualId + ": no renderers");

                foreach (Renderer renderer in renderers)
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) { problems.Add(definition.VisualId + ": null material"); continue; }
                    if (material.shader == null
                        || material.shader.name.IndexOf("InternalError", StringComparison.Ordinal) >= 0)
                        problems.Add($"{definition.VisualId}: {material.name} is on the error shader");
                }

                drawn++;
                NebulaVisualResolver.DestroyInstance(instance);
                if (drawn % 16 == 0) yield return null;
            }

            yield return null;
            Assert.AreEqual(127, drawn, "the catalog sweep did not cover every definition");
            Assert.IsEmpty(problems, string.Join("\n  ", problems.Take(25)));
        }

        /// <summary>
        /// LOD levels switch without changing which materials are on screen.
        /// <para>
        /// Forcing each level in turn and comparing the set of materials the
        /// enabled renderers draw with catches the pop artefact a static
        /// screenshot cannot: a rebind that put LOD1's surface on LOD0's mesh
        /// renders perfectly at one distance and changes appearance at another.
        /// The comparison is per level rather than across them — LOD meshes are
        /// allowed different materials — so what is asserted is that every level
        /// resolves fully, with no nulls and no error shaders.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryLodLevelResolvesItsMaterialsWhenForced()
        {
            var host = new GameObject("lod sweep");
            spawned.Add(host);

            var problems = new List<string>();
            int groupsChecked = 0;

            foreach (NebulaVisualDefinition definition in NebulaVisualCatalog.All)
            {
                GameObject instance = NebulaVisualResolver.Instantiate(definition, host.transform);
                if (instance == null) continue;

                foreach (LODGroup group in instance.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] lods = group.GetLODs();
                    for (int level = 0; level < lods.Length; level++)
                    {
                        group.ForceLOD(level);
                        groupsChecked++;

                        foreach (Renderer renderer in lods[level].renderers)
                        {
                            if (renderer == null)
                            {
                                problems.Add($"{definition.VisualId}: LOD{level} has a missing renderer");
                                continue;
                            }
                            foreach (Material material in renderer.sharedMaterials)
                                if (material == null || material.shader == null
                                    || material.shader.name.IndexOf("InternalError", StringComparison.Ordinal) >= 0)
                                    problems.Add($"{definition.VisualId}: LOD{level} / {renderer.name} "
                                                 + "does not resolve a material");
                        }
                    }
                    group.ForceLOD(-1);
                }

                NebulaVisualResolver.DestroyInstance(instance);
                if (groupsChecked % 40 == 0) yield return null;
            }

            yield return null;
            Assert.Greater(groupsChecked, 150, "the LOD sweep found almost nothing to force");
            Assert.IsEmpty(problems, string.Join("\n  ", problems.Take(25)));
        }

        /// <summary>
        /// A controller held in the state the fail-safe exists for: no catalog
        /// hull, and none arriving.
        /// <para>
        /// The component is disabled rather than merely left alone because
        /// <c>Update</c> calls <c>Refresh</c> every frame and would quietly build
        /// the catalog hull on frame one — which is correct behaviour and exactly
        /// what makes an undisabled probe useless: it would then be testing the
        /// healthy path while claiming to test the degraded one, and the fail-safe
        /// assertions would pass or fail for reasons that have nothing to do with
        /// the fail-safe.
        /// </para>
        /// </summary>
        private PlayerShipVisualController DegradedProbe(string name)
        {
            var host = new GameObject(name);
            spawned.Add(host);
            var controller = host.AddComponent<PlayerShipVisualController>();
            controller.enabled = false;
            return controller;
        }

        private static int MountedHulls(GameObject shipRoot)
        {
            int count = 0;
            foreach (Transform child in shipRoot.transform)
                if (child.GetComponentsInChildren<Renderer>(true).Length > 0
                    && (child.name.StartsWith("Player Ship", StringComparison.Ordinal)
                        || child.name.IndexOf("Hull", StringComparison.Ordinal) >= 0))
                    count++;
            return count;
        }
    }
}
