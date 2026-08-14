using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Combat;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Extraction
{
    /// <summary>
    /// MonoBehaviour adapter for a Command/Laser/Plasma structure. Follows its
    /// planet's surface node (not parented to the scaled Planet), drives a
    /// <see cref="StructureRuntime"/>, and remembers what it consumed so a
    /// pre-active cancel can refund 75%. Registered in the shared structure
    /// registry so dominance can query it. VS-014 §6: also a real
    /// <see cref="ICombatTarget"/> — a raid enemy can physically damage a
    /// Command Center or defense weapon through the same shared cascade
    /// everything else uses.
    /// </summary>
    public class SurfaceStructure : MonoBehaviour, ICombatTarget
    {
        public string WorldId { get; private set; }
        public string NodeId { get; private set; }
        public SurfaceStructureType Type { get; private set; }
        public StructureRuntime Runtime { get; private set; }

        /// <summary>VS-015.1 §6: stable atomic-reservation owner id for this structure's own build cost — all items reserved together at Create/Restore, consumed together the instant it first reaches Active, refunded together (floor 75% each) on Cancel. Never a per-item flat-add.</summary>
        public string BuildOwnerId { get; private set; }
        private LogisticsCargo buildCargo;
        private List<(string itemId, float amount)> buildFullCost = new List<(string, float)>();
        /// <summary>The exact full build cost, for save capture (empty once consumed — i.e. once Active).</summary>
        public IReadOnlyList<(string itemId, float amount)> BuildFullCost => buildFullCost;

        /// <summary>VS-010 Quick Asset Package variant only: that system already permanently consumed this structure's exact share via its own package-wide reservation, so this is just a remembered flat refund debt, never a second live reservation. Null for every ordinary (reservation-based) structure.</summary>
        private List<(string itemId, float amount)> flatRefund;
        public IReadOnlyList<(string itemId, float amount)> FlatRefund => flatRefund;

        /// <summary>VS-014 §2: the in-flight tier upgrade job, if any — the structure stays fully Active/functional the whole time it's set.</summary>
        public StructureUpgradeJob ActiveUpgradeJob { get; private set; }

        /// <summary>VS-015 §5: this Command Center's own physical Maintenance Reserve — null for every non-Command structure.</summary>
        public MaintenanceReserve MaintenanceReserve { get; private set; }

        // --- ICombatTarget (VS-014 §6) ---------------------------------------
        public string TargetId => WorldId + "/" + NodeId;
        public TargetCategory Category => TargetCategory.Structure;
        public Vector3 AbsolutePosition => transform.position + FloatingOrigin.TotalOffset;
        public bool IsTargetable => Runtime != null && Runtime.State != DrillLifecycle.Disabled;
        public void ApplyDamage(float rawAmount, DamageProfile profile) => Runtime.ApplyDamage(rawAmount, profile);

        private PlanetIdentity planet;
        private Sectors.SurfaceAnchorCategory anchorCategory;
        private Vector3 localNormal;
        private float surfaceHeight;
        private GameObject constructionGlow;
        private GameObject body;
        private GameObject constructionStateVisual;
        private GameObject damageStateVisual;
        private GameObject repairUpgradeStateVisual;
        private int visualTier;
        private bool wasActive;
        private float lastDurabilityTotal = -1f;
        private float repairVisualHold;

        /// <summary>
        /// The real 3–8s delivery flight for a build launched from this position.
        /// <para>
        /// Lifted out of <see cref="Create"/> unchanged so the panel can quote the
        /// same number the build will actually use. A second copy of this formula
        /// in the interface would be a second authority on a gameplay duration,
        /// and would be wrong the first time either moved.
        /// </para>
        /// </summary>
        public static float CapsuleSecondsFor(Vector3 shipPosition, Celestial.CelestialBody body)
        {
            if (body == null) return 3f;
            return Mathf.Clamp(3f + Vector3.Distance(shipPosition, body.transform.position)
                / (body.Radius * 4f) * 5f, 3f, 8f);
        }

        /// <summary>VS-015.1 §6: fullCost (parts + every material, already validated affordable by the caller) is reserved atomically here — all items together or none. cargo may be null only for a legacy/no-cost call (never for a real build).</summary>
        public static SurfaceStructure Create(PlanetIdentity planet, PlanetProfile profile, SurfaceStructureDef def, SurfaceNode node,
            Vector3 shipPosition, LogisticsCargo cargo, List<(string itemId, float amount)> fullCost)
        {
            var go = new GameObject($"{def.Name} ({planet.WorldId})");
            var s = go.AddComponent<SurfaceStructure>();
            float capsuleSeconds = CapsuleSecondsFor(shipPosition, planet.Body);
            if (!s.Setup(planet, profile, def, node, new StructureRuntime(def, capsuleSeconds, profile.Environment), cargo, fullCost, shipPosition))
            {
                DestroyNow(go);
                return null;
            }
            return s;
        }

        /// <summary>Restore-only: fullCost is the ORIGINAL full recipe (from save); if the structure isn't Active yet, the reservation is re-derived from it against the cargo's real current stock (never re-validated, matching every other VS-015 restore path).</summary>
        public static SurfaceStructure Restore(PlanetIdentity planet, PlanetProfile profile, SurfaceStructureDef def, SurfaceNode node,
            StructureRuntime runtime, LogisticsCargo cargo, List<(string itemId, float amount)> fullCost)
        {
            var go = new GameObject($"{def.Name} ({planet.WorldId})");
            var s = go.AddComponent<SurfaceStructure>();
            if (!s.Setup(planet, profile, def, node, runtime, cargo, fullCost))
            {
                DestroyNow(go);
                return null;
            }
            return s;
        }

        /// <summary>VS-010 Quick Asset Package only: the cost was already consumed by QuickAssetPackageService's own package-wide transaction — this structure just remembers the pre-computed flat refund debt (no reservation of its own, unrelated to the VS-015.1 §6 multi-item atomic path above).</summary>
        public static SurfaceStructure CreateWithFlatRefund(PlanetIdentity planet, PlanetProfile profile, SurfaceStructureDef def, SurfaceNode node,
            Vector3 shipPosition, List<(string itemId, float amount)> flatRefundAmounts)
        {
            var go = new GameObject($"{def.Name} ({planet.WorldId})");
            var s = go.AddComponent<SurfaceStructure>();
            float capsuleSeconds = CapsuleSecondsFor(shipPosition, planet.Body);
            s.Setup(planet, profile, def, node, new StructureRuntime(def, capsuleSeconds, profile.Environment), null, null, shipPosition);
            s.flatRefund = flatRefundAmounts ?? new List<(string, float)>();
            return s;
        }

        /// <summary>Restore-only counterpart of CreateWithFlatRefund — never re-reserves anything, just restores the exact remembered flat refund debt.</summary>
        public static SurfaceStructure RestoreWithFlatRefund(PlanetIdentity planet, PlanetProfile profile, SurfaceStructureDef def, SurfaceNode node,
            StructureRuntime runtime, List<(string itemId, float amount)> flatRefundAmounts)
        {
            var go = new GameObject($"{def.Name} ({planet.WorldId})");
            var s = go.AddComponent<SurfaceStructure>();
            s.Setup(planet, profile, def, node, runtime, null, null);
            s.flatRefund = flatRefundAmounts ?? new List<(string, float)>();
            return s;
        }

        private bool Setup(PlanetIdentity planetIdentity, PlanetProfile profile, SurfaceStructureDef def, SurfaceNode node,
            StructureRuntime runtime, LogisticsCargo cargo, List<(string itemId, float amount)> fullCost,
            Vector3 capsuleStart = default)
        {
            planet = planetIdentity;
            WorldId = planetIdentity.WorldId;
            NodeId = node.Id;
            Type = def.Type;
            Runtime = runtime;
            localNormal = node.LocalNormal;
            anchorCategory = profile.AnchorCategory;
            // VS-009 §5: total anchor distance is AnchorRadiusMultiplier * R (1.01 solid / 1.03 water / 1.08 gas).
            surfaceHeight = planet.Body.Radius * (profile.AnchorRadiusMultiplier - 1f);

            BuildOwnerId = $"structure-build:{WorldId}/{NodeId}";
            buildCargo = cargo;
            buildFullCost = fullCost != null ? new List<(string, float)>(fullCost) : new List<(string, float)>();

            // A restored owner replaces its old lock atomically. If the saved
            // cargo cannot fund the saved job, materialization stops here; no
            // unfunded runtime is allowed to advance to Active.
            wasActive = runtime.State == DrillLifecycle.Active;
            if (buildFullCost.Count > 0)
            {
                if (buildCargo == null || !buildCargo.TrySetReservation(BuildOwnerId, buildFullCost)) return false;
                if (wasActive)
                {
                    buildCargo.ConsumeReservation(BuildOwnerId);
                    buildFullCost.Clear();
                }
            }

            transform.position = SurfacePoint();
            BuildVisuals(def);
            if (Type == SurfaceStructureType.Command) MaintenanceReserve = new MaintenanceReserve(WorldId, runtime.Tier);
            // LGT-04: presentation only — emission from this structure's own
            // runtime state. Adds no light and changes no gameplay value.
            gameObject.AddComponent<Nebula.Visuals.StructureLightingPresenter>()
                .BindStructure(this, Nebula.Visuals.LightingQualityProfile.FromQualitySettings().Quality);
            // ANI-02: presentation only — poses the visual child from this
            // structure's own lifecycle. Targeting, aim, collider and every
            // gameplay position are untouched.
            gameObject.AddComponent<Nebula.Visuals.StructureAnimationPresenter>().Bind(this, null);
            // ANI-05.1: the mechanisms this model really carries. The whole-root
            // working bob is switched off wherever real parts were found.
            if (NebulaVisualCatalog.TryGetStructure(Type, runtime.Tier, out NebulaVisualDefinition structureVisual)
                && EconomyMechanicalProfiles.HasRealMechanisms(structureVisual.VisualId))
            {
                Transform visualRoot = FindAnimatedVisualRoot();
                if (visualRoot != null)
                {
                    var pose = GetComponent<Nebula.Visuals.StructureAnimationPresenter>();
                    if (pose != null) pose.SuppressWholeRootWorkMotion = true;

                    gameObject.AddComponent<Nebula.Visuals.EconomyMechanicalPresenter>()
                        .Initialize(structureVisual.VisualId, visualRoot, ResolveMechanicalDrive, ResolveMechanicalCadence);
                }
            }

            // ANI-04.3: presentation only — a defense emplacement's turret follows
            // the shots it really fires. Only the two weapon types have turrets.
            if (Type == SurfaceStructureType.Laser || Type == SurfaceStructureType.Plasma)
            {
                gameObject.AddComponent<Nebula.Visuals.DefenseTurretAnimationPresenter>()
                    .Initialize(Type == SurfaceStructureType.Plasma
                        ? "structure.defense.plasma" : "structure.defense.laser",
                        runtime.Tier, () => DefenseOverheatRemaining);
            }
            // ANI-02.1: the delivery capsule's visual descent. A restored
            // structure passes no ship position and descends from above.
            gameObject.AddComponent<Nebula.Visuals.BuildCapsulePresenter>()
                .BindStructure(this, capsuleStart, null);
            ExtractionState.RegisterStructure(this);
            return true;
        }

        private void OnDestroy() => ExtractionState.UnregisterStructure(this);

        private void Update()
        {
            if (planet == null || planet.Body == null) { DestroyNow(gameObject); return; }
            Runtime.Tick(Time.deltaTime);
            transform.position = SurfacePoint();

            // XP: first Command / first inner Laser reaching Active (transition only).
            if (!wasActive && Runtime.State == DrillLifecycle.Active)
            {
                wasActive = true;
                // VS-015.1 §6: consume the whole atomic build reservation exactly once, the instant construction completes.
                if (buildCargo != null && buildFullCost.Count > 0) buildCargo.ConsumeReservation(BuildOwnerId);
                buildFullCost.Clear();
                if (Type == SurfaceStructureType.Command)
                    Progression.ProgressionService.Award(
                        Progression.ProgressionEvents.CommandFirst(WorldId), Progression.ProgressionEvents.CommandXp);
                else
                    Progression.ProgressionService.Award(
                        Progression.ProgressionEvents.DefenseInnerFirst(WorldId), Progression.ProgressionEvents.DefenseXp);
            }

            bool building = Runtime.State == DrillLifecycle.CapsuleInTransit
                            || Runtime.State == DrillLifecycle.Constructing
                            || Runtime.State == DrillLifecycle.Damaged;
            if (constructionGlow != null && !building) { DestroyNow(constructionGlow); constructionGlow = null; }

            TickUpgradeJob(Time.deltaTime);
            RefreshBodyVisualIfNeeded();
            if (body != null) body.SetActive(Runtime.State == DrillLifecycle.Active || Runtime.IsDisabled);
            RefreshStateVisuals(Time.deltaTime);
            TickDefenseCombat(Time.deltaTime);
            MaintenanceReserve?.ApplyTier(Runtime.Tier);
        }


        /// <summary>ANI-05.1: the first renderer-bearing child, which is the authored model.</summary>
        private Transform FindAnimatedVisualRoot()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.GetComponentInChildren<Renderer>(true) != null) return child;
            }
            return null;
        }

        /// <summary>ANI-05.1 presentation read: how hard this structure is really running.</summary>
        private Nebula.Visuals.MechanicalDrive ResolveMechanicalDrive()
        {
            if (Runtime == null) return Nebula.Visuals.MechanicalDrive.Stowed;
            switch (Runtime.State)
            {
                case DrillLifecycle.Constructing: return Nebula.Visuals.MechanicalDrive.Stowed;
                case DrillLifecycle.Active: return Nebula.Visuals.MechanicalDrive.Working;
                case DrillLifecycle.Planned:
                case DrillLifecycle.CapsuleInTransit: return Nebula.Visuals.MechanicalDrive.Stowed;
                default: return Nebula.Visuals.MechanicalDrive.Disabled;
            }
        }

        /// <summary>ANI-05.1 presentation read: cadence only. No production value is touched.</summary>
        private float ResolveMechanicalCadence() => 1f;

        // --- VS-014 §4/§15: planetary defense firing ---------------------------------
        private Combat.EnemyAgent defenseTarget;
        private Vector3 aimDirection;
        private float continuousFireSeconds;
        private float overheatCooldownRemaining;
        private float plasmaFireCooldownRemaining;
        private float laserVisualCooldownRemaining;
        public float DefenseContinuousFireSeconds => continuousFireSeconds;
        public float DefenseOverheatRemaining => overheatCooldownRemaining;
        public float DefensePlasmaCooldownRemaining => plasmaFireCooldownRemaining;

        /// <summary>
        /// Active Laser/Plasma only. Every weapon on the planet reads the SAME
        /// coordinator-resolved shared target this tick (never re-picks per weapon),
        /// then independently gates its own fire on range/LOS/turn-rate aim/overheat
        /// or fire-interval cooldown — the coordinator only ever decides WHO to
        /// shoot at, never HOW/WHEN a specific weapon fires.
        /// </summary>
        private void TickDefenseCombat(float dt)
        {
            if (!SurfaceStructureCatalog.IsActiveDefenseWeapon(Type) || !Runtime.IsActive) return;
            if (overheatCooldownRemaining > 0f) overheatCooldownRemaining = Mathf.Max(0f, overheatCooldownRemaining - dt);
            if (plasmaFireCooldownRemaining > 0f) plasmaFireCooldownRemaining = Mathf.Max(0f, plasmaFireCooldownRemaining - dt);

            StructureTierDef def = PlanetaryDefenseCatalog.Get(Type, Runtime.Tier);
            if (def == null) return;
            CombatEncounterDirector director = StarSystemRegistry.Streaming?.CombatEncounter;
            if (director == null) { defenseTarget = null; return; }

            bool commandActive = ExtractionState.HasActiveCommand(WorldId);
            var inRange = new List<Combat.EnemyAgent>();
            foreach (Combat.EnemyAgent a in Combat.EnemyAgent.All.Values)
            {
                if (a == null || a.Destroyed) continue;
                if ((a.transform.position - transform.position).sqrMagnitude <= def.Range * def.Range) inRange.Add(a);
            }

            defenseTarget = director.DefenseCoordinator.ResolveSharedTarget(WorldId, commandActive, Type, inRange, transform.position, dt);
            if (defenseTarget == null) { continuousFireSeconds = 0f; return; }

            Vector3 toTarget = defenseTarget.transform.position - transform.position;
            float distance = toTarget.magnitude;
            if (distance > def.Range || distance < 0.01f || !HasLineOfSight(transform.position, defenseTarget.transform.position))
            { continuousFireSeconds = 0f; return; }

            Vector3 desiredDirection = toTarget / distance;
            float effectiveTurnRate = def.TurnDegreesPerSecond * (commandActive ? 1f : Combat.PlanetaryDefenseCoordinator.CommandDisabledAcquisitionMultiplier);
            bool aimed;
            if (effectiveTurnRate <= 0f) { aimDirection = desiredDirection; aimed = true; } // T1 Laser: hitscan, always aimed
            else
            {
                if (aimDirection == Vector3.zero) aimDirection = desiredDirection;
                aimDirection = Vector3.RotateTowards(aimDirection, desiredDirection, effectiveTurnRate * Mathf.Deg2Rad * dt, 0f);
                aimed = Vector3.Angle(aimDirection, desiredDirection) <= 5f;
            }
            if (!aimed) { continuousFireSeconds = 0f; return; }

            if (Type == SurfaceStructureType.Laser) FireLaserAtTarget(def, dt);
            else FirePlasmaAtTarget(def, desiredDirection, director);
        }

        private void FireLaserAtTarget(StructureTierDef def, float dt)
        {
            if (overheatCooldownRemaining > 0f) return;
            defenseTarget.ApplyDamage(def.Dps * dt, Combat.DamageProfile.Laser);
            laserVisualCooldownRemaining -= dt;
            if (laserVisualCooldownRemaining <= 0f)
            {
                Transform muzzle = body != null
                    ? NebulaVisualResolver.FindAnchor(body.transform, "anchor.muzzle.laser")
                    : null;
                Vector3 muzzlePosition = muzzle != null ? muzzle.position : transform.position;
                LaserBeamPulse.Spawn(muzzlePosition, defenseTarget.transform.position, transform.parent);
                // LGT-05.1: presentation only, raised on the visible shot the
                // beam pulse already represents — after every overheat and
                // aim check, so a refused shot announces nothing.
                Nebula.Visuals.DefenseFireSignal.Raise(Combat.WeaponFamily.Laser, muzzlePosition,
                    (defenseTarget.transform.position - muzzlePosition).normalized,
                    Nebula.Visuals.DefenseFireOwner.PlanetaryStructure);
                laserVisualCooldownRemaining = 0.1f;
            }
            continuousFireSeconds += dt;
            if (def.ContinuousFireSecondsBeforeOverheat > 0f && continuousFireSeconds >= def.ContinuousFireSecondsBeforeOverheat)
            {
                overheatCooldownRemaining = def.OverheatCooldownSeconds;
                continuousFireSeconds = 0f;
            }
        }

        private void FirePlasmaAtTarget(StructureTierDef def, Vector3 direction, CombatEncounterDirector director)
        {
            if (plasmaFireCooldownRemaining > 0f) return;
            // VS-017A §1: no live lockedTargetId (proximity-hit, never homing), but defenseTarget is the
            // real coordinator-resolved enemy this shot is aimed at -- a stable id worth carrying forward.
            Combat.ProjectileObject.Spawn(Combat.ProjectileKind.Plasma, Runtime.Tier, TargetId, transform.position,
                direction * Combat.WeaponCatalog.PlasmaProjectileSpeed, Combat.ProjectileCatalog.PlanetaryPlasmaLifetimeSeconds,
                null, director, Combat.ProjectileFaction.Player,
                damageAmount: def.Dps * Combat.ProjectileCatalog.PlanetaryPlasmaFireIntervalSeconds,
                intendedTargetId: defenseTarget.Id);
            // LGT-05.1: the projectile is away, so this shot is committed. The
            // impact light still comes later from ProjectileResolved — one
            // muzzle here, one impact there, never both from either.
            Transform plasmaMuzzle = body != null
                ? NebulaVisualResolver.FindAnchor(body.transform, "anchor.projectile.spawn")
                : null;
            Nebula.Visuals.DefenseFireSignal.Raise(Combat.WeaponFamily.Plasma,
                plasmaMuzzle != null ? plasmaMuzzle.position : transform.position, direction,
                Nebula.Visuals.DefenseFireOwner.PlanetaryStructure);
            plasmaFireCooldownRemaining = Combat.ProjectileCatalog.PlanetaryPlasmaFireIntervalSeconds;
        }

        private static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            if (Physics.Raycast(from, delta.normalized, out RaycastHit hit, distance))
                if (hit.collider.GetComponentInParent<CelestialBody>() != null) return false;
            return true;
        }

        /// <summary>VS-014 §2: ticks the in-flight tier upgrade (if any) — Full-system-lifetime only, exactly like Runtime.Tick above, simply by virtue of only running while this GameObject exists.</summary>
        private void TickUpgradeJob(float dt)
        {
            if (ActiveUpgradeJob == null) return;
            if (ActiveUpgradeJob.Tick(dt)) CompleteUpgradeJob();
        }

        private LogisticsCargo pendingUpgradeCargo;

        public StructureUpgradeRejectReason StartUpgrade(int characterLevel, int planetCommandTier, int weaponCoordinationTier, bool hasAnyControlledPlanet, LogisticsCargo cargo)
        {
            StructureUpgradeRejectReason reason = StructureUpgradeService.CheckStart(this, characterLevel, planetCommandTier, weaponCoordinationTier, hasAnyControlledPlanet, cargo, out _);
            if (reason != StructureUpgradeRejectReason.None) return reason;
            StructureUpgradeJob job = StructureUpgradeService.TryStart(this, characterLevel, planetCommandTier, weaponCoordinationTier, hasAnyControlledPlanet, cargo);
            if (job == null) return StructureUpgradeRejectReason.InsufficientResources;
            ActiveUpgradeJob = job;
            pendingUpgradeCargo = cargo;
            return StructureUpgradeRejectReason.None;
        }

        private void CompleteUpgradeJob()
        {
            StructureUpgradeJob job = ActiveUpgradeJob;
            ActiveUpgradeJob = null;
            StructureUpgradeService.Complete(this, job, pendingUpgradeCargo);
            pendingUpgradeCargo = null;
        }

        /// <summary>VS-015 §5/§10: restores this Command Center's Maintenance Reserve contents/policy/active-target exactly as saved. No-op on a non-Command structure (MaintenanceReserve is null there).</summary>
        public void RestoreMaintenanceReserve(MaintenancePolicy policy, string activeEngineerTargetId, List<string> queuedTargetIds, List<(string itemId, float amount)> items)
        {
            if (MaintenanceReserve == null) return;
            MaintenanceReserve.SetPolicy(policy);
            MaintenanceReserve.TrySetActiveTarget(activeEngineerTargetId);
            if (queuedTargetIds != null) foreach (string id in queuedTargetIds) MaintenanceReserve.Enqueue(id);
            if (items != null) foreach (var (itemId, amount) in items) MaintenanceReserve.Add(itemId, amount);
        }

        /// <summary>75% refund of the still-unconsumed upgrade reservation only — the structure's CURRENT tier/KZG is never touched, never a duplicate/no-op if called twice.</summary>
        public void CancelUpgrade(LogisticsCargo cargo)
        {
            if (ActiveUpgradeJob == null) return;
            ActiveUpgradeJob = null;
            StructureUpgradeService.Cancel(this, cargo);
            pendingUpgradeCargo = null;
        }

        /// <summary>Restores an in-flight upgrade and re-derives the original full reservation. Progress changes time remaining, never price: the start path reserved the full recipe and completion consumes it exactly once.</summary>
        public void RestoreUpgradeJob(int targetTier, float progress01, LogisticsCargo cargo)
        {
            StructureTierDef tierDef = PlanetaryDefenseCatalog.Get(Type, targetTier);
            if (tierDef == null) return;
            float remainingFraction = 1f - Mathf.Clamp01(progress01);
            ActiveUpgradeJob = new StructureUpgradeJob(Type, targetTier, tierDef.BuildSeconds, tierDef.BuildSeconds * remainingFraction);
            pendingUpgradeCargo = cargo;
            if (cargo == null || !cargo.TrySetReservation(StructureUpgradeService.OwnerId(this), tierDef.FullCost()))
            {
                ActiveUpgradeJob = null;
                pendingUpgradeCargo = null;
            }
        }

        /// <summary>VS-015.1 §6: cancels a not-yet-active structure — floor(75%) of EVERY reserved item (parts and every material) is refunded together via the single atomic reservation, never just the parts. The Quick Asset Package flat-refund variant instead adds its pre-computed amounts directly (its own consumption already happened elsewhere).</summary>
        public void Cancel(LogisticsCargo cargo)
        {
            if (Runtime.CanCancelForRefund && cargo != null)
            {
                if (flatRefund != null) foreach (var (itemId, amount) in flatRefund) { if (amount > 0f) cargo.Add(itemId, amount); }
                else cargo.ConsumeReservationForRefund(BuildOwnerId, SurfaceStructureCatalog.RefundFraction);
            }
            buildFullCost.Clear();
            DestroyNow(gameObject);
        }

        private void BuildVisuals(SurfaceStructureDef def)
        {
            float unit = Mathf.Max(35f, planet.Body.Radius * 0.06f);
            BuildBodyVisual(def, unit);
            body.SetActive(false);

            float stateEnvelope = unit * 2.6f;
            constructionStateVisual = NebulaVisualResolver.Instantiate(
                NebulaVisualCatalog.StructureConstruction, transform, stateEnvelope,
                "Authored Construction State");
            damageStateVisual = NebulaVisualResolver.Instantiate(
                NebulaVisualCatalog.StructureDamageDisabled, transform, stateEnvelope,
                "Authored Damage/Disabled State");
            repairUpgradeStateVisual = NebulaVisualResolver.Instantiate(
                NebulaVisualCatalog.StructureRepairUpgrade, transform, stateEnvelope,
                "Authored Repair/Upgrade State");

            if (constructionStateVisual == null)
            {
                constructionGlow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                constructionGlow.name = "Construction Glow (Fallback)";
                DestroyNow(constructionGlow.GetComponent<Collider>());
                constructionGlow.transform.SetParent(transform, false);
                constructionGlow.transform.localScale = Vector3.one * (unit * 3f);
                constructionGlow.AddComponent<Billboard>();
                var glowRenderer = constructionGlow.GetComponent<MeshRenderer>();
                glowRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(
                    new Color(0.35f, 0.95f, 1f, 0.4f), MaterialLibrary.RadialGlowTexture);
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                constructionGlow.AddComponent<BeaconPulse>().Configure(1.1f, 0.4f);
            }
            if (constructionStateVisual != null) constructionStateVisual.SetActive(false);
            if (damageStateVisual != null) damageStateVisual.SetActive(false);
            if (repairUpgradeStateVisual != null) repairUpgradeStateVisual.SetActive(false);
            lastDurabilityTotal = DurabilityTotal();

            // VS-009 §5/§6: shared Command/Defense/Support adapter hook — grounds a Water/Gas-anchored structure visually.
            SurfaceAnchorAdapter.CreatePlatform(transform, anchorCategory, unit);
        }

        private void RefreshStateVisuals(float dt)
        {
            bool constructing = Runtime.State == DrillLifecycle.CapsuleInTransit
                                || Runtime.State == DrillLifecycle.Constructing
                                || Runtime.State == DrillLifecycle.Damaged;
            if (constructionStateVisual != null) constructionStateVisual.SetActive(constructing);

            float total = DurabilityTotal();
            if (lastDurabilityTotal >= 0f && total > lastDurabilityTotal + 0.001f)
                repairVisualHold = 0.35f;
            else
                repairVisualHold = Mathf.Max(0f, repairVisualHold - dt);
            lastDurabilityTotal = total;

            float max = Runtime.Durability.ShieldMax + Runtime.Durability.ArmorMax + Runtime.Durability.HullMax;
            bool visiblyDamaged = Runtime.IsDisabled || (max > 0f && total / max < 0.65f);
            if (damageStateVisual != null) damageStateVisual.SetActive(visiblyDamaged);
            if (repairUpgradeStateVisual != null)
                repairUpgradeStateVisual.SetActive(ActiveUpgradeJob != null || repairVisualHold > 0f);
        }

        private float DurabilityTotal() =>
            Runtime?.Durability == null ? 0f
            : Runtime.Durability.Shield + Runtime.Durability.Armor + Runtime.Durability.Hull;

        private void RefreshBodyVisualIfNeeded()
        {
            int tier = Runtime.Tier >= 2 ? 2 : 1;
            if (body != null && visualTier == tier) return;
            if (body != null) NebulaVisualResolver.DestroyInstance(body);
            BuildBodyVisual(SurfaceStructureCatalog.Get(Type), Mathf.Max(35f, planet.Body.Radius * 0.06f));
        }

        private void BuildBodyVisual(SurfaceStructureDef def, float unit)
        {
            visualTier = Runtime.Tier >= 2 ? 2 : 1;
            if (NebulaVisualCatalog.TryGetStructure(Type, visualTier, out NebulaVisualDefinition definition))
                body = NebulaVisualResolver.Instantiate(
                    definition, transform, instanceName: $"Authored {def.Name} T{visualTier} Visual");

            if (body != null) return;

            body = GameObject.CreatePrimitive(def.Type == SurfaceStructureType.Command
                ? PrimitiveType.Cube : PrimitiveType.Capsule);
            body.name = def.Name + " Visual (Fallback)";
            DestroyNow(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localScale = def.Type == SurfaceStructureType.Command
                ? new Vector3(unit * 1.6f, unit, unit * 1.6f)
                : new Vector3(unit * 0.6f, unit * 1.4f, unit * 0.6f);
            Color color = def.Type == SurfaceStructureType.Command
                ? new Color(0.9f, 0.8f, 0.4f) : new Color(1f, 0.4f, 0.35f);
            body.GetComponent<MeshRenderer>().sharedMaterial =
                MaterialLibrary.CreateLit(color, 0.5f, 0.5f);
        }

        private Vector3 SurfacePoint()
        {
            Vector3 worldNormal = planet.Body.transform.rotation * localNormal;
            return planet.Body.transform.position + worldNormal * (planet.Body.Radius + surfaceHeight);
        }

        /// <summary>
        /// <c>Destroy</c> is deferred and simply errors outside play mode, which
        /// makes every visual this class builds unusable from an EditMode test --
        /// the construction glow strips its own collider on the main build path,
        /// so merely creating the structure logged an error and failed the test.
        /// Same shape as <c>VehicleAgent.DestroyNow</c> and the shell's.
        /// </summary>
        private static void DestroyNow(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

    }
}
