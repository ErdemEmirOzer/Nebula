namespace Nebula.Sectors
{
    public enum StellarRiskLevel { Safe, Warning, Critical }

    /// <summary>
    /// Resolved gameplay modifiers for one risk level (VS-009 §2). Combat is
    /// not implemented yet — <see cref="CombatEnergyMultiplier"/> and
    /// <see cref="EnvironmentalHeatPerSecondFractionOfMax"/> are typed data
    /// only, ready for VS-013+ consumers; the Critical zone never applies
    /// shield/armor/hull damage directly here.
    /// </summary>
    public readonly struct StellarRiskState
    {
        public readonly StellarRiskLevel Level;
        public readonly float ShieldRegenMultiplier;
        public readonly float CoolingMultiplier;
        public readonly float CombatEnergyMultiplier;
        /// <summary>Fraction of max heat added per second while in this zone (0 outside Critical).</summary>
        public readonly float EnvironmentalHeatPerSecondFractionOfMax;
        public readonly bool CruiseAllowed;

        public StellarRiskState(StellarRiskLevel level, float shieldRegenMultiplier, float coolingMultiplier,
            float combatEnergyMultiplier, float environmentalHeatPerSecondFractionOfMax, bool cruiseAllowed)
        {
            Level = level;
            ShieldRegenMultiplier = shieldRegenMultiplier;
            CoolingMultiplier = coolingMultiplier;
            CombatEnergyMultiplier = combatEnergyMultiplier;
            EnvironmentalHeatPerSecondFractionOfMax = environmentalHeatPerSecondFractionOfMax;
            CruiseAllowed = cruiseAllowed;
        }
    }

    /// <summary>
    /// Pure C# distance-to-star risk classification (VS-009 §2). Critical and
    /// Warning radii are <see cref="SystemManifest.StarRadius"/> scaled by
    /// the star class's own multipliers (<see cref="StarClassCatalog"/>) —
    /// no UnityEngine dependency, so it is trivially unit-testable and usable
    /// from HUD, ShipCruise and any future combat/heat consumer alike.
    /// </summary>
    public static class StellarRiskResolver
    {
        public static StellarRiskState Resolve(float distance, float starRadius, StarClass starClass)
        {
            float criticalRadius = starRadius * StarClassCatalog.CriticalRadiusMultiplier(starClass);
            float warningRadius = starRadius * StarClassCatalog.WarningRadiusMultiplier(starClass);

            if (distance <= criticalRadius)
                return new StellarRiskState(StellarRiskLevel.Critical, shieldRegenMultiplier: 0f, coolingMultiplier: 0.40f,
                    combatEnergyMultiplier: 0.40f, environmentalHeatPerSecondFractionOfMax: 0.02f, cruiseAllowed: false);

            if (distance <= warningRadius)
                return new StellarRiskState(StellarRiskLevel.Warning, shieldRegenMultiplier: 0f, coolingMultiplier: 0.75f,
                    combatEnergyMultiplier: 0.75f, environmentalHeatPerSecondFractionOfMax: 0f, cruiseAllowed: false);

            return new StellarRiskState(StellarRiskLevel.Safe, shieldRegenMultiplier: 1f, coolingMultiplier: 1f,
                combatEnergyMultiplier: 1f, environmentalHeatPerSecondFractionOfMax: 0f, cruiseAllowed: true);
        }

        public static string Label(StellarRiskLevel level) => level switch
        {
            StellarRiskLevel.Safe => "GÜVENLİ", StellarRiskLevel.Warning => "UYARI", StellarRiskLevel.Critical => "KRİTİK", _ => "?"
        };

        /// <summary>
        /// Text+symbol tag — never color alone (VS-009 §4 information-gate rule).
        /// <para>
        /// This was ✓ / ⚠ / ☠, and all three drew as the same empty box.
        /// LiberationSans.ttf is the only face in the project and its cmap has no
        /// U+2713, U+26A0 or U+2620, so the three risk levels were visually
        /// identical apart from colour — the one thing §4 exists to prevent. It
        /// is the same defect NebulaHudPlanetContext already fixed once, where a
        /// "✓"/"✕" pair was dropped for being font-dependent characters rather
        /// than the project's own iconography. Do not restore them.
        /// </para>
        /// <para>
        /// The replacement is deliberately the ramp
        /// <see cref="Nebula.Sectors.EnvironmentClassCatalog.ShortTag"/> uses.
        /// Both are three-step severity scales governed by the same rule, and
        /// giving them one vocabulary — hollow, filled, filled-triangle — means a
        /// player learns the escalation once instead of twice.
        /// </para>
        /// </summary>
        public static string ShortTag(StellarRiskLevel level) => level switch
        {
            StellarRiskLevel.Safe => "◊ GÜVENLİ", StellarRiskLevel.Warning => "♦ UYARI", StellarRiskLevel.Critical => "▲ KRİTİK", _ => "?"
        };
    }
}
