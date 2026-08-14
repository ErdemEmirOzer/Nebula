using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Progression;
using Nebula.Ship;
using Nebula.UI.Controls;
using Nebula.World.Composition;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// The reading and vocabulary layer chapters F, H, I and J share.
    /// <para>
    /// Two jobs, both of which exist so a step body stays one honest sentence
    /// about real state. First, <b>safe access</b>: the tutorial's
    /// <see cref="NebulaContext"/> deliberately leaves Presentation, Telemetry,
    /// Audio and the audio bridge unset, and a set-once property throws when it
    /// is read early -- so every service a step wants is read through
    /// <see cref="Read{T}"/>, which answers <c>null</c> instead of throwing and
    /// never names one of those four. Second, <b>the Turkish refusal
    /// vocabulary</b>: the hangar, escort, engineer and resupply reject enums
    /// have no Turkish table anywhere in production, so one is built here, in
    /// the tutorial layer, switching on the real enum -- never by editing a
    /// production file, and never by inventing a refusal the game did not make.
    /// </para>
    /// <para>
    /// The <c>Diagnose*</c> methods are the second half of that promise. A step
    /// does not perform the player's command, so it cannot read the command's
    /// return value; instead each diagnoser re-evaluates the <i>same gates, in
    /// the same order, against the same live state</i> as the production
    /// service it mirrors, and mutates nothing. Where production already
    /// exposes a real non-mutating check (<c>ShipCombat.CheckRepairStart</c>,
    /// <c>ShipPlanetEvacuation.CanReclaim</c>, <c>CollectorRouteService.CheckCreate</c>)
    /// that real check is called instead of being mirrored.
    /// </para>
    /// </summary>
    internal static class TutorialFhij
    {
        // ---- safe service access ---------------------------------------------

        /// <summary>
        /// Reads one composed service, or <c>null</c>. The catch is not
        /// defensive noise: an unset set-once property throwing is the documented
        /// behaviour of <see cref="NebulaContext"/>, and a tutorial step reached
        /// from BÖLÜMLER may legitimately run before a service the sequential
        /// path would have had.
        /// </summary>
        private static T Read<T>(TutorialStepContext context, Func<NebulaContext, T> get) where T : class
        {
            if (context == null || context.Gameplay == null) return null;
            try { return get(context.Gameplay); }
            catch (InvalidOperationException) { return null; }
        }

        internal static ShipHangar Hangar(TutorialStepContext context) => Read(context, g => g.Hangar);
        internal static ShipCombat Combat(TutorialStepContext context) => Read(context, g => g.ShipCombat);
        internal static ShipController Ship(TutorialStepContext context) => Read(context, g => g.Ship);
        internal static InterventionGroupDirector Intervention(TutorialStepContext context) =>
            Read(context, g => g.InterventionGroupDirector);

        internal static CombatEncounterDirector Director(TutorialStepContext context)
        {
            ShipCombat combat = Combat(context);
            return combat != null ? combat.Director : null;
        }

        internal static ThreatPressureState Pressure(TutorialStepContext context)
        {
            CombatEncounterDirector director = Director(context);
            return director != null ? director.Pressure : null;
        }

        internal static PlanetaryDefenseCoordinator Defense(TutorialStepContext context)
        {
            CombatEncounterDirector director = Director(context);
            return director != null ? director.DefenseCoordinator : null;
        }

        internal static LogisticsCargo Cargo(TutorialStepContext context)
        {
            ShipController ship = Ship(context);
            if (ship == null) return null;
            ShipLogistics logistics = ship.GetComponent<ShipLogistics>();
            return logistics != null ? logistics.Cargo : null;
        }

        internal static Func<string> KeyHint(CombatAction action) =>
            () => NebulaControlBindings.KeyLabel(NebulaControlBindings.KeyFor(action));

        /// <summary>
        /// A real planet id this step can raise to Controlled when a prerequisite needs
        /// one: the ship's current extraction target if it has one, otherwise the first
        /// planet dominance already knows about, in canonical id order so two runs of
        /// the same step pick the same planet. Null when the session has no planet at
        /// all, which is a step that cannot need this prerequisite anyway.
        /// </summary>
        internal static string PrerequisiteWorldId(TutorialStepContext context)
        {
            ShipController ship = Ship(context);
            ShipExtraction extraction = ship != null ? ship.GetComponent<ShipExtraction>() : null;
            if (extraction != null && extraction.TargetPlanet != null) return extraction.TargetPlanet.WorldId;

            string best = null;
            foreach (KeyValuePair<string, PlanetDominance> kv in ExtractionState.AllDominance)
                if (best == null || string.CompareOrdinal(kv.Key, best) < 0) best = kv.Key;
            if (best != null) return best;

            foreach (PlanetIdentity planet in ExtractionState.AllPlanets)
                if (planet != null && !string.IsNullOrEmpty(planet.WorldId)
                    && (best == null || string.CompareOrdinal(planet.WorldId, best) < 0)) best = planet.WorldId;
            return best;
        }

        // ---- prerequisite installation (never the step's own success) ---------

        /// <summary>
        /// Raises the character level through the real progression service. XP is
        /// awarded, not written: <see cref="ProgressionService.Award"/> is the
        /// only way a level ever changes in this project, and a distinct ledger
        /// id per grant keeps the award idempotent the way every other caller's is.
        /// </summary>
        internal static void EnsureLevel(int level)
        {
            int guard = 0;
            while (ProgressionService.State.Level < level && guard < 400)
            {
                guard++;
                ProgressionService.Award($"tutorial.prep.level.{guard}", 4000);
            }
        }

        internal static void EnsureHangarTier(ShipHangar hangar, int tier)
        {
            if (hangar == null) return;
            if (hangar.Production == null) hangar.InitializeNewCampaign();
            if (hangar.HangarTier < tier) hangar.ApplyHangarTier(tier);
        }

        /// <summary>
        /// Satisfies VS-015 §1's "at least one Controlled planet" requirement through
        /// real dominance state, and puts it back at cleanup.
        /// <para>
        /// P1-ECO-013 made that gate live. A step whose subject is an Engineer has to
        /// set the world up for one the same way it sets up a level, a hangar tier or a
        /// hold — by making the world satisfy the gate, never by leaving the gate out
        /// of the mirror or the production path. This is the identical device
        /// <c>ChapterGSteps</c> already uses for the Command T2 upgrade's own
        /// Controlled-planet requirement.
        /// </para>
        /// </summary>
        internal static void EnsureControlledPlanet(TutorialStepContext context, string worldId)
        {
            if (ExtractionState.HasAnyControlledPlanet() || string.IsNullOrEmpty(worldId)) return;

            PlanetDominance dominance = ExtractionState.GetDominance(worldId);
            DominanceState previousState = dominance.State;
            float previousStabilize = dominance.StabilizeTimer;
            float previousRecovery = dominance.RecoveryTimer;
            bool previousAbandoned = dominance.Abandoned;

            dominance.Restore(DominanceState.Controlled, 0f, 0f);
            context?.Scope?.OnCleanup(() =>
                ExtractionState.GetDominance(worldId)
                    .Restore(previousState, previousStabilize, previousRecovery, previousAbandoned));
        }

        /// <summary>Tops a real item up to a minimum <i>available</i> (unreserved) amount, widening capacity first so the add is never silently clipped.</summary>
        internal static void EnsureItem(LogisticsCargo cargo, string itemId, float minimumAvailable)
        {
            if (cargo == null || string.IsNullOrEmpty(itemId)) return;
            float missing = minimumAvailable - cargo.GetAvailable(itemId);
            if (missing <= 0f) return;
            float needed = LogisticsCargo.VolumePerUnit(itemId) * missing;
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, cargo.UsedVolume + needed + 1f));
            cargo.Add(itemId, missing);
        }

        /// <summary>The first processed material the cargo can actually spare that much of, or <c>null</c>. Mirrors what a material picker would offer.</summary>
        internal static string AffordableMaterial(LogisticsCargo cargo, float amount)
        {
            if (cargo == null) return null;
            string[] ids = VehicleCatalog.SpecialMaterialIds;
            for (int i = 0; i < ids.Length; i++)
                if (cargo.GetAvailable(ids[i]) >= amount) return ids[i];
            return null;
        }

        internal static VehicleAgent FirstVehicle(ShipHangar hangar, VehicleClass vehicleClass,
            Func<VehicleAgent, bool> where = null)
        {
            if (hangar == null) return null;
            IReadOnlyList<VehicleAgent> all = hangar.Vehicles;
            for (int i = 0; i < all.Count; i++)
            {
                VehicleAgent v = all[i];
                if (v == null || v.Def == null || v.Def.Class != vehicleClass) continue;
                if (where != null && !where(v)) continue;
                return v;
            }
            return null;
        }

        /// <summary>
        /// Builds one vehicle through the real production slot and the real
        /// <see cref="HangarProductionService.Tick"/>, so a prerequisite vehicle
        /// exists exactly the way the player's own would -- and hands it to the
        /// step scope, so it cannot outlive the step that made it. Returns the
        /// vehicle, or null when production refused: prepare never forces a spawn.
        /// </summary>
        internal static VehicleAgent BuildVehicleForPrerequisite(TutorialStepContext context,
            VehicleClass vehicleClass, int mk = 1)
        {
            ShipHangar hangar = Hangar(context);
            LogisticsCargo cargo = Cargo(context);
            if (hangar == null || hangar.Production == null) return null;

            VehicleAgent existing = FirstVehicle(hangar, vehicleClass, v => v.Def.Mk == mk);
            if (existing != null) { ReleaseVehicleOnCleanup(context, existing); return existing; }

            VehicleDef def = VehicleCatalog.Get(vehicleClass, mk);
            if (def == null) return null;

            EnsureLevel(def.UnlockLevel);
            EnsureHangarTier(hangar, def.RequiredHangarTier);
            EnsureItem(cargo, ProcessingCatalog.BasicPartId, def.BasicPartCost);
            EnsureItem(cargo, VehicleCatalog.MetalAlloyId, def.SpecialMaterialCost);
            if (def.RequiresControlledPlanet) EnsureControlledPlanet(context, PrerequisiteWorldId(context));

            if (hangar.Production.ActiveJob != null) hangar.CancelProduction();
            if (hangar.TryStartProduction(vehicleClass, VehicleCatalog.MetalAlloyId, mk) != HangarProductionRejectReason.None)
                return null;

            hangar.Production.Tick(def.BuildSeconds + 1f);
            VehicleAgent built = FirstVehicle(hangar, vehicleClass, v => v.Def.Mk == mk);
            ReleaseVehicleOnCleanup(context, built);
            return built;
        }

        /// <summary>
        /// Hands a vehicle to the step scope: at cleanup it gives back the command
        /// slot it was holding, drops its route, leaves the hangar roster and the
        /// process-wide <see cref="VehicleAgent.Registry"/>, and is destroyed.
        /// <para>
        /// A vehicle is the one thing a step spawns that keeps <i>running</i> after
        /// the step ends: <c>VehicleAgent.Update</c> animates a launch off
        /// <c>ship.transform</c>, so a leaked agent throws the moment the session
        /// tears the ship down. It is deactivated before it is destroyed because
        /// destruction is deferred in play mode, and an active agent would take one
        /// more <c>Update</c> in between -- which is exactly the frame that throws.
        /// </para>
        /// <para>
        /// The command-slot release is not bookkeeping tidiness: a launched vehicle
        /// only ever returns its slot by docking (<c>ShipHangar.OnVehicleDocked</c>),
        /// so destroying one mid-mission would leak capacity, and after a few steps
        /// the next launch would be refused with <c>NoCommandCapacity</c>.
        /// </para>
        /// </summary>
        internal static void ReleaseVehicleOnCleanup(TutorialStepContext context, VehicleAgent vehicle)
        {
            if (context == null || vehicle == null) return;
            ShipHangar hangar = Hangar(context);
            string vehicleId = vehicle.VehicleId;

            context.Scope.OnCleanup(() =>
            {
                if (vehicle == null) return;

                bool holdsCommandSlot = vehicle.Mission.Phase != VehiclePhase.InHangar
                                        || vehicle.EngineerTask != null || vehicle.EscortState != null
                                        || vehicle.ResupplyTask != null || vehicle.Route != null;
                if (vehicle.Route != null) LogisticsState.RemoveRoute(vehicle.Route);
                if (hangar != null)
                {
                    if (holdsCommandSlot) hangar.Command.Release(vehicle.Def != null ? vehicle.Def.CommandCost : 1);
                    hangar.RemoveVehicleReference(vehicle);
                }
                if (!string.IsNullOrEmpty(vehicleId)
                    && VehicleAgent.Registry.TryGetValue(vehicleId, out VehicleAgent current) && current == vehicle)
                    VehicleAgent.Registry.Remove(vehicleId);

                vehicle.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(vehicle.gameObject);
                else UnityEngine.Object.DestroyImmediate(vehicle.gameObject);
            });
        }

        // ---- Turkish refusal vocabulary, switched on the real enums -----------

        internal static string RejectText(HangarProductionRejectReason reason) => reason switch
        {
            HangarProductionRejectReason.UnknownVehicle => "BİLİNMEYEN ARAÇ",
            HangarProductionRejectReason.LevelTooLow => "SEVİYE YETERSİZ",
            HangarProductionRejectReason.HangarTierTooLow => "HANGAR SEVİYESİ YETERSİZ",
            HangarProductionRejectReason.QueueBusy => "ÜRETİM KUYRUĞU DOLU",
            HangarProductionRejectReason.MaterialNotSelected => "MALZEME SEÇİLMEDİ",
            HangarProductionRejectReason.MissingControlledPlanet => "KONTROL EDİLEN GEZEGEN YOK",
            HangarProductionRejectReason.InsufficientResources => "KAYNAK YETERSİZ",
            _ => null
        };

        internal static string RejectText(HangarUpgradeRejectReason reason) => reason switch
        {
            HangarUpgradeRejectReason.UnknownVehicle => "BİLİNMEYEN ARAÇ",
            HangarUpgradeRejectReason.NotMkI => "SADECE MK I YÜKSELTİLEBİLİR",
            HangarUpgradeRejectReason.LevelTooLow => "SEVİYE YETERSİZ",
            HangarUpgradeRejectReason.HangarTierTooLow => "HANGAR SEVİYESİ YETERSİZ",
            HangarUpgradeRejectReason.QueueBusy => "ÜRETİM YUVASI DOLU",
            HangarUpgradeRejectReason.VehicleNotInHangar => "ARAÇ HANGARDA DEĞİL",
            HangarUpgradeRejectReason.VehicleCargoNotEmpty => "ARAÇ KARGOSU BOŞ OLMALI",
            HangarUpgradeRejectReason.VehicleHasActiveRoute => "ARAÇ MEŞGUL — AKTİF GÖREV VAR",
            HangarUpgradeRejectReason.InsufficientResources => "KAYNAK YETERSİZ",
            _ => null
        };

        internal static string RejectText(ShipHangar.LaunchRejectReason reason) => reason switch
        {
            ShipHangar.LaunchRejectReason.VehicleNotInHangar => "ARAÇ ZATEN GÖREVDE",
            ShipHangar.LaunchRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ DOLU",
            ShipHangar.LaunchRejectReason.TargetNotInShipSystem => "BU SİSTEMDE UYGUN HEDEF YOK",
            _ => null
        };

        internal static string RejectText(RouteCreateRejectReason reason) => reason switch
        {
            RouteCreateRejectReason.CenterNotActive => "LOJİSTİK MERKEZİ AKTİF DEĞİL",
            RouteCreateRejectReason.RouteCapReached => "ROTA LİMİTİ DOLU",
            RouteCreateRejectReason.VehicleNotCollector => "SADECE COLLECTOR ROTA ALABİLİR",
            RouteCreateRejectReason.VehicleNotEligible => "ARAÇ UYGUN DEĞİL",
            RouteCreateRejectReason.VehicleAlreadyRouted => "ARAÇ ZATEN ROTADA",
            RouteCreateRejectReason.DrillAlreadyRouted => "SONDAJ ZATEN ROTADA",
            RouteCreateRejectReason.DrillNotActive => "SONDAJ AKTİF DEĞİL",
            RouteCreateRejectReason.NotSameSystem => "AYNI SİSTEMDE DEĞİL",
            RouteCreateRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ YETERSİZ",
            _ => null
        };

        internal static string RejectText(ShipHangar.EngineerSendRejectReason reason) => reason switch
        {
            ShipHangar.EngineerSendRejectReason.VehicleNotEligible => "ARAÇ UYGUN DEĞİL",
            ShipHangar.EngineerSendRejectReason.TargetNotEligible => "HEDEF UYGUN DEĞİL",
            ShipHangar.EngineerSendRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ YETERSİZ",
            ShipHangar.EngineerSendRejectReason.NothingToRepair => "ONARILACAK BİR ŞEY YOK",
            ShipHangar.EngineerSendRejectReason.InsufficientResources => "YETERSİZ KAYNAK",
            ShipHangar.EngineerSendRejectReason.TargetHasNoDurability => "BU HEDEF ONARILAMAZ (SONDAJ)",
            _ => null
        };

        internal static string RejectText(ShipHangar.EscortAssignRejectReason reason) => reason switch
        {
            ShipHangar.EscortAssignRejectReason.VehicleNotEligible => "ARAÇ UYGUN DEĞİL",
            ShipHangar.EscortAssignRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ YETERSİZ",
            _ => null
        };

        internal static string RejectText(ShipHangar.MaintenanceResupplyRejectReason reason) => reason switch
        {
            ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible => "ARAÇ UYGUN DEĞİL (MK II GEREKLİ)",
            ShipHangar.MaintenanceResupplyRejectReason.TargetNotEligible => "SİSTEMDE AKTİF KOMUTA MERKEZİ YOK",
            ShipHangar.MaintenanceResupplyRejectReason.InsufficientCargo => "GEMİ KARGOSUNDA UYGUN MALZEME YOK",
            ShipHangar.MaintenanceResupplyRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ YETERSİZ",
            _ => null
        };

        internal static string RejectText(RepairRejectReason reason) => reason switch
        {
            RepairRejectReason.InCombat => "ÇATIŞMADAYKEN ONARILAMAZ",
            RepairRejectReason.TooSoonAfterDamage => "HASARDAN SONRA 8 SANİYE BEKLE",
            RepairRejectReason.TooFast => "ÇOK HIZLI — DUR",
            RepairRejectReason.EmergencyShutdown => "ACİL KAPANMA — SİSTEMLER KAPALI",
            RepairRejectReason.AlreadyActive => "ONARIM ZATEN SÜRÜYOR",
            RepairRejectReason.AlreadyAtCap => "ZATEN TAM ONARIMDA",
            RepairRejectReason.InsufficientResources => "YETERSİZ KAYNAK",
            _ => null
        };

        internal static string RejectText(RouteWaitReason reason) => reason switch
        {
            RouteWaitReason.SourceDrillMissing => "KAYNAK SONDAJ BULUNAMADI",
            RouteWaitReason.SourceDrillInactive => "KAYNAK SONDAJ AKTİF DEĞİL",
            RouteWaitReason.SourceDrillEvacuated => "KAYNAK GEZEGEN TAHLİYE EDİLDİ",
            RouteWaitReason.SourceDrillContested => "KAYNAK GEZEGEN ÇEKİŞMELİ — TOPLAMA DURDU",
            RouteWaitReason.CenterFull => "MERKEZ DOLU",
            RouteWaitReason.CenterDisabled => "MERKEZ DEVRE DIŞI",
            _ => null
        };

        internal static string RejectText(ReclaimRejectReason reason) =>
            ShipPlanetEvacuation.RejectMessage(reason);

        // ---- non-mutating diagnosers, mirroring the real gates ---------------

        /// <summary>Mirrors <see cref="HangarProductionService.TryStartProduction"/>'s gates, in order, without reserving anything.</summary>
        internal static HangarProductionRejectReason DiagnoseProduction(ShipHangar hangar, LogisticsCargo cargo,
            VehicleClass vehicleClass, int mk, string chosenMaterialId)
        {
            VehicleDef def = VehicleCatalog.Get(vehicleClass, mk);
            if (def == null) return HangarProductionRejectReason.UnknownVehicle;
            if (hangar == null || hangar.Production == null) return HangarProductionRejectReason.UnknownVehicle;
            if (hangar.Production.ActiveJob != null) return HangarProductionRejectReason.QueueBusy;
            if (ProgressionService.State.Level < def.UnlockLevel) return HangarProductionRejectReason.LevelTooLow;
            if (hangar.HangarTier < def.RequiredHangarTier) return HangarProductionRejectReason.HangarTierTooLow;
            // P1-ECO-013: the real service applies this gate now, so the mirror must
            // too — a diagnoser that is missing a gate tells the player the build would
            // succeed and then watches it refuse.
            if (def.RequiresControlledPlanet && !ExtractionState.HasAnyControlledPlanet())
                return HangarProductionRejectReason.MissingControlledPlanet;
            if (string.IsNullOrEmpty(chosenMaterialId)
                || Array.IndexOf(VehicleCatalog.SpecialMaterialIds, chosenMaterialId) < 0)
                return HangarProductionRejectReason.MaterialNotSelected;
            if (cargo == null) return HangarProductionRejectReason.InsufficientResources;
            if (cargo.GetAvailable(ProcessingCatalog.BasicPartId) < def.BasicPartCost)
                return HangarProductionRejectReason.InsufficientResources;
            if (cargo.GetAvailable(chosenMaterialId) < def.SpecialMaterialCost)
                return HangarProductionRejectReason.InsufficientResources;
            return HangarProductionRejectReason.None;
        }

        /// <summary>Mirrors <see cref="ShipHangar.TryStartVehicleUpgrade"/>'s eligibility gates, in order, without reserving anything.</summary>
        internal static HangarUpgradeRejectReason DiagnoseUpgrade(ShipHangar hangar, VehicleAgent vehicle, LogisticsCargo cargo)
        {
            if (vehicle == null || vehicle.Def == null) return HangarUpgradeRejectReason.UnknownVehicle;
            if (!VehicleCatalog.TryGetUpgradeRecipe(vehicle.Def.Id, out VehicleUpgradeRecipe recipe))
                return HangarUpgradeRejectReason.NotMkI;
            if (!vehicle.Mission.IsIdleInHangar) return HangarUpgradeRejectReason.VehicleNotInHangar;
            if (vehicle.HeldAmount > 0.0001f) return HangarUpgradeRejectReason.VehicleCargoNotEmpty;
            if (LogisticsState.FindRouteByVehicle(vehicle.VehicleId) != null)
                return HangarUpgradeRejectReason.VehicleHasActiveRoute;
            if (vehicle.EngineerTask != null || vehicle.EscortState != null || vehicle.ResupplyTask != null)
                return HangarUpgradeRejectReason.VehicleHasActiveRoute;

            VehicleDef target = VehicleCatalog.Get(recipe.ToVehicleId);
            if (target == null) return HangarUpgradeRejectReason.UnknownVehicle;
            if (hangar == null || hangar.Production == null) return HangarUpgradeRejectReason.UnknownVehicle;
            if (hangar.Production.ActiveJob != null) return HangarUpgradeRejectReason.QueueBusy;
            if (ProgressionService.State.Level < target.UnlockLevel) return HangarUpgradeRejectReason.LevelTooLow;
            if (hangar.HangarTier < target.RequiredHangarTier) return HangarUpgradeRejectReason.HangarTierTooLow;
            if (cargo == null) return HangarUpgradeRejectReason.InsufficientResources;
            foreach ((string itemId, float amount) in recipe.FullCost())
                if (cargo.GetAvailable(itemId) < amount) return HangarUpgradeRejectReason.InsufficientResources;
            return HangarUpgradeRejectReason.None;
        }

        /// <summary>Mirrors <see cref="ShipHangar.TryLaunch"/>'s gates without reserving a command slot.</summary>
        internal static ShipHangar.LaunchRejectReason DiagnoseLaunch(ShipHangar hangar, VehicleAgent vehicle, string targetWorldId)
        {
            if (hangar == null) return ShipHangar.LaunchRejectReason.VehicleNotInHangar;
            if (vehicle == null || !vehicle.Mission.CanLaunch || vehicle.EngineerTask != null
                || vehicle.EscortState != null || vehicle.ResupplyTask != null)
                return ShipHangar.LaunchRejectReason.VehicleNotInHangar;

            string systemId = hangar.CurrentSystemId;
            string targetSystemId = Sectors.WorldIdCodec.SystemIdFromPlanetWorldId(targetWorldId);
            if (systemId == null || targetSystemId == null || systemId != targetSystemId)
                return ShipHangar.LaunchRejectReason.TargetNotInShipSystem;
            if (hangar.Command.Capacity - hangar.Command.Used < vehicle.Def.CommandCost)
                return ShipHangar.LaunchRejectReason.NoCommandCapacity;
            return ShipHangar.LaunchRejectReason.None;
        }

        /// <summary>Mirrors <see cref="ShipHangar.TryAssignEscort"/>'s two gates without reserving a command slot.</summary>
        internal static ShipHangar.EscortAssignRejectReason DiagnoseEscortAssign(ShipHangar hangar, VehicleAgent vehicle)
        {
            if (hangar == null) return ShipHangar.EscortAssignRejectReason.VehicleNotEligible;
            if (vehicle == null || vehicle.Def == null || vehicle.Def.Class != VehicleClass.Escort)
                return ShipHangar.EscortAssignRejectReason.VehicleNotEligible;
            if (vehicle.EscortState == null && !vehicle.Mission.CanLaunch)
                return ShipHangar.EscortAssignRejectReason.VehicleNotEligible;
            if (vehicle.EscortState == null && hangar.Command.Capacity - hangar.Command.Used < vehicle.Def.CommandCost)
                return ShipHangar.EscortAssignRejectReason.NoCommandCapacity;
            return ShipHangar.EscortAssignRejectReason.None;
        }

        // ---- world readings shared by several chapters ------------------------

        /// <summary>A planet's scan flag, or false when no manifest resolves for it. Never throws out of a validator.</summary>
        internal static bool IsScanned(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return false;
            try
            {
                PlanetProfile profile = ExtractionState.GetProfile(worldId);
                return profile != null && profile.Scanned;
            }
            catch (InvalidOperationException) { return false; }
        }

        internal static PlanetProfile Profile(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return null;
            try { return ExtractionState.GetProfile(worldId); }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>
        /// The four dominance conditions, read exactly the way
        /// <c>DominanceSystem</c> reads them every frame. Duplicated as a read,
        /// never as a second authority: nothing here writes dominance state.
        /// </summary>
        internal static DominanceConditions Conditions(string worldId)
        {
            var conditions = new DominanceConditions();
            PlanetProfile profile = Profile(worldId);
            if (profile == null) return conditions;
            conditions.Scanned = profile.Scanned;
            conditions.ActiveDrill = ExtractionState.HasActiveDrill(worldId);
            conditions.ActiveCommand = ExtractionState.HasActiveCommand(worldId);
            conditions.ActiveDefense = ExtractionState.ActiveDefenseCount(worldId) > 0;
            conditions.CommandDisabled = ExtractionState.CommandDisabled(worldId);
            return conditions;
        }

        internal static SurfaceStructure FirstStructure(string worldId, SurfaceStructureType type)
        {
            IReadOnlyList<SurfaceStructure> all = ExtractionState.Structures;
            for (int i = 0; i < all.Count; i++)
            {
                SurfaceStructure s = all[i];
                if (s == null || s.Type != type) continue;
                if (worldId != null && s.WorldId != worldId) continue;
                return s;
            }
            return null;
        }

        internal static SurfaceStructure AnyStructure(SurfaceStructureType type) => FirstStructure(null, type);

        internal static DrillStructure AnyDrill()
        {
            IReadOnlyList<DrillStructure> all = ExtractionState.Drills;
            for (int i = 0; i < all.Count; i++) if (all[i] != null) return all[i];
            return null;
        }

        internal static EnemyAgent FirstEnemy(string defId)
        {
            foreach (EnemyAgent agent in EnemyAgent.All.Values)
            {
                if (agent == null || agent.Destroyed) continue;
                if (defId != null && (agent.Def == null || agent.Def.Id != defId)) continue;
                return agent;
            }
            return null;
        }

        internal static int LiveEnemyCount(string defId)
        {
            int count = 0;
            foreach (EnemyAgent agent in EnemyAgent.All.Values)
            {
                if (agent == null || agent.Destroyed) continue;
                if (defId != null && (agent.Def == null || agent.Def.Id != defId)) continue;
                count++;
            }
            return count;
        }

        internal static float DurabilityTotal(Durability durability) =>
            durability == null ? 0f : durability.Shield + durability.Armor + durability.Hull;

        /// <summary>
        /// Spawns a real enemy through the real encounter director, or returns
        /// null and leaves the world untouched when no director is composed.
        /// Prepare-only: no step ever completes because an enemy exists.
        /// </summary>
        internal static EnemyAgent SpawnEnemy(TutorialStepContext context, string defId, Vector3 offsetFromShip)
        {
            CombatEncounterDirector director = Director(context);
            ShipController ship = Ship(context);
            if (director == null || ship == null) return null;
            return director.SpawnEnemy(defId, ship.transform.position + offsetFromShip);
        }

        /// <summary>Registers an enemy for destruction at step cleanup, so a teaching spawn never outlives the step that made it.</summary>
        internal static void ReleaseOnCleanup(TutorialStepContext context, EnemyAgent agent)
        {
            if (context == null || agent == null) return;
            context.Scope.OnCleanup(() =>
            {
                if (agent == null || agent.gameObject == null) return;
                // Deactivated first: Destroy is deferred in play mode, and
                // EnemyAgent.Update dereferences the player ship -- which the session
                // may already have torn down by the time the destroy actually lands.
                agent.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(agent.gameObject);
                else UnityEngine.Object.DestroyImmediate(agent.gameObject);
            });
        }
    }

    /// <summary>
    /// Chapter F -- Hangar ve Destek Araçları (curriculum steps 74-93).
    /// <para>
    /// Every step here is written against <c>ShipHangar</c>'s own command seams and
    /// validates the real state those seams produce. That was originally forced --
    /// the Fleet page's single Produce button called
    /// <c>TryStartProduction(VehicleClass.Scout, null)</c>, which the service
    /// rejects outright, so no class was buildable from the interface at all -- and
    /// it is now simply the right level to validate at: the page has since grown a
    /// two-stage picker that calls the same seams with a real material, and because
    /// these steps never watched a button or a caption, none of them had to change
    /// when it did. Nothing is faked and no refusal is invented.
    /// </para>
    /// </summary>
    public static class ChapterFSteps
    {
        private const TutorialChapterId Chapter = TutorialChapterId.Hangar;

        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return CommandCapacity();      // 74
            yield return ScoutBuild();           // 75
            yield return ScoutLaunch();          // 76
            yield return ScoutScan();            // 77
            yield return CollectorBuild();       // 78
            yield return CollectorRouteStep();   // 79
            yield return VehicleUpgrade();       // 80
            yield return MaintenanceResupply();  // 81
            yield return EngineerBuild();        // 82
            yield return EngineerTarget();       // 83
            yield return SafetyGate();           // 84
            yield return StructureRepair();      // 85
            yield return VehicleRecovery();      // 86
            yield return DrillIsNotRepairable(); // 87
            yield return EscortBuild();          // 88
            yield return EscortProtect();        // 89
            yield return EscortStanceStepDef();  // 90
            yield return EscortCommands();       // 91
            yield return InterventionGroupStep(); // 92
            yield return HangarReturn();         // 93
        }

        // ---- 74 ---------------------------------------------------------------

        private static TutorialStepDefinition CommandCapacity() => new TutorialStepDefinition(
            Chapter, "F-01", "Komuta Kapasitesi",
            "Hangar-Bot modülünü etkinleştir ve komuta kapasiteni aç.",
            "Komuta kapasitesinin nereden geldiğini öğren.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    return hangar != null && hangar.Command.Capacity > 0;
                },
                prepare: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    if (hangar == null) return;
                    // Deliberately NOT ApplyHangarTier: opening the capacity is the
                    // step's own success condition, so preparing it would complete it.
                    if (hangar.Production == null) hangar.InitializeNewCampaign();
                    TutorialFhij.EnsureLevel(VehicleCatalog.Scout.UnlockLevel);
                },
                reject: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    if (hangar == null || hangar.Command.Capacity > 0) return null;
                    return TutorialFhij.RejectText(HangarProductionRejectReason.HangarTierTooLow);
                }),
            checklistTr: new[] { "Filo ekranını aç", "KOMUTA KAPASİTESİ kartını oku", "Kapasitenin 0'dan büyük olduğunu doğrula" });

        // ---- 75 ---------------------------------------------------------------

        private static TutorialStepDefinition ScoutBuild() => new TutorialStepDefinition(
            Chapter, "F-02", "Scout Üret",
            "Hangarda bir Scout üretimi başlat.",
            "Bir işlenmiş malzeme seçip üretim yuvasını doldur.",
            () => TutorialSteps.Poll(
                complete: c => ActiveBuildIs(c, VehicleClass.Scout),
                prepare: c => PrepareBuild(c, VehicleCatalog.Scout),
                reject: c => BuildComplaint(c, VehicleClass.Scout, 1)),
            checklistTr: new[] { "Bir işlenmiş malzeme seç", "Üretimi başlat", "Üretim yuvasının dolduğunu gör" });

        // ---- 76 ---------------------------------------------------------------

        private static TutorialStepDefinition ScoutLaunch() => new TutorialStepDefinition(
            Chapter, "F-03", "Scout'u Göreve Gönder",
            "Üretilen Scout'u aynı sistemdeki bir gezegene gönder.",
            "Aracı seç, göreve gönder ve komuta yuvasının dolduğunu gör.",
            () => TutorialSteps.Poll(
                complete: c => TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Scout,
                    v => v.Mission.Kind == MissionKind.Scout && v.Mission.Phase != VehiclePhase.InHangar) != null,
                prepare: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    TutorialFhij.EnsureHangarTier(hangar, 1);
                    TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Scout);
                },
                reject: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    VehicleAgent scout = TutorialFhij.FirstVehicle(hangar, VehicleClass.Scout);
                    if (scout == null) return "HANGARDA SCOUT YOK";
                    if (scout.Mission.Phase != VehiclePhase.InHangar) return null;
                    ShipHangar.LaunchRejectReason reason =
                        TutorialFhij.DiagnoseLaunch(hangar, scout, FirstPlanetInShipSystem(hangar));
                    return TutorialFhij.RejectText(reason);
                }),
            checklistTr: new[] { "Aracı seç", "GÖREVE GÖNDER'e bas", "Hedef gezegeni seç" });

        // ---- 77 ---------------------------------------------------------------

        private static TutorialStepDefinition ScoutScan() => new ScoutScanStep().Definition;

        /// <summary>
        /// The one F step that must remember something across frames: the target
        /// the Scout was actually sent to, and whether that planet was already
        /// scanned when the step began. Completing on "some planet is scanned"
        /// would credit the player for a scan the ship did earlier.
        /// </summary>
        private sealed class ScoutScanStep : ITutorialStepRuntime
        {
            private TutorialStepContext context;
            private string targetWorldId;
            private bool alreadyScannedAtStart;

            internal TutorialStepDefinition Definition => new TutorialStepDefinition(
                Chapter, "F-04", "Scout Taraması",
                "Scout hedef gezegene ulaşsın ve taramayı bitirsin.",
                "Aracın istasyona geçişini ve taramanın tamamlanmasını izle.",
                () => new ScoutScanStep(),
                checklistTr: new[] { "Aracın istasyona geçtiğini gör", "Tarama ilerlemesini izle", "Gezegenin taranmış olduğunu doğrula" });

            public void Prepare(TutorialStepContext ctx)
            {
                context = ctx;
                ShipHangar hangar = TutorialFhij.Hangar(ctx);
                if (hangar == null) return;

                TutorialFhij.EnsureHangarTier(hangar, 1);
                VehicleAgent scout = TutorialFhij.BuildVehicleForPrerequisite(ctx, VehicleClass.Scout);
                if (scout == null) return;

                if (scout.Mission.Phase == VehiclePhase.InHangar)
                {
                    string target = FirstPlanetInShipSystem(hangar);
                    if (target != null) hangar.TryLaunch(scout, MissionKind.Scout, target);
                }
                targetWorldId = scout.Mission.TargetId;
                alreadyScannedAtStart = TutorialFhij.IsScanned(targetWorldId);
            }

            public void Evaluate(TutorialStepReport report)
            {
                if (context == null) return;
                ShipHangar hangar = TutorialFhij.Hangar(context);
                VehicleAgent scout = TutorialFhij.FirstVehicle(hangar, VehicleClass.Scout);

                bool scannedNow = !alreadyScannedAtStart && TutorialFhij.IsScanned(targetWorldId);
                bool workDone = scout != null && scout.ScanProgress01 >= 1f;
                report.SetObjective(0, scout != null && scout.Mission.Phase == VehiclePhase.OnStation);
                report.SetObjective(1, workDone);
                report.SetObjective(2, scannedNow);

                if (scannedNow || workDone) { report.Complete(); return; }
                if (scout == null) { report.Reject("HANGARDA SCOUT YOK"); return; }
                if (scout.Mission.Phase == VehiclePhase.TravelOut) report.Note("TARAMA MENZİLİ DIŞINDA — YAKLAŞILIYOR");
            }

            public void Cleanup() => context = null;
        }

        // ---- 78 ---------------------------------------------------------------

        private static TutorialStepDefinition CollectorBuild() => new TutorialStepDefinition(
            Chapter, "F-05", "Collector Üret",
            "Hangarda bir Collector üret.",
            "Malzemeyi seç ve Collector üretimini başlat.",
            () => TutorialSteps.Poll(
                complete: c => ActiveBuildIs(c, VehicleClass.Collector)
                               || TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Collector) != null,
                prepare: c => PrepareBuild(c, VehicleCatalog.Collector),
                reject: c => BuildComplaint(c, VehicleClass.Collector, 1)),
            checklistTr: new[] { "Malzemeyi seç", "Üretimi başlat", "Araç listesinde Collector'ü gör" });

        // ---- 79 ---------------------------------------------------------------

        private static TutorialStepDefinition CollectorRouteStep() => new TutorialStepDefinition(
            Chapter, "F-06", "Collector Rotası",
            "Bir sondajdan Lojistik Merkezi'ne kalıcı bir Collector rotası kur.",
            "Kalıcı bir toplama rotası oluştur ve teslimatı izle.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    VehicleAgent collector = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Collector,
                        v => v.Route != null);
                    return collector != null && LogisticsState.FindRouteByVehicle(collector.VehicleId) != null;
                },
                prepare: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    TutorialFhij.EnsureHangarTier(hangar, 1);
                    TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Collector);
                },
                objectives: (c, report) =>
                {
                    VehicleAgent collector = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Collector);
                    CollectorRoute route = collector != null ? collector.Route : null;
                    report.SetObjective(0, route != null);
                    report.SetObjective(1, route != null && route.Phase == RoutePhase.Loading);
                    report.SetObjective(2, collector != null && collector.HeldAmount > 0f);
                },
                reject: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    VehicleAgent collector = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Collector);
                    if (collector == null) return "HANGARDA COLLECTOR YOK";
                    if (collector.Route != null) return null;

                    DrillStructure drill = TutorialFhij.AnyDrill();
                    string systemId = hangar != null ? hangar.CurrentSystemId : null;
                    LogisticsCenter center = systemId != null ? LogisticsState.FindCenter(systemId) : null;
                    RouteCreateRejectReason reason = CollectorRouteService.CheckCreate(
                        center != null && center.Runtime.IsActive,
                        systemId != null ? LogisticsState.RouteCountForSystem(systemId) : 0,
                        center != null ? center.Runtime.Def.MaxRoutes : 0,
                        true,
                        collector.Mission.CanLaunch,
                        LogisticsState.FindRouteByVehicle(collector.VehicleId) != null,
                        drill != null && LogisticsState.FindRouteByDrill(drill.WorldId) != null,
                        drill != null && drill.Runtime.State == DrillLifecycle.Active,
                        drill != null && center != null && systemId != null
                            && Sectors.WorldIdCodec.SystemIdFromPlanetWorldId(drill.WorldId) == systemId,
                        hangar != null && hangar.Command.Capacity - hangar.Command.Used > 0);
                    return TutorialFhij.RejectText(reason);
                }),
            checklistTr: new[] { "Rotayı oluştur", "Yüklemeyi izle", "Teslimatı gör" });

        // ---- 80 ---------------------------------------------------------------

        private static TutorialStepDefinition VehicleUpgrade() => new TutorialStepDefinition(
            Chapter, "F-07", "Aracı Mk II'ye Yükselt",
            "Hangardaki bir Mk I aracı Mk II'ye yükselt.",
            "Yükseltme işini başlat ve aracın Mk II olduğunu gör.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    if (hangar == null) return false;
                    if (hangar.Production?.ActiveJob != null && hangar.Production.ActiveJob.IsUpgrade) return true;
                    return TutorialFhij.FirstVehicle(hangar, VehicleClass.Collector, v => v.Def.Mk >= 2) != null;
                },
                prepare: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    LogisticsCargo cargo = TutorialFhij.Cargo(c);
                    TutorialFhij.EnsureLevel(VehicleCatalog.CollectorMk2.UnlockLevel);
                    TutorialFhij.EnsureHangarTier(hangar, VehicleCatalog.CollectorMk2.RequiredHangarTier);
                    TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Collector);
                    if (VehicleCatalog.TryGetUpgradeRecipe(VehicleCatalog.CollectorId, out VehicleUpgradeRecipe recipe))
                        foreach ((string itemId, float amount) in recipe.FullCost())
                            TutorialFhij.EnsureItem(cargo, itemId, amount);
                },
                reject: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    VehicleAgent vehicle = TutorialFhij.FirstVehicle(hangar, VehicleClass.Collector, v => v.Def.Mk == 1);
                    if (vehicle == null) return null;
                    return TutorialFhij.RejectText(TutorialFhij.DiagnoseUpgrade(hangar, vehicle, TutorialFhij.Cargo(c)));
                }),
            checklistTr: new[] { "Aracı seç", "MK II'YE YÜKSELT'e bas", "Yükseltmenin tamamlanmasını bekle" });

        // ---- 81 ---------------------------------------------------------------

        private static TutorialStepDefinition MaintenanceResupply() => new TutorialStepDefinition(
            Chapter, "F-08", "Bakım İkmali",
            "Collector Mk II ile Komuta Merkezi'nin Bakım Rezervi'ne malzeme taşı.",
            "Hızlı ikmal görevini başlat ve rezervin dolduğunu gör.",
            () => TutorialSteps.Poll(
                complete: c => TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Collector,
                    v => v.ResupplyTask != null) != null,
                prepare: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    LogisticsCargo cargo = TutorialFhij.Cargo(c);
                    TutorialFhij.EnsureLevel(VehicleCatalog.CollectorMk2.UnlockLevel);
                    TutorialFhij.EnsureHangarTier(hangar, VehicleCatalog.CollectorMk2.RequiredHangarTier);
                    TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Collector, 2);
                    TutorialFhij.EnsureItem(cargo, ProcessingCatalog.BasicPartId, 20f);
                },
                reject: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    VehicleAgent collector = TutorialFhij.FirstVehicle(hangar, VehicleClass.Collector, v => v.Def.Mk >= 2);
                    if (collector == null)
                        return TutorialFhij.RejectText(ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible);
                    if (collector.ResupplyTask != null) return null;
                    if (TutorialFhij.AnyStructure(SurfaceStructureType.Command) == null)
                        return TutorialFhij.RejectText(ShipHangar.MaintenanceResupplyRejectReason.TargetNotEligible);
                    LogisticsCargo cargo = TutorialFhij.Cargo(c);
                    if (cargo == null || cargo.GetAvailable(ProcessingCatalog.BasicPartId) <= 0f)
                        return TutorialFhij.RejectText(ShipHangar.MaintenanceResupplyRejectReason.InsufficientCargo);
                    return null;
                }),
            checklistTr: new[] { "Mk II Collector'ü seç", "HIZLI İKMAL'e bas", "Teslimatı bekle" });

        // ---- 82 ---------------------------------------------------------------

        private static TutorialStepDefinition EngineerBuild() => new TutorialStepDefinition(
            Chapter, "F-09", "Engineer Üret",
            "Hangarda bir Engineer üret.",
            "Engineer üretimini başlat ve listede belirmesini bekle.",
            () => TutorialSteps.Poll(
                complete: c => ActiveBuildIs(c, VehicleClass.Engineer)
                               || TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Engineer) != null,
                prepare: c => PrepareBuild(c, VehicleCatalog.EngineerMk1),
                reject: c => BuildComplaint(c, VehicleClass.Engineer, 1)),
            checklistTr: new[] { "Malzemeyi seç", "Üretimi başlat", "Engineer'ın listede belirmesini bekle" });

        // ---- 83 ---------------------------------------------------------------

        private static TutorialStepDefinition EngineerTarget() => new TutorialStepDefinition(
            Chapter, "F-10", "Onarım Hedefi Seç",
            "Engineer için aynı sistemdeki gerçek bir onarım hedefi seç.",
            "Gerçek bir bakım görevini başlat.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    VehicleAgent engineer = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Engineer,
                        v => v.EngineerTask != null);
                    return engineer != null && !string.IsNullOrEmpty(engineer.EngineerTask.TargetId);
                },
                prepare: c => PrepareEngineer(c),
                reject: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    VehicleAgent engineer = TutorialFhij.FirstVehicle(hangar, VehicleClass.Engineer);
                    if (engineer == null)
                        return TutorialFhij.RejectText(ShipHangar.EngineerSendRejectReason.VehicleNotEligible);
                    if (engineer.EngineerTask != null) return null;
                    if (hangar != null && hangar.Command.Capacity - hangar.Command.Used < engineer.Def.CommandCost)
                        return TutorialFhij.RejectText(ShipHangar.EngineerSendRejectReason.NoCommandCapacity);
                    if (hangar != null && hangar.BuildEngineerTargetCandidates(engineer).Count == 0)
                        return TutorialFhij.RejectText(ShipHangar.EngineerSendRejectReason.NothingToRepair);
                    return null;
                }),
            checklistTr: new[] { "Hedef listesini oku", "Hasarlı hedefi seç", "Gerekli parça ve malzemeyi gör" });

        // ---- 84 ---------------------------------------------------------------

        private static TutorialStepDefinition SafetyGate() => new TutorialStepDefinition(
            Chapter, "F-11", "Sekiz Saniyelik Güven Süresi",
            "Engineer hedefe varınca sekiz saniye kesintisiz güvenli beklesin.",
            "Güven süresinin dolmasını ve işe geçilmesini izle.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    EngineerMissionState task = EngineerTask(c);
                    return task != null && (task.Phase == EngineerPhase.Working
                                            || task.Phase == EngineerPhase.ReturnUnusedCargo
                                            || task.Phase == EngineerPhase.TravelHome);
                },
                prepare: c => PrepareEngineerAtTarget(c),
                objectives: (c, report) =>
                {
                    EngineerMissionState task = EngineerTask(c);
                    report.SetObjective(0, task != null && task.Phase == EngineerPhase.WaitForSafety);
                    report.SetObjective(1, task != null && task.SafeSecondsAccumulated > 0f);
                },
                reject: c =>
                {
                    EngineerMissionState task = EngineerTask(c);
                    if (task == null) return "AKTİF BAKIM GÖREVİ YOK";
                    if (task.Phase != EngineerPhase.WaitForSafety) return null;
                    CombatEncounterDirector director = TutorialFhij.Director(c);
                    bool unsafeNow = director != null && director.Encounter != null && director.Encounter.IsOpen;
                    return unsafeNow ? "TEHDİT ALGILANDI — GÜVEN SÜRESİ SIFIRLANDI" : null;
                }),
            checklistTr: new[] { "WaitForSafety fazına geçişi gör", "Sayacın dolmasını bekle", "Çatışmada sıfırlandığını gör" });

        // ---- 85 ---------------------------------------------------------------

        private static TutorialStepDefinition StructureRepair() => new StructureRepairStep().Definition;

        /// <summary>
        /// Completes on real restored durability, measured from the value the
        /// target actually had when the step opened -- a structure that was
        /// already healthy must not count as a repair the player performed.
        /// </summary>
        private sealed class StructureRepairStep : ITutorialStepRuntime
        {
            private TutorialStepContext context;
            private string targetId;
            private float baselineDurability;

            internal TutorialStepDefinition Definition => new TutorialStepDefinition(
                Chapter, "F-12", "Yapıyı Onar",
                "Engineer hasarlı yapıyı alan kapasitesine kadar onarsın.",
                "Zırh ve gövdenin gerçekten arttığını gör.",
                () => new StructureRepairStep(),
                checklistTr: new[] { "Working fazını gör", "Zırh ve gövdenin arttığını izle", "Harcanan parçayı doğrula" });

            public void Prepare(TutorialStepContext ctx)
            {
                context = ctx;
                PrepareEngineerAtTarget(ctx);
                EngineerMissionState task = EngineerTask(ctx);
                if (task != null) targetId = task.TargetId;
                baselineDurability = TutorialFhij.DurabilityTotal(TargetDurability());
            }

            private Durability TargetDurability()
            {
                if (string.IsNullOrEmpty(targetId)) return null;
                IReadOnlyList<SurfaceStructure> all = ExtractionState.Structures;
                for (int i = 0; i < all.Count; i++)
                {
                    SurfaceStructure s = all[i];
                    if (s != null && s.TargetId == targetId) return s.Runtime?.Durability;
                }
                return null;
            }

            public void Evaluate(TutorialStepReport report)
            {
                if (context == null) return;
                EngineerMissionState task = EngineerTask(context);
                Durability durability = TargetDurability();
                float now = TutorialFhij.DurabilityTotal(durability);

                report.SetObjective(0, task != null && task.Phase == EngineerPhase.Working);
                report.SetObjective(1, now > baselineDurability + 0.001f);
                report.SetObjective(2, task != null && task.ConsumedParts > 0);

                if (durability != null && now > baselineDurability + 0.001f) { report.Complete(); return; }
                if (task == null) { report.Reject("AKTİF BAKIM GÖREVİ YOK"); return; }
                if (task.Phase == EngineerPhase.WaitForSafety) report.Reject("ÇATIŞMA SÜRÜYOR — ONARIM DURDURULDU");
            }

            public void Cleanup() => context = null;
        }

        // ---- 86 ---------------------------------------------------------------

        private static TutorialStepDefinition VehicleRecovery() => new TutorialStepDefinition(
            Chapter, "F-13", "Devre Dışı Aracı Kurtar",
            "Devre dışı kalmış aracı Engineer ile kurtar.",
            "Kurtarma görevini yürüt ve aracın yeniden hareket ettiğini gör.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    // Success is the recovered vehicle, never the mission being started:
                    // a vehicle that was Stranded is no longer Stranded and has hull back.
                    VehicleAgent collector = TutorialFhij.FirstVehicle(hangar, VehicleClass.Collector);
                    return collector != null && !collector.Stranded && collector.Hull > 0f
                           && !(collector.DurabilityRef != null && collector.DurabilityRef.Disabled);
                },
                prepare: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    LogisticsCargo cargo = TutorialFhij.Cargo(c);
                    // Tier 2, because this step is the one that needs two vehicles
                    // committed at once: the stricken Collector holds a command slot
                    // while it is stranded, and the Engineer's rescue mission reserves
                    // two more. Tier 1's capacity of 2 cannot carry both.
                    TutorialFhij.EnsureHangarTier(hangar, 2);
                    VehicleAgent collector = TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Collector);
                    TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Engineer);
                    // Both builds consumed their own recipes; the rescue reserves again
                    // on top of that, so the recovery cost has to be stocked separately
                    // or TrySendEngineer answers InsufficientResources.
                    TutorialFhij.EnsureItem(cargo, ProcessingCatalog.BasicPartId, 60f);
                    TutorialFhij.EnsureItem(cargo, VehicleCatalog.MetalAlloyId, 60f);
                    if (collector == null || collector.DurabilityRef == null) return;

                    // The Collector has to be OUT before it can be lost. A vehicle
                    // parked in the hangar is deactivated by VehicleAgent.Create, and
                    // VehicleAgent.IsRecoverable requires gameObject.activeSelf -- so a
                    // hangar-parked wreck is not a rescuable one, and TrySendEngineer
                    // would answer NothingToRepair. Launching also fixes HomeSystemId,
                    // which is what puts it in BuildEngineerTargetCandidates at all.
                    if (collector.Mission.Phase == VehiclePhase.InHangar)
                    {
                        string target = FirstPlanetInShipSystem(hangar);
                        if (target != null) hangar.TryLaunch(collector, MissionKind.Collector, target);
                    }

                    // Real damage through the real ICombatTarget path -- never a
                    // hand-set flag: Stranded only becomes true because the vehicle
                    // actually lost its hull.
                    if (!collector.DurabilityRef.Disabled)
                        collector.ApplyDamage((collector.DurabilityRef.ShieldMax + collector.DurabilityRef.ArmorMax
                                               + collector.DurabilityRef.HullMax) * 4f, DamageProfile.Neutral);
                },
                reject: c =>
                {
                    VehicleAgent engineer = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Engineer);
                    if (engineer == null)
                        return TutorialFhij.RejectText(ShipHangar.EngineerSendRejectReason.VehicleNotEligible);
                    VehicleAgent collector = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Collector);
                    if (collector != null && !collector.IsRecoverable && collector.Stranded)
                        return TutorialFhij.RejectText(ShipHangar.EngineerSendRejectReason.NothingToRepair);
                    return null;
                }),
            checklistTr: new[] { "Devre dışı aracı listede gör", "Kurtarma görevi başlat", "Aracın yeniden hareket ettiğini doğrula" });

        // ---- 87 ---------------------------------------------------------------

        private static TutorialStepDefinition DrillIsNotRepairable() => new DrillRefusalStep().Definition;

        /// <summary>
        /// The refusal itself is the lesson, so the step watches for the player
        /// having actually asked: it completes when a Drill was offered as an
        /// Engineer target and the game answered <c>TargetHasNoDurability</c>
        /// while leaving the Engineer without a task. The check is performed on
        /// the real seam, which for this one reason is side-effect free -- the
        /// Drill branch returns before any reservation is taken.
        /// </summary>
        private sealed class DrillRefusalStep : ITutorialStepRuntime
        {
            private TutorialStepContext context;
            private bool refusalSeen;

            internal TutorialStepDefinition Definition => new TutorialStepDefinition(
                Chapter, "F-14", "Sondaj Neden Onarılmaz",
                "Sondajı Engineer hedefi olarak seçmeyi dene ve reddi oku.",
                "Sondajın onarım hedefi olmadığını gerçek reddi görerek öğren.",
                () => new DrillRefusalStep(),
                checklistTr: new[] { "Sondajı hedef olarak dene", "Reddi oku", "Sondajın listede olmadığını doğrula" });

            public void Prepare(TutorialStepContext ctx)
            {
                context = ctx;
                refusalSeen = false;
                // Everything an Engineer build needs, and no Engineer: bringing one
                // is the player's move, and the refusal can only be shown once a
                // real Engineer is standing in front of a real Drill.
                ShipHangar hangar = TutorialFhij.Hangar(ctx);
                LogisticsCargo cargo = TutorialFhij.Cargo(ctx);
                if (hangar == null) return;
                if (hangar.Production == null) hangar.InitializeNewCampaign();
                TutorialFhij.EnsureLevel(VehicleCatalog.EngineerMk1.UnlockLevel);
                TutorialFhij.EnsureHangarTier(hangar, Mathf.Max(1, VehicleCatalog.EngineerMk1.RequiredHangarTier));
                TutorialFhij.EnsureItem(cargo, ProcessingCatalog.BasicPartId, VehicleCatalog.EngineerMk1.BasicPartCost);
                TutorialFhij.EnsureItem(cargo, VehicleCatalog.MetalAlloyId, VehicleCatalog.EngineerMk1.SpecialMaterialCost);
                // "Everything an Engineer build needs" now includes VS-015 §1's
                // Controlled planet, which P1-ECO-013 made a real gate.
                if (VehicleCatalog.EngineerMk1.RequiresControlledPlanet)
                    TutorialFhij.EnsureControlledPlanet(ctx, TutorialFhij.PrerequisiteWorldId(ctx));
            }

            public void Evaluate(TutorialStepReport report)
            {
                if (context == null) return;
                ShipHangar hangar = TutorialFhij.Hangar(context);
                VehicleAgent engineer = TutorialFhij.FirstVehicle(hangar, VehicleClass.Engineer);
                DrillStructure drill = TutorialFhij.AnyDrill();

                bool drillOffered = false;
                if (hangar != null && engineer != null)
                {
                    List<ShipHangar.EngineerTargetCandidate> candidates = hangar.BuildEngineerTargetCandidates(engineer);
                    for (int i = 0; i < candidates.Count; i++)
                        if (candidates[i].Kind == EngineerTargetKind.Drill) drillOffered = true;
                }
                report.SetObjective(2, !drillOffered);

                if (!refusalSeen && hangar != null && engineer != null && drill != null && engineer.EngineerTask == null)
                {
                    ShipHangar.EngineerSendRejectReason reason = hangar.TrySendEngineer(
                        engineer, EngineerTargetKind.Drill, drill.WorldId, EngineerSourceKind.ShipCargo, null);
                    if (reason == ShipHangar.EngineerSendRejectReason.TargetHasNoDurability && engineer.EngineerTask == null)
                    {
                        refusalSeen = true;
                        report.SetObjective(0, true);
                        report.SetObjective(1, true);
                    }
                }

                if (refusalSeen && !drillOffered) { report.Complete(); return; }
                if (drill == null) report.Reject("SİSTEMDE SONDAJ YOK");
                else if (engineer == null) report.Reject(TutorialFhij.RejectText(ShipHangar.EngineerSendRejectReason.VehicleNotEligible));
                else report.Note(TutorialFhij.RejectText(ShipHangar.EngineerSendRejectReason.TargetHasNoDurability));
            }

            public void Cleanup() => context = null;
        }

        // ---- 88 ---------------------------------------------------------------

        private static TutorialStepDefinition EscortBuild() => new TutorialStepDefinition(
            Chapter, "F-15", "Escort Üret",
            "Hangarda bir Escort üret.",
            "Escort üretimini başlat ve listede belirmesini bekle.",
            () => TutorialSteps.Poll(
                complete: c => ActiveBuildIs(c, VehicleClass.Escort)
                               || TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Escort) != null,
                prepare: c => PrepareBuild(c, VehicleCatalog.EscortMk1),
                reject: c => BuildComplaint(c, VehicleClass.Escort, 1)),
            checklistTr: new[] { "Malzemeyi seç", "Üretimi başlat", "Escort'un listede belirmesini bekle" });

        // ---- 89 ---------------------------------------------------------------

        private static TutorialStepDefinition EscortProtect() => new TutorialStepDefinition(
            Chapter, "F-16", "Koruma Hedefi Ata",
            "Escort'a koruyacağı hedefi ata.",
            "Korumanın atandığını ve takibe geçtiğini doğrula.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    VehicleAgent escort = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Escort,
                        v => v.EscortState != null);
                    return escort != null && !string.IsNullOrEmpty(escort.EscortState.ProtectedTargetId);
                },
                prepare: c => PrepareEscort(c),
                reject: c =>
                {
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    VehicleAgent escort = TutorialFhij.FirstVehicle(hangar, VehicleClass.Escort);
                    if (escort == null)
                        return TutorialFhij.RejectText(ShipHangar.EscortAssignRejectReason.VehicleNotEligible);
                    if (escort.EscortState != null) return null;
                    return TutorialFhij.RejectText(TutorialFhij.DiagnoseEscortAssign(hangar, escort));
                }),
            checklistTr: new[] { "Escort'u seç", "GEMİYİ KORU'ya bas", "Korumanın atandığını doğrula" });

        // ---- 90 ---------------------------------------------------------------

        private static TutorialStepDefinition EscortStanceStepDef() => new EscortStanceStep().Definition;

        /// <summary>
        /// Success is the stance actually having <i>changed</i> from the one the
        /// escort carried when the step began, and the real retreat threshold
        /// having moved with it -- the constant table is read through
        /// <c>EscortCombatResolver.ShouldRetreat</c>, never restated here.
        /// </summary>
        private sealed class EscortStanceStep : ITutorialStepRuntime
        {
            private TutorialStepContext context;
            private EscortStance baseline;
            private bool hadEscort;

            internal TutorialStepDefinition Definition => new TutorialStepDefinition(
                Chapter, "F-17", "Escort Duruşu",
                "Escort'un geri çekilme eşiğini belirleyen duruşu seç.",
                "Duruşu değiştir ve eşiğin gerçekten değiştiğini gör.",
                () => new EscortStanceStep(),
                checklistTr: new[] { "Duruşu seç", "Eşiğin değiştiğini doğrula" });

            public void Prepare(TutorialStepContext ctx)
            {
                context = ctx;
                // The protect assignment is F-16's lesson and this step's
                // prerequisite: without an EscortState there is no stance to change.
                PrepareAssignedEscort(ctx);
                EscortState state = Escort();
                hadEscort = state != null;
                baseline = state != null ? state.Stance : EscortStance.Balanced;
            }

            private EscortState Escort()
            {
                VehicleAgent escort = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(context), VehicleClass.Escort,
                    v => v.EscortState != null);
                return escort != null ? escort.EscortState : null;
            }

            public void Evaluate(TutorialStepReport report)
            {
                if (context == null) return;
                EscortState state = Escort();
                if (state == null)
                {
                    report.Reject("ÖNCE BİR KORUMA HEDEFİ ATAYIN");
                    return;
                }
                if (!hadEscort) { baseline = state.Stance; hadEscort = true; }

                bool changed = state.Stance != baseline;
                // The real behavioural difference, not the enum: at the baseline's
                // own threshold the two stances must disagree about retreating.
                bool thresholdMoved = EscortCombatResolver.ShouldRetreat(state.RetreatThresholdFraction, state.Stance, false)
                                      != EscortCombatResolver.ShouldRetreat(state.RetreatThresholdFraction, baseline, false)
                                      || Mathf.Abs(state.RetreatThresholdFraction - ThresholdOf(baseline)) > 0.0001f;
                report.SetObjective(0, changed);
                report.SetObjective(1, thresholdMoved);
                if (changed && thresholdMoved) report.Complete();
            }

            private static float ThresholdOf(EscortStance stance) => stance switch
            {
                EscortStance.Safe => EscortState.SafeRetreatFraction,
                EscortStance.Persistent => EscortState.PersistentRetreatFraction,
                _ => EscortState.BalancedRetreatFraction
            };

            public void Cleanup() => context = null;
        }

        // ---- 91 ---------------------------------------------------------------

        private static TutorialStepDefinition EscortCommands() => new TutorialStepDefinition(
            Chapter, "F-18", "Takip, Angaje, Geri Çekil",
            "Escort'a takip, koruma ve geri çekilme komutlarını ver.",
            "Escort'un üç komuta da gerçekten uyduğunu gör.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    EscortState state = EscortStateOf(c);
                    return state != null && state.Command == EscortCommand.Retreat
                           && state.SubState == EscortSubState.Retreating;
                },
                prepare: c =>
                {
                    PrepareAssignedEscort(c);
                    EnemyAgent threat = TutorialFhij.SpawnEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 400f));
                    TutorialFhij.ReleaseOnCleanup(c, threat);
                },
                objectives: (c, report) =>
                {
                    EscortState state = EscortStateOf(c);
                    report.SetObjective(0, state != null && state.SubState != EscortSubState.Idle);
                    report.SetObjective(1, state != null && state.EngagedThreatId != null);
                    report.SetObjective(2, state != null && state.SubState == EscortSubState.Retreating);
                },
                reject: c => EscortStateOf(c) == null
                    ? TutorialFhij.RejectText(ShipHangar.EscortAssignRejectReason.VehicleNotEligible)
                    : null),
            checklistTr: new[] { "TAKİP ET'e bas", "Angaje olduğunu gör", "GERİ ÇEKİL'e bas" });

        // ---- 92 ---------------------------------------------------------------

        private static TutorialStepDefinition InterventionGroupStep() => new TutorialStepDefinition(
            Chapter, "F-19", "Müdahale Grubu",
            "Sistem müdahale grubuna araç ekle ve politikasını belirle.",
            "Gruba üye ekle ve politikayı Sistem Müdahalesi yap.",
            () => TutorialSteps.Poll(
                complete: c =>
                {
                    InterventionGroupDirector director = TutorialFhij.Intervention(c);
                    Combat.InterventionGroup group = director != null ? director.Group : null;
                    return group != null && group.MemberVehicleIds.Count > 0
                           && group.Policy != InterventionPolicy.Manual;
                },
                prepare: c =>
                {
                    PrepareEscort(c);
                    ShipHangar hangar = TutorialFhij.Hangar(c);
                    InterventionGroupDirector director = TutorialFhij.Intervention(c);
                    if (director == null || hangar == null) return;
                    director.Initialize(hangar);
                    string systemId = hangar.CurrentSystemId;
                    if (systemId != null) director.EnsureGroupForSystem(systemId);
                },
                objectives: (c, report) =>
                {
                    InterventionGroupDirector director = TutorialFhij.Intervention(c);
                    Combat.InterventionGroup group = director != null ? director.Group : null;
                    report.SetObjective(0, group != null && group.MemberVehicleIds.Count > 0);
                    report.SetObjective(1, group != null && group.Policy == InterventionPolicy.SystemIntervention);
                    report.SetObjective(2, group != null && !string.IsNullOrEmpty(group.ActiveTargetPlanetId));
                },
                reject: c =>
                {
                    InterventionGroupDirector director = TutorialFhij.Intervention(c);
                    Combat.InterventionGroup group = director != null ? director.Group : null;
                    if (group == null) return "BU SİSTEMDE MÜDAHALE GRUBU YOK";
                    if (TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Escort) == null)
                        return "ESCORT YOK — GRUP MESAFEDE TUTUYOR";
                    return null;
                }),
            checklistTr: new[] { "Gruba bir Escort ekle", "Politikayı Sistem Müdahalesi yap", "Grubun hedef seçtiğini gör" });

        // ---- 93 ---------------------------------------------------------------

        private static TutorialStepDefinition HangarReturn() => new HangarReturnStep().Definition;

        /// <summary>
        /// Watches the real <see cref="VehicleAgent.DockedInHangar"/> event rather
        /// than polling a phase, because the freed command slot is released from
        /// that event's own handler -- the two facts the step teaches are only
        /// simultaneously true for the frame the event fires.
        /// </summary>
        private sealed class HangarReturnStep : ITutorialStepRuntime
        {
            private TutorialStepContext context;
            private VehicleAgent watched;
            private bool docked;
            private int usedAtStart;

            internal TutorialStepDefinition Definition => new TutorialStepDefinition(
                Chapter, "F-20", "Hangara Dönüş",
                "Aracı geri çağır ve hangara kenetlenmesini izle.",
                "Dönüşü izle ve komuta kapasitesinin serbest kaldığını gör.",
                () => new HangarReturnStep(),
                checklistTr: new[] { "GERİ ÇAĞIR'a bas", "Dönüş yolculuğunu izle", "Komuta kapasitesinin serbest kaldığını doğrula" });

            public void Prepare(TutorialStepContext ctx)
            {
                context = ctx;
                docked = false;
                ShipHangar hangar = TutorialFhij.Hangar(ctx);
                if (hangar == null) return;

                TutorialFhij.EnsureHangarTier(hangar, 1);
                VehicleAgent scout = TutorialFhij.BuildVehicleForPrerequisite(ctx, VehicleClass.Scout);
                if (scout != null && scout.Mission.Phase == VehiclePhase.InHangar)
                {
                    string target = FirstPlanetInShipSystem(hangar);
                    if (target != null) hangar.TryLaunch(scout, MissionKind.Scout, target);
                }
                usedAtStart = hangar.Command.Used;
                watched = scout;
                if (watched == null) return;

                Action<VehicleAgent> handler = _ => docked = true;
                watched.DockedInHangar += handler;
                VehicleAgent captured = watched;
                ctx.Scope.OnCleanup(() => { if (captured != null) captured.DockedInHangar -= handler; });
            }

            public void Evaluate(TutorialStepReport report)
            {
                if (context == null) return;
                ShipHangar hangar = TutorialFhij.Hangar(context);
                bool home = watched != null && watched.Mission.Phase == VehiclePhase.InHangar;
                bool released = hangar != null && hangar.Command.Used < usedAtStart;

                report.SetObjective(0, watched != null && watched.Mission.RecallRequested);
                report.SetObjective(1, watched != null && watched.Mission.Phase == VehiclePhase.TravelReturn);
                report.SetObjective(2, released);

                if (docked || (home && released)) { report.Complete(); return; }
                if (watched == null) { report.Reject("GÖREVDE ARAÇ YOK"); return; }
                if (watched.Mission.Phase == VehiclePhase.InHangar && !watched.Mission.RecallRequested)
                    report.Reject("ARAÇ ZATEN HANGARDA");
            }

            public void Cleanup() { context = null; watched = null; }
        }

        // ---- shared step bodies ----------------------------------------------

        private static bool ActiveBuildIs(TutorialStepContext context, VehicleClass vehicleClass)
        {
            ShipHangar hangar = TutorialFhij.Hangar(context);
            HangarProductionJob job = hangar != null && hangar.Production != null ? hangar.Production.ActiveJob : null;
            return job != null && !job.IsUpgrade && job.VehicleClass == vehicleClass;
        }

        /// <summary>Stocks the real recipe and clears the real gates -- and stops short of starting the job, which is the step's own success.</summary>
        private static void PrepareBuild(TutorialStepContext context, VehicleDef def)
        {
            ShipHangar hangar = TutorialFhij.Hangar(context);
            LogisticsCargo cargo = TutorialFhij.Cargo(context);
            if (hangar == null) return;
            if (hangar.Production == null) hangar.InitializeNewCampaign();
            TutorialFhij.EnsureLevel(def.UnlockLevel);
            TutorialFhij.EnsureHangarTier(hangar, Mathf.Max(1, def.RequiredHangarTier));
            TutorialFhij.EnsureItem(cargo, ProcessingCatalog.BasicPartId, def.BasicPartCost);
            TutorialFhij.EnsureItem(cargo, VehicleCatalog.MetalAlloyId, def.SpecialMaterialCost);
            // P1-ECO-013 made VS-015 §1's Controlled-planet requirement real. A step
            // that asks the player to build an Engineer has to set the world up so the
            // build can succeed, exactly as it already tops up level, hangar tier and
            // materials — the gate itself is never softened for the tutorial.
            if (def.RequiresControlledPlanet)
                TutorialFhij.EnsureControlledPlanet(context, TutorialFhij.PrerequisiteWorldId(context));
        }

        private static string BuildComplaint(TutorialStepContext context, VehicleClass vehicleClass, int mk)
        {
            ShipHangar hangar = TutorialFhij.Hangar(context);
            LogisticsCargo cargo = TutorialFhij.Cargo(context);
            VehicleDef def = VehicleCatalog.Get(vehicleClass, mk);
            string material = def != null ? TutorialFhij.AffordableMaterial(cargo, def.SpecialMaterialCost) : null;
            HangarProductionRejectReason reason = TutorialFhij.DiagnoseProduction(hangar, cargo, vehicleClass, mk, material);
            return TutorialFhij.RejectText(reason);
        }

        private static EngineerMissionState EngineerTask(TutorialStepContext context)
        {
            VehicleAgent engineer = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(context), VehicleClass.Engineer,
                v => v.EngineerTask != null);
            return engineer != null ? engineer.EngineerTask : null;
        }

        private static EscortState EscortStateOf(TutorialStepContext context)
        {
            VehicleAgent escort = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(context), VehicleClass.Escort,
                v => v.EscortState != null);
            return escort != null ? escort.EscortState : null;
        }

        /// <summary>Installs an idle Engineer and the resources one real repair would reserve. Never sends it.</summary>
        private static void PrepareEngineer(TutorialStepContext context)
        {
            ShipHangar hangar = TutorialFhij.Hangar(context);
            LogisticsCargo cargo = TutorialFhij.Cargo(context);
            TutorialFhij.EnsureHangarTier(hangar, 1);
            TutorialFhij.BuildVehicleForPrerequisite(context, VehicleClass.Engineer);
            TutorialFhij.EnsureItem(cargo, ProcessingCatalog.BasicPartId, 60f);
            TutorialFhij.EnsureItem(cargo, VehicleCatalog.MetalAlloyId, 60f);
        }

        /// <summary>
        /// An Engineer that is already on a real mission, standing at its target.
        /// The prerequisite for every step whose lesson happens <i>after</i> the
        /// target was chosen -- the send itself is F-10's lesson, and the phase
        /// walk stops at <see cref="EngineerPhase.WaitForSafety"/>, never through it.
        /// </summary>
        private static void PrepareEngineerAtTarget(TutorialStepContext context)
        {
            PrepareEngineer(context);
            ShipHangar hangar = TutorialFhij.Hangar(context);
            VehicleAgent engineer = TutorialFhij.FirstVehicle(hangar, VehicleClass.Engineer);
            if (hangar == null || engineer == null) return;

            if (engineer.EngineerTask == null)
            {
                List<ShipHangar.EngineerTargetCandidate> candidates = hangar.BuildEngineerTargetCandidates(engineer);
                for (int i = 0; i < candidates.Count && engineer.EngineerTask == null; i++)
                {
                    if (candidates[i].Kind != EngineerTargetKind.Structure) continue;
                    hangar.TrySendEngineer(engineer, candidates[i].Kind, candidates[i].TargetId,
                        EngineerSourceKind.ShipCargo, VehicleCatalog.MetalAlloyId);
                }
            }

            EngineerMissionState task = engineer.EngineerTask;
            if (task == null) return;
            task.BeginLoad(); task.BeginLaunch(); task.BeginTravel();
            if (task.Phase == EngineerPhase.Travel) task.ArriveAtTarget();
        }

        /// <summary>Installs an idle Escort with command capacity for it. Never assigns a protect target.</summary>
        private static void PrepareEscort(TutorialStepContext context)
        {
            ShipHangar hangar = TutorialFhij.Hangar(context);
            LogisticsCargo cargo = TutorialFhij.Cargo(context);
            TutorialFhij.EnsureLevel(VehicleCatalog.EscortMk1.UnlockLevel);
            TutorialFhij.EnsureHangarTier(hangar, VehicleCatalog.EscortMk1.RequiredHangarTier);
            TutorialFhij.BuildVehicleForPrerequisite(context, VehicleClass.Escort);
        }

        /// <summary>An Escort that is already protecting something -- the prerequisite for every step whose lesson is what an assigned Escort then does.</summary>
        private static void PrepareAssignedEscort(TutorialStepContext context)
        {
            PrepareEscort(context);
            ShipHangar hangar = TutorialFhij.Hangar(context);
            VehicleAgent escort = TutorialFhij.FirstVehicle(hangar, VehicleClass.Escort);
            if (hangar != null && escort != null && escort.EscortState == null)
                hangar.TryAssignEscort(escort, "ship", TargetCategory.Ship);
        }

        /// <summary>A real planet world id in the ship's own system, or null. Used only to name a legal launch target.</summary>
        internal static string FirstPlanetInShipSystem(ShipHangar hangar)
        {
            string systemId = hangar != null ? hangar.CurrentSystemId : null;
            if (systemId == null) return null;
            Sectors.SystemManifest manifest = World.StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null || manifest.Planets.Count == 0) return null;
            return manifest.Planets[0].PlanetId;
        }
    }
}
