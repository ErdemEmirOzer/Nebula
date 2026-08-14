using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 4 / AJAN 1 — muharebe girdisi, oyuncunun sürdüğü yoldan.
    /// <para>
    /// Part 1 iki şey buldu ve ikisi de burada kapanıyor. <b>P1-COMBAT-004</b>:
    /// oyun görünümüne yapılan yakalama tıklaması aynı karede lazeri de ateşliyordu
    /// — tek girdi, iki eylem. <b>P1-TEST-042</b>: hiçbir test lazer/plazma/EM
    /// ateşini oyuncunun kullandığı yoldan sürmüyordu, çünkü sürülebilir bir dikiş
    /// yoktu; Part 3 dikişi <see cref="NebulaInputRouter.Source"/> üzerinde açtı ve
    /// bu fixture onu kullanıyor.
    /// </para>
    /// <para>
    /// <b>Ne taklit ediliyor, ne edilmiyor.</b> Yalnızca "hangi tuş basılı" sorusu
    /// taklit ediliyor. Kimin basışı sahiplendiği, silahın ateşleyip ateşlemediği,
    /// enerji/ısı/hasar — hepsi üretim yolunda kalıyor. Yakalama tıklaması da aynı
    /// kaynaktan geliyor, yani "tıklama imleci yakalar ama ateşlemez" iddiası
    /// gerçek tıklamayla ölçülüyor.
    /// </para>
    /// </summary>
    public class AuditPart4CombatInputTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;

        private readonly Dictionary<string, string> slotBackup = new Dictionary<string, string>();
        private readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        private readonly HashSet<KeyCode> down = new HashSet<KeyCode>();

        private static IEnumerable<string> SlotFiles()
        {
            string main = SaveService.SlotFile(GameplaySlot);
            string dir = Path.GetDirectoryName(main);
            yield return main;
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak1.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak2.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak3.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.tmp.json");
        }

        [SetUp]
        public void BackupRealGameplaySlot()
        {
            slotBackup.Clear();
            foreach (string f in SlotFiles())
                slotBackup[f] = File.Exists(f) ? File.ReadAllText(f) : null;

            held.Clear();
            down.Clear();
            downFrame = -1;
            InstallSubstitutedKeyboard();
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
            // The production reset contract, not a hand-kept list of statics: it is
            // what the game's own session teardown calls, so it cannot go stale
            // without the game going stale with it.
            NebulaSessionLifetime.ResetProcessWideState();

            foreach (var kv in slotBackup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        private IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;
            Object.FindObjectOfType<SolarSystemBootstrap>()?.BeginSession();
            yield return null;

            // The substituted keyboard is installed *here*, not in [SetUp], and that is
            // the whole reason this fixture used to work for its first test and fail for
            // every one after it.
            //
            // Loading the scene Single destroys the previous session's
            // NebulaSessionLifetime, whose Dispose runs ResetProcessWideState — which
            // puts the real keyboard back, exactly as it should when a session ends. A
            // source installed before the load is therefore gone by the time the test
            // presses anything, and the first test in the fixture escapes only because
            // there is no earlier session to tear down. The failure surfaced as "the
            // pointer was captured: expected True", naming neither the reset nor the load.
            InstallSubstitutedKeyboard();
        }

        /// <summary>Points the router's key sampling at this fixture's own key sets.</summary>
        private void InstallSubstitutedKeyboard()
        {
            // downFrame == int.MaxValue means "for as long as the press is held"; any
            // other value names the single frame the edge belongs to.
            NebulaInputRouter.Source = key =>
                (downFrame == int.MaxValue || downFrame == Time.frameCount) && down.Contains(key);
            NebulaInputRouter.HeldSource = key => held.Contains(key);
        }

        /// <summary>
        /// One frame with these keys down; the down-edge clears afterwards, as hardware
        /// does.
        /// <para>
        /// The edge is named by frame number rather than produced by "add now, clear
        /// after the next yield". Script execution order decides whether a
        /// MonoBehaviour's <c>Update</c> runs before or after the test coroutine
        /// resumes, so the add/clear form publishes the edge to whichever scripts happen
        /// to run on the right side of it and to no others.
        /// <c>ShipController.HandleCursor</c> was on the wrong side: the capture click
        /// was never seen at all, and the failure surfaced as "the pointer was captured:
        /// expected True", which names nothing that could be fixed. Gating on
        /// <c>Time.frameCount</c> makes every reader in that frame see the same press,
        /// whatever order they run in.
        /// </para>
        /// </summary>
        private IEnumerator PressForOneFrame(params KeyCode[] keys)
        {
            downFrame = Time.frameCount + 1;
            foreach (KeyCode key in keys) { down.Add(key); held.Add(key); }

            yield return null;  // the frame the edge belongs to
            yield return null;  // and past it, so the edge has certainly expired

            downFrame = -1;
            down.Clear();
            // Released as well. A click is a click: leaving the button held meant the
            // frames after the capture were still pressing the weapon's button, so
            // "the capture click did not fire" raced against the next frame firing.
            // Tests that want a hold say so with HoldOnly.
            held.Clear();
        }

        /// <summary>The one frame <see cref="down"/> is allowed to read as a down-edge.</summary>
        private int downFrame = -1;

        private void HoldOnly(params KeyCode[] keys)
        {
            held.Clear();
            foreach (KeyCode key in keys) held.Add(key);
        }

        /// <summary>
        /// The precondition <see cref="ShipController"/> needs before it will read a
        /// capture click at all: <c>Update</c> returns early while the input gate is
        /// raised, so a screen left open makes every capture in this fixture fail with
        /// a message about the pointer and none about the gate.
        /// </summary>
        private static void AssertFlightOwnsThePointer()
        {
            Assert.IsFalse(InputGate.Suppressed,
                "baseline: a screen holds the input gate, so ShipController.Update returns "
                + "before it ever reads the capture click");
        }

        /// <summary>
        /// Clicks the game view — the click that takes the pointer — for one frame.
        /// <para>
        /// The EventSystem is stood down for exactly that frame. Production refuses a
        /// capture while the pointer is over the HUD, which is right and is not what is
        /// under test; but in batch mode there is no physical pointer, so
        /// <c>IsPointerOverGameObject</c> answers from position (0,0), the HUD sits over
        /// that corner, and every capture is refused for a reason that has nothing to do
        /// with the player. Standing the EventSystem down is the only way the fixture can
        /// say "this click landed on the game view, not on a panel".
        /// </para>
        /// <para>
        /// Said through <see cref="ShipController.PointerOverUiSource"/>, the production
        /// seam for exactly this question. Switching the EventSystem's GameObject off
        /// was tried first and is worse than it looks: it perturbs the shell that owns
        /// the EventSystem, and the visible result was that the first test in this
        /// fixture captured the pointer while every test after it did not.
        /// </para>
        /// <para>
        /// Restored in a <c>finally</c>, so a failing assertion cannot leave the rest of
        /// the run believing the pointer is never over the interface.
        /// </para>
        /// </summary>
        private IEnumerator ClickTheGameView(params KeyCode[] keys)
        {
            ShipController.PointerOverUiSource = () => false;

            // Held until the capture is observed, then released.
            //
            // Chasing the edge with a frame number was tried and is not deterministic:
            // whether ShipController.Update runs before or after the test coroutine
            // resumes is script execution order, so a one-frame edge reaches some
            // readers and not others. The visible result was a fixture whose first test
            // captured the pointer and whose later ones did not, with every piece of
            // state the failure printed looking correct.
            //
            // WaitForEndOfFrame would be the exact tool and cannot be used: it never
            // fires under -batchmode -nographics, and the run simply hangs until the
            // watchdog kills it. Pressing until the capture is observed needs no such
            // guarantee — PointerAimLive is published in LateUpdate, so it stays false
            // for every frame of the press and the weapon has nothing to fire on until
            // the capture frame has already ended.
            downFrame = int.MaxValue;
            foreach (KeyCode key in keys) { down.Add(key); held.Add(key); }
            string sampled = "never sampled";
            try
            {
                for (int frame = 0; frame < 12 && !ShipController.PointerAimLive; frame++)
                {
                    ShipController ship = Object.FindObjectOfType<ShipController>();
                    sampled = $"frame{frame}: ship={(ship == null ? "null" : ship.isActiveAndEnabled.ToString())}, "
                              + $"gate={InputGate.Suppressed}, "
                              + $"routerSeesMouse0={NebulaInputRouter.KeyDown(KeyCode.Mouse0)}";
                    yield return null;
                }
            }
            finally
            {
                downFrame = -1;
                down.Clear();
                held.Clear();
                ShipController.PointerOverUiSource = null;
            }

            // The capture is this helper's whole job, so it reports its own failure with
            // the state that decides it. Asserted here rather than three call sites
            // later, where "expected True" could mean any of five things.
            Assert.IsTrue(ShipController.PointerAimLive,
                "the click did not take the pointer. "
                + $"EventSystem.current={(UnityEngine.EventSystems.EventSystem.current == null ? "null" : "live")}, "
                + $"InputGate.Suppressed={InputGate.Suppressed}, "
                + $"ShipControllers={Object.FindObjectsOfType<ShipController>(true).Length}, "
                + $"pressedKey={string.Join(",", keys)}, during-press[{sampled}]");
        }

        // =====================================================================
        // P1-COMBAT-004 — the capture click must not also fire
        // =====================================================================

        /// <summary>
        /// The click that takes the pointer captures it and fires nothing; the very
        /// next frame, the same button held fires the laser.
        /// <para>
        /// Both halves matter. The first is the defect: left mouse is
        /// <c>combat.fire_laser</c> as well as "capture the game view", and the two
        /// read the same button on the same frame, so the click that gave the player
        /// their crosshair also spent energy and dealt damage under it. The second is
        /// the guard against over-correcting — a fix that simply stopped mouse fire
        /// would pass the first assertion and break the weapon.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheCaptureClick_TakesThePointer_AndDoesNotFireTheLaser()
        {
            yield return LoadSceneFresh();
            var boot = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController ship = boot.Context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            CombatEncounterDirector director = StarSystemRegistry.Streaming?.CombatEncounter;

            Assert.IsNotNull(combat, "baseline: the ship has a ShipCombat");
            Assert.IsNotNull(combat.State, "baseline: combat state was initialized by the campaign start");
            Assert.IsNotNull(director, "baseline: the system has a combat encounter director");

            HardpointState laser = combat.Hardpoints[0];
            Assert.AreEqual(WeaponFamily.Laser, laser.Weapon,
                "baseline: hardpoint 0 carries the free T1 laser");

            // A target well inside range and dead ahead, so nothing but the capture
            // rule can be what stops the shot.
            EnemyAgent target = director.SpawnEnemy(EnemyIds.FighterMk1,
                ship.transform.position + ship.transform.forward * (WeaponCatalog.LaserRange(laser.Tier) * 0.3f));
            Assert.IsNotNull(target, "baseline: a target could be spawned");
            Assert.IsTrue(combat.TrySelectNearestTarget(), "baseline: the target can be locked");

            Assert.IsFalse(ShipController.PointerAimLive,
                "baseline: the pointer is not aiming before anything has been clicked");
            AssertFlightOwnsThePointer();

            int fired = 0;
            combat.WeaponFired += _ => fired++;
            // Total durability, not hull. A Fighter Mk I carries 120 shield and 150
            // armour in front of its 220 hull, so a few frames of laser move the top
            // layer and leave the hull untouched — "dealt no damage" was measuring the
            // wrong number and would have stayed red however well the weapon worked.
            float durabilityBefore = target.Combat.Hull + target.Combat.Shield + target.Combat.Armor;

            // The rule, stated the way production states it: the weapon is silent for
            // as long as the pointer is not aiming, and the button being held is not
            // enough. That is what stops the capturing click from also firing, and it
            // is checkable without racing a single frame — an assertion timed to "the
            // capture frame" cannot be, because whether ShipCombat.Update runs before
            // or after the test coroutine on the frame the latch flips is script
            // execution order, and both orders are correct behaviour.
            HoldOnly(CombatBindings.FireLaserKey);
            for (int i = 0; i < 10; i++) yield return null;

            Assert.AreEqual(0, fired,
                "the laser fired with the pointer uncaptured — the capture rule is not what "
                + "keeps the capturing click out of the weapon, so nothing does");
            Assert.AreEqual(durabilityBefore,
                target.Combat.Hull + target.Combat.Shield + target.Combat.Armor, 0.001f,
                "the uncaptured button dealt damage");

            // Now the capture click itself: the same button the laser is bound to,
            // through the same substituted source.
            held.Clear();
            yield return ClickTheGameView(CombatBindings.FireLaserKey);

            Assert.IsTrue(ShipController.PointerAimLive,
                "the click did not capture the pointer at all, so this test proved nothing about "
                + "the frame it captures on");

            // And the very next frame the same button is the weapon's again.
            HoldOnly(CombatBindings.FireLaserKey);
            yield return null;
            yield return null;

            Assert.Greater(fired, 0,
                "the laser never fires on the pointer button, so the capture rule has disarmed "
                + "the weapon instead of the capture frame");
            Assert.Less(target.Combat.Hull + target.Combat.Shield + target.Combat.Armor,
                durabilityBefore, "the laser reported firing and dealt no damage");
        }

        // =====================================================================
        // P1-TEST-042 — the real fire path, driven the way the player drives it
        // =====================================================================

        /// <summary>
        /// Held fire on a locked target spends energy, adds heat and takes hull —
        /// through <c>ShipCombat.Update</c>, the router and the binding, with nothing
        /// called by reflection.
        /// </summary>
        [UnityTest]
        public IEnumerator HeldFire_SpendsEnergy_AddsHeat_AndTakesHull()
        {
            yield return LoadSceneFresh();
            var boot = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController ship = boot.Context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            CombatEncounterDirector director = StarSystemRegistry.Streaming?.CombatEncounter;

            HardpointState laser = combat.Hardpoints[0];
            EnemyAgent target = director.SpawnEnemy(EnemyIds.FighterMk1,
                ship.transform.position + ship.transform.forward * (WeaponCatalog.LaserRange(laser.Tier) * 0.3f));
            Assert.IsTrue(combat.TrySelectNearestTarget(), "baseline: the target can be locked");

            // Capture the pointer first, exactly as a player must before they can aim.
            AssertFlightOwnsThePointer();
            yield return ClickTheGameView(CombatBindings.FireLaserKey);
            Assert.IsTrue(ShipController.PointerAimLive, "baseline: the pointer was captured");

            float energyBefore = combat.State.Energy;
            float heatBefore = laser.Heat;
            // Total durability, not hull. A Fighter Mk I carries 120 shield and 150
            // armour in front of its 220 hull, so a few frames of laser move the top
            // layer and leave the hull untouched — "dealt no damage" was measuring the
            // wrong number and would have stayed red however well the weapon worked.
            float durabilityBefore = target.Combat.Hull + target.Combat.Shield + target.Combat.Armor;
            var families = new List<WeaponFamily>();
            combat.WeaponFired += families.Add;

            HoldOnly(CombatBindings.FireLaserKey);
            for (int i = 0; i < 30; i++) yield return null;

            CollectionAssert.Contains(families, WeaponFamily.Laser,
                "thirty frames of held fire on a locked, in-range target produced no laser shot");
            Assert.Less(target.Combat.Hull + target.Combat.Shield + target.Combat.Armor,
                durabilityBefore, "held fire dealt no damage");
            Assert.Less(combat.State.Energy, energyBefore, "held fire spent no energy");
            Assert.Greater(laser.Heat, heatBefore, "held fire added no heat to the hardpoint");
        }

        // =====================================================================
        // P1-COMBAT-011 — one press is one refusal
        // =====================================================================

        /// <summary>
        /// Holding fire for a family the ship does not carry raises
        /// <c>FireDenied</c> <b>once</b>, not once per frame.
        /// <para>
        /// It used to fire every frame the control was held: sixty refusals a second
        /// into the audio bridge and, through it, sixty refusal cues. That is not a
        /// louder answer, it is an answer nobody can read — and it is the kind of
        /// event storm that hides a second, real refusal arriving underneath it.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator HoldingFireForAWeaponTheShipDoesNotCarry_RefusesOncePerPress()
        {
            yield return LoadSceneFresh();
            var boot = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController ship = boot.Context.Ship;
            var combat = ship.GetComponent<ShipCombat>();

            // Plasma is not on the ship at campaign start; the free hardpoint carries
            // the T1 laser. Asking for plasma is therefore a real, reachable refusal.
            foreach (HardpointState hardpoint in combat.Hardpoints)
                if (!hardpoint.IsEmpty)
                    Assert.AreNotEqual(WeaponFamily.Plasma, hardpoint.Weapon,
                        "baseline: this campaign already carries plasma, so holding its control "
                        + "is not a refusal and this test measures nothing");

            AssertFlightOwnsThePointer();
            yield return ClickTheGameView(CombatBindings.FireLaserKey);
            Assert.IsTrue(ShipController.PointerAimLive, "baseline: the pointer was captured");

            int denials = 0;
            combat.FireDenied += () => denials++;

            HoldOnly(CombatBindings.FirePlasmaKey);
            for (int i = 0; i < 40; i++) yield return null;

            Assert.AreEqual(1, denials,
                $"forty frames of one held press produced {denials} refusals; one press is one refusal. "
                + $"plasmaKey={CombatBindings.FirePlasmaKey}, "
                + $"catalogKey={NebulaInputActions.Key(NebulaInputActions.CombatFirePlasma)}, "
                + $"routerHeld={NebulaInputRouter.Held(NebulaInputActions.CombatFirePlasma)}, "
                + $"owner={NebulaInputRouter.OwnerOf(CombatBindings.FirePlasmaKey)?.Id ?? "none"}, "
                + $"pointerLive={ShipController.PointerAimLive}, "
                + $"contextLive={NebulaInputRouter.IsContextActive(InputContext.Combat)}");

            // Letting go and pressing again is a second press, and it is heard.
            HoldOnly();
            yield return null;
            HoldOnly(CombatBindings.FirePlasmaKey);
            for (int i = 0; i < 5; i++) yield return null;

            Assert.AreEqual(2, denials,
                "a second press was swallowed — the refusal latch never clears, so the player "
                + "is told once and never again");
        }
    }
}
