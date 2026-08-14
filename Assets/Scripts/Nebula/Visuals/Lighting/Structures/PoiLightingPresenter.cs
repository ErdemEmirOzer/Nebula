using System.Collections.Generic;
using UnityEngine;
using Nebula.Poi;

namespace Nebula.Visuals
{
    /// <summary>
    /// Lights one point of interest according to what the player is actually
    /// allowed to know about it.
    /// <para>
    /// The information gate is the whole point. At Signal and Located every POI
    /// emits the same dim, abstract contact colour, so flying up to a wreck and
    /// flying up to an anomaly look identical until the game itself identifies
    /// them. Lighting reads the knowledge level and never advances it — it
    /// grants no XP, resolves nothing and writes nothing back.
    /// </para>
    /// </summary>
    public class PoiLightingPresenter : EmissionPresenter
    {
        private static readonly List<PoiLightingPresenter> Live = new List<PoiLightingPresenter>();

        public static int LiveCount => Live.Count;

        private PoiObject poi;

        /// <summary>The knowledge level last resolved against. Test-facing.</summary>
        public PoiKnowledgeLevel CurrentLevel { get; private set; } = PoiKnowledgeLevel.Unknown;

        /// <summary>The POI's real type. Only meaningful to the presenter once the player has identified it.</summary>
        public PoiType Type => poi != null && poi.Manifest != null ? poi.Manifest.Type : PoiType.Anomaly;

        public void Bind(PoiObject poiObject, LightingQuality quality)
        {
            poi = poiObject;
            SetQuality(quality);
            RebuildRendererCache();
            Apply();
        }

        public static void ApplyQualityToAll(LightingQuality quality)
        {
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] != null) Live[i].SetQuality(quality);
        }

        private void LateUpdate() => Apply();

        protected override Color ResolveEmission()
        {
            if (poi == null || poi.Manifest == null || poi.State == null) return Color.black;

            CurrentLevel = poi.State.Level;

            (Color color, float strength) = StructureLightingProfiles.Poi(poi.Manifest.Type, poi.State.Level);

            bool warning = poi.Manifest.Type == PoiType.Protected
                           && poi.State.Level >= PoiKnowledgeLevel.Identified
                           && poi.State.Level != PoiKnowledgeLevel.Resolved;

            return StructureLightingProfiles.Resolve(color, strength, Quality, warning);
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        private void OnDestroy() => poi = null;
    }
}
