using UnityEngine;
using UnityEngine.UI;
using Nebula.Progression;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// The XP and level-up line, top centre.
    /// <para>
    /// Dropped by the redesign and restored here. <c>ProgressionHud</c> coalesced
    /// awards landing within two seconds of each other into a single line —
    /// "+1 200 XP · SEVİYE 3 → 4 · +2 GELİŞİM PUANI" — which matters because
    /// progression fires in bursts: finishing a scan can award four separate
    /// events in the same frame, and four separate popups would be noise rather
    /// than a reward.
    /// </para>
    /// <para>
    /// A level-up is a celebration, not a status, so it uses the celebration
    /// colour rather than anything from the status palette. That is also what
    /// keeps it from being dimmed by flash reduction alongside genuine alarms.
    /// </para>
    /// </summary>
    public sealed class NebulaHudNotifications : MonoBehaviour
    {
        /// <summary>Awards landing inside this window merge into one line.</summary>
        public const float CoalesceSeconds = 2f;

        /// <summary>How long the merged line stays up once the burst has settled.</summary>
        public const float HoldSeconds = 4f;

        private NebulaUiFactory.LabelHandle line;

        /// <summary>Fades the existing line. Creates no text and no popup.</summary>
        private readonly UiNotificationAnimator notifyAnimation = new UiNotificationAnimator();

        /// <summary>Flashes on a real XP gain only. A refused award raises nothing.</summary>
        private readonly UiValueAnimator xpDelta = new UiValueAnimator();

        /// <summary>Test-facing: real level changes celebrated.</summary>
        public int LevelUpsCelebrated { get; private set; }

        /// <summary>Test-facing: real XP deltas shown.</summary>
        public int XpDeltasShown => xpDelta.HighlightsShown;

        /// <summary>Test-facing: notification entries actually played.</summary>
        public int NotificationsPlayed => notifyAnimation.EntriesPlayed;

        private bool windowActive;
        private int windowXp, windowLevelStart, windowLevelEnd, windowPoints;
        private float windowLastTime;
        private bool subscribed;

        // ---- the composed line (P2-PERF-001) ---------------------------------
        // One string, and the four numbers it was composed from. Not a dictionary,
        // not a growing buffer: the window has exactly one line at a time, so the
        // cache is a single field and can never grow. It is state-aware rather than
        // time-based — the moment any of the four inputs differs the line is rebuilt,
        // so a cached line can never be shown for a window it does not describe.
        private string composedLine;
        private int composedXp = int.MinValue, composedLevelStart = int.MinValue;
        private int composedLevelEnd = int.MinValue, composedPoints = int.MinValue;

        /// <summary>Test-facing: how many times the line's text was actually composed.</summary>
        public int LineCompositions { get; private set; }

        public static NebulaHudNotifications Create(NebulaUiShell shell)
        {
            var go = new GameObject("HUD Notifications");
            var notifications = go.AddComponent<NebulaHudNotifications>();
            notifications.Build(shell);
            return notifications;
        }

        private void Build(NebulaUiShell shell)
        {
            // The overlay layer: a notification must be able to sit over a menu,
            // and must never take a click away from what is underneath it.
            var root = NebulaUiFactory.Group(shell.Root(UiLayer.Overlay), "Notifications");
            NebulaUiFactory.Place(root, new Vector2(0.5f, 1f), new Vector2(0f, -24f),
                new Vector2(900f, 32f));
            // The controller lives on the layer root, never inside the visual it
            // toggles: a widget parented into its own panel is switched off
            // together with it, its Update stops, and it can never decide to
            // come back. Hiding the panel must not disable the thing that shows it.
            transform.SetParent(shell.Root(UiLayer.Overlay), false);

            line = NebulaUiFactory.Label(root, "Line", "", NebulaUiDesignTokens.SizeValue,
                NebulaUiDesignTokens.Celebration, TextAnchor.MiddleCenter, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingButton);
            NebulaUiFactory.Fill(line.Text.rectTransform);
            line.Text.enabled = false;
        }

        private void OnEnable()
        {
            if (subscribed) return;
            subscribed = true;
            ProgressionService.Awarded += OnAwarded;
        }

        private void OnDisable()
        {
            if (!subscribed) return;
            subscribed = false;
            ProgressionService.Awarded -= OnAwarded;
        }

        /// <summary>
        /// The award has already been granted by the time this fires, so the flash
        /// follows a real gain and never a refusal. Only a real level change counts
        /// as a level-up.
        /// </summary>
        private void OnAwarded(AwardResult result)
        {
            if (!result.Awarded) return;

            if (!windowActive || Time.time - windowLastTime > CoalesceSeconds)
            {
                windowActive = true;
                windowXp = 0;
                windowPoints = 0;
                windowLevelStart = result.LevelBefore;
            }

            windowXp += result.Xp;
            windowPoints += result.PointsGained;
            windowLevelEnd = result.LevelAfter;
            windowLastTime = Time.time;

            xpDelta.RaiseDelta(result.Xp);
            if (result.LevelAfter > result.LevelBefore) LevelUpsCelebrated++;
        }

        private void Update()
        {
            if (windowActive)
            {
                line.Value = ComposedLine();

                if (Time.time - windowLastTime > HoldSeconds) windowActive = false;
            }

            // Enter and exit only; the hold in between is the window's own, and is
            // deliberately not something the animator owns.
            float alpha = notifyAnimation.Tick(windowActive, Time.unscaledDeltaTime);
            if (alpha <= 0f)
            {
                if (line.Text.enabled) line.Text.enabled = false;
                return;
            }

            if (!line.Text.enabled) line.Text.enabled = true;
            Color color = NebulaUiDesignTokens.Celebration;
            line.Text.color = new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>
        /// The window's line, composed once per award burst instead of once per frame
        /// (P2-PERF-001).
        /// <para>
        /// The four numbers only ever change inside <see cref="OnAwarded"/>; the text was
        /// nevertheless rebuilt every frame the window was up, which at a 4-second hold is
        /// hundreds of identical strings per reward — landing precisely on combat and
        /// production completions, the busiest moments the game has. The line is now
        /// rebuilt exactly when one of its inputs differs from the one it was built from,
        /// so the produced text is byte-for-byte what it always was and the cost is one
        /// composition per burst.
        /// </para>
        /// </summary>
        private string ComposedLine()
        {
            if (composedLine != null
                && composedXp == windowXp
                && composedLevelStart == windowLevelStart
                && composedLevelEnd == windowLevelEnd
                && composedPoints == windowPoints)
                return composedLine;

            composedXp = windowXp;
            composedLevelStart = windowLevelStart;
            composedLevelEnd = windowLevelEnd;
            composedPoints = windowPoints;
            composedLine = windowLevelEnd > windowLevelStart
                ? $"+{windowXp:N0} XP  ·  SEVİYE {windowLevelStart} → {windowLevelEnd}  ·  +{windowPoints} GELİŞİM PUANI"
                : $"+{windowXp:N0} XP";
            LineCompositions++;
            return composedLine;
        }
    }
}
