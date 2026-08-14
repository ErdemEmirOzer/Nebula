using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The guards the removal matrix names for each deleted class.
    /// <para>
    /// Every one of these was specified before the removals and none of them was
    /// written, so twelve classes came out with nothing standing behind the
    /// promise that they would not come back. A removal without a guard is a
    /// removal that gets undone by the next person who needs the shape of the
    /// thing that was there.
    /// </para>
    /// </summary>
    public class NebulaUiRemovalGuardTests
    {
        private static string UiRoot =>
            Path.Combine(Application.dataPath, "Scripts", "Nebula", "UI");

        private NebulaUiShell shell;
        private float enteredTimeScale;

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void Teardown()
        {
            if (shell != null)
            {
                shell.Shutdown();
                UnityEngine.Object.DestroyImmediate(shell.gameObject);
            }
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
        }

        /// <summary>Source files of the redesigned interface. The map is another workstream's.</summary>
        private static IEnumerable<string> RedesignSources()
        {
            foreach (string file in Directory.GetFiles(UiRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains("StrategicMap")) continue;

                // The two dev-only panels the removal matrix explicitly keeps as
                // they are. They are never created in a release player, so holding
                // them to the player interface's rules would invent work, not guard
                // any.
                string name = Path.GetFileName(file);
                if (name == "SectorDebugHud.cs" || name == "FlightTestSpeedHud.cs") continue;

                yield return file;
            }
        }

        /// <summary>Strips comments, so a line explaining a defect is not read as one.</summary>
        private static string CodeOnly(string path)
        {
            var kept = new System.Text.StringBuilder();
            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*")) continue;
                kept.AppendLine(line);
            }
            return kept.ToString();
        }

        // ---- L-15/L-13/L-14: the classes are gone -----------------------------

        [Test]
        public void LegacyScreenRootsRemoved()
        {
            string[] deleted =
            {
                "ExtractionHud", "ShipHud", "CombatHud", "AttentionHud", "HangarHud",
                "ProgressionHud", "MaintenanceReserveHud", "SupportBuildHud", "NavigationHud",
                "ProgressionScreen", "NavigationScreen", "HangarScreen",
            };

            string scripts = Path.Combine(Application.dataPath, "Scripts", "Nebula");
            foreach (string name in deleted)
            {
                string[] found = Directory.GetFiles(scripts, name + ".cs", SearchOption.AllDirectories);
                Assert.IsEmpty(found, $"{name} is back. It was replaced, not renamed.");
            }
        }

        // ---- L-11: no unicode standing in for art -----------------------------

        [Test]
        public void NoUnicodeIconReachesThePlayer()
        {
            // The exact set the matrix names, plus the arrows that crept back in on
            // the support toggle. Not a general non-ASCII ban: Turkish copy is full
            // of İ, Ş and ğ, and the interface is written in it.
            var forbidden = new Regex("[☠⚠◐▶◀★◆◇✓✕]");
            var offenders = new List<string>();

            foreach (string file in RedesignSources())
                if (forbidden.IsMatch(CodeOnly(file)))
                    offenders.Add(Path.GetFileName(file));

            Assert.IsEmpty(offenders,
                "Unicode glyphs standing in for art this project already owns: "
                + string.Join(", ", offenders));
        }

        // ---- L-01: no text wall ------------------------------------------------

        [Test]
        public void NoPanelOverflowsInsteadOfBeingLaidOut()
        {
            // ExtractionHud's bodyText was 940x148 with verticalOverflow=Overflow,
            // which is what let twelve stacked lines run off the bottom of the
            // screen. Overflow is the mechanism that made the text wall possible.
            var offenders = new List<string>();
            foreach (string file in RedesignSources())
                if (CodeOnly(file).Contains("VerticalWrapMode.Overflow"))
                    offenders.Add(Path.GetFileName(file));

            Assert.IsEmpty(offenders,
                "Vertical overflow is how the old text wall grew: " + string.Join(", ", offenders));
        }

        // ---- L-12: four canvases, not twelve -----------------------------------

        [Test]
        public void TheShellOwnsExactlyFourCanvases()
        {
            int canvases = 0;
            foreach (Canvas canvas in shell.GetComponentsInChildren<Canvas>(true)) { canvases++; }

            Assert.AreEqual(4, canvases,
                "One canvas per HUD meant twelve rebuild groups and no batching. "
                + "Static HUD, dynamic HUD, menu and overlay is the whole set.");
            Assert.AreEqual(4, Enum.GetValues(typeof(UiLayer)).Length,
                "and the layer enum must not grow past what the shell builds");
        }

        // ---- L-18: one font authority ------------------------------------------

        [Test]
        public void NothingResolvesItsOwnFont()
        {
            // Twelve classes each called GetBuiltinResource<Font> for themselves,
            // which is why swapping the face for TMP is a migration rather than an
            // edit. NebulaUiFactory.SharedFont is the one place that decides.
            // The factory used to be exempted here, because it was the one place
            // allowed to resolve the bitmap face. With the level tag on SDF nothing
            // needs that face at all, so the exemption is gone and the rule is now
            // simply that no player-facing file resolves a built-in Font.
            var offenders = new List<string>();
            foreach (string file in RedesignSources())
            {
                if (CodeOnly(file).Contains("GetBuiltinResource<Font>"))
                    offenders.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(offenders,
                "Font resolution belongs to NebulaUiFactory.SharedFontAsset: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// L-19: the migration is finished, so nothing player-facing is left on the
        /// bitmap face.
        /// <para>
        /// The level tag over the ship was the last holdout, and it was kept on
        /// <c>TextMesh</c> because the factory's comment said a world-space tag
        /// "cannot take an SDF asset". It can — <c>TextMeshPro</c> is the world-space
        /// component and takes the same asset the screen-space labels use. The
        /// exemption was a wrong belief rather than a constraint, so it read as
        /// closed while one player-facing label still rendered on LegacyRuntime.
        /// </para>
        /// <para>
        /// <see cref="RedesignSources"/> already drops the two dev-only panels and
        /// the deferred StrategicMap, which is exactly the set the removal matrix
        /// permits to keep the legacy face. Everything it still yields is shipped to
        /// a player and must be SDF.
        /// </para>
        /// </summary>
        [Test]
        public void NoPlayerFacingTextIsLeftOnTheLegacyFont()
        {
            // \b before Pro is what keeps TextMeshPro/TextMeshProUGUI out of this:
            // "P" is a word character, so \bTextMesh\b cannot match them.
            var legacyComponent = new Regex(@"\bTextMesh\b");
            var legacyFace = new Regex(@"LegacyRuntime\.ttf");

            var offenders = new List<string>();
            foreach (string file in RedesignSources())
            {
                string code = CodeOnly(file);
                if (legacyComponent.IsMatch(code) || legacyFace.IsMatch(code))
                    offenders.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(offenders,
                "Player-facing text must be TMP/SDF; only the dev panels and the "
                + "deferred map may keep the bitmap face: " + string.Join(", ", offenders));
        }

        // ---- L-09/L-10: nothing rebuilds a collection every frame ---------------

        [Test]
        public void ThreatMarkersArePooledRatherThanRebuilt()
        {
            NebulaHudTargeting targeting = NebulaHudTargeting.Create(shell, null, null);

            int markers = 0;
            foreach (Transform child in shell.Root(UiLayer.HudDynamic).GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("Marker ")) markers++;

            Assert.AreEqual(NebulaHudTargeting.MaxMarkers, markers,
                "every marker exists before the first frame, so a frame allocates none");

            // The counters are the other half: nothing is shown and nothing has
            // overflowed, so a frame that draws no marker allocates no marker.
            Assert.AreEqual(0, targeting.VisibleMarkers);
            Assert.AreEqual(0, targeting.OverflowCount);
        }

        // ---- L-06: the authored panel family, not flat rectangles ---------------

        [Test]
        public void PanelsUseTheAuthoredFamily()
        {
            Assert.IsNotNull(NebulaUiAssetCatalog.PanelPrimary, "frame.panel.primary is missing");
            Assert.IsNotNull(NebulaUiAssetCatalog.PanelSecondary, "frame.panel.secondary is missing");
            Assert.IsNotNull(NebulaUiAssetCatalog.PanelElevated, "frame.panel.elevated is missing");

            Assert.AreNotSame(NebulaUiAssetCatalog.PanelPrimary, NebulaUiAssetCatalog.PanelElevated,
                "three elevation levels, or there is no hierarchy to read");
        }

        /// <summary>
        /// L-06's other half. 85 loose PNGs is correct output and the wrong
        /// packaging: each is its own texture binding, which is exactly the cost
        /// the four-canvas split was meant to stop paying.
        /// </summary>
        [Test]
        public void EverySpriteFolderIsPacked()
        {
            string spriteRoot = Path.Combine(Application.dataPath, "Resources", "UI");
            string atlasRoot = Path.Combine(spriteRoot, "Atlases");
            Assert.IsTrue(Directory.Exists(atlasRoot), "no atlases have been built");

            foreach (string folder in Directory.GetDirectories(spriteRoot))
            {
                string name = Path.GetFileName(folder);
                if (name == "Atlases") continue;
                if (Directory.GetFiles(folder, "*.png").Length == 0) continue;

                string atlas = Path.Combine(atlasRoot, $"ui.{name.ToLowerInvariant()}.spriteatlas");
                Assert.IsTrue(File.Exists(atlas),
                    $"{name} has sprites but no atlas -- run Nebula/UI/Rebuild Sprite Atlases");
            }
        }

        // ---- the tooltip layer, which existed as a layer and nothing else -------

        /// <summary>
        /// Every disabled control already carried the service's own refusal and
        /// there was nowhere for it to appear. Stored is not the same as reachable.
        /// </summary>
        [Test]
        public void ADisabledControlCarriesItsReasonToTheTooltip()
        {
            NebulaTooltip tooltip = NebulaTooltip.Create(shell);
            Assert.IsFalse(tooltip.Visible, "nothing hovered, nothing shown");

            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);

            NebulaUiFactory.ButtonHandle button = NebulaUiFactory.ButtonControl(
                (RectTransform)host.transform, "Blocked", "DENE",
                NebulaUiAssetCatalog.ButtonKind.Primary, null);
            button.SetAvailable(false, "KOMUTA KAPASİTESİ YETERSİZ");

            var source = button.Button.GetComponent<NebulaTooltipSource>();
            Assert.IsNotNull(source, "every control the factory makes can explain itself");
            Assert.AreEqual("KOMUTA KAPASİTESİ YETERSİZ", source.Reason(),
                "and the reason it gives is the service's, resolved live");

            tooltip.Show(source.Reason(), new Vector2(400f, 400f));
            Assert.IsTrue(tooltip.Visible);
            Assert.AreEqual("KOMUTA KAPASİTESİ YETERSİZ", tooltip.Text);

            tooltip.Hide();
            Assert.IsFalse(tooltip.Visible);
        }

        [Test]
        public void AnAvailableControlHasNothingToExplain()
        {
            NebulaTooltip.Create(shell);
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);

            NebulaUiFactory.ButtonHandle button = NebulaUiFactory.ButtonControl(
                (RectTransform)host.transform, "Fine", "DENE",
                NebulaUiAssetCatalog.ButtonKind.Primary, null);
            button.SetAvailable(true);

            Assert.IsNull(button.Button.GetComponent<NebulaTooltipSource>().Reason(),
                "a control that is not refusing has no refusal to show");
        }

        /// <summary>
        /// A tooltip that eats the click meant for the control it describes is
        /// worse than no tooltip.
        /// </summary>
        [Test]
        public void TheTooltipNeverTakesAClick()
        {
            NebulaTooltip tooltip = NebulaTooltip.Create(shell);
            tooltip.Show("BİR SEBEP", new Vector2(400f, 400f));

            foreach (Graphic graphic in shell.Root(UiLayer.Overlay).GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.name.Contains("Tooltip")
                    && (graphic.transform.parent == null
                        || !graphic.transform.parent.name.Contains("Tooltip"))) continue;

                Assert.IsFalse(graphic.raycastTarget, $"{graphic.name} would swallow the click");
            }
        }

        /// <summary>
        /// L-05. Planets were the last place a stable id reached the player,
        /// because PlanetManifest carries an index and an id and only systems have
        /// a DisplayName. The way out was composition rather than invention: the
        /// system's name plus an orbit numeral, which is how planets are named in
        /// the real sky and costs no new content.
        /// </summary>
        [Test]
        public void APlanetIsNamed_NeverPrintedAsItsId()
        {
            // Built with the codec, so the test cannot drift from the real format.
            string id = Nebula.Sectors.WorldIdCodec.PlanetId("0.0.0/system.03", 2);
            string name = NebulaPlanetName.Of(id);

            Assert.AreNotEqual(id, name, "the id itself must never be the answer");
            StringAssert.DoesNotContain("/", name, "nor any part of it");
            StringAssert.Contains("III", name, "the third orbit reads as the third orbit");
        }

        [Test]
        public void AnUnresolvablePlanetFallsBackToItsOrbit_NotItsId()
        {
            // A planet in a sector that has never been streamed has no manifest,
            // and asking for one is not worth a hitch. A number the player can
            // count still beats a string they can only be confused by.
            string name = NebulaPlanetName.Of(
                Nebula.Sectors.WorldIdCodec.PlanetId("99.99.99/system.99", 0));

            StringAssert.DoesNotContain("99", name);
            StringAssert.DoesNotContain("/", name);
            Assert.IsNotEmpty(name);
        }

        [Test]
        public void ANonsensePlanetIdStillProducesSomethingReadable()
        {
            foreach (string id in new[] { null, "", "nonsense", "a/b" })
            {
                string name = NebulaPlanetName.Of(id);
                Assert.IsNotEmpty(name, $"'{id}' produced nothing at all");
                StringAssert.DoesNotContain("/", name);
            }
        }

        /// <summary>
        /// Block compression turns a one-pixel hairline into a smear, and the whole
        /// panel family is hairlines and hard chamfers. An atlas left on the default
        /// would quietly undo the art.
        /// </summary>
        [Test]
        public void AtlasesAreNotBlockCompressed()
        {
            string atlasRoot = Path.Combine(Application.dataPath, "Resources", "UI", "Atlases");
            if (!Directory.Exists(atlasRoot)) Assert.Ignore("no atlases built");

            foreach (string file in Directory.GetFiles(atlasRoot, "*.spriteatlas"))
            {
                string text = File.ReadAllText(file);
                StringAssert.Contains("textureCompression: 0", text,
                    $"{Path.GetFileName(file)} is compressed; the hairlines will smear");
            }
        }

        // ---- L-17: one modal owner ----------------------------------------------

        [Test]
        public void OnlyTheShellOwnsTheModalGate()
        {
            // Four classes each had their own copy of the same eighteen-line pause
            // block. One divergence between four copies is one stuck-paused bug.
            var offenders = new List<string>();
            foreach (string file in RedesignSources())
            {
                if (Path.GetFileName(file) == "NebulaUiShell.cs") continue;
                if (CodeOnly(file).Contains("Time.timeScale ="))
                    offenders.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(offenders,
                "The pause block belongs to NebulaUiShell alone: " + string.Join(", ", offenders));
        }

        // ---- typography survives the scale range --------------------------------

        [Test]
        public void TypographyStaysReadableAcrossTheScaleRange()
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            menu.AddPage(new NebulaOverviewPage(null));
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            foreach (float scale in new[] { NebulaUiShell.MinUiScale, 1f, NebulaUiShell.MaxUiScale })
            {
                NebulaUiShell.UiScale = scale;
                Canvas.ForceUpdateCanvases();

                foreach (Text text in shell.Root(UiLayer.Menu).GetComponentsInChildren<Text>(true))
                {
                    if (!text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) continue;
                    Assert.GreaterOrEqual(text.fontSize, 9,
                        $"{text.name} is {text.fontSize}pt at {scale:0.00}x -- below the readable floor");
                }
            }

            NebulaUiShell.UiScale = 1f;
        }
    }
}
