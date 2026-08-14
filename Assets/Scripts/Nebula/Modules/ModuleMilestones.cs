using Nebula.Extraction;
using Nebula.World;

namespace Nebula.Modules
{
    /// <summary>
    /// Real predicates behind <see cref="ModuleDef.MilestoneLabel"/>. Every
    /// module defaults to "always satisfied" (the label stays purely
    /// informational, as it was before VS-007) except the ones explicitly
    /// wired here — Hangar-Bot's "First Controlled Planet" (VS-007 §1), and
    /// (VS-013 §2) Weapon Coordination's "Command Center" and Shield
    /// Systems' "First AI Threat". All three ProgressionScreen call sites
    /// (confirm dialog, detail panel, action button) must route through this
    /// single function so the preview and the actual purchase can never
    /// disagree.
    /// </summary>
    public static class ModuleMilestones
    {
        public static bool IsSatisfied(string moduleId)
        {
            if (moduleId == "exploration.hangar_bot") return ExtractionState.HasAnyControlledPlanet();

            // VS-013: "the ship's current system" for this purchase-gate purpose is
            // wherever it's currently Full-materialized — the same notion every other
            // UI (SectorDebugHud etc.) already treats as "where the ship is".
            if (moduleId == "defense.weapon_coordination")
            {
                string fullSystemId = StarSystemRegistry.Streaming?.FullSystemId;
                return fullSystemId != null && SystemStatusQuery.HasActiveCommandAnywhere(fullSystemId);
            }

            if (moduleId == "defense.shield_systems") return Combat.CombatCampaignState.FirstProbe.ThreatKnown;

            return true;
        }
    }
}
