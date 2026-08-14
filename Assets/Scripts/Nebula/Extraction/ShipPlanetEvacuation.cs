using System;
using UnityEngine;
using Nebula.Hangar;
using Nebula.Ship;

namespace Nebula.Extraction
{
    /// <summary>
    /// MonoBehaviour adapter for physical planet evacuation and its reverse,
    /// reclaim (VS-010 §5). A planet-level, explicitly-approved transaction:
    /// locks new submissions, resolves in-flight builds with the existing
    /// cancel/refund rules, physically recalls every vehicle bound to the
    /// planet and waits for them to actually dock before passivating —
    /// nothing is deleted, nothing already-built is re-charged.
    /// </summary>
    public class ShipPlanetEvacuation : MonoBehaviour
    {
        public const KeyCode EvacuateKey = KeyCode.V;
        public const KeyCode ReclaimKey = KeyCode.C;

        private ShipController ship;
        private ShipExtraction extraction;
        private ShipLogistics logistics;
        private ShipHangar hangar;

        public Action RequestSave { get; set; }

        public PlanetEvacuationTransaction ActiveTransaction { get; private set; }
        public string StatusMessage { get; private set; }
        private float messageUntil;

        private const float ArmedSeconds = 3f;
        private bool armed;
        private float armedUntil;
        /// <summary>True while a first Evacuate press is waiting on the confirming second press (irreversible-action UI, VS-010 §6).</summary>
        public bool ArmedForConfirmation => armed && Time.time < armedUntil;

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            extraction = GetComponent<ShipExtraction>();
            logistics = GetComponent<ShipLogistics>();
            hangar = GetComponent<ShipHangar>();
        }

        /// <summary>
        /// When the Evacuation context is listening — deliberately the narrowest
        /// condition that still covers both verbs.
        /// <para>
        /// C is the whole reason this exists. Down-thrust reads C every frame the ship
        /// is flown, and reclaim reads it too; both used to sample it raw, so one press
        /// at an abandoned planet dropped the nose <b>and</b> reclaimed the world. The
        /// fix is arbitration, and arbitration is only honest if this claim is as small
        /// as the verb it protects: reclaim can only land on a planet that has actually
        /// been abandoned, so that — plus the three-second confirm window — is exactly
        /// when the context is live. Everywhere else C stays flight's, which is
        /// everywhere the player is simply flying.
        /// </para>
        /// <para>
        /// Asked rather than pushed, so the answer is right on the frame the planet
        /// changes regardless of which component's <c>Update</c> ran first.
        /// </para>
        /// </summary>
        private bool EvacuationContextLive
        {
            get
            {
                if (!isActiveAndEnabled || InputGate.Suppressed || CombatInputContext.WorldInteractionBlocked) return false;
                PlanetIdentity planet = extraction != null ? extraction.TargetPlanet : null;
                if (planet == null) return false;
                return ArmedForConfirmation || ExtractionState.IsPlanetAbandoned(planet.WorldId);
            }
        }

        private void OnEnable() =>
            NebulaInputRouter.SetContextSource(InputContext.Evacuation, () => EvacuationContextLive);

        /// <summary>Withdraws the claim. A context left standing would keep taking C from down-thrust with nothing behind it.</summary>
        private void OnDisable() =>
            NebulaInputRouter.SetContextSource(InputContext.Evacuation, null);

        private void Update()
        {
            if (InputGate.Suppressed || CombatInputContext.WorldInteractionBlocked) return;
            // Through the router, never Input: V is uncontested and simply reads its
            // catalog key, while C is arbitrated against flight.vertical_down and only
            // answers here when this context outranks flight on that frame.
            if (NebulaInputRouter.Pressed(NebulaInputActions.EvacuationEvacuate)) HandleEvacuateKey();
            if (NebulaInputRouter.Pressed(NebulaInputActions.EvacuationReclaim)) TryReclaim();

            if (ActiveTransaction != null && !ActiveTransaction.Finalized && !ActiveTransaction.Cancelled) TickPending();
        }

        /// <summary>Two-press confirm: the first press only arms a 3s window and shows the preview/warning; a second press within that window actually evacuates. Any other key or a stale window clears the arm.</summary>
        private void HandleEvacuateKey()
        {
            if (ArmedForConfirmation)
            {
                armed = false;
                TryBeginEvacuation();
                return;
            }

            armed = false;
            if (!CanEvacuate(out EvacuationRejectReason reason)) { SetMessage(RejectMessage(reason)); return; }
            armed = true;
            armedUntil = Time.time + ArmedSeconds;
            // Turkish, and no key named in it: Tahliye is a catalog row and the HUD
            // prints its current prompt, so a literal "V" here would be wrong for
            // anyone who moved it.
            SetMessage("ONAYLAMAK İÇİN TEKRAR BASIN — GERİ ALINAMAZ");
        }

        // --- Evacuation --------------------------------------------------------------
        public bool CanEvacuate(out EvacuationRejectReason reason)
        {
            reason = EvacuationRejectReason.None;
            PlanetIdentity planet = extraction != null ? extraction.TargetPlanet : null;
            if (planet == null) { reason = EvacuationRejectReason.NotInRange; return false; }

            bool alreadyInProgress = ActiveTransaction != null && !ActiveTransaction.Finalized && !ActiveTransaction.Cancelled;
            bool alreadyAbandoned = ExtractionState.IsPlanetAbandoned(planet.WorldId);
            float altitude = extraction.Altitude(planet);
            float rangeLimit = planet.Body.Radius * extraction.ScannerProximityR;
            bool inRange = altitude <= rangeLimit;
            bool stabilized = ship.IsStabilized;
            bool contested = ExtractionState.GetDominance(planet.WorldId).State == DominanceState.Contested;

            reason = PlanetEvacuationService.CheckPreconditions(alreadyInProgress, alreadyAbandoned, inRange, stabilized,
                threatDetected: false, planetContested: contested);
            return reason == EvacuationRejectReason.None;
        }

        /// <summary>
        /// Why the action was refused, in the player's language. This string is
        /// shown verbatim on the planets page and in the HUD's context bar, both of
        /// which are Turkish — it was the only English text left on either.
        /// </summary>
        public static string RejectMessage(EvacuationRejectReason reason) => reason switch
        {
            EvacuationRejectReason.AlreadyInProgress => "TAHLİYE ZATEN SÜRÜYOR",
            EvacuationRejectReason.AlreadyAbandoned => "GEZEGEN ZATEN TERK EDİLMİŞ",
            EvacuationRejectReason.NotInRange => "DAHA YAKINA GEL",
            EvacuationRejectReason.NotStabilized => "YÖRÜNGE SABİTLEMEYİ AÇ",
            EvacuationRejectReason.ThreatDetected => "ETKİN TEHDİT ALGILANDI",
            EvacuationRejectReason.PlanetContested => "GEZEGEN ÇEKİŞMELİ",
            _ => null
        };

        /// <summary>Explicit, irreversible-consequence action — the UI must have already shown the result/refund/vehicle/cargo preview and gotten player confirmation before calling this.</summary>
        public bool TryBeginEvacuation()
        {
            if (!CanEvacuate(out EvacuationRejectReason reason)) { SetMessage(RejectMessage(reason)); return false; }

            PlanetIdentity planet = extraction.TargetPlanet;
            string worldId = planet.WorldId;
            LogisticsCargo cargo = logistics != null ? logistics.Cargo : null;

            // Step 1: lock new capsule/job submissions on this planet.
            ExtractionState.SetEvacuationLocked(worldId, true);

            // Step 2: resolve in-flight builds with the existing cancel/refund rules.
            PlanetEvacuationService.ResolveActiveBuilds(worldId, cargo);

            // Step 4/5: auto-transfer what fits; whatever doesn't stays on the planet automatically.
            PlanetEvacuationService.AutoTransferResources(worldId, cargo);

            var tx = new PlanetEvacuationTransaction { PlanetWorldId = worldId };

            // Step 3: physically recall every vehicle bound to this planet; don't finalize until they actually dock.
            if (hangar != null)
            {
                foreach (VehicleAgent v in hangar.Vehicles)
                {
                    if (v == null || v.Mission.TargetId != worldId) continue;
                    if (v.Mission.Phase == VehiclePhase.InHangar) continue;
                    tx.PendingVehicleIds.Add(v.VehicleId);
                    hangar.Recall(v);
                }
            }

            ActiveTransaction = tx;
            if (tx.AllVehiclesReturned) FinalizeEvacuation(tx);
            else SetMessage($"EVACUATION PENDING — WAITING FOR {tx.PendingVehicleIds.Count} VEHICLE(S) TO RETURN");

            RequestSave?.Invoke();
            return true;
        }

        private void TickPending()
        {
            PlanetEvacuationTransaction tx = ActiveTransaction;
            if (hangar != null)
            {
                for (int i = tx.PendingVehicleIds.Count - 1; i >= 0; i--)
                {
                    string vehicleId = tx.PendingVehicleIds[i];
                    VehicleAgent v = FindVehicle(vehicleId);
                    // A vehicle that vanished from the roster (shouldn't happen — never destroyed
                    // outside a full load) or made it back to the hangar clears its pending slot.
                    if (v == null || v.Mission.Phase == VehiclePhase.InHangar) tx.PendingVehicleIds.RemoveAt(i);
                }
            }

            if (tx.AllVehiclesReturned) FinalizeEvacuation(tx);
        }

        private VehicleAgent FindVehicle(string vehicleId)
        {
            if (hangar == null) return null;
            foreach (VehicleAgent v in hangar.Vehicles) if (v != null && v.VehicleId == vehicleId) return v;
            return null;
        }

        private void FinalizeEvacuation(PlanetEvacuationTransaction tx)
        {
            // Step 6: passivate — never deletes structure/node/durability/inventory (step 7).
            PlanetEvacuationService.Passivate(tx.PlanetWorldId);
            ExtractionState.SetEvacuationLocked(tx.PlanetWorldId, false);
            tx.Finalized = true;
            SetMessage("TAHLİYE TAMAMLANDI — GEZEGEN TERK EDİLDİ");
            RequestSave?.Invoke();
        }

        // --- Reclaim -------------------------------------------------------------
        public bool CanReclaim(out ReclaimRejectReason reason)
        {
            reason = ReclaimRejectReason.None;
            PlanetIdentity planet = extraction != null ? extraction.TargetPlanet : null;
            if (planet == null) { reason = ReclaimRejectReason.NotAbandoned; return false; }

            bool abandoned = ExtractionState.IsPlanetAbandoned(planet.WorldId);
            bool hasActiveCommandIgnoringAbandon = ExtractionState.FindStructure(planet.WorldId, SurfaceStructureType.Command)?.Runtime.IsActive ?? false;
            bool hasActiveDefenseIgnoringAbandon = CountActiveDefenseIgnoringAbandon(planet.WorldId) > 0;
            bool dominanceConditionsMet = hasActiveCommandIgnoringAbandon && hasActiveDefenseIgnoringAbandon;

            reason = PlanetEvacuationService.CheckReclaimPreconditions(abandoned, dominanceConditionsMet);
            return reason == ReclaimRejectReason.None;
        }

        /// <summary>VS-014: counts any active real defense weapon (Laser or Plasma) — was hardcoded to Laser only before Plasma existed, same latent under-count fixed in ExtractionState.ActiveDefenseCount.</summary>
        private static int CountActiveDefenseIgnoringAbandon(string worldId)
        {
            int count = 0;
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.WorldId == worldId && SurfaceStructureCatalog.IsActiveDefenseWeapon(s.Type) && s.Runtime.IsActive) count++;
            return count;
        }

        /// <summary>
        /// Why a reclaim was refused, in the player's language. Its evacuation twin
        /// above was translated some time ago and these two were left behind, which
        /// is exactly the state that is hardest to notice: the planets page and the
        /// HUD context bar both show whatever this returns, verbatim, so two English
        /// sentences were reaching the player inside an otherwise Turkish interface.
        /// </summary>
        public static string RejectMessage(ReclaimRejectReason reason) => reason switch
        {
            ReclaimRejectReason.NotAbandoned => "GEZEGEN TERK EDİLMİŞ DEĞİL",
            ReclaimRejectReason.DominanceConditionsNotMet => "ETKİN KOMUTA VE ETKİN SAVUNMA GEREKLİ",
            _ => null
        };

        /// <summary>Reclaim never re-charges existing structures (step 10) and never awards XP — the normal DominanceSystem tick loop re-stabilizes for a fresh 120s exactly like a first-time acquisition.</summary>
        public bool TryReclaim()
        {
            if (!CanReclaim(out ReclaimRejectReason reason)) { SetMessage(RejectMessage(reason)); return false; }
            PlanetEvacuationService.Reclaim(extraction.TargetPlanet.WorldId);
            SetMessage("GERİ ALMA BAŞLADI — YENİDEN DENGELENİYOR");
            RequestSave?.Invoke();
            return true;
        }

        private void SetMessage(string text) { StatusMessage = text; messageUntil = Time.unscaledTime + 3.5f; NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Evacuation, text); }
        public string TransientMessage => Time.unscaledTime < messageUntil ? StatusMessage : null;

        // --- Save/load ---------------------------------------------------------
        /// <summary>Restore-only (VS-010 §7): resumes waiting for exactly the same pending vehicles, re-locking the planet. No new resolve/transfer/passivate side effects — those already happened before this was saved.</summary>
        public void RestoreTransaction(EvacuationDto dto)
        {
            if (dto == null || !dto.active) return;
            var tx = new PlanetEvacuationTransaction { PlanetWorldId = dto.planetWorldId };
            tx.PendingVehicleIds.AddRange(dto.pendingVehicleIds);
            ActiveTransaction = tx;
            ExtractionState.SetEvacuationLocked(dto.planetWorldId, true);
        }
    }
}
