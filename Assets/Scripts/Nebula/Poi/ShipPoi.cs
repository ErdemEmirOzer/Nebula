using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Navigation;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.Poi
{
    /// <summary>
    /// Ship-side POI interaction (VS-012 §8/§9): nearest-target tracking
    /// (mirrors <see cref="ShipExtraction"/>) plus the explicit-commitment
    /// actions (start Anomaly work, lock a Facility choice, send a Scout/
    /// Collector). Locate/identify/Wreck-collection themselves are automatic
    /// proximity ticks owned by <see cref="PoiObject"/> — this class only
    /// ever exposes small, key-simulation-free public methods so tests can
    /// call the exact same API a keypress would (per VS-012 §9).
    /// </summary>
    public class ShipPoi : MonoBehaviour
    {
        public const KeyCode StartOperationKey = KeyCode.F;
        // DT-001: moved off J -- J is now the sole owner of the Test Flight Speed panel toggle.
        public const KeyCode RelayChoiceKey = KeyCode.Z;
        public const KeyCode CancelKey = KeyCode.Escape;

        private ShipController ship;
        private ShipLogistics logistics;
        private ShipHangar hangar;

        public string StatusMessage { get; private set; }
        private float messageUntil;

        public PoiObject TargetPoi { get; private set; }

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            logistics = GetComponent<ShipLogistics>();
            hangar = GetComponent<ShipHangar>();
        }

        private void OnEnable() => NebulaInputRouter.SetContextSource(InputContext.Poi,
            () => TargetPoi != null && !InputGate.Suppressed && !CombatInputContext.WorldInteractionBlocked);

        private void OnDisable() => NebulaInputRouter.SetContextSource(InputContext.Poi, null);

        private void Update()
        {
            if (InputGate.Suppressed || CombatInputContext.WorldInteractionBlocked) return;
            UpdateTarget();

            if (NebulaInputRouter.Pressed(NebulaInputActions.PoiStart)) TryStartPrimaryAction();
            if (NebulaInputRouter.Pressed(NebulaInputActions.PoiSensorRelay)) TryStartSensorRelay();
            // Shared-intent read: Escape stays everybody's cancel, but the key comes
            // from the catalog row rather than from a literal frozen at this call site.
            if (NebulaInputRouter.SharedPressed(NebulaInputActions.PoiCancel)) TryCancelActiveOperation();
        }

        private void UpdateTarget()
        {
            PoiObject best = null;
            float bestSqr = float.MaxValue;
            foreach (var kv in PoiLiveState.States)
            {
                PoiObject obj = PoiObject.Find(kv.Key);
                if (obj == null) continue;
                float sqr = (obj.transform.position - transform.position).sqrMagnitude;
                float interest = PoiCatalog.IdentifyRange * 3f;
                if (sqr < interest * interest && sqr < bestSqr) { bestSqr = sqr; best = obj; }
            }
            TargetPoi = best;
        }

        public float DistanceToTarget() => TargetPoi != null ? Vector3.Distance(transform.position, TargetPoi.transform.position) : float.MaxValue;

        // --- Primary action: start Anomaly work, or Dismantle a Facility ------------
        public bool TryStartPrimaryAction()
        {
            if (TargetPoi == null || TargetPoi.State.Level != PoiKnowledgeLevel.Identified) { SetMessage("HEDEF UYGUN DEĞİL"); return false; }

            if (TargetPoi.Manifest.Type == PoiType.Anomaly)
            {
                bool started = TargetPoi.State.StartOperation(null);
                SetMessage(started ? "ANOMALİ ÇALIŞMASI BAŞLADI" : "BAŞLATILAMADI");
                return started;
            }
            if (TargetPoi.Manifest.Type == PoiType.AbandonedFacility)
            {
                FacilityStartRejectReason reason = FacilityService.CheckStartDismantle(TargetPoi.State);
                if (reason != FacilityStartRejectReason.None) { SetMessage(RejectText(reason)); return false; }
                FacilityService.StartDismantle(TargetPoi.State, null);
                SetMessage("SÖKÜM BAŞLADI — GERİ DÖNÜŞÜMSÜZ");
                return true;
            }
            SetMessage("BU POI İÇİN OTOMATİK (F GEREKMEZ)");
            return false;
        }

        /// <summary>
        /// What the primary action would refuse with right now, or <c>null</c> when it
        /// would go through. Pure: it starts nothing, reserves nothing and publishes
        /// nothing, so a control can ask it five times a second to decide whether it
        /// is interactable and what to print when it is not.
        /// <para>
        /// The same conditions <see cref="TryStartPrimaryAction"/> checks, in the same
        /// order, read out of the same services — not a second opinion. A control that
        /// forms its own view of eligibility is a control that will eventually offer
        /// something the seam then refuses.
        /// </para>
        /// </summary>
        public string PrimaryActionBlockedReason()
        {
            if (TargetPoi == null || TargetPoi.Manifest == null || TargetPoi.State == null) return "MENZİLDE HEDEF YOK";
            if (TargetPoi.State.Level != PoiKnowledgeLevel.Identified) return "HEDEF UYGUN DEĞİL";
            if (TargetPoi.Manifest.Type == PoiType.Anomaly) return null;
            if (TargetPoi.Manifest.Type == PoiType.AbandonedFacility)
            {
                FacilityStartRejectReason reason = FacilityService.CheckStartDismantle(TargetPoi.State);
                return reason == FacilityStartRejectReason.None ? null : RejectText(reason);
            }
            return "BU POI KENDİLİĞİNDEN İŞLER";
        }

        /// <summary>The Sensor Relay counterpart of <see cref="PrimaryActionBlockedReason"/>.</summary>
        public string SensorRelayBlockedReason()
        {
            if (TargetPoi == null || TargetPoi.Manifest == null || TargetPoi.State == null) return "MENZİLDE HEDEF YOK";
            if (TargetPoi.Manifest.Type != PoiType.AbandonedFacility) return "YALNIZCA TERK EDİLMİŞ TESİSTE";
            FacilityStartRejectReason reason = FacilityService.CheckStartRelay(TargetPoi.State, logistics?.Cargo);
            return reason == FacilityStartRejectReason.None ? null : RejectText(reason);
        }

        /// <summary>The verb the primary action performs on the current target, in Turkish.</summary>
        public string PrimaryActionCaption()
        {
            PoiType type = TargetPoi?.Manifest?.Type ?? PoiType.Wreck;
            if (type == PoiType.Anomaly) return "ANOMALİ ÇALIŞMASINI BAŞLAT";
            if (type == PoiType.AbandonedFacility) return "SÖKÜMÜ BAŞLAT";
            return "OPERASYONU BAŞLAT";
        }

        // --- Alternate Facility choice: Sensor Relay ---------------------------------
        public bool TryStartSensorRelay()
        {
            // Was a bare `return false`: pressing the relay key at a Wreck or an
            // Anomaly did nothing and said nothing, which is indistinguishable from a
            // broken key.
            if (TargetPoi == null || TargetPoi.Manifest == null || TargetPoi.Manifest.Type != PoiType.AbandonedFacility)
            { SetMessage("YALNIZCA TERK EDİLMİŞ TESİSTE"); return false; }
            FacilityStartRejectReason reason = FacilityService.CheckStartRelay(TargetPoi.State, logistics?.Cargo);
            if (reason != FacilityStartRejectReason.None) { SetMessage(RejectText(reason)); return false; }
            FacilityService.StartRelay(TargetPoi.State, TargetPoi.Manifest.PoiId, logistics.Cargo, null);
            SetMessage("SENSÖR RÖLESİ BAŞLADI — GERİ DÖNÜŞÜMSÜZ");
            return true;
        }

        /// <summary>Cancels an in-progress (not yet completed) operation back to Identified — never a completed/irreversible Facility choice, which stays locked; this only aborts a still-running timer.</summary>
        public bool TryCancelActiveOperation()
        {
            if (TargetPoi == null || TargetPoi.State.Level != PoiKnowledgeLevel.Active) return false;
            if (TargetPoi.Manifest.Type == PoiType.AbandonedFacility && TargetPoi.State.Choice == FacilityChoice.SensorRelay)
                logistics?.Cargo.ReleaseReservation(FacilityService.RelayOwnerId(TargetPoi.Manifest.PoiId));
            TargetPoi.State.CancelOperationBackToIdentified();
            SetMessage("İPTAL EDİLDİ");
            return true;
        }

        // --- Sending a Scout/Collector to work a POI (VS-012 §8) ---------------------
        public enum SendRejectReason { None, VehicleNotInHangar, NotSameSystem, NoCommandCapacity, WrongVehicleClassForTarget, TargetNotEligible }

        public SendRejectReason TrySendVehicle(VehicleAgent vehicle, string poiId)
        {
            if (vehicle == null || !vehicle.Mission.CanLaunch || vehicle.EngineerTask != null || vehicle.EscortState != null || vehicle.ResupplyTask != null)
                return SendRejectReason.VehicleNotInHangar;
            PoiObject target = PoiObject.Find(poiId);
            if (target == null) return SendRejectReason.TargetNotEligible;

            string systemId = hangar.CurrentSystemId;
            if (systemId == null || Sectors.WorldIdCodec.SystemIdFromPoiWorldId(poiId) != systemId) return SendRejectReason.NotSameSystem;

            bool validClass = vehicle.Def.Class == VehicleClass.Scout
                || (vehicle.Def.Class == VehicleClass.Collector && target.Manifest.Type == PoiType.Wreck && target.Manifest.Risk == PoiRisk.Safe);
            if (!validClass) return SendRejectReason.WrongVehicleClassForTarget;

            if (!hangar.Command.TryReserve(vehicle.Def.CommandCost)) return SendRejectReason.NoCommandCapacity;

            var task = new PoiTask(poiId, vehicle.VehicleId, systemId);
            vehicle.AssignPoiTask(task);
            SetMessage("GÖNDERİLDİ");
            return SendRejectReason.None;
        }

        public void RecallVehicle(VehicleAgent vehicle) => vehicle?.RequestPoiTaskRecall();

        private static string RejectText(FacilityStartRejectReason reason) => reason switch
        {
            FacilityStartRejectReason.NotIdentified => "ÖNCE TANIMLANMALI",
            FacilityStartRejectReason.ChoiceAlreadyLocked => "SEÇİM ZATEN KİLİTLİ",
            FacilityStartRejectReason.InsufficientCargo => "YETERSİZ KARGO",
            _ => "BAŞARISIZ"
        };

        private void SetMessage(string text) { StatusMessage = text; messageUntil = Time.unscaledTime + 3.5f; NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Poi, text); }
        public string TransientMessage => Time.unscaledTime < messageUntil ? StatusMessage : null;
    }
}
