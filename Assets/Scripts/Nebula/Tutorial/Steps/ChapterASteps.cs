using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Simulation;
using Nebula.UI.Controls;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter A — Uçuş ve Arayüz. Curriculum steps A-01…A-13 (global 1–13).
    /// <para>
    /// Every success condition here is real gameplay state: a transform that
    /// really turned, a throttle the ship really carries, a cruise state machine
    /// that really engaged, a HUD counter that only moves when a layer genuinely
    /// absorbed a hit. Nothing in this file reads input, elapsed time, or
    /// displayed text to decide that a step is done.
    /// </para>
    /// </summary>
    public static class ChapterASteps
    {
        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return LookAround();
            yield return SetThrottle();
            yield return AccelerateAndStop();
            // Next to acceleration, which is what it is a variation on. The id is
            // A-14 because the step was written after the chapter was numbered;
            // ids identify, they do not order, and the catalog numbers by yield
            // order rather than by id.
            yield return Boost();
            yield return Roll();
            yield return EngageCruise();
            yield return ApproachPlanet();
            yield return StabilizeOrbit();
            yield return ReleaseOrbit();
            yield return ReadDurability();
            yield return ReadEnergyAndHeat();
            yield return OpenParentMenu();
            yield return ReadContextBar();
            yield return PauseAndModalGate();
        }

        // ---- A-01 -------------------------------------------------------------

        private static TutorialStepDefinition LookAround() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-01", "Çevreye Bak",
            "Fareyi hareket ettirerek gemiyi çevir ve etrafına bak.",
            "Fareyle gemiyi çevirmeyi öğren.",
            () =>
            {
                Quaternion start = Quaternion.identity;
                bool captured = false;

                return TutorialSteps.Poll(
                    complete: ctx => captured &&
                        Quaternion.Angle(start, TutorialAbk.Ship(ctx).transform.rotation) >= 45f,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ShipController ship = TutorialAbk.Ship(ctx);
                        ship.RotationLocked = false;
                        start = ship.transform.rotation;
                        captured = true;
                    },
                    // The game prints nothing here and refuses nothing: HandleRotation
                    // simply early-returns while the cursor is free. So this is worded
                    // as the tutorial's own guidance, never as a refusal by the game.
                    reject: _ => Cursor.lockState != CursorLockMode.Locked
                        ? "FAREYİ YAKALAMAK İÇİN EKRANA TIKLA"
                        : null);
            },
            assets: TutorialAbk.PlayerShipOnly);

        // ---- A-02 -------------------------------------------------------------

        private static TutorialStepDefinition SetThrottle() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-02", "Gazı Ayarla",
            "Fare tekerleğiyle ya da gaz çubuğunu sürükleyerek gazı en az %60'a çıkar.",
            "Gaz kolunu kullanmayı öğren.",
            () =>
            {
                return TutorialSteps.Poll(
                    complete: ctx => TutorialAbk.Ship(ctx).GetThrottleFraction() >= 0.60f,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ShipController ship = TutorialAbk.Ship(ctx);
                        float restore = ship.GetThrottleFraction();
                        ship.ReleaseStabilize();
                        ship.SetThrottleFraction(0f);
                        ctx.Scope.OnCleanup(() => { if (ship != null) ship.SetThrottleFraction(restore); });
                    });
            },
            assets: TutorialAbk.PlayerShipOnly);

        // ---- A-03 -------------------------------------------------------------

        private static TutorialStepDefinition AccelerateAndStop() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-03", "Hızlan ve Yavaşla",
            "İleri itiş ver, hızını 60 birim/saniyenin üzerine çıkar, sonra bırakıp durana kadar bekle.",
            "İtiş ve ataleti tanı.",
            () =>
            {
                bool fast = false, slow = false;

                return TutorialSteps.Poll(
                    complete: _ => fast && slow,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ShipController ship = TutorialAbk.Ship(ctx);
                        float restore = ship.GetThrottleFraction();
                        ship.ReleaseStabilize();
                        TutorialAbk.Cruise(ctx)?.ApplyEngineTier(0);
                        ship.SetThrottleFraction(0.6f);
                        ship.SetVelocity(Vector3.zero);
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (ship == null) return;
                            ship.SetVelocity(Vector3.zero);
                            ship.SetThrottleFraction(restore);
                        });
                    },
                    objectives: (ctx, report) =>
                    {
                        float speed = TutorialAbk.Ship(ctx).Velocity.magnitude;
                        if (speed >= 60f) fast = true;
                        else if (fast && speed < 5f) slow = true;
                        report.SetObjective(0, fast);
                        report.SetObjective(1, slow);
                    });
            },
            checklistTr: new[] { "Hızı 60 u/sn üzerine çıkar", "Hızı tekrar 5 u/sn altına düşür" },
            assets: TutorialAbk.PlayerShipOnly);

        // ---- A-14 -------------------------------------------------------------

        private static TutorialStepDefinition Boost() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-14", "Hızlandırıcıyı Kullan",
            "İleri iterken hızlandırıcıyı basılı tut ve gemiyi normal azami hızının üzerine çıkar.",
            "Hızlandırıcıyı ve sınırını öğren.",
            () =>
            {
                float boostFreeCeiling = float.NaN;

                return TutorialSteps.Poll(
                    // Two halves, and both are required in the same evaluation.
                    // Boosting alone is too weak a proof: it is a readout the ship
                    // recomputes from the flight controls every frame, so it can be
                    // true on a frame where the ship has not gone anywhere. The
                    // speed is the part that cannot be had without the boost
                    // multiplier actually having been applied.
                    // Since VS-017a the flag is also unwritable from outside
                    // ShipController, so the only way to make it true is to hold
                    // the boost control on ShipController.FlightInput -- which in
                    // the shipping game is the keyboard, and in a test is the one
                    // seam that stands in for it.
                    complete: ctx =>
                    {
                        if (float.IsNaN(boostFreeCeiling)) return false;
                        ShipController ship = TutorialAbk.Ship(ctx);
                        return ship.Boosting && ship.Velocity.magnitude > boostFreeCeiling;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ShipController ship = TutorialAbk.Ship(ctx);

                        float restoreThrottle = ship.GetThrottleFraction();
                        Vector3 restoreVelocity = ship.Velocity;
                        // The position too: a boost that really ran carries the ship
                        // hundreds of units before the step ends, and the next step
                        // would otherwise open somewhere the player never flew to.
                        Vector3 restorePosition = ship.transform.position;
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (ship == null) return;
                            ship.SetThrottleFraction(restoreThrottle);
                            ship.SetVelocity(restoreVelocity);
                            ship.transform.position = restorePosition;
                        });

                        // Boost is refused above cruise multiplier 1, so pure local
                        // flight is a prerequisite rather than part of the lesson.
                        ship.ReleaseStabilize();
                        TutorialAbk.Cruise(ctx)?.RestoreFlightState(
                            FlightMode.LocalFlight, 0f, 1f, false, Vector3.zero);
                        ship.CruiseSpeedMultiplier = 1f;

                        ship.SetThrottleFraction(1f);
                        ship.SetVelocity(Vector3.zero);

                        // The ceiling is the game's own commanded-speed formula with
                        // the boost factor left at 1 -- the fastest this ship can
                        // legitimately go without boosting, not a number chosen here.
                        boostFreeCeiling = FlightTestSpeedMath.CommandedSpeed(
                                               ship.Throttle, 1f, ship.CruiseSpeedMultiplier, ship.TestSpeedMultiplier)
                                           * ship.CombatBoostSpeedMultiplier * ship.DamagedSpeedMultiplier;

                        // The flag needs no clearing here any more: it is private to
                        // ShipController and recomputed from the live controls every
                        // frame, and the velocity above opens the step at zero, so a
                        // stale flag cannot make this step open already satisfied.
                    },
                    reject: ctx => TutorialAbk.Ship(ctx).CruiseSpeedMultiplier > 1.0001f
                        ? "SEYİR KİPİNDE HIZLANDIRICI ÇALIŞMAZ; ÖNCE YEREL UÇUŞA DÖN"
                        : null);
            },
            assets: TutorialAbk.PlayerShipOnly,
            // Rendered through the project's own key-label resolver rather than
            // written out, even though a flight key is fixed and unrebindable.
            keyHint: () => NebulaControlBindings.KeyLabel(KeyCode.LeftShift));

        // ---- A-04 -------------------------------------------------------------

        private static TutorialStepDefinition Roll() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-04", "Yönel ve Yat",
            "Q ve E ile gemiyi kendi ekseninde yatır, sonra burnu istediğin yöne çevir.",
            "Yatış ve yönelmeyi öğren.",
            () =>
            {
                Vector3 startUp = Vector3.up;
                bool captured = false;

                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        if (!captured) return false;
                        Transform t = TutorialAbk.Ship(ctx).transform;
                        Vector3 axis = t.forward;
                        Vector3 from = Vector3.ProjectOnPlane(startUp, axis);
                        Vector3 to = Vector3.ProjectOnPlane(t.up, axis);
                        if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return false;
                        return Mathf.Abs(Vector3.SignedAngle(from, to, axis)) >= 60f;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ShipController ship = TutorialAbk.Ship(ctx);
                        // Heading lock is what sets RotationLocked; releasing it is a
                        // prerequisite, not the thing the player is being asked to do.
                        TutorialAbk.Cruise(ctx)?.RestoreFlightState(
                            FlightMode.LocalFlight, 0f, 1f, false, Vector3.zero);
                        ship.RotationLocked = false;
                        startUp = ship.transform.up;
                        captured = true;
                    },
                    reject: ctx => TutorialAbk.Ship(ctx).RotationLocked
                        ? "YÖN SABİTLEME AÇIKKEN DÖNÜLEMEZ"
                        : null);
            },
            assets: TutorialAbk.PlayerShipOnly);

        // ---- A-05 -------------------------------------------------------------

        private static TutorialStepDefinition EngageCruise() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-05", "Seyre Geç",
            "Gazı sonuna kadar aç ve rotanı bozmadan gemiyi seyir hızına ulaştır.",
            "Derin uzay seyrine geçmeyi öğren.",
            () =>
            {
                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        ShipCruise cruise = TutorialAbk.Cruise(ctx);
                        return cruise != null && cruise.Mode == FlightMode.DeepSpaceCruise;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ShipController ship = TutorialAbk.Ship(ctx);
                        ShipCruise cruise = TutorialAbk.Cruise(ctx);

                        float restoreThrottle = ship.GetThrottleFraction();
                        int restoreTier = cruise != null ? cruise.EngineTier : 0;

                        ship.ReleaseStabilize();
                        // Back to local flight first, so the step can never open
                        // already cruising -- which would be it completing itself.
                        cruise?.RestoreFlightState(FlightMode.LocalFlight, 0f, 1f, false, Vector3.zero);
                        cruise?.ApplyEngineTier(1);
                        // Both are real entry conditions, and neither is the lesson.
                        TutorialAbk.PlaceShipClearOfHazards(ctx);
                        // Deliberately below the 0.85 gate, so the step does not open
                        // already eligible.
                        ship.SetThrottleFraction(0.5f);

                        ctx.Scope.OnCleanup(() =>
                        {
                            if (cruise != null)
                            {
                                cruise.RestoreFlightState(FlightMode.LocalFlight, 0f, 1f, false, Vector3.zero);
                                cruise.ApplyEngineTier(restoreTier);
                            }
                            if (ship == null) return;
                            ship.SetThrottleFraction(restoreThrottle);
                            ship.CruiseSpeedMultiplier = 1f;
                        });
                    },
                    reject: ctx => TutorialAbk.CruiseRejectTr(TutorialAbk.Cruise(ctx)?.CruiseBlockedReason),
                    objectives: (ctx, report) =>
                    {
                        ShipCruise cruise = TutorialAbk.Cruise(ctx);
                        report.SetObjective(0, TutorialAbk.Ship(ctx).GetThrottleFraction() >= 0.85f);
                        report.SetObjective(1, cruise != null && cruise.Mode == FlightMode.DeepSpaceCruise);
                    });
            },
            checklistTr: new[] { "Gazı %85 üzerine çıkar", "Uçuş modunu SEYİR'e getir" },
            assets: TutorialAbk.PlayerShipOnly);

        // ---- A-06 -------------------------------------------------------------

        private static TutorialStepDefinition ApproachPlanet() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-06", "Gezegene Yaklaş",
            "Gezegene, hedef olarak seçilene kadar yaklaş.",
            "Yakınlık hedeflemesini öğren.",
            () =>
            {
                string worldId = null;
                bool armed = false;

                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        ShipExtraction extraction = TutorialAbk.Extraction(ctx);
                        if (extraction == null || worldId == null) return false;
                        PlanetIdentity target = extraction.TargetPlanet;
                        // Armed only once the world has really reported "no target
                        // yet" -- a stale target left over from the previous step must
                        // never be credited as this step's approach.
                        if (target == null || target.WorldId != worldId) { armed = true; return false; }
                        return armed;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        PlanetIdentity planet = TutorialAbk.EnsurePlanet(ctx, 0, out _);
                        if (planet == null) return;
                        worldId = planet.WorldId;
                        // Outside body.Radius * 3, the interest radius UpdateTarget uses.
                        TutorialAbk.PlaceShipAtCentreDistance(ctx, planet, planet.Body.Radius * 4.5f);
                    },
                    objectives: (ctx, report) =>
                    {
                        ShipExtraction extraction = TutorialAbk.Extraction(ctx);
                        report.SetObjective(0, extraction != null && extraction.TargetPlanet != null);
                    });
            },
            assets: TutorialAbk.ShipAndMetalPlanet);

        // ---- A-07 -------------------------------------------------------------

        private static TutorialStepDefinition StabilizeOrbit() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-07", "Yörüngeyi Sabitle",
            "Gezegenin yanındayken yörünge sabitlemeyi aç ve geminin gezegenle birlikte taşınmasını izle.",
            "Yörünge sabitlemeyi öğren.",
            () =>
            {
                Transform anchor = null;

                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        ShipController ship = TutorialAbk.Ship(ctx);
                        return anchor != null && ship.IsStabilized && ship.StabilizedAnchor == anchor;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        PlanetIdentity planet = TutorialAbk.EnsurePlanet(ctx, 0, out bool created);
                        if (planet == null) return;
                        anchor = planet.Body.transform;

                        TutorialAbk.MarkScannedForTheLesson(ctx, planet);
                        TutorialAbk.PlaceShipAtCentreDistance(ctx, planet, planet.Body.Radius * 2f);
                        TutorialAbk.Ship(ctx).ReleaseStabilize();

                        // A body that never moves cannot show what "riding the anchor"
                        // means. Only ever added to a body this step created; a real
                        // streamed planet already carries its own orbital motion.
                        if (created)
                        {
                            var drift = planet.Body.gameObject.AddComponent<TutorialPlanetDrift>();
                            drift.MetresPerSecond = planet.Body.transform.right * 40f;
                            ctx.Scope.Track(drift);
                        }
                    },
                    reject: ctx =>
                    {
                        // Re-derived from the same live state ShipExtraction.BuildEligible
                        // reads; the production reason itself is English and never reaches
                        // the player through this card.
                        ShipExtraction extraction = TutorialAbk.Extraction(ctx);
                        if (extraction == null) return null;
                        if (extraction.TargetPlanet == null) return "GEZEGEN MENZİLDE DEĞİL";
                        if (extraction.TargetProfile == null || !extraction.TargetProfile.Scanned)
                            return "ÖNCE GEZEGENİ TARA";
                        return null;
                    });
            },
            assets: TutorialAbk.ShipAndMetalPlanet,
            keyHint: () => NebulaControlBindings.KeyLabel(ShipExtraction.BuildModeKey));

        // ---- A-08 -------------------------------------------------------------

        private static TutorialStepDefinition ReleaseOrbit() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-08", "Sabitlemeyi Bırak",
            "Yörünge sabitlemesini bırak ve serbest uçuşa dön.",
            "Sabitlemeyi bırakmayı öğren.",
            () =>
            {
                bool started = false;

                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        ShipController ship = TutorialAbk.Ship(ctx);
                        return started && !ship.IsStabilized && ship.StabilizedAnchor == null;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        PlanetIdentity planet = TutorialAbk.EnsurePlanet(ctx, 0, out _);
                        if (planet == null) return;
                        TutorialAbk.PlaceShipAtCentreDistance(ctx, planet, planet.Body.Radius * 2f);
                        ShipController ship = TutorialAbk.Ship(ctx);
                        // The step opens stabilized, which is the opposite of its own
                        // success condition.
                        ship.StabilizeTo(planet.Body.transform);
                        started = true;
                        ctx.Scope.OnCleanup(() => { if (ship != null) ship.ReleaseStabilize(); });
                    });
            },
            assets: TutorialAbk.ShipAndMetalPlanet,
            keyHint: () => NebulaControlBindings.KeyLabel(ShipExtraction.BuildModeKey));

        // ---- A-09 -------------------------------------------------------------

        private static TutorialStepDefinition ReadDurability() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-09", "Kalkan, Zırh ve Gövde",
            "Sağ alttaki dayanıklılık panelini aç ve bir isabetin hangi katmana gittiğini gör.",
            "Üç dayanıklılık katmanını tanı.",
            () =>
            {
                NebulaHudVitals vitals = null;

                return TutorialSteps.Poll(
                    complete: _ => vitals != null && vitals.ShieldReactionsShown >= 1,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ShipCombat combat = TutorialAbk.Combat(ctx);
                        if (combat == null) return;
                        if (combat.State == null) combat.InitializeNewCampaign();
                        combat.SetEncounterOpen(false);

                        TutorialAbk.CaptureAndRestoreDurability(ctx, combat);

                        NebulaUiShell shell = TutorialAbk.NewShell(ctx);
                        vitals = NebulaHudVitals.Create(shell, combat);

                        ctx.World?.Spawn(TutorialAbk.TrainingDummy,
                            TutorialAbk.Ship(ctx).transform.position + TutorialAbk.Ship(ctx).transform.forward * 220f,
                            Quaternion.identity);
                    });
            },
            assets: new[] { TutorialAbk.PlayerShip, TutorialAbk.TrainingDummy });

        // ---- A-10 -------------------------------------------------------------

        private static TutorialStepDefinition ReadEnergyAndHeat() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-10", "Enerji ve Isı",
            "Yıldıza fazla yaklaş; enerji yenilenmenin düştüğünü ve silah yuvasının ısındığını panelde izle.",
            "Yıldız yakınında enerji ve ısıyı oku.",
            () =>
            {
                return TutorialSteps.Poll(
                    complete: ctx =>
                    {
                        ShipCombat combat = TutorialAbk.Combat(ctx);
                        return combat != null && combat.Hardpoints != null && combat.Hardpoints.Length > 0
                               && combat.Hardpoints[0].Heat > 0f;
                    },
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ShipCombat combat = TutorialAbk.Combat(ctx);
                        if (combat == null) return;
                        if (combat.State == null) combat.InitializeNewCampaign();
                        combat.SetEncounterOpen(false);

                        TutorialAbk.CaptureAndRestoreHardpoints(ctx, combat);
                        // Heat back to zero after the capture, so the reading the
                        // player is asked to watch is one their own approach made.
                        if (combat.Hardpoints != null && combat.Hardpoints.Length > 0)
                            combat.Hardpoints[0].RestoreFromSave(combat.Hardpoints[0].Weapon,
                                combat.Hardpoints[0].Tier, 0f, false, 0f);

                        TutorialAbk.NewShell(ctx); // the panel the reading is read on

                        // The step ends with the ship deep inside a star's Critical
                        // band, which is not a place to leave it: the next step would
                        // inherit real environmental heat it never asked for.
                        ShipController ship = TutorialAbk.Ship(ctx);
                        Vector3 startedAt = ship.transform.position;
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (ship == null) return;
                            ship.transform.position = startedAt;
                            ship.SetVelocity(Vector3.zero);
                        });

                        TutorialAbk.PlaceShipOutsideEveryStarRiskBand(ctx);
                    },
                    objectives: (ctx, report) =>
                    {
                        ShipCombat combat = TutorialAbk.Combat(ctx);
                        report.SetObjective(0, combat?.State != null &&
                            combat.State.Energy < combat.State.EnergyCapacity);
                    });
            },
            assets: TutorialAbk.PlayerShipOnly);

        // ---- A-11 -------------------------------------------------------------

        private static TutorialStepDefinition OpenParentMenu() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-11", "Komuta Menüsünü Aç",
            "Komuta menüsünü aç ve rayı kullanarak Keşif sayfasına geç.",
            "Komuta menüsünü ve rayı tanı.",
            () =>
            {
                NebulaParentMenuScreen menu = null;
                NebulaUiShell shell = null;

                return TutorialSteps.Poll(
                    complete: _ => menu != null && menu.IsOpen &&
                        menu.CurrentPage == NebulaUiAssetCatalog.Page.Discovery,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ExclusiveModal.ResetForSession();
                        shell = TutorialAbk.NewShell(ctx);
                        menu = TutorialAbk.NewParentMenu(ctx, shell);
                    },
                    reject: _ => shell != null && ExclusiveModal.IsHeldByOther(shell)
                        ? "BAŞKA BİR EKRAN AÇIK"
                        : null,
                    objectives: (_, report) =>
                    {
                        report.SetObjective(0, menu != null && menu.IsOpen);
                        report.SetObjective(1, menu != null &&
                            menu.CurrentPage == NebulaUiAssetCatalog.Page.Discovery);
                    });
            },
            checklistTr: new[] { "Menüyü aç", "Keşif sayfasına geç" },
            assets: TutorialAbk.PlayerShipOnly,
            keyHint: () => NebulaControlBindings.KeyLabel(NebulaParentMenuScreen.ProgressionKey));

        // ---- A-12 -------------------------------------------------------------

        private static TutorialStepDefinition ReadContextBar() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-12", "Bağlam Çubuğu",
            "Taranmamış gezegenin yanına dön ve ekranın altındaki çubuğun sana ne yapabileceğini söylemesini bekle.",
            "Bağlam çubuğunu okumayı öğren.",
            () =>
            {
                NebulaHudContextBar bar = null;

                return TutorialSteps.Poll(
                    complete: _ => bar != null && bar.VisibleActionCount >= 1,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        PlanetIdentity planet = TutorialAbk.EnsurePlanet(ctx, 0, out _);
                        if (planet == null) return;
                        TutorialAbk.PlaceShipAtCentreDistance(ctx, planet, planet.Body.Radius * 4.5f);

                        NebulaUiShell shell = TutorialAbk.NewShell(ctx);
                        bar = NebulaHudContextBar.Create(shell);
                        NebulaHudContextBar owned = bar;
                        NebulaExtractionContextDriver.Create(
                            bar, TutorialAbk.Extraction(ctx),
                            TutorialAbk.Ship(ctx).GetComponent<ShipPlanetEvacuation>());
                        // Opens empty, so the step cannot start already satisfied.
                        bar.Clear();
                        ctx.Scope.OnCleanup(() => { if (owned != null) owned.Clear(); });
                    });
            },
            assets: TutorialAbk.ShipAndMetalPlanet,
            keyHint: () => NebulaControlBindings.KeyLabel(ShipExtraction.ScanKey));

        // ---- A-13 -------------------------------------------------------------

        /// <summary>
        /// A-13 used to teach "the menu pauses the game" and completed on
        /// <c>Time.timeScale == 0</c>. Screens no longer stop the clock — the world
        /// keeps running behind an open menu or map — so the step now teaches the
        /// rule that actually holds: an open screen takes the controls, not the
        /// simulation. Left as it was it would have been a soft lock, because its
        /// first objective could never be satisfied again.
        /// </summary>
        private static TutorialStepDefinition PauseAndModalGate() => new TutorialStepDefinition(
            TutorialChapterId.Flight, "A-13", "Ekranlar ve Kontrol Kilidi",
            "Menü açıkken oyunun arkada devam ettiğini ama uçuş kontrolünün kilitlendiğini gör, "
            + "sonra ESC ile kapatıp uçuşa dön.",
            "Ekran kilidini ve modal kapısını tanı.",
            () =>
            {
                NebulaUiShell shell = null;
                bool gated = false, released = false;

                return TutorialSteps.Poll(
                    complete: _ => gated && released,
                    prepare: ctx =>
                    {
                        TutorialAbk.OpenWorldInput(ctx);
                        ExclusiveModal.ResetForSession();

                        shell = TutorialAbk.NewShell(ctx);
                        TutorialAbk.NewParentMenu(ctx, shell);

                        NebulaUiShell owned = shell;
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (owned != null) owned.PopAll();
                            ExclusiveModal.ResetForSession();
                        });
                    },
                    reject: _ => shell != null && ExclusiveModal.IsHeldByOther(shell)
                        ? "ÖNCE AÇIK EKRANI KAPAT"
                        : null,
                    objectives: (_, report) =>
                    {
                        if (shell == null) return;
                        if (!gated)
                        {
                            // The modal gate is asserted alongside the input gate so
                            // the objective still needs the screen to be genuinely
                            // open, rather than any raised InputGate anywhere.
                            gated = shell.AnyScreenOpen && InputGate.Suppressed
                                    && ReferenceEquals(ExclusiveModal.Owner, shell);
                        }
                        else if (!released)
                        {
                            released = shell.StackDepth == 0 && !InputGate.Suppressed
                                       && ExclusiveModal.Owner == null;
                        }
                        report.SetObjective(0, gated);
                        report.SetObjective(1, released);
                    });
            },
            checklistTr: new[]
            {
                "Menüyü aç ve uçuş kontrolünün kilitlendiğini doğrula",
                "Menüyü kapatıp uçuş kontrolünü geri al",
            },
            assets: TutorialAbk.PlayerShipOnly,
            keyHint: () => NebulaControlBindings.KeyLabel(NebulaParentMenuScreen.ProgressionKey));
    }

    /// <summary>
    /// A tutorial-owned, tutorial-only drift so a body this chapter created has
    /// real motion for Orbit Stabilize to ride. Never added to a streamed planet,
    /// which already carries its own orbital motion.
    /// </summary>
    internal sealed class TutorialPlanetDrift : MonoBehaviour
    {
        internal Vector3 MetresPerSecond;

        private void Update()
        {
            if (MetresPerSecond.sqrMagnitude <= 0f) return;
            transform.position += MetresPerSecond * Time.deltaTime;
        }
    }

    /// <summary>
    /// The plumbing chapters A, B and K share: how a step reaches the live ship,
    /// how it borrows a surface to draw on, how it gets a real planet in front of
    /// the player, and how each of the project's English refusal strings is said
    /// in Turkish.
    /// <para>
    /// Every restore registered here goes through
    /// <see cref="ITutorialStepScope.OnCleanup"/>, so it runs on abandonment and
    /// on an exception, not only when the player succeeds.
    /// </para>
    /// </summary>
    internal static class TutorialAbk
    {
        // ---- declared assets --------------------------------------------------

        internal static readonly TutorialAssetRequirement PlayerShip = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "visual.ship.player.mki.base",
            "the ship the player is flying and turning");

        internal static readonly TutorialAssetRequirement MetalPlanet = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "visual.celestial.planet.metal",
            "the planet the player approaches, scans and stabilises on");

        internal static readonly TutorialAssetRequirement BiomassPlanet = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "visual.celestial.planet.wood",
            "the second planet the extraction target hands over to");

        internal static readonly TutorialAssetRequirement TrainingDummy = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "training.combat_dummy",
            "the hazard whose hit lands on the shield layer");

        internal static readonly TutorialAssetRequirement Anomaly = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "poi.anomaly",
            "the anomaly the player locates, identifies and resolves");

        internal static readonly TutorialAssetRequirement Wreck = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "poi.wreck.small.fresh",
            "the wreck the player salvages");

        internal static readonly TutorialAssetRequirement SalvageNode = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "visual.poi.salvage_node",
            "the individual salvage node the wreck's cargo is drawn from");

        internal static readonly TutorialAssetRequirement AbandonedFacility = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "poi.abandoned_facility",
            "the facility whose irreversible outcome the player chooses");

        internal static readonly TutorialAssetRequirement SensorRelay = new TutorialAssetRequirement(
            TutorialAssetKind.VisualCatalogId, "poi.sensor_relay.resolved",
            "what the facility becomes when the relay branch is taken");

        internal static readonly TutorialAssetRequirement[] PlayerShipOnly = { PlayerShip };
        internal static readonly TutorialAssetRequirement[] ShipAndMetalPlanet = { PlayerShip, MetalPlanet };

        // ---- live gameplay ----------------------------------------------------

        internal static ShipController Ship(TutorialStepContext context) => context.Gameplay.Ship;

        internal static T OnShip<T>(TutorialStepContext context) where T : Component
        {
            ShipController ship = context?.Gameplay?.Ship;
            return ship != null ? ship.GetComponent<T>() : null;
        }

        internal static ShipCruise Cruise(TutorialStepContext context) => OnShip<ShipCruise>(context);
        internal static ShipCombat Combat(TutorialStepContext context) => OnShip<ShipCombat>(context);
        internal static ShipExtraction Extraction(TutorialStepContext context) => OnShip<ShipExtraction>(context);
        internal static ShipLogistics Logistics(TutorialStepContext context) => OnShip<ShipLogistics>(context);
        internal static Poi.ShipPoi PoiDriver(TutorialStepContext context) => OnShip<Poi.ShipPoi>(context);

        /// <summary>
        /// Opens the world's input gate for the length of the step and puts it back
        /// exactly as it was. All three flags are process-wide statics with no
        /// persistence behind them, so nothing restores them if the step does not.
        /// </summary>
        internal static void OpenWorldInput(TutorialStepContext context)
        {
            bool suppressed = InputGate.Suppressed;
            bool combatActive = CombatInputContext.CombatActive;
            bool trainingActive = CombatInputContext.TrainingActive;

            InputGate.Suppressed = false;
            CombatInputContext.CombatActive = false;
            CombatInputContext.TrainingActive = false;

            context.Scope.OnCleanup(() =>
            {
                InputGate.Suppressed = suppressed;
                CombatInputContext.CombatActive = combatActive;
                CombatInputContext.TrainingActive = trainingActive;
            });
        }

        // ---- surfaces ---------------------------------------------------------

        /// <summary>
        /// A shell of the step's own. The tutorial composition skips
        /// <c>PresentationInstaller</c> entirely, so there is no session shell to
        /// borrow -- reading <c>NebulaContext.Presentation</c> would throw.
        /// </summary>
        internal static NebulaUiShell NewShell(TutorialStepContext context)
        {
            NebulaUiShell shell = NebulaUiShell.Create();
            context.Scope.OnCleanup(() =>
            {
                if (shell == null) return;
                shell.Shutdown();
                DestroyNow(shell.gameObject);
            });
            return shell;
        }

        /// <summary>The parent menu with the two pages the flight chapter names, closed.</summary>
        internal static NebulaParentMenuScreen NewParentMenu(TutorialStepContext context, NebulaUiShell shell)
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            menu.AddPage(new NebulaOverviewPage(context.Gameplay));
            menu.AddPage(new NebulaDiscoveryPage(context.Gameplay));
            menu.Close();

            context.Scope.OnCleanup(() =>
            {
                if (menu == null) return;
                menu.Close();
                DestroyNow(menu.gameObject);
            });
            return menu;
        }

        internal static void DestroyNow(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        // ---- planets ----------------------------------------------------------

        /// <summary>
        /// The planet this step points at: a real streamed one when the session
        /// already materialized it, otherwise one built from the same
        /// <see cref="PlanetManifest"/> the streamer would have used, with its real
        /// catalog visual on it. Never a primitive.
        /// </summary>
        internal static PlanetIdentity EnsurePlanet(TutorialStepContext context, int index, out bool created)
        {
            created = false;

            var existing = new List<PlanetIdentity>();
            foreach (PlanetIdentity live in ExtractionState.AllPlanets)
                if (live != null && live.Body != null) existing.Add(live);
            existing.Sort((a, b) => string.CompareOrdinal(a.WorldId, b.WorldId));
            if (index < existing.Count) return existing[index];

            PlanetManifest manifest = ManifestForIndex(index);
            if (manifest == null)
            {
                Debug.LogError("[Tutorial] No PlanetManifest resolves for the start system; " +
                               "a planet step cannot install a real planet.");
                return null;
            }

            PlanetIdentity already = ExtractionState.FindPlanet(manifest.PlanetId);
            if (already != null && already.Body != null) return already;

            ShipController ship = Ship(context);
            Vector3 where = ship.transform.position + ship.transform.forward * (manifest.PlanetRadius * 6f)
                            + ship.transform.right * (manifest.PlanetRadius * 8f * index);

            var holder = new GameObject("Tutorial Planet :: " + manifest.PlanetId);
            holder.transform.position = where;
            context.Scope.Track(holder);

            var body = holder.AddComponent<CelestialBody>();
            body.Configure(CelestialBodyType.Planet, manifest.PlanetRadius, 1f, movedByGravity: false);

            var identity = holder.AddComponent<PlanetIdentity>();
            identity.Initialize(manifest.PlanetId, body, isStartSystem: false);

            GameObject visual = context.World?.Spawn(
                manifest.Family == ResourceFamily.Wood ? BiomassPlanet : MetalPlanet,
                where, Quaternion.identity);
            if (visual != null) visual.transform.SetParent(holder.transform, worldPositionStays: true);

            created = true;
            return identity;
        }

        private static PlanetManifest ManifestForIndex(int index)
        {
            SectorManifestRepository manifests = StarSystemRegistry.Manifests;
            if (manifests == null) return null;
            string systemId = StarSystemRegistry.StartSystemId;
            if (string.IsNullOrEmpty(systemId)) return null;

            SystemManifest system = manifests.ResolveSystem(systemId);
            if (system == null || system.Planets == null || system.Planets.Count == 0) return null;
            return system.Planets[Mathf.Clamp(index, 0, system.Planets.Count - 1)];
        }

        /// <summary>Puts the ship a given distance from a planet's centre, holding station.</summary>
        internal static void PlaceShipAtCentreDistance(TutorialStepContext context, PlanetIdentity planet, float centreDistance)
        {
            if (planet == null || planet.Body == null) return;
            ShipController ship = Ship(context);
            Vector3 centre = planet.Body.transform.position;
            Vector3 direction = ship.transform.position - centre;
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.forward;

            ship.ReleaseStabilize();
            ship.SetVelocity(Vector3.zero);
            ship.transform.position = centre + direction.normalized * Mathf.Max(centreDistance, planet.Body.Radius * 1.05f);
        }

        /// <summary>
        /// Marks a profile scanned without going through
        /// <c>ExtractionState.CompletePlanetScan</c>, which awards XP through the
        /// process-wide ledger. Restores the captured value at cleanup.
        /// </summary>
        internal static void MarkScannedForTheLesson(TutorialStepContext context, PlanetIdentity planet)
        {
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            if (profile == null) return;
            bool wasScanned = profile.Scanned;
            profile.Scanned = true;
            context.Scope.OnCleanup(() => profile.Scanned = wasScanned);
        }

        /// <summary>
        /// Captures the process-wide XP ledger and puts it back at cleanup.
        /// <para>
        /// Required around every step that can reach <c>ProgressionService.Award</c>
        /// -- the planet scan, the anomaly, the wreck. The ledger is a static that
        /// awards an id at most once ever, so a tutorial run that consumed
        /// <c>first_scan</c> would consume it for the player's real campaign too.
        /// </para>
        /// </summary>
        internal static void BracketProgression(TutorialStepContext context)
        {
            long totalXp = Progression.ProgressionService.State.TotalXp;
            int points = Progression.ProgressionService.State.DevelopmentPoints;
            var ledger = new List<string>(Progression.ProgressionService.Ledger.All);
            context.Scope.OnCleanup(() => Progression.ProgressionService.Restore(totalXp, points, ledger));
        }

        // ---- combat readings --------------------------------------------------

        internal static void CaptureAndRestoreDurability(TutorialStepContext context, ShipCombat combat)
        {
            if (combat?.State == null) return;
            Durability durability = combat.State.Durability;
            float shield = durability.Shield, armor = durability.Armor, hull = durability.Hull;
            float energy = combat.State.Energy;
            bool damaged = combat.State.Damaged;

            context.Scope.OnCleanup(() =>
            {
                if (combat == null || combat.State == null) return;
                combat.State.RestoreFromSave(shield, armor, hull, 0f, energy, false, 0f, 0f, damaged);
            });
        }

        internal static void CaptureAndRestoreHardpoints(TutorialStepContext context, ShipCombat combat)
        {
            if (combat?.Hardpoints == null) return;
            Combat.HardpointState[] hardpoints = combat.Hardpoints;
            var snapshot = new (Combat.WeaponFamily? weapon, int tier, float heat, bool overheated)[hardpoints.Length];
            for (int i = 0; i < hardpoints.Length; i++)
                snapshot[i] = (hardpoints[i].Weapon, hardpoints[i].Tier, hardpoints[i].Heat, hardpoints[i].Overheated);

            context.Scope.OnCleanup(() =>
            {
                for (int i = 0; i < hardpoints.Length && i < snapshot.Length; i++)
                    hardpoints[i].RestoreFromSave(snapshot[i].weapon, snapshot[i].tier,
                        snapshot[i].heat, snapshot[i].overheated, 0f);
            });
        }

        /// <summary>
        /// Parks the ship clear of every star's Warning band, so environmental heat
        /// starts at zero and the player's own approach is what raises it.
        /// </summary>
        internal static void PlaceShipOutsideEveryStarRiskBand(TutorialStepContext context)
        {
            ShipController ship = Ship(context);
            Vector3 absolute = ship.transform.position + FloatingOrigin.TotalOffset;

            foreach (StarSystemRecord record in StarSystemRegistry.SystemsNear(absolute))
            {
                float warning = record.StarRadius * StarClassCatalog.WarningRadiusMultiplier(record.StarClass);
                Vector3 away = absolute - record.AbsoluteCenter;
                if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
                if (away.magnitude > warning * 1.2f) continue;

                absolute = record.AbsoluteCenter + away.normalized * (warning * 1.5f);
            }

            ship.SetVelocity(Vector3.zero);
            ship.transform.position = absolute - FloatingOrigin.TotalOffset;
        }

        /// <summary>
        /// Puts the ship where deep-space cruise is actually allowed to engage:
        /// clear of every celestial body's hazard radius and outside every star's
        /// risk band. Both are real entry conditions <c>ShipCruise</c> checks, and
        /// neither of them is the thing the player is being asked to do.
        /// </summary>
        internal static void PlaceShipClearOfHazards(TutorialStepContext context)
        {
            ShipController ship = Ship(context);
            const float hazardRadiusMultiplier = 6f;

            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                IReadOnlyList<Celestial.CelestialBody> bodies = GravitySystem.Bodies;
                for (int i = 0; i < bodies.Count; i++)
                {
                    Celestial.CelestialBody body = bodies[i];
                    if (body == null) continue;
                    float safe = body.Radius * (hazardRadiusMultiplier + 4f);
                    Vector3 away = ship.transform.position - body.transform.position;
                    if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
                    if (away.magnitude >= safe) continue;

                    ship.transform.position = body.transform.position + away.normalized * safe;
                    moved = true;
                }
                if (!moved) break;
            }

            ship.SetVelocity(Vector3.zero);
            PlaceShipOutsideEveryStarRiskBand(context);
        }

        // ---- the game's own refusals, said in Turkish -------------------------

        /// <summary>
        /// <c>ShipCruise.CruiseBlockedReason</c> is produced in English and reaches
        /// the HUD verbatim. This is the whole mapping, on the same shape as
        /// <c>NebulaDiscoveryPage.AnalysisRejectText</c>: an exact switch on the
        /// production strings, and an unrecognised reason is passed through rather
        /// than replaced by an invented refusal.
        /// </summary>
        internal static string CruiseRejectTr(string reason) => reason switch
        {
            null => null,
            "" => null,
            "THROTTLE NEAR MAX TO ENGAGE CRUISE" => "SEYRE GEÇMEK İÇİN GAZI SONA YAKIN AÇ",
            "RELEASE ORBIT STABILIZE" => "ÖNCE YÖRÜNGE SABİTLEMESİNİ BIRAK",
            "EXIT BUILD MODE" => "ÖNCE İNŞA MODUNDAN ÇIK",
            "FINISH TRANSFER/SCAN FIRST" => "ÖNCE AKTARIMI/TARAMAYI BİTİR",
            "TOO CLOSE TO A CELESTIAL BODY" => "GÖK CİSMİNE FAZLA YAKIN",
            "HOLD A STEADY HEADING" => "ROTANI SABİT TUT",
            "TOO CLOSE TO STAR — CRITICAL ZONE" => "YILDIZA FAZLA YAKIN — KRİTİK BÖLGE",
            "TOO CLOSE TO STAR — WARNING ZONE" => "YILDIZA FAZLA YAKIN — UYARI BÖLGESİ",
            "SHIP DAMAGED — CRUISE UNAVAILABLE UNTIL REPAIRED" => "GEMİ HASARLI — ONARILANA KADAR SEYİR YOK",
            "REQUIRES ENGINE-NAVIGATION T1" => "MOTOR-SEYRÜSEFER T1 GEREKLİ",
            _ => reason
        };

        /// <summary>
        /// <c>ShipExtraction.ScanBlockedReason</c> is the one interpolated English
        /// string in the extraction slice. The numbers are the game's own; only the
        /// words are the tutorial's.
        /// </summary>
        internal static string ScanRejectTr(ShipExtraction extraction)
        {
            if (extraction == null || string.IsNullOrEmpty(extraction.ScanBlockedReason)) return null;
            if (extraction.TargetPlanet == null) return "GEZEGEN MENZİLDE DEĞİL";

            float altitude = extraction.Altitude(extraction.TargetPlanet);
            float limit = extraction.TargetPlanet.Body.Radius * extraction.ScannerProximityR;
            return $"YAKLAŞ — YÜKSEKLİK {altitude:0}, GEREKEN {limit:0}";
        }

        /// <summary>
        /// <c>PoiPauseReason</c> has no player-facing text anywhere in production.
        /// This is the tutorial's mapping of the real enum, never of a guess.
        /// </summary>
        internal static string PoiPauseTr(Poi.PoiPauseReason reason) => reason switch
        {
            Poi.PoiPauseReason.OutOfRange => "MENZİL DIŞINDA — YAKLAŞ",
            Poi.PoiPauseReason.NoAssignedWorker => "ATANMIŞ ARAÇ YOK",
            Poi.PoiPauseReason.TargetCargoFull => "KARGO DOLU",
            _ => null
        };

        /// <summary><c>WreckCollectRejectReason</c>, likewise with no production text of its own.</summary>
        internal static string WreckRejectTr(Poi.WreckCollectRejectReason reason) => reason switch
        {
            Poi.WreckCollectRejectReason.NotAccessible => "ENKAZ ERİŞİLEBİLİR DEĞİL",
            Poi.WreckCollectRejectReason.UnstableNotAllowedForCollector => "KARARSIZ ENKAZ TOPLAYICIYA KAPALI",
            _ => null
        };
    }
}
