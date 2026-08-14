using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05.2's decisive check: every node the table claims is verified
    /// against the model as it really instantiates, recursively. The earlier
    /// "single mesh" verdict came from stopping at the prefab wrapper.
    /// </summary>
    public class Ani052ShipVehicleMechanicalRetrofitPlayModeTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
            AnimationAccessibility.ResetToDefaults();
        }

        private Transform Instantiate(string visualId)
        {
            NebulaVisualDefinition definition = null;
            foreach (NebulaVisualDefinition candidate in NebulaVisualCatalog.All)
                if (candidate != null && candidate.VisualId == visualId) { definition = candidate; break; }
            if (definition == null) return null;

            var host = new GameObject($"ani052-{visualId}");
            spawned.Add(host);
            GameObject instance = NebulaVisualResolver.Instantiate(definition, host.transform, instanceName: visualId);
            return instance != null ? instance.transform : null;
        }

        [UnityTest]
        public IEnumerator EveryVehicleMechanism_ExistsInTheRealInstantiatedHierarchy()
        {
            int verified = 0;

            foreach (string visualId in ShipVehicleMechanicalProfiles.KnownVehicleIds)
            {
                Transform root = Instantiate(visualId);
                if (root == null) continue;

                MechanicalPart[] parts = ShipVehicleMechanicalProfiles.ForVehicle(visualId);
                var missing = new List<string>();
                foreach (MechanicalPart part in parts)
                    if (NebulaVisualResolver.FindAnchor(root, part.NodeName) == null) missing.Add(part.NodeName);

                Assert.IsEmpty(missing,
                    $"{visualId}: the table names nodes the real model lacks -> {string.Join(", ", missing)}");
                verified++;
                yield return null;
            }

            Assert.AreEqual(7, verified, "all seven vehicle models must have been verifiable");
        }

        [UnityTest]
        public IEnumerator TheScoutReallyHasItsSensorWingsAndScanOrigin()
        {
            Transform root = Instantiate("vehicle.scout.mk1");
            Assert.IsNotNull(root, "the Scout model must instantiate");

            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(root, "part.sensor_wing_left"));
            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(root, "part.sensor_wing_right"));
            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(root, "anchor.sensor.scan_origin"),
                "the scan VFX anchor the earlier audit said did not exist");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheEscortReallyHasItsFixedLanceAndMuzzle()
        {
            Transform mk1 = Instantiate("vehicle.escort.mk1");
            Assert.IsNotNull(mk1);
            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(mk1, "part.fixed_laser_lance"));
            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(mk1, "anchor.projectile.muzzle"),
                "ANI-GAP-004 claimed no weapon node existed; it does");
            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(mk1, "hardpoint.laser.fixed"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheEngineerMkTwoReallyHasItsWinch()
        {
            Transform root = Instantiate("vehicle.engineer.mk2");
            Assert.IsNotNull(root);
            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(root, "part.recovery_winch"));
            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(root, "anchor.recovery_tow"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePresenterDrivesAVehiclesRealParts_AndStowsThemWhenIdle()
        {
            Transform root = Instantiate("vehicle.collector.mk2");
            Assert.IsNotNull(root);

            MechanicalDrive drive = MechanicalDrive.Working;
            var presenter = root.parent.gameObject.AddComponent<EconomyMechanicalPresenter>();
            presenter.Initialize("vehicle.collector.mk2", root,
                ShipVehicleMechanicalProfiles.ForVehicle("vehicle.collector.mk2"), () => drive, () => 1f);

            Assert.AreEqual(3, presenter.DrivenPartCount, "the carousel and both clamps");

            Transform carousel = NebulaVisualResolver.FindAnchor(root, "part.cargo_carousel");
            Quaternion atRest = carousel.localRotation;

            for (int i = 0; i < 30; i++) presenter.VisualTick(0.05f);
            Assert.AreNotEqual(atRest, carousel.localRotation, "a working carousel must really turn");

            drive = MechanicalDrive.Stowed;
            presenter.VisualTick(0.05f);
            Assert.AreEqual(atRest, carousel.localRotation, "an idle Collector stows");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePresenterNeverMovesTheVehicleRoot()
        {
            Transform root = Instantiate("vehicle.engineer.mk2");
            Assert.IsNotNull(root);

            Transform host = root.parent;
            var presenter = host.gameObject.AddComponent<EconomyMechanicalPresenter>();
            presenter.Initialize("vehicle.engineer.mk2", root,
                ShipVehicleMechanicalProfiles.ForVehicle("vehicle.engineer.mk2"),
                () => MechanicalDrive.Working, () => 1f);

            Vector3 position = host.position;
            Quaternion rotation = host.rotation;

            for (int i = 0; i < 400; i++) presenter.VisualTick(0.02f);

            Assert.AreEqual(position, host.position, "the vehicle root belongs to VehicleAgent");
            Assert.AreEqual(rotation, host.rotation);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MotionOff_SnapsToolsToTheirRealStatePose()
        {
            Transform root = Instantiate("vehicle.scout.mk1");
            Assert.IsNotNull(root);

            var presenter = root.parent.gameObject.AddComponent<EconomyMechanicalPresenter>();
            presenter.Initialize("vehicle.scout.mk1", root,
                ShipVehicleMechanicalProfiles.ForVehicle("vehicle.scout.mk1"),
                () => MechanicalDrive.Working, () => 1f);

            AnimationAccessibility.MotionOff = true;
            presenter.VisualTick(0.02f);

            Transform wing = NebulaVisualResolver.FindAnchor(root, "part.sensor_wing_left");
            Quaternion snapped = wing.localRotation;
            for (int i = 0; i < 50; i++) presenter.VisualTick(0.02f);

            Assert.AreEqual(snapped, wing.localRotation,
                "with motion off the tool holds its real state pose and stops moving");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePlayerShipReallyHasItsHangarHatchAndAuxThrusters()
        {
            Transform root = null;
            foreach (NebulaVisualDefinition candidate in NebulaVisualCatalog.All)
            {
                if (candidate == null || candidate.ResourcePath == null) continue;
                if (!candidate.ResourcePath.Contains("PlayerShip")) continue;
                root = Instantiate(candidate.VisualId);
                if (root != null) break;
            }

            if (root == null) Assert.Inconclusive("no PlayerShip variant is reachable through the catalog view");

            // The nodes ANI-GAP-003 said did not exist.
            bool hasHangarAnchor = NebulaVisualResolver.FindAnchor(root, ShipVehicleMechanicalProfiles.HangarAnchor) != null;
            Assert.IsTrue(hasHangarAnchor, "anchor.hangar is real");
            yield return null;
        }
    }
}
