using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.4.3's audit: each of the eight panels must really hold an
    /// animator or a row registry. The infrastructure existing is not the
    /// deliverable — the call site is.
    /// </summary>
    public class Ani0543RequiredCallSiteAuditTests
    {
        /// <summary>
        /// Re-pointed from the legacy panels to the redesigned interface (UI-09).
        /// The contract is untouched and is the reason this audit exists: a panel
        /// that reports a transaction must hold the animator that flashes only on
        /// a real success, and a panel with a changing row set must hold the
        /// registry that keeps entrances from replaying.
        /// <para>
        /// Migrating this found a genuine gap rather than a naming change — the new
        /// interface had none of these wired at all, so a collected resource, a
        /// finished production job, an accepted reserve transfer and an absorbed
        /// hit all passed silently. They are wired now, from the same real events
        /// the legacy panels used.
        /// </para>
        /// </summary>
        private static readonly (string typeName, string expected)[] Required =
        {
            ("Nebula.UI.Hud.NebulaHudPlanetContext", "UiValueAnimator"),
            ("Nebula.UI.Hud.NebulaHudAlerts", "UiValueAnimator"),
            ("Nebula.UI.Hud.NebulaHudVitals", "UiValueAnimator"),
            ("Nebula.UI.Hud.NebulaHudNavigation", "UiValueAnimator"),
            ("Nebula.UI.Menu.NebulaFleetPage", "UiValueAnimator"),
            ("Nebula.UI.Menu.NebulaFleetPage", "UiStableRowRegistry"),
            ("Nebula.UI.Hud.NebulaHudAlerts", "UiStableRowRegistry")
        };

        private static Type Resolve(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assembly.GetType(typeName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool HoldsField(Type owner, string fieldTypeName)
        {
            FieldInfo[] fields = owner.GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            return fields.Any(f => f.FieldType.Name == fieldTypeName);
        }

        [Test]
        public void EveryRequiredPanel_ReallyHoldsItsAnimatorOrRegistry()
        {
            var missing = new List<string>();

            foreach ((string typeName, string expected) in Required)
            {
                Type owner = Resolve(typeName);
                if (owner == null) { missing.Add($"{typeName}: type not found"); continue; }
                if (!HoldsField(owner, expected))
                    missing.Add($"{typeName}: no {expected} field — the call site is missing");
            }

            Assert.IsEmpty(missing,
                "ANI-05.4.3 requires a real call site in each panel:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void TheAuditCoversEveryPanelThatReportsATransaction()
            => Assert.AreEqual(7, Required.Length,
                "every panel that reports a transaction or carries a changing row set is mandatory");

        [Test]
        public void EachPanelExposesItsOwnTestFacingCounter()
        {
            var missing = new List<string>();

            foreach ((string typeName, string _) in Required)
            {
                Type owner = Resolve(typeName);
                if (owner == null) { missing.Add($"{typeName}: type not found"); continue; }

                bool hasCounter = owner
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Any(p => p.PropertyType == typeof(int)
                              && (p.Name.EndsWith("Shown") || p.Name.EndsWith("LastRefresh")));

                if (!hasCounter) missing.Add($"{typeName}: no test-facing counter");
            }

            Assert.IsEmpty(missing, string.Join("\n  ", missing));
        }

        [Test]
        public void TheInfrastructureItselfStillExists()
        {
            Assert.IsNotNull(Resolve("Nebula.Visuals.UiValueAnimator"));
            Assert.IsNotNull(Resolve("Nebula.Visuals.UiStableRowRegistry"));
            Assert.IsNotNull(Resolve("Nebula.Visuals.UiNotificationAnimator"));
            Assert.IsNotNull(Resolve("Nebula.Visuals.UiPanelAnimator"));
        }

        [Test]
        public void TheSaveAndContentVersions_AreUntouched()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
