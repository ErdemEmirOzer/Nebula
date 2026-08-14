using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Visual-only motion settings. Each one changes how much the picture
    /// moves and nothing else: no speed, no rotation rate, no gameplay value,
    /// and nothing serialized.
    /// <para>
    /// Flash and brightness live in <see cref="LightingAccessibility"/> and
    /// stay there — this class never produces or suppresses a flash.
    /// </para>
    /// </summary>
    public static class AnimationAccessibility
    {
        /// <summary>What survives Reduced Motion. Enough that a state still reads, little enough to stop the picture swimming.</summary>
        public const float ReducedMotionScale = 0.25f;

        /// <summary>Hard ceiling on visual bank, in degrees, whatever a profile asks for.</summary>
        public const float MaxVisualBankDegrees = 18f;

        /// <summary>The ceiling Camera Comfort lowers it to.</summary>
        public const float ComfortBankDegrees = 6f;

        private static bool reducedMotion;
        private static bool motionOff;
        private static bool cameraComfort;

        /// <summary>
        /// Bumped every time one of these settings actually changes.
        /// <para>
        /// The director watches this so that switching a setting is reposed on
        /// the very next tick instead of waiting for the pose throttle to come
        /// round. Without it, <c>NebulaAnimationDirector.Tick(0.02f)</c> — one
        /// tick, below the 25 ms interval of the Medium profile — returned at
        /// the accumulator guard and posed nobody, which is how four fixtures
        /// (Ani053Poi, Ani05World, Ani056, ProcessingBench) all reported
        /// "motion off means the authored pose" against a presenter that had
        /// simply never been asked.
        /// </para>
        /// <para>
        /// A counter rather than an event: an event needs unsubscribing, and a
        /// static event holding a destroyed director is exactly the leak this
        /// project keeps finding.
        /// </para>
        /// </summary>
        public static int SettingsVersion { get; private set; }

        /// <summary>
        /// Cuts secondary motion — banking, vibration, weight transfer — well
        /// down without removing it, so the ship still visibly answers a turn.
        /// </summary>
        public static bool ReducedMotion
        {
            get => reducedMotion;
            set { if (reducedMotion == value) return; reducedMotion = value; SettingsVersion++; }
        }

        /// <summary>
        /// Stops decorative motion entirely. Required mechanical start/end
        /// poses may still be applied, but instantly rather than travelled to,
        /// so nothing is ever left half-open.
        /// </summary>
        public static bool MotionOff
        {
            get => motionOff;
            set { if (motionOff == value) return; motionOff = value; SettingsVersion++; }
        }

        /// <summary>
        /// Tightens the visual bank ceiling. Never touches the camera itself —
        /// animation is not allowed near the camera root at all.
        /// </summary>
        public static bool CameraComfort
        {
            get => cameraComfort;
            set { if (cameraComfort == value) return; cameraComfort = value; SettingsVersion++; }
        }

        public static void ResetToDefaults()
        {
            ReducedMotion = false;
            MotionOff = false;
            CameraComfort = false;
        }

        /// <summary>Multiplier for any decorative amplitude, before the profile's own scale.</summary>
        public static float MotionScale
        {
            get
            {
                if (MotionOff) return 0f;
                return ReducedMotion ? ReducedMotionScale : 1f;
            }
        }

        /// <summary>Applies the motion settings to a decorative amplitude.</summary>
        public static float ScaleDecorative(float amplitude) => amplitude * MotionScale;

        /// <summary>The bank ceiling actually in force.</summary>
        public static float BankCeilingDegrees
        {
            get
            {
                if (MotionOff) return 0f;
                float ceiling = CameraComfort ? ComfortBankDegrees : MaxVisualBankDegrees;
                return ReducedMotion ? ceiling * ReducedMotionScale : ceiling;
            }
        }

        /// <summary>
        /// True when a state change should snap rather than travel. Motion Off
        /// still needs a hangar door to end up open — it just must not be
        /// watched swinging.
        /// </summary>
        public static bool SnapStateChanges => MotionOff;
    }
}
