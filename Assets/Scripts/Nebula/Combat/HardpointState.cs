using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>
    /// One hardpoint's mutable state (VS-013 §2/§3): which weapon (if any)
    /// is mounted, its tier, and this hardpoint's own 0-100 local heat.
    /// Overheating disables only THIS hardpoint, never the whole ship. Pure
    /// C#, no MonoBehaviour/scene dependency.
    /// </summary>
    public class HardpointState
    {
        public int Index { get; }
        public WeaponFamily? Weapon { get; private set; }
        public int Tier { get; private set; }
        public float Heat { get; private set; }
        public bool Overheated { get; private set; }
        private float overheatCooldownRemaining;
        private float sinceLastFire = float.MaxValue;

        public bool IsEmpty => Weapon == null;
        public bool CanFire => !IsEmpty && !Overheated;

        public HardpointState(int index) { Index = index; }

        /// <summary>Same weapon family may occupy more than one hardpoint (VS-013 §2).</summary>
        public void Equip(WeaponFamily family, int tier)
        {
            Weapon = family;
            Tier = tier;
            Heat = 0f;
            Overheated = false;
            overheatCooldownRemaining = 0f;
        }

        public void Unequip() { Weapon = null; Tier = 0; Heat = 0f; Overheated = false; overheatCooldownRemaining = 0f; }

        public void RestoreFromSave(WeaponFamily? weapon, int tier, float heat, bool overheated, float overheatCooldownRemainingSaved)
        {
            Weapon = weapon; Tier = tier;
            Heat = Mathf.Clamp(heat, 0f, WeaponCatalog.HardpointMaxHeat);
            Overheated = overheated;
            overheatCooldownRemaining = Mathf.Max(0f, overheatCooldownRemainingSaved);
        }

        /// <summary>Adds heat (from firing) and resets the idle-cool delay; triggers overheat lockout the instant heat reaches the cap.</summary>
        public void AddHeat(float amount)
        {
            if (amount <= 0f) return;
            Heat = Mathf.Min(WeaponCatalog.HardpointMaxHeat, Heat + amount);
            sinceLastFire = 0f;
            if (Heat >= WeaponCatalog.HardpointMaxHeat - 0.0001f && !Overheated)
            {
                Overheated = true;
                overheatCooldownRemaining = WeaponCatalog.OverheatLockoutSeconds;
            }
        }

        /// <summary>environmentalHeatPerSecond is the star-risk Critical-zone environmental heat (0 outside Critical) — applies regardless of whether this hardpoint is currently firing.</summary>
        public void Tick(float dt, float environmentalHeatPerSecond)
        {
            sinceLastFire += dt;

            if (Overheated)
            {
                overheatCooldownRemaining -= dt;
                if (overheatCooldownRemaining <= 0f)
                {
                    Overheated = false;
                    overheatCooldownRemaining = 0f;
                    Heat = 0f;
                }
                return;
            }

            // Idle cooling must be evaluated on the heat this hardpoint carried
            // BEFORE this tick's environmental heat is added -- otherwise a
            // Critical-zone environmental gain would be instantly wiped out by
            // the same-frame cooldown subtraction whenever the hardpoint hasn't
            // fired recently (the common case), silently defeating the "2%/s of
            // max heat as environmental heat" rule.
            if (sinceLastFire >= WeaponCatalog.HardpointCoolIdleDelaySeconds)
            {
                Heat = Mathf.Max(0f, Heat - WeaponCatalog.HardpointCoolPerSecond * dt);
            }

            if (environmentalHeatPerSecond > 0f)
            {
                Heat = Mathf.Min(WeaponCatalog.HardpointMaxHeat, Heat + environmentalHeatPerSecond * dt);
            }
        }
    }
}
