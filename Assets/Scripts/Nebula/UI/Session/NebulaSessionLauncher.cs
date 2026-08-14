using UnityEngine;
using Nebula.Extraction;
using Nebula.World;

namespace Nebula.UI.Session
{
    /// <summary>
    /// Turns main-menu intent into session lifecycle calls on
    /// <see cref="SolarSystemBootstrap"/>.
    /// <para>
    /// This exists so the menu never holds the bootstrap itself. A menu that can
    /// reach <c>NebulaBootstrap</c> is a menu that can create a universe as a side
    /// effect of being drawn; routing every entry point through one small adapter
    /// keeps "describe a campaign" and "enter a campaign" as separate acts.
    /// </para>
    /// </summary>
    public sealed class NebulaSessionLauncher : INebulaSessionLauncher
    {
        private readonly SolarSystemBootstrap bootstrap;

        /// <summary>The slot a resumed session should restore, or -1 for a fresh campaign.</summary>
        public int PendingLoadSlot { get; private set; } = -1;

        /// <summary>Test-facing: how many sessions this launcher has started.</summary>
        public int SessionsStarted { get; private set; }

        /// <summary>Test-facing: set when <see cref="QuitGame"/> ran, so tests need not kill the process.</summary>
        public bool QuitRequested { get; private set; }

        public NebulaSessionLauncher(SolarSystemBootstrap solarSystemBootstrap)
        {
            bootstrap = solarSystemBootstrap;
        }

        /// <summary>
        /// A brand new campaign. The identity is cleared first so the fresh session
        /// cannot inherit the previous campaign's id — the failure that would make
        /// two unrelated campaigns share telemetry and a save lineage.
        /// </summary>
        public void StartNewCampaign()
        {
            PendingLoadSlot = -1;
            CampaignIdentity.StartNewCampaign();
            Launch();
        }

        /// <summary>
        /// Resumes a slot. The slot is recorded before the session starts so the
        /// load path can pick it up during composition rather than the session
        /// booting a default universe and being overwritten a frame later.
        /// </summary>
        public void ContinueFromSlot(int slot)
        {
            PendingLoadSlot = slot;
            Launch();
        }

        public void QuitGame()
        {
            QuitRequested = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Test-facing: the slot the last launch actually restored, or -1 when the
        /// launch started a fresh campaign.
        /// </summary>
        public int LoadedSlot { get; private set; } = -1;

        private void Launch()
        {
            if (bootstrap == null) return;
            Nebula.World.Composition.NebulaContext context = bootstrap.BeginSession();
            SessionsStarted++;

            // Continue used to end here. PendingLoadSlot was written by every entry
            // point and read by nothing, so "Devam Et" booted a brand new universe
            // and the player's campaign was silently gone -- saved at level 18,
            // resumed at level 1. The slot has to be applied and the load actually
            // run, after composition, or there is no director to load into.
            LoadedSlot = -1;
            if (PendingLoadSlot < 0) return;

            Extraction.ExtractionDirector director = context?.ExtractionDirector;
            if (director == null) return;

            director.ActiveSlot = PendingLoadSlot;
            director.LoadNow();
            LoadedSlot = PendingLoadSlot;
        }

        /// <summary>
        /// Disposes the running session and starts a genuinely new one.
        /// <para>
        /// <c>bootstrap.RestartCampaign()</c> rather than <see cref="StartNewCampaign"/>:
        /// the latter reaches <c>BeginSession</c>, which returns the existing context
        /// when one is already live, so restarting from inside a campaign was a no-op
        /// that still reported success.
        /// </para>
        /// </summary>
        public void RestartCampaign()
        {
            PendingLoadSlot = -1;
            LoadedSlot = -1;
            CampaignIdentity.StartNewCampaign();
            bootstrap?.RestartCampaign();
            SessionsStarted++;
        }

        /// <summary>Tears the running session down and returns to a menu-capable state.</summary>
        public void ReturnToMainMenu()
        {
            PendingLoadSlot = -1;
            bootstrap?.EndSession();
        }
    }
}
