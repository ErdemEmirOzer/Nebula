using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.Logistics
{
    public enum CenterBuildRejectReason
    {
        None, AlreadyExists, LevelTooLow, ModuleTierTooLow, NoActiveCommand, NotInRange, TooFast, InsufficientCargo, NoSafeAnchor
    }

    public enum CenterUpgradeRejectReason
    {
        None, NotActive, AlreadyUpgrading, LevelTooLow, ModuleTierTooLow, NotEnoughControlledPlanets, NotInRange, TooFast, InsufficientCargo
    }

    /// <summary>
    /// Ship-side contextual interaction with the System Logistics Center
    /// (VS-011 §2/§3/§8): build T1, start/cancel the T1-&gt;T2 upgrade,
    /// dismantle, and manual ship&lt;-&gt;center cargo transfer while
    /// physically near the anchor. No new global shortcut — reuses the same
    /// F/T contextual-key convention as <see cref="ShipExtraction"/>
    /// (mutually exclusive by construction: the anchor always sits strictly
    /// between the star's danger boundary and the innermost planet's orbit,
    /// so a ship near it is never simultaneously "near a planet").
    /// </summary>
    public class ShipLogisticsCenter : MonoBehaviour
    {
        public const KeyCode BuildOrUpgradeKey = KeyCode.F;
        public const KeyCode WithdrawKey = KeyCode.T;
        public const KeyCode DepositKey = KeyCode.N;
        public const KeyCode DismantleKey = KeyCode.X;

        private const float ApproachRange = 500f;
        private const float MaxBuildRelativeSpeed = 30f;

        private ShipController ship;
        private ShipLogistics logistics;
        private ShipModules modules;
        private SystemStreamingDirector streaming;

        public string StatusMessage { get; private set; }
        private float messageUntil;

        public string CurrentSystemId { get; private set; }
        public LogisticsCenter NearbyCenter { get; private set; }
        public bool InRange { get; private set; }
        public Vector3 AnchorAbsolutePosition { get; private set; }
        public AnchorRejectReason AnchorReason { get; private set; }
        public bool AnchorValid { get; private set; }
        private bool lastFillWarningActive;
        private LogisticsCenter lastFillWarningCenter;

        public void Initialize(SystemStreamingDirector streamingDirector) => streaming = streamingDirector;

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            logistics = GetComponent<ShipLogistics>();
            modules = GetComponent<ShipModules>();
        }

        private void OnEnable() => NebulaInputRouter.SetContextSource(InputContext.Logistics,
            () => (InRange || NearbyCenter != null) && !InputGate.Suppressed && !CombatInputContext.WorldInteractionBlocked);

        private void OnDisable() => NebulaInputRouter.SetContextSource(InputContext.Logistics, null);

        private void Update()
        {
            if (InputGate.Suppressed || CombatInputContext.WorldInteractionBlocked) return;
            UpdateTarget();
            HandleBuildOrUpgrade();
            HandleTransfer();
            HandleDismantle();
            HandleCancel();
            HandleFillWarning();
        }

        /// <summary>VS-011 §3: fires the %80 fill message exactly once per crossing (the false-&gt;true edge) — never every frame while it stays full.</summary>
        private void HandleFillWarning()
        {
            if (NearbyCenter != lastFillWarningCenter) { lastFillWarningActive = false; lastFillWarningCenter = NearbyCenter; }
            bool active = NearbyCenter != null && NearbyCenter.FillWarningActive;
            if (active && !lastFillWarningActive) SetMessage($"MERKEZ %{NearbyCenter.FillFraction01 * 100f:0} DOLU");
            lastFillWarningActive = active;
        }

        /// <summary>
        /// Esc cancels a not-yet-active build (75% refund) or an in-progress T2 upgrade
        /// (75% refund of the T2 cost; T1 keeps working) — mirrors ShipExtraction's
        /// Esc-cancel convention.
        /// <para>
        /// Through the router's shared-intent read, which is that convention's other
        /// half. Escape stays deliberately unarbitrated — every surface closes its own
        /// thing — but this was the last gameplay owner still holding a literal at the
        /// call site while extraction, POI, combat and both menu hosts read the catalog
        /// row. The key now follows the row, and the sample follows
        /// <c>NebulaInputRouter.Source</c>, so this cancel is drivable from a test
        /// exactly like every other action rather than being the one path a substituted
        /// keyboard cannot reach.
        /// </para>
        /// </summary>
        private void HandleCancel()
        {
            if (!NebulaInputRouter.SharedPressed(NebulaInputActions.LogisticsClose)) return;
            if (TryCancelBuild()) return;
            TryCancelUpgrade();
        }

        // --- Target ------------------------------------------------------------
        private void UpdateTarget()
        {
            CurrentSystemId = StarSystemRegistry.TryFindContaining(ship.transform.position + FloatingOrigin.TotalOffset, out StarSystemRecord record)
                ? record.SystemId : null;
            NearbyCenter = CurrentSystemId != null ? LogisticsState.FindCenter(CurrentSystemId) : null;

            AnchorValid = false; InRange = false;
            if (CurrentSystemId == null) return;

            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(CurrentSystemId);
            Transform starTransform = streaming != null ? streaming.TryGetStarTransform(CurrentSystemId) : null;
            if (manifest == null || starTransform == null) return;

            AnchorValid = LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out AnchorRejectReason reason);
            AnchorReason = reason;
            if (!AnchorValid) return;

            AnchorAbsolutePosition = starTransform.position + offset; // current-frame absolute (star already floating-origin correct)
            float distance = Vector3.Distance(transform.position, AnchorAbsolutePosition);
            InRange = distance <= ApproachRange;
        }

        private float RelativeSpeed() => ship.Velocity.magnitude; // the anchor never moves relative to its system

        // --- Build / upgrade -----------------------------------------------------
        public CenterBuildRejectReason CheckBuild()
        {
            if (NearbyCenter != null) return CenterBuildRejectReason.AlreadyExists;
            if (!AnchorValid) return CenterBuildRejectReason.NoSafeAnchor;
            if (!InRange) return CenterBuildRejectReason.NotInRange;
            if (RelativeSpeed() > MaxBuildRelativeSpeed) return CenterBuildRejectReason.TooFast;
            if (ProgressionService.State.Level < LogisticsCenterCatalog.T1.RequiredLevel) return CenterBuildRejectReason.LevelTooLow;
            int moduleTier = modules != null ? modules.Modules.GetTier(LogisticsCenterCatalog.StationConstructionModuleId) : 0;
            if (moduleTier < LogisticsCenterCatalog.T1.RequiredModuleTier) return CenterBuildRejectReason.ModuleTierTooLow;
            if (!SystemStatusQuery.HasActiveCommandAnywhere(CurrentSystemId)) return CenterBuildRejectReason.NoActiveCommand;
            if (!HasCargo(LogisticsCenterCatalog.T1)) return CenterBuildRejectReason.InsufficientCargo;
            return CenterBuildRejectReason.None;
        }

        public CenterUpgradeRejectReason CheckUpgrade()
        {
            if (NearbyCenter == null || !NearbyCenter.Runtime.IsActive) return CenterUpgradeRejectReason.NotActive;
            if (!InRange) return CenterUpgradeRejectReason.NotInRange;
            if (RelativeSpeed() > MaxBuildRelativeSpeed) return CenterUpgradeRejectReason.TooFast;
            if (!NearbyCenter.Runtime.CanStartUpgrade) return CenterUpgradeRejectReason.AlreadyUpgrading;
            if (ProgressionService.State.Level < LogisticsCenterCatalog.T2.RequiredLevel) return CenterUpgradeRejectReason.LevelTooLow;
            int moduleTier = modules != null ? modules.Modules.GetTier(LogisticsCenterCatalog.StationConstructionModuleId) : 0;
            if (moduleTier < LogisticsCenterCatalog.T2.RequiredModuleTier) return CenterUpgradeRejectReason.ModuleTierTooLow;
            if (ControlledPlanetCount(CurrentSystemId) < 2) return CenterUpgradeRejectReason.NotEnoughControlledPlanets;
            if (!HasCargo(LogisticsCenterCatalog.T2)) return CenterUpgradeRejectReason.InsufficientCargo;
            return CenterUpgradeRejectReason.None;
        }

        private static int ControlledPlanetCount(string systemId)
        {
            int count = 0;
            foreach (DominanceState s in SystemStatusQuery.PlanetDominanceStates(systemId).Values)
                if (s == DominanceState.Controlled) count++;
            return count;
        }

        private bool HasCargo(LogisticsCenterTierDef def)
        {
            LogisticsCargo cargo = logistics != null ? logistics.Cargo : null;
            if (cargo == null) return false;
            foreach (var (itemId, amount) in def.Cost()) if (cargo.GetAvailable(itemId) < amount - 0.0001f) return false;
            return true;
        }

        private void HandleBuildOrUpgrade()
        {
            if (!NebulaInputRouter.Pressed(NebulaInputActions.LogisticsBuild)) return;
            if (NearbyCenter == null) TryBuild(); else TryStartUpgrade();
        }

        // --- Public command seam -------------------------------------------------
        //
        // These are the same calls the contextual keys make, lifted out of the key
        // handlers so a screen can issue the command without either simulating a
        // key press or reaching past this class into LogisticsCenter and writing
        // state directly. There is one authority for each operation and both the
        // keyboard and the interface go through it: the check, the charge, the
        // state change and the player-facing message all stay in one place, so the
        // two entry points can never disagree about whether something happened.

        /// <summary>
        /// Builds the T1 centre if every precondition holds, otherwise refuses and
        /// says why. Returns the reason, <see cref="CenterBuildRejectReason.None"/>
        /// on success.
        /// </summary>
        public CenterBuildRejectReason TryBuild()
        {
            CenterBuildRejectReason reason = CheckBuild();
            if (reason != CenterBuildRejectReason.None) { SetMessage(BuildRejectText(reason)); return reason; }
            if (!ConfirmBuild())
            {
                SetMessage(BuildRejectText(CenterBuildRejectReason.InsufficientCargo));
                return CenterBuildRejectReason.InsufficientCargo;
            }
            return CenterBuildRejectReason.None;
        }

        /// <summary>Starts the T1→T2 upgrade, charging the T2 cost, or refuses with a reason.</summary>
        public CenterUpgradeRejectReason TryStartUpgrade()
        {
            CenterUpgradeRejectReason reason = CheckUpgrade();
            if (reason != CenterUpgradeRejectReason.None) { SetMessage(UpgradeRejectText(reason)); return reason; }

            // P1-ECO-016: the charge is one all-or-nothing transaction, and it is
            // checked. The old loop spent each of the three lines with TryRemove and
            // threw the answer away, so a price partly paid still started the upgrade
            // — safe only for as long as CheckUpgrade happened to have looked at the
            // same numbers with a different method one line earlier.
            LogisticsCargo cargo = logistics.Cargo;
            if (cargo == null || !cargo.TryConsumeAllImmediate(LogisticsCenterCatalog.T2.Cost()))
            {
                SetMessage(UpgradeRejectText(CenterUpgradeRejectReason.InsufficientCargo));
                return CenterUpgradeRejectReason.InsufficientCargo;
            }
            NearbyCenter.StartUpgrade();
            SetMessage("T2 YÜKSELTME BAŞLADI");
            return CenterUpgradeRejectReason.None;
        }

        /// <summary>
        /// Cancels an in-progress upgrade with the same 75% refund the Escape key
        /// gives. False when there is no upgrade running, or the ship is not close
        /// enough to act on the centre at all.
        /// </summary>
        public bool TryCancelUpgrade()
        {
            if (NearbyCenter == null || !InRange || !NearbyCenter.Runtime.Upgrading) return false;
            NearbyCenter.CancelUpgrade(logistics != null ? logistics.Cargo : null);
            SetMessage("YÜKSELTME İPTAL — %75 İADE");
            return true;
        }

        /// <summary>Cancels a not-yet-finished T1 build with its 75% refund.</summary>
        public bool TryCancelBuild()
        {
            if (NearbyCenter == null || !InRange || !NearbyCenter.Runtime.CanCancelForRefund) return false;
            NearbyCenter.Cancel(logistics != null ? logistics.Cargo : null);
            SetMessage("İNŞA İPTAL — %75 İADE");
            return true;
        }

        /// <summary>Dismantles an idle, empty centre for the 50% refund, or refuses with a reason.</summary>
        public bool TryDismantle()
        {
            if (NearbyCenter == null || !InRange) return false;
            if (!NearbyCenter.CanDismantle()) { SetMessage("SÖKÜM İÇİN ENVANTER BOŞ VE DOCK'LAR BOŞ OLMALI"); return false; }
            DismantleNow();
            return true;
        }

        /// <summary>
        /// Moves one transfer budget's worth of cargo into the centre, the same way
        /// holding the deposit key does for one frame. Returns the volume moved.
        /// </summary>
        public float DepositToCenter(float budget) => MoveToCenter(budget);

        /// <summary>The withdraw direction of <see cref="DepositToCenter"/>.</summary>
        public float WithdrawFromCenter(float budget) => MoveFromCenter(budget);

        /// <summary>One second of the centre's own transfer rate — the unit a screen's transfer control moves.</summary>
        public float OneSecondTransferBudget =>
            NearbyCenter != null ? NearbyCenter.Runtime.Def.TransferVolumePerSecond : 0f;

        /// <summary>
        /// Charges the T1 price and raises the centre. False — with nothing spent and
        /// nothing built — when the hold cannot pay after all (P1-ECO-016); the caller
        /// turns that into the player's reason.
        /// </summary>
        private bool ConfirmBuild()
        {
            LogisticsCargo cargo = logistics.Cargo;
            LogisticsCenterTierDef t1 = LogisticsCenterCatalog.T1;
            if (cargo == null || !cargo.TryConsumeAllImmediate(t1.Cost())) return false;

            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(CurrentSystemId);
            Transform starTransform = streaming.TryGetStarTransform(CurrentSystemId);
            LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _);

            int refundParts = Mathf.FloorToInt(t1.PartsCost * LogisticsCenterCatalog.RefundFraction);
            int refundMetal = Mathf.FloorToInt(t1.MetalAlloyCost * LogisticsCenterCatalog.RefundFraction);
            int refundBio = Mathf.FloorToInt(t1.BiocompositeCost * LogisticsCenterCatalog.RefundFraction);
            LogisticsCenter.Create(manifest, starTransform, offset, refundParts, refundMetal, refundBio);
            SetMessage("LOJİSTİK MERKEZİ İNŞAATI BAŞLADI");
            return true;
        }

        // --- Manual transfer -----------------------------------------------------
        private void HandleTransfer()
        {
            if (NearbyCenter == null || !InRange || !NearbyCenter.Runtime.IsActive) return;
            float budget = NearbyCenter.Runtime.Def.TransferVolumePerSecond * Time.deltaTime;

            if (NebulaInputRouter.Held(NebulaInputActions.LogisticsWithdraw)) MoveFromCenter(budget);
            else if (NebulaInputRouter.Held(NebulaInputActions.LogisticsDeposit)) MoveToCenter(budget);
        }

        /// <summary>Centre → ship, up to <paramref name="budget"/> volume. Returns what actually moved.</summary>
        private float MoveFromCenter(float budget)
        {
            if (NearbyCenter == null || !InRange || !NearbyCenter.Runtime.IsActive) return 0f;
            LogisticsCargo shipCargo = logistics != null ? logistics.Cargo : null;
            if (shipCargo == null || budget <= 0.0001f) return 0f;

            float moved = 0f;
            foreach (var kv in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>(NearbyCenter.Inventory.Items))
            {
                if (budget <= 0.0001f) break;
                float want = Mathf.Min(budget, kv.Value);
                float taken = NearbyCenter.Inventory.TryConsumeImmediate(kv.Key, want) ? want : 0f;
                if (taken <= 0f) continue;
                float added = shipCargo.Add(kv.Key, taken);
                if (added < taken) NearbyCenter.Inventory.Add(kv.Key, taken - added); // partial-fit spillback, never lost
                budget -= added;
                moved += added;
            }
            return moved;
        }

        /// <summary>Ship → centre, up to <paramref name="budget"/> volume. Returns what actually moved.</summary>
        private float MoveToCenter(float budget)
        {
            if (NearbyCenter == null || !InRange || !NearbyCenter.Runtime.IsActive) return 0f;
            LogisticsCargo shipCargo = logistics != null ? logistics.Cargo : null;
            if (shipCargo == null || budget <= 0.0001f) return 0f;

            float moved = 0f;
            foreach (var kv in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>(shipCargo.Items))
            {
                if (budget <= 0.0001f) break;
                if (!LogisticsCenterCatalog.IsAcceptedItem(kv.Key)) continue;
                float want = Mathf.Min(budget, kv.Value);
                float added = NearbyCenter.Inventory.Add(kv.Key, want);
                if (added <= 0f) continue;
                // P1-ECO-016: if the hold cannot actually give up what the centre just
                // accepted, the centre must not keep it — otherwise the pair of stores
                // holds more between them than existed a line earlier.
                if (!shipCargo.TryConsumeImmediate(kv.Key, added))
                {
                    NearbyCenter.Inventory.TryConsumeImmediate(kv.Key, added);
                    continue;
                }
                budget -= added;
                moved += added;
            }
            return moved;
        }

        // --- Dismantle -------------------------------------------------------------
        private void HandleDismantle()
        {
            if (!NebulaInputRouter.Pressed(NebulaInputActions.LogisticsDismantle)) return;
            TryDismantle();
        }

        private void DismantleNow()
        {
            LogisticsCenterTierDef def = NearbyCenter.Runtime.Def;
            LogisticsCargo shipCargo = logistics != null ? logistics.Cargo : null;
            if (shipCargo != null)
                foreach (var (itemId, amount) in def.Cost())
                    shipCargo.Add(itemId, Mathf.Floor(amount * LogisticsCenterCatalog.DismantleRefundFraction));
            NearbyCenter.Dismantle();
            Destroy(NearbyCenter.gameObject);
            SetMessage("MERKEZ SÖKÜLDÜ — %50 İADE");
        }

        private void SetMessage(string text) { StatusMessage = text; messageUntil = Time.unscaledTime + 3.5f; NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Logistics, text); }
        public string TransientMessage => Time.unscaledTime < messageUntil ? StatusMessage : null;

        public static string BuildRejectText(CenterBuildRejectReason reason) => reason switch
        {
            CenterBuildRejectReason.AlreadyExists => "SİSTEMDE ZATEN BİR MERKEZ VAR",
            CenterBuildRejectReason.LevelTooLow => "SEVİYE YETERSİZ (18)",
            CenterBuildRejectReason.ModuleTierTooLow => "İSTASYON İNŞA T1 GEREKLİ",
            CenterBuildRejectReason.NoActiveCommand => "SİSTEMDE AKTİF COMMAND GEREKLİ",
            CenterBuildRejectReason.NotInRange => "ÇAPAYA YAKLAŞ",
            CenterBuildRejectReason.TooFast => "BAĞIL HIZ ÇOK YÜKSEK",
            CenterBuildRejectReason.InsufficientCargo => "YETERSİZ KAYNAK",
            CenterBuildRejectReason.NoSafeAnchor => "SİSTEMDE GÜVENLİ ÇAPA YOK",
            _ => "BAŞARISIZ"
        };

        public static string UpgradeRejectText(CenterUpgradeRejectReason reason) => reason switch
        {
            CenterUpgradeRejectReason.NotActive => "MERKEZ AKTİF DEĞİL",
            CenterUpgradeRejectReason.AlreadyUpgrading => "ZATEN YÜKSELTİLİYOR",
            CenterUpgradeRejectReason.LevelTooLow => "SEVİYE YETERSİZ (25)",
            CenterUpgradeRejectReason.ModuleTierTooLow => "İSTASYON İNŞA T2 GEREKLİ",
            CenterUpgradeRejectReason.NotEnoughControlledPlanets => "EN AZ 2 CONTROLLED GEZEGEN GEREKLİ",
            CenterUpgradeRejectReason.NotInRange => "ÇAPAYA YAKLAŞ",
            CenterUpgradeRejectReason.TooFast => "BAĞIL HIZ ÇOK YÜKSEK",
            CenterUpgradeRejectReason.InsufficientCargo => "YETERSİZ KAYNAK",
            _ => "BAŞARISIZ"
        };
    }
}
