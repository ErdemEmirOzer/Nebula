using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter J -- Hâkimiyet (curriculum steps 154-163).
    /// <para>
    /// Ten steps, all dominance. There is deliberately not one nuclear step:
    /// no nuclear structure type exists, the three <c>nuclear.*</c> modules are
    /// capped at tier 0 and rejected as <c>NotImplemented</c>, nothing produces
    /// <c>energy.nuclear</c> or <c>core.nuclear</c>, and
    /// <c>EconomicValueResolver.HasStarConnection</c> is a stub that always
    /// answers false. A tutorial step for any of that would be teaching a
    /// feature the game does not have.
    /// </para>
    /// <para>
    /// Every state change here is <c>PlanetDominance.Tick(dt, conditions)</c>'s
    /// -- the single authority -- and the conditions handed to it are read from
    /// <c>ExtractionState</c> exactly the way <c>DominanceSystem</c> reads them.
    /// Nothing in this file writes a dominance state.
    /// </para>
    /// </summary>
    public static class ChapterJSteps
    {
        private const TutorialChapterId Chapter = TutorialChapterId.Dominance;

        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return FourConditions();     // 154
            yield return Developing();         // 155
            yield return Controlled();         // 156
            yield return Contested();          // 157
            yield return Lost();               // 158
            yield return Reclaim();            // 159
            yield return SystemDominance();    // 160
            yield return LossFromRaid();       // 161
            yield return RouteUnderContested();// 162
            yield return SupportStructures();  // 163
        }

        // ---- 154 --------------------------------------------------------------

        private static TutorialStepDefinition FourConditions()
        {
            return new TutorialStepDefinition(
                Chapter, "J-01", "Hâkimiyetin Dört Şartı",
                "Bir gezegende dört hâkimiyet şartını da sağla.",
                "Tarama, uygun sondaj, Komuta ve savunma şartlarını birlikte sağla.",
                () =>
                {
                    string worldId = null;
                    return TutorialSteps.Poll(
                        complete: c => worldId != null && TutorialFhij.Conditions(worldId).AllMet,
                        prepare: c =>
                        {
                            worldId = ChapterISteps.FirstPlanetWorldId(c);
                            // Only the scan is installed. The three structures are the
                            // player's own work, so preparing cannot meet the four conditions.
                            EnsureScanned(worldId);
                        },
                        objectives: (c, report) =>
                        {
                            DominanceConditions conditions = TutorialFhij.Conditions(worldId);
                            report.SetObjective(0, conditions.Scanned);
                            report.SetObjective(1, conditions.ActiveDrill);
                            report.SetObjective(2, conditions.ActiveCommand);
                            report.SetObjective(3, conditions.ActiveDefense);
                        },
                        reject: c => worldId != null && !TutorialFhij.Conditions(worldId).AllMet
                            ? "HÂKİMİYET ŞARTLARI EKSİK" : null);
                },
                checklistTr: new[] { "Gezegeni tara", "Uygun aileden aktif bir sondaj kur", "Aktif bir Komuta Merkezi kur", "Aktif bir savunma kur" });
        }

        // ---- 155 --------------------------------------------------------------

        private static TutorialStepDefinition Developing()
        {
            return new TutorialStepDefinition(
                Chapter, "J-02", "Gelişiyor",
                "Şartlar sağlanınca yüz yirmi saniyelik istikrar süresini bekle.",
                "Durumun Gelişiyor'a geçtiğini ve sayacın işlediğini gör.",
                () =>
                {
                    string worldId = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            return dominance != null && dominance.State == DominanceState.Developing
                                   && dominance.StabilizeTimer > 0f;
                        },
                        prepare: c =>
                        {
                            worldId = ChapterISteps.FirstPlanetWorldId(c);
                            EnsureScanned(worldId);
                            // A planet an earlier step already carried past Developing
                            // would make this step's own transition unreachable.
                            RewindToStabilizing(Dominance(worldId));
                        },
                        objectives: (c, report) =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            report.SetObjective(0, dominance != null && dominance.State == DominanceState.Developing);
                            report.SetObjective(1, dominance != null && dominance.StabilizeTimer > 0f);
                        },
                        reject: c =>
                        {
                            if (worldId == null) return null;
                            return TutorialFhij.Conditions(worldId).AllMet
                                ? null : "ŞART KAYBEDİLDİ — İSTİKRAR SAYACI SIFIRLANDI";
                        });
                },
                checklistTr: new[] { "Durumun Gelişiyor'a geçtiğini gör", "Sayacın işlediğini izle", "Şart kaybında sıfırlandığını doğrula" });
        }

        // ---- 156 --------------------------------------------------------------

        private static TutorialStepDefinition Controlled()
        {
            return new TutorialStepDefinition(
                Chapter, "J-03", "Kontrol Altında",
                "Gezegenin Kontrol Altında durumuna geçtiğini gör.",
                "İstikrar süresinin dolmasını ve kontrolün kurulmasını gör.",
                () =>
                {
                    string worldId = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            return dominance != null && dominance.State == DominanceState.Controlled;
                        },
                        prepare: c =>
                        {
                            worldId = ChapterISteps.FirstPlanetWorldId(c);
                            EnsureScanned(worldId);
                            PlanetDominance dominance = Dominance(worldId);
                            if (dominance == null) return;
                            RewindToStabilizing(dominance);
                            // One short tick only: enough to enter Developing when the
                            // conditions already hold, never enough to stabilize.
                            if (dominance.State == DominanceState.Unowned)
                                dominance.Tick(0.01f, TutorialFhij.Conditions(worldId));
                        },
                        objectives: (c, report) =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            report.SetObjective(0, dominance != null && dominance.StabilizeTimer >= PlanetDominance.StabilizeSeconds);
                            report.SetObjective(1, dominance != null && dominance.State == DominanceState.Controlled);
                        },
                        reject: c => worldId != null && !TutorialFhij.Conditions(worldId).AllMet
                            ? "HÂKİMİYET ŞARTLARI EKSİK" : null);
                },
                checklistTr: new[] { "İstikrarın dolmasını bekle", "Durumun Kontrol Altında olduğunu doğrula", "Tecrübe kazanımını gör" });
        }

        // ---- 157 --------------------------------------------------------------

        private static TutorialStepDefinition Contested()
        {
            return new TutorialStepDefinition(
                Chapter, "J-04", "Çekişmeli",
                "Komuta Merkezi devre dışı kalınca gezegenin çekişmeli olduğunu gör.",
                "Üretimin gerçekten durduğunu gör.",
                () =>
                {
                    string worldId = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            return dominance != null && dominance.State == DominanceState.Contested
                                   && dominance.ProductionSuspended;
                        },
                        prepare: c =>
                        {
                            worldId = ChapterISteps.FirstPlanetWorldId(c);
                            EnsureScanned(worldId);
                            DriveToControlled(worldId);
                        },
                        objectives: (c, report) =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            report.SetObjective(0, worldId != null && ExtractionState.CommandDisabled(worldId));
                            report.SetObjective(1, dominance != null && dominance.State == DominanceState.Contested);
                            report.SetObjective(2, dominance != null && dominance.ProductionSuspended);
                        },
                        reject: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            return dominance != null && dominance.State == DominanceState.Contested
                                ? "KOMUTA MERKEZİ DEVRE DIŞI — ÜRETİM DURDURULDU" : null;
                        });
                },
                checklistTr: new[] { "Komuta Merkezini devre dışı bırak", "Durumun Çekişmeli olduğunu gör", "Sondaj üretiminin durduğunu doğrula" });
        }

        // ---- 158 --------------------------------------------------------------

        private static TutorialStepDefinition Lost()
        {
            return new TutorialStepDefinition(
                Chapter, "J-05", "Kaybedildi",
                "Kurtarma penceresi dolarsa gezegenin kaybedildiğini gör.",
                "Kurtarma penceresinin dolmasını izle.",
                () =>
                {
                    string worldId = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            return dominance != null && dominance.State == DominanceState.Lost
                                   && dominance.ProductionSuspended;
                        },
                        prepare: c =>
                        {
                            worldId = ChapterISteps.FirstPlanetWorldId(c);
                            EnsureScanned(worldId);
                            DriveToControlled(worldId);
                        },
                        objectives: (c, report) =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            report.SetObjective(0, dominance != null && dominance.State == DominanceState.Contested);
                            report.SetObjective(1, dominance != null && dominance.State == DominanceState.Lost);
                        },
                        reject: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            return dominance != null && dominance.State == DominanceState.Lost
                                ? "KURTARMA SÜRESİ DOLDU — GEZEGEN KAYBEDİLDİ" : null;
                        });
                },
                checklistTr: new[] { "Çekişmeli durumda bekle", "Durumun Kaybedildi olduğunu doğrula" });
        }

        // ---- 159 --------------------------------------------------------------

        private static TutorialStepDefinition Reclaim()
        {
            return new TutorialStepDefinition(
                Chapter, "J-06", "Geri Al",
                "Kaybedilen veya terk edilen gezegeni geri al.",
                "Geri almanın yeni bir istikrar süresi başlattığını gör.",
                () =>
                {
                    string worldId = null;
                    bool wasAbandoned = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            if (dominance == null) return false;
                            return wasAbandoned && !dominance.Abandoned
                                   && dominance.State == DominanceState.Unowned
                                   && dominance.StabilizeTimer <= 0.0001f;
                        },
                        prepare: c =>
                        {
                            worldId = ChapterISteps.FirstPlanetWorldId(c);
                            EnsureScanned(worldId);
                            PlanetDominance dominance = Dominance(worldId);
                            if (dominance == null) return;
                            // The real abandonment flag the evacuation transaction sets.
                            if (!dominance.Abandoned) dominance.SetAbandoned(true);
                            wasAbandoned = dominance.Abandoned;
                        },
                        objectives: (c, report) =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            report.SetObjective(0, worldId != null && ExtractionState.HasActiveCommand(worldId)
                                                   && ExtractionState.ActiveDefenseCount(worldId) > 0);
                            report.SetObjective(1, dominance != null && !dominance.Abandoned);
                            report.SetObjective(2, dominance != null && dominance.StabilizeTimer <= 0.0001f);
                        },
                        reject: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            if (dominance == null) return null;
                            if (!dominance.Abandoned)
                                return TutorialFhij.RejectText(ReclaimRejectReason.NotAbandoned);
                            bool ready = ExtractionState.HasActiveCommand(worldId)
                                         && ExtractionState.ActiveDefenseCount(worldId) > 0;
                            return ready ? null : TutorialFhij.RejectText(ReclaimRejectReason.DominanceConditionsNotMet);
                        });
                },
                checklistTr: new[] { "Komuta ve savunmayı yeniden aktif et", "GEZEGENİ GERİ AL'a bas", "Yeni istikrar süresinin başladığını gör" });
        }

        // ---- 160 --------------------------------------------------------------

        private static TutorialStepDefinition SystemDominance()
        {
            return new TutorialStepDefinition(
                Chapter, "J-07", "Sistem Hâkimiyeti",
                "Sistemdeki tüm gezegenleri kontrol altına al.",
                "Sistem etiketinin Tam Hâkimiyet olduğunu gör.",
                () =>
                {
                    string systemId = null;
                    return TutorialSteps.Poll(
                        complete: c => systemId != null && OwnershipOf(systemId) == SystemOwnershipTag.DominantCore,
                        prepare: c =>
                        {
                            ShipHangar hangar = TutorialFhij.Hangar(c);
                            systemId = hangar != null ? hangar.CurrentSystemId : null;
                            if (systemId == null)
                            {
                                string worldId = ChapterISteps.FirstPlanetWorldId(c);
                                systemId = worldId != null ? WorldIdCodec.SystemIdFromPlanetWorldId(worldId) : null;
                            }
                        },
                        objectives: (c, report) =>
                        {
                            report.SetObjective(0, systemId != null && ControlledPlanetCount(systemId) > 0);
                            report.SetObjective(1, systemId != null && OwnershipOf(systemId) == SystemOwnershipTag.DominantCore);
                        },
                        reject: c => systemId != null && OwnershipOf(systemId) != SystemOwnershipTag.DominantCore
                            ? "TÜM GEZEGENLER KONTROL ALTINDA DEĞİL" : null);
                },
                checklistTr: new[] { "Her gezegeni Kontrol Altına al", "Sistem etiketinin Tam Hâkimiyet olduğunu gör" });
        }

        // ---- 161 --------------------------------------------------------------

        private static TutorialStepDefinition LossFromRaid()
        {
            return new TutorialStepDefinition(
                Chapter, "J-08", "Baskın Yüzünden Kayıp",
                "Bir baskının hedef gezegeni nasıl kaybettirebildiğini gör.",
                "Baskının hedefi çekişmeliye düşürdüğünü gör.",
                () =>
                {
                    string worldId = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            // Set only by RaidScheduler.TickActive, when the raid's own
                            // target planet is really Contested or Lost.
                            return pressure != null && pressure.WasTargetContestedDuringRaid;
                        },
                        prepare: c =>
                        {
                            worldId = ChapterISteps.FirstPlanetWorldId(c);
                            EnsureScanned(worldId);
                            DriveToControlled(worldId);
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            if (pressure == null || pressure.IsRaidInProgress || worldId == null) return;
                            pressure.ScheduleRaid(RaidStage.Assault, RaidDoctrine.Suppression, worldId,
                                new List<string> { EnemyIds.HeavyAssaultMk1 }, 20240809UL, 1f);
                            ThreatPressureState captured = pressure;
                            c.Scope.OnCleanup(() => captured.ClearToIdle());
                        },
                        objectives: (c, report) =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            report.SetObjective(0, worldId != null && ExtractionState.CommandDisabled(worldId));
                            report.SetObjective(1, dominance != null && dominance.State == DominanceState.Contested);
                            report.SetObjective(2, dominance != null && dominance.State == DominanceState.Lost);
                        },
                        reject: c =>
                        {
                            PlanetDominance dominance = Dominance(worldId);
                            return dominance != null && dominance.State == DominanceState.Lost
                                ? "HEDEF GEZEGEN KAYBEDİLDİ — SAVUNMA BAŞARISIZ" : null;
                        });
                },
                checklistTr: new[] { "Komuta Merkezinin devre dışı kaldığını gör", "Gezegenin Çekişmeli olduğunu izle", "Kaybedildi'ye düştüğünü doğrula" });
        }

        // ---- 162 --------------------------------------------------------------

        private static TutorialStepDefinition RouteUnderContested()
        {
            return new TutorialStepDefinition(
                Chapter, "J-09", "Çekişmeli Kaynakta Rota",
                "Kaynak gezegen çekişmeli veya kaybedilmişken rotanın beklediğini gör.",
                "Rotanın bekleme nedenini gerçek durumda oku.",
                () =>
                {
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            CollectorRoute route = AnyRoute(c);
                            return route != null && (route.WaitReason == RouteWaitReason.SourceDrillContested
                                                     || route.WaitReason == RouteWaitReason.SourceDrillEvacuated);
                        },
                        prepare: c =>
                        {
                            ShipHangar hangar = TutorialFhij.Hangar(c);
                            TutorialFhij.EnsureHangarTier(hangar, 1);
                            TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Collector);
                        },
                        objectives: (c, report) =>
                        {
                            CollectorRoute route = AnyRoute(c);
                            VehicleAgent collector = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(c), VehicleClass.Collector);
                            report.SetObjective(0, route != null);
                            report.SetObjective(2, collector != null && collector.HeldAmount <= 0.0001f);
                        },
                        reject: c =>
                        {
                            CollectorRoute route = AnyRoute(c);
                            if (route == null) return "AKTİF ROTA YOK";
                            return TutorialFhij.RejectText(route.WaitReason);
                        });
                },
                checklistTr: new[] { "Rotanın ToSource fazında olduğunu gör", "Kaynağı Çekişmeli yap", "Collector'ün beklediğini doğrula" });
        }

        // ---- 163 --------------------------------------------------------------

        private static TutorialStepDefinition SupportStructures()
        {
            return new TutorialStepDefinition(
                Chapter, "J-10", "Destek Yapılarının Rolü",
                "Sensör Dizisi ve Bakım Platformunun ne kazandırdığını gör.",
                "İki destek yapısını da aktif hâle getir.",
                () => TutorialSteps.Poll(
                    complete: c =>
                    {
                        SurfaceStructure sensor = ActiveStructure(SurfaceStructureType.SensorArray);
                        SurfaceStructure platform = ActiveStructure(SurfaceStructureType.MaintenancePlatform);
                        return sensor != null && platform != null;
                    },
                    prepare: c => { /* Both structures are the player's own build. Nothing is installed. */ },
                    objectives: (c, report) =>
                    {
                        SurfaceStructure sensor = ActiveStructure(SurfaceStructureType.SensorArray);
                        SurfaceStructure platform = ActiveStructure(SurfaceStructureType.MaintenancePlatform);
                        report.SetObjective(0, sensor != null
                            && WarningResolver.SensorArrayBonusSeconds(true, sensor.Runtime.Tier) > 0f);
                        report.SetObjective(1, platform != null
                            && PlanetaryDefenseCatalog.Get(SurfaceStructureType.MaintenancePlatform,
                                platform.Runtime.Tier).EngineerRepairMultiplier > 1f);
                    },
                    reject: c =>
                    {
                        SurfaceStructure sensor = ActiveStructure(SurfaceStructureType.SensorArray);
                        SurfaceStructure platform = ActiveStructure(SurfaceStructureType.MaintenancePlatform);
                        return sensor == null || platform == null ? "DESTEK YAPISI AKTİF DEĞİL" : null;
                    }),
                checklistTr: new[] { "Sensör Dizisi kur ve uyarı bonusunu oku", "Bakım Platformu kur ve onarım hızını gör" });
        }

        // ---- readings ---------------------------------------------------------

        private static PlanetDominance Dominance(string worldId) =>
            string.IsNullOrEmpty(worldId) ? null : ExtractionState.GetDominance(worldId);

        /// <summary>Scans through the real authority when the planet is materialized; a planet with no live identity is left alone rather than faked.</summary>
        private static void EnsureScanned(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return;
            PlanetIdentity planet = ExtractionState.FindPlanet(worldId);
            if (planet != null) { ExtractionState.CompletePlanetScan(planet); return; }
            try { ExtractionState.MarkScanned(worldId); }
            catch (InvalidOperationException) { /* no manifest for this id -- nothing to scan */ }
        }

        /// <summary>
        /// Puts a planet back at the start of its stabilize window when an earlier
        /// step already carried it past Developing.
        /// <para>
        /// This is precondition installation, not a second dominance authority: it
        /// uses <see cref="PlanetDominance.Restore"/>, the same seam a loaded save
        /// uses, and it can only ever set <c>Developing</c> with both timers at zero
        /// -- never <c>Controlled</c>, which is what these steps are asking the
        /// player to earn. Without it, a step teaching the stabilize transition would
        /// be handed its own success by whichever step ran before it.
        /// </para>
        /// </summary>
        private static void RewindToStabilizing(PlanetDominance dominance)
        {
            if (dominance == null) return;
            if (dominance.State == DominanceState.Unowned || dominance.State == DominanceState.Developing) return;
            dominance.Restore(DominanceState.Developing, 0f, 0f, dominance.Abandoned);
        }

        /// <summary>
        /// Runs the real stabilize clock forward for a step whose lesson starts
        /// <i>after</i> control was established. Uses the one authority,
        /// <see cref="PlanetDominance.Tick"/>, with the real conditions -- and does
        /// nothing at all when the four conditions are not actually met.
        /// </summary>
        private static void DriveToControlled(string worldId)
        {
            PlanetDominance dominance = Dominance(worldId);
            if (dominance == null) return;
            DominanceConditions conditions = TutorialFhij.Conditions(worldId);
            if (!conditions.AllMet) return;
            int guard = 0;
            while (dominance.State != DominanceState.Controlled && guard++ < 200)
                dominance.Tick(PlanetDominance.StabilizeSeconds / 100f, conditions);
        }

        private static SurfaceStructure ActiveStructure(SurfaceStructureType type)
        {
            IReadOnlyList<SurfaceStructure> all = ExtractionState.Structures;
            for (int i = 0; i < all.Count; i++)
            {
                SurfaceStructure s = all[i];
                if (s != null && s.Type == type && s.Runtime != null && s.Runtime.IsActive) return s;
            }
            return null;
        }

        private static int ControlledPlanetCount(string systemId)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return 0;
            int controlled = 0;
            for (int i = 0; i < manifest.Planets.Count; i++)
                if (ExtractionState.GetDominance(manifest.Planets[i].PlanetId).State == DominanceState.Controlled)
                    controlled++;
            return controlled;
        }

        /// <summary>The real ownership decision, fed real counts -- the same pure resolver <c>SystemStatusQuery.Ownership</c> uses.</summary>
        private static SystemOwnershipTag OwnershipOf(string systemId)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(systemId);
            if (manifest == null) return SystemOwnershipTag.Undiscovered;
            return SystemOwnershipResolver.Resolve(
                visited: true,
                totalRealPlanets: manifest.Planets.Count,
                controlledPlanets: ControlledPlanetCount(systemId),
                hasActiveLogisticsCenter: SystemStatusQuery.HasActiveLogisticsCenter(systemId),
                hasWorkingCollectorRoute: SystemStatusQuery.HasWorkingCollectorRoute(systemId),
                hasActiveDefense: SystemStatusQuery.HasActiveDefenseAnywhere(systemId));
        }

        private static CollectorRoute AnyRoute(TutorialStepContext context)
        {
            VehicleAgent collector = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(context), VehicleClass.Collector,
                v => v.Route != null);
            if (collector != null) return collector.Route;
            IReadOnlyList<CollectorRoute> routes = LogisticsState.Routes;
            return routes.Count > 0 ? routes[0] : null;
        }
    }
}
