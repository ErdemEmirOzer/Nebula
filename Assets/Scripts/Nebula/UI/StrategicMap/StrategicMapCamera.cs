using UnityEngine;

namespace Nebula.UI.StrategicMap
{
    /// <summary>How much detail the current zoom level is allowed to draw.</summary>
    public enum StrategicZoomTier
    {
        /// <summary>Far out: sector clusters and dots only.</summary>
        Cluster,
        /// <summary>Mid: system dots with their state badges.</summary>
        Systems,
        /// <summary>Close: system names and per-system detail.</summary>
        Detail
    }

    /// <summary>
    /// Pure pan/zoom/momentum state for the strategic map — an Obsidian-graph-style
    /// feel (drag to pan, wheel to zoom toward the cursor, inertial glide) expressed
    /// as plain maths so it can be unit-tested with no scene.
    ///
    /// Map space is its own coordinate system, scaled down from canonical ABSOLUTE
    /// system positions. It is deliberately never mixed with the floating-origin
    /// world space the ship flies in: <see cref="WorldToMap"/> is the only bridge,
    /// and it takes absolute coordinates.
    ///
    /// Every public entry point clamps its own inputs, so a NaN or an absurd delta
    /// can never poison the view transform.
    /// </summary>
    public sealed class StrategicMapCamera
    {
        /// <summary>Canonical absolute units per map unit. One sector (2,000,000 u) becomes 200 map units.</summary>
        public const float WorldToMapScale = 1f / 10000f;

        public const float MinZoom = 0.25f;
        public const float MaxZoom = 6f;

        /// <summary>Below this the map shows clusters; above <see cref="DetailZoomThreshold"/> it shows full detail.</summary>
        public const float SystemsZoomThreshold = 0.7f;
        public const float DetailZoomThreshold = 1.8f;

        private const float MomentumDamping = 6.5f;
        private const float MomentumCutoff = 0.5f;

        /// <summary>Centre of the view, in map space.</summary>
        public Vector2 Center { get; private set; }
        public float Zoom { get; private set; } = 1f;
        public Vector2 Velocity { get; private set; }

        public StrategicZoomTier Tier =>
            Zoom < SystemsZoomThreshold ? StrategicZoomTier.Cluster :
            Zoom < DetailZoomThreshold ? StrategicZoomTier.Systems : StrategicZoomTier.Detail;

        private bool focusing;
        private Vector2 focusFrom, focusTo;
        private float focusFromZoom, focusToZoom, focusElapsed, focusDuration;

        /// <summary>True while a focus animation is playing — the view should not fight it with momentum.</summary>
        public bool IsFocusing => focusing;

        public void Reset()
        {
            Center = Vector2.zero;
            Zoom = 1f;
            Velocity = Vector2.zero;
            focusing = false;
        }

        /// <summary>Projects a canonical ABSOLUTE world position into map space. Never takes a floating-origin-relative position.</summary>
        public static Vector2 WorldToMap(Vector3 absolutePosition) =>
            new Vector2(absolutePosition.x, absolutePosition.z) * WorldToMapScale;

        /// <summary>Map space → screen offset from the view centre, for a given pixel scale.</summary>
        public Vector2 MapToScreen(Vector2 mapPosition, float pixelsPerMapUnit) =>
            (mapPosition - Center) * (Zoom * pixelsPerMapUnit);

        /// <summary>Screen offset (from the view centre) → map space.</summary>
        public Vector2 ScreenToMap(Vector2 screenOffset, float pixelsPerMapUnit)
        {
            float denominator = Zoom * pixelsPerMapUnit;
            if (Mathf.Abs(denominator) < 0.00001f) return Center;
            return Center + screenOffset / denominator;
        }

        /// <summary>Drag-pan by a screen delta. Cancels any running focus animation — the player's own input always wins.</summary>
        public void Pan(Vector2 screenDelta, float pixelsPerMapUnit)
        {
            if (!IsFinite(screenDelta)) return;
            focusing = false;
            float denominator = Mathf.Max(0.00001f, Zoom * pixelsPerMapUnit);
            Center -= screenDelta / denominator;
            ClampState();
        }

        /// <summary>Releases a drag with the given screen-space velocity, starting the inertial glide.</summary>
        public void ReleaseWithMomentum(Vector2 screenVelocity, float pixelsPerMapUnit)
        {
            if (!IsFinite(screenVelocity)) { Velocity = Vector2.zero; return; }
            float denominator = Mathf.Max(0.00001f, Zoom * pixelsPerMapUnit);
            Velocity = -screenVelocity / denominator;
        }

        /// <summary>
        /// Wheel zoom that keeps the point under the cursor anchored, so zooming feels
        /// like it happens where the player is looking rather than at the screen centre.
        /// </summary>
        public void ZoomBy(float wheelDelta, Vector2 cursorScreenOffset, float pixelsPerMapUnit)
        {
            if (!IsFinite(wheelDelta)) return;
            focusing = false;

            Vector2 anchorBefore = ScreenToMap(cursorScreenOffset, pixelsPerMapUnit);
            Zoom = Mathf.Clamp(Zoom * Mathf.Exp(wheelDelta * 0.25f), MinZoom, MaxZoom);
            Vector2 anchorAfter = ScreenToMap(cursorScreenOffset, pixelsPerMapUnit);

            Center += anchorBefore - anchorAfter;
            ClampState();
        }

        /// <summary>Starts a smooth, eased move to a target — the "click a system and glide to it" transition.</summary>
        public void FocusOn(Vector2 mapTarget, float targetZoom, float durationSeconds = StrategicMapTheme.SlowTransitionSeconds)
        {
            if (!IsFinite(mapTarget) || !IsFinite(targetZoom)) return;
            focusFrom = Center;
            focusTo = mapTarget;
            focusFromZoom = Zoom;
            focusToZoom = Mathf.Clamp(targetZoom, MinZoom, MaxZoom);
            focusElapsed = 0f;
            focusDuration = Mathf.Max(0.0001f, durationSeconds);
            focusing = true;
            Velocity = Vector2.zero;
        }

        /// <summary>Advances momentum and any running focus animation. Uses unscaled time — the map must feel identical whatever the simulation is doing.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!IsFinite(unscaledDeltaTime) || unscaledDeltaTime <= 0f) return;
            float dt = Mathf.Min(unscaledDeltaTime, 0.1f); // a long frame must not teleport the view

            if (focusing)
            {
                focusElapsed += dt;
                float t = Mathf.Clamp01(focusElapsed / focusDuration);
                float eased = StrategicMapTheme.EaseFocus(t);
                Center = Vector2.LerpUnclamped(focusFrom, focusTo, eased);
                Zoom = Mathf.Clamp(Mathf.LerpUnclamped(focusFromZoom, focusToZoom, eased), MinZoom, MaxZoom);
                if (t >= 1f) { focusing = false; Center = focusTo; Zoom = focusToZoom; }
                ClampState();
                return;
            }

            if (Velocity.sqrMagnitude <= MomentumCutoff * MomentumCutoff) { Velocity = Vector2.zero; return; }
            Center += Velocity * dt;
            Velocity *= Mathf.Exp(-MomentumDamping * dt);
            ClampState();
        }

        /// <summary>Belt-and-braces: the view transform must never be allowed to become NaN or infinite.</summary>
        private void ClampState()
        {
            if (!IsFinite(Center)) Center = Vector2.zero;
            if (!IsFinite(Zoom)) Zoom = 1f;
            if (!IsFinite(Velocity)) Velocity = Vector2.zero;
            Zoom = Mathf.Clamp(Zoom, MinZoom, MaxZoom);
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool IsFinite(Vector2 v) => IsFinite(v.x) && IsFinite(v.y);
    }
}
