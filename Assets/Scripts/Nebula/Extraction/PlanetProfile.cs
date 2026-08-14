using System.Collections.Generic;
using UnityEngine;
using Nebula.Sectors;

namespace Nebula.Extraction
{
    /// <summary>One of the 12 canonical surface points on a planet.</summary>
    public struct SurfaceNode
    {
        public string Id;
        public SurfaceNodeKind Kind;
        public Vector3 LocalNormal; // unit direction in planet-local space
    }

    /// <summary>
    /// Deterministic, lazily-built resource + surface data for one planet —
    /// resolved ENTIRELY from its <see cref="Sectors.PlanetManifest"/> (VS-008
    /// §7 / VS-009 §1): there is no hash-based fallback here anymore. A
    /// worldId that has no manifest is a bug, not a case to paper over.
    /// </summary>
    public class PlanetProfile
    {
        public string WorldId { get; }
        public ResourceFamily Family { get; }
        public Rarity Rarity { get; private set; }
        public EnvironmentClass Environment { get; }
        public ulong SurfaceLayoutSeed { get; }
        public bool Scanned { get; set; }
        public bool RecommendedStart { get; set; }

        public float T1ProductionPerMinute => ExtractionCatalog.T1ProductionPerMinute(Rarity);
        public float T1StorageCapacity => ExtractionCatalog.T1StorageCapacity(Rarity);
        public float T1EfficiencyPercent => ExtractionCatalog.T1EfficiencyPercent(Rarity);

        public PlanetArchetype Archetype => PlanetArchetypeCatalog.ForFamily(Family);
        public SurfaceAnchorCategory AnchorCategory => PlanetArchetypeCatalog.AnchorCategory(Archetype);
        /// <summary>Total anchor distance from the planet center, as a multiple of planet radius (VS-009 §5: 1.01R solid / 1.03R water / 1.08R gas).</summary>
        public float AnchorRadiusMultiplier => PlanetArchetypeCatalog.AnchorRadiusMultiplier(AnchorCategory);

        private SurfaceNode[] nodes;

        /// <summary>
        /// VS-008 §7 / VS-009 §1 authority: every field comes straight from
        /// the deterministic <see cref="Sectors.PlanetManifest"/> — never
        /// re-hashed here. Exactly one Family, never a list/secondary field.
        /// </summary>
        public PlanetProfile(Sectors.PlanetManifest manifest)
        {
            WorldId = manifest.PlanetId;
            Family = manifest.Family;
            Rarity = manifest.Rarity;
            Environment = manifest.Environment;
            SurfaceLayoutSeed = manifest.SurfaceLayoutSeed;
        }

        /// <summary>Recommended start planets are clamped to Common/Uncommon so the first route is viable.</summary>
        public void ClampToStartFriendly()
        {
            if (Rarity > Rarity.Uncommon) Rarity = Rarity.Uncommon;
        }

        public IReadOnlyList<SurfaceNode> Nodes
        {
            get
            {
                if (nodes == null) nodes = BuildNodes();
                return nodes;
            }
        }

        public SurfaceNode DrillNode => FirstOfKind(SurfaceNodeKind.Drill);
        public SurfaceNode CommandNode => FirstOfKind(SurfaceNodeKind.Command);
        /// <summary>The first inner defense node — the one Command T1 unlocks.</summary>
        public SurfaceNode FirstDefenseNode => FirstOfKind(SurfaceNodeKind.Defense);

        /// <summary>VS-014: the Nth defense node (0 = the existing Laser slot, 1 = the new Plasma slot; 2-6 reserved/locked this slice). Nodes are built Drill, Command, then Defense(0..DefenseCount-1) in order, so index+2 is always the right slot.</summary>
        public SurfaceNode DefenseNode(int index) => Nodes[2 + Mathf.Clamp(index, 0, Sectors.SurfaceSlot.DefenseCount - 1)];

        /// <summary>VS-015 §7: the Nth support node (0 = Sensor Array slot, 1 = Maintenance Platform slot, 2 = hidden/L41+). Nodes are built Drill, Command, Defense(0..DefenseCount-1), then Support(0..SupportCount-1), so 2+DefenseCount+index is always the right slot.</summary>
        public SurfaceNode SupportNode(int index) => Nodes[2 + Sectors.SurfaceSlot.DefenseCount + Mathf.Clamp(index, 0, Sectors.SurfaceSlot.SupportCount - 1)];

        private SurfaceNode FirstOfKind(SurfaceNodeKind kind)
        {
            var list = Nodes;
            for (int i = 0; i < list.Count; i++) if (list[i].Kind == kind) return list[i];
            return list[0];
        }

        /// <summary>
        /// 1 Drill + 1 Command + 7 Defense + 3 Support, on the canonical
        /// logical slots (VS-009 §5). Persistent id is
        /// "{WorldId}/node/{logicalSlotId}"; direction is derived ONLY from
        /// (SurfaceLayoutSeed, logicalSlotId) — never free placement, so a
        /// tier upgrade always stays on the same node.
        /// </summary>
        private SurfaceNode[] BuildNodes()
        {
            var result = new SurfaceNode[12];
            int index = 0;
            AddNode(result, ref index, SurfaceNodeKind.Drill, SurfaceSlot.Drill);
            AddNode(result, ref index, SurfaceNodeKind.Command, SurfaceSlot.Command);
            for (int i = 0; i < SurfaceSlot.DefenseCount; i++) AddNode(result, ref index, SurfaceNodeKind.Defense, SurfaceSlot.Defense(i));
            for (int i = 0; i < SurfaceSlot.SupportCount; i++) AddNode(result, ref index, SurfaceNodeKind.Support, SurfaceSlot.Support(i));
            return result;
        }

        private void AddNode(SurfaceNode[] target, ref int index, SurfaceNodeKind kind, string logicalSlotId)
        {
            // Fibonacci-sphere direction perturbed by the slot's own hash so the
            // spread is even yet unique per planet.
            float golden = (index + 0.5f) / 12f;
            float y = 1f - golden * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            ulong h = ExtractionCatalog.Hash(SurfaceLayoutSeed, logicalSlotId);
            float theta = ExtractionCatalog.Unit01(h) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius).normalized;
            target[index++] = new SurfaceNode { Id = WorldId + "/node/" + logicalSlotId, Kind = kind, LocalNormal = dir };
        }
    }
}
