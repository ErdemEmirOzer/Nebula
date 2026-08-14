using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Ship;
using Nebula.Simulation;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Combat
{
    /// <summary>
    /// MonoBehaviour driving one real AI enemy (VS-013 §6 Probe/Fighter;
    /// VS-014 §5 Looter/Disruptor/Heavy/Siege) — the SAME class for every
    /// role, differing only by <see cref="EnemyDef"/> data and a per-Role
    /// dispatch inside <see cref="Update"/>. Decisions run at the shared 5 Hz
    /// cadence (<see cref="EnemyAiState.DecisionDue"/>); physical
    /// movement/collision runs every FixedUpdate, always through a
    /// segment-checked, obstacle-aware step — never a teleport, never an
    /// instant heading snap. Materialized-system lifetime only:
    /// created/destroyed by <see cref="CombatEncounterDirector"/>, never
    /// spans a demotion. Heavy/Siege prefer a live priority STRUCTURE target
    /// over the player when one exists in range; Looter pursues a loot
    /// target (drill/Collector/Logistics Center) instead of the player
    /// entirely. Neither generalization touches Probe/Fighter's existing
    /// code path at all — both keep behaving exactly as VS-013 shipped them.
    /// </summary>
    public class EnemyAgent : MonoBehaviour
    {
        private static readonly Dictionary<string, EnemyAgent> byId = new Dictionary<string, EnemyAgent>();
        public static IReadOnlyDictionary<string, EnemyAgent> All => byId;
        public static EnemyAgent Find(string id) => byId.TryGetValue(id, out EnemyAgent a) ? a : null;
        public static void ClearRegistry() => byId.Clear();

        private const float CollisionRadius = 6f;

        public string Id { get; private set; }
        public EnemyDef Def { get; private set; }
        public EnemyAiState Ai { get; private set; }
        public Durability Combat { get; private set; }
        public bool Destroyed { get; private set; }

        /// <summary>VS-014 §7: only meaningful for Role==Looter — what it has stolen so far.</summary>
        public LooterCargoState LooterCargo { get; private set; }
        /// <summary>VS-014 §5: Heavy/Siege's current priority structure/logistics-center target, if any is in range — falls back to the player when null.</summary>
        public ICombatTarget StructureTarget { get; private set; }
        public LootTarget? CurrentLootTarget { get; private set; }
        /// <summary>VS-014 §5/§10: Disruptor/Siege visible telegraph countdown before their special action fires — read by the HUD, never silent.</summary>
        public float TelegraphRemaining { get; private set; }
        public bool IsTelegraphing => TelegraphRemaining > 0f;

        private ShipController playerShip;
        private ShipCombat playerCombat;
        private CombatEncounterDirector director;
        private ShipHangar playerHangar;
        private float weaponCooldownRemaining;
        private Vector3 velocity;
        private float disruptRemaining;
        private float disruptImmunityRemaining;
        private float looterMustRetreatLatched;

        /// <summary>EM disrupt (VS-013 §3): 3s impaired (frozen decisions/near-zero movement), then 8s immune to a NEW disrupt. No-op while immune.</summary>
        public bool TryDisrupt(float disruptSeconds)
        {
            if (disruptImmunityRemaining > 0f) return false;
            disruptRemaining = disruptSeconds;
            disruptImmunityRemaining = WeaponCatalog.EmImmunityAfterDisruptSeconds + disruptSeconds;
            return true;
        }
        public bool IsDisrupted => disruptRemaining > 0f;
        public float DisruptRemaining => disruptRemaining;
        public float DisruptImmunityRemaining => disruptImmunityRemaining;

        /// <summary>VS-014 §17: restores this enemy's own disrupt/immunity timers exactly as saved (distinct from ShipCombatState's own Disruptor-on-player timers).</summary>
        public void RestoreDisruptState(float disruptRemainingSeconds, float disruptImmunityRemainingSeconds)
        {
            disruptRemaining = Mathf.Max(0f, disruptRemainingSeconds);
            disruptImmunityRemaining = Mathf.Max(0f, disruptImmunityRemainingSeconds);
        }

        /// <summary>VS-014 §17: restores Disruptor/Siege's own telegraph countdown exactly as saved.</summary>
        public void RestoreTelegraph(float telegraphRemainingSeconds) => TelegraphRemaining = Mathf.Max(0f, telegraphRemainingSeconds);

        public static EnemyAgent Create(string id, EnemyDef def, Vector3 position, ShipController playerShip,
            CombatEncounterDirector director, Transform parent)
        {
            var go = new GameObject(def.DisplayName + " (" + id + ")");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            BuildVisual(go, def);

            var agent = go.AddComponent<EnemyAgent>();
            agent.Id = id;
            agent.Def = def;
            agent.Ai = new EnemyAiState(def);
            // Durability's constructor always starts Shield at 0 (correct for an
            // under-construction planetary structure, its original use case) -- a
            // freshly spawned enemy must start at full shield, so force it via
            // RestoreFromSave right after construction (same fix as ShipCombatState).
            agent.Combat = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, startFraction: 1f);
            agent.Combat.RestoreFromSave(def.ShieldMax, def.ArmorMax, def.HullMax, regenTimer: 0f);
            agent.playerShip = playerShip;
            agent.playerCombat = playerShip != null ? playerShip.GetComponent<ShipCombat>() : null;
            agent.playerHangar = playerShip != null ? playerShip.GetComponent<ShipHangar>() : null;
            agent.director = director;
            if (def.Role == EnemyRole.Looter) agent.LooterCargo = new LooterCargoState(def.CargoCapacityVolume);
            // LGT-05: presentation only — role/phase emission. Adds no light and
            // reads the agent without changing any AI or damage value.
            go.AddComponent<Nebula.Visuals.EnemyLightingPresenter>()
                .Bind(agent, Nebula.Visuals.LightingQualityProfile.FromQualitySettings().Quality);
            // ANI-04: presentation only — poses the visual child and this role's
            // real parts from the phase the AI is really in. Never moves the agent.
            go.AddComponent<Nebula.Visuals.EnemyCombatAnimationPresenter>().Initialize(agent, null);
            byId[id] = agent;
            return agent;
        }

        private static void BuildVisual(GameObject go, EnemyDef def)
        {
            float scale = def.Role switch { EnemyRole.Probe => 12f, EnemyRole.Fighter => 18f, EnemyRole.Looter => 22f, EnemyRole.Disruptor => 20f, EnemyRole.Heavy => 28f, EnemyRole.Siege => 36f, _ => 18f };
            if (NebulaVisualCatalog.TryGetEnemy(def.Id, out NebulaVisualDefinition definition)
                && NebulaVisualResolver.Instantiate(
                    definition, go.transform, scale, "Authored Enemy Visual") != null)
                return;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Enemy Visual (Fallback)";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * scale;
            Color tint = def.Role switch
            {
                EnemyRole.Probe => new Color(0.7f, 0.85f, 1f),
                EnemyRole.Looter => new Color(1f, 0.85f, 0.3f),
                EnemyRole.Disruptor => new Color(0.6f, 0.3f, 1f),
                EnemyRole.Heavy => new Color(0.9f, 0.3f, 0.15f),
                EnemyRole.Siege => new Color(0.55f, 0.1f, 0.1f),
                _ => new Color(1f, 0.4f, 0.35f)
            };
            body.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.CreateLit(tint, 0.4f, 0.2f);
            if (Application.isPlaying) Destroy(body.GetComponent<Collider>()); else DestroyImmediate(body.GetComponent<Collider>());
        }

        /// <summary>
        /// Safety net for the case Unity destroys this GameObject by cascade
        /// (its parent star's root being torn down on demotion) rather than
        /// through <see cref="HandleDefeated"/>/<see cref="HandleEscaped"/>
        /// — the static registry must never keep a dangling entry either way.
        ///
        /// Guarded (never an unconditional Remove(Id)): Unity's Destroy() on
        /// the old system's root is deferred to later in the frame, but a
        /// same-id restore into the NEW system (MaterializeCombat preserving
        /// the saved enemyId, VS-013 §10) can already have re-registered a
        /// live replacement under this exact key by the time this callback
        /// fires. An unconditional Remove would then delete the REPLACEMENT's
        /// entry, not this stale one — only remove if this instance is still
        /// the one actually registered.
        /// </summary>
        private void OnDestroy()
        {
            // An agent can be destroyed before it was ever given an id — a spawn that
            // is torn down in the same frame it was added, which is ordinary during a
            // scene swap. Dictionary lookups throw on a null key rather than missing,
            // so the unguarded form turned that into an ArgumentNullException out of
            // OnDestroy, where it aborts the rest of the teardown. An agent with no id
            // was never in the registry, so there is nothing here to remove.
            if (string.IsNullOrEmpty(Id)) return;
            if (byId.TryGetValue(Id, out EnemyAgent current) && current == this) byId.Remove(Id);
        }

        public void RestoreFromSave(EnemyAiPhase phase, string targetId, float scanElapsed, float continuousEscapeSeconds,
            float shield, float armor, float hull, float regenTimer)
        {
            Ai.RestoreFromSave(phase, targetId, scanElapsed, continuousEscapeSeconds);
            Combat.RestoreFromSave(shield, armor, hull, regenTimer);
        }

        /// <summary>VS-014 §17: restores a Looter's stolen cargo exactly as saved — never re-rolled, never duplicated.</summary>
        public void RestoreLooterCargo(IEnumerable<(string itemId, float amount)> saved) => LooterCargo?.RestoreFromSave(saved);

        public void ApplyDamage(float amount) => ApplyDamage(amount, DamageProfile.Neutral);

        /// <summary>VS-014 §3: the same shared Shield/Armor/Hull multiplier path ship weapons, planetary defenses, and enemy weapons all route through — a plain <see cref="ApplyDamage(float)"/> call is just Neutral (1/1/1).</summary>
        public void ApplyDamage(float amount, DamageProfile profile)
        {
            if (Destroyed) return;
            DamageResolver.Apply(Combat, amount, profile);
            director?.Encounter.RegisterThreatEvent();
            if (Combat.Disabled) HandleDefeated();
        }

        private void HandleDefeated()
        {
            if (Destroyed) return;
            Destroyed = true;

            if (Def.Id == EnemyIds.ScoutProbeMk1) director?.ResolveFirstProbe(probeScanned: false, position: transform.position);

            // VS-014 §7: a destroyed Looter's stolen cargo becomes physical wreckage -- never lost, never duplicated into the general raid salvage.
            if (Def.Role == EnemyRole.Looter && LooterCargo != null)
            {
                List<CombatWreckageNode> nodes = LooterCargoService.ToWreckageNodes(LooterCargo);
                if (nodes.Count > 0 && director != null)
                {
                    var wreck = new CombatWreckageState(director.SystemId + "/combat-wreck.looter." + System.Guid.NewGuid().ToString("N").Substring(0, 6));
                    wreck.Nodes.AddRange(nodes);
                    director.Wreckage.Add(wreck);
                }
            }

            byId.Remove(Id);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        /// <summary>
        /// True while an open screen has taken flight control away from the player.
        /// <para>
        /// Combat is suspended for exactly as long as this holds, and the reason is
        /// symmetry rather than convenience: <see cref="Nebula.Ship.ShipCombat.Update"/>
        /// returns on this same gate, so with it raised the player cannot target,
        /// fire, boost or repair. An enemy that kept attacking through it would be
        /// shooting at someone the game itself has disarmed — and the shell hides the
        /// HUD while a screen is open, so they would not even see it happening.
        /// </para>
        /// <para>
        /// This used to be free: screens zeroed <c>Time.timeScale</c>, which stopped
        /// every enemy along with everything else. Screens no longer stop the clock —
        /// the world is meant to keep living behind a menu — so the suspension that
        /// was previously a side effect of the pause has to be stated.
        /// </para>
        /// <para>
        /// Only combat is suspended. Extraction, production, module upgrades and
        /// navigation all keep running behind an open screen, which is the point of
        /// the change.
        /// </para>
        /// </summary>
        private static bool CombatSuspended => InputGate.Suppressed;

        private void Update()
        {
            if (Destroyed || playerShip == null || CombatSuspended) return;
            float dt = Time.deltaTime;

            if (disruptRemaining > 0f) disruptRemaining = Mathf.Max(0f, disruptRemaining - dt);
            if (disruptImmunityRemaining > 0f) disruptImmunityRemaining = Mathf.Max(0f, disruptImmunityRemaining - dt);
            if (IsDisrupted)
            {
                Ai.TickRetreatEscape(dt, Vector3.Distance(transform.position, playerShip.transform.position), SensorContainsPlayer());
                return; // frozen: no scan/attack/decision progress while disrupted
            }

            switch (Def.Role)
            {
                case EnemyRole.Probe: TickProbe(dt); break;
                case EnemyRole.Looter: TickLooter(dt); break;
                case EnemyRole.Disruptor: TickDisruptor(dt); break;
                case EnemyRole.Heavy: TickStructurePriorityCombat(dt); break;
                case EnemyRole.Siege: TickSiege(dt); break;
                default: TickFighter(dt); break; // EnemyRole.Fighter, and any future combat-capable default
            }

            if (Ai.DecisionDue(dt)) MakeDecision();

            float distanceForEscape = Vector3.Distance(transform.position, EffectiveTargetPosition());
            Ai.TickRetreatEscape(dt, distanceForEscape, SensorContainsPlayer());
            if (Ai.Phase == EnemyAiPhase.Escaped) HandleEscaped();
        }

        private void TickProbe(float dt)
        {
            float distance = Vector3.Distance(transform.position, playerShip.transform.position);
            bool inBand = Ai.Phase == EnemyAiPhase.AttackOrScan && distance <= Def.EngageBandMax && distance >= 0f && HasLineOfSightTo(playerShip.transform.position);
            Ai.TickScan(dt, inBand);
            if (Ai.ScanComplete && Ai.Phase == EnemyAiPhase.AttackOrScan)
            {
                director?.ResolveFirstProbe(probeScanned: true, position: transform.position);
                Ai.MakeDecision(distance, targetLost: false, exitAttackOrScanToRetreat: true);
            }
        }

        private void TickFighter(float dt)
        {
            if (!Def.IsCombatCapable) return;
            weaponCooldownRemaining -= dt;
            if (Ai.Phase == EnemyAiPhase.AttackOrScan && weaponCooldownRemaining <= 0f)
            {
                float distance = Vector3.Distance(transform.position, playerShip.transform.position);
                if (distance <= Def.AttackRange && HasLineOfSightTo(playerShip.transform.position))
                {
                    playerCombat?.ApplyDamage(Def.Dps * 1f); // 1-second-equivalent tick below via cooldown pacing
                    director?.Encounter.RegisterThreatEvent();
                    weaponCooldownRemaining = 1f;
                }
            }
        }

        /// <summary>VS-014 §5: Heavy prioritizes a live active planetary defense structure in range over the player; falls back to the exact Fighter-style player attack otherwise.</summary>
        private void TickStructurePriorityCombat(float dt)
        {
            RefreshStructureTarget(preferDefenseFirst: true);
            weaponCooldownRemaining -= dt;
            if (Ai.Phase != EnemyAiPhase.AttackOrScan || weaponCooldownRemaining > 0f) return;

            Vector3 targetPos = EffectiveTargetPosition();
            float distance = Vector3.Distance(transform.position, targetPos);
            if (distance > Def.AttackRange || !HasLineOfSightTo(targetPos)) return;

            if (StructureTarget != null && StructureTarget.IsTargetable) StructureTarget.ApplyDamage(Def.Dps, DamageProfile.Neutral);
            else playerCombat?.ApplyDamage(Def.Dps);
            director?.Encounter.RegisterThreatEvent();
            weaponCooldownRemaining = 1f;
        }

        /// <summary>VS-014 §5/§15: Command Center then Logistics Center priority, telegraph, then a real physical (slow, Laser-interceptable) projectile — falls back to the player when no priority structure is in range.</summary>
        private void TickSiege(float dt)
        {
            RefreshStructureTarget(preferDefenseFirst: false);
            Vector3 targetPos = EffectiveTargetPosition();
            float distance = Vector3.Distance(transform.position, targetPos);
            bool inRange = Ai.Phase == EnemyAiPhase.AttackOrScan && distance <= Def.AttackRange && HasLineOfSightTo(targetPos);

            if (TelegraphRemaining > 0f)
            {
                TelegraphRemaining = Mathf.Max(0f, TelegraphRemaining - dt);
                if (TelegraphRemaining <= 0f && inRange) FireSiegeProjectile(targetPos);
                return;
            }

            weaponCooldownRemaining -= dt;
            if (inRange && weaponCooldownRemaining <= 0f)
            {
                TelegraphRemaining = Def.TelegraphSeconds;
                weaponCooldownRemaining = 4f; // real cadence between shots, independent of the telegraph window itself
            }
        }

        private void FireSiegeProjectile(Vector3 targetPos)
        {
            Vector3 direction = (targetPos - transform.position).normalized;
            // VS-017A §1: no live lockedTargetId (Siege is proximity-hit, never homing), but the shot
            // still has a real, stable intended target -- whatever EffectiveTargetPosition aimed it at.
            string intendedTargetId = StructureTarget != null && StructureTarget.IsTargetable ? StructureTarget.TargetId : "ship";
            ProjectileObject.Spawn(ProjectileKind.EnemySiege, 1, Id, transform.position, direction * ProjectileCatalog.SiegeProjectileSpeed,
                ProjectileCatalog.SiegeLifetimeSeconds, null, director,
                ProjectileFaction.Hostile, interceptable: true, interceptionDurability: ProjectileCatalog.InterceptionDurability,
                damageAmount: Def.Dps * 2f, // one shot every ~4s carries ~2s-equivalent of its DPS
                intendedTargetId: intendedTargetId);
            director?.Encounter.RegisterThreatEvent();
        }

        /// <summary>VS-014 §5/§10: 1.5s telegraph, then damage + an EM disrupt on the player (respects the player's own per-target disrupt immunity via ShipCombatState).</summary>
        private void TickDisruptor(float dt)
        {
            float distance = Vector3.Distance(transform.position, playerShip.transform.position);
            bool inRange = Ai.Phase == EnemyAiPhase.AttackOrScan && distance <= Def.AttackRange && HasLineOfSightTo(playerShip.transform.position);

            if (TelegraphRemaining > 0f)
            {
                TelegraphRemaining = Mathf.Max(0f, TelegraphRemaining - dt);
                if (TelegraphRemaining <= 0f && inRange)
                {
                    playerCombat?.ApplyDamage(Def.Dps * Def.TelegraphSeconds);
                    playerCombat?.State?.TryDisrupt(Def.DisruptSeconds, Def.DisruptImmunitySeconds);
                    director?.Encounter.RegisterThreatEvent();
                }
                return;
            }

            weaponCooldownRemaining -= dt;
            if (inRange && weaponCooldownRemaining <= 0f)
            {
                TelegraphRemaining = Def.TelegraphSeconds;
                weaponCooldownRemaining = Def.TelegraphSeconds + 3f;
            }
        }

        /// <summary>VS-014 §7: pursues a loot target (drill/Collector/Logistics Center) instead of the player; steals over time; self-defends with flat DPS if the player closes to its attack range meanwhile; retreats at 50% optional/100% mandatory fill.</summary>
        private void TickLooter(float dt)
        {
            if (CurrentLootTarget == null && Ai.Phase != EnemyAiPhase.Retreat && Ai.Phase != EnemyAiPhase.Escaped)
                CurrentLootTarget = LooterCargoService.SelectTarget(director?.SystemId, playerHangar != null ? playerHangar.Vehicles : null);

            if (CurrentLootTarget.HasValue && Ai.Phase == EnemyAiPhase.AttackOrScan)
            {
                float distanceToLoot = Vector3.Distance(transform.position, CurrentLootTarget.Value.Position);
                if (distanceToLoot <= Def.EngageBandMax) TickTheftForCurrentTarget(dt);
            }

            weaponCooldownRemaining -= dt;
            float distanceToPlayer = Vector3.Distance(transform.position, playerShip.transform.position);
            if (Ai.Phase == EnemyAiPhase.AttackOrScan && distanceToPlayer <= Def.AttackRange && weaponCooldownRemaining <= 0f && HasLineOfSightTo(playerShip.transform.position))
            {
                playerCombat?.ApplyDamage(Def.Dps);
                director?.Encounter.RegisterThreatEvent();
                weaponCooldownRemaining = 1f;
            }
        }

        private void TickTheftForCurrentTarget(float dt)
        {
            LootTarget target = CurrentLootTarget.Value;
            float stolen = target.Kind switch
            {
                LootSourceKind.Drill => StealFromDrillById(target.TargetId, dt),
                LootSourceKind.Vehicle => StealFromVehicleById(target.TargetId, dt),
                LootSourceKind.LogisticsCenter => StealFromLogisticsCenterById(target.TargetId, dt),
                _ => 0f
            };
            if (stolen > 0f && director != null && director.Pressure.Phase == RaidPhase.Active) director.Pressure.NoteResourceStolen();
            if (stolen <= 0f && !LooterCargoService.MustRetreat(LooterCargo)) CurrentLootTarget = null; // source drained/gone -- re-select next decision tick
        }

        /// <summary>P1-WORLD-007: resolves the exact drill the target named, not the first one sharing its planet.</summary>
        private float StealFromDrillById(string drillTargetId, float dt)
        {
            DrillStructure drill = LooterCargoService.FindDrillByTargetId(drillTargetId);
            return drill != null ? LooterCargoService.StealFromDrill(LooterCargo, drill, Def.CargoTransferVolumePerSecond, dt) : 0f;
        }

        private float StealFromVehicleById(string vehicleId, float dt)
        {
            VehicleAgent v = playerHangar != null ? FindVehicle(playerHangar.Vehicles, vehicleId) : null;
            return v != null ? LooterCargoService.StealFromVehicle(LooterCargo, v, Def.CargoTransferVolumePerSecond, dt) : 0f;
        }

        private static VehicleAgent FindVehicle(IReadOnlyList<VehicleAgent> vehicles, string id)
        {
            for (int i = 0; i < vehicles.Count; i++) if (vehicles[i] != null && vehicles[i].VehicleId == id) return vehicles[i];
            return null;
        }

        private float StealFromLogisticsCenterById(string targetId, float dt)
        {
            LogisticsCenter center = LogisticsState.FindCenter(director?.SystemId);
            return center != null && center.TargetId == targetId ? LooterCargoService.StealFromLogisticsCenter(LooterCargo, center, Def.CargoTransferVolumePerSecond, dt) : 0f;
        }

        /// <summary>VS-014 §5: refreshes Heavy/Siege's priority structure pick — Heavy prefers the nearest active defense weapon; Siege prefers an active Command Center, then the system's Logistics Center. Never re-picks mid-engagement more than once per decision tick's caller.</summary>
        private void RefreshStructureTarget(bool preferDefenseFirst)
        {
            ICombatTarget best = null; float bestSqr = float.MaxValue;
            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || !s.IsTargetable) continue;
                bool isPriorityType = preferDefenseFirst
                    ? SurfaceStructureCatalog.IsActiveDefenseWeapon(s.Type)
                    : s.Type == SurfaceStructureType.Command;
                if (!isPriorityType) continue;
                float sqr = (s.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = s; }
            }
            if (best == null && !preferDefenseFirst)
            {
                LogisticsCenter center = LogisticsState.FindCenter(director?.SystemId);
                if (center != null && center.IsTargetable) best = center;
            }
            StructureTarget = best;
        }

        private Vector3 EffectiveTargetPosition()
        {
            if (Def.Role == EnemyRole.Looter && CurrentLootTarget.HasValue) return CurrentLootTarget.Value.Position;
            if (StructureTarget != null && StructureTarget.IsTargetable) return StructureTarget.AbsolutePosition - FloatingOrigin.TotalOffset;
            return playerShip.transform.position;
        }

        private void MakeDecision()
        {
            if (Ai.TargetId == null) Ai.AcquireTarget("player");
            float distance = Vector3.Distance(transform.position, EffectiveTargetPosition());
            bool exitToRetreat;
            if (Def.Id == EnemyIds.ScoutProbeMk1) exitToRetreat = Ai.ScanComplete;
            else if (Def.Role == EnemyRole.Looter) exitToRetreat = LooterCargoService.MustRetreat(LooterCargo) || (Combat.Hull / Mathf.Max(1f, Def.HullMax)) <= Def.RetreatHullFraction;
            else exitToRetreat = (Combat.Hull / Mathf.Max(1f, Def.HullMax)) <= Def.RetreatHullFraction;
            Ai.MakeDecision(distance, targetLost: false, exitAttackOrScanToRetreat: exitToRetreat);
        }

        private bool SensorContainsPlayer() => Vector3.Distance(transform.position, playerShip.transform.position) < EnemyCatalog.RetreatEscapeDistance;

        private bool HasLineOfSightTo(Vector3 targetPosition)
        {
            Vector3 delta = targetPosition - transform.position;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            if (Physics.Raycast(transform.position, delta.normalized, out RaycastHit hit, distance))
                if (hit.collider.GetComponentInParent<CelestialBody>() != null) return false;
            return true;
        }

        private void HandleEscaped()
        {
            if (Destroyed) return;
            Destroyed = true;
            // VS-014 §7: an escaped Looter's cargo is a PERMANENT loss (never wreckage, never returned).
            byId.Remove(Id);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        private void FixedUpdate()
        {
            // Movement is gated with the decisions above, not left running: closing on
            // the player and ramming them is ResolveShipCollision's business, so a
            // "frozen decisions, live movement" split would still let a disarmed
            // player be killed, just more slowly.
            if (Destroyed || playerShip == null || CombatSuspended) return;
            float dt = Time.fixedDeltaTime;

            Vector3 desiredDirection = ComputeDesiredDirection();
            Vector3 targetVelocity = desiredDirection * (IsDisrupted ? Def.Speed * 0.05f : Def.Speed);
            velocity = Vector3.Lerp(velocity, targetVelocity, 1f - Mathf.Exp(-3f * dt));

            Vector3 displacement = velocity * dt;
            transform.position = ResolveMovement(transform.position, transform.position + displacement);
            if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(velocity.normalized), 4f * dt);

            ResolveShipCollision(dt);
        }

        private Vector3 ComputeDesiredDirection()
        {
            Vector3 toTarget = EffectiveTargetPosition() - transform.position;
            float distance = toTarget.magnitude;
            if (distance < 0.01f) return transform.forward;
            Vector3 toward = toTarget.normalized;

            Vector3 desired = Ai.Phase switch
            {
                EnemyAiPhase.Approach => toward,
                EnemyAiPhase.AttackOrScan => distance < Def.EngageBandMin ? -toward : (distance > Def.EngageBandMax ? toward : Vector3.Cross(toward, Vector3.up).normalized),
                EnemyAiPhase.Break => -toward,
                EnemyAiPhase.Reposition => Vector3.Cross(toward, Vector3.up).normalized,
                EnemyAiPhase.ReEngage => toward,
                EnemyAiPhase.Retreat => -toward,
                EnemyAiPhase.Escaped => -toward,
                _ => Vector3.zero
            };

            return ApplyObstacleAvoidance(desired);
        }

        /// <summary>1.5s predictive lookahead against celestial bodies/asteroids (VS-013 §6) — steers away before a collision would occur, never an instant snap.</summary>
        private Vector3 ApplyObstacleAvoidance(Vector3 desired)
        {
            if (desired.sqrMagnitude < 0.0001f) return desired;
            Vector3 lookaheadPoint = transform.position + desired.normalized * Def.Speed * EnemyCatalog.ObstacleAvoidanceLookaheadSeconds;

            var bodies = GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBody b = bodies[i];
                if (b == null) continue;
                float minSeparation = ShipCollisionResolver.MinimumSeparation(b.Radius, CollisionRadius);
                if ((lookaheadPoint - b.transform.position).sqrMagnitude < minSeparation * minSeparation)
                {
                    Vector3 away = (transform.position - b.transform.position).normalized;
                    desired = (desired + away).normalized;
                }
            }
            return desired;
        }

        private Vector3 ResolveMovement(Vector3 start, Vector3 end)
        {
            var bodies = GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] == null) continue;
                end = ResolveSphereCollision(start, end, bodies[i].transform.position, bodies[i].Radius + CollisionRadius);
            }
            return end;
        }

        private Vector3 ResolveSphereCollision(Vector3 start, Vector3 end, Vector3 center, float minDistance)
        {
            Vector3 segment = end - start;
            float segLenSq = segment.sqrMagnitude;
            float t = segLenSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(center - start, segment) / segLenSq) : 0f;
            Vector3 closest = start + segment * t;
            Vector3 fromCenter = closest - center;
            if (fromCenter.sqrMagnitude >= minDistance * minDistance) return end;
            Vector3 normal = fromCenter.sqrMagnitude > 1e-6f ? fromCenter.normalized : (start - center).normalized;
            return center + normal * minDistance;
        }

        private float collisionImmunityRemaining;
        private void ResolveShipCollision(float dt)
        {
            if (collisionImmunityRemaining > 0f) { collisionImmunityRemaining -= dt; return; }
            float distance = Vector3.Distance(transform.position, playerShip.transform.position);
            float minSeparation = CollisionRadius + 3.5f; // ship's own default collisionRadius
            if (distance >= minSeparation) return;

            float relativeSpeed = (velocity - playerShip.Velocity).magnitude;
            float enemyDamage = ShipCollisionResolver.DamageForEvent(relativeSpeed, Def.HullMax);
            // VS-013.1: the ship's max Hull now depends on its live Hull Form (level)
            // and Hull Reinforcement tier -- read the actual current value rather than
            // a hardcoded tier-1 approximation.
            float shipMaxHull = playerCombat != null && playerCombat.State != null ? playerCombat.State.Durability.HullMax : CombatCatalog.BaseHull;
            float shipDamage = ShipCollisionResolver.DamageForEvent(relativeSpeed, shipMaxHull);
            if (enemyDamage > 0f) ApplyDamage(enemyDamage);
            if (shipDamage > 0f) playerCombat?.ApplyDamage(shipDamage);
            collisionImmunityRemaining = CombatCatalog.CollisionPairImmunitySeconds;
        }
    }
}
