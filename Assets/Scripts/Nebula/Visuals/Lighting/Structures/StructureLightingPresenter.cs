using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;

namespace Nebula.Visuals
{
    /// <summary>
    /// Lights one built thing — a drill, a surface structure, or the Logistics
    /// Center — from its own runtime state. Reads that state and changes none
    /// of it: production rates, capacities, durability, upgrade jobs and save
    /// data all stay exactly where they were.
    /// <para>
    /// Adds no light. A planet covered in drills and defences costs the scene
    /// zero extra real-time lights; everything is emission answering the one
    /// global key light.
    /// </para>
    /// </summary>
    public class StructureLightingPresenter : EmissionPresenter
    {
        /// <summary>Every live presenter, so the director can push a quality change without anything scanning the scene.</summary>
        private static readonly List<StructureLightingPresenter> Live = new List<StructureLightingPresenter>();

        public static int LiveCount => Live.Count;

        private DrillStructure drill;
        private SurfaceStructure structure;
        private LogisticsCenterRuntime logistics;
        private System.Func<float> logisticsFill;

        /// <summary>The lifecycle state last resolved. Test-facing.</summary>
        public DrillLifecycle CurrentLifecycle { get; private set; } = DrillLifecycle.Planned;

        public void BindDrill(DrillStructure drillStructure, LightingQuality quality)
        {
            drill = drillStructure;
            SetQuality(quality);
            RebuildRendererCache();
            Apply();
        }

        public void BindStructure(SurfaceStructure surfaceStructure, LightingQuality quality)
        {
            structure = surfaceStructure;
            SetQuality(quality);
            RebuildRendererCache();
            Apply();
        }

        /// <summary>
        /// <paramref name="fillFraction"/> is a reader supplied by whoever owns
        /// the cargo — this presenter never opens an inventory itself and is
        /// never a second capacity authority.
        /// </summary>
        public void BindLogistics(LogisticsCenterRuntime runtime, System.Func<float> fillFraction, LightingQuality quality)
        {
            logistics = runtime;
            logisticsFill = fillFraction;
            SetQuality(quality);
            RebuildRendererCache();
            Apply();
        }

        /// <summary>Pushes a quality change to every live structure. Called by the lighting director, and only by it.</summary>
        public static void ApplyQualityToAll(LightingQuality quality)
        {
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] != null) Live[i].SetQuality(quality);
        }

        /// <summary>
        /// Re-resolves after a state change. Driven by <c>LateUpdate</c>, but
        /// the write itself is skipped whenever the resolved colour is
        /// unchanged, so a running drill costs one colour comparison a frame
        /// and nothing else.
        /// </summary>
        private void LateUpdate() => Apply();

        protected override Color ResolveEmission()
        {
            if (drill != null && drill.Runtime != null) return ResolveDrill(drill.Runtime);
            if (structure != null && structure.Runtime != null) return ResolveStructure(structure.Runtime);
            if (logistics != null) return ResolveLogistics(logistics);
            return Color.black;
        }

        private Color ResolveDrill(DrillRuntime runtime)
        {
            CurrentLifecycle = runtime.State;

            bool storageFull = runtime.StorageCapacity > 0f && runtime.StoredAmount >= runtime.StorageCapacity - 0.001f;
            bool upgrading = runtime.ActiveUpgradeJob != null;

            (Color color, float strength) = StructureLightingProfiles.Drill(
                runtime.Family, runtime.Tier, runtime.State, storageFull, upgrading);

            bool warning = StructureLightingProfiles.IsAlarmState(runtime.State) || storageFull;
            return StructureLightingProfiles.Resolve(color, strength, Quality, warning);
        }

        private Color ResolveStructure(StructureRuntime runtime)
        {
            CurrentLifecycle = runtime.State;

            (Color color, float strength) = StructureLightingProfiles.Structure(
                runtime.Type, runtime.Tier, runtime.State, combatReady: runtime.State == DrillLifecycle.Active);

            return StructureLightingProfiles.Resolve(color, strength, Quality,
                StructureLightingProfiles.IsAlarmState(runtime.State));
        }

        private Color ResolveLogistics(LogisticsCenterRuntime runtime)
        {
            float fill = logisticsFill != null ? Mathf.Clamp01(logisticsFill()) : 0f;

            (Color color, float strength) = StructureLightingProfiles.Logistics(
                runtime.Tier, runtime.State, fill, runtime.Upgrading,
                routeActive: false, dockOccupied: false);

            bool warning = fill >= StructureLightingProfiles.LogisticsWarningFill
                           || runtime.State == LogisticsCenterState.Disabled;
            return StructureLightingProfiles.Resolve(color, strength, Quality, warning);
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        private void OnDestroy()
        {
            drill = null;
            structure = null;
            logistics = null;
            logisticsFill = null;
        }
    }
}
