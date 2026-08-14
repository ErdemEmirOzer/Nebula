using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Poi;
using Nebula.Progression;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 2 / AJAN 2 — the PlayMode fixture for session,
    /// static-state, event and time lifecycle. Everything that needs a real
    /// frame boundary lives here, because a frame boundary is exactly what the
    /// findings are about: Unity defers <c>Destroy</c> to the end of the frame,
    /// so an outgoing session's <c>OnDestroy</c> lands on the session that has
    /// already replaced it.
    /// <para>
    /// Every leaf here is <c>[Category("ReleaseRegression")]</c>: short,
    /// deterministic, no loop over a cycle budget, and each one asserts a
    /// player-visible or lifecycle behaviour rather than measuring an accumulation.
    /// The measured cost of the nine is 37.7s in total, longest leaf 7.8s.
    /// </para>
    /// <para>
    /// The endurance and bisection work that used to share this file now lives in
    /// <see cref="AuditPart2LifecycleSoakTests"/> (<c>Soak</c>) and
    /// <see cref="AuditPart2LifecycleDiagnosticTests"/> (<c>Diagnostic</c>), with
    /// their cycle counts intact — see <see cref="LifecycleAuditFixture"/> for why
    /// the split had to happen and <see cref="LifecycleOwnershipRegressionTests"/>
    /// for the short regressions that keep the release lane covering what they
    /// measure.
    /// </para>
    /// <para>Expected to fail on HEAD=4d16782 in: P2-LIFE-001, P2-COMBAT-001.</para>
    /// </summary>
    public class AuditPart2LifecyclePlayModeTests : LifecycleAuditFixture
    {
        // ================================================================
        // P2-LIFE-001 — deferred Destroy vs. key-addressed static registries
        // ================================================================

        /// <summary>
        /// <c>PoiObject.OnDestroy</c> (PoiObject.cs:75-78) removes its own id from
        /// the static registry and from <c>PoiLiveState</c> with no check that the
        /// entry under that id is still its own. Unity runs that OnDestroy at the
        /// END of the frame — after the replacement POI, which has the same
        /// deterministic id (<c>WorldIdCodec.PoiId(systemId, index)</c>), has
        /// already registered itself. The outgoing object therefore deletes the
        /// incoming one.
        /// <para>
        /// This is the exact shape of every same-frame rebuild in production:
        /// <c>ExtractionDirector.LoadNow</c> (:487) and
        /// <c>SystemStreamingDirector.ResetAndBootFull</c> (:131) both destroy the
        /// materialized system and re-materialize the SAME system inside one call.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator PoiRegistration_SurvivesASameFrameRebuildOfTheSamePoiId()
        {
            yield return LoadSceneFresh();

            PoiLiveState.Reset();
            PoiObject.ClearRegistry();

            var systemRoot = new GameObject("audit-system-root");
            var star = new GameObject("audit-star");
            star.transform.SetParent(systemRoot.transform, false);

            const string poiId = "audit/poi.0";
            PoiManifest manifest = new PoiManifest(poiId, "audit-system", 0, PoiType.Anomaly, PoiRisk.Safe,
                new Vector3(100f, 0f, 0f), WreckSize.Small, 1UL, 2UL);

            PoiObject outgoing = PoiObject.Create(manifest, star.transform, new PoiState(poiId), null, null, null, null);
            yield return null;
            Assert.That(PoiObject.Find(poiId), Is.SameAs(outgoing), "baseline: the first POI registered");

            // --- exactly what a load / ResetAndBootFull does, in ONE frame ---
            UnityEngine.Object.Destroy(outgoing.gameObject);          // deferred to end of frame
            PoiObject incoming = PoiObject.Create(manifest, star.transform, new PoiState(poiId), null, null, null, null);

            Assert.That(PoiObject.Find(poiId), Is.SameAs(incoming),
                "baseline: within the same frame the rebuilt POI owns the id");
            Assert.That(PoiLiveState.Find(poiId), Is.Not.Null, "baseline: and owns the live-state entry");

            yield return null;   // Unity now runs the outgoing object's OnDestroy

            string diagnosis =
                " The outgoing PoiObject's deferred OnDestroy removed the id that already belonged to its replacement. "
                + "PoiObject.cs:75-78 removes by key; EnemyAgent.cs:160-163 solves the identical problem with "
                + "`if (byId.TryGetValue(Id, out var current) && current == this)`, and "
                + "ExtractionState.cs:77-85 does the same for planets. POI is the one registry without the guard.";

            bool registryKept = ReferenceEquals(PoiObject.Find(poiId), incoming);
            bool liveStateKept = PoiLiveState.Find(poiId) != null;

            Assert.That(registryKept, Is.True,
                "the live POI lost its PoiObject registry entry one frame after a same-frame rebuild "
                + $"(PoiLiveState entry kept = {liveStateKept})." + diagnosis);
            Assert.That(liveStateKept, Is.True,
                "the live POI lost its PoiLiveState entry one frame after a same-frame rebuild. "
                + "PoiLiveState is what a save reads for this system's POI knowledge/progress." + diagnosis);

            UnityEngine.Object.Destroy(systemRoot);
            PoiLiveState.Reset();
            PoiObject.ClearRegistry();
        }

        /// <summary>
        /// The production-path half of P2-LIFE-001. The start system intentionally
        /// has no POI, so this selects the first real foreign manifest and records
        /// its first physical visit. Production guarantees that visit a Safe Wreck;
        /// no Assume/Inconclusive escape remains.
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator ReloadingTheCurrentSystem_KeepsItsPoiRegistrationsOneFrameLater()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            SystemManifest manifest = FirstForeignManifest(context);
            Assert.That(PoiManifestRepository.Ledger.RecordVisit(manifest.SystemId), Is.True,
                "baseline: the selected foreign system is its first physical visit");
            PoiManifestRepository.Invalidate(manifest.SystemId);
            List<PoiManifest> generated = PoiManifestRepository.Resolve(ExtractionState.UniverseSeed, manifest);
            Assert.That(generated.Count, Is.GreaterThan(0),
                "baseline: the production POI generator must return the first-visit guarantee before Create is exercised");

            PoiLiveState.Reset();
            PoiObject.ClearRegistry();
            PoiManifest poi = generated[0];
            var root = new GameObject("audit-production-poi-root");
            var star = new GameObject("audit-production-poi-star");
            star.transform.SetParent(root.transform, false);

            PoiObject outgoing = PoiObject.Create(poi, star.transform, new PoiState(poi.PoiId), null, null, null, null);
            yield return null;
            Assert.That(PoiObject.Find(poi.PoiId), Is.SameAs(outgoing),
                "baseline: the real generated manifest registered through production PoiObject.Create");

            UnityEngine.Object.Destroy(outgoing.gameObject);
            PoiObject incoming = PoiObject.Create(poi, star.transform, new PoiState(poi.PoiId), null, null, null, null);
            Assert.That(PoiObject.Find(poi.PoiId), Is.SameAs(incoming),
                "baseline: the replacement owns the deterministic production id before deferred OnDestroy");

            yield return null;

            Assert.That(PoiObject.Find(poi.PoiId), Is.SameAs(incoming),
                "the outgoing production PoiObject deleted the generated replacement at the frame boundary; "
                + "PoiObject.OnDestroy removes by deterministic id without checking that the registry still points to this instance");
            Assert.That(PoiLiveState.Find(poi.PoiId), Is.Not.Null,
                "the outgoing production PoiObject also deleted the replacement's save-authority PoiLiveState entry");

            UnityEngine.Object.Destroy(root);
            PoiLiveState.Reset();
            PoiObject.ClearRegistry();
            adapter.EndSession();
        }

        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator SignalPoiWorldMarkers_DoNotRevealTypeOrRiskBeforeIdentification()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            Transform parent = context.Streaming.CombatEncounter.transform;
            var anomalyManifest = new PoiManifest("audit/gate/anomaly", context.StartSystemId, 0,
                PoiType.Anomaly, PoiRisk.Unstable, Vector3.right * 1200f, WreckSize.Small, 1UL, 2UL);
            var wreckManifest = new PoiManifest("audit/gate/wreck", context.StartSystemId, 1,
                PoiType.Wreck, PoiRisk.Safe, Vector3.left * 1200f, WreckSize.Small, 3UL, 4UL);
            PoiObject anomaly = PoiObject.Create(anomalyManifest, parent, new PoiState(anomalyManifest.PoiId),
                context.Ship, context.Hangar, context.NavigationDirector?.Knowledge, context.NavigationDirector?.FrontierSignal);
            PoiObject wreck = PoiObject.Create(wreckManifest, parent, new PoiState(wreckManifest.PoiId),
                context.Ship, context.Hangar, context.NavigationDirector?.Knowledge, context.NavigationDirector?.FrontierSignal);

            yield return null;
            MeshRenderer anomalyMarker = anomaly.transform.Find("Marker")?.GetComponent<MeshRenderer>();
            MeshRenderer wreckMarker = wreck.transform.Find("Marker")?.GetComponent<MeshRenderer>();
            Assert.That(anomalyMarker, Is.Not.Null);
            Assert.That(wreckMarker, Is.Not.Null);
            Assert.That(anomalyMarker.GetComponent<MeshFilter>().sharedMesh,
                Is.SameAs(wreckMarker.GetComponent<MeshFilter>().sharedMesh),
                "Signal markers of different POI types must use one generic geometry");
            Assert.That(anomalyMarker.sharedMaterial.color, Is.EqualTo(wreckMarker.sharedMaterial.color),
                "Signal markers of different risk levels must use one generic colour");

            UnityEngine.Object.Destroy(anomaly.gameObject);
            UnityEngine.Object.Destroy(wreck.gameObject);
            adapter.EndSession();
        }

        // ================================================================
        // P2-COMBAT-001 — Emergency Shutdown behind an open screen
        // ================================================================

        /// <summary>
        /// One measurement, two timers, the same thirty frames, the same open
        /// screen. The 8-second Emergency Shutdown countdown is the only route back
        /// from hull 0, and it used to stop dead behind any screen because
        /// <c>ShipCombat.Update</c> returned on <c>InputGate.Suppressed</c> before it
        /// ever reached <c>UpdateShutdown</c>. <c>ExtractionDirector.cs:106</c> ticks
        /// the autosave timer OUTSIDE its own gate (:100) and always did, which is the
        /// control this measurement is read against: both belong to the same "the
        /// world keeps running behind a screen" contract (3fa9dda), and only one of
        /// them honoured it.
        /// <para>
        /// The production fix is the ORDER of two lines in
        /// <c>ShipCombat.Update</c>: <c>if (State.EmergencyShutdown) { UpdateShutdown(); return; }</c>
        /// at ShipCombat.cs:190 now runs BEFORE <c>if (InputGate.Suppressed) return;</c>
        /// at :191. Swap those two lines back and this test goes red again — the
        /// countdown records a 0.000s advance while the autosave control advances a
        /// full thirty frames.
        /// </para>
        /// <para>
        /// Part 1 read NUnit's "Expected: less than 7.9772501f" line as a before
        /// value and reported an unexplained 1e-4 INCREASE. That number is
        /// <c>remainingBefore - 0.0001f</c>, the assertion's own tolerance term:
        /// before and after were the identical float. This test therefore asserts
        /// the delta directly, so the measurement can no longer be misread.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator EmergencyShutdownCountdown_RunsBehindAScreenLikeEveryOtherCampaignTimer()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            ShipController ship = context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            ExtractionDirector extraction = context.ExtractionDirector;

            FieldInfo autosaveField = typeof(ExtractionDirector).GetField("autosaveTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(autosaveField, Is.Not.Null,
                "the control timer (ExtractionDirector.autosaveTimer) could not be read — the comparison is the point of this test");

            Durability durability = combat.State.Durability;
            combat.ApplyDamage(durability.Shield + durability.Armor + durability.Hull + 50f);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(combat.State.EmergencyShutdown, Is.True, "baseline: hull 0 puts the ship into Emergency Shutdown");
            Assert.That(combat.Shutdown.Active, Is.True, "baseline: the countdown started");

            float shutdownBefore = combat.Shutdown.RemainingSeconds;
            float autosaveBefore = (float)autosaveField.GetValue(extraction);
            float unityTimeBefore = Time.time;

            InputGate.Suppressed = true;                       // exactly what any management screen raises
            for (int i = 0; i < 30; i++) yield return null;
            float shutdownAfter = combat.Shutdown.RemainingSeconds;
            float autosaveAfter = (float)autosaveField.GetValue(extraction);
            float unityTimeAfter = Time.time;
            InputGate.Suppressed = false;

            float shutdownAdvanced = shutdownBefore - shutdownAfter;
            float autosaveAdvanced = autosaveAfter - autosaveBefore;
            float wallClock = unityTimeAfter - unityTimeBefore;

            Assert.That(autosaveAdvanced, Is.GreaterThan(wallClock * 0.5f),
                $"the control timer did not advance either ({autosaveAdvanced:0.#####}s over {wallClock:0.#####}s of "
                + "game time) — the harness, not the gate, would be at fault. ExtractionDirector.cs:106 is outside the gate at :100.");

            Assert.That(shutdownAdvanced, Is.GreaterThan(wallClock * 0.5f),
                $"over 30 frames ({wallClock:0.#####}s of game time) behind an open screen, the Emergency Shutdown "
                + $"countdown advanced {shutdownAdvanced:0.#####}s ({shutdownBefore:0.#######} -> {shutdownAfter:0.#######}) "
                + $"while the autosave timer in the same frames advanced {autosaveAdvanced:0.#####}s. "
                + "ShipCombat.cs:191 returns on InputGate.Suppressed, and it must stay BELOW the "
                + "UpdateShutdown dispatch at :190, or the player's only route out of hull 0 is frozen for as "
                + "long as a screen stays up — while ShipCombat's OWN FixedUpdate (durability regen, hardpoint "
                + "heat, weapon production, repair) keeps ticking through the same screen.");

            adapter.EndSession();
        }

        /// <summary>
        /// The other half of the same component, stated as its own measurement:
        /// <c>ShipCombat.FixedUpdate</c> is NOT behind the gate, so repair and
        /// production keep running behind a screen. Part 3 has to pick one policy
        /// for the whole component; this is the evidence for what it is picking
        /// between.
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator ShipCombatFixedUpdate_KeepsRunningBehindAScreen_UnlikeItsOwnUpdate()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            var combat = context.Ship.GetComponent<ShipCombat>();
            Assert.That(combat.State.TryConsumeEnergy(Mathf.Min(30f, combat.State.Energy)), Is.True,
                "baseline: energy could be spent, so regen has something to restore");
            yield return new WaitForFixedUpdate();

            float energyBefore = combat.State.Energy;
            InputGate.Suppressed = true;
            for (int i = 0; i < 60; i++) yield return null;
            float energyAfter = combat.State.Energy;
            InputGate.Suppressed = false;

            Assert.That(energyAfter, Is.GreaterThan(energyBefore + 0.0001f),
                $"energy regen ({energyBefore:0.###} -> {energyAfter:0.###}) is driven by ShipCombat.FixedUpdate's "
                + "State.Tick, which has no InputGate check at all. It running while the Emergency Shutdown countdown "
                + "in the same component's Update does not is the inconsistency P2-COMBAT-001 is about.");

            adapter.EndSession();
        }

        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator CruiseStateMachine_ContinuesOnTheWorldClockBehindAnInputGate()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            ShipCruise cruise = context.Ship.GetComponent<ShipCruise>();
            cruise.ApplyEngineTier(1);
            cruise.RestoreFlightState(Nebula.Navigation.FlightMode.CruiseDeceleration,
                elapsedSeconds: 0f, speedMultiplier: 2f, headingEngaged: true,
                headingVector: context.Ship.transform.forward);
            float before = cruise.DecelElapsed;
            InputGate.Suppressed = true;
            for (int i = 0; i < 10; i++) yield return null;
            InputGate.Suppressed = false;

            Assert.That(cruise.DecelElapsed, Is.GreaterThan(before),
                "InputGate suppresses controls; it must not freeze the cruise state machine's world-clock tick");
            Assert.That(context.Ship.RotationLocked, Is.True,
                "RotationLocked remains a published state while its input reader is gated");
            adapter.EndSession();
        }

        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator RejectedEngineerShipRepair_FailsWithoutCompletingOrAwardingXp()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            ShipHangar hangar = context.Hangar;
            hangar.ApplyHangarTier(1);
            VehicleAgent engineer = hangar.RestoreVehicle("audit-engineer-reject", VehicleCatalog.EngineerMk1);
            engineer.RestorePhysical(context.StartSystemId, context.Ship.transform.position,
                Quaternion.identity, Vector3.zero, VehiclePhase.InHangar, MissionKind.None,
                null, 0f, false, null, 0f);
            var task = new EngineerMissionState("audit-engineer-reject-mission", engineer.VehicleId,
                context.StartSystemId, EngineerTargetKind.Ship, "ship", EngineerWorkKind.Repair,
                EngineerSourceKind.ShipCargo, null, true, 0, 0f);
            engineer.AssignEngineerTask(task);
            task.ArriveAtTarget();
            task.TickSafety(EngineerMissionState.RequiredSafeSeconds, currentlySafe: true);
            Assert.That(task.Phase, Is.EqualTo(EngineerPhase.Working));

            // The undamaged production ship rejects TryStartRepair as NothingToRepair.
            yield return null;

            Assert.That(task.Phase, Is.EqualTo(EngineerPhase.Cancelled),
                "a rejected TryStartRepair must fail the mission instead of reporting zero-HP completion");
            Assert.That(task.XpAwarded, Is.False);
            Assert.That(engineer.gameObject.activeSelf, Is.False,
                "the failed mission is stopped and cannot drift into the dock/XP path");
            adapter.EndSession();
        }

        // ================================================================
        // Lifecycle behaviour that needs no repetition to state
        // ================================================================

        /// <summary>
        /// P2-LIFE-004 on the unavoidable start-system production path: BootFull
        /// creates every planet's CelestialAnimationPresenter before the animation
        /// director exists, and AnimationInstaller later clears their common queue.
        /// The presenters remain alive but never join ParticipantCount.
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator SessionBoot_AdoptsTheCelestialPresentersCreatedByProductionStreaming()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            CelestialAnimationPresenter[] presenters = UnityEngine.Object.FindObjectsOfType<CelestialAnimationPresenter>(true);
            Assert.That(presenters.Length, Is.GreaterThan(0),
                "baseline: start-system streaming created at least one celestial presenter");

            NebulaAnimationDirector animation = context.AnimationDirector;
            Assert.That(animation, Is.Not.Null);
            yield return null; // director LateUpdate drains any legitimate adoption
            var unregistered = new List<string>();
            foreach (CelestialAnimationPresenter presenter in presenters)
                if (presenter != null && !IsRegisteredParticipant(animation, presenter))
                    unregistered.Add(presenter.gameObject.name + "#" + presenter.GetInstanceID());

            // A presenter created after the director exists uses the same queue and
            // proves that LateUpdate adoption itself works. The defect is boot order.
            var root = new GameObject("audit-adoption-control");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.transform.SetParent(root.transform, false);
            CelestialAnimationPresenter control = root.AddComponent<CelestialAnimationPresenter>();
            control.Configure(Nebula.Celestial.CelestialBodyType.Planet, default,
                PlanetArchetype.MetallicRocky, EnvironmentClass.Calm,
                "audit/adoption-control");
            int controlPending = CombatPresenterAdoption.PendingCount;
            yield return null;

            Assert.That(controlPending, Is.GreaterThan(0), "control presenter entered the real adoption queue");
            Assert.That(IsRegisteredParticipant(animation, control), Is.True,
                "control presenter created after boot was not adopted, so the queue harness is broken");

            Assert.That(unregistered, Is.Empty,
                $"{unregistered.Count}/{presenters.Length} live production celestial presenter(s) were not registered: "
                + string.Join(", ", unregistered) + ". AnimationInstaller calls ResetSessionState after StartStreaming, "
                + "so boot-created presenters are removed from CombatPresenterAdoption before the director can drain it.");

            UnityEngine.Object.Destroy(root);
            adapter.EndSession();
        }

        /// <summary>
        /// P2-LIFE-003 on the real adapter: ending a session while a screen still
        /// holds the gate, then starting the next campaign. The player-visible form
        /// is leaving a campaign from an open menu and pressing New Campaign.
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator EndingASessionWhileAScreenHoldsTheGate_DoesNotHandItToTheNextCampaign()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            adapter.BeginSession();
            yield return null;

            InputGate.Suppressed = true;      // a screen is up when the campaign ends
            adapter.EndSession();
            yield return null;
            yield return null;

            NebulaContext next = adapter.BeginSession();
            yield return null;

            Assert.That(InputGate.Suppressed, Is.False,
                "the next campaign booted with the previous one's input gate still raised. Every contextual ship "
                + "component opens with `if (InputGate.Suppressed) return`, so drilling, scanning, processing, "
                + "evacuation, POI work, logistics and the save key all silently refuse with no screen left to lower it. "
                + "Neither NebulaSessionLifetime.Dispose nor WorldInstaller.PrepareSession names InputGate.");

            Assert.That(next, Is.Not.Null);
            adapter.EndSession();
        }
    }
}
