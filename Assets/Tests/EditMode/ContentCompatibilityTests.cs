using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>TF-003 compatibility EditMode tests: save schema/generation version untouched, and content-lookup stability under a reordered catalog fixture. Pure — no scene, prefab, or Unity asset is loaded by any of these.</summary>
    public class ContentCompatibilityTests
    {
        [Test]
        public void SaveSchema_IsPinned()
        {
            // TF-003 itself never bumped this (still true) -- VS-014 legitimately raised 11 -> 12 afterward.
            Assert.AreEqual(15, SaveService.CurrentSchema, "the shipped save schema is 14; VS-014 took it to 12 and later slices carried it to 14 -- this pin is what stops an unrelated change moving it");
        }

        /// <summary>
        /// Old contract: this pinned 2, the generation version in force when TF-003 landed.
        /// Current production authority: <see cref="SectorManifestGenerator.GenerationVersion"/> = 3
        /// (Assets/Scripts/Nebula/Sectors/SectorManifestGenerator.cs) -- 9d33695 moved systems off the
        /// uniform 2x2x2 sub-cell grid onto the galaxy spiral arms, a deliberate world-generation change
        /// that regenerates every existing campaign's universe and was approved as such.
        /// Invariant still protected: TF-003's content-catalog work is not allowed to move that number.
        /// The literal is deliberate -- reading the constant back would assert nothing, so a bump has to
        /// break this test and be re-pinned in the same commit as the world-gen change that earned it.
        /// </summary>
        [Test]
        public void GenerationVersion_IsPinnedToThree()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion, "TF-003 must never bump generation version");
        }

        [Test]
        public void ReorderedCatalogFixture_ResolvesTheSameSaveFacingIds()
        {
            // Simulates "the underlying content source enumerates in a different order" (e.g. a future
            // ScriptableObject-backed IContentCatalog, or simply a different in-memory ordering) by wrapping the
            // real adapter's lists in reverse. Save files reference these ids as plain strings (VehicleDto.vehicleId,
            // ModuleTierDto.moduleId, ...) -- resolution through RuntimeCatalog must be by id, never by position.
            var real = new CSharpContentCatalog();
            var reordered = new ReorderedContentCatalog(real);

            RuntimeCatalog normal = RuntimeCatalogBuilder.Build(real);
            RuntimeCatalog shuffled = RuntimeCatalogBuilder.Build(reordered);

            ContentVehicle expectedVehicle = normal.GetRequiredVehicle(VehicleCatalog.ScoutId);
            ContentVehicle actualVehicle = shuffled.GetRequiredVehicle(VehicleCatalog.ScoutId);
            Assert.AreEqual(expectedVehicle.DisplayName, actualVehicle.DisplayName);
            Assert.AreEqual(expectedVehicle.Speed, actualVehicle.Speed, 0.001f);

            ContentModule expectedModule = normal.GetRequiredModule("industry.resource_processing");
            ContentModule actualModule = shuffled.GetRequiredModule("industry.resource_processing");
            Assert.AreEqual(expectedModule.UnlockLevel, actualModule.UnlockLevel);
            Assert.AreEqual(expectedModule.TierCosts.Count, actualModule.TierCosts.Count);
        }

        private sealed class ReorderedContentCatalog : IContentCatalog
        {
            private readonly CSharpContentCatalog inner;
            public ReorderedContentCatalog(CSharpContentCatalog inner) { this.inner = inner; }

            public IReadOnlyList<ContentItem> Items => inner.Items.Reverse().ToList();
            public IReadOnlyList<ContentRecipe> Recipes => inner.Recipes.Reverse().ToList();
            public IReadOnlyList<ContentModule> Modules => inner.Modules.Reverse().ToList();
            public IReadOnlyList<ContentVehicle> Vehicles => inner.Vehicles.Reverse().ToList();
            public IReadOnlyList<ContentStructure> Structures => inner.Structures.Reverse().ToList();
            public IReadOnlyList<ContentLogisticsCenterTier> LogisticsCenterTiers => inner.LogisticsCenterTiers.Reverse().ToList();
            public IReadOnlyList<ContentWeapon> Weapons => inner.Weapons.Reverse().ToList();
            public IReadOnlyList<ContentEnemy> Enemies => inner.Enemies.Reverse().ToList();
            public IReadOnlyList<ContentPoiType> PoiTypes => inner.PoiTypes.Reverse().ToList();
            public IReadOnlyList<string> FreeStartingModuleIds => inner.FreeStartingModuleIds.Reverse().ToList();
        }
    }
}
