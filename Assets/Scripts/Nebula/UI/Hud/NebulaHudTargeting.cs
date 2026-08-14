using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// What the player is shooting at, and what else is out there.
    /// <para>
    /// Both halves come from <c>CombatHud</c>, which drew them as text. The target
    /// readout was three lines stacked on the reticle — a literal <c>"+"</c>, a
    /// <c>LEAD +240</c> and an <c>EM LOCK 45%</c> — and the other hostiles were a
    /// six-line list of names and distances in a corner, rebuilt from a fresh
    /// <c>List&lt;EnemyAgent&gt;</c> plus a <c>RemoveAll</c> every single frame
    /// (L-09).
    /// </para>
    /// <para>
    /// Here the target reads as a bracket with one distance and a lock meter that
    /// fills, and the others are markers on a ring pointing where they actually
    /// are, which is the thing a text list could never tell you. The markers are
    /// pooled and the candidate list is a field, so a frame costs no allocation.
    /// </para>
    /// </summary>
    public sealed class NebulaHudTargeting : MonoBehaviour
    {
        /// <summary>Markers on screen at once. Beyond this the count speaks for them.</summary>
        public const int MaxMarkers = 6;

        /// <summary>Ring radius, matching the route indicator's so they read as one system.</summary>
        public const float MarkerRadius = 210f;

        private ShipController ship;
        private ShipCombat combat;

        private RectTransform targetRoot;
        private NebulaUiFactory.LabelHandle targetName, targetDistance;
        private NebulaUiFactory.BarHandle lockBar;

        private RectTransform markerRoot;
        private readonly List<Marker> markers = new List<Marker>(MaxMarkers);
        private NebulaUiFactory.LabelHandle overflowLabel;

        /// <summary>Reused every frame. The old HUD allocated two of these per frame.</summary>
        private readonly List<EnemyAgent> candidates = new List<EnemyAgent>(32);

        /// <summary>Eases the lock meter only; the percentage printed stays the real one.</summary>
        private readonly UiValueAnimator lockMeter = new UiValueAnimator();

        /// <summary>Test-facing: whether the target bracket is up.</summary>
        public bool TargetVisible => targetRoot != null && targetRoot.gameObject.activeSelf;

        /// <summary>Test-facing: threat markers currently drawn.</summary>
        public int VisibleMarkers { get; private set; }

        /// <summary>Test-facing: hostiles beyond the marker cap.</summary>
        public int OverflowCount { get; private set; }

        private sealed class Marker
        {
            public RectTransform Root;
            public Image Icon;
            public NebulaUiFactory.LabelHandle Distance;
        }

        public static NebulaHudTargeting Create(NebulaUiShell shell, ShipController shipController,
            ShipCombat shipCombat)
        {
            var go = new GameObject("HUD Targeting");
            var targeting = go.AddComponent<NebulaHudTargeting>();
            targeting.ship = shipController;
            targeting.combat = shipCombat;
            targeting.Build(shell);
            return targeting;
        }

        private void Build(NebulaUiShell shell)
        {
            // The controller hangs off the layer root, never inside a panel it hides.
            transform.SetParent(shell.Root(UiLayer.HudDynamic), false);

            BuildTargetBracket(shell);
            BuildMarkers(shell);
        }

        private void BuildTargetBracket(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudDynamic), "Target",
                NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Critical));
            targetRoot = panel.rectTransform;
            NebulaUiFactory.Place(targetRoot, new Vector2(0.5f, 0.5f), new Vector2(0f, -132f),
                new Vector2(320f, 62f));

            targetName = NebulaUiFactory.Label(targetRoot, "Name", "",
                NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical),
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(targetName.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(14f, -12f), new Vector2(200f, 16f));

            targetDistance = NebulaUiFactory.Value(targetRoot, "Distance", "",
                NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(targetDistance.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-14f, -12f), new Vector2(100f, 16f));

            lockBar = NebulaUiFactory.Bar(targetRoot, "Lock",
                NebulaUiDesignTokens.Status(UiStatusLevel.Working));
            RectTransform bar = lockBar.Fill.rectTransform.parent as RectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(14f, -46f);
            bar.offsetMax = new Vector2(-14f, -38f);

            targetRoot.gameObject.SetActive(false);
        }

        private void BuildMarkers(NebulaUiShell shell)
        {
            markerRoot = NebulaUiFactory.Group(shell.Root(UiLayer.HudDynamic), "Threat Markers");
            NebulaUiFactory.Fill(markerRoot);

            for (int i = 0; i < MaxMarkers; i++)
            {
                var marker = new Marker();
                // "Marker N", not "Threat N": the container is "Threat Markers", and
                // a prefix that matches both makes any count over the tree wrong.
                Image icon = NebulaUiFactory.Icon(markerRoot, "Marker " + i,
                    NebulaUiAssetCatalog.EdgeMarker, 22f);
                marker.Icon = icon;
                marker.Root = icon.rectTransform;
                NebulaUiFactory.Place(marker.Root, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(22f, 22f));
                icon.color = NebulaUiDesignTokens.Status(UiStatusLevel.Warning);

                marker.Distance = NebulaUiFactory.Label(marker.Root, "Distance", "",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.UpperCenter);
                NebulaUiFactory.Place(marker.Distance.Text.rectTransform, new Vector2(0.5f, 0f),
                    new Vector2(0f, -4f), new Vector2(80f, 14f));

                marker.Root.gameObject.SetActive(false);
                markers.Add(marker);
            }

            overflowLabel = NebulaUiFactory.Label(markerRoot, "Overflow", "",
                NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Warning),
                TextAnchor.MiddleCenter, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(overflowLabel.Text.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -164f), new Vector2(240f, 14f));
            overflowLabel.Text.gameObject.SetActive(false);
        }

        private void Update()
        {
            RefreshTarget();
            RefreshMarkers();
        }

        private void RefreshTarget()
        {
            EnemyAgent target = combat != null ? combat.Target : null;
            bool show = target != null && !target.Destroyed && ship != null;

            if (!show)
            {
                if (targetRoot.gameObject.activeSelf) targetRoot.gameObject.SetActive(false);
                lockMeter.AdoptSilently(0f);
                return;
            }

            if (!targetRoot.gameObject.activeSelf) targetRoot.gameObject.SetActive(true);

            targetName.Value = NebulaText.Upper(target.Def.DisplayName);
            float distance = Vector3.Distance(ship.transform.position, target.transform.position);
            targetDistance.Value = $"{distance:0} u";

            // The bar eases, the state it represents does not.
            float shown = lockMeter.Tick(combat.EmLockProgress01, Time.unscaledDeltaTime);
            lockBar.SetFraction(shown);
        }

        private void RefreshMarkers()
        {
            VisibleMarkers = 0;
            OverflowCount = 0;

            Camera cam = ship != null ? Camera.main : null;
            if (cam == null || combat == null)
            {
                HideAllMarkers();
                return;
            }

            // Cleared and refilled, never reallocated.
            candidates.Clear();
            foreach (KeyValuePair<string, EnemyAgent> entry in EnemyAgent.All)
            {
                EnemyAgent agent = entry.Value;
                if (agent == null || agent.Destroyed || agent == combat.Target) continue;
                candidates.Add(agent);
            }

            if (candidates.Count == 0) { HideAllMarkers(); return; }

            int drawn = 0;
            for (int i = 0; i < candidates.Count && drawn < MaxMarkers; i++)
            {
                EnemyAgent agent = candidates[i];
                Vector3 world = agent.transform.position;

                Vector3 local = cam.transform.InverseTransformDirection(
                    (world - cam.transform.position).normalized);
                float angle = Mathf.Atan2(local.x, local.y);
                if (local.z < 0f) angle += Mathf.PI;

                Marker marker = markers[drawn];
                marker.Root.anchoredPosition =
                    new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * MarkerRadius;
                marker.Root.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
                marker.Distance.Value =
                    $"{Vector3.Distance(ship.transform.position, world):0}u";
                if (!marker.Root.gameObject.activeSelf) marker.Root.gameObject.SetActive(true);
                drawn++;
            }

            for (int i = drawn; i < markers.Count; i++)
                if (markers[i].Root.gameObject.activeSelf)
                    markers[i].Root.gameObject.SetActive(false);

            VisibleMarkers = drawn;
            OverflowCount = Mathf.Max(0, candidates.Count - drawn);

            bool overflow = OverflowCount > 0;
            if (overflowLabel.Text.gameObject.activeSelf != overflow)
                overflowLabel.Text.gameObject.SetActive(overflow);
            if (overflow) overflowLabel.Value = $"+{OverflowCount} DAHA";
        }

        private void HideAllMarkers()
        {
            for (int i = 0; i < markers.Count; i++)
                if (markers[i].Root.gameObject.activeSelf)
                    markers[i].Root.gameObject.SetActive(false);
            if (overflowLabel != null && overflowLabel.Text.gameObject.activeSelf)
                overflowLabel.Text.gameObject.SetActive(false);
        }
    }
}
