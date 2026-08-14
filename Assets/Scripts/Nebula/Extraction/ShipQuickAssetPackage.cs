using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Modules;
using Nebula.Ship;

namespace Nebula.Extraction
{
    /// <summary>
    /// MonoBehaviour adapter for the Quick Asset Package (VS-010 §2):
    /// precondition polling against the ship's current <see cref="ShipExtraction"/>
    /// target planet, atomic confirm, staggered t=0/2/4s capsule sends (each
    /// re-checked against threat/orbit conditions right before it fires —
    /// only an UNSENT capsule can be held back), cancel (75% on already-sent
    /// structures via their own Cancel(cargo), 100% back on whatever is still
    /// only reserved), and retry (re-attempts a held-back send with no new
    /// charge — the cost is already reserved). Every structure it builds goes
    /// through the exact same DrillStructure/SurfaceStructure factories (and
    /// therefore the exact same VS-009 environment-duration scaling, applied
    /// once, and the exact same PlanetDominance 120s stabilization) as a
    /// manual build. Command's material is always the target planet's own
    /// family material; Laser's material is separately player-chosen
    /// (<see cref="PendingLaserMaterialId"/>, cycled with <see cref="CycleLaserMaterialKey"/>).
    /// </summary>
    public class ShipQuickAssetPackage : MonoBehaviour
    {
        /// <summary>
        /// Confirm / retry.
        /// <para>
        /// A property, not a <c>static readonly</c> field. As a field it was evaluated
        /// once at type initialisation and then never again, so it held whatever the
        /// catalog said before the settings store had even been read — a rebind of this
        /// action was accepted by the settings screen, written to disk, reloaded on the
        /// next launch, and still never reached this class. Asking the catalog per read
        /// is the whole point of the catalog.
        /// </para>
        /// </summary>
        public static KeyCode ConfirmKey => NebulaInputActions.Key(NebulaInputActions.QuickPackageConfirm);

        /// <summary>
        /// Cancel. Shares U with the command menu, and is the <i>contextual</i> owner
        /// of it: this action only exists while a package is in flight, and the menu
        /// is the global intent for every other moment.
        /// <para>
        /// Read through <see cref="NebulaInputRouter"/>, never <c>Input</c>. Both
        /// owners used to sample U for themselves in the same frame, so pressing it
        /// during a package cancelled the package <b>and</b> opened the menu — and
        /// Unity guarantees no ordering between two <c>Update</c>s, so which one the
        /// player noticed was luck.
        /// </para>
        /// </summary>
        public static KeyCode CancelKey => NebulaInputActions.Key(NebulaInputActions.QuickPackageCancel);

        /// <summary>Same key as <see cref="ConfirmKey"/>: confirm when idle, retry when a send is held back.</summary>
        public static KeyCode RetryKey => NebulaInputActions.Key(NebulaInputActions.QuickPackageConfirm);

        public static KeyCode CycleLaserMaterialKey =>
            NebulaInputActions.Key(NebulaInputActions.QuickPackageCycleMaterial);

        private ShipController ship;
        private ShipExtraction extraction;
        private ShipLogistics logistics;
        private ShipModules modules;

        public Action RequestSave { get; set; }

        public QuickAssetPackageTransaction ActiveTransaction { get; private set; }
        private DrillStructure sentDrill;
        private SurfaceStructure sentCommand;
        private SurfaceStructure sentLaser;
        private bool drillAttempted, commandAttempted, laserAttempted;

        public string StatusMessage { get; private set; }
        private float messageUntil;

        private int laserMaterialIndex;
        /// <summary>Player's chosen Laser special-material id for the NEXT confirm — independent of Command's (always the planet's own family material).</summary>
        public string PendingLaserMaterialId => ProcessingCatalog.MaterialIds[laserMaterialIndex];

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            extraction = GetComponent<ShipExtraction>();
            logistics = GetComponent<ShipLogistics>();
            modules = GetComponent<ShipModules>();
        }

        /// <summary>
        /// The ship's modules, resolved on demand.
        /// <para>
        /// <see cref="Awake"/> runs the instant this component is added, and
        /// <c>ShipFactory</c> adds this one well before <see cref="ShipModules"/>
        /// — so the field captured there is null for the life of the ship. That
        /// silently failed the Station Construction gate forever, which made
        /// the Quick Asset Package impossible to confirm. Resolving here
        /// removes the ordering dependency instead of trading it for a
        /// different one.
        /// </para>
        /// </summary>
        private ShipModules Modules => modules != null ? modules : (modules = GetComponent<ShipModules>());
        private LogisticsCargo Cargo => (logistics != null ? logistics : (logistics = GetComponent<ShipLogistics>()))?.Cargo;

        /// <summary>
        /// True exactly while <see cref="Cancel"/> would do something — the one
        /// condition under which the cancel action exists at all.
        /// <para>
        /// Public because it is also the enable state any explicit cancel control
        /// needs: a button, a context-bar prompt or a menu row can drive
        /// <see cref="Cancel"/> straight through this without going near a keyboard.
        /// The key is the shortcut for the action, not the action.
        /// </para>
        /// </summary>
        public bool CancelAvailable =>
            ActiveTransaction != null && !ActiveTransaction.Cancelled && !ActiveTransaction.Completed;

        /// <summary>
        /// True when this component would act on a cancel press right now: a live
        /// package, and nothing else holding input. The router asks this at the moment
        /// a key is pressed rather than being told each frame, so the answer cannot be
        /// one frame stale in either direction.
        /// </summary>
        private bool CancelContextLive =>
            this != null && isActiveAndEnabled && CancelAvailable
            && !InputGate.Suppressed && !CombatInputContext.WorldInteractionBlocked;

        private void OnEnable() =>
            NebulaInputRouter.SetContextSource(InputContext.QuickAssetPackage, () => CancelContextLive);

        /// <summary>Withdraws the claim. A context left standing would keep taking U from the command menu with nothing behind it.</summary>
        private void OnDisable() =>
            NebulaInputRouter.SetContextSource(InputContext.QuickAssetPackage, null);

        /// <summary>
        /// Steps the Laser's special material to the next one in the catalog.
        /// <para>
        /// Was three characters inlined in <c>Update</c> behind an <c>Input.GetKeyDown</c>,
        /// which made choosing the material a keyboard-only act with no seam a control
        /// could call. The choice itself is unchanged: same order, same catalog, same
        /// wrap.
        /// </para>
        /// </summary>
        public string CycleLaserMaterial()
        {
            laserMaterialIndex = (laserMaterialIndex + 1) % ProcessingCatalog.MaterialIds.Length;
            return PendingLaserMaterialId;
        }

        /// <summary>
        /// The one entry the confirm control and the confirm key share: retry while a
        /// package is live and something is still held back, confirm otherwise.
        /// </summary>
        public bool ConfirmOrRetry()
        {
            if (CancelAvailable) { Retry(); return true; }
            return TryConfirm(PendingLaserMaterialId);
        }

        private void Update()
        {
            if (InputGate.Suppressed || CombatInputContext.WorldInteractionBlocked) return;

            // Router, not Input: both of these used to read a KeyCode captured at type
            // initialisation, so neither honoured a rebind at all.
            if (NebulaInputRouter.Pressed(NebulaInputActions.QuickPackageCycleMaterial)) CycleLaserMaterial();

            if (NebulaInputRouter.Pressed(NebulaInputActions.QuickPackageConfirm)) ConfirmOrRetry();

            // The router, not Input: U is shared with the command menu and exactly one
            // of the two may answer a press. Cancel wins only while a package is live,
            // which is the only time it has anything to do.
            if (NebulaInputRouter.Pressed(NebulaInputActions.QuickPackageCancel)) Cancel();

            if (CancelAvailable) TickActive(Time.deltaTime);
        }

        // --- Preconditions -------------------------------------------------------
        public bool CanConfirm(out QuickPackageRejectReason reason, string laserMaterialId)
        {
            reason = QuickPackageRejectReason.None;
            if (ActiveTransaction != null && !ActiveTransaction.Cancelled && !ActiveTransaction.Completed)
            { reason = QuickPackageRejectReason.PackageAlreadyActive; return false; }

            PlanetIdentity planet = extraction != null ? extraction.TargetPlanet : null;
            PlanetProfile profile = extraction != null ? extraction.TargetProfile : null;
            if (planet == null || profile == null || string.IsNullOrEmpty(laserMaterialId))
            { reason = QuickPackageRejectReason.NotScanned; return false; }
            if (ExtractionState.IsEvacuationLocked(planet.WorldId))
            { reason = QuickPackageRejectReason.EvacuationInProgress; return false; }

            string commandMaterialId = ProcessingCatalog.MaterialIds[(int)profile.Family];

            bool scanned = profile.Scanned;
            float altitude = extraction.Altitude(planet);
            float rangeLimit = planet.Body.Radius * extraction.ScannerProximityR;
            bool inRange = altitude <= rangeLimit;
            float relSpeed = extraction.RelativeSpeed(planet.Body);
            bool stabilized = ship.IsStabilized;

            bool drillFree = !ExtractionState.IsNodeOccupied(planet.WorldId, profile.DrillNode.Id);
            bool commandFree = !ExtractionState.IsNodeOccupied(planet.WorldId, profile.CommandNode.Id);
            bool laserFree = !ExtractionState.IsNodeOccupied(planet.WorldId, profile.FirstDefenseNode.Id);

            bool hasStation = Modules != null && Modules.Modules.GetTier("industry.station_construction") >= 1;
            bool hasCargo = QuickAssetPackageService.HasSufficientCargo(logistics != null ? logistics.Cargo : null, commandMaterialId, laserMaterialId);

            reason = QuickAssetPackageService.CheckPreconditions(scanned, inRange, relSpeed, stabilized,
                drillFree, commandFree, laserFree, hasStation, threatDetected: false, hasSufficientCargo: hasCargo);
            return reason == QuickPackageRejectReason.None;
        }

        public static string RejectMessage(QuickPackageRejectReason reason) => reason switch
        {
            QuickPackageRejectReason.PackageAlreadyActive => "BURADA ZATEN BİR PAKET SÜRÜYOR",
            QuickPackageRejectReason.EvacuationInProgress => "TAHLİYE SÜRÜYOR",
            QuickPackageRejectReason.NotScanned => "ÖNCE GEZEGENİ TARA",
            QuickPackageRejectReason.NotInRange => "DAHA YAKINA GEL",
            QuickPackageRejectReason.TooFast => "BAĞIL HIZ ÇOK YÜKSEK (EN FAZLA 25)",
            QuickPackageRejectReason.NotStabilized => "YÖRÜNGE SABİTLEMEYİ AÇ",
            QuickPackageRejectReason.NodesOccupied => "SONDAJ/KOMUTA/SAVUNMA YUVASI ZATEN DOLU",
            QuickPackageRejectReason.StationConstructionMissing => "İSTASYON İNŞASI T1 GEREKİYOR",
            QuickPackageRejectReason.ThreatDetected => "ETKİN TEHDİT ALGILANDI",
            // The two figures come from the same totals the reservation charges, so the
            // refusal can never quote a price the transaction does not use.
            QuickPackageRejectReason.InsufficientCargo =>
                $"KARGO YETERSİZ ({QuickAssetPackageService.TotalBasicCost} TEMEL + {QuickAssetPackageService.TotalSpecialCost} ÖZEL)",
            QuickPackageRejectReason.None => null,
            // Never an enum name and never silence: a refusal the table has not been
            // taught still has to say, in the player's language, that it refused.
            _ => "HIZLI KURULUM ŞU AN ONAYLANAMIYOR"
        };

        // --- Confirm ---------------------------------------------------------------
        public bool TryConfirm(string laserMaterialId)
        {
            if (!CanConfirm(out QuickPackageRejectReason reason, laserMaterialId)) { SetMessage(RejectMessage(reason)); return false; }

            string commandMaterialId = ProcessingCatalog.MaterialIds[(int)extraction.TargetProfile.Family];
            string transactionId = Guid.NewGuid().ToString("N");
            QuickAssetPackageTransaction tx = QuickAssetPackageService.TryConfirm(
                transactionId, extraction.TargetPlanet.WorldId, commandMaterialId, laserMaterialId, logistics.Cargo);
            if (tx == null) { SetMessage(RejectMessage(QuickPackageRejectReason.InsufficientCargo)); return false; }

            ActiveTransaction = tx;
            sentDrill = null; sentCommand = null; sentLaser = null;
            drillAttempted = commandAttempted = laserAttempted = false;
            SetMessage("HIZLI KURULUM ONAYLANDI — SONDAJ KAPSÜLÜ YOLDA");
            RequestSave?.Invoke();
            return true;
        }

        // --- Tick / staggered sends -------------------------------------------------
        private void TickActive(float dt)
        {
            QuickAssetPackageTransaction tx = ActiveTransaction;
            tx.ElapsedSeconds += dt;

            if (!tx.DrillSent && !drillAttempted && tx.ElapsedSeconds >= QuickAssetPackageService.DrillSendDelay)
            { drillAttempted = true; TrySend(isDrill: true, isCommand: false); }
            if (!tx.CommandSent && !commandAttempted && tx.ElapsedSeconds >= QuickAssetPackageService.CommandSendDelay)
            { commandAttempted = true; TrySend(isDrill: false, isCommand: true); }
            if (!tx.LaserSent && !laserAttempted && tx.ElapsedSeconds >= QuickAssetPackageService.LaserSendDelay)
            { laserAttempted = true; TrySend(isDrill: false, isCommand: false); }

            if (tx.Completed)
            {
                SetMessage("HIZLI KURULUM TAMAMLANDI — ÜÇ KAPSÜL DE GÖNDERİLDİ");
                RequestSave?.Invoke();
            }
        }

        /// <summary>Re-attempts whichever capsule(s) a threat/orbit violation held back — no new charge, the cost is already reserved.</summary>
        public void Retry()
        {
            QuickAssetPackageTransaction tx = ActiveTransaction;
            if (tx == null || tx.Cancelled || tx.Completed) return;
            if (!tx.DrillSent) drillAttempted = false;
            if (!tx.CommandSent) commandAttempted = false;
            if (!tx.LaserSent) laserAttempted = false;
        }

        /// <summary>Only an UNSENT capsule can be held back by a threat or orbit violation — an already-sent structure's own lifecycle is never interrupted by this.</summary>
        private bool SendEligibleNow()
        {
            PlanetIdentity planet = extraction != null ? extraction.TargetPlanet : null;
            if (planet == null || ActiveTransaction == null || planet.WorldId != ActiveTransaction.PlanetWorldId) return false;
            float altitude = extraction.Altitude(planet);
            float rangeLimit = planet.Body.Radius * extraction.ScannerProximityR;
            const bool threatDetected = false; // no combat/threat system exists this slice — never a real blocker
            return altitude <= rangeLimit && ship.IsStabilized && !threatDetected;
        }

        private void TrySend(bool isDrill, bool isCommand)
        {
            if (!SendEligibleNow()) return;
            PlanetIdentity planet = extraction.TargetPlanet;
            PlanetProfile profile = extraction.TargetProfile;
            Vector3 shipPos = transform.position;
            LogisticsCargo cargo = logistics.Cargo;

            if (isDrill)
            {
                int refundParts = QuickAssetPackageService.SendDrill(ActiveTransaction, cargo);
                sentDrill = DrillStructure.Create(planet, profile, shipPos, DrillStructure.BuildSource.Parts, refundParts);
                SetMessage("SONDAJ KAPSÜLÜ YOLDA (PAKET)");
            }
            else if (isCommand)
            {
                (int refundParts, int refundMaterial) = QuickAssetPackageService.SendCommand(ActiveTransaction, cargo);
                var commandRefund = new List<(string, float)> { (ProcessingCatalog.BasicPartId, refundParts), (ActiveTransaction.CommandMaterialId, refundMaterial) };
                sentCommand = SurfaceStructure.CreateWithFlatRefund(planet, profile, SurfaceStructureCatalog.Command, profile.CommandNode, shipPos, commandRefund);
                SetMessage("KOMUTA KAPSÜLÜ YOLDA (PAKET)");
            }
            else
            {
                (int refundParts, int refundMaterial) = QuickAssetPackageService.SendLaser(ActiveTransaction, cargo);
                var laserRefund = new List<(string, float)> { (ProcessingCatalog.BasicPartId, refundParts), (ActiveTransaction.LaserMaterialId, refundMaterial) };
                sentLaser = SurfaceStructure.CreateWithFlatRefund(planet, profile, SurfaceStructureCatalog.Laser, profile.FirstDefenseNode, shipPos, laserRefund);
                SetMessage("LAZER KAPSÜLÜ YOLDA (PAKET)");
            }
            RequestSave?.Invoke();
        }

        // --- Cancel ------------------------------------------------------------------
        /// <summary>Before any capsule: full refund. After some: sent structures use the existing 75% cancel rule, unsent portion returns 100%.</summary>
        public void Cancel()
        {
            QuickAssetPackageTransaction tx = ActiveTransaction;
            if (tx == null || tx.Cancelled || tx.Completed) return;
            LogisticsCargo cargo = logistics != null ? logistics.Cargo : null;

            if (sentDrill != null) sentDrill.Cancel(cargo);
            if (sentCommand != null) sentCommand.Cancel(cargo);
            if (sentLaser != null) sentLaser.Cancel(cargo);
            QuickAssetPackageService.ReleaseUnsentReservation(tx, cargo);

            tx.Cancelled = true;
            sentDrill = null; sentCommand = null; sentLaser = null;
            SetMessage("HIZLI KURULUM İPTAL EDİLDİ");
            RequestSave?.Invoke();
        }

        private void SetMessage(string text) { StatusMessage = text; messageUntil = Time.unscaledTime + 3.5f; NebulaGameplayFeedback.Publish(GameplayFeedbackSource.QuickPackage, text); }
        public string TransientMessage => Time.unscaledTime < messageUntil ? StatusMessage : null;

        // --- Save/load ---------------------------------------------------------
        /// <summary>
        /// Restore-only (VS-010 §7): rebuilds the transaction exactly as
        /// saved and re-links already-sent structures by looking them up on
        /// their canonical node — callers must invoke this AFTER the target
        /// system's drills/structures are restored (SystemStreamingDirector's
        /// delta restore), never before. No new charge, no re-send.
        /// </summary>
        public bool RestoreTransaction(QuickPackageDto dto)
        {
            if (dto == null || !dto.active) return false;

            var restored = new QuickAssetPackageTransaction
            {
                TransactionId = dto.transactionId, PlanetWorldId = dto.planetWorldId,
                CommandMaterialId = dto.commandMaterialId, LaserMaterialId = dto.laserMaterialId,
                DrillSent = dto.drillSent, CommandSent = dto.commandSent, LaserSent = dto.laserSent,
                ElapsedSeconds = dto.elapsedSeconds
            };
            List<(string itemId, float amount)> missing = QuickAssetPackageService.MissingNodeCost(restored);
            if (missing.Count > 0 && (Cargo == null
                || !Cargo.TrySetReservation(QuickAssetPackageService.OwnerId(restored.TransactionId), missing)))
            {
                ActiveTransaction = null;
                SetMessage("PAKET GERİ YÜKLEME DURDU — KARGO YETERSİZ");
                return false;
            }
            ActiveTransaction = restored;
            drillAttempted = dto.drillSent; commandAttempted = dto.commandSent; laserAttempted = dto.laserSent;

            PlanetProfile profile = ExtractionState.GetProfile(dto.planetWorldId);
            sentDrill = dto.drillSent ? FindDrill(dto.planetWorldId, profile.DrillNode.Id) : null;
            sentCommand = dto.commandSent ? FindStructure(dto.planetWorldId, profile.CommandNode.Id) : null;
            sentLaser = dto.laserSent ? FindStructure(dto.planetWorldId, profile.FirstDefenseNode.Id) : null;
            return true;
        }

        private static DrillStructure FindDrill(string worldId, string nodeId)
        {
            foreach (DrillStructure d in ExtractionState.Drills) if (d != null && d.WorldId == worldId && d.NodeId == nodeId) return d;
            return null;
        }

        private static SurfaceStructure FindStructure(string worldId, string nodeId)
        {
            foreach (SurfaceStructure s in ExtractionState.Structures) if (s != null && s.WorldId == worldId && s.NodeId == nodeId) return s;
            return null;
        }
    }
}
