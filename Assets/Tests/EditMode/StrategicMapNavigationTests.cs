using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;
using NUnit.Framework;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The map's four levels and the trail that names them.
    ///
    /// The interesting cases are all about the galaxy level, which sits between the universe
    /// and a system and can legitimately be skipped: a system can be entered straight from
    /// the overview. Everything here is about what happens on the way back out.
    /// </summary>
    public sealed class StrategicMapNavigationTests
    {
        private static StrategicMapUniverseSnapshot Load()
        {
            var snapshot = StrategicMapStaticPreviewSource.Load();
            Assert.IsNotNull(snapshot, "universe snapshot must load from Resources");
            return snapshot;
        }

        [Test]
        public void Level_StartsAtTheUniverse()
        {
            var presenter = new StrategicMapPresenter();
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);
            Assert.IsNull(presenter.SelectedGalaxyId);
        }

        [Test]
        public void EnteringAGalaxy_OpensThatLevel()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterGalaxy("aetheria");

            Assert.AreEqual(StrategicMapLevel.Galaxy, presenter.Level);
            Assert.AreEqual("aetheria", presenter.SelectedGalaxyId);
        }

        [Test]
        public void SteppingBackFromAGalaxy_ReturnsToTheUniverse()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterGalaxy("aetheria");

            Assert.IsTrue(presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);
            Assert.IsNull(presenter.SelectedGalaxyId, "the galaxy must not stay open behind the overview");
        }

        [Test]
        public void SteppingBackFromASystem_ReturnsToItsGalaxyWhenOneWasOpened()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterGalaxy("aetheria");
            presenter.EnterSystem("helios", "aetheria");

            Assert.IsTrue(presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.Galaxy, presenter.Level);
            Assert.AreEqual("aetheria", presenter.SelectedGalaxyId);
        }

        [Test]
        public void SteppingBackFromASystem_GoesStraightToTheUniverseWhenNoGalaxyWasOpened()
        {
            // A system can be entered from the overview without ever opening its galaxy.
            // Backing out then has nowhere in between to land.
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem("helios");

            Assert.IsTrue(presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);
        }

        [Test]
        public void EnteringASystemWithItsGalaxy_RecordsTheGalaxyForTheWayBack()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem("helios", "aetheria");

            Assert.AreEqual("aetheria", presenter.SelectedGalaxyId);
            Assert.IsTrue(presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.Galaxy, presenter.Level);
        }

        [Test]
        public void SteppingBack_WalksTheWholeChainOneLevelAtATime()
        {
            var presenter = new StrategicMapPresenter();
            presenter.EnterGalaxy("aetheria");
            presenter.EnterSystem("helios", "aetheria");
            presenter.SelectPlanet("helios-planet-1");
            Assert.AreEqual(StrategicMapLevel.Planet, presenter.Level);

            Assert.IsTrue(presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.System, presenter.Level);
            Assert.IsTrue(presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.Galaxy, presenter.Level);
            Assert.IsTrue(presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.Universe, presenter.Level);

            // At the top the map has nowhere left to go, and the caller closes it.
            Assert.IsFalse(presenter.StepBack());
        }

        /// <summary>
        /// The trail is player-facing chrome, and this game's player faces Turkish: the whole
        /// map HUD — clock, depth label, legend, status line, control hint — is Turkish, so
        /// the root of the breadcrumb is "TÜM EVREN". These tests used to spell the retired
        /// English reference's "ENTIRE UNIVERSE" out by hand and failed the moment the chrome
        /// was translated, which pinned a spelling rather than a contract. They now name the
        /// production constant: what is worth pinning is that the trail starts at the top
        /// level and grows only by the levels actually opened.
        /// </summary>
        [Test]
        public void Breadcrumb_NamesOnlyTheLevelsActuallyOpened()
        {
            StrategicMapUniverseSnapshot universe = Load();
            var presenter = new StrategicMapPresenter();

            Assert.AreEqual(StrategicMapHud.UniverseCrumb,
                StrategicMapHud.BuildBreadcrumb(presenter, universe));

            presenter.EnterGalaxy(universe.galaxies[0].id);
            string atGalaxy = StrategicMapHud.BuildBreadcrumb(presenter, universe);
            StringAssert.StartsWith(StrategicMapHud.UniverseCrumb, atGalaxy);
            StringAssert.Contains(universe.galaxies[0].name.ToUpperInvariant(), atGalaxy);

            presenter.EnterSystem(universe.systems[0].id, universe.systems[0].galaxyId);
            string atSystem = StrategicMapHud.BuildBreadcrumb(presenter, universe);
            StringAssert.Contains(universe.systems[0].name.ToUpperInvariant(), atSystem);
        }

        [Test]
        public void Breadcrumb_SkipsTheGalaxyWhenTheSystemWasEnteredWithoutOne()
        {
            StrategicMapUniverseSnapshot universe = Load();
            var presenter = new StrategicMapPresenter();
            presenter.EnterSystem(universe.systems[0].id);

            string trail = StrategicMapHud.BuildBreadcrumb(presenter, universe);
            Assert.AreEqual(2, trail.Split('›').Length,
                "with no galaxy opened the trail is universe then system, nothing between");
        }

        [Test]
        public void Breadcrumb_SurvivesAnUnknownId()
        {
            // Ids come from the preview snapshot, and the live-data adapter will eventually
            // feed this from a different source. An id it cannot name must not break the trail.
            StrategicMapUniverseSnapshot universe = Load();
            var presenter = new StrategicMapPresenter();
            presenter.EnterGalaxy("no-such-galaxy");

            Assert.AreEqual(StrategicMapHud.UniverseCrumb,
                StrategicMapHud.BuildBreadcrumb(presenter, universe),
                "an id the trail cannot name must add nothing at all -- not an empty crumb, "
                + "not a separator with nothing after it");
        }
    }
}
