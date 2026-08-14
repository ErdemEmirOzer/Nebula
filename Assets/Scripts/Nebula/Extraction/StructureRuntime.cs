using UnityEngine;

namespace Nebula.Extraction
{
    /// <summary>
    /// Pure lifecycle + durability for a Command/Laser surface structure.
    /// Capsule (3-8s) → Constructing (catalog seconds, Shield 0 / Armor·Hull at
    /// 50%) → Active. Damage during construction pauses it for 8s; hull hitting
    /// 0 Disables (never destroys) the structure. Reuses <see cref="DrillLifecycle"/>.
    /// </summary>
    public class StructureRuntime
    {
        private const float DamageResumeSeconds = 8f;

        public SurfaceStructureType Type { get; }
        public DrillLifecycle State { get; private set; }
        public float CapsuleRemaining { get; private set; }
        public float BuildRemaining { get; private set; }
        public Durability Durability { get; private set; }
        /// <summary>VS-014 §2: 1 for every structure built before this slice (and every fresh Laser/Plasma T1 build); only Command/Laser/Plasma can ever reach 2 this slice.</summary>
        public int Tier { get; private set; } = 1;

        private readonly float buildSeconds;
        private float damageResumeTimer;

        public bool IsActive => State == DrillLifecycle.Active;
        public bool IsDisabled => State == DrillLifecycle.Disabled;

        public StructureRuntime(SurfaceStructureDef def, float capsuleSeconds, Sectors.EnvironmentClass environment)
        {
            Type = def.Type;
            buildSeconds = def.BuildSeconds;
            CapsuleRemaining = Mathf.Clamp(capsuleSeconds, 3f, 8f);
            // VS-009 §3: environment scales only this post-capsule construction duration.
            BuildRemaining = Sectors.EnvironmentDurationResolver.ScaleDuration(def.BuildSeconds, environment);
            State = DrillLifecycle.CapsuleInTransit;
            Durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
        }

        public StructureRuntime(SurfaceStructureDef def, DrillLifecycle state, float capsuleRemaining,
            float buildRemaining, Durability durability, int tier = 1)
        {
            Type = def.Type;
            buildSeconds = def.BuildSeconds;
            State = state;
            CapsuleRemaining = capsuleRemaining;
            BuildRemaining = buildRemaining;
            Durability = durability;
            Tier = tier;
        }

        /// <summary>
        /// VS-014 §2: atomic tier upgrade completion — preserves the CURRENT
        /// Shield/Armor/Hull PERCENTAGE against the new tier's max (never a
        /// free full heal, never a loss beyond rounding), never touches
        /// State (the structure stays Active throughout its own upgrade).
        /// Mirrors LogisticsCenterRuntime.CompleteUpgrade's exact percentage-
        /// preservation shape — the one established pattern for this project.
        /// </summary>
        public void ApplyTierUpgrade(StructureTierDef newTierDef)
        {
            float shieldFrac = Durability.ShieldMax > 0f ? Durability.Shield / Durability.ShieldMax : 0f;
            float armorFrac = Durability.ArmorMax > 0f ? Durability.Armor / Durability.ArmorMax : 0f;
            float hullFrac = Durability.HullMax > 0f ? Durability.Hull / Durability.HullMax : 0f;

            var newDurability = new Durability(newTierDef.ShieldMax, newTierDef.ArmorMax, newTierDef.HullMax, 0f);
            newDurability.RestoreFromSave(newTierDef.ShieldMax * shieldFrac, newTierDef.ArmorMax * armorFrac, newTierDef.HullMax * hullFrac, Durability.SavedRegenTimer);
            Durability = newDurability;
            Tier = newTierDef.Tier;
        }

        public void Tick(float dt)
        {
            switch (State)
            {
                case DrillLifecycle.CapsuleInTransit:
                    CapsuleRemaining -= dt;
                    if (CapsuleRemaining <= 0f) State = DrillLifecycle.Constructing;
                    break;
                case DrillLifecycle.Constructing:
                    BuildRemaining -= dt;
                    if (BuildRemaining <= 0f) { BuildRemaining = 0f; State = DrillLifecycle.Active; }
                    break;
                case DrillLifecycle.Damaged:
                    damageResumeTimer -= dt;
                    if (damageResumeTimer <= 0f) State = DrillLifecycle.Constructing;
                    break;
                case DrillLifecycle.Active:
                    Durability.Tick(dt);
                    if (Durability.Disabled) State = DrillLifecycle.Disabled;
                    break;
                case DrillLifecycle.Disabled:
                    if (!Durability.Disabled) State = DrillLifecycle.Active; // repaired back up
                    break;
            }
        }

        /// <summary>Combat-free damage entry point (for tests) — routes through the Neutral (1/1/1) profile, so it is exactly equivalent to un-profiled damage.</summary>
        public void ApplyDamage(float amount) => ApplyDamage(amount, Combat.DamageProfile.Neutral);

        /// <summary>VS-014 §3: real weapon damage entry point (ship weapons vs. a hostile structure, or an enemy weapon vs. a planetary defense) — routes through the shared DamageResolver so Laser/Plasma/EM layer multipliers apply exactly like everywhere else.</summary>
        public void ApplyDamage(float rawAmount, Combat.DamageProfile profile)
        {
            Combat.DamageResolver.Apply(Durability, rawAmount, profile);
            if (Durability.Disabled) { State = DrillLifecycle.Disabled; return; }
            if (State == DrillLifecycle.Constructing || State == DrillLifecycle.Damaged)
            {
                State = DrillLifecycle.Damaged;
                damageResumeTimer = DamageResumeSeconds;
            }
        }

        public void Repair(float amount)
        {
            Durability.Repair(amount);
            if (State == DrillLifecycle.Disabled && !Durability.Disabled) State = DrillLifecycle.Active;
        }

        public bool CanCancelForRefund => State == DrillLifecycle.CapsuleInTransit
                                          || State == DrillLifecycle.Constructing
                                          || State == DrillLifecycle.Damaged;
    }
}
