using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;

namespace Nebula.Visuals
{
    /// <summary>
    /// Poses one enemy's visual child and its real mechanical parts from the
    /// phase its own AI is really in.
    /// <para>
    /// <see cref="EnemyAgent"/> and <see cref="EnemyAiResolver"/> remain the only
    /// authorities. This never advances a phase, a scan, a telegraph, a disrupt
    /// or a cargo theft; it never moves the agent root, its collider or its
    /// aim, and it never destroys or unregisters an enemy. A disrupted enemy is
    /// disrupted for exactly as long as the resolver says.
    /// </para>
    /// </summary>
    public class EnemyCombatAnimationPresenter : MonoBehaviour, IVisualAnimationParticipant
    {
        private static readonly List<EnemyCombatAnimationPresenter> AwaitingAdoption =
            new List<EnemyCombatAnimationPresenter>();

        internal static void AdoptPending(NebulaAnimationDirector director)
        {
            if (director == null) return;
            for (int i = 0; i < AwaitingAdoption.Count; i++)
            {
                EnemyCombatAnimationPresenter presenter = AwaitingAdoption[i];
                if (presenter != null) presenter.AttachTo(director);
            }
            AwaitingAdoption.Clear();
        }

        public static void ResetPending() => AwaitingAdoption.Clear();

        public static int PendingCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < AwaitingAdoption.Count; i++)
                    if (AwaitingAdoption[i] != null) live++;
                return live;
            }
        }

        private EnemyAgent agent;
        private NebulaAnimationDirector director;
        private AnimationRegistration registration;
        private Camera billboardCamera;

        private PoseAnchor visual;
        private GameObject cachedVisual;
        private readonly List<PoseAnchor> parts = new List<PoseAnchor>();
        private Transform actionAnchor;

        private Vector3 previousPosition;
        private bool hasPreviousPosition;
        private float bankDegrees;
        private float partPhase;
        private float visualRadius = 4f;

        private SupportOperationLease vfxLease = SupportOperationLease.None;
        private EnemyCombatVisual activeVisual = EnemyCombatVisual.Idle;

        // --- the Looter's real theft, shown as one representative item ---
        private readonly Dictionary<string, float> lastStolen = new Dictionary<string, float>();
        private ItemProxyLease siphonLease = ItemProxyLease.None;
        private Vector3 siphonFrom;
        private Vector3 siphonTo;
        private float siphonElapsed;
        private bool siphonPrimed;

        /// <summary>The pose last read from the enemy's real state. Test-facing.</summary>
        public EnemyCombatVisual CurrentVisual => activeVisual;

        /// <summary>How many real mechanical parts this role's model actually has. Test-facing.</summary>
        public int MechanicalPartCount => parts.Count;

        /// <summary>True when the role's signature VFX anchor exists on the model. Test-facing.</summary>
        public bool HasActionAnchor => actionAnchor != null;

        /// <summary>How many real thefts this Looter has shown. Test-facing; never a cargo count.</summary>
        public int TheftsShown { get; private set; }

        /// <summary>True while a stolen item is crossing to the Looter. Test-facing.</summary>
        public bool HasStolenItemInFlight => siphonLease.IsValid;

        public float BankDegrees => bankDegrees;

        public bool IsAlive => this != null && gameObject != null;

        public void Initialize(EnemyAgent enemyAgent, NebulaAnimationDirector animationDirector)
        {
            agent = enemyAgent;
            Rebind();

            if (animationDirector != null) AttachTo(animationDirector);
            else if (!AwaitingAdoption.Contains(this)) AwaitingAdoption.Add(this);
        }

        private void AttachTo(NebulaAnimationDirector animationDirector)
        {
            if (animationDirector == null || ReferenceEquals(director, animationDirector)) return;
            registration.Release();
            director = animationDirector;
            registration = director.Register(this);
        }

        /// <summary>
        /// Captures the authored pose of the visual child and of whichever of
        /// this role's real parts the model actually carries. A part the audit
        /// lists but the instance lacks is simply skipped.
        /// </summary>
        private void Rebind()
        {
            visual.Reset();
            parts.Clear();
            actionAnchor = null;
            bankDegrees = 0f;
            hasPreviousPosition = false;

            Transform found = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null || child.GetComponentInChildren<Renderer>(true) == null) continue;
                found = child;
                break;
            }

            cachedVisual = found != null ? found.gameObject : null;
            visual = PoseAnchor.Capture(found);
            if (found == null || agent == null || agent.Def == null) return;

            var renderer = found.GetComponentInChildren<Renderer>(true);
            visualRadius = renderer != null ? Mathf.Max(1f, renderer.bounds.extents.magnitude) : 4f;

            foreach (string partName in CombatAnimationProfiles.MechanicalParts(agent.Def.Id))
            {
                Transform part = NebulaVisualResolver.FindAnchor(found, partName);
                if (part != null) parts.Add(PoseAnchor.Capture(part));
            }

            actionAnchor = NebulaVisualResolver.FindAnchor(found, CombatAnimationProfiles.ActionVfxAnchor(agent.Def.Id));
        }

        public void VisualTick(float deltaSeconds)
        {
            if (agent == null || agent.Def == null) { ReleaseVfx(); return; }

            Transform live = FindVisualChild();
            if (!ReferenceEquals(live != null ? live.gameObject : null, cachedVisual)) Rebind();

            activeVisual = ResolveVisual();

            if (!isActiveAndEnabled || !visual.IsValid) { ReleaseVfx(); return; }

            if (AnimationAccessibility.MotionOff)
            {
                // The agent keeps flying its own course; only the decorative
                // pose snaps to its final state.
                visual.Reset();
                ResetParts();
                UpdateVfx();
                return;
            }

            float motion = CombatAnimationProfiles.MotionScaleFor(activeVisual);
            bool secondary = director == null || CombatAnimationProfiles.SecondaryMotionAllowed(director.Profile);

            UpdateBank(deltaSeconds, motion, secondary);
            UpdateParts(deltaSeconds, motion, secondary);
            UpdateVfx();
            SampleTheft();
            AdvanceSiphonProxy(deltaSeconds);
        }

        private Transform FindVisualChild()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.GetComponentInChildren<Renderer>(true) != null) return child;
            }
            return null;
        }

        /// <summary>
        /// Reads the real phase and the two real timers that outrank it. The
        /// disrupt and telegraph clocks belong to the agent.
        /// </summary>
        private EnemyCombatVisual ResolveVisual()
        {
            bool scanning = agent.Def.ScanSeconds > 0f && agent.Ai != null
                            && agent.Ai.ScanElapsed > 0f && !agent.Ai.ScanComplete;
            bool siphoning = agent.LooterCargo != null && agent.CurrentLootTarget.HasValue;

            return CombatAnimationProfiles.ForEnemy(
                agent.Ai != null ? agent.Ai.Phase : EnemyAiPhase.Acquire,
                agent.Destroyed, agent.IsDisrupted, agent.IsTelegraphing, scanning, siphoning);
        }

        /// <summary>Leans the model into the course the agent is really flying.</summary>
        private void UpdateBank(float deltaSeconds, float motion, bool secondary)
        {
            Vector3 position = transform.position;
            Vector3 velocity = Vector3.zero;
            if (hasPreviousPosition && deltaSeconds > 0f) velocity = (position - previousPosition) / deltaSeconds;
            previousPosition = position;
            hasPreviousPosition = true;

            float target = 0f;
            if (!CombatAnimationProfiles.IsClosedPose(activeVisual) && velocity.sqrMagnitude > 1e-4f)
            {
                Vector3 local = transform.InverseTransformDirection(velocity.normalized);
                float scale = CombatAnimationProfiles.BankScale(agent.Def.Id) * motion * (secondary ? 1f : 0.35f);
                target = Mathf.Clamp(-local.x * CombatAnimationProfiles.MaxEnemyBankDegrees * scale,
                    -CombatAnimationProfiles.MaxEnemyBankDegrees, CombatAnimationProfiles.MaxEnemyBankDegrees);
            }

            bankDegrees = Mathf.Lerp(bankDegrees, target, 1f - Mathf.Exp(-5f * deltaSeconds));
            visual.Apply(Vector3.zero, Quaternion.Euler(0f, 0f, bankDegrees));
        }

        /// <summary>
        /// Works this role's real parts: the Probe's iris, the Fighter's pods,
        /// the Looter's harpoons and hatch, the Disruptor's resonators, the
        /// Heavy's wings, the Siege barrel and cradle. Amplitude only — no part
        /// ever leaves its authored base.
        /// </summary>
        private void UpdateParts(float deltaSeconds, float motion, bool secondary)
        {
            if (parts.Count == 0) return;

            if (CombatAnimationProfiles.IsClosedPose(activeVisual) || !secondary) { ResetParts(); return; }

            bool working = activeVisual == EnemyCombatVisual.Attacking
                           || activeVisual == EnemyCombatVisual.Scanning
                           || activeVisual == EnemyCombatVisual.Siphoning
                           || activeVisual == EnemyCombatVisual.Telegraphing;

            if (!working) { ResetParts(); return; }

            partPhase += deltaSeconds * 3f;
            float swing = Mathf.Sin(partPhase) * 6f * motion;

            for (int i = 0; i < parts.Count; i++)
            {
                PoseAnchor part = parts[i];
                float sign = (i % 2 == 0) ? 1f : -1f;
                part.Apply(Vector3.zero, Quaternion.Euler(swing * sign, 0f, 0f));
            }
        }

        private void ResetParts()
        {
            for (int i = 0; i < parts.Count; i++) parts[i].Reset();
            partPhase = 0f;
        }

        /// <summary>
        /// Draws the role's signature effect on the anchor its own model
        /// carries. A full pool costs the effect and nothing else.
        /// </summary>
        private void UpdateVfx()
        {
            bool wanted = CombatAnimationProfiles.DrawsVfx(activeVisual)
                          && !AnimationAccessibility.MotionOff
                          && actionAnchor != null;

            if (!wanted) { ReleaseVfx(); return; }
            if (director == null || director.SupportOperations == null) return;

            NebulaCommonVfx sprite = CombatAnimationProfiles.ActionVfx(agent.Def.Id, activeVisual);

            if (vfxLease.IsValid && vfxLease.Vfx != sprite) ReleaseVfx();

            if (!vfxLease.IsValid)
            {
                if (!director.SupportOperations.TryLease(sprite, out SupportOperationLease lease, out _)) return;
                vfxLease = lease;
            }

            if (!director.SupportOperations.TryGetQuad(vfxLease, out Transform quad) || quad == null)
            {
                vfxLease = SupportOperationLease.None;
                return;
            }

            quad.position = actionAnchor.position;
            // Sized from the model that is actually there, not from a stat.
            quad.localScale = Vector3.one * Mathf.Max(1f, visualRadius * 1.5f);

            if (billboardCamera == null) billboardCamera = Camera.main;
            if (billboardCamera != null)
                quad.rotation = Quaternion.LookRotation(quad.position - billboardCamera.transform.position, Vector3.up);
        }

        /// <summary>
        /// Watches the Looter's own cargo. Only a theft that really succeeded
        /// moves this, so a refused or zero-amount siphon shows nothing. The
        /// amount, the transfer rate, the capacity limit and what happens to the
        /// cargo when the Looter dies all stay where they were.
        /// </summary>
        private void SampleTheft()
        {
            LooterCargoState cargo = agent.LooterCargo;
            if (cargo == null) return;

            if (!siphonPrimed)
            {
                // A restored or promoted Looter adopts its haul silently: a load
                // never replays a theft that already happened.
                foreach (KeyValuePair<string, float> kv in cargo.Items) lastStolen[kv.Key] = kv.Value;
                siphonPrimed = true;
                return;
            }

            string grew = null;
            foreach (KeyValuePair<string, float> kv in cargo.Items)
            {
                float before = lastStolen.TryGetValue(kv.Key, out float v) ? v : 0f;
                if (kv.Value - before > SupportOperationProfiles.TransferEpsilon) { grew = kv.Key; }
                lastStolen[kv.Key] = kv.Value;
            }

            if (grew != null) LaunchSiphonProxy(grew);
        }

        /// <summary>
        /// Sends one representative item across, however much was taken. The
        /// theft has already happened either way — a full pool costs the picture
        /// and nothing else.
        /// </summary>
        private void LaunchSiphonProxy(string itemId)
        {
            if (siphonLease.IsValid) return;
            if (director == null || director.ItemProxies == null) return;
            if (!director.ItemProxies.TryLease(itemId, out ItemProxyLease lease, out Transform proxy)) return;
            if (proxy == null) { director.ItemProxies.Release(lease); return; }

            // The harpoon anchor the model really carries is where the haul
            // arrives; the victim is whatever the Looter is really facing.
            Vector3 hull = actionAnchor != null ? actionAnchor.position : transform.position;

            siphonLease = lease;
            siphonFrom = hull + transform.forward * (visualRadius * 2.5f);
            siphonTo = hull;
            siphonElapsed = 0f;
            TheftsShown++;

            proxy.SetPositionAndRotation(siphonFrom, transform.rotation);
        }

        private void AdvanceSiphonProxy(float deltaSeconds)
        {
            if (!siphonLease.IsValid) return;
            if (director == null || director.ItemProxies == null) { ReleaseSiphon(); return; }
            if (!director.ItemProxies.TryGetProxy(siphonLease, out Transform proxy) || proxy == null) { ReleaseSiphon(); return; }

            siphonElapsed += deltaSeconds;
            float t = Mathf.Clamp01(siphonElapsed / 0.5f);
            proxy.position = Vector3.Lerp(siphonFrom, siphonTo, t);
            if (t >= 1f) ReleaseSiphon();
        }

        private void ReleaseSiphon()
        {
            if (!siphonLease.IsValid) return;
            director?.ItemProxies?.Release(siphonLease);
            siphonLease = ItemProxyLease.None;
            siphonElapsed = 0f;
        }

        private void ReleaseVfx()
        {
            if (!vfxLease.IsValid) return;
            director?.SupportOperations?.Release(vfxLease);
            vfxLease = SupportOperationLease.None;
        }

        public void ResetToBasePose()
        {
            bankDegrees = 0f;
            hasPreviousPosition = false;
            visual.Reset();
            ResetParts();
            ReleaseVfx();
            ReleaseSiphon();
        }

        private void OnDisable() => ResetToBasePose();

        private void OnDestroy()
        {
            registration.Release();
            ResetToBasePose();
            AwaitingAdoption.Remove(this);
            agent = null;
            director = null;
        }
    }
}
