using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nebula.Content
{
    /// <summary>
    /// TF-003: the single immutable, validated content snapshot every gameplay
    /// consumer reads from — built exactly once at boot by
    /// <see cref="RuntimeCatalogBuilder"/> from an <see cref="IContentCatalog"/>
    /// source and never mutated afterward. Every domain is exposed only
    /// through <see cref="ReadOnlyDictionary{TKey,TValue}"/>/<see cref="ReadOnlyCollection{T}"/>
    /// wrapping a PRIVATE backing collection this class alone constructed —
    /// a caller can never reach the private array/dictionary underneath, so
    /// there is no cast-based mutation path back into it. Lookups always use
    /// <see cref="StringComparer.Ordinal"/> — content ids are stable ASCII
    /// strings, never culture-sensitive.
    /// </summary>
    public sealed class RuntimeCatalog
    {
        private readonly ReadOnlyDictionary<string, ContentItem> items;
        private readonly ReadOnlyDictionary<string, ContentRecipe> recipes;
        private readonly ReadOnlyDictionary<string, ContentModule> modules;
        private readonly ReadOnlyDictionary<string, ContentVehicle> vehicles;
        private readonly ReadOnlyDictionary<string, ContentStructure> structures;
        private readonly ReadOnlyDictionary<string, ContentLogisticsCenterTier> logisticsCenterTiers;
        private readonly ReadOnlyDictionary<string, ContentWeapon> weapons;
        private readonly ReadOnlyDictionary<string, ContentEnemy> enemies;
        private readonly ReadOnlyDictionary<string, ContentPoiType> poiTypes;

        public IReadOnlyDictionary<string, ContentItem> Items => items;
        public IReadOnlyDictionary<string, ContentRecipe> Recipes => recipes;
        public IReadOnlyDictionary<string, ContentModule> Modules => modules;
        public IReadOnlyDictionary<string, ContentVehicle> Vehicles => vehicles;
        public IReadOnlyDictionary<string, ContentStructure> Structures => structures;
        public IReadOnlyDictionary<string, ContentLogisticsCenterTier> LogisticsCenterTiers => logisticsCenterTiers;
        public IReadOnlyDictionary<string, ContentWeapon> Weapons => weapons;
        public IReadOnlyDictionary<string, ContentEnemy> Enemies => enemies;
        public IReadOnlyDictionary<string, ContentPoiType> PoiTypes => poiTypes;

        internal RuntimeCatalog(
            IEnumerable<ContentItem> items, IEnumerable<ContentRecipe> recipes, IEnumerable<ContentModule> modules,
            IEnumerable<ContentVehicle> vehicles, IEnumerable<ContentStructure> structures,
            IEnumerable<ContentLogisticsCenterTier> logisticsCenterTiers, IEnumerable<ContentWeapon> weapons,
            IEnumerable<ContentEnemy> enemies, IEnumerable<ContentPoiType> poiTypes)
        {
            this.items = ToReadOnly(items, i => i.Id);
            this.recipes = ToReadOnly(recipes, r => r.Id);
            this.modules = ToReadOnly(modules, m => m.Id);
            this.vehicles = ToReadOnly(vehicles, v => v.Id);
            this.structures = ToReadOnly(structures, s => s.Id);
            this.logisticsCenterTiers = ToReadOnly(logisticsCenterTiers, l => l.Id);
            this.weapons = ToReadOnly(weapons, w => w.Id);
            this.enemies = ToReadOnly(enemies, e => e.Id);
            this.poiTypes = ToReadOnly(poiTypes, p => p.Id);
        }

        private static ReadOnlyDictionary<string, T> ToReadOnly<T>(IEnumerable<T> source, Func<T, string> keyOf)
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T value in source) map[keyOf(value)] = value; // last-write-wins; ContentCatalogValidator is what rejects real duplicates
            return new ReadOnlyDictionary<string, T>(map);
        }

        // --- TryGet / GetRequired, one pair per domain ---------------------------

        public bool TryGetItem(string id, out ContentItem value) => items.TryGetValue(id ?? string.Empty, out value);
        public ContentItem GetRequiredItem(string id) => Require(items, id, "item");

        public bool TryGetRecipe(string id, out ContentRecipe value) => recipes.TryGetValue(id ?? string.Empty, out value);
        public ContentRecipe GetRequiredRecipe(string id) => Require(recipes, id, "recipe");

        public bool TryGetModule(string id, out ContentModule value) => modules.TryGetValue(id ?? string.Empty, out value);
        public ContentModule GetRequiredModule(string id) => Require(modules, id, "module");

        public bool TryGetVehicle(string id, out ContentVehicle value) => vehicles.TryGetValue(id ?? string.Empty, out value);
        public ContentVehicle GetRequiredVehicle(string id) => Require(vehicles, id, "vehicle");

        public bool TryGetStructure(string id, out ContentStructure value) => structures.TryGetValue(id ?? string.Empty, out value);
        public ContentStructure GetRequiredStructure(string id) => Require(structures, id, "structure");

        public bool TryGetLogisticsCenterTier(string id, out ContentLogisticsCenterTier value) => logisticsCenterTiers.TryGetValue(id ?? string.Empty, out value);
        public ContentLogisticsCenterTier GetRequiredLogisticsCenterTier(string id) => Require(logisticsCenterTiers, id, "logistics center tier");

        public bool TryGetWeapon(string id, out ContentWeapon value) => weapons.TryGetValue(id ?? string.Empty, out value);
        public ContentWeapon GetRequiredWeapon(string id) => Require(weapons, id, "weapon");

        public bool TryGetEnemy(string id, out ContentEnemy value) => enemies.TryGetValue(id ?? string.Empty, out value);
        public ContentEnemy GetRequiredEnemy(string id) => Require(enemies, id, "enemy");

        public bool TryGetPoiType(string id, out ContentPoiType value) => poiTypes.TryGetValue(id ?? string.Empty, out value);
        public ContentPoiType GetRequiredPoiType(string id) => Require(poiTypes, id, "POI type");

        /// <summary>True if the id belongs to ANY gameplay domain in this catalog — the membership check <see cref="VisualRegistry"/> uses to reject a binding for an unknown gameplay id, without VisualRegistry needing to know the shape of any individual domain.</summary>
        public bool ContainsContentId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return items.ContainsKey(id) || recipes.ContainsKey(id) || modules.ContainsKey(id) || vehicles.ContainsKey(id)
                || structures.ContainsKey(id) || logisticsCenterTiers.ContainsKey(id) || weapons.ContainsKey(id)
                || enemies.ContainsKey(id) || poiTypes.ContainsKey(id);
        }

        private static T Require<T>(IReadOnlyDictionary<string, T> map, string id, string domainLabel)
        {
            if (map.TryGetValue(id ?? string.Empty, out T value)) return value;
            throw new ContentValidationException($"Unknown required content: no {domainLabel} with id '{id}'.");
        }
    }

    /// <summary>Builds a <see cref="RuntimeCatalog"/> from an <see cref="IContentCatalog"/> source. Always copies every enumerable into its own new collection — never holds onto (or exposes) whatever collection the source enumerable happened to be backed by.</summary>
    public static class RuntimeCatalogBuilder
    {
        public static RuntimeCatalog Build(IContentCatalog source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new RuntimeCatalog(
                source.Items.ToList(), source.Recipes.ToList(), source.Modules.ToList(),
                source.Vehicles.ToList(), source.Structures.ToList(), source.LogisticsCenterTiers.ToList(),
                source.Weapons.ToList(), source.Enemies.ToList(), source.PoiTypes.ToList());
        }
    }
}
