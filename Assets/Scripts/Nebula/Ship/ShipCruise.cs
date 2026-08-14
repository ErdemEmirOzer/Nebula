using UnityEngine;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Navigation;
using Nebula.Simulation;
using Nebula.World;

namespace Nebula.Ship
{
    /// <summary>
    /// Ship-side driver of the Engine-Navigation module's deep-space cruise
    /// (VS-006 §4) and Yön Sabitle / Heading Lock (§4, H key). Ticks the pure
    /// <see cref="CruiseStateMachine"/> every frame with live entry
    /// conditions and applies its result as a speed multiplier on
    /// <see cref="ShipController"/> — yaw/pitch/roll and throttle stay
    /// entirely player-driven throughout; this component never writes
    /// position/rotation directly, only the multiplier and (for heading
    /// lock) a rotation-input suppression flag. Runs before ShipController's
    /// default execution order so RotationLocked is already set before the
    /// controller reads mouse input the same frame.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class ShipCruise : MonoBehaviour
    {
        private const float ThrottleHighFraction = 0.85f;
        private const float HazardRadiusMultiplier = 6f;
        private const float SharpTurnDegPerSec = 25f;
        private const float HeadingLockInputDeadzone = 0.02f;

        private ShipController ship;
        private ShipExtraction extraction;
        private ShipCombat combat;

        private readonly CruiseStateMachine stateMachine = new CruiseStateMachine();
        private readonly HeadingLockState headingLock = new HeadingLockState();

        private Vector3 lastForward = Vector3.forward;
        private float lastTurnDegPerSec;

        // ---- nearby stars, resolved per sector rather than per frame (P2-PERF-002) ----
        // One list, reused in place. Its length is bounded by the systems the 27-sector
        // neighbourhood contains, and it is refilled — never appended to — so a session
        // spent crossing sectors cannot grow it.
        private readonly System.Collections.Generic.List<StellarNeighbour> stellarNeighbours =
            new System.Collections.Generic.List<StellarNeighbour>();
        private Nebula.Sectors.SectorManifestRepository stellarNeighbourSource;
        private Nebula.Sectors.SectorCoordinate stellarNeighbourSector;
        private bool hasStellarNeighbours;

        /// <summary>Test-facing: how many times the nearby-star list was actually walked.</summary>
        public int StellarNeighbourhoodBuilds { get; private set; }

        /// <summary>Test-facing: how many stars the current neighbourhood holds.</summary>
        public int StellarNeighbourCount => stellarNeighbours.Count;

        /// <summary>
        /// The immutable half of a nearby system — exactly the three fields
        /// <see cref="InStellarRiskZone"/> reads, and nothing that streaming can change
        /// underneath the cache. A <c>StarSystemRecord</c> also carries a live
        /// <c>StarTransform</c> and a streaming state, which is why the record itself is
        /// deliberately not what gets stored.
        /// </summary>
        private readonly struct StellarNeighbour
        {
            public readonly Vector3 AbsoluteCenter;
            public readonly float StarRadius;
            public readonly Nebula.Sectors.StarClass StarClass;

            public StellarNeighbour(Vector3 absoluteCenter, float starRadius, Nebula.Sectors.StarClass starClass)
            {
                AbsoluteCenter = absoluteCenter;
                StarRadius = starRadius;
                StarClass = starClass;
            }
        }

        public FlightMode Mode => stateMachine.Mode;
        public float SpeedMultiplier => stateMachine.SpeedMultiplier;
        public float RampElapsed => stateMachine.RampElapsed;
        public float DecelElapsed => stateMachine.DecelElapsed;
        public int EngineTier { get; private set; }
        public bool HeadingLockEngaged => headingLock.Engaged;
        public Vector3 HeadingLockVector => headingLock.HeldDirection;

        /// <summary>
        /// The player's standing permission for Deep Space Cruise (P1-COMBAT-005).
        /// <para>
        /// True from the start, so a campaign that never presses the key behaves exactly
        /// as cruise always has: the eight world conditions decide, and cruise engages
        /// when they line up. Pressing <c>flight.cruise_toggle</c> withdraws the
        /// permission, which takes the ship out through the ordinary eight-second
        /// deceleration rather than a snap; pressing it again gives it back.
        /// </para>
        /// <para>
        /// Session state, not saved state. Adding a field to the save would be a schema
        /// change, and "armed" is the answer a fresh session should start from anyway —
        /// a player who disarmed cruise an hour of play ago and quit should not have to
        /// discover why their ship will not accelerate.
        /// </para>
        /// </summary>
        public bool CruiseArmed { get; private set; } = true;

        /// <summary>
        /// Arms or disarms cruise, and answers with the new state.
        /// <para>
        /// Public and named for the intent so the key is a shortcut for the action
        /// rather than the only way to reach it — the same shape
        /// <see cref="ToggleHeadingLock"/> already has, and the reason a control can
        /// offer this without simulating a key press.
        /// </para>
        /// </summary>
        public bool ToggleCruiseArmed() => CruiseArmed = !CruiseArmed;

        /// <summary>
        /// Engages or releases Yön Sabitle, holding the heading the ship has right now.
        /// <para>
        /// The one place the toggle lives. H used to reach <c>headingLock.Toggle</c>
        /// through a private handler reading <c>Input.GetKeyDown</c> directly, which
        /// meant the feature had exactly one entry point and it was a keyboard — no
        /// control could offer it, and it died entirely behind any open screen. The key
        /// and the HUD's lock button both come through here, so there is one heading
        /// lock rather than two that agree by coincidence.
        /// </para>
        /// </summary>
        public void ToggleHeadingLock() => headingLock.Toggle(transform.forward);

        public float LocalMaxSpeedForTier => EngineTier >= 2 ? ModuleCatalog.LocalTopSpeedByTier[1] : ModuleCatalog.LocalTopSpeedByTier[0];
        public float CruiseTargetSpeedForTier => EngineTier >= 2 ? ModuleCatalog.CruiseSpeedByTier[1]
                                                : EngineTier >= 1 ? ModuleCatalog.CruiseSpeedByTier[0] : 0f;

        /// <summary>Reason cruise cannot be entered right now, or null when eligible (for the HUD).</summary>
        public string CruiseBlockedReason { get; private set; }

        private void Awake()
        {
            ship = GetComponent<ShipController>();
            extraction = GetComponent<ShipExtraction>();
            combat = GetComponent<ShipCombat>();
            ApplyEngineTier(0);
            lastForward = transform.forward;
        }

        /// <summary>Engine-Navigation module tier effect (VS-006 §4).</summary>
        public void ApplyEngineTier(int tier)
        {
            EngineTier = Mathf.Clamp(tier, 0, 2);
            stateMachine.SetEngineTier(EngineTier);
            ship.SetLocalMaxThrottle(EngineTier >= 2 ? ModuleCatalog.LocalTopSpeedByTier[1] : ModuleCatalog.LocalTopSpeedByTier[0]);
        }

        /// <summary>
        /// Declares the Flight context to the router. Flight is live exactly while the
        /// HUD owns input: it is the always-on manoeuvring layer, so it has no narrower
        /// condition of its own, and combat does not suspend it — a ship under fire is
        /// still being flown.
        /// <para>
        /// Registered here rather than on <c>ShipController</c> because this component
        /// is the one that asks the router for a Flight action; a context nobody reads
        /// is a claim with nothing behind it.
        /// </para>
        /// </summary>
        private void OnEnable() =>
            NebulaInputRouter.SetContextSource(InputContext.Flight, () => !InputGate.Suppressed);

        private void OnDisable()
        {
            NebulaInputRouter.SetContextSource(InputContext.Flight, null);
            // A component being switched off must not keep a whole manifest repository
            // reachable: the cached neighbourhood is a performance aid, never an owner.
            InvalidateStellarNeighbourhood();
        }

        private void Update()
        {
            // Menus suppress player input, not the world clock. Cruise ramp/
            // deceleration and RotationLocked therefore continue to publish each
            // frame; only the input reader stays behind the gate.
            if (!InputGate.Suppressed)
            {
                HandleHeadingLockInput();
                if (NebulaInputRouter.Pressed(NebulaInputActions.FlightCruiseToggle)) ToggleCruiseArmed();
            }
            ship.RotationLocked = headingLock.Engaged;

            float dt = Time.deltaTime;
            CruiseEntryConditions conditions = BuildConditions(out string blockedReason);
            CruiseBlockedReason = stateMachine.Mode == FlightMode.LocalFlight ? blockedReason : null;

            stateMachine.Tick(dt, conditions, LocalMaxSpeedForTier, CruiseTargetSpeedForTier);
            ship.CruiseSpeedMultiplier = stateMachine.SpeedMultiplier;

            Vector3 forward = transform.forward;
            lastTurnDegPerSec = dt > 0.0001f ? Vector3.Angle(lastForward, forward) / dt : 0f;
            lastForward = forward;
        }

        private CruiseEntryConditions BuildConditions(out string blockedReason)
        {
            blockedReason = null;
            bool throttleHigh = ship.GetThrottleFraction() >= ThrottleHighFraction;
            if (!throttleHigh) blockedReason = "THROTTLE NEAR MAX TO ENGAGE CRUISE";

            bool notStabilized = !ship.IsStabilized;
            if (!notStabilized) blockedReason ??= "RELEASE ORBIT STABILIZE";

            bool buildModeOff = extraction == null || !extraction.BuildMode;
            if (!buildModeOff) blockedReason ??= "EXIT BUILD MODE";

            // Two halves, deliberately kept apart. `Transferring` and `ScanLatched` are
            // world state — a transfer or a latched scan really is still running behind
            // an open screen, so they answer through the gate. The held scan key is
            // player input and is sampled only while input is live: read unconditionally
            // it would freeze at "down" the instant a menu opened over a held key and
            // block cruise for the rest of the session, since nothing behind the gate
            // ever sees the release. The key itself comes from the catalog row rather
            // than ShipExtraction's constant, so a rebind of Tara moves this too.
            bool scanHeld = !InputGate.Suppressed
                            && NebulaInputRouter.Held(NebulaInputActions.ExtractionScan);
            bool scanRunning = extraction != null && extraction.ScanLatched;
            bool noPhysicalInteraction = extraction == null
                                         || (!extraction.Transferring && !scanHeld && !scanRunning);
            if (!noPhysicalInteraction) blockedReason ??= "FINISH TRANSFER/SCAN FIRST";

            bool notNearHazard = !NearHazard();
            if (!notNearHazard) blockedReason ??= "TOO CLOSE TO A CELESTIAL BODY";

            bool noSharpTurn = lastTurnDegPerSec < SharpTurnDegPerSec;
            if (!noSharpTurn) blockedReason ??= "HOLD A STEADY HEADING";

            bool notInStellarRisk = !InStellarRiskZone(out Nebula.Sectors.StellarRiskLevel riskLevel);
            if (!notInStellarRisk) blockedReason ??= riskLevel == Nebula.Sectors.StellarRiskLevel.Critical
                ? "TOO CLOSE TO STAR — CRITICAL ZONE" : "TOO CLOSE TO STAR — WARNING ZONE";

            bool notDamaged = combat == null || combat.State == null || !combat.State.Damaged;
            if (!notDamaged) blockedReason ??= "SHIP DAMAGED — CRUISE UNAVAILABLE UNTIL REPAIRED";

            // When the player has switched cruise off, that is the reason — not
            // whichever world condition happens also to be unmet. A missing engine tier
            // still outranks it: no key can cruise a ship that cannot.
            if (!CruiseArmed) blockedReason = "SEYİR KİPİ KAPALI — AÇMAK İÇİN TUŞA BAS";
            if (EngineTier < 1) blockedReason = "REQUIRES ENGINE-NAVIGATION T1";

            return new CruiseEntryConditions
            {
                PlayerArmed = CruiseArmed,
                ThrottleHigh = throttleHigh,
                NotStabilized = notStabilized,
                BuildModeOff = buildModeOff,
                NoPhysicalInteractionActive = noPhysicalInteraction,
                NotNearHazard = notNearHazard,
                // Entry only. CruiseEntryConditions.CanEngage ands this in and
                // CruiseEntryConditions.AllMet does not, so an open screen stops the
                // player STARTING a cruise — a throttle left at max behind a menu is
                // not a decision — while a cruise already under way keeps ramping,
                // holding and decelerating on world state alone. It used to be the
                // literal `true`, justified by an InputGate early-return that Part 3
                // replaced (see Update above), so nothing about input could reach the
                // state machine and every test of the field measured the constant.
                InputFree = !InputGate.Suppressed,
                NoSharpTurn = noSharpTurn,
                NotInStellarRisk = notInStellarRisk,
                NotDamaged = notDamaged
            };
        }

        /// <summary>VS-009 §2: blocks cruise while inside ANY nearby system's Warning/Critical stellar-risk radius — works from the manifest alone, no materialization required.</summary>
        private bool InStellarRiskZone(out Nebula.Sectors.StellarRiskLevel level)
        {
            level = Nebula.Sectors.StellarRiskLevel.Safe;
            Vector3 shipAbs = transform.position + FloatingOrigin.TotalOffset;

            // The distance is still measured every frame — that is what the risk depends
            // on. Only the *set* of stars is cached, and only for as long as the ship is
            // in the same sector.
            System.Collections.Generic.List<StellarNeighbour> stars = StellarNeighbourhood(shipAbs);
            for (int i = 0; i < stars.Count; i++)
            {
                StellarNeighbour star = stars[i];
                float distance = Vector3.Distance(shipAbs, star.AbsoluteCenter);
                Nebula.Sectors.StellarRiskState risk = Nebula.Sectors.StellarRiskResolver.Resolve(distance, star.StarRadius, star.StarClass);
                if (!risk.CruiseAllowed) { level = risk.Level; return true; }
            }
            return false;
        }

        /// <summary>
        /// The stars of the ship's sector and its 26 neighbours, rebuilt only when the
        /// answer can have changed (P2-PERF-002).
        /// <para>
        /// <c>StarSystemRegistry.SystemsNear</c> is 27 dictionary lookups and up to 29
        /// iterator state machines, and cruise asked for it on <b>every frame</b> through
        /// <see cref="BuildConditions"/>. Its answer is a pure function of the sector the
        /// ship is in — manifests are immutable and deterministic — and a ship does not
        /// change sector between frames, so the walk was producing the same list hundreds
        /// of times a second.
        /// </para>
        /// <para>
        /// The cache key is the pair (repository, sector), not the sector alone: ending a
        /// campaign replaces the repository, and a list keyed on coordinates alone would
        /// survive into the next universe and report another campaign's stars. The
        /// repository reference is dropped the moment there is no world, so this component
        /// never keeps a disposed universe alive.
        /// </para>
        /// </summary>
        private System.Collections.Generic.List<StellarNeighbour> StellarNeighbourhood(Vector3 shipAbs)
        {
            Nebula.Sectors.SectorManifestRepository manifests = Nebula.World.StarSystemRegistry.Manifests;
            if (manifests == null)
            {
                InvalidateStellarNeighbourhood();
                return stellarNeighbours;
            }

            Nebula.Sectors.SectorCoordinate sector = Nebula.Sectors.SectorCoordinate.FromAbsolutePosition(shipAbs);
            if (hasStellarNeighbours
                && ReferenceEquals(manifests, stellarNeighbourSource)
                && sector.Equals(stellarNeighbourSector))
                return stellarNeighbours;

            stellarNeighbours.Clear();
            foreach (Nebula.World.StarSystemRecord record in Nebula.World.StarSystemRegistry.SystemsNear(shipAbs))
                stellarNeighbours.Add(new StellarNeighbour(record.AbsoluteCenter, record.StarRadius, record.StarClass));

            stellarNeighbourSource = manifests;
            stellarNeighbourSector = sector;
            hasStellarNeighbours = true;
            StellarNeighbourhoodBuilds++;
            return stellarNeighbours;
        }

        private void InvalidateStellarNeighbourhood()
        {
            stellarNeighbours.Clear();
            stellarNeighbourSource = null;
            hasStellarNeighbours = false;
        }

        private bool NearHazard()
        {
            var bodies = GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBody body = bodies[i];
                if (body == null) continue;
                float safe = body.Radius * HazardRadiusMultiplier;
                if ((body.transform.position - transform.position).sqrMagnitude < safe * safe) return true;
            }
            return false;
        }

        /// <summary>
        /// The keyboard half of heading lock: the toggle, and the "player took the
        /// stick back" release.
        /// <para>
        /// The toggle goes through the router on <c>flight.heading_lock</c> rather than
        /// a literal H, so the settings screen's rebind of Yön Sabitle is honoured on
        /// the frame it is taken. The release is an <i>observation</i> of the roll keys
        /// — <see cref="NebulaInputRouter.KeyHeld"/>, not <c>Held</c> — because heading
        /// lock must notice a roll without consuming it; consuming it would stop the
        /// ship rolling at the exact moment the player asked it to.
        /// </para>
        /// </summary>
        private void HandleHeadingLockInput()
        {
            if (NebulaInputRouter.Pressed(NebulaInputActions.FlightHeadingLock))
            {
                ToggleHeadingLock();
                return;
            }
            if (!headingLock.Engaged || Cursor.lockState != CursorLockMode.Locked) return;

            // Mouse look is an InputManager axis, not a KeyCode, so it has no catalog
            // key to route: flight.look is an Axis row carrying KeyCode.None.
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            bool rollInput = NebulaInputRouter.KeyHeld(NebulaInputActions.Key(NebulaInputActions.FlightRoll))
                             || NebulaInputRouter.KeyHeld(NebulaInputActions.Key("flight.roll_right"));
            if (Mathf.Abs(mouseX) > HeadingLockInputDeadzone || Mathf.Abs(mouseY) > HeadingLockInputDeadzone || rollInput)
            {
                headingLock.Disengage();
            }
        }

        // --- Save/load ---------------------------------------------------------
        public void RestoreFlightState(FlightMode mode, float elapsedSeconds, float speedMultiplier, bool headingEngaged, Vector3 headingVector)
        {
            stateMachine.Restore(mode, elapsedSeconds, EngineTier, speedMultiplier);
            ship.CruiseSpeedMultiplier = stateMachine.SpeedMultiplier;
            headingLock.Restore(headingEngaged, headingVector);
        }

        public float ElapsedForSave => stateMachine.Mode == FlightMode.CruiseRamp ? stateMachine.RampElapsed
                                       : stateMachine.Mode == FlightMode.CruiseDeceleration ? stateMachine.DecelElapsed : 0f;
    }
}
