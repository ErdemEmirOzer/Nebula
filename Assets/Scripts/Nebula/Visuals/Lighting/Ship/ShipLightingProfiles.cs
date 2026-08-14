using UnityEngine;
using Nebula.Hangar;

namespace Nebula.Visuals
{
    /// <summary>What the ship is doing, expressed only as something to light. Derived from live gameplay state; never a second authority over it.</summary>
    public enum ShipLightingState
    {
        Idle,
        Throttle,
        Boost,
        Cruise,
        Damaged,
        Disabled,
        Repair
    }

    /// <summary>Where a module visual is in its life. Read from module progression and construction stage.</summary>
    public enum ModuleLightingState
    {
        Locked,
        Installed,
        Upgrading,
        Disabled
    }

    /// <summary>Hangar bay condition, as light.</summary>
    public enum HangarLightingState
    {
        DockAvailable,
        LaunchPreparation,
        Occupied,
        Unavailable
    }

    /// <summary>A support vehicle's condition, as light.</summary>
    public enum VehicleLightingState
    {
        Docked,
        Launched,
        MissionActive,
        Returning,
        Disabled
    }

    /// <summary>
    /// The industrial palette every ship, module, hangar and vehicle light in
    /// Nebula is drawn from. Colours are assigned by function, so a player can
    /// read what a light means without learning a key: energy and engines are
    /// cold white-blue, industry and cargo are amber, engineering is
    /// turquoise, defence is cold cyan, and warnings are the only red on the
    /// hull.
    /// <para>
    /// Every value here is an emission colour and strength. Nothing in this
    /// file creates a real-time light: the whole ship and every vehicle are
    /// lit by the one global key light plus a single hull fill, and everything
    /// else on them glows rather than illuminates.
    /// </para>
    /// </summary>
    public static class ShipLightingProfiles
    {
        // ---- the functional palette --------------------------------------

        /// <summary>Energy and propulsion. Cold white-blue, never a neon.</summary>
        public static readonly Color Energy = new Color(0.62f, 0.78f, 1f);

        /// <summary>Industry, cargo, production. Amber/copper.</summary>
        public static readonly Color Industrial = new Color(1f, 0.66f, 0.28f);

        /// <summary>Engineering and repair work. White-turquoise.</summary>
        public static readonly Color Engineering = new Color(0.55f, 0.95f, 0.88f);

        /// <summary>Defence and sensors. Cold cyan.</summary>
        public static readonly Color Defense = new Color(0.42f, 0.78f, 0.95f);

        /// <summary>Damage and warnings. The only red on the ship, and deliberately restrained.</summary>
        public static readonly Color Warning = new Color(1f, 0.34f, 0.18f);

        /// <summary>Emergency lighting once main power is gone.</summary>
        public static readonly Color Emergency = new Color(1f, 0.45f, 0.22f);

        /// <summary>
        /// Ceiling on any emission strength on the ship or a vehicle. Above
        /// this the hull stops reading as lit metal and starts reading as a
        /// light source, which is the arcade look this project is not.
        /// </summary>
        public const float MaxEmissionStrength = 1.6f;

        // ---- ship states ---------------------------------------------------

        /// <summary>
        /// Engine emission for a ship state. <paramref name="throttleFraction"/>
        /// only matters while under thrust; every other state is a flat,
        /// unmoving value, because a pulsing or blinking hull belongs to the
        /// animation parts, not here.
        /// </summary>
        public static (Color color, float strength) Engine(ShipLightingState state, float throttleFraction)
        {
            float t = Mathf.Clamp01(throttleFraction);
            switch (state)
            {
                case ShipLightingState.Idle: return (Energy, 0.18f);
                case ShipLightingState.Throttle: return (Energy, Mathf.Lerp(0.18f, 0.95f, t));
                case ShipLightingState.Boost: return (Energy, Mathf.Min(1.35f, Mathf.Lerp(0.6f, 1.35f, t)));
                case ShipLightingState.Cruise: return (Energy, 0.85f); // steady, long-haul
                case ShipLightingState.Damaged: return (Energy, Mathf.Lerp(0.12f, 0.5f, t));
                case ShipLightingState.Disabled: return (Energy, 0f); // main power is gone
                case ShipLightingState.Repair: return (Energy, 0.2f);
                default: return (Energy, 0.18f);
            }
        }

        /// <summary>Cockpit and navigation lighting. Off only when main power is.</summary>
        public static (Color color, float strength) Cockpit(ShipLightingState state)
        {
            switch (state)
            {
                case ShipLightingState.Disabled: return (Emergency, 0.22f); // limited emergency lighting only
                case ShipLightingState.Damaged: return (Warning, 0.35f);
                case ShipLightingState.Repair: return (Engineering, 0.5f);
                case ShipLightingState.Boost:
                case ShipLightingState.Cruise: return (Energy, 0.42f);
                default: return (Energy, 0.32f);
            }
        }

        /// <summary>The energy-line / status run along the hull.</summary>
        public static (Color color, float strength) SystemStatus(ShipLightingState state)
        {
            switch (state)
            {
                case ShipLightingState.Damaged: return (Warning, 0.55f);
                case ShipLightingState.Disabled: return (Emergency, 0.3f);
                case ShipLightingState.Repair: return (Engineering, 0.6f);
                case ShipLightingState.Boost: return (Energy, 0.5f);
                default: return (Energy, 0.3f);
            }
        }

        // ---- modules ---------------------------------------------------------

        /// <summary>
        /// Module panel emission. Tier does not change the colour — a higher
        /// tier lights more of the same panel rather than repainting it, so
        /// progression reads as capability rather than as a different device.
        /// </summary>
        public static (Color color, float strength) Module(ModuleLightingState state, Color functionColor, int tier)
        {
            float tierScale = Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(1f, 5f, Mathf.Max(1, tier)));
            switch (state)
            {
                case ModuleLightingState.Locked: return (functionColor, 0f);
                case ModuleLightingState.Installed: return (functionColor, 0.34f * tierScale);
                case ModuleLightingState.Upgrading: return (Industrial, 0.5f); // steady work light
                case ModuleLightingState.Disabled: return (Emergency, 0.28f);
                default: return (functionColor, 0f);
            }
        }

        /// <summary>Which functional colour a module's panels take, from its real catalog id.</summary>
        public static Color ModuleFunctionColor(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return Energy;
            if (moduleId.Contains("industry") || moduleId.Contains("cargo") || moduleId.Contains("processing")
                || moduleId.Contains("construction") || moduleId.Contains("hangar")) return Industrial;
            if (moduleId.Contains("defense") || moduleId.Contains("shield") || moduleId.Contains("weapon")) return Defense;
            if (moduleId.Contains("nuclear") || moduleId.Contains("laboratory") || moduleId.Contains("energy")) return Energy;
            if (moduleId.Contains("exploration") || moduleId.Contains("scanner") || moduleId.Contains("navigation")) return Defense;
            return Energy;
        }

        // ---- hangar -----------------------------------------------------------

        public static (Color color, float strength) Hangar(HangarLightingState state)
        {
            switch (state)
            {
                case HangarLightingState.DockAvailable: return (Defense, 0.24f);       // low cyan/white
                case HangarLightingState.LaunchPreparation: return (Industrial, 0.55f); // steady amber
                case HangarLightingState.Occupied: return (Industrial, 0.7f);           // industrial amber
                case HangarLightingState.Unavailable: return (Warning, 0.2f);           // low red
                default: return (Defense, 0.24f);
            }
        }

        // ---- support vehicles --------------------------------------------------

        /// <summary>
        /// A vehicle's colour is its class's job: cyan sensors for a Scout,
        /// amber haulage for a Collector, turquoise work light for an
        /// Engineer, cold defensive cyan for an Escort.
        /// </summary>
        public static Color VehicleClassColor(VehicleClass vehicleClass)
        {
            switch (vehicleClass)
            {
                case VehicleClass.Scout: return Defense;
                case VehicleClass.Collector: return Industrial;
                case VehicleClass.Engineer: return Engineering;
                case VehicleClass.Escort: return new Color(0.36f, 0.68f, 1f); // colder than sensor cyan
                default: return Energy;
            }
        }

        /// <summary>
        /// Emission for a vehicle in a given state. Mk II adds a small,
        /// controlled amount of lit panel — never a different colour or a
        /// second visual language.
        /// </summary>
        public static (Color color, float strength) Vehicle(VehicleClass vehicleClass, int mk, VehicleLightingState state)
        {
            Color color = VehicleClassColor(vehicleClass);
            float mkBonus = mk >= 2 ? 0.08f : 0f;

            float strength;
            switch (state)
            {
                case VehicleLightingState.Docked: strength = 0.14f; break;
                case VehicleLightingState.Launched: strength = 0.4f; break;
                case VehicleLightingState.MissionActive: strength = 0.62f; break;
                case VehicleLightingState.Returning: strength = 0.34f; break;
                case VehicleLightingState.Disabled: return (Emergency, 0.16f);
                default: strength = 0.14f; break;
            }

            return (color, Mathf.Min(MaxEmissionStrength, strength + mkBonus));
        }

        /// <summary>
        /// Folds accessibility in and clamps. Flash Reduction and
        /// Photosensitivity Safe Mode pull warning and boost intensity down
        /// without removing the colour, so the state stays readable.
        /// </summary>
        public static Color Resolve(Color color, float strength, bool isWarningOrBoost)
        {
            float scaled = isWarningOrBoost
                ? LightingAccessibility.ScaleFlashIntensity(strength)
                : LightingAccessibility.ScaleIntensity(strength);

            scaled = Mathf.Clamp(scaled, 0f, MaxEmissionStrength);
            Color resolved = color * scaled;
            resolved.a = 1f;
            return resolved;
        }
    }
}
