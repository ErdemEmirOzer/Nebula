using System;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Progression;
using Nebula.Ship;

namespace Nebula.Modules
{
    /// <summary>
    /// Ship-side host for the module system: owns the
    /// <see cref="ModuleUpgradeService"/>, ticks it every frame (skipped
    /// automatically whenever Time.timeScale is 0 — the progression screen's
    /// pause — since dt is then 0), and applies the two functional Industry
    /// modules' tier effects to <see cref="LogisticsCargo"/> and
    /// <see cref="ShipProcessing"/> the instant a job completes. Also
    /// forwards start/complete/cancel to an optional save hook, matching
    /// "save on start, cancel, and completion".
    /// </summary>
    public class ShipModules : MonoBehaviour
    {
        public ModuleProgressionState Modules { get; private set; }
        public ModuleUpgradeService Upgrades { get; private set; }

        /// <summary>Set by the bootstrap once the save director exists.</summary>
        public Action RequestSave { get; set; }

        private ShipLogistics logistics;
        private ShipProcessing processing;
        private ShipExtraction extraction;
        private ShipCruise cruise;
        private ShipHangar hangar;
        private ShipCombat combat;

        private void Awake() => EnsureWired();

        /// <summary>
        /// Finds the sibling components and creates the tier state.
        /// <para>
        /// Split out of <see cref="Awake"/> and called defensively, because Unity
        /// does not run <c>Awake</c> on a component added to a GameObject outside
        /// play mode. Anything building a ship in an EditMode test therefore got a
        /// <c>ShipModules</c> whose <see cref="Modules"/> was still null, and
        /// <see cref="InitializeNewCampaign"/> threw on its first line — which is
        /// what forced ship-based tests into PlayMode and made them cost minutes
        /// instead of milliseconds. Idempotent, so the normal play-mode path is
        /// unchanged: <c>Awake</c> still does the wiring exactly once.
        /// </para>
        /// </summary>
        private void EnsureWired()
        {
            if (Modules != null) return;

            logistics = GetComponent<ShipLogistics>();
            processing = GetComponent<ShipProcessing>();
            extraction = GetComponent<ShipExtraction>();
            cruise = GetComponent<ShipCruise>();
            hangar = GetComponent<ShipHangar>();
            combat = GetComponent<ShipCombat>();
            Modules = new ModuleProgressionState();
        }

        /// <summary>Called once by the bootstrap after ProgressionService.Reset() — wires the service and grants the free T1 industry modules.</summary>
        public void InitializeNewCampaign()
        {
            EnsureWired();
            Modules.GrantFreeStartingModules();
            Upgrades = new ModuleUpgradeService(Modules, ProgressionService.State, logistics.Cargo);
            Upgrades.JobStarted += _ => RequestSave?.Invoke();
            Upgrades.JobCancelled += _ => RequestSave?.Invoke();
            Upgrades.JobCompleted += OnJobCompleted;
            ApplyAllFunctionalEffects();
        }

        private void OnJobCompleted(ModuleUpgradeJob job)
        {
            ApplyFunctionalEffect(job.ModuleId);
            RequestSave?.Invoke();
        }

        /// <summary>Applies the current tier's effect for one functional module (cargo capacity / recorded processing tier).</summary>
        public void ApplyFunctionalEffect(string moduleId)
        {
            int tier = Modules.GetTier(moduleId);
            if (tier <= 0) return;
            int tierIndex = Mathf.Clamp(tier - 1, 0, ModuleDef.MaxTier - 1);

            if (moduleId == "industry.logistics_cargo" && logistics != null)
            {
                logistics.Cargo.SetCapacity(ModuleCatalog.CargoCapacityByTier[tierIndex]);
            }
            else if (moduleId == ModuleCatalog.ResourceProcessingId && processing != null)
            {
                // The bench has no slot count and no duration, so this hands over the
                // tier itself rather than two derived numbers. See
                // ShipProcessing.ApplyTier for why it currently grants nothing, and
                // why that is a recorded balance decision rather than a missing effect.
                processing.ApplyTier(tier);
            }
            else if (moduleId == "exploration.scanner" && extraction != null)
            {
                extraction.ApplyScannerTier(tier);
            }
            else if (moduleId == "exploration.engine_navigation" && cruise != null)
            {
                cruise.ApplyEngineTier(tier);
            }
            else if (moduleId == "exploration.hangar_bot" && hangar != null)
            {
                hangar.ApplyHangarTier(tier);
            }
            else if ((moduleId == "defense.hull_reinforcement" || moduleId == "defense.shield_systems" || moduleId == "defense.weapon_coordination") && combat != null)
            {
                // VS-013.1: a hull_reinforcement/shield_systems/weapon_coordination
                // purchase must re-derive ShipCombatState's max Shield/Armor/Hull/Energy
                // immediately -- without this hook the new multiplier would silently
                // never take effect until some unrelated event happened to touch it.
                combat.ReapplyDurabilityTiers();
            }
        }

        public void ApplyAllFunctionalEffects()
        {
            ApplyFunctionalEffect("industry.logistics_cargo");
            ApplyFunctionalEffect(ModuleCatalog.ResourceProcessingId);
            ApplyFunctionalEffect("exploration.scanner");
            ApplyFunctionalEffect("exploration.engine_navigation");
            ApplyFunctionalEffect("exploration.hangar_bot");
        }

        private void Update()
        {
            if (Upgrades == null) return;
            Upgrades.Tick(Time.deltaTime);
        }
    }
}
