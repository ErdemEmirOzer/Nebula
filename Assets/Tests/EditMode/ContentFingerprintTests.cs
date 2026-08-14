using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Nebula.Content;

namespace Nebula.Tests.EditMode
{
    /// <summary>TF-003 EditMode tests for ContentFingerprint's determinism, order-independence, and culture-independence.</summary>
    public class ContentFingerprintTests
    {
        private static RuntimeCatalog CatalogWithOneItem(string id, float volume) =>
            RuntimeCatalogBuilder.Build(OneItemCatalog(id, volume));

        private static FakeContentCatalog OneItemCatalog(string id, float volume)
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem(id, "Test", volume));
            return fake;
        }

        [Test]
        public void ContentRevision_IsExactly3()
        {
            // VS-015 §2: bumped 2 -> 3 for Engineer/Escort Mk I-II vehicles and Sensor Array/Maintenance Platform T1-T2 structures.
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        [Test]
        public void SameContent_ProducesTheSameFingerprint()
        {
            ulong first = ContentFingerprint.Compute(CatalogWithOneItem("item.a", 1f));
            ulong second = ContentFingerprint.Compute(CatalogWithOneItem("item.a", 1f));
            Assert.AreEqual(first, second);
        }

        [Test]
        public void InsertionOrder_NeverAffectsTheFingerprint()
        {
            var fakeA = new FakeContentCatalog();
            fakeA.Items.Add(new ContentItem("item.a", "A", 1f));
            fakeA.Items.Add(new ContentItem("item.b", "B", 2f));

            var fakeB = new FakeContentCatalog();
            fakeB.Items.Add(new ContentItem("item.b", "B", 2f));
            fakeB.Items.Add(new ContentItem("item.a", "A", 1f));

            ulong first = ContentFingerprint.Compute(RuntimeCatalogBuilder.Build(fakeA));
            ulong second = ContentFingerprint.Compute(RuntimeCatalogBuilder.Build(fakeB));
            Assert.AreEqual(first, second);
        }

        [Test]
        public void ChangingAnAuthoritativeStat_ChangesTheFingerprint()
        {
            ulong before = ContentFingerprint.Compute(CatalogWithOneItem("item.a", 1f));
            ulong after = ContentFingerprint.Compute(CatalogWithOneItem("item.a", 2f));
            Assert.AreNotEqual(before, after);
        }

        [Test]
        public void RealCSharpCatalog_FingerprintIsStableAcrossRepeatedBuilds()
        {
            ulong first = ContentFingerprint.Compute(RuntimeCatalogBuilder.Build(new CSharpContentCatalog()));
            ulong second = ContentFingerprint.Compute(RuntimeCatalogBuilder.Build(new CSharpContentCatalog()));
            Assert.AreEqual(first, second);
        }

        [Test]
        public void VisualBindings_NeverAffectTheFingerprint()
        {
            RuntimeCatalog catalog = CatalogWithOneItem("item.a", 1f);
            ulong before = ContentFingerprint.Compute(catalog);

            // Constructing a VisualRegistry against the same catalog must have zero effect on the
            // gameplay-content fingerprint -- ContentFingerprint has no dependency on IVisualRegistry at all.
            VisualRegistry.Build(new[] { new VisualBinding("item.a", "Resources/whatever") }, catalog, out _);
            ulong after = ContentFingerprint.Compute(catalog);

            Assert.AreEqual(before, after);
        }

        [Test]
        public void CurrentCulture_NeverAffectsTheFingerprint()
        {
            RuntimeCatalog catalog = CatalogWithOneItem("item.a", 1.5f);
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                ulong invariant = ContentFingerprint.Compute(catalog);

                // Turkish culture formats a float's decimal separator as ',' instead of '.' by default --
                // exactly the kind of silent locale dependency the fingerprint must never have.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
                ulong turkish = ContentFingerprint.Compute(catalog);

                Assert.AreEqual(invariant, turkish);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }
}
