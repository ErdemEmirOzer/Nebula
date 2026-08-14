using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nebula.Content
{
    /// <summary>
    /// TF-003: the concrete <see cref="IVisualRegistry"/> — a stable content id
    /// -> optional <see cref="VisualBinding"/> map. Works empty or partial by
    /// design (a missing binding is a normal, expected state this slice —
    /// every existing MonoBehaviour keeps building its own placeholder
    /// exactly as before; nothing here changes that). <see cref="Build"/>
    /// never throws; it returns whatever errors it found (duplicate binding
    /// id, or a binding for a gameplay id that doesn't exist in the given
    /// <see cref="RuntimeCatalog"/>) so the caller decides what to do with
    /// them, mirroring <see cref="ContentCatalogValidator"/>'s own contract.
    /// </summary>
    public sealed class VisualRegistry : IVisualRegistry
    {
        private readonly ReadOnlyDictionary<string, VisualBinding> bindings;

        private VisualRegistry(Dictionary<string, VisualBinding> bindings)
        {
            this.bindings = new ReadOnlyDictionary<string, VisualBinding>(bindings);
        }

        public bool TryGetBinding(string contentId, out VisualBinding binding) =>
            bindings.TryGetValue(contentId ?? string.Empty, out binding);

        public static VisualRegistry Build(IEnumerable<VisualBinding> entries, RuntimeCatalog gameplayCatalog, out List<string> errors)
        {
            errors = new List<string>();
            var map = new Dictionary<string, VisualBinding>(StringComparer.Ordinal);
            foreach (VisualBinding entry in entries ?? Array.Empty<VisualBinding>())
            {
                if (string.IsNullOrEmpty(entry.ContentId)) { errors.Add("Visual binding with empty content id."); continue; }
                if (map.ContainsKey(entry.ContentId)) { errors.Add($"Duplicate visual binding id: '{entry.ContentId}'."); continue; }
                if (gameplayCatalog != null && !gameplayCatalog.ContainsContentId(entry.ContentId))
                {
                    errors.Add($"Visual binding '{entry.ContentId}' does not correspond to any known gameplay content id.");
                    continue;
                }
                map[entry.ContentId] = entry;
            }
            return new VisualRegistry(map);
        }

        public static VisualRegistry Empty() => new VisualRegistry(new Dictionary<string, VisualBinding>(StringComparer.Ordinal));
    }
}
