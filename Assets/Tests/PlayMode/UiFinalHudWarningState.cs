using System.Collections;
using NUnit.Framework;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Simulation;
using Nebula.UI.Hud;
using Nebula.UI.Shell;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Drives a running campaign into the two urgent HUD states — a raid pending
    /// and a hold over capacity — and into the one context the bar at the bottom
    /// of the screen is for.
    /// <para>
    /// This lives on its own because two fixtures need the same states and the
    /// evidence is only worth something if they are literally the same states.
    /// <c>UiFinalHudWarningContainmentTests</c> measures rects with them;
    /// <c>UiFinalHudWarningCaptureTests</c> photographs them. If the capture
    /// reached its warnings by some easier route — writing the label text,
    /// switching a panel on by hand, reflecting a private field — the picture
    /// would prove the capture harness works and nothing about the game. So
    /// there is one way in, it is this file, and everything in it goes through a
    /// production seam:
    /// </para>
    /// <list type="bullet">
    /// <item><description>the raid through <see cref="ThreatPressureState.ScheduleRaid"/>,
    /// advanced into Warning by the real <c>RaidScheduler</c>;</description></item>
    /// <item><description>the over-capacity hold through
    /// <see cref="LogisticsCargo.SetCapacity"/>, the module-tier effect that is the
    /// only thing in the game able to shrink a hold under its own contents;</description></item>
    /// <item><description>the context bar by flying the ship to a real planet and
    /// letting <c>ShipExtraction</c> find it and
    /// <c>NebulaExtractionContextDriver</c> refresh on its own cadence.</description></item>
    /// </list>
    /// <para>
    /// Nothing here asserts about the interface. Each widget decides for itself
    /// whether to draw, and what it decides is what the two fixtures measure.
    /// </para>
    /// </summary>
    internal static class UiFinalHudWarningState
    {
        // =====================================================================
        // Reaching the live pieces
        // =====================================================================

        public static NebulaHudAlerts Alerts()
        {
            NebulaHudAlerts alerts = UiFinalPlayMode.Ctx()?.Presentation?.HudAlerts;
            Assert.IsNotNull(alerts, "the campaign composed no HUD alert cluster");
            return alerts;
        }

        public static ShipCombat Combat() => UiFinalPlayMode.Ctx().Ship.GetComponent<ShipCombat>();

        public static LogisticsCargo ShipCargo() =>
            UiFinalPlayMode.Ctx().Ship.GetComponent<ShipLogistics>().Cargo;

        public static RectTransform HudRoot() =>
            UiFinalPlayMode.Shell().Root(UiLayer.HudDynamic);

        public static RectTransform HudStaticRoot() =>
            UiFinalPlayMode.Shell().Root(UiLayer.HudStatic);

        public static RectTransform Find(Transform root, string name)
        {
            foreach (RectTransform candidate in root.GetComponentsInChildren<RectTransform>(true))
                if (candidate.name == name) return candidate;
            return null;
        }

        public static bool CargoWarningVisible()
        {
            RectTransform cargo = Find(HudRoot(), "Cargo");
            if (cargo == null || !cargo.gameObject.activeInHierarchy) return false;
            RectTransform warning = Find(cargo, "Warning");
            return warning != null && warning.gameObject.activeInHierarchy;
        }

        // =====================================================================
        // The states themselves
        // =====================================================================

        /// <summary>
        /// A real raid on this system, composed through the pressure state's own
        /// scheduling call and then advanced by the production
        /// <see cref="RaidScheduler"/>. The warning window is long so the phase
        /// does not walk on to Spawning underneath a measurement; it is the
        /// scheduler that moves it, not this.
        /// </summary>
        public static IEnumerator RaiseARaidWarning()
        {
            yield return UiFinalPlayMode.Until(() => Combat() != null && Combat().Director != null,
                "the streamed-in system to stand up its combat encounter director");

            ThreatPressureState pressure = Combat().Director.Pressure;
            Assert.AreEqual(RaidPhase.None, pressure.Phase, "a raid is already running before this one");

            pressure.ScheduleRaid(RaidStage.Pirate, RaidDoctrine.Loot,
                targetPlanetWorldId: Combat().Director.SystemId + "/planet.0",
                compositionDefIds: new[] { EnemyIds.ScoutProbeMk1 },
                encounterSeed: 1uL,
                warningSeconds: 600f);

            yield return UiFinalPlayMode.Until(() => Alerts().RaidVisible,
                "the HUD's raid banner to notice the raid the pressure state is carrying");
        }

        /// <summary>
        /// The ship's real hold, physically over its real capacity — the case
        /// <c>LogisticsCargo.OverCapacity</c> documents, reached through
        /// <c>SetCapacity</c>, which is the module-tier effect and the only thing
        /// in the game that can shrink a hold under its own contents.
        /// </summary>
        public static IEnumerator OverfillTheHold()
        {
            LogisticsCargo cargo = ShipCargo();
            cargo.Clear();
            cargo.SetCapacity(400f);
            cargo.Add(ExtractionCatalog.ResourceIds[0], 400f);
            cargo.SetCapacity(100f);

            Assert.IsTrue(cargo.OverCapacity,
                "the fixture did not manage to put the hold over capacity, so the warning " +
                "this test measures can never appear");

            yield return UiFinalPlayMode.Until(CargoWarningVisible,
                "the HUD's cargo cluster to notice the hold is over capacity");
        }

        /// <summary>
        /// The context bar, carrying a real action, because the ship is really
        /// next to a real planet.
        /// <para>
        /// <c>NebulaHudContextBar.Show</c> is deliberately <b>not</b> called here.
        /// Calling it would put words on the bar and prove only that a label can
        /// hold a string; what has to be true for the capture to mean anything is
        /// that the whole chain runs — the ship's own <c>UpdateTarget</c> finds
        /// the planet inside its interest radius, <c>NebulaExtractionContextDriver</c>
        /// reads the extraction state on its own 0.25s cadence, and the bar draws
        /// whatever that produced. So the only thing moved is the ship, which is
        /// the one thing a player moves.
        /// </para>
        /// </summary>
        public static IEnumerator BringUpTheContextBar()
        {
            var extraction = UiFinalPlayMode.Ctx().Ship.GetComponent<ShipExtraction>();
            Assert.IsNotNull(extraction,
                "the ship has no ShipExtraction, so it can never acquire a target planet and "
                + "the context bar can never have anything to say");

            NebulaHudContextBar bar = UiFinalPlayMode.Ctx().Presentation.HudContextBar;
            Assert.IsNotNull(bar, "the campaign composed no HUD context bar");

            yield return UiFinalPlayMode.Until(() => NearestPlanet() != null,
                "the streamed-in system to place a planet the ship can be flown to");

            CelestialBody planet = NearestPlanet();

            // Two radii out: inside the 3x-radius interest window
            // ShipExtraction.UpdateTarget uses, and a full radius clear of the
            // surface so nothing is flown into anything.
            UiFinalPlayMode.Ctx().Ship.transform.position =
                planet.transform.position + new Vector3(planet.Radius * 2f, 0f, 0f);

            yield return UiFinalPlayMode.Until(() => extraction.TargetPlanet != null,
                "the ship's own target search to notice the planet it has been flown to");

            yield return UiFinalPlayMode.Until(() => bar.VisibleActionCount > 0,
                "the extraction context driver to put a real action on the context bar");
        }

        private static CelestialBody NearestPlanet()
        {
            var bodies = GravitySystem.Bodies;
            if (bodies == null) return null;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i] != null && bodies[i].BodyType == CelestialBodyType.Planet)
                    return bodies[i];
            return null;
        }
    }
}
