namespace Nebula.Extraction
{
    /// <summary>A deterministic, stable-ID processing recipe: N raw of one family → M of an output item.</summary>
    public class ProcessingRecipe
    {
        public string Id { get; }
        public string InputItemId { get; }      // a raw resource id
        public int InputCount { get; }
        public string OutputItemId { get; }
        public int OutputCount { get; }

        /// <summary>
        /// CONTENT IDENTITY ONLY — no longer a duration.
        /// <para>
        /// VS-017A.2 made conversions instant, so nothing reads this to time anything;
        /// the timer, the progress bar and the speed multiplier it fed are all gone.
        /// It is kept because <see cref="Content.ContentFingerprint"/> hashes it into
        /// the content revision, and dropping a field the fingerprint covers would
        /// change every catalog's identity for a value nobody spends.
        /// </para>
        /// </summary>
        public float Seconds { get; }

        public string DisplayName { get; }

        public ProcessingRecipe(string id, string inputItemId, int inputCount, string outputItemId,
            int outputCount, float seconds, string displayName)
        {
            Id = id; InputItemId = inputItemId; InputCount = inputCount;
            OutputItemId = outputItemId; OutputCount = outputCount; Seconds = seconds; DisplayName = displayName;
        }
    }

    /// <summary>
    /// Processing bench content: two recipes per raw family (a shared Basic Building
    /// Part, and a family-specific refined material). Code-driven with stable IDs,
    /// matching the extraction system's zero-asset style. No waste, no failure, no
    /// random quality.
    /// <para>
    /// This is the one authority on which conversions exist and at what ratio.
    /// <c>ProcessingBenchCommands</c> derives everything from it — what is
    /// processable, which recipe cards a loaded input offers, how much a given amount
    /// yields — so a recipe added here works everywhere with no other change. The
    /// former <c>TestTimeScale</c> is gone with the timers it scaled.
    /// </para>
    /// </summary>
    public static class ProcessingCatalog
    {
        public const string BasicPartId = "part.basic";
        public const string BasicPartName = "Basic Building Part";

        public static readonly string[] MaterialIds =
        {
            "material.metal_alloy", "material.biocomposite", "material.refined_gas",
            "material.purified_water", "material.industrial_diamond"
        };
        public static readonly string[] MaterialNames =
        {
            "Metal Alloy", "Bio-Composite", "Refined Gas", "Purified Water", "Industrial Diamond"
        };

        public static readonly ProcessingRecipe[] Recipes = BuildRecipes();

        private static ProcessingRecipe[] BuildRecipes()
        {
            var list = new ProcessingRecipe[ExtractionCatalog.ResourceIds.Length * 2];
            int k = 0;
            for (int f = 0; f < ExtractionCatalog.ResourceIds.Length; f++)
            {
                string rawId = ExtractionCatalog.ResourceIds[f];
                string rawName = ExtractionCatalog.ResourceNames[f];
                list[k++] = new ProcessingRecipe($"recipe.part.{f}", rawId, 2, BasicPartId, 1, 2f,
                    $"2 {rawName} -> 1 {BasicPartName}");
                list[k++] = new ProcessingRecipe($"recipe.material.{f}", rawId, 3, MaterialIds[f], 1, 4f,
                    $"3 {rawName} -> 1 {MaterialNames[f]}");
            }
            return list;
        }

        public static string ItemDisplayName(string itemId)
        {
            for (int i = 0; i < ExtractionCatalog.ResourceIds.Length; i++)
                if (ExtractionCatalog.ResourceIds[i] == itemId) return ExtractionCatalog.ResourceNames[i];
            if (itemId == BasicPartId) return BasicPartName;
            for (int i = 0; i < MaterialIds.Length; i++)
                if (MaterialIds[i] == itemId) return MaterialNames[i];
            return itemId;
        }
    }
}
