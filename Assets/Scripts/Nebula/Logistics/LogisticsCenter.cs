using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Logistics
{
    /// <summary>
    /// MonoBehaviour adapter for the orbital System Logistics Center (VS-011
    /// §2) — mirrors the DrillRuntime/DrillStructure split: <see cref="Runtime"/>
    /// owns pure lifecycle/durability, this class owns everything physical
    /// (position at its deterministic anchor, placeholder visuals) plus the
    /// center's own <see cref="Inventory"/> and dock bookkeeping. Registers
    /// itself in <see cref="LogisticsState"/> so route/HUD/save code can find
    /// the current Full system's single center (if any). VS-014 §6: also a
    /// real <see cref="ICombatTarget"/> — Siege prioritizes it after Command.
    /// </summary>
    public class LogisticsCenter : MonoBehaviour, ICombatTarget
    {
        public string SystemId { get; private set; }
        public LogisticsCenterRuntime Runtime { get; private set; }
        public LogisticsCargo Inventory { get; private set; }

        // --- ICombatTarget (VS-014 §6) ---------------------------------------
        public string TargetId => SystemId + "/logistics_center";
        public TargetCategory Category => TargetCategory.LogisticsCenter;
        public Vector3 AbsolutePosition => transform.position + FloatingOrigin.TotalOffset;
        public bool IsTargetable => Runtime != null && Runtime.State != LogisticsCenterState.Disabled;
        public void ApplyDamage(float rawAmount, DamageProfile profile) => Runtime.ApplyDamage(rawAmount, profile);

        /// <summary>Derived, never separately stored — the count of this system's routes currently Unloading here IS the dock occupancy; a route's own Phase is the single authority.</summary>
        public int DocksInUse
        {
            get
            {
                int count = 0;
                foreach (CollectorRoute route in LogisticsState.Routes)
                    if (route != null && route.SystemId == SystemId && route.Phase == RoutePhase.Unloading) count++;
                return count;
            }
        }

        /// <summary>True if <see cref="Runtime"/> just transitioned Active-&gt;Disabled this Tick (VS-011 §6) — CollectorRouteDirector reacts to this (it has the ShipHangar reference needed to also update the vehicle's own held-cargo fields, not just the route's mirror).</summary>
        public bool JustBecameDisabled { get; private set; }

        /// <summary>Cancel-refund bookkeeping for the initial T1 build (mirrors SurfaceStructure).</summary>
        public int RefundParts { get; private set; }
        public int RefundMetalAlloy { get; private set; }
        public int RefundBiocomposite { get; private set; }

        /// <summary>Cancel-refund bookkeeping for an in-progress T1-&gt;T2 upgrade.</summary>
        public int UpgradeRefundParts { get; private set; }
        public int UpgradeRefundMetalAlloy { get; private set; }
        public int UpgradeRefundBiocomposite { get; private set; }

        private LogisticsCenterState lastState;
        private Transform visualRoot;
        private LogisticsCenterTier visualTier;

        // --- Creation ------------------------------------------------------------
        public static LogisticsCenter Create(SystemManifest manifest, Transform starTransform, Vector3 anchorOffset,
            int refundParts, int refundMetalAlloy, int refundBiocomposite)
        {
            var go = new GameObject("System Logistics Center");
            go.transform.SetParent(starTransform.parent, false);
            go.transform.position = starTransform.position + anchorOffset;

            var center = go.AddComponent<LogisticsCenter>();
            center.SystemId = manifest.SystemId;
            center.Runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            center.Inventory = new LogisticsCargo(LogisticsCenterCatalog.T1.Capacity);
            center.RefundParts = refundParts; center.RefundMetalAlloy = refundMetalAlloy; center.RefundBiocomposite = refundBiocomposite;
            center.lastState = center.Runtime.State;
            center.BuildVisuals();
            LogisticsState.RegisterCenter(center);
            return center;
        }

        /// <summary>Restore-only: rebuilds from saved DTO fields without re-validating or re-charging anything.</summary>
        public static LogisticsCenter Restore(SystemManifest manifest, Transform starTransform, Vector3 anchorOffset,
            LogisticsCenterRuntime runtime, List<(string itemId, float amount)> inventoryItems)
        {
            var go = new GameObject("System Logistics Center");
            go.transform.SetParent(starTransform.parent, false);
            go.transform.position = starTransform.position + anchorOffset;

            var center = go.AddComponent<LogisticsCenter>();
            center.SystemId = manifest.SystemId;
            center.Runtime = runtime;
            center.Inventory = new LogisticsCargo(runtime.Def.Capacity);
            foreach (var (itemId, amount) in inventoryItems) center.Inventory.LoadItem(itemId, amount);
            center.lastState = runtime.State;
            center.BuildVisuals();
            LogisticsState.RegisterCenter(center);
            return center;
        }

        private void OnDestroy() => LogisticsState.UnregisterCenter(this);

        /// <summary>Restore-only: writes the exact saved refund bookkeeping without re-deriving it from the (possibly already-upgraded) tier def.</summary>
        public void RestoreRefunds(int refundParts, int refundMetalAlloy, int refundBiocomposite,
            int upgradeRefundParts, int upgradeRefundMetalAlloy, int upgradeRefundBiocomposite)
        {
            RefundParts = refundParts; RefundMetalAlloy = refundMetalAlloy; RefundBiocomposite = refundBiocomposite;
            UpgradeRefundParts = upgradeRefundParts; UpgradeRefundMetalAlloy = upgradeRefundMetalAlloy; UpgradeRefundBiocomposite = upgradeRefundBiocomposite;
        }

        // --- Tick ------------------------------------------------------------------
        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Tick(dt);
        }

        /// <summary>Advances the runtime; sets <see cref="JustBecameDisabled"/> for exactly the one Tick where State flips Active-&gt;Disabled (the Director reacts, since it alone has the ShipHangar reference to also fix up the vehicle's own held-cargo fields).</summary>
        public void Tick(float dt)
        {
            JustBecameDisabled = false;
            Runtime.Tick(dt);
            RefreshVisualIfNeeded();

            // Capacity effect must always match the currently-active tier (upgrade completion jumps it atomically).
            Inventory.SetCapacity(Runtime.Def.Capacity);

            if (lastState != LogisticsCenterState.Disabled && Runtime.State == LogisticsCenterState.Disabled) JustBecameDisabled = true;
            lastState = Runtime.State;

            UpdateFillWarning();
        }

        /// <summary>VS-011 §3: level (not edge) state for the 80% fill warning — true for the whole time fill stays at/above 80%, false once it drops back under. The caller (ShipLogisticsCenter) is responsible for deduplicating the actual UI message by watching the false-&gt;true transition, so a vehicle unloading for 30 seconds while already over 80% never spams a fresh warning every frame.</summary>
        public bool FillWarningActive { get; private set; }
        public float FillFraction01 => Runtime.Def.Capacity > 0f ? Inventory.Total / Runtime.Def.Capacity : 0f;

        private void UpdateFillWarning()
        {
            float fill = FillFraction01;
            if (fill >= 0.8f) FillWarningActive = true;
            else FillWarningActive = false; // resets once it drops back under 80% so a future re-cross fires again
        }

        // --- Docks -----------------------------------------------------------------
        /// <summary>True if this center currently has a free dock (DocksInUse &lt; DockCount) — informational only; occupation itself happens implicitly the moment a caller sets a route's Phase to Unloading.</summary>
        public bool HasFreeDock => DocksInUse < Runtime.Def.DockCount;

        // --- T1 -> T2 upgrade --------------------------------------------------------
        /// <summary>Starts the upgrade and remembers the 75%-cancel refund amounts for the T2 cost just spent.</summary>
        public void StartUpgrade()
        {
            LogisticsCenterTierDef t2 = LogisticsCenterCatalog.T2;
            UpgradeRefundParts = Mathf.FloorToInt(t2.PartsCost * LogisticsCenterCatalog.RefundFraction);
            UpgradeRefundMetalAlloy = Mathf.FloorToInt(t2.MetalAlloyCost * LogisticsCenterCatalog.RefundFraction);
            UpgradeRefundBiocomposite = Mathf.FloorToInt(t2.BiocompositeCost * LogisticsCenterCatalog.RefundFraction);
            Runtime.StartUpgrade();
        }

        /// <summary>Cancels an in-progress T1-&gt;T2 upgrade: 75% refund of the T2 cost, T1 keeps working uninterrupted (it was never paused).</summary>
        public void CancelUpgrade(LogisticsCargo shipCargo)
        {
            if (!Runtime.Upgrading) return;
            if (shipCargo != null)
            {
                if (UpgradeRefundParts > 0) shipCargo.Add(ProcessingCatalog.BasicPartId, UpgradeRefundParts);
                if (UpgradeRefundMetalAlloy > 0) shipCargo.Add("material.metal_alloy", UpgradeRefundMetalAlloy);
                if (UpgradeRefundBiocomposite > 0) shipCargo.Add("material.biocomposite", UpgradeRefundBiocomposite);
            }
            Runtime.CancelUpgrade();
            UpgradeRefundParts = 0; UpgradeRefundMetalAlloy = 0; UpgradeRefundBiocomposite = 0;
        }

        // --- Cancel / dismantle ------------------------------------------------------
        /// <summary>Cancels the initial T1 build (75% refund, mirrors SurfaceStructure.Cancel) — only valid while Constructing; a safe no-op otherwise.</summary>
        public void Cancel(LogisticsCargo shipCargo)
        {
            if (!Runtime.CanCancelForRefund) return;
            if (shipCargo != null)
            {
                if (RefundParts > 0) shipCargo.Add(ProcessingCatalog.BasicPartId, RefundParts);
                if (RefundMetalAlloy > 0) shipCargo.Add("material.metal_alloy", RefundMetalAlloy);
                if (RefundBiocomposite > 0) shipCargo.Add("material.biocomposite", RefundBiocomposite);
            }
            Destroy(gameObject);
        }

        public bool CanDismantle()
        {
            bool inventoryEmpty = Inventory.IsEmpty;
            bool noLoadedWaitingCollector = true;
            foreach (CollectorRoute route in LogisticsState.Routes)
                if (route != null && route.SystemId == SystemId && route.HeldAmount > 0.0001f) { noLoadedWaitingCollector = false; break; }
            return Runtime.CanDismantle(inventoryEmpty, DocksInUse == 0, noLoadedWaitingCollector);
        }

        public void Dismantle() => Runtime.Dismantle();

        // --- Placeholder visuals ---------------------------------------------------
        /// <summary>Play Mode uses the deferred Destroy() (matches DrillStructure/SurfaceStructure/VehicleAgent's own convention); DestroyImmediate is required outside Play Mode (e.g. an EditMode test constructing a center directly) or Unity logs an Error.</summary>
        private static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        }


        /// <summary>
        /// ANI-05.1.1 presentation read: what this centre is really doing. The
        /// dock queue, the transfer budget and the capacity limit are all read
        /// and never written.
        /// </summary>
        private Nebula.Visuals.MechanicalDrive ResolveMechanicalDrive()
        {
            if (Runtime == null) return Nebula.Visuals.MechanicalDrive.Stowed;
            switch (Runtime.State)
            {
                case LogisticsCenterState.Constructing: return Nebula.Visuals.MechanicalDrive.Stowed;
                case LogisticsCenterState.Active:
                    if (Runtime.Upgrading) return Nebula.Visuals.MechanicalDrive.Holding;
                    return Inventory != null && Inventory.AvailableVolume <= 0.0001f
                        ? Nebula.Visuals.MechanicalDrive.Holding
                        : Nebula.Visuals.MechanicalDrive.Working;
                default: return Nebula.Visuals.MechanicalDrive.Disabled;
            }
        }

        private float ResolveMechanicalCadence() => 1f;

        private void BuildVisuals()
        {
            // ANI-05.1.1: a rebuild replaces the presenter rather than stacking
            // a second one on the same object.
            foreach (Nebula.Visuals.EconomyMechanicalPresenter existing
                     in GetComponents<Nebula.Visuals.EconomyMechanicalPresenter>())
                Destroy(existing);

            visualTier = Runtime.Tier;
            if (NebulaVisualCatalog.TryGetLogisticsCenter(
                    Runtime.Tier, out NebulaVisualDefinition definition))
            {
                GameObject authored = NebulaVisualResolver.Instantiate(
                    definition, transform, instanceName: $"Authored Logistics Center {Runtime.Tier} Visual");
                if (authored != null)
                {
                    visualRoot = authored.transform;
                    gameObject.AddComponent<Nebula.Visuals.EconomyMechanicalPresenter>()
                        .Initialize(definition.VisualId, visualRoot,
                            ResolveMechanicalDrive, ResolveMechanicalCadence);
                    return;
                }
            }

            var fallbackRoot = new GameObject("Logistics Center Visual (Fallback)");
            fallbackRoot.transform.SetParent(transform, false);
            visualRoot = fallbackRoot.transform;

            var hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hub.name = "Center Hub";
            RemoveCollider(hub);
            hub.transform.SetParent(visualRoot, false);
            hub.transform.localScale = new Vector3(60f, 60f, 60f);
            hub.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateLit(new Color(0.55f, 0.75f, 0.9f), 0.6f, 0.7f);

            for (int i = 0; i < 4; i++)
            {
                GameObject dock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dock.name = "Dock Arm " + i;
                RemoveCollider(dock);
                dock.transform.SetParent(visualRoot, false);
                float angle = i * 90f * Mathf.Deg2Rad;
                dock.transform.localPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 90f;
                dock.transform.localScale = new Vector3(70f, 12f, 20f);
                dock.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                dock.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateLit(new Color(0.35f, 0.5f, 0.6f), 0.4f, 0.5f);
            }
        }

        private void RefreshVisualIfNeeded()
        {
            if (visualRoot != null && visualTier == Runtime.Tier) return;
            if (visualRoot != null) NebulaVisualResolver.DestroyInstance(visualRoot.gameObject);
            visualRoot = null;
            BuildVisuals();
        }
    }
}
