using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Visuals
{
    /// <summary>
    /// Draws what a support vehicle is really doing: the Scout's scan, the
    /// Engineer's repair or recovery, the Escort's standing orders, and one
    /// representative item for every transfer that actually moved cargo.
    /// <para>
    /// Everything is read. The scan counter, the eight-second safety gate, the
    /// transfer rate, the amount moved, the hull restored, the parts consumed
    /// and the XP are all owned elsewhere and are never written here. A vehicle
    /// that gets no proxy and no effect — because the pool is full, or the item
    /// has no authored visual — completes exactly the same transfer.
    /// </para>
    /// </summary>
    public class SupportOperationPresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        /// <summary>How long one representative item takes to cross, in seconds.</summary>
        private const float ProxyFlightSeconds = 0.55f;

        private static readonly List<SupportOperationPresenter> AwaitingAdoption =
            new List<SupportOperationPresenter>();

        internal static void AdoptPending(NebulaAnimationDirector director)
        {
            if (director == null) return;

            for (int i = 0; i < AwaitingAdoption.Count; i++)
            {
                SupportOperationPresenter presenter = AwaitingAdoption[i];
                if (presenter != null) presenter.AttachTo(director);
            }
            AwaitingAdoption.Clear();
        }

        public static void ResetPending() => AwaitingAdoption.Clear();

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

        private VehicleAgent agent;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private Camera billboardCamera;

        private float visualRadius = 4f;

        // --- the leased effect ---
        private SupportOperationLease vfxLease = SupportOperationLease.None;
        private SupportOperationVisual activeVisual = SupportOperationVisual.None;

        // --- one representative item in flight ---
        private ItemProxyLease proxyLease = ItemProxyLease.None;
        private Vector3 proxyFrom;
        private Vector3 proxyTo;
        private float proxyElapsed;

        // --- what we saw last tick, to spot a real change ---
        private float lastHeldAmount;
        private string lastHeldItemId;
        private float lastScanProgress;
        private float lastRestoredHp;
        private float lastConsumedTotal;
        private bool primed;

        /// <summary>What is being drawn right now. Test-facing.</summary>
        public SupportOperationVisual CurrentVisual => activeVisual;

        /// <summary>True while one representative item is crossing. Test-facing.</summary>
        public bool HasItemInFlight => proxyLease.IsValid;

        /// <summary>The direction of the last real transfer this vehicle performed. Test-facing.</summary>
        public TransferDirection LastTransfer { get; private set; } = TransferDirection.None;

        /// <summary>How many real transfers this vehicle has shown. Test-facing; never a gameplay count.</summary>
        public int TransfersShown { get; private set; }

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(VehicleAgent vehicleAgent, NebulaAnimationDirector animationDirector)
        {
            agent = vehicleAgent;
            MeasureVisual();

            if (animationDirector != null) AttachTo(animationDirector);
            else if (!AwaitingAdoption.Contains(this)) AwaitingAdoption.Add(this);
        }

        private void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(director, animationDirector)) return;

            registration.Release();
            director = animationDirector;
            registration = director.Register(this);
        }

        /// <summary>
        /// Sizes the effect from the model that is actually there. With no
        /// per-part anchors on any vehicle prefab, the hull's own bounds are the
        /// honest reference — nothing is invented.
        /// </summary>
        private void MeasureVisual()
        {
            var renderer = GetComponentInChildren<Renderer>(true);
            visualRadius = renderer != null ? Mathf.Max(1f, renderer.bounds.extents.magnitude) : 4f;
        }

        public void VisualTick(float deltaSeconds)
        {
            if (agent == null || agent.Def == null) { ClearAll(); return; }

            if (!isActiveAndEnabled)
            {
                // Docked, demoted, or otherwise off-stage: hand everything back.
                ClearAll();
                primed = false;
                return;
            }

            SampleTransfers();
            UpdateVisual();
            AdvanceProxy(deltaSeconds);
        }

        // --- what the vehicle really did since last tick ----------------------

        /// <summary>
        /// Watches the vehicle's own cargo. Every one of the five transfer
        /// routes — drill, logistics centre either way, the player ship, the
        /// Mk II reserve run, and a salvaged wreck — moves this same field
        /// through ResourceTransferService, so one reading covers them all
        /// without a second transfer authority.
        /// </summary>
        private void SampleTransfers()
        {
            float held = agent.HeldAmount;
            string heldId = agent.HeldItemId;

            if (!primed)
            {
                // A restored or newly promoted vehicle adopts its cargo
                // silently: a load never replays transfers that already happened.
                lastHeldAmount = held;
                lastHeldItemId = heldId;
                lastScanProgress = agent.ScanProgress01;
                lastRestoredHp = agent.EngineerTask != null ? agent.EngineerTask.ActualRestoredHpSoFar : 0f;
                lastConsumedTotal = ConsumedTotal();
                primed = true;
                return;
            }

            // A change of item is a new load, not a delta on the old one.
            float delta = !string.Equals(heldId, lastHeldItemId, System.StringComparison.Ordinal)
                ? (held > 0f ? held : -lastHeldAmount)
                : held - lastHeldAmount;

            TransferDirection direction = SupportOperationProfiles.Direction(delta);
            if (direction != TransferDirection.None)
            {
                // Outbound cargo has usually already been cleared from the
                // vehicle, so the item that left is the one we remembered.
                string movedId = direction == TransferDirection.Inbound ? heldId : lastHeldItemId;
                LaunchRepresentativeProxy(movedId, direction);
            }

            lastHeldAmount = held;
            lastHeldItemId = heldId;

            // An Engineer's parts and material leave the same way.
            float consumed = ConsumedTotal();
            if (consumed - lastConsumedTotal > SupportOperationProfiles.TransferEpsilon)
                LaunchRepresentativeProxy(ConsumedItemId(), TransferDirection.Outbound);
            lastConsumedTotal = consumed;
        }

        private float ConsumedTotal()
        {
            EngineerMissionState task = agent.EngineerTask;
            return task == null ? 0f : task.ConsumedParts + task.ConsumedMaterialAmount;
        }

        private string ConsumedItemId()
        {
            EngineerMissionState task = agent.EngineerTask;
            if (task == null) return null;
            return !string.IsNullOrEmpty(task.ChosenMaterialId) ? task.ChosenMaterialId : ProcessingCatalog.BasicPartId;
        }

        /// <summary>
        /// Sends exactly one item across, however large the transfer was. Ten
        /// thousand units of ore is still one crate on screen — a proxy per unit
        /// would be an object storm, not information.
        /// </summary>
        private void LaunchRepresentativeProxy(string itemId, TransferDirection direction)
        {
            LastTransfer = direction;

            if (string.IsNullOrEmpty(itemId)) return;
            if (proxyLease.IsValid) return;         // one representative at a time
            if (director == null || director.ItemProxies == null) return;
            if (!director.ItemProxies.TryLease(itemId, out ItemProxyLease lease, out Transform proxy)) return;
            if (proxy == null) { director.ItemProxies.Release(lease); return; }

            // With no cargo-bay node on any vehicle prefab, the honest ends are
            // the hull's own centre and the direction it is actually facing —
            // which is the thing it flew to and stopped at.
            Vector3 hull = HullCentre();
            Vector3 outside = hull + transform.forward * (visualRadius * 2.5f);

            proxyLease = lease;
            proxyFrom = direction == TransferDirection.Inbound ? outside : hull;
            proxyTo = direction == TransferDirection.Inbound ? hull : outside;
            proxyElapsed = 0f;
            TransfersShown++;

            proxy.SetPositionAndRotation(proxyFrom, transform.rotation);
        }

        private void AdvanceProxy(float deltaSeconds)
        {
            if (!proxyLease.IsValid) return;
            if (director == null || director.ItemProxies == null) { ReleaseProxy(); return; }
            if (!director.ItemProxies.TryGetProxy(proxyLease, out Transform proxy) || proxy == null) { ReleaseProxy(); return; }

            proxyElapsed += deltaSeconds;
            float t = Mathf.Clamp01(proxyElapsed / ProxyFlightSeconds);
            proxy.position = Vector3.Lerp(proxyFrom, proxyTo, t);

            if (t >= 1f) ReleaseProxy();
        }

        private void ReleaseProxy()
        {
            if (!proxyLease.IsValid) return;
            director?.ItemProxies?.Release(proxyLease);
            proxyLease = ItemProxyLease.None;
            proxyElapsed = 0f;
        }

        // --- the operation effect ---------------------------------------------

        private void UpdateVisual()
        {
            SupportOperationVisual wanted = ResolveVisual();

            if (wanted != activeVisual)
            {
                ReleaseVfx();
                activeVisual = wanted;
            }

            if (!SupportOperationProfiles.HasVfx(activeVisual)) return;
            if (AnimationAccessibility.MotionOff) { ReleaseVfx(); return; }
            if (director == null || director.SupportOperations == null) return;

            if (!vfxLease.IsValid)
            {
                // A full pool simply means no scenery for this one. The scan
                // still completes on schedule.
                if (!director.SupportOperations.TryLease(
                        SupportOperationProfiles.Vfx(activeVisual), out SupportOperationLease lease, out _))
                    return;
                vfxLease = lease;
            }

            if (!director.SupportOperations.TryGetQuad(vfxLease, out Transform quad) || quad == null)
            {
                vfxLease = SupportOperationLease.None;
                return;
            }

            PlaceQuad(quad);
        }

        /// <summary>
        /// Reads the four mutually exclusive mission kinds in the game's own
        /// order of precedence. A dead vehicle shows nothing of its own — the
        /// Engineer recovering it is the one that draws.
        /// </summary>
        private SupportOperationVisual ResolveVisual()
        {
            if (agent.DurabilityRef != null && agent.DurabilityRef.Disabled) return SupportOperationVisual.None;

            if (agent.EngineerTask != null)
            {
                EngineerMissionState task = agent.EngineerTask;
                bool healing = task.ActualRestoredHpSoFar > lastRestoredHp + 1e-4f;
                lastRestoredHp = task.ActualRestoredHpSoFar;
                return SupportOperationProfiles.ForEngineer(task.Phase, task.WorkKind, healing);
            }

            if (agent.EscortState != null)
                return SupportOperationProfiles.ForEscort(agent.EscortState.SubState);

            if (agent.PoiTask != null)
                return SupportOperationProfiles.ForPoi(agent.Def.Class, PoiPhaseOf(agent.PoiTask));

            if (agent.Def.Class == VehicleClass.Scout)
            {
                float progress = agent.ScanProgress01;
                bool advancing = progress > lastScanProgress + 1e-5f && progress < 1f;
                lastScanProgress = progress;
                return SupportOperationProfiles.ForScout(advancing);
            }

            return SupportOperationVisual.None;
        }

        private static PoiTaskPhaseKind PoiPhaseOf(Nebula.Poi.PoiTask task)
        {
            switch (task.Phase)
            {
                case Nebula.Poi.PoiTaskPhase.Working: return PoiTaskPhaseKind.Working;
                case Nebula.Poi.PoiTaskPhase.Returning: return PoiTaskPhaseKind.Returning;
                default: return PoiTaskPhaseKind.ToTarget;
            }
        }

        private Vector3 HullCentre()
        {
            var renderer = GetComponentInChildren<Renderer>(true);
            return renderer != null ? renderer.bounds.center : transform.position;
        }

        /// <summary>
        /// Puts the effect where the work is. When the target is a real vehicle
        /// we know exactly where it is; otherwise the effect sits just ahead of
        /// the operating vehicle, which is the direction it flew to and stopped
        /// facing. No docking port or bay geometry is invented.
        /// </summary>
        private void PlaceQuad(Transform quad)
        {
            Vector3 centre = HullCentre();

            if (SupportOperationProfiles.DrawsOnTarget(activeVisual))
            {
                if (TryResolveTargetCentre(out Vector3 targetCentre)) centre = targetCentre;
                else centre = centre + transform.forward * (visualRadius * 2f);
            }

            quad.position = centre;
            quad.localScale = Vector3.one * (visualRadius * 2f);

            if (billboardCamera == null) billboardCamera = Camera.main;
            if (billboardCamera != null)
                quad.rotation = Quaternion.LookRotation(quad.position - billboardCamera.transform.position, Vector3.up);
        }

        private bool TryResolveTargetCentre(out Vector3 centre)
        {
            centre = Vector3.zero;

            string targetId = null;
            if (agent.EngineerTask != null) targetId = agent.EngineerTask.TargetId;
            else if (agent.EscortState != null) targetId = agent.EscortState.ProtectedTargetId;

            if (string.IsNullOrEmpty(targetId)) return false;
            if (!VehicleAgent.Registry.TryGetValue(targetId, out VehicleAgent target) || target == null) return false;

            var renderer = target.GetComponentInChildren<Renderer>(true);
            centre = renderer != null ? renderer.bounds.center : target.transform.position;
            return true;
        }

        private void ReleaseVfx()
        {
            if (!vfxLease.IsValid) return;
            director?.SupportOperations?.Release(vfxLease);
            vfxLease = SupportOperationLease.None;
        }

        /// <summary>Hands back every visual this vehicle holds. Safe to call repeatedly.</summary>
        public void ClearAll()
        {
            ReleaseVfx();
            ReleaseProxy();
            activeVisual = SupportOperationVisual.None;
        }

        /// <summary>
        /// This presenter poses nothing — it only holds leases — so returning to
        /// base means handing them all back.
        /// </summary>
        public void ResetToBasePose() => ClearAll();

        private void OnDisable() => ClearAll();

        private void OnDestroy()
        {
            registration.Release();
            ClearAll();
            AwaitingAdoption.Remove(this);
            agent = null;
            director = null;
        }
    }
}
