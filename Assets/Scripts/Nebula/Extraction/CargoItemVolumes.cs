using System.Collections.Generic;

namespace Nebula.Extraction
{
    /// <summary>
    /// Per-unit cargo volume for every known item id (VS-005 section 3): raw
    /// resources and part.basic are 1, the five processed materials are 2,
    /// energy.nuclear is 1, core.nuclear is 10. Unknown ids default to 1
    /// (safe fallback — every real item is registered below).
    /// </summary>
    public static class CargoItemVolumes
    {
        private static readonly Dictionary<string, float> volumeById = Build();

        public static float PerUnit(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 1f;
            return volumeById.TryGetValue(itemId, out float v) ? v : 1f;
        }

        private static Dictionary<string, float> Build()
        {
            var map = new Dictionary<string, float>();
            foreach (string rawId in ExtractionCatalog.ResourceIds) map[rawId] = 1f;
            map[ProcessingCatalog.BasicPartId] = 1f;
            foreach (string materialId in ProcessingCatalog.MaterialIds) map[materialId] = 2f;
            map[Modules.ModuleCatalog.EnergyNuclearId] = 1f;
            map[Modules.ModuleCatalog.CoreNuclearId] = 10f;
            return map;
        }
    }
}
