using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Ship;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter I -- Düşman Rolleri, Savunma ve Baskınlar (curriculum steps 134-153).
    /// <para>
    /// Two production facts shape the whole chapter. First, planetary defence
    /// firing has no callable seam: <c>SurfaceStructure.TickDefenseCombat</c>,
    /// <c>FireLaserAtTarget</c> and <c>FirePlasmaAtTarget</c> are private and
    /// reachable only from <c>Update</c>, so the defence steps are validated on
    /// what those methods really do -- damage on the enemy's own
    /// <c>Durability</c>, and a real projectile registered with the encounter
    /// director. Second, an auto-scheduled raid needs tens of active minutes of
    /// accumulated pressure, so a raid step's prerequisite is installed by
    /// calling <c>ThreatPressureState.ScheduleRaid</c> directly. That is setup,
    /// never the player's action: every raid step still completes on a real
    /// phase transition <c>RaidScheduler.Tick</c> performed.
    /// </para>
    /// <para>
    /// The defence-mode step teaches the behaviour the game actually has: the
    /// mode and focus keys act on the nearest Command planet, not on the active
    /// raid's target planet.
    /// </para>
    /// </summary>
    public static class ChapterISteps
    {
        private const TutorialChapterId Chapter = TutorialChapterId.Raids;

        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return Probe();              // 134
            yield return Fighter();            // 135
            yield return Looter();             // 136
            yield return Disruptor();          // 137
            yield return Heavy();              // 138
            yield return SiegeTelegraph();     // 139
            yield return RaidWarning();        // 140
            yield return Pressure();           // 141
            yield return WarningDuration();    // 142
            yield return RaidPhases();         // 143
            yield return Retreat();            // 144
            yield return RaidResult();         // 145
            yield return RaidSalvage();        // 146
            yield return DefenseCoordinator(); // 147
            yield return DefenseModes();       // 148
            yield return SharedTarget();       // 149
            yield return LaserDefense();       // 150
            yield return PlasmaDefense();      // 151
            yield return PirateBase();         // 152
            yield return EscortDefense();      // 153
        }

        // ---- 134 --------------------------------------------------------------

        private static TutorialStepDefinition Probe()
        {
            return new TutorialStepDefinition(
                Chapter, "I-01", "Keşif Sondası",
                "Keşif sondasını tanı: silahsızdır, tarar ve kaçar.",
                "Sondanın taramasını bitirdiğini ya da yok edildiğini gör.",
                () =>
                {
                    string probeId = null;
                    float shipBaseline = 0f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            if (probeId == null) return false;
                            EnemyAgent probe = EnemyAgent.Find(probeId);
                            if (probe == null || probe.Destroyed) return true;   // resolved by destruction
                            return probe.Ai != null && probe.Ai.ScanComplete;    // resolved by a finished scan
                        },
                        prepare: c =>
                        {
                            EnemyAgent probe = SpawnTrainingEnemy(c, EnemyIds.ScoutProbeMk1, new Vector3(0f, 0f, 1200f));
                            probeId = probe != null ? probe.Id : null;
                            ShipCombat combat = TutorialFhij.Combat(c);
                            shipBaseline = combat?.State != null ? TutorialFhij.DurabilityTotal(combat.State.Durability) : 0f;
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent probe = probeId != null ? EnemyAgent.Find(probeId) : null;
                            ShipCombat combat = TutorialFhij.Combat(c);
                            report.SetObjective(0, probe != null);
                            report.SetObjective(1, combat?.State != null
                                && TutorialFhij.DurabilityTotal(combat.State.Durability) >= shipBaseline - 0.001f);
                            report.SetObjective(2, probe != null && probe.Ai != null && probe.Ai.ScanComplete);
                        },
                        reject: c =>
                        {
                            EnemyAgent probe = probeId != null ? EnemyAgent.Find(probeId) : null;
                            return probe != null && probe.Def != null && !probe.Def.IsCombatCapable
                                ? "BU BİRİM SİLAHSIZ — SADECE TARAR" : null;
                        });
                },
                checklistTr: new[] { "Sondayı hedefle", "Hiç hasar vermediğini gör", "Taramasını bitirdiğini izle" });
        }

        // ---- 135 --------------------------------------------------------------

        private static TutorialStepDefinition Fighter()
        {
            return new TutorialStepDefinition(
                Chapter, "I-02", "Avcı",
                "Avcının saldırı bandını ve geri çekilme eşiğini gözle.",
                "Avcının gerçek hasarını ve geri çekilişini gör.",
                () =>
                {
                    string fighterId = null;
                    float shipBaseline = -1f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent fighter = fighterId != null ? EnemyAgent.Find(fighterId) : null;
                            if (fighter != null && fighter.Ai != null && fighter.Ai.Phase == EnemyAiPhase.Retreat) return true;
                            ShipCombat combat = TutorialFhij.Combat(c);
                            if (combat?.State == null || shipBaseline < 0f) return false;
                            return TutorialFhij.DurabilityTotal(combat.State.Durability) < shipBaseline - 0.001f;
                        },
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            EnemyAgent fighter = SpawnTrainingEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 750f));
                            fighterId = fighter != null ? fighter.Id : null;
                            ShipCombat combat = TutorialFhij.Combat(c);
                            shipBaseline = combat?.State != null ? TutorialFhij.DurabilityTotal(combat.State.Durability) : -1f;
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent fighter = fighterId != null ? EnemyAgent.Find(fighterId) : null;
                            report.SetObjective(0, fighter != null);
                            report.SetObjective(1, fighter != null && fighter.Ai != null
                                                   && fighter.Ai.Phase == EnemyAiPhase.Retreat);
                        },
                        reject: c => fighterId != null && EnemyAgent.Find(fighterId) == null
                            ? "AVCI MENZİL DIŞINDA" : null);
                },
                checklistTr: new[] { "Avcının bandda tutunduğunu gör", "Hasar aldıkça geri çekildiğini izle" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 136 --------------------------------------------------------------

        private static TutorialStepDefinition Looter()
        {
            return new TutorialStepDefinition(
                Chapter, "I-03", "Yağmacı ve Kaynak Hırsızlığı",
                "Yağmacının sondajından gerçekten kaynak çaldığını gör.",
                "Sondaj stoğunun düştüğünü ve yağmacının dolduğunu gör.",
                () =>
                {
                    string looterId = null;
                    float cargoBaseline = 0f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent looter = looterId != null ? EnemyAgent.Find(looterId) : null;
                            if (looter?.LooterCargo == null) return false;
                            return looter.LooterCargo.UsedVolume > cargoBaseline + 0.0001f;
                        },
                        prepare: c =>
                        {
                            EnemyAgent looter = SpawnTrainingEnemy(c, EnemyIds.LooterTransportMk1, new Vector3(0f, 0f, 900f));
                            looterId = looter != null ? looter.Id : null;
                            cargoBaseline = looter?.LooterCargo != null ? looter.LooterCargo.UsedVolume : 0f;
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent looter = looterId != null ? EnemyAgent.Find(looterId) : null;
                            DrillStructure drill = TutorialFhij.AnyDrill();
                            report.SetObjective(0, looter != null);
                            report.SetObjective(1, drill != null && drill.Runtime.StoredAmount > 0f);
                            report.SetObjective(2, looter?.LooterCargo != null && looter.LooterCargo.UsedVolume > 0f);
                        },
                        reject: c =>
                        {
                            DrillStructure drill = TutorialFhij.AnyDrill();
                            if (drill == null || drill.Runtime.StoredAmount <= 0f) return "ÇALINACAK KAYNAK KALMADI";
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            return pressure != null && pressure.AnyResourceStolen ? null : null;
                        });
                },
                checklistTr: new[] { "Yağmacının hedefini gör", "Sondaj stoğunun azaldığını izle", "Kargosunun dolduğunu doğrula" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 137 --------------------------------------------------------------

        private static TutorialStepDefinition Disruptor()
        {
            return new TutorialStepDefinition(
                Chapter, "I-04", "Bozucu ve Bozulma Bağışıklığı",
                "Bozucunun telgrafını gör, bozulmayı yaşa ve bağışıklık süresini öğren.",
                "Geminin gerçekten bozulduğunu gör.",
                () =>
                {
                    bool sawDisrupt = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            ShipCombatState state = ShipState(c);
                            if (state != null && state.IsDisrupted) sawDisrupt = true;
                            return sawDisrupt;
                        },
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            SpawnTrainingEnemy(c, EnemyIds.EmDisruptorMk1, new Vector3(0f, 0f, 1500f));
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent disruptor = TutorialFhij.FirstEnemy(EnemyIds.EmDisruptorMk1);
                            ShipCombatState state = ShipState(c);
                            report.SetObjective(0, disruptor != null && disruptor.IsTelegraphing);
                            report.SetObjective(1, state != null && state.IsDisrupted);
                            report.SetObjective(2, sawDisrupt && state != null && !state.IsDisrupted);
                        },
                        reject: c =>
                        {
                            ShipCombatState state = ShipState(c);
                            return sawDisrupt && state != null && !state.IsDisrupted ? "EM BAĞIŞIKLIĞI AKTİF" : null;
                        });
                },
                checklistTr: new[] { "Telgraf sayacını gör", "Bozulmanın başladığını fark et", "İkinci bozulmanın tutmadığını doğrula" });
        }

        // ---- 138 --------------------------------------------------------------

        private static TutorialStepDefinition Heavy()
        {
            return new TutorialStepDefinition(
                Chapter, "I-05", "Ağır Saldırı",
                "Ağır birimin önce savunma yapılarını hedeflediğini gör.",
                "Ağır birimin yapı hedefini gerçek durumda gör.",
                () =>
                {
                    string heavyId = null;
                    string structureTargetId = null;
                    float structureBaseline = -1f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent heavy = heavyId != null ? EnemyAgent.Find(heavyId) : null;
                            if (heavy != null && heavy.StructureTarget != null) return true;
                            Durability durability = StructureDurability(structureTargetId);
                            return durability != null && structureBaseline >= 0f
                                   && TutorialFhij.DurabilityTotal(durability) < structureBaseline - 0.001f;
                        },
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            SurfaceStructure defense = TutorialFhij.AnyStructure(SurfaceStructureType.Laser);
                            structureTargetId = defense != null ? defense.TargetId : null;
                            structureBaseline = defense?.Runtime?.Durability != null
                                ? TutorialFhij.DurabilityTotal(defense.Runtime.Durability) : -1f;
                            EnemyAgent heavy = SpawnTrainingEnemy(c, EnemyIds.HeavyAssaultMk1, new Vector3(0f, 0f, 1400f));
                            heavyId = heavy != null ? heavy.Id : null;
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent heavy = heavyId != null ? EnemyAgent.Find(heavyId) : null;
                            report.SetObjective(0, heavy != null);
                            report.SetObjective(1, heavy != null && heavy.StructureTarget != null);
                        },
                        reject: c => structureTargetId == null ? "ÖNCELİKLİ YAPI MENZİLDE YOK" : null);
                },
                checklistTr: new[] { "Ağır birimi tanı", "Savunma yapısına yöneldiğini gör", "Yapının dayanıklılığının düştüğünü doğrula" });
        }

        // ---- 139 --------------------------------------------------------------

        private static TutorialStepDefinition SiegeTelegraph()
        {
            return new TutorialStepDefinition(
                Chapter, "I-06", "Kuşatma ve Telgraf",
                "Kuşatma biriminin üç saniyelik telgrafını gör ve ağır mermisini izle.",
                "Telgrafın sonunda gerçek merminin doğduğunu gör.",
                () =>
                {
                    int baseline = -1;
                    return TutorialSteps.Poll(
                        // A NEW siege round, never merely "one exists": a live session
                        // can already have hostile siege rounds in flight -- an earlier
                        // step spawns one, and a real Siege unit fires on its own cadence
                        // -- and completing on those would credit the player for a shot
                        // that was already in the air when the step opened.
                        complete: c => baseline >= 0 && SiegeRoundCount(c) > baseline,
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            EnemyAgent siege = SpawnTrainingEnemy(c, EnemyIds.SiegeMk1, new Vector3(0f, 0f, 3000f));
                            // The real telegraph restore seam, wound to the catalog value --
                            // the shot itself is what the player waits for, and is never armed here.
                            if (siege != null) siege.RestoreTelegraph(EnemyCatalog.SiegeMk1.TelegraphSeconds);
                            baseline = SiegeRoundCount(c);
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent siege = TutorialFhij.FirstEnemy(EnemyIds.SiegeMk1);
                            report.SetObjective(0, siege != null && siege.IsTelegraphing);
                            report.SetObjective(1, baseline >= 0 && SiegeRoundCount(c) > baseline);
                        },
                        reject: c => TutorialFhij.FirstEnemy(EnemyIds.SiegeMk1) == null
                            ? "HEDEF KAÇTI, ATIŞ İPTAL" : null);
                },
                checklistTr: new[] { "Telgraf sayacını gör", "Merminin doğduğunu izle", "Merminin kesilebildiğini doğrula" });
        }

        // ---- 140 --------------------------------------------------------------

        private static TutorialStepDefinition RaidWarning()
        {
            return new TutorialStepDefinition(
                Chapter, "I-07", "Baskın Uyarısı",
                "Baskın uyarısını oku ve hazırlanmak için kalan süreyi gör.",
                "Uyarı fazına gerçekten geçildiğini gör.",
                () => TutorialSteps.Poll(
                    // Real state, and honest evidence that there is something to
                    // read -- but not proof that anyone read it. Reaching Warning is
                    // RaidScheduler.Tick's own transition and no command the player
                    // has can cause it, delay it or bring it forward, so the step
                    // ends on the player's own confirmation (see the kind below).
                    complete: c =>
                    {
                        ThreatPressureState pressure = TutorialFhij.Pressure(c);
                        return pressure != null && pressure.Phase == RaidPhase.Warning
                               && pressure.WarningRemainingSeconds > 0f;
                    },
                    prepare: c =>
                    {
                        // Leaves the raid at Scheduled, which is as far as Prepare
                        // may go. The scheduler walks it to Warning on its own.
                        ScheduleTeachingRaid(c);
                    },
                    objectives: (c, report) =>
                    {
                        ThreatPressureState pressure = TutorialFhij.Pressure(c);
                        report.SetObjective(0, pressure != null && pressure.Phase == RaidPhase.Warning);
                        report.SetObjective(1, pressure != null && !string.IsNullOrEmpty(pressure.TargetPlanetWorldId));
                        report.SetObjective(2, pressure != null && pressure.WarningRemainingSeconds > 0f);
                    },
                    reject: c =>
                    {
                        ThreatPressureState pressure = TutorialFhij.Pressure(c);
                        return pressure == null || pressure.Phase == RaidPhase.None ? "AKTİF BASKIN YOK" : null;
                    }),
                checklistTr: new[] { "Uyarı bandını gör", "Hedef gezegeni oku", "Geri sayımın işlediğini doğrula" },
                // A reading, and the only one in this chapter. The raid clock is
                // entirely the scheduler's: there is no key, no control and no
                // gameplay state the player can move that brings the warning on,
                // holds it off, or changes what it says. Prepare stops at Scheduled
                // -- the earliest honest point -- and the world walks it to Warning
                // by itself a second or two later, so with no confirmation the step
                // completed whether or not anyone had looked at the band.
                kind: TutorialStepKind.InformationAcknowledgement);
        }

        // ---- 141 --------------------------------------------------------------

        private static TutorialStepDefinition Pressure()
        {
            return new TutorialStepDefinition(
                Chapter, "I-08", "Tehdit Baskısı",
                "Kontrol ettiğin gezegenlerin baskıyı nasıl yükselttiğini gör.",
                "Baskının gerçekten yükseldiğini gör.",
                () =>
                {
                    float baseline = -1f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            return pressure != null && baseline >= 0f && pressure.Pressure > baseline + 0.0001f;
                        },
                        prepare: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            if (pressure == null) return;
                            pressure.ResetPressure();
                            baseline = pressure.Pressure;
                        },
                        reject: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            return pressure != null && pressure.InGrace ? "KORUMA SÜRESİ AKTİF" : null;
                        });
                },
                checklistTr: new[] { "Baskı değerini oku", "Zamanla arttığını gör", "Koruma süresinde artmadığını doğrula" });
        }

        // ---- 142 --------------------------------------------------------------

        private static TutorialStepDefinition WarningDuration()
        {
            return new TutorialStepDefinition(
                Chapter, "I-09", "Uyarı Süresi",
                "Scout ve Sensör Dizisinin uyarı süresini nasıl uzattığını gör.",
                "Sistemde gerçek bir Scout tutarak bonusu kazan.",
                () =>
                {
                    var alreadyCounted = new HashSet<string>(StringComparer.Ordinal);
                    return TutorialSteps.Poll(
                        // A Scout that was ALREADY earning the bonus when the step
                        // opened is not this step's lesson: an earlier chapter F step
                        // launches one and it is still on station here, so completing on
                        // "some Scout counts" would credit a launch the player did not make.
                        complete: c => NewlyCountingScout(c, alreadyCounted) != null,
                        prepare: c =>
                        {
                            // A Scout is built and left parked -- a hangar-parked Scout is
                            // deliberately worth no bonus -- and every Scout that already
                            // counts is recorded, so only a fresh launch can satisfy this.
                            TutorialFhij.EnsureHangarTier(TutorialFhij.Hangar(c), 1);
                            TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Scout);
                            alreadyCounted.Clear();
                            foreach (VehicleAgent scout in CountingScouts(c)) alreadyCounted.Add(scout.VehicleId);
                        },
                        objectives: (c, report) =>
                        {
                            bool earned = NewlyCountingScout(c, alreadyCounted) != null;
                            report.SetObjective(0, true);
                            report.SetObjective(1, earned);
                            report.SetObjective(2, earned);
                        },
                        reject: c => NewlyCountingScout(c, alreadyCounted) != null
                            ? null : "SCOUT FIRLATILMAMIŞ, BONUS YOK");
                },
                checklistTr: new[] { "Temel süreyi oku", "Sistemde fırlatılmış bir Scout tut", "Sürenin arttığını doğrula" });
        }

        // ---- 143 --------------------------------------------------------------

        private static TutorialStepDefinition RaidPhases()
        {
            return new TutorialStepDefinition(
                Chapter, "I-10", "Baskın Aşamaları",
                "Baskının aşamalarını sırayla takip et.",
                "Uyarıdan doğuşa kadar gerçek faz zincirini izle.",
                () =>
                {
                    bool sawSpawning = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            if (pressure == null) return false;
                            if (pressure.Phase == RaidPhase.Spawning || pressure.Phase == RaidPhase.Active
                                || pressure.Phase == RaidPhase.RetreatCleanup || pressure.Phase == RaidPhase.Resolved)
                                sawSpawning = true;
                            return sawSpawning;
                        },
                        prepare: c => ScheduleTeachingRaid(c),
                        objectives: (c, report) =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            report.SetObjective(0, pressure != null && pressure.Phase == RaidPhase.Warning);
                            report.SetObjective(1, pressure != null && pressure.SpawnedEnemyIds.Count > 0);
                            report.SetObjective(2, pressure != null && pressure.Phase == RaidPhase.Active);
                        },
                        reject: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            return pressure != null && string.IsNullOrEmpty(pressure.TargetPlanetWorldId)
                                ? "BASKIN HEDEFİ KAYBOLDU" : null;
                        });
                },
                checklistTr: new[] { "Uyarıyı gör", "Düşmanların doğduğunu gör", "Aktif çatışmayı izle" });
        }

        // ---- 144 --------------------------------------------------------------

        private static TutorialStepDefinition Retreat()
        {
            return new TutorialStepDefinition(
                Chapter, "I-11", "Geri Çekilme",
                "Yenilen düşmanların geri çekilip sistemden kaçtığını gör.",
                "Düşmanın gerçekten kaçış fazına geçtiğini gör.",
                () =>
                {
                    string enemyId = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            if (enemyId == null) return false;
                            EnemyAgent enemy = EnemyAgent.Find(enemyId);
                            if (enemy == null) return true;          // escaped and de-registered
                            return enemy.Ai != null && (enemy.Ai.Phase == EnemyAiPhase.Retreat
                                                        || enemy.Ai.Phase == EnemyAiPhase.Escaped);
                        },
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            EnemyAgent enemy = SpawnTrainingEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 700f));
                            enemyId = enemy != null ? enemy.Id : null;
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            report.SetObjective(0, enemy != null && enemy.Ai != null && enemy.Ai.Phase == EnemyAiPhase.Retreat);
                            report.SetObjective(2, enemy == null);
                        },
                        reject: c =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            if (enemy?.Combat == null || enemy.Def == null) return null;
                            float hullFraction = enemy.Combat.HullMax > 0f ? enemy.Combat.Hull / enemy.Combat.HullMax : 1f;
                            return hullFraction > enemy.Def.RetreatHullFraction ? "DÜŞMAN HÂLÂ MENZİLDE" : null;
                        });
                },
                checklistTr: new[] { "Retreat fazına geçtiğini gör", "Uzaklaştığını izle", "Kaydın silindiğini doğrula" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 145 --------------------------------------------------------------

        private static TutorialStepDefinition RaidResult()
        {
            return new TutorialStepDefinition(
                Chapter, "I-12", "Baskın Sonucu",
                "Baskının kesin zafer, pahalı zafer veya başarısızlık olarak nasıl değerlendirildiğini gör.",
                "Baskının gerçekten sonuçlandığını gör.",
                () =>
                {
                    bool resolved = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            if (pressure == null) return resolved;
                            if (pressure.Phase == RaidPhase.Resolved || pressure.ResultApplied) resolved = true;
                            return resolved;
                        },
                        prepare: c => ScheduleTeachingRaid(c),
                        objectives: (c, report) =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            if (pressure == null) return;
                            RaidOutcome outcome = RaidOutcomeResolver.Resolve(false, pressure.PlayerWasShutdownDuringRaid,
                                pressure.WasTargetContestedDuringRaid, pressure.AnyResourceStolen);
                            report.SetObjective(0, resolved);
                            report.SetObjective(1, outcome != RaidOutcome.Failure);
                            report.SetObjective(2, pressure.HasMeaningfulParticipation);
                        },
                        reject: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            return pressure != null && !pressure.HasMeaningfulParticipation
                                ? "KATKI YOK — TECRÜBE VERİLMEDİ" : null;
                        });
                },
                checklistTr: new[] { "Sonucu oku", "Hedef gezegenin durumunu doğrula", "Kazanılan tecrübeyi gör" });
        }

        // ---- 146 --------------------------------------------------------------

        private static TutorialStepDefinition RaidSalvage()
        {
            return new TutorialStepDefinition(
                Chapter, "I-13", "Baskın Ganimeti",
                "Kazanılan baskının bıraktığı ganimeti topla.",
                "Baskın enkazının gerçekten oluştuğunu gör.",
                () =>
                {
                    int baseline = -1;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            CombatEncounterDirector director = TutorialFhij.Director(c);
                            return director != null && baseline >= 0 && director.Wreckage.Count > baseline;
                        },
                        prepare: c =>
                        {
                            ScheduleTeachingRaid(c);
                            CombatEncounterDirector director = TutorialFhij.Director(c);
                            baseline = director != null ? director.Wreckage.Count : -1;
                        },
                        objectives: (c, report) =>
                        {
                            CombatEncounterDirector director = TutorialFhij.Director(c);
                            report.SetObjective(0, director != null && baseline >= 0 && director.Wreckage.Count > baseline);
                        },
                        reject: c =>
                        {
                            ThreatPressureState pressure = TutorialFhij.Pressure(c);
                            return pressure != null && pressure.SalvageApplied
                                ? "KAYBEDİLEN SAVUNMA GANİMET VERMEZ" : null;
                        });
                },
                checklistTr: new[] { "Enkazın oluştuğunu gör", "İçindeki kalemleri oku", "Kaynağı çek" });
        }

        // ---- 147 --------------------------------------------------------------

        private static TutorialStepDefinition DefenseCoordinator()
        {
            return new TutorialStepDefinition(
                Chapter, "I-14", "Gezegen Savunma Koordinatörü",
                "Komuta Merkezi aktifken savunmaların tek merkezden yönetildiğini gör.",
                "Aktif Komuta ile savunmanın koordine olduğunu gör.",
                () =>
                {
                    string worldId = null;
                    return TutorialSteps.Poll(
                        complete: c => worldId != null
                                       && ExtractionState.HasActiveCommand(worldId)
                                       && !ExtractionState.CommandDisabled(worldId)
                                       && ExtractionState.ActiveDefenseCount(worldId) > 0,
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            SurfaceStructure defense = TutorialFhij.AnyStructure(SurfaceStructureType.Laser);
                            worldId = defense != null ? defense.WorldId : FirstPlanetWorldId(c);
                            SpawnTrainingEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 600f));
                        },
                        objectives: (c, report) =>
                        {
                            report.SetObjective(0, worldId != null && ExtractionState.HasActiveCommand(worldId));
                            report.SetObjective(1, worldId != null && ExtractionState.ActiveDefenseCount(worldId) > 0);
                            report.SetObjective(2, worldId != null && ExtractionState.CommandDisabled(worldId));
                        },
                        reject: c =>
                        {
                            if (worldId == null) return "MENZİLDE HEDEF YOK";
                            if (!ExtractionState.HasActiveCommand(worldId)) return "KOMUTA DEVRE DIŞI — KOORDİNASYON YOK";
                            return null;
                        });
                },
                checklistTr: new[] { "Aktif Komuta Merkezini doğrula", "Savunmanın aynı hedefi seçtiğini gör", "Komuta devre dışıyken dağıldığını gör" });
        }

        // ---- 148 --------------------------------------------------------------

        private static TutorialStepDefinition DefenseModes()
        {
            return new TutorialStepDefinition(
                Chapter, "I-15", "Öncelik, Dengeli, Odak",
                "Savunma modunu Öncelik, Dengeli ve Odak arasında değiştir.",
                "Modun gerçekten değiştiğini gör. Tuş, en yakın Komuta gezegenine etki eder.",
                () =>
                {
                    string worldId = null;
                    DefenseMode baseline = DefenseMode.RolePriority;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            PlanetaryDefenseCoordinator coordinator = TutorialFhij.Defense(c);
                            return coordinator != null && worldId != null && coordinator.GetMode(worldId) != baseline;
                        },
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            SurfaceStructure command = TutorialFhij.AnyStructure(SurfaceStructureType.Command);
                            worldId = command != null ? command.WorldId : FirstPlanetWorldId(c);
                            PlanetaryDefenseCoordinator coordinator = TutorialFhij.Defense(c);
                            baseline = coordinator != null && worldId != null
                                ? coordinator.GetMode(worldId) : DefenseMode.RolePriority;
                            SpawnTrainingEnemy(c, EnemyIds.LooterTransportMk1, new Vector3(0f, 0f, 600f));
                            SpawnTrainingEnemy(c, EnemyIds.SiegeMk1, new Vector3(200f, 0f, 700f));
                        },
                        reject: c =>
                        {
                            PlanetaryDefenseCoordinator coordinator = TutorialFhij.Defense(c);
                            if (coordinator == null || worldId == null) return "MENZİLDE HEDEF YOK";
                            if (coordinator.GetMode(worldId) == DefenseMode.FocusTarget
                                && string.IsNullOrEmpty(coordinator.GetFocus(worldId)))
                                return "GEÇERLİ ODAK YOK, ÖNCELİĞE DÜŞÜLDÜ";
                            return null;
                        });
                },
                checklistTr: new[] { "Modu değiştir", "Seçilen hedefin değiştiğini gör" },
                keyHint: TutorialFhij.KeyHint(CombatAction.CycleDefenseMode));
        }

        // ---- 149 --------------------------------------------------------------

        private static TutorialStepDefinition SharedTarget()
        {
            return new TutorialStepDefinition(
                Chapter, "I-16", "Ortak Hedef",
                "Aynı gezegendeki tüm savunmaların aynı hedefe ateş ettiğini gör.",
                "Aynı gezegende ikinci bir savunma kur ve paylaşımı gör.",
                () =>
                {
                    string worldId = null;
                    return TutorialSteps.Poll(
                        complete: c => worldId != null
                                       && ExtractionState.ActiveDefenseCount(worldId) >= 2
                                       && ExtractionState.HasActiveCommand(worldId),
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            SurfaceStructure defense = TutorialFhij.AnyStructure(SurfaceStructureType.Laser);
                            worldId = defense != null ? defense.WorldId : FirstPlanetWorldId(c);
                            SpawnTrainingEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 600f));
                            SpawnTrainingEnemy(c, EnemyIds.LooterTransportMk1, new Vector3(150f, 0f, 620f));
                        },
                        objectives: (c, report) =>
                        {
                            report.SetObjective(0, worldId != null && ExtractionState.ActiveDefenseCount(worldId) >= 2);
                            report.SetObjective(1, worldId != null && ExtractionState.HasActiveCommand(worldId));
                        },
                        reject: c => worldId != null && !ExtractionState.HasActiveCommand(worldId)
                            ? "KOMUTA DEVRE DIŞI — KOORDİNASYON YOK" : null);
                },
                checklistTr: new[] { "İki savunma yapısı kur", "İkisinin de aynı düşmanı seçtiğini doğrula" });
        }

        // ---- 150 --------------------------------------------------------------

        private static TutorialStepDefinition LaserDefense() => DefenseDamageStep(
            "I-17", "Lazer Savunması",
            "Gezegen lazer savunmasının anında isabet ettiğini gör.",
            "Düşmanın gerçekten hasar aldığını gör.",
            SurfaceStructureType.Laser,
            new[] { "Lazer savunmasını kur", "Düşmanın menzile girmesini bekle", "Düşmanın hasar aldığını doğrula" });

        // ---- 151 --------------------------------------------------------------

        private static TutorialStepDefinition PlasmaDefense()
        {
            return new TutorialStepDefinition(
                Chapter, "I-18", "Plazma Savunması",
                "Gezegen plazma savunmasının mermiyle ve serpintiyle vurduğunu gör.",
                "Savunmanın gerçek mermisini gör.",
                () =>
                {
                    int baseline = -1;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            CombatEncounterDirector director = TutorialFhij.Director(c);
                            if (director == null || baseline < 0) return false;
                            return CountProjectiles(director, ProjectileKind.Plasma, ProjectileFaction.Player) > baseline;
                        },
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            CombatEncounterDirector director = TutorialFhij.Director(c);
                            baseline = director != null
                                ? CountProjectiles(director, ProjectileKind.Plasma, ProjectileFaction.Player) : -1;
                            SpawnTrainingEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 1200f));
                            SpawnTrainingEnemy(c, EnemyIds.FighterMk1, new Vector3(90f, 0f, 1200f));
                        },
                        objectives: (c, report) =>
                        {
                            report.SetObjective(0, TutorialFhij.AnyStructure(SurfaceStructureType.Plasma) != null);
                            CombatEncounterDirector director = TutorialFhij.Director(c);
                            report.SetObjective(1, director != null && baseline >= 0
                                && CountProjectiles(director, ProjectileKind.Plasma, ProjectileFaction.Player) > baseline);
                        },
                        reject: c => TutorialFhij.AnyStructure(SurfaceStructureType.Plasma) == null
                            ? "PLAZMA NAMLUSU HAZIR DEĞİL" : null);
                },
                checklistTr: new[] { "Plazma savunmasını kur", "Merminin doğduğunu gör", "Serpinti hasarını doğrula" });
        }

        // ---- 152 --------------------------------------------------------------

        private static TutorialStepDefinition PirateBase()
        {
            return new TutorialStepDefinition(
                Chapter, "I-19", "Korunaklı Korsan Üssü",
                "Korsan üssünün taretini ve garnizonunu karşıla.",
                "Üssün gerçekten etkisiz hâle geldiğini gör.",
                () => TutorialSteps.Poll(
                    complete: c =>
                    {
                        CombatEncounterDirector director = TutorialFhij.Director(c);
                        return director?.PirateBase != null && director.PirateBase.Resolved;
                    },
                    prepare: c =>
                    {
                        EnsureShipCombat(c);
                        CombatEncounterDirector director = TutorialFhij.Director(c);
                        ShipController ship = TutorialFhij.Ship(c);
                        if (director == null || ship == null || director.PirateBase != null) return;
                        PirateBaseRuntime runtime = PirateBaseRuntime.Create(
                            "tutorial/poi.pirate_base", director.SystemId,
                            ship.transform.position + new Vector3(0f, 0f, 800f), director, alreadyResolved: false);
                        if (runtime == null) return;
                        director.SetPirateBase(runtime);
                        c.Scope.OnCleanup(() =>
                        {
                            // The director keeps a reference, so clear it before the
                            // marker goes: its own Update would otherwise tick a base
                            // that is on its way out.
                            if (director != null && director.PirateBase == runtime) director.SetPirateBase(null);
                            if (runtime == null) return;
                            runtime.gameObject.SetActive(false);
                            if (Application.isPlaying) UnityEngine.Object.Destroy(runtime.gameObject);
                            else UnityEngine.Object.DestroyImmediate(runtime.gameObject);
                        });
                    },
                    objectives: (c, report) =>
                    {
                        CombatEncounterDirector director = TutorialFhij.Director(c);
                        report.SetObjective(0, director?.PirateBase != null);
                        report.SetObjective(2, director?.PirateBase != null && director.PirateBase.Resolved);
                    },
                    reject: c =>
                    {
                        CombatEncounterDirector director = TutorialFhij.Director(c);
                        return director?.PirateBase != null && director.PirateBase.Resolved
                            ? "ÜS ETKİSİZ HÂLE GETİRİLDİ" : null;
                    }),
                checklistTr: new[] { "Üssü bul", "Taretin ateş ettiğini gör", "Garnizonu ve üssü yok et" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 153 --------------------------------------------------------------

        private static TutorialStepDefinition EscortDefense()
        {
            return new TutorialStepDefinition(
                Chapter, "I-20", "Escort ile Savunma",
                "Escort'un koruduğu hedefe yaklaşan tehdidi karşıladığını gör.",
                "Escort'un gerçekten angaje olduğunu gör.",
                () =>
                {
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EscortState escort = EscortStateOf(c);
                            if (escort == null || string.IsNullOrEmpty(escort.EngagedThreatId)) return false;
                            return escort.SubState == EscortSubState.Engaging
                                   || escort.SubState == EscortSubState.Chasing;
                        },
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            ShipHangar hangar = TutorialFhij.Hangar(c);
                            TutorialFhij.EnsureLevel(VehicleCatalog.EscortMk1.UnlockLevel);
                            TutorialFhij.EnsureHangarTier(hangar, VehicleCatalog.EscortMk1.RequiredHangarTier);
                            // An Escort in the hangar and a threat in the sky -- and
                            // deliberately no protection order. Assigning it here was
                            // Prepare doing the step's own first checklist row: once
                            // the Escort is protecting something, its own AI closes on
                            // the nearest threat within a few frames and the step
                            // completed with the player having given no command at all.
                            TutorialFhij.BuildVehicleForPrerequisite(c, VehicleClass.Escort);
                            SpawnTrainingEnemy(c, EnemyIds.LooterTransportMk1, new Vector3(0f, 0f, 400f));
                        },
                        objectives: (c, report) =>
                        {
                            EscortState escort = EscortStateOf(c);
                            report.SetObjective(0, escort != null);
                            report.SetObjective(1, TutorialFhij.FirstEnemy(EnemyIds.LooterTransportMk1) != null);
                            report.SetObjective(2, escort != null && !string.IsNullOrEmpty(escort.EngagedThreatId));
                        },
                        reject: c =>
                        {
                            EscortState escort = EscortStateOf(c);
                            if (escort == null) return TutorialFhij.RejectText(ShipHangar.EscortAssignRejectReason.VehicleNotEligible);
                            return string.IsNullOrEmpty(escort.EngagedThreatId) ? "TEHDİT ESCORT MENZİLİ DIŞINDA" : null;
                        });
                },
                checklistTr: new[] { "Escort'a koruma ata", "Tehdidi yaklaştır", "Angaje olup ateş ettiğini doğrula" });
        }

        // ---- shared step bodies ----------------------------------------------

        /// <summary>
        /// A planetary-defence lesson, completed by the real durability the
        /// defence's own fire path removes. <c>TickDefenseCombat</c> is private,
        /// so the observable is what it produces, never a call it makes.
        /// </summary>
        private static TutorialStepDefinition DefenseDamageStep(string stepId, string titleTr, string instructionTr,
            string summaryTr, SurfaceStructureType type, string[] checklist)
        {
            return new TutorialStepDefinition(
                Chapter, stepId, titleTr, instructionTr, summaryTr,
                () =>
                {
                    string enemyId = null;
                    float baseline = -1f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            if (enemy == null) return baseline >= 0f && enemyId != null;
                            return baseline >= 0f && TutorialFhij.DurabilityTotal(enemy.Combat) < baseline - 0.001f;
                        },
                        prepare: c =>
                        {
                            EnsureShipCombat(c);
                            SurfaceStructure defense = TutorialFhij.AnyStructure(type);
                            StructureTierDef def = PlanetaryDefenseCatalog.Get(type, defense?.Runtime?.Tier ?? 1);
                            float distance = def != null ? def.Range * 0.5f : 500f;
                            EnemyAgent enemy = SpawnTrainingEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, distance));
                            enemyId = enemy != null ? enemy.Id : null;
                            baseline = enemy != null ? TutorialFhij.DurabilityTotal(enemy.Combat) : -1f;
                        },
                        objectives: (c, report) =>
                        {
                            SurfaceStructure defense = TutorialFhij.AnyStructure(type);
                            report.SetObjective(0, defense != null && defense.Runtime != null && defense.Runtime.IsActive);
                            report.SetObjective(1, enemyId != null && EnemyAgent.Find(enemyId) != null);
                        },
                        reject: c =>
                        {
                            SurfaceStructure defense = TutorialFhij.AnyStructure(type);
                            return defense == null || defense.Runtime == null || !defense.Runtime.IsActive
                                ? "SAVUNMA ATEŞ EDEMİYOR" : null;
                        });
                },
                checklistTr: checklist);
        }

        // ---- readings ---------------------------------------------------------

        private static ShipCombatState ShipState(TutorialStepContext context)
        {
            ShipCombat combat = TutorialFhij.Combat(context);
            return combat != null ? combat.State : null;
        }

        private static void EnsureShipCombat(TutorialStepContext context)
        {
            ShipCombat combat = TutorialFhij.Combat(context);
            if (combat != null && combat.State == null) combat.InitializeNewCampaign();
        }

        private static EscortState EscortStateOf(TutorialStepContext context)
        {
            VehicleAgent escort = TutorialFhij.FirstVehicle(TutorialFhij.Hangar(context), VehicleClass.Escort,
                v => v.EscortState != null);
            return escort != null ? escort.EscortState : null;
        }

        private static EnemyAgent SpawnTrainingEnemy(TutorialStepContext context, string defId, Vector3 offset)
        {
            EnemyAgent agent = TutorialFhij.SpawnEnemy(context, defId, offset);
            TutorialFhij.ReleaseOnCleanup(context, agent);
            return agent;
        }

        private static Durability StructureDurability(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return null;
            IReadOnlyList<SurfaceStructure> all = ExtractionState.Structures;
            for (int i = 0; i < all.Count; i++)
            {
                SurfaceStructure s = all[i];
                if (s != null && s.TargetId == targetId) return s.Runtime?.Durability;
            }
            return null;
        }

        /// <summary>
        /// Every vehicle that is, on its own, earning the Scout warning bonus right
        /// now -- asked one at a time so the answer names <i>which</i> Scout, which
        /// the roster-wide overload cannot. Still the real resolver, still real vehicles.
        /// </summary>
        private static List<VehicleAgent> CountingScouts(TutorialStepContext context)
        {
            var counting = new List<VehicleAgent>();
            ShipHangar hangar = TutorialFhij.Hangar(context);
            string systemId = hangar != null ? hangar.CurrentSystemId : null;
            if (hangar == null || systemId == null) return counting;

            var single = new VehicleAgent[1];
            IReadOnlyList<VehicleAgent> all = hangar.Vehicles;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                single[0] = all[i];
                if (WarningResolver.HasRealActiveScoutInSystem(single, systemId, out _)) counting.Add(all[i]);
            }
            return counting;
        }

        private static VehicleAgent NewlyCountingScout(TutorialStepContext context, HashSet<string> alreadyCounted)
        {
            List<VehicleAgent> counting = CountingScouts(context);
            for (int i = 0; i < counting.Count; i++)
                if (!alreadyCounted.Contains(counting[i].VehicleId)) return counting[i];
            return null;
        }

        /// <summary>How many hostile siege rounds are registered with the encounter director right now.</summary>
        private static int SiegeRoundCount(TutorialStepContext context)
        {
            CombatEncounterDirector director = TutorialFhij.Director(context);
            return director == null ? 0 : CountProjectiles(director, ProjectileKind.EnemySiege, ProjectileFaction.Hostile);
        }

        private static int CountProjectiles(CombatEncounterDirector director, ProjectileKind kind, ProjectileFaction faction)
        {
            int count = 0;
            IReadOnlyList<ProjectileObject> all = director.Projectiles;
            for (int i = 0; i < all.Count; i++)
            {
                ProjectileObject p = all[i];
                if (p == null || p.State == null) continue;
                if (p.State.Kind == kind && p.State.Faction == faction) count++;
            }
            return count;
        }

        internal static string FirstPlanetWorldId(TutorialStepContext context)
        {
            ShipHangar hangar = TutorialFhij.Hangar(context);
            string fromHangar = ChapterFSteps.FirstPlanetInShipSystem(hangar);
            if (fromHangar != null) return fromHangar;
            foreach (PlanetIdentity planet in ExtractionState.AllPlanets)
                if (planet != null) return planet.WorldId;
            return null;
        }

        /// <summary>
        /// Installs a real raid at <see cref="RaidPhase.Scheduled"/> through the
        /// production seam, because pressure alone needs tens of active minutes to
        /// reach the schedule threshold. Setup only: every phase after Scheduled is
        /// <c>RaidScheduler.Tick</c>'s own transition, which is what the step waits for.
        /// </summary>
        private static void ScheduleTeachingRaid(TutorialStepContext context)
        {
            ThreatPressureState pressure = TutorialFhij.Pressure(context);
            if (pressure == null || pressure.IsRaidInProgress) return;
            string worldId = FirstPlanetWorldId(context);
            if (worldId == null) return;
            pressure.ScheduleRaid(RaidStage.Pirate, RaidDoctrine.Loot, worldId,
                new List<string> { EnemyIds.FighterMk1, EnemyIds.LooterTransportMk1 },
                20240809UL, WarningResolver.BaseWarningSeconds);

            context.Scope.OnCleanup(() =>
            {
                // Raiders are spawned by RaidScheduler during the step, not here, so
                // they have no scope of their own -- and an EnemyAgent that outlives
                // its step keeps running Update against a ship that may already be gone.
                for (int i = 0; i < pressure.SpawnedEnemyIds.Count; i++)
                {
                    EnemyAgent raider = EnemyAgent.Find(pressure.SpawnedEnemyIds[i]);
                    if (raider == null) continue;
                    raider.gameObject.SetActive(false);
                    if (Application.isPlaying) UnityEngine.Object.Destroy(raider.gameObject);
                    else UnityEngine.Object.DestroyImmediate(raider.gameObject);
                }
                pressure.ClearToIdle();
            });
        }
    }
}
