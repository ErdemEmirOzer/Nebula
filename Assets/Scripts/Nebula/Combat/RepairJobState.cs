using UnityEngine;

namespace Nebula.Combat
{
    public enum RepairRejectReason { None, InCombat, TooSoonAfterDamage, TooFast, EmergencyShutdown, AlreadyActive, AlreadyAtCap, InsufficientResources }

    /// <summary>
    /// One active repair job's package-progress state (VS-013.1 §5). Single
    /// active job at a time (mirrors WeaponProductionJob's single-slot
    /// convention). Progress advances strictly in proportion to REAL HP
    /// actually applied this tick (never to elapsed time alone) — if the
    /// ship is already at its current cap, progress (and therefore resource
    /// consumption) simply stalls at 0 rather than wasting reserved
    /// resources for zero gain. Pause/resume never resets progress; only
    /// Cancel does (and only after refunding the still-reserved remainder).
    /// </summary>
    public class RepairJobState
    {
        public bool Active { get; private set; }
        public bool Paused { get; private set; }
        /// <summary>The Hull Form the CURRENT package's reserved cost was computed against.</summary>
        public int HullFormForCurrentPackage { get; private set; }
        /// <summary>0..1 progress within the current reserved package.</summary>
        public float PackageProgress01 { get; private set; }

        public bool PackageComplete => Active && PackageProgress01 >= 1f - 0.0001f;

        public void StartPackage(int hullForm)
        {
            Active = true;
            Paused = false;
            HullFormForCurrentPackage = hullForm;
            PackageProgress01 = 0f;
        }

        public void Pause() { if (Active) Paused = true; }
        public void Resume() { if (Active) Paused = false; }

        public void Stop()
        {
            Active = false;
            Paused = false;
            PackageProgress01 = 0f;
        }

        /// <summary>
        /// Advances progress by the REAL fraction of the package span that
        /// `actuallyAppliedHp` represents (never by dt alone) and returns
        /// that same fraction — the caller consumes exactly that fraction of
        /// the reserved package cost from cargo, keeping progress/healing/
        /// consumption permanently in lockstep.
        /// </summary>
        public float AdvanceByAppliedHp(float actuallyAppliedHp, float combinedMaxCapacity)
        {
            if (!Active || Paused || actuallyAppliedHp <= 0f) return 0f;
            float packageSpanHp = RepairCatalog.PackageFraction * combinedMaxCapacity;
            if (packageSpanHp <= 0.0001f) return 0f;
            float delta = Mathf.Min(actuallyAppliedHp / packageSpanHp, 1f - PackageProgress01);
            PackageProgress01 = Mathf.Min(1f, PackageProgress01 + delta);
            return delta;
        }

        /// <summary>Resets package progress to start the NEXT package (auto-chained while the player keeps repairing) — Active stays true, a fresh reservation is the caller's responsibility.</summary>
        public void ResetForNextPackage(int hullForm)
        {
            HullFormForCurrentPackage = hullForm;
            PackageProgress01 = 0f;
        }

        public void RestoreFromSave(bool active, bool paused, int hullFormForCurrentPackage, float packageProgress01)
        {
            Active = active;
            Paused = paused;
            HullFormForCurrentPackage = hullFormForCurrentPackage;
            PackageProgress01 = Mathf.Clamp01(packageProgress01);
        }
    }
}
