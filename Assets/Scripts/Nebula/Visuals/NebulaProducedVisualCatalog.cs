using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Sectors;
using UnityEngine;

namespace Nebula.Visuals
{
    public enum NebulaCommonVfx
    {
        ScanPulse,
        BuildHologram,
        RepairPulse,
        ShieldImpact,
        HitSpark,
        BoostTrail,
        WarningPulse
    }

    /// <summary>
    /// Stable, presentation-only paths for the in-house ArtProduction batch.
    /// High-resolution authoring copies remain in ArtProduction; these paths
    /// point only to the optimized Unity runtime derivatives.
    /// </summary>
    public static class NebulaProducedVisualCatalog
    {
        private const string StarRoot = "VisualTextures/Stars/";
        private const string EnvironmentRoot = "VisualIcons/Environment/";
        private const string StatusRoot = "VisualIcons/Status/";
        private const string VfxRoot = "VisualVfx/Common/";

        public static string StarSurface(StarClass starClass) =>
            StarRoot + "star." + StarToken(starClass) + ".surface.2048x1024";

        public static string StarCorona(StarClass starClass) =>
            StarRoot + "star." + StarToken(starClass) + ".corona_add.1024";

        public static string Environment(EnvironmentClass environment) => environment switch
        {
            EnvironmentClass.Calm => EnvironmentRoot + "environment.calm",
            EnvironmentClass.Harsh => EnvironmentRoot + "environment.harsh",
            EnvironmentClass.Extreme => EnvironmentRoot + "environment.extreme",
            _ => EnvironmentRoot + "environment.calm"
        };

        public static string Knowledge(SystemKnowledgeLevel level) => level switch
        {
            SystemKnowledgeLevel.Signal => StatusRoot + "knowledge.signal",
            SystemKnowledgeLevel.Located => StatusRoot + "knowledge.located",
            SystemKnowledgeLevel.Visited => StatusRoot + "knowledge.visited",
            _ => null
        };

        public static string Dominance(DominanceState state) => state switch
        {
            DominanceState.Developing => StatusRoot + "dominance.developing",
            DominanceState.Controlled => StatusRoot + "dominance.controlled",
            DominanceState.Contested => StatusRoot + "dominance.contested",
            DominanceState.Lost => StatusRoot + "dominance.lost",
            _ => null
        };

        public static string Link(SystemLinkState state) => state switch
        {
            SystemLinkState.Live => StatusRoot + "link.live",
            SystemLinkState.LastKnown => StatusRoot + "link.last_known",
            SystemLinkState.Disconnected => StatusRoot + "link.disconnected",
            _ => null
        };

        public const string Attention = StatusRoot + "attention";
        public const string RaidWarning = StatusRoot + "raid.warning";

        public static string Vfx(NebulaCommonVfx vfx) => vfx switch
        {
            NebulaCommonVfx.ScanPulse => VfxRoot + "vfx.scan.pulse",
            NebulaCommonVfx.BuildHologram => VfxRoot + "vfx.build.hologram",
            NebulaCommonVfx.RepairPulse => VfxRoot + "vfx.repair.pulse",
            NebulaCommonVfx.ShieldImpact => VfxRoot + "vfx.shield.impact",
            NebulaCommonVfx.HitSpark => VfxRoot + "vfx.hit.spark",
            NebulaCommonVfx.BoostTrail => VfxRoot + "vfx.boost.trail",
            NebulaCommonVfx.WarningPulse => VfxRoot + "vfx.warning.pulse",
            _ => VfxRoot + "vfx.hit.spark"
        };

        private static string StarToken(StarClass starClass) => starClass switch
        {
            StarClass.RedDwarf => "reddwarf",
            StarClass.Yellow => "yellow",
            StarClass.White => "white",
            StarClass.BlueGiant => "bluegiant",
            _ => "yellow"
        };
    }

    public static class NebulaProducedVisualResolver
    {
        public static Texture2D StarSurface(StarClass starClass) =>
            NebulaIconResolver.LoadTexture(NebulaProducedVisualCatalog.StarSurface(starClass));

        public static Texture2D StarCorona(StarClass starClass) =>
            NebulaIconResolver.LoadTexture(NebulaProducedVisualCatalog.StarCorona(starClass));

        public static Sprite Environment(EnvironmentClass environment) =>
            NebulaIconResolver.LoadSprite(NebulaProducedVisualCatalog.Environment(environment));

        public static Sprite Knowledge(SystemKnowledgeLevel level) =>
            NebulaIconResolver.LoadSprite(NebulaProducedVisualCatalog.Knowledge(level));

        public static Sprite Dominance(DominanceState state) =>
            NebulaIconResolver.LoadSprite(NebulaProducedVisualCatalog.Dominance(state));

        public static Sprite Link(SystemLinkState state) =>
            NebulaIconResolver.LoadSprite(NebulaProducedVisualCatalog.Link(state));

        public static Sprite AttentionIcon() =>
            NebulaIconResolver.LoadSprite(NebulaProducedVisualCatalog.Attention);

        public static Sprite RaidWarningIcon() =>
            NebulaIconResolver.LoadSprite(NebulaProducedVisualCatalog.RaidWarning);

        public static Texture2D VfxTexture(NebulaCommonVfx vfx) =>
            NebulaIconResolver.LoadTexture(NebulaProducedVisualCatalog.Vfx(vfx));
    }
}
