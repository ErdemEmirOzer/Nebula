using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.UI.StrategicMap;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Strategic map M2: the pure presentation state — breadcrumb, selection and the
    /// Escape hierarchy — plus the single-owner M key rule and the theme's
    /// accessibility behaviour.
    /// </summary>
    public class StrategicMapPresenterTests
    {
        private static StrategicMapSnapshot SnapshotWith(params string[] systemIds)
        {
            var systems = new List<StrategicSystemView>();
            foreach (string id in systemIds) systems.Add(SystemView(id, planetIds: new[] { id + "/pl00", id + "/pl01" }));
            return new StrategicMapSnapshot(systems, new List<StrategicLinkView>(), default, systemIds.Length > 0 ? systemIds[0] : null);
        }

        private static StrategicSystemView SystemView(string systemId, string[] planetIds)
        {
            var planets = new List<StrategicPlanetView>();
            foreach (string pid in planetIds)
                planets.Add(new StrategicPlanetView(pid, pid, 0, 1f, 1f, 0f, false, default, default, default,
                    default, DominanceState.Unowned, false, false, 1, 0f, null));

            return new StrategicSystemView(systemId, systemId, new SectorCoordinate(0, 0, 0),
                SystemKnowledgeLevel.Visited, SystemLinkState.Live, true, Vector3.zero, Vector3.forward,
                true, default, 1f, 1f, planets.Count, SystemOwnershipTag.Discovered, false, false, false, false,
                0f, planets, null);
        }

        // ==================== breadcrumb + hierarchy ====================

        [Test]
        public void FreshPresenter_StartsAtTheUniverseLevelWithNothingSelected()
        {
            var presenter = new StrategicMapPresenter();
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);
            Assert.IsNull(presenter.SelectedSystemId);
            Assert.IsNull(presenter.SelectedPlanetId);
            Assert.IsFalse(presenter.CardOpen);
        }

        [Test]
        public void SingleClick_SelectsWithoutDivingIntoTheSystem()
        {
            var presenter = new StrategicMapPresenter();
            presenter.SelectSystem("sys-a");

            Assert.AreEqual("sys-a", presenter.SelectedSystemId);
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level, "a single click must not change level");
        }

        [Test]
        public void EnterSystemThenPlanet_WalksTheHierarchyDown()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem("sys-a");
            Assert.AreEqual(StrategicMapLevel.System, presenter.Level);

            presenter.SelectPlanet("sys-a/pl00");
            Assert.AreEqual(StrategicMapLevel.Planet, presenter.Level);
            Assert.IsTrue(presenter.CardOpen);
        }

        [Test]
        public void Escape_WalksBackExactlyOneLevelPerPress_AndOnlyClosesAtTheTop()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem("sys-a");
            presenter.SelectPlanet("sys-a/pl00");

            Assert.IsTrue(presenter.StepBack(), "planet card -> system, map stays open");
            Assert.AreEqual(StrategicMapLevel.System, presenter.Level);
            Assert.IsNull(presenter.SelectedPlanetId);

            Assert.IsTrue(presenter.StepBack(), "system -> universe, map stays open");
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);

            Assert.IsFalse(presenter.StepBack(), "at the universe level Escape must close the map");
        }

        [Test]
        public void SelectingAPlanetWithoutASystem_IsIgnored()
        {
            var presenter = new StrategicMapPresenter();
            presenter.SelectPlanet("orphan/pl00");

            Assert.IsNull(presenter.SelectedPlanetId);
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);
        }

        [Test]
        public void Breadcrumb_ReadsUniverseSectorSystemPlanet_InTurkish()
        {
            var presenter = new StrategicMapPresenter();
            StrategicMapSnapshot snapshot = SnapshotWith("sys-a");

            Assert.AreEqual(new[] { "Evren" }, presenter.Breadcrumb(snapshot));

            presenter.EnterSystem("sys-a");
            IReadOnlyList<string> atSystem = presenter.Breadcrumb(snapshot);
            Assert.AreEqual(3, atSystem.Count);
            Assert.AreEqual("Evren", atSystem[0]);
            StringAssert.StartsWith("Sektör", atSystem[1]);
            Assert.AreEqual("sys-a", atSystem[2]);

            presenter.SelectPlanet("sys-a/pl00");
            Assert.AreEqual(4, presenter.Breadcrumb(snapshot).Count);
        }

        // ==================== deterministic selection ====================

        [Test]
        public void ASelectedSystemThatIsStillKnown_SurvivesARefresh()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem("sys-a");
            presenter.SelectPlanet("sys-a/pl00");

            presenter.Revalidate(SnapshotWith("sys-a", "sys-b"));

            Assert.AreEqual("sys-a", presenter.SelectedSystemId);
            Assert.AreEqual("sys-a/pl00", presenter.SelectedPlanetId);
            Assert.AreEqual(StrategicMapLevel.Planet, presenter.Level);
        }

        [Test]
        public void ASelectedSystemThatVanished_NeverLeavesAStaleSelection()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem("sys-gone");
            presenter.SelectPlanet("sys-gone/pl00");

            presenter.Revalidate(SnapshotWith("sys-a"));

            Assert.IsNull(presenter.SelectedSystemId, "a system that is no longer known must not stay selected");
            Assert.IsNull(presenter.SelectedPlanetId);
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);
        }

        [Test]
        public void APlanetThatStoppedBeingVisible_FallsBackToTheSystemLevel()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem("sys-a");
            presenter.SelectPlanet("sys-a/pl00");

            // The same system, but now with no planets visible (e.g. knowledge regressed).
            var systems = new List<StrategicSystemView> { SystemView("sys-a", System.Array.Empty<string>()) };
            presenter.Revalidate(new StrategicMapSnapshot(systems, new List<StrategicLinkView>(), default, "sys-a"));

            Assert.AreEqual("sys-a", presenter.SelectedSystemId);
            Assert.IsNull(presenter.SelectedPlanetId);
            Assert.AreEqual(StrategicMapLevel.System, presenter.Level, "the card must close rather than dangle");
        }

        [Test]
        public void RevalidatingAgainstNoSnapshot_ResetsCleanly()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem("sys-a");
            presenter.Revalidate(null);
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);
            Assert.IsNull(presenter.SelectedSystemId);
        }

        // ==================== M key single ownership ====================

        [Test]
        public void TheStrategicMapIsTheOnlyScreenThatBindsTheMapKey()
        {
            string uiRoot = Path.Combine(Application.dataPath, "Scripts", "Nebula");
            var owners = new List<string>();
            foreach (string file in Directory.GetFiles(uiRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                // Match KeyCode.M exactly -- never KeyCode.Mouse0/Menu/Minus.
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"KeyCode\.M\b(?!ouse|enu|inus)")) continue;
                // A binding-validator table that merely lists reserved keys is not an owner.
                if (Path.GetFileName(file) == "CombatBindingValidator.cs") continue;
                owners.Add(Path.GetFileName(file));
            }

            CollectionAssert.Contains(owners, "StrategicMapScreen.cs", "the strategic map must own the M key");
            foreach (string owner in owners)
            {
                if (owner == "StrategicMapScreen.cs") continue;
                // Any other file that still mentions M must gate it behind an explicit opt-out.
                string path = System.Array.Find(Directory.GetFiles(uiRoot, owner, SearchOption.AllDirectories), _ => true);
                string text = path != null ? File.ReadAllText(path) : "";
                StringAssert.Contains("MapKeyEnabled", text,
                    $"{owner} still binds M without yielding ownership to the strategic map");
            }
        }

        [Test]
        public void NoFunctionKeyIsEverBoundByTheStrategicMap()
        {
            string mapRoot = Path.Combine(Application.dataPath, "Scripts", "Nebula", "UI", "StrategicMap");
            foreach (string file in Directory.GetFiles(mapRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                for (int n = 1; n <= 12; n++)
                    StringAssert.DoesNotContain($"KeyCode.F{n}", text, $"{Path.GetFileName(file)} must never bind an F-key");
            }
        }

        // ==================== accessibility ====================

        [Test]
        public void ReducedMotion_RemovesPulseAndCameraOvershoot_ButNeverInformation()
        {
            StrategicMapTheme.ReducedMotion = false;
            try
            {
                float normalPulse = 0f;
                for (int i = 0; i < 20; i++) normalPulse = Mathf.Max(normalPulse, Mathf.Abs(StrategicMapTheme.Pulse(i * 0.13f)));
                Assert.Greater(normalPulse, 0f, "pulse animates normally");

                // With overshoot, the spring eases past 1 somewhere before settling.
                bool overshoots = false;
                for (float t = 0f; t <= 1f; t += 0.02f) if (StrategicMapTheme.EaseFocus(t) > 1.0005f) overshoots = true;
                Assert.IsTrue(overshoots, "the focus spring normally overshoots slightly");

                StrategicMapTheme.ReducedMotion = true;
                Assert.AreEqual(0f, StrategicMapTheme.Pulse(1.7f), "reduced motion removes pulsing entirely");
                for (float t = 0f; t <= 1f; t += 0.02f)
                    Assert.LessOrEqual(StrategicMapTheme.EaseFocus(t), 1.0005f, "reduced motion removes camera overshoot");

                // The endpoints still resolve, so nothing informational is lost.
                Assert.AreEqual(0f, StrategicMapTheme.EaseFocus(0f), 0.001f);
                Assert.AreEqual(1f, StrategicMapTheme.EaseFocus(1f), 0.001f);
            }
            finally { StrategicMapTheme.ReducedMotion = false; }
        }

        [Test]
        public void EveryStateColourIsPairedWithARealProducedIcon_ColourIsNeverTheOnlyCarrier()
        {
            // Dominance, link and knowledge each resolve to a real shipped icon path,
            // so a colour-blind player still gets the state from the icon.
            // Unowned is deliberately iconless (there is nothing to badge); every state the
            // map actually badges must have a real shipped icon.
            foreach (DominanceState state in new[] { DominanceState.Developing, DominanceState.Controlled,
                                                     DominanceState.Contested, DominanceState.Lost })
                Assert.IsNotEmpty(Visuals.NebulaProducedVisualCatalog.Dominance(state), $"{state} needs an icon");
            foreach (SystemLinkState link in System.Enum.GetValues(typeof(SystemLinkState)))
                Assert.IsNotEmpty(Visuals.NebulaProducedVisualCatalog.Link(link), $"{link} needs an icon");
            foreach (SystemKnowledgeLevel level in System.Enum.GetValues(typeof(SystemKnowledgeLevel)))
                if (level != SystemKnowledgeLevel.Unknown)
                    Assert.IsNotEmpty(Visuals.NebulaProducedVisualCatalog.Knowledge(level), $"{level} needs an icon");
        }
    }
}
