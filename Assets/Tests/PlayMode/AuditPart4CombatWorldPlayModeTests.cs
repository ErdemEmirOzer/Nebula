using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Celestial;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 4 / AJAN 2 — savaş, dünya ve seyir satırlarının regresyon
    /// nöbetçileri, gerçek bileşenler üzerinde.
    /// <para>
    /// Kapsanan satırlar: P1-COMBAT-005, P1-COMBAT-006, P1-COMBAT-008, P1-COMBAT-009,
    /// P1-ECO-014, P1-WORLD-004, P1-WORLD-007.
    /// </para>
    /// </summary>
    [Category("AuditPart4")]
    public class AuditPart4CombatWorldPlayModeTests
    {
        private const ulong Seed = 20260813UL;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private SystemManifest system;
        private SystemStreamingDirector savedStreaming;

        [SetUp]
        public void SetUp()
        {
            // A campaign left running by an earlier fixture is ENDED, not merely reset
            // around. Resetting the statics first and building on top of a session that
            // is still ticking is what made the reset below look sufficient and not be:
            // AuditPart4CombatInputTests boots a real session (SolarSystemBootstrap
            // .BeginSession) and spawns a Fighter Mk I a third of a laser range from the
            // player ship, then restores its save slot and resets the statics without
            // ending the session. The enemy keeps attacking, every hit calls
            // Encounter.RegisterThreatEvent, and CombatEncounterDirector.Update
            // (CombatEncounterDirector.cs:163) republishes IsOpen into
            // CombatInputContext.CombatActive on EVERY frame -- so the reset below is
            // undone on the first frame of this fixture and no per-frame counter-reset
            // could ever win that race. Ending the session removes the writer instead of
            // arguing with it, through the same entry the main menu's "leave campaign"
            // uses. Proven by the release gate's own journal: this fixture's foreign
            // presence leaf passed in three gates where its predecessor was another leaf
            // of this same fixture, and failed in the one where the leaf immediately
            // before it was AuditPart4CombatInputTests.TheCaptureClick_TakesThePointer_
            // AndDoesNotFireTheLaser.
            //
            // Guarded on SessionActive because EndSession also brings the main menu
            // back when there is nothing left running, and this fixture must not add
            // a screen to a scene that had none.
            SolarSystemBootstrap standing = Object.FindObjectOfType<SolarSystemBootstrap>();
            if (standing != null && standing.SessionActive) standing.EndSession();

            // Captured AFTER the session has ended, never before: ending it nulls
            // StarSystemRegistry.Streaming and destroys the director behind it, and
            // TearDown puts this value back -- so capturing first would restore a
            // reference to a destroyed streaming director for every later fixture.
            savedStreaming = StarSystemRegistry.Streaming;

            // The production reset contract, before anything is built. This fixture works
            // on bare components inside whatever scene the previous fixture left behind,
            // and two process-wide flags decide whether ShipExtraction.Update runs at all
            // (ShipExtraction.cs:142): InputGate.Suppressed, raised by every modal screen,
            // and CombatInputContext.WorldInteractionBlocked, raised by
            // ShipCombat.SetEncounterOpen(true) from any live encounter. With either one
            // stuck on, UpdateTarget never runs, TargetPlanet stays null, and
            // HasForeignEntityNearby returns false for a reason that has nothing to do
            // with the enemy it is being asked about -- reported as "gezegenin savunma
            // zarfındaki canlı bir düşman hiç sayılmıyor". Its sibling
            // AuditPart4CombatInputTests already calls this for the same reason.
            NebulaSessionLifetime.ResetProcessWideState();

            StarSystemRegistry.Streaming = null;
            ExtractionState.Reset((int)Seed);
            EnemyAgent.ClearRegistry();
            StarSystemRegistry.Initialize(Seed);

            SectorManifest sector = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
            system = sector.Systems.First(s => s.Planets.Count > 0);
            StarSystemRegistry.SetStartSystem(system.Sector, system.SystemId);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            ExtractionState.DestroyAllDrills();
            ExtractionState.DestroyAllStructures();
            EnemyAgent.ClearRegistry();
            StarSystemRegistry.Streaming = savedStreaming;
            StarSystemRegistry.Clear();
            ExtractionState.Reset((int)Seed);
            Nebula.Progression.ProgressionService.Reset();
            NebulaGameplayFeedback.Clear();
        }

        // =====================================================================
        // Harness
        // =====================================================================

        private GameObject Spawn(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            spawned.Add(go);
            return go;
        }

        private PlanetIdentity MaterializePlanet(int index = 0)
        {
            GameObject go = Spawn("Part4 Planet " + index, system.AbsoluteCenter);
            CelestialBody body = go.AddComponent<CelestialBody>();
            body.Configure(CelestialBodyType.Planet, 60f, 1f, false);
            PlanetIdentity planet = go.AddComponent<PlanetIdentity>();
            planet.Initialize(system.Planets[index].PlanetId, body, true);
            return planet;
        }

        private ShipCombat BuildShip(out ShipController ship)
        {
            GameObject go = Spawn("Part4 Ship", system.AbsoluteCenter);
            ship = go.AddComponent<ShipController>();
            go.AddComponent<ShipLogistics>();
            ShipCombat combat = go.AddComponent<ShipCombat>();

            // The same call a new campaign makes. AddComponent alone leaves State and
            // Hardpoints null — nothing constructs them in Awake — so every test that
            // touched combat.State got a NullReferenceException whose stack pointed at
            // the test rather than at the missing initialisation. Bringing the component
            // to the state production brings it to is the fixture's job, and doing it
            // through the campaign's own entry point means it cannot drift from one.
            combat.InitializeNewCampaign();
            return combat;
        }

        // =====================================================================
        // P1-COMBAT-006 — kaskad hasar her katmanı raporlar
        // =====================================================================

        /// <summary>
        /// Kalkanı, zırhı ve gövdeyi tek vuruşta delen bir isabet, HUD'a yalnızca
        /// "Kalkan" diye ulaşıyordu: olay "vuruştan önce sıfırdan büyük ilk katmanı"
        /// seçiyor ve kaybı yalnız ondan hesaplıyordu. Zırh ve gövde göstergeleri az
        /// önce yedikleri hasara hiç tepki vermiyordu.
        /// </summary>
        [UnityTest]
        public IEnumerator ASingleBlowThroughAllThreeLayersReportsAllThree()
        {
            ShipCombat combat = BuildShip(out _);
            yield return null;

            var layers = new List<ShipDamageLayer>();
            combat.DamageApplied += (layer, _, _2) => layers.Add(layer);

            Durability durability = combat.State.Durability;
            float everything = durability.Shield + durability.Armor + durability.Hull;
            combat.ApplyDamage(everything, DamageProfile.Neutral);

            CollectionAssert.Contains(layers, ShipDamageLayer.Shield);
            CollectionAssert.Contains(layers, ShipDamageLayer.Armor, "gövdeye kadar delen vuruş zırhı hiç raporlamadı");
            CollectionAssert.Contains(layers, ShipDamageLayer.Hull, "gövde kaybı hiç raporlanmadı");
            CollectionAssert.AreEqual(
                new[] { ShipDamageLayer.Shield, ShipDamageLayer.Armor, ShipDamageLayer.Hull }, layers,
                "katmanlar kaskadın gerçek sırasında raporlanmadı");
        }

        /// <summary>Yalnız kalkanı çizen bir vuruş hâlâ tek olay üretir — düzeltme gürültü eklemedi.</summary>
        [UnityTest]
        public IEnumerator AShieldOnlyGrazeStillReportsExactlyOneLayer()
        {
            ShipCombat combat = BuildShip(out _);
            yield return null;

            var layers = new List<ShipDamageLayer>();
            combat.DamageApplied += (layer, _, _2) => layers.Add(layer);

            combat.ApplyDamage(combat.State.Durability.Shield * 0.25f, DamageProfile.Neutral);

            CollectionAssert.AreEqual(new[] { ShipDamageLayer.Shield }, layers);
        }

        /// <summary>Sıfır hasar hiçbir katman raporlamaz — mevcut "0 hasarda geri bildirim yok" kuralı korunuyor.</summary>
        [UnityTest]
        public IEnumerator ZeroDamageReportsNothing()
        {
            ShipCombat combat = BuildShip(out _);
            yield return null;

            int events = 0;
            combat.DamageApplied += (_, _2, _3) => events++;
            combat.ApplyDamage(0f, DamageProfile.Neutral);

            Assert.AreEqual(0, events);
        }

        // =====================================================================
        // P1-COMBAT-009 — ölü alan
        // =====================================================================

        /// <summary>
        /// <c>boostSpeedMultiplierApplied</c> her karede yazılıyor, hiç okunmuyordu:
        /// <c>ShipController.CombatBoostSpeedMultiplier</c>'ın kopyasıydı. Kopya bir
        /// otorite, ilk kaçırılan atamada ayrışır.
        /// </summary>
        [Test]
        public void ShipCombatKeepsNoPrivateCopyOfTheBoostMultiplier()
        {
            Assert.IsNull(typeof(ShipCombat).GetField("boostSpeedMultiplierApplied",
                    BindingFlags.NonPublic | BindingFlags.Instance),
                "gemi hızının ikinci, okunmayan bir kopyası geri geldi");
        }

        // =====================================================================
        // P1-COMBAT-008 — savunma koordinasyonu sebep üretir ve menzili vardır
        // =====================================================================

        /// <summary>
        /// "En yakın Komuta gezegeni" seçiminin üst sınırı yoktu: tuşa basmak, oyuncunun
        /// göremediği bir gezegenin savunmasını yeniden hedefleyebiliyordu. Sınır
        /// gezegenin kendi savunma zarfıdır ve Sensör Dizisi kapısıyla aynı mesafedir.
        /// </summary>
        [Test]
        public void DefenseCoordinationHasAFiniteRange_SharedWithTheForeignPresenceGate()
        {
            Assert.Greater(PlanetaryDefenseCoordinator.CoordinationRange, 0f);
            Assert.AreEqual(PlanetaryDefenseCatalog.PlanetDefenseEnvelopeRange,
                PlanetaryDefenseCoordinator.CoordinationRange, 0.0001f,
                "koordinasyon menzili ile gezegen savunma zarfı iki ayrı sayı oldu");
            Assert.AreEqual(PlanetaryDefenseCatalog.PlasmaT2.Range,
                PlanetaryDefenseCatalog.PlanetDefenseEnvelopeRange, 0.0001f,
                "zarf artık kataloğun en uzun menzilinden türemiyor");
        }

        /// <summary>Ret sebepleri tipli ve Türkçedir; hiçbiri sessiz değildir.</summary>
        [Test]
        public void EveryDefenseCoordinationRefusalHasATurkishSentence()
        {
            foreach (DefenseCoordinationRejectReason reason in
                     System.Enum.GetValues(typeof(DefenseCoordinationRejectReason)))
            {
                string text = NebulaGameplayFeedback.DefenseCoordinationText(reason);
                if (reason == DefenseCoordinationRejectReason.None) { Assert.IsNull(text); continue; }
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{reason}: sessiz ret");
                Assert.AreNotEqual(reason.ToString(), text, $"{reason}: enum adı gösterildi");
            }
        }

        /// <summary>Yeni kurulan bir gemi hiçbir ret taşımaz — sebep alanı geçmişten kirlenmiş başlamaz.</summary>
        [UnityTest]
        public IEnumerator ANewShipCarriesNoDefenseCoordinationRefusal()
        {
            ShipCombat combat = BuildShip(out _);
            yield return null;
            Assert.AreEqual(DefenseCoordinationRejectReason.None, combat.LastDefenseCoordinationReject);
        }

        // =====================================================================
        // P1-COMBAT-005 — Deep Space Cruise'un kendi tuşu
        // =====================================================================

        /// <summary>
        /// H'nin etiketi Part 3'te düzeltildi; geriye seyrin hiçbir kontrolünün
        /// olmaması kalmıştı. Satır artık var, Yön Sabitle'den ayrı, kendi tuşuyla ve
        /// çakışmasız.
        /// </summary>
        [Test]
        public void DeepSpaceCruiseHasItsOwnRow_SeparateFromHeadingLock()
        {
            InputActionDefinition cruise = NebulaInputActions.All
                .FirstOrDefault(a => a.Id == NebulaInputActions.FlightCruiseToggle);
            Assert.IsNotNull(cruise, "Deep Space Cruise'un hâlâ hiçbir satırı yok");
            Assert.AreEqual(InputContext.Flight, cruise.Context);
            Assert.IsTrue(cruise.Rebindable, "seyir tuşu yeniden atanamıyor");

            InputActionDefinition headingLock = NebulaInputActions.All
                .FirstOrDefault(a => a.Id == NebulaInputActions.FlightHeadingLock);
            Assert.IsNotNull(headingLock);
            Assert.AreNotEqual(headingLock.Key, cruise.Key, "seyir ve yön sabitleme aynı tuşu paylaşıyor");
            StringAssert.DoesNotContain("Seyir", headingLock.LabelTr,
                "yön sabitleme satırı yeniden seyir diye adlandırıldı");

            Assert.IsEmpty(NebulaInputActions.Collisions().Where(c => c.Key == cruise.Key).ToList(),
                "seyir tuşu başka bir eylemle çakışıyor");
        }

        /// <summary>
        /// Kontrol gerçek: varsayılan açıktır (mevcut davranış hiç değişmez), kapatmak
        /// seyri dünya koşullarından bağımsız olarak durdurur, açmak geri verir.
        /// </summary>
        [Test]
        public void DisarmingCruiseStopsIt_AndArmedIsTheDefault()
        {
            var conditions = new CruiseEntryConditions
            {
                PlayerArmed = true,
                ThrottleHigh = true, NotStabilized = true, BuildModeOff = true,
                NoPhysicalInteractionActive = true, NotNearHazard = true, InputFree = true,
                NoSharpTurn = true, NotInStellarRisk = true, NotDamaged = true
            };
            Assert.IsTrue(conditions.AllMet);
            Assert.IsTrue(conditions.CanEngage);

            conditions.PlayerArmed = false;
            Assert.IsFalse(conditions.AllMet, "seyir kapatıldığı hâlde sürmeye devam ediyor");
            Assert.IsFalse(conditions.CanEngage);
        }

        /// <summary>Kapalıyken seyir hızlanmaz; açıldığında yeniden hızlanmaya başlar.</summary>
        [Test]
        public void ADisarmedCruiseNeverRamps_AndReArmingLetsItStartAgain()
        {
            var machine = new CruiseStateMachine();
            machine.SetEngineTier(1);

            var disarmed = new CruiseEntryConditions
            {
                PlayerArmed = false,
                ThrottleHigh = true, NotStabilized = true, BuildModeOff = true,
                NoPhysicalInteractionActive = true, NotNearHazard = true, InputFree = true,
                NoSharpTurn = true, NotInStellarRisk = true, NotDamaged = true
            };
            for (int i = 0; i < 600; i++) machine.Tick(1f / 60f, disarmed, 180f, 1800f);
            Assert.AreEqual(FlightMode.LocalFlight, machine.Mode, "kapalı seyir kendiliğinden devreye girdi");
            Assert.AreEqual(1f, machine.SpeedMultiplier, 0.0001f);

            CruiseEntryConditions armed = disarmed;
            armed.PlayerArmed = true;
            machine.Tick(1f / 60f, armed, 180f, 1800f);
            Assert.AreEqual(FlightMode.CruiseRamp, machine.Mode, "açılan seyir hızlanmaya başlamadı");
        }

        /// <summary>Gerçek bileşen üzerinde: varsayılan açık, geçiş tersine çevirir.</summary>
        [UnityTest]
        public IEnumerator TheShipStartsWithCruiseArmed_AndTheToggleFlipsIt()
        {
            GameObject go = Spawn("Part4 Cruise Ship", system.AbsoluteCenter);
            go.AddComponent<ShipController>();
            ShipCruise cruise = go.AddComponent<ShipCruise>();
            yield return null;

            Assert.IsTrue(cruise.CruiseArmed, "yeni gemi seyir kapalı başlıyor — mevcut davranış değişti");
            Assert.IsFalse(cruise.ToggleCruiseArmed());
            Assert.IsFalse(cruise.CruiseArmed);
            Assert.IsTrue(cruise.ToggleCruiseArmed());
            Assert.IsTrue(cruise.CruiseArmed);
        }

        // =====================================================================
        // P1-ECO-014 — 'yabancı varlık yakında' gerçekten yakınlık ve varlık ölçer
        // =====================================================================

        /// <summary>
        /// Kapı, sistemde herhangi bir Anomali POI'si varsa açılıyordu: mesafe yok,
        /// hedef gezegenle ilişki yok, düşman hiç sayılmıyordu. Artık canlı bir Entity
        /// sayılır ve gezegenin savunma zarfının içinde olmak zorundadır.
        /// </summary>
        [UnityTest]
        public IEnumerator TheForeignPresenceGateCountsALiveEntity_AndOnlyNearTheTargetPlanet()
        {
            PlanetIdentity planet = MaterializePlanet();
            GameObject shipObject = Spawn("Part4 Presence Ship", planet.transform.position);
            shipObject.AddComponent<ShipController>();
            shipObject.AddComponent<ShipLogistics>();
            ShipExtraction extraction = shipObject.AddComponent<ShipExtraction>();
            yield return null;

            Assert.IsFalse(extraction.HasForeignEntityNearby(),
                "boş bir gökyüzünde yabancı varlık bulundu");

            float reach = planet.Body.Radius + PlanetaryDefenseCatalog.PlanetDefenseEnvelopeRange;

            // Through EnemyAgent.Create, the real factory. AddComponent alone produces
            // an agent with no id, no Def and no Durability that is never entered into
            // EnemyAgent.All — and the gate walks that registry, so a bare component is
            // invisible to it however close it is parked. The test would have gone on
            // reporting "a live enemy inside the envelope is never counted" against a
            // gate that counts live enemies correctly.
            EnemyAgent farEnemy = EnemyAgent.Create("part4-presence-enemy",
                EnemyCatalog.Get(EnemyIds.FighterMk1),
                planet.transform.position + Vector3.right * (reach * 4f),
                playerShip: null, director: null, parent: null);
            yield return null;
            Assert.IsFalse(extraction.HasForeignEntityNearby(),
                "gezegenden çok uzaktaki bir düşman 'yakında' sayıldı");

            farEnemy.transform.position = planet.transform.position + Vector3.right * (reach * 0.5f);
            yield return null;
            Assert.IsTrue(extraction.HasForeignEntityNearby(),
                "gezegenin savunma zarfındaki canlı bir düşman hiç sayılmıyor");
        }

        // =====================================================================
        // P1-WORLD-007 — hırsız hedeflediği sondajı soyar
        // =====================================================================

        /// <summary>
        /// Hedef, gezegenin değil sondajın kimliğidir: seçim "en dolu sondaj"ı
        /// bulurken çözümleme "o gezegendeki ilk sondaj"ı buluyordu — iki farklı soru.
        /// Kimlik artık projenin her yerde kullandığı <c>worldId/nodeId</c> çifti, ve
        /// seçilen sondaj ile soyulan sondaj aynı nesnedir.
        /// </summary>
        [UnityTest]
        public IEnumerator TheLooterStealsFromTheDrillItSelected()
        {
            if (system.Planets.Count < 2) Assert.Ignore("bu tohumda iki gezegenli sistem yok");

            DrillStructure emptier = RestoreActiveDrill(MaterializePlanet(0), 0.10f);
            DrillStructure fuller = RestoreActiveDrill(MaterializePlanet(1), 0.90f);
            yield return null;

            Assert.AreNotEqual(LooterCargoService.DrillTargetId(emptier), LooterCargoService.DrillTargetId(fuller),
                "iki sondaj aynı kimliği taşıyor — hedefleme ayırt edemez");
            StringAssert.Contains(fuller.NodeId, LooterCargoService.DrillTargetId(fuller),
                "hedef kimliği sondajın düğümünü hiç taşımıyor — gezegen adı tek başına hedef");

            LootTarget? target = LooterCargoService.SelectTarget(system.SystemId, null);
            Assert.IsTrue(target.HasValue, "dolu bir sondaj varken hiç hedef seçilmedi");
            Assert.AreEqual(LootSourceKind.Drill, target.Value.Kind);
            Assert.AreSame(fuller, LooterCargoService.FindDrillByTargetId(target.Value.TargetId),
                "seçilen sondaj ile hedefin çözüldüğü sondaj farklı");

            var looterCargo = new LooterCargoState(1000f);
            float before = fuller.Runtime.StoredAmount;
            float otherBefore = emptier.Runtime.StoredAmount;
            float stolen = LooterCargoService.StealFromDrill(looterCargo,
                LooterCargoService.FindDrillByTargetId(target.Value.TargetId), 50f, 1f);

            Assert.Greater(stolen, 0f, "hedeflenen sondajdan hiçbir şey çalınmadı");
            Assert.AreEqual(before - stolen, fuller.Runtime.StoredAmount, 0.001f,
                "hedeflenen sondajın stoku çalınan kadar azalmadı");
            Assert.AreEqual(otherBefore, emptier.Runtime.StoredAmount, 0.001f,
                "hırsız hedeflemediği sondajdan çaldı");
        }

        /// <summary>Sondajı yok olmuş bir hedef kimliği hiçbir sondaja çözülmez — hırsız rastgele bir yedeğe düşmez.</summary>
        [UnityTest]
        public IEnumerator AStaleDrillTargetResolvesToNothing()
        {
            DrillStructure drill = RestoreActiveDrill(MaterializePlanet(0), 0.5f);
            yield return null;

            string targetId = LooterCargoService.DrillTargetId(drill);
            Assert.AreSame(drill, LooterCargoService.FindDrillByTargetId(targetId));

            ExtractionState.DestroyAllDrills();
            yield return null;
            Assert.IsNull(LooterCargoService.FindDrillByTargetId(targetId));
        }

        /// <summary>Kaydın ürettiği yoldan aktif, belirli oranda dolu bir sondaj — üretimde test-özel hiçbir kapı açmadan.</summary>
        private DrillStructure RestoreActiveDrill(PlanetIdentity planet, float fillFraction)
        {
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            float capacity = ExtractionCatalog.StorageCapacity(profile.Rarity, 1);
            var runtime = new DrillRuntime(profile, DrillLifecycle.Active,
                capsuleRemaining: 0f, buildRemaining: 0f, stored: capacity * fillFraction, accumulator: 0f);
            DrillStructure drill = DrillStructure.Restore(planet, profile, runtime, DrillStructure.BuildSource.Kit, 0);
            spawned.Add(drill.gameObject);
            return drill;
        }

        // =====================================================================
        // P1-WORLD-004 — POI Locate'in mesafe koşulu dünyada da geçerli
        // =====================================================================

        /// <summary>
        /// Locate'in koşulu sabit <c>true</c>'ydu: sistemde olmak yetiyordu. Gemi
        /// menzil dışındayken sayaç durmalı ve sebebini söylemelidir.
        /// </summary>
        [Test]
        public void PoiLocateStallsOutOfScannerRange_AndSaysWhy()
        {
            var state = new PoiState("part4-poi");
            float required = PoiCatalog.ShipLocateSeconds(0);

            Assert.IsFalse(state.TickLocate(required * 2f, conditionMet: false, required));
            Assert.AreEqual(PoiKnowledgeLevel.Signal, state.Level);
            Assert.AreEqual(PoiPauseReason.OutOfRange, state.PauseReason);

            Assert.IsTrue(state.TickLocate(required, conditionMet: true, required));
            Assert.AreEqual(PoiKnowledgeLevel.Located, state.Level);
            Assert.AreEqual(PoiPauseReason.None, state.PauseReason);
        }
    }
}
