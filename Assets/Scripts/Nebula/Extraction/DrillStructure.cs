using UnityEngine;
using Nebula.Visuals;

namespace Nebula.Extraction
{
    /// <summary>
    /// MonoBehaviour adapter for one built T1 drill: it drives a
    /// <see cref="DrillRuntime"/> (all rules live there) and follows its
    /// planet's surface without inheriting the scaled Planet transform —
    /// position = planetCenter + (planetRotation · localNormal) × (radius +
    /// height). A capsule primitive flies in during transit; a pulsing glow
    /// marks construction; the drill body appears once Active.
    /// </summary>
    public class DrillStructure : MonoBehaviour
    {
        public enum BuildSource { Kit, Parts }

        public string WorldId { get; private set; }
        public string NodeId { get; private set; }
        public DrillRuntime Runtime { get; private set; }
        public BuildSource Source { get; private set; }
        public int RefundParts { get; private set; }

        private PlanetIdentity planet;
        private Sectors.PlanetArchetype archetype;
        private Vector3 localNormal;
        private float surfaceHeight;
        private GameObject capsule;
        private Vector3 capsuleStart;
        private float capsuleTotal;
        private GameObject constructionGlow;
        private GameObject drillBody;
        private GameObject drillAdapter;
        private GameObject storedResourceVisual;
        private int shownVisualTier;
        private bool consumedKit;
        private bool wasActive;

        public static DrillStructure Create(PlanetIdentity planetIdentity, PlanetProfile profile,
            Vector3 shipPosition, BuildSource source, int refundParts)
        {
            var go = new GameObject($"Drill T1 ({planetIdentity.WorldId})");
            var drill = go.AddComponent<DrillStructure>();
            drill.Source = source;
            drill.RefundParts = refundParts;
            SurfaceNode node = profile.DrillNode;
            float capsuleSeconds = ComputeCapsuleSeconds(shipPosition, planetIdentity, node);
            drill.Setup(planetIdentity, profile, node, new DrillRuntime(profile, capsuleSeconds), shipPosition);
            return drill;
        }

        public static DrillStructure Restore(PlanetIdentity planetIdentity, PlanetProfile profile,
            DrillRuntime runtime, BuildSource source, int refundParts)
        {
            var go = new GameObject($"Drill T{runtime.Tier} ({planetIdentity.WorldId})");
            var drill = go.AddComponent<DrillStructure>();
            drill.Source = source;
            drill.RefundParts = refundParts;
            drill.Setup(planetIdentity, profile, profile.DrillNode, runtime, planetIdentity.Body.transform.position);
            if (runtime.State == DrillLifecycle.Active) drill.consumedKit = true;
            return drill;
        }

        private static float ComputeCapsuleSeconds(Vector3 shipPosition, PlanetIdentity planet, SurfaceNode node)
        {
            Vector3 surface = SurfacePoint(planet, node.LocalNormal, 0f);
            float distance = Vector3.Distance(shipPosition, surface);
            // Map a plausible orbital distance band to the 3-8s window.
            return Mathf.Clamp(3f + distance / (planet.Body.Radius * 4f) * 5f, 3f, 8f);
        }

        private void Setup(PlanetIdentity planetIdentity, PlanetProfile profile, SurfaceNode node,
            DrillRuntime runtime, Vector3 shipPosition)
        {
            planet = planetIdentity;
            WorldId = planetIdentity.WorldId;
            NodeId = node.Id;
            archetype = profile.Archetype;
            localNormal = node.LocalNormal;
            // VS-009 §5: total anchor distance is AnchorRadiusMultiplier * R (1.01 solid / 1.03 water / 1.08 gas).
            surfaceHeight = planet.Body.Radius * (profile.AnchorRadiusMultiplier - 1f);
            Runtime = runtime;
            capsuleTotal = Mathf.Max(0.01f, runtime.CapsuleRemaining);
            capsuleStart = shipPosition;

            // Seed from current state so a RESTORED active drill never awards.
            wasActive = runtime.State == DrillLifecycle.Active;
            transform.position = SurfacePoint(planet, localNormal, surfaceHeight);
            BuildVisuals();
            // LGT-04: presentation only — emission from the drill's own runtime
            // state. Adds no light and changes no production value.
            gameObject.AddComponent<Nebula.Visuals.StructureLightingPresenter>()
                .BindDrill(this, Nebula.Visuals.LightingQualityProfile.FromQualitySettings().Quality);
            // ANI-02: presentation only — poses the visual child from this
            // drill's own lifecycle. Adds no light, moves no gameplay root, and
            // changes no production, storage or build value.
            gameObject.AddComponent<Nebula.Visuals.DrillAnimationPresenter>().Bind(this, null);
            // ANI-05.1.1: the mechanisms this drill and its surface adapter
            // really carry. Rebinding is idempotent, so an upgrade or a rebuild
            // never leaves two presenters on one drill.
            AttachMechanicalPresenters();
            // ANI-02.1: the delivery capsule's visual descent. Reads this
            // drill's own CapsuleRemaining countdown and owns no transaction.
            gameObject.AddComponent<Nebula.Visuals.BuildCapsulePresenter>()
                .BindDrill(this, shipPosition, null);
            ExtractionState.RegisterDrill(this);
        }

        private void OnDestroy() => ExtractionState.UnregisterDrill(this);

        private void Update()
        {
            if (planet == null || planet.Body == null) { DestroyNow(gameObject); return; }

            // VS-017A.2 §B3: Tick returns true on the single tick a T1->T2 upgrade finishes.
            if (Runtime.Tick(Time.deltaTime)) CompleteUpgradeJob();

            // Only the first (kit-built) drill consumes the pioneer kit.
            if (Source == BuildSource.Kit && !consumedKit && Runtime.State == DrillLifecycle.Active)
            {
                consumedKit = true;
                ExtractionState.ConsumeKit();
            }

            // XP: first drill on this planet reaching Active (transition only).
            if (!wasActive && Runtime.State == DrillLifecycle.Active)
            {
                wasActive = true;
                Progression.ProgressionService.Award(
                    Progression.ProgressionEvents.DrillFirst(WorldId), Progression.ProgressionEvents.DrillXp);
            }

            Vector3 surface = SurfacePoint(planet, localNormal, surfaceHeight);
            transform.position = surface;

            bool building = Runtime.State == DrillLifecycle.CapsuleInTransit
                            || Runtime.State == DrillLifecycle.Constructing
                            || Runtime.State == DrillLifecycle.Damaged;

            if (capsule != null)
            {
                if (Runtime.State == DrillLifecycle.CapsuleInTransit)
                {
                    float p = 1f - Mathf.Clamp01(Runtime.CapsuleRemaining / capsuleTotal);
                    capsule.transform.position = Vector3.Lerp(capsuleStart, surface, p);
                }
                else
                {
                    DestroyNow(capsule);
                    capsule = null;
                }
            }

            if (constructionGlow != null && !building)
            {
                DestroyNow(constructionGlow);
                constructionGlow = null;
            }
            if (drillBody != null) drillBody.SetActive(Runtime.State == DrillLifecycle.Active);
            if (storedResourceVisual != null)
                storedResourceVisual.SetActive(Runtime.State == DrillLifecycle.Active && Runtime.StoredAmount > 0.0001f);
            if (shownVisualTier != Runtime.Tier) RefreshAuthoredDrillVisual();
        }

        // --- VS-017A.2 §B3: T1->T2 upgrade seam (mirrors SurfaceStructure's own) ------

        /// <summary>The cargo that funded the in-flight upgrade — held so the completing tick can consume exactly that reservation, exactly like SurfaceStructure's own pendingUpgradeCargo.</summary>
        private LogisticsCargo pendingUpgradeCargo;

        public DrillUpgradeJob ActiveUpgradeJob => Runtime.ActiveUpgradeJob;

        /// <summary>Starts the T1→T2 upgrade, or returns the typed reason it cannot start. The drill stays Active (but stops producing) throughout.</summary>
        public DrillUpgradeRejectReason StartUpgrade(int stationConstructionTier, LogisticsCargo cargo)
        {
            DrillUpgradeRejectReason reason = DrillUpgradeService.CheckStart(Runtime, stationConstructionTier, cargo);
            if (reason != DrillUpgradeRejectReason.None) return reason;
            DrillUpgradeJob job = DrillUpgradeService.TryStart(Runtime, WorldId, NodeId, stationConstructionTier, cargo);
            if (job == null) return DrillUpgradeRejectReason.InsufficientResources;
            Runtime.BeginUpgrade(job);
            pendingUpgradeCargo = cargo;
            return DrillUpgradeRejectReason.None;
        }

        private void CompleteUpgradeJob()
        {
            DrillUpgradeJob job = Runtime.ActiveUpgradeJob;
            DrillUpgradeService.Complete(Runtime, WorldId, NodeId, job, pendingUpgradeCargo);
            pendingUpgradeCargo = null;
            gameObject.name = $"Drill T{Runtime.Tier} ({WorldId})";
            RefreshAuthoredDrillVisual();
        }

        /// <summary>Cancels the in-flight upgrade with the project-wide floor(75%) refund of whatever is still reserved. The drill's current tier/storage/stored amount are untouched.</summary>
        public void CancelUpgrade(LogisticsCargo cargo)
        {
            if (Runtime.ActiveUpgradeJob == null) return;
            Runtime.AbortUpgrade();
            DrillUpgradeService.Cancel(WorldId, NodeId, cargo ?? pendingUpgradeCargo);
            pendingUpgradeCargo = null;
        }

        /// <summary>
        /// Load-time restore of an in-flight upgrade: re-establishes the reservation from the
        /// REAL remaining cost the save recorded. If those resources genuinely are not there
        /// any more the upgrade is stopped outright rather than continuing unfunded — an
        /// explicit, safe failure, never a free completion.
        /// </summary>
        public bool RestoreUpgrade(DrillUpgradeJob job, LogisticsCargo cargo, System.Collections.Generic.IReadOnlyList<(string itemId, float amount)> remainingCost)
        {
            if (job == null) return false;
            if (!DrillUpgradeService.TryRestoreReservation(WorldId, NodeId, cargo, remainingCost))
            {
                Runtime.AbortUpgrade();
                return false;
            }
            Runtime.RestoreUpgradeJob(job);
            pendingUpgradeCargo = cargo;
            return true;
        }

        /// <summary>Cancels a not-yet-active drill and refunds its build cost (kit, or 75% of the parts).</summary>
        public void Cancel(LogisticsCargo cargo)
        {
            if (Runtime.CanCancelForKitReturn)
            {
                if (Source == BuildSource.Kit) ExtractionState.ReturnKit();
                else if (cargo != null && RefundParts > 0) cargo.Add(ProcessingCatalog.BasicPartId, RefundParts);
            }
            DestroyNow(gameObject);
        }

        /// <summary>Five archetype-varied drill/platform visual hooks (VS-009 §6) — shape, proportions and material differ per archetype so each planet type reads distinctly even before a scan reveals its exact family/rarity in text.</summary>

        /// <summary>
        /// ANI-05.1.1: binds the authored mechanisms on the drill body and on
        /// whichever surface adapter this world really uses. Called again after
        /// a rebuild; existing presenters are replaced rather than added to.
        /// </summary>
        private void AttachMechanicalPresenters()
        {
            foreach (Nebula.Visuals.EconomyMechanicalPresenter existing
                     in GetComponents<Nebula.Visuals.EconomyMechanicalPresenter>())
                DestroyNow(existing);

            string drillVisualId = Runtime != null && Runtime.Tier >= 2
                ? "structure.drill.t2" : "structure.drill.t1";
            AttachMechanicalPresenter(drillVisualId, drillBody != null ? drillBody.transform : null);

            if (drillAdapter == null) return;
            Sectors.SurfaceAnchorCategory anchor =
                Sectors.PlanetArchetypeCatalog.AnchorCategory(archetype);
            string adapterVisualId =
                anchor == Sectors.SurfaceAnchorCategory.Water ? "structure.drill_adapter.ocean" :
                anchor == Sectors.SurfaceAnchorCategory.Gas ? "structure.drill_adapter.gas" :
                "structure.drill_adapter.solid";
            AttachMechanicalPresenter(adapterVisualId, drillAdapter.transform);
        }

        private void AttachMechanicalPresenter(string visualId, Transform visualRoot)
        {
            if (visualRoot == null || !Nebula.Visuals.EconomyMechanicalProfiles.HasRealMechanisms(visualId)) return;
            gameObject.AddComponent<Nebula.Visuals.EconomyMechanicalPresenter>()
                .Initialize(visualId, visualRoot, ResolveMechanicalDrive, ResolveMechanicalCadence);
        }

        /// <summary>ANI-05.1.1 presentation read: how hard this drill is really running.</summary>
        private Nebula.Visuals.MechanicalDrive ResolveMechanicalDrive()
        {
            if (Runtime == null) return Nebula.Visuals.MechanicalDrive.Stowed;
            if (Runtime.State != DrillLifecycle.Active) return Runtime.State == DrillLifecycle.Constructing
                ? Nebula.Visuals.MechanicalDrive.Stowed
                : Nebula.Visuals.MechanicalDrive.Disabled;
            return Runtime.IsFull
                ? Nebula.Visuals.MechanicalDrive.Holding
                : Nebula.Visuals.MechanicalDrive.Working;
        }

        /// <summary>ANI-05.1.1: cadence only — the family's character, never its rate.</summary>
        private float ResolveMechanicalCadence()
            => Runtime != null ? Nebula.Visuals.EconomyMechanicalProfiles.FamilyCadence(Runtime.Family) : 1f;

        private void BuildVisuals()
        {
            float unit = Mathf.Max(30f, planet.Body.Radius * 0.05f);

            (PrimitiveType shape, Vector3 scale, Color color, float smoothness, float metallic) = archetype switch
            {
                Sectors.PlanetArchetype.MetallicRocky => (PrimitiveType.Cylinder, new Vector3(unit, unit * 1.1f, unit), new Color(0.6f, 0.66f, 0.72f), 0.4f, 0.6f),
                Sectors.PlanetArchetype.Biomass => (PrimitiveType.Sphere, new Vector3(unit * 1.2f, unit * 0.9f, unit * 1.2f), new Color(0.35f, 0.6f, 0.3f), 0.1f, 0f),
                Sectors.PlanetArchetype.UpperAtmospherePlatform => (PrimitiveType.Cylinder, new Vector3(unit * 1.6f, unit * 0.35f, unit * 1.6f), new Color(0.6f, 0.85f, 0.85f), 0.5f, 0.1f),
                Sectors.PlanetArchetype.FloatingOceanic => (PrimitiveType.Cylinder, new Vector3(unit * 1.4f, unit * 0.5f, unit * 1.4f), new Color(0.3f, 0.55f, 0.85f), 0.75f, 0.2f),
                Sectors.PlanetArchetype.CrystalShell => (PrimitiveType.Cube, new Vector3(unit * 0.9f, unit * 1.3f, unit * 0.9f), new Color(0.8f, 0.65f, 0.95f), 0.85f, 0.3f),
                _ => (PrimitiveType.Cylinder, new Vector3(unit, unit * 1.1f, unit), new Color(0.6f, 0.66f, 0.72f), 0.4f, 0.6f)
            };

            drillBody = NebulaVisualResolver.Instantiate(
                NebulaVisualCatalog.DrillForTier(Runtime.Tier), transform,
                instanceName: $"Authored Drill T{Runtime.Tier} Visual");
            shownVisualTier = Runtime.Tier;
            if (drillBody == null)
            {
                drillBody = GameObject.CreatePrimitive(shape);
                drillBody.name = "Drill Body (Fallback)";
                DestroyNow(drillBody.GetComponent<Collider>());
                drillBody.transform.SetParent(transform, false);
                drillBody.transform.localScale = scale;
                drillBody.GetComponent<MeshRenderer>().sharedMaterial =
                    MaterialLibrary.CreateLit(color, smoothness, metallic);
            }
            drillBody.SetActive(false);

            Sectors.SurfaceAnchorCategory anchor =
                Sectors.PlanetArchetypeCatalog.AnchorCategory(archetype);
            if (NebulaVisualCatalog.TryGetDrillAdapter(anchor, out NebulaVisualDefinition adapterDefinition))
                drillAdapter = NebulaVisualResolver.Instantiate(
                    adapterDefinition, transform, instanceName: "Authored Drill Adapter");
            if (drillAdapter == null)
                SurfaceAnchorAdapter.CreatePlatform(transform, anchor, unit);

            string resourceId = ExtractionCatalog.ResourceIds[(int)Runtime.Family];
            storedResourceVisual = NebulaVisualResolver.InstantiateItem(
                resourceId, transform, "Stored Resource Visual");
            if (storedResourceVisual != null)
            {
                storedResourceVisual.transform.localPosition = Vector3.right * (unit * 0.7f);
                storedResourceVisual.SetActive(false);
            }

            constructionGlow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            constructionGlow.name = "Construction Glow";
            DestroyNow(constructionGlow.GetComponent<Collider>());
            constructionGlow.transform.SetParent(transform, false);
            constructionGlow.transform.localScale = Vector3.one * (unit * 3f);
            constructionGlow.AddComponent<Billboard>();
            var glowRenderer = constructionGlow.GetComponent<MeshRenderer>();
            glowRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(
                new Color(0.35f, 0.95f, 1f, 0.4f),
                NebulaProducedVisualResolver.VfxTexture(NebulaCommonVfx.BuildHologram));
            glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            constructionGlow.AddComponent<BeaconPulse>().Configure(1.1f, 0.4f);

            capsule = new GameObject("Build Capsule");
            capsule.name = "Build Capsule";
            capsule.transform.position = capsuleStart;
            NebulaVisualDefinition deliveryVisual = NebulaVisualCatalog.BuildCapsule;
            if (Source == BuildSource.Kit
                && NebulaVisualCatalog.TryGetItem(
                    ExtractionCatalog.BuildKitId, out NebulaVisualDefinition pioneerKit))
                deliveryVisual = pioneerKit;
            if (NebulaVisualResolver.Instantiate(
                    deliveryVisual, capsule.transform,
                    instanceName: Source == BuildSource.Kit
                        ? "Authored Universal Pioneer Build Kit"
                        : "Authored Build Capsule") == null)
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fallback.name = "Build Capsule Visual (Fallback)";
                DestroyNow(fallback.GetComponent<Collider>());
                fallback.transform.SetParent(capsule.transform, false);
                fallback.transform.localScale = Vector3.one * (unit * 0.7f);
                fallback.GetComponent<MeshRenderer>().sharedMaterial =
                    MaterialLibrary.CreateUnlit(new Color(0.6f, 0.85f, 1f));
            }
        }

        private void RefreshAuthoredDrillVisual()
        {
            GameObject replacement = NebulaVisualResolver.Instantiate(
                NebulaVisualCatalog.DrillForTier(Runtime.Tier), transform,
                instanceName: $"Authored Drill T{Runtime.Tier} Visual");
            if (replacement == null) return;
            if (drillBody != null) NebulaVisualResolver.DestroyInstance(drillBody);
            drillBody = replacement;
            shownVisualTier = Runtime.Tier;
            drillBody.SetActive(Runtime.State == DrillLifecycle.Active);
        }

        private static Vector3 SurfacePoint(PlanetIdentity planet, Vector3 local, float height)
        {
            Vector3 worldNormal = planet.Body.transform.rotation * local;
            return planet.Body.transform.position + worldNormal * (planet.Body.Radius + height);
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
