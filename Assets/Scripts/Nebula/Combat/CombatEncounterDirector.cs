using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.Combat
{
    /// <summary>
    /// Materialized-Full-system-lifetime director (VS-013 §5) — created and
    /// destroyed by <see cref="Sectors.SystemStreamingDirector"/> alongside
    /// the star/planets themselves, exactly like POI materialization (VS-012).
    /// Owns this system's <see cref="CombatEncounterState"/>, its live
    /// enemies/projectiles, and any combat cargo-loss wreckage. Never
    /// progresses anything for a system that isn't currently Full — when
    /// this object is destroyed (demotion), every enemy/projectile parented
    /// under it is destroyed too, and only the frozen, already-captured
    /// SystemDelta state remains.
    /// </summary>
    public class CombatEncounterDirector : MonoBehaviour
    {
        public event Action<ProjectileState, Vector3, bool> ProjectileResolved;
        public CombatEncounterState Encounter { get; } = new CombatEncounterState();
        public string SystemId { get; private set; }
        public List<CombatWreckageState> Wreckage { get; } = new List<CombatWreckageState>();

        /// <summary>VS-014 §4: the ONE planetary-defense coordination runtime for this Full system's every Command/defense node — never a second global director.</summary>
        public PlanetaryDefenseCoordinator DefenseCoordinator { get; } = new PlanetaryDefenseCoordinator();

        /// <summary>VS-014 §8: this Full system's pressure/grace/raid-flow state — exists only while this director exists, exactly like <see cref="Encounter"/>, and is captured into/restored from this system's own SystemDelta rather than living for the whole campaign.</summary>
        public ThreatPressureState Pressure { get; } = new ThreatPressureState();

        /// <summary>VS-014 §14: non-null only in the one system that actually hosts the campaign's single Protected Pirate Base — materialized fresh each time this system goes Full, never re-spawned/duplicated.</summary>
        public PirateBaseRuntime PirateBase { get; private set; }
        public void SetPirateBase(PirateBaseRuntime runtime) => PirateBase = runtime;

        private ShipController playerShip;
        private ShipCombat playerCombat;
        /// <summary>VS-014 §15: the seam hostile projectiles/AI use to reach the player ship as a real ICombatTarget — never a new Find/locator.</summary>
        public ShipCombat PlayerCombat => playerCombat;
        public ShipController PlayerShip => playerShip;
        private readonly List<ProjectileObject> projectiles = new List<ProjectileObject>();
        private int nextProjectileSequence;
        private int nextEnemySequence;
        private ulong seed;
        internal RuntimeResourceScope ResourceScope { get; private set; }

        public static CombatEncounterDirector Create(string systemId, Transform starTransform, ShipController ship,
            ulong universeSeed, RuntimeResourceScope resourceScope = null)
        {
            var go = new GameObject("Combat Encounter Director");
            if (starTransform != null) go.transform.SetParent(starTransform, false);
            var director = go.AddComponent<CombatEncounterDirector>();
            director.SystemId = systemId;
            director.playerShip = ship;
            director.playerCombat = ship != null ? ship.GetComponent<ShipCombat>() : null;
            director.seed = universeSeed;
            director.ResourceScope = resourceScope;
            director.playerCombat?.SetDirector(director);
            go.AddComponent<CombatWreckagePresenter>().Initialize(director);

            bool hasControlledPlanet = HasControlledPlanetInSystem(systemId);
            CombatCampaignState.Recovery.NoteVisitedSystem(systemId, hasControlledPlanet);

            PirateBaseLedger baseLedger = CombatCampaignState.RaidCampaign.PirateBase;
            if (baseLedger.Spawned && baseLedger.SystemId == systemId)
            {
                Vector3 basePosition = (starTransform != null ? starTransform.position : Vector3.zero)
                    + PirateBaseCatalog.ComputePosition(ExtractionState.UniverseSeed, systemId);
                director.PirateBase = PirateBaseRuntime.Create(baseLedger.PoiId, systemId, basePosition, director, baseLedger.Resolved);
            }

            return director;
        }

        private static bool HasControlledPlanetInSystem(string systemId)
        {
            Sectors.SystemManifest manifest = World.StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return false;
            foreach (Sectors.PlanetManifest p in manifest.Planets)
                if (ExtractionState.GetDominance(p.PlanetId).State == DominanceState.Controlled) return true;
            return false;
        }

        public string NextProjectileId() => SystemId + "/projectile." + (nextProjectileSequence++).ToString("0000");
        public void RegisterProjectile(ProjectileObject p) { if (p != null && !projectiles.Contains(p)) projectiles.Add(p); }
        public void UnregisterProjectile(ProjectileObject p) => projectiles.Remove(p);
        public IReadOnlyList<ProjectileObject> Projectiles => projectiles;
        internal void NotifyProjectileResolved(ProjectileState state, Vector3 position, bool intercepted) =>
            ProjectileResolved?.Invoke(state, position, intercepted);

        public EnemyAgent SpawnEnemy(string defId, Vector3 position)
        {
            EnemyDef def = EnemyCatalog.Get(defId);
            if (def == null || playerShip == null) return null;
            string id = SystemId + "/enemy." + (nextEnemySequence++).ToString("0000");
            using (ResourceScope?.Activate())
                return EnemyAgent.Create(id, def, position, playerShip, this, transform);
        }

        /// <summary>
        /// VS-013 §10 restore path: unlike <see cref="SpawnEnemy"/>, this
        /// preserves the EXACT saved id (never a freshly generated one) --
        /// required so ShipCombatDto.targetEnemyId, an EM projectile's saved
        /// lock target, and the enemy's own future references all keep
        /// resolving after a save/load or a system reboot. Bumps the spawn
        /// sequence past any restored numeric suffix so a later fresh
        /// SpawnEnemy call in the same session can never collide with it.
        /// </summary>
        public EnemyAgent RestoreEnemy(string id, string defId, Vector3 position)
        {
            EnemyDef def = EnemyCatalog.Get(defId);
            if (def == null || playerShip == null || id == null) return null;
            BumpSequencePastRestoredId(id);
            using (ResourceScope?.Activate())
                return EnemyAgent.Create(id, def, position, playerShip, this, transform);
        }

        private void BumpSequencePastRestoredId(string id)
        {
            string prefix = SystemId + "/enemy.";
            if (!id.StartsWith(prefix)) return;
            if (int.TryParse(id.Substring(prefix.Length), out int n) && n >= nextEnemySequence) nextEnemySequence = n + 1;
        }

        /// <summary>Called by EnemyAgent exactly once per Probe resolution (scanned-and-escaped OR destroyed).</summary>
        public void ResolveFirstProbe(bool probeScanned, Vector3 position)
        {
            CombatCampaignState.FirstProbe.MarkResolved(probeScanned);
            if (!probeScanned)
            {
                CombatWreckageState wreck = GenerateProbeSalvage(position);
                if (wreck != null) Wreckage.Add(wreck);
                if (playerCombat != null) ProgressionAwardProbeDefeat();
            }
        }

        private void ProgressionAwardProbeDefeat() =>
            Progression.ProgressionService.Award("combat.probe.first.defeat", CombatCatalog.FirstProbeDefeatXp);

        /// <summary>Small, deterministic (seed-derived, never re-rolled) salvage — this campaign only ever spawns one Scout Probe.</summary>
        private CombatWreckageState GenerateProbeSalvage(Vector3 position)
        {
            ulong basicHash = ExtractionCatalog.Hash(seed, "probe-salvage/basic");
            ulong materialHash = ExtractionCatalog.Hash(seed, "probe-salvage/material");
            int basicAmount = 8 + Mathf.FloorToInt(ExtractionCatalog.Unit01(basicHash) * 8f); // 8-15
            int materialIndex = Mathf.FloorToInt(ExtractionCatalog.Unit01(materialHash) * ProcessingCatalog.MaterialIds.Length) % ProcessingCatalog.MaterialIds.Length;
            string materialId = ProcessingCatalog.MaterialIds[materialIndex];
            ulong materialAmountHash = ExtractionCatalog.Hash(seed, "probe-salvage/material-amount");
            int materialAmount = 3 + Mathf.FloorToInt(ExtractionCatalog.Unit01(materialAmountHash) * 5f); // 3-7

            var wreck = new CombatWreckageState(SystemId + "/combat-wreck.probe");
            wreck.Nodes.Add(new CombatWreckageNode(ProcessingCatalog.BasicPartId, basicAmount));
            wreck.Nodes.Add(new CombatWreckageNode(materialId, materialAmount));
            return wreck;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Encounter.Tick(dt);
            playerCombat?.SetEncounterOpen(Encounter.IsOpen);

            // VS-014 §8: grace/foreign-presence/active-raid-clock timers only ever run
            // while this system is the Full one (this instance's own lifetime already
            // guarantees that) -- Pressure accumulation itself is ticked by the raid
            // scheduler (VS-014 §9/§115), which supplies the stage-dependent rate this
            // pure state class deliberately never computes on its own.
            Pressure.TickGrace(dt);
            Pressure.TickForeignPresence(dt);
            Pressure.TickActive(dt);

            for (int i = Wreckage.Count - 1; i >= 0; i--)
            {
                Wreckage[i].Tick(dt);
                if (Wreckage[i].Expired) Wreckage.RemoveAt(i);
            }
        }

        /// <summary>
        /// Hands back the process-wide flag <see cref="Update"/> raises, because a
        /// flag raised every frame by an object that can be destroyed mid-raise has
        /// no owner left once that object is gone.
        /// <para>
        /// <c>Update</c> republishes <c>Encounter.IsOpen</c> into
        /// <see cref="Ship.CombatInputContext.CombatActive"/> (line 163) — a static
        /// with no persistence and no other writer while a fight is running. This
        /// director is MaterializedSystem-lifetime: a
        /// demotion destroys it the moment the ship leaves the system, and until now
        /// leaving mid-fight was the one exit that left <c>CombatActive</c> latched
        /// true for the rest of the process. Everything
        /// <c>CombatInputContext.WorldInteractionBlocked</c> gates — evacuation,
        /// the System Logistics Center, surface construction, the quick asset
        /// package, POI actions and processing — then stayed dead in the NEXT
        /// system, with no encounter anywhere to explain it and no way for the
        /// player to clear it short of starting another fight and finishing it.
        /// </para>
        /// <para>
        /// Identity-checked for the same reason <c>ExtractionState.UnregisterPlanet</c>
        /// is: a promotion builds the new system's director before Unity gets round to
        /// destroying the old one, so an unconditional release here would clear a flag
        /// the director that replaced this one had already legitimately raised.
        /// </para>
        /// </summary>
        private void OnDisable()
        {
            if (playerCombat != null && playerCombat.Director == this) playerCombat.SetEncounterOpen(false);
        }
    }
}
