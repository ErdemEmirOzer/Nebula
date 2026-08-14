using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>What a mechanical part does. Decides how its transform is driven, nothing else.</summary>
    public enum MechanicalRole
    {
        /// <summary>Turns continuously about its axis while the machine works.</summary>
        Rotary,

        /// <summary>Strokes back and forth along its axis — a bore feed, a piston, a lift.</summary>
        Linear,

        /// <summary>Swings out once when the structure is built or upgraded, and stays out.</summary>
        Deploy,

        /// <summary>Sweeps back and forth across an arc — a dish, a scanner.</summary>
        Sweep,

        /// <summary>Closes on something and holds — a clamp, a claw, a valve.</summary>
        Clamp
    }

    /// <summary>One real part on one real model, and how it moves.</summary>
    public readonly struct MechanicalPart
    {
        public readonly string NodeName;
        public readonly MechanicalRole Role;
        public readonly Vector3 Axis;

        /// <summary>Degrees per second for a rotary, metres for a linear stroke, degrees for a sweep or clamp.</summary>
        public readonly float Magnitude;

        /// <summary>True for parts that only High quality bothers to drive.</summary>
        public readonly bool Secondary;

        public MechanicalPart(string nodeName, MechanicalRole role, Vector3 axis, float magnitude, bool secondary = false)
        {
            NodeName = nodeName;
            Role = role;
            Axis = axis;
            Magnitude = magnitude;
            Secondary = secondary;
        }
    }

    /// <summary>
    /// Every authored mechanism the economy models actually carry, read off the
    /// shipped GLB hierarchies rather than guessed from a prefab wrapper.
    /// <para>
    /// The earlier ANI-02/03 audit stopped at the prefab and concluded these
    /// were single meshes. They are not: a Drill T2 has eighteen parts, the
    /// processing module six, the Logistics Center up to eleven. This table is
    /// the corrected record, and it is the only place a node name is written
    /// down — no presenter guesses a string.
    /// </para>
    /// <para>
    /// Defense turret yaw, pitch and recoil nodes are deliberately absent:
    /// ANI-04.3's <see cref="DefenseTurretAnimationPresenter"/> owns those, and
    /// two owners on one transform is a bug, not a feature.
    /// </para>
    /// </summary>
    public static class EconomyMechanicalProfiles
    {
        private static readonly MechanicalPart[] None = new MechanicalPart[0];

        // --- drills ---------------------------------------------------------

        private static readonly MechanicalPart[] DrillT1 =
        {
            new MechanicalPart("part.drill_rotor", MechanicalRole.Rotary, Vector3.up, 140f),
            new MechanicalPart("part.feed_carriage", MechanicalRole.Linear, Vector3.up, 0.35f)
        };

        private static readonly MechanicalPart[] DrillT2 =
        {
            new MechanicalPart("part.drill_shaft", MechanicalRole.Rotary, Vector3.up, 180f),
            new MechanicalPart("part.drill_bit", MechanicalRole.Rotary, Vector3.up, 260f),
            new MechanicalPart("part.core", MechanicalRole.Rotary, Vector3.up, 40f, secondary: true),
            new MechanicalPart("part.energy_ring.lower", MechanicalRole.Rotary, Vector3.up, 55f, secondary: true),
            new MechanicalPart("part.energy_ring.upper", MechanicalRole.Rotary, Vector3.down, 45f, secondary: true),
            new MechanicalPart("part.power_core", MechanicalRole.Rotary, Vector3.up, 30f, secondary: true),
            new MechanicalPart("part.stabilizer.1", MechanicalRole.Deploy, Vector3.right, 12f),
            new MechanicalPart("part.stabilizer.2", MechanicalRole.Deploy, Vector3.right, 12f),
            new MechanicalPart("part.stabilizer.3", MechanicalRole.Deploy, Vector3.right, 12f),
            new MechanicalPart("part.processor.1", MechanicalRole.Rotary, Vector3.up, 70f, secondary: true),
            new MechanicalPart("part.processor.2", MechanicalRole.Rotary, Vector3.up, 70f, secondary: true),
            new MechanicalPart("part.processor.3", MechanicalRole.Rotary, Vector3.up, 70f, secondary: true),
            new MechanicalPart("part.top_cap", MechanicalRole.Linear, Vector3.up, 0.12f, secondary: true),
            new MechanicalPart("part.base_ring", MechanicalRole.Rotary, Vector3.up, 12f, secondary: true)
        };

        private static readonly MechanicalPart[] DrillAdapterSolid =
        {
            new MechanicalPart("part.bore_crown_port", MechanicalRole.Rotary, Vector3.up, 200f),
            new MechanicalPart("part.bore_crown_starboard", MechanicalRole.Rotary, Vector3.up, 200f),
            new MechanicalPart("part.bore_crown_fore", MechanicalRole.Rotary, Vector3.up, 200f)
        };

        private static readonly MechanicalPart[] DrillAdapterOcean =
        {
            new MechanicalPart("part.intake_turbine_port", MechanicalRole.Rotary, Vector3.forward, 320f),
            new MechanicalPart("part.intake_turbine_starboard", MechanicalRole.Rotary, Vector3.forward, 320f),
            new MechanicalPart("part.filter_cassette", MechanicalRole.Linear, Vector3.up, 0.08f, secondary: true)
        };

        private static readonly MechanicalPart[] DrillAdapterGas =
        {
            new MechanicalPart("part.compressor_rotor", MechanicalRole.Rotary, Vector3.up, 380f),
            new MechanicalPart("part.condenser_lung_1", MechanicalRole.Linear, Vector3.up, 0.1f, secondary: true),
            new MechanicalPart("part.condenser_lung_2", MechanicalRole.Linear, Vector3.up, 0.1f, secondary: true),
            new MechanicalPart("part.condenser_lung_3", MechanicalRole.Linear, Vector3.up, 0.1f, secondary: true),
            new MechanicalPart("part.purge_valve", MechanicalRole.Clamp, Vector3.right, 25f)
        };

        // --- processing -----------------------------------------------------

        private static readonly MechanicalPart[] ProcessingT1 =
        {
            new MechanicalPart("part.processor_drum", MechanicalRole.Rotary, Vector3.forward, 90f),
            new MechanicalPart("part.intake", MechanicalRole.Clamp, Vector3.right, 20f),
            new MechanicalPart("part.output_manifold", MechanicalRole.Clamp, Vector3.right, 18f),
            new MechanicalPart("part.filter_canister_01", MechanicalRole.Rotary, Vector3.up, 35f, secondary: true),
            new MechanicalPart("part.filter_canister_02", MechanicalRole.Rotary, Vector3.up, 35f, secondary: true),
            new MechanicalPart("part.filter_canister_03", MechanicalRole.Rotary, Vector3.up, 35f, secondary: true)
        };

        private static readonly MechanicalPart[] ProcessingT2 =
        {
            new MechanicalPart("part.processor_drum_primary", MechanicalRole.Rotary, Vector3.forward, 110f),
            new MechanicalPart("part.processor_drum_secondary", MechanicalRole.Rotary, Vector3.back, 95f),
            new MechanicalPart("part.intake", MechanicalRole.Clamp, Vector3.right, 20f),
            new MechanicalPart("part.output_manifold", MechanicalRole.Clamp, Vector3.right, 18f),
            new MechanicalPart("part.filter_bank", MechanicalRole.Rotary, Vector3.up, 40f, secondary: true)
        };

        // --- surface structures ----------------------------------------------

        private static readonly MechanicalPart[] CommandT1 =
        {
            new MechanicalPart("part.antenna_yaw", MechanicalRole.Sweep, Vector3.up, 35f),
            new MechanicalPart("part.antenna_pitch", MechanicalRole.Sweep, Vector3.right, 12f, secondary: true)
        };

        private static readonly MechanicalPart[] CommandT2 =
        {
            new MechanicalPart("part.command_orbital_crown", MechanicalRole.Rotary, Vector3.up, 8f)
        };

        private static readonly MechanicalPart[] SensorT1 =
        {
            new MechanicalPart("part.sensor_crown_yaw", MechanicalRole.Sweep, Vector3.up, 45f),
            new MechanicalPart("part.sensor_blade_1", MechanicalRole.Rotary, Vector3.up, 20f, secondary: true),
            new MechanicalPart("part.sensor_blade_2", MechanicalRole.Rotary, Vector3.up, 20f, secondary: true),
            new MechanicalPart("part.sensor_blade_3", MechanicalRole.Rotary, Vector3.up, 20f, secondary: true)
        };

        private static readonly MechanicalPart[] SensorT2 =
        {
            new MechanicalPart("part.gyro_outer_yaw", MechanicalRole.Sweep, Vector3.up, 40f),
            new MechanicalPart("part.gyro_middle_pitch", MechanicalRole.Sweep, Vector3.right, 18f),
            new MechanicalPart("part.sensor_lens_roll", MechanicalRole.Rotary, Vector3.forward, 25f, secondary: true)
        };

        private static readonly MechanicalPart[] MaintenanceT1 =
        {
            new MechanicalPart("part.vehicle_lift", MechanicalRole.Linear, Vector3.up, 0.4f),
            new MechanicalPart("part.repair_arm_alpha", MechanicalRole.Sweep, Vector3.right, 15f),
            new MechanicalPart("part.repair_arm_beta", MechanicalRole.Sweep, Vector3.right, 15f, secondary: true),
            new MechanicalPart("part.repair_arm_gamma", MechanicalRole.Sweep, Vector3.right, 15f, secondary: true)
        };

        private static readonly MechanicalPart[] MaintenanceT2 =
        {
            new MechanicalPart("part.recovery_cradle", MechanicalRole.Linear, Vector3.up, 0.3f),
            new MechanicalPart("part.repair_arm_north", MechanicalRole.Sweep, Vector3.right, 15f),
            new MechanicalPart("part.repair_arm_east", MechanicalRole.Sweep, Vector3.right, 15f, secondary: true),
            new MechanicalPart("part.repair_arm_south", MechanicalRole.Sweep, Vector3.right, 15f, secondary: true),
            new MechanicalPart("part.repair_arm_west", MechanicalRole.Sweep, Vector3.right, 15f, secondary: true),
            new MechanicalPart("part.recovery_cradle.claw_1", MechanicalRole.Clamp, Vector3.right, 22f, secondary: true),
            new MechanicalPart("part.recovery_cradle.claw_2", MechanicalRole.Clamp, Vector3.right, 22f, secondary: true),
            new MechanicalPart("part.recovery_cradle.claw_3", MechanicalRole.Clamp, Vector3.right, 22f, secondary: true),
            new MechanicalPart("part.recovery_cradle.claw_4", MechanicalRole.Clamp, Vector3.right, 22f, secondary: true)
        };

        // --- logistics --------------------------------------------------------

        private static readonly MechanicalPart[] LogisticsT1 =
        {
            new MechanicalPart("part.cargo_carousel", MechanicalRole.Rotary, Vector3.up, 25f),
            new MechanicalPart("part.dock_ring_primary", MechanicalRole.Rotary, Vector3.up, 10f),
            new MechanicalPart("part.route_antenna_left", MechanicalRole.Sweep, Vector3.up, 30f, secondary: true),
            new MechanicalPart("part.route_antenna_right", MechanicalRole.Sweep, Vector3.up, 30f, secondary: true)
        };

        private static readonly MechanicalPart[] LogisticsT2 =
        {
            new MechanicalPart("part.transfer_core", MechanicalRole.Rotary, Vector3.up, 35f),
            new MechanicalPart("part.cargo_carousel_a", MechanicalRole.Rotary, Vector3.up, 25f),
            new MechanicalPart("part.cargo_carousel_b", MechanicalRole.Rotary, Vector3.up, 25f, secondary: true),
            new MechanicalPart("part.cargo_carousel_c", MechanicalRole.Rotary, Vector3.up, 25f, secondary: true),
            new MechanicalPart("part.dock_ring_port", MechanicalRole.Rotary, Vector3.up, 10f),
            new MechanicalPart("part.dock_ring_starboard", MechanicalRole.Rotary, Vector3.down, 10f, secondary: true),
            new MechanicalPart("part.route_antenna_node_01", MechanicalRole.Sweep, Vector3.up, 30f, secondary: true),
            new MechanicalPart("part.route_antenna_node_02", MechanicalRole.Sweep, Vector3.up, 30f, secondary: true),
            new MechanicalPart("part.route_antenna_node_03", MechanicalRole.Sweep, Vector3.up, 30f, secondary: true),
            new MechanicalPart("part.route_antenna_node_04", MechanicalRole.Sweep, Vector3.up, 30f, secondary: true),
            new MechanicalPart("part.route_antenna_node_05", MechanicalRole.Sweep, Vector3.up, 30f, secondary: true)
        };

        // --- defense: construction/upgrade only. Turret nodes are ANI-04.3's. ---

        private static readonly MechanicalPart[] PlasmaDefenseT2Support =
        {
            new MechanicalPart("part.capacitor_pylon.01", MechanicalRole.Deploy, Vector3.right, 10f),
            new MechanicalPart("part.capacitor_pylon.02", MechanicalRole.Deploy, Vector3.right, 10f),
            new MechanicalPart("part.capacitor_pylon.03", MechanicalRole.Deploy, Vector3.right, 10f)
        };


        private static readonly MechanicalPart[] LogisticsCargoT1 =
        {
            new MechanicalPart("part.cargo_magazine", MechanicalRole.Rotary, Vector3.forward, 45f),
            new MechanicalPart("part.clamp_left", MechanicalRole.Clamp, Vector3.forward, 22f),
            new MechanicalPart("part.clamp_right", MechanicalRole.Clamp, Vector3.back, 22f),
            new MechanicalPart("part.cargo_cell_01", MechanicalRole.Linear, Vector3.up, 0.06f, secondary: true),
            new MechanicalPart("part.cargo_cell_02", MechanicalRole.Linear, Vector3.up, 0.06f, secondary: true),
            new MechanicalPart("part.cargo_cell_03", MechanicalRole.Linear, Vector3.up, 0.06f, secondary: true)
        };

        private static readonly MechanicalPart[] LogisticsCargoT2 =
        {
            new MechanicalPart("part.cargo_magazine_left", MechanicalRole.Rotary, Vector3.forward, 45f),
            new MechanicalPart("part.cargo_magazine_right", MechanicalRole.Rotary, Vector3.back, 45f),
            new MechanicalPart("part.transfer_shuttle", MechanicalRole.Linear, Vector3.right, 0.25f),
            new MechanicalPart("part.cargo_clamp.01", MechanicalRole.Clamp, Vector3.forward, 22f, secondary: true),
            new MechanicalPart("part.cargo_clamp.02", MechanicalRole.Clamp, Vector3.forward, 22f, secondary: true)
        };

        private static readonly MechanicalPart[] StationConstructionT1 =
        {
            new MechanicalPart("part.fabricator_arm_left", MechanicalRole.Sweep, Vector3.right, 20f),
            new MechanicalPart("part.fabricator_arm_right", MechanicalRole.Sweep, Vector3.left, 20f),
            new MechanicalPart("part.print_head", MechanicalRole.Linear, Vector3.up, 0.2f),
            new MechanicalPart("part.feed_cassette", MechanicalRole.Rotary, Vector3.forward, 60f, secondary: true)
        };

        private static readonly MechanicalPart[] StationConstructionT2 =
        {
            new MechanicalPart("part.fabrication_gate", MechanicalRole.Deploy, Vector3.right, 30f),
            new MechanicalPart("part.fabricator_arm_left", MechanicalRole.Sweep, Vector3.right, 20f),
            new MechanicalPart("part.fabricator_arm_right", MechanicalRole.Sweep, Vector3.left, 20f),
            new MechanicalPart("part.print_head_primary", MechanicalRole.Linear, Vector3.up, 0.2f),
            new MechanicalPart("part.print_head_secondary", MechanicalRole.Linear, Vector3.up, 0.18f, secondary: true),
            new MechanicalPart("part.feed_cassette", MechanicalRole.Rotary, Vector3.forward, 60f, secondary: true)
        };

        private static readonly Dictionary<string, MechanicalPart[]> ByVisualId =
            new Dictionary<string, MechanicalPart[]>(System.StringComparer.Ordinal)
            {
                { "structure.drill.t1", DrillT1 },
                { "structure.drill.t2", DrillT2 },
                { "structure.drill_adapter.solid", DrillAdapterSolid },
                { "structure.drill_adapter.ocean", DrillAdapterOcean },
                { "structure.drill_adapter.gas", DrillAdapterGas },
                { "module.industry.logistics_cargo.t1", LogisticsCargoT1 },
                { "module.industry.logistics_cargo.t2", LogisticsCargoT2 },
                { "module.industry.station_construction.t1", StationConstructionT1 },
                { "module.industry.station_construction.t2", StationConstructionT2 },
                { "module.industry.resource_processing.t1", ProcessingT1 },
                { "module.industry.resource_processing.t2", ProcessingT2 },
                { "structure.command.t1", CommandT1 },
                { "structure.command.t2", CommandT2 },
                { "structure.support.sensor_array.t1", SensorT1 },
                { "structure.support.sensor_array.t2", SensorT2 },
                { "structure.support.maintenance_platform.t1", MaintenanceT1 },
                { "structure.support.maintenance_platform.t2", MaintenanceT2 },
                { "station.system_logistics.t1", LogisticsT1 },
                { "station.system_logistics.t2", LogisticsT2 },
                { "structure.defense.plasma.t2", PlasmaDefenseT2Support }
            };

        /// <summary>The mechanisms a visual really has, or an empty set.</summary>
        public static MechanicalPart[] For(string visualId)
            => visualId != null && ByVisualId.TryGetValue(visualId, out MechanicalPart[] parts) ? parts : None;

        /// <summary>Every visual id this table covers. Test-facing.</summary>
        public static IEnumerable<string> KnownVisualIds => ByVisualId.Keys;

        /// <summary>True when this visual has real mechanisms and must not be bobbed as a whole.</summary>
        public static bool HasRealMechanisms(string visualId) => For(visualId).Length > 0;

        /// <summary>
        /// The turret transforms ANI-04.3 owns. Nothing in this table may name
        /// one of them, and a test proves it.
        /// </summary>
        public static readonly string[] TurretOwnedNodes =
        {
            "part.turret_yaw", "part.turret_pitch", "part.barrel_pitch",
            "part.elevation_yoke", "part.laser_lance_recoil",
            "part.laser_turret_port", "part.laser_turret_starboard",
            "part.laser_yoke_port", "part.laser_yoke_starboard",
            "swivel-joint", "turret-head"
        };

        /// <summary>True when this node belongs to the combat turret presenter.</summary>
        public static bool IsTurretOwned(string nodeName)
        {
            for (int i = 0; i < TurretOwnedNodes.Length; i++)
                if (string.Equals(TurretOwnedNodes[i], nodeName, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// How the five resource families read on the same mechanism. The model
        /// is identical; only cadence and amplitude differ, because inventing a
        /// mechanism a family's model does not have is exactly the mistake this
        /// part exists to correct.
        /// </summary>
        public static float FamilyCadence(Nebula.Extraction.ResourceFamily family)
        {
            switch (family)
            {
                case Nebula.Extraction.ResourceFamily.Metal: return 0.85f;   // heavy percussion
                case Nebula.Extraction.ResourceFamily.Wood: return 1.0f;     // steady boring
                case Nebula.Extraction.ResourceFamily.Gas: return 1.35f;     // fast compressor
                case Nebula.Extraction.ResourceFamily.Water: return 1.2f;    // turbine
                case Nebula.Extraction.ResourceFamily.Diamond: return 0.7f;  // precise, slow cutter
                default: return 1f;
            }
        }
    }
}
