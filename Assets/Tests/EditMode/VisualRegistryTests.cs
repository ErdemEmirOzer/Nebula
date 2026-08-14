using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Content;

namespace Nebula.Tests.EditMode
{
    /// <summary>TF-003 EditMode tests for VisualRegistry — never a gameplay authority, works empty/partial, rejects duplicates and unknown gameplay ids.</summary>
    public class VisualRegistryTests
    {
        private static RuntimeCatalog OneItemCatalog(string id)
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem(id, "Test", 1f));
            return RuntimeCatalogBuilder.Build(fake);
        }

        [Test]
        public void MissingBinding_IsAllowed_TryGetReturnsFalse()
        {
            VisualRegistry registry = VisualRegistry.Empty();
            Assert.IsFalse(registry.TryGetBinding("item.anything", out VisualBinding binding));
            Assert.IsNull(binding);
        }

        [Test]
        public void PartialRegistry_WithSomeBindings_StillWorksForUnboundIds()
        {
            RuntimeCatalog catalog = OneItemCatalog("item.a");
            VisualRegistry registry = VisualRegistry.Build(new[] { new VisualBinding("item.a", "Resources/a") }, catalog, out List<string> errors);
            CollectionAssert.IsEmpty(errors);
            Assert.IsTrue(registry.TryGetBinding("item.a", out _));
            Assert.IsFalse(registry.TryGetBinding("item.unbound", out _));
        }

        [Test]
        public void DuplicateBindingId_IsRejected()
        {
            RuntimeCatalog catalog = OneItemCatalog("item.a");
            VisualRegistry.Build(new[] { new VisualBinding("item.a", "Resources/a1"), new VisualBinding("item.a", "Resources/a2") },
                catalog, out List<string> errors);
            Assert.IsTrue(errors.Exists(e => e.Contains("Duplicate visual binding")));
        }

        [Test]
        public void BindingForUnknownGameplayId_IsRejected()
        {
            RuntimeCatalog catalog = OneItemCatalog("item.a");
            VisualRegistry.Build(new[] { new VisualBinding("item.does_not_exist", "Resources/x") }, catalog, out List<string> errors);
            Assert.IsTrue(errors.Exists(e => e.Contains("does not correspond to any known gameplay content id")));
        }

        [Test]
        public void VisualBinding_CarriesNoGameplayStatOrSaveState()
        {
            // A VisualBinding has exactly two properties: the content id it maps and a resource path -- structurally
            // incapable of carrying a stat/cost/gate/durability/save field (this test pins that shape down).
            var properties = typeof(VisualBinding).GetProperties();
            Assert.AreEqual(2, properties.Length);
            var names = new HashSet<string> { properties[0].Name, properties[1].Name };
            CollectionAssert.AreEquivalent(new[] { "ContentId", "ResourcePath" }, names);
        }
    }
}
