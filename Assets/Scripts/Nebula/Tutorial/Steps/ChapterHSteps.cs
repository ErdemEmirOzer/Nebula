using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Modules;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter H -- Silahlar ve Muharebe (curriculum steps 113-133).
    /// <para>
    /// Firing has no callable production seam: <c>ShipCombat.FireLaser</c>,
    /// <c>FirePlasma</c> and <c>TickEmLock</c> are private and reachable only
    /// through <c>UnityEngine.Input</c>. Every step here is therefore validated
    /// on what firing actually <i>does</i> -- the real damage
    /// <c>EnemyAgent.ApplyDamage</c> applies, the real projectile
    /// <c>ProjectileObject.Spawn</c> registers, the real energy
    /// <c>ShipCombatState.TryConsumeEnergy</c> removes, the real heat
    /// <c>HardpointState.AddHeat</c> adds -- plus the real
    /// <c>ShipCombat.WeaponFired</c>/<c>FireDenied</c>/<c>DamageApplied</c>
    /// events where they exist. Not one step reads a key.
    /// </para>
    /// <para>
    /// The two refusal steps (gimbal, range/line-of-sight) are validated on the
    /// geometry the player really produced by turning and moving the ship, and
    /// on the target having taken no damage while that geometry held. They are
    /// the honest form of "the game said no": the refusal cannot be observed
    /// directly, but the condition that causes it can, and the absence of damage
    /// proves it was obeyed.
    /// </para>
    /// </summary>
    public static class ChapterHSteps
    {
        private const TutorialChapterId Chapter = TutorialChapterId.Combat;

        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return TargetSelect();       // 113
            yield return FocusTarget();        // 114
            yield return LaserFire();          // 115
            yield return PlasmaFire();         // 116
            yield return EmFire();             // 117
            yield return HitscanVersusShot();  // 118
            yield return PlasmaSplash();       // 119
            yield return EnergyDraw();         // 120
            yield return HardpointHeat();      // 121
            yield return OverheatCycle();      // 122
            yield return GimbalLimit();        // 123
            yield return RangeAndLineOfSight();// 124
            yield return ProjectileIntercept();// 125
            yield return ShieldDamage();       // 126
            yield return ArmourDamage();       // 127
            yield return HullDamage();         // 128
            yield return EmergencyShutdown();  // 129
            yield return Repair();             // 130
            yield return DestroyEnemy();       // 131
            yield return WreckageSalvage();    // 132
            yield return ClearDamaged();       // 133
        }

        // ---- 113 --------------------------------------------------------------

        private static TutorialStepDefinition TargetSelect() => new TutorialStepDefinition(
            Chapter, "H-01", "Hedef Seç",
            "Menzildeki düşmanı elle hedef al.",
            "Gerçek bir düşmanı hedef göstergesine al.",
            () => TutorialSteps.OnEvent(
                subscribe: (c, done) =>
                {
                    ShipCombat combat = TutorialFhij.Combat(c);
                    if (combat == null) return null;
                    Action<EnemyAgent> handler = agent => { if (agent != null) done(); };
                    combat.TargetChanged += handler;
                    return () => { if (combat != null) combat.TargetChanged -= handler; };
                },
                prepare: c =>
                {
                    EnsureCombat(c);
                    if (TutorialFhij.FirstEnemy(EnemyIds.FighterMk1) == null)
                        TutorialFhij.ReleaseOnCleanup(c, TutorialFhij.SpawnEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 600f)));
                },
                reject: c => TutorialFhij.FirstEnemy(null) == null ? "MENZİLDE HEDEF YOK" : null),
            checklistTr: new[] { "Düşmanı görüş alanına al", "Hedef seçme tuşuna bas", "Hedef göstergesinin dolduğunu gör" },
            keyHint: TutorialFhij.KeyHint(CombatAction.ManualTargetSelect));

        // ---- 114 --------------------------------------------------------------

        private static TutorialStepDefinition FocusTarget() => new TutorialStepDefinition(
            Chapter, "H-02", "Odak Hedefi Ata",
            "Seçtiğin düşmanı gezegen savunmasının odak hedefi yap.",
            "Gezegenin odak hedefinin gerçekten değiştiğini gör.",
            () => TutorialSteps.Poll(
                complete: c => FocusedLiveEnemyId(c) != null,
                prepare: c =>
                {
                    EnsureCombat(c);
                    ShipCombat combat = TutorialFhij.Combat(c);
                    if (TutorialFhij.FirstEnemy(EnemyIds.FighterMk1) == null)
                        TutorialFhij.ReleaseOnCleanup(c, TutorialFhij.SpawnEnemy(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 500f)));
                    // A target may be selected for the player -- selecting is H-01's
                    // lesson, not this one -- but the focus itself is never set here.
                    if (combat != null && combat.Target == null) combat.TrySelectNearestTarget();
                },
                reject: c =>
                {
                    ShipCombat combat = TutorialFhij.Combat(c);
                    if (combat == null || combat.Target == null || combat.Target.Destroyed) return "GEÇERLİ ODAK HEDEFİ YOK";
                    return TutorialFhij.Defense(c) == null ? "GEÇERLİ ODAK HEDEFİ YOK" : null;
                }),
            checklistTr: new[] { "Bir düşmanı hedefle", "Odak atama tuşuna bas", "Odak hedefinin değiştiğini gör" },
            keyHint: TutorialFhij.KeyHint(CombatAction.SetFocusTarget));

        // ---- 115 --------------------------------------------------------------

        private static TutorialStepDefinition LaserFire() => WeaponDamageStep(
            "H-03", "Lazer Ateşi", "Hedefe lazerle ateş et.",
            "Lazerin gerçek hasarını hedefin dayanıklılığında gör.",
            WeaponFamily.Laser, CombatAction.FireLaser,
            new[] { "Hedefi seç", "Lazer tuşunu basılı tut", "Hedefin dayanıklılığının düştüğünü gör" });

        // ---- 116 --------------------------------------------------------------

        private static TutorialStepDefinition PlasmaFire()
        {
            return new TutorialStepDefinition(
                Chapter, "H-04", "Plazma Ateşi",
                "Plazma silahıyla bir mermi at.",
                "Merminin gerçekten doğduğunu ve yol aldığını gör.",
                () =>
                {
                    bool fired = false;
                    return TutorialSteps.Poll(
                        complete: c => fired || PlayerProjectileExists(c, ProjectileKind.Plasma),
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, WeaponFamily.Plasma);
                            SpawnTrainingTarget(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 800f));
                            ShipCombat combat = TutorialFhij.Combat(c);
                            if (combat == null) return;
                            Action<WeaponFamily> handler = family => { if (family == WeaponFamily.Plasma) fired = true; };
                            combat.WeaponFired += handler;
                            c.Scope.OnCleanup(() => { if (combat != null) combat.WeaponFired -= handler; });
                        },
                        reject: c =>
                        {
                            ShipCombat combat = TutorialFhij.Combat(c);
                            if (combat?.State == null) return null;
                            if (combat.State.Energy < WeaponCatalog.PlasmaEnergyPerShot) return "PLAZMA HAZIR DEĞİL";
                            return FindHardpoint(combat, WeaponFamily.Plasma) == null ? "PLAZMA TAKILI DEĞİL" : null;
                        });
                },
                checklistTr: new[] { "Hedefi menzile al", "Plazma tuşuna bas", "Merminin yol aldığını gör" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FirePlasma));
        }

        // ---- 117 --------------------------------------------------------------

        private static TutorialStepDefinition EmFire()
        {
            return new TutorialStepDefinition(
                Chapter, "H-05", "EM Ateşi",
                "EM füzesi için kilit tut ve ateşle.",
                "EM'in gerçek etkisini -- düşmanın bozulmasını -- gör.",
                () =>
                {
                    string enemyId = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent target = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            if (target != null && target.IsDisrupted) return true;
                            return PlayerProjectileExists(c, ProjectileKind.Em);
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, WeaponFamily.Em);
                            EnemyAgent enemy = SpawnTrainingTarget(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 900f));
                            enemyId = enemy != null ? enemy.Id : null;
                        },
                        objectives: (c, report) =>
                        {
                            ShipCombat combat = TutorialFhij.Combat(c);
                            report.SetObjective(1, combat != null && combat.EmLockProgress01 > 0f);
                            EnemyAgent target = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            report.SetObjective(2, target != null && target.IsDisrupted);
                        },
                        reject: c =>
                        {
                            ShipCombat combat = TutorialFhij.Combat(c);
                            if (combat?.State == null) return null;
                            if (combat.State.Energy < WeaponCatalog.EmEnergyPerShot) return "ENERJİ YETERSİZ";
                            EnemyAgent target = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            if (target != null && target.DisruptImmunityRemaining > 0f) return "EM BAĞIŞIKLIĞI AKTİF";
                            return null;
                        });
                },
                checklistTr: new[] { "Hedefi konide tut", "Kilidin dolmasını bekle", "Düşmanın kilitlendiğini gör" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireEm));
        }

        // ---- 118 --------------------------------------------------------------

        private static TutorialStepDefinition HitscanVersusShot()
        {
            return new TutorialStepDefinition(
                Chapter, "H-06", "Anında İsabet ve Mermi Farkı",
                "Lazerin anında, plazmanın mermiyle isabet ettiğini karşılaştır.",
                "İki silahın isabet zamanlamasını gerçek durum üzerinden karşılaştır.",
                () =>
                {
                    string enemyId = null;
                    float baseline = 0f;
                    bool sawInstantHit = false;
                    bool sawProjectile = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent target = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            // Two different real observables, deliberately not one: the
                            // laser's damage is already on the target's Durability, while
                            // the plasma round is still a registered projectile in flight.
                            if (PlayerProjectileExists(c, ProjectileKind.Plasma)) sawProjectile = true;
                            if (target != null && TutorialFhij.DurabilityTotal(target.Combat) < baseline - 0.001f)
                                sawInstantHit = true;
                            if (target == null && baseline > 0f) sawInstantHit = true;
                            return sawInstantHit && sawProjectile;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, WeaponFamily.Plasma);
                            EnemyAgent enemy = SpawnTrainingTarget(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 700f));
                            enemyId = enemy != null ? enemy.Id : null;
                            baseline = enemy != null ? TutorialFhij.DurabilityTotal(enemy.Combat) : 0f;
                        },
                        objectives: (c, report) =>
                        {
                            report.SetObjective(0, sawInstantHit);
                            report.SetObjective(1, sawProjectile);
                        });
                },
                checklistTr: new[] { "Lazerin aynı karede vurduğunu gör", "Plazmanın mermiyle gittiğini gör" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FirePlasma));
        }

        // ---- 119 --------------------------------------------------------------

        private static TutorialStepDefinition PlasmaSplash()
        {
            return new TutorialStepDefinition(
                Chapter, "H-07", "Plazma Serpintisi",
                "İki düşman yan yanayken plazmayla ateş et ve serpinti hasarını gör.",
                "İkinci düşmanın da hasar aldığını doğrula.",
                () =>
                {
                    string bystanderId = null;
                    float bystanderBaseline = 0f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent bystander = bystanderId != null ? EnemyAgent.Find(bystanderId) : null;
                            if (bystander == null) return bystanderBaseline > 0f && bystanderId != null;
                            return TutorialFhij.DurabilityTotal(bystander.Combat) < bystanderBaseline - 0.001f;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, WeaponFamily.Plasma);
                            SpawnTrainingTarget(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 700f));
                            EnemyAgent bystander = SpawnTrainingTarget(c, EnemyIds.FighterMk1,
                                new Vector3(WeaponCatalog.PlasmaSplashRadius * 0.8f, 0f, 700f));
                            bystanderId = bystander != null ? bystander.Id : null;
                            bystanderBaseline = bystander != null ? TutorialFhij.DurabilityTotal(bystander.Combat) : 0f;
                        },
                        reject: c => TutorialFhij.LiveEnemyCount(EnemyIds.FighterMk1) < 2
                            ? "SERPİNTİ MENZİLİ DIŞINDA"
                            : null);
                },
                checklistTr: new[] { "İki düşmanı yakın tut", "Birine plazma at", "İkincisinin hasar aldığını doğrula" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FirePlasma));
        }

        // ---- 120 --------------------------------------------------------------

        private static TutorialStepDefinition EnergyDraw()
        {
            return new TutorialStepDefinition(
                Chapter, "H-08", "Enerji Tüketimi",
                "Ateş ederken savaş enerjisinin düştüğünü izle.",
                "Enerjinin gerçekten harcandığını gör.",
                () =>
                {
                    float baseline = -1f;
                    bool spent = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            ShipCombatState state = State(c);
                            if (state == null) return false;
                            // Directional: energy regenerates on its own every tick, so
                            // "it moved" would complete this step without the player firing.
                            if (baseline >= 0f && state.Energy < baseline - 0.001f) spent = true;
                            if (state.WeaponsLocked) spent = true;
                            return spent;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, WeaponFamily.Laser);
                            ShipCombatState state = State(c);
                            baseline = state != null ? state.Energy : -1f;
                        },
                        reject: c =>
                        {
                            ShipCombatState state = State(c);
                            return state != null && state.WeaponsLocked ? "ENERJİ YETERSİZ — SİLAHLAR KİLİTLİ" : null;
                        });
                },
                checklistTr: new[] { "Enerji göstergesini oku", "Ateş et", "Enerjinin azaldığını gör" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 121 --------------------------------------------------------------

        private static TutorialStepDefinition HardpointHeat()
        {
            return new TutorialStepDefinition(
                Chapter, "H-09", "Hardpoint Isısı",
                "Sürekli ateşle hardpoint ısısının yükseldiğini gör.",
                "Isının gerçekten tırmandığını gör.",
                () =>
                {
                    float baseline = -1f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            HardpointState hardpoint = LaserHardpoint(c);
                            return hardpoint != null && baseline >= 0f && hardpoint.Heat > baseline + 0.001f;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, WeaponFamily.Laser);
                            HardpointState hardpoint = LaserHardpoint(c);
                            baseline = hardpoint != null ? hardpoint.Heat : -1f;
                        },
                        reject: c => LaserHardpoint(c) == null ? "HARDPOINT BOŞ" : null);
                },
                checklistTr: new[] { "Isı göstergesini oku", "Kesintisiz ateş et", "Isının tırmandığını gör" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 122 --------------------------------------------------------------

        private static TutorialStepDefinition OverheatCycle()
        {
            return new TutorialStepDefinition(
                Chapter, "H-10", "Aşırı Isınma ve Soğuma",
                "Hardpoint'i aşırı ısıt ve soğuma süresini bekle.",
                "Kilidi ve kilidin açılmasını gerçek hardpoint üzerinde gör.",
                () =>
                {
                    bool sawOverheat = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            HardpointState hardpoint = LaserHardpoint(c);
                            if (hardpoint == null) return false;
                            if (hardpoint.Overheated) sawOverheat = true;
                            return sawOverheat && !hardpoint.Overheated && hardpoint.CanFire;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, WeaponFamily.Laser);
                        },
                        objectives: (c, report) =>
                        {
                            HardpointState hardpoint = LaserHardpoint(c);
                            report.SetObjective(0, hardpoint != null && hardpoint.Heat >= WeaponCatalog.HardpointMaxHeat - 0.001f);
                            report.SetObjective(1, sawOverheat);
                            report.SetObjective(2, sawOverheat && hardpoint != null && hardpoint.CanFire);
                        },
                        reject: c =>
                        {
                            HardpointState hardpoint = LaserHardpoint(c);
                            if (hardpoint == null) return "HARDPOINT BOŞ";
                            return hardpoint.Overheated ? "HARDPOINT AŞIRI ISINDI" : null;
                        });
                },
                checklistTr: new[] { "Isıyı 100'e çıkar", "Ateş edemediğini gör", "Soğuma bitince tekrar ateş et" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 123 --------------------------------------------------------------

        private static TutorialStepDefinition GimbalLimit()
        {
            return new TutorialStepDefinition(
                Chapter, "H-11", "Gimbal Sınırı",
                "Hedefi gimbal konisinin dışına al ve lazerin ateş etmediğini gör.",
                "Koninin dışındaki hedefe hasar gitmediğini doğrula.",
                () =>
                {
                    string enemyId = null;
                    float baseline = 0f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            ShipController ship = TutorialFhij.Ship(c);
                            if (enemy == null || ship == null) return false;
                            Vector3 toTarget = enemy.transform.position - ship.transform.position;
                            bool outsideCone = Vector3.Angle(ship.transform.forward, toTarget) > WeaponCatalog.LaserGimbalDegrees;
                            bool inRange = toTarget.magnitude <= WeaponCatalog.LaserRange(LaserTier(c));
                            bool untouched = TutorialFhij.DurabilityTotal(enemy.Combat) >= baseline - 0.001f;
                            return outsideCone && inRange && untouched;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            // A refusal lesson only teaches anything with the weapon really
                            // installed: with an empty hardpoint "no damage" would be true
                            // for the wrong reason.
                            EnsureTrainedWeapon(c, WeaponFamily.Laser);
                            ShipController ship = TutorialFhij.Ship(c);
                            // Deliberately INSIDE the cone: the player's own turn is what
                            // takes it out, so preparing cannot satisfy the step.
                            Vector3 forward = ship != null ? ship.transform.forward : Vector3.forward;
                            EnemyAgent enemy = SpawnTrainingTarget(c, EnemyIds.FighterMk1,
                                forward * (WeaponCatalog.LaserBaseRange * 0.5f));
                            enemyId = enemy != null ? enemy.Id : null;
                            baseline = enemy != null ? TutorialFhij.DurabilityTotal(enemy.Combat) : 0f;
                        },
                        reject: c =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            ShipController ship = TutorialFhij.Ship(c);
                            if (enemy == null || ship == null) return "MENZİLDE HEDEF YOK";
                            Vector3 toTarget = enemy.transform.position - ship.transform.position;
                            return Vector3.Angle(ship.transform.forward, toTarget) > WeaponCatalog.LaserGimbalDegrees
                                ? "HEDEF GIMBAL AÇISININ DIŞINDA" : null;
                        });
                },
                checklistTr: new[] { "Hedefi koninin dışına çıkar", "Ateş etmeyi dene", "Hasar gitmediğini doğrula" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 124 --------------------------------------------------------------

        private static TutorialStepDefinition RangeAndLineOfSight()
        {
            return new TutorialStepDefinition(
                Chapter, "H-12", "Menzil ve Görüş Hattı",
                "Menzil dışındaki ve gök cismi arkasındaki hedefe ateş etmeyi dene.",
                "İki reddi de gerçek dünya durumu üzerinden gör.",
                () =>
                {
                    string enemyId = null;
                    float baseline = 0f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            ShipController ship = TutorialFhij.Ship(c);
                            if (enemy == null || ship == null) return false;
                            Vector3 from = ship.transform.position;
                            Vector3 to = enemy.transform.position;
                            bool outOfRange = (to - from).magnitude > WeaponCatalog.LaserRange(LaserTier(c));
                            bool blocked = !HasLineOfSight(from, to);
                            bool untouched = TutorialFhij.DurabilityTotal(enemy.Combat) >= baseline - 0.001f;
                            return (outOfRange || blocked) && untouched;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            // Same reason as H-11: an empty hardpoint would prove the refusal
                            // by accident rather than by the range/line-of-sight rule.
                            EnsureTrainedWeapon(c, WeaponFamily.Laser);
                            ShipController ship = TutorialFhij.Ship(c);
                            Vector3 forward = ship != null ? ship.transform.forward : Vector3.forward;
                            EnemyAgent enemy = SpawnTrainingTarget(c, EnemyIds.FighterMk1,
                                forward * (WeaponCatalog.LaserBaseRange * 0.4f));
                            enemyId = enemy != null ? enemy.Id : null;
                            baseline = enemy != null ? TutorialFhij.DurabilityTotal(enemy.Combat) : 0f;
                        },
                        reject: c =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            ShipController ship = TutorialFhij.Ship(c);
                            if (enemy == null || ship == null) return "MENZİLDE HEDEF YOK";
                            Vector3 from = ship.transform.position;
                            Vector3 to = enemy.transform.position;
                            if ((to - from).magnitude > WeaponCatalog.LaserRange(LaserTier(c))) return "HEDEF MENZİL DIŞINDA";
                            return !HasLineOfSight(from, to) ? "GÖRÜŞ HATTI KAPALI" : null;
                        });
                },
                checklistTr: new[] { "Hedefi menzil dışına çıkar", "Hedefi bir gezegenin arkasına al" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 125 --------------------------------------------------------------

        private static TutorialStepDefinition ProjectileIntercept()
        {
            return new TutorialStepDefinition(
                Chapter, "H-13", "Mermi Kesme",
                "Düşman merminin gezegen lazeri tarafından düşürüldüğünü gör.",
                "Merminin gerçekten kesildiğini doğrula.",
                () =>
                {
                    ProjectileState watched = null;
                    return TutorialSteps.Poll(
                        complete: c => watched != null && watched.InterceptedDown,
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            CombatEncounterDirector director = TutorialFhij.Director(c);
                            ShipController ship = TutorialFhij.Ship(c);
                            if (director == null || ship == null) return;
                            ProjectileObject projectile = ProjectileObject.Spawn(
                                ProjectileKind.EnemySiege, 1, "tutorial-siege",
                                ship.transform.position + new Vector3(0f, 0f, 900f),
                                Vector3.back * ProjectileCatalog.SiegeProjectileSpeed,
                                ProjectileCatalog.SiegeLifetimeSeconds, null, director,
                                ProjectileFaction.Hostile, interceptable: true,
                                interceptionDurability: ProjectileCatalog.InterceptionDurability,
                                damageAmount: 100f, intendedTargetId: "ship");
                            watched = projectile != null ? projectile.State : null;
                            if (projectile != null)
                                c.Scope.OnCleanup(() =>
                                {
                                    if (projectile == null || projectile.gameObject == null) return;
                                    // Unregistered and deactivated before the deferred
                                    // destroy, so it can neither travel one more frame
                                    // nor be counted by a later step's projectile reading.
                                    if (director != null) director.UnregisterProjectile(projectile);
                                    projectile.gameObject.SetActive(false);
                                    if (Application.isPlaying) UnityEngine.Object.Destroy(projectile.gameObject);
                                    else UnityEngine.Object.DestroyImmediate(projectile.gameObject);
                                });
                        },
                        objectives: (c, report) =>
                        {
                            report.SetObjective(0, watched != null);
                            report.SetObjective(1, TutorialFhij.AnyStructure(SurfaceStructureType.Laser) != null);
                            report.SetObjective(2, watched != null && watched.InterceptedDown);
                        },
                        reject: c => TutorialFhij.AnyStructure(SurfaceStructureType.Laser) == null
                            ? "SAVUNMA MENZİLİ YETERSİZ"
                            : null);
                },
                checklistTr: new[] { "Düşman merminin geldiğini gör", "Lazer savunmasının menzilini doğrula", "Merminin düştüğünü gör" });
        }

        // ---- 126 --------------------------------------------------------------

        private static TutorialStepDefinition ShieldDamage() => LayerDamageStep(
            "H-14", "Kalkan Hasarı", "Kalkan varken hasarın önce kalkanı yediğini gör.",
            "Yalnızca kalkanın düştüğünü doğrula.", ShipDamageLayer.Shield,
            new[] { "Kalkanın dolu olduğunu gör", "Hasar al", "Yalnızca kalkanın düştüğünü doğrula" },
            CombatAction.FireLaser);

        // ---- 127 --------------------------------------------------------------

        private static TutorialStepDefinition ArmourDamage() => LayerDamageStep(
            "H-15", "Zırh Hasarı", "Kalkan bitince hasarın zırha geçtiğini gör.",
            "Zırhın düştüğünü, gövdenin henüz düşmediğini doğrula.", ShipDamageLayer.Armor,
            new[] { "Kalkanı sıfırla", "Ateşe devam et", "Zırhın düştüğünü doğrula" },
            CombatAction.FirePlasma);

        // ---- 128 --------------------------------------------------------------

        private static TutorialStepDefinition HullDamage() => LayerDamageStep(
            "H-16", "Gövde Hasarı", "Zırh bitince gövdenin hasar aldığını gör.",
            "Gövdenin düştüğünü doğrula.", ShipDamageLayer.Hull,
            new[] { "Zırhı sıfırla", "Ateşe devam et", "Gövdenin düştüğünü doğrula" },
            CombatAction.FireLaser);

        // ---- 129 --------------------------------------------------------------

        private static TutorialStepDefinition EmergencyShutdown()
        {
            return new TutorialStepDefinition(
                Chapter, "H-17", "Acil Kapanma",
                "Gövden sıfırlanınca geminin yok olmadığını, acil kapanmaya geçtiğini gör.",
                "Acil kapanmayı ve kurtarmayı gerçek durum üzerinde gör.",
                () =>
                {
                    bool started = false;
                    bool damagedAtStart = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            ShipCombatState state = State(c);
                            if (state == null) return false;
                            if (state.EmergencyShutdown) started = true;
                            return started || (state.Damaged && !damagedAtStart);
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            ShipCombatState state = State(c);
                            damagedAtStart = state != null && state.Damaged;
                        },
                        objectives: (c, report) =>
                        {
                            ShipCombat combat = TutorialFhij.Combat(c);
                            report.SetObjective(0, combat?.State != null && combat.State.EmergencyShutdown);
                            report.SetObjective(1, combat?.Shutdown != null && combat.Shutdown.Active);
                            report.SetObjective(2, combat?.State != null && combat.State.Damaged);
                        },
                        reject: c =>
                        {
                            ShipCombat combat = TutorialFhij.Combat(c);
                            return combat?.State != null && combat.State.EmergencyShutdown
                                ? "ACİL KAPANMA — SİSTEMLER KAPALI" : null;
                        });
                },
                checklistTr: new[] { "Gövdeyi sıfırla", "Acil kapanma sayacını gör", "Kurtarmanın uygulandığını doğrula" });
        }

        // ---- 130 --------------------------------------------------------------

        private static TutorialStepDefinition Repair() => new TutorialStepDefinition(
            Chapter, "H-18", "Gemiyi Onar",
            "Güvenli bir yerde dur ve onarımı başlat.",
            "Onarımı gerçekten başlat ve zırhın arttığını gör.",
            () => TutorialSteps.OnEvent(
                subscribe: (c, done) =>
                {
                    ShipCombat combat = TutorialFhij.Combat(c);
                    if (combat == null) return null;
                    Action handler = () => done();
                    combat.RepairStarted += handler;
                    return () => { if (combat != null) combat.RepairStarted -= handler; };
                },
                prepare: c =>
                {
                    EnsureCombat(c);
                    ShipCombat combat = TutorialFhij.Combat(c);
                    if (combat?.State == null) return;
                    // Real damage through the real path, then the real "time since damage"
                    // restore seam, so the eight-second gate is genuinely satisfied and the
                    // only thing left undone is the player's own repair command.
                    if (!combat.State.Damaged && combat.State.Durability.Armor > 1f)
                        combat.ApplyDamage(combat.State.Durability.ArmorMax * 0.6f
                                           + combat.State.Durability.ShieldMax, DamageProfile.Neutral);
                    combat.State.RestoreSinceLastDamage(RepairCatalog.RequiredSafeSecondsSinceLastDamage + 1f);
                    combat.SetEncounterOpen(false);
                    foreach ((string itemId, float amount) in RepairCatalog.PackageCost(combat.HullFormValue))
                        TutorialFhij.EnsureItem(TutorialFhij.Cargo(c), itemId, amount * 3f);
                },
                reject: c =>
                {
                    ShipCombat combat = TutorialFhij.Combat(c);
                    if (combat == null) return null;
                    RepairRejectReason reason = combat.CheckRepairStart();
                    return reason == RepairRejectReason.None || reason == RepairRejectReason.AlreadyActive
                        ? null : TutorialFhij.RejectText(reason);
                }),
            checklistTr: new[] { "Hızını düşür", "Sekiz saniye bekle", "Onar tuşuna bas" },
            keyHint: TutorialFhij.KeyHint(CombatAction.Repair));

        // ---- 131 --------------------------------------------------------------

        private static TutorialStepDefinition DestroyEnemy()
        {
            return new TutorialStepDefinition(
                Chapter, "H-19", "Düşmanı Yok Et",
                "Bir düşmanı tamamen yok et.",
                "Düşmanın gerçekten kayıttan silindiğini gör.",
                () =>
                {
                    string enemyId = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            if (enemyId == null) return false;
                            EnemyAgent agent = EnemyAgent.Find(enemyId);
                            return agent == null || agent.Destroyed;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, WeaponFamily.Laser);
                            EnemyAgent enemy = SpawnTrainingTarget(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 500f));
                            enemyId = enemy != null ? enemy.Id : null;
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent agent = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            report.SetObjective(0, agent != null && agent.Combat != null && agent.Combat.Shield <= 0f);
                            report.SetObjective(1, agent == null);
                        },
                        reject: c => enemyId == null ? "MENZİLDE HEDEF YOK" : null);
                },
                checklistTr: new[] { "Kalkan, zırh ve gövdeyi bitir", "Düşmanın sahneden kalktığını gör" },
                keyHint: TutorialFhij.KeyHint(CombatAction.FireLaser));
        }

        // ---- 132 --------------------------------------------------------------

        private static TutorialStepDefinition WreckageSalvage()
        {
            return new TutorialStepDefinition(
                Chapter, "H-20", "Enkaz ve Ganimet",
                "Yok edilen düşmanın bıraktığı enkazdan kaynak topla.",
                "Enkazdaki gerçek kalemi çek.",
                () =>
                {
                    CombatWreckageState watched = null;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            if (watched == null) return false;
                            for (int i = 0; i < watched.Nodes.Count; i++)
                                if (watched.Nodes[i].Remaining < watched.Nodes[i].Total - 0.0001f) return true;
                            return false;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            CombatEncounterDirector director = TutorialFhij.Director(c);
                            if (director == null) return;
                            CombatWreckageState wreck = RaidSalvageResolver.Resolve(
                                "tutorial/combat-wreck.h20", 20240809UL, RaidOutcome.DecisiveVictory,
                                new List<string> { EnemyIds.FighterMk1, EnemyIds.LooterTransportMk1 });
                            if (wreck == null || wreck.Nodes.Count == 0) return;
                            director.Wreckage.Add(wreck);
                            watched = wreck;
                            c.Scope.OnCleanup(() => director.Wreckage.Remove(wreck));
                        },
                        objectives: (c, report) =>
                        {
                            report.SetObjective(0, watched != null);
                            report.SetObjective(1, watched != null && watched.Nodes.Count > 0);
                        },
                        reject: c => watched != null && watched.Expired ? "ENKAZ TÜKENDİ" : null);
                },
                checklistTr: new[] { "Enkazın oluştuğunu gör", "İçindeki kalemleri oku", "Kaynağı çek" });
        }

        // ---- 133 --------------------------------------------------------------

        private static TutorialStepDefinition ClearDamaged()
        {
            return new TutorialStepDefinition(
                Chapter, "H-21", "Yenilgi ve Onarım İhtiyacı",
                "Kurtarma sonrası hasarlı durumdan çıkmak için onarımı tamamla.",
                "Hasarlı işaretinin gerçekten kalktığını gör.",
                () =>
                {
                    bool wasDamaged = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            ShipCombatState state = State(c);
                            if (state == null) return false;
                            if (state.Damaged) { wasDamaged = true; return false; }
                            return wasDamaged;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            ShipCombat combat = TutorialFhij.Combat(c);
                            if (combat?.State == null) return;
                            // Follow the real defeat path: real damage to zero hull, then the
                            // real recovery fractions the emergency recovery applies. Nothing
                            // is hand-set, and the Damaged mark is a consequence, not a write.
                            if (!combat.State.Damaged)
                            {
                                combat.ApplyDamage((combat.State.Durability.ShieldMax + combat.State.Durability.ArmorMax
                                                    + combat.State.Durability.HullMax) * 4f, DamageProfile.Neutral);
                                combat.State.RecoverToFractions(RecoveryResolver.RecoveryHullFraction,
                                    RecoveryResolver.RecoveryArmorFraction, RecoveryResolver.RecoveryShieldFraction);
                            }
                            combat.State.RestoreSinceLastDamage(RepairCatalog.RequiredSafeSecondsSinceLastDamage + 1f);
                            wasDamaged = combat.State.Damaged;
                            foreach ((string itemId, float amount) in RepairCatalog.PackageCost(combat.HullFormValue))
                                TutorialFhij.EnsureItem(TutorialFhij.Cargo(c), itemId, amount * 6f);
                        },
                        objectives: (c, report) =>
                        {
                            ShipCombatState state = State(c);
                            report.SetObjective(0, state != null && state.Damaged);
                            report.SetObjective(2, state != null && !state.Damaged);
                        },
                        reject: c =>
                        {
                            ShipCombatState state = State(c);
                            return state != null && state.Damaged ? "HASARLI: HIZLANDIRMA KULLANILAMAZ" : null;
                        });
                },
                checklistTr: new[] { "Hasarlı işaretini gör", "Hızının düştüğünü fark et", "Zırh ve gövdeyi %60'a çıkar" },
                keyHint: TutorialFhij.KeyHint(CombatAction.Repair));
        }

        // ---- shared step bodies ----------------------------------------------

        /// <summary>
        /// A weapon step whose success is the real damage that weapon's own fire
        /// path applies, measured against the target's durability when the step
        /// opened. The <c>WeaponFired</c> event is recorded as an objective, never
        /// as the success condition -- an event without damage teaches nothing.
        /// </summary>
        private static TutorialStepDefinition WeaponDamageStep(string stepId, string titleTr, string instructionTr,
            string summaryTr, WeaponFamily family, CombatAction action, string[] checklist)
        {
            return new TutorialStepDefinition(
                Chapter, stepId, titleTr, instructionTr, summaryTr,
                () =>
                {
                    string enemyId = null;
                    float baseline = 0f;
                    bool fired = false;
                    bool damaged = false;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            if (enemy == null) return damaged || (enemyId != null && baseline > 0f);
                            if (TutorialFhij.DurabilityTotal(enemy.Combat) < baseline - 0.001f) damaged = true;
                            return damaged;
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, family);
                            ShipCombat combat = TutorialFhij.Combat(c);
                            EnemyAgent enemy = SpawnTrainingTarget(c, EnemyIds.FighterMk1, new Vector3(0f, 0f, 500f));
                            enemyId = enemy != null ? enemy.Id : null;
                            baseline = enemy != null ? TutorialFhij.DurabilityTotal(enemy.Combat) : 0f;
                            if (combat == null) return;
                            if (combat.Target == null) combat.TrySelectNearestTarget();
                            Action<WeaponFamily> handler = f => { if (f == family) fired = true; };
                            combat.WeaponFired += handler;
                            c.Scope.OnCleanup(() => { if (combat != null) combat.WeaponFired -= handler; });
                        },
                        objectives: (c, report) =>
                        {
                            ShipCombat combat = TutorialFhij.Combat(c);
                            report.SetObjective(0, combat != null && combat.Target != null);
                            report.SetObjective(1, fired);
                            report.SetObjective(2, damaged);
                        },
                        reject: c =>
                        {
                            ShipCombat combat = TutorialFhij.Combat(c);
                            if (combat?.State == null) return null;
                            if (combat.State.WeaponsLocked) return "ENERJİ YETERSİZ — SİLAHLAR KİLİTLİ";
                            HardpointState hardpoint = FindHardpoint(combat, family);
                            if (hardpoint == null) return "SİLAH TAKILI DEĞİL";
                            return hardpoint.Overheated ? "HARDPOINT AŞIRI ISINDI" : null;
                        });
                },
                checklistTr: checklist,
                keyHint: TutorialFhij.KeyHint(action));
        }

        /// <summary>
        /// One damage-layer lesson. The prerequisite layers above the taught one
        /// are stripped by real damage in <c>Prepare</c>, and the baseline is read
        /// after that -- so the preparation can never be mistaken for the shot the
        /// player is being asked to take.
        /// </summary>
        private static TutorialStepDefinition LayerDamageStep(string stepId, string titleTr, string instructionTr,
            string summaryTr, ShipDamageLayer layer, string[] checklist, CombatAction action)
        {
            return new TutorialStepDefinition(
                Chapter, stepId, titleTr, instructionTr, summaryTr,
                () =>
                {
                    string enemyId = null;
                    float shieldBase = 0f, armorBase = 0f, hullBase = 0f;
                    return TutorialSteps.Poll(
                        complete: c =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            if (enemy == null || enemy.Combat == null) return false;
                            Durability d = enemy.Combat;
                            return layer switch
                            {
                                ShipDamageLayer.Shield => d.Shield < shieldBase - 0.001f,
                                ShipDamageLayer.Armor => d.Armor < armorBase - 0.001f,
                                _ => d.Hull < hullBase - 0.001f
                            };
                        },
                        prepare: c =>
                        {
                            EnsureCombat(c);
                            EnsureTrainedWeapon(c, FamilyFor(action));
                            string defId = layer == ShipDamageLayer.Armor ? EnemyIds.HeavyAssaultMk1 : EnemyIds.FighterMk1;
                            EnemyAgent enemy = SpawnTrainingTarget(c, defId, new Vector3(0f, 0f, 450f));
                            if (enemy == null || enemy.Combat == null) return;
                            // Strip the layers above with REAL damage -- never a written value.
                            if (layer != ShipDamageLayer.Shield && enemy.Combat.Shield > 0f)
                                enemy.ApplyDamage(enemy.Combat.Shield, DamageProfile.Neutral);
                            if (layer == ShipDamageLayer.Hull && enemy.Combat.Armor > 0f)
                                enemy.ApplyDamage(enemy.Combat.Armor, DamageProfile.Neutral);
                            enemyId = enemy.Id;
                            shieldBase = enemy.Combat.Shield;
                            armorBase = enemy.Combat.Armor;
                            hullBase = enemy.Combat.Hull;
                        },
                        objectives: (c, report) =>
                        {
                            EnemyAgent enemy = enemyId != null ? EnemyAgent.Find(enemyId) : null;
                            report.SetObjective(0, enemy != null);
                        },
                        reject: c => enemyId != null && EnemyAgent.Find(enemyId) == null ? "HEDEF KALMADI" : null);
                },
                checklistTr: checklist,
                keyHint: TutorialFhij.KeyHint(action));
        }

        // ---- readings ---------------------------------------------------------

        private static ShipCombatState State(TutorialStepContext context)
        {
            ShipCombat combat = TutorialFhij.Combat(context);
            return combat != null ? combat.State : null;
        }

        /// <summary>Ensures the ship's real combat state exists. Never equips beyond the free T1 laser <c>InitializeNewCampaign</c> already installs.</summary>
        private static void EnsureCombat(TutorialStepContext context)
        {
            ShipCombat combat = TutorialFhij.Combat(context);
            if (combat != null && combat.State == null) combat.InitializeNewCampaign();
        }

        private static HardpointState LaserHardpoint(TutorialStepContext context)
        {
            ShipCombat combat = TutorialFhij.Combat(context);
            return combat != null ? FindHardpoint(combat, WeaponFamily.Laser) : null;
        }

        /// <summary>
        /// The hardpoint carrying this family among the ship's <i>active</i> slots.
        /// Deliberately not the whole array: only the first
        /// <c>ShipCombat.ActiveHardpointCount</c> slots can fire, so a weapon parked
        /// outside that window is not a weapon the lesson can be taken with.
        /// </summary>
        private static HardpointState FindHardpoint(ShipCombat combat, WeaponFamily family)
        {
            if (combat?.Hardpoints == null) return null;
            int active = Mathf.Min(combat.ActiveHardpointCount, combat.Hardpoints.Length);
            for (int i = 0; i < active; i++)
            {
                HardpointState hardpoint = combat.Hardpoints[i];
                if (hardpoint != null && hardpoint.Weapon == family) return hardpoint;
            }
            return null;
        }

        private static int LaserTier(TutorialStepContext context)
        {
            HardpointState hardpoint = LaserHardpoint(context);
            return hardpoint != null ? Mathf.Max(1, hardpoint.Tier) : 1;
        }

        // ---- the training loadout --------------------------------------------

        /// <summary>The tier every weapon this chapter teaches is produced at. T1 is the tier a new campaign can actually reach; nothing here invents a higher one.</summary>
        private const int TrainingTier = 1;

        /// <summary>The weapon a step's own key hint asks the player to fire.</summary>
        private static WeaponFamily FamilyFor(CombatAction action) => action switch
        {
            CombatAction.FirePlasma => WeaponFamily.Plasma,
            CombatAction.FireEm => WeaponFamily.Em,
            _ => WeaponFamily.Laser
        };

        private const string WeaponCoordinationId = "defense.weapon_coordination";

        /// <summary>
        /// Puts one weapon family on the training ship by <b>producing</b> it --
        /// the same job the Silahlar page starts, with the same tier gate, the
        /// same catalog cost, the same catalog duration, the same
        /// hardpoint-availability rule and the same reject enum. Returns false
        /// when production refused, and the step then stays incomplete: a refusal
        /// is the answer, never something to work around.
        /// <para>
        /// Three prerequisites are prepared first, and only prerequisites. They
        /// are the same three the player satisfies by playing, and none of them
        /// writes a production result:
        /// </para>
        /// <list type="number">
        /// <item>the family-unlock module tier, granted through the real
        /// <c>ModuleProgressionState</c> the way chapters D and E grant theirs --
        /// and restored when the step ends;</item>
        /// <item>a free <i>active</i> hardpoint. This is the one that made steps
        /// H-04..H-07 impossible: Hull Form I carries exactly one active hardpoint
        /// (<c>CombatCatalog.ActiveHardpointSlots</c>) and the free birth Laser
        /// occupies it, so every request answered
        /// <c>NoHardpointAvailable</c> forever. The slot is freed through
        /// <see cref="WeaponLoadoutService.TryRelease"/> -- the single authority
        /// for "the loadout changes outside combat only" -- and the arrangement
        /// that was there is put back when the step ends;</item>
        /// <item>exactly the catalog cost of parts and material, no more.</item>
        /// </list>
        /// </summary>
        private static bool EnsureTrainedWeapon(TutorialStepContext context, WeaponFamily family)
        {
            ShipCombat combat = TutorialFhij.Combat(context);
            if (combat?.Hardpoints == null) return false;

            RestoreTrainingLoadoutOnCleanup(context, combat);
            if (FindHardpoint(combat, family) != null) return true;

            // A refit is a between-lessons action, taken in the safe state the
            // repair lesson (H-18) also asks for. The service refuses in combat and
            // this is the tutorial saying "we are not in one", not bypassing it.
            combat.SetEncounterOpen(false);

            GrantFamilyUnlockTier(combat, family);
            if (!EnsureFreeActiveHardpoint(combat)) return false;

            WeaponTierCost cost = WeaponCatalog.CostForTier(family, TrainingTier);
            LogisticsCargo cargo = TutorialFhij.Cargo(context);
            TutorialFhij.EnsureItem(cargo, ProcessingCatalog.BasicPartId, cost.Parts);
            TutorialFhij.EnsureItem(cargo, cost.MaterialId, cost.Material);

            if (combat.ActiveProductionJob != null) combat.CancelProduction();
            if (combat.TryStartProduction(family, TrainingTier) != WeaponProductionRejectReason.None) return false;

            // The real job, advanced by exactly its own catalog duration -- not a
            // shorter one, and not a completion written around it. AdvanceProduction
            // is the same method ShipCombat.FixedUpdate calls every frame.
            combat.AdvanceProduction(cost.Seconds);
            return FindHardpoint(combat, family) != null;
        }

        /// <summary>Raises the module tier that unlocks this family, exactly to the catalog's own unlock tier and never past it.</summary>
        private static void GrantFamilyUnlockTier(ShipCombat combat, WeaponFamily family)
        {
            ShipModules modules = combat.GetComponent<ShipModules>();
            if (modules?.Modules == null) return;

            int requiredTier = family switch
            {
                WeaponFamily.Em => CombatCatalog.EmUnlockWeaponCoordinationTier,
                WeaponFamily.Plasma => CombatCatalog.PlasmaUnlockWeaponCoordinationTier,
                _ => 0
            };
            if (modules.Modules.GetTier(WeaponCoordinationId) < requiredTier)
                modules.Modules.SetTier(WeaponCoordinationId, requiredTier);
        }

        /// <summary>
        /// Leaves one active hardpoint empty, freeing the least useful occupied one
        /// (highest index first, so the Laser in slot 0 is the last to go) through
        /// the loadout service. Returns false when the service refused -- in combat,
        /// or with a job already in flight.
        /// </summary>
        private static bool EnsureFreeActiveHardpoint(ShipCombat combat)
        {
            int active = Mathf.Min(combat.ActiveHardpointCount, combat.Hardpoints.Length);
            for (int i = 0; i < active; i++)
                if (combat.Hardpoints[i] != null && combat.Hardpoints[i].IsEmpty) return true;

            for (int i = active - 1; i >= 0; i--)
            {
                if (WeaponLoadoutService.TryRelease(combat.ActiveProductionJob, combat.Hardpoints[i], combat.InCombat)
                    == HardpointReleaseRejectReason.None) return true;
            }
            return false;
        }

        /// <summary>
        /// Records what the training scene borrowed -- the hardpoint arrangement and
        /// the weapon-coordination tier -- and gives it all back when the step ends.
        /// <para>
        /// The arrangement is put back through <c>HardpointState.RestoreFromSave</c>,
        /// the project's own "this exact recorded state again" seam, and never
        /// through <c>Equip</c>: nothing here may create a weapon, only return the
        /// one that was already there. Any job still in flight is cancelled through
        /// the real cancel path, so its reservation is released instead of stranded.
        /// </para>
        /// </summary>
        private static void RestoreTrainingLoadoutOnCleanup(TutorialStepContext context, ShipCombat combat)
        {
            var borrowed = new (WeaponFamily? weapon, int tier)[combat.Hardpoints.Length];
            for (int i = 0; i < borrowed.Length; i++)
            {
                HardpointState hardpoint = combat.Hardpoints[i];
                borrowed[i] = hardpoint != null ? (hardpoint.Weapon, hardpoint.Tier) : (null, 0);
            }

            ShipModules modules = combat.GetComponent<ShipModules>();
            int borrowedTier = modules?.Modules != null ? modules.Modules.GetTier(WeaponCoordinationId) : 0;

            context.Scope.OnCleanup(() =>
            {
                if (combat == null || combat.Hardpoints == null) return;
                if (combat.ActiveProductionJob != null) combat.CancelProduction();
                for (int i = 0; i < combat.Hardpoints.Length && i < borrowed.Length; i++)
                    combat.Hardpoints[i]?.RestoreFromSave(borrowed[i].weapon, borrowed[i].tier, 0f, false, 0f);
                if (modules?.Modules != null) modules.Modules.SetTier(WeaponCoordinationId, borrowedTier);
            });
        }

        private static EnemyAgent SpawnTrainingTarget(TutorialStepContext context, string defId, Vector3 offset)
        {
            EnemyAgent agent = TutorialFhij.SpawnEnemy(context, defId, offset);
            TutorialFhij.ReleaseOnCleanup(context, agent);
            return agent;
        }

        private static bool PlayerProjectileExists(TutorialStepContext context, ProjectileKind kind)
        {
            CombatEncounterDirector director = TutorialFhij.Director(context);
            if (director == null) return false;
            IReadOnlyList<ProjectileObject> all = director.Projectiles;
            for (int i = 0; i < all.Count; i++)
            {
                ProjectileObject p = all[i];
                if (p == null || p.State == null) continue;
                if (p.State.Kind == kind && p.State.Faction == ProjectileFaction.Player) return true;
            }
            return false;
        }

        /// <summary>The planet's focus, only when it still names a live enemy -- a stale focus is not a lesson learned.</summary>
        private static string FocusedLiveEnemyId(TutorialStepContext context)
        {
            PlanetaryDefenseCoordinator coordinator = TutorialFhij.Defense(context);
            if (coordinator == null) return null;
            foreach ((string _, DefenseMode _, string focusEnemyId) in coordinator.Snapshot())
            {
                if (string.IsNullOrEmpty(focusEnemyId)) continue;
                EnemyAgent agent = EnemyAgent.Find(focusEnemyId);
                if (agent != null && !agent.Destroyed) return focusEnemyId;
            }
            return null;
        }

        /// <summary>Mirrors <c>ShipCombat.HasLineOfSight</c>: only a real celestial body blocks a shot.</summary>
        private static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            if (Physics.Raycast(from, delta.normalized, out RaycastHit hit, distance))
                return hit.collider.GetComponentInParent<Celestial.CelestialBody>() == null;
            return true;
        }
    }
}
