using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>Where a one-shot notification is in its life.</summary>
    public enum UiNotificationPhase
    {
        Idle,
        Entering,
        Holding,
        Exiting
    }

    /// <summary>
    /// Fades one existing notification line in, holds it for exactly as long as
    /// the panel already holds it, and fades it out.
    /// <para>
    /// It creates no text, no popup and no queue. The panel still decides what
    /// the line says and when it is up; this only supplies the alpha, so a
    /// message that was already on screen when a save loaded simply appears
    /// rather than replaying its entrance.
    /// </para>
    /// </summary>
    public sealed class UiNotificationAnimator
    {
        private float alpha;
        private bool wasShowing;

        /// <summary>What the notification is doing. Test-facing.</summary>
        public UiNotificationPhase Phase { get; private set; } = UiNotificationPhase.Idle;

        /// <summary>The alpha to apply, 0..1. Test-facing.</summary>
        public float Alpha => alpha;

        /// <summary>Entries played since this started. Test-facing; never a gameplay count.</summary>
        public int EntriesPlayed { get; private set; }

        /// <summary>
        /// Adopts whatever is on screen without playing anything — the first
        /// frame, and every load.
        /// </summary>
        public void AdoptSilently(bool showing)
        {
            alpha = showing ? 1f : 0f;
            wasShowing = showing;
            Phase = showing ? UiNotificationPhase.Holding : UiNotificationPhase.Idle;
        }

        /// <summary>
        /// <paramref name="showing"/> is the panel's own decision. The hold
        /// between entry and exit is however long the panel keeps it true, so
        /// no real duration is changed here.
        /// </summary>
        public float Tick(bool showing, float deltaSeconds)
        {
            if (showing != wasShowing)
            {
                Phase = showing ? UiNotificationPhase.Entering : UiNotificationPhase.Exiting;
                if (showing) EntriesPlayed++;
                wasShowing = showing;
            }

            float duration = AnimationAccessibility.MotionOff
                ? 0f
                : Phase == UiNotificationPhase.Entering ? UiAnimationProfiles.NotificationEnterSeconds
                : Phase == UiNotificationPhase.Exiting ? UiAnimationProfiles.NotificationExitSeconds
                : 0f;

            float target = showing ? 1f : 0f;
            alpha = duration <= 0f
                ? target
                : Mathf.MoveTowards(alpha, target, deltaSeconds / duration);

            if (Mathf.Approximately(alpha, target))
                Phase = showing ? UiNotificationPhase.Holding : UiNotificationPhase.Idle;

            return alpha;
        }

        public void Reset()
        {
            alpha = 0f;
            wasShowing = false;
            Phase = UiNotificationPhase.Idle;
            EntriesPlayed = 0;
        }
    }
}
