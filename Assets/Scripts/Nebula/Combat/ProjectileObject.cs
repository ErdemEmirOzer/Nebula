using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.Combat
{
    /// <summary>
    /// MonoBehaviour physical projectile for Plasma/EM (VS-013 §3) and, as of
    /// VS-014 §15, hostile enemy Plasma/Siege fire — Laser (player or
    /// planetary) is always a pure hitscan tick and never spawns one of
    /// these. Position integrates every frame and is checked against real
    /// targets/celestial bodies each tick, exactly like
    /// <see cref="Ship.ShipController.MoveWithCollision"/>'s own approach:
    /// never an instant/teleported hit. A hostile, interceptable projectile
    /// is also checked each tick against every live active planetary Laser
    /// in its own system before it's allowed to reach its target.
    /// </summary>
    public class ProjectileObject : MonoBehaviour
    {
        private const float HitRadius = 15f;

        public ProjectileState State { get; private set; }
        private CombatEncounterDirector director;

        public static ProjectileObject Spawn(ProjectileKind kind, int tier, string ownerId, Vector3 position, Vector3 velocity,
            float lifetimeSeconds, string lockedTargetId, CombatEncounterDirector director,
            ProjectileFaction faction = ProjectileFaction.Player, bool interceptable = false, float interceptionDurability = 0f,
            float damageAmount = 0f, string intendedTargetId = null)
        {
            using (director?.ResourceScope?.Activate())
            {
                var go = new GameObject(kind + " Projectile");
                BuildVisual(go, kind);
                go.transform.position = position;

                var proj = go.AddComponent<ProjectileObject>();
                string id = director != null ? director.NextProjectileId() : System.Guid.NewGuid().ToString("N");
                proj.State = new ProjectileState(id, kind, tier, ownerId, position, velocity, lifetimeSeconds, lockedTargetId,
                    faction, interceptable, interceptionDurability, damageAmount, intendedTargetId);
                proj.director = director;
                director?.RegisterProjectile(proj);
                // ANI-04.3: presentation only — works this round's own core and fins.
                // Never touches its position, velocity, lifetime or collider.
                go.AddComponent<Nebula.Visuals.ProjectileAnimationPresenter>().Initialize(proj);
                return proj;
            }
        }

        private static void BuildVisual(GameObject go, ProjectileKind kind)
        {
            float envelope = kind == ProjectileKind.EnemySiege ? 9f
                : kind == ProjectileKind.Plasma || kind == ProjectileKind.EnemyPlasma ? 6f : 4f;
            if (NebulaVisualCatalog.TryGetProjectile(kind, out NebulaVisualDefinition definition)
                && NebulaVisualResolver.Instantiate(
                    definition, go.transform, envelope, "Authored Projectile Visual") != null)
                return;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.transform.SetParent(go.transform, false);
            bool isPlasmaLike = kind == ProjectileKind.Plasma || kind == ProjectileKind.EnemyPlasma;
            body.transform.localScale = Vector3.one * envelope;
            Color color = kind switch
            {
                ProjectileKind.Plasma => new Color(1f, 0.5f, 0.15f),
                ProjectileKind.EnemyPlasma => new Color(1f, 0.25f, 0.15f),
                ProjectileKind.EnemySiege => new Color(0.9f, 0.1f, 0.1f),
                _ => new Color(0.65f, 0.35f, 1f)
            };
            body.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.CreateAdditive(color);
            if (Application.isPlaying) Destroy(body.GetComponent<Collider>()); else DestroyImmediate(body.GetComponent<Collider>());
        }

        public void RestoreState(ProjectileState restored)
        {
            State = restored;
            transform.position = State.Position;
        }

        private void Update()
        {
            if (State == null) return;
            float dt = Time.deltaTime;

            if (State.Kind == ProjectileKind.Em && State.LockedTargetId != null)
            {
                EnemyAgent target = EnemyAgent.Find(State.LockedTargetId);
                if (target != null && !target.Destroyed) State.GuideToward(target.transform.position, WeaponCatalog.EmMaxTurnRateDegreesPerSecond, dt);
                else State.LoseLock();
            }

            if (State.Faction == ProjectileFaction.Hostile && State.Interceptable)
            {
                TickInterception(dt);
                if (State.InterceptedDown)
                {
                    NebulaVfxPulse.Emit(
                        NebulaCommonVfx.HitSpark, transform.position, 18f,
                        new Color(1f, 0.65f, 0.2f, 0.9f));
                    director?.NotifyProjectileResolved(State, transform.position, intercepted: true);
                    Despawn();
                    return;
                }
            }

            State.Tick(dt);
            transform.position = State.Position;
            if (State.Velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(State.Velocity.normalized);

            if (CheckHit()) return;
            if (State.Expired) Despawn();
        }

        /// <summary>VS-014 §15: every live active planetary Laser in range+LOS chips away at the projectile's interception durability — never a second result once it's already shot down.</summary>
        private void TickInterception(float dt)
        {
            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || s.Type != SurfaceStructureType.Laser || !s.Runtime.IsActive) continue;
                Vector3 laserAbs = s.AbsolutePosition;
                Vector3 projAbs = transform.position + World.FloatingOrigin.TotalOffset;
                float distance = Vector3.Distance(laserAbs, projAbs);
                StructureTierDef def = PlanetaryDefenseCatalog.LaserT1; // interception uses the always-available T1 baseline rate regardless of the actual built tier -- a deliberately simple, uniform defense contribution
                if (distance > def.Range) continue;
                if (!HasLineOfSight(s.transform.position, transform.position)) continue;
                State.ApplyInterceptionHit(def.Dps * dt);
                if (State.InterceptedDown) return;
            }
        }

        private static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            if (Physics.Raycast(from, delta.normalized, out RaycastHit hit, distance))
                if (hit.collider.GetComponentInParent<Celestial.CelestialBody>() != null) return false;
            return true;
        }

        private bool CheckHit()
        {
            if (State.Faction == ProjectileFaction.Hostile) return CheckHostileHit();

            foreach (EnemyAgent agent in new List<EnemyAgent>(EnemyAgent.All.Values))
            {
                if (agent == null || agent.Destroyed) continue;
                if (Vector3.Distance(transform.position, agent.transform.position) > HitRadius) continue;
                ApplyHit(agent);
                return true;
            }

            var bodies = Simulation.GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] == null) continue;
                if (Vector3.Distance(transform.position, bodies[i].transform.position) <= bodies[i].Radius) { Despawn(); return true; }
            }
            return false;
        }

        /// <summary>Hostile fire only ever hits the player ship — never friendly fire against other enemies, never an EnemyAgent target.</summary>
        private bool CheckHostileHit()
        {
            ShipCombat playerCombat = director?.PlayerCombat;
            if (playerCombat != null && playerCombat.IsTargetable)
            {
                if (Vector3.Distance(transform.position, playerCombat.transform.position) <= HitRadius)
                {
                    bool hitShield = playerCombat.State.Durability.Shield > 0.001f;
                    playerCombat.ApplyDamage(State.DamageAmount, DamageProfile.Neutral);
                    EmitImpact(hitShield);
                    Despawn();
                    return true;
                }
            }

            var bodies = Simulation.GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] == null) continue;
                if (Vector3.Distance(transform.position, bodies[i].transform.position) <= bodies[i].Radius) { Despawn(); return true; }
            }
            return false;
        }

        private void ApplyHit(EnemyAgent primaryTarget)
        {
            bool hitShield = primaryTarget.Combat.Shield > 0.001f;
            if (State.Kind == ProjectileKind.Plasma)
            {
                // VS-014 §15: a planetary Plasma defense carries its own DPS-derived DamageAmount (PlanetaryDefenseCatalog, never the ship's tier table); the ship's own Plasma still derives its damage from WeaponCatalog's tier table exactly as before.
                float direct = State.DamageAmount > 0f ? State.DamageAmount : WeaponCatalog.PlasmaDirectDamage(State.Tier);
                primaryTarget.ApplyDamage(direct, DamageProfile.Plasma);
                foreach (EnemyAgent other in new List<EnemyAgent>(EnemyAgent.All.Values))
                {
                    if (other == null || other == primaryTarget || other.Destroyed) continue;
                    float distance = Vector3.Distance(transform.position, other.transform.position);
                    float splash = WeaponCatalog.PlasmaSplashDamageForDirect(direct, distance);
                    if (splash > 0f) other.ApplyDamage(splash, DamageProfile.Plasma);
                }
            }
            else
            {
                primaryTarget.ApplyDamage(WeaponCatalog.EmDamagePerShot(State.Tier), DamageProfile.Em);
                primaryTarget.TryDisrupt(WeaponCatalog.EmDisruptSeconds);
            }
            EmitImpact(hitShield);
            Despawn();
        }

        private void EmitImpact(bool hitShield)
        {
            NebulaVfxPulse.Emit(
                hitShield ? NebulaCommonVfx.ShieldImpact : NebulaCommonVfx.HitSpark,
                transform.position, hitShield ? 24f : 16f,
                hitShield
                    ? new Color(0.25f, 0.9f, 1f, 0.85f)
                    : new Color(1f, 0.55f, 0.2f, 0.9f));
            director?.NotifyProjectileResolved(State, transform.position, intercepted: false);
        }

        private void Despawn()
        {
            director?.UnregisterProjectile(this);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }
    }
}
