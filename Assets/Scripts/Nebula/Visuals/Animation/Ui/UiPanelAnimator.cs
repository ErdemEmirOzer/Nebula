using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Fades and slides one panel, on the UI clock.
    /// <para>
    /// The panel itself still decides when it is open — this reads that and
    /// nothing else. It captures the authored alpha and position once, so a
    /// thousand open/close cycles land on exactly the same place. It runs on
    /// the director's unscaled channel, so a panel opened during a pause still
    /// animates while the world stays frozen.
    /// </para>
    /// <para>
    /// Input safety is part of the animation, not a side effect of it: the
    /// moment a panel starts closing it stops accepting clicks, so a button
    /// cannot be pressed on a panel that is on its way out.
    /// </para>
    /// </summary>
    public sealed class UiPanelAnimator : MonoBehaviour, IVisualAnimationParticipant
    {
        private CanvasGroup group;
        private RectTransform rect;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private System.Func<bool> isOpen;

        private float baseAlpha = 1f;
        private Vector2 basePosition;
        private bool captured;

        private float progress;
        private bool lastOpen;

        /// <summary>What the panel is doing right now. Test-facing.</summary>
        public UiPanelPhase Phase { get; private set; } = UiPanelPhase.Hidden;

        /// <summary>How far through the transition it is, 0..1. Test-facing.</summary>
        public float Progress => progress;

        /// <summary>The authored position this panel always returns to. Test-facing.</summary>
        public Vector2 BasePosition => basePosition;

        /// <summary>The authored alpha this panel always returns to. Test-facing.</summary>
        public float BaseAlpha => baseAlpha;

        public bool IsAlive => this != null && gameObject != null;

        /// <summary>
        /// <paramref name="openPredicate"/> reads the panel's own open state. It
        /// is never written, so the animation can never open or close anything.
        /// </summary>
        public void Initialize(NebulaAnimationDirector animationDirector, System.Func<bool> openPredicate)
        {
            director = animationDirector;
            isOpen = openPredicate;

            Capture();

            // Start wherever the panel really is, so a session that boots with a
            // panel already open does not play an entry for it.
            bool open = isOpen != null && isOpen();
            progress = open ? 1f : 0f;
            lastOpen = open;
            Phase = open ? UiPanelPhase.Shown : UiPanelPhase.Hidden;

            if (director != null) registration = director.RegisterUnscaled(this);
            Apply();
        }

        /// <summary>
        /// Records the authored pose once. Everything after is an offset from
        /// it, which is what makes repeated cycles drift-free.
        /// </summary>
        private void Capture()
        {
            if (captured) return;

            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            rect = GetComponent<RectTransform>();

            baseAlpha = group.alpha;
            basePosition = rect != null ? rect.anchoredPosition : Vector2.zero;
            captured = true;
        }

        public void VisualTick(float deltaSeconds)
        {
            if (isOpen == null) return;

            bool open = isOpen();
            if (open != lastOpen)
            {
                Phase = open ? UiPanelPhase.Entering : UiPanelPhase.Exiting;
                lastOpen = open;
            }

            float duration = UiAnimationProfiles.DurationFor(Phase);
            float target = open ? 1f : 0f;

            progress = duration <= 0f
                ? target
                : Mathf.MoveTowards(progress, target, deltaSeconds / duration);

            if (Mathf.Approximately(progress, target))
                Phase = open ? UiPanelPhase.Shown : UiPanelPhase.Hidden;

            Apply();
        }

        private void Apply()
        {
            if (!captured) return;

            float eased = UiAnimationProfiles.Ease(progress);

            if (group != null)
            {
                group.alpha = baseAlpha * eased;

                bool interactive = UiAnimationProfiles.IsInteractive(Phase);
                group.interactable = interactive;
                group.blocksRaycasts = interactive;
            }

            if (rect != null)
            {
                float slide = UiAnimationProfiles.SlideDistance() * (1f - eased);
                rect.anchoredPosition = basePosition + new Vector2(0f, -slide);
            }
        }

        /// <summary>Puts the panel back exactly where it was authored.</summary>
        public void ResetToBasePose()
        {
            if (!captured) return;
            if (group != null) group.alpha = baseAlpha;
            if (rect != null) rect.anchoredPosition = basePosition;
        }

        private void OnDestroy()
        {
            registration.Release();
            isOpen = null;
            director = null;
        }
    }
}
