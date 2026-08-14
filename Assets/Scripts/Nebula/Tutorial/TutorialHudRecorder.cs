using System.Collections.Generic;

namespace Nebula.Tutorial
{
    /// <summary>
    /// A recording <see cref="ITutorialHud"/>.
    /// <para>
    /// The director has to be provable in EditMode -- that a step really was
    /// shown before its validator attached, that the counter really was the
    /// global one in a standalone run, that the summary really came instead of
    /// step 28 -- and none of that should need a canvas, a font atlas or a frame
    /// loop. This records what the director said and asserts nothing itself.
    /// </para>
    /// <para>
    /// It is also the director's default HUD, so a tutorial scene that loads
    /// before the real card exists runs correctly and silently rather than
    /// null-dereferencing on the first step.
    /// </para>
    /// </summary>
    public sealed class TutorialHudRecorder : ITutorialHud
    {
        public readonly struct ShownStep
        {
            public string StepId { get; }
            public int GlobalNumber { get; }
            public int Total { get; }

            public ShownStep(string stepId, int globalNumber, int total)
            {
                StepId = stepId;
                GlobalNumber = globalNumber;
                Total = total;
            }

            public override string ToString() => $"{StepId} {GlobalNumber}/{Total}";
        }

        public readonly struct Summary
        {
            public TutorialRunMode Mode { get; }
            public int CompletedCount { get; }
            public int Total { get; }

            public Summary(TutorialRunMode mode, int completedCount, int total)
            {
                Mode = mode;
                CompletedCount = completedCount;
                Total = total;
            }
        }

        private readonly List<ShownStep> shown = new List<ShownStep>();
        private readonly List<string> completed = new List<string>();
        private readonly List<Summary> summaries = new List<Summary>();

        /// <summary>Every step the director put on screen, in order.</summary>
        public IReadOnlyList<ShownStep> Shown => shown;

        /// <summary>Every step that reached its success beat, in order.</summary>
        public IReadOnlyList<string> Completed => completed;

        /// <summary>Every summary shown. More than one would itself be a bug.</summary>
        public IReadOnlyList<Summary> Summaries => summaries;

        /// <summary>How many times the report was pushed. A step that never polls is a step that never asked the player for anything.</summary>
        public int ReportUpdates { get; private set; }

        /// <summary>The last feedback line and kind pushed -- the corrective-feedback assertion.</summary>
        public string LastFeedback { get; private set; }

        public TutorialFeedbackKind LastFeedbackKind { get; private set; }

        public int ClearCount { get; private set; }

        public void ShowStep(TutorialStepDefinition step, int total)
        {
            if (step == null) return;
            // Cleared per step, so an acknowledgement can never carry across and
            // complete the following step as well.
            AcknowledgedCurrentStep = false;
            shown.Add(new ShownStep(step.StepId, step.GlobalNumber, total));
        }

        public void UpdateReport(TutorialStepReport report)
        {
            ReportUpdates++;
            if (report == null) return;
            LastFeedback = report.Feedback;
            LastFeedbackKind = report.FeedbackKind;
        }

        public void ShowStepComplete(TutorialStepDefinition step)
        {
            if (step != null) completed.Add(step.StepId);
        }

        public void ShowSummary(TutorialRunMode mode, int completedCount, int total) =>
            summaries.Add(new Summary(mode, completedCount, total));

        public void Clear() => ClearCount++;

        /// <summary>
        /// Recording double for the acknowledgement gate. False until a test calls
        /// <see cref="Acknowledge"/>, so a director that completed an information
        /// step without one fails here rather than in a canvas.
        /// </summary>
        public bool AcknowledgedCurrentStep { get; private set; }

        /// <summary>Stands in for the player pressing the confirmation control.</summary>
        public void Acknowledge() => AcknowledgedCurrentStep = true;

        /// <summary>Clears the acknowledgement. Called when a new step is shown.</summary>
        public void ClearAcknowledgement() => AcknowledgedCurrentStep = false;

        public void Reset()
        {
            shown.Clear();
            completed.Clear();
            summaries.Clear();
            ReportUpdates = 0;
            ClearCount = 0;
            LastFeedback = null;
            LastFeedbackKind = TutorialFeedbackKind.None;
        }
    }
}
