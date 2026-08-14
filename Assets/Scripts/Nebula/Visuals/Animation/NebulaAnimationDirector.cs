using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// The single owner of visual animation for a campaign session. Created
    /// once by <c>AnimationInstaller</c> and reached only through
    /// <c>NebulaContext</c> — it never searches the scene and is never a
    /// second composition root.
    /// <para>
    /// It ticks its participants at the quality profile's rate and does
    /// nothing else. It owns no gameplay state, never writes
    /// <see cref="Time.timeScale"/>, and drives no Rigidbody, collider, camera
    /// or gameplay transform. When the game is paused the world's delta is
    /// zero and animation stops with it — the director does not need to know
    /// what pause means.
    /// </para>
    /// </summary>
    public class NebulaAnimationDirector : MonoBehaviour
    {
        private readonly List<IVisualAnimationParticipant> participants = new List<IVisualAnimationParticipant>();
        private readonly List<IVisualAnimationParticipant> pendingAdd = new List<IVisualAnimationParticipant>();
        private readonly List<IVisualAnimationParticipant> pendingRemove = new List<IVisualAnimationParticipant>();

        /// <summary>
        /// ANI-05 §8: participants that must keep moving while the world is
        /// paused — panels, cards, the map. They are ticked from unscaled time
        /// in the same director; a second director would be a second
        /// composition root, which TF-002 forbids.
        /// </summary>
        private readonly List<IVisualAnimationParticipant> unscaled = new List<IVisualAnimationParticipant>();

        private AnimationQualityProfile profile = AnimationQualityProfile.Medium;
        private float accumulator;
        private bool ticking;

        // Seeded at construction, so a director built while Motion Off is already on
        // does not spend its first tick "settling" something that was never moving.
        // Only a change made after this director existed forces a pose pass.
        private int observedSettingsVersion = AnimationAccessibility.SettingsVersion;

        public AnimationQualityProfile Profile => profile;

        /// <summary>Live participants. Pending additions are not counted until they are actually in.</summary>
        public int ParticipantCount => participants.Count;

        /// <summary>Live UI participants, ticked from unscaled time.</summary>
        public int UnscaledParticipantCount => unscaled.Count;

        /// <summary>Visual ticks issued this session. Test-facing.</summary>
        public long TickCount { get; private set; }

        /// <summary>
        /// Clears any static animation state left by a previous session.
        /// Called at session START, by the installer — never from a teardown:
        /// Unity defers Destroy to end of frame, so an outgoing director doing
        /// this in OnDestroy would wipe the session that had already replaced
        /// it. (LGT-05.1 found exactly that bug in the lighting stack.)
        /// </summary>
        public static void ResetSessionState()
        {
            // AnimationAccessibility is deliberately NOT reset here. "Hareketi Azalt",
            // "Hareket Kapalı" and "Kamera Konforu" are player preferences, not session
            // state: a player who turns motion down at the main menu had it silently
            // turned back on the moment they pressed New Campaign. The presenter
            // ResetPending() calls below really are session state and stay.
            StructurePosePresenter.ResetPending();
            BuildCapsulePresenter.ResetPending();
            VehicleAnimationPresenter.ResetPending();
            SupportOperationPresenter.ResetPending();
            EnemyCombatAnimationPresenter.ResetPending();
            CombatPresenterAdoption.ResetPending();
            EscortFireSignal.ClearSubscribers();
        }

        /// <summary>
        /// The session's delivery-capsule proxies. Built once by the installer;
        /// every capsule in flight draws from here, so a hundred build orders
        /// cost the scene the same handful of objects as one.
        /// </summary>
        public CapsuleProxyPool CapsuleProxies { get; private set; }

        public void BindCapsuleProxies(CapsuleProxyPool pool) => CapsuleProxies = pool;

        /// <summary>
        /// ANI-02.2: pooled item stand-ins for processing input and output.
        /// Kept separate from the capsule pool on purpose — the two hold
        /// different things and must never be able to lease each other's.
        /// </summary>
        public ItemProxyPool ItemProxies { get; private set; }

        public void BindItemProxies(ItemProxyPool pool) => ItemProxies = pool;

        /// <summary>
        /// The session's operation-VFX quads (ANI-03.1). Bounded: capacity ×
        /// the seven shared sprites, no matter how many vehicles are working.
        /// </summary>
        public SupportOperationPool SupportOperations { get; private set; }

        public void BindSupportOperations(SupportOperationPool pool) => SupportOperations = pool;

        public void Initialize(in AnimationQualityProfile startingProfile)
        {
            profile = startingProfile;
            accumulator = 0f;
        }

        /// <summary>Runtime quality switch. Visual only — never written to a save, never read by simulation.</summary>
        public void SetQuality(AnimationQuality quality) => ApplyProfile(AnimationQualityProfile.Resolve(quality));

        /// <summary>
        /// Changes the tick budget without disturbing anything already posed:
        /// no participant is added, removed or re-based, so a quality switch
        /// cannot shift the picture.
        /// </summary>
        public void ApplyProfile(in AnimationQualityProfile newProfile)
        {
            profile = newProfile;
            // Deliberately not resetting the accumulator: dropping accumulated
            // time here would stutter the next tick for no benefit.
        }

        /// <summary>
        /// Registers something to be animated and hands back the only receipt
        /// that can unregister it. Registering the same participant twice is a
        /// no-op that still returns a usable receipt.
        /// </summary>
        public AnimationRegistration Register(IVisualAnimationParticipant participant)
        {
            if (participant == null) return default;

            if (ticking)
            {
                // Mid-tick: queue it. Mutating the list we are walking is the
                // classic way this kind of director corrupts itself.
                if (!pendingAdd.Contains(participant)) pendingAdd.Add(participant);
                pendingRemove.Remove(participant);
            }
            else if (!participants.Contains(participant))
            {
                participants.Add(participant);
            }

            return new AnimationRegistration(this, participant);
        }

        /// <summary>
        /// Drops a participant and puts it back to its authored pose, so
        /// nothing is ever left frozen mid-animation. Safe to call twice, and
        /// safe to call from inside a tick.
        /// </summary>
        public void Unregister(IVisualAnimationParticipant participant)
        {
            if (participant == null) return;

            if (ticking)
            {
                if (!pendingRemove.Contains(participant)) pendingRemove.Add(participant);
                pendingAdd.Remove(participant);
            }
            else
            {
                participants.Remove(participant);
                pendingAdd.Remove(participant);
            }

            unscaled.Remove(participant);

            if (participant.IsAlive) participant.ResetToBasePose();
        }

        private void LateUpdate()
        {
            // Adoption is not part of the throttled pose tick, and used to be. It now
            // happens at the top of Tick itself, ahead of every early return there --
            // the shell freezes the world for as long as a menu is open, so a vehicle
            // queued from the Fleet page must not be left waiting until the player
            // closes the menu again. A presenter gameplay queued joins on the next
            // frame, full stop.

            // World animation stops dead when the game is paused, because a
            // paused world has a delta of zero. UI does not.
            Tick(Time.deltaTime);
            TickUnscaled(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Presenters built by gameplay code that had no route to the session
        /// context queue themselves up; this is where they join.
        /// </summary>
        private void AdoptPendingPresenters()
        {
            StructurePosePresenter.AdoptPending(this);
            BuildCapsulePresenter.AdoptPending(this);
            VehicleAnimationPresenter.AdoptPending(this);
            SupportOperationPresenter.AdoptPending(this);
            EnemyCombatAnimationPresenter.AdoptPending(this);
            CombatPresenterAdoption.AdoptPending(this);
            FlushPending();
        }

        /// <summary>
        /// Registers a participant on the unscaled clock. Returns the same kind
        /// of receipt as <see cref="Register"/>, and unregisters the same way.
        /// </summary>
        public AnimationRegistration RegisterUnscaled(IVisualAnimationParticipant participant)
        {
            if (participant == null) return default;
            if (!unscaled.Contains(participant)) unscaled.Add(participant);
            return new AnimationRegistration(this, participant);
        }

        /// <summary>
        /// Ticks the UI domain. Unrated on purpose: a panel that opens during a
        /// pause must not wait on the world's tick budget. Exposed so a test can
        /// step it deterministically.
        /// </summary>
        public void TickUnscaled(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || unscaled.Count == 0) return;

            for (int i = unscaled.Count - 1; i >= 0; i--)
            {
                IVisualAnimationParticipant participant = unscaled[i];
                if (participant == null || !participant.IsAlive) { unscaled.RemoveAt(i); continue; }
                participant.VisualTick(deltaSeconds);
            }
            UnscaledTickCount++;
        }

        /// <summary>Unscaled ticks issued this session. Test-facing.</summary>
        public long UnscaledTickCount { get; private set; }

        /// <summary>
        /// Accumulates world time and issues visual ticks at the profile's
        /// rate. Exposed so a test can step it deterministically instead of
        /// waiting on frames.
        /// <para>
        /// A delta of zero — which is what a paused game produces — advances
        /// nothing at all.
        /// </para>
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            // Before every guard below, not after them. A fixture that drives the
            // director by hand never reaches a LateUpdate, so this is the only place
            // its queued presenters can join -- and Tick returns early on three
            // counts (zero delta, no interval, accumulator short of the interval).
            // Ani043's "100 projectiles" loop spawns one presenter and calls
            // Tick(0.02f) once per iteration; at the Medium profile's 25 ms interval
            // every second call returned at the accumulator guard, so the last
            // projectile's presenter was still in the queue when the test asserted
            // "every presenter must have been adopted, none left waiting".
            // Adoption costs nothing when the queues are empty and poses nobody.
            AdoptPendingPresenters();

            if (deltaSeconds <= 0f) return;

            float interval = profile.TickInterval;
            if (interval <= 0f) return;

            accumulator += deltaSeconds;

            // A change to Motion Off / Reduced Motion / Camera Comfort is reposed on
            // the very next tick rather than whenever the throttle next comes round.
            // The player asked for the picture to stop moving; making them wait up to
            // a tick interval for it is the wrong answer, and it is also what made
            // "MotionOff = true; director.Tick(0.02f);" assert against a presenter
            // that had never been ticked at all (see AnimationAccessibility.SettingsVersion).
            // Each presenter decides what its own settled pose is -- this is a normal
            // pose pass, not a blanket ResetAllToBasePose, so a bench mid-conversion
            // lands on Complete rather than on nothing.
            bool settleForSettingsChange = observedSettingsVersion != AnimationAccessibility.SettingsVersion;
            if (!settleForSettingsChange && accumulator < interval) return;
            observedSettingsVersion = AnimationAccessibility.SettingsVersion;

            // One pose per frame however much time built up: catching up by
            // ticking repeatedly would make a frame hitch look like a spasm.
            float step = accumulator;
            accumulator = 0f;

            TickParticipants(step);
        }

        private void TickParticipants(float step)
        {
            ticking = true;
            try
            {
                for (int i = 0; i < participants.Count; i++)
                {
                    IVisualAnimationParticipant participant = participants[i];
                    if (participant == null || !participant.IsAlive)
                    {
                        // Whatever this was has been destroyed. Drop it after
                        // the walk rather than compacting the list underfoot.
                        if (!pendingRemove.Contains(participant)) pendingRemove.Add(participant);
                        continue;
                    }

                    participant.VisualTick(step);
                }
            }
            finally
            {
                ticking = false;
                TickCount++;
                FlushPending();
            }
        }

        private void FlushPending()
        {
            for (int i = 0; i < pendingRemove.Count; i++) participants.Remove(pendingRemove[i]);
            pendingRemove.Clear();

            for (int i = 0; i < pendingAdd.Count; i++)
                if (!participants.Contains(pendingAdd[i])) participants.Add(pendingAdd[i]);
            pendingAdd.Clear();
        }

        /// <summary>Puts every live participant back to its authored pose without unregistering anything.</summary>
        public void ResetAllToBasePose()
        {
            for (int i = 0; i < participants.Count; i++)
                if (participants[i] != null && participants[i].IsAlive) participants[i].ResetToBasePose();
        }

        private void OnDestroy()
        {
            // Reset what we still own, then drop our own lists. No static state
            // is touched here — see ResetSessionState for why.
            ResetAllToBasePose();
            participants.Clear();
            pendingAdd.Clear();
            pendingRemove.Clear();
        }
    }
}
