using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Poi;

namespace Nebula.Visuals
{
    /// <summary>
    /// The light language for everything built on or around a planet: drills,
    /// surface structures, the Logistics Center, salvage and every POI.
    /// <para>
    /// Colours continue the functional palette LGT-03 established for the ship
    /// — command is white-cyan, industry is amber, sensors are cyan,
    /// maintenance is turquoise, damage is the only red — so a player reads the
    /// same meaning from a structure as from their own hull. Nothing here
    /// invents a competing palette.
    /// </para>
    /// <para>
    /// Every value is emission. No structure, resource or POI ever gets a
    /// real-time light: a hundred drills cost the same as none.
    /// </para>
    /// </summary>
    public static class StructureLightingProfiles
    {
        /// <summary>Ceiling on any structure or POI emission, matching the ship's.</summary>
        public const float MaxEmissionStrength = ShipLightingProfiles.MaxEmissionStrength;

        // ---- resource families -------------------------------------------------

        /// <summary>
        /// The five families, carrying the same character LGT-02 gave their
        /// home worlds so a Metal drill reads as the same substance as a Metal
        /// planet. Rarity is deliberately absent: it is communicated by the
        /// existing rarity icons, and inventing a second colour code for it
        /// would contradict them.
        /// </summary>
        public static Color ResourceFamilyColor(ResourceFamily family)
        {
            switch (family)
            {
                case ResourceFamily.Metal: return new Color(0.72f, 0.76f, 0.82f);   // cold smoke-grey
                case ResourceFamily.Wood: return new Color(0.78f, 0.58f, 0.32f);    // warm organic brown
                case ResourceFamily.Gas: return new Color(0.62f, 0.72f, 0.80f);     // pale atmospheric
                case ResourceFamily.Water: return new Color(0.38f, 0.70f, 0.88f);   // blue/cyan
                case ResourceFamily.Diamond: return new Color(0.55f, 0.78f, 0.92f); // cyan-lilac crystal
                default: return new Color(0.72f, 0.76f, 0.82f);
            }
        }

        // ---- the one lifecycle resolver -----------------------------------------

        /// <summary>
        /// The baseline every built thing shares, keyed on the real
        /// <see cref="DrillLifecycle"/> both drills and surface structures
        /// already use. Type-specific profiles start from this rather than
        /// re-deciding what "damaged" looks like in five places.
        /// </summary>
        public static (Color color, float strength) Lifecycle(DrillLifecycle state)
        {
            switch (state)
            {
                case DrillLifecycle.Planned: return (ShipLightingProfiles.Industrial, 0f);
                case DrillLifecycle.CapsuleInTransit: return (ShipLightingProfiles.Industrial, 0.16f);
                case DrillLifecycle.Constructing: return (ShipLightingProfiles.Industrial, 0.28f);
                case DrillLifecycle.Active: return (ShipLightingProfiles.Industrial, 0.45f);
                case DrillLifecycle.Damaged: return (ShipLightingProfiles.Warning, 0.34f);
                case DrillLifecycle.Disabled: return (ShipLightingProfiles.Emergency, 0.14f);
                case DrillLifecycle.Dismantled: return (ShipLightingProfiles.Emergency, 0f);
                default: return (ShipLightingProfiles.Industrial, 0f);
            }
        }

        /// <summary>True while the state is one a player should read as an alarm rather than as operation.</summary>
        public static bool IsAlarmState(DrillLifecycle state)
            => state == DrillLifecycle.Damaged || state == DrillLifecycle.Disabled;

        /// <summary>Tier reads as more lit panel, never as a different colour.</summary>
        public static float TierScale(int tier) => tier >= 2 ? 1.22f : 1f;

        // ---- drills ---------------------------------------------------------------

        /// <summary>
        /// A drill wears its resource family once it is actually running.
        /// Before that it is a construction site, and a stalled or full one
        /// falls back to a steady amber warning — never a blink.
        /// </summary>
        public static (Color color, float strength) Drill(ResourceFamily family, int tier, DrillLifecycle state, bool storageFull, bool upgrading)
        {
            if (state != DrillLifecycle.Active) return Lifecycle(state);

            if (upgrading) return (new Color(1f, 0.85f, 0.6f), 0.5f);          // amber-white work light
            if (storageFull) return (ShipLightingProfiles.Industrial, 0.55f);   // steady amber "nothing is moving"

            return (ResourceFamilyColor(family), 0.42f * TierScale(tier));
        }

        /// <summary>Collectable output on a drill's pad — a limited showing of the family colour, never bright enough to read as a beacon.</summary>
        public static (Color color, float strength) DrillOutputReady(ResourceFamily family)
            => (ResourceFamilyColor(family), 0.26f);

        // ---- surface structures -----------------------------------------------------

        /// <summary>Which functional colour a structure type wears when it is running.</summary>
        public static Color StructureColor(SurfaceStructureType type)
        {
            switch (type)
            {
                case SurfaceStructureType.Command: return new Color(0.72f, 0.92f, 1f);        // controlled white-cyan
                case SurfaceStructureType.Laser: return new Color(1f, 0.45f, 0.38f);          // laser palette
                case SurfaceStructureType.Plasma: return new Color(0.65f, 0.55f, 1f);         // plasma palette
                case SurfaceStructureType.SensorArray: return ShipLightingProfiles.Defense;   // cyan
                case SurfaceStructureType.MaintenancePlatform: return ShipLightingProfiles.Engineering; // turquoise-white
                default: return ShipLightingProfiles.Industrial;
            }
        }

        /// <summary>
        /// A structure's light. Defensive structures sit at a low standby and
        /// rise to a static ready level — charge, overheat and muzzle flash are
        /// LGT-05's, not this part's.
        /// </summary>
        public static (Color color, float strength) Structure(SurfaceStructureType type, int tier, DrillLifecycle state, bool combatReady)
        {
            if (state != DrillLifecycle.Active) return Lifecycle(state);

            Color color = StructureColor(type);
            float baseStrength;
            switch (type)
            {
                case SurfaceStructureType.Command: baseStrength = 0.46f; break;
                case SurfaceStructureType.Laser:
                case SurfaceStructureType.Plasma: baseStrength = combatReady ? 0.5f : 0.24f; break; // standby vs ready, both static
                case SurfaceStructureType.SensorArray: baseStrength = 0.4f; break;
                case SurfaceStructureType.MaintenancePlatform: baseStrength = 0.4f; break;
                default: baseStrength = 0.35f; break;
            }

            return (color, baseStrength * TierScale(tier));
        }

        /// <summary>A Sensor Array with nothing to talk to, or a Maintenance Platform waiting on an Engineer.</summary>
        public static (Color color, float strength) StructureBlocked(SurfaceStructureType type)
        {
            switch (type)
            {
                case SurfaceStructureType.SensorArray: return (new Color(0.78f, 0.82f, 0.86f), 0.16f); // low cold white
                case SurfaceStructureType.MaintenancePlatform: return (ShipLightingProfiles.Industrial, 0.2f);
                default: return (ShipLightingProfiles.Industrial, 0.2f);
            }
        }

        // ---- logistics center ----------------------------------------------------------

        /// <summary>Fill fraction at which the centre starts reading as a problem rather than as work.</summary>
        public const float LogisticsWarningFill = 0.8f;

        public static (Color color, float strength) Logistics(LogisticsCenterTier tier, LogisticsCenterState state,
            float fillFraction, bool upgrading, bool routeActive, bool dockOccupied)
        {
            switch (state)
            {
                case LogisticsCenterState.Constructing: return (ShipLightingProfiles.Industrial, 0.28f);
                case LogisticsCenterState.Disabled: return (ShipLightingProfiles.Emergency, 0.14f);
                case LogisticsCenterState.Dismantled: return (ShipLightingProfiles.Emergency, 0f);
            }

            float tierScale = TierScale((int)tier + 1);

            if (upgrading) return (new Color(1f, 0.85f, 0.6f), 0.5f);

            float fill = Mathf.Clamp01(fillFraction);
            if (fill >= 0.999f) return (ShipLightingProfiles.Warning, Mathf.Min(MaxEmissionStrength, 0.6f * tierScale));
            if (fill >= LogisticsWarningFill) return (ShipLightingProfiles.Industrial, 0.55f * tierScale);

            if (dockOccupied) return (ShipLightingProfiles.Industrial, 0.5f * tierScale);
            if (routeActive) return (ShipLightingProfiles.Industrial, 0.38f * tierScale);

            return (new Color(1f, 0.88f, 0.72f), 0.22f * tierScale); // idle: low amber-white
        }

        // ---- POIs ------------------------------------------------------------------------

        /// <summary>
        /// A POI's light is gated by what the player actually knows. At Signal
        /// and Located every type looks identical — an abstract, dim contact —
        /// so approaching one can never reveal what it is before the game says
        /// so. Only Identified opens the type's real profile.
        /// </summary>
        public static (Color color, float strength) Poi(PoiType type, PoiKnowledgeLevel level)
        {
            switch (level)
            {
                case PoiKnowledgeLevel.Unknown: return (PoiContactColor, 0f);
                // Abstract contact: the same for every type, on purpose.
                case PoiKnowledgeLevel.Signal: return (PoiContactColor, 0.08f);
                case PoiKnowledgeLevel.Located: return (PoiContactColor, 0.18f);
                case PoiKnowledgeLevel.Resolved: return (ResolvedColor(type), ResolvedStrength(type));
            }

            // Identified and Active: the type's own character.
            float operational = level == PoiKnowledgeLevel.Active ? 1f : 0.7f;
            switch (type)
            {
                case PoiType.Anomaly: return (new Color(0.55f, 0.62f, 1f), 0.42f * operational);          // mysterious cyan-violet
                case PoiType.Wreck: return (new Color(0.62f, 0.68f, 0.74f), 0.16f * operational);         // very low cold metal
                case PoiType.AbandonedFacility: return (new Color(1f, 0.72f, 0.4f), 0.2f * operational);  // a few emergency panels
                case PoiType.Protected: return (PirateBaseColor, 0.38f * operational);
                default: return (PoiContactColor, 0.2f);
            }
        }

        /// <summary>The abstract "something is out there" colour every unidentified contact shares.</summary>
        public static readonly Color PoiContactColor = new Color(0.6f, 0.72f, 0.85f);

        /// <summary>Hostile identity — dirty red-amber, distinct from the player's own warning red.</summary>
        public static readonly Color PirateBaseColor = new Color(0.85f, 0.33f, 0.22f);

        private static Color ResolvedColor(PoiType type) => type == PoiType.Protected
            ? ShipLightingProfiles.Emergency
            : new Color(0.6f, 0.64f, 0.68f);

        /// <summary>A resolved POI is spent: its systems go dark or nearly so.</summary>
        private static float ResolvedStrength(PoiType type) => type == PoiType.Wreck ? 0.06f : 0.08f;

        /// <summary>A pirate base's turrets and garrison, at standby only. Firing and damage belong to LGT-05.</summary>
        public static (Color color, float strength) PirateBaseStandby(bool resolved)
            => resolved ? (ShipLightingProfiles.Emergency, 0.05f) : (PirateBaseColor, 0.3f);

        /// <summary>The safe-zone marker: visible from a distance without becoming a flare, and never a real beacon light.</summary>
        public static (Color color, float strength) SafetyMarker()
            => (new Color(0.72f, 0.86f, 1f), 0.45f);

        /// <summary>Neutral training identity — deliberately unlike the hostile palette so a dummy is never mistaken for a threat.</summary>
        public static (Color color, float strength) TrainingDummy()
            => (new Color(0.95f, 0.85f, 0.42f), 0.3f);

        // ---- salvage and pickups ------------------------------------------------------------

        /// <summary>
        /// A collectable sitting in space. Carries its family colour so it is
        /// recognisable, at a strength that says "you can pick this up" without
        /// announcing what it is worth — rarity stays with the icons.
        /// </summary>
        public static (Color color, float strength) Pickup(ResourceFamily family, bool discovered)
            => discovered ? (ResourceFamilyColor(family), 0.3f) : (Color.black, 0f);

        /// <summary>Salvage on a wreck. Readable as a work point, never as a working ship.</summary>
        public static (Color color, float strength) SalvageNode(bool discovered)
            => discovered ? (new Color(0.78f, 0.72f, 0.55f), 0.24f) : (Color.black, 0f);

        // ---- resolution ------------------------------------------------------------------------

        /// <summary>
        /// Applies the quality tier and accessibility, then clamps. Low trims
        /// hazard/warning emission; Flash Reduction and Safe Mode trim it
        /// further without removing the colour that carries the meaning.
        /// </summary>
        public static Color Resolve(Color color, float strength, LightingQuality quality, bool isWarning)
        {
            float qualityScale = quality switch
            {
                LightingQuality.Low => isWarning ? 0.6f : 0.8f,
                LightingQuality.High => 1.1f,
                _ => 1f
            };

            float scaled = isWarning
                ? LightingAccessibility.ScaleFlashIntensity(strength * qualityScale)
                : LightingAccessibility.ScaleIntensity(strength * qualityScale);

            scaled = Mathf.Clamp(scaled, 0f, MaxEmissionStrength);
            Color resolved = color * scaled;
            resolved.a = 1f;
            return resolved;
        }
    }
}
