using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Tutorial
{
    /// <summary>
    /// The concrete lifetime of one tutorial step.
    /// <para>
    /// Modelled on <c>NebulaSessionLifetime</c> rather than invented: every
    /// tracked object is destroyed in exact reverse creation order, cleanup is
    /// idempotent, and a second <see cref="Dispose"/> is a silent no-op. What
    /// this adds is the guarantee the contract asks for -- the director disposes
    /// the scope on success, on abandonment, and on an exception thrown out of a
    /// step -- so a step never writes its own teardown and therefore never has
    /// teardown that only runs on the happy path.
    /// </para>
    /// <para>
    /// Cleanup callbacks run first and objects second: a callback typically
    /// unsubscribes an event or hands a pooled lease back, and both of those
    /// must happen while the thing they refer to still exists.
    /// </para>
    /// </summary>
    public sealed class TutorialStepScope : ITutorialStepScope, IDisposable
    {
        private readonly List<UnityEngine.Object> tracked = new List<UnityEngine.Object>();
        private readonly List<Action> cleanups = new List<Action>();
        private GameObject rootObject;

        /// <summary>Every exception a cleanup callback threw, in the order they were thrown. Empty on a clean teardown.</summary>
        private readonly List<Exception> cleanupFailures = new List<Exception>();

        public Transform Root => rootObject != null ? rootObject.transform : null;

        public bool IsDisposed { get; private set; }

        /// <summary>Test-facing: how many objects are still waiting to be destroyed.</summary>
        public int TrackedCount => tracked.Count;

        /// <summary>Test-facing: how many cleanup callbacks are still registered.</summary>
        public int CleanupCount => cleanups.Count;

        /// <summary>Test-facing: cleanup callbacks that threw during the last <see cref="Dispose"/>.</summary>
        public IReadOnlyList<Exception> CleanupFailures => cleanupFailures;

        /// <summary>
        /// Creates a step root under <paramref name="parent"/>. The name carries
        /// the step id so a leaked root is identifiable in the hierarchy by the
        /// step that leaked it, rather than being one of forty identical objects.
        /// </summary>
        public static TutorialStepScope Create(string stepId, Transform parent)
        {
            var scope = new TutorialStepScope();
            scope.rootObject = new GameObject($"Tutorial Step :: {stepId ?? "?"}");
            if (parent != null) scope.rootObject.transform.SetParent(parent, false);
            return scope;
        }

        /// <summary>
        /// Adopts an object for the length of the step.
        /// <para>
        /// A <see cref="GameObject"/> is adopted whole: parented under the step
        /// root if it was a scene root, and destroyed at cleanup. That parenting
        /// is what makes "everything the step spawns lives under the step root"
        /// true rather than aspirational -- a spawned object left as a scene root
        /// would survive the scope that believed it owned it. World position is
        /// preserved, because a step places things at real world anchors and
        /// adopting one must not move it.
        /// </para>
        /// <para>
        /// Anything else -- a <see cref="Component"/>, a material, a texture -- is
        /// recorded and destroyed on its own, and its GameObject is deliberately
        /// left alone. Tracking a component must mean "remove this component",
        /// never "adopt whatever it happens to be attached to": a step that adds a
        /// highlighter to the player's ship and tracked it would otherwise reparent
        /// the ship under the step root and destroy it at the end of the step.
        /// </para>
        /// </summary>
        public T Track<T>(T trackedObject) where T : UnityEngine.Object
        {
            if (trackedObject == null) return trackedObject;

            if (trackedObject is GameObject go && rootObject != null && go != rootObject)
            {
                if (RefuseSessionObject(go)) return trackedObject;
                if (go.transform.parent == null) go.transform.SetParent(rootObject.transform, true);
            }

            tracked.Add(trackedObject);
            return trackedObject;
        }

        /// <summary>
        /// The one adoption that must never happen. The player's ship is a scene
        /// root owned by the session's own lifetime, so adopting it would both
        /// reparent it under the step and destroy it when the step ended -- and
        /// the step after that would have no ship. Refused loudly rather than
        /// silently ignored, because the step that asked is wrong.
        /// </summary>
        private static bool RefuseSessionObject(GameObject candidate)
        {
            if (candidate.GetComponent<Ship.ShipController>() == null) return false;
            Debug.LogError("[Tutorial] A step tried to Track the player ship. " +
                           "The scope owns only what the step created; refused, and the ship is untouched.");
            return true;
        }

        public void OnCleanup(Action release)
        {
            if (release == null) return;
            cleanups.Add(release);
        }

        public GameObject NewGroup(string name)
        {
            var group = new GameObject(string.IsNullOrEmpty(name) ? "Group" : name);
            if (rootObject != null) group.transform.SetParent(rootObject.transform, false);
            tracked.Add(group);
            return group;
        }

        /// <summary>
        /// Runs every registered cleanup, then destroys every tracked object and
        /// the step root, newest first. Idempotent.
        /// <para>
        /// Each callback is isolated: one that throws is recorded in
        /// <see cref="CleanupFailures"/> and the rest still run. A single bad
        /// unsubscribe must not strand thirty spawned objects and a pooled lease
        /// -- that is precisely the leak this class exists to prevent.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            cleanupFailures.Clear();
            for (int i = cleanups.Count - 1; i >= 0; i--)
            {
                try { cleanups[i]?.Invoke(); }
                catch (Exception error)
                {
                    cleanupFailures.Add(error);
                    Debug.LogException(error);
                }
            }
            cleanups.Clear();

            for (int i = tracked.Count - 1; i >= 0; i--) DestroyNow(tracked[i]);
            tracked.Clear();

            DestroyNow(rootObject);
            rootObject = null;
        }

        /// <summary>
        /// <c>Destroy</c> is deferred and simply errors outside play mode, which is
        /// why <c>NebulaMainMenuHost.DestroyNow</c> exists and why this repeats it:
        /// an EditMode test that runs a step and disposes its scope must end with
        /// the objects actually gone, not queued behind a frame that never comes.
        /// Deactivated first so every <c>OnDisable</c> fires synchronously before
        /// the object is torn down, exactly as <c>NebulaSessionLifetime</c> does.
        /// </summary>
        private static void DestroyNow(UnityEngine.Object target)
        {
            if (target == null) return;

            if (target is GameObject go) go.SetActive(false);
            else if (target is Behaviour behaviour) behaviour.enabled = false;

            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
