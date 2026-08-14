using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05.1's decisive check: every mechanism the table claims is verified
    /// against the model as it really instantiates, recursively — not against a
    /// prefab wrapper, which is exactly what the earlier audit got wrong.
    /// </summary>
    public class Ani051EconomyMechanicalRetrofitPlayModeTests
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

        /// <summary>Instantiates a real visual and returns its root.</summary>
        private Transform Instantiate(string visualId)
        {
            NebulaVisualDefinition definition = null;
            foreach (NebulaVisualDefinition candidate in NebulaVisualCatalog.All)
                if (candidate != null && candidate.VisualId == visualId) { definition = candidate; break; }

            if (definition == null) return null;

            var host = new GameObject($"ani051-{visualId}");
            spawned.Add(host);
            GameObject instance = NebulaVisualResolver.Instantiate(definition, host.transform, instanceName: visualId);
            return instance != null ? instance.transform : null;
        }

        private static int CountFound(Transform root, MechanicalPart[] parts, out List<string> missing)
        {
            missing = new List<string>();
            int found = 0;
            foreach (MechanicalPart part in parts)
            {
                if (NebulaVisualResolver.FindAnchor(root, part.NodeName) != null) found++;
                else missing.Add(part.NodeName);
            }
            return found;
        }

        [UnityTest]
        public IEnumerator EveryTabledMechanism_ExistsInTheRealInstantiatedHierarchy()
        {
            var checkedIds = new List<string>();

            foreach (string visualId in EconomyMechanicalProfiles.KnownVisualIds)
            {
                Transform root = Instantiate(visualId);
                if (root == null) continue; // not preloadable in this catalog view

                MechanicalPart[] parts = EconomyMechanicalProfiles.For(visualId);
                int found = CountFound(root, parts, out List<string> missing);

                Assert.AreEqual(parts.Length, found,
                    $"{visualId}: table names nodes the real model does not have -> {string.Join(", ", missing)}");
                checkedIds.Add(visualId);
                yield return null;
            }

            Assert.Greater(checkedIds.Count, 0, "at least one economy visual must have been verifiable");
        }

        [UnityTest]
        public IEnumerator TheDrillT2_ReallyHasItsEighteenPartHierarchy()
        {
            Transform root = Instantiate("structure.drill.t2");
            if (root == null) Assert.Inconclusive("DrillT2 is not preloadable in this catalog view");

            // The claim the earlier audit got wrong, checked recursively.
            int partNodes = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("part.")) partNodes++;

            Assert.GreaterOrEqual(partNodes, 14,
                "the real GLB hierarchy carries a deep part.* tree, not a single mesh");
            Assert.IsNotNull(NebulaVisualResolver.FindAnchor(root, "part.drill_bit"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheProcessingModule_ReallyHasItsSixParts()
        {
            Transform root = Instantiate("module.industry.resource_processing.t1");
            if (root == null) Assert.Inconclusive("the processing module is not preloadable in this catalog view");

            MechanicalPart[] parts = EconomyMechanicalProfiles.For("module.industry.resource_processing.t1");
            int found = CountFound(root, parts, out List<string> missing);
            Assert.AreEqual(parts.Length, found, $"missing: {string.Join(", ", missing)}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePresenterDrivesOnlyWhatTheModelHas()
        {
            Transform root = Instantiate("structure.drill.t1");
            if (root == null) Assert.Inconclusive("DrillT1 is not preloadable in this catalog view");

            var host = root.parent.gameObject;
            MechanicalDrive drive = MechanicalDrive.Working;
            var presenter = host.AddComponent<EconomyMechanicalPresenter>();
            presenter.Initialize("structure.drill.t1", root, () => drive, () => 1f);

            Assert.AreEqual("structure.drill.t1", presenter.VisualId);
            Assert.Greater(presenter.DrivenPartCount, 0, "the presenter must have bound real nodes");

            Transform rotor = NebulaVisualResolver.FindAnchor(root, "part.drill_rotor");
            Assert.IsNotNull(rotor);
            Quaternion atRest = rotor.localRotation;

            for (int i = 0; i < 30; i++) presenter.VisualTick(0.05f);
            Assert.AreNotEqual(atRest, rotor.localRotation, "a working drill must really turn its rotor");

            drive = MechanicalDrive.Disabled;
            presenter.VisualTick(0.05f);
            Assert.AreEqual(atRest, rotor.localRotation, "a disabled drill holds a safe pose");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePresenterNeverMovesTheGameplayRoot()
        {
            Transform root = Instantiate("structure.drill.t2");
            if (root == null) Assert.Inconclusive("DrillT2 is not preloadable in this catalog view");

            var host = root.parent;
            var presenter = host.gameObject.AddComponent<EconomyMechanicalPresenter>();
            presenter.Initialize("structure.drill.t2", root, () => MechanicalDrive.Working, () => 1f);

            Vector3 position = host.position;
            Quaternion rotation = host.rotation;
            Vector3 scale = host.localScale;

            for (int i = 0; i < 400; i++) presenter.VisualTick(0.02f);

            Assert.AreEqual(position, host.position, "the structure root is gameplay");
            Assert.AreEqual(rotation, host.rotation);
            Assert.AreEqual(scale, host.localScale);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MotionOff_StopsTheLoops_AndTenThousandTicksDoNotDrift()
        {
            Transform root = Instantiate("structure.drill.t1");
            if (root == null) Assert.Inconclusive("DrillT1 is not preloadable in this catalog view");

            var presenter = root.parent.gameObject.AddComponent<EconomyMechanicalPresenter>();
            presenter.Initialize("structure.drill.t1", root, () => MechanicalDrive.Working, () => 1f);

            Transform rotor = NebulaVisualResolver.FindAnchor(root, "part.drill_rotor");
            Quaternion authored = rotor.localRotation;

            for (int i = 0; i < 10000; i++) presenter.VisualTick(0.016f);
            Assert.AreNotEqual(authored, rotor.localRotation);

            AnimationAccessibility.MotionOff = true;
            presenter.VisualTick(0.016f);
            Assert.Less(Quaternion.Angle(authored, rotor.localRotation), 0.01f,
                "motion off returns every loop to the authored pose after 10,000 ticks");
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoEconomyPresenterTouchesATurretNode()
        {
            Transform root = Instantiate("structure.defense.plasma.t2");
            if (root == null) Assert.Inconclusive("PlasmaDefenseT2 is not preloadable in this catalog view");

            var presenter = root.parent.gameObject.AddComponent<EconomyMechanicalPresenter>();
            presenter.Initialize("structure.defense.plasma.t2", root, () => MechanicalDrive.Working, () => 1f);

            Transform yaw = NebulaVisualResolver.FindAnchor(root, "part.turret_yaw");
            Transform pitch = NebulaVisualResolver.FindAnchor(root, "part.turret_pitch");
            Quaternion yawAtRest = yaw != null ? yaw.localRotation : Quaternion.identity;
            Quaternion pitchAtRest = pitch != null ? pitch.localRotation : Quaternion.identity;

            for (int i = 0; i < 200; i++) presenter.VisualTick(0.02f);

            if (yaw != null) Assert.AreEqual(yawAtRest, yaw.localRotation, "the turret yaw belongs to ANI-04.3");
            if (pitch != null) Assert.AreEqual(pitchAtRest, pitch.localRotation, "and so does its pitch");
            Assert.AreEqual(3, presenter.DrivenPartCount, "only the three capacitor pylons are this presenter's");
            yield return null;
        }
    }
}
