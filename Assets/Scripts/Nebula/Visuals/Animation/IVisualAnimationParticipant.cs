using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Everything the animation director needs from something it animates.
    /// <para>
    /// Deliberately narrow, and deliberately visual-only: a participant is
    /// handed a delta and asked to pose itself. It is never given, and can
    /// never reach, the gameplay clock, the physics step, or any authority
    /// over what the ship is actually doing.
    /// </para>
    /// </summary>
    public interface IVisualAnimationParticipant
    {
        /// <summary>
        /// False once the thing being animated has gone away. The director uses
        /// this to drop dead entries without needing to know what they were —
        /// a destroyed <see cref="Object"/> reports false through Unity's own
        /// null semantics.
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// Advance the visual by <paramref name="deltaSeconds"/>. Called at the
        /// profile's tick rate, never faster.
        /// </summary>
        void VisualTick(float deltaSeconds);

        /// <summary>
        /// Put everything back exactly where it started and stop moving.
        /// Called when animation is switched off, when the participant is
        /// unregistered, and on session teardown — so a world without
        /// animation is a world in its authored pose, not a world frozen
        /// mid-wobble.
        /// </summary>
        void ResetToBasePose();
    }

    /// <summary>
    /// A registration receipt. Holding one is the only way to unregister, which
    /// is what stops a participant being removed twice or removed by something
    /// that never registered it.
    /// </summary>
    public readonly struct AnimationRegistration
    {
        private readonly NebulaAnimationDirector director;
        private readonly IVisualAnimationParticipant participant;

        internal AnimationRegistration(NebulaAnimationDirector owner, IVisualAnimationParticipant target)
        {
            director = owner;
            participant = target;
        }

        public bool IsValid => director != null && participant != null;

        /// <summary>Idempotent: releasing an already-released or never-valid receipt does nothing.</summary>
        public void Release()
        {
            if (director != null && participant != null) director.Unregister(participant);
        }
    }
}
