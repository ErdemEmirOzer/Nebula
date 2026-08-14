namespace Nebula.Poi
{
    /// <summary>The three combat-free POI types this slice implements. Protected is a reserved enum value only — never produced by the generator, no combat marker exists for it.</summary>
    public enum PoiType { Anomaly, Wreck, AbandonedFacility, Protected }

    /// <summary>VS-012 only ever produces Safe/Unstable. Protected is reserved for a future combat slice — an unused hook, never a real generator output.</summary>
    public enum PoiRisk { Safe, Unstable, Protected }

    public enum WreckSize { Small, Medium, Large }

    /// <summary>The two mutually exclusive, irreversible Abandoned Facility outcomes.</summary>
    public enum FacilityChoice { None, Dismantle, SensorRelay }

    /// <summary>
    /// The information-gate sequence (VS-012 §3) — strictly ascending, no phase ever
    /// skipped or repeated out of order.
    /// <para>
    /// <b>P1-WORLD-005 — what <see cref="Unknown"/> is.</b> It is the rung <i>below</i>
    /// having a record: "this campaign has never heard of this POI". It is deliberately
    /// not a value <see cref="PoiState.Level"/> can ever hold, because a
    /// <c>PoiState</c> only exists once a signal has been received — creating one to
    /// say "we know nothing about this" would be the record whose absence is the whole
    /// meaning of the rung. The only producer is the strategic map, which reports
    /// <c>Unknown</c> for a POI it has no delta record for
    /// (<c>StrategicMapLiveUniverseSource</c>), and that is exactly right.
    /// </para>
    /// <para>
    /// The audit found the documented ladder claiming six live states while live state
    /// only ever showed five. The decision taken was to keep the rung and write down
    /// what it means, rather than to invent a Signal-less <c>PoiState</c> the world
    /// would have to create and hide, or to delete a rung the map genuinely needs.
    /// <c>PoiState</c> guards the invariant itself — see <c>PoiState.Level</c>.
    /// </para>
    /// </summary>
    public enum PoiKnowledgeLevel { Unknown, Signal, Located, Identified, Active, Resolved }

    /// <summary>Why a POI's active timer isn't currently progressing.</summary>
    public enum PoiPauseReason { None, OutOfRange, NoAssignedWorker, TargetCargoFull }

    /// <summary>
    /// Pure data + numeric constants for the three POI types (VS-012). No
    /// UnityEngine.Random anywhere — every roll is a deterministic hash draw
    /// (see <see cref="PoiManifestGenerator"/>). Type-weight ratios are not
    /// specified anywhere in the design brief; this catalog documents them
    /// as plain data (equal weight, 1/1/1) exactly so a future balance pass
    /// can change them without touching the generator's logic at all.
    /// </summary>
    public static class PoiCatalog
    {
        // --- System-level POI count distribution (VS-012 §2) ------------------------
        public const float CountZeroWeight = 0.30f;
        public const float CountOneWeight = 0.50f;
        public const float CountTwoWeight = 0.20f;
        public const int MaxPoiPerSystem = 2;

        /// <summary>
        /// Equal-weight default (1/1/1) — no approved type-mix ratio exists
        /// in the design brief. Data-driven so a future slice can rebalance
        /// without touching <see cref="PoiManifestGenerator"/>.
        /// </summary>
        public static readonly (PoiType type, float weight)[] TypeWeights =
        {
            (PoiType.Anomaly, 1f), (PoiType.Wreck, 1f), (PoiType.AbandonedFacility, 1f)
        };

        // --- Risk distributions (VS-012 §5/§6/§7) ------------------------------------
        public const float AnomalySafeWeight = 0.60f, AnomalyUnstableWeight = 0.40f;
        public const float WreckSafeWeight = 0.70f, WreckUnstableWeight = 0.30f;
        public const float FacilitySafeWeight = 0.40f, FacilityUnstableWeight = 0.60f;

        public const float UnstableDurationMultiplier = 1.5f;

        // --- Locate / identify (VS-012 §3) -------------------------------------------

        /// <summary>
        /// Locate with no Scanner module at all (P1-DATA-020 / P1-WORLD-004).
        /// <para>
        /// There was no tier-0 row: the ship fell back to the T1 duration, so a player
        /// who had never bought a Scanner located POIs at exactly the speed a Scanner
        /// T1 owner did, and the upgrade they paid for changed nothing about POI
        /// Locate. The tier ladder halves at each step (T1 12s → T2 6s); tier 0 is that
        /// same ratio taken one step the other way, so the rung is derived from the
        /// ladder rather than picked, and T1 now buys the same 2x it always bought
        /// between the rungs that did exist.
        /// </para>
        /// </summary>
        public const float ScannerT0LocateSeconds = 24f;
        public const float ScannerT1LocateSeconds = 12f;
        public const float ScannerT2LocateSeconds = 6f;
        public const float ScoutLocateSeconds = 30f;
        public const float IdentifySeconds = 15f;
        public const float IdentifyRange = 2500f;

        /// <summary>How long the ship's own passive scanner needs to Locate at a given <c>exploration.scanner</c> tier. The one place the tier→duration mapping lives.</summary>
        public static float ShipLocateSeconds(int scannerTier) =>
            scannerTier >= 2 ? ScannerT2LocateSeconds
            : scannerTier >= 1 ? ScannerT1LocateSeconds
            : ScannerT0LocateSeconds;

        /// <summary>
        /// How close the ship must be for its own scanner to make progress on a Locate
        /// (P1-WORLD-004).
        /// <para>
        /// The ship's Locate condition used to be the literal <c>true</c>: being
        /// anywhere in the system was enough, so the one exploration verb with a timer
        /// had no spatial meaning at all and a POI on the far side of the star resolved
        /// while the player flew away from it. The distance is derived from the two
        /// authorities that already exist — <see cref="IdentifyRange"/>, the range every
        /// other POI interaction uses, widened by the Scanner module's own
        /// scan-radius multiplier — so buying Scanner T1 buys reach as well as speed,
        /// and the number is the module's, not a new one invented here. Tier 0 gets the
        /// bare identify range: with no scanner, you must be as close to find it as you
        /// would be to work on it.
        /// </para>
        /// </summary>
        public static float ShipLocateRange(int scannerTier) =>
            scannerTier >= 2 ? IdentifyRange * Modules.ModuleCatalog.ScanRadiusMultiplierByTier[1]
            : scannerTier >= 1 ? IdentifyRange * Modules.ModuleCatalog.ScanRadiusMultiplierByTier[0]
            : IdentifyRange;

        // --- Anomaly (VS-012 §5) ------------------------------------------------------
        public const float AnomalyShipSafeSeconds = 40f, AnomalyShipUnstableSeconds = 60f;
        public const float AnomalyScoutSafeSeconds = 60f, AnomalyScoutUnstableSeconds = 90f;
        public const int AnomalyXp = 250;
        public const int AnomalySignalNearestIndexStart = 3; // 4th nearest = index 3 (0-based)
        public const int AnomalySignalNearestIndexEnd = 7;   // 8th nearest = index 7 (inclusive)
        public const int AnomalySignalCount = 1;

        // --- Wreck (VS-012 §6) --------------------------------------------------------
        public const int WreckMinNodes = 2, WreckMaxNodes = 4;
        public const int WreckXp = 150;
        public const float WreckMkITransferPerSecond = 10f;
        public const float WreckMkIITransferPerSecond = 20f;

        public static (int minBasic, int maxBasic, int minSpecial, int maxSpecial) WreckRewardRange(WreckSize size) => size switch
        {
            WreckSize.Small => (10, 20, 5, 10),
            WreckSize.Medium => (20, 30, 10, 15),
            WreckSize.Large => (30, 40, 15, 20),
            _ => (10, 20, 5, 10)
        };

        // --- Abandoned Facility (VS-012 §7) -------------------------------------------
        public const float FacilityDismantleBaseSeconds = 180f, FacilityDismantleUnstableSeconds = 270f;
        public const int FacilityDismantleMinBasic = 30, FacilityDismantleMaxBasic = 80;
        public const int FacilityDismantleMinSpecial = 10, FacilityDismantleMaxSpecial = 30;

        public const float FacilityRelayBaseSeconds = 120f, FacilityRelayUnstableSeconds = 180f;
        public const int FacilityRelayPartsCost = 25;
        // TF-003: was "material.diamond_composite", an id no source in the game ever
        // produced (ProcessingCatalog.MaterialIds has "material.industrial_diamond"
        // instead) -- the Sensor Relay choice was permanently unaffordable. Found by
        // the new cross-reference validator; corrected to the real produced item id.
        public const string FacilityRelayDiamondCompositeId = "material.industrial_diamond";
        public const int FacilityRelayDiamondCompositeCost = 8;
        public const string FacilityRelayBiocompositeId = "material.biocomposite";
        public const int FacilityRelayBiocompositeCost = 5;
        public const int FacilityRelaySignalCount = 2;

        public const int FacilityXp = 300;
    }
}
