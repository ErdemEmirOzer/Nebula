using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Ship
{
    /// <summary>
    /// Defeat-recovery orchestration (VS-013 §8): resolves the recovery
    /// point priority, applies the exact recovery/soft-lock fractions,
    /// computes+spawns the physical cargo-loss wreckage (or skips it under
    /// soft-lock), and repositions the ship. This IS the one approved
    /// defeat-recovery transition ("no long tow simulation, no open-world
    /// jump" means no NEW general-purpose teleport feature is added —
    /// repositioning the ship here is the explicitly approved exception,
    /// the same category of instant reposition <see cref="Extraction.ExtractionDirector.LoadNow"/>
    /// already performs on load).
    /// </summary>
    public class ShipEmergencyRecovery : MonoBehaviour
    {
        private ShipController ship;
        private ShipCombat combat;
        private Extraction.ShipLogistics logistics;
        private SafetyMarkerAnchor safetyMarker;

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            combat = GetComponent<ShipCombat>();
            logistics = GetComponent<Extraction.ShipLogistics>();
        }

        /// <summary>VS-013.2: wires the single shared Safety Marker authority in as the final defeat-recovery fallback position (replaces the old star-center fallback).</summary>
        public void SetSafetyMarker(SafetyMarkerAnchor anchor) => safetyMarker = anchor;

        public void PerformRecovery()
        {
            bool nothingToLose = ComputeNothingToLose();
            bool mercyForRepeats = CombatCampaignState.Recovery.ShouldApplyMercyForRepeatedDefeats();
            bool softLock = nothingToLose || mercyForRepeats;

            CombatCampaignState.Recovery.RecordDefeat();

            // Captured BEFORE any reposition/re-materialization below -- the wreckage
            // belongs to the system the defeat actually happened in, never the
            // recovery destination.
            CombatEncounterDirector defeatSystemDirector = combat.Director;

            CombatWreckageState wreck = null;
            if (!softLock && logistics != null)
            {
                Dictionary<string, float> loss = DefeatCargoLossResolver.ComputeLoss(NormalUnreservedCargoSnapshot());
                if (loss.Count > 0)
                {
                    wreck = new CombatWreckageState(CurrentSystemId() + "/combat-wreck.defeat." + System.Guid.NewGuid().ToString("N").Substring(0, 6));
                    foreach (var kv in loss)
                    {
                        wreck.Nodes.Add(new CombatWreckageNode(kv.Key, kv.Value));
                        logistics.Cargo.TryConsumeImmediate(kv.Key, kv.Value);
                    }
                }
            }

            Vector3 recoveryAbsolutePosition = ResolveRecoveryPosition();
            ship.ReleaseStabilize();
            ship.transform.position = recoveryAbsolutePosition - FloatingOrigin.TotalOffset;
            ship.SetVelocity(Vector3.zero);

            // If recovery moved the ship into a different system than the one currently
            // Full-materialized, force an immediate re-materialization at the new position
            // -- the same approach ExtractionDirector.LoadNow already uses after its own
            // instant reposition, rather than waiting on the gradual per-frame promotion/
            // demotion loop to notice on its own.
            if (StarSystemRegistry.TryFindContaining(recoveryAbsolutePosition, out StarSystemRecord landed)
                && StarSystemRegistry.Streaming != null && StarSystemRegistry.Streaming.FullSystemId != landed.SystemId)
            {
                SystemManifest landedManifest = StarSystemRegistry.Manifests?.ResolveSystem(landed.SystemId);
                if (landedManifest != null) StarSystemRegistry.Streaming.ResetAndBootFull(landedManifest);
            }

            float hullFraction = softLock ? RecoveryResolver.SoftLockHullFraction : RecoveryResolver.RecoveryHullFraction;
            float armorFraction = softLock ? RecoveryResolver.SoftLockArmorFraction : RecoveryResolver.RecoveryArmorFraction;
            combat.State.RecoverToFractions(hullFraction, armorFraction, RecoveryResolver.RecoveryShieldFraction);

            if (wreck != null) defeatSystemDirector?.Wreckage.Add(wreck);
        }

        private bool ComputeNothingToLose()
        {
            if (ExtractionState.HasAnyControlledPlanet()) return false;
            foreach (DrillStructure d in ExtractionState.Drills)
                if (d != null && d.Runtime.State == DrillLifecycle.Active) return false;
            if (logistics != null)
                foreach (var kv in logistics.Cargo.Items)
                    if (logistics.Cargo.GetAvailable(kv.Key) > 0.0001f) return false;
            return true;
        }

        private IEnumerable<KeyValuePair<string, float>> NormalUnreservedCargoSnapshot()
        {
            if (logistics == null) yield break;
            foreach (var kv in logistics.Cargo.Items)
            {
                float available = logistics.Cargo.GetAvailable(kv.Key);
                if (available > 0.0001f) yield return new KeyValuePair<string, float>(kv.Key, available);
            }
        }

        private string CurrentSystemId() =>
            StarSystemRegistry.TryFindContaining(ship.transform.position + FloatingOrigin.TotalOffset, out StarSystemRecord record) ? record.SystemId : null;

        private Vector3 ResolveRecoveryPosition()
        {
            string currentSystemId = CurrentSystemId();
            bool hasCommandHere = currentSystemId != null && SystemStatusQuery.HasActiveCommandAnywhere(currentSystemId);
            bool hasLastVisited = CombatCampaignState.Recovery.LastVisitedControlledSystemId != null;

            RecoveryPointKind kind = RecoveryResolver.Resolve(hasCommandHere, hasLastVisited);
            switch (kind)
            {
                case RecoveryPointKind.ActiveCommandCenterPlanet:
                    return NearestActiveCommandPlanetPosition(currentSystemId) ?? SafetyMarkerPosition();
                case RecoveryPointKind.LastVisitedControlledSystem:
                    return SystemCenter(CombatCampaignState.Recovery.LastVisitedControlledSystemId) ?? SafetyMarkerPosition();
                default:
                    return SafetyMarkerPosition();
            }
        }

        private Vector3? NearestActiveCommandPlanetPosition(string systemId)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return null;
            Vector3 shipAbs = ship.transform.position + FloatingOrigin.TotalOffset;
            PlanetManifest best = null; float bestSqr = float.MaxValue;
            foreach (PlanetManifest p in manifest.Planets)
            {
                if (!ExtractionState.HasActiveCommand(p.PlanetId)) continue;
                Vector3 approx = manifest.AbsoluteCenter; // planet orbital position varies continuously; the star center is a safe, always-valid recovery anchor for the system
                float sqr = (approx - shipAbs).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            return best != null ? manifest.AbsoluteCenter : (Vector3?)null;
        }

        private Vector3? SystemCenter(string systemId)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            return manifest?.AbsoluteCenter;
        }

        /// <summary>VS-013.2: the real Safety Marker anchor's absolute position — never the star/system center. Falls back to the old start-system-center behavior only in the defensive case composition somehow left the anchor unwired.</summary>
        private Vector3 SafetyMarkerPosition()
        {
            if (safetyMarker != null) return safetyMarker.transform.position + FloatingOrigin.TotalOffset;
            string startSystemId = StarSystemRegistry.StartSystemId;
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(startSystemId);
            return manifest?.AbsoluteCenter ?? Vector3.zero;
        }
    }
}
