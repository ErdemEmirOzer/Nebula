using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Explicit owner for runtime-created Unity resources. Composition keeps one
    /// scope for the campaign and each materialized system keeps a nested scope;
    /// factories register into the active scope instead of relying on a scene scan
    /// or Resources.UnloadUnusedAssets.
    /// <para>
    /// Two statics carry the whole contract. <c>session</c> is the standing
    /// fallback owner for the campaign currently composed; <c>current</c> is the
    /// innermost <see cref="Activate"/> block. Every write to either is
    /// identity-checked, because both an outgoing session and an outgoing nested
    /// scope can be disposed AFTER their replacement has already installed itself
    /// — deferred destruction is the normal case here, not the exception.
    /// </para>
    /// </summary>
    public sealed class RuntimeResourceScope : IDisposable
    {
        private static RuntimeResourceScope current;
        private static RuntimeResourceScope session;
        private static int suppressionDepth;

        /// <summary>
        /// How to end the session that owns <see cref="session"/>, installed once by
        /// <c>NebulaSessionLifetime</c>'s static constructor.
        /// <para>
        /// This class can release resources but it cannot deactivate the renderers
        /// still drawing with them — it does not know the session's root GameObjects.
        /// Only the lifetime does, and only it can run the two halves in the order
        /// that makes them safe. The dependency points this way, as a hook rather
        /// than a reference, because <c>Nebula.World.Composition</c> already depends
        /// on <c>Nebula.Visuals</c> and not the other way round.
        /// </para>
        /// </summary>
        private static System.Action endStandingSession;

        /// <summary>
        /// Registers the one implementation of "end the session that is standing".
        /// Called from <c>NebulaSessionLifetime</c>'s static constructor, which by
        /// construction runs before any session scope can exist — a standing
        /// <see cref="session"/> can only have been created by a
        /// <c>NebulaSessionLifetime</c> instance, and constructing one runs that
        /// static constructor first.
        /// </summary>
        public static void InstallStandingSessionTerminator(System.Action terminator)
            => endStandingSession = terminator;

        private readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
        private readonly HashSet<int> instanceIds = new HashSet<int>();
        private bool disposed;

        public int Count => resources.Count;

        /// <summary>
        /// True once <see cref="Dispose"/> has run. A disposed scope owns nothing,
        /// accepts nothing and can never be reopened.
        /// </summary>
        public bool IsDisposed => disposed;

        /// <summary>
        /// Installs the session-lifetime fallback owner and releases the one it
        /// replaces.
        /// <para>
        /// The standing session is ended through its OWN owner first
        /// (<see cref="endStandingSession"/>), not amputated here. Releasing
        /// resources is only half a teardown: this method cannot deactivate the
        /// renderers still drawing with them, and destroying a Material out from
        /// under a live <c>LateUpdate</c> is a MissingReferenceException every frame
        /// rather than a leak. That is measured, twice, on the same line —
        /// <c>GalaxyDustFader.LateUpdate</c>, <c>GalaxyGenerator.cs:433</c>: first
        /// via <c>TutorialSession</c> constructing a second lifetime, and then again
        /// via <b>direct</b> callers of this method
        /// (<c>LifecycleOwnershipRegressionTests.cs:173</c>,
        /// <c>AuditPart2LifecycleDiagnosticTests.cs:673/694/722/762</c>), which
        /// bypass the lifetime's own handover entirely. Fixing only the first left
        /// fifteen reds cascading across eight fixtures the moment a shard happened
        /// to schedule a world-booting test immediately before one of them.
        /// </para>
        /// <para>
        /// The release below is therefore the genuine last resort it always claimed
        /// to be: it is reached only by a scope with no lifetime behind it at all,
        /// which owns no roots and so has nothing to deactivate. Overwriting the
        /// field instead would not "replace an owner" — it would delete the only
        /// reference through which those Materials, Textures and Meshes could EVER
        /// be destroyed.
        /// </para>
        /// <para>
        /// Rejecting instead was considered and refused. The production caller of
        /// this is a constructor (<c>NebulaSessionLifetime</c>), so throwing would
        /// convert a leak into a failure to build the next campaign at all — and it
        /// would leave the orphaned resources exactly where they were.
        /// </para>
        /// </summary>
        public static RuntimeResourceScope BeginSession()
        {
            // Deliberately before `outgoing` is read: a successful teardown clears
            // `session` through Dispose's identity check, so the orphan release below
            // then sees null and correctly does nothing.
            if (session != null) endStandingSession?.Invoke();

            RuntimeResourceScope outgoing = session;
            var scope = new RuntimeResourceScope();
            // Installed BEFORE the outgoing scope drains, deliberately: anything a
            // late OnDestroy creates while that teardown runs then lands in the live
            // session instead of nowhere, and the outgoing Dispose's own identity
            // guard sees a session field that is no longer its own and refuses to
            // clear it. Disposing first and assigning second would invert both.
            session = scope;
            outgoing?.Dispose();
            return scope;
        }

        public static T Own<T>(T resource) where T : UnityEngine.Object
        {
            if (suppressionDepth > 0) return resource;
            (ActiveScope() ?? session)?.Add(resource);
            return resource;
        }

        /// <summary>
        /// Registers only when a nested concrete owner (for example a
        /// MaterializedSystem) is active; otherwise the caller's own session
        /// cache remains the owner.
        /// </summary>
        public static T OwnInActiveScope<T>(T resource) where T : UnityEngine.Object
        {
            if (suppressionDepth == 0) ActiveScope()?.Add(resource);
            return resource;
        }

        /// <summary>
        /// The innermost activated scope that is still able to own something.
        /// <para>
        /// A scope disposed while it was still <c>current</c> declines ownership
        /// (see <see cref="Activate"/>), and declining must not silently mean
        /// "nobody owns this": <see cref="Own"/> falls through to the session
        /// scope, so a resource created in the deferred window after a nested owner
        /// ended still has a bounded owner instead of leaking for the rest of the
        /// process. That is the same answer <see cref="Activate"/> already gives
        /// when a torn-down scope is activated — it never installs itself, so
        /// <c>Own</c> reaches the session — stated once, here.
        /// </para>
        /// </summary>
        private static RuntimeResourceScope ActiveScope()
        {
            RuntimeResourceScope active = current;
            return active != null && !active.disposed ? active : null;
        }

        /// <summary>For explicitly application-lifetime caches only.</summary>
        public static IDisposable SuppressOwnership()
        {
            suppressionDepth++;
            return new Suppression();
        }

        /// <summary>
        /// A torn-down owner declines ownership instead of throwing. Deferred
        /// destruction means a director whose scope was already disposed can still
        /// be reached for one more frame; refusing to own what it creates is
        /// correct there, crashing the caller is not.
        /// </summary>
        public IDisposable Activate()
        {
            if (disposed) return new Activation(null, current);
            RuntimeResourceScope previous = current;
            current = this;
            return new Activation(this, previous);
        }

        private void Add(UnityEngine.Object resource)
        {
            if (disposed || resource == null || !instanceIds.Add(resource.GetInstanceID())) return;
            resources.Add(resource);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Only the session field is this scope's to clear, and only while it is
            // still ITS entry: an older scope disposing late must never null out the
            // session that has already replaced it.
            if (ReferenceEquals(session, this)) session = null;

            // `current` is deliberately NOT touched. It belongs to the
            // Activate/Activation stack, and clearing it here silently LOST THE
            // PARENT scope: an Activation restores `previous` only while `current`
            // is still the scope it installed, so a nested scope disposed inside its
            // own using-block left `current` null, the enclosing Activation then
            // declined to restore, and every later Own in the parent block fell
            // through to the session instead of the parent. A disposed scope left
            // standing as `current` is harmless — ActiveScope() skips it — and the
            // enclosing Activation restores the parent exactly as it always would.

            for (int i = resources.Count - 1; i >= 0; i--) Release(resources[i]);
            resources.Clear();
            instanceIds.Clear();
        }

        /// <summary>
        /// Releases one runtime-created resource the way this class always releases
        /// them, for the call sites that have to let go of something before a scope
        /// ends. <c>SkyboxBaker</c>'s fallback branch is the case that named
        /// this: it allocates the cube <c>RenderTexture</c> before it knows whether
        /// the bake will succeed, so on failure it must destroy a resource no scope
        /// ever took ownership of — and doing that with a bare <c>Destroy</c> is the
        /// one thing a RenderTexture must not receive.
        /// <para>
        /// Public so the contract has exactly one implementation. A second hand-rolled
        /// "release then destroy" is how the first one drifted.
        /// </para>
        /// </summary>
        public static void Release(UnityEngine.Object resource)
        {
            if (resource == null) return;
            // A RenderTexture holds a native GPU surface that its managed object
            // does not own on its own terms, and destroying the currently-active
            // target without releasing it first is what produces the "RenderTexture
            // was destroyed while active" complaint — and leaves the surface itself
            // to be reclaimed whenever the driver gets round to it rather than now.
            // Every other owner of a RenderTexture in this project
            // (StrategicBodyPortrait, StrategicMapUniverseRenderer) releases before
            // destroying; the session's skybox cube reaches its owner through here.
            if (resource is RenderTexture renderTexture)
            {
                if (ReferenceEquals(RenderTexture.active, renderTexture)) RenderTexture.active = null;
                renderTexture.Release();
            }
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }

        private sealed class Activation : IDisposable
        {
            private RuntimeResourceScope owner;
            private readonly RuntimeResourceScope previous;

            public Activation(RuntimeResourceScope owner, RuntimeResourceScope previous)
            {
                this.owner = owner;
                this.previous = previous;
            }

            public void Dispose()
            {
                if (owner == null) return;
                RuntimeResourceScope activated = owner;
                owner = null;
                // Restore only from the state this activation itself installed. If
                // `current` is some other scope, a newer activation is still live
                // and unwinding an older one over it would corrupt it; leaving it
                // alone costs nothing, because that newer activation carries the
                // right `previous` of its own.
                if (ReferenceEquals(current, activated)) current = previous;
            }
        }

        private sealed class Suppression : IDisposable
        {
            private bool active = true;
            public void Dispose()
            {
                if (!active) return;
                active = false;
                suppressionDepth--;
            }
        }
    }
}
