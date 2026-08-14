using UnityEngine;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Composition step 11: creates every UI/HUD/Screen exactly once and
    /// tracks each one's root GameObject under
    /// <see cref="SessionLifetime.TransientUi"/>. No keybinding, layout,
    /// copy, modal, pause, input-gate, or HUD-update logic is touched —
    /// every <c>Create(...)</c> call is byte-identical to the original
    /// monolith's, only regrouped into one place and one order.
    /// </summary>
    public sealed class PresentationInstaller
    {
        public void CreatePresentation(NebulaContext context, NebulaSessionLifetime lifetime)
        {
            Ship.ShipController ship = context.Ship;

            UI.Shell.NebulaUiShell shell = null;
            UI.Menu.NebulaParentMenuScreen parentMenuHandle = null;
            UI.Hud.NebulaHudContextBar contextBarHandle = null;
            UI.Hud.NebulaHudVitals vitalsHandle = null;
            UI.Hud.NebulaHudNavigation navigationHandle = null;
            UI.Hud.NebulaHudAlerts alertsHandle = null;
            UI.Hud.NebulaHudPlanetContext planetContextHandle = null;
            UI.Shell.NebulaConfirmationDialog confirmationHandle = null;
            UI.Menu.NebulaFleetPage fleetPageHandle = null;
            UI.Hud.NebulaHudNotifications notificationsHandle = null;

            shell = UI.Shell.NebulaUiShell.Create();
            lifetime.Track(SessionLifetime.TransientUi, shell.gameObject);

            // The strategic map is the sole owner of the M key. The fallback screen
            // that used to share it is gone, and the parent menu deliberately does
            // not bind M -- see NebulaParentMenuScreen.
            var strategicMapScreen = UI.StrategicMap.StrategicMapScreen.Create(context);
            lifetime.Track(SessionLifetime.TransientUi, strategicMapScreen.gameObject);

            // Same rule as the dev-tool panel below, and it was not being applied:
            // this is a diagnostic overlay by its own description -- manifest cache
            // counts, live GameObject counts, promotion timings -- and it was built
            // unconditionally, so a release player got a green debug readout in the
            // corner of the screen. Never created, not merely hidden.
            UI.SectorDebugHud sectorDebugHud = null;
            if (Debug.isDebugBuild)
            {
                sectorDebugHud = UI.SectorDebugHud.Create(context.Streaming);
                lifetime.Track(SessionLifetime.TransientUi, sectorDebugHud.gameObject);
            }

            // DT-001: dev-tool panel, Editor/Development Build only -- never
            // created (not merely hidden) in a release player.
            UI.FlightTestSpeedHud flightTestSpeedHud = null;
            if (Debug.isDebugBuild)
            {
                flightTestSpeedHud = UI.FlightTestSpeedHud.Create(ship);
                lifetime.Track(SessionLifetime.TransientUi, flightTestSpeedHud.gameObject);
            }

            // ANI-05.4: one panel animator per player-facing panel, all on the
            // director's unscaled channel so they still work while paused. Each
            // reads its own panel's real open state and never writes it; the
            // dev-only huds and the StrategicMap are deliberately excluded.
            Nebula.Visuals.NebulaAnimationDirector animation = context.AnimationDirector;

            if (shell != null)
            {
                var contextBar = UI.Hud.NebulaHudContextBar.Create(shell);
                lifetime.Track(SessionLifetime.TransientUi, contextBar.gameObject);

                var vitals = UI.Hud.NebulaHudVitals.Create(shell, ship.GetComponent<Ship.ShipCombat>());
                lifetime.Track(SessionLifetime.TransientUi, vitals.gameObject);

                var navigation = UI.Hud.NebulaHudNavigation.Create(shell, ship, context.NavigationDirector);
                lifetime.Track(SessionLifetime.TransientUi, navigation.gameObject);

                var alerts = UI.Hud.NebulaHudAlerts.Create(shell, ship.GetComponent<Ship.ShipCombat>(),
                    ship.GetComponent<Extraction.ShipExtraction>(), context.NavigationDirector);
                lifetime.Track(SessionLifetime.TransientUi, alerts.gameObject);

                // One tooltip for the whole shell. Every disabled control already
                // knew why it was disabled; nothing could show it.
                var tooltip = UI.Shell.NebulaTooltip.Create(shell);
                lifetime.Track(SessionLifetime.TransientUi, tooltip.gameObject);

                var confirmation = UI.Shell.NebulaConfirmationDialog.Create(shell);
                lifetime.Track(SessionLifetime.TransientUi, confirmation.gameObject);

                // Not part of the shell: it lives on the ship, in world space, and
                // is the one piece of ProgressionHud the notification line does not
                // carry. It would have gone silently when that class was deleted.
                // Without this the context bar has a Show and nothing that calls it:
                // created, animated, counted, and permanently empty.
                var extractionContext = UI.Hud.NebulaExtractionContextDriver.Create(
                    contextBar, ship.GetComponent<Extraction.ShipExtraction>(),
                    ship.GetComponent<Extraction.ShipPlanetEvacuation>());
                lifetime.Track(SessionLifetime.TransientUi, extractionContext.gameObject);

                var targeting = UI.Hud.NebulaHudTargeting.Create(shell, ship,
                    ship.GetComponent<Ship.ShipCombat>());
                lifetime.Track(SessionLifetime.TransientUi, targeting.gameObject);

                var levelTag = UI.Hud.NebulaShipLevelTag.Create(ship.transform);
                lifetime.Track(SessionLifetime.TransientUi, levelTag.gameObject);

                var notifications = UI.Hud.NebulaHudNotifications.Create(shell);
                lifetime.Track(SessionLifetime.TransientUi, notifications.gameObject);

                var planetContext = UI.Hud.NebulaHudPlanetContext.Create(shell,
                    ship.GetComponent<Extraction.ShipMaintenanceReserveAccess>(),
                    ship.GetComponent<Extraction.ShipExtraction>());
                lifetime.Track(SessionLifetime.TransientUi, planetContext.gameObject);

                var parentMenu = UI.Menu.NebulaParentMenuScreen.Create(shell);
                lifetime.Track(SessionLifetime.TransientUi, parentMenu.gameObject);
                var fleetPage = new UI.Menu.NebulaFleetPage(context, confirmation);

                parentMenu.AddPage(new UI.Menu.NebulaOverviewPage(context));
                // The map is handed off to, not hosted: see NebulaMapPage. It sits
                // second in the rail because that is its place in the enum order.
                parentMenu.AddPage(new UI.Menu.NebulaMapPage(context, shell));
                parentMenu.AddPage(new UI.Menu.NebulaProgressionPage(context, confirmation));
                parentMenu.AddPage(fleetPage);
                parentMenu.AddPage(new UI.Menu.NebulaPlanetsPage(context, confirmation));
                parentMenu.AddPage(new UI.Menu.NebulaResourcesPage(context));
                parentMenu.AddPage(new UI.Menu.NebulaCombatPage(context));
                parentMenu.AddPage(new UI.Menu.NebulaDiscoveryPage(context));
                // The shell goes in so the page can push ERİŞİLEBİLİRLİK as its own
                // screen from inside a session, the way the front door already can.
                parentMenu.AddPage(new UI.Menu.NebulaSettingsPage(context, owningShell: shell));
                parentMenu.AddPage(new UI.Menu.NebulaSessionPage(context, confirmation,
                    new UI.Session.NebulaSessionLauncher(
                        Object.FindObjectOfType<SolarSystemBootstrap>()), shell));

                // The legacy panels the redesign has taken over are switched off
                // rather than destroyed, so nothing is shown twice while their code
                // still exists for the UI-09 parity comparison.
                //
                // ShipHud only goes dark because its two controls moved rather than
                // vanished: throttle dragging is now on the flight cluster's bar, and
                // the centre reticle is authored art on the dynamic layer. Switching
                // it off before those existed would have removed a control, not
                // replaced one.
                // ANI-05.4: the redesigned panels get the same one-animator-per-panel
                // treatment the legacy ones had, on the director's unscaled channel so
                // they still work while the menu holds the game at timeScale zero.
                // Each reads its own panel's real open state and never writes it.
                AttachPanelAnimator(animation, contextBar.gameObject,
                    () => contextBar != null && contextBar.isActiveAndEnabled);
                AttachPanelAnimator(animation, vitals.gameObject,
                    () => vitals != null && vitals.isActiveAndEnabled);
                AttachPanelAnimator(animation, navigation.gameObject,
                    () => navigation != null && navigation.isActiveAndEnabled);
                AttachPanelAnimator(animation, alerts.gameObject,
                    () => alerts != null && alerts.isActiveAndEnabled);
                AttachPanelAnimator(animation, planetContext.gameObject,
                    () => planetContext != null && planetContext.isActiveAndEnabled);
                AttachPanelAnimator(animation, notifications.gameObject,
                    () => notifications != null && notifications.isActiveAndEnabled);
                AttachPanelAnimator(animation, targeting.gameObject,
                    () => targeting != null && targeting.isActiveAndEnabled);
                AttachPanelAnimator(animation, parentMenu.gameObject, () => parentMenu.IsOpen);
                AttachPanelAnimator(animation, confirmation.gameObject, () => confirmation.IsOpen);

                notificationsHandle = notifications;
                parentMenuHandle = parentMenu;
                contextBarHandle = contextBar;
                vitalsHandle = vitals;
                navigationHandle = navigation;
                alertsHandle = alerts;
                planetContextHandle = planetContext;
                confirmationHandle = confirmation;
                fleetPageHandle = fleetPage;

            }

            context.SetPresentation(new NebulaPresentationHandle(
                strategicMapScreen,
                sectorDebugHud, flightTestSpeedHud));

            // Recorded after the handle exists — SetPresentation runs below the
            // block above, so doing this inside it would have written to a null
            // handle and silently done nothing.
            if (shell != null)
            {
                context.Presentation.SetRedesignedUi(shell, parentMenuHandle, contextBarHandle,
                    vitalsHandle, navigationHandle, alertsHandle, planetContextHandle,
                    confirmationHandle, fleetPageHandle, notificationsHandle);
            }
        }

        /// <summary>
        /// Silences a legacy panel without destroying it. Disabling the Canvas
        /// rather than the GameObject keeps the component's Update running, so a
        /// parity test can still read what the old panel <i>would</i> have shown
        /// while the player sees only the new one.
        /// </summary>
        private static void DisableLegacyPanel(GameObject panel)
        {
            if (panel == null) return;
            var canvas = panel.GetComponent<Canvas>();
            if (canvas != null) canvas.enabled = false;
        }

        /// <summary>
        /// Gives one panel its animator, exactly once. A panel that already has
        /// one keeps it, so a rebuilt presentation layer never stacks a second.
        /// </summary>
        /// <summary>
        /// Player-facing panels carrying a <c>UiPanelAnimator</c>. Named here rather
        /// than counted in the tests, so adding a panel without animating it fails
        /// loudly instead of quietly moving a magic number. The dev-only huds and
        /// the strategic map are deliberately excluded.
        /// </summary>
        public const int AnimatedPanelCount = 9;

        private static void AttachPanelAnimator(Nebula.Visuals.NebulaAnimationDirector director,
            GameObject panel, System.Func<bool> openPredicate)
        {
            if (director == null || panel == null) return;
            if (panel.GetComponent<Nebula.Visuals.UiPanelAnimator>() != null) return;

            panel.AddComponent<Nebula.Visuals.UiPanelAnimator>().Initialize(director, openPredicate);
        }
    }
}
