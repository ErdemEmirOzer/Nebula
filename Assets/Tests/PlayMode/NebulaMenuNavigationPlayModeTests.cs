using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Brand;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The main menu driven the way a player without a mouse drives it.
    /// <para>
    /// The brand work moved the menu into a <see cref="ScrollRect"/>, and a scrolling
    /// column is where keyboard and controller navigation quietly breaks: Unity is
    /// perfectly willing to focus a control that has been scrolled out of the window,
    /// so pressing down past the last visible entry looks like nothing happened and
    /// then Enter starts something the player never read. Everything here is about
    /// that failure and the ones next to it — an entry printed through the build line,
    /// a footer that moves when the list does, a wordmark that grows into the column.
    /// </para>
    /// <para>
    /// Nothing in this fixture knows how many entries the menu has or what they are
    /// called. The options are discovered from the live column every case, because a
    /// hard-coded list of eight is a test that keeps passing on the day a ninth entry
    /// is added below the fold — which is exactly the bug class this covers.
    /// </para>
    /// </summary>
    public class NebulaMenuNavigationPlayModeTests
    {
        private NebulaUiShell shell;
        private NebulaMainMenuScreen menu;
        private StubLauncher launcher;
        private Camera viewportCamera;
        private RenderTexture viewportTarget;

        private sealed class StubLauncher : INebulaSessionLauncher
        {
            public void StartNewCampaign() { }
            public void ContinueFromSlot(int slot) { }
            public void QuitGame() { }
            public void ReturnToMainMenu() { }
            public void RestartCampaign() { }
        }

        /// <summary>The two scales the brief names. 0.8 is covered by the brand fixture.</summary>
        private static readonly float[] Scales = { 1f, 1.4f };

        private static IEnumerable<(int width, int height, string label)> Resolutions()
        {
            yield return (1280, 720, "1280x720");
            yield return (1920, 1080, "1920x1080");
            yield return (2560, 1600, "2560x1600");
            yield return (3440, 1440, "3440x1440");
        }

        [SetUp]
        public void Setup()
        {
            ExclusiveModal.ResetForSession();
            NebulaBrandAssetCatalog.Release();
            NebulaLoadingPresenter.ForceClear();
            NebulaUiShell.UiScale = 1f;

            shell = NebulaUiShell.Create();
            launcher = new StubLauncher();
            menu = NebulaMainMenuScreen.Create(shell, launcher);
        }

        [TearDown]
        public void Teardown()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            if (shell != null)
            {
                Canvas canvas = shell.CanvasFor(UiLayer.Menu);
                if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
            }
            if (viewportCamera != null)
            {
                viewportCamera.targetTexture = null;
                Object.DestroyImmediate(viewportCamera.gameObject);
            }
            if (viewportTarget != null) { viewportTarget.Release(); Object.DestroyImmediate(viewportTarget); }
            viewportCamera = null;
            viewportTarget = null;

            if (menu != null) Object.DestroyImmediate(menu.gameObject);
            if (shell != null) Object.DestroyImmediate(shell.gameObject);
            menu = null;
            shell = null;
            NebulaUiShell.UiScale = 1f;
            NebulaBrandAssetCatalog.Release();
            ExclusiveModal.ResetForSession();
        }

        // ---- discovery -------------------------------------------------------

        /// <summary>
        /// Every option the live menu is offering, top to bottom on screen.
        /// <para>
        /// Read off the column rather than declared: order comes from the world
        /// rectangles, not from the build order, so an entry that is moved keeps its
        /// place in the expected traversal automatically. Non-interactable controls
        /// are excluded because Unity's navigation skips them — DEVAM ET is disabled
        /// on a machine with no playable save, and a traversal that insisted on
        /// visiting it would fail for a reason that has nothing to do with the menu.
        /// </para>
        /// </summary>
        private List<Selectable> DiscoverOptions()
        {
            var found = new List<Selectable>();
            foreach (Selectable candidate in menu.MenuColumn.GetComponentsInChildren<Selectable>(true))
            {
                if (!candidate.gameObject.activeInHierarchy) continue;
                if (!candidate.IsInteractable()) continue;
                // Fully qualified: `Navigation` alone binds to the project's own
                // Nebula.Navigation namespace here, not to UnityEngine.UI's struct.
                if (candidate.navigation.mode == UnityEngine.UI.Navigation.Mode.None) continue;
                found.Add(candidate);
            }

            found.Sort((a, b) => WorldRect((RectTransform)b.transform).yMax
                .CompareTo(WorldRect((RectTransform)a.transform).yMax));
            return found;
        }

        /// <summary>
        /// The discovery itself is sound: it finds every interactable control in the
        /// column and nothing outside it.
        /// <para>
        /// A discovery that silently returned one entry would make every traversal
        /// below pass trivially, so it is pinned before it is used.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheMenuOffersMoreThanOneReachableOptionToDiscover()
        {
            yield return SetViewport(1920, 1080);

            List<Selectable> options = DiscoverOptions();
            Assert.Greater(options.Count, 1,
                "discovery found fewer than two reachable options; every traversal below would be vacuous");

            foreach (Selectable option in options)
                Assert.IsTrue(option.transform.IsChildOf(menu.MenuColumn),
                    option.name + " was discovered but does not live in the scrolling column");

            // Everything interactable in the column was found. Counted independently of
            // the discovery's own filter so the two cannot agree by sharing a bug.
            int interactable = 0;
            foreach (Button button in menu.MenuColumn.GetComponentsInChildren<Button>(true))
                if (button.gameObject.activeInHierarchy && button.IsInteractable()) interactable++;
            Assert.AreEqual(interactable, options.Count,
                "discovery missed an interactable button in the column");
        }

        // ---- keyboard and controller traversal -------------------------------

        /// <summary>
        /// Down from the first option reaches the last, visiting each exactly once,
        /// at every resolution and both interface scales.
        /// <para>
        /// Driven through <see cref="ExecuteEvents.moveHandler"/> with a real
        /// <see cref="AxisEventData"/>, which is precisely what
        /// <c>StandaloneInputModule.ProcessMove</c> executes once it has read the
        /// Vertical axis. <c>Selectable.OnMove</c> assigns
        /// <c>BaseEventData.selectedObject</c>, whose setter calls back into
        /// <see cref="EventSystem.SetSelectedGameObject(GameObject, BaseEventData)"/> —
        /// so the selection really moves and the scroll follower really sees it. The
        /// only thing standing in for hardware is the axis read, which
        /// <see cref="TheVerticalAxisAndSubmitAreBoundForKeyboardAndController"/>
        /// covers from the project's own bindings.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryOptionIsReachableByMoveEventsAtEveryResolutionAndScale()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);

                    string where = $"{label} @ {scale:0.0}x";
                    List<Selectable> options = DiscoverOptions();
                    Assert.Greater(options.Count, 1, where + ": nothing to traverse");

                    yield return FocusFirstOption(options[0], where);

                    var visited = new List<Selectable> { options[0] };
                    // One more step than there are options, so a traversal that loops
                    // back to the top is caught rather than running forever.
                    for (int step = 0; step < options.Count + 1; step++)
                    {
                        Selectable before = Current();
                        yield return Move(MoveDirection.Down);
                        Selectable after = Current();

                        if (after == before) break;              // the end of the list
                        Assert.IsFalse(visited.Contains(after),
                            $"{where}: down revisited {after.name}; the navigation loops");
                        visited.Add(after);
                    }

                    Assert.AreEqual(options.Count, visited.Count,
                        $"{where}: down reached {visited.Count} of {options.Count} options "
                        + $"(stopped at {visited[visited.Count - 1].name})");
                    for (int i = 0; i < options.Count; i++)
                        Assert.AreSame(options[i], visited[i],
                            $"{where}: step {i} focused {visited[i].name}, expected {options[i].name}");
                }
            }
        }

        /// <summary>
        /// And back up again, which is not the same claim — automatic navigation
        /// computes each direction independently, so a column can be walkable one way
        /// and skip an entry the other.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTraversalIsSymmetricBackToTheFirstOption()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);

                    string where = $"{label} @ {scale:0.0}x";
                    List<Selectable> options = DiscoverOptions();
                    Selectable last = options[options.Count - 1];

                    yield return FocusByMoving(last, MoveDirection.Down, where);

                    var visited = new List<Selectable> { last };
                    for (int step = 0; step < options.Count + 1; step++)
                    {
                        Selectable before = Current();
                        yield return Move(MoveDirection.Up);
                        Selectable after = Current();
                        if (after == before) break;
                        Assert.IsFalse(visited.Contains(after), $"{where}: up revisited {after.name}");
                        visited.Add(after);
                    }

                    Assert.AreEqual(options.Count, visited.Count,
                        $"{where}: up reached {visited.Count} of {options.Count} options");
                    Assert.AreSame(options[0], visited[visited.Count - 1],
                        where + ": up did not end on the first option");
                }
            }
        }

        /// <summary>
        /// The focused entry is never outside the scrolling window.
        /// <para>
        /// The assertion the whole <c>NebulaMenuScrollFollow</c> component exists for.
        /// Checked after every single move rather than at the end, because a follower
        /// that only catches up when the traversal stops would still leave the player
        /// pressing down against an entry they cannot see.
        /// </para>
        /// <para>
        /// The window has to be inside the canvas as well. An entry sitting neatly
        /// inside a viewport that itself hangs off the bottom of the screen is
        /// invisible, and is the exact shape of the layout bug at 140%.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheScrollWindowFollowsFocusThroughTheWholeList()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);

                    string where = $"{label} @ {scale:0.0}x";
                    List<Selectable> options = DiscoverOptions();

                    yield return FocusFirstOption(options[0], where);
                    AssertFocusVisible(options[0], where + " (first)");

                    for (int step = 1; step < options.Count; step++)
                    {
                        yield return Move(MoveDirection.Down);
                        Selectable focused = Current();
                        Assert.IsNotNull(focused, $"{where}: focus was lost at step {step}");
                        AssertFocusVisible(focused, $"{where} (step {step}: {focused.name})");
                    }

                    // The last option specifically. On 3440x1440 and at 140% this is
                    // the one that used to be off the bottom of the canvas entirely.
                    Assert.AreSame(options[options.Count - 1], Current(),
                        where + ": the traversal did not end on the last option");
                }
            }
        }

        /// <summary>
        /// Every option clears the build line — not just the window that holds them.
        /// <para>
        /// Asserted per entry and with the list scrolled to the bottom, which is the
        /// only state in which the last entry and the footer are close enough to
        /// collide. At 3440x1440 ÇIKIŞ used to occupy 842..890 while the footer had
        /// 832..876.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator NoOptionOverlapsTheFooterAtAnyResolutionOrScale()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);

                    string where = $"{label} @ {scale:0.0}x";
                    RectTransform footer = FindChild("Footer");
                    Assert.IsNotNull(footer, where + ": the footer must exist");
                    Rect build = WorldRect(footer);

                    List<Selectable> options = DiscoverOptions();

                    // Walked with the focus, so each entry is measured in the scroll
                    // position it is actually seen at.
                    yield return FocusFirstOption(options[0], where);
                    for (int step = 0; step < options.Count; step++)
                    {
                        if (step > 0) yield return Move(MoveDirection.Down);
                        Selectable focused = Current();
                        Rect entry = WorldRect((RectTransform)focused.transform);

                        Assert.GreaterOrEqual(entry.yMin, build.yMax - 0.5f,
                            $"{where}: {focused.name} overlaps the build line by "
                            + $"{(build.yMax - entry.yMin) / Mathf.Max(build.height, 0.0001f):P0} of its height");
                    }
                }
            }
        }

        /// <summary>
        /// The footer does not move when the list does.
        /// <para>
        /// It is anchored to the bottom of the canvas and lives outside the
        /// ScrollRect, and the cheapest wrong fix for the overlap above — putting the
        /// footer inside the scrolling content — would pass the test before this one
        /// while scrolling the build string off the screen.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheFooterStaysPutWhileTheColumnScrolls()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);

                    string where = $"{label} @ {scale:0.0}x";
                    RectTransform footer = FindChild("Footer");
                    Assert.IsFalse(footer.IsChildOf(menu.MenuColumn),
                        where + ": the footer is inside the scrolling content");

                    Rect before = WorldRect(footer);

                    List<Selectable> options = DiscoverOptions();
                    yield return FocusFirstOption(options[0], where);
                    for (int step = 1; step < options.Count; step++) yield return Move(MoveDirection.Down);

                    Rect after = WorldRect(footer);
                    Assert.AreEqual(before.yMin, after.yMin, 0.5f, where + ": the footer moved vertically");
                    Assert.AreEqual(before.xMin, after.xMin, 0.5f, where + ": the footer moved horizontally");

                    // And it is still on the canvas, which "did not move" alone does not say.
                    Rect canvas = WorldRect(shell.Root(UiLayer.Menu));
                    Assert.GreaterOrEqual(after.yMin, canvas.yMin - 0.5f, where + ": the footer is below the screen");
                }
            }
        }

        /// <summary>
        /// The plate's wordmark and the menu column never occupy the same band, at
        /// either scale. The interface scale changes the canvas height in layout
        /// units, which moves the column's top without moving the logo — so this is
        /// not implied by the same claim at 100%.
        /// </summary>
        [UnityTest]
        public IEnumerator TheLogoAndTheMenuColumnNeverCollideAtEitherScale()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);
                    menu.KeyArt.Layout();

                    string where = $"{label} @ {scale:0.0}x";
                    Assert.IsTrue(menu.KeyArt.HasLogo, where + ": the separate wordmark is not drawn");
                    Assert.IsFalse(menu.TextWordmarkVisible,
                        where + ": the drawn text wordmark is on screen beside the authored logo");

                    Rect logo = WorldRect((RectTransform)menu.KeyArt.LogoImage.transform);
                    Rect window = WorldRect(menu.MenuViewport);

                    Assert.GreaterOrEqual(logo.yMin, window.yMax - 0.5f,
                        $"{where}: the logo reaches down to {logo.yMin:0.###} but the menu window "
                        + $"starts at {window.yMax:0.###}");
                }
            }
        }

        /// <summary>
        /// The authored wordmark is drawn after everything it has to be seen over.
        /// <para>
        /// This is the assertion the brand suite did not have. Every property said the
        /// logo was there — a sprite was bound, the Image was enabled, the object was
        /// active, the rect was in the right place — and it was ordered underneath the
        /// menu's opaque backdrop, so the top left of the screen was empty sky and the
        /// drawn text title had already stood down for it. Sibling order is the only
        /// thing that distinguishes the two states, so it is what gets checked.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheAuthoredWordmarkIsDrawnOverTheBackdropAndThePlate()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);

                    string where = $"{label} @ {scale:0.0}x";
                    var mark = (RectTransform)menu.KeyArt.LogoImage.transform;
                    var plate = (RectTransform)menu.KeyArt.transform;
                    RectTransform backdrop = FindChild("Backdrop");

                    Assert.IsNotNull(backdrop, where + ": the backdrop must exist");
                    Assert.AreSame(plate.parent, mark.parent, where + ": the pair was reparented apart");
                    Assert.AreSame(plate.parent, backdrop.parent, where + ": the backdrop left the plate's host");

                    Assert.Greater(mark.GetSiblingIndex(), plate.GetSiblingIndex(),
                        where + ": the wordmark is drawn under the key-art plate");
                    Assert.Greater(mark.GetSiblingIndex(), backdrop.GetSiblingIndex(),
                        where + ": the wordmark is drawn under the opaque backdrop");
                    Assert.IsTrue(menu.KeyArt.LogoDrawsAboveThePlate, where + ": draw order is wrong");

                    // And exactly one title is on screen, which is only true if the one
                    // that stood down was replaced by one that is actually visible.
                    Assert.IsTrue(menu.KeyArt.CarriesTitle, where + ": the background carries no title");
                    Assert.IsFalse(menu.TextWordmarkVisible, where + ": two titles are on screen");
                }
            }
        }

        // ---- the device half -------------------------------------------------

        /// <summary>
        /// Both a keyboard and a controller can produce the events the traversal above
        /// consumes.
        /// <para>
        /// A batch-mode run has no devices, so the traversal is driven through the
        /// event the input module raises rather than through hardware. What is left to
        /// prove is that the module's axes are bound for both device families, and
        /// that is a fact about the project's own bindings: the live
        /// <see cref="StandaloneInputModule"/> is asked which axis names it reads, and
        /// the InputManager is required to define each of them twice — once from a key
        /// and once from a joystick.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheVerticalAxisAndSubmitAreBoundForKeyboardAndController()
        {
            yield return null;

            var module = Object.FindObjectOfType<StandaloneInputModule>();
            Assert.IsNotNull(module, "the shell must install an input module for navigation to exist at all");

            string settings = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "ProjectSettings", "InputManager.asset");
            Assert.IsTrue(File.Exists(settings), "InputManager.asset is missing; bindings cannot be verified");
            string text = File.ReadAllText(settings);

            // Movement: one keyboard-typed entry and one joystick-axis entry, both
            // under the name the module reads.
            AssertBoundForBothDevices(text, module.verticalAxis, "vertical navigation");
            AssertBoundForBothDevices(text, module.horizontalAxis, "horizontal navigation");

            // Activation: at least one binding naming a joystick button, and at least
            // one naming a key.
            AssertSubmitReachableFromBothDevices(text, module.submitButton, "submit");
            AssertSubmitReachableFromBothDevices(text, module.cancelButton, "cancel");
        }

        /// <summary>
        /// Every axis entry in InputManager.asset carrying the given name, as its own
        /// list of <c>key: value</c> pairs.
        /// <para>
        /// The file is a flat YAML list of fixed-shape entries, so an entry runs from
        /// its own <c>m_Name</c> to the next. Read line by line rather than with a
        /// multiline regex, because the empty bindings in this file (<c>negativeButton:</c>
        /// with nothing after it) let a <c>\s*</c> that can cross a newline swallow the
        /// following line and report its value as this one's — which is how the first
        /// version of this test claimed Submit had no controller binding while
        /// <c>altPositiveButton: joystick button 0</c> was sitting in the file.
        /// </para>
        /// <para>
        /// Parsed rather than deserialized because a fixture that needed the editor's
        /// serialization API could not live in the player-mode assembly.
        /// </para>
        /// </summary>
        private static List<Dictionary<string, string>> AxisEntries(string asset, string axisName)
        {
            var entries = new List<Dictionary<string, string>>();
            Dictionary<string, string> current = null;

            foreach (string raw in asset.Split('\n'))
            {
                string line = raw.TrimEnd('\r').Trim();
                if (line.StartsWith("- ")) line = line.Substring(2).Trim();

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();

                if (key == "m_Name")
                {
                    current = value == axisName ? new Dictionary<string, string>() : null;
                    if (current != null) entries.Add(current);
                    continue;
                }
                if (current != null && !current.ContainsKey(key)) current[key] = value;
            }

            return entries;
        }

        /// <summary>type 0 is key-or-mouse-button; type 2 is a joystick axis.</summary>
        private static void AssertBoundForBothDevices(string asset, string axisName, string what)
        {
            List<Dictionary<string, string>> entries = AxisEntries(asset, axisName);
            Assert.IsNotEmpty(entries, $"{what}: InputManager defines no axis called '{axisName}'");

            bool keyboard = false, controller = false;
            foreach (Dictionary<string, string> entry in entries)
            {
                entry.TryGetValue("type", out string type);
                if (type == "0" && DeviceBindings(entry).Count > 0) keyboard = true;
                if (type == "2") controller = true;
            }

            Assert.IsTrue(keyboard, $"{what}: '{axisName}' has no keyboard binding");
            Assert.IsTrue(controller, $"{what}: '{axisName}' has no joystick-axis binding; "
                + "a controller cannot move the selection");
        }

        private static void AssertSubmitReachableFromBothDevices(string asset, string axisName, string what)
        {
            List<Dictionary<string, string>> entries = AxisEntries(asset, axisName);
            Assert.IsNotEmpty(entries, $"{what}: InputManager defines no button called '{axisName}'");

            bool keyboard = false, controller = false;
            foreach (Dictionary<string, string> entry in entries)
                foreach (string value in DeviceBindings(entry))
                {
                    if (value.StartsWith("joystick")) controller = true;
                    else if (!value.StartsWith("mouse")) keyboard = true;
                }

            Assert.IsTrue(keyboard, $"{what}: '{axisName}' has no key binding");
            Assert.IsTrue(controller, $"{what}: '{axisName}' has no joystick-button binding");
        }

        /// <summary>The four button slots an axis entry can name, minus the empty ones.</summary>
        private static List<string> DeviceBindings(Dictionary<string, string> entry)
        {
            var bound = new List<string>();
            foreach (string key in new[]
                { "positiveButton", "negativeButton", "altPositiveButton", "altNegativeButton" })
                if (entry.TryGetValue(key, out string value) && value.Length > 0) bound.Add(value);
            return bound;
        }

        // ---- helpers ---------------------------------------------------------

        private static Selectable Current()
        {
            GameObject selected = EventSystem.current == null
                ? null : EventSystem.current.currentSelectedGameObject;
            return selected == null ? null : selected.GetComponent<Selectable>();
        }

        /// <summary>
        /// Asks the screen to establish first focus, the way opening it does, and gives
        /// the follower a frame to see the result.
        /// <para>
        /// This fixture used to call <c>EventSystem.SetSelectedGameObject</c> itself
        /// before every traversal, which quietly made the traversals unfalsifiable in
        /// the one way that matters: production could establish no focus at all and
        /// every test here would still pass, because the fixture had already put the
        /// selection where production was supposed to. First focus is production's job,
        /// so the fixture now triggers the production entry point —
        /// <see cref="NebulaMainMenuScreen.OnRevealed"/>, which is what the shell calls
        /// when this screen comes back to the front — and asserts that a selection
        /// exists afterwards rather than creating one.
        /// </para>
        /// </summary>
        private IEnumerator FocusThroughProduction()
        {
            menu.OnRevealed();
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.IsNotNull(Current(),
                "the screen established no first focus of its own; keyboard and gamepad have nothing to move from");
        }

        /// <summary>
        /// Walks the selection to a specific option using nothing but real move events.
        /// <para>
        /// The alternative — setting the selection directly — is the fixture doing
        /// production's job again, one step further in. Walking there means the setup
        /// is made of the same events the test is about, so a navigation that cannot
        /// reach the option fails here, loudly, instead of being stepped over.
        /// </para>
        /// </summary>
        /// <summary>
        /// Production's first focus <b>is</b> the first option, asserted rather than
        /// arranged. This is the claim P1-TEST-003 / P2-TEST-005 are about: the fixture
        /// used to put the selection on the first option itself, so a screen that
        /// focused nothing still passed every traversal below it.
        /// </summary>
        private IEnumerator FocusFirstOption(Selectable first, string where)
        {
            yield return FocusThroughProduction();
            Assert.AreSame(first, Current(),
                $"{where}: the screen's own first focus landed on "
                + (Current() == null ? "nothing" : Current().name)
                + $" rather than on the first option ({first.name})");
        }

        /// <summary>
        /// Walks the selection to a specific option using nothing but real move events,
        /// starting from wherever production put it.
        /// <para>
        /// The alternative — setting the selection directly — is the fixture doing
        /// production's job again, one step further in. Walking there means the setup
        /// is made of the same events the test is about, so a navigation that cannot
        /// reach the option fails here, loudly, instead of being stepped over.
        /// </para>
        /// </summary>
        private IEnumerator FocusByMoving(Selectable option, MoveDirection direction, string where)
        {
            yield return FocusThroughProduction();

            // Bounded by the column length plus one: a navigation that loops must fail
            // rather than spin.
            int limit = menu.MenuColumn.GetComponentsInChildren<Selectable>(true).Length + 1;
            for (int step = 0; step < limit && Current() != option; step++)
                yield return Move(direction);

            Assert.AreSame(option, Current(),
                $"{where}: {option.name} could not be reached from the screen's own first focus "
                + "using move events alone");
        }

        /// <summary>
        /// One press of a direction, delivered as the input module delivers it.
        /// </summary>
        private IEnumerator Move(MoveDirection direction)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(selected, "nothing is focused; there is nothing to move from");

            var data = new AxisEventData(EventSystem.current)
            {
                moveDir = direction,
                moveVector = direction == MoveDirection.Down ? new Vector2(0f, -1f)
                    : direction == MoveDirection.Up ? new Vector2(0f, 1f)
                    : direction == MoveDirection.Left ? new Vector2(-1f, 0f) : new Vector2(1f, 0f),
            };
            ExecuteEvents.Execute(selected, data, ExecuteEvents.moveHandler);

            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private void AssertFocusVisible(Selectable option, string where)
        {
            Rect entry = WorldRect((RectTransform)option.transform);
            Rect window = WorldRect(menu.MenuViewport);
            Rect canvas = WorldRect(shell.Root(UiLayer.Menu));

            Assert.GreaterOrEqual(entry.yMin, window.yMin - 0.5f,
                $"{where}: the focused entry is cut off below the scroll window");
            Assert.LessOrEqual(entry.yMax, window.yMax + 0.5f,
                $"{where}: the focused entry is cut off above the scroll window");
            Assert.GreaterOrEqual(entry.yMin, canvas.yMin - 0.5f,
                $"{where}: the focused entry is below the bottom of the screen");
            Assert.LessOrEqual(entry.yMax, canvas.yMax + 0.5f,
                $"{where}: the focused entry is above the top of the screen");
        }

        /// <summary>
        /// Repoints the menu canvas at a camera rendering into a target of the given
        /// size — how this project stands in for a resolution the host does not have.
        /// </summary>
        private IEnumerator SetViewport(int width, int height)
        {
            Canvas canvas = shell.CanvasFor(UiLayer.Menu);
            if (viewportCamera == null)
            {
                var go = new GameObject("ViewportCamera") { hideFlags = HideFlags.HideAndDontSave };
                viewportCamera = go.AddComponent<Camera>();
                viewportCamera.orthographic = true;
                viewportCamera.transform.position = new Vector3(0f, 0f, -100f);
                // This camera exists to define a viewport, not to draw one: the fixture
                // reads RectTransform corners and never a pixel. Left enabled, a camera
                // with a targetTexture is rendered every frame by the player loop's
                // RenderOffscreenCameras pass, which on the -nographics null device
                // segfaults the Editor inside DrawLineOrTrailFromNodeQueue -- taking the
                // result XML of every fixture sharing the process with it. Disabling it
                // keeps the canvas sized and projected by this camera while removing it
                // from the render loop, so these nine layout tests still run headless
                // rather than being skipped for want of pixels they never look at.
                viewportCamera.enabled = false;
            }

            viewportCamera.targetTexture = null;
            if (viewportTarget != null) { viewportTarget.Release(); Object.DestroyImmediate(viewportTarget); }
            viewportTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            viewportCamera.targetTexture = viewportTarget;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = viewportCamera;
            canvas.planeDistance = 100f;

            shell.ApplyScale();
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private RectTransform FindChild(string name)
        {
            foreach (Transform child in menu.KeyArt.transform.parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child as RectTransform;
            return null;
        }
    }
}
