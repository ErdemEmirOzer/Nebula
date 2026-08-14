using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.UI.Hud;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The Command Center, built the way a player builds it: in a booted session,
    /// by clicking the control on the surface-construction card.
    /// <para>
    /// Nothing here reaches a private method by reflection and nothing calls
    /// <c>SurfaceStructure.Create</c> to arrange a success. Every build in this
    /// fixture goes through <c>Button.onClick</c>, and every assertion is made
    /// against the backend afterwards — the structure, its node, its lifecycle,
    /// and the reservation in the hold. A control that changed a caption and
    /// nothing else would pass a "the button works" test and fail every one of
    /// these.
    /// </para>
    /// <para>
    /// The refusal tests all assert the same three things together: no structure
    /// appeared, not one unit left the hold, and the card said why. Two out of
    /// three is the silent no-op this work exists to remove.
    /// </para>
    /// </summary>
    public class CommandCenterAccessibilityPlayModeTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
        }

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        // =====================================================================
        // Getting to a planet, honestly
        // =====================================================================

        private static IEnumerator Boot()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
        }

        private static NebulaContext Ctx()
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            Assert.IsNotNull(context, "there is no live session behind this test");
            return context;
        }

        private static PlanetIdentity OuterPlanet(NebulaContext context)
        {
            PlanetManifest outer = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            return ExtractionState.FindPlanet(outer.PlanetId);
        }

        /// <summary>
        /// Parks the ship physically alongside the outermost planet. The extraction
        /// driver still has to notice the planet on its own — nothing assigns
        /// <c>TargetPlanet</c>.
        /// </summary>
        private static IEnumerator Park(NebulaContext context, bool scanned = true)
        {
            PlanetIdentity planet = OuterPlanet(context);
            if (scanned) ExtractionState.CompletePlanetScan(planet);

            context.Ship.transform.position =
                planet.Body.transform.position + new Vector3(planet.Body.Radius * 1.1f, 0f, 0f);
            yield return null;
            yield return null;

            var extraction = context.Ship.GetComponent<ShipExtraction>();
            Assert.AreSame(planet, extraction.TargetPlanet,
                "the ship never noticed the planet it is parked on");
        }

        private static NebulaHudPlanetContext Card(NebulaContext context)
        {
            NebulaHudPlanetContext card = context.Presentation.HudPlanetContext;
            Assert.IsNotNull(card, "the session built no planet-context panel");
            return card;
        }

        /// <summary>The control the player clicks, found under the live HUD by name.</summary>
        private static Button CommandButton()
        {
            NebulaUiShell shell = Ctx().Presentation.UiShell;
            foreach (Button button in shell.Root(UiLayer.HudStatic).GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == NebulaHudPlanetContext.CommandActionButtonName) return button;
            return null;
        }

        /// <summary>
        /// Waits for a condition on wall-clock time, not on a frame count.
        /// <para>
        /// The card refreshes on its own 0.2s cadence and a batchmode frame is far
        /// shorter than a real one, so "wait sixty frames" is a fraction of a
        /// second here and times out on a panel that is working perfectly.
        /// </para>
        /// </summary>
        private static IEnumerator Until(System.Func<bool> ready, string what)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(ready(), what);
        }

        /// <summary>Lets at least one full card refresh land, whatever the frame rate is.</summary>
        private static IEnumerator Refreshed()
        {
            float until = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private static IEnumerator CardUp(NebulaHudPlanetContext card)
        {
            yield return Until(() => card.CommandVisible, "the surface-construction card never appeared");
            yield return Refreshed();
        }

        private static ShipExtraction Extraction(NebulaContext context) =>
            context.Ship.GetComponent<ShipExtraction>();

        private static LogisticsCargo Hold(NebulaContext context) =>
            context.Ship.GetComponent<ShipLogistics>().Cargo;

        /// <summary>Exactly the T1 recipe for this planet's family, and nothing else.</summary>
        private static void StockExactly(LogisticsCargo cargo, ResourceFamily family, float multiplier = 1f)
        {
            cargo.Clear();
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 10000f));
            foreach (var (itemId, amount) in CommandBuildService.BuildT1Cost(family))
                cargo.Add(itemId, amount * multiplier);
        }

        private static ResourceFamily FamilyOf(PlanetIdentity planet) =>
            ExtractionState.GetProfile(planet).Family;

        private static SurfaceStructure Command(PlanetIdentity planet) =>
            ExtractionState.FindStructure(planet.WorldId, SurfaceStructureType.Command);

        /// <summary>A snapshot of the whole hold, for "not one unit moved" assertions.</summary>
        private static Dictionary<string, float> Snapshot(LogisticsCargo cargo)
        {
            var map = new Dictionary<string, float>();
            foreach (var kv in cargo.Items) map[kv.Key] = kv.Value;
            return map;
        }

        private static void AssertHoldUntouched(LogisticsCargo cargo, Dictionary<string, float> before, string why)
        {
            foreach (var kv in before)
                Assert.AreEqual(kv.Value, cargo.GetStored(kv.Key), 0.0001f, $"{why}: {kv.Key} stock moved");
            foreach (var kv in cargo.Items)
                Assert.IsTrue(before.ContainsKey(kv.Key), $"{why}: {kv.Key} appeared out of nowhere");
            foreach (var kv in before)
                Assert.AreEqual(0f, cargo.GetReserved(kv.Key), 0.0001f, $"{why}: {kv.Key} was reserved anyway");
        }

        // =====================================================================
        // 1 — the happy path, through the real control
        // =====================================================================

        [UnityTest]
        public IEnumerator TheCardsButtonBuildsOneCommandCenterOnTheCommandNode()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            StockExactly(Hold(context), FamilyOf(planet));

            yield return Park(context);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            Button build = CommandButton();
            Assert.IsNotNull(build, "there is no Command control on the card");
            Assert.IsTrue(build.interactable,
                $"the control is dead with the exact recipe in the hold: {card.CommandBlockedReason}");
            Assert.AreEqual("KUR · T1", card.CommandCaption);
            Assert.AreEqual("İNŞA EDİLMEDİ", card.CommandStatusText);

            int structuresBefore = ExtractionState.Structures.Count;
            build.onClick.Invoke();
            yield return null;

            Assert.AreEqual(structuresBefore + 1, ExtractionState.Structures.Count,
                $"the click built nothing. Msg={card.CommandMessageShown}");

            SurfaceStructure command = Command(planet);
            Assert.IsNotNull(command, "no Command Center exists on the planet the ship is at");
            Assert.AreEqual(ExtractionState.GetProfile(planet).CommandNode.Id, command.NodeId,
                "the Command Center landed on the wrong surface node");
            Assert.AreEqual(DrillLifecycle.CapsuleInTransit, command.Runtime.State,
                "a fresh build must start as a capsule in flight, not as a finished building");
            Assert.AreEqual(1, command.Runtime.Tier);

            yield return null;
            StringAssert.Contains("KAPSÜL", card.CommandMessageShown ?? "",
                "a successful launch said nothing about the capsule");
        }

        /// <summary>Thirty parts and ten of this planet's own material, reserved — not consumed, not approximated.</summary>
        [UnityTest]
        public IEnumerator TheBuildReservesExactlyThirtyPartsAndTenOfThisPlanetsMaterial()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            ResourceFamily family = FamilyOf(planet);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, family, multiplier: 4f);

            yield return Park(context);
            yield return CardUp(Card(context));

            CommandButton().onClick.Invoke();
            yield return null;

            SurfaceStructure command = Command(planet);
            Assert.IsNotNull(command);

            string materialId = CommandBuildService.MaterialIdFor(family);
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost,
                cargo.GetOwnerReservation(command.BuildOwnerId, ProcessingCatalog.BasicPartId), 0.0001f,
                "the parts reservation is not the catalog's 30");
            Assert.AreEqual(SurfaceStructureCatalog.Command.MaterialCost,
                cargo.GetOwnerReservation(command.BuildOwnerId, materialId), 0.0001f,
                "the material reservation is not the catalog's 10");

            // Everything reserved is still physically present until it completes.
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost * 4f,
                cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "reserving quietly consumed the stock");

            foreach (string otherId in ProcessingCatalog.MaterialIds)
                if (otherId != materialId)
                    Assert.AreEqual(0f, cargo.GetOwnerReservation(command.BuildOwnerId, otherId), 0.0001f,
                        $"{otherId} was reserved for a planet whose family does not use it");
        }

        // =====================================================================
        // 2 — the refusals: nothing built, nothing spent, and it says so
        // =====================================================================

        /// <summary>A hold full of every other family's material still cannot pay for this one.</summary>
        [UnityTest]
        public IEnumerator TheWrongFamilysMaterialIsRefusedEvenWhenThereIsPlentyOfIt()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            ResourceFamily family = FamilyOf(planet);
            string needed = CommandBuildService.MaterialIdFor(family);

            LogisticsCargo cargo = Hold(context);
            cargo.Clear();
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 20000f));
            cargo.Add(ProcessingCatalog.BasicPartId, 500f);
            foreach (string materialId in ProcessingCatalog.MaterialIds)
                if (materialId != needed) cargo.Add(materialId, 500f);

            yield return Park(context);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            Dictionary<string, float> before = Snapshot(cargo);
            int structuresBefore = ExtractionState.Structures.Count;

            Button build = CommandButton();
            Assert.IsFalse(build.interactable, "the control offered a build the planet's own material cannot pay for");
            build.onClick.Invoke();
            yield return null;

            Assert.AreEqual(structuresBefore, ExtractionState.Structures.Count, "a refused build built something");
            Assert.IsNull(Command(planet));
            AssertHoldUntouched(cargo, before, "the wrong-family refusal");
            StringAssert.Contains("KAYNAK YETERSİZ", card.CommandBlockedReason ?? "",
                "the refusal did not name the missing resource");
        }

        /// <summary>Stock is not availability: everything promised elsewhere buys nothing.</summary>
        [UnityTest]
        public IEnumerator ResourcesReservedByAnotherJobCannotBeSpentAgain()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            ResourceFamily family = FamilyOf(planet);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, family);

            Assert.IsTrue(cargo.TryReserveAll("some-other-job", CommandBuildService.BuildT1Cost(family)),
                "the fixture could not set up the competing reservation");

            yield return Park(context);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            int structuresBefore = ExtractionState.Structures.Count;
            Assert.IsFalse(CommandButton().interactable, "reserved stock was offered for a second spend");
            CommandButton().onClick.Invoke();
            yield return null;

            Assert.AreEqual(structuresBefore, ExtractionState.Structures.Count);
            Assert.IsNull(Command(planet));
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost,
                cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.0001f,
                "the other job's reservation was disturbed");
            StringAssert.Contains("KAYNAK YETERSİZ", card.CommandBlockedReason ?? "");

            // And the card is showing the player exactly why a full hold buys nothing:
            // needed 30, available 0, stored 30, of which 30 is reserved elsewhere.
            IReadOnlyList<string> lines = card.CommandCostLinesShown;
            Assert.Greater(lines.Count, 0, "the card is showing no cost at all");
            StringAssert.Contains("EKSİK", lines[0], "the short line is not marked short in words");
            float parts = SurfaceStructureCatalog.Command.PartsCost;
            StringAssert.Contains($"{parts:0} · 0 · {parts:0} · {parts:0}", lines[0],
                "the card is not showing required/available/stored/reserved as four separate numbers");
            StringAssert.Contains("AYRILAN", NebulaHudPlanetContext.CommandCostHeader,
                "the number column never names the reserved figure");
        }

        [UnityTest]
        public IEnumerator AnUnscannedPlanetRefusesTheBuildAndNamesNoMaterial()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, FamilyOf(planet), multiplier: 10f);

            yield return Park(context, scanned: false);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            Dictionary<string, float> before = Snapshot(cargo);
            Assert.IsFalse(CommandButton().interactable);
            CommandButton().onClick.Invoke();
            yield return null;

            Assert.IsNull(Command(planet), "an unscanned planet accepted a Command Center");
            AssertHoldUntouched(cargo, before, "the unscanned refusal");
            Assert.AreEqual("ÖNCE GEZEGENİ TARA", card.CommandBlockedReason);

            // The information gate: the family material must not be on screen.
            string familyMaterialName = CommandBuildText.ItemName(
                CommandBuildService.MaterialIdFor(FamilyOf(planet)));
            foreach (string line in card.CommandCostLinesShown)
                StringAssert.DoesNotContain(familyMaterialName, line,
                    "the card named the family material before the scan");
        }

        [UnityTest]
        public IEnumerator OutOfRangeRefusesAndSaysHowFarAndHowClose()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, FamilyOf(planet), multiplier: 10f);

            yield return Park(context);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            // Still inside the driver's interest radius (so the planet stays the
            // target and the card stays up), but outside the build range.
            var extraction = Extraction(context);
            context.Ship.transform.position =
                planet.Body.transform.position + new Vector3(planet.Body.Radius * 2.6f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreSame(planet, extraction.TargetPlanet, "the ship lost the planet entirely");

            yield return Until(() => !card.CommandAvailable, "the control stayed usable after the world said no");

            Dictionary<string, float> before = Snapshot(cargo);
            CommandButton().onClick.Invoke();
            yield return null;

            Assert.IsNull(Command(planet), "a build was accepted from out of range");
            AssertHoldUntouched(cargo, before, "the out-of-range refusal");
            StringAssert.Contains("GEZEGENE YAKLAŞ", card.CommandBlockedReason ?? "");
            StringAssert.Contains("YÜKSEKLİK", card.CommandBlockedReason ?? "",
                "the refusal did not say how far away the ship actually is");
        }

        [UnityTest]
        public IEnumerator AnEvacuationLockRefusesTheBuildAndSaysSo()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, FamilyOf(planet), multiplier: 10f);

            yield return Park(context);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            ExtractionState.SetEvacuationLocked(planet.WorldId, true);
            try
            {
                yield return Until(() => !card.CommandAvailable, "the control stayed usable after the world said no");

                Dictionary<string, float> before = Snapshot(cargo);
                CommandButton().onClick.Invoke();
                yield return null;

                Assert.IsNull(Command(planet), "a build started during an evacuation");
                AssertHoldUntouched(cargo, before, "the evacuation-lock refusal");
                StringAssert.Contains("TAHLİYE", card.CommandBlockedReason ?? "");
            }
            finally
            {
                ExtractionState.SetEvacuationLocked(planet.WorldId, false);
            }
        }

        [UnityTest]
        public IEnumerator AnOccupiedCommandNodeRefusesAndOffersNoVerb()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, profile.Family, multiplier: 10f);

            yield return Park(context);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            // A Laser standing on the Command node — the node is taken by something
            // that is not a Command Center, so there is no verb at all.
            SurfaceStructure squatter = SurfaceStructure.Create(planet, profile,
                SurfaceStructureCatalog.Laser, profile.CommandNode, context.Ship.transform.position,
                null, new List<(string, float)>());
            try
            {
                yield return Until(() => !card.CommandAvailable, "the control stayed usable after the world said no");

                Dictionary<string, float> before = Snapshot(cargo);
                CommandButton().onClick.Invoke();
                yield return null;

                Assert.IsNull(Command(planet), "a Command Center was built onto an occupied node");
                AssertHoldUntouched(cargo, before, "the occupied-node refusal");
                Assert.AreEqual("KOMUTA YUVASI DOLU", card.CommandBlockedReason);
                StringAssert.Contains("DOLU", card.CommandNodeText,
                    "the node line still reads as empty with something standing on it");
                Assert.AreEqual("İNŞA EDİLMEDİ", card.CommandStatusText,
                    "a Laser on the Command node was reported as a Command Center");
            }
            finally
            {
                Object.DestroyImmediate(squatter.gameObject);
            }
        }

        /// <summary>With no planet at all the card is simply not there — and nothing is silently built.</summary>
        [UnityTest]
        public IEnumerator WithNoTargetPlanetTheCardIsGoneAndTheSeamStillAnswers()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            var extraction = Extraction(context);

            context.Ship.transform.position = new Vector3(0f, 4_000_000f, 0f);
            yield return null;
            yield return null;

            // Asked the instant the target is gone. The ship is still a physical
            // body out here, and a long wait before the call lets it drift back
            // inside a planet's interest radius — which would quietly turn this
            // into a test of the opposite situation.
            Assert.IsNull(extraction.TargetPlanet, "the ship is still near something");

            int structuresBefore = ExtractionState.Structures.Count;
            CommandActionOutcome outcome = extraction.TryBuildOrUpgradeCommand();

            Assert.AreEqual(structuresBefore, ExtractionState.Structures.Count,
                "something was built with no planet to build it on");
            Assert.IsFalse(outcome.Succeeded, "the seam accepted a build with no target");
            Assert.AreEqual(CommandBuildRejectReason.NoTarget, outcome.BuildReject,
                "the seam returned silently instead of naming what is missing");
            Assert.IsFalse(string.IsNullOrEmpty(CommandBuildText.OutcomeText(outcome)));

            NebulaHudPlanetContext card = Card(context);
            yield return Until(() => !card.CommandVisible || extraction.TargetPlanet != null,
                "the card stayed up with no planet under the ship");
            if (extraction.TargetPlanet == null)
                Assert.IsFalse(card.CommandVisible, "the card stayed up with no planet under the ship");
        }

        // =====================================================================
        // 3 — one press, one structure
        // =====================================================================

        [UnityTest]
        public IEnumerator TwoFastClicksProduceOneStructureAndOneReservation()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            ResourceFamily family = FamilyOf(planet);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, family, multiplier: 6f);

            yield return Park(context);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            Button build = CommandButton();
            int structuresBefore = ExtractionState.Structures.Count;

            // Two clicks inside one frame: no refresh, no state read, nothing
            // between them but the seam's own re-evaluation.
            build.onClick.Invoke();
            build.onClick.Invoke();
            yield return null;

            Assert.AreEqual(structuresBefore + 1, ExtractionState.Structures.Count,
                "a double click built two Command Centers");

            SurfaceStructure command = Command(planet);
            Assert.IsNotNull(command);
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost,
                cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.0001f,
                "the second click reserved a second set of parts");
            Assert.AreEqual(SurfaceStructureCatalog.Command.MaterialCost,
                cargo.GetReserved(CommandBuildService.MaterialIdFor(family)), 0.0001f,
                "the second click reserved a second set of material");
            Assert.AreEqual(1, card.CommandAcceptancesShown,
                "the refused second click was still celebrated on screen");
        }

        // =====================================================================
        // 4 — lifecycle, and the reservation spent exactly once
        // =====================================================================

        [UnityTest]
        public IEnumerator TheCapsuleBecomesConstructionBecomesActiveAndSpendsTheReservationOnce()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            ResourceFamily family = FamilyOf(planet);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, family, multiplier: 3f);

            yield return Park(context);
            yield return CardUp(Card(context));

            float partsBefore = cargo.GetStored(ProcessingCatalog.BasicPartId);
            CommandButton().onClick.Invoke();
            yield return null;

            SurfaceStructure command = Command(planet);
            Assert.IsNotNull(command);
            Assert.AreEqual(DrillLifecycle.CapsuleInTransit, command.Runtime.State);

            // Driven through the production tick, not by writing the state.
            for (int i = 0; i < 400 && command.Runtime.State != DrillLifecycle.Active; i++)
            {
                command.Runtime.Tick(1f);
                yield return null;
            }
            Assert.AreEqual(DrillLifecycle.Active, command.Runtime.State,
                "the structure never finished building");

            // SurfaceStructure.Update consumes the reservation on the Active
            // transition; give it the frame it needs, and the card its refresh.
            yield return null;
            yield return null;
            yield return Until(() => Card(context).CommandStatusText == "AKTİF · T1",
                "the card never caught up with the finished build");

            Assert.AreEqual(0f, cargo.GetReserved(ProcessingCatalog.BasicPartId), 0.0001f,
                "the build reservation outlived the build");
            Assert.AreEqual(partsBefore - SurfaceStructureCatalog.Command.PartsCost,
                cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the parts were consumed a number of times that is not exactly one");
            Assert.AreEqual("AKTİF · T1", Card(context).CommandStatusText);
        }

        // =====================================================================
        // 5 — save and load in the middle of the build
        // =====================================================================

        [UnityTest]
        public IEnumerator ABuildSurvivesSaveAndLoadWithTheSameNodeTimeAndReservation()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            ResourceFamily family = FamilyOf(planet);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, family, multiplier: 3f);

            yield return Park(context);
            yield return CardUp(Card(context));

            CommandButton().onClick.Invoke();
            yield return null;

            SurfaceStructure command = Command(planet);
            Assert.IsNotNull(command);

            // Part-way into the construction, so the remaining time is a real
            // number rather than the whole duration.
            command.Runtime.Tick(command.Runtime.CapsuleRemaining + 12f);
            yield return null;
            Assert.AreEqual(DrillLifecycle.Constructing, command.Runtime.State);

            string nodeBefore = command.NodeId;
            float remainingBefore = command.Runtime.BuildRemaining;
            int structuresBefore = ExtractionState.Structures.Count;

            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(structuresBefore, ExtractionState.Structures.Count,
                "the load duplicated a structure");

            SurfaceStructure restored = Command(planet);
            Assert.IsNotNull(restored, "the half-built Command Center did not come back");
            Assert.AreEqual(nodeBefore, restored.NodeId, "it came back on a different node");
            Assert.AreEqual(DrillLifecycle.Constructing, restored.Runtime.State);
            Assert.AreEqual(remainingBefore, restored.Runtime.BuildRemaining, 1.5f,
                "the remaining build time was not preserved");

            LogisticsCargo restoredCargo = Hold(context);
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost,
                restoredCargo.GetOwnerReservation(restored.BuildOwnerId, ProcessingCatalog.BasicPartId), 0.0001f,
                "the parts reservation did not survive the load");
            Assert.AreEqual(SurfaceStructureCatalog.Command.MaterialCost,
                restoredCargo.GetOwnerReservation(restored.BuildOwnerId,
                    CommandBuildService.MaterialIdFor(family)), 0.0001f,
                "the material reservation did not survive the load");
        }

        // =====================================================================
        // 6 — the same control upgrades rather than duplicating
        // =====================================================================

        [UnityTest]
        public IEnumerator OnAnActiveT1TheSameControlBecomesTheT2UpgradeAndNamesItsGate()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            ResourceFamily family = FamilyOf(planet);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, family, multiplier: 3f);

            yield return Park(context);
            NebulaHudPlanetContext card = Card(context);
            yield return CardUp(card);

            CommandButton().onClick.Invoke();
            yield return null;
            SurfaceStructure command = Command(planet);
            Assert.IsNotNull(command);

            for (int i = 0; i < 400 && command.Runtime.State != DrillLifecycle.Active; i++)
            {
                command.Runtime.Tick(1f);
                yield return null;
            }
            Assert.AreEqual(DrillLifecycle.Active, command.Runtime.State);

            // A level-1 pilot with no controlled planet: the verb changes, and the
            // card names the first gate that is missing rather than going quiet.
            yield return Until(() => card.CommandCaption == "YÜKSELT · T2",
                "the control never became the upgrade on a finished T1");
            Assert.AreEqual("YÜKSELT · T2", card.CommandCaption,
                "the control still offers a fresh build on top of a finished one");
            Assert.IsFalse(card.CommandAvailable);
            Assert.IsFalse(string.IsNullOrEmpty(card.CommandBlockedReason));

            int structuresBefore = ExtractionState.Structures.Count;
            CommandButton().onClick.Invoke();
            yield return null;

            Assert.AreEqual(structuresBefore, ExtractionState.Structures.Count,
                "the second use built a second Command Center instead of trying the upgrade");
            Assert.IsNull(command.ActiveUpgradeJob, "an ungated upgrade started");

            // With the gates met, the same control starts the real upgrade.
            for (int i = 0; ProgressionService.State.Level
                     < PlanetaryDefenseCatalog.CommandT2.RequiredCharacterLevel && i < 40; i++)
                ProgressionService.Award("test.command-t2-" + i, 20000);
            Assert.GreaterOrEqual(ProgressionService.State.Level,
                PlanetaryDefenseCatalog.CommandT2.RequiredCharacterLevel,
                "the fixture could not reach the level the T2 gate needs");
            ExtractionState.GetDominance(planet.WorldId).Restore(DominanceState.Controlled, 0f, 0f);
            foreach (var (itemId, amount) in PlanetaryDefenseCatalog.CommandT2.FullCost())
                cargo.Add(itemId, amount * 2f);

            yield return Until(() => card.CommandAvailable,
                "the control never re-enabled with every gate met");
            Assert.IsTrue(card.CommandAvailable,
                $"the upgrade is still refused with every gate met: {card.CommandBlockedReason}");

            CommandButton().onClick.Invoke();
            yield return null;

            Assert.AreEqual(structuresBefore, ExtractionState.Structures.Count,
                "starting the upgrade created a structure");
            Assert.IsNotNull(command.ActiveUpgradeJob, "the upgrade never started");
            Assert.AreEqual(2, command.ActiveUpgradeJob.TargetTier);
            Assert.AreEqual(PlanetaryDefenseCatalog.CommandT2.BuildSeconds,
                command.ActiveUpgradeJob.TotalSeconds, 0.001f,
                "the upgrade is not running on the catalog's own duration");
            Assert.AreEqual(DrillLifecycle.Active, command.Runtime.State,
                "the structure stopped working during its own upgrade");
        }

        // =====================================================================
        // 7 — the key and the control are the same thing
        // =====================================================================

        [UnityTest]
        public IEnumerator TheKeyPathAndTheControlProduceTheSameOutcome()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            var extraction = Extraction(context);
            LogisticsCargo cargo = Hold(context);
            StockExactly(cargo, FamilyOf(planet), multiplier: 6f);

            yield return Park(context);
            yield return CardUp(Card(context));

            // The key's handler, called exactly as the K branch calls it.
            CommandActionOutcome viaKey = extraction.TryBuildOrUpgradeCommand();
            yield return null;
            Assert.IsTrue(viaKey.Succeeded, "the key path refused a build the card offered");
            Assert.AreEqual(CommandActionKind.BuildT1, viaKey.Attempted);

            SurfaceStructure built = Command(planet);
            Assert.IsNotNull(built);
            Object.DestroyImmediate(built.gameObject);
            cargo.ReleaseReservation(built.BuildOwnerId);
            yield return null;
            yield return null;

            // And the control, on the same world, reaches the same outcome.
            yield return Until(() => Card(context).CommandAvailable,
                "the control never came back after the first build was removed");
            CommandButton().onClick.Invoke();
            yield return null;

            SurfaceStructure viaButton = Command(planet);
            Assert.IsNotNull(viaButton, "the control refused what the key had just accepted");
            Assert.AreEqual(DrillLifecycle.CapsuleInTransit, viaButton.Runtime.State);
            Assert.AreEqual(built.NodeId, viaButton.NodeId, "the two paths chose different nodes");
        }

        /// <summary>
        /// The input gate switches the K key off while a menu is open — that is
        /// what it is for. It must not switch the control off with it, or the one
        /// visible way in disappears exactly when the player has a screen open.
        /// </summary>
        [UnityTest]
        public IEnumerator TheControlStillWorksWhileTheParentMenuHasInputSuppressed()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            PlanetIdentity planet = OuterPlanet(context);
            StockExactly(Hold(context), FamilyOf(planet), multiplier: 6f);

            yield return Park(context);
            yield return CardUp(Card(context));

            bool savedGate = InputGate.Suppressed;
            InputGate.Suppressed = true;
            try
            {
                yield return null;
                CommandButton().onClick.Invoke();
                yield return null;

                Assert.IsNotNull(Command(planet),
                    "the control was killed by the same gate that silences the keyboard");
            }
            finally
            {
                InputGate.Suppressed = savedGate;
            }
        }

        // =====================================================================
        // 8 — one card, however the session is opened and closed
        // =====================================================================

        [UnityTest]
        public IEnumerator OpeningAndClosingTheMenuLeavesExactlyOneCard()
        {
            yield return Boot();
            NebulaContext context = Ctx();
            StockExactly(Hold(context), FamilyOf(OuterPlanet(context)), multiplier: 6f);

            yield return Park(context);
            yield return CardUp(Card(context));

            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsNotNull(menu);
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Planets));
            for (int i = 0; i < 4; i++) yield return null;
            menu.Close();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, CountCommandControls(),
                "opening and closing the menu left more than one Command control");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaHudPlanetContext>(true).Length,
                "there is more than one planet-context panel alive");
        }

        [UnityTest]
        public IEnumerator RestartingTheSessionLeavesExactlyOneCard()
        {
            yield return Boot();
            yield return UiFinalPlayMode.EndSessionAndSettle();
            yield return UiFinalPlayMode.BeginSession();
            yield return UiFinalPlayMode.SettleAfterSessionStart();
            yield return UiFinalPlayMode.SettleToOneShell();

            NebulaContext context = Ctx();
            StockExactly(Hold(context), FamilyOf(OuterPlanet(context)), multiplier: 6f);
            yield return Park(context);
            yield return CardUp(Card(context));

            Assert.AreEqual(1, CountCommandControls(),
                "a restarted session left a second Command control behind");
        }

        private static int CountCommandControls()
        {
            int found = 0;
            NebulaUiShell shell = Ctx().Presentation.UiShell;
            foreach (Button button in shell.Root(UiLayer.HudStatic).GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == NebulaHudPlanetContext.CommandActionButtonName) found++;
            return found;
        }
    }
}
