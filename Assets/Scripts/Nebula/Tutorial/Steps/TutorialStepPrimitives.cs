using System;
using System.Reflection;
using UnityEngine;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Runs the <c>Awake</c> Unity skips outside play mode.
    /// <para>
    /// Unity only calls <c>Awake</c> on a runtime-added component when the player
    /// loop is running. A ship assembled in an EditMode test therefore comes back
    /// with every field its <c>Awake</c> would have set still null, and the first
    /// <c>InitializeNewCampaign</c> throws — which is what stood between four
    /// hundred step assertions and running in milliseconds instead of minutes.
    /// </para>
    /// <para>
    /// A no-op in play mode, so the real game never double-initialises: there,
    /// Unity has already done this and doing it again is the bug this guards
    /// against, not the fix.
    /// </para>
    /// </summary>
    public static class TutorialAwake
    {
        /// <summary>Wakes every behaviour on a host that Unity has not woken.</summary>
        public static GameObject WakeAll(GameObject host)
        {
            if (host == null || Application.isPlaying) return host;

            foreach (MonoBehaviour behaviour in host.GetComponents<MonoBehaviour>())
                Wake(behaviour);
            return host;
        }

        /// <summary>Wakes one behaviour. Returns it, so it can wrap an <c>AddComponent</c>.</summary>
        public static T Wake<T>(T behaviour) where T : MonoBehaviour
        {
            if (behaviour == null || Application.isPlaying) return behaviour;

            // Reflection rather than SendMessage. SendMessage routes through Unity's
            // messaging, which asserts ShouldRunBehaviour() on a component the
            // player loop is not running -- exactly our case -- and that assertion
            // reaches the test runner as an unhandled log message and fails the
            // test it was meant to enable.
            for (Type type = behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                MethodInfo awake = type.GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (awake == null) continue;
                awake.Invoke(behaviour, null);
                return behaviour;
            }
            return behaviour;
        }
    }

    /// <summary>
    /// Shared shapes for step runtimes, so a hundred and seventy-five steps are not
    /// a hundred and seventy-five bespoke classes.
    /// <para>
    /// Every primitive here enforces the same rule from the outside: the success
    /// test is a delegate that reads gameplay, and it is only ever consulted during
    /// <see cref="ITutorialStepRuntime.Evaluate"/>. None of them can be satisfied by
    /// an input read, an elapsed timer or a piece of displayed text, because none of
    /// them is given access to one.
    /// </para>
    /// </summary>
    public static class TutorialSteps
    {
        /// <summary>Reads gameplay and answers "has the player done it yet".</summary>
        public delegate bool Probe(TutorialStepContext context);

        /// <summary>Reads gameplay and answers "what is wrong right now", or null when nothing is.</summary>
        public delegate string Complaint(TutorialStepContext context);

        /// <summary>Fills the checklist from live state.</summary>
        public delegate void Objectives(TutorialStepContext context, TutorialStepReport report);

        /// <summary>
        /// The general case: prepare, then poll a real predicate every frame.
        /// <para>
        /// Suits most steps, including observation steps — one where the player
        /// watches a capsule land still completes on the landing having happened,
        /// not on a countdown the tutorial runs.
        /// </para>
        /// </summary>
        public static ITutorialStepRuntime Poll(
            Probe complete,
            Action<TutorialStepContext> prepare = null,
            Complaint reject = null,
            Objectives objectives = null,
            Action<TutorialStepContext> cleanup = null) =>
            new PollStep(complete, prepare, reject, objectives, cleanup);

        /// <summary>
        /// Completes on a C# event rather than a polled value, for things that are
        /// true only for an instant — a shot fired, a job finished, damage applied.
        /// <para>
        /// <paramref name="subscribe"/> is handed a callback to invoke and must
        /// return the matching unsubscribe. The unsubscribe is registered with the
        /// step scope immediately, so it runs even if the step is abandoned or
        /// throws — an event left attached to a live production service is the one
        /// leak that outlives the tutorial scene.
        /// </para>
        /// </summary>
        public static ITutorialStepRuntime OnEvent(
            Func<TutorialStepContext, Action, Action> subscribe,
            Action<TutorialStepContext> prepare = null,
            Complaint reject = null,
            Objectives objectives = null) =>
            new EventStep(subscribe, prepare, reject, objectives);

        /// <summary>
        /// Completes when a value the step captured at prepare time has changed by
        /// at least <paramref name="minimumDelta"/>. For "turn the ship", "spend the
        /// resource", "take the damage" — where the absolute number is a property of
        /// the save the player happens to be in, and only the movement is the lesson.
        /// </summary>
        public static ITutorialStepRuntime Delta(
            Func<TutorialStepContext, float> read,
            float minimumDelta,
            Action<TutorialStepContext> prepare = null,
            Complaint reject = null) =>
            new DeltaStep(read, minimumDelta, prepare, reject);

        private sealed class PollStep : ITutorialStepRuntime
        {
            private readonly Probe complete;
            private readonly Action<TutorialStepContext> prepare, cleanup;
            private readonly Complaint reject;
            private readonly Objectives objectives;
            private TutorialStepContext context;

            internal PollStep(Probe complete, Action<TutorialStepContext> prepare,
                Complaint reject, Objectives objectives, Action<TutorialStepContext> cleanup)
            {
                this.complete = complete ?? throw new ArgumentNullException(nameof(complete));
                this.prepare = prepare;
                this.reject = reject;
                this.objectives = objectives;
                this.cleanup = cleanup;
            }

            public void Prepare(TutorialStepContext ctx)
            {
                context = ctx;
                prepare?.Invoke(ctx);
            }

            public void Evaluate(TutorialStepReport report)
            {
                if (context == null) return;
                objectives?.Invoke(context, report);
                if (complete(context)) { report.Complete(); return; }
                string problem = reject?.Invoke(context);
                if (!string.IsNullOrEmpty(problem)) report.Reject(problem);
            }

            public void Cleanup()
            {
                if (context != null) cleanup?.Invoke(context);
                context = null;
            }
        }

        private sealed class EventStep : ITutorialStepRuntime
        {
            private readonly Func<TutorialStepContext, Action, Action> subscribe;
            private readonly Action<TutorialStepContext> prepare;
            private readonly Complaint reject;
            private readonly Objectives objectives;
            private TutorialStepContext context;
            private bool fired;

            internal EventStep(Func<TutorialStepContext, Action, Action> subscribe,
                Action<TutorialStepContext> prepare, Complaint reject, Objectives objectives)
            {
                this.subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
                this.prepare = prepare;
                this.reject = reject;
                this.objectives = objectives;
            }

            public void Prepare(TutorialStepContext ctx)
            {
                context = ctx;
                fired = false;
                prepare?.Invoke(ctx);

                // Subscribed after prepare, so prerequisite installation cannot trip
                // the very event the step is waiting for and hand the player a step
                // they never performed.
                Action release = subscribe(ctx, () => fired = true);
                if (release != null) ctx.Scope.OnCleanup(release);
            }

            public void Evaluate(TutorialStepReport report)
            {
                if (context == null) return;
                objectives?.Invoke(context, report);
                if (fired) { report.Complete(); return; }
                string problem = reject?.Invoke(context);
                if (!string.IsNullOrEmpty(problem)) report.Reject(problem);
            }

            public void Cleanup() => context = null;
        }

        private sealed class DeltaStep : ITutorialStepRuntime
        {
            private readonly Func<TutorialStepContext, float> read;
            private readonly float minimumDelta;
            private readonly Action<TutorialStepContext> prepare;
            private readonly Complaint reject;
            private TutorialStepContext context;
            private float baseline;

            internal DeltaStep(Func<TutorialStepContext, float> read, float minimumDelta,
                Action<TutorialStepContext> prepare, Complaint reject)
            {
                this.read = read ?? throw new ArgumentNullException(nameof(read));
                this.minimumDelta = Mathf.Abs(minimumDelta);
                this.prepare = prepare;
                this.reject = reject;
            }

            public void Prepare(TutorialStepContext ctx)
            {
                context = ctx;
                prepare?.Invoke(ctx);
                // Baseline taken after prepare: prerequisites move these values, and
                // measuring from before them would credit the player with the setup.
                baseline = read(ctx);
            }

            public void Evaluate(TutorialStepReport report)
            {
                if (context == null) return;
                if (Mathf.Abs(read(context) - baseline) >= minimumDelta) { report.Complete(); return; }
                string problem = reject?.Invoke(context);
                if (!string.IsNullOrEmpty(problem)) report.Reject(problem);
            }

            public void Cleanup() => context = null;
        }
    }
}
