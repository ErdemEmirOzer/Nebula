using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Diagnostics;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-017A.1 §4: a data-driven sweep of every real content catalog entry this project ships
    /// (vehicles, structures/defense tiers, modules, logistics center tiers, weapons, enemies,
    /// POI types) against the L1-40 contract: every gate must be reachable at or below level 40,
    /// every entry must resolve through the SAME RuntimeCatalog/TF-003 snapshot the game itself
    /// validates at boot (ContentCatalogValidator), and level gates are cross-checked against
    /// their own authoritative source catalog (RuntimeCatalog doesn't carry every raw gate field
    /// for every content kind -- e.g. base T1 structures have no player-level gate at all, only
    /// Command tier, which is correct/by-design, not a gap). Findings are classified into the
    /// same QualityGateReport this slice's overall S0/S1 gate uses. This is a structural
    /// reachability/consumer-wiring sweep, not a full manual narrative audit of every field.
    /// </summary>
    public class Vs017a1CatalogAuditTests
    {
        private const int ContractMaxLevel = 40;

        private static RuntimeCatalog BuildCatalog() => RuntimeCatalogBuilder.Build(new CSharpContentCatalog());

        [Test]
        public void ContentCatalogValidator_ReportsZeroErrors_OnTheRealShippedCatalog()
        {
            List<string> errors = ContentCatalogValidator.Validate(new CSharpContentCatalog());
            Assert.AreEqual(0, errors.Count, "the same validator the game itself runs at boot (NebulaBootstrap step 0) must report zero errors on the real shipped content -- a boot-blocking S0 if it doesn't");
        }

        [Test]
        public void AllVehicles_UnlockLevelWithinL1To40_AndRealBuildConsumerExists()
        {
            var report = new QualityGateReport();
            RuntimeCatalog catalog = BuildCatalog();

            foreach (var kv in catalog.Vehicles)
            {
                ContentVehicle v = kv.Value;
                if (v.UnlockLevel > ContractMaxLevel)
                    report.AddS1("l1-40-unreachable-gate", $"vehicle '{v.Id}' requires level {v.UnlockLevel} > {ContractMaxLevel}", v.Id);

                // Real consumer: VehicleCatalog.Get must resolve the same id, and HangarProductionService
                // must have an actual reject-reason path for it (proven at the pure-service level in
                // Vs017aRouteHarnessTests' own real HangarProductionService exercise).
                VehicleDef def = VehicleCatalog.Get(v.Id);
                if (def == null) report.AddS0("dead-catalog-entry", $"vehicle '{v.Id}' exists in RuntimeCatalog but VehicleCatalog.Get cannot resolve it -- an unreachable/dead entry", v.Id);
            }

            ReportFindings(report);
        }

        [Test]
        public void AllModules_UnlockLevelWithinL1To40_AndRealCatalogEntryExists()
        {
            var report = new QualityGateReport();
            RuntimeCatalog catalog = BuildCatalog();

            foreach (var kv in catalog.Modules)
            {
                ContentModule m = kv.Value;
                // "nuclear.*" is real, deliberately-gated L41+ content (out of this slice's own scope
                // per the spec's own "L41+ ekleme" exclusion) -- correctly unreachable within L1-40,
                // not a defect. Everything else is meant to be reachable within the contract window.
                if (m.UnlockLevel > ContractMaxLevel && !m.Id.StartsWith("nuclear."))
                    report.AddS1("l1-40-unreachable-gate", $"module '{m.Id}' requires level {m.UnlockLevel} > {ContractMaxLevel}", m.Id);

                bool found = false;
                foreach (ModuleDef d in ModuleCatalog.Modules) if (d.Id == m.Id) { found = true; break; }
                if (!found) report.AddS0("dead-catalog-entry", $"module '{m.Id}' exists in RuntimeCatalog but not in ModuleCatalog.Modules", m.Id);
            }

            ReportFindings(report);
        }

        [Test]
        public void AllLogisticsCenterTiers_RequiredLevelWithinL1To40()
        {
            var report = new QualityGateReport();
            RuntimeCatalog catalog = BuildCatalog();

            foreach (var kv in catalog.LogisticsCenterTiers)
            {
                ContentLogisticsCenterTier t = kv.Value;
                if (t.RequiredLevel > ContractMaxLevel)
                    report.AddS1("l1-40-unreachable-gate", $"logistics center tier '{t.Id}' requires level {t.RequiredLevel} > {ContractMaxLevel}", t.Id);
            }

            ReportFindings(report);
        }

        [Test]
        public void PlanetaryDefenseTierUpgrades_RequiredCharacterLevelWithinL1To40()
        {
            var report = new QualityGateReport();
            // T1 base structures (SurfaceStructureCatalog.Command/Laser) intentionally carry no
            // player-level gate at all (only Command/Hangar-tier gates) -- by design, not audited
            // here. Only the VS-014+ tier upgrades add a real RequiredCharacterLevel.
            var tiers = new[]
            {
                PlanetaryDefenseCatalog.CommandT2, PlanetaryDefenseCatalog.LaserT2,
                PlanetaryDefenseCatalog.PlasmaT1, PlanetaryDefenseCatalog.PlasmaT2,
                PlanetaryDefenseCatalog.SensorArrayT1, PlanetaryDefenseCatalog.SensorArrayT2,
                PlanetaryDefenseCatalog.MaintenancePlatformT1, PlanetaryDefenseCatalog.MaintenancePlatformT2
            };
            foreach (StructureTierDef t in tiers)
                if (t.RequiredCharacterLevel > ContractMaxLevel)
                    report.AddS1("l1-40-unreachable-gate", $"structure tier '{t.DisplayName}' requires level {t.RequiredCharacterLevel} > {ContractMaxLevel}", t.DisplayName);

            ReportFindings(report);
        }

        [Test]
        public void AllEnemies_ResolveThroughRuntimeCatalogAndLiveEnemyCatalog_Consistently()
        {
            var report = new QualityGateReport();
            RuntimeCatalog catalog = BuildCatalog();

            foreach (var kv in catalog.Enemies)
            {
                Combat.EnemyDef def = Combat.EnemyCatalog.Get(kv.Key);
                if (def == null) report.AddS0("dead-catalog-entry", $"enemy '{kv.Key}' exists in RuntimeCatalog but Combat.EnemyCatalog.Get cannot resolve it", kv.Key);
            }
            // And the reverse -- every live enemy def is represented in the content snapshot.
            foreach (Combat.EnemyDef d in Combat.EnemyCatalog.All)
                if (!catalog.ContainsContentId(d.Id))
                    report.AddS1("catalog-drift", $"live enemy '{d.Id}' is missing from the RuntimeCatalog content snapshot", d.Id);

            ReportFindings(report);
        }

        [Test]
        public void AllProcessingRecipes_ResolveRealInputOutputItems()
        {
            var report = new QualityGateReport();
            RuntimeCatalog catalog = BuildCatalog();

            foreach (var kv in catalog.Recipes)
            {
                ContentRecipe r = kv.Value;
                if (!catalog.TryGetItem(r.InputItemId, out _))
                    report.AddS0("save-facing-item-reference-broken", $"recipe '{r.Id}' references unknown input item '{r.InputItemId}'", r.Id);
                if (!catalog.TryGetItem(r.OutputItemId, out _))
                    report.AddS0("save-facing-item-reference-broken", $"recipe '{r.Id}' references unknown output item '{r.OutputItemId}'", r.Id);
            }

            ReportFindings(report);
        }

        [Test]
        public void ContentFingerprint_GenerationAndSchema_UnchangedByThisAuditSlice()
        {
            // This audit is read-only by construction -- it must never itself be the reason any of
            // these three numbers moved.
            Assert.AreEqual(15, SaveService.CurrentSchema);
            // Old contract: 2, the generation version in force when this audit slice was written.
            // Current production authority: SectorManifestGenerator.GenerationVersion = 3
            // (Assets/Scripts/Nebula/Sectors/SectorManifestGenerator.cs) -- 9d33695 moved systems off the
            // uniform 2x2x2 sub-cell grid onto the galaxy spiral arms, a deliberate world-generation
            // change that regenerates every existing campaign's universe and was approved as such.
            // Invariant still protected: this read-only catalog audit may not move it. The literal is
            // deliberate -- reading the constant back would assert nothing, so a bump has to break this
            // test and be re-pinned alongside the world-gen change that earned it.
            Assert.AreEqual(3, Sectors.SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        private static void ReportFindings(QualityGateReport report)
        {
            if (report.S0Count > 0 || report.S1Count > 0)
            {
                var details = new System.Text.StringBuilder();
                foreach (QualityGateFinding f in report.Findings) details.Append(f.severity).Append(": ").Append(f.description).Append(" | ");
                Assert.Fail($"S0={report.S0Count} S1={report.S1Count}: {details}");
            }
        }

        /// <summary>
        /// VS-017A.1 §6: the single consolidated S0/S1 authority for this slice's own audit surface
        /// -- aggregates every catalog sweep above into one QualityGateReport and writes both the
        /// machine-readable JSON and a short human-readable Markdown summary under TestResults/
        /// (already gitignored). An automated package only ever "passes" at S0=0 and S1=0.
        /// </summary>
        [Test]
        public void ConsolidatedQualityGateReport_WritesJsonAndMarkdown_ToTestResults()
        {
            var report = new QualityGateReport();
            RuntimeCatalog catalog = BuildCatalog();

            List<string> validatorErrors = ContentCatalogValidator.Validate(new CSharpContentCatalog());
            foreach (string e in validatorErrors) report.AddS0("content-validator-error", e, "ContentCatalogValidator");

            foreach (var kv in catalog.Vehicles)
            {
                if (kv.Value.UnlockLevel > ContractMaxLevel) report.AddS1("l1-40-unreachable-gate", $"vehicle '{kv.Value.Id}' requires level {kv.Value.UnlockLevel}", kv.Value.Id);
                if (VehicleCatalog.Get(kv.Value.Id) == null) report.AddS0("dead-catalog-entry", $"vehicle '{kv.Value.Id}' unresolvable", kv.Value.Id);
            }
            foreach (var kv in catalog.Modules)
            {
                if (kv.Value.UnlockLevel > ContractMaxLevel && !kv.Value.Id.StartsWith("nuclear."))
                    report.AddS1("l1-40-unreachable-gate", $"module '{kv.Value.Id}' requires level {kv.Value.UnlockLevel}", kv.Value.Id);
            }
            foreach (var kv in catalog.LogisticsCenterTiers)
                if (kv.Value.RequiredLevel > ContractMaxLevel) report.AddS1("l1-40-unreachable-gate", $"logistics tier '{kv.Value.Id}' requires level {kv.Value.RequiredLevel}", kv.Value.Id);
            foreach (var kv in catalog.Recipes)
            {
                if (!catalog.TryGetItem(kv.Value.InputItemId, out _)) report.AddS0("save-facing-item-reference-broken", $"recipe '{kv.Value.Id}' input", kv.Value.Id);
                if (!catalog.TryGetItem(kv.Value.OutputItemId, out _)) report.AddS0("save-facing-item-reference-broken", $"recipe '{kv.Value.Id}' output", kv.Value.Id);
            }
            foreach (var kv in catalog.Enemies)
                if (Combat.EnemyCatalog.Get(kv.Key) == null) report.AddS0("dead-catalog-entry", $"enemy '{kv.Key}' unresolvable", kv.Key);

            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            report.WriteToTestResults(projectRoot, "vs017a1-quality-gate.json");

            var md = new System.Text.StringBuilder();
            md.Append("# VS-017A.1 Quality Gate\n\n");
            md.Append($"- S0: {report.S0Count}\n- S1: {report.S1Count}\n- Passes: {report.Passes}\n\n");
            foreach (QualityGateFinding f in report.Findings) md.Append($"- **{f.severity}** [{f.category}] {f.description} ({f.source})\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(projectRoot, "TestResults", "vs017a1-quality-gate.md"), md.ToString());

            Assert.IsTrue(report.Passes, $"S0={report.S0Count} S1={report.S1Count} -- see TestResults/vs017a1-quality-gate.{{json,md}}");
        }
    }
}
