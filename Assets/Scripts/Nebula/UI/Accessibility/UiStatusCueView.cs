using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.UI.Accessibility
{
    /// <summary>
    /// Draws a <see cref="UiStatusCue"/> as real geometry — one thin
    /// <see cref="Image"/> per stroke of <see cref="UiCueShapes"/>, plus the
    /// short marker beside it.
    /// <para>
    /// No sprite, no font glyph, no atlas entry: a rotated rect is the smallest
    /// thing that draws on every platform without an import setting, and it is
    /// the only way this mark can be regenerated from a clean checkout. The
    /// colour comes from the one status table, so a cue and the bar beside it can
    /// never disagree.
    /// </para>
    /// </summary>
    public static class UiStatusCueView
    {
        /// <summary>What a built mark exposes, so a caller can re-point it at a new reading.</summary>
        public sealed class Handle
        {
            public RectTransform Root { get; }
            public RectTransform Mark { get; }
            public TMPro.TextMeshProUGUI Marker { get; }
            public UiStatusCue Cue { get; private set; }

            private readonly List<Image> strokes = new List<Image>();
            private UiCueShape builtShape = UiCueShape.None;
            private float markSize;

            internal Handle(RectTransform root, RectTransform mark, TMPro.TextMeshProUGUI marker)
            {
                Root = root;
                Mark = mark;
                Marker = marker;
            }

            internal void Register(Image image) => strokes.Add(image);

            internal void RememberShape(UiCueShape shape, float size)
            {
                builtShape = shape;
                markSize = size;
            }

            /// <summary>How many strokes are actually on screen. Test-facing.</summary>
            public int StrokeCount => strokes.Count;

            internal void SetCue(UiStatusCue cue)
            {
                Cue = cue;

                // Strokes are geometry, not colour, so a new reading whose shape
                // differs has to be rebuilt rather than recoloured.
                //
                // This used to only recolour, which was invisible while every caller
                // was a legend built once per shape. The first live caller -- the HUD
                // warnings, whose cue is None until a raid starts -- got a mark that
                // had been built with nothing in it and could never acquire strokes,
                // so the setting appeared to do nothing. The class already promised
                // callers could "re-point it at a new reading"; now it can.
                UiCueShape wanted = cue.HasMark ? cue.Shape : UiCueShape.None;
                if (wanted != builtShape)
                {
                    foreach (Image image in strokes)
                        if (image != null) DestroyStroke(image.gameObject);
                    strokes.Clear();

                    foreach (UiCueStroke stroke in UiCueShapes.Strokes(wanted))
                        strokes.Add(Stroke(Mark, stroke, markSize));

                    builtShape = wanted;
                }

                Color color = cue.HasMark ? cue.Color : Color.clear;
                foreach (Image image in strokes) image.color = color;
                if (Marker != null)
                {
                    Marker.text = cue.Marker ?? string.Empty;
                    Marker.color = cue.HasMark ? cue.Color : Color.clear;
                }
            }

            /// <summary>
            /// <c>Destroy</c> is deferred and errors outside play mode, so an
            /// EditMode caller that re-points a mark would otherwise leak the old
            /// strokes into the next assertion about child counts.
            /// </summary>
            private static void DestroyStroke(GameObject go)
            {
                if (Application.isPlaying) Object.Destroy(go);
                else Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Builds the mark for a cue inside <paramref name="parent"/>, sized to
        /// <paramref name="markSize"/> square with the marker to its right.
        /// A cue with nothing to draw still builds — it simply draws nothing —
        /// so a panel's layout does not move when the player flips the switch.
        /// </summary>
        public static Handle Build(RectTransform parent, string name, UiStatusCue cue,
            float markSize = 20f, bool showMarker = true, float wordWidth = 132f)
        {
            var rootObject = new GameObject(name, typeof(RectTransform));
            var root = (RectTransform)rootObject.transform;
            root.SetParent(parent, false);

            var markObject = new GameObject("Mark", typeof(RectTransform));
            var mark = (RectTransform)markObject.transform;
            mark.SetParent(root, false);
            mark.anchorMin = mark.anchorMax = new Vector2(0f, 0.5f);
            mark.pivot = new Vector2(0f, 0.5f);
            mark.anchoredPosition = Vector2.zero;
            mark.sizeDelta = new Vector2(markSize, markSize);

            TMPro.TextMeshProUGUI marker = null;
            if (showMarker)
            {
                NebulaUiFactory.LabelHandle label = NebulaUiFactory.Label(root, "Marker",
                    cue.Marker ?? string.Empty, NebulaUiDesignTokens.SizeKeyPrompt,
                    NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
                    NebulaUiDesignTokens.TrackingCaption);
                marker = label.Text;
                RectTransform markerRect = marker.rectTransform;
                markerRect.anchorMin = markerRect.anchorMax = new Vector2(0f, 0.5f);
                markerRect.pivot = new Vector2(0f, 0.5f);
                markerRect.anchoredPosition = new Vector2(markSize + NebulaUiDesignTokens.S2, 0f);
                // Wide enough for the longest word in the vocabulary. A cue word
                // that ellipsizes to "BAĞLANTI…" has lost the channel it was added
                // for, so the rect is sized for the worst case rather than the
                // average one.
                markerRect.sizeDelta = new Vector2(wordWidth, markSize);
            }

            var handle = new Handle(root, mark, marker);

            // A cue with no mark builds no strokes, and the row keeps its size and
            // its position anyway — the mark sits in a fixed box beside the label
            // rather than in a layout the strokes contribute to. So turning cues
            // off removes the marks and moves nothing else, which is the whole
            // claim the setting makes.
            UiCueShape drawn = cue.HasMark ? cue.Shape : UiCueShape.None;
            foreach (UiCueStroke stroke in UiCueShapes.Strokes(drawn))
                handle.Register(Stroke(mark, stroke, markSize));
            handle.RememberShape(drawn, markSize);

            handle.SetCue(cue);
            return handle;
        }

        private static Image Stroke(RectTransform parent, UiCueStroke stroke, float boxSize)
        {
            var go = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(stroke.X * boxSize, stroke.Y * boxSize);
            rect.sizeDelta = new Vector2(
                Mathf.Max(1f, stroke.Length * boxSize),
                Mathf.Max(1f, stroke.Thickness * boxSize));
            rect.localRotation = Quaternion.Euler(0f, 0f, stroke.Degrees);

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }
    }
}
