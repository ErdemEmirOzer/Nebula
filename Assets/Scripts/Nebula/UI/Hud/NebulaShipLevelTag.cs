using TMPro;
using UnityEngine;
using Nebula.Progression;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// The level tag floating above the ship — "L7", or "L30 MAX" at the cap.
    /// <para>
    /// This is the one part of <c>ProgressionHud</c> that was not screen-space
    /// and therefore not replaced by <see cref="NebulaHudNotifications"/>. It was
    /// nearly deleted along with the rest of that class, which would have taken a
    /// visible piece of the game away with nothing standing in for it.
    /// </para>
    /// <para>
    /// Same position, same size, same billboard, same gold — it is deliberately a
    /// faithful copy rather than a redesign, because the tag is read at a glance
    /// from a distance and there is nothing about it to improve.
    /// </para>
    /// <para>
    /// L-19 finishes here. This was the last player-facing label on the bitmap
    /// face, held back by a comment claiming a world-space tag "cannot take an SDF
    /// asset". It can: <see cref="TextMeshPro"/> is TMP's world-space component and
    /// takes the same asset every screen-space label already uses. The exemption
    /// was a belief, not a constraint, and it kept LegacyRuntime.ttf on screen in a
    /// release player long after the migration was called done.
    /// </para>
    /// </summary>
    public sealed class NebulaShipLevelTag : MonoBehaviour
    {
        /// <summary>Height above the ship origin. Unchanged from the old HUD.</summary>
        public const float Height = 4.5f;

        /// <summary>
        /// The size that keeps the tag exactly as big as it was.
        /// <para>
        /// The old tag was a <c>TextMesh</c> at <c>fontSize 48</c> and
        /// <c>characterSize 0.12</c>. TextMesh builds its glyphs at fontSize then
        /// scales by <c>characterSize / 10</c>, so one em stood 48 × 0.12 / 10 =
        /// 0.576 world units tall. TMP's world-space component uses the same 10:1
        /// point-to-unit ratio, so 5.76 points reproduces that height rather than
        /// approximating it — the tag is read at a distance and a size change would
        /// be the one visible regression this swap could cause.
        /// </para>
        /// </summary>
        public const float WorldFontSize = 5.76f;

        private TextMeshPro tag;
        private bool subscribed;

        /// <summary>Test-facing: what the tag currently reads.</summary>
        public string Text => tag != null ? tag.text : null;

        public static NebulaShipLevelTag Create(Transform ship)
        {
            // Built with its RectTransform up front. TMP_Text is a MaskableGraphic
            // and would otherwise have one swapped in underneath the transform this
            // method has already positioned.
            var go = new GameObject("Ship Level Tag", typeof(RectTransform));
            go.transform.SetParent(ship, false);
            go.transform.localPosition = new Vector3(0f, Height, 0f);

            var level = go.AddComponent<NebulaShipLevelTag>();
            level.Build();
            return level;
        }

        private void Build()
        {
            // Bottom-centre pivot plus bottom-centre alignment puts the text's lower
            // edge on the tag's origin, which is where TextAnchor.LowerCenter put it.
            // Without the pivot the glyphs would straddle the ship instead of
            // sitting above it.
            var rect = (RectTransform)transform;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(4f, 1f);

            tag = gameObject.AddComponent<TextMeshPro>();
            tag.font = NebulaUiFactory.SharedFontAsset;
            tag.fontSize = WorldFontSize;
            tag.alignment = TextAlignmentOptions.Bottom;
            tag.color = NebulaUiDesignTokens.Celebration;

            // "L30 MAX" must stay on one line at any distance; the rect is a
            // layout box, not a clip, and wrapping it would be the one way this
            // reads differently from the tag it replaces.
            tag.enableWordWrapping = false;
            tag.overflowMode = TextOverflowModes.Overflow;
            tag.raycastTarget = false;

            gameObject.AddComponent<Billboard>();
            Refresh();
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
        /// Only a real award moves the tag. A refused one leaves it alone, which
        /// is the same rule every other progression surface follows.
        /// </summary>
        private void OnAwarded(AwardResult result)
        {
            if (!result.Awarded) return;
            Refresh();
        }

        private void Refresh()
        {
            if (tag == null) return;
            ProgressionState state = ProgressionService.State;
            tag.text = state.IsMaxLevel ? $"L{state.Level} MAX" : $"L{state.Level}";
        }
    }
}
