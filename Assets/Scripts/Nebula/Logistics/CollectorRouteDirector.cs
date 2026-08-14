using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Logistics
{
    /// <summary>
    /// Central per-frame dock/holding orchestration for every Logistics
    /// Center in the currently Full system (VS-011 §3/§4). Individual
    /// physical travel (ToSource/Loading/ToCenter/Returning) is driven by
    /// each <see cref="VehicleAgent"/> itself; this director owns the parts
    /// that genuinely need one coordinated pass across multiple vehicles at
    /// the same center — dock assignment (deterministic tie-break) and the
    /// center's single aggregate transfer-rate budget, so two vehicles
    /// unloading the same frame can never together exceed the T1/T2 20/40
    /// vol/s cap.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CollectorRouteDirector : MonoBehaviour
    {
        private ShipHangar hangar;

        public void Initialize(ShipHangar shipHangar) => hangar = shipHangar;

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            foreach (LogisticsCenter center in LogisticsState.Centers)
            {
                if (center == null) continue;
                if (center.JustBecameDisabled) ReactToDisabled(center);
                TickCenterDocks(center, dt);
            }
        }

        /// <summary>VS-011 §6: every route at this center pauses; a loaded vehicle first tries to return its cargo to its own source drill (whatever doesn't fit stays held, on the vehicle AND the route's mirror). Nothing is ever deleted. Routes never auto-resume — the player must explicitly resume each one once the center is Active again.</summary>
        private void ReactToDisabled(LogisticsCenter center)
        {
            foreach (CollectorRoute route in LogisticsState.Routes)
            {
                if (route == null || route.SystemId != center.SystemId || route.Paused) continue;
                if (route.HeldAmount > 0.0001f)
                {
                    VehicleAgent vehicle = FindVehicle(route.VehicleId);
                    DrillStructure drill = FindDrill(route.DrillWorldId);
                    if (vehicle != null && drill != null && drill.Runtime.State == DrillLifecycle.Active)
                    {
                        float deposited = drill.Runtime.Deposit(vehicle.HeldAmount);
                        float remaining = vehicle.HeldAmount - deposited;
                        vehicle.HeldAmount = remaining;
                        if (remaining <= 0.0001f) { vehicle.HeldAmount = 0f; vehicle.HeldItemId = null; }
                        route.SetHeld(vehicle.HeldItemId, vehicle.HeldAmount);
                    }
                }
                route.Pause();
            }
        }

        private static DrillStructure FindDrill(string worldId)
        {
            var drills = ExtractionState.Drills;
            for (int i = 0; i < drills.Count; i++)
                if (drills[i] != null && drills[i].WorldId == worldId) return drills[i];
            return null;
        }

        private void TickCenterDocks(LogisticsCenter center, float dt)
        {
            // An empty Holding route (finished unloading while the center was still full) needs
            // no dock to resume — as soon as space exists it just heads back out (VS-011 §3: "merkez
            // doluyken boş Collector yeni kaynak almak için kalkmasın" — released the instant it isn't true anymore).
            foreach (CollectorRoute route in LogisticsState.Routes)
                if (route != null && route.SystemId == center.SystemId && route.Phase == RoutePhase.Holding
                    && route.HeldAmount <= 0.0001f && center.Inventory.AvailableVolume > 0.0001f)
                    route.SetPhase(RoutePhase.ToSource);

            // 1) Promote Queued/loaded-Holding candidates into any free dock, deterministic tie-break.
            var candidates = new List<CollectorRouteService.DockCandidate>();
            foreach (CollectorRoute route in LogisticsState.Routes)
            {
                if (route == null || route.SystemId != center.SystemId) continue;
                bool isHolding = route.Phase == RoutePhase.Holding;
                if (route.Phase != RoutePhase.Queued && !(isHolding && route.HeldAmount > 0.0001f)) continue;
                candidates.Add(new CollectorRouteService.DockCandidate(
                    route.RouteId, route.VehicleId, isHolding, IsSourceDrillFull(route.DrillWorldId), route.Priority, route.DockWaitStartedAt));
            }
            List<CollectorRouteService.DockCandidate> ordered = CollectorRouteService.OrderForDockAssignment(candidates);
            foreach (CollectorRouteService.DockCandidate candidate in ordered)
            {
                if (!center.HasFreeDock) break; // re-derived every iteration — reflects routes already promoted this pass
                CollectorRoute route = FindRoute(candidate.RouteId);
                if (route == null) continue;
                route.EnterUnloading();
            }

            // 2) Ration the aggregate transfer budget across every Unloading route at this center this tick, in canonical vehicle-id order.
            var unloading = new List<CollectorRoute>();
            foreach (CollectorRoute route in LogisticsState.Routes)
                if (route != null && route.SystemId == center.SystemId && route.Phase == RoutePhase.Unloading) unloading.Add(route);
            unloading.Sort((a, b) => string.CompareOrdinal(a.VehicleId, b.VehicleId));

            float remainingBudget = center.Runtime.Def.TransferVolumePerSecond * dt;
            foreach (CollectorRoute route in unloading)
            {
                VehicleAgent vehicle = FindVehicle(route.VehicleId);
                if (vehicle == null) continue;
                if (remainingBudget <= 0.0001f) break;

                string heldItem = vehicle.HeldItemId; float heldAmount = vehicle.HeldAmount;
                float delivered = ResourceTransferService.DeliverToCenter(center.Inventory, ref heldItem, ref heldAmount,
                    vehicle.Def.TransferVolumePerSecond, dt, remainingBudget);
                vehicle.HeldItemId = heldItem; vehicle.HeldAmount = heldAmount;
                route.SetHeld(heldItem, heldAmount);
                remainingBudget -= delivered;

                if (heldAmount <= 0.0001f)
                {
                    // Leaving Unloading here already frees the dock (DocksInUse is derived from Phase).
                    if (center.Inventory.AvailableVolume > 0.0001f) route.SetPhase(RoutePhase.ToSource);
                    else route.EnterHolding(Time.time);
                }
                else if (delivered <= 0.0001f && center.Inventory.AvailableVolume <= 0.0001f)
                {
                    route.EnterHolding(Time.time);
                }
            }
        }

        private static bool IsSourceDrillFull(string drillWorldId)
        {
            var drills = ExtractionState.Drills;
            for (int i = 0; i < drills.Count; i++)
                if (drills[i] != null && drills[i].WorldId == drillWorldId) return drills[i].Runtime.IsFull;
            return false;
        }

        private static CollectorRoute FindRoute(string routeId)
        {
            foreach (CollectorRoute r in LogisticsState.Routes) if (r != null && r.RouteId == routeId) return r;
            return null;
        }

        private VehicleAgent FindVehicle(string vehicleId)
        {
            if (hangar == null) return null;
            foreach (VehicleAgent v in hangar.Vehicles) if (v != null && v.VehicleId == vehicleId) return v;
            return null;
        }
    }
}
