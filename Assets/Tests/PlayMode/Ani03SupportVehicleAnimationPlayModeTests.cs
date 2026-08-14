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
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-03 in a real session: a support vehicle built by the real hangar,
    /// posed from the phase its own mission state machine reports, and proven
    /// never to move the agent, its collider, or any gameplay number. Nothing
    /// here drives a vehicle by hand — every phase comes from the hangar's own
    /// restore/launch APIs.
    /// </summary>
    public class Ani03SupportVehicleAnimationPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;

        private readonly Dictionary<string, string> slot1Backup = new Dictionary<string, string>();

        private static IEnumerable<string> Slot1Files()
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
            slot1Backup.Clear();
            foreach (string f in Slot1Files())
                slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlotAndAccessibility()
        {
            AnimationAccessibility.ResetToDefaults();
            foreach (var kv in slot1Backup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu\'s New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        private static NebulaAnimationDirector Director()
        {
            var director = Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director, "the session must install an animation director");
            return director;
        }

        /// <summary>Builds a real vehicle through the hangar and puts it in the given phase.</summary>
        private static VehicleAgent SpawnVehicle(NebulaContext context, string id, VehicleDef def, VehiclePhase phase)
        {
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            Assert.IsNotNull(hangar);
            hangar.ApplyHangarTier(2);

            VehicleAgent agent = hangar.RestoreVehicle(id, def);
            Assert.IsNotNull(agent, $"the hangar must build {def.Id}");
            agent.RestorePhysical(context.StartSystemId, context.Ship.transform.position, Quaternion.identity,
                Vector3.zero, phase, MissionKind.None, null, 0f, false, null, 0f);
            return agent;
        }

        private static VehicleAnimationPresenter PresenterOf(VehicleAgent agent)
        {
            var presenter = agent.GetComponent<VehicleAnimationPresenter>();
            Assert.IsNotNull(presenter, "every vehicle the hangar builds must carry an animation presenter");
            return presenter;
        }

        // --- wiring ----------------------------------------------------------

        [UnityTest]
        public IEnumerator EveryCatalogVehicle_GetsAPresenterAndAVisualChild()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;

            int index = 0;
            foreach (VehicleDef def in VehicleCatalog.All)
            {
                VehicleAgent agent = SpawnVehicle(context, $"vehicle-ani03-{index++}", def, VehiclePhase.TravelOut);
                VehicleAnimationPresenter presenter = PresenterOf(agent);

                Assert.IsTrue(presenter.HasVisual, $"{def.Id} must expose a visual child to pose");
                Assert.AreEqual(def.Class, presenter.VehicleClass, $"{def.Id} must animate as its own class");
            }
        }

        [UnityTest]
        public IEnumerator PresentersBuiltByGameplay_AreAdoptedByTheDirector()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;

            SpawnVehicle(context, "vehicle-ani03-adopt", VehicleCatalog.Scout, VehiclePhase.TravelOut);
            yield return null;
            yield return null;

            Assert.AreEqual(0, VehicleAnimationPresenter.PendingCount,
                "the director must drain the adoption queue; a presenter left waiting is never ticked");
        }

        // --- the pose follows the real mission -------------------------------

        [UnityTest]
        public IEnumerator ThePose_TracksThePhaseTheMissionStateMachineReports()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = SpawnVehicle(context, "vehicle-ani03-phase", VehicleCatalog.EngineerMk1, VehiclePhase.TravelOut);
            VehicleAnimationPresenter presenter = PresenterOf(agent);

            director.Tick(0.1f);
            Assert.AreEqual(VehicleAnimationState.Travel, presenter.CurrentState, "TravelOut must read as travel");

            agent.RestorePhysical(context.StartSystemId, agent.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.OnStation, MissionKind.None, null, 0f, false, null, 0f);
            director.Tick(0.1f);
            Assert.AreEqual(VehicleAnimationState.Working, presenter.CurrentState, "OnStation must read as working");

            agent.RestorePhysical(context.StartSystemId, agent.transform.position, Quaternion.identity, Vector3.zero,
                VehiclePhase.Docking, MissionKind.None, null, 0f, false, null, 0f);
            director.Tick(0.1f);
            Assert.AreEqual(VehicleAnimationState.Docking, presenter.CurrentState, "Docking must read as docking");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ADisabledVehicle_HoldsItsSafeClosedPose_WhateverItWasDoing()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = SpawnVehicle(context, "vehicle-ani03-dead", VehicleCatalog.EscortMk1, VehiclePhase.TravelOut);
            VehicleAnimationPresenter presenter = PresenterOf(agent);
            director.Tick(0.1f);
            Assert.AreEqual(VehicleAnimationState.Travel, presenter.CurrentState);

            // Real damage through the shared Durability path, not a flag flip.
            agent.DurabilityRef.ApplyDamage(agent.Def.Shield + agent.Def.Armor + agent.Def.Hull + 1000f);
            Assert.IsTrue(agent.DurabilityRef.Disabled, "the test must actually destroy the vehicle");

            for (int i = 0; i < 20; i++) director.Tick(0.05f);

            Assert.AreEqual(VehicleAnimationState.Disabled, presenter.CurrentState,
                "a dead vehicle is dead whatever phase it was in");
            Assert.AreEqual(0f, presenter.BankDegrees, 0.05f, "a dead vehicle does not lean into anything");
            yield return null;
        }

        // --- the gameplay object is never touched ----------------------------

        [UnityTest]
        public IEnumerator TheAgentsOwnTransform_IsNeverMovedByTheAnimation()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            // Parked in the hangar the agent flies nowhere on its own, so any
            // change to its transform could only come from the animation layer.
            VehicleAgent agent = SpawnVehicle(context, "vehicle-ani03-still", VehicleCatalog.CollectorMk2, VehiclePhase.TravelOut);
            agent.RestorePhysical(context.StartSystemId, context.Ship.transform.position + Vector3.right * 40f,
                Quaternion.Euler(0f, 35f, 0f), Vector3.zero, VehiclePhase.TravelOut, MissionKind.None, null, 0f, false, null, 0f);

            Vector3 rootPosition = agent.transform.position;
            Quaternion rootRotation = agent.transform.rotation;
            Vector3 rootScale = agent.transform.localScale;

            for (int i = 0; i < 200; i++) director.Tick(0.02f);

            Assert.AreEqual(rootPosition, agent.transform.position, "the presenter must never move the gameplay root");
            Assert.AreEqual(rootRotation, agent.transform.rotation, "a hardpoint must still point where the agent aimed it");
            Assert.AreEqual(rootScale, agent.transform.localScale, "the collider's scale is gameplay, not presentation");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheVisualChild_ReturnsExactlyToItsAuthoredPose_AfterThousandsOfTicks()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = SpawnVehicle(context, "vehicle-ani03-drift", VehicleCatalog.Scout, VehiclePhase.TravelOut);
            VehicleAnimationPresenter presenter = PresenterOf(agent);

            Transform visual = presenter.VisualTransform;
            Assert.IsNotNull(visual, "the presenter must have found a model to pose");
            Vector3 basePosition = visual.localPosition;
            Quaternion baseRotation = visual.localRotation;
            Vector3 baseScale = visual.localScale;

            // Fly it around so the pose is genuinely exercised, not idling at zero.
            for (int i = 0; i < 2000; i++)
            {
                agent.transform.position += new Vector3(Mathf.Sin(i * 0.05f), 0f, Mathf.Cos(i * 0.05f)) * 0.5f;
                director.Tick(0.016f);
            }

            presenter.ResetToBasePose();

            Assert.AreEqual(basePosition.x, visual.localPosition.x, 1e-4f, "no positional drift after 2000 ticks");
            Assert.AreEqual(basePosition.y, visual.localPosition.y, 1e-4f);
            Assert.AreEqual(basePosition.z, visual.localPosition.z, 1e-4f);
            Assert.Less(Quaternion.Angle(baseRotation, visual.localRotation), 0.01f, "no rotational drift after 2000 ticks");
            Assert.AreEqual(baseScale.x, visual.localScale.x, 1e-4f, "no scale drift after 2000 ticks");
        }

        [UnityTest]
        public IEnumerator TheBank_NeverExceedsTheComfortCeiling_HoweverHardItTurns()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = SpawnVehicle(context, "vehicle-ani03-bank", VehicleCatalog.Scout, VehiclePhase.TravelOut);
            VehicleAnimationPresenter presenter = PresenterOf(agent);

            float worst = 0f;
            for (int i = 0; i < 400; i++)
            {
                // A deliberately absurd sideways jerk — far past anything the
                // real flight code produces.
                agent.transform.position += agent.transform.right * 60f;
                director.Tick(0.016f);
                worst = Mathf.Max(worst, Mathf.Abs(presenter.BankDegrees));
            }

            Assert.LessOrEqual(worst, VehicleAnimationProfiles.MaxBankDegrees + 0.01f,
                "the clamp is a hard ceiling, not a suggestion");
            Assert.Greater(worst, 0f, "a hard turn must actually produce a lean");
        }

        [UnityTest]
        public IEnumerator MotionOff_StopsThePose_ButNotTheVehicle()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            VehicleAgent agent = SpawnVehicle(context, "vehicle-ani03-motionoff", VehicleCatalog.EscortMk2, VehiclePhase.TravelOut);
            VehicleAnimationPresenter presenter = PresenterOf(agent);

            Transform visual = presenter.VisualTransform;
            Assert.IsNotNull(visual, "the presenter must have found a model to pose");
            Quaternion baseRotation = visual.localRotation;

            for (int i = 0; i < 60; i++)
            {
                agent.transform.position += agent.transform.right * 20f;
                director.Tick(0.016f);
            }
            Assert.Greater(Mathf.Abs(presenter.BankDegrees), 0.1f, "the vehicle must be leaning before we switch motion off");

            AnimationAccessibility.MotionOff = true;
            Vector3 positionBefore = agent.transform.position;

            for (int i = 0; i < 60; i++)
            {
                agent.transform.position += agent.transform.right * 20f;
                director.Tick(0.016f);
            }

            Assert.Less(Quaternion.Angle(baseRotation, visual.localRotation), 0.01f,
                "with motion off the model sits at its authored pose");
            Assert.AreNotEqual(positionBefore, agent.transform.position,
                "the vehicle still visibly goes where the mission sends it");
        }

        // --- session hygiene --------------------------------------------------

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesNoStalePresentersWaiting()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            SpawnVehicle(adapter.Context, "vehicle-ani03-restart", VehicleCatalog.EngineerMk2, VehiclePhase.TravelOut);
            yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length,
                "a restart must not leave a second animation director behind");
            Assert.AreEqual(0, VehicleAnimationPresenter.PendingCount,
                "the previous session's presenters must not linger in the adoption queue");
        }
    }
}
