using System.Collections.Generic;

namespace Nebula.Content
{
    /// <summary>
    /// TF-003: the seam between "where content definitions come from" and
    /// "the validated immutable snapshot gameplay reads" (<see cref="RuntimeCatalog"/>).
    /// <see cref="CSharpContentCatalog"/> is the only implementation this slice
    /// (reads the existing static C# catalogs); a future ScriptableObject-backed
    /// implementation would satisfy this same interface with zero change to
    /// <see cref="RuntimeCatalogBuilder"/>, <see cref="ContentCatalogValidator"/>,
    /// or any gameplay consumer. Every list may be empty (an unimplemented
    /// domain) but never null.
    /// </summary>
    public interface IContentCatalog
    {
        IReadOnlyList<ContentItem> Items { get; }
        IReadOnlyList<ContentRecipe> Recipes { get; }
        IReadOnlyList<ContentModule> Modules { get; }
        IReadOnlyList<ContentVehicle> Vehicles { get; }
        IReadOnlyList<ContentStructure> Structures { get; }
        IReadOnlyList<ContentLogisticsCenterTier> LogisticsCenterTiers { get; }
        IReadOnlyList<ContentWeapon> Weapons { get; }
        IReadOnlyList<ContentEnemy> Enemies { get; }
        IReadOnlyList<ContentPoiType> PoiTypes { get; }

        /// <summary>The module ids granted for free at T1 on a new campaign — exposed raw (not folded into ContentModule.FreeStarting alone) so the validator can independently catch an id here that doesn't correspond to any real module.</summary>
        IReadOnlyList<string> FreeStartingModuleIds { get; }
    }
}
