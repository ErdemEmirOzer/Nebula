using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-06: planet operations. The surface used to be summarised as counts
    /// ("DEFENSE 2/3") and its dominance checklist rendered in ASCII
    /// ("SCAN [OK] DRILL [--]"). Both are spatial facts, so both are now drawn —
    /// and the information gate has to survive that.
    /// </summary>
    public class NebulaPlanetsPageTests
    {
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
                Object.DestroyImmediate(shell.gameObject);
            }
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
        }

        private RectTransform Host()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            go.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        // ---- evacuation, which the HUD deliberately does not carry -------------

        /// <summary>
        /// The context bar shows one primary and two secondary, so an idle "you
        /// could abandon this planet" offer does not get a slot there -- only an
        /// evacuation already under way does. This page is where the player finds
        /// the action at all, so it has to actually be here.
        /// </summary>
        [Test]
        public void EvacuationIsOfferedHere_AndRefusesWithAReason()
        {
            var page = new NebulaPlanetsPage(null);
            page.Build(Host());
            page.Refresh();

            Assert.AreEqual("OTURUM YOK", page.EvacuateBlockedReason,
                "with no session the action must say why it cannot run");
        }

        [Test]
        public void WithNoPlanetSelected_EvacuationSaysSo()
        {
            var page = new NebulaPlanetsPage(null);
            page.Build(Host());
            page.Select(null);
            page.Refresh();

            Assert.IsNotNull(page.EvacuateBlockedReason);
            Assert.AreEqual(NebulaText.Upper(page.EvacuateBlockedReason),
                page.EvacuateBlockedReason,
                "disabled-reason copy is upper-case Turkish like every other refusal");
        }

        /// <summary>
        /// A selection that has dropped off the list cannot be acted on, and must
        /// not sit there pointing at a planet that is no longer shown.
        /// </summary>
        [Test]
        public void ASelectionThatLeavesTheList_IsCleared()
        {
            var page = new NebulaPlanetsPage(null);
            page.Build(Host());

            page.Select("no-such-planet");
            page.Refresh();

            Assert.IsNull(page.SelectedWorldId,
                "a stale selection is dropped rather than acted on");
        }

        [Test]
        public void BuildsAndRefreshesWithoutASession()
        {
            var page = new NebulaPlanetsPage(null);
            Assert.DoesNotThrow(() => page.Build(Host()));
            Assert.DoesNotThrow(() => page.Refresh());
            Assert.AreEqual(0, page.VisibleRows);
        }

        [Test]
        public void EveryDominanceState_HasPlayerFacingCopy()
        {
            foreach (DominanceState state in System.Enum.GetValues(typeof(DominanceState)))
            {
                string name = NebulaPlanetsPage.StateName(state);
                Assert.IsFalse(string.IsNullOrEmpty(name), $"{state} has no copy.");
                Assert.AreNotEqual(state.ToString(), name,
                    $"{state} is being shown as its raw enum name.");
            }
        }

        [Test]
        public void AnUnscannedPlanetRevealsNoSlotLayout()
        {
            // The information gate applies here exactly as it does on the HUD: an
            // unscanned surface must not disclose how many nodes it has or where.
            var slots = new UnityEngine.UI.Image[SurfaceSlotView.CanonicalSlotCount];
            var host = Host();
            for (int i = 0; i < slots.Length; i++)
            {
                var go = new GameObject("Slot " + i, typeof(RectTransform));
                go.transform.SetParent(host, false);
                slots[i] = go.AddComponent<UnityEngine.UI.Image>();
            }

            // GetProfile throws for a world with no manifest. A refresh must not
            // take the interface down over a stale id — it draws locked instead.
            Assert.DoesNotThrow(() => SurfaceSlotView.Paint("world-that-was-never-scanned", slots));

            Sprite locked = NebulaUiAssetCatalog.Slot(NebulaUiAssetCatalog.SlotState.Locked);
            foreach (UnityEngine.UI.Image slot in slots)
                Assert.AreSame(locked, slot.sprite,
                    "An unscanned planet disclosed part of its node layout.");
        }

        [Test]
        public void PaintingSlots_ToleratesAnEmptyOrNullWorldId()
        {
            var slots = new UnityEngine.UI.Image[2];
            var host = Host();
            for (int i = 0; i < slots.Length; i++)
            {
                var go = new GameObject("Slot " + i, typeof(RectTransform));
                go.transform.SetParent(host, false);
                slots[i] = go.AddComponent<UnityEngine.UI.Image>();
            }

            Assert.DoesNotThrow(() => SurfaceSlotView.Paint(null, slots));
            Assert.DoesNotThrow(() => SurfaceSlotView.Paint(string.Empty, slots));
        }

        [Test]
        public void PaintingSlots_ToleratesANullArray()
        {
            Assert.DoesNotThrow(() => SurfaceSlotView.Paint("anything", null));
        }

        [Test]
        public void RepeatedRefreshesCreateNothing()
        {
            var page = new NebulaPlanetsPage(null);
            RectTransform host = Host();
            page.Build(host);
            page.Refresh();

            int baseline = host.GetComponentsInChildren<Transform>(true).Length;
            for (int i = 0; i < 300; i++) page.Refresh();

            Assert.AreEqual(baseline, host.GetComponentsInChildren<Transform>(true).Length,
                "Refreshing rebuilt planet rows instead of repainting them.");
        }

        [Test]
        public void RowsAreCappedSoTheListCannotOverflowItsCard()
        {
            var page = new NebulaPlanetsPage(null);
            page.Build(Host());
            page.Refresh();
            Assert.LessOrEqual(page.VisibleRows, NebulaPlanetsPage.MaxPlanetRows);
        }
    }
}
