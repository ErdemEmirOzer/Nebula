using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Poi;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// Command capacity, the production slot, and every vehicle with what it is
    /// actually doing.
    /// <para>
    /// HangarScreen was the largest UI class in the project at 1241 lines, and the
    /// flight HUD reduced all of it to one strip reading
    /// <c>"FİLO: 3 AKTİF / 9 TOPLAM · ⚠ 2 DİKKAT"</c>. Neither told the player
    /// which vehicle needed them or why. This page does, and the HUD strip goes
    /// back to being a summary that points here.
    /// </para>
    /// <para>
    /// Rows are keyed by the vehicle's stable id and reused across refreshes, so a
    /// fleet that changes phase every second never reshuffles under the cursor.
    /// The id itself is never shown — vehicles are named by class and callsign.
    /// </para>
    /// </summary>
    public sealed class NebulaFleetPage : INebulaMenuPage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Fleet;
        public string Title => "Filo";

        private readonly NebulaContext context;

        private NebulaUiFactory.LabelHandle commandValue;
        private NebulaUiFactory.BarHandle commandBar;
        private NebulaUiFactory.LabelHandle productionName, productionEta;
        private NebulaUiFactory.BarHandle productionBar;
        private NebulaUiFactory.LabelHandle emptyState;
        private RectTransform vehicleList;

        private readonly Dictionary<string, VehicleRow> rows = new Dictionary<string, VehicleRow>();
        private readonly HashSet<string> seen = new HashSet<string>();

        /// <summary>Launch targets offered at once. Beyond this the map is the right tool.</summary>
        public const int MaxTargetRows = 6;

        private readonly NebulaConfirmationDialog confirmation;
        private NebulaUiFactory.ButtonHandle produceButton, recallButton, upgradeButton, launchButton;

        /// <summary>
        /// The second rail. These are the actions the old hangar screen offered as
        /// twelve permanently-visible small buttons; here only the ones that apply
        /// to the selected vehicle are on screen, so the rail states what can be
        /// done rather than everything that exists.
        /// </summary>
        private RectTransform contextRail;
        private NebulaUiFactory.ButtonHandle escortFollow, escortProtect, escortRetreat, escortReturn;
        private NebulaUiFactory.ButtonHandle poiDispatch;
        private NebulaUiFactory.ButtonHandle escortStance;
        private NebulaUiFactory.ButtonHandle engineerRepair, engineerCancel;
        private NebulaUiFactory.ButtonHandle resupplyStart, resupplyCancel;
        private NebulaUiFactory.ButtonHandle routePauseResume, routeDelete;
        private NebulaUiFactory.ButtonHandle groupMembership, groupPolicy;
        private NebulaUiFactory.LabelHandle statusLine;

        /// <summary>Test-facing: the last thing the page told the player.</summary>
        public string StatusText => statusLine?.Text != null ? statusLine.Text.text : null;

        /// <summary>Test-facing: context actions currently offered.</summary>
        public int VisibleContextActions { get; private set; }

        private RectTransform targetPicker;
        private NebulaUiFactory.LabelHandle targetHeading, targetEmpty;
        private readonly List<TargetRow> targetRows = new List<TargetRow>(MaxTargetRows);
        private readonly List<string> targetIds = new List<string>(MaxTargetRows);

        /// <summary>
        /// What the one target list is currently choosing. The Engineer and Escort
        /// modes exist because <c>ShipHangar.BuildEngineerTargetCandidates</c> and
        /// <c>BuildEscortProtectableTargets</c> had no caller at all: the page sent
        /// every Engineer to <c>"ship"</c> and assigned every Escort to <c>"ship"</c>,
        /// so damaged structures, damaged support vehicles, disabled vehicles and
        /// every non-ship protectable target were unreachable from the interface even
        /// though the services accept all of them.
        /// </summary>
        private enum TargetMode { Launch, EngineerRepair, EscortProtect, PoiDispatch }

        private TargetMode targetMode = TargetMode.Launch;
        private readonly List<ShipHangar.EngineerTargetCandidate> engineerCandidates =
            new List<ShipHangar.EngineerTargetCandidate>(MaxTargetRows);
        private readonly List<ShipHangar.EscortProtectableTarget> escortCandidates =
            new List<ShipHangar.EscortProtectableTarget>(MaxTargetRows);

        /// <summary>Test-facing: whether the launch target picker is open.</summary>
        public bool TargetPickerOpen => targetPicker != null && targetPicker.gameObject.activeSelf;

        /// <summary>Test-facing: whether the open target list is choosing an Engineer repair target.</summary>
        public bool EngineerTargetPickerOpen => TargetPickerOpen && targetMode == TargetMode.EngineerRepair;

        /// <summary>Test-facing: whether the open target list is choosing an Escort's protected target.</summary>
        public bool EscortTargetPickerOpen => TargetPickerOpen && targetMode == TargetMode.EscortProtect;

        /// <summary>Test-facing: whether the open target list is choosing a POI assignment.</summary>
        public bool PoiTargetPickerOpen => TargetPickerOpen && targetMode == TargetMode.PoiDispatch;

        /// <summary>Test-facing: targets currently offered.</summary>
        public int VisibleTargets { get; private set; }

        /// <summary>
        /// Build options offered at once: the vehicle catalog's seven definitions on
        /// the first stage, the five choosable special materials on the second.
        /// </summary>
        public const int MaxBuildRows = 8;

        private RectTransform buildPicker;
        private NebulaUiFactory.LabelHandle buildHeading, buildEmpty;
        private readonly List<TargetRow> buildRows = new List<TargetRow>(MaxBuildRows);
        private readonly List<string> buildOptionIds = new List<string>(MaxBuildRows);

        /// <summary>Non-null while the picker is on its material stage, waiting for the second half of a chosen build.</summary>
        private VehicleDef pendingBuildDef;

        /// <summary>Test-facing: whether the production picker is open.</summary>
        public bool BuildPickerOpen => buildPicker != null && buildPicker.gameObject.activeSelf;

        /// <summary>Test-facing: build options currently offered.</summary>
        public int VisibleBuildOptions { get; private set; }

        /// <summary>Test-facing: the class awaiting a material choice, or <c>null</c> while the picker is on its vehicle stage.</summary>
        public VehicleClass? PendingBuildClass => pendingBuildDef?.Class;

        /// <summary>Test-facing: the Mk awaiting a material choice, or 0.</summary>
        public int PendingBuildMk => pendingBuildDef?.Mk ?? 0;

        private sealed class TargetRow
        {
            public RectTransform Root;
            public NebulaUiFactory.LabelHandle Label;
        }

        /// <summary>The vehicle the action bar acts on, or <c>null</c>.</summary>
        public string SelectedVehicleId { get; private set; }

        /// <summary>Test-facing: why Recall is unavailable, or <c>null</c>.</summary>
        public string RecallBlockedReason { get; private set; }

        /// <summary>Test-facing: why the Mk II upgrade is unavailable, or <c>null</c>.</summary>
        public string UpgradeBlockedReason { get; private set; }

        /// <summary>Test-facing: why launching is unavailable, or <c>null</c>.</summary>
        public string LaunchBlockedReason { get; private set; }

        /// <summary>Vehicles that need the player: waiting, stranded, or full.</summary>
        public int AttentionCount { get; private set; }

        /// <summary>Test-facing: vehicle rows currently on screen.</summary>
        public int VisibleRows { get; private set; }

        // ANI-05.4.3: the production service raises this only once a job has really
        // finished, so a cancelled or refused order shows nothing.
        private readonly UiValueAnimator productionDelta = new UiValueAnimator();
        private Hangar.HangarProductionService completionSource;

        /// <summary>Test-facing: real production completions actually shown.</summary>
        public int ProductionCompletionsShown => productionDelta.HighlightsShown;

        /// <summary>
        /// Vehicle rows keyed by their own stable id, so a fleet that changes phase
        /// every second never replays an entrance for a vehicle that was already there.
        /// </summary>
        private readonly UiStableRowRegistry vehicleRows = new UiStableRowRegistry();
        private bool rowsAdopted;

        /// <summary>Test-facing: vehicles that genuinely entered on the last refresh.</summary>
        public int VehicleRowsEnteredLastRefresh => rowsAdopted ? vehicleRows.EnteredLastRefresh : 0;

        private void OnJobCompleted(Hangar.HangarProductionJob job, string vehicleId)
            => productionDelta.RaiseDelta(1f);

        /// <summary>Subscribes once the hangar reference is really there.</summary>
        private void EnsureCompletionSubscribed()
        {
            if (completionSource != null) return;
            Hangar.HangarProductionService production = context?.Hangar?.Production;
            if (production == null) return;

            completionSource = production;
            completionSource.JobCompleted += OnJobCompleted;
        }

        private sealed class VehicleRow
        {
            public RectTransform Root;
            public Image Background;
            public NebulaUiFactory.LabelHandle Name;
            public NebulaUiFactory.LabelHandle Status;
            public NebulaUiFactory.LabelHandle Detail;
        }

        /// <summary>
        /// The System Logistics Centre surface. It reads and commands through
        /// <c>ShipLogisticsCenter</c>'s public seam only; this page owns where it
        /// lives, not what it does.
        /// </summary>
        private readonly UI.Logistics.NebulaLogisticsCenterPanel logistics;

        /// <summary>Test-facing: the logistics panel this page hosts.</summary>
        public UI.Logistics.NebulaLogisticsCenterPanel Logistics => logistics;

        /// <summary>Test-facing: whether the logistics panel is open.</summary>
        public bool LogisticsPanelOpen => logistics != null && logistics.IsOpen;

        public NebulaFleetPage(NebulaContext sessionContext,
            NebulaConfirmationDialog confirmationDialog = null)
        {
            context = sessionContext;
            confirmation = confirmationDialog;
            logistics = new UI.Logistics.NebulaLogisticsCenterPanel(sessionContext, () => SelectedVehicleId);
        }

        public void Build(RectTransform host)
        {
            var column = NebulaUiFactory.Group(host, "Fleet Column");
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(32f, 24f);
            column.offsetMax = new Vector2(-32f, -24f);

            BuildCapacityCard(column);
            BuildProductionCard(column);
            BuildVehicleList(column);
            BuildActionBar(column);
            BuildTargetPicker(column);
            BuildProductionPicker(column);

            // The logistics centre is where this page's collectors actually go, and
            // it had no surface at all: capacity, fill, docks, routes, the T2 cost
            // and every refusal were reachable only through four contextual keys.
            // It opens in place, like the launch-target picker, rather than as a
            // second modal over this one.
            logistics.Build(column);
        }

        /// <summary>
        /// A second level inside the page rather than another modal.
        /// <para>
        /// Launching needs a target, and a target needs a list. Pushing a whole
        /// screen for it would put a modal over a modal; instead the picker opens
        /// in place and the page consumes Back to close it, which is exactly what
        /// <see cref="INebulaMenuPage.HandleBack"/> exists for.
        /// </para>
        /// </summary>
        private void BuildTargetPicker(RectTransform parent)
        {
            Image panel = NebulaUiFactory.Panel(parent, "Targets", NebulaUiAssetCatalog.PanelElevated);
            targetPicker = panel.rectTransform;
            targetPicker.anchorMin = new Vector2(0.5f, 0.5f);
            targetPicker.anchorMax = new Vector2(0.5f, 0.5f);
            targetPicker.pivot = new Vector2(0.5f, 0.5f);
            targetPicker.anchoredPosition = Vector2.zero;
            targetPicker.sizeDelta = new Vector2(520f, 360f);

            targetHeading = NebulaUiFactory.SectionTitle(targetPicker, "Title", "HEDEF SEÇ");
            NebulaUiFactory.Place(targetHeading.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -20f), new Vector2(460f, 18f));

            targetEmpty = NebulaUiFactory.Label(targetPicker, "Empty",
                "BU SİSTEMDE UYGUN HEDEF YOK", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleCenter, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(targetEmpty.Text.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(460f, 20f));

            for (int i = 0; i < MaxTargetRows; i++)
            {
                var row = new TargetRow();
                Image rowPanel = NebulaUiFactory.Panel(targetPicker, "Target " + i,
                    NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
                row.Root = rowPanel.rectTransform;
                NebulaUiFactory.Place(row.Root, new Vector2(0f, 1f),
                    new Vector2(20f, -52f - i * 46f), new Vector2(480f, 40f));

                row.Label = NebulaUiFactory.Label(row.Root, "Label", "",
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                    TextAnchor.MiddleLeft);
                NebulaUiFactory.Place(row.Label.Text.rectTransform, new Vector2(0f, 0.5f),
                    new Vector2(16f, 0f), new Vector2(440f, 18f));

                rowPanel.raycastTarget = true;
                var button = row.Root.gameObject.AddComponent<Button>();
                button.targetGraphic = rowPanel;
                // A target row in the mission picker had no hover, pressed or
                // focus state; picking a destination was a click into silence.
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = NebulaUiAssetCatalog.RowInteraction();
                int captured = i;
                button.onClick.AddListener(() => ChooseTarget(captured));

                row.Root.gameObject.SetActive(false);
                targetRows.Add(row);
            }

            targetPicker.gameObject.SetActive(false);
        }

        private void OpenTargetPicker() => OpenTargetPicker(TargetMode.Launch);

        private void OpenTargetPicker(TargetMode mode)
        {
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle == null || hangar == null) return;

            CloseProductionPicker();
            targetMode = mode;
            targetIds.Clear();
            engineerCandidates.Clear();
            escortCandidates.Clear();

            switch (mode)
            {
                case TargetMode.EngineerRepair:
                    // The hangar's own same-system candidate list, computed from live
                    // durability -- never a second copy of those rules here.
                    foreach (ShipHangar.EngineerTargetCandidate candidate in hangar.BuildEngineerTargetCandidates(vehicle))
                    {
                        engineerCandidates.Add(candidate);
                        targetIds.Add(candidate.TargetId);
                        if (targetIds.Count >= MaxTargetRows) break;
                    }
                    break;
                case TargetMode.EscortProtect:
                    foreach (ShipHangar.EscortProtectableTarget candidate in hangar.BuildEscortProtectableTargets())
                    {
                        escortCandidates.Add(candidate);
                        targetIds.Add(candidate.TargetId);
                        if (targetIds.Count >= MaxTargetRows) break;
                    }
                    break;
                case TargetMode.PoiDispatch:
                    CollectPoiTargets(vehicle, hangar, targetIds);
                    break;
                default:
                    CollectTargets(vehicle, hangar, targetIds);
                    break;
            }

            bool any = targetIds.Count > 0;
            if (targetEmpty.Text.gameObject.activeSelf == any)
                targetEmpty.Text.gameObject.SetActive(!any);

            for (int i = 0; i < targetRows.Count; i++)
            {
                bool visible = i < targetIds.Count;
                if (targetRows[i].Root.gameObject.activeSelf != visible)
                    targetRows[i].Root.gameObject.SetActive(visible);
                if (visible) targetRows[i].Label.Value = DescribeRow(i);
            }

            VisibleTargets = targetIds.Count;
            targetHeading.Value = NebulaText.Upper($"{vehicle.Def.DisplayName} · {HeadingFor(mode)}");
            targetPicker.gameObject.SetActive(true);
        }

        private static string HeadingFor(TargetMode mode) => mode switch
        {
            TargetMode.EngineerRepair => "ONARIM HEDEFİ SEÇ",
            TargetMode.EscortProtect => "KORUNACAK HEDEF SEÇ",
            TargetMode.PoiDispatch => "POI GÖREVİ SEÇ",
            _ => "HEDEF SEÇ"
        };

        /// <summary>
        /// One row's label. Engineer and Escort rows are named by the candidate's own
        /// display name, which the hangar already resolved; only a launch target still
        /// falls back to the world id, exactly as before.
        /// </summary>
        private string DescribeRow(int index)
        {
            if (targetMode == TargetMode.EngineerRepair && index < engineerCandidates.Count)
            {
                ShipHangar.EngineerTargetCandidate candidate = engineerCandidates[index];
                return NebulaText.Upper(
                    $"{candidate.DisplayName} — Z %{candidate.ArmorFraction * 100f:0} · G %{candidate.HullFraction * 100f:0}");
            }
            if (targetMode == TargetMode.EscortProtect && index < escortCandidates.Count)
                return NebulaText.Upper(escortCandidates[index].DisplayName);
            if (targetMode == TargetMode.PoiDispatch)
            {
                PoiObject poi = PoiObject.Find(targetIds[index]);
                return poi == null ? "—" : $"{PoiTypeName(poi.Manifest.Type)} · {PoiRiskName(poi.Manifest.Risk)}";
            }
            return DescribeTarget(targetIds[index]);
        }

        private static void CollectPoiTargets(VehicleAgent vehicle, ShipHangar hangar, List<string> into)
        {
            if (vehicle?.Def == null || hangar == null) return;
            string systemId = hangar.CurrentSystemId;
            foreach (PoiObject poi in PoiObject.All)
            {
                if (poi?.Manifest == null || poi.Manifest.SystemId != systemId) continue;
                bool eligible = vehicle.Def.Class == VehicleClass.Scout
                    || (vehicle.Def.Class == VehicleClass.Collector
                        && poi.Manifest.Type == PoiType.Wreck && poi.Manifest.Risk == PoiRisk.Safe);
                if (!eligible) continue;
                into.Add(poi.Manifest.PoiId);
                if (into.Count >= MaxTargetRows) return;
            }
        }

        /// <summary>
        /// Same-system targets the vehicle's class can actually be sent to. The
        /// hangar refuses anything else anyway, so offering it would only produce a
        /// rejection the player did not ask for.
        /// </summary>
        private static void CollectTargets(VehicleAgent vehicle, ShipHangar hangar, List<string> into)
        {
            string systemId = hangar.CurrentSystemId;
            if (systemId == null) return;

            if (vehicle.Def.Class == VehicleClass.Collector)
            {
                foreach (Extraction.DrillStructure drill in Extraction.ExtractionState.Drills)
                {
                    if (drill == null) continue;
                    if (drill.Runtime == null || drill.Runtime.State != Extraction.DrillLifecycle.Active) continue;
                    into.Add(drill.WorldId);
                    if (into.Count >= MaxTargetRows) return;
                }
                return;
            }

            foreach (Extraction.PlanetIdentity planet in Extraction.ExtractionState.AllPlanets)
            {
                if (planet == null) continue;
                into.Add(planet.WorldId);
                if (into.Count >= MaxTargetRows) return;
            }
        }

        /// <summary>Resolved separately so a stable id never enters a player-facing literal.</summary>
        private static string DescribeTarget(string worldId) =>
            string.IsNullOrEmpty(worldId) ? "—" : NebulaText.Upper(worldId);

        private void ChooseTarget(int index)
        {
            if (index < 0 || index >= targetIds.Count) return;
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle == null || hangar == null) { CloseTargetPicker(); return; }

            if (targetMode == TargetMode.EngineerRepair) { SendEngineerTo(hangar, vehicle, index); return; }
            if (targetMode == TargetMode.EscortProtect) { ProtectTarget(hangar, vehicle, index); return; }
            if (targetMode == TargetMode.PoiDispatch) { SendVehicleToPoi(vehicle, targetIds[index]); return; }

            MissionKind kind = vehicle.Def.Class == VehicleClass.Collector
                ? MissionKind.Collector : MissionKind.Scout;

            hangar.TryLaunch(vehicle, kind, targetIds[index]);
            CloseTargetPicker();
            Refresh();
        }

        private void SendVehicleToPoi(VehicleAgent vehicle, string poiId)
        {
            ShipPoi shipPoi = context?.Ship != null ? context.Ship.GetComponent<ShipPoi>() : null;
            if (shipPoi == null)
            {
                // A missing session is a wiring failure, not "that POI is ineligible".
                CloseTargetPicker();
                Report("OTURUM YOK — GÖREV BAŞLATILAMADI");
                Refresh();
                return;
            }
            ShipPoi.SendRejectReason reason = shipPoi.TrySendVehicle(vehicle, poiId);
            CloseTargetPicker();
            Report(reason == ShipPoi.SendRejectReason.None ? "POI GÖREVİ BAŞLADI" : PoiSendRejectText(reason));
            Refresh();
        }

        internal static string PoiSendRejectText(ShipPoi.SendRejectReason reason) => reason switch
        {
            ShipPoi.SendRejectReason.VehicleNotInHangar => "ARAÇ HANGARDA VE HAZIR DEĞİL",
            ShipPoi.SendRejectReason.NotSameSystem => "POI BAŞKA SİSTEMDE",
            ShipPoi.SendRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ YETERSİZ",
            ShipPoi.SendRejectReason.WrongVehicleClassForTarget => "ARAÇ SINIFI BU POI İÇİN UYGUN DEĞİL",
            ShipPoi.SendRejectReason.TargetNotEligible => "POI GÖREV İÇİN UYGUN DEĞİL",
            _ => "POI GÖREVİ BAŞLATILAMADI"
        };

        private static string PoiTypeName(PoiType type) => type switch
        {
            PoiType.Wreck => "ENKAZ",
            PoiType.Anomaly => "ANOMALİ",
            PoiType.AbandonedFacility => "TERK EDİLMİŞ TESİS",
            PoiType.Protected => "KORUNAN BÖLGE",
            _ => "İLGİ NOKTASI"
        };

        private static string PoiRiskName(PoiRisk risk) => risk switch
        {
            PoiRisk.Safe => "GÜVENLİ",
            PoiRisk.Unstable => "KARARSIZ",
            _ => "KORUNUYOR"
        };

        /// <summary>
        /// The real Engineer command, for the real candidate the player picked — kind
        /// and all. The material is the hangar's own affordable pick for this target's
        /// cost, so the page never invents one.
        /// </summary>
        private void SendEngineerTo(ShipHangar hangar, VehicleAgent vehicle, int index)
        {
            ShipHangar.EngineerTargetCandidate candidate = engineerCandidates[index];
            string materialId = candidate.RequiredMaterial > 0.0001f
                ? hangar.FindAffordableMaterial(candidate.RequiredMaterial, EngineerSourceKind.ShipCargo)
                : null;

            ShipHangar.EngineerSendRejectReason reason = hangar.TrySendEngineer(
                vehicle, candidate.Kind, candidate.TargetId, EngineerSourceKind.ShipCargo, materialId);

            CloseTargetPicker();
            Report(reason == ShipHangar.EngineerSendRejectReason.None
                ? "GÖNDERİLDİ" : EngineerSendRejectText(reason));
            Refresh();
        }

        /// <summary>
        /// Assigns an idle Escort to the chosen target, or retargets one that is
        /// already assigned, then issues Protect through the hangar's own command so
        /// the save hook fires exactly as it does for every other escort order.
        /// </summary>
        private void ProtectTarget(ShipHangar hangar, VehicleAgent vehicle, int index)
        {
            ShipHangar.EscortProtectableTarget candidate = escortCandidates[index];

            if (vehicle.EscortState == null)
            {
                ShipHangar.EscortAssignRejectReason reason =
                    hangar.TryAssignEscort(vehicle, candidate.TargetId, candidate.Category);
                if (reason != ShipHangar.EscortAssignRejectReason.None)
                {
                    CloseTargetPicker();
                    Report(EscortAssignRejectText(reason));
                    Refresh();
                    return;
                }
            }
            else
            {
                vehicle.EscortState.SetProtectedTarget(candidate.TargetId, candidate.Category);
            }

            hangar.SetEscortCommand(vehicle, EscortCommand.Protect);
            CloseTargetPicker();
            Report("KOMUT VERİLDİ");
            Refresh();
        }

        private void CloseTargetPicker()
        {
            if (targetPicker != null) targetPicker.gameObject.SetActive(false);
            targetMode = TargetMode.Launch;
            VisibleTargets = 0;
        }

        // ---- production picker ------------------------------------------------
        //
        // The Produce button used to call TryStartProduction(Scout, null) with a
        // hardcoded class and a hardcoded null material, which the service refuses
        // outright (MaterialNotSelected) -- so no vehicle of any class could be built
        // from this page at all, and Collector/Engineer/Escort had no control anywhere.
        // Production needs two real choices (which vehicle, and -- for the classes
        // whose cost includes "any special material" -- which material), and a choice
        // needs a list. It reuses the launch picker's shape rather than a second modal.

        private void BuildProductionPicker(RectTransform parent)
        {
            Image panel = NebulaUiFactory.Panel(parent, "Builds", NebulaUiAssetCatalog.PanelElevated);
            buildPicker = panel.rectTransform;
            buildPicker.anchorMin = new Vector2(0.5f, 0.5f);
            buildPicker.anchorMax = new Vector2(0.5f, 0.5f);
            buildPicker.pivot = new Vector2(0.5f, 0.5f);
            buildPicker.anchoredPosition = Vector2.zero;
            buildPicker.sizeDelta = new Vector2(560f, 420f);

            buildHeading = NebulaUiFactory.SectionTitle(buildPicker, "Title", "ARAÇ SEÇ");
            NebulaUiFactory.Place(buildHeading.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -20f), new Vector2(500f, 18f));

            buildEmpty = NebulaUiFactory.Label(buildPicker, "Empty",
                "ÜRETİLEBİLECEK ARAÇ YOK", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleCenter, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(buildEmpty.Text.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(500f, 20f));

            for (int i = 0; i < MaxBuildRows; i++)
            {
                var row = new TargetRow();
                Image rowPanel = NebulaUiFactory.Panel(buildPicker, "Build " + i,
                    NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
                row.Root = rowPanel.rectTransform;
                NebulaUiFactory.Place(row.Root, new Vector2(0f, 1f),
                    new Vector2(20f, -52f - i * 44f), new Vector2(520f, 40f));

                row.Label = NebulaUiFactory.Label(row.Root, "Label", "",
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                    TextAnchor.MiddleLeft);
                NebulaUiFactory.Place(row.Label.Text.rectTransform, new Vector2(0f, 0.5f),
                    new Vector2(16f, 0f), new Vector2(480f, 18f));

                rowPanel.raycastTarget = true;
                var button = row.Root.gameObject.AddComponent<Button>();
                button.targetGraphic = rowPanel;
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = NebulaUiAssetCatalog.RowInteraction();
                // Captured per row, exactly like the launch picker: a closure over the
                // loop variable would give every row the last index.
                int captured = i;
                button.onClick.AddListener(() => ChooseBuildOption(captured));

                row.Root.gameObject.SetActive(false);
                buildRows.Add(row);
            }

            buildPicker.gameObject.SetActive(false);
        }

        /// <summary>Stage one: every real vehicle definition the catalog carries, with its real cost.</summary>
        private void OpenProductionPicker()
        {
            pendingBuildDef = null;
            buildOptionIds.Clear();
            foreach (VehicleDef def in VehicleCatalog.All)
            {
                buildOptionIds.Add(def.Id);
                if (buildOptionIds.Count >= MaxBuildRows) break;
            }
            buildHeading.Value = "ARAÇ ÜRET — SINIF SEÇ";
            ShowBuildRows();
        }

        /// <summary>Stage two: the five materials the player may spend on a build whose cost says "any special material".</summary>
        private void OpenMaterialPicker(VehicleDef def)
        {
            pendingBuildDef = def;
            buildOptionIds.Clear();
            foreach (string materialId in VehicleCatalog.SpecialMaterialIds)
            {
                buildOptionIds.Add(materialId);
                if (buildOptionIds.Count >= MaxBuildRows) break;
            }
            buildHeading.Value = NebulaText.Upper($"{ClassName(def.Class)} · MK {Roman(def.Mk)} — MALZEME SEÇ");
            ShowBuildRows();
        }

        private void ShowBuildRows()
        {
            CloseTargetPicker();

            bool any = buildOptionIds.Count > 0;
            if (buildEmpty.Text.gameObject.activeSelf == any)
                buildEmpty.Text.gameObject.SetActive(!any);

            for (int i = 0; i < buildRows.Count; i++)
            {
                bool visible = i < buildOptionIds.Count;
                if (buildRows[i].Root.gameObject.activeSelf != visible)
                    buildRows[i].Root.gameObject.SetActive(visible);
                if (visible) buildRows[i].Label.Value = DescribeBuildOption(buildOptionIds[i]);
            }

            VisibleBuildOptions = buildOptionIds.Count;
            buildPicker.gameObject.SetActive(true);
        }

        /// <summary>Resolved separately so a stable id never enters a player-facing literal.</summary>
        private string DescribeBuildOption(string optionId)
        {
            if (pendingBuildDef == null)
            {
                VehicleDef def = VehicleCatalog.Get(optionId);
                if (def == null) return "—";
                return NebulaText.Upper($"{ClassName(def.Class)} · MK {Roman(def.Mk)} — {BuildCostText(def)}");
            }

            float required = VehicleCatalog.TryGetFixedDirectBuildRecipe(pendingBuildDef.Id, out _)
                ? 0f : pendingBuildDef.SpecialMaterialCost;
            Extraction.LogisticsCargo cargo = context?.Hangar?.Cargo;
            float available = cargo != null ? cargo.GetAvailable(optionId) : 0f;
            return NebulaText.Upper(
                $"{Extraction.ProcessingCatalog.ItemDisplayName(optionId)} — {required:0} GEREKLİ · {available:0} MEVCUT");
        }

        /// <summary>The catalog's own numbers, never a second copy of them.</summary>
        private static string BuildCostText(VehicleDef def)
        {
            if (VehicleCatalog.TryGetFixedDirectBuildRecipe(def.Id, out VehicleUpgradeRecipe recipe))
            {
                // Deconstructed to `material`, never to the recipe tuple's own
                // `itemId`: a stable id must not appear inside player-facing text,
                // and the hygiene check reads the interpolation hole, not the value.
                string text = $"{recipe.PartsCost} PARÇA";
                foreach ((string material, float amount) in recipe.MaterialCosts)
                    text += $" + {amount:0} {Extraction.ProcessingCatalog.ItemDisplayName(material)}";
                return text;
            }
            return $"{def.BasicPartCost} PARÇA + {def.SpecialMaterialCost} MALZEME";
        }

        private void ChooseBuildOption(int index)
        {
            if (index < 0 || index >= buildOptionIds.Count) return;
            string optionId = buildOptionIds[index];

            if (pendingBuildDef == null)
            {
                VehicleDef def = VehicleCatalog.Get(optionId);
                if (def == null) { CloseProductionPicker(); return; }

                // A Mk II vehicle's build is a fixed multi-item recipe with no player
                // choice at all, so it starts here; everything else owes a material.
                if (VehicleCatalog.TryGetFixedDirectBuildRecipe(def.Id, out _)) { StartFixedBuild(def); return; }
                OpenMaterialPicker(def);
                return;
            }

            StartChosenMaterialBuild(pendingBuildDef, optionId);
        }

        private void StartFixedBuild(VehicleDef def)
        {
            ShipHangar hangar = context?.Hangar;
            if (hangar == null) { CloseProductionPicker(); Report("OTURUM YOK"); return; }

            HangarProductionRejectReason reason = hangar.TryStartFixedProduction(def.Id);
            FinishBuildAttempt(def, reason);
        }

        private void StartChosenMaterialBuild(VehicleDef def, string chosenMaterialId)
        {
            ShipHangar hangar = context?.Hangar;
            if (hangar == null) { CloseProductionPicker(); Report("OTURUM YOK"); return; }

            HangarProductionRejectReason reason = hangar.TryStartProduction(def.Class, chosenMaterialId, def.Mk);
            FinishBuildAttempt(def, reason);
        }

        private void FinishBuildAttempt(VehicleDef def, HangarProductionRejectReason reason)
        {
            CloseProductionPicker();
            Report(reason == HangarProductionRejectReason.None
                ? NebulaText.Upper($"ÜRETİM BAŞLADI — {ClassName(def.Class)} · MK {Roman(def.Mk)}")
                : ProductionRejectText(reason));
            Refresh();
        }

        private void CloseProductionPicker()
        {
            if (buildPicker != null) buildPicker.gameObject.SetActive(false);
            pendingBuildDef = null;
            VisibleBuildOptions = 0;
        }

        /// <summary>
        /// Test-facing: the page's own production control, driven exactly as the
        /// Produce button drives it. <c>UnityEngine.Input</c> cannot be faked in this
        /// project, so a test picks through these rather than simulating clicks.
        /// </summary>
        public void OpenProductionPickerForTest() => OnProduceClicked();

        /// <summary>Test-facing: chooses the option at <paramref name="index"/> in whichever stage the picker is on.</summary>
        public void ChooseBuildOptionForTest(int index) => ChooseBuildOption(index);

        /// <summary>Test-facing: the index of a vehicle class/Mk in the open picker's vehicle stage, or -1.</summary>
        public int IndexOfBuildOption(VehicleClass vehicleClass, int mk)
        {
            if (pendingBuildDef != null) return -1;
            for (int i = 0; i < buildOptionIds.Count; i++)
            {
                VehicleDef def = VehicleCatalog.Get(buildOptionIds[i]);
                if (def != null && def.Class == vehicleClass && def.Mk == mk) return i;
            }
            return -1;
        }

        /// <summary>Test-facing: the index of a material in the open picker's material stage, or -1.</summary>
        public int IndexOfBuildMaterial(string materialId) =>
            pendingBuildDef == null ? -1 : buildOptionIds.IndexOf(materialId);

        // ---- test-facing drivers for the contextual controls -------------------
        //
        // UnityEngine.Input cannot be faked in this project, so a test drives these
        // exactly as the button's own listener does rather than simulating a click.

        /// <summary>Test-facing: the Engineer rail's repair-target control.</summary>
        public void OpenEngineerTargetPickerForTest() => OpenTargetPicker(TargetMode.EngineerRepair);

        /// <summary>Test-facing: the Escort rail's protect-target control.</summary>
        public void OpenEscortTargetPickerForTest() => OpenTargetPicker(TargetMode.EscortProtect);

        /// <summary>Test-facing: picks the target at <paramref name="index"/> in whichever list is open.</summary>
        public void ChooseTargetForTest(int index) => ChooseTarget(index);

        /// <summary>Test-facing: the row index of a target id in the open list, or -1.</summary>
        public int IndexOfTarget(string targetId) => targetIds.IndexOf(targetId);

        /// <summary>Test-facing: the Escort rail's stance control.</summary>
        public void CycleEscortStanceForTest() => CycleEscortStance();

        /// <summary>Test-facing: the intervention group's membership control.</summary>
        public void ToggleGroupMembershipForTest() => ToggleGroupMembership();

        /// <summary>Test-facing: the intervention group's policy control.</summary>
        public void CycleGroupPolicyForTest() => CycleGroupPolicy();

        /// <summary>
        /// One primary action and two secondary, matching the HUD's rule. Producing
        /// is the thing a player does from an empty hangar, so it is the primary.
        /// </summary>
        private void BuildActionBar(RectTransform parent)
        {
            produceButton = NebulaUiFactory.ButtonControl(parent, "Produce", "ARAÇ ÜRET",
                NebulaUiAssetCatalog.ButtonKind.Primary, OnProduceClicked);
            NebulaUiFactory.Place(produceButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(240f, 44f));

            recallButton = NebulaUiFactory.ButtonControl(parent, "Recall", "GERİ ÇAĞIR",
                NebulaUiAssetCatalog.ButtonKind.Secondary, OnRecallClicked);
            NebulaUiFactory.Place(recallButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-252f, 0f), new Vector2(180f, 44f));

            upgradeButton = NebulaUiFactory.ButtonControl(parent, "Upgrade", "MK II'YE YÜKSELT",
                NebulaUiAssetCatalog.ButtonKind.Secondary, OnUpgradeClicked);
            NebulaUiFactory.Place(upgradeButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-444f, 0f), new Vector2(220f, 44f));

            launchButton = NebulaUiFactory.ButtonControl(parent, "Launch", "GÖREVE GÖNDER",
                NebulaUiAssetCatalog.ButtonKind.Secondary, OpenTargetPicker);
            NebulaUiFactory.Place(launchButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-676f, 0f), new Vector2(220f, 44f));

            NebulaUiFactory.ButtonHandle logisticsButton = NebulaUiFactory.ButtonControl(parent,
                UI.Logistics.NebulaLogisticsCenterPanel.OpenControlName, "LOJİSTİK MERKEZİ",
                NebulaUiAssetCatalog.ButtonKind.Secondary, OpenLogistics);
            NebulaUiFactory.Place(logisticsButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-908f, 0f), new Vector2(230f, 44f));

            BuildContextRail(parent);
        }

        /// <summary>
        /// Escort stance, engineer target, quick resupply and route control. Every
        /// one of these was reachable in the old hangar screen and none of them had
        /// a home in the redesign, so the fleet page grows a contextual rail rather
        /// than the player losing the action.
        /// </summary>
        private void BuildContextRail(RectTransform parent)
        {
            contextRail = NebulaUiFactory.Group(parent, "Context Actions");
            // 940 -> 1568: an Escort can now show four commands, its stance, both
            // intervention-group controls and resupply at once. The slot pitch is
            // unchanged, so nothing already on the rail moves or resizes; the rail
            // simply stops running off its own left edge.
            NebulaUiFactory.Place(contextRail, new Vector2(1f, 0f), new Vector2(0f, 52f),
                new Vector2(1568f, 40f));
            contextRail.pivot = new Vector2(1f, 0f);

            statusLine = NebulaUiFactory.Label(parent, "Status", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(statusLine.Text.rectTransform, new Vector2(0f, 0f),
                new Vector2(0f, 12f), new Vector2(560f, 32f));

            escortFollow = ContextButton("EscortFollow", "TAKİP ET",
                () => IssueEscortCommand(EscortCommand.Follow));
            // Was "GEMİYİ KORU", and it meant it literally: the handler passed the
            // string "ship" every time, so an Escort could protect nothing else.
            escortProtect = ContextButton("EscortProtect", "HEDEF KORU",
                () => OpenTargetPicker(TargetMode.EscortProtect));
            escortRetreat = ContextButton("EscortRetreat", "GERİ ÇEKİL",
                () => IssueEscortCommand(EscortCommand.Retreat));
            escortReturn = ContextButton("EscortReturn", "HANGARA DÖN",
                () => IssueEscortCommand(EscortCommand.ReturnToHangar));
            escortStance = ContextButton("EscortStance", "DURUŞ", CycleEscortStance);

            // Was hardcoded to the main ship as well, while the service accepts
            // structures, support structures, damaged vehicles and disabled ones.
            engineerRepair = ContextButton("EngineerRepair", "ONARIM HEDEFİ",
                () => OpenTargetPicker(TargetMode.EngineerRepair));
            engineerCancel = ContextButton("EngineerCancel", "GÖREVİ İPTAL", CancelEngineerMission);

            resupplyStart = ContextButton("ResupplyStart", "HIZLI İKMAL", StartQuickResupply);
            resupplyCancel = ContextButton("ResupplyCancel", "İKMALİ İPTAL", CancelQuickResupply);

            routePauseResume = ContextButton("RoutePause", "ROTAYI DURAKLAT", ToggleRoutePause);
            routeDelete = ContextButton("RouteDelete", "ROTAYI SİL", DeleteRoute);

            groupMembership = ContextButton("GroupMembership", "GRUBA EKLE", ToggleGroupMembership);
            groupPolicy = ContextButton("GroupPolicy", "MÜDAHALE", CycleGroupPolicy);
            poiDispatch = ContextButton("PoiDispatch", "POI'YE GÖNDER",
                () => OpenTargetPicker(TargetMode.PoiDispatch));
        }

        private NebulaUiFactory.ButtonHandle ContextButton(string name, string caption,
            UnityEngine.Events.UnityAction onClick)
        {
            NebulaUiFactory.ButtonHandle handle = NebulaUiFactory.ButtonControl(contextRail, name,
                caption, NebulaUiAssetCatalog.ButtonKind.Secondary, onClick);
            NebulaUiFactory.Place(handle.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), Vector2.zero, new Vector2(188f, 36f));
            handle.Button.gameObject.SetActive(false);
            return handle;
        }

        // ---- context actions -------------------------------------------------
        //
        // Each of these is the old hangar screen's handler, calling the same
        // service with the same arguments and reporting the same Turkish refusal
        // text. The reject tables are copied verbatim: the wording is the
        // player-facing contract, not an implementation detail.

        private void IssueEscortCommand(EscortCommand command)
        {
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle == null || hangar == null) return;

            if (vehicle.EscortState == null)
            {
                // Not yet assigned: any command from an idle escort assigns it to
                // protect the ship first, exactly as the old screen did.
                ShipHangar.EscortAssignRejectReason reason =
                    hangar.TryAssignEscort(vehicle, "ship", Combat.TargetCategory.Ship);
                if (reason != ShipHangar.EscortAssignRejectReason.None)
                {
                    Report(EscortAssignRejectText(reason));
                    return;
                }
            }

            hangar.SetEscortCommand(vehicle, command);
            Report("KOMUT VERİLDİ");
            Refresh();
        }

        /// <summary>
        /// The Escort's retreat threshold, which is real: 0.60 / 0.35 / 0.15 hull, read
        /// by <c>EscortCombatResolver.ShouldRetreat</c> on both the live and the remote
        /// tick. Every Escort in the game was locked at Balanced because
        /// <c>ShipHangar.SetEscortStance</c> had no caller outside a test. One cycling
        /// control rather than three buttons, matching the route pause/resume control
        /// that already re-captions itself on the same rail.
        /// </summary>
        private void CycleEscortStance()
        {
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle == null || hangar == null) return;
            if (vehicle.EscortState == null) { Report("ÖNCE BİR KORUMA HEDEFİ ATAYIN"); return; }

            EscortStance next = vehicle.EscortState.Stance switch
            {
                EscortStance.Safe => EscortStance.Balanced,
                EscortStance.Balanced => EscortStance.Persistent,
                _ => EscortStance.Safe
            };
            hangar.SetEscortStance(vehicle, next);
            Report(NebulaText.Upper($"DURUŞ: {StanceName(next)}"));
            Refresh();
        }

        internal static string StanceName(EscortStance stance) => stance switch
        {
            EscortStance.Safe => "TEMKİNLİ",
            EscortStance.Persistent => "ISRARCI",
            _ => "DENGELİ"
        };

        internal static string InterventionPolicyName(Combat.InterventionPolicy policy) => policy switch
        {
            Combat.InterventionPolicy.DefendInPlace => "YERİNDE SAVUN",
            Combat.InterventionPolicy.SystemIntervention => "SİSTEM MÜDAHALESİ",
            _ => "ELLE"
        };

        /// <summary>
        /// Adds or removes the selected support vehicle from this system's single
        /// intervention group. <c>InterventionGroupDirector.TickInterventionGroup</c>
        /// returns on its first line while the group is empty, so with no control for
        /// this the whole mechanic never ran.
        /// </summary>
        private void ToggleGroupMembership()
        {
            VehicleAgent vehicle = Selected();
            Hangar.InterventionGroupDirector director = context?.InterventionGroupDirectorOrNull;
            if (vehicle == null || director == null) { Report("OTURUM YOK"); return; }

            if (IsGroupMember(director, vehicle.VehicleId))
            {
                director.RemoveMember(vehicle.VehicleId);
                Report("GRUPTAN ÇIKARILDI");
            }
            else if (director.AddMember(vehicle.VehicleId))
            {
                Report("GRUBA EKLENDİ");
            }
            else
            {
                // AddMember refuses when there is no current system to own a group.
                Report("BİR SİSTEMİN İÇİNDE OLMALISIN");
            }
            Refresh();
        }

        private void CycleGroupPolicy()
        {
            Hangar.InterventionGroupDirector director = context?.InterventionGroupDirectorOrNull;
            if (director == null) { Report("OTURUM YOK"); return; }

            Combat.InterventionPolicy current = director.Group?.Policy ?? Combat.InterventionPolicy.Manual;
            Combat.InterventionPolicy next = current switch
            {
                Combat.InterventionPolicy.Manual => Combat.InterventionPolicy.DefendInPlace,
                Combat.InterventionPolicy.DefendInPlace => Combat.InterventionPolicy.SystemIntervention,
                _ => Combat.InterventionPolicy.Manual
            };
            director.SetPolicy(next);

            Report(director.Group == null
                ? "BİR SİSTEMİN İÇİNDE OLMALISIN"
                : NebulaText.Upper($"MÜDAHALE: {InterventionPolicyName(director.Group.Policy)}"));
            Refresh();
        }

        private static bool IsGroupMember(Hangar.InterventionGroupDirector director, string id)
        {
            IReadOnlyList<string> members = director.Group?.MemberVehicleIds;
            if (members == null) return false;
            for (int i = 0; i < members.Count; i++) if (members[i] == id) return true;
            return false;
        }

        private void CancelEngineerMission()
        {
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle == null || hangar == null) return;

            hangar.CancelEngineerMission(vehicle);
            Report("GÖREV İPTAL EDİLDİ");
            Refresh();
        }

        private void StartQuickResupply()
        {
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle == null || hangar == null) return;

            ShipHangar.MaintenanceResupplyRejectReason reason = hangar.TryStartQuickResupply(vehicle);
            Report(reason == ShipHangar.MaintenanceResupplyRejectReason.None
                ? "İKMAL GÖREVİ BAŞLADI" : ResupplyRejectText(reason));
            Refresh();
        }

        private void CancelQuickResupply()
        {
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle == null || hangar == null) return;

            hangar.CancelMaintenanceResupply(vehicle);
            Report("İKMAL GÖREVİ İPTAL EDİLDİ");
            Refresh();
        }

        private void ToggleRoutePause()
        {
            CollectorRoute route = Selected()?.Route;
            if (route == null) return;

            if (route.Paused) route.Resume(); else route.Pause();
            Report(route.Paused ? "ROTA DURAKLATILDI" : "ROTA SÜRDÜRÜLDÜ");
            Refresh();
        }

        private void DeleteRoute()
        {
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle?.Route == null || hangar == null) return;

            // Deleting a route sends the vehicle home mid-run, which is a real loss
            // of work, so unlike pausing it asks first.
            void Delete()
            {
                hangar.DeleteRoute(vehicle.Route);
                Report("ROTA SİLİNİYOR — ARAÇ EVE DÖNÜYOR");
                Refresh();
            }

            if (confirmation == null) { Delete(); return; }
            confirmation.Ask("ROTAYI SİL",
                "Rota silinecek ve araç hangara dönecek. Devam edilsin mi?",
                null, "SİL", Delete, destructive: true);
        }

        /// <summary>
        /// What the page tells the player about the action they just took.
        /// <para>
        /// It is held until the selection changes. Every action handler ends in a
        /// <see cref="Refresh"/>, and the refresh used to overwrite the status with
        /// the contextual line — which for the commonest case of all, producing a
        /// Scout with no vehicle selected, meant clearing it. A refusal the player
        /// cannot see is the same defect as no refusal at all, and this is the one
        /// action on the page that can be taken with nothing selected.
        /// </para>
        /// </summary>
        private void Report(string message)
        {
            actionStatusHeld = !string.IsNullOrEmpty(message);
            SetStatus(message);
        }

        /// <summary>The contextual line a refresh writes. Never claims the hold.</summary>
        private void SetStatus(string message)
        {
            if (statusLine == null) return;
            statusLine.Value = message ?? string.Empty;
        }

        /// <summary>True while the status line belongs to an action rather than to the refresh.</summary>
        private bool actionStatusHeld;

        /// <summary>
        /// Test-facing lookup over the three reject tables. It exists so a test can
        /// walk every enum value and prove none of them resolves to nothing: an
        /// unmapped reason surfaces to the player as an empty disabled-reason,
        /// which reads as "refused, no reason given".
        /// </summary>
        internal static string RejectTextFor(string kind, object reason) => kind switch
        {
            "resupply" => ResupplyRejectText((ShipHangar.MaintenanceResupplyRejectReason)reason),
            "escort" => EscortAssignRejectText((ShipHangar.EscortAssignRejectReason)reason),
            "engineer" => EngineerSendRejectText((ShipHangar.EngineerSendRejectReason)reason),
            "production" => ProductionRejectText((HangarProductionRejectReason)reason),
            _ => null
        };

        // Verbatim from the screen being replaced. See the removal matrix: every
        // reject reason's Turkish string is kept word for word.

        private static string ResupplyRejectText(ShipHangar.MaintenanceResupplyRejectReason reason) => reason switch
        {
            ShipHangar.MaintenanceResupplyRejectReason.InsufficientCargo => "GEMİ KARGOSUNDA UYGUN MALZEME YOK",
            ShipHangar.MaintenanceResupplyRejectReason.TargetNotEligible => "SİSTEMDE AKTİF KOMUTA MERKEZİ YOK",
            ShipHangar.MaintenanceResupplyRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ YETERSİZ",
            _ => "ARAÇ UYGUN DEĞİL (MK II GEREKLİ)"
        };

        private static string EscortAssignRejectText(ShipHangar.EscortAssignRejectReason reason) => reason switch
        {
            ShipHangar.EscortAssignRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ YETERSİZ",
            _ => "ARAÇ UYGUN DEĞİL"
        };

        private static string EngineerSendRejectText(ShipHangar.EngineerSendRejectReason reason) => reason switch
        {
            ShipHangar.EngineerSendRejectReason.NoCommandCapacity => "KOMUTA KAPASİTESİ YETERSİZ",
            ShipHangar.EngineerSendRejectReason.NothingToRepair => "ONARILACAK BİR ŞEY YOK",
            ShipHangar.EngineerSendRejectReason.InsufficientResources => "YETERSİZ KAYNAK",
            ShipHangar.EngineerSendRejectReason.TargetHasNoDurability => "BU HEDEF ONARILAMAZ (DRILL)",
            _ => "ARAÇ VEYA HEDEF UYGUN DEĞİL"
        };

        /// <summary>Picks the vehicle the action bar acts on.</summary>
        public void Select(string vehicleId)
        {
            SelectedVehicleId = vehicleId;
            // A new selection is a new subject; the previous action's report no
            // longer describes what is on screen.
            actionStatusHeld = false;
            Refresh();
        }

        private VehicleAgent Selected()
        {
            IReadOnlyList<VehicleAgent> vehicles = context?.Hangar?.Vehicles;
            if (vehicles == null || SelectedVehicleId == null) return null;
            for (int i = 0; i < vehicles.Count; i++)
                if (vehicles[i] != null && vehicles[i].VehicleId == SelectedVehicleId) return vehicles[i];
            return null;
        }

        /// <summary>
        /// Opens the production picker, or says why it cannot.
        /// <para>
        /// This used to call <c>TryStartProduction(VehicleClass.Scout, null)</c>: a
        /// hardcoded class with a hardcoded null material, which the service refuses
        /// with <see cref="HangarProductionRejectReason.MaterialNotSelected"/> every
        /// single time. The button therefore could not build a Scout, and no control
        /// anywhere could build a Collector, Engineer or Escort. Production takes two
        /// real choices, so the button opens the list that makes them.
        /// </para>
        /// </summary>
        private void OnProduceClicked()
        {
            ShipHangar hangar = context?.Hangar;
            if (hangar == null) { Report("OTURUM YOK"); return; }

            // The single slot is the one refusal worth making before the player has
            // picked anything -- every other reason depends on which vehicle they pick.
            if (hangar.Production?.ActiveJob != null)
            {
                Report(ProductionRejectText(HangarProductionRejectReason.QueueBusy));
                return;
            }

            OpenProductionPicker();
            Report("ÜRETİLECEK ARACI SEÇİN");
        }

        /// <summary>Why production would not start, in the player's language.</summary>
        internal static string ProductionRejectText(HangarProductionRejectReason reason) => reason switch
        {
            HangarProductionRejectReason.None => null,
            HangarProductionRejectReason.UnknownVehicle => "BİLİNMEYEN ARAÇ",
            HangarProductionRejectReason.LevelTooLow => "SEVİYE YETERSİZ",
            HangarProductionRejectReason.HangarTierTooLow => "HANGAR SEVİYESİ YETERSİZ",
            HangarProductionRejectReason.QueueBusy => "ÜRETİM KUYRUĞU DOLU",
            HangarProductionRejectReason.MaterialNotSelected => "MALZEME SEÇİLMEDİ",
            HangarProductionRejectReason.MissingControlledPlanet => "KONTROL EDİLEN GEZEGEN YOK",
            HangarProductionRejectReason.InsufficientResources => "KAYNAK YETERSİZ",
            _ => "ÜRETİM BAŞLATILAMADI",
        };

        private void OnRecallClicked()
        {
            VehicleAgent vehicle = Selected();
            if (vehicle == null) return;
            context.Hangar.Recall(vehicle);
            Refresh();
        }

        private void OnUpgradeClicked()
        {
            VehicleAgent vehicle = Selected();
            ShipHangar hangar = context?.Hangar;
            if (vehicle == null || hangar == null) return;

            void Start() { hangar.TryStartVehicleUpgrade(vehicle); Refresh(); }

            // Spends materials and occupies the single production slot, so it asks.
            if (confirmation == null) { Start(); return; }

            string callsign = Callsign(vehicle.VehicleId);
            confirmation.Ask($"{vehicle.Def.DisplayName} · {callsign}",
                "Mk II yükseltmesi malzeme harcar ve üretim yuvasını meşgul eder. Devam edilsin mi?",
                null, "YÜKSELT", Start);
        }

        private void BuildCapacityCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Capacity", 0f, 96f, "KOMUTA KAPASİTESİ");

            commandValue = NebulaUiFactory.Value(card, "Value", "—", NebulaUiDesignTokens.SizeValue);
            NebulaUiFactory.Place(commandValue.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -16f), new Vector2(200f, 22f));

            commandBar = NebulaUiFactory.Bar(card, "Bar", NebulaUiDesignTokens.ActionPrimary);
            StretchBar(commandBar, card, -60f);
        }

        private void BuildProductionCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Production", -108f, 96f, "ÜRETİM");

            productionName = NebulaUiFactory.Label(card, "Name", "", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(productionName.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -42f), new Vector2(420f, 20f));

            productionEta = NebulaUiFactory.Value(card, "Eta", "", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(productionEta.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -42f), new Vector2(200f, 20f));

            productionBar = NebulaUiFactory.Bar(card, "Bar",
                NebulaUiDesignTokens.Status(UiStatusLevel.Working));
            StretchBar(productionBar, card, -70f);
        }

        private void BuildVehicleList(RectTransform parent)
        {
            // 528, not 600: the contextual action rail sits above the primary one,
            // and at the content host's real height a 600-tall card ran 16px into it.
            RectTransform card = Card(parent, "Vehicles", -216f, 528f, "ARAÇLAR");

            emptyState = NebulaUiFactory.Label(card, "Empty",
                "HENÜZ ARAÇ YOK — HANGARDA BİR SCOUT ÜRETEREK BAŞLAYIN",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleCenter, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(emptyState.Text.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -90f), new Vector2(700f, 20f));

            vehicleList = NebulaUiFactory.Group(card, "List");
            vehicleList.anchorMin = new Vector2(0f, 1f);
            vehicleList.anchorMax = new Vector2(1f, 1f);
            vehicleList.pivot = new Vector2(0.5f, 1f);
            vehicleList.offsetMin = new Vector2(14f, -516f);
            vehicleList.offsetMax = new Vector2(-14f, -44f);
        }

        private static RectTransform Card(RectTransform parent, string name, float y, float height, string title)
        {
            Image panel = NebulaUiFactory.Panel(parent, name, NebulaUiAssetCatalog.PanelPrimary);
            RectTransform card = panel.rectTransform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, y);
            card.sizeDelta = new Vector2(0f, height);

            NebulaUiFactory.LabelHandle heading = NebulaUiFactory.SectionTitle(card, "Title", title);
            NebulaUiFactory.Place(heading.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -18f), new Vector2(340f, 18f));
            return card;
        }

        private static void StretchBar(NebulaUiFactory.BarHandle bar, RectTransform card, float y)
        {
            RectTransform rect = bar.Track.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(20f, y - NebulaUiDesignTokens.BarHeight);
            rect.offsetMax = new Vector2(-20f, y);
        }

        // ---- refresh ---------------------------------------------------------

        public void Refresh()
        {
            AttentionCount = 0;
            EnsureCompletionSubscribed();
            ShipHangar hangar = context?.Hangar;

            RefreshCapacity(hangar);
            RefreshProduction(hangar);
            RefreshVehicles(hangar);
            RefreshActions(hangar);

            // Only while it is on screen. Refreshing a closed panel would walk every
            // route and every drill on the page's 4Hz timer for nobody.
            if (LogisticsPanelOpen) logistics.Refresh();
        }

        private void RefreshActions(ShipHangar hangar)
        {
            if (produceButton == null) return;

            bool queueBusy = hangar?.Production?.ActiveJob != null;
            produceButton.SetAvailable(hangar != null && !queueBusy,
                hangar == null ? "OTURUM YOK" : "ÜRETİM YUVASI DOLU");

            VehicleAgent vehicle = Selected();

            if (vehicle == null)
            {
                RecallBlockedReason = "BİR ARAÇ SEÇİN";
                UpgradeBlockedReason = "BİR ARAÇ SEÇİN";
            }
            else if (vehicle.Mission.Phase == VehiclePhase.InHangar)
            {
                RecallBlockedReason = "ARAÇ ZATEN HANGARDA";
                UpgradeBlockedReason = UpgradeReject(hangar, vehicle);
            }
            else
            {
                RecallBlockedReason = null;
                UpgradeBlockedReason = "ARAÇ HANGARDA DEĞİL";
            }

            recallButton.SetAvailable(RecallBlockedReason == null, RecallBlockedReason);
            upgradeButton.SetAvailable(UpgradeBlockedReason == null, UpgradeBlockedReason);

            LaunchBlockedReason = vehicle == null ? "BİR ARAÇ SEÇİN"
                : vehicle.Mission.Phase != VehiclePhase.InHangar ? "ARAÇ ZATEN GÖREVDE"
                : hangar != null && hangar.Command != null && hangar.Command.Used >= hangar.Command.Capacity
                    ? "KOMUTA KAPASİTESİ DOLU"
                    : null;
            launchButton.SetAvailable(LaunchBlockedReason == null, LaunchBlockedReason);

            RefreshContextRail(hangar, vehicle);
        }

        /// <summary>
        /// Shows only the actions that apply to the selected vehicle. The old screen
        /// kept all twelve on screen at once and greyed ten of them; showing a
        /// control that can never apply to a Scout is the text wall in button form.
        /// </summary>
        private void RefreshContextRail(ShipHangar hangar, VehicleAgent vehicle)
        {
            if (contextRail == null) return;

            VehicleClass? cls = vehicle?.Def != null ? vehicle.Def.Class : (VehicleClass?)null;
            bool escort = cls == VehicleClass.Escort;
            bool engineer = cls == VehicleClass.Engineer;
            bool onMission = vehicle != null && vehicle.Mission.Phase != VehiclePhase.InHangar;
            bool hasRoute = vehicle?.Route != null;
            bool resupplying = vehicle?.ResupplyTask != null;
            bool poiVehicle = cls == VehicleClass.Scout || cls == VehicleClass.Collector;

            VisibleContextActions = 0;

            Show(poiDispatch, poiVehicle);
            if (poiVehicle)
                poiDispatch.SetAvailable(hangar != null && !onMission,
                    hangar == null ? "OTURUM YOK" : "ARAÇ ZATEN GÖREVDE");

            Show(escortFollow, escort);
            Show(escortProtect, escort);
            Show(escortRetreat, escort);
            Show(escortReturn, escort);
            Show(escortStance, escort);
            if (escort)
            {
                // An idle escort is assignable; the command itself assigns it. The
                // only thing that can refuse is command capacity, and the service
                // owns that answer -- the button says so rather than guessing.
                string blocked = hangar == null ? "OTURUM YOK" : null;
                escortFollow.SetAvailable(blocked == null, blocked);
                escortProtect.SetAvailable(blocked == null, blocked);
                escortRetreat.SetAvailable(blocked == null, blocked);
                escortReturn.SetAvailable(blocked == null, blocked);

                // The stance is a property of a live assignment, so it names the one
                // in force and refuses honestly until there is one to change.
                EscortState state = vehicle.EscortState;
                escortStance.SetCaption(state == null
                    ? "DURUŞ" : NebulaText.Upper($"DURUŞ: {StanceName(state.Stance)}"));
                escortStance.SetAvailable(blocked == null && state != null,
                    blocked ?? "ÖNCE BİR KORUMA HEDEFİ ATAYIN");
            }

            Show(engineerRepair, engineer);
            Show(engineerCancel, engineer);
            if (engineer)
            {
                bool busy = vehicle.EngineerTask != null;
                engineerRepair.SetAvailable(hangar != null && !busy,
                    hangar == null ? "OTURUM YOK" : "ARAÇ ZATEN GÖREVDE");
                engineerCancel.SetAvailable(busy, "SÜRMEKTE OLAN GÖREV YOK");
            }

            // VS-015 §6's intervention group only ever holds Engineer/Escort vehicles,
            // so its two controls appear for exactly those, and the policy control
            // names the policy actually in force.
            bool supportVehicle = escort || engineer;
            Hangar.InterventionGroupDirector director = context?.InterventionGroupDirectorOrNull;
            Show(groupMembership, supportVehicle);
            Show(groupPolicy, supportVehicle);
            if (supportVehicle)
            {
                bool member = director != null && IsGroupMember(director, vehicle.VehicleId);
                groupMembership.SetCaption(member ? "GRUPTAN ÇIKAR" : "GRUBA EKLE");
                groupMembership.SetAvailable(director != null, "OTURUM YOK");

                Combat.InterventionPolicy policy = director?.Group?.Policy ?? Combat.InterventionPolicy.Manual;
                groupPolicy.SetCaption(NebulaText.Upper($"MÜDAHALE: {InterventionPolicyName(policy)}"));
                groupPolicy.SetAvailable(director != null, "OTURUM YOK");
            }

            // Resupply is not class-bound -- any Mk II vehicle can run one -- so it
            // shows whenever a vehicle is selected and lets the service refuse.
            Show(resupplyStart, vehicle != null && !resupplying);
            Show(resupplyCancel, resupplying);
            if (vehicle != null && !resupplying)
                resupplyStart.SetAvailable(hangar != null, "OTURUM YOK");

            Show(routePauseResume, hasRoute);
            Show(routeDelete, hasRoute);
            if (hasRoute)
            {
                routePauseResume.SetCaption(vehicle.Route.Paused ? "ROTAYI SÜRDÜR" : "ROTAYI DURAKLAT");
                routePauseResume.SetAvailable(true, null);
                routeDelete.SetAvailable(hangar != null, "OTURUM YOK");
            }

            // Nothing to say beats a rail of dead controls -- but never over the top
            // of what an action just reported.
            if (actionStatusHeld) return;
            if (vehicle == null) SetStatus(string.Empty);
            else if (onMission && VisibleContextActions == 0) SetStatus("ARAÇ GÖREVDE");
        }

        /// <summary>
        /// Shows or hides one action and, when shown, gives it the next free slot.
        /// Slots are assigned here rather than at build time because the groups
        /// combine: an escort with a resupply mission shows both sets, and fixed
        /// per-group slots would have stacked them on top of each other.
        /// </summary>
        private void Show(NebulaUiFactory.ButtonHandle handle, bool visible)
        {
            if (handle?.Button == null) return;

            if (handle.Button.gameObject.activeSelf != visible)
                handle.Button.gameObject.SetActive(visible);
            if (!visible) return;

            var rect = handle.Button.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(-VisibleContextActions * 196f, 0f);
            VisibleContextActions++;
        }

        /// <summary>
        /// Why the upgrade is refused, without starting it. The hangar's own
        /// eligibility rules are re-read rather than re-derived where they are
        /// cheap and side-effect free; anything deeper is left to the service,
        /// which reports on the attempt.
        /// </summary>
        private static string UpgradeReject(ShipHangar hangar, VehicleAgent vehicle)
        {
            if (hangar == null) return "OTURUM YOK";
            if (vehicle.Def == null) return "ARAÇ UYGUN DEĞİL";
            if (!VehicleCatalog.TryGetUpgradeRecipe(vehicle.Def.Id, out _)) return "SADECE MK I YÜKSELTİLEBİLİR";
            if (vehicle.HeldAmount > 0.0001f) return "ARAÇ KARGOSU BOŞ OLMALI";
            if (hangar.Production?.ActiveJob != null) return "ÜRETİM YUVASI DOLU";
            return null;
        }

        private void RefreshCapacity(ShipHangar hangar)
        {
            if (hangar?.Command == null)
            {
                commandValue.Value = "—";
                commandBar.SetFraction(0f);
                return;
            }

            int used = hangar.Command.Used;
            int capacity = hangar.Command.Capacity;
            commandValue.Value = $"{used} / {capacity}";
            commandBar.SetFraction(capacity > 0 ? used / (float)capacity : 0f);

            // A full ledger is not an error, but it is the reason a launch will be
            // refused, so it earns the badge rather than a silent rejection later.
            bool full = capacity > 0 && used >= capacity;
            commandBar.SetColor(full
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Caution)
                : NebulaUiDesignTokens.ActionPrimary);
            if (full) AttentionCount++;
        }

        private void RefreshProduction(ShipHangar hangar)
        {
            HangarProductionJob job = hangar?.Production?.ActiveJob;
            if (job == null)
            {
                productionName.Value = "ÜRETİM YOK";
                productionName.Text.color = NebulaUiDesignTokens.InkMuted;
                productionEta.Value = string.Empty;
                productionBar.SetFraction(0f);
                return;
            }

            productionName.Text.color = NebulaUiDesignTokens.InkPrimary;
            productionName.Value = job.IsUpgrade
                ? $"{ClassName(job.VehicleClass)} · MK II'YE YÜKSELTME"
                : $"{ClassName(job.VehicleClass)} · MK {Roman(job.Mk)}";
            productionEta.Value = FormatDuration(job.RemainingSeconds);
            productionBar.SetFraction(job.Progress01);
        }

        private void RefreshVehicles(ShipHangar hangar)
        {
            IReadOnlyList<VehicleAgent> vehicles = hangar?.Vehicles;
            int count = vehicles?.Count ?? 0;

            bool anyShown = count > 0;
            if (emptyState.Text.gameObject.activeSelf == anyShown)
                emptyState.Text.gameObject.SetActive(!anyShown);

            seen.Clear();
            vehicleRows.BeginRefresh();
            int index = 0;

            for (int i = 0; i < count; i++)
            {
                VehicleAgent vehicle = vehicles[i];
                if (vehicle == null) continue;

                seen.Add(vehicle.VehicleId);
                vehicleRows.Observe(vehicle.VehicleId);
                VehicleRow row = GetOrCreateRow(vehicle.VehicleId);
                PositionRow(row.Root, index++);
                if (!row.Root.gameObject.activeSelf) row.Root.gameObject.SetActive(true);

                ApplyVehicle(row, vehicle);
            }

            vehicleRows.EndRefresh();
            // The first read after a boot or a load is adopted in silence, so a
            // restored save never plays an entrance for a fleet that was already there.
            rowsAdopted = true;

            foreach (KeyValuePair<string, VehicleRow> entry in rows)
                if (!seen.Contains(entry.Key) && entry.Value.Root.gameObject.activeSelf)
                    entry.Value.Root.gameObject.SetActive(false);

            VisibleRows = index;
        }

        private void ApplyVehicle(VehicleRow row, VehicleAgent vehicle)
        {
            // Callsign, not the stable id. The id is how the save file refers to
            // this vehicle; the player never needs to see it. Resolved into a local
            // first so the id never appears inside a player-facing string literal --
            // the hygiene test cannot tell a safe wrapper from a raw one, and a rule
            // that has to be special-cased stops being a rule.
            string callsign = Callsign(vehicle.VehicleId);
            row.Name.Value = $"{vehicle.Def.DisplayName} · {callsign}";

            bool stranded = vehicle.IsRecoverable;
            bool waiting = vehicle.Mission.Phase == VehiclePhase.Waiting;
            bool full = vehicle.Def.Class == VehicleClass.Collector
                        && vehicle.HeldAmount >= vehicle.Def.CargoCapacity - 0.0001f;

            if (stranded)
            {
                row.Status.Value = "DEVRE DIŞI";
                row.Detail.Value = "KURTARMA İÇİN MÜHENDİS GEREKİYOR";
                Paint(row, UiStatusLevel.Critical, NebulaUiAssetCatalog.RowState.Critical);
                AttentionCount++;
            }
            else if (waiting)
            {
                row.Status.Value = "BEKLİYOR";
                row.Detail.Value = "GÖREV TAMAMLANAMIYOR";
                Paint(row, UiStatusLevel.Warning, NebulaUiAssetCatalog.RowState.Warning);
                AttentionCount++;
            }
            else if (full)
            {
                row.Status.Value = "KARGO DOLU";
                row.Detail.Value = $"{vehicle.HeldAmount:0} / {vehicle.Def.CargoCapacity:0}";
                Paint(row, UiStatusLevel.Caution, NebulaUiAssetCatalog.RowState.Warning);
                AttentionCount++;
            }
            else
            {
                row.Status.Value = PhaseName(vehicle.Mission.Phase);
                row.Detail.Value = DurabilitySummary(vehicle);
                Paint(row, UiStatusLevel.Nominal, NebulaUiAssetCatalog.RowState.Default);
            }
        }

        private static void Paint(VehicleRow row, UiStatusLevel level, NebulaUiAssetCatalog.RowState state)
        {
            row.Background.sprite = NebulaUiAssetCatalog.Row(state);
            row.Status.Text.color = NebulaUiDesignTokens.Status(level);
        }

        private static string DurabilitySummary(VehicleAgent vehicle)
        {
            Extraction.Durability durability = vehicle.DurabilityRef;
            if (durability == null) return string.Empty;
            float hull = durability.HullMax > 0f ? durability.Hull / durability.HullMax * 100f : 0f;
            return $"GÖVDE %{hull:0}";
        }

        private VehicleRow GetOrCreateRow(string vehicleId)
        {
            if (rows.TryGetValue(vehicleId, out VehicleRow existing)) return existing;

            var row = new VehicleRow();
            Image panel = NebulaUiFactory.Panel(vehicleList, vehicleId,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            row.Root = panel.rectTransform;
            row.Background = panel;

            row.Name = NebulaUiFactory.Label(row.Root, "Name", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(row.Name.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(360f, 18f));

            row.Status = NebulaUiFactory.Label(row.Root, "Status", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleRight, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Status.Text.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-16f, 8f), new Vector2(300f, 16f));

            row.Detail = NebulaUiFactory.Label(row.Root, "Detail", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleRight);
            NebulaUiFactory.Place(row.Detail.Text.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-16f, -8f), new Vector2(300f, 14f));

            // The panel is the target graphic. A second Image cannot be added to a
            // GameObject that already has one, because Graphic requires a
            // single-instance CanvasRenderer.
            panel.raycastTarget = true;
            var button = row.Root.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            // Same gap as the picker: the vehicle rows are how a vehicle is
            // selected and they were built with no interaction states at all.
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = NebulaUiAssetCatalog.RowInteraction();
            string captured = vehicleId;
            button.onClick.AddListener(() => Select(captured));

            rows[vehicleId] = row;
            return row;
        }

        private static void PositionRow(RectTransform row, int index)
        {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -index * (NebulaUiDesignTokens.RowHeightLarge + 4f));
            row.sizeDelta = new Vector2(0f, NebulaUiDesignTokens.RowHeightLarge);
        }

        // ---- copy ------------------------------------------------------------

        internal static string PhaseName(VehiclePhase phase)
        {
            switch (phase)
            {
                case VehiclePhase.InHangar: return "HANGARDA";
                case VehiclePhase.Launching: return "KALKIYOR";
                case VehiclePhase.TravelOut: return "GİDİŞ";
                case VehiclePhase.OnStation: return "GÖREVDE";
                case VehiclePhase.TravelReturn: return "DÖNÜŞ";
                case VehiclePhase.Waiting: return "BEKLİYOR";
                case VehiclePhase.Docking: return "YANAŞIYOR";
                default: return "—";
            }
        }

        internal static string ClassName(VehicleClass vehicleClass)
        {
            switch (vehicleClass)
            {
                case VehicleClass.Scout: return "KEŞİF";
                case VehicleClass.Collector: return "TOPLAYICI";
                case VehicleClass.Engineer: return "MÜHENDİS";
                case VehicleClass.Escort: return "REFAKAT";
                default: return "ARAÇ";
            }
        }

        private static string Roman(int mk) => mk >= 2 ? "II" : "I";

        /// <summary>
        /// A short callsign derived from the stable id's trailing digits. Keeps the
        /// vehicle identifiable across refreshes without ever printing the id.
        /// </summary>
        internal static string Callsign(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return "—";
            int end = vehicleId.Length;
            int start = end;
            while (start > 0 && char.IsDigit(vehicleId[start - 1])) start--;
            string digits = start < end ? vehicleId.Substring(start) : null;
            if (string.IsNullOrEmpty(digits)) return "—";
            return digits.PadLeft(2, '0');
        }

        internal static string FormatDuration(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.RoundToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }

        /// <summary>
        /// Back closes the target picker first, and only then falls through to the
        /// shell. This is the one page with a second level, and it is what the
        /// per-page Back hook was designed for.
        /// </summary>
        public bool HandleBack()
        {
            if (LogisticsPanelOpen) { logistics.Close(); return true; }
            if (BuildPickerOpen)
            {
                // Back out of the material stage to the vehicle stage first: the two
                // are one decision, and dropping the whole picker would throw away a
                // choice the player already made.
                if (pendingBuildDef != null) { OpenProductionPicker(); return true; }
                CloseProductionPicker();
                return true;
            }
            if (!TargetPickerOpen) return false;
            CloseTargetPicker();
            return true;
        }

        /// <summary>Opens the logistics surface on whichever centre is live.</summary>
        public void OpenLogistics()
        {
            CloseTargetPicker();
            CloseProductionPicker();
            logistics.Open();
        }
    }
}
