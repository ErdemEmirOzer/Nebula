using System.Collections.Generic;
using UnityEngine;
using Nebula.Modules;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Accessibility
{
    /// <summary>
    /// The interface's status legend: every state a player has to tell apart,
    /// drawn once, from real readings.
    /// <para>
    /// It exists for two reasons and both are player-facing. It is somewhere the
    /// five marks can be learned before they are met in a fight, and it is the
    /// surface the Visual Cue switch is demonstrated on — flip the setting and
    /// this panel is the thing that visibly changes, which is the difference
    /// between a setting that reaches the interface and one that only reaches a
    /// static field.
    /// </para>
    /// <para>
    /// Every row's level is computed by <see cref="LightingUiPalette"/> from a
    /// value of the kind the game already produces. Nothing here invents a
    /// state, subscribes to anything, or holds a reference to a system: it is a
    /// second reading of numbers that already exist.
    /// </para>
    /// </summary>
    public static class UiStatusCueStrip
    {
        /// <summary>One line of the legend.</summary>
        public readonly struct Entry
        {
            public readonly string Label;
            public readonly string Reading;
            public readonly UiStatusCue Cue;

            public Entry(string label, string reading, UiStatusCue cue)
            {
                Label = label;
                Reading = reading;
                Cue = cue;
            }
        }

        /// <summary>What a built strip exposes, so a test or a settings preview can re-read it.</summary>
        public sealed class Handle
        {
            public RectTransform Root { get; }
            public IReadOnlyList<UiStatusCueView.Handle> Marks { get; }

            internal Handle(RectTransform root, IReadOnlyList<UiStatusCueView.Handle> marks)
            {
                Root = root;
                Marks = marks;
            }

            /// <summary>How many rows are currently showing a mark. Zero when cues are off.</summary>
            public int VisibleMarks
            {
                get
                {
                    int count = 0;
                    foreach (UiStatusCueView.Handle mark in Marks)
                        if (mark.Cue.HasMark) count++;
                    return count;
                }
            }
        }

        /// <summary>
        /// The five states the accessibility requirement names, each built from a
        /// reading of the kind the game already produces: a hull that is nearly
        /// gone, a hardpoint about to overheat, a shield that is fine, a relay
        /// that has stopped answering, and an upgrade that has been understood and
        /// refused.
        /// </summary>
        public static IReadOnlyList<Entry> RepresentativeStates()
        {
            return new[]
            {
                new Entry("KALKAN", "%95",
                    UiStatusCue.ForDurability(0.95f, 1f)),
                new Entry("ISI", "%80",
                    UiStatusCue.ForHeat(0.80f, 1f)),
                new Entry("GÖVDE", "%8",
                    UiStatusCue.ForDurability(0.08f, 1f)),
                new Entry("TELEMETRİ", "SİNYAL YOK",
                    UiStatusCue.ForCommunicationLink(live: false, hasLastKnown: false)),
                new Entry("SONDAJ T2", "KAYNAK YETERSİZ",
                    UiStatusCue.ForProgression(UpgradeRejectReason.InsufficientResources)),
            };
        }

        /// <summary>Builds the legend from <see cref="RepresentativeStates"/>.</summary>
        public static Handle Build(RectTransform parent, string name = "Status Cue Legend") =>
            Build(parent, RepresentativeStates(), name);

        /// <summary>
        /// Re-reads <see cref="RepresentativeStates"/> into a built legend.
        /// <para>
        /// The Visual Cue switch strips the mark channel from every cue
        /// (<c>UiStatusCue.Of</c>), and a legend built once keeps whatever it was
        /// built with. Without this the panel that exists to <i>show</i> the switch
        /// working showed nothing changing, which is the same defect as a switch that
        /// reaches no interface at all.
        /// </para>
        /// </summary>
        public static void Repaint(Handle handle)
        {
            if (handle == null) return;
            IReadOnlyList<Entry> entries = RepresentativeStates();
            for (int i = 0; i < handle.Marks.Count && i < entries.Count; i++)
                handle.Marks[i].SetCue(entries[i].Cue);
        }

        /// <summary>Builds the legend from any set of readings.</summary>
        public static Handle Build(RectTransform parent, IReadOnlyList<Entry> entries,
            string name = "Status Cue Legend")
        {
            var rootObject = new GameObject(name, typeof(RectTransform));
            var root = (RectTransform)rootObject.transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);

            float rowHeight = NebulaUiDesignTokens.RowHeight;
            float width = 470f;
            float height = NebulaUiDesignTokens.S8 + entries.Count * rowHeight + NebulaUiDesignTokens.S4;
            root.sizeDelta = new Vector2(width, height);
            root.anchoredPosition = Vector2.zero;

            UnityEngine.UI.Image backdrop =
                NebulaUiFactory.Rect(root, "Backdrop", NebulaUiDesignTokens.Surface1);
            NebulaUiFactory.Fill(backdrop.rectTransform);

            NebulaUiFactory.LabelHandle title = NebulaUiFactory.SectionTitle(
                root, "Title", "DURUM İŞARETLERİ");
            NebulaUiFactory.Place(title.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(NebulaUiDesignTokens.S3, -NebulaUiDesignTokens.S3),
                new Vector2(width - NebulaUiDesignTokens.S6, 18f));

            var marks = new List<UiStatusCueView.Handle>(entries.Count);
            float y = -NebulaUiDesignTokens.S8;

            foreach (Entry entry in entries)
            {
                var rowObject = new GameObject(entry.Label + " Row", typeof(RectTransform));
                var row = (RectTransform)rowObject.transform;
                row.SetParent(root, false);
                NebulaUiFactory.Place(row, new Vector2(0f, 1f),
                    new Vector2(NebulaUiDesignTokens.S3, y),
                    new Vector2(width - NebulaUiDesignTokens.S6, rowHeight));

                UiStatusCueView.Handle mark = UiStatusCueView.Build(row, "Cue", entry.Cue);
                mark.Root.anchorMin = mark.Root.anchorMax = new Vector2(0f, 0.5f);
                mark.Root.pivot = new Vector2(0f, 0.5f);
                mark.Root.anchoredPosition = Vector2.zero;
                mark.Root.sizeDelta = new Vector2(160f, rowHeight);
                marks.Add(mark);

                NebulaUiFactory.LabelHandle label = NebulaUiFactory.Label(row, "Label", entry.Label,
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                    TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
                NebulaUiFactory.Place(label.Text.rectTransform, new Vector2(0f, 0.5f),
                    new Vector2(172f, 0f), new Vector2(120f, rowHeight));

                // The reading is written in the status ink, not the mark colour:
                // StatusInk is the one that lifts Inactive to something readable.
                NebulaUiFactory.LabelHandle reading = NebulaUiFactory.Label(row, "Reading", entry.Reading,
                    NebulaUiDesignTokens.SizeCompact,
                    NebulaUiDesignTokens.StatusInk(entry.Cue.Level),
                    TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
                NebulaUiFactory.Place(reading.Text.rectTransform, new Vector2(1f, 0.5f),
                    new Vector2(-NebulaUiDesignTokens.S2, 0f), new Vector2(150f, rowHeight));

                y -= rowHeight;
            }

            return new Handle(root, marks);
        }
    }
}
