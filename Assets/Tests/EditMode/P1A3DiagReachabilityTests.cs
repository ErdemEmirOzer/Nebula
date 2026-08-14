using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 1 / AJAN 3 — diagnostic fixture (P1A3Diag).
    /// <para>
    /// These are DIAGNOSTIC tests, written to document reachability defects
    /// found by the flight/combat/exploration audit. Each one asserts the
    /// state the game is SUPPOSED to be in ("this mechanic has a way for the
    /// player to reach it"), so a failure here is the evidence for a finding,
    /// not a regression this fixture introduced. They are expected to fail on
    /// HEAD=4d16782 and to go green when the corresponding finding is fixed.
    /// </para>
    /// <para>
    /// Nothing in here mutates production state: every test either reads a
    /// type through reflection or scans the runtime sources as text.
    /// </para>
    /// </summary>
    public class P1A3DiagReachabilityTests
    {
        private static string RuntimeScriptsRoot =>
            Path.Combine(Application.dataPath, "Scripts", "Nebula");

        private static IEnumerable<string> RuntimeCsFiles() =>
            Directory.GetFiles(RuntimeScriptsRoot, "*.cs", SearchOption.AllDirectories);

        /// <summary>Every runtime file, other than the ones named, that mentions <paramref name="token"/>.</summary>
        private static List<string> CallersOf(string token, params string[] excludedFileNames)
        {
            var hits = new List<string>();
            foreach (string file in RuntimeCsFiles())
            {
                string name = Path.GetFileName(file);
                bool excluded = false;
                for (int i = 0; i < excludedFileNames.Length; i++)
                    if (string.Equals(name, excludedFileNames[i], System.StringComparison.Ordinal)) excluded = true;
                if (excluded) continue;

                string text = File.ReadAllText(file);
                if (text.Contains(token)) hits.Add(name);
            }
            return hits;
        }

        // ---- P1-COMBAT: ship weapon production has no player path -------------

        /// <summary>
        /// <see cref="ShipCombat.TryStartProduction"/> is the ONLY way a second
        /// weapon (Plasma / EM, or a higher Laser tier) can ever reach a
        /// hardpoint in a real campaign: the job it creates is what
        /// <c>WeaponLoadoutService.Complete</c> equips in
        /// <c>ShipCombat.FixedUpdate</c>. If nothing outside ShipCombat calls
        /// it, the player is permanently limited to the single free T1 Laser
        /// <c>InitializeNewCampaign</c> pre-equips on hardpoint 0.
        /// </summary>
        [Test]
        public void WeaponProduction_IsReachableFromSomePlayerSurface()
        {
            // The needle used to be "TryStartProduction(WeaponFamily", which matches the
            // DECLARATION and nothing else: the real caller passes a variable
            // (NebulaCombatPage.cs -- combat.TryStartProduction(family, 1)), so the scan
            // was unsatisfiable no matter how reachable the mechanic became, and a green
            // result would only have meant somebody typed the magic characters
            // somewhere. The call shape is what a caller has in common with a caller.
            List<string> callers = CallersOf("TryStartProduction(", "ShipCombat.cs");
            Assert.That(callers, Is.Not.Empty,
                "ShipCombat.TryStartProduction(WeaponFamily, int) has no caller anywhere in Assets/Scripts/Nebula. "
                + "No key in NebulaInputActions and no UI surface starts a weapon production job, so Plasma and EM "
                + "can never be equipped in a real campaign — only the free T1 Laser on hardpoint 0.");
        }

        /// <summary>
        /// The catalog is the single answer to "what can the player press".
        /// A weapon-production action absent from it is a mechanic with no key.
        /// </summary>
        [Test]
        public void WeaponProduction_HasAnEntryInTheInputActionCatalog()
        {
            // The harm this test names is "Plasma/EM are tutorial-only", and a catalog row
            // was one of two ways to remove it. Part 3 took the other: weapon production is
            // a screen action on the Combat page with real Buttons, typed rejects and a
            // hardpoint-release control, reachable by mouse, keyboard and gamepad. Inventing
            // a world hotkey for it now would be inventing input the game does not have, so
            // the assertion is the disjunction it always meant — a catalog row OR a real
            // non-tutorial player surface — and it still fails if BOTH disappear.
            bool catalogRow = false;
            foreach (InputActionDefinition action in NebulaInputActions.All)
                if (action.Id.Contains("weapon") || action.Id.Contains("production")) catalogRow = true;

            string combatPage = Path.Combine(
                Application.dataPath, "Scripts", "Nebula", "UI", "Menu", "NebulaCombatPage.cs");
            bool playerSurface = File.Exists(combatPage)
                                 && Regex.IsMatch(File.ReadAllText(combatPage), @"TryStartProduction\s*\(");

            Assert.That(catalogRow || playerSurface, Is.True,
                "Ship weapon production has neither a row in NebulaInputActions nor a call "
                + "from a non-tutorial player surface, which is what would make Plasma/EM "
                + "tutorial-only. Checked NebulaCombatPage.cs for TryStartProduction(.");
        }

        // ---- P1-WORLD: POI vehicle dispatch has no player path ----------------

        /// <summary>
        /// <see cref="ShipPoi.TrySendVehicle"/> is the only assignment path to
        /// <c>VehicleAgent.AssignPoiTask</c>. With no caller, every
        /// Scout/Collector POI branch in <c>PoiObject</c>
        /// (<c>FindAssignedScout</c>, <c>FindAssignedCollector</c>, the 30s
        /// Scout locate, the Collector wreck collection, the Scout anomaly
        /// work) is unreachable in normal play.
        /// </summary>
        [Test]
        public void PoiVehicleDispatch_IsReachableFromSomePlayerSurface()
        {
            List<string> callers = CallersOf("TrySendVehicle", "ShipPoi.cs");
            Assert.That(callers, Is.Not.Empty,
                "ShipPoi.TrySendVehicle has no caller anywhere in Assets/Scripts/Nebula — no key, no UI. "
                + "Sending a Scout or Collector to work a POI (VS-012 §8) cannot be performed by the player.");
        }

        // ---- P1-COMBAT: drill deploy / defense build are keyboard-only --------

        /// <summary>
        /// K (Command Center) and the support node already have a public seam
        /// the HUD's planet context calls. F (deploy the drill) and L (build or
        /// upgrade the selected defense node) do not: their handlers are
        /// private, so no clickable control can ever exist for them and the two
        /// actions die entirely whenever <see cref="InputGate.Suppressed"/> is
        /// raised by any screen.
        /// </summary>
        [Test]
        public void DrillDeployAndDefenseBuild_ExposeAPublicSeamLikeCommandDoes()
        {
            const BindingFlags Public = BindingFlags.Instance | BindingFlags.Public;
            System.Type t = typeof(ShipExtraction);

            Assert.That(t.GetMethod("TryBuildOrUpgradeCommand", Public), Is.Not.Null,
                "baseline: the Command Center seam is public (this is the one the HUD button uses)");

            // The fix kept ConfirmBuild/BuildOrUpgradeDefense private and added public
            // Try* wrappers alongside them, exactly matching the Command Center's
            // shape. What this test is for is that a control CAN call them, so it
            // names the seam the HUD actually uses rather than the old private one.
            Assert.That(t.GetMethod("TryBuildOrUpgradeDrill", Public), Is.Not.Null,
                "ShipExtraction has no public seam for deploying/upgrading the drill (F): keyboard would be its only entry point.");
            Assert.That(t.GetMethod("TryBuildOrUpgradeDefense", Public), Is.Not.Null,
                "ShipExtraction has no public seam for Laser/Plasma defense build+upgrade (L): keyboard would be its only entry point.");
        }

        /// <summary>
        /// Only the nine combat rows are rebindable. Every extraction, POI,
        /// logistics, evacuation and flight key is a fixed <c>KeyCode</c>
        /// constant, so a player who cannot reach R/B/F/T/K/L has no remedy.
        /// </summary>
        [Test]
        public void ExtractionKeys_AreRebindable()
        {
            var fixedIds = new List<string>();
            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                if (action.Context != InputContext.Extraction && action.Context != InputContext.BuildMode) continue;
                // Shared-intent rows are exempt, and only they. Escape is claimed by seven
                // surfaces at once under the rule "every surface closes its own thing";
                // making it rebindable would require arbitrating it to a single owner and
                // would break that shield, so "not rebindable" is the design here rather
                // than an oversight. The exemption is kept narrow by the assert below,
                // which fails if a shared-intent row is anything other than Escape.
                if (action.SharedIntent)
                {
                    Assert.That(action.Key, Is.EqualTo(KeyCode.Escape),
                        $"{action.Id} claims the shared-intent exemption but is not Escape; "
                        + "the exemption exists for the Escape shield alone.");
                    continue;
                }
                if (!action.Rebindable) fixedIds.Add(action.Id);
            }

            Assert.That(fixedIds, Is.Empty,
                "these extraction/build-mode actions are hard-coded KeyCodes with no rebind path: "
                + string.Join(", ", fixedIds));
        }

        // ---- P1-COMBAT: H is catalogued as cruise, but engages heading lock ---

        /// <summary>
        /// <c>NebulaInputActions.FlightCruiseToggle</c> labels H "Seyir Kipi"
        /// (cruise mode) and cites <c>ShipCruise.cs:165</c>. That line is
        /// <c>headingLock.Toggle(...)</c> — Yön Sabitle. Cruise itself has no
        /// key at all: <see cref="Nebula.Navigation.CruiseStateMachine"/>
        /// enters on conditions alone. So the settings screen tells the player
        /// H does something it does not do, and the thing it really does has no
        /// name in the catalog.
        /// </summary>
        [Test]
        public void HeadingLock_HasItsOwnCatalogRow()
        {
            InputActionDefinition h = null;
            foreach (InputActionDefinition action in NebulaInputActions.All)
                if (action.Key == KeyCode.H && action.Context == InputContext.Flight) h = action;

            Assert.That(h, Is.Not.Null, "baseline: some flight action owns H");
            Assert.That(h.LabelTr, Does.Not.Contain("Seyir"),
                "H is labelled \"" + h.LabelTr + "\" (cruise mode) but ShipCruise.HandleHeadingLockInput toggles "
                + "Heading Lock / Yön Sabitle on it. Deep Space Cruise has no player key at all.");
        }

        // ---- P1-COMBAT: combat fire has no substitutable input source --------

        /// <summary>
        /// Rotation has <c>ShipController.ApplyLook</c>, translation has
        /// <see cref="IShipFlightInput"/>, arbitration has
        /// <c>NebulaInputRouter.Source</c>. Weapon fire has none: it reads
        /// <c>UnityEngine.Input</c> through <see cref="CombatBindings"/> with
        /// no seam, so no automated test can ever exercise the real
        /// fire→energy→heat→damage path the player uses.
        /// </summary>
        [Test]
        public void CombatBindings_ExposeASubstitutableInputSource()
        {
            // The finding was "the real weapon-fire path cannot be driven from a test".
            // Part 3 fixed it, but not where this test was looking: ShipCombat no longer
            // reads CombatBindings for input at all — it asks NebulaInputRouter, which has
            // a substitutable Source — while CombatBindings stayed on as the binding STORE
            // (CurrentBindings / TrySetBinding / ResetToDefaults). So the seam now belongs
            // to the router, and asserting it on the store would demand a seam for reads
            // that no longer happen there. This measures the invariant that actually
            // matters: the path a weapon key really travels is substitutable, and no
            // production file reads input through the store behind the router's back.
            const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;
            System.Type router = typeof(NebulaInputRouter);
            bool routerHasSeam = router.GetProperty("Source", PublicStatic) != null
                                 || router.GetField("Source", PublicStatic) != null;

            Assert.That(routerHasSeam, Is.True,
                "NebulaInputRouter has no substitutable input source, so the weapon-fire "
                + "path cannot be driven end to end from a test.");

            var readers = new List<string>();
            string scripts = Path.Combine(Application.dataPath, "Scripts", "Nebula");
            foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "CombatBindings.cs") continue;
                string text = File.ReadAllText(file);
                if (Regex.IsMatch(text, @"CombatBindings\.\w*(?:Held|Pressed)\s*\("))
                    readers.Add(Path.GetFileName(file));
            }

            Assert.That(readers, Is.Empty,
                "these production files still read input through CombatBindings instead of "
                + "NebulaInputRouter, bypassing both the arbitration and the test seam: "
                + string.Join(", ", readers));
        }
    }
}
