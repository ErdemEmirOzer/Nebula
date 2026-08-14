using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>Which presenter is allowed to write a given transform.</summary>
    public enum TransformOwner
    {
        /// <summary>ANI-01: the player ship's visual root banking.</summary>
        PlayerShipRoot,

        /// <summary>The existing ThrusterEffect — engine halo anchors.</summary>
        ThrusterEffect,

        /// <summary>ANI-04: weapon gimbal and recoil.</summary>
        WeaponGimbal,

        /// <summary>ANI-03: a support vehicle's visual-root banking.</summary>
        VehicleRoot,

        /// <summary>ANI-05.2: ship modules, hangar, RCS, and vehicle class parts.</summary>
        ShipVehicleMechanics,

        /// <summary>Lighting, which only ever writes material values.</summary>
        Lighting
    }

    /// <summary>
    /// The authored mechanisms on the player ship and the seven support
    /// vehicles, read off the real GLB hierarchies.
    /// <para>
    /// This table exists because the ANI-02/03 audit read prefab wrappers and
    /// concluded these models were single meshes. They are not: the Mk II ship
    /// carries a deep <c>part.module.*</c> tree with a real hangar hatch, and
    /// every vehicle has the mechanism its class is named for.
    /// </para>
    /// <para>
    /// Ownership is explicit. The engine halo anchors belong to ThrusterEffect,
    /// the weapon gimbal to ANI-04, the visual roots to ANI-01/03 — none of them
    /// appear here, and a test pins that.
    /// </para>
    /// </summary>
    public static class ShipVehicleMechanicalProfiles
    {
        private static readonly MechanicalPart[] None = new MechanicalPart[0];

        // --- the player ship -------------------------------------------------

        /// <summary>The real hangar hatch, present from Stage06 upward.</summary>
        public const string HangarHatchNode = "part.module.06.hatch.door";

        /// <summary>The real hangar bay the hatch sits in.</summary>
        public const string HangarBayNode = "part.module.06.bay";

        /// <summary>The ship's own anchor for launching and docking.</summary>
        public const string HangarAnchor = "anchor.hangar";

        /// <summary>
        /// The auxiliary thrusters. There is no node named "rcs" anywhere on the
        /// ship; these are the real attitude thrusters and are what attitude
        /// motion is shown on. The main <c>anchor.thruster.L/R</c> belong to
        /// ThrusterEffect and are deliberately absent.
        /// </summary>
        public static readonly MechanicalPart[] AttitudeThrusters =
        {
            new MechanicalPart("part.thruster.aux.L", MechanicalRole.Clamp, Vector3.right, 6f),
            new MechanicalPart("part.thruster.aux.R", MechanicalRole.Clamp, Vector3.left, 6f)
        };

        /// <summary>The hangar hatch, driven as a door rather than a loop.</summary>
        public static readonly MechanicalPart[] HangarDoor =
        {
            new MechanicalPart(HangarHatchNode, MechanicalRole.Deploy, Vector3.right, 85f)
        };

        // --- the seven vehicles -------------------------------------------------

        private static readonly MechanicalPart[] ScoutMk1 =
        {
            new MechanicalPart("part.sensor_wing_left", MechanicalRole.Deploy, Vector3.forward, 40f),
            new MechanicalPart("part.sensor_wing_right", MechanicalRole.Deploy, Vector3.back, 40f)
        };

        private static readonly MechanicalPart[] CollectorMk1 =
        {
            new MechanicalPart("part.cargo_cradle", MechanicalRole.Deploy, Vector3.right, 18f),
            new MechanicalPart("part.cargo_clamp_left", MechanicalRole.Clamp, Vector3.forward, 25f),
            new MechanicalPart("part.cargo_clamp_right", MechanicalRole.Clamp, Vector3.back, 25f)
        };

        private static readonly MechanicalPart[] CollectorMk2 =
        {
            new MechanicalPart("part.cargo_carousel", MechanicalRole.Rotary, Vector3.up, 55f),
            new MechanicalPart("part.cargo_clamp_left", MechanicalRole.Clamp, Vector3.forward, 25f),
            new MechanicalPart("part.cargo_clamp_right", MechanicalRole.Clamp, Vector3.back, 25f)
        };

        private static readonly MechanicalPart[] EngineerMk1 =
        {
            new MechanicalPart("part.repair_mandible_left", MechanicalRole.Deploy, Vector3.forward, 30f),
            new MechanicalPart("part.repair_mandible_right", MechanicalRole.Deploy, Vector3.back, 30f)
        };

        private static readonly MechanicalPart[] EngineerMk2 =
        {
            new MechanicalPart("part.repair_arm_left", MechanicalRole.Deploy, Vector3.forward, 35f),
            new MechanicalPart("part.repair_arm_right", MechanicalRole.Deploy, Vector3.back, 35f),
            new MechanicalPart("part.recovery_winch", MechanicalRole.Rotary, Vector3.right, 120f, secondary: true)
        };

        private static readonly MechanicalPart[] EscortMk1 =
        {
            new MechanicalPart("part.fixed_laser_lance", MechanicalRole.Deploy, Vector3.right, 8f)
        };

        private static readonly MechanicalPart[] EscortMk2 =
        {
            new MechanicalPart("part.fixed_laser_spine", MechanicalRole.Deploy, Vector3.right, 8f)
        };

        private static readonly Dictionary<string, MechanicalPart[]> ByVehicleId =
            new Dictionary<string, MechanicalPart[]>(System.StringComparer.Ordinal)
            {
                { "vehicle.scout.mk1", ScoutMk1 },
                { "vehicle.collector.mk1", CollectorMk1 },
                { "vehicle.collector.mk2", CollectorMk2 },
                { "vehicle.engineer.mk1", EngineerMk1 },
                { "vehicle.engineer.mk2", EngineerMk2 },
                { "vehicle.escort.mk1", EscortMk1 },
                { "vehicle.escort.mk2", EscortMk2 }
            };

        /// <summary>The mechanisms this vehicle's model really has.</summary>
        public static MechanicalPart[] ForVehicle(string visualId)
            => visualId != null && ByVehicleId.TryGetValue(visualId, out MechanicalPart[] parts) ? parts : None;

        /// <summary>Every vehicle visual id this table covers. Test-facing.</summary>
        public static IEnumerable<string> KnownVehicleIds => ByVehicleId.Keys;

        public static bool HasRealMechanisms(string visualId) => ForVehicle(visualId).Length > 0;

        /// <summary>
        /// Transforms this table must never name, because another presenter
        /// already drives them.
        /// </summary>
        public static readonly string[] ForeignOwnedNodes =
        {
            "anchor.thruster.L", "anchor.thruster.R",
            "part.gimbal_yaw", "part.gimbal_pitch",
            "anchor.muzzle.laser", "anchor.projectile.spawn"
        };

        /// <summary>True when this node belongs to a different presenter.</summary>
        public static bool IsForeignOwned(string nodeName)
        {
            for (int i = 0; i < ForeignOwnedNodes.Length; i++)
                if (string.Equals(ForeignOwnedNodes[i], nodeName, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Who owns a given transform. The single record of the split.</summary>
        public static TransformOwner OwnerOf(string nodeName)
        {
            if (nodeName == null) return TransformOwner.ShipVehicleMechanics;
            if (nodeName.StartsWith("anchor.thruster.")) return TransformOwner.ThrusterEffect;
            if (nodeName.StartsWith("part.gimbal_")) return TransformOwner.WeaponGimbal;
            return TransformOwner.ShipVehicleMechanics;
        }
    }
}
