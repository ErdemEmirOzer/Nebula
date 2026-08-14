using NUnit.Framework;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The four numbers a UI pass is never allowed to move.
    /// <para>
    /// Save schema, envelope default, generation version and content revision
    /// decide whether an existing campaign still loads and whether the galaxy
    /// regenerates underneath it. None of them has anything to do with how a
    /// screen looks, so a redesign that changes one has almost certainly changed
    /// it by accident — a stray formatter, a DTO field added "while I was in
    /// there", a catalog entry that felt cosmetic. The damage lands on the
    /// player's save, not in a screenshot, which is why it needs an assertion
    /// rather than a code review.
    /// </para>
    /// <para>
    /// This is a pin, not a policy: a real content or schema change bumps the
    /// constant and this file in the same commit, deliberately and visibly.
    /// </para>
    /// </summary>
    public class UiFinalVersionPinTests
    {
        [Test]
        public void SaveSchema_IsPinned()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema,
                "the UI pass must not move the save schema");
        }

        /// <summary>
        /// The envelope's own default is what an unversioned/legacy blob
        /// deserializes to. P2-SAVE-201 moved it from 13 to 0: at 13 a payload
        /// carrying no schemaVersion at all impersonated a historical v13 save
        /// and cleared every gate the loader has. 0 means "unknown", and
        /// <c>ExtractionDirector</c> refuses anything below 6.
        /// </summary>
        [Test]
        public void SaveEnvelope_DefaultsToUnknown()
        {
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion,
                "a fresh envelope's default schema is unknown, not a historical version and not CurrentSchema");
        }

        [Test]
        public void GenerationVersion_IsStillThree()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion,
                "moving this regenerates the galaxy under every existing campaign");
        }

        [Test]
        public void ContentRevision_IsStillThree()
        {
            Assert.AreEqual(3, ContentFingerprint.ContentRevision,
                "the content layer's shape did not change in a UI pass");
        }
    }
}
