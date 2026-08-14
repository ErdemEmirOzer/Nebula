using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Turkish text for the production refusal enums that have no player-facing
    /// table anywhere in the game.
    /// <para>
    /// Built here rather than in production because the tutorial is not allowed
    /// to edit gameplay files, and because the alternative -- inventing a
    /// sentence per step -- is exactly how a tutorial ends up claiming a refusal
    /// the game never made. Every method here is a total switch over a real
    /// enum the real command returned, so a card can only ever say something the
    /// game actually decided. The precedent is
    /// <c>NebulaDiscoveryPage.AnalysisRejectText</c>.
    /// </para>
    /// </summary>
    internal static class TutorialTr
    {
        public static string Drill(DrillUpgradeRejectReason reason) => reason switch
        {
            DrillUpgradeRejectReason.None => null,
            DrillUpgradeRejectReason.AlreadyUpgrading => "SONDAJ ZATEN YÜKSELTİLİYOR",
            DrillUpgradeRejectReason.NotActive => "SONDAJ HENÜZ AKTİF DEĞİL",
            DrillUpgradeRejectReason.MaxTier => "SONDAJ ZATEN T2",
            DrillUpgradeRejectReason.ModuleTierNotMet => "İSTASYON İNŞASI T2 GEREKLİ",
            DrillUpgradeRejectReason.InsufficientResources => "KAYNAK YETERSİZ",
            _ => "SONDAJ YÜKSELTİLEMEDİ",
        };

        public static string Structure(StructureUpgradeRejectReason reason) => reason switch
        {
            StructureUpgradeRejectReason.None => null,
            StructureUpgradeRejectReason.AlreadyUpgrading => "YAPI ZATEN YÜKSELTİLİYOR",
            StructureUpgradeRejectReason.NotActive => "YAPI HENÜZ AKTİF DEĞİL",
            StructureUpgradeRejectReason.MaxTier => "AZAMİ KADEMEDE",
            StructureUpgradeRejectReason.LevelTooLow => "KARAKTER SEVİYESİ ÇOK DÜŞÜK",
            StructureUpgradeRejectReason.CommandTierNotMet => "KOMUTA MERKEZİ T2 GEREKLİ",
            StructureUpgradeRejectReason.WeaponCoordinationTierNotMet => "DAHA YÜKSEK SİLAH KOORDİNASYONU KADEMESİ GEREKLİ",
            StructureUpgradeRejectReason.NoControlledPlanet => "EN AZ BİR KONTROL EDİLEN GEZEGEN GEREKLİ",
            StructureUpgradeRejectReason.InsufficientResources => "KAYNAK YETERSİZ",
            _ => "YAPI YÜKSELTİLEMEDİ",
        };

        public static string Module(UpgradeRejectReason reason) => reason switch
        {
            UpgradeRejectReason.None => null,
            UpgradeRejectReason.UnknownModule => "BİLİNMEYEN MODÜL",
            UpgradeRejectReason.AlreadyMaxTier => "AZAMİ SEVİYEDE",
            UpgradeRejectReason.TierSkip => "SEVİYE ATLANAMAZ",
            UpgradeRejectReason.NotImplemented => "BU SÜRÜMDE YOK",
            UpgradeRejectReason.LevelTooLow => "SEVİYE YETERSİZ",
            UpgradeRejectReason.MilestoneNotMet => "KİLOMETRE TAŞI TAMAMLANMADI",
            UpgradeRejectReason.QueueBusy => "KUYRUK MEŞGUL",
            UpgradeRejectReason.InsufficientPoints => "GELİŞİM PUANI YETERSİZ",
            UpgradeRejectReason.InsufficientResources => "MALZEME YETERSİZ",
            UpgradeRejectReason.ScreenPaused => "EKRAN DURAKLATILDI",
            _ => "YÜKSELTME REDDEDİLDİ",
        };

        public static string Route(RouteCreateRejectReason reason) => reason switch
        {
            RouteCreateRejectReason.None => null,
            RouteCreateRejectReason.CenterNotActive => "MERKEZ AKTİF DEĞİL",
            RouteCreateRejectReason.RouteCapReached => "ROTA SINIRINA ULAŞILDI",
            RouteCreateRejectReason.VehicleNotCollector => "SEÇİLİ ARAÇ TOPLAYICI DEĞİL",
            RouteCreateRejectReason.VehicleNotEligible => "ARAÇ UYGUN DURUMDA DEĞİL",
            RouteCreateRejectReason.VehicleAlreadyRouted => "ARACIN ZATEN BİR ROTASI VAR",
            RouteCreateRejectReason.DrillAlreadyRouted => "SONDAJIN ZATEN BİR ROTASI VAR",
            RouteCreateRejectReason.DrillNotActive => "SONDAJ ETKİN DEĞİL",
            RouteCreateRejectReason.NotSameSystem => "AYNI SİSTEMDE DEĞİL",
            RouteCreateRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ DOLU",
            _ => "ROTA KURULAMADI",
        };

        public static string Reserve(ReserveTransferRejectReason reason) => reason switch
        {
            ReserveTransferRejectReason.None => null,
            ReserveTransferRejectReason.NotInRange => "KOMUTA MERKEZİNE YAKLAŞIN",
            ReserveTransferRejectReason.ItemNotAccepted => "BU MALZEME KABUL EDİLMİYOR",
            ReserveTransferRejectReason.InsufficientAmount => "YETERLİ MİKTAR YOK",
            ReserveTransferRejectReason.ReserveFull => "REZERV DOLU",
            _ => "AKTARIM REDDEDİLDİ",
        };

        public static string Resupply(ShipHangar.MaintenanceResupplyRejectReason reason) => reason switch
        {
            ShipHangar.MaintenanceResupplyRejectReason.None => null,
            ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible => "ARAÇ UYGUN DURUMDA DEĞİL",
            ShipHangar.MaintenanceResupplyRejectReason.TargetNotEligible => "HEDEF UYGUN DEĞİL",
            ShipHangar.MaintenanceResupplyRejectReason.InsufficientCargo => "KARGODA YETERLİ MALZEME YOK",
            ShipHangar.MaintenanceResupplyRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ DOLU",
            _ => "İKMAL BAŞLATILAMADI",
        };

        public static string Engineer(ShipHangar.EngineerSendRejectReason reason) => reason switch
        {
            ShipHangar.EngineerSendRejectReason.None => null,
            ShipHangar.EngineerSendRejectReason.VehicleNotEligible => "ARAÇ UYGUN DURUMDA DEĞİL",
            ShipHangar.EngineerSendRejectReason.TargetNotEligible => "HEDEF UYGUN DEĞİL",
            ShipHangar.EngineerSendRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ DOLU",
            ShipHangar.EngineerSendRejectReason.NothingToRepair => "ONARILACAK BİR ŞEY YOK",
            ShipHangar.EngineerSendRejectReason.InsufficientResources => "KAYNAK YETERSİZ",
            ShipHangar.EngineerSendRejectReason.TargetHasNoDurability => "HEDEFİN DAYANIKLILIĞI YOK",
            _ => "MÜHENDİS GÖNDERİLEMEDİ",
        };

        /// <summary>
        /// The center's own refusal wording. Production already renders exactly
        /// these strings, but <c>ShipLogisticsCenter.BuildRejectText</c> is
        /// private, so the tutorial repeats them verbatim rather than inventing
        /// its own or making a production member public.
        /// </summary>
        public static string CenterBuild(CenterBuildRejectReason reason) => reason switch
        {
            CenterBuildRejectReason.None => null,
            CenterBuildRejectReason.AlreadyExists => "SİSTEMDE ZATEN BİR MERKEZ VAR",
            CenterBuildRejectReason.LevelTooLow => "SEVİYE YETERSİZ (18)",
            CenterBuildRejectReason.ModuleTierTooLow => "İSTASYON İNŞA T1 GEREKLİ",
            CenterBuildRejectReason.NoActiveCommand => "SİSTEMDE AKTİF COMMAND GEREKLİ",
            CenterBuildRejectReason.NotInRange => "ÇAPAYA YAKLAŞ",
            CenterBuildRejectReason.TooFast => "BAĞIL HIZ ÇOK YÜKSEK",
            CenterBuildRejectReason.InsufficientCargo => "YETERSİZ KAYNAK",
            CenterBuildRejectReason.NoSafeAnchor => "SİSTEMDE GÜVENLİ ÇAPA YOK",
            _ => "BAŞARISIZ",
        };

        public static string CenterUpgrade(CenterUpgradeRejectReason reason) => reason switch
        {
            CenterUpgradeRejectReason.None => null,
            CenterUpgradeRejectReason.NotActive => "MERKEZ AKTİF DEĞİL",
            CenterUpgradeRejectReason.AlreadyUpgrading => "ZATEN YÜKSELTİLİYOR",
            CenterUpgradeRejectReason.LevelTooLow => "SEVİYE YETERSİZ (25)",
            CenterUpgradeRejectReason.ModuleTierTooLow => "İSTASYON İNŞA T2 GEREKLİ",
            CenterUpgradeRejectReason.NotEnoughControlledPlanets => "EN AZ 2 CONTROLLED GEZEGEN GEREKLİ",
            CenterUpgradeRejectReason.NotInRange => "ÇAPAYA YAKLAŞ",
            CenterUpgradeRejectReason.TooFast => "BAĞIL HIZ ÇOK YÜKSEK",
            CenterUpgradeRejectReason.InsufficientCargo => "YETERSİZ KAYNAK",
            _ => "BAŞARISIZ",
        };
    }

    /// <summary>
    /// The one place a chapter C/D/E/G step gets hold of the real things it
    /// teaches with: the ship's own cargo and modules, a registered planet, a
    /// real <see cref="DrillStructure"/>, a real <see cref="SurfaceStructure"/>.
    /// <para>
    /// It prefers whatever the live session already composed -- the campaign's
    /// own ship, its own materialized planets -- and only builds a stand-in when
    /// there is none. The stand-in is not a mock: it is the same production
    /// components (<see cref="ShipLogistics"/>, <see cref="ShipExtraction"/>,
    /// <see cref="ShipModules"/> ...) on a scope-owned GameObject, and a
    /// manifest-backed planet resolved through
    /// <see cref="StarSystemRegistry.Manifests"/>, so every rule that runs is the
    /// real one. That is what lets a step be exercised in an EditMode fixture
    /// without generating a galaxy, while the step itself has exactly one code
    /// path.
    /// </para>
    /// <para>
    /// Nothing here reads <c>Presentation</c>, <c>TelemetryBridge</c>,
    /// <c>AudioService</c> or <c>AudioBridge</c>: the tutorial composition leaves
    /// all four unset and their set-once getters throw on an early read.
    /// </para>
    /// </summary>
    internal sealed class TutorialSite
    {
        private TutorialStepContext context;
        private LogisticsCargo standInCargo;
        private ModuleProgressionState standInTiers;
        private ModuleUpgradeService standInUpgrades;
        private GameObject rig;
        private Transform star;

        public NebulaContext Gameplay { get; private set; }
        public ShipController Ship { get; private set; }
        public ShipLogistics Logistics { get; private set; }
        public ShipExtraction Extraction { get; private set; }
        public ShipProcessing Processing { get; private set; }
        public ShipMaintenanceReserveAccess ReserveAccess { get; private set; }
        public ShipModules ModuleHost { get; private set; }

        public PlanetIdentity Planet { get; private set; }
        public PlanetProfile Profile { get; private set; }
        public DrillStructure Drill { get; private set; }
        public SurfaceStructure Structure { get; private set; }
        public LogisticsCenter Center { get; private set; }

        public string SystemId { get; private set; }
        public string WorldId => Profile != null ? Profile.WorldId : null;

        public LogisticsCargo Cargo => Logistics != null && Logistics.Cargo != null ? Logistics.Cargo : standInCargo;
        public ModuleProgressionState ModuleTiers => ModuleHost != null ? ModuleHost.Modules : standInTiers;

        public ModuleUpgradeService Upgrades =>
            ModuleHost != null && ModuleHost.Upgrades != null ? ModuleHost.Upgrades : standInUpgrades;

        public Transform ShipTransform => Ship != null ? Ship.transform : null;

        // ---- opening ----------------------------------------------------------

        /// <summary>Resolves (or builds) everything the step needs. Safe to call twice.</summary>
        public void Open(TutorialStepContext ctx)
        {
            if (context != null) return;
            context = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Gameplay = ctx.Gameplay;

            ResolveShip();
            EnsureRegistry();
            EnsurePlanet();
        }

        private void ResolveShip()
        {
            Ship = Read(() => Gameplay?.Ship);
            if (Ship == null)
            {
                rig = context.Scope.NewGroup("Tutorial Ship Rig");
                Ship = rig.AddComponent<ShipController>();
                rig.AddComponent<ShipLogistics>();
                rig.AddComponent<ShipProcessing>();
                rig.AddComponent<ShipExtraction>();
                rig.AddComponent<ShipMaintenanceReserveAccess>();
                rig.AddComponent<ShipModules>();

                // Outside play mode Unity runs none of their Awakes, so every field
                // those set is still null and the InitializeNewCampaign calls below
                // throw on their first line. No-op in the real game.
                TutorialAwake.WakeAll(rig);
            }

            GameObject host = Ship.gameObject;
            Logistics = host.GetComponent<ShipLogistics>();
            Extraction = host.GetComponent<ShipExtraction>();
            Processing = host.GetComponent<ShipProcessing>();
            ReserveAccess = host.GetComponent<ShipMaintenanceReserveAccess>();
            ModuleHost = Read(() => Gameplay?.Modules) ?? host.GetComponent<ShipModules>();

            if (Logistics != null && Logistics.Cargo == null) Logistics.InitializeNewCampaign();
            if (ModuleHost != null && ModuleHost.Upgrades == null && Logistics != null && Logistics.Cargo != null)
                ModuleHost.InitializeNewCampaign();

            standInCargo = new LogisticsCargo(ModuleCatalog.CargoCapacityByTier[0]);
            standInTiers = new ModuleProgressionState();
            standInUpgrades = new ModuleUpgradeService(standInTiers, ProgressionService.State, standInCargo);
        }

        /// <summary>
        /// A profile only ever comes from a real <see cref="PlanetManifest"/>
        /// (<c>ExtractionState.BuildProfile</c> throws otherwise), so a step run
        /// outside a composed session has to make sure the manifest repository
        /// exists before it asks for one.
        /// </summary>
        private void EnsureRegistry()
        {
            if (StarSystemRegistry.Manifests == null)
                StarSystemRegistry.Initialize(unchecked((ulong)TutorialSession.TutorialSeed));

            SystemId = StarSystemRegistry.StartSystemId;
            if (string.IsNullOrEmpty(SystemId))
            {
                var sector = new SectorCoordinate(0, 0, 0);
                SystemId = WorldIdCodec.SystemId(sector, 0);
                StarSystemRegistry.SetStartSystem(sector, SystemId);
            }
        }

        private void EnsurePlanet()
        {
            foreach (PlanetIdentity live in ExtractionState.AllPlanets)
            {
                if (live == null || live.Body == null) continue;
                Planet = live;
                Profile = ExtractionState.GetProfile(live);
                SystemId = WorldIdCodec.SystemIdFromPlanetWorldId(live.WorldId) ?? SystemId;
                return;
            }

            SystemManifest system = StarSystemRegistry.Manifests.ResolveSystem(SystemId);
            if (system == null || system.Planets.Count == 0) return;

            PlanetManifest manifest = system.Planets[0];
            GameObject holder = context.Scope.NewGroup("Tutorial Planet :: " + manifest.PlanetId);
            var body = holder.AddComponent<CelestialBody>();
            body.Configure(CelestialBodyType.Planet, manifest.PlanetRadius, 1f, false);
            Planet = holder.AddComponent<PlanetIdentity>();
            Planet.Initialize(manifest.PlanetId, body, true);
            Profile = ExtractionState.GetProfile(Planet);
        }

        /// <summary>A scope-owned star transform, for the orbital things that anchor to one.</summary>
        public Transform Star
        {
            get
            {
                if (star == null) star = context.Scope.NewGroup("Tutorial Star").transform;
                return star;
            }
        }

        public SystemManifest SystemManifest => StarSystemRegistry.Manifests?.ResolveSystem(SystemId);

        // ---- ship placement ---------------------------------------------------

        /// <summary>Puts the ship at a real altitude above the planet's surface.</summary>
        public void PlaceShipAtAltitude(float altitude)
        {
            if (Planet == null || Planet.Body == null || ShipTransform == null) return;
            Vector3 centre = Planet.Body.transform.position;
            ShipTransform.position = centre + Vector3.up * (Planet.Body.Radius + Mathf.Max(0f, altitude));
        }

        /// <summary>
        /// Whether the extraction system has made <b>this site's</b> planet the
        /// current target.
        /// <para>
        /// Named for one planet rather than for "a target", because in the real
        /// tutorial world it is never the only one. <c>ShipExtraction.UpdateTarget</c>
        /// sweeps every celestial body in the scene and takes the nearest inside its
        /// own 3-radius interest sphere — so a step that asked "is anything the
        /// target?" completed itself the moment a frame ran and some other planet
        /// happened to be near where the ship was placed. That is the bug, and this
        /// is the shape that cannot have it.
        /// </para>
        /// <para>
        /// Reads the live <see cref="ShipExtraction"/> whenever one has resolved a
        /// target; with no live component it applies that component's own interest
        /// rule (<c>body.Radius * 3f</c>) to this body, which is what it does.
        /// </para>
        /// </summary>
        public bool TargetIsThePlanet()
        {
            if (Planet == null || Planet.Body == null || ShipTransform == null) return false;

            // The live component's own answer, asked about this body. A different
            // planet being the target is a "no" here, not a "yes" -- that difference
            // is the whole fix.
            if (Extraction != null && Extraction.TargetPlanet != null && Extraction.TargetProfile != null)
                return Extraction.TargetPlanet == Planet;

            if (Profile == null) return false;
            float interest = Planet.Body.Radius * 3f;
            return Vector3.Distance(ShipTransform.position, Planet.Body.transform.position) < interest;
        }

        public float Altitude() =>
            Planet == null || Planet.Body == null || ShipTransform == null
                ? float.MaxValue
                : Vector3.Distance(ShipTransform.position, Planet.Body.transform.position) - Planet.Body.Radius;

        public float ScanRangeLimit() =>
            Planet == null || Planet.Body == null ? 0f
                : Planet.Body.Radius * (Extraction != null ? Extraction.ScannerProximityR : ModuleCatalog.ScanRadiusMultiplierByTier[0]);

        // ---- drills -----------------------------------------------------------

        public DrillStructure InstallDrill(DrillLifecycle state, float stored = 0f, int tier = 1,
            float capsuleRemaining = 0f, float buildRemaining = 0f,
            DrillStructure.BuildSource source = DrillStructure.BuildSource.Kit, int refundParts = 0)
        {
            if (Planet == null || Profile == null) return null;
            ClearDrills();
            var runtime = new DrillRuntime(Profile, state, capsuleRemaining, buildRemaining, stored, 0f, tier);
            Drill = DrillStructure.Restore(Planet, Profile, runtime, source, refundParts);
            Own(Drill != null ? Drill.gameObject : null);
            return Drill;
        }

        /// <summary>The drill this step is watching, or whichever one is on the site's planet.</summary>
        public DrillStructure FindDrill()
        {
            if (Drill != null) return Drill;
            foreach (DrillStructure candidate in ExtractionState.Drills)
                if (candidate != null && candidate.WorldId == WorldId) return candidate;
            return null;
        }

        public void ClearDrills()
        {
            var doomed = new List<DrillStructure>();
            foreach (DrillStructure candidate in ExtractionState.Drills)
                if (candidate != null && candidate.WorldId == WorldId) doomed.Add(candidate);
            for (int i = 0; i < doomed.Count; i++) Kill(doomed[i].gameObject);
            Drill = null;
        }

        // ---- surface structures ----------------------------------------------

        public static Durability DurabilityFor(SurfaceStructureDef def, int tier, float fraction = 1f)
        {
            StructureTierDef tierDef = PlanetaryDefenseCatalog.Get(def.Type, tier);
            return tierDef != null
                ? new Durability(tierDef.ShieldMax, tierDef.ArmorMax, tierDef.HullMax, fraction)
                : new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, fraction);
        }

        public static List<(string itemId, float amount)> BuildCost(SurfaceStructureDef def, ResourceFamily family)
        {
            var cost = new List<(string itemId, float amount)>();
            if (def.PartsCost > 0) cost.Add((ProcessingCatalog.BasicPartId, def.PartsCost));
            if (def.MaterialCost > 0) cost.Add((ProcessingCatalog.MaterialIds[(int)family], def.MaterialCost));
            return cost;
        }

        /// <summary>Installs an already-finished structure (the prerequisite an upgrade step needs to exist).</summary>
        public SurfaceStructure InstallStructure(SurfaceStructureDef def, SurfaceNode node,
            DrillLifecycle state = DrillLifecycle.Active, int tier = 1)
        {
            if (Planet == null || Profile == null) return null;
            var runtime = new StructureRuntime(def, state, 0f, 0f, DurabilityFor(def, tier), tier);
            SurfaceStructure built = SurfaceStructure.Restore(Planet, Profile, def, node, runtime, Cargo,
                new List<(string itemId, float amount)>());
            Own(built != null ? built.gameObject : null);
            Structure = built;
            return built;
        }

        /// <summary>Issues a genuine build, with the atomic multi-item reservation the real command makes.</summary>
        public SurfaceStructure BuildStructure(SurfaceStructureDef def, SurfaceNode node)
        {
            if (Planet == null || Profile == null) return null;
            SurfaceStructure built = SurfaceStructure.Create(Planet, Profile, def, node,
                ShipTransform != null ? ShipTransform.position : Vector3.zero, Cargo, BuildCost(def, Profile.Family));
            Own(built != null ? built.gameObject : null);
            Structure = built;
            return built;
        }

        public SurfaceStructure FindStructure(SurfaceStructureType type) =>
            ExtractionState.FindStructure(WorldId, type);

        public void ClearStructures()
        {
            var doomed = new List<SurfaceStructure>();
            foreach (SurfaceStructure candidate in ExtractionState.Structures)
                if (candidate != null && candidate.WorldId == WorldId) doomed.Add(candidate);
            for (int i = 0; i < doomed.Count; i++) Kill(doomed[i].gameObject);
            Structure = null;
        }

        // ---- logistics center -------------------------------------------------

        public LogisticsCenter InstallCenter(LogisticsCenterState state, LogisticsCenterTier tier = LogisticsCenterTier.T1,
            bool upgrading = false)
        {
            SystemManifest manifest = SystemManifest;
            if (manifest == null) return null;
            RemoveCenter();

            LogisticsCenterTierDef def = LogisticsCenterCatalog.Get(tier);
            var runtime = new LogisticsCenterRuntime(tier, state,
                state == LogisticsCenterState.Constructing ? def.BuildSeconds : 0f,
                new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 1f),
                upgrading, upgrading ? LogisticsCenterCatalog.T2.BuildSeconds : 0f);

            Center = LogisticsCenter.Restore(manifest, Star, Vector3.zero, runtime,
                new List<(string itemId, float amount)>());
            Own(Center != null ? Center.gameObject : null);
            return Center;
        }

        public void RemoveCenter()
        {
            LogisticsCenter found = LogisticsState.FindCenter(SystemId);
            if (found != null) Kill(found.gameObject);
            Center = null;
        }

        // ---- vehicles ---------------------------------------------------------

        /// <summary>
        /// A resupply mission the step is watching but that no live hangar owns.
        /// <para>
        /// A <c>MaintenanceResupplyMission</c> is normally reached through
        /// <c>VehicleAgent.ResupplyTask</c>, and that is the first place
        /// <see cref="FindResupply"/> looks. This field is the second: the mission
        /// object is the same production type running the same public phase
        /// machine, only without a vehicle physically flying it -- which is what
        /// the mission's own transitions exist for.
        /// </para>
        /// </summary>
        public static MaintenanceResupplyMission PendingResupply { get; set; }

        public MaintenanceResupplyMission FindResupply()
        {
            ShipHangar hangar = Read(() => Gameplay?.Hangar);
            if (hangar != null)
                foreach (VehicleAgent vehicle in hangar.Vehicles)
                    if (vehicle != null && vehicle.ResupplyTask != null) return vehicle.ResupplyTask;
            return PendingResupply;
        }

        // ---- resources / progression -----------------------------------------

        public void Grant(string itemId, float amount)
        {
            if (Cargo == null || amount <= 0f) return;
            float needed = amount * LogisticsCargo.VolumePerUnit(itemId);
            if (Cargo.AvailableVolume < needed) Cargo.SetCapacity(Cargo.UsedVolume + needed);
            Cargo.Add(itemId, amount);
        }

        public void GrantAll(IReadOnlyList<(string itemId, float amount)> cost)
        {
            if (cost == null) return;
            for (int i = 0; i < cost.Count; i++) Grant(cost[i].itemId, cost[i].amount);
        }

        /// <summary>
        /// Leaves the hold with none of this item, and with nothing still claiming any.
        /// <para>
        /// Every lock is released, not only the module-upgrade one. <c>TryRemove</c> is
        /// all-or-nothing against the <em>available</em> amount
        /// (<c>LogisticsCargo.cs:112-119</c>), so one leftover reservation made this
        /// remove nothing at all — and say nothing about it. A structure build lock
        /// outlives its structure: destroying the object does not release it, only
        /// Cancel or Consume does, and a tutorial step's scope destroys the object.
        /// So a step that ran earlier in the curriculum could leave D-07's own outputs
        /// sitting in the hold with the strip silently refused, and D-07 — which asks
        /// the player to produce a part and a material and then reads the hold for both
        /// — was marked complete by the director on its first poll with the player
        /// doing nothing. That is the exact failure
        /// <c>TutorialLifecycleTests.NoStepCompletesWhileThePlayerDoesNothing</c>
        /// exists to catch, and it caught it.
        /// </para>
        /// </summary>
        public void Strip(string itemId)
        {
            if (Cargo == null) return;
            Cargo.ReleaseReservation(ModuleUpgradeJob.CargoOwnerId);

            // Copied first: releasing mutates the owner collection being walked.
            foreach (string owner in new List<string>(Cargo.ReservationOwners))
                if (Cargo.GetOwnerReservation(owner, itemId) > 0f) Cargo.ReleaseReservation(owner);

            Cargo.TryRemove(itemId, Cargo.GetStored(itemId));
        }

        /// <summary>
        /// Rewrites the XP ledger without the named events, so a step whose
        /// success is "this award happens" can still be performed on a save that
        /// already earned it. Uses the real restore path, so no XP is minted.
        /// </summary>
        public static void ForgetAwards(params string[] eventIds)
        {
            if (eventIds == null || eventIds.Length == 0) return;
            var keep = new List<string>();
            foreach (string id in ProgressionService.Ledger.All)
            {
                bool drop = false;
                for (int i = 0; i < eventIds.Length; i++) if (eventIds[i] == id) { drop = true; break; }
                if (!drop) keep.Add(id);
            }
            ProgressionService.Restore(ProgressionService.State.TotalXp, ProgressionService.State.DevelopmentPoints, keep);
        }

        /// <summary>
        /// A copy of the ledger, because <c>ProgressionService.Restore</c> clears
        /// the ledger before re-adding from the sequence it was handed -- so
        /// passing <c>Ledger.All</c> straight back in silently empties it.
        /// </summary>
        public static List<string> LedgerSnapshot()
        {
            var snapshot = new List<string>();
            foreach (string id in ProgressionService.Ledger.All) snapshot.Add(id);
            return snapshot;
        }

        public static void SetLevel(int level)
        {
            ProgressionService.Restore(ProgressionMath.TotalXpToReach(Mathf.Clamp(level, 1, ProgressionMath.MaxLevel)),
                ProgressionService.State.DevelopmentPoints, LedgerSnapshot());
        }

        public static void GrantPoints(int points)
        {
            ProgressionService.Restore(ProgressionService.State.TotalXp,
                ProgressionService.State.DevelopmentPoints + Mathf.Max(0, points), LedgerSnapshot());
        }

        // ---- plumbing ---------------------------------------------------------

        /// <summary>Hands an object to the step scope so it dies with the step, in both play and edit mode.</summary>
        public void Own(GameObject spawned)
        {
            if (spawned == null || context == null) return;
            context.Scope.Track(spawned);
        }

        /// <summary><c>Destroy</c> is deferred in play mode and merely errors outside it; this is the one form that works in both.</summary>
        public static void Kill(GameObject target)
        {
            if (target == null) return;
            target.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>Reads a set-once <see cref="NebulaContext"/> property that may never have been set this run.</summary>
        private static T Read<T>(Func<T> get) where T : class
        {
            try { return get(); }
            catch (InvalidOperationException) { return null; }
        }
    }

    /// <summary>Shared shorthand for declaring a step's authored assets.</summary>
    internal static class TutorialAssets
    {
        public static TutorialAssetRequirement Visual(string id, string purpose) =>
            new TutorialAssetRequirement(TutorialAssetKind.VisualCatalogId, id, purpose);

        public static TutorialAssetRequirement[] Of(params TutorialAssetRequirement[] requirements) => requirements;
    }

    /// <summary>
    /// Chapter C -- Sondaj ve Kaynak Çıkarımı (curriculum steps 25-41).
    /// <para>
    /// Every success condition below is a real extraction state or a real
    /// lifecycle transition: <see cref="PlanetProfile.Scanned"/>,
    /// <see cref="DrillLifecycle"/>, <see cref="DrillRuntime.StoredAmount"/>,
    /// <see cref="DrillRuntime.Tier"/>, a <see cref="LogisticsCargo"/> balance,
    /// or a typed reject reason a real command returned. None of them reads
    /// input, elapsed time or displayed text.
    /// </para>
    /// </summary>
    public static class ChapterCSteps
    {
        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return Step("C-01", "Bir gezegene yaklaş",
                "Gemiyi gezegene, yarıçapının üç katından daha yakın bir mesafeye getir.",
                "Gezegen menziline gir; çıkarım hedefi olarak seçilsin.",
                site =>
                {
                    // Latched, not sampled. "This planet, not any planet" fixed one
                    // half -- the extraction system takes the nearest body inside its
                    // own 3-radius sphere, so "something is targeted" was true
                    // wherever the ship happened to land. But asking only "is it the
                    // target now?" still credits a target that was already set when
                    // the step opened, and in a full run the step before this one has
                    // usually left the ship at this very planet.
                    //
                    // So the step waits for the transition it is actually teaching:
                    // the target must first be seen to be something other than this
                    // planet, and only then become it. That is immune to whatever the
                    // world happened to be doing at Prepare, which no amount of
                    // careful placement can be.
                    bool armed = false;

                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            if (!armed)
                            {
                                armed = !site.TargetIsThePlanet();
                                return false;
                            }
                            return site.TargetIsThePlanet();
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            armed = false;
                            // Deliberately far outside the interest radius: approaching is the step.
                            site.PlaceShipAtAltitude(site.Planet != null ? site.Planet.Body.Radius * 9f : 0f);
                        },
                        reject: _ => armed && !site.TargetIsThePlanet() ? "MENZİLDE GEZEGEN YOK" : null);
                },
                checklist: new[] { "Gezegen bağlam satırında görünsün", "Yükseklik değeri okunsun" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("visual.celestial.planet.metal",
                    "the planet the ship approaches")));

            yield return Step("C-02", "Gezegeni tara",
                "Tarama tuşunu basılı tut ve tarama tamamlanana kadar menzilde kal.",
                "Gezegeni tara; kaynak ailesi ve nadirlik açığa çıksın.",
                site => TutorialSteps.Poll(
                    complete: _ => site.Profile != null && site.Profile.Scanned
                                   && ProgressionService.Ledger.Contains(ProgressionEvents.Scan(site.WorldId)),
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        if (site.Profile == null) return;
                        site.Profile.Scanned = false;
                        TutorialSite.ForgetAwards(ProgressionEvents.Scan(site.WorldId),
                            ProgressionEvents.Family(ExtractionCatalog.ResourceIds[(int)site.Profile.Family]));
                        site.PlaceShipAtAltitude(site.ScanRangeLimit() * 0.5f);
                    },
                    reject: _ =>
                    {
                        if (site.Extraction != null && !string.IsNullOrEmpty(site.Extraction.ScanBlockedReason))
                            return $"YAKLAŞ — YÜKSEKLİK {site.Altitude():0}, GEREKEN {site.ScanRangeLimit():0}";
                        return site.Altitude() > site.ScanRangeLimit()
                            ? $"YAKLAŞ — YÜKSEKLİK {site.Altitude():0}, GEREKEN {site.ScanRangeLimit():0}"
                            : null;
                    }),
                checklist: new[] { "Yükseklik tarama sınırının altında olsun", "Tarama yüzdesi %100'e ulaşsın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("visual.celestial.planet.metal",
                    "the body being scanned")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.ScanKey));

            yield return Step("C-03", "Sondaj yuvasını doğrula",
                "Gezegeni tara ve sondaj yuvasının boş olduğunu doğrula.",
                "Sondaj yuvasının kimliğini ve boş olduğunu oku.",
                site => TutorialSteps.Poll(
                    complete: _ => site.Profile != null && site.Profile.Scanned
                                   && site.Profile.DrillNode.Id == site.WorldId + "/node/" + SurfaceSlot.Drill
                                   && !ExtractionState.IsNodeOccupied(site.WorldId, site.Profile.DrillNode.Id),
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearDrills();
                        // The node reading is only meaningful on a scanned planet, so the
                        // scan is what the player still has to produce.
                        if (site.Profile != null) site.Profile.Scanned = false;
                    },
                    reject: _ =>
                    {
                        if (site.Profile == null) return null;
                        if (!site.Profile.Scanned) return "ÖNCE GEZEGENİ TARA";
                        return ExtractionState.IsNodeOccupied(site.WorldId, site.Profile.DrillNode.Id)
                            ? "SONDAJ YUVASI DOLU" : null;
                    }),
                checklist: new[] { "Yuva kimliği okunsun", "Yuvanın dolu olmadığı görülsün" });

            yield return Step("C-04", "Yörüngeyi sabitle",
                "Yörünge sabitleme tuşuna bas ve gemiyi gezegene kilitle.",
                "İnşa modunu aç; bağıl hız sıfıra insin.",
                site => TutorialSteps.Poll(
                    complete: _ => site.Ship != null && site.Planet != null && site.Planet.Body != null
                                   && site.Ship.StabilizedAnchor == site.Planet.Body.transform
                                   && (site.Extraction == null
                                       || Mathf.Approximately(site.Extraction.RelativeSpeed(site.Planet.Body), 0f)),
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        if (site.Profile != null) site.Profile.Scanned = true;
                        ExtractionState.SetEvacuationLocked(site.WorldId, false);
                        site.Ship?.ReleaseStabilize();
                        site.PlaceShipAtAltitude(site.ScanRangeLimit() * 0.5f);
                    },
                    reject: _ =>
                    {
                        if (site.Profile != null && !site.Profile.Scanned) return "ÖNCE GEZEGENİ TARA";
                        return ExtractionState.IsEvacuationLocked(site.WorldId) ? "TAHLİYE SÜRÜYOR" : null;
                    }),
                checklist: new[] { "İnşa modu açılsın", "Bağıl hız sıfıra insin" },
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.BuildModeKey));

            yield return Step("C-05", "Öncü kitini kur",
                "Onay tuşuna basarak sondaj kapsülünü gezegene gönder.",
                "İlk sondajı öncü kitiyle kur; kapsül yola çıksın.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        return drill != null && drill.Runtime.State == DrillLifecycle.CapsuleInTransit;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearDrills();
                        if (site.Profile != null) site.Profile.Scanned = true;
                        ExtractionState.ForceKitState(ExtractionState.KitState.Available);
                        site.PlaceShipAtAltitude(site.ScanRangeLimit() * 0.5f);
                    },
                    reject: _ =>
                    {
                        if (ExtractionState.Kit != ExtractionState.KitState.Consumed) return null;
                        return site.Cargo != null && site.Cargo.GetAvailable(ProcessingCatalog.BasicPartId) >= 20f
                            ? null : "20 TEMEL YAPI PARÇASI GEREKLİ";
                    }),
                checklist: new[] { "Kit rezerve edilsin", "Kapsül yola çıksın" },
                assets: TutorialAssets.Of(
                    TutorialAssets.Visual(ExtractionCatalog.BuildKitId, "the pioneer kit the first drill is built from"),
                    TutorialAssets.Visual("visual.delivery.build_capsule", "the descending build capsule")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.ConfirmKey));

            yield return Step("C-06", "Kapsülün inişini izle",
                "Kapsül yüzeye ulaşana kadar sondaj yuvasını izle.",
                "Kapsülün inişini ve inşaatın başlamasını gör.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        return drill != null && drill.Runtime.State == DrillLifecycle.Constructing;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.InstallDrill(DrillLifecycle.CapsuleInTransit, capsuleRemaining: 3f, buildRemaining: 30f);
                    }),
                checklist: new[] { "Kapsül hareket etsin", "Durum İNŞA EDİLİYOR'a geçsin" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("visual.delivery.build_capsule",
                    "the capsule whose descent the player watches")));

            yield return Step("C-07", "İnşaatın bitmesini bekle",
                "Sondaj etkinleşene kadar inşaatın tamamlanmasını bekle.",
                "İnşaat bitsin; sondaj gövdesi görünür olsun.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        return drill != null && drill.Runtime.State == DrillLifecycle.Active;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.InstallDrill(DrillLifecycle.Constructing, buildRemaining: 30f);
                    },
                    reject: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        return drill != null && drill.Runtime.State == DrillLifecycle.Damaged
                            ? "SONDAJ HASAR ALDI, İNŞAAT DURDU" : null;
                    }),
                checklist: new[] { "İnşa süresi bitsin", "Sondaj gövdesi görünür olsun" },
                assets: TutorialAssets.Of(TutorialAssets.Visual(ExtractionCatalog.DrillT1Id,
                    "the finished T1 drill body")));

            yield return Step("C-08", "Üretimi başlat",
                "Sondajın ham kaynak üretmeye başladığını gör.",
                "İlk birim depoya düşsün.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        return drill != null && drill.Runtime.StoredAmount >= 1f && drill.Runtime.IsProducing;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        DrillStructure drill = site.InstallDrill(DrillLifecycle.Active);
                        if (drill != null) drill.Runtime.ProductionSuspended = false;
                    },
                    reject: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        return drill != null && drill.Runtime.ProductionSuspended
                            ? "GEZEGEN ÇEKİŞMELİ — ÜRETİM DURDU" : null;
                    }),
                checklist: new[] { "Üretim hızı okunsun", "Depoda ilk birim görünsün" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("resource.metal",
                    "the stored raw resource visual on the drill")));

            yield return Step("C-09", "Sondaj deposunu oku",
                "Sondajın yerel deposunun ne kadar dolduğunu oku.",
                "Depolanan miktarı kapasiteye karşı oku.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        if (drill == null || site.Profile == null) return false;
                        float capacity = ExtractionCatalog.StorageCapacity(site.Profile.Rarity, drill.Runtime.Tier);
                        return Mathf.Approximately(drill.Runtime.StorageCapacity, capacity)
                               && drill.Runtime.StoredAmount > 0f && drill.Runtime.StoredAmount < capacity;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.InstallDrill(DrillLifecycle.Active);
                    }),
                checklist: new[] { "Depolanan miktar okunsun", "Depo kapasitesi okunsun" });

            yield return Step("C-10", "Kaynağı gemiye aktar",
                "Aktarma tuşunu basılı tutarak sondaj deposunu gemi kargosuna boşalt.",
                "Sondaj deposunu gemi kargosuna aktar.",
                site =>
                {
                    float storedBaseline = 0f, cargoBaseline = 0f;
                    string itemId = null;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            DrillStructure drill = site.FindDrill();
                            if (drill == null || itemId == null || site.Cargo == null) return false;
                            float moved = site.Cargo.Get(itemId) - cargoBaseline;
                            float drained = storedBaseline - drill.Runtime.StoredAmount;
                            return moved > 0.0001f && Mathf.Abs(moved - drained) <= 0.0001f;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            DrillStructure drill = site.InstallDrill(DrillLifecycle.Active, stored: 20f);
                            if (drill == null || site.Profile == null) return;
                            itemId = ExtractionCatalog.ResourceIds[(int)site.Profile.Family];
                            site.Strip(itemId);
                            storedBaseline = drill.Runtime.StoredAmount;
                            cargoBaseline = site.Cargo.Get(itemId);
                            site.PlaceShipAtAltitude(0f);
                        },
                        reject: _ => site.Cargo != null && site.Cargo.FreeSpace <= 0.0001f ? "KARGO DOLU" : null);
                },
                checklist: new[] { "Sondaj deposu azalsın", "Gemi kargosunda ham kaynak artsın" },
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.TransferKey));

            yield return Step("C-11", "Depo doldu uyarısını gör",
                "Sondaj deposu dolduğunda üretimin durduğunu ve uyarının belirdiğini gör.",
                "Dolu depo dikkat kartını gör.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        if (drill == null || drill.Runtime.IsProducing) return false;
                        AttentionCard? card = AttentionResolver.ResolveForSystem(site.SystemId, DrillsFullInputs(site));
                        return card.HasValue && card.Value.Priority == AttentionPriority.Medium
                               && card.Value.Reason == "DRILLS FULL — TRANSFER NEEDED";
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        if (site.Profile == null) return;
                        float capacity = ExtractionCatalog.StorageCapacity(site.Profile.Rarity, 1);
                        // One unit short of full: the last unit is what the player waits for.
                        site.InstallDrill(DrillLifecycle.Active, stored: Mathf.Max(0f, capacity - 1f));
                    },
                    reject: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        return drill != null && drill.Runtime.IsFull ? "SONDAJLAR DOLU — AKTARIM GEREKLİ" : null;
                    }),
                checklist: new[] { "Depo kapasiteye ulaşsın", "Üretim dursun", "Dikkat kartı çıksın" });

            yield return Step("C-12", "T2 önkoşulunu öğren",
                "Sondaj etkinleşince yükseltmeyi dene ve İstasyon İnşası T2 şartını oku.",
                "Sondaj T2 yükseltmesinin modül kapısını oku.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        if (drill == null) return false;
                        int gate = site.ModuleTiers.GetTier(DrillUpgradeCatalog.GateModuleId);
                        return DrillUpgradeService.CheckStart(drill.Runtime, gate, site.Cargo)
                               == DrillUpgradeRejectReason.ModuleTierNotMet
                               && drill.ActiveUpgradeJob == null;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        // Constructing, not Active: the gate this step teaches is only the
                        // answer once the drill itself is ready, so prepare cannot be it.
                        site.InstallDrill(DrillLifecycle.Constructing, buildRemaining: 30f);
                        site.ModuleTiers.SetTier(DrillUpgradeCatalog.GateModuleId, 1);
                    },
                    reject: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        if (drill == null) return null;
                        int gate = site.ModuleTiers.GetTier(DrillUpgradeCatalog.GateModuleId);
                        return TutorialTr.Drill(DrillUpgradeService.CheckStart(drill.Runtime, gate, site.Cargo));
                    }),
                checklist: new[] { "Yükseltme reddedilsin", "Gerekli modül kademesi okunsun" },
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.ConfirmKey));

            yield return Step("C-13", "Sondajı T2'ye yükselt",
                "İstasyon İnşası T2 hazırken onay tuşuna basarak sondaj yükseltmesini başlat.",
                "T2 yükseltmesini başlat; maliyet ayrılsın.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        if (drill == null || drill.ActiveUpgradeJob == null) return false;
                        string owner = DrillUpgradeService.OwnerId(drill.WorldId, drill.NodeId);
                        return drill.ActiveUpgradeJob.TargetTier == DrillUpgradeCatalog.MaxTier
                               && Mathf.Approximately(
                                   site.Cargo.GetOwnerReservation(owner, ProcessingCatalog.BasicPartId),
                                   DrillUpgradeCatalog.PartsCost);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.InstallDrill(DrillLifecycle.Active);
                        site.ModuleTiers.SetTier(DrillUpgradeCatalog.GateModuleId, 2);
                        if (site.Profile != null) site.GrantAll(DrillUpgradeCatalog.Cost(site.Profile.Family));
                    },
                    reject: _ =>
                    {
                        DrillStructure drill = site.FindDrill();
                        if (drill == null || drill.ActiveUpgradeJob != null) return null;
                        int gate = site.ModuleTiers.GetTier(DrillUpgradeCatalog.GateModuleId);
                        return TutorialTr.Drill(DrillUpgradeService.CheckStart(drill.Runtime, gate, site.Cargo));
                    }),
                checklist: new[] { "35 parça ve 10 özel malzeme ayrılsın", "Yükseltme işi başlasın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("visual.state.structure.repair_upgrade",
                    "the shared upgrade state kit")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.ConfirmKey));

            yield return Step("C-14", "Yükseltme sırasında üretimin durduğunu gör",
                "Yükseltmeyi başlat ve sondaj deposunun artmayı bıraktığını doğrula.",
                "Yükseltme boyunca üretimin tamamen durduğunu gör.",
                site =>
                {
                    // The last reading taken while there was no upgrade running. It
                    // is refreshed on every poll before the job exists, so it is
                    // always "what the store held the instant the upgrade began" --
                    // which is the only number the halt can honestly be measured
                    // against on a drill that was producing right up to that moment.
                    float storedBeforeUpgrade = -1f;

                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            DrillStructure drill = site.FindDrill();
                            if (drill == null) return false;

                            if (drill.ActiveUpgradeJob == null)
                            {
                                storedBeforeUpgrade = drill.Runtime.StoredAmount;
                                return false;
                            }

                            return storedBeforeUpgrade >= 0f
                                   && !drill.Runtime.IsProducing
                                   && drill.Runtime.StoredAmount <= storedBeforeUpgrade + 0.0001f;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            // An active, producing drill with the upgrade affordable
                            // and its module gate open -- everything the upgrade
                            // needs, and NOT the upgrade. Starting it here was the
                            // step preparing its way into its own lesson: once a job
                            // exists, production is already stopped and DrillStructure
                            // .Update advances the job every frame, so the old
                            // "remaining seconds went down" clause was satisfied by
                            // the world within two frames of the card opening.
                            DrillStructure drill = site.InstallDrill(DrillLifecycle.Active, stored: 12f);
                            if (drill == null) return;
                            site.ModuleTiers.SetTier(DrillUpgradeCatalog.GateModuleId, 2);
                            if (site.Profile != null) site.GrantAll(DrillUpgradeCatalog.Cost(site.Profile.Family));
                            storedBeforeUpgrade = drill.Runtime.StoredAmount;
                        },
                        reject: _ =>
                        {
                            DrillStructure drill = site.FindDrill();
                            if (drill == null || drill.Runtime.IsUpgrading) return null;
                            int gate = site.ModuleTiers.GetTier(DrillUpgradeCatalog.GateModuleId);
                            return TutorialTr.Drill(DrillUpgradeService.CheckStart(drill.Runtime, gate, site.Cargo));
                        });
                },
                checklist: new[] { "Yükseltme işi başlasın", "Depolanan miktar artmayı bıraksın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("visual.state.structure.repair_upgrade",
                    "the in-progress upgrade overlay")));

            yield return Step("C-15", "Yükseltmeyi iptal et",
                "Çıkış tuşuna basarak süren yükseltmeyi iptal et ve iadeyi kontrol et.",
                "Süren yükseltmeyi iptal et; %75 iadeyi gör.",
                site =>
                {
                    float expectedParts = -1f, expectedMaterial = -1f;
                    string materialId = null;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            DrillStructure drill = site.FindDrill();
                            if (drill == null || materialId == null || expectedParts < 0f) return false;
                            // A cancel consumes the whole reservation and hands floor(75%)
                            // of it back as fresh stock -- so the hold ends up below where
                            // it started, by exactly the quarter that was forfeited.
                            return drill.ActiveUpgradeJob == null
                                   && drill.Runtime.Tier == 1
                                   && Mathf.Approximately(site.Cargo.Get(ProcessingCatalog.BasicPartId), expectedParts)
                                   && Mathf.Approximately(site.Cargo.Get(materialId), expectedMaterial);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            DrillStructure drill = site.InstallDrill(DrillLifecycle.Active);
                            if (drill == null || site.Profile == null) return;
                            site.ModuleTiers.SetTier(DrillUpgradeCatalog.GateModuleId, 2);
                            site.GrantAll(DrillUpgradeCatalog.Cost(site.Profile.Family));
                            drill.StartUpgrade(2, site.Cargo);
                            materialId = DrillUpgradeCatalog.MaterialIdFor(site.Profile.Family);
                            expectedParts = site.Cargo.Get(ProcessingCatalog.BasicPartId)
                                            - DrillUpgradeCatalog.PartsCost
                                            + Mathf.Floor(DrillUpgradeCatalog.PartsCost * DrillUpgradeCatalog.RefundFraction);
                            expectedMaterial = site.Cargo.Get(materialId)
                                               - DrillUpgradeCatalog.MaterialCost
                                               + Mathf.Floor(DrillUpgradeCatalog.MaterialCost * DrillUpgradeCatalog.RefundFraction);
                        });
                },
                checklist: new[] { "Yükseltme işi bitsin", "Ayrılan kaynağın %75'i geri gelsin", "Kademe T1 kalsın" },
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.ExitKey));

            yield return Step("C-16", "Yükseltmeyi tamamla",
                "Yükseltmeyi yeniden başlat ve sondaj T2'ye çıkana kadar bekle.",
                "Sondaj T2 olsun; depo kapasitesi artsın.",
                site =>
                {
                    float storedBaseline = -1f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            DrillStructure drill = site.FindDrill();
                            if (drill == null || site.Profile == null || storedBaseline < 0f) return false;
                            string owner = DrillUpgradeService.OwnerId(drill.WorldId, drill.NodeId);
                            return drill.Runtime.Tier == 2
                                   && Mathf.Approximately(drill.Runtime.StorageCapacity,
                                       ExtractionCatalog.T2StorageCapacity(site.Profile.Rarity))
                                   && Mathf.Approximately(drill.Runtime.StoredAmount, storedBaseline)
                                   && Mathf.Approximately(
                                       site.Cargo.GetOwnerReservation(owner, ProcessingCatalog.BasicPartId), 0f);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            DrillStructure drill = site.InstallDrill(DrillLifecycle.Active, stored: 7f);
                            if (drill == null || site.Profile == null) return;
                            site.ModuleTiers.SetTier(DrillUpgradeCatalog.GateModuleId, 2);
                            site.GrantAll(DrillUpgradeCatalog.Cost(site.Profile.Family));
                            drill.StartUpgrade(2, site.Cargo);
                            storedBaseline = drill.Runtime.StoredAmount;
                        });
                },
                checklist: new[] { "Kademe 2 olsun", "Depo kapasitesi artsın", "Depolanan kaynak korunsun" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.drill.t2", "the upgraded T2 drill body")));

            yield return Step("C-17", "Uzak sistem davranışını öğren",
                "Sistemden ayrıl ve sondajın uzaktan da üretmeye devam ettiğini gör.",
                "Uzaktaki sondajın üretmeye devam ettiğini gör.",
                site =>
                {
                    float storedBaseline = -1f;
                    string remoteSystemId = null;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            if (remoteSystemId == null || storedBaseline < 0f) return false;
                            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(remoteSystemId);
                            if (delta == null || delta.drills.Count == 0) return false;
                            if (StarSystemRegistry.Streaming != null
                                && StarSystemRegistry.Streaming.GetState(remoteSystemId) == SystemStreamingState.Full)
                                return false;
                            SystemLinkState link = SystemCommunicationResolver.Resolve(remoteSystemId,
                                StarSystemRegistry.Streaming?.FullSystemId, false, false, true, true);
                            return delta.drills[0].stored > storedBaseline + 0.0001f
                                   && link != SystemLinkState.Disconnected;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            remoteSystemId = site.SystemId;
                            SystemDelta delta = StarSystemRegistry.Deltas?.TryGet(remoteSystemId);
                            if (delta == null)
                            {
                                delta = new SystemDelta { systemId = remoteSystemId };
                                StarSystemRegistry.Deltas?.Set(delta);
                            }
                            if (delta.drills.Count == 0)
                            {
                                delta.drills.Add(new DrillDto
                                {
                                    worldId = site.WorldId,
                                    nodeId = site.Profile != null ? site.Profile.DrillNode.Id : null,
                                    state = (int)DrillLifecycle.Active,
                                    stored = 0f,
                                    tier = 1
                                });
                            }
                            storedBaseline = delta.drills[0].stored;
                        },
                        reject: _ =>
                        {
                            if (remoteSystemId == null) return null;
                            SystemLinkState link = SystemCommunicationResolver.Resolve(remoteSystemId,
                                StarSystemRegistry.Streaming?.FullSystemId, false, false, true,
                                StarSystemRegistry.Deltas?.TryGet(remoteSystemId) != null);
                            return link switch
                            {
                                SystemLinkState.Disconnected => "BU SİSTEMLE BAĞLANTI YOK — KOMUTA AĞI DIŞINDA",
                                SystemLinkState.LastKnown => "SON BİLİNEN VERİ — CANLI BAĞLANTI YOK, KOMUT VERİLEMEZ",
                                _ => null,
                            };
                        });
                },
                checklist: new[] { "Sistem bağlantı durumu okunsun", "Uzaktaki sondajın deposu artsın" });
        }

        /// <summary>
        /// The real attention inputs for this system, built the way
        /// <c>AttentionQuery.BuildInputs</c> builds them for the drills-full case:
        /// every Active drill on the site's planet is at capacity.
        /// </summary>
        private static SystemAttentionInputs DrillsFullInputs(TutorialSite site)
        {
            bool anyActive = false, allFull = true;
            foreach (DrillStructure drill in ExtractionState.Drills)
            {
                if (drill == null || drill.Runtime.State != DrillLifecycle.Active) continue;
                anyActive = true;
                if (!drill.Runtime.IsFull) allFull = false;
            }
            return new SystemAttentionInputs
            {
                AllActiveDrillsStalledFull = anyActive && allFull,
                MinRecoveryTimeRemaining = float.MaxValue,
                RaidEtaSeconds = float.MaxValue,
                IsLive = true,
            };
        }

        private static TutorialStepDefinition Step(string id, string title, string instruction, string summary,
            Func<TutorialSite, ITutorialStepRuntime> build, string[] checklist = null,
            TutorialAssetRequirement[] assets = null, Func<string> keyHint = null) =>
            new TutorialStepDefinition(TutorialChapterId.Extraction, id, title, instruction, summary,
                () => build(new TutorialSite()), checklist, assets, keyHint);
    }
}
