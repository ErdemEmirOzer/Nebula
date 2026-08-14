namespace Nebula.Combat
{
    public enum TrainingId { A, B, C }

    /// <summary>
    /// One training's runtime progress (VS-013 §7). Pure step-index state
    /// machine: no XP/resource/salvage/permanent-weapon grant path exists
    /// anywhere in this class — compliance with "never rewards" is by
    /// omission, not a flag to check. Skippable and replayable at any time
    /// (<see cref="Skip"/>/<see cref="Reset"/>) without affecting any other
    /// progression system.
    /// </summary>
    public class TrainingRun
    {
        public const float FailureRetrySeconds = 3f;
        public const int FailuresBeforeHelp = 2;

        public TrainingId Id { get; }
        public int StepCount { get; }
        private readonly bool[] stepCompleted;

        public int CurrentStep { get; private set; }
        public bool Completed => CurrentStep >= StepCount;
        public int FailuresThisStep { get; private set; }
        public bool HelpUnlockedThisStep { get; private set; }
        private float retryDelayRemaining;

        public bool CanAct => retryDelayRemaining <= 0f;

        public TrainingRun(TrainingId id, int stepCount)
        {
            Id = id; StepCount = stepCount; stepCompleted = new bool[stepCount];
        }

        public bool IsStepCompleted(int index) => index >= 0 && index < stepCompleted.Length && stepCompleted[index];

        public void Tick(float dt)
        {
            if (retryDelayRemaining > 0f) retryDelayRemaining = UnityEngine.Mathf.Max(0f, retryDelayRemaining - dt);
        }

        public void CompleteStep()
        {
            if (Completed) return;
            stepCompleted[CurrentStep] = true;
            CurrentStep++;
            FailuresThisStep = 0;
            HelpUnlockedThisStep = false;
        }

        /// <summary>The failed step restarts 3s later from the SAME step (never earlier progress lost, never skipped forward).</summary>
        public void FailStep()
        {
            if (Completed) return;
            FailuresThisStep++;
            retryDelayRemaining = FailureRetrySeconds;
            if (FailuresThisStep >= FailuresBeforeHelp) HelpUnlockedThisStep = true;
        }

        public void Skip()
        {
            for (int i = 0; i < stepCompleted.Length; i++) stepCompleted[i] = true;
            CurrentStep = StepCount;
        }

        public void Reset()
        {
            for (int i = 0; i < stepCompleted.Length; i++) stepCompleted[i] = false;
            CurrentStep = 0; FailuresThisStep = 0; HelpUnlockedThisStep = false; retryDelayRemaining = 0f;
        }

        public void RestoreFromSave(int currentStep, bool[] completedFlags)
        {
            CurrentStep = UnityEngine.Mathf.Clamp(currentStep, 0, StepCount);
            if (completedFlags != null)
                for (int i = 0; i < stepCompleted.Length && i < completedFlags.Length; i++) stepCompleted[i] = completedFlags[i];
        }

        public bool[] SnapshotCompletedFlags()
        {
            var copy = new bool[stepCompleted.Length];
            System.Array.Copy(stepCompleted, copy, stepCompleted.Length);
            return copy;
        }
    }

    /// <summary>
    /// Campaign-wide holder for all three trainings (VS-013 §7). Training A
    /// is available from the very start; B requires Weapon Coordination T1
    /// with a Plasma actually produced/equipped; C requires T2 with an EM
    /// actually produced/equipped — none of these gates ever block normal
    /// module/progression purchases in the other direction.
    /// </summary>
    public class TrainingProgress
    {
        // Internal step granularity is this slice's own breakdown of each training's
        // described objectives — not itself a numeric value from the design brief.
        public TrainingRun A { get; } = new TrainingRun(TrainingId.A, stepCount: 6);
        public TrainingRun B { get; } = new TrainingRun(TrainingId.B, stepCount: 4);
        public TrainingRun C { get; } = new TrainingRun(TrainingId.C, stepCount: 4);

        public bool IsUnlocked(TrainingId id, int weaponCoordinationTier, bool plasmaEquippedAnywhere, bool emEquippedAnywhere) => id switch
        {
            TrainingId.A => true,
            TrainingId.B => weaponCoordinationTier >= CombatCatalog.PlasmaUnlockWeaponCoordinationTier && plasmaEquippedAnywhere,
            TrainingId.C => weaponCoordinationTier >= CombatCatalog.EmUnlockWeaponCoordinationTier && emEquippedAnywhere,
            _ => false
        };

        public TrainingRun Get(TrainingId id) => id switch { TrainingId.A => A, TrainingId.B => B, TrainingId.C => C, _ => null };
    }
}
