namespace Nebula.Content
{
    /// <summary>
    /// TF-003: an optional stable-content-id -> visual-asset-reference
    /// mapping. Never a gameplay authority — carries no stat, cost, gate,
    /// durability, or save state, only what a presentation layer would need
    /// to find a prefab/resource (or fall back to a placeholder). This slice
    /// does not change how any existing placeholder/model is actually built
    /// (TrainingArea, SurfaceStructure, EnemyAgent, etc. keep building their
    /// own primitives exactly as before) — VisualBinding only defines the
    /// SHAPE a future real-asset pass would populate.
    /// </summary>
    public sealed class VisualBinding
    {
        public string ContentId { get; }
        /// <summary>A Resources-relative path or other asset reference — this slice's convention (PlayerShip.glb is NOT re-pathed by this type; it stays wired exactly where it already is).</summary>
        public string ResourcePath { get; }

        public VisualBinding(string contentId, string resourcePath)
        {
            ContentId = contentId;
            ResourcePath = resourcePath;
        }
    }
}
