using System.Collections.Generic;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Logistics
{
    /// <summary>One route feeding a centre, as a screen needs to read it.</summary>
    public struct CentreRouteReading
    {
        public string DrillWorldId;
        public RoutePhase Phase;
        public RouteWaitReason WaitReason;
        public string HeldItemId;
        public float HeldAmount;
    }

    /// <summary>
    /// One System Logistics Centre as a screen needs to read it, from whichever
    /// of the game's two existing authorities currently owns it.
    /// <para>
    /// <see cref="IsMaterialized"/> is the whole distinction: a centre in the
    /// system the ship is standing in is a live <see cref="LogisticsCenter"/>
    /// MonoBehaviour and every field here is this frame's truth. A centre in any
    /// other system exists only as the <c>LogisticsCenterDto</c> inside that
    /// system's persisted <see cref="SystemDelta"/>, so every field here is
    /// whatever was true when that system was last streamed out. Nothing is
    /// invented for the second case and nothing is ticked: an unmaterialized
    /// system does not run.
    /// </para>
    /// </summary>
    public sealed class LogisticsCentreReading
    {
        public string SystemId;

        /// <summary>The live object, or <c>null</c> when this reading came from a delta.</summary>
        public LogisticsCenter Live;

        /// <summary>True when the reading is this frame's live state rather than a stored snapshot.</summary>
        public bool IsMaterialized => Live != null;

        public LogisticsCenterTier Tier;
        public LogisticsCenterState State;
        public bool Upgrading;
        public float UpgradeRemaining;
        public float BuildRemaining;

        public float StoredVolume;
        public float Capacity;
        public float Fill01 => Capacity > 0f ? StoredVolume / Capacity : 0f;

        /// <summary>True at or above the same 80% the centre's own warning uses.</summary>
        public bool FillWarningActive => Fill01 >= 0.8f;

        public int DocksInUse;
        public int DockCount;
        public int MaxRoutes;

        public readonly List<(string itemId, float amount)> Contents = new List<(string, float)>();
        public readonly List<CentreRouteReading> Routes = new List<CentreRouteReading>();

        /// <summary>
        /// Reserved volume inside this centre's own inventory. Read from
        /// <see cref="LogisticsCargo.GetReserved"/>, the real API, and structurally
        /// zero — see <see cref="LogisticsCentreDirectory"/>'s note on reservations.
        /// A delta carries no reservation field at all, so a stored snapshot can
        /// only ever report zero and says so rather than implying it measured one.
        /// </summary>
        public float ReservedVolume;

        /// <summary>False for a delta reading: nothing persisted a reservation to read.</summary>
        public bool ReservedVolumeIsMeasured;
    }

    /// <summary>
    /// Every Logistics Centre the campaign currently knows about, live or last
    /// known, as a read.
    /// <para>
    /// This class introduces <b>no</b> authority. It owns no state, ticks nothing
    /// and writes nothing; it reads the same two sources
    /// <see cref="World.SystemStatusQuery"/> already reads — the live
    /// <see cref="LogisticsState"/> registry for the currently-streamed system and
    /// the persisted <see cref="SystemDelta"/> for every other — and puts them in
    /// one shape so a screen does not have to carry the live/delta split itself.
    /// </para>
    /// <para>
    /// <b>Reservations.</b> A centre's <see cref="LogisticsCenter.Inventory"/> is a
    /// <see cref="LogisticsCargo"/>, so it carries the reservation API, and nothing
    /// in the game ever calls it on a centre: every <c>TryReserveAll</c> call site
    /// in the codebase targets either the ship's own hold or a planet's Maintenance
    /// Reserve. The centre's own save DTO has no reservation field either, so a
    /// reservation could not survive a save even if one were made. That is a design
    /// fact, not a temporary zero, and it is stated here once so the screen can say
    /// it rather than draw a figure that can never move.
    /// </para>
    /// </summary>
    public static class LogisticsCentreDirectory
    {
        /// <summary>
        /// Every system that currently has a centre worth listing, live ones first.
        /// A Dismantled record is not a centre and is never listed.
        /// </summary>
        public static void CollectSystems(List<string> into, int max)
        {
            if (into == null) return;
            into.Clear();

            foreach (LogisticsCenter centre in LogisticsState.Centers)
            {
                if (centre == null || centre.SystemId == null) continue;
                if (centre.Runtime != null && centre.Runtime.State == LogisticsCenterState.Dismantled) continue;
                if (into.Contains(centre.SystemId)) continue;
                into.Add(centre.SystemId);
                if (into.Count >= max) return;
            }

            WorldDeltaRepository deltas = StarSystemRegistry.Deltas;
            if (deltas == null) return;

            foreach (SystemDelta delta in deltas.AllNonEmpty)
            {
                if (delta == null) continue;
                foreach (LogisticsCenterDto dto in delta.logisticsCenters)
                {
                    if (dto == null) continue;
                    string systemId = string.IsNullOrEmpty(dto.systemId) ? delta.systemId : dto.systemId;
                    if (string.IsNullOrEmpty(systemId) || into.Contains(systemId)) continue;
                    if ((LogisticsCenterState)dto.state == LogisticsCenterState.Dismantled) continue;
                    into.Add(systemId);
                    if (into.Count >= max) return;
                }
            }
        }

        /// <summary>
        /// This system's centre, live if it is materialized and from its delta
        /// otherwise. <c>null</c> when the system has no centre in either place.
        /// </summary>
        public static LogisticsCentreReading Read(string systemId)
        {
            if (string.IsNullOrEmpty(systemId)) return null;

            LogisticsCenter live = LogisticsState.FindCenter(systemId);
            if (live != null && live.Runtime != null) return ReadLive(live);

            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(systemId);
            if (delta == null) return null;

            foreach (LogisticsCenterDto dto in delta.logisticsCenters)
            {
                if (dto == null) continue;
                string dtoSystemId = string.IsNullOrEmpty(dto.systemId) ? delta.systemId : dto.systemId;
                if (dtoSystemId != systemId) continue;
                return ReadStored(systemId, dto, delta);
            }
            return null;
        }

        private static LogisticsCentreReading ReadLive(LogisticsCenter centre)
        {
            LogisticsCenterTierDef def = centre.Runtime.Def;
            var reading = new LogisticsCentreReading
            {
                SystemId = centre.SystemId,
                Live = centre,
                Tier = centre.Runtime.Tier,
                State = centre.Runtime.State,
                Upgrading = centre.Runtime.Upgrading,
                UpgradeRemaining = centre.Runtime.UpgradeRemaining,
                BuildRemaining = centre.Runtime.BuildRemaining,
                StoredVolume = centre.Inventory != null ? centre.Inventory.Total : 0f,
                Capacity = def.Capacity,
                DocksInUse = centre.DocksInUse,
                DockCount = def.DockCount,
                MaxRoutes = def.MaxRoutes,
                ReservedVolumeIsMeasured = centre.Inventory != null,
            };

            if (centre.Inventory != null)
            {
                foreach (KeyValuePair<string, float> item in centre.Inventory.Items)
                {
                    reading.Contents.Add((item.Key, item.Value));
                    reading.ReservedVolume += centre.Inventory.GetReserved(item.Key)
                                              * LogisticsCargo.VolumePerUnit(item.Key);
                }
            }

            foreach (CollectorRoute route in LogisticsState.Routes)
            {
                if (route == null || route.SystemId != centre.SystemId) continue;
                reading.Routes.Add(new CentreRouteReading
                {
                    DrillWorldId = route.DrillWorldId,
                    Phase = route.Phase,
                    WaitReason = route.WaitReason,
                    HeldItemId = route.HeldItemId,
                    HeldAmount = route.HeldAmount,
                });
            }

            return reading;
        }

        private static LogisticsCentreReading ReadStored(string systemId, LogisticsCenterDto dto, SystemDelta delta)
        {
            LogisticsCenterTierDef def = LogisticsCenterCatalog.Get((LogisticsCenterTier)dto.tier);
            var reading = new LogisticsCentreReading
            {
                SystemId = systemId,
                Live = null,
                Tier = (LogisticsCenterTier)dto.tier,
                State = (LogisticsCenterState)dto.state,
                Upgrading = dto.upgrading,
                UpgradeRemaining = dto.upgradeRemaining,
                BuildRemaining = dto.buildRemaining,
                Capacity = def.Capacity,
                DockCount = def.DockCount,
                MaxRoutes = def.MaxRoutes,

                // A delta has no reservation field to read, so this is a stated
                // absence rather than a measured zero.
                ReservedVolume = 0f,
                ReservedVolumeIsMeasured = false,
            };

            foreach (CargoItemDto item in dto.inventory)
            {
                if (item == null) continue;
                reading.Contents.Add((item.itemId, item.amount));
                reading.StoredVolume += item.amount * LogisticsCargo.VolumePerUnit(item.itemId);
            }

            foreach (CollectorRouteDto route in delta.collectorRoutes)
            {
                if (route == null || route.systemId != systemId) continue;
                reading.Routes.Add(new CentreRouteReading
                {
                    DrillWorldId = route.drillWorldId,
                    Phase = (RoutePhase)route.phase,
                    WaitReason = (RouteWaitReason)route.waitReason,

                    // Held cargo is deliberately not duplicated into the route DTO
                    // (VehicleDto is its single authority), so a stored reading has
                    // no held amount to quote and quotes none.
                    HeldItemId = null,
                    HeldAmount = 0f,
                });
                if ((RoutePhase)route.phase == RoutePhase.Unloading) reading.DocksInUse++;
            }

            return reading;
        }
    }
}
