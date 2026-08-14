using UnityEngine;

namespace Nebula.Extraction
{
    public enum DrillLifecycle { Planned, CapsuleInTransit, Constructing, Active, Damaged, Disabled, Dismantled }

    /// <summary>
    /// Pure, engine-independent state of one T1 drill: its construction
    /// lifecycle timers and its production/storage with a fractional
    /// accumulator. All rules (3-8s capsule, 30s build, 8s damage-resume,
    /// storage-full pause, fractional carry-over) live here so they can be
    /// unit-tested with no scene. Advanced by <see cref="Tick"/> in real
    /// (active-simulation) time — no offline progress.
    /// </summary>
    public class DrillRuntime
    {
        // Real balance (VS-003). Tests inject controlled dt via Tick.
        public const float TestProductionMultiplier = 1f;
        private const float BuildSeconds = 30f;
        private const float DamageResumeSeconds = 8f;

        public ResourceFamily Family { get; }
        public Rarity Rarity { get; }

        /// <summary>VS-017A.2 §B4: 1 or 2. Production/storage are always re-derived from (Rarity, Tier) through ExtractionCatalog — the single rounding authority the remote distant path uses too.</summary>
        public int Tier { get; private set; } = 1;

        public float ProductionPerMinute => ExtractionCatalog.ProductionPerMinute(Rarity, Tier);
        public float StorageCapacity => ExtractionCatalog.StorageCapacity(Rarity, Tier);

        /// <summary>VS-017A.2 §B3: the single in-flight T1→T2 upgrade, or null. Production is fully halted while this is non-null.</summary>
        public DrillUpgradeJob ActiveUpgradeJob { get; private set; }

        public DrillLifecycle State { get; private set; }
        public float CapsuleRemaining { get; private set; }
        public float BuildRemaining { get; private set; }
        public float StoredAmount { get; private set; }
        public float Accumulator { get; private set; }

        private float damageResumeTimer;

        /// <summary>True while producing and the store is not full.</summary>
        /// <summary>Set by the dominance system: production halts while the planet is Contested/Lost.</summary>
        public bool ProductionSuspended { get; set; }

        public bool IsProducing => State == DrillLifecycle.Active && !ProductionSuspended && !IsUpgrading
                                   && StoredAmount < StorageCapacity - 0.0001f;
        public bool IsFull => State == DrillLifecycle.Active && StoredAmount >= StorageCapacity - 0.0001f;

        /// <summary>VS-017A.2 §B3: production is fully stopped for the whole duration of a tier upgrade.</summary>
        public bool IsUpgrading => ActiveUpgradeJob != null;

        public DrillRuntime(PlanetProfile profile, float capsuleSeconds)
        {
            Family = profile.Family;
            Rarity = profile.Rarity;
            CapsuleRemaining = Mathf.Clamp(capsuleSeconds, 3f, 8f);
            // VS-009 §3: environment scales only this post-capsule construction
            // duration — never production/storage (both read above, untouched).
            BuildRemaining = Sectors.EnvironmentDurationResolver.ScaleDuration(BuildSeconds, profile.Environment);
            State = DrillLifecycle.CapsuleInTransit;
        }

        /// <summary>Restores a drill straight from a save snapshot.</summary>
        public DrillRuntime(PlanetProfile profile, DrillLifecycle state, float capsuleRemaining,
            float buildRemaining, float stored, float accumulator, int tier = 1)
        {
            Family = profile.Family;
            Rarity = profile.Rarity;
            Tier = Mathf.Clamp(tier <= 0 ? 1 : tier, 1, DrillUpgradeCatalog.MaxTier);
            State = state;
            CapsuleRemaining = capsuleRemaining;
            BuildRemaining = buildRemaining;
            StoredAmount = Mathf.Clamp(stored, 0f, StorageCapacity);
            Accumulator = accumulator;
        }

        /// <summary>Restores an in-flight upgrade job exactly as it was saved — never re-derives its remaining time from scratch.</summary>
        public void RestoreUpgradeJob(DrillUpgradeJob job) => ActiveUpgradeJob = job;

        /// <summary>
        /// VS-017A.2 §B3/§B4: swaps in the new tier in place. Stored resource is
        /// deliberately carried over untouched — the capacity grows underneath it, so
        /// nothing is ever wiped or duplicated by an upgrade.
        /// </summary>
        public void ApplyTierUpgrade(int newTier)
        {
            ActiveUpgradeJob = null;
            Tier = Mathf.Clamp(newTier, 1, DrillUpgradeCatalog.MaxTier);
            StoredAmount = Mathf.Clamp(StoredAmount, 0f, StorageCapacity);
        }

        /// <summary>Starts a already-reserved upgrade job. The caller (DrillUpgradeService) owns the gate/cost check and the atomic reservation.</summary>
        public void BeginUpgrade(DrillUpgradeJob job) => ActiveUpgradeJob = job;

        /// <summary>Drops the in-flight job without touching tier/storage/stored — the caller owns the refund.</summary>
        public void AbortUpgrade() => ActiveUpgradeJob = null;

        /// <summary>Advances this drill by <paramref name="dt"/> active-simulation seconds. Returns true on the single tick a tier upgrade finishes, so the owner can consume the reservation and apply the new tier exactly once.</summary>
        public bool Tick(float dt)
        {
            switch (State)
            {
                case DrillLifecycle.CapsuleInTransit:
                    CapsuleRemaining -= dt;
                    if (CapsuleRemaining <= 0f) State = DrillLifecycle.Constructing;
                    break;

                case DrillLifecycle.Constructing:
                    BuildRemaining -= dt;
                    if (BuildRemaining <= 0f)
                    {
                        BuildRemaining = 0f;
                        State = DrillLifecycle.Active;
                    }
                    break;

                case DrillLifecycle.Damaged:
                    // Construction pauses; resumes 8s after the last hit.
                    damageResumeTimer -= dt;
                    if (damageResumeTimer <= 0f) State = DrillLifecycle.Constructing;
                    break;

                case DrillLifecycle.Active:
                    // VS-017A.2 §B3: an upgrading drill produces absolutely nothing —
                    // only the upgrade job itself advances.
                    if (ActiveUpgradeJob != null) return ActiveUpgradeJob.Tick(dt);
                    Produce(dt);
                    break;
            }
            return false;
        }

        private void Produce(float dt)
        {
            if (ProductionSuspended) return;                       // dominance lost/contested
            if (StoredAmount >= StorageCapacity - 0.0001f) return; // full: stop entirely, keep fraction
            Accumulator += ProductionPerMinute / 60f * dt * TestProductionMultiplier;
            float whole = Mathf.Floor(Accumulator);
            if (whole <= 0f) return;
            float space = StorageCapacity - StoredAmount;
            float deposited = Mathf.Min(whole, space);
            StoredAmount += deposited;
            Accumulator -= deposited; // only consume what fit; overflow fraction is held
        }

        /// <summary>Withdraws up to <paramref name="requested"/> from the store; returns what was taken.</summary>
        public float Withdraw(float requested)
        {
            float taken = Mathf.Clamp(requested, 0f, StoredAmount);
            StoredAmount -= taken;
            return taken;
        }

        /// <summary>
        /// Deposits up to <paramref name="amount"/> back into the store,
        /// capped by remaining capacity; returns what actually fit. Used by a
        /// Collector vehicle returning unfully-delivered cargo (VS-007 §5) —
        /// the only sanctioned way to give resources back to a drill; never
        /// mutate StoredAmount from outside this class.
        /// </summary>
        public float Deposit(float amount)
        {
            if (amount <= 0f) return 0f;
            float space = Mathf.Max(0f, StorageCapacity - StoredAmount);
            float added = Mathf.Min(amount, space);
            StoredAmount += added;
            return added;
        }

        /// <summary>Hit during construction: pause and restart the 8s resume timer.</summary>
        public void OnDamaged()
        {
            if (State == DrillLifecycle.Constructing || State == DrillLifecycle.Damaged)
            {
                State = DrillLifecycle.Damaged;
                damageResumeTimer = DamageResumeSeconds;
            }
        }

        public bool CanCancelForKitReturn => State == DrillLifecycle.CapsuleInTransit
                                             || State == DrillLifecycle.Constructing
                                             || State == DrillLifecycle.Damaged;
    }
}
