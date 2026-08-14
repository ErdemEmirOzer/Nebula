using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.Simulation;
using Nebula.Visuals;

namespace Nebula.Extraction
{
    /// <summary>
    /// The ship-side driver of the extraction slice: nearest-planet tracking,
    /// R-hold scanning, B/Esc surface build mode with Orbit Stabilize, F to
    /// confirm the pioneer-kit drill on the drill node, and T to transfer a
    /// nearby active drill's output into the ship's logistics cargo. All
    /// temporary key bindings are gathered here for easy rebinding — this is
    /// not the final input decision.
    /// </summary>
    public class ShipExtraction : MonoBehaviour
    {
        // Temporary controls (central, easy to change; shown on the HUD).
        public const KeyCode ScanKey = KeyCode.R;
        public const KeyCode BuildModeKey = KeyCode.B;
        public const KeyCode ConfirmKey = KeyCode.F;
        public const KeyCode TransferKey = KeyCode.T;
        public const KeyCode CommandKey = KeyCode.K;
        public const KeyCode LaserKey = KeyCode.L;
        public const KeyCode ExitKey = KeyCode.Escape;

        // T0 defaults (1.5R / 8s); raised by the Scanner module's tier via
        // ApplyScannerTier (VS-006 §3: T1 = 2R/6s, T2 = 3R/4s).
        private float scanSeconds = 8f;
        private float proximityR = 1.5f;   // "near enough" for scan and build, in planet radii
        private const float TransferVolumePerSecond = 20f;

        /// <summary>Applies the Scanner module's current tier to scan radius/duration. Tier 0 (or below) keeps the T0 defaults.</summary>
        public void ApplyScannerTier(int tier)
        {
            if (tier <= 0) { proximityR = 1.5f; scanSeconds = 8f; return; }
            int index = Mathf.Clamp(tier - 1, 0, ModuleCatalog.ScanRadiusMultiplierByTier.Length - 1);
            proximityR = ModuleCatalog.ScanRadiusMultiplierByTier[index];
            scanSeconds = ModuleCatalog.ScanSecondsByTier[index];
        }

        public float ScannerScanSeconds => scanSeconds;
        public float ScannerProximityR => proximityR;

        // Relative speed is intentionally NOT gated: planets orbit at thousands
        // of units/sec here, so a hard speed limit made scan/build/transfer
        // unreachable by hand. Proximity is enough; Orbit Stabilize matches the
        // planet's motion for the actual build. (Flexibility over realism.)
        private float TransferRange(CelestialBody body) => Mathf.Max(250f, body.Radius * 2.5f);

        private ShipController ship;
        private ShipLogistics logistics;
        private ShipModules modules;

        /// <summary>VS-014 §2: which defense node Build Mode's L key targets — 0 = Laser (existing), 1 = Plasma (new). Cycled with [ and ] while in Build Mode only.</summary>
        public int SelectedDefenseNodeIndex { get; private set; }

        /// <summary>How many real surface-defense nodes a planet profile offers. Two: Laser and Plasma.</summary>
        public const int DefenseNodeCount = 2;

        /// <summary>
        /// Moves the defense-node selection. The seam <c>[</c> / <c>]</c> and the
        /// planet panel's node stepper both call — selecting a node was previously
        /// reachable only by pressing a bracket key inside build mode, with the field's
        /// setter private, so no control could exist for it at all.
        /// </summary>
        public void StepDefenseNode(int delta)
        {
            if (delta == 0) return;
            SelectedDefenseNodeIndex =
                ((SelectedDefenseNodeIndex + delta) % DefenseNodeCount + DefenseNodeCount) % DefenseNodeCount;
        }

        /// <summary>Selects a node outright, for a control that names the node rather than stepping.</summary>
        public void SelectDefenseNode(int index) =>
            SelectedDefenseNodeIndex = Mathf.Clamp(index, 0, DefenseNodeCount - 1);

        public PlanetIdentity TargetPlanet { get; private set; }
        public PlanetProfile TargetProfile { get; private set; }
        public float ScanProgress { get; private set; }
        public bool BuildMode { get; private set; }
        public string StatusMessage { get; private set; }
        public bool Transferring { get; private set; }
        /// <summary>Why the scan isn't progressing (for the HUD), or null when it is.</summary>
        public string ScanBlockedReason { get; private set; }

        private float scanElapsed;
        private float messageUntil;

        public LogisticsCargo Cargo => logistics != null ? logistics.Cargo : null;

        /// <summary>
        /// The module system, resolved on first use rather than in <see cref="Awake"/>.
        /// <para>
        /// <c>ShipFactory</c> adds this component roughly thirty lines before it adds
        /// <c>ShipModules</c>, so an <c>Awake</c> lookup cached <c>null</c> — and
        /// permanently, because nothing ever asked again. Both tier readers below sit
        /// behind a <c>modules != null ? … : 0</c> guard, so the failure was silent:
        /// Station Construction and Weapon Coordination simply read 0 forever, which
        /// puts the Drill T1→T2 upgrade and Plasma T1 out of reach in a real game
        /// while every gate still reports a tidy "needs tier N" refusal.
        /// </para>
        /// <para>
        /// Resolved lazily rather than by reordering the factory: <c>ShipModules.Awake</c>
        /// reads six components of its own, so moving it earlier trades this bug for
        /// an unknown number of the same shape.
        /// </para>
        /// </summary>
        private ShipModules ModuleHost => modules != null ? modules : modules = GetComponent<ShipModules>();

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            logistics = GetComponent<ShipLogistics>();
            // modules is deliberately not resolved here; see ModuleHost above.
        }

        /// <summary>
        /// Tells the router when build mode is listening, so the two keys it shares
        /// with the processing panel can be arbitrated. Asked rather than pushed, so
        /// the answer is right on the frame build mode opens or closes regardless of
        /// which component's Update ran first.
        /// </summary>
        private void OnEnable()
        {
            NebulaInputRouter.SetContextSource(InputContext.Extraction,
                () => TargetPlanet != null && !CombatInputContext.WorldInteractionBlocked && !InputGate.Suppressed);
            NebulaInputRouter.SetContextSource(InputContext.BuildMode, () => BuildMode);
        }

        private void OnDisable()
        {
            NebulaInputRouter.SetContextSource(InputContext.Extraction, null);
            NebulaInputRouter.SetContextSource(InputContext.BuildMode, null);
        }

        private void Update()
        {
            if (InputGate.Suppressed || CombatInputContext.WorldInteractionBlocked) return;
            UpdateTarget();
            HandleScan();
            HandleBuildMode();
            HandleTransfer();
        }

        // --- Target ----------------------------------------------------------
        private void UpdateTarget()
        {
            PlanetIdentity best = null;
            float bestSqr = float.MaxValue;
            var bodies = GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBody body = bodies[i];
                if (body == null || body.BodyType != CelestialBodyType.Planet) continue;
                var id = body.GetComponent<PlanetIdentity>();
                if (id == null) continue;

                float interest = body.Radius * 3f;
                float sqr = (body.transform.position - transform.position).sqrMagnitude;
                if (sqr < interest * interest && sqr < bestSqr) { bestSqr = sqr; best = id; }
            }

            if (best != TargetPlanet)
            {
                TargetPlanet = best;
                TargetProfile = best != null ? ExtractionState.GetProfile(best) : null;
                scanElapsed = 0f;
                ScanProgress = 0f;
                // A latch is a standing instruction about *this* planet. Carrying it to
                // the next one would restart a scan the player never asked for.
                ScanLatched = false;
            }
        }

        public float Altitude(PlanetIdentity planet) =>
            Vector3.Distance(transform.position, planet.Body.transform.position) - planet.Body.Radius;

        /// <summary>
        /// How fast the ship is really moving relative to a body.
        /// <para>
        /// While Orbit Stabilize is holding the ship on that body it rides the
        /// body's own motion — <see cref="ShipController.StabilizeTo"/> zeroes
        /// the inertial velocity precisely because free translation is off, and
        /// the ship is carried by the anchor instead. Comparing the two raw
        /// velocities in that state reports the body's whole orbital speed as
        /// "relative", which is the opposite of the truth and made every gate
        /// that requires both stabilisation and a low relative speed
        /// unsatisfiable at once. Holding station on a body means zero relative
        /// speed to it, which is what this now says.
        /// </para>
        /// </summary>
        public float RelativeSpeed(CelestialBody body)
        {
            if (body == null) return ship.Velocity.magnitude;
            if (ship.StabilizedAnchor != null && ship.StabilizedAnchor == body.transform) return 0f;
            return (ship.Velocity - body.Velocity).magnitude;
        }

        // --- Scan ------------------------------------------------------------

        /// <summary>
        /// True while a scan is running because the player asked for one from a
        /// control rather than by holding the key.
        /// <para>
        /// Scanning is a hold, and a hold has no button. A <see cref="UnityEngine.UI.Button"/>
        /// cannot be held by a gamepad or by keyboard focus, so the mouse-only pointer
        /// gestures are not an option either — the same press has to be reachable three
        /// ways. A latch is the honest equivalent: it feeds the exact same
        /// <see cref="TickScan"/> the key feeds, at the same rate, with the same range
        /// gate, and the only difference is that the "hold" is being remembered instead
        /// of sampled.
        /// </para>
        /// </summary>
        public bool ScanLatched { get; private set; }

        /// <summary>
        /// Starts or stops a latched scan and says which it did. The HUD's Tara control
        /// calls this; the key still holds. Never silent: refusing to start says why.
        /// </summary>
        public bool ToggleScanLatch()
        {
            if (ScanLatched) { ScanLatched = false; SetMessage("TARAMA DURDURULDU"); return false; }
            if (TargetPlanet == null) { SetMessage("MENZİLDE GEZEGEN YOK"); return false; }
            if (TargetProfile == null || TargetProfile.Scanned) { SetMessage("GEZEGEN ZATEN TARANDI"); return false; }

            ScanLatched = true;
            SetMessage("TARAMA SÜRÜYOR");
            return true;
        }

        private void HandleScan() =>
            TickScan(Time.deltaTime, NebulaInputRouter.Held(NebulaInputActions.ExtractionScan) || ScanLatched);

        /// <summary>
        /// Advances the planet scan by one step, given how long the step was and
        /// whether the scan control is held. Completing it runs the same shared,
        /// idempotent <see cref="ExtractionState.CompletePlanetScan"/> a Scout's
        /// remote scan does.
        /// <para>
        /// Split from the input read so the scan can be driven by something other
        /// than a held key. Everything it does — the range gate, the reset on
        /// release, the completion pulse and message — was previously unreachable
        /// except by physically holding the key down, which no automated test in
        /// this project can do.
        /// </para>
        /// </summary>
        public void TickScan(float deltaTime, bool scanHeld)
        {
            if (TargetPlanet == null || TargetProfile == null || TargetProfile.Scanned)
            {
                scanElapsed = 0f; ScanProgress = 0f; ScanLatched = false; return;
            }

            float altitude = Altitude(TargetPlanet);
            float rangeLimit = TargetPlanet.Body.Radius * proximityR;
            bool inRange = altitude <= rangeLimit;

            if (!scanHeld) { scanElapsed = 0f; ScanProgress = 0f; ScanBlockedReason = null; return; }

            ScanBlockedReason = inRange ? null : $"DAHA YAKINA GELİN (YÜKSEKLİK {altitude:0}, EN ÇOK {rangeLimit:0})";

            if (inRange)
            {
                scanElapsed += deltaTime;
                ScanProgress = Mathf.Clamp01(scanElapsed / scanSeconds);
                if (scanElapsed >= scanSeconds)
                {
                    ScanBlockedReason = null;
                    ScanLatched = false; // the standing instruction is finished, not repeated
                    // Shared, idempotent completion path (VS-007 §4) — a Scout
                    // vehicle's remote scan routes through the exact same call.
                    ExtractionState.CompletePlanetScan(TargetPlanet);
                    NebulaVfxPulse.Emit(
                        NebulaCommonVfx.ScanPulse, TargetPlanet.Body.transform.position,
                        TargetPlanet.Body.Radius * 2.4f,
                        new Color(0.25f, 0.9f, 1f, 0.8f), 1.1f);
                    SetMessage($"TARAMA TAMAMLANDI — {ExtractionCatalog.ResourceNames[(int)TargetProfile.Family].ToUpperInvariant()} · {ExtractionCatalog.RarityNames[(int)TargetProfile.Rarity].ToUpperInvariant()}");
                }
            }
            else
            {
                scanElapsed = 0f; // range/speed loss resets
                ScanProgress = 0f;
            }
        }

        // --- Build mode ------------------------------------------------------
        /// <summary>Building only needs a scanned planet you are near — nothing about speed or a tight orbit band.</summary>
        private bool BuildEligible(out string reason)
        {
            reason = null;
            if (TargetPlanet == null) { reason = "MENZİLDE GEZEGEN YOK"; return false; }
            if (ExtractionState.IsEvacuationLocked(TargetPlanet.WorldId)) { reason = "TAHLİYE SÜRÜYOR"; return false; }
            if (TargetProfile == null || !TargetProfile.Scanned) { reason = "ÖNCE GEZEGENİ TARAYIN"; return false; }
            float alt = Altitude(TargetPlanet);
            float rangeLimit = TargetPlanet.Body.Radius * proximityR;
            if (alt > rangeLimit) { reason = $"DAHA YAKINA GELİN (YÜKSEKLİK {alt:0}, EN ÇOK {rangeLimit:0})"; return false; }
            return true;
        }

        private void HandleBuildMode()
        {
            bool eligible = BuildEligible(out string reason);

            // B toggles Orbit Stabilize as an optional comfort — not required to build.
            if (NebulaInputRouter.Pressed(NebulaInputActions.ExtractionBuildMode)) ToggleBuildMode();
            if (BuildMode && (!eligible || !ship.IsStabilized)) ExitBuildMode();

            // Through the router's shared-intent read rather than Input.GetKeyDown on
            // the constant. Escape stays deliberately unarbitrated -- every surface
            // closes its own thing -- but the key now comes from the catalog row, so
            // it is one declaration instead of a literal frozen at this call site.
            if (BuildMode && NebulaInputRouter.SharedPressed(NebulaInputActions.ExtractionExit)) ExitBuildMode();

            // VS-014 §2: [ and ] cycle which defense node L targets, only while in Build
            // Mode. Through the router, because the processing panel reads the same two
            // keys for its slot selector and both sub-modes can be open at once: one
            // press must reach one of them, and while that panel is open it is the one
            // the player is looking at.
            // Through StepDefenseNode, not an assignment inlined here. The two keys used
            // to flip the index themselves, which meant the planet panel's stepper and
            // the keyboard were two separate statements of one rule -- and the panel's
            // own comment already claimed they shared the seam. With two nodes the
            // observable result is the same either way, so this changes no behaviour;
            // what it changes is that a third node cannot arrive and be reachable from
            // the stepper while the brackets keep toggling between the first two.
            if (BuildMode)
            {
                if (NebulaInputRouter.Pressed(NebulaInputActions.ExtractionPrevDefenseNode))
                    StepDefenseNode(-1);
                if (NebulaInputRouter.Pressed(NebulaInputActions.ExtractionNextDefenseNode))
                    StepDefenseNode(1);
            }

            // F drill, K command (build or upgrade), L defense (build or upgrade the selected node) — all just need to be near a scanned planet.
            if (NebulaInputRouter.Pressed(NebulaInputActions.ExtractionConfirm)) TryBuildOrUpgradeDrill();
            // K carries no eligibility check of its own any more: the seam decides,
            // so the key and the panel's button cannot disagree about whether this
            // planet can take a Command Center or about what to say when it cannot.
            if (NebulaInputRouter.Pressed(NebulaInputActions.ExtractionCommand)) TryBuildOrUpgradeCommand();
            if (NebulaInputRouter.Pressed(NebulaInputActions.ExtractionLaser)) TryBuildOrUpgradeDefense();

            // Esc cancels a not-yet-active drill and returns the kit.
            if (!BuildMode && NebulaInputRouter.SharedPressed(NebulaInputActions.ExtractionExit) && TargetPlanet != null)
            {
                LogisticsCargo cargo = logistics != null ? logistics.Cargo : null;
                DrillStructure pendingDrill = FindPreActiveDrillOnTarget();
                SurfaceStructure pendingStructure = FindPreActiveStructureOnTarget();
                // VS-017A.2 §B3: an in-flight T1->T2 upgrade cancels through the very same
                // control, refunding floor(75%) of whatever is still reserved.
                DrillStructure upgradingDrill = FindUpgradingDrillOnTarget();
                if (pendingDrill != null) { pendingDrill.Cancel(cargo); SetMessage("İNŞA İPTAL EDİLDİ — MALİYET İADE EDİLDİ"); }
                else if (upgradingDrill != null) { upgradingDrill.CancelUpgrade(cargo); SetMessage("SONDAJ T2 YÜKSELTMESİ İPTAL — %75 İADE EDİLDİ"); }
                else if (pendingStructure != null) { pendingStructure.Cancel(cargo); SetMessage("İNŞA İPTAL EDİLDİ — %75 İADE EDİLDİ"); }
            }
        }

        /// <summary>
        /// Orbit Stabilize on or off, and the reason when it will not go on.
        /// <para>
        /// Lifted out of the key handler so the HUD's own YÖRÜNGE SABİTLE control
        /// drives the identical path — it was three inline statements behind
        /// <c>Pressed</c>, which is why the control beside it on the bar was a picture
        /// of a key rather than a button.
        /// </para>
        /// </summary>
        public bool ToggleBuildMode()
        {
            if (BuildMode) { ExitBuildMode(); return false; }
            if (!BuildEligible(out string reason)) { SetMessage(reason); return false; }

            BuildMode = true;
            ship.StabilizeTo(TargetPlanet.Body.transform);
            // No key named in the text. The HUD prints the current prompt beside the
            // verb and Onayla is rebindable, so a hard-coded "F" here would be wrong
            // for anyone who moved it.
            SetMessage("YÖRÜNGE SABİTLENDİ — KURULUM İÇİN ONAYLAYIN");
            return true;
        }

        private void ExitBuildMode()
        {
            BuildMode = false;
            ship.ReleaseStabilize();
        }

        // P1-DATA-011: read from the drill's own catalog row rather than re-declaring
        // its price here. The two names stay because the transaction below reads like
        // a sentence with them, but neither is a second declaration of the number any
        // more — change ExtractionCatalog.DrillT1PartsCost and every site moves,
        // including the Quick Asset Package and the card that prints the price.
        private const int SecondDrillPartsCost = ExtractionCatalog.DrillT1PartsCost;
        private static readonly int SecondDrillPartsRefund = ExtractionCatalog.DrillT1RefundParts;

        /// <summary>VS-017A.2 §B1: the existing Station Construction module tier — the drill T2 upgrade's only functional gate. No new gate authority.</summary>
        private int StationConstructionTier => ModuleHost != null ? ModuleHost.Modules.GetTier(DrillUpgradeCatalog.GateModuleId) : 0;

        /// <summary>Read-only view of the same gate tier for the HUD — never a second gate authority.</summary>
        public int StationConstructionTierForHud => StationConstructionTier;

        private DrillStructure FindActiveDrillOnTarget()
        {
            foreach (DrillStructure drill in ExtractionState.Drills)
                if (drill != null && drill.WorldId == TargetPlanet.WorldId && drill.Runtime.State == DrillLifecycle.Active) return drill;
            return null;
        }

        private void ConfirmBuild()
        {
            // VS-017A.2 §B6: the same "build if empty, else upgrade" flow K/L already use for
            // Command/Laser -- no new screen, no new key.
            if (DrillOnTargetExists())
            {
                DrillStructure existing = FindActiveDrillOnTarget();
                if (existing == null) { SetMessage(DrillUpgradeRejectMessage(DrillUpgradeRejectReason.NotActive)); return; }
                if (existing.ActiveUpgradeJob != null) { SetMessage(DrillUpgradeRejectMessage(DrillUpgradeRejectReason.AlreadyUpgrading)); return; }

                DrillUpgradeRejectReason reason = existing.StartUpgrade(StationConstructionTier, Cargo);
                if (reason == DrillUpgradeRejectReason.None)
                {
                    SetMessage($"SONDAJ T2 YÜKSELTMESİ BAŞLADI — {DrillUpgradeCatalog.UpgradeSeconds:0}s · ÜRETİM DURDU");
                    ExitBuildMode();
                }
                else SetMessage(DrillUpgradeRejectMessage(reason));
                return;
            }

            // First drill uses the one-shot pioneer kit; every later drill
            // costs 20 Basic Building Parts from cargo.
            if (ExtractionState.Kit != ExtractionState.KitState.Consumed)
            {
                if (!ExtractionState.ReserveKit() && ExtractionState.Kit != ExtractionState.KitState.Reserved)
                { SetMessage("ÖNCÜ KİTİ KULLANILAMIYOR"); return; }
                DrillStructure.Create(TargetPlanet, TargetProfile, transform.position, DrillStructure.BuildSource.Kit, 0);
                SetMessage("ÖNCÜ KİTİ BIRAKILDI — KAPSÜL YOLDA");
            }
            else
            {
                LogisticsCargo cargo = logistics != null ? logistics.Cargo : null;
                // Turkish at the producer, and the item's name read from the catalog
                // rather than spelled out. The old line was "NEED 20 BASIC BUILDING
                // PARTS", which the feedback channel's prefix rule turned into
                // "GEREKSİNİM KARŞILANMIYOR · NEED 20 BASIC BUILDING PARTS" -- half a
                // sentence in each language. Same shape as CanAfford below.
                if (!TryPaySecondDrillCost(cargo))
                { SetMessage($"{SecondDrillPartsCost} {ProcessingCatalog.ItemDisplayName(ProcessingCatalog.BasicPartId)} GEREKLİ"); return; }
                DrillStructure.Create(TargetPlanet, TargetProfile, transform.position,
                    DrillStructure.BuildSource.Parts, SecondDrillPartsRefund);
                SetMessage($"SONDAJ BIRAKILDI ({SecondDrillPartsCost} PARÇA) — KAPSÜL YOLDA");
            }
            ExitBuildMode();
        }

        /// <summary>Clickable/key-shared drill command. Eligibility, payment and mutation remain here.</summary>
        public bool TryBuildOrUpgradeDrill()
        {
            if (!BuildEligible(out string reason)) { SetMessage(reason); return false; }
            ConfirmBuild();
            return true;
        }

        /// <summary>
        /// The second-drill transaction seam. Availability and payment are one
        /// all-or-nothing operation, so stock reserved by another owner cannot
        /// pass a stored-total precheck and then produce a free refundable drill.
        /// </summary>
        public static bool TryPaySecondDrillCost(LogisticsCargo cargo) =>
            cargo != null && cargo.TryConsumeImmediate(ProcessingCatalog.BasicPartId, SecondDrillPartsCost);

        private int CharacterLevel => Progression.ProgressionService.State.Level;
        private int WeaponCoordinationTier => ModuleHost != null ? ModuleHost.Modules.GetTier("defense.weapon_coordination") : 0;

        private SurfaceStructure FindStructureOnTarget(SurfaceStructureType type) => ExtractionState.FindStructure(TargetPlanet.WorldId, type);

        // --- Command Center: the one production seam (§3) ----------------------
        // Was a private method reachable only by holding K down, with three silent
        // `return`s in it and no control anywhere on screen. Both halves are public
        // now and both callers -- the key and the panel's button -- go through the
        // same two methods, so there is exactly one place where a Command Center
        // can come into existence.

        /// <summary>
        /// What the Command action would do right now, and whether it may. Pure:
        /// it reserves nothing, spends nothing and changes nothing, so the panel
        /// can ask it five times a second.
        /// </summary>
        public CommandBuildAssessment AssessCommandAction() =>
            CommandBuildService.Evaluate(CommandContext());

        /// <summary>
        /// The typed result of the last Command action, for as long as feedback
        /// should stay on screen. The one channel the key and the button share, so
        /// a player cannot tell which of the two they used from what they read.
        /// </summary>
        public CommandActionOutcome CommandFeedback =>
            Time.unscaledTime < commandFeedbackUntil ? lastCommandOutcome : default;

        private CommandActionOutcome lastCommandOutcome;
        private float commandFeedbackUntil;

        private CommandBuildContext CommandContext()
        {
            if (TargetPlanet == null || TargetProfile == null) return default;

            SurfaceNode node = TargetProfile.CommandNode;
            SurfaceStructure existing = FindStructureOnTarget(SurfaceStructureType.Command);
            return new CommandBuildContext(
                TargetPlanet.WorldId, TargetProfile,
                Altitude(TargetPlanet), TargetPlanet.Body.Radius * proximityR,
                ExtractionState.IsEvacuationLocked(TargetPlanet.WorldId),
                ExtractionState.IsNodeOccupied(TargetPlanet.WorldId, node.Id),
                existing, Cargo, CharacterLevel, WeaponCoordinationTier,
                ExtractionState.HasAnyControlledPlanet(),
                SurfaceStructure.CapsuleSecondsFor(transform.position, TargetPlanet.Body));
        }

        /// <summary>
        /// Builds a fresh Command T1 if the node is empty, else starts the T1→T2
        /// upgrade — the structure stays fully Active throughout its own upgrade.
        /// <para>
        /// The only mutating entry. It re-evaluates rather than trusting anything
        /// the caller passed, so a stale panel and a double click cannot each
        /// produce a structure: the second call sees the first one's capsule
        /// already in transit and refuses.
        /// </para>
        /// <para>
        /// Never returns silently. Every path — no target, no cargo, no profile —
        /// records a typed outcome the interface can read and say out loud.
        /// </para>
        /// </summary>
        public CommandActionOutcome TryBuildOrUpgradeCommand()
        {
            CommandBuildAssessment assessment = AssessCommandAction();

            if (!assessment.CanAct)
                return RecordCommandOutcome(new CommandActionOutcome(false, assessment.Action,
                    assessment.BuildReject, assessment.UpgradeReject));

            if (assessment.Action == CommandActionKind.BuildT1)
            {
                SurfaceStructure created = SurfaceStructure.Create(TargetPlanet, TargetProfile, SurfaceStructureCatalog.Command,
                    TargetProfile.CommandNode, transform.position, Cargo,
                    CommandBuildService.BuildT1Cost(TargetProfile.Family));
                if (created == null)
                    return RecordCommandOutcome(new CommandActionOutcome(false, CommandActionKind.BuildT1,
                        CommandBuildRejectReason.InsufficientResources, StructureUpgradeRejectReason.None));
                ExitBuildMode();
                return RecordCommandOutcome(new CommandActionOutcome(true, CommandActionKind.BuildT1,
                    CommandBuildRejectReason.None, StructureUpgradeRejectReason.None));
            }

            SurfaceStructure existing = FindStructureOnTarget(SurfaceStructureType.Command);
            StructureUpgradeRejectReason reason = existing.StartUpgrade(CharacterLevel, existing.Runtime.Tier,
                WeaponCoordinationTier, ExtractionState.HasAnyControlledPlanet(), Cargo);
            if (reason == StructureUpgradeRejectReason.None) ExitBuildMode();
            return RecordCommandOutcome(new CommandActionOutcome(reason == StructureUpgradeRejectReason.None,
                CommandActionKind.UpgradeT2, CommandBuildRejectReason.None, reason));
        }

        /// <summary>
        /// Publishes the outcome on both channels at once: the typed one the panel
        /// reads, and the transient string channel, in the same Turkish the panel
        /// shows. Neither can now say something the other does not.
        /// </summary>
        private CommandActionOutcome RecordCommandOutcome(CommandActionOutcome outcome)
        {
            lastCommandOutcome = outcome;
            // unscaledTime like every sibling TTL: on the scaled clock the Command
            // card's outcome line never expires while a menu holds timeScale at 0.
            commandFeedbackUntil = Time.unscaledTime + MessageSeconds;
            SetMessage(CommandBuildText.OutcomeText(outcome));
            return outcome;
        }

        /// <summary>L: builds the selected defense node's T1 if empty, else starts (or reports the gate blocking) its T1->T2 upgrade. Node 0 = Laser (existing), node 1 = Plasma (new, needs Command T2 + Weapon Coordination T1 to even start building).</summary>
        private void BuildOrUpgradeDefense()
        {
            SurfaceStructureType type = SelectedDefenseNodeIndex == 0 ? SurfaceStructureType.Laser : SurfaceStructureType.Plasma;
            SurfaceNode node = TargetProfile.DefenseNode(SelectedDefenseNodeIndex);
            SurfaceStructure existing = ExtractionState.IsNodeOccupied(TargetPlanet.WorldId, node.Id)
                ? FindStructureAtNode(node.Id) : null;

            if (existing != null)
            {
                SurfaceStructure commandStructure = FindStructureOnTarget(SurfaceStructureType.Command);
                int commandTier = commandStructure != null && commandStructure.Runtime.IsActive ? commandStructure.Runtime.Tier : 0;
                StructureUpgradeRejectReason reason = existing.StartUpgrade(CharacterLevel, commandTier, WeaponCoordinationTier, ExtractionState.HasAnyControlledPlanet(), Cargo);
                SetMessage(reason == StructureUpgradeRejectReason.None ? "T2 YÜKSELTMESİ BAŞLADI" : UpgradeRejectMessage(reason));
                if (reason == StructureUpgradeRejectReason.None) ExitBuildMode();
                return;
            }

            if (SelectedDefenseNodeIndex != 0) // Plasma T1 fresh build has its own gate, checked before any node-occupied concern
            {
                SurfaceStructure commandStructure = FindStructureOnTarget(SurfaceStructureType.Command);
                int commandTier = commandStructure != null && commandStructure.Runtime.IsActive ? commandStructure.Runtime.Tier : 0;
                StructureTierDef plasmaT1 = PlanetaryDefenseCatalog.PlasmaT1;
                if (CharacterLevel < plasmaT1.RequiredCharacterLevel) { SetMessage($"SEVİYE {plasmaT1.RequiredCharacterLevel} GEREKLİ"); return; }
                if (commandTier < plasmaT1.RequiredCommandTier) { SetMessage("KOMUTA MERKEZİ T2 GEREKLİ"); return; }
                if (WeaponCoordinationTier < plasmaT1.RequiredWeaponCoordinationTier) { SetMessage("SİLAH KOORDİNASYONU T1 GEREKLİ"); return; }
                LogisticsCargo plasmaCargo = Cargo;
                if (plasmaCargo == null) { SetMessage("KARGO YOK"); return; }
                List<(string, float)> plasmaCost = plasmaT1.FullCost();
                if (!HasAllCost(plasmaCost, plasmaCargo)) return;
                if (SurfaceStructure.Create(TargetPlanet, TargetProfile, SurfaceStructureCatalog.Plasma, node, transform.position, plasmaCargo, plasmaCost) == null)
                { SetMessage("YETERSİZ KAYNAK"); return; }
                SetMessage("PLAZMA SAVUNMASI — KAPSÜL YOLDA");
                ExitBuildMode();
                return;
            }

            SurfaceStructureDef laserDef = SurfaceStructureCatalog.Laser;
            if (!ExtractionState.HasActiveCommand(TargetPlanet.WorldId)) { SetMessage("ETKİN BİR KOMUTA MERKEZİ GEREKLİ"); return; }
            if (!FindMaterialWithAtLeast(laserDef.MaterialCost, out string materialId)) { SetMessage($"HERHANGİ BİR ÖZEL MALZEMEDEN {laserDef.MaterialCost} GEREKLİ"); return; }
            LogisticsCargo laserCargo = Cargo;
            if (laserCargo == null) { SetMessage("KARGO YOK"); return; }
            var laserCost = new List<(string, float)> { (ProcessingCatalog.BasicPartId, laserDef.PartsCost), (materialId, laserDef.MaterialCost) };
            if (!HasAllCost(laserCost, laserCargo)) return;
            if (SurfaceStructure.Create(TargetPlanet, TargetProfile, laserDef, node, transform.position, laserCargo, laserCost) == null)
            { SetMessage("YETERSİZ KAYNAK"); return; }
            SetMessage("LAZER SAVUNMASI — KAPSÜL YOLDA");
            ExitBuildMode();
        }

        /// <summary>
        /// Read-only view of the shared "near a scanned, unlocked planet" gate every
        /// build command starts from. Pure — it reserves nothing and says nothing — so
        /// a card can ask it on a repaint to decide whether its control is interactable
        /// and what to print underneath when it is not.
        /// </summary>
        public bool BuildEligibleNow(out string reason) => BuildEligible(out reason);

        /// <summary>Clickable/key-shared defense command for the currently selected real node.</summary>
        public bool TryBuildOrUpgradeDefense()
        {
            if (!BuildEligible(out string reason)) { SetMessage(reason); return false; }
            BuildOrUpgradeDefense();
            return true;
        }

        private SurfaceStructure FindStructureAtNode(string nodeId)
        {
            var structures = ExtractionState.Structures;
            for (int i = 0; i < structures.Count; i++)
                if (structures[i] != null && structures[i].WorldId == TargetPlanet.WorldId && structures[i].NodeId == nodeId) return structures[i];
            return null;
        }

        // --- VS-015 §7: Sensor Array / Maintenance Platform -------------------------
        public int SelectedSupportNodeIndex { get; private set; }
        public void ToggleSupportNodeSelection() => SelectedSupportNodeIndex = SelectedSupportNodeIndex == 0 ? 1 : 0;

        /// <summary>
        /// VS-015 §7's Sensor Array gate: is a foreign Entity or Anomaly actually near
        /// the planet being built on?
        /// <para>
        /// It used to answer a different question entirely (P1-ECO-014): "does any
        /// Anomaly POI exist anywhere in the loaded system". That measured neither of
        /// the two words in its own name. No Entity ever counted — a system full of
        /// hostiles left the gate shut — and an Anomaly on the far side of the star
        /// satisfied a condition about *this planet's* neighbourhood, so the card read
        /// "YABANCI VARLIK · HAZIR" for a planet with nothing near it.
        /// </para>
        /// <para>
        /// Both halves are real now. A live enemy Entity counts, and both kinds must be
        /// within the planet's own defensive envelope of its surface — the same
        /// distance the defenses a Sensor Array serves actually reach
        /// (<see cref="PlanetaryDefenseCatalog.PlanetDefenseEnvelopeRange"/>), rather
        /// than a number invented for this gate alone.
        /// </para>
        /// </summary>
        public bool HasForeignEntityNearby()
        {
            if (TargetPlanet == null || TargetPlanet.Body == null) return false;
            Vector3 planetCenter = TargetPlanet.Body.transform.position;
            float reach = TargetPlanet.Body.Radius + PlanetaryDefenseCatalog.PlanetDefenseEnvelopeRange;
            float reachSqr = reach * reach;

            foreach (Nebula.Combat.EnemyAgent enemy in Nebula.Combat.EnemyAgent.All.Values)
                if (enemy != null && !enemy.Destroyed
                    && (enemy.transform.position - planetCenter).sqrMagnitude <= reachSqr) return true;

            foreach (Poi.PoiObject p in Poi.PoiObject.All)
                if (p != null && p.Manifest != null && p.Manifest.Type == Poi.PoiType.Anomaly
                    && (p.transform.position - planetCenter).sqrMagnitude <= reachSqr) return true;

            return false;
        }

        public bool HasEngineerInSystem()
        {
            string systemId = TargetPlanet != null ? Sectors.WorldIdCodec.SystemIdFromPlanetWorldId(TargetPlanet.WorldId) : null;
            if (systemId == null) return false;
            foreach (Hangar.VehicleAgent v in Hangar.VehicleAgent.Registry.Values)
                if (v != null && v.Def != null && v.Def.Class == Hangar.VehicleClass.Engineer && v.gameObject.activeSelf && v.HomeSystemId == systemId) return true;
            return false;
        }

        /// <summary>VS-015.1 §5: read-only lookup for the UI panel — the currently-built support structure at the given node index (0 = Sensor Array, 1 = Maintenance Platform) on the current target planet, or null if unbuilt.</summary>
        public SurfaceStructure FindSupportStructure(int nodeIndex)
        {
            if (TargetPlanet == null || TargetProfile == null) return null;
            SurfaceNode node = TargetProfile.SupportNode(nodeIndex);
            return ExtractionState.IsNodeOccupied(TargetPlanet.WorldId, node.Id) ? FindStructureAtNode(node.Id) : null;
        }

        /// <summary>VS-015.1 §5: read-only lookup for the UI panel — the active Command Center's tier on the current target planet, or 0 if none/inactive.</summary>
        public int CommandTierOnTarget()
        {
            SurfaceStructure command = FindStructureOnTarget(SurfaceStructureType.Command);
            return command != null && command.Runtime.IsActive ? command.Runtime.Tier : 0;
        }

        /// <summary>Read-only affordability gate for a UI assessment; reserves and reports nothing.</summary>
        public bool CanAfford(StructureTierDef tier, out string missing)
        {
            missing = null;
            LogisticsCargo cargo = Cargo;
            if (tier == null || cargo == null) { missing = "KARGO YOK"; return false; }
            foreach ((string itemId, float amount) in tier.FullCost())
            {
                if (cargo.GetAvailable(itemId) >= amount - 0.0001f) continue;
                missing = $"{amount:0} {ProcessingCatalog.ItemDisplayName(itemId)} GEREKLİ";
                return false;
            }
            return true;
        }

        /// <summary>K: builds/upgrades the selected support node — 0 = Sensor Array, 1 = Maintenance Platform. Same atomic-reservation/75%-cancel/KZG-preserving path as every other structure.</summary>
        public void BuildOrUpgradeSupport()
        {
            SurfaceStructureType type = SelectedSupportNodeIndex == 0 ? SurfaceStructureType.SensorArray : SurfaceStructureType.MaintenancePlatform;
            SurfaceNode node = TargetProfile.SupportNode(SelectedSupportNodeIndex);
            SurfaceStructure existing = ExtractionState.IsNodeOccupied(TargetPlanet.WorldId, node.Id) ? FindStructureAtNode(node.Id) : null;

            if (existing != null)
            {
                SurfaceStructure commandStructure = FindStructureOnTarget(SurfaceStructureType.Command);
                int commandTier = commandStructure != null && commandStructure.Runtime.IsActive ? commandStructure.Runtime.Tier : 0;
                StructureUpgradeRejectReason reason = existing.StartUpgrade(CharacterLevel, commandTier, WeaponCoordinationTier, ExtractionState.HasAnyControlledPlanet(), Cargo);
                SetMessage(reason == StructureUpgradeRejectReason.None ? "T2 YÜKSELTMESİ BAŞLADI" : UpgradeRejectMessage(reason));
                if (reason == StructureUpgradeRejectReason.None) ExitBuildMode();
                return;
            }

            StructureTierDef t1 = PlanetaryDefenseCatalog.Get(type, 1);
            SurfaceStructure commandStruct = FindStructureOnTarget(SurfaceStructureType.Command);
            int cmdTier = commandStruct != null && commandStruct.Runtime.IsActive ? commandStruct.Runtime.Tier : 0;
            if (CharacterLevel < t1.RequiredCharacterLevel) { SetMessage($"SEVİYE {t1.RequiredCharacterLevel} GEREKLİ"); return; }
            if (cmdTier < t1.RequiredCommandTier) { SetMessage("KOMUTA MERKEZİ T2 GEREKLİ"); return; }
            if (type == SurfaceStructureType.SensorArray && !HasForeignEntityNearby()) { SetMessage("YAKINDA YABANCI BİR VARLIK GEREKLİ"); return; }
            if (type == SurfaceStructureType.MaintenancePlatform && !HasEngineerInSystem()) { SetMessage("GÖREVDE BİR MÜHENDİS GEREKLİ"); return; }
            LogisticsCargo supportCargo = Cargo;
            if (supportCargo == null) { SetMessage("KARGO YOK"); return; }
            List<(string, float)> supportCost = t1.FullCost();
            if (!HasAllCost(supportCost, supportCargo)) return;
            if (SurfaceStructure.Create(TargetPlanet, TargetProfile, SurfaceStructureCatalog.Get(type), node, transform.position, supportCargo, supportCost) == null)
            { SetMessage("YETERSİZ KAYNAK"); return; }
            SetMessage($"{t1.DisplayName.ToUpperInvariant()} — KAPSÜL YOLDA");
            ExitBuildMode();
        }

        /// <summary>VS-015.1 §6: validate-only (never consumes) — every structure build now reserves atomically inside SurfaceStructure.Create itself, all items together or none, so this only needs to confirm affordability first.</summary>
        private bool HasAllCost(List<(string itemId, float amount)> cost, LogisticsCargo cargo)
        {
            foreach (var (itemId, amount) in cost)
                if (cargo.GetAvailable(itemId) < amount - 0.0001f) { SetMessage($"{amount:0} {ProcessingCatalog.ItemDisplayName(itemId)} GEREKLİ"); return false; }
            return true;
        }

        /// <summary>VS-017A.2 §B6: typed drill-upgrade rejection reasons surfaced verbatim to the player — never a silent no-op.</summary>
        public static string DrillUpgradeRejectMessage(DrillUpgradeRejectReason reason) => reason switch
        {
            DrillUpgradeRejectReason.AlreadyUpgrading => "SONDAJ T2 YÜKSELTMESİ ZATEN SÜRÜYOR",
            DrillUpgradeRejectReason.NotActive => "SONDAJ HENÜZ ETKİN DEĞİL",
            DrillUpgradeRejectReason.MaxTier => "SONDAJ ZATEN T2",
            DrillUpgradeRejectReason.ModuleTierNotMet => $"İSTASYON İNŞASI T{DrillUpgradeCatalog.RequiredStationConstructionTier} GEREKLİ",
            DrillUpgradeRejectReason.InsufficientResources => "YETERSİZ KAYNAK",
            _ => ""
        };

        private static string UpgradeRejectMessage(StructureUpgradeRejectReason reason) => reason switch
        {
            StructureUpgradeRejectReason.AlreadyUpgrading => "YÜKSELTME ZATEN SÜRÜYOR",
            StructureUpgradeRejectReason.NotActive => "YAPI ETKİN DEĞİL",
            StructureUpgradeRejectReason.MaxTier => "BU AŞAMADA AZAMİ KADEMEDE",
            StructureUpgradeRejectReason.LevelTooLow => "KARAKTER SEVİYESİ YETERSİZ",
            StructureUpgradeRejectReason.CommandTierNotMet => "KOMUTA MERKEZİ T2 GEREKLİ",
            StructureUpgradeRejectReason.WeaponCoordinationTierNotMet => "DAHA YÜKSEK SİLAH KOORDİNASYONU KADEMESİ GEREKLİ",
            StructureUpgradeRejectReason.NoControlledPlanet => "EN AZ BİR DENETİMLİ GEZEGEN GEREKLİ",
            StructureUpgradeRejectReason.InsufficientResources => "YETERSİZ KAYNAK",
            _ => "YÜKSELTİLEMİYOR"
        };

        private bool FindMaterialWithAtLeast(int amount, out string materialId)
        {
            materialId = null;
            LogisticsCargo cargo = logistics != null ? logistics.Cargo : null;
            if (cargo == null) return false;
            float best = 0f;
            for (int i = 0; i < ProcessingCatalog.MaterialIds.Length; i++)
            {
                float have = cargo.GetAvailable(ProcessingCatalog.MaterialIds[i]);
                if (have >= amount && have > best) { best = have; materialId = ProcessingCatalog.MaterialIds[i]; }
            }
            return materialId != null;
        }

        private bool DrillOnTargetExists()
        {
            var drills = ExtractionState.Drills;
            for (int i = 0; i < drills.Count; i++)
                if (drills[i] != null && drills[i].WorldId == TargetPlanet.WorldId) return true;
            return false;
        }

        /// <summary>VS-017A.2 §B3: the target planet's drill if it currently has an in-flight T1→T2 upgrade.</summary>
        private DrillStructure FindUpgradingDrillOnTarget()
        {
            foreach (DrillStructure drill in ExtractionState.Drills)
                if (drill != null && drill.WorldId == TargetPlanet.WorldId && drill.ActiveUpgradeJob != null) return drill;
            return null;
        }

        private DrillStructure FindPreActiveDrillOnTarget()
        {
            var drills = ExtractionState.Drills;
            for (int i = 0; i < drills.Count; i++)
            {
                DrillStructure drill = drills[i];
                if (drill != null && drill.WorldId == TargetPlanet.WorldId
                    && drill.Runtime.State != DrillLifecycle.Active) return drill;
            }
            return null;
        }

        private SurfaceStructure FindPreActiveStructureOnTarget()
        {
            var structures = ExtractionState.Structures;
            for (int i = 0; i < structures.Count; i++)
            {
                SurfaceStructure s = structures[i];
                if (s != null && s.WorldId == TargetPlanet.WorldId && s.Runtime.CanCancelForRefund) return s;
            }
            return null;
        }

        // --- Transfer --------------------------------------------------------

        /// <summary>Why a drill-to-ship transfer step did nothing. Every one of these was a bare <c>return</c>.</summary>
        public enum TransferRejectReason { None, NoCargo, NoDrillInRange, DrillEmpty, CargoFull }

        /// <summary>
        /// True while a transfer is running because the player asked for one from a
        /// control rather than by holding the key — the same standing-instruction
        /// device <see cref="ScanLatched"/> uses, for the same reason.
        /// </summary>
        public bool TransferLatched { get; private set; }

        /// <summary>The reason the last transfer step refused, so a repeat is not announced every frame.</summary>
        private TransferRejectReason lastTransferReject;

        /// <summary>
        /// Moves <paramref name="seconds"/> worth of a nearby active drill's output
        /// into the hold, and says exactly what happened.
        /// <para>
        /// This is the whole transaction, and it is public because it had to be: the
        /// only path in was a private <c>HandleTransfer</c> reading a held key, with
        /// <b>four</b> bare <c>return</c>s in it — no cargo, no drill in range, an
        /// empty drill and a full hold all looked identical from outside, which is to
        /// say they all looked like a key that did not work. Nothing about the rate,
        /// the range or the volume moved; only the answer is new.
        /// </para>
        /// </summary>
        public TransferRejectReason TryTransfer(float seconds)
        {
            LogisticsCargo cargo = logistics != null ? logistics.Cargo : null;
            if (cargo == null) return TransferRejectReason.NoCargo;

            DrillStructure drill = NearestActiveDrill();
            if (drill == null) return TransferRejectReason.NoDrillInRange;
            if (ExtractionState.FindPlanet(drill.WorldId)?.Body == null) return TransferRejectReason.NoDrillInRange;

            if (cargo.FreeSpace <= 0.0001f) return TransferRejectReason.CargoFull;
            if (drill.Runtime.StoredAmount <= 0.0001f) return TransferRejectReason.DrillEmpty;

            float want = Mathf.Min(TransferVolumePerSecond * Mathf.Max(0f, seconds), cargo.FreeSpace);
            float taken = drill.Runtime.Withdraw(want);
            cargo.Add(ExtractionCatalog.ResourceIds[(int)drill.Runtime.Family], taken);
            Transferring = taken > 0f;
            return taken > 0f ? TransferRejectReason.None : TransferRejectReason.DrillEmpty;
        }

        /// <summary>
        /// Starts or stops a latched transfer and says which it did. Refusing to start
        /// names the reason rather than doing nothing, so a control wired to this can
        /// never be a dead button.
        /// </summary>
        public TransferRejectReason ToggleTransferLatch()
        {
            if (TransferLatched)
            {
                TransferLatched = false;
                SetMessage("AKTARIM DURDURULDU");
                return TransferRejectReason.None;
            }

            // Asked for a zero-length step first: it runs every gate the real transfer
            // runs, in the real order, and moves nothing. A latch that would refuse on
            // its first frame is refused now, with the reason, instead of switching on
            // and off again too fast to read.
            TransferRejectReason reason = TryTransfer(0f);
            if (reason != TransferRejectReason.None) { SetRejection(TransferRejectText(reason)); return reason; }

            TransferLatched = true;
            lastTransferReject = TransferRejectReason.None;
            SetMessage("AKTARIM SÜRÜYOR");
            return TransferRejectReason.None;
        }

        /// <summary>Turkish, player-facing, one line per reason. Never the enum's own name.</summary>
        public static string TransferRejectText(TransferRejectReason reason) => reason switch
        {
            TransferRejectReason.NoCargo => "KARGO YOK",
            TransferRejectReason.NoDrillInRange => "MENZİLDE ETKİN SONDAJ YOK",
            TransferRejectReason.DrillEmpty => "SONDAJDA AKTARILACAK KAYNAK YOK",
            TransferRejectReason.CargoFull => "KARGO DOLU",
            _ => null
        };

        private void HandleTransfer()
        {
            Transferring = false;

            bool held = NebulaInputRouter.Held(NebulaInputActions.ExtractionTransfer);
            if (!held && !TransferLatched) { lastTransferReject = TransferRejectReason.None; return; }

            TransferRejectReason reason = TryTransfer(Time.deltaTime);
            if (reason == TransferRejectReason.None) { lastTransferReject = TransferRejectReason.None; return; }

            // A latch is a standing instruction and the instruction has become
            // impossible, so it is dropped rather than left running against a full hold.
            // A held key is the player's hand and stays theirs; it just gets told why.
            if (TransferLatched) TransferLatched = false;

            // Once per reason, not once per frame: holding T against an empty drill for
            // three seconds is one refusal, not a hundred and eighty.
            if (reason != lastTransferReject) { lastTransferReject = reason; SetRejection(TransferRejectText(reason)); }
        }

        private DrillStructure NearestActiveDrill()
        {
            DrillStructure best = null;
            float bestSqr = float.MaxValue;
            var drills = ExtractionState.Drills;
            for (int i = 0; i < drills.Count; i++)
            {
                DrillStructure drill = drills[i];
                if (drill == null || drill.Runtime.State != DrillLifecycle.Active) continue;
                var body = ExtractionState.FindPlanet(drill.WorldId)?.Body;
                if (body == null) continue;
                float range = TransferRange(body);
                float sqr = (drill.transform.position - transform.position).sqrMagnitude;
                if (sqr < range * range && sqr < bestSqr) { bestSqr = sqr; best = drill; }
            }
            return best;
        }

        /// <summary>How long a transient line stays on screen. Shared with <see cref="CommandFeedback"/> so the two never expire apart.</summary>
        public const float MessageSeconds = 3.5f;

        private void SetMessage(string text) { StatusMessage = text; messageUntil = Time.unscaledTime + MessageSeconds; NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Extraction, text); }

        /// <summary>
        /// A refusal, published as one.
        /// <para>
        /// Same channel and same TTL as <see cref="SetMessage"/>, with
        /// <see cref="GameplayFeedbackTone.Rejection"/> instead of the default status
        /// tone — the HUD strip colours by tone, so a refusal sent as a status reads as
        /// a progress note. That distinction is the whole of P1-ECO-017 on the
        /// presentation side; the gameplay side was the four bare <c>return</c>s in
        /// <c>HandleTransfer</c>, which now all arrive here.
        /// </para>
        /// </summary>
        private void SetRejection(string text)
        {
            StatusMessage = text;
            messageUntil = Time.unscaledTime + MessageSeconds;
            NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Extraction, text,
                GameplayFeedbackTone.Rejection);
        }
        public string TransientMessage => Time.unscaledTime < messageUntil ? StatusMessage : null;

        public string CargoKitLabel()
        {
            switch (ExtractionState.Kit)
            {
                case ExtractionState.KitState.Available: return "HAZIR";
                case ExtractionState.KitState.Reserved: return "AYRILDI";
                default: return "KULLANILDI";
            }
        }
    }
}
