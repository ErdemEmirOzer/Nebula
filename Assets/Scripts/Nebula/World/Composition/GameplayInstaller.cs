using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Composition step 9: <see cref="ExtractionDirector"/> creation/save
    /// wiring, dominance, and the logistics/route directors. Save callbacks
    /// are wired through the exact same single delegate
    /// (<c>() =&gt; extractionDirector.SaveNow(null)</c>) every save-capable
    /// ship component used before — no new save path, no new DTO, no change
    /// to autosave timing or transaction behavior.
    /// </summary>
    public sealed class GameplayInstaller
    {
        public void WireGameplay(NebulaContext context, NebulaSessionLifetime lifetime, int resolvedSeed)
        {
            var extractionDirectorGO = new GameObject("Extraction Director");
            lifetime.Track(SessionLifetime.CampaignSession, extractionDirectorGO);
            var extractionDirector = extractionDirectorGO.AddComponent<ExtractionDirector>();
            extractionDirector.Initialize(context.Ship, resolvedSeed);

            ShipHangar hangar = context.Hangar;
            ShipModules modules = context.Modules;
            modules.RequestSave = () => extractionDirector.SaveNow(null);
            hangar.RequestSave = () => extractionDirector.SaveNow(null);
            extractionDirector.Hangar = hangar;
            extractionDirector.Streaming = context.Streaming;

            var shipQuickPackage = context.Ship.GetComponent<ShipQuickAssetPackage>();
            if (shipQuickPackage != null) shipQuickPackage.RequestSave = () => extractionDirector.SaveNow(null);
            var shipEvacuation = context.Ship.GetComponent<ShipPlanetEvacuation>();
            if (shipEvacuation != null) shipEvacuation.RequestSave = () => extractionDirector.SaveNow(null);

            var dominanceGO = new GameObject("Dominance System");
            lifetime.Track(SessionLifetime.CampaignSession, dominanceGO);
            dominanceGO.AddComponent<DominanceSystem>();

            // VS-011: proximity build/upgrade/transfer interaction + the central dock/holding orchestrator.
            context.Ship.GetComponent<ShipLogisticsCenter>().Initialize(context.Streaming);

            var routeDirectorGO = new GameObject("Collector Route Director");
            lifetime.Track(SessionLifetime.CampaignSession, routeDirectorGO);
            var routeDirector = routeDirectorGO.AddComponent<CollectorRouteDirector>();
            routeDirector.Initialize(hangar);

            // VS-015 §5/§6: Maintenance Reserve auto-policies + the same-system intervention group.
            var interventionDirectorGO = new GameObject("Intervention Group Director");
            lifetime.Track(SessionLifetime.CampaignSession, interventionDirectorGO);
            var interventionDirector = interventionDirectorGO.AddComponent<InterventionGroupDirector>();
            interventionDirector.Initialize(hangar);
            context.Streaming.InterventionGroupDirector = interventionDirector;

            context.SetExtractionDirector(extractionDirector);
            context.SetCollectorRouteDirector(routeDirector);
            context.SetInterventionGroupDirector(interventionDirector);

            // VS-013: campaign-wide First Probe warning/soft-lock timers.
            Combat.CombatCampaignDirector combatCampaignDirector = Combat.CombatCampaignDirector.Create(context.Ship);
            lifetime.Track(SessionLifetime.CampaignSession, combatCampaignDirector.gameObject);
            context.SetCombatCampaignDirector(combatCampaignDirector);

            // VS-013.2: wire the single shared Safety Marker authority into every
            // consumer — ShipCombat's physical repair safe-zone check and
            // ShipEmergencyRecovery's final defeat-recovery fallback.
            context.Ship.GetComponent<Ship.ShipCombat>().SetSafetyMarker(context.SafetyMarkerAnchor);
            context.Ship.GetComponent<Ship.ShipEmergencyRecovery>().SetSafetyMarker(context.SafetyMarkerAnchor);

            // VS-013 §7 / VS-013.2: placeholder holographic training area, positioned
            // via the same existing nearby-safe offset from the Safety Marker anchor
            // (no change to training mechanics — only the base point moved off the
            // star/system center onto the real Safety Marker authority).
            Vector3 trainingPosition = context.SafetyMarkerAnchor.transform.position + new Vector3(3000f, 400f, 3000f);
            Ship.TrainingArea trainingArea = Ship.TrainingArea.Create(trainingPosition, context.Ship);
            lifetime.Track(SessionLifetime.CampaignSession, trainingArea.gameObject);
            context.SetTrainingArea(trainingArea);
        }
    }
}
