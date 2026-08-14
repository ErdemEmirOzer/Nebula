using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.UI.Controls;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter B — Keşif ve Tarama. Curriculum steps B-01…B-11 (global 14–24).
    /// <para>
    /// The knowledge ladders are deliberately kept apart, because they are two
    /// different ladders: B-04/B-05/B-07/B-08 teach the POI ladder
    /// (<see cref="PoiKnowledgeLevel"/>: Signal → Located → Identified → Active →
    /// Resolved), while B-06 teaches the system ladder
    /// (<see cref="SystemKnowledgeLevel"/>: Unknown → Signal → Located → Visited,
    /// with no "Identified" in it at all).
    /// </para>
    /// </summary>
    public static class ChapterBSteps
    {
        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return SwitchPlanetTarget();
            yield return ScanThePlanet();
            yield return ReadWhatTheScanRevealed();
            yield return SignalBecomesLocated();
            yield return IdentifyWhatYouLocated();
            yield return WhyTheUnknownStaysUnnamed();
            yield return StartTheAnomalyWork();
            yield return FinishTheAnomaly();
            yield return SalvageAWreck();
            yield return ChooseTheFacilityOutcome();
            yield return AttentionCardsAndTheLastKnownLink();
        }

        // ---- B-01 -------------------------------------------------------------

        private static TutorialStepDefinition SwitchPlanetTarget() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-01", "Hedef Gezegeni Değiştir",
            "İkinci gezegene doğru uç; hedefin ona geçtiğini alt çubukta gör.",
            "Yakınlık hedeflemesinin devrini gör.",
            () =>
            {
                string secondWorldId = null;
                bool armed = false;

                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        ShipExtraction extraction = TutorialAbk.Extraction(ctx);
                        if (secondWorldId == null || extraction == null) return false;
                        PlanetIdentity target = extraction.TargetPlanet;
                        // Armed only once the world has really reported a target that
                        // is not B. A stale handover left over from an earlier step
                        // must never be credited as this one's approach.
                        if (target == null || target.WorldId != secondWorldId) { armed = true; return false; }
                        return armed;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        PlanetIdentity first = TutorialAbk.EnsurePlanet(ctx, 0, out _);
                        PlanetIdentity second = TutorialAbk.EnsurePlanet(ctx, 1, out _);
                        if (first == null || second == null) return;
                        secondWorldId = second.WorldId;
                        // Parked inside the FIRST planet's interest sphere, so the step
                        // opens with the target already on A and the handover is the
                        // only thing left to do.
                        TutorialAbk.PlaceShipAtCentreDistance(ctx, first, first.Body.Radius * 2f);
                    },
                    objectives: (ctx, report) =>
                    {
                        ShipExtraction extraction = TutorialAbk.Extraction(ctx);
                        report.SetObjective(0, extraction?.TargetPlanet != null);
                    });
            },
            assets: new[] { TutorialAbk.PlayerShip, TutorialAbk.MetalPlanet, TutorialAbk.BiomassPlanet });

        // ---- B-02 -------------------------------------------------------------

        private static TutorialStepDefinition ScanThePlanet() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-02", "Gezegeni Tara",
            "Gezegene yaklaş ve tarama tamamlanana kadar tarama tuşunu basılı tut.",
            "Gezegen taramasını tamamla.",
            () =>
            {
                PlanetIdentity planet = null;

                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        if (planet == null) return false;
                        PlanetProfile profile = ExtractionState.GetProfile(planet);
                        return profile != null && profile.Scanned;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        // Every path out of this step can reach ProgressionService.Award
                        // through ExtractionState.CompletePlanetScan, which writes a
                        // process-wide static ledger nothing else puts back.
                        TutorialAbk.BracketProgression(ctx);

                        planet = TutorialAbk.EnsurePlanet(ctx, 0, out _);
                        if (planet == null) return;

                        PlanetProfile profile = ExtractionState.GetProfile(planet);
                        if (profile != null)
                        {
                            bool wasScanned = profile.Scanned;
                            profile.Scanned = false;
                            ctx.Scope.OnCleanup(() => profile.Scanned = wasScanned);
                        }

                        ShipExtraction extraction = TutorialAbk.Extraction(ctx);
                        extraction?.ApplyScannerTier(0);
                        // Inside the interest radius (3R) but outside the T0 scan
                        // radius, so the first objective is genuinely open.
                        TutorialAbk.PlaceShipAtCentreDistance(ctx, planet, planet.Body.Radius * 2.8f);
                    },
                    reject: ctx => TutorialAbk.ScanRejectTr(TutorialAbk.Extraction(ctx)),
                    objectives: (ctx, report) =>
                    {
                        ShipExtraction extraction = TutorialAbk.Extraction(ctx);
                        if (extraction?.TargetPlanet == null) { report.SetObjective(0, false); return; }
                        float limit = extraction.TargetPlanet.Body.Radius * extraction.ScannerProximityR;
                        report.SetObjective(0, extraction.Altitude(extraction.TargetPlanet) <= limit);
                        report.SetObjective(1, extraction.TargetProfile != null && extraction.TargetProfile.Scanned);
                    });
            },
            checklistTr: new[] { "Tarama menziline gir", "Taramayı tamamla" },
            assets: TutorialAbk.ShipAndMetalPlanet,
            keyHint: () => NebulaControlBindings.KeyLabel(ShipExtraction.ScanKey));

        // ---- B-03 -------------------------------------------------------------

        private static TutorialStepDefinition ReadWhatTheScanRevealed() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-03", "Taramanın Söyledikleri",
            "Taranmış gezegenin yanında kal; alt çubuk artık kaynak ailesini ve nadirliğini gösteriyor.",
            "Tarama sonrası bağlam çubuğunu oku.",
            () =>
            {
                NebulaHudContextBar bar = null;

                return TutorialSteps.Poll(
                    // The scan branch offers a primary and no secondaries; the
                    // post-scan branch always appends Orbit Stabilize. Two visible
                    // actions is therefore state, not a string comparison.
                    // It is also state the step's own Prepare produces: the ship is
                    // parked at 2 planet radii, inside the 3-radius interest range,
                    // so NebulaExtractionContextDriver repopulates the bar on its
                    // own 0.25s cadence with the player doing nothing at all. That
                    // is why this is an InformationAcknowledgement below: the
                    // predicate is honest evidence that there is something to read,
                    // and it is the confirmation, not the predicate, that ends the
                    // step.
                    complete: _ => bar != null && bar.VisibleActionCount >= 2,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        PlanetIdentity planet = TutorialAbk.EnsurePlanet(ctx, 0, out _);
                        if (planet == null) return;

                        TutorialAbk.MarkScannedForTheLesson(ctx, planet);
                        TutorialAbk.PlaceShipAtCentreDistance(ctx, planet, planet.Body.Radius * 2f);

                        NebulaUiShell shell = TutorialAbk.NewShell(ctx);
                        bar = NebulaHudContextBar.Create(shell);
                        NebulaHudContextBar owned = bar;
                        NebulaExtractionContextDriver.Create(
                            bar, TutorialAbk.Extraction(ctx),
                            TutorialAbk.Ship(ctx).GetComponent<ShipPlanetEvacuation>());
                        bar.Clear();
                        ctx.Scope.OnCleanup(() => { if (owned != null) owned.Clear(); });
                    });
            },
            // A reading step has to say what to read. Asking the player to confirm
            // they have understood, without naming the two things on the bar the
            // lesson is about, makes the confirmation a formality — which is the
            // failure the acknowledgement gate exists to prevent, arriving by the
            // other door.
            checklistTr: new[]
            {
                "Kaynak ailesini oku",
                "Nadirlik derecesini oku",
            },
            assets: TutorialAbk.ShipAndMetalPlanet,
            // There is nothing for the player to do here and nothing to invent: the
            // context bar is a readout, the ship is already in range, and the two
            // actions it offers are the lesson. So the step ends on the player
            // saying they have read it, and never on the bar simply being drawn.
            kind: TutorialStepKind.InformationAcknowledgement);

        // ---- B-04 -------------------------------------------------------------

        private static TutorialStepDefinition SignalBecomesLocated() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-04", "Sinyalden Konuma",
            "Sistemde kal; gemi tarayıcısı sinyali kendiliğinden konumlandırana kadar bekle.",
            "Sinyalin konumlanmasını izle.",
            () =>
            {
                PoiObject poi = null;

                return TutorialSteps.Poll(
                    complete: _ => poi != null && poi.State != null
                        && poi.State.Level == PoiKnowledgeLevel.Located,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        poi = TutorialPoi.Spawn(ctx, PoiType.Anomaly, PoiRisk.Safe,
                            PoiKnowledgeLevel.Signal, distanceFromShip: 1200f);
                    },
                    reject: _ => poi?.State != null ? TutorialAbk.PoiPauseTr(poi.State.PauseReason) : null);
            },
            assets: new[] { TutorialAbk.PlayerShip, TutorialAbk.Anomaly });

        // ---- B-05 -------------------------------------------------------------

        private static TutorialStepDefinition IdentifyWhatYouLocated() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-05", "Tanımla",
            "Konumu belirlenen işarete 2500 birimden yakına git ve tanımlanmasını bekle.",
            "Konumlanmış işareti tanımla.",
            () =>
            {
                PoiObject poi = null;

                return TutorialSteps.Poll(
                    complete: _ => poi != null && poi.State != null
                        && poi.State.Level == PoiKnowledgeLevel.Identified,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        // Placed beyond PoiCatalog.IdentifyRange, so the identify timer
                        // opens paused on the real OutOfRange reason.
                        poi = TutorialPoi.Spawn(ctx, PoiType.Anomaly, PoiRisk.Safe,
                            PoiKnowledgeLevel.Located, distanceFromShip: PoiCatalog.IdentifyRange * 1.6f);
                    },
                    reject: _ => poi?.State != null ? TutorialAbk.PoiPauseTr(poi.State.PauseReason) : null);
            },
            assets: new[] { TutorialAbk.PlayerShip, TutorialAbk.Anomaly });

        // ---- B-06 -------------------------------------------------------------

        private static TutorialStepDefinition WhyTheUnknownStaysUnnamed() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-06", "Bilinmeyen Neden Gizli",
            "Keşif sayfasını aç ve tanımlanmamış sinyali seç; adının neden görünmediğini oku.",
            "Sistem bilgi merdivenini tanı.",
            () =>
            {
                NebulaDiscoveryPage page = null;
                SystemKnowledgeService knowledge = null;
                string signalSystemId = null;

                return TutorialSteps.Poll(
                    complete: _ => page != null && signalSystemId != null
                        && page.SelectedSystemId == signalSystemId
                        && knowledge != null
                        && knowledge.GetLevel(signalSystemId) == SystemKnowledgeLevel.Signal,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ExclusiveModal.ResetForSession();

                        NavigationDirector navigation = ctx.Gameplay.NavigationDirector;
                        knowledge = navigation.Knowledge;
                        signalSystemId = TutorialPoi.SeedSignalSystem(ctx, knowledge);

                        NebulaUiShell shell = TutorialAbk.NewShell(ctx);
                        NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
                        page = new NebulaDiscoveryPage(ctx.Gameplay);
                        menu.AddPage(page);
                        menu.Open(NebulaUiAssetCatalog.Page.Discovery);

                        NebulaParentMenuScreen owned = menu;
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (owned == null) return;
                            owned.Close();
                            TutorialAbk.DestroyNow(owned.gameObject);
                        });
                    },
                    // Already Turkish in production, and produced by production.
                    reject: _ => page?.AnalysisBlockedReason);
            },
            assets: TutorialAbk.PlayerShipOnly,
            keyHint: () => NebulaControlBindings.KeyLabel(NebulaParentMenuScreen.ProgressionKey));

        // ---- B-07 -------------------------------------------------------------

        private static TutorialStepDefinition StartTheAnomalyWork() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-07", "Anomali Çalışmasını Başlat",
            "Tanımlanmış anomalinin yanındayken çalışmayı başlat.",
            "Anomali çalışmasını başlat.",
            () =>
            {
                PoiObject poi = null;

                return TutorialSteps.Poll(
                    complete: _ => poi != null && poi.State != null
                        && poi.State.Level == PoiKnowledgeLevel.Active,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        poi = TutorialPoi.Spawn(ctx, PoiType.Anomaly, PoiRisk.Safe,
                            PoiKnowledgeLevel.Identified, distanceFromShip: PoiCatalog.IdentifyRange * 0.4f);

                        PoiObject owned = poi;
                        ShipPoi driver = TutorialAbk.PoiDriver(ctx);
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (owned?.State != null && owned.State.Level == PoiKnowledgeLevel.Active)
                                driver?.TryCancelActiveOperation();
                        });
                    },
                    reject: ctx =>
                    {
                        ShipPoi driver = TutorialAbk.PoiDriver(ctx);
                        if (driver == null || poi == null) return null;
                        if (driver.TargetPoi != poi) return "HEDEF UYGUN DEĞİL";
                        return poi.State != null && poi.State.Level != PoiKnowledgeLevel.Identified
                               && poi.State.Level != PoiKnowledgeLevel.Active
                            ? "HEDEF UYGUN DEĞİL"
                            : null;
                    });
            },
            assets: new[] { TutorialAbk.PlayerShip, TutorialAbk.Anomaly },
            keyHint: () => NebulaControlBindings.KeyLabel(ShipPoi.StartOperationKey));

        // ---- B-08 -------------------------------------------------------------

        private static TutorialStepDefinition FinishTheAnomaly() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-08", "Anomaliyi Çöz",
            "Anomalinin yanından ayrılma; çalışma bitince yeni bir sinyal açılacak.",
            "Anomaliyi çöz ve yeni sinyali gör.",
            () =>
            {
                PoiObject poi = null;
                SystemKnowledgeService knowledge = null;
                int signalsAtStart = 0;

                return TutorialSteps.Poll(
                    complete: _ => poi?.State != null
                        && poi.State.Level == PoiKnowledgeLevel.Resolved
                        && knowledge != null
                        && TutorialPoi.SignalCount(knowledge) > signalsAtStart,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        // AnomalyService.TryComplete awards 250 XP into the same static
                        // ledger, so this step runs inside the progression bracket too.
                        TutorialAbk.BracketProgression(ctx);

                        NavigationDirector navigation = ctx.Gameplay.NavigationDirector;
                        knowledge = navigation.Knowledge;
                        TutorialPoi.CaptureAndRestoreKnowledge(ctx, knowledge);
                        signalsAtStart = TutorialPoi.SignalCount(knowledge);

                        poi = TutorialPoi.Spawn(ctx, PoiType.Anomaly, PoiRisk.Safe,
                            PoiKnowledgeLevel.Identified, distanceFromShip: PoiCatalog.IdentifyRange * 0.4f);
                        // Opens Active: the player's job here is to stay, not to start.
                        poi?.State?.StartOperation(null);
                    },
                    reject: _ => poi?.State != null && poi.State.Level == PoiKnowledgeLevel.Active
                        ? TutorialAbk.PoiPauseTr(poi.State.PauseReason)
                        : null,
                    objectives: (_, report) =>
                    {
                        report.SetObjective(0, poi?.State != null && poi.State.Level == PoiKnowledgeLevel.Resolved);
                        report.SetObjective(1, knowledge != null && TutorialPoi.SignalCount(knowledge) > signalsAtStart);
                    });
            },
            checklistTr: new[] { "Çalışmayı tamamla", "Yeni sinyalin açıldığını gör" },
            assets: new[] { TutorialAbk.PlayerShip, TutorialAbk.Anomaly });

        // ---- B-09 -------------------------------------------------------------

        private static TutorialStepDefinition SalvageAWreck() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-09", "Enkazı Topla",
            "Tanımlanmış enkaza yaklaş ve kargona malzeme akmasını izle.",
            "Enkaz toplamayı öğren.",
            () =>
            {
                PoiObject poi = null;
                float remainingAtStart = 0f;
                string firstItemId = null;
                float storedAtStart = 0f;

                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        if (poi?.State == null || firstItemId == null) return false;
                        if (TutorialPoi.SalvageRemaining(poi.State) >= remainingAtStart) return false;
                        LogisticsCargo cargo = TutorialAbk.Logistics(ctx)?.Cargo;
                        return cargo != null && cargo.GetStored(firstItemId) > storedAtStart;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        // WreckService.OnIdentified awards 150 XP on the Identified
                        // transition; the bracket covers a step that ever drives it.
                        TutorialAbk.BracketProgression(ctx);

                        // Outside IdentifyRange, so nothing has been taken yet.
                        poi = TutorialPoi.Spawn(ctx, PoiType.Wreck, PoiRisk.Safe,
                            PoiKnowledgeLevel.Identified, distanceFromShip: PoiCatalog.IdentifyRange * 1.6f);
                        if (poi?.State == null) return;

                        List<SalvageNode> nodes = WreckSalvageGenerator.Generate(poi.Manifest);
                        poi.State.SetSalvageNodes(nodes);
                        remainingAtStart = TutorialPoi.SalvageRemaining(poi.State);
                        firstItemId = nodes.Count > 0 ? nodes[0].ItemId : null;

                        LogisticsCargo cargo = TutorialAbk.Logistics(ctx)?.Cargo;
                        storedAtStart = firstItemId != null && cargo != null ? cargo.GetStored(firstItemId) : 0f;
                        TutorialPoi.CaptureAndRestoreCargo(ctx, cargo);
                    },
                    reject: ctx =>
                    {
                        if (poi?.State == null) return null;
                        LogisticsCargo cargo = TutorialAbk.Logistics(ctx)?.Cargo;
                        if (cargo != null && cargo.AvailableVolume <= 0f)
                            return TutorialAbk.PoiPauseTr(PoiPauseReason.TargetCargoFull);
                        return TutorialAbk.WreckRejectTr(
                            WreckService.CheckCollect(poi.State, poi.Manifest.Risk, byCollector: false));
                    });
            },
            assets: new[] { TutorialAbk.PlayerShip, TutorialAbk.Wreck, TutorialAbk.SalvageNode });

        // ---- B-10 -------------------------------------------------------------

        private static TutorialStepDefinition ChooseTheFacilityOutcome() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-10", "Terk Edilmiş Tesis",
            "Tesiste sökümü ya da sensör rölesini seç — seçim geri alınamaz.",
            "Geri alınamaz tesis seçimini yap.",
            () =>
            {
                PoiObject poi = null;

                return TutorialSteps.Poll(
                    complete: _ => poi?.State != null && poi.State.Choice != FacilityChoice.None,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        poi = TutorialPoi.Spawn(ctx, PoiType.AbandonedFacility, PoiRisk.Safe,
                            PoiKnowledgeLevel.Identified, distanceFromShip: PoiCatalog.IdentifyRange * 0.4f);

                        // Both branches are deliberately affordable, so the choice is
                        // the player's and not the cargo's.
                        LogisticsCargo cargo = TutorialAbk.Logistics(ctx)?.Cargo;
                        TutorialPoi.CaptureAndRestoreCargo(ctx, cargo);
                        if (cargo != null)
                            foreach ((string itemId, float amount) in FacilityService.RelayCost())
                                cargo.LoadItem(itemId, amount);

                        PoiObject owned = poi;
                        ShipPoi driver = TutorialAbk.PoiDriver(ctx);
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (owned?.State != null && owned.State.Level == PoiKnowledgeLevel.Active)
                                driver?.TryCancelActiveOperation();
                        });
                    },
                    reject: ctx =>
                    {
                        if (poi?.State == null) return null;
                        LogisticsCargo cargo = TutorialAbk.Logistics(ctx)?.Cargo;
                        FacilityStartRejectReason dismantle = FacilityService.CheckStartDismantle(poi.State);
                        FacilityStartRejectReason relay = FacilityService.CheckStartRelay(poi.State, cargo);
                        if (dismantle == FacilityStartRejectReason.None || relay == FacilityStartRejectReason.None)
                            return null;
                        return TutorialPoi.FacilityRejectTr(dismantle != FacilityStartRejectReason.None ? dismantle : relay);
                    });
            },
            assets: new[] { TutorialAbk.PlayerShip, TutorialAbk.AbandonedFacility, TutorialAbk.SensorRelay },
            keyHint: () => NebulaControlBindings.KeyLabel(ShipPoi.StartOperationKey));

        // ---- B-11 -------------------------------------------------------------

        private static TutorialStepDefinition AttentionCardsAndTheLastKnownLink() => new TutorialStepDefinition(
            TutorialChapterId.Exploration, "B-11", "Dikkat Kartları ve Son Bilinen Veri",
            "Sağdaki iki dikkat kartını karşılaştır: bulunduğun sistem canlı, uzaktaki sistem \"SON BİLİNEN\".",
            "Canlı ve son bilinen veriyi ayırt et.",
            () =>
            {
                NebulaHudAlerts alerts = null;
                NavigationDirector navigation = null;
                string liveSystemId = null, lastKnownSystemId = null;

                return TutorialSteps.Poll(
                    // A reading. The predicate is the world genuinely showing both
                    // link states at once -- one card the real query resolved Live
                    // and one it resolved LastKnown, on the real HUD -- and that is
                    // true within a frame of Prepare finishing, because Prepare is
                    // what seeds both systems. It is evidence that there is
                    // something to read, not proof that anyone read it, which is
                    // why the step is an InformationAcknowledgement below and ends
                    // on the player's own confirmation.
                    complete: _ =>
                    {
                        if (alerts == null || liveSystemId == null || lastKnownSystemId == null) return false;
                        if (alerts.VisibleAttentionCards < 2) return false;
                        return TutorialPoi.CardIsLastKnown(liveSystemId, navigation) == false
                               && TutorialPoi.CardIsLastKnown(lastKnownSystemId, navigation) == true;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        navigation = ctx.Gameplay.NavigationDirector;
                        TutorialPoi.SeedLiveAndLastKnownSystems(
                            ctx, navigation, out liveSystemId, out lastKnownSystemId);

                        NebulaUiShell shell = TutorialAbk.NewShell(ctx);
                        alerts = NebulaHudAlerts.Create(shell, TutorialAbk.Combat(ctx),
                            TutorialAbk.Extraction(ctx), navigation);
                        NebulaHudAlerts owned = alerts;
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (owned != null) TutorialAbk.DestroyNow(owned.gameObject);
                        });
                    },
                    // Guidance, not a refusal: the game refuses nothing here. It
                    // simply cannot derive a link state for a ship inside no system,
                    // so there is no card to read at all.
                    reject: _ => navigation != null && navigation.CurrentSystemId == null
                        ? "KART OKUNAMIYOR — BİR SİSTEMİN İÇİNDE OLMALISIN"
                        : null,
                    objectives: (_, report) =>
                    {
                        report.SetObjective(0, TutorialPoi.CardIsLastKnown(liveSystemId, navigation) == false);
                        report.SetObjective(1, TutorialPoi.CardIsLastKnown(lastKnownSystemId, navigation) == true);
                    });
            },
            checklistTr: new[]
            {
                "Bulunduğun sistemin canlı kartını gör",
                "Uzaktaki sistemin SON BİLİNEN kartını gör"
            },
            assets: TutorialAbk.PlayerShipOnly,
            // Attention cards carry no interaction at all -- there is no click, no
            // key and no state the player can move -- and flying out of a system to
            // make a card go stale is not available either: leaving one puts the
            // ship inside no system, so no link state can be derived at all (see
            // LeavingASystemPutsTheShipInsideNoSystemAtAll). Reading really is the
            // whole action, so confirming the reading is the honest gate.
            kind: TutorialStepKind.InformationAcknowledgement);
    }

    /// <summary>
    /// Chapter B's own scaffolding: real POIs built from real manifests, and the
    /// captures that put the world back the way the player left it.
    /// </summary>
    internal static class TutorialPoi
    {
        private static int spawnCounter;

        /// <summary>
        /// One real <see cref="PoiObject"/>, from a real <see cref="PoiManifest"/>,
        /// resolved through the project's own visual catalog by
        /// <c>PoiObject.BuildVisuals</c>.
        /// <para>
        /// The hangar reference is deliberately null: no chapter-B step ever
        /// dispatches a Scout or a Collector, and the whole lesson is the ship's
        /// own scanner. Passing one would let a stray vehicle take over the
        /// timers the player is being asked to watch.
        /// </para>
        /// </summary>
        internal static PoiObject Spawn(TutorialStepContext context, PoiType type, PoiRisk risk,
            PoiKnowledgeLevel startLevel, float distanceFromShip)
        {
            ShipController ship = TutorialAbk.Ship(context);
            var anchor = new GameObject("Tutorial POI Anchor");
            anchor.transform.position = ship.transform.position;
            context.Scope.Track(anchor);

            string poiId = $"tutorial.poi.{type}.{++spawnCounter}";
            string systemId = StarSystemRegistry.StartSystemId ?? "tutorial.system";
            var manifest = new PoiManifest(poiId, systemId, 0, type, risk,
                ship.transform.forward * distanceFromShip, WreckSize.Small,
                rewardSeed: 1234ul, visualSeed: 5678ul);

            var state = new PoiState(poiId);
            if (startLevel != PoiKnowledgeLevel.Signal)
            {
                state.RestoreCore(startLevel,
                    startLevel >= PoiKnowledgeLevel.Located ? PoiCatalog.ScannerT1LocateSeconds : 0f,
                    startLevel >= PoiKnowledgeLevel.Identified ? PoiCatalog.IdentifySeconds : 0f,
                    0f, PoiPauseReason.None, null);
            }

            PoiObject poi = PoiObject.Create(manifest, anchor.transform, state,
                ship, null, context.Gameplay.NavigationDirector.Knowledge,
                context.Gameplay.NavigationDirector.FrontierSignal);
            if (poi != null) context.Scope.Track(poi.gameObject);
            return poi;
        }

        internal static float SalvageRemaining(PoiState state)
        {
            float total = 0f;
            for (int i = 0; i < state.SalvageNodes.Count; i++) total += state.SalvageNodes[i].Remaining;
            return total;
        }

        internal static int SignalCount(SystemKnowledgeService knowledge)
        {
            int count = 0;
            foreach (KeyValuePair<string, SystemKnowledgeLevel> entry in knowledge.All)
                if (entry.Value == SystemKnowledgeLevel.Signal) count++;
            return count;
        }

        internal static void CaptureAndRestoreKnowledge(TutorialStepContext context, SystemKnowledgeService knowledge)
        {
            var snapshot = new List<KeyValuePair<string, SystemKnowledgeLevel>>(knowledge.All);
            context.Scope.OnCleanup(() =>
            {
                knowledge.Clear();
                for (int i = 0; i < snapshot.Count; i++) knowledge.ForceLevel(snapshot[i].Key, snapshot[i].Value);
            });
        }

        internal static void CaptureAndRestoreCargo(TutorialStepContext context, LogisticsCargo cargo)
        {
            if (cargo == null) return;
            var snapshot = new List<KeyValuePair<string, float>>(cargo.Items);
            context.Scope.OnCleanup(() =>
            {
                cargo.Clear();
                for (int i = 0; i < snapshot.Count; i++) cargo.LoadItem(snapshot[i].Key, snapshot[i].Value);
            });
        }

        /// <summary>
        /// Raises one real, otherwise-unknown system to Signal so the Discovery
        /// page has something whose name is genuinely withheld. Restored at
        /// cleanup: <c>SystemKnowledgeService.Raise</c> raises no event and
        /// requests no save, but it does outlive the step if nobody puts it back.
        /// </summary>
        internal static string SeedSignalSystem(TutorialStepContext context, SystemKnowledgeService knowledge)
        {
            CaptureAndRestoreKnowledge(context, knowledge);
            string systemId = FirstSystemOtherThan(context, StarSystemRegistry.StartSystemId);
            if (systemId == null) return null;
            knowledge.ForceLevel(systemId, SystemKnowledgeLevel.Signal);
            return systemId;
        }

        /// <summary>
        /// Installs the two link states B-11 asks the player to compare, side by
        /// side and with no travel: the system the ship is physically in, which the
        /// real query resolves <b>Live</b>, and a second Visited system it is not
        /// in, which the same query resolves <b>LastKnown</b>.
        /// <para>
        /// Both cards are real. Each needs a genuine Critical condition, and each
        /// gets it the way the game itself would for a system in that state: the
        /// near one from live <c>ExtractionState</c> dominance, because the ship is
        /// there and the system is Full; the distant one from its persisted
        /// <see cref="SystemDelta"/>, because it is not materialized — which is also
        /// what makes its link LastKnown rather than Disconnected.
        /// </para>
        /// <para>
        /// Seeds nothing at all, and reports false, when the ship is inside no
        /// system. That is not tidiness: <c>NebulaHudAlerts.RefreshAttention</c>
        /// feeds every Visited system straight into
        /// <c>AttentionQuery.TopThreeCards</c> with the ship's current system id,
        /// and that call throws on a null one. Raising anything to Visited from here
        /// while the ship sat in interstellar space would arm a crash inside the
        /// live HUD's own Update, every frame, from state this step installed.
        /// </para>
        /// </summary>
        internal static bool SeedLiveAndLastKnownSystems(TutorialStepContext context,
            NavigationDirector navigation, out string liveSystemId, out string lastKnownSystemId)
        {
            liveSystemId = null;
            lastKnownSystemId = null;

            string here = navigation.CurrentSystemId;
            if (here == null) return false;

            string distant = FirstSystemOtherThan(context, here);
            if (distant == null) return false;

            SystemKnowledgeService knowledge = navigation.Knowledge;
            CaptureAndRestoreKnowledge(context, knowledge);
            // Only a Visited system is an attention candidate at all.
            knowledge.ForceLevel(here, SystemKnowledgeLevel.Visited);
            knowledge.ForceLevel(distant, SystemKnowledgeLevel.Visited);

            if (!SeedContestedLive(context, here)) return false;
            if (!SeedContestedSnapshot(context, distant)) return false;

            liveSystemId = here;
            lastKnownSystemId = distant;
            return true;
        }

        /// <summary>The near system's Critical condition, in live state, as a Full system's card is built from.</summary>
        private static bool SeedContestedLive(TutorialStepContext context, string systemId)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null || manifest.Planets.Count == 0) return false;

            PlanetDominance dominance = ExtractionState.GetDominance(manifest.Planets[0].PlanetId);
            DominanceState wasState = dominance.State;
            float wasStabilize = dominance.StabilizeTimer, wasRecovery = dominance.RecoveryTimer;
            bool wasAbandoned = dominance.Abandoned;
            dominance.Restore(DominanceState.Contested, 0f, 0f);
            context.Scope.OnCleanup(() => dominance.Restore(wasState, wasStabilize, wasRecovery, wasAbandoned));

            // The snapshot as well, so the card still resolves if this system is not
            // Full after all -- AttentionQuery reads one source or the other, never both.
            SeedContestedSnapshot(context, systemId);
            return true;
        }

        /// <summary>The distant system's Critical condition, in its persisted snapshot — the only source a non-materialized system has.</summary>
        private static bool SeedContestedSnapshot(TutorialStepContext context, string systemId)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            WorldDeltaRepository deltas = StarSystemRegistry.Deltas;
            if (manifest == null || manifest.Planets.Count == 0 || deltas == null) return false;

            SystemDelta delta = deltas.GetOrCreate(systemId);
            var added = new DominanceDto
            {
                worldId = manifest.Planets[0].PlanetId,
                state = (int)DominanceState.Contested,
                stabilizeTimer = 0f,
                recoveryTimer = 0f
            };
            delta.dominance.Add(added);
            context.Scope.OnCleanup(() =>
            {
                delta.dominance.Remove(added);
                // A delta this step created and then emptied is residue; the
                // repository's own prune is what decides whether it was already
                // carrying something real.
                deltas.PruneIfEmpty(systemId);
            });
            return true;
        }

        /// <summary>
        /// The real derived link state of the watched system's card, or null when
        /// the question cannot be asked at all. The <c>" · SON BİLİNEN"</c> suffix
        /// is what the player reads; this is what the step observes.
        /// <para>
        /// Null is returned, rather than an answer, whenever the ship is not inside
        /// any system — and the reason is the step's, not a crash's.
        /// <c>SystemStatusQuery.Link</c> now names interstellar space honestly: with
        /// no local Command to originate a relay from, <i>nothing</i> can be Live,
        /// so every card the player can see resolves last-known at once. That is a
        /// correct answer to a different question. B-11 teaches the <i>contrast</i>
        /// between a live card and a stale one, and a state where both cards read
        /// the same has no contrast in it, so the step must say "not observable"
        /// rather than count two last-known cards as the lesson.
        /// </para>
        /// </summary>
        internal static bool? CardIsLastKnown(string systemId, NavigationDirector navigation)
        {
            if (systemId == null || navigation == null) return null;
            if (navigation.CurrentSystemId == null) return null;

            List<AttentionCard> cards = AttentionQuery.TopThreeCards(
                new[] { systemId }, navigation.CurrentSystemId, navigation.Knowledge);
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].SystemId == systemId) return cards[i].IsLastKnown;
            return null;
        }

        /// <summary>
        /// <c>ShipPoi.RejectText</c> is private, so the same real enum is mapped
        /// here to the same words production shows.
        /// </summary>
        internal static string FacilityRejectTr(FacilityStartRejectReason reason) => reason switch
        {
            FacilityStartRejectReason.None => null,
            FacilityStartRejectReason.NotIdentified => "ÖNCE TANIMLANMALI",
            FacilityStartRejectReason.ChoiceAlreadyLocked => "SEÇİM ZATEN KİLİTLİ",
            FacilityStartRejectReason.InsufficientCargo => "YETERSİZ KARGO",
            _ => "BAŞARISIZ"
        };

        /// <summary>
        /// A real nearby system that is not <paramref name="excluding"/> and is not
        /// currently materialized.
        /// <para>
        /// Not-Full matters for B-11: <c>AttentionQuery</c> reads a Full system's
        /// live state and a demoted one's snapshot, never both. A Full neighbour
        /// would be asked for live dominance it does not have and would produce no
        /// card at all, so the step would silently have one card to compare instead
        /// of two.
        /// </para>
        /// </summary>
        private static string FirstSystemOtherThan(TutorialStepContext context, string excluding)
        {
            ShipController ship = TutorialAbk.Ship(context);
            Vector3 absolute = ship.transform.position + FloatingOrigin.TotalOffset;

            string anyOther = null;
            foreach (StarSystemRecord record in StarSystemRegistry.SystemsNear(absolute))
            {
                if (record.SystemId == excluding) continue;
                anyOther ??= record.SystemId;
                if (record.StreamingState != SystemStreamingState.Full) return record.SystemId;
            }
            return anyOther;
        }
    }
}
