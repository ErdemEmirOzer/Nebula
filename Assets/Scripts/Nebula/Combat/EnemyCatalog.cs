namespace Nebula.Combat
{
    /// <summary>The six real AI-driven enemy types (VS-013 §6 Scout Probe/Fighter; VS-014 §5 Looter/Disruptor/Heavy/Siege). Stable, never-renamed ids.</summary>
    public static class EnemyIds
    {
        public const string ScoutProbeMk1 = "enemy.scout_probe.mk1";
        public const string FighterMk1 = "enemy.fighter.mk1";
        public const string LooterTransportMk1 = "enemy.looter_transport.mk1";
        public const string EmDisruptorMk1 = "enemy.em_disruptor.mk1";
        public const string HeavyAssaultMk1 = "enemy.heavy_assault.mk1";
        public const string SiegeMk1 = "enemy.siege.mk1";
    }

    /// <summary>
    /// VS-014 §5: the behavior category driving which special mechanic (if
    /// any) an <see cref="EnemyDef"/> uses on top of the shared
    /// <see cref="EnemyAiResolver"/> phase graph every role runs through.
    /// </summary>
    public enum EnemyRole { Probe, Fighter, Looter, Disruptor, Heavy, Siege }

    /// <summary>
    /// Static definition of one enemy type. IsCombatCapable=false (the Scout
    /// Probe) means it never deals damage and its own AttackOrScan exit
    /// condition is "scan complete", not a hull-fraction retreat trigger —
    /// see <see cref="EnemyAiResolver"/>'s caller-supplied exit flag. Every
    /// VS-014 field below is 0/default for a role that doesn't use it
    /// (Threat/cargo/telegraph/disrupt), the same "inapplicable stays zero"
    /// convention already used for Probe's Dps/AttackRange.
    /// </summary>
    public class EnemyDef
    {
        public string Id { get; }
        public string DisplayName { get; }
        public EnemyRole Role { get; }
        public float Speed { get; }
        public float ShieldMax { get; }
        public float ArmorMax { get; }
        public float HullMax { get; }
        public bool IsCombatCapable { get; }
        public float Dps { get; }
        public float AttackRange { get; }

        /// <summary>The distance band this enemy prefers to sit at while in AttackOrScan (Fighter's "saldırı bandı" / Probe's "tarama bandı").</summary>
        public float EngageBandMin { get; }
        public float EngageBandMax { get; }
        /// <summary>Distance at/under which it breaks off even mid-engagement (VS-013 §6 "&lt;300 break" for the Fighter; a small safety margin for the Probe).</summary>
        public float BreakDistance { get; }
        /// <summary>Distance at/over which it is still clearly Approaching, never yet considered "closing in" (Fighter's "&gt;1100 yaklaşma").</summary>
        public float ApproachThreshold { get; }

        /// <summary>Hull fraction at/under which it exits AttackOrScan into Retreat. 0 for the non-combat Probe (it uses its own scan-complete exit instead). A role may ALSO retreat on a caller-supplied "escort/leader lost" condition (VS-014 §5) — that dynamic condition is never part of this static def, exactly like the existing Fighter hull check is caller-computed, not resolver-internal.</summary>
        public float RetreatHullFraction { get; }
        /// <summary>Probe only: uninterrupted (pause/resume-supported) scan duration.</summary>
        public float ScanSeconds { get; }

        /// <summary>VS-014 §9: raid-composition threat budget cost of one instance of this enemy.</summary>
        public int ThreatBudget { get; }
        /// <summary>Looter only: cargo hold volume (CargoItemVolumes units).</summary>
        public float CargoCapacityVolume { get; }
        /// <summary>Looter only: theft transfer rate.</summary>
        public float CargoTransferVolumePerSecond { get; }
        /// <summary>Disruptor/Siege: seconds of visible telegraph before the special action fires.</summary>
        public float TelegraphSeconds { get; }
        /// <summary>Disruptor only: EM disable duration it inflicts on the player.</summary>
        public float DisruptSeconds { get; }
        /// <summary>Disruptor only: seconds the SAME target is immune to a new disrupt after one lands.</summary>
        public float DisruptImmunitySeconds { get; }

        public EnemyDef(string id, string displayName, EnemyRole role, float speed, float shieldMax, float armorMax, float hullMax,
            bool isCombatCapable, float dps, float attackRange, float engageBandMin, float engageBandMax,
            float breakDistance, float approachThreshold, float retreatHullFraction, float scanSeconds,
            int threatBudget = 0, float cargoCapacityVolume = 0f, float cargoTransferVolumePerSecond = 0f,
            float telegraphSeconds = 0f, float disruptSeconds = 0f, float disruptImmunitySeconds = 0f)
        {
            Id = id; DisplayName = displayName; Role = role; Speed = speed;
            ShieldMax = shieldMax; ArmorMax = armorMax; HullMax = hullMax;
            IsCombatCapable = isCombatCapable; Dps = dps; AttackRange = attackRange;
            EngageBandMin = engageBandMin; EngageBandMax = engageBandMax;
            BreakDistance = breakDistance; ApproachThreshold = approachThreshold;
            RetreatHullFraction = retreatHullFraction; ScanSeconds = scanSeconds;
            ThreatBudget = threatBudget; CargoCapacityVolume = cargoCapacityVolume; CargoTransferVolumePerSecond = cargoTransferVolumePerSecond;
            TelegraphSeconds = telegraphSeconds; DisruptSeconds = disruptSeconds; DisruptImmunitySeconds = disruptImmunitySeconds;
        }
    }

    public static class EnemyCatalog
    {
        /// <summary>5 Hz AI decision cadence (VS-013 §6) -- physics itself still ticks every FixedUpdate.</summary>
        public const float DecisionIntervalSeconds = 0.2f;
        /// <summary>A target decision is held for at least this long before it can change (VS-013 §6).</summary>
        public const float MinimumTargetCommitSeconds = 5f;
        public const float ObstacleAvoidanceLookaheadSeconds = 1.5f;
        public const float RetreatEscapeDistance = 9000f;
        public const float RetreatEscapeContinuousSeconds = 5f;

        /// <summary>VS-014 §7: Looter cargo hold + theft rate, shared by the catalog def and LooterCargoService (never re-typed at the call site).</summary>
        public const float LooterCargoCapacityVolume = 120f;
        public const float LooterTransferVolumePerSecond = 10f;

        public static readonly EnemyDef ScoutProbeMk1 = new EnemyDef(
            EnemyIds.ScoutProbeMk1, "Scout Probe Mk I", EnemyRole.Probe, speed: 230f,
            shieldMax: 80f, armorMax: 60f, hullMax: 100f,
            isCombatCapable: false, dps: 0f, attackRange: 0f,
            engageBandMin: 1000f, engageBandMax: 1400f,
            breakDistance: 300f, approachThreshold: 1400f,
            retreatHullFraction: 0f, scanSeconds: 45f);

        public static readonly EnemyDef FighterMk1 = new EnemyDef(
            EnemyIds.FighterMk1, "Fighter Mk I", EnemyRole.Fighter, speed: 200f,
            shieldMax: 120f, armorMax: 150f, hullMax: 220f,
            isCombatCapable: true, dps: 55f, attackRange: 900f,
            engageBandMin: 650f, engageBandMax: 850f,
            breakDistance: 300f, approachThreshold: 1100f,
            retreatHullFraction: 0.20f, scanSeconds: 0f,
            threatBudget: 2);

        public static readonly EnemyDef LooterTransportMk1 = new EnemyDef(
            EnemyIds.LooterTransportMk1, "Looter Transport Mk I", EnemyRole.Looter, speed: 135f,
            shieldMax: 250f, armorMax: 400f, hullMax: 650f,
            isCombatCapable: true, dps: 30f, attackRange: 800f,
            engageBandMin: 500f, engageBandMax: 800f,
            breakDistance: 250f, approachThreshold: 800f,
            retreatHullFraction: 0.30f, scanSeconds: 0f,
            threatBudget: 4, cargoCapacityVolume: LooterCargoCapacityVolume, cargoTransferVolumePerSecond: LooterTransferVolumePerSecond);

        public static readonly EnemyDef EmDisruptorMk1 = new EnemyDef(
            EnemyIds.EmDisruptorMk1, "EM Disruptor Mk I", EnemyRole.Disruptor, speed: 160f,
            shieldMax: 450f, armorMax: 250f, hullMax: 300f,
            isCombatCapable: true, dps: 35f, attackRange: 2500f,
            engageBandMin: 1800f, engageBandMax: 2400f,
            breakDistance: 600f, approachThreshold: 2400f,
            retreatHullFraction: 0.25f, scanSeconds: 0f,
            threatBudget: 5, telegraphSeconds: 1.5f, disruptSeconds: 2f, disruptImmunitySeconds: 10f);

        public static readonly EnemyDef HeavyAssaultMk1 = new EnemyDef(
            EnemyIds.HeavyAssaultMk1, "Heavy Assault Mk I", EnemyRole.Heavy, speed: 120f,
            shieldMax: 300f, armorMax: 850f, hullMax: 1100f,
            isCombatCapable: true, dps: 130f, attackRange: 1800f,
            engageBandMin: 1200f, engageBandMax: 1600f,
            breakDistance: 400f, approachThreshold: 1600f,
            retreatHullFraction: 0.15f, scanSeconds: 0f,
            threatBudget: 8);

        public static readonly EnemyDef SiegeMk1 = new EnemyDef(
            EnemyIds.SiegeMk1, "Siege Mk I", EnemyRole.Siege, speed: 90f,
            shieldMax: 700f, armorMax: 1200f, hullMax: 1600f,
            isCombatCapable: true, dps: 180f, attackRange: 5000f,
            engageBandMin: 3800f, engageBandMax: 4600f,
            breakDistance: 1000f, approachThreshold: 4600f,
            retreatHullFraction: 0.30f, scanSeconds: 0f,
            threatBudget: 13, telegraphSeconds: 3f);

        public static EnemyDef Get(string id) => id switch
        {
            EnemyIds.ScoutProbeMk1 => ScoutProbeMk1,
            EnemyIds.FighterMk1 => FighterMk1,
            EnemyIds.LooterTransportMk1 => LooterTransportMk1,
            EnemyIds.EmDisruptorMk1 => EmDisruptorMk1,
            EnemyIds.HeavyAssaultMk1 => HeavyAssaultMk1,
            EnemyIds.SiegeMk1 => SiegeMk1,
            _ => null
        };

        public static readonly EnemyDef[] All = { ScoutProbeMk1, FighterMk1, LooterTransportMk1, EmDisruptorMk1, HeavyAssaultMk1, SiegeMk1 };
    }
}
