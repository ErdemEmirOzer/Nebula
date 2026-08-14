using UnityEngine;
using Nebula.Combat;

namespace Nebula.Visuals
{
    /// <summary>
    /// Lights one enemy from its own AI phase and durability. Reads that state
    /// and changes none of it — no AI decision, no damage number and no
    /// targeting result depends on this component existing.
    /// <para>
    /// Emission only. Twenty-four enemies in a raid add zero real-time lights;
    /// their muzzle and impact flashes come from the shared transient pool via
    /// <see cref="CombatLightingBridge"/>, so the ceiling holds however big the
    /// fight gets.
    /// </para>
    /// </summary>
    public class EnemyLightingPresenter : EmissionPresenter
    {
        private EnemyAgent agent;

        /// <summary>The AI phase last resolved against. Test-facing.</summary>
        public EnemyAiPhase CurrentPhase { get; private set; } = EnemyAiPhase.Acquire;

        public EnemyRole Role => agent != null && agent.Def != null ? agent.Def.Role : EnemyRole.Fighter;

        public void Bind(EnemyAgent enemyAgent, LightingQuality quality)
        {
            agent = enemyAgent;
            SetQuality(quality);
            RebuildRendererCache();
            Apply();
        }

        private void LateUpdate() => Apply();

        protected override Color ResolveEmission()
        {
            if (agent == null || agent.Def == null) return Color.black;

            EnemyAiPhase phase = agent.Ai != null ? agent.Ai.Phase : EnemyAiPhase.Acquire;
            CurrentPhase = phase;

            bool disabled = agent.Combat != null && agent.Combat.Disabled;
            bool disrupted = agent.IsDisrupted;

            (Color color, float strength) = CombatLightingProfiles.Enemy(agent.Def.Role, phase, disrupted, disabled);

            // A hostile reading as an alarm is exactly what accessibility
            // settings are allowed to soften, so this goes through the flash path.
            return StructureLightingProfiles.Resolve(color, strength, Quality, isWarning: true);
        }

        private void OnDestroy() => agent = null;
    }
}
