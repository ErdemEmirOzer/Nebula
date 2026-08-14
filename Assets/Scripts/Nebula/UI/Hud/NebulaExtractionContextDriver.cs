using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// Feeds the HUD context bar from the live extraction state.
    /// <para>
    /// <see cref="NebulaExtractionContext"/> was written as the replacement for
    /// <c>ExtractionHud.BuildText</c> and tested thoroughly — but nothing ever
    /// called it outside the tests, and <see cref="NebulaHudContextBar"/> has a
    /// <c>Show</c> and no <c>Update</c>. So the bar was created, animated and
    /// counted while staying permanently empty, and deleting <c>ExtractionHud</c>
    /// left the core loop — scan, drill, transfer, evacuate — with no interface at
    /// all. This is the missing wire.
    /// </para>
    /// <para>
    /// The split is deliberate: the pure function decides what to show and this
    /// decides when. Reading state is cheap, so it runs on the same 0.25s cadence
    /// the other clusters use rather than every frame, and the list it hands over
    /// is a field so a refresh allocates nothing.
    /// </para>
    /// </summary>
    public sealed class NebulaExtractionContextDriver : MonoBehaviour
    {
        /// <summary>Seconds between reads. The bar's own animation is unaffected.</summary>
        public const float RefreshSeconds = 0.25f;

        private NebulaHudContextBar bar;
        private ShipExtraction extraction;
        private ShipPlanetEvacuation evacuation;

        /// <summary>
        /// Resolved off the ship the extraction driver already belongs to rather than
        /// passed in, so the composition installer does not have to learn about it.
        /// A sibling component lookup on an object we are holding — never a scene scan.
        /// </summary>
        private ShipPoi poi;

        private readonly List<HudAction> secondaries = new List<HudAction>(4);
        private float timer;

        /// <summary>Test-facing: refreshes that actually reached the bar.</summary>
        public int RefreshesShown { get; private set; }

        /// <summary>Test-facing: refreshes that hid the bar because nothing applied.</summary>
        public int RefreshesHidden { get; private set; }

        public static NebulaExtractionContextDriver Create(NebulaHudContextBar contextBar,
            ShipExtraction shipExtraction, ShipPlanetEvacuation shipEvacuation)
        {
            var go = new GameObject("HUD Extraction Context");
            go.transform.SetParent(contextBar.transform.parent, false);

            var driver = go.AddComponent<NebulaExtractionContextDriver>();
            driver.bar = contextBar;
            driver.extraction = shipExtraction;
            driver.evacuation = shipEvacuation;
            driver.poi = shipExtraction != null ? shipExtraction.GetComponent<ShipPoi>() : null;
            contextBar.ActionRequested += driver.OnActionRequested;
            return driver;
        }

        private void OnDestroy()
        {
            if (bar != null) bar.ActionRequested -= OnActionRequested;
        }

        /// <summary>
        /// One switch, one seam per action, and never a second copy of the rule.
        /// <para>
        /// Every branch calls the same public method the matching key calls through
        /// <c>NebulaInputRouter</c>, which is the whole contract this bar exists to
        /// satisfy: the click and the keystroke are the same command, so they cannot
        /// disagree about eligibility, cost or refusal text. Nothing here decides
        /// anything — an unknown id is ignored rather than guessed at.
        /// </para>
        /// </summary>
        private void OnActionRequested(string actionId)
        {
            switch (actionId)
            {
                case NebulaInputActions.ExtractionConfirm:
                    extraction?.TryBuildOrUpgradeDrill();
                    break;
                case NebulaInputActions.ExtractionLaser:
                    extraction?.TryBuildOrUpgradeDefense();
                    break;
                case NebulaInputActions.ExtractionCommand:
                    extraction?.TryBuildOrUpgradeCommand();
                    break;
                case NebulaInputActions.ExtractionScan:
                    extraction?.ToggleScanLatch();
                    break;
                case NebulaInputActions.ExtractionTransfer:
                    extraction?.ToggleTransferLatch();
                    break;
                case NebulaInputActions.ExtractionBuildMode:
                    extraction?.ToggleBuildMode();
                    break;
                case NebulaInputActions.PoiStart:
                    poi?.TryStartPrimaryAction();
                    break;
                case NebulaInputActions.PoiSensorRelay:
                    poi?.TryStartSensorRelay();
                    break;
                case NebulaInputActions.PoiCancel:
                    poi?.TryCancelActiveOperation();
                    break;
                default:
                    return;
            }
            Refresh();
        }

        private void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = RefreshSeconds;
            Refresh();
        }

        /// <summary>Callable directly so a test never has to wait out the timer.</summary>
        public void Refresh()
        {
            if (bar == null) return;

            if (extraction == null || extraction.TargetPlanet == null)
            {
                // No planet does not mean nothing to do. A point of interest is the
                // other thing the ship can be physically standing at, and its two
                // commitments -- start the operation, lock the Sensor Relay choice --
                // had no control anywhere in the interface at all.
                if (ShowPoi()) return;

                bar.Hide();
                RefreshesHidden++;
                return;
            }

            secondaries.Clear();
            ExtractionContextState state = ReadState();
            HudAction primary = NebulaExtractionContext.Build(state, null, secondaries,
                out string context);

            bar.Show(context, primary, secondaries);
            RefreshesShown++;
        }

        /// <summary>
        /// The POI face of the bar. Returns false when there is no POI worth a row, so
        /// the caller can fall through to hiding.
        /// <para>
        /// Kept beside the planet path rather than in its own component because it is
        /// the same question — "what can the player do where they are standing" — and
        /// two components answering it would race for the one bar.
        /// </para>
        /// </summary>
        private bool ShowPoi()
        {
            PoiObject target = poi != null ? poi.TargetPoi : null;
            if (target == null || target.Manifest == null || target.State == null) return false;

            secondaries.Clear();
            HudAction primary = NebulaPoiContext.Build(poi, secondaries, out string context);
            if (!primary.IsValid && secondaries.Count == 0) return false;

            bar.Show(context, primary, secondaries);
            RefreshesShown++;
            return true;
        }

        /// <summary>
        /// The live snapshot, read through <see cref="NebulaExtractionContext.Capture"/>
        /// and nothing else.
        /// <para>
        /// This method used to assemble its own <see cref="ExtractionContextState"/>
        /// field by field, alongside <c>Capture</c> doing the same job — two captures of
        /// one fact, and they had already drifted. The hand-built one never set
        /// <c>StationConstructionGateMet</c>, so it defaulted to <c>false</c> and the
        /// drill's T2 verb was disabled on the bar <b>even with the module tier met</b>:
        /// the upgrade was unreachable from the interface for a player who had actually
        /// bought the gate. It also never filled the upgrade cost or the Command
        /// signpost, so the bar priced a T2 as blank and never offered Komuta at all.
        /// Evacuation is layered on afterwards because it is the one thing
        /// <c>Capture</c> cannot see -- it belongs to another component.
        /// </para>
        /// </summary>
        private ExtractionContextState ReadState()
        {
            ExtractionContextState state = NebulaExtractionContext.Capture(extraction);
            ReadEvacuation(ref state);
            return state;
        }

        /// <summary>
        /// The armed window is the reason this exists at all: the first press does
        /// nothing visible on its own, and for a while nothing on screen said the
        /// ship was one keystroke from abandoning a planet for good.
        /// </summary>
        private void ReadEvacuation(ref ExtractionContextState state)
        {
            if (evacuation == null) return;

            PlanetEvacuationTransaction tx = evacuation.ActiveTransaction;
            if (tx != null && !tx.Finalized && !tx.Cancelled)
            {
                state.EvacuationPending = true;
                state.EvacuationPendingVehicles = tx.PendingVehicleIds.Count;
                return;
            }

            state.PlanetAbandoned = extraction.TargetPlanet != null
                && ExtractionState.IsPlanetAbandoned(extraction.TargetPlanet.WorldId);
            state.EvacuationArmed = evacuation.ArmedForConfirmation;

            if (state.PlanetAbandoned)
                state.EvacuationBlockedReason = evacuation.CanReclaim(out ReclaimRejectReason reclaim)
                    ? null : ShipPlanetEvacuation.RejectMessage(reclaim);
            else
                state.EvacuationBlockedReason = evacuation.CanEvacuate(out EvacuationRejectReason evacuate)
                    ? null : ShipPlanetEvacuation.RejectMessage(evacuate);
        }

    }
}
