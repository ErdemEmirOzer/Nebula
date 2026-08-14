using UnityEngine;
using Nebula.Telemetry;

namespace Nebula.World.Composition
{
    /// <summary>
    /// VS-017A.1 §1: composition step 9.5 — wires the single <see cref="TelemetryGameplayBridge"/>
    /// through typed <see cref="NebulaContext"/> references only (no FindObjectOfType, no
    /// name-based lookup, no second telemetry authority). Runs after GameplayInstaller and
    /// NavigationInstaller so every reference the bridge subscribes to (Hangar, Modules,
    /// Streaming, NavigationDirector) already exists. The bridge GameObject is tracked as a
    /// CampaignSession lifetime object, so a restart destroys the old one (unsubscribing
    /// everything in its own OnDestroy) before this step creates a fresh one — never two
    /// live subscribers at once.
    /// </summary>
    public sealed class TelemetryInstaller
    {
        public void WireTelemetry(NebulaContext context, NebulaSessionLifetime lifetime)
        {
            // VS-017A.2 §A2: telemetry only ever CONSUMES the campaign identity that
            // gameplay/save owns (CampaignIdentity, minted by WorldInstaller for a genuinely
            // new campaign and adopted verbatim by a load). It never mints one of its own and
            // never carries a previous campaign's id into a new campaign.
            TelemetryService.StartSession(Extraction.CampaignIdentity.Current);

            var bridgeObject = new GameObject("TelemetryGameplayBridge");
            lifetime.Track(SessionLifetime.CampaignSession, bridgeObject);
            var bridge = bridgeObject.AddComponent<TelemetryGameplayBridge>();
            bridge.Initialize(context);
            context.SetTelemetryBridge(bridge);
        }
    }
}
