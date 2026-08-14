using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §14: the single, persistent Protected Pirate Base — a real
    /// physical, attackable structure (never a fake parallel damage system:
    /// its own Durability + DamageResolver + PlanetaryDefenseCatalog.LaserT1's
    /// exact DPS/range are the SAME shared combat contract every other
    /// defense weapon uses, just oriented hostile-to-player). Materialized
    /// by <see cref="CombatEncounterDirector.Create"/> exactly once per Full
    /// entry into its home system, whenever RaidCampaignState.PirateBase
    /// says it has already been spawned there — the marker itself is never
    /// destroyed/despawned even after Resolved, only its garrison/reward.
    /// </summary>
    public class PirateBaseRuntime : MonoBehaviour, ICombatTarget
    {
        public event Action Hit;
        public event Action TurretFired;
        public event Action Destroyed;
        public event Action RewardGranted;
        public string PoiId { get; private set; }
        public string SystemId { get; private set; }
        public Durability Combat { get; private set; }
        public bool Resolved { get; private set; }

        private ShipController playerShip;
        private ShipCombat playerCombat;
        private CombatEncounterDirector director;
        private float laserCooldownRemaining;
        private readonly List<string> garrisonEnemyIds = new List<string>();

        // --- ICombatTarget ------------------------------------------------------
        public string TargetId => PoiId;
        public TargetCategory Category => TargetCategory.Structure;
        public Vector3 AbsolutePosition => transform.position + FloatingOrigin.TotalOffset;
        public bool IsTargetable => !Resolved && Combat != null && !Combat.Disabled;

        public void ApplyDamage(float rawAmount, DamageProfile profile)
        {
            if (Resolved || Combat == null) return;
            float before = Combat.Shield + Combat.Armor + Combat.Hull;
            DamageResolver.Apply(Combat, rawAmount, profile);
            if (Combat.Shield + Combat.Armor + Combat.Hull < before - 0.0001f)
                Hit?.Invoke();
            if (Combat.Disabled) HandleResolved();
        }

        public static PirateBaseRuntime Create(string poiId, string systemId, Vector3 position, CombatEncounterDirector director, bool alreadyResolved)
        {
            var go = new GameObject("Protected Pirate Base (" + poiId + ")");
            if (director != null) go.transform.SetParent(director.transform, false);
            go.transform.position = position;
            BuildVisual(go);
            var runtime = go.AddComponent<PirateBaseRuntime>();
            runtime.PoiId = poiId;
            runtime.SystemId = systemId;
            runtime.director = director;
            runtime.playerShip = director != null ? director.PlayerShip : null;
            runtime.playerCombat = director != null ? director.PlayerCombat : null;
            runtime.Combat = new Durability(PirateBaseCatalog.ShieldMax, PirateBaseCatalog.ArmorMax, PirateBaseCatalog.HullMax, startFraction: 1f);
            runtime.Combat.RestoreFromSave(PirateBaseCatalog.ShieldMax, PirateBaseCatalog.ArmorMax, PirateBaseCatalog.HullMax, regenTimer: 0f);
            runtime.Resolved = alreadyResolved;
            // ANI-04.3: presentation only — turret aim from the shots this base
            // really fires. A base restored as resolved never replays its fall.
            go.AddComponent<Nebula.Visuals.PirateBaseAnimationPresenter>().Initialize(runtime, alreadyResolved);
            if (!alreadyResolved) runtime.SpawnGarrison();
            return runtime;
        }

        private static void BuildVisual(GameObject go)
        {
            if (NebulaVisualResolver.Instantiate(
                    NebulaVisualCatalog.ProtectedPirateBase, go.transform,
                    instanceName: "Authored Protected Pirate Base Visual") != null)
                return;
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(90f, 60f, 90f);
            body.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.CreateLit(new Color(0.5f, 0.15f, 0.15f), 0.5f, 0.3f);
            if (Application.isPlaying) Destroy(body.GetComponent<Collider>()); else DestroyImmediate(body.GetComponent<Collider>());
        }

        private void SpawnGarrison()
        {
            if (director == null) return;
            bool heavyEligible = CombatCampaignState.RaidCampaign.IsTaught(EnemyIds.HeavyAssaultMk1) && ThreatStageHasProgressedPastPirate();
            foreach (string defId in PirateBaseCatalog.Garrison(heavyEligible))
            {
                Vector3 offset = new Vector3(Random01Deterministic(garrisonEnemyIds.Count, 0) * 400f - 200f,
                    Random01Deterministic(garrisonEnemyIds.Count, 1) * 200f - 100f, Random01Deterministic(garrisonEnemyIds.Count, 2) * 400f - 200f);
                EnemyAgent agent = director.SpawnEnemy(defId, transform.position + offset);
                if (agent != null) garrisonEnemyIds.Add(agent.Id);
            }
        }

        /// <summary>Deterministic per-slot placement offset (never UnityEngine.Random) -- purely cosmetic spread, not a gameplay-relevant seed.</summary>
        private float Random01Deterministic(int slot, int axis) =>
            ExtractionCatalog.Unit01(ExtractionCatalog.Hash(ExtractionState.UniverseSeed, PoiId + "/garrison/" + slot + "/" + axis));

        /// <summary>VS-014 §14: "threat stage has progressed" -- at least one Assault or Siege has already been resolved somewhere in the campaign (never taught early via the Base itself).</summary>
        private static bool ThreatStageHasProgressedPastPirate()
        {
            foreach (RaidHistoryEntry entry in CombatCampaignState.RaidCampaign.History)
                if (entry.Stage == RaidStage.Assault || entry.Stage == RaidStage.Siege) return true;
            return false;
        }

        private void Update()
        {
            if (Resolved || Combat == null) return;
            float dt = Time.deltaTime;
            Combat.Tick(dt);
            TickDefenseFire(dt);
        }

        /// <summary>Two Laser T1-equivalent turrets, folded into one ~2x-rate hitscan tick against the player -- the same shared DamageResolver/DamageProfile.Laser path every planetary Laser uses, oriented hostile.</summary>
        private void TickDefenseFire(float dt)
        {
            laserCooldownRemaining -= dt;
            if (playerCombat == null || playerShip == null || !playerCombat.IsTargetable) return;
            float distance = Vector3.Distance(transform.position, playerShip.transform.position);
            if (distance > PlanetaryDefenseCatalog.LaserT1.Range || laserCooldownRemaining > 0f) return;

            playerCombat.ApplyDamage(PlanetaryDefenseCatalog.LaserT1.Dps, DamageProfile.Laser);
            TurretFired?.Invoke();
            // LGT-05.1: same visual-only contract the planetary structures use.
            // Raised only here, where the shot has already been committed —
            // every range and cooldown check is above this line.
            Transform turretMuzzle = Visuals.NebulaVisualResolver.FindAnchor(transform, "anchor.muzzle.laser");
            Vector3 muzzlePosition = turretMuzzle != null ? turretMuzzle.position : transform.position;
            Visuals.DefenseFireSignal.Raise(WeaponFamily.Laser, muzzlePosition,
                (playerShip.transform.position - muzzlePosition).normalized,
                Visuals.DefenseFireOwner.PirateBase);
            laserCooldownRemaining = 1f; // two turrets' combined DPS delivered once per second
        }

        /// <summary>Exactly once: marks the campaign ledger, awards the one-time XP/parts/material reward, and reduces this system's current Threat Pressure -- guarded by RaidCampaignState.PirateBase.RewardAwarded so a repeated destroy-tick (or a save/load replay) never re-grants it. The Base itself is never despawned -- Resolved just disables its garrison/reward/hostility.</summary>
        private void HandleResolved()
        {
            if (Resolved) return;
            Resolved = true;
            Destroyed?.Invoke();
            CombatCampaignState.RaidCampaign.MarkPirateBaseResolved();

            if (!CombatCampaignState.RaidCampaign.PirateBase.RewardAwarded)
            {
                ulong seed = ExtractionCatalog.Hash(ExtractionState.UniverseSeed, PoiId + "/reward");
                Progression.ProgressionService.Award("vs014.pirate_base.reward/" + PoiId, PirateBaseCatalog.RewardXp);

                int parts = PirateBaseCatalog.ResolveBasicPartsReward(seed);
                int materialAmount = PirateBaseCatalog.ResolveMaterialReward(seed, out string materialId);
                var wreck = new CombatWreckageState(SystemId + "/combat-wreck.pirate_base");
                if (parts > 0) wreck.Nodes.Add(new CombatWreckageNode(ProcessingCatalog.BasicPartId, parts));
                if (materialAmount > 0) wreck.Nodes.Add(new CombatWreckageNode(materialId, materialAmount));
                if (director != null && wreck.Nodes.Count > 0) director.Wreckage.Add(wreck);

                director?.Pressure.ReducePressure(PirateBaseCatalog.RewardThreatPressureReduction);
                CombatCampaignState.RaidCampaign.MarkPirateBaseRewardAwarded();
                RewardGranted?.Invoke();
            }
        }
    }
}
