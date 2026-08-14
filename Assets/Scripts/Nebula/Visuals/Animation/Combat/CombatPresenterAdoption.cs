using System.Collections.Generic;

namespace Nebula.Visuals
{
    /// <summary>
    /// A participant that gameplay code creates without a route to the session
    /// context, and that the director adopts on its next tick.
    /// </summary>
    public interface IAdoptableParticipant : IVisualAnimationParticipant
    {
        void AttachTo(NebulaAnimationDirector director);
    }

    /// <summary>
    /// The one waiting room for combat presenters.
    /// <para>
    /// A projectile, a turret, a Pirate Base and a wreck are all built deep
    /// inside gameplay code that has no business knowing about the animation
    /// director. They queue here; the director drains the queue at the top of
    /// its tick. This is a dispatch list, not a service locator: nothing reads
    /// anything out of it.
    /// </para>
    /// </summary>
    public static class CombatPresenterAdoption
    {
        private static readonly List<IAdoptableParticipant> Waiting = new List<IAdoptableParticipant>();

        public static void Enqueue(IAdoptableParticipant participant)
        {
            if (participant == null || Waiting.Contains(participant)) return;
            Waiting.Add(participant);
        }

        public static void Remove(IAdoptableParticipant participant) => Waiting.Remove(participant);

        internal static void AdoptPending(NebulaAnimationDirector director)
        {
            if (director == null || Waiting.Count == 0) return;

            for (int i = 0; i < Waiting.Count; i++)
            {
                IAdoptableParticipant participant = Waiting[i];
                if (participant != null && participant.IsAlive) participant.AttachTo(director);
            }
            Waiting.Clear();
        }

        /// <summary>
        /// Cleared at session START by the installer — never in an outgoing
        /// director's teardown, which Unity defers past the new session's build.
        /// </summary>
        public static void ResetPending() => Waiting.Clear();

        /// <summary>Presenters genuinely still waiting; destroyed ones are not counted.</summary>
        public static int PendingCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < Waiting.Count; i++)
                    if (Waiting[i] != null && Waiting[i].IsAlive) live++;
                return live;
            }
        }
    }
}
