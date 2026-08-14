using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Visuals
{
    /// <summary>
    /// Flies one delivery capsule from the ship to the surface slot it was
    /// ordered to, purely as scenery.
    /// <para>
    /// The capsule the game actually cares about is the countdown inside
    /// <c>DrillRuntime</c>/<c>StructureRuntime</c>. This presenter reads that
    /// countdown and nothing else: it cannot make a delivery early or late,
    /// cannot fail one, and holds no transaction. When the countdown ends the
    /// proxy goes back to the pool and construction owns the site.
    /// </para>
    /// <para>
    /// One presenter per build, one lease per presenter, and a lease is never
    /// taken again once handed back — so a completed capsule cannot reappear
    /// after a load or a promote.
    /// </para>
    /// </summary>
    public class BuildCapsulePresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        /// <summary>
        /// Presenters built by gameplay code with no route to the session
        /// context wait here until the director adopts them — the same
        /// dispatch queue the structure presenters use, and equally not a
        /// service locator.
        /// </summary>
        private static readonly List<BuildCapsulePresenter> AwaitingAdoption = new List<BuildCapsulePresenter>();

        internal static void AdoptPending(NebulaAnimationDirector director)
        {
            if (director == null) return;

            for (int i = 0; i < AwaitingAdoption.Count; i++)
            {
                BuildCapsulePresenter presenter = AwaitingAdoption[i];
                if (presenter != null) presenter.AttachTo(director);
            }
            AwaitingAdoption.Clear();
        }

        /// <summary>Clears the queue at session start so a new campaign inherits nothing.</summary>
        public static void ResetPending() => AwaitingAdoption.Clear();

        /// <summary>
        /// Presenters still genuinely waiting. Entries whose structure has been
        /// destroyed — a cancelled build, a demoted system — are not counted,
        /// so this never depends on <c>OnDestroy</c> having run first. That is
        /// the same rule the director applies to dead participants.
        /// </summary>
        public static int PendingCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < AwaitingAdoption.Count; i++)
                    if (AwaitingAdoption[i] != null) live++;
                return live;
            }
        }

        private DrillStructure drill;
        private SurfaceStructure structure;

        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private CapsuleProxyLease lease;
        private Transform proxy;

        private Vector3 start;
        private Vector3 slot;
        private Vector3 surfaceNormal;
        private float totalSeconds;
        private bool handedOff;

        /// <summary>The descent phase last resolved. Test-facing.</summary>
        public CapsuleFlightPhase Phase { get; private set; } = CapsuleFlightPhase.Prepared;

        /// <summary>Real progress last read, 0–1. Test-facing.</summary>
        public float Progress { get; private set; }

        /// <summary>True while this presenter is actually holding a proxy.</summary>
        public bool HasProxy => proxy != null && director != null
                                && director.CapsuleProxies != null && director.CapsuleProxies.IsHolder(lease);

        /// <summary>True once the capsule has landed and its proxy has gone back. It never flies again.</summary>
        public bool HandedOff => handedOff;

        public bool IsAlive => this != null && gameObject != null;

        public void BindDrill(DrillStructure drillStructure, Vector3 shipPosition, NebulaAnimationDirector animationDirector)
        {
            drill = drillStructure;
            totalSeconds = drill != null && drill.Runtime != null ? Mathf.Max(0.01f, drill.Runtime.CapsuleRemaining) : 0.01f;
            BindCommon(shipPosition, animationDirector);
        }

        public void BindStructure(SurfaceStructure surfaceStructure, Vector3 shipPosition, NebulaAnimationDirector animationDirector)
        {
            structure = surfaceStructure;
            totalSeconds = structure != null && structure.Runtime != null
                ? Mathf.Max(0.01f, structure.Runtime.CapsuleRemaining) : 0.01f;
            BindCommon(shipPosition, animationDirector);
        }

        private void BindCommon(Vector3 shipPosition, NebulaAnimationDirector animationDirector)
        {
            slot = transform.position;

            // The surface normal is the direction the slot faces: away from the
            // planet centre. This is what the capsule stands up on.
            Transform planetBody = ResolvePlanetBody();
            surfaceNormal = planetBody != null
                ? (slot - planetBody.position).normalized
                : Vector3.up;

            // A restored build has no ship to have come from — the one that
            // sent it is long gone and was never saved. It descends from
            // straight above its slot instead, which is what the player would
            // see if they arrived mid-delivery.
            start = shipPosition.sqrMagnitude > 0f
                ? shipPosition
                : slot + surfaceNormal * Mathf.Max(50f, (slot - (planetBody != null ? planetBody.position : slot)).magnitude * 0.25f);

            // A capsule whose countdown has already finished never flies.
            if (ResolveState() != DrillLifecycle.CapsuleInTransit) handedOff = true;

            if (animationDirector != null) AttachTo(animationDirector);
            else if (!AwaitingAdoption.Contains(this)) AwaitingAdoption.Add(this);
        }

        private Transform ResolvePlanetBody()
        {
            // The structure is parented under its planet, so the parent chain
            // is the honest source — no scene search, no name matching.
            Transform parent = transform.parent;
            return parent != null ? parent : null;
        }

        private void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(director, animationDirector)) return;

            registration.Release();
            director = animationDirector;
            registration = director.Register(this);
        }

        public void VisualTick(float deltaSeconds)
        {
            DrillLifecycle state = ResolveState();

            if (state != DrillLifecycle.CapsuleInTransit)
            {
                // Landed, cancelled, or restored already built: hand the proxy
                // back and never take another for this build.
                ReleaseProxy();
                handedOff = true;
                Phase = CapsuleFlightPhase.ConstructionHandOff;
                return;
            }

            if (handedOff) return;

            Progress = CapsuleFlightProfile.ProgressFromRemaining(ResolveRemaining(), totalSeconds);
            Phase = CapsuleFlightProfile.Phase(Progress);

            if (!EnsureProxy()) return; // the pool said no; the delivery is unaffected

            proxy.position = CapsuleFlightProfile.Position(start, slot, surfaceNormal, Progress);
            proxy.rotation = CapsuleFlightProfile.Orientation(start, slot, surfaceNormal, Progress);
        }

        private bool EnsureProxy()
        {
            if (HasProxy) return true;
            if (director == null || director.CapsuleProxies == null) return false;

            if (!director.CapsuleProxies.TryLease(out lease, out proxy))
            {
                proxy = null;
                return false;
            }
            return true;
        }

        private void ReleaseProxy()
        {
            if (director?.CapsuleProxies != null) director.CapsuleProxies.Release(lease);
            lease = CapsuleProxyLease.None;
            proxy = null;
        }

        private DrillLifecycle ResolveState()
        {
            if (drill != null && drill.Runtime != null) return drill.Runtime.State;
            if (structure != null && structure.Runtime != null) return structure.Runtime.State;
            return DrillLifecycle.Dismantled;
        }

        private float ResolveRemaining()
        {
            if (drill != null && drill.Runtime != null) return drill.Runtime.CapsuleRemaining;
            if (structure != null && structure.Runtime != null) return structure.Runtime.CapsuleRemaining;
            return 0f;
        }

        public void ResetToBasePose() => ReleaseProxy();

        private void OnDisable() => ReleaseProxy();

        private void OnDestroy()
        {
            // Cancel, demote or destroy: the proxy must not be left flying.
            ReleaseProxy();
            registration.Release();
            AwaitingAdoption.Remove(this);
            drill = null;
            structure = null;
            director = null;
        }
    }
}
