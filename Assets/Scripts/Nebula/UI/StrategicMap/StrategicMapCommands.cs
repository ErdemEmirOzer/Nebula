using Nebula.Navigation;
using Nebula.Poi;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.UI.StrategicMap
{
    /// <summary>Why a map-issued command was refused. Always explicit — the map never fails silently.</summary>
    public enum StrategicCommandResult
    {
        Accepted,
        NoTarget,
        /// <summary>The system is not located yet, so a route to it cannot be plotted.</summary>
        NotLocated,
        /// <summary>The selected target is not an unresolved frontier signal.</summary>
        NotSignal,
        /// <summary>The navigation authority itself refused; see the reason text it supplied.</summary>
        RejectedByNavigation,
        /// <summary>The system is not reachable for remote command right now (no link).</summary>
        NoCommunication,
        Unavailable
    }

    /// <summary>
    /// The map's ONLY write surface. It is deliberately tiny and routes everything
    /// through authorities that already exist:
    /// <list type="bullet">
    /// <item>Waypoints go through <see cref="NavigationDirector.TrySelectRoute"/>, which
    /// is the same call the older navigation screen uses. The map plots a route and
    /// reports distance/heading — it never teleports, never jumps, and never moves the
    /// ship. The player still flies manually.</item>
    /// <item>Remote commands are only ever offered where the existing communication
    /// rules already allow them, and always return a typed result.</item>
    /// </list>
    /// Nothing here can change economy, dominance, knowledge or any save-facing state.
    /// </summary>
    public sealed class StrategicMapCommands
    {
        private readonly NebulaContext context;

        public StrategicMapCommands(NebulaContext context) => this.context = context;

        /// <summary>The last human-readable reason the navigation authority gave, if any.</summary>
        public string LastRejectReason { get; private set; }

        /// <summary>Plots (or clears) the player's own waypoint through the real navigation authority.</summary>
        public StrategicCommandResult SetWaypoint(StrategicSystemView system)
        {
            LastRejectReason = null;
            NavigationDirector nav = context?.NavigationDirector;
            if (nav == null) return StrategicCommandResult.Unavailable;
            if (string.IsNullOrEmpty(system.SystemId)) return StrategicCommandResult.NoTarget;

            // A system the player has only a heading for cannot be a route target.
            if (!system.PositionKnown) return StrategicCommandResult.NotLocated;

            if (!nav.TrySelectRoute(system.SystemId, out string reject))
            {
                LastRejectReason = TranslateNavigationReject(reject);
                return StrategicCommandResult.RejectedByNavigation;
            }
            return StrategicCommandResult.Accepted;
        }

        public StrategicCommandResult ClearWaypoint()
        {
            LastRejectReason = null;
            NavigationDirector nav = context?.NavigationDirector;
            if (nav == null) return StrategicCommandResult.Unavailable;
            nav.ClearRoute();
            return StrategicCommandResult.Accepted;
        }

        /// <summary>
        /// Routes toward the POI's owning system through the real navigation authority.
        /// This deliberately does not pretend NavigationDirector can target a system-local
        /// offset, and never starts a POI operation remotely: those actions remain owned by
        /// ShipPoi while the player is physically near the materialized object.
        /// </summary>
        public StrategicCommandResult SetWaypointToPoiSystem(StrategicSystemView system, StrategicPoiView poi)
        {
            LastRejectReason = null;
            if (string.IsNullOrEmpty(poi.PoiId)) return StrategicCommandResult.NoTarget;
            if (!poi.PositionKnown)
            {
                LastRejectReason = "Keşif noktasının konumu henüz bulunmadı";
                return StrategicCommandResult.NotLocated;
            }
            return SetWaypoint(system);
        }

        /// <summary>Starts the existing scanner analysis for a selected Signal-level system.</summary>
        public StrategicCommandResult StartAnalysis(StrategicSystemView system)
        {
            LastRejectReason = null;
            NavigationDirector nav = context?.NavigationDirector;
            if (nav == null) return StrategicCommandResult.Unavailable;
            if (string.IsNullOrEmpty(system.SystemId)) return StrategicCommandResult.NoTarget;
            if (system.Knowledge != SystemKnowledgeLevel.Signal) return StrategicCommandResult.NotSignal;
            if (!nav.TryStartAnalysis(system.SystemId, out string reject))
            {
                LastRejectReason = TranslateNavigationReject(reject);
                return StrategicCommandResult.RejectedByNavigation;
            }
            return StrategicCommandResult.Accepted;
        }

        public StrategicCommandResult CancelAnalysis()
        {
            LastRejectReason = null;
            NavigationDirector nav = context?.NavigationDirector;
            if (nav == null) return StrategicCommandResult.Unavailable;
            if (!nav.Analysis.Active) return StrategicCommandResult.Unavailable;
            nav.CancelAnalysis();
            return StrategicCommandResult.Accepted;
        }

        public bool HasWaypoint => context?.NavigationDirector != null
                                   && !string.IsNullOrEmpty(context.NavigationDirector.SelectedRouteSystemId);

        public bool HasActiveAnalysis => context?.NavigationDirector != null
                                         && context.NavigationDirector.Analysis.Active;

        /// <summary>
        /// Whether a remote command may even be offered for this system. Purely a
        /// permission read of the existing link rules — a disconnected system is never
        /// commandable, and the map must not present the option at all.
        /// </summary>
        public bool CanIssueRemoteCommand(StrategicSystemView system) =>
            system.DetailsKnown && !system.IsCurrentSystem
                                && system.Link == Extraction.SystemLinkState.Live;

        /// <summary>Typed guard for any future remote order the map surfaces. Refuses rather than guessing.</summary>
        public StrategicCommandResult CheckRemoteCommand(StrategicSystemView system)
        {
            LastRejectReason = null;
            if (string.IsNullOrEmpty(system.SystemId)) return StrategicCommandResult.NoTarget;
            if (!system.DetailsKnown) return StrategicCommandResult.NotLocated;
            if (system.IsCurrentSystem || system.Link != Extraction.SystemLinkState.Live)
                return StrategicCommandResult.NoCommunication;
            return StrategicCommandResult.Accepted;
        }

        /// <summary>Player-facing text for a refusal, in Turkish. Never a raw enum or an id.</summary>
        public static string Describe(StrategicCommandResult result) => result switch
        {
            StrategicCommandResult.Accepted => "",
            StrategicCommandResult.NoTarget => "Hedef seçilmedi",
            StrategicCommandResult.NotLocated => "Sistemin konumu henüz bulunmadı",
            StrategicCommandResult.NotSignal => "Seçili hedef analiz edilebilir bir sinyal değil",
            StrategicCommandResult.NoCommunication => "Bu sistemle canlı bağlantı yok",
            StrategicCommandResult.RejectedByNavigation => "Rota reddedildi",
            _ => "Şu anda kullanılamıyor"
        };

        public static string TranslateNavigationReject(string reject) => reject switch
        {
            "REQUIRES SCANNER T1" => "Sinyal analizi için Tarayıcı T1 gerekli",
            "TARGET MUST BE A SIGNAL" => "Hedef artık analiz edilebilir bir sinyal değil",
            "ANALYSIS ALREADY IN PROGRESS" => "Başka bir sinyal analizi zaten sürüyor",
            "TARGET MUST BE LOCATED OR VISITED" => "Rota için sistemin önce konumlandırılması gerekli",
            "CANNOT START" => "Analiz şu anda başlatılamıyor",
            null => null,
            _ => reject
        };
    }
}
